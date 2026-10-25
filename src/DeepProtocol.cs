using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace Aloha
{

    public static class DeepProtocol
    {
        public const string FolderName = "deep";
        public const string ExeName = "deep.exe";
        public const string VirtualSuffix = ".deep.aloha";

        public static string FindExe(NetConfig cfg)
        {
            try
            {
                if (cfg != null && !string.IsNullOrWhiteSpace(cfg.DeepExe) && File.Exists(cfg.DeepExe)) return cfg.DeepExe;
                foreach (var dir in new[] { Path.GetDirectoryName(Application.ExecutablePath), Environment.CurrentDirectory, AlohaPaths.Root })
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    string p = Path.Combine(dir, FolderName, "bin", ExeName);
                    if (File.Exists(p)) return p;
                    p = Path.Combine(dir, FolderName, ExeName);
                    if (File.Exists(p)) return p;
                }
            }
            catch { }
            return null;
        }

        public static string FindConfig(NetConfig cfg, string exe)
        {
            try
            {
                if (cfg != null && !string.IsNullOrWhiteSpace(cfg.DeepConfig) && File.Exists(cfg.DeepConfig)) return cfg.DeepConfig;
                if (exe != null)
                {
                    string p = Path.Combine(Path.GetDirectoryName(exe), "config.json");
                    if (File.Exists(p)) return p;
                }
            }
            catch { }
            return null;
        }

        public static bool IsVirtual(string uri)
        {
            Uri u;
            return !string.IsNullOrEmpty(uri) && Uri.TryCreate(uri, UriKind.Absolute, out u)
                && u.Host.EndsWith(VirtualSuffix, StringComparison.OrdinalIgnoreCase) && u.Host.Length > VirtualSuffix.Length;
        }

        public static string ToVirtual(string typed)
        {
            if (string.IsNullOrWhiteSpace(typed)) return null;
            string rest = typed.Trim();
            if (rest.StartsWith("deep://", StringComparison.OrdinalIgnoreCase)) rest = rest.Substring(7);
            else if (rest.StartsWith("deep:", StringComparison.OrdinalIgnoreCase)) rest = rest.Substring(5).TrimStart('/');
            else return null;
            int slash = rest.IndexOf('/');
            string auth = slash >= 0 ? rest.Substring(0, slash) : rest;
            string path = slash >= 0 ? rest.Substring(slash) : "/";
            auth = auth.Trim().ToLowerInvariant();
            if (auth.Length == 0 || auth.IndexOf(' ') >= 0 || auth.IndexOf('@') >= 0) return null;
            return "https://" + auth + VirtualSuffix + path;
        }

        public static string Display(string shown)
        {
            if (string.IsNullOrEmpty(shown) || !IsVirtual(shown)) return shown;
            try
            {
                var u = new Uri(shown);
                string auth = u.Host.Substring(0, u.Host.Length - VirtualSuffix.Length);
                return "deep://" + auth + u.PathAndQuery + u.Fragment;
            }
            catch { return shown; }
        }

        public static string ToDeepUri(Uri virtualUri)
        {
            string auth = virtualUri.Host.Substring(0, virtualUri.Host.Length - VirtualSuffix.Length);
            return "deep://" + auth + virtualUri.PathAndQuery;
        }

        public class Result
        {
            public int Status = 502;
            public string Reason = "Bad Gateway";
            public string MediaType;
            public List<KeyValuePair<string, string>> Headers = new List<KeyValuePair<string, string>>();
            public byte[] Body = new byte[0];
            public string Error;
        }

        public static Result Request(NetConfig cfg, string deepUri, string method, List<KeyValuePair<string, string>> headers, byte[] body)
        {
            var r = new Result();
            string exe = FindExe(cfg);
            if (exe == null) return Fail(r, 502, "deep.exe was not found. Unzip a DEEP release into " + FolderName + "\\ beside Aloha.exe (" + FolderName + "\\bin\\deep.exe).");
            string config = FindConfig(cfg, exe);
            if (config == null) return Fail(r, 502, "no config.json next to deep.exe (deep init / client-init writes one; set DeepConfig in AlohaNet.json to use another).");
            int timeout = cfg != null && cfg.DeepTimeoutSec > 0 ? cfg.DeepTimeoutSec : 35;

            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(exe)
            };

            psi.Arguments = "request " + Quote(deepUri) + " --config " + Quote(config) + " --info";
            Process p = null;
            try
            {
                p = Process.Start(psi);
                try { ProcessLeash.Leash(p); } catch { }

                var outTask = Task.Run(() => ReadAll(p.StandardOutput.BaseStream, 17 * 1024 * 1024));
                var errTask = Task.Run(() => ReadAll(p.StandardError.BaseStream, 1024 * 1024));
                using (var stdin = p.StandardInput.BaseStream)
                {
                    var req = Encoding.UTF8.GetBytes(RequestJson(method, headers, body));
                    stdin.Write(req, 0, req.Length);
                }
                if (!p.WaitForExit(timeout * 1000))
                {
                    try { p.Kill(); } catch { }
                    return Fail(r, 504, "deep.exe did not answer within " + timeout + " s (" + deepUri + ").");
                }
                Task.WaitAll(new Task[] { outTask, errTask }, 5000);
                byte[] stdout = outTask.IsCompleted ? outTask.Result : new byte[0];
                string stderr = errTask.IsCompleted ? Encoding.UTF8.GetString(errTask.Result) : "";
                if (p.ExitCode != 0)
                    return Fail(r, 502, "deep.exe exit " + p.ExitCode + ": " + (stderr.Trim().Length > 0 ? stderr.Trim() : "(no diagnostic)"));
                ParseResult(r, stderr);
                r.Body = stdout;
                return r;
            }
            catch (Exception ex)
            {
                try { if (p != null && !p.HasExited) p.Kill(); } catch { }
                return Fail(r, 502, "could not run deep.exe: " + ex.Message);
            }
        }

        private static Result Fail(Result r, int status, string text)
        {
            r.Status = status; r.Reason = status == 504 ? "Gateway Timeout" : "Bad Gateway";
            r.Error = text;
            r.MediaType = "text/html; charset=utf-8";
            r.Headers.Clear();
            r.Headers.Add(new KeyValuePair<string, string>("Content-Type", r.MediaType));
            string t = System.Net.WebUtility.HtmlEncode(text);
            r.Body = Encoding.UTF8.GetBytes(
                "<!DOCTYPE HTML PUBLIC \"-//IETF//DTD HTML 2.0//EN\">\n<html><head>\n<title>" + status + " " + r.Reason + "</title>\n</head><body>\n" +
                "<h1>" + r.Reason + "</h1>\n<p>" + t + "</p>\n<hr>\n<address>Aloha DEEP bridge (deep.exe request)</address>\n</body></html>\n");
            return r;
        }

        private static string Quote(string s) { return "\"" + s.Replace("\"", "\\\"") + "\""; }

        private static byte[] ReadAll(Stream s, int cap)
        {
            using (var ms = new MemoryStream())
            {
                var buf = new byte[65536];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    ms.Write(buf, 0, n);
                    if (ms.Length > cap) break;
                }
                return ms.ToArray();
            }
        }

        private static string RequestJson(string method, List<KeyValuePair<string, string>> headers, byte[] body)
        {
            var sb = new StringBuilder("{\"method\":\"" + (method ?? "GET").ToUpperInvariant() + "\",\"headers\":[");
            bool first = true;
            if (headers != null)
                foreach (var h in headers)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"name\":").Append(JsonString(h.Key.ToLowerInvariant())).Append(",\"value\":").Append(JsonString(h.Value)).Append('}');
                }
            sb.Append("],\"body\":\"").Append(body != null && body.Length > 0 ? Convert.ToBase64String(body) : "").Append("\"}");
            return sb.ToString();
        }

        private static string JsonString(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        private static void ParseResult(Result r, string stderr)
        {
            object root = null;
            try
            {
                int i = stderr.IndexOf('{');
                if (i >= 0) root = Json.Parse(stderr.Substring(i));
            }
            catch { }
            var o = root as Dictionary<string, object>;
            if (o == null) { r.Status = 200; r.Reason = "OK"; return; }
            r.Status = (int)Json.Num(o, "status", 200);
            r.Reason = ReasonFor(r.Status);
            r.MediaType = Json.Str(o, "media_type");
            object hs;
            if (o.TryGetValue("headers", out hs) && hs is List<object>)
                foreach (var item in (List<object>)hs)
                {
                    var h = item as Dictionary<string, object>;
                    if (h == null) continue;
                    string n = Json.Str(h, "name"), v = Json.Str(h, "value");
                    if (!string.IsNullOrEmpty(n)) r.Headers.Add(new KeyValuePair<string, string>(n, v ?? ""));
                }
        }

        public static string ReasonFor(int status)
        {
            switch (status)
            {
                case 200: return "OK"; case 201: return "Created"; case 204: return "No Content"; case 301: return "Moved Permanently";
                case 302: return "Found"; case 303: return "See Other"; case 304: return "Not Modified"; case 307: return "Temporary Redirect";
                case 400: return "Bad Request"; case 401: return "Unauthorized"; case 403: return "Forbidden"; case 404: return "Not Found";
                case 405: return "Method Not Allowed"; case 500: return "Internal Server Error"; case 502: return "Bad Gateway";
                case 503: return "Service Unavailable"; case 504: return "Gateway Timeout";
                default: return status < 300 ? "OK" : status < 400 ? "Redirect" : "Error";
            }
        }

        public static void Answer(NetConfig cfg, CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs a)
        {
            Uri u;
            if (!Uri.TryCreate(a.Request.Uri, UriKind.Absolute, out u)) return;
            string deepUri = ToDeepUri(u);
            string method = a.Request.Method ?? "GET";
            var headers = new List<KeyValuePair<string, string>>();
            foreach (var h in a.Request.Headers)
            {
                string k = h.Key.ToLowerInvariant();

                if (k == "accept" || k == "accept-language" || k == "cookie" || k == "content-type" || k == "referer" || k == "if-none-match" || k == "if-modified-since")
                    headers.Add(new KeyValuePair<string, string>(k, h.Value));
            }
            byte[] body = null;
            try
            {
                if (a.Request.Content != null)
                    using (var ms = new MemoryStream()) { a.Request.Content.CopyTo(ms); body = ms.ToArray(); }
            }
            catch { }
            if (body != null && body.Length > 1024 * 1024) body = null;

            var deferral = a.GetDeferral();
            var ctx = SynchronizationContext.Current;
            ActivityLog.Proxy("DEEP " + method + "  " + deepUri);
            Task.Run(() => Request(cfg, deepUri, method, headers, body)).ContinueWith(t =>
            {
                Action finish = () =>
                {
                    try
                    {
                        var r = t.IsFaulted ? Fail(new Result(), 502, "DEEP bridge error: " + t.Exception.GetBaseException().Message) : t.Result;
                        var hsb = new StringBuilder();
                        bool hasType = false;
                        foreach (var h in r.Headers)
                        {
                            string k = h.Key.ToLowerInvariant();
                            if (k == "content-length" || k == "transfer-encoding" || k == "connection") continue;
                            string v = h.Value;
                            if (k == "location" && v.StartsWith("deep://", StringComparison.OrdinalIgnoreCase)) v = ToVirtual(v) ?? v;
                            if (k == "content-type") hasType = true;
                            hsb.Append(h.Key).Append(": ").Append(v).Append("\r\n");
                        }
                        if (!hasType && !string.IsNullOrEmpty(r.MediaType)) hsb.Append("Content-Type: ").Append(r.MediaType).Append("\r\n");
                        hsb.Append("X-Content-Type-Options: nosniff\r\n");
                        if (r.Error != null) ActivityLog.Network("DEEP " + deepUri + " -> " + r.Status + " " + r.Error);
                        else ActivityLog.Proxy("DEEP " + r.Status + "  " + deepUri + "  " + (r.MediaType ?? "") + "  " + r.Body.Length + " B");
                        a.Response = core.Environment.CreateWebResourceResponse(
                            new MemoryStream(r.Body ?? new byte[0]), r.Status, r.Reason ?? ReasonFor(r.Status), hsb.ToString());
                    }
                    catch (Exception ex)
                    {
                        try { a.Response = core.Environment.CreateWebResourceResponse(null, 502, "Bad Gateway", "Content-Type: text/plain\r\n"); } catch { }
                        ActivityLog.Network("DEEP bridge: " + ex.Message);
                    }
                    finally { try { deferral.Complete(); } catch { } }
                };
                if (ctx != null) ctx.Post(_ => finish(), null); else finish();
            });
        }

        public static string RunQuick(NetConfig cfg, string args, int timeoutMs = 8000)
        {
            string exe = FindExe(cfg);
            if (exe == null) return null;
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(exe)
                };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return null; }
                    return (o + e).Trim();
                }
            }
            catch { return null; }
        }

        public static class Json
        {
            public static object Parse(string s) { int i = 0; var v = Value(s, ref i); return v; }
            public static string Str(Dictionary<string, object> o, string k) { object v; return o.TryGetValue(k, out v) ? v as string : null; }
            public static double Num(Dictionary<string, object> o, string k, double d) { object v; return o.TryGetValue(k, out v) && v is double ? (double)v : d; }

            private static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
            private static object Value(string s, ref int i)
            {
                Ws(s, ref i);
                if (i >= s.Length) throw new FormatException("json: end");
                char c = s[i];
                if (c == '{')
                {
                    var d = new Dictionary<string, object>(); i++;
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    while (true)
                    {
                        Ws(s, ref i);
                        string k = (string)Value(s, ref i);
                        Ws(s, ref i); if (s[i] != ':') throw new FormatException("json: colon"); i++;
                        d[k] = Value(s, ref i);
                        Ws(s, ref i);
                        if (s[i] == ',') { i++; continue; }
                        if (s[i] == '}') { i++; return d; }
                        throw new FormatException("json: object");
                    }
                }
                if (c == '[')
                {
                    var l = new List<object>(); i++;
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ']') { i++; return l; }
                    while (true)
                    {
                        l.Add(Value(s, ref i));
                        Ws(s, ref i);
                        if (s[i] == ',') { i++; continue; }
                        if (s[i] == ']') { i++; return l; }
                        throw new FormatException("json: array");
                    }
                }
                if (c == '"')
                {
                    var sb = new StringBuilder(); i++;
                    while (i < s.Length && s[i] != '"')
                    {
                        if (s[i] == '\\' && i + 1 < s.Length)
                        {
                            i++;
                            switch (s[i])
                            {
                                case 'n': sb.Append('\n'); break; case 't': sb.Append('\t'); break; case 'r': sb.Append('\r'); break;
                                case 'b': sb.Append('\b'); break; case 'f': sb.Append('\f'); break;
                                case 'u': sb.Append((char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber)); i += 4; break;
                                default: sb.Append(s[i]); break;
                            }
                        }
                        else sb.Append(s[i]);
                        i++;
                    }
                    i++;
                    return sb.ToString();
                }
                if (s.Length - i >= 4 && s.Substring(i, 4) == "true") { i += 4; return true; }
                if (s.Length - i >= 5 && s.Substring(i, 5) == "false") { i += 5; return false; }
                if (s.Length - i >= 4 && s.Substring(i, 4) == "null") { i += 4; return null; }
                int start = i;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
                double num;
                if (double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out num)) return num;
                throw new FormatException("json: value at " + start);
            }
        }
    }
}
