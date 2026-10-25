using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Aloha
{

    public class AlohaCgiWindow : DafyFrame
    {
        private static readonly Color Fab     = Color.FromArgb(0xFA, 0xFA, 0xFB);
        private static readonly Color Ink     = Color.FromArgb(0x20, 0x20, 0x20);
        private static readonly Color Lime    = Color.FromArgb(0x2e, 0xe0, 0x5a);
        private static readonly Color LimeHi  = Color.FromArgb(0x7d, 0xff, 0xb0);
        private static readonly Color LimeMid = Color.FromArgb(0x1e, 0x9a, 0x40);
        private static readonly Color Amber   = Color.FromArgb(0xff, 0xc0, 0x40);

        private readonly AlohaCgiServer server;
        private readonly Action<string> openInTab;

        private Panel body, canvas, gripCorner;
        private DarkScrollBar vbar;
        private CheckBox isoBox;
        private int scroll = 0, selIdx = -1;
        private string statusText = "add a folder, scan Data\\cgi, or press demo";
        private bool statusIsError;

        private const int ROW_H = 40, SB_W = 16, TOP_H = 40, STATUS_H = 34;

        public AlohaCgiWindow(AlohaCgiServer server, Action<string> openInTab)
            : base("CGI", "CGI — cgi-bin mounts")
        {
            this.server = server;
            this.openInTab = openInTab;
            Size = new Size(1440, 640);
            MinimumSize = new Size(620, 240);
            ClientArea.BackColor = Color.Black;

            BuildBody();
            BuildHeader();

            server.Changed += OnServerChanged;
            FormClosed += (s, e) => { try { server.Changed -= OnServerChanged; } catch { } };
        }

        private void BuildHeader()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = TOP_H, BackColor = Fab };

            var add = new Button { Left = 8, Top = 8, Width = 96, Text = "add folder ▸" };
            add.Click += (s, e) => AddFolder();

            var scan = new Button { Left = 108, Top = 8, Width = 110, Text = "scan Data\\cgi" };
            scan.Click += (s, e) => ScanRoot();

            var demo = new Button { Left = 222, Top = 8, Width = 66, Text = "demo ▸" };
            demo.Click += (s, e) => MakeDemo();

            isoBox = new CheckBox
            {
                Left = 296, Top = 11, Width = 152, Text = "isolated (own port)",
                ForeColor = Ink, BackColor = Color.Transparent, Checked = true
            };

            var open = new Button { Left = 456, Top = 8, Width = 70, Text = "open ▸" };
            open.Click += (s, e) => OpenSelected();

            var files = new Button { Left = 530, Top = 8, Width = 66, Text = "files ▸" };
            files.Click += (s, e) => OpenSelected("?_ls");

            var rem = new Button { Left = 600, Top = 8, Width = 70, Text = "remove" };
            rem.Click += (s, e) => RemoveSelected();

            top.Controls.Add(add); top.Controls.Add(scan); top.Controls.Add(demo);
            top.Controls.Add(isoBox); top.Controls.Add(open); top.Controls.Add(files); top.Controls.Add(rem);
            ClientArea.Controls.Add(top);
        }

        private void BuildBody()
        {
            body = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };

            canvas = new Panel { BackColor = Color.Black, Dock = DockStyle.Fill };
            typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(canvas, true, null);
            canvas.Paint += Canvas_Paint;
            canvas.MouseDown += Canvas_MouseDown;
            canvas.MouseWheel += (s, e) => { scroll = Math.Max(0, scroll - Math.Sign(e.Delta)); SyncBar(); canvas.Invalidate(); };

            vbar = new DarkScrollBar(false) { Width = SB_W, Dock = DockStyle.Fill };
            vbar.ValueChanged += (s, e) => { scroll = vbar.Value; canvas.Invalidate(); };

            gripCorner = new DoubleBufferedPanel { BackColor = Color.FromArgb(0xBB, 0xBB, 0xBB), Height = SB_W, Dock = DockStyle.Bottom };
            gripCorner.Paint += (s, e) =>
            {
                var g = e.Graphics; int w = gripCorner.Width, h = gripCorner.Height;
                using (var hi = new Pen(Color.White)) using (var lo = new Pen(Color.FromArgb(0x60, 0x60, 0x60)))
                    for (int k = 4; k <= 12; k += 4)
                    {
                        g.DrawLine(lo, w - 2, h - k - 1, w - k - 1, h - 2);
                        g.DrawLine(hi, w - 2, h - k - 2, w - k - 2, h - 2);
                    }
            };
            MakeResizeGrip(gripCorner);

            var rightCol = new Panel { Width = SB_W, Dock = DockStyle.Right, BackColor = Color.FromArgb(0xBB, 0xBB, 0xBB) };
            rightCol.Controls.Add(vbar);
            rightCol.Controls.Add(gripCorner);

            body.Controls.Add(canvas);
            body.Controls.Add(rightCol);
            ClientArea.Controls.Add(body);

            canvas.Resize += (s, e) => SyncBar();
            SyncBar();
        }

        private void LayoutGrip()
        {

        }

        private int ListHeight { get { return Math.Max(ROW_H, canvas.ClientSize.Height - STATUS_H); } }
        private int VisibleRows { get { return Math.Max(1, ListHeight / ROW_H); } }
        private int Count { get { return server.Mounts.Count; } }

        private void SyncBar()
        {
            if (vbar == null) return;
            vbar.SetValueModel(Count, VisibleRows);
            vbar.Value = scroll;
            scroll = vbar.Value;
        }

        private void OnServerChanged()
        {
            try { if (IsHandleCreated) BeginInvoke((Action)RefreshMounts); } catch { }
        }
        private void RefreshMounts() { SyncBar(); canvas.Invalidate(); }

        private void Say(string msg, bool error = false)
        { statusText = msg; statusIsError = error; canvas.Invalidate(); }

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int W = canvas.ClientSize.Width, H = canvas.ClientSize.Height;
            g.Clear(Color.Black);

            var mounts = server.Mounts;
            int listH = ListHeight;

            using (var big = new Font("Consolas", 10f, FontStyle.Bold))
            using (var ff  = new Font("Consolas", 8.25f))
            using (var lb  = new SolidBrush(Lime))
            using (var hb  = new SolidBrush(LimeHi))
            using (var mb  = new SolidBrush(LimeMid))
            using (var ab  = new SolidBrush(Amber))
            {
                if (mounts.Count == 0)
                {

                    g.DrawString("no mounts yet.", big, hb, 10, 10);
                    g.DrawString("• add folder    — serve any directory as a CGI app", ff, lb, 10, 34);
                    g.DrawString("• scan Data\\cgi  — mount every subfolder of the scripts root", ff, lb, 10, 50);
                    g.DrawString("• demo          — write a working example app and mount it", ff, lb, 10, 66);
                    g.DrawString("• files         — browse a mount's scripts instead of running index.*", ff, lb, 10, 82);
                    g.DrawString("scripts root: " + CgiRoot(), ff, mb, 10, 106);
                }
                else
                {
                    int shown = Math.Min(VisibleRows, mounts.Count - scroll);
                    for (int i = 0; i < shown; i++)
                    {
                        int ri = i + scroll;
                        if (ri < 0 || ri >= mounts.Count) break;
                        var m = mounts[ri];
                        int y = i * ROW_H;

                        if (ri == selIdx)
                            using (var sel = new SolidBrush(Color.FromArgb(0x10, 0x40, 0x20)))
                                g.FillRectangle(sel, 0, y, W, ROW_H);

                        bool bad = (m.Status ?? "").StartsWith("error");
                        g.DrawString(m.Name, big, hb, 8, y + 3);
                        g.DrawString((m.Isolated ? "[iso]" : "[shd]") + "  :" + m.Port, ff, lb, 8, y + 22);
                        g.DrawString(m.BaseUrl ?? "(not bound)", ff, bad ? ab : lb, 190, y + 3);
                        g.DrawString(m.Dir ?? "", ff, mb, 190, y + 20);
                        if (bad) g.DrawString(m.Status, ff, ab, 640, y + 12);

                        using (var line = new Pen(Color.FromArgb(0x0c, 0x30, 0x18)))
                            g.DrawLine(line, 0, y + ROW_H - 1, W, y + ROW_H - 1);
                    }
                }

                using (var band = new SolidBrush(Color.FromArgb(0x04, 0x18, 0x0c)))
                    g.FillRectangle(band, 0, listH, W, H - listH);
                using (var rule = new Pen(Color.FromArgb(0x14, 0x60, 0x2a)))
                    g.DrawLine(rule, 0, listH, W, listH);
                g.DrawString(statusText ?? "", ff, statusIsError ? ab : hb, 8, listH + 5);
                g.DrawString(mounts.Count + " mount(s)", ff, mb, 8, listH + 18);
            }
        }

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Y >= ListHeight) return;
            int ri = e.Y / ROW_H + scroll;
            var mounts = server.Mounts;
            if (ri >= 0 && ri < mounts.Count)
            {
                selIdx = ri;
                if (e.Clicks >= 2) OpenSelected();
                canvas.Invalidate();
            }
        }

        private void AddFolder()
        {
            using (var d = new FolderBrowserDialog { Description = "choose a directory to serve as a CGI app" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var m = server.AddMount(d.SelectedPath, isoBox.Checked);
                    bool bad = (m.Status ?? "").StartsWith("error");
                    Say(bad ? ("mounted with error: " + m.Status)
                            : ("mounted " + m.Name + " -> " + m.BaseUrl), bad);
                }
                catch (Exception ex) { Say("mount failed: " + ex.Message, true); }
                RefreshMounts();
            }
        }

        private void ScanRoot()
        {
            string root = CgiRoot();
            try
            {
                int n = server.AutoMountRoot(root, isoBox.Checked);
                Say(n > 0 ? ("scanned " + root + " — " + n + " new mount(s)")
                          : ("nothing new in " + root + " (put app folders there, or press demo)"));
            }
            catch (Exception ex) { Say("scan failed: " + ex.Message, true); }
            RefreshMounts();
        }

        private void MakeDemo()
        {
            try
            {
                string dir = Path.Combine(CgiRoot(), "demo");
                Directory.CreateDirectory(dir);

                var py = new StringBuilder();
                py.Append("import os, sys, html\n");
                py.Append("print('Content-Type: text/html; charset=utf-8')\n");
                py.Append("print()\n");
                py.Append("keys = ('REQUEST_METHOD','QUERY_STRING','SCRIPT_NAME','SERVER_PORT','REQUEST_URI','SERVER_SOFTWARE')\n");
                py.Append("rows = ''.join('<tr><td>%s</td><td>%s</td></tr>' % (k, html.escape(os.environ.get(k,''))) for k in keys)\n");
                py.Append("body = '''<!doctype html><meta charset=utf-8><title>aloha cgi demo</title>\n");
                py.Append("<style>body{background:#02100a;color:#2ee05a;font:14px Consolas,monospace;padding:24px}\n");
                py.Append("h1{color:#7dffb0}td{border:1px solid #14602a;padding:3px 10px}a{color:#7dffb0}</style>\n");
                py.Append("<h1>aloha-cgi is live</h1>\n");
                py.Append("<p>This page was produced by <code>index.py</code> running as cgi-bin inside Aloha.</p>\n");
                py.Append("<p>python %s</p>\n");
                py.Append("<p><a href=\"?q=norad&amp;x=1\">try a query string</a> &middot; <a href=\"hello.txt\">a static file</a></p>\n");
                py.Append("<table>%s</table>'''\n");
                py.Append("print(body % (html.escape(sys.version.split()[0]), rows))\n");

                File.WriteAllText(Path.Combine(dir, "index.py"), py.ToString(), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(dir, "hello.txt"),
                    "a static file, served straight from the mount.\r\n", new UTF8Encoding(false));

                var m = server.AddMount(dir, isoBox.Checked, "demo");
                bool bad = (m.Status ?? "").StartsWith("error");
                if (bad) Say("demo written but not bound: " + m.Status, true);
                else
                {
                    Say("demo ready at " + m.BaseUrl + "   (written to " + dir + ")");
                    try { openInTab?.Invoke(m.BaseUrl); } catch { }
                }
            }
            catch (Exception ex) { Say("demo failed: " + ex.Message, true); }
            RefreshMounts();
        }

        private void OpenSelected(string suffix = "")
        {
            var mounts = server.Mounts;
            if (selIdx < 0 || selIdx >= mounts.Count) { Say("select a mount row first", true); return; }
            var m = mounts[selIdx];
            if (string.IsNullOrEmpty(m.BaseUrl)) { Say("that mount never bound: " + m.Status, true); return; }
            string url = m.BaseUrl + suffix;
            try { openInTab?.Invoke(url); Say("opening " + url); }
            catch (Exception ex) { Say("open failed: " + ex.Message, true); }
        }

        private void RemoveSelected()
        {
            var mounts = server.Mounts;
            if (selIdx < 0 || selIdx >= mounts.Count) { Say("select a mount row first", true); return; }
            string name = mounts[selIdx].Name;
            server.RemoveMount(name);
            selIdx = -1;
            Say("removed " + name);
            RefreshMounts();
        }

        private static string CgiRoot()
        {
            string root = Path.Combine(AlohaPaths.Root, "cgi");
            try { Directory.CreateDirectory(root); } catch { }
            return root;
        }
    }
}
