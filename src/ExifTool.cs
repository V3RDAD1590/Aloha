using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Aloha
{
    public sealed class ExifTag
    {
        public string Group, Name, Value;
        public bool Risk;
        public override string ToString() { return Group + " · " + Name + " = " + Value; }
    }

    public sealed class ExifReport
    {
        public string Format = "?";
        public readonly List<ExifTag> Tags = new List<ExifTag>();
        public double? Lat, Lon;
        public long MetaBytes;
        public string Error;
        public bool CanStrip;
    }

    public static class ExifTool
    {
        public static bool Supported(string path)
        {
            string e = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            return e == ".jpg" || e == ".jpeg" || e == ".jfif" || e == ".jpe" || e == ".png" || e == ".webp" || e == ".tif" || e == ".tiff";
        }

        public static ExifReport Read(string path)
        {
            var r = new ExifReport();
            byte[] d;
            try { d = File.ReadAllBytes(path); } catch (Exception ex) { r.Error = ex.Message; return r; }
            try
            {
                if (d.Length > 3 && d[0] == 0xFF && d[1] == 0xD8) { r.Format = "JPEG"; r.CanStrip = true; ReadJpeg(d, r); }
                else if (d.Length > 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) { r.Format = "PNG"; r.CanStrip = true; ReadPng(d, r); }
                else if (d.Length > 12 && Ascii(d, 0, 4) == "RIFF" && Ascii(d, 8, 4) == "WEBP") { r.Format = "WebP"; r.CanStrip = true; ReadWebp(d, r); }
                else if (d.Length > 8 && ((d[0] == 0x49 && d[1] == 0x49) || (d[0] == 0x4D && d[1] == 0x4D))) { r.Format = "TIFF"; ReadTiff(d, 0, d.Length, r); }
                else r.Error = "not a JPEG / PNG / WebP / TIFF";
            }
            catch (Exception ex) { r.Error = "parse stopped: " + ex.Message; }
            return r;
        }

        private static void ReadJpeg(byte[] d, ExifReport r)
        {
            int p = 2;
            while (p + 4 <= d.Length && d[p] == 0xFF)
            {
                byte m = d[p + 1];
                if (m == 0xD8 || (m >= 0xD0 && m <= 0xD7) || m == 0x01) { p += 2; continue; }
                if (m == 0xDA || m == 0xD9) break;
                int len = (d[p + 2] << 8) | d[p + 3];
                int body = p + 4, blen = len - 2;
                if (body + blen > d.Length || len < 2) break;
                if (m == 0xE1 && blen > 6 && Ascii(d, body, 4) == "Exif") { r.MetaBytes += len + 2; ReadTiff(d, body + 6, blen - 6, r); }
                else if (m == 0xE1 && Ascii(d, body, Math.Min(blen, 28)).StartsWith("http://ns.adobe.com/xap/1.0/", StringComparison.Ordinal))
                { r.MetaBytes += len + 2; Xmp(Encoding.UTF8.GetString(d, body + 29, Math.Max(0, blen - 29)), r); }
                else if (m == 0xED) { r.MetaBytes += len + 2; Add(r, "IPTC", "Photoshop / IPTC block", blen + " bytes (captions, keywords, credits)", true); }
                else if (m == 0xFE) { r.MetaBytes += len + 2; Add(r, "JPEG", "Comment", Clean(Encoding.UTF8.GetString(d, body, blen)), true); }
                else if (m == 0xE2 && Ascii(d, body, Math.Min(blen, 11)) == "ICC_PROFILE") Add(r, "JPEG", "ICC colour profile", blen + " bytes (kept by strip)", false);
                p = body + blen;
            }
        }

        private static void ReadPng(byte[] d, ExifReport r)
        {
            int p = 8;
            while (p + 12 <= d.Length)
            {
                int len = BE32(d, p); string type = Ascii(d, p + 4, 4); int body = p + 8;
                if (len < 0 || body + len + 4 > d.Length) break;
                switch (type)
                {
                    case "eXIf": r.MetaBytes += len + 12; ReadTiff(d, body, len, r); break;
                    case "tEXt":
                    {
                        int z = Array.IndexOf(d, (byte)0, body, len); if (z < 0) z = body + len;
                        string k = Latin(d, body, z - body), v = z < body + len ? Latin(d, z + 1, body + len - z - 1) : "";
                        r.MetaBytes += len + 12;
                        if (k == "XML:com.adobe.xmp") Xmp(v, r); else Add(r, "PNG text", k, Clean(v), true);
                        break;
                    }
                    case "iTXt":
                    {
                        int z = Array.IndexOf(d, (byte)0, body, len); string k = z > 0 ? Latin(d, body, z - body) : "?";
                        r.MetaBytes += len + 12;

                        int q = z + 3, z2 = Array.IndexOf(d, (byte)0, q, body + len - q), z3 = z2 >= 0 ? Array.IndexOf(d, (byte)0, z2 + 1, body + len - z2 - 1) : -1;
                        bool compressed = z + 1 < d.Length && d[z + 1] == 1;
                        string v = (!compressed && z3 >= 0) ? Encoding.UTF8.GetString(d, z3 + 1, body + len - z3 - 1) : "(" + len + " bytes" + (compressed ? ", compressed" : "") + ")";
                        if (k == "XML:com.adobe.xmp" && !compressed) Xmp(v, r); else Add(r, "PNG text", k, Clean(v), true);
                        break;
                    }
                    case "zTXt":
                    {
                        int z = Array.IndexOf(d, (byte)0, body, len);
                        r.MetaBytes += len + 12;
                        Add(r, "PNG text", z > 0 ? Latin(d, body, z - body) : "?", "(" + len + " bytes, compressed)", true);
                        break;
                    }
                    case "tIME":
                        r.MetaBytes += len + 12;
                        if (len >= 7) Add(r, "PNG", "Last modified", string.Format("{0:0000}-{1:00}-{2:00} {3:00}:{4:00}:{5:00}", (d[body] << 8) | d[body + 1], d[body + 2], d[body + 3], d[body + 4], d[body + 5], d[body + 6]), false);
                        break;
                    case "IEND": return;
                }
                p = body + len + 4;
            }
        }

        private static void ReadWebp(byte[] d, ExifReport r)
        {
            int p = 12;
            while (p + 8 <= d.Length)
            {
                string id = Ascii(d, p, 4); int len = LE32(d, p + 4), body = p + 8;
                if (len < 0 || body + len > d.Length) break;
                if (id == "EXIF")
                {
                    r.MetaBytes += 8 + len + (len & 1);
                    int off = (len > 6 && Ascii(d, body, 4) == "Exif") ? 6 : 0;    
                    ReadTiff(d, body + off, len - off, r);
                }
                else if (id == "XMP ") { r.MetaBytes += 8 + len + (len & 1); Xmp(Encoding.UTF8.GetString(d, body, len), r); }
                p = body + len + (len & 1);
            }
        }

        private static readonly Dictionary<int, string> Ifd0 = new Dictionary<int, string>
        {
            { 0x010E, "Description" }, { 0x010F, "Camera make" }, { 0x0110, "Camera model" }, { 0x0112, "Orientation" },
            { 0x011A, "X resolution" }, { 0x011B, "Y resolution" }, { 0x0131, "Software" }, { 0x0132, "Modified" },
            { 0x013B, "Artist" }, { 0x8298, "Copyright" }, { 0x9C9B, "Title (Windows)" }, { 0x9C9C, "Comment (Windows)" },
            { 0x9C9D, "Author (Windows)" }, { 0x9C9E, "Keywords (Windows)" }, { 0x9C9F, "Subject (Windows)" }
        };
        private static readonly Dictionary<int, string> ExifIfd = new Dictionary<int, string>
        {
            { 0x829A, "Exposure" }, { 0x829D, "F-number" }, { 0x8827, "ISO" }, { 0x9003, "Taken" }, { 0x9004, "Digitized" },
            { 0x9010, "Time zone" }, { 0x9011, "Time zone (taken)" }, { 0x920A, "Focal length" }, { 0x9209, "Flash" },
            { 0x927C, "Maker note" }, { 0x9286, "User comment" }, { 0xA002, "Width" }, { 0xA003, "Height" },
            { 0xA420, "Image unique ID" }, { 0xA430, "Camera owner" }, { 0xA431, "Body serial no." }, { 0xA432, "Lens spec" },
            { 0xA433, "Lens make" }, { 0xA434, "Lens model" }, { 0xA435, "Lens serial no." }, { 0xA405, "Focal length (35mm)" }
        };
        private static readonly HashSet<int> RiskTags = new HashSet<int> { 0x013B, 0x8298, 0x9C9D, 0x9C9C, 0x9286, 0xA420, 0xA430, 0xA431, 0xA435, 0x927C, 0x010E };

        private static void ReadTiff(byte[] d, int b, int len, ExifReport r)
        {
            if (len < 8) return;
            bool le = d[b] == 0x49;
            Func<int, int> u16 = o => le ? d[b + o] | (d[b + o + 1] << 8) : (d[b + o] << 8) | d[b + o + 1];
            Func<int, uint> u32 = o => le ? (uint)(d[b + o] | (d[b + o + 1] << 8) | (d[b + o + 2] << 16) | (d[b + o + 3] << 24))
                                          : (uint)((d[b + o] << 24) | (d[b + o + 1] << 16) | (d[b + o + 2] << 8) | d[b + o + 3]);
            int ifd0 = (int)u32(4);
            int exifPtr = -1, gpsPtr = -1;
            Walk(d, b, len, ifd0, u16, u32, "EXIF", Ifd0, r, (tag, val) => { if (tag == 0x8769) exifPtr = (int)val; if (tag == 0x8825) gpsPtr = (int)val; });
            if (exifPtr > 0) Walk(d, b, len, exifPtr, u16, u32, "EXIF", ExifIfd, r, null);
            if (gpsPtr > 0) ReadGps(d, b, len, gpsPtr, u16, u32, r);
        }

        private static void Walk(byte[] d, int b, int len, int ifd, Func<int, int> u16, Func<int, uint> u32,
                                 string group, Dictionary<int, string> names, ExifReport r, Action<int, uint> pointer)
        {
            if (ifd <= 0 || ifd + 2 > len) return;
            int n = u16(ifd);
            for (int i = 0; i < n && ifd + 2 + i * 12 + 12 <= len; i++)
            {
                int e = ifd + 2 + i * 12, tag = u16(e), type = u16(e + 2); uint count = u32(e + 4);
                if (tag == 0x8769 || tag == 0x8825) { if (pointer != null) pointer(tag, u32(e + 8)); continue; }
                string name;
                if (!names.TryGetValue(tag, out name)) continue;
                string v = Value(d, b, len, e, type, count, u16, u32, tag);
                if (v == null) continue;
                if (tag == 0x0112) v = Orientation(v);
                Add(r, group, name, v, RiskTags.Contains(tag));
            }
        }

        private static void ReadGps(byte[] d, int b, int len, int ifd, Func<int, int> u16, Func<int, uint> u32, ExifReport r)
        {
            if (ifd + 2 > len) return;
            int n = u16(ifd); string latRef = "N", lonRef = "E"; double[] lat = null, lon = null; string alt = null, date = null, time = null;
            for (int i = 0; i < n && ifd + 2 + i * 12 + 12 <= len; i++)
            {
                int e = ifd + 2 + i * 12, tag = u16(e), type = u16(e + 2); uint count = u32(e + 4);
                int off = (int)u32(e + 8);
                switch (tag)
                {
                    case 1: latRef = ((char)d[b + e + 8]).ToString(); break;
                    case 3: lonRef = ((char)d[b + e + 8]).ToString(); break;
                    case 2: lat = Rationals(d, b, len, off, 3, u32); break;
                    case 4: lon = Rationals(d, b, len, off, 3, u32); break;
                    case 6: { var a = Rationals(d, b, len, off, 1, u32); if (a != null) alt = a[0].ToString("0.#") + " m"; break; }
                    case 7: { var t = Rationals(d, b, len, off, 3, u32); if (t != null) time = string.Format("{0:00}:{1:00}:{2:00} UTC", t[0], t[1], Math.Floor(t[2])); break; }
                    case 29: date = Value(d, b, len, e, type, count, u16, u32, tag); break;
                }
            }
            if (lat != null && lon != null)
            {
                double la = (lat[0] + lat[1] / 60 + lat[2] / 3600) * (latRef == "S" ? -1 : 1);
                double lo = (lon[0] + lon[1] / 60 + lon[2] / 3600) * (lonRef == "W" ? -1 : 1);
                if (!double.IsNaN(la) && !double.IsNaN(lo) && Math.Abs(la) <= 90 && Math.Abs(lo) <= 180)
                {
                    r.Lat = la; r.Lon = lo;
                    Add(r, "GPS", "Location", la.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture) + ", " + lo.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture), true);
                }
            }
            if (alt != null) Add(r, "GPS", "Altitude", alt, true);
            if (date != null || time != null) Add(r, "GPS", "Fix time", ((date ?? "") + " " + (time ?? "")).Trim(), true);
        }

        private static double[] Rationals(byte[] d, int b, int len, int off, int n, Func<int, uint> u32)
        {
            if (off < 0 || off + n * 8 > len) return null;
            var a = new double[n];
            for (int i = 0; i < n; i++) { uint num = u32(off + i * 8), den = u32(off + i * 8 + 4); a[i] = den == 0 ? 0 : (double)num / den; }
            return a;
        }

        private static string Value(byte[] d, int b, int len, int e, int type, uint count, Func<int, int> u16, Func<int, uint> u32, int tag)
        {
            int size = (type == 3 || type == 8) ? 2 : (type == 4 || type == 9 || type == 11) ? 4 : (type == 5 || type == 10 || type == 12) ? 8 : 1;
            long total = (long)size * count;
            if (total <= 0 || total > len) return null;
            int at = total <= 4 ? e + 8 : (int)u32(e + 8);
            if (at < 0 || at + total > len) return null;
            switch (type)
            {
                case 2: return Clean(Encoding.UTF8.GetString(d, b + at, (int)total).TrimEnd('\0'));
                case 3: case 8: return u16(at).ToString();
                case 11: case 12:
                {

                    var raw = new byte[size]; Array.Copy(d, b + at, raw, 0, size);
                    bool le = d[b] == 0x49; if (le != BitConverter.IsLittleEndian) Array.Reverse(raw);
                    double v = size == 4 ? BitConverter.ToSingle(raw, 0) : BitConverter.ToDouble(raw, 0);
                    if (tag == 0x829A && v > 0 && v < 1) return "1/" + Math.Round(1 / v) + " s";
                    if (tag == 0x829D) return "f/" + v.ToString("0.0");
                    if (tag == 0x920A) return v.ToString("0.#") + " mm";
                    return v.ToString("0.###");
                }
                case 4: case 9: return u32(at).ToString();
                case 5: case 10:
                {
                    uint num = u32(at), den = u32(at + 4);
                    if (den == 0) return "0";
                    if (tag == 0x829A && num < den) return "1/" + Math.Round((double)den / num) + " s";
                    double v = (double)num / den;
                    if (tag == 0x829D) return "f/" + v.ToString("0.0");
                    if (tag == 0x920A) return v.ToString("0.#") + " mm";
                    return v.ToString("0.###");
                }
                default:
                    if (tag >= 0x9C9B && tag <= 0x9C9F) return Clean(Encoding.Unicode.GetString(d, b + at, (int)total).TrimEnd('\0'));
                    if (tag == 0x9286 && total > 8) return Clean(Encoding.ASCII.GetString(d, b + at + 8, (int)total - 8).TrimEnd('\0', ' '));
                    return total + " bytes";
            }
        }

        private static string Orientation(string v)
        {
            switch (v) { case "1": return "normal"; case "3": return "rotated 180°"; case "6": return "rotated 90° CW"; case "8": return "rotated 90° CCW"; default: return v; }
        }

        private static void Xmp(string xml, ExifReport r)
        {
            Add(r, "XMP", "XMP packet", xml.Length + " chars", true);
            foreach (var key in new[] { "dc:creator", "xmp:CreatorTool", "photoshop:City", "photoshop:Country", "exif:GPSLatitude", "Iptc4xmpCore:Location", "dc:rights" })
            {
                int i = xml.IndexOf(key, StringComparison.Ordinal); if (i < 0) continue;
                int q = xml.IndexOf('"', i), q2 = q >= 0 ? xml.IndexOf('"', q + 1) : -1;
                int gt = xml.IndexOf('>', i), lt = gt >= 0 ? xml.IndexOf('<', gt) : -1;
                string v = (q > 0 && q2 > q && q - i <= key.Length + 2) ? xml.Substring(q + 1, q2 - q - 1)
                         : (gt > 0 && lt > gt) ? xml.Substring(gt + 1, lt - gt - 1) : null;
                if (!string.IsNullOrWhiteSpace(v)) Add(r, "XMP", key, Clean(v), true);
            }
        }

        public static long Strip(string src, string dst)
        {
            byte[] d = File.ReadAllBytes(src);
            byte[] o;
            if (d.Length > 3 && d[0] == 0xFF && d[1] == 0xD8) o = StripJpeg(d);
            else if (d.Length > 8 && d[0] == 0x89 && d[1] == 0x50) o = StripPng(d);
            else if (d.Length > 12 && Ascii(d, 0, 4) == "RIFF" && Ascii(d, 8, 4) == "WEBP") o = StripWebp(d);
            else throw new NotSupportedException("only JPEG, PNG and WebP can be stripped");
            File.WriteAllBytes(dst, o);
            return d.Length - o.Length;
        }

        private static byte[] StripJpeg(byte[] d)
        {
            var ms = new MemoryStream(d.Length);
            ms.Write(d, 0, 2);
            int p = 2;
            while (p + 4 <= d.Length && d[p] == 0xFF)
            {
                byte m = d[p + 1];
                if (m == 0xDA) break;
                int len = (d[p + 2] << 8) | d[p + 3], body = p + 4;
                if (len < 2 || p + 2 + len > d.Length) break;

                bool drop = m == 0xE1 || m == 0xED || m == 0xFE || (m >= 0xE3 && m <= 0xEC) || m == 0xEF;
                if (!drop) ms.Write(d, p, len + 2);
                p += len + 2;
            }
            ms.Write(d, p, d.Length - p);
            return ms.ToArray();
        }

        private static readonly HashSet<string> PngDrop = new HashSet<string> { "eXIf", "tEXt", "iTXt", "zTXt", "tIME" };
        private static byte[] StripPng(byte[] d)
        {
            var ms = new MemoryStream(d.Length);
            ms.Write(d, 0, 8);
            int p = 8;
            while (p + 12 <= d.Length)
            {
                int len = BE32(d, p); string type = Ascii(d, p + 4, 4);
                if (len < 0 || p + 12 + len > d.Length) { ms.Write(d, p, d.Length - p); break; }
                if (!PngDrop.Contains(type)) ms.Write(d, p, len + 12);
                p += len + 12;
                if (type == "IEND") break;
            }
            return ms.ToArray();
        }

        private static byte[] StripWebp(byte[] d)
        {
            var ms = new MemoryStream(d.Length);
            ms.Write(d, 0, 12);
            int p = 12, vp8x = -1;
            while (p + 8 <= d.Length)
            {
                string id = Ascii(d, p, 4); int len = LE32(d, p + 4), full = 8 + len + (len & 1);
                if (len < 0 || p + full > d.Length) { ms.Write(d, p, d.Length - p); break; }
                if (id == "VP8X") vp8x = (int)ms.Position;
                if (id != "EXIF" && id != "XMP ") ms.Write(d, p, full);
                p += full;
            }
            var o = ms.ToArray();
            if (vp8x >= 0 && vp8x + 8 < o.Length) o[vp8x + 8] &= unchecked((byte)~(0x08 | 0x04));
            int riff = o.Length - 8; o[4] = (byte)riff; o[5] = (byte)(riff >> 8); o[6] = (byte)(riff >> 16); o[7] = (byte)(riff >> 24);
            return o;
        }

        private static void Add(ExifReport r, string g, string n, string v, bool risk) { r.Tags.Add(new ExifTag { Group = g, Name = n, Value = v, Risk = risk }); }
        private static string Ascii(byte[] d, int at, int n) { return at + n <= d.Length ? Encoding.ASCII.GetString(d, at, n) : ""; }
        private static string Latin(byte[] d, int at, int n) { return n > 0 && at + n <= d.Length ? Encoding.GetEncoding(28591).GetString(d, at, n) : ""; }
        private static int BE32(byte[] d, int p) { return (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3]; }
        private static int LE32(byte[] d, int p) { return d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) | (d[p + 3] << 24); }
        private static string Clean(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(); foreach (char c in s) sb.Append(c < 32 ? ' ' : c);
            string t = sb.ToString().Trim();
            return t.Length > 300 ? t.Substring(0, 300) + "…" : t;
        }
    }
}
