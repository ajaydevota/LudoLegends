#!/usr/bin/env bash
#
# Copies the shared rules engine (server/src/LudoLegends.Core/*.cs) into the Unity
# project. Unity compiles everything under Assets/, so the same source files that the
# authoritative server runs are also compiled into the Android client. That is what makes
# the offline AI modes behave identically to online play.
#
# Run after changing anything in LudoLegends.Core, before opening Unity.
#
#   ./tools/sync-core.sh
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="$ROOT/server/src/LudoLegends.Core"
DST="$ROOT/unity/LudoLegends/Assets/Scripts/Shared"

mkdir -p "$DST"

find "$DST" -maxdepth 1 -name '*.cs' -delete

for file in "$SRC"/*.cs; do
  cp "$file" "$DST/"
done

echo "Synced $(ls -1 "$DST" | wc -l | tr -d ' ') shared C# files into $DST"
