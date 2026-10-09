using System;
using System.IO;
using System.Reflection;

namespace Androidzy
{
    static class Paths
    {
        public const string AvdName = "Androidzy";
        public const string ImageRel = @"system-images\android-34\google_apis_playstore\x86_64";

        public static readonly string AppDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        public static readonly string DataRoot = ResolveDataRoot();

        // The Android SDK folder in use. Set at startup by Setup.FindSdk (own copy or an existing SDK).
        public static string Sdk = Path.Combine(DataRoot, "sdk");

        public static string AvdHome { get { return Path.Combine(DataRoot, "avd"); } }
        public static string LogDir { get { return Path.Combine(DataRoot, "logs"); } }
        public static string SettingsFile { get { return Path.Combine(DataRoot, "Androidzy.ini"); } }
        public static string EmuDir { get { return Path.Combine(Sdk, "emulator"); } }
        public static string Emulator { get { return Path.Combine(EmuDir, "emulator.exe"); } }
        public static string Adb { get { return Path.Combine(Sdk, @"platform-tools\adb.exe"); } }
        public static string SystemImage { get { return Path.Combine(Sdk, ImageRel, "system.img"); } }

        // Desktop folder whose contents are copied onto the phone automatically.
        public static string ShareDir
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("ANDROIDZY_SHARE");
                if (!string.IsNullOrEmpty(env)) return env;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Androidzy Share");
            }
        }

        // present once a device has had its preinstalled apps removed; deleted on factory reset
        public static string DebloatFlag { get { return Path.Combine(AvdHome, AvdName + ".avd", "androidzy-debloated.flag"); } }

        // every executable that does GPU work for the emulator; the Windows GPU preference is set per exe
        public static string[] GpuExes
        {
            get
            {
                return new string[]
                {
                    Emulator,
                    Path.Combine(EmuDir, @"qemu\windows-x86_64\qemu-system-x86_64.exe"),
                    Path.Combine(EmuDir, @"qemu\windows-x86_64\qemu-system-x86_64-headless.exe")
                };
            }
        }

        // ANDROIDZY_HOME overrides; a "sdk" folder next to the exe makes it a portable install;
        // otherwise settings, the virtual device and downloaded components live in %LOCALAPPDATA%\Androidzy.
        static string ResolveDataRoot()
        {
            string env = Environment.GetEnvironmentVariable("ANDROIDZY_HOME");
            if (!string.IsNullOrEmpty(env)) return env;
            if (File.Exists(Path.Combine(AppDir, @"sdk\emulator\emulator.exe"))) return AppDir;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Androidzy");
        }
    }
}
