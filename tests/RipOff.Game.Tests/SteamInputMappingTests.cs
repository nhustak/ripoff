using Microsoft.Xna.Framework.Input;
using RipOff.Core;
using RipOff.Game;
using Screen = RipOff.Game.FrontEnd.Screen;

namespace RipOff.Game.Tests;

/// <summary>Steam Input (Spacewar action sets, NOTES §25) → the game's pad layout, and the source choice.</summary>
public class SteamInputMappingTests
{
    static GamePadState Game(SteamActions a) { a.AnyBound = true; return SteamPadMapping.ToState(a, inGame: true); }
    static GamePadState Menu(SteamActions a) { a.AnyBound = true; return SteamPadMapping.ToState(a, inGame: false); }

    [Fact]
    public void ShipActionsMapToTheGameControls()
    {
        Assert.Equal(ButtonState.Pressed, Game(new SteamActions { TurnLeft = true }).DPad.Left);
        Assert.Equal(ButtonState.Pressed, Game(new SteamActions { TurnRight = true }).DPad.Right);
        Assert.True(Game(new SteamActions { Thrust = true }).IsButtonDown(Buttons.A));
        Assert.True(Game(new SteamActions { Fire = true }).IsButtonDown(Buttons.X));
        Assert.True(Game(new SteamActions { Pause = true }).IsButtonDown(Buttons.Start));
        Assert.Equal(-0.8f, Game(new SteamActions { AnalogX = -0.8f }).ThumbSticks.Left.X, 3);
        Assert.Equal(0f, Game(new SteamActions { AnalogX = float.NaN }).ThumbSticks.Left.X);
        Assert.True(Game(new SteamActions()).IsConnected);
        var p = PadMapper.MapPlayer(Game(new SteamActions { Thrust = true, Fire = true, AnalogX = -1f }));
        Assert.True(p.Thrust && p.Fire && p.Left && !p.Right);
    }

    [Fact]
    public void MenuActionsMapToNavigation()
    {
        Assert.Equal(ButtonState.Pressed, Menu(new SteamActions { MenuUp = true }).DPad.Up);
        Assert.Equal(ButtonState.Pressed, Menu(new SteamActions { MenuDown = true }).DPad.Down);
        Assert.Equal(ButtonState.Pressed, Menu(new SteamActions { MenuLeft = true }).DPad.Left);
        Assert.Equal(ButtonState.Pressed, Menu(new SteamActions { MenuRight = true }).DPad.Right);
        Assert.True(Menu(new SteamActions { MenuSelect = true }).IsButtonDown(Buttons.A));
        Assert.True(Menu(new SteamActions { MenuCancel = true }).IsButtonDown(Buttons.B));
        // ship actions are ignored while the menu set is active, and vice versa
        Assert.False(Menu(new SteamActions { Fire = true, Thrust = true }).IsButtonDown(Buttons.X));
        Assert.False(Game(new SteamActions { MenuSelect = true }).IsButtonDown(Buttons.A));
    }

    [Fact]
    public void MergePrefersBoundSteamInputAndOtherwiseKeepsSdlPads()
    {
        var sdl = new GamePadState[4]; sdl[2] = Fake.Pad(Buttons.A);
        var steam = new GamePadState[4]; steam[0] = Menu(new SteamActions { MenuDown = true });
        var r = new GamePadState[4];
        Assert.False(SteamPadMapping.Merge(sdl, steam, steamBound: 0, r));      // Steam sees nothing bound: SDL as is
        Assert.True(r[2].IsButtonDown(Buttons.A)); Assert.False(r[0].IsConnected);
        Assert.True(SteamPadMapping.Merge(sdl, steam, steamBound: 1, r));       // bound: Steam only, no double input
        Assert.Equal(ButtonState.Pressed, r[0].DPad.Down); Assert.False(r[2].IsConnected);
    }

    [Fact]
    public void FrontEndIsFullyDrivableThroughSteamInputActions()
    {
        using var r = new Rig(skipSplash: false);
        void Act(SteamActions a) { r.Tick(SteamPadMapping.ToState(a with { AnyBound = true }, r.F.InGame)); r.Tick(SteamPadMapping.ToState(new SteamActions { AnyBound = true }, r.F.InGame)); }
        Assert.Equal(Screen.Splash, r.F.Top);
        Act(new SteamActions { MenuSelect = true });                            // splash skip
        Assert.Equal(Screen.Main, r.F.Top);
        Act(new SteamActions { MenuDown = true }); Assert.Equal(1, r.F.Selected);
        Act(new SteamActions { MenuUp = true }); Assert.Equal(0, r.F.Selected);
        Act(new SteamActions { MenuSelect = true });                            // START 1 PLAYER
        Assert.Equal(Screen.ModeSelect, r.F.Top);
        Act(new SteamActions { MenuSelect = true });                            // ARCADE
        Assert.Equal(Screen.LocalGame, r.F.Top);
        Assert.True(r.F.InGame);
        var st = r.F.LocalSim!.State;
        r.Ticks(5, SteamPadMapping.ToState(new SteamActions { AnyBound = true }, true));
        int x0 = st.Players[0].X, y0 = st.Players[0].Y, h0 = st.Players[0].Heading;
        r.Ticks(20, SteamPadMapping.ToState(new SteamActions { AnyBound = true, Thrust = true, TurnLeft = true, Fire = true }, true));
        Assert.True(st.Players[0].X != x0 || st.Players[0].Y != y0);
        Assert.NotEqual(h0, st.Players[0].Heading);
        Assert.True(st.ShotsFired[0] >= 1);
        r.Tick(SteamPadMapping.ToState(new SteamActions { AnyBound = true }, true));
        Act(new SteamActions { Pause = true });                                 // pause_menu
        Assert.Equal(Screen.Pause, r.F.Top);
        Assert.False(r.F.InGame);                                               // menu set from now on
        Act(new SteamActions { MenuCancel = true });                            // menu_cancel resumes
        Assert.Equal(Screen.LocalGame, r.F.Top);
    }
}

public class SettingsPathTests
{
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("relative/dir")]
    public void SettingsFolderIsNeverRelative(string? folder)
    {
        Assert.True(Path.IsPathRooted(SettingsStore.AppDataRoot(folder)));
        Assert.Equal(AppContext.BaseDirectory, SettingsStore.AppDataRoot(folder));
    }

    [Fact]
    public void DefaultStorePathIsAbsolute()
    {
        Assert.True(Path.IsPathRooted(new SettingsStore().FilePath));
        Assert.Equal("/x/y", SettingsStore.AppDataRoot("/x/y"));
    }
}
