# Architecture

```
Unity 6 client (Android)
  ├─ REST  (UnityWebRequest + JWT)  ── auth, profile, coins, shop, social, history
  └─ WSS   (ClientWebSocket + JWT)  ── rooms, matchmaking, authoritative game state
                 │
                 ▼
ASP.NET Core 8 authoritative server
  ├─ LudoLegends.Core       shared rules engine (netstandard2.1, no dependencies)
  ├─ RoomManager / GameRoom one lock per room, engine-validated commands
  ├─ CoinService            append-only ledger, idempotent credits/debits
  ├─ MatchService           settlement, stats, missions, achievements, recovery
  ├─ AuthService            guest + Google accounts, self-issued HS256 sessions
  └─ EF Core                SQLite (dev/tests) or PostgreSQL (production)
                 │
                 ├─ Photon Voice (optional, audio never touches this server)
                 └─ FCM push (optional, server-side service account only)
```

## Why one shared rules assembly

The engine is plain C# with zero NuGet dependencies and targets `netstandard2.1`, which both
.NET 8 and Unity 6 consume. Server and client compile the *same files*
(`tools/sync-core.sh` copies them into `Assets/Scripts/Shared`). Consequences:

* offline AI and pass-and-play follow exactly the same rules as online play;
* the client can compute the board layout from the same topology the server validates;
* there is one place to fix a rules bug.

The client never *decides* anything: it sends `roll` or `moveToken`, and renders the
`GameSnapshot` that comes back.

## Wire protocol

One flat `ClientEnvelope` and one `ServerEnvelope` (`Core/Protocol.cs`), JSON, camelCase,
over WebSocket.

Client intents: `hello`, `findMatch`, `cancelMatch`, `createRoom`, `joinRoom`, `leaveRoom`,
`startGame`, `addBot`, `setReady`, `roll`, `moveToken`, `resync`, `ping`, `emote`.

Server messages: `welcome` (includes the authoritative wallet balance), `pong`, `queued`,
`matchFound`, `matchCancelled`, `roomState`, `gameState`, `commandRejected`, `error`,
`playerConnection`, `rematch`, `emote`.

Rejections carry a stable machine code in `errorCode` (`not_your_turn`, `wrong_phase`,
`illegal_move`, `match_finished`, `invalid_payload`, `not_in_room`, ...) and a
human-readable sentence in `error`; clients switch on the code, never on the sentence.
Table stakes are a closed set (0, 100, 250 coins) validated server-side, matching the fee
buttons in the client.

Every accepted command produces a **full authoritative snapshot** plus the events that led
to it. A Ludo snapshot is ~1-2 KB, so this is cheaper than tracking deltas and makes
reconnection a one-message operation. `gameState` includes `turnDeadlineMs` so the HUD
countdown is authoritative too.

## Room lifecycle

```
Lobby ──(seats full / host start / auto-start)──► Playing ──(all but one finished)──► Finished
```

* **Matchmaking**: open tables keyed by seat count and entry fee; a full table auto-starts.
* **Private rooms**: six-character code from an unambiguous alphabet (no O/0, I/1); host
  starts, can add bots to fill seats.
* **Turn clock**: 25 s default. On expiry the server rolls (and moves via the AI) for the
  active seat, so a four-player game cannot stall.
* **Disconnect**: the seat is kept for 60 s; after that the AI plays it until the player
  returns. Tokens are never removed, so leaving cannot improve anyone's position.
* **Reconnect**: `welcome` → room replay → `gameState` snapshot; the client also sends
  `resync` after every reconnect.
* **Cleanup**: rooms with nobody connected for 10 min, or finished rooms after 2 min, are
  dropped. A player who searches again after a finished match is released from the dead
  room first, so a new search always creates or joins a live table. Abandoned matches are
  refunded (see below).
* **Server crash**: `MatchService.BeginAsync` writes a `status=0` match row *before* play.
  On startup `RecoverStaleAsync` aborts matches still running after two hours and refunds
  every entry fee with an idempotent ledger key.

## Economy invariants

* Coins live only in `wallets`; the client's number is a display value.
* Every change is a `coin_transactions` row with a unique `(account_id, idempotency_key)`.
  A duplicate reward collides on that index and rolls back, so retries are harmless.
* Balance updates use set-based SQL (`UPDATE ... SET balance = balance + ?`), and debits
  require `balance >= amount` in the same statement, so concurrent spends cannot overdraw.
* Settlement is keyed by match id (`status 0 → 1`), so a restart or a retry can never pay
  the pot twice.
* Missions, achievements, daily rewards and the shop are all server-computed and
  server-charged. Coins have no cash value and no withdrawal path.

## Scaling notes (single small VM is enough to start)

* One process, one room object per match, ~1 KB of state per room; a 1 vCPU / 1 GB VM
  handles thousands of rooms.
* `PresenceTracker` is in-memory and per instance. Running more than one instance requires
  sticky sessions or a shared presence store (Redis) plus a room-ownership map - the
  interface is isolated so that change is local.
* PostgreSQL is the only shared state; the connection pool is the real limit.

## Honest limits of this build

* Audio/video goes through a third party (Photon Voice) and is optional.
* Push notifications require your own Firebase project; no key ships in the app.
* Ads and Play Billing are compile-guarded integrations: they stay disabled until you add
  the Google plugins and ids. Coins are never sold for money.
