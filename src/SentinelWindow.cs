using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace Aloha
{

    public class SentinelWindow : DafyFrame
    {

        private static readonly Color Fab      = Color.FromArgb(0xFA, 0xFA, 0xFB);
        private static readonly Color Ink       = Color.FromArgb(0x20, 0x20, 0x20);
        private static readonly Color InkDim    = Color.FromArgb(0x70, 0x70, 0x70);
        private static readonly Color GrooveHi  = Color.FromArgb(0xFF, 0xFF, 0xFF);
        private static readonly Color GrooveLo  = Color.FromArgb(0x80, 0x80, 0x80);

        private static readonly Color Box       = Color.Black;

        private static readonly Color White     = Color.FromArgb(0xf0, 0xf0, 0xf0);
        private static readonly Color AlohaLime  = Color.FromArgb(0x66, 0xff, 0x66);
        private static readonly Color LineDim    = Color.FromArgb(90, 0xf0, 0xf0, 0xf0);
        private static readonly Color LineLime   = Color.FromArgb(150, 0x66, 0xff, 0x66);
        private static readonly Color Sel        = Color.White;
        private static readonly Color Alarm      = Color.FromArgb(0xff, 0x33, 0x33);

        private class Node
        {
            public string id;
            public string name, origin, kind;
            public int hits, thread;
            public bool active;
            public long lastHit;
            public string sig, sub;
            public List<string> calls;
            public Node parent;
            public int order;
            public float x3, y3, z3;
            public float px, py;
        }
        private struct Edge { public Node a, b; public string type; }

        private readonly Dictionary<string, Node> nodes = new Dictionary<string, Node>();
        private readonly List<Edge> edges = new List<Edge>();
        private string pageOrigin = "site";

        private Node selected = null;
        private bool paused = false;

        private static readonly float[] B = { 0f, 0f, 1f };
        private const float STEP_LEN = 0.16f, Z_WIND = 0.14f, RING_R = 0.34f;
        private bool dirty = true;
        private readonly Random rng = new Random(0x5EED);

        private int nextThread = 1;
        private readonly HashSet<int> liveThreads = new HashSet<int>();
        private readonly HashSet<int> trimmedThreads = new HashSet<int>();
        private bool compromised = false;
        private int compromisedThread = 0;
        private long compromisedAt = 0;

        private Panel canvas;
        private SentinelLogWindow logWin;
        private System.Windows.Forms.Timer tick;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long NowMs => clock.ElapsedMilliseconds;

        private readonly CoreWebView2 core;
        private CoreWebView2DevToolsProtocolEventReceiver rxConsole, rxException, rxNetwork, rxAnimation;
        private EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs> hConsole, hException, hNetwork, hAnimation;

        private Rectangle boxRect;

        public SentinelWindow(CoreWebView2 coreView) : base("SENTINEL", "SENTINEL \u2014 probe")
        {
            core = coreView;
            Size = new Size(620, 620);
            MinimumSize = new Size(460, 460);
            ClientArea.BackColor = Fab;

            canvas = new Panel { Dock = DockStyle.Fill, BackColor = Fab };
            typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(canvas, true, null);
            canvas.Paint += Canvas_Paint;
            canvas.MouseDown += Canvas_MouseDown;
            ClientArea.Controls.Add(canvas);

            KeyPreview = true;
            KeyDown += Sentinel_KeyDown;

            tick = new System.Windows.Forms.Timer { Interval = 40 };

            tick.Tick += (s, e) =>
            {
                long now = NowMs;
                foreach (var nd in nodes.Values)
                    if (nd.active && now - nd.lastHit > 1500) nd.active = false;
                if (dirty) { LayoutGraph(); dirty = false; }
                canvas.Invalidate();
            };
            tick.Start();

            Shown += async (s, e) => { SpawnLog(); await Wire(); };
            FormClosed += (s, e) => { try { tick?.Stop(); tick?.Dispose(); } catch { } try { logWin?.Close(); } catch { } Unwire(); };
        }

        private void SpawnLog()
        {
            if (logWin != null && !logWin.IsDisposed) return;
            logWin = new SentinelLogWindow();
            logWin.StartPosition = FormStartPosition.Manual;
            PinLog();
            logWin.Show(this);
            this.Move += (s, e) => PinLog();
            this.Resize += (s, e) => PinLog();
        }
        private void PinLog()
        {
            if (logWin == null || logWin.IsDisposed) return;
            logWin.Height = this.Height; logWin.Top = this.Top;
            logWin.Left = this.Left - logWin.Width;
        }
        private void ToLog(string thread, string msg, string sig)
        {
            try { if (logWin != null && !logWin.IsDisposed) logWin.Push(thread, msg, sig); } catch { }
        }

        private async System.Threading.Tasks.Task Wire()
        {
            if (core == null) { ToLog("-", "no engine", ""); return; }
            try
            {
                int th = SpawnThread();
                pageOrigin = HostOf(core.Source ?? "site");
                hConsole   = (o, a) => OnConsole(a.ParameterObjectAsJson, th);
                hException = (o, a) => OnException(a.ParameterObjectAsJson, th);
                hNetwork   = (o, a) => OnNetwork(a.ParameterObjectAsJson, th);
                hAnimation = (o, a) => OnAnimation(a.ParameterObjectAsJson, th);

                rxConsole   = core.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled");
                rxException = core.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown");
                rxNetwork   = core.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
                rxAnimation = core.GetDevToolsProtocolEventReceiver("Animation.animationStarted");
                rxConsole.DevToolsProtocolEventReceived   += hConsole;
                rxException.DevToolsProtocolEventReceived += hException;
                rxNetwork.DevToolsProtocolEventReceived   += hNetwork;
                rxAnimation.DevToolsProtocolEventReceived += hAnimation;

                await core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
                await core.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
                try { await core.CallDevToolsProtocolMethodAsync("Animation.enable", "{}"); } catch { }

                await LearnRelations(th);
                ToLog("0x" + th.ToString("X2"), "probe attached \u2014 field empty", "");
            }
            catch (Exception ex) { ToLog("-", "wire error: " + ex.GetType().Name, ""); }
        }

        private readonly Dictionary<string, List<string>> knownCalls =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> knownSig =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> knownSub =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private async System.Threading.Tasks.Task LearnRelations(int th)
        {
            try
            {
                string js =
                    "(function(){var o=[];try{for(var k in window){try{var v=window[k];" +
                    "if(typeof v==='function'){var s='';try{s=Function.prototype.toString.call(v);}catch(e){}" +
                    "o.push({n:k,src:s.slice(0,400)});}}catch(e){}}}catch(e){}" +
                    "return JSON.stringify(o.slice(0,160));})()";
                string p = "{\"expression\":" + JsonStr(js) + ",\"returnByValue\":true}";
                string res = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", p);
                string val = Extract(res, "\"value\":");
                if (val == null) return;
                string arr = UnJsonStr(val);
                foreach (var fn in ParseFnArray(arr))
                {
                    knownSig[fn.Name] = SigOf(fn.Src);
                    knownSub[fn.Name] = SubCallsOf(fn.Src);
                    knownCalls[fn.Name] = CalleeNames(fn.Src);
                }
                ToLog("0x" + th.ToString("X2"), "learned " + knownCalls.Count + " relations", "");
            }
            catch { }
        }

        private void Unwire()
        {
            try
            {
                if (rxConsole   != null && hConsole   != null) rxConsole.DevToolsProtocolEventReceived   -= hConsole;
                if (rxException != null && hException != null) rxException.DevToolsProtocolEventReceived -= hException;
                if (rxNetwork   != null && hNetwork   != null) rxNetwork.DevToolsProtocolEventReceived   -= hNetwork;
                if (rxAnimation != null && hAnimation != null) rxAnimation.DevToolsProtocolEventReceived -= hAnimation;
            }
            catch { }
        }

        private int SpawnThread() { int t = nextThread++; liveThreads.Add(t); return t; }
        public void Trip(int thread, string reason)
        {
            if (!liveThreads.Contains(thread)) return;
            liveThreads.Remove(thread); trimmedThreads.Add(thread);
            compromised = true; compromisedThread = thread; compromisedAt = NowMs;
            ToLog("0x" + thread.ToString("X2"), "!! TRIMMED " + (reason ?? ""), "");
        }

        private int detectOrder = 0;

        private Node Detect(string origin, string name, string kind, int th)
        {
            string id = origin + "|" + name;
            if (!nodes.TryGetValue(id, out var nd))
            {
                nd = new Node
                {
                    id = id, name = name, origin = origin, kind = kind, thread = th,
                    order = detectOrder++,
                    sig = knownSig.TryGetValue(name, out var sg) ? sg : SigOf(name),
                    sub = knownSub.TryGetValue(name, out var sb) ? sb : "",
                    calls = knownCalls.TryGetValue(name, out var cl) ? cl : new List<string>()
                };
                nodes[id] = nd;
                LinkParent(nd);
                RebuildEdges();
                dirty = true;
            }
            nd.hits++;
            nd.active = true;
            nd.lastHit = NowMs;
            ToLog("0x" + th.ToString("X2"), (nd.hits > 1 ? "[" + nd.hits + "] " : "") + name, nd.sig);
            return nd;
        }

        private void LinkParent(Node child)
        {
            foreach (var other in nodes.Values)
            {
                if (other == child || other.calls == null) continue;
                if (other.origin == child.origin && other.calls.Contains(child.name))
                { child.parent = other; return; }
            }
        }

        private void RebuildEdges()
        {
            edges.Clear();
            foreach (var nd in nodes.Values)
            {

                if (nd.calls != null)
                    foreach (var callee in nd.calls)
                    {
                        string cid = nd.origin + "|" + callee;
                        if (nodes.TryGetValue(cid, out var cb))
                            edges.Add(new Edge { a = nd, b = cb, type = "call" });
                    }

                if (nd.kind == "net")
                    foreach (var other in nodes.Values)
                        if (other != nd && other.kind == "net" &&
                            string.Equals(other.origin, nd.origin, StringComparison.OrdinalIgnoreCase))
                        { edges.Add(new Edge { a = nd, b = other, type = "wire" }); break; }
            }
        }

        private void OnConsole(string json, int th)
        { if (paused) return; string t = Extract(json, "\"type\":\"") ?? "log"; Detect(pageOrigin, "console." + t, "func", th); }
        private void OnException(string json, int th)
        { if (paused) return; Detect(pageOrigin, "exception", "exc", th); }
        private void OnNetwork(string json, int th)
        {
            if (paused) return;
            string url = Extract(json, "\"url\":\"") ?? "socket";

            string host = HostOf(url);
            string tail = PathTail(url);
            Detect(host, tail.Length > 0 ? tail : "net", "net", th);
        }
        private void OnAnimation(string json, int th)
        { if (paused) return; Detect(pageOrigin, "anim", "func", th); }

        private void LayoutGraph()
        {
            var list = new List<Node>(nodes.Values);
            list.Sort((p, q) => p.order.CompareTo(q.order));
            int roots = 0;

            foreach (var nd in list)
            {
                if (nd.parent == null || !nodes.ContainsValue(nd.parent))
                {

                    double a = roots * 2.399963;
                    roots++;
                    nd.x3 = (float)Math.Cos(a) * RING_R;
                    nd.y3 = (float)Math.Sin(a) * RING_R;
                    nd.z3 = 0f;
                }
                else
                {
                    var pr = nd.parent;

                    float dx = pr.x3, dy = pr.y3, dz = 0f;
                    float dl = (float)Math.Sqrt(dx * dx + dy * dy) + 1e-4f;
                    dx /= dl; dy /= dl;

                    float cx = dy * B[2], cy = -dx * B[2];

                    nd.x3 = pr.x3 + cx * STEP_LEN;
                    nd.y3 = pr.y3 + cy * STEP_LEN;
                    nd.z3 = pr.z3 + Z_WIND;
                }
            }

            foreach (var nd in list)
            {
                float persp = 1f / (1f + nd.z3 * 1.1f);
                nd.px = 0.5f + nd.x3 * persp;
                nd.py = 0.5f + nd.y3 * persp;
                if (nd.px < 0.02f) nd.px = 0.02f; if (nd.px > 0.98f) nd.px = 0.98f;
                if (nd.py < 0.02f) nd.py = 0.02f; if (nd.py > 0.98f) nd.py = 0.98f;
            }
        }

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int W = canvas.Width, H = canvas.Height;
            g.Clear(Fab);

            int m = 10, statusH = 20;
            boxRect = new Rectangle(m, m, W - 2 * m, H - 2 * m - statusH);
            DrawSunken(g, boxRect);
            using (var bb = new SolidBrush(Box)) g.FillRectangle(bb, boxRect);

            var clip = g.Clip;
            g.SetClip(boxRect);
            DrawEdges(g);
            DrawNodes(g);
            g.Clip = clip;

            DrawStatus(g, m, H - m - statusH + 2, W - 2 * m);
            if (compromised) DrawCompromised(g, boxRect, NowMs);
        }

        private void DrawSunken(Graphics g, Rectangle r)
        {
            using (var lo = new Pen(GrooveLo)) { g.DrawLine(lo, r.Left - 1, r.Top - 1, r.Right, r.Top - 1); g.DrawLine(lo, r.Left - 1, r.Top - 1, r.Left - 1, r.Bottom); }
            using (var hi = new Pen(GrooveHi)) { g.DrawLine(hi, r.Left - 1, r.Bottom, r.Right, r.Bottom); g.DrawLine(hi, r.Right, r.Top - 1, r.Right, r.Bottom); }
        }

        private PointF P(Node nd) => new PointF(boxRect.X + nd.px * boxRect.Width, boxRect.Y + nd.py * boxRect.Height);

        private void DrawEdges(Graphics g)
        {
            foreach (var e in edges)
            {
                var pa = P(e.a); var pb = P(e.b);
                bool dead = trimmedThreads.Contains(e.a.thread) || trimmedThreads.Contains(e.b.thread);
                if (e.type == "call")
                {

                    float dx = pb.X - pa.X, dy = pb.Y - pa.Y;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy) + 1e-3f;
                    float ux = dx / d, uy = dy / d;
                    PointF bEnd = new PointF(pb.X - ux * 8, pb.Y - uy * 8);
                    bool hot = e.a.active || e.b.active;
                    using (var p = new Pen(dead ? Color.FromArgb(60, 60, 60) : (hot ? LineLime : LineDim)))
                        g.DrawLine(p, pa, bEnd);
                }
                else
                {
                    bool hot = e.a.active || e.b.active;
                    using (var p = new Pen(dead ? Color.FromArgb(60, 60, 60) : (hot ? LineLime : LineDim)) { DashStyle = DashStyle.Dot })
                        g.DrawLine(p, pa, pb);
                    DrawPlug(g, (pa.X + pb.X) / 2, (pa.Y + pb.Y) / 2, dead);
                }
            }
        }

        private void DrawNodes(Graphics g)
        {
            foreach (var nd in nodes.Values)
            {
                var p = P(nd);
                bool dead = trimmedThreads.Contains(nd.thread);
                bool sel = nd == selected;
                Color c = dead ? Color.FromArgb(70, 70, 70) : (nd.active ? AlohaLime : White);
                float r = 3 + Math.Min(6, nd.hits * 0.5f);
                if (sel) c = Sel;

                if (nd.kind == "net")
                {

                    using (var b = new SolidBrush(c)) g.FillEllipse(b, p.X - r, p.Y - r, r * 2, r * 2);
                    DrawPlug(g, p.X, p.Y - r - 5, dead);
                }
                else
                {
                    using (var b = new SolidBrush(c)) g.FillEllipse(b, p.X - r, p.Y - r, r * 2, r * 2);

                    if (nd.kind == "func" && !dead)
                        using (var sp = new Pen(Color.FromArgb(120, c)))
                        {
                            g.DrawLine(sp, p.X - r - 4, p.Y, p.X - r, p.Y);
                            g.DrawLine(sp, p.X + r, p.Y, p.X + r + 4, p.Y);
                        }
                }
                if (sel)
                {
                    using (var ring = new Pen(Color.FromArgb(160, Sel))) g.DrawEllipse(ring, p.X - r - 3, p.Y - r - 3, (r + 3) * 2, (r + 3) * 2);
                    using (var ff = new Font("Consolas", 7f))
                    using (var tb = new SolidBrush(Sel))
                        g.DrawString(nd.name + "  " + nd.sig, ff, tb, p.X + r + 4, p.Y - 6);
                }
            }
        }

        private void DrawPlug(Graphics g, float x, float y, bool dead)
        {
            Color c = dead ? Color.FromArgb(70, 70, 70) : White;
            using (var p = new Pen(c))
            {
                g.DrawRectangle(p, x - 3, y - 2, 6, 4);
                g.DrawLine(p, x - 3, y - 1, x - 6, y - 1);
                g.DrawLine(p, x - 3, y + 1, x - 6, y + 1);
            }
        }

        private void DrawStatus(Graphics g, int x, int y, int w)
        {
            using (var ff = new Font("Consolas", 7.5f))
            using (var ti = new SolidBrush(Ink))
            using (var td = new SolidBrush(InkDim))
            {
                string s = "points " + nodes.Count + "   edges " + edges.Count + "   origins " + CountOrigins();
                g.DrawString(s, ff, ti, x + 2, y + 2);
                string hint = paused ? "PAUSED  [space] resume \u00b7 click node" : "[space] pause \u00b7 [T] trip \u00b7 click node";
                var sz = g.MeasureString(hint, ff);
                g.DrawString(hint, ff, td, x + w - sz.Width - 2, y + 2);
            }
        }
        private int CountOrigins()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var nd in nodes.Values) set.Add(nd.origin);
            return set.Count;
        }

        private void DrawCompromised(Graphics g, Rectangle box, long now)
        {
            long dt = now - compromisedAt;
            int pulse = (int)(70 + 50 * Math.Abs(Math.Sin(dt / 180.0)));
            using (var veil = new SolidBrush(Color.FromArgb(Math.Min(110, pulse), 0xff, 0, 0)))
                g.FillRectangle(veil, box);
            using (var bf = new Font("Consolas", 22f, FontStyle.Bold))
            using (var sf = new Font("Consolas", 9f, FontStyle.Bold))
            {
                string msg = "COMPROMISED";
                var sz = g.MeasureString(msg, bf);
                float cx = box.X + (box.Width - sz.Width) / 2, cy = box.Y + box.Height / 2f - sz.Height;
                using (var fg = new SolidBrush(Alarm)) g.DrawString(msg, bf, fg, cx, cy);
                string meta = "Thread 0x" + compromisedThread.ToString("X2") + " trimmed";
                using (var fg = new SolidBrush(Color.White)) g.DrawString(meta, sf, fg, cx, cy + sz.Height);
            }
        }

        private void Sentinel_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Space: paused = !paused; break;
                case Keys.Escape: if (compromised) compromised = false; selected = null; break;
                case Keys.T: foreach (int t in liveThreads) { Trip(t, "manual"); break; } break;
            }
        }

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            Node best = null; float bd = 12;
            foreach (var nd in nodes.Values)
            {
                var p = P(nd);
                float d = (float)Math.Sqrt((e.X - p.X) * (e.X - p.X) + (e.Y - p.Y) * (e.Y - p.Y));
                if (d < bd) { bd = d; best = nd; }
            }
            selected = best;
        }

        private static string SigOf(string src)
        {
            if (string.IsNullOrEmpty(src)) return "00 00 00 00";
            uint h = 2166136261;
            foreach (char ch in src) { h ^= ch; h *= 16777619; }
            return ((h >> 24) & 0xFF).ToString("X2") + " " + ((h >> 16) & 0xFF).ToString("X2") + " " +
                   ((h >> 8) & 0xFF).ToString("X2") + " " + (h & 0xFF).ToString("X2");
        }
        private static List<string> CalleeNames(string src)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(src)) return found;
            int i = 0, n = src.Length;
            while (i < n && found.Count < 8)
            {
                if (IsIdentStart(src[i]))
                {
                    int s = i; while (i < n && IsIdent(src[i])) i++;
                    if (i < n && src[i] == '(')
                    {
                        string id = src.Substring(s, i - s);
                        if (id != "function" && id != "if" && id != "for" && id != "while" && id != "return"
                            && id != "switch" && id != "catch" && !found.Contains(id))
                            found.Add(id);
                    }
                }
                else i++;
            }
            return found;
        }
        private static string SubCallsOf(string src)
        {
            var c = CalleeNames(src);
            return c.Count == 0 ? "" : ("\"" + string.Join("\",\"", c) + "\"");
        }
        private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_' || c == '$';
        private static bool IsIdent(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

        private struct FnDef { public string Name; public string Src; }
        private static string JsonStr(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n"); else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t"); else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
        private static string UnJsonStr(string s)
        {
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2);
            return s.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");
        }
        private static string Extract(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return null;
            i += key.Length;
            while (i < json.Length && json[i] == ' ') i++;
            if (i < json.Length && json[i] == '"')
            {
                int j = json.IndexOf('"', i + 1);
                while (j > 0 && json[j - 1] == '\\') j = json.IndexOf('"', j + 1);
                return j > i ? json.Substring(i, j - i + 1) : null;
            }
            int e = i; while (e < json.Length && json[e] != ',' && json[e] != '}') e++;
            return json.Substring(i, e - i);
        }
        private static IEnumerable<FnDef> ParseFnArray(string arr)
        {
            var res = new List<FnDef>();
            if (string.IsNullOrEmpty(arr)) return res;
            int i = 0, n = arr.Length;
            while (i < n)
            {
                int nq = arr.IndexOf("\"n\":\"", i, StringComparison.Ordinal);
                if (nq < 0) break;
                nq += 5; int ne = arr.IndexOf('"', nq); if (ne < 0) break;
                string name = arr.Substring(nq, ne - nq);
                string src = "";
                int sq = arr.IndexOf("\"src\":\"", ne, StringComparison.Ordinal);
                if (sq >= 0)
                {
                    sq += 7; int se = sq;
                    while (se < n) { if (arr[se] == '"' && arr[se - 1] != '\\') break; se++; }
                    if (se <= n) src = arr.Substring(sq, Math.Min(se, n) - sq);
                }
                res.Add(new FnDef { Name = name, Src = src });
                i = ne + 1;
            }
            return res;
        }
        private static string HostOf(string url)
        {
            try
            {
                int s = url.IndexOf("://", StringComparison.Ordinal);
                if (s >= 0) url = url.Substring(s + 3);
                int sl = url.IndexOf('/'); if (sl >= 0) url = url.Substring(0, sl);
                return url.Length > 22 ? url.Substring(0, 22) : url;
            }
            catch { return "?"; }
        }

        private static string PathTail(string url)
        {
            try
            {
                int q = url.IndexOf('?'); if (q >= 0) url = url.Substring(0, q);
                int s = url.IndexOf("://", StringComparison.Ordinal);
                if (s >= 0) url = url.Substring(s + 3);
                int sl = url.IndexOf('/'); if (sl < 0) return "";
                string path = url.Substring(sl + 1);
                int last = path.LastIndexOf('/');
                string tail = last >= 0 ? path.Substring(last + 1) : path;
                return tail.Length > 16 ? tail.Substring(0, 16) : tail;
            }
            catch { return ""; }
        }
    }
}
