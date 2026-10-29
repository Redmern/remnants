# Architecture

See `PLAN.md` for the design. This file is the short "where does what live" map.

## Projects

| Path | What | Depends on |
|------|------|------------|
| `src/Remnants.Core` | All game rules. Pure C#, **no Godot**. Deterministic. | — |
| `tests/Remnants.Core.Tests` | xUnit tests for Core, run headless. | Core |
| `game/` | Godot 4.7 C# project: presentation + input only. | Core |
| `data/` | JSON definitions loaded by Core. Balancing is a data edit. | — |

Everything targets **net8.0** (GodotSharp 4.7.2 ships net8.0; Core must load inside Godot).
`global.json` pins the .NET 10 SDK, which builds net8.0 fine.

## Core layout

- `Data/` — definition records (`ResourceDef`, `JobDef`, `BuildingDef`, `RulesDef`), `GameData`
  (indexed + validated), `GameDataLoader` (reads `data/*.json`).
- `Survivors/` — `Survivor` (attributes, skills, health, assignment, needs) and `Body`
  (head, torso, arms, legs; healthy / injured with severity / missing / prosthetic).
- `Colony/` — `ColonyState` (plain serializable data), `ColonySim` (tick + command entry point),
  per-concern systems (production, food, housing, aging), `JobOutput` (the output formula),
  `Training`, commands + handlers, `NewGame`.
- `Random/DeterministicRng` — seeded xorshift64*; its state lives in `ColonyState` / `ScavengeState`.
- `Serialization/` — shared `System.Text.Json` options and `SaveGame` (versioned envelope).
- `Items/ItemDef` — item definitions: kind/slot, weight, armor/dodge/initiative/AP/sight/carry
  modifiers, a `MovementModifier`, attack `ActionDef`s, consumable `UseEffect`.
- `Scavenge/` — the tactical mode (headless):
  - `ScavengeData` + loader (items, enemies, zones, scavenge rules), `ScavengeDefinitions`.
  - `TileMap`/`Pos` (terrain grid, exits, symmetric Bresenham LOS), `MapGenerator` (zone template +
    seed → rooms, scatter, exits, carved route), `NewScavenge` (places parties, enemies, caches, NPCs).
  - `Unit` (party survivor, enemy or NPC: side, owner player, body, gear, pack, AI memory).
  - `ProfileDeriver` — **the core of the mode**: attributes + equipped gear + body state →
    `UnitProfile` (`MovementProfile` with chess-like modes King/Rook/Bishop/Queen/Knight and terrain
    costs; `ActionProfile`s; AP; initiative; sight; carry; armor; dodge). Pure and never stored.
  - `Movement` (reachable tiles per mode, distance fields), `Combat` (reach, cover, hit chance,
    body-part injuries, limb loss, Downed → bleed-out → Dead), `Visibility` (per-side fog + explored).
  - `ScavengeSim` — `Execute(IScavengeCommand)`; initiative turn order across all units of all
    sides; AI turns (`ScavengeAi`: Husk, Linked stub, recruit followers) run inside it.
  - `ExtractionResult` per player — returned survivors (with injuries/health), recruits, deaths, loot.
  - `ScavengeSave` — versioned save/load with validation against `data/`.

## Game layout (`game/`)

- `scripts/Colony/ColonySession` — presenter: owns the `ColonySim`, runs fixed ticks at pause/1x/2x/4x
  (`SecondsPerTick` real seconds per tick at 1x), sends every command, saves/loads `user://colony.json`.
- `scripts/Colony/ColonyScreen` — builds the scene in code and wires views to the session.
- `TownView` (ground, plot grid, building nodes, plot picking), `SurvivorWalkers` (purely visual
  home ↔ work walking), `OrbitCamera` (WASD/arrows/middle-drag pan, wheel zoom, Q/E/right-drag rotate).
- `ModelLibrary` — reads `assets/generated/manifest.json`; buildings map to `{type}_l{level}` (nearest
  lower level, then a placeholder box), survivors to `survivor_adult_male|female` / `survivor_child`
  (placeholder capsule). `tools/sync-assets.sh [EXPORT_DIR]` refreshes `assets/generated/` from the
  remnants-assets `export/` (default `../../remnants-assets/main/export`).
- `scripts/Colony/Ui/` — HUD panels (top bar, roster, building, build menu, event log). Read Core state,
  call the session; no rules. Where the UI needs a rule (Town Hall cap) Core exposes a query.
- The town is a grid of plots (`rules.layout`); `Construct` takes an optional plot.

## Rules of the road

- **State vs rules.** `ColonyState` is data only. Rules live in `ColonySim` and its systems.
  Save = serialize `ColonyState`; definitions are reloaded from `data/`.
- **Deterministic.** Fixed `Tick()`, no wall-clock, no `System.Random`. Randomness goes through
  `ColonySim.Rng`. Same seed + same commands = identical save JSON (tested).
- **Commands only.** The UI changes the sim only via `ColonySim.Execute(IColonyCommand)`
  (`AssignJob`, `Unassign`, `Construct`, `Upgrade`). Core validates and returns a `CommandResult`
  with a `CommandError` code. This is also the future client→server intent format.
- **Tick order:** production (+training) → food → housing → aging.
- **Scavenge commands** (`Move`, `Attack`, `UseItem`, `PickUp`, `Recruit`, `EndTurn`, `Extract`) carry a
  `PlayerId` + `UnitId`; Core checks ownership and whose turn it is and returns a `ScavengeResult`
  with a `ScavengeError`. Several players' parties can share one instance (each party is a side).
  A party is done when it has no conscious controllable unit left (its downed die, recruits stay);
  the instance finishes when every party is done.

## Data files

- `resources.json` — resource ids and names.
- `jobs.json` — per job: skill + weight, attribute weights, body-region weights, tool bonus.
- `buildings.json` — category, job, produced resource, base output, and per-level
  cost / worker slots / output multiplier / beds. Level 1 cost = construction cost.
- `rules.json` — tick/time scale, output formula constants, training rates, food, housing grace,
  new-game setup (counts, age/stat ranges, stock, starting buildings, names).
- `items.json` — weapons (actions), clothing/armor, tools (movement modifiers, carry, sight),
  consumables (med-kit, bandage, evac beacon), materials; tier + loot weight for caches.
- `enemies.json` — hp, AI kind, attributes, skills, equipment (a Husk's claws are an item too).
- `zones.json` — size, threat, loot tier, terrain densities, spawn/exit edges, enemy count + mix,
  caches, NPC count.
- `scavenge.json` — terrain table, movement derivation (base pattern/range, Agility per range,
  encumbrance steps, body rules such as "missing leg → King 1"), AP, sight, carry, combat numbers,
  AI, NPC names, escort radius.

## Running

```sh
./run-tests.sh            # build solution + run Core tests
./run-tests.sh --godot    # also build through the Godot editor headless
godot-mono --path game    # play (main menu → Colony shows a new Core colony)
godot-mono -e --path game # open in the editor
```

Data lookup (`GameDataPaths`): `data/` next to the executable in exported builds (lets players override
balancing), the repo's `data/` (`res://../data`) in dev, else the copy embedded in `Remnants.Game.dll`
at build time — so exports work with nothing copied.

Debug flags (after `--`): `--colony` skips the menu, `--demo` builds a farm and woodcutter and assigns
the adults, `--screenshot=PATH [--frames=N] [--quit]` saves the viewport as PNG. Needs a display; on
Wayland pass `--display-driver wayland --disable-vsync` so an unfocused window still renders:

```sh
godot-mono --display-driver wayland --disable-vsync --path game -- --colony --demo --screenshot=/tmp/colony.png --frames=300 --quit
```
