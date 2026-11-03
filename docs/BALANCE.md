# Balance

How the first hour of a fresh New Game is meant to feel, how we measure it, and where the numbers stand.
All numbers come from the headless playtest bot (`tools/Remnants.Playtest`), so they are repeatable: rerun it after
any data edit and compare.

## Time scale

One in-game day is 24 ticks (a tick is an in-game hour). Real time per tick comes from the **Day length** setting
(Settings → Game: 1 / 2 / 4 / 8 minutes per day at 1x; default 2, so a tick is 5 s): **a day is 2 min at 1x, 1 min at
2x, 30 s at 4x, 15 s at 8x**. Only the real-time pacing changes with the setting; every rule counts ticks, so the
balance in in-game days below holds at any day length and speed. (Before review fixes 1 a tick was 0.5 s, a day 12 s at
1x; the day counts were measured then and are unchanged.) Missions pause colony time. At the default length, an hour of
play at mostly 4x–8x with pauses and a few missions covers roughly 60–120 days; at 1x about 30.

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
| M1 Home | 20/20 | 7 | early Act 1 | needs Farm, Woodcutter, Workshop, Radio Mast, then the dusk scene |
| M2 First Light | 18/20 | 7 | ~day 7–12 | 5 of 23 runs failed (party wipe), retried 3 days later |
| M3 Signal in the Rust | 17/20 | 11 | — | 6 of 23 runs failed; the Tally arrive on round 4; hack lockouts are the main delay |
| M4 The Stranger | 16/20 (4 closed as failed) | 18 | — | Juniper dies in about one run in four; the caravan opens either way |
| M5 Hold the Line | 8/20 | 44 | ~day 30 | gated by Town Hall 2, which the bot reaches late (day 30–48); every run held |

This table is after the rebase onto night missions and cybernetics. Changes from the first sweep, in which M2 failed
16 of 35 runs with 43 deaths and M4 timed out 12 times:
- M2: the antenna wakes one Husk in the shed. It no longer also sends the patrol at the party.
- M4: one crawler ambushes when Juniper moves, not two. Juniper starts at 30 HP, not 18. There are no Linked in the
  Ruins for it (`excludeEnemyTags`, as STORY.md says), which took M4 from 9 failed runs of 18 back to 4.
- Missions in generated zones wait for daylight, since night adds Husks there. Handmade maps and the defense go when
  they go.
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

## Vehicles

Vehicles (ARCHITECTURE.md "Vehicles") trade road time and rations for fuel, noise and a parked stash. All numbers live in
`data/vehicles.json`; the scavenge side in `scavenge.json` (`vehicle`, `salvage`). Tools: `--travel-table`, zone trials
with `--vehicle=TIER` (the party is capped at the vehicle's seats), colony sweeps with `--no-vehicles` or
`--schematics=vehicles` (every vehicle schematic known from day 1). Numbers from the rebased branch.

**Travel days each way, food per member for the round trip, fuel per trip** (`--travel-table`; motor vehicles with
headlights; without them a trip that leaves or arrives at night takes ÷0.75 as long):

| zone | walk | handcart | bicycle cart | motorbike | buggy | truck | armored rig |
|---|---|---|---|---|---|---|---|
| Scrapyard Flats / Suburb Ruins (1 d) | 1.00 · 7 | 1.00 · 7 · 0 | 0.67 · 5.7 · 0 | 0.40 · 4.6 · 2 | 0.33 · 4.3 · 3 | 0.50 · 5 · 6 | 0.57 · 5.3 · 10 |
| Flooded Mall / Rail Depot (2 d) | 2.00 · 11 | 2.00 · 11 · 0 | 1.33 · 8.3 · 0 | 0.80 · 6.2 · 4 | 0.67 · 5.7 · 6 | 1.00 · 7 · 12 | 1.14 · 7.6 · 19 |
| Network Relay (3 d) | 3.00 · 15 | 3.00 · 15 · 0 | 2.00 · 11 · 0 | out of range | 1.00 · 7 · 8 | 1.50 · 9 · 18 | 1.71 · 9.9 · 28 |
| Core Spire (4 d) | 4.00 · 19 | 4.00 · 19 · 0 | 2.67 · 13.7 · 0 | out of range | out of range | 2.00 · 11 · 24 | 2.29 · 12.1 · 37 |

A party of four to the Rail Depot saves 21 food by buggy for 6 fuel (= 10 food at the still's 5 food → 3 fuel), and is home
in under a day and a half instead of four. The truck and rig trade speed for seats, cargo and armor; the rig's 37 fuel to
the Spire is the price of the safest ride there. Extra fuel cans (+1 day range) put the motorbike in reach of the Relay.

**In the field** (zone trials, 200 runs, starting gear; party 3, the motorbike seats 2):

| Suburb Ruins | someone dies | deaths/run | items/run | stashed/run | noise (more Husks) |
|---|---|---|---|---|---|
| on foot | 23% | 0.32 | 11.6 | — | — |
| handcart | 19% | 0.20 | 11.5 | 0.7 | 0 |
| bicycle cart | 19% | 0.20 | 11.5 | 0.7 | 0 |
| motorbike (2) | 35% | 0.45 | 7.5 | 0.5 | ×1.12 |
| buggy | 22% | 0.27 | 11.1 | 0.8 | ×1.18 |
| truck | 27% | 0.34 | 10.6 | 0.6 | ×1.25 |
| armored rig | 30% | 0.42 | 10.3 | 0.6 | ×1.30 |

- A quiet vehicle helps: the parked handcart/cart is an extraction point next to the spawn, so a hurt party gets out sooner
  (deaths 0.32 → 0.20). The motor tiers pay for their noise in Husks: a first pass at noise 0.25 / 0.35 / 0.5 / 0.6 took
  Scrapyard Flats from 7% to 30% runs with a death by truck; halved (0.12 / 0.18 / 0.25 / 0.3) the truck is 9% → 15% there and
  the buggy is about even with walking in the Ruins. The muffler (×0.5 noise) and smoke launcher (no extra Husks) are the
  answer for a player who wants both speed and a quiet arrival. The motorbike row is a party of two, hence the deaths.
- The bot stashes little (0.5–0.8 items a run): it only unloads when it walks past the vehicle loaded with materials. A
  player who ferries salvage to the stash brings home a cargo's worth more; the stash's value is in the hands, not the bot.
- Salvage: 1 roll in tier 1 zones, 2 in tier 2–3, +1 with a winch; engines and chassis only with the vehicle parked.

**Colony sweeps** (60 days, 20 seeds; 120 days, 10 seeds): on foot vs with vehicles the colonies match within noise (60 d:
TH2 29.5 vs 30.5, population 8 vs 8, expedition deaths 8 vs 8 over ~330 runs; 120 d: population 20 vs 16 with every vehicle
schematic, TH2 29.0 vs 29.8). The bot's Garage spends scrap and a worker slot early; it builds vehicles only with scrap to
spare, so even with all schematics known it rarely gets past the handcart (a bicycle cart by day ~97 in one seed of ten).
That is the bot's caution, not a gate: the build costs are 15 wood + 2 wheels (handcart) up to 50 scrap + 6 wheels, 3
frames, a diesel and 6 plates (rig).

**Watch next pass**
- A bot strategy that commits to a Garage line (parts → motorbike → buggy) to measure motor tiers in a real colony.
- Wear (3% a day each way) and repairs (a spare part = 25% of the hull or 50% of a part) are gentle; nothing has broken down
  in the sweeps because the bot drives little.
- Upgrade quality spread (Crude ×0.8 … Fine ×1.25) is cosmetic at today's numbers for small effects (tyres ×1.05).
## Schematics pass

Blueprints became schematics (SCHEMATICS.md). Colony sweep, 20 seeds × 60 days, `main` → this branch (default bot):
median population 7 → 8, Town Hall 2 day 31.0 → 30.0, House affordable 48.4 → 50.4, Armory 51.8 → 52.6, Lab 57.6 → 59.5,
expedition deaths 9 → 12 over ~330 runs. No early gate moved. The bot also runs the hardest allowed zone every other
expedition now (`Stretch`), and `--bold` climbs threat after two runs without a death.

Schematic measures (default bot, which rarely leaves threat 1): first schematic day 11.8 (20/20); known at day
30/60/90 1 / 3 / 4; 150 days: Town Hall 3 and Armory + Lab L2 in 18/20 colonies with the default set plus loot.
The default bot reaches tier 2 rarely (2/20 by day 150) because it hardly ever runs Ashgrove. With `--bold`: first tier-2
schematic day 25.6 (20/20), known at day 90: 8, sources Loot 105 / Trade 27 / Research 5, 74 trader visits; the bolder
climb costs deaths (30 vs ~13 in 90 days) and delays TH2 to day 34.

Zone trials (schematics brought home per run, 300 runs; target from SCHEMATICS.md): Outskirts 0.24 (0.25), Scrapyard
0.22 (0.3), Suburb Ruins with a hacker 0.39 (0.5), Flooded Mall with a hacker 0.25 (0.5), Rail Depot (2 carbines,
plating) 0.36 (0.8), Network Relay 0.31 (1.0). Deeper zones fall short mostly because trial parties die there. Changed:
tier 1–2 cache odds 2% / 4% → 3% / 5%, locker odds 3% / 5% / 6% → 4% / 6% / 7%.
Watch next: Research Data piles up (374 gained over 150 days, little researched) because the heard-of list stays
short without trader visits. A heard-of source for tier-1 zones may be needed.

Story sweep after merging schematics into Story mode (`--story --seeds=1..20 --days=50`; Act 1 pass above in
brackets): M1 20/20 day 7 (20, 7), M2 19 day 7.3 (18, 7), M3 19 day 11.4 (17, 11), M4 17 closed, 5 as failed, day 17.4
(16, 18), M5 7 day 37.8 (8, 44). Story rewards are the main schematic source in Story mode (53 of 96 learned), and the
first tier-2 schematic comes by day 34 in 11 of 20 colonies.


## Medicine

The illness model and its supplies (ARCHITECTURE.md "Medicine", `data/medicine.json`). The aims: **the early game stays
as it was** (herbal remedies and bandages cover the first days, and the defaults include antiseptic and splints);
**infection is a real but manageable threat** (a dirty wound left alone goes bad in a few days, costs output and can
kill, but a dressing prevents it and antibiotics or a patient bed on Medicine handle it); and **the advanced supplies are
a field edge, not a crutch** (a blood bag gets the downed up with 45 HP instead of 15, a stim is one AP now and one
less next turn).

Key numbers (first pass):

| what | number | why |
|---|---|---|
| infection chance | 0.005/h per dirty wound of severity ≥ 0.25; bandaged ×0.35, clean ×0, patient ×0.5 | ~1 in 8 untreated wounds goes bad in a day |
| infection | starts at 0.15–0.3, grows 0.003/h (about a week to the 0.75 health drain); −0.35 output at full | slow enough to see coming |
| on Medicine at the Med Bay | an infection fades at 0.003/h | herbal remedies hold it back without antibiotics |
| fracture | 35% when a limb hit leaves it at ≥ 0.45; −0.25 efficiency, heals ×0.5; splinted −0.08, heals ×1.25 | a splint is worth 2 AP in the field |
| pain | worst injury ≥ 0.55: −15% output; no field AP cost (see below) | |
| fever in the field | infection ≥ 0.5, sickness ≥ 0.4–0.6: −1 AP (never more than 1, never below 2 AP) | sick people should stay home |
| painkiller | ignores 60% of injury penalties for 4 rounds / 24 h; 3 doses close together start a dependency (−0.08 mood/h × severity, fades in ~4 days) | mild and self-limiting; the Med Bay never doses someone near it |
| food / Husk sickness | −30% / −45% output; fade 0.012 / 0.003 per hour; Husk sickness drains health from 0.7 | Husk meat is a real gamble |
| blood bag | revive with 45 HP (med-kit 15); the donor gives 20 health and needs 70+ | |
| surgery kits | Surgical +0.12 (any tier), Suture +0.05 (T0–T2), × quality | a fresh medic's powered-part odds 76% → 88% |
| quality | Crude ×0.8, Fine ×1.25 on heal, revive HP, cure strength, pain relief | Pharmacist adds +0.15 to the quality score |

Pain in the field: the first pass took 1 AP from anyone with a wound of 0.55 or worse. Zone trials (300 runs, two
adults, starting gear) put Suburb Ruins deaths at 31% (`main`) → 37%, Flooded Mall 50% → 59%: a hurt survivor got
slower, got hit again and went down, a death spiral. At a threshold of 0.8 it was still +3/+4 points; without the AP
cost Suburb Ruins is back at 30%. So pain costs output at home and nothing extra in the field (the wound itself already
does); fevers keep their −1 AP, as a reason not to send the sick. Fractures (35%) changed nothing measurable (38% vs
37%). With the shipped data, rebased on `main` (story and schematics in): Outskirts / Suburb Ruins / Flooded Mall runs with a
death 4% / 31% / 52% (`main` before the rebase: 6% / 31% / 50%).

Colony sweep, 8 seeds × 40 days (`--seeds=1..8 --days=40`, `main` → this branch, after the rebase): median population
6 → 6, starvation 0 → 0, expedition deaths 3 over 76 runs → 2 over 88, births 2 → 6, departures 1 → 2, Town Hall 2 day
30.0 → 31.5, min food 4.3 → 3.9 days. Medicine line: 7 infections over the 8 colonies (first on day 24, median), all
cured, none fatal; 100 treatments given at home, 53 supplies crafted at the dispensary; no sicknesses (food-cooking is
not merged); no blood donations (no bot colony reaches Med Bay 2 in 40 days). The bot crafts antiseptic, splints and dressings once the Med Bay stands, antibiotics while anyone is infected, distils
spirits when alcohol runs low, admits the infected and sick, and dresses the wounds of those working through them; in the
field it revives with the strongest kit, splints broken limbs and takes one painkiller when in pain.

The fog-of-war checker in the multiplayer tests now accepts an actor its side saw when the event was logged (FogLog's
`SeenBy` rule): the first pass's pain −1 AP changed a co-op fight so that a watcher went down in the middle of an AI batch, which the
older, stricter check flagged though the server did what FogLog documents.

Watch next pass: no bot colony reaches Med Bay 2 in 40 days, so antibiotics, blood bags and stims are covered by tests,
not sweeps; the infection rate is tuned for a colony that keeps a Med Bay, and one without may want antiseptic earlier
in the bot's build order; when food-cooking lands, Husk sickness odds need a pass of their own.

## Weather pass

Weather is a seeded Markov chain of spells per region (`data/weather.json`, `Colony/Weather.cs`), on whenever the
file is present, so every colony sweep now runs with it. At home: rain slows outdoor work ×0.9 and waters the farm ×1.3;
a thunderstorm ×0.7 outdoors, costs mood and can strike a building with lightning (half output until repaired for 6
wood + 3 scrap); fog costs a little mood; a dust storm ×0.3 outdoors (×0.65 with a dust mask), keeps children in, burns
unmasked outdoor workers' health and the crop ×0.6. On the road: storms add days, a dust storm closes routes two days or
more out. In the field (`scavenge.json environment.weather`): rain and storms cut hearing (×0.6 / ×0.5) and flare time,
storms add thunder (no footsteps) and lightning (everyone seen); fog halves people's sight and gives +10 stealth; a dust
storm cuts sight hard (people ×0.45, Linked ×0.6), +10 stealth except to Husks, −15% ranged hit chance, grounds drones
and halves relay range.

The bot now copes with it: it waits when a dust storm closes the route (`RouteBlocked`) rather than picking another zone,
packs `ArrivalPlan.ExtraRationsPerMember` (the wait for dawn plus weather delays), repairs struck buildings as soon as
it can afford to, and has the Tailor make two dust masks. The colony report adds a line for the weather at home,
lightning strikes and repairs. Zone trials take `--weather=KIND[,KIND...]|all` (held for the whole run at intensity
0.8, no changes mid-run), and `--hour` takes a list too; the two sweep every combination.

```sh
dotnet run --project tools/Remnants.Playtest -- --data=data --seeds=1..40 --days=60 --quiet                    # weather on
dotnet run --project tools/Remnants.Playtest -- --data=NOWEATHER --seeds=1..40 --days=60 --quiet               # data/ copy, weather.json "enabled": false
dotnet run --project tools/Remnants.Playtest -- --data=data --zone=suburb_ruins --runs=300 --hour=12,23 --weather=all
```

### Colony sweep

40 seeds × 60 days, weather off → on (same bot, same Core; days are medians over the colonies that got there):

| | weather off | weather on |
|---|---|---|
| median population at day 60 | 7 | 7 |
| starvation deaths / departures | 0 / 36 | 0 / 36 |
| expedition deaths | 22 over 671 runs | 23 over 660 runs |
| births | 76 | 74 |
| min food stock (days of need, median) | 3.8 | 3.8 |
| Town Hall 2 / House affordable (median day) | 31.5 / 48.4 | 32.6 / 49.3 |
| Armory / Electronics Lab (median day) | 51.5 / 59.3 | 50.5 / 59.0 |
| first pregnancy / first birth (median day) | 35 / 39 | 31 / 34 |
| weather at home | — | clear 47%, overcast 31%, rain 15%, fog 4%, thunderstorm 3%, dust storm 1% |
| lightning strikes / repairs | — | 37 / 36 (0.9 per colony) |

Economy only (`--no-expeditions`, 20 seeds): Town Hall 2 day 21.0 → 22.5, House affordable 33.8 → 35.5, Armory 40.5 →
42.5, Lab 54.5 → 56.8, min food 5.5 → 5.6 days, births 74 → 63, median population 9 → 8.

| target | with weather |
|---|---|
| 1 First days tight but survivable | **holds**: no starvation, min food 3.8 days as before (the low end, as since the night pass) |
| 3 Outskirts usually survivable | **holds**: a death in 4–7% of runs in any weather by day, 2–7% at night (below) |
| 4 Linked rare in early zones | **holds**: none in Outskirts; Suburb Ruins sees one in 17–21% of runs (27–29% in a storm) |
| 5 First births near House affordable | **holds**, about as loosely as before (births still come before the House) |
| 6 Crafting tiers unlock progressively | **holds**: Town Hall 2 ~day 33, Armory ~50, Lab ~59 |

Weather costs the economy one to two days on every milestone (outdoor work lost to storms and lightning at half pace
until repaired), which the expedition colonies hide in their own noise. No `weather.json` numbers were changed. Dust
storms are rare at home (1% of hours, Home Valley ×0.6), so masks and the dust's health loss barely show here.

### Missions in weather

Zone trials, 300 runs per cell, two adults with starting gear, `--hour=12,23 --weather=all` (one weather for the whole
run at intensity 0.8). Someone dies, day → night; wipes and items per run in brackets:

| weather | Town Outskirts | Suburb Ruins | Rail Depot |
|---|---|---|---|
| clear | 6% → 7% (2% → 1%; 10.6 → 10.6) | 33% → 40% (12% → 17%; 8.6 → 7.8) | 92% → 92% (64% → 62%; 1.6 → 1.6) |
| overcast | 5% → 6% | 34% → 39% | 92% → 92% |
| rain | 4% → 5% (1% → 1%; 10.8 → 11.2) | 31% → **26%** (12% → 9%; 8.7 → 9.7) | 93% → 93% (63% → 58%; 1.6 → 1.8) |
| thunderstorm | 6% → **2%** (1% → 0%; 10.7 → 11.4) | 38% → **22%** (10% → 4%; 8.0 → 10.0) | 91% → 89% (59% → 52%; 1.6 → 2.1) |
| fog | 4% → **3%** (0% → 0%; 11.1 → 11.5) | **20% → 17%** (5% → 6%; 10.4 → 10.6) | 85% → 83% (49% → 46%; 3.0 → 3.4) |
| dust storm | 7% → 6% (1% → 1%; 10.8 → 10.8) | 28% → 33% (9% → 11%; 9.3 → 8.7) | **74% → 72%** (**39% → 37%**; 3.9 → 4.5) |

Rail Depot, ambushes per run day → night: clear 4.52 → 5.42, rain 4.89 → 5.55, storm 4.31 → 4.67, fog 4.45 → 4.81, dust
3.36 → 3.41. Suburb Ruins, losing someone when a Linked shows (day): clear 80%, rain 72%, storm 58%, fog 63%, dust 62%.

The same Rail Depot trial with the geared kit and two Linked carbines (`--kit=geared --carbines=2`), someone dies day →
night (Linked kills per run): clear 63% → 84% (2.94 → 2.23), rain 59% → 79% (2.69 → 2.43), thunderstorm 54% → 75%
(2.90 → 2.55), fog 67% → 87% (2.62 → 2.21), dust storm 62% → 83% (2.52 → 2.18).

- **Dust storms are an opening against the Linked for a party that runs.** In Rail Depot a starting party dies in 74%
  of dust-storm runs (92% clear), wipes 39% (64%) and brings back 2.4× the loot: the Linked's sight ×0.6, +10 stealth
  and halved relay range cut ambushes by a quarter. It does nothing for Husks (they smell through it): Suburb Ruins in a
  dust storm is about as deadly as clear, and at night a little worse.
- **For a party that fights the Linked, dust and fog are no help**: the −15% ranged hit chance and the shorter sight
  cost as much as the stealth gains (Linked kills down 10–15%); rain and storms help a little (deaths −4 to −9 points).
- **Fog is the safest weather against Husks**: Suburb Ruins 33% → 20% by day, 17% at night, with more loot (+20%), as
  the party slips past what it cannot see either. It also blunts Rail Depot (85%).
- **Rain and storms turn the night around.** Hearing ×0.6 / ×0.5 and thunder hide the party's footsteps from Husks that
  hear ×1.5 further at night, so a stormy night in Suburb Ruins (22%) is safer than a clear day (33%), and the Outskirts
  at night drop from 7% to 2%. By day rain changes little, and a thunderstorm in Suburb Ruins reads a little worse
  (38%), with a Linked seen in 29% of runs instead of 20% (lightning shows everyone to everyone).
- Overcast is clear for every purpose (within a point).
- The early-zone targets hold in every weather: Outskirts deaths stay at 2–7%, wipes ≤ 2%.

Watch next: the bot does not use the forecast (it never times a run for fog or a storm, nor holds back from one); dust
masks and the dust's health loss barely show at 1% dust hours at home; a geared party might want to hold its fire in
dust (−15%) rather than trade shots.
## Food and cooking

Food is now ingredients in a pantry (ARCHITECTURE.md "Food and cooking"). The aim: the early game stays as tight as
before, and the late surplus the day/night pass flagged gets something to do (it spoils, unless it is cooked,
preserved or kept cold). Consumption rates are unchanged; potatoes are 1 food each and the Farm's potatoes yield what
it made before, so the economy's base is the same.

Data (food.json, recipes.json, buildings.json):

- New game: 40 food → 26 potatoes, 8 beans, 3 canned food (40 food value). Potatoes keep 14 days, beans 30, corn 20,
  meat 3, Husk meat 2, dishes 2–5, preserved food 30–180.
- The Farm's first harvest comes in `growDays` after it is built or switched (potatoes 1, herbs 1, beans 3, corn 4);
  the effort meanwhile is not lost (it is the standing crop). Beans yield ×0.85, herbs ×1.6 (0.2 food each).
- Cooking stretches food: Boiled Potatoes 3 potatoes + water → 4.2 food (×1.4), Stew ~4 food → 6.4, Roast 3 → 4.
- Herbal Remedy and Plant-fiber Cloth take `tag:plant` (any crop) in place of the old food pool, same amounts.
- Buffs: Well Fed ×1.08 output and ×1.25 healing (8–24 h), Comfort +0.1 mood/h, Energised ×1.15 then 4 h sleep debt,
  Soothed ×1.5 healing; Hungry ×0.85 output and ×0.75 healing for 12 h after starving, Malnourished ×0.9.
- Bot: food staffing counts each harvest's food value; it cooks while food lasts over 3 days and keeps about two days
  of dishes, smokes meat and dries produce about to spoil, packs field rations or a good meal for expeditions, and builds
  the Hunting Lodge, Smokehouse and Kitchen L2 after the Lab. A first try with an early Campfire (and its cook) and
  the food buildings before the Armory pushed Town Hall 2 from day 31 to 34; the bot now leaves them later.

20 seeds × 60 days, before (`main`) → after:

| | before | after |
|---|---|---|
| median population at day 60 | 7 | 7 |
| starvation deaths | 0 | 0 |
| min food stock (days of need, median) | 3.8 | 3.6 |
| Town Hall 2 / House affordable (median day) | 31.0 / 48.4 | 31.8 / 48.4 |
| Armory / Electronics Lab (median day) | 51.8 / 57.6 | 49.6 / 56.8 |
| first pregnancy / first birth (median day) | 40 / 44 | 31 / 35 |
| births / departures | 37 / 13 | 35 / 22 |
| expedition deaths | 9 over 324 runs | 2 over 320 runs |
| food rotted / food poisonings / batches cooked per colony | — | 58 / 0 / 103 |
| cooked share of the diet / mean nutrition at the end | — | 82% / 0.58 |

Economy only (`--no-expeditions`): Town Hall 2 day 21.0 → 19.6, House affordable 33.8 → 31.5, Armory 40.5 → 36.6, Lab
54.5 → 51.5, min food 5.5 → 5.4 days, median population 9 → 8, 109 food rotted per colony.

- Target 1 holds: seed 2's first week dips from 40 to 17.1 (was 19.2) on day 6, then recovers; nobody starves in any
  seed. The day-1 drop is larger (−9.5 vs −5.2) because the new farm's potatoes come in on day 2.
- Town Hall 2 timing is within a day; the cooked food's mood and Well Fed output make up for the cook's hours.
- More departures (13 → 22): homeless recruits and newborns. Recruits went 39 → 43 and the colonies conceive earlier
  (food value in dishes counts for the "enough food for babies" check); housing, not food, is the wall there.
- Expedition deaths fell (9 → 2): good meals for the road give +1 AP on the first round. Watch it in zone trials
  before reading too much into 20 colonies.
- Rot: a bot that over-farms loses ~1 food a day per colony late on. A player has the Smokehouse, Cold Cellar
  (schematic), beans and corn, or simply farming less.

Watch next: the Hunting Lodge and Husk-zone game (the bot never hunts Husk game, so taint is untested in the sweep),
the Greenhouse and the Cold Cellar (both behind schematics the bot does not find pre-merge), and variety (the bot
cooks two or three dishes; Same Again rarely triggers).

### After merging story-schematics, story-campaign and weather

Same sweep (20 seeds × 60 days), `main` with schematics, story and weather vs this branch on top of it: median
population 8 → 8, Town Hall 2 day 29.5 → 28.8, House affordable 46.8 → 44.5, Armory 50.8 → 47.5, Lab 59.0 → 58.6, min
food 4.1 → 3.7 days, starvation 0 → 0, departures 15 → 11, births 36 → 34, expedition deaths 7 → 12 over 317 → 328 runs
(within the noise of 20 colonies; zone trials are the better read), food rotted 42 per colony, food poisonings 0. Weather
now waters or buries the crops per harvest (`Crops.Multiplier` in `HarvestSystem`; the Greenhouse is indoors) instead of
scaling every farming worker, and rain or storms skip the Campfire's supper bonus as they skip campfire evenings.

## Electronics and power

Bot changes: the colony bot builds a Fuel Generator after Electronics Lab 2 (solar and a bank once their schematics are
known), runs the Lab as a bench (strip junk except Linked cores, keep a few wiring and boards, then two radios, noise and
holo-decoys, EMPs and a drone variant), and sends radios in pairs, EMPs and decoys spread over the party, and a medic drone
first (never a drone the pilot lacks the perk for). The party bot throws an EMP over two or more Network machines (or one
Linked within 3), a holo-decoy between itself and a Linked when hurt, a noise decoy at full range away from the exit when
two Husks close in, heals with a medic drone, fires a combat drone and ferries a full cargo hold. New measures: a `power:`
line in the colony report, and `--kit=electronics` / `--kit=electronics-combat` zone trials.

### Colony (`main` with schematics, story and weather → this branch, 90 days)

| | seeds 1–20 | seeds 21–40 | all 40 |
|---|---|---|---|
| median pop | 14 → 11 | 12 → 14 | — |
| TH2 / Armory / Lab day | 29.5 / 50.8 / 65.5 → 30.8 / 54.5 / 72.5 | 31.5 / 52.6 / 69.5 → 29.5 / 53.0 / 67.5 | — |
| expedition deaths | 9 / 475 → 15 / 470 | 19 / 472 → 10 / 480 | 28 → 25 |

- The early game does not move beyond seed noise: the two halves swing in opposite directions (the extra loot and drops
  shift every RNG stream). Before the rebase (20 seeds × 60 days) it read TH2 31.0 → 30.6, Armory 51.8 → 49.8, Lab
  57.6 → 56.6. No generator means no grid and nothing new in the log. The new tier-1 loot (broken gadgets, wiring,
  boards, dead radios, noise decoys, radios) was halved after the rebase to about 6% of the tier-1 loot weight, so it
  does not crowd out scrap and food.
- Power is a mid-game boost: by day 90 no bot colony has a generator; over 150 days (before the rebase) 16/20 built one
  (median day 117), with no brownouts or breaker trips, 7 064 powered Lab hours, 333 disassemblies and 220 devices made.

### Zones (300 runs each, party of 2, starting gear; death rate / wipes; before the rebase)

| Zone | plain | + electronics kit | + electronics-combat kit | kit carried, unused |
|---|---|---|---|---|
| Suburb Ruins | 33% / 11% | 30% / 12% | 35% / 12% | — |
| Flooded Mall | 51% / 23% | 59% / 26% | 62% / 27% | 51% / 19% |
| Rail Depot | 93% / 63% | 93% / 63% | 93% / 62% | — |

Kit: two handheld radios, an EMP, a holo-decoy, a noise decoy and a medic (or combat) drone. Per run in the Flooded Mall:
0.55 EMPs, 0.53 holo-decoys, 0.6 noise decoys, 0.5 drone heals (or 0.75 drone bursts).

- **The noise decoy first made things worse** (Suburb wipes 11% → 19%): Husks only turned to it while idle, so it woke the
  map around the party. Now Husks leave a hunt for it unless their prey is adjacent, and the bot throws it away from the
  exit: Suburb Ruins is down to 30% / 12%.
- In the Flooded Mall the kit brings home 21% more items (6.9 vs 5.7) and the runs last two rounds longer (the bot stays
  while a drone keeps healing and a decoy covers it), which costs more lives than the devices save. Carried but unused, the
  kit changes nothing (51%). This is mostly the bot staying too long; a player who leaves when the decoy goes up gets the
  benefit. Watch next: a "decoy up → withdraw" rule for the bot before retuning device numbers.
- After the rebase (schematics, weather on main): plain Suburb Ruins 32% / 10% → 32% / 9%, Flooded Mall 53% / 23% →
  53% / 25%; with the electronics kit 38% / 12% and 55% / 27% — the same pattern (more loot, longer runs).
- The Rail Depot is out of reach for a starting party with or without devices (as before: it needs carbines).

### Watch next

- The generator comes very late for the bot (day ~117); a player builds it right after Lab 2. If that is too late in
  practice, move it before Town Hall 3 in the build order.
- Once the weather branch feeds `Power.SolarWeatherFactor`, check solar arrays at dusk and in storms against the bank size.
- With story-schematics merged, recheck the electronics drop odds against the "first tier-2 schematic by day ~35" target.
## Gear mods

Weapon, armor and clothing mods, Mk II / III kits and helmets (ARCHITECTURE.md "Gear mods"). The aims: **the early game
and the Linked carbine pass stay where they were** (mods are something a colony grows into); **mods are the answer to
the geared kit's known weakness, the Husk fights** (BALANCE.md "Linked carbine pass": a two-handed gun losing accuracy once
claws injure an arm); **the stealth choices are real choices**, a weapon light or a suppressor each paying for itself
and costing something; and a fully modded party still loses people in the Iron Corridor and the Black Grid.

The bot (`GearModder`) fits mods the way a careful player would: per slot the mod that scores best for the run (expected
damage per AP with banes where they bite, noise for a party trying to stay hidden, a light only at night and never
for a stealthy party, worn gear by armor, padding, carry and stealth against plates' weight and noise). The colony bot
crafts default-set mods (iron sights, sharpened edges, padding, webbing) once it is armed and has scrap and cloth to spare,
fits them and any kits at a bench before each expedition, and takes a helmet when it has one. Zone trials take
`--mods=basic` (the default set and a scrap helmet), `full` (Mk II where a kit exists, every survivor-made mod), `stealth`
(the same chosen to stay hidden) or `network` (full plus the EMP coil and Network weave).

Tuning on the way (300 runs, `--kit=geared --carbines=2 --mods=full`, runs with a death / wipes):

| pass | Suburb Ruins | Flooded Mall | Rail Depot | Network Relay |
|---|---|---|---|---|
| unmodded (this branch) | 22% / 11% | 35% / 17% | 62% / 38% | 83% / 70% |
| first numbers (2 / 3 / 4 slots, plates +1.5 / +3, reflex +0.08, Mk II +12%) | 1% / 0% | 2% / 0% | 26% / 6% | 47% / 26% |
| plates halved, smaller accuracy and Mk steps | 3% / 0% | 4% / 0% | 35% / 15% | 60% / 41% |
| **one slot at Mk I (firearm, armor, clothing 1 / 2 / 3)** | **5% / 0%** | **5% / 0%** | **37% / 15%** | **63% / 45%** |

Ablations on the last line (200 runs, one effect switched off at a time: incendiary rounds, armor from plates / Mk /
helmet, every accuracy bonus, reach, the Mk damage step, padding, camo, camo + quiet fabric) all land within 1–4 points of
it; with every effect neutral (the same builds, no numbers) it is back at 26% / 35%. The mods overlap: armor, padding,
accuracy and stealth each fix the same weakness (Husk fights: runs without a Linked go 21–31% → 0–4%), so no single
number is the lever. The Linked are not overcome by mods alone (below).

### Results (300 runs each; "before" = this branch without mods, which matches the carbine and night passes)

**Starting kit** — unchanged by the branch (melee: the after-moving penalty is for ranged attacks):

| zone | someone dies | wipe | with basic mods + scrap helmet |
|---|---|---|---|
| Town Outskirts | 6% | 2% | 4% / 1% |
| Scrapyard Flats | 7% | 1% | 6% / 0% |
| Suburb Ruins | 33% | 10% | 28% / 9% |
| Flooded Mall | 51% | 22% | 46% / 19% |
| Rail Depot | 93% | 65% | 92% / 62% |
| Network Relay | 99% | 86% | — |
| Core Spire | 100% | 97% | — |

**Geared kit** (no carbine; before → full mods): Outskirts 5% → 0%, Scrapyard Flats 12% → 1%, Suburb Ruins 57% / 40% →
22% / 12%, Flooded Mall 79% / 64% → 54% / 36%, Rail Depot 99% / 98% → 95% / 90%. Losing someone when a Linked shows stays
73% (Suburb) and 64% (Mall): without a carbine the Linked still win.

**2 carbines** (before → full → network):

| zone | before | full mods | network mods (EMP coil, weave) |
|---|---|---|---|
| Suburb Ruins | 22% / 11% | 5% / 0% | 2% / 0% |
| Flooded Mall | 35% / 17% | 5% / 0% | 8% / 0% |
| Rail Depot | 62% / 38% | 37% / 15% | 30% / 9% |
| Network Relay | 83% / 70% | 63% / 45% | 51% / 28% |
| Core Spire | — | — | 76% / 62% |

**2 carbines + Linked plating**: Rail Depot 48% / 22% → 14% / 6% with full mods, Network Relay 69% / 52% → 35% / 23%,
Core Spire → 68% / 57%.

**Stealth trade-offs** (geared kit, full mods vs stealth mods, which take a suppressor and quiet fabric and never a light):

| | Suburb Ruins | Flooded Mall | ambushed / run (Suburb, Mall) |
|---|---|---|---|
| day, full | 22% / 12% | 54% / 36% | 0.22, 0.50 |
| day, stealth | 23% / 13% | 56% / 40% | 0.20, 0.50 |
| night, unmodded | 89% / 78% | 98% / 92% | 0.23, 0.89 |
| night, full (weapon light) | 55% / 41% | 80% / 67% | 0.27, 0.81 |
| night, stealth (no light) | 54% / 36% | 81% / 68% | 0.25, 0.98 |

### Against the targets

| target | result |
|---|---|
| Starting kit unchanged | **met**: within 1 point of the carbine pass in every zone (Outskirts 6% / 2%, Suburb 33% / 10%, Mall 51% / 22%) |
| Carbine pass targets intact (the same kits without mods) | **met** within the noise: 2 carbines Suburb 22% / 11% (22% / 10%), Mall 35% / 17% (31% / 11%: the after-moving penalty costs a shooting party ~4 points), Rail Depot + plating 48% / 22% (45% / 27%) |
| Default-set mods are a step, not a leap | **met**: Suburb 33% → 28%, Mall 51% → 46% |
| Stealth trade-offs meaningful | **met**: a light and staying dark are level at night (55% vs 54%, 80% vs 81%), each by a different road (the light sees what comes; the dark is not seen, but is ambushed more in the Mall: 0.98 vs 0.81 a run); by day the suppressor build and the loud one are level |
| Iron Corridor and Black Grid stay dangerous | **met**: fully modded 2-carbine parties lose someone in 37% of Rail Depot runs and 63% of Network Relay runs; with plating 14% / 35%; Core Spire 68–76% |

Colony sweep after rebasing onto `main` (schematics, story, weather), 60 seeds × 60 days in three blocks of 20, the same
data without the gear-mod files → with them (the bot crafting and fitting default-set mods; `--no-mods` turns that off):
expedition deaths 32 over 977 runs → 34 over 966, departures 53 → 60, births 112 → 112, starvation 0 → 0; per block
median population 8 / 7 / 8 → 7 / 8 / 8, Town Hall 2 day 29.5 / 31.5 / 28.6 → 32.5 / 31.8 / 27.8, Lab 59.0 / 57.5 / 58.6
→ 60.1 / 59.0 / 59.3, min food 4.1 / 3.7 / 4.2 → 4.0 / 3.7 / 4.0 days. One block of 20 alone swings by more than this
(expedition deaths 6–19 per block on the same data), so nothing moved outside the noise. The mods cost a few scrap and
cloth once the colony is armed.

Rerun after the rebase (300 runs): starting kit Outskirts 4% / 1%, Scrapyard Flats 10% / 2%, Suburb Ruins 34% / 11%, Flooded
Mall 51% / 23%, Rail Depot 92% / 63%; basic mods 3% / 1%, 6% / 1%, 26% / 8%, 48% / 17%; 2 carbines 20% / 8%, 34% / 16%,
63% / 41% → full mods 3% / 0%, 4% / 0%, 37% / 15% (Network Relay 57% / 39%); geared full mods Suburb 21% / 14%, Mall 54% /
35%; at night full (light) 55% / 35% and 80% / 69% vs stealth (dark) 53% / 36% and 84% / 72%. All within a few points of the
tables above.

After the second rebase (food and cooking, electronics and power; 300 runs): starting kit Outskirts 4% / 1%, Scrapyard
Flats 10% / 2%, Suburb Ruins 34% / 10%, Flooded Mall 50% / 23%; basic mods Suburb 27% / 9%, Mall 49% / 19%; 2 carbines 21% /
8%, 37% / 17%, Rail Depot 64% / 37% → full mods 2% / 0%, 5% / 0%, 35% / 10%. Colony sweep (20 seeds × 60 days, `--no-mods`
→ the bot modding): expedition deaths 12 over 331 → 9 over 330, departures 23 → 24, Town Hall 2 day 31.8 → 30.8, Lab 59.5 →
58.1, min food 3.7 → 3.7 days, starvation 0 → 0.

Watch next: a fully modded mid-game party with two carbines makes Suburb Ruins and Flooded Mall routine (5%). That
needs Armory 3, two carbines, Mk II kits and tier-2 schematics, so it is late by design, but if it feels too safe the
levers are the slot counts (Mk II 2 → 1 for firearms) and the Mk II armor step. The weapon light is never turned off (no
toggle yet). The bot does not use Jury-rig in the field.


## Raids

Colony raids and crafted defenses (data/raids.json; design in ARCHITECTURE.md "Raids and defenses"). Raids auto-resolve
from a strength calculation; the bot (`DefenseBot`) builds defenses, posts guards and preps every warned raid.
`--no-raids` runs the same bot without raids for comparison.

### Targets

- The first raid comes after the basics are up (day 8+, five buildings) and is survivable with a few traps and a tower:
  held in every seed.
- After that, a raid every week or so, growing with the colony's noise, wealth and the raids it has survived.
- A lost raid hurts (stock, injuries, the odd wrecked building), but rarely kills; a colony that invests holds most.
- The early economy keeps its shape: Town Hall 2 a few days later at most.

### The numbers

| What | Value | Why |
|---|---|---|
| Grace | day 12 and five buildings (day 8 before full-game pass 1) | basics first; pressure builds meanwhile, so the first raid comes soon after |
| Threshold / gap | 150 pressure (was 130), ≥ 6 days | one raid a week; the others' pressure ×0.25 after any raid |
| Husk pressure/h | 0.2 + 0.1 · noise | noise ≈ 0.5 per survivor + night shifts + floodlights at night + returns (gunfire) − lures |
| Raider pressure/h | 0.1 + 0.03 · wealth (stock/50 + items/5), from day 10 | rich colonies draw bands |
| Linked pressure/h | 0.05 · Network presence (scouted Linked zones' threat × (1 + day/30)), from day 25 | grows with the Network's reach |
| Size | min + pressure/100 · sizePerPressure, × (1 + 0.1/raid + 0.01/day past grace); first raid ≤ 2 (was 3) | escalation |
| Strength per attacker | Husk 5, raider 6, Linked 9 | ×1.25 Husks, ×1.1 raiders at night |
| Defender | 8 · (0.5 + 0.5 · combat/50) · health · arms + weapon power · 0.6 + armor · 0.8 | gear from the inventory, best to the best |
| Night | defenders ×0.75 (a lit approach recovers 75% of the loss); asleep ×0.5 unless an Alarm Bell | towers, bells and lights matter at night |
| Guard at a post | ×(1.25 + post bonus + Sentry 0.25) | and +2 h warning per guard (max 6) |
| Walls | soak share = min(0.6, soak / (soak + attack)); an open gate halves it | close the gates in prep |
| Pressure p | attack / (attack + defense); held when p < 0.5 | spoils from p 0.4, injuries p² · 1.0 per defender, deaths above p 0.6 |

### Results (20 seeds × 60 days, bot)

    median: pop 7, first pregnancy day  34.0, first birth day  38.0, house affordable  51.8, TH2  37.0, armory  55.5, lab     -, min food days 3.8
    raids: seed  raids held firstDay firstHeld dead taken hurt stolen wrecked defenses guards kinds
             1      5    3  11.2       yes    0     0    2     10       0        6      0 HHHHR
             2      5    5  11.3       yes    0     0    2      7       0        6      0 HHHHR
             3      5    3  11.2       yes    0     0    1      8       0        6      1 HHHRL
             4      6    5  10.8       yes    0     0    6     20       0        6      1 HHHRHR
             5      6    5  10.8       yes    0     0    4     45       0        6      0 HHHRHR
             6      3    1  11.2       yes    0     0    4     26       0        6      0 HHH
             7      5    1  11.8       yes    2     2    3     14       1        6      0 HHHLL
             8      6    6  10.8       yes    0     0    7     22       0        6      0 HHHRHR
             9      5    2  11.8       yes    0     0    4     30       0        6      0 HHHHR
            10      6    4  11.8       yes    0     0    5     23       0        6      1 HHHRHR
            11      5    2  11.8       yes    1     0    5     70       1        6      0 HHHRH
            12      6    6  11.2       yes    0     0    4     13       0        6      1 HHHRHR
            13      6    3  10.8       yes    0     0    2     35       0        6      0 HHHRHR
            14      6    6  11.8       yes    0     0    3     13       0        6      0 HHHRHR
            15      5    3  11.2       yes    0     0    3     15       0        6      0 HHHRH
            16      4    1  11.3       yes    0     2    2      4       2        6      0 HHLL
            17      6    5  11.8       yes    0     0    6     22       0        6      0 HHHRHR
            18      6    5  10.8       yes    0     0    7     35       0        6      0 HHHRHR
            19      5    1  11.2       yes    2     1    1     16       2        6      0 HHHLL
            20      6    6  11.8       yes    0     0    7     15       0        6      1 HHHRHR
    raids: 107 in all, 73 held; first raid day 11.2 (median), first held 20/20; dead 5, taken 5, hurt 78, stolen 443, wrecked 6
    totals: starvation deaths 0, expedition deaths 16 over 295 expeditions (16 with deaths), departures 19, births 30, early Linked sightings 0, recruits 38
    

Without raids (same bot): Town Hall 2 at day 31 (median); with raids at day 37. Every first raid was held; 68% of all raids
were held; 10 survivors were lost to raids over 1,200 colony-days (5 killed, 5 taken by the Linked late on). Most of the
economic cost is labour: a guard is a worker lost, so the bot posts guards only from 7 adults (posting one from 3 adults
pushed Town Hall 2 to day 44, injuries mattered far less). Husks are most of the raids (night, from the north); raiders
come up the road once the stock grows.

### Fought raids (`--fight`: StoryBot plays every raid on the colony map)

20 seeds × 60 days: 108 raids, 90 held (83%; auto-resolve 68%), first raid held 20/20, no raid deaths, population 120 at the
end (auto-resolve 122), Town Hall 2 at day 35.6 (median). The first pass killed 86 survivors: defenders bled out or were
finished where they fell, and field wounds came home above the worsen threshold and killed later (41 "untreated" deaths).
Two rules fixed it: in a raid battle a downed defender is dragged to safety at once (`FieldMission.RescueDowned`), and
wounds come home dressed (`battle.woundCap` 0.45, below `medical.worsenThreshold`). The report's "hurt" counts every injured
part from the field, so it reads high next to auto-resolve's one line per survivor.

### After the merge with schematics, weather, food, power, trading and gear mods (20 seeds × 60 days)

| Run | Raids | Held | First held | Lost (dead + taken) | Population at the end | Town Hall 2 (median day) |
|---|---|---|---|---|---|---|
| auto-resolve | 108 | 79 (73%) | 18/20 | 3 | 131 | 36.6 |
| `--fight` | 106 | 75 (71%) | 20/20 | 0 | 115 | 35.8 |
| `--no-raids` | — | — | — | — | 163 | 30.8 |

Raider wealth now counts the pantry's food value (as the stock it replaced) instead of food items, which had doubled
raider pressure (131 raids, 54% held). The two first raids lost in auto-resolve broke through without deaths or losses
(two of six adults were patients at night). Defense schematics now come from the catalogue (loot, trade, research and beaten
raids); weather cuts warnings (rain ×0.8, fog ×0.6, thunderstorms ×0.7, dust storms ×0.5); powered turrets and floodlights
get their bonus; Husks trample standing crops; modded gear and helmets count in a defender's strength.

### Watch next pass

- Population with fought raids (115) trails auto-resolve (131): field wounds keep defenders in the Med Bay longer.
- Guards are strong but expensive for a small colony; a part-time watch (a rota) may be the better early choice.
- The bot spends only on essentials before Town Hall 2; walls and turrets come late. Players who wall in early will find
  raids easy — that is intended, but check the wall soak cap if raids feel toothless.
- Raider bands field the Tally's `tally_scav` (pistol, padded vest) in fought battles; they may want a melee variant.

## Field loot and salvage

Kills now leave loot most of the time and maps have salvage nodes (ARCHITECTURE.md "Field loot and salvage"). The party
bot takes whole piles when they fit, salvages nodes it has seen when it is healthy, nothing has been heard for two
rounds, the run is young (≤ round 24) and it has 5 kg of room under 80% load, and drops its least valuable material
over 85% (where the second encumbrance step makes it crawl). `--no-salvage` turns salvage off.

Loot per kill (find tier 1, `LootCatalog.Expected`): Husk empty 20%, ~1.5 items (scrap, cloth, canned food, bandage,
flare, sometimes a crude pipe or machete, rarely a Data Shard → 1 Research Data); Crawler empty 30%, ~1; Tally
scavenger ~2 plus their pistol 30% of the time (crude 2:1); Cole always pistol, med-kit, food, scrap and more; cameras
and turrets electronics / scrap / parts; the Linked keep their cores, carbines and schematics and add one salvage pick.
Each threat tier above 1 adds +0.35 picks, +15% quantity, −3% empty and ×1.25 rare chances.

Building materials are heavy on purpose: Salvaged Wood 2.5 kg → 2 wood, Stone Blocks 3 kg → 2 stone, a Concrete Chunk
4 kg → 2 stone, against a carry capacity of ~21–27 kg with 7–10 kg of gear on.

### Zone trials (200 runs, 2 adults, starting gear; resources carried home per run)

| zone | before: deaths / items / scrap, cloth, food | after: deaths / items | after: scrap, cloth, food, wood, stone, electronics | salvage actions |
|---|---|---|---|---|
| Town Outskirts | 4% / 11.1 / 4.4, 1.9, 2.3 | 6% / 16.6 | 7.4, 4.3, 3.4, 2.3, 1.0, 0.6 | 3.6 |
| Scrapyard Flats | 10% / 11.7 / 5.4, 2.2, 3.3 | 15% / 15.7 | 8.6, 4.0, 3.7, 0.9, 0.8, 0.7 | 2.9 |
| Suburb Ruins | 32% / 8.4 / 2.6, 1.2, 1.7 | 35% / 14.1 | 5.3, 4.5, 3.1, 1.4, 0.3, 2.0 | 1.2 |
| Flooded Mall | 55% / 5.5 / 1.8, 0.8, 0.9 | 56% / 9.6 | 4.2, 2.9, 1.6, 0.5, 0.2, 1.8 | 0.6 |
| Rail Depot | 94% / 1.6 / 0.4, 0.1, 0.4 | 94% / 2.1 | 0.9, 0.5, 0.4, 0, 0.1, 0.3 | 0.2 |

Enemy loot alone (`--no-salvage`): Outskirts 5% deaths, 13.6 items, scrap 5.4 cloth 3.3 food 3.3; Scrapyard 16%, 14.1
items, scrap 6.9. So kills add ~2.5 items per early run and salvage another ~3 (mostly wood and stone), at the price of
longer runs (rounds 15 → 19 in the Outskirts) and a little more risk: the deaths drift up by a point or two, within the
noise of 200 runs except the Scrapyard (+5), where the Crawlers and the noise of the cars add up.

### Colony sweep (20 seeds × 60 days)

| | before | after |
|---|---|---|
| median population at day 60 | 8 | 9 |
| Town Hall 2 (median day) | 29.5 | 28.6 |
| House affordable / Armory / Electronics Lab (median day) | 46.8 / 50.8 / 59.0 | 44.7 / 47.8 / 54.8 |
| first birth (median day) | 51 | 47 |
| min food stock (days of need, median) | 4.1 | 4.2 |
| expedition deaths | 7 over 317 runs | 12 over 326 runs |
| starvation deaths | 0 | 0 |

Against the targets: the first week stays tight (food minimum unchanged), wood and stone still come from the Woodcutter
and Quarry (a run brings ~3–5 of each against ~10 per worker-day at home, and costs two adults for days), Town Hall 2
stays around day 30 (−1 day). Scrap and electronics are what speed up: the Armory comes 3 days and the Lab 4 days
earlier. A first draft gave scrap 9–11 per run and noisy, greedy salvaging: TH2 day 26.8 and 19 expedition deaths; the
careful salvaging and less scrap from Husks and cars brought both back.

Watch next: expedition deaths (12 vs 7) are the cost of longer runs; if they stay high, trim salvage `noise` or give the
bot a stricter "nothing heard" window. Wood and stone from the field are deliberately small; a player who hauls (stash
at the exit, come back) gets more than the bot, which never stashes.

### Assets still placeholders

- Model `loot_bag` (a kill's remains / dropped items; now a 0.65× loot crate), a dedicated glint effect.
- Sounds `loot_glint` (a pile appearing), `salvage_wood`, `salvage_metal`, `salvage_stone` (salvage uses `debris_rattle`
  for now), `item_drop` (drop and stash use `loot_pickup`).
- Props reused for nodes: `rubble`, `dead_tree`, `ruins_house_shelf` (furniture), `ruins_house_bed` (bedding),
  `wrecked_car`, `building_shell` (scaled 0.45), `terminal_room_server_rack` (dead electronics).

## Full-game pass 1

Stabilisation pass 1, with every feature on main and on in the sweep: raids, weather, nights, food, medicine, vehicles,
field loot and salvage, schematics, and in Story the campaign through Act 4.
`dotnet run --project tools/Remnants.Playtest -c Release -- --data=data --seeds=1..20 --days=90 [--story]`. "Before" is
main at the start of the pass (da62114); "after" is the end of it.

### 20 seeds × 90 days

| | Sandbox before | Sandbox after | Story before | Story after |
|---|---|---|---|---|
| median population at day 90 | 10 | 10 | **0** (19 of 20 wiped) | 3 (8 of 20 wiped) |
| Town Hall 2 (median day, colonies that got there) | 37.5 | 40.6 | 61.0 (1/20) | 68.6 (8/20) |
| expedition deaths | 15 over 489 runs | 19 over 481 | 82 over 178 | 79 over 362 |
| raids / held / first held | 197 / 105 / 15 of 20 | 178 / 110 / 18 of 20 | 169 / 13 / 9 of 20 | 126 / 27 / 10 of 20 |
| raid dead / taken / buildings wrecked | 9 / 2 / 11 | 3 / 0 / 3 | 6 / **20** / **192** | 13 / 6 / 52 |
| first raid (median day) | 11.8 | 13.8 | 11.9 | 14.1 |
| starvation deaths | 0 | 0 | 1 | 0 |

Town Hall 2 over 20 colonies is too noisy to judge raid tuning (one seed moved from day 26 to 50 on an unrelated
change). The raid numbers below use 60 seeds × 60 days and the mean day, with a colony that never gets there counted as
day 61:

| 60 seeds × 60 days | TH2 mean (± s.e.) | median | never | first raid held | raid dead |
|---|---|---|---|---|---|
| raids as they were | 44.7 ± 1.6 | 37.0 | 13 | 49/60 | 4 |
| raids after this pass | 41.2 ± 1.4 | 38.5 | 8 | 57/60 | 2 |
| `--no-raids` | 38.6 ± 1.6 | 34.1 | 10 | — | — |
| `--no-salvage` (raids after) | 46.0 ± 1.4 | 43.7 | 13 | 56/60 | 7 |
| `--no-expeditions` (raids after) | 26.3 ± 0.2 | 26.5 | 0 | — | — |

Seeds 61–120 confirm the raid change: 44.8 ± 1.7 → 41.2 ± 1.5.

### What changed

- **Story Act 2 waited for nothing.** M6 (The Long Haul) and M7 (The Drowned Clinic) needed only M4 (and Marrow's visits
  or a Med Bay), so they opened around day 16, half an act early. The bot took M7 with Act 1 gear and lost the whole party
  in most seeds. Both now also need **Town Hall 2**, like M5. STORY.md puts Act 2 at days 30–65 on the Town Hall 2
  milestone. `StoryActsTests` checks the gate.
- **The Linked raided Story colonies to death.** Linked raid pressure counted every *known* Linked zone. A Story colony
  hears of the Flooded Mall and the Rail Depot by about day 16 (M4 and Lantern's talk), which tripled the Network
  presence. From day 25 nearly every raid was a Linked probe (kinds `HHLLLLLLLL`), and each lost probe took up to two
  survivors: 20 taken and 192 buildings wrecked over 20 colonies. Network presence now counts only **scouted** zones,
  where the colony has been or flown a drone (`ThreatModel.NetworkPresence`). A rumor draws nothing. The fix brought
  taken survivors from 45 to 2 on the first re-run.
- **A later, lighter first raid** (raids.json `scheduler`): grace 8 → 12 days, first raid ≤ 3 → ≤ 2 attackers,
  threshold 130 → 150. The cost of raids to Town Hall 2 roughly halves (+6.1 → +2.6 days over no raids, mean), first
  raids held go 49 → 57 of 60, raid deaths 4 → 2, and there are 11% fewer raids. Other variants, none better: grace 12
  alone 42.0 (first raid held only 37/60, because a later raid is a bigger one), first ≤ 2 alone 43.6, plus
  `minGapDays` 8 41.3, plus `escalationPerDay` 0.005 41.0.
- **Bot only:** the story bot sends survivors on a mission only at health 90+ (was 70). That took M2 failures from
  9 to 4 of about 25 runs.
- **New tool:** `--mission=ID [--runs=N] [--hour=H,…] [--weather=…] [--th=N]` (`MissionTrial`) plays one story mission
  with a fresh colony's first party.

### The questions this pass was asked

- **Expedition deaths 7 → 12 at the food merge: not food.** By arrival phase, day arrivals had 12 deaths in 414 runs
  and night arrivals 3 in 85, so night is not the cause. Nobody fell sick (0 sicknesses in the sweeps), and the food
  merge's meals *add* a first-round AP. The rise is field loot and salvage: Outskirts zone trials were 4% runs with a
  death before loot, 5% with enemy loot only, 6% with salvage (longer runs, 15 → 19 rounds). Colony sweeps: 13 deaths
  with salvage against 7 without, over ~320 runs each. The rest is noise. Repeated 20 × 60 runs of the same build gave
  12, 13 and 16, and the Poisson spread at that size is ±3.5. Two candidate fixes didn't move it:
  - salvage noise −1 on every node: 12 deaths, Outskirts still 6%
  - a stricter salvage window in the bot (health 80%, nothing heard for 3 rounds, stop at round 20): 16 deaths

  The cost is intended (risk for materials), so salvage stays as it is.
- **Raids and Town Hall 2:** see above. TH2 ~day 32–33 is out of reach through raids alone, because the no-raid economy
  itself is now at a 34-day median (it was 30.8 before vehicles, medicine, loot and story). Vehicles are about 1 to 3
  days of that (60 seeds: 35.8 without them, raids on): the handcart's runs keep two adults out longer. Expeditions as a
  whole are the big lever: without them Town Hall 2 comes on day 26.
- **Loot and salvage don't flood the early economy.** Salvage brings Town Hall 2 about 5 days *earlier* (46.0 → 41.2),
  but colonies that never send anyone out reach it on day 26. In the bot's hands, early expeditions cost more labour than
  they bring home, and loot only partly pays for the adults' time away. Wood and stone still come from the Woodcutter and
  Quarry. Stolen stock in raids is unchanged (2,193 → 2,219 over 60 colonies).
- **Story pacing against STORY.md** (median day a mission closes, colonies that close it):

  | Mission | target | before | after |
  |---|---|---|---|
  | M1 Home | early Act 1 | 7.8 (19/20) | 9.3 (20/20) |
  | M2 First Light | day 7–12 | 9.6 (16/20) | 11.1 (18/20) |
  | M3 Signal in the Rust | Act 1 | 11.4 (15/20) | 14.4 (18/20) |
  | M4 The Stranger | Act 1 | 18.4 (9/20) | 17.4 (13/20) |
  | M5 Hold the Line | ~day 30 (TH2) | — (0/20) | 64.0 (8/20) |
  | M6 / M7 (Act 2) | days 30–65 | 31.9 / 28.3 (3 / 2 of 20, most parties lost) | 75.4 / 79.8 (3 / 1) |

  Act 1 is on time up to M4. From M5 on, the story waits for Town Hall 2, and a Story colony reaches it much later than
  a Sandbox one (day 68 against 41 in this sweep), so Act 2 starts around day 64–75 instead of 30.

### Still open (next Story balance pass)

Taken up in "Full-game pass 2" below.

The Story colony is far weaker than the Sandbox one with the same bot. Median population is 3 against 10. Expedition
deaths are 79 in 362 runs against 19 in 481, and raids held 21% against 62%. What we know:

- The story missions kill in the colony far more than in a trial. `--mission=m2 --runs=200` with a fresh party: 95% done,
  0.25 deaths a run, the same at 02:00, 07:00, 12:00 and 19:00. In the colony sweep it was about 1 death a run (24 in
  25 runs) before the health-90 rule. Colony parties go out hurt, and a hurt arm drops the hit chance to 12–19% (seen in
  the M2 trace).
- M6 and M7 are threat-2 fights. Even at Town Hall 2 the bot brings starting-tier gear: 27 deaths in 15 runs. They may
  want a gear check in the bot, a lighter first wave, or a second retry tier.
- Half the Story colonies never place a defense: the build order leaves no wood spare, and DefenseBot only spends what
  the next build goal does not need.
- The repeatable Dead Drop contract (every 7 days) ties up 2–3 adults about four times per colony. Taking it only once
  changed little: Town Hall 2 68.6 → 59, still median population 2.

## Full-game pass 2

Story survival and early pacing, from pass 1's "Still open". Measured with
`dotnet run --project tools/Remnants.Playtest -c Release -- --data=data --days=90 --seeds=1..60 [--story]`, 60 seeds
(20 were too noisy for Town Hall 2 in pass 1), and confirmed on seeds 61–120. "Before" is `main` at 6075bd1, "after"
is the end of this pass. Each change below is marked **game** (data or rules, what a player gets) or **bot** (the
playtest bot, changed only so it plays like a careful player would; none of it is in the game).

### 60 seeds × 90 days

| | Sandbox before | Sandbox after | Story before | Story after |
|---|---|---|---|---|
| median population at day 90 (mean) | 11 (10.9) | 11 (11.0) | 4 (3.2) | **6** (5.8) |
| colonies wiped out | 0 | 0 | 17 | **1** |
| Town Hall 2: median day of those that got there (how many) | 38.0 (57) | **34.6** (55) | 65.7 (16) | **41.6** (49) |
| Town Hall 2: median day, never = 91 | 38.5 | **35.6** | 91 | **43.8** |
| expedition deaths / runs | 37 / 1467 | 41 / 1459 | 193 / 1096 | **101 / 1424** |
| raids / held / first held | 545 / 372 (68%) / 57 | 543 / 477 (**88%**) / 57 | 376 / 88 (23%) / 33 | 468 / 265 (**57%**) / 40 |
| raid dead / taken / wrecked | 12 / 0 / 10 | 4 / 0 / 6 | 39 / 13 / 86 | 14 / 14 / 29 |
| starvation deaths | 0 | 0 | 6 | 1 |

Sandbox raids held 68% → 88% is mostly the bot: on a warning it now lays barricades and strips on that side.

Seeds 61–120, same builds:

| | Sandbox before | Sandbox after | Story before | Story after |
|---|---|---|---|---|
| median population (wiped) | 11 (0) | 10 (0) | 3 (19) | **6** (1) |
| Town Hall 2 median, never = 91 (reached) | 37.8 (56) | **34.6** (59) | 91 (21) | **45.7** (52) |
| expedition deaths / runs | 43 / 1459 | 38 / 1461 | 211 / 1107 | **85 / 1452** |
| raids held / first held | 71% / 53 | 82% / 54 | 19% / 24 | **60%** / 48 |

Story missions (seeds 1–60; median day a mission closes, colonies that close it, deaths over all runs):

| Mission | before | after |
|---|---|---|
| M1 Home | 7.0 (59) | 6.8 (59) |
| M2 First Light | 8.7 (54), 43 dead in 66 runs | 8.9 (58), 8 dead in 64 |
| M3 Signal in the Rust | 12.4 (51), 30 in 57 | 13.5 (58), 6 in 76 |
| M4 The Stranger | 20.4 (35), 6 in 49 | 17.1 (48), 13 in 64 |
| M5 Hold the Line (end of Act 1) | 66.0 (16), 10 in 16 | **41.9** (46), 4 in 46 |
| M6 The Long Haul | 68.2 (8), 21 in 16 | 61.4 (39), 14 in 42 |
| M7 The Drowned Clinic | 70.9 (5), 22 in 12 | 70.5 (38), 3 in 38 |
| M9 The Tally | 70.4 (5) | 73.4 (23) |
| Dead Drop (repeatable) | 261 runs, 19 dead | 123 runs, 1 dead |
| The Fence Line (new task) | — | 17.0 (57) |

### Against the goals

| Goal | Result |
|---|---|
| 1 Story population within ~20% of Sandbox; expedition deaths close | **Not met.** 6 against 11 (was 4 against 11; wipes 17 → 1). Deaths per run 0.18 → 0.07 (Sandbox 0.03). |
| 2 Sandbox TH2 ≈ 32–35, Story ≈ 35–40; Act 1 by ~30–40, Act 2 by ~70 | Sandbox **met** (34.6 / 35.6). Story 41.6 among those that get there (65.7 before), 44 with the late ones: close, not met. Act 1 ends at M5 on day 42–46; Act 2 (M6, M7, M9) on days 61–73. |
| 3 Raids held ≥ 60% in both; first raid survivable | Sandbox **met** (88%). Story 57% / 60% on the two seed ranges (was 23%), first raid held 40 and 48 of 60. |
| 4 Keep tension | Deaths still happen in every mission and every mode; nothing is free: a short party meets lighter waves but trials still lose 1 a run in M7 with three. |

What is left of the gap: with field missions off (`--story-missions=none`) a Story colony plays exactly like a Sandbox
one (population 11, Town Hall 2 38.5, raids 83% held), so all of it is the missions themselves. They take two to four
adults away about every other day through Act 1, and the colony's own scavenging runs go on as before. The bot does not
trade a scavenging run for a story mission the way a player might. Next pass: look at Act 1's travel and party sizes, and
let the bot skip a scavenging run when a mission is waiting.

Taken up in "Full-game pass 3" below: the gap was housing and wounds, not travel.

### Bisect: what pushed Sandbox Town Hall 2 from ~30 to ~40

Sandbox, 60 seeds × 60 days (a colony that never gets there counts as day 61), `main` with one system off at a time:

| variant | TH2 mean ± s.e. | median |
|---|---|---|
| main | 41.2 ± 1.4 | 38.5 |
| weather off | 40.5 ± 1.5 | 38.6 |
| food spoilage off (shelf life ×∞) | 40.7 ± 1.4 | 38.3 |
| gear mods off | 42.4 ± 1.6 | 38.6 |
| vehicles off | 41.9 ± 1.5 | 38.6 |
| raids off | 38.6 ± 1.6 | 34.9 |
| medicine off (no medicine.json) | **37.0 ± 1.2** | **34.5** |
| — infection off | 39.4 ± 1.5 | 34.9 |
| — fractures off / pain off | 42.3 / 41.8 | 38.7 / 38.8 |
| salvage off | 46.0 ± 1.4 | 43.7 (salvage helps) |
| expeditions off | 26.3 ± 0.2 | 26.5 |
| expeditions off, medicine off | 26.3 ± 0.2 | 26.5 |
| expeditions off, raids off | 25.0 ± 0.2 | 24.8 |
| night off | 24.1 ± 1.4 | 20.3 (not a fair toggle: the night pass scaled every work rate ×1.71) |

- **Medicine (~4 days) and raids (~2.5 days) are the merges that cost the most.** Medicine costs nothing without
  expeditions: all of it comes through expedition wounds (infections, Med Bay time). Weather, spoilage, vehicles and mods
  are within noise.
- Expeditions as a whole are still the big lever (+15 days), as in pass 1.

Fixes tried (same sweep): Town Hall 2 cost 55/35: 38.2 (median 34.3); 50/30: 37.4 (33.8); infection chance 0.003:
40.6 (36.2); **55/35 + infection 0.003: 37.7 (32.9)**, taken; 50/30 + 0.003: 36.9 (32.6).

### What changed

Game:

- **Town Hall L2 70 wood / 45 stone → 55 / 35; infection chance 0.005 → 0.003 per hour** (BALANCE.md "Medicine" said ~1
  in 8 untreated wounds goes bad in a day; now ~1 in 14).
- **Bed rest beats an infection** (`medBay.bedRestEasesInfection` 0.25): a Med Bay patient with no medicine in the
  stores now gets over an infection at a quarter of the treated rate, instead of growing it while bed rest holds their
  health up (a patient could stay in bed for good while the colony had no wood for Herbal Remedy). No change in the
  sweeps; it closes a trap.
- **Readiness warnings** in the mission briefing and the expedition planner (`MissionReadiness`): each wounded survivor
  and what the wound does ("right arm 45%: hits about 55% as often", or the two-handed figure when one arm is fine),
  also for plain expeditions; a party smaller than the mission wants (two for threat 1, from threat 2 as many as may
  go); weapons below a mission's new `gearTier` (M6, M7, M9 and the Act 3–4 holds: a Mk upgrade counts one tier up).
- **Story waves scale down for a light party**: a party the briefing warns about (too few, or under the gear tier)
  meets every scripted wave of two or more one enemy lighter. M7 trial deaths per run (`--mission=m7 --party=4`):
  starting gear 3.15 → 0.31, Armory gear 0.44 → 0.34. With three, still 1–2 a run.
- **Story defenses:** a new Act 1 task, **The Fence Line** (after M2: a Lookout Tower and two spike strips; pays
  `sch_barricade` and 8 scrap), on a new `Defense` colony-objective kind; 57 of 60 colonies do it by day 17. M5 now also
  pays a barricade kit (12 wood, 8 scrap) and `sch_sandbag_nest`. `Advice.Next` tips every player to build defenses a
  few days before the raid grace ends.
- **Contracts don't drain the colony:** Dead Drop needs Town Hall 2 (it is Act 2 content) and repeats every 10 days
  (was 7); Doc's Rounds every 14 (was 10). Dead Drop alone cost Story colonies about 7 days of Town Hall 2 (main
  missions only: 43.6 against 51.0). Story calls never force a launch; every side job stays optional.
- **Act 1 missions pay toward the colony:** M2 20 wood, M3 10 wood + 15 stone, M4 20 wood + 15 stone (Town Hall 2
  mean 60.3 → 53.1 in the Story sweep; half the amounts: 56.2).
- **Marrow's Debt is threat 2** (was 1): if the Tally see the party it is a firefight with three armed scavs and Cole
  (an armed party of three lost 0.9 a run in trials).
- **Bug:** M6's trucks came home as colony survivors ("Cargo truck", carrying carbines into M7), because the `Follow`
  trigger cleared the map's no-recruit flag. Fixed; M7's patient still joins.

Bot (playtest only):

- StoryBot keeps the party together (the one in front waits), joins a friend's fight two on one, breaks off when worn
  (under half its health, or someone down or nearly so; the mission stays "not done yet"), and on the way out swings only
  when the blow is worth it. M2 trial deaths (2 starting adults) 0.26 → 0.04 a run, M3 0.28 → 0.05. The trace showed
  why: one survivor ran ahead and fought three Husks alone, down to a 5% hit chance with both arms hurt.
- No wounded survivor goes on a story mission (pass 1 only checked health 90+). Gear mods are fitted before a mission.
- A main mission waits for the party its briefing asks for, and for Armory weapons before a stand-up fight (the Armory
  moves up the build order and makes machetes for four; after 20 days it goes as it is). Two-person M7 parties were
  most of its dead: M7 deaths 60 → 3.
- A side job goes only with three adults spare and nothing in its briefing to warn about; stealth jobs (Marrow's Debt)
  are left alone, since the bot walks in plain sight. Marrow's Debt is not measured.
- On a raid warning DefenseBot puts barricades (once known) and strips across the warned side.
- Tools: `--mission=ID --party=N --kit=armed`, `--story-missions=all|main|none`; the story trace prints each party.

### What a player has to learn (and where the game says it)

| Lesson | Where |
|---|---|
| A wounded arm costs most of the hit chance; heal first | briefing and planner warnings (with the figure); "Bed Rest" task |
| Threat-2 fights want a full party and Armory weapons | briefing warnings; the waves come a little lighter for a light party, and the briefing says that is not safe |
| Raiders come after about two weeks: tower, strips, barricades on the warned side | The Fence Line task; `Advice.Next` "defenses" tip; M5's barricade kit |
| Contracts are optional; take them with hands to spare | side missions are hollow diamonds; Dead Drop opens at Town Hall 2 |
| Stealth jobs are fights if you are seen | Marrow's Debt is threat 2 on its card |

## Full-game pass 3

Story pacing: close the Story vs Sandbox gap that pass 2 left (population 6 against 11, Town Hall 2 ~44 against ~36).
Measured as in pass 2: `dotnet run --project tools/Remnants.Playtest -c Release -- --data=data --days=90
--seeds=1..60 [--story]`, confirmed on seeds 61–120. "Before" is `main` at a999623 (the end of pass 2), "after" is the
end of this pass. Every change is **game** (rules or data); the bot is unchanged.

### Where the gap was

Pass 2 guessed the missions' travel and away time. The sweep says otherwise:

- **Beds, not recruits.** Story colonies took in *more* recruits than Sandbox ones (281 against 221) but had a third of
  the births (80 against 273) and nearly twice the departures (161 against 90, all "no place to sleep"). They stayed at
  the Shelter's 6 beds to day 90: the House (Town Hall 2, 60 wood / 40 stone) came on day 86 in 10 of 60 colonies,
  against day 58 in 54 of 60 Sandbox ones. A newborn in a full colony leaves with a parent.
- **Hands at work.** By day 31–60 a Story colony had about 0.9 fewer adults (deaths), 0.1–0.5 more away and 0.3 more in
  bed. Farm, Kitchen and Med Bay take the first workers, so every lost hand comes off wood and stone: 0.5 woodcutters
  and quarry workers against 1.2–1.6 in Sandbox, and everything after Town Hall 2 was built at half pace.
- **Hold the Line killed after the fight.** 22 Story deaths (Sandbox 1) were "untreated injuries", 14 of them right
  after M5: a defender downed in the last round comes home at 1 health with a torso wound draining 0.5 an hour and is
  dead before anyone admits them (seen in seed 23: Pell, torso 74%, one of two Med Bay beds free).
- **Not travel.** With no travel at all for story missions, away time drops but the bot fills it with more scavenging
  runs (1633 against 1419) and more deaths (133 against 101); Town Hall 2 and population did not move.

Variants (seeds 1–60, Story; population median, Town Hall 2 median with never = 91, House median day (colonies)):

| variant | pop | TH2 | House |
|---|---|---|---|
| before | 6 | 44.0 | 86.6 (10) |
| `--story-missions=none` | 11 | 38.5 | — |
| story missions with no travel (test hack) | 6 | 47.6 | 80.8 (21) |
| M5 pays +48 wood +40 stone | 6 | 44.0 | 86.6 (10) |
| bot: no Armory jump for M6's gear tier | 6 | 41.6 | 78.8 (14) |
| bot: upgrade the Shelter when someone has no bed | 7 | 44.0 | 85.5 (10) |
| triage on return (taken) | 6 | 41.6 | 81.6 (8) |
| triage + Hollis after M4 | 6 | 38.0 | 86.0 (10) |
| triage + a House after M5 | 11 | 41.6 | 39.8 (49) |
| **triage + Hollis + House (taken)** | **11** | **38.0** | **36.1 (51)** |

Extra materials do not reach the House: the build order and the Armory (M6's gear tier) spend them first. A House
itself does, and the extra adult from Marrow brings Town Hall 2 forward by about 3.5 days.

### What changed (game)

- **Triage on return** (`medical.triageOnReturn`, all modes): when a party comes home, whoever has a wound that is
  getting worse (severity ≥ the worsen threshold) is carried into a free Med Bay bed, the weakest first. With the beds
  full, a patient with nothing worsening and health over half gives up their bed ("gave up their bed for …"). Untreated
  deaths: 22 → 2 (Story), 1 → 0 (Sandbox).
- **Marrow pays in people** (`marrow_hello`, two days after M4): Hollis, one of his drivers, joins as a recruit. A
  failed M4 (Marrow grieving) brings nobody.
- **A House after Hold the Line**: M5 (which needs Town Hall 2, as the House does) ends with Marrow's crew putting up a
  House with their own timber, on a new story effect `Building` (if one already stands, or there is no plot, the
  materials go into the stockpile instead).

What was not needed: shorter Act 1 travel, "not now" on calls (calls never force a launch), one story mission at a time
(there is one expedition slot), a Doc Hale presence bonus. The risk stays: M2–M7 deaths are as before.

### 60 seeds × 90 days

| | Sandbox before | Sandbox after | Story before | Story after |
|---|---|---|---|---|
| median population at day 90 (mean) | 11 (11.0) | 11 (10.6) | 6 (5.8) | **11** (10.3) |
| colonies wiped out | 0 | 1 | 1 | 2 |
| Town Hall 2: median day of those that got there (how many) | 34.6 (55) | 35.5 (57) | 41.6 (49) | **35.8** (53) |
| Town Hall 2: median day, never = 91 | 35.6 | 36.6 | 44.0 | **38.0** |
| House: median day (colonies) | 57.8 (54) | 58.6 (57) | 86.6 (10) | **36.1** (51) |
| births / recruits / left for want of a bed | 273 / 221 / 90 | 251 / 236 / 106 | 80 / 281 / 161 | 241 / 263 / **44** |
| expedition deaths / runs | 41 / 1459 | 40 / 1455 | 101 / 1424 | 120 / 1359 |
| died of untreated injuries | 1 | 0 | 22 | **2** |
| raids held | 477 / 543 (88%) | 478 / 540 (89%) | 265 / 468 (57%) | 321 / 482 (**67%**) |

Seeds 61–120, same builds:

| | Sandbox before | Sandbox after | Story before | Story after |
|---|---|---|---|---|
| median population (mean, wiped) | 10 (10.6, 0) | 11 (11.3, 0) | 6 (5.7, 1) | **11** (10.5, 3) |
| Town Hall 2 median, never = 91 (reached) | 34.6 (59) | 34.6 (58) | 45.8 (52) | **38.5** (55) |
| House: median day (colonies) | 55.8 (52) | 56.5 (53) | 85.6 (9) | **37.0** (54) |
| expedition deaths / runs | 38 / 1461 | 33 / 1461 | 85 / 1452 | 124 / 1460 |
| died of untreated injuries | 3 | 2 | 19 | 4 |
| raids held | 82% | 84% | 60% | **72%** |

The one Sandbox wipe (seed 48) is two deaths in one Outskirts run on day 13 and a third on the next run; an admission
shifts the random stream, so a seed can land on a different run. The Story wipes (2 and 3) are all early: two to four
dead in Act 1 runs by day 17, as in pass 2.

Story missions (median day a mission closes, colonies that close it, deaths over all runs):

| Mission | seeds 1–60 before | after | seeds 61–120 before | after |
|---|---|---|---|---|
| M2 First Light | 9.1 (58), 8 in 64 | 9.1 (58), 8 in 64 | 7.9 (57), 8 in 69 | 7.9 (57), 12 in 74 |
| M3 Signal in the Rust | 13.6 (58), 6 in 76 | 13.6 (58), 6 in 76 | 13.4 (57), 3 in 65 | 13.4 (57), 3 in 66 |
| M4 The Stranger | 17.4 (48), 13 in 64 | 16.9 (46), 13 in 63 | 16.9 (53), 6 in 62 | 16.9 (53), 5 in 61 |
| M5 Hold the Line (end of Act 1) | 41.9 (46), 4 in 46 | **36.1** (50), 4 in 50 | 46.1 (51), 3 in 51 | **36.8** (53), 8 in 55 |
| M6 The Long Haul | 61.4 (39), 14 in 42 | 58.3 (37), 19 in 42 | 64.6 (41), 14 in 46 | 61.6 (45), 11 in 47 |
| M7 The Drowned Clinic | 70.8 (38), 3 in 38 | 70.9 (37), 9 in 39 | 69.4 (35), 3 in 35 | 65.9 (45), 7 in 46 |
| M8 Eyes Up | 87.4 (1) | 84.1 (10), 0 in 15 | — | 80.6 (15), 1 in 20 |
| M9 The Tally | 73.4 (23), 5 in 35 | 75.4 (29), 8 in 42 | 74.4 (32), 5 in 46 | 70.4 (37), 10 in 52 |
| M10 Ghost Signal | — | 84.4 (5), 5 in 6 | — | 82.6 (10), 9 in 13 |
| M11 Lantern (Act 3) | — | 0, 3 in 3 | — | 0, 12 in 15 |
| Doc's Rounds (repeatable) | 44 runs, 4 dead | 51 runs, 9 dead | 43 runs, 9 dead | 71 runs, 21 dead |

M1–M4 are unchanged (the changes come in after M4). Expedition deaths rise because stronger colonies get further: most
of the extra dead are in M10, M11 and Doc's Rounds, which few colonies reached before.

### Against the goals

| Goal | Result |
|---|---|
| Story population within ~20% of Sandbox at day 90 | **Met**: 11 against 11 (means 10.3–10.5 against 10.6–11.3) on both seed ranges. |
| Story Town Hall 2 ≈ day 36–40 | **Met**: 38.0 / 38.5 (never = 91); 35.8 / 36.5 among the colonies that get there. |
| Act 1 done by ~day 35 | **Close**: M5 closes on day 36.1 / 36.8 (was 41.9 / 46.1). M5 waits for Town Hall 2. |
| Deaths stay possible | Yes: 120 / 124 expedition deaths against Sandbox 40 / 33; every mission still loses people. |

### Watch next pass

- **Act 3 with Act 2 gear.** M11 Lantern is never closed and loses 3 in 3 runs (seeds 1–60) and 12 in 15 (seeds
  61–120); M10 loses 5 in 6 and 9 in 13. Colonies now reach them around day 80–85; the bot takes them with what it has. Look at their gear tier
  warnings and the bot's wait for gear, as pass 2 did for M6/M7.
- **Doc's Rounds** got deadlier as more colonies reach it (21 dead in 71 runs on seeds 61–120).
- **M6** still loses 11–19 dead in 42–47 runs.

## Construction

Buildings are built by people over time (see ARCHITECTURE.md "Construction"): Construct and Upgrade place a site, its
materials leave the stockpile, builders haul them over and put in work. Asked for: level 1 quick and cheap ("a simple
shed"), upgrades much longer, early pacing near pass 3. Measured as pass 3 with 120 seeds (1–120) because the 60-seed
medians swung by up to 6 days between near-identical runs: `dotnet run --project tools/Remnants.Playtest -c Release --
--data=data --days=90 --seeds=1..120 [--story]`. "Before" is `main` at d53ec21 (pass 3 and review fixes 1), "after" is this
branch.

### The numbers

| | rule |
|---|---|
| builder work / hour | `workPerHour` 1 × the `construction` job's output (Strength 0.4, Endurance 0.3, Intellect 0.15, Creativity 0.15 + the Construction skill; arms/legs/torso; tools +40%): a fresh adult (stats ~45, skill 0) puts in about **0.45 an hour**, a trained one 0.6–0.7 |
| hauling | 30 units an hour (health, child rate); builders keep the materials 25% ahead of the work |
| L1 `buildWork` | cost / 25, 1–4: Farm, Woodcutter, Quarry 1 (about 1–2 h with two builders, hauling included); Workshop 1.4, Med Bay 1.8, House 4 |
| L2 `buildWork` | 5 + cost / 20, up to 12: Woodcutter 7.5, Town Hall 9.5 (6.4 on the old cost), House/Armory/Lab 12 — **one builder 1–2 days** on the work schedule, two about a day |
| L3 `buildWork` | 18 + cost / 20, up to 32: Woodcutter 23.5, Town Hall 32 — **one builder 3–5 days** |
| `maxBuilders` | 2 / 3 / 4 by level (House 3 / 4 / 4) |
| idle helpers | 50% of a builder, at Normal and High priority sites |
| weather | rain ×0.8, thunderstorm ×0.4, dust storm **0** (halts) |
| perks | Engineer +15% construction, Pack Mule +10% |
| cancel | not yet hauled: all back; hauled: 80% back |
| Town Hall L2 | **48 wood / 30 stone** (was 55 / 35): the labour the upgrade now takes is paid back in materials |

Build menu estimates (two typical builders on the day shift, clock time): Farm L1 ~1–2 h, Woodcutter L2 ~1 day, L3 ~3 days.
The test `Upgrades_take_days_and_the_building_keeps_working_at_its_old_level` pins one average builder at 1–2.5 days for
Woodcutter L2 and 3–6 days for L3.

**An upgrade keeps the building working at its old level** until it is done (workers, output, beds, the Town Hall cap):
nothing stops while the scaffold is up, and the cost of an upgrade is the builders' time and the wait for the new level.

### The bot

The bot (a sensible first-time player) now: counts a site as its goal reached; keeps at most two sites open; puts one
builder on every site before crafters and medics, the best at it first (children too once food is safe), up to two (three
on an upgrade) with about a quarter of its hands in all (`BOT_BUILDER_SHARE`, default 4; 3 and 6 measured the same within
noise); and from day 30, with food under 4 days and every farm slot taken, puts up another Farm (quick and cheap now).
Daily lines show sites (`Fm>2(40%)`) and builders (`bld:2`).

### 120 seeds × 90 days

| | Sandbox before | Sandbox after | Story before | Story after |
|---|---|---|---|---|
| Workshop (4th building) built, median day | 0.8 | 1.5 | 0.8 | 1.5 |
| Town Hall 2: median day (colonies) | 34.6 (115) | **35.8** (114) | 35.8 (108) | **35.8** (106) |
| House built: median day (colonies) | 57.5 (110) | 60.5 (102) | 36.8 (105) | 37.5 (103) |
| median population at day 90 (mean, wiped) | 11 (10.96, 1) | 10 (10.22, 2) | 11 (10.38, 5) | 10 (10.26, 6) |
| births / recruits / left | 511 / 478 / 192 | 441 / 489 / 215 | 446 / 597 / 105 | 430 / 656 / 164 |
| first pregnancy, median day | 36 | 45 | 35 | 37 |
| expedition deaths / runs | 73 / 2916 | 68 / 2886 | 244 / 2819 | 227 / 2834 |
| starvation deaths | 0 | 0 | 0 | 9 (one colony, seed 72) |
| raids held | 86% | 82% | 70% | 65% |

Variants on the way (120 seeds, Town Hall 2 median, Sandbox / Story):

| variant | Sandbox | Story |
|---|---|---|
| L1 cost/15 (1–6), L2 5+cost/20, Town Hall 55/35 | 39.3 | 37.3 |
| L1 cost/25 (1–4), same L2, Town Hall 55/35 | 38.3 | 37.3 |
| same, bot builder share 3 / 4 / 6 | 38.7 / 38.4 / 38.4 | 37.3 / 36.8 / 38.4 |
| **same, Town Hall 48/30 (taken)** | **35.8** | **35.8** |

The first buildings go up within the first day (Farm, Woodcutter and Quarry in the first hours, the Workshop on day 1.5
instead of 0.8). Town Hall 2 is back on pass 3 (Story equal, Sandbox +1.2 days) once its own cost pays for the labour. A
seed-by-seed comparison is noisy: the median paired difference before the Town Hall change was +2.6 days with a
quartile range of −4 to +9, because a site moves the random stream (who is home when a raid or a party leaves).

### Against the goals

| Goal | Result |
|---|---|
| L1 quick and cheap (tens of minutes to a few hours, 1–2 builders) | Met: 1–4 work, 1–2 h for a gathering shed with two builders, up to ~5 h for a House. |
| L2 ≈ 1–2 days, L3 ≈ 3–5 days of builder work | Met for one average builder (tested); two builders halve it. |
| Town Hall 2 and early pacing near pass 3 | Met: Town Hall 2 35.8 / 35.8 (pass 3: 34.6 / 35.8); Workshop +0.7 days. |
| Population | Story equal (mean 10.3 vs 10.4); Sandbox −0.7 on the mean: the House comes 3 days later and the Shelter's 6 beds hold births back (first pregnancy day 45 vs 36). |

### Watch next pass

- **Sandbox housing**: births wait for the House (beds full). A Shelter upgrade is now the quick fix a player would make;
  the bot never upgrades the Shelter (pass 3 found the same in Story).
- **Death spirals**: Story seed 72 starves with a full L1 Farm, 1 wood and half the adults in bed or away; the bot
  keeps its Workshop crafter instead of cutting wood. Base has the same shape in its wiped colonies.
- **Raids held** dipped (86 → 82%, 70 → 65%): builders are not guards, and the upgrades that would add worker slots come
  later. Worth a look with the defense numbers.

## Hit tiers (scavenge tactical cards, unit 1)

A landed hit now resolves by its roll margin: the share of the hit chance the roll came in under
(`combat.tiers` in data/scavenge.json). Below 0.25 it is a **graze** (40% of the damage, at least 1, no body-part
injury), from 0.8 a **solid** hit (full damage, 1.5× injury severity), a plain **hit** between. Armor on the hit region
of 4 or more (after pierce) and cover of 0.25 or more (terrain beside the target plus brace) each shift it down a tier,
never below a graze. So in the open about 25% of hits graze, 55% hit and 20% land solid. The tiers reuse the existing
hit roll, so fixed-seed runs draw the same numbers as before.

**Tuning.** The first cut shifted from armor 2. The first party member's starting Padded Vest is armor 2, so every hit
on them dropped a tier, and early-zone deaths more than halved (Outskirts 6% → 1%, Suburb Ruins 35% → 18%, Flooded Mall
57% → 36%). Sweeping the knobs with the same command showed the graze knobs barely move it: `grazeBelow`/`grazeDamage`
at 0.1–0.25 / 0.4–0.6 all gave Outskirts 1%, Suburb 18–20%. Taking the armor shift away put the numbers back, and the
cover shift is close to irrelevant (the bot rarely fights from cover). So `armorShiftFrom` is now 4: Linked Plating,
Riot Armor and the natural shells shift a tier, the vest and coats do not.

Zone trials: `dotnet run -c Release --project tools/Remnants.Playtest -- --zone=all --runs=300` (run seeds 1..300:
the colony is seeded `seed + 10007·p`, the map `seed · 7919`; party of 2, starting gear). Before = `56e41cc` (main, no
tiers), after = `53eefd0` (this branch).

| Zone | Runs with a death | Wipes | Injuries/run |
|---|---|---|---|
| Outskirts | 6% → 4% | 0% → 1% | 5.0 → 4.8 |
| Scrapyard Flats | 12% → 8% | 3% → 1% | 5.9 → 5.8 |
| Suburb Ruins | 35% → 33% | 14% → 10% | 5.5 → 5.4 |
| Flooded Mall | 57% → 51% | 23% → 19% | 4.3 → 4.4 |
| Rail Depot | 91% → 91% | 65% → 62% | 1.3 → 1.3 |
| Network Relay | 99% → 99% | 88% → 84% | 0.3 → 0.3 |
| Core Spire | 100% → 100% | 97% → 95% | 0.1 → 0.1 |

Target 3 (Outskirts: a death in ≤ 10% of runs, wipes ≤ 3%, nearly every run comes home hurt) holds, and runs stay
about as risky as before: a little safer, because ~25% of hits are grazes that do 40% damage and solid hits only add
injury, not HP. AC-14 re-checks the death and wipe targets once intents and cards are in. The knobs, in order of effect:
`armorShiftFrom`, then `grazeBelow` / `grazeDamage`.

Also in this unit: `combat.armLockBelow` 0.5 (arm efficiency under which arm-locked attacks and cards cannot be used; no
item is marked yet), and field mending (`use.mend`): the Suture Kit takes 0.15 and the Field Trauma Kit 0.2 off the
worst injury's severity during a run.

## Telegraphed plans and cards (scavenge tactical cards, unit 4)

Enemies now telegraph moving then striking (a plan declared on their turn, carried out at the start of their next),
strike at once when a target is already in reach, and turrets telegraph their aim (the hybrid the user chose). Plans
fit a full turn of AP. Parties bring the starter deck; the playtest bot plays cards by a simple priority, steps off
tiles a seen enemy plan aims at, and telegraphs a move and a blow when nothing is in reach. Authored enemies (Cole,
the Linked Warden) bring cards.

Zone trials: `dotnet run -c Release --project tools/Remnants.Playtest -- --zone=all --runs=300` (run seeds 1..300:
colony `seed + 10007·p`, map `seed · 7919`; party of 2, starting gear). Before = `4d25616` (enemies act at once, no cards;
with the new metrics), after = `26f157c`.

| Zone | Runs with a death | Wipes | Runs hurt | Damage taken/run | Death rate with Linked vs without |
|---|---|---|---|---|---|
| Outskirts | 4% → 2% | 1% → 0% | 95% → 94% | 64 → 49 | – |
| Scrapyard Flats | 8% → 6% | 1% → 1% | 97% → 94% | 80 → 62 | – |
| Suburb Ruins | 33% → 18% | 10% → 3% | 86% → 93% | 134 → 104 | 64/26% → 34/14% |
| Flooded Mall | 51% → 31% | 19% → 4% | 74% → 83% | 154 → 104 | 57/26% → 33/23% |
| Rail Depot | 91% → 50% | 62% → 9% | 33% → 75% | 203 → 122 | 92/20% → 50/0% |
| Network Relay | 99% → 65% | 84% → 13% | 14% → 63% | 221 → 130 | 99/0% → 65/0% |
| Core Spire | 100% → 80% | 95% → 22% | 5% → 50% | 227 → 144 | 100/0% → 80/0% |

New metrics (after, per run): Outskirts 23.9 plans (14.8 by enemies; 6.3 fizzled), 10.2 cards played, hits taken
35/48/17% graze/hit/solid in Suburb; enemy attacks per enemy-round 0.40 in Outskirts down to 0.14 in Core Spire.

Against the targets: target 3 holds (Outskirts: a death in 2% of runs ≤ 10%, wipes 0% ≤ 3%, 94% of runs come home
hurt). Target 4 holds: no Linked in Outskirts or Scrapyard Flats; in Suburb Ruins 21% of runs see one and those lose
someone 2.4× as often (34% vs 14%). No data knob was changed. Runs got safer, most in the late zones: telegraphed
approaches can be dodged, cards guard and cover, and wipes in Rail Depot and beyond drop from 62–95% to 9–22%. The
late zones were never meant for a starting party (they stay deadly with Linked: every death there comes with them), but
if they should stay near-certain wipes for starting gear, the knobs are the zones' enemy counts and the Linked's AP or
accuracy.

The earlier option, every enemy turn telegraphed, was measured too (same command, `ee7ba0d`): Outskirts deaths 0%,
runs hurt 45%, damage taken 7 per run, Rail Depot deaths 18%; enemies acted on ~0.07 attacks per enemy-round, because
an enemy in reach still had to wait a turn while players struck at once. That missed target 3, hence the hybrid.

Not fixed here: the playtest bot still now and then paces between two tiles for the rest of a run (one stuck run in
Suburb Ruins, seed 44; the baseline had stuck runs too).

## Late-zone re-tune (scavenge tactical cards, unit 6)

With telegraphs the late zones got far easier (previous section). Each of Rail Depot, Network Relay and Core Spire now
carries a `retune` block in zones.json: more enemies (Rail Depot 6–8 → 11–13, Network Relay 7–10 → 13–16, Core Spire
10–13 → 18–21), more turrets (2–3, 3–4, 6–7) and pylons (2, 3, 4), more reinforcement waves (3, 5, 7) of more units
(3, 3–4, 4–5), called after 1 round of alarm instead of 3. The global `network.reinforceAfterRounds` stays 3. Linked
skill and card lists were tried and barely moved the late zones (+1–4 points) while also touching Suburb Ruins and
the Flooded Mall, so they are not used. The sweep (21 trials) is logged in the orchestration's progress/unit-6.md.

Which runs take it is a setting, to compare (Settings > Game > Late zones; Core `LateZoneTuning`): **as before**
(Current), **re-tuned expeditions** (the default: free expeditions take it, story missions keep the zone as it is) or
**re-tuned everywhere** (story missions in these zones too). Playtest: `--late-zones=current|expeditions|everywhere`
(default expeditions); debug runs: `--scavenge --late-zones=MODE`; multiplayer: the host's "Late zones (host)" picker
or the server's `--late-zones=MODE` (default expeditions), shown in the lobby.

Zone trials: `dotnet run -c Release --project tools/Remnants.Playtest -- --zone=all --runs=300` (run seeds 1..300, as
above), `--late-zones=current` vs the default. Before = `1ce7eea` (the same numbers as `--late-zones=current` now),
after = `93948d0` (the retune is `a029f31`). Pre-telegraph = `4d25616` (the targets: within about 10 points).

| Zone | Pre-telegraph deaths / wipes | Runs with a death | Wipes | Runs hurt | Damage taken/run | Death rate with Linked/without |
|---|---|---|---|---|---|---|
| Outskirts | 4% / 1% | 2% → 2% | 0% → 0% | 94% → 94% | 49 → 49 | – → – |
| Scrapyard Flats | 8% / 1% | 6% → 6% | 1% → 1% | 94% → 94% | 62 → 62 | – → – |
| Suburb Ruins | 33% / 10% | 18% → 18% | 3% → 3% | 93% → 93% | 104 → 104 | 34/14% → 34/14% |
| Flooded Mall | 51% / 19% | 31% → 31% | 4% → 4% | 83% → 83% | 104 → 104 | 33/23% → 33/23% |
| Rail Depot | 91% / 62% | 50% → 95% | 9% → 55% | 75% → 32% | 122 → 189 | 50/0% → 95/0% |
| Network Relay | 99% / 84% | 65% → 96% | 13% → 75% | 63% → 16% | 130 → 203 | 65/0% → 96/0% |
| Core Spire | 100% / 95% | 80% → 100% | 22% → 89% | 50% → 8% | 144 → 215 | 80/0% → 100/0% |

All three late zones are back within 10 points of their pre-telegraph deaths and wipes. Fewer runs come home hurt
there because more of them do not come home. The four earlier zones are unchanged: targets 3 and 4 hold as before.

Story missions (`--mission=ID --runs=100 --party=4 --kit=armed`), wipes with re-tuned expeditions → re-tuned
everywhere: m11b 13% → 48%, m13 58% → 94%, m15 49% → 86%, side_children 58% → 94%, m17_choir 65% → 65%. The
re-tuned expeditions setting leaves every story mission exactly as it was; m12, m16 and the m17 missions use handmade
maps and do not change under either setting.

## Early resource scarcity

Asked for: survivors can now hold several jobs, so start with fewer people (Sandbox: pick the number); buildings cost
less wood; a level 1 Woodcutter cuts less; scavenging is the good early source of wood; building upgrades, the
worker's skill and better tools pay off later. The same curve for every gathered material (wood, stone, scrap).
Measured with `dotnet run --project tools/Remnants.Playtest -c Release -- --data=data --seeds=1..60 --days=60 [--story]
--quiet`; "before" is `main` at 4da608d, "after" this branch.

### The rules

- **Start**: 3 adults + 1 child (was 3 + 2), `rules.newGame.adults`/`children`. Sandbox picks 2–6 on its New Game card
  (`habitantsRange`; one in four a child, at least one from three people up; saved as `ColonyState.NewGameOptions`, the
  last pick remembered in settings.cfg). Story keeps the default (Pell is the child). Start stock 45 wood / 20 stone (60 / 30).
- **Gathering curve** (Woodcutter, Quarry, Scrapyard): lower base output (0.6 / 0.55 / 0.27, was 0.96 / 0.88 / 0.43),
  steeper levels (×1.6 / ×2.2, was ×1.25 / ×1.5), skill weight doubled (2 / 2 / 1.6) so practice matters, tool bonus
  0.6. Per hour for a fresh adult (stats 45, skill 0, no tool):

  | | L1 before → after | L2 | L3 | skill 30 / 70 (L1) | hand tool / best tool (L1) | L3, skill 70, best tool |
  |---|---|---|---|---|---|---|
  | wood | 0.40 → **0.22** | 0.35 | 0.48 | 0.31 / 0.43 | 0.28 / 0.35 | 1.50 (before, no tools existed: 0.98) |
  | stone | 0.37 → **0.20** | 0.32 | 0.44 | 0.28 / 0.39 | 0.26 / 0.32 | 1.38 (0.90) |
  | scrap | 0.19 → **0.10** | 0.16 | 0.23 | 0.14 / 0.19 | 0.13 / 0.16 | 0.67 (0.43) |

- **Tools** (jobs.json `tools`, `WorkTools`): stockpile items a job's workers use, one each, the best to the lowest id;
  a tool on an expedition is away. Woodcutting: Hand Saw 0.5 / Felling Axe 1; Mining: Pickaxe 0.5 / Hand Rock Drill 1;
  Scrap Sorting: Crowbar 0.5 / Cutting Torch 1. The basic three are Workshop L1 recipes (3–5 scrap) and loot; the good
  ones Workshop L2 with Crafting 10. Before this pass `toolQuality` was never set, so tools did nothing at home.
- **Haul** (zones.json `haul`): a plain run that gets someone home brings materials besides the packs, rolled from the
  expedition's seed, × (1 − depletion): Town Outskirts 13–18 wood, 3–6 stone; Scrapyard Flats 20–28 wood, 6–10 stone,
  6–10 scrap; Suburb Ruins 6–10 wood, 4–8 scrap. The haul recovers over a day (`haulRecoveryDays` 1, from
  `ZoneStatus.LastHaulTick`): a run sooner gets elapsed / a day of it, so Outskirts runs chained on one day (they take no
  colony time) haul next to nothing after the first (13 then 0 and 0 in the test, was ~30 in all). The after-action
  report lists it ("Hauled home"). Two fresh adults at a
  level 1 Woodcutter cut ~6 wood in a day of work hours, so a first Outskirts run (a day) brings at least 2× that and a
  Scrapyard Flats run (two days on the road) at least 1.6×.
- **Costs**: level 1 wood down ~25–40% (Farm 10, Woodcutter 6 / 3 stone, Quarry 10, Workshop 18 / 8, Scrapyard 14 / 6,
  Med Bay 22 / 12, Kitchen 22 / 15, Tailor 22 / 8, Campfire 5, ...), gatherer, Farm and Shelter L2 a little. The first
  five builds (Farm, Woodcutter, Quarry, Workshop, Scrapyard: 58 wood / 17 stone) take the start stock plus one Outskirts
  run. Town Hall L2 (48 / 30) and House (60 / 40) were cut in a first try and put back: with the haul Town Hall 2 came
  on day 25 instead of ~31.

Rerun after the haul recovery (`e653583`): the same seeds give the same numbers below, since the bot sends a party
every 3 days and never chains runs. The bot now crafts a basic tool for each bare-handed gatherer (Workshop, after weapons) and the good one once the
Workshop is level 2: basic tools come about day 6–10, good ones about day 53. It still gives everyone one job.

### 60 seeds × 60 days

| | Sandbox before | Sandbox after | Story before | Story after |
|---|---|---|---|---|
| median population at day 60 | 6 | 8 | 10 | 10 |
| colonies wiped / starvation deaths | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 |
| min food stock, days of need (median) | 3.6 | 7.5 | 2.3 | 1.6 |
| Town Hall 2 (median day) | 30.7 | 29.3 | 31.3 | 30.5 |
| House affordable (median day) | 51.6 | 47.6 | 31.6 | 31.8 |
| first birth (median day) | 46 | 34 | 38 | 37 |
| expedition deaths / runs | 13 / 960 | 14 / 982 | 37 / 1045 | 44 / 1057 |
| left for want of a bed | 101 | 43 | 64 | 21 |
| first raid held | 54/60 | 59/60 | 47/60 | 41/60 |

Days 1–10 (Sandbox, 60 seeds, daily stock): median wood 12 → **9**, stone 17 → **9**, scrap 9 → **6**; wood under 10 on
44% → 51% of days. The first week is tighter on materials while the milestones keep their days: the haul fills the gap
the Woodcutter leaves, and from about day 30 levels, skill and tools take over.

- Fewer mouths make food easier in Sandbox (min 7.5 days of need against 3.6) and births come earlier; Story food is a
  little tighter (1.6 days), with no starvation.
- Story's first raid is held less often (41 against 47 of 60): one person fewer at home when it comes. Raid deaths are
  unchanged (3 / 3).
- Late game gathers more than before (a level 3 Woodcutter with a skilled, tooled worker cuts 1.5 an hour against 0.98):
  the payoff the request asked for. Watch the 90-day economy next pass.
