#!/usr/bin/env bash
# Builds Cuida tu Batería and its installer from any folder.
# Needs the .NET SDK (8 or newer). Works on Windows (Git Bash) and Linux.
# Usage: ./build.sh            production build
#        ./build.sh preview    build with test hooks (never ship this one)
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
EXTRA=()
if [[ "${1:-}" == "preview" ]]; then EXTRA=(-p:DefineConstants=PREVIEW); fi
OUT="$ROOT/build"
mkdir -p "$OUT/payload"
dotnet build "$ROOT/app/CuidaTuBateria.csproj" -c Release -o "$OUT/app" "${EXTRA[@]}" -nologo -v q
cp "$OUT/app/CuidaTuBateria.exe" "$OUT/payload/CuidaTuBateria.exe"
dotnet build "$ROOT/installer/CuidaTuBateriaSetup.csproj" -c Release -o "$OUT/installer" "${EXTRA[@]}" -nologo -v q
if [[ "${1:-}" != "preview" ]]; then
  for f in "$OUT/app/CuidaTuBateria.exe" "$OUT/installer/CuidaTuBateriaSetup.exe"; do
    if grep -a -q -E "CF_FAKE|CF_SEQ|CF_PAGE|CF_HIST|CF_SYSFONTS|CF_VIEW|CF_INTRO|CF_UPDATE|CF_LANG|CF_APAGON|CF_FOCUS|CF_RAW|CF_ALWAYS|CF_ABOUT|CF_DESKTOP|CF_WATTS" "$f"; then echo "Test hooks found in $f" >&2; exit 1; fi
  done
fi
sha256sum "$OUT/installer/CuidaTuBateriaSetup.exe"
