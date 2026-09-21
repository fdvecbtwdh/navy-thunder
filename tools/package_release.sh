#!/usr/bin/env bash
# R5 release packaging: builds the Godot .NET frontend into a Windows package.
#
# Prerequisites:
#   - dotnet SDK 10 on PATH
#   - Godot 4.7.x mono editor (set GODOT_EXE below or pass as $1)
#   - Godot .NET export templates installed once:
#       "$GODOT_EXE" --headless --export-install-templates  # or download from
#       https://godotengine.org/download/windows (Export templates (.NET))
#
# Usage: tools/package_release.sh [godot-exe]
set -euo pipefail

GODOT_EXE="${1:-${GODOT_EXE:-/d/Tools/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe}}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION=$(grep -oP '(?<=public const string Version = ")[^"]+' "$ROOT/frontend/Godot/L10n.cs" || echo "0.9.0")
OUT="$ROOT/dist/NavyThunder_${VERSION}_win64"

cd "$ROOT/frontend/Godot"

# 1) publish the .NET assembly (Godot export also builds, explicit = deterministic)
dotnet build NavyThunder.Frontend.csproj -c Release

# 2) Godot release export (requires export templates; see prerequisites)
mkdir -p "$OUT"
"$GODOT_EXE" --headless --path . --export-release "Windows Desktop" "$OUT/NavyThunder.exe"

# 3) game data next to the executable (ships/shells/scenarios are read at runtime)
# clean re-copy: merging a previous package would nest data/data and break validation
rm -rf "$OUT/data" "$OUT/scenarios"
cp -r "$ROOT/data" "$OUT/data"
cp -r "$ROOT/scenarios" "$OUT/scenarios"
mkdir -p "$OUT/assets/models"
for d in "$ROOT/assets/models"/*/; do
    [ -f "$d/hull.obj" ] && mkdir -p "$OUT/assets/models/$(basename "$d")" && cp "$d/hull.obj" "$OUT/assets/models/$(basename "$d")/"
done

echo "package ready: $OUT"
