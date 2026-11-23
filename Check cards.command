#!/bin/bash
# Lists plugged-in cards and shows what ShootDrop WOULD copy. Copies nothing, creates no project.
# Also saves the projects folder (same as your old script) in ShootDrop's settings.
cd "$(dirname "$0")" || exit 1
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1
ROOT="/Volumes/SSD 1TB/SSD Video Projects"
CLI="src/ShootDrop.Cli/bin/Debug/net10.0/shootdrop.dll"
{
  echo "== ShootDrop card check  $(date '+%Y-%m-%d %H:%M:%S') =="
  dotnet build src/ShootDrop.Cli -v q -nologo 2>&1 | tail -3
  echo; echo "== Mounted volumes =="; ls -1 /Volumes
  echo; echo "== Save projects folder =="
  dotnet "$CLI" config --root "$ROOT"
  echo; echo "== Cards ShootDrop sees =="
  dotnet "$CLI" cards
  for d in today yesterday last3; do
    echo; echo "== Dry run: $d, video + audio (nothing is copied) =="
    dotnet "$CLI" plan --date "$d" --types video,audio --cards all --name "Dry run"
  done
} 2>&1 | tee cards.log
echo; echo "Done. Saved to cards.log."
read -n 1 -s -r -p "Press any key to close."
