#!/bin/bash

git submodule foreach git pull
git pull
dotnet build ModbusTLSEnergyMeter.slnx
