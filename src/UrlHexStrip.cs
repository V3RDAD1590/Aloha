using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace Aloha
{

    public class UrlHexStrip : Form
    {

        private static readonly Color Green = TorrcSyntax.Green;
        private static readonly Color Hi    = TorrcSyntax.GreenHi;
        private static readonly Color Dim   = TorrcSyntax.GreenLo;
        private static readonly Color Alarm = Color.FromArgb(0xFF, 0x44, 0x44);

        private const int MAX_BYTES = 2048;
        private const int PAD       = 6;

        private readonly Font mono = new Font("Consolas", 9f);
        private byte[] bytes = new byte[0];
        private List<string> verdict = new List<string>();
        private string dumpText = "";        
        private Timer copyFlash;
        private bool copied;
        private float cw = 7f, lh = 13f;
        private bool measured;

        private int scrollRow;
        private int maxH = int.MaxValue;
        private bool sbDrag;
        private int sbGrab;

        private bool NeedsScroll { get { return Height > 0 && ContentHeight > Height; } }

        private int TotalRows
        {
            get { return (bytes.Length + 15) / 16 + verdict.Count + 2; }
        }
        private int ContentHeight { get { return (int)(TotalRows * lh) + PAD * 2; } }
        private int VisibleRows   { get { return Math.Max(1, (int)((Height - PAD * 2) / lh)); } }
        private int MaxScrollRow  { get { return Math.Max(0, TotalRows - VisibleRows); } }

        private const int SB_W = 11;

        public void ScrollBy(int deltaNotches)
        {
            if (!NeedsScroll) return;
            int next = Math.Max(0, Math.Min(MaxScrollRow, scrollRow - deltaNotches * 3));
            if (next == scrollRow) return;
            scrollRow = next;
            Invalidate();
        }

        private int ThumbH
        {
            get
            {
                float frac = VisibleRows / (float)Math.Max(1, TotalRows);
                return Math.Max(18, (int)(Height * frac));
            }
        }
        private int ThumbY
        {
            get
            {
                int track = Height - ThumbH;
                return (MaxScrollRow == 0 || track <= 0)
                     ? 0 : (int)(track * (scrollRow / (float)MaxScrollRow));
            }
        }
        private void ScrollToThumbY(int y)
        {
            int track = Height - ThumbH;
            if (track <= 0) return;
            int r = (int)Math.Round(MaxScrollRow * (Math.Max(0, Math.Min(track, y)) / (float)track));
            if (r == scrollRow) return;
            scrollRow = r; Invalidate();
        }

        private bool OnBar(int x) { return NeedsScroll && x >= Width - SB_W; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (OnBar(e.X))
            {
                int ty = ThumbY, th = ThumbH;
                if (e.Y >= ty && e.Y < ty + th) { sbDrag = true; sbGrab = e.Y - ty; }
                else ScrollToThumbY(e.Y - th / 2);
                return;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (sbDrag) { ScrollToThumbY(e.Y - sbGrab); return; }
            Cursor = OnBar(e.X) ? Cursors.Default : Cursors.Hand;
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (sbDrag) { sbDrag = false; return; }
            if (OnBar(e.X)) return;
            try
            {
                if (!string.IsNullOrEmpty(dumpText))
                {
                    Clipboard.SetText(dumpText);
                    copied = true; Invalidate();
                    copyFlash.Stop(); copyFlash.Start();
                }
            }
            catch { }
            base.OnMouseUp(e);
        }

        public UrlHexStrip()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition   = FormStartPosition.Manual;
            ShowInTaskbar   = false;
            BackColor       = Color.Black;
            DoubleBuffered  = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);

            copyFlash = new Timer { Interval = 1100 };
            copyFlash.Tick += (s, e) => { copyFlash.Stop(); copied = false; Invalidate(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        public static string HostOf(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            string t = s.Trim();

            int scheme = t.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) t = t.Substring(scheme + 3);

            int slash = t.IndexOfAny(new[] { '/', '?', '#' });
            if (slash >= 0) t = t.Substring(0, slash);

            int at = t.LastIndexOf('@');
            if (at >= 0) t = t.Substring(at + 1);

            if (t.StartsWith("[", StringComparison.Ordinal))     
            {
                int close = t.IndexOf(']');
                return close > 0 ? t.Substring(0, close + 1) : t;
            }
            int colon = t.LastIndexOf(':');
            if (colon > 0) t = t.Substring(0, colon);

            t = t.Trim();
            return t.Length == 0 ? null : t;
        }

        private static bool IsAscii(string s)
        {
            if (s == null) return true;
            foreach (char c in s) if (c > 0x7F) return false;
            return true;
        }

        private static bool HasPunycodeLabel(string host)
        {
            if (host == null) return false;
            foreach (var label in host.Split('.'))
                if (label.StartsWith("xn--", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static readonly Dictionary<int, string> Known = new Dictionary<int, string>
        {

            { 0x0430, "cyrillic small letter a           looks like 'a'" },
            { 0x0435, "cyrillic small letter ie          looks like 'e'" },
            { 0x043E, "cyrillic small letter o           looks like 'o'" },
            { 0x0440, "cyrillic small letter er          looks like 'p'" },
            { 0x0441, "cyrillic small letter es          looks like 'c'" },
            { 0x0443, "cyrillic small letter u           looks like 'y'" },
            { 0x0445, "cyrillic small letter ha          looks like 'x'" },
            { 0x0455, "cyrillic small letter dze         looks like 's'" },
            { 0x0456, "cyrillic small letter byelo. i    looks like 'i'" },
            { 0x0458, "cyrillic small letter je          looks like 'j'" },

            { 0x03B1, "greek small letter alpha          looks like 'a'" },
            { 0x03BD, "greek small letter nu             looks like 'v'" },
            { 0x03BF, "greek small letter omicron        looks like 'o'" },
            { 0x03C1, "greek small letter rho            looks like 'p'" },

            { 0x3002, "ideographic full stop             ACTS AS '.'" },
            { 0xFF0E, "fullwidth full stop               ACTS AS '.'" },
            { 0xFF61, "halfwidth ideographic full stop   ACTS AS '.'" },

            { 0x200B, "zero width space                  INVISIBLE" },
            { 0x200C, "zero width non-joiner             INVISIBLE" },
            { 0x200D, "zero width joiner                 INVISIBLE" },
            { 0x2060, "word joiner                       INVISIBLE" },
            { 0xFEFF, "zero width no-break space (BOM)   INVISIBLE" },
            { 0x00A0, "no-break space                    looks like ' '" },

            { 0x202E, "right-to-left override            REVERSES DISPLAY" },
            { 0x202D, "left-to-right override            REORDERS DISPLAY" },
            { 0x200F, "right-to-left mark                REORDERS DISPLAY" },
            { 0x200E, "left-to-right mark                REORDERS DISPLAY" },
        };

        private static string Describe(int cp)
        {
            string s;
            if (Known.TryGetValue(cp, out s)) return s;
            if (cp >= 0xFF41 && cp <= 0xFF5A)
                return "fullwidth latin small letter     looks like '"
                     + (char)(cp - 0xFF41 + 'a') + "'";
            return CharUnicodeInfo.GetUnicodeCategory((char)cp) + " — non-ascii";
        }

        public bool SetUrl(string url, bool onlyIfNotable)
        {
            if (string.IsNullOrWhiteSpace(url)) { if (Visible) Hide(); return false; }

            string host = HostOf(url);
            bool notable = host != null && (!IsAscii(host) || HasPunycodeLabel(host));
            if (onlyIfNotable && !notable) { if (Visible) Hide(); return false; }

            string shown = url.Trim();
            bytes = Encoding.UTF8.GetBytes(shown);
            if (bytes.Length > MAX_BYTES)
            {
                var cut = new byte[MAX_BYTES];
                Array.Copy(bytes, cut, MAX_BYTES);
                bytes = cut;
            }

            verdict = BuildVerdict(host);
            dumpText = BuildText();
            Invalidate();
            return true;
        }

        private string BuildText()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < bytes.Length; i += 16)
            {
                var row = new StringBuilder(new string(' ', LINE_COLS));
                void put(int col, string s) { for (int k = 0; k < s.Length; k++) row[col + k] = s[k]; }

                put(0, i.ToString("X4"));
                int line = Math.Min(16, bytes.Length - i);
                for (int j = 0; j < line; j++)
                {
                    byte b = bytes[i + j];
                    put(HexCol(j), b.ToString("X2"));
                    row[GutterCol(j)] = (b >= 32 && b < 127) ? (char)b : '.';
                }
                sb.AppendLine(row.ToString().TrimEnd());
            }
            sb.AppendLine();
            foreach (var v in verdict)
                sb.AppendLine(v.Length > 0 ? new string(' ', COL_HEX) + v : "");
            return sb.ToString();
        }

        private static List<string> BuildVerdict(string host)
        {
            var v = new List<string>();
            if (string.IsNullOrEmpty(host)) return v;

            var idn = new IdnMapping();
            bool puny = HasPunycodeLabel(host);

            if (IsAscii(host) && !puny)
            {

                v.Add("");
                v.Add(host);
                return v;
            }

            string ascii = host, unicode = host;
            try { ascii   = idn.GetAscii(host);   } catch { }
            try { unicode = idn.GetUnicode(host); } catch { }

            var seen = new HashSet<int>();
            for (int i = 0; i < unicode.Length; i++)
            {
                int cp = char.ConvertToUtf32(unicode, i);
                if (char.IsHighSurrogate(unicode[i])) i++;
                if (cp <= 0x7F || !seen.Add(cp)) continue;
                v.Add("U+" + cp.ToString("X4") + "  " + Describe(cp));
            }

            v.Add("");
            if (puny)
            {

                v.Add("punycode      " + host);
                v.Add("displays as   " + unicode);
            }
            else if (ascii != host)
            {

                v.Add("resolves as   " + ascii);
                v.Add("NOT           " + host);
            }
            else
            {
                v.Add("host is not valid IDN — cannot be resolved as typed");
            }
            return v;
        }

        private const int COL_HEX    = 6;
        private const int COL_GUTTER = 56;
        private const int LINE_COLS  = 72;

        private static int HexCol(int j)    { return COL_HEX + j * 3 + (j > 7 ? 1 : 0); }
        private static int GutterCol(int j) { return COL_GUTTER + j; }

        private void Measure(Graphics g)
        {
            if (measured) return;

            var sz = TextRenderer.MeasureText(g, new string('0', 100), mono,
                        new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            cw = sz.Width / 100f;
            lh = sz.Height;
            measured = true;
        }

        public int PreferredHeight
        {
            get
            {
                int lines = (bytes.Length + 15) / 16;
                return (int)((lines + verdict.Count + 2) * lh) + PAD * 2;
            }
        }

        private void Draw(Graphics g, string s, int col, int row, Color c)
        {
            TextRenderer.DrawText(g, s, mono,
                new Point((int)(PAD + col * cw), (int)(PAD + row * lh)), c,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.Black);
            Measure(g);

            int row = -scrollRow;
            for (int i = 0; i < bytes.Length; i += 16, row++)
            {
                if (row < 0) continue;
                if (row > VisibleRows) break;
                Draw(g, i.ToString("X4"), 0, row, Dim);   

                int line = Math.Min(16, bytes.Length - i);
                for (int j = 0; j < line; j++)
                {
                    byte b = bytes[i + j];
                    bool hot = b >= 0x80;
                    Draw(g, b.ToString("X2"), HexCol(j), row, hot ? Alarm : Green);
                    Draw(g, hot ? "." : ((b >= 32 && b < 127) ? ((char)b).ToString() : "."),
                         GutterCol(j), row, hot ? Alarm : Hi);
                }
            }

            row++;
            foreach (var v in verdict)
            {
                if (v.Length > 0 && row >= 0 && row <= VisibleRows)
                {
                    bool danger = v.StartsWith("U+", StringComparison.Ordinal) ||
                                  v.StartsWith("resolves", StringComparison.Ordinal) ||
                                  v.StartsWith("punycode", StringComparison.Ordinal) ||
                                  v.StartsWith("displays", StringComparison.Ordinal) ||
                                  v.StartsWith("host is not", StringComparison.Ordinal);
                    Draw(g, v, COL_HEX, row, danger ? Alarm : Green);
                }
                row++;
            }

            if (row >= 0 && row <= VisibleRows)
                Draw(g, copied ? "copied to clipboard" : "click to copy   esc to close",
                     COL_HEX, row, Hi);

            if (NeedsScroll) DrawScrollBar(g);
        }

        private void DrawScrollBar(Graphics g)
        {
            int x = Width - SB_W;
            using (var br = new SolidBrush(Color.FromArgb(0x0A, 0x0A, 0x0A)))
                g.FillRectangle(br, x, 0, SB_W, Height);
            using (var br = new SolidBrush(sbDrag ? Hi : Dim))
                g.FillRectangle(br, x + 2, ThumbY + 1, SB_W - 4, ThumbH - 2);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (mono != null) mono.Dispose();
                if (copyFlash != null) copyFlash.Dispose();
            }
            base.Dispose(disposing);
        }

        public void PinUnder(Form owner, Control anchor)
        {
            if (owner == null || anchor == null) return;
            Point tl = owner.PointToScreen(new Point(anchor.Left, anchor.Bottom));

            maxH = Math.Max(60, owner.ClientSize.Height / 2);
            int h = Math.Max(40, Math.Min(PreferredHeight, maxH));
            if (scrollRow > MaxScrollRow) scrollRow = MaxScrollRow;
            Bounds = new Rectangle(tl.X, tl.Y, anchor.Width, h);
        }
    }
}
