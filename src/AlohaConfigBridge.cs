using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Microsoft.Web.WebView2.Core;

namespace Aloha
{
    public static class AlohaConfigBridge
    {

        public const string Host     = "config.aloha";
        public const string PageUrl  = "https://config.aloha/aloha_config.html";

        public static string Pretty(string src)
        {
            if (string.IsNullOrEmpty(src)) return "";
            if (src.StartsWith(PageUrl, StringComparison.OrdinalIgnoreCase) ||
                src.StartsWith("https://config.aloha", StringComparison.OrdinalIgnoreCase))
                return "aloha://config";
            if (src.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase))
                return "aloha://blank";
            if (src.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                return "";
            return src;
        }

        private static string AppDir =>
            Path.GetDirectoryName(Application.ExecutablePath);
        private static string CatalogPath =>
            Path.Combine(AppDir, "chromium_flags.json");

        public static void Attach(
            CoreWebView2 core,
            NetConfig cfg,
            Action reinit,
            Action refreshConfigPanel)
        {
            if (core == null) return;

            try
            {
                core.SetVirtualHostNameToFolderMapping(
                    Host, AppDir, CoreWebView2HostResourceAccessKind.Allow);
            }
            catch { }

            core.NavigationCompleted += (s, e) =>
            {
                try
                {
                    string url = core.Source ?? "";
                    if (url.IndexOf(Host, StringComparison.OrdinalIgnoreCase) < 0) return;
                    FeedCatalog(core);
                    FeedState(core, cfg);
                }
                catch { }
            };

            core.WebMessageReceived += (s, e) =>
            {

                if (!Aloha.Net.BridgeGuard.Authorize(e)) return;
                string json;
                try { json = e.WebMessageAsJson; }
                catch { return; }
                HandleMessage(json, cfg, reinit, refreshConfigPanel);
            };
        }

        private static void FeedCatalog(CoreWebView2 core)
        {
            try
            {
                if (!File.Exists(CatalogPath))
                {
                    ActivityLog.General("aloha:config — chromium_flags.json not found next to exe; page uses built-in sample data.");
                    return;
                }
                string raw = File.ReadAllText(CatalogPath, Encoding.UTF8);

                string js = "window.setCatalog(" + JsString(raw) + ");";
                core.ExecuteScriptAsync(js);
                ActivityLog.General("aloha:config — catalog loaded (" + raw.Length.ToString("N0") + " bytes).");
            }
            catch (Exception ex)
            {
                ActivityLog.General("aloha:config — catalog load failed: " + ex.Message);
            }
        }

        private static void FeedState(CoreWebView2 core, NetConfig cfg)
        {
            try
            {
                string ov = string.IsNullOrWhiteSpace(cfg.ConfigOverridesJson)
                          ? "{}" : cfg.ConfigOverridesJson;
                core.ExecuteScriptAsync(
                    "window.setOverrides && window.setOverrides(" + JsString(ov) + ");");

                var favs = cfg.FavouriteFlags ?? new System.Collections.Generic.List<string>();
                var arr = new StringBuilder("[");
                for (int i = 0; i < favs.Count; i++)
                {
                    if (i > 0) arr.Append(',');
                    arr.Append(JsString(favs[i] ?? ""));
                }
                arr.Append(']');
                core.ExecuteScriptAsync(
                    "window.setFavourites && window.setFavourites(" + JsString(arr.ToString()) + ");");

                ActivityLog.General("aloha:config — state restored (toggle map " + ov.Length
                                  + " bytes, " + favs.Count + " favourite(s)).");
            }
            catch (Exception ex)
            {
                ActivityLog.General("aloha:config — state restore failed: " + ex.Message);
            }
        }

        private static void ExportSettings(string args, string overridesJson, string favouritesJson)
        {
            try
            {
                string dir = System.IO.Path.Combine(AlohaPaths.Root, "exports");
                System.IO.Directory.CreateDirectory(dir);
                string file = System.IO.Path.Combine(dir,
                    "aloha_flags_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json");

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"exported\": " + JsString(DateTime.Now.ToString("s")) + ",");
                sb.AppendLine("  \"args\": "     + JsString(args ?? "") + ",");
                sb.AppendLine("  \"overrides\": " + (string.IsNullOrWhiteSpace(overridesJson) ? "{}" : overridesJson) + ",");
                sb.AppendLine("  \"favourites\": " + (string.IsNullOrWhiteSpace(favouritesJson) ? "[]" : favouritesJson));
                sb.AppendLine("}");

                System.IO.File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
                ActivityLog.General("aloha:config — exported to " + file);
            }
            catch (Exception ex)
            {
                ActivityLog.General("aloha:config — export FAILED: " + ex.Message);
            }
        }

        private static void HandleMessage(
            string json, NetConfig cfg, Action reinit, Action refreshConfigPanel)
        {
            Msg m;
            try { m = Deserialize<Msg>(json); }
            catch { return; }
            if (m == null || string.IsNullOrEmpty(m.type)) return;

            switch (m.type)
            {
                case "apply":
                    {

                        string args = m.payload != null ? (m.payload.args ?? "") : "";
                        cfg.ExtraSwitches = args.Trim();

                        if (m.payload != null && m.payload.overridesJson != null)
                            cfg.ConfigOverridesJson = m.payload.overridesJson;

                        try { cfg.Save(); } catch { }
                        ActivityLog.General("aloha:config — applying extra switches: "
                            + (args.Length == 0 ? "(none)" : args));

                        if (reinit != null)
                            BeginOnUi(() => reinit());
                        break;
                    }

                case "resetall":
                    {

                        cfg.ExtraSwitches = "";
                        cfg.ConfigOverridesJson = "";
                        try { cfg.Save(); } catch { }
                        ActivityLog.General("aloha:config — every flag override cleared");
                        if (reinit != null)
                            BeginOnUi(() => reinit());
                        break;
                    }

                case "export":
                    {
                        if (m.payload == null) break;
                        ExportSettings(m.payload.args ?? "",
                                       m.payload.overridesJson ?? "{}",
                                       m.payload.favouritesJson ?? "[]");
                        break;
                    }

                case "favourite":
                    {
                        if (m.payload == null) break;
                        string id = m.payload.id
                                  ?? m.payload.switch_string
                                  ?? m.payload.feature_name
                                  ?? m.payload.name;
                        if (string.IsNullOrEmpty(id)) break;
                        if (cfg.FavouriteFlags == null)
                            cfg.FavouriteFlags = new System.Collections.Generic.List<string>();
                        if (!cfg.FavouriteFlags.Contains(id))
                        {
                            cfg.FavouriteFlags.Add(id);
                            try { cfg.Save(); } catch { }
                            ActivityLog.General("aloha:config — favourited: " + id
                                + " -> Browser Configuration");
                            if (refreshConfigPanel != null)
                                BeginOnUi(() => refreshConfigPanel());
                        }
                        break;
                    }

                case "toggle":

                    if (m.payload != null)
                        ActivityLog.General("aloha:config — toggle "
                            + (m.payload.id ?? "?") + " = " + (m.payload.state ?? "?"));
                    break;

                case "copy":

                    break;
            }
        }

        private static void BeginOnUi(Action a)
        {
            var f = Form.ActiveForm ?? (Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null);
            if (f != null && f.IsHandleCreated) { try { f.BeginInvoke(a); return; } catch { } }
            a();
        }

        private static string JsString(string s)
        {
            var sb = new StringBuilder(s.Length + 16);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static T Deserialize<T>(string json) where T : class
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var ser = new DataContractJsonSerializer(typeof(T));
                return ser.ReadObject(ms) as T;
            }
        }

        [DataContract]
        private sealed class Msg
        {
            [DataMember(Name = "type")]    public string type;
            [DataMember(Name = "payload")] public Payload payload;
        }

        [DataContract]
        private sealed class Payload
        {
            [DataMember(Name = "args")]          public string args;

            [DataMember(Name = "overridesJson")]  public string overridesJson;
            [DataMember(Name = "favouritesJson")] public string favouritesJson;
            [DataMember(Name = "id")]            public string id;
            [DataMember(Name = "kind")]          public string kind;
            [DataMember(Name = "state")]         public string state;
            [DataMember(Name = "switch_string")] public string switch_string;
            [DataMember(Name = "feature_name")]  public string feature_name;
            [DataMember(Name = "name")]          public string name;
            [DataMember(Name = "category")]      public string category;
            [DataMember(Name = "description")]   public string description;
            [DataMember(Name = "default")]       public string @default;
        }
    }
}
