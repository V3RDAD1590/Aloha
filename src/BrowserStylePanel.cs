using System;
using System.Drawing;
using System.Windows.Forms;

namespace Aloha
{

    public class BrowserStylePanel : DafyFrame
    {
        private readonly NetConfig cfg;
        private readonly Action onApply;

        private CheckBox cWallpaper;
        private CheckBox cShowBookmark;
        private CheckBox cMediaCard;
        private CheckBox cWin9xScroll, cUrlHex, cFavicons;

        public BrowserStylePanel(NetConfig config, Action applyCallback)
            : base("OPT-STYL", "Browser Style")
        {
            cfg = config;
            onApply = applyCallback;

            Size = new Size(480, 540);
            Font = new Font("Tahoma", 8.25f);

            var body = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Color.Black };
            ClientArea.Controls.Add(body);

            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB),
                Padding = new Padding(14, 10, 14, 10)
            };
            ClientArea.Controls.Add(scroll);
            scroll.BringToFront();

            int y = 6;
            y = Group(scroll, "Home page  ·  wallpaper", y);

            cWallpaper = new CheckBox
            {
                Text = "Show the Blueprint wallpaper on the home page",
                Left = 6, Top = y, Width = 420, Checked = cfg.WallpaperEnabled,
                ForeColor = Color.Black, BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat
            };
            scroll.Controls.Add(cWallpaper);
            y += 26;

            var note = new Label
            {
                Text = "When off, the home page is a plain dark background.",
                Left = 24, Top = y, Width = 410, Height = 18, AutoSize = false,
                ForeColor = Color.Gray
            };
            scroll.Controls.Add(note);

            y += 22;
            y = Group(scroll, "Address bar  \u00b7  scrollbars", y);

            cWin9xScroll = new CheckBox
            {
                Text = "Aloha browser scrollbars \u2014 lime arrows over the page (laggy)",
                Left = 6, Top = y, Width = 420, Checked = cfg.CfgWin9xBrowserScrollbars,
                ForeColor = Color.Black, BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat
            };
            scroll.Controls.Add(cWin9xScroll);
            y += 26;

            cUrlHex = new CheckBox
            {
                Text = "URL hex strip \u2014 phishing / homograph check under the address bar",
                Left = 6, Top = y, Width = 420, Checked = cfg.CfgUrlHexStrip,
                ForeColor = Color.Black, BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat
            };
            scroll.Controls.Add(cUrlHex);
            y += 26;

            var hexNote = new Label
            {
                Text = "Dumps the URL as hex as you type or paste, naming look-alike characters\n"
                     + "(Cyrillic \u0430 vs Latin a) and invisible ones in red. Click to copy, esc to close.\n"
                     + "On hover it stays quiet unless the host is actually suspect.",
                Left = 26, Top = y, Width = 420, Height = 42,
                ForeColor = Color.FromArgb(0x50, 0x50, 0x50), BackColor = Color.Transparent
            };
            scroll.Controls.Add(hexNote);
            y += 48;

            cFavicons = new CheckBox
            {
                Text = "Load site icons (favicons)",
                Left = 6, Top = y, Width = 420, Checked = cfg.CfgLoadFavicons,
                ForeColor = Color.Black, BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat
            };
            scroll.Controls.Add(cFavicons);
            y += 26;

            var favNote = new Label
            {
                Text = "Off by default, and off means NOT FETCHED. A favicon is a supercookie: the\n"
                     + "browser caches it where a page can't read it but can probe it, tracking you\n"
                     + "across cache clears and incognito. On = icons in tabs; off = the request is\n"
                     + "blocked so nothing is cached to probe.",
                Left = 26, Top = y, Width = 420, Height = 56,
                ForeColor = Color.FromArgb(0x50, 0x50, 0x50), BackColor = Color.Transparent
            };
            scroll.Controls.Add(favNote);
            y += 62;

            y = Group(scroll, "Page  ·  file preview", y);
            cMediaCard = new CheckBox
            {
                Text = "Show the file card on hover (type, size, signature) and the hashes sheet on click",
                Left = 6, Top = y, Width = 440, Checked = cfg.CfgMediaCard,
                ForeColor = Color.Black, BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat
            };
            scroll.Controls.Add(cMediaCard);
            y += 26;

            var mediaNote = new Label
            {
                Text = "Hover an image, link to a file, or video and Aloha reads its first bytes to say\n"
                     + "what it really is. Off = nothing is injected into pages and nothing is read.",
                Left = 26, Top = y, Width = 420, Height = 30,
                ForeColor = Color.FromArgb(0x50, 0x50, 0x50), BackColor = Color.Transparent
            };
            scroll.Controls.Add(mediaNote);
            y += 36;

            y = Group(scroll, "Toolbar", y);
            cShowBookmark = new CheckBox
            {
                Text = "Show the bookmark \u2605 button in the address bar",
                Left = 6, Top = y, Width = 420, Checked = cfg.ShowBookmarkButton,
                ForeColor = Color.Black, BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat
            };
            scroll.Controls.Add(cShowBookmark);
            y += 26;

            SetFooterExtra(MakeLabeledButton("Apply", () => Apply()));
        }

        private int Group(Panel host, string title, int y)
        {
            if (y > 6) y += 8;
            var l = new Label
            {
                Text = title, Left = 0, Top = y, AutoSize = true,
                Font = new Font("Tahoma", 8.25f, FontStyle.Bold),
                ForeColor = Color.Black
            };
            host.Controls.Add(l);
            y += 20;

            return y + 4;
        }

        private void Apply()
        {
            cfg.WallpaperEnabled = cWallpaper.Checked;
            cfg.Wallpaper = "blueprint.svg";   
            cfg.ShowBookmarkButton = cShowBookmark.Checked;
            cfg.CfgWin9xBrowserScrollbars = cWin9xScroll.Checked;
            cfg.CfgUrlHexStrip = cUrlHex.Checked;
            cfg.CfgLoadFavicons = cFavicons.Checked;
            cfg.CfgMediaCard = cMediaCard.Checked;
            cfg.Save();
            onApply?.Invoke();
        }
    }
}
