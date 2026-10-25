using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Aloha
{
    public enum HotkeyAction
    {
        SavePage, SavePageQuick,
        NewTab, CloseTab, TabBoard,
        Find, Downloads, Bookmarks,
        Home, Reload, Back, Forward, FocusAddress,
        ZoomIn, ZoomOut, ZoomReset,
        DevTools, Help
    }

    public static class Hotkeys
    {
        public sealed class Entry { public HotkeyAction Action; public string Label; public Keys Default; }

        public static readonly Entry[] Table =
        {
            new Entry { Action = HotkeyAction.SavePage,      Label = "Save page… (Save As dialog)",          Default = Keys.Control | Keys.S },
            new Entry { Action = HotkeyAction.SavePageQuick, Label = "Save page now (MHTML into Downloads)",       Default = Keys.Control | Keys.Shift | Keys.S },
            new Entry { Action = HotkeyAction.NewTab,        Label = "New tab",                                     Default = Keys.Control | Keys.T },
            new Entry { Action = HotkeyAction.CloseTab,      Label = "Close tab",                                   Default = Keys.Control | Keys.W },
            new Entry { Action = HotkeyAction.TabBoard,      Label = "Tab board",                                   Default = Keys.Control | Keys.Shift | Keys.M },
            new Entry { Action = HotkeyAction.Find,          Label = "Find in page",                                Default = Keys.Control | Keys.F },
            new Entry { Action = HotkeyAction.Downloads,     Label = "Downloads window",                            Default = Keys.Control | Keys.J },
            new Entry { Action = HotkeyAction.Bookmarks,     Label = "Bookmarks window",                            Default = Keys.Control | Keys.B },
            new Entry { Action = HotkeyAction.Home,          Label = "Home page",                                   Default = Keys.Alt | Keys.Home },
            new Entry { Action = HotkeyAction.Reload,        Label = "Reload",                                      Default = Keys.F5 },
            new Entry { Action = HotkeyAction.Back,          Label = "Back",                                        Default = Keys.Alt | Keys.Left },
            new Entry { Action = HotkeyAction.Forward,       Label = "Forward",                                     Default = Keys.Alt | Keys.Right },
            new Entry { Action = HotkeyAction.FocusAddress,  Label = "Focus the address bar",                       Default = Keys.Control | Keys.L },
            new Entry { Action = HotkeyAction.ZoomIn,        Label = "Zoom in",                                     Default = Keys.Control | Keys.Oemplus },
            new Entry { Action = HotkeyAction.ZoomOut,       Label = "Zoom out",                                    Default = Keys.Control | Keys.OemMinus },
            new Entry { Action = HotkeyAction.ZoomReset,     Label = "Zoom 100%",                                   Default = Keys.Control | Keys.D0 },
            new Entry { Action = HotkeyAction.DevTools,      Label = "DevTools console",                            Default = Keys.F12 },
            new Entry { Action = HotkeyAction.Help,          Label = "Help",                                        Default = Keys.F1 },
        };

        private static readonly Dictionary<HotkeyAction, Keys> _map = new Dictionary<HotkeyAction, Keys>();
        private static bool _loaded;
        private static readonly KeysConverter _kc = new KeysConverter();

        private static string Path_ { get { return Path.Combine(AlohaPaths.Root, "hotkeys.cfg"); } }

        public static Keys Get(HotkeyAction a)
        {
            EnsureLoaded();
            Keys k; return _map.TryGetValue(a, out k) ? k : Keys.None;
        }

        public static void Set(HotkeyAction a, Keys k) { EnsureLoaded(); _map[a] = k; }

        public static void ResetDefaults() { _map.Clear(); foreach (var e in Table) _map[e.Action] = e.Default; _loaded = true; }

        public static HotkeyAction? Lookup(Keys chord)
        {
            EnsureLoaded();
            if (chord == Keys.None) return null;
            foreach (var kv in _map) if (kv.Value == chord) return kv.Key;
            return null;
        }

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            ResetDefaults();
            try
            {
                if (File.Exists(Path_))
                {
                    foreach (var raw in File.ReadAllLines(Path_, Encoding.UTF8))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line[0] == '#') continue;
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        HotkeyAction a;
                        if (!TryParseAction(line.Substring(0, eq).Trim(), out a)) continue;
                        _map[a] = Parse(line.Substring(eq + 1).Trim());
                    }
                }
            }
            catch (Exception ex) { ActivityLog.General("hotkeys: " + Path_ + " unreadable — " + ex.Message); }
        }

        public static void Save()
        {
            EnsureLoaded();
            try
            {
                var sb = new StringBuilder();
                sb.Append("# Aloha hotkeys — Action=Keys (KeysConverter spelling, e.g. \"Control, Shift, S\"; \"None\" unbinds)\r\n");
                foreach (var e in Table) sb.Append(e.Action).Append('=').Append(Format(Get(e.Action))).Append("\r\n");
                string tmp = Path_ + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                if (File.Exists(Path_)) File.Delete(Path_);
                File.Move(tmp, Path_);
            }
            catch (Exception ex) { ActivityLog.General("hotkeys: save failed — " + ex.Message); }
        }

        private static bool TryParseAction(string s, out HotkeyAction a)
        {
            foreach (HotkeyAction v in Enum.GetValues(typeof(HotkeyAction)))
                if (string.Equals(v.ToString(), s, StringComparison.OrdinalIgnoreCase)) { a = v; return true; }
            a = HotkeyAction.SavePage; return false;
        }

        public static Keys Parse(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Equals("None", StringComparison.OrdinalIgnoreCase)) return Keys.None;
            try { return (Keys)_kc.ConvertFromInvariantString(s); } catch { return Keys.None; }
        }

        public static string Format(Keys k)
        {
            if (k == Keys.None) return "None";
            try { return _kc.ConvertToInvariantString(k); } catch { return "None"; }
        }

        public static string Pretty(Keys k)
        {
            if (k == Keys.None) return "—";
            var sb = new StringBuilder();
            if ((k & Keys.Control) != 0) sb.Append("Ctrl+");
            if ((k & Keys.Alt) != 0) sb.Append("Alt+");
            if ((k & Keys.Shift) != 0) sb.Append("Shift+");
            Keys code = k & Keys.KeyCode;
            string name;
            switch (code)
            {
                case Keys.Oemplus: name = "="; break;
                case Keys.OemMinus: name = "-"; break;
                case Keys.D0: case Keys.D1: case Keys.D2: case Keys.D3: case Keys.D4:
                case Keys.D5: case Keys.D6: case Keys.D7: case Keys.D8: case Keys.D9:
                    name = ((int)code - (int)Keys.D0).ToString(); break;
                case Keys.Left: name = "←"; break;
                case Keys.Right: name = "→"; break;
                case Keys.Up: name = "↑"; break;
                case Keys.Down: name = "↓"; break;
                default: name = code.ToString(); break;
            }
            return sb.Append(name).ToString();
        }

        private static string ChordId(Keys k)
        {
            var sb = new StringBuilder();
            if ((k & Keys.Control) != 0) sb.Append("C");
            if ((k & Keys.Alt) != 0) sb.Append("A");
            if ((k & Keys.Shift) != 0) sb.Append("S");
            sb.Append('+').Append((int)(k & Keys.KeyCode));
            return sb.ToString();
        }

        public static Keys FromChordId(string id)
        {
            try
            {
                int plus = id.IndexOf('+');
                string mods = id.Substring(0, plus); int code = int.Parse(id.Substring(plus + 1));
                Keys k = (Keys)code;
                if (mods.IndexOf('C') >= 0) k |= Keys.Control;
                if (mods.IndexOf('A') >= 0) k |= Keys.Alt;
                if (mods.IndexOf('S') >= 0) k |= Keys.Shift;
                return k;
            }
            catch { return Keys.None; }
        }

        public static string PageScript()
        {
            EnsureLoaded();
            var ids = new List<string>();
            foreach (var kv in _map)
            {
                Keys k = kv.Value; if (k == Keys.None) continue;
                bool mod = (k & (Keys.Control | Keys.Alt)) != 0;
                Keys code = k & Keys.KeyCode;
                bool fkey = code >= Keys.F1 && code <= Keys.F24;
                if (!mod && !fkey) continue;
                ids.Add("\"" + ChordId(k) + "\"");
            }
            return @"(function(){
  var BOUND = {" + string.Join(",", ids.ConvertAll(i => i + ":1")) + @"};
  document.addEventListener('keydown', function(e){
    if(!e.isTrusted || e.metaKey) return;
    var id = (e.ctrlKey?'C':'') + (e.altKey?'A':'') + (e.shiftKey?'S':'') + '+' + e.keyCode;
    if(!BOUND[id]) return;
    try { e.preventDefault(); e.stopPropagation(); } catch(_){}
    try { window.chrome.webview.postMessage('alohakey|' + id); } catch(_){}
  }, true);
})();";
        }
    }
}
