// Cuida tu Batería. Copyright (C) 2026 Alexis Reyes, Reyes Project.
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using RP;

namespace CuidaTuBateria
{
    internal static class Program
    {
        public const string Version = "6.1";
        public const string SourceUrl = "github.com/alexisreyes02/cuida-tu-bateria";
        public const string ExitEventName = "CuidaTuBateria.Exit.RP";

        [DllImport("kernel32.dll")] static extern bool SetDefaultDllDirectories(uint flags);

        [STAThread]
        static void Main(string[] args)
        {
            // Load system libraries only from System32, never from the folder the exe sits in.
            try { SetDefaultDllDirectories(0x800); } catch { }
            // Any error that slips through is written to errores.log instead of showing a dialog.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Log.Write("ui", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("domain", e.ExceptionObject as Exception);

            bool first;
            using (var mutex = new Mutex(true, "CuidaTuBateria.SingleInstance.RP", out first))
            {
                var showEv = new EventWaitHandle(false, EventResetMode.AutoReset, "CuidaTuBateria.Show.RP");
                if (!first) { showEv.Set(); return; }
                // The installer sets this event to ask for a clean exit before an update or removal.
                var exitEv = new EventWaitHandle(false, EventResetMode.ManualReset, ExitEventName);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool toTray = Array.IndexOf(args, "--tray") >= 0;
                Paths.Migrate();
                HealthStore.CleanReports();
                var form = new MainForm(toTray);
                var t = new Thread(() =>
                {
                    try
                    {
                        while (true)
                        {
                            int which = WaitHandle.WaitAny(new WaitHandle[] { showEv, exitEv });
                            for (int i = 0; i < 50 && !form.IsHandleCreated; i++) Thread.Sleep(100);
                            if (form.IsDisposed) return;
                            if (which == 1) { form.BeginInvoke(new Action(form.ExitNow)); return; }
                            form.BeginInvoke(new Action(form.Reveal));
                        }
                    }
                    catch { }
                }) { IsBackground = true };
                t.Start();
                Application.Run(form);
                // The handles are left to the process exit on purpose: the watcher thread may still be waiting on them.
            }
        }
    }

    internal sealed class Settings
    {
        public int Upper = 80;
        public int Lower = 20;
        public bool Notify = true;
        public bool HintShown;
        public bool IntroDone;
        public string Lang = "auto";             // auto, es, en
        public long ApagonUntilUtc;               // ticks; 0 means off
        public double Drained;                    // percentage points used on battery since Since
        public string Since = "";                 // yyyy-MM-dd the count started
        public string WeeklyShown = "";           // yyyy-MM-dd of the last Sunday summary
        public bool AlwaysPlugged;                // point of sale, desk with UPS: never ask to unplug

        static string LegacyFile { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChargeAndForget", "settings.ini"); } }
        static string FilePath { get { return Path.Combine(Paths.Dir, "settings.ini"); } }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                var lines = SafeFile.ReadLines(FilePath) ?? (File.Exists(LegacyFile) ? File.ReadAllLines(LegacyFile) : null);
                if (lines != null)
                {
                    foreach (var line in lines)
                    {
                        int i = line.IndexOf('=');
                        if (i < 1) continue;
                        string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                        int n; long l; double d;
                        if (k == "upper" && int.TryParse(v, out n)) s.Upper = Math.Max(50, Math.Min(100, n));
                        else if (k == "lower" && int.TryParse(v, out n)) s.Lower = Math.Max(5, Math.Min(45, n));
                        else if (k == "notify") s.Notify = v == "1";
                        else if (k == "hint") s.HintShown = v == "1";
                        else if (k == "intro") s.IntroDone = v == "1";
                        else if (k == "lang") s.Lang = v == "es" || v == "en" ? v : "auto";
                        else if (k == "apagon" && long.TryParse(v, out l)) s.ApagonUntilUtc = l;
                        else if (k == "drenado" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.Drained = Math.Max(0, d);
                        else if (k == "desde") s.Since = v;
                        else if (k == "resumen") s.WeeklyShown = v;
                        else if (k == "vive_conectada") s.AlwaysPlugged = v == "1";
                    }
                }
            }
            catch { }
            if (s.Lower > s.Upper - 10) s.Lower = s.Upper - 10;
            MigrateLegacy(s);
            return s;
        }

        // Charge & Forget kept its settings under the old name. Copy them to the new folder once,
        // confirm the copy is on disk, then remove the old file and its folder if it is empty.
        static void MigrateLegacy(Settings s)
        {
            try
            {
                if (!File.Exists(LegacyFile)) return;
                if (!File.Exists(FilePath)) s.Save();
                if (!File.Exists(FilePath)) return;
                File.Delete(LegacyFile);
                string legacyDir = Path.GetDirectoryName(LegacyFile);
                if (Directory.Exists(legacyDir) && Directory.GetFileSystemEntries(legacyDir).Length == 0) Directory.Delete(legacyDir);
            }
            catch { }
        }

        public void Save()
        {
            try
            {
                SafeFile.Write(FilePath, string.Join("\n", new[] {
                    "upper=" + Upper, "lower=" + Lower, "notify=" + (Notify ? "1" : "0"),
                    "hint=" + (HintShown ? "1" : "0"), "intro=" + (IntroDone ? "1" : "0"), "lang=" + Lang,
                    "apagon=" + ApagonUntilUtc, "drenado=" + Drained.ToString("0.#", CultureInfo.InvariantCulture),
                    "desde=" + Since, "resumen=" + WeeklyShown, "vive_conectada=" + (AlwaysPlugged ? "1" : "0") }) + "\n");
            }
            catch { }
        }

        public bool ApagonOn { get { return ApagonUntilUtc > DateTime.UtcNow.Ticks; } }
    }

    internal static class Autorun
    {
        const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "CuidaTuBateria";
        const string Legacy = "ChargeAndForget";

        public static bool IsOn()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(KeyPath)) return k != null && k.GetValue(Name) != null; }
            catch { return false; }
        }

        public static void Set(bool on)
        {
            try
            {
                // CreateSubKey also opens it, and works on a fresh profile where Run does not exist yet.
                using (var k = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    if (k == null) return;
                    k.DeleteValue(Legacy, false);
                    if (on) k.SetValue(Name, "\"" + Application.ExecutablePath + "\" --tray");
                    else k.DeleteValue(Name, false);
                }
            }
            catch { }
        }
    }

    internal sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(12, 13, 18); } }
        public override Color MenuBorder { get { return Color.FromArgb(90, 242, 163, 58); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(242, 163, 58); } }
        public override Color MenuItemSelected { get { return Color.FromArgb(62, 46, 22); } }
        public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(62, 46, 22); } }
        public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(62, 46, 22); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(12, 13, 18); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(12, 13, 18); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(12, 13, 18); } }
        public override Color SeparatorDark { get { return Color.FromArgb(70, 60, 42); } }
        public override Color SeparatorLight { get { return Color.FromArgb(12, 13, 18); } }
        public override Color CheckBackground { get { return Color.FromArgb(176, 104, 24); } }
        public override Color CheckSelectedBackground { get { return Color.FromArgb(176, 104, 24); } }
    }

    internal sealed class Hit
    {
        public RectangleF R;
        public Action Act;
        public bool Hover;
        public string Name;   // read aloud when the keyboard lands on it
        public Hit(RectangleF r, Action a, string name) { R = r; Act = a; Name = name; }
    }

    internal enum St { NoBattery, Unplug, PlugIn, Charging, OnBattery }
    internal enum View { Now, Mine }

    internal sealed class MainForm : Form
    {
        const int W = 860, H = 572;
        const float RX = 440, RW = 392;   // right column of the "Ahora" view
        static readonly StringFormat CenterFmt = Theme.Center;
        static readonly StringFormat FarFmt = Theme.Far;

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        const int WM_DPICHANGED = 0x02E0;

        readonly Settings cfg = Settings.Load();
        readonly UsageLog usage = new UsageLog();
        float scale;
        Cosmos cosmos;
        readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer { Interval = 50 };
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 5000 };
        // A worn connector can flicker between charger and battery. The plug state counts only after
        // it has held for a few seconds; this timer comes back to check.
        readonly System.Windows.Forms.Timer settle = new System.Windows.Forms.Timer { Interval = 8000 };
#if PREVIEW
        bool seqMode = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CF_SEQ"));
        bool fakeMode = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CF_FAKE"));
#endif
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        ToolStripMenuItem miOpen, miMine, miAuto, miApagon, miLang, miLangAuto, miLangEs, miLangEn, miExit;
        readonly Icon icRest;
        readonly Icon[] fxCharge, fxUnplug, fxPlug;
        const int Frames = 12;
        readonly System.Windows.Forms.Timer trayAnim = new System.Windows.Forms.Timer { Interval = 150 };
        Icon[] fxCurrent;
        Icon iconNow;
        string tipNow = "";
        int frame;
        readonly List<Hit> hits = new List<Hit>();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly bool startHidden;

        // hit areas
        Hit hMin, hClose, hTabNow, hTabMine, hUpMinus, hUpPlus, hLoMinus, hLoPlus, hAuto, hNotify, hApagon, hAlways, hImage, hReport, hIntroOk, hIntroKeep, hAbout, hAboutClose;
        bool about;              // the "Acerca de" panel is open
        int focus = -1;          // keyboard focus, index into hits
        bool showFocus;

        View view = View.Now;
        bool intro;
        St st = St.OnBattery;
        int pct = 50, winSeconds = -1, critical = 5;
        int holdReads;           // consecutive readings plugged in, not charging, below 97 %: a factory charge limit at work
        bool desktop;            // a desktop PC whose "battery" is a UPS
        bool chassisKnown;       // no reminder until the chassis check says this is a laptop
        float measureAfter;      // seconds since start before the daily health reading may run
        float trayAnimStart;
        bool plugged, charging, rawPlugged, firstRead = true;
        DateTime rawSinceUtc;
        float shown = 0;
        float lastT;
        PointF mouse = new PointF(W / 2f, H / 2f);
        volatile HealthInfo health;
        volatile bool measuring;
        Autonomy auto = new Autonomy();
        double watts = -1;                    // draw on battery right now, smoothed
        DateTime wattsAtUtc = DateTime.MinValue;
        bool wattsBusy;
        double chargeMin = -1;   // minutes to reach the upper number, while charging
        UsageStats week = new UsageStats();
        string note = ""; float noteUntil;
        bool allowClose, noBattery;
        DateTime lastSettingsSave = DateTime.MinValue;
        readonly Toast toast = new Toast();
        // Reminder episode: starts when a threshold is crossed, ends only when the plug state really changes
        // (that earns the celebration) or, silently, when the percent moves well back inside the safe zone.
        St episode = St.OnBattery;
        DateTime remindAtUtc = DateTime.MinValue, lastPopupUtc = DateTime.MinValue;
        float episodeStart;
        bool dismissed, autoOn, thresholdMoved;
        const int Hysteresis = 5;
        readonly PowerModeChangedEventHandler powerHandler;
        bool animate = true;

        public MainForm(bool toTray)
        {
            ApplyLanguage();
            startHidden = toTray;
            using (var gd = Graphics.FromHwnd(IntPtr.Zero)) scale = Math.Max(1f, gd.DpiX / 96f);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size((int)(W * scale), (int)(H * scale));
            BackColor = Theme.Bg;
            Text = "Cuida tu Batería";
            KeyPreview = true;
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            animate = SystemInformation.UIEffectsEnabled;
            intro = !cfg.IntroDone;

            icRest = Bolt.MakeIcon(Theme.GoldSoft, Theme.GoldDeep, 32);
            fxCharge = Frameset(Color.FromArgb(255, 224, 160), Color.FromArgb(200, 122, 30));
            fxUnplug = Frameset(Color.FromArgb(255, 244, 214), Color.FromArgb(242, 140, 30));
            fxPlug = Frameset(Color.FromArgb(255, 214, 170), Color.FromArgb(214, 96, 32));   // ember: blue is light only
            trayAnim.Tick += delegate { if (fxCurrent == null) return; frame = (frame + 1) % Frames; SetTrayIcon(fxCurrent[frame]); };

            CreateHits();
            BuildHits();
            BuildTray();
            toast.Snoozed += delegate { remindAtUtc = DateTime.UtcNow.AddMinutes(15); ApplyTray(); };
            toast.Dismissed += delegate { dismissed = true; ApplyTray(); };

            usage.Load();
            usage.Drained = cfg.Drained;
            if (cfg.Since.Length == 0) { cfg.Since = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); cfg.Save(); }
            usage.DrainedChanged += delegate
            {
                cfg.Drained = usage.Drained;
                if (DateTime.UtcNow - lastSettingsSave > TimeSpan.FromMinutes(5)) { cfg.Save(); lastSettingsSave = DateTime.UtcNow; }
            };
            health = HealthStore.Load();
#if PREVIEW
            PreviewData();
            var pv = Environment.GetEnvironmentVariable("CF_VIEW");
            if (pv == "Mine") { view = View.Mine; BuildHits(); }
            if (Environment.GetEnvironmentVariable("CF_INTRO") == "1") { intro = true; BuildHits(); }
            else if (fakeMode || seqMode) { intro = false; BuildHits(); }
            if (Environment.GetEnvironmentVariable("CF_APAGON") == "1") cfg.ApagonUntilUtc = DateTime.UtcNow.AddHours(24).Ticks;
            if (Environment.GetEnvironmentVariable("CF_ALWAYS") == "1") cfg.AlwaysPlugged = true;
            if (Environment.GetEnvironmentVariable("CF_ABOUT") == "1") { about = true; BuildHits(); }
            if (Environment.GetEnvironmentVariable("CF_FOCUS") != null) { showFocus = true; focus = int.Parse(Environment.GetEnvironmentVariable("CF_FOCUS")); }
#endif
            anim.Tick += delegate { Tick(); };
            poll.Tick += delegate { ReadBattery(); MeasureHealthIfStale(); WeeklySummary(); };
            settle.Tick += delegate { settle.Stop(); ReadBattery(); };
            powerHandler = delegate (object s, PowerModeChangedEventArgs e)
            {
                if (e.Mode == PowerModes.Suspend) { try { SaveAll(); } catch { } }
                try { if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(ReadBattery)); } catch { }
            };
            SystemEvents.PowerModeChanged += powerHandler;
            endingHandler = delegate { try { SaveAll(); } catch { } };
            SystemEvents.SessionEnding += endingHandler;
            autoOn = Autorun.IsOn();
            // The first daily health reading waits a little, so it does not compete with Windows starting up.
            measureAfter = health == null ? 20f : 240f;
            ThreadPool.QueueUserWorkItem(delegate
            {
                int c = PowerInfo.CriticalLevel();
                bool d = IsDesktop();
                try { BeginInvoke(new Action(delegate { critical = c; desktop = d; chassisKnown = true; ReadBattery(); })); } catch { }
            });
            ReadBattery();
#if PREVIEW
            if (seqMode) poll.Interval = 700;
            if (Environment.GetEnvironmentVariable("CF_DESKTOP") == "1") desktop = true;
            if (seqMode || fakeMode) chassisKnown = true;
#endif
            poll.Start();
        }

        readonly SessionEndingEventHandler endingHandler;

        // A desktop with a USB UPS reports a "battery" that is always plugged in and full. The chassis type
        // tells a desktop from a laptop; anything unknown is treated as a laptop.
        static bool IsDesktop()
        {
            try
            {
                using (var q = new System.Management.ManagementObjectSearcher(@"root\CIMV2", "SELECT ChassisTypes FROM Win32_SystemEnclosure"))
                    foreach (System.Management.ManagementObject o in q.Get())
                    {
                        var types = o["ChassisTypes"] as ushort[];
                        if (types == null || types.Length == 0) return false;
                        int[] portable = { 8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32 };
                        int[] fixedPc = { 3, 4, 5, 6, 7, 13, 15, 16, 17, 23, 24, 34, 35, 36 };
                        foreach (var t in types) if (Array.IndexOf(portable, (int)t) >= 0) return false;
                        foreach (var t in types) if (Array.IndexOf(fixedPc, (int)t) >= 0) return true;
                    }
            }
            catch { }
            return false;
        }

        // Full-screen video, a presentation or a game: Windows says it is not a good moment.
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
        static bool BusyScreen()
        {
            try { int s; if (SHQueryUserNotificationState(out s) == 0) return s == 2 || s == 3 || s == 4; } catch { }
            return false;
        }

        void ApplyLanguage()
        {
            if (cfg.Lang == "es") L.En = false;
            else if (cfg.Lang == "en") L.En = true;
            else L.Auto();
#if PREVIEW
            if (Environment.GetEnvironmentVariable("CF_LANG") == "en") L.En = true;
#endif
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; }
        }

        protected override void SetVisibleCore(bool value)
        {
            if (startHidden && !IsHandleCreated) { value = false; CreateHandle(); }
            base.SetVisibleCore(value);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && WindowState != FormWindowState.Minimized && animate) anim.Start(); else anim.Stop();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) anim.Stop();
            else if (Visible && animate) anim.Start();
        }

        // Full motion only while the window is in front; on battery it halves the frame rate, and behind
        // other windows it idles at 5 frames a second.
        bool active;
        protected override void OnActivated(EventArgs e) { active = true; SetAnimRate(); base.OnActivated(e); }
        protected override void OnDeactivate(EventArgs e) { active = false; SetAnimRate(); base.OnDeactivate(e); }
        // 15 frames a second in front on the charger, 8 on battery, 4 behind other windows.
        void SetAnimRate() { anim.Interval = !active ? 250 : (plugged ? 66 : 125); }

        // The star field exists only while the window is visible: in the tray it costs nothing.
        void EnsureCosmos() { if (cosmos == null) cosmos = new Cosmos(W, H, scale, W * 0.60f, 52f, 15f, 64); }
        void DropCosmos() { if (cosmos != null) { cosmos.Dispose(); cosmos = null; } }

        // Moving the window to a monitor with another scale: redraw at that monitor's size, sharp.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DPICHANGED)
            {
                try
                {
                    int dpi = (int)(m.WParam.ToInt64() & 0xFFFF);
                    var rc = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                    float s = Math.Max(1f, dpi / 96f);
                    if (Math.Abs(s - scale) > 0.01f)
                    {
                        scale = s;
                        DropCosmos();
                    }
                    SetBounds(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
                    Invalidate();
                }
                catch (Exception ex) { Log.Write("dpi", ex); }
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        public void Reveal()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        // Asked by the installer, or from the tray: save, take the tray icon away cleanly and leave.
        public void ExitNow()
        {
            SaveAll();
            HealthStore.CleanReports();
            allowClose = true; tray.Visible = false; Application.Exit();
        }

        void SaveAll() { usage.Flush(); cfg.Drained = usage.Drained; cfg.Save(); }

        // ---------- layout ----------
        void CreateHits()
        {
            hMin = new Hit(new RectangleF(W - 92, 16, 34, 32), delegate { WindowState = FormWindowState.Minimized; }, "min");
            hClose = new Hit(new RectangleF(W - 52, 16, 34, 32), HideToTray, "close");
            hTabNow = new Hit(new RectangleF(W - 304, 18, 74, 34), delegate { SetView(View.Now); }, "now");
            hTabMine = new Hit(new RectangleF(W - 228, 18, 118, 34), delegate { SetView(View.Mine); }, "mine");
            hUpMinus = new Hit(new RectangleF(RX + 214, 404, 32, 32), delegate { SetUpper(cfg.Upper - 5); }, "up-");
            hUpPlus = new Hit(new RectangleF(RX + 320, 404, 32, 32), delegate { SetUpper(cfg.Upper + 5); }, "up+");
            hLoMinus = new Hit(new RectangleF(RX + 214, 438, 32, 32), delegate { SetLower(cfg.Lower - 5); }, "lo-");
            hLoPlus = new Hit(new RectangleF(RX + 320, 438, 32, 32), delegate { SetLower(cfg.Lower + 5); }, "lo+");
            hAuto = new Hit(new RectangleF(RX, 488, 206, 34), ToggleAuto, "auto");
            hNotify = new Hit(new RectangleF(RX + 216, 488, 150, 34), delegate { cfg.Notify = !cfg.Notify; cfg.Save(); }, "notify");
            hApagon = new Hit(new RectangleF(28, 490, 218, 36), ToggleApagon, "apagon");
            hAlways = new Hit(new RectangleF(254, 490, 166, 36), ToggleAlways, "always");
            hImage = new Hit(new RectangleF(28, 492, 196, 36), SaveImage, "image");
            hReport = new Hit(new RectangleF(236, 492, 236, 36), OpenWindowsReport, "report");
            hIntroOk = new Hit(new RectangleF(W - 330, H - 128, 190, 40), delegate { CloseIntro(true); }, "ok");
            hIntroKeep = new Hit(new RectangleF(W - 540, H - 128, 196, 40), delegate { CloseIntro(false); }, "keep");
            hAbout = new Hit(new RectangleF(160, H - 38, 90, 28), delegate { about = true; focus = -1; BuildHits(); }, "about");
            hAboutClose = new Hit(new RectangleF(W - 330, H - 128, 190, 40), delegate { about = false; focus = -1; BuildHits(); }, "aboutclose");
        }

        void BuildHits()
        {
            hits.Clear();
            if (about)
            {
                hits.Add(hAboutClose); hits.Add(hMin); hits.Add(hClose);
            }
            else if (intro)
            {
                if (IntroHasOldNumbers) hits.Add(hIntroKeep);
                hits.Add(hIntroOk);
                hits.Add(hMin); hits.Add(hClose);
            }
            else
            {
                hits.Add(hTabNow); hits.Add(hTabMine);
                if (view == View.Now) { hits.Add(hUpMinus); hits.Add(hUpPlus); hits.Add(hLoMinus); hits.Add(hLoPlus); hits.Add(hAuto); hits.Add(hNotify); hits.Add(hApagon); hits.Add(hAlways); }
                else { hits.Add(hImage); hits.Add(hReport); }
                hits.Add(hAbout); hits.Add(hMin); hits.Add(hClose);
            }
            if (focus >= hits.Count) focus = hits.Count - 1;
        }

        bool IntroHasOldNumbers { get { return cfg.Upper != 80 || cfg.Lower != 20; } }

        void CloseIntro(bool useDefaults)
        {
            if (useDefaults && IntroHasOldNumbers) { cfg.Upper = 80; cfg.Lower = 20; thresholdMoved = true; }
            cfg.IntroDone = true; cfg.Save();
            intro = false; focus = -1; BuildHits(); Evaluate();
        }

        void SetView(View v)
        {
            view = v;
            if (v == View.Mine) week = usage.Week(DateTime.UtcNow, cfg.Upper);
            BuildHits(); Invalidate();
        }

        void SetUpper(int v)
        {
            v = Math.Max(50, Math.Min(100, v));
            if (v - cfg.Lower < 10) v = cfg.Lower + 10;
            cfg.Upper = v; cfg.Save(); thresholdMoved = true; Evaluate();
        }

        void SetLower(int v)
        {
            v = Math.Max(5, Math.Min(45, v));
            if (cfg.Upper - v < 10) v = cfg.Upper - 10;
            cfg.Lower = v; cfg.Save(); thresholdMoved = true; Evaluate();
        }

        void ToggleAuto() { Autorun.Set(!autoOn); autoOn = Autorun.IsOn(); miAuto.Checked = autoOn; Invalidate(); }

        // Blackout mode: on days with power cuts it makes sense to keep the battery full. For 24 hours
        // the app stops asking to unplug; everything else keeps working.
        void ToggleApagon()
        {
            cfg.ApagonUntilUtc = cfg.ApagonOn ? 0 : DateTime.UtcNow.AddHours(24).Ticks;
            cfg.Save();
            if (cfg.ApagonOn && episode == St.Unplug) EndEpisode(false);
            miApagon.Checked = cfg.ApagonOn;
            Evaluate();
        }

        // Always plugged in: a point of sale or a laptop on a desk with a UPS. Asking it to unplug every
        // day would only teach the person to ignore the app, so the unplug reminder goes away for good
        // and the plug-in reminder stays.
        void ToggleAlways()
        {
            cfg.AlwaysPlugged = !cfg.AlwaysPlugged; cfg.Save();
            if (cfg.AlwaysPlugged && episode == St.Unplug) EndEpisode(false);
            miAlways.Checked = cfg.AlwaysPlugged;
            Evaluate();
        }

        string ApagonUntilText()
        {
            var until = new DateTime(cfg.ApagonUntilUtc, DateTimeKind.Utc).ToLocalTime();
            string day = until.Date == DateTime.Now.Date ? L.T("hoy", "today") : L.T("mañana", "tomorrow");
            return Txt.Clock(until) + " " + day;
        }

        void HideToTray()
        {
            Hide();
            DropCosmos();
            about = false; BuildHits();
            if (!cfg.HintShown)
            {
                cfg.HintShown = true; cfg.Save();
                tray.ShowBalloonTip(5000, "Cuida tu Batería", L.T("Sigo pendiente de tu batería desde aquí, al lado del reloj.", "I keep an eye on your battery from here, next to the clock."), ToolTipIcon.Info);
            }
        }

        void Note(string s) { note = s; noteUntil = (float)clock.Elapsed.TotalSeconds + 6f; Invalidate(); }

        void SaveImage()
        {
            var h = health;
            if (h == null || h.Months.Count == 0) { Note(L.T("Todavía no hay datos para la imagen.", "No data for the picture yet.")); return; }
            try
            {
                string f = HealthChart.Export(h, Brand.Name(h.Maker, h.Model));
                Note(L.T("Guardada en Imágenes.", "Saved in Pictures."));
                try { Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "/select,\"" + f + "\""); } catch { }
            }
            catch (Exception ex) { Note(L.T("No pude guardarla. Windows dijo: ", "I could not save it. Windows said: ") + ex.Message); }
        }

        void OpenWindowsReport()
        {
            Note(L.T("Preparando el informe de Windows...", "Preparing the Windows report..."));
            ThreadPool.QueueUserWorkItem(delegate
            {
                string f = null;
                try { f = HealthStore.WriteWindowsReport(); } catch { }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (f == null) { Note(L.T("Windows no quiso generar el informe.", "Windows would not create the report.")); return; }
                        try { Process.Start(new ProcessStartInfo(f) { UseShellExecute = true }); Note(L.T("Abierto en tu navegador. Viene en inglés.", "Opened in your browser.")); }
                        catch { Note(L.T("No pude abrirlo.", "I could not open it.")); }
                    }));
                }
                catch { }
            });
        }

        // Sunday evening, once a week: a short, kind summary. No scolding.
        string weeklyChecked = "";
        void WeeklySummary()
        {
            var now = DateTime.Now;
            string today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (now.DayOfWeek != DayOfWeek.Sunday || now.Hour < 18 || cfg.WeeklyShown == today || weeklyChecked == today || noBattery) return;
            weeklyChecked = today;
            var w = usage.Week(DateTime.UtcNow, cfg.Upper);
            if (w.Days < 3) return;
            cfg.WeeklyShown = today; cfg.Save();
            string msg = L.T("Con batería: " + Txt.Dur(w.BatteryHours * 60) + ". Desconectada " + w.Unplugs + (w.Unplugs == 1 ? " vez" : " veces") + ".",
                             "On battery: " + Txt.Dur(w.BatteryHours * 60) + ". Unplugged " + w.Unplugs + (w.Unplugs == 1 ? " time" : " times") + ".");
            if (w.HighHours > 0.1) msg += L.T(" Pasó " + Txt.Dur(w.HighHours * 60) + " conectada sobre " + L.P(cfg.Upper) + ".", " It spent " + Txt.Dur(w.HighHours * 60) + " plugged in above " + L.P(cfg.Upper) + ".");
            else msg += L.T(" Casi no pasó tiempo llena y conectada. Va bien.", " It spent almost no time full on the charger. Nicely done.");
            tray.ShowBalloonTip(10000, L.T("Tu semana con la batería", "Your battery this week"), msg, ToolTipIcon.Info);
        }

        // ---------- tray ----------
        ToolStripMenuItem miAlways, miAbout;

        void BuildTray()
        {
            menu.Renderer = new ToolStripProfessionalRenderer(new DarkColors());
            menu.BackColor = Color.FromArgb(12, 13, 18);
            menu.ForeColor = Theme.Text;
            menu.Font = new Font("Segoe UI", 9.5f);
            miOpen = new ToolStripMenuItem();
            miOpen.Font = new Font(menu.Font, FontStyle.Bold);
            miOpen.Click += delegate { SetView(View.Now); Reveal(); };
            miMine = new ToolStripMenuItem();
            miMine.Click += delegate { SetView(View.Mine); Reveal(); };
            miApagon = new ToolStripMenuItem();
            miApagon.Click += delegate { ToggleApagon(); };
            miAlways = new ToolStripMenuItem();
            miAlways.Click += delegate { ToggleAlways(); };
            miAuto = new ToolStripMenuItem();
            miAuto.Click += delegate { ToggleAuto(); };
            miLang = new ToolStripMenuItem();
            miLangAuto = new ToolStripMenuItem(); miLangAuto.Click += delegate { SetLang("auto"); };
            miLangEs = new ToolStripMenuItem("Español"); miLangEs.Click += delegate { SetLang("es"); };
            miLangEn = new ToolStripMenuItem("English"); miLangEn.Click += delegate { SetLang("en"); };
            miLang.DropDownItems.AddRange(new ToolStripItem[] { miLangAuto, miLangEs, miLangEn });
            miLang.DropDown.BackColor = menu.BackColor; miLang.DropDown.ForeColor = menu.ForeColor;
            miAbout = new ToolStripMenuItem();
            miAbout.Click += delegate { about = true; BuildHits(); Reveal(); };
            miExit = new ToolStripMenuItem();
            miExit.Click += delegate { ExitNow(); };
            menu.Items.AddRange(new ToolStripItem[] { miOpen, miMine, new ToolStripSeparator(), miApagon, miAlways, miAuto, miLang, new ToolStripSeparator(), miAbout, miExit });
            menu.Opening += delegate { autoOn = Autorun.IsOn(); MenuTexts(); };
            MenuTexts();
            tray.ContextMenuStrip = menu;
            SetTrayIcon(icRest);
            tray.Text = "Cuida tu Batería";
            tray.Visible = true;
            tray.MouseClick += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Reveal(); };
        }

        void MenuTexts()
        {
            miOpen.Text = L.T("Abrir Cuida tu Batería", "Open Cuida tu Batería");
            miMine.Text = L.T("Mi batería", "My battery");
            miApagon.Text = L.T("Modo apagón (cargar a 100 % por 24 h)", "Blackout mode (charge to 100% for 24 h)");
            miApagon.Checked = cfg.ApagonOn;
            miAlways.Text = L.T("Vive conectada (no pedir desconectar)", "Always plugged in (never ask to unplug)");
            miAlways.Checked = cfg.AlwaysPlugged;
            miAuto.Text = L.T("Iniciar con Windows", "Start with Windows");
            miAuto.Checked = autoOn;
            miLang.Text = L.T("Idioma", "Language");
            miLangAuto.Text = L.T("Automático", "Automatic");
            miLangAuto.Checked = cfg.Lang == "auto"; miLangEs.Checked = cfg.Lang == "es"; miLangEn.Checked = cfg.Lang == "en";
            miAbout.Text = L.T("Acerca de", "About");
            miExit.Text = L.T("Salir", "Exit");
        }

        void SetLang(string lang)
        {
            cfg.Lang = lang; cfg.Save();
            ApplyLanguage(); MenuTexts(); tipNow = ""; ApplyTray(); UpdateTitle(); Invalidate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!allowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideToTray(); return; }
            SaveAll();
            HealthStore.CleanReports();
            tray.Visible = false;
            base.OnFormClosing(e);
        }

        // ---------- battery ----------
        void ReadBattery()
        {
            var now = DateTime.UtcNow;
            try
            {
                var ps = SystemInformation.PowerStatus;
                // Unknown is 255 (every bit set), so it must be compared exactly, never as a flag.
                var cs = ps.BatteryChargeStatus;
                bool none = cs != BatteryChargeStatus.Unknown && (cs & BatteryChargeStatus.NoSystemBattery) != 0;
                if (cs == BatteryChargeStatus.Unknown && (ps.BatteryLifePercent < 0f || ps.BatteryLifePercent > 1f)) none = true;
                if (ps.PowerLineStatus != PowerLineStatus.Unknown)
                {
                    bool r = ps.PowerLineStatus == PowerLineStatus.Online;
                    if (firstRead) { plugged = rawPlugged = r; rawSinceUtc = now; }
                    else if (r != rawPlugged) { rawPlugged = r; rawSinceUtc = now; settle.Stop(); settle.Start(); }
                    else if (r != plugged && now - rawSinceUtc >= TimeSpan.FromSeconds(7)) plugged = r;
                    firstRead = false;
                }
                charging = cs != BatteryChargeStatus.Unknown && (cs & BatteryChargeStatus.Charging) != 0;
                noBattery = none;
                pct = none ? 0 : (int)Math.Round(ps.BatteryLifePercent * 100f);
                pct = Math.Max(0, Math.Min(100, pct));
                winSeconds = ps.BatteryLifeRemaining;
            }
            catch { }
            bool record = true;
#if PREVIEW
            var fk = Environment.GetEnvironmentVariable("CF_FAKE");
            var seq = Environment.GetEnvironmentVariable("CF_SEQ");
            if (!string.IsNullOrEmpty(seq)) { var steps = seq.Split(';'); var a2 = steps[Math.Min(seqIdx, steps.Length - 1)].Split(','); seqIdx++; pct = int.Parse(a2[0]); bool rp = a2[1] == "1"; if (Environment.GetEnvironmentVariable("CF_RAW") == "1") { if (rp != rawPlugged) { rawPlugged = rp; rawSinceUtc = now; } else if (rp != plugged && now - rawSinceUtc >= TimeSpan.FromSeconds(7)) plugged = rp; } else plugged = rp; charging = plugged; noBattery = false; if (a2.Length > 2) { cfg.Upper = int.Parse(a2[2]); thresholdMoved = true; } Console.WriteLine("step " + seqIdx + ": pct=" + pct + " raw=" + rp + " plugged=" + plugged + " upper=" + cfg.Upper); }
            if (!string.IsNullOrEmpty(fk)) { var a = fk.Split(','); noBattery = a[0] == "x"; if (!noBattery) { pct = int.Parse(a[0]); plugged = a[1] == "1"; charging = a[2] == "1"; shown = pct; } winSeconds = -1; }
            if (!string.IsNullOrEmpty(fk) || !string.IsNullOrEmpty(seq)) record = false;
#endif
            if (desktop) noBattery = true;
            // A factory limit (Lenovo Conservation, ASUS Balanced, Dell Primarily AC, Windows Smart Charging)
            // stops the charge on its own: plugged in, not charging, well below full.
            holdReads = plugged && !charging && !noBattery && pct < 97 ? holdReads + 1 : 0;
            if (!noBattery)
            {
                if (record) usage.Record(pct, plugged, now);
                var hh = health;
                auto = usage.Estimate(now, pct, plugged, winSeconds, hh == null ? 0 : hh.FullRuntimeMin, critical);
                double cr;
                chargeMin = plugged && charging && pct < cfg.Upper && !cfg.ApagonOn && usage.ChargeRate(now, out cr) ? (cfg.Upper - pct) / cr * 60 : -1;
                if (view == View.Mine) week = usage.Week(now, cfg.Upper);
                SampleWatts(now);
            }
            if (cfg.ApagonUntilUtc != 0 && !cfg.ApagonOn) { cfg.ApagonUntilUtc = 0; cfg.Save(); }
            SetAnimRate();
            Evaluate();
        }

        // Once a minute on battery, ask the battery how many watts it is giving. WMI can be slow,
        // so it runs off the window's thread.
        void SampleWatts(DateTime now)
        {
            if (plugged) { watts = -1; wattsAtUtc = DateTime.MinValue; return; }
#if PREVIEW
            var fw = Environment.GetEnvironmentVariable("CF_WATTS");
            if (!string.IsNullOrEmpty(fw)) { watts = double.Parse(fw, CultureInfo.InvariantCulture); return; }
#endif
            if (wattsBusy || now - wattsAtUtc < TimeSpan.FromSeconds(60)) return;
            wattsBusy = true; wattsAtUtc = now;
            ThreadPool.QueueUserWorkItem(delegate
            {
                double w = PowerInfo.DischargeWatts();
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        wattsBusy = false;
                        if (plugged || w <= 0) return;
                        watts = watts <= 0 ? w : 0.7 * watts + 0.3 * w;
                        Invalidate();
                    }));
                }
                catch { wattsBusy = false; }
            });
        }

        void Evaluate()
        {
            if (noBattery) { st = St.NoBattery; EndEpisode(false); ApplyTray(); UpdateTitle(); Invalidate(); return; }
            bool apagon = cfg.ApagonOn, always = cfg.AlwaysPlugged;
            bool factoryLimit = holdReads >= 2;
            bool limitLikely = plugged && !charging && pct < 97;

            // close the running episode first
            if (episode == St.Unplug)
            {
                if (!plugged) EndEpisode(true);                                  // unplugged: success
                else if (apagon || always || factoryLimit || pct < cfg.Upper - Hysteresis || pct < cfg.Upper && thresholdMoved) EndEpisode(false);
            }
            else if (episode == St.PlugIn)
            {
                if (plugged) EndEpisode(true);                                   // plugged in: success
                else if (pct > cfg.Lower + Hysteresis || pct > cfg.Lower && thresholdMoved) EndEpisode(false);
            }
            thresholdMoved = false;

            // open a new one when a threshold is crossed (on a laptop, once the chassis check has answered)
            if (episode != St.Unplug && episode != St.PlugIn && chassisKnown)
            {
                if (plugged && pct >= cfg.Upper && !apagon && !always && !limitLikely) StartEpisode(St.Unplug);
                else if (!plugged && pct <= cfg.Lower) StartEpisode(St.PlugIn);
            }

            st = (episode == St.Unplug || episode == St.PlugIn) ? episode : (plugged ? St.Charging : St.OnBattery);

            if (episode == St.Unplug || episode == St.PlugIn)
            {
                string left = episode == St.PlugIn && auto.Minutes > 0 ? Txt.Dur(auto.Minutes) : null;
                var now = DateTime.UtcNow;
                if (toast.Active) toast.UpdatePercent(pct, left);
                else if (cfg.Notify && !dismissed && !intro && now >= remindAtUtc)
                {
                    // Never two pop-ups within two minutes, however the charger behaves.
                    bool tooSoon = now - lastPopupUtc < TimeSpan.FromMinutes(2);
#if PREVIEW
                    if (seqMode && Environment.GetEnvironmentVariable("CF_RAW") != "1") tooSoon = false;
#endif
                    if (tooSoon) remindAtUtc = lastPopupUtc.AddMinutes(2);
                    else if (BusyScreen()) remindAtUtc = now.AddMinutes(5);   // not over a presentation or a full-screen video
                    else
                    {
#if PREVIEW
                        Console.WriteLine("   POPUP " + episode);
#endif
                        toast.Present(episode == St.Unplug ? ToastKind.Unplug : ToastKind.PlugIn, pct, left);
                        remindAtUtc = DateTime.MaxValue;
                        lastPopupUtc = now;
                        SystemSounds.Asterisk.Play();
                    }
                }
            }
            if (!animate) shown = pct;
            ApplyTray();
            UpdateTitle();
            Invalidate();
        }

        // The window title is what Alt+Tab and screen readers announce, so it carries the state.
        void UpdateTitle()
        {
            string t = st == St.NoBattery ? "Cuida tu Batería" : "Cuida tu Batería: " + L.P(pct) + ", " + StateWord();
            if (Text != t) Text = t;
        }

        string StateWord()
        {
            switch (st)
            {
                case St.Unplug: return L.T("hora de desconectarla", "time to unplug");
                case St.PlugIn: return L.T("hora de conectarla", "time to plug in");
                case St.Charging: return charging ? L.T("cargando", "charging") : L.T("conectada", "plugged in");
                default: return L.T("con batería", "on battery");
            }
        }

#if PREVIEW
        int seqIdx;
#endif

        void StartEpisode(St kind)
        {
#if PREVIEW
            Console.WriteLine("   START " + kind);
#endif
            episode = kind; dismissed = false; remindAtUtc = DateTime.MinValue; episodeStart = (float)clock.Elapsed.TotalSeconds;
        }

        void EndEpisode(bool success)
        {
            if (episode != St.Unplug && episode != St.PlugIn) return;
            episode = St.OnBattery;
#if PREVIEW
            Console.WriteLine(success ? "   END with celebration" : "   END silently");
#endif
            if (toast.Active) { if (success) toast.Resolve(); else toast.Withdraw(); }
        }

        static Icon[] Frameset(Color top, Color bottom)
        {
            var f = new Icon[Frames];
            for (int i = 0; i < Frames; i++)
                f[i] = Bolt.MakeIcon(top, bottom, 32, (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / Frames)));
            return f;
        }

        void SetTrayIcon(Icon ic) { if (ic != iconNow) { iconNow = ic; tray.Icon = ic; } }

        // The tray bolt breathes while charging and pulses faster when you need to act. The alert pulse
        // calms down after 20 seconds, or as soon as the reminder is snoozed or silenced: the battery is
        // low, and an icon redrawn eleven times a second would spend it.
        void ApplyTray()
        {
            bool alert = st == St.Unplug || st == St.PlugIn;
            var now = DateTime.UtcNow;
            bool snoozed = remindAtUtc != DateTime.MaxValue && remindAtUtc > now;
            bool calm = alert && (dismissed || snoozed || !cfg.Notify || clock.Elapsed.TotalSeconds - episodeStart > 20);
            Icon[] set = st == St.Unplug ? fxUnplug : (st == St.PlugIn ? fxPlug : (plugged && charging ? fxCharge : null));
            // Charging breathes for 30 seconds after it starts, then holds still: the icon still says
            // "charging", and explorer is not asked to redraw the tray seven times a second for hours.
            if (set == fxCharge && fxCurrent != fxCharge && iconNow != fxCharge[Frames / 2]) trayAnimStart = (float)clock.Elapsed.TotalSeconds;
            if (set == fxCharge && clock.Elapsed.TotalSeconds - trayAnimStart > 30) calm = true;
            Icon[] moving = calm ? null : set;
            if (moving != fxCurrent)
            {
                fxCurrent = moving;
                if (moving == null) trayAnim.Stop();
                else { trayAnim.Interval = alert ? 90 : 150; frame = 0; SetTrayIcon(moving[0]); trayAnim.Start(); }
            }
            if (moving == null)
                SetTrayIcon(set != null ? set[Frames / 2] : ((plugged && st != St.NoBattery) ? fxCharge[Frames / 2] : icRest));
            string tip;
            if (st == St.NoBattery) tip = L.T("Cuida tu Batería: sin batería", "Cuida tu Batería: no battery");
            else
            {
                tip = "Cuida tu Batería: " + L.P(pct) + ", " + StateWord();
                if (!plugged && auto.Minutes > 0) tip += L.T(", unos ", ", about ") + Txt.Dur(auto.Minutes);
            }
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            if (tip != tipNow) { tipNow = tip; tray.Text = tip; }
        }

        // Battery health is measured at most once a day, also when the app stays open for weeks.
        void MeasureHealthIfStale()
        {
            if (noBattery || measuring || clock.Elapsed.TotalSeconds < measureAfter) return;
            var h = health;
            if (h != null && h.Date == HealthStore.Today && h.Version >= HealthInfo.CurrentVersion) return;
#if PREVIEW
            if (fakeMode || seqMode || Environment.GetEnvironmentVariable("CF_HIST") == "1") return;
#endif
            measuring = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var m = HealthStore.Measure(health);
                    int c = PowerInfo.CriticalLevel();
                    try { BeginInvoke(new Action(delegate { critical = c; })); } catch { }
                    health = m;
                    HealthStore.Save(m);
                    try { BeginInvoke(new Action(ReadBattery)); } catch { }
                }
                catch (Exception ex) { Log.Write("health", ex); }
                finally { measuring = false; }
            });
        }

        // ---------- animation ----------
        void Tick()
        {
            float t = (float)clock.Elapsed.TotalSeconds;
            float dt = Math.Min(0.25f, t - lastT);
            lastT = t;
            if (cosmos != null) { cosmos.Step(dt); cosmos.Parallax = new PointF((mouse.X / W - 0.5f), (mouse.Y / H - 0.5f)); }
            shown += (pct - shown) * Math.Min(1f, dt * 5f);
            if (Math.Abs(pct - shown) < 0.05f) shown = pct;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            mouse = new PointF(e.X / scale, e.Y / scale);
            bool any = false;
            foreach (var h in hits)
            {
                bool over = h.R.Contains(mouse);
                if (over != h.Hover) { h.Hover = over; if (!anim.Enabled) Invalidate(); }
                any |= over;
            }
            Cursor = any ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                showFocus = false;
                var p = new PointF(e.X / scale, e.Y / scale);
                foreach (var h in hits.ToArray()) if (h.R.Contains(p)) { h.Act(); Invalidate(); return; }
                if (p.Y < 76) { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); }
            }
            base.OnMouseDown(e);
        }

        // Keyboard: Tab and Shift+Tab move between the buttons, Enter or Space presses, Esc goes to the tray.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (hits.Count > 0)
            {
                if (keyData == Keys.Tab || keyData == (Keys.Tab | Keys.Shift))
                {
                    int dir = keyData == Keys.Tab ? 1 : -1;
                    focus = focus < 0 ? (dir > 0 ? 0 : hits.Count - 1) : (focus + dir + hits.Count) % hits.Count;
                    showFocus = true; Invalidate(); return true;
                }
                if ((keyData == Keys.Enter || keyData == Keys.Space) && focus >= 0 && focus < hits.Count)
                {
                    var h = hits[focus]; h.Act(); showFocus = true; Invalidate(); return true;
                }
            }
            if (keyData == Keys.Escape) { if (about) { about = false; BuildHits(); Invalidate(); } else HideToTray(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------- painting ----------
        // Gold for "unplug", ember for "plug in". Blue is light only: it shows as glow, never as a fill.
        Color StateColor()
        {
            switch (st)
            {
                case St.Unplug: return Theme.Alert;
                case St.PlugIn: return Theme.Ember;
                case St.Charging: return Theme.Charge;
                case St.NoBattery: return Theme.Dim;
                default: return Theme.Calm;
            }
        }

        Color GlowColor() { return st == St.PlugIn ? Theme.Halo : StateColor(); }

        // A drawing error must never become an error dialog repeated twenty times a second.
        protected override void OnPaint(PaintEventArgs e)
        {
            try { PaintBody(e.Graphics); }
            catch (Exception ex) { Log.Write("paint", ex); }
        }

        void PaintBody(Graphics g)
        {
            Theme.Quality(g);
            g.ScaleTransform(scale, scale);
            float t = (float)clock.Elapsed.TotalSeconds;
            EnsureCosmos();
            cosmos.Draw(g, t);

            DrawHeader(g, t);
            if (view == View.Now)
            {
                DrawGauge(g, t);
                DrawStatusCard(g, t);
                DrawAutonomyCard(g);
                DrawHealthCard(g);
                DrawThresholdCard(g);
                DrawSwitch(g, new PointF(RX, 495), L.T("Iniciar con Windows", "Start with Windows"), autoOn);
                DrawSwitch(g, new PointF(RX + 216, 495), L.T("Avisos", "Reminders"), cfg.Notify);
                if (!autoOn)
                    using (var b = new SolidBrush(Theme.GoldSoft))
                        g.DrawString(L.T("Apagado: al reiniciar no hay avisos.", "Off: no reminders after a restart."), Theme.Small, b, RX + 50, 518);
                DrawToggleButton(g, hApagon, cfg.ApagonOn,
                    cfg.ApagonOn ? L.T("Apagón hasta las " + Txt.Clock(new DateTime(cfg.ApagonUntilUtc, DateTimeKind.Utc).ToLocalTime()), "Blackout until " + Txt.Clock(new DateTime(cfg.ApagonUntilUtc, DateTimeKind.Utc).ToLocalTime()))
                                 : L.T("¿Cortes hoy? Modo apagón", "Power cuts? Blackout mode"));
                DrawToggleButton(g, hAlways, cfg.AlwaysPlugged, L.T("Vive conectada", "Always plugged in"));
            }
            else DrawMine(g, t);

            using (var b = new SolidBrush(Theme.Dim))
            {
                g.DrawString("Reyes Project  |  v" + Program.Version, Theme.Small, b, 28, H - 32);
                g.DrawString(L.T("Nada sale de esta laptop: la app nunca se conecta a internet.", "Nothing leaves this laptop: the app never goes online."), Theme.Small, b, W - 28, H - 32, FarFmt);
            }
            if (!intro && !about)
                using (var b = new SolidBrush(hAbout.Hover ? Theme.GoldSoft : Theme.Gold))
                    g.DrawString(L.T("Acerca de", "About"), Theme.Small, b, hAbout.R.X + 6, H - 32);
            if (intro) DrawIntro(g, t);
            if (about) DrawAbout(g);
            if (showFocus && focus >= 0 && focus < hits.Count)
            {
                var r = hits[focus].R; r.Inflate(4, 4);
                using (var p = new Pen(Theme.GoldSoft, 1.6f) { DashStyle = DashStyle.Dot })
                using (var gp = Theme.Round(r, 8)) g.DrawPath(p, gp);
            }
            using (var p = new Pen(Color.FromArgb(110, Theme.Gold), 1f))
                g.DrawRectangle(p, 0.5f, 0.5f, W - 1, H - 1);
        }

        void DrawHeader(Graphics g, float t)
        {
            Bolt.Draw(g, new RectangleF(26, 18, 38, 38), Theme.GoldSoft, Theme.GoldDeep, 0.8f + 0.2f * (float)Math.Sin(t * 1.6f));
            var tf = Theme.Typo;
            using (var b = new SolidBrush(Theme.Text)) g.DrawString("Cuida tu", Theme.Title, b, 76, 17, tf);
            float w1 = g.MeasureString("Cuida tu", Theme.Title, 1000, tf).Width + Theme.Title.Size * 0.28f;
            using (var b = new SolidBrush(Theme.Gold)) g.DrawString("Batería", Theme.Title, b, 76 + w1, 17, tf);
            using (var b = new SolidBrush(Theme.Dim))
                g.DrawString(L.T("Conéctala, olvídate y yo te aviso cuando haga falta.", "Plug it in, forget about it, and I will tell you when it matters."), Theme.Sub, b, 77, 47);
            if (!intro && !about)
            {
                DrawTab(g, hTabNow, L.T("Ahora", "Now"), view == View.Now);
                DrawTab(g, hTabMine, L.T("Mi batería", "My battery"), view == View.Mine);
            }
            foreach (var h in new[] { hMin, hClose })
            {
                bool isClose = h == hClose;
                if (h.Hover)
                    using (var b = new SolidBrush(Color.FromArgb(isClose ? 90 : 50, isClose ? Theme.Alert : Theme.Gold)))
                    using (var gp = Theme.Round(h.R, 6)) g.FillPath(b, gp);
                using (var p = new Pen(Theme.Text, 1.6f))
                {
                    float cx = h.R.X + h.R.Width / 2, cy = h.R.Y + h.R.Height / 2;
                    if (!isClose) g.DrawLine(p, cx - 6, cy + 3, cx + 6, cy + 3);
                    else { g.DrawLine(p, cx - 5, cy - 5, cx + 5, cy + 5); g.DrawLine(p, cx + 5, cy - 5, cx - 5, cy + 5); }
                }
            }
        }

        static void DrawTab(Graphics g, Hit h, string text, bool active)
        {
            using (var b = new SolidBrush(active ? Theme.GoldSoft : (h.Hover ? Theme.Text : Theme.Dim)))
                g.DrawString(text, Theme.Btn, b, h.R, CenterFmt);
            if (active)
                using (var p = new Pen(Theme.Gold, 2f)) g.DrawLine(p, h.R.X + 12, h.R.Bottom - 3, h.R.Right - 12, h.R.Bottom - 3);
        }

        void DrawGauge(Graphics g, float t)
        {
            const float cx = 220, cy = 290, r = 122;
            var rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
            Color sc = StateColor(), glow = GlowColor();
            using (var p = new Pen(Color.FromArgb(40, Theme.Gold), 1f)) g.DrawEllipse(p, cx - r - 20, cy - r - 20, (r + 20) * 2, (r + 20) * 2);
            using (var p = new Pen(Color.FromArgb(60, Theme.Gold), 1.2f))
            {
                for (int i = 0; i <= 54; i++)
                {
                    double a = (135 + i * 5) * Math.PI / 180;
                    float len = (i % 9 == 0) ? 9 : 4;
                    float r1 = r + 20, r2 = r + 20 - len;
                    g.DrawLine(p, cx + (float)Math.Cos(a) * r1, cy + (float)Math.Sin(a) * r1, cx + (float)Math.Cos(a) * r2, cy + (float)Math.Sin(a) * r2);
                }
            }
            using (var p = new Pen(Color.FromArgb(46, 120, 105, 80), 16f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(p, rect, 135, 270);
            float sweep = 270f * Math.Max(0.4f, shown) / 100f;
            if (st != St.NoBattery)
            {
                int ga = charging ? (int)(12 + 30 * (0.5 + 0.5 * Math.Sin(t * 3.2))) : (st == St.PlugIn ? 34 : 22);
                for (int i = 3; i >= 1; i--)
                    using (var p = new Pen(Color.FromArgb(ga, glow), 16f + i * 7f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawArc(p, rect, 135, sweep);
                using (var p = new Pen(sc, 16f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(p, rect, 135, sweep);
                using (var p = new Pen(Color.FromArgb(90, 255, 255, 255), 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(p, rect, 135, sweep);
            }
            DrawMarker(g, cx, cy, r, cfg.Lower, Theme.Ember);
            if (!cfg.AlwaysPlugged) DrawMarker(g, cx, cy, r, cfg.ApagonOn ? 100 : cfg.Upper, Theme.Alert);

            string num = st == St.NoBattery ? "n/d" : ((int)Math.Round(shown)).ToString();
            using (var b = new SolidBrush(Theme.Text)) g.DrawString(num, Theme.Big, b, new RectangleF(cx - 100, cy - 66, 200, 100), CenterFmt);
            using (var b = new SolidBrush(Theme.Dim)) g.DrawString(L.T("POR CIENTO", "PERCENT"), Theme.Label, b, new RectangleF(cx - 80, cy + 26, 160, 20), CenterFmt);
            string line = st == St.NoBattery ? (desktop ? L.T("EQUIPO DE ESCRITORIO", "DESKTOP PC") : L.T("SIN BATERÍA", "NO BATTERY"))
                : (plugged ? (charging ? L.T("CONECTADA, CARGANDO", "PLUGGED IN, CHARGING") : L.T("CONECTADA", "PLUGGED IN")) : L.T("CON BATERÍA", "ON BATTERY"));
            using (var b = new SolidBrush(sc)) g.DrawString(line, Theme.Label, b, new RectangleF(cx - 120, cy + 52, 240, 20), CenterFmt);
            using (var b = new SolidBrush(Theme.Dim))
            {
                using (var dot = new SolidBrush(Theme.Ember)) g.FillEllipse(dot, 96, 456, 7, 7);
                g.DrawString(L.T("Conectar ", "Plug in ") + L.P(cfg.Lower), Theme.Small, b, 107, 449);
                using (var dot = new SolidBrush(Theme.Alert)) g.FillEllipse(dot, 222, 456, 7, 7);
                string up = cfg.AlwaysPlugged ? L.T("Vive conectada", "Always plugged in")
                          : (cfg.ApagonOn ? L.T("Apagón: hasta ", "Blackout: up to ") + L.P(100) : L.T("Desconectar ", "Unplug ") + L.P(cfg.Upper));
                g.DrawString(up, Theme.Small, b, 233, 449);
            }
        }

        // The blackout and always-plugged buttons sit under the gauge: easy to find on a day with power cuts.
        static void DrawToggleButton(Graphics g, Hit h, bool on, string text)
        {
            using (var gp = Theme.Round(h.R, 10))
            {
                if (on) for (int i = 3; i >= 1; i--) using (var p = new Pen(Color.FromArgb(26, Theme.Gold), i * 4f)) g.DrawPath(p, gp);
                using (var b = new SolidBrush(Color.FromArgb(on ? 110 : (h.Hover ? 80 : 30), Theme.Gold))) g.FillPath(b, gp);
                using (var p = new Pen(Color.FromArgb(on ? 220 : 140, Theme.Gold), 1f)) g.DrawPath(p, gp);
            }
            using (var b = new SolidBrush(on ? Theme.GoldSoft : Theme.Text)) g.DrawString(text, Theme.Btn, b, h.R, CenterFmt);
        }

        static void DrawMarker(Graphics g, float cx, float cy, float r, int value, Color col)
        {
            double a = (135 + 270.0 * value / 100.0) * Math.PI / 180;
            float x1 = cx + (float)Math.Cos(a) * (r - 14), y1 = cy + (float)Math.Sin(a) * (r - 14);
            float x2 = cx + (float)Math.Cos(a) * (r + 14), y2 = cy + (float)Math.Sin(a) * (r + 14);
            using (var p = new Pen(col, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, x1, y1, x2, y2);
        }

        static void Card(Graphics g, RectangleF r, Color accent, float glow) { Card(g, r, accent, glow, accent); }

        static void Card(Graphics g, RectangleF r, Color accent, float glow, Color glowColor)
        {
            using (var gp = Theme.Round(r, 14))
            {
                using (var b = new SolidBrush(Color.FromArgb(205, Theme.Card))) g.FillPath(b, gp);
                if (glow > 0)
                    for (int i = 3; i >= 1; i--)
                        using (var p = new Pen(Color.FromArgb((int)(18 * glow), glowColor), i * 3f)) g.DrawPath(p, gp);
                using (var p = new Pen(Color.FromArgb(glow > 0 ? 150 : 70, accent), 1f)) g.DrawPath(p, gp);
            }
        }

        static void Label(Graphics g, string s, float x, float y)
        {
            using (var b = new SolidBrush(Theme.Dim)) g.DrawString(s, Theme.Label, b, x, y);
        }

        void DrawStatusCard(Graphics g, float t)
        {
            var r = new RectangleF(RX, 96, RW, 104);
            Color sc = StateColor();
            bool alert = st == St.Unplug || st == St.PlugIn;
            float pulse = alert ? 0.6f + 0.4f * (float)Math.Sin(t * 4f) : 0.5f;
            Card(g, r, sc, pulse, GlowColor());
            string head, body;
            switch (st)
            {
                case St.Unplug:
                    head = L.T("Hora de desconectarla", "Time to unplug");
                    body = L.T("Llegó a " + L.P(pct) + ". Si pasa el día llena, se desgasta más rápido. Desconéctala y que trabaje un rato con batería.",
                               "It reached " + L.P(pct) + ". Kept full all day, it wears out faster. Unplug it and let it run on battery for a while.");
                    break;
                case St.PlugIn:
                    head = L.T("Hora de conectarla", "Time to plug in");
                    body = L.T("Le queda " + L.P(pct) + ". Conéctala antes de que se apague.", L.P(pct) + " left. Plug it in before it shuts down.");
                    break;
                case St.Charging:
                    if (cfg.ApagonOn)
                    {
                        head = L.T("Modo apagón", "Blackout mode");
                        body = L.T("Cárgala hasta 100 %. No te pido que la desconectes hasta las " + ApagonUntilText() + ". Tócalo otra vez para quitarlo.",
                                   "Charge it to 100%. I will not ask you to unplug until " + ApagonUntilText() + ". Press it again to turn it off.");
                        break;
                    }
                    if (cfg.AlwaysPlugged)
                    {
                        head = L.T("Vive conectada", "Always plugged in");
                        body = L.T("No te pido que la desconectes. Si tu marca trae límite de carga, actívalo: en Mi batería te digo dónde.",
                                   "I will not ask you to unplug. If your brand has a charge limit, turn it on: My battery tells you where.");
                        break;
                    }
                    if (holdReads >= 2 && pct >= cfg.Upper - 3)
                    {
                        head = L.T("Todo bien", "All good");
                        body = L.T("Tu laptop dejó de cargar sola en " + L.P(pct) + ": su límite de fábrica está haciendo el trabajo. No hace falta que la desconectes.",
                                   "Your laptop stopped charging by itself at " + L.P(pct) + ": its factory limit is doing the job. No need to unplug it.");
                        break;
                    }
                    head = L.T("Todo bien", "All good");
                    string when = chargeMin > 0 ? L.T(" Llega a " + L.P(cfg.Upper) + " en unos " + Txt.Dur(chargeMin) + ".", " It reaches " + L.P(cfg.Upper) + " in about " + Txt.Dur(chargeMin) + ".")
                                                : L.T(" Te aviso al llegar a " + L.P(cfg.Upper) + ".", " I will tell you at " + L.P(cfg.Upper) + ".");
                    body = (charging ? L.T("Cargando, todavía debajo de " + L.P(cfg.Upper) + ".", "Charging, still below " + L.P(cfg.Upper) + ".") : L.T("Conectada.", "Plugged in.")) + when;
                    break;
                case St.NoBattery:
                    head = desktop ? L.T("Equipo de escritorio", "Desktop PC") : L.T("Sin batería", "No battery");
                    body = desktop ? L.T("La batería que ve Windows es la de una UPS. Cuida tu Batería está hecha para laptops, así que aquí no te molesto.",
                                         "The battery Windows sees belongs to a UPS. Cuida tu Batería is made for laptops, so I will stay quiet here.")
                                   : L.T("Este equipo no reporta batería. Cuida tu Batería está hecha para laptops.", "This computer reports no battery. Cuida tu Batería is made for laptops.");
                    break;
                default:
                    head = L.T("En calma", "Calm");
                    body = L.T("Va con batería al " + L.P(pct) + ". Cuando baje a " + L.P(cfg.Lower) + ", te aviso para que la conectes.",
                               "On battery at " + L.P(pct) + ". When it drops to " + L.P(cfg.Lower) + ", I will tell you to plug it in.");
                    break;
            }
            using (var b = new SolidBrush(sc)) { g.FillEllipse(b, RX + 22, r.Y + 22, 10, 10); g.DrawString(head, Theme.Head, b, RX + 40, r.Y + 11); }
            using (var b = new SolidBrush(Theme.Text)) g.DrawString(body, Theme.Body, b, new RectangleF(RX + 22, r.Y + 42, RW - 40, 60));
        }

        // How long the battery would last with this person's real use. In Venezuela the question is
        // usually "if the power goes out now, how long do I have?", so that is how it is asked.
        void DrawAutonomyCard(Graphics g)
        {
            var r = new RectangleF(RX, 210, RW, 88);
            Card(g, r, Theme.Gold, 0);
            string label, value, sub1 = "", sub2 = "";
            if (st == St.NoBattery) { label = L.T("AUTONOMÍA", "BATTERY LIFE"); value = "n/d"; sub1 = L.T("Sin batería no hay nada que medir.", "Without a battery there is nothing to measure."); }
            else if (plugged)
            {
                label = L.T("SI SE VA LA LUZ AHORA", "IF THE POWER GOES OUT NOW");
                if (auto.Basis == "real")
                {
                    value = Txt.Dur(auto.Minutes);
                    sub1 = L.T("Con tu uso de los últimos días, hasta que Windows la apague.", "Based on the past few days, until Windows shuts it down.");
                    sub2 = L.T("Llena te dura unos ", "A full charge lasts about ") + Txt.Dur(auto.FullMinutes) + ".";
                }
                else if (auto.Basis == "report")
                {
                    value = L.T("Unas ", "About ") + Txt.Dur(auto.Minutes);
                    sub1 = L.T("Cuenta de Windows, con tu uso de otras semanas. Aún no es mía.", "Windows' figure, from other weeks. Not mine yet.");
                    sub2 = L.T("Desconéctala 10 minutos y te digo la de verdad.", "Unplug it for 10 minutes and I will give you the real one.");
                }
                else if (measuring) { value = L.T("Midiendo", "Measuring"); sub1 = L.T("Estoy leyendo el historial de tu batería. Un momento.", "I am reading your battery history. One moment."); }
                else { value = L.T("Aprendiendo", "Learning"); sub1 = L.T("Desconéctala un rato y te digo cuánto te dura.", "Unplug it for a while and I will tell you how long it lasts."); sub2 = L.T("Lo calculo con tu propio uso, no con el de fábrica.", "I base it on how you use it, not on factory figures."); }
            }
            else
            {
                label = L.T("TE QUEDA", "TIME LEFT");
                if (auto.Basis == "real")
                {
                    value = Txt.Dur(auto.Minutes);
                    sub1 = watts > 0
                        ? L.T("Ahora gasta " + Txt.N1(watts) + " W, " + PowerInfo.UseLevel(watts) + ". Hasta que Windows la apague.", "Drawing " + Txt.N1(watts) + " W now, " + PowerInfo.UseLevel(watts) + ". Until Windows shuts it down.")
                        : L.T("Con tu uso real, hasta que Windows la apague.", "At your real pace, until Windows shuts it down.");
                    double toWarn = auto.Minutes * Math.Max(0, pct - cfg.Lower) / Math.Max(1.0, pct - critical);
                    sub2 = pct > cfg.Lower ? L.T("El aviso de conectar llega en unos ", "The plug-in reminder comes in about ") + Txt.Dur(toWarn) + "."
                                           : L.T("Ya pasaste tu aviso de " + L.P(cfg.Lower) + ".", "You are past your " + L.P(cfg.Lower) + " reminder.");
                }
                else if (auto.Basis == "windows" || auto.Basis == "report")
                {
                    value = L.T("Unas ", "About ") + Txt.Dur(auto.Minutes);
                    sub1 = auto.Basis == "windows" ? L.T("Cuenta de Windows para este momento. Aún no es mía.", "Windows' figure for right now. Not mine yet.")
                                                   : L.T("Cuenta de Windows, con tu uso de otras semanas. Aún no es mía.", "Windows' figure, from other weeks. Not mine yet.");
                    sub2 = watts > 0
                        ? L.T("Ahora gasta " + Txt.N1(watts) + " W. En unos minutos te doy la mía.", "Drawing " + Txt.N1(watts) + " W now. Mine comes in a few minutes.")
                        : L.T("En unos minutos con batería te doy la mía.", "Mine comes after a few minutes on battery.");
                }
                else if (measuring) { value = L.T("Midiendo", "Measuring"); sub1 = L.T("Estoy leyendo el historial de tu batería. Un momento.", "I am reading your battery history. One moment."); }
                else { value = L.T("Calculando", "Calculating"); sub1 = L.T("Después de 5 minutos con batería te digo cuánto te queda.", "After 5 minutes on battery I will tell you how much is left."); }
            }
            Label(g, label, RX + 22, r.Y + 12);
            using (var b = new SolidBrush(auto.Basis == "none" ? Theme.Calm : Theme.GoldSoft)) g.DrawString(value, Theme.Head, b, RX + 21, r.Y + 28);
            using (var b = new SolidBrush(Theme.Dim))
            {
                g.DrawString(sub1, Theme.Small, b, RX + 22, r.Y + 53);
                if (sub2.Length > 0) g.DrawString(sub2, Theme.Small, b, RX + 22, r.Y + 69);
            }
        }

        void DrawHealthCard(Graphics g)
        {
            var r = new RectangleF(RX, 308, RW, 80);
            Card(g, r, Theme.Gold, 0);
            var h = health;
            string age = h == null ? "" : Txt.AgeYears(h.BatDate);
            Label(g, L.T("SALUD DE LA BATERÍA", "BATTERY HEALTH") + (age.Length > 0 ? "   |   " + (L.En ? age.ToUpperInvariant() + " OLD" : age.ToUpperInvariant() + " DE FABRICADA") : ""), RX + 22, r.Y + 12);
            float hp = h == null ? -2 : h.Pct;
            string val; Color col; string verdict;
            if (hp >= 0)
            {
                val = L.P(hp);
                col = hp >= 80 ? Theme.GoldSoft : (hp >= 60 ? Theme.Gold : Theme.Ember);
                verdict = Txt.Verdict(hp) + (h.Estimated ? L.T(" (estimado)", " (estimate)") : "");
            }
            else { val = hp == -2 ? "..." : "n/d"; col = Theme.Dim; verdict = hp == -2 ? (noBattery ? "" : L.T("midiendo", "measuring")) : L.T("sin dato de fábrica", "no factory figure"); }
            using (var b = new SolidBrush(col))
            {
                g.DrawString(val, Theme.Head, b, RX + 21, r.Y + 28);
                if (verdict.Length > 0) g.DrawString(verdict, Theme.Sub, b, RX + 30 + Math.Max(50, g.MeasureString(val, Theme.Head).Width), r.Y + 33);
            }
            if (hp >= 0)
            {
                var bar = new RectangleF(RX + RW - 112, r.Y + 36, 90, 8);
                using (var b = new SolidBrush(Color.FromArgb(50, 120, 105, 80))) using (var gp = Theme.Round(bar, 4)) g.FillPath(b, gp);
                var fill = new RectangleF(bar.X, bar.Y, Math.Max(8, bar.Width * hp / 100f), bar.Height);
                using (var b = new SolidBrush(col)) using (var gp = Theme.Round(fill, 4)) g.FillPath(b, gp);
            }
            string detail = h == null ? (noBattery ? "" : L.T("Leyendo los datos de la batería.", "Reading the battery data.")) : h.Detail;
            using (var b = new SolidBrush(Theme.Dim)) g.DrawString(detail, Theme.Small, b, RX + 22, r.Y + 55);
        }

        void DrawThresholdCard(Graphics g)
        {
            var r = new RectangleF(RX, 396, RW, 82);
            Card(g, r, Theme.Gold, 0);
            DrawStepper(g, L.T("Desconectar en", "Unplug at"), 404, cfg.Upper, hUpPlus, hUpMinus);
            DrawStepper(g, L.T("Conectar en", "Plug in at"), 438, cfg.Lower, hLoPlus, hLoMinus);
        }

        void DrawStepper(Graphics g, string label, float y, int value, Hit plus, Hit minus)
        {
            using (var b = new SolidBrush(Theme.Text)) g.DrawString(label, Theme.Body, b, RX + 22, y + 4);
            foreach (var h in new[] { minus, plus })
            {
                using (var gp = Theme.Round(h.R, 8))
                {
                    using (var b = new SolidBrush(Color.FromArgb(h.Hover ? 120 : 60, Theme.Gold))) g.FillPath(b, gp);
                    using (var p = new Pen(Color.FromArgb(150, Theme.Gold), 1f)) g.DrawPath(p, gp);
                }
                using (var p = new Pen(Theme.Text, 1.6f))
                {
                    float cx = h.R.X + h.R.Width / 2, cy = h.R.Y + h.R.Height / 2;
                    g.DrawLine(p, cx - 5, cy, cx + 5, cy);
                    if (h == plus) g.DrawLine(p, cx, cy - 5, cx, cy + 5);
                }
            }
            using (var b = new SolidBrush(Theme.GoldSoft)) g.DrawString(L.P(value), Theme.Btn, b, new RectangleF(minus.R.Right, minus.R.Y, plus.R.X - minus.R.Right, minus.R.Height), CenterFmt);
        }

        void DrawSwitch(Graphics g, PointF p, string label, bool on)
        {
            var track = new RectangleF(p.X, p.Y, 40, 20);
            using (var gp = Theme.Round(track, 10))
            {
                using (var b = new SolidBrush(on ? Theme.GoldDeep : Color.FromArgb(70, 120, 105, 80))) g.FillPath(b, gp);
                using (var pen = new Pen(Color.FromArgb(on ? 220 : 100, Theme.Gold), 1f)) g.DrawPath(pen, gp);
            }
            using (var b = new SolidBrush(on ? Color.White : Theme.Dim)) g.FillEllipse(b, p.X + (on ? 22 : 2), p.Y + 2, 16, 16);
            using (var b = new SolidBrush(Theme.Text)) g.DrawString(label, Theme.Body, b, p.X + 50, p.Y - 1);
        }

        // ---------- "Mi batería" ----------
        void DrawMine(Graphics g, float t)
        {
            var h = health;
            // month by month
            var rc = new RectangleF(28, 96, 484, 258);
            Card(g, rc, Theme.Gold, 0);
            Label(g, L.T("SALUD MES A MES", "HEALTH MONTH BY MONTH"), rc.X + 20, rc.Y + 12);
            if (h != null && h.Pct >= 0)
                using (var b = new SolidBrush(Theme.GoldSoft)) g.DrawString(L.T("guarda " + L.P(h.Pct) + " de lo que traía nueva", L.P(h.Pct) + " of original capacity"), Theme.Label, b, rc.Right - 20, rc.Y + 12, FarFmt);
            HealthChart.Draw(g, new RectangleF(rc.X + 24, rc.Y + 38, rc.Width - 48, rc.Height - 50), h, Theme.Small, false);

            // usage, last seven days
            var ru = new RectangleF(28, 364, 484, 116);
            Card(g, ru, Theme.Gold, 0);
            Label(g, L.T("TU USO, ÚLTIMOS 7 DÍAS", "YOUR USE, LAST 7 DAYS"), ru.X + 20, ru.Y + 12);
            if (week.Days < 0.04 && week.BatteryHours <= 0)
            {
                using (var b = new SolidBrush(Theme.Text))
                    g.DrawString(L.T("Empecé a anotar tu uso hoy. En unos días te muestro cuántas horas usas la batería, cuánto gastas por hora y cuánto tiempo pasa llena y conectada.",
                                     "I started logging your use today. In a few days I will show you how many hours you run on battery, how much you use per hour and how long it sits full on the charger."), Theme.Body, b, new RectangleF(ru.X + 20, ru.Y + 36, ru.Width - 40, 72));
            }
            else
            {
                Stat(g, ru.X + 20, ru.Y + 36, Txt.Dur(week.BatteryHours * 60), L.T("con batería", "on battery"));
                Stat(g, ru.X + 250, ru.Y + 36, week.Unplugs.ToString(), week.Unplugs == 1 ? L.T("vez desconectada", "time unplugged") : L.T("veces desconectada", "times unplugged"));
                Stat(g, ru.X + 20, ru.Y + 72, week.Rate > 0 ? L.P(week.Rate) : "n/d", L.T("de batería por hora", "of battery per hour"));
                Stat(g, ru.X + 250, ru.Y + 72, week.HighHours > 0.02 ? Txt.Dur(week.HighHours * 60) : "0 min", L.T("conectada sobre ", "plugged in above ") + L.P(cfg.Upper));
            }

            // the report in plain words
            var rp = new RectangleF(528, 96, 304, 258);
            Card(g, rp, Theme.Gold, 0);
            Label(g, L.T("EN PALABRAS SIMPLES", "IN PLAIN WORDS"), rp.X + 20, rp.Y + 12);
            float y = rp.Y + 36;
            using (var b = new SolidBrush(Theme.Text))
            using (var bar = new SolidBrush(Color.FromArgb(160, Theme.Gold)))
            {
                foreach (var para in HealthStore.Plain(h, usage.Drained, cfg.Since))
                {
                    var sz = g.MeasureString(para, Theme.Small, (int)(rp.Width - 50));
                    if (y + sz.Height > rp.Bottom - 8) break;
                    g.FillRectangle(bar, rp.X + 20, y + 3, 2, sz.Height - 5);
                    g.DrawString(para, Theme.Small, b, new RectangleF(rp.X + 30, y, rp.Width - 50, sz.Height + 2));
                    y += sz.Height + 7;
                }
            }

            // the brand and its own charge limit
            var rb = new RectangleF(528, 364, 304, 116);
            string maker = h != null ? h.Maker : "", model = h != null ? h.Model : "";
            string guide;
            bool real = Brand.Guide(maker, model, out guide);
            Card(g, rb, real ? Theme.GoldSoft : Theme.Gold, real ? 0.6f : 0);
            Label(g, h == null ? L.T("TU LAPTOP", "YOUR LAPTOP") : L.T("TU LAPTOP: ", "YOUR LAPTOP: ") + Brand.Name(maker, model).ToUpperInvariant(), rb.X + 20, rb.Y + 12);
            using (var b = new SolidBrush(Theme.Text))
                g.DrawString(h == null ? L.T("Leyendo la marca y el modelo.", "Reading the brand and model.") : guide, Theme.Small, b, new RectangleF(rb.X + 20, rb.Y + 32, rb.Width - 36, rb.Height - 38));

            DrawButton(g, hImage, L.T("Guardar como imagen", "Save as picture"));
            DrawButton(g, hReport, L.T("Informe completo de Windows", "Full Windows report"));
            if (note.Length > 0 && t < noteUntil)
                using (var b = new SolidBrush(Theme.GoldSoft)) g.DrawString(note, Theme.Small, b, new RectangleF(486, 498, W - 514, 30));
        }

        static void Stat(Graphics g, float x, float y, string value, string caption)
        {
            using (var b = new SolidBrush(Theme.GoldSoft)) g.DrawString(value, Theme.Head, b, x, y);
            float w = g.MeasureString(value, Theme.Head).Width;
            using (var b = new SolidBrush(Theme.Dim)) g.DrawString(caption, Theme.Small, b, x + w + 2, y + 6);
        }

        static void DrawButton(Graphics g, Hit h, string text)
        {
            using (var gp = Theme.Round(h.R, 10))
            {
                using (var b = new SolidBrush(Color.FromArgb(h.Hover ? 90 : 34, Theme.Gold))) g.FillPath(b, gp);
                using (var p = new Pen(Color.FromArgb(140, Theme.Gold), 1f)) g.DrawPath(p, gp);
            }
            using (var b = new SolidBrush(Theme.Text)) g.DrawString(text, Theme.Btn, b, h.R, CenterFmt);
        }

        static void DrawPrimary(Graphics g, Hit h, string text)
        {
            using (var gp = Theme.Round(h.R, 12))
            {
                if (h.Hover) for (int i = 4; i >= 1; i--) using (var p = new Pen(Color.FromArgb(34, Theme.Gold), i * 4f)) g.DrawPath(p, gp);
                using (var br = new LinearGradientBrush(h.R, h.Hover ? Color.FromArgb(255, 200, 120) : Theme.Gold, Theme.GoldDeep, 90f)) g.FillPath(br, gp);
                using (var p = new Pen(Color.FromArgb(180, Theme.GoldSoft), 1f)) g.DrawPath(p, gp);
            }
            using (var b = new SolidBrush(Theme.Bg)) g.DrawString(text, Theme.Btn, b, h.R, CenterFmt);
        }

        // ---------- first opening ----------
        static Font introTitle;

        void DrawIntro(Graphics g, float t)
        {
            using (var b = new SolidBrush(Color.FromArgb(232, Theme.Bg))) g.FillRectangle(b, 0, 72, W, H - 72);
            var r = new RectangleF(110, 84, W - 220, H - 112);
            Card(g, r, Theme.Gold, 0.5f);
            if (introTitle == null) introTitle = Fonts.Serif(28f);
            using (var b = new SolidBrush(Theme.Text)) g.DrawString(L.T("Antes de empezar", "Before we start"), introTitle, b, r.X + 36, r.Y + 26);
            float y = r.Y + 80;
            y = IntroPara(g, r, y, L.T("Te aviso, no corto la carga.", "I remind you. I cannot stop the charging."),
                L.T("Windows no deja que un programa apague el cargador. Cuando llegue la hora te salgo con un aviso, y tú desconectas o conectas. Si tu laptop trae límite de carga de fábrica, en Mi batería te digo dónde se activa.",
                    "Windows does not let a program switch the charger off. When the time comes I show you a reminder, and you unplug or plug in. If your laptop has a factory charge limit, My battery tells you where to turn it on."));
            y = IntroPara(g, r, y, L.T("¿Por qué 80 % y 20 %?", "Why 80% and 20%?"),
                L.T("Lo que más gasta una batería de litio es pasar horas llena al tope, sobre todo con calor, o bajar casi a cero. Entre esos dos números sufre menos. Es una regla práctica, y los cambias cuando quieras.",
                    "What wears a lithium battery most is sitting full for hours, above all in the heat, or draining almost to zero. Between those two numbers it suffers less. It is a practical rule, and you can change the numbers whenever you like."));
            y = IntroPara(g, r, y, L.T("Los dos botones del aviso.", "The two buttons on the reminder."),
                L.T("«Luego» te lo repite en 15 minutos. La X lo calla hasta el próximo ciclo.", "\"Later\" reminds you again in 15 minutes. The X silences it until the next cycle."));
            if (IntroHasOldNumbers)
            {
                using (var b = new SolidBrush(Theme.GoldSoft))
                    g.DrawString(L.T("Tenías configurado " + L.P(cfg.Upper) + " y " + L.P(cfg.Lower) + ".", "You had " + L.P(cfg.Upper) + " and " + L.P(cfg.Lower) + " set."), Theme.Body, b, r.X + 36, hIntroOk.R.Y - 34);
                DrawButton(g, hIntroKeep, L.T("Dejar " + cfg.Upper + " y " + cfg.Lower, "Keep " + cfg.Upper + " and " + cfg.Lower));
                DrawPrimary(g, hIntroOk, L.T("Usar 80 y 20", "Use 80 and 20"));
            }
            else DrawPrimary(g, hIntroOk, L.T("Entendido", "Got it"));
        }

        // About: who made it, the licence, privacy, and what it is not affiliated with.
        void DrawAbout(Graphics g)
        {
            using (var b = new SolidBrush(Color.FromArgb(236, Theme.Bg))) g.FillRectangle(b, 0, 72, W, H - 72);
            var r = new RectangleF(110, 84, W - 220, H - 112);
            Card(g, r, Theme.Gold, 0.5f);
            if (introTitle == null) introTitle = Fonts.Serif(28f);
            using (var b = new SolidBrush(Theme.Text)) g.DrawString("Cuida tu Batería " + Program.Version, introTitle, b, r.X + 36, r.Y + 24);
            using (var b = new SolidBrush(Theme.Dim)) g.DrawString("Copyright (C) 2026 Alexis Reyes (Reyes Project)", Theme.Small, b, r.X + 38, r.Y + 64);
            float y = r.Y + 92;
            y = IntroPara(g, r, y, L.T("Software libre.", "Free software."),
                L.T("Licencia GPL-3.0 o posterior. Se ofrece gratis y tal cual, sin garantía de ningún tipo. El código fuente está en " + Program.SourceUrl + ".",
                    "Licensed under GPL-3.0 or later. Offered free and as is, with no warranty of any kind. The source code is at " + Program.SourceUrl + "."));
            y = IntroPara(g, r, y, L.T("Tu privacidad.", "Your privacy."),
                L.T("No se conecta a internet ni envía datos a nadie. Tus ajustes, tu uso y la salud de la batería se guardan solo en esta laptop, y al desinstalar decides si se quedan.",
                    "It never goes online and sends nothing to anyone. Your settings, your usage and your battery's health stay on this laptop, and when you uninstall you decide whether they stay."));
            y = IntroPara(g, r, y, L.T("Marcas y cifras.", "Brands and figures."),
                L.T("Las cifras de salud y autonomía son estimaciones. Reyes Project no está afiliado a Lenovo, ASUS, Dell, Samsung, Acer, HP ni Microsoft; sus nombres son marcas de sus dueños y se usan solo para decir dónde está cada ajuste. Letras: Literata e IBM Plex Sans, licencia SIL OFL 1.1.",
                    "Health and battery-life figures are estimates. Reyes Project is not affiliated with Lenovo, ASUS, Dell, Samsung, Acer, HP or Microsoft; their names are their owners' trademarks, used only to say where each setting is. Typefaces: Literata and IBM Plex Sans, SIL OFL 1.1."));
            DrawPrimary(g, hAboutClose, L.T("Cerrar", "Close"));
        }

        float IntroPara(Graphics g, RectangleF r, float y, string lead, string body)
        {
            float x = r.X + 36, w = r.Width - 72;
            using (var bar = new SolidBrush(Theme.Gold))
            using (var tb = new SolidBrush(Theme.GoldSoft))
            using (var bb = new SolidBrush(Theme.Text))
            {
                g.DrawString(lead, Theme.Btn, tb, x + 12, y);
                var sz = g.MeasureString(body, Theme.Body, (int)(w - 12));
                g.DrawString(body, Theme.Body, bb, new RectangleF(x + 12, y + 20, w - 12, sz.Height + 2));
                g.FillRectangle(bar, x, y + 3, 2, 18 + sz.Height);
                return y + 30 + sz.Height;
            }
        }

#if PREVIEW
        // Test data for the screenshots: ten days of use and two years of monthly capacity, shaped
        // like the HP 14-cf the app was first measured on.
        void PreviewData()
        {
            if (Environment.GetEnvironmentVariable("CF_HIST") != "1") return;
            var now = DateTime.UtcNow;
            for (int d = 10; d >= 1; d--)
            {
                var day = now.Date.AddDays(-d).AddHours(12);
                for (int s = 0; s < 3; s++)
                {
                    var t0 = day.AddHours(s * 3.5);
                    int p0 = 80;
                    for (int m = 0; m <= 120; m += 5) usage.Fake(new Sample { T = t0.AddMinutes(m), Pct = p0 - m * 14 / 60, Plugged = false });
                    for (int m = 125; m <= 200; m += 5) usage.Fake(new Sample { T = t0.AddMinutes(m), Pct = Math.Min(80, 52 + (m - 125) / 2), Plugged = true });
                }
            }
            var h = new HealthInfo { Date = HealthStore.Today, Design = 33215, Full = 33215, Spec = 41040, Cycles = 0, Estimated = true, Echo = true,
                BatName = "Primary", BatMaker = "Hewlett-Packard", Chem = "LIon", Maker = "HP", Model = "HP Laptop 14-cf2xxx", BatDate = "2020-09-29", FullRuntimeMin = 224, Version = HealthInfo.CurrentVersion };
            h.Pct = 100f * h.Full / h.Spec;
            int[] cap = { 36010, 35880, 35720, 35600, 35410, 35300, 35120, 35010, 34880, 34700, 34620, 34490, 34400, 34260, 34150, 34020, 33900, 33790, 33650, 33580, 33470, 33390, 33300, 33215 };
            var start = new DateTime(2024, 11, 1);
            for (int i = 0; i < cap.Length; i++) h.Months.Add(new MonthPoint { Month = start.AddMonths(i).ToString("yyyy-MM", CultureInfo.InvariantCulture), Full = cap[i], Ref = 0 });
            health = h;
            usage.Drained = 1260; cfg.Since = "2026-10-20";
            week = usage.Week(now, cfg.Upper);
        }
#endif

        protected override void Dispose(bool disposing)
        {
            if (disposing) { SystemEvents.PowerModeChanged -= powerHandler; SystemEvents.SessionEnding -= endingHandler; cosmos.Dispose(); tray.Dispose(); anim.Dispose(); poll.Dispose(); settle.Dispose(); trayAnim.Dispose(); toast.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
