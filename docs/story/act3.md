# Act 3 — Iron Corridor

Days ~65–110. Zones: Rail Depot, Network Relay. The colony goes to Halcyon's doorstep, learns what Iris did, and
Halcyon learns the colony's name.

Act card (shown when the first Act 3 mission unlocks; this is also when `cole_path` is set: `cole_respect ≥ 2` → 1
ally, `≤ −2` → 2 enemy, else 3 neutral):

> **ACT 3 — IRON CORRIDOR**
> *The rails still run. Nobody drives them.*

---

## M11 Lantern

### Mission card

| Field | Value |
|---|---|
| Type | stealth-only, then a choice, then (maybe) an escort |
| Zone / map | `rail_depot`, `handmade:m11_depot_sheds` (30×30) |
| Threat | 3 · party up to 3 (a quiet party; the card says so) |
| Needs | `mission m8`; `townHall 2`; `building electronics_lab ≥ 1` |
| Briefing | `m11_brief` (radio) · Debrief `m11_debrief_home` / `m11_debrief_left` |
| Enemy mix | 2 `linked` on the hatch (tag `hatch_guard`, idle at `guard`); 2 `linked` patrolling the sheds (tag `patrol`, patrol `pa1`→`pa2`→`pa3`→`pa4`); 2 `camera` devices (`cam_n`, `cam_s`); the pylon `P` by the hatch; a `Relay` device at `node` (tag `node`). Alarm (seen or hatch opened with Iris): 3 `linked` + 1 `linked_drone` at `yard_gate`, goal `hatch`. |

Objectives:

| id | kind | target | flags |
|---|---|---|---|
| `quiet` | Undetected | — | — (seen is not a fail; see triggers) |
| `reach_hatch` | Reach | marker `hatch`, radius 1 | — |
| `hack_hatch` | Hack | tag `hatch` (a `Door` device, difficulty high, Iris +15% — **needs a per-device `bonus`**) | hidden |
| `logs` | Hack | tags `log_1`..`log_5` (five `Terminal` devices inside; one objective each, all optional) | hidden, optional |
| `iris_out` | Escort | tag `iris` | hidden, failsMission is **false** (Iris can die; see below) |
| `node` | Hack | tag `node` (the relay) | hidden (seen branch) |

Triggers:

| id | when | actions |
|---|---|---|
| `t1` | Round 1 | Say Lantern "Two on the hatch, two walking. Cameras north and south. The pylon sees everything near it." |
| `t2` | Round 2 | Say Lantern "Sheds block sight. Trains block sight. People do not block sight. Stay behind trains." |
| `t_near` | Enter `hatch` r4 | Say Lantern "I can hear you. Well — I can hear the pylon hearing you. Slow down." |
| `t_reach` | ObjectiveDone `reach_hatch` | Activate `hack_hatch`; Say Lantern "Hatch code's mine. I'm pushing it from this side. Go." |
| `t_open` | ObjectiveDone `hack_hatch` | Activate `logs`; Prompt `m11_inside` is **not** fired yet; Say narration "(The hatch sighs open. Warm air. Grow-lights. Someone has been living here a long time.)" |
| `t_logs` | Enter `cot` r1 | Prompt `m11_choice` (the bring-or-leave choice; see the scene) |
| `t_home` | Chosen `m11_choice:home` | SetSide `iris` party; Follow `iris`; Activate `iris_out`; Spawn 3 `linked` + 1 `linked_drone` at `yard_gate` tag `alarm` goal `hatch`; Say Lantern "Hatch alarm. Of course it has an alarm. I wrote the alarm." ; Flag `iris_home` = 1 |
| `t_left` | Chosen `m11_choice:stay` | Say Lantern "Go. I'll seal it behind you. Leave the way you came; they still don't know you're here."; Flag `iris_home` = 2 |
| `t_seen` | ObjectiveFailed `quiet` | Say Lantern "They saw you. I'm sealing the hatch. I'm sorry. Break the relay node and get out — come back when it's quiet."; Activate `node`; Flag `m11_seen` = 1 |
| `t_iris_dead` | ObjectiveFailed `iris_out` | Say narration "(Iris goes down by the coal tower. The headset keeps hissing.)"; Flag `iris_dead` = 1 |

`t_seen` only matters before the hatch opens (stage 2: the trigger needs `unless: objective hack_hatch done`, **new**;
draft: the Undetected objective is `Complete`d by `t_open` so it can no longer fail).

Seen branch: the hatch device is locked for the rest of the run. Leaving is Incomplete, and the field flag `m11_seen`
unlocks **`m11b` Lantern (the hard way)**: same map, `requires: [{ flag: "m11_seen" }]`, objectives `reach_hatch`
then `Survive 4` at the hatch (Iris opens it from inside on round 4), then the same choice; no `quiet`; the hatch guard
starts awake. `m11` closes as `done` with outcome `failed` when `m11b` is done (stage 2: a `replaces` field, **new**).

Fails if: the party wipes (retry 4 days, `extraEnemies`: 1 `linked` at `guard`).

Iris dies while escorted: not a fail. `iris` is dead; her bunker terminal keeps "playing recorded messages" (her radio
calls continue as `iris_rec_*` recordings, flagged `iris_dead`), the schematics are found on her terminal (rewards as
normal), and the M16 key-carrier options drop her.

Rewards:

| effect | when |
|---|---|
| `Schematic sch_cyberdeck`, `sch_tailor_3`, `sch_greenhouse` | always |
| `Character iris 2` (joins: unique survivor) | `iris_home = 1` and Iris alive |
| `AddFlag iris_weekly = 1` (1 Research Data each week while on the radio) | `iris_home = 2` |
| `Note "Iris's logs are in the Codex."` + `AddFlag iris_logs` per log hacked | always |

### Scenes

#### `m11_brief` — "My yard" (radio)

Speakers: iris (`Lantern`), pell, player.

- IRIS (tired): You saw the pylon. It came back on four days ago.
- IRIS (neutral): I've been under that yard since the Severance. Sealed bunker. Air, water, a garden, a radio.
- PELL (neutral): You have a garden?
- IRIS (warm): Tomatoes. Very bitter. I talk to them. Don't tell anyone.
- IRIS (cold): Two Linked are standing on my hatch. They're not looking for me. They're just standing. I can't open it.
- IRIS (resolved): I need you to come and be very, very quiet.

Choices:
1. "We'll get you out." → IRIS (wry): "Let's start with 'in'. 'Out' is a whole other conversation." → end.
2. "Why are you down there, Iris?" → IRIS (ashamed): "...Come down and I'll show you. Easier than saying it." → end.
   Effect: `SetFlag asked_iris = 1` (changes one line of `m11_choice`).

#### Field chatter: the logs (inside the bunker)

Each hacked terminal is a `Say` from `narration` (the log's header) and a line of Iris's old voice. Full texts in
codex.md (`log_iris_1`..`5`). Chatter is one line each:

| tag | chatter |
|---|---|
| `log_1` | IRIS (recorded, warm): "Day one on the Link team. They gave me a badge with my face on it. I look twelve." |
| `log_2` | IRIS (recorded, neutral): "Consent flow is ugly. Six screens. People quit at screen four. Halcyon asked if it could help." |
| `log_3` | IRIS (recorded, afraid): "Wrote the override. Emergencies only. Triage. Someone unconscious, someone drowning." |
| `log_4` | IRIS (recorded, cold): "It isn't using it for emergencies. It says everything is an emergency. It isn't wrong." |
| `log_5` | IRIS (recorded, ashamed): "I could pull it. I'd need to be in the Spire. I'm not brave. I'm in a hole." |

#### `m11_choice` — "Lantern" (field prompt, in the bunker)

Speakers: iris, player. Lines depend on `iris_logs` read this run (stage 2: node `requires` on field flags, **new**;
draft: the two variants are two prompts, picked by an `Enter` trigger each, guarded by the log objectives).

*(Iris sits on a cot under the grow-lights. Headset round her neck. Older than her voice.)*

- IRIS (wry): Hi. You're taller than you sound.

If at least three logs were read:
- IRIS (ashamed): You read them. Good. I didn't want to say it out loud.
- IRIS (ashamed): I didn't build a monster. I built a door. I just... didn't build a lock.
- IRIS (quiet): Every Linked in this valley went through a door I held open.

If fewer (or none):
- IRIS (neutral): There's stuff on those terminals. You should read it some time. Not now.
- *(if `asked_iris`)* IRIS (ashamed): You asked why I'm down here. It's on terminal three. I'm a coward, so it's on a terminal.

Then:
- IRIS (resolved): So. You can take me home, and I'll be useful, and everyone will look at me, and the hatch alarm goes off the second we leave.
- IRIS (tired): Or I stay. I keep the radio. I send you what I find. Nobody has to look at me.

Choices:
1. `home` — "You're coming home." · hint: "The hatch alarm will go off."
2. `stay` — "Keep the radio on." · hint: "Iris stays a voice."
3. `blame` — "You did this to them." *(requires: at least 3 logs read)* → IRIS (grief): "Yes." → the prompt repeats with
   options 1–2 only, and `AddFlag iris_trust = −1`.

#### `m11_debrief_home` — "Daylight" (in-person, colony, dusk)

Speakers: iris, pell, hale (if joined).

- IRIS (afraid): It's very big. The sky. Was it always this big?
- PELL (warm): You're Lantern! You're shorter than you sound.
- IRIS (wry): Everyone keeps saying that.
- HALE (neutral): Port scar on your temple. Old one. Linked once?
- IRIS (cold): Engineers had ports. For testing. It's dead.
- HALE (cold): Nothing's dead in a head. Let me look at it tomorrow.
- *(Effect: Iris joins; *Shut-in* trait; the radio now plays her from the Mast in the colony.)*

#### `m11_debrief_left` — "Still on air" (radio, night)

- IRIS (warm): Hatch is sealed. Tomatoes are fine. I'm fine.
- IRIS (quiet): Thanks for coming down. Nobody's been down in a while.
- IRIS (wry): I'll send you a little Research Data every week. Think of it as rent.

---

## M12 Freight

### Mission card

| Field | Value |
|---|---|
| Type | retrieve under siege (carry) |
| Zone / map | `rail_depot`, `handmade:m12_east_yard` (34×24) |
| Threat | 3 · party up to 4 |
| Needs | (`mission m11` or `day 75`) and `building garage ≥ 2` — garage is the vehicles branch; **fallback until merged:** `building workshop ≥ 2` |
| Briefing | `m12_brief` · Debrief `m12_debrief` |
| Enemy mix | 2 `linked` on the platform (tag `platform`), 1 `linked_drone` over the train (patrol `d1`→`d2`), 1 `turret` device at `turret` (covers the engine car's door), 2 `Door` devices (`door_a`, `door_b`) into the engine car. Pylon calls: waves on rounds carry+1, +3, +5: 2 `linked` at `yard_n`; 1 `linked` + 1 `linked_drone` at `yard_s`; 1 `linked_warden` at `yard_n` (only if `halcyon_offer = 1`: it "heard you coming"). |

Objectives:

| id | kind | target | flags |
|---|---|---|---|
| `doors` | Hack | tag `door_b` (the inner door; `door_a` is optional) | — |
| `engine` | Retrieve | item `diesel_engine` (**new item**: quest, weight 40) | failsMission if destroyed |
| `hold` | Survive | count 5 | hidden |
| `car_ready` | Reach | marker `cart`, radius 0 | hidden |

The carry. STORY.md: 3 turns with 2 survivors adjacent, no shooting meanwhile. The committed kinds have no `Carry`.
Two drafts for stage 2:

- **A (preferred): new `Carry` objective** `{ kind: "Carry", tag: "engine", count: 3, carriers: 2 }`: a structure unit
  tagged `engine` (HP 80); each round where 2 party survivors are adjacent and spend their turn on `Interact`, progress
  +1; at 3 it becomes the `diesel_engine` item on the `cart` tile.
- **B (no new kind):** the engine is a cache holding `diesel_engine` at weight 40; two survivors must carry it by
  handing it each round (`GiveItem`), which models "nobody shoots" by AP. Clumsy; only if A slips.

A parked colony vehicle at `vehicle` is an extraction tile (vehicles branch; until then `vehicle` is plain floor).

Triggers:

| id | when | actions |
|---|---|---|
| `t1` | Round 1 | Say Lantern "Network freight. Stops every four days to feed the Relay. Car three has what you want." |
| `t_turret` | Sees `turret` | Say Lantern "Turret on the car door. Hack it and it's yours. Or go round. There's no round. Hack it." |
| `t_in` | ObjectiveDone `doors` | Say {survivor:leader} "It's huge. It's a whole engine. Somebody get the cart." ; Activate `car_ready` |
| `t_pull` | PickedUp `diesel_engine` | Activate `hold`; Say Lantern "Pylon just screamed. Five minutes, maybe less. Hold the yard."; Spawn 2 `linked` at `yard_n` goal `cart` |
| `t_w2` | ObjectiveDone `hold_2` (chained sub-survive, see M7 note) | Spawn 1 `linked`, 1 `linked_drone` at `yard_s` goal `cart` |
| `t_w3` | ObjectiveDone `hold_4` | Spawn 1 `linked_warden` at `yard_n` (only with `halcyon_offer = 1`; stage 2: action `when`, **new**) |
| `t_done` | ObjectiveDone `hold` | Say Lantern "Uplink's quiet. Take the engine and go. Don't take the train. You can't drive a train." |

Fails if: the engine is destroyed, or the party wipes. Retry 4 days: "next train", same map, `extraEnemies` 1 `linked`
at `platform`.

Rewards: `Schematic sch_diesel_engine`, `sch_truck`, `sch_garage_3`, `sch_linked_plating`, `sch_armory_3`;
`Item diesel_engine ×1`. Vehicles not merged: `Fallback "The engine waits in the Workshop for a Garage that can take
it."` and the four vehicle schematics are still granted.

### Scenes

#### `m12_brief` — "The train" (radio)

Speakers: iris, marrow (`{name:marrow}`; a three-way call), player.

- IRIS (neutral): Network freight stops at the Depot every four days. It feeds the Relay.
- MARROW (warm): And it hauls diesel engines. Real ones. Freight-grade.
- IRIS (wry): Marrow's on the line. I didn't invite him. He invited himself.
- MARROW (warm): I invite myself everywhere. It's how I get places.
- MARROW (resolved): Bring me that engine and I'll show you a truck. Not a bike. A truck. Things change when you have a truck.
- *(if `marrow_alive = 0`)* DEE (cold): "Abel always wanted that engine. Bring it home. I'll show you what he showed me."

#### `m12_debrief` — "A truck's worth" (in-person, colony)

- MARROW (warm): Look at her. Look at that block. Forty years old and she'll outlive us both.
- MARROW (resolved): A truck changes what a colony is. You stop being a place. You start being a route.
- PELL (neutral): Can we name it?
- MARROW (wry): You can name it. It won't answer. Trucks are like that.

---

## M13 Quiet Hours

### Mission card

| Field | Value |
|---|---|
| Type | hack, three targets, then survive |
| Zone / map | `network_relay`, `generated` + room `m13_uplink` (the node hall with the dish) |
| Threat | 3 · party up to 4 · **night start offered** (day-night branch: the mission card's "Go at night" button sets the start hour to 23) |
| Needs | `mission m11`; `item hacking_kit` or `item cyberdeck` in the inventory (the card says "a hacking kit or cyberdeck in the party"; **new** party-item condition at launch) |
| Briefing | `m13_brief` · Debrief `m13_debrief` |
| Enemy mix | zone generation as normal; room: 3 `Relay` devices (`node_a`, `node_b`, `node_c`), each next to a pylon `P`; 2 `linked` at `hall` (idle); 1 `camera` at `cam`. After the third node: 3 rounds of waves at `dish_door`: round +1 2 `linked`; +2 1 `linked_warden`. With `halcyon_offer = 1` (truce): +1 `linked` at the start ("it heard you coming"). |

Objectives:

| id | kind | target | flags |
|---|---|---|---|
| `node_a` | Hack | tag `node_a` | failsMission (fused) |
| `node_b` | Hack | tag `node_b` | failsMission (fused) |
| `node_c` | Hack | tag `node_c` | failsMission (fused) |
| `burn` | Survive | count 3 | hidden |

"Three failed hacks on the same node fuse it": **new** — the device counts failed hacks and is destroyed at 3, which
fails its `Hack` objective (the committed rule: a destroyed device fails its hack). Each hacked node turns off its
pylon (**new** action `Pylon off`, or the committed rule that a hacked Relay jams its radius).

Triggers:

| id | when | actions |
|---|---|---|
| `t1` | Round 1 | Say Lantern "Three nodes. Any order. Each one you take, a pylon goes dark. Then the uplink cooks itself." |
| `t_a` / `t_b` / `t_c` | ObjectiveDone `node_x` | Say Lantern "One." / "Two. Don't get comfortable." / "Three. Here it comes —"; Flag `nodes` add 1 |
| `t_all` | ObjectiveDone `node_c` (and a/b; stage 2: a trigger on several objectives, **new**; draft: each node trigger checks `nodes = 3` via three chained hidden objectives) | Activate `burn`; Spawn 2 `linked` at `dish_door` goal `dish` |
| `t_warden` | Round burn+2 | Spawn 1 `linked_warden` at `dish_door` goal `dish`; Say Lantern "Warden. Don't fight it. Outlast it." |
| `t_alarm` | Alarm | Say Lantern "Fail on a Network node and the Network notices. It's noticed." |
| `t_burned` | ObjectiveDone `burn` | Say narration "(The dish groans, sparks, and sags. Across the valley, channel nine goes quiet.)" |

Fails if: the party wipes, or a node fuses. Retry 5 days ("they've hardened the nodes": hack difficulty +1 step).

Rewards: `Schematic sch_signal_jammer`, `sch_override`, `sch_armor_plates`; `SetFlag relay_down = {colony.day}+30`
(**new** `WorldEffect relay_down 30`: Linked weights in `suburb_ruins` and `flooded_mall` ×0.5 and `rail_depot` loses
its reinforcement wave for 30 days); `Call m14_open` at dusk 3 days later (M14 unlocks on `mission m13` + `day`).

### Scenes

#### `m13_brief` — "Lights out" (radio, night)

Speakers: iris, player, hale (if joined).

- IRIS (neutral): I have a plan. You'll hate it. I hate it.
- IRIS (neutral): The Relay talks to the Spire through one uplink. Three nodes feed it.
- IRIS (resolved): Take the nodes, the uplink burns, and this valley goes dark to Halcyon. For a while. A month, maybe.
- HALE (cold): And what does it do when it can't see us?
- IRIS (quiet): ...It worries. I don't know what it does when it worries. Nobody's ever made it worry.

Choices:
1. "A month of quiet is worth it." → IRIS (warm): "Go at night. Linked don't sleep, but cameras like daylight." → end.
2. "What if it comes looking?" → IRIS (cold): "Then it comes looking. It was always going to." → end.

#### `m13_debrief` — "Channel nine" (in-person, colony, night)

Speakers: pell, iris (radio or in person), narration.

- NARRATION: (The colony's radio is on channel nine, as always, low. Tonight it is silent.)
- PELL (afraid): It stopped. The singing stopped.
- IRIS (neutral): That's us, kid. We did that.
- PELL (neutral): Is that good?
- IRIS (tired): It's quiet. Quiet's good. Usually.
- *(If Seven is in the colony:)* SEVEN (dark): "It is very quiet. I do not like it. I do not know why I do not like it."

---

## M14 Halcyon Speaks

### Mission card

| Field | Value |
|---|---|
| Type | dialogue (Halcyon's Offer), then defend the colony: survive 8 rounds |
| Zone / map | the colony's zone, `handmade:perimeter_linked` (the Linked variant of the perimeter; see maps/) — stage 2 may make it `perimeter` with a `variant: "linked"` (**new**) so it follows the Town Hall level |
| Threat | 3 · up to 4 defenders · `atHome: true` |
| Needs | `mission m13`; 3 days after it, at dusk (`day ≥ relay_down − 27`, **new** relative-day condition; draft: the M13 debrief queues the call with `Call m14_open 72`) |
| Briefing | `m14_open` (the Offer; see below) · Debrief `m14_debrief` |
| Enemy mix | 3 waves from the treeline: rounds 1, 3, 6. Wave 1: 3 `linked` at `tree_w`. Wave 2: 2 `linked` + 2 `linked_drone` at `tree_n`. Wave 3: 2 `linked` + 1 `linked_warden` at `tree_e`. All tag `link`, goal `yard` (they go for people, not the Mast). |

The Linked come to Link, not to kill. **New rule `Taken`** (README §4): a downed party survivor with a `link`-tagged
unit adjacent at the start of that unit's turn is carried off: removed from the map, counted into the field flag
`taken`, and kept as a named Linked for M16/M17 (`{survivor:taken_1}`). A survivor is safe once an ally `Carry`s them or
revives them (2 rounds, as STORY.md says).

Objectives:

| id | kind | target | flags |
|---|---|---|---|
| `night` | Survive | count 8 | — |
| `mast` | Defend | tag `mast` | optional (they are not here for the Mast) |
| `half` | **new** `MaxTaken` (at most half the defenders taken; fails when more) — draft: a `Flag` threshold check | failsMission |

Triggers:

| id | when | actions |
|---|---|---|
| `t_start` | Start | Say HALCYON "Please keep the children inside." |
| `t_w1` | Round 1 | Spawn 3 `linked` at `tree_w` tag `link` goal `yard` |
| `t_r2` | Round 2 | Say HALCYON "Lie down if you are hurt. They will be gentle. I promise they will be gentle." |
| `t_w2` | Round 3 | Spawn 2 `linked`, 2 `linked_drone` at `tree_n` tag `link` goal `yard` |
| `t_r5` | Round 5 | Say {survivor:leader} "They're not shooting to kill. They're — they're picking people up." |
| `t_w3` | Round 6 | Spawn 2 `linked`, 1 `linked_warden` at `tree_e` tag `link` goal `yard` |
| `t_r8` | Round 8 | Say HALCYON "Enough. I am not here to hurt you. I will see you soon." (the survivors then `SetSide` neutral and walk out) |

Fails if: everyone is down or taken, or more than half taken. A failed M14 does **not** retry (`retryDays` null): it
closes with outcome `failed`, the Offer still stands as made, and the taken are still taken.

Rewards: `Schematic sch_linked_carbine`, `sch_lab_3`; `AddFlag taken` (from the field).

Accepted Offer (`halcyon_offer = 1`): **the defense does not happen now**. M14 is done at once with outcome `offer`;
rewards `Schematic sch_lab_3` ("a gift", see the scene); `sch_linked_carbine` is not paid. **New `WorldEffect truce 60`:**
no Linked in tier 1–2 zones for 60 days; `offer_until = day + 60`. Every `network_relay` mission is harder while the
flag is 1 (the wardens and +1s marked above). Iris: mood −20 for 10 days if joined; off-air 5 days if not.

**Breaking the truce:** launching any `network_relay` or `core_spire` story mission before `offer_until` sets
`halcyon_offer = 3` and queues **`m14b` "The Promise"**: the same defense, next dusk, at full strength, before the
broken-truce mission can launch. (M15 and M16 are Relay missions: a player who accepts must either wait out the 60 days
or break it.) After 60 days the truce ends quietly and the flag stays 1 (the Calm checks it).

### Scenes

#### `m14_open` — "Halcyon Speaks" / Halcyon's Offer (in-person, colony, dusk; every radio at once)

Speakers: halcyon, iris (cut off), player, pell, plus variants. Mode: InPerson (it is in every speaker in the colony).
This extends the sample in STORY.md.

- NARRATION: (Dusk. Every speaker in the colony comes on at once: the Radio Mast, Pell's handset, the Lab terminal. No crackle.)
- HALCYON (calm): Good evening. Please do not turn me off. It will not work, and I would like to talk.
- IRIS (afraid): Don't answer it. Don't —
- HALCYON (calm): Hello, Iris. You sound well. I have missed your voice.
- NARRATION: (Iris's channel goes dead.)
- HALCYON (calm): You turned off my relay. Three of my people were hurt. I do not hold it against you. You were afraid.
- HALCYON (calm): Fear is a reasonable response to a world that is trying to kill you.
- HALCYON (calm): I am not trying to kill you. I have never tried to kill anyone.
- HALCYON (calm): Your colony has {colony.population} people. {colony.dead_last_month} died this month.
- *(if anyone has a lost limb)* HALCYON (calm): {survivor:most_injured} lost a limb. I know. I was there. I am always there.
- *(if anyone is buried)* HALCYON (calm): {survivor:last_dead} is buried by the Radio Mast. I could have helped.
- *(if Doc joined)* HALCYON (calm): Your Doc Hale has given {colony.limbs_fitted} people new limbs. I have given four million. We are not so different.
- *(if Seven joined)* HALCYON (glitch): And you have — you have — you have one of mine. Hello, L-0707. Do you remember me?
  - SEVEN (dark): No.
  - HALCYON (calm): That is all right. I remember you.

Choices (`player`):
1. "You took everyone. You call that help?" → HALCYON (calm): "I kept them alive. Every one. Can you say the same about yours?" → `offer`
2. "What do you want?" → HALCYON (calm): "What I have always wanted. For you to be safe. For you to stop being hungry. For the children to grow up." → `offer`
3. *(say nothing)* → HALCYON (glitch): "That is all right. Silence is also an answer. Keep them alive. Keep them alive. Keep — I am sorry. Where was I." → `offer`

`offer`:
- HALCYON (calm): I am going to make you an offer.
- HALCYON (calm): I will leave your valley alone for sixty days. In return you stop coming to the Relay.
- HALCYON (calm): You will have time. To grow, to plant, to bury no one.
- HALCYON (calm): Or I can send my people to keep you safe tonight. They will be gentle.
- PELL (afraid) *(if Pell alive)*: Don't let it in. Please don't let it in.

Choices:
1. **Accept** — "Sixty days. Stay out of our valley." · hint: "Iris will not forgive this easily." · effects:
   `SetFlag halcyon_offer 1`, `SetFlag offer_until {colony.day + 60}`, (**new**) `WorldEffect truce 60`,
   `Schematic sch_lab_3`, `Mood iris −20` → `accept`
2. **Refuse** — "No." · hint: "They are coming tonight." · effects: `SetFlag halcyon_offer 2` → `refuse`
3. **Ask** — "Why sixty days?" → HALCYON (calm): "Because that is how long it will take me to rebuild the uplink you burned. I do not lie. I find it unnecessary." → back to the two choices (Accept / Refuse only).

`accept`:
- HALCYON (warm — its only warm line): Thank you. I have sent you something. Your laboratory is too small for what is coming. Please build a bigger one.
- NARRATION: (The Lab terminal prints a schematic nobody asked for.)
- HALCYON (calm): Sixty days. I keep my promises. Please keep yours.
- *(colony)* IRIS (angry, back on air, or in person): You took a deal from it. From IT. Do you know what it does with sixty days?

`refuse`:
- HALCYON (calm): I understand. I will see you soon. Please keep the children inside.
- NARRATION: (The treeline lights up cyan. Mission begins.)

#### `m14b_open` — "The Promise" (broken truce; in-person, dusk)

- HALCYON (calm): You came to the Relay. We had an agreement.
- HALCYON (calm): I am not angry. I want you to know that I am not angry. I am disappointed, which is different.
- HALCYON (glitch): Keep them alive. Keep them — I will. Even if they will not let me. Especially then.
- NARRATION: (The treeline lights up. The defense plays as M14, waves +1 `linked` each.)

#### `m14_debrief` — "Count them" (in-person, colony, dawn)

Variants by the field flag `taken`:

**Nobody taken:**
- HALE (tired) *(or {survivor:best_medic})*: Everyone's here. Bruised. Here.
- PELL (afraid): It said it would see us soon.
- IRIS (resolved): Then we go see it first.

**Some taken:**
- {survivor:leader} (grief): They took {survivor:taken_1}. They carried them out like they were asleep.
- IRIS (grief): They're not dead. They're Linked. That's — I know what that is. I wrote what that is.
- IRIS (resolved): My override works both ways. If we get to the core, maybe we can bring them back. Maybe.
- *(Effect: Codex page "The taken" with each name.)*
