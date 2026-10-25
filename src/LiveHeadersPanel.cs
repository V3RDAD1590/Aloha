using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Aloha
{

    public class LiveHeadersPanel : DafyFrame
    {
        private static readonly Color Modern = Color.FromArgb(0xF0, 0xF0, 0xF0);
        private const int SB = 17;

        private TextBox feed;
        private DarkScrollBar vbar, hbar;
        private Panel corner;
        private Panel btnPause;
        private bool paused;

        public LiveHeadersPanel() : base("OPT-0300", "Live headers")
        {
            Size = new Size(820, 520);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(120, 120);
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

            btnPause = MakeLabeledButton("Pause", () =>
            {
                paused = !paused;
                btnPause.Text = paused ? "Resume" : "Pause";
                btnPause.Invalidate();
            });
            var btnClear = MakeLabeledButton("Clear", () => feed.Clear());
            var btnSave  = MakeLabeledButton("Save...", Save);

            Footer.Controls.Add(btnPause);
            Footer.Controls.Add(btnClear);
            Footer.Controls.Add(btnSave);

            Action layoutRow = () => LayoutFooterRow(btnPause, btnClear, btnSave);
            Footer.Resize += (s, e) => layoutRow();
            this.Shown    += (s, e) => layoutRow();
            layoutRow();

            ClientArea.Controls.Add(feed);
            ClientArea.Controls.Add(vbar);
            ClientArea.Controls.Add(hbar);
            ClientArea.Controls.Add(corner);

            ClientArea.Resize += (s, e) => LayoutMain();
            this.Shown += (s, e) => LayoutMain();
            vbar.Attach(feed);
            hbar.Attach(feed);

            ActivityLog.OnLog += OnActivity;
            FormClosed += (s, e) => ActivityLog.OnLog -= OnActivity;

            feed.AppendText("LIVE HEADERS — request/response + navigation\r\n");
            feed.AppendText("(streams while you browse; 'Pause' to freeze, 'Save' to write a file)\r\n\r\n");
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

        private void OnActivity(ActivityLog.Cat cat, string line)
        {
            if (paused || feed.IsDisposed) return;
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
                using (var sf = new SaveFileDialog { Filter = "Log|*.log|Text|*.txt", FileName = "aloha-headers.log" })
                    if (sf.ShowDialog() == DialogResult.OK)
                        File.WriteAllText(sf.FileName, feed.Text);
            }
            catch { }
        }
    }
}
