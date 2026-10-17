using System.IO;
using Microsoft.Xna.Framework.Input;
using RipOff.Core;
using RipOff.Game;
using Screen = RipOff.Game.FrontEnd.Screen;

namespace RipOff.Game.Tests;

/// <summary>
/// Regression tests for "controller doesn't work at all" (main 97a8b73, NOTES §24): a pad in any slot drives
/// menus and game, hot-plug, odd dead-zone settings, the game without Steam initialized (lazy Steam), the INPUT
/// TEST screen and the input log.
/// </summary>
public class PadRegressionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PadInAnySlotDrivesMenusAndGame(int slot)
    {
        using var r = new Rig(padIndex: slot);                 // the splash was skipped with A on this slot
        Assert.Equal(Screen.Main, r.F.Top);
        r.Press(Buttons.DPadDown); Assert.Equal(1, r.F.Selected);
        r.Tick(Fake.Pad(stickY: -1f)); r.Tick();               // stick down
        Assert.Equal(2, r.F.Selected);
        r.Tick(Fake.Pad(stickY: 1f)); r.Tick(); r.Press(Buttons.DPadUp);
        Assert.Equal(0, r.F.Selected);
        r.Press(Buttons.A);
        Assert.Equal(Screen.LocalGame, r.F.Top);
        var st = r.F.LocalSim!.State;
        r.Ticks(5);
        int x0 = st.Players[0].X, y0 = st.Players[0].Y, h0 = st.Players[0].Heading;
        r.Ticks(20, Fake.Pad(Buttons.A, stickX: -1f, rt: 1f)); // thrust (A), turn (stick), fire (RT)
        Assert.True(st.Players[0].X != x0 || st.Players[0].Y != y0, "pad thrust moved the tank");
        Assert.NotEqual(h0, st.Players[0].Heading);
        Assert.True(st.ShotsFired[0] >= 1);
        r.Tick(); r.Press(Buttons.Start); Assert.Equal(Screen.Pause, r.F.Top);
        r.Press(Buttons.Start); Assert.Equal(Screen.LocalGame, r.F.Top);
    }

    [Fact]
    public void HotPlugMidGameIntoAnotherSlot()
    {
        using var r = new Rig(padIndex: 0);
        r.Choose("START 1 PLAYER");
        var st = r.F.LocalSim!.State;
        r.Ticks(10, default(Microsoft.Xna.Framework.Input.GamePadState)); // unplugged: game keeps running
        Assert.Equal(Screen.LocalGame, r.F.Top);
        r.PadIndex = 3;                                         // plugged back in, SDL gives it slot 3
        r.Tick(Fake.Pad(Buttons.Start));                        // held while connecting: not a press
        Assert.Equal(Screen.LocalGame, r.F.Top);
        r.Tick();
        int x0 = st.Players[0].X, y0 = st.Players[0].Y;
        r.Ticks(20, Fake.Pad(Buttons.A));
        Assert.True(st.Players[0].X != x0 || st.Players[0].Y != y0);
        r.Tick(); r.Press(Buttons.Start);
        Assert.Equal(Screen.Pause, r.F.Top);
        r.Press(Buttons.DPadDown); Assert.Equal(1, r.F.Selected);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("-5")]
    [InlineData("1000")]
    [InlineData("1e30")]
    [InlineData("\"abc\"")]
    public void OddDeadZoneInSettingsDoesNotKillInput(string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ripoff-dz-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"StickDeadzone\": " + json + " }");
        var s = new SettingsStore(dir).Load(out _);
        Assert.InRange(s.StickDeadzone, Settings.DeadzoneMin, Settings.DeadzoneMax);
        using var r = new Rig(s);
        r.Choose("START 1 PLAYER");
        var st = r.F.LocalSim!.State;
        r.Ticks(5);
        int h0 = st.Players[0].Heading;
        r.Ticks(20, Fake.Pad(stickX: -1f));
        Assert.NotEqual(h0, st.Players[0].Heading);
        int h1 = st.Players[0].Heading;
        r.Ticks(20, Fake.Pad(Buttons.DPadRight));
        Assert.NotEqual(h1, st.Players[0].Heading);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(1f)]
    [InlineData(5f)]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void PadMapperDeadZoneIsGuarded(float dz)
    {
        var m = new PadMapper { StickDeadzone = dz };
        Assert.InRange(m.StickDeadzone, PadMapper.MinDeadzone, PadMapper.MaxDeadzone);
        Assert.True(m.Update(Fake.Pad(stickX: -1f)).P1.Left);  // full deflection always turns
        Assert.False(m.Update(Fake.Pad()).P1.Left);            // centred never does
    }

    [Fact]
    public void GameAndMenusWorkWithSteamNotInitialized()
    {
        var steam = new FakeSteam { Initialized = false, Available = true };
        using var r = new Rig(steam: steam);
        r.Choose("START 1 PLAYER");
        var st = r.F.LocalSim!.State;
        r.Ticks(5);
        int x0 = st.Players[0].X, y0 = st.Players[0].Y;
        r.Ticks(20, Fake.Pad(Buttons.A));
        Assert.True(st.Players[0].X != x0 || st.Players[0].Y != y0);
        r.Tick(); r.Press(Buttons.Start); r.Choose("QUIT TO MAIN MENU");
        r.Choose("OPTIONS"); r.Press(Buttons.B);
        r.Choose("ONLINE");
        Assert.Equal(new[] { true, true, true, true, true }, r.F.CurrentEnabled); // Steam items pickable (lazy)
        r.Choose("HOST DIRECT IP"); r.Choose("CANCEL");
        Assert.Equal(0, steam.Inits);                           // local play, menus and direct IP never start Steam
        Assert.False(steam.Initialized);
        r.Choose("HOST ONLINE (STEAM)");                         // only picking a Steam option starts it
        Assert.Equal(1, steam.Inits);
        Assert.Equal(Screen.Connecting, r.F.Top);
    }

    [Fact]
    public void LazySteamThatFailsStaysOnTheOnlineMenuAndGreysOut()
    {
        var steam = new FakeSteam { Initialized = false, Available = false, Status = "Steam not running" };
        using var r = new Rig(steam: steam);
        r.Choose("ONLINE");
        r.Choose("JOIN BY CODE (STEAM)");
        Assert.Equal(1, steam.Inits);
        Assert.Equal(Screen.Online, r.F.Top);
        Assert.Equal(new[] { false, false, true, true, true }, r.F.CurrentEnabled);
    }

    [Fact]
    public void WithoutAnySteamLocalPlayWorks()
    {
        using var r = new Rig(steam: null, padIndex: 2);
        r.Choose("START 2 PLAYERS");
        Assert.Equal(Screen.LocalGame, r.F.Top);
        var st = r.F.LocalSim!.State;
        r.Ticks(5);
        int x0 = st.Players[0].X, y0 = st.Players[0].Y;
        r.Ticks(20, Fake.Pad(Buttons.A));                       // the only pad (slot 2) is player 1
        Assert.True(st.Players[0].X != x0 || st.Players[0].Y != y0);
    }

    [Fact]
    public void InputTestScreenShowsSlotsAndOnlyLeavesOnHeldBOrEsc()
    {
        using var r = new Rig(padIndex: 1);
        r.Choose("OPTIONS"); r.Choose("INPUT TEST");
        Assert.Equal(Screen.InputTest, r.F.Top);
        r.Tick(Fake.Pad(Buttons.X | Buttons.RightShoulder, stickX: 0.5f, rt: 0.8f));
        Assert.True(r.F.Pads[1].Connected);
        Assert.False(r.F.Pads[0].Connected);
        Assert.Equal("X RB", PadText.Pressed(r.F.Pads[1].State));
        Assert.True(r.F.LastPlay.All.Fire);
        var lines = new List<Line>();
        r.F.Build(lines);
        Assert.NotEmpty(lines);
        r.Press(Buttons.B); r.Press(Buttons.A); r.Press(Buttons.Start); r.Press(Buttons.DPadDown);
        Assert.Equal(Screen.InputTest, r.F.Top);              // every button is testable: a tap does not leave
        r.Ticks(FrontEnd.InputTestHoldTicks, Fake.Pad(Buttons.B));
        Assert.Equal(Screen.Options, r.F.Top);
        r.Tick();
        r.Choose("INPUT TEST");
        r.Key(Keys.Escape);
        Assert.Equal(Screen.Options, r.F.Top);
    }

    [Fact]
    public void InputLogSnapshotsAllSlotsThenAppendsEvents()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ripoff-log-" + Guid.NewGuid().ToString("N"));
        var log = new InputLog(dir);
        log.Header("steam: not started at launch (STEAM AT LAUNCH off)");
        var slots = new PadSlot[4];
        slots[2] = new PadSlot { State = Fake.Pad(Buttons.A), Name = "Xbox Controller", Type = "GamePad" };
        for (int i = 0; i < InputLog.SnapshotAfterTicks - 1; i++) log.Tick(slots, "Steam not started");
        Assert.False(File.Exists(log.FilePath));                 // waits for SDL to report pads
        log.Tick(slots, "Steam not started");
        string text = File.ReadAllText(log.FilePath);
        Assert.Contains("slot 0: not connected", text);
        Assert.Contains("slot 2: CONNECTED name=\"Xbox Controller\" type=GamePad buttons=A", text);
        Assert.Contains("STEAM AT LAUNCH off", text);
        slots[2] = default;
        log.Tick(slots, "x");
        log.Event("steam init (lazy, after the window): Steam OK");
        text = File.ReadAllText(log.FilePath);
        Assert.Contains("disconnected: slot 2", text);
        Assert.Contains("steam init (lazy", text);
    }

    [Fact]
    public void OldSettingsWithSteamEnabledNoLongerStartSteamAtLaunch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ripoff-v1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"Version\": 1, \"SteamEnabled\": true, \"AutoFire\": true }");
        var s = new SettingsStore(dir).Load(out var warn);
        Assert.Null(warn);
        Assert.False(s.SteamAtLaunch);
        Assert.True(s.AutoFire);
        Assert.Equal(2, s.Version);
    }
}
