using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RipOff.Core;
using RipOff.Net;
using Steamworks;

namespace RipOff.Game.Steam
{
    /// <summary>
    /// Steam client integration through Steamworks.NET: init (optional — the game runs without Steam), lobbies
    /// tagged with <see cref="Branding.LobbyGameKey"/> + protocol version, 6-character lobby codes, friend invites
    /// (overlay invite dialog, "Join game" from the friends list via rich presence "connect", and the
    /// +connect_lobby command line), and SteamNetworkingMessages session acceptance for <see cref="SteamTransport"/>.
    /// UNCHECKED: none of this has run against a real Steam client (the build box has none); see README.
    /// </summary>
    public sealed class SteamPlatform : ISteamUi, IDisposable
    {
        public enum LobbyPhase { None, Creating, Hosting, Searching, Joining, Joined, Failed }

        public const string KeyGame = "game", KeyVersion = "ver", KeyCode = "code", KeyMode = "mode";

        public bool Available { get; private set; }
        public string Status { get; private set; } = "Steam not initialized";
        public LobbyPhase Phase { get; private set; }
        public string LobbyMessage { get; private set; } = "";
        public CSteamID Lobby { get; private set; } = CSteamID.Nil;
        public string? Code { get; private set; }
        /// <summary>The other player (lobby member for the host, lobby owner for the guest).</summary>
        public CSteamID Peer { get; private set; } = CSteamID.Nil;
        public bool PeerLeft { get; private set; }
        /// <summary>A lobby the user asked to join from Steam (invite, friends list, or command line).</summary>
        public ulong? PendingJoin { get; set; }
        public ulong? TakePendingJoin() { var p = PendingJoin; PendingJoin = null; return p; }
        public bool Initialized => true;
        public bool EnsureInit() => Available;
        public string PersonaName { get; private set; } = "";
        /// <summary>Controllers through ISteamInput (NOTES §25); null until Steam is up.</summary>
        public SteamInputPads? InputPads { get; private set; }

        private Callback<GameLobbyJoinRequested_t>? _cbLobbyJoin;
        private Callback<GameRichPresenceJoinRequested_t>? _cbRpJoin;
        private Callback<LobbyChatUpdate_t>? _cbChat;
        private Callback<SteamNetworkingMessagesSessionRequest_t>? _cbSession;
        private Callback<SteamNetworkingMessagesSessionFailed_t>? _cbSessionFailed;
        private CallResult<LobbyCreated_t>? _crCreate;
        private CallResult<LobbyMatchList_t>? _crList;
        private CallResult<LobbyEnter_t>? _crEnter;
        private string _searchCode = "";
        internal CSteamID FailedSessionWith = CSteamID.Nil;

        /// <summary>SteamAPI_Init. Called through <see cref="SteamGate"/> (lazy by default). The overlay hooks the graphics
        /// device only if this ran before the device was created (Steamworks "Steam Overlay" docs).</summary>
        public static SteamPlatform Init(string[] args)
        {
            var p = new SteamPlatform();
            try
            {
                var r = SteamAPI.InitEx(out string err);
                if (r != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
                {
                    p.Status = "Steam not available (" + Short(r) + (string.IsNullOrEmpty(err) ? "" : ": " + err) + ")";
                    return p;
                }
            }
            catch (DllNotFoundException) { p.Status = "Steam library missing (steam_api64.dll)"; return p; }
            catch (Exception e) { p.Status = "Steam failed to start: " + e.GetType().Name; return p; }

            p.Available = true;
            p.Status = "Steam OK";
            try
            {
                p.PersonaName = SteamFriends.GetPersonaName();
                SteamNetworkingUtils.InitRelayNetworkAccess();
                RelayOnly();
                p._cbLobbyJoin = Callback<GameLobbyJoinRequested_t>.Create(p.OnLobbyJoinRequested);
                p._cbRpJoin = Callback<GameRichPresenceJoinRequested_t>.Create(p.OnRichPresenceJoin);
                p._cbChat = Callback<LobbyChatUpdate_t>.Create(p.OnChatUpdate);
                p._cbSession = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(p.OnSessionRequest);
                p._cbSessionFailed = Callback<SteamNetworkingMessagesSessionFailed_t>.Create(p.OnSessionFailed);
                p._crCreate = CallResult<LobbyCreated_t>.Create(p.OnLobbyCreated);
                p._crList = CallResult<LobbyMatchList_t>.Create(p.OnLobbyList);
                p._crEnter = CallResult<LobbyEnter_t>.Create(p.OnLobbyEnter);
            }
            catch (Exception e) { p.Status = "Steam setup error: " + e.Message; }
            p.InputPads = SteamInputPads.Start();
            p.PendingJoin = ParseConnect(string.Join(' ', args));
            return p;
        }

        private static string Short(ESteamAPIInitResult r) => r.ToString().Replace("k_ESteamAPIInitResult_", "");

        /// <summary>Disables ICE (direct NAT punch-through) so peer traffic always goes through Steam Datagram Relay
        /// and no IP address is shared with the other player (k_ESteamNetworkingConfig_P2P_Transport_ICE_Enable).</summary>
        private static void RelayOnly()
        {
            int v = Constants.k_nSteamNetworkingConfig_P2P_Transport_ICE_Enable_Disable;
            var h = GCHandle.Alloc(v, GCHandleType.Pinned);
            try
            {
                SteamNetworkingUtils.SetConfigValue(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_P2P_Transport_ICE_Enable,
                    ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero,
                    ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, h.AddrOfPinnedObject());
            }
            finally { h.Free(); }
        }

        /// <summary>"+connect_lobby 1234" (Steam's join command line / rich presence connect string) → lobby id.</summary>
        public static ulong? ParseConnect(string s)
        {
            var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < parts.Length; i++)
                if (parts[i] == "+connect_lobby" && ulong.TryParse(parts[i + 1], out var id) && id != 0) return id;
            return null;
        }

        public void RunCallbacks() { if (Available) SteamAPI.RunCallbacks(); }

        // ------------------------------------------------------------------ hosting

        /// <summary>Play mode advertised by the lobby (host: its own; guest: read on entering). Informational: the
        /// guest adopts the mode from the session's verified game config (LockstepSession), not from here.</summary>
        public PlayMode? LobbyMode { get; private set; }

        public void Host(PlayMode mode = PlayMode.Arcade)
        {
            if (!Available) return;
            Leave();
            LobbyMode = mode;
            Phase = LobbyPhase.Creating; LobbyMessage = "creating lobby...";
            // Public so the code search can find it; every search filters on game key + version (below).
            _crCreate!.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, 2));
        }

        private void OnLobbyCreated(LobbyCreated_t r, bool ioFailure)
        {
            if (Phase != LobbyPhase.Creating) return;
            if (ioFailure || r.m_eResult != EResult.k_EResultOK) { Fail("could not create lobby (" + r.m_eResult + ")"); return; }
            Lobby = new CSteamID(r.m_ulSteamIDLobby);
            Code = NewCode();
            SteamMatchmaking.SetLobbyData(Lobby, KeyGame, Branding.LobbyGameKey);
            SteamMatchmaking.SetLobbyData(Lobby, KeyVersion, Protocol.Version.ToString());
            SteamMatchmaking.SetLobbyData(Lobby, KeyCode, Code);
            SteamMatchmaking.SetLobbyData(Lobby, KeyMode, ((int)(LobbyMode ?? PlayMode.Arcade)).ToString());
            SteamFriends.SetRichPresence("connect", "+connect_lobby " + Lobby.m_SteamID);
            SteamFriends.SetRichPresence("status", Branding.GameName + " - waiting for a player");
            Phase = LobbyPhase.Hosting; LobbyMessage = "waiting for a friend";
        }

        public static string NewCode()
        {
            Span<char> c = stackalloc char[OnScreenKeyboard.CodeLength];
            for (int i = 0; i < c.Length; i++) c[i] = OnScreenKeyboard.CodeAlphabet[RandomNumberGenerator.GetInt32(OnScreenKeyboard.CodeAlphabet.Length)];
            return new string(c);
        }

        private void OnChatUpdate(LobbyChatUpdate_t r)
        {
            if (r.m_ulSteamIDLobby != Lobby.m_SteamID) return;
            var user = new CSteamID(r.m_ulSteamIDUserChanged);
            if (user == SteamUser.GetSteamID()) return;
            if ((r.m_rgfChatMemberStateChange & (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0)
            {
                if (Phase == LobbyPhase.Hosting && Peer == CSteamID.Nil)
                {
                    Peer = user; PeerLeft = false;
                    SteamMatchmaking.SetLobbyJoinable(Lobby, false);
                    LobbyMessage = SteamFriends.GetFriendPersonaName(user) + " joined";
                }
            }
            else if (user == Peer) { PeerLeft = true; LobbyMessage = "the other player left the lobby"; }
        }

        public void OpenInviteOverlay() { if (Available && Lobby != CSteamID.Nil) SteamFriends.ActivateGameOverlayInviteDialog(Lobby); }

        // ------------------------------------------------------------------ joining

        public void JoinByCode(string code)
        {
            if (!Available) return;
            Leave();
            _searchCode = code.ToUpperInvariant();
            Phase = LobbyPhase.Searching; LobbyMessage = "looking for code " + _searchCode;
            SteamMatchmaking.AddRequestLobbyListStringFilter(KeyGame, Branding.LobbyGameKey, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter(KeyVersion, Protocol.Version.ToString(), ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter(KeyCode, _searchCode, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(5);
            _crList!.Set(SteamMatchmaking.RequestLobbyList());
        }

        private void OnLobbyList(LobbyMatchList_t r, bool ioFailure)
        {
            if (Phase != LobbyPhase.Searching) return;
            if (ioFailure || r.m_nLobbiesMatching == 0) { Fail("no open game with code " + _searchCode); return; }
            Join(SteamMatchmaking.GetLobbyByIndex(0).m_SteamID);
        }

        /// <summary>Joins a lobby by id (from a code search, an invite or +connect_lobby).</summary>
        public void Join(ulong lobbyId)
        {
            if (!Available) return;
            if (Phase != LobbyPhase.Searching) Leave();
            Phase = LobbyPhase.Joining; LobbyMessage = "joining lobby...";
            _crEnter!.Set(SteamMatchmaking.JoinLobby(new CSteamID(lobbyId)));
        }

        private void OnLobbyEnter(LobbyEnter_t r, bool ioFailure)
        {
            if (Phase != LobbyPhase.Joining) return;
            var lobby = new CSteamID(r.m_ulSteamIDLobby);
            if (ioFailure || r.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            { Fail("could not join (" + (EChatRoomEnterResponse)r.m_EChatRoomEnterResponse + ")"); return; }
            Lobby = lobby;
            // Reject lobbies of other games sharing App ID 480, and other builds of this one.
            string game = SteamMatchmaking.GetLobbyData(lobby, KeyGame), ver = SteamMatchmaking.GetLobbyData(lobby, KeyVersion);
            if (game != Branding.LobbyGameKey) { Fail("that lobby is not a " + Branding.GameName + " game"); return; }
            if (ver != Protocol.Version.ToString()) { Fail("version mismatch: host v" + ver + ", this build v" + Protocol.Version); return; }
            Peer = SteamMatchmaking.GetLobbyOwner(lobby); PeerLeft = false;
            Code = SteamMatchmaking.GetLobbyData(lobby, KeyCode);
            LobbyMode = int.TryParse(SteamMatchmaking.GetLobbyData(lobby, KeyMode), out var lm) && GameRules.IsDefined((PlayMode)lm) ? (PlayMode)lm : null;
            Phase = LobbyPhase.Joined; LobbyMessage = "joined " + SteamFriends.GetFriendPersonaName(Peer);
        }

        private void OnLobbyJoinRequested(GameLobbyJoinRequested_t r) => PendingJoin = r.m_steamIDLobby.m_SteamID;
        private void OnRichPresenceJoin(GameRichPresenceJoinRequested_t r) { var id = ParseConnect(r.m_rgchConnect); if (id != null) PendingJoin = id; }

        // ------------------------------------------------------------------ networking sessions

        private void OnSessionRequest(SteamNetworkingMessagesSessionRequest_t r)
        {
            var id = r.m_identityRemote;
            var user = id.GetSteamID();
            if (user == Peer || IsLobbyMember(user)) SteamNetworkingMessages.AcceptSessionWithUser(ref id);
        }

        private void OnSessionFailed(SteamNetworkingMessagesSessionFailed_t r) => FailedSessionWith = r.m_info.m_identityRemote.GetSteamID();

        private bool IsLobbyMember(CSteamID user)
        {
            if (Lobby == CSteamID.Nil) return false;
            int n = SteamMatchmaking.GetNumLobbyMembers(Lobby);
            for (int i = 0; i < n; i++) if (SteamMatchmaking.GetLobbyMemberByIndex(Lobby, i) == user) return true;
            return false;
        }

        public SteamTransport? CreateTransport() => Peer == CSteamID.Nil ? null : new SteamTransport(this, Peer);

        // ------------------------------------------------------------------ lifecycle

        private void Fail(string why) { Phase = LobbyPhase.Failed; LobbyMessage = why; if (Lobby != CSteamID.Nil) { SteamMatchmaking.LeaveLobby(Lobby); Lobby = CSteamID.Nil; } }

        /// <summary>Leaves any lobby and clears rich presence.</summary>
        public void Leave()
        {
            if (!Available) return;
            if (Lobby != CSteamID.Nil) SteamMatchmaking.LeaveLobby(Lobby);
            Lobby = CSteamID.Nil; Peer = CSteamID.Nil; Code = null; PeerLeft = false; FailedSessionWith = CSteamID.Nil;
            Phase = LobbyPhase.None; LobbyMessage = ""; LobbyMode = null;
            SteamFriends.ClearRichPresence();
        }

        public void Dispose()
        {
            if (!Available) return;
            try { Leave(); } catch (Exception) { }
            _cbLobbyJoin?.Dispose(); _cbRpJoin?.Dispose(); _cbChat?.Dispose(); _cbSession?.Dispose(); _cbSessionFailed?.Dispose();
            _crCreate?.Dispose(); _crList?.Dispose(); _crEnter?.Dispose();
            InputPads?.Shutdown();
            SteamAPI.Shutdown();
            Available = false;
        }
    }
}
