# Multiplayer scavenge (M9)

Shared scavenge instances for 2–4 players on localhost or a LAN: several players' parties on one zone map with the AI
enemies, interleaved turns, PvP, and per-side fog of war. No matchmaking, accounts or cloud. The colony stays
single-player.

## Running it

```sh
# 1. a server (one mission, then it exits once every player's run is over)
dotnet run --project src/Remnants.Server -- --port 7777
#    options: --zone ID (default suburb_ruins) --seed N --players 2 (min to start) --max-players 4
#             --turn-seconds 60 (0 = no AFK timer) --reconnect-seconds 120 --max-party N --bind 0.0.0.0 --data DIR

# 2. two game clients: main menu → Multiplayer → name + server address → Join → Ready
godot-mono --path game
godot-mono --path game
```

Or skip the console server: **Host** in the multiplayer menu runs the same server inside that game (port from the
address field, zone from the Zone list) and joins it; others join that machine's LAN address. The host's game keeps
the server running until every player is done, even after the host's own party is out.

Scripted smoke runs (each flag after `--`; `--mp-host[=PORT]` or `--mp-join=ADDR`, plus `--mp-name=NAME`,
`--mp-ready`, `--mp-zone=ID`, `--mp-seed=N`, `--mp-players=N`, `--mp-turn-seconds=S`; the scavenge screen's
`--autoplay=N` and `--screenshot=PATH --frames=N --quit` work as in single player, and autoplay waits for its turn):

```sh
dotnet run --project src/Remnants.Server -- --port 7790 --zone outskirts --seed 3 --turn-seconds 20 &
godot-mono --display-driver wayland --disable-vsync --path game -- --mp-join=127.0.0.1:7790 --mp-name=Bob --mp-ready --autoplay=8 &
godot-mono --display-driver wayland --disable-vsync --path game -- --mp-join=127.0.0.1:7790 --mp-name=Alice --mp-ready \
    --autoplay=8 --screenshot=/tmp/mp.png --frames=1200 --quit
```

A headless scripted player for testing a server without a second game window:
`dotnet run --project src/Remnants.Server -- --bot 127.0.0.1:7777 --name Bot [--rounds 30]` (two armed adults; it
shoots what it sees, walks to the map centre, then after `--rounds` rounds to an exit and extracts).

## Architecture

| Project | What |
|---------|------|
| `src/Remnants.Core` | The rules, unchanged by M9: `ScavengeSim`, commands carrying `PlayerId` + `UnitId`, side-based hostility, per-side `Visibility`. |
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
`kind` discriminator (added by a resolver in `ProtocolJson`, so Core needs no attributes). `Protocol.Version` (2: the
fog of war's `setStance` / `lookout` commands, `UseItem.at`, and contacts, reveals, flares and sensors in views) is
sent in `Join`; a mismatch is refused. Bump it for any incompatible record change.

| Direction | Message | Meaning |
|-----------|---------|---------|
| C→S | `Join(version, name, party, reconnectToken?)` | take a seat with a party (lobby), or retake a dropped seat |
| C→S | `Ready(isReady)` | lobby; the mission starts when ≥ `minPlayers` are seated and all are ready |
| C→S | `Command(requestId, intent)` | a scavenge command |
| C→S | `Leave` | leave for good (no reconnect) |
| S→C | `Welcome(version, playerId, reconnectToken)` / `JoinRefused(reason)` | seat (player ids 1, 2, …) or why not |
| S→C | `LobbyState(zone, min, max, players)` | after every lobby change |
| S→C | `MissionStarted(zone, map, players, turnSeconds, reconnectSeconds)` | the terrain, once |
| S→C | `Snapshot(view, visible, events)` | the receiver's fog-filtered state + new events it may hear about |
| S→C | `CommandAccepted(requestId, message)` / `CommandRejected(requestId, error, message)` | sent after the snapshot carrying the effects |
| S→C | `TurnChanged(round, unitId?, playerId?, secondsLeft)` | whose turn (unit only if the receiver sees it) |
| S→C | `PlayerLeft(playerId, name, reconnectSeconds)` / `Notice(text)` | drops (0 s = gone for good), timeouts, rejoins |
| S→C | `MissionEnded(result, reason)` | the receiver's `ExtractionResult` |

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
    units, anything on a tile it saw before or after; but an event whose actor it did not see (at the last snapshot or
    now) comes only in its anonymous form with the actor removed ("Bob1 was ambushed from the fog (+20% hit)"), or not at
    all, and side-private events (a motion sensor's alert) only to their side.
- The terrain map is sent whole in `MissionStarted` (it is static and generated from the seed; single player keeps it
  in memory too). Fog hides units, caches and events, not walls.
- Tests check every snapshot and turn message against the server's live visibility at the moment it is sent
  (`NetFixture.CheckFog`: unseen units, other sides' contacts / sensors / reveals, anonymous texts or private events
  leaking, events naming a unit the player was never sent), across scripted multi-round games. `FogNetTests` stage a
  cloaked sniper: the victim's client gets the hit, the "ambushed from the fog" line and an attack contact pointing back,
  never the sniper's id, name or position, unless the muzzle flash puts it in their sight.
- The old soft spot (an event about one of your units naming an actor you could not see) is closed by `FogLog`.

## Parties and results — the trust gap

- **Party in.** `MultiplayerSession.PickParty` takes the party of the colony's expedition when one is in its zone and
  has not started a local run (the colony in memory, else `user://colony.json`): launch an expedition as usual, then
  open Multiplayer instead of Play mission. The members' current survivors and their gear go in `Join`. Without such
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
