using System;
using System.Collections.Generic;
using System.IO;

namespace Aloha
{

    public static class ProfileStore
    {
        public static string Dir
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles"); }
        }

        private static void EnsureDir()
        {
            try { if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir); }
            catch { }
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "profile";
            var chars = name.Trim().ToCharArray();
            var bad = Path.GetInvalidFileNameChars();
            for (int i = 0; i < chars.Length; i++)
                if (Array.IndexOf(bad, chars[i]) >= 0) chars[i] = '_';
            var clean = new string(chars).Trim();
            return clean.Length == 0 ? "profile" : clean;
        }

        public static string PathFor(string name)
        {
            return Path.Combine(Dir, Sanitize(name) + ".json");
        }

        public static string[] List()
        {
            EnsureDir();
            var names = new List<string>();
            try
            {
                foreach (var f in Directory.GetFiles(Dir, "*.json"))
                    names.Add(Path.GetFileNameWithoutExtension(f));
            }
            catch { }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names.ToArray();
        }

        public static bool Exists(string name)
        {
            return File.Exists(PathFor(name));
        }

        public static void Save(string name, NetConfig cfg)
        {
            if (cfg == null) return;
            EnsureDir();
            cfg.Save(PathFor(name));
        }

        public static NetConfig Load(string name)
        {
            return NetConfig.Load(PathFor(name));
        }

        public static void Delete(string name)
        {
            try { var p = PathFor(name); if (File.Exists(p)) File.Delete(p); }
            catch { }
        }
    }
}
