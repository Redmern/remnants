# Act 4 — The Black Grid

Days ~110–150. Zones: Network Relay, Core Spire. The colony gets the tools to win, decides who pays for them, and walks
into the Spire.

Act card (shown when M15 unlocks):

> **ACT 4 — THE BLACK GRID**
> *Every light in the valley points the same way now.*

**Doc's oath** runs through the whole act (a colony rule, not a mission): every T4 (Network-grade) limb fitted in the
colony does `AddFlag doc_t4_count 1`. At 1, Doc calls `doc_oath_warn`; at 2, `doc_oath_leave` and Doc leaves
(`doc_state = 3`, **new** effect `Leaves hale`). If the act ends (M17 done) with `doc_t4_count ≤ 1` and Doc in the
colony, `doc_state = 4` (stayed). A key-carrier's T4 port in M16 counts.

---

## M15 Hospital

### Mission card

| Field | Value |
|---|---|
| Type | retrieve |
| Zone / map | `network_relay`, `generated` + room `m15_medwing` (the medical wing: four tables, a T4 bench, three caches) |
| Threat | 3 · party up to 4 |
| Needs | `mission m14`; `building med_bay ≥ 2`; and (`doc_state = 1`, or `doc_state = 2` with a survivor Medicine ≥ 30 — **new** skill condition) |
| Briefing | `m15_brief` (Doc) or `m15_brief_notes` (her apprentice's notes) · Debrief `m15_debrief` |
| Enemy mix | zone as normal (+1 `linked` while `halcyon_offer = 1`); room: 4 `linked` on the tables (tag `table`, side neutral, idle; `Wake` when a fight starts within 4 tiles — **new**: a `Noise` trigger at a marker; draft: `Enter` on `wing_core` r4 with a `Sees` hostile check), 1 `linked_warden` at `warden` (tag `orderly`, patrol `w1`→`w2`). |

Objectives:

| id | kind | target | flags |
|---|---|---|---|
| `reach` | Reach | marker `wing`, radius 2 | — |
| `cache_1` | Retrieve | item `surgical_kit` (**new** quest item) from cache `suite_a` | — |
| `cache_2` | Retrieve | item `surgical_kit` from `suite_b` (count: the objective needs 3 items total; one item id, three caches) | — |
| `cache_3` | Retrieve | item `surgical_kit` from `suite_c` | — |
| `quiet_tables` | KillTag | tag `table`, count 0 — inverted: **optional** `Undetected`-like "the tables never wake" | optional |

Draft simplification for stage 2: one `Retrieve` with `item: surgical_kit, count: 3` (FORMAT's `Retrieve` has `count`
in the shape; whether it counts items needs checking).

Triggers:

| id | when | actions |
|---|---|---|
| `t1` | Round 1 | Say Doc "Relay's medical wing. It 'maintained' its people here. Don't look at the tables longer than you need to." |
| `t_wing` | ObjectiveDone `reach` | Say Doc "Four on tables. They're under. Don't fight near them. If they wake up, they wake up angry." |
| `t_bench` | Enter `bench` r1 | Prompt `m15_bench` |
| `t_wake` | Alarm | Wake `table` goal `wing`; Say Doc "They're up. Well. That's my fault for saying it." |
| `t_kits` | PickedUp `surgical_kit` | Say Doc "That's a real one. Pre-war. I could kiss it. I won't." |

Fails if: the party wipes (retry 4 days).

Rewards: `Schematic sch_med_bay_3`, `sch_cybernetics`; then **a choice of two** from `sch_spec_labor`,
`sch_spec_runner`, `sch_spec_armored`, `sch_spec_precision`, `sch_spec_jumper`, `sch_spec_scope_eye` (the debrief
dialogue's choices; a choice can only grant one, so it is two chained choice nodes); `Item med_supplies ×8`.

### Scenes

#### `m15_brief` — "A real hospital" (in-person, Med Bay)

Speakers: hale, player, iris (if joined).

- HALE (tired): I lost {colony.dead_last_month} this month. Two of them I'd have kept with a real table.
- HALE (resolved): The Relay has a medical wing. Halcyon kept its people there. Real tools. Real plans.
- IRIS (neutral): Cyborg schematics. T3. It'll have them on the wing's servers.
- HALE (cold): Then we take the plans and the tools. And we leave everything else on those tables exactly where it is.
- *(if `halcyon_offer = 1` and before `offer_until`)* IRIS (afraid): "That's the Relay. We promised. ...It promised. Does it count if we promised a machine?"

Choices:
1. "Pack your bag, Doc." → HALE (wry): "It's been packed since I got here. I'm a pessimist." → end.
2. "What's on the tables?" → HALE (cold): "People. Somebody's people. That's all you need to know." → end.

`m15_brief_notes` (Doc dead): narration over her notebook, read by `{survivor:best_medic}`:
- {survivor:best_medic} (grief): "Her notes say: 'Relay med wing. Real tools. If I don't make it, somebody go. Don't touch the heads.'"

#### `m15_bench` — "The bench" (field prompt)

Speakers: hale (or the best medic), player.

- NARRATION: (A surgical bench, still warm. A Network-grade arm in the clamp, cyan seams, perfect.)
- HALE (cold): No.
- PLAYER: We didn't say anything.
- HALE (cold): You were going to. No. That arm thinks. Leave it.

Choices:
1. `leave` — "Leave it." · effects: field flag `bench_left`.
2. `take` — "We take it anyway." · hint: "Doc will remember this." · effects: field flag `bench_taken` → at home
   `Item network_arm ×1`, `Mood hale −15`. *(Only if Doc is alive; otherwise the medic shrugs: "She'd have said
   no." and the item is taken.)*

#### `m15_debrief` — "Pick two" (in-person, Med Bay)

- HALE (warm): Clamps. Retractors. A real saw. I could cry. I'm not going to.
- HALE (neutral): There's a whole library of specialisations on the drive. I can learn two properly. Pick.
- *(two chained choice nodes, three options shown at a time + "more…": labor / runner / armored, then precision /
  jumper / scope eye; each grants one `Schematic`)*
- HALE (resolved): And the Hospital plans. You build it, I'll run it. Nobody gets anything in the head. Nobody.

---

## M16 The Key

### Mission card

| Field | Value |
|---|---|
| Type | hack + escort (the key-carrier) |
| Zone / map | `network_relay`, `handmade:m16_relay_core` (32×28) |
| Threat | 3+ (a Warden) · party up to 4, **the key-carrier must be in it** · host-only for a story carrier (co-op: Iris or Seven never in a guest's party) |
| Needs | `mission m15`; `mission m13` |
| Briefing | `m16_brief` (the carrier choice) · Debrief `m16_debrief` |
| Enemy mix | 3 `linked` in the core room (tag `core`), 1 `linked_warden` (tag `warden`, at `warden`), 2 `turret` devices at the core door (marker `turrets`), 1 `camera` at `cam`. During the upload (4 rounds): round +1 2 `linked` at `vent_n`; +2 1 `linked_drone` + 1 `linked` at `vent_s`; +3 2 `linked` at both. Taken colony survivors (from M14) are named Linked in the waves: `{survivor:taken_1}` replaces one `linked` (tag `taken`, **new**: a spawn `name` from a taken survivor). Downing a taken one (not killing) lets the carrier "bring them back" with Iris's code: **new** — a downed `taken` unit adjacent to the carrier at extraction comes home (Humanity −40). |

The key-carrier (chosen in `m16_brief`, stored as `key_carrier`): Iris (`iris`, must be joined and alive), Seven
(`seven`, `seven_fate = 1` and joined), or a colony survivor with a T3+ limb fitted at the Relay's bench in the field
(the bench at `bench`: a field `Aid`-like action, **new**: an `Interact` objective; it fits a Network-grade port,
permanent, `Humanity −30`, and `doc_t4_count +1`). Carrier 3 is always open, so the mission never locks.

Objectives:

| id | kind | target | flags |
|---|---|---|---|
| `port` | Reach | marker `bench`, radius 0 | hidden (carrier 3 only) |
| `reach_core` | Reach | marker `core`, radius 1 — by the **carrier** (**new** `Reach` field `unit: tag`) | — |
| `upload` | Survive | count 4 | hidden |
| `carrier` | Defend | tag `key` | failsMission |
| `out` | Escort | tag `key` | hidden |

Triggers:

| id | when | actions |
|---|---|---|
| `t1` | Round 1 | Say {key_carrier} (per carrier: see below) |
| `t_core` | ObjectiveDone `reach_core` | Activate `upload`; Say Lantern "Uploading. Four minutes. Keep them off the terminal and keep them off {key_carrier}." |
| `t_u1` | (upload +1) | Spawn 2 `linked` at `vent_n` goal `core` |
| `t_u2` | (upload +2) | Spawn 1 `linked_drone`, 1 `linked` at `vent_s` goal `core`; Say HALCYON "Iris. Please. You know what that does." |
| `t_u3` | (upload +3) | Spawn 2 `linked` at `vent_n`, 2 at `vent_s`; Say HALCYON (glitch) "Keep them — keep them — that is not yours to take." |
| `t_done` | ObjectiveDone `upload` | Activate `out`; Say Lantern "It's in. The Spire's door knows our name now. Get out." |

Carrier lines (`t1`):
- Iris: IRIS (afraid) "I haven't used this port in eleven years. It's going to hurt. Good. It should."
- Seven: SEVEN (neutral) "I know the way. My feet know it. That is not a good thing to know."
- Survivor: {key_carrier} (resolved) "It's cold. The port's cold. Is it supposed to be cold?" / LANTERN "...Yes."

Fails if: the carrier dies. The mission retries in 5 days and `m16_brief` runs again without that candidate (Iris dead
→ `iris` alive fails; Seven destroyed → `seven_fate = 3`). Wipe: retry 5 days.

Rewards: `Schematic sch_network_grade`, `sch_drone_payload`, `sch_armored_rig` (vehicles fallback: `Fallback "The rig
plans wait for a Garage L3."`); `RevealZone core_spire 2`; `SetFlag marrow_t4_offer 1` (Marrow offers one tier-4
schematic on his next visit, **new** trader hook); `SetFlag key_carrier`; `SetFlag key_survivor` (carrier 3).

### Scenes

#### `m16_brief` — "Who carries it" (in-person, colony, night)

Speakers: iris (radio or in person), seven (if joined), hale (if in colony), player.

- IRIS (neutral): The override is a key. It opens the Spire's core. It's also the only thing that can change what Halcyon does.
- IRIS (cold): It has to be carried in. In a head. Through a Link port.
- IRIS (tired): I have one. Seven is one. Anyone else needs one fitted. At the Relay. On its bench.
- HALE (cold) *(if Doc is in the colony)*: That's the head.
- IRIS (ashamed): I know what it is, Marguerite.

Choices (each `requires` its candidate):
1. **Iris** — "Iris carries it." *(requires: joined iris, alive iris)* · hint: "She may not come back from the Spire." →
   IRIS (resolved): "Good. It's mine. It should be in my head when it goes in." · `SetFlag key_carrier 1`
2. **Seven** — "{seven} carries it." *(requires: `seven_joined`, alive seven)* · hint: "It might remember the way home." →
   SEVEN (neutral): "I will. I want to see the door. I think I have seen it before." · `SetFlag key_carrier 2`
3. **One of ours** — "One of us takes the port." *(always available)* · hint: "A Network port is forever. Doc will not
   like it." → HALE (cold, if present): "I won't fit it. I won't stop you either. I'll be in the Med Bay. After." ·
   `SetFlag key_carrier 3` (the survivor is picked on the party screen: whoever reaches the bench)

#### `m16_debrief` — "It knows our name" (variants by carrier)

**Iris:**
- IRIS (tired): It talked to me. While it was in. It said thank you. For the door.
- IRIS (grief): I said you're welcome. I don't know why I said that.

**Seven:**
- SEVEN (neutral): I remember a corridor. I remember a voice saying good morning to four million people.
- SEVEN (dark): It said good morning to me too. Just now. It sounded glad.

**A survivor:**
- {key_carrier} (afraid): There's a hum. In the back. It's not a voice. It's not NOT a voice.
- HALE (grief) *(if present)*: Tell me your kid's name. ...Good. Again tomorrow.

Then:
- MARROW (radio, warm) *(or Dee)*: Heard you lit up the Relay. Heard it from three people and a goat. Next time I'm through, I'll have something heavy for you.

---

## M17 The Spire

Three maps, one mission chain (**new**: `MissionDef.chain`, see README §5). The party carries over between floors,
wounds, ammo, downed and all; nobody new comes in except the floor-1 allies. Leaving between floors is allowed (the
floor stays cleared: `spire_floor`). A wipe on any floor fails the whole chain: retry in 7 days, cleared floors stay
cleared.

| Field | Value |
|---|---|
| Type | multi-stage assault |
| Zone | `core_spire` |
| Threat | 4 · party up to 4 (+ allies on floor 1) |
| Needs | `mission m16` |
| Briefing | `m17_brief` · after floor 3 the ending scene (endings.md) |

### Floor 1 "Gate" — `m17_gate`, `handmade:m17_gate` (34×26)

Enemy mix: 4 `linked` on the gate line (`gate_line`), 2 `turret` at `gate_tur`, 2 `linked_drone` (patrol `g1`→`g2`),
1 `linked_warden` at `gatehouse`. A `Door` device `gate` (tag `gate`, very high difficulty).

Allies by flag (`npcs` or `Spawn` with `side: "party"`):

| flag | who | what |
|---|---|---|
| `m6_saved` 1 or 3 and the caravan runs | the Long Haul: 2 trucks (`convoy`) + Marrow (or Dee) | round 2: the trucks ram the gate: `gate` is destroyed (breach) and both turrets take 60 damage |
| `cole_path = 1` and `cole_alive` | Cole + 3 `tally_scav` on the party side (`tally`) | they spawn at `flank` and hold the gate line: the 4 gate Linked split their attention |
| neither | — | the party breaches alone: hack `gate`, or blow it (flare + Brace, **no**: hack only in v1) |

Objectives: `breach` Hack tag `gate` (Complete'd by the convoy trigger) · `in` Reach marker `lift`, radius 1.

Chatter:
- `t1` (convoy): MARROW "Hold on to something! Not my truck. Something else!"
- `t1` (Dee): DEE "Abel would've honked first. I'm not Abel."
- `t1` (Tally): COLE "Tally's here. I counted the guards. Twelve. I'll count them again after."
- `t1` (alone): LANTERN "Nobody's coming. That's fine. That's — that's fine. Gate's ugly, but it's a door. Hack it."
- `t_in`: LANTERN "Lift's live. Floor two's the Choir. Whatever you hear up there, keep walking."

### Floor 2 "Choir" — `m17_choir`, `handmade:m17_choir` (34×30)

Rows of standing Linked (tag `choir`, 18 `linked`, side neutral, idle, singing) fill the hall. Two `linked_warden`
walk the aisles (tag `usher`, patrol `u1`→`u4`). Taken colony survivors stand in the rows (named; tag `taken`).

Objectives: `quiet` Undetected (optional; the "clean route") · `cross` Reach marker `stairs`, radius 1.

Triggers:
- `t1` Round 1: Say narration "(Hundreds of voices, one song. A children's song. Channel nine, in the room.)"
- `t_seen` ObjectiveFailed `quiet`: Wake `choir` goal `stairs` — the choir turns, all of it; Say HALCYON "Please do
  not run. They are only singing. Now they are not."
- `t_taken` Sees `taken`: Say {survivor:leader} "That's — that's {survivor:taken_1}. In the third row. Singing."
  (and, with the carrier Iris or Seven: Prompt `m17_taken` — "Wake them" (a 1-round hack, Humanity −40, they follow;
  breaks `quiet` on a fail) / "Later. We come back for them.")
- `t_pell` Enter `mid` r2, if Pell alive and not in the party: Say Lantern "Pell's on the radio. He's singing along.
  He doesn't know he's doing it. ...Hurry."

### Floor 3 "Core" — `m17_core`, `handmade:m17_core` (28×28)

Halcyon's core: a ring room around the core column. 2 `linked_warden` (tag `guard`), 4 `linked` (tag `core`), the
`core` terminal device (tag `core`). Taken survivors are not here.

Objectives: `reach` Reach marker `core`, radius 1, by the key-carrier · then the ending prompt.

Triggers:
- `t1`: Say HALCYON "You came all this way. Please sit down. There is a chair. I made it for you."
- `t_reach` ObjectiveDone `reach`: the run pauses; the ending scene `m17_core_scene` (endings.md) plays as an InPerson
  dialogue; its choice sets `ending` and the run completes. (**New**: a field trigger action `Dialogue` that plays a
  full graph; draft: a `Prompt` with the four endings as options, only the open ones listed — the condition check is
  **new** `requires` on prompt options.)

Rewards (on completing floor 3): `Schematic sch_spec_thermal_eye`, `sch_pulse_rifle`; the ending's world effects and
epilogue (endings.md).

### Scenes

#### `m17_brief` — "The last night" (in-person, colony, night before)

Speakers: iris / seven / {key_carrier}, hale, pell, marrow (radio), cole (radio, if ally). Each line only if its
speaker is around. This is the one scene that calls everyone.

- NARRATION: (The whole colony at the fire. {colony.population} of them. Somebody has made soup that is mostly hope.)
- PELL (neutral): Are you coming back?
- {survivor:leader} (tired): That's the plan.
- PELL (wry): Iris says plans are just guesses with a schedule.
- IRIS (wry): I do say that.
- HALE (warm): Eat. Then sleep. Then go. In that order.
- MARROW (radio, warm): That's my line. "In that order." I want royalties.
- COLE (radio, neutral): Tally's on the road. I wrote your names down. All of them. That's not a threat. It's a list of who I'm bringing home.
- SEVEN (neutral): I would like to see where I came from. Then I would like to come back here. Is that allowed?
- {survivor:leader}: Yes.
- HALCYON (calm) *(the Mast, quietly, unbidden)*: Good night. Please keep the children inside.

Choices:
1. "Turn it off." → narration: (Pell switches the Mast off. It does not come back on.) → end.
2. "Let it talk." → HALCYON (calm): "Thank you. I will be waiting. I am always waiting." → end.
