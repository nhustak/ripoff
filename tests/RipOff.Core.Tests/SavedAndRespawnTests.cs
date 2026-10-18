using RipOff.Core;

namespace RipOff.Core.Tests;

/// <summary>SAVED!! POPUP and SMART RESPAWN (NOTES §33).</summary>
public class SavedAndRespawnTests
{
    static Simulation Start(EnhancedOption o = EnhancedOption.All, bool onePlayer = false, int seed = 1, int canisters = 8)
    {
        var sim = new Simulation();
        sim.StartGame(onePlayer, new GameConfig(canisters, false, PlayMode.Enhanced, o));
        sim.State.SaltEnhancedRng((uint)seed);
        return sim;
    }

    /// <summary>Random held inputs (as ArcadeDeterminismTests), calling <paramref name="before"/> and <paramref name="after"/> around every pass.</summary>
    static void Play(Simulation sim, int seed, int frames, Action<GameState>? before, Action<GameState> after)
    {
        uint r = (uint)seed * 747796405u + 2891336453u;
        uint Rand() { r ^= r << 13; r ^= r >> 17; r ^= r << 5; return r; }
        var held = new byte[2]; var left = new int[2];
        for (int f = 1; f <= frames && sim.State.Mode == GameMode.Playing; f++)
        {
            for (int p = 0; p < 2; p++) if (--left[p] <= 0) { held[p] = (byte)(Rand() & 0xF); left[p] = 1 + (int)(Rand() % 40); }
            PlayerInput In(byte b) => new() { Left = (b & 1) != 0, Right = (b & 2) != 0 && (b & 1) == 0, Thrust = (b & 4) != 0, Fire = (b & 8) != 0 && (f & 1) == 0 };
            before?.Invoke(sim.State);
            sim.Step(new FrameInput { P1 = In(held[0]), P2 = In(held[1]) });
            after(sim.State);
        }
    }

    [Fact]
    public void TogglesAndTuning()
    {
        Assert.Equal(0, GameRules.Arcade.SavedPopupPasses);
        Assert.Equal(RespawnDelayRule.Fixed, GameRules.Arcade.DelayRule);
        var e = GameRules.Enhanced;
        Assert.Equal(38, e.SavedPopupPasses);
        Assert.Equal(RespawnDelayRule.ByCause, e.DelayRule);
        Assert.Equal((25, 38, 19, 570), (e.RespawnDelay, e.RamRespawnExtra, e.QuickDeathRespawnExtra, e.QuickDeathWindowPasses));
        var noSaved = GameRules.EnhancedWith(EnhancedOption.All & ~EnhancedOption.SavedPopup);
        Assert.Equal(0, noSaved.SavedPopupPasses);
        Assert.Equal(RespawnDelayRule.ByCause, noSaved.DelayRule);
        var noSmart = GameRules.EnhancedWith(EnhancedOption.All & ~EnhancedOption.SmartRespawn);
        Assert.Equal(RespawnDelayRule.Fixed, noSmart.DelayRule);
        Assert.Equal((0, 0, 0), (noSmart.RamRespawnExtra, noSmart.QuickDeathRespawnExtra, noSmart.QuickDeathWindowPasses));
        Assert.Equal(38, noSmart.SavedPopupPasses);
        Assert.Equal(GameRules.Arcade, GameRules.For(PlayMode.Arcade, EnhancedOption.All));
    }

    [Fact]
    public void ShootingATowingEnemyShowsSavedAndOnlyThen()
    {
        int popups = 0, towShotKills = 0, rammedTowers = 0;
        for (int seed = 1; seed <= 40; seed++)
        {
            var sim = Start(seed: seed, canisters: 16);
            bool[] wasTow = new bool[3]; int[] prevTimer = new int[3];
            Play(sim, seed, 6000,
                s => { for (int i = 0; i < 3; i++) { wasTow[i] = EnemyState.IsAlive(s.Enemies[i].A) && (s.Enemies[i].A & EnemyState.BitTow) != 0; prevTimer[i] = s.SavedTimer[i]; } },
                s =>
                {
                    for (int i = 0; i < 3; i++)
                    {
                        var e = s.Enemies[i];
                        bool shotNow = EnemyState.IsExploding(e.A) && e.C == 0 && e.Scale == GameData.ExplosionShot;
                        bool rammedNow = EnemyState.IsExploding(e.A) && e.C == 1 && e.Scale == GameData.ExplosionRam;
                        if (s.SavedTimer[i] == 38)
                        {
                            // a popup starts only on a shot kill, where the enemy was
                            Assert.True(shotNow, "popup without a shot kill");
                            Assert.Equal((e.X, e.Y), (s.SavedX[i], s.SavedY[i]));
                            popups++;
                        }
                        else Assert.Equal(Math.Max(0, prevTimer[i] - 1), s.SavedTimer[i]); // counts down by one per pass
                        if (wasTow[i] && shotNow) towShotKills++;
                        if (wasTow[i] && rammedNow) { rammedTowers++; Assert.NotEqual(38, s.SavedTimer[i]); }
                    }
                });
        }
        Console.WriteLine($"SAVED!! census: {popups} popups, {towShotKills} towing enemies shot, {rammedTowers} rammed");
        Assert.True(popups > 10, $"{popups} popups");
        Assert.True(popups >= towShotKills * 9 / 10, $"{popups} popups for {towShotKills} towing enemies shot");
        Assert.Equal(popups, Math.Min(popups, towShotKills)); // never more popups than towing enemies shot
    }

    [Fact]
    public void SavedOffNeverShowsAndAddsNoState()
    {
        var sim = Start(EnhancedOption.All & ~EnhancedOption.SavedPopup, canisters: 16);
        Play(sim, 3, 6000, null, s => Assert.All(s.SavedTimer, t => Assert.Equal(0, t)));
        var on = new GameConfig(8, false, PlayMode.Enhanced, EnhancedOption.All & ~EnhancedOption.SmartRespawn);
        var off = on with { Options = on.Options & ~EnhancedOption.SavedPopup };
        Assert.NotEqual(ArcadeDeterminismTests.Run(2, false, on), ArcadeDeterminismTests.Run(2, false, off));
        // hashed only while on
        var st = Start(EnhancedOption.All & ~EnhancedOption.SavedPopup).State;
        uint h = StateHash.Compute(st); st.SavedTimer[1] = 9; Assert.Equal(h, StateHash.Compute(st));
        st = Start().State;
        h = StateHash.Compute(st); st.SavedTimer[1] = 9; Assert.NotEqual(h, StateHash.Compute(st));
    }

    [Fact]
    public void SceneDrawsSavedOnlyWhileShowingAndWhenOn()
    {
        var s = Start().State;
        int n = Scene.Build(s).Count;
        s.SavedTimer[2] = 38; s.SavedX[2] = 1000; s.SavedY[2] = 300;
        var lines = Scene.Build(s);
        Assert.Equal(n + 26, lines.Count); // S5 A5 V2 E4 D6 !2 !2
        Assert.True(lines.All(l => l.X1 <= Scene.Width - 8 && l.X2 <= Scene.Width - 8), "kept on screen");
        var shown = lines.Except(Scene.Build(Start().State)).ToList();
        Assert.All(shown, l => Assert.True(l.Bright));
        s.SavedTimer[2] = 5;
        var dim = Scene.Build(s).Except(Scene.Build(Start().State)).ToList();
        Assert.NotEmpty(dim);
        Assert.All(dim, l => Assert.False(l.Bright)); // dim in its last quarter
        var off = Start(EnhancedOption.All & ~EnhancedOption.SavedPopup).State;
        int m = Scene.Build(off).Count;
        off.SavedTimer[0] = 38; off.SavedX[0] = 500; off.SavedY[0] = 300;
        Assert.Equal(m, Scene.Build(off).Count);
    }

    // ---------------------------------------------------------------- SMART RESPAWN

    /// <summary>Steps until player <paramref name="p"/> is alive (warped in).</summary>
    static void StepUntilAlive(Simulation sim, int p)
    {
        for (int i = 0; i < 400 && !EnemyState.IsAlive(sim.State.Players[p].A); i++) sim.Step(default);
        Assert.True(EnemyState.IsAlive(sim.State.Players[p].A));
    }

    /// <summary>Explodes player <paramref name="p"/> as the game does (word C: 1 = ram, 2 = shot) and returns the
    /// free passes (from the pass its explosion ends) before its warp-in starts: that is the respawn delay.</summary>
    static int DieAndCountDelay(Simulation sim, int p, int cause)
    {
        var s = sim.State; var r = s.Players[p];
        r.A &= 0xF; r.Scale = GameData.ExplosionRam; r.C = cause;
        int free = 0;
        for (int i = 0; i < 1000; i++)
        {
            sim.Step(default);
            if (s.WarpTimer[p] > 0 || EnemyState.IsAlive(r.A)) return free;
            if (EnemyState.IsFree(r.A)) free++;
        }
        throw new Exception("no respawn");
    }

    [Fact]
    public void DelayDependsOnCauseAndTimeSinceTheLastDeath()
    {
        var sim = Start(onePlayer: true);
        var s = sim.State;
        StepUntilAlive(sim, 0);
        foreach (var e in s.Enemies) e.A = EnemyState.Free; // nobody shoots in the meantime
        // first death, a ram: base + ram
        Assert.Equal(25 + 38, DieAndCountDelay(sim, 0, 1));
        Assert.Equal(25 + 38, s.LastRespawnDelay[0]);
        StepUntilAlive(sim, 0);
        // shot at once again: base + quick
        Assert.Equal(25 + 19, DieAndCountDelay(sim, 0, 2));
        Assert.Equal(25 + 19, s.LastRespawnDelay[0]);
        StepUntilAlive(sim, 0);
        // rammed at once again: base + ram + quick
        DieAndCountDelay(sim, 0, 1);
        Assert.Equal(25 + 38 + 19, s.LastRespawnDelay[0]);
        StepUntilAlive(sim, 0);
        // shot after 15 s or more: the arcade's 25
        s.SinceDeath[0] = 600;
        DieAndCountDelay(sim, 0, 2);
        Assert.Equal(25, s.LastRespawnDelay[0]);
    }

    [Fact]
    public void QuickWindowIsFifteenSeconds()
    {
        foreach (var (since, quick) in new[] { (450, true), (600, false), (GameState.SinceDeathCap, false) })
        {
            var sim = Start(onePlayer: true);
            StepUntilAlive(sim, 0);
            foreach (var e in sim.State.Enemies) e.A = EnemyState.Free;
            sim.State.SinceDeath[0] = since;
            DieAndCountDelay(sim, 0, 2);
            Assert.Equal(quick ? 44 : 25, sim.State.LastRespawnDelay[0]);
        }
    }

    [Fact]
    public void FirstSpawnIsNotDelayedAndOffIsAlwaysTheArcadesDelay()
    {
        var on = Start(); var off = Start(EnhancedOption.All & ~EnhancedOption.SmartRespawn);
        on.Step(default); off.Step(default);
        Assert.True(on.State.WarpTimer[0] > 0 && on.State.WarpTimer[1] > 0);
        Assert.Equal(off.State.WarpTimer[0], on.State.WarpTimer[0]);
        // in played games every delay is one of the four, and always 25 when off
        var seen = new HashSet<int>();
        for (int seed = 1; seed <= 6; seed++)
        {
            var sim = Start(seed: seed);
            Play(sim, seed, 5000, null, s => { for (int p = 0; p < 2; p++) seen.Add(s.LastRespawnDelay[p]); });
            var o = Start(EnhancedOption.All & ~EnhancedOption.SmartRespawn, seed: seed);
            Play(o, seed, 5000, null, s => Assert.All(s.LastRespawnDelay, d => Assert.Equal(25, d)));
        }
        Assert.Subset(new HashSet<int> { 25, 44, 63, 82 }, seen);
        Assert.Contains(63, seen); Assert.Contains(44, seen);
    }

    [Fact]
    public void SmartRespawnIsHashedOnlyWhenOn()
    {
        var st = Start(EnhancedOption.All & ~EnhancedOption.SmartRespawn).State;
        uint h = StateHash.Compute(st); st.SinceDeath[0] = 7; st.RespawnExtra[1] = 19; Assert.Equal(h, StateHash.Compute(st));
        st = Start().State;
        h = StateHash.Compute(st); st.SinceDeath[0] = 7; Assert.NotEqual(h, StateHash.Compute(st));
        // Arcade never uses it
        var a = new Simulation(); a.StartGame(false, new GameConfig(8, false, PlayMode.Arcade, EnhancedOption.All));
        Assert.Equal(RespawnDelayRule.Fixed, a.Rules.DelayRule);
    }
}
