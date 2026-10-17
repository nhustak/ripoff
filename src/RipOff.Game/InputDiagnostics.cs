using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework.Input;

namespace RipOff.Game
{
    /// <summary>One MonoGame pad slot as the host saw it this tick (INPUT TEST screen, input log).</summary>
    public struct PadSlot
    {
        public GamePadState State;
        /// <summary>GamePad.GetCapabilities(i).DisplayName (SDL controller name), or "" when unknown.</summary>
        public string Name;
        /// <summary>GamePad.GetCapabilities(i).GamePadType as text, or "".</summary>
        public string Type;
        public bool Connected => State.IsConnected;
    }

    /// <summary>Text helpers shared by the INPUT TEST screen and the input log.</summary>
    public static class PadText
    {
        private static readonly (Microsoft.Xna.Framework.Input.Buttons b, string name)[] Names =
        {
            (Microsoft.Xna.Framework.Input.Buttons.A, "A"), (Microsoft.Xna.Framework.Input.Buttons.B, "B"), (Microsoft.Xna.Framework.Input.Buttons.X, "X"), (Microsoft.Xna.Framework.Input.Buttons.Y, "Y"),
            (Microsoft.Xna.Framework.Input.Buttons.LeftShoulder, "LB"), (Microsoft.Xna.Framework.Input.Buttons.RightShoulder, "RB"), (Microsoft.Xna.Framework.Input.Buttons.Back, "BACK"), (Microsoft.Xna.Framework.Input.Buttons.Start, "START"),
            (Microsoft.Xna.Framework.Input.Buttons.LeftStick, "LS"), (Microsoft.Xna.Framework.Input.Buttons.RightStick, "RS"), (Microsoft.Xna.Framework.Input.Buttons.BigButton, "GUIDE"),
            (Microsoft.Xna.Framework.Input.Buttons.DPadUp, "UP"), (Microsoft.Xna.Framework.Input.Buttons.DPadDown, "DOWN"), (Microsoft.Xna.Framework.Input.Buttons.DPadLeft, "LEFT"), (Microsoft.Xna.Framework.Input.Buttons.DPadRight, "RIGHT"),
        };

        public static string Pressed(in GamePadState s)
        {
            var sb = new StringBuilder();
            foreach (var (b, n) in Names) if (s.IsButtonDown(b)) sb.Append(sb.Length > 0 ? " " : "").Append(n);
            return sb.Length == 0 ? "-" : sb.ToString();
        }

        public static string Axes(in GamePadState s) =>
            $"LS {s.ThumbSticks.Left.X:+0.00;-0.00} {s.ThumbSticks.Left.Y:+0.00;-0.00}  RS {s.ThumbSticks.Right.X:+0.00;-0.00} {s.ThumbSticks.Right.Y:+0.00;-0.00}  LT {s.Triggers.Left:0.00}  RT {s.Triggers.Right:0.00}";

        /// <summary>Upper case, only characters the vector font has.</summary>
        public static string ForFont(string? s)
        {
            const string ok = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.:-/()%+,=_[] ";
            var sb = new StringBuilder();
            foreach (char c in (s ?? "").ToUpperInvariant()) sb.Append(ok.IndexOf(c) >= 0 ? c : '?');
            return sb.ToString();
        }
    }

    /// <summary>
    /// %APPDATA%\RipOffRemake\input-log.txt: what Windows/SDL/MonoGame report about the pads and Steam, so Nick can
    /// send it to us. Rewritten each launch: a snapshot of all pad slots after <see cref="SnapshotAfterTicks"/> (SDL
    /// only reports pads once the window's event loop runs), then appended events (pad connect/disconnect, Steam
    /// init), at most <see cref="MaxEvents"/>. Never throws; holds no secrets (no Steam IDs or tokens).
    /// </summary>
    public sealed class InputLog
    {
        public const int SnapshotAfterTicks = 76; // 2 s at 38 Hz
        public const int MaxEvents = 60;
        public string FilePath { get; }
        private readonly List<string> _header = new();
        private readonly bool[] _wasConnected = new bool[InputReader.MaxPads];
        private int _ticks, _events;
        private bool _snapshotDone;

        public InputLog(string directory) { FilePath = Path.Combine(directory, "input-log.txt"); }

        public void Header(string line) => _header.Add(line);

        /// <summary>Call once per tick with the slots the game read.</summary>
        public void Tick(ReadOnlySpan<PadSlot> pads, string steamStatus)
        {
            _ticks++;
            if (!_snapshotDone)
            {
                if (_ticks < SnapshotAfterTicks) return;
                _snapshotDone = true;
                var sb = new StringBuilder();
                sb.AppendLine("RipOff input log, " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
                foreach (var h in _header) sb.AppendLine(h);
                sb.AppendLine("steam now: " + steamStatus);
                sb.AppendLine($"pads after {SnapshotAfterTicks / 38.0:0} s:");
                for (int i = 0; i < pads.Length; i++) { sb.AppendLine("  " + Describe(i, pads[i])); _wasConnected[i] = pads[i].Connected; }
                sb.AppendLine("events:");
                Write(sb.ToString(), append: false);
                return;
            }
            for (int i = 0; i < pads.Length && i < _wasConnected.Length; i++)
                if (pads[i].Connected != _wasConnected[i])
                {
                    _wasConnected[i] = pads[i].Connected;
                    Event((pads[i].Connected ? "connected: " : "disconnected: ") + Describe(i, pads[i]));
                }
        }

        public void Event(string text)
        {
            if (!_snapshotDone) { _header.Add("(before snapshot) " + text); return; }
            if (_events++ >= MaxEvents) return;
            Write($"  {DateTimeOffset.Now:HH:mm:ss} {text}{Environment.NewLine}", append: true);
        }

        public static string Describe(int slot, in PadSlot p) => p.Connected
            ? $"slot {slot}: CONNECTED name=\"{p.Name}\" type={p.Type} buttons={PadText.Pressed(p.State)} {PadText.Axes(p.State)}"
            : $"slot {slot}: not connected";

        private void Write(string text, bool append)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (append) File.AppendAllText(FilePath, text); else File.WriteAllText(FilePath, text);
            }
            catch (Exception e) { Console.Error.WriteLine("RipOff: input log not written: " + e.Message); /* diagnostics must never stop the game */ }
        }
    }
}
