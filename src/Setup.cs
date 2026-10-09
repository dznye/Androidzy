using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Xml;

namespace Androidzy
{
    sealed class Component
    {
        public string Name;     // shown to the user
        public string Url;
        public string Sha1;
        public long Size;       // download size in bytes
        public string FinalDir; // where the unpacked folder ends up
        public string Marker;   // file that exists once the component is installed
        public bool Installed { get { return File.Exists(Marker); } }
    }

    // Finds an Android SDK that already has what Androidzy needs, or downloads the pieces from Google's
    // public SDK repository (the same files Android Studio's SDK Manager installs). Nothing from Google
    // is bundled with Androidzy itself.
    static class Setup
    {
        public const string TermsUrl = "https://developer.android.com/studio/terms";
        const string RepoBase = "https://dl.google.com/android/repository/";
        const string ImageBase = RepoBase + "sys-img/google_apis_playstore/";
        const string ImagePackage = "system-images;android-34;google_apis_playstore;x86_64";

        public static bool IsComplete(string sdk)
        {
            return File.Exists(Path.Combine(sdk, @"emulator\emulator.exe"))
                && File.Exists(Path.Combine(sdk, @"emulator\qemu\windows-x86_64\qemu-system-x86_64.exe"))
                && File.Exists(Path.Combine(sdk, @"platform-tools\adb.exe"))
                && File.Exists(Path.Combine(sdk, Paths.ImageRel, "system.img"));
        }

        // Own copy first, then (unless disabled) an SDK installed by Android Studio.
        public static string FindSdk(bool allowExisting)
        {
            string own = Path.Combine(Paths.DataRoot, "sdk");
            if (IsComplete(own)) return own;
            if (allowExisting)
                foreach (string c in ExistingSdkCandidates())
                    if (!string.IsNullOrEmpty(c) && IsComplete(c)) return c;
            return null;
        }

        static IEnumerable<string> ExistingSdkCandidates()
        {
            yield return Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
            yield return Environment.GetEnvironmentVariable("ANDROID_HOME");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Android\Sdk");
        }

        // ---- catalog -------------------------------------------------------------------------------

        public static List<Component> FetchCatalog(string sdk)
        {
            XmlDocument repo = LoadXml(RepoBase + "repository2-3.xml");
            XmlDocument img = LoadXml(ImageBase + "sys-img2-3.xml");
            List<Component> list = new List<Component>();
            list.Add(Pick(repo, "emulator", RepoBase, "Android Emulator", "x64",
                Path.Combine(sdk, "emulator"), Path.Combine(sdk, @"emulator\emulator.exe")));
            list.Add(Pick(repo, "platform-tools", RepoBase, "Platform tools (adb)", null,
                Path.Combine(sdk, "platform-tools"), Path.Combine(sdk, @"platform-tools\adb.exe")));
            list.Add(Pick(img, ImagePackage, ImageBase, "Android 14 system image (Google Play)", null,
                Path.Combine(sdk, Paths.ImageRel), Path.Combine(sdk, Paths.ImageRel, "system.img")));
            return list;
        }

        static XmlDocument LoadXml(string url)
        {
            using (WebClient wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "Androidzy";
                XmlDocument d = new XmlDocument();
                d.LoadXml(wc.DownloadString(url));
                return d;
            }
        }

        static string Text(XmlNode n, string child)
        {
            XmlNode c = n.SelectSingleNode(child);
            return c == null ? "" : c.InnerText.Trim();
        }

        static int Num(XmlNode n, string child)
        {
            int v;
            return int.TryParse(Text(n, child), out v) ? v : 0;
        }

        // newest stable (channel-0) revision of a package, Windows archive
        static Component Pick(XmlDocument doc, string path, string baseUrl, string name, string arch, string finalDir, string marker)
        {
            XmlNode best = null;
            int[] bestRev = null;
            foreach (XmlNode p in doc.SelectNodes("//remotePackage[@path='" + path + "']"))
            {
                XmlNode ch = p.SelectSingleNode("channelRef");
                if (ch == null || ch.Attributes["ref"] == null || ch.Attributes["ref"].Value != "channel-0") continue;
                int[] rev = { Num(p, "revision/major"), Num(p, "revision/minor"), Num(p, "revision/micro") };
                if (best == null || Compare(rev, bestRev) > 0) { best = p; bestRev = rev; }
            }
            if (best == null) throw new InvalidOperationException("Google's catalog has no stable '" + path + "' package.");

            foreach (XmlNode a in best.SelectNodes("archives/archive"))
            {
                string os = Text(a, "host-os"), ar = Text(a, "host-arch");
                if (os.Length > 0 && os != "windows") continue;
                if (arch != null && ar.Length > 0 && ar != arch) continue;
                XmlNode c = a.SelectSingleNode("complete");
                if (c == null) continue;
                string url = Text(c, "url");
                Component comp = new Component();
                comp.Name = name;
                comp.Url = url.StartsWith("http") ? url : baseUrl + url;
                comp.Sha1 = Text(c, "checksum");
                comp.Size = long.Parse(Text(c, "size"));
                comp.FinalDir = finalDir;
                comp.Marker = marker;
                return comp;
            }
            throw new InvalidOperationException("Google's catalog has no Windows download for '" + path + "'.");
        }

        static int Compare(int[] a, int[] b)
        {
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
            return 0;
        }

        // ---- install -------------------------------------------------------------------------------

        public static long TotalBytes(List<Component> comps)
        {
            long t = 0;
            foreach (Component c in comps) if (!c.Installed) t += c.Size;
            return t;
        }

        // Downloads and unpacks every missing component. report(text, percent) is throttled; cancelled() is polled.
        // Components that are already installed are skipped, so a failed run can simply be repeated.
        public static void Install(List<Component> comps, string sdk, Action<string, int> report, Func<bool> cancelled)
        {
            string tmp = Path.Combine(sdk, ".tmp");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);

            double grand = TotalBytes(comps) * 2.0;   // download and unpack weigh the same
            if (grand <= 0) return;
            long done = 0;
            Stopwatch clock = Stopwatch.StartNew();
            long lastReport = -1000;

            foreach (Component c in comps)
            {
                if (c.Installed) continue;
                string zip = Path.Combine(tmp, "pkg" + done + ".zip");
                string dir = Path.Combine(tmp, "unpacked" + done);
                long baseDone = done;
                long got = 0;

                Download(c, zip, n =>
                {
                    got += n;
                    if (clock.ElapsedMilliseconds - lastReport < 150) return;
                    lastReport = clock.ElapsedMilliseconds;
                    report("Downloading " + c.Name + "   " + (got >> 20) + " / " + (c.Size >> 20) + " MB",
                        (int)((baseDone + got) * 100.0 / grand));
                }, cancelled);
                done += c.Size;

                report("Unpacking " + c.Name + "...", (int)(done * 100.0 / grand));
                Extract(zip, dir, f =>
                {
                    if (clock.ElapsedMilliseconds - lastReport < 150) return;
                    lastReport = clock.ElapsedMilliseconds;
                    report("Unpacking " + c.Name + "   " + (int)(f * 100) + "%", (int)((done + f * c.Size) * 100.0 / grand));
                }, cancelled);
                File.Delete(zip);
                Place(dir, c.FinalDir);
                done += c.Size;
            }
            Directory.Delete(tmp, true);
            report("Done", 100);
        }

        static void Download(Component c, string file, Action<int> onBytes, Func<bool> cancelled)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(c.Url);
            req.UserAgent = "Androidzy";
            req.Timeout = 30000;
            req.ReadWriteTimeout = 60000;
            using (WebResponse resp = req.GetResponse())
            using (Stream src = resp.GetResponseStream())
            using (FileStream dst = File.Create(file))
            using (SHA1 sha = SHA1.Create())
            {
                byte[] buf = new byte[1 << 16];
                int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    if (cancelled()) throw new OperationCanceledException();
                    dst.Write(buf, 0, n);
                    sha.TransformBlock(buf, 0, n, null, 0);
                    onBytes(n);
                }
                sha.TransformFinalBlock(buf, 0, 0);
                string got = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                if (got != c.Sha1.ToLowerInvariant())
                    throw new IOException("The download of " + c.Name + " is corrupt (checksum mismatch). Please try again.");
            }
        }

        static void Extract(string zipPath, string destDir, Action<double> progress, Func<bool> cancelled)
        {
            Directory.CreateDirectory(destDir);
            string root = Path.GetFullPath(destDir).TrimEnd('\\') + "\\";
            using (ZipArchive z = ZipFile.OpenRead(zipPath))
            {
                long total = 0, done = 0;
                foreach (ZipArchiveEntry e in z.Entries) total += e.Length;
                foreach (ZipArchiveEntry e in z.Entries)
                {
                    if (cancelled()) throw new OperationCanceledException();
                    string target = Path.GetFullPath(Path.Combine(destDir, e.FullName));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Unsafe path in archive: " + e.FullName);
                    if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    e.ExtractToFile(target, true);
                    done += e.Length;
                    progress(total == 0 ? 1.0 : (double)done / total);
                }
            }
        }

        // Archives hold one top-level folder (emulator, platform-tools, x86_64): move it to its final place.
        static void Place(string unpacked, string finalDir)
        {
            string[] subs = Directory.GetDirectories(unpacked);
            string src = (subs.Length == 1 && Directory.GetFiles(unpacked).Length == 0) ? subs[0] : unpacked;
            if (Directory.Exists(finalDir)) Directory.Delete(finalDir, true);
            Directory.CreateDirectory(Path.GetDirectoryName(finalDir));
            Directory.Move(src, finalDir);
        }
    }
}
