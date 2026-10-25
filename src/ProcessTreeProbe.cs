using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Text;

namespace Aloha
{

    public class ProcessTreeProbe
    {
        public class Proc
        {
            public int Pid;
            public int Ppid;
            public string Type;
            public double Cpu;
        }

        private readonly Dictionary<int, TimeSpan> lastCpu = new Dictionary<int, TimeSpan>();
        private DateTime lastStamp = DateTime.UtcNow;

        public List<Proc> Snapshot(int browserPid)
        {
            var list = new List<Proc>();
            var seen = new HashSet<int>();

            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, ParentProcessId, CommandLine FROM Win32_Process " +
                    "WHERE Name = 'msedgewebview2.exe'"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        int pid  = Convert.ToInt32(mo["ProcessId"] ?? 0);
                        int ppid = Convert.ToInt32(mo["ParentProcessId"] ?? 0);
                        string cmd = (mo["CommandLine"] as string) ?? "";
                        if (pid == 0 || seen.Contains(pid)) continue;
                        seen.Add(pid);

                        list.Add(new Proc
                        {
                            Pid = pid,
                            Ppid = ppid,
                            Type = TypeFromCommandLine(cmd, ppid, browserPid),
                            Cpu = SampleCpu(pid)
                        });
                    }
                }
            }
            catch {  }

            lastStamp = DateTime.UtcNow;
            list.Sort((a, b) => TypeRank(a.Type).CompareTo(TypeRank(b.Type)));
            return list;
        }

        private static string TypeFromCommandLine(string cmd, int ppid, int browserPid)
        {
            int i = cmd.IndexOf("--type=", StringComparison.OrdinalIgnoreCase);
            if (i < 0)
                return (ppid == 0 || ppid == browserPid) ? "browser" : "browser?";
            i += 7;
            int j = i;
            while (j < cmd.Length && cmd[j] != ' ' && cmd[j] != '"') j++;
            string t = cmd.Substring(i, j - i).Trim();

            if (t.Equals("gpu-process", StringComparison.OrdinalIgnoreCase)) return "gpu";
            if (t.StartsWith("utility", StringComparison.OrdinalIgnoreCase)) return "utility";
            return string.IsNullOrEmpty(t) ? "child" : t;
        }

        private static int TypeRank(string t)
        {
            switch (t)
            {
                case "browser": return 0;
                case "gpu": return 1;
                case "network": return 2;
                case "renderer": return 3;
                case "utility": return 4;
                default: return 9;
            }
        }

        private double SampleCpu(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    TimeSpan now = p.TotalProcessorTime;
                    double pct = 0;
                    if (lastCpu.TryGetValue(pid, out TimeSpan prev))
                    {
                        double dtMs = (DateTime.UtcNow - lastStamp).TotalMilliseconds;
                        if (dtMs > 0)
                            pct = Math.Max(0, Math.Min(100, (now - prev).TotalMilliseconds / dtMs * 100.0));
                    }
                    lastCpu[pid] = now;
                    return Math.Round(pct);
                }
            }
            catch { return 0; }
        }

        public static string ToJson(List<Proc> procs)
        {
            var sb = new StringBuilder();
            sb.Append("{\"t\":\"proctree\",\"list\":[");
            for (int i = 0; i < procs.Count; i++)
            {
                var p = procs[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":\"").Append(Esc(p.Type)).Append("\",")
                  .Append("\"pid\":").Append(p.Pid.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append("\"role\":\"").Append(Esc(p.Type)).Append("\",")
                  .Append("\"cpu\":").Append(((int)p.Cpu).ToString(CultureInfo.InvariantCulture))
                  .Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
