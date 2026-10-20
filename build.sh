#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
game_path="${1:-${STARDEW_GAME_PATH:-}}"
smapi_path="${2:-$game_path}"
if [[ -z "$game_path" ]]; then
  echo 'Usage: ./build.sh "/path/to/Stardew Valley 1.6.8" ["/path/to/SMAPI 4.0.8 assemblies"]' >&2
  exit 2
fi
dotnet_bin="${DOTNET:-dotnet}"
if ! command -v "$dotnet_bin" >/dev/null 2>&1 && [[ -x ../.tools/dotnet/dotnet ]]; then
  dotnet_bin=../.tools/dotnet/dotnet
fi
"$dotnet_bin" build ProductionProgress.csproj -c Release "-p:GamePath=$game_path" "-p:SmapiPath=$smapi_path" --nologo
python3 tools/package.py
