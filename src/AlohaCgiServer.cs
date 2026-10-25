using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Aloha
{

    public class AlohaCgiServer : IDisposable
    {
        public class Mount
        {
            public string Name;
            public string Dir;
            public bool   Isolated;
            public int    Port;
            public string BaseUrl;
            public string Status = "stopped";
            internal TcpListener Listener;
        }

        private readonly List<Mount> mounts = new List<Mount>();
        private readonly object gate = new object();

        private TcpListener sharedListener;
        private int sharedPort = 0;
        private volatile bool disposed;

        private const int SharedBase = 8800;
        private int nextIsolated = 8801;

        public IReadOnlyList<Mount> Mounts { get { lock (gate) return mounts.ToArray(); } }
        public event Action Changed;

        private static readonly Dictionary<string, string> Interp =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".py",  "python \"{s}\"" },
            { ".php", "php \"{s}\"" },
            { ".pl",  "perl \"{s}\"" },
            { ".rb",  "ruby \"{s}\"" },
            { ".js",  "node \"{s}\"" },
            { ".jar", "java -jar \"{s}\"" },
            { ".class", "java {n}" },
            { ".exe", null }, { ".bat", null }, { ".cmd", null },
        };

        public static bool IsScript(string ext) { return Interp.ContainsKey(ext ?? ""); }

        public Mount AddMount(string dir, bool isolated, string name = null)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                throw new DirectoryNotFoundException("no such directory: " + dir);

            Mount m;
            lock (gate)
            {
                if (string.IsNullOrEmpty(name)) name = Slug(new DirectoryInfo(dir.TrimEnd('\\', '/')).Name);
                name = UniqueName(name);
                m = new Mount { Name = name, Dir = Path.GetFullPath(dir), Isolated = isolated };
                mounts.Add(m);
                try { StartMount(m); }
                catch (Exception ex) { m.Status = "error: " + ex.Message; }
            }
            Raise();
            return m;
        }

        public void RemoveMount(string name)
        {
            lock (gate)
            {
                var m = mounts.Find(x => x.Name == name);
                if (m == null) return;
                mounts.Remove(m);
                if (m.Isolated) { try { m.Listener?.Stop(); } catch { } }
                else RebuildShared();
            }
            Raise();
        }

        public int AutoMountRoot(string root, bool isolated)
        {
            int added = 0;
            try
            {
                if (!Directory.Exists(root)) { Directory.CreateDirectory(root); return 0; }
                foreach (var d in Directory.GetDirectories(root))
                {
                    string fp = Path.GetFullPath(d);
                    bool have;
                    lock (gate) have = mounts.Exists(x => string.Equals(x.Dir, fp, StringComparison.OrdinalIgnoreCase));
                    if (have) continue;
                    try { AddMount(d, isolated); added++; } catch { }
                }
            }
            catch { }
            return added;
        }

        private void StartMount(Mount m)
        {
            if (m.Isolated)
            {
                var l = BindFree(nextIsolated);
                nextIsolated = ((IPEndPoint)l.LocalEndpoint).Port + 1;
                m.Listener = l;
                m.Port = ((IPEndPoint)l.LocalEndpoint).Port;
                m.BaseUrl = "http://127.0.0.1:" + m.Port + "/";
                m.Status = "running";
                Accept(l, m);
            }
            else
            {
                EnsureShared();
                m.Port = sharedPort;
                m.BaseUrl = "http://127.0.0.1:" + sharedPort + "/" + m.Name + "/";
                m.Status = "running";
            }
        }

        private void EnsureShared()
        {
            if (sharedListener != null) return;
            sharedListener = BindFree(SharedBase);
            sharedPort = ((IPEndPoint)sharedListener.LocalEndpoint).Port;
            Accept(sharedListener, null);
        }

        private void RebuildShared()
        {
            foreach (var m in mounts)
                if (!m.Isolated && sharedListener != null)
                    m.BaseUrl = "http://127.0.0.1:" + sharedPort + "/" + m.Name + "/";
        }

        private static TcpListener BindFree(int start)
        {
            Exception last = null;
            for (int p = start; p < start + 200; p++)
            {
                try
                {
                    var l = new TcpListener(IPAddress.Loopback, p);
                    l.Start();
                    return l;
                }
                catch (Exception ex) { last = ex; }
            }
            throw new IOException("no free loopback port from " + start, last);
        }

        private void Accept(TcpListener l, Mount fixedMount)
        {
            try
            {
                l.BeginAcceptTcpClient(ar =>
                {
                    TcpClient c = null;
                    try { c = l.EndAcceptTcpClient(ar); }
                    catch { return; }
                    if (!disposed) Accept(l, fixedMount);
                    if (c != null) ThreadPool.QueueUserWorkItem(_ => Serve(c, fixedMount));
                }, null);
            }
            catch { }
        }

        private void Serve(TcpClient c, Mount fixedMount)
        {
            try
            {
                using (c)
                using (var ns = c.GetStream())
                {
                    c.ReceiveTimeout = 15000;
                    c.SendTimeout = 30000;

                    string method, rawUrl, ver;
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (!ReadRequestHead(ns, out method, out rawUrl, out ver, headers)) return;

                    byte[] body = new byte[0];
                    string cl;
                    if (headers.TryGetValue("Content-Length", out cl))
                    {
                        int n;
                        if (int.TryParse(cl, out n) && n > 0 && n < 64 * 1024 * 1024)
                            body = ReadExactly(ns, n);
                    }

                    string path = rawUrl, query = "";
                    int q = rawUrl.IndexOf('?');
                    if (q >= 0) { path = rawUrl.Substring(0, q); query = rawUrl.Substring(q + 1); }

                    Mount m = fixedMount;
                    string urlBase = "/";
                    if (m == null)
                    {
                        string bare = path.TrimStart('/');
                        int sl = bare.IndexOf('/');
                        string first = sl < 0 ? bare : bare.Substring(0, sl);
                        lock (gate) m = mounts.Find(x => !x.Isolated && x.Name == first);
                        if (m == null)
                        {
                            Respond(ns, 404, "text/plain; charset=utf-8",
                                    Encoding.UTF8.GetBytes("no such mount: " + first));
                            return;
                        }
                        urlBase = "/" + m.Name + "/";
                    }

                    Handle(ns, m, urlBase, method, rawUrl, path, query, headers, body);
                }
            }
            catch { }
        }

        private static bool ReadRequestHead(Stream s, out string method, out string rawUrl,
                                            out string ver, Dictionary<string, string> headers)
        {
            method = rawUrl = ver = null;
            var head = new MemoryStream();
            int state = 0;
            var one = new byte[1];
            while (head.Length < 64 * 1024)
            {
                int got = s.Read(one, 0, 1);
                if (got <= 0) return false;
                head.WriteByte(one[0]);
                if (one[0] == '\n') { if (state == 1) break; state = 1; }
                else if (one[0] != '\r') state = 0;
            }

            var text = Encoding.ASCII.GetString(head.ToArray());
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            if (lines.Length == 0) return false;

            var parts = lines[0].Split(' ');
            if (parts.Length < 2) return false;
            method = parts[0];
            rawUrl = parts[1];
            ver = parts.Length > 2 ? parts[2] : "HTTP/1.0";

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0) break;
                int c = line.IndexOf(':');
                if (c <= 0) continue;
                headers[line.Substring(0, c).Trim()] = line.Substring(c + 1).Trim();
            }
            return true;
        }

        private static byte[] ReadExactly(Stream s, int n)
        {
            var b = new byte[n];
            int off = 0;
            while (off < n)
            {
                int got = s.Read(b, off, n - off);
                if (got <= 0) break;
                off += got;
            }
            return b;
        }

        private void Handle(Stream ns, Mount m, string urlBase, string method, string rawUrl,
                            string path, string query, Dictionary<string, string> headers, byte[] body)
        {
            try
            {
                string rel = path.StartsWith(urlBase) ? path.Substring(urlBase.Length) : path.TrimStart('/');
                rel = Uri.UnescapeDataString(rel);

                string full = Path.GetFullPath(Path.Combine(m.Dir, rel.Replace('/', Path.DirectorySeparatorChar)));
                string rootSep = m.Dir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!string.Equals(full, m.Dir, StringComparison.OrdinalIgnoreCase)
                    && !full.StartsWith(rootSep, StringComparison.OrdinalIgnoreCase))
                { Respond(ns, 403, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("forbidden")); return; }

                if (rel.Length == 0 || Directory.Exists(full))
                {
                    string dir = rel.Length == 0 ? m.Dir : full;

                    bool forceList = (query ?? "").Split('&').Contains("_ls");

                    if (!forceList)
                    {
                        string idx = FindIndex(dir);
                        if (idx != null) { RunOrServe(ns, m, idx, urlBase, method, rawUrl, path, query, headers, body); return; }
                    }
                    Respond(ns, 200, "text/html; charset=utf-8",
                            Encoding.UTF8.GetBytes(AutoIndex(m, dir, urlBase, rel)));
                    return;
                }

                if (!File.Exists(full))
                { Respond(ns, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("not found: " + rel)); return; }

                RunOrServe(ns, m, full, urlBase, method, rawUrl, path, query, headers, body);
            }
            catch (Exception ex)
            {
                try { Respond(ns, 500, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("aloha-cgi: " + ex.Message)); }
                catch { }
            }
        }

        private void RunOrServe(Stream ns, Mount m, string file, string urlBase, string method, string rawUrl,
                                string path, string query, Dictionary<string, string> headers, byte[] body)
        {

            if ((query ?? "").Split('&').Contains("_src"))
            {
                try { Respond(ns, 200, "text/plain; charset=utf-8", File.ReadAllBytes(file)); }
                catch (Exception ex) { Respond(ns, 500, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(ex.Message)); }
                return;
            }

            if (Interp.ContainsKey(Path.GetExtension(file)))
                RunCgi(ns, m, file, urlBase, method, rawUrl, path, query, headers, body);
            else
                ServeStatic(ns, file);
        }

        private void RunCgi(Stream ns, Mount m, string script, string urlBase, string method, string rawUrl,
                            string path, string query, Dictionary<string, string> headers, byte[] body)
        {
            string ext = Path.GetExtension(script);
            string tmpl = Interp[ext];
            string dir = Path.GetDirectoryName(script);

            var psi = new ProcessStartInfo
            {
                WorkingDirectory = dir,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            if (tmpl == null) psi.FileName = script;
            else
            {
                string cmd = tmpl.Replace("{s}", script)
                                 .Replace("{d}", dir)
                                 .Replace("{n}", Path.GetFileNameWithoutExtension(script));
                int sp = cmd.IndexOf(' ');
                psi.FileName  = sp < 0 ? cmd : cmd.Substring(0, sp);
                psi.Arguments = sp < 0 ? "" : cmd.Substring(sp + 1);
            }

            var e = psi.EnvironmentVariables;
            e["GATEWAY_INTERFACE"] = "CGI/1.1";
            e["SERVER_SOFTWARE"]   = "Aloha-CGI/1";
            e["SERVER_PROTOCOL"]   = "HTTP/1.1";
            e["SERVER_NAME"]       = "127.0.0.1";
            e["SERVER_PORT"]       = m.Port.ToString();
            e["REMOTE_ADDR"]       = "127.0.0.1";
            e["REQUEST_METHOD"]    = method;
            e["QUERY_STRING"]      = query ?? "";
            e["SCRIPT_NAME"]       = urlBase + Path.GetFileName(script);
            e["SCRIPT_FILENAME"]   = script;
            e["REQUEST_URI"]       = rawUrl;
            e["PATH_INFO"]         = path;
            e["PYTHONUNBUFFERED"]  = "1";
            e["PYTHONIOENCODING"]  = "utf-8";
            foreach (var kv in headers)
            {
                string k = kv.Key.ToUpperInvariant().Replace('-', '_');
                if (k == "CONTENT_TYPE" || k == "CONTENT_LENGTH") e[k] = kv.Value;
                else e["HTTP_" + k] = kv.Value;
            }

            byte[] outBytes;
            string errText = "";
            try
            {
                using (var p = Process.Start(psi))
                {
                    try { ProcessLeash.Leash(p); } catch { }

                    string errCap = "";
                    var errT = new Thread(() => { try { errCap = p.StandardError.ReadToEnd(); } catch { } });
                    errT.IsBackground = true;
                    errT.Start();

                    try { if (body != null && body.Length > 0) p.StandardInput.BaseStream.Write(body, 0, body.Length); }
                    catch { }
                    finally { try { p.StandardInput.Close(); } catch { } }

                    using (var ms = new MemoryStream())
                    {
                        p.StandardOutput.BaseStream.CopyTo(ms);
                        outBytes = ms.ToArray();
                    }
                    if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } }
                    errT.Join(2000);
                    errText = errCap;
                }
            }
            catch (Exception ex)
            {
                Respond(ns, 500, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(
                    "aloha-cgi could not run: " + psi.FileName + "\n\n" + ex.Message +
                    "\n\nIs the interpreter installed and on PATH?"));
                return;
            }

            if (outBytes.Length == 0)
            {
                Respond(ns, 500, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(
                    "script produced no output.\n\nstderr:\n" + errText));
                return;
            }

            EmitCgi(ns, outBytes);
        }

        private void EmitCgi(Stream ns, byte[] outBytes)
        {
            int split, skip;
            FindHeaderEnd(outBytes, out split, out skip);

            if (split < 0)
            {
                Respond(ns, 200, "text/html; charset=utf-8", outBytes);
                return;
            }

            int code = 200;
            string ctype = null;
            var extra = new List<string>();
            foreach (var lineRaw in Encoding.UTF8.GetString(outBytes, 0, split).Split('\n'))
            {
                string line = lineRaw.TrimEnd('\r');
                if (line.Length == 0) continue;
                int c = line.IndexOf(':');
                if (c < 0) continue;
                string k = line.Substring(0, c).Trim(), v = line.Substring(c + 1).Trim();
                if (k.Equals("Status", StringComparison.OrdinalIgnoreCase))
                {
                    int sp = v.IndexOf(' ');
                    int n; int.TryParse(sp < 0 ? v : v.Substring(0, sp), out n);
                    if (n >= 100) code = n;
                }
                else if (k.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) ctype = v;
                else if (!k.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) extra.Add(k + ": " + v);
            }

            var bodyBytes = new byte[outBytes.Length - split - skip];
            Buffer.BlockCopy(outBytes, split + skip, bodyBytes, 0, bodyBytes.Length);
            Respond(ns, code, ctype ?? "text/html; charset=utf-8", bodyBytes, extra);
        }

        private void ServeStatic(Stream ns, string file)
        {
            try
            {
                byte[] data = File.ReadAllBytes(file);
                Respond(ns, 200, MimeOf(Path.GetExtension(file)), data);
            }
            catch (Exception ex)
            { Respond(ns, 500, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(ex.Message)); }
        }

        private string AutoIndex(Mount m, string dir, string urlBase, string rel)
        {
            var sb = new StringBuilder();
            sb.Append("<!doctype html><meta charset=utf-8><title>index of /").Append(m.Name).Append("</title>");
            sb.Append("<style>body{background:#000;color:#2ee05a;font:13px Consolas,monospace;padding:16px}"
                    + "a{color:#2ee05a;text-decoration:none}a:hover{background:#10381c}"
                    + "h2{color:#7dffb0;border-bottom:1px solid #14602a;padding-bottom:6px}"
                    + ".d{color:#7dffb0}.t{color:#14a03a}</style>");
            sb.Append("<h2>aloha-cgi &mdash; index of /").Append(WebUtility.HtmlEncode(m.Name));
            if (rel.Length > 0) sb.Append('/').Append(WebUtility.HtmlEncode(rel.TrimEnd('/')));
            sb.Append("</h2><pre>");
            try
            {
                foreach (var d in Directory.GetDirectories(dir))
                    sb.Append("<a class=d href=\"").Append(RelUrl(m.Dir, urlBase, d)).Append("/\">")
                      .Append(WebUtility.HtmlEncode(Path.GetFileName(d))).Append("/</a>\n");
                foreach (var f in Directory.GetFiles(dir))
                {
                    string href = RelUrl(m.Dir, urlBase, f);
                    bool isCgi = Interp.ContainsKey(Path.GetExtension(f));
                    sb.Append("<a href=\"").Append(href).Append("\">")
                      .Append(WebUtility.HtmlEncode(Path.GetFileName(f))).Append("</a>");
                    if (isCgi)
                        sb.Append("  <span class=t>[cgi]</span>  <a class=t href=\"")
                          .Append(href).Append("?_src\">[src]</a>");
                    sb.Append('\n');
                }
            }
            catch (Exception ex) { sb.Append(WebUtility.HtmlEncode(ex.Message)); }
            sb.Append("</pre>");
            return sb.ToString();
        }

        private static string RelUrl(string root, string urlBase, string full)
        {
            string r = full.Substring(root.Length).Replace('\\', '/').TrimStart('/');
            return urlBase + Uri.EscapeUriString(r);
        }

        private static void Respond(Stream ns, int code, string contentType, byte[] body,
                                    List<string> extraHeaders = null)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("HTTP/1.1 ").Append(code).Append(' ').Append(Reason(code)).Append("\r\n");
                sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
                sb.Append("Content-Length: ").Append(body == null ? 0 : body.Length).Append("\r\n");
                sb.Append("Server: Aloha-CGI/1\r\n");
                sb.Append("Cache-Control: no-store\r\n");
                if (extraHeaders != null) foreach (var h in extraHeaders) sb.Append(h).Append("\r\n");
                sb.Append("Connection: close\r\n\r\n");

                var head = Encoding.ASCII.GetBytes(sb.ToString());
                ns.Write(head, 0, head.Length);
                if (body != null && body.Length > 0) ns.Write(body, 0, body.Length);
                ns.Flush();
            }
            catch { }
        }

        private static string Reason(int c)
        {
            switch (c)
            {
                case 200: return "OK";
                case 302: return "Found";
                case 304: return "Not Modified";
                case 400: return "Bad Request";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 500: return "Internal Server Error";
                default:  return "OK";
            }
        }

        private static string FindIndex(string dir)
        {
            foreach (var n in new[] { "index.py", "index.php", "index.js", "index.rb", "index.pl", "index.html", "index.htm" })
            {
                string p = Path.Combine(dir, n);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        private static void FindHeaderEnd(byte[] b, out int split, out int skip)
        {
            for (int i = 0; i < b.Length - 1; i++)
            {
                if (i < b.Length - 3 && b[i] == (byte)'\r' && b[i + 1] == (byte)'\n'
                    && b[i + 2] == (byte)'\r' && b[i + 3] == (byte)'\n') { split = i; skip = 4; return; }
                if (b[i] == (byte)'\n' && b[i + 1] == (byte)'\n') { split = i; skip = 2; return; }
            }
            split = -1; skip = 0;
        }

        private static string MimeOf(string ext)
        {
            switch ((ext ?? "").ToLowerInvariant())
            {
                case ".html": case ".htm": return "text/html; charset=utf-8";
                case ".css":  return "text/css; charset=utf-8";
                case ".js":   return "application/javascript; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".png":  return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".gif":  return "image/gif";
                case ".svg":  return "image/svg+xml";
                case ".ico":  return "image/x-icon";
                case ".txt":  return "text/plain; charset=utf-8";
                case ".wasm": return "application/wasm";
                default:      return "application/octet-stream";
            }
        }

        private static string Slug(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in (s ?? "").ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(c) ? c : '-');
            string r = sb.ToString().Trim('-');
            while (r.Contains("--")) r = r.Replace("--", "-");
            return r.Length == 0 ? "app" : r;
        }

        private string UniqueName(string name)
        {
            string n = name; int i = 2;
            while (mounts.Exists(x => x.Name == n)) n = name + "-" + (i++);
            return n;
        }

        private void Raise() { try { Changed?.Invoke(); } catch { } }

        public void Dispose()
        {
            disposed = true;
            lock (gate)
            {
                foreach (var m in mounts) if (m.Isolated) { try { m.Listener?.Stop(); } catch { } }
                try { sharedListener?.Stop(); } catch { }
                mounts.Clear();
            }
        }
    }
}
