using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Aloha
{

    public class TamperPanel : DafyFrame
    {
        private static readonly Color Modern = Color.FromArgb(0xF0, 0xF0, 0xF0);
        private const int SB = 17;

        private TextBox feed;
        private TextBox input;
        private DarkScrollBar vbar, hbar;
        private Panel corner;
        private Panel btnEnable;

        public TamperPanel() : base("OPT-0400", "Request tamper")
        {
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

            var inputBevel = new BevelPanel { Left = 6, Top = 3, Width = 360, Height = 20, BackColor = Modern };
            input = new TextBox
            {
                BackColor = Modern, ForeColor = Color.Black,
                Font = new Font("Consolas", 9.75f), BorderStyle = BorderStyle.None,
                HideSelection = true, Dock = DockStyle.Fill
            };
            input.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Submit(); } };
            inputBevel.Controls.Add(input);

            btnEnable = MakeLabeledButton("Tamper: OFF", ToggleEnabled);
            var btnClear = MakeLabeledButton("Clear", () => feed.Clear());
            var btnSave  = MakeLabeledButton("Save...", Save);

            Footer.Controls.Add(inputBevel);
            Footer.Controls.Add(btnEnable);
            Footer.Controls.Add(btnClear);
            Footer.Controls.Add(btnSave);
            inputBevel.BringToFront(); btnEnable.BringToFront();
            btnClear.BringToFront(); btnSave.BringToFront();

            void LayoutFoot()
            {

                int limit = FooterRightLimit;
                int g = DafyFrame.FooterGap;
                btnSave.Top = 3;   btnSave.Left   = limit - btnSave.Width - g;
                btnClear.Top = 3;  btnClear.Left  = btnSave.Left - btnClear.Width - g;
                btnEnable.Top = 3; btnEnable.Left = btnClear.Left - btnEnable.Width - g;

                inputBevel.Left = FooterContentLeft;
                inputBevel.Top  = 3;
                int bw = btnEnable.Left - g - inputBevel.Left;
                inputBevel.Visible = bw > 24;
                if (inputBevel.Visible) inputBevel.Width = bw;
                inputBevel.Width = Math.Max(60, btnEnable.Left - inputBevel.Left - 6);
            }
            Footer.Resize += (s, e) => LayoutFoot();

            ClientArea.Controls.Add(feed);
            ClientArea.Controls.Add(vbar);
            ClientArea.Controls.Add(hbar);
            ClientArea.Controls.Add(corner);

            ClientArea.Resize += (s, e) => LayoutMain();
            this.Shown += (s, e) => { LayoutMain(); LayoutFoot(); input.Focus(); };
            vbar.Attach(feed);
            hbar.Attach(feed);

            TamperEngine.OnLog += OnTamperLog;
            FormClosed += (s, e) => TamperEngine.OnLog -= OnTamperLog;

            SyncEnableButton();
            Banner();
            ListRules();
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

        private void Banner()
        {
            Append("REQUEST TAMPER — rules act inside the browser's request hook");
            Append("master switch is OFF until you click 'Tamper: OFF' (it never changes traffic silently)");
            Append("");
            Append("RULES  (match = case-insensitive substring of the request URL)");
            Append("    block <match>                    short-circuit with 403");
            Append("    redirect <match> <url>           302 to <url>");
            Append("    setheader <match> <Name>: <val>  add/replace a request header");
            Append("    rmheader <match> <Name>          strip a request header");
            Append("    respond <match> <code> <ctype> <body...>   canned response");
            Append("");
            Append("MANAGE   list   del <n>   toggle <n>   delall   on   off   clear   help");
            Append("");
            Append("note: 'respond' supplies YOUR body — the real upstream response body");
            Append("      can't be edited in-flight through this API (that's the Intercept proxy's job).");
            Append("");
        }

        private void ToggleEnabled()
        {
            TamperEngine.Enabled = !TamperEngine.Enabled;
            SyncEnableButton();
            Append(TamperEngine.Enabled
                ? "== TAMPER ON  (" + TamperEngine.Count + " rule(s)) =="
                : "== TAMPER OFF ==");
        }

        private void SyncEnableButton()
        {
            btnEnable.Text = TamperEngine.Enabled ? "Tamper: ON" : "Tamper: OFF";
            btnEnable.Invalidate();
        }

        private void Submit()
        {
            string line = (input.Text ?? "").Trim();
            input.Clear();
            if (line.Length == 0) return;
            Append("> " + line);

            string[] p = line.Split(new[] { ' ' }, 2);
            string cmd = p[0].ToLowerInvariant();
            string arg = p.Length > 1 ? p[1].Trim() : "";

            switch (cmd)
            {
                case "help":   Banner(); return;
                case "list":   ListRules(); return;
                case "on":     if (!TamperEngine.Enabled) ToggleEnabled(); else Append("already on"); return;
                case "off":    if (TamperEngine.Enabled)  ToggleEnabled(); else Append("already off"); return;
                case "delall": TamperEngine.ClearRules(); Append("all rules cleared"); ListRules(); return;
                case "del":
                {
                    int n;
                    if (int.TryParse(arg, out n) && TamperEngine.DeleteRule(n)) { Append("deleted rule " + n); ListRules(); }
                    else Append("usage: del <n>   (see 'list')");
                    return;
                }
                case "toggle":
                {
                    int n;
                    if (int.TryParse(arg, out n) && TamperEngine.ToggleRule(n)) { Append("toggled rule " + n); ListRules(); }
                    else Append("usage: toggle <n>   (see 'list')");
                    return;
                }
            }

            string err = TamperEngine.AddFromText(line);
            if (err != null) Append("not added: " + err);
            else
            {
                Append("rule added"
                     + (TamperEngine.Enabled ? "" : "  (tamper is OFF — turn it on to apply)"));
                ListRules();
            }
        }

        private void ListRules()
        {
            var snap = TamperEngine.Snapshot();
            if (snap.Count == 0) { Append("  (no rules)"); return; }
            Append("RULES (" + snap.Count + ")");
            for (int i = 0; i < snap.Count; i++)
                Append("  " + i + "  " + snap[i].Describe());
        }

        private void OnTamperLog(string line)
        {
            if (feed.IsDisposed) return;
            if (feed.InvokeRequired) feed.BeginInvoke((Action)(() => Append(line)));
            else Append(line);
        }

        private void Append(string line)
        {
            if (feed.IsDisposed) return;
            if (feed.TextLength > 200000) feed.Text = feed.Text.Substring(feed.TextLength - 150000);
            feed.AppendText(line + "\r\n");
            feed.SelectionStart = feed.TextLength;
            feed.ScrollToCaret();
        }

        private void Save()
        {
            try
            {
                using (var sf = new SaveFileDialog { Filter = "Log|*.log|Text|*.txt", FileName = "aloha-tamper.log" })
                    if (sf.ShowDialog() == DialogResult.OK)
                        File.WriteAllText(sf.FileName, feed.Text);
            }
            catch { }
        }
    }
}
