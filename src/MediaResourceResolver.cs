using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace Aloha.Media
{
    public sealed class MediaResourceResolver : IResourceResolver
    {
        private const int HEAD = 64 * 1024;
        private const int TIMEOUT_MS = 20000;
        private const int MAX_HOPS = 5;

        private readonly NetConfig _cfg;
        private readonly LocalFileResolver _local = new LocalFileResolver();

        public MediaResourceResolver(NetConfig cfg) { _cfg = cfg; }

        public ResourceInfo Resolve(string url)
        {
            if (string.IsNullOrEmpty(url)) throw new IOException("no url");
            string lower = url.TrimStart().ToLowerInvariant();

            if (lower.StartsWith("data:", StringComparison.Ordinal)) return ResolveData(url);
            if (lower.StartsWith("blob:", StringComparison.Ordinal)) throw new NotSupportedException("blob: lives inside the page — nothing to read");
            if (lower.StartsWith("about:", StringComparison.Ordinal) || lower.StartsWith("javascript:", StringComparison.Ordinal))
                throw new NotSupportedException("not a file");
            if (lower.StartsWith("http://", StringComparison.Ordinal) || lower.StartsWith("https://", StringComparison.Ordinal))
                return ResolveHttp(url);

            var loc = _local.Resolve(url);
            if (loc == null)
            {
                if (url.IndexOf("://", StringComparison.Ordinal) >= 0) throw new NotSupportedException("scheme not supported");
                throw new FileNotFoundException("file not found");
            }
            return loc;
        }

        private static ResourceInfo ResolveData(string url)
        {
            int comma = url.IndexOf(',');
            if (comma < 0) throw new IOException("malformed data: URI");
            string meta = url.Substring(5, comma - 5);
            string payload = url.Substring(comma + 1);
            byte[] bytes;
            if (meta.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
            {
                try { bytes = Convert.FromBase64String(payload.Trim()); }
                catch { throw new IOException("bad base64 in data: URI"); }
            }
            else bytes = Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));

            string mime = meta.Split(';')[0];
            string ext = "";
            int slash = mime.IndexOf('/');
            if (slash >= 0 && slash < mime.Length - 1) ext = mime.Substring(slash + 1);
            if (ext.StartsWith("x-", StringComparison.OrdinalIgnoreCase)) ext = ext.Substring(2);
            if (ext == "jpeg") ext = "jpg"; if (ext == "svg+xml") ext = "svg";
            return new ResourceInfo
            {
                Name = "(inline " + (mime.Length == 0 ? "data" : mime) + ")",
                DeclaredExt = ext,
                Length = bytes.Length,
                IsLocal = true,
                Open = () => new MemoryStream(bytes, false)
            };
        }

        private ResourceInfo ResolveHttp(string url)
        {
            Uri u;
            try { u = new Uri(url); } catch { throw new IOException("malformed url"); }

            long total; string finalUrl; string ctype;
            byte[] head = FetchRange(u, 0, HEAD - 1, out total, out finalUrl, out ctype);
            Uri fu; try { fu = new Uri(finalUrl); } catch { fu = u; }

            string name = fu.Segments.Length > 0 ? Uri.UnescapeDataString(fu.Segments[fu.Segments.Length - 1].TrimEnd('/')) : "";
            if (name.Length == 0) name = fu.Host;
            string decl = Path.GetExtension(name).TrimStart('.');
            if (decl.Length == 0 && !string.IsNullOrEmpty(ctype))
            {

                string m = ctype.Split(';')[0].Trim().ToLowerInvariant();
                int s = m.IndexOf('/'); if (s >= 0) decl = m.Substring(s + 1);
                if (decl == "jpeg") decl = "jpg"; if (decl == "svg+xml") decl = "svg";
            }

            return new ResourceInfo
            {
                Name = name,
                DeclaredExt = decl,
                Length = total,
                IsLocal = false,
                Head = head,

                Open = () => OpenFull(fu)
            };
        }

        private byte[] FetchRange(Uri u, long from, long to, out long total, out string finalUrl, out string ctype)
        {
            total = -1; finalUrl = u.ToString(); ctype = null;
            Uri cur = u;
            for (int hop = 0; hop <= MAX_HOPS; hop++)
            {
                var r = Open(cur, from, to);
                using (r)
                {
                    if (r.Status >= 300 && r.Status < 400 && r.Location != null)
                    {
                        Uri nxt; if (!Uri.TryCreate(cur, r.Location, out nxt)) throw new IOException("bad redirect");
                        cur = nxt; continue;
                    }
                    if (r.Status == 401 || r.Status == 403) throw new IOException("HTTP " + r.Status + " — needs the page's session (no cookies are sent)");
                    if (r.Status != 200 && r.Status != 206) throw new IOException("HTTP " + r.Status);
                    finalUrl = cur.ToString(); ctype = r.ContentType;
                    if (r.Status == 206 && r.RangeTotal >= 0) total = r.RangeTotal;
                    else if (r.Status == 200 && r.ContentLength >= 0) total = r.ContentLength;
                    int want = (int)(to - from + 1);
                    var ms = new MemoryStream();
                    var buf = new byte[16384]; int n;
                    while (ms.Length < want && (n = r.Body.Read(buf, 0, (int)Math.Min(buf.Length, want - ms.Length))) > 0) ms.Write(buf, 0, n);
                    return ms.ToArray();
                }
            }
            throw new IOException("too many redirects");
        }

        private Stream OpenFull(Uri u)
        {
            Uri cur = u;
            for (int hop = 0; hop <= MAX_HOPS; hop++)
            {
                var r = Open(cur, -1, -1);
                if (r.Status >= 300 && r.Status < 400 && r.Location != null)
                {
                    Uri nxt; bool ok = Uri.TryCreate(cur, r.Location, out nxt); r.Dispose();
                    if (!ok) throw new IOException("bad redirect");
                    cur = nxt; continue;
                }
                if (r.Status != 200) { int st = r.Status; r.Dispose(); throw new IOException("HTTP " + st); }
                return r;
            }
            throw new IOException("too many redirects");
        }

        private sealed class Response : Stream
        {
            public int Status; public string Location; public string ContentType;
            public long ContentLength = -1, RangeTotal = -1;
            public Stream Body;
            private TcpClient _tcp; private Stream _io; private byte[] _left; private int _leftPos;
            private readonly bool _chunked; private long _chunkRemain = -1; private bool _done;

            public Response(TcpClient tcp, Stream io, byte[] leftover, bool chunked)
            { _tcp = tcp; _io = io; _left = leftover ?? new byte[0]; _chunked = chunked; Body = this; }

            private int RawRead(byte[] b, int off, int n)
            {
                if (_leftPos < _left.Length) { int t = Math.Min(n, _left.Length - _leftPos); Buffer.BlockCopy(_left, _leftPos, b, off, t); _leftPos += t; return t; }
                return _io.Read(b, off, n);
            }
            private int RawByte() { var one = new byte[1]; return RawRead(one, 0, 1) <= 0 ? -1 : one[0]; }
            private string RawLine()
            {
                var sb = new StringBuilder(); int c;
                while ((c = RawByte()) >= 0) { if (c == '\n') break; if (c != '\r') sb.Append((char)c); if (sb.Length > 8192) throw new IOException("chunk line too long"); }
                return c < 0 && sb.Length == 0 ? null : sb.ToString();
            }
            public override int Read(byte[] b, int off, int n)
            {
                if (_done || n == 0) return 0;
                if (!_chunked) { int r = RawRead(b, off, n); if (r <= 0) _done = true; return r < 0 ? 0 : r; }
                if (_chunkRemain <= 0)
                {
                    if (_chunkRemain == 0) RawLine();
                    string line = RawLine();
                    if (line == null) { _done = true; return 0; }
                    int semi = line.IndexOf(';'); if (semi >= 0) line = line.Substring(0, semi);
                    long size; if (!long.TryParse(line.Trim(), System.Globalization.NumberStyles.HexNumber, null, out size)) throw new IOException("bad chunk size");
                    if (size == 0) { _done = true; return 0; }
                    _chunkRemain = size;
                }
                int take = (int)Math.Min(n, _chunkRemain);
                int got = RawRead(b, off, take);
                if (got <= 0) { _done = true; return 0; }
                _chunkRemain -= got;
                return got;
            }
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { return ContentLength >= 0 ? ContentLength : (RangeTotal >= 0 ? RangeTotal : 0); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override long Seek(long o, SeekOrigin s) { throw new NotSupportedException(); }
            public override void SetLength(long v) { throw new NotSupportedException(); }
            public override void Write(byte[] b, int o, int n) { throw new NotSupportedException(); }
            protected override void Dispose(bool disposing)
            {
                if (disposing) { try { if (_io != null) _io.Dispose(); } catch { } try { if (_tcp != null) _tcp.Close(); } catch { } _io = null; _tcp = null; }
                base.Dispose(disposing);
            }
        }

        private Response Open(Uri u, long from, long to)
        {
            if (u.Scheme != "http" && u.Scheme != "https") throw new NotSupportedException("scheme not supported");
            string host = u.Host; int port = u.IsDefaultPort ? (u.Scheme == "https" ? 443 : 80) : u.Port;
            if (!Aloha.Net.EgressGate.Current.ShouldAllow(host, port)) throw new IOException("egress sealed: " + host + ":" + port);

            SocksFetch.Mode mode; string pHost; int pPort;
            ResolveProxy(_cfg, out mode, out pHost, out pPort);
            if (mode == SocksFetch.Mode.Direct && host.EndsWith(".onion", StringComparison.OrdinalIgnoreCase))
                throw new IOException("no SOCKS proxy configured — .onion needs Tor");

            var tcp = new TcpClient();
            Stream io = null;
            try
            {
                tcp.ReceiveTimeout = TIMEOUT_MS; tcp.SendTimeout = TIMEOUT_MS;
                if (mode == SocksFetch.Mode.Direct) Connect(tcp, host, port);
                else Connect(tcp, pHost, pPort);
                var ns = tcp.GetStream();
                io = ns;

                bool viaHttpProxyPlain = false;
                if (mode == SocksFetch.Mode.Socks5) Socks5Connect(ns, host, port);
                else if (mode == SocksFetch.Mode.HttpProxy)
                {
                    if (u.Scheme == "https") HttpConnect(ns, host, port);
                    else viaHttpProxyPlain = true;
                }
                if (u.Scheme == "https")
                {
                    var ssl = new SslStream(ns, false, (s, c, ch, e) => e == SslPolicyErrors.None);
                    ssl.ReadTimeout = TIMEOUT_MS; ssl.WriteTimeout = TIMEOUT_MS;
                    ssl.AuthenticateAsClient(host, null, SslProtocols.Tls12, false);
                    io = ssl;
                }

                string target = viaHttpProxyPlain ? u.ToString() : (u.PathAndQuery.Length == 0 ? "/" : u.PathAndQuery);
                var req = new StringBuilder();
                req.Append("GET ").Append(target).Append(" HTTP/1.1\r\n");
                req.Append("Host: ").Append(host).Append(u.IsDefaultPort ? "" : ":" + u.Port).Append("\r\n");
                req.Append("User-Agent: Aloha/").Append(Form1.VERSION).Append("\r\nAccept: */*\r\nAccept-Encoding: identity\r\nConnection: close\r\n");
                if (from >= 0) req.Append("Range: bytes=").Append(from).Append('-').Append(to).Append("\r\n");
                req.Append("\r\n");
                var rb = Encoding.ASCII.GetBytes(req.ToString());
                io.Write(rb, 0, rb.Length); io.Flush();

                byte[] leftover; string head = ReadHeaders(io, out leftover);
                var r = new Response(tcp, io, leftover, HeaderValue(head, "Transfer-Encoding") != null
                                                        && HeaderValue(head, "Transfer-Encoding").IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0);
                r.Status = StatusCode(head);
                r.Location = HeaderValue(head, "Location");
                r.ContentType = HeaderValue(head, "Content-Type");
                long cl; if (long.TryParse(HeaderValue(head, "Content-Length") ?? "", out cl)) r.ContentLength = cl;
                string cr = HeaderValue(head, "Content-Range");
                if (cr != null) { int sl = cr.LastIndexOf('/'); long tot; if (sl >= 0 && long.TryParse(cr.Substring(sl + 1).Trim(), out tot)) r.RangeTotal = tot; }
                return r;
            }
            catch
            {
                try { if (io != null) io.Dispose(); } catch { }
                try { tcp.Close(); } catch { }
                throw;
            }
        }

        public static void ResolveProxy(NetConfig cfg, out SocksFetch.Mode mode, out string host, out int port)
        {
            mode = SocksFetch.Mode.Direct; host = "127.0.0.1"; port = 0;
            if (cfg == null) return;
            try
            {
                if (cfg.SealedTor) { mode = SocksFetch.Mode.Socks5; port = NetConfig.SealedTorSocksPort; return; }
                if (cfg.CfgUseInterceptProxy && cfg.InterceptPort > 0) { mode = SocksFetch.Mode.HttpProxy; port = cfg.InterceptPort; return; }
                string scheme = (cfg.ProxyScheme ?? "none").ToLowerInvariant();
                if (scheme == "socks5" || scheme == "socks5h" || scheme == "socks4")
                { mode = SocksFetch.Mode.Socks5; host = string.IsNullOrEmpty(cfg.ProxyHost) ? "127.0.0.1" : cfg.ProxyHost; port = cfg.ProxyPort; }
                else if (scheme == "http")
                { mode = SocksFetch.Mode.HttpProxy; host = string.IsNullOrEmpty(cfg.ProxyHost) ? "127.0.0.1" : cfg.ProxyHost; port = cfg.ProxyPort; }
                if (port <= 0) { mode = SocksFetch.Mode.Direct; port = 0; }
            }
            catch { mode = SocksFetch.Mode.Direct; }
        }

        private static void Connect(TcpClient tcp, string host, int port)
        {
            var ar = tcp.BeginConnect(host, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(TIMEOUT_MS)) { try { tcp.Close(); } catch { } throw new IOException("connect timeout to " + host + ":" + port); }
            tcp.EndConnect(ar);
        }

        private static void Socks5Connect(NetworkStream ns, string host, int port)
        {
            ns.Write(new byte[] { 0x05, 0x01, 0x00 }, 0, 3);
            var greet = ReadExact(ns, 2);
            if (greet[0] != 0x05 || greet[1] != 0x00) throw new IOException("SOCKS5: handshake refused");
            var h = Encoding.ASCII.GetBytes(host);
            if (h.Length > 255) throw new IOException("SOCKS5: hostname too long");
            var req = new byte[7 + h.Length];
            req[0] = 0x05; req[1] = 0x01; req[2] = 0x00; req[3] = 0x03; req[4] = (byte)h.Length;
            Buffer.BlockCopy(h, 0, req, 5, h.Length);
            req[5 + h.Length] = (byte)(port >> 8); req[6 + h.Length] = (byte)(port & 0xFF);
            ns.Write(req, 0, req.Length);
            var rep = ReadExact(ns, 4);
            if (rep[1] != 0x00) throw new IOException("SOCKS5: connect failed (reply " + rep[1] + ")");
            switch (rep[3])
            {
                case 0x01: ReadExact(ns, 6); break;
                case 0x04: ReadExact(ns, 18); break;
                case 0x03: int len = ReadExact(ns, 1)[0]; ReadExact(ns, len + 2); break;
                default: throw new IOException("SOCKS5: unknown address type");
            }
        }

        private static void HttpConnect(NetworkStream ns, string host, int port)
        {
            var rb = Encoding.ASCII.GetBytes("CONNECT " + host + ":" + port + " HTTP/1.1\r\nHost: " + host + ":" + port + "\r\n\r\n");
            ns.Write(rb, 0, rb.Length); ns.Flush();
            byte[] left; string head = ReadHeaders(ns, out left);
            int st = StatusCode(head);
            if (st < 200 || st >= 300) throw new IOException("HTTP proxy CONNECT refused (" + st + ")");

        }

        private static byte[] ReadExact(Stream s, int n)
        {
            var b = new byte[n]; int off = 0;
            while (off < n) { int r = s.Read(b, off, n - off); if (r <= 0) throw new IOException("connection closed"); off += r; }
            return b;
        }

        private static string ReadHeaders(Stream io, out byte[] leftover)
        {
            var acc = new MemoryStream(); var one = new byte[1]; int matched = 0;
            while (acc.Length < 65536)
            {
                int n = io.Read(one, 0, 1);
                if (n <= 0) break;
                acc.WriteByte(one[0]);
                byte b = one[0];
                if (matched == 0 && b == 13) matched = 1;
                else if (matched == 1 && b == 10) matched = 2;
                else if (matched == 2 && b == 13) matched = 3;
                else if (matched == 3 && b == 10) { leftover = new byte[0]; return Encoding.ASCII.GetString(acc.ToArray()); }
                else matched = (b == 13) ? 1 : 0;
            }
            leftover = new byte[0];
            throw new IOException("no HTTP response header");
        }

        private static int StatusCode(string head)
        {
            var first = head.Split('\n')[0].Trim(); var bits = first.Split(' ');
            int st; return (bits.Length > 1 && int.TryParse(bits[1], out st)) ? st : 0;
        }

        private static string HeaderValue(string head, string name)
        {
            foreach (var raw in head.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length > name.Length + 1 && line[name.Length] == ':' && line.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(name.Length + 1).Trim();
            }
            return null;
        }
    }
}
