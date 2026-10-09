using System;

namespace Androidzy
{
    sealed class GpuProfile
    {
        public string Id;
        public string Name;
        public string Description;
        public string EmuGpu;   // value for the emulator's -gpu switch
        public int WinPref;     // Windows per-app GPU preference: 2 = high performance, 1 = power saving, 0 = no override
        public int MinCores, MinRamMb;   // floors applied on top of the chosen CPU / memory (0 = none)
        public int Vsync;               // hw.lcd.vsync to write (0 = leave as is)
        public bool SoftwareVideo;      // turn off the emulator's host ("goldfish") video decoders: apps get Android's own c2.android.* decoders
        public bool DirectNetwork;      // Wi-Fi without the netsim packet streamer: ~25 ms instead of ~110 ms per round trip
        // Emulator features fixed at boot; a snapshot taken with different ones must not be resumed.
        public string BootFeatures()
        {
            string f = "";
            if (SoftwareVideo) f += " -feature -HardwareDecoder";
            if (DirectNetwork) f += " -feature -WiFiPacketStream";
            return f.Trim();
        }
        public bool Adaptive;           // Governor: full power while Android is busy, efficiency mode when idle
        // the floor never takes more than all-but-two of the PC's logical processors
        public int Cores(int chosen) { int f = Math.Min(MinCores, Math.Max(1, Environment.ProcessorCount - 2)); return chosen > f ? chosen : f; }
        public int RamMb(int chosen) { return chosen > MinRamMb ? chosen : MinRamMb; }
        public override string ToString() { return Name; }
    }

    sealed class Res
    {
        public string Name;
        public int W, H, Dpi;
        public bool Fold;   // foldable device: adds a hinge sensor and fold postures
        public override string ToString() { return Name; }
    }

    sealed class Options
    {
        public bool Launch, Headless, Cold, Verbose, NoSave;
        public bool AcceptLicense;   // unattended setup: same as ticking "I accept" and pressing Download
        public bool OwnCopy;         // ignore an existing Android SDK, download a private copy instead
        public string Profile;
        public int ResIndex = -1;   // --res N: pick a screen option by position

        public static Options Parse(string[] a)
        {
            Options o = new Options();
            for (int i = 0; i < a.Length; i++)
            {
                switch (a[i].ToLowerInvariant())
                {
                    case "--launch": o.Launch = true; break;
                    case "--headless": o.Headless = true; break;
                    case "--cold": o.Cold = true; break;
                    case "--verbose": o.Verbose = true; break;
                    case "--no-save": o.NoSave = true; break;
                    case "--accept-license": o.AcceptLicense = true; break;
                    case "--own-copy": o.OwnCopy = true; break;
                    case "--profile": if (i + 1 < a.Length) o.Profile = a[++i]; break;
                    case "--res": if (i + 1 < a.Length) int.TryParse(a[++i], out o.ResIndex); break;
                }
            }
            return o;
        }
    }
}
