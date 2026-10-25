using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Aloha
{
    public static class TorrcSyntax
    {
        public static readonly Color Green   = Color.FromArgb(0x7F, 0xAE, 0x86);
        public static readonly Color GreenHi = Color.FromArgb(0xB4, 0xDC, 0xBA);
        public static readonly Color GreenLo = Color.FromArgb(0x3E, 0x5C, 0x44);
        public static readonly Color GreenTop = Color.FromArgb(0xE6, 0xF4, 0xE8);
        public static readonly Color Amber   = Color.FromArgb(0xFF, 0xB6, 0x2E);
        public const int GUTTER = 34;

        public static readonly HashSet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DataDirectory","SocksPort","ControlPort","DNSPort","TransPort","HTTPTunnelPort","HiddenServiceDir","HiddenServicePort",
            "HiddenServiceVersion","HiddenServiceAuthorizeClient","HiddenServiceMaxStreams","HiddenServiceMaxStreamsCloseCircuit",
            "HiddenServiceNumIntroductionPoints","HiddenServiceSingleHopMode","HiddenServiceNonAnonymousMode","HiddenServiceEnableIntroDoSDefense",
            "HiddenServicePoWDefensesEnabled","DisableNetwork","CookieAuthentication","CookieAuthFile","CookieAuthFileGroupReadable","HashedControlPassword",
            "Log","SafeLogging","RunAsDaemon","ClientOnly","ExitPolicy","ExitRelay","ORPort","Nickname","ContactInfo",
            "UseBridges","Bridge","ClientTransportPlugin","EntryNodes","ExitNodes","ExcludeNodes","ExcludeExitNodes","StrictNodes",
            "NumEntryGuards","CircuitBuildTimeout","LearnCircuitBuildTimeout","MaxCircuitDirtiness","NewCircuitPeriod",
            "IsolateDestAddr","IsolateDestPort","IsolateSOCKSAuth","AutomapHostsOnResolve","AutomapHostsSuffixes","VirtualAddrNetworkIPv4",
            "MapAddress","GeoIPFile","GeoIPv6File","AvoidDiskWrites","ConnectionPadding","ReducedConnectionPadding","FascistFirewall",
            "ReachableAddresses","HTTPSProxy","Socks5Proxy","Socks4Proxy","Socks5ProxyUsername","Socks5ProxyPassword","SocksPolicy",
            "ClientUseIPv6","ClientPreferIPv6ORPort","KeepalivePeriod","Sandbox","OnionTrafficOnly","%include","ControlSocket",
            "ClientOnionAuthDir","HiddenServiceOnionBalanceInstance","LongLivedPorts","WarnPlaintextPorts","RejectPlaintextPorts",
            "__OwningControllerProcess","__DisablePredictedCircuits","DormantCanceledByStartup","DormantOnFirstStartup","FetchDirInfoEarly",
            "BandwidthRate","BandwidthBurst","MaxClientCircuitsPending","CircuitsAvailableTimeout","UseEntryGuards","PathsNeededToBuildCircuits"
        };

        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        private const int WM_SETREDRAW = 0x0B;

        public static List<string> Colorize(RichTextBox box)
        {
            var unknown = new List<string>();
            if (box == null || box.IsDisposed || !box.IsHandleCreated) return unknown;
            int selStart = box.SelectionStart, selLen = box.SelectionLength;
            SendMessage(box.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            try
            {
                box.SelectAll(); box.SelectionColor = Green;
                string[] lines = box.Lines;
                for (int i = 0; i < lines.Length; i++)
                {
                    string ln = lines[i];
                    int start = box.GetFirstCharIndexFromLine(i); if (start < 0) break;
                    string t = ln.TrimStart(); int lead = ln.Length - t.Length;
                    if (t.Length == 0) continue;
                    if (t[0] == '#') { box.Select(start, ln.Length); box.SelectionColor = GreenLo; continue; }
                    int sp = t.IndexOfAny(new[] { ' ', '\t' }); string key = sp < 0 ? t : t.Substring(0, sp);
                    bool ok = Known.Contains(key);
                    if (!ok && !unknown.Contains(key)) unknown.Add(key);
                    box.Select(start + lead, key.Length); box.SelectionColor = ok ? GreenHi : Amber;
                    int hash = ln.IndexOf(" #", StringComparison.Ordinal);
                    if (hash > 0) { box.Select(start + hash, ln.Length - hash); box.SelectionColor = GreenLo; }
                }
                box.Select(selStart, selLen);
            }
            finally
            {
                SendMessage(box.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                box.Invalidate();
            }
            return unknown;
        }

        public static void PaintGutter(Graphics g, Control gutter, RichTextBox box)
        {
            g.Clear(Color.Black);
            using (var p = new Pen(GreenLo)) g.DrawLine(p, gutter.Width - 1, 0, gutter.Width - 1, gutter.Height);
            if (box == null || box.TextLength == 0) return;
            int first = box.GetLineFromCharIndex(box.GetCharIndexFromPosition(new Point(1, 1)));
            int caretLine = box.GetLineFromCharIndex(box.SelectionStart);
            int lines = box.Lines.Length;
            int dy = box.Top - gutter.Top;
            for (int i = first; i < lines; i++)
            {
                int ci = box.GetFirstCharIndexFromLine(i); if (ci < 0) break;
                int y = box.GetPositionFromCharIndex(ci).Y + dy;
                if (y > gutter.Height) break;
                TextRenderer.DrawText(g, (i + 1).ToString(), box.Font, new Rectangle(0, y, gutter.Width - 5, 16),
                    i == caretLine ? GreenHi : GreenLo, TextFormatFlags.Right | TextFormatFlags.NoPadding);
            }
        }

        public static void Verify(string torExe, string torrcText, Control ui, Action<bool, string> done)
        {
            if (string.IsNullOrEmpty(torExe) || !File.Exists(torExe)) { done(false, "tor.exe not found (" + (torExe ?? "no path") + ")"); return; }
            string tmp = Path.Combine(Path.GetTempPath(), "aloha-torrc-check-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            File.WriteAllText(tmp, torrcText ?? "", new UTF8Encoding(false));
            Task.Run(() =>
            {
                string outText; int code = -1;
                try
                {
                    var psi = new ProcessStartInfo(torExe, "--verify-config -f \"" + tmp + "\"")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(torExe) };
                    using (var p = Process.Start(psi))
                    {
                        var o = p.StandardOutput.ReadToEndAsync(); var er = p.StandardError.ReadToEndAsync();
                        if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } }
                        outText = o.Result + er.Result;
                        code = p.HasExited ? p.ExitCode : -1;
                    }
                }
                catch (Exception ex) { outText = ex.Message; }
                try { File.Delete(tmp); } catch { }
                var keep = new StringBuilder();
                foreach (var raw in outText.Replace("\r", "").Split('\n'))
                {
                    string l = raw.Trim(); if (l.Length == 0) continue;
                    int b = l.IndexOf('['); if (b > 0 && b < 30) l = l.Substring(b);
                    if (l.StartsWith("[notice] Tor ", StringComparison.Ordinal) || l.StartsWith("[notice] Read configuration", StringComparison.Ordinal)) continue;
                    keep.AppendLine(l);
                }
                bool ok = code == 0;
                string verdict = (ok ? "✓ Tor accepts this torrc." : "✗ Tor rejects this torrc (exit " + code + "):") + (keep.Length > 0 ? "\r\n" + keep.ToString().Trim() : "");
                try { ui.BeginInvoke((Action)(() => done(ok, verdict))); } catch { }
            });
        }
    }
}
