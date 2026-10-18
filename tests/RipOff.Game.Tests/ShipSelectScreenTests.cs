using System.Text.Json;
using Microsoft.Xna.Framework.Input;
using RipOff.Core;
using RipOff.Game;
using Screen = RipOff.Game.FrontEnd.Screen;

namespace RipOff.Game.Tests;

/// <summary>SHIP SELECT screen (NOTES §30): after ENHANCED with SHIP SELECT on, each player picks a ship; no two players
/// on the same ship; remembered in settings.json; online the host resolves the guest's wish.</summary>
public class ShipSelectScreenTests
{
    [Fact]
    public void TwoPlayersPickInTurnAndCannotShareAShip()
    {
        var s = new Settings();
        using var r = new Rig(s);
        r.Choose("START 2 PLAYERS"); r.Choose(Branding.ModeEnhanced);
        Assert.Equal(Screen.ShipSelect, r.F.Top);
        Assert.Equal("SHIP SELECT - PLAYER 1 SHIP", r.F.Title);
        Assert.Equal(new[] { "CLASSIC 1", "CLASSIC 2", "BACK" }, r.F.CurrentLabels);
        Assert.Equal(0, r.F.Selected);                                      // last pick (default CLASSIC 1)
        Assert.Contains("THE ARCADE'S PLAYER 1 SHIP", r.F.Notes);
        Assert.Contains("PLAYER 2 PICKS NEXT - NO TWO PLAYERS ON THE SAME SHIP", r.F.Notes);
        r.Choose("CLASSIC 2");
        Assert.Equal(Screen.ShipSelect, r.F.Top);
        Assert.Equal("SHIP SELECT - PLAYER 2 SHIP", r.F.Title);
        Assert.Equal(new[] { "CLASSIC 1", "CLASSIC 2 - PLAYER 1", "BACK" }, r.F.CurrentLabels);
        Assert.Equal(new[] { true, false, true }, r.F.CurrentEnabled);
        Assert.Equal(0, r.F.Selected);                                      // P2 moved off P1's ship
        Assert.Contains("PLAYER 1 HAS CLASSIC 2", r.F.Notes);
        r.Press(Buttons.DPadDown); r.Press(Buttons.A);                      // the taken ship: nothing happens
        Assert.Equal(Screen.ShipSelect, r.F.Top);
        r.Choose("CLASSIC 1");
        Assert.Equal(Screen.LocalGame, r.F.Top);
        Assert.Equal(new[] { Ships.Classic2, Ships.Classic1 }, r.F.LocalSim!.State.Ships);
        Assert.Equal((Ships.Classic2, Ships.Classic1), (s.Ship1, s.Ship2)); // saved
        // PLAY AGAIN keeps the ships, no second pick
        Fake.AllCanistersGone(r.F.LocalSim.State); r.Tick();
        r.Ticks(FrontEnd.GameOverLockTicks); r.Choose("PLAY AGAIN");
        Assert.Equal(Screen.LocalGame, r.F.Top);
        Assert.Equal(new[] { Ships.Classic2, Ships.Classic1 }, r.F.LocalSim!.State.Ships);
        // next time the last picks are highlighted
        r.Press(Buttons.Start); r.Choose("QUIT TO MAIN MENU");
        r.Choose("START 2 PLAYERS"); r.Choose(Branding.ModeEnhanced);
        Assert.Equal(1, r.F.Selected);
    }

    [Fact]
    public void BackStepsToPlayerOneThenLeaves()
    {
        using var r = new Rig();
        r.Choose("START 2 PLAYERS"); r.Choose(Branding.ModeEnhanced); r.Choose("CLASSIC 1");
        Assert.Equal("SHIP SELECT - PLAYER 2 SHIP", r.F.Title);
        r.Press(Buttons.B);
        Assert.Equal("SHIP SELECT - PLAYER 1 SHIP", r.F.Title);
        Assert.Equal(0, r.F.Selected);
        r.Key(Keys.Escape);
        Assert.Equal(Screen.Main, r.F.Top);
        Assert.Null(r.F.LocalSim);
        r.Choose("START 2 PLAYERS"); r.Choose(Branding.ModeEnhanced); r.Choose("BACK");
        Assert.Equal(Screen.Main, r.F.Top);
    }

    [Fact]
    public void OnePlayerPicksOnlyTheirShipAndKeyboardWorks()
    {
        var s = new Settings();
        using var r = new Rig(s);
        r.Key(Keys.Enter); r.Key(Keys.Down); r.Key(Keys.Enter);              // START 1 PLAYER, ENHANCED
        Assert.Equal("SHIP SELECT - YOUR SHIP", r.F.Title);
        r.Key(Keys.Down); r.Key(Keys.Enter);                                // CLASSIC 2
        Assert.Equal(Screen.LocalGame, r.F.Top);
        Assert.Equal(Ships.Classic2, r.F.LocalSim!.State.Ships[0]);
        Assert.Equal((Ships.Classic2, Ships.Classic1), (s.Ship1, s.Ship2)); // P2's saved ship moved off it
    }

    [Fact]
    public void ArcadeAndShipSelectOffSkipTheScreenAndFlyTheArcadeShips()
    {
        var s = new Settings { Ship1 = Ships.Classic2, Ship2 = Ships.Classic1 };
        using var r = new Rig(s);
        r.Choose("START 2 PLAYERS"); r.Choose(Branding.ModeArcade);
        Assert.Equal(Screen.LocalGame, r.F.Top);
        Assert.Equal(new[] { Ships.Classic1, Ships.Classic2 }, r.F.LocalSim!.State.Ships);
        r.Press(Buttons.Start); r.Choose("QUIT TO MAIN MENU");
        s.EnhancedOptions = EnhancedOption.All & ~EnhancedOption.ShipSelect;
        r.Choose("START 2 PLAYERS"); r.Choose(Branding.ModeEnhanced);
        Assert.Equal(Screen.LocalGame, r.F.Top);
        Assert.Equal(new[] { Ships.Classic1, Ships.Classic2 }, r.F.LocalSim!.State.Ships);
    }

    [Fact]
    public void SettingsKeepShipsKnownAndDifferent()
    {
        Assert.Equal((0, 1), (new Settings().Ship1, new Settings().Ship2));
        Assert.Equal((1, 0), Pair(new Settings { Ship1 = 1, Ship2 = 1 }.Normalize()));
        Assert.Equal((0, 1), Pair(new Settings { Ship1 = 0, Ship2 = 0 }.Normalize()));
        Assert.Equal((0, 1), Pair(new Settings { Ship1 = 9, Ship2 = -3 }.Normalize()));
        Assert.Equal((1, 0), Pair(new Settings { Ship1 = 1, Ship2 = 7 }.Normalize()));
        var dir = Directory.CreateTempSubdirectory("ripoff-ships").FullName;
        var store = new SettingsStore(dir);
        Assert.Null(store.Save(new Settings { Ship1 = 1, Ship2 = 0 }));
        Assert.Equal((1, 0), Pair(new SettingsStore(dir).Load(out _)));
        File.WriteAllText(store.FilePath, "{\"Canisters\": 8}"); // older file: arcade pairing
        Assert.Equal((0, 1), Pair(store.Load(out _)));
        var g = new Settings { Mode = PlayMode.Enhanced, Ship1 = 1, Ship2 = 0 }.GameConfig;
        Assert.Equal((1, 0), (g.Ship1, g.Ship2));
        static (int, int) Pair(Settings x) => (x.Ship1, x.Ship2);
    }

    [Fact]
    public void OnlineTheHostKeepsItsShipAndTheGuestGetsTheOther()
    {
        using var host = new Rig(new Settings());
        using var guest = new Rig(new Settings());
        host.Choose("ONLINE"); host.Choose("HOST DIRECT IP"); host.Choose(Branding.ModeEnhanced);
        Assert.Equal("SHIP SELECT - YOUR SHIP", host.F.Title);
        Assert.Contains("YOU ARE PLAYER 1 - THE OTHER PLAYER GETS A DIFFERENT SHIP", host.F.Notes);
        host.Choose("CLASSIC 2");
        Assert.Equal(Screen.Connecting, host.F.Top);
        host.Tick();
        int port = int.Parse(host.F.Online.Describe()[0].Split(' ').Last());
        guest.Choose("ONLINE"); guest.Choose("JOIN DIRECT IP");
        Assert.Equal("SHIP SELECT - YOUR SHIP", guest.F.Title);
        Assert.Contains("USED IF THE HOST PLAYS ENHANCED WITH SHIP SELECT ON", guest.F.Notes);
        guest.Choose("CLASSIC 2");                                          // the host's ship
        guest.Type("127.0.0.1:" + port); guest.Key(Keys.Enter);
        for (int i = 0; i < 2000 && !(host.F.Top == Screen.OnlineGame && guest.F.Top == Screen.OnlineGame); i++) { host.Tick(); guest.Tick(); Thread.Sleep(2); }
        Assert.Equal(Screen.OnlineGame, guest.F.Top);
        foreach (var x in new[] { host, guest })
            Assert.Equal(new[] { Ships.Classic2, Ships.Classic1 }, x.F.Online.Session!.Sim!.State.Ships);
        Assert.Equal("SHIPS: P1 CLASSIC 2 - P2 CLASSIC 1 (YOU ARE P2) - YOUR PICK IS THE HOST'S SHIP", guest.F.Online.ShipsText());
        Assert.Equal("SHIPS: P1 CLASSIC 2 - P2 CLASSIC 1 (YOU ARE P1)", host.F.Online.ShipsText());
        Assert.Equal(Ships.Classic2, guest.S.Ship1);                        // the guest's own pick is kept for next time
    }

    [Fact]
    public void GuestWithShipSelectOffJoinsStraightAwayAndSendsItsLastPick()
    {
        var gs = new Settings { EnhancedOptions = EnhancedOption.All & ~EnhancedOption.ShipSelect, Ship1 = Ships.Classic2, Ship2 = Ships.Classic1 };
        using var host = new Rig(new Settings());
        using var guest = new Rig(gs);
        host.Choose("ONLINE"); host.Choose("HOST DIRECT IP"); host.Choose(Branding.ModeEnhanced); host.Choose("CLASSIC 1");
        host.Tick();
        int port = int.Parse(host.F.Online.Describe()[0].Split(' ').Last());
        guest.Choose("ONLINE"); guest.Choose("JOIN DIRECT IP");
        Assert.Equal(Screen.Keyboard, guest.F.Top);
        guest.Type("127.0.0.1:" + port); guest.Key(Keys.Enter);
        for (int i = 0; i < 2000 && !(host.F.Top == Screen.OnlineGame && guest.F.Top == Screen.OnlineGame); i++) { host.Tick(); guest.Tick(); Thread.Sleep(2); }
        Assert.Equal(Screen.OnlineGame, guest.F.Top);
        // the host's toggles decide: SHIP SELECT is on there, so the guest's wish (CLASSIC 2) is flown
        Assert.Equal(new[] { Ships.Classic1, Ships.Classic2 }, guest.F.Online.Session!.Sim!.State.Ships);
        Assert.True(guest.F.Online.Session.Sim.Rules.ShipSelect);
    }
}
