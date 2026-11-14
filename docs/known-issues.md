# Known issues and limitations

Anything marked **blocked** cannot be completed in the environment this project was built
in; the blocker and the exact remaining step are listed. Nothing here is hidden behind
optimistic wording.

## Blocked by the build environment

### 1. No APK / AAB exists — blocked

`LudoLegends-Debug.apk`, `LudoLegends-Release.apk` and `LudoLegends-Release.aab` were **not
generated**. Required and missing:

| Requirement | Status |
|---|---|
| Unity 6.3 LTS Editor (~5 GB) + Android Build Support (~6 GB) | not installed; 8.8 GB free disk |
| Installer download | `unity.com` unreachable from the build container |
| Android SDK + NDK | absent |
| JDK 17 | absent (`java` not found) |
| Unity licence | needs interactive sign-in or a `.ulf` file |

Remaining step: on a machine with Unity 6.3 LTS plus Android modules, run
`Ludo Legends -> Build AAB` (or `BuildScript.BuildAabFromCI`), then `Build APK`. Full
instructions: [unity-client.md](unity-client.md).

### 2. The project has never been opened in the Unity Editor — partially mitigated

Every client script compiles against a UnityEngine API stub (`unity/compile-check`,
0 errors), which caught seven real compile errors. Not exercised: asset import, `.meta`
generation, package resolution, IL2CPP/AOT, and anything that needs a renderer.

Remaining step: open `unity/LudoLegends` in Unity Hub, let it generate `Library/`, then
`./tools/sync-core.sh` and press Play.

### 3. No visual or device testing — blocked

- No screenshot, no device, no emulator: layout has never been seen rendered.
- Screen sizes: the canvas uses a 1080×1920 reference with `matchWidthOrHeight = 0.5` and
  screens are anchored, but odd aspect ratios and display cut-outs are **unverified**.
  `Screen.safeArea` insets are not applied yet — expect to add them for notched phones.
- Performance on low-end hardware (2 GB RAM): **unmeasured**. The design is cheap (2D UGUI,
  procedural sprites, 1–2 KB snapshots, one ticker thread, 60 fps cap) but that is an
  argument, not a measurement.
- Memory, battery and long-session behaviour: unmeasured.

### 4. External services are unverified — need your credentials

| Feature | What is missing |
|---|---|
| Google Sign-In | Google Cloud OAuth **web** client id (server `Ludo__GOOGLECLIENTID`, client `googleWebClientId`); optional Play Games plugin |
| Voice chat | Photon Voice 2 package, Photon app id/voice app id, `PHOTON_VOICE` define, custom-auth URL |
| Ads | Google Mobile Ads plugin, AdMob app id + unit ids. **No ad SDK call exists in the code** — ads are a policy gate only |
| Play Billing | Unity IAP, Play Console product `remove_ads`. **No purchase code exists** |
| Push notifications | Firebase project + service-account JSON on the server only |

Until configured, all of these are inert: no button promises them, no fake success is shown.

## Known engineering limitations

### 5. Single-instance only

Rooms and presence live in process memory. Running more than one server instance requires
sticky sessions or a shared room/presence store (Redis). The interfaces are isolated so that
change is local, but it has not been implemented.

### 6. `FindRoomOf` is a linear scan

Each message scans all rooms. Fine for thousands of rooms on one VM, not for tens of
thousands. Fix would be an account→room index in `RoomManager`.

### 7. Session token travels in the WebSocket query string

Browsers cannot set headers on an upgrade, so `?token=` is supported alongside the
`Authorization` header. Consequence: reverse-proxy access logs must be treated as sensitive,
or auth should move to a one-time socket ticket.

### 8. Rate limits are per IP and per socket

120 HTTP requests/minute per IP and a token bucket per socket. Players behind one NAT share
an IP budget; tune `Ludo__GAME__MESSAGESPERSECOND` and the HTTP limiter for large events.

### 9. Gameplay polish that is intentionally absent

- No rematch button (protocol constant `rematch` is reserved and unimplemented — there is no
  button for it, so nothing is broken).
- No spectator mode, no voice chat moderation/reporting, no friend-invite deep links.
- Avatars and emotes are preset ids; there is no asset store or upload.

### 10. Cosmetics and economy scope

Coins are a closed virtual currency: no purchase for money, no cash-out, no betting on
outcomes. The shop contains cosmetic items only. This is deliberate (Play gambling policy and
store review), not an unfinished feature.

## Bugs fixed during the audit (for reference)

15 real defects were found and fixed across three passes: players trapped in finished rooms,
reconnect marking a live player offline, balance sent through the error field, four Unity
compile errors (missing `using`, `JsonSerializerOptions` handed to Newtonsoft,
`StartCoroutine` on a plain class, `envelope.LegalTokens`), the result screen hiding the last
player, unstable error codes, unimplemented `cancelMatch`, fake AdMob/IAP calls, a hardcoded
25 s timer, stale snapshots rewinding the board, missing Android lifecycle handling, and the
autopilot/turn-phase contract. Full detail: [audit-report.md](audit-report.md).
