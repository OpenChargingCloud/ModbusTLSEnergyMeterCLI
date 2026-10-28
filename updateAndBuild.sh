#!/bin/bash
#
# Pull everything and build it.
#

set -e

cd "$(dirname "$0")"

git submodule foreach git checkout master
git submodule foreach git pull
git pull
dotnet build ModbusTLSEnergyMeter.slnx
