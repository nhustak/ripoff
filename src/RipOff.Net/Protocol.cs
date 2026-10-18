using System;
using System.Collections.Generic;
using System.Text;
using RipOff.Core;

namespace RipOff.Net
{
    /// <summary>Wire format (little-endian). Bumping <see cref="Version"/> makes old and new builds refuse each other.</summary>
    public static class Protocol
    {
        /// <summary>Protocol version; peers with a different value are rejected (Hello).</summary>
        public const ushort Version = 5; // 2: auto-fire accepted (M4); 3: play mode (Arcade/Enhanced) in the game config; 4: Enhanced options byte;
                                         // 5: options 16-bit + ships in the game config (schema 4), ship wish in Hello

        public const byte MsgHello = 1, MsgReject = 2, MsgStart = 3, MsgInput = 4, MsgBye = 5, MsgStartAck = 6;

        /// <summary>Input frames carried per packet at most (older unacknowledged frames are resent first).</summary>
        public const int MaxInputsPerPacket = 64;

        // ---- per-frame input byte: one player's controls plus cabinet buttons ----
        // Online play has no coins or credits (bit 16 was a coin bit in early drafts and is unused).
        public const byte InLeft = 1, InRight = 2, InThrust = 4, InFire = 8, InStart = 32;

        public static byte Pack(in PlayerInput p, bool start) => (byte)(
            (p.Left ? InLeft : 0) | (p.Right ? InRight : 0) | (p.Thrust ? InThrust : 0) | (p.Fire ? InFire : 0)
            | (start ? InStart : 0));

        public static PlayerInput Unpack(byte b) => new PlayerInput
        {
            Left = (b & InLeft) != 0, Right = (b & InRight) != 0, Thrust = (b & InThrust) != 0, Fire = (b & InFire) != 0,
        };

        // ---- messages ----
        /// <summary><paramref name="Build"/>: the sender's BuildInfo.Informational ("2026.10.17.1+abc1234"), for display
        /// only; the protocol <paramref name="Version"/> decides compatibility. Optional on the wire (trailing; older v3
        /// builds do not send it).
        /// <paramref name="Ship"/>: the ship the sender wants to fly (Ships.All id; <see cref="NoShip"/> = no wish). The
        /// host resolves both ships into the start config (LockstepSession); trailing and optional on the wire.</summary>
        public readonly record struct Hello(ushort Version, string GameKey, bool FromHost, string Build = "", int Ship = NoShip);
        public const int NoShip = 0xFF;
        /// <summary>Everything both peers need to build the same simulation: the shared <see cref="GameConfig"/>
        /// (rules/options) plus the session's input delay and the per-game random start (pass counter, RNG seeds).</summary>
        public readonly record struct StartConfig(int InputDelay, GameConfig Game, int PassCounter, int[] Seeds);
        public readonly record struct InputMsg(int Ack, int FirstFrame, byte[] Inputs, (int frame, uint hash)[] Checks);

        public sealed class Writer
        {
            private readonly List<byte> _b = new();
            public Writer U8(int v) { _b.Add((byte)v); return this; }
            public Writer U16(int v) { _b.Add((byte)v); _b.Add((byte)(v >> 8)); return this; }
            public Writer I32(int v) { for (int i = 0; i < 4; i++) _b.Add((byte)(v >> (8 * i))); return this; }
            public Writer Str(string s) { var u = Encoding.UTF8.GetBytes(s); U8(Math.Min(u.Length, 255)); for (int i = 0; i < Math.Min(u.Length, 255); i++) _b.Add(u[i]); return this; }
            public byte[] ToArray() => _b.ToArray();
        }

        public sealed class Reader
        {
            private readonly byte[] _d; private int _p;
            public Reader(byte[] d) { _d = d; }
            private void Need(int n) { if (_p + n > _d.Length) throw new FormatException("packet too short"); }
            public int U8() { Need(1); return _d[_p++]; }
            public int U16() { Need(2); int v = _d[_p] | (_d[_p + 1] << 8); _p += 2; return v; }
            public int I32() { Need(4); int v = _d[_p] | (_d[_p + 1] << 8) | (_d[_p + 2] << 16) | (_d[_p + 3] << 24); _p += 4; return v; }
            public string Str() { int n = U8(); Need(n); var s = Encoding.UTF8.GetString(_d, _p, n); _p += n; return s; }
            public byte[] Bytes(int n) { Need(n); var r = new byte[n]; Array.Copy(_d, _p, r, 0, n); _p += n; return r; }
            public int Remaining => _d.Length - _p;
        }

        public static byte[] EncodeHello(in Hello h) => new Writer().U8(MsgHello).U16(h.Version).Str(h.GameKey).U8(h.FromHost ? 1 : 0).Str(h.Build).U8(h.Ship).ToArray();
        public static Hello DecodeHello(Reader r)
        {
            var version = (ushort)r.U16(); var key = r.Str(); bool fromHost = r.U8() != 0;
            string build = r.Remaining > 0 ? r.Str() : "";
            return new Hello(version, key, fromHost, build, r.Remaining > 0 ? r.U8() : NoShip);
        }

        public static byte[] EncodeReject(string why) => new Writer().U8(MsgReject).Str(why).ToArray();

        public static byte[] EncodeStart(in StartConfig c)
        {
            var w = new Writer().U8(MsgStart).U8(c.InputDelay);
            var g = c.Game.Encode(); w.U8(g.Length); foreach (var b in g) w.U8(b);
            w.U16(c.PassCounter).U8(c.Seeds.Length);
            foreach (var s in c.Seeds) w.U16(s);
            return w.ToArray();
        }

        /// <summary>FNV-1a over the encoded start message: the guest echoes it (StartAck) so the host can verify
        /// that both sides will run exactly the same config.</summary>
        public static uint StartHash(in StartConfig c)
        {
            uint h = 2166136261;
            foreach (var b in EncodeStart(c)) { h ^= b; h *= 16777619; }
            return h;
        }

        public static byte[] EncodeStartAck(uint hash) => new Writer().U8(MsgStartAck).I32((int)hash).ToArray();
        public static uint DecodeStartAck(Reader r) => (uint)r.I32();

        public static StartConfig DecodeStart(Reader r)
        {
            int d = r.U8();
            var game = GameConfig.Decode(r.Bytes(r.U8()));
            int pc = r.U16(), n = r.U8();
            var seeds = new int[n]; for (int i = 0; i < n; i++) seeds[i] = r.U16();
            return new StartConfig(d, game, pc, seeds);
        }

        public static byte[] EncodeInput(in InputMsg m)
        {
            var w = new Writer().U8(MsgInput).I32(m.Ack).I32(m.FirstFrame).U8(m.Inputs.Length);
            foreach (var b in m.Inputs) w.U8(b);
            w.U8(m.Checks.Length);
            foreach (var (f, h) in m.Checks) w.I32(f).I32((int)h);
            return w.ToArray();
        }

        public static InputMsg DecodeInput(Reader r)
        {
            int ack = r.I32(), first = r.I32(), n = r.U8();
            var inputs = r.Bytes(n);
            int k = r.U8(); var checks = new (int, uint)[k];
            for (int i = 0; i < k; i++) checks[i] = (r.I32(), (uint)r.I32());
            return new InputMsg(ack, first, inputs, checks);
        }
    }
}
