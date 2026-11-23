// Made by Claude Ai & Relic Hunter
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RelicMods.Manager;

namespace RelicMods
{
    static partial class Program
    {
        static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
        }

        static void Others(string root)
        {
            string mods = Path.Combine(root, "omods");
            Write(Path.Combine(mods, "HpNums", "modmenu.json"), "{\"name\":\"HP Numbers\",\"author\":\"Maxxfilth\",\"settings\":[{\"type\":\"header\",\"label\":\"Look\"},{\"key\":\"Enabled\",\"type\":\"toggle\",\"default\":true},{\"key\":\"Scale\",\"type\":\"slider\",\"min\":0.5,\"max\":3,\"step\":0.1,\"default\":1},{\"key\":\"Mode\",\"type\":\"choice\",\"options\":[\"A\",\"B\"],\"default\":\"A\"},{\"key\":\"OpenKey\",\"type\":\"key\",\"default\":\"F8\"},{\"type\":\"button\",\"label\":\"Go\",\"action\":\"go\"}]}");
            Write(Path.Combine(mods, "HpNums", "config.txt"), "# HP numbers\r\nEnabled = true   # on/off\r\nScale = 1.5\r\nOpenKey = F8\r\n");
            Write(Path.Combine(mods, "HpNums", "enabled.txt"), "");
            Write(Path.Combine(mods, "OurOld", "modmenu.txt"), "{\"name\":\"Our Old\",\"author\":\"The Relic Hunter\",\"config\":{\"file\":\"settings.json\",\"format\":\"json\"},\"settings\":[{\"key\":\"Speed\",\"type\":\"number\",\"default\":2}]}");
            Write(Path.Combine(mods, "OurOld", "settings.json"), "{\"Speed\":3,\"Other\":\"x\"}");
            Write(Path.Combine(mods, "LuaVals", "modmenu.json"), "{\"name\":\"Lua Vals\",\"author\":\"Someone\",\"config\":{\"format\":\"lua\"},\"settings\":[{\"key\":\"MaxStack\",\"type\":\"number\",\"default\":99},{\"key\":\"Title\",\"type\":\"text\",\"default\":\"Hi\"},{\"key\":\"Radius\",\"type\":\"slider\",\"default\":1}]}");
            Write(Path.Combine(mods, "LuaVals", "Scripts", "main.lua"), "local MaxStack = 999\nlocal Title = \"Hello\"\nlocal config = {\n    Radius = 12.5,\n}\nprint(MaxStack)\n");
            Write(Path.Combine(mods, "Relicky", "relic.json"), "{}");
            Write(Path.Combine(mods, "Relicky", "modmenu.json"), "{\"name\":\"skip me\"}");
            Write(Path.Combine(mods, "Escape", "modmenu.json"), "{\"name\":\"Escape\",\"config\":{\"file\":\"../../evil.txt\"},\"settings\":[{\"key\":\"A\",\"type\":\"toggle\"}]}");

            var list = OtherRelics.Scan(mods);
            Check("Other Relics finds modmenu mods, skips relic.json mods", list.Count == 4 && !list.Any(m => m.Folder == "Relicky"), string.Join(",", list.Select(m => m.Folder)));
            var hp = list.First(m => m.Folder == "HpNums");
            Check("modmenu author decides ours/others", !hp.Ours && list.First(m => m.Folder == "OurOld").Ours);
            Check("config.txt values read (comment stripped)", hp.Values["Enabled"] == "true" && hp.Values["Scale"] == "1.5" && !hp.Values.ContainsKey("Mode"), string.Join(";", hp.Values.Select(kv => kv.Key + "=" + kv.Value)));
            OtherRelics.WriteValues(Path.Combine(mods, "HpNums", "config.txt"), "kv", new Dictionary<string, object> { { "Enabled", false }, { "Scale", 2.25 }, { "Mode", "B" } });
            string cfg = File.ReadAllText(Path.Combine(mods, "HpNums", "config.txt"));
            Check("config.txt write keeps comments and line endings", cfg.Contains("# HP numbers\r\n") && cfg.Contains("Enabled = false   # on/off\r\n") && cfg.Contains("Scale = 2.25\r\n") && cfg.Contains("Mode = B\r\n"), cfg);
            var old = list.First(m => m.Folder == "OurOld");
            Check("modmenu.txt + json config read", old.Values.TryGetValue("Speed", out var sp) && sp == "3", string.Join(";", old.Values.Select(kv => kv.Key + "=" + kv.Value)));
            OtherRelics.WriteValues(Path.Combine(mods, "OurOld", "settings.json"), "json", new Dictionary<string, object> { { "Speed", 5 } });
            string js = File.ReadAllText(Path.Combine(mods, "OurOld", "settings.json"));
            Check("json config write keeps other keys", js.Contains("\"Speed\":5") && js.Contains("\"Other\":\"x\""), js);
            var lv = list.First(m => m.Folder == "LuaVals");
            Check("lua values read", lv.Values["MaxStack"] == "999" && lv.Values["Title"] == "Hello" && lv.Values["Radius"] == "12.5", string.Join(";", lv.Values.Select(kv => kv.Key + "=" + kv.Value)));
            OtherRelics.WriteValues(Path.Combine(mods, "LuaVals", "Scripts", "main.lua"), "lua", new Dictionary<string, object> { { "MaxStack", 50 }, { "Title", "Yo \"there\"" }, { "Radius", 3 } });
            string lua = File.ReadAllText(Path.Combine(mods, "LuaVals", "Scripts", "main.lua"));
            Check("lua values written in place", lua.Contains("local MaxStack = 50\n") && lua.Contains("local Title = \"Yo \\\"there\\\"\"") && lua.Contains("    Radius = 3,") && lua.Contains("print(MaxStack)"), lua);
            var esc = list.First(m => m.Folder == "Escape");
            Check("config outside the mod folder refused", esc.Error != null && !File.Exists(Path.Combine(root, "evil.txt")), esc.Error ?? "(no error)");
        }

        static void SortAndClashes(string root)
        {
            var gs = NewGame(Path.Combine(root, "sortgame"));
            string zips = Path.Combine(root, "sortzips");
            Directory.CreateDirectory(zips);
            InstallZip(gs, MakeZip(zips, "Big Mod-1.0.zip", new Dictionary<string, string> {
                { "BigMod/Scripts/main.lua", "big" }, { "BigMod/Scripts/data.lua", "data" }, { "BigMod/Scripts/ui.lua", "ui" }, { "BigMod/Scripts/x.lua", "x" }, { "BigMod/Scripts/y.lua", "y" }, { "readme.txt", "big readme" } }));
            InstallZip(gs, MakeZip(zips, "Big Mod Patch-1.0.zip", new Dictionary<string, string> { { "BigMod/Scripts/data.lua", "patched" } }));
            InstallZip(gs, MakeZip(zips, "Other Mod-1.0.zip", new Dictionary<string, string> { { "OtherMod/Scripts/main.lua", "other" }, { "OtherMod/Scripts/a.lua", "a" }, { "OtherMod/Scripts/b.lua", "b" }, { "readme.txt", "other readme" } }));
            InstallZip(gs, MakeZip(zips, "Same Lib A-1.0.zip", new Dictionary<string, string> { { "shared/Lib/lib.lua", "same" }, { "AMod/Scripts/main.lua", "a" } }));
            InstallZip(gs, MakeZip(zips, "Same Lib B-1.0.zip", new Dictionary<string, string> { { "shared/Lib/lib.lua", "same" }, { "BMod/Scripts/main.lua", "b" } }));
            InstallZip(gs, MakeZip(zips, "Rival-1.0.zip", new Dictionary<string, string> { { "OtherMod/Scripts/main.lua", "rival" }, { "Rival/Scripts/main.lua", "r" }, { "Rival/Scripts/c.lua", "c" } }));
            var p = gs.Active;
            foreach (var m in gs.Mods) if (!p.Enabled.Contains(m.Key)) p.Enabled.Add(m.Key);
            var patch = gs.Mods.First(m => m.Name.StartsWith("Big Mod Patch"));
            var big = gs.Mods.First(m => m.Name == "Big Mod");
            p.Order.Remove(patch.Key); p.Order.Insert(0, patch.Key);
            Deployer.Desired(gs, p, out var conflicts);
            Check("loose readme.txt files never clash or deploy", !conflicts.Keys.Any(k => k.EndsWith("readme.txt")), string.Join(",", conflicts.Keys));
            Check("identical files aren't clashes", !conflicts.Keys.Any(k => k.Contains("lib.lua")), string.Join(",", conflicts.Keys));
            var svc = new ManagerService();
            svc.State.Games.Clear(); svc.State.Games.Add(gs); svc.State.ActiveGame = gs.Game.Id;
            svc.SortCore(gs);
            Check("Auto-Sort puts the patch after its mod", p.Order.IndexOf(patch.Key) > p.Order.IndexOf(big.Key), string.Join(" | ", p.Order.Select(k => gs.Mod(k).Name)));
            object snap = svc.Snapshot();
            var rows = ((System.Collections.IEnumerable)snap.GetType().GetProperty("mods").GetValue(snap)).Cast<object>().ToList();
            object Row(string name) => rows.First(r => (string)r.GetType().GetProperty("name").GetValue(r) == name);
            var warn = (List<string>)Row("Other Mod").GetType().GetProperty("warn").GetValue(Row("Other Mod"));
            var bigWarn = (List<string>)Row("Big Mod").GetType().GetProperty("warn").GetValue(Row("Big Mod"));
            Check("unfixable clash gets the warning", warn != null && warn.Contains("Rival"), warn == null ? "null" : string.Join(",", warn));
            Check("patch clash is not a warning", bigWarn == null || bigWarn.Count == 0, bigWarn == null ? "" : string.Join(",", bigWarn));
            Deployer.Deploy(gs);
            Check("readme.txt not copied into the game", !File.Exists(Path.Combine(gs.Game.Root, "readme.txt")));
            string won = File.ReadAllText(T(gs, "Binaries/Win64/ue4ss/Mods/BigMod/Scripts/data.lua"));
            Check("patch wins its file after sorting", won == "patched", won + " | order: " + string.Join(" | ", p.Order.Select(k => gs.Mod(k).Name + (p.Enabled.Contains(k) ? "" : "(off)"))) + " | patch files: " + string.Join(",", patch.Files));
        }

        static void Vortex(string root)
        {
            var gs = NewGame(Path.Combine(root, "vgame"));
            string staging = Path.Combine(root, "vstaging");
            Write(Path.Combine(staging, "Cool Mod-123-1-2-1700000000", "CoolMod", "Scripts", "main.lua"), "cool");
            Write(Path.Combine(staging, "Cool Mod-123-1-2-1700000000", "__folder_managed_by_vortex"), "");
            Write(Path.Combine(staging, "Paky-55-2-0-1700000001", "Paky_P.pak"), "pak");
            Write(Path.Combine(staging, "Off Thing-9-1-0-1700000002", "OffThing", "Scripts", "main.lua"), "off");
            string paks = Path.Combine(gs.Game.Root, "Content", "Paks", "~mods");
            Directory.CreateDirectory(paks);
            File.WriteAllText(Path.Combine(paks, "vortex.deployment.json"),
                "{\"instance\":\"x\",\"stagingPath\":" + Newtonsoft(staging) + ",\"files\":[{\"relPath\":\"Paky_P.pak\",\"source\":\"Paky-55-2-0-1700000001\"}]}");
            string mods = gs.Game.ModsDir;
            Directory.CreateDirectory(mods);
            File.WriteAllText(Path.Combine(mods, "vortex.deployment.json"),
                "{\"stagingPath\":" + Newtonsoft(staging) + ",\"files\":[{\"relPath\":\"CoolMod\\\\Scripts\\\\main.lua\",\"source\":\"Cool Mod-123-1-2-1700000000\"}]}");
            var v = Importer.FindVortex(gs.Game);
            Check("Vortex staging found from its deployment file", v != null && string.Equals(v.Staging, staging, StringComparison.OrdinalIgnoreCase), v?.Staging ?? "null");
            var r = Importer.ImportVortex(gs, v);
            Check("Vortex mods imported, deployed ones on", r.Mods == 3 && r.Enabled == 2, r.Mods + " mods, " + r.Enabled + " on; " + string.Join("; ", r.Problems));
            var cool = gs.Mods.FirstOrDefault(m => m.Name == "Cool Mod");
            Check("Vortex Nexus id from folder name", cool != null && cool.SiteModId == "123" && cool.Version == "1.2", cool == null ? "missing" : cool.SiteModId + " " + cool.Version);
            var paky = gs.Mods.FirstOrDefault(m => m.Name == "Paky");
            Check("Vortex pak placed where Vortex deployed it", paky != null && paky.Files.Contains("Content/Paks/~mods/Paky_P.pak"), paky == null ? "missing" : string.Join(",", paky.Files));
        }

        static string Newtonsoft(string s) => "\"" + s.Replace("\\", "\\\\") + "\"";

        static void Crashes()
        {
            string latest = CrashReader.LatestCrash();
            if (latest == null) { Console.WriteLine("SKIP crash reader (no crash folder on this PC)"); return; }
            var r = CrashReader.Analyze(new[] { latest }, null, null, f => new Suspect { Name = f, Folder = f });
            Console.WriteLine("  crash: " + r.Cause + " | module " + r.Module + " | suspects: " + string.Join(", ", r.Suspects.Select(s => s.Name + " (" + s.Confidence + ")")));
            foreach (string e in r.Evidence.Take(6)) Console.WriteLine("    " + e);
            Check("crash reader reads a real crash folder", !string.IsNullOrEmpty(r.Cause) && r.Kind != "none" && r.Module != null, r.Kind + " " + r.Cause);
            string dmp = Directory.GetFiles(latest, "*.dmp").FirstOrDefault();
            if (dmp != null)
            {
                var md = Minidump.Parse(File.ReadAllBytes(dmp));
                Check("minidump module list + exception parsed", md.Modules.Count > 10 && md.ExceptionModule != null, md.Modules.Count + " modules, exception in " + md.ExceptionModule);
            }
            var none = CrashReader.Analyze(new[] { Path.Combine(Path.GetTempPath(), "no-such-crash-folder") }, null, null, null);
            Check("missing crash files explained", none.Kind == "none");
        }

        static void Detect(string root)
        {
            var g = ManagerService.GuessInfo(@"C:\x\UE4SS V4.0.0 STEAM 935 1 2026-11-23T02-55Z 60StiiJeB.zip");
            Check("new Nexus file name gives mod id", g.Site == "nexus" && g.SiteModId == "935" && g.Name == "UE4SS V4.0.0 STEAM" && g.Version == "1", g.Site + " " + g.SiteModId + " " + g.Name + " " + g.Version);
            var old = ManagerService.GuessInfo(@"C:\x\Magnetic Pick-UP-934-1-1-1-1763700000.zip");
            Check("old Nexus file name still works", old.SiteModId == "934" && old.Version == "1.1.1");
            Check("name match: exact", SourceDetect.NameMatch("Magnetic Pick-UP", "Magnetic Pick-UP"));
            Check("name match: longer Nexus title", SourceDetect.NameMatch("Enemy HP Numbers", "Enemy HP Numbers - Exact Health and Weaknesses"));
            Check("name match: word stems", SourceDetect.NameMatch("Fast Eat and Sleep", "Fast Eating Drinking and Sleeping"));
            Check("name match: folder name", SourceDetect.NameMatch("CustomWorldEdits", "Custom World Edits for All"));
            Check("name match rejects other mods", !SourceDetect.NameMatch("Max Cape", "Quest Cape") && !SourceDetect.NameMatch("Game Dump", "Gem Bag"));
            string z = Path.Combine(root, "zoned.zip");
            File.WriteAllText(z, "x");
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c (echo [ZoneTransfer]& echo ZoneId=3& echo ReferrerUrl=https://www.nexusmods.com/runescapedragonwilds/mods/934?tab=files^&file_id=4242& echo HostUrl=https://supporter-files.nexus-cdn.com/59/b8/x?md5=y) > \"" + z + ":Zone.Identifier\"") { CreateNoWindow = true, UseShellExecute = false };
            System.Diagnostics.Process.Start(psi).WaitForExit();
            var info = new InstallInfo { Site = "file" };
            bool ok = SourceDetect.FromZone(z, info);
            Check("download stamp (Zone.Identifier) names the Nexus mod", ok && info.Site == "nexus" && info.SiteModId == "934" && info.SiteFileId == "4242", ok + " " + info.Site + " " + info.SiteModId + " " + info.SiteFileId);
            string cf = Path.Combine(root, "cf.zip");
            File.WriteAllText(cf, "x");
            psi.Arguments = "/c (echo [ZoneTransfer]& echo ReferrerUrl=https://www.curseforge.com/runescape-dragonwilds/mods/cool-thing/files/123) > \"" + cf + ":Zone.Identifier\"";
            System.Diagnostics.Process.Start(psi).WaitForExit();
            var ci = new InstallInfo { Site = "file" };
            Check("download stamp names a CurseForge mod", SourceDetect.FromZone(cf, ci) && ci.Site == "curseforge" && ci.Url == "https://www.curseforge.com/runescape-dragonwilds/mods/cool-thing", ci.Site + " " + ci.Url);
        }

        static int IdentifyDry(string home)
        {
            Store.Home = home;
            var svc = new ManagerService();
            var gs = svc.State.Current;
            var before = gs.Mods.ToDictionary(m => m.Key, m => m.SiteModId + "|" + (m.Picture != null));
            svc.FillInfo().Wait();
            foreach (var m in gs.Mods)
                Console.WriteLine($"{m.Name,-42} nexus={m.Category,-15} auto={m.CategoryAuto,-15} {m.CategoryAutoScore,5:0.#} => {ManagerService.CategoryOf(m)}");
            return 0;
        }

        static void V040(string root)
        {
            var ranges = LuaMatch.FunctionRanges("local a = 1\nlocal function f(x)\n  if x then\n    return \"end\" -- end\n  end\nend\n--[[ function z() end ]]\nlocal t = { s = [[ function y() end ]] }\nfunction M.g()\n  for i = 1, 2 do print(i) end\n  repeat local q = 1 until q\nend\n");
            Check("Lua function ranges ignore strings and comments", ranges.Count == 2 && ranges[0].Key == 2 && ranges[0].Value == 6 && ranges[1].Key == 9 && ranges[1].Value == 12, string.Join(",", ranges.Select(r => r.Key + "-" + r.Value)));
            var b = new byte[0x200];
            int o = 0x41;
            b[o + 8] = 0x0A; b[o + 0x0C] = 5;
            BitConverter.GetBytes(1).CopyTo(b, o + 0x10); BitConverter.GetBytes(2).CopyTo(b, o + 0x14);
            BitConverter.GetBytes(7).CopyTo(b, o + 0x18); BitConverter.GetBytes(7).CopyTo(b, o + 0x1C);
            BitConverter.GetBytes(120).CopyTo(b, o + 0x2C); BitConverter.GetBytes(128).CopyTo(b, o + 0x30);
            BitConverter.GetBytes(0x10000L).CopyTo(b, o + 0x38);
            var protos = LuaMatch.ScanProtos(b);
            Check("Lua function found in crash memory at any byte offset", protos.Count == 1 && protos[0].Line == 120 && protos[0].Last == 128, protos.Count.ToString());
            Check("plain-words crash reasons", LuaMatch.KnownUe4ss(0x2237FD) != null && LuaMatch.ExceptionText(0xC0000005, "reading 0x10").Contains("already gone"));

            var gs = NewGame(Path.Combine(root, "g040"));
            string zips = Path.Combine(root, "z040"); Directory.CreateDirectory(zips);
            InstallZip(gs, MakeZip(zips, "Folder Mod-1.0.zip", new Dictionary<string, string> { { "FolderMod/Scripts/main.lua", "x" }, { "FolderMod/enabled.txt", "" } }));
            InstallZip(gs, MakeZip(zips, "Paky-1.0.zip", new Dictionary<string, string> { { "Paky_P.pak", "p" } }));
            foreach (var m in gs.Mods) gs.Active.Enabled.Add(m.Key);
            Deployer.Deploy(gs);
            var svc = new ManagerService();
            svc.State.Games.Clear(); svc.State.Games.Add(gs); svc.State.ActiveGame = gs.Game.Id;
            string fm = svc.ModFolderPath(gs.Mods.First(m => m.Name == "Folder Mod").Key);
            Check("double-click opens the Lua mod's game folder", fm != null && fm.EndsWith(@"ue4ss\Mods\FolderMod", StringComparison.OrdinalIgnoreCase) && Directory.Exists(fm), fm);
            string pk = svc.ModFolderPath(gs.Mods.First(m => m.Name == "Paky").Key);
            Check("double-click opens the pak mod's ~mods folder", pk != null && pk.Replace('\\', '/').Contains("Content/Paks/~mods") && Directory.Exists(pk), pk);
            Check("frameworks category", ManagerService.CategoryOf(new ModEntry { Name = "RuneSchema 0.9", Kind = "RuneSchema" }) == "Frameworks" && ManagerService.CategoryOf(new ModEntry { Name = "X", Category = "Gameplay" }) == "Gameplay" && ManagerService.CategoryOf(new ModEntry { Name = "Y" }) == "Other");
            var first = gs.Active.Order[0];
            svc.MoveTo(first, 1).Wait();
            Check("drag reorder moves the mod", gs.Active.Order[1] == first);
            svc.SetNote(first, "  turn off before co-op  ").Wait();
            Check("notes saved", gs.Mod(first).Notes == "turn off before co-op");
            var files = svc.ModFiles(first);
            Check("info window gets the file list", files != null && ((System.Collections.IEnumerable)files.GetType().GetProperty("files").GetValue(files)).Cast<object>().Any());
        }

        static void V050(string root)
        {
            var g = ManagerService.GuessInfo(@"C:\x\Dragon Claws 1.0.0 936 1.0.0 2026-11-23T14-46Z zFeAZZ5Nt.zip");
            Check("new Nexus name drops the version", g.Name == "Dragon Claws" && g.SiteModId == "936" && g.Version == "1.0.0", g.Name + "|" + g.SiteModId + "|" + g.Version);
            Check("name match ignores version numbers", SourceDetect.NameMatch("Dragon Claws 1.0.0", "Dragon Claws"));
            var cases = new Dictionary<string, string> {
                { "Relic Hunter UI", "User Interface" }, { "Enemy HP Numbers", "User Interface" }, { "Living Wilds Audio", "Audio" }, { "Cape Effects", "Armour" },
                { "Construction Overhaul", "Buildings" }, { "Elemental Totems", "Spells" }, { "Portable Chests", "Items" }, { "RS Spawner", "Utilities" },
                { "Wedding Bells", "Companions" }, { "Relic PhotoMode", "Visuals" }, { "Perks Of Ashenfall", "Gameplay" }, { "Dragon Claws", "Weapons" },
                { "Lockpicking", "Skills" }, { "Game Dump", "Utilities" } };
            foreach (var kv in cases)
            {
                string got = Categorizer.Guess(kv.Key, "", "Lua mod");
                Check("category: " + kv.Key + " -> " + kv.Value, got == kv.Value, got ?? "(none)");
            }
            Check("category from description words", Categorizer.Guess("Mystery Mod", "Adds a new HUD with a minimap and quest log", "Lua mod") == "User Interface");
            Check("Nexus category names cleaned up", Categorizer.Normalize("UI") == "User Interface" && Categorizer.Normalize("Armor") == "Armour");
            Check("own category wins", ManagerService.CategoryOf(new ModEntry { Name = "Relic Hunter UI", Category = "Gameplay", CategoryUser = "Visuals" }) == "Visuals");
            Check("specific Nexus category beats a weak guess", ManagerService.CategoryOf(new ModEntry { Name = "X", Category = "Visuals", CategoryAuto = "Items", CategoryAutoScore = 3 }) == "Visuals");
            Check("a sure guess beats Nexus", ManagerService.CategoryOf(new ModEntry { Name = "X", Category = "Visuals", CategoryAuto = "Audio", CategoryAutoScore = 12 }) == "Audio");
            Check("guess beats a generic Nexus Gameplay", ManagerService.CategoryOf(new ModEntry { Name = "X", Category = "Gameplay", CategoryAuto = "Armour", CategoryAutoScore = 3 }) == "Armour");
            Check("guess beats Nexus Miscellaneous", ManagerService.CategoryOf(new ModEntry { Name = "X", Category = "Miscellaneous", CategoryAuto = "Items" }) == "Items");

            void Zone(string file, string text)
            {
                if (!File.Exists(file)) File.WriteAllText(file, "x");
                var lines = text.Split('\n');
                string cmd = "/c (" + string.Join("& ", lines.Select(l => "echo " + l.Replace("&", "^&"))) + ") > \"" + file + ":Zone.Identifier\"";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false }).WaitForExit();
            }
            string dir = Path.Combine(root, "dl"); Directory.CreateDirectory(dir);
            string other = Path.Combine(dir, "SkyUI-12604-5-2SE-1600000000.zip");
            Zone(other, "[ZoneTransfer]\nReferrerUrl=https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=files");
            var c1 = DownloadWatcher.Check(other).Result;
            Check("browser download for another game isn't installed", c1 != null && !c1.Install, c1 == null ? "null" : c1.Reason);
            string plain = Path.Combine(dir, "holiday-photos.zip");
            File.WriteAllText(plain, "x");
            Check("downloads not from a mod site are ignored", DownloadWatcher.Check(plain).Result == null);
            string ours = Path.Combine(dir, "Cool Mod-934-1-0-1700000000.zip");
            MakeZip(dir, "Cool Mod-934-1-0-1700000000.zip", new Dictionary<string, string> { { "CoolMod/Scripts/main.lua", "x" } });
            Zone(ours, "[ZoneTransfer]\nReferrerUrl=https://www.nexusmods.com/runescapedragonwilds/mods/934?tab=files");
            var c2 = DownloadWatcher.Check(ours).Result;
            Check("Dragonwilds Nexus download is installed", c2 != null && c2.Install, c2 == null ? "null" : c2.Reason);
            string bad = Path.Combine(dir, "Fake-934-1-0-1700000001.zip");
            MakeZip(dir, "Fake-934-1-0-1700000001.zip", new Dictionary<string, string> { { "Data/Thing.esp", "x" } });
            Zone(bad, "[ZoneTransfer]\nReferrerUrl=https://www.nexusmods.com/runescapedragonwilds/mods/934?tab=files");
            var c3 = DownloadWatcher.Check(bad).Result;
            Check("archive with another game's files isn't installed", c3 != null && !c3.Install, c3 == null ? "null" : c3.Reason);
        }
    }
}
