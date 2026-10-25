using Microsoft.Web.WebView2.Core;

namespace Aloha
{
    public static class NetConfigWebView
    {
        public static void ApplyLive(CoreWebView2 core, NetConfig cfg)
        {
            if (core == null || cfg == null) return;

            CoreWebView2Settings s = core.Settings;
            s.IsScriptEnabled              = !cfg.DisableJavaScript;
            s.IsGeneralAutofillEnabled     = !cfg.DisableAutofill;
            s.IsPasswordAutosaveEnabled    = !cfg.DisablePasswordSave;
            s.IsReputationCheckingRequired = !cfg.DisableSmartScreen;
            s.AreDevToolsEnabled           = !cfg.DisableDevTools;
            if (!string.IsNullOrWhiteSpace(cfg.UserAgent))
                s.UserAgent = cfg.UserAgent;

            CoreWebView2Profile profile = core.Profile;
            if (profile != null)
            {
                switch (cfg.TrackingPrevention)
                {
                    case 0:  profile.PreferredTrackingPreventionLevel = CoreWebView2TrackingPreventionLevel.None;     break;
                    case 1:  profile.PreferredTrackingPreventionLevel = CoreWebView2TrackingPreventionLevel.Basic;    break;
                    case 3:  profile.PreferredTrackingPreventionLevel = CoreWebView2TrackingPreventionLevel.Strict;   break;
                    default: profile.PreferredTrackingPreventionLevel = CoreWebView2TrackingPreventionLevel.Balanced; break;
                }
            }
        }
    }
}
