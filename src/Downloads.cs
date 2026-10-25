using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Aloha
{
    [DataContract]
    public sealed class DownloadRecord
    {
        [DataMember(Order = 0)] public string Url;
        [DataMember(Order = 1)] public string FileName;
        [DataMember(Order = 2)] public string Path;
        [DataMember(Order = 3)] public long   TotalBytes;
        [DataMember(Order = 4)] public long   ReceivedBytes;
        [DataMember(Order = 5)] public string State;
        [DataMember(Order = 6)] public string StartedUtc;
        [DataMember(Order = 7)] public string FinishedUtc;

        public bool UserPaused;
        public bool UserCancelled;
        public int  Retries;

        private static string NowUtc()
        {
            return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        }

        public static DownloadRecord Start(string url, string name, string path, long total)
        {
            return new DownloadRecord
            {
                Url = url, FileName = name, Path = path,
                TotalBytes = total, ReceivedBytes = 0,
                State = "in progress", StartedUtc = NowUtc(), FinishedUtc = ""
            };
        }

        public static DownloadRecord Blocked(string url, string name)
        {
            return new DownloadRecord
            {
                Url = url, FileName = name, Path = "",
                State = "blocked", StartedUtc = NowUtc(), FinishedUtc = NowUtc()
            };
        }

        public static DownloadRecord Cancelled(string url, string name)
        {
            return new DownloadRecord
            {
                Url = url, FileName = name, Path = "",
                State = "cancelled", StartedUtc = NowUtc(), FinishedUtc = NowUtc()
            };
        }
    }

    [DataContract]
    public sealed class DownloadFile
    {
        [DataMember(Order = 0)] public int Version = 1;
        [DataMember(Order = 1)] public List<DownloadRecord> Downloads = new List<DownloadRecord>();
    }

    public sealed class DownloadHistory
    {
        public List<DownloadRecord> Items { get; private set; }
        public event Action Changed;

        private readonly string path;

        public DownloadHistory(string storePath)
        {
            path = storePath;
            Items = new List<DownloadRecord>();
        }

        public void Add(DownloadRecord r)
        {
            if (r == null) return;
            lock (Items) Items.Add(r);
            Save();
            RaiseChanged();
        }

        public void Clear()
        {
            lock (Items) Items.Clear();
            Save();
            RaiseChanged();
        }

        public void RaiseChanged()
        {
            var h = Changed;
            if (h != null) h();
        }

        private readonly object _saveLock = new object();

        public void Save()
        {

            lock (_saveLock)
            {
                try
                {
                    List<DownloadRecord> snapshot;
                    lock (Items) snapshot = new List<DownloadRecord>(Items);
                    var file = new DownloadFile { Downloads = snapshot };
                    var ser = new DataContractJsonSerializer(typeof(DownloadFile));
                    string tmp = path + ".tmp";
                    using (var fs = File.Create(tmp)) ser.WriteObject(fs, file);
                    if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
                }
                catch { }
            }
        }

        public static DownloadHistory Load(string storePath)
        {
            var h = new DownloadHistory(storePath);
            if (!File.Exists(storePath)) return h;
            try
            {
                var ser = new DataContractJsonSerializer(typeof(DownloadFile));
                using (var fs = File.OpenRead(storePath))
                {
                    var file = ser.ReadObject(fs) as DownloadFile;
                    if (file != null && file.Downloads != null) h.Items = file.Downloads;
                }
            }
            catch { }
            return h;
        }
    }
}
