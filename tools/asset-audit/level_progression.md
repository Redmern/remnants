## Level progression

Hand-written review (construction pass, 2026-10-31), kept verbatim by `--write` from `tools/asset-audit/level_progression.md`.
Construction makes level 1 a quick, cheap build and upgrades the long ones, so each building's models should read as a
progression: **L1 a simple shed or frame** (open sides, stored materials, a tarp), **L2 an enclosed building**, **L3 a proper
building** (more storeys, machinery, lights). Judged in the town with `--colony --construction-demo=levels
--level-types=a,b,c,d` (each type at L1/L2/L3 in a row; screenshots below). No new models were made; this is a list for the
asset pass after the style review.

| Building | Models | Reads as a progression? | What the asset pass should change |
|---|---|---|---|
| Woodcutter | L1–L3 | **Yes.** L1 is an open shed over stacked logs (the "simple shed where logs are stored"); L2 an enclosed shed; L3 a workshop with machinery. | L3 could be more of a building (walls, a second storey or a saw hall) rather than a bigger open shed. |
| Farm | L1–L3 | **Yes.** A small plot and hut, then a barn, then a greenhouse tunnel and silo. | — |
| Hunting Lodge | L1–L3 | **Yes.** A drying-rack frame, a log cabin, a bigger cabin. | — |
| Garage | L1–L3 | **Yes.** An open lean-to over a car, a metal garage, a big workshop. | — |
| Shelter | L1–L3 | **Yes.** A hut, a cabin, a tall house. | — |
| Quarry | L1–L3 | **Weak.** All three are stone heaps with a crane; the step up barely shows. | L1: a few cut blocks, a hand winch and a tarp. L3: a proper crusher shed or cutting hall. |
| Scrapyard | L1–L3 | **Weak.** L1 already has the crane and dome of a big yard. | L1: a fenced pile with a sorting table under a tarp; keep the crane for L2–L3. |
| Workshop | L1–L3 | **Weak.** L1 is already a brick building with a chimney. | L1: an open-sided bench shed; L2 the current L1; L3 a larger hall. |
| Town Hall | L1–L3 | **OK.** L1 is a large hall from the start (it is the first building and never built by hand). | — |
| Med Bay | L1–L3 | **OK.** White clinic blocks that grow; L1 is a full building. | L1 could be a tent/field station. |
| Tailor | L1 only | **No.** L2/L3 show the L1 model scaled ×1.12 / ×1.24. | L2 and L3 models; L1 as a market-stall shed fits. |
| Kitchen | L1 only | **No.** Scaled L1. | L2 and L3 models. |
| Armory | L1 only | **No.** Scaled L1. | L2 and L3 models; L1 should be the simpler one. |
| Electronics Lab | L1 only | **No.** Scaled L1. | L2 and L3 models. |
| House | L1 only | **No.** Scaled L1. | L2 and L3 models (more rooms, a storey). |
| Fuel Still | L1 only | **No.** Scaled L1. | L2 model. |
| Smokehouse, Greenhouse, Cold Cellar, Radio Mast, Watchtower | L1–L2 | **OK**, two steps each. | — |

Construction itself (built in code, no assets needed now, nice to have later): a **scaffold** kit (poles, boards, braces),
a **log pile / plank stack** and a **brick pallet** prop for delivered materials (logs are drawn as cylinders, stone uses the
`rubble` prop, other materials the `loot_crate`), and **hammer / saw work loops** (sites use `work_workshop` meanwhile).

![levels_gathering.png](missing_assets/levels_gathering.png)

Rows from the back: Woodcutter, Farm, Quarry, Scrapyard; levels 1, 2, 3 from left to right.

![levels_core.png](missing_assets/levels_core.png)

Rows from the back: Shelter, Workshop, Town Hall, Med Bay.

![levels_production.png](missing_assets/levels_production.png)

Rows from the back: Hunting Lodge, Garage, Tailor, Kitchen.
