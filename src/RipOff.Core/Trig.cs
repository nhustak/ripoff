using System;

namespace RipOff.Core
{
    /// <summary>
    /// Integer trigonometry in the arcade's units: angles in degrees 0..359,
    /// results scaled by an integer "scale" (speed, draw scale).
    /// </summary>
    public static class Trig
    {
        /// <summary>
        /// Quarter-wave sine table, k = 0..90 degrees, value ≈ 256·sin(k°).
        /// Re-derived from the documented formula, not copied from the ROM:
        /// "min(255, round(256·sin(k°)))" with the two documented exceptions
        /// k=38 → 159 and k=70 → 240 (NOTES §9.1; table at $1750, lst:5174-5186).
        /// </summary>
        public static readonly int[] SinTable = BuildSinTable();

        private static int[] BuildSinTable()
        {
            var t = new int[91];
            for (int k = 0; k <= 90; k++)
                t[k] = Math.Min(255, (int)Math.Round(256.0 * Math.Sin(k * Math.PI / 180.0), MidpointRounding.AwayFromZero));
            t[38] = 159; // NOTES §9.1: ROM differs from the formula here
            t[70] = 240; // NOTES §9.1
            return t;
        }

        /// <summary>
        /// Port of $1204 (lst:4188-4311, NOTES §9.2): returns (scale·sin(angle), scale·cos(angle)).
        /// Angle ≥ 360 is reduced once (lst:4192-4198). Quadrant fix-up as in lst:4274-4309.
        /// Rounding of each product is round-half-up ((t·scale + 128) >> 8): it fits all 32,057 moving enemy
        /// frames without a heading change in 9 runs, floor fits only 24,798 (trace:excerpts/p7_checks.txt (a)),
        /// and the full-frame replays match bit-exact (same file, "replay").
        /// Also returns the quarter count q (RAM[$E6]) that the player wall-bounce uses.
        /// </summary>
        public static (int Sin, int Cos, int Quarter) ScaledSinCos(int angle, int scale)
        {
            if (angle >= 360) angle -= 360;
            if (angle < 0) angle += 360; // callers normalise first; kept defensive
            int q = angle / 90;
            int phi = angle % 90;
            int s = (SinTable[phi] * scale + 128) >> 8;
            int c = (SinTable[90 - phi] * scale + 128) >> 8;
            if ((q & 1) != 0) (s, c) = (c, s);
            if (q >= 2) s = -s;
            if (q == 1 || q == 2) c = -c;
            return (s, c, q);
        }

        /// <summary>
        /// Per-update displacement for a tank at heading h moving at speed v:
        /// the code passes angle h−90 to $1204 and adds (cos, sin) to (x, y)
        /// (lst:1786-1841 enemies, lst:2256-2287 players), i.e. (v·sin h, −v·cos h)
        /// (GAME_RULES §Units "Heading convention").
        /// </summary>
        public static (int Dx, int Dy, int Quarter) Displacement(int heading, int speed)
        {
            int a = heading - 90;
            if (a < 0) a += 360; // lst:1791-1797 ($169 compare, +360)
            var (s, c, q) = ScaledSinCos(a, speed);
            return (c, s, q);
        }

        /// <summary>
        /// The atan table at $17EB (lst:5197), indexed [max][min] for 0 ≤ min ≤ max ≤ 31 (row offsets
        /// n(n+1)/2 from $17AB, lst:5187). Re-derived from the documented rule (NOTES §9.3, §20.4):
        /// round-half-up(90 − atan(min/max) in degrees), entry [0][0] = 45, with the 7 documented exceptions.
        /// </summary>
        public static readonly int[][] AtanTable = BuildAtanTable();

        private static int[][] BuildAtanTable()
        {
            var t = new int[32][];
            for (int max = 0; max < 32; max++)
            {
                t[max] = new int[max + 1];
                for (int min = 0; min <= max; min++)
                {
                    double f = max == 0 ? 45.0 : 90.0 - Math.Atan(min / (double)max) * 180.0 / Math.PI;
                    t[max][min] = (int)Math.Floor(f + 0.5);
                }
            }
            // NOTES §20.4: entries where the ROM table differs from the rounded formula.
            t[10][1] = 85; t[22][6] = 76; t[24][21] = 50; t[27][8] = 74;
            t[30][23] = 54; t[31][19] = 59; t[31][23] = 55;
            return t;
        }

        /// <summary>
        /// Port of the atan2 at $1000 (lst:3705-3854, NOTES §9.3, decoded in §20.4): the bearing of (dx, dy)
        /// in the heading convention. Signs give a quadrant code (lst:3707-3740); both magnitudes are halved
        /// until each is below 32 (lst:3741-3766); the table gives the angle; quadrant offsets of 90° are
        /// added (lst:3813-3846). The result can be 360 (dx = 0, dy &lt; 0), and (0, 0) gives 315; both are
        /// literal and match the trace (trace:excerpts/p7_checks.txt (g)).
        /// </summary>
        public static int Bearing(int dx, int dy)
        {
            int ax, ay, quad;
            if (dx > 0)
            {
                ax = dx;
                if (dy > 0) { ay = dy; quad = 1; } else { ay = -dy; quad = 4; }
            }
            else
            {
                ax = -dx;
                if (dy > 0) { ay = dy; quad = 2; } else { ay = -dy; quad = 3; }
            }
            while (ax >= 32 || ay >= 32) { ax >>= 1; ay >>= 1; }
            int max = ax < ay ? ay : ax;
            int min = ax < ay ? ax : ay;
            int v = AtanTable[max][min];
            int e5 = ax < ay ? v : 90 - v;          // lst:3809-3812 ($106B-$1074)
            if ((quad & 1) == 0) e5 = 90 - e5;     // lst:3813-3820 ($1075-$107E)
            int add = quad == 4 ? 0 : quad;        // lst:3821-3846 ($107F-$109A)
            return e5 + 90 * add;
        }

        /// <summary>Port of the multiply in $135A: floor(m·a/128) with a = 0..255 unsigned, m signed (NOTES §9).</summary>
        public static int Mul128(int m, int a) => (int)Math.Floor(m * (double)a / 128.0);
    }
}
