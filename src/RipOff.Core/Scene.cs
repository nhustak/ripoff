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
                // The ship's shape (SHIP SELECT, NOTES §30); Arcade: set 0 for P1, 1 for P2 as before.
                DrawPlayer(s, 0, o);
                if (!s.OnePlayer) DrawPlayer(s, 1, o);
                for (int p = 0; p < (s.OnePlayer ? 1 : 2); p++) if (s.WarpTimer[p] > 0) DrawWarp(s, p, o);
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

        /// <summary>Blink period of an armored ship after an absorbed hit (passes per half cycle).</summary>
        public const int GraceBlinkPasses = 3;

        /// <summary>A player's ship: its arcade tank set, or its remake hull (+ armor plates while it has more than one hit
        /// left, NOTES §31). Blinks dim while it can't be hit after absorbing one.</summary>
        static void DrawPlayer(GameState s, int p, List<Line> o)
        {
            var r = s.Players[p];
            var ship = Ships.Get(s.Ships[p]);
            int start = o.Count;
            if (ship.Body == null || EnemyState.IsFree(r.A) || EnemyState.IsExploding(r.A)) DrawTank(r, Math.Max(0, ship.ShapeSet), o);
            else if (OnFieldDraw(r))
            {
                AddShape(ship.Body, r.X, r.Y, r.Heading, r.Scale, o);
                if (ship.Plates != null && Ships.AbilitiesOn(s.Rules) && s.Hits[p] > 1) AddShape(ship.Plates, r.X, r.Y, r.Heading, r.Scale, o);
            }
            if (Ships.AbilitiesOn(s.Rules) && s.HitGrace[p] > 0 && EnemyState.IsAlive(r.A) && (s.HitGrace[p] / GraceBlinkPasses) % 2 == 1)
                for (int i = start; i < o.Count; i++) o[i] = o[i] with { Bright = false };
        }

        static bool OnFieldDraw(TankRecord r) => !(r.X < 0 || r.X >= Width || r.Y < 0 || r.Y >= Height);

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

        /// <summary>Warp-in radius at the start of the effect (field units).</summary>
        public const float WarpRadius = 120;

        /// <summary>Enhanced warp-in effect (remake art, NOTES §28). Over the warp, t goes 0 → 1:
        /// - 12 spokes spiral in from <see cref="WarpRadius"/> to the warp point (bright);
        /// - a dim 16-sided ring shrinks with them;
        /// - from halfway on, the ship outline grows from nothing to full size (dim until it materializes).</summary>
        public static void DrawWarp(GameState s, int p, List<Line> o)
        {
            int total = Math.Max(1, s.Rules.WarpInPasses);
            float t = 1f - Math.Clamp(s.WarpTimer[p], 0, total) / (float)total;
            float x = s.WarpX[p], y = s.WarpY[p];
            float r = WarpRadius * (1 - t), len = 10 + 26 * (1 - t);
            double spin = t * Math.PI / 2;
            for (int k = 0; k < 12; k++)
            {
                double a = spin + k * Math.PI / 6;
                float c = (float)Math.Cos(a), sn = (float)Math.Sin(a);
                o.Add(new Line(x + c * (r + len), y + sn * (r + len), x + c * r, y + sn * r, true));
            }
            if (r > 2)
            {
                for (int k = 0; k < 16; k++)
                {
                    double a0 = -spin + k * Math.PI / 8, a1 = a0 + Math.PI / 8;
                    o.Add(new Line(x + r * (float)Math.Cos(a0), y + r * (float)Math.Sin(a0), x + r * (float)Math.Cos(a1), y + r * (float)Math.Sin(a1), false));
                }
            }
            if (t >= 0.5f)
            {
                int scale = (int)(GameData.PlayerScale * (t - 0.5f) * 2);
                int start = o.Count;
                var ship = Ships.Get(s.Ships[p]);
                AddShape(ship.Body ?? ShapeData.Body[Math.Clamp(ship.ShapeSet, 0, ShapeData.Body.Length - 1)], x, y, s.WarpHeading[p], scale, o);
                for (int i = start; i < o.Count; i++) o[i] = o[i] with { Bright = false };
            }
        }

        /// <summary>A ship as it looks in play (body + treads), for menus such as SHIP SELECT.</summary>
        public static void DrawShip(int ship, float x, float y, int heading, int scale, List<Line> o, bool bright = true)
        {
            var spec = Ships.Get(ship);
            int start = o.Count;
            if (spec.Body != null)
            {
                AddShape(spec.Body, x, y, heading, scale, o);
                if (spec.Plates != null) AddShape(spec.Plates, x, y, heading, scale, o);
            }
            else
            {
                int set = Math.Clamp(spec.ShapeSet, 0, ShapeData.Body.Length - 1);
                AddShape(ShapeData.Body[set], x, y, heading, scale, o);
                AddShape(ShapeData.Anim8[set][0], x, y, heading, scale, o);
                AddShape(ShapeData.Anim9[set][0], x, y, heading, scale, o);
            }
            if (!bright) for (int i = start; i < o.Count; i++) o[i] = o[i] with { Bright = false };
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

        /// <summary>Score row (remake layout, not the arcade's): edge margin in field units, glyph height 20.
        /// P1 flush left and P2 flush right along the top of the 4:3 field.</summary>
        public const int ScoreMargin = 16, ScoreGlyphHeight = 20;
        public const int ScoreY = Height - ScoreMargin - ScoreGlyphHeight; // 732: glyph baseline (y up)

        /// <summary>Digits drawn for a score: thousands word when non-zero, the units, and the trailing 0.</summary>
        public static int ScoreDigits(int tens, int thousands = 0) =>
            (thousands > 0 ? thousands.ToString().Length + 3 : (tens > 0 ? tens.ToString().Length : 0)) + 1;

        /// <summary>Left x of a score so that its last digit's ink ends at the right margin (digit ink spans 2..30
        /// of its 32-unit cell).</summary>
        public static float ScoreRightAlignedX(int tens, int thousands = 0) =>
            Width - ScoreMargin + 2 - GlyphAdvance * ScoreDigits(tens, thousands);

        /// <summary>Left x of a score so that its first digit's ink starts at the left margin ("1" is inked at 20..28 of its
        /// cell, the other digits from 2).</summary>
        public static float ScoreLeftAlignedX(int tens, int thousands = 0)
        {
            string d = thousands > 0 ? thousands.ToString() + tens.ToString("D3") : (tens > 0 ? tens.ToString() : "0");
            float ink = float.MaxValue;
            foreach (var sg in ShapeData.Glyphs[d[0] - '0']) ink = Math.Min(ink, Math.Min(sg.X1, sg.X2));
            return ScoreMargin - ink;
        }

        public static void DrawScores(GameState s, List<Line> o)
        {
            // No credit display: the remake has no credits (NOTES §23); attract mode shows no score.
            if (s.Mode != GameMode.Playing) return;
            // Remake layout (Nick, 2026-10-17), both modes: P1 top-left, P2 top-right. This is HUD only and the
            // simulation doesn't change. The arcade draws individual scores mid-field: P2 at ($1A0,$176), P1 at
            // ($260,$176) (NOTES §13, lst:1112-1156).
            // UNCHECKED: in a one-player game only P1 is shown (path $04B6 → $0505, NOTES §13).
            Number(s.Score[0], ScoreLeftAlignedX(s.Score[0], s.ScoreThousands[0]), ScoreY, o, s.ScoreThousands[0]);
            if (!s.OnePlayer) Number(s.Score[1], ScoreRightAlignedX(s.Score[1], s.ScoreThousands[1]), ScoreY, o, s.ScoreThousands[1]);
            DrawArmor(s, o);
        }

        /// <summary>Armor pip size and gap (field units) and their row under the scores.</summary>
        public const int PipSize = 10, PipGap = 6, PipY = ScoreY - 12 - PipSize;

        /// <summary>
        /// Armor pips (SHIP ABILITIES, NOTES §31), for ships with more than one hit only: one square per hit of armor
        /// under the player's score (P1 left, P2 right). A hit still left is a bright square with a cross; a hit used up
        /// is a dim outline. While dead, the pips show what the next ship gets (all full). Nothing in Arcade.
        /// </summary>
        public static void DrawArmor(GameState s, List<Line> o)
        {
            if (!Ships.AbilitiesOn(s.Rules)) return;
            for (int p = 0; p < (s.OnePlayer ? 1 : 2); p++)
            {
                int armor = Ships.Stats(s.Rules, s.Ships[p]).Armor;
                if (armor <= 1) continue;
                bool alive = !EnemyState.IsFree(s.Players[p].A) && !EnemyState.IsExploding(s.Players[p].A);
                int left = alive ? s.Hits[p] : armor;
                for (int k = 0; k < armor; k++)
                {
                    float x0 = p == 0 ? ScoreMargin + k * (PipSize + PipGap) : Width - ScoreMargin - PipSize - k * (PipSize + PipGap);
                    float y0 = PipY, x1 = x0 + PipSize, y1 = y0 + PipSize;
                    bool full = k < left;
                    o.Add(new Line(x0, y0, x1, y0, full)); o.Add(new Line(x1, y0, x1, y1, full));
                    o.Add(new Line(x1, y1, x0, y1, full)); o.Add(new Line(x0, y1, x0, y0, full));
                    if (full) { o.Add(new Line(x0, y0, x1, y1, true)); o.Add(new Line(x0, y1, x1, y0, true)); }
                }
            }
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
