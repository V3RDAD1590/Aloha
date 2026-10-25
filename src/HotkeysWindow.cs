using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Aloha
{
    public class HotkeysWindow : DafyFrame
    {
        private static readonly Color Fab = Color.FromArgb(0xFA, 0xFA, 0xFB);
        private readonly Action onApply;
        private readonly Dictionary<HotkeyAction, TextBox> boxes = new Dictionary<HotkeyAction, TextBox>();
        private readonly Dictionary<HotkeyAction, Keys> pending = new Dictionary<HotkeyAction, Keys>();
        private readonly Label status;

        public HotkeysWindow(Action applyCallback)
            : base("OPT-KEYS", "Hotkeys")
        {
            onApply = applyCallback;
            Size = new Size(520, 560);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            Font = new Font("Tahoma", 8.25f);
            ClientArea.BackColor = Fab;

            var canvas = SetupAlohaScroll(Fab);
            int y = 10;
            var head = new Label
            {
                Text = "Click a box and press the keys you want. Backspace clears a binding.\n"
                     + "A chord works whether the page or Aloha's own bar has the focus.",
                Left = 14, Top = y, Width = 460, Height = 32, ForeColor = Color.FromArgb(0x50, 0x50, 0x50)
            };
            canvas.Controls.Add(head);
            y += 40;

            foreach (var e in Hotkeys.Table)
            {
                var l = new Label { Text = e.Label, Left = 14, Top = y + 3, Width = 290, AutoSize = false, ForeColor = Color.Black };
                var tb = new TextBox
                {
                    Left = 310, Top = y, Width = 150, ReadOnly = true, BackColor = Color.White,
                    Font = new Font("Lucida Console", 8.25f), TextAlign = HorizontalAlignment.Center,
                    Cursor = Cursors.Hand, Tag = e.Action
                };
                tb.Text = Hotkeys.Pretty(Hotkeys.Get(e.Action));
                pending[e.Action] = Hotkeys.Get(e.Action);
                tb.GotFocus += (s, a) => { tb.BackColor = Color.FromArgb(0xFF, 0xF6, 0xC8); tb.Text = "press keys…"; };
                tb.LostFocus += (s, a) => { tb.BackColor = Color.White; tb.Text = Hotkeys.Pretty(pending[(HotkeyAction)tb.Tag]); };
                tb.PreviewKeyDown += (s, a) => { a.IsInputKey = true; };
                tb.KeyDown += (s, a) =>
                {
                    a.SuppressKeyPress = true; a.Handled = true;
                    var act = (HotkeyAction)tb.Tag;
                    Keys code = a.KeyCode;
                    if (code == Keys.ControlKey || code == Keys.ShiftKey || code == Keys.Menu || code == Keys.LWin || code == Keys.RWin) return;
                    if (code == Keys.Back || code == Keys.Delete) { pending[act] = Keys.None; tb.Text = Hotkeys.Pretty(Keys.None); Clash(); return; }
                    if (code == Keys.Escape) { tb.Text = Hotkeys.Pretty(pending[act]); ActiveControl = null; return; }
                    pending[act] = a.KeyData;
                    tb.Text = Hotkeys.Pretty(a.KeyData);
                    Clash();
                };
                canvas.Controls.Add(l);
                canvas.Controls.Add(tb);
                boxes[e.Action] = tb;
                y += 27;
            }

            status = new Label { Left = 14, Top = y + 6, Width = 460, Height = 18, ForeColor = Color.FromArgb(0xB0, 0x20, 0x20) };
            canvas.Controls.Add(status);
            y += 30;

            var defaults = new Button { Text = "Defaults", Left = 14, Top = y, Width = 90, Height = 24, FlatStyle = FlatStyle.System };
            defaults.Click += (s, a) =>
            {
                foreach (var e in Hotkeys.Table) { pending[e.Action] = e.Default; boxes[e.Action].Text = Hotkeys.Pretty(e.Default); }
                Clash();
            };
            canvas.Controls.Add(defaults);
            y += 34;

            SetFooterExtra(MakeLabeledButton("Apply", Apply));
            KeyPreview = false;
        }

        private void Clash()
        {
            var seen = new Dictionary<Keys, HotkeyAction>();
            foreach (var e in Hotkeys.Table)
            {
                Keys k = pending[e.Action]; if (k == Keys.None) continue;
                HotkeyAction other;
                if (seen.TryGetValue(k, out other))
                {
                    status.Text = Hotkeys.Pretty(k) + " is bound twice (" + Label(other) + " and " + Label(e.Action) + ") — the first wins.";
                    return;
                }
                seen[k] = e.Action;
            }
            status.Text = "";
        }

        private static string Label(HotkeyAction a) { foreach (var e in Hotkeys.Table) if (e.Action == a) return e.Label; return a.ToString(); }

        private void Apply()
        {
            foreach (var kv in pending) Hotkeys.Set(kv.Key, kv.Value);
            Hotkeys.Save();
            if (onApply != null) onApply();
        }
    }
}
