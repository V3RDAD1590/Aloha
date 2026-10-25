using System;
using System.Drawing;
using System.Windows.Forms;

namespace Aloha
{

    public class FindWindow : DafyFrame
    {

        public event Action<string, bool, bool, bool> FindRequested;
        public event Action ClearRequested;

        private readonly TextBox box;
        private readonly CheckBox matchCase;
        private readonly CheckBox wholeWord;
        private readonly Label result;

        private static readonly Color Face = Color.FromArgb(0xF0, 0xF0, 0xF0);
        private static readonly Font  UiFont = new Font("Tahoma", 8.25f);

        public FindWindow() : base("EDIT-FIND", "Find")
        {
            Size = new Size(width: 300, height: 150);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ClientArea.BackColor = Face;

            var p = new Panel { Dock = DockStyle.Fill, BackColor = Face };
            ClientArea.Controls.Add(p);

            p.Controls.Add(new Label
            {
                Text = "Find what:", AutoSize = true, Left = 12, Top = 15,
                ForeColor = Color.Black, BackColor = Face, Font = UiFont
            });

            var well = new BevelPanel { Left = 78, Top = 11, Width = 258, Height = 22, BackColor = Color.White };
            box = new TextBox
            {
                BorderStyle = BorderStyle.None, Dock = DockStyle.Fill,
                BackColor = Color.White, ForeColor = Color.Black,
                Font = new Font("Tahoma", 9f)
            };
            well.Controls.Add(box);
            p.Controls.Add(well);

            matchCase = new CheckBox
            {
                Text = "Match case", Left = 14, Top = 46, Width = 130,
                ForeColor = Color.Black, BackColor = Face, Font = UiFont, FlatStyle = FlatStyle.Flat
            };
            wholeWord = new CheckBox
            {
                Text = "Match whole word only", Left = 14, Top = 70, Width = 200,
                ForeColor = Color.Black, BackColor = Face, Font = UiFont, FlatStyle = FlatStyle.Flat
            };
            p.Controls.Add(matchCase);
            p.Controls.Add(wholeWord);

            result = new Label
            {
                Text = "", Left = 14, Top = 98, Width = 220, Height = 16, AutoSize = false,
                ForeColor = Color.FromArgb(0x50, 0x50, 0x50), BackColor = Face, Font = UiFont
            };
            p.Controls.Add(result);

            MakeButton(p, "Find Previous", 232, 44,  104, () => Fire(false));
            MakeButton(p, "Find Next",     232, 72,  104, () => Fire(true));
            MakeButton(p, "Close",         232, 108, 104, () => Close());

            box.TextChanged += (s, e) => Fire(true);
            box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    Fire(!e.Shift);
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    Close();
                }
            };
            matchCase.CheckedChanged += (s, e) => Fire(true);
            wholeWord.CheckedChanged += (s, e) => Fire(true);

            FormClosed += (s, e) => { try { ClearRequested?.Invoke(); } catch { } };
            Shown += (s, e) => { box.Focus(); box.SelectAll(); };
        }

        public void Present(string seed)
        {
            if (!string.IsNullOrEmpty(seed)) box.Text = seed;
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
            box.Focus(); box.SelectAll();
            if (box.TextLength > 0) Fire(true);
        }

        public void ShowResult(int current, int total)
        {
            if (box.TextLength == 0) { result.Text = ""; return; }
            result.Text = total <= 0 ? "Phrase not found"
                                     : (current + " of " + total);
        }

        private void Fire(bool forward)
        {
            string q = box.Text;
            if (string.IsNullOrEmpty(q)) { result.Text = ""; try { ClearRequested?.Invoke(); } catch { } return; }
            try { FindRequested?.Invoke(q, matchCase.Checked, wholeWord.Checked, forward); } catch { }
        }

        private Control MakeButton(Control parent, string text, int left, int top, int width, Action onClick)
        {
            var b = new BevelPanel { Raised = true, Left = left, Top = top, Width = width, Height = 23, BackColor = Face };
            var lbl = new Label
            {
                Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Black, BackColor = Face, Font = UiFont
            };
            b.Controls.Add(lbl);
            MouseEventHandler down = (s, e) => { b.Raised = false; b.Invalidate(); };
            MouseEventHandler up   = (s, e) => { b.Raised = true;  b.Invalidate(); };
            EventHandler click = (s, e) => { try { onClick(); } catch { } };
            b.MouseDown += down; b.MouseUp += up; b.Click += click;
            lbl.MouseDown += down; lbl.MouseUp += up; lbl.Click += click;
            parent.Controls.Add(b);
            return b;
        }
    }
}
