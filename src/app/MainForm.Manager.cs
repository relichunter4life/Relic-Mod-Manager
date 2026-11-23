// Made by Claude Ai & Relic Hunter
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using RelicMods.Manager;

namespace RelicMods
{
    sealed partial class MainForm
    {
        int downloadSeq;
        static readonly string[] Archives = { ".zip", ".7z", ".rar" };
        void OnManagerMessage(string op, Dictionary<string, object> m, CoreWebView2WebMessageReceivedEventArgs e)
        {
            switch (op)
            {
                case "mgr.state": Send(mgr.Snapshot()); break;
                case "mgr.toggle": _ = mgr.Toggle(Str(m, "key"), Str(m, "on") == "True"); break;
                case "mgr.move": _ = mgr.Move(Str(m, "key"), Str(m, "dir") == "up" ? -1 : 1); break;
                case "mgr.uninstall": _ = mgr.Uninstall(Str(m, "key")); break;
                case "mgr.profile": _ = mgr.SetProfile(Str(m, "name")); break;
                case "mgr.newProfile": _ = mgr.NewProfile(Str(m, "name"), Str(m, "copy") == "True"); break;
                case "mgr.deleteProfile": _ = mgr.DeleteProfile(Str(m, "name")); break;
                case "mgr.game": _ = mgr.SetGame(Str(m, "id")).ContinueWith(t => BeginInvoke(new Action(SendSnapshot))); break;
                case "mgr.detect": _ = mgr.Detect(); break;
                case "mgr.removeGame": _ = mgr.RemoveGame(Str(m, "id")); break;
                case "mgr.addFolder":
                    using (var d = new FolderBrowserDialog { Description = "Pick your RuneScape: Dragonwilds game folder (or a dedicated server folder)" })
                        if (d.ShowDialog(this) == DialogResult.OK) _ = mgr.AddFolder(d.SelectedPath);
                    break;
                case "mgr.importMo2":
                    {
                        string root = Str(m, "path");
                        if (root == "pick")
                        {
                            using (var d = new FolderBrowserDialog { Description = "Pick your Mod Organizer 2 folder (the one with ModOrganizer.ini)" })
                                root = d.ShowDialog(this) == DialogResult.OK ? d.SelectedPath : null;
                            if (root == null) break;
                        }
                        _ = mgr.ImportMo2(root);
                        break;
                    }
                case "mgr.clean": _ = mgr.CleanGame(); break;
                case "mgr.autosort": _ = mgr.AutoSort(); break;
                case "mgr.watch": _ = mgr.SetWatch(Str(m, "on") == "True").ContinueWith(t => BeginInvoke(new Action(StartWatcher))); break;
                case "mgr.installPath":
                    {
                        string pth = Str(m, "path");
                        if (!string.IsNullOrEmpty(pth) && File.Exists(pth) && DownloadWatcher.Archives.Contains(Path.GetExtension(pth).ToLowerInvariant())) _ = mgr.InstallArchive(pth);
                        break;
                    }
                case "mgr.openModFolder": { string d = mgr.ModFolderPath(Str(m, "key")); if (d != null && Directory.Exists(d)) System.Diagnostics.Process.Start("explorer.exe", "\"" + d + "\""); break; }
                case "mgr.files": { var f = mgr.ModFiles(Str(m, "key")); if (f != null) Send(f); break; }
                case "mgr.note": _ = mgr.SetNote(Str(m, "key"), Str(m, "text")); break;
                case "mgr.category": _ = mgr.SetCategory(Str(m, "key"), Str(m, "text")); break;
                case "mgr.moveTo": _ = mgr.MoveTo(Str(m, "key"), (int)Num(m, "index")); break;
                case "mgr.playVanilla":
                    _ = mgr.SetProfile("Vanilla").ContinueWith(t => BeginInvoke(new Action(() =>
                    {
                        try { mgr.Play(); Send(new { op = "toast", text = "Starting the game without mods. Switch the profile back to Modded afterwards." }); }
                        catch (Exception ex) { Send(new { op = "toast", text = ex.Message, bad = true }); }
                    })));
                    break;
                case "mgr.openPath":
                    {
                        var gs = mgr.State.Current; if (gs == null) break;
                        string what = Str(m, "what"), path = null;
                        if (what == "log") path = Path.Combine(gs.Game.BinDir, "ue4ss", "UE4SS.log");
                        else if (what == "ue4ss") path = Path.Combine(gs.Game.BinDir, "ue4ss");
                        else if (what == "mods") path = gs.Game.ModsDir;
                        else if (what == "crashes") path = CrashReader.CrashesDir;
                        else if (what == "downloads") path = DownloadWatcher.DownloadsFolder();
                        else if (what == "paks") path = Path.Combine(gs.Game.Root, "Content", "Paks");
                        if (path != null && what == "log" && File.Exists(path)) System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\"");
                        else if (path != null && Directory.Exists(path)) System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\"");
                        else Send(new { op = "toast", text = "That isn't there yet.", bad = true });
                        break;
                    }
                case "mgr.view": _ = mgr.SetLibraryView(Str(m, "view")); break;
                case "mgr.vortex":
                    {
                        string root = Str(m, "path");
                        if (root == "pick")
                        {
                            using (var d = new FolderBrowserDialog { Description = "Pick Vortex's mod staging folder for Dragonwilds (Vortex > Settings > Mods)" })
                                root = d.ShowDialog(this) == DialogResult.OK ? d.SelectedPath : null;
                            if (root == null) break;
                        }
                        _ = mgr.ImportVortex(string.IsNullOrEmpty(root) ? null : root);
                        break;
                    }
                case "mgr.apply": _ = mgr.ApplyNow(); break;
                case "mgr.advanced": _ = mgr.SetAdvanced(Str(m, "on") == "True"); break;
                case "mgr.updates": _ = mgr.CheckUpdates(); break;
                case "mgr.place": _ = mgr.AnswerPlacement(Str(m, "id"), Str(m, "choice")); break;
                case "mgr.openFolder":
                    {
                        var gs = mgr.State.Current;
                        string key = Str(m, "key");
                        string dir = key == null ? gs?.Game.Root : Path.Combine(gs?.Game.Storage ?? "", "mods", key);
                        if (dir != null && Directory.Exists(dir)) System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
                        break;
                    }
                case "mgr.play":
                    _ = mgr.ApplyNow().ContinueWith(t => BeginInvoke(new Action(() =>
                    {
                        try { mgr.Play(); Send(new { op = "toast", text = "Starting RuneScape: Dragonwilds" }); }
                        catch (Exception ex) { Send(new { op = "toast", text = ex.Message, bad = true }); }
                    })));
                    break;
                case "mgr.installFile":
                    using (var d = new OpenFileDialog { Filter = "Mod archives (*.zip;*.7z;*.rar)|*.zip;*.7z;*.rar", Multiselect = true, Title = "Install mods" })
                        if (d.ShowDialog(this) == DialogResult.OK) InstallMany(d.FileNames);
                    break;
                case "drop":
                    {
                        var paths = new List<string>();
                        if (e.AdditionalObjects != null)
                            foreach (var o in e.AdditionalObjects)
                                if (o is CoreWebView2File f) paths.Add(f.Path);
                        InstallMany(paths);
                        break;
                    }
            }
        }

        static double Num(Dictionary<string, object> m, string k) =>
            m.TryGetValue(k, out object v) && v != null && double.TryParse(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : 0;

        void InstallMany(IEnumerable<string> paths)
        {
            var list = paths.Where(p => !string.IsNullOrEmpty(p)).ToList();
            var bad = list.Where(p => !Archives.Contains(Path.GetExtension(p).ToLowerInvariant())).ToList();
            if (bad.Count > 0) Send(new { op = "toast", text = "Only .zip, .7z and .rar mod files can be installed: " + Path.GetFileName(bad[0]), bad = true });
            foreach (string p in list.Except(bad)) InstallDropped(p);
        }

    }
}
