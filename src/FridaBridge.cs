using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text;

namespace Aloha
{

    public class FridaBridge : IDisposable
    {
        public event Action<string> Line;
        public event Action<string> Error;

        private Process proc;
        private readonly int browserPid;
        private int targetPid;
        private readonly string agentPath;
        private bool started;

        public int TargetPid { get { return targetPid; } }
        public bool Running { get { return started && proc != null && !proc.HasExited; } }

        public FridaBridge(int webViewBrowserPid, string agentScriptPath)
        {
            browserPid = webViewBrowserPid;
            agentPath = agentScriptPath;
        }

        private static System.Collections.Generic.List<string> FindPythonExes()
        {
            var found = new System.Collections.Generic.List<string>();
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            var roots = new System.Collections.Generic.List<string>();

            string pyBase = System.IO.Path.Combine(local, "Python");
            if (System.IO.Directory.Exists(pyBase))
            {
                try
                {
                    foreach (var d in System.IO.Directory.GetDirectories(pyBase))
                        roots.Add(d);
                    roots.Add(System.IO.Path.Combine(pyBase, "bin"));
                }
                catch { }
            }

            string progPy = System.IO.Path.Combine(local, "Programs", "Python");
            if (System.IO.Directory.Exists(progPy))
            {
                try { foreach (var d in System.IO.Directory.GetDirectories(progPy)) roots.Add(d); }
                catch { }
            }

            foreach (var r in roots)
            {
                try
                {
                    string exe = System.IO.Path.Combine(r, "python.exe");
                    if (System.IO.File.Exists(exe) && !found.Contains(exe)) found.Add(exe);
                }
                catch { }
            }
            return found;
        }

        private int ResolveRendererPid()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, ParentProcessId, CommandLine FROM Win32_Process " +
                    "WHERE Name = 'msedgewebview2.exe'"))
                using (var results = searcher.Get())
                {
                    int firstChild = 0;
                    foreach (ManagementObject mo in results)
                    {
                        int ppid = Convert.ToInt32(mo["ParentProcessId"] ?? 0);
                        int pid  = Convert.ToInt32(mo["ProcessId"] ?? 0);
                        string cmd = (mo["CommandLine"] as string) ?? "";
                        if (ppid == browserPid || pid == browserPid)
                        {
                            if (firstChild == 0 && pid != browserPid) firstChild = pid;
                            if (cmd.IndexOf("--type=renderer", StringComparison.OrdinalIgnoreCase) >= 0)
                                return pid;
                        }
                    }
                    if (firstChild != 0) return firstChild;
                }
            }
            catch (Exception ex)
            {
                Error?.Invoke("renderer lookup failed: " + ex.Message);
            }
            return browserPid;
        }

        public bool Start()
        {
            if (started) return Running;
            started = true;

            if (!File.Exists(agentPath))
            {
                Error?.Invoke("agent script not found: " + agentPath);
                return false;
            }

            targetPid = ResolveRendererPid();

            string agentArg = "-p " + targetPid + " -l \"" + agentPath + "\" -q -t inf";
            string moduleArg = "-m frida_tools.repl " + agentArg;

            var cands = new System.Collections.Generic.List<string[]>();

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] bundled =
                {
                    System.IO.Path.Combine(baseDir, "frida", "frida.exe"),
                    System.IO.Path.Combine(baseDir, "frida.exe")
                };
                foreach (var b in bundled)
                    if (System.IO.File.Exists(b)) cands.Add(new[] { b, agentArg });
            }
            catch { }

            foreach (var py in FindPythonExes())
                cands.Add(new[] { py, moduleArg });

            cands.Add(new[] { "frida",   agentArg });
            cands.Add(new[] { "py",      "-3 " + moduleArg });
            cands.Add(new[] { "python",  moduleArg });
            cands.Add(new[] { "python3", moduleArg });

            var candidates = cands.ToArray();

            string lastErr = "";
            foreach (var cand in candidates)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = cand[0],
                        Arguments = cand[1],
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8
                    };

                    proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                    proc.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data == null) return;
                        string d = e.Data.Trim();
                        if (d.Length > 0 && d[0] == '{') Line?.Invoke(d);
                    };
                    proc.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data)) Error?.Invoke(e.Data);
                    };

                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    try { proc.StandardInput.AutoFlush = true; } catch { }
                    started = true;
                    return true;
                }
                catch (Exception ex)
                {

                    lastErr = ex.Message;
                    try { proc?.Dispose(); } catch { }
                    proc = null;
                }
            }

            Error?.Invoke("frida CLI not found — pip install frida-tools (and add its Scripts to PATH)");
            if (!string.IsNullOrEmpty(lastErr)) {  }
            started = false;
            return false;
        }

        public void Dispose()
        {
            try
            {
                if (proc != null && !proc.HasExited) proc.Kill();
            }
            catch { }
            try { proc?.Dispose(); } catch { }
            proc = null;
        }
    }
}
