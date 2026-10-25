using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Drawing;

namespace Aloha
{

    public class DownloadsWindow : DafyFrame
    {
        private static readonly Color Modern = Color.FromArgb(0xF0, 0xF0, 0xF0);
        private const int SB = 17;

        private readonly DownloadHistory history;
        private TextBox feed;
        private DarkScrollBar vbar, hbar;
        private Panel corner;

        public DownloadsWindow(DownloadHistory hist) : base("OPT-DL", "Downloads")
        {
            history = hist;

            Size = new Size(760, 460);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(140, 140);
            ShowInTaskbar = true;
            ClientArea.BackColor = Color.Black;

            feed = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.None, WordWrap = false,
                BackColor = Color.Black, ForeColor = Color.FromArgb(0x33, 0xFF, 0x66),
                Font = new Font("Consolas", 9.5f), BorderStyle = BorderStyle.None, Dock = DockStyle.None
            };
            vbar = new DarkScrollBar(false, light: true) { Dock = DockStyle.None };
            hbar = new DarkScrollBar(true,  light: true) { Dock = DockStyle.None };
            corner = new Panel { BackColor = Modern };
            StyleAlohaCorner(corner, true);

            var btnFolder = MakeLabeledButton("Open folder", OpenFolder);

            var btnClear = MakeLabeledButton("Clear", ClearHistory);

            var btnSave = MakeLabeledButton("Save...", Save);

            Action layoutRow = () => LayoutFooterRow(btnFolder, btnClear, btnSave);
            Footer.Resize += (s, e) => layoutRow();
            this.Shown    += (s, e) => layoutRow();
            layoutRow();

            Footer.Controls.Add(btnFolder);
            Footer.Controls.Add(btnClear);
            Footer.Controls.Add(btnSave);
            btnFolder.BringToFront(); btnClear.BringToFront(); btnSave.BringToFront();

            ClientArea.Controls.Add(feed);
            ClientArea.Controls.Add(vbar);
            ClientArea.Controls.Add(hbar);
            ClientArea.Controls.Add(corner);

            ClientArea.Resize += (s, e) => LayoutMain();
            this.Shown += (s, e) => { LayoutMain(); Render(); };
            vbar.Attach(feed);
            hbar.Attach(feed);

            history.Changed += OnChanged;
            FormClosed += (s, e) => history.Changed -= OnChanged;

            Render();
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

        private void OnChanged()
        {
            if (feed.IsDisposed) return;
            if (feed.InvokeRequired) feed.BeginInvoke((Action)Render);
            else Render();
        }

        private void Render()
        {
            var sb = new StringBuilder();
            sb.AppendLine("DOWNLOADS \u2014 history");
            sb.AppendLine("(newest last; live progress while a file is downloading)");
            sb.AppendLine();

            System.Collections.Generic.List<DownloadRecord> items;
            var live = history.Items;
            if (live == null) items = null;
            else lock (live) items = new System.Collections.Generic.List<DownloadRecord>(live);
            if (items == null || items.Count == 0)
                sb.AppendLine("(nothing downloaded yet)");
            else
                foreach (var d in items) sb.AppendLine(FormatRow(d));

            feed.Text = sb.ToString();
            feed.SelectionStart = feed.TextLength;
            feed.ScrollToCaret();
        }

        private static string FormatRow(DownloadRecord d)
        {
            string tag, why = "";
            string st = d.State ?? "";
            if (st.StartsWith("interrupted", System.StringComparison.Ordinal))
            {
                int c = st.IndexOf(':'); if (c > 0) why = "  \u2014 " + st.Substring(c + 1).Trim();   
                st = "interrupted";
            }
            switch (st)
            {
                case "completed":   tag = "[done]   "; break;
                case "interrupted": tag = "[failed] "; break;
                case "blocked":     tag = "[blocked]"; break;
                case "cancelled":   tag = "[cancel] "; break;
                default:            tag = "[" + Pct(d) + "]"; break;   
            }
            string size;
            if (d.TotalBytes > 0)       size = Human(d.ReceivedBytes) + " / " + Human(d.TotalBytes);
            else if (d.ReceivedBytes > 0) size = Human(d.ReceivedBytes);
            else                        size = "";

            string when = Pretty(string.IsNullOrEmpty(d.FinishedUtc) ? d.StartedUtc : d.FinishedUtc);
            string name = string.IsNullOrEmpty(d.FileName) ? "(file)" : d.FileName;
            return tag + "  " + name + "   " + size + "   " + Host(d.Url) + "   " + when + why;
        }

        private static string Pct(DownloadRecord d)
        {
            if (d.TotalBytes <= 0) return " .. ";
            int p = (int)(d.ReceivedBytes * 100 / d.TotalBytes);
            if (p < 0) p = 0; if (p > 100) p = 100;
            return (p < 10 ? "  " : (p < 100 ? " " : "")) + p + "%";
        }

        private static string Human(long n)
        {
            double v = n; string[] u = { "B", "KB", "MB", "GB", "TB" }; int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return v.ToString(i == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + u[i];
        }

        private static string Host(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            try { return new Uri(url).Host; } catch { return ""; }
        }

        private static string Pretty(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "";
            DateTime dt;
            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out dt))
                return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            return iso;
        }

        private void OpenFolder()
        {
            string dir = null;
            for (int i = history.Items.Count - 1; i >= 0; i--)
            {
                string p = history.Items[i].Path;
                if (string.IsNullOrEmpty(p)) continue;
                try { dir = Path.GetDirectoryName(p); } catch { dir = null; }
                if (!string.IsNullOrEmpty(dir)) break;
            }
            if (string.IsNullOrEmpty(dir))
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            try { if (Directory.Exists(dir)) Process.Start("explorer.exe", "\"" + dir + "\""); }
            catch { }
        }

        private void ClearHistory()
        {
            if (history.Items.Count == 0) return;
            var ans = MessageBox.Show(this, "Clear the download history?\r\n(Files on disk are not touched.)",
                "Aloha \u2014 downloads", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ans == DialogResult.Yes) history.Clear();
        }

        private void Save()
        {
            try
            {
                using (var sf = new SaveFileDialog { Filter = "Log|*.log|Text|*.txt", FileName = "aloha-downloads.log" })
                    if (sf.ShowDialog() == DialogResult.OK)
                        File.WriteAllText(sf.FileName, feed.Text);
            }
            catch { }
        }
    }
}
