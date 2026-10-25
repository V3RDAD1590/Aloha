using System;

namespace Aloha.RingStoreCore
{
    public sealed class PrivacyOption
    {
        public string Section;
        public string Label;
        public string Description;
        public bool   RequiresRestart;
        public string Reliability;
        public Func<NetworkPrivacyConfig, bool> Get;
        public Action<NetworkPrivacyConfig, bool> Set;

        public PrivacyOption(string section, string label, string desc, bool restart,
            string reliability, Func<NetworkPrivacyConfig,bool> get, Action<NetworkPrivacyConfig,bool> set)
        {
            Section = section; Label = label; Description = desc;
            RequiresRestart = restart; Reliability = reliability; Get = get; Set = set;
        }
    }

    public static class NetworkPrivacyOptions
    {
        public static readonly PrivacyOption[] All =
        {

            new PrivacyOption("Networking", "WebRTC leak protection",
                "Hides your local IP from sites while keeping WebRTC working.",
                true, "durable", c => c.WebRtcLeakProtection, (c,v) => c.WebRtcLeakProtection = v),
            new PrivacyOption("Networking", "Disable QUIC / HTTP3",
                "Forces TCP/TLS for all connections. More predictable, slightly slower.",
                true, "durable", c => c.DisableQuic, (c,v) => c.DisableQuic = v),
            new PrivacyOption("Networking", "Disable background networking",
                "Stops Chromium making network requests in the background.",
                true, "durable", c => c.DisableBackgroundNetworking, (c,v) => c.DisableBackgroundNetworking = v),
            new PrivacyOption("Networking", "Disable hyperlink auditing",
                "Blocks the <a ping> tracking beacons sent when you click some links.",
                true, "durable", c => c.DisableHyperlinkAuditing, (c,v) => c.DisableHyperlinkAuditing = v),

            new PrivacyOption("Privacy", "Disable background extensions",
                "Prevents bundled background pages (e.g. Hangouts) from launching.",
                true, "durable", c => c.DisableBackgroundExtensions, (c,v) => c.DisableBackgroundExtensions = v),
            new PrivacyOption("Privacy", "Disable domain reliability",
                "Stops Google reliability beacons about connection failures.",
                true, "durable", c => c.DisableDomainReliability, (c,v) => c.DisableDomainReliability = v),
            new PrivacyOption("Privacy", "Disable profile sync",
                "Disables the sync subsystem entirely.",
                true, "durable", c => c.DisableSync, (c,v) => c.DisableSync = v),
            new PrivacyOption("Privacy", "Disable Google services",
                "Turns off Translate, optimization hints, and Cast/MediaRouter discovery.",
                true, "durable", c => c.DisableGoogleServices, (c,v) => c.DisableGoogleServices = v),
            new PrivacyOption("Privacy", "Disable Privacy Sandbox / Topics",
                "Disables ad-measurement and the Topics API. Flag names track the Chromium version.",
                true, "best-effort", c => c.DisablePrivacySandbox, (c,v) => c.DisablePrivacySandbox = v),
            new PrivacyOption("Privacy", "Disable autofill",
                "No form autofill.", false, "durable",
                c => c.DisableAutofill, (c,v) => c.DisableAutofill = v),
            new PrivacyOption("Privacy", "Disable password saving",
                "No password autosave prompts.", false, "durable",
                c => c.DisablePasswordSave, (c,v) => c.DisablePasswordSave = v),
            new PrivacyOption("Privacy", "Disable DevTools",
                "Removes access to developer tools.", false, "durable",
                c => c.DisableDevTools, (c,v) => c.DisableDevTools = v),

            new PrivacyOption("Fingerprinting", "Disable canvas readback",
                "Blocks reading pixels back from <canvas> — strong anti-fingerprint, can break some sites.",
                true, "durable", c => c.DisableCanvasReadback, (c,v) => c.DisableCanvasReadback = v),
            new PrivacyOption("Fingerprinting", "Disable WebGL",
                "Turns off WebGL and accelerated 2D. Closest Chromium has to blocking WebGL fingerprinting.",
                true, "durable", c => c.DisableWebGl, (c,v) => c.DisableWebGl = v),
            new PrivacyOption("Fingerprinting", "Reduce User-Agent Client Hints",
                "Trims the UA-CH fingerprint surface. Best-effort; version-dependent.",
                true, "best-effort", c => c.DisableUaClientHints, (c,v) => c.DisableUaClientHints = v),

        };
    }
}
