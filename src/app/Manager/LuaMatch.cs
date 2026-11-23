// Made by Claude Ai & Relic Hunter
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RelicMods.Manager
{
    public static class LuaMatch
    {
        public struct Proto { public int Line, Last, Upvalues, Consts, Code; }

        public static List<Proto> ScanProtos(byte[] b)
        {
            var list = new List<Proto>();
            var seen = new HashSet<long>();
            for (int o = 0; o + 0x40 <= b.Length; o++)
            {
                if (b[o + 8] != 0x0A || b[o + 0x0B] > 1) continue;
                int maxstack = b[o + 0x0C];
                if (maxstack < 2 || b[o + 0x0A] > 40) continue;
                int up = BitConverter.ToInt32(b, o + 0x10), k = BitConverter.ToInt32(b, o + 0x14), code = BitConverter.ToInt32(b, o + 0x18), lineinfo = BitConverter.ToInt32(b, o + 0x1C);
                int sizep = BitConverter.ToInt32(b, o + 0x20), locs = BitConverter.ToInt32(b, o + 0x24), abs = BitConverter.ToInt32(b, o + 0x28);
                int ld = BitConverter.ToInt32(b, o + 0x2C), lld = BitConverter.ToInt32(b, o + 0x30);
                if (up < 0 || up > 255 || k < 0 || k > 100000 || code < 1 || code > 200000 || lineinfo != code || sizep < 0 || sizep > 20000) continue;
                if (locs < 0 || locs > 20000 || abs < 0 || abs > code || ld < 1 || ld > 60000 || lld < ld || lld - ld > 20000) continue;
                long kptr = BitConverter.ToInt64(b, o + 0x38);
                if (kptr == 0 && k > 0 || (kptr & 7) != 0) continue;
                long key = ((long)ld << 32) | (uint)lld;
                if (!seen.Add(key ^ ((long)code << 48))) continue;
                list.Add(new Proto { Line = ld, Last = lld, Upvalues = up, Consts = k, Code = code });
            }
            return list;
        }

        public static List<KeyValuePair<int, int>> FunctionRanges(string src)
        {
            var ranges = new List<KeyValuePair<int, int>>();
            var stack = new Stack<KeyValuePair<string, int>>();
            int line = 1, i = 0, n = src.Length;
            while (i < n)
            {
                char c = src[i];
                if (c == '\n') { line++; i++; continue; }
                if (c == '-' && i + 1 < n && src[i + 1] == '-')
                {
                    int lvl = LongBracket(src, i + 2);
                    if (lvl >= 0) { i = SkipLong(src, i + 2, lvl, ref line); continue; }
                    while (i < n && src[i] != '\n') i++;
                    continue;
                }
                if (c == '[') { int lvl = LongBracket(src, i); if (lvl >= 0) { i = SkipLong(src, i, lvl, ref line); continue; } }
                if (c == '"' || c == '\'')
                {
                    i++;
                    while (i < n && src[i] != c) { if (src[i] == '\\') i++; else if (src[i] == '\n') line++; i++; }
                    i++;
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    int s = i;
                    while (i < n && (char.IsLetterOrDigit(src[i]) || src[i] == '_')) i++;
                    if (s > 0 && (src[s - 1] == '.' || src[s - 1] == ':') && !(s > 1 && src[s - 2] == '.')) continue;
                    string w = src.Substring(s, i - s);
                    switch (w)
                    {
                        case "function": case "if": case "do": case "repeat": stack.Push(new KeyValuePair<string, int>(w, line)); break;
                        case "end":
                            if (stack.Count > 0) { var t = stack.Pop(); if (t.Key == "function") ranges.Add(new KeyValuePair<int, int>(t.Value, line)); }
                            break;
                        case "until": if (stack.Count > 0) stack.Pop(); break;
                    }
                    continue;
                }
                i++;
            }
            return ranges;
        }

        static int LongBracket(string s, int i)
        {
            if (i >= s.Length || s[i] != '[') return -1;
            int j = i + 1, lvl = 0;
            while (j < s.Length && s[j] == '=') { lvl++; j++; }
            return j < s.Length && s[j] == '[' ? lvl : -1;
        }

        static int SkipLong(string s, int i, int lvl, ref int line)
        {
            string close = "]" + new string('=', lvl) + "]";
            int end = s.IndexOf(close, i + 2 + lvl, StringComparison.Ordinal);
            if (end < 0) end = s.Length - close.Length;
            for (int k = i; k < end; k++) if (s[k] == '\n') line++;
            return end + close.Length;
        }

        public sealed class Hit { public string Folder, File; public double Score; public int Matches; }

        public static List<Hit> Match(List<Proto> protos, string modsDir)
        {
            var hits = new List<Hit>();
            if (protos.Count == 0 || string.IsNullOrEmpty(modsDir) || !Directory.Exists(modsDir)) return hits;
            var pairs = protos.Select(p => new KeyValuePair<int, int>(p.Line, p.Last)).Distinct().ToList();
            foreach (string dir in Directory.GetDirectories(modsDir))
            {
                string folder = Path.GetFileName(dir);
                if (folder.Equals("shared", StringComparison.OrdinalIgnoreCase)) continue;
                string scripts = Path.Combine(dir, "Scripts");
                if (!Directory.Exists(scripts)) continue;
                foreach (string f in Directory.GetFiles(scripts, "*.lua", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (new FileInfo(f).Length > 3_000_000) continue;
                        var set = new HashSet<KeyValuePair<int, int>>(FunctionRanges(File.ReadAllText(f)));
                        int m = 0; double score = 0;
                        foreach (var p in pairs)
                            if (set.Contains(p)) { m++; score += 1 + Math.Min(p.Value - p.Key, 30) / 10.0; }
                        if (m > 0) hits.Add(new Hit { Folder = folder, File = f.Substring(dir.Length + 1), Score = score, Matches = m });
                    }
                    catch { }
                }
            }
            var shared = new HashSet<string>(hits.GroupBy(h => Path.GetFileName(h.File).ToLowerInvariant() + "|" + h.Matches).Where(g => g.Select(x => x.Folder).Distinct().Count() > 1).Select(g => g.Key));
            return hits.Where(h => !shared.Contains(Path.GetFileName(h.File).ToLowerInvariant() + "|" + h.Matches)).OrderByDescending(h => h.Score).ToList();
        }

        public static List<string> ErrorStrings(string text)
        {
            var list = new List<string>();
            foreach (Match m in Regex.Matches(text, @"([A-Za-z0-9_\-]+\.lua):(\d+): ([ -~]{6,160})"))
            {
                string msg = m.Groups[1].Value + " line " + m.Groups[2].Value + ": " + m.Groups[3].Value.Trim();
                if (!list.Contains(msg)) list.Add(msg);
                if (list.Count >= 5) break;
            }
            return list;
        }

        static readonly Dictionary<ulong, string> Ue4ss301 = new Dictionary<ulong, string>
        {
            { 0x2237FD, "A mod read a value from a game object that had already been removed." },
            { 0x2AC4B3, "A mod's script memory got mixed up (usually two things running a mod's code at the same moment)." },
            { 0x2AD7EA, "A mod used a game object that had already been removed." },
            { 0xC60148, "A mod's script memory got mixed up, usually by a key press handled at the wrong moment." },
            { 0xC600CD, "A mod's script memory got mixed up, usually by a key press handled at the wrong moment." },
            { 0xC5F53D, "A mod called the game on something that had already been removed." },
            { 0xC6DCA8, "A mod called the game on something that had already been removed." },
            { 0xC6DCD0, "A mod called the game on something that had already been removed." },
            { 0xC6E899, "A mod searched the world while it was still loading." },
            { 0xC6D5C0, "A mod called the game on something that had already been removed." },
            { 0xC6DAA3, "A mod called the game on something that had already been removed." },
            { 0xC63D4E, "A mod passed the wrong kind of value to the game." },
            { 0xC95405, "A mod sent UE4SS a value that was too big (UE4SS stopped the game to stay safe)." },
        };

        public static string KnownUe4ss(ulong offset) => Ue4ss301.TryGetValue(offset, out string t) ? t : null;

        public static string ExceptionText(uint code, string address)
        {
            switch (code)
            {
                case 0xC0000005: return "It tried to use memory that was already gone" + (address != null ? " (" + address + ")" : "") + ".";
                case 0xC0000409: return "A safety check stopped the game (stack overrun).";
                case 0xC00000FD: return "Something called itself forever (stack overflow).";
                case 0xE06D7363: return "An unhandled C++ error.";
                case 0x80000003: return "The game hit an internal check (assert).";
                case 0xC000001D: return "It ran an invalid instruction.";
                default: return code == 0 ? null : "Windows error 0x" + code.ToString("X8") + ".";
            }
        }
    }
}
