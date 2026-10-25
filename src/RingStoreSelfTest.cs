using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Aloha.RingStoreCore
{
    public static class RingStoreSelfTest
    {

        private static readonly object[][] FragVectors =
        {
            new object[]{ 1000u, "446acd4b330a7b738ec69686672955d89930b69458c9a8de686c0558e93afa53" },
            new object[]{ 1001u, "523c086be7967d8e834a85b9cf3dc42b16281886cbd3fbfa28cfff233b5c6cc1" },
            new object[]{ 1002u, "f75ca91b9c9641fb49e1bc2b9c404948b538fb4db3cb8ec65e753c707bbe8e32" },
            new object[]{ 1003u, "e78ce3e09c35eec9095692e8f55b153581557410cc4a9a7010e7cdecd3edbd33" },
            new object[]{ 1004u, "438ee6c95c5ba89ad4296b20ef52029c91a229061ee2d0e9be85f5ce400c0654" },
            new object[]{ 2010u, "d887589baca996c0d453f8376f04820a082ee7ab1342e54f19d72c2b4b7c301a" },
        };
        private const string BookmarkASha        = "0d88e4f7216fae779d98fb2e6a55c984727f5f9b53577917274b3426c1ceee2a";
        private const string BookmarkANumbersHex = "00000000000003e800000001000003e900000002000003ea00000003000003eb";
        private const int    BookmarkALen        = 4194304;

        public static bool Run(Action<string> log)
        {
            bool ok = true;

            using (SHA256 sha = SHA256.Create())
            {
                foreach (object[] v in FragVectors)
                {
                    uint payload = (uint)v[0];
                    string want = (string)v[1];
                    string got = Hex(sha.ComputeHash(RingStore.GenerateFragment(payload, 1u, RingStore.FRAG_SIZE)));
                    bool pass = got == want; ok &= pass;
                    log(string.Format("frag {0,5} parity     : {1}", payload, pass ? "OK" : "MISMATCH"));
                }
            }

            var ring = new RingStore(1u);
            var bm   = new BookmarkStore(ring);
            BookmarkRef refA = bm.AddAuthored(0, new uint[] { 1000u, 1001u, 1002u, 1003u });
            byte[] content = bm.Read(refA);
            bool bmPass = content.Length == BookmarkALen && Sha(content) == BookmarkASha;
            ok &= bmPass;
            log(string.Format("bookmark A round-trip: {0} ({1} bytes)", bmPass ? "OK" : "MISMATCH", content.Length));

            string numsHex = NumbersHex(ring);
            bool serPass = numsHex == BookmarkANumbersHex; ok &= serPass;
            log(string.Format("serialization parity : {0}", serPass ? "OK" : "MISMATCH"));

            int before = ring.SerializedSize;
            ring.Write(4, 1004u);
            bool appendPass = ring.SerializedSize - before == 8; ok &= appendPass;
            string aHash = Sha(bm.Read(refA));
            bool unchanged = aHash == BookmarkASha; ok &= unchanged;
            log(string.Format("append grew by 8     : {0}", appendPass ? "OK" : "MISMATCH"));
            log(string.Format("bookmark A unchanged : {0}", unchanged ? "OK" : "MISMATCH"));

            var sparse = new RingStore(1u);
            sparse.Write(0, 1000u); sparse.Write(1, 1001u); sparse.Write(3, 1003u);
            bool gapPass = sparse.ReadPosition(2) == null
                           && Sha(sparse.ReadPosition(3)) == FragSha(1003u); ok &= gapPass;
            log(string.Format("clean gap / no shift : {0}", gapPass ? "OK" : "MISMATCH"));

            long expanded = 4L * RingStore.FRAG_SIZE;
            log(string.Format("store {0} B -> expanded {1} B  ({2:N0}:1)",
                ring.SerializedSize - 8, expanded, expanded / (ring.SerializedSize - 8)));

            log(ok ? "ALL PARITY CHECKS PASSED" : "FAILURES PRESENT");
            return ok;
        }

        private static string FragSha(uint payload)
        {
            using (SHA256 s = SHA256.Create())
                return Hex(s.ComputeHash(RingStore.GenerateFragment(payload, 1u, RingStore.FRAG_SIZE)));
        }
        private static string Sha(byte[] b)
        {
            using (SHA256 s = SHA256.Create()) return Hex(s.ComputeHash(b));
        }
        private static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (byte x in b) sb.Append(x.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
        private static string NumbersHex(RingStore ring)
        {
            var sb = new StringBuilder();
            byte[] nb = new byte[8];
            foreach (ulong n in ring.Numbers)
            {
                RingStore.WriteUInt64BE(nb, 0, n);
                foreach (byte x in nb) sb.Append(x.ToString("x2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
