using Microsoft.Xna.Framework.Input;
using RipOff.Core;
using RipOff.Game;

namespace RipOff.Game.Tests;

public class InputReaderTests
{
    static (UiInput ui, PlayInput play) Read(InputReader r, GamePadState pad, int index = 0, KeyboardState? k = null)
    {
        var pads = new GamePadState[InputReader.MaxPads]; pads[index] = pad;
        return r.Read(k ?? new KeyboardState(), pads);
    }

    [Fact]
    public void PadEdgesNeedTheSecondTickAfterConnecting()
    {
        var r = new InputReader();
        Assert.False(Read(r, Fake.Pad(Buttons.A)).ui.Accept);   // connect tick with A held: no edge
        Assert.False(Read(r, Fake.Pad(Buttons.A)).ui.Accept);   // still held
        Assert.False(Read(r, Fake.Pad()).ui.Accept);
        Assert.True(Read(r, Fake.Pad(Buttons.A)).ui.Accept);    // press
        var u = Read(r, Fake.Pad(Buttons.B | Buttons.Start | Buttons.X | Buttons.DPadDown)).ui;
        Assert.True(u.Back && u.Start && u.Delete && u.Down && u.Any);
        Assert.False(u.Escape);                                  // B is not Esc
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void AnyPadIndexWorks(int index)
    {
        var r = new InputReader();
        Read(r, Fake.Pad(), index);
        Assert.True(Read(r, Fake.Pad(Buttons.DPadUp), index).ui.Up);
        Assert.True(Read(r, Fake.Pad(Buttons.A), index).play.All.Thrust);
    }

    [Fact]
    public void StickGivesOneMenuStepPerPush()
    {
        var r = new InputReader();
        Read(r, Fake.Pad());
        Assert.False(Read(r, Fake.Pad(stickX: 0.4f)).ui.Right);   // below the menu threshold
        Assert.True(Read(r, Fake.Pad(stickX: 0.8f)).ui.Right);
        Assert.False(Read(r, Fake.Pad(stickX: 0.9f)).ui.Right);   // held
        Read(r, Fake.Pad());
        Assert.True(Read(r, Fake.Pad(stickY: 0.8f)).ui.Up);       // +Y is up
        Assert.True(Read(r, Fake.Pad(stickY: -0.8f)).ui.Down);
    }

    [Fact]
    public void KeyboardEdges()
    {
        var r = new InputReader();
        var u = r.Read(new KeyboardState(Keys.Escape, Keys.Enter, Keys.F10), ReadOnlySpan<GamePadState>.Empty).ui;
        Assert.True(u.Back && u.Escape && u.Enter && u.Options && u.Select && u.Any);
        u = r.Read(new KeyboardState(Keys.Escape, Keys.Enter, Keys.F10), ReadOnlySpan<GamePadState>.Empty).ui;
        Assert.False(u.Back || u.Enter || u.Options || u.Any);
    }

    [Fact]
    public void PlayMappingOnePlayerAndTwoPlayers()
    {
        var r = new InputReader();
        var pads = new[] { Fake.Pad(Buttons.X), Fake.Pad(Buttons.A), default, default };
        var p = r.Read(new KeyboardState(Keys.Left, Keys.D), pads).play;
        Assert.True(p.P1.Left && p.P1.Fire && !p.P1.Right && !p.P1.Thrust);   // arrows + first pad
        Assert.True(p.P2.Right && p.P2.Thrust && !p.P2.Fire);                 // WASD + second pad
        Assert.True(p.All.Left && p.All.Right && p.All.Fire && p.All.Thrust);  // everything for 1P/online
    }
}

public class SettingsTests
{
    static string TempDir() => Directory.CreateTempSubdirectory("ripoff-settings").FullName;

    [Fact]
    public void MissingFileGivesDefaults()
    {
        var store = new SettingsStore(TempDir());
        var s = store.Load(out var warn);
        Assert.Null(warn);
        Assert.False(s.AutoFire); Assert.Equal(8, s.Canisters); Assert.Equal(1.0, s.Volume); Assert.False(s.Muted);
        Assert.False(s.Fullscreen); Assert.Equal(PadMapper.DefaultStickDeadzone, s.StickDeadzone); Assert.False(s.SteamAtLaunch);
    }

    [Fact]
    public void SaveAndLoadRoundTrip()
    {
        var store = new SettingsStore(TempDir());
        var s = new Settings { AutoFire = true, Canisters = 12, Volume = 0.3, Muted = true, Fullscreen = true, StickDeadzone = 0.5f, SteamAtLaunch = true };
        Assert.Null(store.Save(s));
        var t = new SettingsStore(Path.GetDirectoryName(store.FilePath)).Load(out var warn);
        Assert.Null(warn);
        Assert.Equal((true, 12, 0.3, true, true, 0.5f, true), (t.AutoFire, t.Canisters, Math.Round(t.Volume, 3), t.Muted, t.Fullscreen, t.StickDeadzone, t.SteamAtLaunch));
        Assert.Equal(new GameConfig(12, true), t.GameConfig);
        Assert.Contains("\"AutoFire\": true", File.ReadAllText(store.FilePath));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{\"Volume\": \"loud\"}")]
    [InlineData("[1,2,3]")]
    public void CorruptFileFallsBackToDefaultsAndIsKept(string content)
    {
        var dir = TempDir();
        var store = new SettingsStore(dir);
        File.WriteAllText(store.FilePath, content);
        var s = store.Load(out var warn);
        Assert.NotNull(warn);
        Assert.Equal(8, s.Canisters);
        Assert.False(File.Exists(store.FilePath));
        Assert.Equal(content, File.ReadAllText(store.FilePath + ".corrupt"));
        Assert.Null(store.Save(s));                         // and the next save works
        Assert.Equal(2, new SettingsStore(dir).Load(out var w2).Version);
        Assert.Null(w2);
    }

    [Fact]
    public void OutOfRangeValuesAreClamped()
    {
        var store = new SettingsStore(TempDir());
        File.WriteAllText(store.FilePath, "{\"Canisters\": 7, \"Volume\": 5, \"StickDeadzone\": 2, \"UnknownField\": 1}");
        var s = store.Load(out var warn);
        Assert.Null(warn);
        Assert.Equal(8, s.Canisters); Assert.Equal(1.0, s.Volume); Assert.Equal(Settings.DeadzoneMax, s.StickDeadzone, 3);
    }

    [Fact]
    public void UnwritableLocationReportsInsteadOfThrowing()
    {
        var dir = TempDir();
        var blocker = Path.Combine(dir, "file");
        File.WriteAllText(blocker, "x");
        var store = new SettingsStore(Path.Combine(blocker, "sub")); // a directory under a file cannot exist
        Assert.NotNull(store.Save(new Settings()));
        Assert.Null(store.Load(out var warn).GameConfig.Validate());
    }

    [Fact]
    public void DefaultLocationIsUnderApplicationData()
    {
        var store = new SettingsStore();
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), store.FilePath);
        Assert.EndsWith(Path.Combine(Branding.SettingsFolder, "settings.json"), store.FilePath);
    }
}

public class OnScreenKeyboardTests
{
    static UiInput U(bool up = false, bool down = false, bool left = false, bool right = false, bool a = false, bool b = false, bool x = false, bool enter = false, string typed = "") =>
        new() { Up = up, Down = down, Left = left, Right = right, Accept = a, Back = b, Delete = x, Enter = enter, Typed = typed };

    [Fact]
    public void CodeLayoutHasTheAlphabetAndActionKeys()
    {
        var k = new OnScreenKeyboard(OnScreenKeyboard.Mode.LobbyCode);
        var all = k.Rows.SelectMany(r => r).ToList();
        foreach (char c in OnScreenKeyboard.CodeAlphabet) Assert.Contains(c.ToString(), all);
        Assert.Contains(OnScreenKeyboard.KeyDel, all); Assert.Contains(OnScreenKeyboard.KeyOk, all); Assert.Contains(OnScreenKeyboard.KeyCancel, all);
        Assert.DoesNotContain("0", all); Assert.DoesNotContain("O", all);
    }

    [Fact]
    public void NavigationWrapsAndAPressesKeys()
    {
        var k = new OnScreenKeyboard(OnScreenKeyboard.Mode.LobbyCode);
        Assert.Equal("A", k.Current);
        k.Update(U(left: true)); Assert.Equal(k.Rows[0][^1], k.Current); // wraps left
        k.Update(U(up: true)); Assert.Equal(k.Rows.Count - 1, k.Row);    // wraps up to the action row
        k.Update(U(down: true)); Assert.Equal(0, k.Row);
        string under = k.Current;
        k.Update(U(a: true));
        Assert.Equal(under, k.Text);
        k.Update(U(x: true)); Assert.Equal("", k.Text);                  // X deletes
    }

    [Fact]
    public void OkOnlyWithAFullCodeAndTypingWorks()
    {
        var k = new OnScreenKeyboard(OnScreenKeyboard.Mode.LobbyCode);
        Assert.Equal(OnScreenKeyboard.Result.None, k.Press(OnScreenKeyboard.KeyOk));
        Assert.Equal(OnScreenKeyboard.Result.None, k.Update(U(typed: "ab0c", enter: true))); // 0 filtered, too short
        Assert.Equal("ABC", k.Text);
        k.Update(U(typed: "defgh"));
        Assert.Equal("ABCDEF", k.Text);                                   // capped
        Assert.Equal(OnScreenKeyboard.KeyOk, k.Current);                  // cursor jumped to OK
        Assert.Equal(OnScreenKeyboard.Result.Ok, k.Update(U(a: true)));
        Assert.Equal(OnScreenKeyboard.Result.Cancel, k.Update(U(b: true)));
        Assert.True(k.Select(OnScreenKeyboard.KeyCancel));
        Assert.Equal(OnScreenKeyboard.Result.Cancel, k.Update(U(a: true)));
    }

    [Fact]
    public void AddressModeAcceptsIpv4AndIpv6Characters()
    {
        var k = new OnScreenKeyboard(OnScreenKeyboard.Mode.Address);
        k.Update(U(typed: "[FE80::1]:47815 x"));
        Assert.Equal("[fe80::1]:47815x", k.Text);
        Assert.Equal(OnScreenKeyboard.Result.Ok, k.Update(U(enter: true)));
        foreach (var key in new[] { "1", ".", ":", "[", "]" }) Assert.True(k.Select(key), key);
        foreach (char c in "[]:.") Assert.True(VectorFont.Supports(c));
    }
}
