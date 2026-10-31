#!/usr/bin/env bash
# Run godot-mono for automated testing WITHOUT disturbing the user's desktop.
#
# On Hyprland, windowed runs are launched on the hidden special workspace
# "special:agents" with no initial focus, so they never appear on the user's
# screen or steal focus. Headless runs (--headless) and non-Hyprland sessions
# run directly.
#
# Usage: tools/godot-agent.sh [godot-mono args...]   (same args as godot-mono)
# Output: the run's stdout/stderr is printed when it finishes; exit code is preserved.
# Env:    GODOT_AGENT_TIMEOUT=seconds (default 600)
set -uo pipefail

headless=0
for a in "$@"; do [[ "$a" == "--headless" ]] && headless=1; done

if [[ $headless -eq 1 || -z "${HYPRLAND_INSTANCE_SIGNATURE:-}" ]] || ! command -v hyprctl >/dev/null; then
  exec godot-mono "$@"
fi

run_dir="${XDG_CACHE_HOME:-$HOME/.cache}/remnants-agent-runs/$(date +%s)-$$-$RANDOM"
mkdir -p "$run_dir"
inner="$run_dir/run.sh"
{
  echo '#!/usr/bin/env bash'
  printf 'cd %q\n' "$PWD"
  printf 'godot-mono'; printf ' %q' "$@"; printf ' > %q 2>&1\n' "$run_dir/log"
  printf 'echo $? > %q\n' "$run_dir/rc"
} > "$inner"
chmod +x "$inner"

hyprctl eval "hl.exec_cmd('$inner', { workspace = 'special:agents silent', no_initial_focus = true })" >/dev/null

timeout_s="${GODOT_AGENT_TIMEOUT:-600}"
for ((i = 0; i < timeout_s * 4; i++)); do
  [[ -f "$run_dir/rc" ]] && break
  sleep 0.25
done

[[ -f "$run_dir/log" ]] && cat "$run_dir/log"
if [[ ! -f "$run_dir/rc" ]]; then
  echo "godot-agent: timed out after ${timeout_s}s" >&2
  pkill -f -- "$run_dir" 2>/dev/null
  exit 124
fi
rc=$(cat "$run_dir/rc")
rm -rf "$run_dir"
exit "$rc"
