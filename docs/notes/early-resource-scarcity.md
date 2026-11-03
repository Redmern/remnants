# Early resource scarcity: notes

Branch `feat/early-resource-scarcity`. Numbers and results: BALANCE.md "Early resource scarcity".

## Interpretations

- **"Employee level"** is the job's skill (Woodcutting, Mining, Crafting for scrap sorting), which grows by working.
  Survivor XP levels give perks, not output; I made skill count for more (skill weight ×2) instead of tying output to
  the XP level.
- **"Same time at a level 1 woodcutter"** (AC-4): Outskirts costs no colony time on the road, so a run counts as one day;
  a far zone as its round trip (`max(1, 2 × travelDays)` days). Compared against two fresh adults (stats 45, no skill,
  no tools) for a day of work hours, as `ColonySim.OutputPerDay` gives it with the day/night cycle on.
- **"All gathered materials"**: the buildings with `produces` (wood, stone, scrap). Food (farm, greenhouse, hunting)
  was left alone so AC-6 (no starvation) stays safe; cloth only comes from loot, salvage and fiber cloth.
- **Sandbox setting**: a picker on the Sandbox card of the New Game screen (not the Settings panel), remembered in
  settings.cfg; Core stores what was used in `ColonyState.NewGameOptions`.

## Deviations and trade-offs

- **Tools are new**: `Survivor.ToolQuality` existed but nothing set it. Tools are stockpile items (`jobs.json tools`),
  not equipment: no slot, no UI to hand them out; the best go to the lowest survivor ids. New items `felling_axe`,
  `rock_drill`, `cutting_torch` have no icons yet (art not in scope).
- **Haul is abstract**: materials appear on return (not carried, no weight), so carrying limits do not cap it. It skips
  story missions and needs someone home. Seeded from the expedition seed, so it does not shift the colony RNG stream.
- **No save version bump**: `NewGameOptions` and `ExpeditionReport.Haul` are new fields with defaults; older saves load
  with them empty (Sandbox default). Nothing to migrate.
- **Range 2–6**: the Shelter has 6 beds; more would leave people homeless (they leave after 3 days).
- **Town Hall L2 / House costs** were lowered in a first try and restored: the haul plus cheaper costs brought Town Hall
  2 nine days early.
- Late game now out-gathers the old numbers (×1.5 at L3 with skill and best tool). Intended payoff; watch 90-day runs.
- Story's first raid is held less often (41 vs 47 of 60) with one person fewer.

## Open questions

- Should tools be visible per worker in the building panel (which tool, its bonus)? Only the output number shows it now.
- Should the bot use multiple jobs per person? It still gives everyone one job, so the bot is a little pessimistic
  about the smaller start.
- Haul for Flooded Mall and later: none for now (their loot tier carries them).
