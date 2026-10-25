using System;
using System.IO;

namespace Aloha
{

    public static class AlohaPaths
    {
        private static string _root;
        private static bool   _portable;

        public static string BaseDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static bool IsPortable
        {
            get { var _ = Root; return _portable; }
        }

        public static string Root
        {
            get
            {
                if (_root != null) return _root;

                string local = Path.Combine(BaseDir, "Data");
                if (CanWrite(local))
                {
                    _root = local; _portable = true;
                }
                else
                {
                    _root = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Aloha");
                    _portable = false;
                    try
                    {
                        Directory.CreateDirectory(_root);
                        ActivityLog.General("AlohaPaths: install folder is not writable — "
                                          + "falling back to " + _root);
                    }
                    catch { }
                }
                return _root;
            }
        }

        public static string TorData { get { return Ensure(Path.Combine(Root, "tor-data")); } }
        public static string Profile { get { return Ensure(Path.Combine(Root, "profile")); } }
        public static string Tmp     { get { return Ensure(Path.Combine(Root, "tmp")); } }

        public static string Favicons { get { return Ensure(Path.Combine(Root, "favicons")); } }

        public static string Torrc   { get { return Path.Combine(TorData, "torrc"); } }

        public static void MigrateLegacyProfile()
        {
            try
            {
                string legacy = Path.Combine(BaseDir, "AlohaProfile");
                if (!Directory.Exists(legacy)) return;

                string target = Path.Combine(Root, "profile");
                if (Directory.Exists(target) &&
                    Directory.GetFileSystemEntries(target).Length > 0)
                    return;

                if (Directory.Exists(target)) Directory.Delete(target);
                Directory.Move(legacy, target);
                ActivityLog.General("AlohaPaths: moved profile " + legacy + " -> " + target);
            }
            catch (Exception ex)
            {

                ActivityLog.General("AlohaPaths: profile migration skipped — " + ex.Message);
                try { Directory.CreateDirectory(Path.Combine(Root, "profile")); } catch { }
            }
        }

        private static bool CanWrite(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".write-probe");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        private static string Ensure(string dir)
        {
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }
    }
}
