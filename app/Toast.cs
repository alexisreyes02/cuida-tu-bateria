// Cuida tu Batería. Copyright (C) 2026 Alexis Reyes, Reyes Project.
// GPL-3.0-or-later. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using RP;

namespace CuidaTuBateria
{
    internal enum ToastKind { Unplug, PlugIn }

    // Neon lightning reminder. Slides in above the taskbar, strikes like lightning,
    // flickers on like a neon tube, crackles with arcs and sparks. Never steals focus.
    internal sealed class Toast : Form
    {
        const int W = 468, H = 196;

        static readonly Color NeonGold = Color.FromArgb(255, 176, 60);
        static readonly Color NeonHalo = Color.FromArgb(120, 190, 255);
        static readonly Color NeonWin = Color.FromArgb(255, 236, 190);

        struct Spark { public float X, Y, VX, VY, Life, Max; }

        readonly float scale;
        Cosmos cosmos;   // created when shown, freed when hidden: nothing to carry while in the tray
        int slide;       // pixels the toast travels in from, signed by the side of the taskbar
        readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer { Interval = 30 };
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Random rnd = new Random();
        readonly List<Spark> sparks = new List<Spark>();
        readonly RectangleF boltRect = new RectangleF(30, 26, 116, 146);
        readonly FontFamily titleFamily = Fonts.Family("Literata", "Georgia");

        ToastKind kind;
        int pct;
        string left;   // estimated time left, shown on the plug-in reminder
        bool resolved, closing;
        float shownAt, resolvedAt, closeAt, lastT;
        Point target;
        readonly RectangleF rLater = new RectangleF(W - 108, H - 52, 88, 30);
        readonly RectangleF rClose = new RectangleF(W - 42, 4, 36, 32);
        bool hLater, hClose, settled, calm;
        static readonly StringFormat Centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        static readonly StringFormat Near = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near };

        public event Action Snoozed;
        public event Action Dismissed;

        public Toast()
        {
            using (var gd = Graphics.FromHwnd(IntPtr.Zero)) scale = Math.Max(1f, gd.DpiX / 96f);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;
            ClientSize = new Size((int)(W * scale), (int)(H * scale));
            anim.Tick += delegate { Tick(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x08000000 | 0x8; // toolwindow, noactivate, topmost
                cp.ClassStyle |= 0x20000;              // drop shadow
                return cp;
            }
        }

        float Now { get { return (float)clock.Elapsed.TotalSeconds; } }
        public bool Active { get { return Visible && !closing && !resolved; } }

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }
        [DllImport("shell32.dll")] static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);

        // Next to the clock, wherever the taskbar is: bottom, top, left or right, shown or auto-hidden,
        // on the monitor that holds it.
        void PlaceNearTaskbar()
        {
            var screen = Screen.PrimaryScreen;
            uint edge = 3; bool autoHide = false; int bar = (int)(48 * scale);
            try
            {
                var d = new APPBARDATA { cbSize = Marshal.SizeOf(typeof(APPBARDATA)) };
                if (SHAppBarMessage(5, ref d) != IntPtr.Zero)   // ABM_GETTASKBARPOS
                {
                    edge = d.uEdge;
                    var r = Rectangle.FromLTRB(d.rc.Left, d.rc.Top, d.rc.Right, d.rc.Bottom);
                    screen = Screen.FromRectangle(r);
                    bar = edge == 0 || edge == 2 ? r.Width : r.Height;
                }
                var st = new APPBARDATA { cbSize = Marshal.SizeOf(typeof(APPBARDATA)) };
                autoHide = ((long)SHAppBarMessage(4, ref st) & 1) != 0;   // ABM_GETSTATE, ABS_AUTOHIDE
            }
            catch { }
            var wa = screen.WorkingArea;
            int m = (int)(16 * scale), extra = autoHide ? bar : 0;
            int x = wa.Right - Width - m, y = wa.Bottom - Height - m;
            if (edge == 3) y -= extra;                       // bottom
            else if (edge == 1) y = wa.Top + m + extra;       // top
            else if (edge == 0) x = wa.Left + m + extra;      // left
            else x -= extra;                                  // right
            target = new Point(x, y);
            slide = (edge == 0 ? -1 : 1) * (int)(48 * scale);
        }

        public void Present(ToastKind k, int percent, string timeLeft)
        {
            kind = k; pct = percent; left = timeLeft; resolved = false; closing = false; settled = false; calm = false;
            anim.Interval = 30;
            shownAt = Now; lastT = shownAt;
            sparks.Clear();
            if (cosmos == null) cosmos = new Cosmos(W, H, scale, W - 74, 24, 6f, 40);
            PlaceNearTaskbar();
            Location = new Point(target.X + slide, target.Y);
            Opacity = 0;
            if (!Visible) Show();
            anim.Start();
        }

        public void UpdatePercent(int p, string timeLeft) { if (p != pct || timeLeft != left) { pct = p; left = timeLeft; Invalidate(); } }

        public void Resolve()
        {
            if (!Visible || resolved || closing) return;
            resolved = true; resolvedAt = Now; calm = false; anim.Interval = 30; anim.Start();
            for (int i = 0; i < 40; i++) Emit(boltRect.X + boltRect.Width / 2, boltRect.Y + boltRect.Height / 2, 160f);
        }

        // Leaves quietly, without the green celebration (threshold moved, percent drifted back).
        public void Withdraw() { if (Visible && !closing) BeginClose(); }

        void BeginClose() { if (closing) return; closing = true; closeAt = Now; settled = false; calm = false; anim.Interval = 30; anim.Start(); }

        static float Clamp01(float v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }
        static float EaseOutBack(float x) { const float c1 = 1.70158f, c3 = c1 + 1; return 1 + c3 * (float)Math.Pow(x - 1, 3) + c1 * (float)Math.Pow(x - 1, 2); }
        static float EaseInCubic(float x) { return x * x * x; }

        Color Neon() { return resolved ? NeonWin : (kind == ToastKind.Unplug ? NeonGold : NeonHalo); }

        // Neon tube ignition: a few hard flickers, then a steady hum with a rare stutter.
        float Intensity(float t)
        {
            float a = t - (resolved ? resolvedAt : shownAt);
            if (a < 0.9f)
            {
                int step = (int)(a / 0.06f);
                int[] pattern = { 0, 1, 1, 0, 0, 1, 0, 1, 1, 1, 0, 1, 1, 1, 1 };
                return step < pattern.Length ? (pattern[step] == 1 ? 1f : 0.15f) : 1f;
            }
            float hum = 0.9f + 0.1f * (float)Math.Sin(t * 11f);
            int slot = (int)(t * 8f);
            if (!calm && (slot * 7919) % 53 == 0) hum *= 0.35f;
            if (calm) hum = 1f;
            return hum;
        }

        void Emit(float x, float y, float speed)
        {
            double a = rnd.NextDouble() * Math.PI * 2;
            float v = (float)(speed * (0.3 + rnd.NextDouble()));
            float life = 0.5f + (float)rnd.NextDouble() * 0.6f;
            sparks.Add(new Spark { X = x, Y = y, VX = (float)Math.Cos(a) * v, VY = (float)Math.Sin(a) * v - 40, Life = life, Max = life });
        }

        PointF BoltPoint(int i)
        {
            var p = Bolt.P[i % Bolt.P.Length];
            return new PointF(boltRect.X + p.X * boltRect.Width, boltRect.Y + p.Y * boltRect.Height);
        }

        void Tick()
        {
            float t = Now;
            float dt = Math.Min(0.08f, t - lastT); lastT = t;
            if (cosmos != null) cosmos.Step(dt);

            // sparks from the bolt tip and random corners
            if (!closing && !calm && Intensity(t) > 0.6f && rnd.NextDouble() < 0.55)
            {
                var tip = BoltPoint(3);
                Emit(tip.X, tip.Y, 90f);
                if (rnd.NextDouble() < 0.4) { var q = BoltPoint(rnd.Next(Bolt.P.Length)); Emit(q.X, q.Y, 60f); }
            }
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                var s = sparks[i];
                s.Life -= dt; s.X += s.VX * dt; s.Y += s.VY * dt; s.VY += 260f * dt; s.VX *= 0.985f;
                if (s.Life <= 0 || s.Y > H + 10) sparks.RemoveAt(i); else sparks[i] = s;
            }

            if (!closing)
            {
                if (!settled)
                {
                    float p = Clamp01((t - shownAt) / 0.5f);
                    Location = new Point(target.X + (int)((1 - EaseOutBack(p)) * slide), target.Y);
                    Opacity = Math.Min(1.0, p * 2.4);
                    if (p >= 1) settled = true;
                }
                if (resolved && t - resolvedAt > 2.6f) BeginClose();
                // After 20 s the show is over: a steady neon frame redrawn twice a second, so an
                // unattended laptop on battery is not drained by the reminder itself.
                // After 20 s the show is over: one last steady frame, then the timer stops until the person
                // acts or the state changes, so an unattended laptop on battery is not drained by the reminder.
                if (!resolved && !calm && t - shownAt > 20f && sparks.Count == 0) { calm = true; anim.Stop(); Invalidate(); return; }
            }
            else
            {
                float p = Clamp01((t - closeAt) / 0.38f);
                Location = new Point(target.X + (int)(EaseInCubic(p) * slide), target.Y);
                Opacity = 1 - p;
                if (p >= 1) { anim.Stop(); Hide(); sparks.Clear(); if (cosmos != null) { cosmos.Dispose(); cosmos = null; } return; }
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var p = new PointF(e.X / scale, e.Y / scale);
            bool l2 = rLater.Contains(p), c2 = rClose.Contains(p);
            if (l2 != hLater || c2 != hClose) { hLater = l2; hClose = c2; if (!anim.Enabled) Invalidate(); }
            Cursor = (hLater || hClose) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hLater = hClose = false; if (!anim.Enabled) Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var p = new PointF(e.X / scale, e.Y / scale);
            if (rLater.Contains(p) && !resolved) { BeginClose(); if (Snoozed != null) Snoozed(); }
            else if (rClose.Contains(p)) { BeginClose(); if (!resolved && Dismissed != null) Dismissed(); }
            base.OnMouseDown(e);
        }

        // ---------- painting ----------
        // A drawing error must never turn into an error dialog repeated thirty times a second.
        protected override void OnPaint(PaintEventArgs e)
        {
            try { PaintBody(e); } catch (Exception ex) { Log.Write("toast paint", ex); }
        }

        void PaintBody(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Quality(g);
            g.ScaleTransform(scale, scale);
            float t = Now;
            float k = Intensity(t);
            Color neon = Neon();
            if (cosmos != null) cosmos.Draw(g, t); else g.Clear(Theme.Bg);

            // halo behind the bolt
            using (var gp = new GraphicsPath())
            {
                float cx = boltRect.X + boltRect.Width / 2, cy = boltRect.Y + boltRect.Height / 2, r = 110;
                gp.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                using (var pb = new PathGradientBrush(gp))
                {
                    pb.CenterColor = Color.FromArgb((int)(70 * k), neon);
                    pb.SurroundColors = new[] { Color.FromArgb(0, neon) };
                    g.FillPath(pb, gp);
                }
            }

            DrawNeonBolt(g, t, neon, k);
            DrawSparks(g, neon);

            // frame
            float pulse = resolved ? 1f : 0.55f + 0.45f * (float)Math.Sin(t * 6f);
            for (int i = 3; i >= 1; i--)
                using (var p = new Pen(Color.FromArgb((int)(28 * pulse * k), neon), i * 3f)) g.DrawRectangle(p, 1, 1, W - 2, H - 2);
            using (var p = new Pen(Color.FromArgb((int)(120 + 110 * pulse * k), neon), 1.3f)) g.DrawRectangle(p, 0.6f, 0.6f, W - 1.2f, H - 1.2f);

            // text block
            const float tx = 172;
            Theme.Spaced(g, L.T("CUIDA TU BATERÍA", "CUIDA TU BATERÍA"), Theme.Label, Theme.Dim, tx + 2, 16, 1.8f);
            string title, msg;
            if (resolved)
            {
                title = kind == ToastKind.Unplug ? L.T("Eso es, mi pana.", "There you go, friend.") : L.T("Ahí va cargando.", "Charging now.");
                msg = kind == ToastKind.Unplug ? L.T("Batería contenta. Ya puedes volver a olvidarte de ella.", "Happy battery. You can forget about it again.")
                                               : L.T("Llegaste a tiempo. Ya puedes volver a olvidarte de ella.", "Just in time. You can forget about it again.");
            }
            else if (kind == ToastKind.Unplug)
            {
                title = L.T("¡Oye, desconecta esa vaina!", "Hey, unplug that thing!");
                msg = L.T("Ya llegó a " + L.P(pct) + ". Desconéctala y deja que la batería respire.", "It reached " + L.P(pct) + ". Pull the plug and let the battery breathe.");
            }
            else
            {
                title = L.T("¡Oye, conecta el cargador!", "Hey, plug in the charger!");
                msg = L.T("Le queda " + L.P(pct) + (string.IsNullOrEmpty(left) ? ". Conéctala antes de que se apague." : ", unos " + left + " con tu uso. Conéctala."),
                          L.P(pct) + " left" + (string.IsNullOrEmpty(left) ? ". Plug it in before it shuts down." : ", about " + left + " at your pace. Plug it in."));
            }
            NeonText(g, title, new RectangleF(tx, 30, W - tx - 22, 60), neon, k);
            using (var b = new SolidBrush(Theme.Text)) g.DrawString(msg, Theme.Body, b, new RectangleF(tx + 2, 98, W - tx - 24, 46));

            // battery pill
            DrawBattery(g, new RectangleF(tx + 2, H - 44, 34, 16), neon);

            if (!resolved)
            {
                using (var gp = Theme.Round(rLater, 9))
                {
                    using (var b = new SolidBrush(Color.FromArgb(hLater ? 120 : 45, Theme.Gold))) g.FillPath(b, gp);
                    using (var p = new Pen(Color.FromArgb(160, Theme.Gold), 1f)) g.DrawPath(p, gp);
                }
                using (var b = new SolidBrush(Theme.Text)) g.DrawString(L.T("Luego", "Later"), Theme.Btn, b, rLater, Centered);
                // The X used to discard the cycle without saying so. Now the line under the buttons says it.
                using (var b = new SolidBrush(hClose ? Theme.GoldSoft : Theme.Dim))
                    g.DrawString(hClose ? L.T("La X lo calla hasta el próximo ciclo.", "The X silences it until the next cycle.") : L.T("«Luego»: te aviso en 15 min.", "Later: I will remind you in 15 min."), Theme.Small, b, new RectangleF(tx + 2, H - 22, W - tx - 24, 18));
            }
            if (hClose) using (var b = new SolidBrush(Color.FromArgb(80, Theme.Alert))) using (var gp = Theme.Round(rClose, 5)) g.FillPath(b, gp);
            using (var p = new Pen(Theme.Dim, 1.4f))
            {
                float cx = rClose.X + rClose.Width / 2, cy = rClose.Y + rClose.Height / 2;
                g.DrawLine(p, cx - 4, cy - 4, cx + 4, cy + 4); g.DrawLine(p, cx + 4, cy - 4, cx - 4, cy + 4);
            }

            // lightning strike flash on entrance and on success
            float since = t - (resolved ? resolvedAt : shownAt);
            if (since < 0.22f)
                using (var b = new SolidBrush(Color.FromArgb((int)(170 * (1 - since / 0.22f)), 255, 255, 255))) g.FillRectangle(b, 0, 0, W, H);
        }

        void DrawNeonBolt(Graphics g, float t, Color neon, float k)
        {
            using (var gp = Bolt.Path(boltRect))
            {
                // wide soft glow
                for (int i = 9; i >= 1; i--)
                    using (var p = new Pen(Color.FromArgb((int)(16 * k), neon), i * 4.5f) { LineJoin = LineJoin.Round })
                        g.DrawPath(p, gp);
                // translucent body
                using (var b = new SolidBrush(Color.FromArgb((int)(55 * k), neon))) g.FillPath(b, gp);
                // the tube
                using (var p = new Pen(Color.FromArgb((int)(255 * Math.Max(0.25f, k)), neon), 5f) { LineJoin = LineJoin.Round })
                    g.DrawPath(p, gp);
                // white hot core
                using (var p = new Pen(Color.FromArgb((int)(235 * k), 255, 255, 255), 1.8f) { LineJoin = LineJoin.Round })
                    g.DrawPath(p, gp);
            }
            if (k < 0.6f || calm) return;
            // crawling electric arcs between random points of the bolt
            int arcs = rnd.Next(1, 4);
            for (int a = 0; a < arcs; a++)
            {
                if (rnd.NextDouble() < 0.35) continue;
                var p0 = BoltPoint(rnd.Next(Bolt.P.Length));
                var p1 = new PointF(p0.X + rnd.Next(-46, 47), p0.Y + rnd.Next(-46, 47));
                var pts = new PointF[7];
                for (int i = 0; i < pts.Length; i++)
                {
                    float f = i / (float)(pts.Length - 1);
                    pts[i] = new PointF(p0.X + (p1.X - p0.X) * f + (i == 0 || i == pts.Length - 1 ? 0 : rnd.Next(-7, 8)),
                                        p0.Y + (p1.Y - p0.Y) * f + (i == 0 || i == pts.Length - 1 ? 0 : rnd.Next(-7, 8)));
                }
                using (var p = new Pen(Color.FromArgb(70, neon), 5f) { LineJoin = LineJoin.Round }) g.DrawLines(p, pts);
                using (var p = new Pen(Color.FromArgb(230, 255, 255, 255), 1.2f) { LineJoin = LineJoin.Round }) g.DrawLines(p, pts);
            }
        }

        void DrawSparks(Graphics g, Color neon)
        {
            using (var b = new SolidBrush(neon))
            using (var core = new SolidBrush(Color.White))
            {
                foreach (var s in sparks)
                {
                    float f = s.Life / s.Max;
                    b.Color = Color.FromArgb((int)(180 * f), neon);
                    g.FillEllipse(b, s.X - 2.5f, s.Y - 2.5f, 5, 5);
                    core.Color = Color.FromArgb((int)(255 * f), 255, 255, 255);
                    g.FillEllipse(core, s.X - 1, s.Y - 1, 2, 2);
                }
            }
        }

        void NeonText(Graphics g, string text, RectangleF r, Color neon, float k)
        {
            using (var gp = new GraphicsPath())
            {
                gp.AddString(text, titleFamily, (int)FontStyle.Bold, 21f, r, Near);
                for (int i = 4; i >= 1; i--)
                    using (var p = new Pen(Color.FromArgb((int)(34 * k), neon), i * 2.6f) { LineJoin = LineJoin.Round })
                        g.DrawPath(p, gp);
                Color face = Color.FromArgb(255, (neon.R + 255 * 2) / 3, (neon.G + 255 * 2) / 3, (neon.B + 255 * 2) / 3);
                using (var b = new SolidBrush(k > 0.5f ? face : Color.FromArgb(200, neon))) g.FillPath(b, gp);
            }
        }

        void DrawBattery(Graphics g, RectangleF bat, Color neon)
        {
            using (var p = new Pen(Color.FromArgb(210, Theme.Text), 1.3f)) using (var gp = Theme.Round(bat, 4)) g.DrawPath(p, gp);
            using (var b = new SolidBrush(Color.FromArgb(210, Theme.Text))) g.FillRectangle(b, bat.Right + 1, bat.Y + 4.5f, 3, 7);
            float fw = Math.Max(2, (bat.Width - 5) * pct / 100f);
            using (var b = new SolidBrush(Theme.Gold)) using (var gp = Theme.Round(new RectangleF(bat.X + 2.5f, bat.Y + 2.5f, fw, bat.Height - 5), 2)) g.FillPath(b, gp); // blue is light only, never a fill
            using (var b = new SolidBrush(Theme.Text)) g.DrawString(L.P(pct), Theme.Label, b, bat.Right + 8, bat.Y + 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { anim.Dispose(); if (cosmos != null) cosmos.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
