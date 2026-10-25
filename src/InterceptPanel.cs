using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Aloha
{

    public class InterceptPanel : DafyFrame
    {
        private static readonly Color Modern = Color.FromArgb(0xF0, 0xF0, 0xF0);
        private const int SB = 17;

        private readonly NetConfig cfg;
        private readonly Action reinit;
        private readonly InterceptProxy proxy;

        private TextBox feed, portBox;
        private DarkScrollBar vbar, hbar;
        private Panel corner;
        private Label statusLbl;
        private Panel btnStart;

        public InterceptPanel(NetConfig cfg, Action reinit, InterceptProxy proxy)
            : base("OPT-0400", "Intercept proxy")
        {
            this.cfg = cfg; this.reinit = reinit; this.proxy = proxy;

            Size = new Size(860, 540);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(150, 150);
            ShowInTaskbar = true;
            ClientArea.BackColor = Color.Black;

            feed = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.None, WordWrap = false,
                BackColor = Color.Black, ForeColor = Color.FromArgb(0xB4, 0xDC, 0xBA),
                Font = new Font("Consolas", 9.5f), BorderStyle = BorderStyle.None, Dock = DockStyle.None
            };
            vbar = new DarkScrollBar(false, light: true) { Dock = DockStyle.None };
            hbar = new DarkScrollBar(true,  light: true) { Dock = DockStyle.None };
            corner = new Panel { BackColor = Modern };
            StyleAlohaCorner(corner, true);

            btnStart = MakeLabeledButton("Start", ToggleProxy);

            var portCap = new Label
            {
                Text = "Port", AutoSize = true, Left = btnStart.Right + 10, Top = 6,
                ForeColor = Color.Black, BackColor = Modern, Font = new Font("MS Sans Serif", 8f)
            };
            var portWrap = new BevelPanel { Left = portCap.Left + 30, Top = 3, Width = 56, Height = 20, BackColor = Color.White };
            portBox = new TextBox
            {
                BorderStyle = BorderStyle.None, Text = cfg.InterceptPort.ToString(), BackColor = Color.White,
                Left = 3, Top = 3, Width = 50, Font = new Font("MS Sans Serif", 8.25f)
            };
            portWrap.Controls.Add(portBox);

            var btnClear = MakeLabeledButton("Clear", () => feed.Clear());
            var btnSave = MakeLabeledButton("Save...", Save);

            statusLbl = new Label
            {
                Text = "stopped", AutoSize = true, Left = btnSave.Right + 12, Top = 6,
                ForeColor = Color.FromArgb(0x80, 0x10, 0x10), BackColor = Modern, Font = new Font("MS Sans Serif", 8f)
            };

            Action layoutRow = () => LayoutFooterRow(btnStart, portCap, portWrap, btnClear, btnSave, statusLbl);
            Footer.Resize += (s, e) => layoutRow();
            this.Shown    += (s, e) => layoutRow();
            layoutRow();

            Footer.Controls.Add(btnStart);
            Footer.Controls.Add(portCap);
            Footer.Controls.Add(portWrap);
            Footer.Controls.Add(btnClear);
            Footer.Controls.Add(btnSave);
            Footer.Controls.Add(statusLbl);
            btnStart.BringToFront(); portCap.BringToFront(); portWrap.BringToFront();
            btnClear.BringToFront(); btnSave.BringToFront(); statusLbl.BringToFront();

            ClientArea.Controls.Add(feed);
            ClientArea.Controls.Add(vbar);
            ClientArea.Controls.Add(hbar);
            ClientArea.Controls.Add(corner);

            ClientArea.Resize += (s, e) => LayoutMain();
            this.Shown += (s, e) => LayoutMain();
            vbar.Attach(feed);
            hbar.Attach(feed);

            proxy.OnTransaction += OnTx;
            FormClosed += (s, e) => { proxy.OnTransaction -= OnTx; };

            feed.AppendText("INTERCEPT PROXY — Titanium Web Proxy\r\n");
            feed.AppendText("Start routes the browser through 127.0.0.1:<port> and re-inits the engine.\r\n");
            feed.AppendText("HTTPS interception needs the proxy root cert trusted (UAC on first start).\r\n");
            feed.AppendText("Only intercept traffic you're authorised to.\r\n\r\n");

            RefreshState();
        }

        private void LayoutMain()
        {
            int cw = ClientArea.ClientSize.Width;
            int ch = ClientArea.ClientSize.Height;
            if (cw < SB + 1 || ch < SB + 1) return;
            feed.SetBounds(0, 0, cw - SB, ch - SB);
            vbar.SetBounds(cw - SB, 0, SB, ch - SB);
            hbar.SetBounds(0, ch - SB, cw - SB, SB);
            corner.SetBounds(cw - SB, ch - SB, SB, SB);
        }

        private void ToggleProxy()
        {
            if (proxy.Running) StopProxy();
            else StartProxy();
        }

        private void StartProxy()
        {
            int port;
            if (!int.TryParse(portBox.Text.Trim(), out port) || port <= 0 || port > 65535) port = 8888;
            cfg.InterceptPort = port;
            try
            {
                proxy.Start(port, cfg);
                cfg.CfgUseInterceptProxy = true;
                reinit?.Invoke();
            }
            catch (Exception ex)
            {
                cfg.CfgUseInterceptProxy = false;
                Append("!! failed to start: " + ex.Message);
            }
            RefreshState();
        }

        private void StopProxy()
        {
            cfg.CfgUseInterceptProxy = false;
            try { reinit?.Invoke(); } catch { }
            proxy.Stop();
            RefreshState();
        }

        private void RefreshState()
        {
            if (proxy.Running)
            {
                btnStart.Text = "Stop"; btnStart.Invalidate();
                statusLbl.Text = "listening on 127.0.0.1:" + proxy.Port + "   browser routed";
                statusLbl.ForeColor = Color.FromArgb(0x10, 0x70, 0x10);
            }
            else
            {
                btnStart.Text = "Start"; btnStart.Invalidate();
                statusLbl.Text = "stopped";
                statusLbl.ForeColor = Color.FromArgb(0x80, 0x10, 0x10);
            }
            portBox.Enabled = !proxy.Running;
        }

        private void OnTx(string line)
        {
            if (feed.IsDisposed) return;
            if (feed.InvokeRequired) feed.BeginInvoke((Action)(() => Append(line)));
            else Append(line);
        }

        private void Append(string line)
        {
            if (feed.TextLength > 200000) feed.Text = feed.Text.Substring(feed.TextLength - 150000);
            feed.AppendText(line + "\r\n");
            feed.SelectionStart = feed.TextLength;
            feed.ScrollToCaret();
        }

        private void Save()
        {
            try
            {
                using (var sf = new SaveFileDialog { Filter = "Log|*.log|Text|*.txt", FileName = "aloha-intercept.log" })
                    if (sf.ShowDialog() == DialogResult.OK)
                        File.WriteAllText(sf.FileName, feed.Text);
            }
            catch { }
        }
    }
}
