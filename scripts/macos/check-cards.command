#!/bin/bash
# Lists plugged-in cards and shows what ShootDrop WOULD copy. Copies nothing, creates no project.
# Optional: pass your projects folder to save it first, e.g.  check-cards.command "/Volumes/My SSD/Video Projects"
cd "$(dirname "$0")/../.." || exit 1   # run from the repo root
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1
ROOT="${1:-}"
CLI="src/ShootDrop.Cli/bin/Debug/net10.0/shootdrop.dll"
{
  echo "== ShootDrop card check  $(date '+%Y-%m-%d %H:%M:%S') =="
  dotnet build src/ShootDrop.Cli -v q -nologo 2>&1 | tail -3
  echo; echo "== Mounted volumes =="; ls -1 /Volumes
  if [ -n "$ROOT" ]; then echo; echo "== Save projects folder =="; dotnet "$CLI" config --root "$ROOT"; fi
  echo; echo "== Cards ShootDrop sees =="
  dotnet "$CLI" cards
  for d in today yesterday last3; do
    echo; echo "== Dry run: $d, video + audio (nothing is copied) =="
    dotnet "$CLI" plan --date "$d" --types video,audio --cards all --name "Dry run"
  done
} 2>&1 | tee cards.log
echo; echo "Done. Saved to cards.log."
read -n 1 -s -r -p "Press any key to close."
