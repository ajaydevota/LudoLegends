# Unity client: setup, build and publishing

## 1. Requirements

* **Unity 6.3 LTS** (6000.3.x) - `ProjectSettings/ProjectVersion.txt` targets `6000.3.26f1`.
  Unity 6.6 also works; 6.0 LTS is out of support.
* Android Build Support with SDK, NDK and OpenJDK (Unity Hub → Add modules).
* Android 6.0 (API 23) minimum; target API level 35/36 for Play.
* No paid assets, no third-party art. Dependencies: `com.unity.ugui` and
  `com.unity.nuget.newtonsoft-json` (already in `Packages/manifest.json`).

## 1a. Checking the code without Unity

The client can be type-checked on a machine with no Unity install:

```bash
cd unity/compile-check && dotnet build     # expect: Build succeeded, 0 errors
```

That project compiles every file in `Assets/Scripts` (including `Editor/BuildScript.cs`)
against a hand-written UnityEngine/UnityEditor API stub. It catches real errors - seven were
found and fixed this way - but it cannot validate asset import, rendering, layout or IL2CPP.
See [unity/compile-check/README.md](../unity/compile-check/README.md).

### Why no APK/AAB is included

Producing one requires all of the following, none of which exist in the build container used
for this project:

| Requirement | Status there |
|---|---|
| Unity 6.3 LTS Editor (~5 GB) + Android module (~6 GB) | not installed; 8.8 GB free disk, needs ~15 GB |
| Installer download | blocked (`unity.com` unreachable) |
| Android SDK + NDK | absent |
| JDK 17 | absent (`java` not found) |
| Unity licence activation | needs an interactive account sign-in or a `.ulf` licence file |

Nothing was faked to work around this. The build script, signing configuration and Play
Console steps below are complete and are what you run on a machine with Unity installed.

## 2. Opening the project

Option A - open `unity/LudoLegends` directly with Unity Hub. Unity generates `Library/`,
`ProjectSettings/*.asset` and the missing scene on first import.

Option B - create a new 2D project and copy `Assets/` and `Packages/manifest.json` over it.

Then, from the repository root:

```bash
./tools/sync-core.sh      # copies the shared rules engine into Assets/Scripts/Shared
```

`Assets/Scripts/Shared` must contain `GameModel.cs`, `BoardTopology.cs`, `LudoGame.cs`,
`LudoAi.cs`, `Protocol.cs`. Never edit those by hand - edit `server/src/LudoLegends.Core`
and re-run the script.

The game needs **no scene setup**: `AppBootstrap` runs from
`[RuntimeInitializeOnLoadMethod]`, builds the canvas, EventSystem and every screen at
runtime. Press Play in an empty scene.

## 3. Configuration

`Assets/Resources/app-config.json` - public values only:

| field | meaning |
|---|---|
| `apiBaseUrl` | server origin. Emulator: `http://10.0.2.2:5080`; device on LAN: `http://<lan-ip>:5080`; production: `https://...` |
| `socketUrl` | optional override; derived from `apiBaseUrl` when empty |
| `googleWebClientId` | OAuth **web** client id; empty disables the Google button |
| `adsEnabled`, `admob*UnitId` | AdMob integration (needs the plugin) |
| `iapEnabled`, `iapRemoveAdsProductId` | Play Billing for "remove ads" |
| `voiceEnabled`, `photonAppId`, `photonVoiceAppId` | voice chat |

Never put a signing key, service account or API secret in this file - it ships inside the
APK.

## 4. Building APK / AAB

Menu: **Ludo Legends → Configure Android Player Settings**, then **Build APK** or
**Build AAB**.

Player settings applied automatically: IL2CPP, ARM64 + ARMv7, API 23 minimum, portrait,
medium stripping, no vSync/AA (2D board game, low-end friendly).

Release signing (required for Play):

```bash
keytool -genkeypair -v -keystore ~/ludo-release.keystore -alias ludo \
        -keyalg RSA -keysize 4096 -validity 10000
export LUDO_KEYSTORE_PATH=~/ludo-release.keystore
export LUDO_KEYSTORE_PASS=...
export LUDO_KEYALIAS_NAME=ludo
export LUDO_KEYALIAS_PASS=...
```

Without them the build uses the debug keystore (fine for testers, rejected by Play) and the
build log warns you.

CI:

```bash
Unity -quit -batchmode -projectPath unity/LudoLegends \
      -executeMethod LudoLegends.EditorTools.BuildScript.BuildAabFromCI \
      -logFile - | tail -40
```

## 5. Google Play publishing checklist

1. `PlayerSettings` package name must be unique (`com.yourstudio.ludolegends` in
   `BuildScript.cs` - change it before the first upload; it cannot change afterwards).
2. Create the app in Play Console, complete **Data safety** (this build collects: display
   name, gameplay stats, optional email if Google sign-in is enabled; no location, no
   contacts) and **Content rating** (no gambling: coins are virtual, no cash value).
3. Upload the AAB to **Internal testing** first, then promote.
4. Add your release SHA-1/SHA-256 fingerprints if you enable Google Sign-In
   (`keytool -list -v -keystore ...`).
5. Provide an account-deletion path: the app has **Settings → Delete my account**, and the
   listing must link to it as required by Play policy.
6. Target API level: Play requires the current level (35/36); Unity 6.3 supports it.
7. Screenshots: the game renders at 1080×1920; capture from a device/emulator.
8. Keep the privacy policy URL in the listing - it must state that guest accounts are
   anonymous and coins have no monetary value.

## 6. Voice chat (optional)

1. Create a Photon application (Realtime + Voice) at photonengine.com and note the App Ids.
2. Import **Photon Voice 2** (it pulls Realtime) into the project.
3. Add `PHOTON_VOICE` to *Project Settings → Player → Scripting Define Symbols*.
4. Implement `IVoiceProvider` with Photon's `VoiceConnection`/`Recorder` (one class, the
   interface already covers join/leave/mic/mute/mute-all).
5. Set `voiceEnabled: true` and the app ids in `app-config.json`; set
   `LUDO__VOICE__PHOTONAPPID` on the server.
6. Point Photon's **Custom Authentication** URL at `https://<server>/api/photon/auth`. The
   client requests `POST /api/voice/token` when entering a room; the server only issues a
   token to a verified member of that room, valid for 5 minutes.

Voice is optional; every call is a no-op when disabled, and leaving a room disconnects voice.

## 7. Ads and billing (optional, disabled by default)

* Import Google Mobile Ads, set `adsEnabled` + unit ids, add `GOOGLE_MOBILE_ADS`.
  `AdsService.CanShowNow` refuses to show anything during a roll or token animation.
* Import Unity IAP, add `UNITY_PURCHASING`, create the `remove_ads` product in Play Console.
* **Coins are never sold for money.** Consequences: no gambling surface, and no
  client-claimed coin grants that could be forged.

## 8. Push notifications (optional)

FCM needs a Firebase project and a service-account JSON on the **server**
(`LUDO__PUSH__SERVICEACCOUNTPATH`). The app never contains a service account. Until you add
it, notifications are simply not sent - turn timers and reconnection work without them.

## 9. Troubleshooting

| Symptom | Fix |
|---|---|
| "Reconnecting..." forever | wrong `apiBaseUrl`, or the server is not reachable from the device (use the LAN IP, not `localhost`) |
| 401 on every call | the token expired or the server's `LUDO__TOKENSIGNINGKEY` changed; sign out and in |
| Board is grey | `tools/sync-core.sh` was not run, so `Shared/` is missing |
| Room code rejected | codes are 6 chars from the unambiguous alphabet; type without spaces |
| Build fails on IL2CPP | Android NDK/OpenJDK missing in Unity Hub modules |
| No sound | device media volume, and Settings → Sound effects |
