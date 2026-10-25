using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Aloha.RingStoreCore;

namespace Aloha
{

    public class ConsoleWindow : DafyFrame
    {
        protected override bool FooterActsAsResizeGrip { get { return false; } }
        private readonly NetConfig cfg;
        private readonly Action<string> openInBrowser;
        private static readonly Color Modern = Color.FromArgb(0xF0, 0xF0, 0xF0);

        public Func<string> CurrentUrl;
        public BookmarkManager Bookmarks;

        private RichTextBox output;
        private TerminalInput termInput;

        private static readonly Color ClrBody   = Color.FromArgb(0x33, 0xFF, 0x66);
        private static readonly Color ClrHeader = Color.FromArgb(0x7C, 0xFF, 0x9E);
        private static readonly Color ClrRef    = Color.FromArgb(0x66, 0xFF, 0x99);
        private Font fReg, fBold, fUnder, fBoldUnder;

        private string reqMethod = "GET";
        private readonly Dictionary<string, string> reqHeaders = new Dictionary<string, string>();
        private string reqBody = "";

        private readonly Dictionary<string, string> results =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> presets =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static string PresetPath =>
            Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "AlohaPresets.txt");

        public ConsoleWindow(NetConfig config, Action<string> openCallback)
            : base("OPT-0900", "Console")
        {
            cfg = config;
            openInBrowser = openCallback;

            Size = new Size(900, 560 + 30);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(60, 60);
            ShowInTaskbar = true;
            ClientArea.BackColor = Color.Black;

            output = new RichTextBox
            {
                ReadOnly = true, WordWrap = false, DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.None,
                BackColor = Color.Black, ForeColor = ClrBody,
                Font = new Font("Consolas", 10f), BorderStyle = BorderStyle.None,
                Dock = DockStyle.None
            };
            fReg       = new Font(output.Font, FontStyle.Regular);
            fBold      = new Font(output.Font, FontStyle.Bold);
            fUnder     = new Font(output.Font, FontStyle.Underline);
            fBoldUnder = new Font(output.Font, FontStyle.Bold | FontStyle.Underline);
            var vbar = new DarkScrollBar(false, light: true) { Dock = DockStyle.None };
            var hbar = new DarkScrollBar(true,  light: true) { Dock = DockStyle.None };
            var corner = new Panel { BackColor = Modern };
            StyleAlohaCorner(corner, false);

            termInput = new TerminalInput(output.Font, ClrBody);
            termInput.Submitted += line => Submit(line);

            const int SB = 17;
            const int IN = 22;
            void LayoutMain()
            {
                int cw = ClientArea.ClientSize.Width;
                int ch = ClientArea.ClientSize.Height;
                if (cw < SB + 1 || ch < SB + IN + 1) return;
                output.SetBounds(0, 0, cw - SB, ch - SB - IN);
                termInput.SetBounds(0, ch - SB - IN, cw - SB, IN);
                vbar.SetBounds(cw - SB, 0, SB, ch - SB);
                hbar.SetBounds(0, ch - SB, cw - SB, SB);
                corner.SetBounds(cw - SB, ch - SB, SB, SB);
            }

            ClientArea.Controls.Add(output);
            ClientArea.Controls.Add(termInput);
            ClientArea.Controls.Add(vbar);
            ClientArea.Controls.Add(hbar);
            ClientArea.Controls.Add(corner);
            MakeResizeGrip(corner);
            ClientArea.Resize += (s, e) => LayoutMain();
            vbar.Attach(output);
            hbar.Attach(output);
            this.Shown += (s, e) => { LayoutMain(); };

            LoadPresets();
            Banner();
            Shown += (s, e) => termInput.Focus();
        }

        private void Banner()
        {
            Print("ALOHA CONSOLE");
            Print("    request crafting + structure discovery");
            Print("");
            Print("PROXY");
            Print("    " + ProxyDesc());
            Print("");
            Print("    type 'help' for commands \u2014 live headers stream in their own panel.");
            Print("");
        }

        private string ProxyDesc()
        {
            if (string.Equals(cfg.ProxyScheme, "none", StringComparison.OrdinalIgnoreCase))
                return "direct (no proxy)";
            return cfg.ProxyScheme + "://" + cfg.ProxyHost + ":" + cfg.ProxyPort;
        }

        private void Print(string s)
        {
            if (output == null || output.IsDisposed) return;
            s = s ?? "";

            if (IsHeaderLine(s))
                AppendRun(s, ClrHeader, true, false);
            else if (s.StartsWith("> "))            
            {
                AppendRun("> ", ClrHeader, true, false);
                AppendInline(s.Substring(2));
            }
            else
                AppendInline(s);

            AppendRun("\r\n", ClrBody, false, false);
            output.SelectionStart = output.TextLength;
            output.ScrollToCaret();
        }

        private void AppendRun(string text, Color color, bool bold, bool underline)
        {
            if (string.IsNullOrEmpty(text)) return;
            int start = output.TextLength;
            output.AppendText(text);
            output.Select(start, text.Length);
            output.SelectionColor = color;
            output.SelectionFont = bold ? (underline ? fBoldUnder : fBold)
                                        : (underline ? fUnder : fReg);
            output.Select(output.TextLength, 0);
        }

        private void AppendInline(string s)
        {
            if (s.Length == 0) return;
            if (s.Length > 400) { AppendRun(s, ClrBody, false, false); return; }

            int i = 0;
            while (i < s.Length)
            {
                if (s[i] == ' ' || s[i] == '\t')
                {
                    int j = i; while (j < s.Length && (s[j] == ' ' || s[j] == '\t')) j++;
                    AppendRun(s.Substring(i, j - i), ClrBody, false, false);
                    i = j;
                }
                else
                {
                    int j = i; while (j < s.Length && s[j] != ' ' && s[j] != '\t') j++;
                    string tok = s.Substring(i, j - i);
                    bool isRef = LooksLikeRef(tok);
                    AppendRun(tok, isRef ? ClrRef : ClrBody, false, isRef);
                    i = j;
                }
            }
        }

        private static bool IsHeaderLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            if (line[0] == ' ' || line[0] == '\t') return false;
            if (line.StartsWith("> ")) return false;
            if (line.StartsWith("===") || line.StartsWith("---")) return true;
            bool anyLetter = false;
            foreach (char c in line)
                if (char.IsLetter(c)) { anyLetter = true; if (char.IsLower(c)) return false; }
            return anyLetter && line.Length <= 60;
        }

        private static bool LooksLikeRef(string t)
        {
            if (string.IsNullOrEmpty(t)) return false;
            string x = t.Trim().TrimEnd('.', ',', ')', ':', ';', ']');
            if (x.Length < 3) return false;
            if (x.IndexOf("://", StringComparison.Ordinal) >= 0) return true;
            if (x[0] == '/') return true;
            if (x.EndsWith(".onion", StringComparison.OrdinalIgnoreCase)) return true;
            if (x.IndexOf('.') > 0)
            {
                bool hasLetter = false;
                foreach (char c in x)
                {
                    if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' || c == '/')) return false;
                    if (char.IsLetter(c)) hasLetter = true;
                }
                string[] parts = x.Split('.');
                return hasLetter && parts.Length >= 2 && parts[parts.Length - 1].Length >= 2;
            }
            return false;
        }

        private void Submit(string raw)
        {
            string line = (raw ?? "").Trim();
            if (line.Length == 0) return;
            Print("> " + line);
            try { Dispatch(line); }
            catch (Exception ex) { Print("error: " + ex.Message); }
        }

        private void Dispatch(string line)
        {
            string[] parts = line.Split(new[] { ' ' }, 2);
            string cmd = parts[0].ToLowerInvariant();
            string arg = parts.Length > 1 ? parts[1].Trim() : "";

            switch (cmd)
            {
                case "help": Help(); break;
                case "clear": output.Clear(); break;
                case "proxy": Print("console proxy: " + ProxyDesc()); break;

                case "save": SaveCmd(); break;

                case "method":
                    if (arg.Length > 0) { reqMethod = arg.ToUpperInvariant(); Print("method = " + reqMethod); }
                    else Print("method = " + reqMethod);
                    break;
                case "header": HeaderCmd(arg); break;
                case "headers": ShowHeaders(); break;
                case "body": reqBody = arg; Print("body set (" + reqBody.Length + " chars)"); break;

                case "get": _ = DoRequest("GET", arg, true); break;
                case "req": _ = ReqGuided(); break;

                case "preset": PresetCmd(arg); break;

                case "wayback": _ = Wayback(arg); break;
                case "discover": _ = Discover(arg); break;

                case "archive": _ = Archive(arg); break;

                case "results": ShowResults(); break;
                case "open": OpenResult(arg); break;
                case "bookmarks":
                case "bm": BookmarksCmd(); break;

                default: Print("unknown command: " + cmd + "   (type 'help')"); break;
            }
        }

        private void Help()
        {
            Print("COMMANDS");
            Print("    help              show this list");
            Print("    clear             clear the screen");
            Print("    proxy             show the proxy the console uses");
            Print("    save              save this output to a file");
            Print("");
            Print("ARCHIVE  \u00b7  DISCOVERY");
            Print("    archive [URL]     save a fresh snapshot to web.archive.org");
            Print("                      no URL = the page open in the browser");
            Print("    wayback URL       list existing archive.org snapshots");
            Print("    discover DOMAIN   crt.sh subdomains + sitemap + robots + CDX");
            Print("");
            Print("RESULTS");
            Print("    bookmarks  (bm)   list saved bookmarks as open-able results");
            Print("    results           re-show the current named results");
            Print("    open NAME         open a named result in the browser");
            Print("");
            Print("REQUEST CRAFTING");
            Print("    method NAME       set method (GET, POST, HEAD, PUT, DELETE)");
            Print("    header NAME VAL   stage a header for the next request");
            Print("    headers           show staged headers");
            Print("    body TEXT         set the request body");
            Print("    get URL           quick GET, prints the raw response");
            Print("    req               fire the staged request (asks for URL)");
            Print("    preset save|load|list NAME");
            Print("");
            Print("    live request/response headers stream in the 'Live headers' panel.");
        }

        private async Task Archive(string arg)
        {
            string url = string.IsNullOrWhiteSpace(arg)
                       ? (CurrentUrl != null ? CurrentUrl() : "")
                       : arg.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                Print("usage: archive [URL]   (no URL = the page open in the browser)");
                return;
            }
            if (!url.Contains("://")) url = "http://" + url;

            Print("archiving via web.archive.org: " + url);
            Print("(Save Page Now — this can take 10-30s)");
            try
            {
                using (var client = BuildClient())
                {
                    var resp = await client.GetAsync("https://web.archive.org/save/" + url);
                    Print("HTTP " + (int)resp.StatusCode + " " + resp.StatusCode);

                    string snap = null;
                    IEnumerable<string> v;
                    if (resp.Headers.TryGetValues("Content-Location", out v))
                        foreach (var s in v) { snap = "https://web.archive.org" + s; break; }
                    if (snap == null && resp.Headers.Location != null)
                    {
                        string loc = resp.Headers.Location.ToString();
                        snap = loc.StartsWith("http") ? loc : "https://web.archive.org" + loc;
                    }

                    if (snap != null)
                    {
                        results["archive"] = snap;
                        Print("snapshot: " + snap);
                        Print("type 'open archive' to view it.");
                    }
                    else
                    {
                        Print("requested. a snapshot URL wasn't returned right away —");
                        Print("try:  wayback " + url + "   in a moment to list it.");
                    }
                }
            }
            catch (Exception ex) { Print("archive failed: " + ex.Message); }
        }

        private void BookmarksCmd()
        {
            if (Bookmarks == null || Bookmarks.Items == null || Bookmarks.Items.Count == 0)
            {
                Print("(no bookmarks yet — click the \u2605 in the footer on a page you like)");
                return;
            }
            Print("bookmarks  —  type 'open <name>':");
            int i = 1;
            foreach (Bookmark b in Bookmarks.Items)
            {
                string name = "b" + i;
                results[name] = b.Url;
                string title = string.IsNullOrEmpty(b.Title) ? (b.Host ?? "") : b.Title;
                if (title.Length > 26) title = title.Substring(0, 25) + "\u2026";
                Print("  " + name.PadRight(5) + title.PadRight(28) + b.Url);
                i++;
            }
        }

        private void SaveCmd()
        {
            try
            {
                using (var sf = new SaveFileDialog { Filter = "Log files|*.log|Text|*.txt", FileName = "aloha-console.log" })
                    if (sf.ShowDialog() == DialogResult.OK)
                    { File.WriteAllText(sf.FileName, output.Text); Print("saved -> " + sf.FileName); }
            }
            catch (Exception ex) { Print("save failed: " + ex.Message); }
        }

        private void HeaderCmd(string arg)
        {
            var p = arg.Split(new[] { ' ' }, 2);
            if (p.Length < 2) { Print("usage: header NAME VALUE"); return; }
            reqHeaders[p[0]] = p[1];
            Print("header staged: " + p[0] + ": " + p[1]);
        }

        private void ShowHeaders()
        {
            Print("method " + reqMethod);
            if (reqHeaders.Count == 0) Print("(no staged headers)");
            foreach (var kv in reqHeaders) Print("  " + kv.Key + ": " + kv.Value);
            if (reqBody.Length > 0) Print("body: " + reqBody.Length + " chars");
        }

        private async Task ReqGuided()
        {
            string url = Prompt("Request URL:", "http://");
            if (string.IsNullOrWhiteSpace(url)) { Print("cancelled"); return; }
            await DoRequest(reqMethod, url, false);
        }

        private HttpClient BuildClient()
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            if (!string.Equals(cfg.ProxyScheme, "none", StringComparison.OrdinalIgnoreCase))
            {

                if (cfg.ProxyScheme.StartsWith("http"))
                    handler.Proxy = new WebProxy(cfg.ProxyHost + ":" + cfg.ProxyPort, false);
                handler.UseProxy = cfg.ProxyScheme.StartsWith("http");
            }
            var c = new HttpClient(handler);
            c.Timeout = TimeSpan.FromSeconds(40);
            return c;
        }

        private async Task DoRequest(string method, string url, bool quick)
        {
            if (string.IsNullOrWhiteSpace(url)) { Print("usage: get URL  (or set method then 'req')"); return; }
            if (!url.Contains("://")) url = "http://" + url;

            if (!string.Equals(cfg.ProxyScheme, "none", StringComparison.OrdinalIgnoreCase)
                && !cfg.ProxyScheme.StartsWith("http"))
            {
                Print("note: console uses a SOCKS proxy (" + ProxyDesc() + ").");
                Print("      .NET Framework's HttpClient can't do SOCKS directly, so this");
                Print("      request goes DIRECT. (The browser itself still uses Tor.)");
                Print("      For .onion names from the console, see the notes.");
            }

            Print("--- " + method + " " + url + " ---");
            try
            {
                using (var client = BuildClient())
                using (var msg = new HttpRequestMessage(new HttpMethod(method), url))
                {
                    foreach (var kv in reqHeaders)
                    {
                        if (!msg.Headers.TryAddWithoutValidation(kv.Key, kv.Value))
                        {  }
                    }
                    if (reqBody.Length > 0 && method != "GET" && method != "HEAD")
                        msg.Content = new StringContent(reqBody);

                    var resp = await client.SendAsync(msg);
                    Print("HTTP " + (int)resp.StatusCode + " " + resp.StatusCode);
                    foreach (var h in resp.Headers)
                        Print("  " + h.Key + ": " + string.Join(", ", h.Value));
                    if (resp.Content != null)
                        foreach (var h in resp.Content.Headers)
                            Print("  " + h.Key + ": " + string.Join(", ", h.Value));

                    string text = resp.Content != null ? await resp.Content.ReadAsStringAsync() : "";
                    if (text.Length > 4000) text = text.Substring(0, 4000) + "\r\n...[truncated]";
                    Print("");
                    Print(text);
                    Print("--- end ---");
                }
            }
            catch (Exception ex) { Print("request failed: " + ex.Message); }
        }

        private void PresetCmd(string arg)
        {
            var p = arg.Split(new[] { ' ' }, 2);
            string sub = p.Length > 0 ? p[0].ToLowerInvariant() : "";
            string name = p.Length > 1 ? p[1].Trim() : "";
            if (sub == "list")
            {
                if (presets.Count == 0) { Print("(no presets)"); return; }
                foreach (var k in presets.Keys) Print("  " + k);
            }
            else if (sub == "save" && name.Length > 0)
            {
                var sb = new StringBuilder();
                sb.Append(reqMethod).Append("\t").Append(reqBody.Replace("\t", " ").Replace("\n", " "));
                foreach (var kv in reqHeaders) sb.Append("\t").Append(kv.Key).Append("=").Append(kv.Value);
                presets[name] = sb.ToString();
                SavePresets();
                Print("preset saved: " + name);
            }
            else if (sub == "load" && name.Length > 0)
            {
                if (!presets.ContainsKey(name)) { Print("no such preset: " + name); return; }
                var fields = presets[name].Split('\t');
                reqMethod = fields[0];
                reqBody = fields.Length > 1 ? fields[1] : "";
                reqHeaders.Clear();
                for (int i = 2; i < fields.Length; i++)
                {
                    var hp = fields[i].Split(new[] { '=' }, 2);
                    if (hp.Length == 2) reqHeaders[hp[0]] = hp[1];
                }
                Print("preset loaded: " + name);
                ShowHeaders();
            }
            else Print("usage: preset save NAME | preset load NAME | preset list");
        }

        private void LoadPresets()
        {
            try
            {
                if (!File.Exists(PresetPath)) return;
                foreach (var line in File.ReadAllLines(PresetPath))
                {
                    int eq = line.IndexOf('|');
                    if (eq > 0) presets[line.Substring(0, eq)] = line.Substring(eq + 1);
                }
            }
            catch { }
        }
        private void SavePresets()
        {
            try
            {
                var lines = presets.Select(kv => kv.Key + "|" + kv.Value);
                File.WriteAllLines(PresetPath, lines);
            }
            catch { }
        }

        private async Task Wayback(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) { Print("usage: wayback URL"); return; }
            Print("querying archive.org CDX for snapshots of " + url + " ...");
            string api = "http://web.archive.org/cdx/search/cdx?url=" +
                         Uri.EscapeDataString(url) +
                         "&output=text&fl=timestamp,original&collapse=timestamp:8&limit=40";
            try
            {
                using (var c = new HttpClient())
                {
                    c.Timeout = TimeSpan.FromSeconds(40);
                    string txt = await c.GetStringAsync(api);
                    var lines = txt.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length == 0) { Print("no snapshots found."); return; }
                    results.Clear();
                    int i = 1;
                    foreach (var ln in lines)
                    {
                        var f = ln.Split(' ');
                        if (f.Length < 2) continue;
                        string ts = f[0], orig = f[1];
                        string snap = "http://web.archive.org/web/" + ts + "/" + orig;
                        string name = "s" + i++;
                        results[name] = snap;
                        Print("  " + name + "   " + FormatTs(ts) + "   " + orig);
                    }
                    Print("type 'open s1' (etc.) to open a snapshot in the browser.");
                }
            }
            catch (Exception ex) { Print("wayback failed: " + ex.Message); }
        }

        private string FormatTs(string ts)
        {
            if (ts.Length >= 8)
                return ts.Substring(0, 4) + "-" + ts.Substring(4, 2) + "-" + ts.Substring(6, 2);
            return ts;
        }

        private int discoverIdx = 1;
        private int pathIdx = 1;

        private async Task Discover(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain)) { Print("usage: discover DOMAIN"); return; }
            domain = domain.Replace("http://", "").Replace("https://", "").TrimEnd('/');
            results.Clear();
            discoverIdx = 1;
            pathIdx = 1;
            Print("=== discovering structure of " + domain + " (public records only) ===");

            Print("[crt.sh] certificate transparency subdomains...");
            try
            {
                using (var c = new HttpClient())
                {
                    c.Timeout = TimeSpan.FromSeconds(40);
                    c.DefaultRequestHeaders.Add("User-Agent", "Aloha");
                    string j = await c.GetStringAsync("https://crt.sh/?q=%25." + Uri.EscapeDataString(domain) + "&output=json");
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (System.Text.RegularExpressions.Match m in
                             System.Text.RegularExpressions.Regex.Matches(j, "\"common_name\":\"([^\"]+)\""))
                        names.Add(m.Groups[1].Value);
                    foreach (System.Text.RegularExpressions.Match m in
                             System.Text.RegularExpressions.Regex.Matches(j, "\"name_value\":\"([^\"]+)\""))
                        foreach (var n in m.Groups[1].Value.Split('\\', '\n'))
                            if (n.Contains(".")) names.Add(n.Trim());
                    foreach (var n in names.Where(x => !x.StartsWith("*")).Distinct().Take(40))
                    {
                        string name = "h" + discoverIdx++;
                        results[name] = "http://" + n;
                        Print("  " + name + "   " + n);
                    }
                }
            }
            catch (Exception ex) { Print("  crt.sh failed: " + ex.Message); }

            Print("[robots.txt] published paths...");
            await FetchPaths("http://" + domain + "/robots.txt", isRobots: true);

            Print("[sitemap.xml] published URLs...");
            await FetchPaths("http://" + domain + "/sitemap.xml", isRobots: false);

            Print("type 'open h1' / 'open p3' (etc.) to open in the browser. 'results' re-lists.");
        }

        private async Task FetchPaths(string url, bool isRobots)
        {
            try
            {
                using (var c = new HttpClient())
                {
                    c.Timeout = TimeSpan.FromSeconds(25);
                    c.DefaultRequestHeaders.Add("User-Agent", "Aloha");
                    string txt = await c.GetStringAsync(url);
                    var found = new List<string>();
                    if (isRobots)
                    {
                        foreach (var ln in txt.Split('\n'))
                        {
                            var t = ln.Trim();
                            if (t.StartsWith("Disallow:") || t.StartsWith("Allow:") || t.StartsWith("Sitemap:"))
                            {
                                var v = t.Substring(t.IndexOf(':') + 1).Trim();

                                if (v.Length > 0 && v.IndexOf('*') < 0 && v.IndexOf('$') < 0) found.Add(v);
                            }
                        }
                    }
                    else
                    {
                        foreach (System.Text.RegularExpressions.Match m in
                                 System.Text.RegularExpressions.Regex.Matches(txt, "<loc>([^<]+)</loc>"))
                            found.Add(m.Groups[1].Value.Trim());
                    }
                    foreach (var f in found.Distinct().Take(30))
                    {
                        string name = "p" + pathIdx++;
                        string full = f.StartsWith("http") ? f :
                                      ("http://" + url.Replace("http://", "").Split('/')[0] + (f.StartsWith("/") ? "" : "/") + f);
                        results[name] = full;
                        Print("  " + name + "   " + f);
                    }
                    if (found.Count == 0) Print("  (none)");
                }
            }
            catch (Exception ex) { Print("  " + url + " -> " + ex.Message); }
        }

        private void ShowResults()
        {
            if (results.Count == 0) { Print("(no results — run wayback or discover)"); return; }
            foreach (var kv in results) Print("  " + kv.Key + "   " + kv.Value);
        }

        private void OpenResult(string name)
        {
            name = name.Trim();
            if (name.Length == 0) { Print("usage: open NAME   (see 'results')"); return; }
            if (!results.ContainsKey(name)) { Print("no result named '" + name + "'. type 'results'."); return; }
            string url = results[name];
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != "http" && u.Scheme != "https"))
            {
                Print("can't open '" + name + "' \u2014 not a navigable URL: " + url);
                return;
            }
            Print("opening " + url + " in browser...");
            openInBrowser?.Invoke(url);
        }

        private string Prompt(string label, string def)
        {
            using (var f = new Form())
            {
                f.Text = "Aloha"; f.Size = new Size(560, 130);
                f.StartPosition = FormStartPosition.CenterParent;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MinimizeBox = false; f.MaximizeBox = false;
                var lbl = new Label { Text = label, Left = 10, Top = 10, Width = 520 };
                var tb = new TextBox { Left = 10, Top = 32, Width = 520, Text = def };
                var ok = new Button { Text = "OK", Left = 374, Top = 62, Width = 70, DialogResult = DialogResult.OK };
                var cn = new Button { Text = "Cancel", Left = 450, Top = 62, Width = 80, DialogResult = DialogResult.Cancel };
                f.Controls.Add(lbl); f.Controls.Add(tb); f.Controls.Add(ok); f.Controls.Add(cn);
                f.AcceptButton = ok; f.CancelButton = cn;
                return f.ShowDialog() == DialogResult.OK ? tb.Text.Trim() : "";
            }
        }
    }
}
