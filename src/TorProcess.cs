using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Aloha
{

    public class TorProcess
    {
        public int SocksPort   { get; set; } = 9050;
        public int ControlPort { get; set; } = 9051;
        public string DataDir   { get; private set; }
        public string CookiePath => Path.Combine(DataDir, "control_auth_cookie");
        public bool Running => proc != null && !proc.HasExited;

        public static TorProcess Active { get; private set; }
        public int BootstrapPercent { get; private set; }
        public string LastLine { get; private set; } = "";

        public string TorrcOverride { get; set; }

        public void ResetTorrc()
        {
            try
            {
                string t = Path.Combine(DataDir, "torrc");
                if (File.Exists(t)) File.Delete(t);

                string ph = Path.Combine(DataDir, "torrc.phantom");
                if (File.Exists(ph)) File.Delete(ph);
                ActivityLog.General("TorProcess: torrc reset — next start regenerates from settings");
            }
            catch (Exception ex) { ActivityLog.General("TorProcess: could not reset torrc — " + ex.Message); }
        }

        private static string TryRead(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }

        private static void TryWrite(string path, string text)
        {
            try { File.WriteAllText(path, text); } catch { }
        }

        private static string Norm(string s)
        {
            return s == null ? "" : s.Replace("\r\n", "\n").Trim();
        }

        public string ExePathOverride { get; set; }

        public bool? ConsoleOverride { get; set; }
        public string DataDirOverride
        {
            get { return DataDir; }
            set { if (!string.IsNullOrWhiteSpace(value)) DataDir = value; }
        }

        public event Action<string> Line;

        public event Action Ready;

        public event Action<string> Failed;

        private Process proc;
        private string bridgesLine;

        public TorProcess(string bridges = null, int socksPort = 9050, int controlPort = 9051)
        {
            bridgesLine = bridges;
            SocksPort   = socksPort;
            ControlPort = controlPort;

            DataDir = AlohaPaths.TorData;
        }

        internal static string FindTorExe()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates =
                {
                    Path.Combine(baseDir, "tor", "tor.exe"),
                    Path.Combine(baseDir, "tor", "Tor", "tor.exe"),
                    Path.Combine(baseDir, "tor.exe")
                };
                foreach (var c in candidates) if (File.Exists(c)) return c;
            }
            catch { }
            return "tor";   
        }

        public string BuildTorrc()
        {
            string torExe = FindTorExe();
            var sb = new StringBuilder();
            sb.AppendLine("SocksPort " + SocksPort);
            sb.AppendLine("ControlPort " + ControlPort);
            sb.AppendLine("CookieAuthentication 1");
            sb.AppendLine("DataDirectory " + DataDir);
            sb.AppendLine("Log notice stdout");
            sb.AppendLine("AvoidDiskWrites 1");

            try
            {
                string torDir  = Path.GetDirectoryName(torExe) ?? "";
                string parent  = string.IsNullOrEmpty(torDir) ? "" : (Path.GetDirectoryName(torDir) ?? "");
                string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
                string[] roots = { torDir, Path.Combine(torDir, "data"),
                                   Path.Combine(parent, "data"), parent,
                                   Path.Combine(baseDir, "data"), baseDir };
                foreach (var d in roots)
                {
                    if (string.IsNullOrEmpty(d)) continue;
                    string g4 = Path.Combine(d, "geoip");
                    string g6 = Path.Combine(d, "geoip6");
                    bool any = false;
                    if (File.Exists(g4)) { sb.AppendLine("GeoIPFile " + g4); any = true; }
                    if (File.Exists(g6)) { sb.AppendLine("GeoIPv6File " + g6); any = true; }
                    if (any) break;
                }
                if (!string.IsNullOrWhiteSpace(bridgesLine))
                {
                    string[] ptDirs = { Path.Combine(torDir, "pluggable_transports"), torDir,
                                        Path.Combine(parent, "pluggable_transports"), parent,
                                        Path.Combine(baseDir, "pluggable_transports") };
                    string[] names = { "lyrebird.exe", "obfs4proxy.exe" };
                    bool done = false;
                    foreach (var d in ptDirs)
                    {
                        if (string.IsNullOrEmpty(d)) continue;
                        foreach (var nm in names)
                        {
                            string p = Path.Combine(d, nm);
                            if (File.Exists(p)) { sb.AppendLine("ClientTransportPlugin obfs4,meek_lite,webtunnel exec \"" + p + "\""); done = true; break; }
                        }
                        if (done) break;
                    }
                }
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(bridgesLine))
            {
                sb.AppendLine("UseBridges 1");
                foreach (var b in bridgesLine.Replace("\r", "").Split('\n'))
                {
                    string t = b.Trim();
                    if (t.Length == 0) continue;
                    sb.AppendLine(t.StartsWith("Bridge", StringComparison.OrdinalIgnoreCase) ? t : ("Bridge " + t));
                }
            }
            return sb.ToString();
        }

        private string WriteTorrc()
        {
            Directory.CreateDirectory(DataDir);
            string torrcPath   = Path.Combine(DataDir, "torrc");
            string phantomPath = Path.Combine(DataDir, "torrc.phantom");
            string generated   = string.IsNullOrWhiteSpace(TorrcOverride) ? BuildTorrc() : TorrcOverride;

            string lastPhantom = TryRead(phantomPath);
            string current     = TryRead(torrcPath);

            TryWrite(phantomPath, generated);

            if (current == null)
            {
                File.WriteAllText(torrcPath, generated);
                ActivityLog.General("TorProcess: generated torrc from settings -> " + torrcPath);
                return torrcPath;
            }

            if (lastPhantom != null && Norm(current) == Norm(lastPhantom))
            {
                if (Norm(current) != Norm(generated))
                {
                    File.WriteAllText(torrcPath, generated);
                    ActivityLog.General("TorProcess: torrc refreshed from settings (it was unmodified)");
                }
                return torrcPath;
            }

            ActivityLog.General("TorProcess: torrc is hand-edited — settings NOT applied to it. "
                              + "Delete it or call ResetTorrc() to regenerate. "
                              + "What settings would write is in torrc.phantom.");
            return torrcPath;
        }

        public bool Start()
        {
            if (Running) return true;
            try
            {
                string exe = !string.IsNullOrWhiteSpace(ExePathOverride) && File.Exists(ExePathOverride)
                             ? ExePathOverride
                             : FindTorExe();
                string torrc = WriteTorrc();
                BootstrapPercent = 0;

                bool console = false;
                if (ConsoleOverride.HasValue) console = ConsoleOverride.Value;
                else { try { console = NetConfig.Load().CoreConsoleTor; } catch { } }

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "-f \"" + torrc + "\"",
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
                    UseShellExecute = false,
                    RedirectStandardOutput = !console,
                    RedirectStandardError = !console,
                    CreateNoWindow = !console,
                    WindowStyle = console ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden,
                    StandardOutputEncoding = console ? null : Encoding.UTF8
                };

                proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                if (!console)
                {
                    proc.OutputDataReceived += (s, e) => OnStdout(e.Data);
                    proc.ErrorDataReceived  += (s, e) => OnStdout(e.Data);
                }
                proc.Start();
                if (!console) { proc.BeginOutputReadLine(); proc.BeginErrorReadLine(); }
                if (console) ActivityLog.General("TorProcess: [cmd] on — tor is in its own console, "
                                               + "Aloha's tor log stays empty until you turn it off");
                if (string.IsNullOrWhiteSpace(ExePathOverride) &&
                    string.Equals(DataDir, AlohaPaths.TorData, StringComparison.OrdinalIgnoreCase))
                    Active = this;
                return true;
            }
            catch (Exception ex)
            {
                Failed?.Invoke("could not start tor.exe — is it bundled in .\\tor\\ ? (" + ex.Message + ")");
                proc = null;
                return false;
            }
        }

        private void OnStdout(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            LastLine = data;
            Line?.Invoke(data);

            int i = data.IndexOf("Bootstrapped ", StringComparison.Ordinal);
            if (i >= 0)
            {
                int p = i + "Bootstrapped ".Length, j = p;
                while (j < data.Length && char.IsDigit(data[j])) j++;
                if (j > p && int.TryParse(data.Substring(p, j - p), out int pct))
                {
                    BootstrapPercent = pct;
                    if (pct >= 100) Ready?.Invoke();
                }
            }
        }

        public void Stop()
        {
            try { if (proc != null && !proc.HasExited) proc.Kill(); } catch { }
            try { proc?.Dispose(); } catch { }
            proc = null;
            BootstrapPercent = 0;
            if (ReferenceEquals(Active, this)) Active = null;
        }
    }
}
