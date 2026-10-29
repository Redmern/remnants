#!/usr/bin/env bash
# Copy the remnants-assets export (manifest.json, style.json, */*.glb) into game/assets/generated/.
# Usage: tools/sync-assets.sh [EXPORT_DIR]   (or set REMNANTS_ASSETS_EXPORT)
# Default: ../../remnants-assets/main/export relative to the repo root.
# Files there are generated: do not edit them by hand, re-run this instead.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
src="${1:-${REMNANTS_ASSETS_EXPORT:-$root/../../remnants-assets/main/export}}"
dest="$root/game/assets/generated"

if [[ ! -f "$src/manifest.json" ]]; then
  echo "No manifest.json in '$src'. Pass the remnants-assets export/ folder." >&2
  exit 1
fi

mkdir -p "$dest"
# --delete drops models removed upstream; Godot's own *.import sidecars are kept.
rsync -a --delete --exclude '*.import' "$src"/ "$dest"/
echo "Synced $(find "$dest" -name '*.glb' | wc -l) models from $(cd "$src" && pwd) to $dest"
