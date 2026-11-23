# Made by Claude Ai & Relic Hunter
import json, os

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MPU = os.path.join(os.path.dirname(os.path.dirname(HERE)), "Magnetic Pick-UP", "MagneticPickUp-2.0.0", "MagneticPickUp", "relic.json")

demo_manifest = {
    "schema": 1, "id": "RelicDemo", "name": "Relic Demo Mod", "version": "1.0.0", "author": "Relic Hunter",
    "description": "Shows every setting type the Relic Mods window supports.",
    "groups": [{"id": "a", "label": "Every setting type"}],
    "settings": [
        {"key": "Mode", "type": "choice", "group": "a", "label": "Difficulty", "options": ["Casual", "Balanced", "Hardcore"], "default": "Balanced", "tooltip": "A dropdown."},
        {"key": "OpenKey", "type": "key", "group": "a", "label": "Open the demo window", "default": "Shift+F4", "personal": True, "tooltip": "Click, then press a key."},
        {"key": "PhotoKey", "type": "key", "group": "a", "label": "Clashing key", "default": "Shift+F4", "personal": True},
        {"key": "Glow", "type": "color", "group": "a", "label": "Glow colour", "default": "#c8102e", "tooltip": "Pick a colour or a swatch."},
        {"key": "Title", "type": "text", "group": "a", "label": "Your title", "default": "Relic Hunter"},
        {"key": "Gold", "type": "number", "group": "a", "label": "Starting gold", "default": 250, "min": 0, "max": 100000, "suffix": "coins"},
        {"key": "Ignore", "type": "list", "group": "a", "label": "Never pick up", "default": ["Bones", "Ashes"], "tooltip": "Rows you can add, remove and reorder."},
        {"key": "Pets", "type": "picker", "multi": True, "group": "a", "label": "Creatures to spawn", "default": ["wolf"],
         "options": [{"value": "wolf", "label": "Wolf"}, {"value": "boar", "label": "Wild Boar"}, {"value": "spider", "label": "Giant Spider"},
                     {"value": "goblin", "label": "Goblin"}, {"value": "golem", "label": "Earth Golem"}]},
    ],
    "presets": {"Casual": {"Mode": "Casual", "Gold": 1000}, "Hardcore": {"Mode": "Hardcore", "Gold": 0}},
}


def main():
    core = open(os.path.join(HERE, "lua", "RelicCore.relic.json"), encoding="utf-8").read()
    mpu = open(MPU, encoding="utf-8").read()
    snap = {
        "op": "snapshot",
        "core": {"version": "0.1.0", "fromGame": True, "phase": "menu", "dll": "files", "gameRunning": True, "hotkey": True},
        "mods": [
            {"folder": "RelicCore", "manifest": core, "settings": None, "report": True},
            {"folder": "MagneticPickUp", "manifest": mpu, "settings": json.dumps({"schema": 1, "values": {"PickupRadius": 5, "AutoPickup": True}}), "report": True},
            {"folder": "RelicDemo", "manifest": json.dumps(demo_manifest), "settings": None, "report": False},
        ],
        "coreSettings": json.dumps({"schema": 1, "presets": {}, "disabled": {}}),
    }
    with open(os.path.join(HERE, "src", "ui", "demo.json"), "w", encoding="utf-8") as f:
        json.dump(snap, f, indent=1)


if __name__ == "__main__":
    main()
