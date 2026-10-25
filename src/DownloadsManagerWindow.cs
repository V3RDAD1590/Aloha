using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;

namespace Aloha
{
    public sealed class DownloadsManagerWindow : DafyFrame
    {

        internal static readonly Color Lime    = TorrcSyntax.GreenHi;
        internal static readonly Color LimeHi  = TorrcSyntax.GreenTop;
        internal static readonly Color LimeDim = TorrcSyntax.Green;
        internal static readonly Color LimeLo  = Color.FromArgb(0x2A, 0x3E, 0x2E);
        internal static readonly Color SelBg   = Color.FromArgb(0x1E, 0x33, 0x24);
        internal static readonly Color Amber   = Color.FromArgb(0xFF, 0xB6, 0x2E);
        internal static readonly Color Red     = Color.FromArgb(0xFF, 0x1A, 0x1A);
        private static readonly Color Fab      = Color.FromArgb(0xFA, 0xFA, 0xFB);

        private readonly DownloadHistory history;
        private readonly Func<string> downloadFolder;
        private readonly Action<string> openInTab;
        private readonly Action<string> redownload;

        private readonly TextBox query;
        private readonly Label readout, stats, scopeLine;
        private readonly BevelPanel well1, well2;
        private readonly List<Panel> toggles = new List<Panel>();
        private readonly IndexView index;
        private readonly Timer debounce = new Timer { Interval = 180 };
        private DownloadsFolderWindow folders;
        private bool moving, closing;

        internal string Scope;
        private List<DlEntry> all = new List<DlEntry>();

        public DownloadsManagerWindow(DownloadHistory hist, Func<string> dlFolder, Action<string> openTab, Action<string> reget)
            : base("OPT-DLM", "Downloads Manager")
        {
            history = hist; downloadFolder = dlFolder; openInTab = openTab; redownload = reget;
            Size = new Size(560, 490);
            MinimumSize = new Size(470, 380);
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            KeyPreview = true;
            Font = new Font("Tahoma", 8.25f);
            ClientArea.BackColor = Fab;
            HideFooterCubes();

            well1 = new BevelPanel { BackColor = Color.Black };
            var prompt = new Label { Text = "find \u203A", AutoSize = false, ForeColor = LimeDim, BackColor = Color.Black, Font = new Font("Consolas", 9f), TextAlign = ContentAlignment.MiddleLeft };
            query = new TextBox { BorderStyle = BorderStyle.None, BackColor = Color.Black, ForeColor = Lime, Font = new Font("Consolas", 9.5f) };
            readout = new Label { AutoSize = false, ForeColor = LimeDim, BackColor = Color.Black, Font = new Font("Consolas", 8.25f), AutoEllipsis = true };
            well1.Controls.Add(prompt); well1.Controls.Add(query); well1.Controls.Add(readout);
            well1.Resize += (s, e) =>
            {
                int w = well1.ClientSize.Width;
                prompt.SetBounds(4, 3, 44, 17); query.SetBounds(50, 5, w - 56, 16); readout.SetBounds(6, 21, w - 12, 15);
            };

            well2 = new BevelPanel { BackColor = Color.Black };
            stats = new Label { AutoSize = false, ForeColor = Lime, BackColor = Color.Black, Font = new Font("Consolas", 8.25f), AutoEllipsis = true };
            scopeLine = new Label { AutoSize = false, ForeColor = LimeDim, BackColor = Color.Black, Font = new Font("Consolas", 8.25f), AutoEllipsis = true, Cursor = Cursors.Hand };
            scopeLine.Click += (s, e) => SetScope(null);
            well2.Controls.Add(stats); well2.Controls.Add(scopeLine);
            well2.Resize += (s, e) => { int w = well2.ClientSize.Width; stats.SetBounds(6, 4, w - 12, 15); scopeLine.SetBounds(6, 20, w - 12, 15); };

            ClientArea.Controls.Add(well1);
            ClientArea.Controls.Add(well2);

            AddToggle("today", "today"); AddToggle("yday", "yesterday"); AddToggle("7 days", "week"); AddToggle("30 days", "month");
            AddToggle("00\u201306", "00:00-06:00"); AddToggle("06\u201312", "06:00-12:00"); AddToggle("12\u201318", "12:00-18:00"); AddToggle("18\u201324", "18:00-24:00");
            AddToggle("image", "image"); AddToggle("video", "video"); AddToggle("audio", "audio"); AddToggle("doc", "doc");
            AddToggle("archive", "archive"); AddToggle("program", "program"); AddToggle("\u26A0 red", "red");
            AddToggle(">10 MB", ">10mb"); AddToggle(">100 MB", ">100mb"); AddToggle("failed", "failed");
            var clear = MakeLabeledButton("clear", () => { query.Text = ""; query.Focus(); });
            clear.Height = 18; clear.Width = 46;
            toggles.Add(clear); ClientArea.Controls.Add(clear);

            index = new IndexView(this);
            ClientArea.Controls.Add(index);
            index.BringToFront();

            var bFolder = MakeLabeledButton("Open folder", () => OpenFolder(Scope ?? downloadFolder()));
            var bBackup = MakeLabeledButton("Backup folder", () => OpenFolder(BackupDir()));
            Footer.Controls.Add(bFolder); Footer.Controls.Add(bBackup);
            Action row = () => LayoutFooterRow(bFolder, bBackup);
            Footer.Resize += (s, e) => row(); Shown += (s, e) => row();

            query.TextChanged += (s, e) => { debounce.Stop(); debounce.Start(); foreach (var t in toggles) t.Invalidate(); };
            debounce.Tick += (s, e) => { debounce.Stop(); Apply(); };
            ClientArea.Resize += (s, e) => LayoutAll();
            Shown += (s, e) => { LayoutAll(); Reload(); query.Focus(); EnsureFolders(); };
            Activated += (s, e) => Reload();
            history.Changed += OnHistoryChanged;
            FormClosing += (s, e) => closing = true;
            FormClosed += (s, e) =>
            {
                history.Changed -= OnHistoryChanged; debounce.Dispose();
                var f = folders; folders = null;
                try { if (f != null && !f.IsDisposed && !f.Disposing) f.Close(); } catch { }
            };
            KeyDown += OnKey;

            LocationChanged += (s, e) => SyncFolders();
            SizeChanged += (s, e) => SyncFolders();
            Resize += (s, e) => { if (folders != null && !folders.IsDisposed) folders.WindowState = WindowState == FormWindowState.Minimized ? FormWindowState.Minimized : FormWindowState.Normal; };

            CleanDragZips();
        }

        private void EnsureFolders()
        {
            if (folders != null && !folders.IsDisposed) return;
            folders = new DownloadsFolderWindow(this);

            folders.FormClosed += (s, e) =>
            {
                folders = null;
                if (!closing && !IsDisposed && !Disposing) try { BeginInvoke((Action)(() => { if (!IsDisposed && !closing) Close(); })); } catch { }
            };
            SyncFolders();
            folders.Show(MainWindow);
            folders.SetRoot(Scope ?? downloadFolder());
        }

        private void SyncFolders()
        {
            if (moving || folders == null || folders.IsDisposed || WindowState != FormWindowState.Normal) return;
            moving = true;
            try { folders.SetBounds(Right, Top, folders.Width, Height); }
            finally { moving = false; }
        }

        internal void FollowFolders(Rectangle r)
        {
            if (moving) return;
            moving = true;
            try { SetBounds(r.Left - Width, r.Top, Width, r.Height); }
            finally { moving = false; }
        }

        private void OnHistoryChanged()
        {
            if (IsDisposed) return;
            try { BeginInvoke((Action)(() => { if (!IsDisposed) { debounce.Stop(); debounce.Start(); needReload = true; } })); } catch { }
        }
        private bool needReload;

        internal string DownloadFolder() { return downloadFolder() ?? ""; }
        internal string BackupDir() { return Path.Combine(DownloadFolder(), "backup"); }

        private IEnumerable<string> Folders()
        {
            if (Scope != null) { yield return Scope; yield break; }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in new[] { downloadFolder(), BackupDir() })
                if (!string.IsNullOrEmpty(d) && seen.Add(d)) yield return d;
        }

        internal void Reload()
        {
            List<DownloadRecord> snap;
            lock (history.Items) snap = new List<DownloadRecord>(history.Items);
            all = DlIndexBuilder.Build(snap, Folders());
            if (Scope != null) all = all.Where(e => e.Dir.Equals(Scope, StringComparison.OrdinalIgnoreCase)).ToList();
            needReload = false;
            Apply();
        }

        private void Apply()
        {
            if (needReload) { Reload(); return; }
            var q = DlQuery.Parse(query.Text);
            var hits = q.Empty ? all.ToList() : all.Where(e => q.Match(e, n => DownloadToast.DangerousExt(n) != null)).ToList();
            index.SetItems(hits);
            readout.Text = q.Empty ? "13:00-14:00 \u00B7 today \u00B7 >10mb \u00B7 .pdf \u00B7 image \u00B7 site:github.com \u00B7 -word"
                                   : "\u2192 " + string.Join(" \u00B7 ", q.Understood);
            readout.ForeColor = q.Empty ? LimeLo : LimeDim;
            long total = hits.Sum(e => e.Size);
            var dated = hits.Where(e => e.When > DateTime.MinValue).ToList();
            stats.Text = hits.Count + (hits.Count == 1 ? " file" : " files") + " \u00B7 " + DownloadToast.Human(total)
                       + (all.Count != hits.Count ? " (of " + all.Count + ")" : "")
                       + (dated.Count > 0 ? " \u00B7 " + Stamp(dated.Min(e => e.When)) + " \u2192 " + Stamp(dated.Max(e => e.When)) : "");
            scopeLine.Text = "\u25B8 " + (Scope ?? "all download folders") + (Scope != null ? "   (click: all folders)" : "") + " \u00B7 sorted by " + index.SortLabel;
        }

        internal static string Stamp(DateTime t)
        {
            if (t.Date == DateTime.Today) return t.ToString("HH:mm") + " today";
            if (t.Date == DateTime.Today.AddDays(-1)) return t.ToString("HH:mm") + " yday";
            return t.ToString(t.Year == DateTime.Today.Year ? "d MMM HH:mm" : "d MMM yyyy");
        }

        internal void SetScope(string dir)
        {
            Scope = string.IsNullOrEmpty(dir) ? null : dir;
            Reload();
        }

        internal void SelectPath(string path) { index.SelectPath(path); }

        private void AddToggle(string label, string token)
        {
            var t = MakeFooterToggle(label, () => HasToken(token), on => SetToken(token, on));
            t.Height = 18;
            t.Width = Math.Max(40, TextRenderer.MeasureText(label, new Font("Lucida Console", 7f, FontStyle.Bold)).Width + 10);
            toggles.Add(t);
            ClientArea.Controls.Add(t);
        }

        private List<string> Toks() { return (query.Text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList(); }
        private bool HasToken(string tok) { return Toks().Any(x => x.Equals(tok, StringComparison.OrdinalIgnoreCase)); }
        private void SetToken(string tok, bool on)
        {
            var t = Toks().Where(x => !x.Equals(tok, StringComparison.OrdinalIgnoreCase)).ToList();
            if (on) t.Add(tok);
            query.Text = string.Join(" ", t);
            query.SelectionStart = query.TextLength;
        }

        private void LayoutAll()
        {
            int w = ClientArea.ClientSize.Width, h = ClientArea.ClientSize.Height, m = 6;

            int half = (w - 2 * m - 4) / 2;
            well1.SetBounds(m, m, half, 40);
            well2.SetBounds(m + half + 4, m, w - 2 * m - half - 4, 40);
            int x = m, y = well1.Bottom + 6;
            foreach (var t in toggles)
            {
                if (x + t.Width > w - m) { x = m; y += t.Height + 3; }
                t.Location = new Point(x, y); x += t.Width + 3;
            }
            y += 18 + 6;
            index.SetBounds(0, y, w, Math.Max(40, h - y));
        }

        private void OnKey(object s, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.F) { query.Focus(); query.SelectAll(); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape) { if (query.TextLength > 0 && query.Focused) query.Text = ""; else Close(); e.Handled = true; }
            else if (e.KeyCode == Keys.Down && query.Focused) { index.Focus(); index.MoveSel(1, false); e.Handled = true; }
        }

        internal void Open(DlEntry e)
        {
            if (e == null || !e.Exists) return;
            if (DownloadToast.DangerousExt(e.Name) != null &&
                MessageBox.Show(this, "." + DownloadToast.DangerousExt(e.Name) + " files can run code just by being opened.\r\n\r\nOpen " + e.Name + " anyway?",
                    "Aloha \u2014 open file", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Path) { UseShellExecute = true }); } catch (Exception ex) { Flash(ex.Message); }
        }

        internal void Reveal(DlEntry e)
        {
            if (e == null) return;
            try
            {
                if (e.Exists) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + e.Path + "\"");
                else OpenFolder(e.Dir);
            }
            catch { }
        }

        private static void OpenFolder(string dir)
        {
            try { if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\""); } } catch { }
        }

        internal void Backup(IList<DlEntry> sel)
        {
            int n = 0; string dir = BackupDir();
            try { Directory.CreateDirectory(dir); } catch (Exception ex) { Flash("backup: " + ex.Message); return; }
            foreach (var e in sel)
            {
                if (!e.Exists || e.Dir.Equals(dir, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Copy(e.Path, Unique(Path.Combine(dir, e.Name))); n++; } catch (Exception ex) { Flash("backup: " + ex.Message); }
            }
            if (n > 0) { Flash(n + (n == 1 ? " file" : " files") + " copied to " + dir); Reload(); folders?.RefreshTree(); }
        }

        internal void CopyToClipboard(IList<DlEntry> sel)
        {
            var sc = new StringCollection();
            foreach (var e in sel) if (e.Exists) sc.Add(e.Path);
            if (sc.Count == 0) return;
            try { Clipboard.SetFileDropList(sc); Flash(sc.Count + (sc.Count == 1 ? " file" : " files") + " on the clipboard \u2014 paste anywhere"); } catch { }
        }

        internal void Forget(IList<DlEntry> sel)
        {
            int n = 0;
            lock (history.Items) foreach (var e in sel) if (e.Rec != null && history.Items.Remove(e.Rec)) n++;
            if (n > 0) { history.Save(); history.RaiseChanged(); Flash(n + " history " + (n == 1 ? "line" : "lines") + " forgotten (files untouched)"); }
        }

        internal void VirusTotal(DlEntry e)
        {
            if (e == null || !e.Exists || openInTab == null) return;
            Flash("hashing " + e.Name + " \u2026");
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    string sha;
                    using (var s = new FileStream(e.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        sha = await Aloha.Media.HashEngine.ComputeAsync(s, "SHA-256", s.Length);
                    BeginInvoke((Action)(() => { openInTab("https://www.virustotal.com/gui/file/" + sha.ToLowerInvariant()); Flash("SHA-256 " + sha.Substring(0, 16) + "\u2026 \u2192 VirusTotal"); }));
                }
                catch (Exception ex) { try { BeginInvoke((Action)(() => Flash("hash failed: " + ex.Message))); } catch { } }
            });
        }

        internal void Exif(DlEntry e)
        {
            if (e == null || !e.Exists) return;
            new ExifWindow(e.Path, BackupDir(), openInTab, written => { Reload(); folders?.RefreshTree(); SelectPath(written); }).Show(MainWindow);
        }

        internal void Redownload(DlEntry e) { if (e != null && !string.IsNullOrEmpty(e.Url) && redownload != null) redownload(e.Url); }

        internal string[] DragPaths(IList<DlEntry> sel, bool zip)
        {
            var files = sel.Where(e => e.Exists).Select(e => e.Path).ToList();
            if (files.Count == 0) return new string[0];
            if (!zip) return files.ToArray();
            try
            {
                string dir = DragZipDir(); Directory.CreateDirectory(dir);
                string name = files.Count == 1 ? Path.GetFileNameWithoutExtension(files[0]) : "aloha-" + files.Count + "-files";
                string zipPath = Path.Combine(dir, name + ".zip");
                if (File.Exists(zipPath)) File.Delete(zipPath);
                using (var z = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var f in files)
                    {
                        string entry = Path.GetFileName(f), stem = Path.GetFileNameWithoutExtension(f), ext = Path.GetExtension(f);
                        for (int i = 1; !used.Add(entry); i++) entry = stem + " (" + i + ")" + ext;
                        z.CreateEntryFromFile(f, entry, CompressionLevel.Optimal);
                    }
                }
                return new[] { zipPath };
            }
            catch (Exception ex) { Flash("zip failed: " + ex.Message); return files.ToArray(); }
        }

        private static string DragZipDir() { return Path.Combine(AlohaPaths.Root, "dragzip"); }
        private static void CleanDragZips()
        {
            try { foreach (var f in Directory.GetFiles(DragZipDir(), "*.zip")) if ((DateTime.Now - File.GetLastWriteTime(f)).TotalHours > 24) File.Delete(f); } catch { }
        }

        internal static string Unique(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path), stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
            for (int i = 1; i < 10000; i++) { string p = Path.Combine(dir, stem + " (" + i + ")" + ext); if (!File.Exists(p)) return p; }
            return path;
        }

        private Timer flashTimer;
        internal void Flash(string msg)
        {
            stats.Text = msg; stats.ForeColor = Amber;
            flashTimer?.Dispose();
            flashTimer = new Timer { Interval = 3500 };
            flashTimer.Tick += (s, e) => { flashTimer.Stop(); stats.ForeColor = Lime; Apply(); };
            flashTimer.Start();
        }

        private sealed class IndexView : Panel
        {
            private readonly DownloadsManagerWindow W;
            private readonly DarkScrollBar bar = new DarkScrollBar(false, light: true) { Dock = DockStyle.None };
            private readonly Font fRow = new Font("Consolas", 8.75f), fHead = new Font("Tahoma", 7.5f, FontStyle.Bold), fIcon = new Font("Consolas", 7.5f, FontStyle.Bold);
            private List<DlEntry> items = new List<DlEntry>();
            private readonly HashSet<DlEntry> sel = new HashSet<DlEntry>();
            private DlEntry anchor, hover;
            private int top;
            private const int HEAD = 18, ROW = 17, SB = 17;
            private string sortKey = "when"; private bool sortDesc = true;
            private bool zipMode;
            private Rectangle zipIcon, bakIcon;
            private Point downAt; private bool armed;

            public IndexView(DownloadsManagerWindow w)
            {
                W = w;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                BackColor = Color.Black; TabStop = true;
                Controls.Add(bar);
                bar.ValueChanged += (s, e) => { top = bar.Value; Invalidate(); };
                Resize += (s, e) => { bar.SetBounds(Width - SB, 0, SB, Height); Model(); };
            }

            public string SortLabel { get { return (sortKey == "when" ? "date" : sortKey) + (sortDesc ? " \u2193" : " \u2191"); } }
            private int Visible_ { get { return Math.Max(1, (Height - HEAD) / ROW); } }
            private void Model() { bar.SetValueModel(items.Count, Visible_); top = Math.Max(0, Math.Min(top, Math.Max(0, items.Count - Visible_))); bar.Value = top; Invalidate(); }

            public void SetItems(List<DlEntry> list)
            {
                items = list; Sort();
                sel.RemoveWhere(e => !items.Contains(e));
                Model();
            }

            private void Sort()
            {
                Comparison<DlEntry> c;
                switch (sortKey)
                {
                    case "name": c = (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); break;
                    case "size": c = (a, b) => a.Size.CompareTo(b.Size); break;
                    case "from": c = (a, b) => string.Compare(a.Host, b.Host, StringComparison.OrdinalIgnoreCase); break;
                    default: c = (a, b) => a.When.CompareTo(b.When); break;
                }
                items.Sort(sortDesc ? (a, b) => c(b, a) : c);
            }

            public void SelectPath(string path)
            {
                int i = items.FindIndex(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
                if (i < 0) return;
                sel.Clear(); sel.Add(items[i]); anchor = items[i]; EnsureVisible(i); Invalidate();
            }

            private List<DlEntry> Selected() { return items.Where(e => sel.Contains(e)).ToList(); }

            private void Cols(int w, out int xSize, out int xWhen, out int xFrom)
            {
                xFrom = w - 110; xWhen = xFrom - 104; xSize = xWhen - 70;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.Clear(Color.Black);
                int w = Width - SB, xSize, xWhen, xFrom; Cols(w, out xSize, out xWhen, out xFrom);

                using (var b = new SolidBrush(Color.FromArgb(0xF0, 0xF0, 0xF0))) g.FillRectangle(b, 0, 0, w, HEAD);
                using (var p = new Pen(Color.White)) g.DrawLine(p, 0, 0, w, 0);
                using (var p = new Pen(Color.FromArgb(0x80, 0x80, 0x80))) { g.DrawLine(p, 0, HEAD - 1, w, HEAD - 1); foreach (int x in new[] { xSize, xWhen, xFrom }) g.DrawLine(p, x - 4, 2, x - 4, HEAD - 3); }
                Head(g, "name", 6, xSize - 10); Head(g, "size", xSize, 66); Head(g, "when", xWhen, 100); Head(g, "from", xFrom, 104);

                if (items.Count == 0)
                {
                    TextRenderer.DrawText(g, "nothing matches", fRow, new Rectangle(0, HEAD, w, 40), LimeDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    return;
                }
                zipIcon = bakIcon = Rectangle.Empty;
                for (int i = 0; i < Visible_ + 1 && top + i < items.Count; i++)
                {
                    var it = items[top + i];
                    int y = HEAD + i * ROW;
                    bool isSel = sel.Contains(it);
                    if (isSel) using (var b = new SolidBrush(SelBg)) g.FillRectangle(b, 0, y, w, ROW);
                    else if (ReferenceEquals(it, hover)) using (var b = new SolidBrush(Color.FromArgb(0x08, 0x20, 0x10))) g.FillRectangle(b, 0, y, w, ROW);

                    Color c = !it.Exists ? LimeLo : (it.State ?? "").StartsWith("interrupted") ? Amber : (isSel ? LimeHi : Lime);
                    int nameW = xSize - 10 - 6;

                    if (isSel && ReferenceEquals(it, anchor) && it.Exists)
                    {
                        bakIcon = new Rectangle(xSize - 10 - 18, y + 2, 16, ROW - 4);
                        zipIcon = new Rectangle(bakIcon.Left - 30, y + 2, 28, ROW - 4);
                        Icon(g, zipIcon, "zip", zipMode);
                        Icon(g, bakIcon, "\u21EA", false);
                        nameW = zipIcon.Left - 4 - 6;
                    }
                    string red = DownloadToast.DangerousExt(it.Name);
                    TextRenderer.DrawText(g, it.Name, fRow, new Rectangle(6, y, nameW, ROW), red != null ? Red : c, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, it.Size > 0 ? DownloadToast.Human(it.Size) : "\u2014", fRow, new Rectangle(xSize, y, 62, ROW), c, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, it.When > DateTime.MinValue ? Stamp(it.When) : "", fRow, new Rectangle(xWhen, y, 100, ROW), LimeDim, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                    TextRenderer.DrawText(g, !it.Exists ? "missing" : (it.Host.Length > 0 ? it.Host : it.State == "on disk" ? "on disk" : ""), fRow, new Rectangle(xFrom, y, 104, ROW), LimeDim, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                }
            }

            private void Head(Graphics g, string key, int x, int w)
            {
                string t = key + (sortKey == key ? (sortDesc ? " \u25BC" : " \u25B2") : "");
                TextRenderer.DrawText(g, t, fHead, new Rectangle(x, 0, w, HEAD), Color.Black, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            private void Icon(Graphics g, Rectangle r, string text, bool on)
            {
                using (var b = new SolidBrush(on ? Lime : Color.Black)) g.FillRectangle(b, r);
                using (var p = new Pen(on ? Lime : LimeDim)) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
                TextRenderer.DrawText(g, text, fIcon, r, on ? Color.Black : Lime, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            private int RowAt(int y) { if (y < HEAD) return -1; int i = top + (y - HEAD) / ROW; return i < items.Count ? i : -1; }

            private void EnsureVisible(int i)
            {
                if (i < top) top = i; else if (i >= top + Visible_) top = i - Visible_ + 1;
                bar.Value = top; top = bar.Value;
            }

            public void MoveSel(int d, bool extend)
            {
                if (items.Count == 0) return;
                int i = anchor != null ? items.IndexOf(anchor) : -1;
                i = Math.Max(0, Math.Min(items.Count - 1, i + d));
                if (!extend) sel.Clear();
                sel.Add(items[i]); anchor = items[i]; EnsureVisible(i); Invalidate();
                W.folders?.Highlight(items[i].Path);
            }

            protected override void OnMouseWheel(MouseEventArgs e) { bar.Value = top - Math.Sign(e.Delta) * 3; top = bar.Value; Invalidate(); }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int i = RowAt(e.Y); var h = i >= 0 ? items[i] : null;
                if (!ReferenceEquals(h, hover)) { hover = h; Invalidate(); }
                Cursor = (zipIcon.Contains(e.Location) || bakIcon.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;

                if (armed && e.Button == MouseButtons.Left && (Math.Abs(e.X - downAt.X) > 4 || Math.Abs(e.Y - downAt.Y) > 4))
                {
                    armed = false;
                    var pick = Selected();
                    var paths = W.DragPaths(pick, zipMode);
                    if (paths.Length == 0) return;
                    var data = new DataObject(DataFormats.FileDrop, paths);
                    var sc = new StringCollection(); sc.AddRange(paths); data.SetFileDropList(sc);
                    DoDragDrop(data, DragDropEffects.Copy);
                }
            }

            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = null; Invalidate(); }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e); Focus();
                if (e.Y < HEAD && e.Button == MouseButtons.Left) { HeaderClick(e.X); return; }
                if (zipIcon.Contains(e.Location)) { zipMode = !zipMode; W.Flash(zipMode ? "zip ON \u2014 drag the selection out and it arrives as one .zip" : "zip off \u2014 dragging hands over the files themselves"); Invalidate(); return; }
                if (bakIcon.Contains(e.Location)) { W.Backup(Selected()); return; }
                int i = RowAt(e.Y);
                if (i < 0) { if (e.Button == MouseButtons.Left) { sel.Clear(); anchor = null; Invalidate(); } return; }
                var it = items[i];
                if (e.Button == MouseButtons.Right) { if (!sel.Contains(it)) { sel.Clear(); sel.Add(it); anchor = it; } Invalidate(); Menu(e.Location); return; }
                if ((ModifierKeys & Keys.Shift) != 0 && anchor != null)
                {
                    int a = items.IndexOf(anchor), b = i; if (a > b) { int t = a; a = b; b = t; }
                    if ((ModifierKeys & Keys.Control) == 0) sel.Clear();
                    for (int k = a; k <= b; k++) sel.Add(items[k]);
                }
                else if ((ModifierKeys & Keys.Control) != 0) { if (!sel.Remove(it)) sel.Add(it); anchor = it; }
                else if (!sel.Contains(it)) { sel.Clear(); sel.Add(it); anchor = it; }
                else anchor = it;
                armed = true; downAt = e.Location;
                W.folders?.Highlight(it.Path);
                Invalidate();
            }

            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); armed = false; }

            protected override void OnMouseDoubleClick(MouseEventArgs e)
            {
                base.OnMouseDoubleClick(e);
                int i = RowAt(e.Y); if (i >= 0 && !zipIcon.Contains(e.Location) && !bakIcon.Contains(e.Location)) W.Open(items[i]);
            }

            private void HeaderClick(int x)
            {
                int xSize, xWhen, xFrom; Cols(Width - SB, out xSize, out xWhen, out xFrom);
                string k = x >= xFrom - 4 ? "from" : x >= xWhen - 4 ? "when" : x >= xSize - 4 ? "size" : "name";
                if (k == sortKey) sortDesc = !sortDesc; else { sortKey = k; sortDesc = k == "when" || k == "size"; }
                Sort(); Invalidate(); W.Apply();
            }

            protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || k == Keys.PageUp || k == Keys.PageDown || k == Keys.Home || k == Keys.End || base.IsInputKey(k); }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                bool sh = e.Shift;
                switch (e.KeyCode)
                {
                    case Keys.Up: MoveSel(-1, sh); break;
                    case Keys.Down: MoveSel(1, sh); break;
                    case Keys.PageUp: MoveSel(-Visible_, sh); break;
                    case Keys.PageDown: MoveSel(Visible_, sh); break;
                    case Keys.Home: MoveSel(-items.Count, sh); break;
                    case Keys.End: MoveSel(items.Count, sh); break;
                    case Keys.Enter: if (anchor != null) W.Open(anchor); break;
                    case Keys.Delete: W.Forget(Selected()); break;
                    case Keys.Back: W.folders?.Up(); break;
                    case Keys.E: if (anchor != null && anchor.Exists && ExifTool.Supported(anchor.Path)) W.Exif(anchor); break;
                    case Keys.A: if (e.Control) { foreach (var it in items) sel.Add(it); Invalidate(); } break;
                    case Keys.C: if (e.Control) W.CopyToClipboard(Selected()); break;
                    default: return;
                }
                e.Handled = true;
            }

            private void Menu(Point at)
            {
                var pick = Selected(); var one = pick.Count == 1 ? pick[0] : null;
                var m = new ContextMenuStrip { Renderer = new AlohaMenuRenderer(), ShowImageMargin = false, Font = new Font("Tahoma", 8.25f), Padding = new Padding(2) };
                Action<string, bool, Action> add = (t, en, a) => { var mi = new ToolStripMenuItem(t) { Enabled = en }; mi.Click += (s, e) => a(); m.Items.Add(mi); };
                add("Open", one != null && one.Exists, () => W.Open(one));
                add("Show in folder", one != null, () => W.Reveal(one));
                m.Items.Add(new ToolStripSeparator());
                add("Copy file(s)\tCtrl+C", pick.Any(x => x.Exists), () => W.CopyToClipboard(pick));
                add("Copy path", one != null, () => { try { Clipboard.SetText(one.Path); } catch { } });
                add("Copy source URL", one != null && !string.IsNullOrEmpty(one.Url), () => { try { Clipboard.SetText(one.Url); } catch { } });
                m.Items.Add(new ToolStripSeparator());
                add("Back up to \\backup", pick.Any(x => x.Exists), () => W.Backup(pick));
                add(zipMode ? "Drag as files (zip off)" : "Drag as .zip (zip on)", true, () => { zipMode = !zipMode; Invalidate(); });
                add("VirusTotal (hash lookup)", one != null && one.Exists, () => W.VirusTotal(one));
                add("EXIF \u2026 view / strip\tE", one != null && one.Exists && ExifTool.Supported(one.Path), () => W.Exif(one));
                add("Download again", one != null && !string.IsNullOrEmpty(one.Url), () => W.Redownload(one));
                m.Items.Add(new ToolStripSeparator());
                add("Forget in history\tDel", pick.Any(x => x.Rec != null), () => W.Forget(pick));
                m.Closed += (s, e) => BeginInvoke((Action)m.Dispose);
                m.Show(this, at);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { fRow.Dispose(); fHead.Dispose(); fIcon.Dispose(); }
                base.Dispose(disposing);
            }
        }
    }
}
