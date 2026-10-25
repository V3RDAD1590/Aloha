using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Aloha
{

    public class SdrWaterfallWindow : DafyFrame
    {
        private static readonly Color Fab  = Color.FromArgb(0xFA, 0xFA, 0xFB);
        private static readonly Color Ink  = Color.FromArgb(0x20, 0x20, 0x20);
        private static readonly Color Lime = Color.FromArgb(0x2e, 0xe0, 0x5a);
        private const int TOP_H = 32, ZOOM_MAX = 14;

        private static readonly byte[] JR = new byte[256], JG = new byte[256], JB = new byte[256];
        static SdrWaterfallWindow()
        {
            for (int i = 0; i < 256; i++)
            {
                double v = i / 255.0;
                JR[i] = (byte)(255 * Clamp(1.5 - Math.Abs(4 * v - 3)));
                JG[i] = (byte)(255 * Clamp(1.5 - Math.Abs(4 * v - 2)));
                JB[i] = (byte)(255 * Clamp(1.5 - Math.Abs(4 * v - 1)));
            }
        }
        private static double Clamp(double x) { return x < 0 ? 0 : (x > 1 ? 1 : x); }

        private readonly string host;
        private readonly Action<string> openInTab;
        private KiwiWaterfallClient wf;
        private int zoom;

        private Panel canvas;
        private Label status, zoomLbl;

        private readonly object wfLock = new object();
        private byte[] pix;
        private Bitmap bmp;
        private int bw, bh;
        private bool dirty, live;

        public SdrWaterfallWindow(string host, Action<string> openInTab = null, int zoom = 0)
            : base("SDR-WF", "SDR — waterfall  " + ShortHost(host))
        {
            this.host = host;
            this.openInTab = openInTab;
            this.zoom = Math.Max(0, Math.Min(ZOOM_MAX, zoom));
            Size = new Size(1100, 520);
            MinimumSize = new Size(600, 260);
            ClientArea.BackColor = Color.Black;

            BuildCanvas();
            BuildTop();

            Shown += (s, e) => Connect();
            FormClosed += (s, e) =>
            {
                Disconnect();
                lock (wfLock) { bmp?.Dispose(); bmp = null; pix = null; }
            };
        }

        private static string ShortHost(string h)
        {
            if (string.IsNullOrEmpty(h)) return "";
            h = h.Replace("http://", "").Replace("https://", "");
            int s = h.IndexOf('/'); if (s >= 0) h = h.Substring(0, s);
            return h;
        }

        private void BuildTop()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = TOP_H, BackColor = Fab };

            var lblHost = new Label { Left = 8, Top = 8, AutoSize = true, Text = ShortHost(host), ForeColor = Ink, BackColor = Color.Transparent };

            var minus = new Button { Left = 300, Top = 4, Width = 28, Text = "−" };
            zoomLbl = new Label { Left = 330, Top = 8, Width = 56, Text = "zoom " + zoom, ForeColor = Ink, BackColor = Color.Transparent };
            var plus  = new Button { Left = 388, Top = 4, Width = 28, Text = "+" };
            minus.Click += (s, e) => SetZoom(zoom - 1);
            plus.Click  += (s, e) => SetZoom(zoom + 1);

            var reconnect = new Button { Left = 426, Top = 4, Width = 82, Text = "reconnect" };
            reconnect.Click += (s, e) => Connect();

            var web = new Button { Left = 514, Top = 4, Width = 58, Text = "web ▸", Enabled = openInTab != null };
            web.Click += (s, e) => { try { openInTab?.Invoke(WebUrl()); } catch { } };

            status = new Label { Left = 584, Top = 8, AutoSize = true, Text = "", ForeColor = Ink, BackColor = Color.Transparent };

            top.Controls.Add(lblHost); top.Controls.Add(minus); top.Controls.Add(zoomLbl); top.Controls.Add(plus);
            top.Controls.Add(reconnect); top.Controls.Add(web); top.Controls.Add(status);
            ClientArea.Controls.Add(top);
        }

        private string WebUrl()
        {
            string h = host ?? "";
            if (!h.StartsWith("http://") && !h.StartsWith("https://")) h = "http://" + h;
            return h;
        }

        private void BuildCanvas()
        {
            canvas = new Panel { BackColor = Color.Black, Dock = DockStyle.Fill };
            typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(canvas, true, null);
            canvas.Paint += Canvas_Paint;
            canvas.Resize += (s, e) => { lock (wfLock) { if (bw > 0) EnsureHistory(bw, Math.Max(1, canvas.ClientSize.Height)); } canvas.Invalidate(); };
            ClientArea.Controls.Add(canvas);
        }

        private void Connect()
        {
            Disconnect();
            lock (wfLock) { if (pix != null) Array.Clear(pix, 0, pix.Length); dirty = true; live = false; }
            SetStatus("connecting …");

            wf = new KiwiWaterfallClient();
            wf.Status += msg => SetStatus(msg);
            wf.FrameReceived += OnFrame;
            wf.Start(host, zoom);
            canvas.Invalidate();
        }

        private void Disconnect()
        {
            var old = wf; wf = null;
            if (old != null)
            {
                try { old.FrameReceived -= OnFrame; } catch { }
                try { old.Stop(); } catch { }
            }
        }

        private void SetZoom(int z)
        {
            z = Math.Max(0, Math.Min(ZOOM_MAX, z));
            if (z == zoom) return;
            zoom = z;
            zoomLbl.Text = "zoom " + zoom;
            Connect();
        }

        private void SetStatus(string s)
        {
            try
            {
                if (status == null) return;
                if (status.InvokeRequired) status.BeginInvoke((Action)(() => status.Text = s));
                else status.Text = s;
            }
            catch { }
        }

        private void OnFrame(byte[] bins)
        {
            if (bins == null || bins.Length == 0) return;
            lock (wfLock)
            {
                int h = Math.Max(1, canvas.ClientSize.Height);
                EnsureHistory(bins.Length, h);

                int stride = bw * 4;

                Buffer.BlockCopy(pix, stride, pix, 0, stride * (bh - 1));

                int o = stride * (bh - 1);
                int n = Math.Min(bins.Length, bw);
                for (int x = 0; x < n; x++)
                {
                    byte v = bins[x];
                    pix[o] = JB[v]; pix[o + 1] = JG[v]; pix[o + 2] = JR[v]; pix[o + 3] = 255;
                    o += 4;
                }
                dirty = true; live = true;
            }
            try { if (canvas != null && canvas.IsHandleCreated) canvas.BeginInvoke((Action)(() => canvas.Invalidate())); } catch { }
        }

        private void EnsureHistory(int w, int h)
        {
            if (pix != null && bw == w && bh == h) return;
            var np = new byte[w * h * 4];
            if (pix != null && bw == w)
            {
                int keep = Math.Min(bh, h), stride = w * 4;
                Buffer.BlockCopy(pix, (bh - keep) * stride, np, (h - keep) * stride, keep * stride);
            }
            pix = np; bw = w; bh = h;
            bmp?.Dispose();
            bmp = new Bitmap(bw, bh, PixelFormat.Format32bppArgb);
            dirty = true;
        }

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.Black);

            lock (wfLock)
            {
                if (bmp != null && pix != null)
                {
                    if (dirty)
                    {
                        var data = bmp.LockBits(new Rectangle(0, 0, bw, bh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                        try
                        {
                            int stride = bw * 4;
                            if (data.Stride == stride) Marshal.Copy(pix, 0, data.Scan0, pix.Length);
                            else for (int y = 0; y < bh; y++)
                                Marshal.Copy(pix, y * stride, IntPtr.Add(data.Scan0, y * data.Stride), stride);
                        }
                        finally { bmp.UnlockBits(data); }
                        dirty = false;
                    }
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    g.DrawImage(bmp, new Rectangle(0, 0, canvas.ClientSize.Width, canvas.ClientSize.Height));
                }
            }

            using (var f = new Font("Consolas", 7.5f))
            using (var b = new SolidBrush(Lime))
                g.DrawString(live ? "waterfall (live) — zoom " + zoom : "waterfall — waiting for frames", f, b, 6, 4);
        }
    }
}
