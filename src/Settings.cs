using System;
using System.IO;

namespace Androidzy
{
    sealed class Settings
    {
        public string Profile = "";
        public int Res = 1;
        public int Cores = Math.Max(2, Math.Min(6, Environment.ProcessorCount / 2));
        public int RamMb = 4096;
        public bool SaveOnExit = true;
        public bool LicenseAccepted;

        public static Settings Load()
        {
            Settings s = new Settings();
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
                            case "sdk_license_accepted": s.LicenseAccepted = v == "1"; break;
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
                    "ram_mb=" + RamMb, "save_on_exit=" + (SaveOnExit ? "1" : "0"),
                    "sdk_license_accepted=" + (LicenseAccepted ? "1" : "0")
                });
            }
            catch { }
        }
    }
}
