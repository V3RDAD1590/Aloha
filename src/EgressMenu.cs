using System;
using System.Windows.Forms;
using Aloha.Net;

namespace Aloha.Net
{
    public static class EgressMenu
    {
        public const string BrowserConfigLabel = "browser config"; 

        public static void Wire(ToolStripMenuItem editMenu, Action<string> navigate)
        {
            if (editMenu == null || navigate == null) return;

            foreach (ToolStripItem existing in editMenu.DropDownItems)
                if (existing is ToolStripMenuItem && existing.Text == "WebView config") return;

            var webViewConfig = new ToolStripMenuItem("WebView config", null,
                (s, e) => navigate("aloha://config"));
            webViewConfig.ToolTipText = "Chromium / WebView flags (aloha://config)";

            var guard = new ToolStripMenuItem("Bridge guard (foreign pages blocked)")
            {
                CheckOnClick = true,
                Checked = BridgeGuard.Enabled,
                ToolTipText = "Only Aloha's own internal pages may reach the config/profile bridge. Recommended on."
            };
            guard.CheckedChanged += (s, e) => BridgeGuard.Enabled = guard.Checked;

            var seal = new ToolStripMenuItem("Sealed egress (block all off-machine)")
            {
                CheckOnClick = true,
                Checked = EgressGate.Current.Enabled,
                ToolTipText = "Lockdown — nothing leaves except loopback/allowlist. Blocks normal browsing while on."
            };
            seal.CheckedChanged += (s, e) => EgressGate.Current.Enabled = seal.Checked;

            var fw = new ToolStripMenuItem("Renderer firewall lock")
            {
                Checked = EgressFirewall.IsActive(),
                ToolTipText = "Block the renderer's direct sockets at the OS level (needs admin, one UAC). Proxied browsing keeps working."
            };
            fw.Click += (s, e) =>
            {
                bool ok = fw.Checked ? EgressFirewall.Remove() : EgressFirewall.Install();
                if (ok) fw.Checked = !fw.Checked;
            };

            var enableAll = new ToolStripMenuItem("Enable all")
            {
                ToolTipText = "Bridge guard on, egress sealed (browsing blocked until you unseal), firewall lock installed."
            };
            enableAll.Click += (s, e) =>
            {
                BridgeGuard.Enabled = true;         guard.Checked = true;
                EgressGate.Current.Enabled = true;  seal.Checked = true;
                if (!fw.Checked && EgressFirewall.Install()) fw.Checked = true;
            };

            var disableAll = new ToolStripMenuItem("Disable all")
            {
                ToolTipText = "Turn every guard off — back to normal browsing."
            };
            disableAll.Click += (s, e) =>
            {
                BridgeGuard.Enabled = false;        guard.Checked = false;
                EgressGate.Current.Enabled = false; seal.Checked = false;
                if (fw.Checked && EgressFirewall.Remove()) fw.Checked = false;
            };

            var sec = new ToolStripMenuItem("Security");
            sec.DropDownItems.Add(guard);
            sec.DropDownItems.Add(seal);
            sec.DropDownItems.Add(fw);
            sec.DropDownItems.Add(new ToolStripSeparator());
            sec.DropDownItems.Add(enableAll);
            sec.DropDownItems.Add(disableAll);

            int at = IndexOfText(editMenu, BrowserConfigLabel);
            if (at >= 0)
            {
                editMenu.DropDownItems.Insert(at + 1, webViewConfig);
                editMenu.DropDownItems.Insert(at + 2, sec);
            }
            else
            {
                editMenu.DropDownItems.Add(webViewConfig);
                editMenu.DropDownItems.Add(sec);
            }
        }

        private static int IndexOfText(ToolStripMenuItem menu, string contains)
        {
            for (int i = 0; i < menu.DropDownItems.Count; i++)
            {
                var t = menu.DropDownItems[i].Text;
                if (!string.IsNullOrEmpty(t) && t.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            }
            return -1;
        }
    }
}
