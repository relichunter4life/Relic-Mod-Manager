// Made by Claude Ai & Relic Hunter
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using RelicMods.Manager;

namespace RelicMods
{
    sealed partial class MainForm
    {
        Dictionary<string, object> mmBridge;
        int mmSeq;
        bool startChecked;
        DateTime gameStartUtc = DateTime.MinValue;
        object lastCrash;
        DownloadWatcher watcher;

        void StartWatcher()
        {
            watcher?.Stop();
        }

        List<object> OthersForSnapshot()
        {
            var list = new List<object>();
            foreach (var m in OtherRelics.Scan(ModsDir))
                if (m.Ours) list.Add(new { folder = m.Folder, id = m.Id, name = m.Name, author = m.Author, ours = m.Ours, enabled = m.Enabled, manifest = m.Manifest, values = m.Values, error = m.Error });
            return list;
        }

        string ModMenuKey()
        {
            var gs = mgr.State.Current;
            return gs?.Mods.FirstOrDefault(m => m.Files.Any(f => f.IndexOf("/ue4ss/Mods/ModMenu/", StringComparison.OrdinalIgnoreCase) >= 0))?.Key;
        }

        void BumpRev()
        {
            Directory.CreateDirectory(BridgeDir);
            if (rev < 0) rev = ReadInt(Path.Combine(BridgeDir, "rev.txt"));
            rev++;
            File.WriteAllText(Path.Combine(BridgeDir, "rev.txt"), rev.ToString(), new UTF8Encoding(false));
        }

        Dictionary<string, object> Bridge()
        {
            if (mmBridge != null) return mmBridge;
            try
            {
                string t = ReadText(Path.Combine(BridgeDir, "modmenu.json"));
                if (t != null) mmBridge = json.Deserialize<Dictionary<string, object>>(t);
            }
            catch { }
            mmBridge = mmBridge ?? new Dictionary<string, object>();
            if (!(mmBridge.ContainsKey("mods") && mmBridge["mods"] is Dictionary<string, object>)) mmBridge["mods"] = new Dictionary<string, object>();
            mmBridge["actions"] = J.Arr(mmBridge.ContainsKey("actions") ? mmBridge["actions"] : null);
            mmSeq = mmBridge.ContainsKey("n") ? Convert.ToInt32(mmBridge["n"]) : 0;
            return mmBridge;
        }

        void WriteBridge()
        {
            if (!CoreInstalled) return;
            var b = Bridge();
            b["n"] = mmSeq;
            Directory.CreateDirectory(BridgeDir);
            File.WriteAllText(Path.Combine(BridgeDir, "modmenu.json"), json.Serialize(b), new UTF8Encoding(false));
            BumpRev();
        }

        void OnRelicMessage(string op, Dictionary<string, object> m, CoreWebView2WebMessageReceivedEventArgs e)
        {
            switch (op)
            {
                case "mm.save":
                    {
                        string folder = Str(m, "folder");
                        if (string.IsNullOrEmpty(folder) || folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return;
                        string dir = Path.Combine(ModsDir, folder);
                        string mf = OtherRelics.ManifestPath(dir);
                        if (mf == null) return;
                        var mod = OtherRelics.Load(dir, mf);
                        if (mod.Error != null) { Send(new { op = "toast", text = mod.Name + ": " + mod.Error, bad = true }); return; }
                        var vals = m.ContainsKey("values") ? m["values"] as Dictionary<string, object> : null;
                        if (vals == null) return;
                        var changes = vals.Where(kv => mod.Types.ContainsKey(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
                        string file = OtherRelics.ConfigPath(dir, mod.ConfigFile);
                        if (file == null) return;
                        OtherRelics.WriteValues(file, mod.Format, changes);
                        var mods = (Dictionary<string, object>)Bridge()["mods"];
                        var entry = mods.ContainsKey(mod.Id) ? mods[mod.Id] as Dictionary<string, object> : null;
                        int r = entry != null && entry.ContainsKey("rev") ? Convert.ToInt32(entry["rev"]) : 0;
                        var all = entry != null && entry.ContainsKey("set") && entry["set"] is Dictionary<string, object> prev ? new Dictionary<string, object>(prev) : new Dictionary<string, object>();
                        foreach (var kv in changes) all[kv.Key] = kv.Value;
                        mods[mod.Id] = new Dictionary<string, object>
                        {
                            { "rev", r + 1 },
                            { "set", all },
                            { "skip", mod.Types.Where(t => t.Value == "key").Select(t => t.Key).ToList() },
                        };
                        WriteBridge();
                        break;
                    }
                case "mm.action":
                    {
                        string id = Str(m, "id"), action = Str(m, "action");
                        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(action)) return;
                        var list = (List<object>)Bridge()["actions"];
                        mmSeq++;
                        list.Add(new Dictionary<string, object> { { "id", id }, { "a", action }, { "n", mmSeq } });
                        while (list.Count > 20) list.RemoveAt(0);
                        WriteBridge();
                        Send(new { op = "toast", text = CoreInstalled ? "Sent to the game" : "Install Relic Core in the game so buttons reach your mods" });
                        break;
                    }
                case "mm.modmenuOff":
                    {
                        string key = ModMenuKey();
                        if (key != null) _ = mgr.Toggle(key, false).ContinueWith(t => BeginInvoke(new Action(SendSnapshot)));
                        break;
                    }
                case "crash.drop":
                    {
                        var paths = new List<string>();
                        if (e.AdditionalObjects != null)
                            foreach (var o in e.AdditionalObjects)
                                if (o is CoreWebView2File f) paths.Add(f.Path);
                        RunCrash(paths, false);
                        break;
                    }
                case "crash.latest":
                    {
                        string latest = CrashReader.LatestCrash();
                        if (latest == null) { Send(new { op = "toast", text = "No game crash files found on this PC." }); break; }
                        RunCrash(new List<string> { latest }, false);
                        break;
                    }
                case "crash.pick":
                    using (var d = new OpenFileDialog { Filter = "Crash files (*.dmp;*.log;*.runtime-xml)|*.dmp;*.log;*.runtime-xml|All files|*.*", Multiselect = true, Title = "Pick crash files", InitialDirectory = Directory.Exists(CrashReader.CrashesDir) ? CrashReader.CrashesDir : null })
                        if (d.ShowDialog(this) == DialogResult.OK) RunCrash(d.FileNames.ToList(), false);
                    break;
                case "crash.dismiss": _ = mgr.DismissCrash(Str(m, "key")); break;
                case "crash.list": Send(new { op = "crash.list", items = CrashReader.Recent(Path.GetDirectoryName(ModsDir)) }); break;
                case "crash.read": { string pth = Str(m, "path"); if (!string.IsNullOrEmpty(pth) && (File.Exists(pth) || Directory.Exists(pth))) RunCrash(new List<string> { pth }, false); break; }
                case "files.tree": Send(new { op = "files.tree", tree = FolderTree() }); break;
                case "files.open": { string pth = Str(m, "path"); if (!string.IsNullOrEmpty(pth) && Directory.Exists(pth) && pth.StartsWith(GameRootGuess(), StringComparison.OrdinalIgnoreCase)) System.Diagnostics.Process.Start("explorer.exe", "\"" + pth + "\""); break; }
                case "crash.last": if (lastCrash != null) Send(lastCrash); break;
                case "nx.search":
                    {
                        string q = Str(m, "q"), sort = Str(m, "sort");
                        int offset = (int)Num(m, "offset");
                        int token = (int)Num(m, "token");
                        Task.Run(async () =>
                        {
                            try
                            {
                                var res = await NexusData.Search(q, sort, offset).ConfigureAwait(false);
                                BeginInvoke(new Action(() => Send(new { op = "nx.list", token, result = res })));
                            }
                            catch (Exception ex) { BeginInvoke(new Action(() => Send(new { op = "nx.list", token, error = ex.Message }))); }
                        });
                        break;
                    }
            }
        }

        string GameRootGuess()
        {
            string bin = Path.GetDirectoryName(Path.GetDirectoryName(ModsDir));
            return Path.GetDirectoryName(Path.GetDirectoryName(bin));
        }

        static long DirSize(string d, out int count)
        {
            long size = 0; count = 0;
            try { foreach (var f in new DirectoryInfo(d).EnumerateFiles("*", SearchOption.AllDirectories)) { size += f.Length; count++; } } catch { }
            return size;
        }

        object FolderTree()
        {
            string ue4ss = Path.GetDirectoryName(ModsDir), bin = Path.GetDirectoryName(ue4ss), root = GameRootGuess();
            var gs = mgr.State.Current;
            bool same = gs != null && string.Equals(Path.GetFullPath(gs.Game.Root).TrimEnd('\\'), Path.GetFullPath(root).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (same)
                foreach (var kv in gs.Deployed)
                {
                    string name = gs.Mod(kv.Value.Mod)?.Name; if (name == null) continue;
                    string rel = kv.Key.Replace('\\', '/');
                    for (int i = rel.IndexOf('/'); i > 0; i = rel.IndexOf('/', i + 1))
                    {
                        string dir = rel.Substring(0, i);
                        if (!owners.ContainsKey(dir)) owners[dir] = name;
                        else if (owners[dir] != name) owners[dir] = "(several mods)";
                    }
                    owners[rel] = name;
                }
            string Owner(string full)
            {
                string rel = full.Length > root.Length ? full.Substring(root.Length).TrimStart('\\').Replace('\\', '/') : "";
                return owners.TryGetValue(rel, out string o) ? o : null;
            }
            var modsTxt = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string mt = Path.Combine(ModsDir, "mods.txt");
                if (File.Exists(mt))
                    foreach (string line in File.ReadAllLines(mt))
                    {
                        var x = System.Text.RegularExpressions.Regex.Match(line, @"^\s*([^;:#][^:]*?)\s*:\s*([01])");
                        if (x.Success) modsTxt[x.Groups[1].Value] = x.Groups[2].Value == "1";
                    }
            }
            catch { }
            var sections = new List<object>();
            if (Directory.Exists(bin))
            {
                var loose = Directory.GetFiles(bin).Where(f => !Path.GetFileName(f).StartsWith("RSDragonwilds", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(f).StartsWith("tbb", StringComparison.OrdinalIgnoreCase))
                    .Select(f => (object)new { name = Path.GetFileName(f), kind = Path.GetExtension(f).TrimStart('.').ToUpperInvariant(), size = new FileInfo(f).Length, owner = Owner(f) }).ToList();
                sections.Add(new { id = "win64", title = "Game folder (Binaries\\Win64)", path = bin, note = "dwmapi.dll here starts UE4SS when the game launches.", items = loose });
            }
            if (Directory.Exists(ue4ss))
            {
                var items = Directory.GetFiles(ue4ss).Select(f => (object)new { name = Path.GetFileName(f), kind = Path.GetExtension(f).TrimStart('.').ToUpperInvariant(), size = new FileInfo(f).Length, owner = Owner(f) }).ToList();
                sections.Add(new { id = "ue4ss", title = "UE4SS (ue4ss)", path = ue4ss, note = "The mod loader itself, its settings and its log.", items });
            }
            if (Directory.Exists(ModsDir))
            {
                var items = new List<object>();
                foreach (string d in Directory.GetDirectories(ModsDir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    string n = Path.GetFileName(d);
                    bool en = File.Exists(Path.Combine(d, "enabled.txt")) || (modsTxt.TryGetValue(n, out bool on) && on);
                    var tags = new List<string>();
                    if (Directory.Exists(Path.Combine(d, "Scripts"))) tags.Add("Lua");
                    if (Directory.Exists(Path.Combine(d, "dlls"))) tags.Add("DLL");
                    if (File.Exists(Path.Combine(d, "relic.json"))) tags.Add("Relic");
                    if (OtherRelics.ManifestPath(d) != null) tags.Add("Settings");
                    if (n.Equals("shared", StringComparison.OrdinalIgnoreCase)) tags.Add("Library");
                    long size = DirSize(d, out int cnt);
                    items.Add(new { name = n, folder = true, path = d, on = en || n.Equals("shared", StringComparison.OrdinalIgnoreCase), tags, files = cnt, size, owner = Owner(d) });
                }
                sections.Add(new { id = "mods", title = "UE4SS mods (ue4ss\\Mods)", path = ModsDir, note = "Each folder is one mod. A mod runs when it has enabled.txt (or is switched on in mods.txt).", items });
                string rs = Path.Combine(ModsDir, "RuneSchema", "mods");
                if (Directory.Exists(rs))
                {
                    var rsItems = Directory.GetDirectories(rs).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(d => { long sz = DirSize(d, out int c); return (object)new { name = Path.GetFileName(d), folder = true, path = d, on = true, tags = new List<string> { "RuneSchema" }, files = c, size = sz, owner = Owner(d) }; }).ToList();
                    sections.Add(new { id = "rs", title = "RuneSchema mods (ue4ss\\Mods\\RuneSchema\\mods)", path = rs, note = "Items, recipes, NPCs and quests that RuneSchema loads. runeschema.txt lists the ones in use.", items = rsItems });
                }
            }
            foreach (var pk in new[] { new { id = "paks", dir = "~mods", title = "Pak mods (Content\\Paks\\~mods)", note = "Replacement game files. The game loads every .pak here." }, new { id = "logic", dir = "LogicMods", title = "Blueprint mods (Content\\Paks\\LogicMods)", note = "Blueprint mods that UE4SS's BPModLoader starts." } })
            {
                string d = Path.Combine(root, "Content", "Paks", pk.dir);
                if (!Directory.Exists(d)) continue;
                var items = new List<object>();
                foreach (string sub in Directory.GetDirectories(d).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) { long sz = DirSize(sub, out int c); items.Add(new { name = Path.GetFileName(sub), folder = true, path = sub, on = true, tags = new List<string> { "Pak" }, files = c, size = sz, owner = Owner(sub) }); }
                foreach (string f in Directory.GetFiles(d)) items.Add(new { name = Path.GetFileName(f), kind = Path.GetExtension(f).TrimStart('.').ToUpperInvariant(), size = new FileInfo(f).Length, owner = Owner(f) });
                sections.Add(new { pk.id, pk.title, path = d, pk.note, items });
            }
            return new { root, managed = same, sections };
        }

        void RunCrash(List<string> paths, bool auto)
        {
            string ue4ssLog = Path.Combine(Path.GetDirectoryName(ModsDir), "UE4SS.log");
            Task.Run(() =>
            {
                CrashReport r;
                try { r = CrashReader.Analyze(paths, ModsDir, File.Exists(ue4ssLog) ? ue4ssLog : null, f => mgr.Resolve(f)); }
                catch (Exception ex) { r = new CrashReport { Kind = "unknown", Cause = "Couldn't read those crash files: " + ex.Message }; }
                if (r.Suspects.Count == 0 && (r.Kind == "lua" || r.Kind == "game" || r.Kind == "pak" || r.Kind == "unknown"))
                {
                    var recent = mgr.RecentlyChanged(r.When);
                    if (recent.Count > 0) { r.Suspects.AddRange(recent); r.Cause += " These mods changed just before it, so check them first."; }
                }
                BeginInvoke(new Action(() =>
                {
                    lastCrash = new { op = "crash.report", auto, report = r };
                    Send(lastCrash);
                    if (r.Suspects.Any(s => s.Confidence == "likely")) _ = mgr.Blame(r);
                }));
            });
        }

        void OnRunningChanged(bool running)
        {
            if (running) { gameStartUtc = DateTime.UtcNow.AddSeconds(-5); return; }
            if (gameStartUtc == DateTime.MinValue) return;
            var since = gameStartUtc;
            var t = new Timer { Interval = 4000 };
            t.Tick += (s, e) =>
            {
                t.Stop(); t.Dispose();
                var found = CrashReader.NewCrashes(since, Path.GetDirectoryName(ModsDir));
                if (found.Count == 0) return;
                mgr.State.LastCrashSeen = Store.Now();
                RunCrash(new List<string> { found[0] }, true);
                if (fromGame) ShowNow();
            };
            t.Start();
        }

        void CheckCrashOnStart()
        {
            if (startChecked) return;
            startChecked = true;
            if (!fromGame) _ = mgr.FillInfo();
            try { StartWatcher(); } catch (Exception ex) { Store.Log("download watcher: " + ex.Message); }
            DateTime since;
            if (!DateTime.TryParse(mgr.State.LastCrashSeen, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out since))
                since = DateTime.UtcNow.AddDays(-1);
            if (since < DateTime.UtcNow.AddDays(-2)) since = DateTime.UtcNow.AddDays(-2);
            var found = CrashReader.NewCrashes(since, Path.GetDirectoryName(ModsDir));
            mgr.State.LastCrashSeen = Store.Now();
            try { mgr.Save(); } catch { }
            if (found.Count > 0) RunCrash(new List<string> { found[0] }, true);
        }


    }
}
