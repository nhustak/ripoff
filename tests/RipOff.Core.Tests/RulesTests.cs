using RipOff.Core;

namespace RipOff.Core.Tests;

/// <summary>Unit checks of the individual rules in disasm/GAME_RULES.md.</summary>
public class RulesTests
{
    [Theory] // GR §5: v = type + ⌊type/2⌋ + level + 1 (lst:666-674)
    [InlineData(1, 1, 3)] [InlineData(2, 1, 5)] [InlineData(3, 1, 6)] [InlineData(4, 1, 8)]
    [InlineData(5, 1, 9)] [InlineData(6, 1, 11)] [InlineData(1, 2, 4)] [InlineData(6, 3, 13)]
    public void EnemySpeedFormula(int type, int level, int v) => Assert.Equal(v, Simulation.EnemyMaxSpeed(type, level));

    [Theory] // GR §5: turn step ⌊v/2⌋ + 1 (lst:675-677)
    [InlineData(3, 2)] [InlineData(5, 3)] [InlineData(6, 4)] [InlineData(11, 6)]
    public void EnemyTurnStep(int v, int step) => Assert.Equal(step, Simulation.EnemyTurnStep(v));

    [Theory] // GR §8: points = 10·(type + level − 1) (lst:3550-3573, lst:2433-2455)
    [InlineData(1, 1, 1)] [InlineData(6, 1, 6)] [InlineData(1, 2, 2)] [InlineData(6, 3, 8)]
    public void Scoring(int type, int level, int tens) => Assert.Equal(tens, Simulation.PointsTens(type, level));

    [Fact] // NOTES §9.3: 11-bit LFSR, period 2047 from the documented seed $387 (lst:59-61)
    public void RngPeriodIs2047()
    {
        int x = Rng.SeedRecord0, n = 0; // $387 has bit 11 clear, so it lies on the 2047-cycle
        do { x = Rng.Step(x); n++; } while (x != Rng.SeedRecord0 && n < 5000);
        Assert.Equal(2047, n);
        foreach (int seed in new[] { Rng.SeedRecord1, Rng.SeedOthers })
        {
            // $F20 has bit 11 set and so is not on the cycle itself; one step lands on it.
            int y0 = Rng.Step(seed), y = y0, m = 0;
            do { y = Rng.Step(y); m++; } while (y != y0 && m < 5000);
            Assert.Equal(2047, m);
        }
    }

    [Fact] // GR §7: box tests are inclusive; chase starts at exactly 256 (lst:2519-2545)
    public void ChaseThresholdInclusive()
    {
        Assert.True(Simulation.Box(256, -256, GameData.ChaseRadius));
        Assert.False(Simulation.Box(257, 0, GameData.ChaseRadius));
        Assert.True(Simulation.Box(-20, 20, GameData.ShotHitRadius));
        Assert.False(Simulation.Box(0, 21, GameData.ShotHitRadius));
        Assert.True(Simulation.Box(24, 24, GameData.TankHitRadius));
    }

    [Fact] // NOTES §9.1: table = min(255, round(256 sin k)) with exceptions 38 → 159 and 70 → 240
    public void SineTable()
    {
        Assert.Equal(91, Trig.SinTable.Length);
        Assert.Equal(0, Trig.SinTable[0]);
        Assert.Equal(255, Trig.SinTable[90]);
        Assert.Equal(128, Trig.SinTable[30]);
        Assert.Equal(159, Trig.SinTable[38]);
        Assert.Equal(240, Trig.SinTable[70]);
        for (int k = 1; k <= 90; k++) Assert.True(Trig.SinTable[k] >= Trig.SinTable[k - 1]);
    }

    [Fact] // NOTES §20 (f): atan2 port, (0,0) → 315; the four axis bearings
    public void Bearing()
    {
        Assert.Equal(315, Trig.Bearing(0, 0));
        Assert.Equal(45, Trig.AtanTable[0][0]);
        Assert.Equal(85, Trig.AtanTable[10][1]);
        Assert.Equal(55, Trig.AtanTable[31][23]);
        // Heading h moves (sin h, −cos h) (NOTES §11.1); the bearing of a displacement must be the heading.
        foreach (int h in new[] { 10, 45, 100, 200, 300 })
        {
            var (dx, dy, _) = Trig.Displacement(h, 100);
            Assert.InRange(Math.Abs(Trig.Bearing(dx, dy) - h) % 360, 0, 2);
        }
    }

    [Fact] // GR §7: player respawns 25 passes after its record is freed, P1 at (1024,320) angle 270 (lst:862-932)
    public void RespawnTimingAndPlace()
    {
        var sim = new Simulation(); var st = sim.State;
        st.Credits = 1; sim.StartGame(true);
        sim.Step(default); // game-start pass: word 3 = $19 → P1 respawns at once (NOTES §20 (k))
        var p = st.Players[0];
        Assert.Equal(EnemyState.PlayerAlive, p.A);
        // Placed at (1024, 320) angle 270 with speed 5, then moved in the same pass (idle1 f202: x = $3FB).
        Assert.Equal((320, 270), (p.Y, p.Heading));
        Assert.InRange(p.X, 1024 - 5, 1023);
        Assert.InRange(p.Speed, 4, 5);

        // Kill it as a ram would: explosion $300, −$20 per pass, then free, then 25 passes.
        p.A &= 0xF; p.Scale = GameData.ExplosionRam;
        int total = 0;
        while (!EnemyState.IsAlive(p.A) && total < 200) { sim.Step(default); total++; }
        // 24 explosion passes ($300 → 0 by $20) + 25 free passes (GR §7 "Player respawn": "23 + 25" in frame-end
        // logs, where the first explosion pass falls in the kill pass).
        Assert.Equal(24 + 25, total);
    }

    [Fact] // GR §1: 8 canisters with the default DIP, centre cluster positions; slots past N unused (1,1)
    public void CanisterTableAtStart()
    {
        var sim = new Simulation(); var st = sim.State;
        sim.StartGame(true);
        int n = GameData.CanistersForDip[st.CanisterDip];
        Assert.Equal(8, n);
        for (int j = 0; j < n; j++)
        {
            Assert.False(st.Canisters[j].Gone);
            Assert.Equal(GameData.CanisterStarts[j], (st.Canisters[j].X, st.Canisters[j].Y));
        }
        for (int j = n; j < 16; j++) Assert.True(st.Canisters[j].Gone);
    }

    [Fact] // GR §4/§5: first wave 50 passes after start; all three enemies at one spawn point; 1P level-1 handicap
    public void FirstWaveSpawn()
    {
        var sim = new Simulation(); var st = sim.State;
        st.Credits = 1; sim.StartGame(true);
        int passes = 0;
        while (st.Enemies.All(e => EnemyState.IsFree(e.A)) && passes < 500) { sim.Step(default); passes++; }
        Assert.Equal(51, passes); // f202 start → f252 spawn (NOTES §20 (k)): 50 passes after the start pass
        var e0 = st.Enemies[0];
        // Placed on a spawn point, then moved/turned in the same pass.
        Assert.Contains(GameData.SpawnPoints, sp => Math.Abs(sp.X - e0.X) <= e0.Speed && Math.Abs(sp.Y - e0.Y) <= e0.Speed);
        Assert.Equal(EnemyState.Approach, e0.A);
        Assert.Equal(EnemyState.Approach, st.Enemies[1].A);
        Assert.Equal(EnemyState.Free, st.Enemies[2].A); // one-player handicap at level 1 (lst:843-861)
        Assert.Equal(Simulation.EnemyMaxSpeed(1, 1), e0.Speed);
    }

    [Fact] // GR §8: coin → credit, start consumes it; two-player start needs two
    public void CreditsAndStart()
    {
        var sim = new Simulation(); var st = sim.State;
        sim.Step(new FrameInput { Start1 = true });
        Assert.Equal(GameMode.Attract, st.Mode);
        sim.Step(new FrameInput { Coin = true });
        sim.Step(new FrameInput { Start2 = true });
        Assert.Equal(GameMode.Attract, st.Mode);
        sim.Step(new FrameInput { Start1 = true });
        Assert.Equal(GameMode.Playing, st.Mode);
        Assert.True(st.OnePlayer);
        Assert.Equal(0, st.Credits);
    }

    [Fact] // Determinism: two runs with the same inputs give identical state
    public void Deterministic()
    {
        static string Run()
        {
            var sim = new Simulation(); var st = sim.State; st.Credits = 2; sim.StartGame(false);
            for (int f = 0; f < 3000; f++)
                sim.Step(new FrameInput { P1 = new PlayerInput { Left = f % 50 < 20, Thrust = f % 7 < 5, Fire = f % 9 == 0 },
                                          P2 = new PlayerInput { Right = f % 30 < 10, Thrust = true, Fire = f % 13 == 0 } });
            return string.Join(",", Enumerable.Range(0, 5).Select(i => { var r = st.Record(i); return $"{r.A}:{r.X}:{r.Y}:{r.Heading}:{r.B}"; }))
                   + $"|{st.Score[0]}|{st.Score[1]}|{st.WaveCounter}";
        }
        Assert.Equal(Run(), Run());
    }

    [Fact] // NOTES §13: units ≥ 1000 tens carry into the thousands word (lst:1046-1080)
    public void ScoreCarry()
    {
        var sim = new Simulation(); var st = sim.State;
        st.Credits = 1; sim.StartGame(true);
        st.Score[0] = 1005;
        sim.Step(default);
        Assert.Equal((5, 1), (st.Score[0], st.ScoreThousands[0]));
    }

    [Theory] // GR §6 "Enemy fire": window base $10·((A8 >> 2) & 3): record $00 in passes 0-3 mod 16, $10 in 4-7, $20 in 8-11
    [InlineData(0, 0x00)] [InlineData(3, 0x00)] [InlineData(4, 0x10)] [InlineData(9, 0x20)] [InlineData(13, 0x30)] [InlineData(16, 0x00)]
    public void FireWindow(int a8, int expectedBase)
    {
        var sim = new Simulation(); sim.State.PassCounter = a8;
        Assert.Equal(expectedBase, sim.FireWindowBase);
    }

    [Fact] // NOTES §20 (d): the kill test is strict |d| < 150 ($96)
    public void FireReachIsStrict() => Assert.Equal(150, GameData.FireReach);
}
