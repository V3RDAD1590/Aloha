using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Aloha
{

    public class FacetButton : Control
    {
        public string Glyph = "";
        public Color EdgeColor = Color.FromArgb(0xB4, 0xDC, 0xBA);
        public Color FaceColor = Color.FromArgb(0x14, 0x14, 0x14);
        private const int Chamfer = 6;
        private bool hot, down;

        public FacetButton()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint
                     | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Size = new Size(30, 24);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 11f);
        }

        private GraphicsPath Shape()
        {
            int w = Width - 1, h = Height - 1, c = Chamfer;
            var p = new GraphicsPath();
            p.AddLines(new[]
            {
                new Point(c, 0), new Point(w, 0), new Point(w, h - c),
                new Point(w - c, h), new Point(0, h), new Point(0, c)
            });
            p.CloseFigure();
            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Shape())
            {
                Color face = down ? ControlPaint.Dark(FaceColor, 0.02f)
                                  : (hot ? Color.FromArgb(0x20, 0x20, 0x20) : FaceColor);
                using (var fb = new SolidBrush(face)) g.FillPath(fb, path);
                using (var ep = new Pen(EdgeColor, hot ? 1.6f : 1f)) g.DrawPath(ep, path);
            }
            if (!string.IsNullOrEmpty(Glyph))
                TextRenderer.DrawText(g, Glyph, Font, ClientRectangle, EdgeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    }
}
