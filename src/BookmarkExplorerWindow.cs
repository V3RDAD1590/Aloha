using System;
using System.Drawing;
using System.Windows.Forms;
using Aloha.RingStoreCore;

namespace Aloha
{

    public class BookmarkExplorerWindow : DafyFrame
    {
        private readonly BookmarkTreeView view;
        private readonly BookmarkManager mgr;
        private readonly string storePath;

        public BookmarkExplorerWindow(BookmarkManager mgr, string bookmarksPath, Action<string> openCallback)
            : base("OPT-BOOK", "Bookmarks")
        {
            this.mgr = mgr; storePath = bookmarksPath;
            Size = new Size(560, 560);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(160, 160);
            ShowInTaskbar = true;
            ClientArea.BackColor = Color.Black;

            view = new BookmarkTreeView(mgr, bookmarksPath, openCallback) { Dock = DockStyle.Fill };

            view.SelectionChanged += n =>
                SetTitle(string.IsNullOrEmpty(n) ? "Bookmarks" : "Bookmarks \u2014 " + n);
            ClientArea.Controls.Add(view);
            EnableTitleMarquee();

            var bImport = MakeLabeledButton("Import\u2026", ImportFirefox);
            var bExport = MakeLabeledButton("Export\u2026", ExportFirefox);
            Footer.Controls.Add(bImport); Footer.Controls.Add(bExport);
            Action row = () => LayoutFooterRow(bImport, bExport);
            Footer.Resize += (s, e) => row(); Shown += (s, e) => row();
        }

        private void ImportFirefox()
        {
            string start = FirefoxBookmarks.BackupFolder();
            using (var dlg = new OpenFileDialog
            {
                Title = "Import Firefox bookmarks",
                Filter = "Firefox bookmarks (*.json;*.jsonlz4)|*.json;*.jsonlz4|All files|*.*",
                InitialDirectory = start ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                FirefoxBookmarks.ImportResult res;
                try { res = FirefoxBookmarks.Read(dlg.FileName); }
                catch (Exception ex) { MessageBox.Show(this, "Could not read that file:\n" + ex.Message, "Import", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

                var a = MessageBox.Show(this,
                    res.Items.Count + " bookmarks in " + res.Folders + " folders" + (res.Skipped > 0 ? " (" + res.Skipped + " smart bookmarks skipped)" : "") + ".\n\n" +
                    "Yes \u2014 merge them into your bookmarks (duplicates are kept once)\n" +
                    "No \u2014 replace your bookmarks with them (like Firefox's Restore)",
                    "Import Firefox bookmarks", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (a == DialogResult.Cancel) return;
                int added;
                if (a == DialogResult.No) { mgr.Items.Clear(); mgr.Items.AddRange(res.Items); added = res.Items.Count; }
                else added = FirefoxBookmarks.Merge(mgr, res.Items);
                try { mgr.Save(storePath); } catch (Exception ex) { MessageBox.Show(this, "Saving failed: " + ex.Message, "Import"); }
                view.ReloadStore();
                SetTitle("Bookmarks \u2014 " + added + " imported");
            }
        }

        private void ExportFirefox()
        {
            using (var dlg = new SaveFileDialog
            {
                Title = "Export bookmarks for Firefox",
                Filter = "Firefox bookmarks backup (*.json)|*.json",
                FileName = "bookmarks-" + DateTime.Now.ToString("yyyy-MM-dd") + ".json"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    int n = FirefoxBookmarks.Write(dlg.FileName, mgr.Items);
                    SetTitle("Bookmarks \u2014 " + n + " exported");
                }
                catch (Exception ex) { MessageBox.Show(this, "Export failed:\n" + ex.Message, "Export", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        public void FocusView()            { view.FocusTree(); }
        public void Reload()               { view.ReloadStore(); }
        public void NavigateTo(string url) { view.NavigateTo(url); }
    }
}
