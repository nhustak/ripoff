using Microsoft.Xna.Framework.Input;
using RipOff.Core;

namespace RipOff.Game
{
    /// <summary>Inputs taken from the game pads for one pass; OR'd with the keyboard by the host.</summary>
    public struct PadFrame
    {
        public PlayerInput P1, P2;
        public bool Coin, Start1, Start2;
    }

    /// <summary>
    /// Game pad → arcade controls. Pad index 0 drives player 1, index 1 player 2 (MonoGame GamePad, SDL2
    /// GameController on DesktopGL; buttons named in Xbox layout).
    ///   turn:   left stick X beyond <see cref="StickDeadzone"/>, or D-pad left/right
    ///   thrust: A, or right trigger beyond <see cref="TriggerThreshold"/>
    ///   fire:   X, or right bumper (passed as a level: the simulation edge-detects fire like the arcade button)
    ///   coin:   Back / View (either pad, on press)
    ///   start:  Start / Menu on pad 0 = 1-player start; Start on pad 1, or LB + Start on pad 0 = 2-player start
    /// Coin and start are edges (one pass per press), like the keyboard keys: the simulation adds a credit on every
    /// pass the coin input is set. A pad that is disconnected reads as neutral; on the pass a pad (re)connects its
    /// previous state is taken to be the current one, so held buttons do not produce an edge.
    /// </summary>
    public sealed class PadMapper
    {
        /// <summary>Axial dead zone on the left stick X (stick range −1..1). A design choice, not from the arcade.</summary>
        public const float StickDeadzone = 0.35f;
        /// <summary>Right-trigger travel (0..1) that counts as thrust.</summary>
        public const float TriggerThreshold = 0.3f;

        private readonly GamePadState[] _prev = new GamePadState[2];
        private readonly bool[] _connected = new bool[2];

        /// <summary>Whether pad <paramref name="index"/> was connected on the last <see cref="Update"/>.</summary>
        public bool IsConnected(int index) => _connected[index];

        /// <summary>Level controls of one player from one pad state (no edges). Disconnected = neutral.</summary>
        public static PlayerInput MapPlayer(in GamePadState s)
        {
            if (!s.IsConnected) return default;
            float x = s.ThumbSticks.Left.X;
            return new PlayerInput
            {
                Left = x < -StickDeadzone || s.DPad.Left == ButtonState.Pressed,
                Right = x > StickDeadzone || s.DPad.Right == ButtonState.Pressed,
                Thrust = s.Buttons.A == ButtonState.Pressed || s.Triggers.Right > TriggerThreshold,
                Fire = s.Buttons.X == ButtonState.Pressed || s.Buttons.RightShoulder == ButtonState.Pressed,
            };
        }

        /// <summary>Maps the current states of pads 0 and 1 and advances the edge detector by one pass.</summary>
        public PadFrame Update(GamePadState pad0, GamePadState pad1)
        {
            var cur = new[] { pad0, pad1 };
            var f = new PadFrame { P1 = MapPlayer(pad0), P2 = MapPlayer(pad1) };
            bool[] startEdge = new bool[2];
            for (int i = 0; i < 2; i++)
            {
                var s = cur[i];
                if (!s.IsConnected) { _connected[i] = false; _prev[i] = default; continue; }
                var p = _connected[i] ? _prev[i] : s; // first pass after (re)connecting: no edges
                bool Edge(Buttons b) => s.IsButtonDown(b) && !p.IsButtonDown(b);
                f.Coin |= Edge(Buttons.Back);
                startEdge[i] = Edge(Buttons.Start);
                _prev[i] = s; _connected[i] = true;
            }
            bool lbHeld = pad0.IsConnected && pad0.IsButtonDown(Buttons.LeftShoulder);
            f.Start1 = startEdge[0] && !lbHeld;
            f.Start2 = startEdge[1] || (startEdge[0] && lbHeld);
            return f;
        }

        /// <summary>Keyboard and pad inputs OR'd.</summary>
        public static FrameInput Combine(in FrameInput k, in PadFrame p) => new FrameInput
        {
            P1 = Or(k.P1, p.P1),
            P2 = Or(k.P2, p.P2),
            Coin = k.Coin || p.Coin,
            Start1 = k.Start1 || p.Start1,
            Start2 = k.Start2 || p.Start2,
        };

        private static PlayerInput Or(in PlayerInput a, in PlayerInput b) => new PlayerInput
        {
            Left = a.Left || b.Left, Right = a.Right || b.Right, Thrust = a.Thrust || b.Thrust, Fire = a.Fire || b.Fire,
        };
    }
}
