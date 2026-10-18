using RipOff.Core;

namespace RipOff.Core.Tests;

/// <summary>SHIP ABILITIES (NOTES §31): speed, turn, armor and damage per ship, Enhanced only.</summary>
public class ShipAbilitiesTests
{
    static GameConfig Cfg(int s1, int s2, EnhancedOption o = EnhancedOption.All) => new(8, false, PlayMode.Enhanced, o, s1, s2);

    [Fact]
    public void RosterAndStats()
    {
        Assert.Equal(4, Ships.Count);
        Assert.Equal(new[] { "CLASSIC 1", "CLASSIC 2", "SCOUT", "CRUISER" }, Ships.All.Select(s => s.Name));
        var on = GameRules.Enhanced;
        Assert.Equal((10, 8, 1, 1), Ships.Stats(on, Ships.Classic1));
        Assert.Equal((10, 8, 1, 1), Ships.Stats(on, Ships.Classic2));
        Assert.Equal((14, 10, 1, 1), Ships.Stats(on, Ships.Scout));
        Assert.Equal((7, 6, 2, 2), Ships.Stats(on, Ships.Cruiser));
        foreach (var r in new[] { GameRules.Arcade, GameRules.For(PlayMode.Enhanced, EnhancedOption.All & ~EnhancedOption.ShipAbilities) })
            for (int i = 0; i < Ships.Count; i++) Assert.Equal((10, 8, 1, 1), Ships.Stats(r, i));
        Assert.Equal(38, on.ArmorGracePasses);
        Assert.Equal(0, GameRules.Arcade.ArmorGracePasses);
    }

    [Fact]
    public void ToggleNeedsShipSelect()
    {
        Assert.Equal(128, (int)EnhancedOption.ShipAbilities);
        Assert.True(GameRules.Enhanced.ShipAbilities);
        Assert.False(GameRules.Arcade.ShipAbilities);
        Assert.Contains(GameRules.Variations, v => v.Option == EnhancedOption.ShipAbilities && v.Requires == EnhancedOption.ShipSelect);
        var noSelect = GameRules.For(PlayMode.Enhanced, EnhancedOption.All & ~EnhancedOption.ShipSelect);
        Assert.True(noSelect.ShipAbilities);
        Assert.False(Ships.AbilitiesOn(noSelect)); // ignored while SHIP SELECT is off
        for (int i = 0; i < Ships.Count; i++) Assert.Equal((10, 8, 1, 1), Ships.Stats(noSelect, i));
        Assert.True(Ships.AbilitiesOn(GameRules.Enhanced));
        Assert.False(Ships.AbilitiesOn(GameRules.Arcade));
    }

    /// <summary>Holds thrust and left for a while; returns each player's highest speed and turn step while alive.</summary>
    static (int[] Speed, int[] Turn) Drive(GameConfig cfg)
    {
        var sim = ArcadeDeterminismTests.Create(4, false, cfg);
        int[] sp = new int[2], tu = new int[2];
        for (int f = 0; f < 400; f++)
        {
            var i = new PlayerInput { Thrust = true, Left = (f / 60) % 2 == 0, Right = (f / 60) % 2 == 1 };
            sim.Step(new FrameInput { P1 = i, P2 = i });
            for (int p = 0; p < 2; p++)
                if (EnemyState.IsAlive(sim.State.Players[p].A))
                { sp[p] = Math.Max(sp[p], sim.State.Players[p].Speed); tu[p] = Math.Max(tu[p], sim.State.Players[p].TurnStep); }
        }
        return (sp, tu);
    }

    [Fact]
    public void ScoutIsFastAndNimbleCruiserSlow()
    {
        var (sp, tu) = Drive(Cfg(Ships.Scout, Ships.Cruiser));
        Assert.Equal(new[] { 14, 7 }, sp);
        Assert.Equal(new[] { 10, 6 }, tu);
        (sp, tu) = Drive(Cfg(Ships.Classic1, Ships.Classic2));
        Assert.Equal(new[] { 10, 10 }, sp);
        Assert.Equal(new[] { 8, 8 }, tu);
        (sp, tu) = Drive(Cfg(Ships.Scout, Ships.Cruiser, EnhancedOption.All & ~EnhancedOption.ShipAbilities));
        Assert.Equal(new[] { 10, 10 }, sp);                     // abilities off: arcade caps for every ship
        Assert.Equal(new[] { 8, 8 }, tu);
    }

    [Fact]
    public void ScoutShotsAreNoFasterThanTheArcadesFastest()
    {
        var sim = ArcadeDeterminismTests.Create(4, true, Cfg(Ships.Scout, Ships.Classic2));
        int fastest = 0, shipFastest = 0;
        for (int f = 0; f < 600; f++)
        {
            sim.Step(new FrameInput { P1 = new PlayerInput { Thrust = true, Fire = f % 2 == 0, Left = f % 90 < 20 } });
            shipFastest = Math.Max(shipFastest, sim.State.Players[0].Speed);
            foreach (var sh in sim.State.Shots) if (sh.Live) fastest = Math.Max(fastest, Math.Max(Math.Abs(sh.Vx), Math.Abs(sh.Vy)));
        }
        Assert.Equal(14, shipFastest);
        Assert.InRange(fastest, 20, GameData.PlayerMaxSpeed + GameData.ShotSpeedBonus);
    }

    /// <summary>Scripted 2P games with a CRUISER for P1: every enemy-fire hit is followed through.</summary>
    [Fact]
    public void CruiserTakesTwoHitsAndBlinksInBetween()
    {
        int saves = 0, fireDeaths = 0;
        for (int seed = 1; seed <= 16; seed++) CruiserGame(seed, ref saves, ref fireDeaths);
        Assert.True(saves > 0, "no absorbed hit");
        Assert.True(fireDeaths > 0, "no death by enemy fire");
    }

    static void CruiserGame(int seed, ref int saves, ref int fireDeaths)
    {
        var rules = GameRules.Enhanced;
        var sim = ArcadeDeterminismTests.Create(seed, false, Cfg(Ships.Cruiser, Ships.Classic2));
        var s = sim.State;
        uint r = (uint)seed * 747796405u + 2891336453u;
        uint Rand() { r ^= r << 13; r ^= r >> 17; r ^= r << 5; return r; }
        int held = 0, left = 0;
        int prevHits = 1, prevSaves = 0; bool prevAlive = false;
        for (int f = 0; f < 12000; f++)
        {
            if (--left <= 0) { held = (int)(Rand() & 0xF); left = 1 + (int)(Rand() % 40); }
            var i = new PlayerInput { Left = (held & 1) != 0, Right = (held & 3) == 2, Thrust = (held & 4) != 0, Fire = (held & 8) != 0 && f % 2 == 0 };
            sim.Step(new FrameInput { P1 = i, P2 = i });
            var p = s.Players[0];
            bool alive = EnemyState.IsAlive(p.A);
            if (s.ArmorSaves[0] > prevSaves)
            {
                saves++;
                if (!alive) Assert.Equal(1, p.C);                 // absorbed: still flying, unless rammed on the same pass (rams are fatal)
                Assert.Equal(2, prevHits);
                Assert.Equal(1, s.Hits[0]);
                Assert.Equal(rules.ArmorGracePasses - 1, s.HitGrace[0]); // set in EnemyPass, counted down in PlayerPass
            }
            if (prevAlive && EnemyState.IsExploding(p.A) && p.C == 2) { fireDeaths++; Assert.Equal(1, prevHits); } // shot down only on its last hit
            if (alive && !prevAlive) Assert.Equal(2, s.Hits[0]);  // every new ship has full armor
            Assert.Equal(0, s.ArmorSaves[1]);                     // CLASSIC 2 has no armor
            prevHits = s.Hits[0]; prevSaves = s.ArmorSaves[0]; prevAlive = alive;
        }
    }

    [Fact]
    public void AbilitiesAreHashedOnlyWhenOn()
    {
        var on = Cfg(Ships.Cruiser, Ships.Scout);
        Assert.NotEqual(ArcadeDeterminismTests.Run(2, false, on), ArcadeDeterminismTests.Run(2, false, on with { Options = EnhancedOption.All & ~EnhancedOption.ShipAbilities }));
        // Arcade ignores everything about ships
        var a = new GameConfig(8, false, PlayMode.Arcade, EnhancedOption.All, Ships.Cruiser, Ships.Scout);
        Assert.Equal(ArcadeDeterminismTests.Run(2, false, new GameConfig(8)), ArcadeDeterminismTests.Run(2, false, a));
    }

    [Fact]
    public void SceneShowsHullPlatesPipsAndBlink()
    {
        var sim = new Simulation(); sim.StartGame(false, Cfg(Ships.Cruiser, Ships.Scout, EnhancedOption.All & ~EnhancedOption.WarpIn));
        for (int i = 0; i < 3; i++) sim.Step(default);
        var s = sim.State;
        var p1 = s.Players[0]; var p2 = s.Players[1];
        Assert.True(EnemyState.IsAlive(p1.A) && EnemyState.IsAlive(p2.A));
        static List<Line> Shape(Shapes.Seg[] segs, TankRecord r)
        {
            var l = new List<Line>();
            double a = r.Heading * Math.PI / 180.0, k = r.Scale / 128.0, c = Math.Cos(a) * k, sn = Math.Sin(a) * k;
            foreach (var g in segs)
                l.Add(new Line((float)(r.X + c * g.X1 - sn * g.Y1), (float)(r.Y + sn * g.X1 + c * g.Y1), (float)(r.X + c * g.X2 - sn * g.Y2), (float)(r.Y + sn * g.X2 + c * g.Y2), !g.Dim));
            return l;
        }
        var lines = Scene.Build(s);
        Assert.All(Shape(Ships.CruiserBody, p1), l => Assert.Contains(l, lines));
        Assert.All(Shape(Ships.CruiserPlates, p1), l => Assert.Contains(l, lines)); // 2 hits left: plated
        Assert.All(Shape(Ships.ScoutBody, p2), l => Assert.Contains(l, lines));
        // pips: two full ones (4 sides + 2 diagonals each) for the CRUISER, none for the SCOUT
        int Pips(List<Line> ls) => ls.Count(l => l.Y1 >= Scene.PipY && l.Y1 <= Scene.PipY + Scene.PipSize && l.Y2 >= Scene.PipY && l.Y2 <= Scene.PipY + Scene.PipSize && l.X1 < 200);
        Assert.Equal(12, Pips(lines));
        Assert.Equal(0, lines.Count(l => l.Y1 >= Scene.PipY && l.Y1 <= Scene.PipY + Scene.PipSize && l.Y2 >= Scene.PipY && l.Y2 <= Scene.PipY + Scene.PipSize && l.X1 > 800));
        // after an absorbed hit: no plates, one full pip + one dim outline, the ship blinks dim
        s.Hits[0] = 1; s.HitGrace[0] = Scene.GraceBlinkPasses; // (3 / 3) % 2 == 1: dim half
        lines = Scene.Build(s);
        Assert.DoesNotContain(Shape(Ships.CruiserPlates, p1)[0], lines);
        Assert.All(Shape(Ships.CruiserBody, p1), l => Assert.Contains(l with { Bright = false }, lines));
        Assert.Equal(10, Pips(lines));
        s.HitGrace[0] = 0;
        Assert.All(Shape(Ships.CruiserBody, p1), l => Assert.Contains(l, Scene.Build(s)));
        // abilities off: the hull is still the CRUISER's, but no plates and no pips
        sim = new Simulation(); sim.StartGame(false, Cfg(Ships.Cruiser, Ships.Scout, EnhancedOption.All & ~EnhancedOption.WarpIn & ~EnhancedOption.ShipAbilities));
        for (int i = 0; i < 3; i++) sim.Step(default);
        lines = Scene.Build(sim.State);
        Assert.All(Shape(Ships.CruiserBody, sim.State.Players[0]), l => Assert.Contains(l, lines));
        Assert.DoesNotContain(Shape(Ships.CruiserPlates, sim.State.Players[0])[0], lines);
        Assert.Equal(0, Pips(lines));
    }
}
