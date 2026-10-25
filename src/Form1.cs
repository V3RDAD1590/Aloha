using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Aloha.RingStoreCore;
using Aloha.Media;
using Aloha.Net;

namespace Aloha
{
    public partial class Form1 : Form
    {
        public const string VERSION = "6";
        private const string HOME = "";

        private static readonly string UserDataFolder = AlohaPaths.Profile;

        private NetConfig cfg;

        private readonly System.Collections.Generic.List<BrowserWindow> caravan =
            new System.Collections.Generic.List<BrowserWindow>();
        private const int MaxCaravanWindows = 4;

        private BookmarkManager _bookmarks;
        private BookmarkBarHost  _bmHost;
        private DownloadHistory _downloads;
        private static readonly string DownloadsPath =
            System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Application.ExecutablePath),
                "AlohaDownloads.json");
        private static readonly string BookmarksPath =
            System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Application.ExecutablePath),
                "AlohaBookmarks.json");

        private WebScrollBar vScroll, hScroll;
        private Panel scrollCorner;
        private const string HideBarsScript =
            "(function(){var s=document.createElement('style');" +
            "s.textContent='::-webkit-scrollbar{width:0 !important;height:0 !important;display:none !important}" +
            "html{scrollbar-width:none !important;-ms-overflow-style:none !important}';" +
            "(document.head||document.documentElement).appendChild(s);})();";

        private TabStrip tabStrip;
        private TabManagerWindow tabBoardWin;
        private bool tabsMode = false;

        public Form1()
        {
            AlohaPaths.MigrateLegacyProfile();
            DafyFrame.MainWindow = this;
            InitializeComponent();
            cfg = NetConfig.Load();

            if (cfg.InterceptPort == 8888) { cfg.InterceptPort = 8889; try { cfg.Save(); } catch { } }
            ApplyEngineCreationProperties();
            titleBar.Text = "Aloha Browser " + VERSION;
            LoadBranding();
            this.DoubleBuffered = true;
            this.Paint += Form1_Paint;
            this.Resize += (s, e) => { this.Invalidate(); LayoutWeb(); LayoutFooter(); };
            SetupChrome();
            SetupMenus();
            InitTabStrip();
            SetupLoadBar();
            DefocusToolbarButtons();

            textBox2.Cursor = Cursors.Hand;
            textBox2.Click += (s, e) => AlohaProbeConsole.Launch(cfg, this);

            _bookmarks = BookmarkManager.Load(BookmarksPath);
            _downloads = DownloadHistory.Load(DownloadsPath);
            BookmarkButtonGlue.GetCurrentPage = () => new[]
            {
                web != null && web.Source != null ? web.Source.ToString() : "",
                web != null && web.CoreWebView2 != null ? (web.CoreWebView2.DocumentTitle ?? "") : ""
            };
            _bmHost = new BookmarkBarHost(textBox1, _bookmarks, BookmarksPath);
            _bmHost.Attach();
            _bmHost.SetVisible(cfg.ShowBookmarkButton);

            this.Shown  += (s, e) => _bmHost.Reposition();
            this.Resize += (s, e) => _bmHost.Reposition();
            SetupDownloadPinButton();

            SetupUrlHexStrip();

            UrlBoxMargin();
            textBox1.HandleCreated += (s, e) => UrlBoxMargin();
            textBox1.FontChanged   += (s, e) => UrlBoxMargin();

            if (cfg.WinWidth >= this.MinimumSize.Width && cfg.WinHeight >= this.MinimumSize.Height)
            {
                this.StartPosition = FormStartPosition.CenterScreen;
                this.Size = new System.Drawing.Size(cfg.WinWidth, cfg.WinHeight);
            }
            this.FormClosing += (s, e) =>
            {
                if (this.WindowState == FormWindowState.Normal)
                {
                    cfg.WinWidth = this.Width;
                    cfg.WinHeight = this.Height;
                }
                try { cfg.Save(); } catch { }
                try { cgiServer?.Dispose(); } catch { }
            };
        }

        private UrlHexStrip urlHex;
        private string hoverUrl;

        private void SetupUrlHexStrip()
        {
            urlHex = new UrlHexStrip { Owner = this };

            textBox1.TextChanged += (s, e) => RefreshUrlHex();
            textBox1.Enter       += (s, e) => RefreshUrlHex();

            textBox1.Leave += (s, e) =>
            {
                hoverUrl = null;
                if (urlHex != null && urlHex.Visible) urlHex.Hide();
            };

            textBox1.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Escape) return;
                if (urlHex != null && urlHex.Visible) urlHex.Hide();
            };

            textBox1.MouseWheel += (s, e) =>
            {
                if (urlHex == null || !urlHex.Visible) return;
                if (!urlHex.Bounds.Contains(Cursor.Position)) return;
                urlHex.ScrollBy(e.Delta / 120);
            };

            this.Resize          += (s, e) => { if (urlHex != null && urlHex.Visible) urlHex.PinUnder(this, textBox1); };
            this.LocationChanged += (s, e) => { if (urlHex != null && urlHex.Visible) urlHex.PinUnder(this, textBox1); };
        }

        private void RefreshUrlHex()
        {
            if (urlHex == null || urlHex.IsDisposed) return;
            try
            {
                if (!cfg.CfgUrlHexStrip) { if (urlHex.Visible) urlHex.Hide(); return; }

                string url = textBox1.Focused ? textBox1.Text : hoverUrl;

                if (!urlHex.SetUrl(url, onlyIfNotable: !textBox1.Focused)) return;

                urlHex.PinUnder(this, textBox1);
                if (!urlHex.Visible) urlHex.Show();
            }
            catch { }
        }

        private static string LinkFromStatusBar(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return null;
            string s = t.Trim();
            Uri u;
            if (!Uri.TryCreate(s, UriKind.Absolute, out u)) return null;
            switch (u.Scheme)
            {
                case "http": case "https": case "ftp": case "file": return s;
                default: return null;
            }
        }

        private Panel loadBar;
        private Timer loadTimer;
        private double loadProg;
        private bool loadActive;

        private void SetupLoadBar()
        {
            loadBar = new Panel
            {
                Height = 3,
                Width = 0,
                BackColor = System.Drawing.Color.FromArgb(0x2E, 0xE0, 0x5C),
                Visible = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            loadBar.Location = new System.Drawing.Point(0, Math.Max(0, textBox1.Bottom - 3));
            this.Controls.Add(loadBar);
            loadBar.BringToFront();

            loadTimer = new Timer { Interval = 30 };
            loadTimer.Tick += LoadBar_Tick;
        }

        private void LoadStart()
        {
            if (loadBar == null) return;
            loadActive = true;
            loadProg = 0.06;
            loadBar.Width = 0;
            loadBar.Visible = true;
            loadBar.BringToFront();
            if (!loadTimer.Enabled) loadTimer.Start();
        }

        private void LoadDone()
        {
            loadActive = false;
            if (loadBar != null && loadTimer != null && loadBar.Visible && !loadTimer.Enabled)
                loadTimer.Start();
        }

        private void LoadBar_Tick(object sender, EventArgs e)
        {
            if (loadBar == null) { if (loadTimer != null) loadTimer.Stop(); return; }
            int full = Math.Max(1, this.ClientSize.Width);
            if (loadActive)
            {
                if (loadProg < 0.9) loadProg += (0.9 - loadProg) * 0.10 + 0.004;
            }
            else
            {
                loadProg += (1.0 - loadProg) * 0.34 + 0.03;
                if (loadProg >= 0.999)
                {
                    loadBar.Visible = false;
                    loadBar.Width = 0;
                    loadProg = 0;
                    loadTimer.Stop();
                    return;
                }
            }
            loadBar.Width = (int)(full * Math.Min(1.0, loadProg));
        }

        private void DefocusToolbarButtons()
        {
            var btns = new[] { button1, button2, button3, button4, button5, button6, button7, button8 };
            foreach (var b in btns)
            {
                if (b == null) continue;
                b.TabStop = false;
                b.Click += (s, e) => { try { this.ActiveControl = null; } catch { } };
            }
        }

        private Panel btnMax, btnClose, btnMin, footer;
        private readonly Color cFooter = Color.FromArgb(0xF0, 0xF0, 0xF0);

        private const string WebRtcKillScript = @"
(function(){
  var kill = ['RTCPeerConnection','webkitRTCPeerConnection','mozRTCPeerConnection','RTCDataChannel','RTCSessionDescription','RTCIceCandidate','RTCRtpSender'];
  kill.forEach(function(k){
    try { Object.defineProperty(window, k, { value: undefined, writable:false, configurable:false }); }
    catch(e){ try { window[k] = undefined; } catch(_){} }
  });
  function reject(){ return Promise.reject(new DOMException('WebRTC disabled','NotFoundError')); }
  if (navigator.mediaDevices){
    try { navigator.mediaDevices.getUserMedia = reject; } catch(e){}
    try { navigator.mediaDevices.enumerateDevices = function(){ return Promise.resolve([]); }; } catch(e){}
  }
  try { navigator.getUserMedia = navigator.webkitGetUserMedia = navigator.mozGetUserMedia =
        function(c,s,e){ if(e) e(new DOMException('WebRTC disabled','NotFoundError')); }; } catch(e){}
})();";

        private const string FindScript = @"
(function(q, matchCase, wholeWord, forward){
  var HL='__aloha_hl', CUR='__aloha_hl_cur';
  function clear(){
    var ns=document.querySelectorAll('span.'+HL);
    for(var i=0;i<ns.length;i++){var s=ns[i],p=s.parentNode; if(!p) continue;
      p.replaceChild(document.createTextNode(s.textContent),s); p.normalize();}
    window.__alohaFindSpans=null;
  }
  if(!q){ clear(); return '0:0'; }
  var sig=(matchCase?'C':'c')+(wholeWord?'W':'w')+'|'+q;
  var spans=window.__alohaFindSpans;
  if(window.__alohaFindSig===sig && spans && spans.length){
    // same search: just move the cursor
    var n=spans.length, i=window.__alohaFindIdx|0;
    i = forward ? (i+1)%n : (i-1+n)%n;
    window.__alohaFindIdx=i;
    mark(spans,i);
    return n+':'+(i+1);
  }
  clear();
  var flags='g'+(matchCase?'':'i');
  function esc(s){return s.replace(/[.*+?^${}()|[\]\\]/g,'\\$&');}
  var pat=esc(q); if(wholeWord) pat='\\b'+pat+'\\b';
  var re; try{ re=new RegExp(pat,flags); }catch(e){ return '0:0'; }
  var walker=document.createTreeWalker(document.body||document.documentElement, NodeFilter.SHOW_TEXT, {
    acceptNode:function(node){
      if(!node.nodeValue || !node.nodeValue.trim()) return NodeFilter.FILTER_REJECT;
      var p=node.parentNode;
      while(p){ var t=p.nodeName; if(t==='SCRIPT'||t==='STYLE'||t==='NOSCRIPT') return NodeFilter.FILTER_REJECT;
                if(p.classList && p.classList.contains(HL)) return NodeFilter.FILTER_REJECT; p=p.parentNode; }
      return NodeFilter.FILTER_ACCEPT;
    }
  });
  var targets=[], node;
  while((node=walker.nextNode())){ re.lastIndex=0; if(re.test(node.nodeValue)) targets.push(node); }
  var made=[];
  for(var t=0;t<targets.length;t++){
    var tn=targets[t], text=tn.nodeValue, frag=document.createDocumentFragment(), last=0, m; re.lastIndex=0;
    while((m=re.exec(text))){
      if(m.index>last) frag.appendChild(document.createTextNode(text.slice(last,m.index)));
      var sp=document.createElement('span'); sp.className=HL;
      sp.style.backgroundColor='#FFF04A'; sp.style.color='#000';
      sp.appendChild(document.createTextNode(m[0])); frag.appendChild(sp); made.push(sp);
      last=m.index+m[0].length; if(m[0].length===0) re.lastIndex++;
    }
    if(last<text.length) frag.appendChild(document.createTextNode(text.slice(last)));
    if(tn.parentNode) tn.parentNode.replaceChild(frag,tn);
  }
  window.__alohaFindSpans=made; window.__alohaFindSig=sig; window.__alohaFindIdx=0;
  if(!made.length) return '0:0';
  mark(made,0);
  return made.length+':1';

  function mark(spans,idx){
    for(var i=0;i<spans.length;i++){ spans[i].style.backgroundColor = (i===idx)?'#FF9632':'#FFF04A'; }
    var el=spans[idx]; if(el && el.scrollIntoView){
      try{ el.scrollIntoView({block:'center',inline:'nearest'}); }catch(e){ el.scrollIntoView(); }
    }
  }
})(QUERY, MATCHCASE, WHOLEWORD, FORWARD);";

        private const string FindKeyScript = @"
(function(){
  document.addEventListener('keydown', function(e){
    var isFind = (e.keyCode===114 /* F3 */);
    if(!isFind) return;
    try { e.preventDefault(); e.stopPropagation(); } catch(_){}
    try { window.chrome.webview.postMessage('alohafind'); } catch(_){}
  }, true);
})();";

        private const string CtrlOpenScript = @"
(function(){
  document.addEventListener('click', function(e){
    if(!e.ctrlKey || e.button!==0 || !e.isTrusted || e.shiftKey || e.altKey) return;
    var t = e.target, url = null;
    var a = (t && t.closest) ? t.closest('a[href]') : null;
    if(a && a.href) url = a.href;
    if(!url){
      var el = t;
      for(var i=0; i<5 && el; i++){
        var tag = el.tagName ? el.tagName.toLowerCase() : '';
        if((tag==='img'||tag==='video'||tag==='audio'||tag==='source'||tag==='embed') && el.src){ url = el.src; break; }
        try{
          var bg = getComputedStyle(el).backgroundImage;
          var m = bg && bg.match(/url\((.*?)\)/);
          if(m && m[1]){ url = m[1].replace(/^['""]|['""]$/g, ''); break; }
        }catch(_){}
        el = el.parentElement;
      }
    }
    if(url){
      try{ url = new URL(url, location.href).href; }catch(_){}
      e.preventDefault(); e.stopPropagation();
      try{ window.chrome.webview.postMessage('ctrlopen|'+url); }catch(_){}
    }
  }, true);
})();";

        private const string FaviconFetchScript = @"
(function(){
  try{
    var links = Array.prototype.slice.call(document.querySelectorAll(
      'link[rel~=\""icon\""], link[rel=\""shortcut icon\""], link[rel=\""apple-touch-icon\""]'));
    var cand = [];
    for(var i=0;i<links.length;i++){ if(links[i].href) cand.push(links[i].href); }
    // conventional fallback, always worth a try even when undeclared
    try{ cand.push(new URL('/favicon.ico', location.href).href); }catch(_){}
    if(!cand.length) return;

    // de-dupe, keep order
    var seen={}, urls=[];
    for(var j=0;j<cand.length;j++){ if(!seen[cand[j]]){ seen[cand[j]]=1; urls.push(cand[j]); } }

    // try candidates in turn; first that yields image bytes wins
    (function next(k){
      if(k>=urls.length) return;
      var u = urls[k];
      fetch(u, {credentials:'omit', cache:'force-cache'})
        .then(function(r){ if(!r.ok) throw 0; return r.blob(); })
        .then(function(b){
          if(!b || b.size===0 || b.size>512*1024) throw 0;
          var fr = new FileReader();
          fr.onloadend = function(){
            try{ window.chrome.webview.postMessage('favico|'+u+'|'+fr.result); }catch(_){}
          };
          fr.onerror = function(){ next(k+1); };
          fr.readAsDataURL(b);
        })
        .catch(function(){ next(k+1); });
    })(0);
  }catch(_){}
})();";

        private const string DisableWindowOpenScript = @"
(function(){
  try{
    Object.defineProperty(window, 'open', {
      value: function(){ return null; },
      writable: false, configurable: false
    });
  }catch(_){
    try{ window.open = function(){ return null; }; }catch(__){}
  }
})();";

        private BevelPanel fIndentWide;
        private Label sizeReadout;
        private Panel zoomBar;
        private Label zoomReadout;
        private double webZoom = 1.0;

        private void SetupChrome()
        {
            titleBar.TextAlign = ContentAlignment.MiddleCenter;
            this.MinimumSize = new System.Drawing.Size(520, 360);
            SetupFooter();

            interceptItem = new ToolStripMenuItem("Intercept proxy...");
            interceptItem.Click += interceptToolStripMenuItem_Click;
            tamperItem = new ToolStripMenuItem("Request tamper...");
            tamperItem.Click += tamperToolStripMenuItem_Click;
            rawTcpItem = new ToolStripMenuItem("Raw TCP listener...");
            rawTcpItem.Click += rawTcpToolStripMenuItem_Click;
            popupsItem = new ToolStripMenuItem("Blocked popups...");
            popupsItem.Click += (s, e) =>
            {
                if (popupsWin == null || popupsWin.IsDisposed)

                    popupsWin = new PopupsWindow(popupQueue, uri => { if (!string.IsNullOrEmpty(uri)) NewTab(uri); });
                popupsWin.RefreshList();
                popupsWin.Show();
                popupsWin.BringToFront();
            };

            instructionMapItem = new ToolStripMenuItem("Network Map...");
            instructionMapItem.Click += (s, e) =>
            {
                var w = new InstructionMapWindow(
                    web != null && web.CoreWebView2 != null ? web.CoreWebView2.Environment : null);
                w.Show();
                w.BringToFront();
            };

            vpnGateItem = new ToolStripMenuItem("VPN Gate...");
            vpnGateItem.Click += (s, e) =>
            {
                var w = new VpnGateWindow();
                w.Show();
                w.BringToFront();
            };

            torItem = new ToolStripMenuItem("Tor...");
            torItem.Click += (s, e) =>
            {
                var w = new TorWindow();
                w.StartPosition = FormStartPosition.CenterScreen;

                w.OnRouteRequested += () =>
                {
                    cfg.ProxyScheme = "socks5";
                    cfg.ProxyHost = "127.0.0.1";
                    cfg.ProxyPort = w.RoutePort;
                    cfg.Save();
                    ReinitEngine();
                };
                w.Show();
                w.BringToFront();
            };

            advorItem = new ToolStripMenuItem("ADVOR...");
            advorItem.Click += (s, e) =>
            {
                var w = new AdvorWindow(cfg);
                w.StartPosition = FormStartPosition.CenterScreen;
                w.Show();
                w.BringToFront();
            };

#if false

            sentinelItem = new ToolStripMenuItem("Sentinel (probe)...");
            sentinelItem.Click += (s, e) =>
            {
                if (!Ready || web.CoreWebView2 == null)
                {
                    SetStatus("Sentinel needs a loaded page");
                    return;
                }
                var w = new SentinelWindow(web.CoreWebView2);
                w.StartPosition = FormStartPosition.CenterScreen;
                w.Show();
                w.BringToFront();
                w.Activate();
            };
#endif

#if false
            sdrItem = new ToolStripMenuItem("SDR receivers...");
            sdrItem.Click += (s, e) =>
            {
                var w = new SdrBrowserWindow(url => { if (!string.IsNullOrEmpty(url)) NewTab(url); }, cfg);
                w.StartPosition = FormStartPosition.CenterScreen;
                w.Show();
                w.BringToFront();
            };
#endif

            ipv9Item = new ToolStripMenuItem("IPv9...");
            ipv9Item.Click += (s, e) =>
            {
                var w = new Ipv9Window(cfg);
                w.StartPosition = FormStartPosition.CenterScreen;
                w.Show();
                w.BringToFront();
                w.Activate();
            };

            sealedTorItem = new ToolStripMenuItem("Sealed tor");
            sealedTorItem.Click += (s, e) =>
            {
                var w = new SealedTorWindow(cfg, routed => ReinitEngine());
                w.StartPosition = FormStartPosition.CenterScreen;
                w.Show();
                w.BringToFront();
                w.Activate();
            };

            ipv7Item = new ToolStripMenuItem("IPv7");
            ipv7WindowItem = new ToolStripMenuItem("IPv7 window...");
            ipv7WindowItem.Click += (s, e) =>
            {
                var w = new Ipv7Window(cfg, url => { if (!string.IsNullOrEmpty(url)) NewTab(url); });
                w.StartPosition = FormStartPosition.CenterScreen;
                w.Show();
                w.BringToFront();
                w.Activate();
            };
            ipv7StatusItem = new ToolStripMenuItem("Status: ...") { Enabled = false };
            ipv7DoorItem = new ToolStripMenuItem("Open gateway (localhost:8888)");
            ipv7DoorItem.Click += (s, e) => NewTab(Ipv7Window.DoorUrl(cfg));
            ipv7StartItem = new ToolStripMenuItem("Start IPv7 client...");
            ipv7StartItem.Click += (s, e) => Ipv7Window.LaunchClient(cfg, this);
            ipv7SitesItem = new ToolStripMenuItem("Sites") { Enabled = false };
            ipv7Item.DropDownItems.Add(ipv7WindowItem);
            ipv7Item.DropDownItems.Add(new ToolStripSeparator());
            ipv7Item.DropDownItems.Add(ipv7StatusItem);
            ipv7Item.DropDownItems.Add(ipv7SitesItem);
            ipv7Item.DropDownItems.Add(new ToolStripSeparator());
            ipv7Item.DropDownItems.Add(ipv7DoorItem);
            ipv7Item.DropDownItems.Add(ipv7StartItem);
            ipv7Item.DropDownOpening += async (s, e) =>
            {
                ipv7StatusItem.Text = "Status: asking the gateway\u2026";
                var st = await Ipv7Window.ReadStatusAsync(cfg);
                ipv7StatusItem.Text = "Status: " + Ipv7Window.StatusLine(st);
                ipv7SitesItem.DropDownItems.Clear();
                ipv7SitesItem.Enabled = st != null && st.Sites.Count > 0;
                if (st != null)
                    foreach (var site in st.Sites)
                    {
                        var it = new ToolStripMenuItem(site.Name);
                        string u = site.Url;
                        it.Click += (s2, e2) => NewTab(u);
                        ipv7SitesItem.DropDownItems.Add(it);
                    }
            };

            cgiItem = new ToolStripMenuItem("CGI mounts...");
            cgiItem.Click += (s, e) =>
            {
                if (cgiServer == null) cgiServer = new AlohaCgiServer();
                var w = new AlohaCgiWindow(cgiServer, url => { if (!string.IsNullOrEmpty(url)) NewTab(url); });
                w.StartPosition = FormStartPosition.CenterScreen;

                BeginInvoke((Action)(() => { w.Show(); w.Activate(); }));
            };

            deepItem = new ToolStripMenuItem("DEEP protocol...");
            deepItem.Click += (s, e) =>
            {
                var w = new DeepWindow(cfg, url => { if (!string.IsNullOrEmpty(url)) NewTab(url); });
                w.StartPosition = FormStartPosition.CenterScreen;
                w.Show();
                w.BringToFront();
                w.Activate();
            };

            this.FormClosed += (s, e) => { try { interceptProxy.Stop(); } catch { } };

            RaiseChrome();
        }

        private void RaiseChrome()
        {
            if (footer != null) footer.BringToFront();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {

            bool chord = (keyData & (Keys.Control | Keys.Alt)) != 0
                      || ((keyData & Keys.KeyCode) >= Keys.F1 && (keyData & Keys.KeyCode) <= Keys.F24);
            if (chord)
            {
                var act = Hotkeys.Lookup(keyData);
                if (act.HasValue) { PerformHotkey(act.Value); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private HotkeyAction? _lastHotkey; private DateTime _lastHotkeyAt;
        private void PerformHotkey(HotkeyAction a)
        {
            var now = DateTime.UtcNow;
            if (_lastHotkey == a && (now - _lastHotkeyAt).TotalMilliseconds < 200) return;
            _lastHotkey = a; _lastHotkeyAt = now;
            try
            {
                switch (a)
                {
                    case HotkeyAction.SavePage:      SavePageAs(); break;
                    case HotkeyAction.SavePageQuick: SavePageQuick(); break;
                    case HotkeyAction.NewTab:        NewTab(); break;
                    case HotkeyAction.CloseTab:      if (tabStrip != null && tabStrip.ActiveIndex >= 0) OnTabClose(tabStrip.ActiveIndex); break;
                    case HotkeyAction.TabBoard:      OpenTabBoard(); break;
                    case HotkeyAction.Find:          OpenFindWindow(); break;
                    case HotkeyAction.Downloads:     downloadsToolStripMenuItem_Click(this, EventArgs.Empty); break;
                    case HotkeyAction.Bookmarks:     bookmarksToolStripMenuItem_Click(this, EventArgs.Empty); break;
                    case HotkeyAction.Home:          GoHome(); break;
                    case HotkeyAction.Reload:        if (Ready) web.CoreWebView2.Reload(); break;
                    case HotkeyAction.Back:          if (Ready && web.CoreWebView2.CanGoBack) web.CoreWebView2.GoBack(); break;
                    case HotkeyAction.Forward:       if (Ready && web.CoreWebView2.CanGoForward) web.CoreWebView2.GoForward(); break;
                    case HotkeyAction.FocusAddress:  textBox1.Focus(); textBox1.SelectAll(); break;
                    case HotkeyAction.ZoomIn:        SetZoom(webZoom + 0.1); break;
                    case HotkeyAction.ZoomOut:       SetZoom(webZoom - 0.1); break;
                    case HotkeyAction.ZoomReset:     SetZoom(1.0); break;
                    case HotkeyAction.DevTools:      AlohaProbeConsole.Launch(cfg, this); break;
                    case HotkeyAction.Help:          helpWindowToolStripMenuItem_Click(this, EventArgs.Empty); break;
                }
            }
            catch (Exception ex) { ActivityLog.General("hotkey " + a + ": " + ex.Message); }
        }

        private async void SavePageAs()
        {
            if (!Ready) return;
            try
            {
                var r = await web.CoreWebView2.ShowSaveAsUIAsync();
                ActivityLog.General("save as: " + r);
            }
            catch (Exception ex) { ActivityLog.General("save as failed: " + ex.Message); }
        }

        private void OnSaveAsUIShowing(object sender, CoreWebView2SaveAsUIShowingEventArgs e)
        {
            var core = sender as CoreWebView2;
            var deferral = e.GetDeferral();

            BeginInvoke((Action)(() =>
            {
                try
                {
                    string title = core != null ? core.DocumentTitle : "";
                    if (string.IsNullOrWhiteSpace(title)) { try { title = new Uri(core.Source).Host; } catch { title = "page"; } }
                    foreach (char c in System.IO.Path.GetInvalidFileNameChars()) title = title.Replace(c, '_');
                    if (title.Length > 80) title = title.Substring(0, 80);

                    using (var dlg = new SaveFileDialog
                    {
                        Title = "Aloha \u2014 save page",
                        FileName = title.Trim(),
                        InitialDirectory = !string.IsNullOrWhiteSpace(cfg.DownloadDir) ? cfg.DownloadDir : DefaultDownloadFolder(),
                        Filter = "Webpage, complete (*.html)|*.html|Webpage, HTML only (*.html)|*.html|Webpage, single file (*.mhtml)|*.mhtml",
                        FilterIndex = 1, OverwritePrompt = true, AddExtension = true
                    })
                    {
                        if (dlg.ShowDialog(this) != DialogResult.OK) { e.Cancel = true; DlTrace("save as: cancelled"); return; }
                        e.SuppressDefaultDialog = true;
                        e.AllowReplace = true;
                        e.SaveAsFilePath = dlg.FileName;
                        e.Kind = dlg.FilterIndex == 2 ? CoreWebView2SaveAsKind.HtmlOnly
                               : dlg.FilterIndex == 3 ? CoreWebView2SaveAsKind.SingleFile
                               : CoreWebView2SaveAsKind.Complete;
                        DlTrace("save as: " + e.Kind + " -> " + dlg.FileName);
                        WatchSavedPage(dlg.FileName, core != null ? core.Source : "");
                    }
                }
                catch (Exception ex) { DlTrace("save as EXCEPTION " + ex.Message); }
                finally { try { deferral.Complete(); } catch { } }
            }));
        }

        private void WatchSavedPage(string path, string url)
        {
            var t = new System.Windows.Forms.Timer { Interval = 300 };
            long lastLen = -1; int ticks = 0, stable = 0;
            t.Tick += (s, e) =>
            {
                ticks++;
                long len = -1; try { if (System.IO.File.Exists(path)) len = new System.IO.FileInfo(path).Length; } catch { }
                stable = (len >= 0 && len == lastLen) ? stable + 1 : 0;
                lastLen = len;
                if (stable >= 2 || ticks > 200)
                {
                    t.Stop(); t.Dispose();
                    if (len < 0) { DlTrace("save as: nothing appeared at " + path); return; }

                    lock (_downloads.Items)
                        foreach (var ex in _downloads.Items)
                        {
                            if (!string.Equals(ex.Path, path, StringComparison.OrdinalIgnoreCase)) continue;
                            DateTime st;
                            bool recent = DateTime.TryParse(ex.StartedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out st)
                                          && (DateTime.UtcNow - st.ToUniversalTime()).TotalSeconds < 120;
                            if (recent || ex.State != "completed") { DlTrace("save as: already tracked via DownloadStarting"); return; }
                        }
                    var rec = DownloadRecord.Start(url, System.IO.Path.GetFileName(path), path, len);
                    rec.ReceivedBytes = len; rec.State = "completed";
                    rec.FinishedUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
                    _downloads.Add(rec);
                    if (cfg.CfgDownloadToast) { try { Toast().Track(rec); } catch (Exception tx) { DlTrace("toast EXCEPTION " + tx.Message); } }
                    ActivityLog.General("page saved: " + path);
                }
            };
            t.Start();
        }

        private async void SavePageQuick()
        {
            if (!Ready) return;
            try
            {
                string json = await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureSnapshot", "{\"format\":\"mhtml\"}");
                string data = ExtractJsonString(json, "data");
                if (data == null) throw new Exception("no snapshot data");

                string dir = !string.IsNullOrWhiteSpace(cfg.DownloadDir) ? cfg.DownloadDir : DefaultDownloadFolder();
                System.IO.Directory.CreateDirectory(dir);
                string title = web.CoreWebView2.DocumentTitle;
                if (string.IsNullOrWhiteSpace(title)) { try { title = new Uri(web.CoreWebView2.Source).Host; } catch { title = "page"; } }
                foreach (char c in System.IO.Path.GetInvalidFileNameChars()) title = title.Replace(c, '_');
                if (title.Length > 80) title = title.Substring(0, 80);
                string path = UniquePath(System.IO.Path.Combine(dir, title.Trim() + ".mhtml"));
                System.IO.File.WriteAllText(path, data, new System.Text.UTF8Encoding(false));

                long size = new System.IO.FileInfo(path).Length;
                var rec = DownloadRecord.Start(web.CoreWebView2.Source, System.IO.Path.GetFileName(path), path, size);
                rec.ReceivedBytes = size; rec.State = "completed";
                rec.FinishedUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
                _downloads.Add(rec);
                if (cfg.CfgDownloadToast) Toast().Track(rec);
                ActivityLog.General("page saved: " + path);
            }
            catch (Exception ex) { ActivityLog.General("save page failed: " + ex.Message); }
        }

        private string DefaultDownloadFolder()
        {
            try { string d = web.CoreWebView2.Profile.DefaultDownloadFolderPath; if (!string.IsNullOrWhiteSpace(d)) return d; } catch { }
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        private static string UniquePath(string path)
        {
            if (!System.IO.File.Exists(path)) return path;
            string dir = System.IO.Path.GetDirectoryName(path), stem = System.IO.Path.GetFileNameWithoutExtension(path), ext = System.IO.Path.GetExtension(path);
            for (int i = 1; i < 10000; i++)
            {
                string p = System.IO.Path.Combine(dir, stem + " (" + i + ")" + ext);
                if (!System.IO.File.Exists(p)) return p;
            }
            return path;
        }

        private static string ExtractJsonString(string json, string key)
        {
            if (json == null) return null;
            int k = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal); if (k < 0) return null;
            int q = json.IndexOf('"', json.IndexOf(':', k) + 1); if (q < 0) return null;
            var sb = new System.Text.StringBuilder(json.Length);
            for (int i = q + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                char n = json[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break; case 'r': sb.Append('\r'); break; case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break; case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(json.Substring(i + 1, 4), 16)); i += 4; break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }

        private void ToggleMaximize()
        {
            if (this.WindowState == FormWindowState.Maximized)
                this.WindowState = FormWindowState.Normal;
            else
            {
                try { this.MaximizedBounds = Screen.FromControl(this).WorkingArea; } catch { }
                this.WindowState = FormWindowState.Maximized;
            }
        }

        private System.Drawing.Point cornerDragStart;
        private System.Drawing.Size  cornerStartSize;
        private bool cornerDragging, cornerDidDrag;

        private const int SvgTile = 160;
        private const int SvgMinorDivisions = 10;
        private const int SnapGrid = SvgTile / SvgMinorDivisions;

        private int SnapStepW { get { return (cfg != null && cfg.CfgTorLetterbox) ? 200 : SnapGrid; } }
        private int SnapStepH { get { return (cfg != null && cfg.CfgTorLetterbox) ? 100 : SnapGrid; } }

        private void Corner_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            cornerDragging  = true;
            cornerDidDrag   = false;
            cornerDragStart = Cursor.Position;
            cornerStartSize = this.Size;
        }

        private void Corner_MouseMove(object sender, MouseEventArgs e)
        {
            if (!cornerDragging) return;
            var now = Cursor.Position;
            int dx = now.X - cornerDragStart.X;
            int dy = now.Y - cornerDragStart.Y;
            if (!cornerDidDrag && Math.Abs(dx) + Math.Abs(dy) < 4) return;
            cornerDidDrag = true;
            if (this.WindowState == FormWindowState.Maximized) return;
            int newW = cornerStartSize.Width + dx;
            int newH = cornerStartSize.Height + dy;

            if (cfg != null && cfg.CfgSnapViewportToGrid)
            {
                int chromeW = this.Width - this.ClientSize.Width;
                int chromeH = this.Height - this.ClientSize.Height;
                int top     = textBox1 != null ? textBox1.Bottom : 93;
                int vChrome = top + FT;

                int viewW = Math.Max(SnapStepW, ((newW - chromeW) / SnapStepW) * SnapStepW);
                int viewH = Math.Max(SnapStepH, ((newH - chromeH - vChrome) / SnapStepH) * SnapStepH);

                newW = viewW + chromeW;
                newH = viewH + vChrome + chromeH;
            }

            this.Size = new System.Drawing.Size(newW, newH);
        }

        private bool snappingWindow;

        private void SnapWindowToGrid()
        {
            if (snappingWindow) return;
            if (cfg == null || !cfg.CfgSnapViewportToGrid) return;
            if (this.WindowState == FormWindowState.Maximized) return;
            snappingWindow = true;
            try
            {
                int chromeW = this.Width - this.ClientSize.Width;
                int chromeH = this.Height - this.ClientSize.Height;
                int top     = textBox1 != null ? textBox1.Bottom : 93;
                int vChrome = top + FT;
                int viewW = Math.Max(SnapStepW, ((this.ClientSize.Width) / SnapStepW) * SnapStepW);
                int viewH = Math.Max(SnapStepH, ((this.ClientSize.Height - vChrome) / SnapStepH) * SnapStepH);
                this.Size = new System.Drawing.Size(viewW + chromeW, viewH + vChrome + chromeH);
            }
            finally { snappingWindow = false; }
        }

        private void Corner_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            cornerDragging = false;
            cornerDidDrag  = false;

        }

        private void SetupFooter()
        {            footer = new Panel { Dock = DockStyle.Bottom, Height = FT, BackColor = cFooter };

            fIndentWide = new BevelPanel { Left = 2, Top = 0, Height = 17, Width = 300, BackColor = cFooter };

            btnMin   = MakeFooterButton("min",   () => this.WindowState = FormWindowState.Minimized);
            btnMax   = MakeFooterButton("max",   ToggleMaximize);
            btnClose = MakeFooterButton("close", () => this.Close());
            btnMin.Top = 0; btnMax.Top = 0; btnClose.Top = 0;

            footer.Controls.Add(fIndentWide);
            footer.Controls.Add(btnMin);
            footer.Controls.Add(btnMax);
            footer.Controls.Add(btnClose);

            sizeReadout = new Label
            {
                AutoSize = false, Left = 4, Top = 2, Height = 13, Width = 120,
                TextAlign = ContentAlignment.MiddleLeft, BackColor = cFooter,
                ForeColor = Color.FromArgb(0x50, 0x50, 0x50),
                Font = new Font("Lucida Console", 7.5f, FontStyle.Bold)
            };
            fIndentWide.Controls.Add(sizeReadout);
            sizeReadout.BringToFront();

            BuildZoomBar(fIndentWide);

            footer.Resize += (s, e) => LayoutFooter();

            this.Controls.Add(footer);
            LayoutFooter();
        }

        private void LayoutFooter()
        {
            if (footer == null || btnMin == null) return;
            int w = footer.ClientSize.Width;

            btnClose.Left = DafyFrame.FooterGap;

            btnMin.Left = w - btnMin.Width - DafyFrame.FooterGap;
            btnMax.Left = btnMin.Left - btnMax.Width - DafyFrame.FooterGap;

            int wellLeft  = btnClose.Right + DafyFrame.FooterGap;
            int wellRight = btnMax.Left - DafyFrame.FooterGap;
            fIndentWide.Left  = wellLeft;
            fIndentWide.Width = Math.Max(40, wellRight - wellLeft);
            if (sizeReadout != null)
            {

                sizeReadout.TextAlign = ContentAlignment.MiddleRight;
                sizeReadout.Left  = 4;
                sizeReadout.Width = Math.Max(20, fIndentWide.ClientSize.Width
                                               - (zoomBar != null ? zoomBar.Width + 12 : 0) - 8);
                sizeReadout.Text = this.ClientSize.Width + "\u00D7" + this.ClientSize.Height + " px";
            }
            if (zoomBar != null)
            {

                zoomBar.Left = Math.Max(sizeReadout.Right + 12,
                                        fIndentWide.ClientSize.Width - zoomBar.Width - DafyFrame.FooterGap);
                zoomBar.Top  = 2;
                zoomBar.BringToFront();
            }
        }

        private Panel MakeFooterButton(string kind, Action onClick)
        {
            var b = new Panel { Width = 44, Height = 17, Cursor = Cursors.Hand, BackColor = cFooter };
            bool hover = false, pressed = false;
            b.Paint += (s, e) =>
            {
                var g = e.Graphics;
                var r = b.ClientRectangle;
                Color fill = pressed ? Color.FromArgb(0xE4, 0xE4, 0xE4)
                                     : (hover ? Color.FromArgb(0xF7, 0xF7, 0xF7) : cFooter);
                using (var br = new SolidBrush(fill)) g.FillRectangle(br, r);

                int w1 = r.Width - 1, h1 = r.Height - 1;
                Color tl  = pressed ? Color.White : Color.FromArgb(0x80, 0x80, 0x80);
                Color brc = pressed ? Color.FromArgb(0x80, 0x80, 0x80) : Color.White;
                using (var pTl = new Pen(tl))  { g.DrawLine(pTl, 0, 0, w1, 0);  g.DrawLine(pTl, 0, 0, 0, h1); }
                using (var pBr = new Pen(brc)) { g.DrawLine(pBr, 0, h1, w1, h1); g.DrawLine(pBr, w1, 0, w1, h1); }
            };
            b.MouseEnter += (s, e) => { hover = true; b.Invalidate(); };
            b.MouseLeave += (s, e) => { hover = false; pressed = false; b.Invalidate(); };
            b.MouseDown  += (s, e) => { if (e.Button == MouseButtons.Left) { pressed = true; b.Invalidate(); } };
            b.MouseUp    += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                bool fire = pressed && b.ClientRectangle.Contains(e.Location);
                pressed = false; b.Invalidate();
                if (fire) onClick();
            };
            return b;
        }

        private void LoadBranding()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
                string ico = System.IO.Path.Combine(dir, "AlohaIcon.ico");
                if (System.IO.File.Exists(ico))
                {
                    this.Icon = new System.Drawing.Icon(ico);
                    IconTrace("window icon loaded from " + ico);
                }
                else
                {

                    IconTrace("AlohaIcon.ico NOT at " + ico + " — using embedded exe icon");
                    try { this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                    catch (Exception ex2) { IconTrace("embedded icon fallback failed: " + ex2.Message); }
                }
            }
            catch (Exception ex) { IconTrace("LoadBranding EXCEPTION " + ex.Message); }
        }

        private static void IconTrace(string msg)
        {
            try
            {
                string p = System.IO.Path.Combine(AlohaPaths.Root, "icon.log");
                System.IO.File.AppendAllText(p, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine);
            }
            catch { }
        }

        private void Form1_Paint(object sender, PaintEventArgs e)
        {
            var full = this.ClientRectangle;
            int top = textBox1 != null ? Math.Min(textBox1.Bottom, full.Height) : Math.Min(93, full.Height);

            using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                full,
                System.Drawing.Color.FromArgb(252, 252, 253),
                System.Drawing.Color.FromArgb(216, 216, 220),
                System.Drawing.Drawing2D.LinearGradientMode.Vertical))
            {
                e.Graphics.FillRectangle(brush, new System.Drawing.Rectangle(0, 0, full.Width, top));
            }

            if (top < full.Height)
                using (var fill = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xFA, 0xFA, 0xFB)))
                    e.Graphics.FillRectangle(fill, new System.Drawing.Rectangle(0, top, full.Width, full.Height - top));
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            LayoutFooter();
            TidyEngineFolders(0);
            await InitWebAsync();
        }

        private static async Task WaitForBrowserExit(uint pid, int timeoutMs)
        {
            if (pid == 0) { await Task.Delay(300); return; }
            try
            {
                var p = System.Diagnostics.Process.GetProcessById((int)pid);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (!p.HasExited && sw.ElapsedMilliseconds < timeoutMs)
                    await Task.Delay(100);

                if (p.HasExited)
                    ActivityLog.General("engine: browser process " + pid + " released the profile in "
                                      + sw.ElapsedMilliseconds + "ms");
                else
                    ActivityLog.General("engine: browser process " + pid + " STILL ALIVE after "
                                      + timeoutMs + "ms — the new browser args will not take effect "
                                      + "on this rebuild");
            }
            catch (ArgumentException) { }
            catch (Exception ex)
            {
                ActivityLog.General("engine: could not wait on browser process — " + ex.Message);
                await Task.Delay(300);
            }
        }

        private static void TidyEngineFolders(int keepGen)
        {
            try
            {
                string parent = System.IO.Path.GetDirectoryName(UserDataFolder);
                string prefix = System.IO.Path.GetFileName(UserDataFolder) + "_r";
                string keep   = keepGen > 0 ? prefix + keepGen : null;
                foreach (var d in System.IO.Directory.GetDirectories(parent, prefix + "*"))
                    if (keep == null || System.IO.Path.GetFileName(d) != keep)
                        try { System.IO.Directory.Delete(d, true); } catch { }
            }
            catch { }
        }

        private void ApplyEngineCreationProperties()
        {
            string browserArgs = cfg.BuildBrowserArguments();
            web.CreationProperties = new Microsoft.Web.WebView2.WinForms.CoreWebView2CreationProperties
            {
                UserDataFolder = UserDataFolder,
                ProfileName = SafeProfileName(cfg.ProfileName),
                AdditionalBrowserArguments = string.IsNullOrWhiteSpace(browserArgs) ? null : browserArgs
            };
        }

        private static string SafeProfileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (string.Equals(name.Trim(), "default", StringComparison.OrdinalIgnoreCase)) return null;
            var sb = new System.Text.StringBuilder();
            foreach (char ch in name.Trim())
                if (char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-') sb.Append(ch);
            string p = sb.ToString().Trim('.', ' ');
            if (p.Length == 0) return null;
            return p.Length > 64 ? p.Substring(0, 64) : p;
        }

        private async Task InitWebAsync()
        {
            try
            {
                string browserArgs = cfg.BuildBrowserArguments();
                ActivityLog.General("engine init — profile [" + cfg.ProfileName + "]");
                if (!string.IsNullOrWhiteSpace(browserArgs))
                    ActivityLog.General("browser args: " + browserArgs);

                var ensure = web.EnsureCoreWebView2Async();
                if (await Task.WhenAny(ensure, Task.Delay(12000)) != ensure)
                    throw new TimeoutException(
                        "WebView2 took too long to start — the user-data folder may still be " +
                        "locked by the previous engine. Try Apply again.");
                await ensure;

                webZoom = (cfg.WebZoom > 0.0 && cfg.WebZoom <= 50.0) ? cfg.WebZoom : 1.0;
                try { web.ZoomFactor = webZoom; } catch { }
                if (zoomReadout != null) zoomReadout.Text = (int)Math.Round(webZoom * 100) + "%";

                if (cfg.CfgWebRtcDisableFully)
                {
                    await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(WebRtcKillScript);
                    ActivityLog.General("WebRTC: APIs stripped (full disable active)");
                }

                try { await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(CtrlOpenScript); } catch { }

                try { await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(FindKeyScript); } catch { }

                try { _hotkeyScriptId = await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(Hotkeys.PageScript()); } catch { }

                if (cfg.CfgDisableWindowOpen)
                    try { await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(DisableWindowOpenScript); } catch { }

                var s = web.CoreWebView2.Settings;
                s.AreDevToolsEnabled = true;
                s.IsStatusBarEnabled = false;

                s.IsZoomControlEnabled = true;

                if (!string.IsNullOrWhiteSpace(cfg.UserAgent))
                    s.UserAgent = cfg.UserAgent;

                NetConfigWebView.ApplyLive(web.CoreWebView2, cfg);

                try
                {
                    web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        "home.aloha",
                        System.IO.Path.GetDirectoryName(Application.ExecutablePath),
                        CoreWebView2HostResourceAccessKind.Allow);
                }
                catch { }

                AlohaConfigBridge.Attach(web.CoreWebView2, cfg, ReinitEngine, null);

                Aloha.Cutscenes.CutsceneDirector.Current.Attach(this);

                try
                {
                    mediaHook = await MediaMetadataHook.AttachAsync(
                        web.CoreWebView2,
                        System.IO.Path.Combine(AlohaPaths.BaseDir, "aloha_metadata.js"),
                        new MediaResourceResolver(cfg),
                        cfg.CfgMediaCard);
                }
                catch (Exception mmx) { ActivityLog.General("media-metadata hook: " + mmx.Message); }

                web.CoreWebView2.StatusBarTextChanged += (s2, e2) =>
                {
                    try
                    {

                        var core2 = s2 as CoreWebView2;
                        hoverUrl = LinkFromStatusBar(core2?.StatusBarText);
                        RefreshUrlHex();
                    }
                    catch { }
                };

                HookEvents();
                GoHome();

                SnapWindowToGrid();
                this.BeginInvoke((Action)(() => { try { ApplyBrowserScrollbars(); } catch { } }));
            }
            catch (Exception ex)
            {

                try
                {
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(
                            System.IO.Path.GetDirectoryName(Application.ExecutablePath),
                            "aloha_crash.txt"),
                        "WebView2 init failed:\r\n\r\n" + ex.ToString());
                }
                catch { }
                using (var top = new Form
                {
                    TopMost = true,
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Location = new System.Drawing.Point(-3000, -3000),
                    Size = new System.Drawing.Size(1, 1)
                })
                {
                    top.Show();
                    MessageBox.Show(top,
                        "WebView2 failed to start.\r\n\r\nDetail:\r\n" + ex.Message,
                        "Aloha", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private readonly System.Collections.Generic.HashSet<string> sitePeers =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object peerLock = new object();

        private void ClearPeers() { lock (peerLock) sitePeers.Clear(); }

        private void CollectPeer(string uri)
        {
            try
            {
                string h = new Uri(uri).Host;
                if (string.IsNullOrEmpty(h)) return;
                if (h.Equals("home.aloha", StringComparison.OrdinalIgnoreCase)) return;
                if (h == "localhost" || h == "127.0.0.1" || h == "::1") return;
                lock (peerLock) sitePeers.Add(h);
            }
            catch { }
        }

        public string[] SitePeersSnapshot()
        {
            lock (peerLock)
            {
                var arr = new string[sitePeers.Count];
                sitePeers.CopyTo(arr);
                return arr;
            }
        }

        private void HookEvents()
        {

            try
            {
                web.CoreWebView2.IsDefaultDownloadDialogOpenChanged += (o, a) =>
                {
                    try
                    {
                        var core = o as CoreWebView2;
                        if (core != null && core.IsDefaultDownloadDialogOpen && (cfg.CfgDownloadToast || cfg.HideDownloadPopup))
                        { core.CloseDefaultDownloadDialog(); DlTrace("closed the engine's download bubble"); }
                    }
                    catch { }
                };
            }
            catch (Exception bx) { DlTrace("bubble hook unavailable: " + bx.Message); }

            try { web.CoreWebView2.SaveAsUIShowing += OnSaveAsUIShowing; }
            catch (Exception sx) { DlTrace("save-as hook unavailable: " + sx.Message); }

            try { AlohaContextMenu.Attach(web.CoreWebView2, web); } catch (Exception cx) { ActivityLog.General("context menu hook: " + cx.Message); }

            web.CoreWebView2.DownloadStarting += (o, a) =>
            {
                try
                {
                DlTrace("DownloadStarting");

                if (cfg.HideDownloadPopup || cfg.CfgDownloadToast) a.Handled = true;

                string url  = a.DownloadOperation != null ? a.DownloadOperation.Uri : "";
                string path = a.ResultFilePath ?? "";
                string name = !string.IsNullOrEmpty(path) ? System.IO.Path.GetFileName(path) : "";

                if (cfg.BlockDownloads)
                {
                    a.Cancel = true;
                    ActivityLog.General("download blocked: " + url);
                    _downloads.Add(DownloadRecord.Blocked(url, name));
                    return;
                }

                if (cfg.ConfirmDownloads)
                {
                    var deferral = a.GetDeferral();
                    string nm = string.IsNullOrEmpty(name) ? "(file)" : name, u2 = url;
                    BeginInvoke((Action)(() =>
                    {
                        try
                        {
                            var ans = MessageBox.Show(this,
                                "Download this file?\r\n\r\n" + nm + "\r\n" + u2,
                                "Aloha \u2014 confirm download", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                            if (ans != DialogResult.Yes)
                            {
                                a.Cancel = true;
                                ActivityLog.General("download declined: " + u2);
                                _downloads.Add(DownloadRecord.Cancelled(u2, nm));
                            }
                            else PlaceAndTrack(a);
                        }
                        catch (Exception dx) { DlTrace("confirm EXCEPTION " + dx.GetType().Name + ": " + dx.Message); }
                        finally { deferral.Complete(); }
                    }));
                    return;
                }

                PlaceAndTrack(a);
                }
                catch (Exception dx) { DlTrace("DownloadStarting EXCEPTION " + dx.GetType().Name + ": " + dx.Message); }
            };

            web.CoreWebView2.SourceChanged += (o, a) =>
            {
                string src = web.Source?.ToString() ?? "";

                textBox1.Text = DeepProtocol.Display(AlohaConfigBridge.Pretty(src));
                UpdateActiveTabTitle();
            };

            var faviconView = web;

            web.CoreWebView2.FaviconChanged += (o, a) => PullFavicon(faviconView, "FaviconChanged");

            web.CoreWebView2.NavigationStarting += (o, a) =>
            {
                LoadStart();
                ClearPeers();

                if (!a.IsRedirected)
                {
                    int ci = IndexOfView(faviconView);
                    if (ci >= 0) tabStrip.SetIcon(ci, null);
                }
                SetStatus("LOADING\r\n" + a.Uri);
                ActivityLog.Network("resolve/navigate -> " + a.Uri);
            };

            web.CoreWebView2.NavigationCompleted += (o, a) =>
            {
                LoadDone();
                SetStatus((a.IsSuccess ? "OK " : "ERR ") + (int)a.WebErrorStatus +
                          "\r\n" + AlohaConfigBridge.Pretty(web.Source?.ToString() ?? ""));
                if (a.IsSuccess) ActivityLog.Network("loaded OK");
                else ActivityLog.Network("FAILED status=" + a.WebErrorStatus);
                PullFavicon(faviconView, "NavCompleted");
                if (a.IsSuccess && cfg.SealedTor) ReadSealedCircuit(web.Source?.ToString());
                bool wasTyped = cutsceneTypedNav; cutsceneTypedNav = false;
                if (a.IsSuccess)
                    Aloha.Cutscenes.CutsceneDirector.Current.NotifyNavigation(
                        web.Source != null ? web.Source.ToString() : null, wasTyped);

                if (a.IsSuccess && cfg.CfgLoadFavicons)
                {
                    try { faviconView.CoreWebView2.ExecuteScriptAsync(FaviconFetchScript); }
                    catch (Exception fx) { FavTrace("fetchScript failed: " + fx.Message); }
                }
            };

            web.CoreWebView2.NewWindowRequested += (o, a) =>
            {
                a.Handled = true;
                if (string.IsNullOrEmpty(a.Uri)) { ActivityLog.Network("new-window: no uri"); return; }

                string why = cfg.CfgBlockAllPopups ? "block-all" : PopupVerdict(a);
                if (why == null)
                {

                    if ((Control.ModifierKeys & Keys.Shift) != 0)
                    {
                        OpenInCaravan(a.Uri);
                        return;
                    }
                    if (cfg.CfgBackgroundNewTabs) { NewTabBackground(a.Uri); ActivityLog.Network("new BACKGROUND tab (link) -> " + a.Uri); }
                    else { NewTab(a.Uri); ActivityLog.Network("new tab (link) -> " + a.Uri); }
                    return;
                }
                popupQueue.Add(new PopupEntry(DateTime.Now, a.Uri));
                SetStatus("BLOCKED POPUP (" + why + ")\r\n" + a.Uri);
                popupsWin?.RefreshList();
                ActivityLog.Network("blocked popup [" + why + "] -> " + a.Uri);
            };

            web.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            web.CoreWebView2.WebResourceRequested += (o, a) =>
            {
                try
                {

                    if (IsFaviconRequest(a))
                        FavTrace("REQ favicon " + a.Request.Uri + " (favicons=" + cfg.CfgLoadFavicons + ")");
                    if (!cfg.CfgLoadFavicons && IsFaviconRequest(a))
                    {
                        var core0 = o as CoreWebView2;
                        if (core0 != null)
                        {
                            a.Response = core0.Environment.CreateWebResourceResponse(
                                null, 403, "Blocked", "");
                            ActivityLog.Proxy("BLOCK favicon  " + a.Request.Uri);
                            return;
                        }
                    }

                    if (DeepProtocol.IsVirtual(a.Request.Uri))
                    {
                        var core1 = o as CoreWebView2;
                        if (core1 != null) { DeepProtocol.Answer(cfg, core1, a); ActivityLog.Proxy("REQ  " + a.Request.Method + "  " + a.Request.Uri); return; }
                    }
                    var core = o as CoreWebView2;
                    if (core != null) TamperEngine.Apply(core.Environment, a);
                    CollectPeer(a.Request.Uri);
                    ActivityLog.Proxy("REQ  " + a.Request.Method + "  " + a.Request.Uri);
                    foreach (var h in a.Request.Headers)
                        ActivityLog.Network("  > " + h.Key + ": " + h.Value);
                }
                catch { }
            };
            web.CoreWebView2.WebResourceResponseReceived += async (o, a) =>
            {
                try
                {
                    ActivityLog.Proxy("RESP " + a.Response.StatusCode + "  " + a.Request.Uri);
                    foreach (var h in a.Response.Headers)
                        ActivityLog.Network("  < " + h.Key + ": " + h.Value);
                }
                catch { }

                try
                {
                    string ctype = "";
                    try { foreach (var h in a.Response.Headers) if (string.Equals(h.Key, "content-type", StringComparison.OrdinalIgnoreCase)) { ctype = h.Value; break; } } catch { }
                    string rurl = a.Request != null ? a.Request.Uri : null;
                    if (FaviconCache.LooksLikeFavicon(rurl, ctype))
                    {
                        string saved = await FaviconCache.TrySaveFromResponse(a, FavTrace);
                        if (saved != null) PullFavicon(faviconView, "wire");
                    }
                }
                catch { }
            };

            web.CoreWebView2.WebMessageReceived += (o, a) =>
            {
                try
                {
                    string m = a.TryGetWebMessageAsString();
                    if (m == "alohafind")
                    {
                        BeginInvoke((Action)(() => OpenFindWindow()));
                    }
                    else if (!string.IsNullOrEmpty(m) && m.StartsWith("alohakey|", StringComparison.Ordinal))
                    {
                        var act = Hotkeys.Lookup(Hotkeys.FromChordId(m.Substring("alohakey|".Length)));
                        if (act.HasValue) { var a2 = act.Value; BeginInvoke((Action)(() => PerformHotkey(a2))); }
                    }
                    else if (!string.IsNullOrEmpty(m) && m.StartsWith("ctrlopen|", StringComparison.Ordinal))
                    {
                        string url = m.Substring("ctrlopen|".Length);
                        if (!string.IsNullOrEmpty(url)) NewTab(url);
                    }
                    else if (!string.IsNullOrEmpty(m) && m.StartsWith("favico|", StringComparison.Ordinal))
                    {

                        int p1 = m.IndexOf('|');
                        int p2 = m.IndexOf('|', p1 + 1);
                        if (p2 > p1)
                        {
                            string iconUrl = m.Substring(p1 + 1, p2 - p1 - 1);
                            string dataUrl = m.Substring(p2 + 1);
                            OnFaviconBytes(o as CoreWebView2, iconUrl, dataUrl);
                        }
                    }
                }
                catch { }
            };
        }

        private void OnFaviconBytes(CoreWebView2 sender, string iconUrl, string dataUrl)
        {
            try
            {
                if (string.IsNullOrEmpty(iconUrl) || string.IsNullOrEmpty(dataUrl)) return;
                int comma = dataUrl.IndexOf(',');
                if (comma < 0) { FavTrace("favico: no comma in data url  " + iconUrl); return; }
                byte[] bytes = Convert.FromBase64String(dataUrl.Substring(comma + 1));
                if (bytes.Length == 0 || bytes.Length > 512 * 1024) { FavTrace("favico: bad size " + bytes.Length + "  " + iconUrl); return; }

                string saved = FaviconCache.SaveBytes(iconUrl, bytes, FavTrace);
                if (saved == null) return;

                int idx = IndexOfViewByCore(sender);
                if (idx < 0) idx = IndexOfView(web);
                if (idx >= 0)
                {
                    var img = FaviconCache.Load(iconUrl);
                    if (img != null) { tabStrip.SetIcon(idx, img); FavTrace("favico: SET idx=" + idx + " " + img.Width + "x" + img.Height + "  " + iconUrl); }
                }
            }
            catch (Exception ex) { FavTrace("favico: EXCEPTION " + ex.GetType().Name + ": " + ex.Message); }
        }

        private int IndexOfViewByCore(CoreWebView2 core)
        {
            if (tabStrip == null || core == null) return -1;
            for (int k = 0; k < tabStrip.Count; k++)
            {
                var v = tabStrip.ViewAt(k) as Microsoft.Web.WebView2.WinForms.WebView2;
                try { if (v != null && object.ReferenceEquals(v.CoreWebView2, core)) return k; }
                catch { }
            }
            return -1;
        }

        private void SetStatus(string text)
        {
            if (textBox2.IsHandleCreated) textBox2.Text = text;
        }

        private bool Ready => web?.CoreWebView2 != null;

        private bool cutsceneTypedNav;

        private void Go(string input)
        {
            if (!Ready || string.IsNullOrWhiteSpace(input)) return;
            cutsceneTypedNav = true;
            input = input.Trim();

            string low = input.ToLowerInvariant();
            if (low == "about:blank" || low == "about:home" || low == "aloha:home" ||
                low == "aloha://blank" || low == "aloha:blank") { GoHome(); return; }

            if (low == "aloha://config" || low == "aloha:config" || low == "aloha://config/")
            { web.CoreWebView2.Navigate(AlohaConfigBridge.PageUrl); return; }

            if (low.StartsWith("ipv7://") || low.StartsWith("ipv7:"))
            { web.CoreWebView2.Navigate(Ipv7Window.GatewayUrl(cfg, input)); return; }

            if (low.StartsWith("deep://") || low.StartsWith("deep:"))
            {
                string v = DeepProtocol.ToVirtual(input);
                if (v != null) web.CoreWebView2.Navigate(v); else SetStatus("deep:// needs an authority: deep://node.network/");
                return;
            }
            if (input.Contains("://") || HasDirectScheme(input))
                web.CoreWebView2.Navigate(input);
            else if (input.Contains(".") && !input.Contains(" "))
                web.CoreWebView2.Navigate("http://" + input);
            else
                web.CoreWebView2.Navigate(SearchUrl(input));
        }

        private void PlaceAndTrack(CoreWebView2DownloadStartingEventArgs a)
        {
            string url  = a.DownloadOperation != null ? a.DownloadOperation.Uri : "";
            string path = a.ResultFilePath ?? "";
            string name = !string.IsNullOrEmpty(path) ? System.IO.Path.GetFileName(path) : "";

            if (!string.IsNullOrWhiteSpace(cfg.DownloadDir))
            {
                try
                {
                    string chosenDir = string.IsNullOrEmpty(path) ? "" : System.IO.Path.GetDirectoryName(path);
                    bool userChose = !string.IsNullOrEmpty(chosenDir)
                                  && !string.Equals(System.IO.Path.GetFullPath(chosenDir).TrimEnd('\\'),
                                                    System.IO.Path.GetFullPath(DefaultDownloadFolder()).TrimEnd('\\'),
                                                    StringComparison.OrdinalIgnoreCase);
                    if (!userChose)
                    {
                        System.IO.Directory.CreateDirectory(cfg.DownloadDir);
                        string fn = string.IsNullOrEmpty(name) ? "download" : name;
                        a.ResultFilePath = UniquePath(System.IO.Path.Combine(cfg.DownloadDir, fn));
                        path = a.ResultFilePath;
                        name = System.IO.Path.GetFileName(path);
                    }
                    else DlTrace("user-chosen path kept: " + path);
                }
                catch (Exception dx) { DlTrace("DownloadDir EXCEPTION " + dx.Message); }
            }

            var op = a.DownloadOperation;
            long total = (op != null && op.TotalBytesToReceive.HasValue) ? (long)op.TotalBytesToReceive.Value : 0;
            var rec = DownloadRecord.Start(url, name, path, total);
            _downloads.Add(rec);
            ActivityLog.General("download started: " + url);
            if (op != null) _liveOps[rec] = op;
            if (cfg.CfgDownloadToast)
            {
                try { Toast().Track(rec, op); DlTrace("toast: tracking " + name); }
                catch (Exception tx) { DlTrace("toast EXCEPTION " + tx.GetType().Name + ": " + tx.Message); }
            }
            else DlTrace("toast: off (CfgDownloadToast = false)");

            if (op != null)
            {
                op.BytesReceivedChanged += (s2, e2) =>
                {
                    try
                    {
                        rec.ReceivedBytes = op.BytesReceived;
                        if (op.TotalBytesToReceive.HasValue) rec.TotalBytes = (long)op.TotalBytesToReceive.Value;
                        _downloads.RaiseChanged();
                    }
                    catch (Exception dx) { DlTrace("BytesReceivedChanged EXCEPTION " + dx.GetType().Name + ": " + dx.Message); }
                };
                op.StateChanged += (s2, e2) =>
                {
                    try
                    {
                        DlTrace("StateChanged -> " + op.State);
                        try { if (!string.IsNullOrEmpty(op.ResultFilePath)) rec.Path = op.ResultFilePath; } catch { }
                        if (op.State == CoreWebView2DownloadState.Completed)        { rec.State = "completed"; rec.Retries = 0; }
                        else if (op.State == CoreWebView2DownloadState.Interrupted)
                        {
                            string why = ""; try { why = op.InterruptReason.ToString(); } catch { }
                            DlTrace("interrupted: " + why + " path=" + rec.Path + " canResume=" + SafeCanResume(op));
                            KeepAlive(rec, op, why);
                        }
                        else                                                        rec.State = "in progress";
                        rec.ReceivedBytes = op.BytesReceived;
                        if (op.State != CoreWebView2DownloadState.InProgress)
                            rec.FinishedUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
                        _downloads.Save();
                        _downloads.RaiseChanged();
                    }
                    catch (Exception dx) { DlTrace("StateChanged EXCEPTION " + dx.GetType().Name + ": " + dx.Message); }
                };
            }
        }

        private static bool SafeCanResume(CoreWebView2DownloadOperation op) { try { return op.CanResume; } catch { return false; } }

        private static bool Recoverable(string why)
        {
            switch (why)
            {
                case "NetworkFailed": case "NetworkTimeout": case "NetworkDisconnected": case "NetworkServerDown":
                case "ServerFailed": case "ServerUnexpectedResponse": case "ServerContentLengthMismatch":
                case "FileTransientError": case "FileTooShort": case "FileHashMismatch": case "ServerNoRange":
                case "DownloadProcessCrashed":
                    return true;
            }
            return false;
        }

        private void KeepAlive(DownloadRecord rec, CoreWebView2DownloadOperation op, string why)
        {
            if (rec.UserCancelled || why == "UserCanceled" && !rec.UserPaused)
            {
                rec.State = "cancelled";
                ActivityLog.General("download cancelled: " + rec.Url);
                return;
            }
            if (rec.UserPaused || why == "UserPaused")
            {
                rec.State = "paused";
                return;
            }
            if (Recoverable(why) && SafeCanResume(op))
            {
                rec.Retries++;
                int delay = Math.Min(60, 1 << Math.Min(6, rec.Retries));
                rec.State = "retrying " + why + " #" + rec.Retries + " in " + delay + " s";
                var t = new System.Windows.Forms.Timer { Interval = delay * 1000 };
                t.Tick += (s, e) =>
                {
                    t.Stop(); t.Dispose();
                    if (rec.UserPaused || rec.UserCancelled) return;
                    try { if (op.State == CoreWebView2DownloadState.Interrupted) { op.Resume(); rec.State = "in progress"; DlTrace("auto-resume #" + rec.Retries); } }
                    catch (Exception ex) { DlTrace("auto-resume EXCEPTION " + ex.Message); }
                    _downloads.RaiseChanged();
                };
                t.Start();
                return;
            }

            rec.State = why.Length > 0 && why != "None" ? "interrupted: " + why + (Recoverable(why) ? " (server cannot resume)" : "") : "interrupted";
        }

        private void RetryDownload(DownloadRecord rec)
        {
            if (!Ready || rec == null || string.IsNullOrEmpty(rec.Url)) return;
            try { web.CoreWebView2.Navigate(rec.Url); } catch (Exception ex) { DlTrace("retry EXCEPTION " + ex.Message); }
        }

        private Button _dlPinBtn;
        private readonly Dictionary<DownloadRecord, CoreWebView2DownloadOperation> _liveOps =
            new Dictionary<DownloadRecord, CoreWebView2DownloadOperation>();

        private void SetupDownloadPinButton()
        {
            var parent = textBox1.Parent; if (parent == null) return;
            _dlPinBtn = BookmarkButtonGlue.MakeButton("\u21E9");   
            _dlPinBtn.BackColor = textBox1.BackColor;
            _dlPinBtn.FlatAppearance.MouseOverBackColor = ControlPaint.Light(textBox1.BackColor);
            _dlPinBtn.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(textBox1.BackColor);
            new ToolTip().SetToolTip(_dlPinBtn, "Show downloads (stays until you press this again)");
            parent.Controls.Add(_dlPinBtn);
            _dlPinBtn.Click += (s, e) =>
            {
                var t = Toast();
                List<DownloadRecord> recent;
                lock (_downloads.Items) recent = new List<DownloadRecord>(_downloads.Items);
                t.SetPinned(!t.Pinned, recent, r => { CoreWebView2DownloadOperation op; return _liveOps.TryGetValue(r, out op) ? op : null; });
            };
            Action place = () =>
            {
                var star = _bmHost != null ? _bmHost.Button : null;
                int h = Math.Min(_dlPinBtn.Width, textBox1.Height - 2);
                _dlPinBtn.Height = h;
                _dlPinBtn.Top = textBox1.Top + (textBox1.Height - h) / 2;
                _dlPinBtn.Left = (star != null && star.Visible && star.Parent == parent)
                    ? star.Left - _dlPinBtn.Width - 2
                    : textBox1.Right - _dlPinBtn.Width - 4;
                _dlPinBtn.BringToFront();
            };
            if (_bmHost != null) { _bmHost.Button.LocationChanged += (s, e) => place(); _bmHost.Button.VisibleChanged += (s, e) => place(); }
            textBox1.SizeChanged += (s, e) => place();
            textBox1.LocationChanged += (s, e) => place();
            Shown += (s, e) => place();
            place();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        private const int EM_SETMARGINS = 0xD3, EC_LEFTMARGIN = 0x1;

        private void UrlBoxMargin()
        {
            try { if (textBox1 != null && textBox1.IsHandleCreated) SendMessage(textBox1.Handle, EM_SETMARGINS, (IntPtr)EC_LEFTMARGIN, (IntPtr)5); }
            catch { }
        }

        private DownloadToast _toast;
        private DownloadToast Toast()
        {
            if (_toast == null || _toast.IsDisposed)
            {
                _toast = new DownloadToast(this,

                    () => web != null && web.IsHandleCreated ? web.RectangleToScreen(web.ClientRectangle) : RectangleToScreen(ClientRectangle),
                    () => downloadsToolStripMenuItem_Click(this, EventArgs.Empty),
                    url => NewTab(url),
                    RetryDownload);
                var t = _toast;
                _downloads.Changed += t.Changed;
                t.PinnedChanged += () =>
                {
                    if (_dlPinBtn == null) return;
                    _dlPinBtn.ForeColor = t.Pinned ? Color.FromArgb(0x2F, 0x5A, 0x38) : Color.FromArgb(0x70, 0x70, 0x70);
                    _dlPinBtn.Font = new Font("Segoe UI Symbol", 11f, t.Pinned ? FontStyle.Bold : FontStyle.Regular);
                };
                t.FormClosed += (s, e) => { _downloads.Changed -= t.Changed; };
            }
            return _toast;
        }

        private static void DlTrace(string msg)
        {
            try
            {
                string p = System.IO.Path.Combine(AlohaPaths.Root, "download.log");
                System.IO.File.AppendAllText(p,
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  [t" +
                    System.Threading.Thread.CurrentThread.ManagedThreadId + "]  " + msg + Environment.NewLine);
            }
            catch { }
        }

        private void PullFavicon(Microsoft.Web.WebView2.WinForms.WebView2 view, string why)
        {
            try
            {
                if (view == null || view.CoreWebView2 == null) return;
                if (!cfg.CfgLoadFavicons) { FavTrace(why + ": favicons OFF"); return; }
                if (tabStrip == null) return;

                int idx = IndexOfView(view);
                if (idx < 0) { FavTrace(why + ": idx=-1 (view not in strip)"); return; }

                string fav = view.CoreWebView2.FaviconUri;
                if (string.IsNullOrEmpty(fav)) { FavTrace(why + ": idx=" + idx + " no FaviconUri yet"); return; }

                var img = FaviconCache.Load(fav);
                if (img == null)
                {

                    FavTrace(why + ": idx=" + idx + " uri='" + fav + "' not cached yet");
                    return;
                }
                tabStrip.SetIcon(idx, img);
                FavTrace(why + ": idx=" + idx + " SET from cache " + img.Width + "x" + img.Height + "  " + fav);
            }
            catch (Exception ex)
            {
                FavTrace(why + ": EXCEPTION " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void FavTrace(string msg)
        {
            try
            {
                string p = System.IO.Path.Combine(AlohaPaths.Root, "favicon.log");
                System.IO.File.AppendAllText(p,
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine);
            }
            catch { }
        }

        private static bool IsFaviconRequest(CoreWebView2WebResourceRequestedEventArgs a)
        {
            try
            {
                string u = a.Request.Uri.ToLowerInvariant();
                if (u.Contains("favicon")) return true;

                if (u.Contains("apple-touch-icon")) return true;
                return false;
            }
            catch { return false; }
        }

        private readonly Dictionary<string, string> sealedCircPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private ToolTip sealedTip;

        private async void ReadSealedCircuit(string url)
        {
            try
            {
                if (!cfg.SealedTor || string.IsNullOrEmpty(url)) return;
                Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return;
                if (u.Scheme != "http" && u.Scheme != "https") return;
                string host = u.Host.ToLowerInvariant().TrimEnd('.');
                if (host == "localhost" || host.StartsWith("127.") || host.EndsWith(".aloha")) return;
                string rend = host.EndsWith(".onion") ? host.Substring(0, host.Length - 6) : null;

                int rtt = -1;
                try
                {
                    string js = await web.CoreWebView2.ExecuteScriptAsync(
                        "(function(){var n=performance.getEntriesByType('navigation')[0];" +
                        "return n&&n.responseStart>0?Math.round(n.responseStart-n.requestStart):-1})()");
                    int.TryParse((js ?? "").Trim('"'), out rtt);
                    if (rtt == 0) rtt = -1;
                }
                catch { }
                string exitCc = null;

                await Task.Delay(250);
                var hit = await TorControl.OnceAsync(cfg.SealedTorControlPort, null, cfg.SealedTorControlPassword, c =>
                {
                    string e;
                    var circs = c.GetCircuits(out e);
                    TorControl.Circuit best = null;
                    var streams = c.GetStreams(out e);
                    for (int i = streams.Count - 1; i >= 0 && best == null; i--)
                    {
                        var st = streams[i];
                        if (st.CircuitId == "0" || st.Target == null) continue;
                        string th = st.Target; int col = th.LastIndexOf(':'); if (col > 0) th = th.Substring(0, col);
                        if (!th.Equals(host, StringComparison.OrdinalIgnoreCase)) continue;
                        best = circs.Find(x => x.Id == st.CircuitId);
                    }
                    if (best == null && rend != null)
                        for (int i = circs.Count - 1; i >= 0; i--)
                            if (circs[i].Status == "BUILT" && circs[i].RendQuery == rend) { best = circs[i]; break; }

                    if (best != null && rend == null && best.Fingerprints.Count > 0)
                    {
                        string fp = best.Fingerprints[best.Fingerprints.Count - 1];
                        string ns = fp.Length == 40 ? c.GetInfo("ns/id/" + fp) : null;
                        if (ns != null)
                            foreach (var ln in ns.Split('\n'))
                                if (ln.StartsWith("r "))
                                {
                                    var t = ln.Split(' ');
                                    if (t.Length > 6) exitCc = c.GetInfo("ip-to-country/" + t[6]);
                                    break;
                                }
                        if (exitCc == "??") exitCc = null;
                    }
                    return best;
                }, null);
                if (hit == null) return;

                sealedCircPath[host] = "sealed tor \u00b7 circuit " + hit.Id + "  \u00b7  " + (hit.Purpose ?? "GENERAL").ToLowerInvariant()
                    + (hit.Relays.Count > 0 ? "\r\n" + string.Join("  \u2192  ", hit.Relays.ToArray()) : "")
                    + (hit.TimeCreated.Length >= 19 ? "\r\nbuilt " + hit.TimeCreated.Substring(11, 8) + " UTC (" + (TorControl.CircuitAge(hit.TimeCreated) ?? "?") + " ago)" : "")
                    + (exitCc != null ? "\r\nexit " + exitCc : "")
                    + (rtt >= 0 ? "\r\nrtt " + rtt + " ms (request \u2192 first byte)" : "");
                if (sealedTip == null) sealedTip = new ToolTip { InitialDelay = 300, AutoPopDelay = 15000 };
                sealedTip.SetToolTip(textBox1, sealedCircPath[host]);
                ActivityLog.Network("sealed: " + host + " rides " + sealedCircPath[host].Replace("\r\n", "  |  "));
            }
            catch { }
        }

        private static readonly string[] DirectSchemes =
            { "about:", "view-source:", "data:", "file:", "blob:", "chrome:", "edge:" };
        private static bool HasDirectScheme(string s)
        {
            foreach (var p in DirectSchemes)
                if (s.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private string SearchUrl(string query)
        {
            string q = Uri.EscapeDataString(query);
            string tmpl = cfg != null ? cfg.SearchTemplate : null;
            if (!string.IsNullOrWhiteSpace(tmpl) && tmpl.Contains("{q}"))
                return tmpl.Replace("{q}", q);

            bool onTor = cfg != null && cfg.ProxyPort == 9050
                         && !string.IsNullOrEmpty(cfg.ProxyScheme)
                         && cfg.ProxyScheme.StartsWith("socks", StringComparison.OrdinalIgnoreCase);
            return (onTor ? NetConfig.SearchStartpageOnion : NetConfig.SearchStartpage).Replace("{q}", q);
        }

        private void button1_Click(object sender, EventArgs e)
        { if (Ready && web.CoreWebView2.CanGoBack) web.CoreWebView2.GoBack(); }
        private void button2_Click(object sender, EventArgs e)
        { if (Ready && web.CoreWebView2.CanGoForward) web.CoreWebView2.GoForward(); }
        private void button3_Click(object sender, EventArgs e)
        { ToggleTabs(); }

        private void InitTabStrip()
        {
            tabStrip = new TabStrip();
            tabStrip.Visible = false;
            tabStrip.Font = button3.Font;

            tabStrip.SetBounds(button3.Right, button3.Top,
                               Math.Max(200, textBox1.Right - button3.Right), button3.Height);
            tabStrip.SlotWidth = Math.Max(200, textBox1.Right - button3.Right);
            tabStrip.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            tabStrip.ActivateRequested += OnTabActivate;
            tabStrip.CloseRequested += OnTabClose;
            Controls.Add(tabStrip);
            tabStrip.BringToFront();

            this.Resize += (s, e) =>
            {
                if (tabStrip == null) return;
                int w = Math.Max(200, textBox1.Right - button3.Right);
                tabStrip.SlotWidth = w;
            };
            tabStrip.AddTab("Home", web);                   
            tabStrip.SetActive(0);
        }

        private void ToggleTabs()
        {
            tabsMode = !tabsMode;
            button3.Text = tabsMode ? "Tabs" : "Home";
            if (tabStrip != null) tabStrip.Visible = tabsMode;
            button4.Visible = button5.Visible = button6.Visible =
                button7.Visible = button8.Visible = !tabsMode;
            if (tabsMode && tabStrip != null) tabStrip.BringToFront();
        }

        private bool tabBusy = false;

        private int IndexOfView(object v)
        {
            if (tabStrip == null || v == null) return -1;
            for (int k = 0; k < tabStrip.Count; k++)
                if (object.ReferenceEquals(tabStrip.ViewAt(k), v)) return k;
            return -1;
        }

        private void ShowTabView(Microsoft.Web.WebView2.WinForms.WebView2 target)
        {
            if (target == null || tabStrip == null) return;
            for (int k = 0; k < tabStrip.Count; k++)
            {
                var v = tabStrip.ViewAt(k) as Microsoft.Web.WebView2.WinForms.WebView2;
                if (v != null) v.Visible = object.ReferenceEquals(v, target);
            }
            web = target;
            RaiseChrome();
            try
            {
                string src = web.Source != null ? web.Source.ToString() : "";
                textBox1.Text = src.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ? "" : src;
            }
            catch { }
            try { if (web.CoreWebView2 != null) web.CoreWebView2.Resume(); } catch { }
            ApplyBrowserScrollbars();
        }

        private async void OnTabActivate(int i)
        {
            if (tabStrip == null || i < 0 || i >= tabStrip.Count) return;
            var target = tabStrip.ViewAt(i) as Microsoft.Web.WebView2.WinForms.WebView2;
            if (target == null) return;
            var outgoing = web;
            ShowTabView(target);
            tabStrip.SetActive(i);
            UpdateActiveTabTitle();

            if (!object.ReferenceEquals(outgoing, target) && outgoing != null && outgoing.CoreWebView2 != null)
            {
                try { await outgoing.CoreWebView2.TrySuspendAsync(); } catch { }
            }
        }

        private void OnTabClose(int i)
        {
            if (tabStrip == null || i < 0 || i >= tabStrip.Count) return;
            if (tabStrip.Count == 1)
            {
                GoHome();
                tabStrip.SetTitle(0, "new tab");
                tabStrip.SetActive(0);
                return;
            }

            var view = tabStrip.ViewAt(i) as Microsoft.Web.WebView2.WinForms.WebView2;
            bool wasActive = object.ReferenceEquals(view, web);
            int next = (i >= tabStrip.Count - 1) ? i - 1 : i;
            tabStrip.RemoveTab(i);
            if (wasActive)
            {
                var nv = tabStrip.ViewAt(next) as Microsoft.Web.WebView2.WinForms.WebView2;
                ShowTabView(nv);
            }
            int ai = IndexOfView(web);
            if (ai >= 0) tabStrip.SetActive(ai);

            if (view != null && !object.ReferenceEquals(view, web))
            {
                try { this.Controls.Remove(view); } catch { }
                try { view.Dispose(); } catch { }
            }
        }

        private async void NewTabBackground(string url)
        {
            if (tabStrip == null || tabBusy) return;
            tabBusy = true;
            try
            {
                var stayView = tabStrip.ActiveView as Microsoft.Web.WebView2.WinForms.WebView2;
                int stayIdx = tabStrip.ActiveIndex;

                var v = NewTabControl();
                int idx = tabStrip.AddTab("new tab", v);
                web = v;
                ApplyEngineCreationProperties();
                this.Controls.Add(v);
                if (!tabsMode) ToggleTabs();

                await InitWebAsync();
                if (!string.IsNullOrEmpty(url))
                    try { web.CoreWebView2.Navigate(url); } catch { }

                if (stayView != null && !stayView.IsDisposed)
                {
                    web = stayView;
                    ShowTabView(stayView);
                    if (stayIdx >= 0) tabStrip.SetActive(stayIdx);
                    ActivityLog.Network("background tab ready, focus kept on tab " + stayIdx);
                }
            }
            finally { tabBusy = false; }
        }

        private Microsoft.Web.WebView2.WinForms.WebView2 NewTabControl()
        {
            var v = new Microsoft.Web.WebView2.WinForms.WebView2();
            ((System.ComponentModel.ISupportInitialize)v).BeginInit();
            v.Location = new Point(0, textBox1.Bottom);
            v.Size = new Size(this.ClientSize.Width, this.ClientSize.Height - textBox1.Bottom);
            v.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            ((System.ComponentModel.ISupportInitialize)v).EndInit();
            return v;
        }

        private async void NewTab(string url = null)
        {
            if (tabStrip == null || tabBusy) return;
            tabBusy = true;
            try
            {
                var outgoing = tabStrip.ActiveView as Microsoft.Web.WebView2.WinForms.WebView2;

                var v = NewTabControl();
                int idx = tabStrip.AddTab("new tab", v);   
                web = v;
                ApplyEngineCreationProperties();
                this.Controls.Add(v);
                ShowTabView(v);
                tabStrip.SetActive(idx);
                if (!tabsMode) ToggleTabs();

                await InitWebAsync();
                if (!string.IsNullOrEmpty(url))
                    try { web.CoreWebView2.Navigate(url); } catch { }

                if (outgoing != null && !object.ReferenceEquals(outgoing, v) && outgoing.CoreWebView2 != null)
                    try { await outgoing.CoreWebView2.TrySuspendAsync(); } catch { }
            }
            finally { tabBusy = false; }
        }

        private void OpenInCaravan(string url)
        {
            caravan.RemoveAll(w => w == null || w.IsDisposed);

            foreach (var w in caravan)
            {
                if (!w.IsDisposed && w.TabCount < BrowserWindow.MaxTabs)
                {
                    _ = w.AddTab(url, null);
                    w.Activate();
                    ActivityLog.Network("caravan: +tab -> " + url);
                    return;
                }
            }

            if (caravan.Count >= MaxCaravanWindows)
            {
                SetStatus("CARAVAN FULL\r\n4 windows x 4 tabs");
                ActivityLog.Network("caravan: full (max " + MaxCaravanWindows + " windows)");
                return;
            }

            int index = 1;
            var used = new System.Collections.Generic.HashSet<int>();
            foreach (var w in caravan) used.Add(w.WindowIndex);
            while (used.Contains(index)) index++;

            var bw = new BrowserWindow(cfg, index, url);
            bw.FormClosed += (s, e) => caravan.RemoveAll(x => x == null || x.IsDisposed);
            caravan.Add(bw);
            bw.Show();
            ActivityLog.Network("caravan: new window " + index + " -> " + url);
        }

        private void OpenTabBoard()
        {
            if (tabBoardWin != null && !tabBoardWin.IsDisposed)
            {
                try { tabBoardWin.Activate(); tabBoardWin.BringToFront(); } catch { }
                return;
            }
            tabBoardWin = new TabManagerWindow(
                SnapshotTabs,
                EngineLabel,
                i => { OnTabActivate(i); try { this.Activate(); this.BringToFront(); } catch { } },
                i => OnTabClose(i));
            tabBoardWin.Show();
        }

        private List<TabManagerWindow.TabInfo> SnapshotTabs()
        {
            var outp = new List<TabManagerWindow.TabInfo>();
            if (tabStrip == null) return outp;
            int active = tabStrip.ActiveIndex;
            for (int k = 0; k < tabStrip.Count; k++)
            {
                var v = tabStrip.ViewAt(k) as Microsoft.Web.WebView2.WinForms.WebView2;
                string url = "";
                try { url = (v != null && v.Source != null) ? v.Source.ToString() : ""; } catch { }
                outp.Add(new TabManagerWindow.TabInfo { Index = k, Host = HostOf(url), Url = url, Active = (k == active) });
            }
            return outp;
        }

        private static string HostOf(string url)
        {
            if (string.IsNullOrEmpty(url)) return "new tab";
            if (url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return "new tab";
            if (url.IndexOf("home.aloha", StringComparison.OrdinalIgnoreCase) >= 0) return "home";
            try { return new Uri(url).Host; } catch { return url; }
        }

        private string EngineLabel()
        {
            try
            {
                if (cfg != null && cfg.CfgUseInterceptProxy) return "INTERCEPT";
                string sch = (cfg != null ? cfg.ProxyScheme : null);
                sch = (sch ?? "none").Trim().ToLowerInvariant();
                if (cfg == null || sch == "none" || sch.Length == 0 || string.IsNullOrEmpty(cfg.ProxyHost)) return "DIRECT";
                if (cfg.ProxyPort == 9050 || cfg.ProxyPort == 9150) return "TOR " + cfg.ProxyHost + ":" + cfg.ProxyPort;
                return sch.ToUpperInvariant() + " " + cfg.ProxyHost + ":" + cfg.ProxyPort;
            }
            catch { return "DIRECT"; }
        }

        private async void ClearCache()
        {
            if (web?.CoreWebView2 == null) { ActivityLog.General("clear cache: engine not ready"); return; }
            try
            {
                await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.clearBrowserCache", "{}");
                ActivityLog.General("browser cache cleared");
                SetStatus("CACHE CLEARED");
            }
            catch (Exception ex) { ActivityLog.General("clear cache failed: " + ex.Message); }
        }

        private void UpdateActiveTabTitle()
        {
            if (tabStrip == null || tabStrip.ActiveIndex < 0) return;
            string t = "new tab";
            try
            {
                string src = web != null && web.Source != null ? web.Source.ToString() : null;
                if (!string.IsNullOrEmpty(src))
                {
                    if (src.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) t = "new tab";
                    else { try { t = new Uri(src).Host; } catch { t = src; } }
                    if (string.IsNullOrEmpty(t)) t = src;
                }
            }
            catch { }
            tabStrip.SetTitle(tabStrip.ActiveIndex, t);
        }

        private void DisposeExtraTabs()
        {
            if (tabStrip == null) return;
            for (int k = tabStrip.Count - 1; k >= 0; k--)
            {
                var v = tabStrip.ViewAt(k) as Microsoft.Web.WebView2.WinForms.WebView2;
                if (v != null && !object.ReferenceEquals(v, web))
                {
                    try { this.Controls.Remove(v); } catch { }
                    try { v.Dispose(); } catch { }
                    tabStrip.RemoveTab(k);
                }
            }
            int ai = IndexOfView(web);
            if (ai >= 0) tabStrip.SetActive(ai);
        }

        private void GoHome()
        {
            if (!Ready) return;
            string css;
            if (cfg.WallpaperEnabled)
            {
                string file = string.IsNullOrWhiteSpace(cfg.Wallpaper) ? "blueprint.svg" : cfg.Wallpaper;

                css = "background-color:#060c14;" +
                      "background-image:url('" + WallpaperCssUrl(file) + "');" +
                      "background-repeat:repeat;background-size:160px 160px";
            }
            else
            {
                css = "background-color:#0d0d12";   
            }
            web.CoreWebView2.NavigateToString(
                "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
                "<style>html,body{margin:0;height:100%;background:#0d0d12;overflow:hidden}" +
                ".bg{position:fixed;top:0;left:0;right:0;bottom:0;" + css + "}" +
                "</style></head><body><div class=\"bg\"></div></body></html>");

            try { textBox1.Text = ""; } catch { }
            UpdateActiveTabTitle();
        }

        private static string WallpaperCssUrl(string file)
        {
            try
            {
                string[] roots =
                {
                    System.IO.Path.GetDirectoryName(Application.ExecutablePath),
                    AppDomain.CurrentDomain.BaseDirectory,
                    Environment.CurrentDirectory
                };
                foreach (var root in roots)
                {
                    if (string.IsNullOrEmpty(root)) continue;
                    string p = System.IO.Path.Combine(root, file);
                    if (!System.IO.File.Exists(p)) continue;
                    var bytes = System.IO.File.ReadAllBytes(p);
                    if (bytes.Length <= 96 * 1024)
                        return "data:" + MimeFor(file) + ";base64," + Convert.ToBase64String(bytes);
                    break;
                }
            }
            catch { }
            return "https://home.aloha/" + file;   
        }

        private static string MimeFor(string file)
        {
            switch (System.IO.Path.GetExtension(file ?? "").ToLowerInvariant())
            {
                case ".svg":  return "image/svg+xml";
                case ".png":  return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif":  return "image/gif";
                case ".webp": return "image/webp";
                case ".bmp":  return "image/bmp";
                default:      return "application/octet-stream";
            }
        }

        private BrowserStylePanel styleWin;
        private HotkeysWindow hotkeysWin;
        private ToolStripMenuItem hotkeysItem;
        private string _hotkeyScriptId;

        private async void ReinjectHotkeys()
        {
            if (!Ready) return;
            try
            {
                if (_hotkeyScriptId != null) web.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(_hotkeyScriptId);
                _hotkeyScriptId = await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(Hotkeys.PageScript());
            }
            catch (Exception ex) { ActivityLog.General("hotkeys: reinject failed — " + ex.Message); }
        }
        private ToolStripMenuItem browserStyleItem;
        private ToolStripMenuItem interceptItem, rawTcpItem, popupsItem, profilesItem;
        private ToolStripMenuItem tamperItem;
        private ToolStripMenuItem v2rayItem, xrayItem;
        private ToolStripMenuItem instructionMapItem, vpnGateItem, torItem, advorItem, sealedTorItem, ipv9Item, sentinelItem, sdrItem, cgiItem, deepItem;
        private ToolStripMenuItem ipv7Item, ipv7WindowItem, ipv7StatusItem, ipv7SitesItem, ipv7DoorItem, ipv7StartItem;
        private AlohaCgiServer cgiServer;
        private MediaMetadataHook mediaHook;
        private ToolStripMenuItem bookmarksItem;
        private ToolStripMenuItem downloadsItem;
        private void SetupMenus()
        {

            browserStyleItem = new ToolStripMenuItem("Browser Style...");
            browserStyleItem.Click += (s, e) =>
            {
                if (styleWin == null || styleWin.IsDisposed)
                    styleWin = new BrowserStylePanel(cfg, () =>
                    {
                        GoHome();
                        if (_bmHost != null) _bmHost.SetVisible(cfg.ShowBookmarkButton);

                        try { ApplyBrowserScrollbars(); } catch { }
                        try { RefreshUrlHex(); } catch { }
                        try { if (mediaHook != null) { var t = mediaHook.SetEnabledAsync(cfg.CfgMediaCard); } } catch { }
                    });
                styleWin.Show();
                styleWin.BringToFront();
            };

            hotkeysItem = new ToolStripMenuItem("Hotkeys...");
            hotkeysItem.Click += (s, e) =>
            {
                if (hotkeysWin == null || hotkeysWin.IsDisposed) hotkeysWin = new HotkeysWindow(ReinjectHotkeys);
                hotkeysWin.Show(); hotkeysWin.BringToFront();
            };

            inspectorItem = new ToolStripMenuItem("DevTools");
            inspectorItem.Click += (s, e) => AlohaProbeConsole.Launch(cfg, this);

            bookmarksItem = new ToolStripMenuItem("Bookmarks...\tCtrl+B");
            bookmarksItem.Click += bookmarksToolStripMenuItem_Click;

            downloadsItem = new ToolStripMenuItem("Downloads Manager\tCtrl+J");
            downloadsItem.Click += downloadsToolStripMenuItem_Click;

            profilesItem = new ToolStripMenuItem("Profiles");
            profilesItem.DropDownOpening += (s, e) => RebuildProfilesMenu();
            RebuildProfilesMenu();

            v2rayItem = new ToolStripMenuItem("V2Ray...");
            v2rayItem.Click += (s, e) => OpenCoreWindow(ref v2rayWin, "v2ray");
            xrayItem = new ToolStripMenuItem("Xray...");
            xrayItem.Click += (s, e) => OpenCoreWindow(ref xrayWin, "xray");

            findInThisPageToolStripMenuItem.Click += findInThisPageToolStripMenuItem_Click;
            siteMapToolStripMenuItem.Click += siteMapToolStripMenuItem_Click;

            editToolStripMenuItem.DropDownItems.Clear();
            optionsToolStripMenuItem.DropDownItems.Clear();
            navigateToolStripMenuItem.DropDownItems.Clear();

            editToolStripMenuItem.DropDownItems.Add(findInThisPageToolStripMenuItem);
            editToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
            editToolStripMenuItem.DropDownItems.Add(browserStyleItem);
            editToolStripMenuItem.DropDownItems.Add(hotkeysItem);
            editToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
            editToolStripMenuItem.DropDownItems.Add(browserConfigToolStripMenuItem);

            EgressMenu.Wire(editToolStripMenuItem, u => Go(u));
            Aloha.Cutscenes.CutsceneMenu.Wire(editToolStripMenuItem);
            editToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
            editToolStripMenuItem.DropDownItems.Add(inspectorItem);
            editToolStripMenuItem.DropDownItems.Add(devToolsToolStripMenuItem);

            var O = optionsToolStripMenuItem.DropDownItems;
            O.Add(proxyToolStripMenuItem);
            O.Add(networkToolStripMenuItem);
            O.Add(profilesItem);
            O.Add(new ToolStripSeparator());
            O.Add(torItem);
            O.Add(advorItem);
            O.Add(sealedTorItem);
            O.Add(ipv7Item);
            O.Add(ipv9Item);
            O.Add(deepItem);
            O.Add(new ToolStripSeparator());
            O.Add(vpnGateItem);
            O.Add(v2rayItem);
            O.Add(xrayItem);
            O.Add(new ToolStripSeparator());
            O.Add(liveHeadersToolStripMenuItem);
            O.Add(interceptItem);
            O.Add(tamperItem);
            O.Add(rawTcpItem);
            O.Add(popupsItem);
            O.Add(new ToolStripSeparator());
            O.Add(cgiItem);

            var helpItem = new ToolStripMenuItem("Help");
            helpItem.Click += helpWindowToolStripMenuItem_Click;
            helpToolStripMenuItem.DropDownItems.Insert(0, helpItem);

            var blankItem = new ToolStripMenuItem("blank");
            blankItem.Click += (s, e) => GoHome();
            var newTabItem = new ToolStripMenuItem("New Tab\tCtrl+T");
            newTabItem.Click += (s, e) => NewTab();
            var tabBoardItem = new ToolStripMenuItem("Tab Board\tCtrl+Shift+M");
            tabBoardItem.Click += (s, e) => OpenTabBoard();

            var N = navigateToolStripMenuItem.DropDownItems;
            N.Add(newTabItem);
            N.Add(tabBoardItem);
            N.Add(blankItem);
            N.Add(new ToolStripSeparator());
            N.Add(bookmarksItem);
            N.Add(downloadsItem);
            N.Add(new ToolStripSeparator());
            N.Add(siteMapToolStripMenuItem);
            N.Add(instructionMapItem);
            N.Add(consoleToolStripMenuItem);
        }

        private FindWindow findWin;
        private void findInThisPageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFindWindow();
        }

        private void OpenFindWindow()
        {
            if (findWin == null || findWin.IsDisposed)
            {
                findWin = new FindWindow();
                findWin.Owner = this;
                findWin.FindRequested += async (q, mc, ww, fwd) => await RunFindAsync(q, mc, ww, fwd);
                findWin.ClearRequested += () => { try { ClearFind(); } catch { } };
            }

            string seed = null;
            findWin.Present(seed);
        }

        private async System.Threading.Tasks.Task RunFindAsync(string query, bool matchCase, bool wholeWord, bool forward)
        {
            if (!Ready) { findWin?.ShowResult(0, 0); return; }
            try
            {
                string js = FindScript
                    .Replace("QUERY",     JsString(query))
                    .Replace("MATCHCASE", matchCase ? "true" : "false")
                    .Replace("WHOLEWORD", wholeWord ? "true" : "false")
                    .Replace("FORWARD",   forward ? "true" : "false");

                string raw = await web.CoreWebView2.ExecuteScriptAsync(js);

                string s = (raw ?? "").Trim();
                if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2);
                int total = 0, current = 0;
                int colon = s.IndexOf(':');
                if (colon > 0)
                {
                    int.TryParse(s.Substring(0, colon), out total);
                    int.TryParse(s.Substring(colon + 1), out current);
                }
                findWin?.ShowResult(current, total);
            }
            catch { findWin?.ShowResult(0, 0); }
        }

        private async void ClearFind()
        {
            if (!Ready) return;
            try { await web.CoreWebView2.ExecuteScriptAsync("(function(){var ns=document.querySelectorAll('span.__aloha_hl');for(var i=0;i<ns.length;i++){var s=ns[i],p=s.parentNode;if(p){p.replaceChild(document.createTextNode(s.textContent),s);p.normalize();}}window.__alohaFindSpans=null;window.__alohaFindSig=null;})();"); }
            catch { }
        }

        private static string JsString(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
        private void button4_Click(object sender, EventArgs e)
        { if (Ready) web.CoreWebView2.Navigate("http://localhost"); }
        private void button5_Click(object sender, EventArgs e)
        { if (Ready) web.CoreWebView2.Reload(); }
        private void button6_Click(object sender, EventArgs e)
        {
            if (!Ready) return;
            string url = web.Source?.ToString() ?? "";
            textBox1.Text = url;
            if (!string.IsNullOrEmpty(url))
            {
                try { Clipboard.SetText(url); SetStatus("COPIED\r\n" + url); } catch { }
            }
        }
        private void button8_Click(object sender, EventArgs e)
        {
            try
            {
                using (var op = new OpenFileDialog())
                    if (op.ShowDialog() == DialogResult.OK && Ready)
                        web.CoreWebView2.Navigate(new Uri(op.FileName).AbsoluteUri);
            }
            catch { }
        }

        private int nextVpnIpOctet = 1;
        private void button7_Click(object sender, EventArgs e)
        {
            var vpn = VpnGateClient.Active;
            if (vpn == null || !vpn.Connected)
            {
                SetStatus("CHANGE IP\r\nno active VPN \u2014 connect one in VPN Gate first");
                return;
            }
            string ip = "10.10.11." + nextVpnIpOctet;
            nextVpnIpOctet = nextVpnIpOctet >= 254 ? 1 : nextVpnIpOctet + 1;
            string note;
            vpn.ChangeNetworkIp(ip, out note);
            SetStatus("CHANGE IP\r\n" + note);
        }
        private void textBox1_KeyDown(object sender, KeyEventArgs e)
        {

            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; e.Handled = true; }
        }
        private void textBox1_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && textBox1.Text.Trim().Length > 0) Go(textBox1.Text);
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e) => Application.Exit();
        private ToolStripMenuItem inspectorItem;

        private void devToolsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!Ready) return;
            try { web.CoreWebView2.OpenDevToolsWindow(); } catch { }
        }
        private void infoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var w = new AboutWindow();
            w.Show();
            w.BringToFront();
        }

        private void helpWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var w = new HelpWindow();
            w.Show();
            w.BringToFront();
        }

        private ProxyPanel proxyWin;
        private NetworkPanel networkWin;
        private BrowserConfigPanel browserCfgWin;
        private CoreProxyWindow v2rayWin, xrayWin;

        private DateTime lastAllowedPopup = DateTime.MinValue;
        private int      popupBurst;

        private string PopupVerdict(CoreWebView2NewWindowRequestedEventArgs a)
        {

            if (!a.IsUserInitiated) return "no gesture";

            if (!a.Uri.StartsWith("http://",  StringComparison.OrdinalIgnoreCase) &&
                !a.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !a.Uri.StartsWith("aloha://", StringComparison.OrdinalIgnoreCase) &&
                !a.Uri.StartsWith("ipv7://",  StringComparison.OrdinalIgnoreCase) &&
                !a.Uri.StartsWith("deep://",  StringComparison.OrdinalIgnoreCase))
                return "scheme";

            var f = a.WindowFeatures;
            if (f != null && (f.HasSize || f.HasPosition)) return "sized window";

            var now = DateTime.UtcNow;
            if ((now - lastAllowedPopup).TotalMilliseconds < 1200)
            {
                popupBurst++;
                return "burst x" + popupBurst;
            }
            popupBurst = 0;
            lastAllowedPopup = now;
            return null;
        }

        private readonly List<PopupEntry> popupQueue = new List<PopupEntry>();
        private PopupsWindow popupsWin;
        private void chromiumFlagsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                if (web?.CoreWebView2 != null)
                    web.CoreWebView2.Navigate(AlohaConfigBridge.PageUrl);
            }
            catch { }
        }
        private void browserConfigToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (browserCfgWin == null || browserCfgWin.IsDisposed)
                browserCfgWin = new BrowserConfigPanel(cfg, ReinitEngine);
            browserCfgWin.Show();
            browserCfgWin.BringToFront();
        }

        private void proxyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (proxyWin == null || proxyWin.IsDisposed)
                proxyWin = new ProxyPanel(cfg, ReinitEngine, () => ClearCache());
            proxyWin.Show();
            proxyWin.BringToFront();
        }

        private void RebuildProfilesMenu()
        {
            if (profilesItem == null) return;
            profilesItem.DropDownItems.Clear();

            var saveAs = new ToolStripMenuItem("Save current settings as\u2026");
            saveAs.Click += (s, e) => SaveCurrentProfile();
            profilesItem.DropDownItems.Add(saveAs);

            var names = ProfileStore.List();
            if (names.Length == 0) return;

            profilesItem.DropDownItems.Add(new ToolStripSeparator());
            foreach (var n in names)
            {
                string name = n;
                var it = new ToolStripMenuItem(name);
                it.Click += (s, e) => LoadProfile(name);
                profilesItem.DropDownItems.Add(it);
            }

            profilesItem.DropDownItems.Add(new ToolStripSeparator());
            var del = new ToolStripMenuItem("Delete");
            foreach (var n in names)
            {
                string name = n;
                var it = new ToolStripMenuItem(name);
                it.Click += (s, e) => DeleteProfile(name);
                del.DropDownItems.Add(it);
            }
            profilesItem.DropDownItems.Add(del);
        }

        private void SaveCurrentProfile()
        {
            using (var dlg = new NamePromptDialog("Save profile", "Profile name:", ""))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string name = dlg.Value;
                if (string.IsNullOrWhiteSpace(name)) return;
                if (ProfileStore.Exists(name) &&
                    MessageBox.Show(this, "Overwrite profile \"" + name + "\"?", "Profiles",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                ProfileStore.Save(name, cfg);
            }
        }

        private void LoadProfile(string name)
        {
            cfg.CopyFrom(ProfileStore.Load(name));
            cfg.Save();
            ReinitEngine();
        }

        private void DeleteProfile(string name)
        {
            if (MessageBox.Show(this, "Delete profile \"" + name + "\"?", "Profiles",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            ProfileStore.Delete(name);
        }

        private void OpenCoreWindow(ref CoreProxyWindow win, string core)
        {
            if (win == null || win.IsDisposed)
                win = new CoreProxyWindow(cfg, ReinitEngine, core);
            win.Show();
            win.BringToFront();
        }
        private void networkToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (networkWin == null || networkWin.IsDisposed)
                networkWin = new NetworkPanel(cfg, ReinitEngine);
            networkWin.Show();
            networkWin.BringToFront();
        }

        private async void ReinitEngine()
        {
            titleBar.Text = "Aloha Browser " + VERSION;
            ActivityLog.General("re-initialising engine with new config...");
            RemoveBrowserScrollbars();
            DisposeExtraTabs();
            try
            {

                uint oldPid = 0;
                try { if (web.CoreWebView2 != null) oldPid = web.CoreWebView2.BrowserProcessId; } catch { }

                try { web.Dispose(); } catch { }
                this.Controls.Remove(web);
                await WaitForBrowserExit(oldPid, 8000);

                web = new Microsoft.Web.WebView2.WinForms.WebView2();
                ((System.ComponentModel.ISupportInitialize)(web)).BeginInit();
                web.Location = new Point(0, textBox1.Bottom);
                web.Size = new Size(this.ClientSize.Width, this.ClientSize.Height - textBox1.Bottom);
                web.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                ((System.ComponentModel.ISupportInitialize)(web)).EndInit();
                ApplyEngineCreationProperties();
                this.Controls.Add(web);
                RaiseChrome();
                await InitWebAsync();
                if (tabStrip != null)
                {
                    int ai = tabStrip.ActiveIndex >= 0 ? tabStrip.ActiveIndex : 0;
                    if (tabStrip.Count == 0) tabStrip.AddTab("Home", web);
                    else tabStrip.SetView(ai, web);
                    tabStrip.SetActive(ai < tabStrip.Count ? ai : 0);
                }

            }
            catch (Exception ex)
            {
                ActivityLog.General("re-init failed: " + ex.Message);
                MessageBox.Show("Engine re-init failed:\r\n\r\n" + ex.Message,
                    "Aloha", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private ConsoleWindow consoleWin;
        private LiveHeadersPanel liveWin;
        private TamperPanel tamperWin;
        private InterceptPanel interceptWin;
        private RawTcpPanel rawTcpWin;
        private readonly InterceptProxy interceptProxy = new InterceptProxy();

        private void consoleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (consoleWin == null || consoleWin.IsDisposed)
            {
                consoleWin = new ConsoleWindow(cfg, OpenUrl);
                consoleWin.CurrentUrl = () => web != null && web.Source != null ? web.Source.ToString() : "";
                consoleWin.Bookmarks = _bookmarks;
            }
            consoleWin.Show();
            consoleWin.BringToFront();
        }

        private void liveHeadersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (liveWin == null || liveWin.IsDisposed)
                liveWin = new LiveHeadersPanel();
            liveWin.Show();
            liveWin.BringToFront();
        }

        private void tamperToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (tamperWin == null || tamperWin.IsDisposed)
                tamperWin = new TamperPanel();
            tamperWin.Show();
            tamperWin.BringToFront();
        }

        private void interceptToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (interceptWin == null || interceptWin.IsDisposed)
                interceptWin = new InterceptPanel(cfg, ReinitEngine, interceptProxy);
            interceptWin.Show();
            interceptWin.BringToFront();
        }

        private void rawTcpToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (rawTcpWin == null || rawTcpWin.IsDisposed)
                rawTcpWin = new RawTcpPanel();
            rawTcpWin.Show();
            rawTcpWin.BringToFront();
        }

        private FileExplorerWindow overlay;
        private BookmarkExplorerWindow bmOverlay;
        private Timer  b8Timer;
        private string b8Text = "New File...";
        private float  b8Off;
        private bool   b8Overflow;

        private void fileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (overlay == null || overlay.IsDisposed)
            {
                overlay = new FileExplorerWindow(OpenFileFromExplorer) { Owner = this };
                overlay.SelectionChanged += OnFileViewSelection;
                if (b8Timer == null) SetupButton8Marquee();

                this.Resize         += (s, ev) => { if (overlay != null && overlay.Visible) PositionOverlayFull(); };
                this.LocationChanged += (s, ev) => { if (overlay != null && overlay.Visible) PositionOverlayKeepSize(); };
            }
            string navPath = LocalPathFromUrlBar();

            if (overlay.Visible && navPath == null)
            {
                overlay.Hide();
                SetButton8Text("New File...");
                button8.Invalidate();
                return;
            }

            if (!overlay.Visible)
            {
                PositionOverlayFull();
                overlay.Show();
                overlay.FocusView();
            }
            if (navPath != null)
            {
                string p = navPath;
                overlay.BeginInvoke(new Action(delegate { try { overlay.NavigateTo(p); } catch { } }));
            }
            button8.Invalidate();
        }

        private void PositionOverlayFull()
        {
            if (overlay == null) return;
            int top   = (textBox1 != null ? textBox1.Top : 73) - 2;
            int left  = button8.Left;
            int right = textBox1 != null ? textBox1.Right : ClientSize.Width;
            int w = Math.Max(overlay.MinimumSize.Width,  right - left);
            int h = Math.Max(overlay.MinimumSize.Height, ClientSize.Height / 2);
            Point tl = this.PointToScreen(new Point(left, top));
            overlay.Bounds = new Rectangle(tl.X, tl.Y, w, h);
        }

        private void PositionOverlayKeepSize()
        {
            if (overlay == null) return;
            int top = (textBox1 != null ? textBox1.Top : 73) - 2;
            overlay.Location = this.PointToScreen(new Point(button8.Left, top));
        }

        private void OnFileViewSelection(string name)
        {
            SetButton8Text(string.IsNullOrEmpty(name) ? "New File..." : "New File : " + name);
        }

        private void OpenFileFromExplorer(string uri)
        {
            OpenUrl(uri);
            if (overlay != null && overlay.Visible) overlay.Hide();
            SetButton8Text("New File...");
        }

        private string LocalPathFromUrlBar()
        {
            if (textBox1 == null) return null;
            string t = (textBox1.Text ?? "").Trim().Trim('"');
            if (t.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            {
                try { t = new Uri(t).LocalPath; } catch { }
            }
            if (t.Length < 2) return null;
            bool looksLocal = (char.IsLetter(t[0]) && t[1] == ':') || t.StartsWith("\\\\");
            if (!looksLocal) return null;
            try { if (System.IO.Directory.Exists(t) || System.IO.File.Exists(t)) return t; }
            catch { }
            return null;
        }

        private void bookmarksToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (bmOverlay == null || bmOverlay.IsDisposed)
                bmOverlay = new BookmarkExplorerWindow(_bookmarks, BookmarksPath, OpenUrl);

            bmOverlay.Reload();
            bmOverlay.Show();
            bmOverlay.BringToFront();
            bmOverlay.FocusView();

            string here = (web != null && web.Source != null) ? web.Source.ToString() : "";
            if (!string.IsNullOrEmpty(here))
            {
                string u = here;
                bmOverlay.BeginInvoke(new Action(delegate { try { bmOverlay.NavigateTo(u); } catch { } }));
            }
        }

        private DownloadsWindow downloadsWin;

        private DownloadsManagerWindow dlManager;
        private void downloadsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dlManager == null || dlManager.IsDisposed)
            {
                dlManager = new DownloadsManagerWindow(_downloads,
                    () => !string.IsNullOrWhiteSpace(cfg.DownloadDir) ? cfg.DownloadDir : (Ready ? DefaultDownloadFolder()
                          : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
                    url => NewTab(url),
                    url => { if (Ready) try { web.CoreWebView2.Navigate(url); } catch { } });

                var scr = Screen.FromControl(this).WorkingArea;
                int x = Math.Max(scr.Left, Math.Min(Right - 560 - 200 - 20, scr.Right - 760));
                int y = Math.Max(scr.Top, Math.Min(Top + 80, scr.Bottom - 490));
                dlManager.Location = new Point(x, y);
            }
            dlManager.Show();
            if (dlManager.WindowState == FormWindowState.Minimized) dlManager.WindowState = FormWindowState.Normal;
            dlManager.BringToFront();
        }

        private void SetupButton8Marquee()
        {
            button8.Text = "";                 
            button8.Paint += Button8_Paint;
            b8Timer = new Timer { Interval = 40 };
            b8Timer.Tick += (s, e) => { b8Off += 1.2f; button8.Invalidate(); };
        }

        private void SetButton8Text(string t)
        {
            b8Text = t ?? "";
            b8Off = 0f;
            int avail = button8.Width - 12;
            int tw = TextRenderer.MeasureText(b8Text, button8.Font).Width;
            b8Overflow = tw > avail;
            if (b8Timer != null)
            {
                if (b8Overflow) { if (!b8Timer.Enabled) b8Timer.Start(); }
                else b8Timer.Stop();
            }
            button8.Invalidate();
        }

        private void Button8_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = button8.ClientRectangle;
            Color fg = TorrcSyntax.GreenHi;
            int pad = 6, avail = r.Width - pad * 2;
            Size tsz = TextRenderer.MeasureText(g, b8Text, button8.Font);
            if (!b8Overflow)
            {
                var tr = new Rectangle(r.X + pad, r.Y, r.Width - pad, r.Height);
                TextRenderer.DrawText(g, b8Text, button8.Font, tr, fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            else
            {
                int gap = 50, period = tsz.Width + gap;
                int o = ((int)b8Off) % period;
                int y = r.Y + (r.Height - tsz.Height) / 2;
                g.SetClip(new Rectangle(r.X + pad, r.Y, avail, r.Height));
                TextRenderer.DrawText(g, b8Text, button8.Font, new Point(r.X + pad - o, y), fg, TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, b8Text, button8.Font, new Point(r.X + pad - o + period, y), fg, TextFormatFlags.NoPrefix);
                g.ResetClip();
            }
        }

        private void networkMapToolStripMenuItem_Click(object sender, EventArgs e)
        {

            var w = new InstructionMapWindow(
                web != null && web.CoreWebView2 != null ? web.CoreWebView2.Environment : null);
            w.Show();
            w.BringToFront();
        }

        private SiteMapPanel siteMapWin;
        private void siteMapToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (siteMapWin == null || siteMapWin.IsDisposed)
                siteMapWin = new SiteMapPanel(SitePeersSnapshot, OpenUrl);
            siteMapWin.Show();
            siteMapWin.BringToFront();
        }

        public void OpenUrl(string url)
        {
            if (!Ready || string.IsNullOrWhiteSpace(url)) return;
            if (this.InvokeRequired) { this.BeginInvoke((Action)(() => OpenUrl(url))); return; }
            web.CoreWebView2.Navigate(url);
            this.BringToFront();
        }

        private void ApplyBrowserScrollbars()
        {
            RemoveBrowserScrollbars();
            if (cfg.CfgWin9xBrowserScrollbars && Ready)
            {
                vScroll = new WebScrollBar(false);
                hScroll = new WebScrollBar(true);
                scrollCorner = new Panel { BackColor = Color.FromArgb(0xEC, 0xEC, 0xEE), Cursor = Cursors.SizeNWSE };
                scrollCorner.MouseDown += Corner_MouseDown;
                scrollCorner.MouseMove += Corner_MouseMove;
                scrollCorner.MouseUp += Corner_MouseUp;
                this.Controls.Add(vScroll);
                this.Controls.Add(hScroll);
                this.Controls.Add(scrollCorner);
                vScroll.BringToFront();
                hScroll.BringToFront();
                scrollCorner.BringToFront();
                vScroll.Attach(web.CoreWebView2);
                hScroll.Attach(web.CoreWebView2);

                HideRootScrollbars(true);
                ActivityLog.General("modern browser scrollbars: on");
            }
            LayoutWeb();
            RaiseChrome();
        }

        private const string RootScrollbarCss =
            "html::-webkit-scrollbar,body::-webkit-scrollbar{width:0!important;height:0!important;display:none!important}" +
            "html,body{scrollbar-width:none!important}";
        private const string RootScrollbarScript =
            "(function(){function add(){if(document.getElementById('aloha-rootsb'))return;var s=document.createElement('style');" +
            "s.id='aloha-rootsb';s.textContent=" + "\"" + RootScrollbarCss + "\"" + ";(document.head||document.documentElement).appendChild(s);}" +
            "if(document.documentElement)add();else document.addEventListener('DOMContentLoaded',add);})();";
        private string _rootSbScriptId;
        private CoreWebView2 _rootSbCore;

        private async void HideRootScrollbars(bool on)
        {
            if (!Ready) return;
            try
            {
                if (!ReferenceEquals(_rootSbCore, web.CoreWebView2)) { _rootSbCore = web.CoreWebView2; _rootSbScriptId = null; }
                if (on && _rootSbScriptId == null)
                {
                    _rootSbScriptId = await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(RootScrollbarScript);
                    await web.CoreWebView2.ExecuteScriptAsync(RootScrollbarScript);
                }
                else if (!on && _rootSbScriptId != null)
                {
                    web.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(_rootSbScriptId);
                    _rootSbScriptId = null;
                    await web.CoreWebView2.ExecuteScriptAsync("(function(){var s=document.getElementById('aloha-rootsb');if(s)s.remove();})();");
                }
            }
            catch (Exception ex) { ActivityLog.General("root scrollbars: " + ex.Message); }
        }

        private void RemoveBrowserScrollbars()
        {
            HideRootScrollbars(false);
            if (vScroll != null) { vScroll.Detach(); this.Controls.Remove(vScroll); vScroll.Dispose(); vScroll = null; }
            if (hScroll != null) { hScroll.Detach(); this.Controls.Remove(hScroll); hScroll.Dispose(); hScroll = null; }
            if (scrollCorner != null) { this.Controls.Remove(scrollCorner); scrollCorner.Dispose(); scrollCorner = null; }
        }

        private const int SB = 17;
        private const int FT = 17;

        private const int ZB_BTN = 16, ZB_READ = 40, ZB_H = 13;
        private void BuildZoomBar(Control parent)
        {
            zoomBar = new Panel { Width = ZB_BTN * 2 + ZB_READ, Height = ZB_H, Top = 1, BackColor = cFooter };

            var minus = new Panel { Left = 0, Top = 0, Width = ZB_BTN, Height = ZB_H, Cursor = Cursors.Hand };
            zoomReadout = new Label
            {
                Left = ZB_BTN, Top = 0, Width = ZB_READ, Height = ZB_H,
                TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand,
                BackColor = Color.FromArgb(0xF4, 0xF4, 0xF4), ForeColor = Color.FromArgb(0x50, 0x50, 0x50),
                Font = new Font("Lucida Console", 7.5f, FontStyle.Bold), Text = "100%"
            };
            var plus = new Panel { Left = ZB_BTN + ZB_READ, Top = 0, Width = ZB_BTN, Height = ZB_H, Cursor = Cursors.Hand };

            minus.Paint += (s, e) => DrawZoomButton(e.Graphics, ((Panel)s).ClientRectangle, "\u2212");
            plus.Paint  += (s, e) => DrawZoomButton(e.Graphics, ((Panel)s).ClientRectangle, "+");
            minus.Click += (s, e) => SetZoom(webZoom - 0.10);
            plus.Click  += (s, e) => SetZoom(webZoom + 0.10);
            zoomReadout.Click += (s, e) => SetZoom(1.0);

            zoomBar.Controls.Add(minus);
            zoomBar.Controls.Add(zoomReadout);
            zoomBar.Controls.Add(plus);
            parent.Controls.Add(zoomBar);
            zoomBar.BringToFront();
        }

        private void DrawZoomButton(Graphics g, Rectangle r, string glyph)
        {
            using (var f = new SolidBrush(Color.FromArgb(0xF0, 0xF0, 0xF0))) g.FillRectangle(f, r);
            using (var hi = new Pen(Color.White))
            { g.DrawLine(hi, r.Left, r.Top, r.Right - 1, r.Top); g.DrawLine(hi, r.Left, r.Top, r.Left, r.Bottom - 1); }
            using (var sh = new Pen(Color.FromArgb(0xB0, 0xB0, 0xB0)))
            { g.DrawLine(sh, r.Left, r.Bottom - 1, r.Right - 1, r.Bottom - 1); g.DrawLine(sh, r.Right - 1, r.Top, r.Right - 1, r.Bottom - 1); }
            TextRenderer.DrawText(g, glyph, new Font("Lucida Console", 8f, FontStyle.Bold), r,
                Color.FromArgb(0x50, 0x50, 0x50), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void SetZoom(double z)
        {
            double want = Math.Max(0.01, Math.Round(z, 2));
            try
            {
                if (web != null && web.CoreWebView2 != null) web.ZoomFactor = want;
                webZoom = want;
            }
            catch { return; }
            if (zoomReadout != null) zoomReadout.Text = (int)Math.Round(webZoom * 100) + "%";
            cfg.WebZoom = webZoom;
        }

        private void LayoutWeb()
        {
            if (web == null) return;
            web.Anchor = AnchorStyles.None;
            int top = textBox1 != null ? textBox1.Bottom : 93;
            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;
            int bottom = h - FT;
            int webH = Math.Max(0, bottom - top);
            bool bars = cfg.CfgWin9xBrowserScrollbars && vScroll != null;

            int webX = 0, webW = w;
            if (cfg.CfgSnapViewportToGrid)
            {

                int snappedW = Math.Max(SnapStepW, (w / SnapStepW) * SnapStepW);
                int snappedH = Math.Max(SnapStepH, (webH / SnapStepH) * SnapStepH);
                webX = 0;
                webW = snappedW;
                webH = snappedH;
            }

            if (bars) web.SetBounds(webX, top, Math.Max(0, webW - SB), Math.Max(0, webH - SB));
            else      web.SetBounds(webX, top, webW, webH);
            if (bars)
            {
                vScroll.SetBounds(webX + webW - SB, top, SB, Math.Max(0, webH - SB));
                hScroll.SetBounds(webX, top + webH - SB, Math.Max(0, webW - SB), SB);
                scrollCorner.SetBounds(webX + webW - SB, top + webH - SB, SB, SB);
                vScroll.BringToFront();
                hScroll.BringToFront();
                scrollCorner.BringToFront();
            }
            if (footer != null) footer.BringToFront();
        }
    }
}
