using System;
using System.Drawing;
using System.Windows.Forms;

namespace Aloha.RingStoreCore
{
    public static class BookmarkButtonGlue
    {

        public static Func<string[]> GetCurrentPage;

        public static Bookmark BookmarkCurrent(BookmarkManager mgr, string storePath)
        {
            if (mgr == null) throw new ArgumentNullException("mgr");
            if (GetCurrentPage == null)
                throw new InvalidOperationException("GetCurrentPage is not wired");
            string[] p = GetCurrentPage();
            if (p == null || p.Length < 1 || string.IsNullOrEmpty(p[0])) return null;
            string url = p[0];
            string title = p.Length > 1 ? p[1] : "";
            Bookmark bm = mgr.AddCurrentSite(url, title);
            mgr.Save(storePath);
            return bm;
        }

        public static Button MakeButton(string text)
        {
            var b = new Button
            {
                Text = text,
                BackColor = SystemColors.Window,
                ForeColor = Color.FromArgb(0x70, 0x70, 0x70),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Symbol", 11f),
                AutoSize = false,
                Width = 22,
                Height = 18,
                TabStop = false,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                Padding = Padding.Empty,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.MiddleCenter
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(0xE8, 0xE8, 0xE8);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(0xD6, 0xD6, 0xD6);
            return b;
        }
    }
}
