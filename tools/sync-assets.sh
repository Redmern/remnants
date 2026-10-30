#!/usr/bin/env bash
# Copy the remnants-assets export into game/assets/generated/: the models (manifest.json, style.json, */*.glb) and the
# audio (audio/manifest.json, audio/*/*.ogg, read by game/scripts/Audio/AudioManifest.cs).
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

if [[ -d "$src/audio" && ! -f "$src/audio/manifest.json" ]]; then
  echo "'$src/audio' has no manifest.json; re-export the audio in remnants-assets." >&2
  exit 1
fi

mkdir -p "$dest"
# --delete drops models and sounds removed upstream; Godot's own *.import sidecars are kept.
rsync -a --delete --exclude '*.import' "$src"/ "$dest"/
echo "Synced $(find "$dest" -name '*.glb' | wc -l) models from $(cd "$src" && pwd) to $dest"
if [[ -f "$dest/audio/manifest.json" ]]; then
  echo "Synced $(find "$dest/audio" -name '*.ogg' | wc -l) audio files ($(du -sh "$dest/audio" | cut -f1)) to $dest/audio"
else
  echo "No audio in this export (the game stays silent apart from its UI placeholders)."
fi
echo "Open the project once (or: godot-mono --headless --path game --import) so Godot imports new files, then commit them with their .import sidecars."
