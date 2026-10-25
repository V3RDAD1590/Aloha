using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Aloha
{
    public class Torrc7Window : DafyFrame
    {
        private static readonly Color Fab     = Color.FromArgb(0xFA, 0xFA, 0xFB);
        private static readonly Color Green   = Color.FromArgb(0x7F, 0xAE, 0x86);
        private static readonly Color GreenHi = Color.FromArgb(0xB4, 0xDC, 0xBA);
        private static readonly Color GreenLo = Color.FromArgb(0x3E, 0x5C, 0x44);
        private static readonly Color Amber   = Color.FromArgb(0xFF, 0xB6, 0x2E);
        private const int SB = 17, GUTTER = 34, RESULT_H = 58;

        private readonly string userPath, torExe;
        private readonly Func<string> generate;
        private readonly Action<string> log;

        private readonly Label state;
        private readonly Panel gutter, result;
        private readonly RichTextBox box;
        private readonly DarkScrollBar vbar, hbar;
        private readonly Panel corner;
        private readonly TextBox resultText;
        private readonly Timer colorTimer = new Timer { Interval = 250 };
        private bool dirty, loading, coloring;

        private const int SET_H = 24;
        private readonly NetConfig cfg;
        private Panel tSite;
        private Label exitLbl, preview;
        private TextBox exitBox;

        public Torrc7Window(string userTorrcPath, Func<string> generated, Action<string> logLine, string torExePath = null, NetConfig config = null)
            : base("OPT-TORRC7", "torrc7")
        {
            userPath = userTorrcPath; generate = generated; log = logLine; torExe = torExePath; cfg = config;
            Size = new Size(700, 470);
            MinimumSize = new Size(420, 260);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            KeyPreview = true;
            Font = new Font("Tahoma", 8.25f);
            ClientArea.BackColor = Fab;
            HideFooterCubes();

            state = new Label { AutoSize = false, ForeColor = Color.FromArgb(0x40, 0x40, 0x40), BackColor = Fab, Font = new Font("Tahoma", 8.25f), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };

            gutter = new DoubleBufferedPanel { BackColor = Color.Black };
            gutter.Paint += PaintGutter;

            box = new RichTextBox
            {
                BorderStyle = BorderStyle.None, DetectUrls = false, WordWrap = false, AcceptsTab = true,
                BackColor = Color.Black, ForeColor = Green, Font = new Font("Consolas", 9.5f),
                ScrollBars = RichTextBoxScrollBars.ForcedBoth
            };
            vbar = new DarkScrollBar(false, light: true) { Dock = DockStyle.None };
            hbar = new DarkScrollBar(true, light: true) { Dock = DockStyle.None };
            corner = new Panel { BackColor = Color.FromArgb(0xF0, 0xF0, 0xF0) };

            result = new Panel { BackColor = Color.Black };
            resultText = new TextBox
            {
                Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, WordWrap = true,
                BackColor = Color.Black, ForeColor = GreenLo, Font = new Font("Consolas", 8.75f),
                Text = "Check runs tor.exe --verify-config on the text above (nothing is saved)."
            };
            result.Controls.Add(resultText);

            foreach (var c in new Control[] { state, gutter, box, vbar, hbar, corner, result }) ClientArea.Controls.Add(c);
            if (cfg != null) BuildSettingsRow();
            vbar.Attach(box); hbar.Attach(box);

            var bSave  = MakeLabeledButton("Save", Save);
            var bCheck = MakeLabeledButton("Check", Check);
            var bGen   = MakeLabeledButton("Regenerate", Regenerate);
            foreach (var b in new[] { bSave, bCheck, bGen }) Footer.Controls.Add(b);
            Action row = () => LayoutFooterRow(bSave, bCheck, bGen);
            Footer.Resize += (s, e) => row(); Shown += (s, e) => row();

            box.TextChanged += (s, e) => { if (loading || coloring) return; dirty = true; ShowState(); colorTimer.Stop(); colorTimer.Start(); gutter.Invalidate(); };
            box.VScroll += (s, e) => gutter.Invalidate();
            box.SelectionChanged += (s, e) => gutter.Invalidate();
            colorTimer.Tick += (s, e) => { colorTimer.Stop(); Colorize(); };
            ClientArea.Resize += (s, e) => LayoutAll();
            Shown += (s, e) => LayoutAll();
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
                else if (e.Control && e.KeyCode == Keys.S) { Save(); e.Handled = true; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.F5) { Check(); e.Handled = true; }
            };
            FormClosing += (s, e) =>
            {
                if (!dirty) return;
                var a = MessageBox.Show(this, "Save the changes to torrc7.user?", "torrc7", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (a == DialogResult.Cancel) e.Cancel = true; else if (a == DialogResult.Yes) Save();
            };
            FormClosed += (s, e) => colorTimer.Dispose();

            LoadText();
        }

        private void LayoutAll()
        {
            int w = ClientArea.ClientSize.Width, h = ClientArea.ClientSize.Height;
            if (w < 100 || h < 100) return;
            state.SetBounds(8, 0, w - 16, 22);
            int top = 22;
            if (cfg != null)
            {
                tSite.SetBounds(8, top + 2, tSite.Width, 20);
                exitLbl.SetBounds(tSite.Right + 10, top + 2, 30, 20);
                exitBox.SetBounds(exitLbl.Right, top + 3, 34, 18);
                preview.SetBounds(exitBox.Right + 10, top + 2, Math.Max(40, w - exitBox.Right - 18), 20);
                top += SET_H;
            }
            int edH = Math.Max(40, h - top - SB - RESULT_H - 2);
            gutter.SetBounds(0, top, GUTTER, edH);
            box.SetBounds(GUTTER, top, w - GUTTER, edH + SB);
            vbar.SetBounds(w - SB, top, SB, edH);
            hbar.SetBounds(0, top + edH, w - SB, SB);
            corner.SetBounds(w - SB, top + edH, SB, SB);
            vbar.BringToFront(); hbar.BringToFront(); corner.BringToFront();
            result.SetBounds(0, top + edH + SB + 2, w, RESULT_H);
            resultText.SetBounds(GUTTER, 4, w - GUTTER - 6, RESULT_H - 6);
            gutter.Invalidate();
        }

        private void BuildSettingsRow()
        {
            tSite = MakeFooterToggle("[per site]", () => !cfg.SealedTorSharedCircuits, v =>
            {
                cfg.SealedTorSharedCircuits = !v; try { cfg.Save(); } catch { }
                log?.Invoke("sealed | circuits: " + (v ? "ONE PER SITE (IsolateDestAddr)" : "SHARED") + "  (Stop + Start for the torrc to follow)");
                UpdatePreview();
                if (!dirty && !UserFileExists) LoadText();
            });
            tSite.Width = Math.Max(tSite.Width, TextRenderer.MeasureText("[per site]", new Font("Lucida Console", 7f, FontStyle.Bold)).Width + 12);
            exitLbl = new Label { Text = "exit", AutoSize = false, BackColor = Fab, ForeColor = Color.FromArgb(0x40, 0x40, 0x40), TextAlign = ContentAlignment.MiddleRight, Font = new Font("Tahoma", 8.25f) };
            exitBox = new TextBox
            {
                BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black, ForeColor = GreenHi, Font = new Font("Consolas", 9f),
                MaxLength = 3, CharacterCasing = CharacterCasing.Lower, TextAlign = HorizontalAlignment.Center,
                Text = string.IsNullOrEmpty(cfg.SealedTorExit) ? "any" : cfg.SealedTorExit
            };
            exitBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { ApplyExit(); e.Handled = e.SuppressKeyPress = true; } };
            exitBox.Leave += (s, e) => ApplyExit();
            preview = new Label { AutoSize = false, BackColor = Fab, ForeColor = Color.FromArgb(0x2F, 0x5A, 0x38), Font = new Font("Consolas", 8.25f), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            foreach (var c in new Control[] { tSite, exitLbl, exitBox, preview }) ClientArea.Controls.Add(c);
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (preview == null) return;
            string ex = cfg.SealedTorExit ?? "";
            preview.Text = "SocksPort 9070" + (cfg.SealedTorSharedCircuits ? "" : " IsolateDestAddr")
                         + (ex.Length > 0 ? "   \u00b7  ExitNodes {" + ex + "} StrictNodes 1" : "   \u00b7  exit: any");
        }

        private void ApplyExit()
        {
            string v = (exitBox.Text ?? "").Trim().ToLowerInvariant();
            if (v == "any" || v == "*") v = "";
            if (v.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(v, "^[a-z]{2}$"))
            { Result("exit wants a two-letter country code (de, nl, ch \u2026) or any", true); exitBox.Text = string.IsNullOrEmpty(cfg.SealedTorExit) ? "any" : cfg.SealedTorExit; return; }
            if (v == (cfg.SealedTorExit ?? "")) { if (v.Length == 0) exitBox.Text = "any"; return; }
            cfg.SealedTorExit = v; try { cfg.Save(); } catch { }
            if (v.Length == 0) exitBox.Text = "any";
            UpdatePreview();

            if (!dirty && !UserFileExists) LoadText();
            else log?.Invoke("sealed | exit " + (v.Length > 0 ? "{" + v + "}" : "any") + " \u2014 torrc7.user is hand-edited: write ExitNodes / StrictNodes in it yourself");

            System.Threading.Tasks.Task<bool> t = TorControl.OnceAsync(cfg.SealedTorControlPort, null, cfg.SealedTorControlPassword, c =>
            {
                string e; return v.Length == 0 ? c.ResetConf("ExitNodes StrictNodes", out e) : c.SetConf("ExitNodes={" + v + "} StrictNodes=1", out e);
            }, false);
            t.ContinueWith(r => { try { BeginInvoke((Action)(() => Result(r.Result ? "exit " + (v.Length > 0 ? "{" + v + "}" : "any") + " applied to the running sealed Tor"
                                                                                       : "exit saved \u2014 the sealed Tor isn't running; it takes effect on Start", false))); } catch { } });
        }

        private void PaintGutter(object sender, PaintEventArgs e) { TorrcSyntax.PaintGutter(e.Graphics, gutter, box); }

        private bool UserFileExists { get { try { return File.Exists(userPath); } catch { return false; } } }

        private void LoadText()
        {
            loading = true;
            try { box.Text = (UserFileExists ? File.ReadAllText(userPath, Encoding.UTF8) : (generate() ?? "")).Replace("\r\n", "\n").Replace("\n", "\r\n"); }
            catch (Exception ex) { box.Text = "# could not read torrc: " + ex.Message; }
            loading = false; dirty = false;
            Colorize();
            ShowState();
        }

        private string unknownNote;
        private void ShowState()
        {
            state.Text = (UserFileExists ? "hand-edited · Start uses torrc7.user as written" : "generated · from the policy switch and paths (not saved)")
                       + (dirty ? "   — unsaved" : "")
                       + (unknownNote != null ? "   — " + unknownNote : "");
            state.ForeColor = unknownNote != null ? Color.FromArgb(0xA0, 0x60, 0x00) : Color.FromArgb(0x40, 0x40, 0x40);
        }

        private void Colorize()
        {
            if (box.IsDisposed) return;
            coloring = true;
            List<string> unknown;
            try { unknown = TorrcSyntax.Colorize(box); } finally { coloring = false; }
            unknownNote = unknown.Count == 0 ? null : "not a known option: " + string.Join(", ", unknown) + " (Check before Start)";
            ShowState();
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(userPath));
                File.WriteAllText(userPath, box.Text.Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(false));
                dirty = false; ShowState();
                log?.Invoke("sealed | torrc7.user saved \u2014 the next Start uses it (Stop + Start to apply)");
            }
            catch (Exception ex) { Result("save failed: " + ex.Message, true); }
        }

        private void Regenerate()
        {
            if (dirty && MessageBox.Show(this, "Discard your edits and go back to the generated torrc?", "torrc7", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { if (UserFileExists) File.Delete(userPath); } catch { }
            log?.Invoke("sealed | torrc7.user removed \u2014 back to the generated torrc");
            LoadText();
            Result("back to the generated torrc.", false);
        }

        private void Check()
        {
            Result("checking with tor.exe \u2026", false);
            TorrcSyntax.Verify(torExe, box.Text, this, (ok, verdict) => Result(verdict, !ok));
        }

        private void Result(string text, bool bad)
        {
            if (resultText.IsDisposed) return;
            resultText.ForeColor = bad ? Amber : GreenHi;
            resultText.Text = text;
        }
    }
}
