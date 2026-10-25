using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Aloha
{

    public class DeepWindow : DafyFrame
    {
        private static readonly Color Fab    = Color.FromArgb(0xFA, 0xFA, 0xFB);
        private static readonly Color DimGry = Color.FromArgb(0x77, 0x77, 0x77);
        private const int LOG_H = 110, BAR_ROW = 24;

        private readonly NetConfig cfg;
        private readonly Action<string> openInTab;
        private TextBox statusBox, logBox;
        private Panel logWell;
        private Panel btnRefresh, btnDemo, btnInit, btnConsole, btnViewer, btnDocs;
        private string exe, config, version, viewer;
        private readonly List<string> networks = new List<string>();

        public DeepWindow(NetConfig config_, Action<string> openInTab_)
            : base("OPT-DEEP", "DEEP protocol")
        {
            cfg = config_ ?? new NetConfig();
            openInTab = openInTab_;
            Size = new Size(820, 380);
            MinimumSize = new Size(560, 280);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = true;
            KeyPreview = true;
            ClientArea.BackColor = Fab;

            var canvas = SetupAlohaScroll(Fab);
            statusBox = new TextBox
            {
                Multiline = true, ReadOnly = true, WordWrap = false,
                ScrollBars = ScrollBars.None, BorderStyle = BorderStyle.None,
                BackColor = Fab, ForeColor = Color.Black,
                Font = new Font("Consolas", 9.5f), Left = 0, Top = 0
            };
            canvas.Controls.Add(statusBox);

            btnRefresh = MakeLabeledButton("Refresh", () => { Probe(); Render(); });
            btnDemo    = MakeLabeledButton("Demo", () => RunInConsole("demo", "the DEEP demo: two private networks, both ends authenticated"));
            btnInit    = MakeLabeledButton("Init node…", InitNode);
            btnConsole = MakeLabeledButton("Console", () => RunInConsole(null, "a console in deep\\bin (deep.exe version / serve / fetch …)"));
            btnViewer  = MakeLabeledButton("Viewer", ToggleViewer);
            btnDocs    = MakeLabeledButton("Docs", OpenDocs);
            foreach (var b in new[] { btnRefresh, btnDemo, btnInit, btnConsole, btnViewer, btnDocs })
            {
                b.BackColor = Fab;
                ClientArea.Controls.Add(b);
                b.BringToFront();
            }

            logWell = new Panel { BackColor = Color.Black, Padding = new Padding(4, 3, 0, 0) };
            logWell.Paint += (s, e) =>
            {
                using (var dk = new Pen(Color.FromArgb(0x80, 0x80, 0x80))) e.Graphics.DrawLine(dk, 0, 0, logWell.Width, 0);
                using (var md = new Pen(Color.FromArgb(0x40, 0x40, 0x40))) e.Graphics.DrawLine(md, 0, 1, logWell.Width, 1);
            };
            logBox = new TextBox
            {
                Multiline = true, ReadOnly = true, WordWrap = false,
                ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
                BackColor = Color.Black, ForeColor = Color.FromArgb(0xC8, 0xC8, 0xC8),
                Font = new Font("Consolas", 9f), Dock = DockStyle.Fill
            };
            logWell.Controls.Add(logBox);
            ClientArea.Controls.Add(logWell);
            logWell.BringToFront();
            SetAlohaBottomInset(LOG_H + BAR_ROW);

            void LayoutBody()
            {
                var r = AlohaBottomStripRect;
                int rowY = r.Y + (BAR_ROW - 20) / 2;
                int x = r.Right - 4;
                foreach (var c in new Control[] { btnDocs, btnViewer, btnConsole, btnInit, btnDemo, btnRefresh })
                { x -= c.Width; c.SetBounds(Math.Max(4, x), rowY, c.Width, 20); x -= 6; c.BringToFront(); }
                logWell.SetBounds(0, r.Y + BAR_ROW, Math.Max(40, r.Width), Math.Max(20, r.Height - BAR_ROW));
                logWell.BringToFront();
            }
            ClientArea.Resize += (s, e) => LayoutBody();
            Shown += (s, e) => LayoutBody();
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; } };

            Probe();
            Render();
        }

        private void LogLine(string s)
        {
            if (s == null) return;
            ActivityLog.General("DEEP: " + s);
            if (logBox == null || logBox.IsDisposed) return;
            try
            {
                if (logBox.InvokeRequired) { logBox.BeginInvoke((Action)(() => LogLine(s))); return; }
                logBox.AppendText(s + "\r\n");
                logBox.SelectionStart = logBox.TextLength;
                logBox.ScrollToCaret();
            }
            catch { }
        }

        private void Probe()
        {
            exe = DeepProtocol.FindExe(cfg);
            config = DeepProtocol.FindConfig(cfg, exe);
            version = exe != null ? FirstLine(DeepProtocol.RunQuick(cfg, "version")) : null;
            viewer = exe != null ? FirstLine(DeepProtocol.RunQuick(cfg, "viewer status")) : null;
            networks.Clear();
            try
            {
                if (config != null)
                {
                    var root = DeepProtocol.Json.Parse(File.ReadAllText(config, Encoding.UTF8)) as Dictionary<string, object>;
                    object nets;
                    if (root != null && root.TryGetValue("networks", out nets) && nets is Dictionary<string, object>)
                        foreach (var kv in (Dictionary<string, object>)nets)
                        {
                            var n = kv.Value as Dictionary<string, object>;
                            string adapter = n != null ? DeepProtocol.Json.Str(n, "adapter") ?? "?" : "?";
                            var ends = new List<string>();
                            object ep;
                            if (n != null && n.TryGetValue("endpoints", out ep) && ep is Dictionary<string, object>)
                                foreach (var e in (Dictionary<string, object>)ep) ends.Add(e.Key);
                            networks.Add(kv.Key + "  (" + adapter + (ends.Count > 0 ? ": " + string.Join(", ", ends.ToArray()) : "") + ")");
                        }
                }
            }
            catch (Exception ex) { LogLine("config.json: " + ex.Message); }
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            string line = s.Replace("\r", "").Split('\n')[0].Trim();
            return line.Length > 90 ? line.Substring(0, 90) + "…" : line;
        }

        private void Render()
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("  DEEP            : Decentralized Extensible Endpoint Protocol  ·  deep://node.network/path");
            sb.AppendLine("  deep.exe        : " + (exe ?? "NOT FOUND — unzip a DEEP release into deep\\ beside Aloha.exe"));
            sb.AppendLine("  version         : " + (version ?? "—"));
            sb.AppendLine("  config.json     : " + (config ?? "none (Init node… writes one; or set DeepConfig in AlohaNet.json)"));
            sb.AppendLine("  networks        : " + (networks.Count == 0 ? "none configured" : networks[0]));
            for (int i = 1; i < networks.Count; i++) sb.AppendLine("                    " + networks[i]);
            sb.AppendLine("  viewer          : " + (viewer ?? "—") + "   (Aloha renders deep:// itself; the viewer is DEEP's own, optional)");
            sb.AppendLine("  in the browser  : type deep://authority/path  →  https://authority.deep.aloha/  (deep request, verified body)");
            statusBox.Text = sb.ToString();
            statusBox.Size = MeasureBlock(statusBox.Text, statusBox.Font);
            bool have = exe != null;
            foreach (var b in new[] { btnDemo, btnInit, btnConsole, btnViewer }) SetEnabled(b, have);
        }

        private static void SetEnabled(Panel b, bool on)
        {
            if (b == null) return;
            b.Enabled = on; b.Cursor = on ? Cursors.Hand : Cursors.Default;
            b.ForeColor = on ? Color.Black : DimGry; b.Invalidate();
        }

        private void RunInConsole(string deepArgs, string what)
        {
            if (exe == null) return;
            try
            {
                string dir = Path.GetDirectoryName(exe);
                string cmd = deepArgs == null ? "/k title DEEP && cd /d \"" + dir + "\" && deep.exe version"
                                              : "/k title DEEP " + deepArgs + " && cd /d \"" + dir + "\" && deep.exe " + deepArgs;
                var p = Process.Start(new ProcessStartInfo("cmd.exe", cmd) { UseShellExecute = true, WorkingDirectory = dir });
                try { ProcessLeash.Leash(p); } catch { }
                LogLine("opened " + what);
            }
            catch (Exception ex) { LogLine("! " + ex.Message); }
        }

        private void InitNode()
        {
            string authority, address;
            using (var d1 = new NamePromptDialog("Init node", "Authority (the node's name, e.g. node.alpha):", "node.alpha"))
            {
                if (d1.ShowDialog(this) != DialogResult.OK || d1.Value.Trim().Length == 0) return;
                authority = d1.Value.Trim();
            }
            using (var d2 = new NamePromptDialog("Init node", "Listen address (127.0.0.1:9761 is local only):", "127.0.0.1:9761"))
            {
                if (d2.ShowDialog(this) != DialogResult.OK || d2.Value.Trim().Length == 0) return;
                address = d2.Value.Trim();
            }
            string dirName = "node-" + authority.Replace('.', '-');
            RunInConsole("init --authority " + authority + " --dir " + dirName + " --address " + address,
                         "deep init for " + authority + " (writes " + dirName + "\\ with server.json, client.json and the identity)");
            LogLine("then: deep.exe serve --config " + dirName + "\\server.json  — and point config.json at it to browse deep://" + authority + "/");
        }

        private void ToggleViewer()
        {
            if (exe == null) return;
            bool on = viewer != null && viewer.IndexOf("enabled", StringComparison.OrdinalIgnoreCase) >= 0
                      && viewer.IndexOf("disabled", StringComparison.OrdinalIgnoreCase) < 0;
            string r = DeepProtocol.RunQuick(cfg, on ? "viewer disable" : "viewer enable");
            LogLine("viewer " + (on ? "disable" : "enable") + ": " + (FirstLine(r) ?? "(no answer)"));
            Probe(); Render();
        }

        private void OpenDocs()
        {
            try
            {
                string dir = exe != null ? Path.GetDirectoryName(Path.GetDirectoryName(exe)) : null;
                string readme = dir != null ? Path.Combine(dir, "README.md") : null;
                if (readme != null && File.Exists(readme))
                {
                    if (openInTab != null) openInTab(new Uri(readme).AbsoluteUri);
                    else Process.Start(new ProcessStartInfo(readme) { UseShellExecute = true });
                }
                else LogLine("README.md not found next to deep\\bin\\");
            }
            catch (Exception ex) { LogLine("! " + ex.Message); }
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
