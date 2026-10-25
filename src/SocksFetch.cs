using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace Aloha
{

    public static class SocksFetch
    {
        public enum Mode { Direct, Socks5, HttpProxy }

        public class Result
        {
            public int Status;
            public string FinalUrl;
            public string Body;
            public byte[] Raw;
            public int Hops;
            public string ContentType;
            public int Bytes { get { return Body == null ? 0 : Body.Length; } }
        }

        public static Result GetFollow(string url, Mode mode, string proxyHost, int proxyPort,
                                       int timeoutMs = 20000, int maxHops = 5)
        {
            string cur = url;
            for (int hop = 0; hop <= maxHops; hop++)
            {
                var r = GetOnce(cur, mode, proxyHost, proxyPort, timeoutMs);
                r.Hops = hop;
                if (r.Status < 300 || r.Status >= 400 || string.IsNullOrEmpty(r.FinalUrl) || r.FinalUrl == cur)
                    return r;
                cur = r.FinalUrl;
            }
            throw new IOException("too many redirects starting at " + url);
        }

        public static string Get(string url, Mode mode, string proxyHost, int proxyPort, int timeoutMs = 20000)
        {
            return GetFollow(url, mode, proxyHost, proxyPort, timeoutMs).Body;
        }

        public static Result Post(string url, Mode mode, string proxyHost, int proxyPort,
                                  string contentType, byte[] body, int timeoutMs = 20000)
        {
            return Request("POST", url, mode, proxyHost, proxyPort, contentType, body ?? new byte[0], timeoutMs);
        }

        private static Result GetOnce(string url, Mode mode, string proxyHost, int proxyPort, int timeoutMs)
        {
            return Request("GET", url, mode, proxyHost, proxyPort, null, null, timeoutMs);
        }

        public static string ProbeSocks(string proxyHost, int proxyPort, string host, int port, int timeoutMs = 15000)
        {
            if (!Aloha.Net.EgressGate.Current.ShouldAllow(host, port)) return "egress sealed: " + host + ":" + port;
            try
            {
                using (var tcp = new TcpClient())
                {
                    tcp.ReceiveTimeout = timeoutMs; tcp.SendTimeout = timeoutMs;
                    try { Connect(tcp, proxyHost, proxyPort, Math.Min(timeoutMs, 5000)); }
                    catch (Exception ex) { return "SOCKS proxy " + proxyHost + ":" + proxyPort + " unreachable (" + ex.Message + ")"; }
                    using (var ns = tcp.GetStream())
                    {
                        try { Socks5Connect(ns, host, port); }
                        catch (Exception ex) { return "proxy up, but " + host + ":" + port + " not reachable through it (" + ex.Message + ")"; }
                    }
                }
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        private static Result Request(string method, string url, Mode mode, string proxyHost, int proxyPort,
                                      string contentType, byte[] body, int timeoutMs)
        {
            var u = new Uri(url);
            if (u.Scheme != "http")
                throw new NotSupportedException("SocksFetch is http:// only (the receiver lists are bare http)");

            string host = u.Host;
            int port = u.IsDefaultPort ? 80 : u.Port;

            if (!Aloha.Net.EgressGate.Current.ShouldAllow(host, port))
                throw new IOException("egress sealed: " + host + ":" + port);

            using (var tcp = new TcpClient())
            {
                tcp.ReceiveTimeout = timeoutMs;
                tcp.SendTimeout = timeoutMs;

                if (mode == Mode.Direct) Connect(tcp, host, port, timeoutMs);
                else Connect(tcp, proxyHost, proxyPort, timeoutMs);

                using (var ns = tcp.GetStream())
                {
                    if (mode == Mode.Socks5) Socks5Connect(ns, host, port);

                    string target = (mode == Mode.HttpProxy) ? url : (u.PathAndQuery.Length == 0 ? "/" : u.PathAndQuery);

                    var req = new StringBuilder();
                    req.Append(method).Append(' ').Append(target).Append(" HTTP/1.1\r\n");
                    req.Append("Host: ").Append(u.Host).Append(u.IsDefaultPort ? "" : ":" + u.Port).Append("\r\n");
                    req.Append("User-Agent: Aloha/1\r\n");
                    req.Append("Accept: text/html,*/*\r\n");
                    req.Append("Accept-Encoding: identity\r\n");   
                    if (body != null)
                    {
                        req.Append("Content-Type: ").Append(string.IsNullOrEmpty(contentType) ? "application/octet-stream" : contentType).Append("\r\n");
                        req.Append("Content-Length: ").Append(body.Length).Append("\r\n");
                    }
                    req.Append("Connection: close\r\n\r\n");
                    var rb = Encoding.ASCII.GetBytes(req.ToString());
                    ns.Write(rb, 0, rb.Length);
                    if (body != null && body.Length > 0) ns.Write(body, 0, body.Length);
                    ns.Flush();

                    byte[] all;
                    using (var ms = new MemoryStream())
                    {
                        var buf = new byte[16384];
                        int n;
                        while ((n = ns.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                        all = ms.ToArray();
                    }

                    int split = -1;
                    for (int i = 0; i < all.Length - 3; i++)
                        if (all[i] == 13 && all[i + 1] == 10 && all[i + 2] == 13 && all[i + 3] == 10) { split = i + 4; break; }
                    if (split < 0)
                        return new Result { Status = 0, FinalUrl = url, Body = Encoding.UTF8.GetString(all), Raw = all };

                    string head = Encoding.ASCII.GetString(all, 0, split);
                    var raw = new byte[all.Length - split];
                    Buffer.BlockCopy(all, split, raw, 0, raw.Length);

                    if (head.IndexOf("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase) >= 0)
                        raw = DechunkBytes(raw);

                    string bodyText = Encoding.UTF8.GetString(raw);
                    var res = new Result { Body = bodyText, Raw = raw, FinalUrl = url };

                    var firstLine = head.Split('\n')[0].Trim();
                    var bits = firstLine.Split(' ');
                    if (bits.Length > 1) int.TryParse(bits[1], out res.Status);

                    res.ContentType = HeaderValue(head, "Content-Type");
                    string loc = HeaderValue(head, "Location");
                    if (!string.IsNullOrEmpty(loc))
                    {

                        try { res.FinalUrl = new Uri(new Uri(url), loc).ToString(); }
                        catch { res.FinalUrl = loc; }
                    }
                    return res;
                }
            }
        }

        private static string HeaderValue(string head, string name)
        {
            foreach (var raw in head.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                int c = line.IndexOf(':');
                if (c <= 0) continue;
                if (string.Equals(line.Substring(0, c).Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(c + 1).Trim();
            }
            return null;
        }

        private static void Connect(TcpClient tcp, string host, int port, int timeoutMs)
        {
            var ar = tcp.BeginConnect(host, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(timeoutMs))
                throw new IOException("connect timed out: " + host + ":" + port);
            tcp.EndConnect(ar);
        }

        private static void Socks5Connect(NetworkStream ns, string host, int port)
        {
            ns.Write(new byte[] { 0x05, 0x01, 0x00 }, 0, 3);
            var greet = ReadExact(ns, 2);
            if (greet[0] != 0x05 || greet[1] != 0x00)
                throw new IOException("SOCKS5: proxy refused the no-auth method");

            var h = Encoding.ASCII.GetBytes(host);
            if (h.Length > 255) throw new IOException("SOCKS5: hostname too long");
            var req = new byte[7 + h.Length];
            req[0] = 0x05; req[1] = 0x01; req[2] = 0x00; req[3] = 0x03;
            req[4] = (byte)h.Length;
            Buffer.BlockCopy(h, 0, req, 5, h.Length);
            req[5 + h.Length] = (byte)(port >> 8);
            req[6 + h.Length] = (byte)(port & 0xFF);
            ns.Write(req, 0, req.Length);

            var rep = ReadExact(ns, 4);
            if (rep[1] != 0x00) throw new IOException("SOCKS5: connect failed, reply code " + rep[1] + " (" + Rep(rep[1]) + ")");

            switch (rep[3])
            {
                case 0x01: ReadExact(ns, 4 + 2); break;
                case 0x04: ReadExact(ns, 16 + 2); break;
                case 0x03: int len = ReadExact(ns, 1)[0]; ReadExact(ns, len + 2); break;
                default: throw new IOException("SOCKS5: unknown address type " + rep[3]);
            }
        }

        private static string Rep(byte c)
        {
            switch (c)
            {
                case 1: return "general failure";
                case 2: return "connection not allowed";
                case 3: return "network unreachable";
                case 4: return "host unreachable";
                case 5: return "connection refused";
                case 6: return "TTL expired";
                case 7: return "command not supported";
                case 8: return "address type not supported";
                default: return "?";
            }
        }

        private static byte[] ReadExact(Stream s, int n)
        {
            var b = new byte[n];
            int off = 0;
            while (off < n)
            {
                int got = s.Read(b, off, n - off);
                if (got <= 0) throw new IOException("SOCKS5: short read");
                off += got;
            }
            return b;
        }

        private static byte[] DechunkBytes(byte[] body)
        {
            var outp = new MemoryStream();
            int i = 0;
            while (i < body.Length)
            {
                int nl = -1;
                for (int k = i; k + 1 < body.Length; k++) if (body[k] == 13 && body[k + 1] == 10) { nl = k; break; }
                if (nl < 0) break;
                string sizeLine = Encoding.ASCII.GetString(body, i, nl - i).Split(';')[0].Trim();
                int size;
                if (!int.TryParse(sizeLine, System.Globalization.NumberStyles.HexNumber, null, out size) || size <= 0) break;
                i = nl + 2;
                if (i + size > body.Length) size = body.Length - i;
                outp.Write(body, i, size);
                i += size + 2;
            }
            return outp.Length > 0 ? outp.ToArray() : body;
        }

    }
}
