using System;
using System.Drawing;
using System.Windows.Forms;

namespace Aloha
{

    public class FileExplorerWindow : Form
    {
        private static readonly Color Modern = Color.FromArgb(0xF0, 0xF0, 0xF0);
        private const int FOOTER_H = 26;

        private FileTreeView view;
        private Panel footer;
        private BevelPanel fIndent, grip;
        private Label fStatus;

        public event Action<string> SelectionChanged;

        private bool gripDragging, gripDidDrag;
        private Point gripStart;
        private Size gripStartSize;

        public FileExplorerWindow(Action<string> openCallback)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            MinimumSize = new Size(280, 240);

            view = new FileTreeView(openCallback) { Dock = DockStyle.None };
            view.SelectionChanged += n => SelectionChanged?.Invoke(n);
            view.SelectionPath    += p => { if (fStatus != null) fStatus.Text = p ?? ""; };
            Controls.Add(view);

            footer  = new Panel { BackColor = Modern };
            fIndent = new BevelPanel { Top = 3, Left = 4, Height = 20, BackColor = Modern };
            fStatus = new Label
            {
                Dock = DockStyle.Fill, BackColor = Modern, ForeColor = Color.FromArgb(0x30, 0x30, 0x30),
                Font = new Font("Consolas", 8.25f), TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true, Padding = new Padding(4, 0, 4, 0)
            };
            fIndent.Controls.Add(fStatus);
            grip  = new BevelPanel { Top = 3, Width = 20, Height = 20, BackColor = Modern, Cursor = Cursors.SizeNWSE };
            grip.MouseDown += Grip_Down;
            grip.MouseMove += Grip_Move;
            grip.MouseUp   += Grip_Up;
            footer.Controls.Add(fIndent);
            footer.Controls.Add(grip);
            footer.Resize += (s, e) => LayoutFooter();
            Controls.Add(footer);

            this.Resize += (s, e) => LayoutOverlay();
            LayoutOverlay();
        }

        public void FocusView() { view.FocusTree(); }
        public void NavigateTo(string path) { view.NavigateTo(path); }

        private void LayoutOverlay()
        {
            int cw = ClientSize.Width, ch = ClientSize.Height;
            int bodyH = Math.Max(0, ch - FOOTER_H);
            view.SetBounds(0, 0, cw, bodyH);
            footer.SetBounds(0, bodyH, cw, FOOTER_H);
            LayoutFooter();
        }

        private void LayoutFooter()
        {
            int fw = footer.ClientSize.Width;
            grip.Left    = fw - grip.Width - 3;
            fIndent.Left = 4;
            fIndent.Width = Math.Max(20, grip.Left - fIndent.Left - 4);
        }

        private void Grip_Down(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            gripDragging = true; gripDidDrag = false;
            gripStart = Cursor.Position; gripStartSize = this.Size;
        }
        private void Grip_Move(object s, MouseEventArgs e)
        {
            if (!gripDragging) return;
            var now = Cursor.Position;
            int dx = now.X - gripStart.X, dy = now.Y - gripStart.Y;
            if (!gripDidDrag && Math.Abs(dx) + Math.Abs(dy) < 4) return;
            gripDidDrag = true;
            int w = Math.Max(MinimumSize.Width,  gripStartSize.Width  + dx);
            int h = Math.Max(MinimumSize.Height, gripStartSize.Height + dy);
            this.Size = new Size(w, h);
        }
        private void Grip_Up(object s, MouseEventArgs e) { gripDragging = false; gripDidDrag = false; }
    }
}
