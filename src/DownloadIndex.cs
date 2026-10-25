using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Aloha
{
    public sealed class DlEntry
    {
        public string Name, Path, Url, Host, Ext, State;
        public long Size;
        public DateTime When;
        public bool Exists;
        public DownloadRecord Rec;
        public string Dir { get { try { return System.IO.Path.GetDirectoryName(Path) ?? ""; } catch { return ""; } } }
    }

    public static class DlCategories
    {
        public static readonly Dictionary<string, string[]> Map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "image",   new[] { "png","jpg","jpeg","jfif","gif","bmp","webp","avif","heic","heif","tif","tiff","ico","svg","psd","raw","dng" } },
            { "video",   new[] { "mp4","mkv","webm","mov","avi","wmv","flv","m4v","mpg","mpeg","ts","3gp" } },
            { "audio",   new[] { "mp3","wav","flac","ogg","opus","m4a","aac","wma","aiff","mid","midi" } },
            { "doc",     new[] { "pdf","doc","docx","odt","rtf","txt","md","xls","xlsx","ods","csv","ppt","pptx","odp","epub","mhtml","html","htm" } },
            { "archive", new[] { "zip","7z","rar","tar","gz","tgz","bz2","xz","zst","cab","iso","img" } },
            { "program", new[] { "exe","msi","msix","appx","bat","cmd","ps1","com","scr","jar","apk","dmg","deb","rpm" } },
            { "code",    new[] { "c","cpp","h","cs","py","js","ts","java","go","rs","rb","php","lua","json","xml","yml","yaml","sh" } },
            { "text",    new[] { "txt","md","log","csv","ini","cfg","json","xml" } },
        };
        public static readonly Dictionary<string, string> Alias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "images","image" },{ "img","image" },{ "pic","image" },{ "pics","image" },{ "photo","image" },{ "photos","image" },
            { "videos","video" },{ "movie","video" },{ "movies","video" },
            { "music","audio" },{ "sound","audio" },
            { "docs","doc" },{ "document","doc" },{ "documents","doc" },
            { "archives","archive" },{ "compressed","archive" },
            { "programs","program" },{ "app","program" },{ "apps","program" },{ "exe","program" },{ "installer","program" },
            { "source","code" },
        };
    }

    public sealed class DlQuery
    {

        public readonly List<string> Words = new List<string>(), NotWords = new List<string>();
        public readonly HashSet<string> Exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> States = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Sites = new List<string>();
        public int? TodFrom, TodTo;
        public DateTime? DateFrom, DateTo;
        public DateTime? Since;
        public readonly HashSet<DayOfWeek> Days = new HashSet<DayOfWeek>();
        public readonly HashSet<int> Months = new HashSet<int>();
        public long? MinSize, MaxSize;
        public bool RedOnly;
        public readonly List<string> Understood = new List<string>();

        private static readonly string[] DayNames = { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };
        private static readonly string[] MonNames = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };

        public static DlQuery Parse(string text)
        {
            var q = new DlQuery();
            foreach (var raw in Tokens(text ?? "")) q.Eat(raw);
            return q;
        }

        private static IEnumerable<string> Tokens(string s)
        {
            var sb = new StringBuilder(); bool quoted = false;
            foreach (char c in s)
            {
                if (c == '"') { quoted = !quoted; if (!quoted && sb.Length > 0) { yield return "\u0001" + sb; sb.Clear(); } continue; }
                if (char.IsWhiteSpace(c) && !quoted) { if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); } continue; }
                sb.Append(c);
            }
            if (sb.Length > 0) yield return (quoted ? "\u0001" : "") + sb;
        }

        private void Eat(string t)
        {

            if (t.Length > 0 && t[0] == '\u0001') { Words.Add(t.Substring(1)); Understood.Add("“" + t.Substring(1) + "”"); return; }
            string l = t.ToLowerInvariant();
            Match m;

            if ((m = Regex.Match(l, @"^(\d{1,2})(?::(\d{2}))?-(\d{1,2})(?::(\d{2}))?$")).Success)
            {
                int h1 = int.Parse(m.Groups[1].Value), h2 = int.Parse(m.Groups[3].Value);
                int m1 = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0, m2 = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 0;
                if (h1 <= 24 && h2 <= 24 && m1 < 60 && m2 < 60)
                {
                    TodFrom = (h1 * 60 + m1) % 1440; TodTo = (h2 * 60 + m2) % 1440;
                    Understood.Add(string.Format("{0:00}:{1:00}\u2013{2:00}:{3:00}", TodFrom / 60, TodFrom % 60, TodTo / 60, TodTo % 60));
                    return;
                }
            }

            if ((m = Regex.Match(l, @"^(\d{1,2}):(\d{2})$")).Success && int.Parse(m.Groups[1].Value) < 24)
            {
                TodFrom = int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value); TodTo = TodFrom + 59;
                Understood.Add(l + " (+59 min)"); return;
            }

            var today = DateTime.Today;
            if (l == "today") { DateFrom = DateTo = today; Understood.Add("today"); return; }
            if (l == "yesterday") { DateFrom = DateTo = today.AddDays(-1); Understood.Add("yesterday"); return; }
            if (l == "week") { Since = DateTime.Now.AddDays(-7); Understood.Add("last 7 days"); return; }
            if (l == "month") { Since = DateTime.Now.AddDays(-30); Understood.Add("last 30 days"); return; }
            if ((m = Regex.Match(l, @"^(\d+)(d|h|w)$")).Success)
            {
                int n = int.Parse(m.Groups[1].Value); string u = m.Groups[2].Value;
                Since = u == "h" ? DateTime.Now.AddHours(-n) : DateTime.Now.AddDays(-(u == "w" ? 7 * n : n));
                Understood.Add("last " + n + (u == "h" ? " h" : u == "w" ? " weeks" : " days")); return;
            }

            if (l.Contains(".."))
            {
                var parts = l.Split(new[] { ".." }, StringSplitOptions.None);
                DateTime a, b;
                if (parts.Length == 2 && TryDate(parts[0], out a) && TryDate(parts[1], out b))
                { DateFrom = a < b ? a : b; DateTo = a < b ? b : a; Understood.Add(DateFrom.Value.ToString("d MMM") + "–" + DateTo.Value.ToString("d MMM yyyy")); return; }
            }
            DateTime d;
            if (TryDate(l, out d)) { DateFrom = DateTo = d; Understood.Add(d.ToString("ddd d MMM yyyy")); return; }

            int di = Array.FindIndex(DayNames, x => l.StartsWith(x) && l.Length <= 9);
            if (di >= 0 && Regex.IsMatch(l, "^(sun|mon|tue|wed|thu|fri|sat)[a-z]*$")) { Days.Add((DayOfWeek)di); Understood.Add(DayNames[di]); return; }
            int mi = Array.FindIndex(MonNames, x => l.StartsWith(x));
            if (mi >= 0 && Regex.IsMatch(l, "^(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*$")) { Months.Add(mi + 1); Understood.Add(MonNames[mi]); return; }

            if ((m = Regex.Match(l, @"^(>=|<=|>|<)(\d+(?:\.\d+)?)(b|k|kb|m|mb|g|gb)?$")).Success)
            {
                long v = Bytes(m.Groups[2].Value, m.Groups[3].Value);
                if (m.Groups[1].Value.StartsWith(">")) MinSize = v; else MaxSize = v;
                Understood.Add(m.Groups[1].Value + " " + DownloadToast.Human(v)); return;
            }
            if ((m = Regex.Match(l, @"^(\d+(?:\.\d+)?)(k|kb|m|mb|g|gb)-(\d+(?:\.\d+)?)(k|kb|m|mb|g|gb)$")).Success)
            {
                MinSize = Bytes(m.Groups[1].Value, m.Groups[2].Value); MaxSize = Bytes(m.Groups[3].Value, m.Groups[4].Value);
                Understood.Add(DownloadToast.Human(MinSize.Value) + "–" + DownloadToast.Human(MaxSize.Value)); return;
            }

            if (l.StartsWith(".") && l.Length > 1) { Exts.Add(l.Substring(1)); Understood.Add(l); return; }
            if (l.StartsWith("ext:") || l.StartsWith("type:"))
            {
                string v = l.Substring(l.IndexOf(':') + 1);
                if (!AddCategory(v)) { Exts.Add(v.TrimStart('.')); Understood.Add("." + v.TrimStart('.')); }
                return;
            }
            if (l == "red" || l == "danger" || l == "dangerous") { RedOnly = true; Understood.Add("⚠ opens-to-run types"); return; }
            if (AddCategory(l)) return;

            switch (l)
            {
                case "done": case "completed": case "finished": States.Add("completed"); Understood.Add("done"); return;
                case "failed": case "interrupted": States.Add("interrupted"); Understood.Add("failed"); return;
                case "running": case "active": case "downloading": States.Add("in progress"); Understood.Add("running"); return;
                case "paused": States.Add("paused"); Understood.Add("paused"); return;
                case "cancelled": case "canceled": States.Add("cancelled"); Understood.Add("cancelled"); return;
                case "missing": case "gone": States.Add("missing"); Understood.Add("missing from disk"); return;
            }

            if (l.StartsWith("site:") && l.Length > 5) { Sites.Add(l.Substring(5)); Understood.Add("from " + l.Substring(5)); return; }
            if (l.StartsWith("@") && l.Length > 1) { Sites.Add(l.Substring(1)); Understood.Add("from " + l.Substring(1)); return; }

            if (l.StartsWith("-") && l.Length > 1) { NotWords.Add(l.Substring(1)); Understood.Add("not “" + l.Substring(1) + "”"); return; }
            Words.Add(l); Understood.Add("“" + l + "”");
        }

        private bool AddCategory(string l)
        {
            string key = DlCategories.Map.ContainsKey(l) ? l : (DlCategories.Alias.ContainsKey(l) ? DlCategories.Alias[l] : null);
            if (key == null) return false;
            foreach (var e in DlCategories.Map[key]) Exts.Add(e);
            Understood.Add(key);
            return true;
        }

        private static long Bytes(string num, string unit)
        {
            double v = double.Parse(num, CultureInfo.InvariantCulture);
            switch ((unit ?? "").ToLowerInvariant())
            {
                case "k": case "kb": return (long)(v * 1024);
                case "m": case "mb": return (long)(v * 1024 * 1024);
                case "g": case "gb": return (long)(v * 1024L * 1024 * 1024);
                default: return (long)v;
            }
        }

        private static bool TryDate(string s, out DateTime d)
        {
            d = default(DateTime);
            Match m;
            if ((m = Regex.Match(s, @"^(\d{4})-(\d{1,2})-(\d{1,2})$")).Success)
                return TryMake(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), out d);
            if ((m = Regex.Match(s, @"^(\d{1,2})[./](\d{1,2})(?:[./](\d{2,4}))?$")).Success)   
            {
                int y = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : DateTime.Today.Year;
                if (y < 100) y += 2000;
                return TryMake(y, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value), out d);
            }
            return false;
        }
        private static bool TryMake(int y, int mo, int da, out DateTime d)
        {
            d = default(DateTime);
            if (mo < 1 || mo > 12 || da < 1 || da > DateTime.DaysInMonth(Math.Max(1, Math.Min(9999, y)), mo)) return false;
            d = new DateTime(y, mo, da); return true;
        }

        public bool Empty
        {
            get
            {
                return Words.Count == 0 && NotWords.Count == 0 && Exts.Count == 0 && States.Count == 0 && Sites.Count == 0 && !TodFrom.HasValue &&
                       !DateFrom.HasValue && !Since.HasValue && Days.Count == 0 && Months.Count == 0 && !MinSize.HasValue && !MaxSize.HasValue && !RedOnly;
            }
        }

        public bool Match(DlEntry e, Func<string, bool> isRed)
        {
            string hay = (e.Name + " " + e.Path + " " + e.Url).ToLowerInvariant();
            foreach (var w in Words) if (hay.IndexOf(w, StringComparison.Ordinal) < 0) return false;
            foreach (var w in NotWords) if (hay.IndexOf(w, StringComparison.Ordinal) >= 0) return false;
            if (Exts.Count > 0 && !Exts.Contains(e.Ext ?? "")) return false;
            if (RedOnly && (isRed == null || !isRed(e.Name))) return false;
            if (Sites.Count > 0) { bool ok = false; foreach (var s in Sites) if ((e.Host ?? "").EndsWith(s, StringComparison.OrdinalIgnoreCase)) ok = true; if (!ok) return false; }
            if (States.Count > 0)
            {
                string st = !e.Exists ? "missing" : (e.State ?? "completed");
                if (st.StartsWith("interrupted") || st.StartsWith("retrying")) st = st.StartsWith("retrying") ? "in progress" : "interrupted";
                if (!States.Contains(st) && !(States.Contains("missing") && !e.Exists)) return false;
            }
            if (MinSize.HasValue && e.Size < MinSize.Value) return false;
            if (MaxSize.HasValue && e.Size > MaxSize.Value) return false;
            if (DateFrom.HasValue && e.When.Date < DateFrom.Value) return false;
            if (DateTo.HasValue && e.When.Date > DateTo.Value) return false;
            if (Since.HasValue && e.When < Since.Value) return false;
            if (Days.Count > 0 && !Days.Contains(e.When.DayOfWeek)) return false;
            if (Months.Count > 0 && !Months.Contains(e.When.Month)) return false;
            if (TodFrom.HasValue)
            {
                int t = e.When.Hour * 60 + e.When.Minute, a = TodFrom.Value, b = TodTo.Value;
                bool inside = a <= b ? (t >= a && t <= b) : (t >= a || t <= b);
                if (!inside) return false;
            }
            return true;
        }
    }

    public static class DlIndexBuilder
    {

        public static List<DlEntry> Build(IList<DownloadRecord> history, IEnumerable<string> folders)
        {
            var byPath = new Dictionary<string, DlEntry>(StringComparer.OrdinalIgnoreCase);
            var list = new List<DlEntry>();
            if (history != null)
                foreach (var r in history)
                {
                    if (r == null) continue;
                    string path = r.Path ?? "";
                    var e = new DlEntry
                    {
                        Rec = r, Name = string.IsNullOrEmpty(r.FileName) ? System.IO.Path.GetFileName(path) : r.FileName,
                        Path = path, Url = r.Url ?? "", State = r.State ?? "", Size = r.TotalBytes > 0 ? r.TotalBytes : r.ReceivedBytes
                    };
                    DateTime w;
                    string stamp = !string.IsNullOrEmpty(r.FinishedUtc) ? r.FinishedUtc : r.StartedUtc;
                    e.When = DateTime.TryParse(stamp, null, DateTimeStyles.RoundtripKind, out w) ? w.ToLocalTime() : DateTime.MinValue;
                    try { e.Exists = path.Length > 0 && File.Exists(path); if (e.Exists) e.Size = new FileInfo(path).Length; } catch { }
                    Fill(e);
                    if (path.Length > 0)
                    {
                        DlEntry prev;
                        if (byPath.TryGetValue(path, out prev)) { if (prev.When <= e.When) { list.Remove(prev); byPath[path] = e; list.Add(e); } continue; }
                        byPath[path] = e;
                    }
                    list.Add(e);
                }
            if (folders != null)
                foreach (var dir in folders)
                {
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                    string[] files; try { files = Directory.GetFiles(dir); } catch { continue; }
                    foreach (var f in files)
                    {
                        if (byPath.ContainsKey(f)) continue;
                        if (f.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                        try
                        {
                            var fi = new FileInfo(f);
                            if ((fi.Attributes & FileAttributes.Hidden) != 0) continue;
                            var e = new DlEntry { Name = fi.Name, Path = f, Size = fi.Length, When = fi.LastWriteTime, Exists = true, State = "on disk", Url = "" };
                            Fill(e); byPath[f] = e; list.Add(e);
                        }
                        catch { }
                    }
                }
            return list;
        }

        private static void Fill(DlEntry e)
        {
            int dot = (e.Name ?? "").LastIndexOf('.');
            e.Ext = dot >= 0 && dot < e.Name.Length - 1 ? e.Name.Substring(dot + 1).ToLowerInvariant() : "";
            try { e.Host = string.IsNullOrEmpty(e.Url) ? "" : new Uri(e.Url).Host; } catch { e.Host = ""; }
        }
    }
}
