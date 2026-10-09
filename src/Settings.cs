using System;
using System.IO;

namespace Androidzy
{
    sealed partial class Settings
    {
        public string Profile = "";
        public int Res = 1;
        public int Cores = Math.Max(2, Math.Min(6, Environment.ProcessorCount / 2));
        public int RamMb = 4096;
        public bool SaveOnExit = true;
        public bool Debloat = true;
        public bool LicenseAccepted;
        public string BootFeatures = "";   // boot-time emulator features of the last launch (decides whether the snapshot can be resumed)
        public string ShareDir = "";       // share folder chosen in the app ("" = Desktop\Androidzy Share)
        public string Timezone = "";       // IANA zone for the phone, e.g. America/New_York ("" = the PC's zone)
        public string PrivateDns = "";     // DNS-over-TLS host for Android's Private DNS, e.g. one.one.one.one ("" = leave as is)
        public string HttpProxy = "";      // host:port handed to the emulator's -http-proxy ("" = direct)

        // A src\Local.cs that is not in the repository can implement this to bake in personal defaults;
        // values in Androidzy.ini still win. Without that file the call compiles away.
        partial void LocalDefaults();

        public static Settings Load()
        {
            Settings s = new Settings();
            s.LocalDefaults();
            try
            {
                if (File.Exists(Paths.SettingsFile))
                    foreach (string line in File.ReadAllLines(Paths.SettingsFile))
                    {
                        int i = line.IndexOf('=');
                        if (i <= 0) continue;
                        string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                        int n;
                        switch (k)
                        {
                            case "profile": s.Profile = v; break;
                            case "resolution": if (int.TryParse(v, out n)) s.Res = n; break;
                            case "cores": if (int.TryParse(v, out n)) s.Cores = n; break;
                            case "ram_mb": if (int.TryParse(v, out n)) s.RamMb = n; break;
                            case "save_on_exit": s.SaveOnExit = v != "0"; break;
                            case "debloat": s.Debloat = v != "0"; break;
                            case "sdk_license_accepted": s.LicenseAccepted = v == "1"; break;
                            case "boot_features": s.BootFeatures = v; break;
                            case "share_dir": s.ShareDir = v; break;
                            case "timezone": if (v.Length > 0) s.Timezone = v; break;
                            case "private_dns": if (v.Length > 0) s.PrivateDns = v; break;
                            case "http_proxy": if (v.Length > 0) s.HttpProxy = v; break;
                        }
                    }
            }
            catch { }
            if (s.Res < 0 || s.Res >= Host.Resolutions.Length) s.Res = 1;
            s.Cores = Math.Max(1, Math.Min(Environment.ProcessorCount, s.Cores));
            if (Array.IndexOf(Host.RamChoicesMb, s.RamMb) < 0) s.RamMb = 4096;
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Paths.DataRoot);
                File.WriteAllLines(Paths.SettingsFile, new string[]
                {
                    "profile=" + Profile, "resolution=" + Res, "cores=" + Cores,
                    "ram_mb=" + RamMb, "save_on_exit=" + (SaveOnExit ? "1" : "0"), "debloat=" + (Debloat ? "1" : "0"),
                    "sdk_license_accepted=" + (LicenseAccepted ? "1" : "0"),
                    "boot_features=" + BootFeatures,
                    "share_dir=" + ShareDir,
                    "timezone=" + Timezone,
                    "private_dns=" + PrivateDns,
                    "http_proxy=" + HttpProxy
                });
            }
            catch { }
        }
    }
}
