# Editor-less compile check for the Unity client

This project compiles **every C# file** in `Assets/Scripts` (runtime *and* `Editor/`) against
a hand-written stub of the UnityEngine / UnityEngine.UI / UnityEditor APIs, so the client can
be type-checked on a machine with no Unity install.

```bash
cd unity/compile-check
dotnet build          # 0 errors = the client type-checks
```

## What it does prove

* every file parses and every type/member the client calls on **its own classes** exists with
  a compatible signature (this is what catches things like a renamed method or a missing
  `using`);
* no duplicate members, no ambiguous overloads, no invalid casts, no wrong argument counts;
* the shared rules engine files in `Assets/Scripts/Shared` still compile as part of the client;
* `Assets/Scripts/Editor/BuildScript.cs` compiles too (`UNITY_EDITOR` is defined).

## What it does not prove

* that the stub signatures match Unity's real API **exactly**. They follow the public Unity 6
  API for everything used here, but a mismatch in a rarely used overload is possible;
* anything runtime: serialization, rendering, layout, coroutine timing, Android lifecycle,
  permissions, IL2CPP/AOT behaviour, or actual frame rates;
* that the project imports cleanly into the Editor (asset GUIDs, `.meta` files, packages).

So this replaces "it looks like it compiles" with "it does compile against these signatures".
It does **not** replace opening the project in Unity 6.3 LTS.

## Stub layout

| File | Covers |
|---|---|
| `Stubs/UnityEngine.cs` | Object/GameObject/Transform/RectTransform, Vector/Color/Rect/Mathf/Time/Screen/Application, Debug, PlayerPrefs, Resources, Sprite/Texture/AudioClip/AudioSource, attributes |
| `Stubs/UnityEngine.UI.cs` | UGUI (Image, Text, Button, InputField, ScrollRect, layout groups, CanvasScaler, Outline), EventSystems, UnityWebRequest |
| `Stubs/UnityEditor.cs` | MenuItem, PlayerSettings (+ Android), NamedBuildTarget, BuildPipeline/BuildReport, EditorBuildSettings/EditorUserBuildSettings |

When Unity's real API and a stub disagree, fix the stub — never the game code that compiles in
Unity. Any change to `Assets/Scripts` should be followed by `dotnet build` here.
