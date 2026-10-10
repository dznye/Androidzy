using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Androidzy
{
    sealed class MainForm : Form
    {
        readonly Settings st = Settings.Load();
        readonly string[] gpus = Host.DetectGpus();
        readonly Options cli;

        ComboBox cbProfile, cbRes, cbRam, cbFps;
        NumericUpDown nudCores;
        CheckBox chkCold, chkSave, chkWipe, chkDebloat;
        ShareSync share;
        Label lblSub, lblDesc, lblStatus;
        Button btnLaunch, btnStop;
        TextBox txtLog;

        // first-run setup
        GroupBox gbSetup;
        CheckBox chkLicense;
        Button btnDownload, btnCancel;
        ProgressBar prog;
        Label lblSetupStatus;
        bool sdkReady, downloading, pendingLaunch;
        volatile bool cancelDownload;

        // running emulator
        Process proc;
        int port;
        bool booted;
        string gpuInUse = "";
        GpuProfile runProfile;
        StreamWriter logWriter;
        readonly List<string> recent = new List<string>();

        public MainForm(Options o)
        {
            cli = o;
            Paths.ShareChoice = st.ShareDir;
            BuildUi();
            ResolveSdk();
            pendingLaunch = cli.Launch;
            Shown += delegate
            {
                if (sdkReady) { if (pendingLaunch) { pendingLaunch = false; Launch(); } }
                else if (cli.AcceptLicense) { chkLicense.Checked = true; StartDownload(); }
            };
        }

        // ---- layout --------------------------------------------------------------------------------

        static TableLayoutPanel MakeTable()
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill; t.AutoSize = true; t.ColumnCount = 2;
            t.Padding = new Padding(8, 4, 8, 8);
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return t;
        }

        static void AddRow(TableLayoutPanel t, string label, Control c, int height)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            Label l = new Label();
            l.Text = label; l.Dock = DockStyle.Fill; l.TextAlign = ContentAlignment.MiddleLeft;
            c.Dock = DockStyle.Fill;
            t.Controls.Add(l, 0, r);
            t.Controls.Add(c, 1, r);
        }

        static void AddFull(TableLayoutPanel t, Control c, int height)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(height > 0 ? new RowStyle(SizeType.Absolute, height) : new RowStyle(SizeType.AutoSize));
            c.Dock = DockStyle.Fill;
            t.Controls.Add(c, 0, r);
        }

        static GroupBox MakeGroup(string text, Control inner)
        {
            GroupBox g = new GroupBox();
            g.Text = text; g.Dock = DockStyle.Fill; g.AutoSize = true;
            g.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            g.Controls.Add(inner);
            return g;
        }

        static void AddRoot(TableLayoutPanel root, Control c, SizeType type)
        {
            int r = root.RowCount++;
            root.RowStyles.Add(type == SizeType.Percent ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            c.Dock = DockStyle.Fill;
            root.Controls.Add(c, 0, r);
        }

        void BuildUi()
        {
            SuspendLayout();
            Text = "Androidzy";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(620, 740);
            MinimumSize = new Size(580, 680);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill; root.ColumnCount = 1; root.Padding = new Padding(14);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // header
            Label title = new Label();
            title.Text = "Androidzy"; title.Font = new Font("Segoe UI Semibold", 17F); title.AutoSize = true;
            lblSub = new Label();
            lblSub.ForeColor = SystemColors.GrayText; lblSub.AutoSize = true; lblSub.Margin = new Padding(3, 0, 3, 0);
            LinkLabel lnkBy = new LinkLabel();
            lnkBy.Text = "Androidzy by @dznye"; lnkBy.AutoSize = true; lnkBy.Margin = new Padding(3, 2, 3, 8);
            lnkBy.LinkClicked += delegate { try { Process.Start("https://github.com/dznye"); } catch { } };
            FlowLayoutPanel head = new FlowLayoutPanel();
            head.FlowDirection = FlowDirection.TopDown; head.WrapContents = false; head.AutoSize = true;
            head.Controls.Add(title); head.Controls.Add(lblSub); head.Controls.Add(lnkBy);
            AddRoot(root, head, SizeType.AutoSize);

            // first-run setup (only visible while the Android components are missing)
            Label lblSetup = new Label();
            lblSetup.Text = "Androidzy needs Google's Android Emulator, platform tools and the Android 14 (Google Play) system image: " +
                            "about 2 GB to download and 4.5 GB on disk. They are fetched straight from Google and are licensed to you " +
                            "under the Android Software Development Kit License Agreement.";
            LinkLabel lnkTerms = new LinkLabel();
            lnkTerms.Text = "Read the license agreement"; lnkTerms.AutoSize = true;
            lnkTerms.LinkClicked += delegate { try { Process.Start(Setup.TermsUrl); } catch { } };
            chkLicense = new CheckBox();
            chkLicense.Text = "I have read and accept the license agreement"; chkLicense.AutoSize = true;
            chkLicense.Checked = st.LicenseAccepted;
            chkLicense.CheckedChanged += delegate { btnDownload.Enabled = chkLicense.Checked && !downloading; };
            btnDownload = new Button(); btnDownload.Text = "Download"; btnDownload.Size = new Size(120, 30);
            btnDownload.Enabled = chkLicense.Checked;
            btnDownload.Click += delegate { StartDownload(); };
            btnCancel = new Button(); btnCancel.Text = "Cancel"; btnCancel.Size = new Size(80, 30); btnCancel.Enabled = false;
            btnCancel.Click += delegate { cancelDownload = true; btnCancel.Enabled = false; lblSetupStatus.Text = "Cancelling..."; };
            FlowLayoutPanel dlButtons = new FlowLayoutPanel();
            dlButtons.AutoSize = true; dlButtons.Controls.Add(btnDownload); dlButtons.Controls.Add(btnCancel);
            prog = new ProgressBar(); prog.Minimum = 0; prog.Maximum = 100;
            lblSetupStatus = new Label();
            TableLayoutPanel ts = new TableLayoutPanel();
            ts.Dock = DockStyle.Fill; ts.AutoSize = true; ts.ColumnCount = 1; ts.Padding = new Padding(8, 4, 8, 8);
            ts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddFull(ts, lblSetup, 66);
            AddFull(ts, lnkTerms, 22);
            AddFull(ts, chkLicense, 26);
            AddFull(ts, dlButtons, 38);
            AddFull(ts, prog, 22);
            AddFull(ts, lblSetupStatus, 22);
            gbSetup = MakeGroup("First-run setup", ts);
            AddRoot(root, gbSetup, SizeType.AutoSize);

            // graphics
            cbProfile = new ComboBox(); cbProfile.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (GpuProfile p in Host.Profiles) cbProfile.Items.Add(p);
            lblDesc = new Label(); lblDesc.ForeColor = SystemColors.GrayText;
            Label lblGpu = new Label(); lblGpu.Text = gpus.Length > 0 ? string.Join("\r\n", gpus) : "(none found)";
            LinkLabel lnk = new LinkLabel(); lnk.Text = "Windows graphics settings"; lnk.AutoSize = true;
            lnk.LinkClicked += delegate { try { Process.Start("ms-settings:display-advanced-graphics"); } catch { } };
            TableLayoutPanel tg = MakeTable();
            AddRow(tg, "GPU profile", cbProfile, 28);
            AddRow(tg, "", lblDesc, 46);
            AddRow(tg, "Detected GPUs", lblGpu, 38);
            AddRow(tg, "", lnk, 22);
            AddRoot(root, MakeGroup("Graphics", tg), SizeType.AutoSize);

            // display / performance
            cbRes = new ComboBox(); cbRes.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (Res r in Host.Resolutions) cbRes.Items.Add(r);
            nudCores = new NumericUpDown();
            nudCores.Minimum = 1; nudCores.Maximum = Environment.ProcessorCount;
            cbRam = new ComboBox(); cbRam.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (int mb in Host.RamChoicesMb) cbRam.Items.Add((mb / 1024) + " GB");
            cbFps = new ComboBox(); cbFps.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (int f in Host.FpsChoices) cbFps.Items.Add(f == 60 ? "60 fps (smoothest scrolling)" : "30 fps (lighter: video feeds, everyday apps)");
            TableLayoutPanel tp = MakeTable();
            AddRow(tp, "Screen", cbRes, 28);
            AddRow(tp, "Frame rate", cbFps, 28);
            AddRow(tp, "CPU cores", nudCores, 28);
            AddRow(tp, "Memory", cbRam, 28);
            AddRoot(root, MakeGroup("Display and performance", tp), SizeType.AutoSize);

            // options
            chkSave = new CheckBox(); chkSave.Text = "Save state on exit (fast next start)"; chkSave.AutoSize = true;
            chkCold = new CheckBox(); chkCold.Text = "Cold boot this time"; chkCold.AutoSize = true;
            chkWipe = new CheckBox(); chkWipe.Text = "Wipe all data (factory reset)"; chkWipe.AutoSize = true;
            chkDebloat = new CheckBox(); chkDebloat.Text = "Remove preinstalled apps (keeps Play Store, Files, Settings)"; chkDebloat.AutoSize = true;
            FlowLayoutPanel opts = new FlowLayoutPanel();
            opts.AutoSize = true; opts.Margin = new Padding(3, 8, 3, 0);
            opts.Controls.Add(chkSave); opts.Controls.Add(chkCold); opts.Controls.Add(chkWipe); opts.Controls.Add(chkDebloat);
            AddRoot(root, opts, SizeType.AutoSize);

            // buttons
            btnLaunch = new Button(); btnLaunch.Text = "Launch"; btnLaunch.Size = new Size(130, 34);
            btnLaunch.Font = new Font("Segoe UI Semibold", 10F);
            btnStop = new Button(); btnStop.Text = "Stop"; btnStop.Size = new Size(90, 34); btnStop.Enabled = false;
            Button btnLogs = new Button(); btnLogs.Text = "Open logs"; btnLogs.Size = new Size(100, 34);
            Button btnShare = new Button(); btnShare.Text = "Share folder \u25BE"; btnShare.Size = new Size(120, 34);
            btnLaunch.Click += delegate { Launch(); };
            btnStop.Click += delegate { StopAsync(); };
            btnLogs.Click += delegate { try { Directory.CreateDirectory(Paths.LogDir); Process.Start(Paths.LogDir); } catch { } };
            ContextMenuStrip shareMenu = new ContextMenuStrip();
            shareMenu.Items.Add("Open share folder", null, delegate { try { EnsureShareFolder(); Process.Start(Paths.ShareDir); } catch { } });
            shareMenu.Items.Add("Move share folder...", null, delegate { ChooseShareFolder(); });
            ToolStripItem miDefault = shareMenu.Items.Add("Use the Desktop folder again", null, delegate { SetShareFolder(""); });
            shareMenu.Opening += delegate { miDefault.Enabled = st.ShareDir.Length > 0; };
            btnShare.Click += delegate { shareMenu.Show(btnShare, new Point(0, btnShare.Height)); };
            Button btnUsers = new Button(); btnUsers.Text = "Phone users \u25BE"; btnUsers.Size = new Size(125, 34);
            ContextMenuStrip usersMenu = new ContextMenuStrip();
            btnUsers.Click += delegate { FillUsersMenu(usersMenu); usersMenu.Show(btnUsers, new Point(0, btnUsers.Height)); };
            FlowLayoutPanel btns = new FlowLayoutPanel();
            btns.AutoSize = true; btns.Margin = new Padding(3, 10, 3, 4);
            btns.Controls.Add(btnLaunch); btns.Controls.Add(btnStop); btns.Controls.Add(btnShare); btns.Controls.Add(btnUsers); btns.Controls.Add(btnLogs);
            AddRoot(root, btns, SizeType.AutoSize);

            lblStatus = new Label(); lblStatus.AutoSize = true; lblStatus.Margin = new Padding(6, 4, 3, 6);
            AddRoot(root, lblStatus, SizeType.AutoSize);

            txtLog = new TextBox();
            txtLog.Multiline = true; txtLog.ReadOnly = true; txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Font = new Font("Consolas", 8.5F); txtLog.BackColor = SystemColors.Window;
            txtLog.WordWrap = false;
            AddRoot(root, txtLog, SizeType.Percent);

            Controls.Add(root);

            // restore settings
            GpuProfile sel = Host.FindProfile(cli.Profile) ?? Host.FindProfile(st.Profile);
            if (sel == null) sel = Host.FindProfile(Host.HasDiscreteGpu(gpus) ? "dedicated" : "auto");
            cbProfile.SelectedItem = sel;
            cbRes.SelectedIndex = (cli.ResIndex >= 0 && cli.ResIndex < Host.Resolutions.Length) ? cli.ResIndex : st.Res;
            nudCores.Value = st.Cores;
            cbRam.SelectedIndex = Array.IndexOf(Host.RamChoicesMb, st.RamMb);
            cbFps.SelectedIndex = Array.IndexOf(Host.FpsChoices, st.Fps);
            chkSave.Checked = st.SaveOnExit && !cli.NoSave;
            chkDebloat.Checked = st.Debloat;
            chkCold.Checked = cli.Cold;
            EnsureShareFolder();
            cbProfile.SelectedIndexChanged += delegate
            {
                ShowProfileInfo();
                // a task profile fills in its screen, cores and memory; every box can still be changed afterwards
                GpuProfile p = (GpuProfile)cbProfile.SelectedItem;
                if (!cbRes.Enabled) return;   // running: hardware cannot change until the next launch
                int r = p.SuggestW > 0 ? Host.FindRes(p.SuggestW, p.SuggestH) : -1;
                if (r >= 0) cbRes.SelectedIndex = r;
                if (p.SuggestCores > 0) nudCores.Value = Math.Max(nudCores.Minimum, Math.Min(nudCores.Maximum, p.SuggestCores));
                int m = Array.IndexOf(Host.RamChoicesMb, p.SuggestRamMb);
                if (m >= 0) cbRam.SelectedIndex = m;
                int fi = Array.IndexOf(Host.FpsChoices, p.SuggestFps);
                if (fi >= 0) cbFps.SelectedIndex = fi;
            };
            ShowProfileInfo();

            FormClosing += OnClosing;
            ResumeLayout(true);
        }

        void ShowProfileInfo()
        {
            GpuProfile p = (GpuProfile)cbProfile.SelectedItem;
            string d = p.Description;
            if (p.WinPref == 2 && !Host.HasDiscreteGpu(gpus))
                d += "  (No dedicated GPU detected - this behaves like Windows default.)";
            lblDesc.Text = d;
        }

        void SetStatus(string text) { lblStatus.Text = text; }

        // ---- phone users ---------------------------------------------------------------------------
        // Separate Android users on the one phone: each has its own app data and accounts, so the same app can be
        // signed in to a different account in each. Built fresh every time the menu opens.
        void FillUsersMenu(ContextMenuStrip menu)
        {
            menu.Items.Clear();
            if (proc == null || !booted) { menu.Items.Add("Start the phone first").Enabled = false; return; }
            string serial = "emulator-" + port;
            List<Phone.User> users; int current, max;
            Cursor = Cursors.WaitCursor;
            try { users = Phone.Users(serial); current = Phone.CurrentUser(serial); max = Phone.MaxUsers(serial); }
            finally { Cursor = Cursors.Default; }

            foreach (Phone.User u in users)
            {
                Phone.User user = u;
                ToolStripMenuItem it = new ToolStripMenuItem(u.Name + (u.Id == 0 ? "  (owner)" : ""));
                it.Checked = u.Id == current;
                it.Click += delegate { if (user.Id != current) SwitchUserAsync(serial, user); };
                menu.Items.Add(it);
            }
            menu.Items.Add(new ToolStripSeparator());
            ToolStripItem add = menu.Items.Add(users.Count >= max ? "Add user...  (Android's limit of " + max + " reached)" : "Add user...");
            add.Enabled = users.Count < max;
            add.Click += delegate { AddUser(serial); };
            ToolStripMenuItem remove = new ToolStripMenuItem("Remove user");
            foreach (Phone.User u in users)
            {
                if (u.Id == 0) continue;
                Phone.User user = u;
                remove.DropDownItems.Add(u.Name, null, delegate { RemoveUser(serial, user, current); });
            }
            remove.Enabled = remove.DropDownItems.Count > 0;
            menu.Items.Add(remove);
        }

        void AddUser(string serial)
        {
            string name = AskText("Add phone user", "Name for the new user (letters, numbers, spaces, - _ .):", "");
            if (name == null) return;
            name = name.Trim();
            if (!Regex.IsMatch(name, @"^[A-Za-z0-9 _.\-]{1,30}$")) { Error("Use 1-30 letters, numbers, spaces, - _ or ."); return; }
            bool debloat = chkDebloat.Checked;
            Action<string> say = m => PostToUi(() => AppendLog(m));
            say("> adding phone user \"" + name + "\"...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err;
                int id = Phone.CreateUser(serial, name, out err);
                if (id < 0) { say("> could not add the user: " + err); return; }
                if (debloat) Phone.Debloat(serial, delegate { }, id);
                int apps = Phone.CopyApps(serial, 0, id);
                Phone.PrepareUser(serial, id);
                say("> phone user \"" + name + "\" added (" + apps + " of your apps copied, with their own empty data)");
                PostToUi(() =>
                {
                    if (MessageBox.Show(this, "Switch to \"" + name + "\" now?\r\n\r\nThe first switch shows Android's short setup for the new user.",
                            "Phone users", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        SwitchUserAsync(serial, new Phone.User { Id = id, Name = name });
                });
            });
        }

        void SwitchUserAsync(string serial, Phone.User u)
        {
            AppendLog("> switching to phone user \"" + u.Name + "\"...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = Phone.SwitchUser(serial, u.Id);
                PostToUi(() => AppendLog(ok ? "> now using \"" + u.Name + "\"" : "> could not switch to \"" + u.Name + "\""));
            });
        }

        void RemoveUser(string serial, Phone.User u, int current)
        {
            if (MessageBox.Show(this, "Remove \"" + u.Name + "\" from the phone?\r\n\r\nThis deletes everything in that user: app data, signed-in accounts and files. It cannot be undone.",
                    "Remove phone user", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            AppendLog("> removing phone user \"" + u.Name + "\"...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                if (u.Id == current) Phone.SwitchUser(serial, 0);   // Android cannot remove the user in use
                bool ok = Phone.RemoveUser(serial, u.Id);
                PostToUi(() => AppendLog(ok ? "> removed \"" + u.Name + "\"" : "> could not remove \"" + u.Name + "\""));
            });
        }

        // A one-line text prompt (WinForms has none built in).
        string AskText(string title, string prompt, string value)
        {
            using (Form f = new Form())
            {
                f.Text = title; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MinimizeBox = f.MaximizeBox = false;
                f.StartPosition = FormStartPosition.CenterParent; f.ClientSize = new Size(380, 110); f.Font = Font;
                Label l = new Label(); l.Text = prompt; l.SetBounds(12, 12, 356, 20);
                TextBox t = new TextBox(); t.Text = value; t.SetBounds(12, 36, 356, 24);
                Button ok = new Button(); ok.Text = "OK"; ok.DialogResult = DialogResult.OK; ok.SetBounds(212, 72, 75, 28);
                Button cancel = new Button(); cancel.Text = "Cancel"; cancel.DialogResult = DialogResult.Cancel; cancel.SetBounds(293, 72, 75, 28);
                f.Controls.AddRange(new Control[] { l, t, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog(this) == DialogResult.OK ? t.Text : null;
            }
        }

        // Pick (or create, with "Make New Folder") any folder on the PC as the share folder.
        void ChooseShareFolder()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Choose or create the folder whose files are copied to the phone.";
                dlg.ShowNewFolderButton = true;
                try { dlg.SelectedPath = Paths.ShareDir; } catch { }
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedPath.Length > 0) SetShareFolder(dlg.SelectedPath);
            }
        }

        // Points the share folder somewhere else (files already in the old one stay where they are) and,
        // when the phone is running, restarts the copy-to-phone watcher on the new folder.
        void SetShareFolder(string dir)
        {
            st.ShareDir = dir; st.Save();
            Paths.ShareChoice = dir;
            EnsureShareFolder();
            AppendLog("> share folder: " + Paths.ShareDir);
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANDROIDZY_SHARE")))
                AppendLog("> note: ANDROIDZY_SHARE is set, so that folder is used instead");
            if (proc != null && share != null)
            {
                StopShare();
                ShareSync s = new ShareSync("emulator-" + port, Paths.ShareDir, m => PostToUi(() => AppendLog(m)));
                s.Start();
                share = s;
            }
        }

        // The folder that is copied to the phone automatically; created on every start if missing.
        static void EnsureShareFolder()
        {
            try
            {
                string d = Paths.ShareDir;
                if (Directory.Exists(d)) return;
                Directory.CreateDirectory(d);
                File.WriteAllText(Path.Combine(d, "README.txt"),
                    "Drop files into this folder while Androidzy is running and they are copied to the phone automatically.\r\n\r\n" +
                    "  Photos       ->  Pictures/Androidzy\r\n  Videos       ->  Movies/Androidzy\r\n" +
                    "  Music        ->  Music/Androidzy\r\n  Anything else ->  Download/Androidzy\r\n\r\n" +
                    "Open Files (or Photos, a music or video app) on the phone to find them.\r\n");
            }
            catch { }
        }

        void Error(string msg) { MessageBox.Show(this, msg, "Androidzy", MessageBoxButtons.OK, MessageBoxIcon.Error); }

        void PostToUi(Action act)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(act); } catch { }
        }

        // ---- first-run setup -----------------------------------------------------------------------

        void ResolveSdk()
        {
            string sdk = Setup.FindSdk(!cli.OwnCopy);
            if (sdk != null) Paths.Sdk = sdk;
            sdkReady = sdk != null;
            gbSetup.Visible = !sdkReady;
            lblSub.Text = sdkReady ? Host.VersionLine() : "Android 14 (API 34)  -  Google Play";
            SetRunning(false);
            SetStatus(sdkReady ? "Ready" : "Download the Android components to get started");
        }

        void StartDownload()
        {
            if (downloading) return;
            long need = 7L << 30;   // zips are deleted as we go; peak is roughly the image zip plus its unpacked size
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(Paths.DataRoot));
                long free = new DriveInfo(root).AvailableFreeSpace;
                if (free < need && MessageBox.Show(this,
                        "Only " + (free >> 30) + " GB are free on " + root + " and about 7 GB are needed while installing.\r\n\r\nContinue anyway?",
                        "Androidzy", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
            }
            catch { }

            st.LicenseAccepted = true; st.Save();
            downloading = true; cancelDownload = false;
            btnDownload.Enabled = false; chkLicense.Enabled = false; btnCancel.Enabled = true;
            prog.Value = 0;
            lblSetupStatus.Text = "Contacting Google...";
            string sdk = Path.Combine(Paths.DataRoot, "sdk");
            Thread t = new Thread(delegate() { DownloadWorker(sdk); });
            t.IsBackground = true;
            t.Start();
        }

        void DownloadWorker(string sdk)
        {
            string error = null;
            try
            {
                Directory.CreateDirectory(sdk);
                List<Component> comps = Setup.FetchCatalog(sdk);
                Setup.Install(comps, sdk, (text, pct) => PostToUi(() =>
                {
                    lblSetupStatus.Text = text;
                    prog.Value = Math.Max(0, Math.Min(100, pct));
                }), () => cancelDownload);
            }
            catch (OperationCanceledException) { error = "Cancelled."; }
            catch (Exception ex) { error = ex.Message; }
            PostToUi(() => OnDownloadDone(error));
        }

        void OnDownloadDone(string error)
        {
            downloading = false;
            chkLicense.Enabled = true; btnCancel.Enabled = false;
            btnDownload.Enabled = chkLicense.Checked;
            if (error != null)
            {
                lblSetupStatus.Text = "Stopped: " + error;
                return;
            }
            ResolveSdk();
            if (sdkReady && pendingLaunch) { pendingLaunch = false; Launch(); }
        }

        // ---- emulator ------------------------------------------------------------------------------

        void SetRunning(bool running)
        {
            btnLaunch.Enabled = !running && sdkReady;
            btnStop.Enabled = running;
            cbProfile.Enabled = cbRes.Enabled = nudCores.Enabled = cbRam.Enabled = cbFps.Enabled = !running;
            chkCold.Enabled = chkWipe.Enabled = chkSave.Enabled = chkDebloat.Enabled = !running;
        }

        void Launch()
        {
            if (proc != null || !sdkReady) return;
            if (!File.Exists(Paths.Emulator) || !File.Exists(Paths.SystemImage) || !File.Exists(Paths.Adb))
            {
                Error("The Android components were not found:\r\n  " + Paths.Emulator + "\r\n  " + Paths.SystemImage + "\r\n  " + Paths.Adb);
                return;
            }
            if (chkWipe.Checked && !cli.Launch &&
                MessageBox.Show(this, "This erases every app, account and file inside the emulator.\r\n\r\nContinue?",
                    "Wipe data", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            GpuProfile prof = (GpuProfile)cbProfile.SelectedItem;
            Res res = Host.Resolutions[cbRes.SelectedIndex];
            st.Profile = prof.Id; st.Res = cbRes.SelectedIndex; st.Cores = (int)nudCores.Value;
            st.RamMb = Host.RamChoicesMb[cbRam.SelectedIndex];
            st.Fps = Host.FpsChoices[cbFps.SelectedIndex];
            if (!cli.NoSave) st.SaveOnExit = chkSave.Checked;   // --no-save is a one-off, never persisted
            st.Debloat = chkDebloat.Checked;
            st.Save();
            // memory guard: your choice stays saved, but this launch gets a size Windows can actually spare
            long availMb = 0;
            st.RunRamMb = (st.MemoryGuard && !prof.Classic) ? Host.GuardRam(st.RamMb, out availMb) : 0;
            if (chkWipe.Checked) { try { File.Delete(Paths.DebloatFlag); } catch { } }   // a factory reset brings the apps back

            try
            {
                Host.ApplyGpuPreference(prof);
                Host.PrepareAvd(st, res, prof);
            }
            catch (Exception ex) { Error("Could not prepare the virtual device:\r\n" + ex.Message); return; }

            port = Host.FreeConsolePort();
            if (port < 0) { Error("No free emulator console port (5554-5584). Close another emulator and retry."); return; }

            StringBuilder a = new StringBuilder();
            a.AppendFormat("-avd {0} -port {1} -gpu {2} -cores {3} -memory {4}", Paths.AvdName, port, prof.EmuGpu, st.Cores, st.EffRamMb);
            a.Append(" -no-boot-anim -no-metrics -netdelay none -netspeed full -accel on");
            if (st.HttpProxy.Length > 0) a.Append(" -http-proxy " + st.HttpProxy);
            if (st.Timezone.Length > 0) a.Append(" -timezone " + st.Timezone);   // cold boots start in this zone too
            string feats = prof.BootFeatures();
            if (feats.Length > 0) a.Append(" " + feats);
            // a snapshot only resumes at the same memory size, so a size change counts as a boot feature change
            string bootKey = (feats + " mem=" + st.EffRamMb).Trim();
            bool featsSwitched = bootKey != st.BootFeatures;
            st.BootFeatures = bootKey; st.Save();
            if (chkCold.Checked || chkWipe.Checked || featsSwitched) a.Append(" -no-snapshot-load");
            if (!chkSave.Checked) a.Append(" -no-snapshot-save");
            if (chkWipe.Checked) a.Append(" -wipe-data");
            if (cli.Headless) a.Append(" -no-window");
            if (cli.Verbose) a.Append(" -verbose");

            ProcessStartInfo psi = new ProcessStartInfo(Paths.Emulator, a.ToString());
            psi.WorkingDirectory = Paths.EmuDir;
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            psi.EnvironmentVariables["ANDROID_SDK_ROOT"] = Paths.Sdk;
            psi.EnvironmentVariables["ANDROID_HOME"] = Paths.Sdk;
            psi.EnvironmentVariables["ANDROID_AVD_HOME"] = Paths.AvdHome;
            psi.EnvironmentVariables["PATH"] = Path.Combine(Paths.Sdk, "platform-tools") + ";" + psi.EnvironmentVariables["PATH"];
            // Saving the quick-boot snapshot can take a while; the default grace period is only 20 s.
            psi.EnvironmentVariables["ANDROID_EMULATOR_WAIT_TIME_BEFORE_KILL"] = "60";
            // The Windows GPU preference above orders the host GPUs, so index 0 is the GPU the profile pinned.
            // Without this the emulator scores GPUs itself and can run Vulkan on a different GPU than GLES.
            if (prof.WinPref > 0) psi.EnvironmentVariables["ANDROID_EMU_VK_SELECT_GPU"] = "0";
            else psi.EnvironmentVariables.Remove("ANDROID_EMU_VK_SELECT_GPU");

            try
            {
                Directory.CreateDirectory(Paths.LogDir);
                logWriter = new StreamWriter(Path.Combine(Paths.LogDir, "emulator-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log"));
                logWriter.AutoFlush = true;
            }
            catch { logWriter = null; }

            txtLog.Clear(); recent.Clear(); booted = false; gpuInUse = "";
            AppendLog("> emulator.exe " + a);
            AppendLog("> SDK: " + Paths.Sdk);
            AppendLog("> GPU profile: " + prof.Name + "  (Windows preference " + prof.WinPref + ")");
            if (prof.SoftwareVideo) AppendLog("> video: Android software decoders (goldfish host decoders off)");
            if (prof.DirectNetwork) AppendLog("> network: direct Wi-Fi (netsim packet streamer off)");
            if (featsSwitched) AppendLog("> boot features changed: cold boot this time");
            AppendLog("> display: " + res.W + " x " + res.H + ", locked at " + st.Fps + " fps");
            if (st.EffRamMb != st.RamMb)
                AppendLog("> memory guard: Windows has only " + (availMb / 1024.0).ToString("0.0") + " GB free, so the phone gets " + (st.EffRamMb / 1024) +
                          " GB instead of " + (st.RamMb / 1024) + " GB (a bigger phone would be swapped to disk and stutter).");
            if (availMb > 0 && !prof.Classic && availMb < Host.MinPhoneRamMb + 1536)
                AppendLog("> warning: Windows is short of memory even for a 4 GB phone (the minimum for this Android). Close browsers and other big apps, or the phone will be swapped to disk and video will stutter.");
            else if (availMb > 0)
                AppendLog("> memory: " + (st.EffRamMb / 1024) + " GB for the phone; Windows has " + (availMb / 1024.0).ToString("0.0") + " GB available" +
                          (st.MemoryGuard && !prof.Classic ? "" : " (memory guard off)"));
            runProfile = prof;

            proc = new Process();
            proc.StartInfo = psi;
            proc.EnableRaisingEvents = true;
            proc.OutputDataReceived += OnData;
            proc.ErrorDataReceived += OnData;
            proc.Exited += delegate { PostToUi(new Action(OnExited)); };
            try
            {
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                Error("Could not start the emulator:\r\n" + ex.Message);
                proc = null;
                return;
            }
            chkWipe.Checked = false; chkCold.Checked = false;
            Process watched = proc; int watchPort = port;
            Thread w = new Thread(delegate() { WatchBoot(watchPort, watched); });
            w.IsBackground = true;
            w.Start();
            SetRunning(true);
            SetStatus("Starting...  (the very first boot can take a few minutes)");
        }

        void OnData(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            string line = e.Data;
            PostToUi(() => AppendLog(line));
        }

        void AppendLog(string line)
        {
            if (logWriter != null) { try { logWriter.WriteLine(line); } catch { } }
            recent.Add(line);
            if (recent.Count > 300) recent.RemoveAt(0);
            txtLog.AppendText(line + "\r\n");
            if (txtLog.TextLength > 80000) txtLog.Text = txtLog.Text.Substring(txtLog.TextLength - 40000);
            Match m = Regex.Match(line, @"Selecting Vulkan device: (.+?), Version");
            if (m.Success) gpuInUse = m.Groups[1].Value;
            // a quick boot restores a snapshot and never prints "Boot completed"
            if (!booted && (line.IndexOf("boot completed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            line.IndexOf("Successfully loaded snapshot", StringComparison.OrdinalIgnoreCase) >= 0))
                OnBooted();
        }

        // Android is up: show it and run the per-boot steps once. Reached from a log line or from the adb poll,
        // whichever comes first (the emulator's output is buffered, so a quick-boot "ready" line can arrive late).
        void OnBooted()
        {
            if (booted || proc == null) return;
            booted = true;
            SetStatus("Running  -  emulator-" + port + (gpuInUse.Length > 0 ? "  -  GPU: " + gpuInUse : ""));
            int p = port; bool deb = chkDebloat.Checked;
            bool fold = cbRes.SelectedIndex >= 0 && Host.Resolutions[cbRes.SelectedIndex].Fold;
            Thread t = new Thread(delegate() { PostBoot(p, deb, fold); });
            t.IsBackground = true;
            t.Start();
        }

        // Asks Android itself whether it has finished booting.
        void WatchBoot(int consolePort, Process watched)
        {
            string serial = "emulator-" + consolePort;
            DateTime end = DateTime.UtcNow.AddMinutes(10);
            while (DateTime.UtcNow < end)
            {
                Thread.Sleep(3000);
                try { if (watched.HasExited) return; } catch { return; }
                string v = "";
                try { v = Phone.Adb(serial, "shell getprop sys.boot_completed", 8000).Trim(); } catch { }
                if (v == "1") { PostToUi(() => { if (proc == watched) OnBooted(); }); return; }
            }
        }

        // Runs on every boot, in the background, once Android is up.
        void PostBoot(int consolePort, bool removeApps, bool isFold)
        {
            string serial = "emulator-" + consolePort;
            Action<string> say = delegate(string m) { PostToUi(() => AppendLog(m)); };
            try
            {
                if (!Phone.WaitForBoot(serial, 180000)) { say("> post-boot steps skipped: Android did not report ready"); return; }

                say(Phone.SetUsLocation(serial) ? "> location set to the US (New York)" : "> could not set the location");
                if (st.Timezone.Length > 0) say("> time zone: " + Phone.SetTimezone(serial, st.Timezone));
                if (st.PrivateDns.Length > 0) { Phone.SetPrivateDns(serial, st.PrivateDns); say("> encrypted DNS (Private DNS): " + st.PrivateDns); }
                say(st.HttpProxy.Length > 0
                    ? "> IP: TCP goes through the proxy " + st.HttpProxy + " (UDP/QUIC does not; a VPN on the PC covers both)"
                    : "> IP: direct - sites see this PC's public IP (a VPN on the PC changes that for the phone too)");
                say("> privacy settings applied: " + Phone.HardenPrivacy(serial));
                Phone.HideSoftKeyboard(serial);
                say("> on-screen keyboard hidden (use your PC keyboard)");
                say("> device name: " + Phone.SetDeviceName(serial, isFold ? "Pixel Fold" : "Androidzy"));
                if (isFold)
                {
                    // open the real hinge (the emulator's window and Android both follow it); a quick-boot restore can
                    // leave it closed, and forcing Android's state without it leaves the window half-width
                    Phone.Adb(serial, "shell cmd device_state state reset", 10000);
                    Phone.Adb(serial, "emu sensor set hinge-angle0 180", 10000);
                    // the emulator's pixel_fold hardware overlay is what draws the camera hole; switch it off
                    Phone.Adb(serial, "shell cmd overlay disable --user 0 com.android.internal.emulation.pixel_fold", 10000);
                    say("> Pixel Fold unfolded, camera hole removed");
                }

                if (removeApps && !File.Exists(Paths.DebloatFlag))
                {
                    say("> removing preinstalled apps (keeping Play Store, Files, Settings)...");
                    int n = Phone.Debloat(serial, say);
                    try { File.WriteAllText(Paths.DebloatFlag, DateTime.Now.ToString("s")); } catch { }
                    say("> removed " + n + " apps");
                }

                ShareSync s = new ShareSync(serial, Paths.ShareDir, say);
                s.Start();
                PostToUi(() => { if (proc != null) { StopShare(); share = s; } else s.Dispose(); });
                say("> share folder ready: " + Paths.ShareDir);
            }
            catch (Exception ex) { say("> post-boot error: " + ex.Message); }
        }

        void StopShare()
        {
            if (share != null) { try { share.Dispose(); } catch { } share = null; }
        }

        void OnExited()
        {
            StopShare();
            int code = 0;
            try { code = proc.ExitCode; } catch { }
            proc = null;
            AppendLog("> emulator exited (code " + code + ")");
            if (logWriter != null) { try { logWriter.Dispose(); } catch { } logWriter = null; }
            SetRunning(false);

            string all = string.Join("\n", recent.ToArray());
            string hint = "";
            if (code != 0 && Regex.IsMatch(all, "WHPX|HAXM|AEHD|hardware acceleration|hypervisor", RegexOptions.IgnoreCase))
                hint = "Hardware acceleration is unavailable. Turn on virtualization in the BIOS and enable the Windows feature " +
                       "\"Windows Hypervisor Platform\" (optionalfeatures.exe), then restart Windows.";
            SetStatus(code == 0 ? "Stopped" : "Stopped (exit code " + code + ")");
            if (hint.Length > 0) Error(hint);
        }

        void StopAsync()
        {
            if (proc == null) return;
            btnStop.Enabled = false;
            SetStatus("Stopping...  (saving state)");
            Process p = proc; int pt = port;
            ThreadPool.QueueUserWorkItem(delegate { Host.StopEmulator(p, pt); });
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (downloading)
            {
                if (MessageBox.Show(this, "A download is in progress. Cancel it and exit?", "Androidzy",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) { e.Cancel = true; return; }
                cancelDownload = true;
            }
            if (proc == null) return;
            if (MessageBox.Show(this, "The emulator is still running. Stop it and exit?", "Androidzy",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            Cursor = Cursors.WaitCursor;
            Host.StopEmulator(proc, port);
        }
    }
}
