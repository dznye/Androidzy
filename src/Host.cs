using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Win32;

namespace Androidzy
{
    // Everything that touches the machine: GPU detection and preference, the virtual device, ports, stopping.
    static class Host
    {
        public static readonly GpuProfile[] Profiles =
        {
            new GpuProfile { Id = "dedicated", Name = "Dedicated GPU (best performance)", EmuGpu = "host", WinPref = 2,
                Description = "Pins the emulator's renderer to your high-performance GPU (NVIDIA / AMD) and uses its native drivers. Smoothest for scrolling, video and 3D." },
            // THE VERIFIED ONE: exactly what Androidzy 1.3.2 ran when TikTok was measured at a steady 30 fps with no stalls and
            // the editor worked: full-size Pixel Fold, 60 fps, 4 cores, 8 GB, goldfish decoders off, direct Wi-Fi, and nothing
            // else (Classic = no power governor, no CPU cap, no memory guard, normal process priority). Only the boxes are
            // editable now; a launch with these values is identical to 1.3.2.
            new GpuProfile { Id = "tiktok", Name = "TikTok (smooth video)", EmuGpu = "host", WinPref = 2, SoftwareVideo = true, DirectNetwork = true, Classic = true,
                SuggestW = 2208, SuggestH = 1840, SuggestFps = 60, SuggestCores = 4, SuggestRamMb = 8192,
                Description = "The verified TikTok setup (as in Androidzy 1.3.2): dedicated GPU, video decoded by Android itself, direct Wi-Fi (about 4x lower latency). Fills in Pixel Fold, 60 fps, 4 cores, 8 GB and adds nothing else: no CPU cap, no power switching, no memory guard." },
            new GpuProfile { Id = "integrated", Name = "Integrated GPU (battery saver)", EmuGpu = "host", WinPref = 1,
                Description = "Pins the renderer to the power-saving GPU (usually Intel). Cooler, quieter and easy on the battery: a good fit for an always-on second device." },
            new GpuProfile { Id = "auto", Name = "Windows default", EmuGpu = "host", WinPref = 0,
                Description = "Hardware GPU, but Windows decides which one. Removes any override this app set earlier." },
            new GpuProfile { Id = "software", Name = "Software renderer (compatibility)", EmuGpu = "swangle", WinPref = 0,
                Description = "CPU rendering through SwiftShader/ANGLE. Slow, but works when GPU drivers misbehave." }
        };

        public static readonly Res[] Resolutions =
        {
            new Res { Name = "1280 x 720  landscape (light)",        W = 1280, H = 720,  Dpi = 160 },
            new Res { Name = "1920 x 1080  landscape (recommended)", W = 1920, H = 1080, Dpi = 240 },
            new Res { Name = "2560 x 1440  landscape (sharp)",       W = 2560, H = 1440, Dpi = 320 },
            new Res { Name = "1080 x 2400  phone portrait",          W = 1080, H = 2400, Dpi = 420 },
            new Res { Name = "Google Pixel Fold  (foldable, shows fold controls)", W = 2208, H = 1840, Dpi = 380, Fold = true }
        };

        public static int DefaultRes { get { return FindRes(2208, 1840); } }   // Google Pixel Fold

        // RAM Windows could hand out right now in MB, including what it can reclaim from caches (0 if unknown).
        public static long AvailableRamMb()
        {
            try { using (System.Diagnostics.PerformanceCounter c = new System.Diagnostics.PerformanceCounter("Memory", "Available MBytes")) return (long)c.NextValue(); }
            catch { return 0; }
        }

        // The phone's memory for this launch: the chosen size, or the largest smaller size that still leaves
        // Windows ~3.5 GB for itself and your other apps, never below the 4 GB this Android 14 image needs (the emulator
        // starts a smaller phone with 4 GB anyway). (The emulator also needs ~1 GB beyond what the phone
        // sees). When the PC runs short the emulator is swapped to disk and video stutters, whatever the settings.
        public static int GuardRam(int chosenMb, out long availMb)
        {
            availMb = AvailableRamMb();
            if (availMb <= 0) return chosenMb;
            long allowed = availMb - 3584;
            int pick = MinPhoneRamMb;
            foreach (int mb in RamChoicesMb) if (mb >= MinPhoneRamMb && mb <= chosenMb && mb <= allowed) pick = mb;
            return pick;
        }

        // Physical memory of this PC in MB (0 if Windows will not say).
        public static long TotalRamMb()
        {
            try
            {
                using (ManagementObjectSearcher q = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                    foreach (ManagementObject o in q.Get()) return Convert.ToInt64(o["TotalPhysicalMemory"]) / (1024 * 1024);
            }
            catch { }
            return 0;
        }

        // Position of the screen with these pixels, or -1.
        public static int FindRes(int w, int h)
        {
            for (int i = 0; i < Resolutions.Length; i++) if (Resolutions[i].W == w && Resolutions[i].H == h) return i;
            return -1;
        }

        public static readonly int[] RamChoicesMb = { 2048, 3072, 4096, 6144, 8192 };
        public static readonly int[] FpsChoices = { 60, 30 };
        public const int MinPhoneRamMb = 4096;   // the emulator raises anything smaller to 4 GB for this image

        // Written once when the virtual device is created; hardware keys are updated on every launch.
        static readonly string[] BaseConfig =
        {
            "AvdId=" + Paths.AvdName,
            "PlayStore.enabled=true",
            "abi.type=x86_64",
            "avd.ini.displayname=" + Paths.AvdName,
            "avd.ini.encoding=UTF-8",
            "disk.dataPartition.size=8G",
            "fastboot.chosenSnapshotFile=",
            "fastboot.forceChosenSnapshotBoot=no",
            "fastboot.forceColdBoot=no",
            "fastboot.forceFastBoot=yes",
            "hw.accelerometer=yes",
            "hw.arc=false",
            "hw.audioInput=yes",
            "hw.audioOutput=yes",
            "hw.battery=yes",
            "hw.camera.back=none",
            "hw.camera.front=none",
            "hw.cpu.arch=x86_64",
            "hw.dPad=no",
            "hw.device.manufacturer=Google",
            "hw.gltransport=asg",
            "hw.gps=yes",
            "hw.gyroscope=yes",
            "hw.keyboard=yes",
            "hw.mainKeys=no",
            "hw.sdCard=no",
            "hw.sensors.orientation=yes",
            "hw.sensors.proximity=yes",
            "hw.trackBall=no",
            "image.sysdir.1=" + Paths.ImageRel + "\\",
            "runtime.network.latency=none",
            "runtime.network.speed=full",
            "showDeviceFrame=no",
            "skin.dynamic=yes",
            "tag.display=Google Play",
            "tag.id=google_apis_playstore",
            "vm.heapSize=512"
        };

        public static GpuProfile FindProfile(string id)
        {
            foreach (GpuProfile p in Profiles) if (p.Id == id) return p;
            return null;
        }

        public static string[] DetectGpus()
        {
            List<string> names = new List<string>();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                    foreach (ManagementBaseObject o in s.Get())
                        if (o["Name"] != null) names.Add(o["Name"].ToString());
            }
            catch { }
            return names.ToArray();
        }

        public static bool HasDiscreteGpu(string[] gpus)
        {
            foreach (string g in gpus)
            {
                string n = g.ToLowerInvariant();
                if (n.Contains("nvidia") || n.Contains("radeon") || n.Contains("amd ") || n.Contains("arc ")) return true;
            }
            return false;
        }

        public static string VersionLine()
        {
            string rev = "?";
            try
            {
                foreach (string l in File.ReadAllLines(Path.Combine(Paths.EmuDir, "source.properties")))
                    if (l.StartsWith("Pkg.Revision=")) rev = l.Substring("Pkg.Revision=".Length).Trim();
            }
            catch { }
            return "Android 14 (API 34)  -  Google Play  -  emulator " + rev;
        }

        // Same switch as Settings > System > Display > Graphics > "Choose a GPU for this app".
        public static void ApplyGpuPreference(GpuProfile p)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences"))
            {
                foreach (string exe in Paths.GpuExes)
                {
                    if (p.WinPref > 0) k.SetValue(exe, "GpuPreference=" + p.WinPref + ";", RegistryValueKind.String);
                    else if (k.GetValue(exe) != null) k.DeleteValue(exe, false);
                }
            }
        }

        static void SetKey(List<string> lines, string key, string val)
        {
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].StartsWith(key + "=")) { lines[i] = key + "=" + val; return; }
            lines.Add(key + "=" + val);
        }

        static void RemoveKeys(List<string> lines, string prefix)
        {
            lines.RemoveAll(delegate(string l) { return l.StartsWith(prefix); });
        }

        // Pixel Fold: inner display 2208x1840 with a vertical hinge in the middle and closed / half-open / open
        // postures; the emulator then offers its fold controls (Extended controls > Virtual sensors > Device pose).
        static void ApplyFold(List<string> lines, Res r)
        {
            RemoveKeys(lines, "hw.sensor.hinge");
            RemoveKeys(lines, "hw.sensor.posture_list");
            RemoveKeys(lines, "hw.displayRegion");
            SetKey(lines, "hw.device.name", "pixel_fold");
            SetKey(lines, "hw.sensor.hinge", "yes");
            SetKey(lines, "hw.sensor.hinge.count", "1");
            SetKey(lines, "hw.sensor.hinge.type", "1");        // vertical
            SetKey(lines, "hw.sensor.hinge.sub_type", "1");    // visible hinge
            SetKey(lines, "hw.sensor.hinge.ranges", "0-180");
            SetKey(lines, "hw.sensor.hinge.defaults", "180");
            SetKey(lines, "hw.sensor.hinge.areas", (r.W / 2) + "-0-0-" + r.H);
            SetKey(lines, "hw.sensor.posture_list", "1,2,3");   // closed, half-open, open
            SetKey(lines, "hw.sensor.hinge_angles_posture_definitions", "0-30, 30-150, 150-180");
            SetKey(lines, "hw.sensor.hinge.fold_to_displayRegion.0.1_at_posture", "1");
            SetKey(lines, "hw.displayRegion.0.1.xOffset", "0");
            SetKey(lines, "hw.displayRegion.0.1.yOffset", "0");
            SetKey(lines, "hw.displayRegion.0.1.width", (r.W / 2).ToString());
            SetKey(lines, "hw.displayRegion.0.1.height", r.H.ToString());
        }

        static void RemoveFold(List<string> lines)
        {
            RemoveKeys(lines, "hw.sensor.hinge");
            RemoveKeys(lines, "hw.sensor.posture_list");
            RemoveKeys(lines, "hw.displayRegion");
            // left behind, the pixel_fold device definition crashes the emulator at start with a non-fold screen
            RemoveKeys(lines, "hw.device.name");
        }

        // Creates the virtual device on first use, then applies the per-run hardware settings.
        // The AVD pointer is rewritten every time so the data folder can be moved.
        public static void PrepareAvd(Settings s, Res r, GpuProfile p)
        {
            string avdDir = Path.Combine(Paths.AvdHome, Paths.AvdName + ".avd");
            Directory.CreateDirectory(avdDir);
            File.WriteAllText(Path.Combine(Paths.AvdHome, Paths.AvdName + ".ini"),
                "avd.ini.encoding=UTF-8\r\npath=" + avdDir + "\r\npath.rel=" + Paths.AvdName + ".avd\r\ntarget=android-34\r\n",
                Encoding.ASCII);

            string cfg = Path.Combine(avdDir, "config.ini");
            List<string> lines = File.Exists(cfg) ? new List<string>(File.ReadAllLines(cfg)) : new List<string>(BaseConfig);
            SetKey(lines, "hw.lcd.width", r.W.ToString());
            SetKey(lines, "hw.lcd.height", r.H.ToString());
            SetKey(lines, "hw.lcd.density", r.Dpi.ToString());
            SetKey(lines, "hw.initialOrientation", r.W >= r.H ? "landscape" : "portrait");
            SetKey(lines, "hw.cpu.ncore", s.Cores.ToString());
            SetKey(lines, "hw.ramSize", s.EffRamMb.ToString());
            SetKey(lines, "hw.lcd.vsync", s.Fps.ToString());   // the display's refresh rate: Android cannot go above it
            SetKey(lines, "hw.gpu.enabled", "yes");
            SetKey(lines, "hw.gpu.mode", p.EmuGpu);
            if (r.Fold) ApplyFold(lines, r); else RemoveFold(lines);
            File.WriteAllLines(cfg, lines.ToArray(), Encoding.ASCII);
        }

        public static int FreeConsolePort()
        {
            for (int port = 5554; port <= 5584; port += 2)
            {
                try
                {
                    TcpListener a = new TcpListener(IPAddress.Loopback, port);
                    TcpListener b = new TcpListener(IPAddress.Loopback, port + 1);
                    a.Start(); b.Start(); a.Stop(); b.Stop();
                    return port;
                }
                catch { }
            }
            return -1;
        }

        public static void KillTree(int pid)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("taskkill", "/PID " + pid + " /T /F");
                psi.CreateNoWindow = true; psi.UseShellExecute = false;
                Process.Start(psi).WaitForExit(10000);
            }
            catch { }
        }

        // Ask the emulator to exit itself (lets it save the quick-boot snapshot); kill it if it does not.
        // (emulator.exe also leaves a detached guard process behind that idles for the shutdown grace period;
        // it is harmless and not tied to this Process.)
        public static void StopEmulator(Process p, int port)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Paths.Adb, "-s emulator-" + port + " emu kill");
                psi.CreateNoWindow = true; psi.UseShellExecute = false;
                Process a = Process.Start(psi);
                a.WaitForExit(8000);
            }
            catch { }
            try { if (!p.WaitForExit(90000)) KillTree(p.Id); } catch { }
        }
    }
}
