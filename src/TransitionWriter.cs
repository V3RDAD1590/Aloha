using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Aloha.RingStoreCore;

namespace Aloha
{

    internal static class TransitionWriter
    {
        const int W = 680, H = 680, GX = 24, GY = 24;
        const string GLOW = "#ff2a00", HIT = "#00ff00", TXT = "#dd2000";

        private struct Frame { public string Label; public double cx, cy, w, h, x, y; public bool rect; }

        public static uint Fnv1a32(string s)
        {
            uint h = 0x811c9dc5u;
            foreach (byte b in Encoding.UTF8.GetBytes(s ?? "")) { h ^= b; h *= 0x01000193u; }
            return h;
        }

        private static bool Inside(Frame p, double px, double py)
        {
            if (p.rect) return p.x <= px && px <= p.x + p.w && p.y <= py && py <= p.y + p.h;
            double rx = p.w / 2.0, ry = p.h / 2.0;
            double dx = (px - p.cx) / rx, dy = (py - p.cy) / ry;
            return dx * dx + dy * dy <= 1.0;
        }

        private static string F1(double v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }
        private static string F2(double v) { return v.ToString("0.00", CultureInfo.InvariantCulture); }
        private static string U(uint v)    { return v.ToString(CultureInfo.InvariantCulture); }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;").Replace("'", "&#x27;");
        }

        public static string Emit(IList<string> frameLabels, string scope, string hit, uint version)
        {
            try
            {
                uint seed = Fnv1a32(scope);
                byte[] ks = RingStore.GenerateFragment(seed, version, 4096);

                var placed = new List<Frame>();
                int off = 0;
                for (int i = 0; i < frameLabels.Count; i++)
                {
                    int bx = ks[off], by = ks[off + 1], bw = ks[off + 2], bh = ks[off + 3]; off += 4;
                    double cx = 110 + (bx / 255.0) * (W - 220);
                    double cy = 110 + (by / 255.0) * (H - 220);
                    double w  = 150 + (bw / 255.0) * 210;
                    double h  = 130 + (bh / 255.0) * 190;
                    placed.Add(new Frame
                    {
                        Label = frameLabels[i], rect = (i % 2 == 0),
                        cx = cx, cy = cy, w = w, h = h, x = cx - w / 2.0, y = cy - h / 2.0
                    });
                }

                double cellw = W / (double)GX, cellh = H / (double)GY;
                var cells = new List<int[]>();
                double bestx = W / 2.0, besty = H / 2.0; int bestc = 1;
                for (int gy = 0; gy < GY; gy++)
                    for (int gx = 0; gx < GX; gx++)
                    {
                        double px = (gx + 0.5) * cellw, py = (gy + 0.5) * cellh;
                        int cnt = 0;
                        foreach (var p in placed) if (Inside(p, px, py)) cnt++;
                        if (cnt >= 2)
                        {
                            cells.Add(new[] { gx, gy, cnt });
                            if (cnt > bestc) { bestc = cnt; bestx = px; besty = py; }
                        }
                    }

                var s = new List<string>();
                s.Add("<svg width=\"100%\" viewBox=\"0 0 " + W + " " + H + "\" xmlns=\"http://www.w3.org/2000/svg\" role=\"img\">");
                s.Add("<title>transition - " + Esc(scope) + "</title>");
                s.Add("<rect x=\"0\" y=\"0\" width=\"" + W + "\" height=\"" + H + "\" fill=\"#000\"/>");

                s.Add("<g fill=\"" + GLOW + "\">");
                foreach (var c in cells)
                {
                    double op = Math.Min(0.42, 0.10 + 0.12 * (c[2] - 1));
                    s.Add("<rect x=\"" + F1(c[0] * cellw) + "\" y=\"" + F1(c[1] * cellh) +
                          "\" width=\"" + F1(cellw) + "\" height=\"" + F1(cellh) + "\" opacity=\"" + F2(op) + "\"/>");
                }
                s.Add("</g>");

                s.Add("<g fill=\"" + TXT + "\" font-family=\"monospace\" font-size=\"11\">");
                foreach (var p in placed)
                    s.Add("<text x=\"" + F1(p.x + 4) + "\" y=\"" + F1(p.y + 14) + "\">" + Esc(p.Label) + "</text>");
                s.Add("</g>");

                s.Add("<circle cx=\"" + F1(bestx) + "\" cy=\"" + F1(besty) + "\" r=\"6.5\" fill=\"" + HIT + "\"/>");
                s.Add("<g fill=\"" + HIT + "\" font-family=\"monospace\" font-size=\"12\"><text x=\"" +
                      F1(bestx + 18) + "\" y=\"" + F1(besty + 4) + "\">" + Esc(hit) + "</text></g>");

                s.Add("<g fill=\"" + TXT + "\" font-family=\"monospace\" font-size=\"12\">");
                s.Add("<text x=\"14\" y=\"22\">transition  " + Esc(scope) + "</text>");
                s.Add("<text x=\"14\" y=\"" + (H - 16) + "\">seed=" + U(seed) + "  depth=" + bestc +
                      "  frames=" + frameLabels.Count + "  (regenerates byte-identical)</text>");
                s.Add("</g>");
                s.Add("</svg>");

                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dev", "devtools", "transitions");
                Directory.CreateDirectory(dir);
                string name = "transition_" + U(seed) + "_" +
                              DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".svg";
                string path = Path.Combine(dir, name);
                File.WriteAllText(path, string.Join("\n", s), new UTF8Encoding(false));
                return path;
            }
            catch { return null; }
        }
    }
}
