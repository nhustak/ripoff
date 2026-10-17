using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using RipOff.Net;
using Steamworks;

namespace RipOff.Game.Steam
{
    /// <summary>
    /// <see cref="ITransport"/> over ISteamNetworkingMessages (channel 0) to one Steam user. Sessions are implicit
    /// (created by the first send, accepted by <see cref="SteamPlatform"/>), routed through Steam Datagram Relay
    /// (ICE disabled in SteamPlatform), so no port forwarding is needed. UNCHECKED: not run against Steam.
    /// </summary>
    public sealed class SteamTransport : ITransport
    {
        private readonly SteamPlatform _platform;
        private readonly CSteamID _peer;
        private SteamNetworkingIdentity _id;
        private readonly IntPtr[] _msgs = new IntPtr[64];
        private readonly Queue<byte[]> _inbox = new();
        private bool _closed;

        public SteamTransport(SteamPlatform platform, CSteamID peer)
        {
            _platform = platform; _peer = peer;
            _id = new SteamNetworkingIdentity(); _id.SetSteamID(peer);
            Description = "Steam relay with " + SteamFriends.GetFriendPersonaName(peer);
        }

        public TransportState State
        {
            get
            {
                if (_closed || _platform.PeerLeft || _platform.FailedSessionWith == _peer) return TransportState.Closed;
                return TransportState.Connected;
            }
        }

        public string Description { get; }
        public string? Error { get; private set; }

        public void Poll()
        {
            if (_closed) return;
            int n = SteamNetworkingMessages.ReceiveMessagesOnChannel(0, _msgs, _msgs.Length);
            for (int i = 0; i < n; i++)
            {
                var m = SteamNetworkingMessage_t.FromIntPtr(_msgs[i]);
                if (m.m_identityPeer.GetSteamID() == _peer && m.m_cbSize >= 0)
                {
                    var b = new byte[m.m_cbSize];
                    Marshal.Copy(m.m_pData, b, 0, m.m_cbSize);
                    _inbox.Enqueue(b);
                }
                SteamNetworkingMessage_t.Release(_msgs[i]);
            }
            if (_platform.PeerLeft) Error = "the other player left";
            else if (_platform.FailedSessionWith == _peer) Error = "Steam connection to the other player failed";
        }

        public void Send(byte[] data, int length, bool reliable)
        {
            if (_closed) return;
            int flags = (reliable ? Constants.k_nSteamNetworkingSend_Reliable : Constants.k_nSteamNetworkingSend_UnreliableNoDelay)
                        | Constants.k_nSteamNetworkingSend_AutoRestartBrokenSession;
            var h = GCHandle.Alloc(data, GCHandleType.Pinned);
            try { SteamNetworkingMessages.SendMessageToUser(ref _id, h.AddrOfPinnedObject(), (uint)length, flags, 0); }
            finally { h.Free(); }
        }

        public bool TryReceive(out byte[] packet)
        {
            if (_inbox.Count > 0) { packet = _inbox.Dequeue(); return true; }
            packet = Array.Empty<byte>(); return false;
        }

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            try { SteamNetworkingMessages.CloseSessionWithUser(ref _id); } catch (Exception) { }
        }
    }
}
