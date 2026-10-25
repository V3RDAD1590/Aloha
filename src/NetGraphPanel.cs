using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Windows.Forms;

namespace Aloha
{

    public class NetGraphPanel : DafyFrame
    {
        private static readonly Color Modern = Color.FromArgb(0xF0, 0xF0, 0xF0);
        private static readonly Color Green  = Color.FromArgb(0xB4, 0xDC, 0xBA);
        private const int SB = 17;

        private const double GOLDEN = 2.39996323;

        private Canvas canvas;
        private Panel corner, status;
        private Timer poll;
        private int slotSeq = 0;
        private string statusText = "reading…";
        private Panel header;
        private Font headerFont;
        private int radarX = -107, radarY = 78, eventsCount = 0, alertsCount = 0;

        private Font nodeFont, localFont, hudFont, detailFont;

        private readonly Dictionary<string, Node> nodes =
            new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        private Node local;
        private string hoverKey, dragKey;
        private Point mouse;

        private class Node
        {
            public string Key;
            public float X, Y;
            public double Angle;
            public bool Pinned;
            public int Count;
            public bool IsLocal;
            public Dictionary<TcpState, int> States = new Dictionary<TcpState, int>();
            public Color Tone = Color.FromArgb(0x7F, 0xAE, 0x86);
        }

        private class Canvas : Panel
        {
            public Canvas() { this.DoubleBuffered = true; this.BackColor = Color.Black; }
        }

        public NetGraphPanel() : base("OPT-0700", "Network map")
        {
            Size = new Size(760, 600);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(120, 80);
            ShowInTaskbar = true;
            ClientArea.BackColor = Color.Black;

            nodeFont   = new Font("Consolas", 7.5f);
            localFont  = new Font("Consolas", 8f, FontStyle.Bold);
            hudFont    = new Font("Consolas", 7.5f);
            detailFont = new Font("Consolas", 8f);
            headerFont = new Font("Lucida Console", 9f, FontStyle.Bold);

            header = new Panel { BackColor = Color.White, Height = 24 };
            header.Paint += Header_Paint;

            canvas = new Canvas { Dock = DockStyle.None };
            canvas.Paint      += Canvas_Paint;
            canvas.MouseMove  += Canvas_MouseMove;
            canvas.MouseDown  += Canvas_MouseDown;
            canvas.MouseUp    += (s, e) => { dragKey = null; };
            canvas.MouseLeave += (s, e) => { hoverKey = null; canvas.Invalidate(); };

            corner = new Panel { BackColor = Modern };
            MakeResizeGrip(corner);

            local = new Node { Key = "*local*", IsLocal = true, Tone = Green };
            nodes[local.Key] = local;

            var btnRefresh = MakeLabeledButton("Refresh", () => { Poll(); canvas.Invalidate(); });
            var btnTidy = MakeLabeledButton("Tidy", Tidy);
            status = new Panel { Top = 3, Height = 20, Width = 360, BackColor = Modern };
            status.Paint += (s, e) =>
                TextRenderer.DrawText(e.Graphics, statusText, detailFont, status.ClientRectangle,
                    Color.FromArgb(0x44, 0x44, 0x44),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            Footer.Controls.Add(btnRefresh);
            Footer.Controls.Add(btnTidy);
            Footer.Controls.Add(status);
            Footer.Resize += (s, e) => LayoutFooterBits(btnRefresh, btnTidy);
            LayoutFooterBits(btnRefresh, btnTidy);

            ClientArea.Controls.Add(canvas);
            ClientArea.Controls.Add(corner);
            ClientArea.Controls.Add(header);
            header.BringToFront();
            ClientArea.Resize += (s, e) => LayoutMain();
            this.Shown += (s, e) => { LayoutMain(); Poll(); };
            this.FormClosed += (s, e) =>
            {
                poll?.Stop(); poll?.Dispose();
                nodeFont?.Dispose(); localFont?.Dispose(); hudFont?.Dispose(); detailFont?.Dispose();
                headerFont?.Dispose();
            };

            poll = new Timer { Interval = 2000 };
            poll.Tick += (s, e) => Poll();
            poll.Start();
        }

        private void LayoutMain()
        {
            int cw = ClientArea.ClientSize.Width, ch = ClientArea.ClientSize.Height;
            if (cw < 20 || ch < 20) return;
            canvas.SetBounds(0, 24, cw, ch - 24);
            header.SetBounds(0, 0, cw, 24);
            header.BringToFront();
            corner.SetBounds(cw - SB, ch - SB, SB, SB);
            corner.BringToFront();
            Relayout();
            canvas.Invalidate();
        }

        private void LayoutFooterBits(Panel btnRefresh, Panel btnTidy)
        {

            LayoutFooterRow(btnRefresh, btnTidy);
            status.Left = btnTidy.Right + 12;
            status.Top = (Footer.ClientSize.Height - status.Height) / 2;
            status.Width = Math.Max(40, FooterRightLimit - status.Left - DafyFrame.FooterGap);
            status.Invalidate();
        }

        private void Tidy()
        {
            foreach (var n in nodes.Values) n.Pinned = false;
            Relayout();
            canvas.Invalidate();
        }

        private void Relayout()
        {
            int w = canvas.ClientSize.Width, h = canvas.ClientSize.Height;
            if (w < 10 || h < 10) return;
            int cap = 0; foreach (var n in nodes.Values) if (!n.IsLocal) cap++;
            if (cap < 1) cap = 1;
            int cols = Math.Max(4, Math.Min(12, (int)Math.Round(Math.Sqrt(cap * 1.7))));
            int perCol = (int)Math.Ceiling(cap / (double)cols);
            float mL = 64f, mR = 84f, mT = 26f, mB = 42f;
            float colStep = (w - mL - mR) / Math.Max(1, cols - 1);
            float rowStep = (h - mT - mB) / Math.Max(1, perCol > 1 ? perCol - 1 : 1);
            float bottom = h - mB;
            foreach (var n in nodes.Values)
            {
                if (n.IsLocal || n.Pinned) continue;
                int slot = (int)Math.Round(n.Angle / GOLDEN);
                int col = slot / perCol, row = slot % perCol;
                if (col >= cols) { col = slot % cols; row = (slot / cols) % perCol; }
                n.X = mL + col * colStep;
                n.Y = bottom - row * rowStep;
            }

            int lc = cols / 2;
            local.X = mL + lc * colStep;
            local.Y = bottom;
        }

        private void Poll()
        {
            try
            {
                var props = IPGlobalProperties.GetIPGlobalProperties();
                var conns = props.GetActiveTcpConnections();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var agg = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
                int listeners = 0, total = 0;

                foreach (var c in conns)
                {
                    var r = c.RemoteEndPoint;

                    if (r == null || r.Port == 0 ||
                        r.Address.Equals(IPAddress.Any) || r.Address.Equals(IPAddress.IPv6Any))
                    { listeners++; continue; }

                    total++;
                    string key = r.Address.ToString();
                    Node n;
                    if (!agg.TryGetValue(key, out n)) { n = new Node { Key = key }; agg[key] = n; }
                    n.Count++;
                    int cur; n.States.TryGetValue(c.State, out cur); n.States[c.State] = cur + 1;
                    seen.Add(key);
                }

                foreach (var kv in agg)
                {
                    Node existing;
                    if (nodes.TryGetValue(kv.Key, out existing) && !existing.IsLocal)
                    {
                        existing.Count  = kv.Value.Count;
                        existing.States = kv.Value.States;
                        existing.Tone   = ToneFor(kv.Value.States);
                    }
                    else
                    {
                        var n = kv.Value;
                        n.Angle = (slotSeq++) * GOLDEN;
                        n.Tone = ToneFor(n.States);
                        nodes[kv.Key] = n;
                    }
                }

                var gone = nodes.Keys.Where(k => k != local.Key && !seen.Contains(k)).ToList();
                foreach (var k in gone) nodes.Remove(k);

                Relayout();
                canvas.Invalidate();

                statusText = (nodes.Count - 1) + " hosts · " + total + " conns · "
                           + listeners + " listening   (TCP, read-only)";
                eventsCount = total;
                alertsCount = nodes.Values.Count(n => !n.IsLocal
                    && (n.States.ContainsKey(TcpState.SynSent) || n.States.ContainsKey(TcpState.SynReceived)));
                header?.Invalidate();
                status.Invalidate();
            }
            catch (Exception ex)
            {
                statusText = "read failed: " + ex.Message;
                status.Invalidate();
            }
        }

        private static Color ToneFor(Dictionary<TcpState, int> st)
        {

            if (st.ContainsKey(TcpState.Established)) return Color.FromArgb(0xFC, 0xFC, 0xFC);
            if (st.ContainsKey(TcpState.SynSent) || st.ContainsKey(TcpState.SynReceived))
                return Color.FromArgb(0xB4, 0xDC, 0xBA);
            return Color.FromArgb(0x7F, 0xAE, 0x86);
        }

        private void Header_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int w = header.ClientSize.Width, hh = header.ClientSize.Height;
            using (var bg = new SolidBrush(Color.White)) g.FillRectangle(bg, 0, 0, w, hh);
            using (var sep = new Pen(Color.FromArgb(0x80, 0x80, 0x80))) g.DrawLine(sep, 0, hh - 1, w, hh - 1);

            TextRenderer.DrawText(g, "Network map", headerFont, new Point(8, 0),
                Color.Black, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
                    | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping);

            string xyz = "X=" + (radarX < 0 ? "-" : "+") + Math.Abs(radarX).ToString("0000")
                       + " Y=" + (radarY < 0 ? "-" : "+") + Math.Abs(radarY).ToString("0000") + " Z=01";
            Size xz = TextRenderer.MeasureText(g, xyz, headerFont);
            Size tw = TextRenderer.MeasureText(g, "Network map", headerFont);
            var patch = new Rectangle(8 + tw.Width + 18, 2, xz.Width + 12, hh - 5);
            using (var bb = new SolidBrush(Color.Black)) g.FillRectangle(bb, patch);
            TextRenderer.DrawText(g, xyz, headerFont, patch, Color.FromArgb(0xF7, 0xFF, 0x3A),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            string right = "01/20/93  12:23  Events " + eventsCount + "  Alerts " + alertsCount;
            Size rz = TextRenderer.MeasureText(g, right, headerFont);
            TextRenderer.DrawText(g, right, headerFont, new Point(w - rz.Width - 8, 0),
                Color.Black, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = canvas.ClientSize.Width, h = canvas.ClientSize.Height;

            var prevSm = g.SmoothingMode; g.SmoothingMode = SmoothingMode.None;
            using (var bpen = new Pen(Color.FromArgb(0xFC, 0xFC, 0xFC)))
                g.DrawRectangle(bpen, 5, 5, Math.Max(1, w - 11), Math.Max(1, h - 11));
            g.SmoothingMode = prevSm;

            var list = nodes.Values.ToList();

            foreach (var a in list)
            {
                Node best = null; double bd = double.MaxValue;
                foreach (var b in list)
                {
                    if (b == a) continue;
                    double d = (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
                    if (d < bd) { bd = d; best = b; }
                }
                if (best == null) continue;
                bool hot = (a.Key == hoverKey || best.Key == hoverKey);
                Color col = hot ? Color.FromArgb(0xCF, 0xFF, 0xDF) : Color.FromArgb(150, 0x7F, 0xAE, 0x86);

                float dx = best.X - a.X, dy = best.Y - a.Y;
                float dlen = Math.Min(Math.Abs(dx), Math.Abs(dy));
                float mx = a.X + Math.Sign(dx) * dlen, my = a.Y + Math.Sign(dy) * dlen;
                var route = new[] { new PointF(a.X, a.Y), new PointF(mx, my), new PointF(best.X, best.Y) };
                using (var pen = new Pen(col, hot ? 2f : 1f))
                    g.DrawLines(pen, route);
            }

            foreach (var n in list)
            {
                bool hot = (n.Key == hoverKey);
                bool active = n.States.ContainsKey(TcpState.Established);
                Color c = active ? Color.FromArgb(0xFC, 0xFC, 0xFC) : (n.IsLocal ? Green : n.Tone);
                float s = 4f + Math.Min(5, n.Count);
                using (var fill = new SolidBrush(c))
                    g.FillRectangle(fill, n.X - s, n.Y - s, s * 2, s * 2);
                if (hot)
                    using (var p = new Pen(Color.White, 1.5f))
                        g.DrawRectangle(p, n.X - s - 2, n.Y - s - 2, s * 2 + 4, s * 2 + 4);
                using (var tb = new SolidBrush(hot ? Color.White : c))
                    g.DrawString(n.IsLocal ? "LOCAL" : n.Key, n.IsLocal ? localFont : nodeFont,
                        tb, n.X + s + 3, n.Y - 7);
            }

            using (var hb = new SolidBrush(Color.FromArgb(0x2C, 0x6A, 0x40)))
                g.DrawString("observation · distributed view · no capture", hudFont, hb, 8, h - 16);

            if (hoverKey != null && nodes.ContainsKey(hoverKey) && hoverKey != local.Key)
                DrawDetail(g, nodes[hoverKey], w, h);
        }

        private void DrawDetail(Graphics g, Node n, int w, int h)
        {
            var lines = new List<string> { n.Key, n.Count + (n.Count == 1 ? " connection" : " connections") };
            foreach (var kv in n.States.OrderByDescending(k => k.Value))
                lines.Add("  " + kv.Key + " x" + kv.Value);

            int bw = 0, bh = 8;
            foreach (var ln in lines)
            {
                var sz = g.MeasureString(ln, detailFont);
                bw = Math.Max(bw, (int)sz.Width);
                bh += (int)sz.Height;
            }
            bw += 14;
            int bx = mouse.X + 14, by = mouse.Y + 12;
            if (bx + bw > w) bx = w - bw - 4;
            if (by + bh > h) by = h - bh - 4;
            if (bx < 2) bx = 2; if (by < 2) by = 2;

            using (var bg = new SolidBrush(Color.FromArgb(0xEA, 0x02, 0x12, 0x09)))
                g.FillRectangle(bg, bx, by, bw, bh);
            using (var bp = new Pen(Green))
                g.DrawRectangle(bp, bx, by, bw, bh);

            float yy = by + 4; bool first = true;
            foreach (var ln in lines)
            {
                using (var tb = new SolidBrush(first ? Color.FromArgb(0xCF, 0xFF, 0xDF) : Green))
                    g.DrawString(ln, detailFont, tb, bx + 6, yy);
                yy += g.MeasureString(ln, detailFont).Height; first = false;
            }
        }

        private string HitTest(Point p)
        {
            string best = null; float bestD = float.MaxValue;
            foreach (var n in nodes.Values)
            {
                if (n.IsLocal) continue;
                float r = 4f + Math.Min(8, n.Count) + 5f;
                float dx = p.X - n.X, dy = p.Y - n.Y;
                float d = dx * dx + dy * dy;
                if (d <= r * r && d < bestD) { bestD = d; best = n.Key; }
            }
            return best;
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            mouse = e.Location;
            int cwid = Math.Max(1, canvas.ClientSize.Width), chei = Math.Max(1, canvas.ClientSize.Height);
            radarX = (int)Math.Round((e.X / (float)cwid - 0.5f) * 400);
            radarY = (int)Math.Round((0.5f - e.Y / (float)chei) * 200);
            header.Invalidate();
            if (dragKey != null && nodes.ContainsKey(dragKey))
            {
                var n = nodes[dragKey];
                n.X = e.X; n.Y = e.Y; n.Pinned = true;
                canvas.Invalidate();
                return;
            }
            string near = HitTest(e.Location);
            if (near != hoverKey) { hoverKey = near; canvas.Invalidate(); }
        }

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            string near = HitTest(e.Location);
            if (near != null) dragKey = near;
        }
    }
}
