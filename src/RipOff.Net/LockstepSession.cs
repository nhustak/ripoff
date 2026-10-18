using System;
using System.Collections.Generic;
using System.Diagnostics;
using RipOff.Core;

namespace RipOff.Net
{
    public enum SessionPhase { Handshake, Running, Desynced, Failed, Closed }

    /// <summary>
    /// Two-player input-sync lockstep over any <see cref="ITransport"/>. The simulation is deterministic at 38 Hz, so
    /// each peer runs its own copy and only inputs travel: the input sampled on local tick t is applied on frame
    /// t + <see cref="InputDelay"/> on both machines, and a frame is simulated only when both players' inputs for it
    /// are known. Inputs are sent unreliably, every packet repeating all frames the peer has not acknowledged yet, so
    /// loss and reordering cost nothing but a resend. Every <see cref="ChecksumInterval"/> frames each peer sends a
    /// <see cref="StateHash"/> of its state after that frame; a mismatch stops the session as desynced.
    /// No rollback: if the peer's input is late the game waits (shown as "waiting for peer").
    /// </summary>
    public sealed class LockstepSession : IDisposable
    {
        /// <summary>Default input delay: 3 frames = 79 ms at 38 Hz (see NOTES §22).</summary>
        public const int DefaultInputDelay = 3;
        public const int DefaultChecksumInterval = 19; // twice a second
        public const double DefaultTimeoutSeconds = 10;
        /// <summary>Most frames simulated in one <see cref="Update"/> when catching up after a stall.</summary>
        public const int MaxCatchUp = 2;

        private readonly ITransport _t;
        private readonly string _gameKey;
        private readonly Func<double> _clock;
        private readonly double _timeout;
        private readonly List<byte> _local = new();
        private readonly List<byte> _remote = new();
        private readonly List<bool> _remoteKnown = new();
        private readonly Dictionary<int, uint> _localHashes = new();
        private readonly Dictionary<int, uint> _peerHashes = new();
        private readonly Queue<int> _unsentChecks = new();
        private readonly List<(int frame, uint hash)> _recentChecks = new();
        private int _peerAck = -1;      // highest frame of ours the peer has (contiguously)
        private int _remoteContig = -1; // highest contiguous frame of theirs we have
        private bool _helloOk, _startSent;
        private double _lastHeard;
        private Protocol.StartConfig? _pendingStart;
        private Protocol.StartConfig _hostConfig; // host: Ship2 is set from the guest's Hello before Start is sent

        public bool IsHost { get; }
        /// <summary>0 for the host (player 1), 1 for the guest (player 2).</summary>
        public int LocalPlayer => IsHost ? 0 : 1;
        public int InputDelay { get; private set; }
        public int ChecksumInterval { get; }
        public SessionPhase Phase { get; private set; } = SessionPhase.Handshake;
        public string Status { get; private set; } = "connecting";
        public Simulation? Sim { get; private set; }
        /// <summary>Next frame to simulate (= frames simulated so far).</summary>
        public int Frame { get; private set; }
        public int DesyncFrame { get; private set; } = -1;
        /// <summary>True when the last <see cref="Update"/> could not advance because the peer's input was missing.</summary>
        public bool Waiting { get; private set; }
        public int StepsLastUpdate { get; private set; }
        public int ChecksumsCompared { get; private set; }
        public ITransport Transport => _t;
        /// <summary>The start parameters in use once running (sent by the host, received by the guest).</summary>
        public Protocol.StartConfig? Config { get; private set; }
        /// <summary>This side's build string sent in Hello (display only; default BuildInfo.Informational).</summary>
        public string LocalBuild { get; init; } = BuildInfo.Informational;
        /// <summary>Guest: the ship it wants (sent in Hello; the host may move it off the host's ship). Host: unused, its
        /// own ship is hostConfig.Game.Ship1. <see cref="Protocol.NoShip"/> = no wish (keep the host config's Ship2).</summary>
        public int LocalShip { get; init; } = Protocol.NoShip;
        /// <summary>The ship the peer asked for in its Hello (<see cref="Protocol.NoShip"/> if none).</summary>
        public int PeerShipWish { get; private set; } = Protocol.NoShip;
        /// <summary>The peer's build string from its Hello ("" until received, or when an older build sent none).</summary>
        public string PeerBuild { get; private set; } = "";
        /// <summary>Host: the guest echoed the hash of the start config (game options included) and it matched.
        /// Guest: the received config passed <see cref="GameConfig.Validate"/>. No frame is simulated before this.</summary>
        public bool ConfigVerified { get; private set; }
        /// <summary>Called after every simulated frame with the frame number and the state after it.</summary>
        public Action<int, GameState>? FrameSimulated;
        /// <summary>Test/diagnostic: when set, the input bytes (P1, P2) of every frame are kept in <see cref="InputsUsed"/>.</summary>
        public bool RecordInputs { get; set; }
        public List<(byte p1, byte p2)> InputsUsed { get; } = new();

        /// <param name="hostConfig">Host only: the game parameters sent to the guest (seeds, canisters, delay).</param>
        public LockstepSession(ITransport transport, bool isHost, string gameKey, Protocol.StartConfig? hostConfig = null,
            int checksumInterval = DefaultChecksumInterval, Func<double>? clock = null, double timeoutSeconds = DefaultTimeoutSeconds)
        {
            _t = transport; IsHost = isHost; _gameKey = gameKey; ChecksumInterval = checksumInterval;
            var sw = Stopwatch.StartNew();
            _clock = clock ?? (() => sw.Elapsed.TotalSeconds);
            _timeout = timeoutSeconds;
            _hostConfig = hostConfig ?? DefaultConfig(Environment.TickCount);
            if (isHost && _hostConfig.Game.Validate() is { } bad) throw new ArgumentException("game config: " + bad, nameof(hostConfig));
            _lastHeard = _clock();
        }

        /// <summary>
        /// Host side of SHIP SELECT online (NOTES §30): the host keeps its own ship (Ship1); the guest gets the ship it
        /// asked for, unless that is the host's ship or unknown, in which case it gets the lowest other ship. With no wish
        /// the host config's Ship2 stays (moved the same way if it clashes). Always called; the sim only uses the ships
        /// when the rules have SHIP SELECT on.
        /// </summary>
        public static GameConfig ResolveShips(GameConfig g, int guestWish)
        {
            int want = guestWish == Protocol.NoShip ? g.Ship2 : guestWish;
            if (!Ships.IsDefined(want) || want == g.Ship1) want = Ships.FirstOtherThan(g.Ship1);
            return g with { Ship2 = want };
        }

        /// <summary>Start parameters derived from a seed: RNG words and pass counter vary per game.</summary>
        public static Protocol.StartConfig DefaultConfig(int seed, int inputDelay = DefaultInputDelay, GameConfig? game = null)
        {
            uint x = (uint)seed * 2654435761u + 0x9E3779B9u;
            int Next() { x ^= x << 13; x ^= x >> 17; x ^= x << 5; return (int)(x & 0xFFF); }
            var seeds = new int[5];
            for (int i = 0; i < 5; i++) { int v = Next(); seeds[i] = v == 0 ? 0xF20 : v; } // LFSR state must be non-zero
            return new Protocol.StartConfig(inputDelay, game ?? GameConfig.Default, Next() & 0xFF, seeds);
        }

        /// <summary>Builds the start-of-game simulation both peers run (two-player game, free play).</summary>
        public static Simulation CreateSimulation(in Protocol.StartConfig c)
        {
            var sim = new Simulation(); var s = sim.State;
            s.PassCounter = c.PassCounter;
            for (int i = 0; i < 5 && i < c.Seeds.Length; i++) s.Record(i).B = c.Seeds[i];
            sim.StartGame(onePlayer: false, c.Game);
            return sim;
        }

        /// <summary>
        /// One game tick: pump the network, handle handshake messages, queue this tick's local input, send, and
        /// simulate as many frames as both inputs allow (up to <see cref="MaxCatchUp"/>).
        /// </summary>
        public void Update(in PlayerInput local, bool start = false)
        {
            StepsLastUpdate = 0;
            if (Phase is SessionPhase.Failed or SessionPhase.Closed) return;
            _t.Poll();
            Receive();
            if (_t.State == TransportState.Closed && Phase != SessionPhase.Desynced) { Fail(_t.Error ?? "connection closed"); return; }
            if (Phase == SessionPhase.Handshake)
            {
                if (!_helloSent && _t.State == TransportState.Connected)
                {
                    Send(Protocol.EncodeHello(new Protocol.Hello(Protocol.Version, _gameKey, IsHost, LocalBuild, IsHost ? _hostConfig.Game.Ship1 : LocalShip)), true);
                    _helloSent = true;
                }
                if (IsHost && _helloOk && !_startSent)
                {
                    _hostConfig = _hostConfig with { Game = ResolveShips(_hostConfig.Game, PeerShipWish) };
                    Send(Protocol.EncodeStart(_hostConfig), true); _startSent = true;
                    Begin(_hostConfig);
                }
                else if (!IsHost && _helloOk && _pendingStart is { } cfg)
                {
                    // Guest: refuse a config this build cannot run, else confirm exactly what we received.
                    if (cfg.Game.Validate() is { } bad) { string why = "host's game config: " + bad; Send(Protocol.EncodeReject(why), true); Fail(why); return; }
                    Send(Protocol.EncodeStartAck(Protocol.StartHash(cfg)), true);
                    ConfigVerified = true;
                    Begin(cfg);
                }
            }
            if (_clock() - _lastHeard > _timeout) { Fail("connection lost (no data for " + _timeout + " s)"); return; }
            // After a desync keep sending our last hashes (no new frames are simulated), so the peer detects it too
            // even if it had not yet received the mismatching checksum when we stopped.
            if (Phase == SessionPhase.Desynced) { SendInputs(); return; }
            if (Phase != SessionPhase.Running) return;

            if (_local.Count <= Frame + InputDelay) _local.Add(Protocol.Pack(local, start));
            SendInputs();
            Waiting = true;
            if (!ConfigVerified) { Waiting = true; Status = "verifying game config"; return; } // host: until StartAck
            while (StepsLastUpdate < MaxCatchUp && Frame < _local.Count && Frame < _remoteKnown.Count && _remoteKnown[Frame])
            {
                Step();
                Waiting = false;
                if (Phase != SessionPhase.Running) return;
            }
            if (StepsLastUpdate > 0) Waiting = false;
            Status = Waiting ? "waiting for peer" : "running";
        }

        private bool _helloSent;

        private void Begin(in Protocol.StartConfig cfg)
        {
            Config = cfg;
            InputDelay = Math.Clamp(cfg.InputDelay, 1, 10);
            Sim = CreateSimulation(cfg);
            for (int f = 0; f < InputDelay; f++)
            {
                if (_local.Count <= f) _local.Add(0);
                SetRemote(f, 0);
            }
            Phase = SessionPhase.Running; Status = "running";
        }

        private void Step()
        {
            byte mine = _local[Frame], theirs = _remote[Frame];
            byte p1 = IsHost ? mine : theirs, p2 = IsHost ? theirs : mine;
            var s = Sim!.State;
            if (RecordInputs) InputsUsed.Add((p1, p2));
            StepFrame(Sim, p1, p2);
            FrameSimulated?.Invoke(Frame, s);
            if (Frame % ChecksumInterval == 0)
            {
                uint h = StateHash.Compute(s);
                _localHashes[Frame] = h; _unsentChecks.Enqueue(Frame);
                _recentChecks.Add((Frame, h)); if (_recentChecks.Count > 4) _recentChecks.RemoveAt(0);
                Compare(Frame);
            }
            Frame++; StepsLastUpdate++;
            // forget old hashes
            if (Frame % 256 == 0)
            {
                foreach (var k in new List<int>(_localHashes.Keys)) if (k < Frame - 2048) _localHashes.Remove(k);
                foreach (var k in new List<int>(_peerHashes.Keys)) if (k < Frame - 2048) _peerHashes.Remove(k);
            }
        }

        /// <summary>
        /// Advances <paramref name="sim"/> one frame from the two players' input bytes. Online games do not use coins
        /// or credits: in attract mode (after a game over) either player's Start begins a new two-player game directly
        /// (Simulation.StartGame), and no coin/start/credit input reaches the simulation. Both peers apply this to the
        /// same bytes, so it stays deterministic; offline replays use the same function.
        /// </summary>
        public static void StepFrame(Simulation sim, byte p1, byte p2)
        {
            var s = sim.State;
            if (s.Mode == GameMode.Attract && ((p1 | p2) & Protocol.InStart) != 0) sim.StartGame(onePlayer: false);
            sim.Step(new FrameInput { P1 = Protocol.Unpack(p1), P2 = Protocol.Unpack(p2) });
        }

        private void Compare(int frame)
        {
            if (!_localHashes.TryGetValue(frame, out var a) || !_peerHashes.TryGetValue(frame, out var b)) return;
            ChecksumsCompared++;
            _peerHashes.Remove(frame);
            if (a != b)
            {
                DesyncFrame = frame; Phase = SessionPhase.Desynced;
                Status = $"DESYNC at frame {frame} (local {a:X8}, peer {b:X8})";
            }
        }

        private void SendInputs()
        {
            int first = _peerAck + 1, n = Math.Min(_local.Count - first, Protocol.MaxInputsPerPacket);
            if (n < 0) n = 0;
            var inputs = _local.GetRange(first, n).ToArray();
            var msg = new Protocol.InputMsg(_remoteContig, first, inputs, _recentChecks.ToArray());
            Send(Protocol.EncodeInput(msg), false);
        }

        private void Receive()
        {
            while (_t.TryReceive(out var pkt))
            {
                _lastHeard = _clock();
                if (pkt.Length == 0) continue;
                var r = new Protocol.Reader(pkt);
                try
                {
                    int type = r.U8();
                    switch (type)
                    {
                        case Protocol.MsgHello:
                            var h = Protocol.DecodeHello(r);
                            PeerBuild = h.Build;
                            if (h.Version != Protocol.Version || h.GameKey != _gameKey)
                            {
                                string why = $"version mismatch: peer {h.GameKey} v{h.Version} (build {BuildInfo.DisplayOf(h.Build.Length > 0 ? h.Build : "unknown")}), "
                                    + $"this build {_gameKey} v{Protocol.Version} (build {BuildInfo.DisplayOf(LocalBuild)})";
                                Send(Protocol.EncodeReject(why), true); Fail(why); return;
                            }
                            if (h.FromHost == IsHost) { string why = IsHost ? "both peers are hosting" : "both peers are joining"; Send(Protocol.EncodeReject(why), true); Fail(why); return; }
                            PeerShipWish = h.Ship;
                            _helloOk = true;
                            break;
                        case Protocol.MsgReject: Fail("rejected by peer: " + r.Str()); return;
                        case Protocol.MsgStart: if (!IsHost) _pendingStart = Protocol.DecodeStart(r); break;
                        case Protocol.MsgStartAck:
                            if (IsHost && _startSent && !ConfigVerified)
                            {
                                uint echoed = Protocol.DecodeStartAck(r), mine = Protocol.StartHash(_hostConfig);
                                if (echoed != mine) { string why = $"game config mismatch (host {mine:X8}, guest {echoed:X8})"; Send(Protocol.EncodeReject(why), true); Fail(why); return; }
                                ConfigVerified = true;
                            }
                            break;
                        case Protocol.MsgInput: OnInput(Protocol.DecodeInput(r)); break;
                        case Protocol.MsgBye: Phase = SessionPhase.Closed; Status = "the other player left"; return;
                    }
                }
                catch (FormatException) { /* malformed packet: ignore */ }
            }
        }

        private void OnInput(in Protocol.InputMsg m)
        {
            if (m.Ack > _peerAck) _peerAck = Math.Min(m.Ack, _local.Count - 1);
            for (int i = 0; i < m.Inputs.Length; i++) SetRemote(m.FirstFrame + i, m.Inputs[i]);
            foreach (var (f, hash) in m.Checks)
            {
                if (f < 0 || _peerHashes.ContainsKey(f)) continue;
                if (_localHashes.ContainsKey(f) || f >= Frame) { _peerHashes[f] = hash; Compare(f); }
            }
        }

        private void SetRemote(int f, byte v)
        {
            if (f < 0 || f > Frame + 4096) return;
            while (_remote.Count <= f) { _remote.Add(0); _remoteKnown.Add(false); }
            if (_remoteKnown[f]) return;
            _remote[f] = v; _remoteKnown[f] = true;
            while (_remoteContig + 1 < _remoteKnown.Count && _remoteKnown[_remoteContig + 1]) _remoteContig++;
        }

        private void Send(byte[] data, bool reliable) => _t.Send(data, data.Length, reliable);

        private void Fail(string why) { Phase = SessionPhase.Failed; Status = why; }

        /// <summary>Test hook: corrupt this peer's state to check desync detection.</summary>
        internal void InjectDesync() { if (Sim != null) Sim.State.Score[0] += 1; }

        /// <summary>Tells the peer we are leaving (best effort) and closes the transport.</summary>
        public void Dispose()
        {
            try { if (_t.State == TransportState.Connected) Send(new[] { Protocol.MsgBye }, true); _t.Poll(); } catch (Exception) { }
            _t.Dispose();
            if (Phase is SessionPhase.Handshake or SessionPhase.Running) { Phase = SessionPhase.Closed; Status = "closed"; }
        }
    }
}
