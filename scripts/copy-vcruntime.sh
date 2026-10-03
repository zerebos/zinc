#!/usr/bin/env bash
# Copies the Visual C++ runtime (vcruntime140.dll) next to the native DLLs, from the newest Visual Studio
# redistributable folder: native/<arch>/vcruntime140.dll.
#
# ghostty-vt.dll and zinc-sprite.dll are built for Zig's windows-msvc target, so they import vcruntime140.dll,
# which isn't part of Windows: most PCs have it from some other app, a fresh one (Windows Sandbox, a new install)
# doesn't, and Zinc then fails with "Unable to load DLL 'ghostty-vt'". Microsoft allows apps to carry it in
# their own folder ("app-local"); the copy in VC\Redist is the one meant for that.
#
# Run by build-libghostty.sh; alone: scripts/copy-vcruntime.sh [x64|arm64]. Needs Visual Studio or its Build
# Tools with the C++ workload (for the Redist folder).
set -euo pipefail

ARCH="${1:-x64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VSWHERE="/c/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe"
[ -x "$VSWHERE" ] || { echo "vswhere.exe not found: install Visual Studio or its Build Tools (C++ workload)" >&2; exit 1; }

# The newest redistributable among every installed Visual Studio (newer versions run older programs' code).
FOUND=""
while IFS= read -r install; do
  install="$(cygpath -u "$install")"
  for dll in "$install"/VC/Redist/MSVC/*/"$ARCH"/Microsoft.VC14*.CRT/vcruntime140.dll; do
    [ -f "$dll" ] && FOUND="$FOUND$dll"$'\n'
  done
done < <("$VSWHERE" -all -products '*' -property installationPath | tr -d '\r')

# Each path is .../MSVC/<version>/<arch>/Microsoft.VC14x.CRT/vcruntime140.dll: sort on <version>.
DLL="$(printf '%s' "$FOUND" | sed '/^$/d' | awk -F/ '{print $(NF-3) "\t" $0}' | sort -V | tail -1 | cut -f2)"
[ -n "$DLL" ] || { echo "no $ARCH vcruntime140.dll in any Visual Studio's VC\\Redist folder" >&2; exit 1; }

mkdir -p "$ROOT/native/$ARCH"
cp "$DLL" "$ROOT/native/$ARCH/"
echo "vcruntime140.dll ($(basename "$(dirname "$(dirname "$(dirname "$DLL")")")")) -> native/$ARCH/vcruntime140.dll"
