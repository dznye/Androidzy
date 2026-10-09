using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Androidzy
{
    // Things done to the running Android device through adb after it has booted.
    static class Phone
    {
        // The emulator reports this GPS position on every boot (New York City, USA).
        public const string UsLongitude = "-74.0060";
        public const string UsLatitude = "40.7128";

        // Launcher apps that stay when the preinstalled apps are removed: Google Play, Files,
        // Settings (needed to add accounts) and the Google app (the launcher's search bar is built on it).
        static readonly string[] Keep =
        {
            "com.android.vending",
            "com.google.android.documentsui",
            "com.android.settings",
            "com.google.android.googlequicksearchbox"
        };

        public static string Adb(string serial, string args, int timeoutMs)
        {
            ProcessStartInfo psi = new ProcessStartInfo(Paths.Adb, "-s " + serial + " " + args);
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            // adb prints some results (for example "1 file pushed") on stderr, so return both streams
            StringBuilder err = new StringBuilder();
            using (Process p = new Process())
            {
                p.StartInfo = psi;
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
                p.Start();
                p.BeginErrorReadLine();
                string output = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } }
                else p.WaitForExit();   // lets the async stderr reader finish
                lock (err) return output + err.ToString();
            }
        }

        // single-quote a string for the device shell
        public static string Sh(string s) { return "'" + s.Replace("'", "'\\''") + "'"; }

        public static bool WaitForBoot(string serial, int timeoutMs)
        {
            DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < end)
            {
                try { if (Adb(serial, "shell getprop sys.boot_completed", 8000).Trim() == "1") return true; } catch { }
                Thread.Sleep(2000);
            }
            return false;
        }

        public static bool SetUsLocation(string serial)
        {
            // 1 = device only: apps get the GPS position below and nothing from Google's network location service
            Adb(serial, "shell settings put secure location_mode 1", 10000);
            string r = Adb(serial, "emu geo fix " + UsLongitude + " " + UsLatitude, 10000);
            return r.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Privacy hygiene using the switches Android exposes: no background Wi-Fi/Bluetooth scanning (these feed
        // location services), no open-network notifications, no app-crash reports sent from the device.
        // This reduces what the device volunteers about itself. It does not make it anonymous: your Google
        // account, the apps you sign in to and your network still identify you.
        public static string HardenPrivacy(string serial)
        {
            string[] cmds =
            {
                "shell settings put global wifi_scan_always_enabled 0",
                "shell settings put global ble_scan_always_enabled 0",
                "shell settings put global wifi_networks_available_notification_on 0",
                "shell settings put secure send_action_app_error 0"
            };
            foreach (string c in cmds) Adb(serial, c, 10000);
            return "background Wi-Fi/Bluetooth scanning off, network location off, error reporting off";
        }

        // Removes every app with a launcher icon except the ones in Keep, for the current user only.
        // Reversible: "cmd package install-existing <package>" or simply install it again from Google Play.
        public static int Debloat(string serial, Action<string> log)
        {
            List<string> pkgs = new List<string>();
            string o = Adb(serial, "shell \"cmd package query-activities --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER\"", 20000);
            foreach (string line in o.Split('\n'))
            {
                string l = line.Trim();
                int i = l.IndexOf('/');
                if (i <= 0) continue;
                string pkg = l.Substring(0, i);
                if (!System.Text.RegularExpressions.Regex.IsMatch(pkg, @"^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$")) continue;
                if (Array.IndexOf(Keep, pkg) < 0 && !pkgs.Contains(pkg)) pkgs.Add(pkg);
            }
            int removed = 0;
            foreach (string pkg in pkgs)
            {
                string r = Adb(serial, "shell pm uninstall --user 0 " + pkg, 30000);
                if (r.IndexOf("Success", StringComparison.OrdinalIgnoreCase) >= 0) removed++;
                else log("> could not remove " + pkg);
            }
            return removed;
        }
    }

    // Watches a folder on the PC and copies everything dropped into it onto the phone, then tells Android
    // about the new files so Photos, Files, music and video apps see them straight away.
    sealed class ShareSync : IDisposable
    {
        readonly string serial, dir;
        readonly Action<string> log;
        readonly Queue<string> queue = new Queue<string>();
        readonly HashSet<string> queued = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly AutoResetEvent signal = new AutoResetEvent(false);
        FileSystemWatcher watcher;
        Thread worker;
        volatile bool stop;

        public ShareSync(string serial, string dir, Action<string> log)
        {
            this.serial = serial; this.dir = dir.TrimEnd('\\'); this.log = log;
        }

        public void Start()
        {
            Directory.CreateDirectory(dir);
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) Enqueue(f);
            watcher = new FileSystemWatcher(dir);
            watcher.IncludeSubdirectories = true;
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
            watcher.Created += delegate(object s, FileSystemEventArgs e) { Enqueue(e.FullPath); };
            watcher.Changed += delegate(object s, FileSystemEventArgs e) { Enqueue(e.FullPath); };
            watcher.Renamed += delegate(object s, RenamedEventArgs e) { Enqueue(e.FullPath); };
            watcher.EnableRaisingEvents = true;
            worker = new Thread(Run);
            worker.IsBackground = true;
            worker.Start();
        }

        void Enqueue(string path)
        {
            try { if (Directory.Exists(path) || Path.GetFileName(path).ToLowerInvariant() == "readme.txt" && Path.GetDirectoryName(path).TrimEnd('\\').Equals(dir, StringComparison.OrdinalIgnoreCase)) return; } catch { return; }
            lock (queue) { if (queued.Add(path)) queue.Enqueue(path); }
            signal.Set();
        }

        void Run()
        {
            while (!stop)
            {
                signal.WaitOne(1000);
                while (!stop)
                {
                    string path = null;
                    lock (queue) { if (queue.Count > 0) { path = queue.Dequeue(); queued.Remove(path); } }
                    if (path == null) break;
                    try { Push(path); } catch (Exception ex) { log("> share: could not copy " + Path.GetFileName(path) + " (" + ex.Message + ")"); }
                }
            }
        }

        static string FolderFor(string file)
        {
            switch (Path.GetExtension(file).ToLowerInvariant())
            {
                case ".jpg": case ".jpeg": case ".png": case ".gif": case ".webp": case ".bmp": case ".heic": case ".heif": return "Pictures";
                case ".mp4": case ".mkv": case ".mov": case ".webm": case ".3gp": case ".avi": case ".m4v": return "Movies";
                case ".mp3": case ".m4a": case ".wav": case ".flac": case ".ogg": case ".aac": case ".opus": return "Music";
                default: return "Download";
            }
        }

        // wait until whatever is copying the file into the folder has finished
        static bool WaitUntilStable(string path)
        {
            long last = -1;
            for (int i = 0; i < 240; i++)
            {
                try
                {
                    if (!File.Exists(path)) return false;
                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (fs.Length == last) return true;
                        last = fs.Length;
                    }
                }
                catch (IOException) { }
                Thread.Sleep(500);
            }
            return false;
        }

        void Push(string path)
        {
            if (!WaitUntilStable(path)) return;
            long len = new FileInfo(path).Length;
            string rel = path.Substring(dir.Length).TrimStart('\\').Replace('\\', '/');
            string remote = "/sdcard/" + FolderFor(path) + "/Androidzy/" + rel;

            string have = Phone.Adb(serial, "shell \"stat -c %s " + Phone.Sh(remote) + "\"", 15000).Trim();
            if (have == len.ToString()) return;   // already on the phone

            string o = Phone.Adb(serial, "push \"" + path + "\" \"" + remote + "\"", 600000);
            if (o.IndexOf("pushed", StringComparison.OrdinalIgnoreCase) < 0) { log("> share: push failed for " + rel + ": " + o.Trim()); return; }
            Phone.Adb(serial, "shell \"am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d " +
                Phone.Sh("file://" + Uri.EscapeUriString(remote)) + "\"", 15000);
            log("> shared with the phone: " + rel + "  ->  " + remote.Substring("/sdcard/".Length));
        }

        public void Dispose()
        {
            stop = true;
            try { if (watcher != null) { watcher.EnableRaisingEvents = false; watcher.Dispose(); } } catch { }
            signal.Set();
        }
    }
}
