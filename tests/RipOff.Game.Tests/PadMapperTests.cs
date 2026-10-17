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
        foreach (var s in new[] { fromPad, fromKey }) { s.State.CanisterDip = 3; s.StartGame(true); }
        for (int i = 0; i < 200; i++)
        {
            bool down = (i / 25) % 2 == 1;
            var pf = m.Update(Pad(buttons: down ? Buttons.X : 0, stickX: i % 50 < 10 ? -1 : 0), None);
            fromPad.Step(new FrameInput { P1 = pf.P1 });
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
            sim.State.CanisterDip = 3; sim.StartGame(true);
            for (int i = 0; i < 300; i++)
            {
                bool down = i >= 40 && (i < 140 || (i / 7) % 2 == 0); // held for 100 passes, then pumped
                sim.Step(new FrameInput { P1 = m.Update(pad(down), None).P1 });
            }
            return sim;
        }
        var rt = Play(d => Pad(rt: d ? 1f : 0f));
        var x = Play(d => Pad(buttons: d ? Buttons.X : 0));
        var rb = Play(d => Pad(buttons: d ? Buttons.RightShoulder : 0));
        var key = new Simulation(); key.State.CanisterDip = 3; key.StartGame(true);
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
        var sim = new Simulation(); sim.State.CanisterDip = 3; sim.StartGame(true);
        for (int i = 0; i < 60; i++) sim.Step(default);
        int newShots = 0; var m = new PadMapper();
        for (int i = 0; i < 200; i++)
        {
            int before = sim.State.Shots.Count(s => s.Live);
            sim.Step(new FrameInput { P1 = m.Update(Pad(rt: 1f), None).P1 });
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
    public void FirstConnectedPadIsPlayerOneWhateverItsIndex()
    {
        // M4 controller fix: a pad SDL puts at index 1 or 2 (index 0 empty) still drives player 1.
        var m = new PadMapper();
        var f = m.Update(None, Pad(buttons: Buttons.A), None, Pad(buttons: Buttons.X));
        Assert.True(f.P1.Thrust); Assert.True(f.P2.Fire);
        Assert.Equal((1, 3, 2), (m.P1Index, m.P2Index, m.ConnectedCount));
        f = m.Update(None, None, Pad(dLeft: true), None);
        Assert.True(f.P1.Left); Assert.Equal(default(PlayerInput), f.P2);
        f = m.Update(None, None, None, None);
        Assert.Equal((-1, -1, 0), (m.P1Index, m.P2Index, m.ConnectedCount));
        Assert.Equal(default(PlayerInput), f.P1);
    }

    [Theory]
    [InlineData(0.10f, 0.12f, true)]
    [InlineData(0.35f, 0.30f, false)]
    [InlineData(0.60f, 0.59f, false)]
    [InlineData(0.60f, 0.61f, true)]
    public void DeadZoneIsConfigurable(float deadzone, float stick, bool turns)
    {
        var m = new PadMapper { StickDeadzone = deadzone };
        Assert.Equal(turns, m.Update(Pad(stickX: stick)).P1.Right);
        Assert.Equal(turns, PadMapper.MapPlayer(Pad(stickX: -stick), deadzone).Left);
    }

    [Fact]
    public void NoCoinOrStartInGameplayControls()
    {
        // Back/View and Start map to nothing in play (no credits; Start opens the pause menu via InputReader).
        Assert.Equal(default(PlayerInput), PadMapper.MapPlayer(Pad(buttons: Buttons.Back | Buttons.Start | Buttons.B | Buttons.Y)));
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
