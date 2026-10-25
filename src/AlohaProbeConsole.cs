using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace Aloha
{

    public static class AlohaProbeConsole
    {
        public const string ScriptName = "aloha_probe.py";
        public const string BatchName  = "aloha_devtool.bat";

        public static void Launch(NetConfig cfg, IWin32Window owner = null)
        {
            if (cfg != null && !cfg.CfgEmbeddedDevTools)
            {
                MessageBox.Show(owner,
                    "The probe attaches over the DevTools protocol, and that port is off.\r\n\r\n" +
                    "Turn on \"Embedded DevTools\" in Browser Configuration, reload a page, " +
                    "then open the probe again.\r\n\r\n" +
                    "It is loopback-only and origin-locked — nothing external can reach it.",
                    "Aloha probe — CDP port is closed",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string script = FindScript();
            if (script == null)
            {
                MessageBox.Show(owner,
                    ScriptName + " was not found next to Aloha.exe.\r\n\r\n" +
                    "It ships as a loose file (like instr_agent.js and the tor bundle) so you " +
                    "can edit it. Drop it beside the executable and try again.",
                    "Aloha probe — script missing",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int port = NetConfig.EmbeddedDevToolsPort;

            string batch = FindBeside(BatchName);
            string args;
            if (batch != null)
            {
                args = "/C \"\"" + batch + "\" " + port + "\"";
            }
            else
            {
                string python = PickPython(cfg);
                args = "/K title Aloha DevTool" +
                       " & color 04" +
                       " & \"" + python + "\" \"" + script + "\" --port " + port +
                       " --theme devtool";
            }

            try
            {
                var psi = new ProcessStartInfo("cmd.exe", args)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(script)
                };
                var p = Process.Start(psi);
                try { ProcessLeash.Leash(p); } catch { }
                ActivityLog.General("DevTool console launched on 127.0.0.1:" + port +
                                    (batch != null ? " via " + BatchName : " via python"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner,
                    "Could not start the probe console.\r\n\r\n" + ex.Message,
                    "Aloha probe", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string FindScript() { return FindBeside(ScriptName); }

        private static string FindBeside(string name)
        {
            foreach (var dir in new[]
            {
                Path.GetDirectoryName(Application.ExecutablePath),
                Environment.CurrentDirectory,
                AlohaPaths.Root
            })
            {
                if (string.IsNullOrEmpty(dir)) continue;
                string p = Path.Combine(dir, name);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        private static string PickPython(NetConfig cfg)
        {
            try
            {
                if (cfg != null && !string.IsNullOrWhiteSpace(cfg.PythonExe))
                    return cfg.PythonExe.Trim();
            }
            catch { }
            return "python";
        }
    }
}
