using System;
using System.Drawing;
using System.Windows.Forms;

namespace Aloha
{

    public class TerminalInput : Control
    {
        private string buffer = "";
        private bool blinkOn = true;
        private readonly Timer blink;
        private readonly Color lime;
        public event Action<string> Submitted;

        public TerminalInput(Font font, Color limeColor)
        {
            lime = limeColor;
            Font = font;
            BackColor = Color.Black;
            TabStop = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            blink = new Timer { Interval = 530 };
            blink.Tick += (s, e) => { blinkOn = !blinkOn; Invalidate(); };
            blink.Start();
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); blinkOn = true; Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override bool IsInputKey(Keys keyData) { return true; }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            char c = e.KeyChar;
            if (c == '\r' || c == '\n')
            {
                string cmd = buffer; buffer = "";
                var h = Submitted; if (h != null) h(cmd);
            }
            else if (c == '\b') { if (buffer.Length > 0) buffer = buffer.Substring(0, buffer.Length - 1); }
            else if (!char.IsControl(c)) { buffer += c; }
            blinkOn = true; Invalidate();
            e.Handled = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var bg = new SolidBrush(Color.Black)) g.FillRectangle(bg, ClientRectangle);

            const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding
                                        | TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
            const int pad = 4;
            TextRenderer.DrawText(g, buffer, Font, new Rectangle(pad, 0, Width - pad, Height), lime, flags);

            Size textSz = TextRenderer.MeasureText(g, buffer, Font, new Size(int.MaxValue, Height),
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            int cell = TextRenderer.MeasureText(g, "0", Font, new Size(int.MaxValue, Height),
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
            if (blinkOn)
            {
                int ch = Font.Height - 4;
                int cy = (Height - ch) / 2;
                using (var cb = new SolidBrush(lime))
                    g.FillRectangle(cb, pad + textSz.Width + 1, cy, Math.Max(7, cell), ch);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && blink != null) blink.Dispose();
            base.Dispose(disposing);
        }
    }
}
