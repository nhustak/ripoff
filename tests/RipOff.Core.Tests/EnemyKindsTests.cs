using RipOff.Core;

namespace RipOff.Core.Tests;

/// <summary>TANK and HUNTER enemies (NOTES §32), Enhanced only.</summary>
public class EnemyKindsTests
{
    static Simulation Start(int type, int level = 1, EnhancedOption o = EnhancedOption.All, bool onePlayer = false, int s1 = 0, int s2 = 1, uint salt = 1)
    {
        var sim = new Simulation();
        sim.StartGame(onePlayer, new GameConfig(8, false, PlayMode.Enhanced, o, s1, s2));
        sim.State.SaltEnhancedRng(salt);
        sim.State.EnemyType = type; sim.State.Level = level;
        return sim;
    }

    static void StepToSpawn(Simulation sim)
    {
        for (int i = 0; i < 300; i++)
        {
            sim.Step(default);
            if (sim.State.Enemies.Any(e => !EnemyState.IsFree(e.A))) return;
        }
        Assert.Fail("no wave spawned");
    }

    [Fact]
    public void TogglesAndTuning()
    {
        Assert.Equal(256, (int)EnhancedOption.TankEnemies);
        Assert.Equal(512, (int)EnhancedOption.HunterEnemies);
        var e = GameRules.Enhanced; var a = GameRules.Arcade;
        Assert.Equal((25, 2, 70, 2), (e.TankChancePercent, e.TankFromType, e.TankSpeedPercent, e.TankHits));
        Assert.Equal((20, 3), (e.HunterChancePercent, e.HunterFromType));
        Assert.Equal((0, 0, 100, 1, 0, 0), (a.TankChancePercent, a.TankFromType, a.TankSpeedPercent, a.TankHits, a.HunterChancePercent, a.HunterFromType));
        Assert.Contains(GameRules.Variations, v => v.Option == EnhancedOption.TankEnemies && v.Label == "TANK ENEMIES");
        Assert.Contains(GameRules.Variations, v => v.Option == EnhancedOption.HunterEnemies && v.Label == "HUNTER ENEMIES");
    }

    [Fact]
    public void FirstTypeHasNoNewKindsAndDrawsNoRandomness()
    {
        for (uint salt = 1; salt <= 40; salt++)
        {
            var sim = Start(type: 1, o: EnhancedOption.All & ~EnhancedOption.WarpIn, salt: salt); // no random warp spot drawn
            uint rng = sim.State.EnhancedRng;
            StepToSpawn(sim);
            Assert.All(sim.State.EnemyKinds, k => Assert.Equal(EnemyKind.Normal, k));
            Assert.Equal(rng, sim.State.EnhancedRng); // no kind rolls (nothing aimed or fired yet)
        }
    }

    /// <summary>Spawns many waves of a type and counts the kinds.</summary>
    static (int Enemies, int Tanks, int Waves, int HunterWaves, int MaxHunters) Census(int type, EnhancedOption o = EnhancedOption.All, int level = 1)
    {
        int enemies = 0, tanks = 0, waves = 0, hunterWaves = 0, maxH = 0;
        for (uint salt = 1; salt <= 600; salt++)
        {
            var sim = Start(type, level, o, salt: salt);
            StepToSpawn(sim);
            var s = sim.State; waves++;
            int h = 0;
            for (int i = 0; i < 3; i++)
            {
                if (EnemyState.IsFree(s.Enemies[i].A)) { Assert.Equal(EnemyKind.Normal, s.EnemyKinds[i]); continue; }
                enemies++;
                if (s.EnemyKinds[i] == EnemyKind.Tank) tanks++;
                if (s.EnemyKinds[i] == EnemyKind.Hunter) h++;
            }
            if (h > 0) hunterWaves++;
            maxH = Math.Max(maxH, h);
        }
        return (enemies, tanks, waves, hunterWaves, maxH);
    }

    [Fact]
    public void MixMatchesTheTuning()
    {
        var t2 = Census(2);
        Assert.Equal(0, t2.HunterWaves);                         // hunters from type 3
        Assert.InRange(t2.Tanks * 100.0 / t2.Enemies, 20, 30);  // 25 %
        var t3 = Census(3);
        Assert.Equal(1, t3.MaxHunters);                          // at most one per wave
        Assert.InRange(t3.HunterWaves * 100.0 / t3.Waves, 15, 25); // 20 %
        Assert.InRange(t3.Tanks * 100.0 / t3.Enemies, 15, 30);  // 25 %, a few turned into the hunter
        var lvl2 = Census(1, level: 2);                          // after the first level every type has both
        Assert.True(lvl2.Tanks > 0 && lvl2.HunterWaves > 0);
        var off = Census(3, EnhancedOption.All & ~EnhancedOption.TankEnemies & ~EnhancedOption.HunterEnemies);
        Assert.Equal((0, 0), (off.Tanks, off.HunterWaves));
        var tanksOnly = Census(3, EnhancedOption.All & ~EnhancedOption.HunterEnemies);
        Assert.True(tanksOnly.Tanks > 0); Assert.Equal(0, tanksOnly.HunterWaves);
        var huntersOnly = Census(3, EnhancedOption.All & ~EnhancedOption.TankEnemies);
        Assert.Equal(0, huntersOnly.Tanks); Assert.True(huntersOnly.HunterWaves > 0);
    }

    /// <summary>A wave with enemy 0 forced to be a tank, parked in the open.</summary>
    static (Simulation Sim, TankRecord E) ParkedTank(int ship1 = Ships.Classic1, EnhancedOption o = EnhancedOption.All)
    {
        for (uint salt = 1; ; salt++)
        {
            var sim = Start(type: 3, o: o & ~EnhancedOption.WarpIn, onePlayer: true, s1: ship1, s2: ship1 == 1 ? 0 : 1, salt: salt);
            StepToSpawn(sim);
            var s = sim.State;
            if (s.EnemyKinds[0] != EnemyKind.Tank) continue;
            var e = s.Enemies[0];
            e.X = 300; e.Y = 600; e.Speed = 0; e.Word3 = 0;
            return (sim, e);
        }
    }

    static void ShootAt(Simulation sim, TankRecord e)
    {
        sim.State.Shots[0] = new Shot { Live = true, X = e.X - 5, Y = e.Y, Vx = 5, Vy = 0 };
        sim.Step(default);
    }

    [Fact]
    public void TankIsSlowerAndTakesTwoHits()
    {
        var (sim, e) = ParkedTank();
        var s = sim.State;
        Assert.Equal(2, s.EnemyHits[0]);
        var plates = Scene.Build(s).Count;
        int score = s.Score[0];
        ShootAt(sim, e);
        Assert.True(EnemyState.IsAlive(e.A));                    // armor took it
        Assert.Equal(1, s.EnemyHits[0]);
        Assert.Equal(1, s.TankArmorHits);
        Assert.False(s.Shots[0].Live);
        Assert.Equal(score, s.Score[0]);
        Assert.Equal(plates - Scene.TankPlates.Length, Scene.Build(s).Count); // plates gone
        ShootAt(sim, e);
        Assert.True(EnemyState.IsExploding(e.A));
        Assert.Equal(score + Simulation.PointsTens(s.EnemyType, s.Level), s.Score[0]); // normal points
    }

    [Fact]
    public void TankSpeedIsSeventyPercent()
    {
        for (uint salt = 1; salt < 200; salt++)
        {
            var sim = Start(type: 4, level: 1, salt: salt);
            StepToSpawn(sim);
            var s = sim.State;
            int normal = -1, tank = -1;
            for (int i = 0; i < 3; i++)
            {
                if (EnemyState.IsFree(s.Enemies[i].A)) continue;
                if (s.EnemyKinds[i] == EnemyKind.Tank) tank = s.Enemies[i].Word3; else normal = s.Enemies[i].Word3;
            }
            if (normal < 0 || tank < 0) continue;
            Assert.Equal(Math.Max(2, normal * 70 / 100), tank);
            return;
        }
        Assert.Fail("no mixed wave");
    }

    [Fact]
    public void CruiserShotsKillATankInOne()
    {
        var (sim, e) = ParkedTank(Ships.Cruiser);
        ShootAt(sim, e);
        Assert.True(EnemyState.IsExploding(e.A));
        Assert.Equal(0, sim.State.TankArmorHits);
        // with SHIP ABILITIES off the CRUISER's shots do 1 like any other
        (sim, e) = ParkedTank(Ships.Cruiser, EnhancedOption.All & ~EnhancedOption.ShipAbilities);
        ShootAt(sim, e);
        Assert.True(EnemyState.IsAlive(e.A));
    }

    [Fact]
    public void RammingATankDestroysItOutright()
    {
        var (sim, e) = ParkedTank();
        var p = sim.State.Players[0];
        for (int i = 0; i < 100 && !EnemyState.IsAlive(p.A); i++) sim.Step(default);
        Assert.True(EnemyState.IsAlive(p.A));
        e.X = p.X + 10; e.Y = p.Y;
        sim.Step(default);
        Assert.True(EnemyState.IsExploding(e.A));
        Assert.Equal(2, sim.State.EnemyHits[0]); // its armor never mattered
    }

    /// <summary>Scripted 2P games at a type with hunters: every hunter is followed pass by pass.</summary>
    [Fact]
    public void HuntersChaseLivePlayersAndNeverTakeCanisters()
    {
        int hunterPasses = 0, chasing = 0, waits = 0, hunterKills = 0;
        for (uint salt = 1; salt <= 30; salt++)
        {
            var sim = Start(type: 3, salt: salt);
            var s = sim.State;
            uint r = salt * 747796405u + 2891336453u;
            uint Rand() { r ^= r << 13; r ^= r >> 17; r ^= r << 5; return r; }
            int held = 0, left = 0;
            for (int f = 0; f < 4000; f++)
            {
                if (--left <= 0) { held = (int)(Rand() & 0xF); left = 1 + (int)(Rand() % 40); }
                var i = new PlayerInput { Left = (held & 1) != 0, Right = (held & 3) == 2, Thrust = (held & 4) != 0, Fire = (held & 8) != 0 && f % 2 == 0 };
                bool[] wasHunter = s.EnemyKinds.Select(k => k == EnemyKind.Hunter).ToArray();
                bool[] wasAlive = s.Enemies.Select(e => EnemyState.IsAlive(e.A)).ToArray();
                sim.Step(new FrameInput { P1 = i, P2 = i });
                for (int k = 0; k < 3; k++)
                {
                    var e = s.Enemies[k];
                    if (s.EnemyKinds[k] != EnemyKind.Hunter) continue;
                    if (wasHunter[k] && wasAlive[k] && EnemyState.IsExploding(e.A)) hunterKills++;
                    if (!EnemyState.IsAlive(e.A)) continue;
                    hunterPasses++;
                    Assert.Equal(0, e.A & EnemyState.BitTow);     // never towing
                    Assert.NotEqual(EnemyState.AtCanister, e.A);  // never at a canister
                    Assert.Equal(1, s.EnemyHits[k]);
                    bool anyone = EnemyState.IsAlive(s.Players[0].A) || EnemyState.IsAlive(s.Players[1].A);
                    if (anyone && e.A == EnemyState.Chase) chasing++;
                    if (!anyone) { Assert.NotEqual(EnemyState.Chase, e.A); waits++; }
                }
            }
        }
        Assert.True(hunterPasses > 1000, $"{hunterPasses} hunter passes");
        Assert.True(chasing > hunterPasses / 2, $"chasing {chasing} of {hunterPasses}");
        Assert.True(waits > 0);
        Assert.True(hunterKills > 0);
    }

    [Fact]
    public void HunterGoesForTheNearestLivePlayer()
    {
        for (uint salt = 1; salt < 400; salt++)
        {
            var sim = Start(type: 3, o: EnhancedOption.All & ~EnhancedOption.WarpIn & ~EnhancedOption.TankEnemies, salt: salt);
            StepToSpawn(sim);
            var s = sim.State;
            int h = Array.IndexOf(s.EnemyKinds, EnemyKind.Hunter);
            if (h < 0) continue;
            for (int i = 0; i < 80 && !(EnemyState.IsAlive(s.Players[0].A) && EnemyState.IsAlive(s.Players[1].A)); i++) sim.Step(default);
            var e = s.Enemies[h];
            for (int k = 0; k < 3; k++) if (k != h) s.Enemies[k].A = EnemyState.Free;
            e.X = 500; e.Y = 300;
            s.Players[0].X = 100; s.Players[0].Y = 100;   // 400 away
            s.Players[1].X = 700; s.Players[1].Y = 400;   // 200 away
            sim.Step(default);
            Assert.Equal(EnemyState.Chase, e.A);
            Assert.Equal((s.Players[1].X, s.Players[1].Y), (e.C, e.D));
            return;
        }
        Assert.Fail("no hunter wave");
    }

    [Fact]
    public void KindsAreHashedOnlyWhenOn()
    {
        var on = new GameConfig(8, false, PlayMode.Enhanced, EnhancedOption.All);
        Assert.NotEqual(ArcadeDeterminismTests.Run(3, false, on), ArcadeDeterminismTests.Run(3, false, on with { Options = Enhanced20261017_7Tests.Release7 }));
        // Arcade never rolls a kind
        var sim = new Simulation(); sim.StartGame(false, new GameConfig(8)); sim.State.EnemyType = 3;
        StepToSpawn(sim);
        Assert.All(sim.State.EnemyKinds, k => Assert.Equal(EnemyKind.Normal, k));
    }

    [Fact]
    public void SceneMarksTanksAndHunters()
    {
        var (sim, e) = ParkedTank();
        var s = sim.State;
        var lines = Scene.Build(s);
        var plates = new List<Line>();
        double a = e.Heading * Math.PI / 180.0, k = e.Scale / 128.0, c = Math.Cos(a) * k, sn = Math.Sin(a) * k;
        foreach (var g in Scene.TankPlates)
            plates.Add(new Line((float)(e.X + c * g.X1 - sn * g.Y1), (float)(e.Y + sn * g.X1 + c * g.Y1), (float)(e.X + c * g.X2 - sn * g.Y2), (float)(e.Y + sn * g.X2 + c * g.Y2), !g.Dim));
        Assert.All(plates, l => Assert.Contains(l, lines));
        s.EnemyKinds[0] = EnemyKind.Hunter; s.EnemyHits[0] = 1;
        lines = Scene.Build(s);
        Assert.DoesNotContain(plates[0], lines);
        int n = lines.Count;
        s.EnemyKinds[0] = EnemyKind.Normal;
        Assert.Equal(n - Scene.HunterMark.Length, Scene.Build(s).Count);
    }
}
