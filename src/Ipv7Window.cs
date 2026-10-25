using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Aloha
{

    public class Ipv7Window : DafyFrame
    {
        public const string FolderName = "ipv7";
        private static readonly string[] ClientExes = { "IPv7Client.exe", "IPv7Host.exe" };
        private static readonly Color Ink = Color.FromArgb(0xE6, 0xE6, 0xE6);
        private static readonly Color Dim = Color.FromArgb(0x8A, 0x8A, 0x8A);
        private const int BAR_ROW = 26, SITES_H = 96;

        private readonly NetConfig cfg;
        private readonly Action<string> openInTab;
        private TextBox readout;
        private ListBox sitesBox;
        private Panel btnRefresh, btnDoor, btnStart, btnOpen, btnFolder;
        private System.Windows.Forms.Timer poll;
        private Status last;
        private bool polling;

        [DataContract] public class Site
        {
            [DataMember(Name = "name")] public string Name;
            [DataMember(Name = "url")]  public string Url;
        }
        [DataContract] public class Session
        {
            [DataMember(Name = "host")]        public string Host;
            [DataMember(Name = "fingerprint")] public string Fingerprint;
            [DataMember(Name = "address")]     public string Address;
            [DataMember(Name = "key")]         public string Key;
            [DataMember(Name = "carrier")]     public string Carrier;
            [DataMember(Name = "bound")]       public bool   Bound;
            [DataMember(Name = "resumed")]     public bool   Resumed;
            [DataMember(Name = "uptime")]      public int    Uptime;
        }
        [DataContract] public class Status
        {
            [DataMember(Name = "version")]    public int    Version;
            [DataMember(Name = "gateway")]    public string Gateway;
            [DataMember(Name = "url")]        public string Url;
            [DataMember(Name = "uptime")]     public int    Uptime;
            [DataMember(Name = "requests")]   public int    Requests;
            [DataMember(Name = "served")]     public int    Served;
            [DataMember(Name = "failed")]     public int    Failed;
            [DataMember(Name = "refused")]    public int    Refused;
            [DataMember(Name = "redirected")] public int    Redirected;
            [DataMember(Name = "sites")]      public List<Site> Sites;
            [DataMember(Name = "session")]    public Session Session;
            public string Error;
        }

        public static int Port(NetConfig c) { return c != null && c.Ipv7GatewayPort > 0 ? c.Ipv7GatewayPort : 8888; }
        public static string DoorUrl(NetConfig c) { return "http://localhost:" + Port(c) + "/"; }

        public static string GatewayUrl(NetConfig c, string typed)
        {
            string rest = (typed ?? "").Trim();
            if (rest.StartsWith("ipv7://", StringComparison.OrdinalIgnoreCase)) rest = rest.Substring(7);
            else if (rest.StartsWith("ipv7:", StringComparison.OrdinalIgnoreCase)) rest = rest.Substring(5);
            rest = rest.TrimStart('/');
            if (rest.Length == 0) return DoorUrl(c);
            int slash = rest.IndexOf('/');
            string name = slash >= 0 ? rest.Substring(0, slash) : rest;
            string path = slash >= 0 ? rest.Substring(slash) : "/";
            return DoorUrl(c) + "IPv7:" + Uri.EscapeDataString(name) + path;
        }

        public static Task<Status> ReadStatusAsync(NetConfig c)
        {
            return Task.Run(() =>
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + Port(c) + "/IPv7/status.json");
                    req.Host = "localhost:" + Port(c);          
                    req.Proxy = null;
                    req.Timeout = 1500; req.ReadWriteTimeout = 1500;
                    req.UserAgent = "Aloha/" + Form1.VERSION;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var s = resp.GetResponseStream())
                    {
                        var ser = new DataContractJsonSerializer(typeof(Status));
                        var st = (Status)ser.ReadObject(s);
                        if (st.Sites == null) st.Sites = new List<Site>();
                        return st;
                    }
                }
                catch (Exception ex)
                {
                    return new Status { Sites = new List<Site>(), Error = ex is WebException ? "gateway not running" : ex.Message };
                }
            });
        }

        public static string StatusLine(Status st)
        {
            if (st == null || st.Error != null) return (st != null ? st.Error : "?") + " — start the IPv7 client";
            var s = st.Session;
            if (s == null || !s.Bound) return "gateway up, no session";
            return (s.Resumed ? "resumed" : "bound") + " · " + s.Address + " · " + st.Sites.Count + " site" + (st.Sites.Count == 1 ? "" : "s")
                 + " · " + st.Served + " page" + (st.Served == 1 ? "" : "s");
        }

        public static string FindClient(NetConfig c)
        {
            try
            {
                if (c != null && !string.IsNullOrWhiteSpace(c.Ipv7ClientExe) && File.Exists(c.Ipv7ClientExe)) return c.Ipv7ClientExe;
                foreach (var dir in new[] { Path.GetDirectoryName(Application.ExecutablePath), Environment.CurrentDirectory, AlohaPaths.Root })
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    foreach (var name in ClientExes)
                    {
                        string p = Path.Combine(dir, FolderName, name);
                        if (File.Exists(p)) return p;
                    }
                    string py = Path.Combine(dir, FolderName, "ipv7_client.py");
                    if (File.Exists(py)) return py;
                }
            }
            catch { }
            return null;
        }

        public static void LaunchClient(NetConfig c, IWin32Window owner)
        {
            string exe = FindClient(c);
            if (exe == null)
            {
                MessageBox.Show(owner,
                    "No IPv7 client found in the " + FolderName + "\\ folder next to Aloha.exe.\r\n\r\n" +
                    "Put IPv7Client.exe (or IPv7Host.exe) there, with the IPv7.key invite beside it, and try again.",
                    "IPv7 — client not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                ProcessStartInfo psi;
                string dir = Path.GetDirectoryName(exe);
                if (exe.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                {
                    string py = c != null && !string.IsNullOrWhiteSpace(c.PythonExe) ? c.PythonExe.Trim() : "python";
                    psi = new ProcessStartInfo("cmd.exe", "/k title IPv7 && cd /d \"" + dir + "\" && \"" + py + "\" \"" + exe + "\"")
                          { UseShellExecute = true, WorkingDirectory = dir };
                }
                else psi = new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = dir };
                if (c != null && c.Ipv7GatewayPort > 0 && c.Ipv7GatewayPort != 8888)
                {

                    Environment.SetEnvironmentVariable("IPV7_GATEWAY_PORT", c.Ipv7GatewayPort.ToString());
                }
                Process.Start(psi);
                ActivityLog.General("IPv7 client launched — " + exe);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "Could not start the IPv7 client.\r\n\r\n" + ex.Message, "IPv7", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public Ipv7Window(NetConfig config, Action<string> openInTab_)
            : base("OPT-IPV7", "IPv7")
        {
            cfg = config ?? new NetConfig();
            openInTab = openInTab_;
            Size = new Size(760, 440);
            MinimumSize = new Size(520, 320);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = true;
            KeyPreview = true;
            ClientArea.BackColor = Color.Black;

            var canvas = SetupAlohaScroll(Color.Black);
            readout = new TextBox
            {
                Multiline = true, ReadOnly = true, WordWrap = false,
                ScrollBars = ScrollBars.None, BorderStyle = BorderStyle.None,
                BackColor = Color.Black, ForeColor = Ink,
                Font = new Font("Consolas", 10f), Left = 0, Top = 0
            };
            canvas.Controls.Add(readout);

            sitesBox = new ListBox
            {
                BackColor = Color.Black, ForeColor = Ink, BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 10f), IntegralHeight = false
            };
            sitesBox.DoubleClick += (s, e) => OpenSelected();
            sitesBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { OpenSelected(); e.Handled = true; } };
            ClientArea.Controls.Add(sitesBox);

            btnRefresh = MakeLabeledButton("Refresh", () => { var t = Refresh(); });
            btnDoor    = MakeLabeledButton("Open door", () => Open(DoorUrl(cfg)));
            btnOpen    = MakeLabeledButton("Open site", OpenSelected);
            btnStart   = MakeLabeledButton("Start client", () => LaunchClient(cfg, this));
            btnFolder  = MakeLabeledButton("Folder", OpenFolder);
            foreach (var b in new[] { btnRefresh, btnDoor, btnOpen, btnStart, btnFolder })
            {
                b.BackColor = Color.Black;
                b.ForeColor = Ink;
                ClientArea.Controls.Add(b);
                b.BringToFront();
            }
            SetAlohaBottomInset(SITES_H + BAR_ROW);

            void LayoutBody()
            {
                var r = AlohaBottomStripRect;
                int rowY = r.Y + (BAR_ROW - 20) / 2;
                int x = r.Right - 4;
                foreach (var c in new Control[] { btnFolder, btnStart, btnOpen, btnDoor, btnRefresh })
                { x -= c.Width; c.SetBounds(Math.Max(4, x), rowY, c.Width, 20); x -= 6; c.BringToFront(); }
                sitesBox.SetBounds(8, r.Y + BAR_ROW, Math.Max(40, r.Width - 8), Math.Max(20, r.Height - BAR_ROW));
                sitesBox.BringToFront();
            }
            ClientArea.Resize += (s, e) => LayoutBody();
            Shown += (s, e) => LayoutBody();
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; } };
            FormClosed += (s, e) => { try { poll?.Stop(); poll?.Dispose(); } catch { } };

            Render();
            var first = Refresh();
            poll = new System.Windows.Forms.Timer { Interval = 2000 };
            poll.Tick += (s, e) => { var t = Refresh(); };
            poll.Start();
        }

        private async Task Refresh()
        {
            if (polling) return;
            polling = true;
            try
            {
                var st = await ReadStatusAsync(cfg);
                if (IsDisposed) return;
                last = st;
                Render();
            }
            finally { polling = false; }
        }

        private static string Row(string label, string value)
        {

            string lab = label + " " + string.Concat(System.Linq.Enumerable.Repeat(". ", 40));
            return "   " + lab.Substring(0, 32).TrimEnd().PadRight(32) + " : " + value;
        }

        private static string Since(int seconds)
        {
            if (seconds < 60) return seconds + "s";
            if (seconds < 3600) return (seconds / 60) + "m " + (seconds % 60) + "s";
            return (seconds / 3600) + "h " + ((seconds % 3600) / 60) + "m";
        }

        private void Render()
        {
            var sb = new StringBuilder();
            var st = last;
            sb.AppendLine();
            sb.AppendLine("IPv7 Gateway:");
            sb.AppendLine();
            sb.AppendLine(Row("Door", DoorUrl(cfg)));
            if (st == null) sb.AppendLine(Row("Status", "asking the gateway ..."));
            else if (st.Error != null)
            {
                sb.AppendLine(Row("Status", st.Error));
                sb.AppendLine(Row("Client", FindClient(cfg) ?? "not found in " + FolderName + "\\ (Start client needs it there)"));
                sb.AppendLine();
                sb.AppendLine("   !NO MATCH!  (no gateway on localhost:" + Port(cfg) + " — Start client, log in, and it appears)");
            }
            else
            {
                var s = st.Session;
                sb.AppendLine(Row("Session", s == null ? "none" : s.Bound ? (s.Resumed ? "bound (resumed)" : "bound") : "not bound"));
                if (s != null)
                {
                    sb.AppendLine(Row("Host", s.Host ?? "?"));
                    sb.AppendLine(Row("TP/IX Address", s.Address ?? "?"));
                    sb.AppendLine(Row("Key", s.Key ?? "?"));
                    sb.AppendLine(Row("Carrier", s.Carrier ?? "?"));
                    sb.AppendLine(Row("Up", Since(s.Uptime)));
                }
                sb.AppendLine(Row("Pages", st.Served + " served, " + st.Failed + " failed, " + st.Refused + " refused"));
                sb.AppendLine();
                sb.AppendLine(s != null && s.Bound ? "   !MATCH!" : "   !NO MATCH!  (the client is up but not logged in)");
            }
            sb.AppendLine();
            sb.AppendLine("Sites:  (double-click opens;  typed links: ipv7://NAME/path)");
            readout.Text = sb.ToString();
            readout.Size = MeasureBlock(readout.Text, readout.Font);

            string sel = sitesBox.SelectedItem as string;
            sitesBox.BeginUpdate();
            sitesBox.Items.Clear();
            if (st != null && st.Error == null)
            {
                if (st.Sites.Count == 0) sitesBox.Items.Add("   (this invite opens no sites)");
                foreach (var site in st.Sites) sitesBox.Items.Add("   " + site.Name.PadRight(24) + "  " + site.Url);
            }
            if (sel != null) { int i = sitesBox.Items.IndexOf(sel); if (i >= 0) sitesBox.SelectedIndex = i; }
            sitesBox.EndUpdate();
            SetEnabled(btnOpen, st != null && st.Error == null && st.Sites.Count > 0);
        }

        private Site Selected()
        {
            if (last == null || last.Error != null || sitesBox.SelectedIndex < 0 || sitesBox.SelectedIndex >= last.Sites.Count) return null;
            return last.Sites[sitesBox.SelectedIndex];
        }

        private void OpenSelected()
        {
            var site = Selected();
            if (site == null && last != null && last.Error == null && last.Sites.Count > 0) site = last.Sites[0];
            if (site != null) Open(site.Url);
        }

        private void Open(string url)
        {
            if (openInTab != null) { try { openInTab(url); return; } catch { } }
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        private void OpenFolder()
        {
            try
            {
                string exe = FindClient(cfg);
                string dir = exe != null ? Path.GetDirectoryName(exe) : Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), FolderName);
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
            }
            catch { }
        }

        private static void SetEnabled(Panel b, bool on)
        {
            if (b == null) return;
            b.Enabled = on; b.Cursor = on ? Cursors.Hand : Cursors.Default;
            b.ForeColor = on ? Ink : Dim; b.Invalidate();
        }

        private static Size MeasureBlock(string text, Font f)
        {
            int maxW = 40, lines = 0;
            foreach (var line in text.Replace("\r", "").Split('\n'))
            {
                int w = TextRenderer.MeasureText(line, f).Width;
                if (w > maxW) maxW = w;
                lines++;
            }
            int lh = TextRenderer.MeasureText("Ag", f).Height + 2;
            return new Size(maxW + 12, Math.Max(1, lines) * lh + 8);
        }
    }
}
