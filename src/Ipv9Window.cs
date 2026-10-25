using System;
using System.Drawing;
using System.Windows.Forms;

namespace Aloha
{

    public class Ipv9Window : DafyFrame
    {
        private static readonly Color Fab = Color.FromArgb(0xFA, 0xFA, 0xFB);

        public Ipv9Window(NetConfig config = null)
            : base("OPT-IPV9", "IPv9")
        {
            Size = new Size(920, 530);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            KeyPreview = true;
            Font = new Font("Tahoma", 8.25f);
            ClientArea.BackColor = Fab;

            var canvas = SetupAlohaScroll(Fab);
            canvas.BackColor = Fab;

            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; } };
        }
    }
}
