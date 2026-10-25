using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Aloha
{

    public static class FaviconCache
    {

        private static bool bodyReadUnavailable = false;

        private static readonly Dictionary<string, Image> mem =
            new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        private static string KeyFor(string url)
        {
            using (var sha = SHA1.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(url ?? ""));
                var sb = new StringBuilder(h.Length * 2);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static string PathFor(string url)
        {
            return Path.Combine(AlohaPaths.Favicons, KeyFor(url) + ".ico");
        }

        public static bool LooksLikeFavicon(string url, string contentType)
        {
            if (!string.IsNullOrEmpty(url))
            {
                string u = url.ToLowerInvariant();
                if (u.Contains("favicon")) return true;
                if (u.Contains("apple-touch-icon")) return true;
            }
            if (!string.IsNullOrEmpty(contentType))
            {
                string c = contentType.ToLowerInvariant();
                if (c.Contains("image/x-icon") || c.Contains("image/vnd.microsoft.icon")) return true;
                if (c.Contains("image/png") && !string.IsNullOrEmpty(url) &&
                    url.ToLowerInvariant().Contains("icon")) return true;
            }
            return false;
        }

        public static async Task<string> TrySaveFromResponse(
            CoreWebView2WebResourceResponseReceivedEventArgs a, Action<string> trace)
        {
            if (bodyReadUnavailable) return null;

            string url = null;
            try { url = a.Request != null ? a.Request.Uri : null; } catch { }
            if (string.IsNullOrEmpty(url)) return null;

            try { if (a.Response == null || a.Response.StatusCode != 200) return null; }
            catch { return null; }

            string dest = PathFor(url);
            try
            {
                using (Stream body = await a.Response.GetContentAsync())
                {
                    if (body == null) { trace?.Invoke("save: null body " + url); return null; }

                    using (var ms = new MemoryStream())
                    {
                        body.CopyTo(ms);
                        byte[] bytes = ms.ToArray();
                        if (bytes.Length == 0) { trace?.Invoke("save: empty body " + url); return null; }

                        if (bytes.Length > 512 * 1024) { trace?.Invoke("save: too big (" + bytes.Length + ") " + url); return null; }

                        File.WriteAllBytes(dest, bytes);
                        trace?.Invoke("save: " + bytes.Length + "B -> " + Path.GetFileName(dest) + "  " + url);
                    }
                }
                lock (mem) { mem.Remove(url); }
                return dest;
            }
            catch (NotImplementedException)
            {

                bodyReadUnavailable = true;
                trace?.Invoke("save: GetContentAsync NOT IMPLEMENTED -- disabling wire capture");
                return null;
            }
            catch (Exception ex)
            {
                trace?.Invoke("save: EXCEPTION " + ex.GetType().Name + ": " + ex.Message + "  " + url);
                return null;
            }
        }

        public static bool WireCaptureAvailable { get { return !bodyReadUnavailable; } }

        public static string SaveBytes(string url, byte[] bytes, Action<string> trace)
        {
            if (string.IsNullOrEmpty(url) || bytes == null || bytes.Length == 0) return null;
            try
            {
                string dest = PathFor(url);
                File.WriteAllBytes(dest, bytes);
                lock (mem) { mem.Remove(url); }
                trace?.Invoke("favico: saved " + bytes.Length + "B -> " + Path.GetFileName(dest) + "  " + url);
                return dest;
            }
            catch (Exception ex)
            {
                trace?.Invoke("favico: save EXCEPTION " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private const double MaxAgeHours = 72.0;

        public static Image Load(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            lock (mem)
                if (mem.TryGetValue(url, out var hit)) return hit;

            string p = PathFor(url);
            if (!File.Exists(p)) return null;
            try
            {
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(p);
                if (age.TotalHours > MaxAgeHours) { try { File.Delete(p); } catch { } return null; }

                Image img;
                using (var raw = Image.FromFile(p))
                    img = new Bitmap(raw);
                lock (mem) mem[url] = img;
                return img;
            }
            catch { return null; }
        }

        public static bool Has(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            lock (mem)
                if (mem.ContainsKey(url)) return true;
            return File.Exists(PathFor(url));
        }
    }
}
