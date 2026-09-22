#!/bin/bash
#
# Start the simulated SunSpec Modbus/TLS energy meter.
#
# Everything after this script's name is handed to the meter, so
#   ./run.sh --any --port 5802
# listens on every address on an unprivileged port, and
#   ./run.sh --help
# lists what else it takes.
#
# The registered mbaps port 802 is privileged on Linux: either run this as
# root, or allow the binary to bind low ports once with
#   sudo setcap cap_net_bind_service=+ep ModbusTLSEnergyMeterCLI/bin/Release/net10.0/ModbusTLSEnergyMeterCLI
# or pick a port above 1024.

set -e

cd "$(dirname "$0")"

dotnet run --project ModbusTLSEnergyMeterCLI/ModbusTLSEnergyMeterCLI.csproj -c Release -- "$@"
