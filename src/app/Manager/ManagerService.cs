// Made by Claude Ai & Relic Hunter
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RelicMods.Manager
{
    public sealed class PendingInstall
    {
        public string Id, Archive, Extracted;
        public List<string> Files;
        public InstallInfo Info;
    }

    public sealed class ManagerService
    {
        public AppState State { get; private set; }
        public bool DeployPending { get; private set; }
        public string LastResult { get; private set; }
        public event Action<object> Event;
        readonly Dictionary<string, PendingInstall> pending = new Dictionary<string, PendingInstall>();
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        public bool Busy { get; private set; }

        public ManagerService()
        {
            State = Store.Load();
        }

        void Emit(object e) => Event?.Invoke(e);
        void Progress(string text) => Emit(new { op = "mgr.progress", text });

        public void EnsureGames()
        {
            if (State.Games.Count > 0) return;
            foreach (var g in GameLocator.Find()) AddGame(g);
            Save();
        }

        GameState AddGame(Game g)
        {
            var existing = State.Games.FirstOrDefault(x => string.Equals(x.Game.Root, g.Root, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;
            g.Storage = GameLocator.DefaultStorage(g);
            var gs = new GameState { Game = g };
            gs.Profiles.Add(new Profile { Name = "Modded" });
            gs.Profiles.Add(new Profile { Name = "Vanilla" });
            g.ActiveProfile = "Modded";
            State.Games.Add(gs);
            if (State.ActiveGame == null) State.ActiveGame = g.Id;
            return gs;
        }

        public void Save() => Store.Save(State);

        public object Snapshot()
        {
            var gs = State.Current;
            bool running = Deployer.GameRunning();
            Dictionary<string, List<string>> conflicts = null;
            if (gs != null) Deployer.Desired(gs, gs.Active, out conflicts);
            var losers = new Dictionary<string, int>();
            var winners = new Dictionary<string, int>();
            if (conflicts != null)
                foreach (var c in conflicts.Values)
                {
                    for (int i = 0; i < c.Count - 1; i++) losers[c[i]] = losers.TryGetValue(c[i], out int n) ? n + 1 : 1;
                    string w = c[c.Count - 1];
                    winners[w] = winners.TryGetValue(w, out int m) ? m + 1 : 1;
                }
            var clashes = gs == null || conflicts == null ? new Dictionary<string, Dictionary<string, ClashInfo>>() : Clashes(gs, conflicts);
            bool hasUe4ss = gs != null && gs.Active != null && (gs.Active.Enabled.Any(k => gs.Mod(k)?.Kind?.Contains("UE4SS") == true) || Ue4ssCheck.DllPath(gs.Game.BinDir) != null);
            bool hasRs = gs != null && gs.Active != null && (gs.Active.Enabled.Any(k => gs.Mod(k)?.Kind?.Split('+').Any(x => x.Trim() == "RuneSchema") == true) || File.Exists(Path.Combine(gs.Game.ModsDir, "RuneSchema", "dlls", "main.dll")));
            return new
            {
                op = "mgr.state",
                running,
                busy = Busy,
                deployPending = DeployPending,
                lastResult = LastResult,
                advanced = State.Advanced,
                activeGame = gs?.Game.Id,
                games = State.Games.Select(x => new { id = x.Game.Id, name = x.Game.Name, kind = x.Game.Kind, root = x.Game.Root, bin = x.Game.BinName, storage = x.Game.Storage }).ToList(),
                profiles = gs?.Profiles.Select(p => p.Name).ToList() ?? new List<string>(),
                activeProfile = gs?.Game.ActiveProfile,
                deployedProfile = gs?.DeployedProfile,
                lastDeploy = gs?.LastDeploy,
                has = new { ue4ss = hasUe4ss, runeschema = hasRs },
                mods = gs == null ? null : gs.Active.Order.Select((k, i) => new { k, i }).Select(x =>
                {
                    var m = gs.Mod(x.k);
                    if (m == null) return null;
                    var needs = m.Needs.Where(n => (n == "ue4ss" && !hasUe4ss) || (n == "runeschema" && !hasRs)).ToList();
                    return new
                    {
                        key = m.Key, name = m.Name, version = m.Version, kind = m.Kind, site = m.Site, siteModId = m.SiteModId,
                        url = m.Url, picture = m.Picture, files = m.Files.Count, order = x.i,
                        on = gs.Active.Enabled.Contains(m.Key), latest = m.LatestVersion,
                        loses = losers.TryGetValue(m.Key, out int l) ? l : 0, wins = winners.TryGetValue(m.Key, out int w) ? w : 0,
                        needs, installedAt = m.InstalledAt, author = m.Author, relic = m.Relic == true,
                        crash = m.CrashNote, crashAt = m.CrashAt, notes = m.Notes, category = CategoryOf(m), catUser = !string.IsNullOrEmpty(m.CategoryUser),
                        clashWith = clashes.TryGetValue(m.Key, out var cw) ? cw.Select(z => new { key = z.Key, name = gs.Mod(z.Key)?.Name ?? z.Key, files = z.Value.Files, wins = z.Value.Wins, fixable = z.Value.Fixable }).ToList() : null,
                        warn = clashes.TryGetValue(m.Key, out var cw2) ? cw2.Where(z => !z.Value.Fixable).Select(z => gs.Mod(z.Key)?.Name ?? z.Key).ToList() : null
                    };
                }).Where(x => x != null).ToList(),
                conflicts = conflicts?.Select(c => new { file = c.Key, mods = c.Value.Select(k => gs.Mod(k)?.Name ?? k).ToList() }).Take(300).ToList(),
                mo2 = Importer.FindMo2(),
                vortex = gs == null ? null : Importer.FindVortex(gs.Game)?.Staging,
                ue4ss = gs == null ? null : Ue4ssCheck.Info(gs.Game.BinDir),
                libraryView = State.LibraryView ?? "gallery",
                watch = State.WatchDownloads,
                downloads = DownloadsSnapshot(),
                watchFolder = DownloadWatcher.DownloadsFolder(),
            };
        }

        async Task Run(string what, Func<Task> work)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            Store.Log(what);
            Busy = true;
            Emit(new { op = "mgr.busy", busy = true, text = what });
            try { await work().ConfigureAwait(false); }
            catch (Exception ex) { Store.Log(what + " FAILED: " + ex); Emit(new { op = "toast", text = ex.Message, bad = true }); LastResult = ex.Message; }
            finally
            {
                Busy = false;
                try { Save(); } catch (Exception ex) { Emit(new { op = "toast", text = "Couldn't save Relic Mod Manager state: " + ex.Message, bad = true }); }
                Emit(Snapshot());
                gate.Release();
            }
        }

        public Task Do(string what, Action work) => Run(what, () => { work(); return Task.CompletedTask; });

        void Apply()
        {
            var gs = State.Current;
            if (gs == null) return;
            if (Deployer.GameRunning()) { DeployPending = true; LastResult = "Close the game to apply your changes."; return; }
            var r = Deployer.Deploy(gs, Progress);
            DeployPending = false;
            LastResult = r.Summary();
            Store.Log("deploy: " + LastResult + (r.Problems.Count > 0 ? " | " + string.Join(" | ", r.Problems.Take(5)) : ""));
            if (r.Problems.Count > 0) Emit(new { op = "toast", text = r.Problems.Count + " files had problems: " + r.Problems[0], bad = true });
        }

        public static InstallInfo GuessInfo(string archive)
        {
            string name = Path.GetFileNameWithoutExtension(archive);
            var info = new InstallInfo { Name = name, Site = "file" };
            var nexus = Regex.Match(name, @"^(.*?)-(\d+)-([\d-]+?)-(\d{9,11})$");
            if (nexus.Success)
            {
                info.Name = nexus.Groups[1].Value.Trim();
                info.Site = "nexus";
                info.SiteGame = NexusData.GameDomain;
                info.SiteModId = nexus.Groups[2].Value;
                info.Version = nexus.Groups[3].Value.Replace('-', '.');
                info.Url = "https://www.nexusmods.com/" + NexusData.GameDomain + "/mods/" + info.SiteModId;
                return info;
            }
            var nx2 = Regex.Match(name, @"^(.+?) (\d+) ([0-9][\w.\-]*?) \d{4}-\d\d-\d\dT\d\d-\d\dZ(?: \w+)?$");
            if (nx2.Success)
            {
                info.Name = Regex.Replace(nx2.Groups[1].Value.Replace('_', ' ').Trim(), @"[\s\-]+v?\d+(?:\.\d+)+$", "", RegexOptions.IgnoreCase);
                info.Site = "nexus";
                info.SiteGame = NexusData.GameDomain;
                info.SiteModId = nx2.Groups[2].Value;
                info.Version = nx2.Groups[3].Value;
                info.Url = "https://www.nexusmods.com/" + NexusData.GameDomain + "/mods/" + info.SiteModId;
                return info;
            }
            var ver = Regex.Match(name, @"^(.*?)[\s_\-]+v?(\d+(?:\.\d+){1,3}[a-z]?)$", RegexOptions.IgnoreCase);
            if (ver.Success) { info.Name = ver.Groups[1].Value.Trim(); info.Version = ver.Groups[2].Value; }
            info.Name = Regex.Replace(info.Name, @"[_]+", " ").Trim();
            return info;
        }

        public Task InstallArchive(string archive, InstallInfo info = null) => Run("Installing " + Path.GetFileName(archive), async () =>
        {
            var gs = State.Current ?? throw new InvalidOperationException("Add your game first (Games page).");
            bool dropped = info == null;
            info = info ?? GuessInfo(archive);
            if (dropped && info.Site != "nexus") SourceDetect.FromZone(archive, info);
            if (info.Site == "nexus" && !string.IsNullOrEmpty(info.SiteModId))
            {
                try
                {
                    var found = (await NexusData.Mods(new[] { info.SiteModId }).ConfigureAwait(false)).FirstOrDefault();
                    if (found != null) { info.Name = found.Name ?? info.Name; info.Picture = found.Picture; info.Author = found.Author; info.Summary = found.Summary; info.Category = found.Category; }
                    if (!string.IsNullOrEmpty(info.SiteFileId))
                        info.Version = await NexusData.FileVersion(info.SiteModId, info.SiteFileId).ConfigureAwait(false) ?? info.Version;
                }
                catch { }
            }
            Progress("Unpacking " + Path.GetFileName(archive));
            string tmp = Path.Combine(Store.Home, "tmp", Guid.NewGuid().ToString("N").Substring(0, 12));
            Installer.Extract(archive, tmp);
            var files = Installer.ListFiles(tmp);
            var plan = Installer.Plan(files, info.Name ?? archive);
            if (plan.NeedsChoice)
            {
                string id = Guid.NewGuid().ToString("N").Substring(0, 8);
                pending[id] = new PendingInstall { Id = id, Archive = archive, Extracted = tmp, Files = files, Info = info };
                Emit(new { op = "mgr.place", id, name = info.Name, files = files.Take(60).ToList(), more = Math.Max(0, files.Count - 60), unplaced = plan.Unplaced.Take(20).ToList(), placed = plan.Map.Count });
                return;
            }
            Finish(gs, tmp, plan, info, archive);
            _ = FillInfo();
        });

        public Task AnswerPlacement(string id, string choice) => Run("Installing", () =>
        {
            if (!pending.TryGetValue(id, out var p)) return Task.CompletedTask;
            pending.Remove(id);
            if (choice == "cancel") { TryDelete(p.Extracted); return Task.CompletedTask; }
            var plan = Installer.PlanWithChoice(p.Files, choice, p.Info.Name);
            Finish(State.Current, p.Extracted, plan, p.Info, p.Archive);
            return Task.CompletedTask;
        });

        void Finish(GameState gs, string tmp, Placement plan, InstallInfo info, string archive)
        {
            Progress("Storing " + info.Name);
            var mod = Installer.Install(gs, tmp, plan, info, archive);
            TryDelete(tmp);
            mod.CrashNote = null; mod.CrashAt = null;
            mod.Relic = LooksRelic(gs, mod);
            SortCore(gs);
            Apply();
            Emit(new { op = "toast", text = "Installed " + mod.Name + (mod.Version != null ? " " + mod.Version : "") });
            var missing = mod.Needs.Where(n =>
                (n == "ue4ss" && Ue4ssCheck.DllPath(gs.Game.BinDir) == null && !gs.Active.Enabled.Any(k => gs.Mod(k)?.Kind == "UE4SS")) ||
                (n == "runeschema" && !File.Exists(Path.Combine(gs.Game.ModsDir, "RuneSchema", "dlls", "main.dll")) && !gs.Active.Enabled.Any(k => gs.Mod(k)?.Kind == "RuneSchema"))).ToList();
            if (missing.Count > 0) Emit(new { op = "mgr.needs", key = mod.Key, name = mod.Name, needs = missing });
        }

        static void TryDelete(string dir) { try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { } }

        public Task Toggle(string key, bool on) => Do("Updating", () =>
        {
            var p = State.Current?.Active; if (p == null) return;
            if (on) { if (!p.Enabled.Contains(key)) p.Enabled.Add(key); } else p.Enabled.Remove(key);
            Apply();
        });

        public Task Move(string key, int delta) => Do("Changing load order", () =>
        {
            var p = State.Current?.Active; if (p == null) return;
            int i = p.Order.IndexOf(key); int j = i + delta;
            if (i < 0 || j < 0 || j >= p.Order.Count) return;
            p.Order.RemoveAt(i); p.Order.Insert(j, key);
            Apply();
        });

        public Task Uninstall(string key) => Do("Uninstalling", () =>
        {
            var gs = State.Current; var mod = gs?.Mod(key); if (mod == null) return;
            if (Deployer.GameRunning()) throw new InvalidOperationException("Close the game before uninstalling.");
            foreach (var p in gs.Profiles) { p.Enabled.Remove(key); }
            Apply();
            foreach (var p in gs.Profiles) p.Order.Remove(key);
            gs.Mods.Remove(mod);
            TryDelete(Path.Combine(gs.Game.Storage, "mods", key));
            Emit(new { op = "toast", text = "Uninstalled " + mod.Name });
        });

        public Task SetProfile(string name) => Do("Switching profile", () =>
        {
            var gs = State.Current; if (gs == null || !gs.Profiles.Any(p => p.Name == name)) return;
            gs.Game.ActiveProfile = name;
            Apply();
        });

        public Task NewProfile(string name, bool copy) => Do("Adding profile", () =>
        {
            var gs = State.Current; if (gs == null) return;
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 40 || gs.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Pick a new, short profile name.");
            var src = gs.Active;
            gs.Profiles.Add(new Profile { Name = name, Order = src.Order.ToList(), Enabled = copy ? src.Enabled.ToList() : new List<string>() });
            gs.Game.ActiveProfile = name;
            Apply();
        });

        public Task DeleteProfile(string name) => Do("Deleting profile", () =>
        {
            var gs = State.Current; if (gs == null || gs.Profiles.Count <= 1) return;
            var p = gs.Profiles.FirstOrDefault(x => x.Name == name); if (p == null) return;
            gs.Profiles.Remove(p);
            if (gs.Game.ActiveProfile == name) { gs.Game.ActiveProfile = gs.Profiles[0].Name; Apply(); }
        });

        public Task SetGame(string id) => Do("Switching game", () => { if (State.Games.Any(g => g.Game.Id == id)) State.ActiveGame = id; });

        public Task Detect() => Do("Looking for the game", () =>
        {
            int before = State.Games.Count;
            foreach (var g in GameLocator.Find()) AddGame(g);
            Emit(new { op = "toast", text = State.Games.Count > before ? "Found " + (State.Games.Count - before) + " game install(s)" : "No new game installs found" });
        });

        public Task AddFolder(string folder) => Do("Adding game folder", () =>
        {
            var g = GameLocator.FromFolder(folder) ?? throw new InvalidOperationException("That folder doesn't look like RuneScape: Dragonwilds (no RSDragonwilds Binaries folder found).");
            var gs = AddGame(g);
            State.ActiveGame = gs.Game.Id;
        });

        public Task RemoveGame(string id) => Do("Removing game", () =>
        {
            var gs = State.Games.FirstOrDefault(g => g.Game.Id == id); if (gs == null) return;
            if (gs.Deployed.Count > 0) throw new InvalidOperationException("Switch this game to the Vanilla profile first so its mod files are removed.");
            State.Games.Remove(gs);
            if (State.ActiveGame == id) State.ActiveGame = State.Games.FirstOrDefault()?.Game.Id;
        });

        public Task ImportMo2(string root) => Do("Importing from MO2", () =>
        {
            var gs = State.Current ?? throw new InvalidOperationException("Add your game first.");
            var r = Importer.ImportMo2(gs, root ?? Importer.FindMo2() ?? throw new InvalidOperationException("MO2 folder not found."), Progress);
            SortCore(gs);
            Emit(new { op = "toast", text = $"Imported {r.Mods} mods from MO2 ({r.Enabled} on)" + (r.Problems.Count > 0 ? ", " + r.Problems.Count + " problems" : "") });
            Apply();
        });

        public Task CleanGame() => Do("Cleaning the game folder", () =>
        {
            var gs = State.Current ?? throw new InvalidOperationException("Add your game first.");
            int n = Deployer.CleanGameFolder(gs);
            Emit(new { op = "toast", text = n + " old mod files moved to " + Path.Combine(gs.Game.Storage, "cleaned") });
            Apply();
        });

        public Task ApplyNow() => Do("Applying", Apply);

        public Task SetAdvanced(bool on) => Do("", () => State.Advanced = on);

        public Task CheckUpdates() => Run("Checking for updates", async () =>
        {
            var gs = State.Current; if (gs == null) return;
            var nexus = gs.Mods.Where(m => m.Site == "nexus" && !string.IsNullOrEmpty(m.SiteModId)).ToList();
            if (nexus.Count == 0) return;
            var found = await NexusData.Mods(nexus.Select(m => m.SiteModId)).ConfigureAwait(false);
            int n = 0;
            foreach (var m in nexus)
            {
                var f = found.FirstOrDefault(x => x.ModId == m.SiteModId);
                if (f == null) continue;
                m.Picture = m.Picture ?? f.Picture;
                m.Author = m.Author ?? f.Author;
                m.LatestCheckedAt = Store.Now();
                if (!string.IsNullOrEmpty(f.Version) && !UnknownVersion(m.Version) && !SameVersion(f.Version, m.Version)) { m.LatestVersion = f.Version; n++; }
                else m.LatestVersion = null;
            }
            Emit(new { op = "toast", text = n == 0 ? "Every Nexus mod is up to date" : n + " update(s) available" });
        });

        static bool SameVersion(string a, string b)
        {
            string Norm(string s) => Regex.Replace((s ?? "").Trim().TrimStart('v', 'V'), @"(\.0)+$", "");
            return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
        }

        public sealed class ClashInfo { public int Files; public bool Wins; public bool Fixable = true; }

        static readonly string[] SoftExt = { ".txt", ".md", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".url", ".html", ".pdf", ".log" };

        static bool AddOnOf(ModEntry a, ModEntry b)
        {
            if (a == null || b == null || a.Files.Count == 0) return false;
            var set = new HashSet<string>(b.Files, StringComparer.OrdinalIgnoreCase);
            int shared = a.Files.Count(f => set.Contains(f));
            return shared >= Math.Ceiling(a.Files.Count * 0.8) && a.Files.Count < b.Files.Count;
        }

        static bool PatchName(string n) => Regex.IsMatch(n ?? "", @"\b(patch|fix|compat|compatibility|addon|add-on|tweak|override)\b", RegexOptions.IgnoreCase);

        Dictionary<string, Dictionary<string, ClashInfo>> Clashes(GameState gs, Dictionary<string, List<string>> conflicts)
        {
            var r = new Dictionary<string, Dictionary<string, ClashInfo>>();
            foreach (var c in conflicts)
            {
                bool soft = SoftExt.Contains(Path.GetExtension(c.Key).ToLowerInvariant());
                string winner = c.Value[c.Value.Count - 1];
                for (int i = 0; i < c.Value.Count; i++)
                    for (int j = 0; j < c.Value.Count; j++)
                    {
                        if (i == j) continue;
                        string a = c.Value[i], b = c.Value[j];
                        if (!r.TryGetValue(a, out var d)) r[a] = d = new Dictionary<string, ClashInfo>();
                        if (!d.TryGetValue(b, out var ci)) d[b] = ci = new ClashInfo();
                        ci.Files++;
                        if (a == winner) ci.Wins = true;
                        var ma = gs.Mod(a); var mb = gs.Mod(b);
                        bool related = AddOnOf(ma, mb) || AddOnOf(mb, ma) || PatchName(ma?.Name) || PatchName(mb?.Name);
                        if (!soft && !related) ci.Fixable = false;
                    }
            }
            return r;
        }

        static int Tier(ModEntry m)
        {
            string k = m.Kind ?? "", n = m.Name ?? "";
            if (k == "UE4SS" || Regex.IsMatch(n, @"^UE4SS\b", RegexOptions.IgnoreCase)) return 0;
            if (k == "RuneSchema" || Regex.IsMatch(n, @"^(RuneSchema|Relic Core|Relic Mod Manager|Mod Menu|Shared Mod Data|BPModLoader|UEHelpers)\b", RegexOptions.IgnoreCase) || Regex.IsMatch(n, @"\b(library|framework)\b", RegexOptions.IgnoreCase)) return 1;
            if (PatchName(n)) return 3;
            return 2;
        }

        public int SortCore(GameState gs)
        {
            var p = gs?.Active; if (p == null) return 0;
            var before = p.Order.ToList();
            var mods = p.Order.Select(k => gs.Mod(k)).Where(m => m != null).ToList();
            var sorted = mods.Select((m, i) => new { m, i }).OrderBy(x => Tier(x.m)).ThenBy(x => x.i).Select(x => x.m).ToList();
            for (int pass = 0; pass < 3; pass++)
                for (int i = 0; i < sorted.Count; i++)
                    for (int j = i + 1; j < sorted.Count; j++)
                        if (AddOnOf(sorted[i], sorted[j]) && Tier(sorted[i]) >= Tier(sorted[j]))
                        {
                            var a = sorted[i];
                            sorted.RemoveAt(i);
                            sorted.Insert(j, a);
                            i--;
                            break;
                        }
            var order = sorted.Select(m => m.Key).ToList();
            foreach (string k in before) if (!order.Contains(k)) order.Add(k);
            foreach (var prof in gs.Profiles)
            {
                var have = new HashSet<string>(prof.Order);
                var rest = prof.Order.Where(k => !order.Contains(k)).ToList();
                prof.Order = order.Where(k => have.Contains(k)).Concat(rest).ToList();
            }
            int moved = 0;
            for (int i = 0; i < before.Count && i < p.Order.Count; i++) if (before[i] != p.Order[i]) moved++;
            return moved;
        }

        public Task AutoSort() => Do("Auto-Sort", () =>
        {
            var gs = State.Current; if (gs == null) return;
            int moved = SortCore(gs);
            Apply();
            Deployer.Desired(gs, gs.Active, out var conflicts);
            int bad = Clashes(gs, conflicts).Values.SelectMany(d => d.Values).Count(c => !c.Fixable) / 2;
            Emit(new { op = "toast", text = (moved == 0 ? "Load order was already fine" : "Auto-Sort moved " + moved + " mod(s)") + (bad > 0 ? ". " + bad + " clash(es) need you: replace or remove one of those mods (marked ⚠)" : ""), bad = bad > 0 });
        });

        public Task ImportVortex(string staging) => Do("Importing from Vortex", () =>
        {
            var gs = State.Current ?? throw new InvalidOperationException("Add your game first.");
            var v = Importer.FindVortex(gs.Game, staging) ?? throw new InvalidOperationException("Vortex wasn't found for this game. Pick Vortex's mod staging folder.");
            var r = Importer.ImportVortex(gs, v, Progress);
            SortCore(gs);
            Emit(new { op = "toast", text = $"Imported {r.Mods} mods from Vortex ({r.Enabled} on)" + (r.Problems.Count > 0 ? ", " + r.Problems.Count + " skipped: " + r.Problems[0] : ""), bad = r.Problems.Count > 0 });
            Apply();
        });

        readonly object dlLock = new object();

        public void SaveDownload(DownloadRec r)
        {
            lock (dlLock)
            {
                var list = new List<DownloadRec>(State.Downloads ?? new List<DownloadRec>());
                list.RemoveAll(x => x.Id == r.Id);
                list.Add(Copy(r));
                while (list.Count > 80) list.RemoveAt(0);
                State.Downloads = list;
                try { Store.Save(State); } catch (Exception ex) { Store.Log("save download: " + ex.Message); }
            }
        }

        public void ClearDownloads()
        {
            lock (dlLock)
            {
                State.Downloads = (State.Downloads ?? new List<DownloadRec>()).Where(x => x.State == "downloading" || x.State == "installing").ToList();
                try { Store.Save(State); } catch { }
            }
        }

        static DownloadRec Copy(DownloadRec r) => (DownloadRec)typeof(DownloadRec).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(r, null);

        public static object DownloadMsg(DownloadRec d) => new { op = "mgr.download", id = d.Id, name = d.Name, file = d.File, path = d.Path, state = d.State, reason = d.Reason, received = d.Received, total = d.Total, picture = d.Picture, site = d.Site, modId = d.ModId, at = d.At };

        List<object> DownloadsSnapshot()
        {
            lock (dlLock) return (State.Downloads ?? new List<DownloadRec>()).Select(DownloadMsg).ToList();
        }

        public DownloadRec FindDownload(int id)
        {
            lock (dlLock) { var r = State.Downloads?.FirstOrDefault(x => x.Id == id); return r == null ? null : Copy(r); }
        }

        public ModEntry ModForArchive(string path) => State.Current?.Mods.FirstOrDefault(m => string.Equals(m.Archive, path, StringComparison.OrdinalIgnoreCase));

        public void PurgeDownloads(Func<DownloadRec, bool> drop)
        {
            lock (dlLock)
            {
                var list = State.Downloads ?? new List<DownloadRec>();
                if (!list.Any(drop)) return;
                State.Downloads = list.Where(x => !drop(x)).ToList();
                try { Store.Save(State); } catch { }
            }
        }

        public bool HasArchive(string path) => State.Current?.Mods.Any(m => string.Equals(m.Archive, path, StringComparison.OrdinalIgnoreCase)) == true;

        public bool PendingFor(string path)
        {
            lock (pending) return pending.Values.Any(p => string.Equals(p.Archive, path, StringComparison.OrdinalIgnoreCase));
        }

        public int LastDownloadId()
        {
            lock (dlLock) return State.Downloads == null || State.Downloads.Count == 0 ? 0 : State.Downloads.Max(x => x.Id);
        }

        public Task SetWatch(bool on) => Do("", () => State.WatchDownloads = on);

        public IEnumerable<string> UsedArchives() => State.Games.SelectMany(g => g.Mods).Select(m => m.Archive).Where(a => !string.IsNullOrEmpty(a));

        public Task SetLibraryView(string view) => Do("", () => State.LibraryView = view == "gallery" ? "gallery" : "list");

        public Task DismissCrash(string key) => Do("", () => { var m = State.Current?.Mod(key); if (m != null) { m.CrashNote = null; m.CrashAt = null; } });

        public Task Blame(CrashReport r) => Do("", () =>
        {
            var gs = State.Current; if (gs == null || r == null) return;
            foreach (var s in r.Suspects.Where(x => x.Confidence == "likely" && x.ModKey != null))
            {
                var m = gs.Mod(s.ModKey); if (m == null) continue;
                m.CrashNote = r.Cause + (string.IsNullOrEmpty(s.Why) ? "" : " " + s.Why);
                m.CrashAt = r.When;
            }
        });

        public List<Suspect> RecentlyChanged(string when)
        {
            var list = new List<Suspect>();
            var gs = State.Current;
            if (gs?.Active == null || !DateTime.TryParse(when, out DateTime crash)) return list;
            foreach (var m in gs.Mods.Where(x => gs.Active.Enabled.Contains(x.Key)))
            {
                if (!DateTime.TryParse(m.InstalledAt, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime at)) continue;
                at = at.ToLocalTime();
                if (at > crash || crash - at > TimeSpan.FromDays(3)) continue;
                list.Add(new Suspect { Name = m.Name, ModKey = m.Key, Relic = m.Relic == true, Confidence = "possible",
                    Why = "Installed or updated " + at.ToString("MMM d, HH:mm") + ", shortly before the crash.",
                    ReportUrl = m.Relic == true ? "https://discord.gg/BumVNYDUec" : m.Site == "nexus" && !string.IsNullOrEmpty(m.SiteModId) ? "https://www.nexusmods.com/" + NexusData.GameDomain + "/mods/" + m.SiteModId + "?tab=bugs" : m.Site == "curseforge" ? m.Url : null });
            }
            return list.OrderByDescending(s => s.Why).Take(3).ToList();
        }

        public Suspect Resolve(string folder)
        {
            var s = new Suspect { Folder = folder, Name = folder };
            var gs = State.Current;
            string f = "/" + folder.ToLowerInvariant();
            var mod = gs?.Mods.FirstOrDefault(m => m.Files.Any(x =>
            {
                string l = "/" + x.ToLowerInvariant();
                return l.Contains("/ue4ss/mods" + f + "/") || l.Contains("/~mods" + f + "/") || l.Contains("/~mods" + f + ".") || l.Contains("/runeschema/mods" + f + "/") || l.Contains("/logicmods" + f);
            }));
            if (mod != null)
            {
                s.Name = mod.Name; s.ModKey = mod.Key; s.Relic = mod.Relic == true;
                if (!s.Relic && mod.Site == "nexus" && !string.IsNullOrEmpty(mod.SiteModId)) s.ReportUrl = "https://www.nexusmods.com/" + NexusData.GameDomain + "/mods/" + mod.SiteModId + "?tab=bugs";
                else if (!s.Relic && mod.Site == "curseforge") s.ReportUrl = mod.Url;
            }
            if (gs != null && !s.Relic)
            {
                string dir = Path.Combine(gs.Game.ModsDir, folder);
                if (File.Exists(Path.Combine(dir, "relic.json"))) s.Relic = true;
                else
                {
                    string mf = Directory.Exists(dir) ? OtherRelics.ManifestPath(dir) : null;
                    if (mf != null) { var mm = OtherRelics.Load(dir, mf); s.Relic = mm.Ours; if (mod == null) s.Name = mm.Name ?? folder; }
                }
            }
            if (s.Relic) s.ReportUrl = "https://discord.gg/BumVNYDUec";
            return s;
        }

        static bool UnknownVersion(string v) => string.IsNullOrEmpty(v) || Regex.IsMatch(v, @"^d\d{4}\.\d{1,2}\.\d{1,2}");

        string StorageText(GameState gs, ModEntry m, Func<string, bool> pick)
        {
            string rel = m.Files.FirstOrDefault(pick);
            if (rel == null) return null;
            try { return File.ReadAllText(Path.Combine(gs.Game.Storage, "mods", m.Key, rel.Replace('/', '\\'))); } catch { return null; }
        }

        async Task Identify(GameState gs, ModEntry m)
        {
            m.IdTriedAt = Store.Now();
            var info = new InstallInfo { Site = m.Site };
            string relic = StorageText(gs, m, f => Path.GetFileName(f).Equals("relic.json", StringComparison.OrdinalIgnoreCase) && f.IndexOf("/ue4ss/Mods/", StringComparison.OrdinalIgnoreCase) >= 0);
            if (relic != null)
            {
                var nx = Regex.Match(relic, "\"nexus\"\\s*:\\s*\"?(\\d+)");
                var rv = Regex.Match(relic, "\"version\"\\s*:\\s*\"([^\"]+)\"");
                if (rv.Success && UnknownVersion(m.Version)) m.Version = rv.Groups[1].Value;
                if (nx.Success) { info.Site = "nexus"; info.SiteModId = nx.Groups[1].Value; }
            }
            if (info.SiteModId == null && m.Archive != null && File.Exists(m.Archive)) SourceDetect.FromZone(m.Archive, info);
            if (info.SiteModId == null && m.Archive != null && !m.Archive.StartsWith("MO2:") && !m.Archive.StartsWith("Vortex:"))
            {
                var g = GuessInfo(m.Archive);
                if (g.Site == "nexus") { info.Site = "nexus"; info.SiteModId = g.SiteModId; if (UnknownVersion(m.Version)) m.Version = g.Version; }
            }
            string folder = SourceDetect.ModFolder(m);
            if (info.SiteModId == null && folder != null && SourceDetect.KnownRelic.TryGetValue(folder, out string known)) { info.Site = "nexus"; info.SiteModId = known; }
            if (info.SiteModId == null)
            {
                string mm = StorageText(gs, m, f => Path.GetFileName(f).StartsWith("modmenu.", StringComparison.OrdinalIgnoreCase));
                string author = mm != null ? Regex.Match(mm, "\"author\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value : null;
                if (string.IsNullOrEmpty(author) && m.Relic == true) author = "RelicHunter";
                string mmName = mm != null ? Regex.Match(mm, "\"name\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value : null;
                foreach (string name in new[] { m.Name, mmName, folder }.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    List<NexusData.ModInfo> found;
                    try { found = await NexusData.FindByName(name).ConfigureAwait(false); } catch { break; }
                    var hit = found.FirstOrDefault(f => SourceDetect.NameMatch(name, f.Name) && SourceDetect.AuthorMatch(author, f));
                    if (hit != null) { info.Site = "nexus"; info.SiteModId = hit.ModId; break; }
                }
            }
            if (info.Site == "nexus" && info.SiteModId != null)
            {
                m.Site = "nexus"; m.SiteGame = NexusData.GameDomain; m.SiteModId = info.SiteModId;
                m.Url = "https://www.nexusmods.com/" + NexusData.GameDomain + "/mods/" + info.SiteModId;
                m.InfoCheckedAt = null;
                Store.Log("identified " + m.Name + " as Nexus mod " + info.SiteModId);
            }
            else if (info.Site == "curseforge" && info.Url != null) { m.Site = "curseforge"; m.SiteGame = info.SiteGame; m.SiteModId = info.SiteModId; m.Url = info.Url; }
        }

        string ModText(GameState gs, ModEntry m)
        {
            var parts = new List<string> { m.Summary };
            string mm = StorageText(gs, m, f => Path.GetFileName(f).StartsWith("modmenu.", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).Equals("relic.json", StringComparison.OrdinalIgnoreCase));
            if (mm != null) parts.Add(Regex.Match(mm, "\"description\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value);
            string rd = StorageText(gs, m, f => Regex.IsMatch(Path.GetFileName(f), @"^read ?me.*\.(txt|md)$", RegexOptions.IgnoreCase));
            if (rd != null) parts.Add(rd.Length > 2000 ? rd.Substring(0, 2000) : rd);
            return string.Join("\n", parts.Where(x => !string.IsNullOrEmpty(x)));
        }

        public Task FillInfo() => Run("", async () =>
        {
            var gs = State.Current; if (gs == null) return;
            try { await FillCore(gs).ConfigureAwait(false); }
            finally { foreach (var m in gs.Mods) { m.CategoryAuto = Categorizer.Guess(m.Name, ModText(gs, m), m.Kind, out double sc); m.CategoryAutoScore = sc; } }
        });

        async Task FillCore(GameState gs)
        {
            foreach (var m in gs.Mods.Where(x => x.Relic == null)) m.Relic = LooksRelic(gs, m);
            foreach (var m in gs.Mods.Where(x => string.IsNullOrEmpty(x.SiteModId) && x.IdTriedAt == null).ToList())
            {
                Progress("Finding " + m.Name + " on Nexus");
                await Identify(gs, m).ConfigureAwait(false);
            }
            var need = gs.Mods.Where(m => m.Site == "nexus" && !string.IsNullOrEmpty(m.SiteModId) &&
                (((m.Picture == null || m.Author == null) && m.InfoCheckedAt == null) || (m.Category == null && m.CategoryTriedAt == null))).ToList();
            if (need.Count == 0) return;
            var found = await NexusData.Mods(need.Select(m => m.SiteModId)).ConfigureAwait(false);
            foreach (var m in need)
            {
                m.InfoCheckedAt = Store.Now();
                m.CategoryTriedAt = Store.Now();
                var f = found.FirstOrDefault(x => x.ModId == m.SiteModId); if (f == null) continue;
                m.Category = m.Category ?? f.Category;
                m.Summary = m.Summary ?? f.Summary;
                m.Picture = m.Picture ?? f.Picture;
                m.Author = m.Author ?? f.Author;
                if (!string.IsNullOrEmpty(f.Name) && (Regex.IsMatch(m.Name ?? "", @"\d{4}-\d\d-\d\dT\d\d") || Regex.IsMatch(m.Name ?? "", @"[ -]" + m.SiteModId + @"[ -]"))) m.Name = f.Name;
                if (OtherRelics.IsRelicAuthor(f.Author) || OtherRelics.IsRelicAuthor(f.Uploader)) m.Relic = true;
            }
        }

        static bool LooksRelic(GameState gs, ModEntry m)
        {
            if (OtherRelics.IsRelicAuthor(m.Author)) return true;
            string root = Path.Combine(gs.Game.Storage, "mods", m.Key);
            foreach (string rel in m.Files)
            {
                string n = Path.GetFileName(rel).ToLowerInvariant();
                if (n == "relic.json") return true;
                if (n == "modmenu.json" || n == "modmenu.txt")
                {
                    try
                    {
                        string t = File.ReadAllText(Path.Combine(root, rel.Replace('/', '\\')));
                        var a = Regex.Match(t, "\"author\"\\s*:\\s*\"([^\"]*)\"");
                        if (a.Success && OtherRelics.IsRelicAuthor(a.Groups[1].Value)) return true;
                    }
                    catch { }
                }
            }
            return false;
        }

        public static string CategoryOf(ModEntry m)
        {
            string k = m.Kind ?? "", n = m.Name ?? "";
            if (!string.IsNullOrEmpty(m.CategoryUser)) return m.CategoryUser;
            if (k == "UE4SS" || k == "RuneSchema" || Regex.IsMatch(n, @"^(UE4SS|RuneSchema|Relic Core|Relic Mod Manager|Mod Menu|Shared Mod Data|BPModLoader)\b", RegexOptions.IgnoreCase)) return "Frameworks";
            string nx = Categorizer.Normalize(m.Category);
            bool generic = string.IsNullOrEmpty(nx) || nx == "Gameplay" || nx == "Miscellaneous" || nx == "Other";
            if (!string.IsNullOrEmpty(m.CategoryAuto) && (generic || m.CategoryAutoScore >= 8)) return m.CategoryAuto;
            if (!string.IsNullOrEmpty(nx)) return nx;
            return m.CategoryAuto ?? "Other";
        }

        static string ModDirRel(ModEntry m)
        {
            if (m.Kind == "UE4SS") return Installer.Win64 + "ue4ss";
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in m.Files)
            {
                if (Deployer.IsJunk(f)) continue;
                string d = null;
                var x = Regex.Match(f, @"^(.*?/ue4ss/Mods/RuneSchema/mods/[^/]+)/", RegexOptions.IgnoreCase);
                if (!x.Success) x = Regex.Match(f, @"^(.*?/ue4ss/Mods/[^/]+)/", RegexOptions.IgnoreCase);
                if (!x.Success) x = Regex.Match(f, @"^(Content/Paks/(?:~mods|LogicMods)/[^/]+)/", RegexOptions.IgnoreCase);
                if (!x.Success) x = Regex.Match(f, @"^(Content/Paks/(?:~mods|LogicMods))/", RegexOptions.IgnoreCase);
                d = x.Success ? x.Groups[1].Value : (f.Contains("/") ? f.Substring(0, f.LastIndexOf('/')) : "");
                if (d.EndsWith("/ue4ss/Mods/shared", StringComparison.OrdinalIgnoreCase)) continue;
                counts[d] = counts.TryGetValue(d, out int c) ? c + 1 : 1;
            }
            return counts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
        }

        public string ModFolderPath(string key)
        {
            var gs = State.Current; var m = gs?.Mod(key); if (m == null) return null;
            string rel = ModDirRel(m);
            if (gs.Active?.Enabled.Contains(key) == true && rel != null)
            {
                string p = Path.Combine(gs.Game.Root, Deployer.MapRel(gs.Game, rel).Replace('/', '\\'));
                if (Directory.Exists(p)) return p;
            }
            string st = Path.Combine(gs.Game.Storage, "mods", key);
            if (rel != null && Directory.Exists(Path.Combine(st, rel.Replace('/', '\\')))) return Path.Combine(st, rel.Replace('/', '\\'));
            return st;
        }

        public Task MoveTo(string key, int index) => Do("Changing load order", () =>
        {
            var p = State.Current?.Active; if (p == null) return;
            int i = p.Order.IndexOf(key); if (i < 0) return;
            p.Order.RemoveAt(i);
            p.Order.Insert(Math.Max(0, Math.Min(index, p.Order.Count)), key);
            Apply();
        });

        public Task SetNote(string key, string text) => Do("", () =>
        {
            var m = State.Current?.Mod(key); if (m == null) return;
            text = (text ?? "").Trim();
            m.Notes = text.Length == 0 ? null : (text.Length > 300 ? text.Substring(0, 300) : text);
        });

        public Task SetCategory(string key, string cat) => Do("", () =>
        {
            var m = State.Current?.Mod(key); if (m == null) return;
            cat = (cat ?? "").Trim();
            m.CategoryUser = cat.Length == 0 || cat == "Automatic" ? null : (cat.Length > 40 ? cat.Substring(0, 40) : cat);
        });

        public object ModFiles(string key)
        {
            var gs = State.Current; var m = gs?.Mod(key); if (m == null) return null;
            Deployer.Desired(gs, gs.Active, out var conflicts);
            var lost = new HashSet<string>(conflicts.Where(c => c.Value.Contains(key) && c.Value[c.Value.Count - 1] != key).Select(c => c.Key), StringComparer.OrdinalIgnoreCase);
            var won = new HashSet<string>(conflicts.Where(c => c.Value.Count > 1 && c.Value[c.Value.Count - 1] == key).Select(c => c.Key), StringComparer.OrdinalIgnoreCase);
            return new
            {
                op = "mgr.files", key,
                files = m.Files.Take(2000).Select(f => { string mp = Deployer.MapRel(gs.Game, f); return new { rel = f, lost = lost.Contains(mp), won = won.Contains(mp) }; }).ToList(),
                more = Math.Max(0, m.Files.Count - 2000),
                conflicts = conflicts.Where(c => c.Value.Contains(key)).Select(c => new { file = c.Key, mods = c.Value.Select(k => gs.Mod(k)?.Name ?? k).ToList() }).Take(300).ToList(),
                folder = ModFolderPath(key),
                archive = m.Archive, installedAt = m.InstalledAt, site = m.Site, url = m.Url,
            };
        }

        public void Play()
        {
            var gs = State.Current; if (gs == null) return;
            if (gs.Game.Kind == "steam") Process.Start(new ProcessStartInfo("steam://rungameid/" + (gs.Game.SteamAppId ?? GameLocator.SteamAppId)) { UseShellExecute = true });
            else
            {
                string exe = Directory.GetFiles(gs.Game.BinDir, "RSDragonwilds*.exe").FirstOrDefault();
                if (exe == null || gs.Game.Kind == "gamepass") throw new InvalidOperationException("Start the game from the Xbox app. Your mods are already in place.");
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = gs.Game.BinDir });
            }
        }
    }
}
