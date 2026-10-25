using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Aloha
{

    public class FileTreeView : Panel
    {
        private static readonly Color Modern = Color.FromArgb(0xF0, 0xF0, 0xF0);

        private static readonly Color Green  = TorrcSyntax.GreenHi;
        private static readonly Pen   LinePen = new Pen(TorrcSyntax.GreenLo);
        private const int SB = 17, MINPEEK = 46, MINTREE = 46;

        private readonly Action<string> openInBrowser;
        private TreeView tree;
        private TextBox peek;
        private Panel corner;
        private Panel divider;
        private DarkScrollBar treeVbar, treeHbar, peekVbar, peekHbar;
        private string selPath;
        private bool selIsDir;
        private int  peekH;
        private bool userSplit;
        private bool divDragging;
        private int  divStartY, divStartPeek;

        public event Action<string> SelectionChanged;

        public event Action<string> SelectionPath;

        private class NodeInfo { public string Path; public string Name; public bool IsDir; public bool Loaded; }

        public FileTreeView(Action<string> openCallback)
        {
            openInBrowser = openCallback;
            BackColor = Color.Black;

            tree = new TreeView
            {
                BackColor = Color.Black, ForeColor = Green,
                Font = new Font("Consolas", 9.5f), BorderStyle = BorderStyle.None,
                Dock = DockStyle.None,
                DrawMode = TreeViewDrawMode.OwnerDrawText,
                ShowLines = false, ShowPlusMinus = false, ShowRootLines = false,
                HideSelection = false, FullRowSelect = false,
                Indent = 18, ItemHeight = 18, PathSeparator = "\\"
            };
            tree.DrawNode            += Tree_DrawNode;
            tree.BeforeExpand        += Tree_BeforeExpand;
            tree.AfterExpand         += (s, e) => UpdateTreeHbar();
            tree.AfterCollapse       += (s, e) => UpdateTreeHbar();
            tree.AfterSelect         += (s, e) => UpdatePeek(e.Node);
            tree.KeyDown             += Tree_KeyDown;
            tree.NodeMouseClick      += Tree_NodeMouseClick;
            tree.NodeMouseDoubleClick += Tree_NodeMouseDoubleClick;

            peek = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.None, WordWrap = false,
                BackColor = Color.Black, ForeColor = Green,
                Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.None, Dock = DockStyle.None
            };

            treeVbar = new DarkScrollBar(false, light: true) { Dock = DockStyle.None };
            treeHbar = new DarkScrollBar(true,  light: true) { Dock = DockStyle.None };
            peekVbar = new DarkScrollBar(false, light: true) { Dock = DockStyle.None };
            peekHbar = new DarkScrollBar(true,  light: true) { Dock = DockStyle.None };
            corner   = new BevelPanel { BackColor = Modern };
            divider  = new BevelPanel { BackColor = Modern, Cursor = Cursors.SizeNS };
            divider.MouseDown += Div_Down;
            divider.MouseMove += Div_Move;
            divider.MouseUp   += Div_Up;

            Controls.Add(tree);
            Controls.Add(peek);
            Controls.Add(treeVbar);
            Controls.Add(treeHbar);
            Controls.Add(peekVbar);
            Controls.Add(peekHbar);
            Controls.Add(divider);
            Controls.Add(corner);
            this.Resize += (s, e) => { userSplit = false; LayoutMain(); };

            treeVbar.AttachScrollable(tree);
            treeHbar.AttachScrollable(tree);
            peekVbar.Attach(peek);
            peekHbar.Attach(peek);

            LoadRoots();
        }

        public void ReloadRoots() { LoadRoots(); }
        public void GoHomeDir()   { GoHome(); }
        public void FocusTree()   { try { tree.Focus(); } catch { } }

        public void NavigateTo(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            path = path.Trim().Trim('"');
            try
            {
                if (Directory.Exists(path)) ExpandTo(path);
                else if (File.Exists(path))
                {
                    string dir = System.IO.Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) ExpandTo(dir);
                }
            }
            catch { }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) { LayoutMain(); FocusTree(); }
        }

        private int CurrentPeekH()
        {
            int avail = ClientSize.Height;
            int half  = avail / 2;
            if (!userSplit) return half;
            int max = avail - MINTREE;
            return Math.Max(MINPEEK, Math.Min(peekH, max));
        }

        private void LayoutMain()
        {
            int cw = ClientSize.Width;
            int ch = ClientSize.Height;
            if (cw < SB + 4 || ch < MINPEEK + MINTREE) return;
            int ph = CurrentPeekH();
            int treeH = ch - ph;

            tree.SetBounds(0, 0, cw, treeH);
            treeVbar.SetBounds(cw - SB, 0, SB, treeH - SB);
            treeHbar.SetBounds(0, treeH - SB, cw - SB, SB);
            divider.SetBounds(cw - SB, treeH - SB, SB, SB);

            peek.SetBounds(0, treeH, cw - SB, ph);
            peekVbar.SetBounds(cw - SB, treeH, SB, ph - SB);
            peekHbar.SetBounds(0, ch - SB, cw - SB, SB);
            corner.SetBounds(cw - SB, ch - SB, SB, SB);

            treeVbar.BringToFront(); treeHbar.BringToFront();
            peekVbar.BringToFront(); peekHbar.BringToFront(); corner.BringToFront();
            divider.BringToFront();
            UpdateTreeHbar();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SCROLLINFO { public int cbSize, fMask, nMin, nMax, nPage, nPos, nTrackPos; }
        [DllImport("user32.dll")] private static extern bool GetScrollInfo(IntPtr hWnd, int n, ref SCROLLINFO si);
        private const int SB_HORZ = 0, SIF_RANGE = 0x1, SIF_PAGE = 0x2;

        private bool TreeOverflowsH()
        {
            if (tree == null || !tree.IsHandleCreated) return false;
            var si = new SCROLLINFO { cbSize = Marshal.SizeOf(typeof(SCROLLINFO)), fMask = SIF_RANGE | SIF_PAGE };
            if (!GetScrollInfo(tree.Handle, SB_HORZ, ref si)) return false;
            return si.nPage > 0 && (si.nMax - si.nMin) >= si.nPage;
        }

        private void UpdateTreeHbar()
        {
            bool need = TreeOverflowsH();
            if (treeHbar.Visible != need) treeHbar.Visible = need;
        }

        private void Div_Down(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            divDragging = true;
            divStartY = Cursor.Position.Y;
            divStartPeek = CurrentPeekH();
        }
        private void Div_Move(object s, MouseEventArgs e)
        {
            if (!divDragging) return;
            int dy = Cursor.Position.Y - divStartY;
            userSplit = true;
            peekH = divStartPeek - dy;
            LayoutMain();
        }
        private void Div_Up(object s, MouseEventArgs e) { divDragging = false; }

        private void LoadRoots()
        {
            tree.BeginUpdate();
            tree.Nodes.Clear();
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        string label = d.Name +
                            (d.IsReady && !string.IsNullOrEmpty(d.VolumeLabel) ? "  " + d.VolumeLabel : "");
                        tree.Nodes.Add(MakeDirNode(d.RootDirectory.FullName, label));
                    }
                    catch { }
                }
            }
            catch { }
            tree.EndUpdate();
        }

        private void GoHome()
        {
            ExpandTo(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }

        private void ExpandTo(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return;
                string root = System.IO.Path.GetPathRoot(path);
                TreeNode cur = null;
                foreach (TreeNode n in tree.Nodes)
                    if (string.Equals(((NodeInfo)n.Tag).Path, root, StringComparison.OrdinalIgnoreCase)) { cur = n; break; }
                if (cur == null) return;
                cur.Expand();
                string rest = path.Substring(root.Length).Trim('\\');
                if (rest.Length > 0)
                    foreach (var part in rest.Split('\\'))
                    {
                        TreeNode next = null;
                        foreach (TreeNode c in cur.Nodes)
                        {
                            var ci = c.Tag as NodeInfo;
                            if (ci != null && string.Equals(ci.Name, part, StringComparison.OrdinalIgnoreCase)) { next = c; break; }
                        }
                        if (next == null) break;
                        next.Expand();
                        cur = next;
                    }
                tree.SelectedNode = cur;
                cur.EnsureVisible();
            }
            catch { }
        }

        private TreeNode MakeDirNode(string path, string label)
        {
            var info = new NodeInfo { Path = path, Name = label, IsDir = true, Loaded = false };
            var n = new TreeNode(label) { Tag = info };
            n.Nodes.Add(new TreeNode(""));   
            return n;
        }

        private TreeNode MakeFileNode(string path, string name)
        {
            var info = new NodeInfo { Path = path, Name = name, IsDir = false, Loaded = true };
            return new TreeNode(name) { Tag = info };
        }

        private TreeNode MakeLeaf(string text, string path)
        {
            return new TreeNode(text) { Tag = new NodeInfo { Path = path, Name = text, IsDir = false, Loaded = true } };
        }

        private void Tree_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            var info = e.Node.Tag as NodeInfo;
            if (info == null || !info.IsDir || info.Loaded) return;
            info.Loaded = true;
            e.Node.Nodes.Clear();
            try
            {
                foreach (var dir in Directory.GetDirectories(info.Path))
                    try { e.Node.Nodes.Add(MakeDirNode(dir, System.IO.Path.GetFileName(dir))); } catch { }
                foreach (var file in Directory.GetFiles(info.Path))
                    try { e.Node.Nodes.Add(MakeFileNode(file, System.IO.Path.GetFileName(file))); } catch { }
                if (e.Node.Nodes.Count == 0)
                    e.Node.Nodes.Add(MakeLeaf("(empty)", info.Path));
            }
            catch (UnauthorizedAccessException) { e.Node.Nodes.Add(MakeLeaf("(access denied)", info.Path)); }
            catch (Exception ex)               { e.Node.Nodes.Add(MakeLeaf("(" + ex.Message + ")", info.Path)); }
        }

        private void Tree_DrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            var g = e.Graphics;
            var b = e.Bounds;
            if (b.Width <= 0 || b.Height <= 0) { e.DrawDefault = false; return; }
            bool sel = (e.State & TreeNodeStates.Selected) != 0;

            if (e.Node.Level >= 1)
            {
                int ind = tree.Indent;
                int top = b.Top, bot = b.Bottom, mid = b.Top + b.Height / 2;
                int cx = b.Left - ind / 2;
                g.DrawLine(LinePen, cx, top, cx, mid);
                g.DrawLine(LinePen, cx, mid, b.Left - 2, mid);
                if (e.Node.NextNode != null)
                    g.DrawLine(LinePen, cx, mid, cx, bot);

                TreeNode p = e.Node.Parent;
                int colCx = cx - ind;
                while (p != null && p.Level >= 1)
                {
                    if (p.NextNode != null) g.DrawLine(LinePen, colCx, top, colCx, bot);
                    p = p.Parent; colCx -= ind;
                }
            }

            using (var bg = new SolidBrush(sel ? Color.FromArgb(0x1E, 0x33, 0x24) : Color.Black))
                g.FillRectangle(bg, b);
            TextRenderer.DrawText(g, e.Node.Text, tree.Font, b, sel ? TorrcSyntax.GreenTop : Green,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private void Tree_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            var info = e.Node.Tag as NodeInfo;
            if (info == null) return;
            tree.SelectedNode = e.Node;
            if (info.IsDir)
            {
                if (e.Node.IsExpanded) e.Node.Collapse();
                else e.Node.Expand();
            }
        }

        private void Tree_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            var info = e.Node.Tag as NodeInfo;
            if (info != null && !info.IsDir) FullPreview();
        }

        private void Tree_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5) { e.Handled = true; LoadRoots(); return; }
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = true; e.SuppressKeyPress = true;
            var node = tree.SelectedNode;
            var info = node == null ? null : node.Tag as NodeInfo;
            if (info == null) return;
            if (info.IsDir)
            {
                if (node.IsExpanded) node.Collapse();
                else node.Expand();
            }
            else OpenFile(info.Path);
        }

        private void OpenFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                openInBrowser?.Invoke(new Uri(path).AbsoluteUri);
            }
            catch { }
        }

        private void UpdatePeek(TreeNode node)
        {
            var info = node == null ? null : node.Tag as NodeInfo;
            if (info == null) { peek.Text = ""; selPath = null; SelectionChanged?.Invoke(""); SelectionPath?.Invoke(""); return; }
            selPath = info.Path; selIsDir = info.IsDir;
            SelectionChanged?.Invoke(info.Name);
            SelectionPath?.Invoke(info.Path);
            try
            {
                if (info.IsDir)
                {
                    peek.Text = info.Name + "\r\n" + info.Path + "\r\n\r\n[folder]";
                    return;
                }
                if (!File.Exists(info.Path)) { peek.Text = info.Name; return; }
                var fi = new FileInfo(info.Path);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(fi.Name);
                sb.AppendLine(fi.FullName);
                sb.AppendLine();
                sb.AppendLine("size:     " + fi.Length.ToString("N0") + " bytes (" + Human(fi.Length) + ")");
                sb.AppendLine("created:  " + fi.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("modified: " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine();
                sb.AppendLine("---- first bytes ----");
                sb.Append(Preview(info.Path));
                peek.Text = sb.ToString();
                peek.SelectionStart = 0; peek.ScrollToCaret();
            }
            catch (Exception ex) { peek.Text = info.Name + "\r\n\r\n(" + ex.Message + ")"; }
        }

        private static string Human(long n)
        {
            double v = n; string[] u = { "B", "KB", "MB", "GB", "TB" }; int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return v.ToString(i == 0 ? "0" : "0.0") + " " + u[i];
        }

        private static string Preview(string path)
        {
            try
            {
                byte[] buf;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int want = (int)Math.Min(8192, fs.Length);
                    buf = new byte[want];
                    int read = fs.Read(buf, 0, want);
                    if (read < buf.Length) Array.Resize(ref buf, read);
                }
                int nonprint = 0;
                foreach (var x in buf)
                    if (x == 0 || (x < 32 && x != 9 && x != 10 && x != 13)) nonprint++;
                bool binary = buf.Length > 0 && nonprint * 100 / buf.Length > 10;
                if (!binary)
                {
                    string txt = System.Text.Encoding.UTF8.GetString(buf);
                    if (txt.Length > 6000) txt = txt.Substring(0, 6000) + "\r\n...[truncated]";
                    return txt;
                }
                return HexDump(buf, Math.Min(buf.Length, 384));
            }
            catch (Exception ex) { return "(" + ex.Message + ")"; }
        }

        private void FullPreview()
        {
            if (selPath == null || selIsDir) return;
            try
            {
                if (!File.Exists(selPath)) return;
                var fi = new FileInfo(selPath);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(fi.Name);
                sb.AppendLine(fi.FullName);
                sb.AppendLine();
                sb.AppendLine("size:     " + fi.Length.ToString("N0") + " bytes (" + Human(fi.Length) + ")");
                sb.AppendLine("created:  " + fi.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("modified: " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine();
                sb.AppendLine("---- full file ----");
                sb.Append(FullContent(selPath));
                peek.Text = sb.ToString();
                peek.SelectionStart = 0; peek.ScrollToCaret();
            }
            catch (Exception ex) { peek.Text = "(" + ex.Message + ")"; }
        }

        private static string FullContent(string path)
        {
            const long TEXT_CAP = 8L * 1024 * 1024;
            const int  HEX_CAP  = 256 * 1024;
            try
            {
                byte[] head;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int want = (int)Math.Min(4096, fs.Length);
                    head = new byte[want];
                    fs.Read(head, 0, want);
                }
                int nonprint = 0;
                foreach (var x in head)
                    if (x == 0 || (x < 32 && x != 9 && x != 10 && x != 13)) nonprint++;
                bool binary = head.Length > 0 && nonprint * 100 / head.Length > 10;

                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (!binary)
                    {
                        int want = (int)Math.Min(TEXT_CAP, fs.Length);
                        var buf = new byte[want];
                        int read = fs.Read(buf, 0, want);
                        string txt = System.Text.Encoding.UTF8.GetString(buf, 0, read);
                        if (fs.Length > TEXT_CAP)
                            txt += "\r\n...[showing first " + Human(TEXT_CAP) + " of " + Human(fs.Length) + "]";
                        return txt;
                    }
                    else
                    {
                        int want = (int)Math.Min(HEX_CAP, fs.Length);
                        var buf = new byte[want];
                        int read = fs.Read(buf, 0, want);
                        string dump = HexDump(buf, read);
                        if (fs.Length > HEX_CAP)
                            dump += "\r\n...[showing first " + Human(HEX_CAP) + " of " + Human(fs.Length) + "]";
                        return dump;
                    }
                }
            }
            catch (Exception ex) { return "(" + ex.Message + ")"; }
        }

        private static string HexDump(byte[] buf, int count)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < count; i += 16)
            {
                sb.Append(i.ToString("X4")).Append("  ");
                int line = Math.Min(16, count - i);
                for (int j = 0; j < 16; j++)
                {
                    if (j < line) sb.Append(buf[i + j].ToString("X2")).Append(' ');
                    else sb.Append("   ");
                    if (j == 7) sb.Append(' ');
                }
                sb.Append(' ');
                for (int j = 0; j < line; j++)
                {
                    byte x = buf[i + j];
                    sb.Append(x >= 32 && x < 127 ? (char)x : '.');
                }
                sb.Append("\r\n");
            }
            return sb.ToString();
        }
    }
}
