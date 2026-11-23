// Made by Claude Ai & Relic Hunter
using System.IO;
using System.Linq;
using Microsoft.Win32;
using RelicMods.Manager;

namespace RelicMods
{
    static partial class Program
    {
        static void SetNxm(string cmd)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(NxmProtocol.ClassesKey + @"\shell\open\command")) k.SetValue("", cmd);
        }

        static void V060(string root)
        {
            NxmProtocol.ClassesKey = @"Software\RelicModsTest\Classes\nxm";
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\RelicModsTest", false);
                string mo2 = Path.Combine(root, "MO2", "nxmhandler.exe");
                Write(mo2, "x");
                string ours = Path.Combine(root, "game", "App", "RelicMods.exe");
                Check("nxm: nothing to give back when no link is set", !NxmProtocol.ReleaseOld());
                SetNxm("\"" + mo2 + "\" \"%1\"");
                Check("nxm: MO2's link is left alone", !NxmProtocol.ReleaseOld() && NxmProtocol.ExeOf(NxmProtocol.Current()) == mo2);
                Write(Path.Combine(Store.Home, "nxm-previous.txt"), "\"" + mo2 + "\" \"%1\"");
                SetNxm("\"" + ours + "\" \"%1\"");
                Check("nxm: old Relic Mods link gives MO2 its link back", NxmProtocol.ReleaseOld() && NxmProtocol.ExeOf(NxmProtocol.Current()) == mo2 && !File.Exists(Path.Combine(Store.Home, "nxm-previous.txt")), NxmProtocol.Current());
                SetNxm("\"C:\\Somewhere\\RelicModManager.exe\" \"%1\"");
                Check("nxm: with nothing to give back the link is removed", NxmProtocol.ReleaseOld() && NxmProtocol.Current() == null);
            }
            finally
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\RelicModsTest", false);
                NxmProtocol.ClassesKey = @"Software\Classes\nxm";
            }

            var svc = new ManagerService();
            svc.SaveDownload(new DownloadRec { Id = 5, Name = "A", State = "done", Path = @"C:\a.zip" });
            svc.SaveDownload(new DownloadRec { Id = 6, Name = "B", State = "skipped", Path = @"C:\b.zip" });
            svc.SaveDownload(new DownloadRec { Id = 6, Name = "B2", State = "done", Path = @"C:\b.zip" });
            svc.SaveDownload(new DownloadRec { Id = 7, Name = "Nexus mod 931", State = "needs-signin" });
            var again = new ManagerService();
            Check("install list survives a restart", again.State.Downloads.Count == 3 && again.FindDownload(6).Name == "B2" && again.LastDownloadId() == 7);
            again.PurgeDownloads(r => r.State == "needs-signin" || string.IsNullOrEmpty(r.Path));
            Check("old Mod Manager Download rows are cleared", new ManagerService().State.Downloads.Select(d => d.Id).SequenceEqual(new[] { 5, 6 }));
            var copy = again.FindDownload(5); copy.Name = "changed";
            Check("install records are copies", again.FindDownload(5).Name == "A");
            again.ClearDownloads();
            Check("clear list empties finished installs", new ManagerService().State.Downloads.Count == 0);
            for (int i = 1; i <= 90; i++) svc.SaveDownload(new DownloadRec { Id = 100 + i, State = "done", Path = "x" });
            Check("install list capped at 80", svc.State.Downloads.Count == 80 && svc.State.Downloads.First().Id == 111);
            svc.ClearDownloads();
        }
    }
}
