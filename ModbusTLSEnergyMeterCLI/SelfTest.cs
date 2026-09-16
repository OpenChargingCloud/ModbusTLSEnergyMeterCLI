/*
 * Copyright (c) 2014-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of the Modbus/TLS Energy Meter <https://github.com/OpenChargingCloud/ModbusTLSEnergyMeter>
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using System.Globalization;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.Modbus;
using org.GraphDefined.Vanaheimr.Hermod.SunSpecModbusTLS.Common;

using HermodModbusTCPClient = org.GraphDefined.Vanaheimr.Hermod.Modbus.ModbusTCPClient;
using HermodIPAddress       = org.GraphDefined.Vanaheimr.Hermod.IPAddress;
using NetIPAddress          = System.Net.IPAddress;

#endregion

namespace cloud.charging.open.EnergyMeters.ModbusTLS.CLI
{

    /// <summary>
    /// Speak Modbus/TLS to a meter that is already running, and print what it says.
    /// </summary>
    /// <remarks>
    /// This is the shortest answer to "is the thing I just started on that Linux
    /// box actually working?": it walks the whole path a real client walks - TLS,
    /// the client certificate, the role in it, the authorization policy and the
    /// register map - and fails loudly wherever that path is broken.
    /// </remarks>
    public static class SelfTest
    {

        #region RunAsync(Address, Port, Role, PKIDirectory, PfxPassword, Verbose)

        /// <summary>
        /// Read one meter and print it. Returns a process exit code.
        /// </summary>
        /// <param name="Address">Where the meter listens.</param>
        /// <param name="Port">On which port.</param>
        /// <param name="Role">Which of the client certificates to present.</param>
        /// <param name="PKIDirectory">Where those certificates live.</param>
        /// <param name="PfxPassword">The password of the PKCS#12 files there.</param>
        /// <param name="Verbose">Whether a failure should bring its stack trace.</param>
        public static async Task<Int32> RunAsync(NetIPAddress  Address,
                                                 Int32         Port,
                                                 String        Role,
                                                 String        PKIDirectory,
                                                 String        PfxPassword,
                                                 Boolean       Verbose)
        {

            #region The certificates this test presents and expects

            var clientPfx   = Path.Combine(PKIDirectory, $"client-{Role}.pfx");
            var meterCert   = Path.Combine(PKIDirectory, "server.crt");

            if (!File.Exists(clientPfx))
            {

                Console.Error.WriteLine($"There is no client certificate for the role '{Role}' in {PKIDirectory}!");
                Console.Error.WriteLine();
                Console.Error.WriteLine("The roles a meter of this kind knows are:");

                foreach (var knownRole in SunSpecRoles.AllMandatory)
                    Console.Error.WriteLine($"  {knownRole}");

                return 2;

            }

            if (!File.Exists(meterCert))
            {
                Console.Error.WriteLine($"The meter certificate '{meterCert}' does not exist, so there is nothing to pin against!");
                return 2;
            }

            X509Certificate2[]  clientChain;
            X509Certificate2    expectedMeterCertificate;

            try
            {
                clientChain               = LoadPKCS12Chain(clientPfx, PfxPassword, "Issuing Clients CA");
                expectedMeterCertificate  = X509CertificateLoader.LoadCertificateFromFile(meterCert);
            }
            catch (Exception e)
            {

                Console.Error.WriteLine($"The certificates could not be read: {e.Message}");

                if (Verbose)
                    Console.Error.WriteLine(e);

                return 1;

            }

            #endregion

            Console.WriteLine();
            Console.WriteLine($"  reading        {Address}:{Port}");
            Console.WriteLine($"  as             {Role}");
            Console.WriteLine($"  client cert    {clientChain[0].Subject}");
            Console.WriteLine();

            using var client = new HermodModbusTCPClient(

                                   HermodIPAddress.FromDotNet(Address),
                                   IPPort.Parse(Port),

                                   UnitAddress:                 1,

                                   // Hermod's Modbus client counts registers the
                                   // way the specification writes them down, from
                                   // one, and puts address-1 on the wire. SunSpec
                                   // addresses are absolute, so this puts it back.
                                   StartingAddressOffset:       1,

                                   RemoteCertificateValidator:  (sender,
                                                                 certificate,
                                                                 chain,
                                                                 modbusClient,
                                                                 policyErrors) => PinnedTo(certificate, expectedMeterCertificate),

                                   ClientCert:                  clientChain[0],
                                   ClientCertificateChain:      clientChain,
                                   TLSProtocol:                 SslProtocols.Tls12 | SslProtocols.Tls13,
                                   PreferIPv4:                  true,
                                   RequestTimeout:              TimeSpan.FromSeconds(10),
                                   MaxNumberOfRetries:          1

                               );

            try
            {

                var connected = await client.ReconnectAsync();

                if (!connected.IsSuccess)
                {

                    Console.Error.WriteLine("Could not connect: " +
                                            String.Join(", ", connected.Errors.Select(error => error.ToString())));
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("A meter that is running but refuses the handshake usually means the");
                    Console.Error.WriteLine("certificates on both sides come from different PKIs - check --pki.");

                    return 1;

                }

                // A role the meter does not know - the NO-ROLE certificate above
                // all - must be refused, so for those the refusal is the pass.
                var mayRead = SunSpecRoles.IsMandatoryRole(Role);

                UInt16[] registers;

                try
                {

                    var response = await client.ReadHoldingRegisters(
                                             SunSpecMeterMap.BaseAddress,
                                             SunSpecMeterMap.RegisterCount
                                         );

                    registers = response.HoldingRegisters.ToArray();

                }
                catch (ArgumentException e) when (e.ParamName == "PDU")
                {
                    // A Modbus exception response is two bytes where this client
                    // expects a block of registers, and it gives up on the length
                    // before it ever looks at the function code. So this, and not
                    // a tidy result object, is how a refusal arrives here.
                    return Refused(Role, mayRead);
                }

                if (!mayRead)
                {
                    Console.Error.WriteLine($"The meter let '{Role}' read, although it is not one of the roles a meter must accept!");
                    Console.Error.WriteLine("That is a hole in the authorization policy, not a passing test.");
                    return 1;
                }

                if (registers.Length != SunSpecMeterMap.RegisterCount)
                {
                    Console.Error.WriteLine($"Asked for {SunSpecMeterMap.RegisterCount} registers, got {registers.Length}.");
                    return 1;
                }

                if (registers[SunSpecMeterMap.OffMarker]     != (UInt16) (SunSpecMeterMap.SunSpecMarker >> 16) ||
                    registers[SunSpecMeterMap.OffMarker + 1] != (UInt16) (SunSpecMeterMap.SunSpecMarker & 0xFFFF))
                {
                    Console.Error.WriteLine("The first two registers are not the SunSpec marker - this is not a SunSpec device.");
                    return 1;
                }

                Print(registers);

                await client.Close();

                return 0;

            }
            catch (Exception e)
            {

                Console.Error.WriteLine($"The meter could not be read: {e.Message}");

                if (Verbose)
                    Console.Error.WriteLine(e);

                return 1;

            }

        }

        #endregion


        #region (private static) Refused(Role, MayRead)

        /// <summary>
        /// The meter said no. Whether that is right depends on who asked.
        /// </summary>
        private static Int32 Refused(String   Role,
                                     Boolean  MayRead)
        {

            if (MayRead)
            {

                Console.Error.WriteLine($"The meter refused to be read by '{Role}', although that role may read everything.");
                Console.Error.WriteLine("Its own log says why - look there for a line beginning with 'RBAC DENY'.");

                return 1;

            }

            Console.WriteLine("  refused        the meter answered with a Modbus exception, which is exactly what");
            Console.WriteLine("                 must happen for a certificate carrying no role it knows.");

            return 0;

        }

        #endregion

        #region (private static) Print(Registers)

        /// <summary>
        /// Turn the register block into the numbers it stands for.
        /// </summary>
        private static void Print(UInt16[] Registers)
        {

            var currentSF  = (Int16) Registers[SunSpecMeterMap.OffMeterA_SF];
            var voltageSF  = (Int16) Registers[SunSpecMeterMap.OffMeterV_SF];
            var frequencySF= (Int16) Registers[SunSpecMeterMap.OffMeterHz_SF];
            var powerSF    = (Int16) Registers[SunSpecMeterMap.OffMeterW_SF];
            var energySF   = (Int16) Registers[SunSpecMeterMap.OffMeterWh_SF];

            Console.WriteLine($"  manufacturer   {Text(Registers, SunSpecMeterMap.OffCommonMn,  16)}");
            Console.WriteLine($"  model          {Text(Registers, SunSpecMeterMap.OffCommonMd,  16)}");
            Console.WriteLine($"  options        {Text(Registers, SunSpecMeterMap.OffCommonOpt,  8)}");
            Console.WriteLine($"  version        {Text(Registers, SunSpecMeterMap.OffCommonVr,   8)}");
            Console.WriteLine($"  serial         {Text(Registers, SunSpecMeterMap.OffCommonSN,  16)}");
            Console.WriteLine($"  unit address   {Registers[SunSpecMeterMap.OffCommonDA]}");
            Console.WriteLine($"  model          {Registers[SunSpecMeterMap.OffMeterId]} (length {Registers[SunSpecMeterMap.OffMeterLen]})");
            Console.WriteLine();

            Console.WriteLine("                 voltage      current         power");

            foreach (var (name, voltage, current, power) in new[] {
                         ("L1",     SunSpecMeterMap.OffMeterPhVphA, SunSpecMeterMap.OffMeterAphA, SunSpecMeterMap.OffMeterWphA),
                         ("L2",     SunSpecMeterMap.OffMeterPhVphB, SunSpecMeterMap.OffMeterAphB, SunSpecMeterMap.OffMeterWphB),
                         ("L3",     SunSpecMeterMap.OffMeterPhVphC, SunSpecMeterMap.OffMeterAphC, SunSpecMeterMap.OffMeterWphC),
                         ("total",  SunSpecMeterMap.OffMeterPhV,    SunSpecMeterMap.OffMeterA,    SunSpecMeterMap.OffMeterW)
                     })
            {
                Console.WriteLine($"  {name,-12} " +
                                  $"{Scaled((Int16) Registers[voltage], voltageSF, "V"),11} " +
                                  $"{Scaled((Int16) Registers[current], currentSF, "A"),12} " +
                                  $"{Scaled((Int16) Registers[power],   powerSF,   "W"),13}");
            }

            Console.WriteLine();
            Console.WriteLine($"  frequency      {Scaled((Int16) Registers[SunSpecMeterMap.OffMeterHz], frequencySF, "Hz")}");
            Console.WriteLine($"  exported       {Scaled(UInt32At(Registers, SunSpecMeterMap.OffMeterTotWhExp), energySF, "Wh")}");
            Console.WriteLine($"  imported       {Scaled(UInt32At(Registers, SunSpecMeterMap.OffMeterTotWhImp), energySF, "Wh")}");
            Console.WriteLine();
            Console.WriteLine($"  meter mode     {Registers[SunSpecMeterMap.OffMeterMeterMode]}   " +
                              $"(register {SunSpecMeterMap.Addr(SunSpecMeterMap.OffMeterMeterMode)}, writable from {SunSpecRoles.GridService} up)");
            Console.WriteLine();

        }

        #endregion

        #region (private static) Text   (Registers, Offset, Length)

        /// <summary>
        /// A SunSpec string: ASCII, two characters per register, padded with NUL.
        /// </summary>
        private static String Text(UInt16[]  Registers,
                                   UInt16    Offset,
                                   Int32     Length)
        {

            var bytes = new Byte[Length * 2];

            for (var i = 0; i < Length; i++)
            {
                bytes[2 * i]     = (Byte) (Registers[Offset + i] >> 8);
                bytes[2 * i + 1] = (Byte) (Registers[Offset + i] & 0xFF);
            }

            return System.Text.Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ');

        }

        #endregion

        #region (private static) UInt32At(Registers, Offset)

        private static UInt32 UInt32At(UInt16[]  Registers,
                                       UInt16    Offset)

            => ((UInt32) Registers[Offset] << 16) | Registers[Offset + 1];

        #endregion

        #region (private static) Scaled (Value, ScaleFactor, Unit)

        /// <summary>
        /// A SunSpec value and its decimal scale factor, as the number it means.
        /// </summary>
        private static String Scaled(Int64   Value,
                                     Int16   ScaleFactor,
                                     String  Unit)
        {

            var scaled    = Value * Math.Pow(10, ScaleFactor);
            var decimals  = Math.Max(0, -ScaleFactor);

            return $"{scaled.ToString($"F{decimals}", CultureInfo.InvariantCulture)} {Unit}";

        }

        #endregion

        #region (private static) LoadPKCS12Chain(Path, Password, IssuingCAName)

        /// <summary>
        /// The leaf certificate with its private key, and the CA that issued it.
        /// </summary>
        /// <remarks>
        /// SunSpec wants an mbaps peer to send its whole chain, so the client
        /// hands over both - the PKCS#12 files from the built-in PKI carry the
        /// issuing CA and the root next to the leaf.
        /// </remarks>
        private static X509Certificate2[] LoadPKCS12Chain(String  Path,
                                                          String  Password,
                                                          String  IssuingCAName)
        {

            var certificates      = X509CertificateLoader.LoadPkcs12CollectionFromFile(
                                        Path,
                                        Password,
                                        X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable
                                    ).
                                    OfType<X509Certificate2>().
                                    ToArray();

            var clientCertificate = certificates.Single(certificate =>  certificate.HasPrivateKey &&
                                                                       !IsCertificateAuthority(certificate));

            var issuingCA         = certificates.Single(certificate =>  IsCertificateAuthority(certificate) &&
                                                                        certificate.Subject.Contains(IssuingCAName, StringComparison.Ordinal));

            return [
                clientCertificate,
                issuingCA
            ];

        }

        #endregion

        #region (private static) IsCertificateAuthority(Certificate)

        private static Boolean IsCertificateAuthority(X509Certificate2 Certificate)

            => Certificate.Extensions.
                   OfType<X509BasicConstraintsExtension>().
                   Any(extension => extension.CertificateAuthority);

        #endregion

        #region (private static) PinnedTo(Certificate, Expected)

        /// <summary>
        /// The meter is trusted because it is exactly the one whose certificate
        /// lies next to ours, not because some CA vouches for it.
        /// </summary>
        private static TLSValidationResult PinnedTo(X509Certificate2?  Certificate,
                                                    X509Certificate2   Expected)
        {

            if (Certificate is null)
                return TLSValidationResult.Failed("The meter sent no certificate at all!");

            return String.Equals(Certificate.Thumbprint,
                                 Expected.Thumbprint,
                                 StringComparison.OrdinalIgnoreCase)

                       ? TLSValidationResult.Success()
                       : TLSValidationResult.Failed($"The meter sent '{Certificate.Subject}', which is not the certificate in this PKI directory!");

        }

        #endregion

    }

}
