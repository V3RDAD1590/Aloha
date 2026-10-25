using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Aloha
{
    public sealed class DownloadsFolderWindow : DafyFrame
    {
        private readonly DownloadsManagerWindow mgr;
        private readonly TreeView tree;
        private readonly Label pathLine;
        private string root;
        private bool following;

        public DownloadsFolderWindow(DownloadsManagerWindow manager) : base("OPT-DLF", "folders")
        {
            mgr = manager;
            Size = new Size(200, 490);
            MinimumSize = new Size(160, 200);
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            KeyPreview = true;
            Font = new Font("Tahoma", 8.25f);
            ClientArea.BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB);
            HideSizeReadout();

            var bUp   = MakeLabeledButton("\u25B2 up", Up);
            var bRef  = MakeLabeledButton("\u27F3", RefreshTree);
            var bHome = MakeLabeledButton("\u2302", () => { SetRoot(null); mgr.SetScope(null); });
            foreach (var b in new[] { bUp, bRef, bHome }) { b.Height = 18; ClientArea.Controls.Add(b); }
            bRef.Width = 26; bHome.Width = 26;

            pathLine = new Label { AutoSize = false, AutoEllipsis = true, ForeColor = Color.FromArgb(0x40, 0x40, 0x40), Font = new Font("Consolas", 7.5f), BackColor = Color.Transparent };
            ClientArea.Controls.Add(pathLine);

            tree = new TreeView
            {
                BackColor = Color.Black, ForeColor = DownloadsManagerWindow.Lime, LineColor = TorrcSyntax.GreenLo,
                BorderStyle = BorderStyle.None, Font = new Font("Consolas", 8.75f), HideSelection = false, FullRowSelect = true,
                ShowLines = true, ShowRootLines = true, ItemHeight = 17
            };
            ClientArea.Controls.Add(tree);

            tree.BeforeExpand += (s, e) => Fill(e.Node);
            tree.NodeMouseClick += (s, e) =>
            {
                tree.SelectedNode = e.Node;
                var p = e.Node.Tag as string; if (p == null) return;
                if (Directory.Exists(p)) mgr.SetScope(p); else mgr.SelectPath(p);
            };
            tree.NodeMouseDoubleClick += (s, e) => { var p = e.Node.Tag as string; if (p != null && Directory.Exists(p)) { SetRoot(p); mgr.SetScope(p); } };
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Back || (e.Alt && e.KeyCode == Keys.Up)) { Up(); e.Handled = true; }
                else if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
            };

            ClientArea.Resize += (s, e) =>
            {
                int w = ClientArea.ClientSize.Width, h = ClientArea.ClientSize.Height;
                bUp.Location = new Point(4, 4); bRef.Location = new Point(bUp.Right + 3, 4); bHome.Location = new Point(bRef.Right + 3, 4);
                pathLine.SetBounds(4, 26, w - 8, 14);
                tree.SetBounds(0, 42, w, Math.Max(20, h - 42));
            };

            LocationChanged += (s, e) => { if (!following && WindowState == FormWindowState.Normal) mgr.FollowFolders(Bounds); };
        }

        public new void SetBounds(int x, int y, int w, int h)
        {
            following = true;
            try { base.SetBounds(x, y, w, h); } finally { following = false; }
        }

        public void SetRoot(string dir)
        {
            root = string.IsNullOrEmpty(dir) ? mgr.DownloadFolder() : dir;
            RefreshTree();
        }

        public void RefreshTree()
        {
            tree.BeginUpdate();
            try
            {
                tree.Nodes.Clear();
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) { pathLine.Text = "(folder not found)"; return; }
                pathLine.Text = root;
                var n = new TreeNode(Path.GetFileName(root.TrimEnd('\\')).Length > 0 ? Path.GetFileName(root.TrimEnd('\\')) : root) { Tag = root };
                tree.Nodes.Add(n);
                Fill(n); n.Expand();
            }
            finally { tree.EndUpdate(); }
        }

        public void Up()
        {
            if (string.IsNullOrEmpty(root)) return;
            var parent = Directory.GetParent(root.TrimEnd('\\'));
            if (parent == null) return;
            root = parent.FullName;
            RefreshTree();
            mgr.SetScope(root);
        }

        public void Highlight(string path)
        {
            foreach (TreeNode n in Walk(tree.Nodes))
                if (string.Equals(n.Tag as string, path, StringComparison.OrdinalIgnoreCase)) { tree.SelectedNode = n; n.EnsureVisible(); return; }
        }

        private static System.Collections.Generic.IEnumerable<TreeNode> Walk(TreeNodeCollection c)
        {
            foreach (TreeNode n in c) { yield return n; foreach (var k in Walk(n.Nodes)) yield return k; }
        }

        private void Fill(TreeNode node)
        {
            var dir = node.Tag as string;
            if (dir == null || !Directory.Exists(dir)) return;
            if (node.Nodes.Count == 1 && node.Nodes[0].Tag == null) node.Nodes.Clear();
            else if (node.Nodes.Count > 0) return;
            try
            {
                foreach (var d in Directory.GetDirectories(dir))
                {
                    try { if ((new DirectoryInfo(d).Attributes & FileAttributes.Hidden) != 0) continue; } catch { continue; }
                    var k = new TreeNode(Path.GetFileName(d)) { Tag = d };
                    k.Nodes.Add(new TreeNode("\u2026"));   
                    node.Nodes.Add(k);
                }
                foreach (var f in Directory.GetFiles(dir))
                {
                    if (f.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase)) continue;
                    var k = new TreeNode(Path.GetFileName(f)) { Tag = f, ForeColor = DownloadDangerColor(f) };
                    node.Nodes.Add(k);
                }
            }
            catch (Exception ex) { node.Nodes.Add(new TreeNode("(" + ex.Message + ")") { ForeColor = DownloadsManagerWindow.Amber }); }
        }

        private static Color DownloadDangerColor(string f)
        {
            return DownloadToast.DangerousExt(Path.GetFileName(f)) != null ? DownloadsManagerWindow.Red : DownloadsManagerWindow.LimeDim;
        }
    }
}
