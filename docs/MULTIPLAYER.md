# Online multiplayer

Two to four players play one match over a direct network connection. One player hosts, the others join by IP address and port. There is no dedicated server, matchmaking or account system.

The networking approach (ENet host/join, lobby, RPC node) was taken from the Wargame-Godot project (`src/Game/Network/` there). The sync model is different: Wargame sends full state snapshots, this game relays inputs (see below).

Everything online lives in `game/` (presentation layer). `Artillery.Core` has no network code.

## How it works: lockstep input relay

The simulation in `Artillery.Core` is deterministic: given the same `MatchSettings` (including `Seed`) and the same sequence of player inputs, every machine computes exactly the same match. So each peer runs its **own** `MatchEngine`, and only the player decisions travel over the network:

| Message | Sent by | Content |
|---|---|---|
| Turn action (`A`) | the owner of the seat whose turn it is | a shot (`ShotInput`) or a self-destruct |
| Shop cart (`S`) | the owner of a seat, when they leave the shop | cash and the three inventory arrays of their tank |

Everything else (terrain generation, turn order, AI turns, damage, wind, scores) is recomputed locally on every peer from the shared seed.

The host is a hub, not an authority over state: a guest sends its message to the host, the host checks that the sender owns that seat and forwards it to the other guests. Each peer keeps a queue per `(kind, seat)` and the battle loop waits on it when it is a remote player's turn.

### The one rule that keeps peers in sync

Human-seat code must never consume the shared RNG (`MatchState.Rng`) on one machine only. `AiPlayer.ChooseShot` and `Shop.AiShop` both use it, so they may only run for computer seats (which every peer runs identically). If this is broken, the peers silently drift apart.

A cheap desync check guards against it: every shot message carries a hash of the state (round, turn, current player, wind, and every tank's position, hull, shield, cash; FNV-1a, because `System.HashCode` is randomised per process). The receiver compares it with its own state before firing and shows a warning toast ("game state out of sync") plus a Godot warning if they differ.

## Files

| File | Role |
|---|---|
| `game/scripts/Net/NetSession.cs` | The network node: ENet host/join, lobby state, RPCs, message queues, wire formats, state hash |
| `game/scripts/Ui/MultiplayerScreen.cs` | Connect form and lobby UI; also `NetPrefs` (name/address/port in `user://lobby.cfg`) |
| `game/scripts/Ui/SetupScreen.cs` | Reused by the host for match settings (`UseForLobby()`) |
| `game/scripts/Ui/TitleScreen.cs` | "Multiplayer" button |
| `game/scripts/Main.cs` | Flow: lobby, shop sync, battle, abort handling |
| `game/scripts/Battle/BattleScene.cs` | Remote turns (`RemoteTurn`), sending local turns |
| `game/scripts/Ui/PauseMenu.cs` | `Online` mode |

`NetSession` is a child of `Main` named `Net`, so it sits at `/root/Main/Net` on every peer. Godot RPCs need the node path to match on both sides, so do not rename or move it.

## Lobby and seats

1. **Host** opens the Multiplayer screen, enters a name and a port (default 7000), and presses *Open Lobby*. The lobby shows the LAN address with a Copy button. For internet play the host forwards that UDP port.
2. **Guests** enter the host's address and port and press *Connect*. A connect attempt times out after 10 s with an error message.
3. The host presses *Match Settings*, which opens the normal setup screen. Its start button reads "Save & Back to Lobby". The settings are published to all guests and shown in the lobby.
4. **Seats:** the k-th `Human` seat in the settings belongs to the k-th lobby member (host first, then guests in join order). All other seats are computers. Human seats are named after the lobby members.
5. *Start Match* is enabled when the number of human seats equals the number of connected players. The lobby says why it cannot start otherwise (settings missing, too many or too few human seats).

The lobby holds at most 4 members. Connections are refused once a match is running.

## Match flow online

- **Shopping:** each player shops on their own machine at the same time. Seats owned by other peers show "Waiting for X to finish shopping...". When a player presses Done, their cart is sent and applied to their tank on every peer.
- **Battle:** on your turn you aim and fire as usual and the shot is sent. On someone else's turn the HUD shows "Waiting for X...", then their barrel swings to the chosen aim and the shot plays. Computer turns are calculated locally on every peer.
- **Round and final results:** each peer clicks through at its own pace. A peer that is ahead only buffers messages.
- **Pause menu:** Esc does not pause anything online. *End Round* and *End Game* are hidden (they would act at different points on different peers). *Self-Destruct* works on your own turn and is relayed like a shot. *Quit* becomes "Leave match".

## Disconnects

- In the lobby: a guest leaving frees their seat and the lobby updates. If the host closes the lobby, guests return to the connect form with "The host closed the lobby."
- During a match: if anyone drops, the match cannot continue. Every remaining peer returns to the title screen with a message such as "X left the match." There is no reconnect or replacement by a computer.
- After the last round, the final results screen no longer treats a disconnect as an abort.

## Wire format

Messages are plain strings sent over reliable RPCs (`NetSession`):

- Lobby: `Hello(name)` guest to host; `ReceiveLobby(members, settings)` host to guests; `ReceiveStart(settings, seats)` host to guests.
- Match: `Submit(kind, seat, payload)` guest to host, `Deliver(kind, seat, payload)` host to guests.
- Settings: `rounds|shots|cash|order|air|seed|landscapes|players|wind|windEvery`, player names URL-escaped.
- Shot: `S|weapon|casing|power|bearing|elevation|targetX:targetY|stateHash`. Doubles are written with the round-trip format `R` and parsed with the invariant culture, so aim values arrive bit-exact.
- Self-destruct: `D`. Shop cart: `cash|weapons|utilities|casings`, each array comma-separated.

A malformed message from a peer is ignored or skips that turn; the host only forwards messages from the peer that owns the seat.

## Testing

- Unit tests cover the core only. The network layer has no xUnit tests because it needs the Godot runtime.
- **Headless loopback test** (needs the Godot mono console executable): start two instances, the host first.

  ```
  Godot_..._console.exe --headless --path game -- --mphost
  Godot_..._console.exe --headless --path game -- --mpjoin=127.0.0.1
  ```

  Both play a 2-round match with two "human" seats and one computer. The humans are played by a scripted stand-in (`BattleScene.TestShot`) that aims at the nearest enemy without touching the shared RNG. Compare the `Round N finished: ... scores=... cash=...` lines of both logs; they must match, and no "desync" warning may appear.
- **Screenshots:** `--mpshot=ABS.png` renders one screen to an image and quits. Add `--mpscreen=setup` or `--mpscreen=settings` for those screens (default is the multiplayer screen).

## Known limits

- Direct IP only: no LAN discovery, relay or NAT traversal. Internet play needs a forwarded UDP port.
- No reconnect and no spectators.
- Peers must run the same game version. There is no version handshake, so mismatched builds can desync.
- Players trust each other: the host validates seat ownership but not the content of a shot beyond the engine's own clamping.
