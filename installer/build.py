"""Build the Wrath Access installer.

Default: a SINGLE-FILE WrathAccessInstaller.exe — Nuitka --onefile with the whole
mod payload embedded (--include-data-dir), so the entire release is one exe.
Onefile self-extracts to temp at runtime, which is the classic AV-heuristic
pattern; Nuitka's onefile has a far better reputation than PyInstaller's, and
testers + a local Defender scan verify each release. If flags ever appear, build
with --folder for the standalone-folder fallback (no self-extraction; zip it),
and the durable escalation is code signing (SignPath OSS / Azure Trusted Signing).

Steps:
  1. `dotnet build -c Release` (compiles WrathAccess.dll without deploying).
  2. Stage the payload (the exact native-mod layout the dev deploy produces):
       payload/WrathAccess/  — manifest + settings json, Assemblies/ (WrathAccess.dll,
                               TolkDotNet.dll, NAudio.dll), assets/, empty Bundles/ + Blueprints/
       payload/game/         — the Tolk native DLLs that go next to Wrath.exe
  3. Compile installer.py with Nuitka.

Output: installer/build/WrathAccessInstaller.exe (or, with --folder,
installer/build/WrathAccessInstaller/ — zip that folder).

Prerequisites: pip install wxPython nuitka  (Nuitka fetches a C compiler on
first run if none is installed).
"""

import glob
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
BUILD = os.path.join(HERE, "build")
STAGED_PAYLOAD = os.path.join(BUILD, "payload")


def run(args, cwd=None):
    print(">", " ".join(args))
    subprocess.check_call(args, cwd=cwd or REPO)


def stage_payload():
    mod = os.path.join(STAGED_PAYLOAD, "WrathAccess")
    asm = os.path.join(mod, "Assemblies")
    game = os.path.join(STAGED_PAYLOAD, "game")
    for d in (asm, os.path.join(mod, "Bundles"), os.path.join(mod, "Blueprints"), game):
        os.makedirs(d, exist_ok=True)
    # Empty dirs don't survive archives/data-embedding; the installer recreates
    # Bundles/Blueprints/Assemblies at install time regardless (the loader
    # throws if they're missing), so .keep files here are just belt-and-braces.
    for d in ("Bundles", "Blueprints"):
        open(os.path.join(mod, d, ".keep"), "w").close()

    # Mod root: manifest + (required-even-empty) settings json.
    shutil.copy2(os.path.join(REPO, "OwlcatModificationManifest.json"), mod)
    shutil.copy2(os.path.join(REPO, "OwlcatModificationSettings.json"), mod)

    # Assemblies: our dll + the managed deps (managed dlls ONLY in here).
    shutil.copy2(os.path.join(REPO, "bin", "Release", "WrathAccess.dll"), asm)
    shutil.copy2(os.path.join(REPO, "tolk", "TolkDotNet.dll"), asm)
    naudio = glob.glob(os.path.join(os.path.expanduser("~"), ".nuget", "packages",
                                    "naudio", "*", "lib", "net35", "NAudio.dll"))
    if not naudio:
        raise SystemExit("NAudio.dll not found in the NuGet cache — run a build first.")
    shutil.copy2(sorted(naudio)[-1], asm)

    # Assets at the mod ROOT (everything under Assemblies/ gets Assembly.LoadFrom'd).
    shutil.copytree(os.path.join(REPO, "assets"), os.path.join(mod, "assets"))

    # Native DLLs for the game folder.
    for dll in ("Tolk.dll", "nvdaControllerClient64.dll", "SAAPI64.dll"):
        shutil.copy2(os.path.join(REPO, "tolk", "x64", dll), game)


def main():
    folder_mode = "--folder" in sys.argv

    if os.path.isdir(BUILD):
        shutil.rmtree(BUILD)

    run(["dotnet", "build", "-c", "Release"])
    stage_payload()

    nuitka = [sys.executable, "-m", "nuitka",
              "--assume-yes-for-downloads",
              "--windows-console-mode=disable",
              "--output-dir=" + BUILD,
              "--output-filename=WrathAccessInstaller.exe"]
    if folder_mode:
        print("Compiling installer with Nuitka (standalone folder)...")
        nuitka += ["--standalone"]
    else:
        print("Compiling installer with Nuitka (single-file exe, payload embedded)...")
        nuitka += ["--onefile", "--include-data-dir=" + STAGED_PAYLOAD + "=payload"]
    run(nuitka + [os.path.join(HERE, "installer.py")], cwd=HERE)

    if folder_mode:
        dist = os.path.join(BUILD, "WrathAccessInstaller")
        shutil.move(os.path.join(BUILD, "installer.dist"), dist)
        shutil.copytree(STAGED_PAYLOAD, os.path.join(dist, "payload"))
        print("\nDone: " + dist)
        print("Zip that folder for release; testers run WrathAccessInstaller.exe inside it.")
    else:
        print("\nDone: " + os.path.join(BUILD, "WrathAccessInstaller.exe"))
        print("That single exe IS the release (payload embedded).")


if __name__ == "__main__":
    main()
