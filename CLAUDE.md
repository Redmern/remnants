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

## Lessons from the combat playtest fixes (fleet task, 2026-11)

- **Tuning:** before tuning balance numbers, list the behaviour rules that must not be traded for a number. Reaching a metric by breaking a rule (enemies skipping walkers to cut fizzles) is a failed tune; revert it and report the miss.
- **Metrics:** define each metric in the spec (for example, what counts as a "planned action" in the fizzle share). The event log only records successful blows, so an undefined share was overstated until review.
- **Review:** run a fresh read-only review of the final fix diff just before delivery. It found real problems in the last round of the combat work.
- **Commits:** gate a commit on the test result. Do not chain `git commit` after tests with `;`.
- **Coverage:** keep a metric test for the playtest counters, and find a way to stage enemy behaviour on screen. Most enemy behaviour was never seen in the last task.
- **Hidden window:** `tools/godot-agent.sh` needs `HYPRLAND_INSTANCE_SIGNATURE`. If it is unset, look under `/run/user/1000/hypr/` and export it before falling back to headless-only. Say what was not seen on screen.
