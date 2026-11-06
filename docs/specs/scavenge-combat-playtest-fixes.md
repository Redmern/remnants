# Spec: scavenge combat playtest fixes (remnants, base origin/main 5293faf+)

Stage: Specify. Gate: user approves.

## Problem
Windows playtest of v0.5.3: "no defence skills like block, battling feels strange, not logical", and "just a walk towards enemy and attack". Cause (verified in discover.md): block exists only as cards (1 Parry in 18); enemies aim at the target's current tile while walks resolve first, so walking away dodges almost everything (fizzled actions 2.6 -> ~61); enemies never push, dash or defend; items, hacking and drone shots resolve instantly during planning; tooltips describe the old unit-targeted system; two sounds are unmapped (audio self-test fails).
Main goal: a combat with counters, situational choices, reads and baits, bounded luck, so that "if he does this I do this" matters. Measured, not asserted.

## Kept design
Per round each unit has MP, AP, stamina; plans one move, one action, one card. Only movement is telegraphed. Movement resolves first, then actions by priority. Attacks and defence act on tiles with a visible pattern. A block can stun. Successful actions fill adrenaline.

## Delivery decision (user)
ONE PR against main, one commit per unit (in order), stacked locally only. Not merged, no release, no config/version bump. Evidence and a Merge Danger section in the PR. spec copied to docs/specs/scavenge-combat-playtest-fixes.md.

## Scope
In: Defend action; counter relations; enemy aim/techniques/tendencies; planned item/hack/drone actions; situation previews; bounded luck with visible odds; bots and policies; stale text; two sounds; tuning and BALANCE.md; protocol/save bumps as needed.
Out: new art/audio assets; game version bump; releases; merging; colony-side combat changes; looting and field commands that are out of combat (stay instant unless queueing adds value; each call recorded in progress/open-decisions).

## Acceptance criteria (EARS)

### Defend (item 1)
- AC-1. The action row shall always offer Defend, occupying the Action slot, available to every unit with AP, with no card needed.
- AC-2. When the player plans Defend, the player shall pick tile(s) and the pattern shall be shown on the field in the same visual language as attack patterns, in the pending plan and in the resolve playback.
- AC-3. The Defend pattern and block strength shall come from the unit's weapon or shield, with values in data/*.json.
- AC-4. When a hostile blow or push lands on an armed Defend tile, the system shall resolve it through the existing block/stun rules (Defences, Stuns), including a stun chance and adrenaline.
- AC-5. Block cards (Parry, Shield Wall, Hold the Gap) shall remain and be strictly stronger versions of Defend (larger pattern, higher block, or extra effect).
- AC-6. Defend shall have a hotkey (free key, no conflict with existing bindings, also fixing the Key.Period duplicate if touched) and an (i) tooltip with help text.

### Counters (A)
- AC-7. When an Attack lands on a tile armed by Defend, the Defend shall win: block applies and the stun roll is made.
- AC-8. When a Push targets a unit that is defending, the push shall break the guard: the block on that unit is lost for the round and the push resolves.
- AC-9. When an Attack's pattern covers the destination of a Push or Dash, or lands first by priority, the attack shall hit the mover; the mover's push/dash does not cancel it.
- AC-10. When a Dash is planned against a Push (the pusher's target moves out of the pushed tile before the push resolves), the Push shall fizzle with a visible log entry. (User added: Dash beats Push.)
- AC-11. When a Recover action's owner is hit while recovering, the recover shall be cancelled and the owner shall take an extra-damage bonus (values in data); an unattacked Recover shall succeed.
- AC-12. The UI and in-game help shall show these relations (a relation card in the Defend/Action help topic and a per-action "beats / loses to" line in the tooltips).
- AC-13. Tests shall show that no single action wins against all others: for each pair in a fixed matrix the outcome is as above.

### Situation (B)
- AC-14. When planning a Push, the preview shall show if the push would collide with a wall, unit or hazard, and what it would do.
- AC-15. Where facing, flank/rear, hazards, chokepoints, stamina, priority or adrenaline change the result, the plan preview shall name the factor (for example "rear +X%", "wall collision", "low stamina: acts later").
- AC-16. Rules shall use the existing mechanics (facing bonus, Pushes collisions, Priority, Adrenaline); no hidden new mechanics.

### Enemy aim and techniques (items 2, 3; C)
- AC-17. When an enemy plans a strike, it shall aim using only information visible to it: current positions, its own sight, and telegraphed (visible) movement of the target. It shall never read the target's hidden plan or action.
- AC-18. Aim shall be deterministic per seed and vary by type: Husk swings at the target's current tile, or the adjacent tile closest to the Husk when the target is next to it; Linked squads aim at the most likely destination (data-driven heuristic).
- AC-19. Baiting shall still work: a player who moves away from a tile the enemy swings at shall dodge it.
- AC-20. When an enemy's action fizzles on an empty tile, it shall be exposed: it loses priority and stamina next round (values in data) and the UI shall flag it.
- AC-21. Enemies shall use Push, Dash and Defend according to per-type data tendencies in data/enemies.json: Husk always swings when the target steps adjacent; Linked pushes targets toward walls/hazards and repositions; Warden guards when hurt then shoves; Cole dashes to flank and defends when threatened.
- AC-22. Each type's tendencies shall be listed in help (enemy codex or hover info) so the player can read them.
- AC-23. When an enemy plans, ThreatZone and similar reads shall not use another unit's hidden plan (fix the AI-reads-earlier-AI-plan leak).

### Plan consistency (item 4)
- AC-24. When a player uses an item in a fight, hacks, or fires a drone, the command shall queue as a planned action in the Action slot and resolve in the resolve phase by priority, through patterns and block where it deals damage.
- AC-25. Out-of-combat field commands (looting, pickup, drop, stash, extract, recruit, salvage, drone deploy/recall outside a fight) may stay instant; each decision shall be recorded in progress/open-decisions.md.
- AC-26. Multiplayer protocol, fog redaction (`SideFog`), and saves shall carry the new plan kinds. Version constants shall be bumped (save PlanSlotsVersion, Net Protocol) with a migration test for older saves; config/version unchanged.

### Luck (D)
- AC-27. Combat shall use bounded seeded randomness: hit tiers (graze/hit/solid), crit chance (raised by flank/rear), and the stun roll. Replays with the same seed shall be identical; juice-determinism shall pass.
- AC-28. Before the player commits, the UI shall show the odds (percentages) for hit tiers, crit and stun.
- AC-29. A correct read shall win clearly more often than not, verified by a test or policy run, but not always.

### Measure (E)
- AC-30. tools/Remnants.Playtest shall include a "walk-and-attack-only" policy and a "mixed" policy using Defend, push, dash, bait and counters; the Server bot (Bot.cs) and PartyBot shall use Defend, push and dash.
- AC-31. On the same seeds, walk-and-attack shall take clearly more damage and survive less than the mixed policy; both reported in docs/BALANCE.md with seeds, run counts and commands. If this is not met, the PR shall say so plainly.
- AC-32. Compared with a pre-rework baseline recomputed on the same seeds, fizzled actions shall fall to where most planned actions do something (target: fizzled less than 30% of planned actions), with damage taken, deaths and loot reported before/after in docs/BALANCE.md. Tuning happens last.

### Cleanup (item 5)
- AC-33. No tooltip, help text or ARCHITECTURE section shall describe the old unit-targeted or sub-round turn model (stale strings listed in discover.md, plus docs/ARCHITECTURE.md combat section rewritten).
- AC-34. game/audio/sound_events.json shall map scavenge.clashed and scavenge.recovered to existing sounds; `--audio-selftest` passes. No new assets.

### Verification and evidence
- AC-35. `./run-tests.sh --godot` exits 0; tools/smoke-playthrough.sh and tools/juice-determinism.sh pass.
- AC-36. New Core rules shall have tests written first. Rules live in Remnants.Core; numbers in data/*.json.
- AC-37. The Defend flow and enemy behaviour shall be looked at on a hidden window via tools/godot-agent.sh with screenshots; the PR lists what was seen and separately what was not seen on screen, and what only a human can judge ("feels like chess").
- AC-38. No visible Godot window is ever opened; no progress/notes files in the repo root.

## Assumptions
- Luck uses the existing seeded RNG stream; new rolls are added without changing existing rolls' order in non-combat code.
- Dash "beats" Push by moving out of the pushed tile (movement resolves first), so no separate rule beyond the push fizzling; verify in Plan.
- 4-way cycle is Attack>Push/Dash, Defend>Attack, Push>Defend, Dash>Push, Recover loses to everything that hits it.

## Open questions (deferred to Plan; recommendations)
- Defend pattern default for unarmed units: single tile in front (recommended).
- Exact numbers (crit %, exposed penalty, recover bonus): set in tuning against AC-31.
- Whether drone deploy/fly in combat is queued (recommended: yes if in a fight, instant otherwise).
- One PR will be large; risk noted for the Merge Danger section.
