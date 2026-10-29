#!/usr/bin/env bash
# Build everything and run the headless Core tests. Pass --godot to also build the Godot project
# through the editor (needs godot-mono on PATH).
set -euo pipefail
cd "$(dirname "$0")"

dotnet build Remnants.sln --nologo -v quiet
dotnet test tests/Remnants.Core.Tests --nologo --no-build -v quiet

if [[ "${1:-}" == "--godot" ]]; then
  godot-mono --headless --path game --build-solutions --quit
fi
