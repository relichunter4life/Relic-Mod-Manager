// Made by Claude Ai & Relic Hunter
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace RelicMods.Manager
{
    public static class SourceDetect
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);

        public static readonly Dictionary<string, string> KnownRelic = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "RSSpawner", "933" }, { "MagneticPickUp", "934" }, { "CustomWorldEdits", "932" }, { "WeddingBells", "927" },
            { "LivingWildsAudio", "918" }, { "PenguinAssassin", "894" }, { "OutfitSetBags", "883" }, { "RelicHunterUI", "869" },
            { "PerksOfAshenfall", "852" }, { "CharacterOrigins", "747" }, { "CapeEffects", "709" }, { "EnemiesReIgnited", "706" },
            { "RelicPhotoMode", "801" }, { "DynamicDisplayEnhancements", "802" },
        };

        public static string ReadZone(string file)
        {
            try
            {
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) return null;
                using (var h = CreateFile(file + ":Zone.Identifier", 0x80000000, 7, IntPtr.Zero, 3, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) return null;
                    using (var s = new FileStream(h, FileAccess.Read))
                    using (var r = new StreamReader(s, Encoding.UTF8))
                        return r.ReadToEnd();
                }
            }
            catch { return null; }
        }

        public static bool FromZone(string archive, InstallInfo info)
        {
            string z = ReadZone(archive);
            if (z == null) return false;
            foreach (string key in new[] { "ReferrerUrl", "HostUrl" })
            {
                var m = Regex.Match(z, "^" + key + @"=(\S+)", RegexOptions.Multiline);
                if (!m.Success) continue;
                string url = m.Groups[1].Value;
                var nx = Regex.Match(url, @"nexusmods\.com/(?:games/)?([^/?#]+)/mods/(\d+)", RegexOptions.IgnoreCase);
                if (nx.Success)
                {
                    Set(info, "nexus", nx.Groups[1].Value, nx.Groups[2].Value, "https://www.nexusmods.com/" + nx.Groups[1].Value + "/mods/" + nx.Groups[2].Value);
                    var fid = Regex.Match(url, @"file_id=(\d+)");
                    if (fid.Success) info.SiteFileId = fid.Groups[1].Value;
                    return true;
                }
                var cdn = Regex.Match(url, @"nexus-cdn\.com/(\d+)/(\d+)/", RegexOptions.IgnoreCase);
                if (cdn.Success && cdn.Groups[1].Value == NexusData.GameId)
                {
                    Set(info, "nexus", NexusData.GameDomain, cdn.Groups[2].Value, "https://www.nexusmods.com/" + NexusData.GameDomain + "/mods/" + cdn.Groups[2].Value);
                    return true;
                }
                var cf = Regex.Match(url, @"curseforge\.com/([^/?#]+)/([^/?#]+)/([^/?#]+)", RegexOptions.IgnoreCase);
                if (cf.Success && info.Site != "nexus")
                {
                    info.Site = "curseforge";
                    info.SiteGame = cf.Groups[1].Value;
                    info.SiteModId = cf.Groups[3].Value;
                    info.Url = "https://www.curseforge.com/" + cf.Groups[1].Value + "/" + cf.Groups[2].Value + "/" + cf.Groups[3].Value;
                    return true;
                }
            }
            return false;
        }

        static void Set(InstallInfo info, string site, string game, string id, string url)
        {
            info.Site = site; info.SiteGame = game; info.SiteModId = id; info.Url = url;
        }

        static List<string> Words(string s) =>
            Regex.Matches((s ?? "").ToLowerInvariant(), @"[a-z0-9]+").Cast<Match>().Select(m => m.Value).ToList();

        public static string Loose(string s) => string.Concat(Words(s));

        public static bool NameMatch(string ours, string theirs)
        {
            var a = Words(Regex.Replace(ours ?? "", @"([a-z])([A-Z])", "$1 $2"));
            if (a.Any(w => !w.All(char.IsDigit))) a = a.Where(w => !w.All(char.IsDigit) && !Regex.IsMatch(w, @"^v\d+$")).ToList();
            var b = Words(theirs);
            if (a.Count == 0 || b.Count == 0) return false;
            if (Loose(ours) == Loose(theirs)) return true;
            return a.All(w => b.Any(t => t == w || (w.Length >= 3 && t.StartsWith(w)) || (t.Length >= 4 && w.StartsWith(t))));
        }

        public static bool AuthorMatch(string known, NexusData.ModInfo m)
        {
            if (string.IsNullOrEmpty(known)) return true;
            string k = Loose(known);
            foreach (string x in new[] { m.Author, m.Uploader })
            {
                string l = Loose(x);
                if (l.Length >= 3 && (k.Contains(l) || l.Contains(k))) return true;
            }
            return false;
        }

        public static string ModFolder(ModEntry m)
        {
            foreach (string f in m.Files)
            {
                var x = Regex.Match(f, @"ue4ss/Mods/([^/]+)/", RegexOptions.IgnoreCase);
                if (x.Success && !x.Groups[1].Value.Equals("shared", StringComparison.OrdinalIgnoreCase)) return x.Groups[1].Value;
            }
            return null;
        }
    }
}
