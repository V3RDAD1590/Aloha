using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Aloha.RingStoreCore
{
    [DataContract]
    public sealed class Bookmark
    {
        [DataMember(Order = 0)] public string Url;
        [DataMember(Order = 1)] public string Title;
        [DataMember(Order = 2)] public string AddedUtc;
        [DataMember(Order = 3)] public string Host;
        [DataMember(Order = 4)] public string Domain;

        [DataMember(Order = 5, EmitDefaultValue = false)] public string Folder;
        [DataMember(Order = 6, EmitDefaultValue = false)] public string Tags;
        [DataMember(Order = 7, EmitDefaultValue = false)] public string Keyword;
    }

    [DataContract]
    public sealed class BookmarkFile
    {
        [DataMember(Order = 0)] public int Version = 1;
        [DataMember(Order = 1)] public List<Bookmark> Bookmarks = new List<Bookmark>();
    }

    public sealed class BookmarkManager
    {
        public List<Bookmark> Items { get; private set; }
        public BookmarkManager() { Items = new List<Bookmark>(); }

        public Bookmark AddCurrentSite(string url, string title)
        {
            if (string.IsNullOrEmpty(url)) return null;
            string host = HostOf(url);
            Bookmark existing = Items.FirstOrDefault(b => b.Url == url);
            if (existing != null) { existing.Title = title ?? existing.Title; return existing; }
            var bm = new Bookmark
            {
                Url      = url,
                Title    = string.IsNullOrEmpty(title) ? host : title,
                AddedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Host     = host,
                Domain   = RegistrableDomain(host),
            };
            Items.Add(bm);
            return bm;
        }

        public bool Remove(string url)
        {
            int i = Items.FindIndex(b => b.Url == url);
            if (i < 0) return false;
            Items.RemoveAt(i);
            return true;
        }

        public static string ClusterKey(Bookmark b) { return b.Domain; }

        public void Save(string path)
        {
            var file = new BookmarkFile { Bookmarks = Items };
            var ser = new DataContractJsonSerializer(typeof(BookmarkFile));
            using (var fs = File.Create(path)) ser.WriteObject(fs, file);
        }

        public static BookmarkManager Load(string path)
        {
            var mgr = new BookmarkManager();
            if (!File.Exists(path)) return mgr;
            var ser = new DataContractJsonSerializer(typeof(BookmarkFile));
            using (var fs = File.OpenRead(path))
            {
                var file = ser.ReadObject(fs) as BookmarkFile;
                if (file != null && file.Bookmarks != null) mgr.Items = file.Bookmarks;
            }
            return mgr;
        }

        private static readonly HashSet<string> MultiPartSuffix =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "co.uk","org.uk","ac.uk","gov.uk","co.jp","co.nz","com.au","net.au",
            "com.br","co.in","co.za","com.cn","co.kr",
        };

        public static string HostOf(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            try
            {
                if (url.IndexOf("://", StringComparison.Ordinal) < 0) url = "http://" + url;
                return new Uri(url).Host.ToLowerInvariant();
            }
            catch { return url.ToLowerInvariant(); }
        }

        public static string RegistrableDomain(string host)
        {
            if (string.IsNullOrEmpty(host)) return "";
            host = host.ToLowerInvariant().Trim('.');
            string[] labels = host.Split('.');
            if (labels.Length <= 2) return host;
            string last2 = labels[labels.Length - 2] + "." + labels[labels.Length - 1];
            if (MultiPartSuffix.Contains(last2) && labels.Length >= 3)
                return labels[labels.Length - 3] + "." + last2;
            return last2;
        }
    }
}
