// Made by Claude Ai & Relic Hunter
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace RelicMods.Manager
{
    public sealed class Game
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Root { get; set; }
        public string BinName { get; set; } = "Win64";
        public string SteamAppId { get; set; }
        public string ActiveProfile { get; set; } = "Modded";
        public string Storage { get; set; }

        [ScriptIgnore] public string ModsDir => Path.Combine(Root, "Binaries", BinName, "ue4ss", "Mods");
        [ScriptIgnore] public string BinDir => Path.Combine(Root, "Binaries", BinName);
    }

    public sealed class ModEntry
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Site { get; set; }
        public string SiteGame { get; set; }
        public string SiteModId { get; set; }
        public string SiteFileId { get; set; }
        public string Url { get; set; }
        public string Archive { get; set; }
        public string InstalledAt { get; set; }
        public string Kind { get; set; }
        public List<string> Files { get; set; } = new List<string>();
        public List<string> Needs { get; set; } = new List<string>();
        public string Picture { get; set; }
        public string LatestVersion { get; set; }
        public string LatestCheckedAt { get; set; }
        public string Author { get; set; }
        public bool? Relic { get; set; }
        public string CrashNote { get; set; }
        public string CrashAt { get; set; }
        public string InfoCheckedAt { get; set; }
        public string IdTriedAt { get; set; }
        public string Notes { get; set; }
        public string Category { get; set; }
        public string CategoryTriedAt { get; set; }
        public string CategoryUser { get; set; }
        public string CategoryAuto { get; set; }
        public double CategoryAutoScore { get; set; }
        public string Summary { get; set; }
    }

    public sealed class Profile
    {
        public string Name { get; set; }
        public List<string> Order { get; set; } = new List<string>();
        public List<string> Enabled { get; set; } = new List<string>();
    }

    public sealed class DeployedFile
    {
        public string Mod { get; set; }
        public string Rel { get; set; }
        public string Id { get; set; }
        public long SrcSize { get; set; }
        public long SrcTicks { get; set; }
        public long TgtSize { get; set; }
        public long TgtTicks { get; set; }
        public bool Copy { get; set; }
        public bool Generated { get; set; }
    }

    public sealed class GameState
    {
        public Game Game { get; set; }
        public List<ModEntry> Mods { get; set; } = new List<ModEntry>();
        public List<Profile> Profiles { get; set; } = new List<Profile>();
        public Dictionary<string, DeployedFile> Deployed { get; set; } = new Dictionary<string, DeployedFile>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Backups { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> CreatedDirs { get; set; } = new List<string>();
        public string DeployedProfile { get; set; }
        public string LastDeploy { get; set; }

        [ScriptIgnore] public Profile Active => Profiles.FirstOrDefault(p => p.Name == Game.ActiveProfile) ?? Profiles.FirstOrDefault();
        public ModEntry Mod(string key) => Mods.FirstOrDefault(m => m.Key == key);
    }

    public sealed class AppState
    {
        public int Schema { get; set; } = 1;
        public string ActiveGame { get; set; }
        public bool Advanced { get; set; }
        public string LibraryView { get; set; } = "gallery";
        public string LastCrashSeen { get; set; }
        public bool WatchDownloads { get; set; } = true;
        public List<DownloadRec> Downloads { get; set; } = new List<DownloadRec>();
        public List<GameState> Games { get; set; } = new List<GameState>();

        [ScriptIgnore] public GameState Current => Games.FirstOrDefault(g => g.Game.Id == ActiveGame) ?? Games.FirstOrDefault();
    }

    public sealed class DownloadRec
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string File { get; set; }
        public string Path { get; set; }
        public string State { get; set; }
        public string Reason { get; set; }
        public string At { get; set; }
        public string Site { get; set; }
        public string ModId { get; set; }
        public string FileId { get; set; }
        public string Version { get; set; }
        public string Picture { get; set; }
        public long Received { get; set; }
        public long Total { get; set; }
    }

    public static class J
    {
        public static List<object> Arr(object o)
        {
            if (o == null || o is string) return new List<object>();
            if (o is System.Collections.IEnumerable e && !(o is IDictionary<string, object>)) return e.Cast<object>().ToList();
            return new List<object>();
        }
    }

    public static class Store
    {
        public static string Home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RelicMods");
        static string StateFile => Path.Combine(Home, "state.json");
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static AppState Load()
        {
            try
            {
                if (File.Exists(StateFile))
                {
                    var s = Json.Deserialize<AppState>(File.ReadAllText(StateFile, Encoding.UTF8));
                    if (s != null)
                    {
                        foreach (var g in s.Games)
                        {
                            g.Deployed = new Dictionary<string, DeployedFile>(g.Deployed ?? new Dictionary<string, DeployedFile>(), StringComparer.OrdinalIgnoreCase);
                            g.Backups = new Dictionary<string, string>(g.Backups ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                        }
                        return s;
                    }
                }
            }
            catch { }
            return new AppState();
        }

        public static void Save(AppState s)
        {
            Directory.CreateDirectory(Home);
            string tmp = StateFile + ".tmp";
            File.WriteAllText(tmp, Json.Serialize(s), new UTF8Encoding(false));
            if (File.Exists(StateFile))
            {
                string bak = StateFile + ".bak";
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(StateFile, bak);
            }
            File.Move(tmp, StateFile);
        }

        public static string Slug(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
                sb.Append(char.IsLetterOrDigit(c) ? c : (c == ' ' || c == '-' || c == '_' || c == '.') ? '_' : '\0');
            string r = sb.ToString().Replace("\0", "");
            while (r.Contains("__")) r = r.Replace("__", "_");
            r = r.Trim('_');
            if (r.Length > 48) r = r.Substring(0, 48);
            return r.Length == 0 ? "Mod" : r;
        }

        public static string Now() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        static readonly object LogLock = new object();
        public static void Log(string text)
        {
            try
            {
                lock (LogLock)
                {
                    Directory.CreateDirectory(Home);
                    string f = Path.Combine(Home, "relicmods.log");
                    if (File.Exists(f) && new FileInfo(f).Length > 1_000_000) File.Delete(f);
                    File.AppendAllText(f, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
