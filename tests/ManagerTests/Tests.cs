// Made by Claude Ai & Relic Hunter
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using RelicMods.Manager;

namespace RelicMods
{
    static partial class Program
    {
        public const string Version = "test";
        static int pass, fail;

        static void Check(string name, bool ok, string detail = "")
        {
            if (ok) pass++; else fail++;
            Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok ? "" : "  -> " + detail));
        }

        static string Rel(Placement p, string f) => p.Map.TryGetValue(f, out var r) ? r : "(none)";

        static string MakeZip(string dir, string name, Dictionary<string, string> files)
        {
            string path = Path.Combine(dir, name);
            if (File.Exists(path)) File.Delete(path);
            using (var z = ZipFile.Open(path, ZipArchiveMode.Create))
                foreach (var kv in files)
                    using (var w = new StreamWriter(z.CreateEntry(kv.Key).Open())) w.Write(kv.Value);
            return path;
        }

        static GameState NewGame(string root, string bin = "Win64")
        {
            var proj = Path.Combine(root, "RSDragonwilds", "RSDragonwilds");
            Directory.CreateDirectory(Path.Combine(proj, "Binaries", bin));
            Directory.CreateDirectory(Path.Combine(proj, "Content", "Paks"));
            File.WriteAllText(Path.Combine(proj, "Binaries", bin, "RSDragonwilds-" + bin + "-Shipping.exe"), "fake");
            var g = GameLocator.FromFolder(root);
            g.Storage = Path.Combine(root, "storage");
            var gs = new GameState { Game = g };
            gs.Profiles.Add(new Profile { Name = "Modded" });
            gs.Profiles.Add(new Profile { Name = "Vanilla" });
            return gs;
        }

        static void InstallZip(GameState gs, string zip, InstallInfo info = null)
        {
            info = info ?? ManagerService.GuessInfo(zip);
            string tmp = Path.Combine(Store.Home, "tmp", Guid.NewGuid().ToString("N"));
            Installer.Extract(zip, tmp);
            var plan = Installer.Plan(Installer.ListFiles(tmp), info.Name);
            if (plan.NeedsChoice) plan = Installer.PlanWithChoice(Installer.ListFiles(tmp), "ue4ss", info.Name);
            Installer.Install(gs, tmp, plan, info, zip);
            Directory.Delete(tmp, true);
        }

        static string T(GameState gs, string rel) => Path.Combine(gs.Game.Root, Deployer.MapRel(gs.Game, rel).Replace('/', '\\'));

        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--identify") return IdentifyDry(args[1]);
            if (args.Length == 3 && args[0] == "--crash")
            {
                var r = CrashReader.Analyze(new[] { args[1] }, args[2], null, f => new Suspect { Name = f, Folder = f });
                Console.WriteLine("CAUSE: " + r.Cause + " | DETAIL: " + r.Detail + " | LUA ERROR: " + r.LuaError + " | MODULE: " + r.Module);
                foreach (var x in r.Suspects) Console.WriteLine("SUSPECT: " + x.Name + " (" + x.Confidence + ") " + x.Why);
                foreach (var e in r.Evidence) Console.WriteLine("  " + e);
                return 0;
            }
            string root = Path.Combine(Path.GetTempPath(), "relicmods-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(root);
            Store.Home = Path.Combine(root, "home");
            try
            {
                Plans();
                Guesses();
                Flow(root);
                GamePass(root);
                Mo2(root);
                Others(root);
                SortAndClashes(root);
                Vortex(root);
                Crashes();
                Detect(root);
                V040(root);
                V050(root);
                V060(root);
            }
            catch (Exception ex) { Check("no crash", false, ex.ToString()); }
            try { Directory.Delete(root, true); } catch { }
            Console.WriteLine($"\n{pass}/{pass + fail} passed");
            return fail == 0 ? 0 : 1;
        }

        static void Plans()
        {
            var p = Installer.Plan(new[] { "MagneticPickUp/Scripts/main.lua", "MagneticPickUp/relic.json", "MagneticPickUp/README.txt" }, "MagneticPickUp-2.0.0.zip");
            Check("Lua mod folder goes to ue4ss/Mods", Rel(p, "MagneticPickUp/Scripts/main.lua") == "Binaries/Win64/ue4ss/Mods/MagneticPickUp/Scripts/main.lua" && !p.NeedsChoice, Rel(p, "MagneticPickUp/Scripts/main.lua"));

            p = Installer.Plan(new[] { "dwmapi.dll", "ue4ss/UE4SS.dll", "ue4ss/Mods/mods.txt", "ue4ss/Mods/Keybinds/Scripts/main.lua" }, "UE4SS_v4.0.0.zip");
            Check("UE4SS zip goes to Win64", Rel(p, "dwmapi.dll") == "Binaries/Win64/dwmapi.dll" && Rel(p, "ue4ss/UE4SS.dll") == "Binaries/Win64/ue4ss/UE4SS.dll" && Rel(p, "ue4ss/Mods/Keybinds/Scripts/main.lua") == "Binaries/Win64/ue4ss/Mods/Keybinds/Scripts/main.lua", string.Join(", ", p.Map.Values));
            Check("UE4SS kind", Installer.Kind(p.Map.Values) == "UE4SS" && Installer.Needs(p.Map.Values).Count == 0, Installer.Kind(p.Map.Values));

            p = Installer.Plan(new[] { "Binaries/Win64/ue4ss/Mods/X/Scripts/main.lua", "Content/Paks/~mods/X/X_P.pak" }, "x.zip");
            Check("MO2-style zip keeps its layout", Rel(p, "Binaries/Win64/ue4ss/Mods/X/Scripts/main.lua") == "Binaries/Win64/ue4ss/Mods/X/Scripts/main.lua" && Rel(p, "Content/Paks/~mods/X/X_P.pak") == "Content/Paks/~mods/X/X_P.pak");

            p = Installer.Plan(new[] { "RuneSchema/dlls/main.dll", "RuneSchema/Runeschema Files/dlls/main.dll", "RuneSchema/mods/runeschema.txt", "RuneSchema/Helpy/Helpy.exe" }, "RuneSchema 0.9.0.1.zip");
            Check("RuneSchema keeps nested folders inside it", Rel(p, "RuneSchema/Runeschema Files/dlls/main.dll") == "Binaries/Win64/ue4ss/Mods/RuneSchema/Runeschema Files/dlls/main.dll" && Rel(p, "RuneSchema/mods/runeschema.txt") == "Binaries/Win64/ue4ss/Mods/RuneSchema/mods/runeschema.txt", string.Join(", ", p.Map.Values));
            Check("RuneSchema kind", Installer.Kind(p.Map.Values).StartsWith("RuneSchema") && Installer.Needs(p.Map.Values).SequenceEqual(new[] { "ue4ss" }), Installer.Kind(p.Map.Values) + " / " + string.Join(",", Installer.Needs(p.Map.Values)));

            p = Installer.Plan(new[] { "ue4ss/Mods/MaxCape/Scripts/main.lua", "ue4ss/Mods/RuneSchema/mods/MaxCape/assets/10-MaxCape.json" }, "Max Cape.zip");
            Check("Lua + RuneSchema part mod", Rel(p, "ue4ss/Mods/RuneSchema/mods/MaxCape/assets/10-MaxCape.json") == "Binaries/Win64/ue4ss/Mods/RuneSchema/mods/MaxCape/assets/10-MaxCape.json");
            Check("it needs UE4SS and RuneSchema", Installer.Needs(p.Map.Values).SequenceEqual(new[] { "ue4ss", "runeschema" }), string.Join(",", Installer.Needs(p.Map.Values)));

            p = Installer.Plan(new[] { "RuneSchema/mods/Dye/assets/a.jsonc" }, "Dye.zip");
            Check("RuneSchema-only mod", Rel(p, "RuneSchema/mods/Dye/assets/a.jsonc") == "Binaries/Win64/ue4ss/Mods/RuneSchema/mods/Dye/assets/a.jsonc");

            p = Installer.Plan(new[] { "Better Spell Icons/zBetterSpellIcons_P.pak", "Better Spell Icons/zBetterSpellIcons_P.utoc", "Better Spell Icons/zBetterSpellIcons_P.ucas", "readme.txt" }, "Better Spell Icons.zip");
            Check("loose paks go to ~mods", Rel(p, "Better Spell Icons/zBetterSpellIcons_P.pak") == "Content/Paks/~mods/Better Spell Icons/zBetterSpellIcons_P.pak" && p.Extras.Contains("readme.txt") && !p.NeedsChoice, Rel(p, "Better Spell Icons/zBetterSpellIcons_P.pak"));

            p = Installer.Plan(new[] { "RSDragonwilds/RSDragonwilds/Content/Paks/~mods/Y/Y_P.pak" }, "y.zip");
            Check("full game path is trimmed", Rel(p, "RSDragonwilds/RSDragonwilds/Content/Paks/~mods/Y/Y_P.pak") == "Content/Paks/~mods/Y/Y_P.pak");

            p = Installer.Plan(new[] { "LogicMods/Cool.pak" }, "Cool.zip");
            Check("LogicMods paks", Rel(p, "LogicMods/Cool.pak") == "Content/Paks/LogicMods/Cool.pak" && Installer.Kind(p.Map.Values) == "LogicMod");

            p = Installer.Plan(new[] { "Scripts/main.lua", "enabled.txt" }, "Cool Mod");
            Check("bare Scripts folder uses the mod name", Rel(p, "Scripts/main.lua") == "Binaries/Win64/ue4ss/Mods/Cool_Mod/Scripts/main.lua", Rel(p, "Scripts/main.lua"));

            p = Installer.Plan(new[] { "stuff/thing.lua", "readme.txt" }, "Odd.zip");
            Check("unknown layout asks the player", p.NeedsChoice);
            var q = Installer.PlanWithChoice(new[] { "stuff/thing.lua", "stuff/x.txt" }, "ue4ss", "Odd");
            Check("the player's choice is used", Rel(q, "stuff/thing.lua") == "Binaries/Win64/ue4ss/Mods/Odd/thing.lua", Rel(q, "stuff/thing.lua"));
        }

        static void Guesses()
        {
            var i = ManagerService.GuessInfo(@"C:\dl\Magnetic Pick-UP-934-1-1-1-1763750000.zip");
            Check("Nexus file name gives mod id + version", i.Name == "Magnetic Pick-UP" && i.SiteModId == "934" && i.Version == "1.1.1" && i.Site == "nexus", i.Name + "|" + i.SiteModId + "|" + i.Version);
            i = ManagerService.GuessInfo(@"C:\dl\MagneticPickUp-2.0.0.zip");
            Check("plain file name gives name + version", i.Name == "MagneticPickUp" && i.Version == "2.0.0" && i.Site == "file", i.Name + "|" + i.Version);
        }

        static void Flow(string root)
        {
            string gameRoot = Path.Combine(root, "steam");
            var gs = NewGame(gameRoot);
            Check("game folder recognised", gs.Game != null && gs.Game.BinName == "Win64" && gs.Game.Root.EndsWith(@"RSDragonwilds\RSDragonwilds"));
            string pre = T(gs, "Binaries/Win64/dwmapi.dll");
            File.WriteAllText(pre, "old dwmapi from a manual install");
            string zips = Path.Combine(root, "zips");
            Directory.CreateDirectory(zips);

            InstallZip(gs, MakeZip(zips, "UE4SS_v4.0.0.zip", new Dictionary<string, string> { { "dwmapi.dll", "proxy" }, { "ue4ss/UE4SS.dll", "ue4ss" }, { "ue4ss/Mods/mods.txt", "Keybinds : 1" } }));
            InstallZip(gs, MakeZip(zips, "Alpha-1.0.zip", new Dictionary<string, string> { { "Alpha/Scripts/main.lua", "alpha" }, { "Alpha/config.txt", "Speed = 1" }, { "Shared/Scripts/main.lua", "from alpha" } }));
            InstallZip(gs, MakeZip(zips, "Beta-2.0.zip", new Dictionary<string, string> { { "Beta/Scripts/main.lua", "beta" }, { "Shared/Scripts/main.lua", "from beta" } }));
            Check("three mods installed, all on in Modded", gs.Mods.Count == 3 && gs.Active.Enabled.Count == 3, gs.Mods.Count + " / " + gs.Active.Enabled.Count);
            Check("Vanilla profile stays empty", gs.Profiles.First(p => p.Name == "Vanilla").Enabled.Count == 0);

            var r = Deployer.Deploy(gs);
            Check("deploy links every file", r.Problems.Count == 0 && r.Linked >= 7, r.Summary() + " " + string.Join("; ", r.Problems));
            Check("old dwmapi set aside", r.BackedUp == 1 && File.ReadAllText(pre) == "proxy");
            string lua = T(gs, "Binaries/Win64/ue4ss/Mods/Alpha/Scripts/main.lua");
            Check("files are hard links to storage", Deployer.FileId(lua) == Deployer.FileId(Path.Combine(gs.Game.Storage, "mods", gs.Mods[1].Key, @"Binaries\Win64\ue4ss\Mods\Alpha\Scripts\main.lua")));
            string shared = T(gs, "Binaries/Win64/ue4ss/Mods/Shared/Scripts/main.lua");
            Check("conflict: later mod wins", File.ReadAllText(shared) == "from beta" && r.Conflicts.Count == 1, File.ReadAllText(shared));

            var p0 = gs.Active;
            int bi = p0.Order.IndexOf(gs.Mods[2].Key);
            p0.Order.RemoveAt(bi); p0.Order.Insert(1, gs.Mods[2].Key);
            Deployer.Deploy(gs);
            Check("load order change flips the winner", File.ReadAllText(shared) == "from alpha", File.ReadAllText(shared));

            File.WriteAllText(T(gs, "Binaries/Win64/ue4ss/Mods/Alpha/config.txt"), "Speed = 9");
            InstallZip(gs, MakeZip(zips, "Alpha-1.1.zip", new Dictionary<string, string> { { "Alpha/Scripts/main.lua", "alpha 1.1" }, { "Alpha/config.txt", "Speed = 1" }, { "Shared/Scripts/main.lua", "from alpha" } }));
            Check("update replaces, does not duplicate", gs.Mods.Count == 3 && gs.Mods.Any(m => m.Version == "1.1"));
            r = Deployer.Deploy(gs);
            Check("update keeps the player's config", File.ReadAllText(T(gs, "Binaries/Win64/ue4ss/Mods/Alpha/config.txt")) == "Speed = 9" && File.ReadAllText(lua) == "alpha 1.1", File.ReadAllText(T(gs, "Binaries/Win64/ue4ss/Mods/Alpha/config.txt")) + " | " + File.ReadAllText(lua) + " | " + r.Summary());

            int before = gs.Mods.Count;
            InstallZip(gs, MakeZip(zips, "Alpha Renamed-77-1-2-1763700000.zip", new Dictionary<string, string> { { "Alpha/Scripts/main.lua", "alpha 1.2" }, { "Shared/Scripts/main.lua", "from alpha" } }));
            Check("same mod from another source updates it", gs.Mods.Count == before && gs.Mods.Any(m => m.SiteModId == "77" && m.Version == "1.2"), gs.Mods.Count + " mods");

            gs.Active.Enabled.Remove(gs.Mods[2].Key);
            Deployer.Deploy(gs);
            Check("turning a mod off removes its files", !File.Exists(T(gs, "Binaries/Win64/ue4ss/Mods/Beta/Scripts/main.lua")) && !Directory.Exists(T(gs, "Binaries/Win64/ue4ss/Mods/Beta")));

            string rsZip = MakeZip(zips, "RuneSchema-570-0-9-0-1-1763700000.zip", new Dictionary<string, string> { { "RuneSchema/dlls/main.dll", "rs" }, { "RuneSchema/mods/runeschema.txt", "HuntersRequest : 1" }, { "RuneSchema/mods/HuntersRequest/npc/a.jsonc", "{}" } });
            InstallZip(gs, rsZip);
            InstallZip(gs, MakeZip(zips, "Max Cape.zip", new Dictionary<string, string> { { "ue4ss/Mods/MaxCape/Scripts/main.lua", "cape" }, { "ue4ss/Mods/RuneSchema/mods/MaxCape/assets/a.json", "{}" } }));
            Deployer.Deploy(gs);
            string rsList = File.ReadAllText(T(gs, Deployer.RuneSchemaList));
            Check("runeschema.txt lists RuneSchema mods", rsList.Contains("HuntersRequest : 1") && rsList.Contains("MaxCape : 1"), rsList);
            gs.Active.Enabled.Remove(gs.Mods.First(m => m.Name == "Max Cape").Key);
            Deployer.Deploy(gs);
            rsList = File.ReadAllText(T(gs, Deployer.RuneSchemaList));
            Check("turning a RuneSchema mod off removes its line", !rsList.Contains("MaxCape") && rsList.Contains("HuntersRequest"), rsList);

            string strayDir = T(gs, "Binaries/Win64/ue4ss/Mods/OldManual");
            Directory.CreateDirectory(strayDir);
            File.WriteAllText(Path.Combine(strayDir, "main.lua"), "stray");
            int moved = Deployer.CleanGameFolder(gs);
            Check("clean moves only unmanaged files", moved == 1 && !File.Exists(Path.Combine(strayDir, "main.lua")) && File.Exists(lua), "moved " + moved);

            gs.Game.ActiveProfile = "Vanilla";
            r = Deployer.Deploy(gs);
            bool clean = !Directory.Exists(T(gs, "Binaries/Win64/ue4ss")) && !Directory.Exists(T(gs, "Content/Paks/~mods"));
            Check("Vanilla profile leaves a clean game", clean && gs.Deployed.Count == 0, r.Summary() + " deployed=" + gs.Deployed.Count);
            Check("Vanilla puts the old game file back", File.Exists(pre) && File.ReadAllText(pre) == "old dwmapi from a manual install" && r.Restored == 1);
            Check("game exe untouched", File.Exists(T(gs, "Binaries/Win64/RSDragonwilds-Win64-Shipping.exe")));

            gs.Game.ActiveProfile = "Modded";
            r = Deployer.Deploy(gs);
            Check("back to Modded relinks", File.Exists(lua) && File.ReadAllText(pre) == "proxy" && r.Problems.Count == 0, r.Summary());

            File.Delete(lua);
            File.WriteAllText(lua, "edited by hand");
            gs.Game.ActiveProfile = "Vanilla";
            r = Deployer.Deploy(gs);
            Check("a file replaced by hand is left alone", File.Exists(lua) && File.ReadAllText(lua) == "edited by hand" && r.Left == 1, r.Summary());

            Store.Save(new AppState { ActiveGame = gs.Game.Id, Games = new List<GameState> { gs } });
            var back = Store.Load();
            Check("state saves and loads", back.Current != null && back.Current.Mods.Count == gs.Mods.Count && back.Current.Profiles.Count == 2 && back.Current.Game.Root == gs.Game.Root);
        }

        static void GamePass(string root)
        {
            var gs = NewGame(Path.Combine(root, "xbox"), "WinGDK");
            Check("Game Pass build found", gs.Game.BinName == "WinGDK" && gs.Game.Kind == "gamepass", gs.Game.BinName + " " + gs.Game.Kind);
            string zips = Path.Combine(root, "zips2");
            Directory.CreateDirectory(zips);
            InstallZip(gs, MakeZip(zips, "Gamma.zip", new Dictionary<string, string> { { "Gamma/Scripts/main.lua", "g" } }));
            Deployer.Deploy(gs);
            Check("Game Pass files go to WinGDK", File.Exists(Path.Combine(gs.Game.Root, @"Binaries\WinGDK\ue4ss\Mods\Gamma\Scripts\main.lua")));
        }

        static void Mo2(string root)
        {
            string mo2 = Path.Combine(root, "MO2");
            Directory.CreateDirectory(Path.Combine(mo2, "profiles", "Default"));
            File.WriteAllText(Path.Combine(mo2, "ModOrganizer.ini"), "[General]\r\nselected_profile=@ByteArray(Default)\r\n");
            File.WriteAllText(Path.Combine(mo2, "profiles", "Default", "modlist.txt"), "# list\r\n+Top Mod\r\n-Off Mod\r\n+Stuff_separator\r\n+Bottom Mod\r\n");
            void Mod(string name, string rel, string meta = null)
            {
                string f = Path.Combine(mo2, "mods", name, rel.Replace('/', '\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllText(f, name);
                if (meta != null) File.WriteAllText(Path.Combine(mo2, "mods", name, "meta.ini"), meta);
            }
            Mod("Top Mod", "Binaries/Win64/ue4ss/Mods/Same/Scripts/main.lua", "[General]\r\nmodid=934\r\nversion=1.1.1\r\n");
            Mod("Bottom Mod", "Binaries/Win64/ue4ss/Mods/Same/Scripts/main.lua");
            Mod("Off Mod", "Content/Paks/~mods/Off/Off_P.pak");
            var gs = NewGame(Path.Combine(root, "mo2game"));
            var r = Importer.ImportMo2(gs, mo2);
            Check("MO2 mods imported", r.Mods == 3 && r.Enabled == 2, r.Mods + " mods, " + r.Enabled + " on, " + string.Join("; ", r.Problems));
            var top = gs.Mods.FirstOrDefault(m => m.Name == "Top Mod");
            Check("MO2 Nexus id + version kept", top != null && top.Site == "nexus" && top.SiteModId == "934" && top.Version == "1.1.1");
            Check("MO2 off mod stays off", !gs.Active.Enabled.Contains(gs.Mods.First(m => m.Name == "Off Mod").Key));
            Deployer.Deploy(gs);
            Check("MO2 priority kept (top of list wins)", File.ReadAllText(Path.Combine(gs.Game.Root, @"Binaries\Win64\ue4ss\Mods\Same\Scripts\main.lua")) == "Top Mod");
        }
    }
}
