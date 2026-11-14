# Ludo Legends — Godot 4.7 edition

Classic Ludo for Android, ported from the Unity C# client to Godot 4.7 (GDScript).
No Unity, no license, no server: you play Red against three bots, fully offline.

## Run / edit
Open this folder (`godot/`) with **Godot 4.7.x** and press F5.
You can also open it on the phone with the Godot Android editor.

## Build the APK
The GitHub Actions workflow `.github/workflows/godot-android.yml` exports a signed
APK on every push (Artifacts → `LudoLegends-apk`). It needs nothing but the repo:
Godot 4.7.2, export templates, JDK 17 and the Android SDK are set up inside CI.

Local build (any PC with Godot 4.7 + Android export templates + Android SDK):

```
godot --headless --path godot --export-release "Android" build/LudoLegends.apk
```

## Layout
| File | What it does |
| --- | --- |
| `scripts/topology.gd` | 15×15 board geometry: 52-cell ring, 5-cell home columns, safe squares |
| `scripts/engine.gd` | Rules engine: roll, move, capture, blocking, three-sixes, exact roll home |
| `scripts/ai.gd` | Bot heuristics (capture > home > enter home column > leave base > safe square) |
| `scripts/board_view.gd` | Draws the board and tokens, turns taps into token picks |
| `scripts/main.gd` | HUD, turn flow, bot pacing, win screen |
| `tests/smoke.gd` | Headless test: 120 full games plus rule assertions |

Run the test with:

```
godot --headless --path godot --script res://tests/smoke.gd
```
