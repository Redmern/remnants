# Architecture

See `PLAN.md` for the design. This file is the short "where does what live" map.

## Projects

| Path | What | Depends on |
|------|------|------------|
| `src/Remnants.Core` | All game rules. Pure C#, **no Godot**. Deterministic. | — |
| `src/Remnants.Net` | Multiplayer: protocol, TCP/loopback transports, per-side fog (`SideFog`), the authoritative `GameHost`, the client `NetClient`. No Godot. See `MULTIPLAYER.md`. | Core |
| `src/Remnants.Server` | Headless console server for one shared scavenge instance (and a `--bot` client). | Net |
| `tests/Remnants.Core.Tests` | xUnit tests for Core, run headless. | Core |
| `tests/Remnants.Net.Tests` | Protocol round-trips, scripted 2-player loopback games, a localhost TCP game. | Net |
| `tools/Remnants.Playtest` | Headless bot playthroughs of a fresh colony and zone trials, for balancing. See `BALANCE.md`. | Core |
| `game/` | Godot 4.7 C# project: presentation + input only. | Core, Net |
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
  `Training`, commands + handlers, `NewGame`, `Advice` (first-time tips: the next sensible step for a colony state). `ColonySim` optionally holds the `ScavengeData` (items, zones);
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
  - `Devices` — field effects of equipped devices (signal jammer; scanner).
  - `Detection` — who sees whom inside sight: stealth vs detection, flares, reveals, scanners, contacts (see "Fog of war").
  - `FogLog` — which events a side hears and how (hidden actors become anonymous); shared by single player and `SideFog`.
  - `Alliances` (teams of players as one shared side: offers, merges, breaks with a warning round) and `CoopActions`
    (give an item, carry / put down the downed, team pings) — see "Co-op" below.
  - `TileMap`/`Pos` (terrain grid, exits, symmetric Bresenham LOS), `MapGenerator` (zone template +
    seed → rooms, scatter, exits, carved route), `NewScavenge` (places parties, enemies, caches, NPCs).
  - `Unit` (party survivor, enemy or NPC: side, owner player, body, gear, pack, AI memory).
  - `ProfileDeriver` — **the core of the mode**: attributes + equipped gear + body state →
    `UnitProfile` (`MovementProfile` with chess-like modes King/Rook/Bishop/Queen/Knight and terrain
    costs; `ActionProfile`s; AP; initiative; sight; carry; armor; dodge; stealth; detection; brace cover). Pure and never stored.
  - `Movement` (reachable tiles per mode, distance fields; the first step of a move always fits, so a range-1 unit can
    slog onto rubble or water), `Combat` (reach, cover, hit chance,
    body-part injuries, limb loss, Downed → bleed-out → Dead; ambushes), `Visibility` (per-side sight + explored, lit tiles).
  - `ScavengeSim` — `Execute(IScavengeCommand)`; initiative turn order across all units of all
    sides; AI turns (`ScavengeAi`: Husk, Linked squad, recruit followers) run inside it. See "Linked squad AI" below.
  - `Hacking` (devices, the Hack action, Override, placement) and `Drones` (scout drones) — see "Hacking and scout drones".
  - `Network` — the Linked relay graph (groups, shared knowledge, squad focus, pylon alarm and reinforcements).
  - `ExtractionResult` per player — returned survivors (with injuries/health), recruits, deaths, loot.
  - `ScavengeSave` — versioned save/load with validation against `data/`.

## Game layout (`game/`)

- `scripts/Colony/ColonySession` — presenter: owns the `ColonySim`, runs fixed ticks at pause/1x/2x/4x
  (`SecondsPerTick` real seconds per tick at 1x), sends every command, saves/loads `user://colony.json`. `Phase` +
  `PhaseChanged` and `TickProgress` serve the day/night presentation (see "Day and night").
- `scripts/Colony/ColonyScreen` — builds the scene in code and wires views to the session.
- `TownView` (plot grid as kerbed packed-earth lots, building nodes, plot picking; building names only for the hovered
  and selected building), `TownDressing` (purely visual scenery from the kit: noise-varied earth, a campfire on the
  commons, a loose fence of barriers/sandbags with a gate and a rutted road, dead trees, rubble and ruined shells
  outside), `SurvivorWalkers` (purely visual ambient life: workers circle their workplace with the odd break at home,
  idle adults potter near home or sit at the campfire, children play by the Town Hall, patients sit outside the Med
  Bay, infants stay home; long walks follow the gutters between plots, figures keep apart, at most 2x speed),
  `OrbitCamera` (WASD/arrows/middle-drag pan, wheel zoom, Q/R/right-drag rotate; speeds from `GameSettings`). `TownLayout.Commons` puts the
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
- `scripts/HudTheme` — the one `Theme` for every screen's HUD: worn-metal 9-slice panels, bevelled buttons with hover /
  pressed / disabled / focus states, tabs, lists, sliders, tooltips, scrollbars, progress bars, the bundled fonts; type
  variations (`Card`, `CardSelected`, `Inset`, `Chip`, `Toast` panels; `Title`, `Display`, `Heading`, `Muted`, `Small`,
  `Number` labels; `Primary`, `Flat`, `Tab` buttons). Set on each HUD root and overlay (a `CanvasLayer` does not pass a
  theme on). Panels pick a variation and add content, not styles.
- `scripts/Hud/` — the HUD component library and design tokens, shared by all screens:
  - `Palette` (colour tokens: rust/amber for the player, moss good, red danger; **cyan only for the AI Network**),
    `Fonts` (Barlow body, Barlow Condensed display/numbers, OFL, in `game/ui/fonts` with their licences), `PanelArt`
    (procedural grain/bevel/rivet 9-slice textures), `Fmt` (number formatting), `Motion` (reveal/press/flash tweens).
  - `Icons` — line icons by id from `game/ui/icons/{group}_{id}.svg` (24×24, white 2px stroke, tinted; imported with the
    "keep" importer so exports ship the SVG), rasterised at 2× with mipmaps; fallbacks per call, then `item_generic`.
    `Icons.Item` prefers a rendered gear icon `assets/generated/icons/{baseItemId}.png` when the asset pipeline adds one.
  - Components: `Widgets` (labels, section headers, icon buttons with the hotkey in the tooltip, chips, cost chips that
    mark what is missing, `Pips`), `StatBar`, `ResourceChip`, `Portrait`, `Toasts`, `ConfirmDialog`, `Overlay` (dimmed
    modal base) with `PauseMenu` and `SettingsPanel`, `ProfileChips` (a field profile as chips).
  - `PortraitStudio` — survivor portraits rendered at runtime from the 3D models (prosthetics, and for field units
    their gear) in a private `SubViewport`, one per frame, cached by look; lives on the tree root.
  - `GameSettings` — `user://settings.cfg`: UI scale (root `ContentScaleFactor` = auto by window height × the
    setting), window mode/size, vsync, master/music/sfx/ambience bus volumes (buses made at boot), mute when unfocused,
    visual sound cues, camera pan/zoom speed.
    `Hotkeys` installs the InputMap actions (Esc menu, Space pause, 1/2/3 speed, B build, E expedition, I items,
    L log, Tab next survivor, Q/R colony camera rotation; Space/Enter end turn in scavenge). `GameSettings.Boot` runs
    once from whichever scene starts first and also attaches `DebugCapture`.
- `scripts/MainMenu` + `MenuBackdrop` — the title screen over a throwaway grown colony circled by a slow camera:
  Continue (the session's colony, else the save), New Game, Load, Multiplayer, Settings, Quit, debug skirmish.
- `scripts/Colony/Ui/` — `ColonyHud` lays the colony HUD out and owns its hotkeys: `TopBar` (clock, speed, resource
  chips with the change over the last day and what makes/eats each, population / beds / food-days pressure chips,
  Build / Items / Expedition / World map / Menu), `RosterPanel` of `SurvivorCard`s (portrait, job, health and mood
  bars, badges: injured / worsening, patient, pregnant, away, starving, homeless with its timer; collapsible),
  `SurvivorPanel` (tabs Overview with the job output formula as a tooltip, Skills, Body, Family with matchmaking),
  `BuildingPanel` (level pips, worker portraits, upgrade cost chips and Town Hall cap), `BuildMenu` (category filter,
  grid with costs, locks and tooltips), `EventLog` (filterable, collapsible feed with icons; first-time tips from
  `Advice`; raises toasts for deaths, births, departures, a party's arrival and return, lost limbs, recruits),
  `ExpeditionPanel`, `WorldMapPanel`, `MedBayPanel`, `ProductionPanel`, `InventoryPanel`, `AfterActionDialog` (who came
  home with injury chips, who was lost, recruits, loot). Esc closes the topmost panel, then opens the pause menu
  (resume, save, load, settings, main menu, quit). Read Core state, call the session; no rules. Where the UI
  needs a rule Core exposes a query (`MaxUpgradeLevel`, `MaxPartySize`, `HealPerTick`/`HealEta`, `IsWorsening`,
  `CraftEffortPerTick`, `CraftStatus`, `SkillGap`, `CanQueue`, `QualityChances`, `PreviewProfile` for a survivor's field profile with
  a given loadout, `FoodPerDay`/`FoodDays`, `OutputPerTick`); the per-survivor output breakdown uses `JobOutput`'s
  public factors.
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
- `ScavengeDriver` — `IScavengeDriver`, where the screen's game runs: `LocalDriver` (an in-process `ScavengeSim`) or
  `RemoteDriver` (a multiplayer `NetClient`; its `Sim` is a read model of the server's fog-filtered view, rebuilt per
  snapshot, and it raises `Pushed` when other players act). `ScavengeSession.Begin` takes a sim or a driver.
- `ScavengePresenter` — plays the local player through the driver, sends every command (awaited: remote commands
  return after the server's snapshot), reports `ScavengeError`s as feedback, and
  answers UI questions by asking Core (`Reachable`, `Combat.CheckReach` / `HitChance`, `VisibleTo`, explored tiles).
  `TakeNewEvents` hands each command's events (including the AI turns it set off) to the views for replay.
- `Scavenge/Fog/FogOverlay` — the fog's map layer (purely visual, rebuilt on sync): contact markers
  (`contact_marker`, tinted amber, red for where shots came from), overwatch eye rings (`overwatch_marker`) and, for the
  local side's watchers, a soft amber ring at their sight radius shown only while that unit is selected or hovered
  (`SetFocus`), a red ring on revealed hostiles, burning flares (`item_flare_lit` + a light + the lit disc), the local
  side's motion sensors (`item_motion_sensor` + radius), and a red incoming-fire streak from the source to the victim
  when one of ours is ambushed. Its text (contact `?`, OVERWATCH / BRACED / LOOKOUT / REVEALED, AMBUSH!) is drawn by
  the HUD's `WorldTags`. `Scavenge/Ui/StancePanel` — Overwatch (O) / Brace (B) / Lookout (L) buttons for the acting
  unit. Flares and sensors are "use" items that target a tile (highlighted in range); attack previews say
  `AMBUSH 85% · 8–14` when the target's side cannot see the attacker. Units show only when the side sees them
  (`IScavengeDriver.SeenUnits`), not merely their tile. `--fog-demo` stages an ambush with a unit on overwatch
  (`Fog/FogDemo`, `ScavengeScreen.FogDemo`), with `--screenshot=PATH --quit` saving the moment.
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
  `SidePalette` colours rings, labels and turn chips alike (other players' parties violet). Units that leave a remote
  view are hidden; the turn bar shows whose turn it is and the turn timer in multiplayer. HUD in `Scavenge/Ui/`, laid out
  by `ScavengeHud` (which also owns the hotkeys and the pause menu): `TurnBar` (zone, round, initiative order as
  portrait chips with a side-colour edge, the acting unit's AP pips, End Turn, Leave, menu), `UnitPanel` (cards: portrait
  rendered from the unit's model with its gear, HP bar and AP pips; Body with part icons and bars; field profile chips;
  gear, pack and load), `ActionPanel` and `StancePanel` built from `ActionButton`s (icon, detail line, AP-cost pips, hotkey
  keycap), `WorldTags` (everything written over the map as HUD chips in one stack per unit — name and HP bar, status
  tags, attack preview with hit chance and AMBUSH, rising pop-ups — so nothing overlaps; contacts' `?` on their tiles),
  `ScavengeLog` (icon per event kind; only Network events are cyan), `ResultPanel` (`ExtractionResult` in the
  after-action style). Portraits of field units come from `UnitPortrait`.
- Scavenge input: left-click a blue tile to move, a red target to attack with the chosen action, a unit to inspect it;
  item buttons arm "use on" and the next unit click is the target; right-click/Esc cancels (Esc with nothing to cancel
  opens the pause menu); Space/Enter ends the turn; 1–3 pick the attack, 4–6 aim an item, O/B/L stances, F loot,
  T recruit, X extract.

- `scripts/Multiplayer/` — `MultiplayerMenu` (scene `Multiplayer.tscn`: name, address, Host / Join, lobby with Ready),
  `MultiplayerSession` (the party from the colony's waiting expedition or a debug party, the connection, the in-process
  host, auto-reconnect, applying the result to the colony), `NetPump` (posts network callbacks onto the main thread).
  See `MULTIPLAYER.md`.

- `scripts/Audio/` — presentation only, no rules. `AudioManager` is the one autoload (`project.godot`): it makes the
  buses (`AudioBuses`: Master with compressor + limiter → Music, SFX (→ UI, World), Ambience; Music and Ambience duck
  under stings), reads `assets/generated/audio/manifest.json` (`AudioManifest`: id, category, file, variants, loop
  points, default volume, tags, bpm, stems; synced from remnants-assets `export/audio/`) and the event map
  `game/audio/sound_events.json` (`SoundEventMap`: dotted event names → sound ids, falling back by dropping the last
  part; per-id tuning — bus, volume, pitch/volume jitter, cooldown, voice limit, 3D attenuation, duck; music states;
  ambience; zone → tags → loops; weather loops). `Fire(event, pos?)`, `Play(id)`, `PlayAt(id, pos)` play pooled
  voices with a random variant; an id the manifest lacks stays silent and is logged once (`PlaceholderSounds`
  synthesises a few UI blips and two stings until the files land). Retuning is a data edit.
  - `MusicDirector` — states menu, colony_day/night/tension, world_map, scavenge_explore/combat, after_action,
    coop_lobby; crossfades on the next bar (bpm from the manifest), never restarts a state's track, stems by
    `Intensity`, `linked_layer` on top, one-shot stings (`music.extraction_sting`).
  - Scene hooks, one node each: `ColonyAudio` (colony events at their buildings, day/night bed — the one day/night
    decision is `ColonyAudio.IsNight` —, campfire, work loops at staffed buildings, listener height follows zoom,
    music state), `ScavengeAudio` (each replayed event via `ScavengeScreen`'s replay step, so sound lands with the
    animation: attacks by action, moves/deaths/hits by who, barks, overwatch shots, contacts, hacked devices by
    kind, drone hum; zone ambience; combat/explore music and the Linked layer), `UiAudio` (buttons, overlays, side
    panels and toasts hooked as nodes enter the tree; menu and lobby music), `SoundCues` (accessibility: chips at the
    screen edge toward off-screen ambush/contact/alarm sounds), `WeatherAudio` (`Set(kind, intensity)`,
    `Lightning(distance)`, `Gust()`; nothing drives it yet).
  - `--audio-trace` logs every fired event and its sound; `--audio-selftest` (run by `run-tests.sh --godot`) checks
    every Core event type is mapped and replays a scripted colony, an autopiloted scavenge run and the hack/drone
    events, failing when key events pick the wrong id; `--weather=KIND[:INTENSITY]` sets the weather bed.

## Rules of the road

- **State vs rules.** `ColonyState` is data only. Rules live in `ColonySim` and its systems.
  Save = serialize `ColonyState`; definitions are reloaded from `data/`.
- **Deterministic.** Fixed `Tick()`, no wall-clock, no `System.Random`. Randomness goes through
  `ColonySim.Rng`. Same seed + same commands = identical save JSON (tested).
- **Commands only.** The UI changes the sim only via `ColonySim.Execute(IColonyCommand)`
  (`AssignJob`, `Unassign`, `Construct`, `Upgrade`, `LaunchExpedition`, `ApplyExtractionResult`, `AdmitPatient`,
  `Discharge`, `FitProsthetic`, `SetRecipe`, `QueueRecipe`, `CancelOrder`, `StudyBlueprint`, `Matchmake`, `ChoosePerk`,
  `StartStudy`, `CancelStudy`, `SetNightShift`, `InstallFloodlight`). Core validates and returns a `CommandResult`
  with a `CommandError` code. This is also the future client→server intent format.
- **Tick order:** production (+training) → crafting → study → medical → food → day/night (sleep, night work, evenings) →
  relationships (mood, affinity, couples) →
  family (births, conception) → housing → growth (aging, growing up).
- **Scavenge commands** (`Move`, `Attack`, `UseItem` (on a unit, or a tile with `At`), `PickUp`, `Recruit`, `SetStance`
  (Overwatch / Brace), `Lookout`, `EndTurn`, `Extract`; co-op: `ProposeAlliance`, `RespondAlliance`, `BreakAlliance`,
  `PingTile`, `GiveItem`, `Carry`, `PutDown`; tech: `Hack`, `DeployDrone`, `FlyDrone`, `RecallDrone`, `MarkTarget`,
  `DropPayload`) carry a
  `PlayerId` + `UnitId`; Core checks ownership and whose turn it is and returns a `ScavengeResult`
  with a `ScavengeError`. Several players' parties can share one instance (each party is a side).
  A party is done when it has no conscious controllable unit left (its downed die, recruits stay or go over to an ally),
  unless an ally is still up to carry its downed out; the instance finishes when every party is done.

## Colony ↔ scavenge loop

- **Launch** (`LaunchExpedition(zoneId, members)`): adults only (and `NewScavenge` rejects a child in any party, so
  debug and server parties cannot carry one either), at home and not patients; party size ≤
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
  the homeless grace timer (it waits while they are away on an expedition). When it runs out an adult leaves; an infant or child leaves with a parent who is at home
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
  runs forever without blocking others. When nothing can go because every order needs more skill than any worker has,
  the workers **practise** (train as if working), so a fresh colony with no skills grows into gated recipes;
  `ColonySim.SkillGap` says which skill, how far off and how long. `CancelOrder` refunds a paid batch (the same items back). `SetRecipe` is the
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
  lose reinforcements and shared targets there). A **scanner** gives sight and detection, and its side sees hostiles
  within its radius through walls and stealth (see "Fog of war"). Ammo is not modelled (guns do not use it).
- **Saves**: version 5 adds `ColonyState.Zones` and the expedition's road (`travelTicks`, `arrivalTick`, `depletion`,
  `pendingResult`, `returnTick`); version 4 and older load with zones as the data starts and an expedition already in
  its zone. Scavenge saves are version 2 (alarm, reinforcement waves, suppression); version 1 loads. Colony version 6 /
  scavenge version 3 add the fog of war (see "Fog of war").
- **Saves (v4)**: version 4 stores queues, `NextOrderId` and `KnownBlueprints`. Older saves migrate: a building's
  `recipeId`/`craftProgress`/`craftPaid` becomes one repeating order, and the old generic `blueprint` item becomes
  `bp_scanner` (`SaveGame.LegacyBlueprintItem`).
- **UI**: selecting a production building shows `ProductionPanel` (queue with progress and cancel, recipe picker with
  ×1/×5/repeat, locked reasons, inputs, quality odds). The top bar's Items button opens `InventoryPanel` (items by kind
  and quality, studied blueprints, Study buttons). Buildings render `{type}_l{level}` models, falling back to the
  nearest lower level.

## Fog of war

Scavenging is played half-blind: you see tiles, not necessarily who stands on them. Everything is in Core
(`Detection`, `Combat`, `ScavengeSim`, `FogLog`), tuned in `scavenge.json` `fog`, deterministic (no rolls: the same
positions and gear always give the same answer), and per side.

- **Stealth vs detection** (`ProfileDeriver`): stealth = `base + (Agility − 50)·perAgility − load·perKg + gear stealth +
  enemy stealth + (leg efficiency − 1)·legEfficiency` (rag coat +3, scout jacket +4, camo cloak +15, riot armor −10; loot
  is loud; hurt or peg legs are loud; the Linked +25, Husks −2). Detection = `(base + (Perception − 50)·perPerception +
  gear detection + lookout/overwatch bonus) · head efficiency` (binoculars +10, scanner +15).
- **Seen** (`Detection.SideSees`): a hostile in a side unit's sight (range + line of sight) is seen when
  `detection − stealth − (distance − closeRange)·perTile ≥ 0`; within `closeRange` always. A side also sees everyone on a
  tile a **flare** lights (every side does), a unit **revealed** to it (muzzle flash, motion sensor) while one of its
  units has line of sight within `revealSightMultiplier` × its sight, and hostiles within a working **scanner**'s radius
  (through walls and stealth). The AI uses the same rules (`Detection.Spots`; Network sharing; Husk spotting).
- **Contacts** (`ScavengeState.Contacts`, per side, no identity, up to `contactFuzz` tiles off, gone after
  `contactRounds`): gunfire heard within the action's `noise` (Noise), unseen hostiles moving within `hearMovement`
  (Movement), where an attack on one of the side's units came from (Attack, with `Toward` = the victim), a motion sensor
  going off (Sensor). Only party sides keep contacts; the AI hears through `MakeNoise` as before.
- **Ambush** (`Combat.AmbushAgainst`): an attack whose target's side does not see the attacker as it resolves gets
  `ambush.hitBonus`, ignores the target's (positive) dodge and multiplies damage by `damageMultiplier`. A **braced** target
  gives no bonus; an ally next to the target ("watching each other's backs") removes `backsReduction` of it. Afterwards
  the attacker is revealed to each side it hit at for the action's `reveal` rounds (guns 2, melee and zappers 1), those
  sides get an Attack contact, other sides in earshot a Noise contact, and overwatch may react. Previews
  (`Combat.ChanceToHit`) include it. Attacking a unit your side cannot see fails with `NotVisible`.
- **Defenses**: `SetStance(Overwatch)` (1 AP; ends the turn; the AP left become reactions, max 2): a free reaction attack
  (−`accuracyPenalty`) at each hostile that moves into, or reveals itself (by firing, or in a flare) within, its sight and
  reach, until its next turn; reactions do not set off reactions; +10 detection meanwhile. `SetStance(Brace)` (1 AP; ends
  the turn): no ambush bonus against it, +`coverBonus` cover, `nextTurnApPenalty` AP less next turn. `Lookout` (1 AP;
  does not end the turn): +sight and +detection (`detectionBonus + Perception·detectionPerPerception`) until its next
  turn. Stances and lookout end when the unit's next turn starts.
- **Gear** (`items.json`): Flare (consumable, `UseItem` at a tile within 5: lights radius 3 for 3 rounds; Electronics
  Lab L1, loot), Motion Sensor (placed on a tile within 1: reveals and privately alerts its side (`ScavengeEvent.Side`)
  when a hostile moves into radius 3; Electronics Lab L1, loot tier 2), Camo Cloak (body, +15 stealth; Tailor L2, loot
  tier 2), Scanner (sees hostiles within 6 through walls and stealth), Binoculars (+10 detection).
- **The Linked** (`ScavengeAi`): a Linked that knows of nobody waits on overwatch (`fog.ai.linkedLurk`) instead of
  wandering, and one with AP left after closing in does too; position scoring adds `fog.ai.hiddenWeight` for tiles no
  known hostile would detect it on and scores shots with the ambush bonus, so they fire from the fog; drones (Perception
  85) spot for the group; a signal jammer cuts a Linked off from that shared knowledge. Husks do not plan ambushes but
  can still stumble out of the dark.
- **Log** (`FogLog`): the sim stamps each event with the party sides that saw its actor at that moment
  (`ScavengeEvent.SeenBy`); an event about a target the side never saw is dropped, and one whose actor it did not see
  then shows only in its `Anonymous` form
  ("Ana was ambushed from the fog (+20% hit)", "Ana was hit in the Torso for 12 from the fog"), or not at all; side-only
  events stay with their side. The local driver and the server's `SideFog` both use it.
- **Saves**: scavenge save version 3 (stances, lookout, contacts, reveals, flares, sensors); colony save version 6 carries
  it; older saves load with none of it (co-op state came with versions 4 / 7, see "Co-op").
- Scenario tests: `FogTests` (stealth derivation, detection range, lookout, NotVisible, ambush + reveal + contact +
  anonymous log, brace, backs, overwatch reactions incl. on a revealed ambusher, flares, sensors, scanner, noise
  contacts, contact expiry, Linked lurking, drone spotting with and without a jammer, determinism, save migration);
  `FogNetTests` (no-leak multiplayer cases).

## Co-op

Players scavenge together as teams (details, protocol and how to run it: MULTIPLAYER.md "Co-op and alliances").

- **A team is a side.** Allied parties share `Party.Side` = `team:N` (`Unit.TeamSide`; alone, `player:N`), and every
  unit a player owns carries it. Everything keyed by side then works for the team with no special cases: hostility
  (`Unit.HostileTo`, so no friendly fire, overwatch ignores allies, AoE spares them), sight and detection (`Visibility`,
  `Detection`), contacts, reveals, sensors, explored tiles, "watching each other's backs", the log (`FogLog`: an ally's
  events are the team's). Ownership, turns, loot and results stay on `Unit.PlayerId`.
- `Alliances`: `Merge` moves two sides' parties, units, contacts, reveals, sensors, pings and explored tiles onto a new team
  side (`ScavengeState.NextTeamId`); `Split` takes one player off (a team left with one dissolves), copying what the team
  knew to each side and handing sensors / pings back by owner. Offers (`ScavengeState.Proposals`) need mutual sight and
  an answer on the other player's turn; breaks (`ScavengeState.Breaks`) take effect at the start of round announced +
  1 + `breakWarningRounds`. Round start (`Alliances.RoundStarted`) lapses offers, fades pings and applies breaks.
  Alliance events carry `ScavengeEvent.Sides` (sent to exactly those sides).
- `CoopActions`: give (into the other unit's pack, so it goes home with its player), carry (`Unit.CarryingId` /
  `CarriedById`: the carried unit shares the carrier's tile, `UnitAt` skips it, `ProfileDeriver` takes
  `carryRangePenalty` off the carrier's move and drops special moves; `Sync` keeps it on the carrier and lets go when the
  carrier falls), pings (`ScavengeState.Pings`, per side, limited per turn by `Unit.PingsUsed`).
- `ScavengeSim.UpdateParties` keeps an all-downed party in play while an ally is up (not one whose player has left for
  good: `ScavengeSim.Abandon`, called by the host, sets `Party.Left`); `Extract` takes the carried unit
  (home to its own player) and every follower on the side within the escort radius, re-owned to the extractor.
- `NewScavenge`: `PartySetup.Team` starts parties allied and spawned together; `EnemyScale` multiplies the enemy count
  and per-type caps by `1 + enemiesPerExtraPlayer × (players − 1)`.
- Net: `SideFog` follows the party's live side (team fog, split at the break), filters pings / offers / breaks;
  `GameHost` has `MissionMode` (Versus / Coop) and `SetTeam`, and turns alliance changes into the alliance messages for
  the players involved. Game: `Palette.Ally` / `SidePalette.Ally`, `CoopPanel` (Team), `AllianceBanner`, the Ally card
  section, ping pins in `FogOverlay`, lobby mode / team, Expedition → Online / Co-op.
- **Saves**: scavenge save version 4 (offers, breaks, pings, next team id, carrying, pings used, sensor owner, party left) and colony
  save version 7 carry it; older saves load with everyone on their own side and nobody carried.
- Tests: `CoopTests` (no friendly fire and overwatch, team fog and contacts, backs, offers and answers, decline and
  lapse, break timing and split sight, carry out and per-player results, put down / let go, left behind, give, heal an
  ally, per-player extraction and recruit hand-over, pings, co-op spawn and enemy scaling, saves); Net: `CoopNetTests`.

## Hacking and scout drones

Tuned in `scavenge.json` (`hacking`, `devices`, `drone`, `world.droneScout`), items (`hackTool`, `drone`), zones
(`devices`: [min, max] per kind) and two device enemies (`camera`, `turret`). Core: `Scavenge/Hacking.cs`, `Scavenge/Drones.cs`.

- **Devices** (`ScavengeState.Devices`, `ZoneDevice`): terminal, camera, turret, blast door, locker, relay node, placed by
  `Hacking.PlaceZoneDevices` with their own RNG after everything else (doors in room doorways, cameras in walls next to
  floor, the rest on floor; nothing that blocks a tile may cut off part of the map). A camera or turret is also a unit
  (`UnitKind.Device`, `Unit.DeviceId`): sight, fire, fog and being shot work as for anyone, its side is its controller,
  shot down the device is `Disabled`. Cameras are Linked-kind (they relay on the Network and are jammed like the Linked)
  and take no turns; turrets take turns and shoot the nearest hostile they see (`ScavengeAi.Machine`). Other devices block
  their tile (`Hacking.Blocks`, used by `Movement` and the AI's distance field); a door lets its controller's side through.
- **Hack** (`Hack(deviceId | targetId)`): a hacking tool carried anywhere (kit: adjacent; cyberdeck: 3 tiles with line
  of sight, +15%, 2 progress per action), `hacking.apCost` less `hack_ap_discount`. Each action adds progress for the
  side; the one that completes the kind's `work` rolls `Hacking.Chance` = base + Hacking·perSkill + (Intellect−50)·perIntellect
  + tool + `hack_chance_bonus` − difficulty − the Network penalty (unless a jammer hostile to the Network covers it).
  Success: terminal → its items into the pack, every tile explored for the side, sensor contacts on every unseen enemy;
  camera → sees for the side; turret → fires for it; door → the side passes, others are locked out; locker → its loot as a
  cache on its tile; relay → a jam field (`devices.Relay.radius`) against the Network (`Devices.JamsTile`) and no more
  reinforcements anywhere (`Hacking.UplinkDown`). Failure: noise in `alarmRadius` (less `hack_alarm_reduction`) with noise
  contacts for parties in earshot; for a device on the Network (not jammed) every Linked hunts the hacker and the
  Network's alarm counter rises; a feedback shock (`shockDamage`, halved by `field_repair`) to the right arm; progress lost
  and a `lockoutRounds` lockout for the side. Hacking trains the Hacking skill and adds `Unit.FieldXp`.
- **Override** (`can_hack_linked`): `Hack(targetId)` on a Linked enemy (not a Husk: disconnected) rolls once with
  `overrideDifficulty`; success puts it on the hacker's side for `overrideRounds` (it fights for them, off the Network,
  `Unit.OverrideUntilRound`), failure is an alarm with the Linked turning on the hacker.
- **Cameras spot**: an enemy camera that picks out a hostile after a move logs it and points every Linked within its
  radius at it (`Hacking.CameraCheck`).
- **Scout drone** (`UnitKind.Drone`): `DeployDrone(at)` on a free tile next to the operator (the item leaves the pack or
  Tool slot); the drone is a unit of the operator's side with the `drone` frame (sight ~12, dodge, low HP), takes no turns
  and is not player-controlled. `FlyDrone(to)` (operator's AP): up to the item's `flyRange` over any terrain, ending on a
  passable free tile, within the control range (item + Drone Piloting·`controlPerSkill` + `drone_range_bonus`) of the
  operator. Battery (item + `drone_battery_bonus`) drops 1 per round (`jammedDrain` in any jammer's field, where it flies
  1 tile); flat, or with its operator down or gone, it lands and is left as an item. `RecallDrone` within control range puts
  it back (it recharges `rechargePerRound`; `minBattery` to take off again); extracting brings it home. Melee cannot reach
  it, Husks ignore it, the Linked focus it; destroyed it is lost. `MarkTarget` (`drone_can_mark`): a hostile the drone sees is
  seen by the side through walls and stealth and hit at +`markHitBonus` for `markRounds` (`ScavengeState.Marks`).
  `DropPayload` (`drone_payload`): a carried flare or motion sensor is set off on the drone's tile. Drone sight is side
  sight, so allies share it.
- **Field XP**: `ReturnedSurvivor.FieldXp` (hacks, flights) is paid out on top of the mission XP when the party is home.
- **World map** (`SendScoutDrone(zone, pilot?)`, `WorldMap.SendDrone`): a drone from the inventory flies to a rumored zone
  within `maxTravelDays` (its battery), reports in after `travelDays · daysPerTravelDay` (at least 6 h): the zone is scouted and
  its neighbours rumored, as after a visit but with no depletion; over the zone it may be shot down
  (`lossByThreat[threat]`, less `lossReductionPerSkill` per pilot Drone Piloting point, relative); otherwise it is back in the
  inventory after the flight home plus `rechargeDays`. The pilot trains Drone Piloting and earns XP.
  `ColonyState.DroneFlights`.
- **Saves**: scavenge save version 5 (devices, marks, hooks, field XP, drones, batteries, overrides) and colony version 9
  (drone flights; carries scavenge v5); older saves load with no devices, no drones out and no perks in the field.
- **Game**: `TechPanel` (Hack H per device in reach with chance and progress, Deploy / Recall K, Fly J, Mark M, Payload P;
  Deploy and Fly arm a click on a highlighted tile), `DeviceViews` (`device_{kind}` with `_hacked` / `_open` /
  `_hacked_open` / `_disabled`, a `hack_marker` and progress arc for the side's hack, `drone_mark_marker` on marked targets),
  `DeviceTip` (hover: state, owner, what a hack does), the drone card with battery pips on `UnitPanel`, camera / turret /
  `scout_drone` unit models, log icons, and Send scout drone on the world-map card. `--tech-demo=hack|turret|drone` (with
  `--scavenge`) stages each for screenshots.
- Tests: `HackingTests` (tool / reach / AP, terminal over two actions, failure alarm + Network + shock + lockout, Network
  Ghost, jammers, chance factors and perks, camera spotting and team sight, turret for each side, shot-down turret, doors,
  lockers, relay, Override, field XP, zone placement never cutting the map, perks into the field, the other hooks, saves),
  `DroneTests` (deploy, flight over walls revealing fog, control range and perks, battery and landing, recall and recharge,
  extraction, shot down, the Linked vs Husks, melee, Spotter, Payload, jammer, allies, saves), `WorldMapTests` (scouting
  flights), `HackNetTests` (no leaks of devices, hacks or drones; team camera sight; drone sight shared; perks over the wire).

## Linked squad AI

- **Network** (`Scavenge/Network.cs`): nodes are active, unjammed Linked units and online `Pylon` tiles (a terrain:
  impassable, cover, does not block sight). Two units link within either one's `enemies[].relayRange` (Chebyshev; the
  drone relays furthest), a unit and a pylon or two pylons within `network.pylonRange`. A connected group knows every
  hostile any member sees (`Known`) and shares one **focus**: lowest `HP / exposure`, where exposure is 1 − the least
  cover the target has against any member (weakest / most exposed).
- **A Linked turn** (`ScavengeAi.Squad`): share the sighting (idle members start hunting; one that does not see the
  target logs "learns of X through the Network"); if badly hurt (`retreatHpFraction`) and the squad still has a healthy
  member, **fall back** to the farthest, most covered tile; else **reposition** once to the reachable tile scoring best on
  expected damage at the focus (hit chance includes the focus's cover, so tiles that strip it — **flanking**, logged —
  score higher) + `coverWeight` × own cover against every known hostile + `apWeight` × AP left; then **shoot** the focus
  (or another known hostile) when the hit chance is at least `suppressBelowHitChance`, else lay down **suppressing fire**
  (`Unit.Suppressed`: it loses `suppressApPenalty` AP at the start of its next turn); else close in.
- **Reinforcements**: at round start the Network is alarmed while a group with an online pylon knows a hostile;
  after `reinforceAfterRounds` such rounds in a row that pylon calls a wave (`zones[].reinforcements`: waves, count,
  mix) onto free floor next to it (`ScavengeState.AlarmRounds`, `ReinforcementWaves`).
- **Signal jammer** (Tool device): a Linked inside a hostile jammer's radius is off the Network — no sharing, no
  coordination, fights like a Husk — and pylons inside it go offline (no relay, no reinforcements).
- **Enemy variants** are data (`enemies.json` + `natural` items): Husk; Husk Crawler (fast, low HP, dodgy "low profile"
  carapace); Linked; Linked Drone (rotors: Queen move, rubble/cover/water cost 1, weak zapper, long relay, sharp
  sight — the spotter); Linked Warden (shell: King 1, armor 7; `pulse` with `area: 1` hits everyone hostile around the
  target, each with its own roll). Models are `enemy_{id}`; pylon tiles `network_pylon`.
- Scenario tests (`LinkedAiTests`) build small arenas and assert sharing, relay range, pylon relay, jamming, flanking,
  focus fire, suppression, retreat and reinforcements.

## World map

- `zones.json` holds the world: per zone `region`, `mapPos` (0..1), `travelDays`, `depletionPerVisit`, `replenishPerDay`,
  `startIntel` (Unknown / Rumored / Scouted), `reveals` (zones a visit turns into rumors), `description`, plus the scavenge
  template (threat, loot tier, `enemyMix` entries with an optional `max` cap, `pylons`, `reinforcements`). Seven zones run
  from the doorstep Outskirts (Husks only) to the Core Spire (4 days, Wardens, three pylons, the best loot).
- Colony side (`Colony/World.cs`, `WorldMap`): `ColonyState.Zones` keeps intel, depletion and visits per zone (absent =
  as the data starts). Launch needs a zone that is not Unknown and rations of `rationFoodPerMember +
  rationFoodPerMemberPerDay · 2 · travelDays` per member. `Expedition` phases: **Outbound** until `ArrivalTick` (the
  mission cannot be opened), **InZone**, **Returning** (`ApplyExtractionResult` on a far zone stores `PendingResult` and
  `ReturnTick`; the tick applies it then). On return the zone is Scouted, depleted, and its `reveals` become Rumored;
  depletion recovers per day and thins the next run's caches (`NewScavenge` `ZoneConditions`). `ColonySim.WorldZones()` /
  `ZoneInfo(id)` give the UI everything incl. `LinkedPresence`.
- Game: `WorldMapPanel` (opened from Expedition → World map) draws the chart in code; clicking a marker shows its card;
  "Plan expedition" opens the expedition planner on that zone, which launches as before.

## Perks and study

All numbers live in `data/perks.json` (`skills`, `xp`, `study`, `disciplines`, `perks`); Core is `Colony/Progression.cs`,
`Colony/PerkEffects.cs`, `Colony/Study.cs` and `Data/Progression.cs`.

- **Skills**: Hacking (Intellect + Perception) and Drone Piloting (Perception + Agility) join the others. Every skill
  gain goes through `Survivor.GainSkill(skill, amount, source)` (diminishing: `amount · (1 − skill/100)`, returns the
  gain). Colony practice: a job's `trains` map grows side skills at a share of the main gain (Electronics Lab work →
  Hacking 0.3, Drone Piloting 0.15). The scavenge side calls `GainSkill` on its units' skills; field gains come home
  with the result.
- **XP** (`Survivor.Xp`, `Level`, `PerkPoints`): `xp.perWorkTick` per tick at a job (gathering, crafting, treating,
  practising), `perCraft` per finished batch to each worker, `perStudyTick` / `perTeachTick` in class, and on
  `ApplyExtractionResult` each survivor home gets `perMission + perMissionThreat · threat + perFieldSkillPoint · skill
  points gained in the field`. Children earn ×`childMultiplier`, infants nothing, perks may add `xpBonus`. Level L needs
  `perLevel·(L−1) + perLevelStep·(L−1)(L−2)/2` XP; each level gives `pointsPerLevel` points and logs `LevelUp`.
  `ColonySim.GrantXp(survivorId, amount, source)` is the entry point for XP from outside the tick (the scavenge side).
- **Perks** (`PerkDef`): discipline, tier, cost, `requires` (skills, attributes, perks, survivor level, a building at a
  level, a studied blueprint/**datashard** item, adults only) and `effects`. `ColonySim.PerkStatus(s, perk)` says
  Owned / Locked (datashard not studied) / MissingPrerequisites (with lines for the tooltip) / NoPoints / Ok.
  `ChoosePerk(survivor, perk)` spends points; errors `UnknownPerk`, `AlreadyHasPerk`, `PerkLocked`,
  `PerkRequirementsNotMet`, `NoPerkPoints`. Datashards (`ds_network_ghost`, `ds_override`, `ds_drone_payload`) are
  Blueprint items: studied with `StudyBlueprint` at the Electronics Lab like any blueprint, they unlock perks.
- **Effects**: `jobOutput` (job id → bonus; a `perks` factor in `JobOutput.PerTick` and Med Bay care), `craftQuality`
  (job id → added to the quality score when this survivor is the building's best worker), `studySpeed`, `xpBonus`, and
  `hooks` for the scavenge mode. `PerkEffects.For(survivor, data)` (or `ColonySim.PerkEffects(s)`) sums a survivor's
  perks: `Hook(id)`, `Has(id)` (flag > 0), `Hooks`, `JobOutputBonus(job)`, `CraftQualityBonus(job)`. Scavenge units
  carry their colony survivor as `Unit.Origin`, so the field can call it on that. Hook ids are constants in `PerkHooks`
  and validated at load (an unknown id in perks.json is an error):

  | hook id | meaning (values of several perks add up) | perks |
  |---|---|---|
  | `hack_chance_bonus` | added to hack chance (0.1 = +10 points) | Hacking I/II/III |
  | `hack_alarm_reduction` | share of a hack's alarm/noise removed (0.5 = half) | Network Ghost |
  | `can_hack_linked` | flag: can hack Linked units, not only Husks and devices | Override |
  | `hack_ap_discount` | AP off a hack's cost | Hacking II |
  | `field_repair` | flag/strength: jury-rig gear and devices in the field | Jury-rig |
  | `drone_range_bonus` | tiles added to a scout drone's control range | Drone Operator I/II |
  | `drone_battery_bonus` | rounds added to a scout drone's battery | Drone Operator I/II |
  | `drone_can_mark` | flag: the drone marks what it sees for the whole party | Spotter |
  | `drone_payload` | flag: the drone carries and drops a flare or motion sensor | Payload |
  | `accuracy_bonus` | added to hit chance (0.05 = +5 points) | Steady Aim, Marksman |
  | `melee_damage_bonus` | melee damage multiplier bonus (0.15 = +15%) | Brawler |
  | `overwatch_reactions_bonus` | extra overwatch reactions | Sentinel |
  | `brace_cover_bonus` | cover added while braced | Dug In |
  | `stealth_bonus` | added to stealth | Light Step, Ghost |
  | `detection_bonus` | added to detection | Keen Eye |
  | `sight_bonus` | tiles added to sight | Keen Eye |
  | `carry_bonus` | kg added to carry capacity | Pack Mule |
  | `loot_bonus` | extra share of cache items (0.2 = +20%) | Scavenger |
  | `field_heal_bonus` | healing items restore this much more (0.25 = +25%) | Field Medic |
  | `revive_hp_bonus` | HP added when reviving a downed ally | Triage |
  | `bleedout_rounds_bonus` | rounds added to a stabilized ally's bleed-out | Triage |

  `NewScavenge` resolves each survivor's perks into `Unit.Hooks` (`ScavengeData.HooksFor`, from perks.json loaded with the
  scavenge data), so the field needs no colony data and a server resolves them from the joined party. Every hook is
  applied: the profile ones in `ProfileDeriver`, the medic ones in `UseItem`, `loot_bonus` in `PickUp`, overwatch in
  `SetStance`, the hacking and drone ones in `Hacking` / `Drones` (see "Hacking and scout drones").
- **Study** (`StudyOrder` in `ColonyState.Studies`; `StartStudy(student, building, skill | perk, mentor?, manual?)`,
  `CancelStudy`): `study.buildings` says what each study building teaches and how many students per level (Town Hall:
  the colony skills; Electronics Lab: Electronics, Hacking, Drone Piloting, and the Tech and Drones perks). Student and
  mentor leave their jobs and cannot be assigned while in class (`InClass`). A skill grows by
  `skillPerTick · aptitude · (mentor ? mentorMultiplier : 1) · (child ? training.childMultiplier : 1) · (1 + studySpeed +
  manual studyBonus)` (diminishing), up to `selfStudyCap` alone (a **field manual**'s cap if higher), or the mentor's
  level when that is higher; it never lowers a skill. A **field manual**
  is a Material item with `manual: { skill, studyBonus, cap }`, used up at the start. A mentor must lead by `mentorLead`
  and teaches at most `maxStudentsPerMentor`. A perk is studied only from a mentor who has it (`NeedsMentor`), taking
  `perkWorkPerTier · tier` effort, and is learned without a point; its requirements still apply. A class ends when the
  student is gone, away or admitted; a skill class whose mentor goes carries on alone (logged), a perk class stops.
  Deaths and departures clean up classes at once (`Bonds.OnGone`), and loading drops classes left dangling. Perks cannot
  be chosen while away (the field reads them), and choosing a perk ends a class for it.
  `ColonySim.PlanStudy` previews cap, rate and ETA (or the error); `StudyEta`, `StudySlots`, `ClassOf` serve the UI.
  Errors: `NotAStudyBuilding`, `InvalidStudy`, `StudyFull`, `CannotStudyHere`, `InClass`, `NeedsMentor`, `MentorNotAhead`,
  `MentorBusy`, `AlreadyAtCap`, `NotAManual`, `WrongManual`, `UnknownStudy`.
- **Gear**: Hacking Kit (Lab L1) and Cyberdeck (Lab L3, `bp_cyberdeck`, uses a kit), Drone Parts (Lab L1) and Scout Drone
  (Lab L2, `bp_scout_drone`, 2 parts); their field behaviour is in "Hacking and scout drones".
- **UI**: the survivor panel's Perks tab (`PerksTab`: level, XP, points, discipline switcher, `PerkTree` with
  prerequisite lines, detail with ✓/✗ requirements and Learn), XP bar + level and perk-point / in-class badges on
  `SurvivorCard`, `StudyPanel` on study buildings, toasts for level-ups, perks and finished classes. `--perks-demo`
  stages it for screenshots (`--survivor-tab=4` opens Perks).
- **Saves**: colony version 8 adds XP, levels, points, perks, classes and the new skills. Older survivors get
  `xp.migrationPerSkillPoint` XP per skill point they already have (veterans arrive with levels and unspent points);
  unknown perk ids in a save are an error. Tests: `ProgressionTests`, `StudyTests`.

## Day and night

An hour is a tick (`hour = tick % ticksPerDay`); a new game starts at `newGame.startHour` (06:00). Everything is in
`rules.json` `dayNight`; Core is `Colony/DayNight.cs`, game-side `Colony/DayNightLighting.cs`.

- **Clock** (`DayNight`, pure): `Hour`, `Day`, `HourOf(double tick)` for smooth presentation, `PhaseAt(tick)` →
  `DayPhase` Dawn (05) / Day (07) / Dusk (18) / Night (20). `ColonySim.Hour` and `ColonySim.Phase` are the queries.
  `dayNight.enabled` false (the code default, so hand-built test rules keep the old math) means everyone works every
  tick and nobody sleeps; tests use `TestData.Real` with it off and `TestData.RealWithNights` for the cycle itself.
- **Schedule** (`Schedule`): the day shift is `workHours` [06, 20); adults sleep `adultSleep` [22, 06), children
  `childSleep` [19, 07) (children work 07–19 and go to bed first). A worker's `Factor` this hour: 1 on the day shift;
  `nightShift.outputMultiplier` (0.7) outside it if their building has `NightShift` on, they are an adult and the
  building is lit (indoors, level ≥ `litMinLevel`, or a Floodlight); else 0; times `tired.outputMultiplier` (0.85) while
  tired. Production, crafting effort, Med Bay staffing and training/XP all go through `Schedule.Crew`; classes keep the
  student's day shift. Queries: `OnShift`, `IsAsleep`, `IsTired`, `CanRunNightShift`, `OutputPerDay` (per-day output on
  the schedule; `OutputPerTick` stays the on-shift rate), `Schedule.ShiftHoursPerDay`.
- **Sleep and mood** (`DayNightSystem`, after food): asleep in their window (a bed or a Med Bay cot) gives
  `sleepMoodPerTick` / `sleepHealthPerTick` and pays back sleep debt; working the window adds `Survivor.SleepDebt`; at
  `tired.threshold` they are tired (−mood per tick, −15% output) until slept off. Night work costs
  `nightShift.moodPerTick`. Off work from dusk to bedtime: `campfireMoodPerTick`. A cooked meal from dusk to dawn adds
  `nightMealMoodPerTick`. Logged only when relevant: `NightShift` at the end of the day shift per building with a night
  crew ("Night shift at Woodcutter: −30% output." or "Too dark …"), `Tired` at the start of the day shift.
- **Commands**: `SetNightShift(building, on)` (errors `NoJobAtBuilding`, `NeedsLight`), `InstallFloodlight(building)`
  (`dayNight.floodlightCost`; errors `NotOutdoor`, `AlreadyLit`, `CannotAfford`).
- **Save** version 10 adds `Building.NightShift`, `Building.Floodlight`, `Survivor.SleepDebt`; older saves load with none
  on and everyone rested.
- **Presentation** (no rules): `DayNightLighting` (made by `TownView`, so the menu backdrop has it too) eases a shown time
  toward `Tick + session.TickProgress` and samples keyframes for the sun (east to west, warm low light at dawn and dusk),
  moonlight, sky colour, ambient and fog; whole days skipped are dropped, so fast-forward and loads never flash, and a
  paused game holds still. Lights register with `AddLight` / `AddGlow` and fade with `Night`: a lantern per building,
  an inside glow in homes and the Town Hall, a floodlight mast on lit outdoor workplaces that burns while the Night Shift
  is on, the campfire (flickers, brighter at night) and six perimeter torches; at most 12 lamp OmniLights, the rest only
  glow. `SurvivorWalkers` send sleepers home (patients to the Med Bay door) and in, children first by their bedtime;
  night-shift workers stay at their site; the awake evening crowd sits at the campfire. `MenuBackdrop` holds the light
  at dusk. HUD: phase icon and name in the `TopBar` (tooltip: hours, who works or sleeps, night shifts), Night Shift
  toggle with its penalty and the Floodlight on the `BuildingPanel`, asleep / night shift / tired badges on survivor
  cards, a toast on a shift's first night (later nights only log).
- **For audio**: switch on `ColonySession.Phase` / `PhaseChanged(DayPhase)` (raised after the tick that crossed into a
  phase, after a fast-forward and after a load), or `DayNight.PhaseAt(tick, rules)` in Core — not on raw hours.
  `ColonyAudio.IsNight` still reads the clock with its own 20–06 window until it is switched over.

## Data files

- `resources.json` — resource ids and names.
- `jobs.json` — per job: skill + weight, attribute weights, body-region weights, tool bonus, `childrenAllowed`.
- `buildings.json` — category, job, produced resource, base output (per worker-hour on shift), `outdoor` (a Night Shift
  needs light), `minTownHallLevel`, and per-level
  cost / worker slots / output multiplier / beds / patients. Level 1 cost = construction cost.
- `recipes.json` — what production buildings make (see "Crafting tiers").
- `perks.json` — skills (names, learning attributes), XP sources and level curve, study buildings and rates,
  disciplines and the perk tree (see "Perks and study").
- `rules.json` — tick/time scale, output formula constants, training rates, food, housing grace,
  meals, crafting (queue limits, quality roll), expedition (party size per Town Hall level, rations, recruit ages), medical (heal/worsen rates, medicine use),
  life (growth stages and rates), relationships (affinity), family (conception, pregnancy, baby names), mood,
  new-game setup (counts, age/stat ranges, stock, starting items, starting buildings, names, start hour), `dayNight`
  (phase hours, work and sleep windows, sleep / campfire / supper mood, Night Shift, sleep debt, light, Floodlight cost).
- `items.json` — weapons (actions, with `noise` and `reveal`), clothing/armor (with `stealth`), tools (movement modifiers, carry, sight, `detection`),
  flares and motion sensors (`use.lightRadius`/`lightRounds`, `use.sensorRadius`: used on a tile),
  consumables (med-kit, bandage, evac beacon), materials (with `salvage` into colony resources), prosthetics
  (region, quality, Med Bay level), devices (scanner, signal jammer), blueprints (study building and level);
  tier + loot weight for caches; `natural` for enemy body parts (never looted or crafted). Every other item has a
  recipe or a loot weight (tested).
- `enemies.json` — hp, AI kind, attributes, skills, equipment (a Husk's claws are an item too).
- `zones.json` — size, threat, loot tier, terrain densities, spawn/exit edges, enemy count + mix,
  caches, NPC count, hackable devices per kind.
- `scavenge.json` — `hacking`, `devices`, `drone`, `world.droneScout` (see "Hacking and scout drones"), `coop` (alliance offer / break-warning rounds, pings, give and carry costs, carry movement penalty,
  enemies per extra player), `fog` (stealth, detection, contacts, ambush, overwatch, brace, lookout, Linked fog AI), `network` (Linked squad tuning), `world.colonyPos`, terrain table, movement derivation (base pattern/range, Agility per range,
  encumbrance steps, body rules such as "missing leg → King 1"), AP, sight, carry, combat numbers,
  AI, NPC names, escort radius, gear quality tiers.

## Running

```sh
./run-tests.sh            # build solution + run Core tests
./run-tests.sh --godot    # also build through the Godot editor headless
godot-mono --path game    # play (main menu → Colony shows a new Core colony, Scavenge a debug mission)
godot-mono -e --path game # open in the editor
dotnet run --project src/Remnants.Server -- --port 7777   # multiplayer server; then Multiplayer → Join in two games
dotnet run --project tools/Remnants.Playtest -- --data=data --seeds=1..20 --days=60 --quiet   # balance sweep (BALANCE.md)
dotnet run --project tools/Remnants.Playtest -- --data=data --zone=all --runs=300            # zone lethality
dotnet run --project tools/Remnants.Playtest -- --data=data --zone=suburb_ruins --runs=300 --coop=2   # co-op trial
```

Data lookup (`GameDataPaths`): `data/` next to the executable in exported builds (lets players override
balancing), the repo's `data/` (`res://../data`) in dev, else the copy embedded in `Remnants.Game.dll`
at build time — so exports work with nothing copied.

Debug flags (after `--`): `--colony` skips the menu, `--scavenge` starts the debug mission (`--autoplay=N` plays N
orders with a simple bot so enemy turns replay, `--zoom=SIZE` sets the ortho camera size), `--demo` builds a farm and woodcutter and assigns
the adults, `--loop-demo` sends the adults on an expedition to the outskirts (scavenge screen, or resolved in place with
`--autopilot`) and builds a Med Bay and Workshop, `--family-demo` does `--demo`, encourages the first man and woman
and fast-forwards until a baby is born (then selects the mother), `--crafting-demo` builds a grown town (Town Hall 3, every
production building, some at level 2–3) with staffed, queued Armory and Tailor and opens the Armory and item panels, `--expedition` opens the expedition planner with two adults picked, `--world-map` opens the world map
(`--zone=ID` selects a marker), `--tech-demo=hack|turret|drone` (with `--scavenge`) stages a hack in progress, a hacked turret firing or a
drone scouting (saves its own still with `--screenshot`), `--fog-demo` (with `--scavenge`) stages a fog ambush (`--select-unit=watcher` shows the unit on overwatch with its range;
`--select-unit=ID` and `--pause-menu` work in any scavenge run), `--world-demo` fakes a few weeks of scouting and sends a party on the road (for
screenshots); `--scavenge --zone=ID --seed=N` plays the debug mission in another zone, `--multiplayer` opens the multiplayer menu and
`--mp-host[=PORT]` / `--mp-join=ADDR` with `--mp-name`, `--mp-ready`, `--mp-coop`, `--mp-team=N`, … script it (see `MULTIPLAYER.md`), `--screenshot=PATH [--frames=N] [--quit]` saves the viewport as PNG (any scene). Day and night: `--night-demo` (the demo town two days on, a Floodlight and the Night Shift on the Woodcutter,
selected), `--hour=H` runs to the next H:00, pauses and puts sleepers indoors, `--no-select` clears the selection for
scenery shots. HUD shots: `--pause-menu`, `--settings`, `--build-menu`, `--select-survivor=ID`, `--survivor-tab=N` (colony), `--menu-settings` (main menu). Needs a display; on
Wayland pass `--display-driver wayland --disable-vsync` so an unfocused window still renders:

```sh
godot-mono --display-driver wayland --disable-vsync --path game -- --colony --demo --screenshot=/tmp/colony.png --frames=300 --quit
```
