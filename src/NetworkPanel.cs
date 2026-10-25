using System;
using System.Drawing;
using System.Windows.Forms;

namespace Aloha
{

    public class NetworkPanel : DafyFrame
    {
        private readonly NetConfig cfg;
        private readonly Action onApply;

        private ComboBox cmbDns;
        private Panel head;
        private CheckBox chkDoh;
        private TextBox txtDoh, txtRules, txtUA;
        private TextBox log;

        public NetworkPanel(NetConfig config, Action applyCallback)
            : base("OPT-NET", "Network")
        {
            cfg = config;
            onApply = applyCallback;

            Size = new Size(900, 430 + 30);
            Font = new Font("Tahoma", 8.25f);

            var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
            ClientArea.Controls.Add(body);
            head = new Panel { Dock = DockStyle.Top, BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB) };
            ClientArea.Controls.Add(head);
            head.BringToFront();

            int y = 14;
            AddLabel("DNS mode", 14, y);
            cmbDns = new ComboBox
            {
                Left = 150, Top = y - 3, Width = 160,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbDns.Items.AddRange(new object[] { "remote", "local" });
            cmbDns.SelectedItem = (cfg.DnsMode == "local") ? "local" : "remote";
            head.Controls.Add(cmbDns);
            AddHint("remote = resolve through the proxy (Tor-side, for .onion)", 150, y + 22);

            y += 50;
            chkDoh = new CheckBox { Text = "Enable DNS-over-HTTPS", Left = 14, Top = y, Width = 220, Checked = cfg.DohEnabled };
            head.Controls.Add(chkDoh);
            y += 26;
            AddLabel("DoH endpoint", 14, y);
            txtDoh = AddText(150, y, 440, cfg.DohEndpoint);
            AddHint("e.g. https://mozilla.cloudflare-dns.com/dns-query  (blank = engine default)", 150, y + 22);

            y += 52;
            AddLabel("Host-resolver rules", 14, y);
            txtRules = AddText(150, y, 440, cfg.HostResolverRules);
            AddHint("per-browser MapAddress, e.g.  MAP example.onion 127.0.0.1", 150, y + 22);

            y += 52;
            AddLabel("User-agent", 14, y);
            txtUA = AddText(150, y, 440, cfg.UserAgent);
            AddHint("blank = engine default", 150, y + 22);

            head.Height = y + 36;

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

            SetFooterExtra(MakeLabeledButton("Apply (reloads engine)", () => Apply()));

            ActivityLog.OnLog += HandleLog;
            FormClosed += (s, e) => ActivityLog.OnLog -= HandleLog;

            ActivityLog.Network("panel opened — dns=" + cmbDns.SelectedItem);
        }

        private void HandleLog(ActivityLog.Cat cat, string line)
        {
            if (cat != ActivityLog.Cat.Network && cat != ActivityLog.Cat.General) return;
            if (log.IsDisposed) return;
            if (log.InvokeRequired) { log.BeginInvoke((Action)(() => Append(line))); }
            else Append(line);
        }
        private void Append(string line) => log.AppendText(line + "\r\n");

        private void Apply()
        {
            cfg.DnsMode = cmbDns.SelectedItem?.ToString() ?? "remote";
            cfg.DohEnabled = chkDoh.Checked;
            cfg.DohEndpoint = txtDoh.Text.Trim();
            cfg.HostResolverRules = txtRules.Text.Trim();
            cfg.UserAgent = txtUA.Text.Trim();
            cfg.Save();
            ActivityLog.Network("applied — dns=" + cfg.DnsMode +
                                " doh=" + (cfg.DohEnabled ? cfg.DohEndpoint : "off"));
            if (!string.IsNullOrWhiteSpace(cfg.HostResolverRules))
                ActivityLog.Network("resolver-rules: " + cfg.HostResolverRules);
            onApply?.Invoke();
        }

        private void AddLabel(string t, int x, int y)
            => head.Controls.Add(new Label { Text = t, Left = x, Top = y, Width = 130, Height = 16, AutoSize = false });
        private void AddHint(string t, int x, int y)
            => head.Controls.Add(new Label { Text = t, Left = x, Top = y, Width = 460, ForeColor = Color.Gray, AutoSize = false });
        private TextBox AddText(int x, int y, int w, string val)
        {
            var tb = new TextBox { Left = x, Top = y - 3, Width = w, Text = val ?? "" };
            head.Controls.Add(tb); return tb;
        }
    }
}
