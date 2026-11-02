// Cuida tu Batería. Copyright (C) 2026 Alexis Reyes, Reyes Project.
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace RP
{
    // Two languages, decided once at start: Spanish when Windows speaks Spanish, English otherwise.
    // The person can force either from the tray menu. Every string goes through T(es, en).
    internal static class L
    {
        public static bool En;
        public static string T(string es, string en) { return En ? en : es; }
        // "80 %" in Spanish, "80%" in English.
        public static string P(int n) { return En ? n + "%" : n + " %"; }
        public static string P(double n) { return P((int)Math.Round(n)); }
        public static System.Globalization.CultureInfo Culture
        {
            get { return System.Globalization.CultureInfo.GetCultureInfo(En ? "en-US" : "es-ES"); }
        }
        public static void Auto()
        {
            // Many people in Venezuela run an English Windows with Spanish regional settings. English is
            // chosen only when neither the display language nor the regional format is Spanish.
            try
            {
                En = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "es"
                  && System.Globalization.CultureInfo.CurrentCulture.TwoLetterISOLanguageName != "es";
            }
            catch { En = false; }
        }
    }

    // Errors that would otherwise end in a dialog are written here instead, small and local.
    internal static class Log
    {
        public static void Write(string where, Exception ex)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CuidaTuBateria");
                Directory.CreateDirectory(dir);
                string f = Path.Combine(dir, "errores.log");
                if (File.Exists(f) && new FileInfo(f).Length > 100000) File.Delete(f);
                File.AppendAllText(f, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "  " + where + "  " + (ex == null ? "" : ex.GetType().Name + ": " + ex.Message) + "\n");
            }
            catch { }
        }
    }

    // Writes to a temporary file first and swaps it in, so a crash or a forced close mid-write
    // never leaves a half-written file behind.
    internal static class SafeFile
    {
        // If a swap was cut short, the complete copy is the .tmp one: use it.
        public static string[] ReadLines(string path)
        {
            if (File.Exists(path)) return File.ReadAllLines(path);
            if (File.Exists(path + ".tmp")) return File.ReadAllLines(path + ".tmp");
            return null;
        }

        public static void Write(string path, string text)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); return; } catch { }
                File.Delete(path);
            }
            File.Move(tmp, path);
        }
    }

    // The house typefaces travel inside the exe, so they render the same on every laptop.
    // Literata for titles, IBM Plex Sans for text (both SIL Open Font License 1.1, see assets/fonts).
    internal static class Fonts
    {
        static readonly PrivateFontCollection pfc = new PrivateFontCollection();
        static readonly List<IntPtr> pinned = new List<IntPtr>();
        static readonly Dictionary<string, FontFamily> fams = new Dictionary<string, FontFamily>();

        static Fonts()
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var res in asm.GetManifestResourceNames())
            {
                if (!res.StartsWith("font.")) continue;
                try
                {
                    byte[] data;
                    using (var s = asm.GetManifestResourceStream(res))
                    using (var ms = new MemoryStream()) { s.CopyTo(ms); data = ms.ToArray(); }
                    IntPtr p = Marshal.AllocCoTaskMem(data.Length);
                    Marshal.Copy(data, 0, p, data.Length);
                    pinned.Add(p); // GDI+ reads from this memory for the life of the process
                    pfc.AddMemoryFont(p, data.Length);
                }
                catch { }
            }
            try { foreach (var f in pfc.Families) fams[f.Name] = f; } catch { }
        }

        public static Font Get(string family, float px, FontStyle style, string fallback)
        {
#if PREVIEW
            // Mono's GDI+ cannot draw letters from memory fonts; preview screenshots use the installed copies.
            if (Environment.GetEnvironmentVariable("CF_SYSFONTS") == "1") return new Font(family, px, style, GraphicsUnit.Pixel);
#endif
            FontFamily ff;
            if (fams.TryGetValue(family, out ff))
            {
                try { return new Font(ff, px, style, GraphicsUnit.Pixel); } catch { }
            }
            return new Font(fallback, px, style, GraphicsUnit.Pixel);
        }

        public static FontFamily Family(string family, string fallback)
        {
#if PREVIEW
            if (Environment.GetEnvironmentVariable("CF_SYSFONTS") == "1") return new FontFamily(family);
#endif
            FontFamily ff;
            if (fams.TryGetValue(family, out ff)) return ff;
            return new FontFamily(fallback);
        }

        public static Font Serif(float px) { return Get("Literata", px, FontStyle.Bold, "Georgia"); }
        public static Font SerifLight(float px)
        {
            // GDI+ may file the Light cut under "Literata Light" or under "Literata" itself.
            if (fams.ContainsKey("Literata Light")) return Get("Literata Light", px, FontStyle.Regular, "Georgia");
            return Get("Literata", px, FontStyle.Regular, "Georgia");
        }
        public static Font Sans(float px) { return Get("IBM Plex Sans", px, FontStyle.Regular, "Segoe UI"); }
        public static Font SansSemi(float px)
        {
            // The SemiBold file registers its own family name; old GDI+ builds may use either.
#if PREVIEW
            if (Environment.GetEnvironmentVariable("CF_SYSFONTS") == "1") return new Font("IBM Plex Sans SmBld", px, FontStyle.Regular, GraphicsUnit.Pixel);
#endif
            FontFamily ff;
            foreach (var n in new[] { "IBM Plex Sans SemiBold", "IBM Plex Sans SmBld" })
                if (fams.TryGetValue(n, out ff)) try { return new Font(ff, px, FontStyle.Regular, GraphicsUnit.Pixel); } catch { }
            return Get("IBM Plex Sans", px, FontStyle.Bold, "Segoe UI Semibold");
        }
    }

    internal static class Theme
    {
        // Palette of the house: night and gold. Blue exists only as halo light, never as a fill.
        public static readonly Color Bg = Color.FromArgb(3, 5, 9);            // noche #030509
        public static readonly Color Card = Color.FromArgb(11, 12, 17);
        public static readonly Color Gold = Color.FromArgb(242, 163, 58);     // #F2A33A
        public static readonly Color GoldSoft = Color.FromArgb(255, 209, 138); // #FFD18A
        public static readonly Color GoldDeep = Color.FromArgb(176, 104, 24);
        public static readonly Color Ember = Color.FromArgb(201, 119, 43);
        public static readonly Color Halo = Color.FromArgb(128, 184, 255);    // light only
        public static readonly Color Charge = GoldSoft;
        public static readonly Color Alert = Gold;
        public static readonly Color Calm = Color.FromArgb(214, 196, 160);
        public static readonly Color Text = Color.FromArgb(242, 238, 230);
        public static readonly Color Dim = Color.FromArgb(150, 142, 128);

        public static readonly Font Title = Fonts.Serif(24f);
        public static readonly Font Sub = Fonts.Sans(13f);
        public static readonly Font Small = Fonts.Sans(12.5f);
        public static readonly Font Label = Fonts.SansSemi(11.5f);
        public static readonly Font Body = Fonts.Sans(13.5f);
        public static readonly Font Head = Fonts.Serif(19f);
        public static readonly Font Big = Fonts.SerifLight(64f);
        public static readonly Font Btn = Fonts.SansSemi(13f);

        public static void Quality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2f;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Spaced(Graphics g, string s, Font f, Color c, float x, float y, float spacing)
        {
            using (var b = new SolidBrush(c))
            {
                foreach (char ch in s)
                {
                    string t = ch.ToString();
                    g.DrawString(t, f, b, x, y, Typo);
                    x += g.MeasureString(t, f, 1000, Typo).Width + spacing;
                    if (ch == ' ') x += f.Size * 0.12f;
                }
            }
        }

        public static float SpacedWidth(Graphics g, string s, Font f, float spacing)
        {
            float w = 0;
            foreach (char ch in s)
            {
                w += g.MeasureString(ch.ToString(), f, 1000, Typo).Width + spacing;
                if (ch == ' ') w += f.Size * 0.12f;
            }
            return w;
        }

        // StringFormat.GenericTypographic builds a new object on every call; keep one.
        public static readonly StringFormat Typo = (StringFormat)StringFormat.GenericTypographic.Clone();
        public static readonly StringFormat Center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        public static readonly StringFormat CenterTop = new StringFormat { Alignment = StringAlignment.Center };
        public static readonly StringFormat Far = new StringFormat { Alignment = StringAlignment.Far };

        public static Color A(Color c, int a) { return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c); }
    }

    internal static class Bolt
    {
        public static readonly PointF[] P = {
            new PointF(0.62f, 0.04f), new PointF(0.26f, 0.56f), new PointF(0.47f, 0.56f),
            new PointF(0.38f, 0.96f), new PointF(0.76f, 0.40f), new PointF(0.54f, 0.40f), new PointF(0.80f, 0.04f)
        };

        public static GraphicsPath Path(RectangleF r)
        {
            var pts = new PointF[P.Length];
            for (int i = 0; i < P.Length; i++)
                pts[i] = new PointF(r.X + P[i].X * r.Width, r.Y + P[i].Y * r.Height);
            var gp = new GraphicsPath();
            gp.AddPolygon(pts);
            return gp;
        }

        public static void Draw(Graphics g, RectangleF r, Color top, Color bottom, float glow)
        {
            using (var gp = Path(r))
            {
                if (glow > 0.01f)
                {
                    for (int i = 6; i >= 1; i--)
                    {
                        using (var pen = new Pen(Color.FromArgb((int)(26 * glow), top), r.Width * 0.045f * i))
                        {
                            pen.LineJoin = LineJoin.Round;
                            g.DrawPath(pen, gp);
                        }
                    }
                }
                var rr = new RectangleF(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height));
                using (var br = new LinearGradientBrush(rr, top, bottom, 90f))
                    g.FillPath(br, gp);
                using (var pen = new Pen(Color.FromArgb(200, 255, 244, 220), Math.Max(1f, r.Width * 0.012f)))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPath(pen, gp);
                }
            }
        }

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr h);

        public static Icon MakeIcon(Color top, Color bottom, int size) { return MakeIcon(top, bottom, size, 1f); }

        static Color Dimmed(Color c, float k)
        {
            return Color.FromArgb(c.A, (int)(10 + (c.R - 10) * k), (int)(9 + (c.G - 9) * k), (int)(8 + (c.B - 8) * k));
        }

        // bright 0..1 lets the tray icon breathe: low values dim the bolt and shrink its glow.
        public static Icon MakeIcon(Color top, Color bottom, int size, float bright)
        {
            top = Dimmed(top, 0.45f + 0.55f * bright);
            bottom = Dimmed(bottom, 0.45f + 0.55f * bright);
            float glowK = size >= 24 ? 0.25f + 0.95f * bright : 0f;
            using (var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    float pad = size * 0.04f;
                    Draw(g, new RectangleF(pad, pad, size - 2 * pad, size - 2 * pad), top, bottom, glowK);
                }
                IntPtr h = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { try { DestroyIcon(h); } catch { } }
            }
        }
    }

    // Deep space backdrop: cached nebula, drifting stars, and a small Gargantua style black hole.
    internal sealed class Cosmos : IDisposable
    {
        struct Star { public float X, Y, Z, Ph; public int Tint; }

        readonly int w, h;
        readonly Bitmap nebula;
        readonly Star[] stars;
        readonly SolidBrush starBrush = new SolidBrush(Color.White);
        readonly float hx, hy, hr;
        public PointF Parallax;

        public Cosmos(int w, int h, float scale, float holeX, float holeY, float holeR) : this(w, h, scale, holeX, holeY, holeR, 64) { }

        public Cosmos(int w, int h, float scale, float holeX, float holeY, float holeR, int starCount)
        {
            this.w = w; this.h = h; hx = holeX; hy = holeY; hr = holeR;
            nebula = new Bitmap((int)(w * scale), (int)(h * scale), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(nebula))
            {
                g.ScaleTransform(scale, scale);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Theme.Bg);
                Glow(g, w * 0.88f, h * 0.10f, 380, Color.FromArgb(34, 242, 163, 58));
                Glow(g, w * 0.06f, h * 0.98f, 320, Color.FromArgb(26, 128, 184, 255));
                Glow(g, w * 0.46f, h * 0.55f, 440, Color.FromArgb(12, 255, 209, 138));
                Glow(g, w * 0.70f, h * 0.90f, 260, Color.FromArgb(18, 201, 119, 43));
            }
            var r = new Random(11);
            stars = new Star[starCount];
            for (int i = 0; i < stars.Length; i++)
                stars[i] = new Star { X = (float)r.NextDouble() * w, Y = (float)r.NextDouble() * h, Z = (float)r.NextDouble(), Ph = (float)r.NextDouble() * 6.28f, Tint = r.Next(5) };
        }

        static void Glow(Graphics g, float cx, float cy, float rad, Color c)
        {
            using (var gp = new GraphicsPath())
            {
                gp.AddEllipse(cx - rad, cy - rad, rad * 2, rad * 2);
                using (var pb = new PathGradientBrush(gp))
                {
                    pb.CenterColor = c;
                    pb.SurroundColors = new[] { Color.FromArgb(0, c) };
                    g.FillPath(pb, gp);
                }
            }
        }

        public void Step(float dt)
        {
            for (int i = 0; i < stars.Length; i++)
            {
                stars[i].X += (3f + 9f * stars[i].Z) * dt;
                stars[i].Y += (1f + 2.5f * stars[i].Z) * dt;
                if (stars[i].X > w + 4) stars[i].X = -4;
                if (stars[i].Y > h + 4) stars[i].Y = -4;
                stars[i].Ph += dt * (0.5f + stars[i].Z * 1.8f);
            }
        }

        public void Draw(Graphics g, float t)
        {
            var im = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor; // 1:1 device pixels, no need for bicubic every frame
            g.DrawImage(nebula, 0, 0, w, h);
            g.InterpolationMode = im;
            for (int i = 0; i < stars.Length; i++)
            {
                var s = stars[i];
                float tw = 0.5f + 0.5f * (float)Math.Sin(s.Ph);
                int a = (int)(255 * (0.25f + 0.75f * s.Z) * (0.35f + 0.65f * tw));
                Color c = s.Tint == 0 ? Theme.GoldSoft : (s.Tint == 1 ? Theme.Halo : Color.White);
                starBrush.Color = Color.FromArgb(Math.Max(0, Math.Min(255, a)), c);
                float sz = 0.9f + s.Z * 1.5f;
                float x = s.X + Parallax.X * s.Z * 16f;
                float y = s.Y + Parallax.Y * s.Z * 16f;
                g.FillEllipse(starBrush, x, y, sz, sz);
            }
            if (hr > 0) DrawHole(g, t);
        }

        void DrawHole(Graphics g, float t)
        {
            float cx = hx + Parallax.X * 5f, cy = hy + Parallax.Y * 5f;
            float pulse = 0.85f + 0.15f * (float)Math.Sin(t * 0.9f);
            for (int i = 0; i < 5; i++)
            {
                using (var p = new Pen(Color.FromArgb((int)((58 - 10 * i) * pulse), 255, 176, 96), 1f + i * 1.7f))
                    g.DrawEllipse(p, cx - hr - i * 0.6f, cy - hr - i * 0.6f, 2 * hr + i * 1.2f, 2 * hr + i * 1.2f);
            }
            using (var b = new SolidBrush(Color.Black))
                g.FillEllipse(b, cx - hr, cy - hr, 2 * hr, 2 * hr);
            for (int i = 4; i >= 0; i--)
            {
                using (var p = new Pen(Color.FromArgb((int)((130 - 24 * i) * pulse), 255, 214, 160), 0.8f + i * 1.5f))
                    g.DrawEllipse(p, cx - hr * 2.4f, cy - hr * 0.17f, hr * 4.8f, hr * 0.34f);
            }
            using (var p = new Pen(Color.FromArgb(230, 255, 245, 230), 1f))
                g.DrawEllipse(p, cx - hr * 2.4f, cy - hr * 0.17f, hr * 4.8f, hr * 0.34f);
        }

        public void Dispose() { nebula.Dispose(); starBrush.Dispose(); }
    }
}
