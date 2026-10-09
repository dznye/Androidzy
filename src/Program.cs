using System;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace Androidzy
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Google's servers require TLS 1.2+; older .NET defaults would fail the download.
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)(3072 | 12288); }
            catch { try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch { } }

            bool created;
            string id = Paths.DataRoot.ToLowerInvariant().GetHashCode().ToString("x");
            using (Mutex m = new Mutex(true, @"Local\Androidzy_" + id, out created))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                if (!created)
                {
                    MessageBox.Show("Androidzy is already running.", "Androidzy", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.Run(new MainForm(Options.Parse(args)));
            }
        }
    }
}
