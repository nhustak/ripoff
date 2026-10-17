using System;
using System.Collections.Generic;
using LiteNetLib;

namespace RipOff.Net
{
    /// <summary>
    /// Direct UDP link (LAN or a forwarded port) with LiteNetLib. The host listens on a port and accepts one peer
    /// whose connection key matches (game key + protocol version), the guest connects to an address. Unreliable
    /// packets use DeliveryMethod.Unreliable, reliable ones ReliableOrdered (channel 0).
    /// </summary>
    public sealed class LiteNetTransport : ITransport
    {
        public const int DefaultPort = 47815; // arbitrary unregistered-looking port; configurable in the menu

        private readonly EventBasedNetListener _listener = new();
        private readonly NetManager _net;
        private readonly Queue<byte[]> _inbox = new();
        private readonly bool _host;
        private NetPeer? _peer;
        private bool _closed, _disposed;

        public TransportState State => _closed ? TransportState.Closed : _peer != null ? TransportState.Connected : TransportState.Connecting;
        public string Description { get; private set; }
        public string? Error { get; private set; }
        public int LocalPort => _net.LocalPort;

        /// <summary>Connection key: peers with a different game key or protocol version are refused at connect.</summary>
        public static string ConnectionKey(string gameKey) => gameKey + "/" + Protocol.Version;

        private LiteNetTransport(bool host, string key, string description)
        {
            _host = host; Description = description;
            _net = new NetManager(_listener) { DisconnectTimeout = 10000, UnconnectedMessagesEnabled = false };
            _listener.ConnectionRequestEvent += req =>
            {
                if (!_host || _peer != null) { req.Reject(); return; }
                req.AcceptIfKey(key);
            };
            _listener.PeerConnectedEvent += p =>
            {
                if (_peer == null) { _peer = p; Description = (_host ? "LAN host, peer " : "LAN ") + p.Address + ":" + p.Port; }
            };
            _listener.PeerDisconnectedEvent += (p, info) =>
            {
                if (p == _peer || _peer == null) { _closed = true; Error = "disconnected: " + info.Reason; }
            };
            _listener.NetworkReceiveEvent += (p, reader, channel, method) =>
            {
                _inbox.Enqueue(reader.GetRemainingBytes());
                reader.Recycle();
            };
            _listener.NetworkErrorEvent += (ep, err) => { Error = "network error: " + err; };
        }

        /// <summary>Listens on <paramref name="port"/> (0 = any free port) for one guest.</summary>
        public static LiteNetTransport Host(string gameKey, int port = DefaultPort)
        {
            var t = new LiteNetTransport(true, ConnectionKey(gameKey), "LAN host, waiting on port " + port);
            if (!t._net.Start(port)) { t._closed = true; t.Error = "could not listen on UDP port " + port; }
            else t.Description = "LAN host, waiting on port " + t._net.LocalPort;
            return t;
        }

        /// <summary>Connects to a host at <paramref name="address"/>:<paramref name="port"/>.</summary>
        public static LiteNetTransport Join(string gameKey, string address, int port = DefaultPort)
        {
            var t = new LiteNetTransport(false, ConnectionKey(gameKey), "connecting to " + address + ":" + port);
            if (!t._net.Start()) { t._closed = true; t.Error = "could not open a UDP socket"; return t; }
            try { t._net.Connect(address, port, ConnectionKey(gameKey)); }
            catch (Exception e) { t._closed = true; t.Error = "bad address: " + e.Message; }
            return t;
        }

        public void Poll() { if (!_disposed) _net.PollEvents(); }

        public void Send(byte[] data, int length, bool reliable)
        {
            if (_peer == null || _closed) return;
            _peer.Send(data, 0, length, 0, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
        }

        public bool TryReceive(out byte[] packet)
        {
            if (_inbox.Count > 0) { packet = _inbox.Dequeue(); return true; }
            packet = Array.Empty<byte>(); return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            try { _net.PollEvents(); _net.Stop(true); } catch (Exception) { }
            _closed = true; _disposed = true;
        }
    }
}
