# Schematics

Phases 1–2 are built (schematics core, research and trade); see "As built" at the end for where the code differs from
this design. Numbers are first guesses checked by the playtest bot (BALANCE.md "Schematics pass").

## In one paragraph

Today the game has **blueprints**: a handful of items that drop in tier 2–3 zones, get "studied" at a building, and
unlock one recipe each. Datashards work the same way but unlock perks. This document folds both into one idea, the
**Schematic**: a piece of knowledge that lets your colony build or make something new. Every colony starts with a
**default set** that is enough to play the whole early game. Everything else comes from playing: looting, killing
Linked, hacking terminals, trading, researching, and, in Story mode, from missions. **Nothing is story-only.** The
story is the reliable, guided way to get a schematic; the open world is the lucky or patient way.

## What a schematic is

A schematic is something you **know**, not something you **own**. In the field it is an item (a plan on paper, a
data chip, an encrypted Network core) that you carry home like any other loot. Once your colony learns it, the item
is gone and the knowledge stays for good. It cannot be lost, stolen or used up.

A schematic can unlock any of these:

| Unlock kind | Example | What changes in the game |
|---|---|---|
| A building type | Watchtower, Greenhouse | it shows up in the build menu |
| Building levels | Workshop L3, Med Bay L3 (Hospital) | the upgrade button works past L2 |
| An item recipe | Hunting Rifle, Scanner, Evac Beacon | the recipe can be queued at its building |
| A weapon or armor recipe | Linked Carbine, Riot Armor | same, at the Armory |
| A device | Signal Jammer, Scout Drone, Cyberdeck | same, at the Electronics Lab |
| A prosthetic tier | T1 Mechanical, T2 Powered, T3 Cyborg, T4 Network-grade | every limb recipe of that tier |
| A prosthetic specialisation | Runner legs, Hacker arm, Thermal eye | that branch of the limb tree |
| A vehicle chassis, engine or upgrade | Motorbike, Small Engine, Muffler | that build or fit at the Garage |
| A technique (old "datashards") | Network Ghost, Override, Drone Payload | the perk can be learned |

One schematic can unlock several things when they belong together (for example "Cybernetics" unlocks both the cyber
arm and the cyber leg). We keep that rare, so a find always feels concrete: "you can now make X".

**What stays outside the system:** field manuals (they speed up study of a skill; they are consumables, not unlocks)
and the Town Hall level cap (it is still the main pacing wall, see BALANCE.md).

## The default set

Every new colony, in Story and Sandbox alike, knows all of this from day one. The test for "default": a careful player
can reach Town Hall 3, an Armory and an Electronics Lab at L2, and run Outskirts, Scrapyard Flats, Suburb Ruins and
Flooded Mall without ever finding a schematic. They will be under-armed for the Rail Depot and above, and that is the
point.

**Buildings**

| Building | Levels known |
|---|---|
| Town Hall | 1–3 |
| Shelter, House | 1–3 |
| Woodcutter, Quarry, Scrapyard, Farm | 1–3 |
| Kitchen | 1–3 |
| Radio Mast (new, cheap: 15 wood, 10 scrap) | 1 |
| Garage (new, vehicles) | 1 |
| Workshop, Tailor, Med Bay, Armory, Electronics Lab | 1–2 |

The Radio Mast is new and is in the default set because it is how both modes reach the outside world: in Story mode
it is how characters call you, in Sandbox it is how traders and contracts find you.

**Recipes** (everything in `recipes.json` today that has no blueprint, minus three moved behind schematics, see
"Migration")

- Workshop: Herbal Remedy, Peg Leg, Hook Arm, Lead Pipe, Rebar Spear, Med-kit
- Tailor: Plant-fiber Cloth, Bandages, Rag Coat, Backpack, Padded Vest, Climbing Gear, Scout Jacket
- Kitchen: Simple Meals, Hearty Stew, Canned Rations
- Armory: Machete, Pistol
- Electronics Lab: Salvaged Circuits, Flares, Motion Sensor, Binoculars, Drone Parts, Hacking Kit
- Prosthetics: tier T0 Crude (peg leg, hook arm)
- Garage: T0 Handcart, and the basic vehicle parts it needs (wooden wheels, spare parts)

That is three melee weapons, a gun, two armors, scouting and fog tools, a hacking kit, med-kits and crude limbs:
a full early game. What it lacks on purpose: long guns, heavy armor, drones, Network tech, good prosthetics, anything
with an engine, and the third level of the production buildings.

## The catalogue

Tier says where it is normally found: **1** = Home Valley (Outskirts, Scrapyard Flats), **2** = Ashgrove (Suburb
Ruins, Flooded Mall), **3** = Iron Corridor (Rail Depot, Network Relay), **4** = The Black Grid (Core Spire).
This "find tier" is not the same as an item's own tier. Vehicles use six tiers of their own (T0 Handcart, T1 Bicycle
Cart, T2 Motorbike, T3 Buggy, T4 Truck, T5 Armored Rig), shown in the Unlocks column. Their find tiers are 1, 2, 2, 3
and 4 (the handcart is default), so the Buggy is a late-Ashgrove find, the Truck an Iron Corridor one, and the Armored
Rig comes only from the Black Grid. Prosthetic tiers (T0–T4) work the same way.

Source codes:

- **Loot** = caches and lockers in zones of that tier or higher
- **Term** = hacked terminals
- **Kill** = dropped by a killed enemy (named)
- **Trade** = Marrow's caravan (Story) or a passing trader (Sandbox)
- **Research** = researched at the Town Hall with Research Data
- **Story** = rewarded by a story mission (number from STORY.md)

"New" marks content that does not exist yet. Prosthetic ids follow the prosthetics-tree branch and will be renamed
to match it when that lands.

| id | Unlocks | Tier | Sources |
|---|---|---|---|
| `sch_watchtower` | **New** Watchtower building L1–2 (sight over the colony, an overwatch post on defense maps) | 1 | Loot 1+, Trade, Research, Story M2 |
| `sch_camo_cloak` | Camo Cloak recipe (Tailor L2) | 1 | Loot 1+, Trade, Research, Story M3 |
| `sch_grappling_hook` | Grappling Hook recipe (Workshop L2) | 1 | Loot 1+, Trade, Research, Story M4 |
| `sch_radio_mast_2` | Radio Mast L2 (traders call twice as often; contracts from farther zones) | 1 | Loot 1+, Trade, Research, Story side "Marrow's Debt" |
| `sch_workshop_3` | Workshop L3 | 2 | Loot 2+, Trade, Research, Story M5 |
| `sch_tailor_3` | Tailor L3 | 2 | Loot 2+, Trade, Research, Story M11 |
| `sch_greenhouse` | **New** Greenhouse building L1–2 (farming that ignores bad weather) | 2 | Loot 2+, Trade, Research, Story M11 |
| `sch_rifle` | Hunting Rifle recipe (Armory L3) | 2 | Loot 2+, Trade, Research, Story M6 |
| `sch_riot_armor` | Riot Armor recipe (Armory L2) | 2 | Loot 2+, Trade, Research, Story M9 |
| `sch_scanner` | Scanner recipe (Lab L2) | 2 | Loot 2+, Term, Research, Story M8 |
| `sch_evac_beacon` | Evac Beacon recipe (Lab L2) | 2 | Loot 2+, Term, Trade, Research, Story M9 |
| `sch_scout_drone` | Scout Drone recipe (Lab L2) | 2 | Loot 2+, Term, Trade, Research, Story M8 |
| `sch_mech_limbs` | Prosthetics T1 Mechanical (all slots) | 2 | Loot 2+, Trade, Research, Story M7 |
| `sch_powered_limbs` | Prosthetics T2 Powered (all slots; includes today's Servo Arm) | 2 | Loot 2+, Kill Linked (2%), Research, Story M7 |
| `sch_spec_labor` | Labor arm specialisation | 2 | Loot 2+, Trade, Research, Story M15 (choice) |
| `sch_spec_runner` | Runner leg specialisation | 2 | Loot 2+, Trade, Research, Story M15 (choice) |
| `sch_spec_armored` | Armored leg specialisation | 2 | Loot 2+, Trade, Research, Story M15 (choice) |
| `sch_spec_precision` | Precision arm specialisation | 2 | Loot 2+, Trade, Research, Story M15 (choice) |
| `sch_network_ghost` | Technique: Network Ghost perk (was `ds_network_ghost`) | 2 | Term, Kill Linked Drone (2%), Research, Story M10 |
| `sch_armory_3` | Armory L3 | 3 | Loot 3+, Trade, Research, Story M12 |
| `sch_lab_3` | Electronics Lab L3 | 3 | Loot 3+, Term, Research, Story M14 |
| `sch_med_bay_3` | Med Bay L3 "Hospital" | 3 | Loot 3+, Trade, Research, Story M15 |
| `sch_linked_carbine` | Linked Carbine recipe (Armory L3) | 3 | Loot 3+, Kill Linked (3%) / Warden (25%), Research, Story M14 |
| `sch_linked_plating` | Linked Plating recipe (Armory L3) | 3 | Loot 3+, Kill Linked (2%) / Warden (15%), Research, Story M12 |
| `sch_signal_jammer` | Signal Jammer recipe (Lab L3) | 3 | Loot 3+, Term, Research, Story M13 |
| `sch_cyberdeck` | Cyberdeck recipe (Lab L3) | 3 | Term, Trade, Research, Story M11 |
| `sch_cybernetics` | Prosthetics T3 Cyborg (cyber arm, cyber leg, and the eye slot if added) | 3 | Loot 3+, Kill Warden (10%), Research, Story M15 |
| `sch_override` | Technique: Override perk (was `ds_override`) | 3 | Term, Kill Warden (10%), Research, Story M13 |
| `sch_drone_payload` | Technique: Payload perk (was `ds_drone_payload`) | 3 | Term, Kill Linked Drone (3%), Research, Story M16 |
| `sch_spec_hacker` | Hacker arm specialisation | 3 | Term, Research, Story side "Seven's Name" |
| `sch_spec_blade` | Blade arm specialisation | 3 | Loot 3+, Trade, Research, Story side "Cole's Last Job" |
| `sch_spec_jumper` | Jumper leg specialisation | 3 | Loot 3+, Trade, Research, Story M15 (choice) |
| `sch_spec_silent` | Silent leg specialisation | 3 | Loot 3+, Term, Research, Story side "Seven's Name" |
| `sch_spec_scope_eye` | Scope eye specialisation | 3 | Loot 3+, Trade, Research, Story M15 (choice) |
| `sch_network_grade` | Prosthetics T4 Network-grade | 4 | Loot 4 (Core Spire), Kill Warden (5%), Story M16 |
| `sch_spec_gun` | Gun arm specialisation (T3+) | 4 | Loot 4, Kill Warden (5%), Story side "Cole's Last Job" (ally branch) |
| `sch_spec_thermal_eye` | Thermal eye specialisation | 4 | Loot 4, Term (Core Spire only), Story M17 |
| `sch_garage_2` | Garage L2 (engine work; motorbike and buggy builds) | 1 | Loot 1+, Trade, Research, Story M6 |
| `sch_bicycle_cart` | Vehicle T1 Bicycle Cart (chassis recipe) | 1 | Loot 1+, Trade, Research, Story M4 |
| `sch_biofuel_still` | **New** Biofuel Still building L1–2 (food or wood → Fuel) | 1 | Loot 1+, Trade, Research, Story M6 |
| `sch_cargo_rack` | Vehicle upgrade: Cargo Rack (+cargo home) | 1 | Loot 1+, Trade, Research |
| `sch_motorbike` | Vehicle T2 Motorbike (chassis recipe) | 2 | Loot 2+, Trade, Research, Story M6 |
| `sch_small_engine` | Engine: Small Engine (motorbike, buggy) | 2 | Loot 2+, Trade, Research, Story M6 |
| `sch_muffler` | Vehicle upgrade: Muffler (less noise when arriving and leaving) | 2 | Loot 2+, Trade, Research, Story side "Marrow's Debt" |
| `sch_offroad_tyres` | Vehicle upgrade: Off-road Tyres (ignores rain and mud limits) | 2 | Loot 2+, Trade, Research |
| `sch_buggy` | Vehicle T3 Buggy (chassis recipe) | 2 | Loot 2+, Trade, Research, Story M9 (ally branch) |
| `sch_garage_3` | Garage L3 (heavy chassis; truck and rig builds) | 3 | Loot 3+, Trade, Research, Story M12 |
| `sch_truck` | Vehicle T4 Truck (chassis recipe) | 3 | Loot 3+, Trade, Research, Story M12 |
| `sch_diesel_engine` | Engine: Freight Diesel (truck, rig) | 3 | Loot 3+ (Rail Depot weighted ×3), Research, Story M12 |
| `sch_armor_plates` | Vehicle upgrade: Armor Plates (a parked vehicle is hard cover; less ambush on the road) | 3 | Loot 3+, Kill Warden (5%), Research, Story M13 |
| `sch_armored_rig` | Vehicle T5 Armored Rig (chassis recipe) | 4 | Loot 4, Kill Warden (3%), Story M16 |
| `sch_pulse_rifle` | **New** Pulse Rifle (Armory L3; a hand-carried Warden pulse cannon, area 1) | 4 | Loot 4, Kill Warden (8%), Story M17 |

Check on the rule: every row with a Story source also has at least one non-story source, and for every tier-1 to
tier-3 schematic that includes Research, so a patient player can always get there even with bad luck. Tier-4
schematics cannot be researched; they come from the Core Spire, Wardens or the story's last missions. That makes
the Black Grid worth visiting in Sandbox.

## Where schematics come from

### Drop odds by zone tier

Each cache item, each locker item and each terminal item rolls "is it a schematic?" first. If yes, it picks one from
the schematics allowed in that zone: tier up to the zone's tier, with the zone's own tier weighted ×2. Schematics your
colony already knows are picked at **half weight**, so duplicates happen but do not dominate.

| Zone (tier) | Per cache item | Per locker item | Per terminal item | Expected per typical run* | A run brings one home |
|---|---|---|---|---|---|
| Town Outskirts (1) | 2% | 3% | 25% | 0.25 | about 1 run in 4 |
| Scrapyard Flats (1) | 2% | 3% | 25% | 0.3 | about 1 in 3–4 |
| Suburb Ruins (2) | 4% | 5% | 35% | 0.5 | about 2 in 5 |
| Flooded Mall (2) | 4% | 5% | 35% | 0.5 | about 2 in 5 |
| Rail Depot (3) | 6% | 6% | 45% | 0.8 | about 1 in 2 |
| Network Relay (3) | 6% | 6% | 45% | 1.0 | more than 1 in 2 |
| Core Spire (4) | 8% | 8% | 60% | 1.5 | nearly always |

\* "Typical run" means the item counts from BALANCE.md for a party that survives (Outskirts ~11 items, Suburb ~9) plus
the terminal a hacker usually opens. The terminal is where a hacking kit pays off.

Today's numbers for comparison: blueprints sit in the general loot table at weight 0.3–0.5 and never drop in tier-1
zones, so an early colony sees roughly one every 5–10 runs in Suburb Ruins and none at home. The proposal is more
generous early, because the default set is now the floor and schematics are the main thing that makes a run exciting.

Vehicle schematics follow the same rules. Fuel, wheels, chassis parts, armor plates, fuel cans and spare parts are
ordinary items or resources (loot and recipes), not schematics; only the chassis tiers, engines, upgrades and the
Garage/Still levels are knowledge. The handcart (T0) and the Garage L1 are default so every colony can try carrying more
home from day one.

### Killed enemies

Uses the per-enemy `drops` tables the linked-carbine-balance branch adds (`enemies.json`). Husks drop no schematics
(they are disconnected; there is nothing left in them). The Linked carry Network knowledge:

| Enemy | Schematic drop chance (one roll per kill) | Pool |
|---|---|---|
| Linked | 5% total | Linked Carbine 3%, Linked Plating 2%, Powered limbs 2% (rolled in that order, first hit wins) |
| Linked Drone | 4% total | Network Ghost 2%, Drone Payload 3% |
| Linked Warden | 50% total | Linked Carbine 25%, Linked Plating 15%, Cybernetics 10%, Override 10%, Pulse Rifle 8%, Network-grade 5%, Gun arm 5% |

A dropped schematic lies on the enemy's tile like other loot, so someone still has to pick it up and get out.

### Hacked terminals

A terminal already holds 1–2 items from its own table (`scavenge.json` `devices.Terminal.loot`). The schematic chance in
the table above replaces the fixed blueprint/datashard entries there; the pool for a terminal is the "Term" column of
the catalogue, which leans to Network tech (drones, techniques, the cyberdeck, scanners). Field manuals and electronics
stay in the terminal table as the non-schematic results.

### Trading

**Story mode:** Abel Marrow's caravan (STORY.md) is the trader. **Sandbox:** a nameless caravan with the same rules.

- Unlocks: Story mission M4 (meeting Marrow's scout). Sandbox: you have a Radio Mast and have scouted Suburb Ruins.
- Visits every 10 days (every 5 with Radio Mast L2), stays 2 days, parks outside the colony.
- Stock: 2 schematics you do not know: one of tier ≤ 2 and, from day 60 on, one of tier ≤ 3. Prices:

| Tier | Price (pick one) |
|---|---|
| 1 | 30 scrap, or 25 food + 10 cloth, or 4 Research Data |
| 2 | 40 scrap + 8 electronics, or 30 food + 6 medicine, or 8 Research Data |
| 3 | 60 scrap + 20 electronics, or 15 Research Data |

- The trader also **buys** schematics and Research Data, so duplicates are never worthless.
- In Sandbox there are no tier-4 schematics for sale. In Story mode, Marrow offers one tier-4 schematic once, after M16.

### Research (study)

Research is the guaranteed slow path. It lives at the Town Hall, the building that already teaches skills and perks.

- **Research Data** is a new colony resource. It comes from duplicate schematics (below), from salvaging a schematic
  you do not want, from a few terminal rolls, and as a small mission reward.
- At Town Hall L2 you can research any tier 1–2 schematic; at Town Hall L3 with an Electronics Lab L2, tier 3.
- Cost: Research Data (tier 1: 4, tier 2: 8, tier 3: 16) plus time, worked like a study class by one researcher
  (Intellect and Electronics speed it up): about 3 / 6 / 12 in-game days.
- **You have to know the schematic exists.** A schematic is researchable once you have seen it: in a trader's stock,
  dropped by an enemy you killed but did not collect, on a terminal you failed to hack, or mentioned in a story
  dialogue. The schematics archive lists these as "Heard of". This keeps research from turning into a shopping list
  on day 30, and gives a failed run something: "we know the Rail Depot has rifle plans".

### Story missions

Each main mission names its schematics up front, so the player can pick the mission for the reward they need. Rewards
are guaranteed on success. If the colony already knows a reward (found it earlier), it pays the duplicate value in
Research Data instead, plus a line of dialogue ("Already got that one? Then I owe you").

## Learning a schematic

### Recommendation: learning is automatic, decrypting is a job

Today: pick up a blueprint, bring it home, open the inventory, press Study, which needs a building of a certain level
and happens instantly. The click adds nothing but a gate, and players forget to press it.

Proposal:

1. **Plain schematics** (tier 1–2: paper plans, maker's notes) are **learned the moment the party gets home**, with a
   toast and a log line: "Ines brought home the Hunting Rifle schematic. The Armory can make it at L3." No building is
   needed to learn it; the recipe's own building level is the gate, and the recipe list already shows "needs Armory L3".
2. **Encrypted schematics** (tier 3–4 and every technique: Network chips) come home as an item and need a **Decrypt**
   order at the Electronics Lab: L2 for tier 3, L3 for tier 4. It is a normal production order with work but no
   inputs (work 12 for tier 3, 24 for tier 4), and needs a worker with Electronics or Hacking 20. It uses the existing
   queue, practice and skill-gap code.

Why keep the decrypt step at all: it gives the Electronics Lab and the hacker a job in the colony, it makes Network
loot feel different from a paper plan, and it is the one moment where a story character (Iris) can talk you through
it. Why drop the click for plain schematics: there is no decision in it.

### The Schematics archive

A new tab next to Items (the old "studied blueprints" list). It shows, per unlock kind: **Known**, **To decrypt** (with
a Decrypt button that queues the Lab order), **Heard of** (researchable, with cost and where it was seen) and **Unknown**
(a silhouette and a hint, e.g. "found in the Iron Corridor"). In Story mode, schematics a mission rewards show the
mission name.

## Duplicates

Finding a schematic you already know, or carrying home a second copy of one you are decrypting:

- It becomes **Research Data** automatically when the party gets home: tier 1 → 2, tier 2 → 4, tier 3 → 6, tier 4 → 10.
- In the field it still shows its name, so the player knows what it is, with a "known" tag.
- In co-op and versus, a known schematic can still be **given** to another player (it is an item in your pack until you
  get home). That makes duplicates useful as trade between friends.

Research Data closes the loop: a colony that keeps finding the same rifle plan can turn four of them into the scanner
it actually wants.

## Multiplayer and co-op

- **Schematics belong to a colony.** There is no account-wide unlock. A new colony starts with the default set again.
- **Co-op: recommendation, everyone who gets home gets a copy.** When an allied team extracts, every schematic any
  member carries out is copied to every allied player who extracts alive (their colony learns it, or turns it into
  Research Data). Knowledge is data and data copies; co-op should not turn a find into an argument. It also makes
  "bring a friend along to help me with the Rail Depot" a real favour that pays both.
- **Versus: no copies.** Whoever carries it out has it. It can be killed for and stolen like any other item. A
  schematic in a pack of a downed enemy is a reason to go there.
- A schematic **given** to an ally (Give action) goes home with that ally only, as today.
- The server stays authoritative: copies are made in each player's extraction result, nothing new is trusted from the
  client. Fog: a schematic on the ground is an item like any other (no new leaks).
- Story missions in co-op: see STORY.md "Co-op" (the guest gets the mission's schematic rewards as copies).

## Data and migration

### Data

- New `data/schematics.json`: one entry per schematic with `id`, `name`, `tier`, `encrypted` (bool), `unlocks` (list of
  `{ kind: building | buildingLevel | recipe | prosthetic | perk, id, level? }`), `sources` (`loot`, `terminal`,
  `trade`, `research`, `story`, drop tables reference it from `enemies.json`) and `default` (bool).
- `recipes.json`: `blueprint` becomes `schematic`; default recipes have no field.
- `buildings.json`: per level an optional `schematic`; per building an optional `schematic` for the building itself.
- `perks.json`: `requires.blueprint` becomes `requires.schematic`.
- `items.json`: schematic items become kind `Schematic` with a reference to their `schematics.json` entry; the old
  `blueprint: { studyAt, minLevel }` block goes away (the Decrypt rule replaces it).
- `rules.json`: `schematics` block with the drop odds, duplicate values, research costs and trader prices above.

### Renames

| Old item id | New schematic id |
|---|---|
| `bp_riot_armor` | `sch_riot_armor` |
| `bp_rifle` | `sch_rifle` |
| `bp_scanner` | `sch_scanner` |
| `bp_evac_beacon` | `sch_evac_beacon` |
| `bp_linked_carbine` | `sch_linked_carbine` |
| `bp_linked_plating` | `sch_linked_plating` |
| `bp_signal_jammer` | `sch_signal_jammer` |
| `bp_cybernetics` | `sch_cybernetics` |
| `bp_cyberdeck` | `sch_cyberdeck` |
| `bp_scout_drone` | `sch_scout_drone` |
| `ds_network_ghost` | `sch_network_ghost` |
| `ds_override` | `sch_override` |
| `ds_drone_payload` | `sch_drone_payload` |

### Content that becomes gated

Three recipes are free today and move behind a schematic, because the early tiers otherwise had nothing to find:
Camo Cloak (`sch_camo_cloak`), Grappling Hook (`sch_grappling_hook`) and Servo Arm (into `sch_powered_limbs`). The L3 of
Workshop, Tailor, Armory, Electronics Lab and Med Bay also become gated. The Rifle, Linked Carbine and Linked Plating
recipes already need Armory L3, so their schematic plus `sch_armory_3` is a real two-step climb; that is intended for
the Iron Corridor tier.

### Old saves (next free colony save version)

- `KnownBlueprints` becomes `KnownSchematics`, with each id renamed by the table above.
- Old blueprint items in the colony inventory and in a mid-mission scavenge state are renamed. Old items whose
  schematic is already known turn into Research Data on load.
- **Nobody loses anything they could do before.** A loaded save is granted every newly gated schematic it was already
  using: Camo Cloak and Grappling Hook if Tailor/Workshop is at L2, Powered limbs if it owns or has fitted a Servo Arm,
  and the L3 schematic for every building already at L3.
- The save remembers it was migrated so the bot and tests can tell migrated colonies apart.

### Balance work to do

- Rerun the 20-seed × 60-day sweep: Town Hall 2, Armory and Lab days should not move (no early gate changed).
- New measures for the bot: schematics found per colony by day 30 / 60 / 90, duplicates turned into Research Data,
  days until the first tier-2 schematic. Target: the first schematic by about day 10, the first tier-2 one by about
  day 35, without the story.
- Zone trials: count schematics per run per zone and compare with the table above.

## As built (phases 1–2)

Core lives in `src/Remnants.Core/Schematics/` (self-contained); ARCHITECTURE.md "Schematics" maps it. Where the build
differs from the design above:

- **One source of gating.** What a schematic unlocks is written only in `data/schematics.json` (`unlocks`); recipes,
  buildings, perks and the cybernetics tree carry no `schematic`/`blueprint` field. Core indexes the catalogue the other
  way (`SchematicCatalog.GateOf…`). Anything no schematic unlocks is in the default set.
- **Rules in the schematics file.** The drop odds, duplicate values, research costs, decrypt rules, trader prices,
  contracts, copy cost and trade values are the `rules` block of `schematics.json`, not `rules.json`, so the module
  stays in one file. Features add their own entries in `data/schematics.<feature>.json` (only `schematics` and
  `traders`; merged in name order; a reused id is an error).
- **Find tier = zone threat** (Core Spire is tier 4, although its item loot tier is 3).
- **Cybernetics** (merged from prosthetics-tree): prosthetic unlocks are `tier:N` and `spec:ID` of
  `cybernetics.json`. A design needs its tier's schematic and, for a specialisation, its spec's schematic, on top of
  the tree's own requirements. `sch_cybernetics` (was `bp_cybernetics`) and `sch_network_grade` (was
  `bp_network_grade`, also gates the Linked Salvage perk) keep the old "skips the tier below" rule (tiers with
  `schematicSkips`). T0 Crude is the default; T1 needs `sch_mech_limbs`, T2 `sch_powered_limbs`.
- **Planned entries.** Vehicles, the Garage and the Biofuel Still are not in the game yet; their schematics are in the
  catalogue as `planned` (shown as "coming" in the archive) and never drop, sell or research. The Watchtower and the
  Greenhouse are built as simple buildings (the Watchtower has no effect yet; the Greenhouse is an indoor farm). The
  Pulse Rifle is an Armory L3 recipe (2 Linked Cores).
- **Enemy drops.** A kill rolls its schematic drops in order and only the first hit drops; a known schematic drops at
  `knownWeight` × its chance.
- **Heard of** comes from the trader's stock, a schematic left lying on a tile the party saw, a terminal it failed to
  hack (the side glimpses what the terminal held), and `Schematics.HearOf` (story dialogue).
- **Research** takes one researcher (off their job) on their day shift; factor `0.4 + Intellect·0.006 +
  Electronics·0.006`, work 42 / 84 / 168 (about 3 / 6 / 12 days for an average researcher). Tier 3 needs Town Hall 3 and
  an Electronics Lab L2.
- **Decrypt** is a synthesized Electronics Lab recipe per encrypted schematic (`decrypt:<id>`, its chip as the one
  input), at the existing queue. The minimum is Electronics 20 (Hacking does not count).
- **Trader.** Sandbox: "A passing caravan" (`traders` in the data), visiting once the colony has a Radio Mast and has
  scouted Suburb Ruins; Story mode swaps its trader in with `RadioMast.SetTrader`. Before day 60 the stock is two tier
  ≤ 2 schematics; from day 60 one of them is tier 3. Tier-4 prices exist for a story trader (`maxTier` 4).
- **Radio contracts:** one offer every 6 days (a call): bring a party home from a known zone (threat ≤ 2 at Radio Mast
  L1, any at L2) within 8 days, for Research Data plus scrap or electronics.
- **Copies.** `CopySchematic` makes a tradeable item copy of a known schematic for Research Data (`copyCost` 1 / 2 / 3 /
  5), for trade between players; the receiving colony learns it the usual way. `TradeValues` gives reference values in
  scrap from the price table.
- **Saves:** colony version 13 (`ColonyState.Schematics`, `ColonyState.Radio`); protocol 7 (the extraction result's
  co-op copies and heard-of list).

