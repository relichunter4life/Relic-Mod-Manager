// Made by Claude Ai & Relic Hunter
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RelicMods.Manager
{
    public sealed class Suspect
    {
        public string Folder { get; set; }
        public string Name { get; set; }
        public string Why { get; set; }
        public string Confidence { get; set; }
        public string ModKey { get; set; }
        public string ReportUrl { get; set; }
        public bool Relic { get; set; }
    }

    public sealed class CrashReport
    {
        public string When { get; set; }
        public string Source { get; set; }
        public string Kind { get; set; }
        public string Cause { get; set; }
        public string Error { get; set; }
        public string Module { get; set; }
        public string Detail { get; set; }
        public string LuaError { get; set; }
        public List<Suspect> Suspects { get; set; } = new List<Suspect>();
        public List<string> Evidence { get; set; } = new List<string>();
    }

    public static class CrashReader
    {
        static readonly string[] SystemModules = { "ntdll", "kernelbase", "kernel32", "ucrtbase", "vcruntime140", "vcruntime140_1", "msvcp140", "user32", "win32u", "combase", "rpcrt4", "ws2_32", "gdi32" };
        static readonly string[] GpuModules = { "nvwgf2umx", "nvlddmkm", "amdxx64", "amdxc64", "atidxx64", "igd12umd64", "igdumdim64", "d3d12core", "d3d12", "dxgi", "nvgpucomp64" };

        public static string CrashesDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RSDragonwilds", "Saved", "Crashes");

        public static List<string> NewCrashes(DateTime sinceUtc, string ue4ssDir)
        {
            var found = new List<KeyValuePair<DateTime, string>>();
            try
            {
                if (Directory.Exists(CrashesDir))
                    foreach (string d in Directory.GetDirectories(CrashesDir))
                    {
                        var t = Directory.GetLastWriteTimeUtc(d);
                        if (t > sinceUtc) found.Add(new KeyValuePair<DateTime, string>(t, d));
                    }
                if (ue4ssDir != null && Directory.Exists(ue4ssDir))
                    foreach (string f in Directory.GetFiles(ue4ssDir, "crash_*.dmp"))
                    {
                        var t = File.GetLastWriteTimeUtc(f);
                        if (t > sinceUtc) found.Add(new KeyValuePair<DateTime, string>(t, f));
                    }
            }
            catch { }
            return found.OrderByDescending(x => x.Key).Select(x => x.Value).ToList();
        }

        public static List<object> Recent(string ue4ssDir)
        {
            var all = new List<Tuple<DateTime, string, string, long>>();
            try
            {
                if (Directory.Exists(CrashesDir))
                    foreach (string d in Directory.GetDirectories(CrashesDir))
                        all.Add(Tuple.Create(Directory.GetLastWriteTime(d), d, "Game crash folder", Directory.GetFiles(d).Sum(f => new FileInfo(f).Length)));
                if (ue4ssDir != null && Directory.Exists(ue4ssDir))
                    foreach (string f in Directory.GetFiles(ue4ssDir, "crash_*.dmp"))
                        all.Add(Tuple.Create(File.GetLastWriteTime(f), f, "UE4SS crash dump", new FileInfo(f).Length));
            }
            catch { }
            return all.OrderByDescending(x => x.Item1).Take(12).Select(x => (object)new { path = x.Item2, when = x.Item1.ToString("yyyy-MM-dd HH:mm"), kind = x.Item3, mb = Math.Round(x.Item4 / 1048576.0, 1) }).ToList();
        }

        public static string LatestCrash()
        {
            try
            {
                if (!Directory.Exists(CrashesDir)) return null;
                return Directory.GetDirectories(CrashesDir).OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault();
            }
            catch { return null; }
        }

        public static CrashReport Analyze(IEnumerable<string> inputs, string modsDir, string ue4ssLog, Func<string, Suspect> resolve)
        {
            var r = new CrashReport();
            var files = new List<string>();
            foreach (string p in inputs.Where(x => !string.IsNullOrEmpty(x)))
            {
                if (Directory.Exists(p)) files.AddRange(Directory.GetFiles(p, "*", SearchOption.TopDirectoryOnly));
                else if (File.Exists(p)) files.Add(p);
            }
            if (files.Count == 0) { r.Kind = "none"; r.Cause = "No crash files were found there. Drop the crash folder, or its .dmp, .log or .runtime-xml file."; return r; }
            r.Source = string.Join(", ", inputs.Select(Path.GetFileName));
            var when = files.Select(File.GetLastWriteTime).Max();
            r.When = when.ToString("yyyy-MM-dd HH:mm");

            var votes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var why = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void Vote(string folder, double w, string reason)
            {
                folder = (folder ?? "").Trim();
                if (folder.Length == 0 || folder.Equals("shared", StringComparison.OrdinalIgnoreCase) || folder.Equals("ModMenu", StringComparison.OrdinalIgnoreCase) && w < 2) return;
                votes[folder] = (votes.TryGetValue(folder, out double v) ? v : 0) + w;
                if (!why.ContainsKey(folder) || w >= 2) why[folder] = reason;
            }

            string topModule = null, dllMod = null;
            bool tarray = false, oom = false, gpu = false;
            var paks = new List<string>();

            foreach (string f in files.Where(x => x.EndsWith(".runtime-xml", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
            {
                string t = Read(f, 4 << 20);
                var em = Regex.Match(t, @"<ErrorMessage>([^<]*)");
                if (em.Success) r.Error = System.Net.WebUtility.HtmlDecode(em.Groups[1].Value.Trim());
                var pc = Regex.Match(t, @"<PCallStack>([^<]*)");
                if (pc.Success)
                    foreach (Match fm in Regex.Matches(pc.Groups[1].Value, @"^\s*([A-Za-z0-9_\-\.]+)\s+0x[0-9a-fA-F]+\s*\+\s*([0-9a-fA-F]+)", RegexOptions.Multiline))
                    {
                        string mod = fm.Groups[1].Value;
                        if (SystemModules.Contains(mod.ToLowerInvariant())) continue;
                        topModule = topModule ?? mod;
                        r.Evidence.Add("Crash stack starts in " + mod + " + 0x" + fm.Groups[2].Value);
                        break;
                    }
            }

            foreach (string f in files.Where(x => x.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    byte[] b = ReadBytes(f, 96 << 20);
                    var md = Minidump.Parse(b);
                    if (md.ExceptionModule != null)
                    {
                        string name = Path.GetFileNameWithoutExtension(md.ExceptionModule);
                        if (topModule == null || SystemModules.Contains(topModule.ToLowerInvariant())) topModule = name;
                        r.Evidence.Add("Exception 0x" + md.ExceptionCode.ToString("X8") + " in " + md.ExceptionModule + " + 0x" + md.ExceptionOffset.ToString("X"));
                        string addr = md.ExceptionCode == 0xC0000005 && md.Params >= 2 ? (md.Access == 1 ? "writing" : md.Access == 8 ? "running code at" : "reading") + " 0x" + md.BadAddress.ToString("X") : null;
                        r.Detail = LuaMatch.ExceptionText(md.ExceptionCode, addr);
                        if (name.Equals("UE4SS", StringComparison.OrdinalIgnoreCase)) { string known = LuaMatch.KnownUe4ss(md.ExceptionOffset); if (known != null) r.Detail = known + " " + r.Detail; }
                        var dm = Regex.Match(md.ExceptionModule, @"ue4ss[\\/]Mods[\\/]([^\\/]+)[\\/]dlls[\\/]", RegexOptions.IgnoreCase);
                        if (dm.Success) { dllMod = dm.Groups[1].Value; Vote(dllMod, 6, "The crash happened inside this mod's DLL."); }
                    }
                    foreach (string mod in md.Modules)
                    {
                        var mm = Regex.Match(mod, @"ue4ss[\\/]Mods[\\/]([^\\/]+)[\\/]", RegexOptions.IgnoreCase);
                        if (mm.Success) r.Evidence.Add("DLL loaded: " + mm.Groups[1].Value + "\\" + Path.GetFileName(mod));
                    }
                    string ascii = Encoding.ASCII.GetString(b);
                    var errs = LuaMatch.ErrorStrings(ascii);
                    if (errs.Count > 0)
                    {
                        r.LuaError = errs[0];
                        foreach (string e in errs) r.Evidence.Add("Lua error in memory: " + e);
                        if (modsDir != null && Directory.Exists(modsDir))
                        {
                            var em = Regex.Match(errs[0], @"^(.+?\.lua) line (\d+)");
                            int ln = int.Parse(em.Groups[2].Value);
                            var owners = new List<string>();
                            foreach (string dir in Directory.GetDirectories(modsDir))
                            {
                                string sc = Path.Combine(dir, "Scripts");
                                if (!Directory.Exists(sc)) continue;
                                foreach (string lf in Directory.GetFiles(sc, em.Groups[1].Value, SearchOption.AllDirectories))
                                    try { if (File.ReadLines(lf).Count() >= ln) { owners.Add(Path.GetFileName(dir)); break; } } catch { }
                            }
                            if (owners.Count == 1) Vote(owners[0], 3, "The Lua error \"" + errs[0] + "\" comes from this mod's script.");
                            else foreach (string o in owners.Take(3)) Vote(o, 0.7, "It has a script like the one in the Lua error (" + errs[0] + ").");
                        }
                    }
                    if (modsDir != null)
                    {
                        var protos = LuaMatch.ScanProtos(b);
                        var hits = LuaMatch.Match(protos, modsDir);
                        var byMod = hits.GroupBy(h => h.Folder).Select(g => g.First()).ToList();
                        if (protos.Count > 0) r.Evidence.Add(protos.Count + " Lua functions found in the crash memory" + (byMod.Count > 0 ? "; best match " + byMod[0].Folder + "\\" + byMod[0].File + " (" + byMod[0].Matches + " functions)" : ""));
                        if (byMod.Count > 0 && byMod[0].Score >= 4 && (byMod.Count == 1 || byMod[0].Score >= 2 * byMod[1].Score))
                            Vote(byMod[0].Folder, 4, "Its script " + byMod[0].File + " was running when the game crashed (" + byMod[0].Matches + " of its functions are in the crash memory).");
                        else foreach (var h in byMod.Take(2)) r.Evidence.Add("Weak match (not blamed): " + h.Folder + "\\" + h.File + ", " + h.Matches + " functions");
                    }
                    string wide = Encoding.Unicode.GetString(b, 0, b.Length & ~1);
                    foreach (string s in new[] { ascii, wide })
                    {
                        foreach (Match m in Regex.Matches(s, @"[\\/]Mods[\\/]([A-Za-z0-9_ .\-]{2,60})[\\/](?:Scripts|scripts)[\\/]([A-Za-z0-9_\-]+\.lua)(?::(\d+))?"))
                            Vote(m.Groups[1].Value, m.Groups[3].Success ? 2 : 0.6, "Its script " + m.Groups[2].Value + (m.Groups[3].Success ? " line " + m.Groups[3].Value : "") + " was running when the game crashed.");
                        foreach (Match m in Regex.Matches(s, @"\[([A-Za-z][A-Za-z0-9_ \-]{2,40})\][^\r\n]{0,80}\.lua:\d+"))
                            Vote(m.Groups[1].Value, 1.5, "An error from this mod's script was in the crash memory.");
                    }
                }
                catch (Exception ex) { r.Evidence.Add("Couldn't read " + Path.GetFileName(f) + ": " + ex.Message); }
            }

            foreach (string f in files.Where(x => x.EndsWith(".log", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(x).Equals("UE4SS.log", StringComparison.OrdinalIgnoreCase)))
            {
                var tail = Tail(f, 600);
                foreach (string l in tail)
                {
                    if (l.Contains("Trying to resize TArray") || l.Contains("Serial size mismatch") || l.Contains("Corrupt data found")) { tarray = true; r.Evidence.Add(Trim(l)); }
                    if (l.IndexOf("Ran out of memory", StringComparison.OrdinalIgnoreCase) >= 0 || l.Contains("Out of memory")) { oom = true; r.Evidence.Add(Trim(l)); }
                    if (l.Contains("Fatal error") || l.Contains("Assertion failed") || l.Contains("LowLevelFatalError")) r.Evidence.Add(Trim(l));
                    var pm = Regex.Match(l, @"[\\/]~mods[\\/]([^\\/]+?)(?:[\\/][^\\/]*)?\.(?:pak|utoc)", RegexOptions.IgnoreCase);
                    if (pm.Success && !paks.Contains(pm.Groups[1].Value, StringComparer.OrdinalIgnoreCase)) paks.Add(pm.Groups[1].Value);
                    var gm = Regex.Match(l, @"/Game/Mods/([A-Za-z0-9_\-]+)/");
                    if (gm.Success && (l.Contains("Error") || l.Contains("Fatal"))) Vote(gm.Groups[1].Value, 2, "The game log names this mod's assets right before the crash.");
                }
            }

            string ue = files.FirstOrDefault(x => Path.GetFileName(x).Equals("UE4SS.log", StringComparison.OrdinalIgnoreCase));
            if (ue == null && ue4ssLog != null && File.Exists(ue4ssLog) && Math.Abs((File.GetLastWriteTime(ue4ssLog) - when).TotalMinutes) < 15) ue = ue4ssLog;
            if (ue != null && File.Exists(ue))
            {
                var tail = Tail(ue, 400);
                string lastStart = null;
                foreach (string l in tail)
                {
                    var fl = Regex.Match(l, @"Failed to load dll.*?[\\/]Mods[\\/]([^\\/]+)[\\/]", RegexOptions.IgnoreCase);
                    if (fl.Success) { Vote(fl.Groups[1].Value, 3, "UE4SS couldn't load this mod's DLL. It was made for a different UE4SS version."); r.Evidence.Add(Trim(l)); }
                    var st = Regex.Match(l, @"Starting (?:Lua )?mod '([^']+)'|Mod '([^']+)' has enabled\.txt");
                    if (st.Success) lastStart = st.Groups[1].Success ? st.Groups[1].Value : st.Groups[2].Value;
                    var le = Regex.Match(l, @"[\\/]Mods[\\/]([^\\/]+)[\\/]Scripts[\\/]([^:\s]+\.lua):(\d+)");
                    if (le.Success && (l.Contains("rror") || l.Contains("attempt") || l.Contains("traceback")))
                        Vote(le.Groups[1].Value, 1, "UE4SS.log shows an error in " + le.Groups[2].Value + " line " + le.Groups[3].Value + ".");
                }
                if (lastStart != null && tail.Count > 0 && Regex.IsMatch(tail[tail.Count - 1] + (tail.Count > 1 ? tail[tail.Count - 2] : ""), "Starting|has enabled.txt|Mod '"))
                    Vote(lastStart, 3, "UE4SS.log stops while this mod was starting, so it crashed during start-up.");
            }

            string top = (topModule ?? "").ToLowerInvariant();
            gpu = GpuModules.Contains(top);
            r.Module = topModule;
            var ranked = votes.OrderByDescending(v => v.Value).ToList();

            if (gpu) { r.Kind = "gpu"; r.Cause = "Your graphics driver crashed (" + topModule + "). This usually isn't a mod: update your graphics driver or lower the graphics settings."; }
            else if (oom) { r.Kind = "memory"; r.Cause = "The game ran out of memory. Close other programs, or turn off big texture or world mods."; }
            else if (dllMod != null) { r.Kind = "dll"; r.Cause = "The game crashed inside " + dllMod + "'s DLL."; }
            else if (top == "ue4ss") { r.Kind = "lua"; r.Cause = ranked.Count > 0 ? "A UE4SS mod's script touched something the game had already removed." : "A UE4SS mod's script crashed the game, but the crash files don't say which one."; }
            else if (tarray) { r.Kind = "pak"; r.Cause = "A pak mod made for an older game version broke while loading."; }
            else if (top.StartsWith("rsdragonwilds")) { r.Kind = "game"; r.Cause = ranked.Count > 0 ? "The game crashed while a mod was working on it." : "The game itself crashed. The crash files don't name any mod."; }
            else if (top.Length > 0) { r.Kind = "other"; r.Cause = "The crash happened in " + topModule + "."; }
            else { r.Kind = "unknown"; r.Cause = "The crash files don't say what crashed."; }

            if (r.Kind == "pak" && ranked.Count == 0)
                foreach (string p in paks) Vote(p, 0.5, "Pak mods are the usual cause of this crash after a game update.");
            ranked = votes.OrderByDescending(v => v.Value).ToList();
            double best = ranked.Count > 0 ? ranked[0].Value : 0;
            foreach (var kv in ranked.Take(4))
            {
                if (r.Kind == "gpu" || r.Kind == "memory") break;
                var s = resolve?.Invoke(kv.Key) ?? new Suspect { Folder = kv.Key, Name = kv.Key };
                s.Folder = s.Folder ?? kv.Key;
                s.Why = why.TryGetValue(kv.Key, out string w) ? w : "";
                s.Confidence = kv.Value >= 2 && kv.Value >= best * 0.99 ? "likely" : "possible";
                if (r.Suspects.Any(x => x.Name == s.Name)) continue;
                r.Suspects.Add(s);
            }
            if (r.Suspects.Count > 0 && r.Suspects[0].Confidence == "likely")
                r.Cause += " Most likely: " + r.Suspects[0].Name + ".";
            r.Evidence = r.Evidence.Distinct().Take(14).ToList();
            return r;
        }

        static string Trim(string l) => l.Length > 220 ? l.Substring(0, 220) + "…" : l;

        static string Read(string f, int max)
        {
            using (var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var buf = new byte[Math.Min(max, s.Length)];
                int n = s.Read(buf, 0, buf.Length);
                return Encoding.UTF8.GetString(buf, 0, n);
            }
        }

        static byte[] ReadBytes(string f, int max)
        {
            using (var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var buf = new byte[Math.Min(max, s.Length)];
                int got = 0;
                while (got < buf.Length) { int n = s.Read(buf, got, buf.Length - got); if (n <= 0) break; got += n; }
                return buf;
            }
        }

        public static List<string> Tail(string f, int lines)
        {
            try
            {
                using (var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long start = Math.Max(0, s.Length - 400_000);
                    s.Seek(start, SeekOrigin.Begin);
                    var buf = new byte[s.Length - start];
                    int got = 0;
                    while (got < buf.Length) { int n = s.Read(buf, got, buf.Length - got); if (n <= 0) break; got += n; }
                    var all = Encoding.UTF8.GetString(buf, 0, got).Split('\n').Select(x => x.TrimEnd('\r')).Where(x => x.Length > 0).ToList();
                    return all.Skip(Math.Max(0, all.Count - lines)).ToList();
                }
            }
            catch { return new List<string>(); }
        }
    }

    public sealed class Minidump
    {
        public List<string> Modules = new List<string>();
        public string ExceptionModule;
        public uint ExceptionCode, Params;
        public ulong ExceptionAddress, ExceptionOffset, BadAddress, Access;

        public static Minidump Parse(byte[] b)
        {
            var d = new Minidump();
            if (b.Length < 32 || BitConverter.ToUInt32(b, 0) != 0x504D444D) return d;
            uint count = BitConverter.ToUInt32(b, 8), dirRva = BitConverter.ToUInt32(b, 12);
            var mods = new List<Tuple<ulong, uint, string>>();
            for (uint i = 0; i < count; i++)
            {
                long e = dirRva + i * 12;
                if (e + 12 > b.Length) break;
                uint type = BitConverter.ToUInt32(b, (int)e), rva = BitConverter.ToUInt32(b, (int)e + 8);
                if (type == 4 && rva + 4 <= b.Length)
                {
                    uint n = BitConverter.ToUInt32(b, (int)rva);
                    for (uint j = 0; j < n && j < 2000; j++)
                    {
                        long m = rva + 4 + j * 108;
                        if (m + 108 > b.Length) break;
                        ulong baseAddr = BitConverter.ToUInt64(b, (int)m);
                        uint size = BitConverter.ToUInt32(b, (int)m + 8);
                        uint nameRva = BitConverter.ToUInt32(b, (int)m + 20);
                        string name = "";
                        if (nameRva + 4 <= b.Length)
                        {
                            int len = (int)BitConverter.ToUInt32(b, (int)nameRva);
                            if (len > 0 && len < 2048 && nameRva + 4 + len <= b.Length) name = Encoding.Unicode.GetString(b, (int)nameRva + 4, len);
                        }
                        mods.Add(Tuple.Create(baseAddr, size, name));
                        d.Modules.Add(name);
                    }
                }
                else if (type == 6 && rva + 40 <= b.Length)
                {
                    d.ExceptionCode = BitConverter.ToUInt32(b, (int)rva + 8);
                    d.ExceptionAddress = BitConverter.ToUInt64(b, (int)rva + 24);
                    d.Params = BitConverter.ToUInt32(b, (int)rva + 32);
                    if (rva + 56 <= b.Length && d.Params >= 2) { d.Access = BitConverter.ToUInt64(b, (int)rva + 40); d.BadAddress = BitConverter.ToUInt64(b, (int)rva + 48); }
                }
            }
            if (d.ExceptionAddress != 0)
                foreach (var m in mods)
                    if (d.ExceptionAddress >= m.Item1 && d.ExceptionAddress < m.Item1 + m.Item2)
                    {
                        d.ExceptionModule = m.Item3;
                        d.ExceptionOffset = d.ExceptionAddress - m.Item1;
                        break;
                    }
            return d;
        }
    }
}
