# Early resource scarcity: notes

Branch `feat/early-resource-scarcity`. Numbers and results: BALANCE.md "Early resource scarcity". This file is the
branch's progress notes (interpretations, deviations, trade-offs, open questions), kept in the repo next to the docs so
the reasoning ships with the change; delete it once the branch is merged if notes should not live in `docs/`.

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
- Tools go to everyone on the job's roster, on shift or not (a Night Shift crew holds tools too). Simple and stable; a
  per-hour handout would need tools to move between people.

- **TradeNetTests pins the stock** (`Colony()` tops wood/stone up to 60/30) instead of rewriting its expectations: the
  trade tests check escrow and refund arithmetic from a known stock in about a dozen asserts, not new-game balance.
  Pinning in the one fixture keeps them independent of `rules.newGame.stock`, so the next stock change does not touch
  them. ConstructionTests read costs from data instead, since there the cost is the subject.

## Review follow-ups (fixed)

- Building tooltip, survivor overview and help breakdown now show the stockpile tool (they used the unset
  `Survivor.ToolQuality`).
- Bot: falls back to the basic tool when the good one cannot be made, checks the recipe's Crafting skill, finds recipes
  by item. Sandbox picker drops a remembered pick outside the range. A tiny start always has a man and a woman. Tool
  qualities are validated to 0..1.

- Round 1: **chained Outskirts runs** (no colony time on the road) gave ~30 wood on day 1. The haul now recovers over a
  day per zone (`ZoneStatus.LastHaulTick`, zone `haulRecoveryDays` 1): a run sooner gets elapsed / a day of it. Loot
  and caches are unchanged (they had the same property before). `WorkTools.Handout` is computed once per production tick.

## Open questions
- Should the building panel name each worker's tool? The breakdowns show the bonus; no per-tool list yet.
- Should the bot use multiple jobs per person? It still gives everyone one job, so the bot is a little pessimistic
  about the smaller start.
- Haul for Flooded Mall and later: none for now (their loot tier carries them).
