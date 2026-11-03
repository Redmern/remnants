# Tile-attack-only combat: progress notes

## Done
- `Attack` command, its handler and wire name `attack` removed; `attackTile` stays. Protocol bumped to 19
  (`Protocol.TileAttacksOnlyVersion`): an older peer would still send `attack`.
- `TileStrikes.StrikeAt(sim, unit, action, target)`: the one place that turns "hit this hostile with this action" into an
  `AttackTile` (stab / slash via `BestAim`, any other action at the target's tile; null when it cannot reach). Used by
  `ScavengeAi`, `Bot.cs`, `StoryBot`, `PartyBot`, the test bot, `ScavengeScreen` (click on hostile, auto-play) and demos.
- `ScavengeAi.Strike`: every strike at once is a declared tile strike (no `Combat.Resolve` fallback).
- `PlannedActions.Resolve`: non-patterned plan attacks go through `TileStrikes.Aim` / `TileStrikes.Strike` like
  stab / slash ones.
- Tests converted: `StrikeNow` (AttackTile through `Execute`, then end player sub-turns until it lands) for flow tests;
  `Blow` (Declare + `ResolveAll` on the spot, turn untouched) for tests about what a hit does (bestiary counts, bane,
  suppressor noise, scope AP, miss text), where the turn moving on (AI turns, AP refill) would change what is measured.

## Interpretations / deviations (AC-5)
- **Unknown action, reach, AP**: enforced by `TileStrikes.Declare` (unchanged).
- **Visibility / fog (`NotVisible`)**: consciously dropped. Strikes name a tile, not a unit id, so there is nothing to
  probe; a strike into the fog is readied blind and answers exactly like one at an empty tile (new FogTests case).
  `ScavengeError.NotVisible` is still used elsewhere.
- **Friendly fire (`FriendlyFire`)**: enforced by construction: a strike only ever comes down on hostiles; a strike at
  an ally's tile succeeds (AP paid, noise made) and fizzles. Tests in ScavengeCommandTests, CoopTests, CoopNetTests now
  check the ally is untouched. The enum value `ScavengeError.FriendlyFire` is now unused by Core but kept (error names
  are on the wire / in `ScavengeText`); remove it if you prefer.
- **"Not a hostile target" (`InvalidTarget`)** and **melee vs drone (`OutOfRange`)**: same as friendly fire: the strike
  is allowed and hits nothing (DroneTests updated).
- **Plans keep their timing**: a plan's attack still comes down when the plan resolves (start of the unit's next turn),
  via `TileStrikes.Strike`, as patterned plans already did; it is not deferred to the sub-round's end.
- **Suppressing fire** (AI and plans) stays `Combat.Suppress`: no damage roll, and `AttackTile` has no suppress mode.
- **Kept instant** as instructed: overwatch (`ScavengeSim`), card reactions (`CardPlay`), `DroneFire`. No change proposed.
- **Demos** (`--anim-demo`, `--loot-demo`): each blow is now an `AttackTile` followed by ending the party's sub-turns
  until it lands (`StrikeAndLand`). Built, not run visually.

## Verification
- `./run-tests.sh`: Core 1206 passed, Net 103 passed; solution builds with no warnings.
- `dotnet run --project tools/Remnants.Playtest -- --zone=outskirts --runs=10`: bots play through (no failing commands).
- `dotnet format --verify-no-changes` reports whitespace issues repo-wide, pre-existing (none on lines changed here).
