using RipOff.Core;

namespace RipOff.Core.Tests;

/// <summary>Score HUD layout: P1 flush left and P2 flush right along the top, same in Arcade and Enhanced.</summary>
public class ScoreLayoutTests
{
    static GameState Playing(bool onePlayer, PlayMode mode, int s1, int k1, int s2, int k2)
    {
        var sim = new Simulation();
        sim.StartGame(onePlayer, new GameConfig(8, false, mode));
        var st = sim.State;
        st.Score[0] = s1; st.ScoreThousands[0] = k1; st.Score[1] = s2; st.ScoreThousands[1] = k2;
        return st;
    }

    static List<Line> Scores(GameState st) { var o = new List<Line>(); Scene.DrawScores(st, o); return o; }

    [Theory]
    [InlineData(PlayMode.Arcade, 0, 0, 0, 0)]
    [InlineData(PlayMode.Arcade, 25, 0, 7, 0)]
    [InlineData(PlayMode.Arcade, 100, 0, 1, 0)]
    [InlineData(PlayMode.Enhanced, 999, 0, 120, 0)]
    [InlineData(PlayMode.Enhanced, 5, 12, 0, 3)]
    public void TwoPlayerScoresSitFlushLeftAndRightAtTheTop(PlayMode mode, int s1, int k1, int s2, int k2)
    {
        var st = Playing(false, mode, s1, k1, s2, k2);
        var all = Scores(st);
        var p1 = new List<Line>(); Scene.DrawScores(Playing(true, mode, s1, k1, 0, 0), p1);
        var p2 = all.Skip(p1.Count).ToList();
        Assert.NotEmpty(p2);

        Assert.Equal(Scene.ScoreMargin, p1.Min(l => Math.Min(l.X1, l.X2)), 3);                 // ink starts at the left margin
        Assert.Equal(Scene.Width - Scene.ScoreMargin, p2.Max(l => Math.Max(l.X1, l.X2)), 3);  // last 0 ends at the right margin
        foreach (var l in all)
        {
            Assert.InRange(Math.Min(l.Y1, l.Y2), Scene.ScoreY, Scene.Height - Scene.ScoreMargin);
            Assert.InRange(Math.Max(l.Y1, l.Y2), Scene.ScoreY, Scene.Height - Scene.ScoreMargin);
        }
        Assert.True(p1.Max(l => Math.Max(l.X1, l.X2)) < p2.Min(l => Math.Min(l.X1, l.X2)), "scores overlap");
    }

    [Fact]
    public void OnePlayerShowsOnlyP1TopLeft()
    {
        var o = Scores(Playing(true, PlayMode.Arcade, 420, 0, 999, 0));
        Assert.NotEmpty(o);
        Assert.True(o.Max(l => Math.Max(l.X1, l.X2)) < Scene.Width / 2f);
        Assert.Equal(Scene.ScoreMargin, o.Min(l => Math.Min(l.X1, l.X2)), 3);
    }

    [Fact]
    public void NoScoreOutsideAGame()
    {
        var st = new Simulation().State;
        Assert.Empty(Scores(st));
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(7, 0, 2)]
    [InlineData(999, 0, 4)]
    [InlineData(5, 12, 6)]
    public void DigitCount(int tens, int thousands, int digits) => Assert.Equal(digits, Scene.ScoreDigits(tens, thousands));
}
