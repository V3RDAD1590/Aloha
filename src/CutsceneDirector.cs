using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;

namespace Aloha.Cutscenes
{
    public sealed class CutsceneDirector
    {
        public static readonly CutsceneDirector Current = new CutsceneDirector();

        private CutsceneBook _book;
        private Dictionary<string, DateTime> _lastPlayed = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly HashSet<string> _playedEver = new HashSet<string>(StringComparer.Ordinal);
        private readonly Random _rng = new Random();

        private Form _owner;
        private CutsceneWindow _live;
        private int _playedThisSession;
        private DateTime _lastAnyPlay = DateTime.MinValue;

        public event EventHandler StateChanged;

        public static string Dir
        {
            get
            {
                string d = Path.Combine(AlohaPaths.Root, "cutscenes");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        private static string RulesPath { get { return Path.Combine(Dir, "rules.json"); } }
        private static string StatePath { get { return Path.Combine(Dir, "state.json"); } }

        public void Attach(Form owner)
        {
            _owner = owner;
            Load();
        }

        public CutsceneBook Book
        {
            get { if (_book == null) Load(); return _book; }
        }

        public bool Enabled
        {
            get { return Book.Enabled; }
            set
            {
                if (Book.Enabled == value) return;
                Book.Enabled = value;
                Save();
                var h = StateChanged; if (h != null) h(this, EventArgs.Empty);
            }
        }

        public void Load()
        {
            _book = null;
            try
            {
                if (File.Exists(RulesPath))
                {
                    using (var fs = File.OpenRead(RulesPath))
                    {
                        var ser = new DataContractJsonSerializer(typeof(CutsceneBook));
                        _book = (CutsceneBook)ser.ReadObject(fs);
                    }
                }
            }
            catch (Exception ex)
            {
                ActivityLog.General("Cutscenes: rules.json unreadable (" + ex.Message + ") — using defaults");
                try { File.Copy(RulesPath, RulesPath + ".bad", true); } catch { }
            }

            if (_book == null && !File.Exists(RulesPath))
            {
                try
                {
                    string sample = Path.Combine(AlohaPaths.BaseDir, "cutscenes.sample.json");
                    if (File.Exists(sample))
                    {
                        File.Copy(sample, RulesPath, false);
                        using (var fs = File.OpenRead(RulesPath))
                        {
                            var ser = new DataContractJsonSerializer(typeof(CutsceneBook));
                            _book = (CutsceneBook)ser.ReadObject(fs);
                        }
                        ActivityLog.General("Cutscenes: seeded rules.json from the sample book");
                    }
                }
                catch (Exception ex) { ActivityLog.General("Cutscenes: seed failed — " + ex.Message); }
            }

            if (_book == null) _book = CutsceneBook.Defaults();
            _book.Normalize();
            RetireRules();

            _lastPlayed = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            _playedEver.Clear();
            try
            {
                if (File.Exists(StatePath))
                {
                    foreach (var line in File.ReadAllLines(StatePath, Encoding.UTF8))
                    {
                        int tab = line.IndexOf('\t');
                        if (tab <= 0) continue;
                        string id = line.Substring(0, tab);
                        DateTime when;
                        if (DateTime.TryParse(line.Substring(tab + 1), null,
                                System.Globalization.DateTimeStyles.RoundtripKind, out when))
                        {
                            _lastPlayed[id] = when;
                            _playedEver.Add(id);
                        }
                    }
                }
            }
            catch { }
        }

        private static readonly string[] RetiredSampleIds = { "onion-first" };   

        private void RetireRules()
        {
            if (_book == null || _book.Rules == null || _book.Rules.Length == 0) return;
            var keep = new List<CutsceneRule>();
            bool changed = false;
            foreach (var r in _book.Rules)
            {
                if (r != null && Array.IndexOf(RetiredSampleIds, r.Id) >= 0) { changed = true; continue; }
                keep.Add(r);
            }
            if (!changed) return;
            _book.Rules = keep.ToArray();
            ActivityLog.General("Cutscenes: retired sample rule(s) removed from rules.json");
            Save();
        }

        public void Save()
        {
            try
            {
                string tmp = RulesPath + ".tmp";
                using (var fs = File.Create(tmp))
                {
                    var ser = new DataContractJsonSerializer(typeof(CutsceneBook));
                    ser.WriteObject(fs, _book);
                }
                if (File.Exists(RulesPath)) File.Delete(RulesPath);
                File.Move(tmp, RulesPath);
            }
            catch (Exception ex) { ActivityLog.General("Cutscenes: save failed — " + ex.Message); }
        }

        private void SaveState()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var kv in _lastPlayed)
                    sb.Append(kv.Key).Append('\t').Append(kv.Value.ToString("o")).Append("\r\n");
                File.WriteAllText(StatePath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public void ResetHistory()
        {
            _lastPlayed.Clear();
            _playedEver.Clear();
            _playedThisSession = 0;
            _lastAnyPlay = DateTime.MinValue;
            try { if (File.Exists(StatePath)) File.Delete(StatePath); } catch { }
        }

        public void NotifyNavigation(string uri, bool userTyped)
        {
            try
            {
                var rule = Choose(uri, userTyped);
                if (rule != null) Play(rule);
            }
            catch (Exception ex) { ActivityLog.General("Cutscenes: " + ex.Message); }
        }

        public CutsceneRule Choose(string uri, bool userTyped)
        {
            if (_owner == null || _owner.IsDisposed) return null;
            if (_live != null && !_live.IsDisposed) return null;
            if (!Book.Enabled) return null;
            if (userTyped && !Book.AllowOnTypedNavigation) return null;
            if (_playedThisSession >= Book.MaxPerSession) return null;
            if ((DateTime.UtcNow - _lastAnyPlay).TotalMinutes < Book.GlobalCooldownMin) return null;

            string host;
            try { host = new Uri(uri).Host; }
            catch { return null; }
            if (string.IsNullOrEmpty(host)) return null;

            if (host.EndsWith(".aloha", StringComparison.OrdinalIgnoreCase)) return null;
            if (host == "127.0.0.1" || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return null;

            DateTime local = DateTime.Now;
            var eligible = new List<CutsceneRule>();
            foreach (var r in Book.Rules)
            {
                if (r == null || !r.Enabled) continue;
                if (r.OnceEver && _playedEver.Contains(r.Id)) continue;
                if (!r.MatchesHost(host)) continue;
                if (!r.MatchesDay(local)) continue;
                if (!r.MatchesTime(local)) continue;

                DateTime last;
                if (_lastPlayed.TryGetValue(r.Id, out last))
                {
                    if (r.CooldownMin > 0 && (DateTime.UtcNow - last).TotalMinutes < r.CooldownMin) continue;
                    if (r.OncePerDay && last.ToLocalTime().Date == local.Date) continue;
                }
                eligible.Add(r);
            }
            if (eligible.Count == 0) return null;

            eligible.Sort((a, b) => Specificity(b, host).CompareTo(Specificity(a, host)));
            var pick = eligible[0];
            if (pick.Chance < 1.0 && _rng.NextDouble() > pick.Chance) return null;
            return pick;
        }

        private static int Specificity(CutsceneRule r, string host)
        {
            int best = 0;
            foreach (var p in r.Hosts)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (p == "*") { if (best < 1) best = 1; }
                else if (p.StartsWith("*.", StringComparison.Ordinal)) { if (best < 2) best = 2; }
                else if (host.Equals(p, StringComparison.OrdinalIgnoreCase)) best = 3;
            }
            return best;
        }

        public void Play(CutsceneRule rule)
        {
            if (rule == null || _owner == null || _owner.IsDisposed) return;
            if (_live != null && !_live.IsDisposed) return;

            _playedThisSession++;
            _lastAnyPlay = DateTime.UtcNow;
            _lastPlayed[rule.Id] = DateTime.UtcNow;
            _playedEver.Add(rule.Id);
            SaveState();
            ActivityLog.General("Cutscene: " + rule.Id);

            var w = new CutsceneWindow(rule, _owner, Book.FullDesktop, Dir);
            _live = w;
            w.FormClosed += (s, e) => { if (ReferenceEquals(_live, w)) _live = null; };
            w.Show(_owner);
        }

        public bool Preview(string id)
        {
            foreach (var r in Book.Rules)
                if (r != null && r.Id == id) { Play(r); return true; }
            return false;
        }

        public void DismissLive()
        {
            var w = _live;
            if (w != null && !w.IsDisposed) w.Close();
        }
    }
}
