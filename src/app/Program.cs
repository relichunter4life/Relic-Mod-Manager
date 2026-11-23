// Made by Claude Ai & Relic Hunter
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using RelicMods.Manager;

namespace RelicMods
{
    static class Program
    {
        public const string Version = "0.7.1";

        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, "Local\\RelicMods.Window", out created))
            using (var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\RelicMods.Show"))
            {
                string home = Paths.Arg(args, "--home");
                if (!string.IsNullOrEmpty(home)) Manager.Store.Home = Path.GetFullPath(home);
                if (!created)
                {
                    MainForm.PostToInbox(args);
                    showSignal.Set();
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(args, showSignal));
                GC.KeepAlive(mutex);
            }
        }
    }

    sealed class Paths
    {
        public readonly string App, Ui, Core, Mods;
        public readonly bool InsideGame;

        public Paths(string[] args)
        {
            App = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            Ui = Path.Combine(App, "ui");
            Core = Arg(args, "--core") ?? Path.GetDirectoryName(App);
            Mods = Path.GetDirectoryName(Core);
            InsideGame = File.Exists(Path.Combine(Core, "Scripts", "main.lua")) && File.Exists(Path.Combine(Core, "relic.json"));
        }

        public static string Arg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }

    sealed partial class MainForm : Form
    {
        const int WM_HOTKEY = 0x0312;
        const int HOTKEY_ID = 0x5243;
        const uint MOD_SHIFT = 0x0004, MOD_NOREPEAT = 0x4000, VK_F7 = 0x76;

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        readonly Paths P;
        readonly bool fromGame, dev, selfTest;
        readonly string[] startArgs;
        readonly EventWaitHandle showSignal;
        readonly WebView2 web;
        readonly Label fallback;
        readonly System.Windows.Forms.Timer poll;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        readonly Dictionary<string, DateTime> stamps = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        readonly ManagerService mgr = new ManagerService();
        bool webReady, hotkey, quitting;
        string gameText = "", phase = "", dllState = "unknown", mappedMods;
        int ticks, gameMissing, rev = -1;
        Rectangle saved;

        string ModsDir
        {
            get
            {
                if (P.InsideGame) return P.Mods;
                return mgr.State.Current?.Game.ModsDir ?? P.Mods;
            }
        }
        string CoreDir => Path.Combine(ModsDir, "RelicCore");
        string SettingsDir => Path.Combine(CoreDir, "settings");
        string BridgeDir => Path.Combine(CoreDir, "bridge");
        bool CoreInstalled => File.Exists(Path.Combine(CoreDir, "relic.json"));

        public MainForm(string[] args, EventWaitHandle showSignal)
        {
            P = new Paths(args);
            fromGame = args.Any(a => a.Equals("--from-game", StringComparison.OrdinalIgnoreCase));
            dev = args.Any(a => a.Equals("--dev", StringComparison.OrdinalIgnoreCase));
            selfTest = args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase));
            startArgs = args;
            this.showSignal = showSignal;
            mgr.Event += e => { try { if (IsHandleCreated) BeginInvoke(new Action(() => Send(e))); } catch { } };
            string addGame = Paths.Arg(args, "--game");
            if (addGame != null) { try { mgr.AddFolder(addGame).Wait(); } catch { } }
            else if (!fromGame && !args.Any(a => a.Equals("--no-detect", StringComparison.OrdinalIgnoreCase))) { try { mgr.EnsureGames(); } catch { } }
            var toInstall = args.Where(a => Archives.Contains(Path.GetExtension(a).ToLowerInvariant()) && File.Exists(a)).ToList();
            if (toInstall.Count > 0 && !fromGame) Shown += (s, e) => InstallMany(toInstall);

            Text = "Relic Mod Manager";
            BackColor = Color.FromArgb(11, 8, 8);
            MinimumSize = new Size(980, 640);
            StartPosition = FormStartPosition.Manual;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            saved = LoadBounds();
            Bounds = saved;
            TopMost = fromGame;

            web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(11, 8, 8) };
            fallback = new Label
            {
                Dock = DockStyle.Fill, Visible = false, ForeColor = Color.FromArgb(236, 230, 214),
                BackColor = Color.FromArgb(11, 8, 8), Font = new Font("Segoe UI", 12f), TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(web);
            Controls.Add(fallback);

            if (fromGame && !CoreFlags().Item1)
            {
                Opacity = 0;
                Shown += (s, e) => { Hide(); Opacity = 1; };
            }
            Load += async (s, e) => await StartWeb();
            poll = new System.Windows.Forms.Timer { Interval = 300 };
            poll.Tick += (s, e) => Poll();
            poll.Start();

            var waiter = new Thread(() =>
            {
                while (!quitting)
                {
                    if (showSignal.WaitOne(500) && !quitting)
                        try { BeginInvoke(new Action(() => { ShowNow(); ReadInbox(); })); } catch { }
                }
            }) { IsBackground = true };
            waiter.Start();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int on = 1, caption = 0x0008080B, text = 0x005CB7E3, border = 0x00180A7A;
            try
            {
                DwmSetWindowAttribute(Handle, 20, ref on, 4);
                DwmSetWindowAttribute(Handle, 35, ref caption, 4);
                DwmSetWindowAttribute(Handle, 36, ref text, 4);
                DwmSetWindowAttribute(Handle, 34, ref border, 4);
            }
            catch { }
            hotkey = RegisterHotKey(Handle, HOTKEY_ID, MOD_SHIFT | MOD_NOREPEAT, VK_F7);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                Toggle();
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (fromGame && !quitting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToGame();
                return;
            }
            SaveBounds();
            quitting = true;
            if (hotkey) UnregisterHotKey(Handle, HOTKEY_ID);
            base.OnFormClosing(e);
        }

        async System.Threading.Tasks.Task StartWeb()
        {
            try
            {
                string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RelicCore", "WebView2");
                var env = await CoreWebView2Environment.CreateAsync(null, data);
                await web.EnsureCoreWebView2Async(env);
                var core = web.CoreWebView2;
                core.Settings.AreDevToolsEnabled = dev;
                core.Settings.AreDefaultContextMenusEnabled = dev;
                core.Settings.AreBrowserAcceleratorKeysEnabled = dev;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.Settings.IsPasswordAutosaveEnabled = false;
                core.Settings.IsGeneralAutofillEnabled = false;
                core.SetVirtualHostNameToFolderMapping("relic.mods", P.Ui, CoreWebView2HostResourceAccessKind.DenyCors);
                MapMods();
                core.WebMessageReceived += OnMessage;
                core.NewWindowRequested += (s, e) => { e.Handled = true; OpenExternal(e.Uri); };
                core.NavigationStarting += (s, e) =>
                {
                    if (!e.Uri.StartsWith("https://relic.mods/", StringComparison.OrdinalIgnoreCase))
                    {
                        e.Cancel = true;
                        OpenExternal(e.Uri);
                    }
                };
                string view = selfTest ? "selftest" : Paths.Arg(startArgs, "--view");
                web.Source = new Uri("https://relic.mods/index.html" + (view != null ? "#" + Uri.EscapeDataString(view) : ""));
                webReady = true;
            }
            catch (Exception ex)
            {
                web.Visible = false;
                fallback.Visible = true;
                fallback.Text = !Directory.Exists(P.Ui)
                    ? "Some of Relic Mod Manager's files are missing (the ui folder next to RelicModManager.exe).\n\nUnzip Relic Mod Manager again, keeping all its folders together, and start it again."
                    : "Relic Mod Manager needs the Microsoft Edge WebView2 Runtime.\n\n" +
                    "Windows 10 and 11 usually have it. Install it from microsoft.com/edge/webview2 and start Relic Mod Manager again.\n\n" + ex.Message;
            }
        }

        void MapMods()
        {
            if (web.CoreWebView2 == null) return;
            string dir = ModsDir;
            if (dir == mappedMods || dir == null) return;
            try
            {
                if (mappedMods != null) web.CoreWebView2.ClearVirtualHostNameToFolderMapping("mods.relic");
                if (Directory.Exists(dir)) web.CoreWebView2.SetVirtualHostNameToFolderMapping("mods.relic", dir, CoreWebView2HostResourceAccessKind.DenyCors);
                mappedMods = dir;
            }
            catch { }
        }

        void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            Dictionary<string, object> m;
            try { m = json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson); }
            catch { return; }
            string op = Str(m, "op") ?? "";
            try
            {
                if (op == "mgr.dlclear" || op == "mgr.dlinstall" || op == "mgr.dlshow") { OnDownloadMessage(op, m); return; }
                if (op.StartsWith("mgr.") || op == "drop") { OnManagerMessage(op, m, e); return; }
                if (op.StartsWith("mm.") || op.StartsWith("crash.") || op.StartsWith("nx.") || op.StartsWith("files.")) { OnRelicMessage(op, m, e); return; }
                switch (op)
                {
                    case "init": SendSnapshot(); Send(mgr.Snapshot()); CheckCrashOnStart(); OnUiReady(); break;
                    case "save": SaveSettings(Str(m, "mod"), m.ContainsKey("data") ? m["data"] : null); break;
                    case "hide": if (fromGame) HideToGame(); break;
                    case "open": OpenExternal(Str(m, "url")); break;
                    case "copy": Clipboard.SetText(Str(m, "text") ?? ""); Send(new { op = "toast", text = "Copied" }); break;
                    case "report": SendReport(Str(m, "mod")); break;
                    case "log": SendLogErrors(); break;
                    case "selftest": FinishSelfTest(m); break;
                }
            }
            catch (Exception ex)
            {
                Send(new { op = "toast", text = op + ": " + ex.Message, bad = true });
            }
        }

        void Send(object payload)
        {
            if (!webReady || web.CoreWebView2 == null) return;
            web.CoreWebView2.PostWebMessageAsJson(json.Serialize(payload));
        }

        void SendSnapshot()
        {
            MapMods();
            var mods = new List<object>();
            if (Directory.Exists(ModsDir))
            {
                foreach (string dir in Directory.GetDirectories(ModsDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    string manifest = Path.Combine(dir, "relic.json");
                    if (!File.Exists(manifest)) continue;
                    string folder = Path.GetFileName(dir);
                    string settingsFile = Path.Combine(SettingsDir, folder + ".json");
                    Remember(settingsFile);
                    mods.Add(new
                    {
                        folder,
                        manifest = ReadText(manifest),
                        settings = ReadText(settingsFile),
                        report = File.Exists(Path.Combine(dir, "report.txt")),
                        enabled = File.Exists(Path.Combine(dir, "enabled.txt"))
                    });
                }
            }
            string coreFile = Path.Combine(SettingsDir, "_core.json");
            Remember(coreFile);
            Send(new
            {
                op = "snapshot",
                core = new { version = Program.Version, fromGame, insideGame = P.InsideGame, phase, dll = dllState, gameRunning = GameRunning(), hotkey, modsFolder = ModsDir, coreInstalled = CoreInstalled },
                mods,
                others = OthersForSnapshot(),
                modMenuKey = ModMenuKey(),
                coreSettings = ReadText(coreFile)
            });
        }

        void SaveSettings(string mod, object data)
        {
            if (string.IsNullOrEmpty(mod) || data == null) return;
            foreach (char c in mod) if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-')) return;
            if (!CoreInstalled) { Send(new { op = "toast", text = "Install Relic Core in this game to save mod settings.", bad = true }); return; }
            Directory.CreateDirectory(SettingsDir);
            Directory.CreateDirectory(BridgeDir);
            string file = Path.Combine(SettingsDir, mod + ".json");
            string text = json.Serialize(data);
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(file)) File.Delete(file);
            File.Move(tmp, file);
            Remember(file);
            if (rev < 0) rev = ReadInt(Path.Combine(BridgeDir, "rev.txt"));
            rev++;
            File.WriteAllText(Path.Combine(BridgeDir, "rev.txt"), rev.ToString(), new UTF8Encoding(false));
        }

        void FinishSelfTest(Dictionary<string, object> m)
        {
            if (!selfTest) return;
            var t = new System.Windows.Forms.Timer { Interval = 600 };
            t.Tick += (s, e) =>
            {
                t.Stop();
                string mod = Str(m, "mod") ?? "", key = Str(m, "key") ?? "";
                string text = ReadText(Path.Combine(SettingsDir, mod + ".json")) ?? "";
                string want = "\"" + key + "\":" + (Str(m, "value") ?? "").ToLowerInvariant();
                bool ok = Str(m, "ok") == "True" && text.Contains(want) && ReadText(Path.Combine(BridgeDir, "rev.txt")) != null;
                File.WriteAllText(Path.Combine(BridgeDir, "selftest.txt"),
                    (ok ? "ok" : "FAIL") + "\nui: " + Str(m, "ok") + " " + (Str(m, "text") ?? "") + "\nlooked for: " + want + "\nfile: " + text + "\nrev: " + ReadText(Path.Combine(BridgeDir, "rev.txt")));
                quitting = true;
                Application.Exit();
            };
            t.Start();
        }

        void SendReport(string mod)
        {
            if (string.IsNullOrEmpty(mod)) return;
            string text = ReadText(Path.Combine(ModsDir, mod, "report.txt"));
            Send(new { op = "report", mod, text = text ?? "" });
        }

        void SendLogErrors()
        {
            var lines = new List<string>();
            string text = ReadText(Path.Combine(Path.GetDirectoryName(ModsDir), "UE4SS.log"));
            if (text != null)
            {
                string[] keys = { "error", "failed", "exception", "not installable", "fatal" };
                foreach (string line in text.Split('\n'))
                {
                    string l = line.TrimEnd('\r');
                    string low = l.ToLowerInvariant();
                    if (keys.Any(k => low.Contains(k))) lines.Add(l);
                }
            }
            string head = text == null ? "" : string.Join("\n", text.Split('\n').Take(3)).Trim();
            Send(new { op = "log", found = text != null, head, lines = lines.Skip(Math.Max(0, lines.Count - 200)).ToList() });
        }

        void Poll()
        {
            ticks++;
            string game = ReadText(Path.Combine(BridgeDir, "game.json"));
            if (game != null && game != gameText)
            {
                gameText = game;
                Dictionary<string, object> g = null;
                try { g = json.Deserialize<Dictionary<string, object>>(game); } catch { }
                if (g != null)
                {
                    string newPhase = Str(g, "phase") ?? "";
                    dllState = Str(g, "dll") ?? dllState;
                    if (newPhase != phase)
                    {
                        phase = newPhase;
                        var core = CoreFlags();
                        if (fromGame && phase == "menu" && core.Item1) ShowNow();
                        else if (fromGame && phase == "world" && core.Item2 && Visible) HideToGame();
                    }
                    Send(new { op = "game", phase, dll = dllState, gameRunning = GameRunning() });
                }
            }
            if (ticks % 3 == 0) CheckSettingsChanged();
            if (ticks % 10 == 0)
            {
                bool running = GameRunning();
                if (running != lastRunning)
                {
                    lastRunning = running;
                    OnRunningChanged(running);
                    Send(new { op = "game", phase, dll = dllState, gameRunning = running });
                    Send(mgr.Snapshot());
                }
            }
            if (fromGame && ticks % 7 == 0)
            {
                if (GameRunning()) gameMissing = 0;
                else if (++gameMissing >= 3) { quitting = true; SaveBounds(); Application.Exit(); }
            }
        }
        bool lastRunning;

        void CheckSettingsChanged()
        {
            foreach (var kv in stamps.ToList())
            {
                DateTime now = File.Exists(kv.Key) ? File.GetLastWriteTimeUtc(kv.Key) : DateTime.MinValue;
                if (now == kv.Value) continue;
                stamps[kv.Key] = now;
                string name = Path.GetFileNameWithoutExtension(kv.Key);
                Send(new { op = "settings", mod = name, text = ReadText(kv.Key) });
            }
        }

        Tuple<bool, bool> CoreFlags()
        {
            bool autoOpen = true, hideInWorld = true;
            try
            {
                string t = ReadText(Path.Combine(SettingsDir, "RelicCore.json"));
                if (t != null)
                {
                    var d = json.Deserialize<Dictionary<string, object>>(t);
                    if (d != null && d.ContainsKey("values") && d["values"] is Dictionary<string, object> v)
                    {
                        if (v.ContainsKey("AutoOpen") && v["AutoOpen"] is bool a) autoOpen = a;
                        if (v.ContainsKey("HideInWorld") && v["HideInWorld"] is bool h) hideInWorld = h;
                    }
                }
            }
            catch { }
            return Tuple.Create(autoOpen, hideInWorld);
        }

        void Toggle()
        {
            if (Visible && WindowState != FormWindowState.Minimized && GetForegroundWindow() == Handle) HideToGame();
            else ShowNow();
        }

        void ShowNow()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            if (GameRunning()) TopMost = true;
            Activate();
            BringToFront();
            SetForegroundWindow(Handle);
            Send(new { op = "shown" });
        }

        void HideToGame()
        {
            SaveBounds();
            IntPtr game = GameWindow();
            if (game == IntPtr.Zero && !fromGame) { WindowState = FormWindowState.Minimized; return; }
            Hide();
            if (game != IntPtr.Zero) SetForegroundWindow(game);
        }

        static Process[] GameProcesses()
        {
            return Process.GetProcesses().Where(p =>
            {
                try { return p.ProcessName.StartsWith("RSDragonwilds", StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            }).ToArray();
        }

        static bool GameRunning() => GameProcesses().Length > 0;

        static IntPtr GameWindow()
        {
            foreach (var p in GameProcesses())
            {
                try { if (p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle; } catch { }
            }
            return IntPtr.Zero;
        }

        void OpenExternal(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            string[] allowed = { "https://www.nexusmods.com/", "https://nexusmods.com/", "https://discord.gg/", "https://relicmods.pages.dev/", "https://www.curseforge.com/" };
            if (!allowed.Any(a => url.StartsWith(a, StringComparison.OrdinalIgnoreCase))) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        void Remember(string file)
        {
            stamps[file] = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
        }

        Rectangle LoadBounds()
        {
            var screen = Screen.PrimaryScreen.WorkingArea;
            int w = Math.Min(1240, screen.Width - 80), h = Math.Min(800, screen.Height - 80);
            var def = new Rectangle(screen.Left + (screen.Width - w) / 2, screen.Top + (screen.Height - h) / 2, w, h);
            string t = ReadText(Path.Combine(Store.Home, "window.txt"));
            if (t == null) return def;
            var parts = t.Trim().Split(' ').Select(s => int.TryParse(s, out int n) ? n : int.MinValue).ToArray();
            if (parts.Length != 4 || parts.Any(n => n == int.MinValue)) return def;
            var r = new Rectangle(parts[0], parts[1], Math.Max(980, parts[2]), Math.Max(640, parts[3]));
            return Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(r)) ? r : def;
        }

        void SaveBounds()
        {
            try
            {
                var r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                if (r == saved) return;
                saved = r;
                Directory.CreateDirectory(Store.Home);
                File.WriteAllText(Path.Combine(Store.Home, "window.txt"), $"{r.X} {r.Y} {r.Width} {r.Height}");
            }
            catch { }
        }

        static string Str(Dictionary<string, object> m, string key) =>
            m != null && m.TryGetValue(key, out object v) && v != null ? v.ToString() : null;

        static int ReadInt(string file)
        {
            string t = ReadText(file);
            return t != null && int.TryParse(t.Trim(), out int n) ? n : 0;
        }

        static string ReadText(string file)
        {
            try
            {
                if (!File.Exists(file)) return null;
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, new UTF8Encoding(false), true))
                    return sr.ReadToEnd();
            }
            catch { return null; }
        }
    }
}
