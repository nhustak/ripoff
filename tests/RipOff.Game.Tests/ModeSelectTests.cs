using Microsoft.Xna.Framework.Input;
using RipOff.Core;
using RipOff.Game;
using Screen = RipOff.Game.FrontEnd.Screen;

namespace RipOff.Game.Tests;

/// <summary>MODE SELECT (ARCADE / ENHANCED) after picking local play or hosting: pad and keyboard, remembered in
/// settings.json, and online the guest adopts the host's mode.</summary>
public class ModeSelectTests
{
    [Fact]
    public void LocalPlayGoesThroughModeSelectWithThePad()
    {
        using var r = new Rig();
        r.Choose("START 1 PLAYER");
        Assert.Equal(Screen.ModeSelect, r.F.Top);
        Assert.Equal(new[] { Branding.ModeArcade, Branding.ModeEnhanced, "BACK" }, r.F.CurrentLabels);
        Assert.Equal(0, r.F.Selected);                         // default: Arcade
        int saves = r.Saves;
        r.Press(Buttons.DPadDown); r.Press(Buttons.A);
        Assert.Equal(Screen.LocalGame, r.F.Top);
        Assert.Equal(PlayMode.Enhanced, r.F.LocalSim!.State.PlayMode);
        Assert.True(r.F.LocalSim.State.OnePlayer);
        Assert.Equal(PlayMode.Enhanced, r.S.Mode);
        Assert.True(r.Saves > saves);                           // remembered right away
        // PLAY AGAIN keeps the mode
        Fake.AllCanistersGone(r.F.LocalSim.State); r.Tick();
        Assert.Equal(Screen.GameOver, r.F.Top);
        r.Ticks(FrontEnd.GameOverLockTicks); r.Choose("PLAY AGAIN");
        Assert.Equal(PlayMode.Enhanced, r.F.LocalSim!.State.PlayMode);
    }

    [Fact]
    public void LastChoiceIsHighlightedAndBackCancels()
    {
        using var r = new Rig(new Settings { Mode = PlayMode.Enhanced });
        r.Choose("START 2 PLAYERS");
        Assert.Equal(Screen.ModeSelect, r.F.Top);
        Assert.Equal(1, r.F.Selected);
        r.Press(Buttons.B);
        Assert.Equal(Screen.Main, r.F.Top);
        Assert.Null(r.F.LocalSim);
        r.Choose("START 2 PLAYERS"); r.Choose("BACK");
        Assert.Equal(Screen.Main, r.F.Top);
        r.Choose("START 2 PLAYERS"); r.Choose(Branding.ModeArcade);
        Assert.Equal(Screen.LocalGame, r.F.Top);
        Assert.False(r.F.LocalSim!.State.OnePlayer);
        Assert.Equal(PlayMode.Arcade, r.F.LocalSim.State.PlayMode);
        Assert.Equal(PlayMode.Arcade, r.S.Mode);
    }

    [Fact]
    public void KeyboardOnly()
    {
        using var r = new Rig();
        r.Key(Keys.Enter);                                      // START 1 PLAYER
        Assert.Equal(Screen.ModeSelect, r.F.Top);
        r.Key(Keys.Escape);
        Assert.Equal(Screen.Main, r.F.Top);
        r.Key(Keys.Enter); r.Key(Keys.Down); r.Key(Keys.Enter);
        Assert.Equal(Screen.LocalGame, r.F.Top);
        Assert.Equal(PlayMode.Enhanced, r.F.LocalSim!.State.PlayMode);
    }

    [Fact]
    public void ModeIsSavedByNameAndBadValuesReadAsArcade()
    {
        var dir = Directory.CreateTempSubdirectory("ripoff-settings").FullName;
        var store = new SettingsStore(dir);
        Assert.Equal(PlayMode.Arcade, store.Load(out _).Mode);  // missing file
        Assert.Null(store.Save(new Settings { Mode = PlayMode.Enhanced }));
        Assert.Contains("\"Enhanced\"", File.ReadAllText(store.FilePath));
        Assert.Equal(PlayMode.Enhanced, new SettingsStore(dir).Load(out var w).Mode);
        Assert.Null(w);
        File.WriteAllText(store.FilePath, "{\"Mode\": 7}");
        Assert.Equal(PlayMode.Arcade, store.Load(out w).Mode);
        Assert.Null(w);
        File.WriteAllText(store.FilePath, "{\"Canisters\": 4}");  // file from before modes
        var old = store.Load(out w);
        Assert.Equal(PlayMode.Arcade, old.Mode); Assert.Equal(4, old.Canisters); Assert.Null(w);
        Assert.Equal(new GameConfig(4, false, PlayMode.Arcade), old.GameConfig);
    }

    [Fact]
    public void OnlineGuestAdoptsTheHostsMode()
    {
        using var host = new Rig(new Settings { Mode = PlayMode.Arcade });
        using var guest = new Rig(new Settings { Mode = PlayMode.Arcade });
        host.Choose("ONLINE"); host.Choose("HOST DIRECT IP");
        Assert.Equal(Screen.ModeSelect, host.F.Top);
        host.Choose(Branding.ModeEnhanced);
        Assert.Equal(Screen.Connecting, host.F.Top);
        Assert.Equal("MODE: " + Branding.ModeEnhanced, host.F.Online.ModeText());
        host.Tick();
        int port = int.Parse(host.F.Online.Describe()[0].Split(' ').Last());
        guest.Choose("ONLINE"); guest.Choose("JOIN DIRECT IP");    // joining has no mode select
        Assert.Equal(Screen.Keyboard, guest.F.Top);
        guest.Type("127.0.0.1:" + port); guest.Key(Keys.Enter);
        Assert.Equal(Screen.Connecting, guest.F.Top);
        Assert.Equal("MODE: SET BY THE HOST", guest.F.Online.ModeText());
        for (int i = 0; i < 2000 && !(host.F.Top == Screen.OnlineGame && guest.F.Top == Screen.OnlineGame); i++) { host.Tick(); guest.Tick(); Thread.Sleep(2); }
        Assert.Equal(Screen.OnlineGame, guest.F.Top);
        Assert.Equal(PlayMode.Enhanced, guest.F.Online.Session!.Sim!.State.PlayMode);
        Assert.Equal(PlayMode.Enhanced, host.F.Online.Session!.Sim!.State.PlayMode);
        Assert.Equal("MODE: " + Branding.ModeEnhanced + " (HOST'S CHOICE)", guest.F.Online.ModeText());
        Assert.Equal(PlayMode.Arcade, guest.S.Mode);              // the guest's own setting is untouched
    }
}
