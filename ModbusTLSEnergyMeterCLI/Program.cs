/*
 * Copyright (c) 2014-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of ModbusTLSEnergyMeterCLI <https://github.com/OpenChargingCloud/ModbusTLSEnergyMeterCLI>
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

using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

using org.GraphDefined.Vanaheimr.Hermod.SunSpecModbusTLS.Common;
using org.GraphDefined.Vanaheimr.Hermod.SunSpecModbusTLS.PKI;

using cloud.charging.open.protocols.WWCP.Node;
using cloud.charging.open.protocols.WWCP.Node.Logging;
using cloud.charging.open.protocols.WWCP.Node.Configuration;

using cloud.charging.open.EnergyMeters.ModbusTLS;
using cloud.charging.open.EnergyMeters.ModbusTLS.CommandLine;

using IIPAddress  = org.GraphDefined.Vanaheimr.Hermod.IIPAddress;
using IPv4Address = org.GraphDefined.Vanaheimr.Hermod.IPv4Address;
using IPvXAddress = org.GraphDefined.Vanaheimr.Hermod.IPvXAddress;
using IPPort      = org.GraphDefined.Vanaheimr.Hermod.IPPort;

#endregion

namespace cloud.charging.open.EnergyMeters.ModbusTLS.CLI
{

    /// <summary>
    /// One simulated SunSpec energy meter, speaking Modbus/TLS, with a prompt,
    /// until 'quit', Ctrl+C or SIGTERM.
    /// </summary>
    public class Program
    {

        #region Data

        /// <summary>
        /// The port IANA registered for Modbus/TLS ("mbaps").
        /// </summary>
        public const  Int32   DefaultPort            = 802;

        /// <summary>
        /// The password of every PKCS#12 file that the built-in PKI writes.
        /// </summary>
        /// <remarks>
        /// These certificates are for a simulator. The password is in the source
        /// of the generator, it is in this file, and it is printed at every
        /// start - it protects nothing and is not meant to.
        /// </remarks>
        public const  String  DemoPfxPassword        = "demo";

        /// <summary>
        /// The common name of the meter certificate, unless asked otherwise.
        /// </summary>
        public const  String  DefaultDeviceName      = "EnergyMeter01";

        private const String  ServerPfxFileName      = "server.pfx";
        private const String  ClientsCACertFileName  = "issuing-clients-ca.crt";

        #endregion


        #region (private static) TryTakeValue  (Arguments, ref Index, out Value)

        private static Boolean TryTakeValue(String[]     Arguments,
                                            ref Int32    Index,
                                            out String?  Value)
        {

            if (Index + 1 < Arguments.Length && !Arguments[Index + 1].StartsWith("--"))
            {
                Value = Arguments[++Index];
                return true;
            }

            Value = null;
            return false;

        }

        #endregion

        #region (private static) RepositoryRoot()

        /// <summary>
        /// The directory holding ModbusTLSEnergyMeter.slnx, looked up from the
        /// binary and from the current directory; the current directory when
        /// neither leads to it.
        /// </summary>
        /// <remarks>
        /// The PKI defaults to a place below it, so that the certificates do
        /// not end up in bin/ - where the next "dotnet clean" would take the
        /// meter's identity with it.
        /// </remarks>
        private static String RepositoryRoot()
        {

            foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            {

                var directory = new DirectoryInfo(start);

                while (directory is not null)
                {

                    if (File.Exists(Path.Combine(directory.FullName, "ModbusTLSEnergyMeter.slnx")))
                        return directory.FullName;

                    directory = directory.Parent;

                }

            }

            return Environment.CurrentDirectory;

        }

        #endregion

        #region (private static) PrintUsage()

        private static void PrintUsage()
        {

            Console.WriteLine("Usage: ModbusTLSEnergyMeter [--port <number>] [--any | --listen <address>]");
            Console.WriteLine("                            [--pki <directory>] [--regenerate-pki]");
            Console.WriteLine("                            [--name <text>] [--hostname <name>] [--ip <address>]");
            Console.WriteLine("                            [--server-pfx <file>] [--pfx-password <text>]");
            Console.WriteLine("                            [--client-ca <file>] [--serial <text>]");
            Console.WriteLine("                            [--meter-mode <net|import|export>] [--sim-day <minutes>]");
            Console.WriteLine("                            [--idle-timeout <seconds>] [--write-timeout <seconds>]");
            Console.WriteLine("                            [--http-port <number>] [--https] [--config <file>] [--data <directory>]");
            Console.WriteLine("                            [--log-days <number>]");
            Console.WriteLine("                            [--selftest [<role>]] [--verify-log]");
            Console.WriteLine("                            [--verbose | --quiet]");
            Console.WriteLine();
            Console.WriteLine("Network:");
            Console.WriteLine($"  --port <number>      TCP port to listen on (default: {DefaultPort}, the registered mbaps port)");
            Console.WriteLine("  --any                listen on all addresses instead of 127.0.0.1");
            Console.WriteLine("  --listen <address>   listen on one specific address");
            Console.WriteLine("  --idle-timeout <s>   drop a connection that asks nothing for this long (default: 300)");
            Console.WriteLine("  --write-timeout <s>  drop a connection that stops reading our answers (default: 30)");
            Console.WriteLine();
            Console.WriteLine("Web interface:");
            Console.WriteLine($"  --http-port <number> where people sign in to administer this meter (default: {ModbusTLSEnergyMeter.DefaultHTTPPort})");
            Console.WriteLine("                       --any applies to this listener as well");
            Console.WriteLine("  --https              serve the web interface over TLS. Its certificate is its own,");
            Console.WriteLine("                       kept apart from the Modbus/TLS one: what a charging station");
            Console.WriteLine("                       checks and what a browser checks come from different places");
            Console.WriteLine("                       and are never the same file. At the first start the meter");
            Console.WriteLine("                       signs one for itself, so that the page can be reached at all;");
            Console.WriteLine("                       a browser will say it does not know who signed it, and it is");
            Console.WriteLine("                       right. Ask for a proper one on the Web certificate page.");
            Console.WriteLine($"  --config <file>      where the name servers and the time servers live (default:");
            Console.WriteLine($"                       {WWCPConfigFile.DefaultFileName} below the repository root)");
            Console.WriteLine("  --data <directory>   where the accounts and the log live (default: data/ below the");
            Console.WriteLine($"                       repository root). At the first start an administrator, '{ModbusTLSEnergyMeter.DefaultAdminUser}',");
            Console.WriteLine("                       is made there and its password is shown once.");
            Console.WriteLine($"  --log-days <number>  how many days of the log files are kept (default: {ModbusTLSEnergyMeter.DefaultLogKeepDays}).");
            Console.WriteLine("                       The log book beside them - the entries that are evidence: the");
            Console.WriteLine("                       clock, the certificates, every write and every refusal - is");
            Console.WriteLine("                       signed, chained and kept whole. 0 writes neither, and keeps");
            Console.WriteLine("                       the log in memory only.");
            Console.WriteLine();
            Console.WriteLine("Certificates:");
            Console.WriteLine("  --pki <directory>    where the certificates live (default: pki/ below the repository");
            Console.WriteLine("                       root). A whole PKI - root CA, two issuing CAs, a meter");
            Console.WriteLine("                       certificate and one client certificate per SunSpec role - is");
            Console.WriteLine("                       built there at the first start.");
            Console.WriteLine("  --regenerate-pki     build the PKI again, overwriting what is there. Every client");
            Console.WriteLine("                       certificate handed out so far stops working.");
            Console.WriteLine($"  --name <text>        common name of the meter certificate (default: {DefaultDeviceName})");
            Console.WriteLine("  --hostname <name>    another DNS name for the meter certificate, repeatable. Give the");
            Console.WriteLine("                       name that clients dial when the meter is not on their machine.");
            Console.WriteLine("  --ip <address>       another IP address for the meter certificate, repeatable");
            Console.WriteLine("  --server-pfx <file>  use this PKCS#12 file instead of a generated one");
            Console.WriteLine($"  --pfx-password <t>   its password (default: {DemoPfxPassword})");
            Console.WriteLine("  --client-ca <file>   the CA that client certificates must chain to");
            Console.WriteLine();
            Console.WriteLine("Meter:");
            Console.WriteLine("  --serial <text>      serial number in SunSpec Common Model 1 (default: the --name)");
            Console.WriteLine("  --meter-mode <mode>  where this meter sits, and therefore which way energy flows");
            Console.WriteLine("                       through it (default: net). Writable afterwards through");
            Console.WriteLine($"                       register {SunSpecMeterMap.Addr(SunSpecMeterMap.OffMeterMeterMode)} and on the web page:");
            Console.WriteLine("                         net     at the grid connection point: the simulated site");
            Console.WriteLine("                                 draws at night and feeds back around noon, so the");
            Console.WriteLine("                                 power is signed and both counters move");
            Console.WriteLine("                         import  in front of a load, which is what a charging");
            Console.WriteLine("                                 station's meter is: only imported energy");
            Console.WriteLine("                         export  in front of a generator: only exported energy, and");
            Console.WriteLine("                                 zero at night, because the sun is down");
            Console.WriteLine("  --sim-day <minutes>  how much real time one simulated day takes (default: 1440,");
            Console.WriteLine("                       a real day). Compressing it runs the load and the sun");
            Console.WriteLine("                       faster so that a whole day can be watched over a coffee;");
            Console.WriteLine("                       the energy counters still count real seconds.");
            Console.WriteLine();
            Console.WriteLine("Checking:");
            Console.WriteLine("  --verify-log         do not serve: walk the log book on disk, check every line");
            Console.WriteLine("                       against the chain and the signature, and say what it found");
            Console.WriteLine("  --selftest [<role>]  do not serve: connect to a meter that is already running, read");
            Console.WriteLine($"                       its registers and print them. Role defaults to {SunSpecRoles.ReadOnly}.");
            Console.WriteLine();
            Console.WriteLine("Log:");
            Console.WriteLine("  -v, --verbose        write every entry, down to the debug ones");
            Console.WriteLine("  -q, --quiet          write only warnings and worse");
            Console.WriteLine();
            Console.WriteLine($"On Linux port {DefaultPort} is privileged: either start this as root, or allow the");
            Console.WriteLine("binary to bind low ports once with setcap, or pick a port above 1024 with --port.");
            Console.WriteLine();
            Console.WriteLine("Once it is up, the console is a prompt: 'help' lists what can be typed there,");
            Console.WriteLine("Tab completes it, and 'quit' or Ctrl+C stops the meter. Started where there is");
            Console.WriteLine("no terminal - from a script, under a service manager, in CI, or with the output");
            Console.WriteLine("going into a file - there is no prompt and it simply runs.");

        }

        #endregion


        public static async Task<Int32> Main(String[] Arguments)
        {

            Console.OutputEncoding = System.Text.Encoding.UTF8;

            #region Arguments

            var      port           = DefaultPort;
            var      anyAddress     = false;
            String?  listenAddress  = null;
            String?  pkiDirectory   = null;
            var      regeneratePKI  = false;
            String?  deviceName     = null;
            var      extraDNSNames  = new List<String>();
            var      extraIPs       = new List<IPAddress>();
            String?  serverPfxPath  = null;
            String?  pfxPassword    = null;
            String?  clientCAPath   = null;
            String?  serialNumber   = null;
            var      meterMode      = SunSpecMeterMode.Net;
            TimeSpan? simulatedDay  = null;
            var      idleTimeout    = TimeSpan.FromSeconds(300);
            var      writeTimeout   = TimeSpan.FromSeconds(30);
            IPPort?  httpPort       = null;
            var      https          = false;
            String?  configFilePath = null;
            String?  dataPath       = null;
            Int32?   logKeepDays    = null;
            var      verifyLog      = false;
            var      selfTest       = false;
            var      selfTestRole   = SunSpecRoles.ReadOnly;
            var      verbose        = false;
            var      quiet          = false;

            for (var i = 0; i < Arguments.Length; i++)
            {
                switch (Arguments[i])
                {

                    case "--port":
                        if (i + 1 < Arguments.Length && UInt16.TryParse(Arguments[i + 1], out var parsedPort) && parsedPort > 0)
                        {
                            port = parsedPort;
                            i++;
                        }
                        else
                        {
                            Console.Error.WriteLine("Missing or invalid port number after --port!");
                            return 2;
                        }
                        break;

                    case "--any":
                        anyAddress = true;
                        break;

                    case "--listen":
                        if (!TryTakeValue(Arguments, ref i, out listenAddress))
                        {
                            Console.Error.WriteLine("Missing address after --listen!");
                            return 2;
                        }
                        break;

                    case "--idle-timeout":
                        if (i + 1 < Arguments.Length && UInt32.TryParse(Arguments[i + 1], out var parsedIdle) && parsedIdle > 0)
                        {
                            idleTimeout = TimeSpan.FromSeconds(parsedIdle);
                            i++;
                        }
                        else
                        {
                            Console.Error.WriteLine("Missing or invalid number of seconds after --idle-timeout!");
                            return 2;
                        }
                        break;

                    case "--write-timeout":
                        if (i + 1 < Arguments.Length && UInt32.TryParse(Arguments[i + 1], out var parsedWrite) && parsedWrite > 0)
                        {
                            writeTimeout = TimeSpan.FromSeconds(parsedWrite);
                            i++;
                        }
                        else
                        {
                            Console.Error.WriteLine("Missing or invalid number of seconds after --write-timeout!");
                            return 2;
                        }
                        break;

                    case "--http-port":
                        if (i + 1 < Arguments.Length && UInt16.TryParse(Arguments[i + 1], out var parsedHTTPPort) && parsedHTTPPort > 0)
                        {
                            httpPort = IPPort.Parse(parsedHTTPPort);
                            i++;
                        }
                        else
                        {
                            Console.Error.WriteLine("Missing or invalid port number after --http-port!");
                            return 2;
                        }
                        break;

                    case "--config":
                        if (!TryTakeValue(Arguments, ref i, out configFilePath))
                        {
                            Console.Error.WriteLine("Missing file after --config!");
                            return 2;
                        }
                        break;

                    case "--log-days":
                        if (i + 1 < Arguments.Length && Int32.TryParse(Arguments[i + 1], out var parsedDays) && parsedDays >= 0)
                        {
                            logKeepDays = parsedDays;
                            i++;
                        }
                        else
                        {
                            Console.Error.WriteLine("Missing or invalid number of days after --log-days!");
                            return 2;
                        }
                        break;

                    case "--data":
                        if (!TryTakeValue(Arguments, ref i, out dataPath))
                        {
                            Console.Error.WriteLine("Missing directory after --data!");
                            return 2;
                        }
                        break;

                    case "--pki":
                        if (!TryTakeValue(Arguments, ref i, out pkiDirectory))
                        {
                            Console.Error.WriteLine("Missing directory after --pki!");
                            return 2;
                        }
                        break;

                    case "--regenerate-pki":
                        regeneratePKI = true;
                        break;

                    case "--name":
                        if (!TryTakeValue(Arguments, ref i, out deviceName))
                        {
                            Console.Error.WriteLine("Missing text after --name!");
                            return 2;
                        }
                        break;

                    case "--hostname":
                        if (!TryTakeValue(Arguments, ref i, out var dnsName))
                        {
                            Console.Error.WriteLine("Missing name after --hostname!");
                            return 2;
                        }
                        extraDNSNames.Add(dnsName!);
                        break;

                    case "--ip":
                        if (!TryTakeValue(Arguments, ref i, out var ipText) ||
                            !IPAddress.TryParse(ipText, out var extraIP))
                        {
                            Console.Error.WriteLine("Missing or invalid address after --ip!");
                            return 2;
                        }
                        extraIPs.Add(extraIP);
                        break;

                    case "--server-pfx":
                        if (!TryTakeValue(Arguments, ref i, out serverPfxPath))
                        {
                            Console.Error.WriteLine("Missing file after --server-pfx!");
                            return 2;
                        }
                        break;

                    case "--pfx-password":
                        if (!TryTakeValue(Arguments, ref i, out pfxPassword))
                        {
                            Console.Error.WriteLine("Missing text after --pfx-password!");
                            return 2;
                        }
                        break;

                    case "--client-ca":
                        if (!TryTakeValue(Arguments, ref i, out clientCAPath))
                        {
                            Console.Error.WriteLine("Missing file after --client-ca!");
                            return 2;
                        }
                        break;

                    case "--serial":
                        if (!TryTakeValue(Arguments, ref i, out serialNumber))
                        {
                            Console.Error.WriteLine("Missing text after --serial!");
                            return 2;
                        }
                        break;

                    case "--meter-mode":
                        if (!TryTakeValue(Arguments, ref i, out var modeText) ||
                            !SunSpecMeterModeExtensions.TryParse(modeText, out meterMode))
                        {
                            Console.Error.WriteLine("Missing or unknown mode after --meter-mode! Expected net, import or export.");
                            return 2;
                        }
                        break;

                    case "--sim-day":
                        if (i + 1 < Arguments.Length && UInt32.TryParse(Arguments[i + 1], out var parsedDay) && parsedDay > 0)
                        {
                            simulatedDay = TimeSpan.FromMinutes(parsedDay);
                            i++;
                        }
                        else
                        {
                            Console.Error.WriteLine("Missing or invalid number of minutes after --sim-day!");
                            return 2;
                        }
                        break;

                    case "--https":
                        https = true;
                        break;

                    case "--verify-log":
                        verifyLog = true;
                        break;

                    case "--selftest":
                        selfTest = true;
                        if (TryTakeValue(Arguments, ref i, out var role))
                            selfTestRole = role!;
                        break;

                    case "-v":
                    case "--verbose":
                        verbose = true;
                        break;

                    case "-q":
                    case "--quiet":
                        quiet = true;
                        break;

                    case "-h":
                    case "--help":
                        PrintUsage();
                        return 0;

                    default:
                        Console.Error.WriteLine($"Unknown argument '{Arguments[i]}'!");
                        PrintUsage();
                        return 2;

                }
            }

            if (verbose && quiet)
            {
                Console.Error.WriteLine("--verbose and --quiet ask for opposite things!");
                return 2;
            }

            if (anyAddress && listenAddress is not null)
            {
                Console.Error.WriteLine("--any and --listen ask for opposite things!");
                return 2;
            }

            IPAddress address;

            if (anyAddress)
                address = IPAddress.Any;

            else if (listenAddress is not null)
            {

                if (!IPAddress.TryParse(listenAddress, out var parsedAddress))
                {
                    Console.Error.WriteLine($"'{listenAddress}' is not an IP address!");
                    return 2;
                }

                address = parsedAddress;

            }

            else
                address = IPAddress.Loopback;

            deviceName    = String.IsNullOrWhiteSpace(deviceName)   ? DefaultDeviceName : deviceName.  Trim();
            serialNumber  = String.IsNullOrWhiteSpace(serialNumber) ? deviceName        : serialNumber.Trim();
            pfxPassword ??= DemoPfxPassword;

            #endregion

            #region The PKI, built at the first start

            var pkiDir     = Path.GetFullPath(pkiDirectory ?? Path.Combine(RepositoryRoot(), "pki"));
            var serverPfx  = serverPfxPath ?? Path.Combine(pkiDir, ServerPfxFileName);
            var clientsCA  = clientCAPath  ?? Path.Combine(pkiDir, ClientsCACertFileName);

            // Certificates from elsewhere are the operator's business: we
            // neither build over them nor claim they came from here.
            var ownPKI     = serverPfxPath is null && clientCAPath is null;

            if (ownPKI && (regeneratePKI || !File.Exists(serverPfx) || !File.Exists(clientsCA)))
            {
                try
                {

                    Console.WriteLine(regeneratePKI
                                          ? $"Building the PKI again in {pkiDir} ..."
                                          : $"No certificates in {pkiDir} yet, building a PKI ...");
                    Console.WriteLine();

                    await new ModbusPKI().BuildPKI(
                              pkiDir,
                              deviceName,
                              extraDNSNames,
                              extraIPs
                          );

                    Console.WriteLine();

                }
                catch (Exception e)
                {

                    Console.Error.WriteLine($"The PKI could not be built: {e.Message}");

                    if (verbose)
                        Console.Error.WriteLine(e);

                    return 1;

                }
            }

            else if (extraDNSNames.Count > 0 || extraIPs.Count > 0)
                Console.WriteLine("Note: --hostname and --ip only reach a certificate while it is being built, " +
                                  "and these already exist. Add --regenerate-pki to build them again.");

            foreach (var (what, path) in new[] { ("meter certificate", serverPfx),
                                                 ("client CA",         clientsCA) })
            {
                if (!File.Exists(path))
                {
                    Console.Error.WriteLine($"The {what} '{path}' does not exist!");
                    return 1;
                }
            }

            #endregion

            #region --verify-log: check the log instead of serving

            if (verifyLog)
                return VerifyLog(dataPath ?? Path.Combine(RepositoryRoot(), "data"));

            #endregion

            #region --selftest: be a client instead of a meter

            if (selfTest)
                return await SelfTest.RunAsync(
                                 address.Equals(IPAddress.Any) ? IPAddress.Loopback : address,
                                 port,
                                 selfTestRole,
                                 pkiDir,
                                 pfxPassword,
                                 verbose
                             );

            #endregion

            #region The meter and its Modbus/TLS frontend

            ModbusTLSEnergyMeter energyMeter;

            try
            {
                energyMeter = new ModbusTLSEnergyMeter(
                                  SerialNumber:       serialNumber,
                                  ServerPfxPath:      serverPfx,
                                  ServerPfxPassword:  pfxPassword,
                                  ClientCACertPath:   clientsCA,
                                  ListenAddress:      address,
                                  ListenPort:         port,
                                  IdleTimeout:        idleTimeout,
                                  WriteTimeout:       writeTimeout,
                                  MeterMode:          meterMode,
                                  SimulatedDayLength: simulatedDay,

                                  HTTPHostname:       anyAddress
                                                          ? IPvXAddress.Any
                                                          : (IIPAddress) IPv4Address.Localhost,
                                  HTTPPort:           httpPort,
                                  HTTPS:              https,
                                  DataPath:           dataPath       ?? Path.Combine(RepositoryRoot(), "data"),
                                  LogKeepDays:        logKeepDays,
                                  ConfigFile:         new WWCPConfigFile(
                                                          configFilePath ?? Path.Combine(RepositoryRoot(), WWCPConfigFile.DefaultFileName)
                                                      ),

                                  ConsoleLogLevel:    verbose ? LogLevel.Debug
                                                          : quiet ? LogLevel.Warning
                                                                  : LogLevel.Info
                              );
            }
            catch (Exception e)
            {

                Console.Error.WriteLine($"The meter could not be set up: {e.Message}");

                if (verbose)
                    Console.Error.WriteLine(e);

                return 1;

            }

            await using (energyMeter)
            {

                try
                {
                    await energyMeter.Start();
                }
                catch (PortUnavailableException problem)
                {
                    return ReportListenFailure(problem, verbose);
                }
                catch (Exception e)
                {

                    Console.Error.WriteLine($"The meter could not start: {e.Message}");

                    if (verbose)
                        Console.Error.WriteLine(e);

                    return 1;

                }

                PrintBanner(energyMeter, pkiDir, pfxPassword, ownPKI);

                #region The command line, until 'quit', Ctrl+C or SIGTERM

                // Whether anybody can type here at all. Started from a script,
                // from a service manager or in CI, this process has no terminal
                // on its input and Console.ReadKey throws rather than waiting -
                // and there would be nobody to type anyway. Then the meter
                // simply runs, exactly as it did before there was a command
                // line, and the web interface is how it is spoken to.
                //
                // The output counts too: the prompt is drawn by moving the
                // cursor, and with the output going into "| tee" or a file there
                // is no cursor to move. Measured on Windows with the vehicle,
                // whose prompt then looked at its input only: the prompt threw
                // while drawing itself, before a key was pressed, and the
                // program was gone within 200 ms of its banner - with exit code
                // 0, a program that said all was well.
                var canBeTypedAt = !Console.IsInputRedirected &&
                                   !Console.IsOutputRedirected;

                Console.WriteLine(canBeTypedAt
                                      ? "Type 'help' for what can be typed here, 'quit' or Ctrl+C to stop."
                                      : "Press Ctrl+C to stop. (No terminal here, so nothing to type at.)");
                Console.WriteLine();

                var stopped = new TaskCompletionSource();

                // Ctrl+C still means stop, as it always has here. The command
                // line adds a handler of its own for it, which cancels whatever
                // command is running; both fire, and that is the intended
                // reading of Ctrl+C - abandon what is running and shut the
                // meter down. 'quit' is the same thing said politely.
                Console.CancelKeyPress += (_, e) => {
                    e.Cancel = true;
                    stopped.TrySetResult();
                };

                // systemd stops a service with SIGTERM, and a meter that is
                // killed instead of stopped leaves its clients waiting.
                using var sigterm = PosixSignalRegistration.Create(
                                        PosixSignal.SIGTERM,
                                        context => {
                                            context.Cancel = true;
                                            stopped.TrySetResult();
                                        }
                                    );

                if (canBeTypedAt)
                {

                    var cli          = new MeterCLI(energyMeter);
                    var brokeAtOnce  = false;

                    while (true)
                    {

                        // From here two things write on one screen: this command
                        // line, and the meter's log from whichever thread did
                        // the thing it is reporting. So the log stops writing of
                        // its own accord and asks the command line for the screen
                        // instead - which takes the half-typed command off it,
                        // writes the entry whole, and puts the command back with
                        // the cursor where it was.
                        energyMeter.ShareConsoleWith(cli.WriteBlock);

                        // On a thread of its own, because Console.ReadKey blocks
                        // the one it is called on: awaited directly, the command
                        // line would keep this thread inside ReadKey and Ctrl+C
                        // would have nobody left to wake.
                        var since   = System.Diagnostics.Stopwatch.GetTimestamp();
                        var typing  = Task.Run(cli.Run);

                        // The Modbus/TLS frontend as well, which is what this
                        // waited for before there was a prompt: a meter whose
                        // frontend has stopped is not kept up by somebody
                        // typing.
                        await Task.WhenAny(stopped.Task, typing, energyMeter.RunTask);

                        if (!typing.IsFaulted)
                            break;

                        // A command line that broke is not somebody asking for
                        // the meter to stop. What broke the vehicle's and the
                        // station's first was a line typed wider than the
                        // window: until Styx learned to show such a line
                        // through a window onto it, it threw out of the line
                        // editor - measured in 80 columns, "Parameter 'left',
                        // actual value was 80" - and a program that took that
                        // for 'quit' shut down with exit code 0. That cause is
                        // gone; this is for the next one.
                        //
                        // The console goes back to the log first, with a lock
                        // of its own, because the command line's way of writing
                        // may be what broke: a prompt that fails while drawing
                        // itself stays registered as the line on the screen,
                        // and every entry after that fails trying to take it
                        // off again.
                        //
                        // Then a new prompt - unless the last one was already
                        // a new one and broke again the moment it started.
                        // That is a console a prompt cannot be drawn on at all,
                        // and asking a third time would only fail a third time.
                        // How fast the first one broke says nothing: a line
                        // pasted in straight after the start is still a line.
                        var padlock = new Lock();

                        energyMeter.ShareConsoleWith(write => { lock (padlock) { write(); } });

                        var atOnce = System.Diagnostics.Stopwatch.GetElapsedTime(since) < TimeSpan.FromSeconds(1);
                        var giveUp = atOnce && brokeAtOnce;

                        brokeAtOnce = atOnce;

                        // On one line, as every entry is: the message of an
                        // exception may carry line breaks of its own - the one
                        // above does, before "Actual value was 80" - and in the
                        // log file a second line has no time, no level and no
                        // tags.
                        var why = typing.Exception?.GetBaseException().Message.ReplaceLineEndings(" ");

                        energyMeter.Log.Warning(
                            $"The command line stopped working: {why} " +
                            (giveUp
                                 ? "A new one broke again as soon as it started, so there is none; the meter keeps running, and Ctrl+C stops it."
                                 : "A new one is started."),
                            "cli"
                        );

                        if (giveUp)
                        {
                            await Task.WhenAny(stopped.Task, energyMeter.RunTask);
                            break;
                        }

                    }

                }

                else
                    await Task.WhenAny(stopped.Task, energyMeter.RunTask);

                #endregion

                Console.WriteLine();
                Console.WriteLine("Stopping ...");

                try
                {
                    await energyMeter.Stop();
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"The meter stopped with an error: {e.Message}");
                    return 1;
                }

            }

            #endregion

            return 0;

        }


        #region (private static) VerifyLog(DataPath)

        /// <summary>
        /// Walk the log book on disk and say whether it still leads to where it
        /// says it does, without starting a meter.
        /// </summary>
        /// <remarks>
        /// Without starting one on purpose: the question is asked of a meter
        /// that is running somewhere else, or of a directory copied off one,
        /// and starting a second meter on the same files would append to the
        /// very log being checked.
        /// </remarks>
        private static Int32 VerifyLog(String DataPath)
        {

            var path = Path.Combine(Path.GetFullPath(DataPath), ModbusTLSEnergyMeter.LogDirectoryName);

            if (!Directory.Exists(path))
            {
                Console.Error.WriteLine($"There is no log book in '{path}'.");
                return 2;
            }

            // The files the meter writes its log book to, and none of the log
            // files beside them: those are for reading, and are not signed.
            using var store   = new SignedLog(path, ModbusTLSEnergyMeter.MeterKind.LogFilePrefix);
            var       result  = store.Verify();

            Console.WriteLine();
            Console.WriteLine($"  log book       {path}");
            Console.WriteLine($"  signing key    {result.KeyId}");
            Console.WriteLine($"  entries        {result.Entries}");
            Console.WriteLine($"  head           {result.Head}");
            Console.WriteLine();

            foreach (var file in result.Files)
                Console.WriteLine($"    {file.Name.PadRight(28)}{file.Entries,7} {(file.IsIntact ? "intact" : "BROKEN: " + file.Problem)}");

            Console.WriteLine();

            if (result.IsIntact)
            {
                Console.WriteLine("  Every line matches its own hash, follows the line before it, and is signed");
                Console.WriteLine("  by this key.");
                Console.WriteLine();
                Console.WriteLine("  That says the files were not edited by anybody who lacked the key - which");
                Console.WriteLine("  sits beside them. Compare the head above against a copy kept elsewhere to");
                Console.WriteLine("  find out whether the log was replaced wholesale.");
                return 0;
            }

            Console.Error.WriteLine($"  The log book is broken: {result.FirstProblem}");
            return 1;

        }

        #endregion

        #region (private static) ReportListenFailure(Problem, Verbose)

        /// <summary>
        /// Say which of the meter's two ports could not be had, and what to do
        /// about it.
        /// </summary>
        /// <remarks>
        /// Which one matters, because a different switch moves each: --port the
        /// Modbus/TLS frontend, --http-port the web interface. "Port in use" on
        /// its own sends somebody to move the wrong one half of the time.
        /// </remarks>
        private static Int32 ReportListenFailure(PortUnavailableException  Problem,
                                                 Boolean                   Verbose)
        {

            var port    = Problem.Port.ToUInt16();
            var option  = Problem.Whose == ModbusTLSEnergyMeter.ModbusTLSPort
                              ? "--port"
                              : "--http-port";

            Console.Error.WriteLine($"The meter could not start: {Problem.Message}.");

            if (Problem.Because == SocketError.AccessDenied &&
                port < 1024 &&
                !OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"Port {port} is privileged. Either start this as root, or once:");
                Console.Error.WriteLine($"  sudo setcap cap_net_bind_service=+ep {Environment.ProcessPath}");
                Console.Error.WriteLine($"or pick a port above 1024 with {option}.");
            }

            else if (Problem.Because == SocketError.AddressAlreadyInUse)
                Console.Error.WriteLine("Another meter already running is the usual answer. " +
                                        $"Stop it, or give this one another port with {option} <number>.");

            if (Verbose)
                Console.Error.WriteLine(Problem);

            return 1;

        }

        #endregion

        #region (private static) PrintBanner       (...)

        /// <summary>
        /// What somebody who just started this needs to know.
        /// </summary>
        private static void PrintBanner(ModbusTLSEnergyMeter  Meter,
                                        String                PKIDirectory,
                                        String                PfxPassword,
                                        Boolean               OwnPKI)
        {

            var firstRegister = Meter.Device.BaseAddress;
            var lastRegister  = (UInt16) (Meter.Device.BaseAddress + Meter.Device.RegisterCount - 1);

            Console.WriteLine();
            Console.WriteLine($"  listening      {Meter.ListenAddress}:{Meter.ListenPort}  (mbaps - TLS only, mutual authentication)");
            Console.WriteLine($"  device         {Meter.Device.DisplayName}");
            Console.WriteLine($"  simulating     {Meter.Device.Mode.Description()}" +
                              (Meter.Device.SimulatedDayLength == TimeSpan.FromDays(1)
                                   ? ""
                                   : $", a day every {Meter.Device.SimulatedDayLength.TotalMinutes:F0} min"));
            Console.WriteLine($"  registers      {firstRegister} - {lastRegister}  (SunSpec Common Model 1 + Meter Model 213)");
            Console.WriteLine($"  meter cert     {Meter.MeterCertificate.Subject}");
            Console.WriteLine($"                 valid until {Meter.MeterCertificate.NotAfter:yyyy-MM-dd}" +
                              (Meter.ModbusCertificates.Next?.Certificate is X509Certificate2 nextModbus
                                   ? $", then the newer one from {nextModbus.NotBefore:yyyy-MM-dd HH:mm}"
                                   : "") +
                              $" ({Meter.ModbusCertificates.Entries.Count()} in the store)");
            Console.WriteLine($"  accepted CAs   {String.Join(", ", Meter.ClientTrust.Chains.Where(chain => chain.Enabled).Select(chain => chain.Name))}");
            Console.WriteLine($"  certificates   {PKIDirectory}");
            Console.WriteLine();
            Console.WriteLine($"  web interface  {Meter.WebInterfaceURL}" +
                              (Meter.HTTPS
                                   ? $"  (TLS, {Meter.WebCertificates.Current?.Certificate?.Subject ?? "no certificate"})"
                                   : "  (plain HTTP)"));
            Console.WriteLine($"  sign in        POST {Meter.WebInterfaceURL}{ModbusTLSEnergyMeter.ExtAPIPath.ToString().Trim('/')}/auth/login");
            Console.WriteLine($"  JSON API       {Meter.APIURL}v1/status");
            Console.WriteLine($"  event stream   {Meter.APIURL}v1/events");
            Console.WriteLine($"  accounts       {Meter.ExtAPI.Users.Count()} in {Meter.AccountsPath}");
            Console.WriteLine($"  log files      {(Meter.LogPath is not null ? $"{Meter.LogPath}, kept {Meter.LogKeepDays} days" : "none - the log is in memory only (--log-days 0)")}");
            Console.WriteLine($"  log book       {(Meter.MetrologicalLog is not null ? $"{Meter.MetrologicalLog.Path}, kept whole, signed by {Meter.MetrologicalLog.Signer.KeyId}" : "none")}");
            Console.WriteLine($"  configuration  {Meter.ConfigFile.Path}");

            #region What this binary was built from

            // Read out of the assemblies rather than asked of git here: git
            // would describe the working tree as it is now, while these
            // describe the trees each part was compiled from, and after a
            // checkout without a rebuild those are not the same answer.
            //
            // Worth the six lines because this meter signs things. A reading
            // or a charging session is only as trustworthy as the code that
            // signed it, and that code comes from six repositories that move
            // independently.
            var builtFrom = BuiltFrom.Repositories.ToArray();

            if (builtFrom.Length > 0)
            {

                // One line each, and the whole hash. This is meant to be read
                // out of a bug report and pasted into a checkout, and an
                // abbreviation is a thing somebody then has to guess the rest
                // of. The column is as wide as the longest name rather than a
                // number picked today, so a repository joining later still
                // lines up.
                // Two repositories here are cloned into directories of the
                // same name - the command line tool's and the library's - so
                // the name alone does not say which line is which. Where that
                // happens the assembly is named as well; where it does not,
                // nothing is added.
                String Label(LoadedAssembly repository)
                    => builtFrom.Count(other => other.Repository == repository.Repository) > 1
                           ? $"{repository.Repository} ({repository.Name})"
                           : repository.Repository!;

                var width = builtFrom.Max(repository => Label(repository).Length);

                for (var i = 0; i < builtFrom.Length; i++)
                    Console.WriteLine((i == 0 ? "  built from     " : "                 ") +
                                      Label(builtFrom[i]).PadRight(width) +
                                      "  " +
                                      builtFrom[i].Commit);

            }

            #endregion

            Console.WriteLine($"  name servers   {(Meter.DNSEnabled ? String.Join(", ", Meter.DNSClient.DNSServers) : "switched off")}");
            #region The time servers

            var bands = Meter.TimeSources.Bands();
            var asked = bands.SelectMany(band => band).ToArray();

            // The one server the clock is checked against - and not the single
            // client beside the group, which was named here: a file listing one
            // server of its own was announced as the default one, root dot and
            // all, while the log line above it named the right one.
            if (asked.Length <= 1)
                Console.WriteLine($"  time server    {(asked.Length == 1 ? asked[0].Hostname.Trimmed : "none switched on")}{(Meter.NTSEnabled ? $", checked every {Meter.TimeCheckEvery.TotalMinutes:F0} min" : " (switched off)")}");

            else
            {

                // One line per band, because a band is the unit that is
                // asked at once - putting two bands on one line would read
                // as six equal servers when it is two and then four.
                for (var i = 0; i < bands.Count; i++)
                    Console.WriteLine((i == 0 ? "  time servers   " : "                 ") +
                                      String.Join(", ", bands[i].Select(source => source.Hostname.Trimmed)) +
                                      (bands.Count > 1 ? $"   (priority {bands[i][0].Priority})" : ""));

                Console.WriteLine($"                 at least {Meter.TimeSources.MinServers} of them must answer" +
                                  (Meter.NTSEnabled ? $", checked every {Meter.TimeCheckEvery.TotalMinutes:F0} min" : " - and NTS is switched off"));

            }

            #endregion

            if (OwnPKI)
            {

                Console.WriteLine();
                Console.WriteLine($"  A client needs one of these, and the PKCS#12 password of all of them is '{PfxPassword}':");
                Console.WriteLine();

                foreach (var role in SunSpecRoles.AllMandatory)
                    Console.WriteLine($"    client-{role}.pfx".PadRight(52) + RightsOf(role));

                Console.WriteLine("    client-NO-ROLE.pfx".PadRight(52) + "refused - for testing that the policy holds");
                Console.WriteLine();
                Console.WriteLine($"  Read this meter yourself with:  ModbusTLSEnergyMeterCLI --selftest --port {Meter.ListenPort}");

            }

            if (Meter.GeneratedPassword is not null)
            {
                Console.WriteLine();
                Console.WriteLine("  +- First start: there were no accounts, so an administrator was made -------");
                Console.WriteLine($"  |  user      {Meter.GeneratedUserId}");
                Console.WriteLine($"  |  password  {Meter.GeneratedPassword}");
                Console.WriteLine("  |  It is shown here once and kept only as a hash. Write it down.");
                Console.WriteLine("  +---------------------------------------------------------------------------");
            }

            Console.WriteLine();

        }

        #endregion

        #region (private static) RightsOf          (Role)

        private static String RightsOf(String Role)

            => Role switch {
                   SunSpecRoles.ReadOnly              => "read everything, write nothing",
                   SunSpecRoles.GridService           => "and write the commanded registers",
                   SunSpecRoles.NetworkAdministrator  => "and write the protected registers",
                   SunSpecRoles.SuperAdministrator    => "and write everything",
                   _                                  => ""
               };

        #endregion

    }

}
