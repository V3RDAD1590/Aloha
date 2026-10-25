using System;
using System.Drawing;
using System.Windows.Forms;

namespace Aloha
{

    public class ProxyPanel : DafyFrame
    {

        private class CapLabel : Label
        {
            protected override void WndProc(ref Message m)
            {
                const int WM_NCHITTEST = 0x0084;
                if (m.Msg == WM_NCHITTEST) { m.Result = (IntPtr)(-1); return; }
                base.WndProc(ref m);
            }
        }

        private readonly NetConfig cfg;
        private readonly Action onApply;
        private readonly Action onClearCache;

        private ComboBox cmbScheme, cmbProfiles;
        private TextBox txtHost, txtPort, txtBypass;

        private ComboBox cmbMode, cmbSocksVer;
        private TextBox txtHttp, txtHttps, txtSocks, txtPac, txtAuthUser, txtAuthPass;

        private TextBox txtHttpPort, txtHttpsPort, txtSocksPort, txtSinglePort;
        private CheckBox chkProxyDns, chkParsePaste;
        private Panel grpSingle, grpPerScheme, grpPac;
        private Label lblAuth;
        private TextBox log;
        private Panel head;
        private Button btnTor, btnDirect;

        public ProxyPanel(NetConfig config, Action applyCallback, Action clearCacheCallback)
            : base("OPT-PRXY", "Proxy")
        {
            cfg = config;
            onApply = applyCallback;
            onClearCache = clearCacheCallback;

            Size = new Size(1120, 490);
            Font = new Font("Tahoma", 8.25f);

            var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
            ClientArea.Controls.Add(body);

            head = new Panel { Dock = DockStyle.Top, BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB) };
            ClientArea.Controls.Add(head);
            head.BringToFront();

            int y = 14;
            AddLabel("Proxy mode", 14, y);
            cmbMode = new ComboBox { Left = 130, Top = y - 3, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbMode.Items.AddRange(new object[] { "single", "perscheme", "pac", "system", "auto", "direct" });
            cmbMode.SelectedItem = Has(cfg.ProxyMode) ? cfg.ProxyMode : "single";
            cmbMode.SelectedIndexChanged += (s, e) => ApplyModeVisibility();
            head.Controls.Add(cmbMode);
            chkParsePaste = new CheckBox
            {
                Text = "parse pasted host:port into the port box",
                Left = 300, Top = y - 2, Width = 320, Checked = true, BackColor = Color.Transparent
            };
            head.Controls.Add(chkParsePaste);
            AddHint("single=one proxy · perscheme=HTTP/HTTPS/SOCKS apart · pac=script · system/auto/direct", 130, y + 20);

            y += 44;
            grpSingle = new Panel { Left = 0, Top = y, Width = 700, Height = 78, BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB) };
            head.Controls.Add(grpSingle);
            AddLabelTo(grpSingle, "Proxy scheme", 14, 4);
            cmbScheme = new ComboBox { Left = 130, Top = 1, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbScheme.Items.AddRange(new object[] { "none", "socks5h", "socks5", "http" });
            cmbScheme.SelectedItem = Has(cfg.ProxyScheme) ? cfg.ProxyScheme : "none";
            grpSingle.Controls.Add(cmbScheme);
            AddLabelTo(grpSingle, "Host", 14, 38);
            txtHost = AddTextTo(grpSingle, 130, 38, 220, cfg.ProxyHost);
            AddLabelTo(grpSingle, "Port", 360, 38, 32);
            txtSinglePort = AddTextTo(grpSingle, 396, 38, 70, cfg.ProxyPort.ToString());
            txtPort = txtSinglePort;
            WireParse(txtHost, txtSinglePort);

            grpPerScheme = new Panel { Left = 0, Top = y, Width = 900, Height = 118, BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB) };
            head.Controls.Add(grpPerScheme);

            AddLabelTo(grpPerScheme, "HTTP proxy", 14, 4);
            txtHttp = AddTextTo(grpPerScheme, 130, 4, 220, HostPart(cfg.ProxyHttp));
            AddLabelTo(grpPerScheme, "port", 360, 4, 32);
            txtHttpPort = AddTextTo(grpPerScheme, 396, 4, 70, PortPart(cfg.ProxyHttp));
            WireParse(txtHttp, txtHttpPort);

            AddLabelTo(grpPerScheme, "HTTPS proxy", 14, 38);
            txtHttps = AddTextTo(grpPerScheme, 130, 38, 220, HostPart(cfg.ProxyHttps));
            AddLabelTo(grpPerScheme, "port", 360, 38, 32);
            txtHttpsPort = AddTextTo(grpPerScheme, 396, 38, 70, PortPart(cfg.ProxyHttps));
            WireParse(txtHttps, txtHttpsPort);

            AddLabelTo(grpPerScheme, "SOCKS proxy", 14, 72);
            txtSocks = AddTextTo(grpPerScheme, 130, 72, 220, HostPart(cfg.ProxySocks));
            AddLabelTo(grpPerScheme, "port", 360, 72, 32);
            txtSocksPort = AddTextTo(grpPerScheme, 396, 72, 70, PortPart(cfg.ProxySocks));
            WireParse(txtSocks, txtSocksPort);
            cmbSocksVer = new ComboBox { Left = 480, Top = 69, Width = 60, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbSocksVer.Items.AddRange(new object[] { "5", "4" });
            cmbSocksVer.SelectedItem = Has(cfg.ProxySocksVersion) ? cfg.ProxySocksVersion : "5";
            grpPerScheme.Controls.Add(cmbSocksVer);
            AddLabelTo(grpPerScheme, " ", 548, 72, 60);

            grpPac = new Panel { Left = 0, Top = y, Width = 700, Height = 40, BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB) };
            head.Controls.Add(grpPac);
            AddLabelTo(grpPac, "PAC URL", 14, 4);
            txtPac = AddTextTo(grpPac, 130, 4, 440, cfg.ProxyPacUrl);
            AddHintTo(grpPac, "http(s):// or file:// to a proxy.pac script", 130, 26);

            y += 118 + 18;
            AddLabel("Bypass list", 14, y);
            txtBypass = AddText(130, y, 440, cfg.BypassList);
            AddHint("hosts that skip the proxy (comma-separated, * wildcards ok)", 130, y + 22);

            y += 50;
            chkProxyDns = new CheckBox
            {
                Text = "Resolve DNS through the SOCKS proxy (no local DNS leak)",
                Left = 130, Top = y, Width = 440, Checked = cfg.ProxyProxyDns,
                BackColor = Color.Transparent
            };
            head.Controls.Add(chkProxyDns);

            y += 30;
            lblAuth = new CapLabel { Text = "Proxy auth", Left = 14, Top = y, Width = 110, Height = 16, AutoSize = false };
            head.Controls.Add(lblAuth);

            head.Controls.Add(new CapLabel { Text = "user", Left = 130, Top = y + 1, Width = 32, Height = 16, AutoSize = false, ForeColor = Color.Gray });
            txtAuthUser = AddText(164, y, 150, cfg.ProxyAuthUser);
            head.Controls.Add(new CapLabel { Text = "pass", Left = 326, Top = y + 1, Width = 32, Height = 16, AutoSize = false, ForeColor = Color.Gray });
            txtAuthPass = AddText(360, y, 150, cfg.ProxyAuthPass);
            txtAuthPass.UseSystemPasswordChar = true;

            y += 40;
            AddLabel("Profile", 14, y);
            cmbProfiles = new ComboBox { Left = 130, Top = y - 3, Width = 180, DropDownStyle = ComboBoxStyle.DropDown };
            cmbProfiles.SelectedIndexChanged += (s, e) => { if (cmbProfiles.SelectedItem != null) LoadNamed(cmbProfiles.SelectedItem.ToString()); };
            head.Controls.Add(cmbProfiles);

            var btnSave = new RoundButton { Text = "Save", Left = 318, Top = y - 4, Width = 56, Height = 24 };
            btnSave.Click += (s, e) => SaveProfile();
            head.Controls.Add(btnSave);

            var btnDel = new RoundButton { Text = "Delete", Left = 378, Top = y - 4, Width = 60, Height = 24 };
            btnDel.Click += (s, e) => DeleteProfile();
            head.Controls.Add(btnDel);

            var btnFile = new RoundButton { Text = "From File", Left = 442, Top = y - 4, Width = 80, Height = 24 };
            btnFile.Click += (s, e) => LoadFromFile();
            head.Controls.Add(btnFile);

            btnTor = new RoundButton { Text = "Tor (SOCKS 9050)", Left = 526, Top = y - 4, Width = 100, Height = 24 };
            btnTor.Click += (s, e) => FillFrom(NetConfig.Default());
            head.Controls.Add(btnTor);

            btnDirect = new RoundButton { Text = "Direct", Left = 630, Top = y - 4, Width = 68, Height = 24 };
            btnDirect.Click += (s, e) => { FillFrom(NetConfig.DirectProfile()); pendingSealed = "off"; };
            head.Controls.Add(btnDirect);
            btnTor.Click += (s, e) => pendingSealed = "off";

            var btnSealed = new RoundButton { Text = "Sealed tor", Left = 702, Top = y - 4, Width = 100, Height = 24 };
            btnSealed.Click += (s, e) => SealedTorPreset();
            head.Controls.Add(btnSealed);

            y += 38;
            var btnApply = new RoundButton { Text = "Apply Settings", Left = 130, Top = y - 4, Width = 110, Height = 24 };
            btnApply.Click += (s, e) => Apply();
            head.Controls.Add(btnApply);

            var btnClear = new RoundButton { Text = "Clear Cache", Left = 246, Top = y - 4, Width = 100, Height = 24 };
            btnClear.Click += (s, e) => { onClearCache?.Invoke(); ActivityLog.Proxy("clear cache requested (whole browser)"); };
            head.Controls.Add(btnClear);

            RefreshProfiles();
            ApplyModeVisibility();

            head.Height = y + 26;

            const int SB = 17;
            log = new TextBox
            {
                Left = 0, Top = 0,
                Width = body.ClientSize.Width, Height = body.ClientSize.Height,
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                WordWrap = false, BorderStyle = BorderStyle.None,
                BackColor = Color.Black, ForeColor = Color.FromArgb(0xB4, 0xDC, 0xBA),
                Font = new Font("Consolas", 9f),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            body.Controls.Add(log);

            var vbar = new DarkScrollBar(false, true) { Dock = DockStyle.None };
            vbar.Left = body.ClientSize.Width - SB; vbar.Top = 0;
            vbar.Width = SB; vbar.Height = body.ClientSize.Height - SB;
            vbar.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            body.Controls.Add(vbar);
            vbar.AttachScrollable(log);
            vbar.BringToFront();

            var hbar = new DarkScrollBar(true, true) { Dock = DockStyle.None };
            hbar.Left = 0; hbar.Top = body.ClientSize.Height - SB;
            hbar.Width = body.ClientSize.Width - SB; hbar.Height = SB;
            hbar.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            body.Controls.Add(hbar);
            hbar.AttachScrollable(log);
            hbar.BringToFront();

            var corner = new Panel { BackColor = Color.FromArgb(0xF0, 0xF0, 0xF0) };
            corner.SetBounds(body.ClientSize.Width - SB, body.ClientSize.Height - SB, SB, SB);
            corner.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            corner.Cursor = Cursors.SizeNWSE;
            body.Controls.Add(corner);
            corner.BringToFront();
            MakeResizeGrip(corner);
            HideFooterCubes();

            ActivityLog.OnLog += HandleLog;
            FormClosed += (s, e) => ActivityLog.OnLog -= HandleLog;

            ActivityLog.Proxy("panel opened — scheme=" + cmbScheme.SelectedItem);
        }

        private void HandleLog(ActivityLog.Cat cat, string line)
        {
            if (cat != ActivityLog.Cat.Proxy && cat != ActivityLog.Cat.General) return;
            if (log.IsDisposed) return;
            if (log.InvokeRequired) { log.BeginInvoke((Action)(() => Append(line))); }
            else Append(line);
        }
        private void Append(string line)
        {
            log.AppendText(line + "\r\n");
        }

        private void FillFrom(NetConfig c)
        {
            cmbMode.SelectedItem = Has(c.ProxyMode) ? c.ProxyMode : "single";
            cmbScheme.SelectedItem = c.ProxyScheme;
            txtHost.Text = c.ProxyHost;
            txtSinglePort.Text = c.ProxyPort.ToString();
            txtHttp.Text = HostPart(c.ProxyHttp); txtHttpPort.Text = PortPart(c.ProxyHttp);
            txtHttps.Text = HostPart(c.ProxyHttps); txtHttpsPort.Text = PortPart(c.ProxyHttps);
            txtSocks.Text = HostPart(c.ProxySocks); txtSocksPort.Text = PortPart(c.ProxySocks);
            cmbSocksVer.SelectedItem = Has(c.ProxySocksVersion) ? c.ProxySocksVersion : "5";
            txtPac.Text = c.ProxyPacUrl ?? "";
            chkProxyDns.Checked = c.ProxyProxyDns;
            txtAuthUser.Text = c.ProxyAuthUser ?? "";
            txtAuthPass.Text = c.ProxyAuthPass ?? "";
            txtBypass.Text = c.BypassList;
            cmbProfiles.Text = c.ProfileName;
            ApplyModeVisibility();
            ActivityLog.Proxy("loaded profile: " + c.ProfileName);
        }

        private string pendingSealed;

        private void SealedTorPreset()
        {
            cmbMode.SelectedItem = "single";
            cmbScheme.SelectedItem = "socks5h";          
            txtHost.Text = "127.0.0.1";
            txtSinglePort.Text = NetConfig.SealedTorSocksPort.ToString();
            txtBypass.Text = "";                         
            chkProxyDns.Checked = true;
            ApplyModeVisibility();
            pendingSealed = "on";
            ActivityLog.Proxy("preset: Sealed tor \u2192 socks5h://127.0.0.1:" + txtSinglePort.Text +
                              ". Press Apply, then Start the sealed Tor in Options \u203A Sealed tor.");
        }

        private void Apply()
        {
            if (pendingSealed != null)
            {
                cfg.SealedTor = pendingSealed == "on";
                ActivityLog.Proxy("Sealed tor routing " + (cfg.SealedTor ? "ON" : "OFF"));
                pendingSealed = null;
            }
            cfg.ProxyMode = cmbMode.SelectedItem?.ToString() ?? "single";
            cfg.ProxyScheme = cmbScheme.SelectedItem?.ToString() ?? "none";
            cfg.ProxyHost = txtHost.Text.Trim();
            int.TryParse(txtSinglePort.Text.Trim(), out int p); cfg.ProxyPort = p == 0 ? 9050 : p;
            cfg.ProxyHttp = JoinHostPort(txtHttp.Text, txtHttpPort.Text);
            cfg.ProxyHttps = JoinHostPort(txtHttps.Text, txtHttpsPort.Text);
            cfg.ProxySocks = JoinHostPort(txtSocks.Text, txtSocksPort.Text);
            cfg.ProxySocksVersion = cmbSocksVer.SelectedItem?.ToString() ?? "5";
            cfg.ProxyPacUrl = txtPac.Text.Trim();
            cfg.ProxyProxyDns = chkProxyDns.Checked;
            cfg.ProxyAuthUser = txtAuthUser.Text.Trim();
            cfg.ProxyAuthPass = txtAuthPass.Text;
            cfg.BypassList = txtBypass.Text.Trim();
            cfg.ProfileName = cmbProfiles.Text.Trim();
            cfg.Save();
            ActivityLog.Proxy("applied — mode=" + cfg.ProxyMode);
            ActivityLog.Proxy("args: " + cfg.BuildBrowserArguments());
            onApply?.Invoke();
        }

        private static string ProfilesDir()
        {
            string d = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles");
            try { System.IO.Directory.CreateDirectory(d); } catch { }
            return d;
        }
        private static string ProfilePath(string name)
        {
            foreach (char ch in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            return System.IO.Path.Combine(ProfilesDir(), name + ".json");
        }

        private void RefreshProfiles()
        {
            string current = cmbProfiles.Text;
            cmbProfiles.BeginUpdate();
            cmbProfiles.Items.Clear();
            try
            {
                foreach (var f in System.IO.Directory.GetFiles(ProfilesDir(), "*.json"))
                    cmbProfiles.Items.Add(System.IO.Path.GetFileNameWithoutExtension(f));
            }
            catch { }
            cmbProfiles.EndUpdate();
            cmbProfiles.Text = string.IsNullOrEmpty(current) ? (cfg.ProfileName ?? "default") : current;
        }

        private NetConfig FormSnapshot()
        {
            var snap = new NetConfig();
            snap.CopyFrom(cfg);
            snap.ProxyMode   = cmbMode.SelectedItem?.ToString() ?? "single";
            snap.ProxyScheme = cmbScheme.SelectedItem?.ToString() ?? "none";
            snap.ProxyHost   = txtHost.Text.Trim();
            int.TryParse(txtSinglePort.Text.Trim(), out int p); snap.ProxyPort = p == 0 ? 9050 : p;
            snap.ProxyHttp   = JoinHostPort(txtHttp.Text, txtHttpPort.Text);
            snap.ProxyHttps  = JoinHostPort(txtHttps.Text, txtHttpsPort.Text);
            snap.ProxySocks  = JoinHostPort(txtSocks.Text, txtSocksPort.Text);
            snap.ProxySocksVersion = cmbSocksVer.SelectedItem?.ToString() ?? "5";
            snap.ProxyPacUrl = txtPac.Text.Trim();
            snap.ProxyProxyDns = chkProxyDns.Checked;
            snap.ProxyAuthUser = txtAuthUser.Text.Trim();
            snap.ProxyAuthPass = txtAuthPass.Text;
            snap.BypassList  = txtBypass.Text.Trim();
            snap.ProfileName = cmbProfiles.Text.Trim();
            return snap;
        }

        private void SaveProfile()
        {
            string name = cmbProfiles.Text.Trim();
            if (string.IsNullOrEmpty(name)) { ActivityLog.Proxy("save: name a profile first"); return; }
            try { FormSnapshot().Save(ProfilePath(name)); ActivityLog.Proxy("profile saved: " + name); RefreshProfiles(); cmbProfiles.Text = name; }
            catch (Exception ex) { ActivityLog.Proxy("save failed: " + ex.Message); }
        }

        private void DeleteProfile()
        {
            string name = cmbProfiles.Text.Trim();
            if (string.IsNullOrEmpty(name)) { ActivityLog.Proxy("delete: name a profile first"); return; }
            try
            {
                string path = ProfilePath(name);
                if (System.IO.File.Exists(path)) { System.IO.File.Delete(path); ActivityLog.Proxy("profile deleted: " + name); }
                else ActivityLog.Proxy("delete: no such profile: " + name);
                RefreshProfiles();
            }
            catch (Exception ex) { ActivityLog.Proxy("delete failed: " + ex.Message); }
        }

        private void LoadNamed(string name)
        {
            try { string path = ProfilePath(name); if (System.IO.File.Exists(path)) FillFrom(NetConfig.Load(path)); }
            catch (Exception ex) { ActivityLog.Proxy("load failed: " + ex.Message); }
        }

        private void LoadFromFile()
        {
            using (var ofd = new OpenFileDialog { Filter = "Aloha profile (*.json)|*.json|All files (*.*)|*.*", Title = "Load proxy profile" })
            {
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                try { FillFrom(NetConfig.Load(ofd.FileName)); ActivityLog.Proxy("profile loaded from file: " + System.IO.Path.GetFileName(ofd.FileName)); }
                catch (Exception ex) { ActivityLog.Proxy("load file failed: " + ex.Message); }
            }
        }

        private bool Has(string s) => !string.IsNullOrWhiteSpace(s);

        private static bool IsTopological(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s.Contains(":")) return false;               

            foreach (char c in s)
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-')) return true;
            return false;
        }

        private static string HostPart(string hostPort)
        {
            if (string.IsNullOrWhiteSpace(hostPort)) return "";
            string s = hostPort.Trim();
            if (IsTopological(s)) return s;
            int c = s.LastIndexOf(':');
            return c > 0 ? s.Substring(0, c) : s;
        }

        private static string PortPart(string hostPort)
        {
            if (string.IsNullOrWhiteSpace(hostPort)) return "";
            string s = hostPort.Trim();
            if (IsTopological(s)) return "";
            int c = s.LastIndexOf(':');
            return (c > 0 && c < s.Length - 1) ? s.Substring(c + 1) : "";
        }

        private static string JoinHostPort(string host, string port)
        {
            host = (host ?? "").Trim();
            port = (port ?? "").Trim();
            if (host.Length == 0) return "";
            if (IsTopological(host)) return host;
            return port.Length > 0 ? host + ":" + port : host;
        }

        private void WireParse(TextBox host, TextBox port)
        {
            host.TextChanged += (s, e) =>
            {
                if (chkParsePaste != null && !chkParsePaste.Checked) return;
                string t = host.Text;
                if (string.IsNullOrEmpty(t)) return;
                if (IsTopological(t)) return;

                int scheme = t.IndexOf("://", StringComparison.Ordinal);
                if (scheme >= 0)
                {
                    string after = t.Substring(scheme + 3);
                    int slash = after.IndexOfAny(new[] { '/', '?', '#' });
                    if (slash >= 0) after = after.Substring(0, slash);
                    t = after;
                }

                int c = t.LastIndexOf(':');
                if (c > 0 && c < t.Length - 1)
                {
                    string h = t.Substring(0, c);
                    string p = t.Substring(c + 1);

                    bool allDigits = p.Length > 0;
                    foreach (char ch in p) if (!char.IsDigit(ch)) { allDigits = false; break; }
                    if (allDigits)
                    {

                        if (host.Text != h) { host.Text = h; host.SelectionStart = h.Length; }
                        if (port != null) port.Text = p;
                    }
                }
            };
        }

        private void AddLabel(string t, int x, int y)
            => head.Controls.Add(new CapLabel { Text = t, Left = x, Top = y, Width = 110, Height = 16, AutoSize = false });
        private void AddHint(string t, int x, int y)
            => head.Controls.Add(new CapLabel { Text = t, Left = x, Top = y, Width = 440, Height = 18, ForeColor = Color.Gray, AutoSize = false });
        private TextBox AddText(int x, int y, int w, string val)
        {
            var tb = new TextBox { Left = x, Top = y - 3, Width = w, Text = val ?? "" };
            head.Controls.Add(tb); return tb;
        }

        private static void AddLabelTo(Panel p, string t, int x, int y, int w = 110)
            => p.Controls.Add(new CapLabel { Text = t, Left = x, Top = y, Width = w, Height = 16, AutoSize = false });
        private static void AddHintTo(Panel p, string t, int x, int y)
            => p.Controls.Add(new CapLabel { Text = t, Left = x, Top = y, Width = 260, Height = 18, ForeColor = Color.Gray, AutoSize = false });
        private static TextBox AddTextTo(Panel p, int x, int y, int w, string val)
        {
            var tb = new TextBox { Left = x, Top = y - 3, Width = w, Text = val ?? "" };
            p.Controls.Add(tb); return tb;
        }

        private void ApplyModeVisibility()
        {
            string m = cmbMode.SelectedItem?.ToString() ?? "single";
            if (grpSingle != null)    grpSingle.Visible    = (m == "single");
            if (grpPerScheme != null) grpPerScheme.Visible = (m == "perscheme");
            if (grpPac != null)       grpPac.Visible       = (m == "pac");

            if (m == "single"    && grpSingle != null)    grpSingle.BringToFront();
            if (m == "perscheme" && grpPerScheme != null) grpPerScheme.BringToFront();
            if (m == "pac"       && grpPac != null)       grpPac.BringToFront();

            bool proxied = (m == "single" || m == "perscheme" || m == "pac" || m == "auto" || m == "system");
            if (chkProxyDns != null) chkProxyDns.Enabled = proxied;
            if (txtAuthUser != null) txtAuthUser.Enabled = proxied;
            if (txtAuthPass != null) txtAuthPass.Enabled = proxied;
        }
    }
}
