# Wrath Access installer

An accessible wxPython GUI installer for the mod, compiled with **Nuitka
`--standalone`** (a plain folder of real native binaries — deliberately NOT
`--onefile`, whose self-extract-to-temp behaviour is the classic antivirus
false-positive trigger). Defender behaviour is verified by testers per release;
if flags ever appear, the escalation path is code signing (SignPath's free OSS
program / Azure Trusted Signing).

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
python installer/build.py
```
Output: `installer/build/WrathAccessInstaller/` — zip it; testers run
`WrathAccessInstaller.exe` inside.

## Developing / testing the GUI without compiling
```
pip install wxPython
python installer/installer.py
```
(The payload folder only needs to exist for the Install button — run
`build.py` once, or point a junction at a staged payload.)
