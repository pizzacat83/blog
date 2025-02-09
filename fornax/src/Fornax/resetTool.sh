#!/bin/sh

cd "$(dirname "$0")" || exit $?

dotnet tool uninstall -g Fornax
dotnet pack -c Release -o nupkg
dotnet tool install --add-source ./nupkg -g fornax
echo "Finished fornax reset"
