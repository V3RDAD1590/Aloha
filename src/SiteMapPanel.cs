using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Aloha
{

    public class SiteMapPanel : DafyFrame
    {
        private static readonly Color Lime = Color.FromArgb(0xB4, 0xDC, 0xBA);
        private readonly Func<string[]> getPeers;
        private readonly Action<string> openInBrowser;
        private readonly ListBox list;
        private readonly List<string> rowHosts = new List<string>();

        public SiteMapPanel(Func<string[]> peers, Action<string> openCallback)
            : base("OPT-0900", "Site map")
        {
            getPeers = peers;
            openInBrowser = openCallback;

            Size = new Size(720, 480);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(90, 90);
            ShowInTaskbar = true;
            ClientArea.BackColor = Color.Black;

            list = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                ForeColor = Lime,
                Font = new Font("Consolas", 10f),
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 18
            };

            list.DrawItem += (s, e) =>
            {
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using (var bg = new SolidBrush(sel ? Color.FromArgb(0x10, 0x30, 0x18) : Color.Black))
                    e.Graphics.FillRectangle(bg, e.Bounds);
                if (e.Index >= 0 && e.Index < list.Items.Count)
                    TextRenderer.DrawText(e.Graphics, list.Items[e.Index].ToString(), list.Font,
                        e.Bounds, Lime, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            };
            list.DoubleClick += (s, e) =>
            {
                int i = list.SelectedIndex;
                if (i >= 0 && i < rowHosts.Count && !string.IsNullOrEmpty(rowHosts[i]))
                    openInBrowser?.Invoke("https://" + rowHosts[i]);
            };
            ClientArea.Controls.Add(list);

            var btnRefresh = MakeLabeledButton("Refresh", Rescan);

            var btnSave = MakeLabeledButton("Save...", Save);

            Action layoutRow = () => LayoutFooterRow(btnRefresh, btnSave);
            Footer.Resize += (s, e) => layoutRow();
            this.Shown    += (s, e) => layoutRow();
            layoutRow();

            Footer.Controls.Add(btnRefresh);
            Footer.Controls.Add(btnSave);
            btnRefresh.BringToFront(); btnSave.BringToFront();

            Shown += (s, e) => Rescan();
        }

        private void AddLine(string text, string host)
        {
            list.Items.Add(text);
            rowHosts.Add(host);
        }

        private void Rescan()
        {
            list.Items.Clear();
            rowHosts.Clear();

            string[] hosts = getPeers() ?? new string[0];
            Array.Sort(hosts, StringComparer.OrdinalIgnoreCase);

            AddLine("SITE MAP  —  hosts this page connects to (double-click to open)", null);
            AddLine("", null);
            if (hosts.Length == 0)
            {
                AddLine("(nothing yet — browse to a page, then Refresh)", null);
                return;
            }
            AddLine("resolving " + hosts.Length + " host(s)...", null);

            Task.Run(() =>
            {
                var rows = new List<KeyValuePair<string, string>>();
                foreach (var h in hosts)
                {
                    string ipText;
                    try
                    {
                        var addrs = Dns.GetHostAddresses(h);
                        var parts = new List<string>();
                        foreach (var a in addrs)
                            if (!IPAddress.IsLoopback(a)) parts.Add(a.ToString());
                        ipText = parts.Count > 0 ? string.Join(", ", parts) : "(no address)";
                    }
                    catch { ipText = "(unresolved)"; }
                    rows.Add(new KeyValuePair<string, string>(h, h + "   ->   " + ipText));
                }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        list.Items.Clear();
                        rowHosts.Clear();
                        AddLine("SITE MAP  —  hosts this page connects to (double-click to open)", null);
                        AddLine(hosts.Length + " peer host(s)  —  our own host excluded", null);
                        AddLine("", null);
                        foreach (var r in rows) AddLine(r.Value, r.Key);
                    }));
                }
                catch { }
            });
        }

        private void Save()
        {
            try
            {
                using (var sf = new SaveFileDialog { Filter = "Text|*.txt", FileName = "aloha-sitemap.txt" })
                    if (sf.ShowDialog() == DialogResult.OK)
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var it in list.Items) sb.AppendLine(it.ToString());
                        System.IO.File.WriteAllText(sf.FileName, sb.ToString());
                    }
            }
            catch { }
        }
    }
}
