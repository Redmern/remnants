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
| `tools/Remnants.Bench` | Headless performance baselines: colony tick, scavenge AI turns, save/load. See `PERFORMANCE.md`. | Core, Playtest |
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
    (`CraftingSystem`: production queues and quality rolls; queue/cancel/set-recipe handlers) — see
    "Crafting tiers" below.
  - `Life` (`Bonds` formulas and queries, `RelationshipSystem`, `FamilySystem`, `GrowthSystem`, `Matchmake`) and
    `Relationship` (pair affinity + the player's `PairStance`) — see "Population life" below.
- `Random/DeterministicRng` — seeded xorshift64*; its state lives in `ColonyState` / `ScavengeState`.
- `Serialization/` — shared `System.Text.Json` options and `SaveGame` (versioned envelope).
- `Items/ItemDef` — item definitions: kind/slot, weight, armor/dodge/initiative/AP/sight/carry
  modifiers, a `MovementModifier`, attack `ActionDef`s, consumable `UseEffect`, `DeviceDef`, a `Schematic` id on schematic items, and for
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
    sides; AI turns (`ScavengeAi`: Husk, Linked squad, turrets, recruit followers) run inside it. See "Linked squad AI"
    and "Telegraphed plans and cards" below.
  - `PlannedActions` (telegraphed plans), `Cards/` (`CardDefs`, `SquadDecks`, `CardPlay`, `CardSlots`, `EnemyCards`,
    `BotTactics`) — see "Telegraphed plans and cards".
  - `Hacking` (devices, the Hack action, Override, placement) and `Drones` (scout drones) — see "Hacking and scout drones".
  - `Network` — the Linked relay graph (groups, shared knowledge, squad focus, pylon alarm and reinforcements).
  - `ExtractionResult` per player — returned survivors (with injuries/health), recruits, deaths, loot.
  - `ScavengeSave` — versioned save/load with validation against `data/`.
  - `FieldTrade` — a live trade between allies in a mission: pack items swapped atomically (see "Player trading").
  - Story missions in the field: `FieldMission` (objectives, triggers, prompts), `MissionRules` (the rules over
    `ScavengeState.Mission`), `HandmadeMaps` (handmade maps and fixed rooms). See "Story mode".
- `Story/` — Story mode's campaign (see "Story mode"): `CampaignState`, `Campaign`, `Dialogue`, `StoryData` + loader.
- `Trade/` — player trading (see "Player trading"): `TradeGood` / `TradeGoods` (lines, validation, caps), `TradeRules`
  and `TradePrices` (data/trade.json), `ColonyTrade` + `TradeState` (escrow, settle, refund, receive, courier deliveries;
  idempotent per transaction id), `Courier` (travel plan, composable modifiers), `FoodGoods` (food lines and their Pantry lots).

## Game layout (`game/`)

- `scripts/Colony/ColonySession` — presenter: owns the `ColonySim`, runs fixed ticks at pause/1x/2x/4x/8x
  (`SecondsPerTick` real seconds per tick at 1x, from the `GameSettings.DayLengthMinutes` setting, 2 min per day = 5 s
  per tick by default; pausing keeps the way into the tick; `RealSecondsPerTick` at the current speed), sends every command, saves/loads `user://colony.json`. `Phase` +
  `PhaseChanged` and `TickProgress` serve the day/night presentation (see "Day and night").
- `scripts/Colony/ColonyScreen` — builds the scene in code and wires views to the session.
- `TownView` (no visible lots: a soft round earth patch under each building, warmer when selected; the plot grid only as
  faint rounded footprints while placing, the hovered plot green or red; building nodes, plot picking; building names only for the hovered
  and selected building), `TownDressing` (purely visual scenery from the kit: noise-varied earth, a campfire on the
  commons, a loose fence of barriers/sandbags with a gate and a rutted road, dead trees, rubble and ruined shells
  outside), `SurvivorWalkers` (purely visual ambient life: workers circle their workplace with the odd break at home,
  idle adults potter near home or sit at the campfire, children play by the Town Hall, patients sit outside the Med
  Bay, infants stay home; long walks follow the gutters between plots, figures keep apart, at most 2x speed, 3x at 8x),
  `OrbitCamera` (WASD/arrows/middle-drag pan, wheel zoom, Q/R/right-drag rotate; speeds from `GameSettings`). `TownLayout.Commons` puts the
  campfire and playground just outside the grid beside the Town Hall, so they never take a plot.
- `ModelLibrary` — reads `assets/generated/manifest.json`; buildings map to `{type}_l{level}` (nearest
  lower level, then a placeholder box), survivors to `survivor_adult_male|female` / `survivor_child`
  (placeholder capsule), dressed by `SurvivorDressing` (below). `tools/sync-assets.sh [EXPORT_DIR]` refreshes `assets/generated/` from the
  remnants-assets `export/` (default `../../remnants-assets/main/export`), models and `audio/` alike.
- `SurvivorDressing` (shared by both views, purely visual): missing limbs hidden; prosthetics swapped in at the joint,
  the mesh the part's cybernetics node names (`prosthetic_*_hook|peg_*` below quality 1, `*_cyborg_*` from there for
  parts outside the tree); an eye implant on the head; equipped gear hung on the body's manifest
  `sockets` (the `item_{baseId}` model's `attach` point lands on the socket, under the part it rides on, turned by its
  `rot_deg`, scaled by chest height for bodies other than the adult male). Body → torso; weapon → hand, else back (a
  missing arm drops it to the back; a prosthetic arm holds it); tool → back, else chest, else hand. Colony survivors
  carry no gear in Core, so the colony shows prosthetics only.
- `GearModels` (purely visual, used by `SurvivorDressing` for field units, portraits and the Gear Bench render): a modded
  item id resolves to its base model `item_{definedId}`; each mod's model (a code-side map from mod item id to the
  asset id, else the data's `model`, else nothing) is parented under the gear's `mod_{slot}` node (the glb node, else the
  manifest `mod_slots` point), offset by minus its `attach.mount`. Node choice: the mod model's `params.slots`, then the
  data slot (`stock` → `mod_stock` else `mod_grip`; `visor` → `mod_visor` else `mod_filter`, a filter the other way).
  Mods with `params.apply = "recolor"` (camo, waterproofing) recolour the garment's materials named in `params.recolor`
  to `style.json` `palette[value]` (and `params.roughness`). Headgear hangs on the `head` socket. Missing models,
  nodes or a manifest without `mod_slots` leave the gear plain.
- `scripts/HudTheme` — the one `Theme` for every screen's HUD: worn-metal 9-slice panels, bevelled buttons with hover /
  pressed / disabled / focus states, tabs, lists, sliders, tooltips, scrollbars, progress bars, the bundled fonts; type
  variations (`Card`, `CardSelected`, `Inset`, `Chip`, `Toast` panels; `Title`, `Display`, `Heading`, `Muted`, `Small`,
  `Number` labels; `Primary`, `Flat`, `Tab` buttons). Set on each HUD root and overlay (a `CanvasLayer` does not pass a
  theme on). Panels pick a variation and add content, not styles.
- `scripts/Hud/` — the HUD component library and design tokens, shared by all screens:
  - `Palette` (colour tokens: rust/amber for the player, moss good, red danger; **cyan only for the AI Network**),
    `Fonts` (Barlow body, Barlow Condensed display/numbers, OFL, in `game/ui/fonts` with their licences), `PanelArt`
    (procedural grain/bevel/rivet 9-slice textures), `Fmt` (number formatting), `Motion` (the motion vocabulary: one
    set of durations, `Fast` 120 ms / `Normal` 180 ms / `Slow` 220 ms, ease-out cubic; `Reveal` / `Conceal` panels, `Enter` /
    `Exit` list rows with a capped stagger, `CountUp` numbers, `PressFeedback`, `OnPress` for buttons that act on press,
    `Flash`, `FadeIn` for popups the caller places (fade and a slight grow, position left alone); one running tween per control and purpose; all of it instant with Settings → Reduce motion). Tweens only
    touch modulate, scale and free-standing positions, never a container child's layout.
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
    visual sound cues, camera pan/zoom speed, day length (Game tab: real minutes per in-game day at 1x).
    `Hotkeys` is one table of `Binding`s (action, label, context Everywhere / Colony / Scavenge, keys; `Shift(key)` makes
    a Shift chord) that installs the InputMap actions and feeds Settings → Controls (plus `Hotkeys.Fixed`: pan, zoom,
    rotate, cancel, orbit, turn the defense being placed). Everywhere: Esc menu, F1 help, F3 frame time. Colony: Space
    pause, 1/2/3/4 speed, B build, F defend, E expedition, I items, L log, Tab next survivor, P power grid, M world map,
    G garage, Y radio, T research, K pantry, Q/R camera rotation. Scavenge: Space/Enter end turn, 1–9 order slots
    (attacks, then items), O/B/L stances, F loot, I salvage, T recruit, X extract, R load the vehicle, G give, C carry,
    Z/V/N pings, Y/U alliance, H hack, K drone, J fly, M mark, P payload, Shift+R drone role, Shift+I ferry, Tab next
    unit, Home centre, Backspace cancel aiming. `GameSettings.Boot` runs
    once from whichever scene starts first and also attaches `DebugCapture`. `DebugCapture` also attaches `PerfMonitor` for
    `--perf-log` (frame time, draw calls, memory, hitches; see `PERFORMANCE.md`).
  - `Unchanged` — sets a control's text / tooltip / texture / visibility / colour only when it changed; HUD panels
    that refresh every tick use it so unchanged values cost no engine call or relayout.
  - `Help/` — in-game help. Topics live in `data/help/*.json` (Core `Remnants.Core.Help.HelpCatalog`: id, title, a
    short hover line, a long text in `HelpMarkup` — paragraphs, `## ` headings, `- ` bullets, `**bold**`, `*em*`,
    `[[topic]]` links —, an optional live-breakdown `provider`, related topics, search keywords; `Validate` checks ids,
    links, providers and short-text length, a Core test runs it on the shipped files). `Breakdowns` are the providers:
    read-only Core queries that list the terms of a rule with live numbers (`job_output`, `colony_output`, `food_days`,
    `travel`, `hit_chance`, `humanity`) for a `HelpContext` (colony, survivor, building, zone, scavenge sim, attacker,
    action, target). Game side: `InfoTip` is the code-drawn (i) placed next to any label (`InfoTip.Beside(label,
    topic, context?)`); hover, keyboard focus or a touch-hold shows the short text, click / Enter opens the detail.
    `HelpLayer` (a `CanvasLayer` on the tree root, attached at boot, runs while paused) owns the one shared tooltip
    (80 ms delay, instant when moving between tips, kept on screen), the detail popover (`TopicView`: text, live
    numbers, related links with Back; Esc / click outside closes) and `HelpIndex` (F1 or Help in the pause menu:
    search over every topic, grouped by category). `HelpLibrary.Ambient` is the screen's context (the colony screen:
    the sim, the open survivor and building; scavenge: the acting unit, its chosen attack and the hovered hostile),
    each `InfoTip` may add its own on top. Debug (`HelpSelfTest.HelpDemo`): `--help-demo[=TOPIC]` hovers then opens the
    first visible `InfoTip` (of that topic), `--help-index` opens the index.
  - `FrameTimeOverlay` — debug frame-time graph top right (F3, Settings → Display → Show frame time, or
    `--frame-overlay`): last / mean / max ms, fps, frames over 16.7 ms per second and in total, a 2 s graph.
- `scripts/MainMenu` + `MenuBackdrop` — the title screen over a throwaway grown colony circled by a slow camera:
  Continue (the session's colony, else the save), New Game, Load, Multiplayer, Settings, Quit, debug skirmish.
  `--new-game-choice` opens the Story / Sandbox choice (screenshots).
- `scripts/Colony/Ui/` — `ColonyHud` lays the colony HUD out and owns its hotkeys: top bar; roster and survivor details
  down the left; one centre panel at a time (`Inventory`, `Pantry`, `Expedition`, `Garage`, `Repairs`: opening one hides
  the others); the raid warning under the top bar; the right column at `PanelWidth.Side` (the selected building, the build
  menu, defend catalogue and selected defense, the event log at a fixed height under them); toasts bottom-right; world
  map, after-action and raid reports, raid prep, pause menu and settings on top. Hotkeys do what their buttons do; M
  world map, K pantry; G / Y raise `BuildingRequested("garage" | "radio_mast")` and T `ResearchRequested`, and
  `ColonyScreen` selects the best such building (the Town Hall). Esc closes the topmost panel, then opens the pause
  menu (resume, save, load, settings, main menu, quit). Debug: `--hud-size=WxH` lays the HUD out in a smaller area
  (logical units; `1506x847` is 1280×720 at its auto scale).
  - Shared parts (`PanelParts`): `Space` (spacing scale 4 / 8 / 12 / 16), `Fit` (fit-to-content panels re-measure when
    their parent's room changes), `PanelWidth` (roster, survivor, building column), `CostRow` (cost chips kept by
    resource id, a cost the stock cannot cover red with "short N"), `TabStrip` (the one tab style, tabs act on press and
    can hide), `FoldSection` (chevron, caps title, a summary on the right, optional (i); body revealed with `Motion`),
    `KeyBadge` (the hotkey letter drawn in a button's corner). `PanelKit`: `PanelTabs` (a tab strip over pages built
    once and kept, Left / Right between tabs, the page revealed; only the visible page is refreshed), `Expander`
    ("▸ More" over a kept body), `Stepper` ("[−] 3 [+]", wheel and arrows), `KeyedRows<TKey>` (rows kept by key: built
    when a key appears with `Motion.Enter`, moved, updated in place, removed with `Motion.Exit`, so pickers, hover and
    focus survive a tick), and helpers (focusable buttons, captions and headers with an (i), height-keeping labels,
    state chips, `Clickable` cards with keyboard focus and Up / Down). Panels are built once and updated in place; rebuilds only on a
    changed signature.
  - `TopBar` — one row at every size: phase, weather and one forecast chip ("Rain in 6h", the forecast in its
    tooltip), clock in 10-minute steps between ticks, speed; the core resources (food, wood, stone, scrap) as
    `StockChip`s (count-up amount, ▲ / ▼ trend, tooltip with the change per day and what makes it) and the rest in a
    `ResourceDrawer` "+N" drop-down (`StockChips`); population, beds, food-days and power pressure chips with an (i);
    actions Build, Defend, Repairs, Items, Pantry, Expedition, World map, Menu — labelled when there is room, else
    icons with a `KeyBadge`, else a 4×2 icon grid (measured again only when the width or content changes).
  - `RosterPanel` of `SurvivorCard`s down the left (adults then minors; head count, H / M legend, collapse to
    portraits; as tall as its cards, scrolls past the room): card = portrait, name, badges (injured / worsening,
    patient, pregnant, away, homeless with its timer, starving, in class, asleep / night shift / tired, food buffs,
    perk points), every job with its effort share ("Woodcutter 50% · Farm 50%") and age on one line, H and M bars, a
    `LevelPip` ringed by XP. Left-click selects; right-click opens Assign to… / Add a job at… (buildings with a free
    slot; adds the job, off at the activity cap with the reason), Move here (leave other jobs)…, Leave a job…, Send to
    the Med Bay, Make idle (leave all jobs), Open details; a card dragged onto the building panel adds a job there
    (payload `{"survivor": id}`). All job menus go through `JobPicks` (Core): `Add` keeps the other jobs, `Move`
    replaces them, `Leave` drops one workplace only.
  - `SurvivorPanel` — tabs Overview (labelled health / mood bars, needs and status, nutrition, every job held
    with its effort share and a button to leave that one, an "Add a job…" picker with a "Move here (leave other jobs)"
    tick, and the cap explained when reached, the output line with `JobOutput`'s formula as tooltip and (i); Body — only the parts that are not
    healthy, conditions with treatments, humanity — and Family — ties, matchmaking with the current stance — as
    `FoldSection`s), Skills (attributes as a 2×3 grid, trained skills by value, the untrained folded) and Perks
    (`PerksTab`: level, XP, points, discipline switcher, a `PerkTree` of `PerkNode`s owned / open / missing / locked
    behind a schematic, detail with Learn). `OpenTab` keeps the old indices (Body / Family open and scroll to their section).
  - `BuildingPanel` — the selected building, one panel with a pinned header (icon, name, level pips, status chips for
    power / raid damage / lightning damage / night shift that jump to their section, `WorkerSlots`, the output line)
    over tabs: Overview (what it does, damage and repair, Night Shift and Floodlight, the Power switch), Upgrade (the
    next level's gain, named cost chips, the blocker with a link — "Open archive" for a missing schematic) and one tab
    per hosted panel (`AddExtra`: crops, the production queue, Med Bay patients, research, radio, study), shown while
    that panel applies. A new building opens on its first hosted tab, else Overview. As tall as its content up to
    `MaxHeight` (the column's room from the screen); past that the tab body scrolls under the header.
  - `WorkerSlots` (also the `DefensePanel`'s guard slots): worker portraits (click: Unassign / Open survivor;
    right-click unassigns at once; new ones enter with `Motion.Enter`) and a focusable "+" per free slot that opens
    `WorkerPicker` (portrait, current jobs with shares, the slot's fit score with its parts, injured / tired / child / patient /
    away / in-class badges, free people first, best first); a click sends the owner's command (`JobPicks.Add` /
    `AssignGuard`; shift-click `JobPicks.Move` where the owner offers it) and Core's refusal shows in red; removing a
    worker sends `JobPicks.Leave` for that workplace only. Debug: `--worker-demo=open|assign|multi` (assign fails when
    the picked survivor loses another job; multi then opens their panel), `--placing-demo`.
  - `BuildMenu` — one row of category icon tabs, a grid of tiles (icon, name, a one-line "what it does", `CostRow`),
    locked tiles with a lock and the reason on the tile (schematic, Town Hall level, already built); picking one starts
    placement, with a cursor tag naming the building, its cost and how to cancel. Fills the column.
  - `EventLog` — hour, icon and colour per line, "Day N" rules, filters All / People / Colony / Alerts, a fixed height
    (three lines, ten expanded with L) so it never pushes the panels above; first-time `Advice` tips are an Advisor
    strip on top (one at a time, dismissable), not log lines. Important events become `Notice`s for toasts, merged by
    event type and survivor ("Juno reached level 3 (+2 perk points)"); events with their own report get none.
  - Building screens, one task per tab (`PanelTabs`), rows kept by id and updated in place, Core decides:
    `ProductionPanel` (Queue: active order with its bar, the rest one line with a state chip — Repeat, ×N, Next,
    Waiting for materials, Not started — "Again", Gear Bench / Cybernetics buttons; Recipes: group tabs at the
    Electronics Lab, a quick "+" per row, the picked recipe's needs, work, time, quality odds, power, research chance,
    a `Stepper`, Repeat, Queue; Charging at the Electronics Lab; at cooking buildings the recipe book with ingredient
    chips and locked dishes under "Locked (N)"; `--production-tab=queue|recipes|...`), `GaragePanel` (Vehicles: list
    with condition, the picked one turning in 3D with four key numbers and the rest behind More; Upgrades: slots with
    fitted chips, a picker per free slot, before → after, cost, blocker; Repairs; Build = the Garage's
    `ProductionPanel`; per-tab signatures; `--garage-tab=`), `GearBenchPanel` (overlay; Bench / Queue tabs: gear list,
    slots with fitting mods and hover preview, Mk kit upgrade, before/after `GearBench.Stats` with a `BodySilhouette`
    for worn gear, a collapsible compare; the bench orders; `--gear-tab=`), `MedBayPanel` (pinned header: beds, heal
    rate, medicine and days it lasts, "999+" for a huge stock; Patients, Surgery — queue, odds, ETA, prosthetic fitting,
    augmentation behind a confirm, "Tree" — and Dispensary in `MedBayPanel.Dispensary.cs`; hosted in the building
    panel it opens on Patients), `CyberneticsPanel` (overlay: slot switcher, `CyberTree` tier columns T0..T4 with
    prerequisite lines and a legend, the picked design in reading order — needs, components, effects, humanity, upkeep,
    Queue pinned, compare with a survivor's limb, Fit / Book surgery), `StudyPanel` (classes, a new class pre-picking the
    best mentor and manual, Core's `PlanStudy` before Start), `ResearchPanel` (running research with ETA and Cancel, a
    researcher picker, `ResearchOptionRow`s kept in place), `RadioPanel` (`RadioTab`s from a `Tabs` list —
    Calls with Answer / Accept, Trader as cards with Buy per price option and a Sell section, Contracts; each tab a
    status line plus a body rebuilt on its signature).
  - `InventoryPanel` (Items; the second tab is `SchematicsArchive`: schematics by what they unlock with status Known /
    To decrypt / Heard of / Unknown / Planned, filter and search, player wording for sources and missions via
    `SchematicText`; `ShowSchematic(id)` deep-links to one), `PantryPanel`, `WorldMapPanel` (`WorldMapCanvas` chart,
    a summary card for the picked zone, legend chips incl. `LegendMark`, keys, no per-tick rebuilds; "Plan expedition"
    hands the zone to the planner), `ExpeditionPanel` (five steps — Destination, Party, Gear, Vehicle & supplies,
    Review — one on screen at a time, in `ExpeditionSteps`; a summary bar with InfoTips on every step; tabs marked
    settled / warning / blocked; Review lists the warnings with links to their step and Launch says why it is disabled;
    keys 1–5, Tab, Enter; Repeat last refills the session's last launched plan; per tick only the summary and the shown
    step refresh; `--expedition-step=N`), `AfterActionDialog` (who came home with injury chips, who was lost,
    recruits, loot).
  - Read Core state, call the session; no rules. Where the UI needs a rule Core exposes a query (`MaxUpgradeLevel`,
    `MaxPartySize`, `HealPerTick`/`HealEta`, `IsWorsening`, `CraftEffortPerTick`, `CraftStatus`, `SkillGap`,
    `CanQueue`, `QualityChances`, `PreviewProfile` for a survivor's field profile with a given loadout,
    `FoodPerDay`/`FoodDays`, `OutputPerTick`); the per-survivor output breakdown uses `JobOutput`'s public factors. Bars
    fed once a tick (production queue, research) glide across the next tick (`Ui.Glide`). `tools/ux-gallery.sh` captures
    every screen through the debug flags and writes a before/after page.
- Electronics and power UI: `ProductionPanel` groups a bench's recipes into tabs by `RecipeDef.Group` (Electronics Lab:
  Components / Devices / Upgrades / Disassembly / Other), badges `NeedsPower` recipes and orders, shows disassembly yields and
  research chance, the lab's powered line and the charging bench (`ColonyState.Charging`, `Power.ChargeEta`); `TopBar`
  shows used/supply, battery % and brownout / breaker trouble once a generator or bank stands, with a grid button (P) for
  `PowerOverlay` (posts, tags and cables over the town from `ColonySim.PowerNodes`, fed / not fed / idle / off); the
  `BuildingPanel` Overview tab (`PowerText`, a power status chip in the header) has the on/off switch (`SetPower`). Power buildings use shaped placeholders
  (`Placeholders`, category colour) until `fuel_generator_l1` etc. exist. In the field: EMP / holo-decoy / noise decoy use
  buttons (tile-aimed like flares, charges for reusable kits), drone-role orders in `TechPanel` (Shift+R: heal / load / fire,
  Shift+I: ferry), the drone card's variant, role and cargo hold, hologram decoys, the EMP ring (`UnitViews.EmpRing`), tinted
  `scout_drone` models for the variants, EMP / DECOY world tags and log icons.
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
  by `ScavengeHud` (which also owns the hotkeys and the pause menu). Turn bar on top: `TurnBar` (zone, round, the
  initiative order as `OrderChip`s kept per unit — side-colour edge, the acting unit larger, framed and named, those
  that acted faded, unseen ones "?", HP as a bottom-edge tint —, the acting unit's AP pips, End Turn, Leave, menu), the
  `AllianceBanner` under it. Left: `UnitPanel`, summary first — header (portrait rendered from the unit's model with
  its gear, name, kind, status, "Not their turn" when one of yours is selected while another acts, HP bar, AP pips
  unless it is the acting unit) and folding sections (`Scavenge/Ui/Expander`: chevron, icon, title, a one-line summary
  while folded, an (i); open state remembered per section for the session): Body ("All parts ok", else only hurt, lost
  and prosthetic parts), Field profile (Move, Sight, Armor, Dodge with an (i) each; AP, initiative and attacks behind
  More), Gear (folded, summary; slots, pack, load), and an Ally section for a co-op ally's read-only unit — with
  `LootPanel` docked under it while the acting unit stands at a pile, on an exit with a pack, or is slowed (one load bar
  against the encumbrance steps, item chips per pile, Take all (F) amber when it slows, `PackRow`s with Drop 1 and Stash
  on an exit). Right: one fixed-width column — `ActingHeader` (the acting unit's portrait and "Silas's turn", and a
  reserved two-line hint: what a click does while aiming an item / ping / drone order, another unit selected, enemy
  turn, autoplay; its height never changes, so the buttons below never move), `MissionPanel`, then the orders in a
  fixed order: `ActionPanel` (Attack · Use, numbered 1–9, attacks then items), `StancePanel`, `ActionPanel.Context`
  ("Here": salvage I, recruit T, the vehicle chip with Load R / Take, extract X), `TechPanel`, `CoopPanel` (Team); all
  built from `ActionButton`s (icon, detail line, AP-cost pips, keycap; toggles act on press). Orders dim while another
  unit is selected and stay put (dimmed) while a turn replays, catching up after; both side columns scroll on a short
  screen. Bottom: toasts and `ScavengeLog` (round, icon and colour per kind, errors red, only Network events cyan;
  folds to three lines, opens on hover). Under everything `WorldTags` (one stack of `TagChip`s per unit, kept and
  updated in place via `TagChip.Set` — name and HP bar, status tags, attack preview with hit chance and AMBUSH, rising
  pop-ups — so nothing overlaps; contacts' `?` on their tiles); on top `ResultPanel` (`ExtractionResult` in the
  after-action style), pause menu, settings, and `DeviceTip` at the cursor. Portraits of field units come from
  `UnitPortrait`. Nothing rebuilds per event: the order panels (`ActionPanel`, `TechPanel`, `CoopPanel`) keep their rows
  through `PanelRows` (rows asked for in order are re-used and updated in place via `ActionButton.Configure`, extras
  hidden; `Heading` with an optional (i), `Note`, pooled status chips), and `StancePanel`, `MissionPanel`, the order
  chips, `LootPanel`, the ping row and the vehicle chip update in place; panels reveal / conceal and rows enter with
  `Motion`; `ScavengeScreen.RefreshHud` coalesces every request in a frame into one deferred refresh.
- Scavenge input: left-click a blue tile to move, a red target to attack with the chosen action, a unit to inspect it;
  item buttons arm "use on" and the next unit click is the target; right-click / Esc / Backspace cancels aiming (Esc
  with nothing to cancel opens the pause menu); Space/Enter ends the turn; 1–9 are the order slots (attacks, then items:
  with two attacks the items start at 3), Tab next unit, Home centres on the acting unit; the rest per the `Hotkeys`
  table (drone orders that share a letter take Shift: Shift+R role, Shift+I ferry).

- `scripts/Multiplayer/` — `MultiplayerMenu` (scene `Multiplayer.tscn`: name, address, Host / Join, lobby with Ready),
  `MultiplayerSession` (the party from the colony's waiting expedition or a debug party, the connection, the in-process
  host, auto-reconnect, applying the result to the colony), `NetPump` (posts network callbacks onto the main thread).
  See `MULTIPLAYER.md`.

- `scripts/Audio/` — presentation only, no rules. `AudioManager` is the one autoload (`project.godot`): it makes the
  buses (`AudioBuses`: Master with compressor + limiter → Music, SFX (→ UI, World, SFXStereo), Ambience; Music and
  Ambience duck under stings; per-bus trims from the map's `mix`, measured with `--audio-meter`), reads `assets/generated/audio/manifest.json` (`AudioManifest`: id, category, bus, file, variants, loop
  points, default volume, tags, music bpm / sync group, stems; synced with the models from remnants-assets `export/` by
  `tools/sync-assets.sh`, then imported by Godot) and the event map
  `game/audio/sound_events.json` (`SoundEventMap`: dotted event names → sound ids, falling back by dropping the last
  part; per-id tuning — bus, volume, pitch/volume jitter, cooldown, voice limit, 3D attenuation, duck; music states;
  ambience; zone → tags → loops; weather loops). `Fire(event, pos?)`, `Play(id)`, `PlayAt(id, pos)` play pooled
  voices with a random variant; an id the manifest lacks stays silent and is logged once (`PlaceholderSounds`
  synthesises a few UI blips and two stings until the files land). Retuning is a data edit.
  - `MusicDirector` — states menu, colony_day/night/tension, world_map, scavenge_explore/combat, after_action,
    coop_lobby; crossfades on the next bar (bpm from the manifest) or at once at the same position within a
    sync group (scavenge explore / combat / layers), never restarts a state's track, stems by `Intensity`, layers on
    top (`linked_layer`, `contact_layer` while something is heard but not seen), one-shot stings (`music.extraction_sting`).
  - Scene hooks, one node each: `ColonyAudio` (colony events at their buildings, day/night bed — the one day/night
    decision is `ColonyAudio.IsNight(ColonySession.Phase)`, refreshed on `PhaseChanged`; the music holds day or night 45 s, since a day passes in a couple of minutes —, campfire, work loops at staffed buildings, listener height follows zoom,
    music state), `ScavengeAudio` (each replayed event via `ScavengeScreen`'s replay step, so sound lands with the
    animation: attacks by action, moves/deaths/hits by who, barks, overwatch shots, contacts, hacked devices by
    kind, drone hum; zone ambience; combat/explore music and the Linked layer), `UiAudio` (buttons, overlays, side
    panels and toasts hooked as nodes enter the tree; menu and lobby music), `SoundCues` (accessibility: chips at the
    screen edge toward off-screen ambush/contact/alarm sounds), `WeatherAudio` (`Set(kind, intensity)`,
    `Lightning(distance)`, `Gust()`; driven by the weather, see "Weather").
  - `--audio-trace` logs every fired event and its sound; `--audio-selftest` (run by `run-tests.sh --godot`) checks
    every Core event type is mapped and replays a scripted colony, an autopiloted scavenge run and the hack/drone
    events, failing when key events pick the wrong id; `--weather=KIND[:INTENSITY]` sets the weather bed; `--audio-meter` prints each bus's peak every second. Once the
    export is synced the self-test also fails on a mapped id the manifest lacks or a file that does not load.

### Character animation (`scripts/Anim/`)

Purely visual. Survivors, enemies, prosthetics and both drones are rigged glbs (remnants-assets manifest v2: `clips`
with loop flags and events in seconds, `limb_bones`, `sockets[slot].bone`); `ModelLibrary` reads them, and static v1
models still load and fall back to tweens.

- `CharacterRig` — added to a rigged model (`CharacterRig.Attach(library, model, id)`, found again with `Of`). It drives
  the glTF `AnimationPlayer` through an `AnimationTree` state machine with a crossfade between every pair of clips (one
  machine per clip set, shared). A **base loop** (`Loop`: idle, walk, overwatch_aim, crouch_brace, downed_loop, hover,
  work_*, sit, child_play) runs until a **one-shot** (`Once`: attacks, hit_react, throw, use_item, lookout, downed, death)
  returns to it, hands over to a named loop (`downed` → `downed_loop`) or holds (`death`). Manifest events come out as
  the `ClipEvent` signal; gaits without footfall events get a `step` per half cycle. The tree advances by hand:
  `SpeedScale` (time scale, gait pace) and `UpdateEvery` (animation LOD). `PoseStill` poses a portrait in idle.
- `ClipMap` — Core → clip names, best first, so each body takes the first it has: attacks by action id then pattern
  (`swing` → attack_swing, `aimed_shot` → shoot_rifle, `claw` → attack_claw, `zap` → shoot_burst, ...), the hold loop by
  status/stance, work loops by job id.
- `SurvivorDressing` on a rigged body moves the prosthetic's skinned mesh under the body's `Skeleton3D` (`{part}_prosthetic`,
  bound by bone name, only when the body has every `limb_bones` bone); the model id comes from the cybernetics data
  (`ProstheticModel`), and the tier tint only marks a stand-in when that mesh is missing. An eye implant
  (`prosthetic_eye_*`) moves its `eye` part (skinned to `neck`) under the skeleton the same way, keeping the head. A
  blade arm also fits `prosthetic_arm_blade_open_*`, hidden; `BladeArm` shows it around an attack clip's `impact`. Gear hangs on a `BoneAttachment3D` on the socket bone (`Socket_{bone}`), offset by
  minus its attach point; a one-armed survivor holds the weapon in the left hand.
- Scavenge (`UnitViews`): units walk a view-side tile path (8-way over what the unit can cross) facing each step, the walk
  clip timed to the tween (carry_walk when carrying, run on long paths); attacks turn to the target and land the hit flash,
  pop-up, hit_react, muzzle flash and sound on the clip's `impact` / `fire` / `pulse` event (`Replay`'s `onStep` runs at
  that beat); stances, downed, death (Linked `flicker` pulses cyan), hack, throw, use_item and carrying (the carried unit
  rides `socket_back`) play their clips; drones hover. Footfalls raise `Footstep` → `ScavengeAudio.Footstep`
  (`scavenge.step.{who}` if mapped, else `scavenge.step.{terrain}`).
- Colony (`SurvivorWalkers`): walk / run (children), the job's work loop at the workplace, sit at the fire and outside the
  Med Bay, child_play; clip speed = colony pace capped at 2x times the gait pace; pose updates every frame near the
  camera, every 2nd / 4th further out, every 8th off screen; near footfalls fire `colony.step`.
- Debug: `--anim-selftest` (headless, in `run-tests.sh --godot`) checks every rigged model's clips, event timing, loops,
  downed/death, prosthetic swaps (one data-named mesh per tier, an eye implant, the blade opening on impact), sockets and the portrait pose. `--anim-demo=survivor|cyborg|enemy`
  (with `--scavenge`) and `--anim-demo=colony` (with `--colony --demo --hour=8`) stage captures that `FrameRecorder`
  saves with `--record=DIR [--record-every=K] [--record-start=N --record-frames=M] [--gif=PATH] --quit`; pass Godot's
  `--fixed-fps 30` so clips keep their speed while frames are saved.

## Rules of the road

- **State vs rules.** `ColonyState` is data only. Rules live in `ColonySim` and its systems.
  Save = serialize `ColonyState`; definitions are reloaded from `data/`.
- **Deterministic.** Fixed `Tick()`, no wall-clock, no `System.Random`. Randomness goes through
  `ColonySim.Rng`. Same seed + same commands = identical save JSON (tested).
- **Commands only.** The UI changes the sim only via `ColonySim.Execute(IColonyCommand)`
  (`AssignJob`, `Unassign`, `Construct`, `Upgrade`; sites: `AssignBuilder`, `CancelConstruction`, `PauseConstruction`, `SetConstructionPriority`, `SetSiteNightShift`; `LaunchExpedition`, `ApplyExtractionResult`, `AdmitPatient`,
  `Discharge`, `FitProsthetic`, `SetRecipe`, `QueueRecipe`, `CancelOrder`, `Matchmake`, `ChoosePerk`,
  `StartStudy`, `CancelStudy`, `SetNightShift`, `InstallFloodlight`, `RepairBuilding`, `SetPower`; vehicles: `FitUpgrade`, `RemoveUpgrade`, `RepairVehicle`; schematics: `StartResearch`, `CancelResearch`, `SalvageSchematic`, `CopySchematic`, `BuyFromTrader`, `SellToTrader`, `AnswerCall`, `AcceptContract`; defenses: see "Raids and defenses"). Core validates and returns a `CommandResult`
  with a `CommandError` code. This is also the future client→server intent format.
- **Tick order:** power grid (and the charging bench) → production (+training) → construction sites → crafting → study → research → medical → food → day/night (sleep, night work, evenings) → weather (mood, dust, lightning) →
  relationships (mood, affinity, couples) →
  family (births, conception) → housing → growth (aging, growing up) → world map → raids (upkeep, guards, repairs, scheduler).
- **Scavenge commands** (`Move`, `AttackTile`, `UseItem` (on a unit, or a tile with `At`), `PickUp`, `Recruit`, `SetStance`
  (Overwatch / Brace), `Lookout`, `EndTurn`, `Extract`; co-op: `ProposeAlliance`, `RespondAlliance`, `BreakAlliance`,
  `PingTile`, `GiveItem`, `Carry`, `PutDown`; tech: `Hack`, `DeployDrone`, `FlyDrone`, `RecallDrone`, `MarkTarget`,
  `DropPayload`; drone roles: `DroneHeal`, `DroneLoad`, `DroneFerry`, `DroneFire`; vehicles: `StashItem`, `TakeFromStash`, `Salvage`) carry a
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
- `FitProsthetic(survivor, part, item)` straps a crude or mechanical part (tier below `surgery.fromTier`) onto a missing
  limb: a prosthetic item of that region (`items[].prosthetic`: region, quality, `minMedBayLevel`) in the inventory and a
  Med Bay of that level. Powered parts and up go on by surgery (`ScheduleSurgery`, see "Cybernetics"). The part's
  efficiency becomes the item's quality × its upkeep — above 1 for cybernetics — which feeds job output (body-region
  weights), scavenge movement range (leg efficiency, plus `scavenge.json` body rules such as two good prosthetic legs →
  +1 range), attacks (arms) and, for an eye implant, sight and aim (the head region is head × eyes).
- Sources: recipes (Workshop: Herbal Remedy → Medicine, Peg Leg, Hook Arm, Mechanical parts and Servos at L2, Med-kit at
  L2; Electronics Lab: the powered, cyborg and Network-grade designs), loot (`med_supplies`, Servo Arm, Cybernetic
  Leg/Arm, components) and the starting inventory (`rules.newGame.items`).

### Cybernetics

The prosthetics crafting tree; the full design with every number is in **CYBERNETICS.md**. Data: `cybernetics.json`
(tiers, specialisations, nodes, paired sets, surgery / humanity / upkeep / interference rules), loaded into both
`GameData.Cybernetics` and `ScavengeData.Cybernetics` (`CyberneticsData`: lookups, `TierOf`, `ActiveSets`, validation).
Each node is a prosthetic item of the same id (validated both ways) made by the recipe that outputs it.

- **Body**: `BodyPart.Eyes` (head region; `RegionEfficiency(Head)` = head × eyes; never hit or lost, only replaced
  electively). `BodyPartState` adds `HumanityCost`, `Elective`, `Upkeep` (multiplies the quality), `ServiceTicks`, and
  `Interfered` (scavenge only: locked this round, efficiency 0). `Body.Humanity` = 100 − Σ costs. `Body.Parts` fills in
  missing parts on load, so older bodies gain healthy eyes.
- **Tree** (`Cybernetics.Status(sim, node)` → Locked / Available / Known with the missing lines, recipe, owned and fitted
  counts): a way in (one requirement known — `ColonyState.KnownCybernetics`, filled from crafted, owned, fitted and booked
  parts each tick; tier 0 always — or, for tiers with `schematicSkips`, the tier schematic known), its tier and spec schematics known, the node's perk on someone at home, and the recipe's
  building level. `Cybernetics.CraftGate` makes `QueueRecipe` / `SetRecipe` answer `RecipeLocked` until then.
- **Surgery** (`ScheduleSurgery`, `CancelSurgery`; `ColonyState.Surgeries`): the patient holds a bed; each Med Bay works
  its first booking with `Cybernetics.EffortPerTick` and medicine; `Blocked` says why not (no medic, no Cyber-Surgeon for
  T3+); `SuccessChance` is the roll; a failure still fits the part and leaves a torso infection. `Cybernetics.Fit` puts a
  part on (humanity cost, elective flag, knowledge) and returns the one it replaced. Errors `NeedsSurgery`,
  `ElectiveNotAllowed` (healthy part without confirmation, below T3, or a child), `PartInjured`, `AlreadyBooked`,
  `UnknownSurgery`, `NoCybernetics`.
- **Each tick** (`CyberneticsSystem`, after relationships): knowledge, surgery, upkeep (`serviceCost` from the stockpile
  every `serviceDays`, else upkeep decays), humanity (mood cap, partner strain and breakup, children's mood cap, log
  lines). Events `SurgeryBooked`, `SurgeryDone`, `SurgeryComplication`, `SurgeryCancelled`, `ProstheticServiced`,
  `ProstheticNeedsService`, `Humanity`.
- **Effects**: node and set effects (`jobOutput`, `craftQuality`, `hooks`) add into `PerkEffects.For(survivor, data)`
  (so job output, crafting quality and colony hooks see them) while the part works. In the field, `NewScavenge` resolves
  them into `Unit.CyberHooks` (one source per part or set, with its parts); `Unit.Hook` counts a source only while none
  of its parts is interfered. Fitted items count like worn gear in `ProfileDeriver` (`ScavengeData.FittedParts`: armor,
  stealth, sight, detection, initiative, carry; their attacks at that arm's efficiency; their move modes, deduplicated at
  the cheapest AP), sets add their movement modifier, `armor_bonus` adds armor; `Hacking.Tool` takes a hacking tool
  built into a working arm (never a spare one in the pack); `Detection.ScanRadius` takes `thermal_radius`.
- **Interference** (`Interference.RoundStarted`, after `Network.RoundStarted`): Network-grade parts unlock and reroll each
  round against Linked on the Network in range, a hostile with Override, and any jammer; `LimbLocked` event.
  `Interference.Chance` serves the UI. The Linked core comes from `enemies[].drops` (chance 1: one per Linked, three per Warden).
- **UI**: `CyberneticsPanel` (from the Med Bay's "Tree" button and the Workshop / Electronics Lab production panels):
  Arms / Legs / Eyes tabs, tiers T0–T4 as columns with prerequisite lines, locked / can-be-made / known states, a detail
  pane (needs with ✓/✗, component chips, effects, humanity, upkeep, fitting, "Queue at …") and a compare card (a
  survivor's current limb vs. the design, Fit / Book surgery). `MedBayPanel` lists the surgery queue (progress, odds,
  ETA or what blocks it, cancel) and books fits (`CyberBooking`; a healthy part asks for confirmation with the humanity
  hit). The survivor Body tab shows the humanity bar and band, the eyes, and each part's tier, specialisation,
  augmentation, humanity and next service. The scavenge unit card shows tier and effects and "LOCKED" while
  interfered. `SurvivorDressing` fits the node's model id (tier tint only on a stand-in) (`CyberText` holds the shared strings).
  `--cyber-demo` (with `--cyber-tree` or `--survivor-tab=2`) stages it for screenshots.
- **Saves**: colony 12 (`SaveGame.MigrateCybernetics`: legacy parts → hook / peg below quality 1, cybernetic arm / leg
  from it, replacement humanity), scavenge 7 (survivors' cyber hooks resolved on load), protocol 6. Tests:
  `CyberneticsTests`.

### Medicine

A small illness model and the supplies that treat it; numbers in BALANCE.md "Medicine". Data: `medicine.json`
(conditions, infection, fracture, pain, painkiller, sickness kinds, Med Bay care, surgery kits, donor, field, migration,
ingredient adapters), loaded into both `GameData.Medicine` and `ScavengeData.Medicine` (`MedicineData`, validated: every
condition the rules name exists). Without the file nothing changes (no conditions, no infections, no auto-treatment).

- **Body** (so it travels with the party export and comes home on `ExtractionResult`): `Body.Conditions`
  (`BodyCondition`: id, optional part, severity 0..1, `Remaining` for passing effects, `Treated` for a splinted fracture),
  `Body.HumanityLoss` (the Calm injector; part of `Humanity`), `Body.Doses` (recent painkillers) and `PainRelief` (the
  `painkilled` effect). `BodyPartState.Dressing` (None / Bandaged / Clean; a new injury undoes it) and `ConditionPenalty`
  (a fracture's efficiency cost, lower once splinted). `Body.PartEfficiency(part)` lets a painkiller dull the injury
  penalty; `RegionEfficiency` and the arm reads in `ProfileDeriver` use it. Healing a part clears its conditions.
- **Conditions** (`ConditionKind`): wound and surgical infection (grow, block the part's healing, fever, drain health
  from a severity), fracture, food and Husk sickness (fade; Husk sickness drains), painkiller dependency (a mood dip,
  fades), the `painkilled` effect. Pain is derived: a worst injury at `pain.threshold` costs output (and
  `pain.fieldApPenalty` AP, 0 as shipped) until a painkiller dulls it. `Treatment` (Survivors, pure) holds what items and conditions do, the same at home and in the
  field: `Apply`, `Helps`, `Fracture`/`MaybeFracture`, `Afflict`, `Numb`, `OutputFactor` (into `JobOutput.ConditionFactor`,
  which `PerTick`, Med Bay care and surgery effort multiply in), `FieldApPenalty` (into the profile's MaxAp), `HealFactor`
  (an infection stops a part healing, a fracture slows it, a splint speeds it).
- **Colony** (`MedicineSystem`, after `MedicalSystem`): dirty wounds of at least `infection.minSeverity` roll
  `chancePerTick` × dressing × patient multipliers; conditions grow or fade (patients on Medicine fade faster); painkiller
  effects and doses count down; deaths go to the memorial ("died of an infected wound"). Then each Med Bay **auto-treats**
  up to `medBay.treatmentsPerTick` patients from the inventory, in `ColonyState.MedicalCare.Priorities` order (default
  Bleeding → Infection → Fracture → Sickness → Pain; `SetCarePriorities` reorders or switches them off; never painkillers
  to someone near a dependency). `TreatSurvivor(survivor, item)` is the player's override for anyone at home. Patients
  are admitted for illness too (`Medicine.NeedsCare`) and kept until nothing is left to treat. Events `Infected`,
  `FellSick`, `Recovered`, `Treated`, `BloodDonated`. **`Sickness.Apply(state, data, survivor, kind, severity)`** is the
  entry point for food-cooking (kinds `food`, `spoiled`, `raw`, `husk`, or a condition id).
- **Supplies**: items with `medical: true` come in quality tiers like gear (`HasQuality`; a variant scales `heal`,
  `reviveHp`, `cureStrength`, `painRelief`), crafted at the Med Bay (its job is `medicine`, so the Medicine skill and
  Pharmacist's `craftQuality` set the roll). `UseEffect` gains `dress`/`dressAll`, `cures`/`cureStrength`, `splint`,
  `painRelief`/`painRounds`/`painTicks`, `stimAp`/`crashAp`, `calmRounds`, `humanityCost`; `Treats` says it is a treatment.
  Recipes may need a blood donor (`donorHealth`: the order waits for `Medicine.Donor`, who gives health when the batch
  starts). Surgery uses the best kit in stock (`surgery.kits`: odds × its quality, used up) and a failed one also leaves a
  `surgical_infection` on the part. Herbal supplies take the Pantry item `herbs` as a recipe ingredient (food-cooking); `alcohol`
  is a resource of its own with a Kitchen fallback (`spirits`) until the vehicles' still outputs it. Alcohol, chemicals and
  pharma are priced in `schematics.json` `resourceValues`, so supplies trade at their quality (`base@quality` ids).
- **Merged features**: `FoodSickness.Apply` (bad, spoiled, raw food, bad rations; tainted food is Husk sickness) forwards to
  `Sickness.Apply` at `foodSicknessScale` and keeps the `FoodPoisoning` event; without medicine.json food's own "sick"
  buff applies. The electronics medic drone (`DroneRoles.HealItem`) drops the least medical item that does the job,
  dressings, splints and cures included, never a stim or the Calm injector, and applies it with `Treatment.Apply`.
  Auto-resolved raid wounds (`AutoResolve.Hurt`) are open wounds that can get infected and can fracture by the field
  rule; raid battles on the map go through `Combat` like any fight.
- **Field** (`FieldMedicine`): `UseItem` on a conscious unit heals, dresses, cures, splints and numbs (`Treatment.Apply`),
  a stim adds AP now and sets `Unit.CrashAp` for the next turn, the Calm injector sets `Unit.CalmUntilRound` (suppression
  cannot pin them) and costs humanity. Downed units are stabilized / revived as before (a blood bag revives with 45 HP).
  `FieldMedicine.ApCost` applies `medical_ap_discount` (Combat Medic). A hit leaving an arm or leg at
  `fracture.minSeverity` may break it. New rolls (fractures, infections) are `SideRoll`s off the stream's state, so they
  do not shift other systems' dice. The server runs the same code; the protocol carries the new body and unit fields.
- **Schematics** (`schematics.medicine.json`, merged into the catalogue by `SchematicCatalogLoader`): bandage, splint,
  herbal remedy, antiseptic and the basic med-kit have no schematic (the default set); the rest gate their Med Bay
  recipes (`Schematics.RecipeUnlocked`). Doc Hale's missions (M7, M15) are listed on the advanced ones; the Calm injector
  is encrypted (terminals, loot, research).
- **UI**: `MedBayPanel.Dispensary` (stock chips with the best quality, the auto-treat order: toggle, move up, reset), the
  generic `ProductionPanel` for the Med Bay's recipes and queue, patient lines with condition tags,
  `SurvivorPanel.Conditions` in the Body tab (each condition, what helps, a button per stocked item that would treat it
  now; parts show infected / fractured / splinted and their dressing), the scavenge unit card's condition line and
  medical tooltips (`MedicalText`) on action buttons, pack chips and the inventory. Item hotkeys 4–9.
  `--colony --medicine-demo` (with `--dispensary` or `--survivor-tab=2`) and `--scavenge --medicine-demo` (a blood-bag
  revive, saved with `--screenshot`) stage it.
- **Saves**: colony `SaveGame.MedicineVersion` (old saves: half the Medicine stock becomes antiseptic, 2 for 1),
  protocol `Protocol.MedicineVersion`; both keyed so a merge renumbers one constant. Tests: `MedicineTests`.

## Multiple activities

A survivor holds up to `rules.maxActivities` (3) activities at once (`Colony/Activities.cs`): each job
(`Survivor.Jobs`, the single source of truth; `AssignedBuildingId` is the first job and setting it replaces them all), a
class as student, teaching as mentor, and one construction site as builder. `AssignJob` adds a job, `StartStudy` and
`AssignBuilder` add theirs; one more past the cap fails with `TooManyActivities`. `Unassign(survivor, building)` leaves
one job or site; `Unassign(survivor)` leaves every job. **Effort splits 1/N** (`Activities.Share`): each job's output
(`Schedule.Crew` factor: gathering, harvest, crafting, Med Bay care; surgery effort and the output estimates follow),
job training and work XP (`Training.Work`), study progress and study/teaching XP, and a builder's haul and work. N = 1
is unchanged. Guard duty, research, expeditions and Med Bay beds stay exclusive and clear every job. Save version 28
writes `jobs`; older saves' `assignedBuildingId` loads as a one-job list.

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

### Gear Bench (UI)

`GearBenchPanel` (an overlay, from the "Gear Bench" button on the production panel of a `rules.benches` building):
the colony's moddable gear grouped (weapons, armor and clothing, headgear) plus survivors' fitted gun arms, each with its
mod chips; the picked piece's model with its mods (`PortraitStudio.Object`), Mk pips, the Mk kit upgrade (needs the kit
and a `rules.upgradeAt` building), and every slot of its type: the mod in it with Remove, the fitting mods in the stores
(hover previews, click fits; `GearBench.Install` says why one cannot), or locked with the Mk that opens one more. The
compare column puts `GearBench.Stats` of the build next to the previewed one with good/bad deltas (weapons: accuracy,
damage, range, AP, noise, reveal; worn gear: armor per region, melee armor, injury resist, move noise, stealth, carry;
weight and slots for both), the gained and lost effect tags, and a body silhouette of armor per region. A strip lists
the mod and kit orders in every building's queue (right-click cancels) with a picker to queue one (`CanQueue` reasons).
Network-grade mods are cyan (`GearText`), nothing else is. In the field `UnitPanel` shows each piece as "Pistol Mk II"
with mod chips, and with Jury-rig (`rules.fieldHook`) on the unit's turn Fit / Remove buttons (`FieldMod`); attack
previews add mod tags under the hit chance: true AP from `Combat.ApCost` ("+1 AP close"), extra banes that apply to
the target (EMP, SHOCK, INCENDIARY) and armor pierce.

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
  that much of the job's skill); a schematic in `schematics.json` may gate it, and `learns` marks the synthesized Decrypt orders.
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
  carry bonuses, plus an action's `bane` (its damage bonus and armor pierce) and `qualityAccuracy` (accuracy per point
  of multiplier: the Linked carbine's 0.4 makes Fine +0.10, Crude −0.08). Variants are ordinary item ids, so
  inventory, expeditions, profiles and saves need nothing special.
- **Schematics** replaced blueprints (see "Schematics" below and SCHEMATICS.md).
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
- **Saves (v4)**: version 4 stores queues, `NextOrderId` and the studied blueprints (version 15 turns them into schematics). Older saves migrate: a building's
  `recipeId`/`craftProgress`/`craftPaid` becomes one repeating order, and the old generic `blueprint` item becomes
  `bp_scanner` (`SaveGame.LegacyBlueprintItem`).
- **UI**: selecting a production building shows `ProductionPanel` (queue with progress and cancel, recipe picker with
  ×1/×5/repeat, locked reasons, inputs, quality odds; Decrypt orders while their chip is in the stores). The top bar's
  Items button opens `InventoryPanel`: items by kind and quality, and the Schematics tab (`SchematicsArchive`: every
  schematic by category with Known / To decrypt / Heard of / Unknown / Coming, Copy, Decrypt, Salvage; see SCHEMATICS.md).
  The Town Hall shows `ResearchPanel` (running research, researcher picker, heard-of schematics with Start), the Radio
  Mast `RadioPanel` (tabs from `RadioPanel.Tabs`: Calls, Trader, Contracts). Buildings render `{type}_l{level}` models, falling back to the
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
  (`Combat.ChanceToHit`) include it. Attacks aim at tiles, not units: a strike into the fog is readied blind and its
  reply is the same whether anyone stands there, so it never tells a side what it cannot see.
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
- Scenario tests: `FogTests` (stealth derivation, detection range, lookout, blind strikes into the fog, ambush + reveal + contact +
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

## Player trading

Barter between players, live (a trade window in the lobby or between allies in a mission) and asynchronous (the
Trading Post market server). Details, protocol, trust and how to run it: MULTIPLAYER.md "Trading".

- **Goods** are `TradeGood` lines: a resource or an item id with a count. Quality is part of the item id (a variant is its
  own item); `ItemExtras` (mods, Mk) ride along for weapon mods. Schematic copies are items (a blueprint / schematic item),
  so they trade like anything else and the buyer learns them the normal way. Survivors are not tradeable (a v2 idea:
  a recruit hand-over would need the survivor record, its bonds and the colony-side checks).
- **Colony ledger** (`ColonyTrade`, `ColonyState.Trade`): `Escrow` takes goods out under a transaction id (live trades on
  confirm, market posts and accepts), `Settle` forgets them when the trade goes through, `Refund` puts them back,
  `Receive` pays a claim once per id (`TradeState.Applied`), `Dispatch` sends a courier that `ColonyTrade.Run` (every tick)
  hands over on arrival (`ColonySim.CourierArrived`, a `CourierArrived` event). Live escrows left over from a lost
  connection are refunded when the colony screen opens. Colony save version 19; older saves load with an empty ledger.
- **Food** (`FoodGoods`): food items trade as Pantry items. Escrow takes fresh units only, soonest to spoil first
  (`Pantry.TakeIngredient`), and keeps the lots taken (`ItemExtras.Lots`: count, quality, tainted, time left); arrival adds
  them back as lots with the same quality, taint and time left (`Pantry.Add` with a shelf factor). Offers show the lots
  they would come from (`FoodGoods.WithLots`). Spoiled food cannot be offered.
- **Devices**: device quality is the item id; charge lives in the colony's charging queue (stores only hold full devices)
  and, in the field, in `Unit.ChargesUsed`, which `FieldTrade.Swap` moves with the device (most-spent copy first).
- **Couriers** (`Courier.Plan`): `baseDays + daysPerLeg × legs`, legs 1..`maxLegs` from the two players' names (the same
  on both sides), times every factor: `courier.buildingFactors` from data (a Garage) and `Courier.Modifiers`, a list of
  `Func<ColonySim, CourierFactor?>` other systems add to (weather, a vehicle or caravan upgrade). The Radio Mast gate
  (`courier.requiresBuilding`) applies once the game data has that building.
- **Schematic copies**: made with `Schematics.MakeCopy` (the archive's Copy, Research Data); the seller keeps the
  knowledge. A copy that arrives by trade goes through `Schematics.Receive` (learned, waiting for a decrypt, or Research
  Data when already known); a refunded copy comes back as the item.
- **Reference values** (`TradePrices`): the trader's prices, `Schematics.TradeValues` (resources, items, schematics, food).
- Net: `TradeDesk` (live trades, owned by `GameHost`), `LiveTradeSync` (client: escrow on confirm, courier on commit,
  refund on cancel), `MarketHost` + `MarketStore` (the Trading Post: JSON file, atomic writes) and `MarketClient`
  (colony-aware client: escrow, retries under the same id, claims). Game: `scripts/Trade/` (`TradeWindow`, `TradeDock`,
  `TradingPostPanel`, `TradingPostTab`, `TradeUi`), the lobby's Trade row, the Team panel's Trade buttons, the Radio
  Mast's Trading Post tab, courier toasts and world-map markers.
- Tests: `TradeTests` (Core), `TradeNetTests` and `MarketTests` (Net, loopback, including the two-player smoke script).

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

## Telegraphed plans and cards

- **Reach** (`Combat.Tiles(map, action, from, facing)`): the tiles an attack pattern covers; `Combat.Hittable` adds the
  line-of-sight rule, `CheckReach` is membership plus LOS, and `Combat.ThreatZone(sim, unit)` unions a unit's attacks.
- **Tile attacks and sub-rounds** (`TileStrikes`, command `AttackTile`, wire `attackTile`; `ScavengeState.Strikes`,
  `SubRound`): a round is `scavenge.json turn.subRounds` sub-rounds (data: 2). Every unit acts in each in initiative
  order on one turn's AP, refilled only when the round starts; `EndTurn` ends the sub-turn, `WholeTurn` the round, and a
  unit out of AP, on a stance or with a plan sits out later sub-rounds. An attack is declared on a tile and a direction:
  a weapon with `modes` covers `Combat.Covered` (stab: a line of `reach`, `diagonalReach` along a diagonal; slash: the
  front tile and its two flanks), any other action the one tile in reach. The AP goes at once and the sub-turn ends; at
  the sub-round's end strikes come down in initiative order on every hostile on a covered tile in sight, an empty tile is
  a miss (the one seen there dodged). Plans take a `mode` / `direction` too and strike every hostile on the covered tiles
  when they resolve. Tile strikes are the only way to attack: there is no command that strikes a unit at once. The AI's,
  the bots' and auto-play's strikes at once are tile strikes too (`TileStrikes.StrikeAt`: a stab / slash aimed by
  `TileStrikes.BestAim`, the aim covering the most seen hostiles; any other action at the target's tile). A strike only
  ever hits hostiles (no friendly fire), only what the action can reach (melee under a flying drone hits nothing), and
  pays its AP even on an empty tile. `Combat.Resolve` (a blow on one unit at once) is left only for what is out of turn by
  nature: overwatch reactions, card reactions and `DroneFire`. Readied strikes and patterned plans telegraph their
  covered tiles (`TacticsView`).
- **Hit tiers** (`Combat.TierOf`, `scavenge.json combat.tiers`): a landed hit is a graze (a share of the damage, no
  injury), a hit or a solid hit (more injury) by its roll margin; armor, cover and card guard shift it down. Attack
  events carry the `Tier` and the `Amount`.
- **Plans** (`PlannedActions`, command `DeclareIntent`, wire `plannedAction`; `Unit.Plan` = `PlannedAction`): a path of
  whole moves, an optional attack tile and action (or suppressing fire). Free, no noise, ends the turn, locked; it must
  fit a full turn's AP (`UnitProfile.MaxAp`, what it pays from), whatever the unit has left when it declares. It
  resolves at the start of the unit's next turn through the plain move rules and a tile strike (`TileStrikes.Strike`, a
  stab / slash's covered tiles or the one tile) from where the path ended, paid from that turn's AP: a step that no
  longer works ends the path, whoever hostile is on the tile then is struck (suppressing fire pins them instead), an empty tile is a miss with
  no refund (the aimed-at unit dodged it: `Unit.OutplayedId`, the ambush hit bonus on its next attack on the attacker,
  through the next round). Downed, killed, pinned or stunned cancels the plan.
- **Enemy AI telegraphs** (user decision: the hybrid): `ScavengeAi.Planned` runs a unit's usual behaviour (Spot,
  TryAttack, Reposition, Retreat, Suppress, shared squad focus) against a look-ahead position and AP. If it chose to
  move and then strike (or only move), it declares that plan; if its choice is to strike from where it stands (a target
  already in reach at the start of its turn), it takes its turn at once. A turret always telegraphs its aim tile.
  Recruits act at once.
- **Cards** (`data/cards.json`, `ScavengeData.Cards`): card defs (category, rarity, points, AP cost / boost,
  consumable, reaction, arm lock, `CardRequirement` any-of skill / attribute / perk hook, hard or baseline, scaling,
  effects in the game's own terms: move, hit, guard, cover, mend, draw, counter) and the deck rules (hand size, an
  optional draw cap, budget, deck caps, slot rules, village upgrades, trade values by rarity).
- **Squad deck** (`SquadDecks`, `ScavengeState.Decks`): one per party, built from the colony's deck (`PartySetup.Deck`),
  shuffled on its own stream (`ScavengeState.CardRngState`, never the game RNG); a full opening hand; each round start
  refills the points and the hand. `CardPlay` checks and applies `PlayCard` / `DiscardDeadCard`; `CardSlots` counts
  personal slots (level, skills, gear `cardSlots`, perk hook `card_slots`); `EnemyCards` deals authored enemies' cards
  (`enemies[].cards`) into one enemy deck and plays them after their plan; `BotTactics` holds the bots' heuristics.
- **Colony** (`ColonyCards`, `ColonyState.Cards`): the collection (copies per id), the deck, slot cards, upgrades;
  starter set for new and migrated colonies; cards from loot / salvage rows (the run's `Party.CardPouch`, kept only if
  someone gets out), recipe outputs (`recipes[].card`: a recipe that makes something else teaches its card once per colony,
  `CardCollection.Taught`; a recipe whose only output is the card grants it every craft), trade (`GoodKind.Card`); consumables played leave the collection.
- **Saves**: scavenge save 16 (`ScavengeSave.TileAttacksVersion`: sub-round, strikes, turn-over round; 15 loads at
  sub-round 1), from 15 (`ScavengeSave.IntentsVersion`; older runs are refused) and colony save 27
  (`SaveGame.CardsVersion`; older colonies get the starter set, and a run in progress inside one starts over from its
  expedition's seed). Protocol 19 (see MULTIPLAYER.md, "Telegraphed plans and cards over the wire").
- **Late-zone retune** (`ZoneDef.Retune`, zones.json `retune`; `LateZoneTuning`, `ZoneTuning.Applies`): a zone's
  alternative enemy count, devices, pylons and reinforcements (waves, count, `afterRounds`). `NewScavenge.Create` applies
  it when `ZoneConditions.LateZones` picks the run (Current; Expeditions: no story mission; Everywhere) and records
  `ScavengeState.Retuned`, which `Network` reads for reinforcements. The colony takes the mode from
  `ColonySim.LateZones` (set from the game's setting when a run opens; not saved). Multiplayer: `HostOptions.LateZones`
  (default Expeditions; the host's "Late zones" picker, the server's `--late-zones`), shown in `LobbyState.LateZones`.
  Tests: `LateZoneTuningTests`.
- Tests: `ReachTests`, `HitTierTests`, `InjuryEffectTests`, `PlannedActionTests`, `AiPlanTests`, `CardDataTests`,
  `SquadDeckTests`, `PlayCardTests`, `CardSlotTests`, `ColonyCardTests`, `CardSourceTests`, `BotTacticsTests`; Net
  `CardNetTests`, `FogNetTests`.
- **Bestiary** (`Bestiary`, thresholds `scavenge.json bestiary`): per NPC enemy type, how many a party saw, fought
  (attacked or was attacked by) and killed, each enemy counted once per run (`Party.Bestiary`, `SeenIds` /
  `FoughtIds` / `KilledIds`). It comes home in `ExtractionResult.Bestiary` only if someone got out and is merged into
  `ColonyState.Bestiary` (colony save 27; a save 26 starts empty). The tier (seen, fought, killed) gates what
  `Bestiary.Describe` / `List` reveal: weapons and reach, then AP and moves, then everything. Other players are never in
  it; SideFog sends a player only their own party's counts. Position, facing, plans and reach show for any seen
  attacker regardless. The tier gate is a display gate: `Bestiary.Knowledge` gives the side's tier, `Bestiary.Health`
  and `HelpContext.TargetKnown` (the hit-chance terms fold into one "Target's defence" row) apply it. Seen enemy units
  are still sent whole in snapshots (SideFog), per the existing fog model. Tests: `BestiaryTests`.
- **UI view models** (`TacticsView`, `DeckBuilder.View`, all plain C# with tests in `TacticsViewTests`): enemy
  telegraphs as a side sees them (path, strike tiles, aimed at us; never the card), the threat overlay (planned blows
  over plain reach, fog-filtered), hit previews from a tile (`Combat.ChanceToHit` with `from`, terms from
  `Breakdowns.HitChanceOf`), the hand (points, piles, slot cards, who can play each, dead cards), the character picker
  (`CardPlay.Check`: who can take a card now, and Core's reason for the rest) and the village deck builder.
  Godot draws them: `TelegraphView` (trails, pulsing strike rings, flashes on resolve / dodge), `MapView` highlight
  kinds `Threat` / `ThreatPlanned`, `Ui/HandStrip` (hand and picker), `UnitPanel` (enemy stats per bestiary tier, via
  `ScavengePresenter.KnowledgeOf` and `BestiaryKnowledge.Colony`), and the colony overlays `DeckBuilderPanel` and
  `BestiaryPanel` (Deck and Bestiary in the top bar). Sound cues are `scavenge.intent_*`, `scavenge.card_*`,
  `scavenge.countered` in `game/audio/sound_events.json`. Debug captures: `--scavenge --tactics-demo[=overlay|picker|
  reaction|enemy|preview]`, `--colony --deck-demo=deck|slots|upgrades`, `--colony --bestiary-demo`.

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
- **Anti-Network weapons**: an action's `bane` (`tag`, `hitBonus`, `damageMultiplier`, `armorPierce`, `disruptRounds`)
  applies against enemies whose `enemies[].tags` hold the tag (`Combat.Bane`): more hit chance, the damage roll
  multiplied, armor counted for less, and a hit Linked knocked off the relay (`Unit.OffRelayUntilRound`, treated as
  jammed) for that many rounds. The Linked carbine carries one against `network`; the Linked themselves carry the same
  carbine, but nothing they shoot is tagged, so it does not make them stronger.
- **Drops**: a killed enemy leaves only what its `enemies[].drops` roll (each `{item, chance}` on its own, from the sim
  RNG), never its equipment: a Linked sometimes its carbine or a schematic, a Warden more often (at most one schematic per kill).
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
  level, adults only; a schematic in `schematics.json` may gate it — the old datashards are technique schematics) and `effects`. `ColonySim.PerkStatus(s, perk)` says
  Owned / Locked (its schematic unknown) / MissingPrerequisites (with lines for the tooltip) / NoPoints / Ok.
  `ChoosePerk(survivor, perk)` spends points; errors `UnknownPerk`, `AlreadyHasPerk`, `PerkLocked`,
  `PerkRequirementsNotMet`, `NoPerkPoints`.
  Technique schematics (`sch_network_ghost`, `sch_override`, `sch_drone_payload`) unlock perks.
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
  | `surgery_success_bonus` | colony: added to the success of a surgery this medic leads | Surgeon I/II, Cyber-Surgeon |
  | `surgery_speed_bonus` | colony: surgery effort bonus when this medic leads (0.25 = +25%) | Surgeon I/II |
  | `cyber_surgery` | colony flag: can lead surgery on cyborg-grade (T3+) parts | Cyber-Surgeon |
  | `cyber_interference_reduction` | share of the Network interference chance removed | Linked Salvage |
  | `armor_bonus` | flat damage reduction added | Bulwark (armored legs set) |
  | `thermal_radius` | hostiles within it are seen through walls and stealth | Thermal / Network-grade Eye |
  | `night_detection_bonus` | detection at night (for the day-night cycle; unread yet) | Thermal / Network-grade Eye |
  | `trap_cost_reduction` | colony: share off traps' build and re-arm cost while home (best counts, ≤ 0.5) | Engineer |
  | `repair_speed_bonus` | colony: repairs this much faster while home (best counts) | Engineer |
  | `post_defense_bonus` | colony raids: a guard on duty at a post fights this much better | Sentry |
  | `sentry_lead_hours` | colony raids: hours of warning added while on guard duty | Sentry |

  Fitted cybernetics give hooks too (see "Cybernetics"); they reach the field as `Unit.CyberHooks`.

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
  the schedule, a standing Night Shift crew counted tired; `OutputPerTick` stays the on-shift rate),
  `CraftEffortPerHourAveraged` (production ETAs), `Schedule.ShiftHoursPerDay`.
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
  toward `Tick + session.TickProgress` (so it glides through each multi-second tick) and samples keyframes for the sun (east to west, warm low light at dawn and dusk),
  moonlight, sky colour, ambient and fog; whole days skipped are dropped, so fast-forward and loads never flash, and a
  paused game holds still. Lights register with `AddLight` / `AddGlow` and fade with `Night`: a lantern per building,
  an inside glow in homes and the Town Hall, a floodlight mast on lit outdoor workplaces that burns while the Night Shift
  is on, the campfire (flickers, brighter at night) and six perimeter torches; at most 12 lamp OmniLights, the rest only
  glow. `SurvivorWalkers` send sleepers home (patients to the Med Bay door) and in, children first by their bedtime;
  night-shift workers stay at their site; the awake evening crowd sits at the campfire. `MenuBackdrop` holds the light
  at dusk. HUD: phase icon and name in the `TopBar` (tooltip: hours, who works or sleeps, night shifts), Night Shift
  toggle with its penalty and the Floodlight on the `BuildingPanel`, asleep / night shift / tired badges on survivor
  cards, a toast on a shift's first night (later nights only log).
- **Night missions** (`Scavenge/FieldEnvironment.cs`): every run has a `ScavengeEnvironment` (`Hour`, `Phase`,
  `Weather` slot) in `ScavengeState.Environment`, fixed when it is made (`ZoneConditions.Environment`). The colony gives
  a mission the hour its party arrives (`ColonySim.MissionEnvironment(expedition.ArrivalTick)`); `LaunchExpedition(…,
  WaitFor)` lets the party wait outside for a phase, at `rationFoodPerMemberPerDay` per day waited (`ColonySim.PlanArrival`
  previews arrival, phase, wait and food; the planner shows it). Effects are data (`scavenge.json environment.phases`,
  `environment.weather`): per `SenseKind` (Human, Husk, Linked, Drone, Camera, Turret) a sight multiplier (floored at
  `minSight`), a stealth bonus against every observer kind not in `stealthIgnoredBy`, Husk count and hearing multipliers,
  a flare radius multiplier. A weather entry combines with the phase (multipliers multiply, bonuses add), so the weather
  pass only adds data and sets `Weather`. Night: people see ×0.6, Husks ×0.8, drones ×0.8, the Linked and cameras ×1;
  +15 stealth except to the Linked's sensors, cameras and turrets (a hacked camera still sees for its side); ×1.25 Husks
  (extra Husks from the Husk part of the mix, so day runs are unchanged) who hear ×1.5 further; flares light ×1.25 further.
  A muzzle flash or sensor reveal is seen with the observer's sight before the dark (`Detection.SeesRevealed`).
  Applied in `Detection.Spots`, `Visibility.SideVisible`, `ScavengeSim.Profile` (sight shown in the HUD), `MakeNoise`,
  flare use and `NewScavenge`. Scavenge save version 6 (older runs: midday), colony save 11 (`Expedition.WaitTicks`).
  Multiplayer: the server decides the hour (see MULTIPLAYER.md "Night over the wire"). Presentation: the scavenge screen
  uses `DayNightLighting.SetupFixed(hour)` (no distance fog), a short OmniLight per ranged shot, flares' lights as
  before, the hour and phase on the turn bar. `DayNightLighting.Weather` takes the sampled `SkyLook` and returns it
  changed, for the weather pass. `--scavenge --hour=H` plays the debug mission at an hour.
- **For audio**: switch on `ColonySession.Phase` / `PhaseChanged(DayPhase)` (raised after the tick that crossed into a
  phase, after a fast-forward and after a load), or `DayNight.PhaseAt(tick, rules)` in Core — not on raw hours.
  `ColonyAudio.IsNight(DayPhase)` does: the night bed and music for Night, the day ones for Dawn, Day and Dusk.

## Schematics

Design and numbers: SCHEMATICS.md (its "As built" lists where the build differs). Core: `src/Remnants.Core/Schematics/`,
self-contained; data: `data/schematics.json` (+ `schematics.<feature>.json` extensions).

- **Catalogue** (`SchematicDefs.cs`): `SchematicDef` (id, name, find tier 1–4, `encrypted`, `default`, `planned`,
  `unlocks` of kind Building / BuildingLevel / Recipe / Perk / Prosthetic `tier:N`|`spec:ID` / Vehicle, `sources`,
  `storyMissions`), `SchematicRules`, `TraderDef`. `SchematicCatalog` indexes it both ways (`GateOfRecipe`,
  `GateOfBuildingLevel`, `GateOfPerk`, `GateOf(kind, id)`) and is shared by `GameData.Schematics` and
  `ScavengeData.Schematics`; `ScavengeData` makes one `ItemKind.Schematic` item per schematic (same id), and
  `GameData` one Decrypt recipe (`decrypt:<id>`, `RecipeDef.Learns`) per encrypted one. Nothing gated = default set.
- **API** (`Schematics.cs`, for the story campaign and others): `Grant(colony, id, source)` (learn now; known → its
  duplicate value in Research Data), `Receive` (a copy reached home: plain → learned, encrypted → a chip in the inventory
  for a Decrypt order, known → Research Data), `Has`, `HearOf(colony, id, where)`, `Status`, `RecipeUnlocked`,
  `PerkUnlocked`, `LevelUnlocked`, `MakeCopy`. `ColonySim.SchematicUnlocked` fires on every learn (also logged).
- **Gates**: `Construct` / `Upgrade` fail with `SchematicNeeded`; `CraftingHandlers.CheckRecipe` with `RecipeLocked`;
  `Progression.Status` locks perks; `Cybernetics.Missing` adds the tier and spec schematics (`TierGate`, `SpecGate`).
- **Field** (`SchematicDrops.cs`): each cache / locker / terminal item first rolls the site's chance at the zone's
  find tier (its threat) and picks from the `Loot` (or `Terminal`) pool: tier ≤ zone, own tier ×2, known ×½
  (`ScavengeState.KnownSchematics`, from `ZoneConditions`). Enemy drops: first schematic hit only. A failed terminal hack
  records its schematics in `ScavengeState.HeardSchematics`. `ExtractionResult.HeardOf` (seen but left behind) and
  `SharedSchematics` (co-op: allies on the side who got out share their copies) go home; `ExpeditionHandlers.Stow`
  calls `Receive` for each schematic item in the packs; the report lists `Schematics` (id → outcome).
- **Research** (`SchematicCommands.cs`, `Research`): `StartResearch(id, researcher)` / `CancelResearch`; one order at a
  time in `SchematicsState.Research`, run after study in the tick. `SalvageSchematic` turns a chip into Research Data.
- **Radio Mast** (`RadioMast.cs`, run after the world map each tick): the call queue (`Enqueue`, `Calls`, the
  `AnswerCall` command → `ColonySim.RadioCallAnswered`, `RadioCallReceived`), trader visits (`TraderAvailable`, stock
  rolled from unknown `Trade` schematics, `BuyFromTrader`, `SellToTrader`, `SetTrader` for Story mode) and contracts
  (`AcceptContract`; paid by `RadioMast.PartyHome`). `TradeValues` gives reference values in scrap.
- **Saves**: colony version 15 (`ColonyState.Schematics`, `ColonyState.Radio`); `SaveGame.MigrateSchematics` renames
  `bp_*`/`ds_*` to `sch_*`, grants every newly gated schematic the colony was already using (levels it has, queued
  recipes, Camo Cloak / Grappling Hook with Tailor / Workshop L2, the tiers and specs of cybernetics designs it knows,
  owns or has fitted), learns plain schematic items in stock, turns known copies into Research Data, sets `Migrated`.
- Tests: `SchematicsTests.cs` (data rules incl. no story-only schematic, the default set, learning and decrypting,
  gates, research, trader and calls, contracts, drops, migration, copies and trade values).
## Story mode

The spec is STORY.md. The data format, field by field, is docs/story/FORMAT.md. Core: `Story/` (campaign) and
`Scavenge/FieldMission.cs`, `MissionRules.cs`, `HandmadeMaps.cs` (the field side, which runs without the Story module).

- **Data** (`data/story/`, `StoryDataLoader`, `StoryData.Validate`): `characters.json`, `missions/*.json` (campaign
  order), `dialogue/*.json`, `maps/*.json`. `ColonySim.Story` holds it; the game sets it on every colony, a Sandbox one
  never uses it.
- **Campaign** (`ColonyState.Campaign`, null = Sandbox): mission progress (Locked / Available / Active / Done(outcome) /
  Failed(retry tick)), flags, characters, the radio inbox, the open dialogue, the dialogue log, the last debrief.
  `Campaign.Start` makes a fresh colony a Story game: one starting child becomes Pell (`Survivor.UniqueId`).
  `Campaign.Tick` runs after the colony tick and the world map: retries come back, missions whose `requires` hold
  unlock in data order and queue their briefing call, and colony missions (M1, the colony tasks) check their
  objectives. A scene objective calls once the others are done, at its time of day.
- **Field missions**: `LaunchStoryMission(mission, members)` launches through the expedition rules (a mission may allow
  a bigger party) and tags `Expedition.MissionId`. `OpenExpeditionScavenge` builds the run from `Campaign.Setup`: the
  script, the handmade map (perimeter variant by Town Hall level), the fixed room, the watchtower posts, a retry's
  extra enemies. `ApplyNow` hands the `ObjectiveResult` to `Campaign.Returned`, which adds the field flags and then
  pays rewards (done), applies the fail branch (failed), or leaves the mission open (left early).
- **Rewards**: schematics through `Campaign.GrantSchematic` → `Schematics.Grant(colony, id, SchematicSource.Story)` (a
  known one pays its duplicate value in Research Data, the `research_data` resource; `ResearchData` effects add to it
  too). Features not built yet (vehicles) grant the schematic and a `Fallback` reward. Story saves before colony
  version 15 hand the campaign's own schematics and Research Data flag to the module on load.
- **Calls ring on the Radio Mast**: `Campaign.QueueCall` records the call in the campaign (`Inbox`: which dialogue it
  opens, when) and, once its time comes, `RadioMast.Enqueue`s it (kind Story, tag `story:<callId>`, `RadioCall.Rung`).
  Answering it there (`AnswerCall` → `ColonySim.RadioCallAnswered` → `Campaign.Answered`) opens the dialogue like
  `TakeCall`; taking it from the story HUD clears it from the mast's queue.
- **Dialogue** (`Dialogue`): graphs of lines with up to three choices. Node effects apply when a line is shown, choice
  effects when it is picked; conditional lines and choices use `ConditionDef`. Placeholders are resolved in Core.
  Commands: `TakeCall`, `AdvanceDialogue`, `ChooseDialogue`; the UI only renders `Dialogue.View`.
- **In the field** (`MissionRules`, `ScavengeState.Mission`): objectives (reach, hack, retrieve, escort, aid,
  survive, defend, kill tag, undetected) are re-checked after every command and AI turn. Triggers fire once each: on
  start, round, area entered, objective done or failed, item picked up, a tagged unit seen, alarm, prompt chosen.
  Their actions are spawn, chatter, activate / complete / fail, field flags, change sides, follow, prompt, split the
  loot and wake. A prompt blocks every command but `ChooseOption`. A mission at home (`AtHome`, the perimeter defense)
  ends the run when it is done or failed, with everyone still standing home. Story units carry `Unit.Tag`,
  `CharacterId` (never a colony recruit; they come back in `ObjectiveResult.Escorted`), `NoRecruit`, `Patrol` and
  `Raised` (a watchtower post: + sight and detection, `scavenge.json` `story`). Structures (the Radio Mast) are units
  on the party's side that take no turns. Neutral bands (the Tally) take no turns until a trigger makes them hostile
  (`band:<tag>`, a foe to everyone).
- **Maps** (`HandmadeMaps`): an ASCII grid plus a legend of named markers. Fixtures sit at markers: caches, devices,
  enemies with patrols, NPCs, structures, towers. `Validate` checks the grid, the fixtures and that every tile can be
  reached (doors count as passable). `Stamp` puts a fixed room into a generated map: a ring of floor round it, paths cut
  to the spawn and every exit, and no random fixtures inside. `NewScavenge.Create(..., MissionSetup)` takes either a
  handmade map or a generated map with a room. A raid system can pass its own `HandmadeMapDef` built from the colony
  layout.
- **Saves**: colony version 14 (13 is held by the schematics branch; `Campaign`, `Survivor.UniqueId`, `Expedition.MissionId`), scavenge version 8
  (`Mission`: the whole field script is copied in, plus the map id and `HandmadeMaps.Hash`; units' and devices' story
  fields). Older saves load as Sandbox and as plain runs. Day-night, prosthetics and schematics take versions too;
  whichever merges later renumbers.
- **Game** (`game/scripts/Story/`):
  - `StoryHud` in the colony HUD: a strip under the top bar (radio with waiting calls, journal, next objective),
    `RadioPanel`, `JournalPanel`, `BriefingPanel` (Plan the party → the expedition planner in mission mode),
    `DebriefPanel`, and `DialoguePanel` (portrait by character id and mood, radio frame variant, typewriter, choices
    1–3 with hints, Skip, log). Colony time pauses while a dialogue is open.
  - `StoryArt` resolves portraits, markers and frames from `assets/generated`.
  - `WorldMapPanel` draws gold / hollow diamonds on zone edges (pulsing while new), radio waves at the colony, Marrow's
    truck on its road, and a story section on the zone card.
  - In the scavenge HUD: `MissionPanel` (objectives with progress), `ChatterStrip`, the prompt as a `DialoguePanel`,
    and `ObjectiveMarkers` (beams on objective tiles, watchtower posts).
  - New Game asks Story or Sandbox (`NewGameChoice`).
  - Debug: `--story` (with `--colony`), `--story-demo=radio|dialogue|worldmap|briefing|journal`,
    `--scavenge --story-mission=ID [--th=N] [--rounds=N]`.
- **Acts 2–4** (`Story/StoryCharacters.cs`, `Story/StoryWorld.cs`; content in `docs/story/`):
  - *Unique survivors*: a character with a `survivor` def joins as a colony survivor (`StoryCharacters.Join`: fixed
    stats, perks, story traits from `traits.json`). A trait's hooks are copied onto `Survivor.TraitHooks` and read like
    perk hooks at home (`PerkEffects.For`) and in the field (`Unit.Hooks`). Daily rules: Seven's humanity grows back
    (`Body.HumanityLoss` shrinks to a cap of 60) and unsettles the others under 30; Cole keeps score; Pell's mood
    follows the colony's humanity. Doc's oath counts each Network-grade part fitted (`doc_t4_count`): a warning, then
    she leaves. A joined character missing from the roster is dead if in the memorial, else left.
  - *Conditions and effects*: humanity (colony or one survivor, `of`), joined / dead / adult, skill, `any` / `not`;
    mood, humanity, leaves, dies, close a mission, world effects, endings, recruits, the taken coming home, renames.
    "key" names the key-carrier. Each objective that came off comes home as flag `<mission>_<objective>`.
  - *Missions*: `repeatDays` (Doc's Rounds), `chainFrom` (the Spire's three floors: only who came out of the floor
    below may go on), `mustBring` (a character or "key"), `hostOnly`; a fail effect may close a mission for good.
    The caravan's visits count into `marrow_visits` (M6).
  - *World effects* (`StoryWorld`): `relay_down`, `truce` (Halcyon's Offer; launching a Relay or Spire mission during it
    breaks it: `halcyon_offer` 3 and `m14b`), and the endings' `silence`, `terms`, `second_voice`. They reach the zones
    as `ZoneConditions.NetworkWeight`, `HuskCountMultiplier`, `NetworkNeutral`.
  - *Endings, epilogue, Codex*: `endings.json` (requires, closed text, world effects, slides whose first matching
    variant shows), the `Ending` effect (the Calm ends the game: `CampaignState.Over`), `codex.json` pages unlocked and
    grown by conditions. The taken (M14) leave the roster for `CampaignState.Taken`.
  - *Field*: `Carry` (heavy work by N survivors next to a marker) and `Keep` (at most N in 100 taken) objectives;
    `After` (rounds after an objective starts), `Hurt` and `Taken` triggers; `if` guards on triggers and prompt options
    (run flags, else the campaign's at launch); `Leave`, `Lock`, `Open` actions; `takeTag` (the Linked carry off the
    downed); `fuseAfter` (failed hacks destroy a node); `Reach` `by` a tag; party survivors tagged by `MissionSetup.Tags`.
  - *Co-op story v1*: `HostOptions.Mission` (`--story-mission ID` on the server, `Campaign.CoopSetup`): the first seat
    is the host; the mission goes to the host's team only (`SideFog`), without campaign flags; a guest's result
    carries `GuestSchematics` and no campaign progress. Handmade maps put a team on one side. Protocol 15
    (`Protocol.StoryVersion`; added in stabilisation pass 1, the feature had shipped without a bump).
  - *Game*: `CodexPanel` and `EpiloguePanel` in the story HUD; `--story-demo=offer|join|codex|epilogue
    [--ending=ID] [--slide=N] [--codex-tab=people|logs|story]`.
  - *Playtest*: `--story-acts[=terms|silence|voice|calm|all] [--seed=N] [--trace]` plays Acts 2–4 from Act 1 done to an
    ending with a strong, kept-up colony; each variant picks its dialogue choices and field prompt options.
  - *Saves*: colony version 25, scavenge version 14.
- **Save migration chain**: `SaveMigrationChainTests` loads real old saves (`tests/Remnants.Core.Tests/Fixtures/saves`:
  colony v1 from 80b13f7, colony v2 with a scavenge v1 run in progress from 6b1a2d4^, and that run alone), written by
  the code of the time, through every migration to the current version with all data on; each plays three days (or
  three rounds) and saves and loads again. Pre-grid v1 saves get plots in building order (`SaveGame.MigratePlots`).
  To add a fixture: `git archive <commit>` into a scratch folder, write a save there with a few lines against its Core.
- **Playtest**: `--story [--prompt=N]` plays a Story game. The colony bot takes calls (first choice), builds the Radio
  Mast and a Watchtower, and launches open story missions; `StoryBot` plays them by their objectives. See BALANCE.md
  "Story Act 1 pass".
- **Tests**: `StoryDataTests` (data valid, rewards are catalogue schematics, maps connected, door passable, room
  stamping), `CampaignTests` (Pell, M1 → M2 with the dusk scene, unlock order, fail and retry, failed-for-good, rewards
  through the schematics module, calls on the Radio Mast, leaving early, saves mid-campaign and mid-mission, perimeter variants and towers),
  `DialogueTests` (choices, effects, conditions, conditional lines, placeholders, Sandbox), `ObjectiveTests` (each kind
  passes and fails; triggers once; prompts block and branch; patrols; wipe; determinism).
## Vehicles

All numbers live in `data/vehicles.json` (tiers, upgrades, road rules, weather effects), the Garage / Biofuel Still in
`buildings.json`, parts and vehicle builds in `recipes.json`, parts and schematic items in `items.json`, the field rules in
`scavenge.json` (`vehicle`, `salvage`, terrains `Vehicle` / `ArmoredVehicle`), the Mechanics discipline in `perks.json`, and
the schematic catalogue in `data/schematics.vehicles.json`. Core: `Data/Vehicles.cs`, `Colony/Vehicles.cs` (`VehicleRules`),
`Colony/VehicleSchematics.cs`, `Scavenge/ParkedVehicles.cs`, `Scavenge/Salvaging.cs`.

- **Tiers**: T0 Handcart (every colony starts with one), T1 Bicycle Cart, T2 Motorbike, T3 Buggy, T4 Truck, T5 Armored
  Rig: speed, seats (null = any), cargo kg, fuel per travel day, range on a tank, noise, max condition (= HP when parked),
  armor, slots. A vehicle is a `ColonyState.Vehicles` entry (tier, name, condition, fitted upgrades, `Away`).
- **Building one** is a Garage recipe with `vehicle: TIER` (parts as `itemInputs`, schematic as `blueprint`), so it takes
  workers, effort and the queue like any batch; the finished batch rolls the vehicle out (`VehicleBuilt`). Parts (wheel,
  spare parts, chassis frame, fuel can, small engine, freight diesel, armor plate) are `vehiclePart` items: they get
  Crude / Standard / Fine variants (`{id}@{tier}`) rolled from the crafter's **Mechanics** skill (new; the Garage job).
  Fuel: the Biofuel Still turns crops (any `tag:plant` ingredient; L2: wood) into the `fuel` resource; it also distils `spirits` → alcohol.
- **Slots and upgrades** (`FitUpgrade`, `RemoveUpgrade`, `RepairVehicle`; Garage level, schematic, tier, a free slot): Engine
  (tuned engine, turbo, muffler, biofuel conversion), Tyres (off-road, run-flats, desert), Armor (plates, cage, ram) and
  Utility (cargo rack, trailer, fuel cans, headlights, winch, roof turret, smoke launcher, radio booster, drone pad, medkit
  locker, mobile workbench, solar charger). The handcart has one utility slot, the rig the most; Garage L3 adds a utility
  slot to every vehicle. Parts are paid best quality first; their mean quality multiplier scales the effect (m → 1 + (m −
  1)·q, a → a·q); a Tuner at the Garage adds `engine_tune_bonus` to engine upgrades. Taking one off returns half its parts at
  its quality. `VehicleRules.Stats` sums a vehicle's working upgrades; `CanFit` says why not.
- **Wear**: the road wears the vehicle and every fitted upgrade (`wearPerTravelDay` each way); HP lost in a mission wears the
  upgrades too (`combatWearToParts`). An upgrade below `partBrokenBelow` stays fitted but does nothing; a vehicle below
  `minLaunchCondition` cannot leave. `RepairVehicle` spends spare parts on the hull, then the most worn parts; a Grease Monkey
  at the Garage needs fewer (`repair_parts_discount`). Nothing is ever deleted by wear: only a wreck (or a vehicle left in
  the zone) is lost.
- **Travel** (`VehicleRules.Plan` → `VehicleTrip`, `ColonySim.PlanTravel`): the world map's planner
  (`WorldMap.Travel`: base time plus the weather's delay or closed route) with the vehicle's own `TravelModifier`s on top —
  its speed and upgrades (multipliers), what the weather on the road does to vehicles (`vehicles.json weather` by the live
  weather kind, `VehicleRules.WeatherKind`; `ColonySim.WeatherAt` overrides it for tests: block some tiers, slow all, mud
  slows wheels unless all-terrain; desert tyres or a ram drive a blocked road slowly instead), and the dark (a motor vehicle
  without headlights leaving or arriving at night). Range on a tank blocks a trip. On foot the zone's days stand. Rations
  are planned on the time moving (`VehicleTrip.MovingTicks`), the weather's delays add their own. `LaunchExpedition(…, VehicleId)` checks seats (with the Town Hall cap,
  `MaxPartySizeWith`), condition, the plan, fuel for the round trip and rations for the shorter trip; the way home is
  planned when the run ends (`Expedition.ReturnTravelTicks`). The vehicle's noise (none under a smoke launcher) draws that
  share more Husks on arrival (`ZoneConditions.HuskMultiplier`). A working vehicle at home extends drone scouting
  (`WorldMap.DroneReachDays`); a radio booster adds `VehicleRules.RadioRangeBonus` for the Radio Mast.
- **In the field** (`PartySetup.Vehicle` = `FieldVehicle`): the vehicle parks on the spawn edge next to the party, its tile
  impassable cover (hard cover when armored). The side extracts from any tile next to it; `StashItem` loads carried items
  into it within its cargo (`TakeFromStash` with a mobile workbench); at round start a workbench mends it, a solar charger
  refills adjacent drone batteries (`VehiclePower`), a roof turret shoots the nearest hostile in range, and every adjacent
  enemy hits it (armor, run-flats). At 0 HP it is wrecked: stash lost, no extraction. The result carries `VehicleResult`
  (HP, wrecked, stash); the stash counts as loot when someone drove it home; a medkit locker heals the riders.
- **Wreck parts**: stripping a wreck is the generic `Salvage` on a `wrecked_car` salvage node (see "Field loot and
  salvage"); each lot adds `Salvaging.Roll` (`FieldSalvage.Extras["wrecked_car"]`): `rollsByLootTier` rolls on the
  scavenge.json `salvage` table (`WreckSalvageRules`; big parts only with the party's vehicle parked, a winch adds a roll)
  at the zone tier's quality. `Salvaging.Wrecks` still pairs cover tiles as the map draws cars; `SalvagedWrecks` is kept
  for older saves.
- **Schematics**: `data/schematics.vehicles.json` in the schematics catalogue: recipes (vehicle builds, engines, plates),
  the Garage's levels, the Biofuel Still, and `Vehicle` unlocks naming upgrades (checked by the validator
  `VehicleSchematics` registers). Upgrades ask `VehicleSchematics.Has` (= `Schematics.Has`); defaults (cargo rack, fuel
  cans, spare parts, handcart) need none.
- **Saves**: colony version 23 (vehicles; the expedition's vehicle, noise, fuel, return trip; older colonies get the
  handcart), scavenge version 12 (parked vehicles, stripped wrecks); protocol 13.
- **Game**: `GaragePanel` (opens with the Garage selected: vehicles, slot chips with quality and condition, fit preview with
  before → after numbers, parts by quality, repairs), the expedition planner's vehicle picker (days, fuel, seats, cargo,
  noise, terms), the vehicle token on the world map, `MapView` parks `vehicle_parked_extraction_t{tier}` (or the cart model)
  on its tile, `ActionPanel` has the vehicle chip, Load (R), Take, the node Salvage (I) and Extract by the vehicle (X); the after-action
  report says which vehicle came home or was lost. Audio: vehicle events, the Garage loop, parked engine idle.
- Tests: `VehicleTests`, `VehicleUpgradeTests`, `VehicleNetTests`.

## Weather

Colony and world in `data/weather.json`, field effects in `scavenge.json environment.weather` (same ids); Core is
`Colony/Weather.cs` and `Data/Weather.cs`, game-side `Weather/WeatherView.cs` and `Weather/ColonyWeather.cs` (namespace
`Remnants.Game.WeatherFx`, so it does not shadow Core's `Weather`).

- **Spells** (`WeatherSpell`: index, kind, start, end, intensity): per region a Markov chain; the next kind is weighted by
  `next[previous]` × `regions[region]` (the wastes get more dust), its hours and intensity drawn from the kind's ranges.
  Each spell comes from a hash of (`WeatherState.Seed`, region, index), so the weather never depends on when it was asked
  for and never touches the colony RNG (lightning strikes are the one draw from it, only in a storm).
  `ColonyState.Weather` keeps the seed and a cache of spells (pruned three days back). The seed comes from the colony's
  RNG state the first time (new games and older saves). Disabled (no file, or `enabled: false`) is always clear.
  `TestData.Real` and `RealWithNights` have weather off; `RealWithWeather` is the data as shipped.
- **Queries**: `Weather.Current`, `SpellAt(region, tick)`, `SpellsBetween`, `RegionOf(zone)`, `GroundsDrones`,
  `Forecast` (days by Town Hall level, +1 with an Electronics Lab L2; the next call's accuracy from base, Lab level and a
  scanner in the stores (`instrumentItems`), less per day ahead; a miss names the kind's `confusedWith`; stable per
  spell), `Masks`, `Masked`, `OutdoorCrew`.
- **At home** (`WeatherSystem`, after day and night; `Schedule.Crew` multiplies `Weather.WorkFactor`, so the Night Shift
  and the weather stack): outdoor work × `outdoorOutput`; in a dust storm a worker with a mask works at `maskedOutput`,
  without one at `outdoorOutput` and loses health (never below 25); children stay in; mood per tick; no campfire;
  lightning damages a building (`Building.Damaged`: × `repair.damagedOutput` until `RepairBuilding`, `repair.cost`;
  error `NotDamaged`). Logs `WeatherChanged`, `Storm` (a severe kind starting or ending: toasts), `LightningStruck`,
  `Repaired`.
- **Crops: one hook.** `Crops.Modifiers(data, spell)` returns `CropModifier(Source, Reason, Multiplier)` terms (now only
  the weather's `cropOutput`: rain 1.3, storm 1.1, dust 0.6); the food system applies them per
  weather-sensitive harvest at outdoor buildings (`HarvestSystem`; the Greenhouse is indoors), see "Food and cooking".
- **Dust masks**: `dust_mask` ("Dust Mask & Goggles", Tailor L1, no blueprint, so default-known; see SCHEMATICS.md). It is
  a Material for now and counted through `weather.json maskItems`, so moving it to a Headgear slot is a data change.
- **On the road: the travel planner.** `WorldMap.Travel(sim, zone, departTick, homeward)` gives a `TravelPlan`: the zone's
  base ticks and the `TravelModifier(Source, Reason, ExtraTicks, Multiplier, Blocks)` terms from every source in
  `WorldMap.TravelTerms`; multipliers multiply the base, extra ticks add, any block blocks. Weather is one source
  (`Weather.TravelTerm`): the worse of home and the zone's weather at departure adds `travel.extraDays`; a kind with
  `travel.blocksFromDays`, raging over the zone, closes routes that long or longer (only outbound). Vehicles add their own
  term, independently. `PlanArrival` returns the plan (`Travel`, `DelayRationsPerMember`, `ExtraRationsPerMember`,
  `Blocked`); `LaunchExpedition` fails with `RouteBlocked`, and pays rations for the delay; `Expedition.TravelTicks` is the
  way out, then the way home (planned when the run ends). `SendScoutDrone` fails with `DronesGrounded` in a dust storm or
  heavy rain at home or over the zone (`WorldMap.DronesGrounded`).
- **In the field**: `ColonySim.MissionEnvironment(tick, zone)` sets the weather over the zone's region at arrival and the
  spells starting within `field.lookaheadHours` as `ScavengeEnvironment.Shifts` (round `1 + hours × roundsPerHour`).
  `FieldEnvironment.RoundStarted` applies a shift due (`WeatherChanged`), then rolls thunder (`Thunder`: no movement
  contacts this round) and lightning (`Lightning`: every unit on the map seen by every side this round) from the run's RNG,
  only when the weather has them. `EnvironmentEffects` adds `hearingMultiplier` (noise and footsteps), `flareRoundsMultiplier`,
  `rangedAccuracyPenalty` (`Combat.WeatherPenalty`), `dronesGrounded` (Deploy / Fly fail with `DroneGrounded`; Recall
  works), `relayRangeMultiplier` (`Network.Groups`), `thunderChance`, `lightningChance`. Rain: shorter hearing and flares,
  a little stealth; fog: sight much lower; dust: sight and aim down, drones grounded, the Linked's links halved, Husks unaffected
  (their sight stays, and they see through its stealth); thunderstorm: rain plus thunder, lightning and grounded drones.
  Scavenge save 9, colony save 16.
- **Presentation** (no rules): `WeatherView` (made by `ColonyScreen` and `ScavengeScreen`) keeps GPU particles (rain
  streaks, dust puffs, debris) over the ground point the camera looks at, grades the time of day's `SkyLook`
  (`DayNightLighting.Weather`: wet and dark under rain, grey overcast, a brown cast and haze in dust, ground mist
  (`SkyLook.GroundFog`, height fog that also works on the tactical map) in fog), crossfades between kinds over 3 s, and
  flashes for lightning (`Strike`; the colony strikes on its own in a thunderstorm, the field on Core's `Lightning`).
  `GameSettings.WeatherEffects`: Full; Reduced (a third of the particles, no flashes); Off (tint only). `ColonyWeather`
  follows `Weather.Current` into the view and `WeatherAudio` (`WeatherSounds.AudioKind`: rain, heavy rain from 0.75 and in
  storms, dust storm, wind when overcast); gusts rattle debris in dust. HUD: the top bar's weather chip (tooltip: what it
  does, masks, the forecast), toasts for storms and lightning, Repair on the building panel, the planner's travel terms and
  closed routes, the turn bar's weather chip (tooltip: field effects), log icons. `--weather=KIND[:INTENSITY]` forces it
  in the colony (from now on) and in the debug mission. Not done yet: wind on trees and flags, a wet-surface shader.
- **Multiplayer**: the server decides it (see MULTIPLAYER.md "Weather over the wire").
## Food and cooking

Food is ordinary inventory items (`items.json` kind `Food`); `data/food.json` says what each does. Core is
`Colony/Pantry.cs`, `Colony/FoodSystem.cs`, `Colony/Harvest.cs`, `Colony/Rations.cs`, `Data/Food.cs`; tests are
`FoodCookingTests.cs`.

- **Pantry** (`Pantry`, `ColonyState.Food`): per food item a list of lots (count, age, shelf, quality tier, tainted)
  plus the opened portions being eaten. Lots are the items' instance data and follow `Inventory.Counts`:
  `Pantry.Reconcile` turns items added by anything (loot, trade, crafting) into a fresh lot and takes missing ones from
  the oldest lots, so other branches can move food as plain items. Queries: `FoodValue` (fresh food in the old food
  units, what `ColonySim.FoodStock` / `FoodDays` read), `TakeFoodValue` (for food-priced costs elsewhere), `Entries` (the
  pantry view), `Available` / `TakeIngredient` (recipe inputs by item id or `tag:x`, soonest to spoil first).
- **Spoilage**: lots age a tick per tick (Cold Cellar `cellarRateByLevel` for `cellar` items; a powered cellar
  `poweredCellarMultiplier` again through the `FoodPower` adapter, false until the power grid merges). Past its shelf a lot
  is spoiled (no longer food value, eaten only when nothing else is left, with a sickness chance) and rots away after
  `spoilage.spoiledDays` (`FoodRotted`).
- **Eating** (`FoodSystem`, same rates as before: adult 0.05/h): children first, each picks by a score (cooked first,
  quality, mood, health, buffs; minus their recent share of that dish; +0.3 for what spoils within a day; −1 for
  long-keeping food, kept for the road). A dish adds mood and health (× quality effect), its buffs, the hot-supper bonus
  between dusk and dawn (×`campfireSupperMultiplier` with a Campfire), and moves `Survivor.Nutrition` towards its
  nutrition. Starving gives Hungry; Bland (no cooked meal for `blandAfterHours`), Malnourished (nutrition under
  `malnourishedBelow`) and Same Again (one dish over `variety.share` of a fading diet) follow the diet.
- **Buffs** (`FoodEffects`, `Survivor.Buffs`): output (in `JobOutput.PerTick`), healing (Med Bay care and natural healing),
  mood and health per tick, sleep debt when a stim wears off, AP on the first field round. Sickness goes through one
  function, `FoodSickness.Apply` (the Food Poisoning debuff; the medicine-crafting illness hook replaces its body).
- **Children**: a malnourished tick counts as hungry growth; attribute growth runs at `lerp(growthAtLow, growthAtHigh,
  nutrition)` of the base rate.
- **Harvests** (`HarvestSystem`, `SetHarvest`, `Building.Harvest`/`HarvestProgress`/`HarvestReadyTick`): Farm crops
  (potatoes, beans, corn, herbs), Greenhouse (vegetables, herbs; indoors, no weather), Hunting Lodge (game with hides →
  cloth, Husk-zone game that is tainted, forage). Effort = job output × yield (× `Crops.Multiplier` for weather-sensitive
  harvests outdoors: rain waters them, dust buries them) accrues per output item; whole items come in once `growDays`
  have passed since planting or switching (switching loses the standing crop).
  `ColonySim.OutputPerTick/PerDay` give a harvest building's food value.
- **Cooking** (Campfire, Kitchen L1–3, Smokehouse): ordinary recipes with `ingredients` (item ids or `tag:x`). A food
  output is rolled like gear quality (cook's Cooking skill, level, `craftQuality.cooking` perks: Camp Cook, Chef) and
  becomes a lot; tainted inputs stay tainted at `quality.taintKept`; preserved output keeps longer by the cook's
  `shelf_life_bonus` hook (Preserver).
- **Rations** (`Rations`, `LaunchExpedition.Rations`): the need stays a food-value number (`WorldMap.RationsPerMember`
  plus the wait), so vehicles can price days on the road; `RationChoice.Prefer` packs one item first, the rest is food
  that keeps until the party is home. Risky food (spoiled, tainted, spoiling on the road) may sicken members at launch;
  a dish with a `fieldBuff` (one per member, standard quality or better) gives Fed for the Road, read into
  `PartyMember.FirstRoundAp` → `Unit.FirstRoundAp`. `Expedition.Rations` keeps what was packed.
- **Schematics** (`data/schematics.food.json`, merged into the catalogue): Cold Cellar, Pickling, Medicinal Broth,
  Feast, Stim Tea, Field Rations (the Greenhouse's `sch_greenhouse` is in schematics.json). Gating is the catalogue's
  (`Schematics.RecipeUnlocked` / `LevelUnlocked`); every other food recipe and building is default.
- **Food as a price**: trader prices, rewards and story effects may name `food` (`Pantry.FoodKey`): food value from the
  pantry (`Schematics.Payment`, `Pantry.TakeFoodValue`), or into it as canned food (`Pantry.AddFoodValue`). Food items
  are valued by trade at food value × `resourceValues.food` (dishes with the crafted markup).
- **Save**: `SaveGame.FoodVersion` converts the old pool (potatoes 50%, beans 25%, corn 15%, canned 10% of its food
  value), Simple Meals → Boiled Potatoes, Hearty Stew → Stew, their Kitchen orders, and leaves farms on ripe potatoes.
- **UI**: the production panel is a recipe book at cooking buildings; `HarvestPanel` (crop picker), `PantryPanel` (top
  bar), buff badges on `SurvivorCard` and lines in the survivor overview, the ration picker in `ExpeditionPanel`;
  `--colony --food-demo --food-view=kitchen|pantry|farm|roster` sets up screenshots.

## Electronics and power

Everything is data in `data/electronics.json` (items, recipes, disassembly table, the three power buildings, `power`,
`charging`), merged at load like `gear_mods.json`: its items join `items.json` (`ScavengeData.Electronics`), its recipes and
buildings join the colony data (`GameData.Electronics`). Core: `Data/Electronics.cs`, `Colony/Power.cs`,
`Scavenge/ElectronicWarfare.cs`, `Scavenge/DroneRoles.cs`.

- **Component chain** (Electronics Lab, `RecipeDef.Group` "Components"): scrap → wiring → circuit boards → antennas and sensor
  modules; microchips and cyan **Network chips** (from Linked circuitry, `NetworkGrade`) are tier 3 and `needsPower`. The
  cybernetics power cell is the basic battery and the Workshop servo is reused. Devices and their Mk upgrades (groups
  "Devices", "Upgrades") consume components; tools roll quality as before, and quality now also scales a device's radius,
  a drone's battery, a hacking tool's chance and a radio's range (`ScavengeData` variants).
- **Disassembly**: one `disassemble_{item}` recipe per table entry (group "Disassembly"): the item in, its first yield as the
  recipe's item and the rest as `RecipeDef.Byproducts`, `ResearchChance` for one Research Data. Each component has a chance of
  one more: the bench's best worker's `disassembly_yield_bonus` (Circuit Tinkerer) + Electronics × `skillYieldPerPoint`. Loot
  adds broken gadgets, dead radios, smashed drones; the Linked drop Linked circuitry (electronics.json `drops`).
- **Schematics**: `schematics.electronics.json` is a catalogue extension the Schematics module merges (recipes and the
  solar array / battery bank gated, Kill-sourced ones dropped by the Linked through electronics.json `drops`). Not listed,
  so open from the start: wiring, circuit boards, antennas, the power cell, handheld radios, noise decoys, the fuel
  generator (the scanner stays behind `sch_scanner`; `sch_scanner_mk2` gates Mk II). A disassembly's research roll pays
  the module's Research Data. Iris's missions are listed per schematic (`storyMissions`); every one also drops, sits in
  terminals, trades or can be researched.
- **Power grid** (`Power.Run`, first in the tick): with no generator or bank built nothing happens (no brownouts: the early
  game is unchanged). Consumers (priority = data order) draw per level, only while their workplace has a crew on shift;
  `floodlight` = every building with a Floodlight after dusk, `charging` = the bench per device. Draws are cut by the best
  `power_efficiency` (Electrician) at home. Supply: solar (`solarByPhase` × `Power.SolarWeatherFactor`, the weather
  adapter), battery banks (rate-limited), fuel generators (the first of `fuel` in stock: vehicles' `fuel`, else wood, per
  power supplied). Consumers that do not fit are shed (brownout, logged); demand above `overloadRatio` × capacity for
  `overloadTicks` trips the breaker for `breakerTicks`. Effects (`PowerEffect`): Lab `CraftSpeed` (+25%, tier 3 recipes wait:
  `CraftStatus.NeedsPower`), Med Bay L3 `SurgeryBonus`, Floodlight `NightShiftBonus` (through `Schedule.Crew`), Charging.
  **Seam**: `Power.IsPowered(state, building)` / `ColonySim.IsPowered` for the Radio Mast, Cold Cellar and turrets (their
  consumer entries are ignored until those buildings exist). `SetPower(building, on)` switches anything on the grid
  (`NotOnGrid`). `PowerState` keeps what each part did last tick for the UI (`ColonySim.PowerNodes`).
- **Charges and charging**: `UseEffect.Charges` makes a device reusable; `Unit.ChargesUsed` pools spent charges per item id
  (`ElectronicWarfare.ChargesLeft`); results carry them (`ReturnedSurvivor.ChargesUsed`) and `Stow` puts drained devices on the
  charging bench (`ColonyState.Charging`), `workPerCharge` each at `poweredRate` while fed, else `unpoweredRate`, plus
  `Power.VehicleChargeRate` (the vehicles adapter for a powered parked vehicle). No other durability.
- **Field devices**: scanners and jammers Mk II–III (radius), sensor kits (charges), radios carried anywhere (two on a side:
  +accuracy within range in `Combat.ChanceToHit`, extra pings, a squad radio's overwatch fires at what the side sees),
  rangefinder (`ItemDef.RangedAccuracy`), EMP (`EmpDef`: Linked off the relay and `Unit.EmpUntilRound` with no AP, drones fall,
  cameras/turrets fried, Network-grade implants nearby lock — your own too), holo-decoy (`UnitKind.Decoy`: a fake survivor of
  the user's side that takes no turns, lures, draws the Linked's focus and fades after `DecoyUntilRound`), noise decoy (Husks
  follow it unless their prey is adjacent; the Linked only when they lose sight), Network deck (`HackToolDef.TraceChance`:
  a hack action may be traced → Linked hunt the hacker). All tile devices also work as drone payloads.
- **Drone roles** (`DroneItemDef.Role`, `DroneRoles`): medic (`DroneHeal`: a healing item from the operator's pack on a friend
  next to the drone, with medic perks), cargo (`DroneLoad` from an adjacent cache up to `Cargo`; `DroneFerry` next to an exit
  → `Party.Secured`, home whatever happens, in `ExtractionResult.Secured`), combat (`DroneFire` with its natural gun, the
  operator's Drone Piloting as Firearms; `RequiresHook` drone_range_bonus = Drone Operator I). Scout Mk II–III are batteries.
- **Net**: protocol 9 (the role commands); `SideFog` shows a rival's decoy as a survivor and hides others' charges.
- **Saves**: colony 18 (`Power`, `Charging`), scavenge 10 (decoys, EMP stuns, charges, secured cargo); keyed on presence, so
  older saves load with an empty grid and nothing charging.
- Tests: `ElectronicsTests.cs` (data, power, disassembly, field devices, drone roles, saves), `ElectronicsNetTests`.
## Gear mods

Weapon, armor and clothing mods and Mk II / III upgrades, one framework for all gear. Data: `gear_mods.json` (rules, slots,
gear types with their slots and slot counts per Mk, the moddable gear and its upgrade family, the marks, the mods and their
effects, upgrade kits, headgear, zone biomes, extra enemy tags / drops / terminal loot, recipes) and
`schematics.gearmods.json` (the gated recipes' schematics, in the Schematics module's catalogue format). Core:
`Items/GearMods.cs`, `Items/ItemCatalog.cs`, `Items/GearBench.cs`, `Items/GearModSchematics.cs`, `Scavenge/GearEffects.cs`,
`Colony/GearBenchHandlers.cs`. Balance: BALANCE.md "Gear mods".

- **A modded piece is an item id**: `base[@quality][+mk2|+mk3][+mod[@quality]]...`, mods in the type's slot order
  (`GearModsData.Canonical`), so the same build always has the same id and stacks. `ScavengeData.Items` is an `ItemCatalog`:
  defined items and quality variants as before, and any valid modded id resolved on demand (`GearModsData.Check` + `Build`;
  cached, pure); enumerating it lists only the defined items, so modded gear is never loot. Everything that stores item ids —
  the colony inventory, expeditions, units' equipment and packs, caches, fitted prosthetics, saves, the wire, trades between
  players — carries the mods with no extra state, and `Items.ContainsKey` is the validation (a save or a joining party with a
  build gear_mods.json does not allow is refused like an unknown item). `ItemDef` gains `Mk`, `Mods`, `UnmoddedId` /
  `IsModded`; `DefinedId` stays the base item (models, icons, recipe inputs). Recipe item inputs never take modded gear.
- **Types and slots**: firearm (sight, muzzle, stock, underbarrel: 1 / 2 / 3 at Mk I / II / III), melee (edge, grip, coil:
  1 / 2 / 3), integrated (a fitted gun arm: one of sight, muzzle, underbarrel), armor (plating, lining, outer, webbing:
  1 / 2 / 3), clothing (lining, outer, webbing: 1 / 2 / 3), helmet (plating, lining, visor: 1 / 2 / 3), mask (visor: 1).
  Families (sidearm, long gun, carbine, melee, clothing, armor, plating) name the kit per Mk.
- **Effects** (`ModEffect`): on attacks (ranged mods change ranged attacks, melee mods melee ones) accuracy (per action id
  too: a compensator on bursts), damage, reach, noise, reveal, AP, strength scaling, armor pierce, injury multiplier, a
  scope's close-range AP (`ActionDef.CloseRange` / `CloseApPenalty`, `Combat.ApCost`), a stock's share of the after-moving
  penalty, extra banes (`ActionDef.ExtraBanes`: EMP coil vs `network` and `device`, incendiary rounds vs `husk`, a shock
  coil's `stun`), EMP feedback; on the piece armor, melee armor, injury resist, stealth, carry, sight, detection, move noise,
  camo, weather protection, thermal mask, Network weave, a light. A mod's quality scales it: benefits × the tier's
  multiplier, drawbacks ÷ it (`ModEffect.AtQuality`). A Mk adds damage and accuracy to weapons, armor (base + bonus, × a
  multiplier) and coverage to worn gear, and a little weight, and stacks with quality.
- **Coverage and the Head slot**: `EquipSlot.Head` (`ItemKind.Headgear`: scrap helmet, riot helmet, gas mask, and the
  weather's dust mask, which the colony still counts as a mask with the gas mask, `weather.json maskItems`). Gear with
  `coverage` counts its armor only on hits to those regions (`UnitProfile.RegionArmor`, `ArmorAgainst(region, melee)`);
  gear without it protects every part as before. `Combat.Strike` takes armor for the part hit (after the hit location
  roll, so the RNG stream is unchanged), less the attack's pierce; injury severity × the attack's injury multiplier ×
  (1 − the wearer's injury resist).
- **In the field** (`GearEffects`): a ranged attack after moving this turn (`Unit.Moved`) loses
  `rules.movedAccuracyPenalty` (the Linked and machines do not), less a stock's share; a **weapon light** in the dark (human
  sight below `darkBelowSight`: night, or blinding weather) lets its carrier see in a cone toward `Unit.Facing` (set by
  moves and attacks) as by day (`Detection.Spots`, `Visibility.SideVisible`), costs it stealth, and as a beacon shows it to
  anyone with line of sight in their pre-dark sight; **camo** adds stealth in zones of its biome (`zoneBiomes`); **thermal
  masking** adds stealth in the dark against the senses that see through it and hides from thermal eyes (not scanners);
  **Network weave** adds stealth against the Linked's senses, takes its share off Network interference and EMP feedback,
  and Husks hear its wearer further (`HuskLure`); **plates** clank (`MoveNoise`: idle enemies in earshot of a move come
  looking, and parties hear it further; quiet fabric takes it off); **weather protection** keeps a weather's effects off the
  wearer's senses (`FieldEnvironment.EffectsFor`: waterproofing rain and thunderstorms, a dust filter, the dust mask and
  the gas mask dust storms, the gas mask fog too); an **EMP shot** may lock up Network-grade
  parts of the shooter's side within 1 tile (`GearEffects.Feedback`, one roll per part).
- **Gear Bench** (`GearBench`: pure; `InstallMod`, `RemoveMod`, `UpgradeGear` colony commands): mods go on and come off
  gear in the inventory (or a survivor's fitted gun arm: `SurvivorId` + `Part`) at an Armory, Workshop or Tailor; fitting
  into a taken slot swaps the old mod out; removing returns it intact; kits are fitted at the Armory or Tailor, one Mk at a
  time. Errors `NoBench`, `NotModdable`, `ModDoesNotFit`, `NoFreeModSlot`, `EmptyModSlot`, `WrongKit`, `MaxMk`; event
  `GearModded`. In the field `FieldMod` (Jury-rig, `field_repair`; `rules.fieldApCost` AP) fits a mod from the pack to an
  equipped piece or takes one off into the pack (scavenge event `GearModded`).
- **Crafting**: mod and kit recipes at the Workshop (simple mods, scrap helmet), Tailor (clothing mods, gas mask, clothing
  kits), Armory (firearm mods, plates, visor, riot helmet, weapon and armor kits) and Electronics Lab (light, laser, shock
  coil, thermal masking, and the Network-grade EMP coil and weave from Linked cores). Crafted mods roll a quality from the
  crafter's skill like gear. Mods, kits and schematics are loot; the EMP coil, weave and carbine/plating kit schematics also
  drop from the Linked and come from terminals.
- **Schematics** (`GearModSchematics`): the gear schematics are `data/schematics.gearmods.json`, merged into the catalogue
  by the Schematics module (see "Schematics"); each unlocks the mod and kit recipes it names, so `CheckRecipe` gates them
  like any other. The default set (iron sights, wrapped grip, sharpened edge, padding, webbing, scrap plates, visor, dust
  filter, helmets, gas mask, Mk II for sidearms, melee, clothing and armor) has no entry. `GearModSchematics.Has` /
  `RecipeUnlocked` are `Schematics.Has` / `Schematics.RecipeUnlocked`. Caches, lockers and terminals roll gear schematics
  from the catalogue by their sources; the EMP coil, weave, shock coil and carbine / plating kit schematics also drop from
  the Linked (`gear_mods.json drops`, kept in step with the `Kill` sources).
- **Multiplayer**: the server's party check (`Items.TryGetValue` + slot) validates every build; `fieldMod` is an intent;
  views carry equipment as ids (a visible unit's mods are as visible as its weapon). Protocol 11.
- **Saves**: colony 20 (`SaveGame.GearModsVersion`), scavenge 11 (units' `Moved` and `Facing`); older saves load unchanged. Tests: `GearModsTests`,
  `GearModsNetTests`.

## Raids and defenses

Core is `src/Remnants.Core/Raids/`, data `data/raids.json` and `data/schematics.defense.json`; balance in BALANCE.md "Raids".
`ColonySim.Raids` (`RaidData`, settable like `Story`) is optional: without it nothing below happens (tests of other rules keep
their RNG draws), so the game, the playtest bot and raid tests set it after `NewGame`/`SaveGame.Load`. State is
`ColonyState.Defense` (`DefenseState`). `RaidSystem.Run` is the last step of `ColonySim.Tick` (after `WorldMap`).

- **Grid** (`DefenseGrid`): the plot grid at `grid.cellsPerPlot` (4) cells per plot inside a `grid.margin` (4) of cells; a
  cell is one 2 m scavenge tile, so the grid maps 1:1 onto the battle map (see "Fought raids" below). Cell (0, 0) is the
  north-west corner; South (+y, +Z in the town) is the road and the main gate. `SideOf`, `DistanceToEdge`, `Coverage` and
  `Covers` (range, turret arcs around the facing) serve the rules and the overlays.
- **Defenses** (`DefenseDef`, kinds Wall / Gate / Barricade / Post / Tower / Light / Turret / Alarm / Trap): cost, upkeep (paid
  at midnight; unpaid ones stand idle that day), HP, soak (walls), flat defense, warning hours, guard slots and post bonus,
  range/arc, lights, wakes (alarm), trap damage vs tags, re-armable with a re-arm cost or used up, lure, `upgradesTo` (wood →
  scrap → concrete walls; tower levels 1–3), `poweredBonus`. Commands (`IRaidCommand`, handled by `DefenseHandlers`):
  `PlaceDefense`, `RemoveDefense` (half back, scaled by HP), `RotateDefense`, `UpgradeDefense` (the cost difference),
  `RearmTrap`, `AssignGuard`, `SetNightWatch`, `QueueRepair` / `CancelRepair`, and the prep commands below. UI queries:
  `DefenseHandlers.CanPlace` (the ghost's ok / reason), `Cost` (Engineer discount), `UpgradeCost`, `FreeGuardSlots`, `CanRecall`.
- **Schematics** (`DefenseSchematics`): `schematics.defense.json` is part of the schematics catalogue (unlock kind
  `UnlockKind.Defense`, ids checked by `RaidData.Validate`). A defense no schematic names is default content (Wooden Wall,
  Gate, Spike Strip, Alarm Bell, Lookout Tower); the rest are found as loot, traded, researched, or carried by beaten raids
  (`Droppable`: unknown defense schematics with the Loot source, granted with `SchematicSource.Kill`). `Unlocked` = no gate or
  `Schematics.Has`.
- **Guards** (`Guards`): a guard has no building job and stands at a post on the day shift, or the night watch (the hours
  outside `dayNight.workHours`). `Schedule.OnShift` asks `Guards.OnShift`, so the night watch is night work to the day/night
  rules (sleep debt, mood); off duty in `guard.daySleepHours` they sleep it off. On duty they train like workers at the
  `guard` job (Firearms; Perception, Endurance; Melee on the side) with work XP. `AssignJob` ends guard duty.
- **Threats** (`ThreatModel`): pressure per hour per kind = `(basePerHour + perInputPerHour · input) · weather`; Husks hear
  noise (survivors, night shifts, Floodlights at night, returns, noisy items such as vehicles, minus lures), the Linked follow
  Network presence (the threat of *scouted* Linked zones, growing with the days; a rumor draws nothing), raiders smell wealth. `RaidSystem` draws a raid when a
  kind reaches `scheduler.threshold` — after the grace (days and buildings), never within `minGapDays` of the last — with a
  size from pressure and escalation (raids survived, days), an approach side and an arrival phase by weight (Husks favour
  night). It sets out `travelHours` ahead.
- **Warning** (`RaidWarning`): lead hours = base + the best tower + other watching defenses (bell, tripwires) + watching
  buildings (`warning.buildingHoursPerLevel`: Watchtower, Radio Mast once that data lands) + guards on duty (+ Sentry) + a drone
  patrol (a scout drone and a pilot at home), × weather, capped. The raid is seen once its arrival is within the lead; the size
  estimate's error shrinks with the lead. Logged as `RaidWarned`.
- **Prep** (only while a raid is warned): `SetDefenders`, `ShelterChildren`, `CloseGates`, `RecallExpedition` (a party still
  walking out that can make it home), `ChooseFight` (fight it on the map; `RaidBattle.CannotFight` says why not).
- **Fought raids** (`RaidBattle`): a fought raid reaches `RaidStage.Battle` on arrival (`RaidAtTheFence`) and waits up to
  `battle.maxWaitHours` for the player to open it, then settles itself by auto-resolve. `RaidBattle.Open` builds (or resumes
  `Raid.Battle`, carried in the colony save) a scavenge run on `ColonyMap`: the grid as a `HandmadeMapDef`, with built plots
  as 3×3 wall blocks whose door tile holds the building as a structure unit (HP = `consequences.buildingHp` minus damage;
  Town Hall tagged `hall`, Radio Mast `mast`, others `b{id}`), wrecked ones as rubble; walls and shut gates as wall, barricades
  and nests as cover, destroyed defenses as rubble, towers (and Watchtower buildings) as raised posts, turrets as party-owned
  turret devices (`MapDeviceDef.Party`); the approach band as the spawn marker; exits on the far side; the party by the Town
  Hall. The script (`Script`): Survive `battle.rounds` + Defend `hall` (fails the mission), attackers in `battle.waves` from
  the approach toward the hall, tagged `raider`, minus those the traps take out first (`TrapKills`). Defenders take their
  auto-resolve gear; guards start on their posts; lit floodlights become lights at dusk and night; the hour is the arrival's.
  `ApplyRaidBattle(result)` brings everyone home as they are (injuries, deaths, field skill), puts each structure's lost HP on
  its building (a destroyed one is wrecked), and calls `Consequences.Apply` with a `BattleOutcome` (the battle's dead, hurt
  and kills replace the dice) and a pressure from how it went (held: ½ · attackers left standing; lost or abandoned: ½ + ½ ·
  that), so traps, walls, spoils, mood, loot and schematics follow as in auto-resolve; the report has `Fought`.
- **Story**: `Campaign.Setup` fights a perimeter mission (M5, M14) on `RaidBattle.StoryMap` — the colony map with the
  approach from the north named after the script's spawn marker (`treeline`) — when it has every marker the script names and
  every structure a Defend objective needs (M5: a built Radio Mast); otherwise on the handmade `perimeter_thN`.
- **Auto-resolve** (`AutoResolve.Assess` → `RaidAssessment`, `Consequences.Apply`): the formula is in the class remarks and
  BALANCE.md; gear is handed out from the inventory best-first per slot (weapon, body, head); modded gear resolves through the
  item catalogue with its mods applied, so gear mods count as in the field. Consequences, from the pressure p: traps spring (re-armable ones wait, used-up ones go), walls on the approach take
  damage first, then (if they got in) the nearest buildings (`DefenseState.BuildingDamage`; a building at 0 HP is **wrecked**:
  workers leave, `AssignJob` fails with `Wrecked`), spoils (raiders steal the listed resources; Husks trample food through the
  `CropDamage` adapter, which will call the Pantry once food-cooking lands), injuries (p² per defender) and deaths, the Linked
  carry people off, mood for everyone, XP for the defenders, loot and a schematic from a beaten raid. `RaidReport` goes to
  `DefenseState.LastReport` (history kept) and the log (`RaidRepelled` / `RaidBreached`). `RaidSystem.Finish` is shared with
  the fought battle.
- **Repairs**: `QueueRepair` books a building or defense; materials are paid when the repair starts; it progresses on the day
  shift at `repair.baseHpPerTick` plus idle adults by Crafting (+ the Engineer's `repair_speed_bonus`).
- **Other systems** (`Raids/Adapters.cs`): `RaidWeather` reads the colony's weather spell (`Weather.SpellAt`; rain, fog,
  thunderstorms and dust storms cut the warning and shift pressure and strength per raids.json `weather`); `DefensePower`
  counts a defense as powered when a building the grid fed last tick (`Power.IsPowered`) stands within 6 cells, and then it
  gets its `poweredBonus` (turrets' defense, floodlights' range), never a requirement; `CropDamage` spoils food through the
  food system: `crops` tramples the standing crop of outdoor harvests (`Building.HarvestProgress`), `food` takes pantry food
  value (`Pantry.TakeFoodValue`). Raid loot goes through `RaidLoot.Roll` (the seam for enemy loot tables).
- **Presentation** (`game/scripts/Colony`: `DefenseView`, `DefenseModels`, `RaidText`; `Colony/Ui`: `DefenseCatalog`,
  `DefensePanel`, `RepairPanel`, `RaidBanner`, `RaidPrepDialog`, `RaidReportDialog`): Defend mode (TopBar or **F**; D stays
  camera pan) with the catalogue by category, cost chips and lock reasons, a grid-snapped ghost tinted by `CanPlace`, drag to
  lay a line of walls or traps, R to rotate, the free-cell grid and coverage / fire-arc overlays; a placed defense's panel
  (HP, repair / re-arm / upgrade / rotate / remove with costs, guards and the night watch); defenses on the town by
  `defense_{id}` with fallbacks by kind, sprung / unpaid / destroyed looks, damaged and wrecked buildings; the warning banner
  and the prep screen (time pauses; defenders, shelter, gates, recall, lead sources, strength bar and breakdown); the raid
  report; the repair queue (TopBar Repairs). `--colony --raid-demo=placement|warning|prep|report` stages each for
  `--screenshot=PATH --quit`.
- **Saves**: colony version 21 (`SaveGame.RaidsVersion`) adds `Defense`; older saves load with none, and the gap since "the
  last raid" counts from the save's tick. Guards of missing survivors and repairs of missing targets are dropped on load, and
  defense schematics a pre-merge raids save kept in `Defense.KnownSchematics` move into `ColonyState.Schematics`.

## Field loot and salvage

Core: `Scavenge/LootTables.cs` (tables, rolls), `Scavenge/FieldLoot.cs` (kills, piles, take all, drop, stash, hooks),
`Scavenge/FieldSalvage.cs` (nodes, the Salvage action). Data: `data/loot_tables.json` (+ `loot_tables.<feature>.json`),
`data/salvage.json`. Numbers: BALANCE.md "Field loot and salvage".

- **Loot tables** (`LootCatalog`): per table a `nothing` chance (guaranteed entries still drop), `rolls` weighted picks
  from `entries` (`qty`, `minTier`/`maxTier`, `quality` weights → a gear variant), `rare` entries rolled on their own,
  `guaranteed` ones always. `rules` scale by the find tier (the zone's threat 1..4: more picks, more of each, less empty,
  likelier rares) and add picks at night, in some weather and per point of the finder's `loot_bonus` hook; `difficulty`
  multiplies quantities per Story difficulty. `LootCatalog.Expected` gives the means (tooltips, bot, tests).
- **Kills** (`ScavengeSim.Kill` → `FieldLoot.Drops`): an enemy, or a camera/turret unit, rolls its `enemies.json` `loot`
  table, then its `drops` schematics exactly as SCHEMATICS.md says (on the sim RNG, first hit only). Table rolls use their
  own RNG per kill and per salvage lot (`FieldLoot.Rng`: `ScavengeState.LootSeed` + the unit or node), so loot never
  shifts combat rolls. The killer (`Combat.ApplyDamage` passes the attacker) brings its perks. The loot lies in a pile
  (`LootCache.Kind` Remains, `Source` = the unit's name), logged as `LootDropped` with the tile and no actor, so only sides
  that see the tile hear "Husk 3 dropped: 2 Scrap, Bandage".
- **Piles**: `LootCache` with `Kind` (Cache, Remains, Dropped, Stash), `Source`, `PlayerId` (a stash's owner). `PickUp`
  takes one item, `TakeAll` everything that fits (lightest first) for one pick-up's AP, `DropItem` puts a carried item on
  the tile (`salvage.json` `dropApCost`), `StashItem` loads it into the player's parked vehicle when it is next to the unit
  (`ParkedVehicles.Stash`: pick-up AP, within cargo) or, on an exit tile, puts it in their stash pile (`stashApCost`). What is left in a stash goes into
  `ExtractionResult.Stash` → `ZoneStatus.Stash` and is back on the zone's first exit next run (plain runs only).
- **Salvage nodes** (`SalvageNode`: kind, tile, lots left, progress; shared by everyone): placed per zone from
  `salvage.json` `zones` (or `default`) on their own seed after the devices (`FieldSalvage.Place`: Rubble on rubble,
  wrecked cars on cover, the rest on floor by a wall), or at handmade-map markers (`HandmadeMapDef.Salvage`). `Salvage`
  (adjacent, `apCost` AP) adds 1 + the best carried tool's bonus to the node's progress, makes noise (idle enemies in
  earshot hunt, other parties get a Noise contact unless they see the worker), practises the node's `skill` and gives
  field XP; at `work` points a lot comes off: the node's table (quantity + `yieldPerSkill` × skill) plus
  `FieldSalvage.Extras[kind]` rolls (vehicles: wreck parts), into the pack while it fits, the rest onto a pile there.
- **Raids**: `FieldLoot.RaidSpoils(colony, enemyIds, tier)` rolls the killed raiders' tables on the colony RNG and
  deposits them (`ExpeditionHandlers.Deposit`: salvage → stockpile, schematics received, the rest into the inventory).
- **Data rules**: an entry naming an item the data does not have, or an extension of a missing table, is skipped and
  listed in `ScavengeData.LootWarnings` (the game and the playtest print them); bad numbers are `GameDataException`s.
  **A feature adds loot** in its own `data/loot_tables.<feature>.json`: `"tables": [...]` for new tables (a new enemy
  names one with `loot`), `"extend": [{ "table": "husk", "entries": [...], "rare": [...] }]` to add to existing ones. It
  can ship before its items merge.
- Net: one `stashItem` and one `salvage` command serve both the vehicle and the exit stash, and nodes and wrecks.
  `SideFog` keeps piles (with kind and source) and salvage nodes per side as last seen; node progress only while in
  sight; the loot seed is never sent. Commands `takeAll`, `dropItem`, `stashItem`, `salvage`.
- Game: piles draw as a crate (a small one, or amber for a stash, for a `loot_bag` model to replace) with a bobbing amber
  gem (`MapView.Pile`); `SalvageViews` draws floor nodes' props with a ring and a progress arc; `LootTip` (hover: pile
  contents, node yield / actions / noise / lots), `LootPanel` (carry bar against the encumbrance steps, item chips, Take
  all (F), Drop, Stash), the Salvage button (I) in `ActionPanel`. `--loot-demo=pile|salvage` (with `--scavenge`) stages
  both for stills.
- **Saves**: keyed versions `ScavengeSave.LootVersion` (piles' kind, source and owner, nodes, loot seed, difficulty; an
  older run gets a loot seed from its RNG state) and `SaveGame.LootVersion` (zone stashes); `Protocol.LootVersion`.
- Tests: `LootTests.cs` (tables, warnings, empty share, tiers, quality, piles, determinism, take all, drop and stash,
  vehicle adapter, stash between runs, raid spoils, salvage work / tools / noise / overflow / extras, placement, saves);
  Net: `LootNetTests` (no pile or line for a rival who never saw the tile; salvage round trip).

## Construction

Construct and Upgrade place a **construction site** instead of finishing at once (`rules.json` `construction`; off in code
by default, so hand-built test rules and `TestData.Real` keep instant building). Core is `Colony/Construction.cs`
(`ConstructionState`, `ConstructionSite`, `ConstructionSystem`) and `Colony/ConstructionHandlers.cs`; balance in BALANCE.md
"Construction".

- **Site** (`ColonyState.Construction.Sites`): the building's id (a new building's future id, handed out at placement, so
  `Construct` still returns the id the building will have; an upgrade's is the standing building), type, plot, target
  level, `Work`/`WorkNeeded` (`buildWork` of the level), `Materials` (the level's cost, taken from the stockpile when the
  site is placed: Construct and Upgrade still check affordability up front) and `Delivered`, `Builders`, `Helpers`,
  `Paused`, `Priority` (Low / Normal / High), `NightShift`. A site holds its plot (`IsPlotFree`), counts for `unique`, and
  blocks a second upgrade (`UnderConstruction`).
- **Work** (each tick after production): every site not paused gets its crew — assigned builders at full rate, then idle
  survivors (no job, post, site, bed, class or research) as **helpers** at `helperShare`, filling the open places of the
  Normal/High sites, High first. A builder on shift hauls `haulPerHour` (× health × child rate) while the delivered share
  is less than the work share + `haulLead` (or work has caught up with it), else builds `workPerHour` × the `construction`
  job's output formula (`JobOutput`: Strength/Endurance/Intellect/Creativity + the new **Construction** skill, tools, body,
  health, mood, conditions, perks — `engineer` +15%, `pack_mule` +10% — and food). Work never passes `WorkNeeded ×
  delivered share`. Training (`Training.Work`) trains Construction. Children may build (`childrenAllowed`) at the child rate.
- **Schedule and weather**: the day shift as for any job (`Schedule.OnShift` counts builders and helpers as working, so
  sleep and night work apply); a site's Night Shift only where it is lit — an upgrade of a lit building (indoors, level ≥
  `litMinLevel` or a Floodlight); a new building's site is dark (`NeedsLight`). Weather: a kind's `colony.buildOutput`
  (rain 0.8, thunderstorm 0.4, **dust storm 0: building halts**), else its outdoor rate; dust masks left after the
  outdoor crew cover builders where masks matter; children stay in when the kind says so.
- **Done**: when the work is done and everything is delivered, a new building appears (`Constructed`, crops planted) or
  the standing one reaches its level (`Upgraded`); builders become idle. **An upgrade keeps the building working at its
  old level** (its workers, output, beds, Town Hall cap) until then — a site's builders are not its workers.
- **Commands**: `AssignBuilder(survivor, building)` (keeps their jobs and class, leaves a guard post; `CanBuild`: home, not a
  patient, baby or researcher; `NoFreeSlot` past `maxBuilders`, default `defaultMaxBuilders`; `TooManyActivities` at the
  cap, see Multiple activities); `Unassign(survivor, building)` takes a builder off that site, `Unassign(survivor)` off
  any site when they hold no job; guard duty, an expedition, a Med Bay bed or research release them. `CancelConstruction` gives
  back what was not hauled in full and the hauled materials at `refundShare` (80%), rounded down. `PauseConstruction`,
  `SetConstructionPriority`, `SetSiteNightShift`. Queries: `ColonySim.Site(id)`, `SiteForecast(site)` (crew, rates per
  hour averaged over the day, ETA, why it stands still), `BuildHours(def, level)` (clock hours with two typical
  builders: the build menu), `MaxBuilders`.
- **Raids**: a new building's site has its progress share of level-1 HP (at least a tenth). Auto-resolve picks sites near
  the approach among its targets: a hit knocks work back by the HP taken (materials stay); an upgrade under way loses work
  with its building's damage. On the battle map (`RaidBattle.ColonyMap`) a site from 15% stands as a frame (`=`, low
  cover) and from 60% as walls (`#`), with a structure (`s{id}`) at its HP; what it loses comes off its work.
- **Bonds**: builders at one site share a workplace for affinity (as co-workers do).
- **Repairs**: raid repairs are worked by idle adults; builders and helpers on a site are not idle, so a site at Normal or
  High priority with open places draws idle hands away from the repair queue (set it to Low to keep them repairing).
- **Setup without sites**: `ColonySim.InstantConstruction` (not saved) or `sim.Instantly(() => …)` finishes Construct and
  Upgrade at once — story gifts (`EffectKind.Building`), debug scenes, the menu backdrop and the audio self-test use it.
- **Save** version 26 adds `ColonyState.Construction`. Older saves had nothing pending (building was instant): every
  building stays finished, no sites, Construction skill 0. Loading a save with sites on data with construction off
  finishes them; a site whose type or level no longer exists is dropped and refunded.
- **Presentation** (no rules): `ConstructionSites` (made by `TownView`) draws each site — the level-1 model rising with
  progress through `BuildReveal` (a clip shader made in code from each surface's own material, a fresh-timber band at the
  cut), a procedural scaffold (one multimesh of boxes, bottom-up so a prefix is a lower scaffold), delivered materials as
  piles (logs in code, `rubble` for stone, `loot_crate` for the rest; they shrink as the work uses them), dust while the
  crew works and a progress ring (rust when nobody works it). The footprint outline shows on a new site's plot. Upgrades
  get a full scaffold round the standing model; when the level comes, `TownView` hands the old model to
  `ConstructionSites.Finish` (it sinks away, the new one settles in, a dust puff). `SurvivorWalkers` send builders and
  helpers to the site plot (hauling: between the Town Hall and the site with `carry_walk`; then `work_hammer`, sometimes
  `work_craft`); `ColonyAudio` plays `ambience.work.construction_site` (the workshop loop) by crew size. UI:
  `ConstructionSitePanel` (the building panel's **Construction** tab, its first hosted tab, so a site opens on it; a new
  building's site shows only the pinned header and this tab; materials hauled/needed as chips kept by id, work bar, builder
  `WorkerSlots`, helpers, ETA, Pause, Priority, Night shift, Cancel; updated in place; a survivor card dropped on it makes a
  builder), help topics `construction`, `builders`, `hauling`, `upgrade_time` (data/help/colony.json) with the live
  `construction_site` breakdown (`HelpContext.Site`), `SiteTooltip` (hover card), the build menu's time
  chip and the upgrade box's build time. Raid battles draw sites with `ColonyBattleDressing` (reveal + scaffold).
- **Debug**: `--colony --construction-demo=0|50|100|upgrade|assign|levels [--site-tip] [--level-types=a,b,c,d]`.

## Data files

- `resources.json` — resource ids and names.
- `schematics.json` — the schematics catalogue, traders and the rules of drops, decrypting, research, the radio,
  copies and trade values; `schematics.<feature>.json` add entries (see "Schematics").
- `jobs.json` — per job: skill + weight, attribute weights, body-region weights, tool bonus, `tools` (stockpile items the
  job's workers use, item → quality; `WorkTools` hands one to each worker, the best to the lowest id), `childrenAllowed`.
- `buildings.json` — category, job, produced resource, base output (per worker-hour on shift), `outdoor` (a Night Shift
  needs light), `minTownHallLevel`, and per-level
  cost / worker slots / output multiplier / beds / patients / `buildWork` / `maxBuilders`. Level 1 cost = construction cost
  (see "Construction").
- `recipes.json` — what production buildings make (see "Crafting tiers"); food recipes take `ingredients`.
- `food.json` — food items (food value, nutrition, shelf, tags, meal effects and buffs), harvests, buffs and the food
  rules (see "Food and cooking"); `schematics.food.json` — the food schematics.
- `vehicles.json` — vehicle tiers, slots, upgrades, wear and road rules, weather effects (see "Vehicles");
  `schematics.vehicles.json` — the vehicle schematics; `vehicle` recipes build vehicles at the Garage.
- `raids.json` — the defense grid, raid scheduler, threats (enemies, pressure, size, strength, approach and arrival weights,
  trap tags, spoils, loot, schematic chance), noise, warning, day-phase and weather modifiers, strength, consequences, guard,
  repair, and every defense and trap (see "Raids and defenses"). `schematics.defense.json` — the schematics that unlock them.
- `perks.json` — skills (names, learning attributes), XP sources and level curve, study buildings and rates,
  disciplines and the perk tree (see "Perks and study").
- `electronics.json` / `schematics.electronics.json` — components, devices, drone variants, disassembly, power buildings,
  grid and charging rules; electronics schematics (see "Electronics and power").
- `gear_mods.json`, `schematics.gearmods.json` — gear mods, Mk kits, headgear and their schematics (see "Gear mods").
- `cybernetics.json` — tiers, specialisations, the tree's nodes (one per prosthetic item), paired sets, surgery, humanity,
  upkeep and interference (see "Cybernetics" and CYBERNETICS.md).
- `medicine.json` — conditions, infection, fractures, pain and painkillers, sickness kinds, Med Bay auto-treatment,
  surgery kits, blood donors, field rules and the save migration (see "Medicine").
- `schematics.medicine.json` — the medical schematics (the gated supplies; the basics are in the default set).
- `trade.json` — player trading: offer caps and suspicious amounts, courier timing and building factors, market limits
  (listing hours, open listings, rate limits), live-trade idle timeout. Reference values come from schematics.json.
- `rules.json` — tick/time scale, output formula constants, training rates, food (how much is eaten), housing grace,
  crafting (queue limits, quality roll), expedition (party size per Town Hall level, rations, recruit ages), medical (heal/worsen rates, medicine use),
  life (growth stages and rates), relationships (affinity), family (conception, pregnancy, baby names), mood,
  new-game setup (counts, age/stat ranges, stock, starting items, starting buildings, names, start hour), `dayNight`
  (phase hours, work and sleep windows, sleep / campfire / supper mood, Night Shift, sleep debt, light, Floodlight cost).
- `items.json` — weapons (actions, with `noise`, `reveal`, `bane` and `qualityAccuracy`), clothing/armor (with `stealth`), tools (movement modifiers, carry, sight, `detection`),
  flares and motion sensors (`use.lightRadius`/`lightRounds`, `use.sensorRadius`: used on a tile),
  consumables (med-kit, bandage and the other medical supplies with `medical: true` and their `use` effects, evac beacon), materials (with `salvage` into colony resources; cybernetic components),
  prosthetics (region — `Head` for eye implants — quality, Med Bay level; the field stats, attacks, move modes or hacking
  tool of a fitted part), devices (scanner, signal jammer);
  tier + loot weight for caches; `natural` for enemy body parts (never looted or crafted). Every other item has a
  recipe or a loot weight (tested).
- `enemies.json` — hp, AI kind, attributes, skills, equipment (a Husk's claws are an item too), `tags` (for weapon
  banes), `loot` (its loot table), `drops` (schematics and fixed items; Linked and Wardens always leave Linked cores) and,
  for authored enemies, `cards` (see "Telegraphed plans and cards").
- `cards.json` — tactical cards and the squad deck's rules (see "Telegraphed plans and cards").
- `loot_tables.json` — loot tables and their scaling rules; `loot_tables.<feature>.json` add tables or extend them (see
  "Field loot and salvage").
- `salvage.json` — salvage node kinds (table, prop, terrain, work, lots, AP, noise, skill, tools), zone densities, drop and
  stash costs.
- `zones.json` — size, threat, loot tier, terrain densities, spawn/exit edges, enemy count + mix,
  caches, NPC count, hackable devices per kind, `haul` (materials a plain run brings home: resource → [min, max], × (1 −
  depletion), in `ExpeditionReport.Haul`).
- `weather.json` — weather kinds (hours, intensity, forecast confusion, drones, colony and travel effects), the Markov
  `next` weights, per-region multipliers, the forecast, field changes (`roundsPerHour`, `lookaheadHours`), repair, mask
  items and crop jobs (see "Weather"). Optional: without it the weather is always clear.
- `scavenge.json` — `environment` (per day phase and per weather: sight per sense kind, stealth bonus and who ignores it,
  Husk count and hearing, flare radius; see "Day and night"), `hacking`, `devices`, `drone`, `world.droneScout` (see "Hacking and scout drones"), `coop` (alliance offer / break-warning rounds, pings, give and carry costs, carry movement penalty,
  enemies per extra player), `fog` (stealth, detection, contacts, ambush, overwatch, brace, lookout, Linked fog AI), `network` (Linked squad tuning), `world.colonyPos`, terrain table, movement derivation (base pattern/range, Agility per range,
  encumbrance steps, body rules such as "missing leg → King 1"), AP, sight, carry, combat numbers (with hit `tiers` and
  `armLockBelow`), `plans`,
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
dotnet run --project tools/Remnants.Playtest -- --data=data --mission=m2 --runs=200 --hour=2,12   # one story mission, fresh first party (MissionTrial)
dotnet run --project tools/Remnants.Playtest -- --data=data --zone=suburb_ruins --runs=300 --coop=2   # co-op trial
```

Data lookup (`GameDataPaths`): `data/` next to the executable in exported builds (lets players override
balancing), the repo's `data/` (`res://../data`) in dev, else the copy embedded in `Remnants.Game.dll`
at build time (`EmbeddedData.props`: every `data/**/*.json` as `Remnants.Data.<path>`) — so exports work with nothing
copied. The choice is one `DataSource` (Core), and every loader (`GameDataLoader.Load`, `ScavengeDataLoader.Load`,
`StoryDataLoader.Load`, `RaidDataLoader.Load`, `TradeRules.Load`) reads its files through it, including catalogues
split over files (`DataSource.Catalogue("loot_tables.json")` = the main file then `loot_tables.*.json` by name), so the
folder and the embedded copy can't load different files. `EmbeddedDataTests` imports the same props and checks parity.

Export: `godot-mono --headless --path game --export-release Linux ../build/linux/remnants.x86_64` (preset in
`game/export_presets.cfg`; `build/` is ignored). Smoke test: `build/linux/remnants.x86_64 --headless --quit-after 1200 --
--new-game=story` (or `sandbox`) clicks New Game for you and should log nothing but "Colony: data from the data embedded…".

Playthrough smoke test: `DISPLAY=:0 tools/smoke-playthrough.sh` launches the real game once per step (New Game → Story,
the radio call, the journal and M1 briefing, the Garage and an expedition with a vehicle, the world map, a field run with a
vehicle, a fight's loot pile, salvage, the return, the gear bench, Armory, kitchen, dispensary, Electronics Lab, a raid
warning, prep, a defense battle and its report) via the debug flags, with a screenshot and a log each in `build/smoke`,
and prints each step's error count. `--disable-vsync` keeps a window on a hidden workspace from stalling.

Debug flags (after `--`): `--colony` skips the menu, `--scavenge` starts the debug mission (`--autoplay=N` plays N
orders with a simple bot so enemy turns replay, `--zoom=SIZE` sets the ortho camera size), `--demo` builds a farm and woodcutter and assigns
the adults, `--loop-demo` sends the adults on an expedition to the outskirts (scavenge screen, or resolved in place with
`--autopilot`) and builds a Med Bay and Workshop, `--family-demo` does `--demo`, encourages the first man and woman
and fast-forwards until a baby is born (then selects the mother), `--crafting-demo` builds a grown town (Town Hall 3, every
production building, some at level 2–3) with staffed, queued Armory and Tailor and opens the Armory and item panels, `--expedition` opens the expedition planner with two adults picked, `--world-map` opens the world map
(`--zone=ID` selects a marker), `--garage-demo` (a Garage L3 with vehicles, upgrades and parts, selected; with `--expedition
--zone=ID --vehicle=TIER` the planner picks that vehicle), `--world-demo --vehicle=TIER` (the party drives),
`--scavenge --vehicle=TIER` (a parked vehicle in the debug mission), `--tech-demo=hack|turret|drone|medic|cargo|combat|emp` (with `--scavenge`) stages a hack in progress, a hacked turret firing, a
drone scouting, a medic drone treating a teammate, a cargo drone loaded beside an exit, a combat drone firing or an EMP bursting over a Linked and its
drone (saves its own still with `--screenshot`), `--power-demo` (colony) builds a grown town with a Fuel Generator, Solar Array, Battery Bank and a
staffed, queued level 3 Electronics Lab with two devices charging, and opens its bench (`--bench-tab=Devices` etc. picks the tab), `--power-overlay`
opens the power grid overlay, `--fog-demo` (with `--scavenge`) stages a fog ambush (`--select-unit=watcher` shows the unit on overwatch with its range;
`--select-unit=ID` and `--pause-menu` work in any scavenge run), `--world-demo` fakes a few weeks of scouting and sends a party on the road (for
screenshots); `--scavenge --zone=ID --seed=N` plays the debug mission in another zone, `--multiplayer` opens the multiplayer menu and
`--mp-host[=PORT]` / `--mp-join=ADDR` with `--mp-name`, `--mp-ready`, `--mp-coop`, `--mp-team=N`, … script it (see `MULTIPLAYER.md`), `--screenshot=PATH [--frames=N] [--quit]` saves the viewport as PNG (any scene). Day and night: `--night-demo` (the demo town two days on, a Floodlight and the Night Shift on the Woodcutter,
selected), `--hour=H` runs to the next H:00, pauses and puts sleepers indoors, `--no-select` clears the selection for
scenery shots. Schematics: `--schematics-demo` (the crafting town plus a Radio Mast, a Lab L2, known / heard-of / encrypted
schematics, a research running and the trader parked) with `--archive [--archive-filter=known|todecrypt|heard|unknown]`,
`--research` (selects the Town Hall), `--radio=calls|trader|contracts` (selects the Radio Mast) and `--collapse-log`. HUD shots: `--pause-menu`, `--settings`, `--build-menu`, `--select-survivor=ID`, `--survivor-tab=N` (colony), `--menu-settings` (main menu). `--gear-demo` (colony): a town with an Armory, Workshop and Tailor, gear, mods and kits in the stores, the Gear Bench open on a Mk II pistol with a Reflex Sight previewed (`--gear-item=ID`, `--gear-preview=MOD` pick others).
`--scavenge --gear-demo`: the debug mission with a modded unit (Mk III pistol with scope, suppressor, EMP coil; plated vest; scrap helmet; Jury-rig) aiming at a revealed Linked two tiles off (saves its own still with `--screenshot`). Needs a display; on
Wayland pass `--display-driver wayland --disable-vsync` so an unfocused window still renders:

```sh
godot-mono --display-driver wayland --disable-vsync --path game -- --colony --demo --screenshot=/tmp/colony.png --frames=300 --quit
```
