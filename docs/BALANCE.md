# Balance

How the first hour of a fresh New Game is meant to feel, how we measure it, and where the numbers stand.
All numbers come from the headless playtest bot (`tools/Remnants.Playtest`), so they are repeatable: rerun it after
any data edit and compare.

## Time scale

One in-game day is 24 ticks; at 1x a tick is 0.5 s, so **a day is 12 s at 1x, 3 s at 4x**. Missions pause colony
time. With pauses, a few missions and mixed speeds, the first hour of play covers roughly the first 60–120 days, so
the targets below are in days.

## Targets (pass 1)

| # | Target | Measured as |
|---|--------|-------------|
| 1 | The first days are tight but survivable with sensible play | no starvation; food stock dips to about 4–6 days of need; food Δ/day near zero in the first week |
| 2 | Wood and stone drive early decisions | every early build and the Town Hall upgrade wait on wood/stone; people are split between gathering, farming and expeditions |
| 3 | The first expedition to Town Outskirts is risky but usually survivable with basic gear | 2 adults with starting gear: someone dies in ≤ 10% of runs, wipes ≤ 3%, nearly every run comes home hurt |
| 4 | Linked encounters in early zones are rare and scary | none in Outskirts / Scrapyard Flats; Suburb Ruins: seen in ~20% of runs, and a run that meets one loses someone far more often |
| 5 | First births happen around when a House is affordable | median first birth within a few days of the median "House affordable" day |
| 6 | Crafting tiers unlock progressively | Workshop day 1 → Kitchen/Tailor → Town Hall 2 (~day 30) → Armory (~day 50) → Electronics Lab (~day 60); skill-gated recipes reachable by practice |

## The bot

`dotnet run --project tools/Remnants.Playtest -- [--days=N] [--seeds=A..B | --seed=N] [--quiet] [--trace] [--no-expeditions] [--data=DIR]`
plays a fresh colony per seed and prints a daily timeline (population adults/children/infants, beds, food and its
change, stock, buildings, where people work, notable events) plus expedition outcomes and a summary table.
`--zone=ID|all [--runs=N] [--party=N] [--no-defenses] [--kit=fog|geared]` plays many runs of one zone with a fresh colony's first party (two adults,
spear/pipe, padded vest/rag coat, a med-kit and bandages) to read off how deadly it is. Pass `--data=data` to read
the repo's data folder directly instead of the copy made at build time.

What the bot does (a sensible first-time player, not an optimiser):

- **Staffing** every 4 ticks: farmers (children first) until food production covers the need with a margin that
  grows when stock is low; a medic while anyone is a patient; one crafter per building with a queue; the rest on
  the gatherer the next build is shortest of.
- **Build order**: Farm, Woodcutter, Quarry, Workshop, Scrapyard, Med Bay, then upgrades as the Town Hall allows,
  Kitchen, Tailor, Town Hall 2, House, Armory, … Electronics Lab.
- **Crafting**: Herbal Remedy when medicine is low, spears until there are 3 weapons, cloth/bandages/vests,
  meals when food is plentiful, machetes, circuits; studies every blueprint it finds.
- **Care**: admits anyone with a wound that will not heal on its own (≥ `naturalHealMaxSeverity`).
- **Expeditions**: from day 2, every 3 days, at least two healthy adults (never the last adult at home), the best
  gear, med-kit and bandages; the easiest known zone (Outskirts, Scrapyard Flats when Outskirts is picked over),
  threat 2 only after two clean runs. The party bot (`PartyBot`) plays through Core commands: revive/patch allies,
  attack for the best expected damage, recruit strangers, loot the nearest cache, retreat when hurt, loaded, late,
  or on sighting a Linked. It knows where caches are (a player would find them by exploring) but not where enemies are.

## Results

20 seeds × 60 days, same bot and same Core, original data (`main` before this pass) vs. tuned data:

| | before | after |
|---|---|---|
| median population at day 60 | 5 | 7 |
| expedition deaths (20 colonies) | 50 over 262 runs (40 runs with a death) | 18 over 319 runs (15) |
| starvation deaths | 0 | 0 |
| min food stock (days of need, median) | 5.2 | 4.9 |
| Town Hall 2 (median day) | 46 | 31 |
| House affordable (median day) | 54.5 | 47.5 |
| first pregnancy / first birth (median day) | 33 / 37 | 39 / 43 |
| Armory / Electronics Lab (median day) | 59 / never | 50 / 59 |

90 days: before pop 5, 62 expedition deaths, TH2 day 60, no Electronics Lab; after pop 11, 18 deaths, TH2 day 31,
Lab day 68. Without expeditions (economy only): TH2 day 25.5 → 21, House affordable 41 → 35, first birth day 24 both.

Zone trials, 300 runs each, 2 adults with starting gear:

| zone | someone dies (before → after) | wipe | Linked seen in | lose someone when a Linked shows |
|---|---|---|---|---|
| Town Outskirts | 11% → **5%** | 3% → 1% | 0% | — |
| Scrapyard Flats | 17% → **7%** | 2% → 1% | 0% | — |
| Suburb Ruins | 39% → **30%** | 15% → 10% | 36% → **20%** | 74% → 64% |
| Flooded Mall | 48% | 20% | 75% | 55% |
| Rail Depot | 89% | 52% | 98% | 90% |
| Network Relay | 96% | 73% | 100% | 96% |
| Core Spire | 99% | 91% | 100% | 99% |

Outskirts runs still come home hurt nearly every time (~5 new wounds per run, mostly light), which is what makes the
Med Bay and medicine matter. Zones from Flooded Mall up are not meant for a starting party; they were left alone.

A typical tuned colony (seed 2): food dips from 40 to ~26 in the first week while farm, woodcutter, quarry and
workshop go up on day 1; the first Outskirts run leaves on day 2; Med Bay day 7; both children are adults by about day 10;
Kitchen ~12, Tailor ~18; Town Hall 2 day 26; House day 42 (beds 6 → 16); first births right after; Armory day 47.

## Changes in this pass

Data:

- Town Hall L2 70 wood / 45 stone (was 80 / 60): it caps every building's level, so it was the main wall.
- Woodcutter base output 0.55 (0.5), Quarry 0.5 (0.4): stone was the bottleneck.
- Town Outskirts 2–4 husks (3–5); Scrapyard Flats 3–5 enemies (4–6).
- Suburb Ruins Linked weight 0.5, at most 1 (1, at most 2).
- Medical: `healPerTick` 0.012 (0.01), `unstaffedCare` 0.4 (0.25), `medicinePerPatientTick` 0.015 (0.02); Herbal
  Remedy makes 2 medicine (1). Before, two patients ate the starting medicine in two days and then sat in bed for a
  week, so every expedition took two workers out of the economy.
- Electronics Lab L1 costs 40 scrap and no electronics (30 scrap + 5 electronics): electronics only dropped in tier-2
  zones or came from the Lab, so the whole electronics tier waited on a 30%-lethal zone.

Core fixes the bot surfaced:

- **Movement**: a unit whose range is 1 (low Agility, hurt legs, a missing leg, riot armor) could never step onto
  Rubble, Cover or Water and was stuck behind them for good. The first step of a move now always fits.
- **Housing**: a homeless recruit sent on a far expedition "left the colony" on the road, and opening the mission
  then threw. The homeless timer now waits while someone is away.
- **Crafting**: skills only trained while an order was worked and everyone starts at 0, so a queue of only
  skill-gated recipes never moved. Workers now practise while every order needs more skill; the production panel
  names the skill, the gap, the practice time and a recipe they can make meanwhile.
- **Children** never go on a scavenge run: `NewScavenge` rejects them (colony, debug and server paths), `GameHost`
  refuses such a join, and the debug / multiplayer default party is adults only.

Feel / onboarding: first-time tips in the colony log (`Advice.Next`), minors' growth shown as
"child · adult in 3d 17h", the Med Bay shows the medicine rate to three decimals.

## Fog of war pass

The fog of war (stealth vs detection, ambushes, Overwatch / Brace / Lookout, flares, motion sensors; see ARCHITECTURE.md
"Fog of war") changes every fight, so the targets above were re-measured with the same seeds. The bot now only fights
what its side sees, scans (Lookout) when it hears something it cannot see, throws a flare at where an ambush came from,
keeps next to an ally, and ends idle turns braced (after being shot at from the fog, or with a Linked about) or on
overwatch (with contacts around). `--no-defenses` turns that off; `--kit=fog` adds two flares each and a motion sensor;
`--kit=geared` is a crafted kit (pistol / rifle, vest / camo cloak, binoculars, flares, sensor) whose bot stands and
fights the Linked.

Zone trials, 300 runs each, 2 adults with starting gear (before = `main` at pass 1):

| zone | someone dies: before → fog, defenses (no defenses) | wipe | Linked seen | lose someone when a Linked shows | ambushed / run |
|---|---|---|---|---|---|
| Town Outskirts | 5% → **6%** (6%) | 1% → 1% | 0% | — | 0.00 |
| Scrapyard Flats | 7% → **9%** (9%) | 1% → 1% | 0% | — | 0.00 |
| Suburb Ruins | 30% → **32%** (34%) | 10% → 10% (13%) | 20% → 20% | 64% → **73%** (78%); with flares + sensor 74% | 0.61 |
| Flooded Mall | 48% → **50%** (52%) | 20% → 22% | 75% → 74% | 55% → 57% (59%) | 1.80 |
| Rail Depot | 89% → 88% (88%) | 52% → 56% | 98% → 97% | 90% → 90% | 5.05 |
| Network Relay | 96% → 95% (97%) | 73% → 74% | 100% | 96% → 95% | 6.80 |
| Core Spire | 99% → 99% (98%) | 91% → 93% | 100% | 99% → 99% | 7.51 |

- Early zones hold their targets (≤ 10% deaths, ≤ 3% wipes): Husks are easy to see (low stealth, closeRange always
  sees), so ambushes there are nearly zero and the fog mostly shows up as contacts.
- Suburb Ruins with a Linked got scarier (64% → 73% lose someone): the Linked are the ones who hide (+25 stealth), lurk
  on overwatch and open from the fog. The bot's defenses take 5 points off (78% → 73%) and the overall death rate 2
  points; a starter party still mostly survives by running, which is the intended answer to a Linked at that stage.
- "Linked seen" now counts Linked the side actually picked out (not just stood on a visible tile), so it is a little
  stricter than before; the rate is unchanged.

Geared trial (`--kit=geared`, standing and fighting the Linked; defenses on / off): Suburb Ruins 51% / 50% runs with a
death, Flooded Mall 79% / 76%, Rail Depot 97% / 96%. A two-person party that trades fire with the Linked does worse than
one that runs, with or without the fog defenses: their carbine (three 10–16 bursts a turn, armor 4, 70 HP) outclasses a
pistol, so this is base combat balance, not the fog. Overwatch fires (~2.8 reactions per Flooded Mall run) but against
a Linked that shoots from 8 tiles it rarely gets the chance before the damage is done.

Colony sweep (20 seeds × 60 days, before → after): median population 7 → 7, starvation 0 → 0, expedition deaths 18 → 13
over 319 → 323 runs, Town Hall 2 day 31 → 30.7, House affordable 47.5 → 50.5, Armory 49.7 → 49.7, Electronics Lab
58.7 → 51.7, min food 4.9 → 4.5 days; births in the 60 days 34 → 24 (first birth median 43 → 39). Economy-only runs
(`--no-expeditions`) are identical, so the drift comes from expedition outcomes (who comes home when).

Changes made while tuning:

- Overwatch costs 1 AP and keeps the AP left as reactions (max 2). At 2 AP for one reaction it was a bad trade against
  any shooter.
- A revealed unit is seen with line of sight out to 1.5× sight (`revealSightMultiplier`). Before, a Linked firing from
  8 tiles at survivors who see 7 was never revealed, so every burst was an ambush (Suburb Ruins with a Linked hit 86%).
- An ambush takes away positive dodge only (clumsiness still counts).
- Flare / motion sensor / camo cloak loot weights 1 / 0.5 / 0.5 (from 2 / 1 / 1): at the higher weights they diluted
  scrap and cloth drops enough to push Town Hall 2 to day 35.

## Co-op pass

Co-op (MULTIPLAYER.md "Co-op and alliances") puts two or more parties on one team: shared sight, backs covered by the
other party, med-kits and carrying across parties. Without scaling that is simply easier, so the zone's enemy count and
per-type caps scale with the number of players: × (1 + `coop.enemiesPerExtraPlayer` × (players − 1)), caps rounded up.
The aim: **per party, a co-op run is about as risky as a solo run of the same zone**, so teaming up is a choice, not a
cheat.

Measured with `--zone=… --runs=200 --coop=2` (two allied parties, each a fresh colony's first party of two with starting
gear; the PartyBot plays both, revives / bandages / carries allies) against the solo trial on the same build, for three
values of the knob. "Per party, a death" is the share of parties that lost someone; "mission" counts a death on either.

| zone | solo: a death / wipe | ×1.5 per party (mission) | **×2 per party (mission)** | ×2.5 per party (mission) |
|---|---|---|---|---|
| Town Outskirts | 8% / 2% | 2% / 0% (4%) | **4% / 1% (7%)** | 8% / 1% (12%) |
| Scrapyard Flats | 10% / 2% | 3% / 0% (4%) | **7% / 1% (11%)** | 13% / 4% (19%) |
| Suburb Ruins | 34% / 11% | 19% / 3% (28%) | **33% / 13% (46%)** | 45% / 22% (59%) |
| Flooded Mall | 50% / 22% | 41% / 14% (57%) | **63% / 32% (76%)** | 81% / 57% (90%) |

Three allied parties at ×3 (`--coop=3`): Outskirts 4% per party (8% per mission), Suburb Ruins 38% / 13% wiped (57%).

- **×2 is the default** (`enemiesPerExtraPlayer` 1.0, was a first guess of 0.5): Suburb Ruins, the zone where co-op
  matters most, matches solo per party (33% vs 34%); the early zones stay a little gentler (4–7% vs 8–10%), which is fine
  for the zones new players will try co-op in. At ×1.5 co-op roughly halved the risk.
- Flooded Mall gets harder than solo at ×2 (63% vs 50%): its Linked caps (one drone, one Linked) double too, and the
  Linked are what kills a starting party. If that feels wrong, cap scaling could use a smaller factor than the count.
- Allies seeing for each other shows up as fewer ambushes per party; carrying rarely triggers in the bot runs (a med-kit is
  usually at hand), so its tuning (`carryRangePenalty` 1) is unmeasured.
- Versus missions scale the same way (it is the number of players, allied or not).

## Perks and XP pass

Experience, perks and study (see ARCHITECTURE.md "Perks and study"). The goal: a perk point every few in-game days of
focused work — something to look forward to each week, not each hour — with children (who learn faster) a little
quicker and missions worth about a day's work. The bot now spends perk points as they come (a perk for its current job
first, then any open one, lowest tier first); it does not study, since classes take people off work.

`xp`: `perLevel` 96 (level 2 after 4 days of work at 24 XP/day), `perLevelStep` 72 (each level costs 3 more days than
the one before), `perCraft` 4, `perMission` 20 + 10 per threat + 4 per field skill point, children ×1.5.
First try was `perLevelStep` 48, `perMission` 30 + 15/threat + 10 per field skill point: 8.8 survivor-days per level,
mean level 6.1 by day 60, and 473 unspent points over 20 colonies, because gatherers, cooks and tailors had nothing
to spend on. Woodsman, Prospector, Camp Cook, Needlework and Tinkerer (tier 1 job perks) fill that gap.

20 seeds × 60 days, `main` before this branch vs. after:

| | before | after |
|---|---|---|
| median population at day 60 | 7 | 7 |
| starvation deaths / departures | 0 / 13 | 0 / 9 |
| expedition deaths | 13 over 323 runs | 13 over 325 runs |
| births | 24 | 30 |
| Town Hall 2 / House affordable (median day) | 30.7 / 50.5 | 30.4 / 44.7 |
| Armory (median day) | 49.7 | 48.9 |
| Electronics Lab reached (seeds) | 1 of 20 | 3 of 20 |
| min food stock (days of need, median) | 4.5 | 4.8 |
| first perk point (median day) | — | 2.7 (a child farming; adults ~day 4) |
| survivor-days per level-up | — | 10.7 |
| perks learned per colony / mean level at day 60 / max level | — | 18.4 / 5.3 / 7 |
| unspent points (20 colonies) | — | 237 |

Perks raise output by 5–10% in the jobs people stay in, so the economy is a little quicker (House a few days earlier,
more births); food and deaths do not move. Datashards, manuals, drone parts, the hacking kit and the new blueprints add
loot weight 0.3–0.5 each at tiers 1–3; zone lethality is unchanged (no combat numbers moved).

Study (not used by the bot): `skillPerTick` 0.12. Alone, a 50-attribute adult gets a skill from 0 to the self-study
cap of 20 in about 10 days (practising at a job takes ~19); with a mentor about 5 days, and on up to the mentor's level. A tier-1 perk from a mentor takes ~3 days, tier 3
~8. Both cost two people's work, so study is for catching someone up (a child, a recruit) rather than for everyone.

## Watch next pass

- Late food surplus: by day 60 the bot's colonies sit on 100–150 food; Kitchen meals and more mouths absorb some of it.
- Outskirts wounds: ~5 per run. Fine with a Med Bay; before one exists, 30–60% wounds just sit there.
- Children grow up in about two weeks of colony time (a few minutes at 1x); they reach expedition age very early.
- Linked base combat: a geared party that fights them loses (see the fog pass). Their carbine's three bursts per turn are
  the lever (AP cost 2, or fewer bursts), or a better party bot that breaks line of sight, flanks and uses the jammer.
- Fog tuning knobs worth trying first: Linked `stealth` (25), `ambush.hitBonus` (0.2), `overwatch.reactions` (2) and
  the Linked lurking on overwatch (`fog.ai.linkedLurk`).

## Hacking and drones pass

Hackable devices, Override and scout drones (ARCHITECTURE.md "Hacking and scout drones"). The aim: **hacking is a real
choice** — a hacked locker or terminal is worth a cache or more, a camera or turret turned around changes a fight, a relay
stops reinforcements — but a failed hack is loud (alarm, the Linked told where you are on a Network device), hurts
(4–10 shock to the arm) and locks the device for 3 rounds. A drone is information: it pays when the player acts on what it
sees, and draws Linked fire.

Knobs (`scavenge.json`): `hacking.baseChance` 0.55, `perSkill` 0.005, `perIntellect` 0.004, `networkPenalty` 0.15 (so a
fresh hacker with a kit has ~65% on a locker, ~40% on a camera, ~35% on a terminal; Hacking 35 + Hacking I ~80% / ~68%),
`apCost` 2, `alarmRadius` 8, `lockoutRounds` 3, `shockDamage` [4, 10]; per device `work` (locker 1, camera 1, terminal 2,
door 2, turret 3, relay 3) and `difficulty`; drone battery 6 rounds, flight 4 tiles, control range 6 (+1 per 25 Drone
Piloting), Linked shoot it on sight (14 HP). Devices per zone: Outskirts 1–2 lockers and maybe a terminal; Suburb Ruins adds a
terminal, maybe a camera and a door; Rail Depot and up add cameras, turrets and relay nodes.

The party bot (`PartyBot`) hacks lockers and terminals in reach when nothing hostile is in sight and the chance is at least
45% (cameras 40%, relay nodes 50% once the Linked are about), walks to devices worth hacking, overrides an adjacent Linked
with the perk at ≥ 40%, and spends the last AP of a turn flying the drone ahead (recalling it before it runs flat). An
earlier version flew the drone first thing every turn: Suburb Ruins deaths went 33% → 43%, because the operator fell behind
the party. Kits for trials: `--kit=hack` (a hacking kit), `--kit=drone` (a scout drone), `--kit=tech` (both), `--kit=hacker`
(both, with Hacking 35 + Hacking I and Drone Piloting 25 + Drone Operator I + Spotter); `--no-tech` carries them unused.

Zone trials, 300 runs, two adults with starting gear, `main` before this branch → with devices in the zones (no tech):

| zone | someone dies | wipe | items / run |
|---|---|---|---|
| Town Outskirts | 6% → 6% | 1% → 1% | 10.6 → 10.6 |
| Scrapyard Flats | 9% → 8% | 1% → 2% | — |
| Suburb Ruins | 34% → 33% | 9% → 12% | 8.7 |
| Flooded Mall | 49% → 52% | 22% → 22% | 5.6 |
| Rail Depot | 88% → 92% | 58% → 63% | 1.7 |
| Network Relay | 96% → 99% | 74% → 86% | — |
| Core Spire | 98% → 100% | 92% → 96% | — |

Early zones keep their targets (lockers block a tile, nothing more without a kit). From Rail Depot up the cameras call the
Linked in and turrets cover approaches, so the zones that were already not for a starting party got a little worse.

With tech (same 300 seeds; "unused" = the same kit carried with `--no-tech`):

| zone | kit | someone dies | items / run | hacks (failed) / run | devices opened / run | drone lost |
|---|---|---|---|---|---|---|
| Outskirts | unused | 6% | 10.6 | — | — | — |
| Outskirts | hack | 6% | **13.4** | 0.71 (0.47) | 0.7 | — |
| Outskirts | drone | 7% | 11.2 | — | — | 0% |
| Outskirts | tech | 7% | 14.0 | 0.67 (0.36) | 0.7 | 0% |
| Outskirts | hacker | 9% | 14.5 | 1.03 (0.27) | 1.0 | 0% |
| Suburb Ruins | unused | 33% | 8.7 | — | — | — |
| Suburb Ruins | hack | 36% | 9.7 | 0.37 (0.21) | 0.4 | — |
| Suburb Ruins | drone | 32% | 8.4 | — | — | 2% |
| Suburb Ruins | tech | 36% | 9.3 | 0.25 (0.18) | 0.3 | 2% |
| Suburb Ruins | hacker | 41% | 9.6 | 0.45 (0.12) | 0.4 | 1% |
| Flooded Mall | unused | 52% | 5.6 | — | — | — |
| Flooded Mall | hack | 54% | 6.1 | 0.12 (0.07) | 0.1 | — |
| Flooded Mall | drone | 57% | 5.1 | — | — | 6% |
| Flooded Mall | tech | 57% | 5.9 | 0.09 (0.06) | 0.1 | 6% |

- **Hacking pays**: a kit is +25% loot in the Outskirts (a hacked locker spills 2–4 items) at no extra risk there, and +10% in
  Suburb Ruins for ~3 points more deaths — the alarms (about one failed hack every five runs) bring Husks and, on a
  Network device, the Linked. A trained hacker fails far less (27% of tries vs 40%) and opens more, and so stays longer and
  meets more trouble (Suburb Ruins 41%): the payoff is real, and so is the exposure.
- **The drone** in Suburb Ruins cuts deaths in runs that meet a Linked (82% → 67%: the bot sees it coming and leaves) for
  about the same overall rate; in Flooded Mall it draws the Linked (they focus the drone and the squad hunts toward it) and
  is shot down in 6% of runs. The bot does not use what it sees beyond that, so this is a floor on its value.
- **Marks** never fired in these trials (Spotter only marks Linked the drone sees, which the bot then flees); covered by tests.

Colony sweep (20 seeds × 60 days, before → after): median population 7 → 8, starvation 0 → 0, expedition deaths 13 over
325 runs → 11 over 335, births 30 → 33, departures 9 → 14, Town Hall 2 day 30.4 → 32.4, House affordable 44.7 → 51.2,
Armory 48.9 → 53.0, Lab 58.4 → 58.5, min food 4.8 → 4.9 days. The colony bot packs any hacking kit / drone it finds and
sends a spare drone over rumored zones, but rarely has either before day 60; the drift comes from different expedition
outcomes (lockers block tiles, so runs differ), as in the fog pass.

Watch next pass: Rail Depot and up are harder with cameras and turrets; the hack AP cost (2) against the per-device work;
whether a novice should get lockers at 65% (they are not on the Network) or terminals higher than ~35%.
