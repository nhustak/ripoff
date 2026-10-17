using RipOff.Core;

namespace RipOff.Core.Tests;

/// <summary>Enhanced RANDOM WARP-IN and WARP KILLS (NOTES §29): spot rules over many games, picked at warp start,
/// determinism, the warp kill (score, wave count, canister drop, player survives), and the toggles off reproducing
/// 2026.10.17.3/.4.</summary>
public class WarpRandomTests
{
    const EnhancedOption NoKills = EnhancedOption.All & ~EnhancedOption.WarpKills;

    static Simulation Start(bool onePlayer, EnhancedOption o = EnhancedOption.All, int seed = 0)
    {
        var sim = seed == 0 ? new Simulation() : ArcadeDeterminismTests.Create(seed, onePlayer, new GameConfig(8, false, PlayMode.Enhanced, o));
        if (seed == 0) sim.StartGame(onePlayer, new GameConfig(8, false, PlayMode.Enhanced, o));
        return sim;
    }

    static void AssertSpotRules(int x, int y, (int X, int Y)? other, string what)
    {
        int m = GameData.WarpMargin;
        Assert.True(x >= m && x <= GameData.FieldWidth - m && y >= m && y <= GameData.FieldHeight - m, $"{what}: ({x},{y}) inside the margin");
        var c = GameData.CanisterCluster; int k = GameData.WarpCanisterClearance;
        Assert.False(x >= c.MinX - k && x <= c.MaxX + k && y >= c.MinY - k && y <= c.MaxY + k, $"{what}: ({x},{y}) clear of the canisters");
        if (other is { } o)
            Assert.True(Math.Sqrt((double)(x - o.X) * (x - o.X) + (double)(y - o.Y) * (y - o.Y)) >= GameData.WarpPlayerClearance, $"{what}: ({x},{y}) far from the other player at {o}");
    }

    [Fact]
    public void EnhancedDefaultsAreRandomWithKillsAndArcadeHasNeither()
    {
        Assert.Equal(WarpInLocation.Random, GameRules.Enhanced.WarpLocation);
        Assert.True(GameRules.Enhanced.WarpKills);
        Assert.Equal(WarpInLocation.Fixed, GameRules.Arcade.WarpLocation);
        Assert.False(GameRules.Arcade.WarpKills);
        var fixedNoKill = GameRules.For(PlayMode.Enhanced, WarpInTests.FixedWarp);
        Assert.Equal((WarpInLocation.Fixed, false), (fixedNoKill.WarpLocation, fixedNoKill.WarpKills));
        Assert.Equal(30, fixedNoKill.WarpInPasses);
    }

    [Fact]
    public void ThousandsOfRandomSpotsFollowTheRules()
    {
        // Direct: every other-player position on a grid (and none), many RNG states.
        var s = new GameState { PlayMode = PlayMode.Enhanced };
        s.EnhancedRng = 12345;
        int n = 0;
        for (int ox = 0; ox <= 1024; ox += 64)
            for (int oy = 0; oy <= 768; oy += 64)
                for (int t = 0; t < 40; t++, n++)
                {
                    (int, int)? other = (t % 10 == 0) ? null : (ox, oy);
                    s.OnePlayer = other == null;
                    s.Players[1].A = EnemyState.PlayerAlive; s.Players[1].X = ox; s.Players[1].Y = oy;
                    var (x, y, h) = Simulation.RandomWarpPoint(s, 0);
                    AssertSpotRules(x, y, other, "direct");
                    Assert.InRange(h, 0, 360);
                }
        Assert.Equal(17 * 13 * 40, n);
    }

    [Fact]
    public void FallbacksFollowTheRulesAndOneIsAlwaysFarEnough()
    {
        foreach (var f in GameData.WarpFallbacks) AssertSpotRules(f.X, f.Y, null, "fallback");
        for (int ox = 0; ox <= 1024; ox += 8)
            for (int oy = 0; oy <= 768; oy += 8)
                Assert.Contains(GameData.WarpFallbacks, f => Simulation.WarpSpotOk(f.X, f.Y, (ox, oy)));
    }

    [Fact]
    public void InRealGamesEveryWarpSpotFollowsTheRulesAndIsPickedAtWarpStart()
    {
        int warps = 0, kills = 0;
        for (int seed = 1; seed <= 120; seed++)
        {
            bool one = seed % 4 == 0;
            var sim = Start(one, EnhancedOption.All, seed);
            var s = sim.State;
            uint r = (uint)seed * 2654435761u;
            uint Rand() { r ^= r << 13; r ^= r >> 17; r ^= r << 5; return r; }
            var spot = new (int X, int Y)?[2];
            for (int f = 0; f < 4000 && s.Mode == GameMode.Playing; f++)
            {
                var pre = new (int X, int Y)?[2];
                for (int q = 0; q < 2; q++) pre[q] = !EnemyState.IsFree(s.Players[q].A) ? (s.Players[q].X, s.Players[q].Y) : null;
                var preTimer = new[] { s.WarpTimer[0], s.WarpTimer[1] };
                PlayerInput In() { uint b = Rand(); return new() { Left = (b & 1) != 0, Right = (b & 6) == 2, Thrust = (b & 8) != 0, Fire = (b & 16) != 0 }; }
                sim.Step(new FrameInput { P1 = In(), P2 = In() });
                for (int p = 0; p < 2; p++)
                {
                    if (one && p == 1) continue;
                    if (s.WarpTimer[p] == sim.Rules.WarpInPasses && preTimer[p] == 0)
                    {
                        warps++;
                        int q = 1 - p;
                        // what the other player was when p picked: P1 picks first in the pass, so P2 sees P1's new warp.
                        (int X, int Y)? other = one ? null
                            : s.WarpTimer[q] > 0 && (preTimer[q] > 0 || q < p) ? (s.WarpX[q], s.WarpY[q]) : pre[q];
                        AssertSpotRules(s.WarpX[p], s.WarpY[p], other, $"seed {seed} frame {f} P{p + 1}");
                        spot[p] = (s.WarpX[p], s.WarpY[p]);
                    }
                    else if (s.WarpTimer[p] > 0) Assert.Equal(spot[p], (s.WarpX[p], s.WarpY[p])); // fixed for the whole warp
                    else if (preTimer[p] == 1 && EnemyState.IsAlive(s.Players[p].A))
                    {
                        // materialized this pass at the spot shown by the effect (then moved at most one pass)
                        Assert.True(Math.Abs(s.Players[p].X - spot[p]!.Value.X) <= 12 && Math.Abs(s.Players[p].Y - spot[p]!.Value.Y) <= 12);
                    }
                }
            }
            kills += s.WarpKills[0] + s.WarpKills[1];
        }
        Assert.True(warps > 300, $"{warps} warps");
        Console.WriteLine($"WARPSTATS warps={warps} warpKills={kills}");
        Assert.True(kills > 0, "warp kills happen in natural play");
    }

    [Fact]
    public void RandomWarpIsDeterministicAndDiffersFromFixed()
    {
        var cfg = new GameConfig(8, false, PlayMode.Enhanced);
        for (int seed = 1; seed <= 5; seed++)
        {
            Assert.Equal(ArcadeDeterminismTests.Run(seed, false, cfg), ArcadeDeterminismTests.Run(seed, false, cfg));
            Assert.NotEqual(ArcadeDeterminismTests.Run(seed, false, cfg), ArcadeDeterminismTests.Run(seed, false, cfg with { Options = WarpInTests.FixedWarp }));
        }
    }

    /// <summary>Full state hash (Enhanced fields included) of the scripted Enhanced games with options 0x0F (WARP-IN,
    /// ENEMY AIM ERROR, SLOWER RAMP, SLOWER TOWING), recorded on main at fdd05ee (2026.10.17.4).</summary>
    static readonly Dictionary<int, uint[]> EnhancedAt20261017_4 = new()
    {
        [1] = new uint[] { 0x5F47E0D0u, 0x9F60A667u, 0xB61D9463u, 0xA1D7E1CCu, 0x6F534628u, 0xDC20A992u, 0x8D67CFA5u, 0x4C5084A6u, 0xC7C5C852u, 0xC6F985C4u, 0x5B556AFEu, 0x4A8F0108u },
        [2] = new uint[] { 0x66D7D953u, 0xF43F5994u, 0xED06BAA2u, 0x64117F28u, 0xD7365A82u, 0xCAF1AF66u, 0x951565E7u, 0xDD98F526u, 0xAC762427u, 0xB1B7C94Du, 0xBFD62D9Fu, 0x9FEF3492u },
        [3] = new uint[] { 0x58B7D339u, 0xE7B72EBEu, 0x96798567u, 0x01672536u, 0x3144B6E5u, 0x81D6F6ABu, 0xF6E2B786u, 0xC6C0C01Au, 0xEE3BD796u, 0x4AFB24A6u, 0x554904F1u, 0x2C015EFBu },
        [4] = new uint[] { 0x6806FF63u, 0x1DA9C44Du, 0xF4A40913u, 0xBE666236u, 0xF90F931Bu, 0xA0CF65D1u, 0x8B2E5B63u, 0x98EDD098u, 0x8EECD9EAu, 0x5D954021u, 0xD519F192u, 0x1D731E40u },
        [5] = new uint[] { 0xBD6AC85Eu, 0x2F893C22u, 0xB9F0F565u, 0x7CF49AAFu, 0x44EFE3E2u, 0xA376B095u, 0xF405265Cu, 0x4F073230u, 0x1F50E4F7u, 0x9B4BE3F9u, 0x24F68074u, 0xE5EE2AD8u },
    };

    [Theory]
    [MemberData(nameof(ArcadeDeterminismTests.Cases), MemberType = typeof(ArcadeDeterminismTests))]
    public void BothNewTogglesOffReproduce2026_10_17_4(int seed, bool onePlayer, int canisters, bool autoFire)
    {
        var old = new GameConfig(canisters, autoFire, PlayMode.Enhanced, WarpInTests.FixedWarp);
        Assert.Equal((EnhancedOption)0x0F, WarpInTests.FixedWarp);
        Assert.Equal(EnhancedAt20261017_4[seed], ArcadeDeterminismTests.Run(seed, onePlayer, old));
        Assert.NotEqual(EnhancedAt20261017_4[seed], ArcadeDeterminismTests.Run(seed, onePlayer, old with { Options = EnhancedOption.All }));
    }

    // ------------------------------------------------------------------ warp kill

    /// <summary>A 1P Enhanced game with P1 warping in (from the first pass), stepped to <paramref name="left"/> passes before
    /// it materializes; enemy 0 is alive and put where asked.</summary>
    static Simulation WarpingWith(EnhancedOption o, int left, int dx, int dy, out TankRecord e)
    {
        var sim = Start(true, o);
        sim.Step(default);
        var s = sim.State;
        Assert.True(s.WarpTimer[0] > 0);
        while (s.WarpTimer[0] > left) sim.Step(default);
        e = s.Enemies[0];
        if (!EnemyState.IsAlive(e.A)) { e.A = EnemyState.Approach; e.Scale = GameData.EnemyScale; } // wave not in yet: bring one in
        Assert.True(EnemyState.IsAlive(e.A), "enemy 0 alive");
        e.X = s.WarpX[0] + dx; e.Y = s.WarpY[0] + dy; e.Speed = 0;
        return sim;
    }

    [Fact]
    public void MaterializingOnAnEnemyDestroysItScoresAndThePlayerSurvives()
    {
        var sim = WarpingWith(EnhancedOption.All, 1, 10, -6, out var e);
        var s = sim.State;
        int score = s.Score[0], kills = s.KillsThisWave, expect = Simulation.PointsTens(s.EnemyType, s.Level);
        sim.Step(default); // the pass it materializes in
        Assert.True(EnemyState.IsAlive(s.Players[0].A), "player materialized");
        Assert.True(EnemyState.IsExploding(e.A), "warp-killed enemy explodes");
        Assert.Equal(GameData.ExplosionRam - GameData.ExplosionStep, e.Scale); // ram explosion, first tick done in its next pass
        Assert.Equal(score + expect, s.Score[0]);
        Assert.Equal(kills + 1, s.KillsThisWave);
        Assert.Equal(1, s.WarpKills[0]);
        Assert.True(s.Sound.Explosions >= 1);
        for (int i = 0; i < 40; i++) { sim.Step(default); Assert.True(EnemyState.IsAlive(s.Players[0].A), "player survives"); }
    }

    [Fact]
    public void WarpKillsOffKeepsTheOldRamBothExplode()
    {
        var sim = WarpingWith(NoKills, 1, 10, -6, out var e);
        var s = sim.State; int score = s.Score[0];
        sim.Step(default);
        Assert.True(EnemyState.IsExploding(e.A));
        Assert.True(EnemyState.IsExploding(s.Players[0].A), "without WARP KILLS the overlap is a ram: the player dies too");
        Assert.Equal(score + Simulation.PointsTens(s.EnemyType, s.Level), s.Score[0]);
        Assert.Equal(0, s.WarpKills[0]);
    }

    [Fact]
    public void OnlyOverlappingEnemiesAndOnlyAtMaterialize()
    {
        // Just outside the contact box: spared.
        var sim = WarpingWith(EnhancedOption.All, 1, GameData.TankHitRadius + 40, 0, out var e);
        sim.Step(default);
        Assert.True(EnemyState.IsAlive(e.A));
        Assert.Equal(0, sim.State.WarpKills[0]);
        // On the spot mid-warp, gone before the end: spared (no kill zone during the warp).
        sim = WarpingWith(EnhancedOption.All, 15, 0, 0, out e);
        var s = sim.State;
        sim.Step(default);
        Assert.True(EnemyState.IsAlive(e.A), "no kill while warping");
        int farX = s.WarpX[0] + 300 > 1000 ? s.WarpX[0] - 300 : s.WarpX[0] + 300, farY = s.WarpY[0];
        while (s.WarpTimer[0] > 0) { e.X = farX; e.Y = farY; sim.Step(default); }
        Assert.True(EnemyState.IsAlive(e.A));
        Assert.Equal(0, s.WarpKills[0]);
    }

    [Fact]
    public void WarpKillingATowerDropsItsCanister()
    {
        var sim = WarpingWith(EnhancedOption.All, 1, 0, 8, out var e);
        var s = sim.State;
        int j = 3;
        e.A = EnemyState.Tow; e.Word2 = j;
        s.Canisters[j].X2 = ((e.X) << 1) | 1; s.Canisters[j].Y2 = (e.Y + 20) << 1;
        sim.Step(default);
        Assert.True(EnemyState.IsExploding(e.A));
        for (int i = 0; i < 30 && !EnemyState.IsFree(e.A); i++) sim.Step(default);
        Assert.True(EnemyState.IsFree(e.A));
        Assert.False(s.Canisters[j].Taken, "canister dropped");
        Assert.False(s.Canisters[j].Gone);
        Assert.Equal(1, s.WarpKills[0]);
    }

    [Fact]
    public void TheEffectIsDrawnAtTheRandomSpot()
    {
        var sim = Start(true);
        sim.Step(default);
        var s = sim.State;
        var w = (X: s.WarpX[0], Y: s.WarpY[0]);
        Assert.NotEqual((GameData.WarpInPoints[0].X, GameData.WarpInPoints[0].Y), w);
        var near = Scene.Build(s).Where(l => Math.Abs(l.X1 - w.X) < 200 && Math.Abs(l.Y1 - w.Y) < 200).ToList();
        Assert.Equal(12 + 16, near.Count); // spokes + ring around the random spot
        Assert.All(near, l => Assert.True(l.X1 >= 0 && l.X1 <= 1024 && l.Y1 >= 0 && l.Y1 <= 768 && l.X2 >= 0 && l.X2 <= 1024 && l.Y2 >= 0 && l.Y2 <= 768, "effect not clipped"));
    }
}

public class EnhancedSaltTests
{
    [Fact]
    public void SaltChangesEnhancedOnlyAndIsDeterministicPerSalt()
    {
        GameState Fresh(PlayMode m) { var sim = new Simulation(); sim.StartGame(true, new GameConfig(8, false, m)); return sim.State; }
        var a = Fresh(PlayMode.Arcade); a.SaltEnhancedRng(123);
        Assert.Equal(0u, a.EnhancedRng);
        var e1 = Fresh(PlayMode.Enhanced); var e2 = Fresh(PlayMode.Enhanced);
        uint before = e1.EnhancedRng;
        e1.SaltEnhancedRng(123); e2.SaltEnhancedRng(123);
        Assert.Equal(e1.EnhancedRng, e2.EnhancedRng);
        Assert.NotEqual(before, e1.EnhancedRng);
        var spots = new HashSet<(int, int)>();
        for (uint salt = 1; salt <= 50; salt++)
        {
            var s = Fresh(PlayMode.Enhanced); s.SaltEnhancedRng(salt);
            var (x, y, _) = Simulation.RandomWarpPoint(s, 0);
            spots.Add((x, y));
        }
        Assert.True(spots.Count > 40, $"{spots.Count} distinct first spots from 50 salts");
    }
}
