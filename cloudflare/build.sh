#!/bin/sh
curl -sSL https://dot.net/v1/dotnet-install.sh > dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh -c 8.0 -InstallDir ./dotnet
./dotnet/dotnet --version
./dotnet/dotnet tool restore
./dotnet/dotnet paket restore
./dotnet/dotnet run --configuration Release --project fornax/src/Fornax build
