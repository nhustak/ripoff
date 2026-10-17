using System;
using System.Collections.Generic;

namespace RipOff.Net
{
    /// <summary>
    /// In-memory link between two <see cref="ITransport"/> endpoints with a simulated clock, latency, jitter,
    /// loss, duplication and therefore reordering (unreliable packets are delivered by arrival time, which jitter
    /// shuffles). Reliable packets are never lost and keep their order. Deterministic for a given seed.
    /// Advance time with <see cref="Advance"/>; nothing is delivered before its arrival time.
    /// </summary>
    public sealed class LoopbackNetwork
    {
        public double NowMs { get; private set; }
        public double LatencyMs { get; set; }
        public double JitterMs { get; set; }
        public double LossRate { get; set; }
        public double DuplicateRate { get; set; }
        public long Sent { get; private set; }
        public long Dropped { get; private set; }
        public long Reordered { get; private set; }

        private readonly Random _rng;
        public Endpoint A { get; }
        public Endpoint B { get; }

        public LoopbackNetwork(int seed = 1, double latencyMs = 0, double jitterMs = 0, double lossRate = 0, double duplicateRate = 0)
        {
            _rng = new Random(seed);
            LatencyMs = latencyMs; JitterMs = jitterMs; LossRate = lossRate; DuplicateRate = duplicateRate;
            A = new Endpoint(this, "loopback A"); B = new Endpoint(this, "loopback B");
            A.Peer = B; B.Peer = A;
        }

        public void Advance(double ms) => NowMs += ms;

        private void Deliver(Endpoint to, byte[] data, bool reliable)
        {
            Sent++;
            if (!reliable && _rng.NextDouble() < LossRate) { Dropped++; return; }
            int copies = !reliable && _rng.NextDouble() < DuplicateRate ? 2 : 1;
            for (int c = 0; c < copies; c++)
            {
                double at = NowMs + LatencyMs + _rng.NextDouble() * JitterMs;
                if (reliable) { at = Math.Max(at, to.LastReliableAt); to.LastReliableAt = at; }
                if (to.Queue.Count > 0 && at < to.Queue[^1].at) Reordered++;
                // keep the queue sorted by arrival time (stable for equal times)
                int i = to.Queue.Count;
                while (i > 0 && to.Queue[i - 1].at > at) i--;
                to.Queue.Insert(i, (at, (byte[])data.Clone()));
            }
        }

        public sealed class Endpoint : ITransport
        {
            private readonly LoopbackNetwork _net;
            internal Endpoint? Peer;
            internal readonly List<(double at, byte[] data)> Queue = new();
            internal double LastReliableAt;
            private bool _closed;

            internal Endpoint(LoopbackNetwork net, string name) { _net = net; Description = name; }

            public TransportState State => _closed || (Peer?._closed ?? true) ? TransportState.Closed : TransportState.Connected;
            public string Description { get; }
            public string? Error => State == TransportState.Closed ? "loopback closed" : null;
            public void Poll() { }

            public void Send(byte[] data, int length, bool reliable)
            {
                if (State != TransportState.Connected) return;
                var copy = new byte[length]; Array.Copy(data, copy, length);
                _net.Deliver(Peer!, copy, reliable);
            }

            public bool TryReceive(out byte[] packet)
            {
                if (Queue.Count > 0 && Queue[0].at <= _net.NowMs) { packet = Queue[0].data; Queue.RemoveAt(0); return true; }
                packet = Array.Empty<byte>(); return false;
            }

            /// <summary>Simulates the other side vanishing (cable pulled).</summary>
            public void Kill() => _closed = true;
            public void Dispose() => _closed = true;
        }
    }
}
