using System;
using System.Collections.Generic;

namespace Aloha.RingStoreCore
{
    public struct Vec2
    {
        public double X, Y;
        public Vec2(double x, double y) { X = x; Y = y; }
    }

    public static class BookmarkLayout
    {

        public static Vec2[] Compute(string[] clusterKeys, int iters = 400, int seed = 110)
        {
            int n = clusterKeys.Length;
            var pos = new Vec2[n];
            if (n == 0) return pos;

            var rnd = new Random(seed);
            for (int i = 0; i < n; i++) pos[i] = new Vec2(rnd.NextDouble(), rnd.NextDouble());

            const double kRep = 0.030, kAtt = 0.220;
            var cx = new Dictionary<string, double>();
            var cy = new Dictionary<string, double>();
            var cc = new Dictionary<string, int>();
            var dx = new double[n];
            var dy = new double[n];

            for (int it = 0; it < iters; it++)
            {
                double t = 1.0 - (double)it / iters;

                cx.Clear(); cy.Clear(); cc.Clear();
                for (int i = 0; i < n; i++)
                {
                    string k = clusterKeys[i];
                    if (!cc.ContainsKey(k)) { cx[k] = 0; cy[k] = 0; cc[k] = 0; }
                    cx[k] += pos[i].X; cy[k] += pos[i].Y; cc[k]++;
                }
                foreach (var kv in cc) { cx[kv.Key] /= kv.Value; cy[kv.Key] /= kv.Value; }

                Array.Clear(dx, 0, n); Array.Clear(dy, 0, n);
                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < n; j++)
                    {
                        if (i == j) continue;
                        double rx = pos[i].X - pos[j].X, ry = pos[i].Y - pos[j].Y;
                        double d2 = rx * rx + ry * ry + 1e-9;
                        double f = kRep / d2;
                        dx[i] += rx * f; dy[i] += ry * f;
                    }
                    string k = clusterKeys[i];
                    dx[i] += (cx[k] - pos[i].X) * kAtt;
                    dy[i] += (cy[k] - pos[i].Y) * kAtt;
                }
                for (int i = 0; i < n; i++)
                {
                    pos[i].X += dx[i] * t;
                    pos[i].Y += dy[i] * t;
                }
            }

            double loX = double.MaxValue, hiX = double.MinValue,
                   loY = double.MaxValue, hiY = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                if (pos[i].X < loX) loX = pos[i].X; if (pos[i].X > hiX) hiX = pos[i].X;
                if (pos[i].Y < loY) loY = pos[i].Y; if (pos[i].Y > hiY) hiY = pos[i].Y;
            }
            double sx = hiX - loX; if (sx == 0) sx = 1;
            double sy = hiY - loY; if (sy == 0) sy = 1;
            for (int i = 0; i < n; i++)
                pos[i] = new Vec2((pos[i].X - loX) / sx, (pos[i].Y - loY) / sy);
            return pos;
        }
    }
}
