# Side missions

Six side missions from STORY.md plus Cole's raid (the enemy branch's colony defense). All are `main: false` (a hollow
diamond). Ids are `side_*`. Repeatable ones need **new** `repeat: { everyDays }` on `MissionDef` (README §5).

| id | Name | Type | Zone / map | Needs | Repeat |
|---|---|---|---|---|---|
| `side_marrows_debt` | Marrow's Debt | retrieve | `scrapyard_flats`, generated + room `side_tally_camp` | `m6`, `marrow_alive` | no |
| `side_docs_rounds` | Doc's Rounds | rescue | Ashgrove zones (rotates: `suburb_ruins`, `flooded_mall`, `scrapyard_flats`), generated + room `side_hideout` | `m7` | every 10 days |
| `side_sevens_name` | Seven's Name | stealth | `suburb_ruins`, generated + room `side_yellow_kitchen` | `seven_joined`, act 3 (`m11`) | no |
| `side_coles_last_job` | Cole's Last Job | ally: escort / enemy: defend the colony | ally: `scrapyard_flats`, generated + room `side_tally_camp`; enemy: `perimeter` | `m9`, act 3, `cole_path` 1 or 2 | no |
| `side_tally_raid` | The Tally Comes | defend the colony | `perimeter` | `cole_path = 2`, 10 days into Act 3 | no |
| `side_children` | Children of the Choir | rescue | `network_relay`, generated + room `side_choir_nursery` | `m14`; Pell adult; colony humanity ≥ 50 (hidden until then) | no |
| `side_dead_drop` | Dead Drop | trader contract | any zone the caravan knows, generated + room `side_dead_drop` | the caravan open (`m4`) | every 7 days |

---

## Marrow's Debt

| Field | Value |
|---|---|
| Threat | 1 (Flats) · party up to 3 |
| Briefing | `sd_debt_brief` · Debrief `sd_debt_done` |
| Enemy mix | room: the Tally camp, 3 `tally_scav` (tag `tally`, **side neutral**; they turn `enemies` if they see you take the ledger) around a fire; zone Husks as normal. |

Objectives: `ledger` Retrieve item `marrow_ledger` (**new** quest item; in a `Locker` device tagged `strongbox`, easy) ·
`quiet` Undetected (optional; keeps Cole out of it).

Triggers: `t1` Round 1 Say {name:marrow} "Red book. Tin box. Cole's fire. Don't read it." · `PickedUp marrow_ledger`
Say {name:marrow} "Got it? Don't read it. ...You read it. Everybody reads it." · `ObjectiveFailed quiet` SetSide
`tally` enemies; Say "Tally" "That's Marrow's book! Cole wants that book!"; Flag `debt_seen` 1.

Rewards: `Schematic sch_radio_mast_2`, `sch_muffler` (vehicles fallback note), `Item fuel_can ×4` (fallback `scrap ×8`).
Seen: `AddFlag cole_respect −1`. Unseen: none. Not a fail if seen; a wipe retries in 3 days.

Scenes:

`sd_debt_brief` (radio):
- MARROW (wry): I owe the Tally money. Not a lot. A lot.
- MARROW (ashamed): It's in my ledger. Along with who else I owe. Along with what I carry, and where, and when.
- MARROW (cold): If Cole reads that book, every road I run is his. Get it back before he does.
- Choices: 1. "What's in it for us?" → MARROW (warm): "Mast plans. Make your radio shout twice as far. And a muffler, because you're loud." · 2. "Why not just pay him?" → MARROW (tired): "Because then he'd know I could."

`sd_debt_done` (in-person, the caravan's next visit):
- MARROW (warm): Didn't read it, did you? ...Page eleven's about you. It says "good people, bad bandages".

---

## Doc's Rounds (repeatable)

| Field | Value |
|---|---|
| Threat | zone's · party up to 3 |
| Briefing | `sd_rounds_call` (one of four patients, rotating by `rounds_done`) · Debrief `sd_rounds_done` |
| Enemy mix | zone generation; room: the patient NPC (`patient`, hp 30, `noRecruit: false`), 2 `husk` at `door` (idle). |

Objectives: `find` Reach marker `patient`, r 1 · `treat` Aid tag `patient` · `bring` Escort tag `patient` (failsMission).

Triggers: `ObjectiveDone find` Say patient (per call, see below) · `ObjectiveDone treat` Follow `patient`; Say Doc
(radio) "Good. Now walk them. Slowly. Faster than that."

Fails if: the patient dies. No retry; the next call comes in 10 days. `AddFlag rounds_lost 1` (Doc: mood −10 for 3 days).

Rewards: `Item med_supplies ×3`, the patient joins as a recruit (with a rolled injury), `ResearchData 2`;
`AddFlag rounds_done 1`.

Patients (`sd_rounds_call`, radio; speaker `hale` relaying):
1. "Woman in the Ruins. Broken ankle, two kids, one bad tooth. Bring all three of them. Bring the tooth."
2. "Old man in the Mall. Says he's fine. He's not fine. Nobody who says they're fine is fine."
3. "Kid on the Flats. Fever. His mother's carrying him. Carry her too if you have to."
4. "Someone on the old frequency. Won't say their name. Says they had a Link once. Says it's quiet now. — Go easy."

Doc absent (`doc_state` 2 or 3): the calls come through {survivor:best_medic}'s radio as raw distress calls instead
("—please, anyone, she can't walk—"); same mission.

---

## Seven's Name

| Field | Value |
|---|---|
| Threat | 2 · party up to 3, **Seven must be in it** |
| Briefing | `sd_seven_brief` · Debrief `sd_seven_name` (the naming) |
| Enemy mix | zone generation, Linked weight ×1.5 (Network Echo is loud here); room: the yellow-tiled kitchen, empty, one `husk` at `chair` that does not move (tag `still`, side neutral — it sits at the table, a Husk on its last routine: "dinner"). |

Objectives: `quiet` Undetected (failsMission: if seen, Seven turns back: "Not like this. Not with noise.") ·
`kitchen` Reach marker `kitchen`, r 1, by Seven (**new** `unit` field, as M16) · `look` Reach marker `window`, r 0.

Triggers: `t1` Say Seven "Left at the burned car. Then the tree that looks like a hand. My feet know this." ·
`Enter kitchen r2` Prompt `sd_kitchen` · `Sees still` Say Seven "There is someone at the table. Do not wake them. They
are having dinner."

Fails if: seen (retry in 5 days) or the party wipes.

Rewards: `Schematic sch_spec_hacker`, `sch_spec_silent`; `SetFlag seven_named 1`; `SetFlag seven_pronoun 0|1`; Seven's
humanity +10 (**new** effect `Humanity seven 10`).

### `sd_seven_brief` (in-person, the Lab)

- SEVEN (neutral): I know where the kitchen is.
- {survivor:leader}: Since when?
- SEVEN (neutral): Since this morning. A child sang a song. The song had a street in it. The street had a kitchen.
- PELL (afraid) *(if Pell alive)*: It was just a song.
- SEVEN (warm): Yes. Thank you for it.

### `sd_kitchen` — "Whose kitchen" (field prompt, in the kitchen)

- NARRATION: (Yellow tiles. A radio on the windowsill. A child's height marks on the door frame, in two colours.)
- SEVEN (neutral): This is it. I know the tiles. I know the radio. I do not know who I was when I knew them.
- SEVEN (neutral): There are two sets of marks. Someone measured two children. Was I one of the children? Was I the one measuring?
- SEVEN (dark): Or was I the thing that came in the door, after?

Choices:
1. `person` — "You were someone who lived here." → SEVEN (warm): "Then I was loved. That is a good thing to have been."
2. `copy` — "Maybe you're a copy. That's still someone." → SEVEN (neutral): "A copy of a person is a kind of person. I can live with kind of."
3. `halcyon` — "Maybe you're part of it. Of Halcyon." → SEVEN (dark): "Then a piece of it came here and remembered a kitchen. That is strange. That might be hope."

Each sets field flag `seven_hint` 1/2/3 (read by the Second Voice ending lines; none is confirmed).

### `sd_seven_name` — "A name" (in-person, colony, night)

- SEVEN (neutral): I would like a name. Not a number. A number is what you call a thing you count.
- SEVEN (neutral): I do not want to be counted. I want to be called.

Choices (player picks one; Pell suggests if alive):
1. "Pick one yourself." → SEVEN (warm): "Then: Tile. Because I remember the tiles. Is that silly?" → name **Tile**.
2. *(if Pell alive)* PELL (warm): "What about Kit? Like a kitchen. Kind of." → SEVEN (warm): "Kit. Yes." → name **Kit**.
3. "Seven. It's yours now, not theirs." → SEVEN (resolved): "Seven. Then I keep it on purpose." → name **Seven**.

Then:
- SEVEN (neutral): And... you say "it". Some of you. Is that right?

Choices:
1. "We could say 'they'." → SEVEN (warm): "They. Yes. There are more than one of us in here. That is correct." → `seven_pronoun 1`
2. "What do you want?" → SEVEN (neutral): "I do not know. 'It' has been fine. 'It' has kept me safe. Ask me again later." → `seven_pronoun 0`

(Stage 2: a free-typed name is out of scope; three names. `{seven}` resolves to the chosen one.)

---

## Cole's Last Job

Two missions under one id; the branch is fixed by `cole_path` when it unlocks.

### Ally branch (`cole_path = 1`): escort

| Field | Value |
|---|---|
| Threat | 2 · party up to 4 + Cole's band (party side) |
| Map | `scrapyard_flats`, generated + room `side_tally_camp` |
| Briefing | `sd_cole_ally_brief` · Debrief `sd_cole_ally_done` |
| Enemy mix | the Linked hit the Tally camp: round 1 3 `linked` at the room's `north`; round 3 2 `linked_drone`; round 5 1 `linked_warden`. Cole (`cole`, tag `cole`, party side, `noKill` off) + 3 `tally_scav` (tag `tally`, party side) + 4 Tally non-combatants (npcs, tag `family`). |

Objectives: `family` Escort tag `family` (count: all alive is the full reward; **new** `Escort` count) · `cole` Escort
tag `cole` (failsMission: Cole dies → the band scatters, `cole_alive 0`).

Triggers: `t1` Say Cole "Nineteen names on the list. I'd like nineteen at the end." · Round 3 Say Cole "Drones! Heads down,
kids first, count off!" · Round 5 Say Cole "Warden. I'll hold it. — No? You'll hold it? ...I'm writing that down."

Rewards: `Schematic sch_spec_gun`; `Character cole 2` (joins, unique) + 3 Tally recruits (the `tally` units that leave
alive); `SetFlag cole_alive 1`; Tally trade ends (they live with you now).

Scenes:

`sd_cole_ally_brief` (radio):
- COLE (tired): Tally to colony. I'm calling in what you owe me. Then we're square.
- COLE (afraid): Linked found our camp. They're not shooting. They're picking up the kids.
- COLE (resolved): I'm moving everyone. Nineteen people. Walk us out.

`sd_cole_ally_done` (in-person, the gate):
- COLE (wry): Here's the list. Nineteen names. {tally_alive}... I'll fix the number.
- COLE (neutral): You build walls. We build lists. Turns out lists burn fine.
- COLE (warm): So. You have room? I count, I carry, I complain. Ask anyone.
- Choices: 1. "There's room." → joins. 2. "Room for everyone." (same; **hint** "Cole will remember this.") → `Mood cole +10`.

### Enemy branch (`cole_path = 2`): the duel at the colony

| Field | Value |
|---|---|
| Threat | 3 · up to 4 defenders · `atHome: true` |
| Map | `perimeter` (the Town Hall variant) |
| Briefing | `sd_cole_enemy_brief` · Debrief `sd_cole_enemy_done` |
| Enemy mix | Cole (`cole`, tag `cole`, hostile, **no** `noKill`) + 6 `tally_scav` + 2 `tally_blade` in two waves (rounds 1 and 4) from `tree_w` and `road`. |

Objectives: `cole` KillTag tag `cole` count 1 · `mast` Defend tag `mast` (optional) · `hold` Survive 10 (alternative:
holding 10 rounds ends it with Cole retreating, `cole_alive 1`, no reward).

Triggers: Round 1 Say Cole "I had nineteen names. Now I have twelve. Tell me again whose fault that is." · Round 4
Say Cole "Second wave. I always bring a second wave. I wrote that down too." · `ObjectiveDone cole` Say narration "(Cole
goes down by the gate. His notebook falls open. Your colony's name is on every page.)"

Rewards: Cole killed: `Schematic sch_spec_blade` ("from his body": the blade arm he wore), `Item cole_ledger` (Codex
page "The Tally's list"), `SetFlag cole_alive 0`. Held 10 rounds: `SetFlag cole_alive 1`; the Tally leave the valley.

`sd_cole_enemy_brief` (radio, his voice on your Mast):
- COLE (cold): Colony. This is Cole. I'm going to read you some names.
- COLE (cold): *(Seven names. Pauses after each one.)*
- COLE (cold): Those are mine. Tonight I come and get some of yours.

`sd_cole_enemy_done`, Cole dead:
- IRIS (tired): He was you. A year down a worse road. That's what scares me.
- PELL (afraid) *(if alive)*: Did he have kids?
- {survivor:leader} (grief): He had a list.

---

## The Tally Comes (enemy branch raid)

The Tally raid once in Act 3 (STORY.md, Choices table). A smaller defense than Cole's Last Job: 4 `tally_scav` + Cole
(`noKill`: he leaves at half health) on `perimeter`; objectives `hold` Survive 6, `mast` Defend. Rewards: none but
their gear; fail: they take food (`Resource food −30`) and leave. Chatter: Cole "Food, fuel, medicine. In that order.
I heard that's how you count." Unlocks 10 days after `cole_path = 2` is set; Cole's Last Job (enemy) needs it done.

---

## Children of the Choir

| Field | Value |
|---|---|
| Threat | 3 · party up to 4 |
| Needs | `m14`; Pell alive and adult (**new** condition `adult pell`; if Pell is dead, the mission still opens with {survivor:leader} in his lines); colony humanity ≥ 50 (**new**; hidden until it holds) |
| Map | `network_relay`, generated + room `side_choir_nursery` |
| Briefing | `sd_choir_brief` · Debrief `sd_choir_done` |
| Enemy mix | zone as normal; room: 3 Linked children (npcs, tag `child`, hp 40, side neutral, idle, singing), 2 `linked` "minders" (tag `minder`), 1 `camera`. |

Objectives: `wake_1..3` Hack tag `child_x` (a 1-round "de-Link" with Iris's code, **new** hackable story unit as M10;
needs a hacking kit or cyberdeck) · `out` Escort tag `child` (count ≥ 1 for done; each one out is a recruit) · `quiet`
Undetected (optional: no fight near the children).

Triggers: `t1` Say Pell "I can hear them. They're singing the song. The one from channel nine. I know all the words." ·
`ObjectiveDone wake_1` Say narration "(The child blinks. Looks at their hands. Starts to cry, the way children cry when
they've just woken up somewhere wrong.)"; Follow `child_1` · `Alarm` Wake `minder` goal `child`.

Rewards: each child out: a child recruit with humanity 60 (**new**: recruit humanity); `AddFlag children_freed n`.
Codex "The Choir".

`sd_choir_brief` (in-person, the Mast):
- PELL (resolved): I used to listen to channel nine. Every night. You know that.
- PELL (resolved): Some of the voices are kids. Iris says they keep kids at the Relay. In a room. Singing.
- IRIS (cold): He's right. I didn't want him to be right.
- PELL (resolved): I'm going. I'm not asking. I'm grown now. You said.

`sd_choir_done`:
- HALE (warm) *(if present)*: Warm blankets. Soup. No questions until tomorrow. That's the treatment.
- PELL (tired): One of them asked me what my name was. I told her. She said it back. That's all. She just said it back.

---

## Dead Drop (repeatable)

The Sandbox radio contract dressed for the story: {name:marrow} (or the Tally trader if `tally_trader`) asks for a pickup.
`side_dead_drop` rotates zone, threat = zone's, party up to 3. Room: a small cache shed with a `Locker` (tag `drop`)
holding a rolled trade crate; objective `crate` Retrieve item `trade_crate` (**new**: a quest item paid out as trade
goods at home). Rewards: `Resource` trade goods by zone tier; 20% a tier ≤ 2 schematic from the caravan's stock (**new**:
a reward `RollSchematic` with a max tier). Chatter varies by giver: Marrow "Box in a shed. Don't open it. You'll open
it." / Dee "Pickup. In and out." / Cole "It's counted. I'll know."
