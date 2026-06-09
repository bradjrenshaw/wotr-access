# Wrath Access installer

An accessible **Rust + wxdragon** (native wxWidgets controls) GUI installer —
the same proven approach as the SayTheSpire2 installer: compiles to a small
ordinary exe with no self-extraction or runtime unpacking, so it doesn't trip
antivirus heuristics the way packaged Python does (the Python/Nuitka attempt
got `Wacatac.H!ml`-flagged mid-build and was abandoned).

## What it does
- Detects the game across Steam libraries (`libraryfolders.vdf`), Browse fallback.
- **Install / Update**: downloads the chosen release's `WrathAccess.zip` from
  GitHub (version picker incl. pre-releases, release notes shown first) and
  extracts it: `WrathAccess/` into the game's LocalLow `Modifications\` folder
  (replaced wholesale), `game/` (Tolk natives) next to `Wrath.exe`; recreates
  the empty `Assemblies`/`Bundles`/`Blueprints` dirs the loader requires; adds
  `WrathAccess` to `EnabledModifications` in
  `OwlcatModificationManagerSettings.json` (other mods' entries preserved).
- **Install from file**: the same from a local zip (testers / offline).
- **Uninstall**: reverses everything; the user's Wrath Access settings are kept.
- `--cli` flag: a fully keyboard/console flow instead of the GUI.

## Building
```
cargo build --release          # in installer/
```
Release artifacts (payload zip + installer exe): `python scripts/release.py`,
then `gh release create vX.Y.Z dist/WrathAccess.zip dist/WrathAccessInstaller.exe`.
Keep the tag in sync with `Version` in `OwlcatModificationManifest.json` — the
installer compares it against release tags to offer updates.
