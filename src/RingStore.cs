using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Aloha.RingStoreCore
{
    public sealed class RingStore
    {
        public const int  FRAG_SIZE    = 1 << 20;
        public const int  PAYLOAD_BITS = 32;
        public const uint PAYLOAD_MASK = 0xFFFFFFFFu;
        private const int STAMP_BYTES  = 4;
        private const int NUMBER_BYTES = 8;

        public uint Version { get; private set; }
        private readonly List<ulong> _numbers = new List<ulong>();

        public RingStore(uint version) { Version = version; }

        public IList<ulong> Numbers { get { return _numbers.AsReadOnly(); } }
        public int Count { get { return _numbers.Count; } }

        public ulong Write(long position, uint payload)
        {
            if (position < 0) throw new ArgumentOutOfRangeException("position");
            ulong n = ((ulong)position << PAYLOAD_BITS) | payload;
            _numbers.Add(n);
            return n;
        }

        public int SerializedSize { get { return STAMP_BYTES + NUMBER_BYTES * _numbers.Count; } }

        public byte[] GenerateFragment(uint payload)
        {
            return GenerateFragment(payload, Version, FRAG_SIZE);
        }

        public static byte[] GenerateFragment(uint payload, uint version, int size)
        {

            byte[] seed = Encoding.UTF8.GetBytes(
                version.ToString(CultureInfo.InvariantCulture) + ":" +
                payload.ToString(CultureInfo.InvariantCulture));

            byte[] outBuf = new byte[size];
            byte[] input  = new byte[seed.Length + 8];
            Buffer.BlockCopy(seed, 0, input, 0, seed.Length);

            int filled = 0;
            ulong counter = 0;
            using (SHA256 sha = SHA256.Create())
            {
                while (filled < size)
                {
                    WriteUInt64BE(input, seed.Length, counter);
                    byte[] block = sha.ComputeHash(input);
                    int take = Math.Min(block.Length, size - filled);
                    Buffer.BlockCopy(block, 0, outBuf, filled, take);
                    filled += take;
                    counter++;
                }
            }
            return outBuf;
        }

        public byte[] ReadPosition(long position)
        {
            for (int i = 0; i < _numbers.Count; i++)
            {
                ulong n = _numbers[i];
                if ((long)(n >> PAYLOAD_BITS) == position)
                    return GenerateFragment((uint)(n & PAYLOAD_MASK));
            }
            return null;
        }

        public Dictionary<long, byte[]> Hand()
        {
            var vol = new Dictionary<long, byte[]>();
            for (int i = 0; i < _numbers.Count; i++)
            {
                ulong n = _numbers[i];
                vol[(long)(n >> PAYLOAD_BITS)] = GenerateFragment((uint)(n & PAYLOAD_MASK));
            }
            return vol;
        }

        public void Save(string path)
        {
            using (FileStream fs = File.Create(path))
            {
                byte[] stamp = new byte[4];
                WriteUInt32BE(stamp, 0, Version);
                fs.Write(stamp, 0, 4);
                byte[] nb = new byte[8];
                for (int i = 0; i < _numbers.Count; i++)
                {
                    WriteUInt64BE(nb, 0, _numbers[i]);
                    fs.Write(nb, 0, 8);
                }
            }
        }

        public static RingStore Load(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < STAMP_BYTES) throw new InvalidDataException("store too short");
            var s = new RingStore(ReadUInt32BE(bytes, 0));
            for (int off = STAMP_BYTES; off + 8 <= bytes.Length; off += 8)
                s._numbers.Add(ReadUInt64BE(bytes, off));
            return s;
        }

        internal static void WriteUInt32BE(byte[] b, int o, uint v)
        {
            b[o]   = (byte)(v >> 24); b[o+1] = (byte)(v >> 16);
            b[o+2] = (byte)(v >> 8);  b[o+3] = (byte)v;
        }
        internal static void WriteUInt64BE(byte[] b, int o, ulong v)
        {
            for (int i = 7; i >= 0; i--) { b[o + i] = (byte)(v & 0xFF); v >>= 8; }
        }
        internal static uint ReadUInt32BE(byte[] b, int o)
        {
            return ((uint)b[o] << 24) | ((uint)b[o+1] << 16) | ((uint)b[o+2] << 8) | b[o+3];
        }
        internal static ulong ReadUInt64BE(byte[] b, int o)
        {
            ulong v = 0; for (int i = 0; i < 8; i++) v = (v << 8) | b[o + i]; return v;
        }
    }

    public struct BookmarkRef
    {
        public long Start; public int Count;
        public BookmarkRef(long start, int count) { Start = start; Count = count; }
    }

    public sealed class BookmarkStore
    {
        private readonly RingStore _ring;
        public BookmarkStore(RingStore ring) { _ring = ring; }
        public RingStore Ring { get { return _ring; } }

        public BookmarkRef AddAuthored(long startPosition, uint[] payloads)
        {
            for (int i = 0; i < payloads.Length; i++)
                _ring.Write(startPosition + i, payloads[i]);
            return new BookmarkRef(startPosition, payloads.Length);
        }

        public byte[] Read(BookmarkRef bm)
        {
            long total = (long)bm.Count * RingStore.FRAG_SIZE;
            if (total > int.MaxValue)
                throw new InvalidOperationException("bookmark exceeds 2 GB; stream per-position instead");
            byte[] outBuf = new byte[(int)total];
            for (int i = 0; i < bm.Count; i++)
            {
                byte[] frag = _ring.ReadPosition(bm.Start + i);
                if (frag != null)
                    Buffer.BlockCopy(frag, 0, outBuf, i * RingStore.FRAG_SIZE, frag.Length);

            }
            return outBuf;
        }
    }
}
