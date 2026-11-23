// Made by Claude Ai & Relic Hunter
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace RelicMods.Manager
{
    public sealed class MenuMod
    {
        public string Folder, Id, Name, Author, Manifest, ConfigFile, Format, Error;
        public bool Ours, Enabled;
        public Dictionary<string, string> Values = new Dictionary<string, string>();
        public Dictionary<string, string> Types = new Dictionary<string, string>();
    }

    public static class OtherRelics
    {
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        const int MaxFile = 256 * 1024;

        public static bool IsRelicAuthor(string author) =>
            !string.IsNullOrEmpty(author) && author.IndexOf("relic", StringComparison.OrdinalIgnoreCase) >= 0;

        public static string ManifestPath(string modDir)
        {
            foreach (string n in new[] { "modmenu.json", "modmenu.txt" })
            {
                string p = Path.Combine(modDir, n);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        public static List<MenuMod> Scan(string modsDir)
        {
            var list = new List<MenuMod>();
            if (string.IsNullOrEmpty(modsDir) || !Directory.Exists(modsDir)) return list;
            foreach (string dir in Directory.GetDirectories(modsDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(Path.Combine(dir, "relic.json"))) continue;
                string mf = ManifestPath(dir);
                if (mf == null) continue;
                var m = Load(dir, mf);
                if (m != null) list.Add(m);
            }
            return list;
        }

        public static MenuMod Load(string dir, string manifestPath)
        {
            var m = new MenuMod { Folder = Path.GetFileName(dir), Enabled = File.Exists(Path.Combine(dir, "enabled.txt")) };
            try
            {
                if (new FileInfo(manifestPath).Length > MaxFile) { m.Error = "modmenu file is too big"; return m; }
                m.Manifest = File.ReadAllText(manifestPath, Encoding.UTF8).TrimStart('﻿');
                var d = Json.Deserialize<Dictionary<string, object>>(m.Manifest);
                if (d == null) { m.Error = "modmenu file is empty"; return m; }
                m.Id = Str(d, "id") ?? m.Folder;
                m.Name = Str(d, "name") ?? m.Folder;
                m.Author = Str(d, "author");
                m.Ours = IsRelicAuthor(m.Author);
                var cfg = d.TryGetValue("config", out object c) ? c as Dictionary<string, object> : null;
                m.Format = (cfg != null ? Str(cfg, "format") : null) ?? "kv";
                m.ConfigFile = (cfg != null ? Str(cfg, "file") : null) ?? (m.Format == "lua" ? "Scripts/main.lua" : "config.txt");
                var keys = new List<string>();
                if (d.TryGetValue("settings", out object so))
                    foreach (var s in J.Arr(so).OfType<Dictionary<string, object>>())
                    {
                        string k = Str(s, "key"), t = Str(s, "type");
                        if (k == null || t == null || t == "header" || t == "button") continue;
                        keys.Add(k);
                        m.Types[k] = t;
                    }
                string file = ConfigPath(dir, m.ConfigFile);
                if (file == null) { m.Error = "its config file is outside the mod folder"; return m; }
                m.Values = ReadValues(file, m.Format, keys);
            }
            catch (Exception ex) { m.Error = "couldn't read modmenu file: " + ex.Message; }
            return m;
        }

        static string Str(Dictionary<string, object> d, string k) =>
            d.TryGetValue(k, out object v) && v != null && !(v is Dictionary<string, object>) && !(v is System.Collections.IEnumerable && !(v is string)) ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;

        public static string ConfigPath(string modDir, string rel)
        {
            string root = Path.GetFullPath(modDir).TrimEnd('\\') + "\\";
            string full = Path.GetFullPath(Path.Combine(modDir, (rel ?? "config.txt").Replace('/', '\\')));
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        static readonly Regex KvLine = new Regex(@"^(\s*)([A-Za-z0-9_.\-]+)(\s*=\s*)(.*?)(\s+[#;].*)?\s*$");

        public static Dictionary<string, string> ReadValues(string file, string format, IEnumerable<string> keys)
        {
            var vals = new Dictionary<string, string>();
            if (!File.Exists(file) || new FileInfo(file).Length > MaxFile) return vals;
            string text = File.ReadAllText(file, Encoding.UTF8).TrimStart('﻿');
            var want = new HashSet<string>(keys);
            if (format == "json")
            {
                var d = Json.Deserialize<Dictionary<string, object>>(text) ?? new Dictionary<string, object>();
                foreach (var kv in d)
                    if (want.Contains(kv.Key) && kv.Value != null && !(kv.Value is Dictionary<string, object>) && !(kv.Value is System.Collections.IEnumerable && !(kv.Value is string)))
                        vals[kv.Key] = kv.Value is bool b ? (b ? "true" : "false") : Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
                return vals;
            }
            if (format == "lua")
            {
                foreach (string k in want)
                {
                    var m = LuaFind(text, k);
                    if (m.Success) vals[k] = Unquote(m.Groups[3].Value);
                }
                return vals;
            }
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                string t = line.TrimStart();
                if (t.StartsWith("#") || t.StartsWith(";") || t.StartsWith("[")) continue;
                var m = KvLine.Match(line);
                if (!m.Success) continue;
                string k = m.Groups[2].Value;
                if (want.Contains(k) && !vals.ContainsKey(k)) vals[k] = Unquote(m.Groups[4].Value.Trim());
            }
            return vals;
        }

        static Match LuaFind(string text, string key) =>
            Regex.Match(text, @"^([ \t]{0,8}(?:local[ \t]+)?)(" + Regex.Escape(key) + @"[ \t]*=[ \t]*)(true|false|-?\d+(?:\.\d+)?|""(?:[^""\\\r\n]|\\.)*""|'(?:[^'\\\r\n]|\\.)*')", RegexOptions.Multiline);

        static string Unquote(string v)
        {
            if (v.Length >= 2 && ((v[0] == '"' && v[v.Length - 1] == '"') || (v[0] == '\'' && v[v.Length - 1] == '\'')))
                return v.Substring(1, v.Length - 2).Replace("\\\"", "\"").Replace("\\'", "'").Replace("\\\\", "\\");
            return v;
        }

        public static string Text(object v)
        {
            if (v == null) return "";
            if (v is bool b) return b ? "true" : "false";
            if (v is string s) return s;
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        static bool LooksNumber(string s) => Regex.IsMatch(s ?? "", @"^-?\d+(\.\d+)?$");

        public static void WriteValues(string file, string format, Dictionary<string, object> changes)
        {
            if (changes == null || changes.Count == 0) return;
            string text = File.Exists(file) ? File.ReadAllText(file, Encoding.UTF8).TrimStart('﻿') : "";
            string nl = text.Contains("\r\n") ? "\r\n" : "\n";
            if (format == "json")
            {
                var d = (text.Trim().Length > 0 ? Json.Deserialize<Dictionary<string, object>>(text) : null) ?? new Dictionary<string, object>();
                foreach (var kv in changes) d[kv.Key] = kv.Value;
                Save(file, Json.Serialize(d));
                return;
            }
            if (format == "lua")
            {
                foreach (var kv in changes)
                {
                    var m = LuaFind(text, kv.Key);
                    if (!m.Success) continue;
                    string orig = m.Groups[3].Value, val = Text(kv.Value);
                    bool quoted = orig.StartsWith("\"") || orig.StartsWith("'");
                    if (!quoted && !(val == "true" || val == "false" || LooksNumber(val))) continue;
                    string lit = quoted ? "\"" + val.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" : val;
                    text = text.Substring(0, m.Groups[3].Index) + lit + text.Substring(m.Groups[3].Index + m.Groups[3].Length);
                }
                Save(file, text);
                return;
            }
            var lines = text.Length == 0 ? new List<string>() : text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            if (lines.Count > 0 && lines[lines.Count - 1] == "") lines.RemoveAt(lines.Count - 1);
            var done = new HashSet<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                string t = lines[i].TrimStart();
                if (t.StartsWith("#") || t.StartsWith(";") || t.StartsWith("[")) continue;
                var m = KvLine.Match(lines[i]);
                if (!m.Success) continue;
                string k = m.Groups[2].Value;
                if (!changes.ContainsKey(k) || done.Contains(k)) continue;
                string old = m.Groups[4].Value.Trim();
                string val = Text(changes[k]);
                if (old.Length >= 2 && (old[0] == '"' || old[0] == '\'')) val = old[0] + val + old[0];
                lines[i] = m.Groups[1].Value + k + m.Groups[3].Value + val + m.Groups[5].Value;
                done.Add(k);
            }
            foreach (var kv in changes) if (!done.Contains(kv.Key)) lines.Add(kv.Key + " = " + Text(kv.Value));
            Save(file, string.Join(nl, lines) + nl);
        }

        static void Save(string file, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string tmp = file + ".relictmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(file)) File.Delete(file);
            File.Move(tmp, file);
        }
    }
}
