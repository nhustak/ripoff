using System;
using System.Collections.Generic;
using RipOff.Core.Shapes;

namespace RipOff.Core
{
    /// <summary>A line in field coordinates (0..1024 × 0..768, y up, NOTES §10.1). Bright = not dim (NOTES §5).</summary>
    public readonly record struct Line(float X1, float Y1, float X2, float Y2, bool Bright);

    /// <summary>
    /// Turns a <see cref="GameState"/> into vector lines. Pure, deterministic, renderer-agnostic: the MonoGame
    /// line layer and the headless PNG check both draw this list.
    /// </summary>
    public static class Scene
    {
        public const int Width = 1024, Height = 768;

        public static List<Line> Build(GameState s, List<Line>? into = null)
        {
            var o = into ?? new List<Line>();
            o.Clear();
            if (s.Mode == GameMode.Playing && s.BonusFlag) { DrawBonus(s, o); return o; }

            DrawCanisters(s, o);
            for (int i = 0; i < 3; i++) DrawTank(s.Enemies[i], s.Enemies[i].Type + 1, o);
            if (s.Mode == GameMode.Playing)
            {
                DrawTank(s.Players[0], 0, o);
                if (!s.OnePlayer) DrawTank(s.Players[1], 1, o);
            }
            foreach (var fl in s.FireLines)
            {
                var e = s.Enemies[fl.Record];
                // Fire line: shapes $1F89/$1F92/$1F9B/$1FA4 × scale 80/128 along the heading (GR §6 "Fire line",
                // lst:3125-3145). Frame = RAM[$A8] & 3. UNCHECKED: frame index = A8 & 3 (see State.FireLine).
                AddShape(ShapeData.FireLine[fl.Frame], e.X, e.Y, e.Heading, GameData.EnemyScale, o);
            }
            foreach (var sh in s.Shots)
            {
                if (!sh.Live) continue;
                // UNCHECKED: how the arcade draws a shot is not decoded; drawn here as a 2-unit line.
                o.Add(new Line(sh.X - 1, sh.Y, sh.X + 1, sh.Y, true));
            }
            DrawScores(s, o);
            return o;
        }

        static void DrawTank(TankRecord r, int set, List<Line> o)
        {
            if (EnemyState.IsFree(r.A)) return;
            if (EnemyState.IsExploding(r.A))
            {
                // UNCHECKED: explosion art. We draw shape $1FAE (headerless burst) centred on the tank, sized by
                // the explosion countdown (word 4 from $200/$300 down by $20, GR §7 rows 98-99).
                float k = Math.Max(0, r.Scale) / (float)GameData.ExplosionRam;
                foreach (var sg in ShapeData.Burst1FAE)
                    o.Add(new Line(r.X + (sg.X1 - 34) * k, r.Y + (sg.Y1 - 32) * k,
                                   r.X + (sg.X2 - 34) * k, r.Y + (sg.Y2 - 32) * k, true));
                return;
            }
            if (r.X < 0 || r.X >= Width || r.Y < 0 || r.Y >= Height) return; // off-field: draw skipped (GR §6)
            set = Math.Clamp(set, 0, ShapeData.Body.Length - 1);
            // Body + tread frames: origin + R_ccw(heading)·p·scale/128 (NOTES §9.4, §10). Scale = word 4.
            AddShape(ShapeData.Body[set], r.X, r.Y, r.Heading, r.Scale, o);
            // UNCHECKED: tread animation advance rule; frame 0 of both tread lists is drawn.
            AddShape(ShapeData.Anim8[set][0], r.X, r.Y, r.Heading, r.Scale, o);
            AddShape(ShapeData.Anim9[set][0], r.X, r.Y, r.Heading, r.Scale, o);
        }

        static void AddShape(Seg[] segs, float ox, float oy, int heading, int scale, List<Line> o)
        {
            double a = heading * Math.PI / 180.0, k = scale / 128.0;
            double c = Math.Cos(a) * k, sn = Math.Sin(a) * k;
            foreach (var g in segs)
            {
                o.Add(new Line((float)(ox + c * g.X1 - sn * g.Y1), (float)(oy + sn * g.X1 + c * g.Y1),
                               (float)(ox + c * g.X2 - sn * g.Y2), (float)(oy + sn * g.X2 + c * g.Y2), !g.Dim));
            }
        }

        static void DrawCanisters(GameState s, List<Line> o)
        {
            // Every slot whose y word bit 0 is clear is drawn at (x/2 − $0D, y/2 − $2E) of the table words
            // (NOTES §10.3, lst:4613-4624): $173E for even entries, $172C for odd; $1747/$1735 when RAM[$A8] bit 1
            // is set (lst:4633-4654). Canister[] order: $172C, $1735, $173E, $1747.
            bool small = (s.PassCounter & 2) != 0;
            for (int j = 0; j < s.Canisters.Length; j++)
            {
                var c = s.Canisters[j];
                if (c.Gone) continue;
                int g = (j & 1) == 0 ? (small ? 3 : 2) : (small ? 1 : 0);
                float x0 = (c.X2 >> 1) - 0x0D, y0 = (c.Y2 >> 1) - 0x2E;
                foreach (var sg in ShapeData.Canister[g]) o.Add(new Line(x0 + sg.X1, y0 + sg.Y1, x0 + sg.X2, y0 + sg.Y2, true));
            }
        }

        /// <summary>Glyph advance. UNCHECKED: per-glyph advance of the $157F routine; 32 used (digit width 2..30).</summary>
        public const int GlyphAdvance = 32;

        static float Glyph(int g, float x, float y, List<Line> o)
        {
            foreach (var sg in ShapeData.Glyphs[g]) o.Add(new Line(x + sg.X1, y + sg.Y1, x + sg.X2, y + sg.Y2, true));
            float w = 0; foreach (var sg in ShapeData.Glyphs[g]) w = Math.Max(w, Math.Max(sg.X1, sg.X2));
            return x + (g >= 10 ? w + 8 : GlyphAdvance);
        }

        /// <summary>Score in tens with leading-zero suppression and the trailing "0" (NOTES §13 "Score display"):
        /// the thousands word only when non-zero, then the units. UNCHECKED: units zero-padded to 3 digits after a
        /// non-zero thousands word.</summary>
        static float Number(int tens, float x, float y, List<Line> o, int thousands = 0)
        {
            string d = thousands > 0 ? thousands.ToString() + tens.ToString("D3") : (tens > 0 ? tens.ToString() : "");
            foreach (char ch in d) x = Glyph(ch - '0', x, y, o);
            return Glyph(0, x, y, o);
        }

        static void DrawScores(GameState s, List<Line> o)
        {
            if (s.Mode != GameMode.Playing)
            {
                // UNCHECKED: the arcade attract screen (credits, high scores at ($200,$10)/($200,$2DC), NOTES §13) is
                // not reproduced; the remake shows only the credit count, bottom centre, so a player can see it.
                float x = 0x200 - GlyphAdvance / 2f;
                foreach (char ch in s.Credits.ToString()) x = Glyph(ch - '0', x, 0x40, o);
                return;
            }
            // Individual scores (Scores DIP 0): P2 at ($1A0,$176), P1 at ($260,$176) (NOTES §13, lst:1112-1156).
            // UNCHECKED: in a one-player game only P1 is shown (path $04B6 → $0505, NOTES §13).
            Number(s.Score[0], 0x260, 0x176, o, s.ScoreThousands[0]);
            if (!s.OnePlayer) Number(s.Score[1], 0x1A0, 0x176, o, s.ScoreThousands[1]);
        }

        static void DrawBonus(GameState s, List<Line> o)
        {
            // "BONUS LEVEL + (L−1)0" (NOTES §13, lst:1252-1353). UNCHECKED: glyph positions; centred here.
            float x = 512 - 200, y = 384;
            x = Glyph(10, x, y, o);
            x = Glyph(11, x + 8, y, o);
            x = Glyph(12, x + 8, y, o);
            Number(s.Level - 1, x, y, o);
        }
    }
}
