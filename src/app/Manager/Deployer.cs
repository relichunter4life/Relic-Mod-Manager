// Made by Claude Ai & Relic Hunter
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace RelicMods.Manager
{
    public sealed class DeployResult
    {
        public int Linked, Copied, Removed, Restored, BackedUp, Left, Generated;
        public List<string> Problems = new List<string>();
        public Dictionary<string, List<string>> Conflicts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public string Summary() =>
            $"{Linked + Copied} files placed ({Copied} copied), {Removed} removed, {BackedUp} game files set aside, {Restored} put back" +
            (Left > 0 ? $", {Left} left alone (changed outside Relic Mod Manager)" : "") + (Problems.Count > 0 ? $", {Problems.Count} problems" : "");
    }

    public static class Deployer
    {
        public const string RuneSchemaList = Installer.ModsRoot + "RuneSchema/mods/runeschema.txt";
        const string RsModsPrefix = Installer.ModsRoot + "RuneSchema/mods/";

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateHardLink(string newFile, string existing, IntPtr sa);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFileInformationByHandle(SafeFileHandle h, out BY_HANDLE_FILE_INFORMATION info);
        [StructLayout(LayoutKind.Sequential)]
        struct BY_HANDLE_FILE_INFORMATION
        {
            public uint Attributes; public long Created, Accessed, Written;
            public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }

        public static string FileId(string path)
        {
            using (var h = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero))
            {
                if (h.IsInvalid) return null;
                if (!GetFileInformationByHandle(h, out var i)) return null;
                return i.Volume.ToString("x") + ":" + i.IndexHigh.ToString("x") + i.IndexLow.ToString("x8");
            }
        }

        public static bool GameRunning() => Process.GetProcesses().Any(p =>
        {
            try { return p.ProcessName.StartsWith("RSDragonwilds", StringComparison.OrdinalIgnoreCase); } catch { return false; }
        });

        public static string MapRel(Game g, string rel)
        {
            if (g.BinName != "Win64" && rel.StartsWith(Installer.Win64, StringComparison.OrdinalIgnoreCase))
                return "Binaries/" + g.BinName + "/" + rel.Substring(Installer.Win64.Length);
            return rel;
        }

        public static string Target(Game g, string mapped) => Path.Combine(g.Root, mapped.Replace('/', '\\'));
        public static string Source(GameState gs, string mod, string rel) => Path.Combine(gs.Game.Storage, "mods", mod, rel.Replace('/', '\\'));

        static readonly string[] JunkExt = { ".txt", ".md", ".pdf", ".url", ".html", ".htm", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".nfo", ".rtf", ".docx", ".bbcode" };

        public static bool IsJunk(string rel) =>
            rel.IndexOf('/') < 0 && JunkExt.Contains(Path.GetExtension(rel).ToLowerInvariant());

        static readonly Dictionary<string, bool> SameCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public static bool SameContent(string a, string b)
        {
            try
            {
                var fa = new FileInfo(a); var fb = new FileInfo(b);
                if (!fa.Exists || !fb.Exists || fa.Length != fb.Length) return false;
                string key = a + "|" + b + "|" + fa.Length + "|" + fa.LastWriteTimeUtc.Ticks + "|" + fb.LastWriteTimeUtc.Ticks;
                lock (SameCache)
                    if (SameCache.TryGetValue(key, out bool known)) return known;
                bool same = true;
                using (var sa = fa.OpenRead())
                using (var sb = fb.OpenRead())
                {
                    var ba = new byte[65536]; var bb = new byte[65536];
                    while (true)
                    {
                        int na = sa.Read(ba, 0, ba.Length), nb = sb.Read(bb, 0, bb.Length);
                        if (na != nb) { same = false; break; }
                        if (na == 0) break;
                        for (int i = 0; i < na; i++) if (ba[i] != bb[i]) { same = false; break; }
                        if (!same) break;
                    }
                }
                lock (SameCache) SameCache[key] = same;
                return same;
            }
            catch { return false; }
        }

        public static Dictionary<string, KeyValuePair<string, string>> Desired(GameState gs, Profile p, out Dictionary<string, List<string>> conflicts)
        {
            var want = new Dictionary<string, KeyValuePair<string, string>>(StringComparer.OrdinalIgnoreCase);
            conflicts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (p == null) return want;
            foreach (string key in p.Order)
            {
                if (!p.Enabled.Contains(key)) continue;
                var mod = gs.Mod(key);
                if (mod == null) continue;
                foreach (string rel in mod.Files)
                {
                    if (IsJunk(rel)) continue;
                    string mapped = MapRel(gs.Game, rel);
                    if (want.TryGetValue(mapped, out var prev) && prev.Key != key && !SameContent(Source(gs, prev.Key, prev.Value), Source(gs, key, rel)))
                    {
                        if (!conflicts.TryGetValue(mapped, out var l)) conflicts[mapped] = l = new List<string> { prev.Key };
                        if (!l.Contains(key)) l.Add(key);
                    }
                    want[mapped] = new KeyValuePair<string, string>(key, rel);
                }
            }
            return want;
        }

        public static DeployResult Deploy(GameState gs, Action<string> progress = null)
        {
            if (GameRunning()) throw new InvalidOperationException("Close RuneScape: Dragonwilds first. Mods can't change while the game is running.");
            var g = gs.Game;
            var r = new DeployResult();
            var profile = gs.Active;
            var want = Desired(gs, profile, out r.Conflicts);
            string rsKey = MapRel(g, RuneSchemaList);
            var rsMods = want.Values.Select(v => v.Value)
                .Where(rel => rel.StartsWith(RsModsPrefix, StringComparison.OrdinalIgnoreCase) && rel.Substring(RsModsPrefix.Length).Contains("/"))
                .Select(rel => rel.Substring(RsModsPrefix.Length).Split('/')[0])
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            string rsBase = null;
            if (rsMods.Count > 0)
            {
                if (want.TryGetValue(rsKey, out var b)) rsBase = ReadText(Source(gs, b.Key, b.Value));
                want.Remove(rsKey);
            }

            progress?.Invoke("Removing files that are no longer needed");
            foreach (var kv in gs.Deployed.ToList())
            {
                string k = kv.Key;
                var d = kv.Value;
                string target = Target(g, k);
                if (d.Generated && rsMods.Count > 0 && k.Equals(rsKey, StringComparison.OrdinalIgnoreCase)) continue;
                string source = d.Generated ? null : Source(gs, d.Mod, d.Rel);
                bool ours = IsOurs(target, d);
                bool current = !d.Generated && ours && IsCurrent(target, source, d);
                if (!d.Generated && want.TryGetValue(k, out var w) && w.Key == d.Mod && w.Value == d.Rel && current) continue;
                try
                {
                    if (File.Exists(target))
                    {
                        if (ours)
                        {
                            if (d.Copy && current && Changed(target, d)) File.Copy(target, source, true);
                            File.Delete(target);
                            r.Removed++;
                        }
                        else r.Left++;
                    }
                    gs.Deployed.Remove(k);
                    if (gs.Backups.TryGetValue(k, out string bak))
                    {
                        if (File.Exists(bak) && !File.Exists(target))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            File.Move(bak, target);
                            r.Restored++;
                        }
                        gs.Backups.Remove(k);
                    }
                }
                catch (Exception ex) { r.Problems.Add(k + ": " + ex.Message); }
            }

            progress?.Invoke("Placing mod files");
            foreach (var kv in want)
            {
                string k = kv.Key;
                if (gs.Deployed.ContainsKey(k)) continue;
                string source = Source(gs, kv.Value.Key, kv.Value.Value);
                string target = Target(g, k);
                try
                {
                    if (!File.Exists(source)) { r.Problems.Add(k + ": missing from storage"); continue; }
                    SetAside(gs, k, target, r);
                    EnsureDir(gs, Path.GetDirectoryName(target));
                    var d = new DeployedFile { Mod = kv.Value.Key, Rel = kv.Value.Value };
                    if (CreateHardLink(target, source, IntPtr.Zero))
                    {
                        d.Id = FileId(target);
                        r.Linked++;
                    }
                    else
                    {
                        File.Copy(source, target, true);
                        d.Copy = true;
                        r.Copied++;
                    }
                    Stamp(d, source, target);
                    gs.Deployed[k] = d;
                }
                catch (Exception ex) { r.Problems.Add(k + ": " + ex.Message); }
            }

            if (rsMods.Count > 0)
            {
                string target = Target(g, rsKey);
                try
                {
                    string text = MergeRuneSchemaList(rsBase, rsMods);
                    bool mine = gs.Deployed.TryGetValue(rsKey, out var d) && d.Generated;
                    if (!mine) SetAside(gs, rsKey, target, r);
                    if (ReadText(target) != text)
                    {
                        EnsureDir(gs, Path.GetDirectoryName(target));
                        File.WriteAllText(target, text, new UTF8Encoding(false));
                    }
                    gs.Deployed[rsKey] = new DeployedFile { Generated = true, Rel = RuneSchemaList, Mod = "" };
                    r.Generated++;
                }
                catch (Exception ex) { r.Problems.Add("runeschema.txt: " + ex.Message); }
            }

            foreach (string dir in gs.CreatedDirs.OrderByDescending(x => x.Length).ToList())
            {
                string full = Path.Combine(g.Root, dir);
                try
                {
                    if (!Directory.Exists(full)) gs.CreatedDirs.Remove(dir);
                    else if (!Directory.EnumerateFileSystemEntries(full).Any()) { Directory.Delete(full); gs.CreatedDirs.Remove(dir); }
                }
                catch { }
            }
            gs.DeployedProfile = profile?.Name;
            gs.LastDeploy = Store.Now();
            return r;
        }

        static void SetAside(GameState gs, string k, string target, DeployResult r)
        {
            if (!File.Exists(target)) return;
            string bak = Path.Combine(gs.Game.Storage, "backup", k.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(bak));
            if (File.Exists(bak)) File.Delete(bak);
            File.Move(target, bak);
            gs.Backups[k] = bak;
            r.BackedUp++;
        }

        static bool IsOurs(string target, DeployedFile d)
        {
            if (!File.Exists(target)) return false;
            if (d.Generated || d.Copy) return true;
            return d.Id != null && FileId(target) == d.Id;
        }

        static bool IsCurrent(string target, string source, DeployedFile d)
        {
            if (source == null || !File.Exists(source)) return false;
            var s = new FileInfo(source);
            if (d.Copy) return s.Length == d.SrcSize && s.LastWriteTimeUtc.Ticks == d.SrcTicks;
            return FileId(source) == FileId(target);
        }

        static bool Changed(string target, DeployedFile d)
        {
            var t = new FileInfo(target);
            return t.Length != d.TgtSize || t.LastWriteTimeUtc.Ticks != d.TgtTicks;
        }

        static void Stamp(DeployedFile d, string source, string target)
        {
            var s = new FileInfo(source);
            var t = new FileInfo(target);
            d.SrcSize = s.Length; d.SrcTicks = s.LastWriteTimeUtc.Ticks;
            d.TgtSize = t.Length; d.TgtTicks = t.LastWriteTimeUtc.Ticks;
        }

        static void EnsureDir(GameState gs, string dir)
        {
            var missing = new List<string>();
            string root = gs.Game.Root.TrimEnd('\\');
            string cur = dir;
            while (!Directory.Exists(cur) && cur.Length > root.Length)
            {
                missing.Add(cur);
                cur = Path.GetDirectoryName(cur);
            }
            missing.Reverse();
            foreach (string m in missing)
            {
                Directory.CreateDirectory(m);
                string rel = m.Substring(root.Length + 1);
                if (!gs.CreatedDirs.Contains(rel)) gs.CreatedDirs.Add(rel);
            }
        }

        public static string MergeRuneSchemaList(string baseText, List<string> mods)
        {
            var lines = (baseText ?? "").Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).ToList();
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string l in lines)
            {
                int c = l.IndexOf(':');
                if (c > 0 && !l.TrimStart().StartsWith(";") && !l.TrimStart().StartsWith("#")) named.Add(l.Substring(0, c).Trim());
            }
            foreach (string m in mods) if (!named.Contains(m)) lines.Add(m + " : 1");
            return string.Join("\r\n", lines) + "\r\n";
        }

        public static int CleanGameFolder(GameState gs)
        {
            if (GameRunning()) throw new InvalidOperationException("Close RuneScape: Dragonwilds first.");
            var g = gs.Game;
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            string dest = Path.Combine(g.Storage, "cleaned", stamp);
            var roots = new[] { "Binaries/" + g.BinName + "/ue4ss", "Content/Paks/~mods", "Content/Paks/LogicMods" };
            var single = new[] { "Binaries/" + g.BinName + "/dwmapi.dll", "Binaries/" + g.BinName + "/ue4ss_loader.dll" };
            var files = new List<string>();
            foreach (string r in roots)
            {
                string full = Target(g, r);
                if (Directory.Exists(full)) files.AddRange(Directory.GetFiles(full, "*", SearchOption.AllDirectories));
            }
            foreach (string s in single) { string full = Target(g, s); if (File.Exists(full)) files.Add(full); }
            int moved = 0;
            string root = g.Root.TrimEnd('\\') + "\\";
            foreach (string f in files)
            {
                string rel = f.Substring(root.Length).Replace('\\', '/');
                if (gs.Deployed.ContainsKey(rel)) continue;
                string to = Path.Combine(dest, rel.Replace('/', '\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Move(f, to);
                moved++;
            }
            foreach (string r in roots)
            {
                string full = Target(g, r);
                if (!Directory.Exists(full)) continue;
                foreach (string d in Directory.GetDirectories(full, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
                    try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { }
            }
            return moved;
        }

        static string ReadText(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; } catch { return null; }
        }
    }
}
