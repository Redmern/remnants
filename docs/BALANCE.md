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

## Watch next pass

- Late food surplus: by day 60 the bot's colonies sit on 100–150 food; Kitchen meals and more mouths absorb some of it.
- Outskirts wounds: ~5 per run. Fine with a Med Bay; before one exists, 30–60% wounds just sit there.
- Children grow up in about two weeks of colony time (a few minutes at 1x); they reach expedition age very early.
- Linked base combat: a geared party that fights them loses (see the fog pass). Their carbine's three bursts per turn are
  the lever (AP cost 2, or fewer bursts), or a better party bot that breaks line of sight, flanks and uses the jammer.
- Fog tuning knobs worth trying first: Linked `stealth` (25), `ambush.hitBonus` (0.2), `overwatch.reactions` (2) and
  the Linked lurking on overwatch (`fog.ai.linkedLurk`).
