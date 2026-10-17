using System;
using Microsoft.Xna.Framework.Input;
using RipOff.Core;

namespace RipOff.Game
{
    /// <summary>Gameplay controls taken from the pads for one pass; OR'd with the keyboard by the front end.</summary>
    public struct PadFrame
    {
        public PlayerInput P1, P2;
    }

    /// <summary>
    /// Game pad → tank controls (MonoGame GamePad = SDL2 GameController on DesktopGL; Xbox button names).
    /// The first connected pad (lowest MonoGame index) drives player 1 and the second connected pad player 2, so a
    /// pad that Windows/SDL puts at index 1 or 2 still works (M4 fix: before, only indices 0 and 1 were read).
    ///   turn:   left stick X beyond <see cref="StickDeadzone"/> (Options), or D-pad left/right
    ///   thrust: A, or left trigger beyond <see cref="TriggerThreshold"/>
    ///   fire:   X, right bumper, or right trigger beyond <see cref="TriggerThreshold"/>: one "fire held" level; the
    ///           simulation decides what holding does (one shot per press, or auto-fire when that option is on)
    /// There are no coin or start buttons (no credits, M4); Start opens the pause menu (InputReader / FrontEnd).
    /// A disconnected pad reads as neutral.
    /// </summary>
    public sealed class PadMapper
    {
        /// <summary>Default axial dead zone on the left stick X (stick range −1..1). A design choice, not from the arcade.</summary>
        public const float DefaultStickDeadzone = 0.35f;
        /// <summary>Trigger travel (0..1) beyond which LT counts as thrust and RT as fire.</summary>
        public const float TriggerThreshold = 0.3f;

        /// <summary>Dead zone in use (Options → STICK DEAD ZONE).</summary>
        public float StickDeadzone
        {
            get => _deadzone;
            // Guard (pad regression, NOTES §24): a NaN or extreme value must never switch the stick off or make it
            // always-on, whatever a settings file says.
            set => _deadzone = float.IsFinite(value) ? Math.Clamp(value, MinDeadzone, MaxDeadzone) : DefaultStickDeadzone;
        }
        private float _deadzone = DefaultStickDeadzone;
        public const float MinDeadzone = 0.05f, MaxDeadzone = 0.9f;

        /// <summary>MonoGame indices of the pads driving P1 and P2 on the last <see cref="Update"/> (-1 = none).</summary>
        public int P1Index { get; private set; } = -1;
        public int P2Index { get; private set; } = -1;
        public int ConnectedCount { get; private set; }

        /// <summary>Level controls of one player from one pad state (no edges). Disconnected = neutral.</summary>
        public static PlayerInput MapPlayer(in GamePadState s, float deadzone = DefaultStickDeadzone)
        {
            if (!s.IsConnected) return default;
            float x = s.ThumbSticks.Left.X;
            return new PlayerInput
            {
                Left = x < -deadzone || s.DPad.Left == ButtonState.Pressed,
                Right = x > deadzone || s.DPad.Right == ButtonState.Pressed,
                Thrust = s.Buttons.A == ButtonState.Pressed || s.Triggers.Left > TriggerThreshold,
                Fire = s.Buttons.X == ButtonState.Pressed || s.Buttons.RightShoulder == ButtonState.Pressed
                    || s.Triggers.Right > TriggerThreshold,
            };
        }

        /// <summary>Maps the pads (index order): first connected = P1, second connected = P2.</summary>
        public PadFrame Update(params GamePadState[] pads) => Update((ReadOnlySpan<GamePadState>)pads);

        public PadFrame Update(ReadOnlySpan<GamePadState> pads)
        {
            var f = new PadFrame();
            P1Index = P2Index = -1; ConnectedCount = 0;
            for (int i = 0; i < pads.Length; i++)
            {
                if (!pads[i].IsConnected) continue;
                ConnectedCount++;
                if (P1Index < 0) { P1Index = i; f.P1 = MapPlayer(pads[i], StickDeadzone); }
                else if (P2Index < 0) { P2Index = i; f.P2 = MapPlayer(pads[i], StickDeadzone); }
            }
            return f;
        }

        public static PlayerInput Or(in PlayerInput a, in PlayerInput b) => new PlayerInput
        {
            Left = a.Left || b.Left, Right = a.Right || b.Right, Thrust = a.Thrust || b.Thrust, Fire = a.Fire || b.Fire,
        };
    }
}
