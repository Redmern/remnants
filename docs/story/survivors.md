# Unique survivors

Story characters who join the colony as real survivors (`Survivor.UniqueId` = the character id from
`characters.json`). Each is a data-ready row for a future `data/story/characters.json` extension (**new** field
`survivor` on `CharacterDef`): fixed sex and age, attributes, skills, perks (existing ids from `data/perks.json`), and
traits. A trait is a name, a line of text, and hooks — existing `PerkEffects` hook ids where one fits, **new** ones
marked and listed at the end. Unique survivors never appear as random recruits, never leave on their own except where a
trait says so, and their deaths fire story fallbacks (STORY.md).

Attributes are 0–100 (`AttributeKind`); skills 0–100 (`Skill`). Rows not given use the normal roll for a recruit.

## Iris Vahn (`iris`, she/her)

Joins: M11 "home" (`iris_home = 1`). Age 44, Female.

| Attribute | Value | | Skill | Value |
|---|---|---|---|---|
| Strength | 30 | | Electronics | 50 |
| Agility | 40 | | Hacking | 40 |
| Endurance | 35 | | DronePiloting | 30 |
| Intellect | 80 | | Farming | 20 (the bunker garden) |
| Creativity | 60 | | Firearms | 10 |
| Perception | 55 | | Medicine | 10 |

Perks: `hacking_1`, `hacking_2`, `bench_engineer`, `quick_study`. Body: a dead Link port (Head, cosmetic; Humanity 90 —
she counts as already a little "fitted": **new** `startHumanity`).

| Trait | Text | Hooks |
|---|---|---|
| *Network Engineer* | "Electronics 50, Hacking 40. Decrypt orders take half the work." | `decrypt_work_mult: 0.5` (**new**, colony), `hack_chance_bonus: 0.05` |
| *Guilt* | "Her mood drops when a Linked is killed in front of her." | `mood_on_kill_tag: { tag: "linked", value: −8 }` (**new**, field → home) |
| *Shut-in* | "Never goes on scavenges unless the mission requires it." | `no_scavenge: 1` (**new**: the party screen refuses her unless the mission's `requires` names her) |
| *Link port* | "She can carry the key." | `link_port: 1` (**new**, read by M16) |

Death: her bunker terminal keeps playing recorded messages (`iris_rec_*` dialogue ids replace her calls); her
schematics are found on it.

## Dr. Marguerite Hale (`hale`, she/her, call sign Doc)

Joins: M7, Doc alive. Age 56, Female. Her own right forearm is a T1 mechanical prosthetic (fitted at join, Humanity 97).

| Attribute | Value | | Skill | Value |
|---|---|---|---|---|
| Strength | 45 | | Medicine | 45 |
| Agility | 40 | | Crafting | 25 |
| Endurance | 55 | | Cooking | 20 |
| Intellect | 70 | | Melee | 20 (a bone saw) |
| Creativity | 45 | | Firearms | 15 |
| Perception | 65 | | | |

Perks: `field_medic`, `triage`, `surgeon`, `surgery_1`.

| Trait | Text | Hooks |
|---|---|---|
| *Field Surgeon* | "Medicine 45, Surgeon. Fittings she leads succeed 20% more often." | `surgery_success_bonus: 0.2` (existing, CYBERNETICS.md) |
| *Oath* | "Will not fit T4 limbs. If the colony fits one anyway, her mood drops; after a second, she leaves." | `refuses_tier: 4` (**new**: she cannot lead T4 surgery), plus the story rule in act4.md (`doc_t4_count`) |
| *Teacher* | "Counts as a mentor for Medicine up to 45." | `mentor: { skill: "Medicine", cap: 45 }` (**new**: reuses `Study` teaching) |

Death: `doc_state = 2` (if after M7: her notebook in the Hospital, epilogue line). Leaving: `doc_state = 3`, she goes
to the Mall; she still sends medicine with the caravan (`+3 med_supplies` each caravan visit).

## Cole Brannigan (`cole`, he/him)

Joins: Cole's Last Job, ally branch. Age 38, Male.

| Attribute | Value | | Skill | Value |
|---|---|---|---|---|
| Strength | 55 | | Firearms | 50 |
| Agility | 60 | | Melee | 45 |
| Endurance | 60 | | Crafting | 30 |
| Intellect | 55 | | Cooking | 20 |
| Creativity | 35 | | | |
| Perception | 65 | | | |

Perks: `steady_aim`, `scavenger`, `pack_mule`, `dug_in`. (Matches the committed `cole` enemy's attributes.)

| Trait | Text | Hooks |
|---|---|---|
| *Quartermaster* | "+15% carry for the party he leads. Expeditions need one ration less." | `party_carry_mult: 0.15` (**new**: applies to every unit in his party), `expedition_rations: −1` (**new**) |
| *Keeps Score* | "Mood +10 while the colony has more food than mouths, −10 when not." | `mood_food_surplus: 10` (**new**: ± by food days ≥ population) |

### The three Tally recruits

Ordinary recruits (no `UniqueId`), rolled as normal with these fixed names and a nudge, from the `tally` units that
left alive (fewer if some died):

| Name | Pronouns | Nudge | Perk |
|---|---|---|---|
| Ruth "Ledger" Okafor | she/her | Intellect +15, Crafting 25 | `tinkerer` |
| Ames | he/him | Firearms 30 | `steady_aim` |
| Little Tom | he/him (age 17) | Agility +15, Melee 25 | `light_step` |

## Seven (`seven`, it — or they, chosen in Seven's Name)

Joins: M10 capture, after a 7-day quarantine at the Lab (`seven_joined`). No age (adult), no sex roll (**new**:
`Sex` needs a third value or a "none" flag; draft: stored as Female with `UniqueId` overriding every pronoun in text).
Cannot have children, cannot partner (**new** `no_partner`).

| Attribute | Value | | Skill | Value |
|---|---|---|---|---|
| Strength | 70 | | Hacking | 50 |
| Agility | 55 | | Electronics | 35 |
| Endurance | 80 | | Melee | 30 |
| Intellect | 60 | | Farming | 15 |
| Creativity | 20 | | | |
| Perception | 70 | | | |

Perks: `hacking_1`, `network_ghost`. Body: Linked plating as torso armour built in (`linked_plating`, cannot be
removed), Humanity 0.

| Trait | Text | Hooks |
|---|---|---|
| *Ghost in the Wire* | "Hacking 50. Hacks devices from 2 tiles away without a kit. Immune to Override." | `hack_range: 2` (**new**), `hack_without_kit: 1` (**new**), `override_immune: 1` (**new**) |
| *Not Human?* | "Humanity starts at 0 and rises with time in the colony — the only one whose does. Others' mood −5 near it until it reaches 30." | `humanity_growth_per_day: 0.5` (**new**: +0.5/day at home, +10 from Seven's Name, cap 60), `nearby_mood: { value: −5, radius: 3, untilHumanity: 30 }` (**new**) |
| *Network Echo* | "Small chance per mission the Linked notice it: an extra Linked patrol." | `network_echo_chance: 0.15` (**new**: at scavenge start, +1 `linked` patrol) |
| *Link port* | "It can carry the key." | `link_port: 1` (**new**) |

Humanity curve check: joined around day 75, +0.5/day → 30 by day ~135 without Seven's Name, ~115 with it. That puts
Seven past 30 (children stop being afraid) in mid-Act 4 for a normal pace. The Second Voice ending reads "Seven alive"
only, not its humanity.

Free path (`seven_fate = 2`): Seven never joins; it appears at the Spire core (endings.md).

## Pell (`pell`, he/him)

Already a colony survivor from the start (Act 1 committed: `UniqueId = "pell"`, one of the two starting children,
attributes rolled). Acts 2–4 add growing up and two traits:

| Trait | Text | Hooks | When |
|---|---|---|---|
| *Radio Kid* | "Hears new contacts first. Learns Electronics faster at the Radio Mast." | `skill_xp_mult: { skill: "Electronics", value: 0.25 }` (**new**) | from the start |
| *Grew Up Here* | "Raised in this colony. His mood follows the colony's humanity." | `mood_follows_humanity: 1` (**new**: mood cap = colony humanity + 30) | when he becomes an adult |

Growing up: children reach adulthood in a few weeks of game time (rules.life); Pell's adult portrait variant swaps
in (`pell_adult`), and he can go on missions from Act 2. Story reads `adult pell` (**new** condition) for Children of
the Choir. If he dies, the story continues; the epilogue remembers him (endings.md).

## Not joining (for completeness)

| id | Note |
|---|---|
| `marrow` | Visits every 10 days as the trader. Dies only in M6; `{name:marrow}` → Dee after (**new** `successor: "dee"`). |
| `dee` | Runs the caravan if Marrow dies. Never joins. |
| `juniper` | Act 1. Rides with the caravan in M6 (cosmetic) and in the M17 convoy. |
| `halcyon` | No body. Portrait = the cyan ring; moods `calm`, `glitch`. |

## New hook ids

| hook | scope | meaning | from |
|---|---|---|---|
| `decrypt_work_mult` | colony | multiplies the work of decrypt orders she runs | Iris *Network Engineer* |
| `mood_on_kill_tag` | field → home | mood change when a unit with the tag dies in sight | Iris *Guilt* |
| `no_scavenge` | colony | not selectable for a party unless the mission names them | Iris *Shut-in* |
| `link_port` | story | can carry the key in M16 | Iris, Seven, a fitted survivor |
| `refuses_tier` | colony | cannot lead surgery on parts of this tier or higher | Doc *Oath* |
| `mentor` | colony | counts as a teacher for a skill up to a cap | Doc *Teacher* |
| `party_carry_mult` | field | carry bonus for every unit in the party | Cole *Quartermaster* |
| `expedition_rations` | colony | rations an expedition needs, added | Cole *Quartermaster* |
| `mood_food_surplus` | colony | mood ± by food vs mouths | Cole *Keeps Score* |
| `hack_range` | field | hack from this many tiles | Seven |
| `hack_without_kit` | field | no hacking kit needed | Seven |
| `override_immune` | field | Linked Override cannot target this unit | Seven |
| `humanity_growth_per_day` | colony | humanity rises at home | Seven *Not Human?* |
| `nearby_mood` | colony | mood change for others within a radius, until a humanity | Seven *Not Human?* |
| `network_echo_chance` | field | chance of an extra Linked patrol at the start | Seven *Network Echo* |
| `skill_xp_mult` | colony | XP multiplier for one skill | Pell *Radio Kid* |
| `mood_follows_humanity` | colony | mood cap from colony humanity | Pell *Grew Up Here* |

## New data ids

| Kind | id | Where | Note |
|---|---|---|---|
| enemy | `tally_blade` | M9, Cole's Last Job (enemy) | as `tally_scav` with `machete` + `padded_vest`, Melee 45 |
| item (quest) | `drone_core` | M8 | weight 6 |
| item (quest) | `diesel_engine` | M12 | weight 40; also a vehicle part when the vehicles branch lands |
| item | `surgical_kit` | M15 | main's medicine item (no story copy) |
| item (quest) | `marrow_ledger` | Marrow's Debt | weight 1 |
| item (quest) | `cole_ledger` | Cole's Last Job (enemy) | weight 1; a Codex page |
| item (quest) | `trade_crate` | Dead Drop | paid out as trade goods |
| item | `fuel_can` | M6, Marrow's Debt | the vehicles feature's fuel can |
| character | `tile` / `kit` names | Seven's Name | names only; Seven keeps id `seven` |
