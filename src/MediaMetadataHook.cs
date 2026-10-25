using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Aloha.Media
{

    public sealed class ResourceInfo
    {
        public string Name;
        public string DeclaredExt;
        public long Length;
        public bool IsLocal;
        public byte[] Head;
        public Func<Stream> Open;
    }

    public interface IResourceResolver { ResourceInfo Resolve(string url); }

    public sealed class LocalFileResolver : IResourceResolver
    {
        public ResourceInfo Resolve(string url)
        {
            string path = url;
            if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                try { path = new Uri(url).LocalPath; } catch { return null; }
            }
            else if (url.IndexOf("://", StringComparison.Ordinal) >= 0)
            {
                return null;
            }
            if (!File.Exists(path)) return null;
            var fi = new FileInfo(path);
            return new ResourceInfo
            {
                Name = fi.Name,
                DeclaredExt = fi.Extension.TrimStart('.'),
                Length = fi.Length,
                IsLocal = true,
                Open = () => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            };
        }
    }

    public sealed class MediaMetadataHook
    {
        private const int HEAD = 64 * 1024;
        private const long CHEAP_SHA_LIMIT = 64L * 1024 * 1024;

        private readonly CoreWebView2 _core;
        private readonly IResourceResolver _resolver;
        private readonly SynchronizationContext _ui;

        private MediaMetadataHook(CoreWebView2 core, IResourceResolver resolver, SynchronizationContext ui, string script)
        { _core = core; _resolver = resolver; _ui = ui; _script = script; }

        private readonly string _script;
        private string _scriptId;
        public bool Enabled { get { return _scriptId != null; } }

        public static async Task<MediaMetadataHook> AttachAsync(CoreWebView2 core, string scriptPath, IResourceResolver resolver, bool enabled = true)
        {
            var hook = new MediaMetadataHook(core, resolver, SynchronizationContext.Current, File.ReadAllText(scriptPath));
            core.WebMessageReceived += hook.OnWebMessage;
            if (enabled) await hook.SetEnabledAsync(true);
            return hook;
        }

        public async Task SetEnabledAsync(bool on)
        {
            if (on == Enabled) return;
            if (on)
            {
                _scriptId = await _core.AddScriptToExecuteOnDocumentCreatedAsync(_script);
            }
            else
            {
                string id = _scriptId; _scriptId = null;
                try { _core.RemoveScriptToExecuteOnDocumentCreated(id); } catch { }

                try { _core.PostWebMessageAsJson("{\"t\":\"off\"}"); } catch { }
            }
        }

        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!Enabled) return;
            InMsg msg;
            try { msg = Parse(e.TryGetWebMessageAsString()); }
            catch { return; }
            if (msg == null || msg.t == null) return;

            if (msg.t == "open")
            {
                if (!string.IsNullOrEmpty(msg.url)) try { MediaSheetWindow.Open(msg.url, _resolver); } catch (Exception ex) { ActivityLog.General("media sheet: " + ex.Message); }
                return;
            }

            Task.Run(() => Handle(msg));
        }

        private async Task Handle(InMsg msg)
        {
            try
            {
                if (string.IsNullOrEmpty(msg.url)) { Fail(msg, "no url"); return; }
                ResourceInfo ri;
                try { ri = _resolver.Resolve(msg.url); }
                catch (NotSupportedException nse) { Fail(msg, nse.Message); return; }
                catch (FileNotFoundException) { Fail(msg, "file not found"); return; }
                catch (IOException ioe) { Fail(msg, ioe.Message); return; }
                if (ri == null) { Fail(msg, "cannot open resource"); return; }

                if (msg.t == "sniff")
                {
                    Post(MetaJson(msg.id, await SniffAsync(ri)));
                }
                else if (msg.t == "hash")
                {
                    var progress = new Progress<int>(pct => Post(ProgressJson(msg.id, msg.algo, pct)));
                    string hex;
                    using (var s = ri.Open())
                        hex = await HashEngine.ComputeAsync(s, msg.algo, ri.Length, progress);
                    Post(HashJson(msg.id, msg.algo, hex, null));
                }
            }
            catch (NotSupportedException nse) { Fail(msg, nse.Message); }
            catch (Exception ex) { Fail(msg, "error: " + ex.Message); }
        }

        public static async Task<Meta> SniffAsync(ResourceInfo ri)
        {
            byte[] head, tail = null;
            if (ri.Head != null) head = ri.Head;
            else using (var s = ri.Open()) head = ReadUpTo(s, HEAD);
            if (ri.IsLocal && ri.Length > 2 * HEAD)
                using (var s = ri.Open()) { s.Seek(ri.Length - HEAD, SeekOrigin.Begin); tail = ReadUpTo(s, HEAD); }

            var meta = MediaSniffer.Sniff(ri.Name, head, ri.Length, tail);
            if (ri.IsLocal && ri.Length > 0 && ri.Length < CHEAP_SHA_LIMIT)
            {
                try { using (var s = ri.Open()) meta.Sha = ShortHex(await HashEngine.ComputeAsync(s, "SHA-256", ri.Length)); }
                catch { meta.Sha = null; }
            }
            return meta;
        }

        private void Fail(InMsg msg, string why)
        {
            if (msg.t == "hash") Post(HashJson(msg.id, msg.algo, null, why));

            else Post("{\"t\":\"meta\",\"id\":" + msg.id + ",\"err\":" + Str(why) + "}");
        }

        private void Post(string json)
        {
            if (_ui != null) _ui.Post(_ => { try { _core.PostWebMessageAsJson(json); } catch { } }, null);
            else try { _core.PostWebMessageAsJson(json); } catch { }
        }

        private static string MetaJson(long id, Meta m)
        {
            var sb = new StringBuilder();
            sb.Append("{\"t\":\"meta\",\"id\":").Append(id).Append(",\"meta\":{");
            sb.Append("\"name\":").Append(Str(m.Name));
            sb.Append(",\"type\":").Append(Str(m.Type));
            sb.Append(",\"ext\":").Append(Str(m.Ext));
            sb.Append(",\"decl\":").Append(m.Declared == null ? "null" : Str(m.Declared));
            sb.Append(",\"magic\":").Append(Str(m.Magic));
            sb.Append(",\"sig\":").Append(Str(m.Signature));
            sb.Append(",\"sha\":").Append(m.Sha == null ? "null" : Str(m.Sha));
            sb.Append(",\"note\":").Append(m.Note == null ? "null" : Str(m.Note));
            sb.Append(",\"entropy\":").Append(m.Entropy.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(",\"rows\":[");
            for (int i = 0; i < m.Rows.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('[').Append(Str(m.Rows[i][0])).Append(',').Append(Str(m.Rows[i][1])).Append(']');
            }
            sb.Append("]}}");
            return sb.ToString();
        }
        private static string HashJson(long id, string algo, string hex, string err)
        {
            var sb = new StringBuilder();
            sb.Append("{\"t\":\"hash\",\"id\":").Append(id).Append(",\"algo\":").Append(Str(algo));
            sb.Append(",\"hex\":").Append(hex == null ? "null" : Str(hex));
            if (err != null) sb.Append(",\"err\":").Append(Str(err));
            sb.Append('}');
            return sb.ToString();
        }
        private static string ProgressJson(long id, string algo, int pct)
        { return "{\"t\":\"progress\",\"id\":" + id + ",\"algo\":" + Str(algo) + ",\"pct\":" + pct + "}"; }

        private static string Str(string s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder(s.Length + 2); sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c); break;
                }
            }
            sb.Append('"'); return sb.ToString();
        }
        private static string ShortHex(string full)
        { return (full != null && full.Length > 12) ? full.Substring(0, 6) + "…" + full.Substring(full.Length - 4) : full; }

        [DataContract]
        private sealed class InMsg
        {
            [DataMember(Name = "t")] public string t;
            [DataMember(Name = "id")] public long id;
            [DataMember(Name = "url")] public string url;
            [DataMember(Name = "kind")] public string kind;
            [DataMember(Name = "algo")] public string algo;
        }
        private static InMsg Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (InMsg)new DataContractJsonSerializer(typeof(InMsg)).ReadObject(ms);
        }

        private static byte[] ReadUpTo(Stream s, int n)
        {
            var buf = new byte[n]; int off = 0, r;
            while (off < n && (r = s.Read(buf, off, n - off)) > 0) off += r;
            if (off == n) return buf;
            var trimmed = new byte[off]; Array.Copy(buf, trimmed, off); return trimmed;
        }
    }
}
