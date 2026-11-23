// Made by Claude Ai & Relic Hunter
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace RelicMods.Manager
{
    public static class Ue4ssCheck
    {
        public const string OursSha = "3843d81a8280096b";
        public const string OursName = "UE4SS 4.0";
        public const int NexusId = 935;
        static readonly Dictionary<string, object> Cache = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public static string DllPath(string binDir)
        {
            if (string.IsNullOrEmpty(binDir)) return null;
            foreach (string p in new[] { Path.Combine(binDir, "ue4ss", "UE4SS.dll"), Path.Combine(binDir, "UE4SS.dll") })
                if (File.Exists(p)) return p;
            return null;
        }

        public static object Info(string binDir)
        {
            string dll = DllPath(binDir);
            if (dll == null) return new { found = false, ours = false, version = (string)null };
            var fi = new FileInfo(dll);
            string key = dll + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            lock (Cache) if (Cache.TryGetValue(key, out object hit)) return hit;
            string sha = "";
            try
            {
                using (var s = new FileStream(dll, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var h = SHA256.Create())
                    sha = BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
            }
            catch { }
            bool ours = sha.StartsWith(OursSha);
            string version = ours ? "4.0.0" : Version(binDir, dll);
            var info = new { found = true, ours, version, sha = sha.Length > 16 ? sha.Substring(0, 16) : sha };
            lock (Cache) Cache[key] = info;
            return info;
        }

        static string Version(string binDir, string dll)
        {
            foreach (string log in new[] { Path.Combine(Path.GetDirectoryName(dll), "UE4SS.log"), Path.Combine(binDir, "ue4ss", "UE4SS.log") })
            {
                try
                {
                    if (!File.Exists(log)) continue;
                    foreach (string l in File.ReadLines(log).Take(6))
                    {
                        var m = Regex.Match(l, @"UE4SS - (v[\d.]+[^\r\n]*?)(?:\s*\[|$)");
                        if (m.Success) return m.Groups[1].Value.Replace("Beta #0 - Git SHA #", "").Replace("  ", " ").Trim();
                    }
                }
                catch { }
            }
            try
            {
                var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(dll);
                if (!string.IsNullOrEmpty(v.ProductVersion)) return v.ProductVersion;
            }
            catch { }
            return "an older version";
        }
    }
}
