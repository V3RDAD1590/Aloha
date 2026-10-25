using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace Aloha
{

    public class TorControl : IDisposable
    {
        private readonly string host;
        private readonly int port;
        private readonly string cookiePath;
        private TcpClient client;
        private NetworkStream stream;
        private StreamReader reader;
        public bool Authenticated { get; private set; }

        public string Password { get; set; }

        public string AuthMethod { get; private set; } = "";

        public string CookieFileUsed { get; private set; } = "";

        public string OfferedMethods { get; private set; } = "";

        public TorControl(string cookiePath, int controlPort = 9051, string host = "127.0.0.1")
        {
            this.host = host; this.port = controlPort; this.cookiePath = cookiePath;
        }

        public bool Connect(out string error)
        {
            error = null;
            try
            {
                client = new TcpClient();
                client.Connect(host, port);
                stream = client.GetStream();
                stream.ReadTimeout = 5000;

                reader = new StreamReader(stream, System.Text.Encoding.GetEncoding(28591), false);
            }
            catch (Exception ex)
            {
                error = "no Tor control port on " + host + ":" + port + " (" + ex.Message + ")";
                return false;
            }

            var methods = new List<string>();
            string torCookieFile = null;
            try
            {
                string pi = Send("PROTOCOLINFO 1");
                foreach (var raw in pi.Replace("\r", "").Split('\n'))
                {
                    string ln = raw.Trim();
                    int a = ln.IndexOf("AUTH METHODS=", StringComparison.Ordinal);
                    if (a < 0) continue;
                    string rest = ln.Substring(a + "AUTH METHODS=".Length);
                    int sp = rest.IndexOf(' ');
                    string list = sp >= 0 ? rest.Substring(0, sp) : rest;
                    foreach (var m in list.Split(',')) if (m.Length > 0) methods.Add(m.Trim().ToUpperInvariant());
                    int cf = rest.IndexOf("COOKIEFILE=", StringComparison.Ordinal);
                    if (cf >= 0) torCookieFile = UnquoteQString(rest.Substring(cf + "COOKIEFILE=".Length));
                }
            }
            catch {  }
            OfferedMethods = string.Join(",", methods.ToArray());

            byte[] cookie = null;
            string cookieErr = null;
            foreach (var p in new[] { torCookieFile, cookiePath })
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                try
                {
                    if (!File.Exists(p)) { cookieErr = "cookie file not found: " + p; continue; }
                    var b = File.ReadAllBytes(p);
                    if (b.Length != 32) { cookieErr = "cookie file is " + b.Length + " bytes, not 32: " + p; continue; }
                    cookie = b; CookieFileUsed = p; break;
                }
                catch (Exception ex) { cookieErr = "cookie unreadable (" + ex.Message + "): " + p; }
            }

            try
            {
                string reply;
                if (methods.Contains("NULL"))
                {
                    AuthMethod = "NULL";
                    reply = Send("AUTHENTICATE");
                }
                else if (methods.Contains("SAFECOOKIE") && cookie != null)
                {
                    AuthMethod = "SAFECOOKIE";
                    var clientNonce = new byte[32];
                    using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(clientNonce);
                    string ch = Send("AUTHCHALLENGE SAFECOOKIE " + Hex(clientNonce));
                    string serverHashHex = Field(ch, "SERVERHASH="), serverNonceHex = Field(ch, "SERVERNONCE=");
                    if (serverHashHex == null || serverNonceHex == null) { error = "control auth failed: bad AUTHCHALLENGE reply: " + ch; return false; }
                    byte[] serverNonce = Unhex(serverNonceHex);
                    byte[] msg = Concat(cookie, clientNonce, serverNonce);

                    string expectServer = Hex(Hmac("Tor safe cookie authentication server-to-controller hash", msg));
                    if (!string.Equals(expectServer, serverHashHex, StringComparison.OrdinalIgnoreCase))
                    { error = "control auth failed: SERVERHASH mismatch — this is not the Tor that owns " + CookieFileUsed; return false; }
                    string clientHash = Hex(Hmac("Tor safe cookie authentication controller-to-server hash", msg));
                    reply = Send("AUTHENTICATE " + clientHash);
                }
                else if ((methods.Contains("COOKIE") || methods.Count == 0) && cookie != null)
                {
                    AuthMethod = "COOKIE";
                    reply = Send("AUTHENTICATE " + Hex(cookie));
                }
                else if (methods.Contains("HASHEDPASSWORD") && !string.IsNullOrEmpty(Password))
                {
                    AuthMethod = "HASHEDPASSWORD";
                    reply = Send("AUTHENTICATE \"" + Password.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");
                }
                else
                {

                    var sb = new StringBuilder("control auth failed: Tor accepts [" + OfferedMethods + "]");
                    if (methods.Contains("COOKIE") || methods.Contains("SAFECOOKIE"))
                        sb.Append(" but no readable cookie — ").Append(cookieErr ?? "no cookie path known")
                          .Append(torCookieFile != null ? " (Tor says its cookie is " + torCookieFile + ")" : "");
                    if (methods.Contains("HASHEDPASSWORD")) sb.Append("; it wants a password and none is set");
                    error = sb.ToString();
                    return false;
                }

                if (reply.Contains("250 OK")) { Authenticated = true; return true; }
                error = "control auth failed (" + AuthMethod + (CookieFileUsed.Length > 0 ? ", cookie " + CookieFileUsed : "") + "): " + reply;
                return false;
            }
            catch (Exception ex)
            {
                error = "control auth failed (" + AuthMethod + "): " + ex.Message;
                return false;
            }
        }

        private static string Field(string reply, string key)
        {
            int i = reply.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return null;
            i += key.Length;
            int j = i;
            while (j < reply.Length && Uri.IsHexDigit(reply[j])) j++;
            return j > i ? reply.Substring(i, j - i) : null;
        }

        private static byte[] Hmac(string key, byte[] msg)
        {
            using (var h = new System.Security.Cryptography.HMACSHA256(Encoding.ASCII.GetBytes(key)))
                return h.ComputeHash(msg);
        }

        private static byte[] Concat(params byte[][] parts)
        {
            int n = 0; foreach (var p in parts) n += p.Length;
            var outp = new byte[n]; int o = 0;
            foreach (var p in parts) { Buffer.BlockCopy(p, 0, outp, o, p.Length); o += p.Length; }
            return outp;
        }

        private static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (byte x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        private static byte[] Unhex(string hex)
        {
            var b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return b;
        }

        private static string UnquoteQString(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Trim();
            if (s.Length < 2 || s[0] != '"') { int sp = s.IndexOf(' '); return sp > 0 ? s.Substring(0, sp) : s; }
            var sb = new StringBuilder();
            for (int i = 1; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"') break;
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    switch (n)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        case '\'': sb.Append('\''); break;
                        default:
                            if (n >= '0' && n <= '7')
                            {
                                int v = 0, k = 0;
                                while (k < 3 && i < s.Length && s[i] >= '0' && s[i] <= '7') { v = v * 8 + (s[i] - '0'); i++; k++; }
                                i--;
                                sb.Append((char)v);
                            }
                            else sb.Append(n);
                            break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private readonly object io = new object();

        private sealed class Reply
        {
            public readonly List<string> Lines = new List<string>();
            public readonly List<string> Data  = new List<string>();
            public string Final = "";                                   
            public string Code { get { return Final.Length >= 3 ? Final.Substring(0, 3) : ""; } }
            public bool Ok { get { return Code == "250"; } }
            public override string ToString()
            {
                var sb = new StringBuilder();
                foreach (var l in Lines) sb.AppendLine(l);
                return sb.ToString().TrimEnd();
            }

            public List<string> Values(string key)
            {
                var outp = new List<string>();
                string k = key + "=";
                foreach (var l in Lines)
                {
                    if (l.Length < 5) continue;
                    string body = l.Substring(4);
                    if (l[3] == '-' && body.StartsWith(k, StringComparison.Ordinal)) outp.Add(body.Substring(k.Length));
                    else if (l[3] == '+' && body.StartsWith(k, StringComparison.Ordinal)) outp.AddRange(Data);
                }
                return outp;
            }
        }

        private string Send(string cmd) { return SendReply(cmd).ToString(); }

        private Reply SendReply(string cmd)
        {
            lock (io)
            {
                var bytes = Encoding.ASCII.GetBytes(cmd + "\r\n");
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
                return ReadReply();
            }
        }

        private Reply ReadReply()
        {
            var r = new Reply();
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                bool status = line.Length >= 4 && char.IsDigit(line[0]) && char.IsDigit(line[1]) && char.IsDigit(line[2]);
                char sep = status ? line[3] : '\0';
                r.Lines.Add(line);
                if (status && sep == '+')
                {

                    string d;
                    while ((d = reader.ReadLine()) != null)
                    {
                        if (d == ".") break;
                        r.Data.Add(d.StartsWith("..") ? d.Substring(1) : d);
                    }
                    if (d == null) break;
                    continue;
                }
                if (status && sep == ' ') { r.Final = line; break; }

            }
            return r;
        }

        public static string CircuitAge(string timeCreated)
        {
            DateTime t;
            if (string.IsNullOrEmpty(timeCreated) ||
                !DateTime.TryParse(timeCreated, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out t)) return null;
            var d = DateTime.UtcNow - t;
            if (d.TotalSeconds < 0) d = TimeSpan.Zero;
            if (d.TotalMinutes < 1) return (int)d.TotalSeconds + "s";
            if (d.TotalHours < 1) return (int)d.TotalMinutes + "m";
            return (int)d.TotalHours + "h";
        }

        public static System.Threading.Tasks.Task<T> OnceAsync<T>(int port, string cookieHint, string password,
            Func<TorControl, T> fn, T fail, Action<string> onError = null)
        {
            return System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using (var c = new TorControl(cookieHint, port) { Password = password })
                    {
                        string err;
                        if (!c.Connect(out err)) { onError?.Invoke(err); return fail; }
                        return fn(c);
                    }
                }
                catch (Exception ex) { onError?.Invoke(ex.Message); return fail; }
            });
        }

        public string GetInfo(string key)
        {
            try
            {
                if (!Authenticated) return null;
                var r = SendReply("GETINFO " + key);
                if (!r.Ok) return null;
                var v = r.Values(key);
                return v.Count == 0 ? null : string.Join("\n", v.ToArray());
            }
            catch { return null; }
        }

        public int GetBootstrapPercent()
        {
            try
            {
                if (!Authenticated) return -1;
                string r = Send("GETINFO status/bootstrap-phase");
                int i = r.IndexOf("PROGRESS=", StringComparison.Ordinal);
                if (i < 0) return -1;
                int j = i + 9, k = j;
                while (k < r.Length && char.IsDigit(r[k])) k++;
                int pct; return int.TryParse(r.Substring(j, k - j), out pct) ? pct : -1;
            }
            catch { return -1; }
        }

        public bool NewIdentity(out string error)
        {
            error = null;
            try
            {
                if (!Authenticated) { error = "not authenticated"; return false; }
                var r = Send("SIGNAL NEWNYM");
                if (r.Contains("250 OK")) return true;
                error = r; return false;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public void ClearDnsCache()
        {
            try { if (Authenticated) Send("SIGNAL CLEARDNSCACHE"); } catch { }
        }

        public class Circuit
        {
            public string Id;
            public string Status;
            public string Purpose;
            public List<string> Relays = new List<string>();

            public List<string> Fingerprints = new List<string>();
            public string RendQuery = "";   
            public string TimeCreated = ""; 
            public string BuildFlags = "";  
        }

        public List<Circuit> GetCircuits(out string error)
        {
            error = null;
            var outp = new List<Circuit>();
            try
            {
                if (!Authenticated) { error = "not authenticated"; return outp; }
                var rep = SendReply("GETINFO circuit-status");
                if (!rep.Ok) { error = rep.Final; return outp; }

                foreach (var raw in rep.Values("circuit-status"))
                {
                    string ln = raw.Trim();
                    if (ln.Length == 0) continue;

                    var tok = ln.Split(' ');
                    if (tok.Length < 2) continue;
                    var c = new Circuit { Id = tok[0], Status = tok[1] };
                    if (tok.Length >= 3 && (tok[2].Contains("~") || tok[2].Contains("$")))
                    {
                        foreach (var hop in tok[2].Split(','))
                        {

                            int sep = hop.IndexOfAny(new[] { '~', '=' });
                            c.Relays.Add(sep >= 0 ? hop.Substring(sep + 1) : hop);
                            string fp = "";
                            if (hop.StartsWith("$"))
                            {
                                int end = sep >= 0 ? sep : hop.Length;
                                if (end > 1) fp = hop.Substring(1, end - 1).ToUpperInvariant();
                            }
                            c.Fingerprints.Add(fp);
                        }
                    }
                    foreach (var t in tok)
                    {
                        if (t.StartsWith("PURPOSE=")) c.Purpose = t.Substring("PURPOSE=".Length);
                        else if (t.StartsWith("REND_QUERY=")) c.RendQuery = t.Substring("REND_QUERY=".Length).ToLowerInvariant();
                        else if (t.StartsWith("TIME_CREATED=")) c.TimeCreated = t.Substring("TIME_CREATED=".Length);
                        else if (t.StartsWith("BUILD_FLAGS=")) c.BuildFlags = t.Substring("BUILD_FLAGS=".Length);
                    }
                    outp.Add(c);
                }
            }
            catch (Exception ex) { error = ex.Message; }
            return outp;
        }

        public class Stream
        {
            public string Id;
            public string Status;
            public string CircuitId;
            public string Target;
            public string Purpose;
            public string SourceAddr;
        }

        public List<Stream> GetStreams(out string error)
        {
            error = null;
            var outp = new List<Stream>();
            try
            {
                if (!Authenticated) { error = "not authenticated"; return outp; }
                var rep = SendReply("GETINFO stream-status");
                if (!rep.Ok) { error = rep.Final; return outp; }
                foreach (var raw in rep.Values("stream-status"))
                {
                    string ln = raw.Trim();
                    if (ln.Length == 0) continue;
                    var tok = ln.Split(' ');
                    if (tok.Length < 4) continue;
                    var s = new Stream { Id = tok[0], Status = tok[1], CircuitId = tok[2], Target = tok[3] };
                    for (int i = 4; i < tok.Length; i++)
                    {
                        if (tok[i].StartsWith("PURPOSE=")) s.Purpose = tok[i].Substring(8);
                        else if (tok[i].StartsWith("SOURCE_ADDR=")) s.SourceAddr = tok[i].Substring(12);
                    }
                    outp.Add(s);
                }
            }
            catch (Exception ex) { error = ex.Message; }
            return outp;
        }

        public bool ExtendCircuit(string path, out string circuitId, out string error)
        {
            circuitId = null; error = null;
            try
            {
                if (!Authenticated) { error = "not authenticated"; return false; }
                var r = Send("EXTENDCIRCUIT 0 " + path + " purpose=general");
                int i = r.IndexOf("EXTENDED ", StringComparison.Ordinal);
                if (r.StartsWith("250") && i >= 0)
                {
                    int j = i + 9, k = j;
                    while (k < r.Length && char.IsDigit(r[k])) k++;
                    circuitId = r.Substring(j, k - j);
                    return circuitId.Length > 0;
                }
                error = r; return false;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public bool CloseCircuit(string circuitId, out string error)
        {
            error = null;
            try
            {
                if (!Authenticated) { error = "not authenticated"; return false; }
                var r = Send("CLOSECIRCUIT " + circuitId);
                if (r.Contains("250")) return true;
                error = r; return false;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public bool AddOnion(string keySpec, IEnumerable<string> ports, bool detach, out string serviceId, out string privateKey, out string error)
        {
            serviceId = null; privateKey = null; error = null;
            try
            {
                if (!Authenticated) { error = "not authenticated"; return false; }
                var sb = new StringBuilder("ADD_ONION ").Append(keySpec);
                if (detach) sb.Append(" Flags=Detach");
                int n = 0;
                foreach (var p in ports) { if (!string.IsNullOrWhiteSpace(p)) { sb.Append(" Port=").Append(p.Trim()); n++; } }
                if (n == 0) { error = "no Port= mapping"; return false; }
                var r = Send(sb.ToString());
                foreach (var raw in r.Replace("\r", "").Split('\n'))
                {
                    string ln = raw.Trim();
                    int i = ln.IndexOf("ServiceID=", StringComparison.Ordinal);
                    if (i >= 0) serviceId = ln.Substring(i + 10).Trim();
                    i = ln.IndexOf("PrivateKey=", StringComparison.Ordinal);
                    if (i >= 0) privateKey = ln.Substring(i + 11).Trim();
                }
                if (r.Contains("250 OK") && !string.IsNullOrEmpty(serviceId)) return true;
                error = r; return false;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public bool DelOnion(string serviceId, out string error)
        {
            error = null;
            try
            {
                if (!Authenticated) { error = "not authenticated"; return false; }
                var r = Send("DEL_ONION " + serviceId);
                if (r.Contains("250 OK")) return true;
                error = r; return false;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public List<string> GetOnions(out string error)
        {
            error = null;
            var outp = new List<string>();
            try
            {
                if (!Authenticated) { error = "not authenticated"; return outp; }
                foreach (var key in new[] { "onions/current", "onions/detached" })
                {
                    var rep = SendReply("GETINFO " + key);
                    if (!rep.Ok) continue;
                    foreach (var raw in rep.Values(key))
                    {
                        string ln = raw.Trim();
                        if (ln.Length >= 16 && !outp.Contains(ln)) outp.Add(ln);
                    }
                }
            }
            catch (Exception ex) { error = ex.Message; }
            return outp;
        }

        public string GetBootstrapPhase()
        {
            try
            {
                if (!Authenticated) return "";
                string r = Send("GETINFO status/bootstrap-phase");
                int i = r.IndexOf("SUMMARY=", StringComparison.Ordinal);
                if (i >= 0)
                {
                    int q1 = r.IndexOf('"', i), q2 = q1 >= 0 ? r.IndexOf('"', q1 + 1) : -1;
                    if (q1 >= 0 && q2 > q1) return r.Substring(q1 + 1, q2 - q1 - 1);
                }
            }
            catch { }
            return "";
        }

        public bool GetTraffic(out long read, out long written, out string error)
        {
            read = 0; written = 0; error = null;
            try
            {
                if (!Authenticated) { error = "not authenticated"; return false; }
                read    = InfoLong("traffic/read");
                written = InfoLong("traffic/written");
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private long InfoLong(string key)
        {
            var rep = SendReply("GETINFO " + key);
            if (!rep.Ok) return 0;
            foreach (var v in rep.Values(key)) { long n; if (long.TryParse(v.Trim(), out n)) return n; }
            return 0;
        }

        public class Relay
        {
            public string Nick;
            public string Fingerprint;
            public string Ip;
            public int    ORPort;
            public List<string> Flags = new List<string>();
            public long   BandwidthKb;
            public string Country = "??";  
            public bool IsExit   { get { return Flags.Contains("Exit"); } }
            public bool IsGuard  { get { return Flags.Contains("Guard"); } }
            public bool IsFast   { get { return Flags.Contains("Fast"); } }
            public bool IsStable { get { return Flags.Contains("Stable"); } }
        }

        public List<Relay> GetAllRelays(out string error)
        {
            error = null;
            var outp = new List<Relay>();
            try
            {
                if (!Authenticated) { error = "not authenticated"; return outp; }
                var rep = SendReply("GETINFO ns/all");   
                if (!rep.Ok) { error = rep.Final; return outp; }
                Relay cur = null;
                foreach (var raw in rep.Values("ns/all"))
                {
                    if (raw.Length == 0) continue;
                    char t = raw[0];
                    if (t == 'r' && raw.Length > 1 && raw[1] == ' ')
                    {
                        if (cur != null) outp.Add(cur);
                        var tk = raw.Split(' ');
                        cur = new Relay();
                        if (tk.Length >= 8)
                        {
                            cur.Nick        = tk[1];
                            cur.Fingerprint = B64ToHex(tk[2]);
                            cur.Ip          = tk[6];
                            int op; if (int.TryParse(tk[7], out op)) cur.ORPort = op;
                        }
                    }
                    else if (t == 's' && cur != null && raw.Length > 1 && raw[1] == ' ')
                    {
                        foreach (var fl in raw.Substring(2).Split(' '))
                            if (fl.Length > 0) cur.Flags.Add(fl);
                    }
                    else if (t == 'w' && cur != null && raw.StartsWith("w "))
                    {
                        foreach (var tok in raw.Substring(2).Split(' '))
                            if (tok.StartsWith("Bandwidth="))
                            { long bw; if (long.TryParse(tok.Substring(10), out bw)) cur.BandwidthKb = bw; }
                    }
                }
                if (cur != null) outp.Add(cur);
            }
            catch (Exception ex) { error = ex.Message; }
            return outp;
        }

        public bool SetConf(string options, out string error)
        {
            error = null;
            try
            {
                if (!Authenticated) { error = "not authenticated"; return false; }
                var r = Send("SETCONF " + options);
                if (r.Contains("250 OK")) return true;
                error = r; return false;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public bool ResetConf(string keys, out string error)
        {
            error = null;
            try
            {
                if (!Authenticated) { error = "not authenticated"; return false; }
                var r = Send("RESETCONF " + keys);
                if (r.Contains("250 OK")) return true;
                error = r; return false;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public bool SaveConf(out string error)
        {
            error = null;
            try
            {
                if (!Authenticated) { error = "not authenticated"; return false; }
                var r = Send("SAVECONF");
                if (r.Contains("250")) return true;
                error = r; return false;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public bool MapAddress(string from, string to, out string error)
        {
            error = null;
            try
            {
                if (!Authenticated) { error = "not authenticated"; return false; }
                var r = Send("MAPADDRESS " + from + "=" + to);
                if (r.Contains("250")) return true;
                error = r; return false;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private static string B64ToHex(string b64)
        {
            try
            {
                string s = b64; int pad = s.Length % 4; if (pad > 0) s += new string('=', 4 - pad);
                byte[] by = Convert.FromBase64String(s);
                var sb = new StringBuilder(by.Length * 2);
                foreach (var b in by) sb.Append(b.ToString("x2"));
                return sb.ToString().ToUpperInvariant();
            }
            catch { return ""; }
        }

        public void Dispose()
        {
            try { reader?.Dispose(); } catch { }
            try { stream?.Dispose(); } catch { }
            try { client?.Close(); } catch { }
            client = null; Authenticated = false;
        }
    }
}
