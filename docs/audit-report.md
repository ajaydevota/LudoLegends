# Audit report - production completion pass

Date: 13 November 2026. Scope: every source file in this repository, plus live multiplayer
testing over real sockets.

**Bottom line:** the server is production-grade and tested end to end (85 automated tests,
including eight that play real matches over WebSocket). The Unity client is complete in
structure but has never been compiled or run, because this environment has no Unity Editor,
no Android SDK and no device. No APK or AAB is included, and none can be produced here.
Nothing below claims otherwise.

---

## 1. Features implemented

| Area | Status | Evidence |
|---|---|---|
| Rules engine (all standard Ludo rules) | Implemented | 29 unit tests, one per rule |
| Server-authoritative dice/movement/results | Implemented | engine tests + live match tests |
| 2/3/4 player online matches | Implemented | live 4-client match, 3-seat room test |
| Matchmaking + private room codes | Implemented | live tests incl. full-room rejection |
| Turn clock + AI takeover | Implemented | server tests; ticker in `RoomTicker` |
| Disconnect / reconnect | Implemented | live reconnect test |
| Coin economy (ledger, no duplicates) | Implemented | 48 server tests + live settlement test |
| Daily rewards, missions, achievements, shop | Implemented | server tests + REST smoke test |
| Friends, leaderboard, history, profile | Implemented | REST endpoints, server tests |
| Google Sign-In (server side) | Implemented | token validation + link-to-guest tests |
| Voice chat | **Not implemented** | provider interface + token endpoint only |
| Ads / Play Billing | **Not implemented** | disabled, no SDK calls, no UI |
| Push notifications | **Not implemented** | requires your Firebase service account |
| Unity client (UI, board, animations, offline) | Written, **not compiled** | see §7 |

## 2. Bugs found and fixed in this pass

1. **Players were trapped in finished rooms** (critical). After a match ended, the player
   stayed a member of that room, so the next "find match" returned the dead room instead of
   a new table. Fixed with `RoomManager.ReleaseFromFinishedRoom`, called on create/join/find,
   plus the client leaving the room from the result screen. Regression test:
   `A_finished_match_does_not_trap_players_in_the_old_room`.
2. **Reconnect could mark a live player offline** (critical). The old socket's cleanup ran
   after the new socket had registered and cleared `Connected`, so the room saw a
   disconnected player who was actually online - and could hand the seat to autopilot.
   Fixed by skipping detach when the registry holds a newer connection.
3. **Balance smuggled through the error field.** The welcome message put the wallet balance
   into `ServerEnvelope.Error` and the player level into `Seat`. Added a real `Balance`
   field; the HUD now updates from it.
4. **Compile errors in the Unity client**: `VoiceChatService.Instance` was referenced but the
   service is static; `AppSession` called a non-existent `SendLeaveRoom`; `GameController`
   read `envelope.LegalTokens`, which is not a field (it lives on the snapshot).
5. **Result screen hid players who had not finished.** The rank loop skipped rank 0, so the
   last player was missing from a 4-player result. Now every seat is listed.
6. **Unstable error codes.** Engine rejections used ad-hoc strings while the protocol
   documented stable codes. Added `ErrorCodes.NotYourTurn/WrongPhase/IllegalMove/
   MatchFinished` and mapped `CommandStatus` onto them.
7. **No way to cancel matchmaking.** `cancelMatch` was defined in the protocol but
   unimplemented - a client button would have received "unknown message type". Implemented
   server-side (lobby-only, cannot eject a private-room host) and in the client.
8. **Dead code / placeholder paths removed**: `ConnectionSeatsAsync` (no-op hook),
   AdMob/IAP pseudo-SDK calls that would have broken a real integration, and a mic button
   that appeared purely from a config flag without a voice provider.
9. **Hardcoded 25 s timer** in the HUD instead of configured turn length.
10. **Stale snapshots** could roll the board backwards after a reconnect; the controller now
    ignores snapshots older than the current version.
11. **No Android lifecycle handling.** A backgrounded app kept the microphone open and could
    resume with a dead socket. Now pausing leaves the voice channel, and resuming reopens or
    resyncs the connection so the match continues where the server left it.
12. **Autopilot vs. returning player.** Found while stabilising the reconnect test: after the
    grace period the server legitimately rolls for an absent seat, so a player who returns
    mid-turn must act on the phase the snapshot shows (roll *or* move), not assume it is
    their roll. Documented in the client contract and asserted by the test.

Also fixed during this pass: unvalidated table entry fees (now a closed set of 0/100/250,
matching the UI) so clients cannot invent arbitrary stakes.

## 3. Multiplayer test results (real sockets, real server process)

`server/tests/LudoLegends.Integration.Tests` launches the actual server binary on a free
port with a throwaway SQLite database, then connects independent WebSocket peers.

| Test | Result |
|---|---|
| Four independent clients matchmake and play a full match to a winner (738 actions), all four converge on the same final state | **Pass** |
| Two-player paid table: entry fee charged exactly once each, pot paid once to the winner, total coins conserved (1000 → 1000), no double payment after waiting | **Pass** |
| Three-player private room: code join, full-room rejection for a fourth, distinct colours, start | **Pass** |
| Hard socket drop → opponent notified offline → same account reconnects → same match, same seat, can still act | **Pass** |
| Out-of-turn roll rejected (`not_your_turn`), forged die value ignored, token index 99 rejected, duplicate command id does not roll twice, illegal token rejected, non-member rejected | **Pass** |
| `cancelMatch` answered, then a new search works | **Pass** |
| Finished room does not trap the player | **Pass** |
| Protocol carries no field for dice, results or state | **Pass** |

These are genuine network tests: separate TCP connections, server-side dice, server-side
validation. They are **not** device tests - latency, Android lifecycle and UI behaviour on a
phone remain unverified.

## 4. Rules testing

Covered by 29 engine tests: ring geometry, 57-step progress, eight safe cells, home columns,
six to leave base, six grants another roll, illegal die values, rolling twice, out-of-turn
moves, duplicate command ids, capture, safe-cell immunity, blocking, exact-home requirement,
no overshoot, three-sixes forfeit, finished-seat skipping, winning, post-finish rejection,
snapshot legal-token list, crypto dice range, and three AI behaviours.

## 5. Security audit

* No hardcoded credentials anywhere in C#, JSON, shell or docs (grep sweep).
* All player identity derives from a verified JWT; no endpoint accepts an account id.
* Coin changes are append-only ledger rows with a unique `(account, idempotency_key)` index.
* Debits are set-based (`balance >= amount`), so concurrent spending cannot overdraw.
* Settlement is keyed by match id and idempotent across restarts.
* Daily/mission/achievement claims are unique per account per period.
* Socket rate limit (token bucket) plus per-IP HTTP limit (120/min) and a 4 KB message cap.
* Unknown fields in client payloads are ignored, so a forged `die` cannot influence anything.
* Production refuses to start without a 32+ character signing key.

Known weaknesses, documented rather than hidden:

* The WebSocket session token travels in the query string (browsers cannot set headers on
  upgrade). Reverse-proxy access logs must be treated as sensitive, or auth moved to a
  one-time ticket.
* `PresenceTracker` and rooms are in-process, so multi-instance deployment needs sticky
  sessions or a shared room map.
* `FindRoomOf` scans all rooms per message; fine for thousands of rooms on one VM, not for
  tens of thousands.

## 6. Hosting costs

See [docs/server-deployment.md](server-deployment.md). Short version: **Oracle Cloud Always
Free** ARM VM (2 OCPU / 12 GB) for the server plus **Neon free** PostgreSQL (1 GB, sleeps
after 5 idle minutes) is genuinely $0 at this scale. Photon's free tier is 100 CCU for games
only, one app. Nothing gives an always-on WebSocket process for free without a caveat; the
full comparison is in that document.

## 7. Remaining issues (honest list)

1. **The Unity project has never been opened in the Editor.** Its C# now compiles cleanly
   against a Unity API stub (see 7a), which caught and fixed seven real compile errors, but
   asset import, `.meta` generation, package resolution and IL2CPP have not run.
2. **No APK, AAB or debug APK.** Unity 6.3 + Android SDK/NDK + a keystore are required;
   `Assets/Scripts/Editor/BuildScript.cs` automates all three once they exist.
3. **No device, emulator or UI screenshot.** Layout is computed for a 1080x1920 reference
   with `matchWidthOrHeight = 0.5`; overlap on unusual aspect ratios has not been observed
   because nothing can render here.
4. **Voice, ads, billing, push and Google Sign-In are unverified** pending your Photon,
   AdMob, Play Console, Firebase and Google Cloud credentials.
5. **Performance on low-end hardware is untested.** Design choices that help (2D UI only,
   procedural sprites, 1-2 KB snapshots, no per-frame allocations in the game loop, 60 fps
   cap, single ticker thread) are in place, but no measurement was possible here.

## 7a. Phase 3: compiling the client without Unity

**Unity cannot be installed in this environment.** Evidence:

| Requirement | Reality here |
|---|---|
| Disk for Unity 6.3 + Android modules | **8.8 GB free**, needs ~15 GB |
| Installer download | `unity.com` unreachable (connection timeout) |
| Android SDK / NDK | absent |
| JDK | `java` not installed |
| License activation | requires an interactive Unity account sign-in; `convex`-style headless activation needs a `.ulf` license file, which cannot be generated here |

So instead of fabricating a build, every client script was compiled against a faithful
UnityEngine API stub (`unity/compile-check`):

```bash
cd unity/compile-check && dotnet build     # 0 errors, 0 warnings from game code
```

This is a real compile of all 8,000+ lines of client C#, including the Editor build script
(`UNITY_EDITOR` defined). It found three further genuine Unity compile errors, now fixed:

13. `AppSession.cs` used `RoomSnapshot` without `using LudoLegends.Core;`.
14. `SocketClient` declared its serializer settings as `System.Text.Json.JsonSerializerOptions`
    and passed them to Newtonsoft's `JsonConvert` — two different libraries, so this could
    never compile in Unity. It now uses `JsonSerializerSettings` with a camelCase contract
    resolver, keeping the wire format identical.
15. `UIRouter` is a plain class but called `StartCoroutine` 24 times. Coroutines are now hosted
    by the bootstrap object, so they also survive screen rebuilds.

It also exposed two stub-level API mismatches (UnityWebRequest.EscapeURL/Result and
ScrollRect.scrollSensitivity) that confirmed those calls are correct Unity API usage.

## 8. Reproducing every check

```bash
cd server
dotnet test                        # 88 tests: 32 rules/AI + 48 server + 8 live multiplayer
dotnet publish src/LudoLegends.Server -c Release -o publish

cd ../unity/compile-check
dotnet build                       # type-checks the whole Unity client, 0 errors
```

The full suite was run three times back to back after the last change: 85/85 passing every
run, no flaky retries.
