using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Aloha
{

    public class RawTcpPanel : DafyFrame
    {
        private static readonly Color Modern = Color.FromArgb(0xF0, 0xF0, 0xF0);
        private const int SB = 17;

        private TextBox feed, portBox;
        private DarkScrollBar vbar, hbar;
        private Panel corner;
        private Label statusLbl;
        private Panel btnStart;

        private TcpListener listener;
        private Thread worker;
        private volatile bool running;

        public RawTcpPanel() : base("OPT-0500", "Raw TCP listener")
        {
            Size = new Size(860, 540);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(180, 180);
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

            btnStart = MakeLabeledButton("Start", ToggleListen);

            var portCap = new Label
            {
                Text = "Port", AutoSize = true, Left = btnStart.Right + 10, Top = 6,
                ForeColor = Color.Black, BackColor = Modern, Font = new Font("MS Sans Serif", 8f)
            };
            var portWrap = new BevelPanel { Left = portCap.Left + 30, Top = 3, Width = 56, Height = 20, BackColor = Color.White };
            portBox = new TextBox
            {
                BorderStyle = BorderStyle.None, Text = "9999", BackColor = Color.White,
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

            ClientArea.Controls.Add(feed);
            ClientArea.Controls.Add(vbar);
            ClientArea.Controls.Add(hbar);
            ClientArea.Controls.Add(corner);

            ClientArea.Resize += (s, e) => LayoutMain();
            this.Shown += (s, e) => LayoutMain();

            vbar.Attach(feed);
            hbar.Attach(feed);

            feed.AppendText("RAW TCP LISTENER — hex dump, no parsing  (port of tcP-un.py)\r\n");
            feed.AppendText("Start binds the port and prints every byte, 16 per line.\r\n");
            feed.AppendText("Only listen on ports/interfaces you're authorised to.\r\n\r\n");

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

        private void ToggleListen()
        {
            if (running) StopListen();
            else StartListen();
        }

        private void StartListen()
        {
            int port;
            if (!int.TryParse(portBox.Text.Trim(), out port) || port <= 0 || port > 65535) port = 9999;
            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                running = true;
                worker = new Thread(Loop) { IsBackground = true };
                worker.Start();
                Emit("== listening on port " + port + " — raw mode, no parsing ==");
                Emit(new string('=', 60));
            }
            catch (Exception ex)
            {
                running = false;
                Emit("!! failed to bind " + port + ": " + ex.Message);
            }
            RefreshState();
        }

        private void StopListen()
        {
            running = false;
            try { listener?.Stop(); } catch { }
            Emit("== stopped ==");
            RefreshState();
        }

        private void Loop()
        {
            try
            {
                while (running)
                {
                    if (!listener.Pending()) { Thread.Sleep(50); continue; }
                    using (var client = listener.AcceptTcpClient())
                    {
                        var ep = client.Client.RemoteEndPoint as IPEndPoint;
                        Emit("");
                        Emit("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] from "
                             + (ep != null ? ep.Address + ":" + ep.Port : "?"));
                        Emit(new string('-', 60));

                        var ns = client.GetStream();
                        var one = new byte[1];
                        var sb = new StringBuilder();
                        int count = 0, r;
                        while (running && (r = ns.Read(one, 0, 1)) > 0)
                        {
                            sb.Append(one[0].ToString("x2")).Append(' ');
                            count++;
                            if (count % 16 == 0) { Emit(sb.ToString().TrimEnd()); sb.Clear(); }
                        }
                        if (sb.Length > 0) Emit(sb.ToString().TrimEnd());
                        Emit("[closed after " + count + " bytes]");
                    }
                }
            }
            catch (Exception ex)
            {
                if (running) Emit("!! " + ex.Message);
            }
        }

        private void RefreshState()
        {
            if (running)
            {
                statusLbl.Text = "listening";
                statusLbl.ForeColor = Color.FromArgb(0x10, 0x70, 0x10);
            }
            else
            {
                statusLbl.Text = "stopped";
                statusLbl.ForeColor = Color.FromArgb(0x80, 0x10, 0x10);
            }
            portBox.Enabled = !running;
        }

        private void Emit(string line)
        {
            if (feed.IsDisposed) return;
            if (feed.InvokeRequired) feed.BeginInvoke((Action)(() => Append(line)));
            else Append(line);
        }

        private void Append(string line)
        {
            if (feed.TextLength > 400000) feed.Text = feed.Text.Substring(feed.TextLength - 300000);
            feed.AppendText(line + "\r\n");
            feed.SelectionStart = feed.TextLength;
            feed.ScrollToCaret();
        }

        private void Save()
        {
            try
            {
                using (var sf = new SaveFileDialog { Filter = "Log|*.log|Text|*.txt", FileName = "aloha-rawtcp.log" })
                    if (sf.ShowDialog() == DialogResult.OK)
                        File.WriteAllText(sf.FileName, feed.Text);
            }
            catch { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopListen();
            base.OnFormClosed(e);
        }
    }
}
