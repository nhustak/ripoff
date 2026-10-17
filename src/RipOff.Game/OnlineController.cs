using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using RipOff.Core;
using RipOff.Game.Steam;
using RipOff.Net;

namespace RipOff.Game
{
    /// <summary>
    /// The online entry point for any front end (README "Online API"): host via Steam (lobby + 6-character code),
    /// join by code or by Steam lobby id (invites), host / join by direct IP (LiteNetLib). Everything is driven by
    /// plain method calls and strings, so a pad-only on-screen keyboard can feed codes and addresses; nothing here
    /// reads the keyboard. Call <see cref="Update"/> once per 38 Hz tick with the local player's controls; draw
    /// <c>Session.Sim</c> while <see cref="Running"/>; show <see cref="Describe"/> otherwise.
    /// Host = player 1, guest = player 2. The host's <see cref="GameConfig"/> is used and verified by both peers.
    /// The session is created once the transport has a peer, so a host can wait as long as it likes.
    /// </summary>
    public sealed class OnlineController : IDisposable
    {
        public enum Kind { None, SteamHost, SteamJoin, LanHost, LanJoin }

        private readonly Func<SteamPlatform?> _steamSource;
        private SteamPlatform? _steam => _steamSource();
        private ITransport? _transport;
        private bool _desyncReported;

        public Kind Mode { get; private set; }
        public LockstepSession? Session { get; private set; }
        /// <summary>Why the connection ended (null while it is fine).</summary>
        public string? Error { get; private set; }
        public string? DesyncReportPath { get; private set; }
        /// <summary>Directory for desync reports (defaults to the executable's directory).</summary>
        public string ReportDirectory { get; set; } = AppContext.BaseDirectory;
        public Action<int, GameState>? FrameSimulated;

        public OnlineController(SteamPlatform? steam) : this(() => steam) { }
        private OnlineController(Func<SteamPlatform?> source) { _steamSource = source; }
        /// <summary>Steam looked up on use (lazy init through <see cref="SteamGate"/>).</summary>
        public static OnlineController WithSteamSource(Func<SteamPlatform?> source) => new(source);

        public bool Active => Mode != Kind.None;
        public bool Running => Session?.Phase == SessionPhase.Running;
        public bool Steam => Mode is Kind.SteamHost or Kind.SteamJoin;

        /// <summary>Game rules used when this machine hosts (the guest receives and verifies them).</summary>
        public GameConfig HostConfig { get; private set; } = GameConfig.Default;

        public void HostSteam(GameConfig? config = null) { Stop(); HostConfig = config ?? GameConfig.Default; Mode = Kind.SteamHost; _steam?.Host(HostConfig.Mode); }
        public void JoinSteamCode(string code) { Stop(); Mode = Kind.SteamJoin; _steam?.JoinByCode(code); }
        public void JoinSteamLobby(ulong lobby) { Stop(); Mode = Kind.SteamJoin; _steam?.Join(lobby); }

        public void HostLan(GameConfig? config = null, int port = LiteNetTransport.DefaultPort)
        {
            Stop(); HostConfig = config ?? GameConfig.Default; Mode = Kind.LanHost;
            _transport = LiteNetTransport.Host(Branding.LobbyGameKey, port);
        }

        /// <summary>"host", "host:port", "[v6]:port".</summary>
        public void JoinLan(string address)
        {
            Stop(); Mode = Kind.LanJoin;
            if (!TryParseAddress(address, out var host, out var port)) { Error = "bad address: " + address; return; }
            _transport = LiteNetTransport.Join(Branding.LobbyGameKey, host, port);
        }

        public static bool TryParseAddress(string s, out string host, out int port)
        {
            s = s.Trim(); host = s; port = LiteNetTransport.DefaultPort;
            if (s.Length == 0) return false;
            int colon = s.LastIndexOf(':');
            if (s.StartsWith('[')) // [v6]:port
            {
                int close = s.IndexOf(']');
                if (close < 0) return false;
                host = s.Substring(1, close - 1);
                if (close + 1 < s.Length) { if (s[close + 1] != ':' || !int.TryParse(s.AsSpan(close + 2), out port)) return false; }
            }
            else if (colon >= 0 && s.IndexOf(':') == colon) // exactly one colon: host:port
            {
                host = s.Substring(0, colon);
                if (!int.TryParse(s.AsSpan(colon + 1), out port)) return false;
            }
            return host.Length > 0 && port is > 0 and < 65536;
        }

        /// <summary>One game tick (38 Hz). <paramref name="start"/> starts a new game after a game over (no coins online).</summary>
        public void Update(in PlayerInput local, bool start)
        {
            if (Mode == Kind.None || Error != null) return;
            if (Session == null)
            {
                if (Steam)
                {
                    if (_steam == null || !_steam.Available) { Error = "Steam is not available"; return; }
                    if (_steam.Phase == SteamPlatform.LobbyPhase.Failed) { Error = _steam.LobbyMessage; return; }
                    bool ready = Mode == Kind.SteamHost ? _steam.Phase == SteamPlatform.LobbyPhase.Hosting : _steam.Phase == SteamPlatform.LobbyPhase.Joined;
                    if (ready && _steam.CreateTransport() is { } st) { _transport = st; StartSession(); }
                }
                else if (_transport != null)
                {
                    _transport.Poll();
                    if (_transport.State == TransportState.Closed) { Error = _transport.Error ?? "connection failed"; return; }
                    if (_transport.State == TransportState.Connected) StartSession();
                }
                if (Session == null) return;
            }
            Session.Update(local, start: start);
            if (Session.Phase == SessionPhase.Failed) Error = Session.Status;
            if (Session.Phase == SessionPhase.Desynced && !_desyncReported) { _desyncReported = true; DesyncReportPath = WriteDesyncReport(); }
        }

        private void StartSession()
        {
            bool host = Mode is Kind.SteamHost or Kind.LanHost;
            var cfg = LockstepSession.DefaultConfig(Environment.TickCount, game: HostConfig);
            Session = new LockstepSession(_transport!, host, Branding.LobbyGameKey, host ? cfg : null) { RecordInputs = true };
            Session.FrameSimulated = (f, s) => FrameSimulated?.Invoke(f, s);
        }

        /// <summary>Lines describing the connection for the status screen.</summary>
        /// <summary>The play mode of this session for status screens: the host's own choice, the received (verified)
        /// config on the guest, or the Steam lobby's advertised mode before that. Null when not online.</summary>
        public string? ModeText()
        {
            if (Mode is Kind.SteamHost or Kind.LanHost) return "MODE: " + Branding.ModeName(HostConfig.Mode);
            if (Mode is not (Kind.SteamJoin or Kind.LanJoin)) return null;
            if (Session?.Config is { } c) return "MODE: " + Branding.ModeName(c.Game.Mode) + " (HOST'S CHOICE)";
            if (Mode == Kind.SteamJoin && _steam?.LobbyMode is { } lm) return "MODE: " + Branding.ModeName(lm) + " (HOST'S CHOICE)";
            return "MODE: SET BY THE HOST";
        }

        /// <summary>The Enhanced options of this session (host: its own; guest: the received config). Null when the
        /// session is not Enhanced, or not known yet.</summary>
        public string? OptionsText()
        {
            GameConfig? c = Mode is Kind.SteamHost or Kind.LanHost ? HostConfig : Session?.Config?.Game;
            if (c is not { Mode: PlayMode.Enhanced } g) return null;
            return "ENHANCED OPTIONS: " + FrontEnd.EnhancedSummary(g.Options) + (Mode is Kind.SteamHost or Kind.LanHost ? "" : " (HOST'S CHOICE)");
        }

        /// <summary>"OTHER PLAYER: V2026.10.17.1" once the peer's Hello arrived (null before, or if it sent no build).</summary>
        public string? PeerBuildText() => Session?.PeerBuild is { Length: > 0 } b ? "OTHER PLAYER: " + BuildInfo.DisplayOf(b) : null;

        public List<string> Describe()
        {
            var l = new List<string>();
            if (Error != null) { l.Add(Error.ToUpperInvariant()); return l; }
            switch (Mode)
            {
                case Kind.SteamHost:
                    if (_steam?.Code is { } code) l.Add("CODE: " + code);
                    l.Add((_steam?.LobbyMessage ?? "").ToUpperInvariant());
                    l.Add("SHARE THE CODE, OR INVITE WITH SHIFT+TAB / THE BUTTON BELOW");
                    break;
                case Kind.SteamJoin:
                    l.Add((_steam?.LobbyMessage ?? "").ToUpperInvariant());
                    break;
                case Kind.LanHost:
                    l.Add(("waiting on UDP port " + ((_transport as LiteNetTransport)?.LocalPort ?? 0)).ToUpperInvariant());
                    foreach (var a in LocalAddresses()) l.Add("THIS PC: " + a.ToUpperInvariant());
                    l.Add("THE OTHER PLAYER CHOOSES JOIN DIRECT IP");
                    break;
                case Kind.LanJoin:
                    l.Add((_transport?.Description ?? "").ToUpperInvariant());
                    break;
            }
            if (Session != null) l.Add(("handshake: " + Session.Status).ToUpperInvariant());
            return l;
        }

        /// <summary>Up, non-loopback IPv4 addresses of this machine (for the LAN host screen).</summary>
        public static List<string> LocalAddresses()
        {
            var r = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var a in ni.GetIPProperties().UnicastAddresses)
                        if (a.Address.AddressFamily == AddressFamily.InterNetwork) r.Add(a.Address.ToString());
                }
            }
            catch (Exception) { /* informational only */ }
            return r.Distinct().Take(3).ToList();
        }

        /// <summary>Writes what is needed to investigate a desync: versions, start config, hashes, every frame's inputs.</summary>
        private string? WriteDesyncReport()
        {
            var s = Session!;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"{Branding.GameName} desync report");
                sb.AppendLine($"time: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
                sb.AppendLine($"protocol: v{Protocol.Version}  game key: {Branding.LobbyGameKey}  build: {BuildInfo.Informational}  peer build: {(s.PeerBuild.Length > 0 ? s.PeerBuild : "unknown")}");
                sb.AppendLine($"role: {(s.IsHost ? "host (P1)" : "guest (P2)")}  transport: {s.Transport.Description}");
                sb.AppendLine($"status: {s.Status}");
                if (s.Config is { } c)
                    sb.AppendLine($"start: delay {c.InputDelay}, canisters {c.Game.Canisters}, auto-fire {c.Game.AutoFire}, pass counter {c.PassCounter}, seeds {string.Join(",", c.Seeds)}");
                sb.AppendLine($"local state hash at frame {s.Frame}: {StateHash.Compute(s.Sim!.State):X8}");
                sb.AppendLine("inputs (frame: p1 p2, bits L1 R2 T4 F8 C16 S32):");
                for (int f = 0; f < s.InputsUsed.Count; f++) sb.Append(f).Append(": ").Append(s.InputsUsed[f].p1).Append(' ').Append(s.InputsUsed[f].p2).AppendLine();
                var path = Path.Combine(ReportDirectory, $"desync-{DateTime.Now:yyyyMMdd-HHmmss}-{(s.IsHost ? "host" : "guest")}.txt");
                File.WriteAllText(path, sb.ToString());
                return path;
            }
            catch (Exception e) { Console.Error.WriteLine("RipOff: could not write desync report: " + e.Message); return null; }
        }

        public void Stop()
        {
            Session?.Dispose(); Session = null;
            _transport?.Dispose(); _transport = null;
            if (Steam) _steam?.Leave();
            Mode = Kind.None; Error = null; DesyncReportPath = null; _desyncReported = false;
        }

        public void Dispose() => Stop();
    }
}
