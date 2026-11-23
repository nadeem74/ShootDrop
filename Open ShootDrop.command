#!/bin/bash
# Starts the ShootDrop app (the "Ready to copy" window). Close the app window to finish.
cd "$(dirname "$0")" || exit 1
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet run --project src/ShootDrop.App 2>&1 | tee app.log
