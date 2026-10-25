using System;
using System.Windows.Forms;

namespace Aloha
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                Report("Unhandled", e.ExceptionObject as Exception);
            Application.ThreadException += (s, e) =>
                Report("UI thread", e.Exception);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (!Preflight()) return;

            try
            {
                Application.Run(new Form1());
            }
            catch (Exception ex)
            {
                Report("Run", ex);
            }
        }

        private static bool Preflight()
        {
            var missing = new System.Collections.Generic.List<string>();
            string dir = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
            foreach (var an in System.Reflection.Assembly.GetExecutingAssembly().GetReferencedAssemblies())
            {
                try { System.Reflection.Assembly.Load(an); }
                catch { missing.Add(an.Name + ".dll  (" + an.Version + ")"); }
            }
            bool loader = false;
            foreach (var rel in new[] { "WebView2Loader.dll", @"runtimes\win-x64\native\WebView2Loader.dll",
                                        @"runtimes\win-x86\native\WebView2Loader.dll", @"runtimes\win-arm64\native\WebView2Loader.dll" })
                if (System.IO.File.Exists(System.IO.Path.Combine(dir, rel))) { loader = true; break; }
            if (!loader) missing.Add(@"WebView2Loader.dll  (next to the exe, or runtimes\win-x64\native\)");

            bool runtime = WebView2RuntimeInstalled();
            if (missing.Count == 0 && runtime) return true;

            var sb = new System.Text.StringBuilder();
            if (missing.Count > 0)
            {
                sb.Append("Aloha can't start: these files are missing next to Aloha.exe\r\n(" + dir + "):\r\n\r\n");
                foreach (var m in missing) sb.Append("   \u2022 ").Append(m).Append("\r\n");
                sb.Append("\r\nCopy the WHOLE build folder (bin\\x64\\Release\\net48 \u2014 every .dll, the runtimes\\ folder, " +
                          "tor\\, torrc7\\ and the .js/.html/.py files), not Aloha.exe on its own.\r\n");
            }
            if (!runtime)
                sb.Append("\r\nThe Microsoft Edge WebView2 Runtime is not installed on this PC.\r\n" +
                          "Install the \"Evergreen\" runtime from Microsoft (developer.microsoft.com/microsoft-edge/webview2), then start Aloha again.\r\n");
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "aloha_crash.txt"), sb.ToString()); } catch { }
            MessageBox.Show(sb.ToString(), "Aloha \u2014 missing files", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return missing.Count == 0;
        }

        private static bool WebView2RuntimeInstalled()
        {
            const string guid = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
            string[] keys =
            {
                @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\" + guid,
                @"SOFTWARE\Microsoft\EdgeUpdate\Clients\" + guid
            };
            try
            {
                foreach (var hive in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
                    foreach (var k in keys)
                        using (var rk = hive.OpenSubKey(k))
                        {
                            var v = rk?.GetValue("pv") as string;
                            if (!string.IsNullOrEmpty(v) && v != "0.0.0.0") return true;
                        }
            }
            catch { return true; }
            return false;
        }

        private static bool reported;
        private static void Report(string where, Exception ex)
        {
            string text = where + " error:\r\n\r\n" + (ex != null ? ex.ToString() : "(no detail)");
            try
            {
                string path = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(Application.ExecutablePath),
                    "aloha_crash.txt");
                System.IO.File.WriteAllText(path, text);
            }
            catch { }
            if (!reported)
            {
                reported = true;
                MessageBox.Show(text, "Aloha — startup error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
