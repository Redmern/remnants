# Performance

Baselines and the perf pass of 2026-10-31. "Before" is `main` at 2203dc4 (every system merged), "after" is the
perf-pass branch on top of it. Machine: Ryzen (24 threads), Radeon RX 6600, 1920x1080, Linux/Wayland. Other agents
were building and running Godot on the same machine during the runs, so absolute numbers move by ±20% between runs;
every comparison below is A/B back-to-back, best of two.

## How to measure

Core (headless, no Godot):

```
dotnet build -c Release tools/Remnants.Bench
dotnet tools/Remnants.Bench/bin/Release/net8.0/Remnants.Bench.dll [colony] [save] [scavenge] [--seed=2] [--days=120] [--runs=60]
    [--write-save=PATH]   # the grown colony, for the game's --load-save
    [--loop=DAYS]         # keep ticking afterwards, for a profiler (dotnet-trace collect -- dotnet ...Bench.dll colony --loop=1500)
```

- **colony**: the playtest bot grows seed 2 to day 120 (20 survivors, 19 buildings, power, ~200 pantry lots, production
  queues, raids on), saves, and times 30 in-game days of pure `Tick` from the load (after a JIT warm-up).
- **save**: seed 2 grown to day 90; `SaveGame.Save` / `Load` (with validation against the data) best of 5.
- **scavenge**: `ZoneTrial` runs on the biggest zones (`network_relay` 32x32, `core_spire` 34x34) with the tech kit
  (hacking kit + scout drone), and two co-op parties with drones on `core_spire`. "End turn" is the wall-clock of the
  `EndTurn`/`SetStance` command, i.e. every AI turn until a player unit is up again.

Godot (needs a display; the window may sit on a hidden workspace, hence `--disable-vsync`):

```
DISPLAY=:0 godot-mono --path game --disable-vsync [--max-fps 60] --resolution 1920x1080 -- --keep-window \
    --perf-log --perf-seconds=30 [--perf-every=5] [--perf-hitches] \
    --colony --load-save=/path/colony120.json --hour=12|21 [--weather=thunderstorm] --speed=4
DISPLAY=:0 godot-mono ... -- --keep-window --perf-log --perf-seconds=30 --perf-hitches --scavenge --zone=core_spire --hour=23 --autoplay=80
```

`PerfMonitor` (`game/scripts/PerfMonitor.cs`) prints `[perf]` lines: wall-clock frame time (mean / p95 / p99 / max,
frames over 16.7 / 20 / 33 ms), draw calls, primitives, nodes, objects, orphans, static / managed / video memory, and a
summary at the end (then quits). `--perf-hitches` adds one line per frame over 16.7 ms with the GCs and colony ticks in
it and the time of any `PerfMonitor.Measure("label")` spans (the scavenge screen measures `execute`, `sync`, `hud`).
With vsync on a hidden workspace Godot drops to 1 fps; use `--disable-vsync`, and `--max-fps 60` for a 60 Hz run.

## Core

| | before | after |
|---|---:|---:|
| Colony tick, mean | 0.71 ms | **0.39 ms** |
| Colony tick, p95 | 1.61 ms | **0.48 ms** |
| Colony tick, max (a raid/day rollover) | 6.7 ms | 5.3 ms |
| Sim per in-game day | 17.1 ms | **9.5 ms** |
| Allocated per tick | 467 KB | **142 KB** |
| Gen0 GCs per 30 days | 21 | 7 |
| Sim cost at 4x (8 ticks/s) | 5.7 ms/s | 3.2 ms/s (0.3% of a core) |
| Save day 90 | 297 KB, 0.65 ms | same |
| Load day 90 (validated) | 4.2 ms (8.5 ms first) | same |
| AI end turn, network_relay tech: mean / p95 / max | 1.8 / 7.1 / 13.4 ms | **1.3 / 5.0 / 8.4 ms** |
| AI end turn, core_spire tech | 3.6 / 12.4 / 21.6 ms | **2.5 / 8.6 / 14.7 ms** |
| AI end turn, core_spire 2 co-op parties + drones | 6.9 / 28.0 / 65.7 ms | **4.5 / 17.6 / 35.4 ms** |

(Before the rebase onto the Story pacing pass the colony tick went 0.89 → 0.21 ms; its new medical/story rules made
the base tick dearer, the gain holds.)

All three targets were already met before the pass (tick far under a frame, enemy turns < 200 ms, save/load < 1 s);
the pass cut the tick in half and its garbage by two thirds, and AI turns by a third.

What was slow:
- `FoodSystem` was 72% of a tick: `Open` sorted every lot of an item to pick one, `Pantry.Reconcile` and `Prune`
  rebuilt id lists and copied the lot dictionary on every call (several per tick per survivor).
- `PerkEffects.For` (called per survivor per system: power efficiency, XP, output, crafting) made three dictionaries
  and walked the cybernetics tree with an `OrderBy` every time.
- `ProfileDeriver.Derive` was 42% of AI time: `Detection.Spots` derives observer and target profiles for every pair,
  and sight checks (`SideSees`, `Network.Known`, `Visibility.SideVisible`, AI position scoring) call it in loops.

What changed (results byte-identical: the playtest suite — 4 sandbox seeds x 120 days, a Story game, every zone with
the tech kit, co-op drones on core_spire — prints the same before and after, and all tests pass unchanged):
- `Pantry.FirstLot` finds the lot to open by one scan with the same order as `Candidates`; `Reconcile` returns early
  when the lots already match the inventory; `Prune` only copies when something is empty.
- `PerkEffects.For` makes its maps only when something goes in them (else the shared `None`) and skips the
  cybernetics walk for bodies without a prosthetic.
- `ProfileDeriver.Memo(data)` remembers each unit's profile for the length of a read-only query (no unit changes
  inside); used by `SideSees`, `Network.Known`, `Visibility.SideVisible` and `ScavengeAi.Reposition`'s scoring.

## Godot

Uncapped frame time at 1920x1080 (30 s each, best of two A/B runs; before / after):

| Scene | mean | p99 | max | draws (mean) | frames > 20 ms |
|---|---:|---:|---:|---:|---:|
| Colony day, 4x, 20 walkers | 5.80 / 5.82 ms | 7.14 / **6.37** | 47.9 / 46.0 | 2301 / **2158** | 2 / 1 |
| Colony night (lamps, torches, fires), 4x | 5.82 / 5.83 | 7.52 / **6.38** | 24.0 / **19.0** | 2315 / **2173** | 1 / 0 |
| Colony night thunderstorm, 4x | 5.90 / 5.91 | 7.71 / **6.92** | 22.1 / 22.4 | 2287 / **2148** | 2 / 1 |
| Scavenge core_spire day, autoplay | 3.55 / 3.54 | 5.49 / 5.47 | 68.3 / 65.7 | 1492 / 1491 | 34 / 26 |
| Scavenge core_spire night (fog), autoplay | 2.95 / 2.94 | 5.00 / 4.97 | 82.4 / **59.5** | 1243 / 1242 | 36 / 34 |
| Scavenge network_relay storm, autoplay | 3.22 / 3.22 | 5.65 / 5.60 | 53.0 / 53.9 | 1000 / 1001 | 16 / 13 |
| Colony day 4x, **capped at 60 fps** | 60 fps / 60 fps | 16.75 / 16.73 | 46.0 / 45.3 | | 3 / 2 |

On a quieter machine earlier in the day the colony ran at 430–600 fps (1.6–2.3 ms a frame) and scavenge at 900–1000
fps; with other Godot instances sharing the GPU the frame is GPU-bound (~6 ms), so the CPU savings show in p99, max
and draw calls more than in the mean. **Colony at 4x holds 60 fps** (target met before and after); the one 45 ms
frame per run is the first tick after loading (portraits render, the HUD builds).

Memory over a long 4x run (10 minutes capped at 60 fps, ~200 in-game days): 60 fps throughout, one frame over 33 ms;
static memory 153 MB → 162 MB in the first two minutes (portrait and glyph caches) and then flat at 165 MB; nodes
~5000 and objects ~13 000 steady, 7 orphans throughout, managed heap a 27–42 MB sawtooth. **No leak.**

Where a colony frame goes (dotnet-trace of the running game, managed time): `CharacterRig._Process` (advancing the
AnimationTrees of ~20 survivors) ~60%, `SurvivorWalkers` 14%, `DayNightLighting.Apply` 6%, and the tick frames' HUD
refresh. A tick frame (8 a second at 4x) cost ~6 ms of managed time, 84% of it the HUD: survivor cards re-set every
label, tooltip and style, resource chips re-applied theme colour overrides (each one relayouts), the defense view
walked every fence prop.

What changed (purely visual code, nothing the sim sees):
- `Hud.Unchanged`: sets a label's text, tooltip, texture, visibility, font colour or modulate only when it differs from
  what it last set. Used by `SurvivorCard`, `ResourceChip` and the `TopBar` chips; `StatBar`, `Portrait` and the card's
  selection skip their redraw / restyle when nothing changed. 140 fewer canvas draw calls per frame.
- `DefenseView` hides fence props under defenses only when the defenses' cells changed.
- `SurvivorWalkers.Separate` reads each figure's position once instead of twice per pair (n² engine calls).
- `WorldTags` (scavenge): pop-ups are a managed list instead of node metadata read back every frame; stack keys and
  signatures without per-frame LINQ/string churn; positions and visibility set only on change.
- `MapView` pools the tile highlight quads and labels with one shared mesh (it made a new `PlaneMesh` and node per tile
  on every HUD refresh). `StancePanel` keeps its buttons while the acting unit, AP and stance are the same.

## Scavenge command frames (UI/UX pass, stage 1)

What a command cost: `ActionPanel`, `TechPanel`, `CoopPanel` and `LootPanel` freed and rebuilt every button on every HUD
refresh, `UnitPanel` its whole gear card, and `RefreshHud` ran several times in one frame (a replay step, the sync and a
selection change). Now the order panels keep their rows (`PanelRows`: same row, same kind → `ActionButton.Configure` in
place; extras hidden), the gear card and the loot panel rebuild only when their signature changes, and the HUD refreshes
once per frame (deferred). `PerfMonitor`'s summary now ends with `span-frames` (the frames that ran a measured span) and
one `span LABEL: n mean p95 max` line per label.

Headless A/B (`--headless ... --perf-log --perf-seconds=40 --perf-hitches --scavenge --zone=Z --hour=12 --autoplay=200`,
the same seed and commands; before = e2df81b with the same PerfMonitor). Headless has no GPU work, so these are the CPU
side of a command frame. `sync` no longer contains the HUD (it is deferred), so compare hud + sync:

| core_spire | before | after |
|---|---:|---:|
| hud, mean / p95 / max (n=63) | 4.05 / 9.30 / 12.5 ms | **2.69 / 6.75 / 12.4 ms** |
| sync, mean / p95 / max (n=34) | 5.17 / 6.38 / 6.6 ms (incl. hud) | **1.25 / 2.06 / 3.0 ms** |
| frames with a span: mean / p95 | 10.7 / 20.1 ms | **9.8 / 15.8 ms** |
| frames over 16.7 ms in 40 s | 6 | **3** |

| network_relay | before | after |
|---|---:|---:|
| hud, mean / p95 / max (n=47) | 4.14 / 8.26 / 16.0 ms | **2.55 / 4.84 / 12.2 ms** |
| sync, mean / p95 / max (n=26) | 5.12 / 7.44 / 7.9 ms | **1.34 / 1.82 / 2.9 ms** |
| frames with a span: mean / p95 | 10.0 / 22.0 ms | **8.9 / 17.5 ms** |
| frames over 16.7 ms in 40 s | 6 | **3** |

The frames still over budget are AI turns: `execute` (every AI unit's turn until a player unit is up) is up to 40–50 ms
on core_spire. It runs on the main thread inside the command; moving it to a worker needs the per-frame readers
(`WorldTags._Process`, `UnitViews._Process` walk `State.Units`) to read a snapshot first. That and spreading AI turns over
frames (they are replayed one by one anyway) is the next lever.

Windowed runs now go through `tools/godot-agent.sh` (a hidden workspace); there Godot only draws once a second, so
wall-clock frame times need a visible window (see "How to measure") and the stage-1 numbers above are headless.

## UI/UX pass, stage 2 (merged rework)

Stage 2 kept every scavenge HUD panel in place (stance, mission, turn-bar order chips, loot, pings, world-tag chips; the
unit card's gear rows only on a gear or pack change, skipped while folded) and froze the order panels during enemy turns.
Same headless A/B as above, back to back, e2df81b vs the merged branch:

| Scavenge, 40 s autoplay | core_spire before → after | network_relay before → after |
|---|---:|---:|
| hud mean / p95 / max | 4.31 / 10.44 / 14.3 → **1.13 / 3.51 / 9.8 ms** | 4.44 / 9.42 / 18.2 → **1.19 / 3.61 / 11.4 ms** |
| sync mean / p95 | 5.55 / 7.91 (incl. hud) → **1.64 / 3.87 ms** | 5.63 / 8.12 → **1.65 / 2.09 ms** |
| hud + sync per command, mean | 9.9 → **2.8 ms** | 10.1 → **2.8 ms** |
| frames with a span: mean / p95 | 11.7 / 33.1 → **9.4 / 14.2 ms** | 10.7 / 24.8 → **8.5 / 17.3 ms** |
| frames over 16.7 ms in 40 s | 7 → **4** | 8 → **3** |

The frames still over budget are the AI turns (`execute` p95 46 ms / max 54 ms on core_spire, unchanged): see "Not done".

Colony at 4× with UI-heavy panels open, headless (`--headless -- --perf-log --perf-seconds=30 --colony FLAGS --speed=4`;
CPU: sim ticks, HUD refresh and layout; the headless loop is capped near 145 fps, so the mean sits at 6.9 ms and the
hitches are the story):

| Colony 4×, 30 s | p99 before → after | max before → after | frames > 16.7 ms | frames > 20 ms |
|---|---:|---:|---:|---:|
| Expedition planner open, 2 picked (`--crafting-demo --expedition`) | 11.6 → **6.96 ms** | 43.3 → **18.4 ms** | 38 → **1** | 36 → **0** |
| Garage open (`--garage-demo`) | 7.63 → **6.94 ms** | 33.1 → **18.4 ms** | 38 → **1** | 22 → **0** |
| Kitchen recipe book (`--food-demo --food-view=kitchen`) | 13.2 → **6.94 ms** | 32.1 → **18.1 ms** | 7 → **1** | 1 → **0** |
| Armory with a queue (`--crafting-demo`) | 7.66 → **6.94 ms** | 21.6 → **17.8 ms** | 3 → **1** | 1 → **0** |

The one frame over 16.7 ms left in each is the first tick after loading (portraits render, the HUD builds). Windowed
60 Hz numbers need a visible window; agents run Godot on a hidden workspace, which presents about once a second.

## Not done / next

- **Scavenge AI turns**: see "Scavenge command frames" above: `execute` is the last part of a command frame over budget.
- **Animation**: `CharacterRig` is the biggest per-frame cost and scales with frame rate (each near figure advances its
  AnimationTree every frame). LOD exists (`UpdateEvery` by zoom and frustum); a pose-rate cap (e.g. 60 Hz) would halve
  it on fast monitors.
- **Draw calls**: ~2200 in the colony (sun shadows double the scene's), 1000–1500 in scavenge. Fine on this GPU;
  MultiMesh for town dressing (rubble, fence posts, dead trees) and tiles would be the lever on weaker ones.
- Core has headroom; `RaidSystem.Schedule` / `ThreatModel` and `Power.Run` are the next items in a tick profile.
