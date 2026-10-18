using RipOff.Core;
using RipOff.Net;

namespace RipOff.Net.Tests;

public class GameConfigTests
{
    const string Key = "test-game";

    /// <summary>Wraps a transport and rewrites incoming Start packets (simulates a peer/build with other rules).</summary>
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

    static (LockstepSession host, LockstepSession guest) Run(Protocol.StartConfig cfg, Action<byte[]>? tamper = null, int ticks = 120)
    {
        var net = new LoopbackNetwork(7, 20, 10);
        var host = new LockstepSession(net.A, true, Key, cfg, clock: () => net.NowMs / 1000);
        ITransport gt = tamper == null ? net.B : new Tamper(net.B, tamper);
        var guest = new LockstepSession(gt, false, Key, clock: () => net.NowMs / 1000);
        for (int i = 0; i < ticks; i++) { host.Update(default); guest.Update(default); net.Advance(1000.0 / 38); }
        return (host, guest);
    }

    [Fact]
    public void DefaultIsTheArcadeDefault()
    {
        Assert.Equal(8, GameConfig.Default.Canisters);
        Assert.Equal(GameData.DefaultCanisterDip, GameConfig.Default.CanisterDip);
        Assert.False(GameConfig.Default.AutoFire);
        Assert.Null(GameConfig.Default.Validate());
    }

    [Fact]
    public void EncodeDecodeRoundTripsAndValidates()
    {
        foreach (int c in GameData.CanistersForDip)
            foreach (bool af in new[] { false, true })
            {
                var g = new GameConfig(c, af);
                Assert.Equal(g, GameConfig.Decode(g.Encode()));
            }
        Assert.NotNull(new GameConfig(5).Validate());
        Assert.Null(new GameConfig(8, AutoFire: true).Validate()); // auto-fire is implemented since M4
        foreach (var m in new[] { PlayMode.Arcade, PlayMode.Enhanced })
        {
            var g = new GameConfig(4, true, m);
            Assert.Equal(g, GameConfig.Decode(g.Encode()));
        }
        foreach (var o in new[] { EnhancedOption.All, EnhancedOption.None, EnhancedOption.WarpIn | EnhancedOption.SlowerTowing })
        {
            var g = new GameConfig(8, false, PlayMode.Enhanced, o);
            Assert.Equal(g, GameConfig.Decode(g.Encode()));
            Assert.Null(g.Validate());
        }
        Assert.Throws<FormatException>(() => GameConfig.Decode(new byte[] { 9, 8, 0, 0, 15, 0, 0, 1 })); // unknown schema
        Assert.Throws<FormatException>(() => GameConfig.Decode(new byte[] { 1, 8, 0 }));          // schema 1 (before play modes)
        var v3 = Assert.Throws<FormatException>(() => GameConfig.Decode(new byte[] { 2, 8, 0, 1 })); // schema 2 (before Enhanced options)
        Assert.Contains("schema 2", v3.Message);
        Assert.Throws<FormatException>(() => GameConfig.Decode(new byte[] { 3, 8, 0, 1, 0x3F }));  // schema 3 (one options byte, no ships)
        Assert.Throws<FormatException>(() => GameConfig.Decode(new byte[] { 4, 8, 0, 1, 0x7F, 0 })); // schema 4 cut short
        Assert.Throws<FormatException>(() => GameConfig.Decode(new byte[] { 4, 8, 0x80, 0, 15, 0, 0, 1 })); // reserved flag bit
        Assert.NotNull(GameConfig.Decode(new byte[] { 4, 8, 0, 7, 15, 0, 0, 1 }).Validate());   // unknown play mode: refused, with a reason
        Assert.Contains("enhanced options", GameConfig.Decode(new byte[] { 4, 8, 0, 1, 0x7F, 0x80, 0, 1 }).Validate()); // unknown high bit
        Assert.Null(GameConfig.Decode(new byte[] { 4, 8, 0, 1, 0x7F, 0, 0, 1 }).Validate()); // SHIP SELECT is bit 0x40
        Assert.Null(GameConfig.Decode(new byte[] { 4, 8, 0, 1, 0xFF, 0, 0, 1 }).Validate()); // SHIP ABILITIES is bit 0x80
        var all = (int)EnhancedOption.All;
        Assert.Equal(EnhancedOption.All, GameConfig.Decode(new byte[] { 4, 8, 0, 1, (byte)all, (byte)(all >> 8), 0, 1 }).Options);
        // ships: known and different (NOTES §30)
        var swapped = new GameConfig(8, false, PlayMode.Enhanced, EnhancedOption.All, Ships.Classic2, Ships.Classic1);
        Assert.Null(swapped.Validate());
        Assert.Equal(swapped, GameConfig.Decode(swapped.Encode()));
        Assert.Equal(new byte[] { 4, 8, 0, 1, (byte)all, (byte)(all >> 8), 1, 0 }, swapped.Encode());
        Assert.Contains("both players", GameConfig.Decode(new byte[] { 4, 8, 0, 1, 0x7F, 0, 1, 1 }).Validate());
        Assert.Contains("unknown ship", GameConfig.Decode(new byte[] { 4, 8, 0, 1, 0x7F, 0, 0, 99 }).Validate());
        Assert.Contains("unknown ship", GameConfig.Decode(new byte[] { 4, 8, 0, 0, 0x7F, 0, 200, 0 }).Validate()); // checked in Arcade too
        Assert.NotNull(new GameConfig(8, false, (PlayMode)7).Validate());
    }

    [Fact]
    public void HostConfigReachesTheGuestAndIsVerified()
    {
        var (host, guest) = Run(LockstepSession.DefaultConfig(3, game: new GameConfig(16)));
        Assert.Equal(SessionPhase.Running, host.Phase);
        Assert.True(host.ConfigVerified); Assert.True(guest.ConfigVerified);
        Assert.Equal(16, guest.Config!.Value.Game.Canisters);
        Assert.Equal(1, guest.Sim!.State.CanisterDip);
        Assert.True(host.Frame > 50);
    }

    [Fact]
    public void ConfigAlteredInTransitIsCaughtByTheEchoedHash()
    {
        // Start = [type, delay, len, schema, canisters, flags, ...]: the guest sees 12 canisters instead of 8.
        var (host, guest) = Run(LockstepSession.DefaultConfig(3), p => p[4] = 12);
        Assert.Equal(SessionPhase.Failed, host.Phase);
        Assert.Contains("game config mismatch", host.Status);
        Assert.Equal(0, host.Frame); // nothing simulated with unverified rules
        Assert.Equal(SessionPhase.Failed, guest.Phase);
    }

    [Fact]
    public void GuestRefusesAConfigItCannotRun()
    {
        var (host, guest) = Run(LockstepSession.DefaultConfig(3), p => p[4] = 7); // 7 canisters: no such setting
        Assert.Equal(SessionPhase.Failed, guest.Phase);
        Assert.Contains("canister", guest.Status);
        Assert.Equal(SessionPhase.Failed, host.Phase);
        Assert.Contains("rejected by peer", host.Status);
    }

    [Fact]
    public void AutoFireIsSyncedAndBothPeersStayIdentical()
    {
        var net = new LoopbackNetwork(9, 40, 30, 0.05);
        var host = new LockstepSession(net.A, true, Key, LockstepSession.DefaultConfig(8, game: new GameConfig(12, AutoFire: true)), clock: () => net.NowMs / 1000);
        var guest = new LockstepSession(net.B, false, Key, clock: () => net.NowMs / 1000);
        var h = new Dictionary<int, uint>(); var g = new Dictionary<int, uint>();
        host.FrameSimulated = (f, st) => h[f] = StateHash.Compute(st);
        guest.FrameSimulated = (f, st) => g[f] = StateHash.Compute(st);
        for (int i = 0; i < 3000; i++)
        {
            host.Update(new PlayerInput { Fire = i % 100 < 70, Left = i % 60 < 20 });   // long holds: auto-fire repeats
            guest.Update(new PlayerInput { Fire = i % 80 < 50, Thrust = i % 9 < 4 });
            net.Advance(1000.0 / 38);
        }
        Assert.Equal(SessionPhase.Running, host.Phase); Assert.Equal(SessionPhase.Running, guest.Phase);
        Assert.True(guest.Config!.Value.Game.AutoFire);
        Assert.True(guest.Sim!.State.AutoFire && host.Sim!.State.AutoFire);
        int common = 0;
        foreach (var (f, hash) in h) if (g.TryGetValue(f, out var gh)) { Assert.Equal(hash, gh); common++; }
        Assert.True(common > 2500, $"{common} common frames");
        // auto-fire actually fired more than one shot per press for both players
        Assert.True(host.Sim.State.ShotsFired[0] > 3000 / 100 && host.Sim.State.ShotsFired[1] > 3000 / 80);
        Assert.True(host.ChecksumsCompared > 100);
    }

    [Fact]
    public void GuestAdoptsTheHostsPlayModeAndBothStayIdentical()
    {
        var net = new LoopbackNetwork(11, 30, 20, 0.03);
        var host = new LockstepSession(net.A, true, Key, LockstepSession.DefaultConfig(5, game: new GameConfig(8, Mode: PlayMode.Enhanced)), clock: () => net.NowMs / 1000);
        var guest = new LockstepSession(net.B, false, Key, clock: () => net.NowMs / 1000);
        var h = new Dictionary<int, uint>(); var g = new Dictionary<int, uint>();
        host.FrameSimulated = (f, st) => h[f] = StateHash.Compute(st);
        guest.FrameSimulated = (f, st) => g[f] = StateHash.Compute(st);
        for (int i = 0; i < 1500; i++)
        {
            host.Update(new PlayerInput { Fire = i % 10 < 3, Left = i % 60 < 20 });
            guest.Update(new PlayerInput { Fire = i % 8 < 2, Thrust = i % 9 < 4 });
            net.Advance(1000.0 / 38);
        }
        Assert.Equal(SessionPhase.Running, host.Phase); Assert.Equal(SessionPhase.Running, guest.Phase);
        Assert.Equal(PlayMode.Enhanced, guest.Config!.Value.Game.Mode);
        Assert.Equal(PlayMode.Enhanced, guest.Sim!.State.PlayMode);
        Assert.Equal(PlayMode.Enhanced, host.Sim!.State.PlayMode);
        int common = 0;
        foreach (var (f, hash) in h) if (g.TryGetValue(f, out var gh)) { Assert.Equal(hash, gh); common++; }
        Assert.True(common > 1200, $"{common} common frames");
    }

    [Theory]
    [InlineData((ushort)(EnhancedOption.WarpIn | EnhancedOption.SlowerTowing))]           // fixed warp, aim error and slower ramp off
    [InlineData((ushort)EnhancedOption.All)]                                               // random warp-in + warp kills + ship select
    [InlineData((ushort)(EnhancedOption.WarpIn | EnhancedOption.WarpKills))]               // warp kills at the fixed points
    public void GuestAdoptsTheHostsEnhancedOptionsAndBothWarpInIdentically(ushort o)
    {
        var net = new LoopbackNetwork(12, 30, 20, 0.03);
        var opts = (EnhancedOption)o;
        var host = new LockstepSession(net.A, true, Key, LockstepSession.DefaultConfig(6, game: new GameConfig(8, Mode: PlayMode.Enhanced, Options: opts)), clock: () => net.NowMs / 1000);
        var guest = new LockstepSession(net.B, false, Key, clock: () => net.NowMs / 1000);
        var h = new Dictionary<int, uint>(); var g = new Dictionary<int, uint>();
        int hostWarp = 0, guestWarp = 0;
        host.FrameSimulated = (f, st) => { h[f] = StateHash.Compute(st); if (st.WarpTimer[0] > 0 || st.WarpTimer[1] > 0) hostWarp++; };
        guest.FrameSimulated = (f, st) => { g[f] = StateHash.Compute(st); if (st.WarpTimer[0] > 0 || st.WarpTimer[1] > 0) guestWarp++; };
        for (int i = 0; i < 1500; i++)
        {
            host.Update(new PlayerInput { Fire = i % 10 < 3, Left = i % 60 < 20 });
            guest.Update(new PlayerInput { Fire = i % 8 < 2, Thrust = i % 9 < 4 });
            net.Advance(1000.0 / 38);
        }
        Assert.Equal(SessionPhase.Running, host.Phase); Assert.Equal(SessionPhase.Running, guest.Phase);
        Assert.Equal(opts, guest.Config!.Value.Game.Options);
        Assert.Equal(opts, guest.Sim!.State.Options);
        Assert.Equal(opts, host.Sim!.State.Options);
        Assert.Equal(guest.Sim.Rules, GameRules.For(PlayMode.Enhanced, opts)); // the guest runs the host's toggles, not its own defaults
        if ((opts & EnhancedOption.EnemyAimError) == 0) Assert.Equal(0, guest.Sim.Rules.AimErrorAt(1));
        Assert.Equal((opts & EnhancedOption.RandomWarpIn) != 0 ? WarpInLocation.Random : WarpInLocation.Fixed, guest.Sim.Rules.WarpLocation);
        Assert.Equal((opts & EnhancedOption.WarpKills) != 0, guest.Sim.Rules.WarpKills);
        Assert.True(hostWarp > 0 && hostWarp == guestWarp, $"warp frames host {hostWarp} guest {guestWarp}");
        int common = 0;
        foreach (var (f, hash) in h) if (g.TryGetValue(f, out var gh)) { Assert.Equal(hash, gh); common++; }
        Assert.True(common > 1200, $"{common} common frames");
    }

    [Fact]
    public void OptionsAlteredInTransitAreCaughtAndUnknownOptionsRefused()
    {
        // v5: options became 16-bit and the ships were added (GameConfig schema 4, NOTES §30); v4 builds are refused at
        // Hello.
        Assert.Equal(6, Protocol.Version); // 6: SHIP ABILITIES bit (2026.10.17.7)
        // Start = [type, delay, len, schema, canisters, flags, mode, options lo, options hi, ship1, ship2, ...]
        var cfg = LockstepSession.DefaultConfig(3, game: new GameConfig(8, Mode: PlayMode.Enhanced));
        var (host, guest) = Run(cfg, p => p[7] = (byte)EnhancedOption.WarpIn);
        Assert.Equal(SessionPhase.Failed, host.Phase);
        Assert.Contains("game config mismatch", host.Status);
        (host, guest) = Run(cfg, p => p[7] = (byte)(EnhancedOption.All & ~EnhancedOption.WarpKills)); // a new bit flipped
        Assert.Equal(SessionPhase.Failed, host.Phase);
        Assert.Contains("game config mismatch", host.Status);
        (host, guest) = Run(cfg, p => p[8] = 0x80);
        Assert.Equal(SessionPhase.Failed, guest.Phase);
        Assert.Contains("enhanced options", guest.Status);
    }

    [Fact]
    public void ModeAlteredInTransitIsCaughtAndUnknownModeRefused()
    {
        // Start = [type, delay, len, schema, canisters, flags, mode, ...]
        var (host, guest) = Run(LockstepSession.DefaultConfig(3), p => p[6] = (byte)PlayMode.Enhanced);
        Assert.Equal(SessionPhase.Failed, host.Phase);
        Assert.Contains("game config mismatch", host.Status);
        Assert.Equal(0, host.Frame);

        (host, guest) = Run(LockstepSession.DefaultConfig(3), p => p[6] = 9);
        Assert.Equal(SessionPhase.Failed, guest.Phase);
        Assert.Contains("play mode", guest.Status);
        Assert.Equal(SessionPhase.Failed, host.Phase);
        Assert.Contains("rejected by peer", host.Status);
    }

    [Fact]
    public void HostCannotStartWithAnInvalidConfig()
    {
        var net = new LoopbackNetwork(1);
        Assert.Throws<ArgumentException>(() => new LockstepSession(net.A, true, Key, LockstepSession.DefaultConfig(1, game: new GameConfig(7))));
    }
}
