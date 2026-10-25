using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Aloha.RingStoreCore;

namespace Aloha
{
    [DataContract]
    internal sealed class FxNode
    {
        [DataMember(Name = "guid", EmitDefaultValue = false, Order = 0)] public string Guid;
        [DataMember(Name = "title", EmitDefaultValue = false, Order = 1)] public string Title;
        [DataMember(Name = "index", EmitDefaultValue = false, Order = 2)] public int? Index;
        [DataMember(Name = "dateAdded", EmitDefaultValue = false, Order = 3)] public long? DateAdded;
        [DataMember(Name = "lastModified", EmitDefaultValue = false, Order = 4)] public long? LastModified;
        [DataMember(Name = "id", EmitDefaultValue = false, Order = 5)] public int? Id;
        [DataMember(Name = "typeCode", EmitDefaultValue = false, Order = 6)] public int? TypeCode;
        [DataMember(Name = "type", EmitDefaultValue = false, Order = 7)] public string Type;
        [DataMember(Name = "root", EmitDefaultValue = false, Order = 8)] public string Root;
        [DataMember(Name = "uri", EmitDefaultValue = false, Order = 9)] public string Uri;
        [DataMember(Name = "tags", EmitDefaultValue = false, Order = 10)] public string Tags;
        [DataMember(Name = "keyword", EmitDefaultValue = false, Order = 11)] public string Keyword;
        [DataMember(Name = "iconUri", EmitDefaultValue = false, Order = 12)] public string IconUri;
        [DataMember(Name = "children", EmitDefaultValue = false, Order = 13)] public List<FxNode> Children;
    }

    internal static class FirefoxBookmarks
    {
        public const string Place = "text/x-moz-place", Container = "text/x-moz-place-container", Separator = "text/x-moz-place-separator";

        private static readonly string[][] Roots =
        {
            new[] { "menu________",    "menu",    "bookmarksMenuFolder" },
            new[] { "toolbar_____",    "toolbar", "toolbarFolder" },
            new[] { "unfiled_____",    "unfiled", "unfiledBookmarksFolder" },
            new[] { "mobile______",    "mobile",  "mobileFolder" },
        };

        public sealed class ImportResult
        {
            public List<Bookmark> Items = new List<Bookmark>();
            public int Folders, Skipped;
        }

        public static ImportResult Read(string path)
        {
            byte[] raw = File.ReadAllBytes(path);
            byte[] json = IsMozLz4(raw) ? MozLz4(raw) : raw;
            int start = (json.Length >= 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF) ? 3 : 0;
            FxNode root;
            using (var ms = new MemoryStream(json, start, json.Length - start))
                root = (FxNode)new DataContractJsonSerializer(typeof(FxNode)).ReadObject(ms);
            if (root == null) throw new InvalidDataException("not a Firefox bookmarks file");

            var res = new ImportResult();
            Walk(root, "", res, true);
            return res;
        }

        private static void Walk(FxNode n, string path, ImportResult res, bool isPlacesRoot)
        {
            if (n == null) return;
            if (n.Type == Container || n.Children != null)
            {

                string here = isPlacesRoot ? "" : (path.Length == 0 ? RootKey(n) : path + "/" + Clean(n.Title));
                if (!isPlacesRoot && path.Length > 0) res.Folders++;
                if (n.Children != null) foreach (var c in n.Children) Walk(c, here, res, false);
                return;
            }
            if (n.Type == Separator) return;
            string uri = n.Uri ?? "";
            if (uri.Length == 0 || uri.StartsWith("place:", StringComparison.OrdinalIgnoreCase)) { res.Skipped++; return; }
            string host = BookmarkManager.HostOf(uri);
            res.Items.Add(new Bookmark
            {
                Url = uri,
                Title = string.IsNullOrEmpty(n.Title) ? host : n.Title,
                AddedUtc = FromMicros(n.DateAdded).ToString("o", CultureInfo.InvariantCulture),
                Host = host,
                Domain = BookmarkManager.RegistrableDomain(host),
                Folder = path.Length > 0 ? path : null,
                Tags = string.IsNullOrEmpty(n.Tags) ? null : n.Tags,
                Keyword = string.IsNullOrEmpty(n.Keyword) ? null : n.Keyword
            });
        }

        private static string RootKey(FxNode n)
        {
            foreach (var r in Roots) if (n.Guid == r[0] || n.Root == r[2]) return r[1];
            return Clean(n.Title) ?? "menu";
        }

        private static string Clean(string t) { return string.IsNullOrEmpty(t) ? "(untitled)" : t.Replace("/", "∕"); }

        public static int Merge(BookmarkManager mgr, List<Bookmark> incoming)
        {
            var have = new HashSet<string>(mgr.Items.Select(b => b.Url), StringComparer.Ordinal);
            int added = 0;
            foreach (var b in incoming)
            {
                if (have.Contains(b.Url))
                {
                    var mine = mgr.Items.First(x => x.Url == b.Url);
                    if (string.IsNullOrEmpty(mine.Folder)) mine.Folder = b.Folder;
                    if (string.IsNullOrEmpty(mine.Tags)) mine.Tags = b.Tags;
                    if (string.IsNullOrEmpty(mine.Keyword)) mine.Keyword = b.Keyword;
                    continue;
                }
                mgr.Items.Add(b); have.Add(b.Url); added++;
            }
            return added;
        }

        public static int Write(string path, IEnumerable<Bookmark> items)
        {
            long now = ToMicros(DateTime.UtcNow);
            int nextId = 1;
            FxNode MakeFolder(string guid, string title, string root)
                => new FxNode { Guid = guid, Title = title, DateAdded = now, LastModified = now, Id = nextId++, TypeCode = 2, Type = Container, Root = root, Children = new List<FxNode>() };

            var placesRoot = MakeFolder("root________", "", "placesRoot");
            var roots = new Dictionary<string, FxNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in Roots) { var f = MakeFolder(r[0], r[1], r[2]); roots[r[1]] = f; placesRoot.Children.Add(f); }

            var folders = new Dictionary<string, FxNode>(StringComparer.Ordinal);
            FxNode FolderFor(string p)
            {
                FxNode f;
                if (folders.TryGetValue(p, out f)) return f;
                int slash = p.LastIndexOf('/');
                FxNode parent = slash < 0 ? null : FolderFor(p.Substring(0, slash));
                if (parent == null)
                {
                    if (!roots.TryGetValue(p, out f)) { f = MakeFolder(NewGuid(), p, null); roots["menu"].Children.Add(f); }
                }
                else
                {
                    f = MakeFolder(NewGuid(), p.Substring(slash + 1).Replace("∕", "/"), null);
                    parent.Children.Add(f);
                }
                folders[p] = f;
                return f;
            }

            int n = 0;
            foreach (var b in items.OrderBy(x => x.Folder ?? "￿").ThenBy(x => x.Domain).ThenBy(x => x.AddedUtc))
            {
                if (string.IsNullOrEmpty(b.Url)) continue;
                string p = !string.IsNullOrEmpty(b.Folder) ? b.Folder
                         : "menu/Aloha/" + (string.IsNullOrEmpty(b.Domain) ? "(no domain)" : b.Domain);
                long added = ToMicros(ParseIso(b.AddedUtc));
                FolderFor(p).Children.Add(new FxNode
                {
                    Guid = NewGuid(), Title = b.Title ?? b.Url, DateAdded = added, LastModified = added,
                    Id = nextId++, TypeCode = 1, Type = Place, Uri = b.Url,
                    Tags = string.IsNullOrEmpty(b.Tags) ? null : b.Tags,
                    Keyword = string.IsNullOrEmpty(b.Keyword) ? null : b.Keyword
                });
                n++;
            }
            Index(placesRoot);

            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(FxNode)).WriteObject(ms, placesRoot);

                string json = Encoding.UTF8.GetString(ms.ToArray()).Replace("\\/", "/");
                File.WriteAllText(path, json, new UTF8Encoding(false));
            }
            return n;
        }

        private static void Index(FxNode f)
        {
            if (f.Children == null) return;
            for (int i = 0; i < f.Children.Count; i++) { f.Children[i].Index = i; Index(f.Children[i]); }
        }

        private static readonly Random rng = new Random();
        private static string NewGuid()
        {
            const string a = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
            var sb = new StringBuilder(12);
            lock (rng) for (int i = 0; i < 12; i++) sb.Append(a[rng.Next(64)]);
            return sb.ToString();
        }

        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static long ToMicros(DateTime utc) { return (utc.ToUniversalTime() - Epoch).Ticks / 10; }
        private static DateTime FromMicros(long? us) { return us.HasValue && us.Value > 0 ? Epoch.AddTicks(us.Value * 10) : DateTime.UtcNow; }
        private static DateTime ParseIso(string s)
        {
            DateTime t;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t) ? t : DateTime.UtcNow;
        }

        private static bool IsMozLz4(byte[] b)
        {
            return b.Length > 12 && b[0] == 'm' && b[1] == 'o' && b[2] == 'z' && b[3] == 'L' && b[4] == 'z' && b[5] == '4' && b[6] == '0' && b[7] == 0;
        }

        private static byte[] MozLz4(byte[] src)
        {
            int outLen = BitConverter.ToInt32(src, 8);
            if (outLen <= 0 || outLen > 512 * 1024 * 1024) throw new InvalidDataException("bad mozLz4 size");
            var dst = new byte[outLen];
            int s = 12, d = 0;
            while (s < src.Length)
            {
                int token = src[s++];
                int lit = token >> 4;
                if (lit == 15) { int b; do { b = src[s++]; lit += b; } while (b == 255); }
                if (d + lit > dst.Length || s + lit > src.Length) throw new InvalidDataException("bad LZ4 literal run");
                Buffer.BlockCopy(src, s, dst, d, lit); s += lit; d += lit;
                if (s >= src.Length) break;
                int offset = src[s] | (src[s + 1] << 8); s += 2;
                int ml = token & 15;
                if (ml == 15) { int b; do { b = src[s++]; ml += b; } while (b == 255); }
                ml += 4;
                int m = d - offset;
                if (offset == 0 || m < 0 || d + ml > dst.Length) throw new InvalidDataException("bad LZ4 match");
                for (int i = 0; i < ml; i++) dst[d++] = dst[m + i];
            }
            if (d != outLen) Array.Resize(ref dst, d);
            return dst;
        }

        public static string BackupFolder()
        {
            try
            {
                string profiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mozilla", "Firefox", "Profiles");
                if (!Directory.Exists(profiles)) return null;
                return Directory.GetDirectories(profiles)
                    .Select(p => Path.Combine(p, "bookmarkbackups"))
                    .Where(Directory.Exists)
                    .OrderByDescending(Directory.GetLastWriteTimeUtc)
                    .FirstOrDefault();
            }
            catch { return null; }
        }
    }
}
