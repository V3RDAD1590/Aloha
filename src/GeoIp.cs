using System;
using System.Collections.Generic;
using System.IO;

namespace Aloha
{

    public class GeoIp
    {
        private long[] lo = new long[0];
        private long[] hi = new long[0];
        private string[] cc = new string[0];
        private int n;

        public bool Loaded => n > 0;
        public int Count => n;

        public GeoIp() { TryLoad(); }

        private static string FindGeoip()
        {
            try
            {
                string b = AppDomain.CurrentDomain.BaseDirectory ?? ".";
                string[] c =
                {
                    Path.Combine(b, "tor", "data", "geoip"),
                    Path.Combine(b, "tor", "geoip"),
                    Path.Combine(b, "data", "geoip"),
                    Path.Combine(b, "geoip"),
                    Path.Combine(b, "tor", "tor", "data", "geoip")
                };
                foreach (var p in c) if (File.Exists(p)) return p;
            }
            catch { }
            return null;
        }

        private void TryLoad()
        {
            string path = FindGeoip();
            if (path == null) return;
            try
            {
                var L = new List<long>(); var H = new List<long>(); var C = new List<string>();
                foreach (var raw in File.ReadLines(path))
                {
                    if (raw.Length == 0 || raw[0] == '#') continue;
                    int a = raw.IndexOf(',');         if (a < 0) continue;
                    int b2 = raw.IndexOf(',', a + 1); if (b2 < 0) continue;
                    long l, h;
                    if (!long.TryParse(raw.Substring(0, a), out l)) continue;
                    if (!long.TryParse(raw.Substring(a + 1, b2 - a - 1), out h)) continue;
                    L.Add(l); H.Add(h); C.Add(raw.Substring(b2 + 1).Trim());
                }
                lo = L.ToArray(); hi = H.ToArray(); cc = C.ToArray(); n = lo.Length;
            }
            catch { n = 0; }
        }

        public string Lookup(string ipv4)
        {
            if (n == 0) return "??";
            long ip;
            if (!TryIpToLong(ipv4, out ip)) return "??";
            int a = 0, b = n - 1;
            while (a <= b)
            {
                int m = (a + b) >> 1;
                if (ip < lo[m]) b = m - 1;
                else if (ip > hi[m]) a = m + 1;
                else return cc[m];
            }
            return "??";
        }

        private static bool TryIpToLong(string ip, out long val)
        {
            val = 0;
            var parts = (ip ?? "").Split('.');
            if (parts.Length != 4) return false;
            long v = 0;
            foreach (var p in parts)
            {
                int o;
                if (!int.TryParse(p, out o) || o < 0 || o > 255) return false;
                v = (v << 8) | (uint)o;
            }
            val = v;
            return true;
        }
    }
}
