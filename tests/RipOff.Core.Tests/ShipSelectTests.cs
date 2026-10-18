using RipOff.Core;
using RipOff.Core.Shapes;

namespace RipOff.Core.Tests;

/// <summary>SHIP SELECT (NOTES §30): the ships in the game config, used only in Enhanced with the toggle on.</summary>
public class ShipSelectTests
{
    static readonly GameConfig Swapped = new(8, false, PlayMode.Enhanced, EnhancedOption.All, Ships.Classic2, Ships.Classic1);

    [Fact]
    public void RosterIsTheTwoArcadeShipsWithStableIds()
    {
        Assert.Equal(2, Ships.Count);
        Assert.Equal((0, "CLASSIC 1", 0), (Ships.All[0].Id, Ships.All[0].Name, Ships.All[0].ShapeSet));
        Assert.Equal((1, "CLASSIC 2", 1), (Ships.All[1].Id, Ships.All[1].Name, Ships.All[1].ShapeSet));
        for (int i = 0; i < Ships.Count; i++) Assert.Equal(i, Ships.All[i].Id);
        Assert.Equal(Ships.Classic1, Ships.Default(0));
        Assert.Equal(Ships.Classic2, Ships.Default(1));
        Assert.Equal(1, Ships.FirstOtherThan(0));
        Assert.Equal(0, Ships.FirstOtherThan(1));
        Assert.False(Ships.IsDefined(-1)); Assert.False(Ships.IsDefined(2));
    }

    [Fact]
    public void ToggleIsEnhancedOnlyAndDefaultOn()
    {
        Assert.False(GameRules.Arcade.ShipSelect);
        Assert.True(GameRules.Enhanced.ShipSelect);
        Assert.False(GameRules.For(PlayMode.Enhanced, EnhancedOption.All & ~EnhancedOption.ShipSelect).ShipSelect);
        Assert.False(GameRules.For(PlayMode.Arcade, EnhancedOption.All).ShipSelect);
        Assert.Contains(GameRules.Variations, v => v.Option == EnhancedOption.ShipSelect && v.Label == "SHIP SELECT" && v.Requires == EnhancedOption.None);
        Assert.Equal(64, (int)EnhancedOption.ShipSelect);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PickedShipsAreFlownOnlyInEnhancedWithTheToggleOn(bool onePlayer)
    {
        var sim = new Simulation(); sim.StartGame(onePlayer, Swapped);
        Assert.Equal(new[] { Ships.Classic2, Ships.Classic1 }, sim.State.Ships);
        sim = new Simulation(); sim.StartGame(onePlayer, Swapped with { Options = EnhancedOption.All & ~EnhancedOption.ShipSelect });
        Assert.Equal(new[] { Ships.Classic1, Ships.Classic2 }, sim.State.Ships);
        sim = new Simulation(); sim.StartGame(onePlayer, Swapped with { Mode = PlayMode.Arcade });
        Assert.Equal(new[] { Ships.Classic1, Ships.Classic2 }, sim.State.Ships);
        Assert.Equal((Ships.Classic2, Ships.Classic1), Swapped.EffectiveShips);
        Assert.Equal((Ships.Classic1, Ships.Classic2), (Swapped with { Mode = PlayMode.Arcade }).EffectiveShips);
    }

    [Fact]
    public void ArcadeIgnoresTheShipsCompletely()
    {
        // Arcade must stay frame-identical to arcade-v1.0: the ships in the config change nothing, hash included.
        var a = new GameConfig(8, false, PlayMode.Arcade);
        Assert.Equal(ArcadeDeterminismTests.Run(3, false, a), ArcadeDeterminismTests.Run(3, false, a with { Ship1 = 1, Ship2 = 0 }));
    }

    [Fact]
    public void ShipsAreHashedOnlyWithTheToggleOn()
    {
        var on = new GameConfig(8, false, PlayMode.Enhanced, EnhancedOption.All);
        Assert.NotEqual(ArcadeDeterminismTests.Run(2, false, on), ArcadeDeterminismTests.Run(2, false, Swapped));
        var off = on with { Options = EnhancedOption.All & ~EnhancedOption.ShipSelect };
        Assert.Equal(ArcadeDeterminismTests.Run(2, false, off), ArcadeDeterminismTests.Run(2, false, off with { Ship1 = 1, Ship2 = 0 }));
    }

    [Fact]
    public void SceneDrawsEachPlayerWithItsShipsShape()
    {
        var sim = new Simulation(); sim.StartGame(false, Swapped with { Options = EnhancedOption.All & ~EnhancedOption.WarpIn });
        for (int i = 0; i < 3; i++) sim.Step(default);
        var s = sim.State;
        Assert.False(EnemyState.IsFree(s.Players[0].A));
        var lines = Scene.Build(s);
        static List<Line> Ship(int id, TankRecord r) { var l = new List<Line>(); Scene.DrawShip(id, r.X, r.Y, r.Heading, r.Scale, l); return l; }
        Assert.All(Ship(Ships.Classic2, s.Players[0]), l => Assert.Contains(l, lines)); // P1 flies CLASSIC 2 (set 1)
        Assert.All(Ship(Ships.Classic1, s.Players[1]), l => Assert.Contains(l, lines));
        Assert.DoesNotContain(Ship(Ships.Classic1, s.Players[0])[0], lines);
        // Default pairing draws the arcade sets as before.
        sim = new Simulation(); sim.StartGame(false, new GameConfig(8)); for (int i = 0; i < 3; i++) sim.Step(default);
        lines = Scene.Build(sim.State);
        Assert.All(Ship(Ships.Classic1, sim.State.Players[0]), l => Assert.Contains(l, lines));
    }

    [Fact]
    public void WarpInOutlineIsThePickedShip()
    {
        var sim = new Simulation(); sim.StartGame(true, Swapped);
        var s = sim.State;
        while (s.WarpTimer[0] == 0 || s.WarpTimer[0] > s.Rules.WarpInPasses / 4) sim.Step(default);
        var l = new List<Line>(); Scene.DrawWarp(s, 0, l);
        // the outline's first segment is the CLASSIC 2 body (set 1), dimmed
        var want = new List<Line>();
        int scale = (int)(GameData.PlayerScale * ((1f - s.WarpTimer[0] / (float)s.Rules.WarpInPasses) - 0.5f) * 2);
        Scene.DrawShip(Ships.Classic2, s.WarpX[0], s.WarpY[0], s.WarpHeading[0], scale, want, bright: false);
        Assert.Contains(want[0], l);
        Assert.Equal(ShapeData.Body[1].Length, l.Count(x => want.Take(ShapeData.Body[1].Length).Contains(x)));
    }
}
