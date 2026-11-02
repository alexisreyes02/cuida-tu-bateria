// Instalador de Cuida tu Batería. Copyright (C) 2026 Alexis Reyes, Reyes Project.
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using RP;

namespace CuidaTuBateriaSetup
{
    internal static class Program
    {
        [DllImport("kernel32.dll")] static extern bool SetDefaultDllDirectories(uint flags);

        [STAThread]
        static void Main(string[] args)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Log.Write("setup ui", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("setup", e.ExceptionObject as Exception);
            try { SetDefaultDllDirectories(0x800); } catch { }
            L.Auto();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool silent = Array.IndexOf(args, "/silent") >= 0 || Array.IndexOf(args, "/S") >= 0;
            bool uninstall = Array.IndexOf(args, "/uninstall") >= 0;
            bool fromTemp = Array.IndexOf(args, "/temp") >= 0;
            // An exe cannot delete itself while it runs. Instead of a cmd.exe trick (which antivirus
            // engines read as ransomware behaviour), the uninstaller copies itself to the temp folder,
            // starts the copy and leaves; the copy removes the whole install, its original included.
            if (uninstall && !fromTemp && SetupForm.RunFromTemp(silent)) return;
            if (silent) { Environment.Exit(SetupForm.Silent(uninstall)); return; }
            Application.Run(new SetupForm(uninstall));
        }
    }

    internal sealed class Hit
    {
        public RectangleF R;
        public Action Act;
        public bool Hover;
        public Hit(RectangleF r, Action a) { R = r; Act = a; }
    }

    internal enum Page { Welcome, Working, Done, Error, UConfirm, UWorking, UDone }

    internal sealed class SetupForm : Form
    {
        const int W = 760, H = 480;
        const string Version = "6.1";
        static readonly Font F38 = Fonts.Serif(38f);
        static readonly Font F30 = Fonts.Serif(30f);
        const string AppName = "Cuida tu Batería";
        const string ExeName = "CuidaTuBateria.exe";
        const string UninstName = "uninstall.exe";
        const string UninstKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CuidaTuBateria";
        const string LegacyUninstKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ChargeAndForget";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ExitEventName = "CuidaTuBateria.Exit.RP";
        static readonly string[] ProcNames = { "CuidaTuBateria", "ChargeAndForget" };
        static readonly StringFormat Ellipsis = new StringFormat { Trimming = StringTrimming.EllipsisPath, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int w, int l);

        readonly bool uninstall;
        readonly float scale;
        readonly Cosmos cosmos;
        readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer { Interval = 40 };
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly List<Hit> hits = new List<Hit>();
        PointF mouse = new PointF(W / 2f, H / 2f);
        float lastT;

        Page page;
        string dir;
        string oldVersion;          // installed version, if this is an update
        bool running;               // the app is open right now
        bool optAuto = true, optDesktop = true, optLaunch = true, optKeep = true;
        volatile float prog, shown;
        volatile string stepText = "";
        volatile string errorText = "";
        string warnText = "";

        public SetupForm(bool uninstallMode)
        {
            uninstall = uninstallMode;
            using (var gd = Graphics.FromHwnd(IntPtr.Zero)) scale = Math.Max(1f, gd.DpiX / 96f);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size((int)(W * scale), (int)(H * scale));
            BackColor = Theme.Bg;
            Text = uninstall ? L.T("Quitar Cuida tu Batería", "Remove Cuida tu Batería") : L.T("Instalador de Cuida tu Batería", "Cuida tu Batería Setup");
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            dir = uninstall ? (RegisteredDir() ?? ArgDir() ?? Path.GetDirectoryName(Application.ExecutablePath)) : DefaultDir();
            if (!uninstall)
            {
                // An update goes where the earlier version lives, keeps its numbers, its history and its choices.
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(UninstKey))
                        if (k != null)
                        {
                            oldVersion = k.GetValue("DisplayVersion") as string;
                            string loc = k.GetValue("InstallLocation") as string;
                            if (!string.IsNullOrEmpty(loc) && Directory.Exists(loc)) dir = loc;
                        }
                    if (oldVersion == null)
                        using (var k = Registry.CurrentUser.OpenSubKey(LegacyUninstKey))
                            if (k != null) oldVersion = (k.GetValue("DisplayVersion") as string) ?? L.T("anterior", "earlier");
                    if (oldVersion != null) { optAuto = AutorunWasOn(); optDesktop = DesktopLinkExists(); }
                }
                catch { }
                running = IsRunning();
            }

            cosmos = new Cosmos(W, H, scale, W - 120, 72, 20f, 60);
            page = uninstall ? Page.UConfirm : Page.Welcome;
            BuildHits();
#if PREVIEW
            if (Environment.GetEnvironmentVariable("CF_LANG") == "en") L.En = true;
            var pv = Environment.GetEnvironmentVariable("CF_PAGE");
            if (!string.IsNullOrEmpty(pv)) { page = (Page)Enum.Parse(typeof(Page), pv); prog = 0.62f; shown = 0.62f; stepText = L.T("Copiando los archivos", "Copying the files"); BuildHits(); }
            if (Environment.GetEnvironmentVariable("CF_UPDATE") == "1") { oldVersion = "5.2.1"; running = true; }
            if (pv == "Error") errorText = Friendly(new UnauthorizedAccessException("Access to the path is denied."));
#endif
            anim.Tick += delegate { Tick(); };
            anim.Start();
        }

        // The folder passed by the original uninstaller to its temp copy.
        static string ArgDir()
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (a.StartsWith("/dir=", StringComparison.OrdinalIgnoreCase)) return a.Substring(5).Trim('"');
            return null;
        }

        public static bool RunFromTemp(bool silent)
        {
            try
            {
                string self = Path.GetFullPath(Application.ExecutablePath);
                string d = RegisteredDir() ?? Path.GetDirectoryName(self);
                string temp = Path.Combine(Path.GetTempPath(), "CuidaTuBateria-quitar-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
                File.Copy(self, temp, true);
                Process.Start(new ProcessStartInfo(temp, "/uninstall /temp" + (silent ? " /silent" : "") + " \"/dir=" + d + "\"") { UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
                return true;
            }
            catch (Exception ex) { Log.Write("uninstall copy", ex); return false; }
        }

        static void CleanOldTempCopies()
        {
            try
            {
                foreach (var f in Directory.GetFiles(Path.GetTempPath(), "CuidaTuBateria-quitar-*.exe"))
                    if (!string.Equals(Path.GetFullPath(f), Path.GetFullPath(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase)) TryDelete(f);
            }
            catch { }
        }

        static string DataDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CuidaTuBateria"); } }
        static string OldDataDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CuidaTuBateria"); } }

        static string DefaultDir() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\CuidaTuBateria"); }

        static string RegisteredDir()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(UninstKey))
                {
                    var loc = k == null ? null : k.GetValue("InstallLocation") as string;
                    return string.IsNullOrEmpty(loc) ? null : loc;
                }
            }
            catch { return null; }
        }

        // Only this Windows session: another user's copy on the same laptop is not ours to close.
        static List<Process> Mine()
        {
            var r = new List<Process>();
            int sid = Process.GetCurrentProcess().SessionId;
            foreach (var n in ProcNames)
                try { foreach (var p in Process.GetProcessesByName(n)) if (p.SessionId == sid) r.Add(p); } catch { }
            return r;
        }

        static bool IsRunning() { return Mine().Count > 0; }

        static bool AutorunWasOn()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && (k.GetValue("CuidaTuBateria") != null || k.GetValue("ChargeAndForget") != null); }
            catch { return true; }
        }

        static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"); } }
        static bool DesktopLinkExists() { try { return File.Exists(DesktopLink); } catch { return true; } }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; }
        }

        // ---------- hit areas (rebuilt per page) ----------
        Hit hClose, hPrimary, hSecondary, hOpt1, hOpt2, hBrowse;

        void BuildHits()
        {
            hits.Clear();
            hClose = new Hit(new RectangleF(W - 52, 16, 32, 28), delegate { if (page != Page.Working && page != Page.UWorking) Close(); });
            hits.Add(hClose);
            hPrimary = new Hit(new RectangleF(W - 218, H - 78, 190, 48), null);
            hSecondary = new Hit(new RectangleF(W - 338, H - 78, 108, 48), delegate { Close(); });
            switch (page)
            {
                case Page.Welcome:
                    hPrimary.Act = StartInstall; hits.Add(hPrimary); hits.Add(hSecondary);
                    hOpt1 = new Hit(new RectangleF(60, 380, 230, 30), delegate { optAuto = !optAuto; }); hits.Add(hOpt1);
                    hOpt2 = new Hit(new RectangleF(310, 380, 220, 30), delegate { optDesktop = !optDesktop; }); hits.Add(hOpt2);
                    hBrowse = new Hit(new RectangleF(W - 126, 324, 66, 28), Browse); if (oldVersion == null) hits.Add(hBrowse);
                    break;
                case Page.Done:
                    hPrimary.Act = Finish; hits.Add(hPrimary);
                    hOpt1 = new Hit(new RectangleF(60, 380, 260, 30), delegate { optLaunch = !optLaunch; }); hits.Add(hOpt1);
                    break;
                case Page.Error:
                case Page.UDone:
                    hPrimary.Act = Close; hits.Add(hPrimary);
                    break;
                case Page.UConfirm:
                    hPrimary.Act = StartUninstall; hits.Add(hPrimary); hits.Add(hSecondary);
                    hOpt1 = new Hit(new RectangleF(W / 2f - 170, 300, 340, 30), delegate { optKeep = !optKeep; }); hits.Add(hOpt1);
                    break;
            }
        }

        void SetPage(Page p) { page = p; BuildHits(); }

        void Browse()
        {
            using (var d = new FolderBrowserDialog())
            {
                d.Description = L.T("Elige dónde instalar Cuida tu Batería", "Choose where to install Cuida tu Batería");
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string chosen = d.SelectedPath;
                string candidate = string.Equals(Path.GetFileName(chosen.TrimEnd('\\')), "CuidaTuBateria", StringComparison.OrdinalIgnoreCase)
                    ? chosen : Path.Combine(chosen, "CuidaTuBateria");
                // The data folder in AppData\Roaming shares the name; installing there would mix the app with
                // the person's history, and the uninstaller would have to tell them apart.
                string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (Path.GetFullPath(candidate).StartsWith(Path.GetFullPath(roaming), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFullPath(candidate).TrimEnd('\\'), Path.GetFullPath(DataDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                { warnText = L.T("Esa carpeta guarda tus datos. Elige otra, por ejemplo la que viene marcada.", "That folder holds your data. Choose another, such as the one suggested."); return; }
                // Program Files and other system folders need administrator rights, which this installer
                // never asks for. Check before starting instead of failing halfway.
                if (CanWrite(candidate)) { dir = candidate; warnText = ""; }
                else warnText = L.T("Esa carpeta es de Windows y pide permisos de administrador. Elige una carpeta tuya.", "That folder belongs to Windows and needs administrator rights. Choose one of your own.");
            }
        }

        static bool CanWrite(string folder)
        {
            try
            {
                string probe = Directory.Exists(folder) ? folder : Path.GetDirectoryName(folder);
                string f = Path.Combine(probe, ".ctb_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(f, "");
                File.Delete(f);
                return true;
            }
            catch { return false; }
        }

        void Finish()
        {
            if (optLaunch)
            {
                try { Process.Start(new ProcessStartInfo(Path.Combine(dir, ExeName)) { UseShellExecute = true, WorkingDirectory = dir }); } catch { }
            }
            Close();
        }

        // ---------- install ----------
        void StartInstall()
        {
            prog = 0; shown = 0; stepText = L.T("Preparando la carpeta", "Preparing the folder");
            SetPage(Page.Working);
            new Thread(DoInstall) { IsBackground = true }.Start();
        }

        void Step(string text, float p) { stepText = text; prog = p; Thread.Sleep(320); }

        // First the app is asked to close by itself (it saves and removes its tray icon cleanly). Only if
        // it does not answer in a few seconds is it stopped by force. This runs only after the person
        // pressed Instalar, Actualizar or Quitar on a screen that said the app would be closed.
        static void CloseApp()
        {
            try
            {
                EventWaitHandle ev;
                if (EventWaitHandle.TryOpenExisting(ExitEventName, out ev)) using (ev) ev.Set();
            }
            catch { }
            for (int i = 0; i < 30 && IsRunning(); i++) Thread.Sleep(200);
            foreach (var pr in Mine())
            {
                try { pr.Kill(); pr.WaitForExit(4000); } catch { }
            }
        }

        void DoInstall()
        {
            try
            {
                string target = Install(dir, optAuto, optDesktop, Step);
                // It was open before the update: bring it back the way it was, in the tray.
                if (running)
                    try { Process.Start(new ProcessStartInfo(target, "--tray") { UseShellExecute = true, WorkingDirectory = dir }); optLaunch = false; } catch { }
                Step(L.T("Listo", "Done"), 1f);
                BeginInvoke(new Action(delegate { SetPage(Page.Done); }));
            }
            catch (Exception ex)
            {
                errorText = Friendly(ex);
                BeginInvoke(new Action(delegate { SetPage(Page.Error); }));
            }
        }

        static string Install(string dir, bool auto, bool desktop, Action<string, float> step)
        {
            step(L.T("Preparando la carpeta", "Preparing the folder"), 0.08f);
            Directory.CreateDirectory(dir);
            CloseApp();

            step(L.T("Copiando los archivos", "Copying the files"), 0.34f);
            string target = Path.Combine(dir, ExeName);
            Exception last = null;
            for (int i = 0; i < 6; i++)
            {
                try
                {
                    using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.exe"))
                    using (var f = File.Create(target)) s.CopyTo(f);
                    last = null; break;
                }
                catch (Exception ex) { last = ex; Thread.Sleep(500); }
            }
            if (last != null) throw last;
            string unin = Path.Combine(dir, UninstName);
            if (!string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(unin), StringComparison.OrdinalIgnoreCase))
                File.Copy(Application.ExecutablePath, unin, true);
            // The licence travels with the program, as the GPL asks.
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("LICENSE.txt"))
                    if (s != null) using (var f = File.Create(Path.Combine(dir, "LICENSE.txt"))) s.CopyTo(f);
            }
            catch { }
            // Only now, with the new version safely on disk, the old Charge & Forget leftovers go.
            RemoveLegacy();
            CleanOldTempCopies();

            step(L.T("Creando los accesos", "Creating the shortcuts"), 0.62f);
            Link(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"), target, dir);
            if (desktop) Link(DesktopLink, target, dir); else TryDelete(DesktopLink);

            step(L.T("Registrando en Windows", "Registering with Windows"), 0.84f);
            long bytes = 0;
            foreach (var f in new[] { target, unin }) try { bytes += new FileInfo(f).Length; } catch { }
            using (var k = Registry.CurrentUser.CreateSubKey(UninstKey))
            {
                k.SetValue("DisplayName", AppName);
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "Reyes Project");
                k.SetValue("InstallLocation", dir);
                k.SetValue("DisplayIcon", target);
                k.SetValue("UninstallString", "\"" + unin + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + unin + "\" /uninstall /silent");
                k.SetValue("URLInfoAbout", "https://reyesproject.com");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)Math.Max(1, (bytes + 1023) / 1024), RegistryValueKind.DWord);
            }
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                k.DeleteValue("ChargeAndForget", false);
                if (auto) k.SetValue("CuidaTuBateria", "\"" + target + "\" --tray");
                else k.DeleteValue("CuidaTuBateria", false);
            }
            return target;
        }

        // Unattended mode for winget and scripts. A fresh install takes the defaults; an update keeps
        // what the person chose (start with Windows, desktop shortcut). /uninstall /silent removes the
        // app and keeps the person's numbers and history.
        public static int Silent(bool uninstall)
        {
            try
            {
                if (uninstall)
                {
                    string d = RegisteredDir() ?? ArgDir() ?? Path.GetDirectoryName(Application.ExecutablePath);
                    if (!SafeToRemove(d)) return 2;
                    Uninstall(d, true, (a, b) => { });
                }
                else
                {
                    string d = RegisteredDir();
                    bool update = d != null;
                    if (d == null) d = DefaultDir();
                    bool wasRunning = IsRunning();
                    bool auto = update ? AutorunWasOn() : true;
                    bool desktop = update ? DesktopLinkExists() : false;
                    string target = Install(d, auto, desktop, (a, b) => { });
                    if (wasRunning) try { Process.Start(new ProcessStartInfo(target, "--tray") { UseShellExecute = true, WorkingDirectory = d }); } catch { }
                }
                return 0;
            }
            catch (Exception ex) { Log.Write("silent", ex); return 1; }
        }

        // Windows speaks English in its error messages. Say what happened in plain words first.
        static string Friendly(Exception ex)
        {
            if (ex is UnauthorizedAccessException)
                return L.T("No pude terminar: Windows no me deja escribir en esa carpeta. Usa la que viene marcada, que es tuya y no pide permisos.",
                           "I could not finish: Windows will not let me write in that folder. Use the suggested one, which is yours and needs no permissions.");
            if (ex is IOException && ex.Message.IndexOf("space", StringComparison.OrdinalIgnoreCase) >= 0)
                return L.T("No pude terminar: el disco está lleno. Libera un poco de espacio e intenta de nuevo.", "I could not finish: the disk is full. Free some space and try again.");
            if (ex is IOException)
                return L.T("No pude terminar: un archivo está en uso. Cierra Cuida tu Batería desde la bandeja e intenta de nuevo.",
                           "I could not finish: a file is in use. Close Cuida tu Batería from the tray and try again.");
            return L.T("No pude terminar. Windows dijo: ", "I could not finish. Windows said: ") + ex.Message;
        }

        static void Link(string lnk, string target, string workDir)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object sh = Activator.CreateInstance(t);
            object l = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
            Type lt = l.GetType();
            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, l, new object[] { target });
            lt.InvokeMember("Arguments", BindingFlags.SetProperty, null, l, new object[] { "" });
            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, l, new object[] { workDir });
            lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, l, new object[] { target + ",0" });
            lt.InvokeMember("Description", BindingFlags.SetProperty, null, l, new object[] { L.T("Te avisa cuándo conectar y desconectar tu laptop", "Reminds you when to plug in and unplug your laptop") });
            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, l, null);
        }

        // Charge & Forget was the earlier name: clear its files, shortcuts and registry so nothing is left twice.
        static void RemoveLegacy()
        {
            try
            {
                string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\ChargeAndForget");
                TryDelete(Path.Combine(old, "ChargeAndForget.exe"));
                TryDelete(Path.Combine(old, "ChargeAndForget.exe.config"));
                TryDelete(Path.Combine(old, UninstName));
                TryRemoveEmpty(old);
                TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Charge & Forget.lnk"));
                TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Charge & Forget.lnk"));
                try { Registry.CurrentUser.DeleteSubKeyTree(LegacyUninstKey, false); } catch { }
                try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true)) if (k != null) k.DeleteValue("ChargeAndForget", false); } catch { }
            }
            catch { }
        }

        // ---------- uninstall ----------
        // Removal touches only the app's own two files, in the folder Windows has on record for it, and
        // only if that folder is named CuidaTuBateria and is not the data folder. Nothing else is deleted.
        static bool SafeToRemove(string d)
        {
            if (string.IsNullOrEmpty(d)) return false;
            if (!string.Equals(Path.GetFileName(d.TrimEnd('\\')), "CuidaTuBateria", StringComparison.OrdinalIgnoreCase)) return false;
            string full = Path.GetFullPath(d).TrimEnd('\\');
            return !string.Equals(full, Path.GetFullPath(OldDataDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                && !string.Equals(full, Path.GetFullPath(DataDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        void StartUninstall()
        {
            if (!SafeToRemove(dir))
            {
                errorText = L.T("No encuentro la carpeta de la app en el registro de Windows, así que no borro nada. Quítala desde Configuración, Aplicaciones.",
                                "I cannot find the app's folder in the Windows registry, so I will not delete anything. Remove it from Settings, Apps.");
                SetPage(Page.Error); return;
            }
            prog = 0; shown = 0; stepText = L.T("Cerrando la app", "Closing the app");
            SetPage(Page.UWorking);
            new Thread(DoUninstall) { IsBackground = true }.Start();
        }

        void DoUninstall()
        {
            try
            {
                Uninstall(dir, optKeep, Step);
                Step(L.T("Listo", "Done"), 1f);
                BeginInvoke(new Action(delegate { SetPage(Page.UDone); }));
            }
            catch (Exception ex)
            {
                errorText = Friendly(ex);
                BeginInvoke(new Action(delegate { SetPage(Page.Error); }));
            }
        }

        static void Uninstall(string dir, bool keepData, Action<string, float> step)
        {
            step(L.T("Cerrando la app", "Closing the app"), 0.15f);
            CloseApp();
            step(L.T("Quitando los accesos", "Removing the shortcuts"), 0.4f);
            TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"));
            TryDelete(DesktopLink);
            step(L.T("Quitándola de Windows", "Removing it from Windows"), 0.65f);
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstKey, false); } catch { }
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true)) if (k != null) k.DeleteValue("CuidaTuBateria", false); } catch { }
            step(L.T("Borrando los archivos", "Deleting the files"), 0.88f);
            string self = Path.GetFullPath(Application.ExecutablePath);
            foreach (var name in new[] { ExeName, ExeName + ".config", UninstName, "LICENSE.txt" })
            {
                string f = Path.Combine(dir, name);
                if (!string.Equals(Path.GetFullPath(f), self, StringComparison.OrdinalIgnoreCase)) TryDelete(f);
            }
            TryRemoveEmpty(dir);
            try { Directory.Delete(Path.Combine(Path.GetTempPath(), "CuidaTuBateria"), true); } catch { }
            TryDelete(Path.Combine(Path.GetTempPath(), "Informe de batería de Windows.html"));
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            TryRemoveEmpty(Path.Combine(appData, "ChargeAndForget"));
            if (!keepData)
            {
                foreach (var data in new[] { DataDir, OldDataDir })
                {
                    foreach (var name in new[] { "settings.ini", "uso.csv", "salud.txt", "errores.log", "settings.ini.tmp", "uso.csv.tmp", "salud.txt.tmp" })
                        TryDelete(Path.Combine(data, name));
                    TryRemoveEmpty(data);
                }
                TryDelete(Path.Combine(appData, "ChargeAndForget", "settings.ini"));
                TryRemoveEmpty(Path.Combine(appData, "ChargeAndForget"));
            }
        }

        static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
        static void TryRemoveEmpty(string d) { try { if (Directory.Exists(d) && Directory.GetFileSystemEntries(d).Length == 0) Directory.Delete(d); } catch { } }

        // ---------- input ----------
        void Tick()
        {
            float t = (float)clock.Elapsed.TotalSeconds;
            float dt = Math.Min(0.1f, t - lastT); lastT = t;
            cosmos.Step(dt);
            cosmos.Parallax = new PointF(mouse.X / W - 0.5f, mouse.Y / H - 0.5f);
            shown += (prog - shown) * Math.Min(1f, dt * 4f);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            mouse = new PointF(e.X / scale, e.Y / scale);
            bool any = false;
            foreach (var h in hits) { h.Hover = h.R.Contains(mouse); any |= h.Hover; }
            Cursor = any ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                var p = new PointF(e.X / scale, e.Y / scale);
                foreach (var h in hits.ToArray()) if (h.R.Contains(p) && h.Act != null) { h.Act(); return; }
                if (p.Y < 70) { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); }
            }
            base.OnMouseDown(e);
        }

        // Enter presses the main button, Esc closes when nothing is running.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter && hPrimary != null && hPrimary.Act != null && hits.Contains(hPrimary)) { hPrimary.Act(); return true; }
            if (keyData == Keys.Escape && page != Page.Working && page != Page.UWorking) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------- painting ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            try { PaintBody(e.Graphics); } catch (Exception ex) { Log.Write("setup paint", ex); }
        }

        void PaintBody(Graphics g)
        {
            Theme.Quality(g);
            g.ScaleTransform(scale, scale);
            float t = (float)clock.Elapsed.TotalSeconds;
            cosmos.Draw(g, t);

            DrawClose(g);
            switch (page)
            {
                case Page.Welcome: DrawWelcome(g, t); break;
                case Page.Working: DrawRing(g, t, oldVersion != null ? L.T("ACTUALIZANDO", "UPDATING") : L.T("INSTALANDO", "INSTALLING"), Theme.Gold); break;
                case Page.Done: DrawDone(g, t, false); break;
                case Page.Error: DrawError(g, t); break;
                case Page.UConfirm: DrawUConfirm(g, t); break;
                case Page.UWorking: DrawRing(g, t, L.T("QUITANDO", "REMOVING"), Theme.Alert); break;
                case Page.UDone: DrawDone(g, t, true); break;
            }
            using (var b = new SolidBrush(Color.FromArgb(120, Theme.Dim))) g.DrawString("v" + Version, Theme.Small, b, 24, H - 26);
            using (var p = new Pen(Color.FromArgb(110, Theme.Gold), 1f)) g.DrawRectangle(p, 0.5f, 0.5f, W - 1, H - 1);
        }

        void DrawClose(Graphics g)
        {
            var h = hClose;
            if (page == Page.Working || page == Page.UWorking) return;
            if (h.Hover) using (var b = new SolidBrush(Color.FromArgb(90, Theme.Alert))) using (var gp = Theme.Round(h.R, 6)) g.FillPath(b, gp);
            using (var p = new Pen(Theme.Text, 1.6f))
            {
                float cx = h.R.X + h.R.Width / 2, cy = h.R.Y + h.R.Height / 2;
                g.DrawLine(p, cx - 5, cy - 5, cx + 5, cy + 5); g.DrawLine(p, cx + 5, cy - 5, cx - 5, cy + 5);
            }
        }

        static void Title(Graphics g, float x, float y, Font f)
        {
            var tf = Theme.Typo;
            using (var b = new SolidBrush(Theme.Text)) g.DrawString("Cuida tu", f, b, x, y, tf);
            float w = g.MeasureString("Cuida tu", f, 1000, tf).Width + f.Size * 0.28f;
            using (var b = new SolidBrush(Theme.Gold)) g.DrawString("Batería", f, b, x + w, y, tf);
        }

        static void Button(Graphics g, Hit h, string text, bool primary)
        {
            using (var gp = Theme.Round(h.R, 12))
            {
                if (primary)
                {
                    if (h.Hover)
                        for (int i = 4; i >= 1; i--) using (var p = new Pen(Color.FromArgb(34, Theme.Gold), i * 4f)) g.DrawPath(p, gp);
                    using (var br = new LinearGradientBrush(h.R, h.Hover ? Color.FromArgb(255, 200, 120) : Theme.Gold, Theme.GoldDeep, 90f)) g.FillPath(br, gp);
                    using (var p = new Pen(Color.FromArgb(180, Theme.GoldSoft), 1f)) g.DrawPath(p, gp);
                }
                else
                {
                    using (var b = new SolidBrush(Color.FromArgb(h.Hover ? 70 : 28, Theme.Gold))) g.FillPath(b, gp);
                    using (var p = new Pen(Color.FromArgb(120, Theme.Gold), 1f)) g.DrawPath(p, gp);
                }
            }
            using (var b = new SolidBrush(primary ? Theme.Bg : Theme.Text))
                g.DrawString(text, Theme.Btn, b, h.R, Theme.Center);
        }

        void Switch(Graphics g, float x, float y, string label, bool on)
        {
            var track = new RectangleF(x, y, 40, 20);
            using (var gp = Theme.Round(track, 10))
            {
                using (var b = new SolidBrush(on ? Theme.GoldDeep : Color.FromArgb(70, 120, 105, 80))) g.FillPath(b, gp);
                using (var pen = new Pen(Color.FromArgb(on ? 220 : 100, Theme.Gold), 1f)) g.DrawPath(pen, gp);
            }
            using (var b = new SolidBrush(on ? Color.White : Theme.Dim)) g.FillEllipse(b, x + (on ? 22 : 2), y + 2, 16, 16);
            using (var b = new SolidBrush(Theme.Text)) g.DrawString(label, Theme.Body, b, x + 50, y - 1);
        }

        void DrawWelcome(Graphics g, float t)
        {
            float pulse = 0.8f + 0.2f * (float)Math.Sin(t * 1.5f);
            Bolt.Draw(g, new RectangleF(56, 74, 120, 120), Theme.GoldSoft, Theme.GoldDeep, pulse);
            Title(g, 204, 72, F38);
            using (var b = new SolidBrush(Theme.Dim)) g.DrawString(L.T("Te avisa cuándo conectar y desconectar tu laptop.", "Reminds you when to plug in and unplug your laptop."), Theme.Sub, b, 208, 128);
            // The house detail (Guide, rule one): Alexis's own canaimita story, in his voice, signed.
            using (var bar = new SolidBrush(Theme.Gold)) g.FillRectangle(bar, 210, 166, 2, 58);
            using (var b = new SolidBrush(Theme.Text))
            {
                g.DrawString(L.T("Una vez reviví una canaimita que estaba súper muerta.", "I once brought a Canaima school laptop back from the dead."), Theme.Body, b, 222, 162);
                g.DrawString(L.T("Esta app te avisa a tiempo para que tu laptop no llegue a ese punto.", "This app warns you in time so your laptop never gets to that point."), Theme.Body, b, 222, 184);
            }
            using (var b = new SolidBrush(Theme.Dim)) g.DrawString("Alexis Reyes, Reyes Project", Theme.Small, b, 222, 210);

            if (oldVersion != null)
            {
                using (var b = new SolidBrush(Theme.GoldSoft))
                    g.DrawString(L.T("Tienes la " + oldVersion + ". La paso a la " + Version + " sin tocar tus números ni tu historial.",
                                     "You have version " + oldVersion + ". I will take it to " + Version + " and keep your numbers and history."), Theme.Body, b, new RectangleF(60, 252, W - 120, 22));
                if (running)
                    using (var b = new SolidBrush(Theme.Text))
                        g.DrawString(L.T("Cuida tu Batería está abierta. La cierro un momento para actualizarla y la vuelvo a abrir al final.",
                                         "Cuida tu Batería is open. I will close it for a moment to update it and open it again at the end."), Theme.Small, b, new RectangleF(60, 276, W - 120, 20));
            }
            else if (running)
                using (var b = new SolidBrush(Theme.Text))
                    g.DrawString(L.T("Cuida tu Batería está abierta. La cierro un momento para instalar y luego la abres de nuevo.",
                                     "Cuida tu Batería is open. I will close it for a moment to install, and you can open it again afterward."), Theme.Small, b, new RectangleF(60, 268, W - 120, 20));

            using (var b = new SolidBrush(Theme.Dim)) g.DrawString(L.T("UBICACIÓN", "LOCATION"), Theme.Label, b, 60, 304);
            var row = new RectangleF(60, 324, oldVersion == null ? W - 200 : W - 120, 28);
            using (var gp = Theme.Round(row, 8))
            {
                using (var b = new SolidBrush(Color.FromArgb(150, Theme.Card))) g.FillPath(b, gp);
                using (var p = new Pen(Color.FromArgb(80, Theme.Gold), 1f)) g.DrawPath(p, gp);
            }
            using (var b = new SolidBrush(Theme.Text)) g.DrawString(dir, Theme.Small, b, new RectangleF(70, 324, row.Width - 20, 28), Ellipsis);
            if (oldVersion == null) Button(g, hBrowse, L.T("Cambiar", "Change"), false);
            if (warnText.Length > 0) using (var b = new SolidBrush(Theme.Gold)) g.DrawString(warnText, Theme.Small, b, 60, 356);

            Switch(g, 60, 385, L.T("Iniciar con Windows", "Start with Windows"), optAuto);
            Switch(g, 310, 385, L.T("Acceso en el escritorio", "Desktop shortcut"), optDesktop);
            using (var b = new SolidBrush(Theme.Dim)) g.DrawString(L.T("Solo para tu usuario. No pide permisos de administrador.", "For your user only. No administrator rights needed."), Theme.Small, b, 60, H - 56);
            Button(g, hSecondary, L.T("Cancelar", "Cancel"), false);
            Button(g, hPrimary, oldVersion != null ? L.T("Actualizar", "Update") : L.T("Instalar", "Install"), true);
        }

        void DrawRing(Graphics g, float t, string caption, Color col)
        {
            const float cx = W / 2f, cy = 210, r = 92;
            var rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
            using (var p = new Pen(Color.FromArgb(40, Theme.Gold), 1f)) g.DrawEllipse(p, cx - r - 18, cy - r - 18, (r + 18) * 2, (r + 18) * 2);
            using (var p = new Pen(Color.FromArgb(46, 120, 105, 80), 12f)) g.DrawEllipse(p, rect);
            float sweep = 360f * Math.Max(0.01f, shown);
            for (int i = 3; i >= 1; i--) using (var p = new Pen(Color.FromArgb(22, col), 12f + i * 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(p, rect, -90, sweep);
            using (var p = new Pen(col, 12f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(p, rect, -90, sweep);
            Bolt.Draw(g, new RectangleF(cx - 34, cy - 40, 68, 68), Theme.GoldSoft, Theme.GoldDeep, 0.6f + 0.4f * (float)Math.Sin(t * 3f));
            using (var b = new SolidBrush(Theme.Text)) g.DrawString(((int)Math.Round(shown * 100)) + " %", Theme.Head, b, new RectangleF(cx - 60, cy + 38, 120, 28), Theme.CenterTop);
            using (var b = new SolidBrush(col)) g.DrawString(caption, Theme.Label, b, new RectangleF(0, 336, W, 20), Theme.CenterTop);
            using (var b = new SolidBrush(Theme.Dim)) g.DrawString(stepText, Theme.Body, b, new RectangleF(0, 358, W, 24), Theme.CenterTop);
        }

        void DrawDone(Graphics g, float t, bool removing)
        {
            const float cx = W / 2f, cy = 166, r = 70;
            Color col = removing ? Theme.Gold : Theme.Charge;
            for (int i = 4; i >= 1; i--) using (var p = new Pen(Color.FromArgb(22, col), 8f + i * 6f)) g.DrawEllipse(p, cx - r, cy - r, r * 2, r * 2);
            using (var p = new Pen(col, 8f)) g.DrawEllipse(p, cx - r, cy - r, r * 2, r * 2);
            using (var p = new Pen(col, 9f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(p, new[] { new PointF(cx - 28, cy + 2), new PointF(cx - 8, cy + 24), new PointF(cx + 32, cy - 22) });
            string head = removing ? L.T("Quitada", "Removed") : (oldVersion != null ? L.T("Actualizada", "Updated") : L.T("Instalada", "Installed"));
            using (var b = new SolidBrush(col)) g.DrawString(head, F30, b, new RectangleF(0, 252, W, 44), Theme.CenterTop);
            string line = removing ? L.T("Cuida tu Batería ya no está en este equipo. Gracias por haberla usado.", "Cuida tu Batería is no longer on this computer. Thank you for using it.")
                : (running ? L.T("Ya está de vuelta en la bandeja, al lado del reloj, con tus números de siempre.", "It is back in the tray, next to the clock, with your usual numbers.")
                           : L.T("Cuida tu Batería queda pendiente desde la bandeja, al lado del reloj.", "Cuida tu Batería now keeps watch from the tray, next to the clock."));
            using (var b = new SolidBrush(Theme.Text)) g.DrawString(line, Theme.Body, b, new RectangleF(0, 302, W, 24), Theme.CenterTop);
            if (removing)
            {
                if (optKeep) using (var b = new SolidBrush(Theme.Dim)) g.DrawString(L.T("Tus números y tu historial quedaron guardados por si vuelves.", "Your numbers and history are kept in case you come back."), Theme.Small, b, new RectangleF(0, 330, W, 20), Theme.CenterTop);
                Button(g, hPrimary, L.T("Cerrar", "Close"), true);
            }
            else
            {
                if (!running) Switch(g, 60, 385, L.T("Abrir Cuida tu Batería ahora", "Open Cuida tu Batería now"), optLaunch);
                Button(g, hPrimary, L.T("Finalizar", "Finish"), true);
            }
        }

        void DrawError(Graphics g, float t)
        {
            using (var b = new SolidBrush(Theme.Alert)) g.DrawString(L.T("Algo salió mal", "Something went wrong"), F30, b, new RectangleF(0, 146, W, 44), Theme.CenterTop);
            using (var b = new SolidBrush(Theme.Text)) g.DrawString(errorText, Theme.Body, b, new RectangleF(90, 208, W - 180, 96), Theme.CenterTop);
            Button(g, hPrimary, L.T("Cerrar", "Close"), true);
        }

        void DrawUConfirm(Graphics g, float t)
        {
            Bolt.Draw(g, new RectangleF(W / 2f - 40, 88, 80, 80), Theme.GoldSoft, Theme.GoldDeep, 0.7f + 0.3f * (float)Math.Sin(t * 1.5f));
            using (var b = new SolidBrush(Theme.Text)) g.DrawString(L.T("¿Quitar Cuida tu Batería?", "Remove Cuida tu Batería?"), F30, b, new RectangleF(0, 192, W, 44), Theme.CenterTop);
            using (var b = new SolidBrush(Theme.Dim)) g.DrawString(L.T("Se quitan la app y sus accesos de este equipo.", "The app and its shortcuts are removed from this computer."), Theme.Body, b, new RectangleF(0, 244, W, 24), Theme.CenterTop);
            Switch(g, hOpt1.R.X, hOpt1.R.Y + 5, L.T("Conservar tus números y tu historial", "Keep your numbers and your history"), optKeep);
            Button(g, hSecondary, L.T("No quitar", "Cancel"), false);
            Button(g, hPrimary, L.T("Quitar", "Remove"), true);
        }

        // Alt+F4 must not cut an install or removal in half.
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (page == Page.Working || page == Page.UWorking) { e.Cancel = true; return; }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { cosmos.Dispose(); anim.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
