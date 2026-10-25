using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace Aloha
{

    public class AlohaInspectorWindow : DafyFrame
    {
        protected override bool FooterActsAsResizeGrip { get { return false; } }

        private static readonly Color Modern    = Color.FromArgb(0xF0, 0xF0, 0xF0);
        private static readonly Color ClrHeader = Color.White;
        private static readonly Color ClrBody   = Color.FromArgb(0xA0, 0x00, 0x21);
        private static readonly Color ClrSelBg  = Color.FromArgb(0x55, 0x00, 0x16);
        private static readonly Color ClrFab    = Color.FromArgb(0xFA, 0xFA, 0xFB);
        private static readonly Color ClrPrev   = Color.FromArgb(0x5A, 0x00, 0x16);

        private readonly CoreWebView2 core;
        private RichTextBox output, sidebar;
        private Panel panelHost;
        private TextBox input;
        private Font fReg, fBold;

        private CoreWebView2DevToolsProtocolEventReceiver consoleRx;
        private CoreWebView2DevToolsProtocolEventReceiver exceptionRx;
        private EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs> consoleHandler;
        private EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs> exceptionHandler;
        private CoreWebView2DevToolsProtocolEventReceiver netReqRx;
        private CoreWebView2DevToolsProtocolEventReceiver netRespRx;
        private EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs> netReqHandler;
        private EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs> netRespHandler;
        private bool monitoring;

        private readonly List<string> history = new List<string>();
        private int historyIdx;

        private enum Nav { None, Forms }
        private Nav nav = Nav.None;
        private List<FormEntry> formRows;
        private int sel;
        private int esel = -1;

        private const int SB  = 16;
        private const int SBW = 256;
        private const int IN  = 20;
        private class FormEntry { public int Index; public string Summary; public List<string> Fields = new List<string>(); }

        private int detailFormIndex;
        private List<string> detailFields;
        private List<string> predicted;
        private List<string> lastFrameLabels;
        private string lastSeedScope;
        private const uint TVERSION = 1;
        private List<string> descendParams;
        private string descendScope;
        private int descendFieldIdx = -1;
        private List<string> descendVals;
        private List<string> descendPins;
        private List<string> descendTopo;
        private string descendBaseScope;
        private int descendDepth = 1;

        private static readonly Color ClrCmd = Color.FromArgb(11, 122, 130);
        private readonly Dictionary<int, string> paletteCmd = new Dictionary<int, string>();
        private readonly Dictionary<int, int> formRowLine = new Dictionary<int, int>();

        public AlohaInspectorWindow(CoreWebView2 core) : base("DEVTOOLS", "DevTools")
        {
            this.core = core;

            Size = new Size(1088, 480);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(80, 80);
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
            fReg  = new Font(output.Font, FontStyle.Regular);
            fBold = new Font(output.Font, FontStyle.Bold);

            sidebar = new RichTextBox
            {
                ReadOnly = true, WordWrap = false, DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.None,
                BackColor = ClrFab, ForeColor = Color.Black,
                Font = new Font("Consolas", 10f), BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill, TabStop = false
            };
            sidebar.GotFocus += (s, e) => { try { input.Focus(); } catch { } };
            sidebar.MouseDown += Sidebar_MouseDown;

            panelHost = new Panel { BackColor = ClrFab, Padding = new Padding(2) };
            panelHost.Controls.Add(sidebar);

            var vbar   = new DarkScrollBar(false, light: true) { Dock = DockStyle.None };
            var hbar   = new DarkScrollBar(true,  light: true) { Dock = DockStyle.None };
            var corner = new Panel { BackColor = Modern };
            StyleAlohaCorner(corner, false);

            input = new TextBox
            {
                BackColor = Color.Black, ForeColor = ClrBody,
                Font = new Font("Consolas", 10f), BorderStyle = BorderStyle.None,
                HideSelection = true
            };
            input.KeyDown += Input_KeyDown;
            input.GotFocus += (s, e) => { input.SelectionLength = 0; MakeBlockCaret(input); };
            input.LostFocus += (s, e) => { try { DestroyCaret(); } catch { } };

            HideFooterCubes();

            void LayoutMain()
            {
                int cw = ClientArea.ClientSize.Width;
                int ch = ClientArea.ClientSize.Height;
                if (cw < SBW + SB + 40 || ch < SB + IN + 1) return;
                int rightEdge = cw - SBW;
                output.SetBounds(0, 0, rightEdge - SB, ch - SB - IN);
                input.SetBounds(0, ch - SB - IN, rightEdge - SB, IN);
                vbar.SetBounds(rightEdge - SB, 0, SB, ch - SB);
                hbar.SetBounds(0, ch - SB, rightEdge - SB, SB);
                corner.SetBounds(rightEdge - SB, ch - SB, SB, SB);
                panelHost.SetBounds(rightEdge, 0, SBW, ch);
            }

            ClientArea.Controls.Add(panelHost);
            ClientArea.Controls.Add(output);
            ClientArea.Controls.Add(input);
            ClientArea.Controls.Add(vbar);
            ClientArea.Controls.Add(hbar);
            ClientArea.Controls.Add(corner);
            MakeResizeGrip(corner);
            ClientArea.Resize += (s, e) => LayoutMain();
            vbar.Attach(output);
            hbar.Attach(output);

            this.Shown += (s, e) => { LayoutMain(); input.Focus(); };
            Banner();

            Load += async (s, e) => await WireAsync();
            FormClosed += (s, e) => Unwire();
        }

        private void Banner()
        {
            PrintHeader("Aloha browser " + Form1.VERSION + " DevTool");
            Print("    PolyForm Noncommercial 1.0.0 -- see LICENSE");
            Print("");
            RenderCommandPalette();
        }

        private void RenderCommandPalette()
        {
            if (sidebar == null || sidebar.IsDisposed) return;
            sidebar.Clear();
            paletteCmd.Clear();
            formRowLine.Clear();
            PaletteEntry("FORMS", "forms", true);
            PaletteEntry("HELP",  "help",  false);
            PaletteEntry("CLEAR", "clear", false);
            try { sidebar.Select(0, 0); } catch { }
        }

        private void PaletteEntry(string header, string cmd, bool first)
        {
            if (!first) AppendTo(sidebar, "\r\n", Color.Black, false, (Color?)ClrFab);
            AppendTo(sidebar, header + "\r\n", Color.Black, true, (Color?)ClrFab);
            int line = sidebar.GetLineFromCharIndex(sidebar.TextLength);
            AppendTo(sidebar, "  run ", Color.Black, false, (Color?)ClrFab);
            AppendTo(sidebar, "'" + cmd + "'\r\n", ClrCmd, false, (Color?)ClrFab);
            paletteCmd[line] = cmd;
        }

        private void Sidebar_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int ci = sidebar.GetCharIndexFromPosition(e.Location);
            int line = sidebar.GetLineFromCharIndex(ci);
            string cmd;
            if (paletteCmd.TryGetValue(line, out cmd))
            {
                input.Text = cmd;
                Submit();
                try { input.Focus(); } catch { }
                return;
            }
            int row;
            if (formRowLine.TryGetValue(line, out row))
            {
                SelectForm(row);
                try { input.Focus(); } catch { }
            }
        }

        private async System.Threading.Tasks.Task WireAsync()
        {
            try
            {
                consoleHandler   = (s, e) => OnConsole(e.ParameterObjectAsJson);
                exceptionHandler = (s, e) => OnException(e.ParameterObjectAsJson);
                consoleRx   = core.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled");
                exceptionRx = core.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown");
                consoleRx.DevToolsProtocolEventReceived   += consoleHandler;
                exceptionRx.DevToolsProtocolEventReceived += exceptionHandler;
                await core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
                Print("console is live \u2014 type JS, or 'forms' / 'help'.");
                Print("");
            }
            catch (Exception ex) { Print("inspector init error: " + ex.Message); }
        }

        private void Unwire()
        {
            try
            {
                if (consoleRx   != null && consoleHandler   != null) consoleRx.DevToolsProtocolEventReceived   -= consoleHandler;
                if (exceptionRx != null && exceptionHandler != null) exceptionRx.DevToolsProtocolEventReceived -= exceptionHandler;
                if (netReqRx    != null && netReqHandler    != null) netReqRx.DevToolsProtocolEventReceived     -= netReqHandler;
                if (netRespRx   != null && netRespHandler   != null) netRespRx.DevToolsProtocolEventReceived    -= netRespHandler;
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool CreateCaret(IntPtr h, IntPtr bmp, int w, int ht);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowCaret(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyCaret();

        private void MakeBlockCaret(TextBox tb)
        {
            try
            {
                int w = TextRenderer.MeasureText("0", tb.Font).Width;
                CreateCaret(tb.Handle, (IntPtr)1, Math.Max(6, w), tb.Font.Height);
                ShowCaret(tb.Handle);
            }
            catch { }
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && nav == Nav.Forms)
            {
                e.SuppressKeyPress = true; nav = Nav.None; esel = -1;
                if (monitoring) { _ = StopMonitor(); }
                RenderCommandPalette();
                Print("(closed forms)");
                return;
            }

            if (nav == Nav.Forms && input.Text.Length == 0)
            {
                if (e.KeyCode == Keys.Up)       { e.SuppressKeyPress = true; SelectElement(esel - 1); return; }
                if (e.KeyCode == Keys.Down)     { e.SuppressKeyPress = true; SelectElement(esel + 1); return; }
                if (e.KeyCode == Keys.PageUp)   { e.SuppressKeyPress = true; SelectForm(sel - 1); return; }
                if (e.KeyCode == Keys.PageDown) { e.SuppressKeyPress = true; SelectForm(sel + 1); return; }
                if (e.KeyCode == Keys.Enter)    { e.SuppressKeyPress = true; RunOp('a'); return; }
            }
            if (e.KeyCode == Keys.Up)    { e.SuppressKeyPress = true; StepHistory(-1); return; }
            if (e.KeyCode == Keys.Down)  { e.SuppressKeyPress = true; StepHistory(+1); return; }
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Submit(); }
        }

        private void SelectForm(int i)
        {
            if (formRows == null || formRows.Count == 0) return;
            sel = Math.Max(0, Math.Min(formRows.Count - 1, i));
            detailFormIndex = formRows[sel].Index;
            detailFields = formRows[sel].Fields;
            esel = -1;
            RenderPanel();
            RenderFormPane();
        }

        private void SelectElement(int i)
        {
            if (detailFields == null) return;
            int n = detailFields.Count;
            if (i < -1) i = -1;
            if (i > n - 1) i = n - 1;
            esel = i;
            RenderFormPane();
        }

        private void RenderPanel()
        {
            if (sidebar == null || sidebar.IsDisposed) return;
            sidebar.Clear();
            paletteCmd.Clear();
            formRowLine.Clear();

            AppendTo(sidebar, "FORMS  (" + formRows.Count + ")\r\n", Color.Black, true, (Color?)ClrFab);
            for (int i = 0; i < formRows.Count; i++)
            {
                int line = sidebar.GetLineFromCharIndex(sidebar.TextLength);
                AppendTo(sidebar, "  " + (i == sel ? "> " : "  ") + ShortFormLabel(formRows[i]) + "\r\n",
                         Color.Black, false, (Color?)ClrFab);
                formRowLine[line] = i;
            }

            int total = 0;
            foreach (var fe in formRows) total += fe.Fields.Count;
            string host = (formRows.Count > 0) ? HostOf(formRows[sel]) : "";
            PanelBlock("PAGE", new List<string> {
                "host:   " + (string.IsNullOrEmpty(host) ? "(none)" : host),
                "forms:  " + formRows.Count,
                "fields: " + total
            }, false);

            try { sidebar.Select(0, 0); } catch { }
        }

        private void PanelBlock(string header, List<string> lines, bool first)
        {
            if (!first) AppendTo(sidebar, "\r\n", Color.Black, false, (Color?)ClrFab);
            AppendTo(sidebar, header + "\r\n", Color.Black, true, (Color?)ClrFab);
            foreach (var ln in lines)
                AppendTo(sidebar, "  " + ln + "\r\n", Color.Black, false, (Color?)ClrFab);
        }

        private static string HostOf(FormEntry f)
        {
            var parts = f.Summary.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4)
            {
                string last = parts[parts.Length - 1];
                try { return new Uri(last).Host; } catch { return last; }
            }
            return "";
        }

        private void RenderFormPane()
        {
            if (output == null || output.IsDisposed) return;
            predicted = null;
            output.Clear();
            PrintHeader("Aloha browser " + Form1.VERSION + " DevTool");
            Print("");
            PrintSection(formRows[sel].Summary);
            Print("  fields: " + detailFields.Count + "     \u2191\u2193 element \u00B7 PgUp/PgDn form \u00B7 Esc close");
            Print("");

            bool whole = (esel < 0);
            AppendRun((whole ? "> " : "  ") + "\u25C6 whole form \u2014 no divide   (operators with no <N> act here)\r\n",
                      whole ? Color.White : ClrBody, true, whole ? (Color?)ClrSelBg : null);
            for (int i = 0; i < detailFields.Count; i++)
            {
                string nm = FieldAttr(detailFields[i], "name");
                string ty = FieldAttr(detailFields[i], "type");
                string val = FieldValue(detailFields[i]);
                bool s = (i == esel);
                AppendRun((s ? "> " : "  ") + "[" + i + "] " + (string.IsNullOrEmpty(nm) ? "(none)" : nm)
                        + "  (" + (string.IsNullOrEmpty(ty) ? "?" : ty) + ")  = "
                        + (string.IsNullOrEmpty(val) ? "(empty)" : val) + "\r\n",
                        s ? Color.White : ClrBody, false, s ? (Color?)ClrSelBg : null);
            }
            Print("");
            PrintSection("operators");
            Print("  inspect   fill   probe   monitor   predict");
            Print("  the selected row is the target \u2014 or address one: inspect <0> \u00B7 fill <0> value \u00B7 probe <0> \u00B7 predict <0>");
            Print("  after probe finds hits: descend <n> pins the found value and probes one ring deeper");
            Print("  or  send <n> value  pins your own value (special chars ok) and goes deeper");
            Print("  each ring holds the pins \u2014 inputs that only appear in combination surface");
        }

        private static string FieldValue(string line)
        {
            int vi = line.IndexOf("value=");
            if (vi < 0) return "";
            string rest = line.Substring(vi + 6);
            int ph = rest.IndexOf(" ph=");
            if (ph >= 0) rest = rest.Substring(0, ph);
            return rest.Trim();
        }

        private static string ShortFormLabel(FormEntry f)
        {
            var parts = f.Summary.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string num = "", method = "", host = "";
            for (int i = 0; i < parts.Length; i++) if (parts[i].StartsWith("#")) num = parts[i];
            if (parts.Length >= 3) method = parts[2];
            if (parts.Length >= 4)
            {
                string last = parts[parts.Length - 1];
                try { host = new Uri(last).Host; } catch { host = last; }
                if (string.IsNullOrEmpty(host)) host = last;
            }
            return (num + " " + method + " " + host).Trim();
        }

        private async void RunOp(char op, int? ptr = null, string value = "")
        {
            if (nav != Nav.Forms) return;

            int? eff = ptr ?? (esel >= 0 ? (int?)esel : null);
            bool set; int idx;
            if (eff.HasValue) { set = false; idx = eff.Value; }
            else { set = true; idx = -1; }

            int count = (detailFields == null) ? 0 : detailFields.Count;
            if (!set && (idx < 0 || idx >= count))
            {
                Print("no element <" + idx + ">  (range 0.." + (count - 1) + ")");
                return;
            }

            string scope = set ? "whole form (no divide)" : ("field [" + idx + "]");
            switch (op)
            {
                case 'a':
                    if (set) await InspectSet(); else await InspectField(idx);
                    return;
                case 'b':
                    if (set) await FireSet(); else await FillField(idx, value);
                    return;
                case 'c':
                    if (set) await ProbeSet(); else await ProbeField(idx);
                    return;
                case 'e':
                    if (set) PredictSet(); else PredictField(idx);
                    return;
                case 'd':
                    await ToggleMonitor();
                    return;
                default:
                    Print(OpName(op) + " \u2192 " + scope + "  (next)");
                    return;
            }
        }

        private async Task FillField(int j, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                Print("fill: type  fill <value>  to set this field (e.g. fill hello)");
                return;
            }
            try
            {
                string js =
                    "(function(n,j,v){var f=document.forms[n];if(!f)return 'form gone';var el=f.elements[j];if(!el)return 'field gone';"
                  + "var old=el.value;try{el.value=v;el.dispatchEvent(new Event('input',{bubbles:true}));el.dispatchEvent(new Event('change',{bubbles:true}));}catch(e){}"
                  + "return 'FILL  name='+(el.name||el.id||'(none)')+'\\n  old='+(old||'(empty)')+'\\n  new='+(el.value||'(empty)');})("
                  + detailFormIndex + "," + j + "," + JsonString(value) + ")";
                string p = "{\"expression\":" + JsonString(js) + ",\"returnByValue\":true}";
                string res = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", p);
                string bdy = JStr(res, "value") ?? RenderEval(res);
                Print("");
                foreach (var l in bdy.Split('\n'))
                {
                    string tt = l.TrimStart();
                    if (tt.StartsWith("FILL")) PrintSection(l); else Print(l);
                }
            }
            catch (Exception ex) { Print("fill error: " + ex.Message); }
        }

        private async Task FireSet()
        {
            try
            {
                string js =
                    "(function(n){var f=document.forms[n];if(!f)return Promise.resolve('form gone');"
                  + "var m=(f.method||'get').toUpperCase();var act=f.action||location.href;"
                  + "var fd;try{fd=new FormData(f);}catch(e){return Promise.resolve('FormData failed: '+e);}"
                  + "var ps=new URLSearchParams();fd.forEach(function(v,k){ps.append(k,v);});"
                  + "var url=act,opt={method:m,redirect:'manual'};"
                  + "if(m==='GET'){url=act+(act.indexOf('?')<0?'?':'&')+ps.toString();}else{opt.body=ps;}"
                  + "var t0=Date.now();"
                  + "return fetch(url,opt).then(function(r){var ms=Date.now()-t0;return r.text().then(function(b){"
                  + "return 'FIRE  '+m+'  '+url.slice(0,140)+'\\n  status='+r.status+' '+r.statusText+'  type='+r.type+'  '+ms+'ms\\n  bytes='+b.length;});})"
                  + ".catch(function(e){return 'FIRE failed (CORS or network): '+e;});})(" + detailFormIndex + ")";
                string p = "{\"expression\":" + JsonString(js) + ",\"returnByValue\":true,\"awaitPromise\":true}";
                string res = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", p);
                string bdy = JStr(res, "value") ?? RenderEval(res);
                Print("");
                foreach (var l in bdy.Split('\n'))
                {
                    string tt = l.TrimStart();
                    if (tt.StartsWith("FIRE")) PrintSection(l); else Print(l);
                }
            }
            catch (Exception ex) { Print("fire error: " + ex.Message); }
        }

        private class Frame { public string Label; public string Why; public List<string> Items; }

        private void PredictSet()
        {
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fl in detailFields)
            {
                string nm = FieldAttr(fl, "name");
                if (!string.IsNullOrEmpty(nm) && nm != "(none)") have.Add(nm);
            }
            string action = "", method = "GET";
            if (formRows != null && sel >= 0 && sel < formRows.Count)
            {
                var ap = formRows[sel].Summary.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (ap.Length > 0) action = ap[ap.Length - 1];
                foreach (var t in ap) if (t == "GET" || t == "POST" || t == "PUT" || t == "DELETE" || t == "PATCH") method = t;
            }
            string a = action.ToLowerInvariant();
            bool isSearch = a.Contains("search") || a.Contains("/find") || have.Contains("q") || have.Contains("query") || have.Contains("keyword");
            bool isApi    = a.Contains("/api") || a.Contains("/v1") || a.Contains("/v2") || a.Contains("/v3") || a.Contains(".json") || a.Contains("graphql") || a.Contains("/rest");
            bool isAuth   = a.Contains("login") || a.Contains("signin") || a.Contains("sign-in") || a.Contains("auth") || a.Contains("session") || a.Contains("token") || have.Contains("password") || have.Contains("passwd");
            string nature = isAuth ? "auth" : isSearch ? "search" : isApi ? "api" : "generic";

            var frames = new List<Frame>();
            Action<string, string, string[]> F = (label, why, ps) =>
            {
                var fresh = new List<string>();
                foreach (var p in ps) if (!have.Contains(p) && !fresh.Contains(p)) fresh.Add(p);
                if (fresh.Count > 0) frames.Add(new Frame { Label = label, Why = why, Items = fresh });
            };

            if (nature == "search")
            {
                F("pagination",   "list endpoints almost always page",      new[] { "page", "offset", "limit", "per_page", "start", "count" });
                F("format",       "content-type / shape switches",          new[] { "format", "output", "_format", "callback" });
                F("locale",       "i18n knobs",                             new[] { "lang", "locale", "hl", "lr", "region" });
                F("ordering",     "result ordering",                        new[] { "sort", "order", "dir", "desc" });
                F("diagnostics",  "dev switches sometimes left live",       new[] { "debug", "verbose", "test" });
            }
            else if (nature == "api")
            {
                F("field-select", "sparse / expanded responses",            new[] { "fields", "select", "include", "expand", "only" });
                F("format",       "content-type / shape switches",          new[] { "format", "output", "type", "pretty", "callback" });
                F("pagination",   "collections page",                       new[] { "page", "offset", "limit", "per_page", "cursor" });
                F("identity",     "auth params, often telling",             new[] { "api_key", "token", "key", "access_token" });
                F("version",      "versioning & cache-busting",             new[] { "v", "version", "_", "nocache" });
            }
            else if (nature == "auth")
            {
                F("flow",         "redirect / continuation auth carries",   new[] { "redirect", "next", "return", "continue", "callback" });
                F("diagnostics",  "dev switches sometimes left live",       new[] { "debug", "test", "dry_run" });
                F("format",       "content-type switches",                  new[] { "format", "output" });
            }
            else
            {
                F("pagination",   "list-style endpoints page",              new[] { "page", "offset", "limit", "start", "count" });
                F("format",       "content-type / shape switches",          new[] { "format", "output", "type", "callback" });
                F("diagnostics",  "dev switches sometimes left live",       new[] { "debug", "verbose", "test" });
                F("locale",       "i18n knobs",                             new[] { "lang", "locale", "hl" });
                F("ordering",     "result ordering",                        new[] { "sort", "order", "dir" });
            }

            predicted = new List<string>();
            foreach (var fr in frames) { foreach (var it in fr.Items) if (!predicted.Contains(it)) predicted.Add(it); if (predicted.Count >= 12) break; }

            lastFrameLabels = new List<string>();
            foreach (var fr in frames) lastFrameLabels.Add(fr.Label);
            lastSeedScope = method + " " + action;

            RenderPrediction("whole form #" + detailFormIndex,
                             "reads as  " + nature + "  \u00b7  " + method + "  \u00b7  " + detailFields.Count + " fields",
                             frames, "+name=1");
        }

        private void PredictField(int j)
        {
            if (detailFields == null || j < 0 || j >= detailFields.Count) return;
            string line = detailFields[j];
            string name = FieldAttr(line, "name"), type = FieldAttr(line, "type"), val = FieldValue(line);
            string nl = (name + " " + type).ToLowerInvariant();
            bool hasVal = !string.IsNullOrEmpty(val) && val != "(empty)";
            double tmp;
            bool numeric = (type == "number") || (hasVal && double.TryParse(val, out tmp))
                         || nl.Contains("page") || nl.Contains("limit") || nl.Contains("count")
                         || nl.Contains("id") || nl.Contains("qty") || nl.Contains("amount");
            bool boolish = val == "true" || val == "false" || val == "1" || val == "0" || val == "yes" || val == "no" || type == "checkbox";
            bool langish = nl.Contains("lang") || nl.Contains("locale") || val == "en" || val == "english";
            bool emailish = (type == "email") || nl.Contains("email") || nl.Contains("mail");

            var frames = new List<Frame>();
            Action<string, string, string[]> F = (label, why, xs) =>
            {
                var items = new List<string>();
                foreach (var x in xs) items.Add(x.Length == 0 ? "(empty)" : x);
                frames.Add(new Frame { Label = label, Why = why, Items = items });
            };

            if (numeric)
            {
                F("boundaries", "off-by-one and overflow edges", new[] { "0", "1", "-1", "2", "999999" });
                F("absence",    "what nothing does",             new[] { "" });
            }
            else if (boolish)
            {
                F("the flip", "toggle the state", new[] { "true", "false", "1", "0" });
            }
            else if (langish)
            {
                if (hasVal) F("the known", "echo what is there", new[] { val });
                F("neighbours", "adjacent options in the space", new[] { "en", "de", "fr", "xx" });
            }
            else if (emailish)
            {
                F("valid edges", "format acceptance",            new[] { "a@b.co", "x+probe@aloha.io" });
                F("malformed",   "where validation draws a line", new[] { "a@b", "plainstring" });
            }
            else
            {
                F("absence & echo", "empty, then a marker to see if it reflects back",
                  new[] { "", "aloha-" + Math.Abs(Environment.TickCount % 100000) });
                F("length", "how it handles a long value", new[] { new string('x', 64) });
            }

            predicted = new List<string>();
            foreach (var fr in frames)
                foreach (var it in fr.Items)
                {
                    string raw = (it == "(empty)") ? "" : it;
                    if (!predicted.Contains(raw)) predicted.Add(raw);
                }

            lastFrameLabels = new List<string>();
            foreach (var fr in frames) lastFrameLabels.Add(fr.Label);
            lastSeedScope = "field:" + (string.IsNullOrEmpty(name) ? "(none)" : name) + " #" + detailFormIndex;

            RenderPrediction("field [" + j + "]  " + (string.IsNullOrEmpty(name) ? "(none)" : name),
                             "type  " + (string.IsNullOrEmpty(type) ? "?" : type) + (hasVal ? "  \u00b7  now = " + val : ""),
                             frames, "value");
        }

        private void RenderPrediction(string scope, string nature, List<Frame> frames, string probeHint)
        {
            Print("");
            PrintSection("\u25C7 PREDICT \u2014 " + scope);
            Print("  " + nature);
            Print("");
            if (frames.Count == 0)
            {
                Print("  nothing new to place \u2014 the visible structure may already be the whole contract.");
                return;
            }
            foreach (var fr in frames)
            {
                PrintSection("  " + fr.Label);
                Print("    " + fr.Why);
                Print("    " + string.Join("   ", fr.Items));
            }
            Print("");
            Print("  frames placed from shape \u2014 hypotheses, not facts; none is confirmed.");
            Print("  probe with c \u2014 it sends each (" + probeHint + ") and reads what actually lands.");
        }

        private void WriteTransition(List<string> hits, List<string> hitParams, List<string> hitVals, int fieldIdx)
        {
            int hc = (hits == null) ? 0 : hits.Count;
            string headline = (fieldIdx >= 0)
                ? (hc + " value" + (hc == 1 ? "" : "s") + " moved this field")
                : (hc + " input" + (hc == 1 ? "" : "s") + " found that the form never showed");
            EmitTransition(lastSeedScope, lastFrameLabels, hits, headline);
            if (hc > 0 && hitParams != null && hitParams.Count > 0)
            {
                descendParams    = hitParams;
                descendVals      = hitVals;
                descendScope     = lastSeedScope;
                descendBaseScope = lastSeedScope;
                descendFieldIdx  = fieldIdx;
                descendPins      = new List<string>();
                descendTopo      = new List<string>();
                descendDepth     = 1;
                Print("");
                Print("  ring 1   " + lastSeedScope);
                Print("  found inputs \u2014 selectable:");
                for (int i = 0; i < hits.Count; i++) Print("    [" + i + "] " + hits[i]);
                Print("    send <n> value   send your own value into one  (e.g.  send 0 any&symbol!string1)");
                Print("    descend <n>      pin the found value, then probe one ring deeper");
            }
        }

        private void EmitTransition(string scope, List<string> frameLabels, List<string> hits, string headline)
        {
            if (hits == null || hits.Count == 0 || frameLabels == null || frameLabels.Count == 0) return;
            int n = Math.Min(3, hits.Count);
            string hitLine = string.Join("   ", hits.GetRange(0, n));
            string path = TransitionWriter.Emit(frameLabels, scope ?? "scope", hitLine, TVERSION);
            Print("");
            if (path != null)
            {
                PrintSection("\u25C7 transition \u2014 " + headline);
                Print("  " + hitLine);
                Print("  " + path);
            }
            else Print("  transition write failed");
        }

        private async void Descend(int? ptr)
        {
            int idx = ptr ?? 0;
            if (descendParams == null || descendParams.Count == 0 || idx < 0 || idx >= descendParams.Count)
            { Print("  no input <" + idx + "> to descend into \u2014 probe (c) first"); return; }
            if (descendFieldIdx >= 0)
            { Print("  descend works on whole-form inputs \u2014 here use  send <n> value  to drive the field"); return; }
            string val = (descendVals != null && idx < descendVals.Count) ? descendVals[idx] : "1";
            await DeepProbe(descendParams[idx] + "=" + val, TopoRecord(idx, val));
        }

        private string TopoRecord(int chosen, string chosenVal)
        {
            var parts = new List<string>();
            for (int i = 0; i < descendParams.Count; i++)
            {
                string v = (i == chosen) ? chosenVal
                         : ((descendVals != null && i < descendVals.Count) ? descendVals[i] : "1");
                string pv = descendParams[i] + "=" + v;
                parts.Add(i == chosen ? "(" + pv + ")" : pv);
            }
            return string.Join("\u00B7", parts);
        }

        private async Task DeepProbe(string newPin, string topoRecord)
        {
            if (descendPins == null) descendPins = new List<string>();
            if (descendTopo == null) descendTopo = new List<string>();
            descendPins.Add(newPin);
            descendTopo.Add(topoRecord);
            descendDepth = descendPins.Count + 1;

            string scope = (descendBaseScope ?? lastSeedScope ?? "scope") + " : " + string.Join(" : ", descendTopo);

            var pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in descendPins) { int e = p.IndexOf('='); pinned.Add(e > 0 ? p.Substring(0, e) : p); }
            var cands = new List<string>();
            if (predicted != null)
                foreach (var c in predicted) { if (!pinned.Contains(c) && !cands.Contains(c)) cands.Add(c); if (cands.Count >= 6) break; }

            Print("");
            PrintSection("\u25C7 DEEP \u2014 ring " + descendDepth + "  ::  " + string.Join("  ::  ", descendPins));

            if (cands.Count == 0)
            {
                Print("  no candidates left to try at this depth.");
                OfferLayer(scope, new List<string>(), new List<string>(), new List<string>(), new List<string>(descendPins));
                return;
            }

            string pinsArr = JsArray(descendPins);
            string candsArr = JsArray(cands);
            try
            {
                string js =
                    "(function(n,pins,cands,vals){var f=document.forms[n];if(!f)return Promise.resolve('form gone');"
                  + "var m=(f.method||'get').toUpperCase();var act=f.action||location.href;"
                  + "function base(extra){var fd=new FormData(f);var ps=new URLSearchParams();fd.forEach(function(v,k){ps.append(k,v);});"
                  + "pins.forEach(function(p){var i=p.indexOf('=');if(i>0)ps.append(p.slice(0,i),p.slice(i+1));else ps.append(p,'1');});"
                  + "if(extra){var j=extra.indexOf('=');ps.append(extra.slice(0,j),extra.slice(j+1));}"
                  + "var url=act,opt={method:m,redirect:'manual'};if(m==='GET'){url=act+(act.indexOf('?')<0?'?':'&')+ps.toString();}else{opt.body=ps;}"
                  + "return fetch(url,opt).then(function(r){return r.text().then(function(b){return {s:r.status,len:b.length};});}).catch(function(e){return {s:-1,len:0};});}"
                  + "return base(null).then(function(b0){var L=['DEEP  '+m+'  '+act,'  baseline (pins held)  status='+b0.s+'  bytes='+b0.len];"
                  + "var ci=0,vi=0;function step(){if(ci>=cands.length)return L.join('\\n');var C=cands[ci];var V=vals[vi];"
                  + "return base(C+'='+V).then(function(r){var d=r.len-b0.len;var moved=(r.s!==b0.s)||(d!==0);"
                  + "if(moved){var tag=(r.s!==b0.s)?'status changed':('bytes '+(d>0?'+':'')+d);L.push('  +'+C+'='+V+'  -> status='+r.s+'  bytes='+r.len+'  ['+tag+']');ci++;vi=0;}"
                  + "else{vi++;if(vi>=vals.length){vi=0;ci++;}}return step();});}return step();});})("
                  + detailFormIndex + "," + pinsArr + "," + candsArr + ",[\"1\",\"999999\"])";
                string pp = "{\"expression\":" + JsonString(js) + ",\"returnByValue\":true,\"awaitPromise\":true}";
                string res = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", pp);
                string bdy = JStr(res, "value") ?? RenderEval(res);
                foreach (var l in bdy.Split('\n')) { string tt = l.TrimStart(); if (tt.StartsWith("DEEP")) PrintSection(l); else Print(l); }

                var fParams = new List<string>(); var fVals = new List<string>(); var labels = new List<string>();
                foreach (var l in bdy.Split('\n'))
                {
                    var mm = Regex.Match(l, @"\+(\S+?)=(\S+?)\s*->\s*status=(\S+)\s+bytes=\S+\s*\[(.*?)\]");
                    if (mm.Success)
                    {
                        fParams.Add(mm.Groups[1].Value);
                        fVals.Add(mm.Groups[2].Value);
                        labels.Add(mm.Groups[1].Value + "=" + mm.Groups[2].Value + " \u2192 " + mm.Groups[4].Value.Trim());
                    }
                }
                var frames = new List<string>(descendPins); frames.AddRange(fParams);
                OfferLayer(scope, fParams, fVals, labels, frames);
            }
            catch (Exception ex) { Print("deep error: " + ex.Message); }
        }

        private void OfferLayer(string scope, List<string> findParams, List<string> findVals,
                                List<string> labels, List<string> frameLabels)
        {
            descendParams   = findParams;
            descendVals     = findVals;
            descendScope    = scope;
            descendFieldIdx = -1;
            string cond = (descendPins != null && descendPins.Count > 0)
                ? " conditional on " + string.Join(" ", descendPins) : "";
            EmitTransition(scope, frameLabels, labels,
                "ring " + descendDepth + " \u2014 " + labels.Count + " input" + (labels.Count == 1 ? "" : "s") + cond);
            if (labels.Count == 0) { Print("  \u2014 nothing deeper surfaces here (leaf)"); return; }
            Print("");
            Print("  ring " + descendDepth + "   " + scope);
            Print("  found inputs \u2014 selectable:");
            for (int i = 0; i < labels.Count; i++) Print("    [" + i + "] " + labels[i]);
            Print("    send <n> value   pin your own value, then probe one ring deeper");
            Print("    descend <n>      pin the found value, then probe one ring deeper");
        }

        private static void DescendPlan(string p, out List<string> values, out List<string> frames)
        {
            string s = (p ?? "").ToLowerInvariant();
            if (s == "format" || s == "output" || s == "type" || s == "callback" || s == "_format")
            { values = new List<string> { "json", "xml", "html", "csv" }; frames = new List<string> { "format shapes" }; }
            else if (s == "lang" || s == "locale" || s == "hl" || s == "lr" || s == "region")
            { values = new List<string> { "en", "de", "fr", "xx" }; frames = new List<string> { "locale neighbours" }; }
            else if (s == "debug" || s == "verbose" || s == "test" || s == "pretty" || s == "nocache" || s == "dry_run")
            { values = new List<string> { "1", "0", "true", "false" }; frames = new List<string> { "the flip" }; }
            else
            { values = new List<string> { "0", "1", "-1", "2", "999999", "" }; frames = new List<string> { "boundaries", "absence" }; }
        }

        private async Task ProbeDescendAsync(string param, List<string> values, List<string> frames)
        {
            string arr = JsArray(values);
            try
            {
                string js =
                    "(function(n,P,vs){var f=document.forms[n];if(!f)return Promise.resolve('form gone');"
                  + "var m=(f.method||'get').toUpperCase();var act=f.action||location.href;"
                  + "function send(val){var fd=new FormData(f);var ps=new URLSearchParams();fd.forEach(function(v,k){ps.append(k,v);});if(val!==null)ps.append(P,val);"
                  + "var url=act,opt={method:m,redirect:'manual'};if(m==='GET'){url=act+(act.indexOf('?')<0?'?':'&')+ps.toString();}else{opt.body=ps;}"
                  + "return fetch(url,opt).then(function(r){return r.text().then(function(b){return {s:r.status,len:b.length};});}).catch(function(e){return {s:-1,len:0};});}"
                  + "return send(null).then(function(base){var L=['DESCEND ('+P+' @ form #'+n+')  '+m+'  '+act,'  baseline  status='+base.s+'  bytes='+base.len];"
                  + "var i=0;function nx(){if(i>=vs.length)return L.join('\\n');var v=vs[i++];return send(v).then(function(r){var d=r.len-base.len;"
                  + "var tag=(r.s!==base.s)?'status changed':(d!==0?('bytes '+(d>0?'+':'')+d):'no change');L.push('  '+P+'='+(v===''?'(empty)':v)+'  -> status='+r.s+'  bytes='+r.len+'  ['+tag+']');return nx();});}return nx();});})("
                  + detailFormIndex + "," + JsonString(param) + "," + arr + ")";
                string pp = "{\"expression\":" + JsonString(js) + ",\"returnByValue\":true,\"awaitPromise\":true}";
                string res = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", pp);
                string bdy = JStr(res, "value") ?? RenderEval(res);
                Print("");
                foreach (var l in bdy.Split('\n'))
                {
                    string tt = l.TrimStart();
                    if (tt.StartsWith("DESCEND")) PrintSection(l); else Print(l);
                }
                var deeper = new List<string>();
                foreach (var l in bdy.Split('\n'))
                {
                    var mm = Regex.Match(l, @"=\s*(.*?)\s*->\s*status=(\S+)\s+bytes=\S+\s*\[(.*?)\]");
                    if (mm.Success && mm.Groups[3].Value.Trim() != "no change")
                        deeper.Add(param + "=" + mm.Groups[1].Value.Trim() + " \u2192 " + mm.Groups[3].Value.Trim());
                }
                EmitTransition(descendScope + " : " + param, frames, deeper,
                    "descend " + param + " \u2014 " + deeper.Count + " value" + (deeper.Count == 1 ? "" : "s") + " moved the response");
            }
            catch (Exception ex) { Print("descend error: " + ex.Message); }
        }

        private async void FuzzSend(int? ptr, string value)
        {
            if (value == null) value = "";
            int idx = ptr ?? 0;
            string param; int fidx;
            bool haveFound = descendParams != null && descendParams.Count > 0;
            if (haveFound && idx >= 0 && idx < descendParams.Count)
            {
                param = descendParams[idx];
                fidx  = descendFieldIdx;
            }
            else
            {

                if (detailFields == null || idx < 0 || idx >= detailFields.Count)
                { Print("  no field <" + idx + "> on this form, and no probe hits to send into \u2014 run  probe  first, or  fill <n> value  to fill & fire"); return; }
                fidx  = idx;
                param = FieldAttr(detailFields[idx], "name");
                if (string.IsNullOrEmpty(param)) param = "field[" + idx + "]";
                Print("  no probe hits here \u2014 sending straight into field [" + idx + "]  (" + param + ")");
            }
            string pArg = (fidx >= 0) ? "null" : JsonString(param);
            try
            {
                string js =
                    "(function(n,fidx,P,val){var f=document.forms[n];if(!f)return Promise.resolve('form gone');"
                  + "var m=(f.method||'get').toUpperCase();var act=f.action||location.href;var el=null,orig=null;"
                  + "if(fidx>=0){el=f.elements[fidx];if(!el)return Promise.resolve('field gone');orig=el.value;}"
                  + "function send(setVal){if(fidx>=0){el.value=(setVal===null?orig:setVal);}var fd=new FormData(f);var ps=new URLSearchParams();fd.forEach(function(v,k){ps.append(k,v);});if(fidx<0&&setVal!==null){ps.append(P,setVal);}"
                  + "var url=act,opt={method:m,redirect:'manual'};if(m==='GET'){url=act+(act.indexOf('?')<0?'?':'&')+ps.toString();}else{opt.body=ps;}"
                  + "return fetch(url,opt).then(function(r){return r.text().then(function(b){return {s:r.status,len:b.length};});}).catch(function(e){return {s:-1,len:0};});}"
                  + "return send(null).then(function(base){return send(val).then(function(r){if(fidx>=0)el.value=orig;var d=r.len-base.len;"
                  + "var tag=(r.s!==base.s)?'status changed':(d!==0?('bytes '+(d>0?'+':'')+d):'no change');var tgt=(fidx>=0?('field['+fidx+']'):P);"
                  + "return ['SEND  '+tgt+' = '+val,'  baseline  status='+base.s+'  bytes='+base.len,'  sent      -> status='+r.s+'  bytes='+r.len+'  ['+tag+']'].join('\\n');});});})("
                  + detailFormIndex + "," + fidx + "," + pArg + "," + JsonString(value) + ")";
                string pp = "{\"expression\":" + JsonString(js) + ",\"returnByValue\":true,\"awaitPromise\":true}";
                string res = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", pp);
                string bdy = JStr(res, "value") ?? RenderEval(res);
                Print("");
                foreach (var l in bdy.Split('\n'))
                {
                    string tt = l.TrimStart();
                    if (tt.StartsWith("SEND")) PrintSection(l); else Print(l);
                }
                var mt = Regex.Match(bdy, @"\[([^\]]*)\]");
                string tag = mt.Success ? mt.Groups[1].Value.Trim() : "";
                if (tag != "" && tag != "no change")
                {
                    if (fidx >= 0)
                    {
                        string tgt = "field[" + fidx + "]";
                        var moved = new List<string> { tgt + "=" + value + " \u2192 " + tag };
                        EmitTransition(descendScope + " : " + tgt + "=" + value,
                            new List<string> { "custom send", tgt }, moved,
                            "send " + tgt + " \u2014 your value moved the response (" + tag + ")");
                    }
                    else
                    {
                        Print("  it moved the response \u2014 pinning " + param + "=" + value + ", probing one ring deeper");
                        await DeepProbe(param + "=" + value, TopoRecord(idx, value));
                    }
                }
            }
            catch (Exception ex) { Print("send error: " + ex.Message); }
        }

        private async Task ProbeSet()
        {
            var cands = new List<string>();
            if (predicted != null) for (int i = 0; i < predicted.Count && cands.Count < 6; i++) cands.Add(predicted[i]);
            if (cands.Count == 0) { cands.Add("debug"); cands.Add("format"); cands.Add("limit"); cands.Add("page"); }
            string arr = JsArray(cands);
            try
            {
                string js =
                    "(function(n,cs){var f=document.forms[n];if(!f)return Promise.resolve('form gone');"
                  + "var m=(f.method||'get').toUpperCase();var act=f.action||location.href;"
                  + "function send(extra){var fd=new FormData(f);var ps=new URLSearchParams();fd.forEach(function(v,k){ps.append(k,v);});if(extra)ps.append(extra,'1');"
                  + "var url=act,opt={method:m,redirect:'manual'};if(m==='GET'){url=act+(act.indexOf('?')<0?'?':'&')+ps.toString();}else{opt.body=ps;}"
                  + "return fetch(url,opt).then(function(r){return r.text().then(function(b){return {s:r.status,len:b.length};});}).catch(function(e){return {s:-1,len:0};});}"
                  + "return send('').then(function(base){var L=['PROBE (whole form #'+n+')  '+m+'  '+act,'  baseline  status='+base.s+'  bytes='+base.len];"
                  + "var i=0;function nx(){if(i>=cs.length)return L.join('\\n');var c=cs[i++];return send(c).then(function(r){var d=r.len-base.len;"
                  + "var tag=(r.s!==base.s)?'status changed':(d!==0?('bytes '+(d>0?'+':'')+d):'no change');L.push('  +'+c+'=1  -> status='+r.s+'  bytes='+r.len+'  ['+tag+']');return nx();});}return nx();});})("
                  + detailFormIndex + "," + arr + ")";
                string p = "{\"expression\":" + JsonString(js) + ",\"returnByValue\":true,\"awaitPromise\":true}";
                string res = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", p);
                string bdy = JStr(res, "value") ?? RenderEval(res);
                Print("");
                foreach (var l in bdy.Split('\n'))
                {
                    string tt = l.TrimStart();
                    if (tt.StartsWith("PROBE")) PrintSection(l); else Print(l);
                }
                var hits = new List<string>();
                var hitParams = new List<string>();
                var hitVals = new List<string>();
                foreach (var l in bdy.Split('\n'))
                {
                    var mm = Regex.Match(l, @"\+(\S+?)=1\s*->\s*status=(\S+)\s+bytes=\S+\s*\[(.*?)\]");
                    if (mm.Success && mm.Groups[3].Value.Trim() != "no change")
                    {
                        hits.Add(mm.Groups[1].Value + "=1 \u2192 " + mm.Groups[3].Value.Trim());
                        hitParams.Add(mm.Groups[1].Value);
                        hitVals.Add("1");
                    }
                }
                WriteTransition(hits, hitParams, hitVals, -1);
            }
            catch (Exception ex) { Print("probe error: " + ex.Message); }
        }

        private async Task ProbeField(int j)
        {
            var vals = new List<string>();
            if (predicted != null) for (int i = 0; i < predicted.Count && vals.Count < 5; i++) vals.Add(predicted[i]);
            if (vals.Count == 0) { vals.Add(""); vals.Add("test"); vals.Add("aloha-probe"); }
            string arr = JsArray(vals);
            try
            {
                string js =
                    "(function(n,j,vs){var f=document.forms[n];if(!f)return Promise.resolve('form gone');var el=f.elements[j];if(!el)return Promise.resolve('field gone');"
                  + "var orig=el.value;var m=(f.method||'get').toUpperCase();var act=f.action||location.href;"
                  + "function sendWith(val){el.value=val;var fd=new FormData(f);var ps=new URLSearchParams();fd.forEach(function(v,k){ps.append(k,v);});"
                  + "var url=act,opt={method:m,redirect:'manual'};if(m==='GET'){url=act+(act.indexOf('?')<0?'?':'&')+ps.toString();}else{opt.body=ps;}"
                  + "return fetch(url,opt).then(function(r){return r.text().then(function(b){return {s:r.status,len:b.length};});}).catch(function(e){return {s:-1,len:0};});}"
                  + "return sendWith(orig).then(function(base){var L=['PROBE field ['+j+']  name='+(el.name||'(none)'),'  baseline (='+(orig||'(empty)')+')  status='+base.s+'  bytes='+base.len];"
                  + "var i=0;function nx(){if(i>=vs.length){el.value=orig;return L.join('\\n');}var v=vs[i++];return sendWith(v).then(function(r){var d=r.len-base.len;"
                  + "var tag=(r.s!==base.s)?'status changed':(d!==0?('bytes '+(d>0?'+':'')+d):'same');L.push('  ='+(v===''?'(empty)':v)+'  -> status='+r.s+'  bytes='+r.len+'  ['+tag+']');return nx();});}return nx();});})("
                  + detailFormIndex + "," + j + "," + arr + ")";
                string p = "{\"expression\":" + JsonString(js) + ",\"returnByValue\":true,\"awaitPromise\":true}";
                string res = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", p);
                string bdy = JStr(res, "value") ?? RenderEval(res);
                Print("");
                foreach (var l in bdy.Split('\n'))
                {
                    string tt = l.TrimStart();
                    if (tt.StartsWith("PROBE")) PrintSection(l); else Print(l);
                }
                string fname = (j >= 0 && j < detailFields.Count) ? FieldAttr(detailFields[j], "name") : "";
                if (string.IsNullOrEmpty(fname)) fname = "field[" + j + "]";
                var hits = new List<string>();
                var fieldParams = new List<string>();
                var fieldVals = new List<string>();
                foreach (var l in bdy.Split('\n'))
                {
                    var mm = Regex.Match(l, @"=\s*(.*?)\s*->\s*status=(\S+)\s+bytes=\S+\s*\[(.*?)\]");
                    if (mm.Success && mm.Groups[3].Value.Trim() != "same")
                    {
                        hits.Add(fname + "=" + mm.Groups[1].Value.Trim() + " \u2192 " + mm.Groups[3].Value.Trim());
                        fieldParams.Add(fname);
                        fieldVals.Add(mm.Groups[1].Value.Trim());
                    }
                }
                WriteTransition(hits, fieldParams, fieldVals, j);
            }
            catch (Exception ex) { Print("probe error: " + ex.Message); }
        }

        private static string FieldAttr(string line, string key)
        {
            var m = Regex.Match(line, key + "=(\\S+)");
            return m.Success ? m.Groups[1].Value : "";
        }

        private static string JsArray(List<string> xs)
        {
            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < xs.Count; i++) { if (i > 0) sb.Append(','); sb.Append(JsonString(xs[i])); }
            return sb.Append(']').ToString();
        }

        private static char WordToOp(string w)
        {
            switch (w)
            {
                case "inspect": case "a": return 'a';
                case "fill":    case "b": return 'b';
                case "probe":   case "c": return 'c';
                case "monitor": case "d": return 'd';
                case "predict": case "e": return 'e';
                case "descend": case "t": return 't';
                case "send":    case "f": return 'f';
            }
            return '\0';
        }

        private static string OpName(char o)
        {
            switch (o)
            {
                case 'a': return "inspect";
                case 'b': return "fill";
                case 'c': return "probe";
                case 'd': return "monitor";
                case 'e': return "predict";
                case 't': return "descend";
                case 'f': return "send";
            }
            return "?";
        }

        private async Task InspectField(int j)
        {
            try
            {
                string js =
                    "(function(n,j){var f=document.forms[n];if(!f)return 'form gone';var el=f.elements[j];if(!el)return 'field gone';"
                  + "var L=[];L.push('FIELD ['+j+']  '+(el.tagName||'').toLowerCase()+'  name='+(el.name||el.id||'(none)'));"
                  + "L.push('  type='+(el.type||'')+'  value='+(el.value||'(empty)'));"
                  + "var a=el.attributes;L.push('  attributes: '+a.length);"
                  + "for(var k=0;k<a.length;k++)L.push('    '+a[k].name+'='+a[k].value);"
                  + "var r=el.getBoundingClientRect();L.push('  rect: '+Math.round(r.x)+','+Math.round(r.y)+'  '+Math.round(r.width)+'x'+Math.round(r.height));"
                  + "var h=el.outerHTML||'';if(h.length>300)h=h.slice(0,300)+'...';L.push('  html: '+h);"
                  + "return L.join('\\n');})(" + detailFormIndex + "," + j + ")";
                string p = "{\"expression\":" + JsonString(js) + ",\"returnByValue\":true}";
                string res = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", p);
                string bdy = JStr(res, "value") ?? RenderEval(res);
                Print("");
                foreach (var l in bdy.Split('\n'))
                {
                    string tt = l.TrimStart();
                    if (tt.StartsWith("FIELD")) PrintSection(l); else Print(l);
                }
            }
            catch (Exception ex) { Print("inspect error: " + ex.Message); }
        }

        private async Task InspectSet()
        {
            try
            {
                string js =
                    "(function(n){var f=document.forms[n];if(!f)return 'form gone';var p=[];"
                  + "for(var k=0;k<f.elements.length;k++){var el=f.elements[k];if(!el.name)continue;"
                  + "if((el.type==='checkbox'||el.type==='radio')&&!el.checked)continue;"
                  + "p.push(encodeURIComponent(el.name)+'='+encodeURIComponent(el.value||''));}"
                  + "var L=[];L.push('PAYLOAD (whole form #'+n+', no divide)  '+((f.method||'get').toUpperCase())+'  '+(f.action||location.href));"
                  + "L.push('  pairs: '+p.length);L.push('  '+p.join('&'));return L.join('\\n');})(" + detailFormIndex + ")";
                string p = "{\"expression\":" + JsonString(js) + ",\"returnByValue\":true}";
                string res = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", p);
                string bdy = JStr(res, "value") ?? RenderEval(res);
                Print("");
                foreach (var l in bdy.Split('\n'))
                {
                    string tt = l.TrimStart();
                    if (tt.StartsWith("PAYLOAD")) PrintSection(l); else Print(l);
                }
            }
            catch (Exception ex) { Print("inspect error: " + ex.Message); }
        }

        private static int ParseIndex(string s)
        {
            int h = s.IndexOf('#'); if (h < 0) return 0;
            int j = h + 1, v = 0;
            while (j < s.Length && char.IsDigit(s[j])) { v = v * 10 + (s[j] - '0'); j++; }
            return v;
        }

        private async void Submit()
        {
            string raw = input.Text;
            if (string.IsNullOrEmpty(raw)) return;
            input.Clear();
            history.Add(raw); historyIdx = history.Count;
            Echo(raw);

            if (nav == Nav.Forms)
            {
                string cc = raw.Trim();

                int wi = 0;
                while (wi < cc.Length && char.IsLetter(cc[wi])) wi++;
                string word = cc.Substring(0, wi).ToLowerInvariant();
                char op = WordToOp(word);
                if (op >= 'a' && op <= 'e')
                {
                    string rest = (wi < cc.Length) ? cc.Substring(wi).TrimStart() : "";
                    int? ptr = null;
                    if (rest.StartsWith("<"))                 
                    {
                        int gt = rest.IndexOf('>');
                        if (gt > 1)
                        {
                            int v;
                            if (int.TryParse(rest.Substring(1, gt - 1).Trim(), out v)) ptr = v;
                            rest = rest.Substring(gt + 1);
                        }
                    }
                    string value = rest.TrimStart();
                    RunOp(op, ptr, value);
                    return;
                }
                if (op == 't')
                {
                    string rest = (wi < cc.Length) ? cc.Substring(wi).TrimStart() : "";
                    int? ptr = null;
                    if (rest.StartsWith("<"))
                    {
                        int gt = rest.IndexOf('>');
                        if (gt > 1) { int v; if (int.TryParse(rest.Substring(1, gt - 1).Trim(), out v)) ptr = v; }
                    }
                    Descend(ptr);
                    return;
                }
                if (op == 'f')
                {
                    string rest = (wi < cc.Length) ? cc.Substring(wi).TrimStart() : "";
                    int? ptr = null;
                    if (rest.StartsWith("<"))
                    {
                        int gt = rest.IndexOf('>');
                        if (gt > 1) { int v; if (int.TryParse(rest.Substring(1, gt - 1).Trim(), out v)) ptr = v; rest = rest.Substring(gt + 1); }
                    }
                    else
                    {
                        int sp = rest.IndexOf(' ');
                        string tok = sp < 0 ? rest : rest.Substring(0, sp);
                        int v;
                        if (tok.Length > 0 && int.TryParse(tok, out v)) { ptr = v; rest = sp < 0 ? "" : rest.Substring(sp + 1); }
                    }
                    FuzzSend(ptr, rest.TrimStart());
                    return;
                }
            }
            nav = Nav.None;

            switch (raw.Trim().ToLowerInvariant())
            {
                case "forms": await EnumerateFormsAsync(); return;
                case "help":  Help();  return;
                case "clear": output.Clear(); Banner(); return;
            }

            try
            {
                string p = "{\"expression\":" + JsonString(raw)
                         + ",\"returnByValue\":true,\"includeCommandLineAPI\":true,"
                         + "\"userGesture\":true,\"awaitPromise\":true,\"replMode\":true}";
                string res = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", p);
                Print(RenderEval(res));
            }
            catch (Exception ex) { Print("eval error: " + ex.Message); }
        }

        private const string FORMS_JS = @"(function(){
 var L=[];
 function fld(el){
  var t=(el.tagName||'').toLowerCase();
  var nm=el.name||el.id||'(none)';
  var ty=el.type||'';
  var ph=el.placeholder?(' ph='+el.placeholder):'';
  return '    '+t+'  name='+nm+'  type='+ty+ph;
 }
 var fs=document.forms;
 L.push('FORMS: '+fs.length);
 for(var i=0;i<fs.length;i++){
  var f=fs[i];
  L.push('FORM #'+i+'  '+((f.method||'get').toUpperCase())+'  '+(f.action||location.href));
  for(var j=0;j<f.elements.length;j++){var el=f.elements[j]; if(el.name||el.type) L.push(fld(el));}
 }
 var loose=[], all=document.querySelectorAll('input,textarea,select,button');
 for(var k=0;k<all.length;k++){ if(!all[k].form) loose.push(all[k]); }
 if(loose.length){ L.push('LOOSE (no form): '+loose.length); for(var n=0;n<loose.length;n++) L.push(fld(loose[n])); }
 if(fs.length===0 && loose.length===0) L.push('(nothing interactive found)');
 return L.join('\n');
})()";

        private async System.Threading.Tasks.Task EnumerateFormsAsync()
        {
            try
            {
                string p = "{\"expression\":" + JsonString(FORMS_JS) + ",\"returnByValue\":true}";
                string res = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", p);
                string body = JStr(res, "value");
                if (body == null) { Print(RenderEval(res)); return; }

                formRows = new List<FormEntry>();
                FormEntry cur = null; bool inLoose = false;
                foreach (var line in body.Split('\n'))
                {
                    string t = line.TrimStart();
                    if (t.StartsWith("LOOSE")) { inLoose = true; cur = null; continue; }
                    if (t.StartsWith("FORMS:")) continue;
                    if (t.StartsWith("FORM #"))
                    {
                        inLoose = false;
                        cur = new FormEntry { Index = ParseIndex(t), Summary = t };
                        formRows.Add(cur);
                    }
                    else if (!inLoose && cur != null && t.Length > 0) cur.Fields.Add(t);
                }

                if (formRows.Count == 0)
                {
                    nav = Nav.None;
                    Print("(no <form> found \u2014 the page may build forms with JS; run 'forms' again once it loads)");
                    return;
                }
                nav = Nav.Forms; SelectForm(0);
            }
            catch (Exception ex) { Print("forms error: " + ex.Message); }
        }

        private void Help()
        {
            PrintSection("commands");
            Print("  forms   list the page's forms \u2014 then \u2191\u2193 select, Enter dump, Esc back");
            Print("  clear   clear the feed");
            Print("  help    this list");
            Print("  <expr>  evaluate JS in the page");
            Print("  descend <n>     pin a found value and probe one ring deeper (recursive)");
            Print("  send <n> value  pin your own value and probe one ring deeper");
            Print("  (the panel at right lists the runnable ones \u2014 click to run)");
        }

        private void StepHistory(int dir)
        {
            if (history.Count == 0) return;
            historyIdx = Math.Max(0, Math.Min(history.Count, historyIdx + dir));
            input.Text = historyIdx < history.Count ? history[historyIdx] : "";
            input.SelectionStart = input.Text.Length;
        }

        private async Task ToggleMonitor()
        {
            if (monitoring) { await StopMonitor(); return; }
            try
            {
                netReqHandler  = (s, e) => OnNetRequest(e.ParameterObjectAsJson);
                netRespHandler = (s, e) => OnNetResponse(e.ParameterObjectAsJson);
                netReqRx  = core.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
                netRespRx = core.GetDevToolsProtocolEventReceiver("Network.responseReceived");
                netReqRx.DevToolsProtocolEventReceived  += netReqHandler;
                netRespRx.DevToolsProtocolEventReceived += netRespHandler;
                await core.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
                monitoring = true;
                Print("");
                PrintSection("monitor on \u2014 live network, page scope \u00B7 d again to stop");
                Print("  \u2192 request   \u2190 response");
            }
            catch (Exception ex) { Print("monitor error: " + ex.Message); }
        }

        private async Task StopMonitor()
        {
            try
            {
                if (netReqRx  != null && netReqHandler  != null) netReqRx.DevToolsProtocolEventReceived  -= netReqHandler;
                if (netRespRx != null && netRespHandler != null) netRespRx.DevToolsProtocolEventReceived -= netRespHandler;
                await core.CallDevToolsProtocolMethodAsync("Network.disable", "{}");
            }
            catch { }
            monitoring = false;
            netReqRx = null; netRespRx = null; netReqHandler = null; netRespHandler = null;
            PrintSection("monitor off");
        }

        private void OnNetRequest(string json)
        {
            string method = JStr(json, "method") ?? "?";
            string url    = JStr(json, "url") ?? "";
            string type   = JStr(json, "type");
            Print("  \u2192 " + method + " " + Clip(url, 120) + (type != null ? "  [" + type + "]" : ""));
        }

        private void OnNetResponse(string json)
        {
            string status = JNum(json, "status") ?? "?";
            string url    = JStr(json, "url") ?? "";
            string mime   = JStr(json, "mimeType");
            Print("  \u2190 " + status + " " + Clip(url, 120) + (mime != null ? "  " + mime : ""));
        }

        private static string JNum(string json, string key)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string Clip(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            return s.Length <= n ? s : s.Substring(0, n) + "\u2026";
        }

        private void OnConsole(string json)
        {
            string type = JStr(json, "type") ?? "log";
            var parts = new List<string>();
            foreach (Match m in Regex.Matches(json,
                "\"(?:description|value)\"\\s*:\\s*(\"(?:[^\"\\\\]|\\\\.)*\"|true|false|null|-?\\d+(?:\\.\\d+)?)"))
                parts.Add(Tok(m.Groups[1].Value));
            Print("[" + type + "] " + string.Join(" ", parts));
        }

        private void OnException(string json)
        {
            Print("\u26A0 " + (JStr(json, "description") ?? JStr(json, "text") ?? "(uncaught exception)"));
        }

        private string RenderEval(string json)
        {
            if (JHas(json, "exceptionDetails"))
                return "\u26A0 " + (JStr(json, "description") ?? JStr(json, "text") ?? "(exception)");
            string desc = JStr(json, "description");
            if (desc != null) return desc;
            var m = Regex.Match(json,
                "\"value\"\\s*:\\s*(\"(?:[^\"\\\\]|\\\\.)*\"|true|false|null|-?\\d+(?:\\.\\d+)?)");
            if (m.Success) return Tok(m.Groups[1].Value);
            return JStr(json, "type") ?? "(no result)";
        }

        private void PrintHeader(string s)  { AppendRun((s ?? "") + "\r\n", ClrHeader, true); }
        private void PrintSection(string s)  { AppendRun((s ?? "") + "\r\n", ClrBody, true); }
        private void Print(string s)       { AppendRun((s ?? "") + "\r\n", ClrBody, false); }
        private void Echo(string s)        { AppendRun("> ", ClrBody, true); AppendRun((s ?? "") + "\r\n", ClrBody, false); }

        private void AppendRun(string text, Color color, bool bold) { AppendTo(output, text, color, bold, null); }
        private void AppendRun(string text, Color color, bool bold, Color? back) { AppendTo(output, text, color, bold, back); }
        private void AppendTo(RichTextBox box, string text, Color color, bool bold, Color? back)
        {
            if (box == null || box.IsDisposed || string.IsNullOrEmpty(text)) return;
            if (box.InvokeRequired) { box.BeginInvoke((Action)(() => AppendTo(box, text, color, bold, back))); return; }
            int start = box.TextLength;
            box.AppendText(text);
            box.Select(start, text.Length);
            box.SelectionColor = color;
            box.SelectionBackColor = back ?? Color.Black;
            box.SelectionFont = bold ? fBold : fReg;
            box.Select(box.TextLength, 0);
            box.ScrollToCaret();
        }

        private static string JStr(string json, string key)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? Unescape(m.Groups[1].Value) : null;
        }
        private static bool JHas(string json, string key)
        {
            return Regex.IsMatch(json, "\"" + Regex.Escape(key) + "\"\\s*:");
        }
        private static string Tok(string raw)
        {
            if (raw.Length > 1 && raw[0] == '"') return Unescape(raw.Substring(1, raw.Length - 2));
            return raw;
        }
        private static string Unescape(string s)
        {
            return s.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\t", "\t")
                    .Replace("\\r", "").Replace("\\/", "/").Replace("\\\\", "\\");
        }
        private static string JsonString(string s)
        {
            var sb = new System.Text.StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
