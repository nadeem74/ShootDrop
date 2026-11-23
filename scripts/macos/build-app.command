#!/bin/bash
# Builds ShootDrop.app (self-contained, Apple Silicon) into artifacts/ and opens it.
# Double-click in Finder. Writes app-build.log in the repo root.
cd "$(dirname "$0")/../.." || exit 1   # run from the repo root
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1
APP="artifacts/ShootDrop.app"
LOG="app-build.log"
: > "$LOG"
log() { echo "$*" | tee -a "$LOG"; }

log "== Build ShootDrop.app  $(date '+%Y-%m-%d %H:%M:%S') =="
pkill -f "ShootDrop.app/Contents/MacOS/ShootDrop" 2>/dev/null && sleep 1   # close a running copy
rm -rf artifacts/publish

if ! dotnet publish src/ShootDrop.App -c Release -r osx-arm64 --self-contained true -o artifacts/publish >> "$LOG" 2>&1; then
  log "PUBLISH FAILED. Errors:"
  grep -E "error [A-Z]+[0-9]+|error:" "$LOG" | sort -u | head -40 | tee -a "$LOG"
  echo; read -n 1 -s -r -p "Press any key to close."; exit 1
fi
log "Publish: OK"

scripts/macos/package-app.sh artifacts/publish "$APP" 0.2.0 2>&1 | tee -a "$LOG"
open "$APP" && log "Opened ShootDrop.app"
