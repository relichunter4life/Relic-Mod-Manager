# Made by Claude Ai & Relic Hunter
import json, os, shutil, sys, tempfile
from lupa.lua54 import LuaRuntime

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PKG = os.path.join(HERE, "build", "pkg", "RelicCore")
MPU = os.path.join(os.path.dirname(os.path.dirname(HERE)), "Magnetic Pick-UP", "MagneticPickUp-2.0.0", "MagneticPickUp")

SHARED = {}
RESULTS = []


def check(name, ok, detail=""):
    RESULTS.append((name, bool(ok)))
    print(("PASS " if ok else "FAIL ") + name + ("" if ok else "  -> " + str(detail)))


STUBS = r"""
local py = ...
T = 0
os.clock = function() return T end
EXEC = {}
os.execute = function(cmd) EXEC[#EXEC + 1] = cmd; return true end
LOOPS, NOTIFY, CONSOLE = {}, {}, {}
function print(...) end
ModRef = {
  SetSharedVariable = function(self, k, v) py.set(k, v) end,
  GetSharedVariable = function(self, k) return py.get(k) end,
}
function LoopInGameThreadWithDelay(ms, fn) LOOPS[#LOOPS + 1] = fn; return #LOOPS end
function LoopAsync(ms, fn) LOOPS[#LOOPS + 1] = fn end
function ExecuteInGameThread(fn) fn() end
function ExecuteWithDelay(ms, fn) end
function NotifyOnNewObject(cls, fn) NOTIFY[cls] = fn end
function RegisterConsoleCommandHandler(name, fn) CONSOLE[name] = fn end
function RegisterHook() return 0, 0 end
function StaticFindObject() return nil end
function FindAllOf() return {} end
function FindFirstOf() return nil end
function run_loops(n) for _ = 1, n or 1 do for _, fn in ipairs(LOOPS) do fn() end end end
function fake_pc(name, pawn)
  return { IsValid = function() return true end, IsLocalController = function() return true end,
    GetFullName = function() return name end, Pawn = pawn }
end
"""


class Py:
    @staticmethod
    def set(k, v):
        SHARED[k] = v

    @staticmethod
    def get(k):
        return SHARED.get(k)


def runtime():
    lua = LuaRuntime(unpack_returned_tuples=True)
    lua.execute(STUBS, Py)
    return lua


def lua_path(p):
    return p.replace("\\", "/")


def read(p):
    with open(p, encoding="utf-8") as f:
        return f.read()


def main():
    root = tempfile.mkdtemp(prefix="reliccore-test-")
    mods = os.path.join(root, "ue4ss", "Mods")
    os.makedirs(mods)
    core = os.path.join(mods, "RelicCore")
    shutil.copytree(PKG, core)
    mpu = os.path.join(mods, "MagneticPickUp")
    shutil.copytree(MPU, mpu)
    with open(os.path.join(mpu, "config.txt"), "w") as f:
        f.write("# old\nEnabled = true\nAutoPickup = false\nPickupRadius = 5   # metres\nPickEvery = 0.2\nVerbose = true\nBogus = 9\n")

    # json.lua
    lua = runtime()
    J = lua.eval('dofile("%s")' % lua_path(os.path.join(core, "Scripts", "json.lua")))
    src = '{"a":[1,2.5,{"b":"x\\"y\\u00e9"}],"t":true,"n":null,"e":[],"o":{}}'
    back = json.loads(J.encode(J.decode(src)))
    check("json round trip", back["a"][0] == 1 and back["a"][1] == 2.5 and back["a"][2]["b"] == 'x"yé' and back["t"] is True and "n" not in back and back["e"] == [] , back)
    bad = lua.eval('function(J) return pcall(J.decode, "{bad") end')(J)
    check("json rejects bad text", bad[0] is False if isinstance(bad, tuple) else bad is False, bad)

    # Relic Core main.lua
    core_lua = runtime()
    core_lua.execute('dofile("%s")' % lua_path(os.path.join(core, "Scripts", "main.lua")))
    game = json.loads(read(os.path.join(core, "bridge", "game.json")))
    check("core writes game.json at start", game["phase"] == "boot" and game["core"] == "0.7.1", game)
    check("core shares its version", SHARED.get("RelicCore.version") == "0.7.1", SHARED)
    check("core writes report.txt", "Relic Mod Manager 0.7.1 report" in read(os.path.join(core, "report.txt")))
    check("core watches player controllers", core_lua.eval('NOTIFY["/Script/Engine.PlayerController"] ~= nil'))

    core_lua.execute('T = 1; NOTIFY["/Script/Engine.PlayerController"](fake_pc("BP_MainMenuPlayerController_C /Game/Maps/MainMenu.MainMenu:PC", nil)); run_loops(1)')
    game = json.loads(read(os.path.join(core, "bridge", "game.json")))
    check("main menu detected", game["phase"] == "menu", game)
    cmd = core_lua.eval("EXEC[1]")
    check("window started once at the main menu", cmd and "RelicModManager.exe" in cmd and "--from-game" in cmd and core_lua.eval("#EXEC") == 1, cmd)

    core_lua.execute('T = 2; NOTIFY["/Script/Engine.PlayerController"](fake_pc("BP_PlayerController_C /Game/Maps/World.World:PC", {IsValid = function() return true end})); run_loops(1)')
    game = json.loads(read(os.path.join(core, "bridge", "game.json")))
    check("world detected", game["phase"] == "world" and SHARED.get("RelicCore.phase") == "world", game)
    core_lua.execute('T = 3; NOTIFY["/Script/Engine.PlayerController"](fake_pc("BP_MainMenuPlayerController_C /Game/Maps/MainMenu.MainMenu:PC2", nil)); run_loops(1)')
    check("window not started twice", core_lua.eval("#EXEC") == 1)

    with open(os.path.join(core, "bridge", "rev.txt"), "w") as f:
        f.write("3")
    core_lua.execute("T = 4; run_loops(1)")
    check("core passes the settings revision on", SHARED.get("RelicCore.rev") == "3", SHARED.get("RelicCore.rev"))

    # Other Relics live bridge (Mod Menu's shared-variable format)
    def bridge(mods, actions, rev):
        with open(os.path.join(core, "bridge", "modmenu.json"), "w") as f:
            json.dump({"n": len(actions), "mods": mods, "actions": actions}, f)
        with open(os.path.join(core, "bridge", "rev.txt"), "w") as f:
            f.write(rev)
    bridge({"HpNums": {"rev": 1, "set": {"Enabled": False, "Scale": 2.5, "OpenKey": "F9"}, "skip": ["OpenKey"]}}, [{"id": "HpNums", "a": "go", "n": 1}], "4")
    core_lua.execute("T = 5; run_loops(1)")
    check("Other Relics values published without Mod Menu", SHARED.get("ModMenu.HpNums.Enabled") is False and SHARED.get("ModMenu.HpNums.Scale") == 2.5 and "ModMenu.HpNums.OpenKey" not in SHARED, {k: v for k, v in SHARED.items() if k.startswith("ModMenu")})
    check("Other Relics revision goes up", isinstance(SHARED.get("ModMenu.HpNums.rev"), (int, float)) and SHARED.get("ModMenu.HpNums.rev") > 1000, SHARED.get("ModMenu.HpNums.rev"))
    check("Other Relics button reaches the mod", SHARED.get("ModMenu.HpNums.action") == "go#1", SHARED.get("ModMenu.HpNums.action"))
    SHARED["ModMenu.version"] = "2.0.3"
    bridge({"HpNums": {"rev": 2, "set": {"Enabled": True, "Mode": "B"}, "skip": []}, "Other": {"rev": 1, "set": {"X": 3}, "skip": []}}, [{"id": "HpNums", "a": "go", "n": 1}], "5")
    core_lua.execute("T = 6; run_loops(1)")
    req = SHARED.get("ModMenu.request.set") or ""
    check("with Mod Menu loaded, changes go through its request channel", (req.startswith("HpNums#") and "|Enabled=true" in req and "|Mode=B" in req) or req.startswith("Other#"), req)
    core_lua.execute("T = 6.1; run_loops(1)")
    check("requests are spaced out", (SHARED.get("ModMenu.request.set") or "") == req, SHARED.get("ModMenu.request.set"))
    core_lua.execute("T = 6.5; run_loops(1)")
    req2 = SHARED.get("ModMenu.request.set") or ""
    check("second mod's request follows", req2 != req and (req2.startswith("Other#") or req2.startswith("HpNums#")) and ("|X=3" in req2 or "|Enabled=true" in req2), req2)
    check("same button press isn't sent twice", SHARED.get("ModMenu.HpNums.action") == "go#1")
    del SHARED["ModMenu.version"]

    # Magnetic Pick-UP 2.0.0 with Relic Core
    m = runtime()
    m.execute('dofile("%s")' % lua_path(os.path.join(mpu, "Scripts", "main.lua")))
    sf = os.path.join(core, "settings", "MagneticPickUp.json")
    check("old config.txt imported into Relic Core", os.path.isfile(sf))
    data = json.loads(read(sf)) if os.path.isfile(sf) else {}
    v = data.get("values", {})
    check("imported values are right", v.get("AutoPickup") is False and v.get("PickupRadius") == 5 and v.get("PickEvery") == 0.2 and v.get("Verbose") is True and "Bogus" not in v, v)

    def report():
        m.execute('CONSOLE["magneticpickup"]()')
        return read(os.path.join(mpu, "report.txt"))

    rep = report()
    check("report names Relic Core as the settings source", "settings: Relic Mod Manager settings" in rep and "imported" in rep, rep)
    check("report shows imported reach", "reach 5.0 m" in rep and "Auto-Pickup Ground Items off" in rep, rep)

    data["values"]["PickupRadius"] = 7.5
    data["values"]["AutoPickup"] = True
    with open(sf, "w") as f:
        json.dump(data, f)
    m.execute("T = 10; run_loops(1)")
    SHARED["RelicCore.rev"] = "4"
    m.execute("T = 11; run_loops(1)")
    rep = report()
    check("live change applied without restart", "reach 7.5 m" in rep and "Auto-Pickup Ground Items ON" in rep, rep)

    data["values"]["PickupRadius"] = 99
    with open(sf, "w") as f:
        json.dump(data, f)
    SHARED["RelicCore.rev"] = "5"
    m.execute("T = 12; run_loops(1)")
    check("out-of-range value is clamped", "reach 10.0 m" in report())

    with open(os.path.join(core, "settings", "_core.json"), "w") as f:
        json.dump({"schema": 1, "disabled": {"MagneticPickUp": True}}, f)
    SHARED["RelicCore.rev"] = "6"
    m.execute("T = 13; run_loops(1)")
    rep = report()
    check("turning the mod off in Relic Mods works", "settings: mod OFF" in rep and "TURNED OFF" in rep, rep)
    with open(os.path.join(core, "settings", "_core.json"), "w") as f:
        json.dump({"schema": 1, "disabled": {}}, f)
    SHARED["RelicCore.rev"] = "6b"
    m.execute("T = 14; run_loops(1)")
    rep = report()
    check("turning it back on works", "settings: mod on" in rep and "TURNED OFF" not in rep, rep)

    # Without the Core running (no shared revision): falls back to reading rev.txt
    SHARED.clear()
    os.remove(os.path.join(core, "settings", "_core.json"))
    m2 = runtime()
    m2.execute('dofile("%s")' % lua_path(os.path.join(mpu, "Scripts", "main.lua")))
    with open(os.path.join(core, "bridge", "rev.txt"), "w") as f:
        f.write("3")
    m2.execute("T = 20; run_loops(1)")
    data["values"]["PickupRadius"] = 2
    with open(sf, "w") as f:
        json.dump(data, f)
    with open(os.path.join(core, "bridge", "rev.txt"), "w") as f:
        f.write("7")
    m2.execute("T = 22; run_loops(1)")
    m2.execute('CONSOLE["magneticpickup"]()')
    rep = read(os.path.join(mpu, "report.txt"))
    check("live change works even if the Core mod is off", "reach 2.0 m" in rep and "not running" in rep, rep)

    # Without Relic Core installed
    shutil.rmtree(core)
    SHARED.clear()
    m3 = runtime()
    m3.execute('dofile("%s")' % lua_path(os.path.join(mpu, "Scripts", "main.lua")))
    m3.execute('CONSOLE["magneticpickup"]()')
    rep = read(os.path.join(mpu, "report.txt"))
    check("without Relic Core it uses config.txt", "Relic Core not installed" in rep and "reach 5.0 m" in rep, rep)

    shutil.rmtree(root, ignore_errors=True)
    fails = [n for n, ok in RESULTS if not ok]
    print("\n%d/%d passed" % (len(RESULTS) - len(fails), len(RESULTS)))
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
