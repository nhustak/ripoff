using System.Diagnostics;
using RipOff.Core;
using RipOff.Net;

namespace RipOff.Net.Tests;

/// <summary>Real UDP sockets on 127.0.0.1 through LiteNetLib (no Steam).</summary>
public class LiteNetTests
{
    const string Key = "test-game";

    static bool Pump(Func<bool> done, Action tick, int timeoutMs = 10000)
    {
        var sw = Stopwatch.StartNew();
        while (!done()) { if (sw.ElapsedMilliseconds > timeoutMs) return false; tick(); Thread.Sleep(2); }
        return true;
    }

    [Fact]
    public void LockstepOverLocalhostUdp()
    {
        using var hostT = LiteNetTransport.Host(Key, 0);
        Assert.NotEqual(TransportState.Closed, hostT.State);
        using var guestT = LiteNetTransport.Join(Key, "127.0.0.1", hostT.LocalPort);
        var host = new LockstepSession(hostT, true, Key, LockstepSession.DefaultConfig(42));
        var guest = new LockstepSession(guestT, false, Key);
        var hh = new Dictionary<int, uint>(); var gh = new Dictionary<int, uint>();
        host.FrameSimulated = (f, s) => hh[f] = StateHash.Compute(s);
        guest.FrameSimulated = (f, s) => gh[f] = StateHash.Compute(s);
        int tick = 0;
        bool ok = Pump(() => host.Frame >= 600 && guest.Frame >= 600, () =>
        {
            var p1 = new PlayerInput { Left = tick % 90 < 20, Thrust = tick % 7 < 4, Fire = tick % 11 < 2 };
            var p2 = new PlayerInput { Right = tick % 70 < 15, Thrust = tick % 5 < 2, Fire = tick % 13 < 2 };
            host.Update(p1); guest.Update(p2); tick++;
        }, 30000);
        Assert.True(ok, $"host {host.Phase} {host.Status} f{host.Frame}, guest {guest.Phase} {guest.Status} f{guest.Frame}");
        for (int f = 0; f < 600; f++) Assert.True(hh[f] == gh[f], $"differs at {f}");
        Assert.True(host.ChecksumsCompared > 10);
        Assert.StartsWith("LAN", hostT.Description);
    }

    [Fact]
    public void WrongGameKeyIsRefusedAtConnect()
    {
        using var hostT = LiteNetTransport.Host(Key, 0);
        using var guestT = LiteNetTransport.Join("other-game", "127.0.0.1", hostT.LocalPort);
        bool closed = Pump(() => guestT.State == TransportState.Closed, () => { hostT.Poll(); guestT.Poll(); }, 15000);
        Assert.True(closed, "guest with a different key should be disconnected");
        Assert.NotEqual(TransportState.Connected, hostT.State);
    }

    [Fact]
    public void BadAddressFailsCleanly()
    {
        using var t = LiteNetTransport.Join(Key, "not a host name !!", 1);
        for (int i = 0; i < 5; i++) t.Poll();
        Assert.NotEqual(TransportState.Connected, t.State);
    }
}
