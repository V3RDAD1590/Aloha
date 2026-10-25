using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Aloha
{
    public sealed class ExifWindow : DafyFrame
    {
        private static readonly Color Lime = DownloadsManagerWindow.Lime, Dim = DownloadsManagerWindow.LimeDim, Lo = DownloadsManagerWindow.LimeLo, Red = DownloadsManagerWindow.Red;

        private string path;
        private readonly string backupDir;
        private readonly Action<string> openInTab;
        private readonly Action<string> onWritten;
        private ExifReport rep;
        private readonly Sheet sheet;
        private readonly Panel bMap, bCopy, bInPlace;
        private string note;

        public ExifWindow(string file, string backupFolder, Action<string> openTab, Action<string> written)
            : base("OPT-EXIF", "EXIF")
        {
            path = file; backupDir = backupFolder; openInTab = openTab; onWritten = written;
            Size = new Size(470, 440);
            MinimumSize = new Size(360, 240);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true; KeyPreview = true;
            ClientArea.BackColor = Color.Black;

            var canvas = SetupAlohaScroll(Color.Black);
            sheet = new Sheet(this) { BackColor = Color.Black, Left = 0, Top = 0 };
            canvas.Controls.Add(sheet);

            bCopy = MakeLabeledButton("Strip → copy", () => DoStrip(false));
            bInPlace = MakeLabeledButton("Strip original", () => DoStrip(true));
            bMap = MakeLabeledButton("Map", () => { if (rep != null && rep.Lat.HasValue && openInTab != null) openInTab(string.Format(System.Globalization.CultureInfo.InvariantCulture, "https://www.openstreetmap.org/?mlat={0}&mlon={1}#map=15/{0}/{1}", rep.Lat, rep.Lon)); });
            foreach (var b in new[] { bCopy, bInPlace, bMap }) Footer.Controls.Add(b);
            Action row = () => LayoutFooterRow(bCopy, bInPlace, bMap);
            Footer.Resize += (s, e) => row(); Shown += (s, e) => row();

            ClientArea.Resize += (s, e) => Relayout();
            Shown += (s, e) => Relayout();
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; } };
            Load_(file);
        }

        public void Load_(string file)
        {
            path = file;
            SetTitle("EXIF  ·  " + Path.GetFileName(file));
            rep = ExifTool.Read(file);
            bool can = rep.CanStrip && rep.MetaBytes > 0;
            bCopy.Enabled = can; bInPlace.Enabled = can; bMap.Enabled = rep.Lat.HasValue;
            foreach (var b in new[] { bCopy, bInPlace, bMap }) { b.ForeColor = b.Enabled ? Color.Black : Color.Gray; b.Invalidate(); }
            Relayout();
        }

        private void DoStrip(bool inPlace)
        {
            if (rep == null || !rep.CanStrip) return;
            try
            {
                string dir = Path.GetDirectoryName(path), stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
                if (!inPlace)
                {
                    string dst = DownloadsManagerWindow.Unique(Path.Combine(dir, stem + " (no exif)" + ext));
                    long gone = ExifTool.Strip(path, dst);
                    note = "wrote " + Path.GetFileName(dst) + " · " + DownloadToast.Human(gone) + " of metadata removed";
                    if (onWritten != null) onWritten(dst);
                    Load_(dst);
                }
                else
                {
                    if (MessageBox.Show(this, "Strip the metadata from the original?\r\n\r\nThe untouched original is copied to\r\n" + backupDir + "\r\nfirst.",
                            "Aloha — strip EXIF", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    Directory.CreateDirectory(backupDir);
                    string bak = DownloadsManagerWindow.Unique(Path.Combine(backupDir, Path.GetFileName(path)));
                    File.Copy(path, bak);
                    string tmp = path + ".exif-tmp";
                    long gone = ExifTool.Strip(path, tmp);
                    File.Delete(path); File.Move(tmp, path);
                    note = DownloadToast.Human(gone) + " of metadata removed · original kept in backup\\" + Path.GetFileName(bak);
                    if (onWritten != null) onWritten(path);
                    Load_(path);
                }
            }
            catch (Exception ex) { note = "strip failed: " + ex.Message; Relayout(); }
        }

        private void Relayout()
        {
            if (sheet == null) return;
            int w = Math.Max(340, AlohaContent.Width);
            sheet.Width = w;
            using (var g = sheet.CreateGraphics()) sheet.Height = Math.Max(AlohaContent.Height, sheet.Draw(g, w, false));
            sheet.Invalidate();
        }

        private sealed class Sheet : Panel
        {
            private readonly ExifWindow W;
            private readonly Font f = new Font("Consolas", 9f), fb = new Font("Consolas", 9f, FontStyle.Bold);
            public Sheet(ExifWindow w)
            {
                W = w;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }
            protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Color.Black); Draw(e.Graphics, Width, true); }

            public int Draw(Graphics g, int w, bool paint)
            {
                int y = 10, P = 12, LH = 16;
                var r = W.rep; if (r == null) return 40;
                Action<string, Color, Font> line = (t, c, ft) =>
                {
                    int h = TextRenderer.MeasureText(g, t, ft, new Size(w - 2 * P, 1000), TextFormatFlags.WordBreak).Height;
                    if (paint) TextRenderer.DrawText(g, t, ft, new Rectangle(P, y, w - 2 * P, h), c, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                    y += Math.Max(LH, h);
                };
                line(Path.GetFileName(W.path), Lime, fb);
                if (r.Error != null) { line(r.Error, DownloadsManagerWindow.Amber, f); return y + 10; }
                int risky = r.Tags.FindAll(t => t.Risk).Count;
                line(r.Format + " · " + r.Tags.Count + " field" + (r.Tags.Count == 1 ? "" : "s") + (risky > 0 ? " · " + risky + " that can identify you" : "")
                     + " · " + DownloadToast.Human(r.MetaBytes) + " of metadata", risky > 0 ? Red : Dim, f);
                if (W.note != null) line(W.note, DownloadsManagerWindow.Amber, f);
                y += 6;
                if (r.Tags.Count == 0) { line("clean — no metadata found", Lime, f); return y + 10; }

                string grp = null; int kw = 150;
                foreach (var t in r.Tags)
                {
                    if (t.Group != grp)
                    {
                        grp = t.Group; y += 4;
                        if (paint) { TextRenderer.DrawText(g, grp.ToUpperInvariant(), f, new Point(P, y), Lo, TextFormatFlags.NoPadding); using (var p = new Pen(Lo)) g.DrawLine(p, P + 90, y + 7, w - P, y + 7); }
                        y += LH;
                    }
                    int h = TextRenderer.MeasureText(g, t.Value ?? "", f, new Size(w - 2 * P - kw, 1000), TextFormatFlags.WordBreak).Height;
                    if (paint)
                    {
                        TextRenderer.DrawText(g, t.Name, f, new Rectangle(P, y, kw - 6, LH), t.Risk ? Red : Dim, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                        TextRenderer.DrawText(g, t.Value ?? "", f, new Rectangle(P + kw, y, w - 2 * P - kw, h), t.Risk ? Red : Lime, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                    }
                    y += Math.Max(LH, h) + 1;
                }
                return y + 12;
            }
            protected override void Dispose(bool disposing) { if (disposing) { f.Dispose(); fb.Dispose(); } base.Dispose(disposing); }
        }
    }
}
