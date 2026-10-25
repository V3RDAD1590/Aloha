using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aloha
{
    public sealed class TorStripedFetch
    {
        public sealed class Stripe
        {
            public int Index;
            public long From, To;
            public long Done;
            public string Status = "queued"; 
            public string Isolation = "";    
            public int Attempts;
            public long Length { get { return To - From + 1; } }
        }

        public string ProxyHost = "127.0.0.1";
        public int ProxyPort = 9050;
        public int TimeoutMs = 30000;
        public bool AllowInvalidCertificate = false;

        public event Action<Stripe> Progress;
        public event Action<string> Log;

        public List<Stripe> Stripes = new List<Stripe>();
        public long TotalBytes = -1;
        public bool RangesSupported;
        public string FinalUrl;
        public string ContentType;

        private const int MinStripe = 256 * 1024;

        public async Task<string> DownloadAsync(string url, string destPath, int parts, CancellationToken ct)
        {
            if (parts < 1) parts = 1; if (parts > 16) parts = 16;
            var u = new Uri(url);
            if (u.Scheme != "http" && u.Scheme != "https") throw new NotSupportedException("http(s) only");

            Uri cur = u;
            ProbeResult probe = null;
            for (int hop = 0; hop < 6; hop++)
            {
                Gate(cur);
                probe = await Task.Run(() => Probe(cur, ct), ct);
                if (probe.Redirect == null) break;
                Emit("redirect → " + probe.Redirect);
                cur = new Uri(cur, probe.Redirect);
            }
            if (probe == null || probe.Redirect != null) throw new IOException("too many redirects");
            FinalUrl = cur.ToString();
            ContentType = probe.ContentType;
            TotalBytes = probe.Total;
            RangesSupported = probe.Ranges;

            Stripes.Clear();
            if (!RangesSupported || TotalBytes <= 0)
            {
                Emit(RangesSupported ? "size unknown — one stream" : "server ignores Range — one stream (no striping possible here)");
                Stripes.Add(new Stripe { Index = 0, From = 0, To = TotalBytes > 0 ? TotalBytes - 1 : -1 });
            }
            else
            {
                int n = parts;
                while (n > 1 && TotalBytes / n < MinStripe) n--;
                long per = TotalBytes / n;
                for (int i = 0; i < n; i++)
                    Stripes.Add(new Stripe { Index = i, From = i * per, To = (i == n - 1) ? TotalBytes - 1 : (i + 1) * per - 1 });
                Emit(TotalBytes + " bytes in " + n + " stripes over " + n + " circuits");
            }

            string destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
            using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                if (TotalBytes > 0) fs.SetLength(TotalBytes);

            var tasks = new List<Task>();
            foreach (var s in Stripes)
            {
                var st = s;
                tasks.Add(Task.Run(() => RunStripe(cur, st, destPath, ct), ct));
            }
            await Task.WhenAll(tasks);

            long sum = 0; foreach (var s in Stripes) sum += s.Done;
            if (TotalBytes > 0 && sum != TotalBytes) throw new IOException("reassembly short: " + sum + " of " + TotalBytes + " bytes");
            if (TotalBytes <= 0) TotalBytes = sum;
            Emit("done — " + sum + " bytes → " + destPath);
            return destPath;
        }

        private void RunStripe(Uri u, Stripe s, string destPath, CancellationToken ct)
        {
            for (s.Attempts = 1; s.Attempts <= 3; s.Attempts++)
            {
                ct.ThrowIfCancellationRequested();
                s.Isolation = "stripe" + s.Index + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                s.Done = 0;
                try
                {
                    FetchRange(u, s, destPath, ct);
                    s.Status = "done"; Report(s);
                    return;
                }
                catch (OperationCanceledException) { s.Status = "cancelled"; Report(s); throw; }
                catch (Exception ex)
                {
                    s.Status = s.Attempts < 3 ? "retry (" + ex.Message + ")" : "failed (" + ex.Message + ")";
                    Emit("stripe " + s.Index + " attempt " + s.Attempts + ": " + ex.Message);
                    Report(s);
                    if (s.Attempts >= 3) throw;
                }
            }
        }

        private void FetchRange(Uri u, Stripe s, string destPath, CancellationToken ct)
        {
            s.Status = "connecting"; Report(s);
            using (var tcp = new TcpClient())
            {
                tcp.ReceiveTimeout = TimeoutMs; tcp.SendTimeout = TimeoutMs;
                Connect(tcp, ProxyHost, ProxyPort, TimeoutMs);
                using (var ns = tcp.GetStream())
                {
                    Socks5Connect(ns, u.Host, u.Port, s.Isolation, "aloha");
                    Stream io = ns;
                    if (u.Scheme == "https")
                    {
                        s.Status = "tls"; Report(s);
                        io = Tls(ns, u.Host);
                    }
                    bool ranged = s.To >= 0 && RangesSupported;
                    var req = new StringBuilder();
                    req.Append("GET ").Append(u.PathAndQuery.Length == 0 ? "/" : u.PathAndQuery).Append(" HTTP/1.1\r\n");
                    req.Append("Host: ").Append(u.Host).Append(u.IsDefaultPort ? "" : ":" + u.Port).Append("\r\n");
                    req.Append("User-Agent: Aloha/1\r\nAccept: */*\r\nAccept-Encoding: identity\r\nConnection: close\r\n");
                    if (ranged) req.Append("Range: bytes=").Append(s.From).Append('-').Append(s.To).Append("\r\n");
                    req.Append("\r\n");
                    var rb = Encoding.ASCII.GetBytes(req.ToString());
                    io.Write(rb, 0, rb.Length); io.Flush();

                    byte[] leftover; string head = ReadHeaders(io, out leftover);
                    int status = StatusCode(head);
                    if (ranged && status != 206) throw new IOException("expected 206 for the range, got " + status);
                    if (!ranged && status != 200) throw new IOException("HTTP " + status);
                    long len = HeaderLong(head, "Content-Length", -1);
                    if (ranged && len >= 0 && len != s.Length) throw new IOException("range length mismatch: " + len + " vs " + s.Length);

                    s.Status = "receiving"; Report(s);
                    using (var fs = new FileStream(destPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                    {
                        fs.Seek(s.From < 0 ? 0 : s.From, SeekOrigin.Begin);
                        long want = ranged ? s.Length : len;
                        long got = 0;
                        if (leftover.Length > 0)
                        {
                            int take = want >= 0 ? (int)Math.Min(leftover.Length, want) : leftover.Length;
                            fs.Write(leftover, 0, take); got += take;
                        }
                        var buf = new byte[65536];
                        long lastReport = 0;
                        while (want < 0 || got < want)
                        {
                            ct.ThrowIfCancellationRequested();
                            int room = want < 0 ? buf.Length : (int)Math.Min(buf.Length, want - got);
                            int n = io.Read(buf, 0, room);
                            if (n <= 0) break;
                            fs.Write(buf, 0, n); got += n;
                            s.Done = got;
                            if (got - lastReport >= 65536) { lastReport = got; Report(s); }
                        }
                        if (want >= 0 && got != want) throw new IOException("short body: " + got + " of " + want);
                        s.Done = got;
                    }
                }
            }
        }

        private sealed class ProbeResult { public long Total = -1; public bool Ranges; public string Redirect; public string ContentType; }

        private ProbeResult Probe(Uri u, CancellationToken ct)
        {
            var res = new ProbeResult();
            using (var tcp = new TcpClient())
            {
                tcp.ReceiveTimeout = TimeoutMs; tcp.SendTimeout = TimeoutMs;
                Connect(tcp, ProxyHost, ProxyPort, TimeoutMs);
                using (var ns = tcp.GetStream())
                {
                    Socks5Connect(ns, u.Host, u.Port, "probe-" + Guid.NewGuid().ToString("N").Substring(0, 8), "aloha");
                    Stream io = ns;
                    if (u.Scheme == "https") io = Tls(ns, u.Host);
                    string req = "GET " + (u.PathAndQuery.Length == 0 ? "/" : u.PathAndQuery) + " HTTP/1.1\r\n" +
                                 "Host: " + u.Host + (u.IsDefaultPort ? "" : ":" + u.Port) + "\r\n" +
                                 "User-Agent: Aloha/1\r\nAccept: */*\r\nAccept-Encoding: identity\r\nRange: bytes=0-0\r\nConnection: close\r\n\r\n";
                    var rb = Encoding.ASCII.GetBytes(req);
                    io.Write(rb, 0, rb.Length); io.Flush();
                    byte[] leftover; string head = ReadHeaders(io, out leftover);
                    int status = StatusCode(head);
                    if (status >= 300 && status < 400) { res.Redirect = HeaderValue(head, "Location"); if (res.Redirect == null) throw new IOException("HTTP " + status + " without Location"); return res; }
                    if (status != 200 && status != 206) throw new IOException("HTTP " + status + " on probe");
                    res.ContentType = HeaderValue(head, "Content-Type");
                    if (status == 206)
                    {

                        string cr = HeaderValue(head, "Content-Range") ?? "";
                        int slash = cr.LastIndexOf('/');
                        long total;
                        if (slash >= 0 && long.TryParse(cr.Substring(slash + 1).Trim(), out total)) { res.Total = total; res.Ranges = true; }
                        else { res.Ranges = true; res.Total = -1; }
                    }
                    else
                    {
                        res.Ranges = false;
                        res.Total = HeaderLong(head, "Content-Length", -1);
                    }
                    return res;
                }
            }
        }

        private void Gate(Uri u)
        {
            int port = u.IsDefaultPort ? (u.Scheme == "https" ? 443 : 80) : u.Port;
            if (!Aloha.Net.EgressGate.Current.ShouldAllow(u.Host, port))
                throw new IOException("egress sealed: " + u.Host + ":" + port);
        }

        private static void Connect(TcpClient tcp, string host, int port, int timeoutMs)
        {
            var ar = tcp.BeginConnect(host, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) throw new IOException("SOCKS proxy " + host + ":" + port + " — connect timed out");
            tcp.EndConnect(ar);
        }

        private static void Socks5Connect(NetworkStream ns, string host, int port, string user, string pass)
        {
            ns.Write(new byte[] { 0x05, 0x02, 0x00, 0x02 }, 0, 4);
            var greet = ReadExact(ns, 2);
            if (greet[0] != 0x05) throw new IOException("SOCKS5: bad greeting");
            if (greet[1] == 0x02)
            {
                byte[] ub = Encoding.ASCII.GetBytes(user), pb = Encoding.ASCII.GetBytes(pass);
                var auth = new byte[3 + ub.Length + pb.Length];
                auth[0] = 0x01; auth[1] = (byte)ub.Length; Buffer.BlockCopy(ub, 0, auth, 2, ub.Length);
                auth[2 + ub.Length] = (byte)pb.Length; Buffer.BlockCopy(pb, 0, auth, 3 + ub.Length, pb.Length);
                ns.Write(auth, 0, auth.Length);
                var ar = ReadExact(ns, 2);
                if (ar[1] != 0x00) throw new IOException("SOCKS5: user/pass refused");
            }
            else if (greet[1] != 0x00) throw new IOException("SOCKS5: no acceptable auth method (0x" + greet[1].ToString("x2") + ")");

            var h = Encoding.ASCII.GetBytes(host);
            if (h.Length > 255) throw new IOException("SOCKS5: hostname too long");
            var req = new byte[7 + h.Length];
            req[0] = 0x05; req[1] = 0x01; req[2] = 0x00; req[3] = 0x03; req[4] = (byte)h.Length;
            Buffer.BlockCopy(h, 0, req, 5, h.Length);
            req[5 + h.Length] = (byte)(port >> 8); req[6 + h.Length] = (byte)(port & 0xFF);
            ns.Write(req, 0, req.Length);
            var rep = ReadExact(ns, 4);
            if (rep[1] != 0x00) throw new IOException("SOCKS5: connect failed, reply " + rep[1] + " (" + Rep(rep[1]) + ")");
            switch (rep[3])
            {
                case 0x01: ReadExact(ns, 6); break;
                case 0x04: ReadExact(ns, 18); break;
                case 0x03: int len = ReadExact(ns, 1)[0]; ReadExact(ns, len + 2); break;
                default: throw new IOException("SOCKS5: unknown address type " + rep[3]);
            }
        }

        private static string Rep(byte c)
        {
            switch (c)
            {
                case 1: return "general failure"; case 2: return "not allowed by ruleset"; case 3: return "network unreachable";
                case 4: return "host unreachable"; case 5: return "connection refused"; case 6: return "TTL expired";
                case 7: return "command not supported"; case 8: return "address type not supported"; default: return "?";
            }
        }

        private Stream Tls(NetworkStream ns, string host)
        {
            var ssl = new SslStream(ns, false, (sender, cert, chain, errors) => errors == SslPolicyErrors.None || AllowInvalidCertificate);
            ssl.ReadTimeout = TimeoutMs; ssl.WriteTimeout = TimeoutMs;
            ssl.AuthenticateAsClient(host, null, SslProtocols.Tls12, false);
            return ssl;
        }

        private static string ReadHeaders(Stream io, out byte[] leftover)
        {
            var acc = new MemoryStream();
            var one = new byte[1];
            int matched = 0;
            while (acc.Length < 65536)
            {
                int n = io.Read(one, 0, 1);
                if (n <= 0) break;
                acc.WriteByte(one[0]);

                if ((matched == 0 || matched == 2) && one[0] == 13) matched++;
                else if ((matched == 1 || matched == 3) && one[0] == 10) matched++;
                else matched = one[0] == 13 ? 1 : 0;
                if (matched == 4) break;
            }
            leftover = new byte[0];
            var all = acc.ToArray();
            if (matched != 4) throw new IOException("no HTTP header terminator");
            return Encoding.ASCII.GetString(all);
        }

        private static int StatusCode(string head)
        {
            var first = head.Split('\n')[0].Trim();
            var bits = first.Split(' ');
            int st; return (bits.Length > 1 && int.TryParse(bits[1], out st)) ? st : 0;
        }

        private static string HeaderValue(string head, string name)
        {
            foreach (var raw in head.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                int c = line.IndexOf(':');
                if (c <= 0) continue;
                if (string.Equals(line.Substring(0, c).Trim(), name, StringComparison.OrdinalIgnoreCase)) return line.Substring(c + 1).Trim();
            }
            return null;
        }

        private static long HeaderLong(string head, string name, long dflt)
        {
            string v = HeaderValue(head, name); long n;
            return (v != null && long.TryParse(v, out n)) ? n : dflt;
        }

        private static byte[] ReadExact(Stream s, int n)
        {
            var b = new byte[n]; int off = 0;
            while (off < n) { int got = s.Read(b, off, n - off); if (got <= 0) throw new IOException("SOCKS5: short read"); off += got; }
            return b;
        }

        private void Report(Stripe s) { var h = Progress; if (h != null) { try { h(s); } catch { } } }
        private void Emit(string s) { var h = Log; if (h != null) { try { h(s); } catch { } } }
    }
}
