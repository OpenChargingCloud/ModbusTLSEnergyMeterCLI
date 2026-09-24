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

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.CLI;
using org.GraphDefined.Vanaheimr.Illias;

#endregion

namespace cloud.charging.open.EnergyMeters.ModbusTLS.CommandLine
{

    /// <summary>
    /// Ask this meter's time servers what the time is.
    /// </summary>
    /// <remarks>
    /// The same thing the NTS page does with 'Check the clock now'. Both end in
    /// <see cref="ModbusTLSEnergyMeter.SyncTimeAsync"/>, which asks the same
    /// group of servers, writes every step into the log and keeps the result
    /// as the last synchronisation the page shows - so the log book reads the
    /// same whichever of the two was used, apart from the one line that says
    /// who asked. A command that did any of that itself would be the beginning
    /// of two meters disagreeing about what they did.
    ///
    /// Like the button, it does not step the clock: it says whether the time
    /// servers can be reached and what they think of the local clock.
    ///
    /// It takes no time server after it. The vehicle's and the charging
    /// station's do, and then test that one server step by step, as the Test
    /// button in its row of their NTS page does; this meter has neither that
    /// button nor the test behind it, and a command is a second way of asking
    /// for what the web interface does - never an implementation of its own.
    /// </remarks>
    /// <param name="CLI">The command line of the meter to ask.</param>
    public class SyncNTSCommand(MeterCLI CLI) : ACLICommand<MeterCLI>(CLI),
                                                ICLICommand
    {

        #region Data

        /// <summary>
        /// The name this is typed as. Taken from the class name, as everywhere
        /// else: "SyncNTSCommand" without its last seven characters. Found in
        /// any case - "syncnts" is the same command.
        /// </summary>
        public static readonly String CommandName = nameof(SyncNTSCommand)[..^7].ToLowerFirstChar();

        #endregion

        #region Suggest(Arguments)

        /// <summary>
        /// Complete the command. It takes nothing after it: which servers are
        /// asked is the meter's configuration, as it is for the button.
        /// </summary>
        public override IEnumerable<SuggestionResponse> Suggest(String[] Arguments)
        {

            if (Arguments.Length == 1 &&
                CommandName.StartsWith(Arguments[0], StringComparison.CurrentCultureIgnoreCase))
            {
                return [ SuggestionResponse.CommandCompleted(CommandName) ];
            }

            return [];

        }

        #endregion

        #region Execute(Arguments, CancellationToken)

        public override async Task<String[]> Execute(String[]           Arguments,
                                                     CancellationToken  CancellationToken)
        {

            if (Arguments.Length > 1)
                return [ $"Usage: {Help()}" ];

            // The line the web interface writes when 'Check the clock now' is
            // pressed, in the same words, at the same level and with the tags
            // it carries there - "cli" where it says "web". There it names the
            // account that pressed the button; here it is whoever is at the
            // console, which the meter cannot tell apart. Said before anything
            // is asked, because the entries that follow record which servers
            // were asked and what came of it, and nothing in them says who
            // wanted it.
            cli.Meter.Log.Notice(
                "Somebody at the command line asked this meter to check its clock.",
                "nts", "cli"
            );

            var result = await cli.Meter.SyncTimeAsync(CancellationToken);

            return Outcome(result);

        }

        #endregion

        #region Help()

        public override String Help()

            => $"{CommandName} - ask the time servers what the time is, as 'Check the clock now' on the NTS page does; the clock is not stepped";

        #endregion


        #region (private static) Outcome(Result)

        /// <summary>
        /// What the NTS page shows after 'Check the clock now', as lines for the
        /// console: how it went, and what each server said.
        /// </summary>
        /// <remarks>
        /// The servers get a line each because the log does not have them: it
        /// records that the group was asked and what the group concluded, and
        /// which server was the one that did not answer is only in here and on
        /// the page.
        /// </remarks>
        private static String[] Outcome(JObject Result)
        {

            var lines    = new List<String>();
            var servers  = Result["servers"] as JArray ?? [];
            var took     = Result.Value<Int64?>("runtime_ms") is Int64 runtime
                               ? $" after {runtime} ms"
                               : "";

            if (Result.Value<Boolean>("ok"))
            {

                var group = Result["group"] as JObject;

                lines.Add($"succeeded{took}: {group?.Value<Int32?>("answered")} of {servers.Count} server(s) answered " +
                          $"({group?.Value<Int32?>("required")} required), " +
                          $"offset {Milliseconds(group?.Value<Double?>("offset_ms"), Signed: true)}, " +
                          $"spread {Milliseconds(group?.Value<Double?>("spread_ms"))}");

                if (group?.Value<Boolean?>("deviationExceeded") == true)
                    lines.Add("  The servers disagree by more than the agreed deviation - see the log.");

            }
            else
                lines.Add($"failed{took}: {Result.Value<String>("error") ?? "no reason was given"}");

            if (servers.Count > 0)
            {

                // Without the root's dot, as the banner writes them:
                // "ptbtime1.ptb.de." is correct and nobody reads it that way.
                static String Hostname(JToken Server)
                    => (Server.Value<String>("hostname") ?? "").TrimEnd('.');

                var width = servers.Max(server => Hostname(server).Length);

                foreach (var server in servers.OfType<JObject>())
                    lines.Add($"  {Hostname(server).PadRight(width)}  {ServerSaid(server)}");

            }

            return [.. lines];

        }

        #endregion

        #region (private static) ServerSaid(Server)

        /// <summary>
        /// What one server said: its offset and round trip when its answer
        /// counted, and otherwise why it did not.
        /// </summary>
        /// <remarks>
        /// An answer that did not authenticate is said to be one. The page
        /// writes "no answer" for it, and on a console that is the difference
        /// between a server that is down and one that is not the server it
        /// claims to be.
        /// </remarks>
        private static String ServerSaid(JObject Server)
        {

            if (Server.Value<Boolean>("ok"))
                return $"{Milliseconds(Server.Value<Double?>("offset_ms"), Signed: true)}, " +
                       $"round trip {Milliseconds(Server.Value<Double?>("roundTrip_ms"))}, " +
                       $"key exchange {Server.Value<String>("keyExchange") ?? "unknown"}";

            if (Server.Value<String>("error") is String error && error.Length > 0)
                return error;

            if (Server.Value<Boolean?>("authenticated") == false)
                return "answered, but the answer did not authenticate";

            return "no answer";

        }

        #endregion

        #region (private static) Milliseconds(Value, Signed = false)

        /// <summary>
        /// The one place a number with a fraction is written, and so the one
        /// place the culture has to be kept out: these sentences are English,
        /// and a meter in a German locale would otherwise write "+1,2 ms" in
        /// the middle of one.
        /// </summary>
        private static String Milliseconds(Double? Value, Boolean Signed = false)

            => Value is Double value
                   ? value.ToString(Signed ? "+0.0;-0.0;0.0" : "0.0", CultureInfo.InvariantCulture) + " ms"
                   : "unknown";

        #endregion

    }

}
