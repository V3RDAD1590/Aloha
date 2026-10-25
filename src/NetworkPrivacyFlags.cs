using System.Collections.Generic;
using Microsoft.Web.WebView2.Core;

namespace Aloha.RingStoreCore
{
    public static class NetworkPrivacyFlags
    {
        public static string BuildBrowserArguments(NetworkPrivacyConfig c)
        {
            var args = new List<string>();
            var disableFeatures = new List<string>();

            if (c.WebRtcLeakProtection)

                args.Add("--force-webrtc-ip-handling-policy=default_public_interface_only");
            if (c.DisableQuic)                 args.Add("--disable-quic");
            if (c.DisableBackgroundNetworking) args.Add("--disable-background-networking");
            if (c.DisableHyperlinkAuditing)    args.Add("--no-pings");

            if (c.DisableBackgroundExtensions) args.Add("--disable-component-extensions-with-background-pages");
            if (c.DisableDomainReliability)    args.Add("--disable-domain-reliability");
            if (c.DisableSync)                 args.Add("--disable-sync");

            if (c.DisableCanvasReadback)       args.Add("--disable-reading-from-canvas");
            if (c.DisableWebGl)                args.Add("--disable-3d-apis");

            if (c.DisableGoogleServices)
            {
                disableFeatures.Add("Translate");
                disableFeatures.Add("OptimizationHints");
                disableFeatures.Add("MediaRouter");          
            }
            if (c.DisablePrivacySandbox)
            {
                disableFeatures.Add("PrivacySandboxAdsAPIs");
                disableFeatures.Add("BrowsingTopics");
            }
            if (c.DisableUaClientHints)
                disableFeatures.Add("UserAgentClientHint");

            if (disableFeatures.Count > 0)
                args.Add("--disable-features=" + string.Join(",", disableFeatures));

            return string.Join(" ", args);
        }

        public static void ApplySettings(CoreWebView2Settings s, NetworkPrivacyConfig c)
        {
            if (s == null) return;
            s.IsGeneralAutofillEnabled    = !c.DisableAutofill;
            s.IsPasswordAutosaveEnabled   = !c.DisablePasswordSave;
            s.AreDevToolsEnabled          = !c.DisableDevTools;
            s.AreDefaultContextMenusEnabled = !c.DisableDefaultContextMenu;
            if (!string.IsNullOrEmpty(c.UserAgentOverride))
                s.UserAgent = c.UserAgentOverride;
        }
    }
}
