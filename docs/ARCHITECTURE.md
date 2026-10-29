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

- `Data/` — definition records (`ResourceDef`, `JobDef`, `BuildingDef`, `RecipeDef`, `RulesDef`), `GameData`
  (indexed + validated, `RecipesFor(building)`), `GameDataLoader` (reads `data/*.json`).
- `Survivors/` — `Survivor` (attributes, skills, health, assignment, needs, family ties, mood, growth), `Pregnancy` and `Body`
  (head, torso, arms, legs; healthy / injured with severity / missing / prosthetic).
- `Colony/` — `ColonyState` (plain serializable data), `ColonySim` (tick + command entry point),
  per-concern systems (production, crafting, medical, food, relationships, family, housing, growth), `JobOutput` (the output formula),
  `Training`, commands + handlers, `NewGame`. `ColonySim` optionally holds the `ScavengeData` (items, zones);
  expeditions, prosthetics and item recipes need it, and it cross-checks colony data that names items.
  - `ItemInventory` — whole items the colony owns (gear, consumables, prosthetics) by item id.
  - `Expedition` + `ExpeditionHandlers` — the loop with scavenge mode (see "Colony ↔ scavenge loop" below).
  - `Medical` (`Care` formulas, `MedicalSystem`, admit/discharge/fit-prosthetic handlers) and `Crafting`
    (`CraftingSystem`: production queues and quality rolls; queue/cancel/set-recipe/study-blueprint handlers) — see
    "Crafting tiers" below.
  - `Life` (`Bonds` formulas and queries, `RelationshipSystem`, `FamilySystem`, `GrowthSystem`, `Matchmake`) and
    `Relationship` (pair affinity + the player's `PairStance`) — see "Population life" below.
- `Random/DeterministicRng` — seeded xorshift64*; its state lives in `ColonyState` / `ScavengeState`.
- `Serialization/` — shared `System.Text.Json` options and `SaveGame` (versioned envelope).
- `Items/ItemDef` — item definitions: kind/slot, weight, armor/dodge/initiative/AP/sight/carry
  modifiers, a `MovementModifier`, attack `ActionDef`s, consumable `UseEffect`, `DeviceDef`, `BlueprintDef`, and for
  quality variants `BaseId`/`Quality`.
- `Scavenge/` — the tactical mode (headless):
  - `ScavengeData` + loader (items, enemies, zones, scavenge rules), `ScavengeDefinitions`. It derives the quality
    variants of every equippable item (`QualityVariant`, `QualityOf`).
  - `Devices` — field effects of equipped devices (signal jammer; scanner hook).
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
- `TownView` (plot grid as kerbed packed-earth lots, building nodes, plot picking; building names only for the hovered
  and selected building), `TownDressing` (purely visual scenery from the kit: noise-varied earth, a campfire on the
  commons, a loose fence of barriers/sandbags with a gate and a rutted road, dead trees, rubble and ruined shells
  outside), `SurvivorWalkers` (purely visual ambient life: workers circle their workplace with the odd break at home,
  idle adults potter near home or sit at the campfire, children play by the Town Hall, patients sit outside the Med
  Bay, infants stay home; long walks follow the gutters between plots, figures keep apart, at most 2x speed),
  `OrbitCamera` (WASD/arrows/middle-drag pan, wheel zoom, Q/E/right-drag rotate). `TownLayout.Commons` puts the
  campfire and playground just outside the grid beside the Town Hall, so they never take a plot.
- `ModelLibrary` — reads `assets/generated/manifest.json`; buildings map to `{type}_l{level}` (nearest
  lower level, then a placeholder box), survivors to `survivor_adult_male|female` / `survivor_child`
  (placeholder capsule), dressed by `SurvivorDressing` (below). `tools/sync-assets.sh [EXPORT_DIR]` refreshes `assets/generated/` from the
  remnants-assets `export/` (default `../../remnants-assets/main/export`).
- `SurvivorDressing` (shared by both views, purely visual): missing limbs hidden; prosthetics swapped in at the joint,
  `prosthetic_*_hook|peg_*` below quality 1, `*_cyborg_*` from there; equipped gear hung on the body's manifest
  `sockets` (the `item_{baseId}` model's `attach` point lands on the socket, under the part it rides on, turned by its
  `rot_deg`, scaled by chest height for bodies other than the adult male). Body → torso; weapon → hand, else back (a
  missing arm drops it to the back; a prosthetic arm holds it); tool → back, else chest, else hand. Colony survivors
  carry no gear in Core, so the colony shows prosthetics only.
- `scripts/HudTheme` — the one `Theme` for both screens' HUDs (opaque dark panels, buttons, scrollbars, font size), set
  on each HUD root and on dialogs (a `CanvasLayer` does not pass a theme on). Panels add content, not styles.
- `scripts/Colony/Ui/` — HUD panels (top bar with a beds/population warning, roster with partner/growth per line, a
  family section, mood and a matchmaking row, building, build menu, event log, `ExpeditionPanel`,
  `MedBayPanel`, `ProductionPanel`, `InventoryPanel`, `AfterActionDialog`). Read Core state, call the session; no rules. Where the UI
  needs a rule Core exposes a query (`MaxUpgradeLevel`, `MaxPartySize`, `HealPerTick`/`HealEta`, `IsWorsening`,
  `CraftEffortPerTick`, `CraftStatus`, `CanQueue`, `QualityChances`, `PreviewProfile` for a survivor's field profile with
  a given loadout).
- `ColonySession.Current` keeps the colony alive across scene changes, so a party can go to the scavenge scene and
  come back. Each `ColonyScreen` detaches its handlers when it leaves the tree.
- `scripts/ScavengeHandoff` — how the colony enters scavenge mode: `ColonySession.PlayExpedition` builds (or resumes)
  the expedition's `ScavengeSim` and `ScavengeHandoff.Begin` hands it to `ScavengeSession.Begin` with a callback that
  applies the result and returns to `Colony.tscn`, where the after-action report pops up. `UseAutopilot` (debug)
  resolves the run in place with `Autopilot` (walk to the nearest exit, extract) instead.
- The town is a grid of plots (`rules.layout`); `Construct` takes an optional plot.
- `scripts/Scavenge/ScavengeSession` — **the entry seam into scavenge mode** (static). `Begin(tree, sim, playerId,
  onFinished?, returnScene?)` hands a ready `ScavengeSim` to `scenes/scavenge/Scavenge.tscn` (or `Begin(tree, zoneId, seed,
  party)` builds one with `NewScavenge`). When the party is done and the player closes the summary, `End` stores
  `LastResult`, raises `Finished`, then calls `onFinished(result)` or changes to `returnScene` (default: main menu).
  "Leave" mid-run ends it early with `Complete = false`. Opening the scene directly plays a debug mission (`DebugParty`).
- `ScavengePresenter` — owns the sim for the local player, sends every command, reports `ScavengeError`s as feedback, and
  answers UI questions by asking Core (`Reachable`, `Combat.CheckReach` / `HitChance`, `VisibleTo`, explored tiles).
  `TakeNewEvents` hands each command's events (including the AI turns it set off) to the views for replay.
- `ScavengeScreen` builds the scene: `MapView` (models per tile, exits, fog: unexplored tiles hidden, remembered ones
  dimmed with a material overlay; framed move/attack/use highlights). Tile models are picked from manifest tags, so new
  variants drop in via `tools/sync-assets.sh`: ground = `tile`+`ground` (concrete base with patches of the others, picked by
  `GroundPattern` from a hash of the map, 85% base; patches are shaded toward a flat earth tone; the striped asphalt variant is left out), lone Cover tile = `cover_low`, paired
  Cover tiles = `wrecked_car`, stand-alone Wall tile = `cover_high`, wall runs = `wall_segment`, exits = `exit_marker`
  facing the map edge. `UnitViews` (survivor/enemy models,
  survivors dressed by `SurvivorDressing` with their gear and prosthetics, side rings; names only for the selected,
  hovered and acting units, an HP bar when focused or hurt; replays events with
  tweens and hit flashes, skipping what the local side cannot see), `TacticalCamera` (orthographic, yaw 45°, pitch
  35.264°; WASD/middle-drag pan, wheel zoom, Q/E rotate 90°; starts on the party, follows other units' visible actions).
  `SidePalette` colours rings, labels and turn chips alike. HUD in `Scavenge/Ui/`: `TurnBar` (round, initiative order as
  side-coloured chips, AP, End Turn), `UnitPanel` (HP, bleed-out, hurt body parts, movement modes named by `MoveMode.Source`, gear, load),
  `ActionPanel` (attack choice, use item, loot, recruit, extract), `ScavengeLog`, `ResultPanel` (`ExtractionResult`).
- Scavenge input: left-click a blue tile to move, a red target to attack with the chosen action, a unit to inspect it;
  item buttons arm "use on" and the next unit click is the target; right-click/Esc cancels; Space/Enter ends the turn.

## Rules of the road

- **State vs rules.** `ColonyState` is data only. Rules live in `ColonySim` and its systems.
  Save = serialize `ColonyState`; definitions are reloaded from `data/`.
- **Deterministic.** Fixed `Tick()`, no wall-clock, no `System.Random`. Randomness goes through
  `ColonySim.Rng`. Same seed + same commands = identical save JSON (tested).
- **Commands only.** The UI changes the sim only via `ColonySim.Execute(IColonyCommand)`
  (`AssignJob`, `Unassign`, `Construct`, `Upgrade`, `LaunchExpedition`, `ApplyExtractionResult`, `AdmitPatient`,
  `Discharge`, `FitProsthetic`, `SetRecipe`, `QueueRecipe`, `CancelOrder`, `StudyBlueprint`, `Matchmake`). Core validates and returns a `CommandResult`
  with a `CommandError` code. This is also the future client→server intent format.
- **Tick order:** production (+training) → crafting → medical → food → relationships (mood, affinity, couples) →
  family (births, conception) → housing → growth (aging, growing up).
- **Scavenge commands** (`Move`, `Attack`, `UseItem`, `PickUp`, `Recruit`, `EndTurn`, `Extract`) carry a
  `PlayerId` + `UnitId`; Core checks ownership and whose turn it is and returns a `ScavengeResult`
  with a `ScavengeError`. Several players' parties can share one instance (each party is a side).
  A party is done when it has no conscious controllable unit left (its downed die, recruits stay);
  the instance finishes when every party is done.

## Colony ↔ scavenge loop

- **Launch** (`LaunchExpedition(zoneId, members)`): adults only, at home and not patients; party size ≤
  `rules.expedition.partySizeByTownHallLevel[TownHall-1]`; each member's equipment (by slot) and pack come out of
  the colony inventory; `rationFoodPerMember` food per member is taken from the stockpile. One expedition at a time.
  Members are `Away`: no job, **they do not eat colony food** (the rations cover them), they are not treated and
  their wounds do not change, but they keep their bed. The expedition stores a seed drawn from the colony RNG.
- **Run**: `ColonySim.OpenExpeditionScavenge()` builds the `ScavengeSim` from the members' *current* survivors
  (attributes, skills, health, body incl. injuries and prosthetics) and their gear, as player
  `ColonySim.ExpeditionPlayerId`, and keeps its live `ScavengeState` in `Expedition.Scavenge`. Called again (after
  a load, or after the scavenge screen's Leave) it resumes that state. Colony time does not pass during a mission.
- **Apply** (`ApplyExtractionResult(result)`, only once `Complete`): returned survivors get the result's body,
  health, attributes and skills (field gains stick), worn gear goes back to the inventory, carried items too except
  items with `salvage`, which become stockpile resources (scrap → Scrap Metal, med supplies → Medicine). Party
  members not returned are dead: removed for good, logged, added to `ColonyState.Memorial`; their gear is lost.
  Recruits join as new adults (age from `recruitAgeRange`) without a bed, so the homeless grace timer applies.
  `ColonyState.LastReport` holds the after-action report (returned with new injuries, dead, recruits, loot beyond
  what the party took out, lost gear).
- **Saving mid-mission**: the colony save (version 2+) carries the expedition and its `ScavengeState`, validated
  against `items.json`/`zones.json` on load; version 1 saves still load. The scavenge screen has no save of its own:
  Leave suspends the run (the result is not final, the party stays out) and the colony can then save; Expedition →
  Play mission resumes it.

### Care and prosthetics

- `AdmitPatient` puts an injured survivor in a Med Bay bed (`levels[].patients`); patients do not work and are
  discharged automatically once healed. Each tick a patient uses `medicinePerPatientTick` Medicine and every wound
  loses `healPerTick · level.outputMultiplier · (unstaffedCare + Σ medic performance)` severity (medic performance is
  the `medicine` job's output factors). Medics train only while treating. Without Medicine a patient heals at the
  natural rate and does not worsen.
- Outside the Med Bay wounds below `naturalHealMaxSeverity` heal slowly; wounds at or above `worsenThreshold` worsen:
  a limb reaching severity 1 is lost, head/torso wounds drain health (death goes to the memorial).
- `FitProsthetic(survivor, part, item)` needs a missing limb, a prosthetic item of that region (`items[].prosthetic`:
  region, quality, `minMedBayLevel`) in the inventory and a Med Bay of that level. The part's efficiency becomes the
  item's quality — above 1 for cybernetics — which feeds job output (body-region weights), scavenge movement range
  (leg efficiency, plus `scavenge.json` body rules such as two good prosthetic legs → +1 range) and attacks (arms).
- Sources: recipes (Workshop: Herbal Remedy → Medicine, Peg Leg, Hook Arm, Med-kit at L2; Electronics Lab: Servo Arm,
  Cybernetic Leg/Arm from a blueprint), loot (`med_supplies`, Servo Arm, Cybernetic Leg/Arm) and the starting inventory
  (`rules.newGame.items`).

## Population life

All numbers live in `rules.json` (`life`, `relationships`, `family`, `mood`, `food.infantPerTick`) and `jobs.json`
(`childrenAllowed`). The time scale is for fun: growing up takes a few in-game weeks, not years.

- **Relationships**: every tick each pair of single adults at home gains affinity
  `(base + sharedWorkplace + sharedHome + compatibility · similarity) · (encouraged ? encourageMultiplier : 1)`
  (similarity = 1 − mean attribute difference / 100); at `coupleThreshold` the closest ready pair becomes a couple
  (any sexes). `Matchmake(a, b, stance)` lets the player encourage, discourage (no gain; splits a couple) or reset a pair.
- **Births**: every `conceptionCheckTicks` a woman and man in a couple, both at home, she not pregnant and without
  an infant, conceive with `conceptionChance` when a bed is free for the baby (beds > people + pregnancies) and the
  stock holds `minFoodPerPerson` food per person. The baby's aptitude (parents' mean ± `inheritVariation`) is rolled
  at conception; after `pregnancyTicks` an infant is born with attributes at `birthAttributeFraction` of it and a name
  from `family.maleNames/femaleNames`. Pregnant survivors stay home from expeditions.
- **Growing up**: infant → child after `infantTicks`, child → adult `childTicks` later (`GrowthTicks`). Each fed tick
  moves attributes `attributeGrowthPerTick` of the way to the aptitude; hungry ticks do not and are counted
  (`MalnourishedTicks`), so malnourished children grow up weaker (noted when they come of age). Infants eat
  `infantPerTick`, children `childPerTick`, and both are fed before adults. Infants cannot work; children only do
  `childrenAllowed` jobs (Farming, Scrap Sorting, Crafting) at `childOutputMultiplier`, training at
  `training.childMultiplier` — they grow into what they do. A minor's age shown in years is their share of `adultAge`.
- **Mood**: 0–100, starts at 100, recovers `recoveryPerTick`. A death costs the partner `partnerGrief` (and logs
  grief) and parents/children `familyGrief`; a partner leaving or a break-up costs `heartbreak`. Job output (and
  medic care) is multiplied by `lerp(lowMoodMultiplier, 1, mood/100)`.
- **Housing pressure**: beds are spread over housing buildings (`Survivor.HomeBuildingId`); people keep their home
  and newcomers move in with their partner or parents where there is room. Newborns and recruits without a bed start
  the homeless grace timer. When it runs out an adult leaves; an infant or child leaves with a parent who is at home
  (mother first), ending that parent's couple; with no parent at home the child is taken in by passing travelers.
  Starvation still applies to everyone.
- **Saves**: version 3 adds partners, parents, mood, pregnancies, growth, homes and `ColonyState.Relationships`.
  Version 2 (and 1) saves load: their children are placed on the growth track by age (no aptitude, so they change
  only by training) and everything else starts empty/content.

## Crafting tiers

- **Buildings**: Workshop (tools, simple weapons, prosthetics), Tailor (cloth, clothing, light armor, bandages,
  climbing gear), Kitchen (meals, canned rations), Armory (blades, guns, riot armor, Linked gear), Electronics Lab
  (circuits, binoculars, scanner, jammer, evac beacon, servo and cybernetic limbs), House (10/14/20 beds). Each has 3
  levels; the Town Hall caps levels as before, and `minTownHallLevel` gates construction (Armory, Electronics Lab,
  House need Town Hall 2). Each production building has its own job: crafting (Intellect+Creativity), tailoring
  (Creativity+Agility), cooking (Creativity+Perception), gunsmithing (Intellect+Strength+Perception, Firearms skill),
  electronics (Intellect+Creativity). Effort per tick is the usual job output formula, and working trains the job.
- **Recipes** (`data/recipes.json`): building, min level, work, resource `inputs` and item `itemInputs` (any quality of
  that item counts, worst first), one item or resource output with an amount, optional `minSkill` (some worker needs
  that much of the job's skill) and optional `blueprint`.
- **Queue**: each production building holds up to `crafting.maxQueue` orders (`QueueRecipe(building, recipe, count |
  repeat)`). Each tick it works one order: the first already paid, else the first that can start (skill + inputs), which
  is then paid. A finished batch decrements a counted order (removed at zero); a repeating order goes to the back, so it
  runs forever without blocking others. `CancelOrder` refunds a paid batch (the same items back). `SetRecipe` is the
  old shortcut: the queue becomes one repeating order.
- **Quality**: crafted gear (any equippable item) rolls a tier: `score = best worker skill/100 + (level−1) ·
  qualityPerLevel ± qualitySpread` (uniform), the highest tier whose `crafting.qualityThresholds` it reaches. Tiers
  live in `scavenge.json` `quality` (Crude ×0.8 stats / ×1.15 weight, Standard = the item as defined, Fine ×1.25 /
  ×0.9); `ScavengeData` derives `{item}@{tier}` variants that scale armor, damage, positive dodge/initiative, sight and
  carry bonuses. Variants are ordinary item ids, so inventory, expeditions, profiles and saves need nothing special.
- **Blueprints**: `Blueprint` items (`blueprint.studyAt` + `minLevel`) drop from caches in tier 2–3 zones (the zone
  loot table is every item with a loot weight at or below the zone's loot tier). `StudyBlueprint(item)` uses one up at
  a study building of that level and adds it to `ColonyState.KnownBlueprints`; recipes naming it can then be queued.
- **Kitchen meals**: `food.meals` (best first) are eaten before raw food; one unit covers `foodValue` food of the need
  and adds `moodPerTick` / `healthPerTick`. Simple Meals turn 4 food into 6 food's worth; Hearty Stew (L2) adds more
  mood and healing.
- **Gear in the field**: crafted gear is the same item data as loot, so it drives `ProfileDeriver` (riot armor sets King
  1, climbing gear cheapens rubble, a grappling hook adds the knight jump, quality scales the numbers). Devices: a
  **signal jammer** in the Tool slot jams Linked within its radius (they stop taking cover; M8's squad AI should also
  lose reinforcements and shared targets there). A **scanner** gives sight; `Devices.ScannedBy` is the hook for seeing
  caches/units through walls, not used by the scavenge view yet. Ammo is not modelled (guns do not use it).
- **Saves**: version 4 stores queues, `NextOrderId` and `KnownBlueprints`. Older saves migrate: a building's
  `recipeId`/`craftProgress`/`craftPaid` becomes one repeating order, and the old generic `blueprint` item becomes
  `bp_scanner` (`SaveGame.LegacyBlueprintItem`).
- **UI**: selecting a production building shows `ProductionPanel` (queue with progress and cancel, recipe picker with
  ×1/×5/repeat, locked reasons, inputs, quality odds). The top bar's Items button opens `InventoryPanel` (items by kind
  and quality, studied blueprints, Study buttons). Buildings render `{type}_l{level}` models, falling back to the
  nearest lower level.

## Data files

- `resources.json` — resource ids and names.
- `jobs.json` — per job: skill + weight, attribute weights, body-region weights, tool bonus, `childrenAllowed`.
- `buildings.json` — category, job, produced resource, base output, `minTownHallLevel`, and per-level
  cost / worker slots / output multiplier / beds / patients. Level 1 cost = construction cost.
- `recipes.json` — what production buildings make (see "Crafting tiers").
- `rules.json` — tick/time scale, output formula constants, training rates, food, housing grace,
  meals, crafting (queue limits, quality roll), expedition (party size per Town Hall level, rations, recruit ages), medical (heal/worsen rates, medicine use),
  life (growth stages and rates), relationships (affinity), family (conception, pregnancy, baby names), mood,
  new-game setup (counts, age/stat ranges, stock, starting items, starting buildings, names).
- `items.json` — weapons (actions), clothing/armor, tools (movement modifiers, carry, sight),
  consumables (med-kit, bandage, evac beacon), materials (with `salvage` into colony resources), prosthetics
  (region, quality, Med Bay level), devices (scanner, signal jammer), blueprints (study building and level);
  tier + loot weight for caches; `natural` for enemy body parts (never looted or crafted). Every other item has a
  recipe or a loot weight (tested).
- `enemies.json` — hp, AI kind, attributes, skills, equipment (a Husk's claws are an item too).
- `zones.json` — size, threat, loot tier, terrain densities, spawn/exit edges, enemy count + mix,
  caches, NPC count.
- `scavenge.json` — terrain table, movement derivation (base pattern/range, Agility per range,
  encumbrance steps, body rules such as "missing leg → King 1"), AP, sight, carry, combat numbers,
  AI, NPC names, escort radius, gear quality tiers.

## Running

```sh
./run-tests.sh            # build solution + run Core tests
./run-tests.sh --godot    # also build through the Godot editor headless
godot-mono --path game    # play (main menu → Colony shows a new Core colony, Scavenge a debug mission)
godot-mono -e --path game # open in the editor
```

Data lookup (`GameDataPaths`): `data/` next to the executable in exported builds (lets players override
balancing), the repo's `data/` (`res://../data`) in dev, else the copy embedded in `Remnants.Game.dll`
at build time — so exports work with nothing copied.

Debug flags (after `--`): `--colony` skips the menu, `--scavenge` starts the debug mission (`--autoplay=N` plays N
orders with a simple bot so enemy turns replay, `--zoom=SIZE` sets the ortho camera size), `--demo` builds a farm and woodcutter and assigns
the adults, `--loop-demo` sends the adults on an expedition to the outskirts (scavenge screen, or resolved in place with
`--autopilot`) and builds a Med Bay and Workshop, `--family-demo` does `--demo`, encourages the first man and woman
and fast-forwards until a baby is born (then selects the mother), `--crafting-demo` builds a grown town (Town Hall 3, every
production building, some at level 2–3) with staffed, queued Armory and Tailor and opens the Armory and item panels, `--expedition` opens the expedition planner with two adults picked, `--screenshot=PATH [--frames=N] [--quit]` saves the viewport as PNG. Needs a display; on
Wayland pass `--display-driver wayland --disable-vsync` so an unfocused window still renders:

```sh
godot-mono --display-driver wayland --disable-vsync --path game -- --colony --demo --screenshot=/tmp/colony.png --frames=300 --quit
```
