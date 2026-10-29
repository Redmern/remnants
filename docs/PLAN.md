# REMNANTS — design + build plan

Working title. Godot 4.7 (.NET/mono) + C#. Assets built in Blender 5.2 (headless, scripted).

## Pitch

AI tried to take the world. Humans fought back and lost. The AI won but came out badly
damaged: its central system (**the Network**) no longer reaches all of its machines.
- **Husks**: disconnected cyborg/zombie-like ex-humans and machines. Not smart, they just
  run old routines. Common, weak to medium.
- **Linked**: units still connected to the Network. Coordinated, smart, very strong. Rare
  early on, common in high-risk zones that also pay out the biggest rewards.

You lead a small group of survivors. You rebuild a settlement (**Colony mode**) and send
a few people into dangerous zones (**Scavenge mode**). Every loss is permanent.

## Mode 1: Colony (real-time with pause, sim ticks)

- **Start**: 5 survivors, 3 adults (at least one man and one woman) and 2 children. Tunable.
- **Resources**: wood, stone, scrap metal, food, cloth, electronics, medicine (tiers unlock later).
- **Buildings**: every building starts at level 1 and is upgraded with materials. Higher
  levels mean more output, slots, and recipes.
  - Gathering: Woodcutter, Quarry, Scrapyard, Farm / Hunting lodge.
  - Core: Town Hall / HQ (raises the cap on building levels and the scavenge party size).
  - Housing: Shelters to Houses (beds = housing capacity).
  - Production: Workshop (tools), Tailor (clothes/armor), Electronics Lab (devices,
    computers), Armory (guns, ammo).
  - Care: Med Bay to Hospital (heals injuries, fits prosthetics), Kitchen (meals).
- **Survivors** have:
  - Attributes: Strength, Agility, Endurance, Intellect, Creativity, Perception (0–100).
  - Skills that grow with use (Woodcutting, Mining, Crafting, Tailoring, Electronics,
    Medicine, Firearms, Melee, Cooking, Farming).
  - **Job output = f(relevant attributes, skill, tool, health)**. Examples: woodcutting is
    mostly Strength and Endurance; tool-making is Intellect and Creativity; tailoring is
    Creativity and Agility. Weights live in data files.
  - **Doing a job trains it**: chopping wood raises Strength and Woodcutting, weapon work
    raises Intellect and Firearms, and so on. Children train faster and grow into what they do.
  - Body: a body-part model (head, torso, arms, legs). Injuries have a severity. A
    permanent loss (for example a leg) leaves a debuff until a **prosthetic** is fitted at the
    Med Bay. Cybernetics can end up **better** than the original limb.
  - Needs: food and housing. Starvation causes damage and then death.
- **Population growth**: adults form relationships, couples have children, children need
  food and care and grow into adults over time. Recruits come from scavenging.
- **Housing pressure**: anyone without a bed (newborns, recruits) has a grace timer. If no
  housing shows up before it runs out, they leave. Without food they starve.

## Mode 2: Scavenge (turn-based, tile-grid tactics, "chess-like")

- Top-down tile map (procedural, from zone templates). Fog of war.
- Party of 1–N survivors. N grows with the Town Hall level. A small population means
  every sortie is a real risk.
- **Movement and attacks depend on the character plus what they carry**: attributes, gear
  weight, clothing, weapon, tools, prosthetics, and injuries together define a
  **MovementProfile** (range, pattern, terrain costs) and a set of **ActionProfiles**
  (reach, pattern, damage, AP cost). Some combinations give chess-like patterns: a
  heavy-armor character moves 1 tile in any direction, a light scout moves 3 orthogonally,
  a grappling tool allows jumps.
- Turn order follows initiative (Agility plus gear). Every unit has action points.
- Enemies: Husks (simple behavior trees: wander, aggro, swarm) and Linked (coordinated
  squad AI that flanks, focuses fire, and calls reinforcements).
- **Zones** have a threat level and a reward tier. Higher-threat zones have more Linked
  units and better loot.
- Map contents: loot caches, materials, blueprints, **NPC survivors who can be recruited**
  (they follow you to extraction and join the colony if you have housing), and later
  other real players.
- **Consequences**: HP goes to 0 → Downed (bleed-out timer) → Dead (permanent). Items
  like a med-kit or an evac beacon can pull a downed or badly hurt survivor out. Injuries
  carry back into the colony (healing time, permanent limb loss).
- **Extraction**: reach an exit tile to bring loot home. A wipe loses the carried loot and
  maybe the people.

## Multiplayer (later, design for it now)

- Scavenge zones become shared instances. Several players' parties sit on the same map,
  turns are simultaneous or interleaved per round, PvP is possible, and loot is contested.
- An **authoritative headless C# server** that reuses `Remnants.Core`. The client only
  sends intents. Colony stays single-player and server-saved.
- So from day one, **all game rules live in `Remnants.Core`**: pure C#, no Godot types,
  deterministic (seeded RNG, no wall-clock), state serializable.

## Architecture

```
remnants/                         (git repo, Godot project + .NET solution)
  Remnants.sln
  src/Remnants.Core/              pure C# rules: Survivors, Colony, Scavenge, Items, Data
  src/Remnants.Server/            (M8) headless authoritative server
  tests/Remnants.Core.Tests/      xUnit, the sim is tested headless
  game/                           Godot 4.7 C# project (presentation + input only)
    assets/generated/             synced from remnants-assets export (do not edit by hand)
  data/                           JSON defs: buildings, items, jobs, enemies, zones
  docs/                           PLAN.md, ARCHITECTURE.md, decisions

remnants-assets/                  (git repo) Blender pipeline
  scripted Blender builds (bpy), style rules, renders, export/*.glb + manifest.json
```

The Godot layer reads Core state and renders it. The UI sends commands
(`AssignJob`, `UpgradeBuilding`, `MoveUnit`, ...) to Core, and Core validates and applies them.

Art direction (first pass): low-poly, chunky readable silhouettes, muted post-apocalyptic
palette (rust, concrete, moss) with cold cyan accents for anything Network-connected.
3D for both modes, with an isometric/top-down camera.

## Milestones

| # | Milestone | Done when |
|---|-----------|-----------|
| M0 | Foundation | solution + Core + tests + Godot project build/run; `./run-tests.sh` green |
| M1 | Colony core (headless) | survivors/attributes/skills, jobs, gathering, build+upgrade, food, housing, sim tick; tests |
| M2 | Colony view | Godot town scene, placeholder or generated models, roster + job panel, build/upgrade UI, time controls |
| M3 | Scavenge core (headless) | grid, turns/AP, movement/action profiles from gear, Husk AI, loot, extraction, down/death; tests |
| M4 | Scavenge view | tactical map scene, unit selection, move/attack highlights, fog |
| M5 | Loop | send party from colony → scavenge → return loot, injuries, recruits; med bay, prosthetics |
| M6 | Life | relationships, births, children growth, job-based training, housing/starvation departures |
| M7 | Crafting tiers | tailor, electronics lab, armory, blueprints, gear affects scavenge profiles |
| M8 | Linked AI + zones | squad AI, threat/reward zones, world map |
| M9 | Multiplayer scavenge | headless server, shared instances, PvP |

Assets run in parallel: style guide → survivors (adult/child, m/f) → level-1 buildings →
Husk → Linked → tiles/props → building upgrade levels → gear.
