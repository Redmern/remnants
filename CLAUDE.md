# REMNANTS: agent rules

## Never disturb the user's desktop

The user works on this PC while agents run. **Never launch a visible Godot window directly.**

- Use `--headless` whenever no rendering is needed (builds, self-tests, sims).
- Anything that needs a real window (screenshots, perf captures, smoke runs, UI checks) MUST go through
  `tools/godot-agent.sh <same args as godot-mono>`. On Hyprland it runs the game on the hidden workspace
  `special:agents` with no focus, then prints the log and returns the exit code.
- Never call `godot-mono` without `--headless` directly, and don't use `--keep-window` or other flags that
  bring windows forward.
- The repo scripts (`tools/smoke-playthrough.sh`, `tools/ui-screenshots.sh`, and similar) already route through the wrapper. Use them.
