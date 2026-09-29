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
using System.Security.Cryptography.X509Certificates;

using org.GraphDefined.Vanaheimr.Hermod.SunSpecModbusTLS.Common;
using org.GraphDefined.Vanaheimr.Hermod.SunSpecModbusTLS.PKI;

using cloud.charging.open.protocols.WWCP.Node;
using cloud.charging.open.protocols.WWCP.Node.Logging;
using cloud.charging.open.protocols.WWCP.Node.CommandLine;
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


        #region (private static) PrintUsage()

        /// <summary>
        /// What -h shows, laid out as every kind of node lays out its own:
        /// wrapped at <see cref="NodeUsage.Width"/>, and what a switch does
        /// beginning at <see cref="NodeUsage.Column"/> - on the switch's line
        /// where the switch leaves room for it, on the lines below where not.
        /// </summary>
        /// <remarks>
        /// In the meter's own words rather than in <see cref="NodeUsage"/>'s,
        /// because the switches are not the node's: --port is the Modbus/TLS
        /// port here and always was, the web interface's is --http-port, and
        /// the accounts and the log live below --data.
        /// </remarks>
        private static void PrintUsage()
        {

            static void Say(IEnumerable<String> Lines)
            {
                foreach (var line in Lines)
                    Console.WriteLine(line);
            }

            static void Switch(String Name, String Says)
                => Say(NodeUsage.Switch(Name, Says));

            var below = new String(' ', NodeUsage.Column);

            Say(Synopsis("[--port <number>]", "[--any | --listen <address>]",
                         "[--pki <directory>]", "[--regenerate-pki]",
                         "[--name <text>]", "[--hostname <name>]", "[--ip <address>]",
                         "[--server-pfx <file>]", "[--pfx-password <text>]",
                         "[--client-ca <file>]", "[--serial <text>]",
                         "[--meter-mode <net|import|export>]", "[--sim-day <minutes>]",
                         "[--idle-timeout <seconds>]", "[--write-timeout <seconds>]",
                         "[--http-port <number>]", "[--https]", "[--config <file>]", "[--data <directory>]",
                         "[--log-days <number>]",
                         "[--selftest [<role>]]", "[--verify-log]",
                         "[--verbose | --quiet]"));
            Console.WriteLine();

            Console.WriteLine("Network:");
            Switch("--port <number>",       $"TCP port to listen on (default: {DefaultPort}, the registered mbaps port)");
            Switch("--any",                  "listen on all addresses instead of 127.0.0.1");
            Switch("--listen <address>",     "listen on one specific address");
            Switch("--idle-timeout <s>",     "drop a connection that asks nothing for this long (default: 300)");
            Switch("--write-timeout <s>",    "drop a connection that stops reading our answers (default: 30)");
            Console.WriteLine();

            Console.WriteLine("Web interface:");
            Switch("--http-port <number>",  $"where people sign in to administer this meter (default: {ModbusTLSEnergyMeter.DefaultHTTPPort})");
            Say(NodeUsage.Wrap("--any applies to this listener as well", below, below));
            Switch("--https",                "serve the web interface over TLS. Its certificate is its own, kept apart from the Modbus/TLS " +
                                             "one: what a charging station checks and what a browser checks come from different places and " +
                                             "are never the same file. At the first start the meter signs one for itself, so that the page " +
                                             "can be reached at all; a browser will say it does not know who signed it, and it is right. Ask " +
                                             "for a proper one on the Web certificate page.");
            Switch("--config <file>",       $"where the name servers and the time servers live (default: {WWCPConfigFile.DefaultFileName} " +
                                             "below the repository root)");
            Switch("--data <directory>",     "where the accounts and the log live (default: data/ below the repository root). At the first " +
                                            $"start an administrator, '{ModbusTLSEnergyMeter.DefaultAdminUser}', is made there and its password " +
                                             "is shown once.");
            Switch("--log-days <number>",   $"how many days of the log files are kept (default: {ModbusTLSEnergyMeter.DefaultLogKeepDays}). The " +
                                             "log book beside them - the entries that are evidence: the clock, the certificates, every write " +
                                             "and every refusal - is signed, chained and kept whole. 0 writes neither, and keeps the log in " +
                                             "memory only.");
            Console.WriteLine();

            Console.WriteLine("Certificates:");
            Switch("--pki <directory>",      "where the certificates live (default: pki/ below the repository root). A whole PKI - root CA, " +
                                             "two issuing CAs, a meter certificate and one client certificate per SunSpec role - is built " +
                                             "there at the first start.");
            Switch("--regenerate-pki",       "build the PKI again, overwriting what is there. Every client certificate handed out so far " +
                                             "stops working.");
            Switch("--name <text>",         $"common name of the meter certificate (default: {DefaultDeviceName})");
            Switch("--hostname <name>",      "another DNS name for the meter certificate, repeatable. Give the name that clients dial when " +
                                             "the meter is not on their machine.");
            Switch("--ip <address>",         "another IP address for the meter certificate, repeatable");
            Switch("--server-pfx <file>",    "use this PKCS#12 file instead of a generated one");
            Switch("--pfx-password <t>",    $"its password (default: {DemoPfxPassword})");
            Switch("--client-ca <file>",     "the CA that client certificates must chain to");
            Console.WriteLine();

            Console.WriteLine("Meter:");
            Switch("--serial <text>",        "serial number in SunSpec Common Model 1 (default: the --name)");
            Switch("--meter-mode <mode>",    "where this meter sits, and therefore which way energy flows through it (default: net). " +
                                            $"Writable afterwards through register {SunSpecMeterMap.Addr(SunSpecMeterMap.OffMeterMeterMode)} " +
                                             "and on the web page:");

            // Listed as the node lists the kinds of certificate: two columns
            // further in than what a switch does, and what each one is where
            // the longest name ends.
            foreach (var (mode, says) in new[] {
                         ("net",     "at the grid connection point: the simulated site draws at night and feeds back around noon, so " +
                                     "the power is signed and both counters move"),
                         ("import",  "in front of a load, which is what a charging station's meter is: only imported energy"),
                         ("export",  "in front of a generator: only exported energy, and zero at night, because the sun is down")
                     })
            {
                var name = $"{below}  {mode,-6}  ";
                Say(NodeUsage.Wrap(says, name, new String(' ', name.Length)));
            }

            Switch("--sim-day <minutes>",    "how much real time one simulated day takes (default: 1440, a real day). Compressing it runs " +
                                             "the load and the sun faster so that a whole day can be watched over a coffee; the energy " +
                                             "counters still count real seconds.");
            Console.WriteLine();

            Console.WriteLine("Checking:");
            Switch("--verify-log",           "do not serve: walk the log book on disk, check every line against the chain and the " +
                                             "signature, and say what it found");
            Switch("--selftest [<role>]",    "do not serve: connect to a meter that is already running, read its registers and print " +
                                            $"them. Role defaults to {SunSpecRoles.ReadOnly}.");
            Console.WriteLine();

            Console.WriteLine("Log:");
            Switch("-v, --verbose",          "write every entry, down to the debug ones");
            Switch("-q, --quiet",            "write only warnings and worse");
            Console.WriteLine();

            Say(NodeUsage.Wrap($"On Linux port {DefaultPort} is privileged: either start this as root, or allow the binary to bind " +
                                "low ports once with setcap, or pick a port above 1024 with --port.", "", ""));
            Console.WriteLine();

            Say(NodeUsage.Wrap("Once it is up, the console is a prompt: 'help' lists what can be typed there, Tab completes it, " +
                               "and 'quit' or Ctrl+C stops the meter. Started where there is no terminal - from a script, under a " +
                               "service manager, in CI, or with the output going into a file - there is no prompt and it simply " +
                               "runs.", "", ""));

        }

        #endregion

        #region (private static) Synopsis(params Items)

        /// <summary>
        /// The switches in one place, laid out as the node lays out its own:
        /// one bracketed item after the other behind the program's name, and a
        /// new line, begun below the first item, where the next item would
        /// reach past <see cref="NodeUsage.Width"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="NodeUsage.Wrap"/> breaks between words, and so would
        /// break "[--port &lt;number&gt;]" in two.
        /// </remarks>
        /// <param name="Items">The switches, one bracketed item each.</param>
        private static IEnumerable<String> Synopsis(params String[] Items)
        {

            const String usage = "Usage: ModbusTLSEnergyMeter";

            var line = usage;

            foreach (var item in Items)
            {

                if (line.Length > usage.Length && line.Length + 1 + item.Length > NodeUsage.Width)
                {
                    yield return line;
                    line = new String(' ', usage.Length);
                }

                line += " " + item;

            }

            yield return line;

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
                        if (!NodeArguments.TryTakeValue(Arguments, ref i, out listenAddress))
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
                        if (!NodeArguments.TryTakeValue(Arguments, ref i, out configFilePath))
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
                        if (!NodeArguments.TryTakeValue(Arguments, ref i, out dataPath))
                        {
                            Console.Error.WriteLine("Missing directory after --data!");
                            return 2;
                        }
                        break;

                    case "--pki":
                        if (!NodeArguments.TryTakeValue(Arguments, ref i, out pkiDirectory))
                        {
                            Console.Error.WriteLine("Missing directory after --pki!");
                            return 2;
                        }
                        break;

                    case "--regenerate-pki":
                        regeneratePKI = true;
                        break;

                    case "--name":
                        if (!NodeArguments.TryTakeValue(Arguments, ref i, out deviceName))
                        {
                            Console.Error.WriteLine("Missing text after --name!");
                            return 2;
                        }
                        break;

                    case "--hostname":
                        if (!NodeArguments.TryTakeValue(Arguments, ref i, out var dnsName))
                        {
                            Console.Error.WriteLine("Missing name after --hostname!");
                            return 2;
                        }
                        extraDNSNames.Add(dnsName);
                        break;

                    case "--ip":
                        if (!NodeArguments.TryTakeValue(Arguments, ref i, out var ipText) ||
                            !IPAddress.TryParse(ipText, out var extraIP))
                        {
                            Console.Error.WriteLine("Missing or invalid address after --ip!");
                            return 2;
                        }
                        extraIPs.Add(extraIP);
                        break;

                    case "--server-pfx":
                        if (!NodeArguments.TryTakeValue(Arguments, ref i, out serverPfxPath))
                        {
                            Console.Error.WriteLine("Missing file after --server-pfx!");
                            return 2;
                        }
                        break;

                    case "--pfx-password":
                        if (!NodeArguments.TryTakeValue(Arguments, ref i, out pfxPassword))
                        {
                            Console.Error.WriteLine("Missing text after --pfx-password!");
                            return 2;
                        }
                        break;

                    case "--client-ca":
                        if (!NodeArguments.TryTakeValue(Arguments, ref i, out clientCAPath))
                        {
                            Console.Error.WriteLine("Missing file after --client-ca!");
                            return 2;
                        }
                        break;

                    case "--serial":
                        if (!NodeArguments.TryTakeValue(Arguments, ref i, out serialNumber))
                        {
                            Console.Error.WriteLine("Missing text after --serial!");
                            return 2;
                        }
                        break;

                    case "--meter-mode":
                        if (!NodeArguments.TryTakeValue(Arguments, ref i, out var modeText) ||
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
                        if (NodeArguments.TryTakeValue(Arguments, ref i, out var role))
                            selfTestRole = role;
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

            // Where the PKI, the data and the configuration go unless told
            // otherwise: below the directory holding ModbusTLSEnergyMeter.slnx,
            // so that the certificates do not end up in bin/ - where the next
            // "dotnet clean" would take the meter's identity with it.
            var root = NodeProgram.RepositoryRoot("ModbusTLSEnergyMeter.slnx");

            #region The PKI, built at the first start

            var pkiDir     = Path.GetFullPath(pkiDirectory ?? Path.Combine(root, "pki"));
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
                return VerifyLog(dataPath ?? Path.Combine(root, "data"));

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
                                  DataPath:           dataPath       ?? Path.Combine(root, "data"),
                                  LogKeepDays:        logKeepDays,
                                  ConfigFile:         new WWCPConfigFile(
                                                          configFilePath ?? Path.Combine(root, WWCPConfigFile.DefaultFileName)
                                                      ),

                                  ConsoleLogLevel:    verbose ? LogLevel.Debug
                                                          : quiet ? LogLevel.Warning
                                                                  : LogLevel.Info
                              );
            }
            catch (Exception e)
            {
                return NodeProgram.CouldNotBeSetUp(ModbusTLSEnergyMeter.MeterKind, e, verbose);
            }

            await using (energyMeter)
            {

                // A port the meter could not have is the node's to explain,
                // with WhatToDoAbout for the switch that moves it. Started lets
                // anything else through, and whatever else would stop the start
                // is said here, as it was before the node said the rest, rather
                // than end the process with a stack trace.
                try
                {
                    if (await energyMeter.Started(verbose, WhatToDoAbout) is Int32 notStarted)
                        return notStarted;
                }
                catch (Exception e)
                {

                    Console.Error.WriteLine($"The {ModbusTLSEnergyMeter.MeterKind.Name} could not start: {e.Message}");

                    if (verbose)
                        Console.Error.WriteLine(e);

                    return 1;

                }

                PrintBanner(energyMeter, pkiDir, pfxPassword, ownPKI);

                #region The command line, until 'quit', Ctrl+C or SIGTERM

                // The node's command line: a prompt where somebody can type,
                // nothing but waiting where nobody can - started from a script,
                // a service manager or CI - and the log and the prompt sharing
                // one screen. Its end is 'quit', Ctrl+C or SIGTERM, and the end
                // of the Modbus/TLS frontend as well, which is what this waited
                // for before there was a prompt: a meter whose frontend has
                // stopped is not kept up by somebody typing.
                await new MeterCLI(energyMeter).RunUntilStopped(energyMeter.RunTask);

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

        #region (private static) WhatToDoAbout(Problem)

        /// <summary>
        /// What somebody can do about a port of the meter's that something
        /// else is listening on: move it, with the switch that moves that one.
        /// </summary>
        /// <remarks>
        /// Which one matters, because a different switch moves each: --port the
        /// Modbus/TLS frontend, --http-port the web interface. "Port in use" on
        /// its own sends somebody to move the wrong one half of the time - and
        /// so does the node's own sentence, asked for where this gives none: it
        /// names --port, which is the web interface's on every other kind of
        /// node.
        /// </remarks>
        private static String WhatToDoAbout(PortUnavailableException Problem)

            => "Another meter already running is the usual answer. Stop it, or give this one another port with " +
              $"{(Problem.Whose == ModbusTLSEnergyMeter.ModbusTLSPort ? "--port" : "--http-port")} <number>.";

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
                              (Meter.ModbusCertificates.Next is { } nextModbus
                                   ? $", then the newer one from {nextModbus.NotBefore.UtcDateTime:yyyy-MM-dd HH:mm}"
                                   : "") +
                              $" ({Meter.ModbusCertificates.Candidates.Count} valid for Modbus/TLS in the store)");
            Console.WriteLine($"  accepted CAs   {String.Join(", ", Meter.ClientRoots.Usable.Select(root => root.Label))}");
            Console.WriteLine($"  certificates   {PKIDirectory}");
            Console.WriteLine();
            Console.WriteLine($"  web interface  {Meter.WebInterfaceURL}" +
                              (Meter.HTTPS
                                   ? $"  (TLS, {Meter.WebCertificates.Current?.Certificate.Subject ?? "no certificate"})"
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
            // Worth the lines because this meter signs things. A reading or a
            // charging session is only as trustworthy as the code that signed
            // it, and that code comes from repositories that move
            // independently. The node says it as every kind of node does: a
            // line for each repository with its whole commit, and the assembly
            // beside the name where two share one - as the command line tool's
            // and the library's do here.
            foreach (var line in Meter.BuiltFrom.BannerLines())
                Console.WriteLine(line);

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
