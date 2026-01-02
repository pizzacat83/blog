#!/bin/sh
set -eux
cd "$(dirname "$0")/.."
curl -sSL https://dot.net/v1/dotnet-install.sh > dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh -c 10.0 --install-dir "$HOME/.dotnet"
export PATH="$PATH:$HOME/.dotnet"
dotnet --version
dotnet tool restore
dotnet build --configuration Release
dotnet run --configuration Release --project fornax/src/Fornax build
