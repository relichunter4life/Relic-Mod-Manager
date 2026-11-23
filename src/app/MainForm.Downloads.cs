// Made by Claude Ai & Relic Hunter
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RelicMods.Manager;

namespace RelicMods
{
    sealed partial class MainForm
    {
        bool uiReady;

        static string InboxDir => Path.Combine(Store.Home, "inbox");

        void UI(Action a) { try { if (IsHandleCreated) BeginInvoke(a); } catch { } }

        void Push(DownloadRec r, bool save)
        {
            Send(ManagerService.DownloadMsg(r));
            if (save) mgr.SaveDownload(r);
        }

        void PushLater(DownloadRec r, bool save)
        {
            var msg = ManagerService.DownloadMsg(r);
            if (save) mgr.SaveDownload(r);
            UI(() => Send(msg));
        }

        void OnUiReady()
        {
            if (uiReady) { ReadInbox(); return; }
            uiReady = true;
            mgr.PurgeDownloads(r => r.State != "downloading" && r.State != "installing");
            downloadSeq = Math.Max(downloadSeq, mgr.LastDownloadId());
            if (fromGame) return;
            if (Paths.Arg(startArgs, "--home") == null)
                try { if (NxmProtocol.ReleaseOld()) Store.Log("gave the Nexus 'Mod Manager Download' link back"); }
                catch (Exception ex) { Store.Log("nxm release: " + ex.Message); }
            ReadInbox();
        }

        public static void PostToInbox(string[] args)
        {
            var items = args.Where(a => Archives.Contains(Path.GetExtension(a).ToLowerInvariant()) && File.Exists(a)).Select(Path.GetFullPath).ToList();
            if (items.Count == 0) return;
            try
            {
                Directory.CreateDirectory(InboxDir);
                File.WriteAllLines(Path.Combine(InboxDir, DateTime.UtcNow.Ticks + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".txt"), items);
            }
            catch { }
        }

        void ReadInbox()
        {
            if (!uiReady || fromGame) return;
            string[] files;
            try { files = Directory.Exists(InboxDir) ? Directory.GetFiles(InboxDir, "*.txt").OrderBy(f => f).ToArray() : new string[0]; }
            catch { return; }
            var archives = new List<string>();
            foreach (string f in files)
            {
                try
                {
                    bool fresh = File.GetLastWriteTimeUtc(f) > DateTime.UtcNow.AddMinutes(-10);
                    if (fresh) archives.AddRange(File.ReadAllLines(f).Where(File.Exists));
                    File.Delete(f);
                }
                catch { }
            }
            if (archives.Count > 0) InstallMany(archives);
        }

        void InstallDropped(string path)
        {
            long size = 0;
            try { size = new FileInfo(path).Length; } catch { }
            var guess = ManagerService.GuessInfo(path);
            var r = new DownloadRec
            {
                Id = ++downloadSeq, Name = guess?.Name ?? Path.GetFileNameWithoutExtension(path), File = Path.GetFileName(path), Path = path,
                At = Store.Now(), Received = size, Total = size, State = "installing"
            };
            Push(r, false);
            _ = Task.Run(() => InstallDownloaded(r, null));
        }

        async Task InstallDownloaded(DownloadRec r, InstallInfo info)
        {
            if (string.IsNullOrEmpty(r.Path) || !File.Exists(r.Path))
            {
                r.State = "failed"; r.Reason = "the file isn't there any more";
                PushLater(r, true);
                return;
            }
            r.State = "installing"; r.Reason = null;
            PushLater(r, false);
            if (info == null && r.Site == "nexus" && !string.IsNullOrEmpty(r.ModId))
                info = new InstallInfo
                {
                    Name = r.Name, Site = "nexus", SiteGame = NexusData.GameDomain, SiteModId = r.ModId, SiteFileId = r.FileId,
                    Version = r.Version, Picture = r.Picture, Url = "https://www.nexusmods.com/" + NexusData.GameDomain + "/mods/" + r.ModId
                };
            await mgr.InstallArchive(r.Path, info).ConfigureAwait(false);
            var mod = mgr.ModForArchive(r.Path);
            if (mod != null)
            {
                r.State = "done";
                r.Name = mod.Name ?? r.Name;
                r.Picture = mod.Picture ?? r.Picture;
                r.Site = mod.Site ?? r.Site;
                r.ModId = mod.SiteModId ?? r.ModId;
                r.Version = mod.Version ?? r.Version;
            }
            else if (mgr.PendingFor(r.Path)) { r.State = "waiting"; r.Reason = "pick where its files go"; }
            else { r.State = "failed"; r.Reason = mgr.LastResult ?? "it didn't install"; }
            PushLater(r, true);
        }

        void OnBrowserDownload(string path, DownloadCheck c)
        {
            long size = 0;
            try { size = new FileInfo(path).Length; } catch { }
            var r = new DownloadRec
            {
                Id = ++downloadSeq, Name = c.Info?.Name ?? Path.GetFileNameWithoutExtension(path), File = Path.GetFileName(path), Path = path,
                At = Store.Now(), Site = c.Info?.Site, ModId = c.Info?.SiteModId, FileId = c.Info?.SiteFileId, Picture = c.Info?.Picture,
                Version = c.Info?.Version, Received = size, Total = size
            };
            if (!c.Install) { r.State = "skipped"; r.Reason = c.Reason; Push(r, true); return; }
            Send(new { op = "toast", text = "Installing " + r.Name + " from your browser download" });
            _ = Task.Run(() => InstallDownloaded(r, c.Info));
        }

        void OnDownloadMessage(string op, Dictionary<string, object> m)
        {
            switch (op)
            {
                case "mgr.dlclear": mgr.ClearDownloads(); Send(mgr.Snapshot()); break;
                case "mgr.dlinstall":
                    {
                        var r = mgr.FindDownload((int)Num(m, "id"));
                        if (r == null) break;
                        if (string.IsNullOrEmpty(r.Path) || !File.Exists(r.Path)) { Send(new { op = "toast", text = "That file isn't there any more", bad = true }); break; }
                        _ = Task.Run(() => InstallDownloaded(r, null));
                        break;
                    }
                case "mgr.dlshow":
                    {
                        var r = mgr.FindDownload((int)Num(m, "id"));
                        if (r?.Path != null && File.Exists(r.Path)) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + r.Path + "\"");
                        break;
                    }
            }
        }
    }
}
