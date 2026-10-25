using System;
using System.Drawing;
using System.Windows.Forms;

namespace Aloha
{

    public class BrowserConfigPanel : DafyFrame
    {
        private readonly NetConfig cfg;
        private readonly Action onApply;

        private CheckBox cWebRtcFix, cWebRtcOff,
                         cDnsPrefetch, cPreconnect,
                         cSafeBrowsing, cHyperlinkAudit, cDomainReliability, cComponentUpdate,
                         cBackgroundNet, cSync,
                         cQuic, cReferrers, cMediaDevices;
        private CheckBox cBlockDl, cConfirmDl, cHidePopup, cDlToast;
        private CheckBox cBlockAllPopups;
        private CheckBox cDisableWindowOpen, cBackgroundNewTabs;
        private CheckBox cCoreConsoleTor, cCoreConsoleVpn, cCoreConsoleProxy;
        private CheckBox cSealedTor;
        private CheckBox cFridaSandboxOff;
        private CheckBox cEmbeddedDevTools;
        private CheckBox cSnapGrid;
        private CheckBox cTorLetterbox;
        private TextBox  tDownloadDir;
        private TextBox  tSearch;
        private CheckBox cDoh;
        private TextBox  tDohEndpoint;

        public BrowserConfigPanel(NetConfig config, Action applyCallback)
            : base("OPT-CFG", "Browser Configuration")
        {
            cfg = config;
            onApply = applyCallback;

            Size = new Size(560, 620);
            Font = new Font("Tahoma", 8.25f);

            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB),
                Padding = new Padding(14, 10, 14, 10)
            };
            ClientArea.Controls.Add(scroll);

            var applyStrip = new Panel { Dock = DockStyle.Bottom, Height = 38, BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB) };
            var applyBtn = new RoundButton
            {
                Text = "Apply Settings",
                Width = 150, Height = 24,
                BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB)
            };
            applyBtn.Click += (s, e) => Apply();
            applyStrip.Controls.Add(applyBtn);
            void CenterApply() { applyBtn.Left = Math.Max(8, (applyStrip.ClientSize.Width - applyBtn.Width) / 2); applyBtn.Top = 7; }
            applyStrip.Resize += (s, e) => CenterApply();
            ClientArea.Controls.Add(applyStrip);

            var body = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Color.Black };
            ClientArea.Controls.Add(body);

            CenterApply();

            int y = 6;

            y = Group(scroll, "Search engine", y);
            var lblSearch = new Label
            {
                Text = "Query URL  ({q} = search terms).  Blank = auto: Startpage .onion on Tor, clearnet otherwise.",
                Left = 6, Top = y, Width = 500, Height = 16, AutoSize = false,
                ForeColor = Color.FromArgb(0x40, 0x40, 0x40), BackColor = Color.Transparent,
                Font = new Font("Tahoma", 7.5f)
            };
            scroll.Controls.Add(lblSearch); y += 18;
            tSearch = new TextBox { Left = 6, Top = y, Width = 500, Text = cfg.SearchTemplate ?? "" };
            scroll.Controls.Add(tSearch); y += 28;
            int spy = y;
            var bSearchAuto = new RoundButton { Text = "Auto", Left = 6, Top = spy, Width = 150, Height = 24,
                BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB) };
            bSearchAuto.Click += (s, e) => tSearch.Text = "";
            var bSearchSp = new RoundButton { Text = "Startpage", Left = 162, Top = spy, Width = 150, Height = 24,
                BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB) };
            bSearchSp.Click += (s, e) => tSearch.Text = NetConfig.SearchStartpage;
            var bSearchSpO = new RoundButton { Text = "Startpage (Onion)", Left = 318, Top = spy, Width = 176, Height = 24,
                BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB) };
            bSearchSpO.Click += (s, e) => tSearch.Text = NetConfig.SearchStartpageOnion;
            scroll.Controls.Add(bSearchAuto); scroll.Controls.Add(bSearchSp); scroll.Controls.Add(bSearchSpO);
            y += 32;

            y = Group(scroll, "Proxy  ·  VPN consoles", y);
            cCoreConsoleTor = Check(scroll, "Tor \u2014 tor.exe in its own console with live logs  [cmd]",
                cfg.CoreConsoleTor, ref y);
            cCoreConsoleVpn = Check(scroll, "VPN \u2014 openvpn in its own console with live logs  [cmd]",
                cfg.CoreConsoleVpn, ref y);
            cCoreConsoleProxy = Check(scroll, "Proxy core \u2014 v2ray / xray in its own console with live logs  [cmd]",
                cfg.CoreConsoleProxy, ref y);
            var lblCmd = new Label
            {
                Text = "Also on each window's footer as [cmd]. A process can show a console OR have its\n"
                     + "output captured, not both \u2014 so turning one on empties that core's log inside Aloha.",
                Left = 26, Top = y, Width = 500, Height = 30,
                ForeColor = Color.FromArgb(0x50, 0x50, 0x50), BackColor = Color.Transparent
            };
            scroll.Controls.Add(lblCmd);
            y += 34;

            y = Group(scroll, "WebRTC  ·  IP leak surface", y);
            cWebRtcFix = Check(scroll, "Route WebRTC through proxy only (disable non-proxied UDP)",
                cfg.CfgWebRtcLeakFix, ref y);
            cWebRtcOff = Check(scroll, "Disable WebRTC entirely (breaks video/voice calls)",
                cfg.CfgWebRtcDisableFully, ref y);

            y = Group(scroll, "DNS  ·  prefetch", y);
            cDnsPrefetch = Check(scroll, "Disable DNS prefetching / network prediction",
                cfg.CfgDisableDnsPrefetch, ref y);
            cPreconnect = Check(scroll, "Disable speculative preconnect",
                cfg.CfgDisablePreconnect, ref y);

            y = Group(scroll, "DNS-over-HTTPS", y);
            cDoh = Check(scroll, "Resolve DNS over HTTPS (secure mode)", cfg.DohEnabled, ref y);
            var lblDoh = new Label
            {
                Text = "Resolver template.  NOTE: a SOCKS/Tor proxy resolves DNS itself, so DoH mainly applies in Direct mode.",
                Left = 6, Top = y, Width = 500, Height = 16, AutoSize = false,
                ForeColor = Color.FromArgb(0x40, 0x40, 0x40), BackColor = Color.Transparent,
                Font = new Font("Tahoma", 7.5f)
            };
            scroll.Controls.Add(lblDoh); y += 18;
            tDohEndpoint = new TextBox { Left = 6, Top = y, Width = 372,
                Text = string.IsNullOrEmpty(cfg.DohEndpoint) ? "https://cloudflare-dns.com/dns-query" : cfg.DohEndpoint };
            scroll.Controls.Add(tDohEndpoint);
            var bDohCf = new RoundButton { Text = "Cloudflare", Left = tDohEndpoint.Right + 6, Top = y - 1, Width = 116, Height = 24,
                BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB) };
            bDohCf.Click += (s, e) => tDohEndpoint.Text = "https://cloudflare-dns.com/dns-query";
            scroll.Controls.Add(bDohCf);
            y += 32;

            y = Group(scroll, "Phone-home  ·  telemetry", y);
            cSafeBrowsing = Check(scroll, "Disable Safe Browsing URL lookups (Google)",
                cfg.CfgDisableSafeBrowsing, ref y);
            cHyperlinkAudit = Check(scroll, "Disable hyperlink auditing  <a ping>  beacons",
                cfg.CfgDisableHyperlinkAudit, ref y);
            cDomainReliability = Check(scroll, "Disable Domain Reliability uploads (Google)",
                cfg.CfgDisableDomainReliability, ref y);
            cComponentUpdate = Check(scroll, "Disable component auto-update",
                cfg.CfgDisableComponentUpdate, ref y);

            y = Group(scroll, "Background networking", y);
            cBackgroundNet = Check(scroll, "Disable background networking",
                cfg.CfgDisableBackgroundNet, ref y);
            cSync = Check(scroll, "Disable profile sync",
                cfg.CfgDisableSync, ref y);

            y = Group(scroll, "Protocols  ·  fingerprint surface", y);
            cQuic = Check(scroll, "Disable QUIC (force predictable TCP through proxy)",
                cfg.CfgDisableQuic, ref y);
            cReferrers = Check(scroll, "Send no referrers",
                cfg.CfgReduceReferrers, ref y);
            cMediaDevices = Check(scroll, "Spoof media devices (fake cam/mic — breaks real capture)",
                cfg.CfgDisableMediaDevices, ref y);

            y = Group(scroll, "Downloads", y);
            cBlockDl = Check(scroll, "Block all downloads",
                cfg.BlockDownloads, ref y);
            cConfirmDl = Check(scroll, "Ask before every download (no automatic downloads)",
                cfg.ConfirmDownloads, ref y);
            cHidePopup = Check(scroll, "Hide the browser's built-in download popup",
                cfg.HideDownloadPopup, ref y);
            cDlToast = Check(scroll, "Aloha's own download popup (black/lime, top-right of the page; replaces the engine's)",
                cfg.CfgDownloadToast, ref y);
            cBlockAllPopups = Check(scroll, "Block ALL new tabs/windows from every site (queues them for review)",
                cfg.CfgBlockAllPopups, ref y);
            cDisableWindowOpen = Check(scroll, "Disable window.open() in JavaScript entirely (may break some sites)",
                cfg.CfgDisableWindowOpen, ref y);
            cBackgroundNewTabs = Check(scroll, "Open new tabs in the background (stay on the current page)",
                cfg.CfgBackgroundNewTabs, ref y);

            var lblDir = new Label
            {
                Text = "Download folder (blank = browser default):", Left = 6, Top = y, AutoSize = true,
                ForeColor = Color.Black, BackColor = Color.Transparent
            };
            scroll.Controls.Add(lblDir); y += 20;
            tDownloadDir = new TextBox { Left = 6, Top = y, Width = 372, Text = cfg.DownloadDir ?? "" };
            var btnBrowse = new RoundButton
            {
                Text = "Browse...", Left = tDownloadDir.Right + 6, Top = y - 1, Width = 84, Height = 24,
                BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB)
            };
            btnBrowse.Click += (s, e) =>
            {
                using (var fb = new FolderBrowserDialog())
                    if (fb.ShowDialog() == DialogResult.OK) tDownloadDir.Text = fb.SelectedPath;
            };
            scroll.Controls.Add(tDownloadDir);
            scroll.Controls.Add(btnBrowse);
            y += 32;

            y = Group(scroll, "Interface", y);
            cSealedTor = Check(scroll, "Sealed tor: route the browser through the sealed Tor (127.0.0.1:9070)",
                cfg.SealedTor, ref y);

            y = Group(scroll, "Diagnostics", y);
            cFridaSandboxOff = Check(scroll,
                "Allow INSTR instruction tracing (--no-sandbox \u2014 weakens renderer sandbox)",
                cfg.CfgAllowFridaSandboxOff, ref y);
            var lblWarn = new Label
            {
                Text = "Required for INSTR \u2192 THREAD/REGISTERS to read the live renderer via Frida.\n"
                     + "Leaves the page renderer un-sandboxed. Enable only while tracing.",
                Left = 24, Top = y, Width = 470, Height = 32, AutoSize = false,
                ForeColor = Color.FromArgb(0x80, 0x40, 0x00), BackColor = Color.Transparent,
                Font = new Font("Tahoma", 7.5f)
            };
            scroll.Controls.Add(lblWarn);
            y += 36;

            cEmbeddedDevTools = Check(scroll,
                "Embedded DevTools (in-frame inspector \u2014 opens a loopback debug port)",
                cfg.CfgEmbeddedDevTools, ref y);
            var lblDev = new Label
            {
                Text = "Opens a CDP port bound to 127.0.0.1 with an OS-assigned port, locked to the\n"
                     + "self-hosted inspector origin so no foreign page can connect. Needs a DevTools\n"
                     + "frontend in .\\devtools\\. Off = the native DevTools window. Applying reloads the engine.",
                Left = 24, Top = y, Width = 470, Height = 44, AutoSize = false,
                ForeColor = Color.FromArgb(0x80, 0x40, 0x00), BackColor = Color.Transparent,
                Font = new Font("Tahoma", 7.5f)
            };
            scroll.Controls.Add(lblDev);
            y += 48;

            y = Group(scroll, "Anti-fingerprint", y);
            cSnapGrid = Check(scroll,
                "Snap viewport to the blueprint grid (report grid-aligned window size)",
                cfg.CfgSnapViewportToGrid, ref y);
            var lblSnap = new Label
            {
                Text = "Rounds the measurable page viewport down to a 16px grid cell so sites\n"
                     + "see coarse, shared dimensions instead of your exact window size.",
                Left = 24, Top = y, Width = 470, Height = 32, AutoSize = false,
                ForeColor = Color.FromArgb(0x40, 0x40, 0x40), BackColor = Color.Transparent,
                Font = new Font("Tahoma", 7.5f)
            };
            scroll.Controls.Add(lblSnap);
            y += 36;

            cTorLetterbox = Check(scroll,
                "Tor letterbox preset (snap to Tor Browser's 200\u00D7100 buckets)",
                cfg.CfgTorLetterbox, ref y);
            var lblTor = new Label
            {
                Text = "Stricter: rounds to the exact 200\u00D7100 sizes Tor Browser uses, so you\n"
                     + "share a window-size bucket with all Tor users. Requires grid snap above.",
                Left = 24, Top = y, Width = 470, Height = 32, AutoSize = false,
                ForeColor = Color.FromArgb(0x40, 0x40, 0x40), BackColor = Color.Transparent,
                Font = new Font("Tahoma", 7.5f)
            };
            scroll.Controls.Add(lblTor);
            y += 36;

            cTorLetterbox.Enabled = cSnapGrid.Checked;
            cSnapGrid.CheckedChanged += (s, e) =>
            {
                cTorLetterbox.Enabled = cSnapGrid.Checked;
                if (!cSnapGrid.Checked) cTorLetterbox.Checked = false;
            };

            const int SB = 17;
            var vbar = new DarkScrollBar(false, light: true) { Dock = DockStyle.None };
            vbar.Left   = ClientArea.ClientSize.Width - SB;
            vbar.Top    = 0;
            vbar.Width  = SB;
            vbar.Height = ClientArea.ClientSize.Height - body.Height - applyStrip.Height;
            vbar.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            ClientArea.Controls.Add(vbar);
            vbar.AttachScrollable(scroll);
            vbar.BringToFront();
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

        private CheckBox Check(Panel host, string text, bool val, ref int y)
        {
            var c = new CheckBox
            {
                Text = text, Left = 6, Top = y, Width = 500, Checked = val,
                ForeColor = Color.Black, BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat
            };
            host.Controls.Add(c);
            y += 24;
            return c;
        }

        private void Apply()
        {
            cfg.CfgWebRtcLeakFix          = cWebRtcFix.Checked;
            cfg.CfgWebRtcDisableFully     = cWebRtcOff.Checked;
            cfg.CfgDisableDnsPrefetch     = cDnsPrefetch.Checked;
            cfg.CfgDisablePreconnect      = cPreconnect.Checked;
            cfg.CfgDisableSafeBrowsing    = cSafeBrowsing.Checked;
            cfg.CfgDisableHyperlinkAudit  = cHyperlinkAudit.Checked;
            cfg.CfgDisableDomainReliability = cDomainReliability.Checked;
            cfg.CfgDisableComponentUpdate = cComponentUpdate.Checked;
            cfg.CfgDisableBackgroundNet   = cBackgroundNet.Checked;
            cfg.CfgDisableSync            = cSync.Checked;
            cfg.CfgDisableQuic            = cQuic.Checked;
            cfg.CfgReduceReferrers        = cReferrers.Checked;
            cfg.CfgDisableMediaDevices    = cMediaDevices.Checked;
            cfg.SealedTor                 = cSealedTor.Checked;
            cfg.CfgAllowFridaSandboxOff   = cFridaSandboxOff.Checked;
            cfg.CfgEmbeddedDevTools       = cEmbeddedDevTools.Checked;
            cfg.CfgSnapViewportToGrid     = cSnapGrid.Checked;
            cfg.CfgTorLetterbox           = cSnapGrid.Checked && cTorLetterbox.Checked;
            cfg.BlockDownloads            = cBlockDl.Checked;
            cfg.ConfirmDownloads          = cConfirmDl.Checked;
            cfg.HideDownloadPopup         = cHidePopup.Checked;
            cfg.CfgDownloadToast          = cDlToast.Checked;
            cfg.CfgBlockAllPopups         = cBlockAllPopups.Checked;
            cfg.CfgDisableWindowOpen      = cDisableWindowOpen.Checked;
            cfg.CfgBackgroundNewTabs      = cBackgroundNewTabs.Checked;
            cfg.CoreConsoleTor            = cCoreConsoleTor.Checked;
            cfg.CoreConsoleVpn            = cCoreConsoleVpn.Checked;
            cfg.CoreConsoleProxy          = cCoreConsoleProxy.Checked;
            cfg.DownloadDir               = (tDownloadDir.Text ?? "").Trim();
            cfg.SearchTemplate            = (tSearch.Text ?? "").Trim();
            cfg.DohEnabled                = cDoh.Checked;
            cfg.DohEndpoint               = (tDohEndpoint.Text ?? "").Trim();
            cfg.Save();
            onApply?.Invoke();
        }
    }
}
