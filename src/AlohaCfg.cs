using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace Aloha
{

    public static class AlohaCfg
    {
        public static string FilePath  { get { return Path.Combine(AlohaPaths.Root, "aloha.cfg"); } }
        public static string TracePath { get { return Path.Combine(AlohaPaths.Root, "aloha.cfg.log"); } }

        private static string LegacyJson
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AlohaNet.json"); }
        }

        private static void Trace(string line)
        {
            try { ActivityLog.General("cfg: " + line); } catch { }
            try
            {
                Directory.CreateDirectory(AlohaPaths.Root);
                File.AppendAllText(TracePath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + line + Environment.NewLine);
            }
            catch { }
        }

        private static FieldInfo[] Fields()
        {
            return typeof(NetConfig).GetFields(BindingFlags.Public | BindingFlags.Instance);
        }

        private static string Key(string name)
        {
            var sb = new StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsUpper(c))
                {
                    bool boundary = i > 0 && (!char.IsUpper(name[i - 1]) ||
                                    (i + 1 < name.Length && char.IsLower(name[i + 1])));
                    if (boundary) sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static Dictionary<string, FieldInfo> Map()
        {
            var d = new Dictionary<string, FieldInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Fields()) d[Key(f.Name)] = f;
            return d;
        }

        private static string Fmt(object v, Type t)
        {
            if (v == null) return "";
            if (t == typeof(bool))   return ((bool)v) ? "1" : "0";
            if (t == typeof(int))    return ((int)v).ToString(CultureInfo.InvariantCulture);
            if (t == typeof(double)) return ((double)v).ToString("R", CultureInfo.InvariantCulture);
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static bool TryParse(string s, Type t, out object v)
        {
            v = null;
            if (t == typeof(bool))
            {
                string x = (s ?? "").Trim().ToLowerInvariant();
                v = (x == "1" || x == "true" || x == "yes" || x == "on");
                return true;
            }
            if (t == typeof(int))
            {
                int i;
                if (!int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out i))
                    return false;
                v = i; return true;
            }
            if (t == typeof(double))
            {
                double d;
                if (!double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    return false;
                v = d; return true;
            }
            if (t == typeof(string)) { v = s ?? ""; return true; }
            return false;
        }

        private static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    default:   sb.Append(c);      break;
                }
            }
            return sb.Append('"').ToString();
        }

        private static string Unquote(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                s = s.Substring(1, s.Length - 2);
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    if (n == 'n')      sb.Append('\n');
                    else if (n == 'r') sb.Append('\r');
                    else               sb.Append(n);
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        public static string Compose(NetConfig cfg)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// Aloha configuration");
            sb.AppendLine("// Written " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " by Aloha " + Form1.VERSION);
            sb.AppendLine("//");
            sb.AppendLine("//   key \"value\"      one per line, booleans are 1/0");
            sb.AppendLine("//   // at the START of a line is a comment; later in a line it is not,");
            sb.AppendLine("//   so URLs are safe. Unknown keys are ignored (and counted in aloha.cfg.log).");
            sb.AppendLine("//");
            sb.AppendLine("// Rewritten in full on every save. Hand edits survive only while Aloha is closed.");
            sb.AppendLine();

            foreach (var f in Fields())
            {
                string key = Key(f.Name);
                object val = f.GetValue(cfg);

                if (f.FieldType == typeof(List<string>))
                {
                    var list = val as List<string>;
                    if (list == null || list.Count == 0)
                        sb.AppendLine("// " + key + "   (none)");
                    else
                        foreach (var item in list)
                            sb.AppendLine(Pad(key) + Quote(item ?? ""));
                    continue;
                }

                if (f.FieldType == typeof(bool) || f.FieldType == typeof(int) ||
                    f.FieldType == typeof(double) || f.FieldType == typeof(string))
                {
                    sb.AppendLine(Pad(key) + Quote(Fmt(val, f.FieldType)));
                    continue;
                }

                sb.AppendLine("// " + key + "   (unsupported type " + f.FieldType.Name + " — NOT SAVED)");
                Trace("compose: field " + f.Name + " has unsupported type "
                      + f.FieldType.Name + " — not written");
            }
            return sb.ToString();
        }

        private static string Pad(string key)
        {
            return key.Length >= 34 ? key + " " : key.PadRight(34);
        }

        public static NetConfig Parse(string text, out int applied, out int unknown)
        {
            applied = 0; unknown = 0;
            var cfg = new NetConfig();
            var map = Map();
            var listsCleared = new HashSet<string>();

            foreach (var raw in (text ?? "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//")) continue;

                int sp = -1;
                for (int i = 0; i < line.Length; i++)
                    if (char.IsWhiteSpace(line[i])) { sp = i; break; }

                string key = sp < 0 ? line : line.Substring(0, sp);
                string val = sp < 0 ? ""   : line.Substring(sp).Trim();

                FieldInfo f;
                if (!map.TryGetValue(key, out f))
                {
                    unknown++;
                    Trace("parse: unknown key '" + key + "' ignored");
                    continue;
                }

                if (f.FieldType == typeof(List<string>))
                {
                    var list = f.GetValue(cfg) as List<string>;
                    if (list == null) { list = new List<string>(); f.SetValue(cfg, list); }
                    if (listsCleared.Add(key)) list.Clear();
                    list.Add(Unquote(val));
                    applied++;
                    continue;
                }

                object parsed;
                if (TryParse(Unquote(val), f.FieldType, out parsed))
                {
                    f.SetValue(cfg, parsed);
                    applied++;
                }
                else Trace("parse: could not read " + key + " = " + val
                           + " as " + f.FieldType.Name + " — keeping default");
            }
            return cfg;
        }

        private static string Render(FieldInfo f, NetConfig c)
        {
            if (f.FieldType == typeof(List<string>))
            {
                var l = f.GetValue(c) as List<string>;
                return l == null ? "" : string.Join("|", l.ToArray());
            }
            return Fmt(f.GetValue(c), f.FieldType);
        }

        private static List<string> Diff(NetConfig wrote, NetConfig readBack)
        {
            var outp = new List<string>();
            foreach (var f in Fields())
            {
                if (f.FieldType != typeof(bool) && f.FieldType != typeof(int) &&
                    f.FieldType != typeof(double) && f.FieldType != typeof(string) &&
                    f.FieldType != typeof(List<string>)) continue;

                string a = Render(f, wrote), b = Render(f, readBack);
                if (a != b)
                    outp.Add(Key(f.Name) + ": wrote \"" + a + "\" -> read back \"" + b + "\"");
            }
            return outp;
        }

        public static bool Save(NetConfig cfg)
        {
            string p = FilePath;
            if (cfg == null) { Trace("save: null config, ignored"); return false; }

            try
            {
                Directory.CreateDirectory(AlohaPaths.Root);
                string tmp = p + ".tmp";
                File.WriteAllText(tmp, Compose(cfg), new UTF8Encoding(false));
                if (File.Exists(p)) File.Replace(tmp, p, null);
                else File.Move(tmp, p);
            }
            catch (Exception ex)
            {
                Trace("WRITE FAILED  " + p + "  — " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }

            try
            {
                int applied, unknown;
                var back  = Parse(File.ReadAllText(p), out applied, out unknown);
                var diffs = Diff(cfg, back);
                if (diffs.Count == 0)
                {
                    Trace("write OK  " + p + "  (" + applied + " settings, verified"
                        + (AlohaPaths.IsPortable ? "" : ", NON-PORTABLE fallback root") + ")");
                    return true;
                }
                Trace("WRITE VERIFY MISMATCH  " + p + "  — " + diffs.Count + " field(s) did not survive:");
                foreach (var d in diffs) Trace("    " + d);
                return false;
            }
            catch (Exception ex)
            {
                Trace("WRITE VERIFY FAILED  " + p + "  — " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        public static NetConfig Load()
        {
            string p = FilePath;
            try
            {
                if (!File.Exists(p))
                {
                    var migrated = MigrateFromJson();
                    if (migrated != null) return migrated;

                    Trace("no cfg at " + p + " — writing defaults");
                    var fresh = new NetConfig();
                    Save(fresh);
                    return fresh;
                }

                int applied, unknown;
                var cfg = Parse(File.ReadAllText(p), out applied, out unknown);
                Trace("read OK  " + p + "  (" + applied + " settings"
                    + (unknown > 0 ? ", " + unknown + " unknown key(s) ignored" : "") + ")");
                return cfg;
            }
            catch (Exception ex)
            {

                try
                {
                    string bad = p + ".bad";
                    if (File.Exists(bad)) File.Delete(bad);
                    File.Copy(p, bad);
                }
                catch { }
                Trace("READ FAILED  " + p + "  — " + ex.GetType().Name + ": " + ex.Message
                    + "  (copy kept as aloha.cfg.bad)");
                return new NetConfig();
            }
        }

        private static NetConfig MigrateFromJson()
        {
            try
            {
                string j = LegacyJson;
                if (!File.Exists(j)) return null;

                var cfg = NetConfig.Load(j);
                if (cfg == null) return null;

                Trace("migrating " + j + "  ->  " + FilePath);
                Save(cfg);
                return cfg;
            }
            catch (Exception ex)
            {
                Trace("migration from AlohaNet.json failed — " + ex.Message);
                return null;
            }
        }
    }
}
