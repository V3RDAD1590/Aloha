using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Web.WebView2.Core;

namespace Aloha
{

    public static class TamperEngine
    {
        public enum Kind { Block, Redirect, SetHeader, RmHeader, Respond }

        public sealed class Rule
        {
            public bool On = true;
            public Kind Kind;
            public string Match = "";   
            public string A = "";       
            public string B = "";       
            public string Body = "";    

            public string Describe()
            {
                string head = (On ? "[on] " : "[off] ");
                switch (Kind)
                {
                    case Kind.Block:     return head + "block      " + Match;
                    case Kind.Redirect:  return head + "redirect   " + Match + "  ->  " + A;
                    case Kind.SetHeader: return head + "setheader  " + Match + "   " + A + ": " + B;
                    case Kind.RmHeader:  return head + "rmheader   " + Match + "   " + A;
                    case Kind.Respond:   return head + "respond    " + Match + "   " + A + " " + B
                                              + " (" + Body.Length + " chars)";
                }
                return head + "?";
            }
        }

        public static bool Enabled = false;

        private static readonly List<Rule> rules = new List<Rule>();
        private static readonly object gate = new object();

        public static event Action<string> OnLog;
        public static event Action OnRulesChanged;

        private static void Log(string s) { try { OnLog?.Invoke(s); } catch { } }
        private static void Changed() { try { OnRulesChanged?.Invoke(); } catch { } }

        public static List<Rule> Snapshot()
        {
            lock (gate) return new List<Rule>(rules);
        }

        public static int Count { get { lock (gate) return rules.Count; } }

        public static void ClearRules()
        {
            lock (gate) rules.Clear();
            Changed();
        }

        public static bool DeleteRule(int index)
        {
            lock (gate)
            {
                if (index < 0 || index >= rules.Count) return false;
                rules.RemoveAt(index);
            }
            Changed();
            return true;
        }

        public static bool ToggleRule(int index)
        {
            lock (gate)
            {
                if (index < 0 || index >= rules.Count) return false;
                rules[index].On = !rules[index].On;
            }
            Changed();
            return true;
        }

        public static string AddFromText(string text)
        {
            string line = (text ?? "").Trim();
            if (line.Length == 0) return "empty";

            string kind, rest;
            Split1(line, out kind, out rest);
            kind = kind.ToLowerInvariant();

            string match, tail;
            Split1(rest, out match, out tail);
            if (kind != "block" && match.Length == 0)
                return "need a URL match: " + kind + " <match> ...";

            var r = new Rule();
            switch (kind)
            {
                case "block":

                    r.Kind = Kind.Block; r.Match = rest.Trim();
                    if (r.Match.Length == 0) return "usage: block <match>";
                    break;

                case "redirect":
                    if (tail.Trim().Length == 0) return "usage: redirect <match> <url>";
                    r.Kind = Kind.Redirect; r.Match = match; r.A = tail.Trim();
                    if (!r.A.Contains("://")) r.A = "https://" + r.A;
                    break;

                case "rmheader":
                    if (tail.Trim().Length == 0) return "usage: rmheader <match> <HeaderName>";
                    r.Kind = Kind.RmHeader; r.Match = match; r.A = tail.Trim();
                    break;

                case "setheader":
                    int colon = tail.IndexOf(':');
                    if (colon < 1) return "usage: setheader <match> <Name>: <value>";
                    r.Kind = Kind.SetHeader; r.Match = match;
                    r.A = tail.Substring(0, colon).Trim();
                    r.B = tail.Substring(colon + 1).Trim();
                    if (r.A.Length == 0) return "header name is empty";
                    break;

                case "respond":
                    string status, t2;
                    Split1(tail, out status, out t2);
                    string ctype, body;
                    Split1(t2, out ctype, out body);
                    int sc;
                    if (!int.TryParse(status, out sc) || sc < 100 || sc > 599)
                        return "usage: respond <match> <status> <contentType> <body...>";
                    if (ctype.Length == 0) ctype = "text/plain";
                    r.Kind = Kind.Respond; r.Match = match; r.A = sc.ToString();
                    r.B = ctype; r.Body = body;
                    break;

                default:
                    return "unknown rule '" + kind + "'  (block | redirect | setheader | rmheader | respond)";
            }

            lock (gate) rules.Add(r);
            Changed();
            return null;
        }

        private static void Split1(string s, out string first, out string rest)
        {
            s = (s ?? "").TrimStart();
            int sp = s.IndexOf(' ');
            if (sp < 0) { first = s; rest = ""; }
            else { first = s.Substring(0, sp); rest = s.Substring(sp + 1).TrimStart(); }
        }

        private static bool Hit(string uri, string match)
        {
            if (string.IsNullOrEmpty(match)) return false;
            return uri.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void Apply(CoreWebView2Environment env, CoreWebView2WebResourceRequestedEventArgs a)
        {
            if (!Enabled) return;
            List<Rule> snap;
            lock (gate) { if (rules.Count == 0) return; snap = new List<Rule>(rules); }

            string uri;
            try { uri = a.Request.Uri ?? ""; } catch { return; }

            foreach (var r in snap)
            {
                if (!r.On || !Hit(uri, r.Match)) continue;
                try
                {
                    switch (r.Kind)
                    {
                        case Kind.SetHeader:
                            a.Request.Headers.SetHeader(r.A, r.B);
                            Log("SET    " + r.A + ": " + r.B + "   " + uri);
                            break;

                        case Kind.RmHeader:
                            a.Request.Headers.RemoveHeader(r.A);
                            Log("RM     " + r.A + "   " + uri);
                            break;

                        case Kind.Block:
                            if (a.Response == null && env != null)
                            {
                                a.Response = env.CreateWebResourceResponse(
                                    null, 403, "Blocked by Aloha", "Content-Type: text/plain");
                                Log("BLOCK  403   " + uri);
                            }
                            return;

                        case Kind.Redirect:
                            if (a.Response == null && env != null)
                            {
                                a.Response = env.CreateWebResourceResponse(
                                    null, 302, "Found", "Location: " + r.A);
                                Log("REDIR  -> " + r.A + "   (" + uri + ")");
                            }
                            return;

                        case Kind.Respond:
                            if (a.Response == null && env != null)
                            {
                                int sc = 200; int.TryParse(r.A, out sc);
                                var bytes = Encoding.UTF8.GetBytes(r.Body ?? "");
                                var ms = new MemoryStream(bytes);
                                a.Response = env.CreateWebResourceResponse(
                                    ms, sc, Reason(sc), "Content-Type: " + r.B);
                                Log("RESP   " + sc + " " + r.B + " (" + bytes.Length + "b)   " + uri);
                            }
                            return;
                    }
                }
                catch (Exception ex) { Log("rule error: " + ex.Message); }
            }
        }

        private static string Reason(int sc)
        {
            switch (sc)
            {
                case 200: return "OK";
                case 201: return "Created";
                case 204: return "No Content";
                case 301: return "Moved Permanently";
                case 302: return "Found";
                case 400: return "Bad Request";
                case 401: return "Unauthorized";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 500: return "Internal Server Error";
            }
            return "OK";
        }
    }
}
