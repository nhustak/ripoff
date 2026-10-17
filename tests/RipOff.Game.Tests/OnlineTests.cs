using RipOff.Core;
using RipOff.Game;
using RipOff.Game.Steam;
using RipOff.Net;

namespace RipOff.Game.Tests;

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
            Assert.Equal(OnScreenKeyboard.CodeLength, c.Length);
            Assert.All(c, ch => Assert.Contains(ch, OnScreenKeyboard.CodeAlphabet));
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
