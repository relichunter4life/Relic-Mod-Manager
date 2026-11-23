# Made by Claude Ai & Relic Hunter
import os, shutil, subprocess, sys, zipfile

VERSION = "0.7.1"
HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BUILD = os.path.join(HERE, "build")
PKG = os.path.join(BUILD, "pkg", "RelicCore")
STANDALONE = os.path.join(BUILD, "standalone", "Relic Mod Manager")
APP_OUT = os.path.join(BUILD, "app")
SKIP_APP = {"Microsoft.Web.WebView2.Wpf.dll"}
SKIP_UI = {"demo.json"}

WARNING = """Windows may say "Windows protected your PC" the first time.
Relic Mod Manager is a free community project, and we can't afford the
$200-400 a year a code-signing certificate costs, so Windows doesn't
know us yet. Click "More info", then "Run anyway".
"""

README = """Made by Claude Ai & Relic Hunter

RELIC MOD MANAGER {v} - RuneScape: Dragonwilds

What it does
- A simple mod manager made only for RuneScape: Dragonwilds: drag and drop a mod file
  on Install Mods and every file goes to the right game folder.
- Every Relic mod's settings in one place, next to the game.
- Press Shift+F7 in game to open or hide it. Esc hides it too.
- Opens by itself at the main menu and tucks away when a world loads (both can be turned off).
- Changes apply in game right away.

Needs
- UE4SS v4.0.0 (Nexus mod 935).
- Microsoft Edge WebView2 Runtime (already on Windows 10 and 11).

Install
1. Relic Mod Manager: drop this zip on its Install Mods page.
2. By hand: copy the RelicCore folder into
   RSDragonwilds\\Binaries\\Win64\\ue4ss\\Mods\\

Where things are
- Your settings: RelicCore\\settings\\<Mod>.json (kept when mods update).
- report.txt in the RelicCore folder shows the window and game state.
- Console command: reliccore (writes report.txt).

""" + WARNING

APP_README = """Made by Claude Ai & Relic Hunter

RELIC MOD MANAGER {v} - mod manager for RuneScape: Dragonwilds

Start
- Unzip the "Relic Mod Manager" folder anywhere (for example your Documents) and run RelicModManager.exe.

""" + WARNING + """
What it does
- Finds your Steam or Game Pass copy of the game.
- Install Mods: drag and drop .zip / .7z / .rar mod files and they install in the right place.
- Library: turn mods on or off with one switch, load order, Auto-Sort, notes.
- Nexus Mods: browse Dragonwilds mods, newest first; they open in your own browser.
- Profiles: "Modded" and "Vanilla" (no mods at all), or make your own.
- Check for updates: compares your Nexus mods with Nexus.
- Coming from Mod Organizer 2? Games & Profiles > Import from MO2.

How mods reach the game
- Mods are kept in a storage folder (shown on the Games & Profiles page) and linked into the game folder. Turning a mod off removes its links; your mod files stay safe in storage.
- Close the game before changing mods.

Your data
- %LOCALAPPDATA%\\RelicMods (list of mods, profiles, downloads).
"""


def run(cmd, cwd):
    print("> " + " ".join(cmd))
    r = subprocess.run(cmd, cwd=cwd)
    if r.returncode != 0:
        sys.exit("command failed")


def copy_app(dest):
    os.makedirs(dest, exist_ok=True)
    for f in os.listdir(APP_OUT):
        src = os.path.join(APP_OUT, f)
        if os.path.isfile(src) and f not in SKIP_APP and (f.endswith(".dll") or f.endswith(".exe") or f.endswith(".config")):
            shutil.copy2(src, os.path.join(dest, f))
    shutil.copytree(os.path.join(HERE, "src", "ui"), os.path.join(dest, "ui"),
                    ignore=lambda d, names: [n for n in names if n in SKIP_UI])


def zip_dir(folder, zp):
    if os.path.exists(zp):
        os.remove(zp)
    root = os.path.dirname(folder)
    with zipfile.ZipFile(zp, "w", zipfile.ZIP_DEFLATED) as z:
        for d, _, files in os.walk(folder):
            for f in sorted(files):
                full = os.path.join(d, f)
                z.write(full, os.path.relpath(full, root).replace("\\", "/"))
    print("built " + zp + " (%d KB)" % (os.path.getsize(zp) // 1024))


def main():
    run(["dotnet", "build", "-c", "Release", "-o", APP_OUT], os.path.join(HERE, "src", "app"))
    for d in (os.path.join(BUILD, "pkg"), os.path.join(BUILD, "standalone")):
        if os.path.isdir(d):
            shutil.rmtree(d)
    os.makedirs(os.path.join(PKG, "Scripts"))
    for f in ("main.lua", "json.lua", "relic.lua"):
        shutil.copy2(os.path.join(HERE, "lua", f), os.path.join(PKG, "Scripts", f))
    shutil.copy2(os.path.join(HERE, "lua", "RelicCore.relic.json"), os.path.join(PKG, "relic.json"))
    copy_app(os.path.join(PKG, "App"))
    for sub, text in (("settings", "Relic Mod Manager saves your Relic mod settings here.\n"),
                      ("bridge", "The game and the Relic Mod Manager window talk through these files.\n")):
        os.makedirs(os.path.join(PKG, sub))
        with open(os.path.join(PKG, sub, "README.txt"), "w", encoding="utf-8") as fh:
            fh.write(text)
    open(os.path.join(PKG, "enabled.txt"), "w").close()
    with open(os.path.join(PKG, "README.txt"), "w", encoding="utf-8") as fh:
        fh.write(README.format(v=VERSION))
    zip_dir(PKG, os.path.join(HERE, "RelicModManager-%s.zip" % VERSION))

    copy_app(STANDALONE)
    with open(os.path.join(STANDALONE, "README.txt"), "w", encoding="utf-8") as fh:
        fh.write(APP_README.format(v=VERSION))
    zip_dir(STANDALONE, os.path.join(HERE, "RelicModManager-Standalone-%s.zip" % VERSION))


if __name__ == "__main__":
    main()
