using RipOff.Core;

namespace RipOff.Core.Tests;

/// <summary>Enhanced-mode knobs (GameRules, NOTES §27) take effect; Arcade keeps the arcade values. Arcade play itself is
/// pinned by ArcadeDeterminismTests and EscalationTests.</summary>
public class EnhancedRulesTests
{
    [Fact]
    public void ArcadeKnobsAreTheArcade()
    {
        var a = GameRules.Arcade;
        Assert.Equal(0, a.EnemyAimErrorDeg); Assert.Equal(0, a.AimErrorAt(1)); Assert.Equal(0, a.EnemyMissCooldown);
        Assert.Equal(0, a.ChaseReactionPasses); Assert.Equal(100, a.ChaseTurnPercent);
        Assert.Equal(1, a.ClearsPerTypeStep); Assert.Equal(2, a.FullWaveFromLevel); Assert.Equal(0, a.EnemySpeedCapBase);
        Assert.Equal(100, a.TowSpeedPercent);
    }

    [Fact]
    public void EnhancedKnobs()
    {
        var e = GameRules.Enhanced;
        Assert.Equal(PlayMode.Enhanced, e.Mode);
        Assert.Equal(20, e.AimErrorAt(1)); Assert.Equal(18, e.AimErrorAt(2)); Assert.Equal(10, e.AimErrorAt(6)); Assert.Equal(10, e.AimErrorAt(40));
        Assert.Equal(12, e.EnemyHitRadius); Assert.Equal(32, e.EnemyMissCooldown); Assert.Equal(19, e.ChaseReactionPasses);
        Assert.Equal(2, e.ClearsPerTypeStep); Assert.Equal(3, e.FullWaveFromLevel); Assert.Equal(8, e.EnemySpeedCapBase);
        Assert.Equal(75, e.TowSpeedPercent);
        // knobs Enhanced does not change stay at the arcade values
        Assert.Equal(GameRules.Arcade.AutoFireInterval, e.AutoFireInterval);
        Assert.Equal(GameRules.Arcade.RespawnDelay, e.RespawnDelay);
    }

    static Simulation Start(PlayMode m, int type = 1, int level = 1)
    {
        var sim = new Simulation();
        sim.StartGame(true, new GameConfig(8, false, m));
        sim.State.EnemyType = type; sim.State.Level = level;
        return sim;
    }

    static void StepToSpawn(Simulation sim)
    {
        for (int i = 0; i < 200; i++)
        {
            sim.Step(default);
            if (sim.State.Enemies.Any(e => !EnemyState.IsFree(e.A))) return;
        }
        Assert.Fail("no wave spawned");
    }

    [Theory]
    [InlineData(PlayMode.Arcade, 12, 3)]   // type 6 level 2: v = 6 + 3 + 2 + 1, full wave
    [InlineData(PlayMode.Enhanced, 10, 2)] // capped at 8 + level, one-player handicap still on at level 2
    public void SpawnSpeedAndWaveSize(PlayMode m, int speed, int enemies)
    {
        var sim = Start(m, type: 6, level: 2);
        StepToSpawn(sim);
        var live = sim.State.Enemies.Where(e => !EnemyState.IsFree(e.A)).ToList();
        Assert.Equal(enemies, live.Count);
        Assert.All(live, e => Assert.Equal(speed, e.Word3));
        Assert.All(live, e => Assert.Equal(Simulation.EnemyTurnStep(speed), e.TurnStep));
    }

    [Fact]
    public void EnhancedFullWaveFromLevel3()
    {
        var sim = Start(PlayMode.Enhanced, type: 1, level: 3);
        StepToSpawn(sim);
        Assert.Equal(3, sim.State.Enemies.Count(e => !EnemyState.IsFree(e.A)));
    }

    /// <summary>Clears every wave instantly (as if all enemies were shot): Arcade steps the type each clear, Enhanced
    /// every second clear.</summary>
    [Theory]
    [InlineData(PlayMode.Arcade, new[] { 2, 3, 4, 5 })]
    [InlineData(PlayMode.Enhanced, new[] { 1, 2, 2, 3 })]
    public void TypeStepsPerClearedWave(PlayMode m, int[] typesAfterEachClear)
    {
        var sim = Start(m); var s = sim.State;
        foreach (int want in typesAfterEachClear)
        {
            StepToSpawn(sim);
            foreach (var e in s.Enemies) e.A = EnemyState.Free;
            s.KillsThisWave = 3; s.TheftsThisWave = 0;
            sim.Step(default);                                // wave end bookkeeping
            Assert.Equal(want, s.EnemyType);
        }
    }

    /// <summary>A still player (no input) is hunted by chasers: in Arcade every fire test in reach kills, in Enhanced
    /// some miss, and the game still has kills (not a pacifist mode).</summary>
    [Fact]
    public void EnhancedEnemiesMissSometimes()
    {
        int ev = 0, hits = 0;
        foreach (var (m, check) in new (PlayMode, bool)[] { (PlayMode.Arcade, true), (PlayMode.Enhanced, false) })
        {
            ev = hits = 0;
            for (int seed = 1; seed <= 12; seed++)
            {
                var sim = ArcadeDeterminismTests.Create(seed, true, new GameConfig(8, false, m));
                for (int f = 0; f < 6000 && sim.State.Mode == GameMode.Playing; f++)
                    sim.Step(new FrameInput { P1 = new PlayerInput { Left = f % 200 < 30, Thrust = f % 300 < 20 } });
                ev += sim.State.EnemyFireEvents; hits += sim.State.EnemyFireHits;
            }
            Assert.True(ev > 10, $"{m}: {ev} fire events");
            if (check) Assert.Equal(ev, hits);
            else { Assert.True(hits < ev * 0.8, $"Enhanced {hits}/{ev}"); Assert.True(hits > 0, "Enhanced never hits"); }
        }
    }

    [Fact]
    public void EnhancedRngIsSeededFromTheStartWordsAndArcadeNeverUsesIt()
    {
        var a = ArcadeDeterminismTests.Create(4, true, new GameConfig(8));
        Assert.Equal(0u, a.State.EnhancedRng);
        for (int f = 0; f < 3000; f++) a.Step(new FrameInput { P1 = new PlayerInput { Fire = f % 2 == 0 } });
        Assert.Equal(0u, a.State.EnhancedRng);
        Assert.Equal(0, a.State.ClearsTowardStep);
        Assert.All(a.State.EnemyFireCooldown, c => Assert.Equal(0, c));
        var e1 = ArcadeDeterminismTests.Create(4, true, new GameConfig(8, Mode: PlayMode.Enhanced));
        var e2 = ArcadeDeterminismTests.Create(4, true, new GameConfig(8, Mode: PlayMode.Enhanced));
        var e3 = ArcadeDeterminismTests.Create(5, true, new GameConfig(8, Mode: PlayMode.Enhanced));
        Assert.NotEqual(0u, e1.State.EnhancedRng);
        Assert.Equal(e1.State.EnhancedRng, e2.State.EnhancedRng);
        Assert.NotEqual(e1.State.EnhancedRng, e3.State.EnhancedRng);
    }
}
