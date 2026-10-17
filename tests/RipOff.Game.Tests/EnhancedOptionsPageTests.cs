using Microsoft.Xna.Framework.Input;
using RipOff.Core;
using RipOff.Game;
using Screen = RipOff.Game.FrontEnd.Screen;

namespace RipOff.Game.Tests;

/// <summary>ENHANCED OPTIONS page (Options → ENHANCED OPTIONS): pad and keyboard, saved, used by Enhanced games only.</summary>
public class EnhancedOptionsPageTests
{
    static readonly string[] AllOn = { "WARP-IN: ON", "ENEMY AIM ERROR: ON", "SLOWER RAMP: ON", "SLOWER TOWING: ON", "ALL ON", "BACK" };

    [Fact]
    public void PadTogglesEachRowSavesAndBackReturnsToOptions()
    {
        var s = new Settings();
        using var r = new Rig(s);
        r.Choose("OPTIONS"); r.Choose("ENHANCED OPTIONS: ALL ON");
        Assert.Equal(Screen.EnhancedOptions, r.F.Top);
        Assert.Equal(AllOn, r.F.CurrentLabels);
        int saves = r.Saves;
        r.Press(Buttons.A); Assert.Equal(EnhancedOption.All & ~EnhancedOption.WarpIn, s.EnhancedOptions);   // A toggles
        r.Press(Buttons.DPadRight); Assert.Equal(EnhancedOption.All, s.EnhancedOptions);                     // right too
        r.Press(Buttons.DPadDown); r.Press(Buttons.DPadLeft);                                                 // left too
        r.Press(Buttons.DPadDown); r.Press(Buttons.A);
        r.Press(Buttons.DPadDown); r.Press(Buttons.A);
        Assert.Equal(EnhancedOption.WarpIn, s.EnhancedOptions);
        Assert.Equal(new[] { "WARP-IN: ON", "ENEMY AIM ERROR: OFF", "SLOWER RAMP: OFF", "SLOWER TOWING: OFF", "ALL ON", "BACK" }, r.F.CurrentLabels);
        Assert.Equal(new[] { "EnemyAimError", "SlowerRamp", "SlowerTowing" }, s.EnhancedOff);
        Assert.True(r.Saves >= saves + 5);
        r.Choose("ALL ON"); Assert.Equal(EnhancedOption.All, s.EnhancedOptions);
        r.Press(Buttons.B);
        Assert.Equal(Screen.Options, r.F.Top);
        Assert.Contains("ENHANCED OPTIONS: ALL ON", r.F.CurrentLabels);
    }

    [Fact]
    public void KeyboardOnly()
    {
        var s = new Settings();
        using var r = new Rig(s);
        r.Key(Keys.F10);                                          // Options
        Assert.Equal(Screen.Options, r.F.Top);
        for (int i = 0; i < 20 && !r.F.CurrentLabels[r.F.Selected].StartsWith("ENHANCED OPTIONS"); i++) r.Key(Keys.Down);
        r.Key(Keys.Enter);
        Assert.Equal(Screen.EnhancedOptions, r.F.Top);
        r.Key(Keys.Down); r.Key(Keys.Enter);                      // ENEMY AIM ERROR off
        r.Key(Keys.Down); r.Key(Keys.Right);                      // SLOWER RAMP off
        Assert.Equal(EnhancedOption.WarpIn | EnhancedOption.SlowerTowing, s.EnhancedOptions);
        r.Key(Keys.F10);                                          // no second Options on top
        Assert.Equal(Screen.EnhancedOptions, r.F.Top);
        r.Key(Keys.Escape);
        Assert.Equal(Screen.Options, r.F.Top);
        Assert.Contains("ENHANCED OPTIONS: OFF: ENEMY AIM ERROR, SLOWER RAMP", r.F.CurrentLabels);
    }

    [Fact]
    public void EnhancedGamesUseTheTogglesAndArcadeIgnoresThem()
    {
        var s = new Settings { Mode = PlayMode.Enhanced, EnhancedOptions = EnhancedOption.SlowerRamp };
        using var r = new Rig(s);
        r.Choose("START 1 PLAYER");
        Assert.Contains(r.F.Notes, n => n.StartsWith("ENHANCED OPTIONS: OFF: WARP-IN, ENEMY AIM ERROR, SLOWER TOWING"));
        r.Choose(Branding.ModeEnhanced);
        var st = r.F.LocalSim!.State;
        Assert.Equal(EnhancedOption.SlowerRamp, st.Options);
        Assert.Equal(0, st.Rules.WarpInPasses);
        Assert.Equal(2, st.Rules.ClearsPerTypeStep);
        r.Press(Buttons.Start); r.Choose("QUIT TO MAIN MENU");
        r.Choose("START 1 PLAYER"); r.Choose(Branding.ModeArcade);
        Assert.Same(GameRules.Arcade, r.F.LocalSim!.State.Rules);
        // in an Arcade game the page says it doesn't apply
        r.Press(Buttons.Start); r.Choose("OPTIONS"); r.Choose("ENHANCED OPTIONS");
        Assert.Contains("NOT USED IN THIS ARCADE GAME", r.F.Notes);
    }

    static string TempDir() => Directory.CreateTempSubdirectory("ripoff-enh").FullName;

    [Fact]
    public void SavedAndOldFilesDefaultOn()
    {
        var store = new SettingsStore(TempDir());
        var s = new Settings { EnhancedOptions = EnhancedOption.WarpIn | EnhancedOption.SlowerTowing };
        Assert.Null(store.Save(s));
        var t = new SettingsStore(Path.GetDirectoryName(store.FilePath)!).Load(out var warn);
        Assert.Null(warn);
        Assert.Equal(EnhancedOption.WarpIn | EnhancedOption.SlowerTowing, t.EnhancedOptions);
        Assert.Equal(t.EnhancedOptions, t.GameConfig.Options);
        Assert.Contains("\"EnhancedOff\"", File.ReadAllText(store.FilePath));

        foreach (var json in new[]
        {
            "{ \"Version\": 2, \"Mode\": \"Enhanced\" }",                         // before the page existed
            "{ \"EnhancedOff\": null }",
            "{ \"EnhancedOff\": [\"SomethingFromTheFuture\", \"WarpIn\", \"WarpIn\"] }", // unknown names ignored
        })
        {
            var dir = TempDir();
            File.WriteAllText(Path.Combine(dir, "settings.json"), json);
            var u = new SettingsStore(dir).Load(out var w);
            Assert.Null(w);
            Assert.Equal(json.Contains("WarpIn") ? EnhancedOption.All & ~EnhancedOption.WarpIn : EnhancedOption.All, u.EnhancedOptions);
            if (json.Contains("WarpIn")) Assert.Equal(new[] { "WarpIn" }, u.EnhancedOff);
        }
        Assert.Equal(EnhancedOption.All, new Settings().GameConfig.Options);
    }
}
