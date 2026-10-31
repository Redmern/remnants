# UX audit — Remnants (stage 1, "before")

Scope: every screen in `docs/ux/before/` (56 captures, 1600 px wide) and the UI code in `game/scripts/Colony/Ui`,
`game/scripts/Scavenge/Ui`, `game/scripts/Hud` and `game/scripts/Story`. This document lists problems and the
planned stage-2 fix per screen. Code findings are against commit `e2df81b` (the screenshots' build). Work in
progress in the tree at the time of writing (Reduce motion in `Motion`/`SettingsPanel`, `ActionButton`, help
topics) is not counted. Re-check those items before ticking them off.

**How to read it**

- Priority: **P0** = fix first (blocks understanding or causes jank on common actions), **P1** = next, **P2** = polish.
- Problem tags: `clutter` · `spacing` · `hidden action` · `unclear number` · `too many clicks` · `overlap` ·
  `unreadable text` · `missing feedback` · `jank`.
- InfoTip = a small (i) icon next to a label. Hover: a 1–2 sentence tooltip (`HelpTopic.Short`). Click: a detail
  popover with the long text and live numbers (`HelpTopic.Long` + `Provider` from `src/Remnants.Core/Help`).
  Each one below names the label it sits on and the help topic id → `topic`.
- Screenshots are linked relative to this file: `ux/before/NAME.jpg`.

---

## Contents

1. [Direct user feedback: expedition planner (P0)](#1-direct-user-feedback-expedition-planner-p0)
2. ["Less at once" for other dense screens](#2-less-at-once-for-other-dense-screens)
3. [Top problems at a glance](#3-top-problems-at-a-glance)
4. [Colony HUD](#4-colony-hud)
5. [Survivor details](#5-survivor-details)
6. [Building panels](#6-building-panels)
7. [Build menu](#7-build-menu)
8. [Crafting benches](#8-crafting-benches)
9. [Garage](#9-garage)
10. [Cybernetics / Med Bay](#10-cybernetics--med-bay)
11. [Study / Perks](#11-study--perks)
12. [Research / Schematics archive](#12-research--schematics-archive)
13. [Radio / Trader](#13-radio--trader)
14. [Expedition planner (screen notes)](#14-expedition-planner-screen-notes)
15. [World map](#15-world-map)
16. [After-action](#16-after-action)
17. [Scavenge HUD](#17-scavenge-hud)
18. [Raids](#18-raids)
19. [Story](#19-story)
20. [Main menu, New game, Settings, Pause, Multiplayer](#20-main-menu-new-game-settings-pause-multiplayer)
21. [Cross-cutting: design system](#21-cross-cutting-design-system)
22. [Cross-cutting: motion](#22-cross-cutting-motion)
23. [Cross-cutting: performance / jank list](#23-cross-cutting-performance--jank-list)
24. [Cross-cutting: keyboard / gamepad focus](#24-cross-cutting-keyboard--gamepad-focus)
25. [Cross-cutting: resolutions and UI scale](#25-cross-cutting-resolutions-and-ui-scale)
26. [Frame-time numbers](#26-frame-time-numbers)

---

## 1. Direct user feedback: expedition planner (P0)

> "The expedition menu should have tabs or something like that so the UI is easier to see. There is too much
> information going on."

Screens: [expedition](ux/before/expedition.jpg) · [expedition_vehicle](ux/before/expedition_vehicle.jpg) ·
[kitchen](ux/before/kitchen.jpg) (planner open with 0 picked) · [story_codex](ux/before/story_codex.jpg) (planner under the Codex)

### 1.1 What is wrong today

`ExpeditionPanel` (623 lines) is a single 560 px column that stacks Destination, Vehicle, Arrival, Rations, Party
(a 300 px inner scroll), Colony items and the Launch row. At 1600×868 the panel runs off the bottom of the screen.

| # | Problem | Tag | Evidence |
|---|---|---|---|
| 1 | Launch is below the fold. You have to scroll past every party card and the colony item chips to find it. | hidden action | `_launch` is added after `_inventory` in the ctor; it is not visible in any capture. |
| 2 | Seven sections at once. Most of them are prose, e.g. the night note is three lines ("Night: your people see much less…"). | clutter | expedition_vehicle.jpg |
| 3 | Each picked survivor opens a ~180 px card: 4 slot dropdowns, a Pack row and 7 profile chips. With 2 picked, the party block is taller than the screen. | clutter | `RebuildRows` |
| 4 | The profile chips are icon-only: "King 2", "3", "45", "6.4", "0/21", "0", "Punch 2-4". | unclear number | `ProfileChips.Build` |
| 5 | The zone label crams four facts into one dropdown entry: "Town Outskirts (threat 1, loot tier 1, on the doorstep)". The info below repeats them. | clutter | `Refresh` zone items + `_zoneInfo` |
| 6 | Costs are scattered. Food is in the Party line ("rations 6 food (have 40)"), fuel is in the vehicle text ("fuel 3 (have 400)"), and the wait cost is in the arrival note. No single total. | unclear number | `RefreshSummary`, `RefreshVehicleInfo`, `RefreshArrival` |
| 7 | Warnings (`_readiness`) sit between the Party header and the cards. They are easy to miss and absent from the launch area. | missing feedback | `RefreshSummary` |
| 8 | Speed terms are a raw list: "Buggy ×3 · Upgrades ×1.06". | unclear number | `RefreshVehicleInfo` `plan.Terms` |
| 9 | Pack items are added one at a time: pick a dropdown entry, then press "+". There are no presets and no "same as last time". | too many clicks | `PackPicker` |
| 10 | Every checkbox, slot pick, "+" and Clear sets `_signature = ""` and calls `Refresh()`. That frees and rebuilds every party card, its OptionButtons and its `Portrait`s. Any inventory count change does the same, e.g. a Workshop finishing a craft while the panel is open, which can close an open dropdown. | jank | `RebuildRows` via `_signature` (includes `Inventory.Counts`) |
| 11 | `RefreshSummary` runs every tick: it rebuilds the vehicle list check, ration text and arrival text. `Widgets.Clear(_inventory)` runs on every inventory change. | jank | `Refresh` → `RefreshSummary` |
| 12 | The Online / Co-op launch button has the same weight as Launch. | clutter | `_launchOnline` |
| 13 | It opens over the town with the roster and log still visible. The log's bottom edge overlaps the panel (expedition.jpg, bottom right). | overlap | ColonyHud layout |
| 14 | No hotkeys inside the panel. Every OptionButton has `FocusMode.None`, so keyboard play is impossible. | missing feedback | ctor |

### 1.2 Redesign: a 5-step wizard with tabs

Layout: a centred modal, 760 px wide (`PanelWidth.Wide`) and at most 80% of the viewport height, dimmed behind
(`Overlay`). Parts, top to bottom:

- A step tab strip: `1 Destination · 2 Party · 3 Gear · 4 Vehicle & supplies · 5 Review`. Each tab carries a status dot: done (moss), needs attention (amber), blocked (red).
- The step body, which shows only what that step needs and scrolls on its own if needed.
- A persistent summary bar, always visible on every step (see 1.3).
- A footer with `Back` · `Next` (Primary) and, on step 5, `Launch`. Inside the footer, the Next button names the next step ("Next: Party").

Navigation: `1`–`5` jump to a step, `Tab` / `Shift+Tab` go to the next / previous step, `Enter` = Next (on step 5:
Launch), `Esc` = close (with the plan kept until launch or colony reload). Steps are not locked. Next is only a
suggestion; the user can jump to any step at any time.

**Repeat last expedition**: a button in the header (and `R` while the planner is open). It refills zone, party,
per-survivor loadout, vehicle, rations and wait from the last launched plan, then drops anyone or anything no longer
available and opens step 5 with amber notes for what changed. It is stored per save in `ColonyState`, or in the UI
session if we keep it UI-only.

### 1.3 Summary bar (all steps)

A single 36 px row of chips, each with an InfoTip:

| Chip | Example | InfoTip topic |
|---|---|---|
| Zone + threat | `Suburb Ruins · T2` | `expeditions` |
| Party | `2/4` (seats or Town Hall cap) | `expeditions` |
| Travel | `8 h each way · arrives D1 20:00 NIGHT` | `travel` |
| Food | `9 food (have 39)` | `rations` |
| Fuel | `3 (have 400)` (hidden on foot) | `vehicles` |
| Warnings | `⚠ 2` (click jumps to the step that raised it) | `expeditions` |

The chips update in place (`Unchanged` setters). No rebuild per tick.

### 1.4 Mapping today's content onto the steps (item by item)

| Today (`ExpeditionPanel`) | Goes to | Shown as |
|---|---|---|
| `_away` + `_play` / `_online` (party out) | Not a step | A separate "Party out" state of the same modal: status line, Play / Online buttons. Unchanged. |
| `_zone` OptionButton | 1 Destination | A list of known zones (name + threat pips + travel), left column. Selecting a zone shows its card on the right. |
| World map button | 1 Destination | A "Pick on map" button in the step header. The world map returns to step 1 with the zone set. |
| `_zoneInfo` (`ColonyText.ZoneSummary`: threat, reward, Linked, enemies, strangers, description) | 1 Destination | A zone card with a short summary (threat, loot tier, "Linked seen") and the rest behind an expander "Intel". InfoTips: Threat → `expeditions`; Linked → `fog`. |
| Weather "over Region right now" (from `RefreshArrival`) | 1 Destination | A "Forecast" row with weather icons for the travel window. InfoTip → `weather`. |
| `_arrival` + `_wait` + `_arrivalIcon` | 1 Destination | An "Arrival" row: phase icon + "D1 20:00 NIGHT" + wait dropdown. InfoTip → `travel`. |
| `_arrivalNote` (phase prose) | 1 Destination | One short line ("Night: less sight, more Husks"). The full prose goes in the InfoTip → `stealth`. |
| Route blocked (`_blocked`) | 1 Destination | A red banner on the step and a red dot on tab 1. Launch is disabled with that reason. |
| Party checkboxes (`RebuildRows` head: portrait, name, HP, body icon) | 2 Party | A compact grid of survivor tiles (portrait, name, HP bar, body icon). Click toggles. Hotkeys `Ctrl+1..9` toggle. No gear here. |
| `_partyInfo` (cap, Town Hall level, seats) | 2 Party | Header line "2 / 4 seats". Cap reason in an InfoTip → `expeditions`. |
| `_readiness` (wounded, mission readiness) | 2 Party | Amber badges on the affected tiles, plus a "Readiness" list under the grid. They also count in the summary bar. InfoTip → `injuries`. |
| Field profile (`ProfileChips` per member) | 2 Party | One line per picked survivor: movement, AP, best attack. The full chips sit behind an expander. InfoTips on each chip: movement → `expeditions`, sight → `fog`, armor → `gear_mods`. |
| Slot pickers ×4 (`SlotPicker`) | 3 Gear | A per-survivor sub-tab (portrait tabs on the left). Four slot cells in a 2×2 grid, each showing the current item and its key stat. A click opens a list popover with the "N left" counts. |
| Pack (`PackPicker`, "+", Clear) | 3 Gear | A pack list with − / + steppers per item and weight vs capacity. Presets: "Medic kit", "Same as last time". |
| Profile preview (`UpdatePreview`) | 3 Gear | Live under the slots for the selected survivor only. It updates in place. |
| (new) Gear Bench shortcut | 3 Gear | "Mod this at the Gear Bench" link on a weapon or armor cell. It opens `GearBenchPanel` on that piece and returns here. |
| `_inventory` (all colony items as chips) | 3 Gear | Removed as a block. The slot popovers already list what is free. An optional "Stores" expander stays collapsed by default. |
| `_vehicle` + `_vehicleInfo` | 4 Vehicle & supplies | Vehicle cards (On foot + each vehicle): time each way, fuel, seats, cargo, condition. Broken ones show greyed with the reason. Speed terms go behind an InfoTip → `vehicles`. Noise → `stealth`. |
| (new) Upgrades summary | 4 Vehicle & supplies | The fitted upgrade chips on the selected vehicle card, and a "Garage" link. |
| `_ration` + `_rationInfo` | 4 Vehicle & supplies | Rations: "Pack automatically" by default, with the bill as one line ("9 Potatoes · 3.6 kg"), the field buff as a moss chip, and spoil risk as an amber chip. InfoTips → `rations`, `food_buffs`, `spoilage`. |
| (new) Cargo | 4 Vehicle & supplies | Cargo capacity vs the planned pack weight + rations. InfoTip → `vehicles`. |
| Launch / Launch online | 5 Review | One compact summary (see 1.5), then `Launch` (Primary, `Enter`). "Launch online / co-op" is a secondary button. |
| `PlanForMission` (story) | All | The same wizard. Step 4 shows "On foot (story mission)" locked, and step 5 lists the briefing objectives in one line. |

### 1.5 Step 5: Review & launch (one compact summary)

```
Suburb Ruins (rumored) · 1 day each way by Dust Devil · arrives D1 20:00 NIGHT
Party  Silas (Pistol, Scout Jacket) · Juno (Lead Pipe)
Cost   9 food of 39 · 3 fuel of 400 · back around D3 06:00
Risks  ⚠ Juno hurt (−aim)   ⚠ Night: more Husks   ⚠ 10% of rations spoil on the road
                                            [Launch online]  [ Launch expedition ⏎ ]
```

Each warning links back to its step. Launch is disabled only for blockers (no party, over cap, route closed, not
enough food). The disabled button says why ("Not enough food: 6 of 9").

### 1.6 Code plan (stage 2)

- Split `ExpeditionPanel` into an `ExpeditionPlan` model (zone, picks, loadouts, vehicle, rations, wait; it computes
  the summary from `ColonySim.PlanTravel` / `PlanArrival` / `Rations.Plan`) and five step views. Each step view is
  built once and updated in place.
- Survivor tiles and gear cells are keyed by survivor id and updated, not rebuilt. Only add or remove a tile when
  the candidate list changes. Keep `_signature` out of every user action.
- Per tick, only the summary bar and the visible step refresh. Hidden steps don't refresh.
- Stash the last launched `ExpeditionPlan` for "Repeat last expedition".

---

## 2. "Less at once" for other dense screens

The same rules as the planner: summary first, details on expand, one task per view, a persistent slim status line.

### 2.1 Garage (P1) — [garage](ux/before/garage.jpg), [expedition_vehicle](ux/before/expedition_vehicle.jpg)

Today: vehicle list, 3D preview, 9 stats, 4 slot rows with pickers, a fit preview card, Repairs and "Parts in the
stores", all at once, *plus* the Garage building panel on the right (workers, night shift, production queue, Study).

- Tabs inside the Garage panel: **Vehicles** (list + preview + key stats: speed, cargo, fuel/day, condition),
  **Upgrades** (slots, pickers, before→after), **Repairs** (damaged vehicles and parts needed), **Build** (the Garage
  production queue moved here from the right-hand panel).
- Stats: show 4 by default. Armor, range, noise, seats and max condition go behind a "More" expander. InfoTips:
  Speed → `travel`, Noise → `stealth`, Fuel/day → `vehicles`, Cargo → `vehicles`.
- "Parts in the stores" moves into the Upgrades tab as a filter for the slot picker, not a footer strip.
- `GaragePanel.Refresh` signature includes every stockpile amount (`Stockpile.Amounts`) and inventory count. Any
  production tick rebuilds the list, detail, parts and repairs (`RebuildList`, `RebuildDetail`, `RebuildParts`,
  `RebuildRepairs`), and an open slot dropdown closes. Stage 2: split the signatures per section and update rows in place.

### 2.2 Gear Bench (P1) — [gear_bench](ux/before/gear_bench.jpg)

Today: a 3-column modal (Gear list · Mod slots · Compare) plus "Mods and kits in production" and a Queue row along the bottom.

- Keep the 3 columns (they work), but make **Compare** collapse to the 2–3 stats that change, with "All stats" behind an
  expander. Today 9 rows show "—" for no change.
- The mod pickers per slot ("+ Iron Sights + Reflex Sight + Scope") become one picker per slot with hover preview.
  Today's hover preview is good; keep it.
- Move "Mods and kits in production" + Queue to a **Queue** tab. The orders across Armory/Workshop/Tailor show there with their building.
- Text: "Needs a Sidearm Mk III Kit (none in the stores)." gets a "Queue kit" button next to it (today it is prose).
- Jank: `GearBenchPanel.Refresh` calls `RefreshOrders` every tick, and that runs `Widgets.Clear(_orders)` with no signature.
  The main signature includes all inventory counts and all building levels, so any craft rebuilds list, slots and compare.
  Fix: signature per section; orders update their progress bars in place.

### 2.3 Building panels (P0) — see [section 6](#6-building-panels)

The right column stacks `BuildingPanel` + `HarvestPanel` / `MedBayPanel` / `RadioPanel` / `ResearchPanel` /
`ProductionPanel` / `StudyPanel` in one ScrollContainer. Plan: one building = one panel with tabs
(**Overview · Work · Upgrade** + building-specific tabs), header always pinned.

### 2.4 Med Bay (P1) — [medbay_dispensary](ux/before/medbay_dispensary.jpg)

Tabs: **Patients** (beds, ETA, admit) · **Surgery** (queue) · **Dispensary** (stock + auto-treat order). The header
(Med Bay, level, beds used) is pinned. Today the header scrolls away and the user loses which building this is.

### 2.5 Electronics Lab / Production (P1) — [electronics_lab](ux/before/electronics_lab.jpg)

Tabs: **Queue** · **Recipes** (group tabs inside) · **Charging bench** · **Study**. The queue shows only the active
order expanded; the rest are one line each.

### 2.6 Cybernetics (P2) — [cybernetics_tree](ux/before/cybernetics_tree.jpg)

The detail pane is a wall of text. Order it as Effects → Cost (components) → Requirements (✓/✗ list with fix links)
→ Compare. Put Humanity and Upkeep into a 2-chip row with InfoTips → `humanity`, `prosthetic_tiers`.

### 2.7 Survivor panel (P1)

The opposite problem: tabs are mostly empty (Overview ~45% empty, Family ~70%). Merge **Body** and **Family** into
**Overview** sections with expanders, and keep **Skills** and **Perks** as tabs. See [section 5](#5-survivor-details).

---

## 3. Top problems at a glance

| # | Problem | Where | P |
|---|---|---|---|
| 1 | Expedition planner shows everything at once. Launch is below the fold. | ExpeditionPanel | P0 |
| 2 | Scavenge command frames cost 20–30 ms because 5 panels clear and rebuild their children on every refresh. | `ActionPanel`, `CoopPanel`, `TechPanel`, `LootPanel`, `UnitPanel.RefreshGear` | P0 |
| 3 | Assigning a worker takes 4+ clicks, and the building's empty "+" slot is not clickable. | `BuildingPanel.RefreshWorkers`, `SurvivorPanel.Assign` | P0 |
| 4 | Icon-only numbers everywhere: top-bar resources, rates without a period, "7.2d", the survivor card bars, "L1", field profile chips. | TopBar, SurvivorCard, ProfileChips | P0 |
| 5 | The right column stacks several panels in one scroll. The building header scrolls away and the LOG clips the bottom. | ColonyHud right stack | P0 |
| 6 | Top-bar resources wrap to two rows with 11+ resources and push the HUD down. | TopBar chips (`HFlowContainer`) | P1 |
| 7 | The same event is shown 3×: toast + log + modal/strip. | EventLog.Important → Toasts | P1 |
| 8 | Dev strings leak to the player: "LeftArm", "Learned from Debug", "story: M6", "Skirmish (debug mission)", "Debug party". | MedBay, Archive, Menu, Multiplayer | P1 |
| 9 | The `ReduceMotion` setting exists in `GameSettings` but is neither shown in Settings nor read by `Motion`. | Settings, Motion | P1 |
| 10 | No keyboard/gamepad focus. 36 controls set `FocusMode.None`. | everywhere | P1 |

---

## 4. Colony HUD

Screens: [colony](ux/before/colony.jpg) · [colony_demo](ux/before/colony_demo.jpg) · [new_game_choice](ux/before/new_game_choice.jpg)
(misnamed: it is the colony HUD on day 1) · [pause_menu](ux/before/pause_menu.jpg) (dense background) · [crafting](ux/before/crafting.jpg)

Priority **P0**.

### 4.1 Top bar (`TopBar`)

| Problem | Tag |
|---|---|
| Resource chips are icon + number only (60 / 30 / 0 / 2). There are 11 with a grown colony, and with rates they wrap to a second row (gear_bench.jpg, kitchen.jpg: "2 000 +8000" on row 2). | unclear number, clutter, jank (layout jump when a new resource appears) |
| Rates like "+440", "+1600", "-140.3" are about 7 px, have no period ("/day"?), and on a stock of 36 a −140 is alarming without context. | unreadable text, unclear number |
| Right block: "5" (people), "5/6" (beds), "7.2d" in green (food days) have no labels. "5/6!" in red (overcrowded) is unexplained. | unclear number |
| "NEXT" over the day with 3–4 tiny weather glyphs is unreadable. | unreadable text |
| Eight icon-only buttons in a 4×2 grid (Build, Defend, Repairs, Items, Pantry, Expedition, World map, Menu, plus Power when present). Their meaning shows only on hover. | hidden action |
| No hotkeys for World map, Pantry, Repairs, Research or Radio. | missing feedback |

Fix (stage 2):
- Resource chips: one fixed row. Show the 4 core resources (food, wood, stone, scrap) and fold the rest into a "+7"
  chip that opens a resource drawer. Rates are shown as "+1.2/d" in the tooltip and as a small ▲/▼ arrow on the chip.
  InfoTip on the drawer header → `jobs_output`.
- Right block: label the chips on hover and add InfoTips. People → `mood`, Beds "5/6" → `mood` (crowding),
  Food days "7.2d" → `food`, Power "5/11" → `power`.
- Action buttons: a labelled single row at ≥1600 px ("Build B", "Expedition E", …). Below that, icon-only with the
  hotkey letter drawn as a corner badge.
- New hotkeys: `M` world map, `K` pantry (kitchen), `G` garage (its Repairs tab also lists building repairs), `R` research (Town Hall), `Y` radio, `F1` help index.
- Weather "NEXT": replace it with one forecast chip ("Rain in 6 h"). The full forecast lives in its tooltip. InfoTip → `weather`.

### 4.2 Roster (`RosterPanel`, `SurvivorCard`)

| Problem | Tag |
|---|---|
| Each card has three bars (heart, smiley, XP) with no numbers or legend, plus "L1". | unclear number |
| Status glyphs (zᶻ sleep, ♥, book, bed, "3d 0h" red tags in story_codex) have no legend. | unclear number |
| The job line runs into the age: "In class: Drone Pilotin child 13y". | overlap |
| About 40% of the column is empty with 5 survivors, yet it is a fixed full-height column. | spacing |
| The roster is not a drag source. A worker cannot be dropped on a building. | too many clicks |

Fix:
- Card: name, job, age on one line with ellipsis. Bars get a hover number and a 1-letter label (H/M). "L1" becomes
  a level pip with a tooltip. InfoTips on the roster header: Health → `health`, Mood → `mood`.
- Shrink the panel to its content height (`SizeFlags.ShrinkBegin`) so the map shows below it.
- Drag a card onto a building (in the town or in an open building panel) to assign it. See 6.2.
- Right-click a card → context menu: Assign to… (list of buildings with free slots), Send to Med Bay, Open details.

### 4.3 Event log and toasts (`EventLog`, `Toasts`)

| Problem | Tag |
|---|---|
| Five icon-only filter buttons. "Tip" lines are buried between events. | hidden action, clutter |
| Timestamps "D1 06h" / "01 07h" are tiny and cryptic. | unreadable text |
| `EventLog.Important` raises a toast for what the log also shows, and the after-action modal shows it a third time ("The party is home"). | clutter |
| The log sits under the right column and clips the bottom of building panels (crafting.jpg: "Quality: Crude 70%" cut off). | overlap |
| The tooltip "Expand the log Hotkey: L" renders at the top-left corner over the clock (medbay_dispensary.jpg, trader.jpg). | overlap |

Fix:
- Tips go to a separate "Advisor" chip (one at a time, dismissable), not the log.
- No toast for an event that opens a modal at the same time (party home, raid report).
- Log collapsed by default to 3 lines and a fixed height; it does not share the right column's scroll.
- Timestamps as "Day 2 · 07:00" in the tooltip; the row shows only the hour.

### 4.4 Layout

- The right column (`ColonyHud` `_rightScroll`) holds every building-related panel in one scroll. See section 6.
- Panels in the centre (`floating`: Inventory, Pantry, Expedition, Garage, Repairs) can open together, and the pause
  menu opens on top of them without closing them (pause_menu.jpg). Rule: one centre panel at a time; opening one closes the other.
- `ColonyHud.Refresh` calls 25 panel refreshes every tick. Most return early when hidden, but `ProductionPanel`,
  `HarvestPanel`, `BuildingPanel` and `MedBayPanel` decide visibility inside `Refresh`. Stage 2: refresh only open panels.

---

## 5. Survivor details

Screens: [family](ux/before/family.jpg) (actually the Overview tab) · [survivor_skills](ux/before/survivor_skills.jpg) ·
[survivor_body](ux/before/survivor_body.jpg) · [survivor_family](ux/before/survivor_family.jpg) ·
[body_conditions](ux/before/body_conditions.jpg) · [cybernetics_body](ux/before/cybernetics_body.jpg) · [study](ux/before/study.jpg)

Priority **P1**.

| Problem | Tag | Where |
|---|---|---|
| Overview: two bars with icons only ("100", "100 content"). "Makes 0.46 Wood/h on shift (6.4/day). Hover for the breakdown." hides the important part behind a hover. | unclear number, hidden action | `SurvivorPanel` Overview |
| Job: dropdown "Woodcutter L1 (1/2)", then a ✓ icon button (Assign) and a zᶻ icon button (night shift). Neither is labelled. | hidden action | `_assign` is `IconButton("stat_check", "", …)` |
| Job dropdown greyed while in class, no reason shown (study.jpg). | missing feedback | `RefreshJobs` |
| Skills: the list is clipped at "Firearms" with no scroll cue. 0–100 scale not stated. 6 of 7 skills at 0 still take full rows. | hidden action, clutter | Skills tab |
| Body: "healthy" and "100%" on every row (redundant). Injured rows mix "45% hurt" with "100%" healthy in the same column. "54%" (infection) vs "45%" (limb) for the same arm. | unclear number | `RefreshBody`, `SurvivorPanel.Conditions` |
| Cyber limbs: "Left arm: Network-grade /" and "Right leg: Cyborg Runner L" are clipped by the bar. | overlap | Body rows |
| Treat action "Crude Antibiotics" looks like a link. | hidden action | Conditions |
| Family: Match / Apart / Let be have no current-state highlight. About 70% of the tab is empty. | missing feedback, spacing | Family tab |
| Panel width differs between captures (21% vs 25%). | spacing | — |

Fix:
- Overview top: name, age, job (with an inline "Change job" picker that assigns on select, no ✓ needed), health and
  mood as labelled bars with numbers. InfoTips: Health → `health`, Mood → `mood`, Nutrition → `food_buffs`,
  Output "6.4/day" → `jobs_output` (popover shows the breakdown that is a hover today), Night shift → `shifts`.
- Skills tab: attributes as a compact 2×3 grid with numbers. Skills sorted by value; skills at 0 behind "Show untrained (6)".
  InfoTips on headers: Attributes → `attributes`, Skills → `skills`.
- Body: show only parts that are not healthy, plus a "All healthy" line. Unify to "hurt 45%" everywhere and give
  the infection its own chip ("Infection 54%"). InfoTips: Humanity → `humanity`, Conditions → `injuries`, infection chip → `infection`.
- Treatment actions as real buttons ("Give Crude Antibiotics").
- Family: show the current stance as a selected segmented control (Match / Apart / Let be).
- Tabs: Overview (with Body and Family sections) · Skills · Perks. Width fixed at `PanelWidth.Side` (360 px).
- Keyboard: `Tab` already cycles survivors. Add `Ctrl+1..3` for the tabs.

---

## 6. Building panels

Screens: [colony_demo](ux/before/colony_demo.jpg) (Farm) · [family](ux/before/family.jpg) (Farm) ·
[crafting](ux/before/crafting.jpg) (Armory) · [garage](ux/before/garage.jpg) (Garage) ·
[electronics_lab](ux/before/electronics_lab.jpg) · [kitchen](ux/before/kitchen.jpg) · [research](ux/before/research.jpg) (Town Hall) ·
[medbay_dispensary](ux/before/medbay_dispensary.jpg)

Priority **P0**.

### 6.1 Problems

| Problem | Tag | Where |
|---|---|---|
| One ScrollContainer holds `BuildingPanel` and below it `HarvestPanel`, `MedBayPanel`, `RadioPanel`, `ResearchPanel`, `ProductionPanel` and `StudyPanel`. Scrolling to the queue hides the building's name (electronics_lab.jpg starts at "UPGRADE MAX LEVEL REACHED"; medbay_dispensary.jpg starts at "Night shift: off"). | hidden action | `ColonyHud` `stack` |
| The LOG sits under the column and cuts the last lines (Armory "Quality: Crude 70%", Farm crop cards, Med Bay Admit). | overlap | ColonyHud right column |
| Empty worker slots ("+") say "Free slot: select a survivor and Assign them here." but clicking does nothing. | hidden action, too many clicks | `BuildingPanel.RefreshWorkers` (`EmptySlot` tooltip only) |
| Worker portraits: "Assign and unassign from the survivor's panel." No unassign on the building. | too many clicks | same |
| Upgrade block is always expanded: "LEVEL 3: 4 WORKERS, OUTPUT ×1.5", cost chips with no names, red "Level 3 needs the Armory L3 schematic (see Items → Schematics)", and a disabled full-width "Upgrade" bar that reads as a divider. | clutter, hidden action | `RefreshUpgrade` |
| Night shift row: a "Night shift: off" button plus a two-line explanation, always visible. | clutter | `RefreshNight` |
| The same fact twice: "Potatoes: about 12.1 food per day" (output) and again under CROPS. | clutter | `OutputText` + `HarvestPanel` |
| Cost chips "10", "5 !" with no resource name; "!" is the only signal besides red. | unclear number | `Widgets.CostChips` |
| "Working on Machete, 1.74 work/h on shift: done in 4h": "work/h" is an abstract unit. | unclear number | `ProductionPanel.Status` |
| Production queue: queue a recipe = group tab → recipe dropdown → quantity dropdown → Queue (4 clicks). Dropdown text truncated ("Handheld Radi"). No reorder. | too many clicks, unreadable text | `ProductionPanel.RefreshRecipes` |
| Power line "Draws 3 · idle (nobody on shift) · +25% crafting speed; tier 3 parts need it." with an orange "On the grid" toggle that looks like a primary action. Radio Mast shows "NOT FED" next to an active "On the grid" (trader.jpg). | clutter, missing feedback | `BuildingPanel.RefreshPower` |
| Repair box clears and rebuilds its cost chips on every tick while damaged. | jank | `BuildingPanel.RefreshRepair` (no signature) |
| `BuildingPanel.Refresh` sets texts and tooltips every tick without `Unchanged`. | jank (minor) | `Refresh` |

### 6.2 Plan

One building = one panel, `PanelWidth.Side` (360 px), with a pinned header and tabs. The LOG no longer shares the column.

```
[icon] Armory  ■■□   L2/3                      [x]
Workers [Silas][Juno][ + ]     Makes ~6 items/day (i)
[ Overview | Queue | Upgrade ]         (+ building tabs)
```

- **Header (pinned)**: icon, name, level pips, workers row, the one output line with InfoTip → `jobs_output`. Power
  state as a small chip (Fed / Not fed / Off) with InfoTip → `power`. Damage as a red chip with "Repair" inline.
- **Empty "+" slot is a button**: click → popover of adults sorted by best skill for this job (skill value, current
  job, "idle" first). One click assigns. Right-click a portrait → Unassign. Drag from the roster also works.
  That takes assigning from 4 clicks (card → job dropdown → building → ✓) to 2.
- **Overview tab**: night shift as a toggle with a 1-line note and InfoTip → `shifts`; crops/hunting cards
  (`HarvestPanel`); beds or patients.
- **Queue tab** (production buildings): orders as compact rows (icon, name, ×N, progress). The active one shows its ETA
  in hours. "work/h" goes in the InfoTip → `jobs_output`. Reorder with drag or ▲▼. Below: a recipe grid (icon tiles
  with cost chips, like the build menu) instead of the dropdown. Click a tile = queue 1, Shift+click = ×5,
  Ctrl+click = repeat. Locked tiles say why. Quality odds behind an InfoTip → `schematics`.
- **Upgrade tab**: next level's gain, named cost chips, blockers as links ("Needs Armory L3 schematic → Open
  archive"). The Upgrade button says why when disabled.
- **Building tabs**: Kitchen → Recipe book; Electronics Lab → Charging bench, Study; Med Bay → Patients, Surgery,
  Dispensary; Radio Mast → Calls, Trader, Contracts; Town Hall → Research, Study; Garage → see 2.1.
- Keyboard: `Ctrl+1..5` switch tabs, `Delete` cancels the selected order, `+`/`-` change its count.
- Cost chips: resource name on hover, and the "!" replaced by a small red "short 5" text when not affordable.
- Jank: header and rows updated in place; `RefreshRepair` gets a signature; use `Unchanged` for every label in `Refresh`.

---

## 7. Build menu

Screen: [build_menu](ux/before/build_menu.jpg) · Priority **P2**.

| Problem | Tag |
|---|---|
| 7 category tabs wrap to 2 rows in a 300 px panel ("All Core Housing Gathering / Production Care Power"). | spacing |
| Tiles show cost chips with icons only ("10", "5"). | unclear number |
| Locked tile (Town Hall "free" + lock) does not say why without hover. | hidden action |
| Hint "Pick a building, then click a free plot." is tiny under the grid. | unreadable text |
| No hotkeys for categories or tiles. | missing feedback |

Fix: category tabs as icons with a label on hover, on one row. A tile shows "what it does" in one line under its name
(today only in the tooltip). The lock reason shows in small text on the tile. Number keys `1–9` pick a tile while the
menu is open (the hint shows "Press 1–9"). Placing: show the cost and "Right-click to cancel" next to the cursor.
`BuildMenu.Tile.Refresh` already caches costs by signature (good).

---

## 8. Crafting benches

Priority **P1** (Armory/Tailor/Workshop, Kitchen, Electronics Lab), **P2** (Dispensary).

### 8.1 Armory / Tailor / Workshop — [crafting](ux/before/crafting.jpg), [pause_menu](ux/before/pause_menu.jpg)

- The Items modal opens in the centre at the same time as the Armory panel. Both show item lists (Items: "Machete ×1",
  Armory queue: "Machete ×4 6.9/10").
- Queue rows: "Pistol repeat not started": the state reads as part of the name.
- "Gear Bench" is a small link in the Production header. It is hard to find.
- Fix: see 6.2 (Queue tab with recipe tiles). Order rows show state as a chip (Repeat, Waiting for materials, Not
  started). "Gear Bench" becomes a full button in the Queue tab header.
- Code: `ProductionPanel.RefreshOrders` frees and rebuilds all order rows whenever any `o.Remaining` changes,
  i.e. every time one item finishes. Update rows in place by order id.

### 8.2 Gear Bench — [gear_bench](ux/before/gear_bench.jpg)

See 2.2. In addition:
- The top bar wraps to two rows behind the modal (11 resources at 2 000).
- "Bench at the Armory (level 2)" in the top right: unclear whether it limits anything. InfoTip → `gear_mods`.
- "MK ■■□ 1/2 slots used" next to "Pistol Mk II": two numbers that mean different things (Mk tier vs slot count).
- Compare: deltas "+4%" moss and "+0.2 kg" rust are good. Keep them.

### 8.3 Kitchen — [kitchen](ux/before/kitchen.jpg)

- The recipe book is a 2-column card grid with 3–5 ingredient chips each ("any fresh meat 6/2 · 16h"). That is
  readable, but at 360 px with 12 cards it is a long scroll, and the queue above scrolls out of view.
- "6/2 · 16h": stock/need and spoil time without labels. InfoTip on the book header → `spoilage`.
- Locked cards ("Stim Tea needs the Stim Tea schematic") take as much room as open ones.
- `ProductionPanel.RefreshBook` signature includes `sim.Hour`: the whole book (12+ cards) rebuilds every game hour
  (several times a second at 4×). Fix: update the ingredient chips in place, rebuild only on known-recipe change.
- Fix: cards collapsed to one line (dish, food value, buff icon, "can make ×N"). Selecting expands ingredients.
  Locked cards grouped under "Locked (2)". InfoTip on buffs → `food_buffs`.

### 8.4 Dispensary — [medbay_dispensary](ux/before/medbay_dispensary.jpg)

- Item chips ("Bandage ×8") look like buttons but do nothing.
- Auto-treat priority list with ^ arrows. The last row is clipped. The "▷|" header icon is unlabeled.
- Fix: Dispensary tab (2.4). The stock as a plain list. Priority list with drag handles and a label on the header
  icon. InfoTip → `injuries`.

### 8.5 Electronics Lab — [electronics_lab](ux/before/electronics_lab.jpg), [perks](ux/before/perks.jpg), [study](ux/before/study.jpg)

- Header scrolled away. Group tabs (Components / Devices / Upgrades / Disassembly / Other) clipped at the bottom
  of the panel in perks.jpg and study.jpg.
- Log full of "Electronics Lab queued …" lines.
- Charging bench: `ProductionPanel.RefreshBench` signature has `Done:0.##`, so the rows rebuild on every progress step.
  Fix: update bars in place.
- Fix: tabs per 2.5. Don't log "queued" for the player's own clicks; the queue shows it.

---

## 9. Garage

Screens: [garage](ux/before/garage.jpg) · Priority **P1**. Full plan in 2.1.

Extra notes from the screen:
- Two panels titled "Garage" side by side (the centre Garage panel and the right building panel). Merge them.
- Slot rows: "Engine 1/1", "Armor 0/1", "Utility 1/3" plus chips and "Empty — fit…" dropdowns. Three different
  controls in one row (chip, ×, dropdown).
- "Cargo kg 80 → 120" preview is good. Keep the arrow style for all previews.
- "Repairs: Old Faithful: 3 spare parts [Repair]": fine, but should list the time and who does it.
- "Parts in the stores" chips at the bottom duplicate the slot pickers.
- Hotkeys: none. Add `G` to open the Garage (top bar has no button for it today), and arrows to move between vehicles.

---

## 10. Cybernetics / Med Bay

Screens: [cybernetics_tree](ux/before/cybernetics_tree.jpg) · [cybernetics_body](ux/before/cybernetics_body.jpg) ·
[cybernetics_medbay](ux/before/cybernetics_medbay.jpg) · [medbay_dispensary](ux/before/medbay_dispensary.jpg) ·
[body_conditions](ux/before/body_conditions.jpg) · Priority **P1**.

| Problem | Tag |
|---|---|
| Tree: "×1"/"×2" on nodes (owned count?) and ✓/lock/☆ icons with no legend. About half the grid is blank under T0/T1. | unclear number, spacing |
| Detail pane: 5 "Needs" lines, upkeep, humanity, fitting, components, all prose. "Queue at Electronics Lab" is clipped at the bottom of the modal. | clutter, hidden action |
| Compare: "17 (−15 here)" in both columns. "120%" red vs "135%" with no arrow. | unclear number |
| Med Bay: "Medicine 2001.8 (0.015/patient/h: without it only natural healing)". "3 patient beds" vs "Beds 1/3". | unclear number |
| Patient row: "Juno: LeftArm 45%, HP 80, ETA 19h · infected, splinted, fever". "LeftArm" is an enum name. "ETA —" is unexplained. | unreadable text |
| Discharge is an exit-arrow icon with no label. The Admit dropdown is empty with no placeholder. | hidden action |
| `CyberneticsPanel` tree (`Build` → `Widgets.Clear(_columns)`) rebuilds on every selection change. That is acceptable (user action), but it also rebuilds on any signature change while open. | jank (minor) |

Fix:
- Tree: a legend row (Known ✓ · Available · Locked 🔒 · Owned ×N). Compact the columns to fit content.
- Detail: Effects → Cost → Requirements (each ✗ with its fix link: "Research" / "Open archive") → Compare. Pin the
  Queue button in the detail pane's footer. InfoTips: Humanity → `humanity`, Tier header → `prosthetic_tiers`,
  Upkeep → `prosthetic_tiers`.
- Compare: "Humanity 17 → 2 (−15)" with an arrow and delta colour, the same style as the Gear Bench.
- Med Bay: "Medicine 2002 (enough for ~X days)" with an InfoTip → `health`. Patient rows use `MedicalText` part
  names ("left arm"). A Discharge text button. Admit placeholder "Pick an injured survivor".

---

## 11. Study / Perks

Screens: [perks](ux/before/perks.jpg) · [study](ux/before/study.jpg) · Priority **P2**.

| Problem | Tag |
|---|---|
| 6 discipline tabs are icons only. The category is explained only by hover. | hidden action |
| Learned, available and locked nodes look alike. "Quick Study" orange: selected or available? Lines are faint. | unclear number |
| The tree overflows the tab and the bottom node is clipped. No perk cost shown. | hidden action |
| Study: the class is only text in the survivor's Overview. Starting a class needs the Town Hall or Electronics Lab panel → Study section → student, subject, mentor, manual dropdowns → Start. | too many clicks |

Fix:
- Discipline tabs with short labels (Tech, Field, …). Node states: Learned = filled moss, Available = amber outline,
  Locked = dim with lock; selected = thicker amber border. InfoTip on the tab header → `skills`.
- Fit the tree to the panel (scroll with a visible bar). Show "1 pt" on available nodes.
- Study: "Start a class" button on the survivor Overview (student pre-filled). The Study tab in the building panel
  pre-picks the best mentor. InfoTip → `skills`.

---

## 12. Research / Schematics archive

Screens: [research](ux/before/research.jpg) · [archive](ux/before/archive.jpg) · Priority **P1**.

| Problem | Tag |
|---|---|
| "One research at a time…" in orange, yet every row still shows an enabled "Start". | missing feedback |
| "×0.74" / "Silas ×0.71, leaves the Armory": the multiplier is unexplained. | unclear number |
| Research is in two places (Town Hall panel, archive "Research" button). | too many clicks |
| Archive: "15 9/146 known · 3 to decrypt …" (the leading "15" is Research Data, unlabeled). 146 rows, no search. "Learned from Debug", "story: M6" leak. "Copy 1" label unclear. | unclear number, clutter |
| The toast "Schematic found … queue a Decrypt at the Electronics Lab (L2)" gives instructions but no button. | hidden action |
| "(see Items → Schematics)" prose in many panels instead of a link. | too many clicks |

Fix:
- Research: disable "Start" with the reason while one runs, or make it "Queue next". Researcher factor as "speed ×0.74"
  with an InfoTip → `research`. Archive "Research" opens the Town Hall Research tab directly.
- Archive: a search field, filters with counts, Research Data shown as a labelled chip. "Copy (1 Research Data)".
  Source strings mapped to player text ("found in Town Outskirts", "story mission 6"). InfoTip on the header → `schematics`.
- Every "(see Items → Schematics)" becomes a link button that opens the archive filtered to that schematic.
- Toasts that name an action get that action as a button ("Queue Decrypt").

---

## 13. Radio / Trader

Screens: [radio_calls](ux/before/radio_calls.jpg) · [trader](ux/before/trader.jpg) · Priority **P2**.

| Problem | Tag |
|---|---|
| Calls are inside the Radio Mast building panel. The only HUD cue is the log (in Sandbox). | hidden action |
| Answer (filled), Accept (filled + ✓), Decline (text link): three styles. | clutter |
| "Fades out in 1d 7h" is the key urgency info and is grey and small. The third call is clipped. | unreadable text |
| Trader: price options as rows of icon-only chips ("40 / 8", "30 ! / 6") with one Buy button each. It is not clear they are alternatives. | unclear number |
| trader.jpg was captured at ~75% scale: the smallest text in the set. | unreadable text |
| `RadioPanel` tabs rebuild on signature (fine). | — |

Fix:
- A radio chip in the top bar when calls wait ("Radio 2"), in Sandbox too (Story already has it in its strip).
- Buttons: Primary (Answer/Accept), Secondary (Decline). Fade time as an amber chip when under a day.
- Trader: one card per item with "Pay with: [Scrap 40 + Wood 8] or [Scrap 30 + …]" as a segmented choice and one Buy.
  The colony stock for each needed resource shows in the chip tooltip. InfoTip → `trading`.
- `Y` opens the radio.

---

## 14. Expedition planner (screen notes)

The full redesign is in [section 1](#1-direct-user-feedback-expedition-planner-p0). Extra notes:
- kitchen.jpg: with 0 picked, "Pick a party to see what it packs." and three unchecked rows. Step 2 should be the
  landing step when the zone is already set (from the world map or a briefing).
- story_codex.jpg: the Codex opens over the planner without closing it; two × buttons close together. Modals must stack
  with one dim layer and Esc closing the top one only.
- While a party is out, the panel is just a status line and Play. Keep it, but add the arrival countdown as a chip in the top bar.

---

## 15. World map

Screens: [world_map](ux/before/world_map.jpg) · [world_vehicle](ux/before/world_vehicle.jpg) · Priority **P1**.

### 15.1 Problems

| Problem | Tag |
|---|---|
| The side card is mostly empty: big gaps between the stats and the button, and below the legend. | spacing |
| Card text: "Threat 2 · reward: loot tier 2, 4–6 caches", "1–1 strangers" (degenerate range), "the odd Linked" repeating the flavour line. "Tier 2" is never explained. | unclear number |
| The legend is one run-on paragraph at the bottom of the card ("● threat 1 ● 2 ● 3 ● 4 / bigger marker = richer loot · ○ Linked presence · ? rumored…"), far from the map. Its ◆ ◇ ))) glyphs don't appear on the map, and vehicles are missing from it. | unclear number |
| Route labels "1d / 2d / 3d" converge on COLONY and overlap marker rings ("◉1d"). The vehicle marker "T3 Buggy 59/60" collides with the route label and covers the Town Outskirts marker, so both are hard to click (world_vehicle). | overlap |
| Region names (ASHGROVE, HOME VALLEY, IRON CORRIDOR) are very low-contrast; HOME VALLEY is drawn over a river. | unreadable text |
| The primary button is a disabled grey bar "A party is already out" with no where/when and no link to the party. | missing feedback |
| `WorldMapPanel.Refresh` runs `RefreshStory` → `Widgets.Clear(_story)` every tick while the map is open, and queues a canvas redraw every tick. | jank |
| "Plan expedition" opens the old planner on the Destination section. The world map and planner do not share state. | too many clicks |

### 15.2 Fix

- The world map becomes the large view of wizard step 1. "Plan expedition" → step 2 (Party) with the zone set.
- Zone card: summary first (threat pips, loot tier, travel by foot/vehicle, Linked seen), description and story
  missions behind an expander. InfoTips: Threat → `expeditions`, Travel → `travel`, Linked ring → `fog`, drone → `drones`.
- A legend chip row (rumored "?", threat colour, reward size, Linked ring, depleted arc).
- Hotkeys: `M` toggles, arrows / `Tab` cycle zones, `Enter` = Plan expedition.
- Jank: `RefreshStory` gets a signature (zone id + mission statuses); redraw the canvas only when the party moves or the selection changes.

---

## 16. After-action

Screen: [after_action](ux/before/after_action.jpg) · Priority **P2**.

| Problem | Tag |
|---|---|
| About 45% of the modal is empty between the loot and the button. | spacing |
| The title "AFTER-ACTION REPORT" uses a different, larger font from the other panel titles. | clutter |
| No XP gained, no schematic heard of (the log says "Heard of the Cold Cellar schematic"), no ration use. | missing feedback |
| The toast "The party is home" + log + modal: 3× the same news. | clutter |

Fix: fit the height to content. Sections: Came home (with XP gained), Lost, Recruited, Loot, Intel (schematics heard
of, zone revealed), Supplies used. Buttons: "Back to the colony" (Enter) and "Plan again" (repeat last expedition).
Use the `Title` label variation like every modal. `AfterActionDialog` builds once per report (fine).

---

## 17. Scavenge HUD

Screens: [scavenge](ux/before/scavenge.jpg) · [scavenge_autoplay](ux/before/scavenge_autoplay.jpg) ·
[scavenge_pause](ux/before/scavenge_pause.jpg) · [scavenge_vehicle](ux/before/scavenge_vehicle.jpg) ·
[fog_demo](ux/before/fog_demo.jpg) · [fog_overwatch](ux/before/fog_overwatch.jpg) · [tech_hack](ux/before/tech_hack.jpg) ·
[tech_drone](ux/before/tech_drone.jpg) · [loot_pile](ux/before/loot_pile.jpg) · [salvage](ux/before/salvage.jpg) ·
[field_blood_bag](ux/before/field_blood_bag.jpg) · [story_mission](ux/before/story_mission.jpg)

Priority **P0**.

### 17.1 Layout (`ScavengeHud`)

Turn bar on top; `UnitPanel` (320 px) top-left; right column with `MissionPanel`, `ActionPanel` (300 px),
`StancePanel`, `TechPanel`, `CoopPanel`; `LootPanel` bottom-centre; log bottom-right; `WorldTags` over the map.

### 17.2 Problems

| Problem | Tag | Where |
|---|---|---|
| **Every command frame rebuilds 5 panels.** `ActionPanel.Refresh`, `CoopPanel.Refresh`, `TechPanel.Refresh` and `LootPanel.Refresh` start with `Widgets.Clear(_content)` and re-create every `ActionButton`. `UnitPanel.RefreshGear` clears `_gear` on every `Present`. PERFORMANCE.md: one 20–30 ms frame per command. | jank | Scavenge/Ui |
| The right column grows and shrinks as buttons come and go (Salvage, Recruit, Vehicle, Extract appear when in reach). Buttons under the cursor move. | jank (layout jump) | `ActionPanel` |
| The hint "Click a lit tile to move, a red target to attack with the chosen action." is always shown (2 lines). | clutter | `ActionPanel._hint` |
| AP costs are small orange squares with no legend. | unclear number | `ActionButton` pips |
| Field profile: 8–9 icon-only chips "Rook 3", "3", "48", "9.4", "4.7/21", "0", "4%", "Shoot 8-14", "Punch 2-4". "King 1"/"Rook 3" (movement modes) are unexplained. "4.7/21" appears twice (chip and load bar). | unclear number | `ProfileChips`, `UnitPanel.RefreshProfile` |
| Body card: 7 rows of full bars and "ok" while healthy. That is 7 lines of nothing. | clutter | `UnitPanel` body |
| Turn bar: 6–7 "?" portraits, a stopwatch icon with no label, "SILAS · 3/3 AP" with pips duplicating the unit card. | unclear number | `TurnBar` |
| Evac Beacon subtitle clipped "…next to yo". | unreadable text | `ActionButton` |
| Hotkeys skip numbers: attacks 1–2, items start at 4 (attack slot 3 is reserved). | unclear number | GameSettings bindings |
| Key conflicts: `R` is both `scav_stash` and `scav_drone_action`; `I` is both `scav_salvage` and `scav_ferry`. | missing feedback | `GameSettings` hotkey table |
| No key to cycle my units, centre the camera on the acting unit, or undo a move preview. | missing feedback | — |
| The log says "R0" while the header says "ROUND 1". | unclear number | `ScavengeLog` |
| `TurnBar` rebuilds the initiative portraits when any unit's HP changes (`Hp:0` in the signature). | jank (minor) | `TurnBar.Refresh` |
| The left card shows the *selected* unit; Actions/Stance/Tech show the *acting* unit. With Juno or the drone selected, the right column still offers Silas's "Shoot" and the turn bar says "SILAS · 3/3 AP" (fog_overwatch, tech_drone). | missing feedback | `UnitPanel.Present` vs `ActionPanel.Refresh` (`ActingUnit`) |
| The right column reaches 10–13 rows (scavenge_vehicle, tech_drone). Section order differs: Salvage and Vehicle sit inside Actions, Tech is its own box. Three orange "primary" items compete (selected Shoot, Extract, End turn). | clutter | `ScavengeHud` right column |
| The log uses raw grid coordinates: "Silas moved (14,27)->(14,24)". | unreadable text | `ScavengeLog` |
| Multi-turn progress ("works the dead tree (1/2)", "terminal (1/2)") lives only in the log, not on the button or the map. | missing feedback | `ActionPanel` salvage, `TechPanel` hack |
| Autoplay: only "Waiting for the other units to act…" shows; there is no clear autoplay state. | missing feedback | `ActionPanel._hint` |
| The bottom-right log (440×150) covers map tiles even when it holds one line. | overlap | `ScavengeHud.Log` |

### 17.3 Plan

**Jank first (P0):**
- `ActionPanel`, `CoopPanel`, `TechPanel`, `LootPanel`: keep a pool of `ActionButton`s keyed by action id (attack id,
  item id, node id, npc id, "extract", …). Per refresh: update title, detail, AP, blocked and pressed state through
  `Unchanged`, show/hide pooled buttons, and only create a button for a key never seen in this run. Same model as
  `StancePanel` (signature + keep children).
- `UnitPanel.RefreshGear`: signature on (unit id, equipment, mods, pack, charges); rows updated in place.
- `TurnBar`: drop HP from the order signature; update the HP tint per chip in place.
- Target: no frame over 16.7 ms on a command (see section 26).

**Layout:**
- Right column: fixed width 300 px and a fixed section order: Attack · Use · Stance · Context (Salvage / Loot /
  Recruit / Vehicle / Extract / Tech / Team). Context buttons go in a reserved slot area so nothing above moves.
- Hint: shown only for the first 3 rounds or while aiming ("Click a tile within 4… Esc cancels"); otherwise an (i)
  InfoTip on the Actions header → `ambush` (attacks and hit chance).
- AP pips: a legend in the Actions header tooltip ("■ = 1 AP"). InfoTip → `overwatch` on the Stance header,
  `stealth` on Lookout, `hacking` on Hack, `drones` on Drone, `loot` on Loot, `salvage` on Salvage, `coop` on Team, `alliances` on the alliance banner.
- Unit card: header (portrait, name, HP, AP). The Body section collapses to "All parts ok" when healthy and lists only
  hurt parts otherwise. The field profile shows 4 labelled values (Move "Rook 3", Sight 9.4, Armor 0, Dodge 4%) with
  the rest behind "More". InfoTips: Move → `expeditions`, Sight → `fog`, Armor → `gear_mods`, Dodge → `stealth`,
  Load → `loot`.
- Turn bar: remove the AP duplicate ("SILAS · 3/3 AP") or the card's AP row (keep the bar's: it is next to End turn).
  "?" chips get the tooltip "Unseen: heard but not seen", and the stopwatch gets "Round timer" or is removed.
- Keys: renumber items to start at 3 when there are only 2 attacks (or show the real key on each button; it already
  does). Fix the R and I conflicts (stash → `R`, drone action → `Shift+R`; salvage → `I`, ferry → `Shift+I`).
  New: `Tab` = next own unit, `Home` = centre the camera on the acting unit, `Backspace` = cancel aim.
- Selected vs acting: when the selected unit is not the acting one, dim the right column and title it "Silas's
  turn". The left card gets a "Not their turn" tag. A drone selected while its operator acts shows the drone's own
  actions (Fly, Recall, Mark) first.
- Log: use place names or directions instead of coordinates ("Silas moved 3 tiles north"). Multi-turn progress shows
  on the button ("Salvage Dead Tree · 1/2") and as a small tag on the map node.
- Log: collapsed to 3 lines by default, expands on hover, and keeps clear of the map's bottom-right tiles.
- End turn: keep Space/Enter. Add "End turn with N AP left?" as a 1-line confirm only when a unit has unused AP and
  has not acted (setting: off by default).

### 17.4 Sub-screens

**Fog / overwatch** — [fog_demo](ux/before/fog_demo.jpg), [fog_overwatch](ux/before/fog_overwatch.jpg) · P1

- World tags stack in a 100 px area: "AMBUSH!", "OVERWATCH" (clipped to "…ERWATCH"), damage "11" and two nameplates
  overlap. `WorldTags` stacks per unit, but pop-ups from neighbouring units still collide. Fix: a global collision
  pass (nudge stacks apart) and pop-ups that rise and fade in 600 ms.
- The red "?! 1r" contact marker is unexplained. Fix: tooltip "Heard 1 round ago" and an InfoTip in the legend → `fog`.
- The overwatch ring is a large orange circle across the whole map and under the log. Fix: a thinner, lower-alpha ring,
  drawn only for the selected or hovered unit. InfoTip → `overwatch`.
- Body rows: "Torso · hurt 85%" while HP drops. Is 85% damage or health? The log says "injured (66%)" then "(85%)".
  Fix: one convention ("hurt 85%") everywhere, matching the colony Body tab (section 5).
- "Left arm · T0" (tier code) and "King 1" vs "Rook 3" (movement modes) are jargon. Fix: "Crude arm", "Move 1 (any
  direction)". InfoTip → `prosthetic_tiers`, `expeditions`.
- AMBUSH: explain on hover ("+X% to hit from hiding") with InfoTip → `ambush`. Brace / Lookout get `ambush` / `stealth`.
- fog_demo was retaken at 1920×1080 in stage 1.

**Tech: hack / drone** — [tech_hack](ux/before/tech_hack.jpg), [tech_drone](ux/before/tech_drone.jpg) · P1

- "Hack Terminal: 36% on the last step · 1/2 done": what 36% is is not clear. Fix: "Step 2 of 2 · 36% success". InfoTip → `hacking`.
- The terminal on the map has no label or highlight when it is in reach. Fix: an outline and a "Hack (H)" tag on hover.
- Drone card: five lines of prose ("Flies on its operator's AP; melee cannot reach it…"). Fix: chips for battery,
  range and rounds left; the prose in an InfoTip → `drones`. Make the shared AP explicit: "Uses Silas's AP (1/3)".
- "Mark Husk 6": the target has no name label on the map. Fix: hovering the button highlights the target.
- Hotkeys J/K/M in addition to 1/2/4/5/O/B/L/H: fine, but the right column should group them so the letters read as a set.

**Loot / salvage** — [loot_pile](ux/before/loot_pile.jpg), [salvage](ux/before/salvage.jpg) · P1

- The Loot panel floats at the bottom centre, not near the pile, and covers tiles. Fix: anchor it beside the unit or
  dock it under the unit card.
- Weight shows 3 times (profile chip, Gear footer, Loot bar). "1.8 kg before it slows you" but "Take all" (2.4 kg)
  gives no warning. Fix: one load bar (in the Loot panel when open), and "Take all (slows you: −1 move)" in amber.
  InfoTip → `loot`.
- The same panel title "LOOT" is used for "Your pack" (Drop rows). Fix: title per mode ("Pile", "Your pack").
- "Drop" on "Stone Blocks ×3": drops one or all? Fix: Drop 1 / Drop all.
- Salvage subtitle truncated ("~3.1 Salvaged Wood · 1 action · noi…"). Fix: two lines, or noise as an icon chip.
  InfoTip → `salvage`.
- The field profile chips reflow to 3 rows when loaded (layout jump). Fix: fixed 4-chip summary (17.3).

**Vehicle** — [scavenge_vehicle](ux/before/scavenge_vehicle.jpg) · P2

- "Truck 104/130 HP · stash 0/240 kg" is a status chip inside the action list. Fix: move it to the turn bar (a vehicle
  chip next to the zone name) and keep only Load / Take / Extract in the actions. InfoTip → `vehicles`.
- "Extract by the Truck: leave the zone with what you carry; the…" is truncated. Fix: short title "Extract (X)",
  details in the tooltip.
- The truck is not labelled on the map. Fix: a world tag with its HP when hovered or adjacent.

**Field medicine** — [field_blood_bag](ux/before/field_blood_bag.jpg) · P2

- The Blood Bag named in the capture does not show in Use (only Bandage and Evac Beacon). Either the item is not
  usable on that unit or the capture missed it. Check the debug flag. Fix: show unusable items greyed with the reason.
- "Left leg · hurt · broke…" is clipped. InfoTip → `injuries`.

---

## 18. Raids

Screens: [raid_placement](ux/before/raid_placement.jpg) · [raid_warning](ux/before/raid_warning.jpg) ·
[raid_prep](ux/before/raid_prep.jpg) · [raid_battle](ux/before/raid_battle.jpg) · [raid_report](ux/before/raid_report.jpg) · Priority **P1**.

| Screen | Problem | Tag |
|---|---|---|
| placement | The top bar holds about 25 elements, almost all icon-only (10 resources, "5/16", "7.2d", a wrench with "3"). | unclear number |
| placement | Sandbag Nest: "soak 3" is unexplained. "Tomas — idle (guards elsewhere)" contradicts itself. "—" under the guard portraits is meaningless. "Day watch: the post is empty at night." is confusing. | unclear number |
| placement | Defend catalogue: cost chips are icon-only. "Scrap Turret" appears under the Lights tab. | unclear number |
| placement | Two stacked right panels (Sandbag Nest + Defend), each with its own ×. The log line is clipped at the bottom. | clutter, overlap |
| placement | The placement ghost and rotate arrow are small on a low-contrast grid. | unreadable text |
| warning | The banner pushes the roster down (layout jump). "8 h" shows twice. The current plan ("everyone able defends · children not sheltered · gates open · plan: auto-resolve") is tiny grey text. | jank, unreadable text |
| warning | Banner + toast + log all say the same thing. | clutter |
| prep | The defenders list is clipped (Mara cut in half) with no scroll cue. "combat 0" on every defender. Guards have checkboxes but "guards always fight". | hidden action, unclear number |
| prep | Warning lead time: "+1 h, +6 h, +1 h, +4 h → 12 h (this raid was seen 8 h out)": the maths reads wrong. | unclear number |
| prep | Odds: "Defense 49 against attack 6 (of 42: traps take 26, walls soak the rest down)". The white tick on the bar is unexplained. Mixed precision (+13.7, +2). | unclear number |
| prep | No "decide later" / close. What happens without a choice is not stated (the default is auto-resolve). | missing feedback |
| battle | About 19 portraits in the turn bar; the last is clipped. Friend and foe are hard to tell apart. | clutter |
| battle | "Keep the Town Hall standing · 34/100" is grey like the rest; there is no urgency colour. Red danger zones have no legend. | missing feedback |
| battle | The "Lookout" callout bar uses another style. The log is spammy ("Husk Crawler 1 hit Town Hall for 7" ×3). | clutter |
| report | "×9 … 7 of them down": the other 2 are not accounted for. The bar proportion doesn't match 49 vs 11. | unclear number |
| report | Breakdown is hover-only ("Hover the bar for the breakdown"). "Spike Strip" ×3 as separate chips. "Queue all repairs" without a cost. "LEFT BEHIND": collected or not? | hidden action |
| report | Toasts ("Raid repelled: Repelled 9 raider band.") peek out behind the modal and repeat it. | overlap |

Fix:
- Placement: one right panel at a time (the catalogue replaces the selected-defense card while placing). Cost chips
  named on hover. Terms: "soak" → "absorbs 3 damage per hit" with InfoTip → `defenses`. Guard slots: an empty slot is
  a button (same as building worker slots, 6.2).
- Warning: the banner overlays the map (absolute position) and does not push the roster. One line: "Raider band from
  the south · 3–11 · arrives 15:00 (8 h)" plus a plan chip row (Defenders 3 · Children: not sheltered · Gates: open ·
  Auto-resolve). InfoTip → `raids`. No toast when the banner appears.
- Prep: two columns with their own scroll and a visible scrollbar. Defenders as cards with combat value labelled
  ("Fighting 0") and InfoTip → `defenses`. Guards shown locked (no checkbox). Lead time as one line "Seen 8 h out;
  next time 12 h" with the breakdown in an InfoTip → `raids`. Odds bar: legend for the tick ("auto-resolve threshold").
  Footer: Fight / Auto-resolve as a segmented choice + "Done" (Enter) and Esc = keep the current plan.
- Battle: the turn bar groups by side (yours | theirs) with a count when more than 10. The objective uses `Danger`
  when under 40%. Danger zones get a legend chip.
- Report: breakdown expanded inline (no hover). Group equal chips ("Spike Strip ×3"). Show the repair cost on the
  button. "Left behind (collected)" or "(on the ground)". Account for every raider (down / fled).

Code notes:
- `RaidPrepDialog.RefreshDefenders` clears and rebuilds the defender cards on every signature change (each toggle).
  Acceptable, as time is paused, but update the toggled card in place to avoid the flash.
- `DefensePanel.RefreshStatus` / `RefreshActions` / `RefreshGuards` clear on signature change (fine).
- `DefenseCatalog.Tile.Refresh` caches by signature (good).

---

## 19. Story

Screens: [story_radio](ux/before/story_radio.jpg) · [story_journal](ux/before/story_journal.jpg) ·
[story_briefing](ux/before/story_briefing.jpg) · [story_codex](ux/before/story_codex.jpg) · [story_mission](ux/before/story_mission.jpg) · Priority **P1**.

| Problem | Tag | Where |
|---|---|---|
| Starting a mission takes 6 steps: toast/strip → Radio → Take the call → (dialogue) → Journal → Briefing → Plan the party → planner. The strip says "open the journal for the briefing". | too many clicks | StoryHud |
| Radio: "Lantern  First Light" without labels (caller vs mission). "Heard" entries tiny and not replayable. The toast says "(+2 more)" while the list shows 1. | unclear number | StoryHud radio inbox |
| Journal: "Open" vs "Briefing" sound alike. "Done" looks like a button. About 60% empty. | hidden action, spacing | StoryHud journal |
| Briefing: "(a fixed place)", "Threat 1" with no scale, "AP" jargon in "Teaches". No way back to the journal. | unclear number | Briefing |
| Codex: no list/detail split, long text column with no read/unread markers. Opens over the expedition planner. | clutter, overlap | `CodexPanel` |
| Mission (field): three objectives all "(optional)", so the main goal is unclear. Dialogue bar repeats the log. | unclear number, clutter | `MissionPanel`, `ChatterStrip` |
| Modal titles differ: "Journal" mixed-case display font vs "SETTINGS"/"PAUSED" caps. | clutter | — |
| Radio inbox and journal `Refresh` clear and rebuild on every call (only on open, fine). `MissionPanel.Refresh` keys on round + objectives (fine). | — | StoryHud |

Fix:
- After a call ends with a new mission, show its briefing directly (one click: "Plan the party").
- Journal rows: one primary action per state (Briefing → "Plan" when ready, "Resume" when the party is out). "Done" as
  a plain moss label.
- Briefing chips: "Threat 1 of 5" with an InfoTip → `expeditions`. Drop "(a fixed place)". "Back" returns to the journal.
- Codex: left list (entries, unread dot), right reader. One modal at a time.
- MissionPanel: main objective first in Display weight. "(optional)" goes to a separate "Bonus" group.
- Hotkeys: `J` journal, `Y` radio, `O` codex.

---

## 20. Main menu, New game, Settings, Pause, Multiplayer

Screens: [menu](ux/before/menu.jpg) · [menu_settings](ux/before/menu_settings.jpg) · [settings](ux/before/settings.jpg) ·
[pause_menu](ux/before/pause_menu.jpg) · [scavenge_pause](ux/before/scavenge_pause.jpg) · [multiplayer](ux/before/multiplayer.jpg) ·
[new_game_choice](ux/before/new_game_choice.jpg) · Priority **P1** (Settings), **P2** (rest).

Note: `new_game_choice.jpg` was retaken with `--new-game-choice` (stage 1 added the flag).

### 20.1 Main menu
- "CONTINUE" and "LOAD" are greyed with no reason (no save) and look like unfocused buttons. Fix: hide them, or show "No save yet".
- "Skirmish (debug mission)" link under QUIT is visible to players. Fix: behind a debug flag.
- No version/build number. The "Fonts: Barlow…" credit is about 9 px dark-on-dark. Fix: credits screen, version in a corner at `Small`.
- Main menu buttons are condensed ALL CAPS; pause menu buttons are sentence case. Pick one (sentence case, Display font).

### 20.2 Settings (P1)
- UI scale says "100%" while the note says "Window 2568×1393 → interface at 139%". The relation (auto × setting) is not explained. Window size "1280 × 720" does not match the real window.
- Checkboxes (Fullscreen, Vertical sync) don't follow the label | control columns of the dropdown rows.
- Reduce motion and Show frame time were added in stage 1 (Display tab); still no text size, no colour-blind option, no FPS cap, no reset to defaults.
- Fix: rows as "Label (i) | control". UI scale shown as "Auto 139% × 100% = 139%". Add **Reduce motion**, **Text size** (Small / Normal / Large: adds +0/+2/+4 to the type scale), **Show hotkey badges**, **Reset to defaults**. Controls tab: show conflicts in red (see 17.2).
- In-game Settings: pause colony time while open (settings.jpg shows play still active).

### 20.3 Pause
- Opens over the Items window and the Armory panel without closing or dimming them enough (pause_menu.jpg). Fix: one `Overlay` dim at 60% and close the centre panels.
- Save/Load: no last-save time. Fix: "Save (last: Day 3 14:00)".
- scavenge_pause: "Leave the zone" duplicates the turn bar's Leave. "Quit" doesn't say whether the run is kept
  (the confirm does, but only after the click). The right panel stays readable behind a weak dim. Fix: "Quit to
  desktop (run is lost)" as the label, the same 60% dim as the colony pause, and the title "Paused" in the shared `Title` style.

### 20.4 Multiplayer
- A tooltip floats at the top-left corner ("Toggle ready: the mission starts when everyone is"). It is placed away from its control and truncated.
- "(host)" suffixes, raw "127.0.0.1:7777", "threat 2" without scale, "Party: Debug party (Arlo, Noor, Emil)".
- Host and Join are equal orange primaries; Ready looks disabled with no reason; the empty LOBBY area has no placeholder.
- Fix: two tabs, **Host** (zone, mode) and **Join** (address with recent list). Lobby shows "Not connected" until connected. Ready says why it is disabled. Player-facing party text.

---

## 21. Cross-cutting: design system

Today there are two parallel helper sets: `Colony/Ui/Ui.cs` (`Ui.Label(size)`, `Ui.Button`, `Ui.Muted`, `Ui.Bad`,
`Ui.Hex`) and `Hud/Widgets.cs` (`Widgets.Label(variation)`, `IconButton`, `TextButton`, `Chip`, `CostChips`). Font sizes
are set ad hoc (`Ui.Label(text, 13)`, `AddThemeFontSizeOverride("font_size", 22)` in `UnitPanel`, `15` in `Widgets.Chip`,
and so on). Separations are 1, 2, 3, 4, 6, 8 and 10.

### 21.1 Spacing scale
`Space.Xs = 4`, `Sm = 8`, `Md = 12`, `Lg = 16`, `Xl = 24`. Use 4 inside chips and rows, 8 between controls,
12 between sections, 16 for panel padding, 24 between columns. Replace the 1/2/3/6/10 separations.

### 21.2 Type scale
Keep the `HudTheme` label variations and make them the only way to size text:

| Variation | Font | Size | Use |
|---|---|---|---|
| `Title` | Display bold | 34 → **28** | Modal titles only (After-action, Pause, Settings, Journal, Codex: all the same). |
| `Display` | Display | 20 | Panel titles. |
| `Heading` | Display | 13 | Section headers (caps). |
| body (default) | Body | 15 | Normal text. |
| `Muted` | Body | 14 | Secondary text. |
| `Small` | Body | 12 → **13** | Minimum size for any text the player needs. Nothing below 13 px at 100%. |
| `Number` | Numbers | 16 | Values, chips. |

Ban `Ui.Label(text, size)` and `AddThemeFontSizeOverride` in panels; use variations. Text size setting scales the theme.

### 21.3 Panel widths
`PanelWidth.Side = 360` (roster, survivor, building column), `Narrow = 300` (scavenge actions), `Wide = 760`
(expedition wizard, garage, gear bench, cybernetics), `Modal = 520` (settings, pause dialogs, reports). Today: 300,
320, 360, 560 and full-width modals vary per capture.

### 21.4 Tokens to standardise on
- Colours: `Palette` only. Drop `Ui.Muted`/`Ui.Bad`/`Ui.Good` aliases and `[color=#999]` literals (e.g. `ExpeditionPanel.RefreshVehicleInfo`).
- Buttons: `Widgets.IconButton` with variations `Primary` (one per view), default (secondary), `Flat` (tertiary/close),
  `Tab`. `Ui.Button` goes. Destructive actions (Remove, Cancel order, Decline) get a `Danger` variation.
- Containers: `Card`, `CardSelected`, `Inset`, `Chip`, `Toast`. Add `Banner` (warning strip) and `SummaryBar`.
- New shared components: `InfoTip(topicId)`, `Tabs` (one style; today Settings/Codex/Survivor/Radio tabs look
  different, and `RadioPanel`/`SurvivorPanel` use Godot `TabContainer` while others use `Tab` buttons),
  `Expander(title, body)`, `Stepper(−/+)`, `KeyBadge("E")`, `LinkButton(target)` for "see X → Y".
- Cost chips: resource name on hover (exists), add a "short N" suffix instead of "!".
- Dev strings: a lint pass. No enum names (`BodyPart.ToString()`) or data ids ("Debug", "M6") in UI text.

---

## 22. Cross-cutting: motion

`Motion` has `Fast = 0.12` and `Normal = 0.2` with ease-out cubic. Good base.

- Durations: 120 ms for hover/press/tab switches, 180 ms for panels and popovers, 220 ms max for modals. `Flash` uses
  600 ms; drop it to 400 ms.
- **Reduce motion**: wire `GameSettings.ReduceMotion` into `Motion.Reveal` / `Toggle` / `PressFeedback` / `Flash`
  (instant show, no scale, colour change without tween), into `Toasts` slide-in and the dialogue typewriter
  (`DialoguePanel`). Add it to Settings → Display.
- No layout jumps: panels appear in reserved space or as overlays. The scavenge right column reserves its context slot
  area (17.3). The top bar keeps one row (4.1). Toasts already float in a plain `Control` (good).
- Refresh only changed rows: every list keyed by id with in-place updates. `Unchanged` for labels, tooltips,
  textures, visibility and colours (already used by `TopBar` and `SurvivorCard`; extend to every panel).
- Progress bars tween to their new value over 120 ms instead of jumping (skip under Reduce motion).

---

## 23. Cross-cutting: performance / jank list

Panels that free and re-create children on refresh. "Per refresh" = every tick (colony) or every command (scavenge)
while visible. "On change" = guarded by a signature, but the signature changes often.

| File:method | When it rebuilds | Severity |
|---|---|---|
| `Scavenge/Ui/ActionPanel.cs:Refresh` | Every refresh (`Widgets.Clear(_content)`, all `ActionButton`s new) | P0 |
| `Scavenge/Ui/CoopPanel.cs:Refresh` | Every refresh | P0 |
| `Scavenge/Ui/TechPanel.cs:Refresh` | Every refresh | P0 |
| `Scavenge/Ui/LootPanel.cs:Refresh` | Every refresh (incl. new `StatBar`) | P0 |
| `Scavenge/Ui/UnitPanel.cs:RefreshGear` | Every `Present` (new rows, chips, `StatBar`) | P0 |
| `Scavenge/Ui/TurnBar.cs:Refresh` | On change; signature includes every unit's HP | P2 |
| `Scavenge/Ui/WorldTags.cs` (stack `Widgets.Clear(stack.Box)`) | On tag change per unit | P2 (check) |
| `Colony/Ui/ExpeditionPanel.cs:RebuildRows` | Every user action in the party list and every inventory count change | P0 |
| `Colony/Ui/ExpeditionPanel.cs:RefreshSummary` | Every tick; `Widgets.Clear(_inventory)` on inventory change | P1 |
| `Colony/Ui/GaragePanel.cs:Refresh` → `RebuildList/Detail/Parts/Repairs` | Signature includes all stockpile amounts: in practice every production tick | P0 |
| `Colony/Ui/GearBenchPanel.cs:RefreshOrders` | Every tick (no signature) | P1 |
| `Colony/Ui/GearBenchPanel.cs:Refresh` (list, slots, compare) | Any inventory count or building level change | P1 |
| `Colony/Ui/ProductionPanel.cs:RefreshBook` | Signature includes `sim.Hour`: every game hour | P1 |
| `Colony/Ui/ProductionPanel.cs:RefreshOrders` | Every time an order's `Remaining` changes | P1 |
| `Colony/Ui/ProductionPanel.cs:RefreshBench` | Every charging progress step (`Done:0.##`) | P1 |
| `Colony/Ui/BuildingPanel.cs:RefreshRepair` | Every tick while the building is damaged | P1 |
| `Colony/Ui/WorldMapPanel.cs:RefreshStory` | Every tick while the map is open | P1 |
| `Colony/Ui/PantryPanel.cs:Refresh` | On change; signature includes spoil time in 2 h steps | P2 |
| `Colony/Ui/InventoryPanel.cs:RefreshItems` | Any inventory change | P2 |
| `Colony/Ui/MedBayPanel.cs:RebuildPatients` / `RefreshSurgeries`, `MedBayPanel.Dispensary.cs:RefreshDispensary` | On change | P2 |
| `Colony/Ui/ResearchPanel.cs:Refresh` (`_data`, `_options`) | On change | P2 |
| `Colony/Ui/RosterPanel.cs:Refresh` | Only when the survivor list changes (good) | — |
| `Colony/Ui/RaidPrepDialog.cs:RefreshDefenders` | Every toggle (time paused) | P2 |
| `Colony/Ui/CyberneticsPanel.cs` tree `Build`, `PerksTab` tree `Build` | On selection / signature change | P2 |
| `Story/StoryHud.cs` radio inbox / journal `Refresh` | On open and on each call | P2 |

Stage-2 rule: a panel builds its children once per key (id) and updates them through `Unchanged`. A signature may
decide *what* to update, never trigger a full clear of a list the user is interacting with.

---

## 24. Cross-cutting: keyboard / gamepad focus

- 36 controls set `FocusMode = None` (all `Widgets.IconButton`/`TextButton`, every OptionButton in the planner, Settings
  checkboxes). There is no focus path through any panel and no gamepad support (no Joypad bindings in `GameSettings`).
- HudTheme already defines an amber focus box (`focus` stylebox); it is never seen.

Plan:
- Default `FocusMode.All` for buttons, tabs, pickers and checkboxes inside panels and modals; keep `None` only on
  map-overlay chips and world tags.
- Each panel/modal sets its first focus on open (planner: current step's first control; Settings: first tab; dialogs:
  the Primary button). `Esc` / gamepad B closes, `Enter` / A activates, `Tab` / bumpers switch steps or tabs.
- Focus neighbours set explicitly in grids (build menu tiles, recipe tiles, perk and cyber trees).
- Gamepad bindings in `GameSettings.Bindings` for: confirm, back, next/prev tab (LB/RB), end turn (Y), menu (Start),
  cycle unit/survivor (RT/LT).
- Show `KeyBadge` hotkey hints on buttons (setting "Show hotkey badges"), and swap to gamepad glyphs when the last input was a pad.
- Hotkeys to add (colony): `M` world map, `K` pantry, `G` garage, `R` research, `Y` radio, `J` journal,
  `O` codex, `F1` help index, `Ctrl+1..5` panel tabs. In the expedition planner: `1–5`, `Tab`, `Enter`, `R` repeat
  last. Scavenge: `Tab` next unit, `Home` centre camera, fix the R/I conflicts.

---

## 25. Cross-cutting: resolutions and UI scale

`GameSettings.ApplyScale` sets `ContentScaleFactor = AutoScale(height) × UiScale`, with `AutoScale = clamp(height/1000, 0.85, 3)`
and `UiScales = 0.75…1.5`. The captures were taken at different effective scales (trader.jpg ~75%, radio_calls.jpg ~120%).

Checks for stage 2 (each at UI scale 85%, 100% and 130%):

| Resolution | Auto scale | What to check |
|---|---|---|
| 1280×720 | 0.85 | Top bar fits on one row with 11 resources (folded). Expedition wizard (760 px) fits in width; body scrolls, summary bar and footer stay visible. Right column 360 px + roster leave ≥ 400 px of map. Scavenge unit card + actions leave the centre tiles visible. Nothing under 13 px. |
| 1920×1080 | 1.08 | Labelled top-bar buttons appear. No panel clipped by the log. Building header pinned. |
| 2560×1440 | 1.44 | Panels don't stretch: max widths hold. Text crisp (`LinearWithMipmaps` already set). Icons from the 16/18/20 px sets don't blur at ×1.44; check `Icons.Get` sizes. |

- Make the Settings note explain the maths (20.2).
- Add a debug flag `--ui-check` that opens each panel at the three sizes for screenshots (stage-2 "after" set).

---

## 26. Frame-time numbers

Fill in from the perf monitor (frames over 20 ms, see `docs/PERFORMANCE.md`). Same machine and flags as the baselines there.

**Stage 1 (measured, headless A/B, see `docs/PERFORMANCE.md` → "Scavenge command frames").** Scavenge autoplay, 40 s,
CPU side of each command frame, before (e2df81b) → after stage 1:

| Zone | hud mean / p95 | sync mean / p95 | command frames mean / p95 | frames > 16.7 ms |
|---|---|---|---|---|
| core_spire | 4.05 / 9.30 → **2.69 / 6.75** | 5.17 / 6.38 → **1.25 / 2.06** | 10.7 / 20.1 → **9.8 / 15.8** | 6 → **3** |
| network_relay | 4.14 / 8.26 → **2.55 / 4.84** | 5.12 / 7.44 → **1.34 / 1.82** | 10.0 / 22.0 → **8.9 / 17.5** | 6 → **3** |

What is left over budget is AI turn time (`execute`, up to 40–50 ms on core_spire). Rows below get filled in stage 2.

| Scenario | Before (ms, p50 / worst) | After stage 2 (ms, p50 / worst) |
|---|---|---|
| Scavenge: move one tile (command frame) | TBD | TBD |
| Scavenge: attack (command + AI replies) | TBD | TBD |
| Scavenge: end turn | TBD | TBD |
| Colony 4×, Expedition planner open, 2 picked | TBD | TBD |
| Colony 4×, Garage open, production running | TBD | TBD |
| Colony 4×, Gear Bench open | TBD | TBD |
| Colony 4×, Kitchen recipe book open | TBD | TBD |
| Colony 4×, Electronics Lab with charging bench | TBD | TBD |
| Colony 4×, World map open | TBD | TBD |
| Colony: open / close building panel | TBD | TBD |
| Expedition planner: tick a survivor | TBD | TBD |
