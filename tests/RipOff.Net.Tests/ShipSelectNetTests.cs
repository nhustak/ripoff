using RipOff.Core;
using RipOff.Net;

namespace RipOff.Net.Tests;

/// <summary>SHIP SELECT online (NOTES §30): the guest's wish travels in Hello, the host resolves both ships into the
/// start config, and both machines fly the same ships.</summary>
public class ShipSelectNetTests
{
    const string Key = "test-game";

    sealed class Tamper : ITransport
    {
        private readonly ITransport _t; private readonly Action<byte[]> _edit;
        public Tamper(ITransport t, Action<byte[]> edit) { _t = t; _edit = edit; }
        public TransportState State => _t.State;
        public string Description => _t.Description;
        public string? Error => _t.Error;
        public void Poll() => _t.Poll();
        public void Send(byte[] data, int length, bool reliable) => _t.Send(data, length, reliable);
        public bool TryReceive(out byte[] packet)
        {
            if (!_t.TryReceive(out packet)) return false;
            if (packet.Length > 0 && packet[0] == Protocol.MsgStart) _edit(packet);
            return true;
        }
        public void Dispose() => _t.Dispose();
    }

    static (LockstepSession host, LockstepSession guest) Run(GameConfig game, int guestShip, Action<byte[]>? tamper = null, int ticks = 600,
        List<uint>? hostHashes = null, List<uint>? guestHashes = null)
    {
        var net = new LoopbackNetwork(9, 25, 10);
        var host = new LockstepSession(net.A, true, Key, LockstepSession.DefaultConfig(4, game: game), clock: () => net.NowMs / 1000);
        ITransport gt = tamper == null ? net.B : new Tamper(net.B, tamper);
        var guest = new LockstepSession(gt, false, Key, clock: () => net.NowMs / 1000) { LocalShip = guestShip };
        if (hostHashes != null) host.FrameSimulated = (f, s) => hostHashes.Add(StateHash.Compute(s));
        if (guestHashes != null) guest.FrameSimulated = (f, s) => guestHashes.Add(StateHash.Compute(s));
        for (int i = 0; i < ticks; i++)
        {
            host.Update(new PlayerInput { Fire = i % 10 < 3, Left = i % 50 < 15 });
            guest.Update(new PlayerInput { Fire = i % 8 < 2, Thrust = i % 9 < 4 });
            net.Advance(1000.0 / 38);
        }
        return (host, guest);
    }

    static GameConfig Enhanced(int ship1, int ship2 = -1, EnhancedOption o = EnhancedOption.All) =>
        new(8, false, PlayMode.Enhanced, o, ship1, ship2 < 0 ? Ships.FirstOtherThan(ship1) : ship2);

    [Fact]
    public void HostResolvesTheGuestsWish()
    {
        var g = Enhanced(Ships.Classic1);
        Assert.Equal(Ships.Classic2, LockstepSession.ResolveShips(g, Ships.Classic2).Ship2);   // free: granted
        Assert.Equal(Ships.Classic2, LockstepSession.ResolveShips(g, Ships.Classic1).Ship2);   // the host's ship: moved
        Assert.Equal(Ships.Classic2, LockstepSession.ResolveShips(g, 77).Ship2);               // unknown: moved
        Assert.Equal(g.Ship2, LockstepSession.ResolveShips(g, Protocol.NoShip).Ship2);         // no wish: host config's
        var h = Enhanced(Ships.Classic2);
        Assert.Equal(Ships.Classic1, LockstepSession.ResolveShips(h, Ships.Classic2).Ship2);
        Assert.Equal(Ships.Classic1, LockstepSession.ResolveShips(h, Ships.Classic1).Ship2);
        foreach (int w in new[] { 0, 1, 5, Protocol.NoShip })
            foreach (int s1 in new[] { 0, 1 })
            {
                var r = LockstepSession.ResolveShips(Enhanced(s1), w);
                Assert.Null(r.Validate());
                Assert.Equal(s1, r.Ship1); // the host always keeps its pick
            }
    }

    [Fact]
    public void HelloCarriesTheShipWishAndOlderHellosHaveNone()
    {
        var h = new Protocol.Hello(Protocol.Version, Key, false, "2026.10.17.6", Ships.Classic2);
        var r = new Protocol.Reader(Protocol.EncodeHello(h)); r.U8();
        Assert.Equal(h, Protocol.DecodeHello(r));
        var old = new Protocol.Writer().U8(Protocol.MsgHello).U16(Protocol.Version).Str(Key).U8(0).Str("x").ToArray();
        r = new Protocol.Reader(old); r.U8();
        Assert.Equal(Protocol.NoShip, Protocol.DecodeHello(r).Ship);
    }

    [Theory]
    [InlineData(0, 1, 0, 1)] // guest asks for the free ship
    [InlineData(0, 0, 0, 1)] // guest asks for the host's ship: gets the other
    [InlineData(1, 0, 1, 0)] // host on ship 1, guest on ship 0
    [InlineData(1, 1, 1, 0)]
    [InlineData(1, Protocol.NoShip, 1, 0)]
    [InlineData(Ships.Cruiser, Ships.Scout, Ships.Cruiser, Ships.Scout)]   // SHIP ABILITIES: armor state in lockstep
    [InlineData(Ships.Scout, Ships.Scout, Ships.Scout, Ships.Classic1)]
    public void BothMachinesFlyTheResolvedShips(int hostShip, int guestWish, int want1, int want2)
    {
        var hh = new List<uint>(); var gh = new List<uint>();
        var (host, guest) = Run(Enhanced(hostShip), guestWish, hostHashes: hh, guestHashes: gh);
        Assert.Equal(SessionPhase.Running, host.Phase); Assert.Equal(SessionPhase.Running, guest.Phase);
        Assert.True(host.ConfigVerified && guest.ConfigVerified);
        Assert.Equal(guestWish, host.PeerShipWish);
        foreach (var s in new[] { host, guest })
        {
            Assert.Equal((want1, want2), (s.Config!.Value.Game.Ship1, s.Config.Value.Game.Ship2));
            Assert.Equal(new[] { want1, want2 }, s.Sim!.State.Ships);
        }
        int n = Math.Min(hh.Count, gh.Count);
        Assert.True(n > 400, $"{n} frames");
        Assert.Equal(hh.Take(n), gh.Take(n));
    }

    [Fact]
    public void ShipSelectOffFliesTheArcadeShipsWhateverWasPicked()
    {
        var (host, guest) = Run(Enhanced(Ships.Classic2, o: EnhancedOption.All & ~EnhancedOption.ShipSelect), Ships.Classic1);
        Assert.Equal(SessionPhase.Running, guest.Phase);
        Assert.Equal(new[] { Ships.Classic1, Ships.Classic2 }, host.Sim!.State.Ships);
        Assert.Equal(new[] { Ships.Classic1, Ships.Classic2 }, guest.Sim!.State.Ships);
        var (ah, ag) = Run(new GameConfig(8, false, PlayMode.Arcade, EnhancedOption.All, Ships.Classic2, Ships.Classic1), Ships.Classic1);
        Assert.Equal(new[] { Ships.Classic1, Ships.Classic2 }, ag.Sim!.State.Ships);
    }

    [Fact]
    public void ShipsAlteredInTransitAreCaughtAndDuplicatesRefused()
    {
        // Start = [type, delay, len, schema, canisters, flags, mode, options lo, options hi, ship1, ship2, ...]
        var cfg = Enhanced(Ships.Classic1);
        var (host, guest) = Run(cfg, Ships.Classic2, p => (p[9], p[10]) = (p[10], p[9]), ticks: 120);
        Assert.Equal(SessionPhase.Failed, host.Phase);
        Assert.Contains("game config mismatch", host.Status);
        (host, guest) = Run(cfg, Ships.Classic2, p => p[10] = p[9], ticks: 120);
        Assert.Equal(SessionPhase.Failed, guest.Phase);
        Assert.Contains("both players", guest.Status);
    }
}
