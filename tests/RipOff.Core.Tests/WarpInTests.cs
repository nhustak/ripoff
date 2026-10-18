using RipOff.Core;

namespace RipOff.Core.Tests;

/// <summary>Enhanced warp-in (NOTES §28): timing, no hits / collisions / fire while warping, the respawn path, the
/// effect lines, and Arcade untouched.</summary>
public class WarpInTests
{
    /// <summary>Warp-in at the fixed points without warp kills: the warp-in of 2026.10.17.3/.4 (random spots and warp
    /// kills are WarpRandomTests).</summary>
    /// <summary>The 2026.10.17.4 options: WARP-IN at the fixed points plus the three tunings (no later features).</summary>
    public const EnhancedOption FixedWarp = EnhancedOption.WarpIn | EnhancedOption.EnemyAimError | EnhancedOption.SlowerRamp | EnhancedOption.SlowerTowing;

    static Simulation Start(bool onePlayer = true, EnhancedOption o = FixedWarp, PlayMode m = PlayMode.Enhanced)
    {
        var sim = new Simulation();
        sim.StartGame(onePlayer, new GameConfig(8, false, m, o));
        return sim;
    }

    static readonly FrameInput Busy = new() { P1 = new PlayerInput { Fire = true, Thrust = true, Left = true }, P2 = new PlayerInput { Fire = true, Thrust = true } };

    [Fact]
    public void ArcadeAndWarpOffDriveInFromTheEdgeOnTheFirstPass()
    {
        foreach (var sim in new[] { Start(m: PlayMode.Arcade), Start(o: EnhancedOption.All & ~EnhancedOption.WarpIn) })
        {
            Assert.Equal(0, sim.Rules.WarpInPasses);
            sim.Step(default);
            var p = sim.State.Players[0];
            Assert.Equal(EnemyState.PlayerAlive, p.A);
            Assert.InRange(p.Speed, GameData.RespawnSpeed - 1, GameData.RespawnSpeed); // coasting may already take 1 off in the first pass
            Assert.Equal(0, sim.State.WarpTimer[0]);
            Assert.Equal(GameData.PlayerStarts[0].Heading, p.Heading);
        }
    }

    [Fact]
    public void WarpInTakesWarpInPassesAndMaterializesStillAtTheWarpPoint()
    {
        var sim = Start(onePlayer: false);
        int n = sim.Rules.WarpInPasses;
        Assert.Equal(30, n);
        for (int i = 1; i <= n; i++)
        {
            sim.Step(Busy); // fire, thrust and turn held the whole time
            for (int p = 0; p < 2; p++)
            {
                Assert.True(EnemyState.IsFree(sim.State.Players[p].A), $"P{p + 1} materialized early at pass {i}");
                Assert.Equal(n + 1 - i, sim.State.WarpTimer[p]);
            }
        }
        Assert.Equal(0, sim.State.ShotsFired[0] + sim.State.ShotsFired[1]); // no fire while warping (also with fresh presses below)
        sim.Step(Busy);
        for (int p = 0; p < 2; p++)
        {
            var r = sim.State.Players[p];
            Assert.Equal(EnemyState.PlayerAlive, r.A);
            Assert.Equal(0, sim.State.WarpTimer[p]);
            Assert.Equal((GameData.WarpInPoints[p].X, GameData.WarpInPoints[p].Y, GameData.WarpInPoints[p].Heading), (sim.State.WarpX[p], sim.State.WarpY[p], sim.State.WarpHeading[p]));
            // materialized this pass at the warp point with speed 0; the player pass of the same pass then applies
            // input (thrust only counts every 4th pass, so x/y move by at most one step)
            Assert.InRange(r.X, GameData.WarpInPoints[p].X - 1, GameData.WarpInPoints[p].X + 1);
            Assert.InRange(r.Speed, 0, 1);
        }
        sim.Step(default); sim.Step(Busy); // fire is edge-triggered (arcade): release, then press
        Assert.True(sim.State.ShotsFired[0] > 0, "can fire once materialized");
    }

    [Fact]
    public void FirePressesDuringTheWarpDoNothing()
    {
        var sim = Start();
        for (int i = 0; i < sim.Rules.WarpInPasses; i++) sim.Step(new FrameInput { P1 = new PlayerInput { Fire = i % 2 == 0 } });
        Assert.Equal(0, sim.State.ShotsFired[0]);
        Assert.All(sim.State.Shots, sh => Assert.False(sh.Live));
    }

    [Fact]
    public void AnEnemyOnTheWarpPointNeitherCollidesNorShootsWhileWarping()
    {
        var sim = Start();
        sim.Step(default); // warp starts
        var s = sim.State;
        var e = s.Enemies[0];
        var w = GameData.WarpInPoints[0];
        for (int i = 0; i < sim.Rules.WarpInPasses - 1; i++)
        {
            // an enemy parked right on the warp point, chasing (aim bits clear), in its fire window
            e.A = EnemyState.Chase; e.X = w.X; e.Y = w.Y; e.C = w.X; e.D = w.Y; e.Speed = e.Word3 = 2; e.Type = 1; e.Scale = GameData.EnemyScale;
            sim.Step(default);
            Assert.True(EnemyState.IsAlive(e.A), "the warping player rammed the enemy");
            Assert.True(EnemyState.IsFree(s.Players[0].A), "the warping player was hit");
        }
        Assert.Equal(0, s.EnemyFireHits);
        Assert.Equal(0, s.EnemyFireEvents); // no fire test even finds a live victim
    }

    [Fact]
    public void RespawnAfterDeathWaitsTheDelayThenWarpsIn()
    {
        int Passes(Simulation sim)
        {
            for (int i = 0; i < 200 && sim.State.Players[0].A != EnemyState.PlayerAlive; i++) sim.Step(default);
            var p = sim.State.Players[0];
            p.A &= 0xF; p.Scale = GameData.ExplosionRam; p.C = 2; // killed by enemy fire, as FireTest does
            int n = 0;
            while (p.A != EnemyState.PlayerAlive && n < 500) { sim.Step(default); n++; }
            return n;
        }
        int arcade = Passes(Start(m: PlayMode.Arcade));
        int warp = Passes(Start());
        int off = Passes(Start(o: EnhancedOption.All & ~EnhancedOption.WarpIn));
        Assert.Equal(arcade, off);
        Assert.Equal(arcade + GameRules.Enhanced.WarpInPasses, warp);
    }

    [Fact]
    public void OnlyP1WarpsInAOnePlayerGame()
    {
        var sim = Start();
        sim.Step(default);
        Assert.True(sim.State.WarpTimer[0] > 0);
        Assert.Equal(0, sim.State.WarpTimer[1]);
    }

    [Fact]
    public void TheEffectSpiralsInAndTheShipFadesIn()
    {
        var sim = Start();
        sim.Step(default);
        var w = GameData.WarpInPoints[0];
        List<Line> Lines() => Scene.Build(sim.State);
        float MaxR(IEnumerable<Line> ls) => ls.Max(l => Math.Max(Math.Max(Math.Abs(l.X1 - w.X), Math.Abs(l.Y1 - w.Y)), Math.Max(Math.Abs(l.X2 - w.X), Math.Abs(l.Y2 - w.Y))));
        var early = Lines().Where(l => Math.Abs(l.X1 - w.X) < 200 && Math.Abs(l.Y1 - w.Y) < 200).ToList();
        Assert.Equal(12 + 16, early.Count); // spokes + ring, no ship yet
        float r0 = MaxR(early);
        for (int i = 0; i < 20; i++) sim.Step(default);
        var late = Lines().Where(l => Math.Abs(l.X1 - w.X) < 200 && Math.Abs(l.Y1 - w.Y) < 200).ToList();
        Assert.True(MaxR(late) < r0, "the effect contracts");
        Assert.Contains(late, l => !l.Bright && Math.Abs(l.X1 - w.X) < 40); // dim ship outline growing in
        Assert.Equal(12, late.Count(l => l.Bright));
    }

    /// <summary>Core state hash (Enhanced-only fields left out) of scripted Enhanced games, recorded on main at bcd902b
    /// (2026.10.17.2's Enhanced, before warp-in and the options) with ZGoldenDump-style code.</summary>
    static readonly Dictionary<int, uint[]> EnhancedBeforeWarpIn = new()
    {
        [1] = new uint[] { 0x67FAAAFBu, 0xCFAE6E19u, 0xC6A03C1Au, 0xC51AE5D2u, 0x9FFB2628u, 0x5BBE88DEu, 0xD3C10649u, 0x7A1FD05Cu, 0x7D98A727u, 0x61626CDBu, 0x7079C9DFu, 0xB9D4BD47u },
        [2] = new uint[] { 0x69EAF56Eu, 0x6EC7D4B3u, 0xFF646521u, 0x7FDC1D0Eu, 0x7F9C95F3u, 0xF2AE45A5u, 0x3F4A556Au, 0xDB9152F3u, 0x544E9F79u, 0x3CDB87DDu, 0x43A288E7u, 0x30983365u },
        [3] = new uint[] { 0x492A579Fu, 0x108DB17Eu, 0xF68B0846u, 0xB6BE3314u, 0x779B8B25u, 0xD15C9C67u, 0x971239D0u, 0xF3C9FF20u, 0x80859DDFu, 0xFA5F6636u, 0x5B985578u, 0xE94CB1DAu },
        [4] = new uint[] { 0x3BAC1B05u, 0x6604269Au, 0x25E9EA30u, 0x73D457D6u, 0x8E24DE93u, 0x9A1B156Fu, 0x5846A50Bu, 0x3FA4C4A7u, 0x84C1FF9Eu, 0xC26C4F3Bu, 0x60F03AFFu, 0x281C23C4u },
        [5] = new uint[] { 0x40433494u, 0x891D359Eu, 0x841FD863u, 0xA51AAE36u, 0x5C3B28ECu, 0x0D125820u, 0x142BA3ADu, 0x3620E736u, 0xECA058BCu, 0x7C9ED577u, 0x4B827FA2u, 0x72308888u },
    };

    static uint[] CoreRun(int seed, bool onePlayer, GameConfig cfg)
    {
        var sim = ArcadeDeterminismTests.Create(seed, onePlayer, cfg);
        uint r = (uint)seed * 747796405u + 2891336453u;
        uint Rand() { r ^= r << 13; r ^= r >> 17; r ^= r << 5; return r; }
        var held = new byte[2]; var left = new int[2];
        var hs = new List<uint>();
        for (int f = 1; f <= ArcadeDeterminismTests.Frames; f++)
        {
            for (int p = 0; p < 2; p++) if (--left[p] <= 0) { held[p] = (byte)(Rand() & 0xF); left[p] = 1 + (int)(Rand() % 40); }
            PlayerInput In(byte b) => new() { Left = (b & 1) != 0, Right = (b & 2) != 0 && (b & 1) == 0, Thrust = (b & 4) != 0, Fire = (b & 8) != 0 && (f & 1) == 0 };
            sim.Step(new FrameInput { P1 = In(held[0]), P2 = In(held[1]) });
            if (f % ArcadeDeterminismTests.Every == 0)
            {
                var s = sim.State; var m = s.PlayMode; s.PlayMode = PlayMode.Arcade; // hash without the Enhanced-only fields
                hs.Add(StateHash.Compute(s)); s.PlayMode = m;
            }
        }
        return hs.ToArray();
    }

    [Theory]
    [MemberData(nameof(ArcadeDeterminismTests.Cases), MemberType = typeof(ArcadeDeterminismTests))]
    public void WarpInOffIsTheEnhancedOfBeforeWarpIn(int seed, bool onePlayer, int canisters, bool autoFire)
    {
        // the options of 2026.10.17.2: the three tunings, no warp-in and none of the later toggles
        var off = new GameConfig(canisters, autoFire, PlayMode.Enhanced, EnhancedOption.EnemyAimError | EnhancedOption.SlowerRamp | EnhancedOption.SlowerTowing);
        Assert.Equal(EnhancedBeforeWarpIn[seed], CoreRun(seed, onePlayer, off));
        // later toggles that change no simulation state (all but the enemy kinds) still reproduce it
        Assert.Equal(EnhancedBeforeWarpIn[seed], CoreRun(seed, onePlayer, off with { Options = EnhancedOption.All & ~EnhancedOption.WarpIn & ~EnhancedOption.TankEnemies & ~EnhancedOption.HunterEnemies }));
        Assert.NotEqual(EnhancedBeforeWarpIn[seed], CoreRun(seed, onePlayer, off with { Options = EnhancedOption.All }));
    }

    [Theory]
    [MemberData(nameof(ArcadeDeterminismTests.Cases), MemberType = typeof(ArcadeDeterminismTests))]
    public void AllOptionsOffPlaysLikeArcade(int seed, bool onePlayer, int canisters, bool autoFire)
    {
        var arcade = CoreRun(seed, onePlayer, new GameConfig(canisters, autoFire));
        Assert.Equal(arcade, CoreRun(seed, onePlayer, new GameConfig(canisters, autoFire, PlayMode.Enhanced, EnhancedOption.None)));
        Assert.Equal(arcade, ArcadeDeterminismTests.Run(seed, onePlayer, new GameConfig(canisters, autoFire))); // core hash = full hash in Arcade
    }

    [Fact]
    public void EnhancedWithWarpInIsDeterministic()
    {
        var cfg = new GameConfig(8, false, PlayMode.Enhanced);
        Assert.Equal(ArcadeDeterminismTests.Run(2, false, cfg), ArcadeDeterminismTests.Run(2, false, cfg));
        Assert.NotEqual(ArcadeDeterminismTests.Run(2, false, cfg), ArcadeDeterminismTests.Run(2, false, cfg with { Options = EnhancedOption.All & ~EnhancedOption.WarpIn }));
    }
}
