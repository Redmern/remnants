# Multiplayer scavenge (M9)

Shared scavenge instances for 2–4 players on localhost or a LAN: several players' parties on one zone map with the AI
enemies, interleaved turns, PvP or co-op (teams with shared sight, see "Co-op and alliances"), and per-side fog of war. No matchmaking, accounts or cloud. The colony stays
single-player.

## Running it

```sh
# 1. a server (one mission, then it exits once every player's run is over)
dotnet run --project src/Remnants.Server -- --port 7777
#    options: --zone ID (default suburb_ruins) --seed N --players 2 (min to start) --max-players 4
#             --turn-seconds 60 (0 = no AFK timer) --reconnect-seconds 120 --max-party N --bind 0.0.0.0 --data DIR
#             --coop (everyone on one team; default versus) --hour 0-23 (time of day; default: from the seed)

# 2. two game clients: main menu → Multiplayer → name + server address → Join → Ready
godot-mono --path game
godot-mono --path game
```

Or skip the console server: **Host** in the multiplayer menu runs the same server inside that game (port from the
address field, zone from the Zone list) and joins it; others join that machine's LAN address. The host's game keeps
the server running until every player is done, even after the host's own party is out.

Scripted smoke runs (each flag after `--`; `--mp-host[=PORT]` or `--mp-join=ADDR`, plus `--mp-name=NAME`,
`--mp-ready`, `--mp-zone=ID`, `--mp-seed=N`, `--mp-players=N`, `--mp-turn-seconds=S`, `--mp-coop` (host a co-op
mission), `--mp-team=N` (a versus lobby team); the scavenge screen's
`--autoplay=N` and `--screenshot=PATH --frames=N --quit` work as in single player, and autoplay waits for its turn):

```sh
dotnet run --project src/Remnants.Server -- --port 7790 --zone outskirts --seed 3 --turn-seconds 20 &
godot-mono --display-driver wayland --disable-vsync --path game -- --mp-join=127.0.0.1:7790 --mp-name=Bob --mp-ready --autoplay=8 &
godot-mono --display-driver wayland --disable-vsync --path game -- --mp-join=127.0.0.1:7790 --mp-name=Alice --mp-ready \
    --autoplay=8 --screenshot=/tmp/mp.png --frames=1200 --quit
```

A headless scripted player for testing a server without a second game window:
`dotnet run --project src/Remnants.Server -- --bot 127.0.0.1:7777 --name Bot [--rounds 30] [--team N] [--hostile] [--propose]`
(two armed adults with a bandage and a med-kit; it shoots what it sees, walks to the map centre, then after `--rounds`
rounds to an exit and extracts). As an ally it accepts every alliance offer (not with `--hostile`; `--propose` makes it
offer one to every rival it is in contact with, and hold fire on them while the offer is open), revives / bandages /
carries allies next to it, follows the nearest ally instead of the centre, covers them on overwatch, and leaves once the
rounds are up or its allies are gone. `--team N` picks a lobby team on a versus server.

## Architecture

| Project | What |
|---------|------|
| `src/Remnants.Core` | The rules: `ScavengeSim`, commands carrying `PlayerId` + `UnitId`, side-based hostility, per-side `Visibility`; co-op in `Alliances` (team sides) and `CoopActions` (give, carry, ping). |
| `src/Remnants.Net` | Protocol (`Protocol.cs`, `ProtocolJson`), transports (`IConnection`/`IHostSink`/`IClientTransport`; `Tcp.cs`, `LoopbackHub`), `SideFog`, `GameHost`, `HostedServer`, `NetClient`. Pure .NET, no Godot. |
| `src/Remnants.Server` | Console host (`Program`) and the `--bot` client. |
| `tests/Remnants.Net.Tests` | Protocol round-trips, scripted 2-player games over loopback, one localhost TCP game. |
| `game/scripts/Multiplayer` | `MultiplayerMenu` (Host / Join / lobby), `MultiplayerSession` (party, connection, in-process host, reconnect, result), `NetPump` (main-thread queue). |
| `game/scripts/Scavenge/ScavengeDriver` | `IScavengeDriver`: `LocalDriver` (single player) and `RemoteDriver` (a `NetClient`) behind the same `ScavengePresenter`, so the scavenge screen and views are shared, not forked. |

```
 game client                          server (Remnants.Server or Host button)
 ScavengeScreen → ScavengePresenter   GameHost ── ScavengeSim (the only one that executes)
      │ IScavengeDriver                  │  ▲          │
      ▼                                  │  │ Command  ▼
 RemoteDriver → NetClient ── TCP ────────┘  │       SideFog per seat → Snapshot (own view)
      ▲  read model: ScavengeSim over  ◀────┴── Snapshot / TurnChanged / MissionEnded
      │  the last Snapshot (queries only)
```

- **Authoritative.** Clients send intents: Core's own scavenge commands (`Move`, `Attack`, `UseItem`, `PickUp`,
  `Recruit`, `SetStance`, `Lookout`, `EndTurn`, `Extract`) wrapped in `Command(requestId, intent)`. The host rejects an intent whose
  `PlayerId` is not the sender's seat, then runs it through `ScavengeSim.Execute`, which checks ownership, turn and
  rules as in single player. The verdict comes back as `CommandAccepted` / `CommandRejected` (Core's `ScavengeError`).
- **Determinism stays on the server.** The client's `NetClient.View` is a `ScavengeSim` built over the received
  view only so the UI can ask Core questions (reachable tiles, hit chances, profiles); it is never executed. The view
  carries no RNG state. Wall-clock time exists only in the host (`TimeProvider`) to decide timeouts; a timeout becomes
  a plain `EndTurn`, so the sim's input is still just commands.
- **Threading.** `GameHost` is single-threaded: `TcpServer` runs every sink call and every `Tick` under one lock.
  On the client, TCP reads happen on a background task and are posted to `NetPump` (a node under the tree root, so it
  survives scene changes), which runs them on the Godot main thread.
- **Transport behind an interface.** `IHostSink` (server side) and `IClientTransport` (client side); `TcpServer` /
  `TcpClientTransport` for real play, `LoopbackHub` in tests. Loopback serializes every message through
  `ProtocolJson` both ways, so tests exercise the wire format, and delivers synchronously, so game scripts are exact.

## Protocol

One JSON object per frame (4-byte big-endian length + UTF-8 JSON, max 4 MB), `System.Text.Json` with Core's save
options (camelCase, string enums), compact. Records have a `type` discriminator; the command inside `Command` has a
`kind` discriminator (added by a resolver in `ProtocolJson`, so Core needs no attributes). `Protocol.Version` is
sent in `Join`; a mismatch is refused. Bump it for any incompatible record change. 2: the fog of war's `setStance` /
`lookout` commands, `UseItem.at`, and contacts, reveals, flares and sensors in views. **3: co-op** — `SetTeam`, the lobby
`mode` and each player's `team` (`LobbyState`, `PlayerInfo`, `MissionStarted.mode`), the five alliance messages, the
command kinds `proposeAlliance`, `respondAlliance`, `breakAlliance`, `ping` (its tile kind is `mark`, since `kind` is
the discriminator), `giveItem`, `carry`, `putDown`, and pings, alliance offers and breaks in views. (The co-op brief
called this "v2"; 2 was already the fog of war, so co-op is 3.) **4: hacking and drones** — the command kinds `hack`,
`deployDrone`, `flyDrone`, `recallDrone`, `markTarget`, `dropPayload`, and devices and drone marks in views. **6: cybernetics** — bodies carry the eyes and each
part's humanity, upkeep and `interfered` flag, units carry `cyberHooks` (stripped from other players' views like perk
hooks), and the `limbLocked` event.

| Direction | Message | Meaning |
|-----------|---------|---------|
| C→S | `Join(version, name, party, reconnectToken?)` | take a seat with a party (lobby), or retake a dropped seat |
| C→S | `Ready(isReady)` | lobby; the mission starts when ≥ `minPlayers` are seated and all are ready |
| C→S | `SetTeam(team?)` | lobby, versus only: start on team N with whoever picks it (allied, spawned together), null = alone; unreadies |
| C→S | `Command(requestId, intent)` | a scavenge command |
| C→S | `Leave` | leave for good (no reconnect) |
| S→C | `Welcome(version, playerId, reconnectToken)` / `JoinRefused(reason)` | seat (player ids 1, 2, …) or why not |
| S→C | `LobbyState(zone, min, max, players, mode)` | after every lobby change; each player with their starting `team` (co-op: all 1) |
| S→C | `MissionStarted(zone, map, players, turnSeconds, reconnectSeconds, mode)` | the terrain, once; players with their starting team |
| S→C | `Snapshot(view, visible, events)` | the receiver's fog-filtered state + new events it may hear about |
| S→C | `CommandAccepted(requestId, message)` / `CommandRejected(requestId, error, message)` | sent after the snapshot carrying the effects |
| S→C | `TurnChanged(round, unitId?, playerId?, secondsLeft)` | whose turn (unit only if the receiver sees it) |
| S→C | `PlayerLeft(playerId, name, reconnectSeconds)` / `Notice(text)` | drops (0 s = gone for good), timeouts, rejoins |
| S→C | `MissionEnded(result, reason)` | the receiver's `ExtractionResult` |
| S→C | `AllianceProposed(from, to, untilRound)` | to the two players only |
| S→C | `AllianceAccepted(team)` | to everyone on the merged team |
| S→C | `AllianceDeclined(from, to, lapsed)` | to the two players only (declined, or the offer ran out) |
| S→C | `AllianceBreakWarning(playerId, effectiveRound)` | to the whole team |
| S→C | `AllianceBroken(playerId, formerAllies)` | to the leaver and the team they left |

### Full snapshots, not deltas

After every accepted command (and every server-side turn change) each seat gets a full snapshot of **its own view**.
Measured in `Snapshot_size_per_action_stays_small` (Suburb Ruins, two parties of two, five rounds): the map in
`MissionStarted` is ~6.5 KB once; a snapshot averages ~14 KB (max ~15.6 KB) per player per action, against ~67 KB
for a full indented save of the whole instance. On a LAN that is nothing, and full views keep the hard parts simple:
fog filtering is one function of (state, side), a reconnecting client needs no catch-up, and a lost or reordered
update cannot desync anything. Deltas or compression (the JSON is very repetitive) are the next step if this ever
goes over the internet; the protocol version covers that change.

## Turns

- **Interleaved initiative** across every unit of every side, exactly as Core already does: each round orders all
  conscious units (both parties, the AI, recruits) by initiative; AI turns run inside the server's `Execute` and
  are shown to each client as the events it may see.
- **Turn timer** (`--turn-seconds`, default 60, 0 = off): when a player's unit has been up that long the host ends
  its turn (`Notice: "X's turn timed out."`). Each unit's turn gets a fresh timer; `TurnChanged.secondsLeft` lets
  the client count down (the turn bar shows `Alice's turn · 42s`).
- **Disconnect** = the party holds position: whenever a dropped seat's unit is up, the host ends its turn at once,
  but only while some connected player is still playing — with nobody left to play against, the instance holds still
  rather than letting the AI run against absent parties.
- **Reconnect** within `--reconnect-seconds` (default 120): the client keeps the `Welcome` token and, when the link
  drops mid-mission, retries every 3 s (`MultiplayerSession`); `NetClient.Reconnect` sends `Join` with the token
  over the new link and gets `Welcome`, `MissionStarted`, a fresh `Snapshot` and the turn. After the window the seat
  is gone for good (`PlayerLeft` with 0 s); its party keeps waiting until it dies or the mission ends.
- **End.** A party is done as in single player (nobody conscious and controllable left on the map); its player gets
  `MissionEnded` right away while the others play on. The host ends once every seat is done or gone. In the lobby a
  drop simply frees the seat.

## PvP and fog

- Each party is its own side (`player:N`), so Core's side-based rules already make other players' units hostile:
  they can be attacked, they block tiles and they shoot back. Items cannot target hostiles, as in single player.
  Other players' units get their own colour (`SidePalette.OtherPlayer`, violet) on rings, labels and turn chips.
- `SideFog` decides what a seat may receive:
  - units of its own side (any status); everyone else only while the side **sees them now** (Core's
    `Detection.SideSees`: in sight and detected past their stealth, lit by a flare, revealed, or scanned — a stealthy
    unit on a visible tile is not sent), with AI memory stripped and the colony record cut to name, sex and life stage
    (the model);
  - its own contacts and motion sensors, reveals only of units it is sent, and every burning flare (they light up for
    everyone);
  - loot caches **as last seen** — a cache emptied out of sight still shows its old contents until seen again;
  - the turn order cut to known units, with `SideFog.HiddenUnitId` (-1) when an unseen unit is up;
  - its own explored tiles only, every party's done flag, no RNG state, no Network alarm counters;
  - events through Core's `FogLog`, like the single-player log: round/party bookkeeping, anything done by or to its own
    units, anything on a tile it saw before or after; but an event about a target it never saw is dropped, one whose actor it
    did not see when the event happened (`SeenBy`, stamped by the sim, never sent) comes only in its anonymous form with the actor removed ("Bob1 was ambushed from the fog (+20% hit)"), or not at
    all, and side-private events (a motion sensor's alert) only to their side.
- The terrain map is sent whole in `MissionStarted` (it is static and generated from the seed; single player keeps it
  in memory too). Fog hides units, caches and events, not walls.
- Tests check every snapshot and turn message against the server's live visibility at the moment it is sent
  (`NetFixture.CheckFog`: unseen units, other sides' contacts / sensors / reveals, anonymous texts or private events
  leaking, events naming an actor or target the player was never sent), across scripted multi-round games. `FogNetTests` stage a
  cloaked sniper: the victim's client gets the hit, the "ambushed from the fog" line and an attack contact pointing back,
  never the sniper's id, name or position, unless the muzzle flash puts it in their sight.
- The old soft spot (an event about one of your units naming an actor you could not see) is closed by `FogLog`.

## Co-op and alliances

Players can scavenge together. The rules are all in Core (`Alliances`, `CoopActions`; tuned in `scavenge.json` `coop`),
so single player, the server and the bots share them.

- **Teams are sides.** Allied parties share one side (`team:N`; a lone party is `player:N`). Every side-based rule then
  treats them as one: no friendly fire (an attack on an ally's unit is rejected with `FriendlyFire`), one fog of war
  (what any ally sees, every contact, reveal, motion sensor and explored tile is the team's), overwatch never fires on
  allies, and an ally next to you watches your back against ambushes. Ownership (`PlayerId`), turns, the turn timer,
  loot and results stay per player. Non-allied players stay hostile exactly as before.
- **Two ways to team up.** A **co-op mission** (`--coop`, or Mode → Co-op when hosting) puts every seat on team 1; a
  versus lobby can also group players with `SetTeam`. Parties on one team spawn next to each other. **In the field**,
  a player whose party and another's see each other (`Alliances.InContact`: each side sees a unit of the other) offers
  an alliance (`ProposeAlliance`, free, on your turn); the other answers on their own turn (`RespondAlliance`, accept or
  decline); offers lapse after `proposalRounds` (2). On accept the two teams merge into a new team side and share sight
  from that moment.
- **Breaking** (`BreakAlliance`) is announced to the team and takes effect when round *announced + 1 +
  `breakWarningRounds`* starts, so everyone gets a full round's warning (banner and log on both sides) — no instant
  backstab. Then the leaver is on their own side again (a team left with one player dissolves too); each side keeps a
  copy of what the team knew (contacts, reveals, explored tiles), motion sensors and pings go with whoever placed them,
  and ex-allies put down each other's carried downed. From that round on vision is separate and fire is open.
- **Co-op actions** (also between your own units): heal or revive an adjacent ally with a bandage / med-kit (plain
  `UseItem`: items work on anyone not hostile); `GiveItem` hands a carried item to an adjacent survivor on your side
  (`giveApCost` 1) — it goes into the other player's party and home to their colony; `Carry` lifts an adjacent downed
  unit (`carryApCost` 1): it rides on your tile (not blocking it), you lose `carryRangePenalty` (1) movement and any
  special moves, and when you extract they leave with you and go home to their own player; `PutDown` (free) sets them on
  a free tile next to you, and a carrier who goes down drops them. `PingTile` marks a tile for your team only (danger /
  go here / loot; free, `pingsPerTurn` 2, fading after `pingRounds` 2).
- **Downed with an ally up.** A party whose people are all downed is not done while an ally is still conscious: the ally
  may revive or carry them out (they keep bleeding unless stabilized). When the last ally leaves, they are left behind.
- **Loot, recruits, extraction stay per player.** Each player's `ExtractionResult` holds their own survivors, recruits,
  dead and the loot those carried, and goes to their own colony. Loot caches are first come, first served (no auto-
  split: whoever picks an item up owns it; hand it over with Give if you want to share). A recruit joins whoever escorts
  it out: extracting takes every follower on your side within the escort radius, an ally's too, and they become yours;
  a recruit whose player's run ended follows the nearest ally. If one player extracts the others play on; the mission
  ends for everyone when all players have extracted, died or left.
- **Turns.** The interleaved initiative order across every unit of every side is unchanged; allied units are not
  grouped together (it would only matter on initiative ties, which are rare with Agility-based initiative and broken by
  unit id; left out to keep one turn order for everyone). The turn timer applies per player as before.
- **Difficulty** scales with the number of players in the mission, allied or not: enemies × (1 +
  `enemiesPerExtraPlayer` × (players − 1)), and per-type caps (e.g. at most one Linked) scale the same, rounded up
  (`NewScavenge.EnemyScale`; default ×2 for two players). See BALANCE.md "Co-op pass".
- **Fog over the wire.** `SideFog` follows the party's *current* side at every update, so allies get the team's view
  (their ally's units with the colony record cut to name, sex and life stage, like anyone else's; the whole team's
  contacts, sensors and explored tiles; the team's pings) and the moment an alliance breaks each gets only their own
  again. Alliance offers (in views and `AllianceProposed`) go only to the two players, breaks only to the team, alliance
  log lines are addressed (`ScavengeEvent.Sides`) to the two teams involved and carry no unit or position (an offer must
  not give away a hidden scout), and a third player hears none of it: other teams' parties and units show up in a view on
  their owner's own side (`player:N`), so who is allied with whom stays private. A seat gone for good is
  `ScavengeSim.Abandon`ed: its party holds position and allies all downed waiting on it are left behind. `NetFixture.CheckFog`
  judges every message against the receiver's live side.

### Real parties for online play

From the colony: **Expedition → Launch online / co-op** (or **Online / Co-op** next to Play mission for a party already in
its zone) launches the expedition exactly like a local one (adults only, the Town Hall party cap, each member's gear
out of the colony inventory, rations from the stockpile; Core's `LaunchExpedition`), saves, and opens the multiplayer
menu, which brings those survivors and their gear (`MultiplayerSession.PickParty`). On return the `ExtractionResult` is
applied to the colony as the expedition's result and saved — the same trust gap as below. Without such an expedition
the menu still brings the debug party (main-menu Skirmish, tests, quick LAN games).

### Running a co-op session locally

```sh
# a co-op server and a headless ally
dotnet run --project src/Remnants.Server -- --port 7791 --zone outskirts --coop --players 2 &
dotnet run --project src/Remnants.Server -- --bot 127.0.0.1:7791 --name Rook &
# you: main menu → Multiplayer → Join → Ready (or scripted:)
godot-mono --display-driver wayland --disable-vsync --path game -- --mp-join=127.0.0.1:7791 --mp-name=Alice --mp-ready

# two humans: one hosts in-game with Mode → Co-op (or --mp-host --mp-coop), the other joins
godot-mono --path game -- --mp-host=7791 --mp-coop --mp-name=Alice
godot-mono --path game -- --mp-join=127.0.0.1:7791 --mp-name=Bob --mp-ready

# an alliance in the field: a versus server where the bot offers one (Outskirts spawns parties in sight of each other)
dotnet run --project src/Remnants.Server -- --port 7793 --zone outskirts --players 2 --turn-seconds 20 &
dotnet run --project src/Remnants.Server -- --bot 127.0.0.1:7793 --name Rook --propose &
godot-mono --path game -- --mp-join=127.0.0.1:7793 --mp-name=Alice --mp-ready

# 2 allies against a third player: a versus lobby where two pick team 1
dotnet run --project src/Remnants.Server -- --port 7795 --zone outskirts --players 3 &
dotnet run --project src/Remnants.Server -- --bot 127.0.0.1:7795 --name Ally --team 1 &
dotnet run --project src/Remnants.Server -- --bot 127.0.0.1:7795 --name Rival --hostile &
godot-mono --path game -- --mp-join=127.0.0.1:7795 --mp-name=Alice --mp-team=1 --mp-ready
```

In the scavenge screen allies are soft green (rings, name tags, turn-bar portraits; `Palette.Ally` — cyan stays the
Network's, violet the hostile players'). Selecting an ally's unit shows a read-only card with an **Ally** section. The
**Team** panel has Give (G), Carry / Put down (C), pings (Z danger, V go here, N loot, then click a tile; pins on the
map), Accept (Y) / Decline (U) an offer, Offer an alliance (Y) to a rival you see, and Leave the alliance; the
**alliance banner** under the turn bar shows an open offer, a coming break with its round, and alliance news.

## Hacking and drones over the wire

- **Devices** reach a client like caches: only once its side has seen the tile (or explored it through a hacked
  terminal's map), as last seen, current while in sight or while the side controls the device. Never their contents
  (locker / terminal items), never another side's hack in progress or lockout, and control by another team shows as the
  owner's own side (as with parties). A camera's or turret's unit is a unit like any other: sent only while seen, and a
  device record names it only then.
- **Hacked cameras and turrets** are on the hacker's side, so they see and fight for the whole team (team fog needs
  nothing extra); a rival learns of the change only by seeing the device again.
- **Drones** are units of their operator's side: allies get them and their sight; rivals only while they see the drone,
  and then without the operator's id when the operator is unseen (and an operator's unseen drone id is cut). Other
  players' perks (`Unit.Hooks`), field XP and packed drone battery are stripped from views.
- **Perks** travel with the party: the survivors in `Join` carry their perk ids, and the server resolves the hooks from its
  own data (perks.json next to the other data files). The usual trust gap applies (a modified client can claim perks).
- **Cybernetics** travel the same way: the bodies in `Join` name their fitted parts, and the server resolves their hooks
  and sets from its own cybernetics.json. Network interference is rolled on the server like everything else; a rival
  sees a locked limb only in the unit's body when it sees the unit.
- `NetFixture.CheckFog` checks all of it on every message; `HackNetTests` stage the cases.

## Night over the wire

- **The server decides the hour** (`HostOptions.Hour`, `--hour`; without it `seed % 24`) and makes the instance with it
  (`ZoneConditions.Environment`, phase by the default colony hours). Joining clients never choose it. A player who hosts
  from the colony's waiting party passes the hour that party arrives at (and maybe waited and paid rations for) as
  `Hour`; a party that joins someone else's server plays at that server's hour.
- **Every view carries the environment** (hour, phase, weather slot): it is public, the same for everyone, so every
  client lights the map alike. Protocol version 5.
- **Fog follows the dark on the server.** `SideFog` asks the same `Detection` / `Visibility` that the night changes
  (shorter human sight, stealth bonus except against the Linked and cameras), so a party seen by day is not sent at night,
  and flares (which every side sees) show the same lit ground to all. `NightNetTests` stage it with `CheckFog` on every
  message: server-decided hour in both views, a day/night standoff (sent by day, not at night, nothing leaked), a flare.

## Vehicles over the wire

- **Each player brings their own vehicle**: `Join` carries the party's `FieldVehicle` (from the colony's chosen vehicle). The
  server parks it at that player's spawn point and refuses a party larger than its seats or a vehicle that does not add up
  (HP, cargo, turret over `scavenge.json vehicle` limits). Cargo is enforced by the sim: `stashItem` fails past the limit.
- Commands `stashItem`, `takeFromStash`, `salvage`. Views carry the parked vehicles: the side's own in full, another
  party's only once its tile is explored, never with its stash; stripped wrecks are public. Protocol version 6.
- `VehicleNetTests`: two players with their own vehicles (stash hidden from the other), seat and stat refusals, stash over
  the wire.

## Parties and results — the trust gap

- **Party in.** `MultiplayerSession.PickParty` takes the party of the colony's expedition when one is in its zone and
  has not started a local run (the colony in memory, else `user://colony.json`): Expedition → Launch online / co-op, or
  Online / Co-op for a party already in its zone (see "Real parties for online play"). The members' current survivors and their gear go in `Join`. Without such
  an expedition the menu brings a debug party (seeded by the player name). The server's zone is used, not the
  expedition's.
- **Result out.** `MissionEnded` carries the player's own `ExtractionResult` (only their survivors, recruits, dead and
  loot). The client applies it to its colony as the expedition's result (`ColonySession.ApplyMultiplayerResult`
  remaps the server's player id to `ColonySim.ExpeditionPlayerId`) and saves. A result that is not final (the player
  left early, or the connection was lost for good) leaves the party out in the colony, as Leave does in single player.
- **What the server trusts.** It checks the party size (`--max-party`, default the largest Town Hall party), that nobody
  in it is a child (a join with one is refused), and that every item exists and sits in its slot. It does **not** know the colony: a modified client can join with invented
  survivors and gear, and can apply any result to its own save (or play the same expedition again locally after
  leaving). Fine between friends on a LAN; not for strangers.
- **What a server-saved colony would need:** colonies stored server-side (accounts, the colony save per account);
  `LaunchExpedition` executed on the server, so the party, its gear and rations come out of the stored colony and the
  instance is joined from that expedition; `ApplyExtractionResult` executed by the server on the stored colony when the
  party is done (the client only displays the report); the client's `ColonySim` becoming a read model like
  `NetClient.View`; and expedition state (`Expedition.Scavenge`) pointing at a server instance instead of a local run.

## Tests

`tests/Remnants.Net.Tests` (run by `./run-tests.sh`):

- `ProtocolTests`: every message and command kind round-trips; garbage is rejected.
- `FogNetTests`: a hidden attacker stays anonymous and unplaced for the victim; a revealed one shows up; sensors,
  contacts and stances stay with their side over the wire.
- `MultiplayerTests` (loopback, manual clock): lobby and start; refused joins (version, bad gear, party size, running
  mission); turn ownership and spoofed player ids; interleaving across both parties and the AI; PvP attack seen by both;
  fog filtering over several rounds and remembered caches; disconnect → auto-wait → reconnect with the token, also over
  a new link with the same `NetClient`; the reconnect window; nobody connected → the instance holds; AFK timeout;
  per-player extraction results; ending with a player gone; snapshot size.
- `TcpTests`: two clients join, start and play over localhost TCP.
- `NightNetTests`: the server's hour in every view; a party seen at noon is not sent at night; a flare lights the same
  ground for both sides.
- `CoopNetTests` (loopback): a co-op lobby (everyone team 1, shared sight, the ally's colony record cut); two allies play a
  whole mission (ping, give an item and friendly fire over the wire) and each gets their own result; two allies against a
  third player (teams picked in a versus lobby) play to the end with nothing of the team reaching the rival; an alliance
  formed in the field (offer and answer reach only the two, merged sight) and broken (warning to the team, a full
  round, then separate sight); a declined offer told to both and nobody else. `ProtocolTests` round-trip every new
  message, command kind and a co-op view.
