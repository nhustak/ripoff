using System;

namespace RipOff.Net
{
    public enum TransportState { Connecting, Connected, Closed }

    /// <summary>
    /// A point-to-point packet link to the one other player. Implementations: <see cref="LoopbackNetwork"/>
    /// (in-memory, tests), <see cref="LiteNetTransport"/> (UDP direct IP / LAN), and RipOff.Game's SteamTransport
    /// (Steam Datagram Relay). The lockstep session only uses this interface, so game logic never touches Steam.
    /// </summary>
    public interface ITransport : IDisposable
    {
        TransportState State { get; }
        /// <summary>Human-readable link description for the UI, e.g. "Steam relay" or "LAN 192.168.1.5:47815".</summary>
        string Description { get; }
        /// <summary>Why the link closed or failed, if it did.</summary>
        string? Error { get; }
        /// <summary>Pumps the underlying network. Called once per game tick before receiving.</summary>
        void Poll();
        /// <summary>Sends one packet. Unreliable packets may be lost, duplicated or reordered; reliable ones arrive
        /// once and in order relative to other reliable packets.</summary>
        void Send(byte[] data, int length, bool reliable);
        /// <summary>Returns the next received packet, if any.</summary>
        bool TryReceive(out byte[] packet);
    }
}
