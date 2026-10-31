# Review guide

A short guide to playing Remnants for review: how to start it, what to try in 30–60 minutes, shortcuts that skip the
slow parts, and where to look at the art on its own. No programming needed: every command below is copied into a
terminal as-is.

## 1. Start the game

```sh
godot-mono --path ~/repos/newgamev2/remnants/main/game
```

The main menu opens. **New Game** offers **Story** (a fixed start, radio calls and missions) or **Sandbox** (a random
colony, no story, just the valley).

If every building and person looks like a grey box or a plain capsule, the art hasn't been imported yet. Run this once,
wait for it to finish, then start the game again:

```sh
godot-mono --headless --path ~/repos/newgamev2/remnants/main/game --import
```

Saves go into Godot's user folder, so a review doesn't touch anything in the repository.

## 2. Review checklist (about 30–60 minutes)

Work down the list in order. Each step says what to do and what to look for. Note anything that looks wrong, reads
badly or confuses you. Section 5 says which notes help most.

### Story mode (about 25 minutes)

1. **New game.** Main menu → New Game → **Story**. Look at the colony from above: can you tell the buildings apart?
   Can you tell who is who? Turn the camera with **Q** / **R**.
2. **Colony basics.** Click a survivor (or press **Tab** to step through them): read their panel (health, mood, body
   parts, job). Open the build menu (**B**) and place something cheap, such as a farm: it becomes a **construction site** (an outline,
   a scaffold, the materials in piles). Select it and click "+" under Builders to put someone on it (idle people lend a
   hand too), then watch it rise; hover the site for the time left. Upgrades take days and the building keeps working
   meanwhile. Unpause with **Space** and speed
   time up with **1 / 2 / 3 / 4** (normal, 2×, 4×, 8×). A day takes 2 minutes at normal speed (Settings → Game → Day
   length: 1 / 2 / 4 / 8 minutes). Watch people walk to work, eat and sleep, and the light glide from dawn to dusk.
   Click a building with jobs (e.g. the Woodcutter): a "+" on a free worker slot lists everyone with their job fit
   and status; click one to assign them, click a worker's portrait to unassign or open them. In build mode the plot
   footprints appear; outside it the town has no plot squares.
3. **Story calls.** Before long the radio crackles: a toast says someone is calling. Open the call. Look at the
   portrait, the dialogue frame and the text. Press **Space** or **Enter** to move the conversation on. The journal
   (story panel) lists the missions, and the first one is **M1 "Home"**.
4. **First expedition, with fog and an ambush.** Press **E** for the expedition planner, pick two or three adults, give
   them gear and launch. On the field map you play in turns: click a lit tile to move, a red target to attack, and
   **Space** ends the turn. You can only see what your people can see (the fog). Enemies can hide in it and ambush
   you. Try **O** (overwatch: shoot whatever moves into view), **B** (brace), **L** (lookout), **F** (take loot from
   a pile) and **X** (extract: leave the map with what you carry). Check whether the danger reads clearly before it
   hits you.
5. **Coming home.** After extracting, the after-action report shows what came back and who got hurt. A wounded
   survivor goes straight to the Med Bay if you have one.
6. **Crafting benches.** Once you have the buildings (or use the shortcuts in section 3): the **Armory** and **Tailor**
   (weapons, armor, clothes), the **Gear Bench** (mods on weapons and armor), the **Kitchen** (meals), the
   **Dispensary** (medicine) and the **Electronics Lab** (devices, drones). Queue something, then look at the item
   icons and the 3D previews.
7. **A raid.** Press **F** to place defenses (walls, gate, sandbag nest, towers, traps). Sooner or later a warning
   says raiders are coming. Post guards, then fight the defense battle on your own colony map and read the raid report
   afterwards.
8. **Vehicles.** Build a **Garage**, then pick a vehicle in the expedition planner. It travels faster on the world map
   and parks in the field to carry loot (**R** loads it).

### Sandbox (about 10 minutes)

9. Main menu → New Game → **Sandbox**. You get a random start and no radio story. Is it clear what to do first without
   the story to guide you? Build, run time at 4× or 8×, send one expedition and see how the first few days feel.

### Multiplayer with the bot (about 10 minutes)

10. A computer-controlled partner (the bot) joins you on a co-op field mission. Open **two** terminals.

    In the first, start a co-op server and the bot (leave this terminal open):

    ```sh
    cd ~/repos/newgamev2/remnants/main
    dotnet run --project src/Remnants.Server -- --port 7791 --zone outskirts --coop --players 2 &
    dotnet run --project src/Remnants.Server -- --bot 127.0.0.1:7791 --name Rook
    ```

    In the second, join it from the game:

    ```sh
    godot-mono --path ~/repos/newgamev2/remnants/main/game -- --mp-join=127.0.0.1:7791 --mp-name=You --mp-ready
    ```

    (Or start the game normally: **Multiplayer → Join**, address `127.0.0.1:7791`, then **Ready**.) Your ally "Rook"
    shows in soft green. Try pinging (**Z** danger, **V** go here, **N** loot) and giving an item (**G**). When you're
    done, close the game and press **Ctrl+C** in the first terminal.

## 3. Shortcuts that save time

### Keys in play

| Where | Key | What it does |
|---|---|---|
| Everywhere | **Esc** | Pause menu / back |
| Colony | **Space** | Pause / resume time |
| Colony | **1 / 2 / 3 / 4** | Normal speed / 2× / 4× / 8× (fast-forward; a day is 2 min at normal speed by default) |
| Colony | **B / F / E / I / P** | Build menu / place defenses / expedition / items / power overlay |
| Colony | **Tab**, **Q / R** | Next survivor, turn the camera |
| Field | **Space** or **Enter** | End turn |
| Field | **1–3**, **4–9** | Attacks, items |
| Field | **O / B / L** | Overwatch / brace / lookout |
| Field | **F / I / X / C / H / K** | Loot / salvage / extract / carry a downed ally / hack / scout drone |
| Dialogue | **Space** or **Enter** | Next line |

The full list is in **Settings → Controls**.

### Jump straight to a scene (demo flags)

Add these after `--` when starting the game. Each one sets up a scene so you don't have to play up to it. For
example:

```sh
godot-mono --path ~/repos/newgamev2/remnants/main/game -- --colony --raid-demo=battle
```

| Flags after `--` | What you get |
|---|---|
| `--new-game=story` / `--new-game=sandbox` | Skips the menu and starts a new game |
| `--colony --demo` | A small colony with a farm and woodcutter, people at work |
| `--colony --construction-demo=50` | A Workshop half built, two builders on it (`=0`, `=100`, `=upgrade` for the Woodcutter on scaffolding) |
| `--colony --crafting-demo` | A grown town with every workshop, the Armory open with a queue |
| `--colony --gear-demo` | The Gear Bench open on a pistol with a sight previewed |
| `--colony --food-demo --food-view=kitchen` | The Kitchen |
| `--colony --medicine-demo --dispensary` | The Dispensary |
| `--colony --power-demo --bench-tab=Devices` | Generators, batteries and the Electronics Lab |
| `--colony --night-demo` | The town at night with a floodlight and the night shift |
| `--colony --story --story-demo=radio` | A story radio call (also `journal`, `briefing`, `codex`) |
| `--scavenge --story-mission=m6` | Plays a story mission directly (`m1` to `m16`, `m17_gate`, `m17_choir`, `m17_core`) |
| `--colony --raid-demo=placement` | Defenses being placed (also `warning`, `prep`, `battle`, `report`) |
| `--colony --garage-demo` | A Garage with vehicles and upgrades |
| `--colony --world-demo --vehicle=motorbike` | A party driving on the world map |
| `--scavenge` | A quick field mission (add `--vehicle=buggy` for a parked vehicle) |
| `--scavenge --fog-demo` | A fog ambush, staged |
| `--scavenge --tech-demo=hack` | A hack in progress (also `turret`, `drone`, `medic`, `cargo`, `combat`, `emp`) |
| `--colony --loop-demo` | A party sent out on an expedition, plus a Med Bay and Workshop |
| `--colony --family-demo` | Fast-forwards until a baby is born |

The main menu also has **Skirmish (debug mission)**: a quick field fight with a ready-made party.

## 4. Where to see the art on its own

Each contact sheet shows models from three angles, so you can judge the style without playing. All of them are in
`~/repos/newgamev2/remnants-assets/main/review/`:

| File | What it shows |
|---|---|
| `contact_sheet.png` | Every model in the game on one sheet: the full picture |
| `contact_sheet_batch2.png` | Upgraded buildings, Husk crawler and Linked enemies, everyday gear (packs, weapons, armor), cover props, simple prosthetics |
| `contact_sheet_batch3.png` | Fog-of-war gear and map markers |
| `contact_sheet_batch4.png` | Hackable devices (cameras, doors, turrets, terminals), the scout drone and hacking gear |
| `contact_sheet_batch5.png` | Vehicles, the Garage, the fuel still, map tokens and vehicle parts |
| `contact_sheet_story.png` | Every story asset: the named characters, the Long Haul trucks, story buildings and set pieces |
| `portrait_sheet.png` | Every character portrait (neutral, one mood, and the radio version) |
| `contact_sheet_weapon_mods.png` | The weapon mods (sights, suppressors, grips and so on) |
| `contact_sheet_armor_mods.png` | Armor, clothing and helmet mods, and headwear |
| `mod_fit_sheet.png` | Weapon mods fitted onto the weapons |
| `armor_fit_sheet.png` | Armor and clothing worn on the body |
| `contact_sheet_prosthetics.png` | Prosthetic limbs by tier and kind, and eye implants |
| `fitcheck_prosthetics.png` | Survivors wearing a mix of prosthetics, posed |
| `contact_sheet_vehicle_upgrades.png` | Vehicle upgrade parts (armor plates, cages, rams) |
| `fit_sheet_vehicle_upgrades.png` | Those upgrades fitted on every vehicle tier |
| `contact_sheet_food.png` | Food buildings and props (hunting lodge, smokehouse, crops, cold cellar) |
| `icon_sheet_food.png` | Food icons at their real size on the game's panels |
| `animations.html` | Open in a browser: every character's animations as small looping clips |
| `audio_index.html` | Open in a browser: every sound with a play button |

**Placeholders.** Some things don't have their own art yet and show a stand-in. The list is
`docs/MISSING_ASSETS.md` in the game repo, and `docs/missing_assets/` has screenshots of the most visible spots:

- `defenses_placeholders.png`: traps, the floodlight, the alarm bell and the top-tier tower drawn as simple shapes. The
  screenshot is older than this pass: the gate, sandbag nest, spike strip and first two towers now use real models.
- `gear_bench_line_icons.png`: gear shows a simple line icon where a rendered item picture will go.
- `story_m6_long_haul.png`: Marrow on the Long Haul, taken before the fix that gave him his own model (he walked as a
  stock survivor; he now has his own).

Mood portraits work the same way: where a character has no picture for a mood (warm, tired, wry and so on), the game
shows their neutral or worried portrait instead.

## 5. Feedback that helps most

- **Art style.** Does it hang together: buildings, people, enemies, icons and portraits? Is anything off-style, too
  dark, too busy or too plain? Which contact-sheet pieces do you like most and least? Since the next art batch depends
  on this, it's the most useful note of all.
- **Readability.** Can you tell at a glance who is yours, who is an enemy, what is cover and what is loot? Do icons
  say what they mean? Is any text too small or hard to read?
- **Difficulty.** Did the colony starve or coast? Was the first expedition or the first raid too harsh or too easy?
  Where did you feel stuck or bored?
- **What's confusing.** Anything you didn't understand, couldn't find, or did by accident. Note what you expected to
  happen and what happened instead.

Screenshots help. A rough note such as "day 3, the raid report, couldn't tell who died" is plenty.
