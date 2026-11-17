// Cuida tu Batería. Copyright (C) 2026 Alexis Reyes, Reyes Project.
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. See the LICENSE file.

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using RP;

namespace CuidaTuBateria
{
    internal sealed class UpdateOffer
    {
        public Version Ver;
        public string Text;      // "6.3" or "6.3.1", as people read it
        public string ExeName, ExeUrl, FirmaUrl, Sha256;
    }

    // Updates, only if the person said yes. Once a day the app asks GitHub which version is the latest
    // published one (drafts are never seen). Nothing about the laptop is sent: one plain request.
    //
    // A new version is offered only if its release carries firma.txt, signed with Reyes Project's own
    // private key (it lives in NordPass, never in the repository or in GitHub). The public half is
    // built into the app. So even someone who took over the GitHub account could not push an installer
    // to people's laptops: without the key, the app ignores it. The installer is then downloaded and
    // its SHA-256 must match the signed one, byte for byte, before it runs.
    internal static class Updater
    {
        const string Repo = "alexisreyes02/cuida-tu-bateria";
        const string Product = "cuida-tu-bateria";

        public static Version Current
        {
            get { var v = Assembly.GetExecutingAssembly().GetName().Version; return new Version(v.Major, v.Minor, Math.Max(0, v.Build)); }
        }

        static string PublicKey()
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("update.key.xml"))
            {
                if (s == null) return null;
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }

        // The exact text the key signs. Kept in one place so the app and the signing script agree.
        public static string Signed(string version, string exeName, string sha256)
        {
            return Product + "\n" + version + "\n" + exeName + "\n" + sha256;
        }

        public static bool Verify(string publicKeyXml, string message, string signatureB64)
        {
            try
            {
                byte[] sig = Convert.FromBase64String(signatureB64);
                using (var rsa = new RSACryptoServiceProvider(new CspParameters { ProviderType = 24 }))
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.FromXmlString(publicKeyXml);
                    if (rsa.KeySize < 3072) return false;
                    return rsa.VerifyData(Encoding.UTF8.GetBytes(message), sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch { return false; }
        }

        static HttpWebRequest Request(string url)
        {
            // .NET 4.6.1 does not offer TLS 1.2 by default on every Windows; GitHub requires it.
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var rq = (HttpWebRequest)WebRequest.Create(url);
            rq.UserAgent = "CuidaTuBateria/" + Current;
            rq.Accept = "application/vnd.github+json, application/octet-stream, text/plain";
            rq.Timeout = 30000; rq.ReadWriteTimeout = 60000;
            rq.AllowAutoRedirect = true;
            return rq;
        }

        static byte[] Get(string url, int maxBytes)
        {
            using (var rs = (HttpWebResponse)Request(url).GetResponse())
            using (var s = rs.GetResponseStream())
            using (var m = new MemoryStream())
            {
                var buf = new byte[81920]; int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    m.Write(buf, 0, n);
                    if (m.Length > maxBytes) throw new IOException("too large");
                }
                return m.ToArray();
            }
        }

        // Asks GitHub. Returns an offer only for a newer, correctly signed version; null otherwise.
        public static UpdateOffer Check()
        {
            string key = PublicKey();
            if (string.IsNullOrEmpty(key)) return null;
            string json = Encoding.UTF8.GetString(Get("https://api.github.com/repos/" + Repo + "/releases/latest", 1 << 20));
            var o = Parse(json, Current);
            if (o == null) return null;
            string firma = Encoding.UTF8.GetString(Get(o.FirmaUrl, 16384));
            return Accept(o, firma, key) ? o : null;
        }

        // Reads GitHub's answer: a newer tag with both its installer and firma.txt, or null.
        public static UpdateOffer Parse(string json, Version current)
        {
            var tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?(\\d+)\\.(\\d+)\\.(\\d+)\"");
            if (!tag.Success) return null;
            var ver = new Version(int.Parse(tag.Groups[1].Value), int.Parse(tag.Groups[2].Value), int.Parse(tag.Groups[3].Value));
            if (ver <= current) return null;
            string v3 = ver.ToString(3);
            string exeName = "CuidaTuBateriaSetup-" + v3 + ".exe";
            string exeUrl = null, firmaUrl = null;
            foreach (Match m in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"(https://github\\.com/" + Regex.Escape(Repo) + "/releases/download/[^\"]+)\""))
            {
                string u = m.Groups[1].Value;
                if (u.EndsWith("/" + exeName, StringComparison.Ordinal)) exeUrl = u;
                else if (u.EndsWith("/firma.txt", StringComparison.Ordinal)) firmaUrl = u;
            }
            if (exeUrl == null || firmaUrl == null) return null;   // unsigned release: ignored on purpose
            return new UpdateOffer { Ver = ver, Text = ver.Build == 0 ? ver.ToString(2) : v3, ExeName = exeName, ExeUrl = exeUrl, FirmaUrl = firmaUrl };
        }

        // Checks firma.txt: same product, version and file, a SHA-256, and Reyes Project's signature over them.
        public static bool Accept(UpdateOffer o, string firma, string publicKeyXml)
        {
            var lines = firma.Replace("\r", "").Split('\n');
            string v3 = o.Ver.ToString(3);
            if (lines.Length < 5 || lines[0] != Product || lines[1] != v3 || lines[2] != o.ExeName) return false;
            if (!Regex.IsMatch(lines[3], "^[0-9a-f]{64}$")) return false;
            if (!Verify(publicKeyXml, Signed(lines[1], lines[2], lines[3]), lines[4].Trim())) return false;
            o.Sha256 = lines[3];
            return true;
        }

        // Downloads the installer next to the app's data and checks it against the signed hash.
        public static string Download(UpdateOffer o)
        {
            byte[] exe = Get(o.ExeUrl, 64 << 20);
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(exe)).Replace("-", "").ToLowerInvariant();
            if (hash != o.Sha256) throw new InvalidDataException("hash mismatch");
            string dir = Path.Combine(Paths.Dir, "actualizacion");
            if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir)) try { File.Delete(f); } catch { }
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, o.ExeName);
            File.WriteAllBytes(path, exe);
            return path;
        }

        // The installer closes this app by itself, updates it keeping every setting and history, and opens it again.
        public static void Run(string installer, bool openWindow)
        {
            Process.Start(new ProcessStartInfo(installer, "/silent" + (openWindow ? " /abrir" : "")) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(installer) });
        }

        // Leftover installers from a finished update are removed at start.
        public static void Clean()
        {
            try
            {
                string dir = Path.Combine(Paths.Dir, "actualizacion");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch { }
        }
    }
}
