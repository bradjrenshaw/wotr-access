# WrathAccess — Accessibility Mod for Pathfinder: Wrath of the Righteous

Screen-reader accessibility mod for blind players. Speaks UI focus, menus,
dialogue, and (later) turn-based combat via Tolk. Sibling project to
SayTheSpire / SayTheSpire2; reuse those patterns where they fit.

## Game facts
- **Engine**: Unity **2020.3.48f1**, **Mono** scripting backend (not IL2CPP) →
  `Assembly-CSharp.dll` is fully decompilable and Harmony-patchable.
- **Install**: `C:\Program Files (x86)\Steam\steamapps\common\Pathfinder Second Adventure`
- **Managed dir** (reference assemblies): `<Game>\Wrath_Data\Managed`
  - `Assembly-CSharp.dll` — game code, `Kingmaker.*` namespace.
  - `Owlcat.Runtime.UI.dll` — UI controls + the **console navigation / focus** system.
  - `0Harmony.dll` ships with the game.
- **Modding framework**: **Unity Mod Manager (UMM)**. WotR has first-class UMM support.
- **Target framework**: `net48`. (Must match/exceed net4.8 because UMM's bundled
  `0Harmony` 2.3.6 is built against net4.8; targeting net472 makes MSBuild drop the
  UnityModManager reference.) Needs the .NET Framework 4.8 targeting pack to build.
- **Harmony**: reference UMM's `0Harmony.dll` from
  `<Managed>\UnityModManager\0Harmony.dll` (the instance UMM loads at runtime),
  not the game's older bundled copy.

## Decompiled reference (not in this repo)
- `../wotr-decompiled/Assembly-CSharp-full/` — **COMPLETE** (~10,070 types). Game
  code, `Kingmaker.*`. This is the one to use.
- `../wotr-decompiled/Owlcat.Runtime.UI/` — decompiled cleanly, the UI/navigation
  layer (console-nav, MVVM base, controls). Where focus/control logic lives.
- `../wotr-decompiled/Owlcat.Runtime.Core/` — decompiled cleanly.
- `../wotr-decompiled/Assembly-CSharp/` — old PARTIAL ilspycmd dump (~1038 files);
  superseded by `-full`, kept only as a fallback.

### Re-decompiling after a game update
`ilspycmd` (and any naive ICSharpCode run) **stack-overflows** on ~100 of Owlcat's
heavily-generic types — an *infinite* conversion-resolution cycle, uncatchable, so it
kills the process and truncates output. Use the crash-isolating wrapper at
`../wotr-decompiled/_decompiler/` (run `run.sh`): it records progress before each type,
and when the process dies it restarts past the offending index, skipping ~1% and
capturing the rest. ~100 restarts is normal.

## Build & deploy
```
dotnet build
```
Debug build compiles `WrathAccess.dll` and copies it + `Info.json` +
`TolkDotNet.dll` into `<Game>\Mods\WrathAccess\`, and the native Tolk dlls next
to `Wrath.exe`. Then restart the game. Release does **not** deploy.

Prerequisite: **UMM must be installed into the game first** (it provides
`UnityModManager.dll`, referenced by the csproj at
`<Managed>\UnityModManager\UnityModManager.dll`). If your install put UMM
elsewhere, override `-p:UmmDll=...`. UMM is installed here via the **doorstop**
method: `winhttp.dll` + `doorstop_config.ini` in the game root, manager binaries
+ `Config.xml` in `<Managed>\UnityModManager\`.

## Logs
UMM log: `<Game>\Mods\UnityModManager.log` (and the in-game UMM console).
Our lines go through `Main.Log` (UMM's logger).

## Navigation strategy (decided)
**Custom keyboard navigation in Mouse mode** — NOT the gamepad/console-nav system.
Console nav works but is too coarse (linearizes complex screens, no typeahead/panel
jumps) and reshapes the whole UI. Instead we build our own nav over the live
**View/VM tree** (`ViewBase<TVM>` + `GetViewModel()`), read/activate via the
`OwlcatSelectable`/`IConsoleEntity` control family, and scope to the active screen via
`RootUIContext`. We suppress the game's own keybindings with
`Game.Instance.Keyboard.Disabled` while our focus mode owns the keyboard.

## Architecture (current — input substrate)
- `src/Main.cs` — UMM entry (`Load`). Boots Tolk, Harmony, registers input,
  ticks `InputManager` from `OnUpdate`; `Enabled` master switch.
- `src/Tts.cs` — Tolk wrapper. Never interrupts by default (SayTheSpire preference).
- `src/Input/` — ported SayTheSpire2 input framework, Unity-backed:
  `InputManager` (registry + per-frame poll), `InputAction`, `InputBinding` +
  `KeyboardBinding` (Unity `KeyCode` polling; needs `UnityEngine.InputLegacyModule`).
- `src/FocusMode.cs` — holds `KeyboardAccess.Disabled.Scope()` to mute game hotkeys.
- Current bindings are proof-of-life test hotkeys (Ctrl+Shift+A toggle focus mode,
  Ctrl+Shift+T speak test) — to be replaced by the real nav action set.

## Roadmap
1. **(done)** Loader + TTS + UMM doorstop install + full decompile.
2. **(in progress)** Input substrate: InputManager + FocusMode suppression.
3. Output: port the `Message`/localization layer.
4. Active-screen resolver (`RootUIContext`) + screen base.
5. Generic control adapter (`OwlcatSelectable` family: read label/state, activate, hover-for-tooltip).
6. Custom nav model (tab/shift-tab between groups, arrows, typeahead) over the View/VM tree.
7. First screen: main-menu sidebar (`ContextMenuEntityVM` list) → New Game wizard.
8. Later: character creation (level-up UI), combat (model-driven), exploration scan.
