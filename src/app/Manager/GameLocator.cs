// Made by Claude Ai & Relic Hunter
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace RelicMods.Manager
{
    public static class GameLocator
    {
        public const string SteamAppId = "1374490";

        public static List<Game> Find()
        {
            var found = new List<Game>();
            foreach (string lib in SteamLibraries())
            {
                string root = Path.Combine(lib, "steamapps", "common", "RSDragonwilds", "RSDragonwilds");
                var g = FromProject(root, "steam");
                if (g != null) { g.SteamAppId = SteamAppId; found.Add(g); }
            }
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    string xbox = Path.Combine(d.RootDirectory.FullName, "XboxGames");
                    if (!Directory.Exists(xbox)) continue;
                    foreach (string dir in Directory.GetDirectories(xbox))
                    {
                        if (dir.IndexOf("dragonwilds", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        foreach (string proj in SearchProject(dir, 4))
                        {
                            var g = FromProject(proj, "gamepass");
                            if (g != null) found.Add(g);
                        }
                    }
                }
                catch { }
            }
            return found.GroupBy(g => g.Root.ToLowerInvariant()).Select(x => x.First()).ToList();
        }

        public static Game FromFolder(string picked)
        {
            if (string.IsNullOrEmpty(picked) || !Directory.Exists(picked)) return null;
            foreach (string proj in new[] { picked, Path.Combine(picked, "RSDragonwilds") }.Concat(SearchProject(picked, 4)))
            {
                var g = FromProject(proj, "custom");
                if (g != null)
                {
                    if (Directory.GetFiles(Path.Combine(proj, "Binaries", g.BinName), "*Server*.exe").Any()) g.Kind = "server";
                    return g;
                }
            }
            return null;
        }

        static IEnumerable<string> SearchProject(string dir, int depth)
        {
            if (depth < 0) yield break;
            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch { yield break; }
            if (Directory.Exists(Path.Combine(dir, "Binaries")) && Directory.Exists(Path.Combine(dir, "Content"))) yield return dir;
            foreach (string s in subs)
            {
                string n = Path.GetFileName(s);
                if (n.Equals("Engine", StringComparison.OrdinalIgnoreCase) || n.Equals("Binaries", StringComparison.OrdinalIgnoreCase) || n.Equals("Content", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (string r in SearchProject(s, depth - 1)) yield return r;
            }
        }

        static Game FromProject(string proj, string kind)
        {
            try
            {
                if (!Directory.Exists(proj)) return null;
                foreach (string bin in new[] { "Win64", "WinGDK" })
                {
                    string b = Path.Combine(proj, "Binaries", bin);
                    if (!Directory.Exists(b)) continue;
                    if (!Directory.GetFiles(b, "*.exe").Any(f => Path.GetFileName(f).StartsWith("RSDragonwilds", StringComparison.OrdinalIgnoreCase))) continue;
                    string full = Path.GetFullPath(proj);
                    string label = kind == "steam" ? "Steam" : kind == "gamepass" ? "Game Pass" : kind == "server" ? "Dedicated server" : "Custom folder";
                    return new Game
                    {
                        Id = Store.Slug(kind + "_" + Fnv(full.ToLowerInvariant().TrimEnd('\\')).ToString("x8")),
                        Name = "RuneScape: Dragonwilds (" + label + ")",
                        Kind = bin == "WinGDK" && kind == "custom" ? "gamepass" : kind,
                        Root = full,
                        BinName = bin,
                    };
                }
            }
            catch { }
            return null;
        }

        static uint Fnv(string s)
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return h;
        }

        public static List<string> SteamLibraries()
        {
            var libs = new List<string>();
            var roots = new List<string>();
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                foreach (string key in new[] { @"Software\Valve\Steam", @"Software\WOW6432Node\Valve\Steam" })
                {
                    try
                    {
                        using (var k = hive.OpenSubKey(key))
                        {
                            foreach (string name in new[] { "SteamPath", "InstallPath" })
                            {
                                if (k?.GetValue(name) is string p && !string.IsNullOrEmpty(p)) roots.Add(p.Replace('/', '\\'));
                            }
                        }
                    }
                    catch { }
                }
            }
            roots.Add(@"C:\Program Files (x86)\Steam");
            foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(root)) continue;
                libs.Add(root);
                string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                try
                {
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                }
                catch { }
            }
            return libs.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string DefaultStorage(Game g)
        {
            try
            {
                string drive = Path.GetPathRoot(g.Root);
                string sys = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
                if (!string.Equals(drive, sys, StringComparison.OrdinalIgnoreCase))
                {
                    string d = Path.Combine(drive, "RelicMods", g.Id);
                    Directory.CreateDirectory(d);
                    return d;
                }
            }
            catch { }
            string local = Path.Combine(Store.Home, "games", g.Id);
            Directory.CreateDirectory(local);
            return local;
        }
    }
}
