using RipOff.Core;
using RipOff.Net;

namespace RipOff.Net.Tests;

public class LockstepTests
{
    const string Key = "test-game";
    const double TickMs = 1000.0 / 38;

    /// <summary>Deterministic pseudo-random player input per (player, tick): turns, thrust bursts, fire taps, starts.</summary>
    static (PlayerInput p, bool start) Bot(int player, int tick)
    {
        uint x = (uint)(tick / 9 * 7919 + player * 104729) * 2654435761u; x ^= x >> 15;
        var p = new PlayerInput { Left = (x & 3) == 1, Right = (x & 3) == 2, Thrust = (x & 12) != 0, Fire = ((tick + player) % 5) < 2 && (x & 16) != 0 };
        return (p, tick % 400 == 399); // a start press now and then (restarts after game over)
    }

    sealed class Pair
    {
        public readonly LoopbackNetwork Net;
        public readonly LockstepSession Host, Guest;
        public int Tick;
        public Pair(LoopbackNetwork net, string guestKey = Key, Protocol.StartConfig? cfg = null)
        {
            Net = net;
            Host = new LockstepSession(net.A, true, Key, cfg ?? LockstepSession.DefaultConfig(1234), clock: () => net.NowMs / 1000);
            Guest = new LockstepSession(net.B, false, guestKey, clock: () => net.NowMs / 1000);
            Host.RecordInputs = true;
        }
        public bool Idle;
        public void RunTicks(int n, Action<int>? each = null)
        {
            for (int i = 0; i < n; i++, Tick++)
            {
                var (hp, hs) = Bot(0, Tick); var (gp, gs) = Bot(1, Tick);
                if (Idle) { hp = default; gp = default; }
                Host.Update(hp, start: hs); Guest.Update(gp, start: gs);
                Net.Advance(TickMs);
                each?.Invoke(Tick);
            }
        }
    }

    static (Dictionary<int, uint> host, Dictionary<int, uint> guest) Log(Pair pair)
    {
        var h = new Dictionary<int, uint>(); var g = new Dictionary<int, uint>();
        pair.Host.FrameSimulated = (f, st) => h[f] = StateHash.Compute(st);
        pair.Guest.FrameSimulated = (f, st) => g[f] = StateHash.Compute(st);
        return (h, g);
    }

    [Theory]
    [InlineData(1, 0, 0, 0.0)]        // perfect link
    [InlineData(2, 40, 0, 0.0)]       // 40 ms latency
    [InlineData(3, 30, 60, 0.0)]      // jitter > tick (26 ms): reordering
    [InlineData(4, 60, 120, 0.10)]    // jitter + 10 % loss
    [InlineData(5, 150, 80, 0.25)]    // bad link: 150 ms, 25 % loss
    public void TwoPeersStayIdenticalForThousandsOfFrames(int seed, double latency, double jitter, double loss)
    {
        var net = new LoopbackNetwork(seed, latency, jitter, loss, duplicateRate: 0.05);
        var pair = new Pair(net);
        var (h, g) = Log(pair);
        int gameOvers = 0, kills = 0; var prevH = pair.Host.FrameSimulated!;
        pair.Host.FrameSimulated = (f, st) => { prevH(f, st); if (st.GameOverThisPass) gameOvers++; kills += st.Sound.Explosions; };
        pair.RunTicks(6000);
        Assert.Equal(SessionPhase.Running, pair.Host.Phase);
        Assert.Equal(SessionPhase.Running, pair.Guest.Phase);
        int frames = Math.Min(pair.Host.Frame, pair.Guest.Frame);
        Assert.True(frames > (latency + jitter > 100 ? 2500 : 5000), $"only {frames} frames simulated");
        // every simulated frame identical on both machines
        for (int f = 0; f < frames; f++) Assert.True(h[f] == g[f], $"state differs at frame {f}");
        Assert.True(pair.Host.ChecksumsCompared > frames / LockstepSession.DefaultChecksumInterval / 2);
        Assert.True(pair.Guest.ChecksumsCompared > frames / LockstepSession.DefaultChecksumInterval / 2);
        if (jitter > TickMs) Assert.True(net.Reordered > 100, $"reordered {net.Reordered}");
        if (loss > 0) Assert.True(net.Dropped > 100);
        // the game really was played (kills, deaths)
        Assert.True(kills > 10, $"explosions {kills}");
    }

    [Fact]
    public void GameOverAndFreePlayRestartStayInSync()
    {
        // Idle players lose all canisters; a later Start (every 400 ticks) begins a new game on both machines.
        var net = new LoopbackNetwork(21, 40, 50, 0.1);
        var pair = new Pair(net) { Idle = true };
        var (h, g) = Log(pair);
        int gameOvers = 0, restarts = 0; var prev = pair.Host.FrameSimulated!; GameMode last = GameMode.Playing;
        pair.Host.FrameSimulated = (f, st) =>
        {
            prev(f, st);
            if (st.GameOverThisPass) gameOvers++;
            if (last == GameMode.Attract && st.Mode == GameMode.Playing) restarts++;
            last = st.Mode;
        };
        pair.RunTicks(14000);
        Assert.True(gameOvers >= 1, $"game overs {gameOvers}");
        Assert.True(restarts >= 1, $"restarts {restarts}");
        int frames = Math.Min(pair.Host.Frame, pair.Guest.Frame);
        for (int f = 0; f < frames; f++) Assert.True(h[f] == g[f], $"state differs at frame {f}");
        Assert.Equal(SessionPhase.Running, pair.Guest.Phase);
    }

    [Fact]
    public void LockstepMatchesAnOfflineReplayOfTheSameInputs()
    {
        // Capture the inputs each frame used on the host, replay them on a fresh simulation: same states.
        var net = new LoopbackNetwork(7, 50, 70, 0.15);
        var cfg = LockstepSession.DefaultConfig(99);
        var pair = new Pair(net, cfg: cfg);
        var (h, _) = Log(pair);
        pair.RunTicks(3000);
        int frames = pair.Host.Frame;
        Assert.True(frames > 2000);
        var reference = LockstepSession.CreateSimulation(cfg);
        Assert.Equal(GameMode.Playing, reference.State.Mode);
        Assert.False(reference.State.OnePlayer);
        var used = pair.Host.InputsUsed;
        for (int f = 0; f < frames; f++)
        {
            LockstepSession.StepFrame(reference, used[f].p1, used[f].p2);
            Assert.True(StateHash.Compute(reference.State) == h[f], $"replay differs at {f}");
        }
        Assert.Equal(-1, pair.Host.DesyncFrame); Assert.Equal(-1, pair.Guest.DesyncFrame);
    }

    [Fact]
    public void InjectedDesyncIsDetectedOnBothSides()
    {
        var net = new LoopbackNetwork(11, 30, 20, 0.05);
        var pair = new Pair(net);
        pair.RunTicks(500);
        Assert.Equal(SessionPhase.Running, pair.Host.Phase);
        int at = pair.Guest.Frame;
        pair.Guest.InjectDesync();
        pair.RunTicks(200);
        Assert.Equal(SessionPhase.Desynced, pair.Host.Phase);
        Assert.Equal(SessionPhase.Desynced, pair.Guest.Phase);
        Assert.InRange(pair.Host.DesyncFrame, at, at + 2 * LockstepSession.DefaultChecksumInterval + 10);
        Assert.Contains("DESYNC", pair.Guest.Status);
    }

    [Fact]
    public void VersionOrKeyMismatchIsRejected()
    {
        var net = new LoopbackNetwork(3, 20);
        var pair = new Pair(net, guestKey: "someone-elses-spacewar-game");
        pair.RunTicks(100);
        Assert.Equal(SessionPhase.Failed, pair.Host.Phase);
        Assert.Equal(SessionPhase.Failed, pair.Guest.Phase);
        Assert.Contains("mismatch", pair.Host.Status);
        Assert.Null(pair.Host.Sim);
    }

    [Fact]
    public void HelloCarriesTheBuildForDisplayAndOlderHellosStillDecode()
    {
        var h = new Protocol.Hello(Protocol.Version, "k", true, "2026.10.17.1+abc1234");
        var d = Protocol.DecodeHello(new Protocol.Reader(Protocol.EncodeHello(h)[1..]));
        Assert.Equal(h, d);
        // a v3 Hello from before builds were sent (enhanced-preview-1): no trailing string
        var old = new Protocol.Writer().U16(Protocol.Version).Str("k").U8(1).ToArray();
        Assert.Equal("", Protocol.DecodeHello(new Protocol.Reader(old)).Build);
    }

    [Fact]
    public void PeersLearnEachOthersBuildAndAMismatchNamesBoth()
    {
        var net = new LoopbackNetwork(4, 20);
        var host = new LockstepSession(net.A, true, "k", LockstepSession.DefaultConfig(1), clock: () => net.NowMs / 1000) { LocalBuild = "2026.10.17.1+aaaaaaa" };
        var guest = new LockstepSession(net.B, false, "k", clock: () => net.NowMs / 1000) { LocalBuild = "2026.10.17.2+bbbbbbb" };
        for (int i = 0; i < 100; i++) { host.Update(default); guest.Update(default); net.Advance(1000.0 / 38); }
        Assert.Equal(SessionPhase.Running, host.Phase);
        Assert.Equal("2026.10.17.2+bbbbbbb", host.PeerBuild);   // different builds, same protocol: allowed, shown
        Assert.Equal("2026.10.17.1+aaaaaaa", guest.PeerBuild);

        var net2 = new LoopbackNetwork(5, 20);
        var h2 = new LockstepSession(net2.A, true, "k", LockstepSession.DefaultConfig(1), clock: () => net2.NowMs / 1000) { LocalBuild = "2026.10.17.1+aaaaaaa" };
        var g2 = new LockstepSession(net2.B, false, "other-key", clock: () => net2.NowMs / 1000) { LocalBuild = "dev+ccccccc" };
        for (int i = 0; i < 100; i++) { h2.Update(default); g2.Update(default); net2.Advance(1000.0 / 38); }
        Assert.Equal(SessionPhase.Failed, h2.Phase);
        Assert.Contains("version mismatch", h2.Status);
        Assert.Contains("build DEV CCCCCCC", h2.Status);
        Assert.Contains("build V2026.10.17.1", h2.Status);
    }

    [Fact]
    public void LostPeerTimesOut()
    {
        var net = new LoopbackNetwork(5, 20);
        var pair = new Pair(net);
        pair.RunTicks(200);
        net.LossRate = 1.0; // the link goes silent but nobody closes it
        pair.RunTicks((int)(38 * (LockstepSession.DefaultTimeoutSeconds + 1)));
        Assert.Equal(SessionPhase.Failed, pair.Host.Phase);
        Assert.Contains("connection lost", pair.Host.Status);
    }

    [Fact]
    public void InputDelayIsApplied()
    {
        var net = new LoopbackNetwork(1);
        var pair = new Pair(net, cfg: LockstepSession.DefaultConfig(5, inputDelay: 3));
        pair.RunTicks(50);
        Assert.Equal(3, pair.Host.InputDelay); Assert.Equal(3, pair.Guest.InputDelay);
        // on a perfect link the sim runs one frame per tick, never more than the delay behind real time
        Assert.InRange(pair.Host.Frame, 50 - 3 - 3, 50 + 3); // never ahead of real time by more than the delay
        Assert.False(pair.Host.Waiting);
    }

    [Fact]
    public void PeerLeavingEndsTheSession()
    {
        var net = new LoopbackNetwork(1, 10);
        var pair = new Pair(net);
        pair.RunTicks(100);
        pair.Guest.Dispose();
        pair.RunTicks(20);
        Assert.NotEqual(SessionPhase.Running, pair.Host.Phase);
    }

    [Fact]
    public void ProtocolRoundTrips()
    {
        var m = new Protocol.InputMsg(41, 40, new byte[] { 1, 2, 63 }, new[] { (38, 0xDEADBEEFu) });
        var d = Protocol.DecodeInput(new Protocol.Reader(Protocol.EncodeInput(m)[1..]));
        Assert.Equal(41, d.Ack); Assert.Equal(40, d.FirstFrame); Assert.Equal(m.Inputs, d.Inputs); Assert.Equal(m.Checks, d.Checks);
        var c = LockstepSession.DefaultConfig(77);
        var c2 = Protocol.DecodeStart(new Protocol.Reader(Protocol.EncodeStart(c)[1..]));
        Assert.Equal(c.Seeds, c2.Seeds); Assert.Equal(c.PassCounter, c2.PassCounter); Assert.Equal(c.InputDelay, c2.InputDelay);
        var p = new PlayerInput { Left = true, Fire = true };
        Assert.Equal(p, Protocol.Unpack(Protocol.Pack(p, false)));
        Assert.Throws<FormatException>(() => Protocol.DecodeInput(new Protocol.Reader(new byte[] { 1, 2 })));
        Assert.All(c.Seeds, s => Assert.InRange(s, 1, 0xFFF));
    }
}
