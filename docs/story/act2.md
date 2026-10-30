# Act 2 — Ashgrove

Days ~30–65. Zones: Scrapyard Flats, Suburb Ruins, Flooded Mall. The colony gets a doctor, a road and a rival. The
Linked stop being a rumour and start being a voice.

Act card (shown when M5 is done):

> **ACT 2 — ASHGROVE**
> *The radio reaches further now. So does everything listening to it.*

---

## M6 The Long Haul

### Mission card

| Field | Value |
|---|---|
| Type | escort (field) |
| Zone / map | `scrapyard_flats`, `generated` with the **road** template (long and narrow, 40×14, spawn West, exit East). Needs a road template in `zones.json` (`templates.road`); until then the normal Flats map with the convoy start at the spawn edge and `far_edge` = the exit tiles. |
| Threat | 2 · party up to 4 |
| Needs | `mission m4 done`; flag `marrow_visits ≥ 2` (counted by the caravan visit) |
| Needs text | "Marrow's caravan has visited twice." |
| Briefing | Marrow needs guns on his convoy through the Ruins road. |
| Enemy mix | start: 3 `husk` on the verge (idle, wake on noise). Round 6 ambush: 1 `linked` (tag `ambush`) at `ambush_n`, 2 `linked_drone` (tag `ambush`) at `ambush_s`. Retry (if wiped): +1 `linked`. |

Allied units (NPCs, party side after the start trigger): `marrow` (character `marrow`, rides `truck_people`),
`truck_people` and `truck_cargo` (vehicle NPCs, 60 HP, cover 0.3 to adjacent units, move 3 tiles a round toward
`far_edge` — **needs NPC `pathTo` behaviour**, see README), `dee` (character `dee`, rides `truck_cargo`, never dies in
M6: she is in the cab).

Objectives:

| id | kind | target | flags |
|---|---|---|---|
| `o_people` | Escort | tag `truck_people` | optional |
| `o_cargo` | Escort | tag `truck_cargo` | optional |
| `o_marrow` | Escort | tag `marrow` | optional |
| `o_ambush` | KillTag | tag `ambush`, count 3 | hidden, optional |
| `o_out` | Reach | marker `far_edge`, radius 2 | — |

Triggers:

| id | when | actions |
|---|---|---|
| `t_start` | Start | SetSide `convoy` → party; Say marrow "Stay off the hoods. Stay near the wheels."; Prompt `m6_open_field` (none: the opening is a scene at home) |
| `t_r2` | Round 2 | Say marrow "Freight road. Used to be eight lanes. Now it's two and a half." |
| `t_r4` | Round 4 | Say iris (radio) "I've got Network chatter on your road. Could be nothing. It's never nothing." |
| `t_ambush` | Round 6 | Spawn 1 `linked` at `ambush_n`, tag `ambush`, goal `truck_cargo`; Spawn 2 `linked_drone` at `ambush_s`, tag `ambush`, goal `truck_people`; Activate `o_ambush`; Prompt `m6_split` |
| `t_people` | Chosen `m6_split:people` | Say marrow "Good call. Cargo's insured. By nobody. Go." ; Flag `cover` = 1 |
| `t_cargo` | Chosen `m6_split:cargo` | Say marrow "Fuel's worth more than me today. Fine. Fine!"; Flag `cover` = 2 |
| `t_both` | Chosen `m6_split:both` | Say marrow "Ha! Greedy. I like greedy. Don't die of it."; Flag `cover` = 3 |
| `t_marrow_down` | ObjectiveFailed `o_marrow` | Say dee "Abel's down. Abel's — keep driving. KEEP DRIVING."; Flag `marrow_dead` = 1 |
| `t_cleared` | ObjectiveDone `o_ambush` | Say marrow "Count the people. Then count the cargo. In that order. Always that order." |

Outcome (read at home): people truck out → bit 1; cargo truck out → bit 2; `m6_saved` = the sum (0 counts as 2:
the cargo is salvaged from the wreck). Marrow out alive → `marrow_alive` = 1, else 0 and Dee runs the caravan.

Fails if: the party wipes (retry in 4 days; the convoy waits at the Flats, now with 2 more Husks on the road).
Marrow dying is **not** a fail (STORY.md): the mission completes at the cargo outcome.

Rewards (on return):

| effect | when |
|---|---|
| `schematic sch_motorbike`, `sch_small_engine`, `sch_garage_2`, `sch_biofuel_still` | always (vehicles not merged: `fallback "Marrow's notes are in the Workshop until you have a Garage."`, plus `resource fuel` → `item scrap ×10` stand-in) |
| `schematic sch_rifle` | always |
| `item fuel_can ×4` (fallback `item scrap ×8`) | always |
| `set marrow_price_mod = −10` | `m6_saved` 1 or 3 |
| `item linked_carbine ×3`, `item electronics ×6` | `m6_saved` 2 or 3 |
| `set m6_saved`, `set marrow_alive` | always |

### Scenes

#### `m6_open` — "Ride shotgun" (in-person, colony gate, morning)

Speakers: marrow, dee, player, pell. Mode: in-person.

> *(The Long Haul idles outside the gate: three trucks, patched, one with a bedframe welded on as a plough.)*

- MARROW (warm): Morning. Don't get up. I mean that, I'm selling, not asking.
- MARROW (wry): Actually I'm asking. Road through the Ruins. I need guns on the wheels.
- DEE (cold): We lost two outriders this month. Juniper's still limping.
- MARROW (warm): Dee thinks I'm too nice. Dee's right. Pay's good though.
- PELL (neutral): Is that a bed on the front?
- MARROW (wry): Plough, kid. Bed was its first job. Everything has a second job now.

Choices:

1. "What's the cargo?" → MARROW (wry): "Fuel, seed, a crate of medicine for a place that's still there. And people. Six of them." → node `m6_open_go`
2. "What's in it for us?" → MARROW (warm): "Something you can't buy. I'll teach you to build a bike. Two wheels, a prayer and a muffler." → `m6_open_go`
3. "Why not go around?" → DEE (cold): "There is no around." → `m6_open_go`

`m6_open_go`:
- MARROW (resolved): We roll at noon. Stay near the wheels. Wheels are cover. Hoods are targets.
- *(Effects: start mission M6 available on the map; `character marrow met`, `character dee met`.)*

#### `m6_split` — "They split up" (field prompt, pausing)

Speaker: dee (radio, from the cargo cab). Mode: in-person (field prompt).

- DEE (afraid): Drones on the people truck. Something big on mine. We're splitting — we have to!
- MARROW (hurt): Don't argue with her. Pick one. Pick one NOW.

Choices (prompt options):

1. `people` — "Cover the people." · hint: "The cargo truck is on its own."
2. `cargo` — "Cover the cargo." · hint: "Marrow's truck is on its own."
3. `both` — "We split too." · hint: "Thin, but nobody's alone." (always available; a strong party can save both)

#### `m6_close` — "In that order" (in-person, colony, evening)

Variants by outcome. Speakers: marrow or dee, player.

**People saved (1), Marrow alive:**
- MARROW (tired): Six people. Six. You know what they're worth on the market? Nothing. Good.
- MARROW (warm): Lost the fuel. Lost the seed. I'll find more. I can't find more of them.
- MARROW (resolved): You get Long Haul prices from now on. Family prices. Don't tell anyone, they'll all want to be family.
- MARROW (wry): And the bike. Sit down. Two wheels, a prayer and a muffler.

**Cargo saved (2), Marrow alive:**
- MARROW (cold): We got the fuel through.
- MARROW (grief): Two of the six didn't. Hannah and the boy.
- MARROW (cold): You did what I asked. I asked wrong.
- MARROW (tired): Count the people. Then count the cargo. In that order. ...I'll be saying that to myself for a while.
- *(Effects: three Linked carbines from the ambush, crates of electronics. Marrow keeps his word on the bike.)*

**Both saved (3):**
- MARROW (warm): Everybody. Every crate. You know how often that happens? I'll tell you. Today. Once.
- DEE (wry): Don't let it go to his head. It's already gone to his head.

**Marrow dead (any outcome):**
- DEE (cold): He was in the cab. He said he'd get out. He always gets out.
- DEE (grief): ...
- DEE (cold): Long Haul's mine now. Prices go up. The road doesn't care, so I won't either.
- DEE (resolved): He wanted you to have the bike plans. Here. He drew the muffler himself. Badly.
- *(Effects: `marrow_alive = 0`. From now on `{name:marrow}` is Dee, prices +10% for 20 days.)*

---

## M7 The Drowned Clinic

### Mission card

| Field | Value |
|---|---|
| Type | survive, then escort |
| Zone / map | `flooded_mall`, `handmade:m7_drowned_pharmacy` (30×26, water) |
| Threat | 2 · party up to 4 |
| Needs | `mission m4 done`; `building med_bay ≥ 1` |
| Needs text | "A Med Bay." |
| Briefing | Doc Hale is mid-surgery in the Mall pharmacy. The water is coming in. So are the Husks. |
| Enemy mix | placed: 2 `husk` in the food court (patrol `fc_a`→`fc_b`), 1 `husk_crawler` in the flooded hall. Waves while holding: round +1: 3 `husk` at `wave_w`; +3: 2 `husk` + 1 `husk_crawler` at `wave_n`; +5: 3 `husk` at `wave_w` and `wave_n`. Retry: +1 `husk_crawler` per wave. |

NPCs: `hale` (character `hale`, at `doc`, holds, never moves until `o_hold` is done), `patient` (Tomas,
male, 35 HP, `NoRecruit = false`, at `table`).

Objectives:

| id | kind | target | flags |
|---|---|---|---|
| `o_reach` | Reach | marker `clinic`, radius 2 | — |
| `o_hold` | Survive | count 6 | hidden |
| `o_doc` | Escort | tag `doc` | hidden, failsMission |
| `o_patient` | Escort | tag `patient` | hidden, optional |
| `o_doc_alive` | Defend | tag `doc` | failsMission |

Triggers:

| id | when | actions |
|---|---|---|
| `t_r1` | Round 1 | Say iris (radio) "Mall's half underwater. Water slows you. It slows them too. Mostly." |
| `t_door` | Enter `clinic_door` r1 | Say hale "Shut the door. No — leave it. I need the light. Shut the OTHER door." |
| `t_reach` | ObjectiveDone `o_reach` | Prompt `m7_clinic` |
| `t_hold` | Chosen `m7_clinic:*` (any) | Activate `o_hold`; Spawn 3 `husk` at `wave_w` goal `doc` |
| `t_wave2` | Round hold+3 (a `Round` trigger set from the hold start — **needs relative rounds**, see note) | Spawn 2 `husk`, 1 `husk_crawler` at `wave_n` goal `doc`; Say hale "Halfway. Don't look. I said don't look." |
| `t_wave3` | Round hold+5 | Spawn 3 `husk` at `wave_w`, 3 at `wave_n`; Say hale "Closing. Somebody find me a clean towel. Clean-ish." |
| `t_done` | ObjectiveDone `o_hold` | Activate `o_doc`, `o_patient`; SetSide `doc`, `patient` → party; Follow `doc`, `patient`; Say hale "Done. He'll live if you carry him. Carry him." |
| `t_aid` | Chosen `m7_clinic:help` | Flag `helped_doc` = 1 |

Note: the framework fires `Round` triggers on absolute rounds. Relative waves ("3 rounds after `o_hold` starts") need
either a `delay` on a trigger or chained `ObjectiveDone` on sub-survive objectives. Draft uses three chained `Survive`
objectives (`o_hold_1` 1 round, `o_hold_2` 2, `o_hold_3` 3) behind `o_hold` if no delay field is added.

Fails if: Doc dies (`o_doc_alive` fails). Then: rewards are still paid from her notes (found at `doc`), `doc_state = 2`,
she does not join, and M15's prerequisite changes to `building med_bay ≥ 2` + a survivor with Medicine ≥ 30. No retry.
Party wipe: retry in 3 days, the clinic sealed (Doc held out alone; she is weaker: 50 HP).

Rewards:

| effect | when |
|---|---|
| `character hale joins` (unique, see survivors.md) | Doc alive |
| `schematic sch_mech_limbs`, `schematic sch_powered_limbs` | always |
| `item med_supplies ×6` | always |
| `character recruit "Tomas"` with a fitted `peg_leg` | patient out alive |
| `set doc_state = 1` / `= 2` | alive / dead |
| `call m7_home +6h` | Doc joined |

### Scenes

#### `m7_open` — "Clinic's open" (radio, colony, day)

Speakers: iris, unknown (hale, no portrait yet: a hiss). Mode: radio.

- IRIS (neutral): I've got someone. Mall frequency. They've been on loop for three days.
- HALE (tired) *(recorded, crackle)*: —clinic's open. Mall, pharmacy wing, south stairs. Bring your own bandages, we're out. Clinic's open—
- IRIS (wry): Loop's changed today. Listen.
- HALE (afraid) *(live)*: —water's up to the second shelf. If anyone's out there I have a man on the table and I am NOT stopping.
- IRIS (resolved): That's live. That's now.

Choices:
1. "We're going." → IRIS (warm): "Of course you are. Pharmacy wing. Bring a med-kit for him and patience for her."
2. "Is the Mall safe?" → IRIS (wry): "It's a mall full of water and dead people. Define safe." → same end.

#### `m7_clinic` — "I am not stopping" (field prompt, at the clinic)

Speakers: hale, player. Mode: in-person (pausing).

- HALE (angry): You're late. Everyone's late. Hold that lamp.
- PLAYER: We need to go. Now.
- HALE (cold): He has a leg that's half gone and a leg that's half mine. Six minutes. Hold the door for six minutes.
- HALE (tired): Then I'll go anywhere you like.

Choices:
1. `hold` — "Six minutes. Go." · effects: none.
2. `help` — "Tell me what to do." *(requires: a party member with Medicine ≥ 20 — framework: prompt option `requires`)* →
   HALE (wry): "Hold still. You'll scream either way, but if you hold still you'll keep the leg. ...That was for him. You just hand me things." · effects: field flag `helped_doc`; the hold counts down one round faster (`o_hold_3` count 2).
3. `why` — "Is he worth it?" → HALE (cold): "Ask me that again and you can hold the saw." · hint: "Doc will remember this." · effects: field flag `doc_cold`.

#### `m7_close` — "A leg is a leg" (in-person, colony Med Bay, night)

Speakers: hale, player, pell. Mode: in-person.

- HALE (tired): Nice Med Bay. Who built it, a carpenter?
- PLAYER: Yes.
- HALE (wry): Shows. I'll fix it.
- PELL (afraid): Is his leg gone?
- HALE (warm): Half of it. The other half's wood now. Wood's fine. Wood doesn't get infected.
- HALE (resolved): Here's my rule, so nobody's surprised. A leg is a leg. Wood, steel, carbon, I don't care.
- HALE (cold): The head is where the person is. Nobody touches the head. Not you. Not them. Not me.

Variant `doc_cold` set: before the rule —
- HALE (cold): You asked if he was worth it. Don't ever ask me that about anyone in this colony.

Choices:
1. "Stay with us." → HALE (tired): "I wasn't asking. I'm staying. Your bandages are a disgrace." (joins)
2. "What do you need?" → HALE (neutral): "Sleep. Then a better Med Bay. Then a hospital, but let's not get ahead of ourselves." (joins)

#### `m7_home` — "First fitting" (in-person, colony Med Bay, 6 hours later)

Speakers: hale, `{survivor:most_injured}` if anyone has a lost limb, else narration.

- HALE (neutral): {survivor:most_injured}. Sit. You've been walking on that like it owes you money.
- HALE (warm): Mechanical. Springs and a socket. No power, no upkeep, no nonsense.
- HALE (wry): That arm's better than the one you were born with. Don't tell your mother.
- *(Opens a free T1 fitting order for that survivor, if any; teaches prosthetics.)*

---

## M8 Eyes Up

### Mission card

| Field | Value |
|---|---|
| Type | retrieve, then a world-map drone flight |
| Zone / map | `flooded_mall`, `generated` + room `m8_atrium` |
| Threat | 2 · party up to 4 |
| Needs | (`mission m7 done` or `day ≥ 45`) and `building electronics_lab ≥ 1` |
| Briefing | A scout drone went down in the Mall atrium, under the Linked drone's circuit. |
| Enemy mix | zone generation as normal (Linked weight as zone); the room adds 1 `linked_drone` (tag `circuit`, patrol `c1`→`c2`→`c3`→`c4`), 1 `camera` at `cam` (device), 1 `husk` at `fountain`. |

Objectives:

| id | kind | target | flags |
|---|---|---|---|
| `o_core` | Retrieve | item `drone_core` (**new item**, 1 slot, 6 kg) | failsMission if destroyed (see note) |
| `o_camera` | Hack | tag `cam` | optional |

Note: "destroyed while carried" — a carrier going down drops the core; it is destroyed only by an AoE on its tile.
Framework: `Retrieve` fails if the item stops existing (needs an item-destroyed check; flagged).

Triggers:

| id | when | actions |
|---|---|---|
| `t_r1` | Round 1 | Say iris "Atrium's under a drone circuit. Four corners, round and round. Count it." |
| `t_cam` | Sees tag `cam` | Say iris "Camera. Don't hack the camera. Hack the thing the camera reports to. ...Kidding, there's nothing up there. Hack the camera." |
| `t_core` | PickedUp `drone_core` | Say iris "That's it. That's my baby. Don't drop her. She's very old and very rude." |
| `t_alarm` | Alarm | Say iris "That was loud. Drone's going to come look. Be somewhere else." ; Wake `circuit` goal `core` |

Rewards: `schematic sch_scout_drone`, `schematic sch_scanner`, `item scout_drone ×1`, then `call m8_flight +2h`.
The flight scene reveals `rail_depot` (Scouted). If the colony has no drone pilot, the scene flies it anyway (Iris).

Fails if: the core is destroyed (retry 3 days, "the drone's wreck has a second core, smaller, in the parking deck").

### Scenes

#### `m8_open` — "Eyes on the Depot" (radio)

Speakers: iris, pell. Mode: radio.

- IRIS (neutral): I want to see the Rail Depot. I can't. Nobody's been in or out for two years.
- PELL (neutral): Can't you just look?
- IRIS (wry): With what, kid, my eyes? I live in a hole.
- IRIS (neutral): I had a drone. It went down in the Mall atrium. Bring me its core and I'll give you its brothers.
- PELL (warm): Drones! Can I fly one?
- IRIS (warm): ...If your people say yes. And if you don't crash it into a Husk on purpose.

#### `m8_flight` — "Look down" (radio, colony, world map)

Speakers: iris, pell, narration. Mode: radio, then the world map pans to the Depot.

- IRIS (neutral): Core's talking. Sending her up. Watch the map.
- NARRATION: *(The scout drone crosses the Ruins and the Mall. The Rail Depot fogs out of the dark: sheds, a yard, a
  pylon blinking cyan.)*
- PELL (afraid): The pylon's on. Iris, the pylon's on.
- IRIS (cold): ...Yeah. I see it.
- IRIS (quiet): That's my yard, kid. I'm under it.
- *(Effects: `reveal rail_depot scouted`; `set depot_seen = 1`; Iris's next call is M11's opener once M11 unlocks.)*

Choices (Pell's line only plays if Pell is alive; otherwise `{player}` says "That pylon's on."):
1. "You live under the Depot?" → IRIS (ashamed): "Long story. Short version: yes. Shorter version: later."
2. "Then we come get you." → IRIS (wry): "Let's not get sentimental. Get the decrypt queue going first. I'll walk you through it."

---

## M9 The Tally

### Mission card

| Field | Value |
|---|---|
| Type | retrieve, three ways |
| Zone / map | `suburb_ruins`, `generated` + room `m9_armoury` (the bunker: door, vent, two halls, vault) |
| Threat | 2 · party up to 4 |
| Needs | `mission m3 done`, `mission m6 done` |
| Briefing | A sealed pre-war armoury has opened. The Tally got there first. Cole is waiting at the door. |
| Enemy mix | room: 4 `husk` inside (tag `vault_husk`, idle at `hall_a`, `hall_b`), 1 `husk_crawler` at `vault`. The Tally (neutral side at start, tag `tally`): `cole` at `door` (named "Cole", `noKill`), 5 at `tally_line` (4 `tally_scav`, 1 `tally_blade` (**new**: machete, see survivors.md)). |

Objectives:

| id | kind | target | flags |
|---|---|---|---|
| `o_vault` | Reach | marker `vault`, radius 1 | — |
| `o_husks` | KillTag | tag `vault_husk`, count 5 | hidden (Share) |
| `o_tally` | KillTag | tag `tally`, count 5 | hidden (Take; Cole escapes and does not count) |
| `o_vent` | Undetected | — | hidden (Slip in) |

Triggers:

| id | when | actions |
|---|---|---|
| `t_sees` | Sees tag `tally` | Prompt `m9_door` |
| `t_share` | Chosen `m9_door:share` | SetSide `tally` → party (alliance); Activate `o_husks`; Flag `share` = 1; Say cole "Front room's yours. Back room's ours. Middle's whoever's still standing." |
| `t_take` | Chosen `m9_door:take` | SetSide `tally` → hostile; Activate `o_tally`; Flag `take` = 1; Say cole "Wrong answer. I'm writing it down." |
| `t_slip` | Chosen `m9_door:slip` | Activate `o_vent`; Flag `slip` = 1; Say cole "Suit yourself. Door's ours." |
| `t_seen` | ObjectiveFailed `o_vent` | SetSide `tally` → hostile; Activate `o_tally`; Flag `take` = 1; Say cole "There you are. Vents. Cute." |
| `t_cole_hurt` | Cole below 50% HP (**needs an HP trigger**; until then: Round 4 after `t_take`) | Say cole "That's one you owe me. I write it down."; Cole leaves by the nearest exit (noKill) |
| `t_vault` | ObjectiveDone `o_vault` | Split `vault_locker` if `share`; Say cole (share) "Half. Count it. I did." |

Outcome flags home: `share` → `cole_respect += 2`, `tally_trader = 1`; `take` → `cole_respect −= 3`; `slip` (not seen) →
`cole_respect −= 1`, `cole_grudge = 1`.

Fails if: the party wipes (retry 4 days; the Tally have the bunker and it is "Take" only).

Rewards (all at home):

| effect | when |
|---|---|
| `schematic sch_riot_armor`, `schematic sch_evac_beacon` | always |
| `schematic sch_buggy` (vehicles not merged: fallback `"Cole's chassis plan waits for a Garage."`) | share |
| vault locker items: `riot_armor ×2`, `rifle ×1` | take (full) |
| half the locker (`riot_armor ×1`) | slip, share (the split) |

### Scenes

#### `m9_open` — "The door's open" (radio)

Speakers: iris, player.

- IRIS (neutral): Something popped in the Ruins last night. Old civil-defence armoury. Thirty years sealed.
- IRIS (wry): Somebody had a code. Or a crowbar and a lot of patience.
- IRIS (cold): Tally colours on the door already. Your friend Cole.
- PLAYER: He's not our friend.
- IRIS (neutral): Then he's your neighbour. Worse.

#### `m9_door` — "Cole at the door" (field prompt)

Speakers: cole, player. Variants by `cole_respect` (from M3).

Opening, `cole_respect ≥ 1`:
- COLE (wry): Look who it is. Terminal-splitter. I've got you down as fair.
- COLE (neutral): Bunker's full of Husks. My people are tired. Yours look fresh.

Opening, `cole_respect = 0`:
- COLE (neutral): You left last time. Didn't fight, didn't talk. I've got you down as a question mark.

Opening, `cole_respect ≤ −1`:
- COLE (cold): You shot at my people over a terminal. I've got you down. In ink.

Then:
- COLE (neutral): Here's the offer. We clear it together. We split it down the middle. I count, you watch me count.
- COLE (cold): Or you can try the other thing.

Choices:
1. `share` — "Together. Half each." · hint: "Cole will remember this."
2. `take` — "It's ours. Walk away." · hint: "This is a fight."
3. `slip` — "*(nod, then go round the back)*" · hint: "If they see you, it's a fight." *(requires: none; the vent is
   marked once the prompt closes)*

#### `m9_close` — variants (in-person, at home, or radio)

**Share:**
- COLE (radio, wry): Tally here. First time I've called anyone. Don't make it weird.
- COLE (neutral): I've got a spare chassis plan. Buggy. You've got a Garage, or you will. Trade?
- COLE (warm): We'll come by. We trade fair. I write everything down, so you'll know it's fair.
- *(Effects: `sch_buggy`; Tally trade visits every 15 days.)*

**Take:**
- IRIS (radio, tired): Tally's moved camp. Further from you. I heard a man on their band reading out names.
- IRIS (quiet): Twelve names. He paused after each one.

**Slip in, unseen:**
- COLE (radio, cold): Vent was a nice touch. The half you took was the half I'd have given you.
- COLE (cold): Next time, ask. That's all. You'd be amazed what people give you if you ask.

---

## M10 Ghost Signal

### Mission card

| Field | Value |
|---|---|
| Type | stealth / capture, three outcomes |
| Zone / map | `flooded_mall` or `suburb_ruins` (whichever has lower depletion; ties → `flooded_mall`), `generated` + room `m10_store` (a flooded shoe shop) |
| Threat | 2 · party up to 4 |
| Needs | `mission m8 done` |
| Briefing | A Linked transponder broadcasting nothing. A Linked unit sitting in a flooded store, not moving. |
| Enemy mix | zone as normal; the room: `seven` (a `linked` unit, tag `seven`, named "L-0707", `Side` neutral, never attacks, `NoRecruit`). Capture: on carrying, round +3: 2 `linked` at the nearest edge, tag `hunt`, goal `seven`. |

Objectives:

| id | kind | target | flags |
|---|---|---|---|
| `o_find` | Reach | marker `store`, radius 2 | — |
| `o_capture` | Escort | tag `seven` | hidden (Capture) |
| `o_hack` | Hack | tag `seven` (the unit is hackable with Iris's code: 80% flat, no Override) — **needs unit hacking for story units** | hidden (Capture) |
| `o_destroy` | KillTag | tag `seven`, count 1 | hidden (Destroy) |

Triggers:

| id | when | actions |
|---|---|---|
| `t_sees` | Sees tag `seven` | Say seven "What day is it?" ; Prompt `m10_store` (after the first party unit gets within 3 tiles: `Enter store r3`) |
| `t_capture` | Chosen `m10_store:capture` | Activate `o_hack`; Say iris "Use my code. It won't fight it. I don't think it'll fight it." |
| `t_hacked` | ObjectiveDone `o_hack` | Activate `o_capture`; Follow `seven`; Say seven "Oh. That is quiet. Thank you." |
| `t_hunt` | Round (hack done + 3) | Spawn 2 `linked` at `hunt_edge` tag `hunt`, goal `seven`; Say iris "Two Linked inbound. They want it back. Don't let them have it back." |
| `t_free` | Chosen `m10_store:free` | Say seven "Then I will go. Here. You might need this more."; Flag `freed` = 1; Complete `o_find`; (Seven leaves a `linked_core` at its tile and walks to an exit) |
| `t_destroy` | Chosen `m10_store:destroy` | Activate `o_destroy`; SetSide `seven` → hostile (it still never attacks) |
| `t_dead` | ObjectiveDone `o_destroy` | Say seven "Is it — is it night now?" ; Flag `destroyed` = 1 |

Fails if: never. Leaving without choosing leaves the mission on the map. Capture with Seven killed on the way (by the
hunters) counts as Destroy (without the player's hand: `seven_fate = 3`, `seven_killed_by = linked`).

Rewards:

| effect | when |
|---|---|
| `schematic sch_network_ghost` | always |
| `set seven_fate = 1`; Seven to quarantine (Lab), `seven_joined` 7 days later | captured |
| `set seven_fate = 2`; `item linked_core ×1` | freed |
| `set seven_fate = 3`; `item linked_plating ×1`; `research 6` | destroyed |

### Scenes

#### `m10_open` — "Nothing on the line" (radio)

Speakers: iris, pell.

- IRIS (neutral): This is going to sound strange.
- IRIS (neutral): There's a Linked transponder in the Mall. It's on. It's broadcasting. It's broadcasting nothing.
- PELL (neutral): Nothing like quiet?
- IRIS (afraid): Nothing like an empty room. Linked don't do empty. They're never alone in there.
- IRIS (resolved): Go look. Don't shoot first. ...I can't believe I'm saying that.

#### `m10_store` — "What day is it?" (field prompt)

Speakers: seven (L-0707), player, iris (radio).

- SEVEN (neutral): What day is it?
- PLAYER: ...Why?
- SEVEN (neutral): I have been counting. I lost count at four hundred and eleven. Then I started again. That was wrong.
- SEVEN (dark): You are bleeding. I am told to help. I am not told by anyone. That is new.
- IRIS (afraid): It's not linked. It's not a Husk. It's — I don't have a word for it.
- SEVEN (neutral): I have a memory of a kitchen. Yellow tiles. Someone singing. Do you know whose kitchen this is?

Choices:
1. `capture` — "Come with us." · hint: "The Linked will want it back."
2. `free` — "Go. You're free." · hint: "We won't see it again. Probably."
3. `destroy` — "*(raise your weapon)*" · hint: "It won't fight back."

On `destroy`, before the action:
- SEVEN (neutral): Oh. All right. Will you tell someone about the kitchen?

#### `m10_close` — variants (radio / in-person)

**Captured (in-person, the Lab, night):**
- IRIS (radio, neutral): Lock the Lab. Not because it'll hurt anyone. Because I don't know that it won't.
- SEVEN (neutral): There is a window. There is a child at the window.
- PELL (afraid): It's looking at me.
- SEVEN (warm): Hello. You are small. I was small. I think. One of me was.
- *(Effects: quarantine week; the colony's mood −5 near the Lab for that week.)*

**Freed (radio):**
- IRIS (tired): It walked out the north side. Didn't hurry. Stopped once to look at a bird.
- IRIS (quiet): I don't know if we did a good thing. I think we did a thing it asked for. That's rarer.

**Destroyed (radio):**
- IRIS (cold): Transponder's off. Room's empty now. Properly empty.
- IRIS (tired): Bring the plating home. It's good plating. ...I'm going off-air for a bit.

---

## Act 2 closing card

Shown when M8, M9 and M10 are all done (in any outcome) and day ≥ 60:

> **END OF ACT 2**
> *{colony.population} people. {name:marrow}'s convoy on the road. A doctor who will not touch the head.*
> *Somewhere under the Rail Depot, a voice you trust is hiding something.*
