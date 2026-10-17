using RipOff.Game;

namespace RipOff.Game.Tests;

public class WindowGeometryTests
{
    static readonly PixelRect Primary = new(0, 0, 1920, 1040);     // 1080p minus a task bar
    static readonly PixelRect Right = new(1920, 0, 2560, 1440);    // second monitor to the right
    static readonly PixelRect[] One = { Primary }, Two = { Primary, Right };

    [Fact]
    public void NoSavedGeometryGivesDefaultCenteredOnPrimary()
    {
        var p = WindowGeometry.Resolve(null, Two);
        Assert.Equal(new WindowGeometry.Placement(1024, 768, ((1920 - 1024) / 2, (1040 - 768) / 2), false), p);
    }

    [Fact]
    public void SavedGeometryOnScreenIsRestoredExactly()
    {
        Assert.Equal(new WindowGeometry.Placement(800, 600, (100, 50), true), WindowGeometry.Resolve(new PixelRect(100, 50, 800, 600), One));
        // ...also on the second monitor.
        Assert.Equal(new WindowGeometry.Placement(1600, 900, (2300, 200), true), WindowGeometry.Resolve(new PixelRect(2300, 200, 1600, 900), Two));
    }

    [Fact]
    public void WindowOnAMonitorThatIsGoneFallsBackToDefault()
    {
        var p = WindowGeometry.Resolve(new PixelRect(2300, 200, 1600, 900), One);
        Assert.False(p.FromSaved);
        Assert.Equal((1024, 768), (p.Width, p.Height));
        Assert.Equal(((1920 - 1024) / 2, (1040 - 768) / 2), p.Position);
    }

    [Theory]
    [InlineData(-5000, -5000, 800, 600)]   // far off every display
    [InlineData(1870, 100, 800, 600)]      // only 50 px showing (< MinVisible)
    [InlineData(100, -590, 800, 600)]      // title bar above the top: can't be grabbed
    [InlineData(100, 100, 100, 100)]       // too small
    [InlineData(100, 100, 0, 600)]
    [InlineData(100, 100, 800, -3)]
    [InlineData(100, 100, 99999, 600)]     // garbage size
    public void OffScreenOrInsaneGeometryFallsBackToDefault(int x, int y, int w, int h)
    {
        var p = WindowGeometry.Resolve(new PixelRect(x, y, w, h), One);
        Assert.False(p.FromSaved);
        Assert.Equal(new WindowGeometry.Placement(1024, 768, (448, 136), false), p);
    }

    [Fact]
    public void PartlyOffScreenButGrabbableIsNudgedFullyOn()
    {
        // 300 px hang off the right edge, 100 off the bottom: still grabbable, so it is kept and pulled on.
        var p = WindowGeometry.Resolve(new PixelRect(1420, 540, 800, 600), One);
        Assert.Equal(new WindowGeometry.Placement(800, 600, (1120, 440), true), p);
        // Spanning both monitors: goes to the one it overlaps most (the right one here).
        var q = WindowGeometry.Resolve(new PixelRect(1800, 100, 1000, 700), Two);
        Assert.Equal(new WindowGeometry.Placement(1000, 700, (1920, 100), true), q);
    }

    [Fact]
    public void BiggerThanTheDisplayIsShrunkToFit()
    {
        // Saved on a big monitor, now on a smaller resolution: clamp the size, keep it on screen.
        var p = WindowGeometry.Resolve(new PixelRect(0, 0, 2400, 1300), One);
        Assert.Equal(new WindowGeometry.Placement(1920, 1040, (0, 0), true), p);
    }

    [Fact]
    public void SmallDisplayGetsAScaledDownDefault()
    {
        var p = WindowGeometry.Default(new PixelRect(0, 0, 1024, 600));
        Assert.True(p.Width <= 1024 * 9 / 10 && p.Height <= 600 * 9 / 10);
        Assert.Equal(4.0 / 3, (double)p.Width / p.Height, 2);
        Assert.Equal(((1024 - p.Width) / 2, (600 - p.Height) / 2), p.Position);
    }

    [Fact]
    public void UnknownDisplaysTrustASaneSizeAndLetTheOsPlaceIt()
    {
        Assert.Equal(new WindowGeometry.Placement(800, 600, null, true), WindowGeometry.Resolve(new PixelRect(-9999, 5, 800, 600), null));
        Assert.Equal(new WindowGeometry.Placement(1024, 768, null, false), WindowGeometry.Resolve(new PixelRect(0, 0, 5, 5), new PixelRect[0]));
        Assert.Equal(new WindowGeometry.Placement(1024, 768, null, false), WindowGeometry.Resolve(null, null));
    }

    static string TempDir() => Directory.CreateTempSubdirectory("ripoff-window").FullName;

    [Fact]
    public void WindowGeometryRoundTripsThroughSettingsJson()
    {
        var store = new SettingsStore(TempDir());
        var s = new Settings { WindowX = -1200, WindowY = 33, WindowWidth = 1280, WindowHeight = 720, WindowMaximized = true };
        Assert.Null(store.Save(s));
        var t = new SettingsStore(Path.GetDirectoryName(store.FilePath)).Load(out var warn);
        Assert.Null(warn);
        Assert.Equal(new PixelRect(-1200, 33, 1280, 720), t.WindowRect);
        Assert.True(t.WindowMaximized);
        Assert.Contains("\"WindowWidth\": 1280", File.ReadAllText(store.FilePath));
        Assert.DoesNotContain("WindowRect", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void OldSettingsFileWithoutWindowFieldsGivesDefaultPlacement()
    {
        var dir = TempDir();
        var store = new SettingsStore(dir);
        File.WriteAllText(store.FilePath, "{\"Version\": 2, \"Canisters\": 8, \"Fullscreen\": false}");
        var s = store.Load(out var warn);
        Assert.Null(warn);
        Assert.Null(s.WindowRect);
        Assert.False(s.WindowMaximized);
        Assert.False(WindowGeometry.Resolve(s.WindowRect, One).FromSaved);
        // A partial rect (hand-edited) is treated as missing.
        File.WriteAllText(store.FilePath, "{\"WindowX\": 10, \"WindowY\": 10, \"WindowWidth\": 900}");
        Assert.Null(store.Load(out _).WindowRect);
    }

    [Fact]
    public void CloneKeepsWindowGeometry()
    {
        var s = new Settings { WindowX = 1, WindowY = 2, WindowWidth = 640, WindowHeight = 480, WindowMaximized = true };
        var c = s.Clone();
        Assert.Equal(s.WindowRect, c.WindowRect);
        Assert.True(c.WindowMaximized);
    }
}
