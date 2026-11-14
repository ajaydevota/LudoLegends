# Building the APK without a PC

You have an Android phone and no computer. That is workable: the build has to happen on a
Linux machine in the cloud, and both legitimate options below are driven from a browser.

**Nothing here has been run by this project's author** — no cloud account was available, and
no Android build has ever been produced for this code. These are the correct steps, not a
report of a successful build.

## What you need first (all doable from a phone browser)

1. A **Unity account** (free): <https://id.unity.com> — same login as the Unity Editor.
2. Put this project in a **Git repository**: create a repo on GitHub (mobile web works), then
   upload `LudoLegends-source.zip` contents. GitHub's web uploader cannot unzip, so use the
   GitHub mobile app or a phone-side git client (e.g. Termux + git, or Working Copy on iOS).
   A public repo keeps CI minutes free; the repo contains no secrets.
3. An **Android keystore** for a release build. You can generate one in Termux:
   ```bash
   pkg install openjdk-17
   keytool -genkeypair -v -keystore ludo-release.keystore -alias ludo \
           -keyalg RSA -keysize 4096 -validity 10000
   ```
   Keep the file and passwords safe: Google Play will not accept an update signed with a
   different key. (For a first test build you can skip this and use the debug key.)

## Option A — Unity Build Automation (first-party, has a free tier)

Unity's own cloud builder, formerly "Unity Cloud Build".

* Free tier: **5 GB storage, 200 Windows build minutes, 2 concurrent machines**
  ([Unity support](https://support.unity.com/hc/en-us/articles/6093104438932-How-does-the-pricing-for-Unity-Build-Automation-work),
  Feb 2026). Android builds consume minutes; a small project like this is typically a few
  minutes per build, so the free allowance is enough for several attempts.
* Steps: <https://cloud.unity.com> → DevOps → Build Automation → connect your Git repo →
  add an **Android** target configuration → set the Unity version to **6000.3.26f1** →
  Build. The finished `.apk`/`.aab` is downloadable from the build page, straight to the phone.
* Build Automation can also run the project's own build entry point if you prefer:
  `LudoLegends.EditorTools.BuildScript.BuildAabFromCI`.

## Option B — GitHub Actions with GameCI (free minutes on public repos)

GameCI's `game-ci/unity-builder` action installs Unity, activates a licence and builds.

```yaml
# .github/workflows/android.yml
name: Android build
on: [workflow_dispatch]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/cache@v4
        with:
          path: unity/LudoLegends/Library
          key: Library-Android-${{ hashFiles('unity/LudoLegends/Packages/manifest.json') }}
      - uses: game-ci/unity-builder@v4
        env:
          UNITY_LICENSE: ${{ secrets.UNITY_LICENSE }}   # .ulf file contents
          UNITY_EMAIL: ${{ secrets.UNITY_EMAIL }}
          UNITY_PASSWORD: ${{ secrets.UNITY_PASSWORD }}
        with:
          projectPath: unity/LudoLegends
          targetPlatform: Android
          androidExportType: androidAppBundle
          buildMethod: LudoLegends.EditorTools.BuildScript.BuildAabFromCI
      - uses: actions/upload-artifact@v4
        with:
          name: LudoLegends-AAB
          path: build/Android
```

* The licence: run `game-ci/unity-request-activation-file` once (it prints a request file),
  upload it at <https://license.unity3d.com/manual> to get a `.ulf`, and store it as the
  `UNITY_LICENSE` secret. All of that works from a phone browser.
* Free minutes: GitHub gives 2,000 Actions minutes/month on private repos and unlimited on
  public repos; a Unity Android build is roughly 5–15 minutes cold, ~2–5 with the Library cache.

## After either build

1. Download the `.apk` on the phone, allow "install unknown apps" for your browser, tap it.
2. The game will show its login screen, but **online play needs a server it can reach**.
   `10.0.2.2` in `Assets/Resources/app-config.json` is emulator-only; on a phone you must point
   it at a public HTTPS origin (see [server-deployment.md](server-deployment.md)) and rebuild,
   or the app will sit at "Reconnecting…".
3. Offline modes (pass-and-play, versus AI) need no server and work immediately.

## Honest status

| Item | State |
|---|---|
| Source | complete, in `artifacts/LudoLegends-source.zip` |
| Client C# | compiles against a Unity API stub, 0 errors (`unity/compile-check`) |
| APK / AAB | **never produced** — no Unity, no SDK, no licence in this environment |
| Cloud build | **not attempted** — needs your Unity/GitHub account |
| Phone install | **never tested** |
