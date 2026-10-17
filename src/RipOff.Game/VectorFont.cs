using System;
using System.Collections.Generic;
using RipOff.Core;

namespace RipOff.Game
{
    /// <summary>
    /// Stroke font for menus and status text (our own UI, not arcade data): each glyph is line segments on a 4×6
    /// grid, y up, drawn through the same line renderer as the game.
    /// </summary>
    public static class VectorFont
    {
        private static readonly Dictionary<char, (float x1, float y1, float x2, float y2)[]> Glyphs = Build();

        private static Dictionary<char, (float, float, float, float)[]> Build()
        {
            const string O = "0,0 0,6|0,6 4,6|4,6 4,0|4,0 0,0";
            const string P = "0,0 0,6|0,6 4,6|4,6 4,3|4,3 0,3";
            const string S = "4,6 0,6|0,6 0,3|0,3 4,3|4,3 4,0|4,0 0,0";
            var src = new Dictionary<char, string>
            {
                ['A'] = "0,0 0,4|0,4 2,6|2,6 4,4|4,4 4,0|0,3 4,3",
                ['B'] = "0,0 0,6|0,6 3,6|3,6 4,5|4,5 4,4|4,4 3,3|0,3 3,3|3,3 4,2|4,2 4,1|4,1 3,0|3,0 0,0",
                ['C'] = "4,6 0,6|0,6 0,0|0,0 4,0",
                ['D'] = "0,0 0,6|0,6 2,6|2,6 4,4|4,4 4,2|4,2 2,0|2,0 0,0",
                ['E'] = "4,6 0,6|0,6 0,0|0,0 4,0|0,3 3,3",
                ['F'] = "4,6 0,6|0,6 0,0|0,3 3,3",
                ['G'] = "4,6 0,6|0,6 0,0|0,0 4,0|4,0 4,3|4,3 2,3",
                ['H'] = "0,0 0,6|4,0 4,6|0,3 4,3",
                ['I'] = "0,6 4,6|2,6 2,0|0,0 4,0",
                ['J'] = "4,6 4,0|4,0 0,0|0,0 0,2",
                ['K'] = "0,0 0,6|4,6 0,3|0,3 4,0",
                ['L'] = "0,6 0,0|0,0 4,0",
                ['M'] = "0,0 0,6|0,6 2,3|2,3 4,6|4,6 4,0",
                ['N'] = "0,0 0,6|0,6 4,0|4,0 4,6",
                ['O'] = O,
                ['P'] = P,
                ['Q'] = O + "|2,2 4,0",
                ['R'] = P + "|0,3 4,0",
                ['S'] = S,
                ['T'] = "0,6 4,6|2,6 2,0",
                ['U'] = "0,6 0,0|0,0 4,0|4,0 4,6",
                ['V'] = "0,6 2,0|2,0 4,6",
                ['W'] = "0,6 1,0|1,0 2,3|2,3 3,0|3,0 4,6",
                ['X'] = "0,0 4,6|0,6 4,0",
                ['Y'] = "0,6 2,3|4,6 2,3|2,3 2,0",
                ['Z'] = "0,6 4,6|4,6 0,0|0,0 4,0",
                ['0'] = O + "|0,0 4,6",
                ['1'] = "2,0 2,6|2,6 1,5|1,0 3,0",
                ['2'] = "0,6 4,6|4,6 4,3|4,3 0,3|0,3 0,0|0,0 4,0",
                ['3'] = "0,6 4,6|4,6 4,0|4,0 0,0|1,3 4,3",
                ['4'] = "0,6 0,3|0,3 4,3|4,6 4,0",
                ['5'] = S,
                ['6'] = "4,6 0,6|0,6 0,0|0,0 4,0|4,0 4,3|4,3 0,3",
                ['7'] = "0,6 4,6|4,6 2,0",
                ['8'] = O + "|0,3 4,3",
                ['9'] = "4,3 0,3|0,3 0,6|0,6 4,6|4,6 4,0|4,0 0,0",
                ['.'] = "2,0 2,0.3",
                [':'] = "2,1 2,1.3|2,4.7 2,5",
                ['-'] = "1,3 3,3",
                ['/'] = "0,0 4,6",
                ['>'] = "0,6 4,3|4,3 0,0",
                ['<'] = "4,6 0,3|0,3 4,0",
                ['?'] = "0,6 4,6|4,6 4,3|4,3 2,3|2,3 2,2|2,0 2,0.3",
                ['!'] = "2,6 2,2|2,0 2,0.3",
                ['('] = "3,6 1,4|1,4 1,2|1,2 3,0",
                [')'] = "1,6 3,4|3,4 3,2|3,2 1,0",
                ['%'] = "0,0 4,6|0,6 0,5|4,1 4,0",
                ['+'] = "2,1 2,5|0,3 4,3",
                [','] = "2,0.5 1,-1",
                ['\''] = "2,6 2,5",
                ['='] = "0,2 4,2|0,4 4,4",
                ['_'] = "0,-0.5 4,-0.5",
                [' '] = "",
            };
            var d = new Dictionary<char, (float, float, float, float)[]>();
            foreach (var (ch, def) in src)
            {
                var segs = new List<(float, float, float, float)>();
                foreach (var part in def.Split('|', StringSplitOptions.RemoveEmptyEntries))
                {
                    var pts = part.Split(' ');
                    var a = pts[0].Split(','); var b = pts[1].Split(',');
                    float F(string v) => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
                    segs.Add((F(a[0]), F(a[1]), F(b[0]), F(b[1])));
                }
                d[ch] = segs.ToArray();
            }
            return d;
        }

        public static bool Supports(char c) => Glyphs.ContainsKey(char.ToUpperInvariant(c));

        /// <summary>Advance per character for glyph height <paramref name="size"/> (cell 4 wide + 2 gap, 6 high).</summary>
        public static float Advance(float size) => size * 6 / 6f;

        public static float Width(string text, float size) => text.Length * Advance(size) - size / 3f;

        /// <summary>Draws <paramref name="text"/> with its baseline-left at (x, y); characters are size high.</summary>
        public static void Draw(string text, float x, float y, float size, List<Line> into, bool bright = true)
        {
            float k = size / 6f;
            foreach (char raw in text)
            {
                char c = char.ToUpperInvariant(raw);
                if (Glyphs.TryGetValue(c, out var segs))
                    foreach (var (x1, y1, x2, y2) in segs) into.Add(new Line(x + x1 * k, y + y1 * k, x + x2 * k, y + y2 * k, bright));
                x += Advance(size);
            }
        }

        public static void DrawCentered(string text, float cx, float y, float size, List<Line> into, bool bright = true)
            => Draw(text, cx - Width(text, size) / 2, y, size, into, bright);
    }
}
