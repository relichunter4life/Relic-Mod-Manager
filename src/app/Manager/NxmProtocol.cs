// Made by Claude Ai & Relic Hunter
using System;
using System.IO;
using Microsoft.Win32;

namespace RelicMods.Manager
{
    public static class NxmProtocol
    {
        public static string ClassesKey = @"Software\Classes\nxm";
        static string BackupFile => Path.Combine(Store.Home, "nxm-previous.txt");

        public static string Current()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(ClassesKey + @"\shell\open\command"))
                return k?.GetValue("") as string;
        }

        public static string ExeOf(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return null;
            cmd = cmd.Trim();
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                return end > 1 ? cmd.Substring(1, end - 1) : null;
            }
            int sp = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return sp > 0 ? cmd.Substring(0, sp + 4) : cmd.Split(' ')[0];
        }

        static bool Alive(string cmd)
        {
            string e = ExeOf(cmd);
            try { return e != null && File.Exists(e); } catch { return false; }
        }

        public static bool OwnedByRelic(string cmd)
        {
            string e = ExeOf(cmd);
            if (e == null) return false;
            string f = Path.GetFileName(e).ToLowerInvariant();
            return f == "relicmods.exe" || f == "relicmodmanager.exe";
        }

        public static bool ReleaseOld()
        {
            if (!OwnedByRelic(Current())) return false;
            string prev = null;
            try { if (File.Exists(BackupFile)) prev = File.ReadAllText(BackupFile).Trim(); } catch { }
            if (!string.IsNullOrEmpty(prev) && Alive(prev) && !OwnedByRelic(prev))
            {
                using (var cmd = Registry.CurrentUser.CreateSubKey(ClassesKey + @"\shell\open\command")) cmd.SetValue("", prev);
                using (var icon = Registry.CurrentUser.CreateSubKey(ClassesKey + @"\DefaultIcon")) icon.SetValue("", "\"" + ExeOf(prev) + "\",0");
            }
            else Registry.CurrentUser.DeleteSubKeyTree(ClassesKey, false);
            try { if (File.Exists(BackupFile)) File.Delete(BackupFile); } catch { }
            return true;
        }
    }
}
