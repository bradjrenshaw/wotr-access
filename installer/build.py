"""Build the Wrath Access installer release folder.

Steps:
  1. `dotnet build -c Release` (compiles WrathAccess.dll without deploying).
  2. Stage the payload (the exact native-mod layout the dev deploy produces):
       payload/WrathAccess/  — manifest + settings json, Assemblies/ (WrathAccess.dll,
                               TolkDotNet.dll, NAudio.dll), assets/, empty Bundles/ + Blueprints/
       payload/game/         — the Tolk native DLLs that go next to Wrath.exe
  3. Compile installer.py with Nuitka --standalone (NOT --onefile: one-file
     self-extraction is the classic AV-heuristic trigger; standalone is a plain
     folder of real binaries) and place the payload next to the exe.

Output: installer/build/WrathAccessInstaller/ — zip that folder for release.

Prerequisites: pip install wxPython nuitka  (Nuitka offers to fetch a C compiler
on first run if none is installed).
"""

import glob
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
BUILD = os.path.join(HERE, "build")
DIST = os.path.join(BUILD, "WrathAccessInstaller")
PAYLOAD = os.path.join(DIST, "payload")


def run(args, cwd=None):
    print(">", " ".join(args))
    subprocess.check_call(args, cwd=cwd or REPO)


def stage_payload():
    mod = os.path.join(PAYLOAD, "WrathAccess")
    asm = os.path.join(mod, "Assemblies")
    game = os.path.join(PAYLOAD, "game")
    for d in (asm, os.path.join(mod, "Bundles"), os.path.join(mod, "Blueprints"), game):
        os.makedirs(d, exist_ok=True)

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
    if os.path.isdir(BUILD):
        shutil.rmtree(BUILD)

    run(["dotnet", "build", "-c", "Release"])

    print("Compiling installer with Nuitka (standalone)...")
    run([sys.executable, "-m", "nuitka", "--standalone",
         "--assume-yes-for-downloads",
         "--windows-console-mode=disable",
         "--output-dir=" + BUILD,
         "--output-filename=WrathAccessInstaller.exe",
         os.path.join(HERE, "installer.py")], cwd=HERE)

    # Nuitka puts the app in build/installer.dist — rename to the release folder name.
    shutil.move(os.path.join(BUILD, "installer.dist"), DIST)
    stage_payload()

    print("\nDone: " + DIST)
    print("Zip that folder for release; testers run WrathAccessInstaller.exe inside it.")


if __name__ == "__main__":
    main()
