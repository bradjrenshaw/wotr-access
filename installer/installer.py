"""Wrath Access installer — accessible wxPython GUI.

Installs the WrathAccess mod into the game's NATIVE mod system (no Unity Mod Manager):
  1. Copies the bundled mod folder (payload/WrathAccess) to
     %LocalLow%/Owlcat Games/Pathfinder Wrath Of The Righteous/Modifications/WrathAccess
     (recreating the empty Bundles/ and Blueprints/ dirs the game's loader requires).
  2. Adds "WrathAccess" to EnabledModifications in OwlcatModificationManagerSettings.json
     (created if missing; other fields/entries preserved).
  3. Copies the Tolk native DLLs (payload/game/*) next to Wrath.exe so P/Invoke finds them.
Uninstall reverses all three (user settings under LocalLow/WrathAccess are kept).

Accessibility notes: standard wx controls only (native Win32 = MSAA/UIA friendly),
labels attached to fields, mnemonics on buttons, a read-only multiline log that
screen readers can review, and message boxes for completion (announced as modals).

Build: see build.py (Nuitka --standalone; NOT onefile, to avoid AV heuristics).
"""

import json
import os
import re
import shutil
import sys

import wx

MOD_NAME = "WrathAccess"
GAME_FOLDER_NAME = "Pathfinder Second Adventure"
GAME_EXE = "Wrath.exe"
LOCALLOW_GAME_DIR = os.path.join("Owlcat Games", "Pathfinder Wrath Of The Righteous")
MANAGER_SETTINGS = "OwlcatModificationManagerSettings.json"
TOLK_DLLS = ["Tolk.dll", "nvdaControllerClient64.dll", "SAAPI64.dll"]


# ---------------------------------------------------------------- paths

def payload_dir() -> str:
    """The payload shipped next to the installer (payload/WrathAccess + payload/game)."""
    base = os.path.dirname(os.path.abspath(sys.argv[0]))
    return os.path.join(base, "payload")


def local_low() -> str:
    profile = os.environ.get("USERPROFILE") or os.path.expanduser("~")
    return os.path.join(profile, "AppData", "LocalLow")


def game_locallow_dir() -> str:
    return os.path.join(local_low(), LOCALLOW_GAME_DIR)


def find_game_dir() -> str:
    """Best-effort game install detection: default Steam path, then every Steam library."""
    candidates = [
        r"C:\Program Files (x86)\Steam\steamapps\common\\" + GAME_FOLDER_NAME,
        r"C:\Program Files\Steam\steamapps\common\\" + GAME_FOLDER_NAME,
    ]
    candidates.extend(steam_library_candidates())
    for c in candidates:
        if os.path.isfile(os.path.join(c, GAME_EXE)):
            return c
    return ""


def steam_library_candidates():
    """Steam install dir from the registry + extra libraries from libraryfolders.vdf."""
    out = []
    try:
        import winreg
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam") as key:
            steam_path = winreg.QueryValueEx(key, "SteamPath")[0]
    except OSError:
        return out
    steam_path = os.path.normpath(steam_path)
    libs = [steam_path]
    vdf = os.path.join(steam_path, "steamapps", "libraryfolders.vdf")
    try:
        with open(vdf, "r", encoding="utf-8", errors="replace") as f:
            # crude but adequate: every "path" "X:\\library" line names a library root
            libs.extend(m.replace("\\\\", "\\") for m in
                        re.findall(r'"path"\s+"([^"]+)"', f.read()))
    except OSError:
        pass
    for lib in libs:
        out.append(os.path.join(lib, "steamapps", "common", GAME_FOLDER_NAME))
    return out


# ---------------------------------------------------------------- install steps

def load_manager_settings(path: str) -> dict:
    if os.path.isfile(path):
        with open(path, "r", encoding="utf-8-sig") as f:
            data = json.load(f)
        if not isinstance(data, dict):
            data = {}
    else:
        data = {}
    data.setdefault("SourceDirectories", [])
    data.setdefault("EnabledModifications", [])
    return data


def save_manager_settings(path: str, data: dict) -> None:
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=2)


def install(game_dir: str, log) -> None:
    src_mod = os.path.join(payload_dir(), MOD_NAME)
    src_game = os.path.join(payload_dir(), "game")
    if not os.path.isdir(src_mod):
        raise RuntimeError("Installer payload is missing (payload/%s). "
                           "Re-download the installer package." % MOD_NAME)
    if not os.path.isfile(os.path.join(game_dir, GAME_EXE)):
        raise RuntimeError("That folder doesn't contain %s — pick the game's install folder."
                           % GAME_EXE)

    # 1. The mod folder. Replace wholesale so stale files from older versions can't linger.
    dest_mod = os.path.join(game_locallow_dir(), "Modifications", MOD_NAME)
    log("Installing mod files to " + dest_mod)
    if os.path.isdir(dest_mod):
        shutil.rmtree(dest_mod)
    shutil.copytree(src_mod, dest_mod)
    # The game's loader enumerates these and throws if they're missing (zips drop empty dirs).
    for required in ("Bundles", "Blueprints", "Assemblies"):
        os.makedirs(os.path.join(dest_mod, required), exist_ok=True)

    # 2. Enable the mod.
    settings_path = os.path.join(game_locallow_dir(), MANAGER_SETTINGS)
    log("Enabling the mod in " + MANAGER_SETTINGS)
    data = load_manager_settings(settings_path)
    if MOD_NAME not in data["EnabledModifications"]:
        data["EnabledModifications"].append(MOD_NAME)
    save_manager_settings(settings_path, data)

    # 3. Tolk natives next to Wrath.exe.
    log("Copying screen-reader libraries next to " + GAME_EXE)
    for dll in TOLK_DLLS:
        src = os.path.join(src_game, dll)
        if not os.path.isfile(src):
            raise RuntimeError("Installer payload is missing payload/game/" + dll)
        try:
            shutil.copy2(src, os.path.join(game_dir, dll))
        except PermissionError:
            raise RuntimeError("Couldn't copy %s — close the game if it's running, "
                               "then install again." % dll)
    log("Install complete.")


def uninstall(game_dir: str, log) -> None:
    dest_mod = os.path.join(game_locallow_dir(), "Modifications", MOD_NAME)
    if os.path.isdir(dest_mod):
        log("Removing " + dest_mod)
        shutil.rmtree(dest_mod)

    settings_path = os.path.join(game_locallow_dir(), MANAGER_SETTINGS)
    if os.path.isfile(settings_path):
        log("Disabling the mod in " + MANAGER_SETTINGS)
        data = load_manager_settings(settings_path)
        data["EnabledModifications"] = [m for m in data["EnabledModifications"] if m != MOD_NAME]
        save_manager_settings(settings_path, data)

    if game_dir and os.path.isdir(game_dir):
        for dll in TOLK_DLLS:
            p = os.path.join(game_dir, dll)
            if os.path.isfile(p):
                log("Removing " + p)
                try:
                    os.remove(p)
                except PermissionError:
                    raise RuntimeError("Couldn't remove %s — close the game if it's running, "
                                       "then uninstall again." % dll)
    log("Uninstall complete. (Your Wrath Access settings were kept.)")


# ---------------------------------------------------------------- GUI

class InstallerFrame(wx.Frame):
    def __init__(self):
        super().__init__(None, title="Wrath Access Installer")
        panel = wx.Panel(self)
        outer = wx.BoxSizer(wx.VERTICAL)

        intro = wx.StaticText(panel, label=(
            "Installs the Wrath Access screen-reader mod for "
            "Pathfinder: Wrath of the Righteous."))
        outer.Add(intro, 0, wx.ALL, 10)

        row = wx.BoxSizer(wx.HORIZONTAL)
        label = wx.StaticText(panel, label="&Game folder:")
        self.path = wx.TextCtrl(panel, value=find_game_dir(), size=(420, -1))
        browse = wx.Button(panel, label="&Browse...")
        browse.Bind(wx.EVT_BUTTON, self.on_browse)
        row.Add(label, 0, wx.ALIGN_CENTER_VERTICAL | wx.RIGHT, 6)
        row.Add(self.path, 1, wx.RIGHT, 6)
        row.Add(browse, 0)
        outer.Add(row, 0, wx.EXPAND | wx.LEFT | wx.RIGHT, 10)

        buttons = wx.BoxSizer(wx.HORIZONTAL)
        install_btn = wx.Button(panel, label="&Install")
        uninstall_btn = wx.Button(panel, label="&Uninstall")
        close_btn = wx.Button(panel, label="E&xit")
        install_btn.Bind(wx.EVT_BUTTON, self.on_install)
        uninstall_btn.Bind(wx.EVT_BUTTON, self.on_uninstall)
        close_btn.Bind(wx.EVT_BUTTON, lambda e: self.Close())
        for b in (install_btn, uninstall_btn, close_btn):
            buttons.Add(b, 0, wx.RIGHT, 6)
        outer.Add(buttons, 0, wx.ALL, 10)

        log_label = wx.StaticText(panel, label="&Status:")
        outer.Add(log_label, 0, wx.LEFT | wx.RIGHT, 10)
        self.log_ctrl = wx.TextCtrl(panel, style=wx.TE_MULTILINE | wx.TE_READONLY,
                                    size=(560, 160))
        outer.Add(self.log_ctrl, 1, wx.EXPAND | wx.ALL, 10)

        panel.SetSizerAndFit(outer)
        self.Fit()
        install_btn.SetDefault()
        self.path.SetFocus()

    def log(self, message: str) -> None:
        self.log_ctrl.AppendText(message + "\n")

    def on_browse(self, _event) -> None:
        dlg = wx.DirDialog(self, "Choose the game's install folder",
                           defaultPath=self.path.GetValue() or "C:\\")
        if dlg.ShowModal() == wx.ID_OK:
            self.path.SetValue(dlg.GetPath())
        dlg.Destroy()

    def on_install(self, _event) -> None:
        self.run(install, "Wrath Access has been installed. Start the game to use it.")

    def on_uninstall(self, _event) -> None:
        self.run(uninstall, "Wrath Access has been uninstalled.")

    def run(self, action, success_message: str) -> None:
        try:
            action(self.path.GetValue().strip(), self.log)
        except Exception as e:  # show the reason, readable by the screen reader
            self.log("Error: " + str(e))
            wx.MessageBox(str(e), "Wrath Access Installer", wx.OK | wx.ICON_ERROR, self)
            return
        wx.MessageBox(success_message, "Wrath Access Installer",
                      wx.OK | wx.ICON_INFORMATION, self)


def main() -> None:
    app = wx.App()
    InstallerFrame().Show()
    app.MainLoop()


if __name__ == "__main__":
    main()
