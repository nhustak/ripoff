using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RipOff.Core;
using RipOff.Game;

namespace RipOff.Game.Tests;

public class PadMapperTests
{
    /// <summary>A connected pad with the given stick X, right trigger, buttons and D-pad.</summary>
    static GamePadState Pad(float stickX = 0, float rt = 0, Buttons buttons = 0, bool dLeft = false, bool dRight = false, float lt = 0) =>
        new GamePadState(new GamePadThumbSticks(new Vector2(stickX, 0), Vector2.Zero), new GamePadTriggers(lt, rt),
            new GamePadButtons(buttons),
            new GamePadDPad(ButtonState.Released, ButtonState.Released, dLeft ? ButtonState.Pressed : ButtonState.Released, dRight ? ButtonState.Pressed : ButtonState.Released));

    static readonly GamePadState None = default; // disconnected

    [Fact]
    public void FakeStatesAreConnectedAndDefaultIsNot()
    {
        Assert.True(Pad().IsConnected);
        Assert.False(None.IsConnected);
        Assert.Equal(default(PlayerInput), PadMapper.MapPlayer(None));
    }

    [Theory]
    [InlineData(0f, false, false)]
    [InlineData(0.30f, false, false)]
    [InlineData(-0.30f, false, false)]
    [InlineData(0.36f, false, true)]
    [InlineData(-0.36f, true, false)]
    [InlineData(1f, false, true)]
    [InlineData(-1f, true, false)]
    public void StickDeadzone(float x, bool left, bool right)
    {
        var p = PadMapper.MapPlayer(Pad(stickX: x));
        Assert.Equal(left, p.Left); Assert.Equal(right, p.Right);
        Assert.False(p.Thrust || p.Fire);
    }

    [Fact]
    public void DPadTurnsAndOrsWithStick()
    {
        Assert.True(PadMapper.MapPlayer(Pad(dLeft: true)).Left);
        Assert.True(PadMapper.MapPlayer(Pad(dRight: true)).Right);
        var both = PadMapper.MapPlayer(Pad(stickX: 0.1f, dRight: true));
        Assert.True(both.Right); Assert.False(both.Left);
    }

    [Fact]
    public void ThrustAndFireButtons()
    {
        Assert.True(PadMapper.MapPlayer(Pad(buttons: Buttons.A)).Thrust);
        // left trigger = thrust past 0.3, held = thrust
        Assert.False(PadMapper.MapPlayer(Pad(lt: 0.3f)).Thrust);
        Assert.True(PadMapper.MapPlayer(Pad(lt: 0.31f)).Thrust);
        Assert.True(PadMapper.MapPlayer(Pad(lt: 1f)).Thrust);
        Assert.False(PadMapper.MapPlayer(Pad(lt: 1f)).Fire);
        // right trigger = fire past 0.3, and no longer thrust
        Assert.False(PadMapper.MapPlayer(Pad(rt: 1f)).Thrust);
        Assert.False(PadMapper.MapPlayer(Pad(rt: 0.3f)).Fire);
        Assert.True(PadMapper.MapPlayer(Pad(rt: 0.31f)).Fire);
        Assert.True(PadMapper.MapPlayer(Pad(rt: 1f)).Fire);
        Assert.True(PadMapper.MapPlayer(Pad(buttons: Buttons.X)).Fire);
        Assert.True(PadMapper.MapPlayer(Pad(buttons: Buttons.RightShoulder)).Fire);
        Assert.False(PadMapper.MapPlayer(Pad(buttons: Buttons.B | Buttons.Y | Buttons.LeftShoulder)).Fire);
    }

    [Fact]
    public void FireIsALevelSoTheSimulationEdgeDetectsIt()
    {
        var m = new PadMapper();
        for (int i = 0; i < 3; i++) Assert.True(m.Update(Pad(buttons: Buttons.X), None).P1.Fire);
        // the simulation sees the same level as from the keyboard: identical games pad vs keyboard
        var fromPad = new Simulation(); var fromKey = new Simulation();
        foreach (var s in new[] { fromPad, fromKey }) { s.State.CanisterDip = 3; s.State.Credits = 1; s.StartGame(true); }
        for (int i = 0; i < 200; i++)
        {
            bool down = (i / 25) % 2 == 1;
            var pf = m.Update(Pad(buttons: down ? Buttons.X : 0, stickX: i % 50 < 10 ? -1 : 0), None);
            fromPad.Step(PadMapper.Combine(default, pf));
            fromKey.Step(new FrameInput { P1 = new PlayerInput { Fire = down, Left = i % 50 < 10 } });
            Assert.Equal(fromKey.State.Sound.Torpedo, fromPad.State.Sound.Torpedo);
        }
        Assert.Equal(fromKey.State.Players[0].X, fromPad.State.Players[0].X);
    }

    [Fact]
    public void RightTriggerFiresExactlyLikeXAndRb()
    {
        // Same signal, same code path: games driven by RT, X, RB and the keyboard are identical, held or pumped.
        Simulation Play(Func<bool, GamePadState> pad)
        {
            var m = new PadMapper(); var sim = new Simulation();
            sim.State.CanisterDip = 3; sim.State.Credits = 1; sim.StartGame(true);
            for (int i = 0; i < 300; i++)
            {
                bool down = i >= 40 && (i < 140 || (i / 7) % 2 == 0); // held for 100 passes, then pumped
                sim.Step(PadMapper.Combine(default, m.Update(pad(down), None)));
            }
            return sim;
        }
        var rt = Play(d => Pad(rt: d ? 1f : 0f));
        var x = Play(d => Pad(buttons: d ? Buttons.X : 0));
        var rb = Play(d => Pad(buttons: d ? Buttons.RightShoulder : 0));
        var key = new Simulation(); key.State.CanisterDip = 3; key.State.Credits = 1; key.StartGame(true);
        for (int i = 0; i < 300; i++) key.Step(new FrameInput { P1 = new PlayerInput { Fire = i >= 40 && (i < 140 || (i / 7) % 2 == 0) } });
        foreach (var other in new[] { x, rb, key })
        {
            Assert.Equal(other.State.Score[0], rt.State.Score[0]);
            for (int k = 0; k < rt.State.Shots.Length; k++) Assert.Equal(other.State.Shots[k], rt.State.Shots[k]);
            Assert.Equal(other.State.TorpedoTimer, rt.State.TorpedoTimer);
        }
        // while held every pass maps to "fire", for RT as for X
        var m2 = new PadMapper();
        for (int i = 0; i < 5; i++) Assert.True(m2.Update(Pad(rt: 0.8f), None).P1.Fire);
    }

    [Fact]
    public void HeldFireGivesOneShotInTheSimulation()
    {
        // Documents the existing behaviour (Simulation.cs fire block, lst:2578-2582): a held fire input fires once.
        var sim = new Simulation(); sim.State.CanisterDip = 3; sim.State.Credits = 1; sim.StartGame(true);
        for (int i = 0; i < 60; i++) sim.Step(default);
        int newShots = 0; var m = new PadMapper();
        for (int i = 0; i < 200; i++)
        {
            int before = sim.State.Shots.Count(s => s.Live);
            sim.Step(PadMapper.Combine(default, m.Update(Pad(rt: 1f), None)));
            if (sim.State.Shots.Count(s => s.Live) > before) newShots++;
        }
        Assert.Equal(1, newShots);
    }

    [Fact]
    public void TriggerHeldOnConnectReadsAsHeld()
    {
        // Triggers are levels like X/RB: connecting with RT pulled gives the same "fire held" level a held X gives;
        // whether that fires is the simulation's fire edge (same as plugging in with X held).
        var m = new PadMapper();
        Assert.Equal(new PadMapper().Update(Pad(buttons: Buttons.X), None).P1.Fire, m.Update(Pad(rt: 1f), None).P1.Fire);
    }

    [Fact]
    public void CoinAndStartAreEdges()
    {
        var m = new PadMapper();
        Assert.False(m.Update(Pad(), Pad()).Coin);                       // connect, nothing held
        Assert.True(m.Update(Pad(buttons: Buttons.Back), Pad()).Coin);   // press
        Assert.False(m.Update(Pad(buttons: Buttons.Back), Pad()).Coin);  // held: no repeat credit
        Assert.False(m.Update(Pad(), Pad()).Coin);
        Assert.True(m.Update(Pad(), Pad(buttons: Buttons.Back)).Coin);   // coin from pad 1 too

        var s = m.Update(Pad(buttons: Buttons.Start), Pad());
        Assert.True(s.Start1); Assert.False(s.Start2);
        Assert.False(m.Update(Pad(buttons: Buttons.Start), Pad()).Start1); // held
        s = m.Update(Pad(), Pad(buttons: Buttons.Start));
        Assert.True(s.Start2); Assert.False(s.Start1);
        m.Update(Pad(buttons: Buttons.LeftShoulder), Pad());
        s = m.Update(Pad(buttons: Buttons.LeftShoulder | Buttons.Start), Pad());
        Assert.True(s.Start2); Assert.False(s.Start1);                   // LB + Start on pad 0 = 2-player start
    }

    [Fact]
    public void HotPlugNeverProducesSpuriousEdges()
    {
        var m = new PadMapper();
        // pad connects with Back and Start already held: no coin, no start
        var f = m.Update(Pad(buttons: Buttons.Back | Buttons.Start), None);
        Assert.False(f.Coin || f.Start1 || f.Start2);
        Assert.True(m.IsConnected(0)); Assert.False(m.IsConnected(1));
        // disconnect mid-game: neutral, no exception
        f = m.Update(None, None);
        Assert.Equal(default(PlayerInput), f.P1);
        Assert.False(m.IsConnected(0));
        // reconnect still holding: no edge; release and press again: edge
        Assert.False(m.Update(Pad(buttons: Buttons.Back), None).Coin);
        Assert.False(m.Update(Pad(), None).Coin);
        Assert.True(m.Update(Pad(buttons: Buttons.Back), None).Coin);
        // pad 1 appears and vanishes repeatedly
        for (int i = 0; i < 10; i++) { var g = m.Update(Pad(), i % 2 == 0 ? Pad(buttons: Buttons.Start) : None); Assert.False(g.Start2); }
    }

    [Fact]
    public void KeyboardAndPadAreOred()
    {
        var k = new FrameInput { P1 = new PlayerInput { Left = true }, P2 = new PlayerInput { Fire = true }, Coin = true };
        var p = new PadFrame { P1 = new PlayerInput { Thrust = true }, P2 = new PlayerInput { Right = true }, Start2 = true };
        var c = PadMapper.Combine(k, p);
        Assert.True(c.P1.Left && c.P1.Thrust && !c.P1.Right && !c.P1.Fire);
        Assert.True(c.P2.Fire && c.P2.Right && !c.P2.Left);
        Assert.True(c.Coin && c.Start2 && !c.Start1);
    }
}

public class SoundOutputTests(Xunit.Abstractions.ITestOutputHelper log)
{
    [Fact]
    public void Pcm16Conversion()
    {
        var src = new[] { 0f, 1f, -1f, 0.5f, float.NaN, 2f, -2f };
        var dst = new byte[src.Length * 2];
        Assert.Equal(14, SoundOutput.ToPcm16(src, src.Length, dst));
        short S(int i) => (short)(dst[2 * i] | (dst[2 * i + 1] << 8));
        Assert.Equal(0, S(0)); Assert.Equal(32767, S(1)); Assert.Equal(-32767, S(2));
        Assert.Equal(16384, S(3)); Assert.Equal(0, S(4)); Assert.Equal(32767, S(5)); Assert.Equal(-32767, S(6));
    }

    [Fact]
    public void DisabledOutputStillRunsTheBoard()
    {
        using var o = new SoundOutput(enable: false);
        Assert.False(o.Available);
        for (int i = 0; i < 50; i++) o.Update(new SoundState { Motor = true, Explosions = i == 3 ? 1 : 0 });
    }

    [Fact]
    public void WithoutAudioHardwareNothingThrows()
    {
        // On a machine without an audio device MonoGame throws NoAudioHardwareException (or fails to load OpenAL);
        // SoundOutput must swallow it. With a device this simply plays a few silent passes.
        using var o = new SoundOutput();
        for (int i = 0; i < 10; i++) o.Update(new SoundState { BackgroundMuted = true });
        Assert.False(string.IsNullOrEmpty(o.Status));
        log.WriteLine("SoundOutput status on this machine: " + o.Status);
    }
}
