using Microsoft.Xna.Framework.Input;
using RipOff.Core;
using RipOff.Game;
using Screen = RipOff.Game.FrontEnd.Screen;

namespace RipOff.Game.Tests;

/// <summary>ENHANCED OPTIONS page (Options → ENHANCED OPTIONS): pad and keyboard, saved, used by Enhanced games only.</summary>
public class EnhancedOptionsPageTests
{
    // The default page: everything ON but SMART RESPAWN (off by default since 2026.10.17.10).
    static readonly string[] Defaults = { "WARP-IN: ON", "ENEMY AIM ERROR: ON", "SLOWER RAMP: ON", "SLOWER TOWING: ON", "RANDOM WARP-IN: ON", "WARP KILLS: ON", "SHIP SELECT: ON", "SHIP ABILITIES: ON", "TANK ENEMIES: ON", "HUNTER ENEMIES: ON", "SAVED!! POPUP: ON", "SMART RESPAWN: OFF", "ALL ON", "BACK" };
    // later default-ON toggles (SMART RESPAWN starts off)
    const EnhancedOption New = EnhancedOption.RandomWarpIn | EnhancedOption.WarpKills | EnhancedOption.ShipSelect | EnhancedOption.ShipAbilities
        | EnhancedOption.TankEnemies | EnhancedOption.HunterEnemies | EnhancedOption.SavedPopup;

    [Fact]
    public void PadTogglesEachRowSavesAndBackReturnsToOptions()
    {
        var s = new Settings();
        using var r = new Rig(s);
        r.Choose("OPTIONS"); r.Choose("ENHANCED OPTIONS: DEFAULT");
        Assert.Equal(Screen.EnhancedOptions, r.F.Top);
        Assert.Equal(Defaults, r.F.CurrentLabels);
        int saves = r.Saves;
        r.Press(Buttons.A); Assert.Equal(EnhancedOption.Defaults & ~EnhancedOption.WarpIn, s.EnhancedOptions);   // A toggles
        r.Press(Buttons.DPadRight); Assert.Equal(EnhancedOption.Defaults, s.EnhancedOptions);                     // right too
        r.Press(Buttons.DPadDown); r.Press(Buttons.DPadLeft);                                                 // left too
        r.Press(Buttons.DPadDown); r.Press(Buttons.A);
        r.Press(Buttons.DPadDown); r.Press(Buttons.A);
        Assert.Equal(EnhancedOption.WarpIn | New, s.EnhancedOptions);
        Assert.Equal(new[] { "WARP-IN: ON", "ENEMY AIM ERROR: OFF", "SLOWER RAMP: OFF", "SLOWER TOWING: OFF", "RANDOM WARP-IN: ON", "WARP KILLS: ON", "SHIP SELECT: ON", "SHIP ABILITIES: ON", "TANK ENEMIES: ON", "HUNTER ENEMIES: ON", "SAVED!! POPUP: ON", "SMART RESPAWN: OFF", "ALL ON", "BACK" }, r.F.CurrentLabels);
        Assert.Equal(new[] { "EnemyAimError", "SlowerRamp", "SlowerTowing" }, s.EnhancedOff);
        Assert.Empty(s.EnhancedOn);
        Assert.True(r.Saves >= saves + 5);
        r.Choose("ALL ON"); Assert.Equal(EnhancedOption.All, s.EnhancedOptions);
        Assert.Equal(new[] { "SmartRespawn" }, s.EnhancedOn);                  // ALL ON turns the default-off one on too
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
        Assert.Equal(EnhancedOption.WarpIn | EnhancedOption.SlowerTowing | New, s.EnhancedOptions);
        r.Key(Keys.F10);                                          // no second Options on top
        Assert.Equal(Screen.EnhancedOptions, r.F.Top);
        r.Key(Keys.Escape);
        Assert.Equal(Screen.Options, r.F.Top);
        Assert.Contains("ENHANCED OPTIONS: OFF: ENEMY AIM ERROR, SLOWER RAMP, SMART RESPAWN", r.F.CurrentLabels);
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

    [Fact]
    public void WarpRowsAreDimmedAndIgnoredWhileWarpInIsOff()
    {
        var s = new Settings();
        using var r = new Rig(s);
        r.Choose("OPTIONS"); r.Choose("ENHANCED OPTIONS: DEFAULT");
        int rnd = Array.IndexOf(Defaults, "RANDOM WARP-IN: ON"), kills = Array.IndexOf(Defaults, "WARP KILLS: ON");
        Assert.True(r.F.CurrentEnabled[rnd] && r.F.CurrentEnabled[kills]);
        // RANDOM WARP-IN off while WARP-IN is on: changeable.
        for (int i = 0; i < rnd; i++) r.Press(Buttons.DPadDown);
        r.Press(Buttons.A);
        Assert.Equal(EnhancedOption.Defaults & ~EnhancedOption.RandomWarpIn, s.EnhancedOptions);
        // WARP-IN off: both rows dim, keep their values, can't be changed, and say why.
        for (int i = 0; i < rnd; i++) r.Press(Buttons.DPadUp);
        r.Press(Buttons.A);
        Assert.False(r.F.CurrentEnabled[rnd]); Assert.False(r.F.CurrentEnabled[kills]);
        Assert.Equal("RANDOM WARP-IN: OFF", r.F.CurrentLabels[rnd]); Assert.Equal("WARP KILLS: ON", r.F.CurrentLabels[kills]);
        var before = s.EnhancedOptions;
        for (int i = 0; i < kills; i++) r.Press(Buttons.DPadDown);
        Assert.Equal(kills, r.F.Selected);                     // still reachable, to read the note
        r.Press(Buttons.A); r.Press(Buttons.DPadLeft); r.Press(Buttons.DPadRight);
        Assert.Equal(before, s.EnhancedOptions);
        Assert.Contains("NEEDS WARP-IN - IGNORED WHILE IT IS OFF", r.F.Notes);
        // The rules ignore them too: no warp at all.
        Assert.Equal(0, GameRules.For(PlayMode.Enhanced, s.EnhancedOptions).WarpInPasses);
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
            Assert.Equal(json.Contains("WarpIn") ? EnhancedOption.Defaults & ~EnhancedOption.WarpIn : EnhancedOption.Defaults, u.EnhancedOptions);
            if (json.Contains("WarpIn")) Assert.Equal(new[] { "WarpIn" }, u.EnhancedOff);
        }
        Assert.Equal(EnhancedOption.Defaults, new Settings().GameConfig.Options);

        // A file from 2026.10.17.3/.4 (before RANDOM WARP-IN / WARP KILLS): its choices kept, the new two ON.
        var old = TempDir();
        File.WriteAllText(Path.Combine(old, "settings.json"), "{ \"Version\": 2, \"EnhancedOff\": [\"SlowerRamp\"] }");
        Assert.Equal(EnhancedOption.Defaults & ~EnhancedOption.SlowerRamp, new SettingsStore(old).Load(out _).EnhancedOptions);
    }

    [Fact]
    public void SmartRespawnStartsOffAndOnlyAnExplicitOnKeepsIt()
    {
        // fresh settings
        Assert.Equal(0, (int)(new Settings().EnhancedOptions & EnhancedOption.SmartRespawn));
        // A 2026.10.17.9 file (SMART RESPAWN defaulted ON; "never touched" and "turned on" both saved as not in
        // EnhancedOff, no EnhancedOn list): reads OFF now, its other choices kept.
        foreach (var json in new[]
        {
            "{ \"Version\": 2, \"Mode\": \"Enhanced\", \"EnhancedOff\": [] }",
            "{ \"Version\": 2, \"EnhancedOff\": [\"SlowerRamp\"] }",
            "{ \"Version\": 2, \"EnhancedOff\": [\"SmartRespawn\"] }",   // turned off on .9: stays off
        })
        {
            var dir = TempDir();
            File.WriteAllText(Path.Combine(dir, "settings.json"), json);
            var u = new SettingsStore(dir).Load(out var w);
            Assert.Null(w);
            var expect = EnhancedOption.Defaults & (json.Contains("SlowerRamp") ? ~EnhancedOption.SlowerRamp : EnhancedOption.All);
            Assert.Equal(expect, u.EnhancedOptions);
            Assert.DoesNotContain("SmartRespawn", u.EnhancedOff);              // normalized away: default-off rows live in EnhancedOn
        }
        // turned ON deliberately (2026.10.17.10+): saved in EnhancedOn and kept across a save/load
        var store = new SettingsStore(TempDir());
        var s = new Settings { EnhancedOptions = EnhancedOption.Defaults | EnhancedOption.SmartRespawn };
        Assert.Equal(new[] { "SmartRespawn" }, s.EnhancedOn);
        Assert.Empty(s.EnhancedOff);
        Assert.Null(store.Save(s));
        Assert.Contains("\"EnhancedOn\"", File.ReadAllText(store.FilePath));
        var back = new SettingsStore(Path.GetDirectoryName(store.FilePath)!).Load(out _);
        Assert.Equal(EnhancedOption.All, back.EnhancedOptions);
        Assert.Equal(EnhancedOption.All, back.GameConfig.Options);
        // and switched off again: nothing in either list for it
        back.EnhancedOptions &= ~EnhancedOption.SmartRespawn;
        Assert.Empty(back.EnhancedOn); Assert.Empty(back.EnhancedOff);
        // a stray default-on name in EnhancedOn is ignored
        var dir2 = TempDir();
        File.WriteAllText(Path.Combine(dir2, "settings.json"), "{ \"EnhancedOn\": [\"WarpIn\", \"SmartRespawn\", \"Nope\"] }");
        var v = new SettingsStore(dir2).Load(out _);
        Assert.Equal(EnhancedOption.All, v.EnhancedOptions);
        Assert.Equal(new[] { "SmartRespawn" }, v.EnhancedOn);
    }
}
