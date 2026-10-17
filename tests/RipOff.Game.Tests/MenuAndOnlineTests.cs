using RipOff.Core;
using RipOff.Game;
using RipOff.Game.Steam;
using RipOff.Net;

namespace RipOff.Game.Tests;

public class MenuTests
{
    static MenuInput Down => new() { Down = true };
    static MenuInput Up => new() { Up = true };
    static MenuInput Select => new() { Select = true };
    static MenuInput Back => new() { Back = true };
    static MenuInput Type(string s) => new() { Typed = s };

    [Fact]
    public void MainMenuHasTheRequiredItems()
    {
        var m = new Menu();
        Assert.Equal(new[] { "LOCAL PLAY", "HOST ONLINE (STEAM)", "JOIN BY CODE (STEAM)", "HOST DIRECT IP (LAN)", "JOIN DIRECT IP", "QUIT" }, m.MainLabels);
    }

    [Fact]
    public void NavigationWrapsAndSelects()
    {
        var m = new Menu { SteamAvailable = true };
        Assert.Equal(MenuAction.LocalPlay, m.Update(Select));
        m.Update(Up); // wraps to QUIT
        Assert.Equal(MenuAction.Quit, m.Update(Select));
        m.Update(Down); m.Update(Down); // LOCAL → HOST ONLINE
        Assert.Equal(MenuAction.HostSteam, m.Update(Select));
        m.Update(Down); m.Update(Down);
        Assert.Equal(MenuAction.HostLan, m.Update(Select));
    }

    [Fact]
    public void SteamItemsAreDisabledWithoutSteam()
    {
        var m = new Menu { SteamAvailable = false };
        m.Update(Down);
        Assert.Equal(MenuAction.None, m.Update(Select));
        m.Update(Down);
        Assert.Equal(MenuAction.None, m.Update(Select));
        Assert.Equal(Menu.Screen.Main, m.Current);
    }

    [Fact]
    public void CodeEntryByKeyboardUppercasesFiltersAndNeedsSixChars()
    {
        var m = new Menu { SteamAvailable = true };
        m.Update(Down); m.Update(Down); m.Update(Select);
        Assert.Equal(Menu.Screen.EnterCode, m.Current);
        m.Update(Type("ab0c"));                     // 0 is not in the code alphabet
        Assert.Equal("ABC", m.Text);
        Assert.Equal(MenuAction.None, m.Update(Select)); // too short
        m.Update(Type("defghij"));                  // capped at 6
        Assert.Equal("ABCDEF", m.Text);
        m.Update(new MenuInput { Backspace = true });
        m.Update(Type("z"));
        Assert.Equal(MenuAction.JoinSteamCode, m.Update(Select));
        Assert.Equal("ABCDEZ", m.EnteredText);
    }

    [Fact]
    public void CodeEntryByPad()
    {
        var m = new Menu { SteamAvailable = true };
        m.Update(Down); m.Update(Down); m.Update(Select);
        m.Update(Up);                                // first char 'A'
        m.Update(Up);                                // 'B'
        for (int i = 0; i < 5; i++) m.Update(new MenuInput { Right = true }); // add five 'A'
        m.Update(Down);                              // last one back-cycles to '9'
        Assert.Equal("BAAAA9", m.Text);
        m.Update(new MenuInput { Left = true });     // delete
        Assert.Equal("BAAAA", m.Text);
        m.Update(Back);
        Assert.Equal(Menu.Screen.Main, m.Current);
    }

    [Fact]
    public void AddressEntry()
    {
        var m = new Menu();
        for (int i = 0; i < 4; i++) m.Update(Down);
        m.Update(Select);
        Assert.Equal(Menu.Screen.EnterAddress, m.Current);
        Assert.Equal(MenuAction.None, m.Update(Select)); // empty
        m.Update(Type("192.168.1.20:47815\r"));
        Assert.Equal(MenuAction.JoinLan, m.Update(Select));
        Assert.Equal("192.168.1.20:47815", m.EnteredText);
    }

    [Fact]
    public void StatusScreenOptions()
    {
        var m = new Menu();
        m.ShowStatus("HOST ONLINE", new[] { "CODE: ABCDEF" }, ("INVITE A STEAM FRIEND", MenuAction.InviteOverlay), ("CANCEL", MenuAction.Cancel));
        Assert.Equal(MenuAction.InviteOverlay, m.Update(Select));
        m.Update(Down);
        m.ShowStatus("HOST ONLINE", new[] { "CODE: ABCDEF", "X joined" }, ("INVITE A STEAM FRIEND", MenuAction.InviteOverlay), ("CANCEL", MenuAction.Cancel));
        Assert.Equal(MenuAction.Cancel, m.Update(Select)); // selection kept across refreshes
        Assert.Equal(MenuAction.Cancel, m.Update(Back));
    }

    [Fact]
    public void MenuDrawsEveryScreenWithinTheField()
    {
        var m = new Menu { SteamAvailable = false, SteamNote = "STEAM NOT AVAILABLE", Footer = "ARROWS" };
        var lines = new List<Line>();
        foreach (var screen in new Action[] { () => m.ShowMain(), () => { for (int i = 0; i < 4; i++) m.Update(Down); m.Update(Select); m.Update(Type("10.0.0.1")); },
                     () => m.ShowStatus("T", new[] { "STEAM NOT AVAILABLE (FAILEDGENERIC: FAILED TO LOAD MODULE /HOME/BOX/.STEAM/SDK64/STEAMCLIENT.SO) - LOCAL AND DIRECT IP PLAY STILL WORK" }, ("OK", MenuAction.Cancel)) })
        {
            m.ShowMain(); screen(); lines.Clear(); m.Build(lines);
            Assert.NotEmpty(lines);
            Assert.All(lines, l => Assert.True(Math.Min(l.X1, l.X2) >= 0 && Math.Max(l.X1, l.X2) <= Scene.Width && Math.Min(l.Y1, l.Y2) >= 0 && Math.Max(l.Y1, l.Y2) <= Scene.Height, $"{l.X1},{l.Y1} {l.X2},{l.Y2}"));
        }
    }
}

public class BrandingAndSteamTests
{
    [Fact]
    public void BrandingIsConsistent()
    {
        Assert.Equal(480u, Branding.SteamAppId);
        Assert.Equal("480", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "steam_appid.txt")).Trim());
        Assert.False(string.IsNullOrWhiteSpace(Branding.LobbyGameKey));
        Assert.NotNull(typeof(Simulation).Assembly.GetType(Branding.ShapeSet));
        foreach (char c in Branding.GameName) Assert.True(c == ' ' || VectorFont.Supports(c), "font lacks " + c);
    }

    [Fact]
    public void LobbyCodesUseTheAlphabet()
    {
        for (int i = 0; i < 200; i++)
        {
            var c = SteamPlatform.NewCode();
            Assert.Equal(Menu.CodeLength, c.Length);
            Assert.All(c, ch => Assert.Contains(ch, Menu.CodeAlphabet));
        }
    }

    [Fact]
    public void ConnectStringParsing()
    {
        Assert.Equal(109775241234567890UL, SteamPlatform.ParseConnect("+connect_lobby 109775241234567890"));
        Assert.Equal(5UL, SteamPlatform.ParseConnect("--foo +connect_lobby 5 bar"));
        Assert.Null(SteamPlatform.ParseConnect("+connect_lobby"));
        Assert.Null(SteamPlatform.ParseConnect("+connect_lobby x"));
        Assert.Null(SteamPlatform.ParseConnect(""));
    }

    /// <summary>The build box has no Steam client: init must fail cleanly and leave the game usable.</summary>
    [Fact]
    public void SteamInitWithoutSteamFailsGracefully()
    {
        using var s = SteamPlatform.Init(Array.Empty<string>());
        Assert.False(s.Available);
        Assert.False(string.IsNullOrWhiteSpace(s.Status));
        s.RunCallbacks(); s.Host(); s.JoinByCode("ABCDEF"); s.OpenInviteOverlay(); s.Leave(); // all no-ops
        Assert.Equal(SteamPlatform.LobbyPhase.None, s.Phase);

        using var oc = new OnlineController(s);
        oc.HostSteam();
        oc.Update(default, false);
        Assert.Equal("Steam is not available", oc.Error);
    }
}

public class OnlineControllerTests
{
    [Theory]
    [InlineData("192.168.1.2", "192.168.1.2", 47815)]
    [InlineData("192.168.1.2:5000", "192.168.1.2", 5000)]
    [InlineData("myhost.lan:1", "myhost.lan", 1)]
    [InlineData("[::1]:6000", "::1", 6000)]
    [InlineData("[::1]", "::1", 47815)]
    [InlineData("fe80::1", "fe80::1", 47815)]
    public void AddressParsing(string s, string host, int port)
    {
        Assert.True(OnlineController.TryParseAddress(s, out var h, out var p));
        Assert.Equal(host, h); Assert.Equal(port, p);
    }

    [Theory]
    [InlineData("")]
    [InlineData("host:")]
    [InlineData("host:99999")]
    [InlineData("[::1")]
    public void BadAddresses(string s) => Assert.False(OnlineController.TryParseAddress(s, out _, out _));

    static void Pump(OnlineController a, OnlineController b, int ticks, Func<int, bool>? until = null)
    {
        for (int i = 0; i < ticks; i++)
        {
            a.Update(new PlayerInput { Fire = i % 7 == 0, Left = i % 50 < 10 }, i == 100);
            b.Update(new PlayerInput { Thrust = i % 3 == 0, Right = i % 40 < 8 }, false);
            if (until != null && until(i)) return;
            Thread.Sleep(2);
        }
    }

    /// <summary>The menu's direct-IP path end to end on localhost: host waits, guest connects, both run in step.</summary>
    [Fact]
    public void DirectIpHostAndJoinOnLocalhostRunInStepAndReportDesync()
    {
        var dir = Directory.CreateTempSubdirectory("ripoff-desync");
        using var host = new OnlineController(null) { ReportDirectory = dir.FullName };
        using var guest = new OnlineController(null) { ReportDirectory = dir.FullName };
        host.HostLan(new GameConfig(12), port: 0); // any free port
        for (int i = 0; i < 20; i++) { host.Update(default, false); Thread.Sleep(5); } // hosts can wait indefinitely
        Assert.Null(host.Error);
        Assert.Null(host.Session);
        int port = int.Parse(host.Describe()[0].Split(' ').Last());
        guest.JoinLan("127.0.0.1:" + port);
        Pump(host, guest, 3000, _ => host.Running && guest.Running && host.Session!.Frame > 300 && guest.Session!.Frame > 300);
        Assert.True(host.Running, host.Session?.Status ?? host.Error);
        Assert.True(guest.Running, guest.Session?.Status ?? guest.Error);
        Assert.True(host.Session!.IsHost);
        Assert.False(guest.Session!.IsHost);
        // both peers simulated identical input streams (checksums compared meanwhile, no desync)
        Assert.Equal(SessionPhase.Running, host.Session.Phase);
        var g = guest.Session.InputsUsed; var h = host.Session.InputsUsed;
        int n = Math.Min(g.Count, h.Count);
        Assert.True(n > 300);
        for (int f = 0; f < n; f++) Assert.Equal(h[f], g[f]);

        host.Session.InjectDesync();
        Pump(host, guest, 3000, _ => host.Session.Phase == SessionPhase.Desynced && guest.Session.Phase == SessionPhase.Desynced && host.DesyncReportPath != null && guest.DesyncReportPath != null);
        Assert.Equal(SessionPhase.Desynced, host.Session.Phase);
        Assert.Equal(SessionPhase.Desynced, guest.Session.Phase);
        Assert.True(File.Exists(host.DesyncReportPath));
        var report = File.ReadAllText(guest.DesyncReportPath!);
        Assert.Contains("DESYNC at frame", report);
        Assert.Contains("guest (P2)", report);
        Assert.Contains("seeds", report);
        Assert.Contains("canisters 12", report);
        Assert.Equal(12, guest.Session.Config!.Value.Game.Canisters); // host's rules reached the guest
        dir.Delete(true);
    }

    [Fact]
    public void JoiningNobodyReportsAnError()
    {
        using var guest = new OnlineController(null);
        guest.JoinLan("127.0.0.1:1"); // nothing listens there
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (guest.Error == null && sw.Elapsed.TotalSeconds < 20) { guest.Update(default, false); Thread.Sleep(10); }
        Assert.NotNull(guest.Error);
        Assert.Contains("DISCONNECT", guest.Describe()[0]);
    }
}
