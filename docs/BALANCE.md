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

## Day and night pass

The colony now keeps hours (ARCHITECTURE.md "Day and night"): a 14-hour day shift (06–20), adults asleep 22–06 and
children 19–07, an evening off in between. Before this pass everyone worked all 24 ticks of a day, so every work rate
was rescaled by 24/14 ≈ 1.71 and then the gatherers and the Med Bay were retuned with the bot (20 seeds × 60 days, same
bot; its farm staffing now counts output per day on the schedule, `Schedule.ShiftHoursPerDay`). The bot never turns a
Night Shift on, so these numbers are the plain day/night economy.

Data changes (before → after):

- Base output: Woodcutter 0.55 → 0.96, Quarry 0.5 → 0.88, Scrapyard 0.25 → 0.43, Farm 0.6 → 1.03; Workshop, Tailor,
  Kitchen, Armory, Electronics Lab 1.0 → 1.7 (crafting effort per worker-hour).
- Training `skillPerTick` 0.05 → 0.085, `attributePerTick` 0.01 → 0.017; XP `perWorkTick` 1 → 1.7, `perStudyTick` 1 → 1.7,
  `perTeachTick` 0.5 → 0.85; study `skillPerTick` 0.12 → 0.2, `perkWorkPerTier` 96 → 56 (classes keep the day shift).
- Medical `healPerTick` 0.012 → 0.0155: medics now only work days, so nights are unstaffed care (`unstaffedCare` 0.4) while
  patients still use medicine every tick. At 0.012 two long-term patients ran the medicine out and sat in bed for 30 days
  (seed 2), which pushed Town Hall 2 past day 55 in four seeds.
- Children: they work 12 h (07–19) and keep `childOutputMultiplier` 0.5, so a farming child gives 0.86 of what it did.
- New: `dayNight` in rules.json (hours, sleep, campfire, hot supper, Night Shift −30% output and 0.5 mood/h, sleep debt
  and tiredness −15% output, Floodlight 10 wood + 5 scrap, lit at level 2); `newGame.startHour` 6 (games start at dawn).

20 seeds × 60 days, `main` before → after:

| | before | after |
|---|---|---|
| median population at day 60 | 8 | 8 |
| starvation deaths / departures | 0 / 14 | 0 / 17 |
| expedition deaths | 11 over 335 runs | 7 over 337 runs |
| births | 33 | 37 |
| min food stock (days of need, median) | 4.9 | 4.2 |
| Town Hall 2 / House affordable (median day) | 32.4 / 51.2 | 32.0 / 48.5 |
| Armory / Electronics Lab (median day) | 53.0 / 58.5 | 51.0 / 58.8 |
| first pregnancy / first birth (median day) | 38 / 42 | 36 / 40 |
| first perk point (median day) / survivor-days per level-up | 2.7 / 10.6 | 3.4 / 11.1 |

Economy only (`--no-expeditions`): Town Hall 2 day 20.7 → 21.0, House affordable 33.7 → 33.8, Armory 40.0 → 40.5, Lab
54.0 → 54.5, min food 5.3 → 5.5 days, median population 8 → 9.

- Target 1 holds, a little tighter: the first week now dips to ~21–28 food (seeds 2 and 7; it was ~27–36), with the
  day-2 drop of −6.6 coming from the first night, when nobody farms. Min food 4.2 days is at the low end of 4–6.
- The first night costs a few hours of production, which is why the first perk point slips from day 2.7 to 3.4; the
  level pace after that is unchanged (11 survivor-days per level).
- The Night Shift is the player's lever for a crunch: +7 worker-hours a night at 70%, for ~5 mood a night and a tired,
  15%-slower day. Kept on every night, mood drifts to 0 over a couple of weeks (output ×0.8), so it does not pay for
  itself long-term; a night or two before a build does. Untested by the bot.
- Sleep restores 0.2 mood and 0.3 health per hour in a bed; evenings at the campfire 0.15 mood per hour; a cooked meal
  between dusk and dawn +0.1. Mood only falls from grief and night work, so these mostly speed up recovery after a loss.

### Night missions

A mission is played at the hour its party arrives (`scavenge.json environment`; ARCHITECTURE.md "Day and night"). Night:
people see ×0.6 (drones ×0.8, Husks ×0.8, Linked and cameras unaffected), +15 stealth except against the Linked's
sensors, cameras and turrets, ×1.25 Husks who hear ×1.5 further, flares ×1.25 radius. Dawn and dusk: people ×0.8, +5
stealth. A first try at ×1.5 Husks made night Outskirts 12% deadly and Suburb Ruins 41%; ×1.25 keeps the early zones
inside their targets while night stays the riskier hour for a party that fights its way to every cache.

Zone trials, 300 runs each, two adults with starting gear (`--zone=… --runs=300 --hour=12` vs `--hour=23`; on `main`
after the Linked carbine pass):

| zone | someone dies: day → night | wipe | ambushed / run | lose someone when a Linked shows | ...when none does |
|---|---|---|---|---|---|
| Town Outskirts | 6% → 7% | 1% → 1% | 0.00 → 0.01 | — | 6% → 7% |
| Scrapyard Flats | 8% → 10% | 2% → 3% | 0.00 → 0.00 | — | 8% → 10% |
| Suburb Ruins | 33% → 37% | 12% → 16% | 0.36 → 0.44 | 82% → 68% | 21% → 30% |
| Flooded Mall | 52% → 58% | 22% → 25% | 1.22 → 1.58 | 60% → 69% | 26% → 27% |
| Rail Depot | 92% → 93% | 64% → 62% | 4.47 → 5.44 | 94% → 93% | — |
| Network Relay | 98% → 99% | 86% → 85% | 6.37 → 8.08 | 98% → 99% | — |
| Core Spire | 100% → 100% | 97% → 97% | 6.49 → 9.13 | 100% → 100% | — |

- Against the Linked the night shows as **ambushes**: their sensors still pick the party out while the party's own sight
  shrinks and the Linked's +15 night stealth hides them, so ambushes per run rise by 20–40% from Suburb Ruins up. Flooded
  Mall with a Linked about goes 60% → 69%. Suburb Ruins with a Linked reads 82% → 68%, but that is ~60 runs out of 300 and
  within the noise; the overall Suburb figure (33% → 37%) and its ambushes go up.
- Husk zones get a little worse for this bot (more Husks that hear further), which fights what it finds and walks to
  every cache. The night's upside, slipping past Husks (+15 stealth against them, Husk sight ×0.8), is only partly used:
  the bot does not route around enemies. `Night_hides_people_from_husks_but_not_from_the_Linked` pins the rule.
- Waiting for dawn costs `rationFoodPerMemberPerDay` (2) per day waited per member: a doorstep party leaving at 22:00
  waits 7 h for 0.6 food each, rounded up to 1.

Colony sweep (20 seeds × 60 days) with the bot waiting for dawn rather than arriving at night, vs the day/night pass
above: median population 8 → 7, Town Hall 2 day 32.0 → 31.6, House affordable 48.5 → 48.4, Armory 51.0 → 51.8, min food
4.2 → 3.8 days (the waits' rations), expedition deaths 7 → 9 over 337 → 336 runs, starvation 0.

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

## Story Act 1 pass

`dotnet run --project tools/Remnants.Playtest -- --story --seeds=1..20 --days=50` (the story bot: two strongest adults,
the colony's gear, the first choice in every dialogue, "talk" to the Tally).

| Mission | Closed in 50 days | Median day | STORY.md target | Notes |
|---|---|---|---|---|
| M1 Home | 20/20 | 8 | early Act 1 | needs Farm, Woodcutter, Workshop, Radio Mast, then the dusk scene |
| M2 First Light | 17/20 | 9 | ~day 7–12 | 5 of 22 runs failed (party wipe), retried 3 days later |
| M3 Signal in the Rust | 17/20 | 15 | — | the Tally arrive on round 4; hack lockouts are the main delay |
| M4 The Stranger | 16/20 (4 closed as failed) | 18 | — | Juniper dies in about one run in four; the caravan opens either way |
| M5 Hold the Line | 10/20 | 39 | ~day 30 | gated by Town Hall 2, which the bot reaches late (day 30–47); every run held |

Changes from the first sweep, in which M2 failed 16 of 35 runs with 43 deaths and M4 timed out 12 times:
- M2: the antenna wakes one Husk in the shed. It no longer also sends the patrol at the party.
- M4: one crawler ambushes when Juniper moves, not two. Juniper starts at 30 HP, not 18.
- The bot: it no longer walks out on a teammate who is fighting, and it waits for a bandage before an Aid mission.

Act 1 lands on the doc's timeline up to M4. M5 waits on Town Hall 2, which the bot reaches late, as in the plain
sweeps.

## Linked carbine pass

The fog pass found that a geared party which fights the Linked still loses, and named the Linked carbine as the lever.
The aim: **the carbine is the turning point against the Linked, not a straight upgrade.** A starting party still has
to run. A geared party with carbines and the fog defenses can stand and fight Linked squads in the mid zones and
usually win, though it comes home hurt. Rail Depot, Network Relay and Core Spire stay very dangerous, but two carbines,
armor and good play give a fair chance in the Depot.

Measured with `--zone=… --runs=300` and the kits below. The before numbers are `main` at the hacking-and-drones pass
with only the new kit options added. "Lose someone when a Linked shows" counts runs where the side picked out a Linked.
The kits are `--kit=geared` (pistol / rifle, vest / camo cloak, binoculars, flares, sensor; the bot stands and fights),
plus `--carbines=N` (that many members carry a Linked carbine instead of their gun), `--armor` (everyone wears Linked
plating) and `--carbine-quality=fine|crude`. The bot fires the carbine whenever it has one: it picks the attack with
the best expected damage per AP, and the bane counts in the hit chance it sees.

### What changed

- **Bane** (new in Core, `ActionDef.bane`): an attack's edge against enemies whose `enemies[].tags` contain its tag.
  The Linked, the drones, the Wardens and the cameras are tagged `network`. The carbine's burst now has
  `bane: { tag: network, hitBonus: 0.3, damageMultiplier: 2.0, armorPierce: 4, disruptRounds: 2 }`. Against the
  Network it hits more often, hits twice as hard, ignores the Linked's plating, and knocks a hit Linked off the relay
  for this round and the next (it fights alone, as if jammed). Against Husks it is the same gun as before (Burst
  10–16, reach 8, 1 AP, noise 10, reveal 2). The Linked keep carrying the same carbine (and its model); nothing they
  shoot carries the tag, so the Linked are exactly as strong as before against every party.
- **Quality matters more**: quality tiers now scale a bane (its extra damage and its pierce) too, and the carbine's
  `qualityAccuracy: 0.4` adds accuracy per point of multiplier: Fine +0.10, Crude −0.08. Fine carbines are what make
  the high zones winnable. Crude ones are a trap (Suburb Ruins with 2 crude carbines: 51% someone dies, 28% wipes).
- **Drops** (new in Core, `enemies[].drops`): an enemy leaves only what its drop table rolls, never its equipment.
  Before, every dead Linked left its carbine (a 100% drop). Now: Linked carbine 15% and blueprint 5%; Warden carbine
  60%, blueprint 25% and electronics always; Linked Drone electronics 50%. Caches in the tier-3 zones (Rail Depot and
  up) still roll carbines (loot weight 0.5) and blueprints (0.4). The Armory recipe was already in place: level 3,
  scrap 16, electronics 4, Gunsmithing 30, gated by "Blueprint: Linked Carbine", which is studied at Electronics Lab
  level 2.
- **No ammo.** Core has no ammunition or charge system, and a consumable cannot be tied to an attack with data alone.
  A data-only version would be a pack item that does nothing, so this pass skips it.
- Linked HP, armor, stealth and the squad AI numbers are unchanged. The bane did all the work, so nothing had to make
  the Linked weaker against parties without a carbine.

How the bane got there (2 carbines, someone dies / wipes; Rail Depot with plating):

| bane | Suburb Ruins | Flooded Mall | Rail Depot + plating |
|---|---|---|---|
| none (before) | 37% / 23% | 67% / 49% | 92% / 86% |
| ×1.5 damage, pierce 4, +0.15 hit | 28% / 12% | 45% / 26% | 71% / 56% |
| ×1.75, pierce 4, +0.2 hit, relay knock-off 2 | 26% / 10% | 40% / 18% | 63% / 44% |
| **×2.0, pierce 4, +0.3 hit, relay knock-off 2** | **22% / 10%** | **31% / 11%** | **45% / 27%** |

The relay knock-off was worth only 1–2 points by itself (squads still focus fire with whoever is left on the relay),
but it is what makes a hit felt in the AI's play. The accuracy bonus did the most: survivors start with Firearms 0, so
before this pass they hit a Linked about 40% of the time while the Linked hit them 55–75%.

### Results (before → after, 300 runs each)

**Starting kit**

| zone | someone dies | wipe | lose someone when a Linked shows | Linked kills / run |
|---|---|---|---|---|
| Town Outskirts | 6% → **6%** | 1% → **1%** | — → **—** | 0.00 → **0.00** |
| Scrapyard Flats | 8% → **8%** | 2% → **2%** | — → **—** | 0.00 → **0.00** |
| Suburb Ruins | 33% → **33%** | 12% → **12%** | 82% → **82%** | 0.00 → **0.00** |
| Flooded Mall | 52% → **52%** | 22% → **22%** | 59% → **60%** | 0.19 → **0.19** |
| Rail Depot | 92% → **92%** | 63% → **64%** | 94% → **94%** | 0.19 → **0.17** |
| Network Relay | 99% → **98%** | 86% → **86%** | 99% → **98%** | 0.19 → **0.20** |
| Core Spire | 100% → **100%** | 96% → **97%** | 100% → **100%** | 0.33 → **0.35** |

**Geared, no carbine**

| zone | someone dies | wipe | lose someone when a Linked shows | Linked kills / run |
|---|---|---|---|---|
| Town Outskirts | 6% → **6%** | 1% → **1%** | — → **—** | 0.00 → **0.00** |
| Scrapyard Flats | 11% → **11%** | 4% → **4%** | — → **—** | 0.00 → **0.00** |
| Suburb Ruins | 54% → **54%** | 38% → **38%** | 93% → **94%** | 0.04 → **0.04** |
| Flooded Mall | 81% → **82%** | 66% → **65%** | 87% → **89%** | 0.34 → **0.34** |
| Rail Depot | 98% → **97%** | 96% → **95%** | 98% → **98%** | 0.41 → **0.39** |
| Network Relay | 100% → **100%** | 99% → **99%** | 100% → **100%** | 0.46 → **0.44** |
| Core Spire | 100% → **100%** | 100% → **100%** | 100% → **100%** | 0.53 → **0.52** |

**Geared, 1 carbine**

| zone | someone dies | wipe | lose someone when a Linked shows | Linked kills / run |
|---|---|---|---|---|
| Town Outskirts | 3% → **3%** | 0% → **0%** | — → **—** | 0.00 → **0.00** |
| Scrapyard Flats | 7% → **7%** | 1% → **1%** | — → **—** | 0.00 → **0.00** |
| Suburb Ruins | 35% → **23%** | 22% → **12%** | 91% → **48%** | 0.06 → **0.18** |
| Flooded Mall | 72% → **46%** | 52% → **20%** | 81% → **48%** | 0.48 → **0.95** |
| Rail Depot | 95% → **80%** | 90% → **62%** | 96% → **80%** | 0.57 → **2.08** |
| Network Relay | 100% → **96%** | 100% → **90%** | 100% → **96%** | 0.63 → **2.54** |
| Core Spire | 100% → **99%** | 100% → **99%** | 100% → **99%** | 0.69 → **2.83** |

**Geared, 2 carbines**

| zone | someone dies | wipe | lose someone when a Linked shows | Linked kills / run |
|---|---|---|---|---|
| Town Outskirts | 3% → **3%** | 0% → **0%** | — → **—** | 0.00 → **0.00** |
| Scrapyard Flats | 4% → **4%** | 1% → **1%** | — → **—** | 0.00 → **0.00** |
| Suburb Ruins | 37% → **22%** | 23% → **10%** | 90% → **30%** | 0.07 → **0.23** |
| Flooded Mall | 67% → **31%** | 49% → **11%** | 74% → **31%** | 0.52 → **1.06** |
| Rail Depot | 96% → **63%** | 88% → **40%** | 97% → **64%** | 0.64 → **2.88** |
| Network Relay | 100% → **80%** | 99% → **65%** | 100% → **80%** | 0.66 → **4.29** |
| Core Spire | 100% → **95%** | 99% → **91%** | 100% → **95%** | 0.71 → **4.96** |

**Geared, 2 carbines + Linked plating**

| zone | someone dies | wipe | lose someone when a Linked shows | Linked kills / run |
|---|---|---|---|---|
| Town Outskirts | 1% → **1%** | 0% → **0%** | — → **—** | 0.00 → **0.00** |
| Scrapyard Flats | 1% → **1%** | 0% → **0%** | — → **—** | 0.00 → **0.00** |
| Suburb Ruins | 23% → **7%** | 13% → **1%** | 75% → **12%** | 0.09 → **0.25** |
| Flooded Mall | 48% → **10%** | 34% → **2%** | 60% → **12%** | 0.64 → **1.05** |
| Rail Depot | 92% → **45%** | 86% → **27%** | 95% → **46%** | 0.76 → **3.13** |
| Network Relay | 99% → **71%** | 97% → **57%** | 99% → **71%** | 0.82 → **5.14** |
| Core Spire | 100% → **90%** | 100% → **82%** | 100% → **90%** | 0.82 → **5.95** |

**Geared, 2 Fine carbines** (`--carbine-quality=fine`; after only)

| zone | someone dies | wipe | lose someone when a Linked shows | Linked kills / run |
|---|---|---|---|---|
| Town Outskirts | 0% | 0% | — | 0.00 |
| Scrapyard Flats | 0% | 0% | — | 0.00 |
| Suburb Ruins | 6% | 2% | 14% | 0.25 |
| Flooded Mall | 13% | 3% | 15% | 1.11 |
| Rail Depot | 44% | 20% | 44% | 3.35 |
| Network Relay | 64% | 44% | 64% | 5.45 |
| Core Spire | 81% | 74% | 81% | 6.97 |

### Against the targets

| target | result |
|---|---|
| Starting kit roughly unchanged | **met**: identical to within 1 point (the drop roll shifts the RNG stream a little) |
| 2 carbines, Suburb Ruins: someone dies ≤ 25%, wipes ≤ 5% | deaths **met** (22%); wipes **missed** (10%) |
| 2 carbines, Flooded Mall: someone dies ≤ 25%, wipes ≤ 5% | **missed** (31% / 11%) |
| …the same with Linked plating, or with Fine carbines | **met**: Suburb 7% / 1% and 6% / 2%; Mall 10% / 2% and 13% / 3% |
| Rail Depot, 2 carbines + armor: wipes ≤ 30% | **met** (27%, from 86%); with Fine carbines and no plating 20% |
| Network Relay and Core Spire stay harder than Rail Depot | **met**: 57% / 82% wipes vs 27% (plating); 44% / 74% vs 20% (Fine) |

- **Against the Linked the carbine is the turning point.** In Suburb Ruins, losing someone when a Linked shows drops
  from 82% (starting kit) and 94% (geared, no carbine) to 48% with one carbine and 30% with two. In Flooded Mall the
  drop is 89% → 31%. Linked kills per run go up 3–6× over the geared kit without a carbine. The kill-to-drop rate (15%) means a party that can already
  fight the Linked brings home a carbine every few runs in the Depot. Before that, carbines come from Rail Depot caches,
  from Wardens, or from the Armory blueprint.
- **The misses are Husk fights, not Linked fights.** With 2 unarmored carbines, 20% of Suburb runs *without* a Linked
  still lose someone (starting kit: 21%), and so do 32% of Mall runs without one (starting kit: 26%). In the Mall, runs
  with a Linked are no deadlier than runs without one (31% vs 32%). The geared kit does no better than pipes and spears
  against Husks: a two-handed gun
  loses accuracy fast once Husk claws injure an arm, and the bot walks into melee range to shoot. Making the carbine
  better against Husks would make it a straight upgrade, which this pass was told to avoid. With plating, or with Fine
  carbines, both mid zones meet the targets.
- Not trivial: with 2 carbines, Rail Depot still loses someone in 63% of runs (45% with plating), and Core Spire wipes
  82–91% unless the carbines are Fine.
- Bot tries that did not help, so they were left out: walking back to a downed ally to revive them (deaths unchanged,
  more stalls), and holding overwatch for Husks coming in instead of closing in (geared deaths +9 points).

Watch next: the Husk side of geared play (a ranged unit stepping out of melee before it shoots; Husk accuracy 0.75 +
skill against Firearms-0 survivors); whether Fine carbines (+0.10 accuracy, ×2.25 against the Network) make the Relay too
easy for a late colony; carbine drop pacing in a colony sweep.
## Cybernetics

The prosthetics tree (CYBERNETICS.md). The aims: **a lost limb is a setback, not a death sentence** (crude parts from day
1, mechanical ones mid-game); **powered and cyborg parts are a real power curve** you build toward over months, gated by
Electronics Lab levels, the Biomech → Neural Interface perks and components; **Network-grade is a trade-off**, best
where the Linked are few and no better than cyborg where they are many; and the bill is paid in **humanity**, not only
in scrap. The bot (`ColonyBot`) fits the best part it has to a missing or crude limb (surgery when needed), crafts for
amputees, and — like a curious player — builds one part of each tier as it opens so the next tier can be known. It
never augments electively. Electronics Lab 2 joins the end of its build order.

Progression timeline, 20 seeds × 150 days (`--days=150 --seeds=1..20`), first day a design of the tier can be queued:

| tier | median day | colonies that reach it | what gates it in practice |
|---|---|---|---|
| T0 Crude | 1 | 20/20 | Workshop 1 |
| T1 Mechanical | 81 | 20/20 | Workshop 2 + a Tinkerer (60-day runs: 1 of 20, day 53) |
| T2 Powered | 102 | 17/20 | a T1 part made + an Electronics Lab tech with Biomech |
| T3 Cyborg | 133 | 9/20 | a T2 part made + Neural Interface (Electronics 30, level 5) + Lab 2; or `bp_cybernetics` found in a tier-2 zone |
| T4 Network-grade | — | 0/20 | Linked Salvage needs `bp_network_grade` (tier-3 zones) and Linked cores: late game by design |

The first try had Biomech need Crafting 20 *and* Electronics 15, and Neural Interface Electronics 30 on top: no bot
colony ever had one person with both, so T2/T3 never opened. Biomech now sits on the electronics track (the Lab makes
every powered part). Before the "blueprint **or** knowledge" rule, T3 also needed the blueprint, which a starter bot
never finds (0/20).

Colony sweep (20 seeds × 60 days, `main` → this branch): median population 8 → 7, starvation 0 → 0, expedition deaths 11
over 335 runs → 10 over 336, births 33 → 33, departures 14 → 12, Town Hall 2 day 32.4 → 33.2, House affordable 51.2 →
51.6, Armory 53.0 → 51.4, Lab 58.5 → 58.5, min food 4.9 → 4.5 days. The new loot (components, blueprints) dilutes caches
a little; the drift is within the noise of different expedition outcomes. Limbs are rarely lost by the bot (1 in 20
colonies over 150 days), so surgery and humanity barely show in sweeps; they are covered by tests.

Zone trials, 300 runs, two adults with starting gear plus a cybernetic kit (`--kit=…`; parts fitted on healthy limbs,
so this is the most a party could carry, not what a colony has):

| zone | no kit (`main` → branch) | peg (one peg leg) | mech (one mech leg) | powered (runner / silent pair + blade / precision arm) | cyborg (runner / jumper pair + blade arm / gun arm + scope eye) | network (Network legs, arm, eye) |
|---|---|---|---|---|---|---|
| Town Outskirts | 6% → 6% | 5% | 5% | 2% | 0% | 0% (items 15.9 vs 10.6) |
| Suburb Ruins | 33% → 31% | 33% | 33% | 16% | 6% | 6% (0.5 lock-ups/run at the first try) |
| Flooded Mall | 52% → 50% | 51% | 51% | 31% | 18% | **21%** (4.1 lock-ups/run) |
| Rail Depot | 92% | — | — | — | 60% | **56%** (5.5 lock-ups/run) |

- A peg or mechanical leg on one side barely changes the odds (rounds go up 16 → 20: they are slower, so they loot a
  little more and leave later). That is the intent: crude parts restore a survivor, they do not make one.
- **Powered parts halve the deaths** (Suburb Ruins 31% → 16%, Flooded Mall 50% → 31%): mostly the runner pair's extra
  tile (the bot survives by leaving) and the blade's silent attack. **Cyborg** parts make the mid zones routine and Rail
  Depot survivable (92% → 60%). This is a lot, but it costs a whole body: fitted electively the cyborg kit is 105–140
  humanity (0: detached), and as replacements it needs three or four lost limbs. A colony gets there around day 130+.
- **Network interference** at the first guess (`perLinked` 0.08, max 0.4) locked ~2–3 limbs a run in Linked zones and
  Network-grade still beat cyborg everywhere (Flooded Mall 15% vs 18%, Rail Depot 49% vs 60%). At `perLinked` 0.15,
  `overrider` 0.15, `jammer` 0.2, max 0.6 it is the trade-off it should be: better where the Linked are few (Outskirts
  loot +35%, the hacker arm and stats), level with cyborg where they are many (Mall 21% vs 18%, Rail Depot 56% vs 60%).
  Linked Salvage halves the risk, which tips it back toward Network-grade for a colony that invests.

Surgery (tests and the formula): a fresh medic fits a powered part at 76%, a Medicine 40 Cyber-Surgeon with Surgeon I/II
a cyborg part at 97%; T2 surgery is 24 effort (~1–2 days at a Med Bay 2), T3 48, T4 72, at 0.03 medicine a tick.
Failures leave an infection (torso 0.30–0.55), never a death; at 0 Medicine skill a powered part fails ~30% of the
time.

Rerun after rebasing onto `main` (day-night, Linked carbine pass): 60-day sweep `main` → branch: population 8 → 8,
expedition deaths 7 → 9 over 337/338 runs, births 37 → 36, Town Hall 2 day 32.0 → 32.0, Lab 58.8 → 60.0, min food
4.2 → 4.2 days. 150 days: T1 day 78 (19/20), T2 109 (16/20), T3 137 (10/20). Zone trials match the table above
within 2 points (no kit: Outskirts 6%, Suburb 31%, Mall 50%, Rail Depot 92%; network kit: Mall 21%, Rail Depot 57%).

Watch next pass: T1 at day ~80 feels late for the first amputee (Workshop 2 waits behind the House and Armory in the
bot's order); the runner pair dominates every kit because the bot's answer to danger is running; humanity strain on
couples is unmeasured in sweeps (no bot augments), so its rates (`partnerStrainPerTick` 0.05, breakup below 40) are a
first guess.
