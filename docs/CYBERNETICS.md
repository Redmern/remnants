# Cybernetics

*Keep them alive. Keep them human.*

A lost limb is no longer the end of a survivor's usefulness, and a healthy one is no longer safe from ambition. The
cybernetics tree turns prosthetics into a crafting tree per limb slot, with specialisations, surgery, upkeep, Network
interference and a cost no stockpile can pay: **humanity**.

All numbers live in `data/cybernetics.json` (plus the parts in `items.json`, their recipes in `recipes.json`, the perks
in `perks.json` and the Linked core drop in `enemies.json`). Code: `Data/Cybernetics.cs` (definitions, lookups,
validation), `Colony/Cybernetics.cs` (tree status, surgery, upkeep, humanity), `Scavenge/Interference.cs`, and the hooks
in `ProfileDeriver`, `Hacking.Tool` and `Detection.ScanRadius`. Tests: `CyberneticsTests.cs`. Balance: BALANCE.md
"Cybernetics".

## Slots and tiers

Slots: **arms** (left / right), **legs** (left / right) and the **eyes** — a new body part in the head region. The eyes
are never hit or lost in the field; they are only ever replaced electively. The head region's efficiency is the head's
times the eyes' (healthy eyes change nothing; an eye implant at quality 1.15 scales sight, detection and ranged aim by
1.15).

| tier | name | how it goes on | humanity (replacement / elective) | upkeep | look until tier models exist |
|---|---|---|---|---|---|
| T0 | Crude | straps on (`FitProsthetic`), Med Bay 1 | 0 / — | none | peg / hook models |
| T1 | Mechanical | straps on, Med Bay 1 | 3 / — | none | cyborg model, tint `#9a8668` |
| T2 | Powered | surgery, Med Bay 2 | 8 / — | every 10 days: 2 scrap, 1 electronics | cyborg model, tint `#b07a4a` |
| T3 | Cyborg | surgery by a Cyber-Surgeon, Med Bay 2 | 15 / 35 | every 8 days: 2 scrap, 1 electronics | cyborg model, tint `#7f8c99` |
| T4 | Network-grade | surgery by a Cyber-Surgeon, Med Bay 3 | 22 / 45 | every 6 days: 2 electronics | cyborg model, tint `#5fd0e0` (Network cyan) |

## The tree

A design (node) opens when **one of its lower-tier requirements is known** — crafted, fitted or owned at some point;
tier 0 is known from the start — **or its blueprint is studied** (a blueprint skips the tier below), and **someone at
home has its perk**. Its recipe adds the building level and the components. Specialisations start at T2.

```
ARMS   T0 hook_arm ─ T1 mech_arm ─┬ T2 servo_arm ──────┐
                    (Tinkerer)    ├ T2 labor_arm ──────┼─ T3 cyber_arm ───────────────┐
                                  │                    ├─ T3 labor_arm_mk2            │
                                  ├ T2 precision_arm ──┼─ T3 precision_arm_mk2        ├─ T4 network_arm
                                  │                    ├─ T3 hacker_arm               │   (Linked Salvage,
                                  ├ T2 blade_arm ──────┼─ T3 blade_arm_mk2            │    any T3 arm, or
                                  │   (Biomech)        └─ T3 gun_arm (blade or labor) ┘    bp_network_grade)
                                  │                       (Neural Interface; or bp_cybernetics)
LEGS   T0 peg_leg ─ T1 mech_leg ──┬ T2 runner_leg ──┬─ T3 cyber_leg (any T2 leg)  ┐
                                  │                 ├─ T3 runner_leg_mk2           │
                                  ├ T2 silent_leg ──┼─ T3 jumper_leg (runner or silent)
                                  │                 ├─ T3 silent_leg_mk2           ├─ T4 network_leg
                                  └ T2 armored_leg ─┴─ T3 armored_leg_mk2          ┘
EYES   (elective only)            T3 scope_eye (precision_arm or servo_arm) ──┬─ T4 network_eye
                                  T3 thermal_eye (precision_arm or silent_leg)┘
```

| design | quality | made at | components | effects |
|---|---|---|---|---|
| Hook Arm / Peg Leg (T0) | 0.5 / 0.6 | Workshop 1 | — | — |
| Mechanical Arm / Leg (T1) | 0.85 | Workshop 2, Crafting 15 | — | — |
| Servo Arm (T2) | 1.0 | Lab 1, Electronics 20 | 2 servo, 1 power cell | — |
| Labor Arm (T2) | 1.05 | Lab 1 | 2 servo, 1 power cell | +8% woodcutting, mining, scavenging; +3 kg carry |
| Precision Arm (T2) | 1.0 | Lab 1 | 2 servo, 1 power cell | +0.05 crafting quality (crafting, tailoring, electronics, gunsmithing); +3 accuracy |
| Blade Arm (T2) | 0.95 | Lab 1 | 2 servo, 1 power cell | Arm Blade attack 7–12 (silent, no reveal); +10% melee |
| Runner Leg (T2) | 1.1 | Lab 1 | 2 servo, 1 power cell | +2 initiative |
| Silent Leg (T2) | 1.0 | Lab 1 | 2 servo, 1 power cell | +4 stealth |
| Armored Leg (T2) | 0.9 | Lab 1 | 2 servo, 1 power cell | +1 armor, −3 stealth |
| Cybernetic Arm / Leg (T3) | 1.3 | Lab 2, Electronics 30 | neural link, myomer bundle, 2 servo, power cell | — |
| Cyborg Labor Arm (T3) | 1.3 | Lab 2 | same | +15% gathering jobs; +6 kg |
| Cyborg Precision Arm (T3) | 1.25 | Lab 2 | same | +0.1 crafting quality; +5 accuracy |
| Hacker Arm (T3) | 1.2 | Lab 2 | same | built-in hacking tool (range 2, +10%); +5% hack chance |
| Cyborg Blade Arm (T3) | 1.25 | Lab 2 | same | Arm Blade 10–17; +20% melee |
| Gun Arm (T3) | 1.2 | Lab 2 | same | Arm Gun 9–14, reach 6, 1 AP, no free hand needed |
| Cyborg Runner Leg (T3) | 1.35 | Lab 2 | same | +4 initiative |
| Jumper Leg (T3) | 1.2 | Lab 2 | same | knight jump over anything (2 AP) |
| Cyborg Silent Leg (T3) | 1.25 | Lab 2 | same | +8 stealth |
| Cyborg Armored Leg (T3) | 1.1 | Lab 2 | same | +2 armor, −4 stealth |
| Scope Eye (T3) | 1.15 | Lab 2 | neural link, servo | +1 sight, +5 accuracy |
| Thermal Eye (T3) | 1.1 | Lab 2 | neural link, power cell | +10 detection; hostiles within 2 tiles seen through walls and stealth; `night_detection_bonus` 10 |
| Network-grade Arm (T4) | 1.5 | Lab 3, Electronics 40 | 2 Linked cores, neural link, 2 myomer, 2 power cells | hacking tool (range 2, +15%, progress 2); +10% gathering; +0.05 quality; +5 accuracy, +15% melee, +5% hack; +4 kg |
| Network-grade Leg (T4) | 1.6 | Lab 3 | same | +4 initiative, +6 stealth |
| Network-grade Eye (T4) | 1.3 | Lab 3 | Linked core, neural link, power cell | +2 sight, +10 detection, +5 accuracy, thermal 2 |

Quality is the part's efficiency (× its upkeep): it feeds job output through the body-region weights, movement range
(legs), attack accuracy and melee damage (arms), and sight / detection / ranged aim (eyes). Above 1 beats the original.
The existing `scavenge.json` body rule still applies: two prosthetic legs of quality ≥ 1.2 give +1 range.

**Paired sets** (two parts of the same specialisation, T2+, both working):

| set | spec | bonus |
|---|---|---|
| Heavy Lifter | labor | +4 kg carry |
| Steady Pair | precision | +3 accuracy |
| Twin Blades | blade | +10% melee |
| Matched Striders | runner | +1 move range |
| Spring-loaded | jumper (T3) | the knight jump costs 1 AP |
| Ghost Step | silent | +5 stealth |
| Bulwark | armored | +1 armor, range at most 3 |

**Components** (items, all `Material`): Servo (Workshop 2: 6 scrap, 1 wood; loot T1), Power Cell (Lab 1: 2 electronics,
2 scrap; loot T2), Myomer Bundle (Tailor 2: 4 cloth, 1 medicine; loot T2), Neural Link (Lab 2: 4 electronics, 1 scrap;
loot T3), **Linked Core** (never crafted: every killed Linked drops one, a Warden three). Blueprints: Cybernetics
(`bp_cybernetics`, loot T2, studied at Lab 2: opens every T3 design without a T2 known) and Network-grade Limbs
(`bp_network_grade`, loot T3, Lab 3: opens T4 designs, and Linked Salvage needs it).

**Perks**:

| perk | discipline / tier | requires | effect |
|---|---|---|---|
| Tinkerer (existing) | Tech 1 | Crafting 10 | gates T1 |
| Biomech | Tech 2 | Electronics 15, Intellect 35, level 3, an Electronics Lab | gates T2; +0.05 electronics quality |
| Neural Interface | Tech 3 | Electronics 30, Biomech, level 5, Electronics Lab 2 | gates T3 and the eyes; +5% electronics |
| Linked Salvage | Tech 3 | Electronics 40, Hacking 20, Neural Interface, level 7, `bp_network_grade` | gates T4; `cyber_interference_reduction` 0.5 |
| Surgeon I | Medic 1 | Medicine 15, Perception 35 | `surgery_success_bonus` 0.08, `surgery_speed_bonus` 0.25 |
| Surgeon II | Medic 2 | Medicine 30, Surgeon I, level 3, Med Bay 2 | the same again |
| Cyber-Surgeon | Medic 3 | Medicine 40, Electronics 15, Surgeon II, level 5, Med Bay 2 | `cyber_surgery` (operate T3+), +0.05 success |

(The old Medic tier-3 "Surgeon" perk, id `surgeon`, is now named **Chief Medic**; it is unchanged.)

## Surgery

`ScheduleSurgery(survivor, part, item, elective?, bay?)` books an operation: the item is set aside, the patient takes a
Med Bay bed (and is not discharged while booked). Each bay works its first booking. T0/T1 parts can also be strapped on
at once with `FitProsthetic` (missing limbs only); from T2 that command answers `NeedsSurgery`.

```
effort/tick = Σ medics at the bay (performance · tool · health · mood · perks) · bay level multiplier · (1 + lead's surgery_speed_bonus)
work        = [4, 8, 24, 48, 72] by tier      medicine: 0.03 per tick worked
success     = 0.75 + 0.004 · lead's Medicine + lead's surgery_success_bonus − [0, 0, 0.05, 0.15, 0.25][tier] − (elective ? 0.05 : 0)
              clamped to [0.30, 0.97]
```

The lead is the bay's medic with the best Medicine. No medic: no progress. T3+ needs the lead to be a Cyber-Surgeon.
A failed roll still fits the part, but leaves an infection: a torso wound of severity 0.30–0.55 that keeps the patient in
bed on medicine until it heals. **Nobody dies on the table.** A replaced prosthetic goes back to the inventory.
Examples: a fresh medic (Medicine 15, no perks) fitting a powered leg: 76%; Medicine 40 with Surgeon I + II and
Cyber-Surgeon fitting a cyborg arm: 0.75 + 0.16 + 0.21 − 0.15 = 97% (the cap).

## Elective augmentation

Replacing a **healthy** part (or a part that was already replaced electively) is an augmentation: only T3 and up, only
adults, and the command must carry `Elective: true` — without it the answer is `ElectiveNotAllowed` with the humanity it
would cost, which the UI shows in a confirm dialog. Injured parts must heal first (`PartInjured`).

## Humanity

`Body.Humanity = 100 − Σ the fitted parts' humanity costs`, never below 0. The cost is fixed when the part is fitted
(replacement or elective, see the tier table). Examples: a peg leg 100; two cyborg legs after losing both 70; a powered
arm, two cyborg legs and an elective scope eye 100 − 8 − 15 − 15 − 35 = 27; four elective Network-grade limbs 0.

| humanity | effect (each tick, at home) |
|---|---|
| ≥ 70 | none |
| < 70 | mood capped at `40 + 60 · humanity / 70` (83 at humanity 50, 70 at 35, 40 at 0) |
| < 50 (detached) | their partner's bond erodes by `0.05 · (50 − humanity) / 50` per tick (0.6 a day at humanity 25); below 40 the couple splits ("X says Y is not the person they fell for"); their children's mood is capped at 70; every 48 ticks a 30% chance of a line in the log ("Rae flexed a hand that is not theirs for a long time…") |

A bond starts at 100 when a couple forms, so a detached survivor at humanity 25 keeps their partner about 100 days;
at humanity 0, about 50.

## Upkeep

Powered parts and up are serviced automatically every `serviceDays` from the stockpile. While the colony cannot pay, the
part's upkeep drops 0.05 a day down to 0.6 (its efficiency is quality × upkeep: a cyborg leg at 1.3 wears down to 0.78);
the first missed service is logged, and the next paid one restores it.

## Network interference

Network-grade parts are the best there is, but the Network still knows its own. At the start of every round each such
part of an active unit unlocks and is rolled again; it locks up for the round (efficiency 0, its hooks and set bonuses
lapse; logged as `LimbLocked`) with

```
chance = min(0.60, 0.15 · Linked on the Network within 6 tiles
                   + 0.15 if a hostile with Override is within 6
                   + 0.20 if any signal jammer covers the unit — your own side's too)
         · (1 − cyber_interference_reduction)
```

So a jammer is a real trade-off for a Network-grade party, Linked Salvage halves the risk, and a jammed Linked (off the
Network) does not count. Nothing is rolled without such a part or a source nearby, so other runs are unchanged.

## Hook ids added

| hook id | meaning | from |
|---|---|---|
| `surgery_success_bonus` | colony: added to a surgery's success chance when this medic leads | Surgeon I/II, Cyber-Surgeon |
| `surgery_speed_bonus` | colony: surgery effort multiplier bonus when this medic leads | Surgeon I/II |
| `cyber_surgery` | colony flag: can lead surgery on T3+ parts | Cyber-Surgeon |
| `cyber_interference_reduction` | share of the interference chance removed | Linked Salvage |
| `armor_bonus` | flat damage reduction added | Bulwark set |
| `thermal_radius` | hostiles within this many tiles are seen through walls and stealth | Thermal / Network-grade Eye |
| `night_detection_bonus` | detection added at night — a hook for the day-night cycle, read by nothing yet | Thermal / Network-grade Eye |

Fitted parts also give existing hooks (`accuracy_bonus`, `melee_damage_bonus`, `hack_chance_bonus`, `carry_bonus`,
`stealth_bonus`) through their node and set effects.

## Saves

Colony save 11 (scavenge save 6, protocol 5). Older prosthetics map into the tree: a part with an item outside the tree
(none before items existed) becomes the Hook Arm / Peg Leg below quality 1 and the Cybernetic Arm / Leg (T3) from there,
keeping its quality, and pays its tier's replacement humanity (so old cyborg legs cost 15 each; crude ones nothing).
Every body gains healthy eyes; upkeep starts full; the colony knows what it has fitted or owns.

## Models for remnants-assets

Until the rigs branch lands, the game maps every tier onto the existing meshes: T0 → `prosthetic_{arm,leg}_{hook,peg}_{l,r}`,
T1+ → `prosthetic_{arm,leg}_cyborg_{l,r}` tinted by tier. Each node's `model` id in `cybernetics.json` names its mesh
variant (`prosthetic_{arm|leg}_{model}_{l|r}`), so new meshes drop in by changing that id. Wanted, fitted to the new
bones, left and right each, with the joint node names the current prosthetics use:

| model id (`prosthetic_…`) | for | look |
|---|---|---|
| `arm_mech_{l,r}` / `leg_mech_{l,r}` | T1 Mechanical | scrap-metal frame, cables, leather straps, brass springs |
| `arm_servo_{l,r}` / `leg_servo_{l,r}` | T2 standard | rust-red plates, exposed servos, a power cell on the forearm / calf |
| `arm_labor_{l,r}` | T2/T3 Labor | oversized, hydraulic pistons, wide grip |
| `arm_precision_{l,r}` | T2/T3 Precision | slim, many-jointed fingers, a lens on the wrist |
| `arm_blade_{l,r}` | T2/T3 Blade | forearm housing with a folding blade (open/closed states or a blade node) |
| `arm_hacker_{l,r}` | T3 Hacker | fingertip probes, a deck panel on the forearm, cold cyan status lights |
| `arm_gun_{l,r}` | T3 Gun | a short barrel along the forearm, magazine at the elbow |
| `leg_runner_{l,r}` | T2/T3 Runner | blade-runner foot, light frame |
| `leg_jumper_{l,r}` | T3 Jumper | reverse-knee hydraulic thigh |
| `leg_silent_{l,r}` | T2/T3 Silent | padded sole, damped joints, wrapped in cloth |
| `leg_armored_{l,r}` | T2/T3 Armored | heavy plating, wide stance |
| `arm_cyborg_{l,r}` / `leg_cyborg_{l,r}` | T3 standard (exists) | — |
| `arm_network_{l,r}` / `leg_network_{l,r}` | T4 Network-grade | Linked style: smooth black shell, cyan seams (match `enemy_linked`) |
| `eye_scope`, `eye_thermal`, `eye_network` | eye implants | a lens over one eye socket (scope: barrel; thermal: red; network: cyan), parented to the head |

Mk2 (T3) variants of a specialisation may reuse the T2 mesh with the T3 tint, or get their own `_mk2` mesh.
