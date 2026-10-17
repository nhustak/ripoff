using System.Reflection;
using RipOff.Core;

namespace RipOff.Core.Tests;

/// <summary>Enhanced options (GameRules.Variations): each toggle owns knobs, off = Arcade values for exactly those.</summary>
public class EnhancedOptionsTests
{
    static readonly PropertyInfo[] Knobs = typeof(GameRules).GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && p.Name != "EqualityContract").ToArray();

    [Fact]
    public void AllOnIsEnhancedAndAllOffIsArcadeKnobForKnob()
    {
        Assert.Same(GameRules.Enhanced, GameRules.For(PlayMode.Enhanced, EnhancedOption.All));
        Assert.Same(GameRules.Enhanced, GameRules.For(PlayMode.Enhanced));
        Assert.Equal(GameRules.Arcade with { Mode = PlayMode.Enhanced }, GameRules.For(PlayMode.Enhanced, EnhancedOption.None));
        foreach (EnhancedOption o in Enum.GetValues<EnhancedOption>()) Assert.Same(GameRules.Arcade, GameRules.For(PlayMode.Arcade, o));
    }

    [Fact]
    public void EveryVariationRowHasItsOwnBitAndEveryKnobThatDiffersBelongsToARow()
    {
        var bits = GameRules.Variations.Select(v => v.Option).ToArray();
        Assert.Equal(bits.Length, bits.Distinct().Count());
        Assert.Equal(EnhancedOption.All, bits.Aggregate(EnhancedOption.None, (a, b) => a | b));
        Assert.All(GameRules.Variations, v => Assert.Single(Enum.GetValues<EnhancedOption>(), e => e == v.Option));
    }

    [Fact]
    public void SwitchingOneOffRestoresOnlyItsKnobsToArcade()
    {
        var owned = new Dictionary<string, EnhancedOption>();
        foreach (var v in GameRules.Variations)
        {
            var r = GameRules.For(PlayMode.Enhanced, EnhancedOption.All & ~v.Option);
            Assert.Equal(v.Off(GameRules.Enhanced), r);
            int changed = 0;
            foreach (var k in Knobs)
            {
                object? now = k.GetValue(r), enh = k.GetValue(GameRules.Enhanced), arc = k.GetValue(GameRules.Arcade);
                if (Equals(now, enh)) continue;
                Assert.Equal(arc, now);                  // off = the Arcade value
                Assert.False(owned.ContainsKey(k.Name), $"{k.Name} owned by two rows");
                owned[k.Name] = v.Option; changed++;
            }
            Assert.True(changed > 0, $"{v.Label} changes nothing");
        }
        // Every Enhanced knob that differs from Arcade (except the mode) is owned by some row, so ALL OFF reaches Arcade.
        foreach (var k in Knobs)
            if (k.Name != nameof(GameRules.Mode) && !Equals(k.GetValue(GameRules.Enhanced), k.GetValue(GameRules.Arcade)))
                Assert.True(owned.ContainsKey(k.Name), $"{k.Name} differs in Enhanced but no toggle owns it");
        Assert.Equal(EnhancedOption.WarpIn, owned[nameof(GameRules.WarpInPasses)]);
        Assert.Equal(EnhancedOption.EnemyAimError, owned[nameof(GameRules.EnemyHitRadius)]);
        Assert.Equal(EnhancedOption.SlowerRamp, owned[nameof(GameRules.EnemySpeedCapBase)]);
        Assert.Equal(EnhancedOption.SlowerTowing, owned[nameof(GameRules.TowSpeedPercent)]);
    }

    [Fact]
    public void ToggleOffMatchesThePreviousEnhancedKnob()
    {
        // Per toggle, one behaviour check against the 2026.10.17.x knob semantics.
        var noAim = GameRules.For(PlayMode.Enhanced, EnhancedOption.All & ~EnhancedOption.EnemyAimError);
        Assert.Equal(0, noAim.AimErrorAt(1));          // exact aim again
        Assert.Equal(0, noAim.EnemyMissCooldown);
        Assert.Equal(0, noAim.ChaseReactionPasses);
        var noRamp = GameRules.For(PlayMode.Enhanced, EnhancedOption.All & ~EnhancedOption.SlowerRamp);
        Assert.Equal((1, 2, 0), (noRamp.ClearsPerTypeStep, noRamp.FullWaveFromLevel, noRamp.EnemySpeedCapBase));
        Assert.Equal(GameRules.Enhanced.TowSpeedPercent, noRamp.TowSpeedPercent); // others stay Enhanced
        Assert.Equal(100, GameRules.For(PlayMode.Enhanced, EnhancedOption.All & ~EnhancedOption.SlowerTowing).TowSpeedPercent);
    }

    [Fact]
    public void OptionsAreInTheHashOnlyOutsideArcade()
    {
        var a = new GameState(); var b = new GameState();
        b.Options = EnhancedOption.None;
        Assert.Equal(StateHash.Compute(a), StateHash.Compute(b));   // Arcade ignores them
        a.PlayMode = b.PlayMode = PlayMode.Enhanced;
        Assert.NotEqual(StateHash.Compute(a), StateHash.Compute(b));
    }
}
