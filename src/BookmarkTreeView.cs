using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Aloha.RingStoreCore;

namespace Aloha
{

    public class BookmarkTreeView : Panel
    {
        private static readonly Color Modern = Color.FromArgb(0xF0, 0xF0, 0xF0);
        private static readonly Color Green  = Color.FromArgb(0xB4, 0xDC, 0xBA);
        private static readonly Color Gold   = Color.FromArgb(0xE8, 0xA3, 0x17);
        private static readonly Pen   LinePen = new Pen(Color.FromArgb(0x3E, 0x5C, 0x44));
        private const int SB = 17, MINPEEK = 46, MINTREE = 46;

        private readonly Action<string> openInBrowser;
        private readonly BookmarkManager mgr;
        private readonly string storePath;

        private TreeView tree;
        private TextBox peek;
        private Panel corner;
        private Panel divider;
        private DarkScrollBar treeVbar, treeHbar, peekVbar, peekHbar;
        private string selUrl;
        private bool selIsDomain;
        private int  peekH;
        private bool userSplit;
        private bool divDragging;
        private int  divStartY, divStartPeek;

        public event Action<string> SelectionChanged;

        public event Action<string> SelectionPath;

        private class NodeInfo
        {
            public bool IsDomain;
            public string Domain;
            public string Url;
            public string Title;
            public Bookmark Bm;
        }

        public BookmarkTreeView(BookmarkManager manager, string bookmarksPath, Action<string> openCallback)
        {
            mgr = manager;
            storePath = bookmarksPath;
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
                Indent = 18, ItemHeight = 18, PathSeparator = "/"
            };
            tree.DrawNode             += Tree_DrawNode;
            tree.AfterExpand          += (s, e) => UpdateTreeHbar();
            tree.AfterCollapse        += (s, e) => UpdateTreeHbar();
            tree.AfterSelect          += (s, e) => UpdatePeek(e.Node);
            tree.KeyDown              += Tree_KeyDown;
            tree.NodeMouseClick       += Tree_NodeMouseClick;
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

            BuildTree();
        }

        public void ReloadStore()
        {

            try
            {
                if (mgr != null && !string.IsNullOrEmpty(storePath))
                {
                    var fresh = BookmarkManager.Load(storePath);
                    mgr.Items.Clear();
                    mgr.Items.AddRange(fresh.Items);
                }
            }
            catch { }
            BuildTree();
        }
        public void FocusTree() { try { tree.Focus(); } catch { } }

        public void NavigateTo(string urlOrHost)
        {
            if (string.IsNullOrWhiteSpace(urlOrHost)) return;
            string host = BookmarkManager.HostOf(urlOrHost.Trim());
            string dom = BookmarkManager.RegistrableDomain(host);
            foreach (TreeNode n in tree.Nodes)
            {
                var info = n.Tag as NodeInfo;
                if (info != null && info.IsDomain &&
                    string.Equals(info.Domain, dom, StringComparison.OrdinalIgnoreCase))
                {
                    n.Expand();
                    tree.SelectedNode = n;
                    n.EnsureVisible();
                    return;
                }
            }
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

        private void BuildTree()
        {
            string keepDomain = selIsDomain ? selUrl : DomainOfSelectedLeaf();
            tree.BeginUpdate();
            tree.Nodes.Clear();

            var items = (mgr != null && mgr.Items != null) ? mgr.Items : new List<Bookmark>();
            if (items.Count == 0)
            {
                var empty = new TreeNode("(no bookmarks yet — click the \u2605 in the footer on a page you like)")
                { Tag = new NodeInfo { IsDomain = false } };
                tree.Nodes.Add(empty);
                tree.EndUpdate();
                UpdatePeek(null);
                return;
            }

            var groups = items
                .GroupBy(b => KeyOf(b))
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var g in groups)
            {
                int count = g.Count();
                var dn = new TreeNode(g.Key + "   (" + count + ")")
                { Tag = new NodeInfo { IsDomain = true, Domain = g.Key } };
                foreach (var b in g.OrderBy(x => LeafLabel(x), StringComparer.CurrentCultureIgnoreCase))
                {
                    var info = new NodeInfo { IsDomain = false, Domain = g.Key, Url = b.Url, Title = b.Title, Bm = b };
                    dn.Nodes.Add(new TreeNode(LeafLabel(b)) { Tag = info });
                }
                dn.Expand();
                tree.Nodes.Add(dn);
            }
            tree.EndUpdate();

            if (!string.IsNullOrEmpty(keepDomain))
            {
                foreach (TreeNode n in tree.Nodes)
                {
                    var info = n.Tag as NodeInfo;
                    if (info != null && info.IsDomain &&
                        string.Equals(info.Domain, keepDomain, StringComparison.OrdinalIgnoreCase))
                    { tree.SelectedNode = n.Nodes.Count > 0 ? n.Nodes[0] : n; n.EnsureVisible(); break; }
                }
            }
            if (tree.SelectedNode == null && tree.Nodes.Count > 0)
                tree.SelectedNode = tree.Nodes[0];
        }

        private string DomainOfSelectedLeaf()
        {
            var info = tree.SelectedNode == null ? null : tree.SelectedNode.Tag as NodeInfo;
            return info != null ? info.Domain : null;
        }

        private static string KeyOf(Bookmark b)
        {
            if (b == null) return "";
            if (!string.IsNullOrEmpty(b.Domain)) return b.Domain;
            string host = !string.IsNullOrEmpty(b.Host) ? b.Host : BookmarkManager.HostOf(b.Url);
            string dom = BookmarkManager.RegistrableDomain(host);
            return string.IsNullOrEmpty(dom) ? "(local)" : dom;
        }

        private static string LeafLabel(Bookmark b)
        {
            if (b == null) return "";
            if (!string.IsNullOrEmpty(b.Title)) return b.Title;
            if (!string.IsNullOrEmpty(b.Host)) return b.Host;
            return b.Url ?? "";
        }

        private void Tree_DrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            var g = e.Graphics;
            var b = e.Bounds;
            if (b.Width <= 0 || b.Height <= 0) { e.DrawDefault = false; return; }
            bool sel = (e.State & TreeNodeStates.Selected) != 0;
            var info = e.Node.Tag as NodeInfo;
            bool isDomain = info != null && info.IsDomain;

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
            TextRenderer.DrawText(g, e.Node.Text, tree.Font, b, isDomain ? Gold : Green,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private void Tree_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            var info = e.Node.Tag as NodeInfo;
            if (info == null) return;
            tree.SelectedNode = e.Node;
            if (info.IsDomain)
            {
                if (e.Node.IsExpanded) e.Node.Collapse();
                else e.Node.Expand();
            }
        }

        private void Tree_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            var info = e.Node.Tag as NodeInfo;
            if (info != null && !info.IsDomain && !string.IsNullOrEmpty(info.Url)) OpenBookmark(info.Url);
        }

        private void Tree_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5) { e.Handled = true; ReloadStore(); return; }

            if (e.KeyCode == Keys.Delete)
            {
                var node = tree.SelectedNode;
                var info = node == null ? null : node.Tag as NodeInfo;
                if (info != null && !info.IsDomain && !string.IsNullOrEmpty(info.Url))
                {
                    e.Handled = true; e.SuppressKeyPress = true;
                    DeleteBookmark(info.Url);
                }
                return;
            }

            if (e.KeyCode != Keys.Enter) return;
            e.Handled = true; e.SuppressKeyPress = true;
            var n = tree.SelectedNode;
            var ni = n == null ? null : n.Tag as NodeInfo;
            if (ni == null) return;
            if (ni.IsDomain)
            {
                if (n.IsExpanded) n.Collapse();
                else n.Expand();
            }
            else if (!string.IsNullOrEmpty(ni.Url)) OpenBookmark(ni.Url);
        }

        private void OpenBookmark(string url)
        {
            try { if (!string.IsNullOrEmpty(url)) openInBrowser?.Invoke(url); }
            catch { }
        }

        private void DeleteBookmark(string url)
        {
            if (mgr == null) return;
            mgr.Remove(url);
            try { if (!string.IsNullOrEmpty(storePath)) mgr.Save(storePath); } catch { }
            BuildTree();
        }

        private void UpdatePeek(TreeNode node)
        {
            var info = node == null ? null : node.Tag as NodeInfo;
            if (info == null) { peek.Text = ""; selUrl = null; SelectionChanged?.Invoke(""); SelectionPath?.Invoke(""); return; }

            if (info.IsDomain)
            {
                selIsDomain = true; selUrl = info.Domain;
                SelectionChanged?.Invoke(info.Domain);
                SelectionPath?.Invoke(info.Domain);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(info.Domain);
                sb.AppendLine();
                sb.AppendLine("[" + node.Nodes.Count + (node.Nodes.Count == 1 ? " bookmark]" : " bookmarks]"));
                sb.AppendLine();
                foreach (TreeNode c in node.Nodes)
                {
                    var ci = c.Tag as NodeInfo;
                    if (ci != null) sb.AppendLine("  " + (ci.Title ?? "") + "  —  " + (ci.Url ?? ""));
                }
                peek.Text = sb.ToString();
                peek.SelectionStart = 0; peek.ScrollToCaret();
                return;
            }

            selIsDomain = false; selUrl = info.Url;
            SelectionChanged?.Invoke(LeafLabelOrUrl(info));
            SelectionPath?.Invoke(info.Url ?? "");
            try
            {
                var b = info.Bm;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(string.IsNullOrEmpty(info.Title) ? "(untitled)" : info.Title);
                sb.AppendLine(info.Url ?? "");
                sb.AppendLine();
                sb.AppendLine("host:    " + (b != null && !string.IsNullOrEmpty(b.Host) ? b.Host : BookmarkManager.HostOf(info.Url)));
                sb.AppendLine("domain:  " + info.Domain);
                sb.AppendLine("added:   " + PrettyDate(b != null ? b.AddedUtc : null));
                if (b != null && !string.IsNullOrEmpty(b.Folder))  sb.AppendLine("folder:  " + b.Folder.Replace("/", " \u203A "));
                if (b != null && !string.IsNullOrEmpty(b.Tags))    sb.AppendLine("tags:    " + b.Tags);
                if (b != null && !string.IsNullOrEmpty(b.Keyword)) sb.AppendLine("keyword: " + b.Keyword);
                sb.AppendLine();
                sb.AppendLine("---- pointer ----");
                sb.AppendLine("Live content the address points to — not regenerated.");
                sb.AppendLine("Enter / double-click opens it in the browser.");
                sb.AppendLine("Delete removes it from the store.");
                peek.Text = sb.ToString();
                peek.SelectionStart = 0; peek.ScrollToCaret();
            }
            catch (Exception ex) { peek.Text = (info.Title ?? info.Url) + "\r\n\r\n(" + ex.Message + ")"; }
        }

        private static string LeafLabelOrUrl(NodeInfo info)
        {
            if (info == null) return "";
            if (!string.IsNullOrEmpty(info.Title)) return info.Title;
            return info.Url ?? "";
        }

        private static string PrettyDate(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "(unknown)";
            DateTime dt;
            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out dt))
                return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            return iso;
        }
    }
}
