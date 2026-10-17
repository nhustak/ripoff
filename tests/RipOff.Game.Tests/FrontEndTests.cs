using Microsoft.Xna.Framework.Input;
using RipOff.Core;
using RipOff.Game;
using RipOff.Net;
using Screen = RipOff.Game.FrontEnd.Screen;

namespace RipOff.Game.Tests;

/// <summary>The whole front end driven by a fake controller (and keyboard), through the real InputReader.</summary>
public class FrontEndTests
{
    [Fact]
    public void SplashSkipsOnAnyButtonAnyKeyOrTimeout()
    {
        foreach (var skip in new Action<Rig>[] { r => r.Press(Buttons.Y), r => r.Press(Buttons.Start), r => r.Key(Keys.Q), r => r.Ticks(FrontEnd.SplashTicks) })
        {
            using var r = new Rig(skipSplash: false);
            Assert.Equal(Screen.Splash, r.F.Top);
            skip(r);
            Assert.Equal(Screen.Main, r.F.Top);
        }
    }

    [Fact]
    public void SplashTitleComesFromBranding()
    {
        using var r = new Rig(skipSplash: false);
        r.Ticks(FrontEnd.Tps * 2);
        var lines = new List<Line>(); r.F.Build(lines);
        var expected = new List<Line>(); VectorFont.Draw(Branding.GameName, 0, 0, 10, expected);
        Assert.True(lines.Count > expected.Count); // full title (and frame, prompt) drawn
    }

    [Fact]
    public void MainMenuLayout()
    {
        using var r = new Rig();
        Assert.Equal(new[] { "START 1 PLAYER", "START 2 PLAYERS", "ONLINE", "OPTIONS", "EXIT" }, r.F.CurrentLabels);
    }

    [Fact]
    public void PadNavigatesWithDpadAndStickAndWraps()
    {
        using var r = new Rig();
        r.Press(Buttons.DPadDown); Assert.Equal(1, r.F.Selected);
        r.Tick(Fake.Pad(stickY: -0.9f)); r.Tick(Fake.Pad(stickY: -0.9f)); r.Tick(); // held stick = one step
        Assert.Equal(2, r.F.Selected);
        r.Press(Buttons.DPadUp); r.Press(Buttons.DPadUp); r.Press(Buttons.DPadUp);
        Assert.Equal(4, r.F.Selected); // wrapped to EXIT
        r.Tick(Fake.Pad(stickY: 0.9f)); r.Tick();
        Assert.Equal(3, r.F.Selected);
    }

    [Fact]
    public void PadAtAnotherIndexDrivesTheMenus()
    {
        using var r = new Rig(padIndex: 2); // index 0 and 1 empty (the M4 controller bug)
        r.Choose("OPTIONS");
        Assert.Equal(Screen.Options, r.F.Top);
        r.Press(Buttons.B);
        Assert.Equal(Screen.Main, r.F.Top);
    }

    [Fact]
    public void StartOnePlayerStartsAtOnceWithTheSettings()
    {
        using var r = new Rig(new Settings { Canisters = 16, AutoFire = true });
        r.Choose("START 1 PLAYER"); r.Choose("ARCADE");
        Assert.Equal(Screen.LocalGame, r.F.Top);
        Assert.Equal(FrontEnd.GameKind.OnePlayer, r.F.Game);
        var st = r.F.LocalSim!.State;
        Assert.Equal(GameMode.Playing, st.Mode);
        Assert.True(st.OnePlayer && st.AutoFire);
        Assert.Equal(16, GameData.CanistersForDip[st.CanisterDip]);
        // the pad drives player 1 right away (thrust with A, fire with RT)
        r.Ticks(5);
        int x0 = st.Players[0].X, y0 = st.Players[0].Y;
        r.Ticks(30, Fake.Pad(Buttons.A, rt: 1f));
        Assert.True(st.Players[0].X != x0 || st.Players[0].Y != y0);
        Assert.True(st.ShotsFired[0] > 1); // auto-fire on: held RT repeats
    }

    [Fact]
    public void StartTwoPlayersUsesTheSecondPadForPlayerTwo()
    {
        using var r = new Rig();
        r.Others[1] = Fake.Pad(); r.Tick(); // second pad connects
        r.Choose("START 2 PLAYERS"); r.Choose("ARCADE");
        var st = r.F.LocalSim!.State;
        Assert.False(st.OnePlayer);
        r.Ticks(5);
        int x1 = st.Players[1].X, y1 = st.Players[1].Y, h0 = st.Players[0].Heading;
        r.Others[1] = Fake.Pad(Buttons.A | Buttons.DPadLeft); // pad 2: thrust and turn
        r.Ticks(30);
        Assert.True(st.Players[1].X != x1 || st.Players[1].Y != y1);
        Assert.Equal(h0, st.Players[0].Heading);                // player 1 does not turn from pad 2
    }

    [Fact]
    public void PauseWithStartOrEscFreezesTheLocalGame()
    {
        using var r = new Rig();
        r.Choose("START 1 PLAYER"); r.Choose("ARCADE");
        r.Ticks(10);
        r.Press(Buttons.B); // B is not pause in a game
        Assert.Equal(Screen.LocalGame, r.F.Top);
        r.Press(Buttons.Start);
        Assert.Equal(Screen.Pause, r.F.Top);
        Assert.Equal(new[] { "RESUME", "OPTIONS", "QUIT TO MAIN MENU", "EXIT TO DESKTOP" }, r.F.CurrentLabels);
        int pc = r.F.LocalSim!.State.PassCounter;
        r.Ticks(100);
        Assert.Equal(pc, r.F.LocalSim.State.PassCounter); // frozen
        r.Press(Buttons.Start);                             // Start toggles back
        Assert.Equal(Screen.LocalGame, r.F.Top);
        r.Key(Keys.Escape);
        Assert.Equal(Screen.Pause, r.F.Top);
        r.Press(Buttons.B);                                 // B closes the pause menu
        Assert.Equal(Screen.LocalGame, r.F.Top);
        r.Key(Keys.Escape); r.Key(Keys.Escape);             // Esc opens and closes
        Assert.Equal(Screen.LocalGame, r.F.Top);
        r.Key(Keys.Escape); r.Choose("RESUME");
        Assert.Equal(Screen.LocalGame, r.F.Top);
        Assert.NotEqual(pc, r.F.LocalSim.State.PassCounter);
    }

    [Fact]
    public void PauseOptionsAndQuitToMainMenu()
    {
        using var r = new Rig();
        r.Choose("START 2 PLAYERS"); r.Choose("ARCADE");
        r.Press(Buttons.Start); r.Choose("OPTIONS");
        Assert.Equal(new[] { Screen.Main, Screen.LocalGame, Screen.Pause, Screen.Options }, r.F.Stack);
        r.Press(Buttons.B);
        Assert.Equal(Screen.Pause, r.F.Top);
        r.Choose("QUIT TO MAIN MENU");
        Assert.Equal(new[] { Screen.Main }, r.F.Stack);
        Assert.Null(r.F.LocalSim);
        Assert.False(r.F.ExitRequested);
    }

    [Fact]
    public void ExitToDesktopFromPause()
    {
        using var r = new Rig();
        r.Choose("START 1 PLAYER"); r.Choose("ARCADE"); r.Press(Buttons.Start);
        r.Choose("EXIT TO DESKTOP");
        Assert.True(r.F.ExitRequested);
    }

    [Fact]
    public void OnlyExitQuits()
    {
        using var r = new Rig();
        var mash = new Action[]
        {
            () => r.Press(Buttons.B), () => r.Key(Keys.Escape), () => r.Press(Buttons.Start), () => r.Key(Keys.F10),
            () => r.Press(Buttons.Back), () => r.Key(Keys.D5), () => r.Key(Keys.Back), () => r.Press(Buttons.Y),
        };
        void Mash() { for (int k = 0; k < 3; k++) foreach (var m in mash) { m(); Assert.False(r.F.ExitRequested, "quit on " + r.F.Top); } }
        void GoMain()
        {
            for (int i = 0; i < 30 && !(r.F.Stack.Count == 1 && r.F.Top == Screen.Main); i++)
            {
                if (r.F.Top is Screen.LocalGame or Screen.GameOver) r.Press(Buttons.Start);
                else if (r.F.Top == Screen.Pause) r.Choose("QUIT TO MAIN MENU");
                else r.Press(Buttons.B);
            }
            Assert.Equal(new[] { Screen.Main }, r.F.Stack);
        }
        Mash(); GoMain();                              // main menu, options
        r.F.StartLocal(true); Mash(); GoMain();         // in game, pause, options
        r.Choose("ONLINE"); Mash(); GoMain();
        r.Choose("OPTIONS"); Mash(); GoMain();
        Assert.False(r.F.ExitRequested);
        r.Choose("EXIT");
        Assert.True(r.F.ExitRequested);
    }

    [Fact]
    public void BackOnTheMainMenuDoesNothing()
    {
        using var r = new Rig();
        for (int i = 0; i < 5; i++) { r.Press(Buttons.B); r.Key(Keys.Escape); }
        Assert.Equal(new[] { Screen.Main }, r.F.Stack);
        Assert.Equal(0, r.F.Selected);
    }

    [Fact]
    public void F10OpensOptionsFromEverywhere()
    {
        using var r = new Rig(skipSplash: false);
        r.Key(Keys.F10);
        Assert.Equal(new[] { Screen.Main, Screen.Options }, r.F.Stack); // from the splash
        r.Key(Keys.F10);                                                // already open: stays
        Assert.Equal(Screen.Options, r.F.Top);
        r.Key(Keys.Escape);
        Assert.Equal(Screen.Main, r.F.Top);
        r.F.StartLocal(true);
        r.Key(Keys.F10);
        Assert.Equal(new[] { Screen.Main, Screen.LocalGame, Screen.Options }, r.F.Stack);
        int pc = r.F.LocalSim!.State.PassCounter; r.Ticks(20);
        Assert.Equal(pc, r.F.LocalSim.State.PassCounter);              // paused under options
        r.Press(Buttons.B);
        Assert.Equal(Screen.LocalGame, r.F.Top);
    }

    [Fact]
    public void OptionsChangeEverySettingFromThePadAndSave()
    {
        var s = new Settings();
        using var r = new Rig(s);
        r.Choose("OPTIONS");
        Assert.Equal(new[] { "AUTO FIRE: OFF", "CANISTERS: 8", "VOLUME: 100%", "MUTE: OFF", "DISPLAY: WINDOWED", "STICK DEAD ZONE: 35%", "STEAM AT LAUNCH: OFF", "ENHANCED OPTIONS: ALL ON", "INPUT TEST", "BACK" }, r.F.CurrentLabels);
        r.Press(Buttons.A); Assert.True(s.AutoFire);                       // A toggles
        r.Press(Buttons.DPadRight); Assert.False(s.AutoFire);              // right toggles too
        r.Press(Buttons.DPadDown); r.Press(Buttons.DPadRight); Assert.Equal(12, s.Canisters);
        r.Press(Buttons.DPadRight); Assert.Equal(16, s.Canisters);
        r.Press(Buttons.DPadRight); Assert.Equal(4, s.Canisters);          // wraps
        r.Press(Buttons.DPadLeft); Assert.Equal(16, s.Canisters);
        r.Press(Buttons.DPadDown); r.Press(Buttons.DPadLeft); r.Press(Buttons.DPadLeft);
        Assert.Equal(0.8, s.Volume, 3);
        r.Press(Buttons.DPadDown); r.Press(Buttons.A); Assert.True(s.Muted);
        r.Press(Buttons.DPadDown); r.Press(Buttons.A); Assert.True(s.Fullscreen);
        r.Press(Buttons.DPadDown); r.Press(Buttons.DPadRight); Assert.Equal(0.40f, s.StickDeadzone, 3);
        for (int i = 0; i < 20; i++) r.Press(Buttons.DPadLeft);
        Assert.Equal(Settings.DeadzoneMin, s.StickDeadzone, 3);           // clamped
        r.Press(Buttons.DPadDown); r.Press(Buttons.A); Assert.True(s.SteamAtLaunch);
        Assert.True(r.Saves >= 10);
        r.Press(Buttons.DPadDown); Assert.Equal("ENHANCED OPTIONS: ALL ON", r.F.CurrentLabels[r.F.Selected]);
        r.Press(Buttons.DPadDown); Assert.Equal("INPUT TEST", r.F.CurrentLabels[r.F.Selected]);
        r.Press(Buttons.DPadDown); Assert.Equal("BACK", r.F.CurrentLabels[r.F.Selected]);
        r.Press(Buttons.A);
        Assert.Equal(Screen.Main, r.F.Top);
    }

    [Fact]
    public void VolumeShortcutsWorkOutsideTextEntry()
    {
        var s = new Settings();
        using var r = new Rig(s);
        r.Key(Keys.OemMinus); Assert.Equal(0.9, s.Volume, 3);
        r.Key(Keys.M); Assert.True(s.Muted);
        r.Key(Keys.OemPlus); Assert.False(s.Muted); Assert.Equal(1.0, s.Volume, 3);
    }

    [Fact]
    public void LocalGameOverOffersPlayAgainAndMainMenu()
    {
        using var r = new Rig();
        r.Choose("START 1 PLAYER"); r.Choose("ARCADE");
        var st = r.F.LocalSim!.State;
        Fake.AllCanistersGone(st); // game over on the next pass
        r.Tick();
        Assert.Equal(Screen.GameOver, r.F.Top);
        Assert.Equal(new[] { "PLAY AGAIN", "MAIN MENU" }, r.F.CurrentLabels);
        r.Press(Buttons.A);                               // locked for a second: ignored
        Assert.Equal(Screen.GameOver, r.F.Top);
        r.Ticks(FrontEnd.GameOverLockTicks);
        r.Press(Buttons.A);
        Assert.Equal(Screen.LocalGame, r.F.Top);
        Assert.NotSame(st, r.F.LocalSim!.State);          // a fresh game
        Fake.AllCanistersGone(r.F.LocalSim.State);
        r.Tick(); r.Ticks(FrontEnd.GameOverLockTicks);
        r.Choose("MAIN MENU");
        Assert.Equal(new[] { Screen.Main }, r.F.Stack);
    }

    [Fact]
    public void OnlineMenuWithoutSteamGreysOutSteamItems()
    {
        using var r = new Rig(steam: new FakeSteam { Available = false });
        r.Choose("ONLINE");
        Assert.Equal(new[] { "HOST ONLINE (STEAM)", "JOIN BY CODE (STEAM)", "HOST DIRECT IP", "JOIN DIRECT IP", "BACK" }, r.F.CurrentLabels);
        Assert.Equal(new[] { false, false, true, true, true }, r.F.CurrentEnabled);
        r.Press(Buttons.A);                               // disabled: nothing happens
        Assert.Equal(Screen.Online, r.F.Top);
        r.Press(Buttons.B);
        Assert.Equal(Screen.Main, r.F.Top);
    }

    [Fact]
    public void JoinByCodeUsesThePadKeyboard()
    {
        var steam = new FakeSteam { Available = true };
        using var r = new Rig(steam: steam);
        r.Choose("ONLINE"); r.Choose("JOIN BY CODE");
        Assert.Equal(Screen.Keyboard, r.F.Top);
        var kb = r.F.Keyboard!;
        Assert.Equal(OnScreenKeyboard.Mode.LobbyCode, kb.Kind);
        // type K7QX2M with the D-pad and A only
        foreach (char c in "K7QX2M")
        {
            for (int i = 0; i < 100 && kb.Current != c.ToString(); i++)
                r.Press(kb.Rows[kb.Row].Contains(c.ToString()) ? Buttons.DPadRight : Buttons.DPadDown);
            Assert.Equal(c.ToString(), kb.Current);
            r.Press(Buttons.A);
        }
        Assert.Equal("K7QX2M", kb.Text);
        Assert.Equal(OnScreenKeyboard.KeyOk, kb.Current);  // a full code jumps to OK
        r.Press(Buttons.A);
        Assert.Equal(Screen.Connecting, r.F.Top);
        Assert.Equal(OnlineController.Kind.SteamJoin, r.F.Online.Mode);
        r.Tick();                                          // no real Steam behind the fake: reports the error
        Assert.Equal(new[] { "BACK" }, r.F.CurrentLabels);
        Assert.Equal("Steam is not available", r.F.Online.Error);
        r.Press(Buttons.B);
        Assert.Equal(Screen.Online, r.F.Top);
        Assert.False(r.F.Online.Active);
    }

    [Fact]
    public void KeyboardCancelReturnsToTheOnlineMenu()
    {
        using var r = new Rig();
        r.Choose("ONLINE"); r.Choose("JOIN DIRECT IP");
        Assert.Equal(OnScreenKeyboard.Mode.Address, r.F.Keyboard!.Kind);
        r.Type("10.0.0.");
        r.Key(Keys.M); // a letter typed on the keyboard screen is text, not the mute shortcut
        Assert.False(r.F.Settings.Muted);
        r.Press(Buttons.B);
        Assert.Equal(Screen.Online, r.F.Top);
    }

    [Fact]
    public void SteamInviteJumpsToJoining()
    {
        var steam = new FakeSteam { Available = true };
        using var r = new Rig(steam: steam);
        r.F.StartLocal(true);
        steam.Pending = 12345;
        r.Tick();
        Assert.Equal(new[] { Screen.Main, Screen.Online, Screen.Connecting }, r.F.Stack);
        Assert.Null(r.F.LocalSim);
    }

    [Fact]
    public void SteamHostShowsInviteButton()
    {
        var steam = new FakeSteam { Available = true, Code = "ABCDEF" };
        using var r = new Rig(steam: steam);
        r.Choose("ONLINE"); r.Choose("HOST ONLINE"); r.Choose("ARCADE");
        Assert.Equal(Screen.Connecting, r.F.Top);
        // the fake has no SteamPlatform behind OnlineController, so it ends with "Steam is not available"
        r.Tick();
        Assert.Equal(new[] { "BACK" }, r.F.CurrentLabels);
        r.Press(Buttons.A);
        Assert.Equal(Screen.Online, r.F.Top);
    }

    /// <summary>Two front ends on localhost: host via HOST DIRECT IP, guest types the address on the pad keyboard.
    /// The host's auto-fire and canisters reach the guest; pausing online overlays without stopping the game.</summary>
    [Fact]
    public void OnlineDirectIpEndToEndWithPads()
    {
        using var host = new Rig(new Settings { AutoFire = true, Canisters = 4 });
        using var guest = new Rig(new Settings { AutoFire = false, Canisters = 16 });
        host.Choose("ONLINE"); host.Choose("HOST DIRECT IP"); host.Choose("ARCADE");
        Assert.Equal(Screen.Connecting, host.F.Top);
        host.Tick();
        string portLine = host.F.Online.Describe()[0];
        int port = int.Parse(portLine.Split(' ').Last());
        guest.Choose("ONLINE"); guest.Choose("JOIN DIRECT IP");
        guest.Type("127.0.0.1:" + port);
        guest.Key(Keys.Enter);
        Assert.Equal(Screen.Connecting, guest.F.Top);
        for (int i = 0; i < 2000 && !(host.F.Top == Screen.OnlineGame && guest.F.Top == Screen.OnlineGame); i++) { host.Tick(); guest.Tick(); Thread.Sleep(2); }
        Assert.Equal(Screen.OnlineGame, host.F.Top);
        Assert.Equal(Screen.OnlineGame, guest.F.Top);
        var gs = guest.F.Online.Session!;
        Assert.True(gs.Config!.Value.Game.AutoFire);
        Assert.Equal(4, gs.Config.Value.Game.Canisters);
        // guest opens its menu: overlay, the shared game keeps running
        guest.Press(Buttons.Start);
        Assert.Equal(Screen.Pause, guest.F.Top);
        Assert.Equal("LEAVE ONLINE GAME", guest.F.CurrentLabels[2]);
        int f0 = gs.Frame;
        for (int i = 0; i < 200; i++) { host.Tick(); guest.Tick(); Thread.Sleep(2); }
        Assert.True(gs.Frame > f0 + 50, $"frames {f0} → {gs.Frame}");
        guest.Press(Buttons.B);
        Assert.Equal(Screen.OnlineGame, guest.F.Top);
        // guest leaves: guest back at the main menu, host told the game ended
        guest.Press(Buttons.Start); guest.Choose("LEAVE ONLINE GAME");
        Assert.Equal(new[] { Screen.Main }, guest.F.Stack);
        for (int i = 0; i < 200 && host.F.Top != Screen.Connecting; i++) { host.Tick(); Thread.Sleep(2); }
        Assert.Equal(new[] { Screen.Main, Screen.Connecting }, host.F.Stack);
        Assert.Equal(new[] { "BACK" }, host.F.CurrentLabels);
        host.Press(Buttons.A);
        Assert.Equal(new[] { Screen.Main }, host.F.Stack);
    }

    /// <summary>The build version is drawn in the bottom-right corner of the main menu and the pause menu.</summary>
    [Fact]
    public void VersionShowsOnMainMenuAndPause()
    {
        using var r = new Rig();
        Assert.Equal(BuildInfo.Display, FrontEnd.VersionText);
        var with = new List<Line>(); var without = new List<Line>();
        int Corner(List<Line> l) => l.Count(x => x.X1 > Scene.Width - 300 && x.Y1 < 30 && x.Y2 < 30);
        r.F.Build(with);
        Assert.True(Corner(with) > 5, "main menu corner");
        r.Choose("OPTIONS"); r.F.Build(without);
        Assert.Equal(0, Corner(without));               // not on other menus
        r.Press(Buttons.B);
        r.F.StartLocal(true); r.Press(Buttons.Start);
        Assert.Equal(Screen.Pause, r.F.Top);
        r.F.Build(with);
        Assert.True(Corner(with) > 5, "pause corner");
    }

    [Fact]
    public void EveryScreenDrawsInsideTheField()
    {
        using var r = new Rig(steam: new FakeSteam { Available = true, Code = "ABCDEF" });
        var lines = new List<Line>();
        void Check()
        {
            r.F.Build(lines);
            Assert.NotEmpty(lines);
            Assert.All(lines, l => Assert.True(Math.Min(l.X1, l.X2) >= -1 && Math.Max(l.X1, l.X2) <= Scene.Width + 1 && Math.Min(l.Y1, l.Y2) >= -5 && Math.Max(l.Y1, l.Y2) <= Scene.Height + 1,
                $"{r.F.Top}: {l}"));
        }
        Check();
        r.Choose("OPTIONS"); Check(); r.Press(Buttons.B);
        r.Choose("ONLINE"); Check();
        r.Choose("JOIN DIRECT IP"); r.Type("192.168.100.200:47815"); Check(); r.Press(Buttons.B);
        r.Choose("JOIN BY CODE"); Check(); r.Press(Buttons.B);
        r.Press(Buttons.B);
        r.Choose("START 1 PLAYER"); Check(); r.Press(Buttons.B);   // MODE SELECT
        r.F.StartLocal(true); Check(); r.Press(Buttons.Start); Check();
        Fake.AllCanistersGone(r.F.LocalSim!.State);
        r.Press(Buttons.Start); r.Tick(); Check();
    }
}
