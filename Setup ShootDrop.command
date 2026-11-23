#!/bin/bash
# ShootDrop one-click setup for macOS.
# Double-click this file in Finder. It:
#   1. checks for the .NET 10 SDK and installs it into ~/.dotnet if missing (Microsoft's official script, no admin password)
#   2. builds the solution and runs the tests
#   3. writes setup.log, build.log and test.log next to this file so Claude can read them
# It never touches your cards, your SSD or anything outside this folder and ~/.dotnet.

cd "$(dirname "$0")" || exit 1
LOG="setup.log"
: > "$LOG"
say() { echo "$*" | tee -a "$LOG"; }

say "== ShootDrop setup  $(date '+%Y-%m-%d %H:%M:%S') =="
say "macOS $(sw_vers -productVersion 2>/dev/null)  $(uname -m)"

export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

has_sdk10() { command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; }

if has_sdk10; then
  say ".NET 10 SDK: found ($(dotnet --version))"
else
  say ".NET 10 SDK: not found, installing into ~/.dotnet (official Microsoft install script)..."
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh 2>>"$LOG" \
    && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet" >>"$LOG" 2>&1
  if has_sdk10; then
    say ".NET 10 SDK: installed ($(dotnet --version))"
    if ! grep -q '.dotnet' "$HOME/.zprofile" 2>/dev/null; then
      printf '\n# .NET SDK (added by ShootDrop setup)\nexport DOTNET_ROOT="$HOME/.dotnet"\nexport PATH="$HOME/.dotnet:$PATH"\n' >> "$HOME/.zprofile"
      say "Added ~/.dotnet to your PATH in ~/.zprofile (new Terminal windows will find dotnet)."
    fi
  else
    say "ERROR: could not install the .NET 10 SDK. See setup.log."
    echo; read -n 1 -s -r -p "Press any key to close."; exit 1
  fi
fi

say "Claude Code: $(command -v claude >/dev/null 2>&1 && echo found || echo 'not found (optional)')"
say "git: $(command -v git >/dev/null 2>&1 && git --version || echo 'not found')"

say ""
say "== Build =="
if dotnet build ShootDrop.slnx > build.log 2>&1; then
  say "Build: OK"
  BUILD_OK=1
else
  say "Build: FAILED (details in build.log)"
  grep -E "error [A-Z]+[0-9]+" build.log | sort -u | head -40 | tee -a "$LOG"
  BUILD_OK=0
fi

if [ "$BUILD_OK" = 1 ]; then
  say ""
  say "== Tests =="
  if dotnet test ShootDrop.slnx --no-build > test.log 2>&1; then
    say "Tests: all passed"
  else
    say "Tests: some FAILED (details in test.log)"
  fi
  grep -E "(Passed!|Failed!|Total tests|Passed:|Failed:)" test.log | tail -5 | tee -a "$LOG"
fi

if [ ! -d .git ] && command -v git >/dev/null 2>&1; then
  git init -q -b main && say "Created a git repository (no commit yet)."
fi

say ""
say "Done. Tell Claude in the chat: \"setup finished\"."
echo
read -n 1 -s -r -p "Press any key to close this window."
