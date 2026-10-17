using RipOff.Core;

namespace RipOff.Core.Tests;

/// <summary>Auto-fire (remake option, NOTES §23): off = arcade one shot per press; on = repeat every
/// GameData.AutoFireInterval passes while held, never more than 4 live shots per player.</summary>
public class AutoFireTests
{
    static Simulation Start(bool autoFire, bool onePlayer = true)
    {
        var sim = new Simulation();
        sim.StartGame(onePlayer, new GameConfig(8, autoFire));
        sim.Step(default); // respawn pass (players appear in the first pass)
        Assert.Equal(EnemyState.PlayerAlive, sim.State.Players[0].A);
        return sim;
    }

    static int Live(GameState s, int p) => Enumerable.Range(p * 4, 4).Count(k => s.Shots[k].Live);

    [Fact]
    public void OffHoldingFireGivesOneShot()
    {
        var sim = Start(autoFire: false);
        for (int i = 0; i < 300; i++) sim.Step(new FrameInput { P1 = new PlayerInput { Fire = true } });
        Assert.Equal(1, sim.State.ShotsFired[0]);
    }

    [Fact]
    public void OffEveryPressFiresUpToTheCap()
    {
        var sim = Start(autoFire: false);
        for (int i = 0; i < 16; i++) sim.Step(new FrameInput { P1 = new PlayerInput { Fire = i % 2 == 0 } });
        Assert.Equal(4, sim.State.ShotsFired[0]); // 8 presses, 4 slots, shots still in flight
        Assert.Equal(4, Live(sim.State, 0));
    }

    [Fact]
    public void OnRepeatsAtTheIntervalWhileHeld()
    {
        var sim = Start(autoFire: true);
        var firedAt = new List<int>();
        for (int i = 0; i < 3 * GameData.AutoFireInterval + 1; i++)
        {
            int before = sim.State.ShotsFired[0];
            sim.Step(new FrameInput { P1 = new PlayerInput { Fire = true } });
            if (sim.State.ShotsFired[0] > before) firedAt.Add(i);
        }
        Assert.Equal(new[] { 0, GameData.AutoFireInterval, 2 * GameData.AutoFireInterval, 3 * GameData.AutoFireInterval }, firedAt);
        Assert.Equal(6, GameData.AutoFireInterval); // documented rate: 38 / 6 ≈ 6.3 shots per second
    }

    [Fact]
    public void OnNeverExceedsTheFourShotCapAndRefiresWhenASlotFrees()
    {
        var sim = Start(autoFire: true);
        int maxLive = 0, prevLive = 0, refireOnFree = 0;
        for (int i = 0; i < 2000; i++)
        {
            sim.Step(new FrameInput { P1 = new PlayerInput { Fire = true, Left = i % 90 < 30 } });
            int live = Live(sim.State, 0);
            maxLive = Math.Max(maxLive, live);
            if (prevLive == 4 && live == 4 && sim.State.AutoFireTimer[0] == 0) refireOnFree++;
            prevLive = live;
            Assert.InRange(sim.State.ShotsFired[0], 0, i / GameData.AutoFireInterval + 1); // never faster than the rate
        }
        Assert.Equal(4, maxLive);
        Assert.True(sim.State.ShotsFired[0] > 2000 / GameData.AutoFireInterval / 4, $"only {sim.State.ShotsFired[0]} shots");
    }

    [Fact]
    public void OnReleaseAndPressFiresImmediately()
    {
        var sim = Start(autoFire: true);
        sim.Step(new FrameInput { P1 = new PlayerInput { Fire = true } });  // shot 1
        sim.Step(new FrameInput { P1 = new PlayerInput { Fire = true } });  // timer 1
        sim.Step(default);                                                   // release resets the timer
        sim.Step(new FrameInput { P1 = new PlayerInput { Fire = true } });  // shot 2 at once (press edge)
        Assert.Equal(2, sim.State.ShotsFired[0]);
        Assert.Equal(0, sim.State.AutoFireTimer[0]);
    }

    [Fact]
    public void BothPlayersAndDeterministic()
    {
        static (uint hash, int p1, int p2) Run(bool autoFire)
        {
            var sim = Start(autoFire, onePlayer: false);
            for (int f = 0; f < 3000; f++)
                sim.Step(new FrameInput { P1 = new PlayerInput { Left = f % 50 < 20, Thrust = f % 7 < 5, Fire = f % 40 < 30 },
                                          P2 = new PlayerInput { Right = f % 30 < 10, Thrust = true, Fire = f % 60 < 45 } });
            return (StateHash.Compute(sim.State), sim.State.ShotsFired[0], sim.State.ShotsFired[1]);
        }
        var on = Run(true);
        Assert.Equal(on, Run(true));
        var off = Run(false);
        Assert.Equal(off, Run(false));
        Assert.NotEqual(on.hash, off.hash);
        Assert.True(on.p1 > off.p1 && on.p2 > off.p2, $"on {on.p1}/{on.p2}, off {off.p1}/{off.p2}");
    }
}
