# Wrath Access installer

An accessible wxPython GUI installer for the mod, compiled with **Nuitka
`--onefile`** with the mod payload embedded — the entire release is ONE
`WrathAccessInstaller.exe` (~15 MB). Onefile self-extracts to temp at runtime
(the classic antivirus-heuristic pattern), so Defender behaviour is verified by
testers per release; the local on-demand Defender scan of the first build was
clean. If flags ever appear: `python installer/build.py --folder` builds the
standalone-folder fallback (no self-extraction; zip it), and the durable
escalation is code signing (SignPath's free OSS program / Azure Trusted
Signing).

## What it does
- Detects the game install (default Steam paths + every Steam library via the
  registry and `libraryfolders.vdf`), with a Browse fallback.
- **Install**: copies `payload/WrathAccess` into
  `%LocalLow%\Owlcat Games\Pathfinder Wrath Of The Righteous\Modifications\`,
  recreates the empty `Bundles`/`Blueprints` dirs the game's loader requires,
  adds `WrathAccess` to `EnabledModifications` in
  `OwlcatModificationManagerSettings.json`, and copies the Tolk native DLLs next
  to `Wrath.exe`.
- **Uninstall**: reverses all of it; the user's Wrath Access settings are kept.

## Building a release
```
pip install wxPython nuitka
python installer/build.py            # single-file exe (payload embedded)
python installer/build.py --folder   # standalone-folder fallback (zip it)
```
Output: `installer/build/WrathAccessInstaller.exe` — that exe IS the release.

## Developing / testing the GUI without compiling
```
pip install wxPython
python installer/installer.py
```
(The payload folder only needs to exist for the Install button — run
`build.py` once, or point a junction at a staged payload.)
