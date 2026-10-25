using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;

namespace Aloha.Proof
{

    public static class SymbolField
    {
        const int W = 16, H = 12;
        static char[,] cell = new char[H, W];

        public static void Structure()
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    cell[y, x] = '.';

            Place('a', 3, 2); Place('b', 11, 3); Place('c', 7, 6);
            Place('d', 5, 8); Place('e', 12, 9);
            Dump("field (seeded):");

            using (var pA = new GraphicsPath())
            using (var pB = new GraphicsPath())
            {
                pA.AddRectangle(new Rectangle(2, 1, 8, 7));
                pB.AddEllipse(new Rectangle(5, 3, 9, 8));
                using (var reg = new Region(pA))
                using (var rB  = new Region(pB))
                {
                    reg.Intersect(rB);

                    var caught = new List<string>();
                    for (int y = 0; y < H; y++)
                        for (int x = 0; x < W; x++)
                        {
                            bool inside = reg.IsVisible(new PointF(x + 0.5f, y + 0.5f));
                            if (inside && cell[y, x] != '.') caught.Add(cell[y, x] + "@(" + x + "," + y + ")");
                            else if (inside && cell[y, x] == '.') cell[y, x] = ':';
                        }
                    Dump("registry = overlap of two placed frames (':')  caught: "
                         + (caught.Count > 0 ? string.Join(" ", caught) : "(none)"));
                }
            }

            RotateSymbols(90f);
            Dump("after 90 deg matrix operator (symbols permuted, not drawn):");
        }

        static void RotateSymbols(float deg)
        {
            var pts  = new List<PointF>();
            var syms = new List<char>();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (cell[y, x] != '.' && cell[y, x] != ':')
                    { pts.Add(new PointF(x, y)); syms.Add(cell[y, x]); }

            var arr = pts.ToArray();
            using (var m = new Matrix())
            {
                m.RotateAt(deg, new PointF((W - 1) / 2f, (H - 1) / 2f));
                m.TransformPoints(arr);
            }

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (cell[y, x] != ':') cell[y, x] = '.';

            for (int i = 0; i < arr.Length; i++)
            {
                int nx = (int)Math.Round(arr[i].X), ny = (int)Math.Round(arr[i].Y);
                if (nx >= 0 && nx < W && ny >= 0 && ny < H) cell[ny, nx] = syms[i];
            }
        }

        static void Place(char s, int x, int y)
        {
            if (x >= 0 && x < W && y >= 0 && y < H) cell[y, x] = s;
        }

        static void Dump(string title)
        {
            Console.WriteLine(title);
            for (int y = 0; y < H; y++)
            {
                var sb = new StringBuilder();
                for (int x = 0; x < W; x++) sb.Append(cell[y, x]).Append(' ');
                Console.WriteLine(sb.ToString());
            }
            Console.WriteLine();
        }
    }
}
