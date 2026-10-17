using System;
using System.Text;
using Microsoft.Xna.Framework.Input;
using RipOff.Core;

namespace RipOff.Game
{
    /// <summary>One tick of menu/UI input: edges (one tick per press) from the keyboard and every pad.</summary>
    public struct UiInput
    {
        /// <summary>Navigation: arrows, D-pad, left stick (past <see cref="InputReader.MenuStickThreshold"/>).</summary>
        public bool Up, Down, Left, Right;
        /// <summary>Pad A (also Space on the keyboard).</summary>
        public bool Accept;
        /// <summary>Keyboard Enter.</summary>
        public bool Enter;
        /// <summary>Pad B or keyboard Esc: back / close a menu.</summary>
        public bool Back;
        /// <summary>Keyboard Esc only: opens the pause menu in a game (pad B does not).</summary>
        public bool Escape;
        /// <summary>Pad Start / Menu: opens (and closes) the pause menu in a game.</summary>
        public bool Start;
        /// <summary>F10: Options from anywhere.</summary>
        public bool Options;
        /// <summary>Pad X or keyboard Backspace: delete a character (on-screen keyboard).</summary>
        public bool Delete;
        /// <summary>Any key or any pad button pressed this tick (splash skip).</summary>
        public bool Any;
        /// <summary>M, −, + keyboard shortcuts.</summary>
        public bool Mute, VolumeDown, VolumeUp;
        /// <summary>Text typed this tick (window TextInput).</summary>
        public string Typed;
        /// <summary>A, Enter or Space.</summary>
        public readonly bool Select => Accept || Enter;
    }

    /// <summary>Gameplay controls of one tick: keyboard sets and pads.</summary>
    public struct PlayInput
    {
        /// <summary>2-player mapping: P1 = arrows + Right Ctrl + first pad, P2 = WASD + Left Ctrl + second pad.</summary>
        public PlayerInput P1, P2;
        /// <summary>1-player / online mapping: every keyboard set and every pad OR'd.</summary>
        public PlayerInput All;
    }

    /// <summary>
    /// Turns raw keyboard + pad states into <see cref="UiInput"/> and <see cref="PlayInput"/>. Pure (no MonoGame
    /// device calls), so tests drive every screen with fake <see cref="GamePadState"/> values.
    /// M4 controller fix: every pad index (0..<see cref="MaxPads"/>−1) is read for menus; the stick is turned into
    /// directions with an explicit threshold; and a pad's first tick after connecting gives no edges (hot-plug),
    /// but the second tick does.
    /// </summary>
    public sealed class InputReader
    {
        /// <summary>Pads read (MonoGame DesktopGL supports more; four covers any couch).</summary>
        public const int MaxPads = 4;
        /// <summary>Stick deflection that counts as a menu direction.</summary>
        public const float MenuStickThreshold = 0.5f;

        private KeyboardState _prevKeys;
        private readonly GamePadState[] _prevPads = new GamePadState[MaxPads];
        public PadMapper Pads { get; } = new PadMapper();

        private static readonly Buttons[] AnyButtons =
        {
            Buttons.A, Buttons.B, Buttons.X, Buttons.Y, Buttons.Start, Buttons.Back, Buttons.LeftShoulder, Buttons.RightShoulder,
            Buttons.DPadUp, Buttons.DPadDown, Buttons.DPadLeft, Buttons.DPadRight, Buttons.LeftStick, Buttons.RightStick, Buttons.BigButton,
        };

        public (UiInput ui, PlayInput play) Read(KeyboardState k, ReadOnlySpan<GamePadState> pads, string? typed = null)
        {
            bool Edge(Keys key) => k.IsKeyDown(key) && !_prevKeys.IsKeyDown(key);
            var ui = new UiInput
            {
                Up = Edge(Keys.Up), Down = Edge(Keys.Down), Left = Edge(Keys.Left), Right = Edge(Keys.Right),
                Accept = Edge(Keys.Space), Enter = Edge(Keys.Enter), Back = Edge(Keys.Escape), Escape = Edge(Keys.Escape), Options = Edge(Keys.F10),
                Delete = Edge(Keys.Back),
                Mute = Edge(Keys.M), VolumeDown = Edge(Keys.OemMinus) || Edge(Keys.Subtract), VolumeUp = Edge(Keys.OemPlus) || Edge(Keys.Add),
                Typed = typed ?? "",
            };
            foreach (var key in k.GetPressedKeys()) if (!_prevKeys.IsKeyDown(key)) { ui.Any = true; break; }

            for (int i = 0; i < MaxPads; i++)
            {
                var s = i < pads.Length ? pads[i] : default;
                var p = _prevPads[i];
                _prevPads[i] = s;
                if (!s.IsConnected || !p.IsConnected) continue; // hot-plug: no edges on the connect tick
                bool B(Buttons b) => s.IsButtonDown(b) && !p.IsButtonDown(b);
                var (su, sd, sl, sr) = StickDir(s); var (pu, pd, pl, pr) = StickDir(p);
                ui.Up |= B(Buttons.DPadUp) || (su && !pu);
                ui.Down |= B(Buttons.DPadDown) || (sd && !pd);
                ui.Left |= B(Buttons.DPadLeft) || (sl && !pl);
                ui.Right |= B(Buttons.DPadRight) || (sr && !pr);
                ui.Accept |= B(Buttons.A);
                ui.Back |= B(Buttons.B);
                ui.Start |= B(Buttons.Start);
                ui.Delete |= B(Buttons.X);
                foreach (var b in AnyButtons) if (B(b)) { ui.Any = true; break; }
            }
            _prevKeys = k;

            var pf = Pads.Update(pads);
            var k1 = new PlayerInput { Left = k.IsKeyDown(Keys.Left), Right = k.IsKeyDown(Keys.Right), Thrust = k.IsKeyDown(Keys.Up), Fire = k.IsKeyDown(Keys.RightControl) };
            var k2 = new PlayerInput { Left = k.IsKeyDown(Keys.A), Right = k.IsKeyDown(Keys.D), Thrust = k.IsKeyDown(Keys.W), Fire = k.IsKeyDown(Keys.LeftControl) };
            var play = new PlayInput { P1 = PadMapper.Or(k1, pf.P1), P2 = PadMapper.Or(k2, pf.P2) };
            var all = PadMapper.Or(k1, k2);
            for (int i = 0; i < pads.Length && i < MaxPads; i++) all = PadMapper.Or(all, PadMapper.MapPlayer(pads[i], Pads.StickDeadzone));
            play.All = all;
            return (ui, play);
        }

        private static (bool up, bool down, bool left, bool right) StickDir(in GamePadState s)
        {
            if (!s.IsConnected) return default;
            var v = s.ThumbSticks.Left; // MonoGame: +Y is up
            return (v.Y > MenuStickThreshold, v.Y < -MenuStickThreshold, v.X < -MenuStickThreshold, v.X > MenuStickThreshold);
        }
    }
}
