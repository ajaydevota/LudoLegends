# Ludo Legends

An original, server-authoritative online Ludo game for Android: Unity 6 client, ASP.NET Core
8 backend, one shared rules engine, real coin economy and every standard Ludo rule.

> **Original work.** All branding, code, board art (procedurally drawn), sounds
> (procedurally synthesised) and UI are created for this project. Nothing is copied from
> Ludo King or any other title.

---

## What is in this repository

```
server/
  src/LudoLegends.Core/        shared rules engine - netstandard2.1, zero dependencies
  src/LudoLegends.Server/      authoritative ASP.NET Core 8 server (REST + WebSocket)
  tests/LudoLegends.Core.Tests/    29 tests: every rule, geometry, AI, dice
  tests/LudoLegends.Server.Tests/  48 tests: economy, anti-cheat, rooms, auth
  tools/BoardPreview/          renders the board from the topology (verification tool)
unity/LudoLegends/
  Assets/Scripts/Shared/       the same rules engine, copied by tools/sync-core.sh
  Assets/Scripts/              client: UI, board, networking, offline play, services
  Assets/Scripts/Editor/       APK/AAB build automation
  Assets/Resources/app-config.json   public client configuration
docs/                          architecture, deployment, schema, security, client setup
tools/sync-core.sh             copies the shared engine into the Unity project
.env.example                   server environment template
```

## What is verified, and what is not

Verified in this environment (Linux container, .NET 8):

* **The Unity client type-checks with zero errors.** `cd unity/compile-check && dotnet build`
  compiles every client script (runtime and Editor) against a UnityEngine API stub, because no
  Unity install fits in this environment. It found and fixed seven real compile errors; it is
  *not* a substitute for opening the project in Unity.
* `dotnet test` - **88 tests pass**: 32 rules/AI, 48 server, and 8 live multiplayer tests that
  start the real server and play matches over separate WebSocket connections. Run it
  yourself: `cd server && dotnet test`.
* Multiplayer verified over real sockets: a four-client match played to a winner (738
  actions, all clients converging on the same final state), paid-table settlement with coin
  conservation, private-room codes with full-room rejection, disconnect/reconnect into the
  same seat, and rejection of out-of-turn rolls, forged moves and duplicated commands.
* Production publish: `dotnet publish src/LudoLegends.Server -c Release` succeeds.
* The server builds and runs; a live smoke test exercised `/health`, guest login, `/api/me`,
  the daily reward claim (and its double-claim rejection), wallet balance, the shop and
  401 responses for unauthenticated calls.
* The board geometry is rendered to `artifacts/board-preview.png` straight from
  `BoardTopology`, so the 52-cell ring, home columns, yards and safe squares can be seen.

**Not verified here, because it cannot be:**

* The Unity project has never been opened in the Editor - there is no Unity install in this
  container (8.8 GB free vs ~15 GB needed, no installer access, no JDK/Android SDK, no license
  activation). The code compiles against an API stub, but asset import and IL2CPP have not run.
* No APK/AAB is included. Building one needs Unity 6.3 LTS + the Android SDK/NDK with your
  keystore. `Assets/Scripts/Editor/BuildScript.cs` automates both.
* The WebSocket path is now exercised end to end by the integration suite, but only from
  scripted .NET clients - not from a phone, and not through the Unity client.
* Voice, ads, Play Billing and push are integration points gated behind your credentials and
  third-party plugins. They are disabled by default and never pretend to work.

## Quick start

### 1. Server (2 minutes, SQLite)

```bash
cd server
dotnet test                      # 77 tests
export Ludo__TokenSigningKey="$(openssl rand -base64 48)"
dotnet run --project src/LudoLegends.Server
# health: http://localhost:5080/health
```

The server creates `ludolegends.db` on first run (development only). Guest login, wallets,
daily rewards, rooms and matches work immediately with no external services.

### 2. Unity client

1. Install **Unity 6.3 LTS** with Android Build Support (SDK/NDK/OpenJDK).
2. Create a new **2D (Built-in or URP)** project named `LudoLegends`, then copy
   `unity/LudoLegends/Assets` and `unity/LudoLegends/Packages/manifest.json` into it.
   (Or open `unity/LudoLegends` directly with Unity Hub - it will generate the rest.)
3. Run `./tools/sync-core.sh` so the shared engine is present.
4. Set `apiBaseUrl` in `Assets/Resources/app-config.json`:
   * Android emulator: `http://10.0.2.2:5080`
   * real device: `http://<your-lan-ip>:5080` (same Wi-Fi) or your HTTPS deployment
5. Press Play. The game builds its own UI; no scene or prefab setup is required.

### 3. Build for Android

```
Unity menu: Ludo Legends -> Build APK     (testing)
Unity menu: Ludo Legends -> Build AAB     (Play Store upload)
```
Release signing is read from `LUDO_KEYSTORE_PATH`, `LUDO_KEYSTORE_PASS`,
`LUDO_KEYALIAS_NAME`, `LUDO_KEYALIAS_PASS`. Without them the build uses the debug key,
which Google Play rejects - the build log warns about this.

## Documentation

| Document | Contents |
|---|---|
| [docs/game-rules.md](docs/game-rules.md) | Board model, 57-step progress, all rules, AI, dice |
| [docs/architecture.md](docs/architecture.md) | Components, wire protocol, rooms, anti-cheat, scaling |
| [docs/server-deployment.md](docs/server-deployment.md) | Free-tier hosting options with real limits, TLS, systemd, costs |
| [docs/database-schema.md](docs/database-schema.md) | Tables, indexes, PostgreSQL DDL, access rules |
| [docs/security.md](docs/security.md) | Threat model, tokens, rate limits, secrets, deletion |
| [docs/unity-client.md](docs/unity-client.md) | Unity setup, config, APK/AAB, Play Store, voice/ads/billing |
| [docs/audit-report.md](docs/audit-report.md) | Audit pass: bugs found and fixed, multiplayer results, remaining gaps |
| [docs/known-issues.md](docs/known-issues.md) | Every remaining limitation, with the exact blocker and next step |
| [docs/build-without-a-pc.md](docs/build-without-a-pc.md) | Building the APK from a phone via cloud CI (no PC needed) |
| [unity/compile-check/README.md](unity/compile-check/README.md) | Type-check the client without a Unity install |

Build artefacts kept in this repository:

| File | What it is |
|---|---|
| [artifacts/LudoLegends-source.zip](artifacts/LudoLegends-source.zip) | Complete source project (client, server, tests, docs) |
| [artifacts/server-test-results.log](artifacts/server-test-results.log) | Actual `dotnet test` output: 88/88 passing |
| [artifacts/unity-compile-check.log](artifacts/unity-compile-check.log) | Actual client type-check: 0 warnings, 0 errors |
| [artifacts/board-preview.png](artifacts/board-preview.png) | Board rendered from the shared topology |

## Design decisions worth knowing

* **One rules engine, two hosts.** Server and client compile the same C# files, so offline
  play provably matches online play.
* **Snapshots, not deltas.** Every accepted command broadcasts the full game state plus
  events; reconnection is one message and clients can never drift.
* **Idempotency everywhere.** Commands, coin credits, entry fees and settlement all carry
  keys with unique indexes behind them, so retries and crashes cannot duplicate value.
* **No secrets in the app.** The client only holds public configuration (server URL, Google
  web client id, ad unit ids). Session tokens are issued and verified by the server.
* **No gambling.** Coins are a closed virtual currency: no purchase for money, no cash-out,
  no betting on outcomes. Shop items are cosmetic.
