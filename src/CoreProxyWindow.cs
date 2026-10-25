using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

namespace Aloha
{

    public class CoreProxyWindow : DafyFrame
    {
        private static readonly Color Green = Color.FromArgb(0xB4, 0xDC, 0xBA);
        private static readonly Color Dim   = Color.FromArgb(0x7F, 0xAE, 0x86);

        private readonly NetConfig cfg;
        private readonly Action reinit;
        private readonly string coreName;
        private readonly CoreProxyClient client;

        private List<CoreProxyClient.CoreServer> servers = new List<CoreProxyClient.CoreServer>();
        private readonly Dictionary<string, string> sourceMap = new Dictionary<string, string>();

        private ComboBox cmbSource;
        private TreeView serverTree;
        private const int PAGE = 500;
        private TextBox  logBox;
        private Label    lblStatus;
        private Button   btnConnect, btnDisconnect;
        private CheckBox chkRules;

        private Panel    configHost, paneConfig, paneLink, paneInfo, paneFav;
        private TextBox  txtConfig;
        private TextBox  txtLink;
        private TextBox  txtInfo;
        private ListBox  favList;
        private Label    lblLink;
        private ComboBox cfgSection;
        private string   lastSection;
        private CoreProxyClient.CoreServer selectedServer;
        private bool suppressCfgChange;

        private Panel coreContent;
        private const int MIN_W = 660, MIN_H = 460;

        protected override bool FooterActsAsResizeGrip { get { return false; } }

        private class CoreGroup
        {
            public int Depth;
            public List<CoreProxyClient.CoreServer> Servers;
            public CoreGroup(int d, List<CoreProxyClient.CoreServer> s) { Depth = d; Servers = s; }
        }

        private class CorePage
        {
            public List<TreeNode> Slice;
            public CorePage(List<TreeNode> s) { Slice = s; }
        }

        private bool routed = false;
        private string prevScheme, prevHost;
        private int prevPort;

        public CoreProxyWindow(NetConfig config, Action reinitCb, string core)
            : base("OPT-" + ((core ?? "v2ray").Trim().ToUpperInvariant()), TitleFor(core))
        {
            cfg = config;
            reinit = reinitCb;

            SetFooterExtra(MakeFooterToggle("[cmd]",
                () => cfg != null && cfg.CoreConsoleProxy,
                v  => { if (cfg == null) return; cfg.CoreConsoleProxy = v; try { cfg.Save(); } catch { } }));
            coreName = (core ?? "v2ray").Trim().ToLowerInvariant();
            client = new CoreProxyClient(coreName, 0);

            Size = new Size(920, 560);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            Font = new Font("Tahoma", 8.25f);

            var head = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB) };

            head.Controls.Add(new Label { Text = "Source", Left = 10, Top = 14, Width = 48, Height = 16, AutoSize = false, ForeColor = Color.Black });
            cmbSource = new ComboBox { Left = 62, Top = 11, Width = 372, DropDownStyle = ComboBoxStyle.DropDown };
            BuildSources();
            head.Controls.Add(cmbSource);
            head.Controls.Add(MakeButton("Fetch",   440, 10, 72, DoFetch));
            head.Controls.Add(MakeButton("Bundled", 516, 10, 80, DoBundled));
            head.Controls.Add(MakeButton("Latency", 602, 10, 80, DoLatency));   

            btnConnect    = MakeButton("Connect",    62, 44, 92, DoConnect);
            btnDisconnect = MakeButton("Disconnect",160, 44,104, DoDisconnect);
            head.Controls.Add(btnConnect);
            head.Controls.Add(btnDisconnect);

            chkRules = new CheckBox
            {
                Text = "Iran routing rules (geoip/geosite)",
                Left = 278, Top = 47, AutoSize = true,
                ForeColor = Color.Black, BackColor = Color.Transparent
            };
            chkRules.CheckedChanged += (s, e) => { if (selectedServer != null) RegenConfig(); };
            head.Controls.Add(chkRules);

            lblStatus = new Label
            {
                Left = 10, Top = 74, Width = 660, Height = 16, AutoSize = false,
                ForeColor = Dim, BackColor = Color.Transparent,
                Font = new Font("Tahoma", 8f)
            };
            head.Controls.Add(lblStatus);

            serverTree = new TreeView
            {
                Dock = DockStyle.Fill, BackColor = Color.Black, ForeColor = Green,
                BorderStyle = BorderStyle.None, HideSelection = false,
                ShowLines = false, ShowPlusMinus = true, ShowRootLines = true,
                DrawMode = TreeViewDrawMode.OwnerDrawText, ItemHeight = 18,
                Font = new Font("Consolas", 9f)
            };
            serverTree.DrawNode += ServerTree_DrawNode;
            serverTree.BeforeExpand += ServerTree_BeforeExpand;
            serverTree.AfterSelect += (s, e) =>
            {
                var cs = e.Node != null ? e.Node.Tag as CoreProxyClient.CoreServer : null;
                if (cs != null) { selectedServer = cs; RegenConfig(); }
            };
            serverTree.NodeMouseDoubleClick += (s, e) =>
            {
                var cs = e.Node != null ? e.Node.Tag as CoreProxyClient.CoreServer : null;
                if (cs != null) ShowDetail(cs);
            };
            serverTree.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    var cs = serverTree.SelectedNode != null ? serverTree.SelectedNode.Tag as CoreProxyClient.CoreServer : null;
                    if (cs != null) { DoConnectServer(cs, EditorConfigOrNull()); e.Handled = true; }
                }
            };
            var listHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
            listHost.Controls.Add(serverTree);
            AttachAloha(listHost, serverTree, true);

            configHost = new Panel { Dock = DockStyle.Right, Width = 300, BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB) };

            var cfgStrip = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB) };
            cfgSection = new ComboBox { Left = 6, Top = 3, Width = 198, DropDownStyle = ComboBoxStyle.DropDownList };
            cfgSection.Items.AddRange(new object[] { "Config (JSON)", "Paste a link", "Server details", "Share link", "Favourites" });
            cfgSection.SelectedIndexChanged += (s, e) => RegenConfig();
            cfgSection.SelectedIndex = 0;
            cfgStrip.Controls.Add(cfgSection);
            cfgStrip.Controls.Add(MakeButton("Regen", 300 - 76, 2, 70, RegenConfig));

            var cfgBody = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB) };

            paneConfig = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB), Padding = new Padding(8) };
            txtConfig = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = false,
                ScrollBars = ScrollBars.Vertical, WordWrap = true, BorderStyle = BorderStyle.Fixed3D,
                BackColor = Color.White, ForeColor = Color.Black, Font = new Font("Consolas", 8.5f)
            };
            txtConfig.TextChanged += (s, e) => { if (suppressCfgChange) return;  };
            paneConfig.Controls.Add(txtConfig);

            paneLink = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB), Padding = new Padding(8) };
            txtLink = new TextBox
            {
                Dock = DockStyle.Top, Height = 96, Multiline = true, WordWrap = true,
                ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.Fixed3D,
                BackColor = Color.White, ForeColor = Color.Black, Font = new Font("Consolas", 8.5f)
            };
            lblLink = new Label
            {
                Dock = DockStyle.Top, Height = 34, AutoSize = false,
                ForeColor = Color.FromArgb(0x40, 0x40, 0x40), BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB), Font = new Font("Tahoma", 8.25f),
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 6, 0),
                Text = "paste a share link"
            };
            paneLink.Controls.Add(txtLink);
            paneLink.Controls.Add(lblLink);

            paneInfo = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB), Padding = new Padding(8) };
            txtInfo = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, WordWrap = true, BorderStyle = BorderStyle.Fixed3D,
                BackColor = Color.White, ForeColor = Color.Black, Font = new Font("Consolas", 8.5f)
            };
            paneInfo.Controls.Add(txtInfo);

            paneFav = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB), Padding = new Padding(8) };
            favList = new ListBox
            {
                Dock = DockStyle.Fill, BackColor = Color.White, ForeColor = Color.Black,
                BorderStyle = BorderStyle.Fixed3D, Font = new Font("Consolas", 8.5f), IntegralHeight = false
            };
            favList.DoubleClick += (s, e) => UseFavourite();
            var favBar = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB) };
            favBar.Controls.Add(MakeButton("Use",     4, 3, 56, UseFavourite));
            favBar.Controls.Add(MakeButton("Remove", 64, 3, 70, RemoveFavourite));
            favBar.Controls.Add(MakeButton("Add sel",138, 3, 78, AddSelectedToFav));
            paneFav.Controls.Add(favList);
            paneFav.Controls.Add(favBar);

            cfgBody.Controls.Add(paneConfig);
            cfgBody.Controls.Add(paneLink);
            cfgBody.Controls.Add(paneInfo);
            cfgBody.Controls.Add(paneFav);
            configHost.Controls.Add(cfgBody);
            configHost.Controls.Add(cfgStrip);
            RegenConfig();

            var sep = new Panel { Dock = DockStyle.Right, Width = 1, BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB) };

            logBox = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, WordWrap = true, BorderStyle = BorderStyle.None,
                BackColor = Color.Black, ForeColor = Green, Font = new Font("Consolas", 9f)
            };
            var logHost = new Panel { Dock = DockStyle.Bottom, Height = 116, BackColor = Color.Black };
            logHost.Controls.Add(logBox);
            AttachAloha(logHost, logBox, false, noArrows: true);

            var midHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB) };
            midHost.Controls.Add(listHost);
            midHost.Controls.Add(sep);
            midHost.Controls.Add(configHost);

            var canvas = SetupAlohaScroll(Color.FromArgb(0xFA, 0xFA, 0xFB));
            coreContent = new Panel { BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB), Location = new Point(0, 0) };

            var logGap = new Panel { Dock = DockStyle.Bottom, Height = 32, BackColor = Color.FromArgb(0xFA, 0xFA, 0xFB) };
            coreContent.Controls.Add(midHost);
            coreContent.Controls.Add(head);
            coreContent.Controls.Add(logGap);
            coreContent.Controls.Add(logHost);
            canvas.Controls.Add(coreContent);

            ClientArea.Resize += (s, e) => LayoutCore();
            this.Shown        += (s, e) => LayoutCore();
            LayoutCore();

            client.Log += OnCoreLog;

            UpdateButtons();
            SetStatus("pick a source and Fetch \u2014 or Bundled for offline (." + "\\" + coreName + "\\configs)");

            this.Shown += (s, e) => DoFetch();
            this.FormClosed += (s, e) => { client.Log -= OnCoreLog;  };
        }

        private static string TitleFor(string core)
        {
            return (core ?? "").Trim().ToLowerInvariant() == "xray" ? "Xray" : "V2Ray";
        }

        private void BuildSources()
        {
            void Add(string label, string url) { sourceMap[label] = url; cmbSource.Items.Add(label); }

            Add("barry-far \u00b7 all (base64)", "https://raw.githubusercontent.com/barry-far/V2ray-Config/refs/heads/main/All_Configs_base64_Sub.txt");
            Add("barry-far \u00b7 vmess",         "https://raw.githubusercontent.com/barry-far/V2ray-Config/refs/heads/main/Splitted-By-Protocol/vmess.txt");
            Add("barry-far \u00b7 vless",         "https://raw.githubusercontent.com/barry-far/V2ray-Config/refs/heads/main/Splitted-By-Protocol/vless.txt");
            Add("barry-far \u00b7 trojan",        "https://raw.githubusercontent.com/barry-far/V2ray-Config/refs/heads/main/Splitted-By-Protocol/trojan.txt");
            Add("ebrasha \u00b7 all",             "https://raw.githubusercontent.com/ebrasha/free-v2ray-public-list/refs/heads/main/all_extracted_configs.txt");
            Add("ebrasha \u00b7 vless",           "https://raw.githubusercontent.com/ebrasha/free-v2ray-public-list/refs/heads/main/vless_configs.txt");
            Add("ebrasha \u00b7 vmess",           "https://raw.githubusercontent.com/ebrasha/free-v2ray-public-list/refs/heads/main/vmess_configs.txt");
            Add("ebrasha \u00b7 trojan",          "https://raw.githubusercontent.com/ebrasha/free-v2ray-public-list/refs/heads/main/trojan_configs.txt");

            if (cmbSource.Items.Count > 0) cmbSource.SelectedIndex = 0;
        }

        private string ResolveSource(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string mapped;
            return sourceMap.TryGetValue(text.Trim(), out mapped) ? mapped : text.Trim();
        }

        private void DoFetch()
        {
            string url = ResolveSource(cmbSource.Text);
            SetStatus("fetching " + coreName + " list\u2026");
            var t = new Thread(() =>
            {
                string err;
                var rows = client.FetchServers(url, out err);
                if (rows.Count == 0)
                {
                    var b = client.LoadBundledServers();
                    if (b.Count > 0) { rows = b; err = (err == null ? "" : err + "  ") + "(using bundled configs)"; }
                }
                Action apply = () =>
                {
                    FillList(rows);
                    SetStatus(rows.Count + " servers" + (err != null ? "  \u2014 " + err : "")
                        + "  \u00b7 public list, treat as untrusted");
                };
                try { if (IsHandleCreated && !IsDisposed) BeginInvoke(apply); } catch { }
            }) { IsBackground = true };
            t.Start();
        }

        private void DoBundled()
        {
            var rows = client.LoadBundledServers();
            FillList(rows);
            SetStatus(rows.Count + " bundled servers from ." + "\\" + coreName + "\\configs");
        }

        private void FillList(List<CoreProxyClient.CoreServer> rows)
        {
            servers = rows ?? new List<CoreProxyClient.CoreServer>();
            serverTree.BeginUpdate();
            serverTree.Nodes.Clear();

            foreach (var kv in GroupBy(servers, ProtocolKey))
            {
                var protoNode = new TreeNode(kv.Key + "  (" + kv.Value.Count + ")")
                {
                    Tag = new CoreGroup(0, kv.Value)
                };
                protoNode.Nodes.Add(Placeholder());
                serverTree.Nodes.Add(protoNode);
            }

            serverTree.EndUpdate();
        }

        private void ServerTree_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            var node = e.Node;
            if (!(node.Nodes.Count >= 1 && node.Nodes[0].Tag == null)) return;
            var children = BuildChildren(node);
            serverTree.BeginUpdate();
            node.Nodes.Clear();
            node.Nodes.AddRange(children.ToArray());
            serverTree.EndUpdate();
        }

        private List<TreeNode> BuildChildren(TreeNode node)
        {
            var page = node.Tag as CorePage;
            if (page != null) return page.Slice;

            var grp = node.Tag as CoreGroup;
            if (grp == null) return new List<TreeNode>();

            var entries = BuildEntries(grp);
            return entries.Count > PAGE ? Paginate(entries) : entries;
        }

        private List<TreeNode> BuildEntries(CoreGroup grp)
        {
            var outNodes = new List<TreeNode>();
            if (grp.Depth == 0)
            {
                foreach (var kv in GroupBy(grp.Servers, CountryKey))
                {
                    var n = new TreeNode(CountryLabel(kv.Key) + "  (" + kv.Value.Count + ")")
                    { Tag = new CoreGroup(1, kv.Value) };
                    n.Nodes.Add(Placeholder());
                    outNodes.Add(n);
                }
            }
            else if (grp.Depth == 1)
            {
                foreach (var kv in GroupBy(grp.Servers, DomainKey))
                {
                    var n = new TreeNode(kv.Key + "  (" + kv.Value.Count + ")")
                    { Tag = new CoreGroup(2, kv.Value) };
                    n.Nodes.Add(Placeholder());
                    outNodes.Add(n);
                }
            }
            else
            {
                foreach (var s in grp.Servers)
                    outNodes.Add(new TreeNode(s.Display) { Tag = s });
            }
            return outNodes;
        }

        private List<TreeNode> Paginate(List<TreeNode> entries)
        {
            var pages = new List<TreeNode>();
            for (int start = 0; start < entries.Count; start += PAGE)
            {
                int end = Math.Min(start + PAGE, entries.Count);
                var slice = entries.GetRange(start, end - start);
                var pn = new TreeNode((start + 1) + "\u2013" + end) { Tag = new CorePage(slice) };
                pn.Nodes.Add(Placeholder());
                pages.Add(pn);
            }
            return pages;
        }

        private static TreeNode Placeholder() { return new TreeNode("\u2026"); }   

        private static SortedDictionary<string, List<CoreProxyClient.CoreServer>> GroupBy(
            List<CoreProxyClient.CoreServer> rows, Func<CoreProxyClient.CoreServer, string> key)
        {
            var d = new SortedDictionary<string, List<CoreProxyClient.CoreServer>>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in rows)
            {
                string k = key(s) ?? "";
                List<CoreProxyClient.CoreServer> g;
                if (!d.TryGetValue(k, out g)) { g = new List<CoreProxyClient.CoreServer>(); d[k] = g; }
                g.Add(s);
            }
            return d;
        }

        private static string ProtocolKey(CoreProxyClient.CoreServer s)
        {
            return string.IsNullOrEmpty(s.Protocol) ? "other" : s.Protocol;
        }

        private static string CountryKey(CoreProxyClient.CoreServer s)
        {
            return FlagToCode(s != null ? s.Remark : null) ?? "ZZ";
        }
        private static string CountryLabel(string key)
        {
            return key == "ZZ" ? "?? (no flag)" : key;
        }

        private static string FlagToCode(string remark)
        {
            if (string.IsNullOrEmpty(remark)) return null;
            for (int i = 0; i + 1 < remark.Length; )
            {
                if (char.IsHighSurrogate(remark[i]) && char.IsLowSurrogate(remark[i + 1]))
                {
                    int cp = char.ConvertToUtf32(remark[i], remark[i + 1]);
                    int j = i + 2;
                    if (cp >= 0x1F1E6 && cp <= 0x1F1FF
                        && j + 1 < remark.Length
                        && char.IsHighSurrogate(remark[j]) && char.IsLowSurrogate(remark[j + 1]))
                    {
                        int cp2 = char.ConvertToUtf32(remark[j], remark[j + 1]);
                        if (cp2 >= 0x1F1E6 && cp2 <= 0x1F1FF)
                        {
                            char a = (char)('A' + (cp - 0x1F1E6));
                            char b = (char)('A' + (cp2 - 0x1F1E6));
                            return new string(new[] { a, b });
                        }
                    }
                    i += 2;
                }
                else i++;
            }
            return null;
        }

        private static string DomainKey(CoreProxyClient.CoreServer s)
        {
            string a = s != null ? s.Address : null;
            return string.IsNullOrWhiteSpace(a) ? "(no host)" : a.Trim().ToLowerInvariant();
        }

        private void LayoutCore()
        {
            if (coreContent == null) return;
            int w = Math.Max(AlohaContent.Width,  MIN_W);
            int h = Math.Max(AlohaContent.Height, MIN_H);
            coreContent.SetBounds(0, 0, w, h);
        }

        private string CurrentSection()
        {
            return cfgSection != null && cfgSection.SelectedItem != null
                ? cfgSection.SelectedItem.ToString() : "Config (JSON)";
        }

        private void RegenConfig()
        {
            if (paneConfig == null) return;
            string sec = CurrentSection();
            bool sectionChanged = sec != lastSection;
            lastSection = sec;

            bool showConfig = sec == "Config (JSON)";
            bool showLink   = sec == "Paste a link" || sec == "Share link";
            bool showInfo   = sec == "Server details";
            bool showFav    = sec == "Favourites";
            paneConfig.Visible = showConfig;
            paneLink.Visible   = showLink;
            paneInfo.Visible   = showInfo;
            paneFav.Visible    = showFav;
            if (showConfig)    paneConfig.BringToFront();
            else if (showLink) paneLink.BringToFront();
            else if (showInfo) paneInfo.BringToFront();
            else if (showFav)  paneFav.BringToFront();

            suppressCfgChange = true;
            if (showFav)
            {
                RefreshFavourites();
            }
            else if (showConfig)
            {
                txtConfig.Text = selectedServer != null
                    ? client.BuildConfigJson(selectedServer, chkRules != null && chkRules.Checked)
                    : "// select a server in the tree";
            }
            else if (sec == "Paste a link")
            {
                txtLink.ReadOnly = false;
                lblLink.Text = "paste a vmess:// vless:// trojan:// or ss:// link, then Connect";
                if (sectionChanged) txtLink.Text = "";   
            }
            else if (sec == "Share link")
            {
                txtLink.ReadOnly = true;
                lblLink.Text = "share link (read-only)";
                txtLink.Text = selectedServer != null
                    ? (string.IsNullOrEmpty(selectedServer.Raw) ? "(no share link)" : selectedServer.Raw)
                    : "(select a server)";
            }
            else if (showInfo)
            {
                txtInfo.Text = selectedServer != null ? DetailsText(selectedServer) : "select a server in the tree";
            }
            suppressCfgChange = false;
        }

        private CoreProxyClient.CoreServer PastedLinkServer()
        {
            if (txtLink == null) return null;
            foreach (var raw in (txtLink.Text ?? "").Replace("\r", "").Split('\n'))
            {
                string ln = raw.Trim();
                if (ln.Length == 0 || ln.StartsWith("//")) continue;
                var s = CoreProxyClient.ParseLink(ln);
                if (s != null) return s;
            }
            return null;
        }

        private static string DetailsText(CoreProxyClient.CoreServer s)
        {
            var sb = new System.Text.StringBuilder();
            void Row(string k, string v) { if (!string.IsNullOrEmpty(v)) sb.Append(k.PadRight(12)).Append(v).Append("\r\n"); }
            Row("protocol", s.Protocol);
            Row("remark",   s.Remark);
            Row("address",  s.Address);
            Row("port",     s.Port > 0 ? s.Port.ToString() : "");
            if (s.Protocol == "shadowsocks") { Row("method", s.SsMethod); Row("password", s.SsPassword); }
            else Row(s.Protocol == "trojan" ? "password" : "id", s.Id);
            Row("network",     s.Network);
            Row("security",    s.Tls);
            Row("sni",         s.Sni);
            Row("host",        s.Host);
            Row("path",        s.Path);
            Row("flow",        s.Flow);
            Row("fingerprint", s.Fingerprint);
            Row("publicKey",   s.PublicKey);
            Row("shortId",     s.ShortId);
            return sb.ToString();
        }

        private string EditorConfigOrNull()
        {
            if (txtConfig == null || CurrentSection() != "Config (JSON)") return null;
            string t = txtConfig.Text;
            if (string.IsNullOrWhiteSpace(t)) return null;
            foreach (var line in t.Replace("\r", "").Split('\n'))
            {
                string ln = line.Trim();
                if (ln.Length > 0 && !ln.StartsWith("//")) return t;
            }
            return null;
        }

        private void DoConnect()
        {
            if (CurrentSection() == "Paste a link")
            {
                var parsed = PastedLinkServer();
                if (parsed == null) { SetStatus("paste a valid vmess:// vless:// trojan:// or ss:// link"); return; }
                selectedServer = parsed;
                DoConnectServer(parsed, null);
                return;
            }
            var node = serverTree.SelectedNode;
            var s = (node != null ? node.Tag as CoreProxyClient.CoreServer : null) ?? selectedServer;
            string raw = EditorConfigOrNull();
            if (s == null && raw == null) { SetStatus("select a server \u2014 expand a group, then pick a leaf"); return; }
            DoConnectServer(s, raw);
        }

        private void DoConnectServer(CoreProxyClient.CoreServer s) { DoConnectServer(s, null); }

        private void DoConnectServer(CoreProxyClient.CoreServer s, string rawConfig)
        {
            if (s == null && string.IsNullOrWhiteSpace(rawConfig)) return;

            var other = CoreProxyClient.Active;
            if (other != null && other != client) other.Disconnect();

            string cfgPath;
            string err = !string.IsNullOrWhiteSpace(rawConfig)
                ? client.ConnectRaw(rawConfig, s != null ? s.Display : coreName, out cfgPath)
                : client.Connect(s, chkRules != null && chkRules.Checked, out cfgPath);
            AfterConnect(err, s != null ? s.Display : "custom config");
        }

        private void AfterConnect(string err, string label)
        {
            if (err != null) { SetStatus("connect failed: " + err); UpdateButtons(); return; }

            if (!routed)
            {
                prevScheme = cfg.ProxyScheme; prevHost = cfg.ProxyHost; prevPort = cfg.ProxyPort;
            }
            cfg.ProxyScheme = "socks5";
            cfg.ProxyHost   = "127.0.0.1";
            cfg.ProxyPort   = client.SocksPort;
            cfg.Save();
            routed = true;
            if (reinit != null) reinit();

            SetStatus("routed via " + coreName + " \u2192 socks5 127.0.0.1:" + client.SocksPort
                + "  (" + (string.IsNullOrEmpty(label) ? "custom config" : label) + ")");
            UpdateButtons();
        }

        private void RefreshFavourites()
        {
            if (favList == null) return;
            favList.Items.Clear();
            try
            {
                if (Directory.Exists(client.FavouritesDir))
                    foreach (var f in Directory.GetFiles(client.FavouritesDir, "*.json"))
                        favList.Items.Add(Path.GetFileNameWithoutExtension(f));
            }
            catch { }
            if (favList.Items.Count == 0) SetStatus("no favourites yet \u2014 pick a server, then Add sel");
        }

        private void AddSelectedToFav()
        {
            var s = CurrentSection() == "Paste a link" ? PastedLinkServer() : selectedServer;
            if (s == null) { SetStatus("select a server in the tree first, then Add sel"); return; }
            try
            {
                Directory.CreateDirectory(client.FavouritesDir);
                string json = client.BuildConfigJson(s, chkRules != null && chkRules.Checked);
                string path = Path.Combine(client.FavouritesDir, client.SafeName(s.Display) + ".json");
                File.WriteAllText(path, json);
                SetStatus("favourited: " + s.Display);
            }
            catch (Exception ex) { SetStatus("could not save favourite: " + ex.Message); return; }
            RefreshFavourites();
        }

        private void UseFavourite()
        {
            if (favList == null || favList.SelectedItem == null) { SetStatus("pick a favourite from the list"); return; }
            string name = favList.SelectedItem.ToString();
            string path = Path.Combine(client.FavouritesDir, name + ".json");
            if (!File.Exists(path)) { SetStatus("favourite missing on disk: " + name); RefreshFavourites(); return; }

            var other = CoreProxyClient.Active;
            if (other != null && other != client) other.Disconnect();
            AfterConnect(client.ConnectConfigFile(path), name);
        }

        private void RemoveFavourite()
        {
            if (favList == null || favList.SelectedItem == null) { SetStatus("pick a favourite to remove"); return; }
            string name = favList.SelectedItem.ToString();
            try { File.Delete(Path.Combine(client.FavouritesDir, name + ".json")); }
            catch (Exception ex) { SetStatus("could not remove: " + ex.Message); return; }
            SetStatus("removed favourite: " + name);
            RefreshFavourites();
        }

        private void DoLatency()
        {
            var leaves = new List<TreeNode>();
            CollectLeaves(serverTree.Nodes, leaves);
            if (leaves.Count == 0) { SetStatus("expand a group first \u2014 nothing to probe"); return; }
            SetStatus("pinging " + leaves.Count + " server(s)\u2026");

            var t = new Thread(() =>
            {
                int remaining = leaves.Count;
                var gate = new Semaphore(16, 16);
                var done = new ManualResetEvent(false);
                foreach (var node in leaves)
                {
                    var n = node;
                    var s = n.Tag as CoreProxyClient.CoreServer;
                    if (s == null) { if (Interlocked.Decrement(ref remaining) == 0) done.Set(); continue; }
                    gate.WaitOne();
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        long ms;
                        try { ms = PingMs(s.Address, s.Port, 1500); }
                        finally { gate.Release(); }
                        string label = s.Display + "   " + (ms < 0 ? "timeout" : ms + " ms");
                        Ui(() => { try { n.Text = label; } catch { } });
                        if (Interlocked.Decrement(ref remaining) == 0) done.Set();
                    });
                }
                done.WaitOne();
                Ui(() => SetStatus("latency done"));
            });
            t.IsBackground = true;
            t.Start();
        }

        private void Ui(Action a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch { }
        }

        private static void CollectLeaves(TreeNodeCollection nodes, List<TreeNode> outLeaves)
        {
            foreach (TreeNode n in nodes)
            {
                if (n.Tag is CoreProxyClient.CoreServer) outLeaves.Add(n);
                if (n.Nodes.Count > 0) CollectLeaves(n.Nodes, outLeaves);
            }
        }

        private static long PingMs(string host, int port, int timeoutMs)
        {
            if (string.IsNullOrWhiteSpace(host) || port <= 0) return -1;
            try
            {
                var sw = Stopwatch.StartNew();
                using (var c = new TcpClient())
                {
                    var ar = c.BeginConnect(host, port, null, null);
                    bool ok = ar.AsyncWaitHandle.WaitOne(timeoutMs);
                    if (!ok || !c.Connected) return -1;
                    c.EndConnect(ar);
                    sw.Stop();
                    return sw.ElapsedMilliseconds;
                }
            }
            catch { return -1; }
        }

        private void ShowDetail(CoreProxyClient.CoreServer s)
        {
            if (s == null) return;
            bool rules = chkRules != null && chkRules.Checked;
            string json = client.BuildConfigJson(s, rules);
            var w = new CoreServerDetailWindow(s, json);
            w.OnConnect += cs => DoConnectServer(cs);
            w.Show();
        }

        private void DoDisconnect()
        {
            client.Disconnect();
            if (routed)
            {
                cfg.ProxyScheme = string.IsNullOrEmpty(prevScheme) ? "none" : prevScheme;
                cfg.ProxyHost   = prevHost ?? "";
                cfg.ProxyPort   = prevPort > 0 ? prevPort : 9050;
                cfg.Save();
                routed = false;
                if (reinit != null) reinit();
            }
            SetStatus(coreName + " stopped \u2014 browser proxy restored");
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool on = client.Connected;
            if (btnConnect != null)    btnConnect.Enabled = true;
            if (btnDisconnect != null) btnDisconnect.Enabled = on;
        }

        private void OnCoreLog(string line)
        {
            Action a = () =>
            {
                if (logBox == null || logBox.IsDisposed) return;
                logBox.AppendText(line + "\r\n");
            };
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch { }
        }

        private void SetStatus(string s)
        {
            if (lblStatus != null) lblStatus.Text = s;
        }

        private void ServerTree_DrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            var b = e.Bounds;
            if (b.Width <= 0 || b.Height <= 0) { e.DrawDefault = false; return; }
            bool sel = (e.State & TreeNodeStates.Selected) != 0 || (e.State & TreeNodeStates.Focused) != 0;
            int right = Math.Max(b.Right, serverTree.ClientSize.Width);
            using (var bg = new SolidBrush(sel ? Color.FromArgb(0x10, 0x3A, 0x20) : Color.Black))
                e.Graphics.FillRectangle(bg, new Rectangle(b.Left, b.Top, right - b.Left, b.Height));
            bool leaf = e.Node.Tag is CoreProxyClient.CoreServer;
            Color fc = sel ? Green : (leaf ? Color.FromArgb(0x2E, 0xE0, 0x5C) : Color.FromArgb(0x70, 0xC2, 0x88));
            using (var fg = new SolidBrush(fc))
                e.Graphics.DrawString(e.Node.Text, serverTree.Font, fg, b.Left, b.Top + 1);
        }

        private Button MakeButton(string text, int left, int top, int width, Action onClick)
        {
            var b = new Button
            {
                Text = text, Left = left, Top = top, Width = width, Height = 22,
                FlatStyle = FlatStyle.System
            };
            b.Click += (s, e) => onClick();
            return b;
        }

        private void AttachAloha(Panel host, Control inner, bool horizontal, bool noArrows = false)
        {
            var vbar = new DarkScrollBar(false, true) { Dock = DockStyle.None, NoArrows = noArrows };
            host.Controls.Add(vbar);
            var tb = inner as TextBoxBase;
            if (tb != null) vbar.Attach(tb); else vbar.AttachScrollable(inner);

            DarkScrollBar hbar = null;
            Panel corner = null;
            if (horizontal)
            {
                hbar = new DarkScrollBar(true, true) { Dock = DockStyle.None, NoArrows = noArrows };
                host.Controls.Add(hbar);
                hbar.AttachScrollable(inner);
                corner = new Panel { BackColor = Color.FromArgb(0xF0, 0xF0, 0xF0) };
                host.Controls.Add(corner);
            }

            vbar.BringToFront();
            if (hbar != null) hbar.BringToFront();
            if (corner != null) corner.BringToFront();

            void Layout()
            {
                int w = host.ClientSize.Width, h = host.ClientSize.Height;
                if (horizontal)
                {
                    vbar.SetBounds(Math.Max(0, w - 17), 0, 17, Math.Max(0, h - 17));
                    hbar.SetBounds(0, Math.Max(0, h - 17), Math.Max(0, w - 17), 17);
                    corner.SetBounds(Math.Max(0, w - 17), Math.Max(0, h - 17), 17, 17);
                }
                else
                {
                    vbar.SetBounds(Math.Max(0, w - 17), 0, 17, h);
                }
            }
            host.Resize += (s, e) => Layout();
            Layout();
        }
    }
}
