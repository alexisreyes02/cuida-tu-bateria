// Cuida tu Batería. Copyright (C) 2026 Alexis Reyes, Reyes Project.
// GPL-3.0-or-later. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using RP;

namespace CuidaTuBateria
{
    internal static class Paths
    {
        // The usage log and the health readings describe this laptop, so they live in Local AppData:
        // a roaming profile or a redirected AppData folder would otherwise mix two machines.
        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CuidaTuBateria"); } }
        public static string OldDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CuidaTuBateria"); } }

        // 5.x kept everything in Roaming. Move it once; the copy is checked before the original goes.
        public static void Migrate()
        {
            try
            {
                if (!Directory.Exists(OldDir)) return;
                Directory.CreateDirectory(Dir);
                foreach (var f in Directory.GetFiles(OldDir))
                {
                    string name = Path.GetFileName(f);
                    if (name.EndsWith(".tmp")) { try { File.Delete(f); } catch { } continue; }
                    string to = Path.Combine(Dir, name);
                    if (!File.Exists(to)) File.Copy(f, to);
                    if (File.Exists(to) && new FileInfo(to).Length >= new FileInfo(f).Length) File.Delete(f);
                }
                if (Directory.GetFileSystemEntries(OldDir).Length == 0) Directory.Delete(OldDir);
            }
            catch { }
        }
    }

    // Windows' own critical battery level: the laptop hibernates or shuts down there, not at 0 %.
    internal static class PowerInfo
    {
        [System.Runtime.InteropServices.DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
        [System.Runtime.InteropServices.DllImport("powrprof.dll")] static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr h);
        static Guid SubBattery = new Guid("e73a048d-bf27-4f12-9731-8b2076e8891f");
        static Guid BatLevelCrit = new Guid("9a66d8d7-4ff7-4ef9-b5a2-5a326ca2a469");

        public static int CriticalLevel()
        {
            try
            {
                IntPtr p;
                if (PowerGetActiveScheme(IntPtr.Zero, out p) != 0) return 5;
                try
                {
                    var scheme = (Guid)System.Runtime.InteropServices.Marshal.PtrToStructure(p, typeof(Guid));
                    uint v;
                    if (PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref SubBattery, ref BatLevelCrit, out v) == 0 && v <= 50) return (int)v;
                }
                finally { LocalFree(p); }
            }
            catch { }
            return 5;   // a common default; used only when Windows does not answer
        }

        // What the laptop draws from the battery right now, in watts, measured by the battery itself.
        // Two batteries are added up. -1 when it is not discharging or the firmware does not say.
        public static double DischargeWatts()
        {
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT DischargeRate, Discharging FROM BatteryStatus"))
                {
                    double mw = 0; bool any = false;
                    foreach (System.Management.ManagementObject o in s.Get())
                    {
                        object d = o["Discharging"];
                        if (d != null && !Convert.ToBoolean(d)) continue;
                        int r = Convert.ToInt32(o["DischargeRate"]);
                        if (r > 0 && r < 200000) { mw += r; any = true; }
                    }
                    return any ? mw / 1000.0 : -1;
                }
            }
            catch { }
            return -1;
        }

        // A rough reading of how hard the laptop is working, for a thin and light laptop.
        public static string UseLevel(double w)
        {
            if (w < 8) return L.T("uso ligero", "light use");
            if (w < 15) return L.T("uso normal", "normal use");
            return L.T("uso pesado", "heavy use");
        }
    }

    internal static class Txt
    {
        static readonly string[] MonthsEs = { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };
        static readonly string[] ShortEs = { "ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic" };
        static readonly string[] MonthsEn = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
        static readonly string[] ShortEn = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        public static string N(int v) { return v.ToString("N0", L.Culture); }
        public static string N1(double v) { return v.ToString("0.0", L.Culture); }

        // "3 h 10 min", "45 min". Rounded to five minutes: the estimate is not more precise than that.
        public static string Dur(double minutes)
        {
            if (minutes < 0) return "";
            if (minutes < 10) return Math.Max(1, (int)Math.Round(minutes)) + " min";
            int m = (int)(Math.Round(minutes / 5.0) * 5);
            int h = m / 60; m %= 60;
            if (h == 0) return m + " min";
            return m == 0 ? h + " h" : h + " h " + m + " min";
        }

        static bool D(string ymd, out DateTime d)
        {
            return DateTime.TryParseExact(ymd ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
        }

        public static string DateLong(string ymd)
        {
            DateTime d;
            if (!D(ymd, out d)) return ymd;
            return L.En ? MonthsEn[d.Month - 1] + " " + d.Day + ", " + d.Year : d.Day + " de " + MonthsEs[d.Month - 1] + " de " + d.Year;
        }

        public static string Today()
        {
            var d = DateTime.Now;
            return L.En ? MonthsEn[d.Month - 1] + " " + d.Day + ", " + d.Year : d.Day + " de " + MonthsEs[d.Month - 1] + " de " + d.Year;
        }

        // "6 años", "1 año y 3 meses", "8 meses". Empty when the date is unknown.
        public static string Age(string ymd)
        {
            DateTime d;
            if (!D(ymd, out d)) return "";
            var now = DateTime.Now;
            int months = (now.Year - d.Year) * 12 + now.Month - d.Month - (now.Day < d.Day ? 1 : 0);
            if (months < 0) return "";
            int y = months / 12, m = months % 12;
            if (y == 0) return m <= 1 ? L.T("menos de dos meses", "less than two months") : m + L.T(" meses", " months");
            string ys = y == 1 ? L.T("1 año", "1 year") : y + L.T(" años", " years");
            if (m == 0) return ys;
            return ys + L.T(" y ", " and ") + (m == 1 ? L.T("1 mes", "1 month") : m + L.T(" meses", " months"));
        }

        public static string AgeYears(string ymd)
        {
            DateTime d;
            if (!D(ymd, out d)) return "";
            int y = DateTime.Now.Year - d.Year - (DateTime.Now.DayOfYear < d.DayOfYear ? 1 : 0);
            if (y < 1) return L.T("menos de 1 año", "less than 1 year");
            return y == 1 ? L.T("1 año", "1 year") : y + L.T(" años", " years");
        }

        public static string MonthLong(string ym)
        {
            int y, mo;
            if (!Parse(ym, out y, out mo)) return ym;
            return L.En ? MonthsEn[mo - 1] + " " + y : MonthsEs[mo - 1] + " de " + y;
        }

        public static string MonthShort(string ym)
        {
            int y, mo;
            if (!Parse(ym, out y, out mo)) return ym;
            return (L.En ? ShortEn : ShortEs)[mo - 1] + " " + (y % 100).ToString("00");
        }

        public static string Clock(DateTime local)
        {
            return local.ToString(L.En ? "h:mm tt" : "h:mm tt", L.En ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("es-VE"));
        }

        // Apple and others treat 80 % as the expected level at the end of a battery's rated life,
        // so "excellent" starts at 90 %.
        public static string Verdict(float pct)
        {
            if (pct >= 90) return L.T("Excelente", "Excellent");
            if (pct >= 80) return L.T("Buena", "Good");
            if (pct >= 60) return L.T("Gastada", "Worn");
            return L.T("Débil", "Weak");
        }

        // The one-line advice for the technician's picture and the plain-words report.
        public static string Advice(float pct)
        {
            if (pct >= 80) return L.T("Sigue bien. No hace falta cambiarla.", "Still in good shape. No need to replace it.");
            if (pct >= 60) return L.T("Aguanta, pero ya se nota. Cuídala y ve pensando en el repuesto.", "It holds up, but you can tell. Look after it and start thinking about a replacement.");
            return L.T("Conviene cambiarla pronto.", "It is worth replacing soon.");
        }

        public static bool Parse(string ym, out int y, out int mo)
        {
            y = mo = 0;
            if (ym == null || ym.Length < 7) return false;
            return int.TryParse(ym.Substring(0, 4), out y) && int.TryParse(ym.Substring(5, 2), out mo) && mo >= 1 && mo <= 12;
        }

        public static int MonthsBetween(string a, string b)
        {
            int y1, m1, y2, m2;
            if (!Parse(a, out y1, out m1) || !Parse(b, out y2, out m2)) return 0;
            return (y2 - y1) * 12 + (m2 - m1);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Usage log: one line each time the percent or the plug changes, plus a heartbeat every
    // 10 minutes. It stays on this laptop (uso.csv) and keeps 30 days. From it the app learns how
    // fast this particular person drains and charges this particular battery.
    // ---------------------------------------------------------------------------------------------
    internal struct Sample { public DateTime T; public int Pct; public bool Plugged; }

    internal sealed class UsageStats
    {
        public double BatteryHours, HighHours, Rate, Days;
        public int Unplugs;
    }

    internal sealed class Autonomy
    {
        public double Minutes = -1;      // to 0 %
        public double FullMinutes = -1;  // a full charge, with this person's usage
        public string Basis = "none";    // real, windows, report, none
    }

    internal sealed class UsageLog
    {
        const int KeepDays = 30;
        static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(10);
        static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(16);   // longer means sleep or app closed
        // Many firmwares (HP among them) report 100 % on the charger and then fall to 95 % within
        // minutes of unplugging. That drop is a recalibration, not use, so readings at 98 % or more
        // on battery never count toward the drain rate.
        const int TopBand = 98;
        readonly List<Sample> s = new List<Sample>();
        DateTime lastWrite = DateTime.MinValue;
        int lastPct = -1; bool lastPlugged;

        // Percentage points drained on battery since the app started counting, kept across the
        // 30-day window: 100 points is one full charge cycle.
        public double Drained;
        public event Action DrainedChanged;

        static string FilePath { get { return Path.Combine(Paths.Dir, "uso.csv"); } }

        public int Count { get { return s.Count; } }

        public void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var cut = DateTime.UtcNow.AddDays(-KeepDays);
                int dropped = 0;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var a = line.Split(',');
                    if (a.Length < 3) continue;
                    DateTime t; int p;
                    if (!DateTime.TryParse(a[0], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) continue;
                    if (!int.TryParse(a[1], out p)) continue;
                    if (t < cut) { dropped++; continue; }
                    s.Add(new Sample { T = t, Pct = p, Plugged = a[2].Trim() == "1" });
                }
                if (s.Count > 0) { var last = s[s.Count - 1]; lastPct = last.Pct; lastPlugged = last.Plugged; lastWrite = last.T; }
                if (dropped > 0) Rewrite();
            }
            catch { }
        }

        void Rewrite()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var x in s) sb.Append(Line(x));
                SafeFile.Write(FilePath, sb.ToString());
            }
            catch { }
        }

        static string Line(Sample x)
        {
            return x.T.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture) + "," + x.Pct + "," + (x.Plugged ? "1" : "0") + "\n";
        }

        // New lines wait in memory and reach the disk every 30 minutes, on suspend, on logoff and on exit:
        // an old hard disk is not woken up every minute for one line.
        readonly StringBuilder pending = new StringBuilder();
        DateTime lastFlush = DateTime.UtcNow;
        public bool Changed;

        public void Record(int pct, bool plugged, DateTime nowUtc)
        {
            if (pct == lastPct && plugged == lastPlugged && nowUtc - lastWrite < Heartbeat) return;
            if (s.Count > 0)
            {
                var prev = s[s.Count - 1];
                int drop = prev.Pct - pct;
                // Every point used on battery counts toward the cycles, including the top band.
                if (!prev.Plugged && !plugged && drop > 0 && drop <= 10 && nowUtc - prev.T <= MaxGap)
                { Drained += drop; if (DrainedChanged != null) DrainedChanged(); }
            }
            lastPct = pct; lastPlugged = plugged; lastWrite = nowUtc;
            var x = new Sample { T = nowUtc, Pct = pct, Plugged = plugged };
            s.Add(x);
            Changed = true;
            pending.Append(Line(x));
            if (nowUtc - lastFlush > TimeSpan.FromMinutes(30)) Flush();
        }

        public void Flush()
        {
            lastFlush = DateTime.UtcNow;
            if (pending.Length == 0) return;
            try { Directory.CreateDirectory(Paths.Dir); File.AppendAllText(FilePath, pending.ToString()); pending.Clear(); } catch { }
        }

#if PREVIEW
        public void Fake(Sample x) { s.Add(x); }
#endif

        bool UsablePair(Sample a, Sample b, DateTime cut)
        {
            if (b.T < cut || a.Plugged || b.Plugged) return false;
            var dt = b.T - a.T;
            if (dt <= TimeSpan.Zero || dt > MaxGap) return false;
            if (b.Pct > a.Pct) return false;        // a recalibration jump, not usage
            if (a.Pct >= TopBand) return false;     // the post-unplug firmware drop
            return true;
        }

        // Average drain on battery over the last days, in percent per hour.
        public bool HistoryRate(DateTime nowUtc, int days, out double rate)
        {
            rate = 0;
            double drop = 0, hours = 0;
            var cut = nowUtc.AddDays(-days);
            for (int i = 1; i < s.Count; i++)
            {
                var a = s[i - 1]; var b = s[i];
                if (!UsablePair(a, b, cut)) continue;
                drop += a.Pct - b.Pct; hours += (b.T - a.T).TotalHours;
            }
            if (hours < 0.5 || drop < 3) return false;
            rate = drop / hours;
            return rate > 0.5 && rate < 200;
        }

        // Drain of the current unplugged stretch, if it is long enough to mean something.
        public bool SessionRate(DateTime nowUtc, int pctNow, out double rate)
        {
            rate = 0;
            int i = s.Count - 1;
            if (i < 0 || s[i].Plugged) return false;
            while (i > 0 && !s[i - 1].Plugged && s[i].T - s[i - 1].T <= MaxGap) i--;
            // start after the firmware's top band
            while (i < s.Count - 1 && s[i].Pct >= TopBand) i++;
            var first = s[i];
            if (first.Pct >= TopBand) return false;
            double hours = (nowUtc - first.T).TotalHours;
            int drop = first.Pct - pctNow;
            if (hours < 1.0 / 12 || drop < 2) return false;
            rate = drop / hours;
            return rate > 0.5 && rate < 200;
        }

        // How fast this charger fills this battery, in percent per hour, below 95 % where charging
        // is still steady. Only rising readings count: a flat line on the charger means it is not charging.
        public bool ChargeRate(DateTime nowUtc, out double rate)
        {
            rate = 0;
            double rise = 0, hours = 0;
            var cut = nowUtc.AddDays(-14);
            for (int i = 1; i < s.Count; i++)
            {
                var a = s[i - 1]; var b = s[i];
                if (b.T < cut || !a.Plugged || !b.Plugged || a.Pct >= 95 || b.Pct <= a.Pct) continue;
                var dt = b.T - a.T;
                if (dt <= TimeSpan.Zero || dt > MaxGap) continue;
                rise += b.Pct - a.Pct; hours += dt.TotalHours;
            }
            if (hours < 0.25 || rise < 5) return false;
            rate = rise / hours;
            return rate > 2 && rate < 400;
        }

        public Autonomy Estimate(DateTime nowUtc, int pct, bool plugged, int windowsSeconds, double reportFullMinutes, int critical)
        {
            int usable = Math.Max(0, pct - critical), usableFull = Math.Max(1, 100 - critical);
            var r = new Autonomy();
            double hist, sess = 0;
            bool hasHist = HistoryRate(nowUtc, 14, out hist);
            bool hasSess = !plugged && SessionRate(nowUtc, pct, out sess);
            double rate = 0;
            if (hasHist && hasSess) rate = 0.6 * sess + 0.4 * hist;
            else if (hasSess) rate = sess;
            else if (hasHist) rate = hist;
            if (rate > 0)
            {
                r.Basis = "real";
                r.Minutes = usable / rate * 60;
                r.FullMinutes = usableFull / rate * 60;
            }
            else if (!plugged && windowsSeconds > 0)
            {
                r.Basis = "windows";
                r.Minutes = windowsSeconds / 60.0;
                if (reportFullMinutes > 0) r.FullMinutes = reportFullMinutes;
            }
            else if (reportFullMinutes > 0)
            {
                // What Windows measured on this laptop in the last weeks, until our own log has enough.
                r.Basis = "report";
                r.Minutes = reportFullMinutes * usable / usableFull;
                r.FullMinutes = reportFullMinutes;
            }
            return r;
        }

        public UsageStats Week(DateTime nowUtc, int upper)
        {
            var st = new UsageStats();
            var cut = nowUtc.AddDays(-7);
            DateTime first = DateTime.MaxValue;
            for (int i = 0; i < s.Count; i++)
            {
                if (s[i].T < cut) continue;
                if (s[i].T < first) first = s[i].T;
                if (i == 0) continue;
                var a = s[i - 1]; var b = s[i];
                if (a.T < cut) continue;
                if (a.Plugged && !b.Plugged) st.Unplugs++;
                var dt = b.T - a.T;
                if (dt <= TimeSpan.Zero || dt > MaxGap) continue;
                if (!a.Plugged && !b.Plugged) st.BatteryHours += dt.TotalHours;
                if (a.Plugged && b.Plugged && a.Pct >= upper && b.Pct >= upper) st.HighHours += dt.TotalHours;
            }
            st.Days = first == DateTime.MaxValue ? 0 : (nowUtc - first).TotalDays;
            double r;
            if (HistoryRate(nowUtc, 7, out r)) st.Rate = r;
            return st;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Battery health: WMI first, then Windows' own battery report. The report also gives the cycle
    // count, the battery's name and its capacity month by month since Windows started measuring.
    // Measured at most once a day and cached in salud.txt, so the app starts light.
    // ---------------------------------------------------------------------------------------------
    internal sealed class MonthPoint { public string Month; public int Full, Ref; }

    internal sealed class HealthInfo
    {
        public string Date = "";              // yyyy-MM-dd of the measure
        public int Design, Full, Spec, Cycles = -1;
        public string BatDate = "";           // yyyy-MM-dd the battery was made, when the firmware says
        public double FullRuntimeMin;         // minutes on a full charge, as Windows measured it
        public int Version;
        public bool Estimated, Echo;
        public string BatName = "", BatMaker = "", Chem = "", Maker = "", Model = "";
        public float Pct = -1;
        public List<MonthPoint> Months = new List<MonthPoint>();

        public const int CurrentVersion = 2;   // a cache written by an older build is measured again

        public int Reference { get { return Estimated ? Spec : (Echo ? 0 : Design); } }

        public string Detail
        {
            get
            {
                if (Full <= 0) return L.T("Esta laptop no reporta su capacidad.", "This laptop does not report its capacity.");
                if (Pct < 0) return L.T("Hoy " + Txt.N(Full) + " mWh. Tu laptop no reporta la de fábrica.", "Today " + Txt.N(Full) + " mWh. Your laptop does not report the factory figure.");
                return Estimated
                    ? L.T("Su medidor: " + Txt.N(Full) + " mWh. La ficha: unos " + Txt.N(Reference) + " mWh.", "Its gauge: " + Txt.N(Full) + " mWh. Spec sheet: about " + Txt.N(Reference) + " mWh.")
                    : L.T("Hoy guarda " + Txt.N(Full) + " de " + Txt.N(Reference) + " mWh de fábrica", "Holds " + Txt.N(Full) + " of its factory " + Txt.N(Reference) + " mWh today");
            }
        }
    }

    internal static class HealthStore
    {
        static string FilePath { get { return Path.Combine(Paths.Dir, "salud.txt"); } }

        public static string Today { get { return DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); } }

        public static HealthInfo Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var h = new HealthInfo();
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i < 1) continue;
                    string k = line.Substring(0, i), v = line.Substring(i + 1);
                    int n; float f;
                    switch (k)
                    {
                        case "fecha": h.Date = v; break;
                        case "design": int.TryParse(v, out h.Design); break;
                        case "full": int.TryParse(v, out h.Full); break;
                        case "spec": int.TryParse(v, out h.Spec); break;
                        case "ciclos": if (int.TryParse(v, out n)) h.Cycles = n; break;
                        case "estimada": h.Estimated = v == "1"; break;
                        case "eco": h.Echo = v == "1"; break;
                        case "pct": if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) h.Pct = f; break;
                        case "bateria": h.BatName = v; break;
                        case "fabricante_bateria": h.BatMaker = v; break;
                        case "quimica": h.Chem = v; break;
                        case "marca": h.Maker = v; break;
                        case "modelo": h.Model = v; break;
                        case "fecha_bateria": h.BatDate = v; break;
                        case "autonomia_llena": double rt; if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out rt)) h.FullRuntimeMin = rt; break;
                        case "version": int.TryParse(v, out h.Version); break;
                        case "mes":
                            var a = v.Split(',');
                            int full, rf;
                            if (a.Length >= 3 && int.TryParse(a[1], out full) && int.TryParse(a[2], out rf)) h.Months.Add(new MonthPoint { Month = a[0], Full = full, Ref = rf });
                            break;
                    }
                }
                return h;
            }
            catch { return null; }
        }

        public static void Save(HealthInfo h)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("fecha=").Append(h.Date).Append('\n');
                sb.Append("design=").Append(h.Design).Append('\n');
                sb.Append("full=").Append(h.Full).Append('\n');
                sb.Append("spec=").Append(h.Spec).Append('\n');
                sb.Append("ciclos=").Append(h.Cycles).Append('\n');
                sb.Append("estimada=").Append(h.Estimated ? "1" : "0").Append('\n');
                sb.Append("eco=").Append(h.Echo ? "1" : "0").Append('\n');
                sb.Append("pct=").Append(h.Pct.ToString(CultureInfo.InvariantCulture)).Append('\n');
                sb.Append("bateria=").Append(Clean(h.BatName)).Append('\n');
                sb.Append("fabricante_bateria=").Append(Clean(h.BatMaker)).Append('\n');
                sb.Append("quimica=").Append(Clean(h.Chem)).Append('\n');
                sb.Append("marca=").Append(Clean(h.Maker)).Append('\n');
                sb.Append("modelo=").Append(Clean(h.Model)).Append('\n');
                sb.Append("fecha_bateria=").Append(h.BatDate).Append('\n');
                sb.Append("autonomia_llena=").Append(h.FullRuntimeMin.ToString("0", CultureInfo.InvariantCulture)).Append('\n');
                sb.Append("version=").Append(HealthInfo.CurrentVersion).Append('\n');
                foreach (var m in h.Months) sb.Append("mes=").Append(m.Month).Append(',').Append(m.Full).Append(',').Append(m.Ref).Append('\n');
                SafeFile.Write(FilePath, sb.ToString());
            }
            catch { }
        }

        static string Clean(string s) { return (s ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim(); }

        // Published factory capacities for model families whose firmware does not report one.
        // Offline on purpose: nothing leaves the laptop. Source: HP HT03XL spec, 41.04 Wh, fitted to
        // HP 14-ce, 14-cf, 15-cs and 15-da.
        static readonly string[] SpecModels = { "14-cf", "14-ce", "15-cs", "15-da" };
        static readonly int[] SpecMwh = { 41040, 41040, 41040, 41040 };

        static int SpecCapacity(string maker, string model)
        {
            string m = (maker + " " + model).ToLowerInvariant();
            if (!m.Contains("hp") && !m.Contains("hewlett")) return 0;
            for (int i = 0; i < SpecModels.Length; i++) if (m.Contains(SpecModels[i])) return SpecMwh[i];
            return 0;
        }

        static bool Same(int a, int b) { return a > 0 && b > 0 && Math.Abs(a - b) <= a / 200; }

        public static HealthInfo Measure(HealthInfo previous)
        {
            var h = new HealthInfo { Date = Today, Version = HealthInfo.CurrentVersion };
            h.Maker = WmiProp(@"root\CIMV2", "SELECT Manufacturer FROM Win32_ComputerSystem", "Manufacturer");
            h.Model = WmiProp(@"root\CIMV2", "SELECT Model FROM Win32_ComputerSystem", "Model");
            int design = WmiInt("SELECT DesignedCapacity FROM BatteryStaticData", "DesignedCapacity");
            int full = WmiInt("SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", "FullChargedCapacity");

            var report = ReadReport();
            if (report != null)
            {
                string bat = Block(report, "Batteries") ?? report;
                int d2 = 0, f2 = 0;
                foreach (Match bm in Regex.Matches(bat, "<Battery>(.*?)</Battery>", RegexOptions.Singleline))
                { d2 += Num(Element(bm.Groups[1].Value, "DesignCapacity")); f2 += Num(Element(bm.Groups[1].Value, "FullChargeCapacity")); }
                if (d2 == 0 && f2 == 0) { d2 = Num(Element(bat, "DesignCapacity")); f2 = Num(Element(bat, "FullChargeCapacity")); }
                if ((design <= 0 || full <= 0 || Same(design, full)) && d2 > 0 && f2 > 0) { design = d2; full = f2; }
                int cyc = Num(Element(bat, "CycleCount"));
                h.Cycles = cyc > 0 ? cyc : (Element(bat, "CycleCount") != null ? 0 : -1);
                h.BatName = Element(bat, "Id") ?? "";
                h.BatMaker = Element(bat, "Manufacturer") ?? "";
                h.Chem = Element(bat, "Chemistry") ?? "";
                h.BatDate = BirthDate(bat) ?? "";
                ParseHistory(report, h);
            }

            h.Design = design; h.Full = full;
            h.Echo = Same(design, full);
            h.Spec = SpecCapacity(h.Maker, h.Model);
            if (h.Echo && h.Spec > 0 && full > 0 && full < h.Spec * 1.2)
            {
                // The firmware hides the factory value, so use the published spec for this model family.
                h.Estimated = true;
                h.Pct = Math.Min(100f, 100f * full / h.Spec);
            }
            else if (h.Echo || full <= 0 || design <= 0 || full >= design * 1.6) h.Pct = -1;
            else h.Pct = Math.Min(100f, 100f * full / design);

            // Month by month: what the report knows, what earlier measures knew, and today.
            var map = new SortedDictionary<string, MonthPoint>(StringComparer.Ordinal);
            if (previous != null) foreach (var m in previous.Months) map[m.Month] = m;
            foreach (var m in h.Months) map[m.Month] = m;
            if (full > 0)
            {
                string cur = DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                // The report's median for this month is steadier than one reading; use today's only without it.
                if (!h.Months.Exists(m => m.Month == cur))
                    map[cur] = new MonthPoint { Month = cur, Full = full, Ref = h.Echo ? 0 : design };
            }
            h.Months = new List<MonthPoint>(map.Values);
            if (h.Months.Count > 36) h.Months.RemoveRange(0, h.Months.Count - 36);
            return h;
        }

        // Windows writes one entry per week or so, and HP firmware recalibrates often, so a single
        // entry can be off by thousands of mWh. Each month shows the median of its entries.
        static void ParseHistory(string x, HealthInfo h)
        {
            var byMonth = new SortedDictionary<string, List<int[]>>(StringComparer.Ordinal);
            var runtimes = new List<double>();
            int hs = x.IndexOf("<History", StringComparison.Ordinal);
            if (hs >= 0)
            {
                int he = x.IndexOf("</History>", hs, StringComparison.Ordinal);
                string block = he > hs ? x.Substring(hs, he - hs) : x.Substring(hs);
                // Attribute form: <HistoryEntry LocalStartDate="..." DesignCapacity="..." FullChargeCapacity="..." />
                foreach (Match m in Regex.Matches(block, "<HistoryEntry\\b([^>]*)>"))
                {
                    string attrs = m.Groups[1].Value;
                    Add(byMonth, Attr(attrs, "LocalStartDate") ?? Attr(attrs, "StartDate"), Num(Attr(attrs, "FullChargeCapacity")), Num(Attr(attrs, "DesignCapacity")));
                    double rt = Duration(Attr(attrs, "EstimatedFullChargeActiveTime"));
                    if (rt > 0) runtimes.Add(rt);
                }
                // Element form, in case a Windows build writes children instead of attributes.
                if (byMonth.Count == 0)
                    foreach (Match m in Regex.Matches(block, "<HistoryEntry>(.*?)</HistoryEntry>", RegexOptions.Singleline))
                    {
                        string e = m.Groups[1].Value;
                        Add(byMonth, Element(e, "LocalStartDate") ?? Element(e, "StartDate"), Num(Element(e, "FullChargeCapacity")), Num(Element(e, "DesignCapacity")));
                        double rt = Duration(Element(e, "EstimatedFullChargeActiveTime"));
                        if (rt > 0) runtimes.Add(rt);
                    }
            }
            h.Months = new List<MonthPoint>();
            foreach (var kv in byMonth)
            {
                var l = kv.Value;
                l.Sort((a, b) => a[0].CompareTo(b[0]));
                var mid = l[l.Count / 2];
                int full = l.Count % 2 == 1 ? mid[0] : (l[l.Count / 2 - 1][0] + mid[0]) / 2;
                h.Months.Add(new MonthPoint { Month = kv.Key, Full = full, Ref = Same(mid[1], mid[0]) ? 0 : mid[1] });
            }
            // Runtime on a full charge, as Windows measured it: the median of the last four periods,
            // or the estimate "since the OS was installed" when the periods do not carry it.
            if (runtimes.Count > 0)
            {
                var last = runtimes.GetRange(Math.Max(0, runtimes.Count - 4), Math.Min(4, runtimes.Count));
                last.Sort();
                h.FullRuntimeMin = last[last.Count / 2];
            }
            else
            {
                string re = Block(x, "RuntimeEstimates");
                if (re != null)
                {
                    string fc = Block(re, "FullChargeCapacity") ?? re;
                    h.FullRuntimeMin = Duration(Element(fc, "ActiveRuntime"));
                }
            }
            if (h.FullRuntimeMin < 10 || h.FullRuntimeMin > 60 * 48) h.FullRuntimeMin = 0;
        }

        // ISO 8601 ("PT3H48M45S") or clock form ("3:48:45"), in minutes.
        static double Duration(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            try { return System.Xml.XmlConvert.ToTimeSpan(s.Trim()).TotalMinutes; } catch { }
            TimeSpan t;
            if (TimeSpan.TryParse(s.Trim(), CultureInfo.InvariantCulture, out t)) return t.TotalMinutes;
            return 0;
        }

        static void Add(SortedDictionary<string, List<int[]>> map, string date, int full, int design)
        {
            if (string.IsNullOrEmpty(date) || date.Length < 7 || full <= 0) return;
            string ym = date.Substring(0, 7);
            int y, mo;
            if (!Txt.Parse(ym, out y, out mo)) return;
            List<int[]> l;
            if (!map.TryGetValue(ym, out l)) map[ym] = l = new List<int[]>();
            l.Add(new[] { full, design });
        }

        // The battery's birth date. Some firmware has a field for it; HP writes it inside the serial
        // number ("06509 09/29/2020"). Only the date is kept, never the serial.
        static string BirthDate(string bat)
        {
            foreach (var raw in new[] { Element(bat, "ManufactureDate"), Element(bat, "SerialNumber") })
            {
                if (string.IsNullOrEmpty(raw)) continue;
                var m = Regex.Match(raw, "(\\d{1,2})/(\\d{1,2})/(\\d{4})");
                if (m.Success) { var d = Safe(int.Parse(m.Groups[3].Value), int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)); if (d != null) return d; }
                m = Regex.Match(raw, "(\\d{4})-(\\d{2})-(\\d{2})");
                if (m.Success) { var d = Safe(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)); if (d != null) return d; }
            }
            return null;
        }

        static string Safe(int y, int mo, int d)
        {
            if (y < 2000 || y > DateTime.Now.Year || mo < 1 || mo > 12 || d < 1 || d > 31) return null;
            try { var dt = new DateTime(y, mo, d); return dt > DateTime.Now ? null : dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); } catch { return null; }
        }

        static string Block(string x, string name)
        {
            int a = x.IndexOf("<" + name + ">", StringComparison.Ordinal);
            if (a < 0) return null;
            int b = x.IndexOf("</" + name + ">", a, StringComparison.Ordinal);
            return b > a ? x.Substring(a, b - a) : null;
        }

        static string Attr(string attrs, string name)
        {
            var m = Regex.Match(attrs, "\\b" + name + "=\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        static string Element(string x, string name)
        {
            var m = Regex.Match(x, "<" + name + ">([^<]*)</" + name + ">");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        static int Num(string s) { int n; return s != null && int.TryParse(s.Trim(), out n) ? n : 0; }

        static string WmiProp(string scope, string query, string prop)
        {
            try
            {
                using (var s = new ManagementObjectSearcher(scope, query))
                    foreach (ManagementObject o in s.Get()) return (Convert.ToString(o[prop]) ?? "").Trim();
            }
            catch { }
            return "";
        }

        static int WmiInt(string query, string field)
        {
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\WMI", query))
                {
                    // A laptop with two batteries (ThinkPad Power Bridge, Surface Book) reports one instance each.
                    int sum = 0;
                    foreach (ManagementObject o in s.Get()) sum += Convert.ToInt32(o[field]);
                    return sum;
                }
            }
            catch { }
            return 0;
        }

        static string ReadReport()
        {
            string f = Path.Combine(Path.GetTempPath(), "ctb_battery_" + Process.GetCurrentProcess().Id + ".xml");
            try
            {
                var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"), "/batteryreport /xml /output \"" + f + "\"")
                { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
                using (var p = Process.Start(psi)) { if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } return null; } }
                if (!File.Exists(f)) return null;
                byte[] raw = File.ReadAllBytes(f);
                return (raw.Length > 1 && raw[1] == 0) ? Encoding.Unicode.GetString(raw) : Encoding.UTF8.GetString(raw);
            }
            catch { return null; }
            finally { try { if (File.Exists(f)) File.Delete(f); } catch { } }
        }

        // Windows' report carries the battery serial and the computer name, so it lives in its own temp
        // folder with an unpredictable name and is deleted the next time the app starts or closes.
        static string ReportDir { get { return Path.Combine(Path.GetTempPath(), "CuidaTuBateria"); } }

        public static void CleanReports()
        {
            try { if (Directory.Exists(ReportDir)) Directory.Delete(ReportDir, true); } catch { }
            try { var old = Path.Combine(Path.GetTempPath(), "Informe de batería de Windows.html"); if (File.Exists(old)) File.Delete(old); } catch { }
        }

        // Windows' full battery report, in its own HTML (English). Opened only when asked.
        public static string WriteWindowsReport()
        {
            Directory.CreateDirectory(ReportDir);
            string f = Path.Combine(ReportDir, L.T("Informe de batería de Windows ", "Windows battery report ") + Guid.NewGuid().ToString("N").Substring(0, 8) + ".html");
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"), "/batteryreport /output \"" + f + "\"")
            { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
            using (var p = Process.Start(psi)) { if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } return null; } }
            return File.Exists(f) ? f : null;
        }

        // The report in plain words, as a list of short paragraphs.
        public static List<string> Plain(HealthInfo h, double drained, string since)
        {
            var r = new List<string>();
            if (h == null || h.Full <= 0) { r.Add(L.T("Todavía no tengo los datos de tu batería. Dame un minuto.", "I do not have your battery data yet. Give me a minute.")); return r; }
            string age = Txt.Age(h.BatDate);
            bool genericName = h.BatName.Length == 0 || h.BatName.Equals("Primary", StringComparison.OrdinalIgnoreCase) || h.BatName.Equals("Battery", StringComparison.OrdinalIgnoreCase);
            if (age.Length > 0)
                r.Add(L.T("Tu batería se fabricó el " + Txt.DateLong(h.BatDate) + ": tiene " + age + ".",
                          "Your battery was made on " + Txt.DateLong(h.BatDate) + ". It is " + age + " old."));
            if (h.Cycles > 0) r.Add(L.T("Lleva " + Txt.N(h.Cycles) + " ciclos de carga completos.", "It has gone through " + Txt.N(h.Cycles) + " full charge cycles."));
            else if (drained >= 20 && since.Length > 0)
            {
                // The firmware does not count cycles, so the app counts them from what it has seen drained.
                double cycles = drained / 100.0;
                r.Add(L.T("Desde el " + Txt.DateLong(since) + " la usaste al menos el equivalente a " + Txt.N1(cycles) + (cycles >= 1.95 || cycles < 0.95 ? " cargas completas." : " carga completa."),
                          "Since " + Txt.DateLong(since) + " you have used at least the equivalent of " + Txt.N1(cycles) + (cycles >= 1.95 || cycles < 0.95 ? " full charges." : " full charge.")));
            }
            if (h.Pct >= 0)
                r.Add(L.T("Hoy guarda " + L.P(h.Pct) + " de lo que traía nueva" + (h.Estimated ? " (según la ficha del modelo)" : "") + ". " + Txt.Advice(h.Pct),
                          "Today it holds " + L.P(h.Pct) + " of what it held when new" + (h.Estimated ? " (based on the model's spec sheet)" : "") + ". " + Txt.Advice(h.Pct)));
            else r.Add(L.T("Hoy guarda " + Txt.N(h.Full) + " mWh. Tu laptop no dice cuánto traía de fábrica, así que no invento un porcentaje.",
                           "Today it holds " + Txt.N(h.Full) + " mWh. Your laptop does not report its factory capacity, so I will not make up a percentage."));
            if (h.Months.Count >= 2)
            {
                var a = h.Months[0]; var b = h.Months[h.Months.Count - 1];
                int months = Txt.MonthsBetween(a.Month, b.Month);
                if (a.Full > 0 && months >= 2)
                {
                    double loss = 100.0 * (a.Full - b.Full) / a.Full;
                    if (loss >= 1)
                    {
                        r.Add(L.T("En " + Txt.MonthLong(a.Month) + " guardaba " + Txt.N(a.Full) + " mWh. Desde entonces perdió " + L.P(loss) + " en " + months + " meses.",
                                  "In " + Txt.MonthLong(a.Month) + " it held " + Txt.N(a.Full) + " mWh. Since then it has lost " + L.P(loss) + " in " + months + " months."));
                        if (months >= 6) r.Add(L.T("A ese paso pierde cerca de " + L.P(loss * 12 / months) + " al año. Es un estimado: depende del uso y del calor.",
                                                   "At that pace it loses about " + L.P(loss * 12 / months) + " a year. It is an estimate: it depends on use and heat."));
                    }
                    else r.Add(L.T("Desde " + Txt.MonthLong(a.Month) + " casi no ha perdido capacidad.", "Since " + Txt.MonthLong(a.Month) + " it has barely lost capacity."));
                }
            }
            return r;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Brand: some laptops carry a charge limit from the factory, and that one really cuts the charge.
    // Every path below comes from the manufacturer's own support page (see README, sources).
    // ---------------------------------------------------------------------------------------------
    internal static class Brand
    {
        public static string Name(string maker, string model)
        {
            string m = (maker ?? "").Trim();
            string l = m.ToLowerInvariant();
            if (l.Contains("hewlett") || l == "hp") m = "HP";
            else if (l.Contains("lenovo")) m = "Lenovo";
            else if (l.Contains("asus")) m = "ASUS";
            else if (l.Contains("dell")) m = "Dell";
            else if (l.Contains("samsung")) m = "Samsung";
            else if (l.Contains("acer")) m = "Acer";
            string mo = (model ?? "").Trim();
            if (mo.StartsWith(m + " ", StringComparison.OrdinalIgnoreCase)) mo = mo.Substring(m.Length + 1);
            string full = (m + " " + mo).Trim();
            return full.Length > 0 ? full : L.T("tu laptop", "your laptop");
        }

        // Returns true when the brand has a real limit the person can turn on.
        public static bool Guide(string maker, string model, out string text)
        {
            string l = ((maker ?? "") + " " + (model ?? "")).ToLowerInvariant();
            if (l.Contains("lenovo"))
            {
                text = L.T("Lenovo trae su propio límite, y ese sí corta la carga. En Lenovo Vantage entra a Dispositivo, Configuración de mi dispositivo, Batería, y activa el Modo de conservación (IdeaPad) o el umbral de carga (ThinkPad).",
                           "Lenovo has its own limit, and that one really stops the charge. In Lenovo Vantage go to Device, My Device Settings, Battery, and turn on Conservation Mode (IdeaPad) or the charge threshold (ThinkPad).");
                return true;
            }
            if (l.Contains("asus"))
            {
                text = L.T("ASUS trae su propio límite, y ese sí corta la carga. En MyASUS busca el Modo de cuidado de la batería y elige Modo equilibrado: deja de cargar al pasar de 80 %.",
                           "ASUS has its own limit, and that one really stops the charge. In MyASUS find Battery Care Mode and choose Balanced Mode: it stops charging above 80 %.");
                return true;
            }
            if (l.Contains("dell"))
            {
                text = L.T("Dell trae su propio límite, y ese sí corta la carga. En Dell Power Manager elige Primarily AC Use, o Custom para decir dónde empieza y dónde termina la carga.",
                           "Dell has its own limit, and that one really stops the charge. In Dell Power Manager choose Primarily AC Use, or Custom to set where charging starts and stops.");
                return true;
            }
            if (l.Contains("samsung"))
            {
                text = L.T("En los Galaxy Book 3 y posteriores, abre Samsung Settings, Batería y rendimiento, y activa Proteger la batería: se detiene en 85 %.",
                           "On Galaxy Book 3 and later, open Samsung Settings, Battery and performance, and turn on Protect battery: it stops at 85 %.");
                return true;
            }
            if (l.Contains("acer"))
            {
                text = L.T("Acer trae su propio límite, y ese sí corta la carga. En el Centro de control de Acer (o Care Center) activa Límite de carga de la batería: se detiene en 80 %.",
                           "Acer has its own limit, and that one really stops the charge. In Acer Control Center (or Care Center) turn on Battery Charge Limit: it stops at 80 %.");
                return true;
            }
            if (l.Contains("hewlett") || l.Contains("hp "))
            {
                if (l.Contains("elitebook") || l.Contains("probook") || l.Contains("zbook"))
                {
                    text = L.T("Tu HP es de la línea de negocios. En el BIOS busca HP Battery Health Manager y elige Maximize My Battery Health: la carga se queda en 80 %.",
                               "Your HP is a business model. In the BIOS find HP Battery Health Manager and choose Maximize My Battery Health: the charge stays at 80 %.");
                    return true;
                }
                text = L.T("Tu HP no trae un límite de carga que yo conozca. Por eso estoy aquí: te aviso, y tú desconectas.",
                           "Your HP has no charge limit that I know of. That is why I am here: I remind you, and you unplug.");
                return false;
            }
            text = L.T("No tengo verificado si tu marca trae límite de carga. Busca en la app del fabricante algo como \"límite de carga\" o \"cuidado de la batería\". Mientras tanto, te aviso yo.",
                       "I have not verified whether your brand has a charge limit. Look in the maker's app for something like \"charge limit\" or \"battery care\". Meanwhile, I will remind you.");
            return false;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Health chart: one bar per month, the factory line as a thread of halo light. Drawn in the
    // window and, bigger, into a picture the person can keep or share.
    // ---------------------------------------------------------------------------------------------
    internal static class HealthChart
    {
        // Font.Height asks the font table for its line spacing; computed from the size it is the same
        // everywhere and does not trip older GDI+ builds with fonts loaded from memory.
        static float LineH(Font f) { return (float)Math.Ceiling(f.Size * 1.35f); }

        public static void Draw(Graphics g, RectangleF r, HealthInfo h, Font small, bool large)
        {
            if (h == null || h.Months.Count == 0)
            {
                using (var b = new SolidBrush(Theme.Dim))
                    g.DrawString(L.T("Todavía no hay meses que mostrar. Cada mes guardo cuánta carga aguanta tu batería.",
                                     "No months to show yet. Every month I keep how much charge your battery holds."), small, b, r, Theme.Center);
                return;
            }
            var pts = h.Months;
            int n = Math.Min(pts.Count, large ? 24 : 12);
            var list = pts.GetRange(pts.Count - n, n);
            int refLine = h.Reference;
            int max = refLine, min = int.MaxValue;
            foreach (var m in list) { max = Math.Max(max, Math.Max(m.Full, m.Ref)); min = Math.Min(min, m.Full); }
            if (max <= 0) return;
            // Bars start near the lowest month, not at zero, so a few points of wear are visible.
            int floor = (int)(Math.Floor(min * 0.85 / 1000.0) * 1000);
            float top = r.Y + (large ? 26 : 18), bottom = r.Bottom - (large ? 26 : 18);
            float slot = r.Width / n;
            float bw = Math.Min(large ? 46 : 26, slot * 0.62f);
            Func<int, float> Y = v => bottom - (bottom - top) * Math.Max(0, v - floor) / (float)Math.Max(1, max - floor);

            using (var grid = new Pen(Color.FromArgb(28, Theme.Gold), 1f))
                for (int i = 1; i <= 4; i++) g.DrawLine(grid, r.X, bottom - (bottom - top) * i / 4f, r.Right, bottom - (bottom - top) * i / 4f);

            for (int i = 0; i < n; i++)
            {
                var m = list[i];
                float x = r.X + slot * i + (slot - bw) / 2;
                float y = Y(m.Full);
                var bar = new RectangleF(x, y, bw, Math.Max(2, bottom - y));
                bool last = i == n - 1;
                using (var br = new LinearGradientBrush(new RectangleF(bar.X, bar.Y - 1, bar.Width, bar.Height + 2), last ? Theme.GoldSoft : Theme.Gold, Theme.GoldDeep, 90f))
                using (var gp = Theme.Round(bar, Math.Min(4, bw / 3)))
                    g.FillPath(br, gp);
                int rf = m.Ref > 0 ? m.Ref : refLine;
                string top1 = rf > 0 ? L.P(100.0 * m.Full / rf) : Txt.N1(m.Full / 1000.0);
                bool every = n <= (large ? 24 : 8) || i % 2 == (n - 1) % 2;
                using (var b = new SolidBrush(last ? Theme.GoldSoft : Theme.Text))
                    if (every) g.DrawString(top1, small, b, new RectangleF(x - 20, y - LineH(small) - 2, bw + 40, LineH(small) + 2), Theme.CenterTop);
                using (var b = new SolidBrush(Theme.Dim))
                    if (every) g.DrawString(Txt.MonthShort(m.Month), small, b, new RectangleF(x - 20, bottom + 4, bw + 40, LineH(small) + 2), Theme.CenterTop);
            }
            if (refLine > 0)
            {
                float y = Y(refLine);
                using (var p = new Pen(Color.FromArgb(170, Theme.GoldSoft), 1.2f) { DashStyle = DashStyle.Dash }) g.DrawLine(p, r.X, y, r.Right, y);
                using (var b = new SolidBrush(Theme.GoldSoft))
                    g.DrawString(L.T("nueva: ", "new: ") + Txt.N(refLine) + " mWh" + (h.Estimated ? L.T(" (según la ficha)", " (spec sheet)") : ""), small, b, r.X, y - LineH(small) - 1);
            }
        }

        // A 1200 x 675 picture: good for WhatsApp, Instagram or the technician.
        public static string Export(HealthInfo h, string machine)
        {
            const int W = 1200, H = 675;
            using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var cos = new Cosmos(W, H, 1f, 0, 0, 0, 90))
                using (var title = Fonts.Serif(40f))
                using (var sub = Fonts.Sans(20f))
                using (var small = Fonts.Sans(16f))
                using (var big = Fonts.Serif(30f))
                {
                    Theme.Quality(g);
                    cos.Draw(g, 0);
                    Bolt.Draw(g, new RectangleF(58, 52, 54, 54), Theme.GoldSoft, Theme.GoldDeep, 0.8f);
                    using (var b = new SolidBrush(Theme.Text)) g.DrawString(L.T("La salud de mi batería", "My battery's health"), title, b, 126, 50);
                    string age = Txt.AgeYears(h.BatDate);
                    using (var b = new SolidBrush(Theme.Dim))
                        g.DrawString(machine + L.T(", medida el ", ", measured on ") + Txt.Today() + (age.Length > 0 ? L.T(". Batería fabricada hace " + age + ".", ". Battery made " + age + " ago.") : ""), sub, b, 129, 104);
                    if (h.Pct >= 0)
                        using (var b = new SolidBrush(Theme.GoldSoft))
                            g.DrawString(L.T("Conserva ", "Keeps ") + L.P(h.Pct) + (h.Estimated ? L.T(" (estimado)", " (estimated)") : ""), big, b, W - 60, 56, Theme.Far);
                    if (h.Pct >= 0)
                        using (var b = new SolidBrush(Theme.GoldSoft))
                            g.DrawString(L.T("Veredicto: ", "Verdict: ") + Txt.Advice(h.Pct) + (h.Cycles > 0 ? L.T("  Ciclos: ", "  Cycles: ") + Txt.N(h.Cycles) + "." : ""), small, b, 129, 138);
                    Draw(g, new RectangleF(70, 184, W - 140, 390), h, small, true);
                    using (var p = new Pen(Color.FromArgb(110, Theme.Gold), 1f)) g.DrawRectangle(p, 0.5f, 0.5f, W - 1, H - 1);
                    using (var b = new SolidBrush(Theme.Dim))
                    {
                        g.DrawString(L.T("Capacidad de carga completa, mes a mes, según Windows.", "Full charge capacity, month by month, as Windows measured it."), small, b, 70, H - 64);
                        g.DrawString(L.T("Cuida tu Batería, de Reyes Project", "Cuida tu Batería, by Reyes Project"), small, b, W - 70, H - 64, Theme.Far);
                    }
                }
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (string.IsNullOrEmpty(dir)) dir = Paths.Dir;
                Directory.CreateDirectory(dir);
                string f = Path.Combine(dir, L.T("Salud de mi batería (", "My battery health (") + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ").png");
                bmp.Save(f, ImageFormat.Png);
                return f;
            }
        }
    }
}
