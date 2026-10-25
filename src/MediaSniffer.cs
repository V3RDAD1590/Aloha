using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Aloha.Media
{

    public sealed class Meta
    {
        public string Name;
        public string Type;
        public string Ext;
        public string Declared;
        public string Magic;
        public string Signature;
        public List<string[]> Rows = new List<string[]>();
        public string Sha;
        public string Note;
        public double Entropy;
    }

    public static class MediaSniffer
    {

        private struct Sig { public int Off; public byte[] Bytes; public string Type; public string Ext; }
        private static Sig S(int off, byte[] b, string t, string e) { return new Sig { Off = off, Bytes = b, Type = t, Ext = e }; }
        private static byte[] B(params int[] xs) { var a = new byte[xs.Length]; for (int i = 0; i < xs.Length; i++) a[i] = (byte)xs[i]; return a; }
        private static byte[] A(string s) { return Encoding.ASCII.GetBytes(s); }

        private static readonly Sig[] SIGS = new[]
        {
            S(0, B(0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A), "PNG image", "png"),
            S(0, B(0xFF,0xD8,0xFF),                          "JPEG image", "jpg"),
            S(0, A("GIF87a"),                                "GIF image", "gif"),
            S(0, A("GIF89a"),                                "GIF image", "gif"),
            S(0, A("BM"),                                    "BMP image", "bmp"),
            S(0, A("RIFF"),                                  "RIFF container", "webp"), 
            S(0, B(0x49,0x49,0x2A,0x00),                     "TIFF image", "tiff"),
            S(0, B(0x4D,0x4D,0x00,0x2A),                     "TIFF image", "tiff"),
            S(0, A("%PDF-"),                                 "PDF document", "pdf"),
            S(0, B(0x50,0x4B,0x03,0x04),                     "ZIP archive", "zip"),
            S(0, B(0x50,0x4B,0x05,0x06),                     "ZIP archive (empty)", "zip"),
            S(0, B(0x1F,0x8B),                               "GZIP archive", "gz"),
            S(0, B(0x42,0x5A,0x68),                          "BZIP2 archive", "bz2"),
            S(0, B(0xFD,0x37,0x7A,0x58,0x5A,0x00),           "XZ archive", "xz"),
            S(0, B(0x37,0x7A,0xBC,0xAF,0x27,0x1C),           "7-Zip archive", "7z"),
            S(0, B(0x52,0x61,0x72,0x21,0x1A,0x07),           "RAR archive", "rar"),
            S(0, A("SIMPLE  ="),                             "FITS (astronomy)", "fits"),
            S(0, A("OggS"),                                  "Ogg media", "ogg"),
            S(0, A("fLaC"),                                  "FLAC audio", "flac"),
            S(0, A("ID3"),                                   "MP3 audio (ID3)", "mp3"),
            S(0, A("%!PS"),                                  "PostScript", "ps"),
            S(0, A("wOFF"),                                  "WOFF font", "woff"),
            S(0, A("wOF2"),                                  "WOFF2 font", "woff2"),
            S(0, B(0x00,0x01,0x00,0x00,0x00),                "TrueType font", "ttf"),
            S(0, A("OTTO"),                                  "OpenType font", "otf"),
            S(0, B(0x00,0x00,0x01,0x00),                     "ICO icon", "ico"),
            S(0, A("<?xml"),                                 "XML document", "xml"),
            S(0, A("SQLite format 3\0"),                     "SQLite database", "sqlite"),
            S(0, B(0x00,0x61,0x73,0x6D),                     "WebAssembly", "wasm"),
            S(0, A("PAR1"),                                  "Parquet columnar", "parquet"),
            S(0, B(0x4D,0x5A),                               "Windows PE (exe/dll)", "exe"),
            S(0, B(0x7F,0x45,0x4C,0x46),                     "ELF binary", "bin"),
        };

        public static Meta Sniff(string name, byte[] head, long totalSize, byte[] tail = null)
        {
            var m = new Meta { Name = name };
            head = head ?? new byte[0];

            string type = "binary (unrecognized)", ext = null;
            foreach (var s in SIGS)
            {
                if (StartsWith(head, s.Off, s.Bytes))
                {
                    type = s.Type; ext = s.Ext;
                    if (s.Bytes.Length == 4 && s.Type == "RIFF container")
                    {
                        string four = Ascii(head, 8, 4);
                        if (four == "WEBP") { type = "WebP image"; ext = "webp"; }
                        else if (four == "WAVE") { type = "WAV audio"; ext = "wav"; }
                        else if (four == "AVI ") { type = "AVI video"; ext = "avi"; }
                    }
                    break;
                }
            }

            if (ext == null && Ascii(head, 4, 4) == "ftyp")
            {
                string brand = Ascii(head, 8, 4);
                if (brand.StartsWith("hei") || brand.StartsWith("mif")) { type = "HEIF/HEIC image"; ext = "heic"; }
                else if (brand.StartsWith("avif")) { type = "AVIF image"; ext = "avif"; }
                else { type = "MP4 / MOV media"; ext = "mp4"; }
            }

            m.Type = type;
            m.Ext = ext != null ? ext.ToUpperInvariant() : "—";
            m.Magic = Hex(head, 0, 4);
            m.Signature = Hex(head, 0, 16);
            m.Entropy = Entropy(head);

            string declared = ExtOf(name);
            if (ext != null && declared != null && !ExtMatches(declared, ext))
                m.Declared = declared;

            m.Rows.Add(new[] { "Size", FormatSize(totalSize) });

            int[] wh;
            switch (Root(type))
            {
                case "PNG": case "JPEG": case "GIF": case "BMP": case "WebP": case "TIFF":
                    wh = Dimensions(type, head);
                    if (wh != null) m.Rows.Add(new[] { "Dimensions", wh[0] + " × " + wh[1] });
                    break;
                case "ZIP":
                    int entries = ZipEntries(head, tail);
                    if (entries >= 0) m.Rows.Add(new[] { "Entries", entries.ToString() });
                    break;
                case "FITS":
                    m.Rows.Add(new[] { "Header", "SIMPLE = T" });
                    m.Note = "no viewer yet → hex fallback";
                    break;
            }

#if ALOHA_METADATAEXTRACTOR
            try { EnrichWithMetadataExtractor(m, head); } catch {  }
#endif
            if (ext == null) m.Note = m.Note ?? "no signature → hex + entropy fallback";
            return m;
        }

        public static int[] Dimensions(string type, byte[] b)
        {
            try
            {
                string r = Root(type);
                if (r == "PNG") return new[] { BE32(b, 16), BE32(b, 20) };
                if (r == "GIF") return new[] { LE16(b, 6), LE16(b, 8) };
                if (r == "BMP") return new[] { LE32(b, 18), Math.Abs(LE32(b, 22)) };
                if (r == "JPEG")
                {
                    int i = 2;
                    while (i < b.Length - 9)
                    {
                        if (b[i] != 0xFF) { i++; continue; }
                        int mk = b[i + 1];
                        bool sof = (mk >= 0xC0 && mk <= 0xCF && mk != 0xC4 && mk != 0xC8 && mk != 0xCC);
                        if (sof) return new[] { BE16(b, i + 7), BE16(b, i + 5) };
                        int seg = BE16(b, i + 2); i += 2 + seg;
                    }
                }
                if (r == "WebP")
                {
                    string f = Ascii(b, 12, 4);
                    if (f == "VP8X") return new[] { 1 + LE24(b, 24), 1 + LE24(b, 27) };
                    if (f == "VP8L") { int bits = LE32(b, 21); return new[] { (bits & 0x3FFF) + 1, ((bits >> 14) & 0x3FFF) + 1 }; }
                    if (f == "VP8 ") return new[] { LE16(b, 26) & 0x3FFF, LE16(b, 28) & 0x3FFF };
                }
            }
            catch { }
            return null;
        }

        public static int ZipEntries(byte[] head, byte[] tail)
        {
            byte[] eocdBuf = tail ?? head;
            byte[] needle = B(0x50, 0x4B, 0x05, 0x06);
            int idx = LastIndexOf(eocdBuf, needle);
            if (idx >= 0 && idx + 12 <= eocdBuf.Length) return LE16(eocdBuf, idx + 10);

            int c = 0; byte[] lh = B(0x50, 0x4B, 0x03, 0x04);
            for (int i = 0; i + 4 <= head.Length; i++) if (Match(head, i, lh)) c++;
            return c > 0 ? c : -1;
        }

        public static double Entropy(byte[] b)
        {
            if (b == null || b.Length == 0) return 0;
            var counts = new int[256];
            foreach (var x in b) counts[x]++;
            double h = 0, n = b.Length;
            for (int i = 0; i < 256; i++)
            {
                if (counts[i] == 0) continue;
                double p = counts[i] / n;
                h -= p * Math.Log(p, 2);
            }
            return h;
        }

        private static bool StartsWith(byte[] b, int off, byte[] sig)
        {
            if (b.Length < off + sig.Length) return false;
            for (int i = 0; i < sig.Length; i++) if (b[off + i] != sig[i]) return false;
            return true;
        }
        private static bool Match(byte[] b, int at, byte[] sig)
        {
            if (at + sig.Length > b.Length) return false;
            for (int i = 0; i < sig.Length; i++) if (b[at + i] != sig[i]) return false;
            return true;
        }
        private static int LastIndexOf(byte[] hay, byte[] needle)
        {
            for (int i = hay.Length - needle.Length; i >= 0; i--) if (Match(hay, i, needle)) return i;
            return -1;
        }
        private static string Ascii(byte[] b, int off, int len)
        {
            if (b.Length < off + len) return "";
            return Encoding.ASCII.GetString(b, off, len);
        }
        private static int BE16(byte[] b, int i) { return (b[i] << 8) | b[i + 1]; }
        private static int BE32(byte[] b, int i) { return (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3]; }
        private static int LE16(byte[] b, int i) { return b[i] | (b[i + 1] << 8); }
        private static int LE24(byte[] b, int i) { return b[i] | (b[i + 1] << 8) | (b[i + 2] << 16); }
        private static int LE32(byte[] b, int i) { return b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24); }

        private static string Hex(byte[] b, int off, int len)
        {
            var sb = new StringBuilder();
            for (int i = off; i < off + len && i < b.Length; i++) { if (sb.Length > 0) sb.Append(' '); sb.Append(b[i].ToString("X2")); }
            return sb.ToString();
        }
        private static string Root(string type)
        {
            int sp = type.IndexOf(' ');
            return sp < 0 ? type : type.Substring(0, sp);
        }
        private static string ExtOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            int dot = name.LastIndexOf('.');
            if (dot < 0 || dot == name.Length - 1) return null;
            return name.Substring(dot + 1).ToLowerInvariant();
        }
        private static bool ExtMatches(string declared, string detected)
        {
            declared = declared.ToLowerInvariant(); detected = detected.ToLowerInvariant();
            if (declared == detected) return true;

            if ((declared == "jpg" || declared == "jpeg" || declared == "jfif") && detected == "jpg") return true;
            if ((declared == "tif" || declared == "tiff") && detected == "tiff") return true;
            if ((declared == "htm" || declared == "html") && detected == "xml") return false;
            if (detected == "zip" && (declared == "docx" || declared == "xlsx" || declared == "pptx" ||
                declared == "epub" || declared == "jar" || declared == "apk" || declared == "odt" ||
                declared == "ods" || declared == "odp" || declared == "cbz")) return true; 
            return false;
        }
        public static string FormatSize(long bytes)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes; int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            string h = i == 0 ? bytes + " B" : v.ToString("0.0", CultureInfo.InvariantCulture) + " " + u[i] +
                       " (" + bytes.ToString("N0", CultureInfo.InvariantCulture) + " B)";
            return h;
        }

#if ALOHA_METADATAEXTRACTOR

        private static void EnrichWithMetadataExtractor(Meta m, byte[] head)
        {
            using (var ms = new MemoryStream(head))
            {
                var dirs = MetadataExtractor.ImageMetadataReader.ReadMetadata(ms);
                string make = null, model = null, taken = null, exposure = null, fnum = null, iso = null, gps = null;
                foreach (var d in dirs)
                {
                    foreach (var t in d.Tags)
                    {
                        switch (t.Name)
                        {
                            case "Make": make = t.Description; break;
                            case "Model": model = t.Description; break;
                            case "Date/Time Original": taken = t.Description; break;
                            case "Exposure Time": exposure = t.Description; break;
                            case "F-Number": fnum = t.Description; break;
                            case "ISO Speed Ratings": iso = t.Description; break;
                        }
                    }
                    var g = d as MetadataExtractor.Formats.Exif.GpsDirectory;
                    if (g != null) { var loc = g.GetGeoLocation(); if (loc != null) gps = loc.Latitude.ToString("0.0000") + ", " + loc.Longitude.ToString("0.0000"); }
                }
                if (make != null || model != null) m.Rows.Add(new[] { "Camera", string.Join(" ", new[] { make, model }).Trim() });
                if (exposure != null || fnum != null || iso != null)
                    m.Rows.Add(new[] { "Exposure", string.Join(" · ", TrimNulls(exposure, fnum != null ? "f/" + fnum : null, iso != null ? "ISO " + iso : null)) });
                if (taken != null) m.Rows.Add(new[] { "Taken", taken });
                if (gps != null) m.Rows.Add(new[] { "GPS", gps });
            }
        }
        private static string[] TrimNulls(params string[] xs)
        { var l = new List<string>(); foreach (var x in xs) if (!string.IsNullOrEmpty(x)) l.Add(x); return l.ToArray(); }
#endif
    }
}
