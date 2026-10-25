using System;
using System.Drawing;
using System.Windows.Forms;

namespace Aloha
{

    public class BevelPanel : Panel
    {

        public bool Raised { get; set; }

        public BevelPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.FromArgb(0xBB, 0xBB, 0xBB);
            Padding = new Padding(2, 2, 2, 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            int w = Width - 1, h = Height - 1;
            Color tl = Raised ? Color.White : Color.FromArgb(0x80, 0x80, 0x80);
            Color br = Raised ? Color.FromArgb(0x80, 0x80, 0x80) : Color.White;
            using (var penTL = new Pen(tl))
            using (var penBR = new Pen(br))
            {

                g.DrawLine(penTL, 0, 0, w, 0);
                g.DrawLine(penTL, 0, 0, 0, h);

                g.DrawLine(penBR, 0, h, w, h);
                g.DrawLine(penBR, w, 0, w, h);
            }
        }
    }
}
