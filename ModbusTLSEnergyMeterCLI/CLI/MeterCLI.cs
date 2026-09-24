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

using System.Reflection;

#endregion

namespace cloud.charging.open.EnergyMeters.ModbusTLS.CommandLine
{

    /// <summary>
    /// The command line of a running energy meter.
    /// </summary>
    /// <remarks>
    /// Everything a command needs is reachable from here, which is why every
    /// command takes one of these: the meter itself, and through it its
    /// configuration, its log and everything the JSON API can do. A command is
    /// a second way of asking for the same thing as the web interface - never
    /// an implementation of its own.
    ///
    /// Commands are not listed anywhere. The constructor asks Styx to walk this
    /// assembly for anything that implements ICLICommand and can be built from
    /// a MeterCLI, so a new command is a new file and nothing else.
    ///
    /// Styx's CLI is named in full, where the vehicle's and the station's
    /// import its namespace: this program's own namespace ends in ".CLI", and
    /// from anywhere below cloud.charging.open.EnergyMeters.ModbusTLS the name
    /// on its own finds that namespace before any type of that name.
    /// </remarks>
    public class MeterCLI : org.GraphDefined.Vanaheimr.CLI.CLI
    {

        #region Properties

        /// <summary>
        /// The energy meter these commands are about.
        /// </summary>
        public ModbusTLSEnergyMeter Meter { get; }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Create the command line of the given energy meter.
        /// </summary>
        /// <param name="Meter">The running energy meter.</param>
        /// <param name="AssembliesWithCLICommands">Further assemblies to search for commands. This one is searched either way.</param>
        public MeterCLI(ModbusTLSEnergyMeter  Meter,
                        params Assembly[]     AssembliesWithCLICommands)

            : base(AssembliesWithCLICommands)

        {

            this.Meter = Meter;

            RegisterCLIType(typeof(MeterCLI));

        }

        #endregion


        #region (protected override) GetPrompt()

        /// <summary>
        /// The meter's serial number, because a meter usually runs on a bench
        /// beside a charging station and a vehicle, and often beside a second
        /// meter - and a console should say which of them it is before it asks
        /// for a command.
        /// </summary>
        /// <remarks>
        /// The serial number rather than a fixed word, as the vehicle takes its
        /// name: it is what --serial or --name gave this meter, what SunSpec
        /// Common Model 1 reports and what --selftest prints as "serial", so
        /// the prompt says the same thing about this meter as everything else.
        /// </remarks>
        protected override String GetPrompt()

            => $"{Meter.SerialNumber}> ";

        #endregion

    }

}
