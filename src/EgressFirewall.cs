using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Aloha.Net
{
    public static class EgressFirewall
    {
        private const string RuleName = "Aloha - renderer egress block";

        private const string Wv2ClientKey =
            @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

        public static string WebView2ExePath()
        {

            try
            {
                foreach (var p in Process.GetProcessesByName("msedgewebview2"))
                {
                    try { var f = p.MainModule != null ? p.MainModule.FileName : null;
                          if (!string.IsNullOrEmpty(f) && File.Exists(f)) return f; }
                    catch {  }
                }
            }
            catch { }

            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                using (var k = baseKey.OpenSubKey(Wv2ClientKey))
                {
                    var ver = k != null ? k.GetValue("pv") as string : null;
                    if (!string.IsNullOrEmpty(ver))
                    {
                        foreach (var pf in new[]
                        {
                            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                        })
                        {
                            if (string.IsNullOrEmpty(pf)) continue;
                            var cand = Path.Combine(pf, @"Microsoft\EdgeWebView\Application", ver, "msedgewebview2.exe");
                            if (File.Exists(cand)) return cand;
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        public static bool IsActive()
        {
            try
            {
                var psi = new ProcessStartInfo("netsh",
                    "advfirewall firewall show rule name=\"" + RuleName + "\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    return o.IndexOf("Block", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }

        public static bool Install() { return Apply(true); }
        public static bool Remove()  { return Apply(false); }

        private static bool Apply(bool install)
        {
            string exe = install ? WebView2ExePath() : null;
            if (install && string.IsNullOrEmpty(exe))
            {
                Aloha.ActivityLog.General("egress firewall: msedgewebview2.exe not found — start the engine, then try again.");
                return false;
            }

            string bat;
            try { bat = Path.Combine(Aloha.AlohaPaths.Tmp, "aloha_egress_fw.bat"); }
            catch { bat = Path.Combine(Path.GetTempPath(), "aloha_egress_fw.bat"); }

            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("netsh advfirewall firewall delete rule name=\"" + RuleName + "\" >nul 2>&1");
            if (install)
                sb.AppendLine("netsh advfirewall firewall add rule name=\"" + RuleName +
                              "\" dir=out action=block program=\"" + exe + "\" enable=yes profile=any");

            try { File.WriteAllText(bat, sb.ToString()); }
            catch (Exception ex) { Aloha.ActivityLog.General("egress firewall: cannot write script — " + ex.Message); return false; }

            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"")
                {
                    UseShellExecute = true,
                    Verb = "runas",              
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit();
                    bool ok = p.ExitCode == 0;
                    Aloha.ActivityLog.General("egress firewall: " + (install ? "installed block rule for " + exe
                                                                             : "removed block rule")
                                              + (ok ? "" : " (netsh exit " + p.ExitCode + ")"));
                    return ok;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {

                Aloha.ActivityLog.General("egress firewall: elevation declined — no change.");
                return false;
            }
            catch (Exception ex)
            {
                Aloha.ActivityLog.General("egress firewall: " + ex.Message);
                return false;
            }
        }
    }
}
