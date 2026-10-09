using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;

namespace Androidzy
{
    // Gives the emulator the PC's power while Android is working and hands it back when Android is idle.
    // Busy: above-normal CPU priority, Windows power throttling off, normal memory priority.
    // Idle: below-normal CPU priority, Windows efficiency mode (EcoQoS) and low memory priority, so when
    // the PC runs short of RAM Windows takes it from the idle emulator before your other apps.
    // (The emulator cannot shrink Android's RAM while it runs: its QEMU has no free-page reporting.)
    // Busy/idle is read from Android's own CPU counters, not from the emulator process, whose window
    // redraw alone keeps it at a steady baseline even on an idle home screen.
    sealed class Governor : IDisposable
    {
        const int PollMs = 1500;
        const double BusyShare = 0.12;    // Android CPU use (share of its cores) that counts as busy
        const int IdleAfterMs = 20000;    // quiet time before handing power back

        readonly string serial;
        readonly int emulatorPid;
        readonly Action<string> say;
        volatile bool stop;

        public Governor(string serial, int emulatorPid, Action<string> say)
        {
            this.serial = serial; this.emulatorPid = emulatorPid; this.say = say;
        }

        public void Start()
        {
            Thread t = new Thread(Run);
            t.IsBackground = true;
            t.Start();
        }

        public void Dispose() { stop = true; }

        void Run()
        {
            Process qemu = null;
            bool? boosted = null;
            long prevBusy = -1, prevTotal = -1;
            DateTime lastBusy = DateTime.UtcNow;
            while (!stop)
            {
                Thread.Sleep(PollMs);
                try
                {
                    if (qemu == null || qemu.HasExited) { qemu = FindQemu(emulatorPid); boosted = null; if (qemu == null) continue; }

                    long busy, total;
                    if (!ReadCpu(out busy, out total)) continue;
                    bool active = false;
                    if (prevTotal >= 0 && total > prevTotal)
                        active = (double)(busy - prevBusy) / (total - prevTotal) >= BusyShare;
                    prevBusy = busy; prevTotal = total;

                    DateTime now = DateTime.UtcNow;
                    if (active) lastBusy = now;
                    bool want = active || (now - lastBusy).TotalMilliseconds < IdleAfterMs;
                    if (boosted != want)
                    {
                        Apply(qemu, want);
                        if (boosted != null) say(want ? "> performance: boost (Android busy)" : "> performance: eco (Android idle, power handed back to Windows)");
                        boosted = want;
                    }
                }
                catch { }
            }
        }

        // First line of /proc/stat: cpu user nice system idle iowait irq softirq steal ...
        bool ReadCpu(out long busy, out long total)
        {
            busy = total = 0;
            string s = Phone.Adb(serial, "shell head -1 /proc/stat", 4000);
            string[] f = s.Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 5 || f[0] != "cpu") return false;
            for (int i = 1; i < f.Length && i <= 8; i++)
            {
                long v; if (!long.TryParse(f[i], out v)) return false;
                total += v;
                if (i != 4 && i != 5) busy += v;   // everything except idle and iowait
            }
            return true;
        }

        static Process FindQemu(int parentPid)
        {
            using (ManagementObjectSearcher q = new ManagementObjectSearcher(
                "SELECT ProcessId FROM Win32_Process WHERE ParentProcessId = " + parentPid + " AND Name LIKE 'qemu-system-%'"))
                foreach (ManagementObject o in q.Get())
                    try { return Process.GetProcessById(Convert.ToInt32(o["ProcessId"])); } catch { }
            return null;
        }

        static void Apply(Process p, bool boost)
        {
            try { p.PriorityClass = boost ? ProcessPriorityClass.AboveNormal : ProcessPriorityClass.BelowNormal; } catch { }
            try
            {
                PowerThrottling s = new PowerThrottling();
                s.Version = 1;
                s.ControlMask = ExecutionSpeed;        // we decide, not Windows
                s.StateMask = boost ? 0u : ExecutionSpeed;   // 0 = full speed, ExecutionSpeed = efficiency mode
                SetProcessInformation(p.Handle, ProcessPowerThrottlingClass, ref s, Marshal.SizeOf(typeof(PowerThrottling)));
            }
            catch { }   // Windows older than 10 1709: priority alone
            try
            {
                MemoryPriority m = new MemoryPriority();
                m.Value = boost ? MemoryPriorityNormal : MemoryPriorityLow;
                SetProcessInformation(p.Handle, ProcessMemoryPriorityClass, ref m, Marshal.SizeOf(typeof(MemoryPriority)));
            }
            catch { }
        }

        const int ProcessMemoryPriorityClass = 0;
        const int ProcessPowerThrottlingClass = 4;
        const uint ExecutionSpeed = 0x1;
        const uint MemoryPriorityLow = 2, MemoryPriorityNormal = 5;

        [StructLayout(LayoutKind.Sequential)]
        struct PowerThrottling { public uint Version, ControlMask, StateMask; }

        [StructLayout(LayoutKind.Sequential)]
        struct MemoryPriority { public uint Value; }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottling info, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetProcessInformation(IntPtr process, int infoClass, ref MemoryPriority info, int size);
    }
}
