#!/bin/bash
# Wraps a `dotnet publish` output folder into a macOS .app bundle.
# Usage: scripts/macos/package-app.sh <publish-dir> <output.app> [version]
# Used by scripts/macos/build-app.command and by .github/workflows/release.yml.
set -euo pipefail

PUBLISH="$1"
APP="$2"
VERSION="${3:-0.1.0}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
ICON="$ROOT/src/ShootDrop.App/Assets/icon-1024.png"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"

# Icon: build every size macOS wants from the 1024 px PNG.
ISET="$(mktemp -d)/ShootDrop.iconset"
mkdir -p "$ISET"
for s in 16 32 128 256 512; do
  sips -z $s $s "$ICON" --out "$ISET/icon_${s}x${s}.png" >/dev/null
  d=$((s * 2)); sips -z $d $d "$ICON" --out "$ISET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ISET" -o "$APP/Contents/Resources/ShootDrop.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>ShootDrop</string>
  <key>CFBundleDisplayName</key><string>ShootDrop</string>
  <key>CFBundleIdentifier</key><string>io.github.shootdrop.app</string>
  <key>CFBundleVersion</key><string>${VERSION}</string>
  <key>CFBundleShortVersionString</key><string>${VERSION}</string>
  <key>CFBundleExecutable</key><string>ShootDrop</string>
  <key>CFBundleIconFile</key><string>ShootDrop</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
</dict>
</plist>
PLIST

# Ad-hoc signature so Apple Silicon will launch it (not notarised; see README).
codesign --force --deep --sign - "$APP" >/dev/null 2>&1 || true
echo "Built $APP ($VERSION)"
