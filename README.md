# WrathAccess

A screen-reader accessibility mod for **Pathfinder: Wrath of the Righteous**, for blind and
visually-impaired players. It speaks UI focus, menus, and controls via
[Tolk](https://github.com/dkager/tolk), and provides custom keyboard navigation over the game's UI.

> **Status:** early development. The foundation (loader, speech, input, screen/navigation
> framework) and the **main menu** and **Settings** screens are working; more screens are in
> progress.

## What works so far

- Speech output through Tolk (NVDA, JAWS, SAPI, etc.).
- Custom keyboard navigation in mouse mode, with typematic key-repeat matching your OS settings.
- **Main menu** — sidebar buttons with enabled/disabled state.
- **Settings** — tabs, checkboxes, sliders, dropdowns (cycle + an options submenu), and
  **key bindings** (read, rebind with live capture + conflict feedback, and clear).
- Navigable generic confirm/message dialogs (e.g. the unsaved-settings prompt).

## Default keys

Toggle accessibility focus mode with **Ctrl+Shift+A**. While it's on:

| Key | Action |
| --- | --- |
| Arrow keys | Navigate / adjust the focused control |
| Tab / Shift+Tab | Move between regions |
| Enter | Primary action (activate / rebind) |
| Backspace | Secondary action (e.g. clear a key binding) |
| Escape | Back / close |

## Requirements

- Pathfinder: Wrath of the Righteous (Unity 2020.3, Mono backend).
- [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) installed into the game.
- A supported screen reader.

## Building

The mod targets `net48`. With the .NET Framework 4.8 targeting pack and UMM installed into the game:

```
dotnet build
```

A Debug build compiles `WrathAccess.dll` and deploys it (plus `Info.json` and the Tolk DLLs)
into the game's `Mods\WrathAccess\` folder. Restart the game to load changes. See
[`CLAUDE.md`](CLAUDE.md) for the full build/install details.

## License

Not yet chosen. The bundled `tolk/` binaries are third-party (Tolk and its screen-reader client
libraries), redistributed under their own licenses.
