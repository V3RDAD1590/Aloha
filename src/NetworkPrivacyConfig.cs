using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Aloha.RingStoreCore
{
    [DataContract]
    public sealed class NetworkPrivacyConfig
    {

        [DataMember] public bool WebRtcLeakProtection       = true;
        [DataMember] public bool DisableQuic                = false;
        [DataMember] public bool DisableBackgroundNetworking = true;
        [DataMember] public bool DisableHyperlinkAuditing   = true;

        [DataMember] public bool DisableBackgroundExtensions = true;
        [DataMember] public bool DisableDomainReliability    = true;
        [DataMember] public bool DisableSync                 = true;
        [DataMember] public bool DisableGoogleServices       = true;
        [DataMember] public bool DisablePrivacySandbox       = true;

        [DataMember] public bool   DisableCanvasReadback = false;
        [DataMember] public bool   DisableWebGl          = false;
        [DataMember] public bool   DisableUaClientHints  = true;
        [DataMember] public string UserAgentOverride     = "";        

        [DataMember] public bool DisableAutofill          = true;
        [DataMember] public bool DisablePasswordSave      = true;
        [DataMember] public bool DisableDevTools          = false;
        [DataMember] public bool DisableDefaultContextMenu = false;

        public void Save(string path)
        {
            var ser = new DataContractJsonSerializer(typeof(NetworkPrivacyConfig));
            using (var fs = File.Create(path)) ser.WriteObject(fs, this);
        }

        public static NetworkPrivacyConfig Load(string path)
        {
            if (!File.Exists(path)) return new NetworkPrivacyConfig();
            var ser = new DataContractJsonSerializer(typeof(NetworkPrivacyConfig));
            using (var fs = File.OpenRead(path))
                return (ser.ReadObject(fs) as NetworkPrivacyConfig) ?? new NetworkPrivacyConfig();
        }
    }
}
