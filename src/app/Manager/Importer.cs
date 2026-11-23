// Made by Claude Ai & Relic Hunter
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RelicMods.Manager
{
    public sealed class ImportResult
    {
        public int Mods, Files, Enabled, Skipped;
        public List<string> Problems = new List<string>();
    }

    public static class Importer
    {
        public static string FindMo2()
        {
            foreach (string c in new[] { @"D:\Modding\MO2-Dragonwilds", @"C:\Modding\MO2-Dragonwilds",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModOrganizer", "RuneScape Dragonwilds") })
                if (File.Exists(Path.Combine(c, "ModOrganizer.ini"))) return c;
            return null;
        }

        static string Ini(string file, string key)
        {
            if (!File.Exists(file)) return null;
            foreach (string line in File.ReadAllLines(file))
            {
                if (!line.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) continue;
                string v = line.Substring(key.Length + 1).Trim();
                var m = Regex.Match(v, @"^@ByteArray\((.*)\)$");
                if (m.Success) v = m.Groups[1].Value;
                return v.Replace(@"\\", @"\");
            }
            return null;
        }

        public static ImportResult ImportMo2(GameState gs, string mo2Root, Action<string> progress = null)
        {
            var r = new ImportResult();
            string ini = Path.Combine(mo2Root, "ModOrganizer.ini");
            if (!File.Exists(ini)) throw new FileNotFoundException("ModOrganizer.ini not found in " + mo2Root);
            string modsDir = (Ini(ini, "mod_directory") ?? Path.Combine(mo2Root, "mods")).Replace("%BASE_DIR%", mo2Root);
            string profile = Ini(ini, "selected_profile") ?? "Default";
            string list = Path.Combine(mo2Root, "profiles", profile, "modlist.txt");
            if (!File.Exists(list)) throw new FileNotFoundException("MO2 mod list not found: " + list);
            var entries = new List<KeyValuePair<string, bool>>();
            foreach (string raw in File.ReadAllLines(list))
            {
                string l = raw.Trim();
                if (l.Length < 2 || l.StartsWith("#")) continue;
                char c = l[0];
                string name = l.Substring(1);
                if ((c != '+' && c != '-') || name.EndsWith("_separator", StringComparison.OrdinalIgnoreCase)) continue;
                entries.Add(new KeyValuePair<string, bool>(name, c == '+'));
            }
            entries.Reverse();
            string overwrite = Path.Combine(mo2Root, "overwrite");
            var all = entries.Select(e => new { Name = e.Key, On = e.Value, Dir = Path.Combine(modsDir, e.Key) }).ToList();
            if (Directory.Exists(overwrite) && Directory.EnumerateFiles(overwrite, "*", SearchOption.AllDirectories).Any())
                all.Add(new { Name = "MO2 Overwrite", On = true, Dir = overwrite });

            var profileObj = gs.Active;
            foreach (var e in all)
            {
                if (!Directory.Exists(e.Dir)) { r.Skipped++; continue; }
                progress?.Invoke("Importing " + e.Name);
                string root = Path.GetFullPath(e.Dir).TrimEnd('\\') + "\\";
                var files = Directory.GetFiles(e.Dir, "*", SearchOption.AllDirectories)
                    .Select(f => f.Substring(root.Length).Replace('\\', '/'))
                    .Where(f => !f.Equals("meta.ini", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (files.Count == 0) { r.Skipped++; continue; }
                var plan = new Placement();
                foreach (string f in files)
                {
                    string rel = f;
                    if (rel.StartsWith("Binaries/WinGDK/", StringComparison.OrdinalIgnoreCase)) rel = Installer.Win64 + rel.Substring(16);
                    plan.Map[f] = rel;
                }
                string meta = Path.Combine(e.Dir, "meta.ini");
                string modid = Ini(meta, "modid");
                var info = new InstallInfo
                {
                    Name = e.Name,
                    Version = Ini(meta, "version"),
                    Site = modid != null && modid != "0" && modid != "-1" ? "nexus" : "mo2",
                    SiteGame = "runescapedragonwilds",
                    SiteModId = modid != null && modid != "0" && modid != "-1" ? modid : null,
                    Url = Ini(meta, "url"),
                };
                if (info.Site == "mo2") info.SiteModId = null;
                try
                {
                    var mod = Installer.Install(gs, e.Dir, plan, info, "MO2: " + e.Name);
                    if (profileObj != null)
                    {
                        if (e.On) { if (!profileObj.Enabled.Contains(mod.Key)) profileObj.Enabled.Add(mod.Key); r.Enabled++; }
                        else profileObj.Enabled.Remove(mod.Key);
                    }
                    r.Mods++;
                    r.Files += mod.Files.Count;
                }
                catch (Exception ex) { r.Problems.Add(e.Name + ": " + ex.Message); }
            }
            return r;
        }
            static readonly System.Web.Script.Serialization.JavaScriptSerializer Json = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public sealed class VortexSetup
        {
            public string Staging;
            public Dictionary<string, Dictionary<string, string>> Deployed = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        }

        static IEnumerable<string> ManifestDirs(Game g)
        {
            string parent = Path.GetDirectoryName(g.Root);
            var dirs = new List<string> { parent, g.Root, g.BinDir, Path.Combine(g.BinDir, "ue4ss"), g.ModsDir,
                Path.Combine(g.Root, "Content", "Paks"), Path.Combine(g.Root, "Content", "Paks", "~mods"), Path.Combine(g.Root, "Content", "Paks", "LogicMods") };
            return dirs.Where(d => d != null && Directory.Exists(d)).Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public static VortexSetup FindVortex(Game g, string stagingOverride = null)
        {
            var v = new VortexSetup();
            if (g != null)
                foreach (string dir in ManifestDirs(g))
                    foreach (string f in Directory.GetFiles(dir, "vortex.deployment*.json"))
                    {
                        try
                        {
                            var d = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(f));
                            if (d == null) continue;
                            if (v.Staging == null && d.TryGetValue("stagingPath", out object sp) && sp is string sps && Directory.Exists(sps)) v.Staging = sps;
                            if (!d.TryGetValue("files", out object fo)) continue;
                            var arr = J.Arr(fo);
                            string root = Path.GetFullPath(g.Root).TrimEnd('\\') + "\\";
                            foreach (var e in arr.OfType<Dictionary<string, object>>())
                            {
                                string rel = e.TryGetValue("relPath", out object r) ? r as string : null;
                                string src = e.TryGetValue("source", out object so) ? so as string : null;
                                if (rel == null || src == null) continue;
                                string abs = Path.GetFullPath(Path.Combine(dir, rel.Replace('/', '\\')));
                                if (!abs.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                                if (!v.Deployed.TryGetValue(src, out var map)) v.Deployed[src] = map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                map[rel.Replace('\\', '/')] = abs.Substring(root.Length).Replace('\\', '/');
                            }
                        }
                        catch { }
                    }
            if (stagingOverride != null && Directory.Exists(stagingOverride)) v.Staging = stagingOverride;
            if (v.Staging == null)
            {
                string def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vortex", NexusData.GameDomain, "mods");
                if (Directory.Exists(def)) v.Staging = def;
            }
            bool anyStaged = v.Staging != null && Directory.EnumerateDirectories(v.Staging).Any(d => !Path.GetFileName(d).StartsWith("__"));
            return !anyStaged && v.Deployed.Count == 0 ? null : v;
        }

        public static ImportResult ImportVortex(GameState gs, VortexSetup v, Action<string> progress = null)
        {
            var r = new ImportResult();
            if (v?.Staging == null || !Directory.Exists(v.Staging)) throw new DirectoryNotFoundException("Vortex's mod staging folder wasn't found. Pick it yourself (Vortex > Settings > Mods > Mod Staging Folder).");
            var profile = gs.Active;
            foreach (string dir in Directory.GetDirectories(v.Staging).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string folder = Path.GetFileName(dir);
                if (folder.StartsWith("__") || folder.StartsWith(".")) continue;
                string root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
                var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                    .Select(f => f.Substring(root.Length).Replace('\\', '/'))
                    .Where(f => !f.StartsWith("__") && !Path.GetFileName(f).StartsWith("vortex.deployment", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (files.Count == 0) { r.Skipped++; continue; }
                progress?.Invoke("Importing " + folder);
                bool on = v.Deployed.TryGetValue(folder, out var map);
                Placement plan;
                if (on && files.All(f => map.ContainsKey(f)))
                {
                    plan = new Placement();
                    foreach (string f in files) plan.Map[f] = map[f];
                }
                else
                {
                    plan = Installer.Plan(files, folder);
                    if (plan.NeedsChoice) { r.Problems.Add(folder + ": couldn't tell where its files go; install it from its archive instead"); r.Skipped++; continue; }
                }
                var info = ManagerService.GuessInfo(folder + ".zip");
                if (info.Site == "file") info.Site = "vortex";
                try
                {
                    var mod = Installer.Install(gs, dir, plan, info, "Vortex: " + folder);
                    if (profile != null)
                    {
                        if (on) { if (!profile.Enabled.Contains(mod.Key)) profile.Enabled.Add(mod.Key); r.Enabled++; }
                        else profile.Enabled.Remove(mod.Key);
                    }
                    r.Mods++;
                    r.Files += mod.Files.Count;
                }
                catch (Exception ex) { r.Problems.Add(folder + ": " + ex.Message); }
            }
            return r;
        }
    }
}
