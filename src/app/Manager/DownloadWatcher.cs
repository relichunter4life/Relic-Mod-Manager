// Made by Claude Ai & Relic Hunter
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace RelicMods.Manager
{
    public sealed class DownloadCheck
    {
        public bool Install;
        public string Reason;
        public InstallInfo Info;
    }

    public sealed class DownloadWatcher : IDisposable
    {
        [DllImport("shell32.dll")]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        public static readonly string[] Archives = { ".zip", ".7z", ".rar" };
        static readonly string[] Foreign = { ".esp", ".esm", ".esl", ".bsa", ".ba2", ".jar", ".pex", ".forge" };

        public static string DownloadsFolder()
        {
            try
            {
                if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out IntPtr p) == 0)
                {
                    string s = Marshal.PtrToStringUni(p);
                    Marshal.FreeCoTaskMem(p);
                    if (!string.IsNullOrEmpty(s) && Directory.Exists(s)) return s;
                }
            }
            catch { }
            string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            return Directory.Exists(d) ? d : null;
        }

        readonly Action<string, DownloadCheck> found;
        readonly HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        FileSystemWatcher fsw;
        public string Folder { get; private set; }

        public DownloadWatcher(Action<string, DownloadCheck> onFound) { found = onFound; }

        public void Start(string folder, IEnumerable<string> alreadyUsed, TimeSpan lookBack)
        {
            Stop();
            Folder = folder;
            if (folder == null || !Directory.Exists(folder)) return;
            foreach (string u in alreadyUsed) seen.Add(u);
            fsw = new FileSystemWatcher(folder) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite };
            fsw.Created += (s, e) => Consider(e.FullPath);
            fsw.Renamed += (s, e) => Consider(e.FullPath);
            fsw.EnableRaisingEvents = true;
            DateTime since = DateTime.Now - lookBack;
            try
            {
                foreach (string f in Directory.GetFiles(folder))
                    if (Archives.Contains(Path.GetExtension(f).ToLowerInvariant()) && File.GetLastWriteTime(f) > since) Consider(f);
            }
            catch { }
        }

        public void Stop()
        {
            if (fsw != null) { fsw.EnableRaisingEvents = false; fsw.Dispose(); fsw = null; }
        }

        public void Dispose() => Stop();

        void Consider(string path)
        {
            if (!Archives.Contains(Path.GetExtension(path).ToLowerInvariant())) return;
            lock (seen) if (!seen.Add(path)) return;
            Task.Run(async () =>
            {
                if (!await WaitDone(path).ConfigureAwait(false)) { lock (seen) seen.Remove(path); return; }
                DownloadCheck c;
                try { c = await Check(path).ConfigureAwait(false); }
                catch (Exception ex) { c = new DownloadCheck { Reason = "couldn't check it: " + ex.Message }; }
                if (c != null) found(path, c);
            });
        }

        static async Task<bool> WaitDone(string path)
        {
            long last = -1; int stable = 0;
            for (int i = 0; i < 240; i++)
            {
                await Task.Delay(1000).ConfigureAwait(false);
                if (!File.Exists(path)) return false;
                long size;
                try
                {
                    using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) size = s.Length;
                }
                catch { stable = 0; continue; }
                if (size > 0 && size == last) { if (++stable >= 2) return true; } else stable = 0;
                last = size;
            }
            return false;
        }

        public static async Task<DownloadCheck> Check(string path)
        {
            string zone = SourceDetect.ReadZone(path) ?? "";
            string z = zone.ToLowerInvariant();
            bool nexus = z.Contains("nexusmods.com") || z.Contains("nexus-cdn.com");
            bool cf = z.Contains("curseforge.com") || z.Contains("forgecdn.net");
            if (!nexus && !cf) return null;
            var info = ManagerService.GuessInfo(path);
            SourceDetect.FromZone(path, info);
            if (info.Site == "nexus" && info.SiteGame != null && !info.SiteGame.Equals(NexusData.GameDomain, StringComparison.OrdinalIgnoreCase))
                return new DownloadCheck { Reason = "it's a mod for another game (" + info.SiteGame + ")", Info = info };
            bool ours = z.Contains("/" + NexusData.GameDomain + "/") || z.Contains("nexus-cdn.com/" + NexusData.GameId + "/") || z.Contains("/runescape-dragonwilds/");
            if (!ours && nexus && info.SiteModId != null)
            {
                string fileName = info.Name;
                var hit = (await NexusData.Mods(new[] { info.SiteModId }).ConfigureAwait(false)).FirstOrDefault();
                if (hit != null && SourceDetect.NameMatch(fileName, hit.Name)) { ours = true; info.Name = hit.Name; info.Picture = hit.Picture; info.Author = hit.Author; }
                else return new DownloadCheck { Reason = "Nexus mod " + info.SiteModId + " isn't the same mod in RuneScape: Dragonwilds, so it's probably for another game", Info = info };
            }
            if (!ours) return new DownloadCheck { Reason = "couldn't confirm it's a RuneScape: Dragonwilds mod", Info = info };
            List<string> files;
            try { files = Installer.ListArchive(path); }
            catch { return new DownloadCheck { Reason = "Relic Mod Manager can't open that archive", Info = info }; }
            if (files.Any(f => Foreign.Contains(Path.GetExtension(f).ToLowerInvariant())))
                return new DownloadCheck { Reason = "it has files from another game", Info = info };
            return new DownloadCheck { Install = true, Info = info };
        }
    }
}
