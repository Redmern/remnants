#!/usr/bin/env bash
# Export a playable Windows x86_64 build and zip it to build/remnants-windows-x86_64.zip.
# Headless only (needs godot-mono and the 4.7.2 mono export templates). The game data ships embedded in
# Remnants.Game.dll, so no side files are needed; a data/ folder next to remnants.exe would override it.
set -euo pipefail
cd "$(dirname "$0")/.."

out=build/windows
zip=build/remnants-windows-x86_64.zip
rm -rf "$out" "$zip"
mkdir -p "$out"

godot-mono --headless --path game --export-release "Windows Desktop" ../build/windows/remnants.exe 2>&1 | tee build/export-windows.log
for f in remnants.exe remnants.console.exe remnants.pck data_Remnants.Game_windows_x86_64; do
  [[ -e "$out/$f" ]] || { echo "export-windows: missing $out/$f" >&2; exit 1; }
done
if grep -qE '^(ERROR|SCRIPT ERROR)' build/export-windows.log; then
  echo "export-windows: export logged errors (see build/export-windows.log)" >&2; exit 1
fi

cp docs/INSTALL_WINDOWS.md "$out/README.md"
(cd build && rm -rf remnants && cp -r windows remnants && zip -qr "$(basename "$zip")" remnants && rm -rf remnants)
echo "export-windows: $zip ($(du -h "$zip" | cut -f1))"
