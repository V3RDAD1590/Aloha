using System;
using System.Globalization;
using System.Runtime.Serialization;

namespace Aloha.Cutscenes
{
    [DataContract]
    public sealed class CutsceneBook
    {

        [DataMember] public bool Enabled { get; set; }

        [DataMember] public int MaxPerSession { get; set; }

        [DataMember] public int GlobalCooldownMin { get; set; }

        [DataMember] public bool FullDesktop { get; set; }

        [DataMember] public bool AllowOnTypedNavigation { get; set; }

        [DataMember] public CutsceneRule[] Rules { get; set; }

        public static CutsceneBook Defaults()
        {
            var b = new CutsceneBook();
            b.Normalize();
            return b;
        }

        public void Normalize()
        {
            if (MaxPerSession <= 0) MaxPerSession = 3;
            if (GlobalCooldownMin <= 0) GlobalCooldownMin = 20;
            if (Rules == null) Rules = new CutsceneRule[0];
            foreach (var r in Rules) if (r != null) r.Normalize();
        }
    }

    [DataContract]
    public sealed class CutsceneRule
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public bool Enabled { get; set; }

        [DataMember] public string[] Hosts { get; set; }

        [DataMember] public string From { get; set; }
        [DataMember] public string To { get; set; }

        [DataMember] public string[] Days { get; set; }

        [DataMember] public double Chance { get; set; }

        [DataMember] public int  CooldownMin { get; set; }
        [DataMember] public bool OncePerDay { get; set; }
        [DataMember] public bool OnceEver { get; set; }

        [DataMember] public int TimeoutMs { get; set; }

        [DataMember] public string Video { get; set; }
        [DataMember] public bool VideoMuted { get; set; }

        [DataMember] public CutsceneStep[] Scene { get; set; }

        public void Normalize()
        {
            if (string.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            if (Hosts == null || Hosts.Length == 0) Hosts = new[] { "*" };
            if (Chance <= 0.0) Chance = 1.0;
            if (Chance > 1.0) Chance = 1.0;
            if (CooldownMin < 0) CooldownMin = 0;
            if (Scene == null) Scene = new CutsceneStep[0];
        }

        public bool MatchesHost(string host)
        {
            if (string.IsNullOrEmpty(host)) return false;
            foreach (var p in Hosts)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (p == "*") return true;
                if (p.StartsWith("*.", StringComparison.Ordinal))
                {
                    string suffix = p.Substring(2);
                    if (host.Equals(suffix, StringComparison.OrdinalIgnoreCase)) return true;
                    if (host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase)) return true;
                }
                else if (host.Equals(p, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public bool MatchesDay(DateTime local)
        {
            if (Days == null || Days.Length == 0) return true;
            string name = local.DayOfWeek.ToString().Substring(0, 3);
            foreach (var d in Days)
                if (!string.IsNullOrEmpty(d) && d.Length >= 3 &&
                    d.Substring(0, 3).Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public bool MatchesTime(DateTime local)
        {
            int from, to;
            if (!ParseHm(From, out from) || !ParseHm(To, out to)) return true;
            int now = local.Hour * 60 + local.Minute;
            if (from == to) return true;
            if (from < to) return now >= from && now < to;
            return now >= from || now < to;
        }

        private static bool ParseHm(string s, out int minutes)
        {
            minutes = 0;
            if (string.IsNullOrEmpty(s)) return false;
            var parts = s.Split(':');
            if (parts.Length != 2) return false;
            int h, m;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out h)) return false;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out m)) return false;
            if (h < 0 || h > 23 || m < 0 || m > 59) return false;
            minutes = h * 60 + m;
            return true;
        }
    }

    [DataContract]
    public sealed class CutsceneStep
    {

        [DataMember] public string Op { get; set; }
        [DataMember] public string Text { get; set; }
        [DataMember] public string Color { get; set; }
        [DataMember] public double X { get; set; }
        [DataMember] public double Y { get; set; }
        [DataMember] public double W { get; set; }
        [DataMember] public double H { get; set; }
        [DataMember] public int Size { get; set; }
        [DataMember] public int Ms { get; set; }
        [DataMember] public int Cps { get; set; }
        [DataMember] public bool Center { get; set; }
        [DataMember] public bool Bold { get; set; }
    }
}
