#!/bin/bash
# Builds ShootDrop.app (self-contained, Apple Silicon) into artifacts/ and opens it.
# Writes app-build.log next to this file.
cd "$(dirname "$0")" || exit 1
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1
APP="artifacts/ShootDrop.app"
ICON="src/ShootDrop.App/Assets/icon-1024.png"
LOG="app-build.log"
: > "$LOG"
log() { echo "$*" | tee -a "$LOG"; }

log "== Build ShootDrop.app  $(date '+%Y-%m-%d %H:%M:%S') =="
pkill -f "ShootDrop.app/Contents/MacOS/ShootDrop" 2>/dev/null && sleep 1   # close a running copy
rm -rf artifacts/publish "$APP" artifacts/ShootDrop.iconset

if ! dotnet publish src/ShootDrop.App -c Release -r osx-arm64 --self-contained true -o artifacts/publish >> "$LOG" 2>&1; then
  log "PUBLISH FAILED. Errors:"
  grep -E "error [A-Z]+[0-9]+|error:" "$LOG" | sort -u | head -40 | tee -a "$LOG"
  echo; read -n 1 -s -r -p "Press any key to close."; exit 1
fi
log "Publish: OK"

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R artifacts/publish/. "$APP/Contents/MacOS/"

ISET=artifacts/ShootDrop.iconset; mkdir -p "$ISET"
for s in 16 32 128 256 512; do
  sips -z $s $s "$ICON" --out "$ISET/icon_${s}x${s}.png" >/dev/null
  d=$((s*2)); sips -z $d $d "$ICON" --out "$ISET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ISET" -o "$APP/Contents/Resources/ShootDrop.icns" && log "Icon: OK"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>ShootDrop</string>
  <key>CFBundleDisplayName</key><string>ShootDrop</string>
  <key>CFBundleIdentifier</key><string>io.github.shootdrop.app</string>
  <key>CFBundleVersion</key><string>0.1.0</string>
  <key>CFBundleShortVersionString</key><string>0.1.0</string>
  <key>CFBundleExecutable</key><string>ShootDrop</string>
  <key>CFBundleIconFile</key><string>ShootDrop</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
</dict>
</plist>
PLIST
log "Built $APP"
open "$APP" && log "Opened ShootDrop.app"
