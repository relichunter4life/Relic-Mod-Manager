// Made by Claude Ai & Relic Hunter
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace RelicMods.Manager
{
    public sealed class Placement
    {
        public Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> Unplaced = new List<string>();
        public List<string> Extras = new List<string>();
        public bool NeedsChoice => Map.Count == 0 || Unplaced.Count > 0;
    }

    public sealed class InstallInfo
    {
        public string Name, Version, Site, SiteGame, SiteModId, SiteFileId, Url, Picture, Author, Summary, Category;
    }

    public static class Installer
    {
        public const string Win64 = "Binaries/Win64/";
        public const string ModsRoot = "Binaries/Win64/ue4ss/Mods/";
        static readonly string[] PakExt = { ".pak", ".utoc", ".ucas", ".sig" };
        static readonly string[] ContentExt = { ".lua", ".dll", ".pak", ".utoc", ".ucas", ".uasset", ".uexp", ".ubulk", ".jsonc", ".usmap", ".exe" };
        static readonly string[] KeepNames = { "config.txt", "settings.txt", "settings.ini", "config.ini", "ue4ss-settings.ini" };

        public static readonly Dictionary<string, string> ChoiceBases = new Dictionary<string, string>
        {
            { "ue4ss", ModsRoot + "{name}/" },
            { "paks", "Content/Paks/~mods/{name}/" },
            { "logicmods", "Content/Paks/LogicMods/" },
            { "win64", Win64 },
            { "project", "" },
        };

        public static string Extract(string archive, string into)
        {
            if (Directory.Exists(into)) Directory.Delete(into, true);
            Directory.CreateDirectory(into);
            string full = Path.GetFullPath(into).TrimEnd('\\') + "\\";
            using (var a = ArchiveFactory.Open(archive))
            {
                foreach (var e in a.Entries.Where(x => !x.IsDirectory))
                {
                    string key = (e.Key ?? "").Replace('\\', '/').TrimStart('/');
                    if (key.Length == 0) continue;
                    string dest = Path.GetFullPath(Path.Combine(into, key.Replace('/', '\\')));
                    if (!dest.StartsWith(full, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe path in archive: " + key);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    e.WriteToFile(dest, new ExtractionOptions { Overwrite = true, PreserveFileTime = true });
                }
            }
            return into;
        }

        public static List<string> ListArchive(string archive)
        {
            using (var a = ArchiveFactory.Open(archive))
                return a.Entries.Where(x => !x.IsDirectory).Select(x => (x.Key ?? "").Replace('\\', '/')).ToList();
        }

        public static List<string> ListFiles(string dir)
        {
            string root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            return Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .Select(f => f.Substring(root.Length).Replace('\\', '/'))
                .Where(f => !f.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase) && !f.EndsWith("/.DS_Store") && f != ".DS_Store")
                .ToList();
        }

        public static Placement Plan(IEnumerable<string> files, string archiveName)
        {
            var list = files.ToList();
            var p = new Placement();
            string an = archiveName ?? "Mod";
            string ext0 = Path.GetExtension(an).ToLowerInvariant();
            string fallbackName = Store.Slug(ext0 == ".zip" || ext0 == ".7z" || ext0 == ".rar" ? Path.GetFileNameWithoutExtension(an) : an);
            var allDirs = list.Select(ModDirOf).Where(d => d != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var modDirs = allDirs.Where(d => !allDirs.Any(o => o.Length < d.Length && d.StartsWith(o, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(d => d.Length).ToList();
            var ue4ssParents = new HashSet<string>(list.Select(f => UpTo(f, "ue4ss/")).Where(x => x != null), StringComparer.OrdinalIgnoreCase);

            foreach (string f in list)
            {
                string name = Path.GetFileName(f);
                if (name.Equals("meta.ini", StringComparison.OrdinalIgnoreCase) && !f.Contains("/")) continue;
                string rel = Anchor(f, modDirs, ue4ssParents, fallbackName);
                if (rel != null) { p.Map[f] = rel; continue; }
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ContentExt.Contains(ext) || f.IndexOf("/Scripts/", StringComparison.OrdinalIgnoreCase) >= 0) p.Unplaced.Add(f);
                else p.Extras.Add(f);
            }
            return p;
        }

        public static Placement PlanWithChoice(IEnumerable<string> files, string choice, string name)
        {
            var p = new Placement();
            if (!ChoiceBases.TryGetValue(choice ?? "", out string b)) b = ChoiceBases["ue4ss"];
            b = b.Replace("{name}", Store.Slug(name));
            var list = files.ToList();
            string common = CommonDir(list);
            foreach (string f in list)
            {
                if (Path.GetFileName(f).Equals("meta.ini", StringComparison.OrdinalIgnoreCase) && !f.Contains("/")) continue;
                p.Map[f] = Normalize(b + f.Substring(common.Length));
            }
            return p;
        }

        static string CommonDir(List<string> files)
        {
            if (files.Count == 0) return "";
            var first = files[0].Split('/');
            int n = first.Length - 1;
            foreach (string f in files)
            {
                var parts = f.Split('/');
                int i = 0;
                while (i < n && i < parts.Length - 1 && string.Equals(parts[i], first[i], StringComparison.OrdinalIgnoreCase)) i++;
                n = i;
            }
            return n == 0 ? "" : string.Join("/", first.Take(n)) + "/";
        }

        static string ModDirOf(string f)
        {
            foreach (string marker in new[] { "Scripts/main.lua", "dlls/main.dll" })
            {
                if (f.Equals(marker, StringComparison.OrdinalIgnoreCase)) return "";
                if (f.EndsWith("/" + marker, StringComparison.OrdinalIgnoreCase)) return f.Substring(0, f.Length - marker.Length);
            }
            int sc = f.IndexOf("/Scripts/", StringComparison.OrdinalIgnoreCase);
            if (sc > 0 && f.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) return f.Substring(0, sc + 1);
            return null;
        }

        static string UpTo(string f, string segment)
        {
            int i = IndexOfSegment(f, segment);
            return i < 0 ? null : f.Substring(0, i);
        }

        static int IndexOfSegment(string f, string segment)
        {
            if (f.StartsWith(segment, StringComparison.OrdinalIgnoreCase)) return 0;
            int i = f.IndexOf("/" + segment, StringComparison.OrdinalIgnoreCase);
            return i < 0 ? -1 : i + 1;
        }

        static int LastIndexOfSegment(string f, string segment)
        {
            int i = f.LastIndexOf("/" + segment, StringComparison.OrdinalIgnoreCase);
            if (i >= 0) return i + 1;
            return f.StartsWith(segment, StringComparison.OrdinalIgnoreCase) ? 0 : -1;
        }

        static string Anchor(string f, List<string> modDirs, HashSet<string> ue4ssParents, string fallbackName)
        {
            foreach (string seg in new[] { "Binaries/Win64/", "Binaries/WinGDK/", "Content/Paks/", "Content/" })
            {
                int i = LastIndexOfSegment(f, seg);
                if (i >= 0)
                {
                    string rel = f.Substring(i);
                    if (rel.StartsWith("Binaries/WinGDK/", StringComparison.OrdinalIgnoreCase)) rel = Win64 + rel.Substring("Binaries/WinGDK/".Length);
                    return Normalize(rel);
                }
            }
            int u = LastIndexOfSegment(f, "ue4ss/");
            if (u >= 0) return Normalize(Win64 + f.Substring(u));
            string fname = Path.GetFileName(f);
            if (fname.Equals("dwmapi.dll", StringComparison.OrdinalIgnoreCase) || fname.Equals("ue4ss_loader.dll", StringComparison.OrdinalIgnoreCase))
            {
                string parent = f.Length > fname.Length ? f.Substring(0, f.Length - fname.Length) : "";
                if (ue4ssParents.Contains(parent)) return Win64 + fname;
            }
            int rs = LastIndexOfSegment(f, "RuneSchema/mods/");
            if (rs >= 0 && !modDirs.Any(d => d.Length > 0 && f.StartsWith(d, StringComparison.OrdinalIgnoreCase) && d.EndsWith("RuneSchema/", StringComparison.OrdinalIgnoreCase)))
                return Normalize(ModsRoot + f.Substring(rs));
            foreach (string d in modDirs)
            {
                if (d.Length > 0 && !f.StartsWith(d, StringComparison.OrdinalIgnoreCase)) continue;
                string modName = d.Length == 0 ? fallbackName : d.TrimEnd('/').Split('/').Last();
                if (modName.Equals("Mods", StringComparison.OrdinalIgnoreCase) || modName.Length == 0) modName = fallbackName;
                return Normalize(ModsRoot + modName + "/" + f.Substring(d.Length));
            }
            int m = LastIndexOfSegment(f, "Mods/");
            if (m >= 0)
            {
                string rest = f.Substring(m + 5);
                if (rest.Contains("/")) return Normalize(ModsRoot + rest);
            }
            string ext = Path.GetExtension(f).ToLowerInvariant();
            if (PakExt.Contains(ext))
            {
                if (f.IndexOf("LogicMods/", StringComparison.OrdinalIgnoreCase) >= 0) return "Content/Paks/LogicMods/" + fname;
                string parent = Path.GetDirectoryName(f.Replace('/', '\\'));
                string folder = string.IsNullOrEmpty(parent) ? fallbackName : Path.GetFileName(parent);
                return "Content/Paks/~mods/" + folder + "/" + fname;
            }
            return null;
        }

        static string Normalize(string rel)
        {
            rel = rel.Replace('\\', '/');
            while (rel.Contains("//")) rel = rel.Replace("//", "/");
            return rel.TrimStart('/');
        }

        public static string Kind(IEnumerable<string> rels)
        {
            var r = rels.ToList();
            var kinds = new List<string>();
            if (r.Any(x => x.EndsWith("ue4ss/UE4SS.dll", StringComparison.OrdinalIgnoreCase))) kinds.Add("UE4SS");
            if (r.Any(x => x.EndsWith("Mods/RuneSchema/dlls/main.dll", StringComparison.OrdinalIgnoreCase))) kinds.Add("RuneSchema");
            if (r.Any(x => x.IndexOf("Mods/RuneSchema/mods/", StringComparison.OrdinalIgnoreCase) >= 0) && !kinds.Contains("RuneSchema")) kinds.Add("RuneSchema mod");
            if (r.Any(x => x.EndsWith("/dlls/main.dll", StringComparison.OrdinalIgnoreCase) && x.IndexOf("Mods/RuneSchema/", StringComparison.OrdinalIgnoreCase) < 0)) kinds.Add("DLL mod");
            if (r.Any(x => x.EndsWith("/Scripts/main.lua", StringComparison.OrdinalIgnoreCase) && x.IndexOf("Mods/RuneSchema/", StringComparison.OrdinalIgnoreCase) < 0 && !kinds.Contains("UE4SS"))) kinds.Add("Lua mod");
            if (r.Any(x => x.StartsWith("Content/Paks/LogicMods/", StringComparison.OrdinalIgnoreCase))) kinds.Add("LogicMod");
            if (r.Any(x => x.StartsWith("Content/Paks/", StringComparison.OrdinalIgnoreCase) && !x.StartsWith("Content/Paks/LogicMods/", StringComparison.OrdinalIgnoreCase))) kinds.Add("Pak mod");
            return kinds.Count == 0 ? "Files" : string.Join(" + ", kinds);
        }

        public static List<string> Needs(IEnumerable<string> rels)
        {
            var r = rels.ToList();
            var needs = new List<string>();
            bool isUe4ss = r.Any(x => x.EndsWith("ue4ss/UE4SS.dll", StringComparison.OrdinalIgnoreCase));
            bool isRs = r.Any(x => x.EndsWith("Mods/RuneSchema/dlls/main.dll", StringComparison.OrdinalIgnoreCase));
            if (!isUe4ss && r.Any(x => x.StartsWith(ModsRoot, StringComparison.OrdinalIgnoreCase) || x.StartsWith("Content/Paks/LogicMods/", StringComparison.OrdinalIgnoreCase))) needs.Add("ue4ss");
            if (!isRs && r.Any(x => x.IndexOf("Mods/RuneSchema/mods/", StringComparison.OrdinalIgnoreCase) >= 0)) needs.Add("runeschema");
            return needs;
        }

        public static void ReadRelicManifest(string extracted, Placement plan, InstallInfo info)
        {
            if (info.Site != "file") return;
            var hit = plan.Map.FirstOrDefault(kv => kv.Value.StartsWith(ModsRoot, StringComparison.OrdinalIgnoreCase)
                && kv.Value.EndsWith("/relic.json", StringComparison.OrdinalIgnoreCase)
                && kv.Value.Substring(ModsRoot.Length).Count(c => c == '/') == 1);
            if (hit.Key == null) return;
            try
            {
                var d = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(extracted, hit.Key.Replace('/', '\\'))));
                if (d.TryGetValue("name", out object n) && n is string name && name.Length > 0) info.Name = name;
                if (d.TryGetValue("version", out object v) && v is string ver && ver.Length > 0) info.Version = ver;
                if (d.TryGetValue("nexus", out object x) && x != null && int.TryParse(Convert.ToString(x), out int id) && id > 0)
                {
                    info.Site = "nexus";
                    info.SiteGame = NexusData.GameDomain;
                    info.SiteModId = id.ToString();
                    info.Url = "https://www.nexusmods.com/" + NexusData.GameDomain + "/mods/" + id;
                }
            }
            catch { }
        }

        public static ModEntry Install(GameState gs, string extracted, Placement plan, InstallInfo info, string archive)
        {
            ReadRelicManifest(extracted, plan, info);
            string Loose(string n) => new string((n ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            var existing = gs.Mods.FirstOrDefault(m =>
                (!string.IsNullOrEmpty(info.SiteModId) && m.Site == info.Site && m.SiteModId == info.SiteModId) ||
                (string.IsNullOrEmpty(info.SiteModId) && Loose(m.Name).Length > 0 && Loose(m.Name) == Loose(info.Name)));
            if (existing == null)
            {
                var mine = Identity(plan.Map.Values);
                var newFiles = new HashSet<string>(plan.Map.Values, StringComparer.OrdinalIgnoreCase);
                if (mine.Count > 0)
                    existing = gs.Mods.FirstOrDefault(m =>
                    {
                        var theirs = Identity(m.Files);
                        int overlap = mine.Count(theirs.Contains);
                        if (overlap == 0 || overlap < 0.8 * Math.Max(mine.Count, theirs.Count)) return false;
                        var old = m.Files.Where(f => f.IndexOf('/') >= 0).ToList();
                        return old.Count == 0 || old.Count(newFiles.Contains) >= 0.5 * old.Count;
                    });
            }
            string key = existing?.Key ?? UniqueKey(gs, info.Name);
            string modsDir = Path.Combine(gs.Game.Storage, "mods");
            string final = Path.Combine(modsDir, key);
            string staging = Path.Combine(modsDir, key + ".new");
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            foreach (var kv in plan.Map)
            {
                string src = Path.Combine(extracted, kv.Key.Replace('/', '\\'));
                string dst = Path.Combine(staging, kv.Value.Replace('/', '\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(src, dst, true);
            }
            if (existing != null && Directory.Exists(final))
            {
                foreach (string old in Directory.GetFiles(final, "*", SearchOption.AllDirectories))
                {
                    string rel = old.Substring(final.Length + 1);
                    if (!IsKeep(rel)) continue;
                    string dst = Path.Combine(staging, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Copy(old, dst, true);
                }
                Directory.Delete(final, true);
            }
            Directory.Move(staging, final);

            var mod = existing ?? new ModEntry { Key = key };
            mod.Name = info.Name;
            mod.Version = info.Version;
            mod.Site = info.Site;
            mod.SiteGame = info.SiteGame;
            mod.SiteModId = info.SiteModId;
            mod.SiteFileId = info.SiteFileId;
            mod.Url = info.Url;
            mod.Picture = info.Picture ?? mod.Picture;
            mod.Author = info.Author ?? mod.Author;
            mod.Summary = info.Summary ?? mod.Summary;
            mod.Category = info.Category ?? mod.Category;
            mod.Archive = archive;
            mod.InstalledAt = Store.Now();
            mod.Files = Directory.GetFiles(final, "*", SearchOption.AllDirectories)
                .Select(f => f.Substring(final.Length + 1).Replace('\\', '/')).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            mod.Kind = Kind(mod.Files);
            mod.Needs = Needs(mod.Files);
            if (mod.LatestVersion != null && mod.LatestVersion == mod.Version) mod.LatestVersion = null;
            if (existing == null)
            {
                gs.Mods.Add(mod);
                foreach (var p in gs.Profiles)
                {
                    if (!p.Order.Contains(key)) p.Order.Add(key);
                    if (p.Name == gs.Game.ActiveProfile && !p.Enabled.Contains(key)) p.Enabled.Add(key);
                }
            }
            return mod;
        }

        public static HashSet<string> Identity(IEnumerable<string> rels)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string r in rels)
            {
                if (r.StartsWith(ModsRoot, StringComparison.OrdinalIgnoreCase))
                {
                    var parts = r.Substring(ModsRoot.Length).Split('/');
                    if (parts.Length < 2) continue;
                    if (parts[0].Equals("RuneSchema", StringComparison.OrdinalIgnoreCase) && parts.Length > 3 && parts[1].Equals("mods", StringComparison.OrdinalIgnoreCase))
                        set.Add("rs:" + parts[2]);
                    else if (!parts[0].Equals("RuneSchema", StringComparison.OrdinalIgnoreCase) || r.EndsWith("RuneSchema/dlls/main.dll", StringComparison.OrdinalIgnoreCase))
                        set.Add("mod:" + parts[0]);
                }
                else if (r.StartsWith("Content/Paks/~mods/", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = r.Substring(19).Split('/');
                    set.Add("pak:" + (parts.Length > 1 ? parts[0] : Path.GetFileNameWithoutExtension(parts[0])));
                }
                else if (r.StartsWith("Content/Paks/LogicMods/", StringComparison.OrdinalIgnoreCase))
                    set.Add("logic:" + Path.GetFileNameWithoutExtension(r));
                else if (r.EndsWith("ue4ss/UE4SS.dll", StringComparison.OrdinalIgnoreCase))
                    set.Add("ue4ss");
            }
            if (set.Contains("ue4ss")) set.RemoveWhere(x => x.StartsWith("mod:"));
            return set;
        }

        public static bool IsKeep(string rel)
        {
            string n = Path.GetFileName(rel).ToLowerInvariant();
            if (KeepNames.Contains(n)) return true;
            string r = rel.Replace('\\', '/').ToLowerInvariant();
            return r.Contains("/reliccore/settings/") && n.EndsWith(".json");
        }

        static string UniqueKey(GameState gs, string name)
        {
            string b = Store.Slug(name);
            string k = b;
            int i = 2;
            while (gs.Mods.Any(m => m.Key.Equals(k, StringComparison.OrdinalIgnoreCase))) k = b + "_" + i++;
            return k;
        }
    }
}
