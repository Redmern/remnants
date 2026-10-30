#!/usr/bin/env bash
# Build everything and run the headless Core and multiplayer tests. Pass --godot to also build the Godot project
# through the editor and run the headless audio self-test (needs godot-mono on PATH).
set -euo pipefail
cd "$(dirname "$0")"

dotnet build Remnants.sln --nologo -v quiet
dotnet test tests/Remnants.Core.Tests --nologo --no-build -v quiet
dotnet test tests/Remnants.Net.Tests --nologo --no-build -v quiet

if [[ "${1:-}" == "--godot" ]]; then
  godot-mono --headless --path game --build-solutions --quit
  godot-mono --headless --path game -- --audio-selftest | grep -E '^\[audio-selftest\]'
fi
