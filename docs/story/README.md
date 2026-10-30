# Story content: Acts 2–4

Scene scripts, mission specs and map drafts for the second half of the campaign. The spec is `docs/STORY.md`
(characters, structure, endings) and `docs/SCHEMATICS.md` (reward ids). This folder is **content, not data**; field names follow the committed
`docs/story/FORMAT.md` on story-campaign: it is
written so it converts one-to-one into the story framework's formats (`MissionDef`, `FieldMission`, `DialogueDef`,
`HandmadeMapDef`) once that framework is on main. Nothing here is loaded by the game yet.

| File | What |
|---|---|
| [act2.md](act2.md) | Act 2 "Ashgrove": M6–M10, scenes and mission specs |
| [act3.md](act3.md) | Act 3 "Iron Corridor": M11–M14, Halcyon's Offer |
| [act4.md](act4.md) | Act 4 "The Black Grid": M15–M17 (three floors) |
| [side_missions.md](side_missions.md) | the six side missions, Cole's raid |
| [endings.md](endings.md) | the four endings, epilogue slideshows with flag variants, after-the-end world changes |
| [survivors.md](survivors.md) | unique survivors as data-ready tables, new hook ids |
| [codex.md](codex.md) | Codex entries: character pages, logs, terminals |
| [maps/](maps/) | handmade map drafts in the `HandmadeMapDef` JSON format |

## Conventions

### Scenes

Every scene has an id (`m6_open`, `m11_hatch`), which becomes a `DialogueDef.Id` (or a field `PromptDef.Id` when it is a
choice in the field). Scene header fields:

- **Mode:** `radio` (radio frame, grainy portrait, call sign; colony time pauses), `in-person` (dialogue panel; a
  mission's opening and closing scenes and every pausing choice), or `chatter` (the strip above the action bar; does not
  pause; becomes a trigger `Say` action).
- **Speakers:** character ids from the committed `characters.json` (`iris`, `marrow`, `dee`, `juniper`, `hale`, `cole`,
  `seven`, `halcyon`, `pell`). In a field `Say` the speaker is the **display name** ("Lantern", "Doc", "Cole Brannigan"),
  as in act1.json; the drafts write the id or call sign and stage 2 maps it. `player` is the colony's lead survivor (the party leader in the field,
  the Town Hall's best talker at home). `pell` is the colony's own Pell. `narration` is stage text.
- **Portrait moods** (one per speaker per line, default `neutral`): `neutral`, `warm`, `wry`, `tired`, `hurt`, `angry`,
  `afraid`, `grief`, `cold`, `resolved`, `ashamed`. Halcyon has two: `calm` and `glitch` (the ring stutters). Seven has
  `dark` (its one cyan light off) next to the usual ones.
- **Lines:** `SPEAKER (mood):` text. One idea per line. Halcyon is the only one in perfect sentences.
- **Choices** are numbered, at most three, with `requires` (conditions), `effects` and `hint` (shown text, never
  numbers). Conditions and effects use the framework vocabulary:
  - conditions: `mission M6 = people`, `flag cole_respect ≥ 2`, `alive iris`, `day ≥ 75`, `building med_bay ≥ 2`,
    `item hacking_kit`, plus three new ones marked **(new)**: `humanity ≥ 50` (colony average, adults),
    `survivor_humanity seven ≥ 30`, and `joined iris`.
  - effects: `set flag = n`, `add flag += n`, `schematic sch_x`, `item id ×n`, `resource id n`, `research n`,
    `reveal zone (scouted)`, `call dialogue_id +h hours`, `character id met|joins`, `fallback "text"`, `note "text"`,
    plus **(new)** `mood character ±n`, `humanity character ±n`, `leaves character`, `start mission`, `world effect id`.
- **Placeholders:** the committed ones (`{survivor:leader}`, `{survivor:best_medic}`, `{survivor:best_shot}`,
  `{name:<characterId>}`, `{colony.population}`, `{colony.dead_last_month}`, `{colony.day}`) plus **(new)**
  `{survivor:most_injured}`, `{survivor:last_dead}`, `{survivor:taken_1}`, `{key_carrier}`, `{seven}` (Seven's chosen
  name, "Seven" until then) and `{seven.they}` / `{seven.them}` / `{seven.their}`. `{name:marrow}` resolves to Dee once
  Marrow is dead (**new**: a character `successor`).

### Mission specs

Mission ids are lowercase (`m6`, `side_marrows_debt`). Mission cards follow STORY.md's fields, in the shape of `MissionDef` + `FieldMission`:

- **Objectives** use only the committed kinds: `Reach`, `Hack`, `Retrieve`, `Escort`, `Aid`, `Survive`, `Defend`,
  `KillTag`, `Undetected`. Columns: id, kind, target (marker / tag / item / count), and flags (`optional`,
  `failsMission`, `hidden` = activated by a trigger).
- **Triggers** use the committed `TriggerKind`s (`Start`, `Round`, `Enter`, `ObjectiveDone`, `ObjectiveFailed`,
  `PickedUp`, `Sees`, `Alarm`, `Chosen`) and actions (`Spawn`, `Say`, `Activate`, `Complete`, `Fail`, `Flag`,
  `SetSide`, `Follow`, `Prompt`, `Split`, `Wake`). A chatter line is a `Say`.
- **Map:** `handmade:<id>` (a file in `maps/`), `perimeter` (the colony perimeter by Town Hall level), or `generated` with
  a fixed `room` (also in `maps/`, `"room": true`).
- **Rewards** name schematic ids from SCHEMATICS.md. Where the feature behind a reward is not merged yet (vehicles),
  the card gives the `fallback` effect: the schematic is still granted (so it is there the day the feature lands) and a
  stand-in is paid now.

## Campaign flags

All long-lived state the story reads. Flags are ints (`CampaignState.Flags`); 0 = unset.

| Flag | Values | Set by | Read by |
|---|---|---|---|
| `cole_met` | 1 | M3 (Act 1, committed) | Cole lines |
| `cole_respect` | −5..+5 | M3, M9, Marrow's Debt, M13 | Cole ally (≥ 2) / enemy (≤ −2) / neutral; M17 gate |
| `cole_path` | 1 ally, 2 enemy, 3 neutral | Act 3 start (day the first Act 3 mission opens) | Cole's Last Job branch, Tally raid, M17 |
| `cole_alive` | 1 / 0 | Cole's Last Job | epilogue |
| `tally_trader` | 1 | M9 Share | Tally trade visits |
| `marrow_alive` | 1 / 0 | M6 | `{marrow}`, caravan, M17 gate, epilogue |
| `m6_saved` | 1 people, 2 cargo, 3 both | M6 | prices (people/both −10%), "in that order", M17 convoy |
| `doc_state` | 1 joined, 2 dead in M7, 3 left (oath), 4 stayed after Act 4 | M7, oath | M15, Hospital line, epilogue |
| `doc_t4_count` | 0..n | every T4 limb fitted in the colony | oath warnings (1) and leaving (2) |
| `seven_fate` | 1 captured, 2 freed, 3 destroyed | M10 | Seven joining, Seven's Name, M16 key, Second Voice |
| `seven_joined` | 1 | quarantine done (7 days after M10 Capture) | Act 3+ |
| `seven_named` | 1 | Seven's Name | `{seven}` |
| `seven_pronoun` | 0 it, 1 they | Seven's Name (or earlier colony use) | `{seven.they}` |
| `iris_home` | 1 home, 2 left on the radio | M11 | Iris joining, weekly Research Data, key carrier |
| `iris_logs` | 0..5 | M11 (terminals read) | M11 confession scene depth, Codex |
| `halcyon_offer` | 1 accepted, 2 refused, 3 accepted then broken | M14 | Relay missions' difficulty, Iris's mood, the Calm |
| `offer_until` | colony day | M14 accept | zones' Linked weights, "broken" check |
| `taken` | 0..n | M14 (survivors carried off) | M16, M17 Choir (named Linked), epilogue |
| `key_carrier` | 1 Iris, 2 Seven, 3 a colony survivor | M16 | Terms, Second Voice, M17 core scene |
| `key_survivor` | survivor id | M16 (carrier 3) | `{key_carrier}` |
| `relay_down` | colony day it ends | M13 | zone weights for 30 days |
| `children_freed` | 0..3 | Children of the Choir | epilogue, Terms |
| `spire_floor` | 0..3 | M17 floors cleared | M17 retries |
| `ending` | 1 Silence, 2 Terms, 3 Second Voice, 4 Calm | M17 core | world changes, epilogue |
| `uploaded` | 2 Seven, 3 a survivor | Second Voice | epilogue, after-the-end radio |

Character life and joining use the framework's character state (`alive iris`, `joined hale`), not flags.

## What the framework needs for Acts 2–4

Gathered from the specs below; stage 2 built these on the framework, not beside it (see ARCHITECTURE.md "Story mode" →
"Acts 2–4"). Where the data differs from the specs: M6's trucks follow the party (no `pathTo`); Seven (M10) and the
Choir children are reached and then follow (no hacking units); M12's engine is a `Carry` objective; M14's taken use
`takeTag` and a `Keep` objective; the Spire is three missions chained with `chainFrom`.

1. **Conditions:** `Humanity` (colony average of adults), `SurvivorHumanity` (one unique survivor), `Joined`, and
   `Outcome` matching one of several (`people|both`).
2. **Effects:** `Mood`, `Humanity`, `Leaves`, `StartMission`, `WorldEffect` (timed zone weight changes: M13 relay down,
   the Offer's truce, after-the-end), and `Join` with a unique-survivor def (stats, traits).
3. **Field:** a `Carry` objective for the M12 engine (STORY.md lists `Carry(objectId, turns, carriers)`; it is not in the
   committed kinds). M12 gives two drafts (a new `Carry` kind, or a heavy `Retrieve` fallback).
4. **Field:** a `Taken` rule for M14: a downed party unit adjacent to a Linked at the end of its next round is carried off
   (counted into the `taken` flag); M14's objective "at most half taken" is a `KillTag`-shaped counter, flagged there.
5. **Missions:** chains (`M17` three floors, each a `MissionDef` with `chain` = next id, party carried over), a
   repeatable flag (Doc's Rounds, Dead Drop), and a `Hidden` (side missions shown only when conditions hold).
6. **Enemies:** the Tally use the committed `tally_scav` and `cole`; one new def, `tally_blade` (machete), and a
   `noKill` flag on `cole` for M9 (he always escapes).
7. **NPC behaviour:** `pathTo(marker)` for M6's trucks; story units that can be hacked (Seven in M10); an HP-threshold
   trigger (Cole flees at half health); relative rounds (a trigger `delay` after an objective activates).
8. **Placeholders:** `{seven}` and its pronouns, `{survivor:most_injured}`, `{survivor:taken_1}`, `{key_carrier}`.
9. **Epilogue:** a slideshow def (stills + text with conditions), and after-the-end world effects.
10. **Smaller field rules** found while writing the specs: trigger `delay` / relative rounds (M7 waves, M12, M16
    upload), a trigger on several objectives (M13), action `when` (M12 warden only under the truce), prompt-option
    `requires` (M7 "help", M17 endings), a device fail counter (M13 fuse), per-device hack `bonus` (M11 Iris +15%),
    `Reach` by a tagged unit (M16 carrier, Seven's Name), `Escort` count (Cole's family), a `Dialogue` field action (M17
    core), a `replaces` field for retry variants (M11b), a launch condition on party items (M13), and the validator
    counting `Door` devices as passable (maps/README.md).
11. **Rewards and calls** go through the committed paths: `Campaign.Schematics` (`ISchematicGrants`, `Schematics.Grant`
    at merge) and radio calls as RadioMast `story:<callId>` tags.
