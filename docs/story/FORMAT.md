# Story data format

The story's data lives in `data/story/`. Core loads it with `StoryDataLoader.LoadFromDirectory("data/story")`, and
`StoryData.Validate(game, scavenge)` lists every broken reference (the tests run it). JSON is camelCase, enums are
strings, and comments and trailing commas are allowed. Act 1 (M1–M5) is the worked example: `missions/act1.json`,
`dialogue/act1_*.json`, `maps/*.json`.

| Path | Holds | Code |
|---|---|---|
| `characters.json` | an array of characters | `Story/StoryDefinitions.cs` `CharacterDef` |
| `missions/*.json` | arrays of missions, in campaign order (files sorted by name) | `MissionDef` |
| `dialogue/*.json` | arrays of dialogue graphs | `DialogueDef` |
| `maps/*.json` | one handmade map or fixed room per file | `Scavenge/HandmadeMaps.cs` `HandmadeMapDef` |

## Characters

```json
{ "id": "iris", "name": "Iris Vahn", "callSign": "Lantern", "pronouns": "she/her", "role": "the radio voice" }
```

Portraits resolve by `id`: `game/ui/portraits/{id}.png`, falling back to a placeholder. Pell is a real colony survivor
(`Survivor.UniqueId = "pell"`) and gets the rendered survivor portrait.

## Conditions (`requires`, choice `requires`, effect `when`)

Every field that is set must hold:

```json
{ "mission": "m3", "outcome": "done", "building": "med_bay", "level": 1, "townHall": 2, "day": 30,
  "flag": "cole_respect", "min": 1, "max": 5, "item": "hacking_kit", "count": 1, "alive": "marrow" }
```

- `mission`: done (closed). Add `outcome` (`done` / `failed`) to tell them apart.
- `flag`: with neither `min` nor `max`, the flag is non-zero.

## Effects (rewards, `fail.effects`, dialogue line and choice `effects`)

`{ "kind": K, "id": "...", "value": 1, "text": "...", "when": { condition } }`

| kind | does |
|---|---|
| `SetFlag` / `AddFlag` | campaign flag `id` = / += `value` |
| `Schematic` | grants schematic `id` through `ISchematicGrants`. A duplicate gives Research Data. |
| `Item` | `value` × item `id` into the inventory. A negative value takes items away. |
| `Resource` | `value` of resource `id` into the stockpile |
| `ResearchData` | `value` Research Data |
| `RevealZone` | zone `id` becomes Rumored, or Scouted with `value: 2`. `text` overrides the debrief line. |
| `Call` | queues dialogue `id` as a radio call, `value` hours from now (0–1 = now) |
| `Character` | character `id` is met (`value: 1`) or joins (`value: 2`) |
| `Fallback` | a reward for a feature not built yet (vehicles, limb tiers). It only shows `text`. Grant the schematic too. |
| `Note` | a line in the colony log and the debrief |

## Dialogue

```json
{ "id": "m2_brief", "mode": "Radio", "caller": "iris", "title": "First Light",
  "nodes": [
    { "id": "a", "speaker": "narration", "text": "(The Radio Mast crackles.)", "next": "b" },
    { "id": "b", "speaker": "pell", "text": "It's doing the thing again.", "next": "c" },
    { "id": "c", "speaker": "player", "text": "", "choices": [
        { "text": "Who are you?", "next": "c1" },
        { "text": "Talk to them.", "next": "d", "hint": "Cole will remember this",
          "effects": [ { "kind": "AddFlag", "id": "cole_respect", "value": 1 } ],
          "requires": [ { "flag": "met_cole" } ] } ] },
    { "id": "c1", "speaker": "iris", "text": "Call me Lantern.", "effects": [ { "kind": "Call", "id": "m2_more", "value": 6 } ] }
  ] }
```

- **`mode`:** `Radio` (radio frame, call sign) or `InPerson`.
- **Start and end:** the graph starts at its first node. A node without `next` and without `choices` ends it.
- **Choices:** at most 3 per node. A node with choices has no `next` of its own. At least one choice must have no
  `requires`, so the dialogue never dead-ends.
- **`speaker`:**
  - a character id;
  - `player`: the colony's lead survivor, the highest-level adult at home;
  - `pell`: the colony's Pell;
  - `narration`: no name, shown in italics.
- **`effects`:** a node's effects apply when the line is shown, a choice's when it is picked. Every line and choice goes
  to the log (history).
- **`hint`:** the obvious consequence, shown under the choice. Never numbers.
- **Placeholders in text:** `{survivor:leader}`, `{survivor:best_medic}`, `{survivor:best_shot}`, `{name:<characterId>}`,
  `{colony.population}`, `{colony.dead_last_month}`, `{colony.day}`.
- **Played to the end:** a dialogue played to its end is added to `CampaignState.Heard`, which `Scene` objectives wait
  for.

## Missions

```json
{ "id": "m2", "act": 1, "name": "First Light", "type": "Field", "main": true,
  "summary": "...", "teaches": "...", "needsText": "M1",
  "zone": "outskirts", "threat": 1, "map": "handmade:relay_shed", "room": null, "maxParty": null,
  "requires": [ { "mission": "m1" } ],
  "briefing": "m2_brief", "debrief": "m2_debrief",
  "field": { ... FieldMission ... },
  "rewards": [ { "kind": "Schematic", "id": "sch_watchtower" } ],
  "fail": { "retryDays": 3, "call": "m2_retry", "effects": [], "extraEnemies": [ { "enemy": "husk", "marker": "shed_in" } ] } }
```

- **`type`:**
  - `Colony`: played at home with `colonyObjectives` (below).
  - `Field`: played in a zone with `field`.
- **`main`:** true draws a gold diamond on the world map, false a hollow one. `task: true` marks a colony task (journal
  only).
- **`map`:**
  - `handmade:<id>`: a full handmade map, with its fixtures only.
  - `perimeter`: the colony perimeter variant for the Town Hall level (`perimeter_th1..3`).
  - `generated`: the zone's generated map. `room: "<id>"` stamps a fixed room into it.
- **Unlocking:** a mission unlocks, in file order, when all its `requires` hold. Its `briefing` then calls on the radio.
  A field mission becomes Available; a colony mission becomes Active.
- **Results:**
  - **Done:** pays `rewards` and queues `debrief`.
  - **Failed:** applies `fail.effects` and queues `fail.call`. With `retryDays` the mission comes back after that many
    days, with `extraEnemies` added to the map. Without it the mission closes as done with outcome `failed`, so later
    missions still unlock.
  - **Incomplete:** the party left early. The mission stays Available.
- **Field flags:** flags set in the field (trigger `Flag`) are **added** to the campaign flags when the party is home.

### Colony objectives (`type: Colony`)

```json
{ "id": "mast", "kind": "Build", "text": "Build the Radio Mast", "building": "radio_mast", "level": 1 }
```

| kind | fields | done when |
|---|---|---|
| `Build` | `building`, `level` | the colony has that building at that level or higher |
| `HaveItem` | `item`, `count` | the inventory holds `count` |
| `HaveResource` | `resource`, `count` | the stockpile holds `count` |
| `Patient` | — | someone is admitted to a Med Bay |
| `ChildJob` | `job` | a child works that job |
| `Scene` | `dialogue`, `phase` | every other objective is done. At `phase` (`Dawn`/`Day`/`Dusk`/`Night`) the dialogue calls; the objective is done once it has been played to the end. |

## Field script (`field`, a `FieldMission`)

```json
{ "id": "m2", "name": "First Light", "atHome": false, "failOnWipe": true,
  "objectives": [ { "id": "reach", "kind": "Reach", "text": "Reach the relay shed", "marker": "shed", "radius": 1 } ],
  "triggers": [ { "id": "t1", "when": { "kind": "Round", "round": 1 },
                  "actions": [ { "kind": "Say", "speaker": "Lantern", "text": "Husks walk the same loop forever." } ] } ] }
```

- **`atHome`:** the fight is at the colony (a defense). The run ends when the mission is done or failed, and everyone
  still on the map is home, downed included.
- **`failOnWipe`:** a party that gets nobody home fails the mission.
- **`excludeEnemyTags`:** a generated zone's own enemies with any of these tags are left out (`["network"]`: no Linked).

Objectives: `{ id, kind, text, marker, radius, tag, item, count, optional, failsMission, hidden }`

| kind | passes | fails |
|---|---|---|
| `Reach` | a party survivor is within `radius` of a `marker` tile | — |
| `Hack` | the device tagged `tag` is hacked by the party | the device is destroyed |
| `Retrieve` | `item` is carried out (at extraction; the panel shows "carried" meanwhile) | — |
| `Escort` | the unit tagged `tag` leaves with the party | it dies |
| `Aid` | a party unit treats the unit tagged `tag` (heal or stabilize) | it dies |
| `Survive` | `count` rounds pass after it becomes active | — |
| `Defend` | the unit tagged `tag` stands at the end | it dies |
| `KillTag` | `count` units tagged `tag` are dead | — |
| `Undetected` | at the end, if never broken | a hostile (or a camera) picks out a party unit |

- **`failsMission`:** failing this objective fails the mission.
- **`optional`:** the objective does not count toward done.
- **`hidden`:** the objective starts inactive until a trigger `Activate`s it.

Triggers each fire once, in definition order: `{ id, when: { kind, ... }, actions: [ ... ] }`

| when.kind | fields |
|---|---|
| `Start` | the run starts |
| `Round` | `round`: at the start of that round |
| `Enter` | `marker`, `radius`: a party survivor comes that close |
| `ObjectiveDone` / `ObjectiveFailed` | `objective` |
| `PickedUp` | `item`: a party unit picks it up |
| `Sees` | `tag`: the party sees a unit with that tag |
| `Alarm` | a hack failed |
| `Chosen` | `choice`: `"promptId:optionId"` |

| action.kind | fields | does |
|---|---|---|
| `Spawn` | `enemy`, `count`, `marker`, `tag`, `goal`, `side` | enemies at (or near) the marker tiles, hunting toward `goal` |
| `Say` | `speaker`, `text` | a chatter line above the action bar. It does not pause. |
| `Activate` / `Complete` / `Fail` | `objective` | changes that objective |
| `Flag` | `flag`, `value`, `add` | sets a field flag (sent home in the result, where it is added) |
| `SetSide` | `tag`, `side` | puts the tagged units on `party`, `neutral`, `hostile` (a band of their own, foe to all) or `enemies` |
| `Follow` | `tag` | the tagged units follow the nearest party survivor (escort) |
| `Prompt` | `prompt: { id, speaker, text, options: [ { id, text, hint } ] }` | a pausing choice. The answer fires `Chosen` triggers. |
| `Split` | `tag` | halves what is left in the tagged device (a split of the spoils) |
| `Wake` | `tag`, `goal` | idle tagged units start hunting toward `goal` |

## Handmade maps and rooms (`maps/*.json`)

```json
{ "id": "relay_shed", "zone": "outskirts", "room": false,
  "rows": [ "#####E####", "#..a.....#", "#S.......#" ],
  "markers": { "a": "antenna", "h": "husk_a" },
  "caches": [ { "marker": "antenna", "items": [ "relay_antenna" ] } ],
  "devices": [ { "kind": "Terminal", "marker": "term", "tag": "terminal", "items": [ "manual_electronics" ] } ],
  "enemies": [ { "enemy": "husk", "marker": "husk_a", "tag": "patrol", "count": 0, "patrol": [ "p1", "p2" ], "name": null, "side": null } ],
  "npcs": [ { "name": "Juniper", "marker": "scout", "tag": "juniper", "character": "juniper", "hp": 18, "noRecruit": true, "sex": "Female" } ],
  "structures": [ { "name": "Radio Mast", "marker": "mast", "hp": 120, "tag": "mast" } ],
  "towers": "tower" }
```

- **Grid characters:**
  - terrain: `#` wall, `.` floor, `~` water, `%` rubble, `=` cover, `P` pylon;
  - `S` spawn and `E` exit (both floor);
  - any other character must be named in `markers`. It is a floor tile with that name, and many tiles can share a
    name.
- **Full maps:** need a spawn and an exit, and every passable tile must be reachable from the spawn. Devices block their
  tiles, except cameras and turrets.
- **Rooms (`room: true`):** no spawn or exit. A room is stamped into the generated map deep in the zone, with a ring of
  floor round it and paths cut to the spawn and to every exit. Its doorways are its passable border tiles. Random loot,
  enemies and devices are kept out of it.
- **Fixtures:**
  - **`enemies`:** one unit per marker tile (`count` 0 = all of them). `patrol` markers are walked in a loop while the
    unit is idle. `side: "neutral"` makes it a bystander until a trigger changes its side.
  - **`npcs`:** neutral survivor-like units. `character` makes the unit a story character: it never joins the colony as
    a recruit, and it comes home in the result's `Escorted`.
  - **`structures`:** units on the party's side that take no turns (the Radio Mast). `Defend` them.
  - **`towers`:** the first N tiles of that marker are raised watchtower posts (more sight and detection), N = the
    colony's Watchtower levels.
- **Saves:** a saved run keeps its whole script, its markers and the map's id and hash (`HandmadeMaps.Hash`). It does
  not reload the map from data.
