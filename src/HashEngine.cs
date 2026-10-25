using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
#if ALOHA_IOHASHING
using System.IO.Hashing;
#endif
#if ALOHA_BOUNCYCASTLE
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
#endif

namespace Aloha.Media
{
    public interface IHasher { void Update(byte[] buf, int len); string Final(); }

    public static class HashEngine
    {
        public static bool IsSupported(string algo) { return Create(algo) != null; }

        public static async Task<string> ComputeAsync(Stream s, string algo, long total,
            IProgress<int> progress = null, CancellationToken ct = default(CancellationToken))
        {
            var h = Create(algo);
            if (h == null) throw new NotSupportedException(algo + " — not available in this build");
            byte[] buf = new byte[1 << 20];
            long done = 0; int n, lastPct = -1;
            while ((n = await s.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
            {
                h.Update(buf, n); done += n;
                if (progress != null && total > 0)
                {
                    int pct = (int)(done * 100 / total);
                    if (pct != lastPct) { lastPct = pct; progress.Report(pct); }
                }
            }
            return h.Final();
        }

        private static IHasher Create(string algo)
        {
            switch (algo)
            {

                case "MD5": return new Bcl(MD5.Create());
                case "SHA-1": return new Bcl(SHA1.Create());
                case "SHA-256": return new Bcl(SHA256.Create());
                case "SHA-384": return new Bcl(SHA384.Create());
                case "SHA-512": return new Bcl(SHA512.Create());

                case "CRC-32C": return new Crc32c();
                case "Adler-32": return new Adler32();
                case "FNV-1a": return new Fnv1a64();
                case "MurmurHash3": return new Murmur3x64_128();
#if ALOHA_IOHASHING
                case "CRC-32": return new Io(new Crc32());
                case "CRC-64/XZ": return new Io(new Crc64());
                case "xxHash64": return new Io(new XxHash64());
                case "xxHash3-128": return new Io(new XxHash128());
#endif
#if ALOHA_BOUNCYCASTLE
                case "SHA-224": return new Bc(new Sha224Digest());
                case "SHA-512/256": return new Bc(new Sha512tDigest(256));
                case "MD4": return new Bc(new MD4Digest());
                case "SHA3-256": return new Bc(new Sha3Digest(256));
                case "SHA3-512": return new Bc(new Sha3Digest(512));
                case "Keccak-256": return new Bc(new KeccakDigest(256));
                case "SHAKE128": return new BcXof(new ShakeDigest(128), 32);
                case "SHAKE256": return new BcXof(new ShakeDigest(256), 64);
                case "BLAKE2b-256": return new Bc(new Blake2bDigest(256));
                case "BLAKE2s-128": return new Bc(new Blake2sDigest(128));
                case "RIPEMD-160": return new Bc(new RipeMD160Digest());
                case "Whirlpool": return new Bc(new WhirlpoolDigest());
                case "Tiger": return new Bc(new TigerDigest());
                case "Skein-512": return new Bc(new SkeinDigest(512, 512));
                case "Streebog-256": return new Bc(new Gost3411_2012_256Digest());
                case "SM3": return new Bc(new SM3Digest());
#endif

                default: return null;
            }
        }

        internal static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (var x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        private sealed class Bcl : IHasher
        {
            private readonly HashAlgorithm _h; public Bcl(HashAlgorithm h) { _h = h; }
            public void Update(byte[] buf, int len) { _h.TransformBlock(buf, 0, len, null, 0); }
            public string Final() { _h.TransformFinalBlock(new byte[0], 0, 0); return Hex(_h.Hash); }
        }
#if ALOHA_IOHASHING
        private sealed class Io : IHasher
        {
            private readonly NonCryptographicHashAlgorithm _h; public Io(NonCryptographicHashAlgorithm h) { _h = h; }
            public void Update(byte[] buf, int len) { _h.Append(new ReadOnlySpan<byte>(buf, 0, len)); }
            public string Final() { return Hex(_h.GetCurrentHash()); }
        }
#endif
#if ALOHA_BOUNCYCASTLE
        private sealed class Bc : IHasher
        {
            private readonly IDigest _d; public Bc(IDigest d) { _d = d; }
            public void Update(byte[] buf, int len) { _d.BlockUpdate(buf, 0, len); }
            public string Final() { var o = new byte[_d.GetDigestSize()]; _d.DoFinal(o, 0); return Hex(o); }
        }
        private sealed class BcXof : IHasher
        {
            private readonly ShakeDigest _d; private readonly int _len; public BcXof(ShakeDigest d, int len) { _d = d; _len = len; }
            public void Update(byte[] buf, int len) { _d.BlockUpdate(buf, 0, len); }
            public string Final() { var o = new byte[_len]; _d.DoFinal(o, 0, _len); return Hex(o); }
        }
#endif

        private sealed class Adler32 : IHasher
        {
            private uint _a = 1, _b = 0; const uint MOD = 65521;
            public void Update(byte[] buf, int len)
            { for (int i = 0; i < len; i++) { _a = (_a + buf[i]) % MOD; _b = (_b + _a) % MOD; } }
            public string Final() { uint v = (_b << 16) | _a; return v.ToString("x8"); }
        }

        private sealed class Fnv1a64 : IHasher
        {
            private ulong _h = 0xCBF29CE484222325UL; const ulong P = 0x100000001B3UL;
            public void Update(byte[] buf, int len) { for (int i = 0; i < len; i++) { _h ^= buf[i]; _h *= P; } }
            public string Final() { return _h.ToString("x16"); }
        }

        private sealed class Crc32c : IHasher
        {
            private static readonly uint[] T = Build();
            private uint _c = 0xFFFFFFFF;
            private static uint[] Build()
            {
                var t = new uint[256];
                for (uint i = 0; i < 256; i++)
                { uint c = i; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? (0x82F63B78 ^ (c >> 1)) : (c >> 1); t[i] = c; }
                return t;
            }
            public void Update(byte[] buf, int len) { for (int i = 0; i < len; i++) _c = T[(_c ^ buf[i]) & 0xFF] ^ (_c >> 8); }
            public string Final() { return (_c ^ 0xFFFFFFFF).ToString("x8"); }
        }

        private sealed class Murmur3x64_128 : IHasher
        {
            const ulong C1 = 0x87c37b91114253d5UL, C2 = 0x4cf5ad432745937fUL;
            private ulong _h1, _h2; private long _len;
            private readonly byte[] _tail = new byte[16]; private int _tailLen;

            public void Update(byte[] buf, int len)
            {
                int i = 0;
                if (_tailLen > 0)
                {
                    while (_tailLen < 16 && i < len) _tail[_tailLen++] = buf[i++];
                    if (_tailLen == 16) { Block(_tail, 0); _len += 16; _tailLen = 0; }
                }
                while (i + 16 <= len) { Block(buf, i); _len += 16; i += 16; }
                while (i < len) _tail[_tailLen++] = buf[i++];
            }

            private void Block(byte[] b, int o)
            {
                ulong k1 = BitConverter.ToUInt64(b, o), k2 = BitConverter.ToUInt64(b, o + 8);
                k1 *= C1; k1 = Rotl(k1, 31); k1 *= C2; _h1 ^= k1;
                _h1 = Rotl(_h1, 27); _h1 += _h2; _h1 = _h1 * 5 + 0x52dce729;
                k2 *= C2; k2 = Rotl(k2, 33); k2 *= C1; _h2 ^= k2;
                _h2 = Rotl(_h2, 31); _h2 += _h1; _h2 = _h2 * 5 + 0x38495ab5;
            }

            public string Final()
            {
                ulong k1 = 0, k2 = 0; long total = _len + _tailLen;
                int t = _tailLen;
                if (t > 8)
                { for (int i = t - 1; i >= 8; i--) k2 ^= (ulong)_tail[i] << (8 * (i - 8)); k2 *= C2; k2 = Rotl(k2, 33); k2 *= C1; _h2 ^= k2; }
                if (t > 0)
                { for (int i = Math.Min(t, 8) - 1; i >= 0; i--) k1 ^= (ulong)_tail[i] << (8 * i); k1 *= C1; k1 = Rotl(k1, 31); k1 *= C2; _h1 ^= k1; }
                _h1 ^= (ulong)total; _h2 ^= (ulong)total;
                _h1 += _h2; _h2 += _h1;
                _h1 = FMix(_h1); _h2 = FMix(_h2);
                _h1 += _h2; _h2 += _h1;
                var o = new byte[16];
                Array.Copy(BitConverter.GetBytes(_h1), 0, o, 0, 8);
                Array.Copy(BitConverter.GetBytes(_h2), 0, o, 8, 8);
                return Hex(o);
            }
            private static ulong Rotl(ulong x, int r) { return (x << r) | (x >> (64 - r)); }
            private static ulong FMix(ulong k) { k ^= k >> 33; k *= 0xff51afd7ed558ccdUL; k ^= k >> 33; k *= 0xc4ceb9fe1a85ec53UL; k ^= k >> 33; return k; }
        }
    }
}
