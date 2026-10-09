namespace Androidzy
{
    sealed class GpuProfile
    {
        public string Id;
        public string Name;
        public string Description;
        public string EmuGpu;   // value for the emulator's -gpu switch
        public int WinPref;     // Windows per-app GPU preference: 2 = high performance, 1 = power saving, 0 = no override
        public override string ToString() { return Name; }
    }

    sealed class Res
    {
        public string Name;
        public int W, H, Dpi;
        public override string ToString() { return Name; }
    }

    sealed class Options
    {
        public bool Launch, Headless, Cold, Verbose, NoSave;
        public bool AcceptLicense;   // unattended setup: same as ticking "I accept" and pressing Download
        public bool OwnCopy;         // ignore an existing Android SDK, download a private copy instead
        public string Profile;

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
                }
            }
            return o;
        }
    }
}
