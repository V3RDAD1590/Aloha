using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Media;
using System.Windows.Forms;

namespace Aloha.Cutscenes
{
    public sealed class CutsceneWindow : Form
    {
        private const double REF_H = 1080.0;

        private readonly CutsceneRule _rule;
        private readonly string _dir;
        private readonly Timer _tick;
        private readonly List<Item> _items = new List<Item>();
        private readonly DateTime _opened = DateTime.UtcNow;

        private int _step = -1;
        private DateTime _stepStart;
        private Color _bg = Color.Black;
        private bool _scanlines = true;
        private double _fade = 1.0;
        private Color _flashColor = Color.Empty;
        private DateTime _flashStart;
        private int _flashMs;
        private bool _closing;

        private Microsoft.Web.WebView2.WinForms.WebView2 _video;
        private bool _videoTried;
        private string _videoSrc, _videoOrigin;

        private Bitmap _shot;
        private bool _showShot;

        private sealed class ShiftField
        {
            public int UnitW;
            public int UnitH;
            public double[][] Delay;
            public double[][] Sway;
            public double Span;
            public double Accel;
            public bool Slide;
        }
        private ShiftField _field;
        private double _fieldT = -1.0;

        private enum Kind { Text, Rule, Box, Bars }

        private sealed class Item
        {
            public Kind Kind;
            public string Text;
            public Font Font;
            public Color Color;
            public RectangleF Rect;
            public bool Center;
            public int Reveal;
            public DateTime Born;
        }

        public CutsceneWindow(CutsceneRule rule, Form owner, bool fullDesktop, string dir)
        {
            _rule = rule;
            _dir = dir;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            KeyPreview = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            Screen scr = owner != null ? Screen.FromControl(owner) : Screen.PrimaryScreen;
            Bounds = fullDesktop || owner == null ? scr.Bounds : owner.Bounds;

            KeyDown += (s, e) => Dismiss();
            MouseDown += (s, e) => Dismiss();
            Deactivate += (s, e) => Dismiss();

            TryCapture(Bounds);

            _tick = new Timer();
            _tick.Interval = 33;
            _tick.Tick += (s, e) => Advance();

            if (rule.Scene != null && rule.Scene.Length > 0)
            {
                _step = -1;
                _stepStart = DateTime.UtcNow;
                _tick.Start();
            }
            else if (!BeginVideo())
            {
                _step = -1;
                _stepStart = DateTime.UtcNow;
                _tick.Start();
            }
        }

        protected override bool ShowWithoutActivation { get { return false; } }

        private bool _cursorHidden;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            try { Cursor.Hide(); _cursorHidden = true; } catch { }
        }

        private void TryCapture(Rectangle area)
        {
            try
            {
                var bmp = new Bitmap(Math.Max(1, area.Width), Math.Max(1, area.Height));
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(area.Location, Point.Empty, area.Size);
                _shot = bmp;
            }
            catch (Exception ex)
            {

                ActivityLog.General("Cutscene: screen capture unavailable - " + ex.Message);
                _shot = null;
            }
        }

        private void BuildMelt()
        {
            int uw = Math.Max(2, (int)S(6));
            int cols = Math.Max(1, Width / uw) + 1;
            var rng = new Random();
            var delay = new double[cols][];

            int prev = rng.Next(0, 16);
            for (int c = 0; c < cols; c++)
            {
                prev += rng.Next(-1, 2);
                if (prev < 0) prev = 0;
                if (prev > 15) prev = 15;
                delay[c] = new[] { prev / 15.0 * 0.40 };
            }
            _field = new ShiftField { UnitW = uw, UnitH = 0, Delay = delay,
                                      Span = 1.22, Accel = 1.15, Slide = true };
            _fieldT = 0.0; _showShot = true;
        }

        private void BuildCascade()
        {
            int uw = Math.Max(6, (int)S(10));
            int uh = Math.Max(10, (int)S(18));
            int cols = Math.Max(1, Width / uw) + 1;
            int rows = Math.Max(1, Height / uh) + 1;
            var rng = new Random();
            var delay = new double[cols][];
            var sway = new double[cols][];
            for (int c = 0; c < cols; c++)
            {
                delay[c] = new double[rows];
                sway[c] = new double[rows];
                for (int r = 0; r < rows; r++)
                {

                    delay[c][r] = (r / (double)rows) * 0.55 + rng.NextDouble() * 0.30;
                    sway[c][r] = (rng.NextDouble() - 0.5) * uw * 0.9;
                }
            }
            _field = new ShiftField { UnitW = uw, UnitH = uh, Delay = delay, Sway = sway,
                                      Span = 1.0, Accel = 4.0, Slide = false };
            _fieldT = 0.0; _showShot = true;
        }

        private void DrawShot(Graphics g)
        {
            if (_shot == null) return;
            if (_field == null || _fieldT < 0.0) { g.DrawImage(_shot, 0, 0, Width, Height); return; }

            var f = _field;
            int cols = f.Delay.Length;
            int rows = f.UnitH <= 0 ? 1 : f.Delay[0].Length;

            for (int c = 0; c < cols; c++)
            {
                int x0 = c * f.UnitW;
                if (x0 >= _shot.Width) break;
                int w = Math.Min(f.UnitW, _shot.Width - x0);
                if (w <= 0) continue;

                for (int r = 0; r < rows; r++)
                {
                    double t = _fieldT * f.Span - f.Delay[c][r];
                    if (t < 0.0) t = 0.0;
                    int dy = (int)(t * t * Height * f.Accel);

                    if (f.Slide)
                    {
                        if (dy >= Height) continue;
                        int h = Math.Min(Height - dy, _shot.Height);
                        if (h <= 0) continue;
                        g.DrawImage(_shot, new Rectangle(x0, dy, w, h),
                                           new Rectangle(x0, 0, w, h), GraphicsUnit.Pixel);
                    }
                    else
                    {
                        int sy = r * f.UnitH;
                        if (sy + f.UnitH > _shot.Height) continue;
                        int y = sy + dy;
                        if (y >= Height) continue;
                        int dx = f.Sway != null ? (int)(f.Sway[c][r] * Math.Min(1.0, t * 4.0)) : 0;
                        int x = x0 + dx;
                        if (x < 0 || x + w > Width) x = x0;
                        int h = Math.Min(f.UnitH, Height - y);
                        if (h <= 0) continue;
                        g.DrawImage(_shot, new Rectangle(x, y, w, h),
                                           new Rectangle(x0, sy, w, h), GraphicsUnit.Pixel);
                    }
                }
            }
        }

        private bool BeginVideo()
        {
            if (string.IsNullOrEmpty(_rule.Video)) return false;
            try { return StartVideo(_rule.Video); }
            catch (Exception ex)
            {
                ActivityLog.General("Cutscene video failed - " + ex.Message);
                return false;
            }
        }

        private bool StartVideo(string spec)
        {
            bool remote = spec.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                       || spec.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            string origin;

            if (remote)
            {
                Uri u;
                if (!Uri.TryCreate(spec, UriKind.Absolute, out u)) return false;

                if (!Aloha.Net.EgressGate.Current.ShouldAllow(u.Host, u.Port))
                {
                    ActivityLog.General("Cutscene: egress gate refused " + u.Host);
                    return false;
                }
                _videoSrc = u.AbsoluteUri;
                origin = u.Host;
            }
            else
            {
                string path = Path.IsPathRooted(spec) ? spec : Path.Combine(_dir, spec);
                if (!File.Exists(path)) return false;
                _videoSrc = new Uri(path).AbsoluteUri;
                origin = "local file";
            }
            _videoOrigin = origin;

            _video = new Microsoft.Web.WebView2.WinForms.WebView2();
            _video.Dock = DockStyle.Fill;
            _video.DefaultBackgroundColor = Color.Black;
            Controls.Add(_video);

            var skip = new Label();
            skip.Text = "ESC \u2014 SKIP";
            skip.AutoSize = true;
            skip.ForeColor = Color.FromArgb(0xB4, 0xDC, 0xBA);
            skip.BackColor = Color.Black;
            skip.Font = new Font("Tahoma", 9f, FontStyle.Bold);
            skip.Padding = new Padding(8, 4, 8, 4);
            skip.Cursor = Cursors.Hand;
            skip.Click += (s, e) => Dismiss();
            Controls.Add(skip);
            skip.BringToFront();
            skip.Location = new Point(Width - skip.PreferredWidth - 24, 24);
            skip.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            var band = new Label();
            band.Text = "  presented by " + origin + "  ";
            band.AutoSize = true;
            band.ForeColor = Color.FromArgb(0x1C, 0xA8, 0x1C);
            band.BackColor = Color.Black;
            band.Font = new Font("Tahoma", 8f, FontStyle.Regular);
            band.Padding = new Padding(6, 3, 6, 3);
            Controls.Add(band);
            band.BringToFront();
            band.Location = new Point(24, Height - band.PreferredHeight - 24);
            band.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;

            InitVideoAsync();
            if (_rule.TimeoutMs > 0) { _tick.Interval = 200; _tick.Start(); }
            else { _tick.Stop(); }
            return true;
        }

        private async void InitVideoAsync()
        {
            try
            {
                string udf = Path.Combine(_dir, "wv2");
                Directory.CreateDirectory(udf);
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment
                                .CreateAsync(null, udf, null);
                await _video.EnsureCoreWebView2Async(env);
                if (IsDisposed) return;

                var s = _video.CoreWebView2.Settings;
                s.AreDevToolsEnabled = false;
                s.AreDefaultContextMenusEnabled = false;
                s.IsStatusBarEnabled = false;
                s.AreBrowserAcceleratorKeysEnabled = false;
                s.IsZoomControlEnabled = false;

                _video.CoreWebView2.NewWindowRequested += (o, a) => { a.Handled = true; };
                _video.CoreWebView2.WindowCloseRequested += (o, a) => Dismiss();

                _video.WebMessageReceived += (o, a) => Dismiss();
                _video.CoreWebView2.NavigationCompleted += (o, a) =>
                {
                    if (!a.IsSuccess)
                    {
                        ActivityLog.General("Cutscene: media failed (" + a.WebErrorStatus + ")");
                        Dismiss();
                    }
                };

                string html =
                    "<!doctype html><html><head><meta charset=\"utf-8\">" +
                    "<style>html,body{margin:0;height:100%;background:#000;overflow:hidden}" +
                    "video{position:absolute;top:0;left:0;width:100%;height:100%;" +
                    "object-fit:contain;background:#000}</style></head><body>" +
                    "<video id=\"v\" src=\"" + Esc(_videoSrc) + "\" autoplay " +
                    (_rule.VideoMuted ? "muted " : "") + "playsinline></video>" +
                    "<script>(function(){var v=document.getElementById('v');" +
                    "var bail=function(){try{window.chrome.webview.postMessage('end');}catch(e){}};" +
                    "v.addEventListener('ended',bail);v.addEventListener('error',bail);" +
                    "document.addEventListener('keydown',bail,true);" +
                    "document.addEventListener('mousedown',bail,true);" +
                    "v.play().catch(function(){v.muted=true;v.play().catch(bail);});" +
                    "})();<\\/script></body></html>";

                _video.CoreWebView2.NavigateToString(html);
            }
            catch (Exception ex)
            {
                ActivityLog.General("Cutscene video init failed - " + ex.Message);
                Dismiss();
            }
        }

        private static string Esc(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            return v.Replace("&", "&amp;").Replace("\"", "&quot;")
                    .Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private void Advance()
        {
            if (_closing) return;

            if (_rule.TimeoutMs > 0 && (DateTime.UtcNow - _opened).TotalMilliseconds > _rule.TimeoutMs)
            { Dismiss(); return; }

            if (_video != null) return;

            bool again = true;
            while (again)
            {
                again = false;
                if (_step < 0) { _step = 0; _stepStart = DateTime.UtcNow; Begin(Step(_step)); }
                var st = Step(_step);
                if (st == null)
                {

                    if (!_videoTried && !string.IsNullOrEmpty(_rule.Video))
                    {
                        _videoTried = true;
                        if (BeginVideo()) break;
                    }
                    if ((DateTime.UtcNow - _stepStart).TotalMilliseconds > 900) Dismiss();
                    break;
                }
                if (Done(st))
                {
                    _step++;
                    _stepStart = DateTime.UtcNow;
                    var next = Step(_step);
                    if (next != null) { Begin(next); again = true; }
                }
            }
            Invalidate();
        }

        private CutsceneStep Step(int i)
        {
            return (i >= 0 && i < _rule.Scene.Length) ? _rule.Scene[i] : null;
        }

        private float S(double v) { return (float)(v * Height / REF_H); }

        private Font MakeFont(CutsceneStep st)
        {
            float size = S(st.Size > 0 ? st.Size : 28);
            if (size < 6f) size = 6f;
            var style = st.Bold ? FontStyle.Bold : FontStyle.Regular;
            try { return new Font("Consolas", size, style, GraphicsUnit.Pixel); }
            catch { return new Font(FontFamily.GenericMonospace, size, style, GraphicsUnit.Pixel); }
        }

        private static Color Parse(string s, Color fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            try
            {
                if (s.StartsWith("#", StringComparison.Ordinal))
                {
                    string h = s.Substring(1);
                    if (h.Length == 6)
                        return Color.FromArgb(
                            Convert.ToInt32(h.Substring(0, 2), 16),
                            Convert.ToInt32(h.Substring(2, 2), 16),
                            Convert.ToInt32(h.Substring(4, 2), 16));
                }
                return Color.FromName(s);
            }
            catch { return fallback; }
        }

        private static readonly Color Lime = Color.FromArgb(0xB4, 0xDC, 0xBA);

        private void Begin(CutsceneStep st)
        {
            string op = (st.Op ?? "").ToLowerInvariant();
            switch (op)
            {
                case "clear":
                    foreach (var it in _items) if (it.Font != null) it.Font.Dispose();
                    _items.Clear();
                    _bg = Parse(st.Color, Color.Black);
                    break;

                case "print":
                case "type":
                    _items.Add(new Item
                    {
                        Kind = Kind.Text,
                        Text = st.Text ?? "",
                        Font = MakeFont(st),
                        Color = Parse(st.Color, Lime),
                        Rect = new RectangleF((float)(st.X * Width), (float)(st.Y * Height), 0, 0),
                        Center = st.Center,
                        Reveal = op == "print" ? int.MaxValue : 0,
                        Born = DateTime.UtcNow
                    });
                    break;

                case "rule":
                    _items.Add(new Item
                    {
                        Kind = Kind.Rule,
                        Color = Parse(st.Color, Lime),
                        Rect = new RectangleF((float)(st.X * Width), (float)(st.Y * Height),
                                              (float)((st.W > 0 ? st.W : 1.0) * Width), Math.Max(1f, S(2))),
                        Born = DateTime.UtcNow
                    });
                    break;

                case "box":
                    _items.Add(new Item
                    {
                        Kind = Kind.Box,
                        Color = Parse(st.Color, Lime),
                        Rect = new RectangleF((float)(st.X * Width), (float)(st.Y * Height),
                                              (float)(st.W * Width), (float)(st.H * Height)),
                        Born = DateTime.UtcNow
                    });
                    break;

                case "bars":
                    _items.Add(new Item
                    {
                        Kind = Kind.Bars,
                        Color = Parse(st.Color, Lime),
                        Rect = new RectangleF((float)(st.X * Width), (float)(st.Y * Height),
                                              (float)((st.W > 0 ? st.W : 0.3) * Width),
                                              (float)((st.H > 0 ? st.H : 0.08) * Height)),
                        Born = DateTime.UtcNow
                    });
                    break;

                case "scan":
                    _scanlines = !string.Equals(st.Text, "off", StringComparison.OrdinalIgnoreCase);
                    break;

                case "flash":
                    _flashColor = Parse(st.Color, Color.White);
                    _flashMs = st.Ms > 0 ? st.Ms : 160;
                    _flashStart = DateTime.UtcNow;
                    break;

                case "freeze":
                    _showShot = _shot != null; _field = null; _fieldT = -1.0;
                    break;

                case "melt":
                    if (_shot != null) BuildMelt();
                    break;

                case "cascade":
                    if (_shot != null) BuildCascade();
                    break;

                case "sound":
                    try { SystemSounds.Beep.Play(); } catch { }
                    break;
            }
        }

        private bool Done(CutsceneStep st)
        {
            string op = (st.Op ?? "").ToLowerInvariant();
            double ms = (DateTime.UtcNow - _stepStart).TotalMilliseconds;

            switch (op)
            {
                case "type":
                    {
                        var it = _items.Count > 0 ? _items[_items.Count - 1] : null;
                        if (it == null || it.Kind != Kind.Text) return true;
                        int cps = st.Cps > 0 ? st.Cps : 38;
                        it.Reveal = (int)(ms * cps / 1000.0);
                        if (it.Reveal < it.Text.Length) return false;
                        it.Reveal = it.Text.Length;
                        return ms >= st.Ms;
                    }
                case "wait":  return ms >= (st.Ms > 0 ? st.Ms : 500);
                case "melt":
                case "cascade":
                    {
                        if (_shot == null || _field == null) return true;
                        int d = st.Ms > 0 ? st.Ms : (_field.Slide ? 2200 : 2800);
                        _fieldT = ms / d;
                        if (ms < d) return false;
                        if (!string.Equals(st.Text, "keep", StringComparison.OrdinalIgnoreCase))
                        { _showShot = false; _field = null; _fieldT = -1.0; }
                        return true;
                    }
                case "bars":  return ms >= (st.Ms > 0 ? st.Ms : 1200);
                case "flash": return ms >= _flashMs;
                case "fade":
                    {
                        int d = st.Ms > 0 ? st.Ms : 500;
                        bool outward = string.Equals(st.Text, "out", StringComparison.OrdinalIgnoreCase);
                        double t = Math.Min(1.0, ms / d);
                        _fade = outward ? 1.0 - t : t;
                        return ms >= d;
                    }
                default:      return ms >= st.Ms;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(_bg);

            if (_video != null) return;

            if (_showShot) DrawShot(g);

            foreach (var it in _items)
            {
                using (var brush = new SolidBrush(it.Color))
                using (var pen = new Pen(it.Color, Math.Max(1f, S(2))))
                {
                    switch (it.Kind)
                    {
                        case Kind.Text:
                            {
                                string shown = it.Reveal >= it.Text.Length
                                             ? it.Text
                                             : it.Text.Substring(0, Math.Max(0, it.Reveal));
                                if (shown.Length == 0) break;
                                SizeF sz = g.MeasureString(shown, it.Font);
                                float x = it.Center ? (Width - sz.Width) / 2f : it.Rect.X;
                                g.DrawString(shown, it.Font, brush, x, it.Rect.Y);

                                if (it.Reveal < it.Text.Length &&
                                    DateTime.UtcNow.Millisecond % 600 < 320)
                                    g.FillRectangle(brush, x + sz.Width, it.Rect.Y + sz.Height * 0.12f,
                                                    it.Font.Size * 0.6f, sz.Height * 0.76f);
                                break;
                            }
                        case Kind.Rule:
                            g.FillRectangle(brush, it.Rect.X, it.Rect.Y, it.Rect.Width, it.Rect.Height);
                            break;
                        case Kind.Box:
                            g.DrawRectangle(pen, it.Rect.X, it.Rect.Y, it.Rect.Width, it.Rect.Height);
                            break;
                        case Kind.Bars:
                            DrawBars(g, it, brush);
                            break;
                    }
                }
            }

            if (_scanlines)
            {
                int step = Math.Max(2, (int)S(3));
                using (var p = new Pen(Color.FromArgb(38, 0, 0, 0), 1f))
                    for (int y = 0; y < Height; y += step) g.DrawLine(p, 0, y, Width, y);
            }

            using (var path = new GraphicsPath())
            {
                path.AddEllipse(-Width * 0.25f, -Height * 0.35f, Width * 1.5f, Height * 1.7f);
                using (var br = new PathGradientBrush(path))
                {
                    br.CenterColor = Color.FromArgb(0, 0, 0, 0);
                    br.SurroundColors = new[] { Color.FromArgb(150, 0, 0, 0) };
                    g.FillRectangle(br, ClientRectangle);
                }
            }

            if (_flashColor != Color.Empty)
            {
                double t = (DateTime.UtcNow - _flashStart).TotalMilliseconds / Math.Max(1, _flashMs);
                if (t < 1.0)
                {
                    int a = (int)(220 * (1.0 - t));
                    using (var br = new SolidBrush(Color.FromArgb(Math.Max(0, Math.Min(255, a)),
                                                                 _flashColor)))
                        g.FillRectangle(br, ClientRectangle);
                }
            }

            if (_fade < 1.0)
            {
                int a = (int)(255 * (1.0 - _fade));
                using (var br = new SolidBrush(Color.FromArgb(Math.Max(0, Math.Min(255, a)), 0, 0, 0)))
                    g.FillRectangle(br, ClientRectangle);
            }
        }

        private void DrawBars(Graphics g, Item it, SolidBrush brush)
        {
            double age = (DateTime.UtcNow - it.Born).TotalSeconds;
            int n = 24;
            float bw = it.Rect.Width / n;
            for (int i = 0; i < n; i++)
            {
                double phase = age * 2.4 + i * 0.42;
                float h = (float)((0.35 + 0.65 * Math.Abs(Math.Sin(phase))) * it.Rect.Height);
                g.FillRectangle(brush, it.Rect.X + i * bw, it.Rect.Bottom - h,
                                Math.Max(1f, bw * 0.62f), h);
            }
        }

        private void Dismiss()
        {
            if (_closing) return;
            _closing = true;
            try { _tick.Stop(); } catch { }
            BeginInvoke((Action)(() => { try { Close(); } catch { } }));
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Dismiss();
            return true;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { _tick.Dispose(); } catch { }
            foreach (var it in _items) { try { if (it.Font != null) it.Font.Dispose(); } catch { } }
            _items.Clear();
            try { if (_video != null) { _video.Dispose(); _video = null; } } catch { }
            try { if (_shot != null) { _shot.Dispose(); _shot = null; } } catch { }
            if (_cursorHidden) { try { Cursor.Show(); _cursorHidden = false; } catch { } }
            if (_cursorHidden) { try { Cursor.Show(); _cursorHidden = false; } catch { } }
            base.OnFormClosed(e);
        }
    }
}
