using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace RipOff.Game
{
    /// <summary>
    /// One Steam Input controller's action states for one tick (read by Steam.SteamInputPads through ISteamInput).
    /// Action names are App 480 Spacewar's, from Valve's SDK sample (steamworksexample/gameenginesdl.cpp:1234-1253,
    /// SteamworksExample/steam_input_manifest.vdf): set "ship_controls" = turn_left, turn_right, forward_thrust,
    /// backward_thrust, fire_lasers, pause_menu + analog "analog_controls" (joystick_move; x steers, Ship.cpp:421-432);
    /// set "menu_controls" = menu_up/down/left/right/select/cancel.
    /// </summary>
    public struct SteamActions
    {
        public bool TurnLeft, TurnRight, Thrust, Fire, Pause;
        public float AnalogX, AnalogY;
        public bool MenuUp, MenuDown, MenuLeft, MenuRight, MenuSelect, MenuCancel;
        /// <summary>At least one action is bound in the active action set (Steam's bActive): Steam Input is actually
        /// delivering this controller to us.</summary>
        public bool AnyBound;
    }

    /// <summary>
    /// Turns Steam Input actions into a <see cref="GamePadState"/> in the same Xbox layout the rest of the game
    /// reads (InputReader / PadMapper), so menus and play need no Steam-specific code:
    ///   ship set: turn_left/right → D-pad left/right, analog_controls → left stick, forward_thrust → A (thrust),
    ///             fire_lasers → X (fire), pause_menu → Start
    ///   menu set: menu_up/down/left/right → D-pad, menu_select → A, menu_cancel → B
    /// And decides which source the game uses (<see cref="Merge"/>).
    /// </summary>
    public static class SteamPadMapping
    {
        public static GamePadState ToState(in SteamActions a, bool inGame)
        {
            Buttons b = 0;
            bool up = false, down = false, left, right;
            Vector2 stick = Vector2.Zero;
            if (inGame)
            {
                left = a.TurnLeft; right = a.TurnRight;
                if (a.Thrust) b |= Buttons.A;
                if (a.Fire) b |= Buttons.X;
                if (a.Pause) b |= Buttons.Start;
                stick = new Vector2(Clamp(a.AnalogX), Clamp(a.AnalogY));
            }
            else
            {
                up = a.MenuUp; down = a.MenuDown; left = a.MenuLeft; right = a.MenuRight;
                if (a.MenuSelect) b |= Buttons.A;
                if (a.MenuCancel) b |= Buttons.B;
            }
            static ButtonState S(bool v) => v ? ButtonState.Pressed : ButtonState.Released;
            return new GamePadState(new GamePadThumbSticks(stick, Vector2.Zero), new GamePadTriggers(0, 0),
                new GamePadButtons(b), new GamePadDPad(S(up), S(down), S(left), S(right)));
        }

        private static float Clamp(float v) => float.IsFinite(v) ? Math.Clamp(v, -1f, 1f) : 0f;

        /// <summary>
        /// Picks the pad source for this tick. When Steam Input delivers at least one controller (an action is bound,
        /// <see cref="SteamActions.AnyBound"/>), its controllers fill the slots and the SDL/XInput pads are ignored:
        /// with Steam Input on, the same physical pad can show up in both, and using both would double every menu
        /// press. Otherwise the SDL pads (MonoGame GamePad) are used unchanged. Returns true when Steam Input won.
        /// </summary>
        public static bool Merge(ReadOnlySpan<GamePadState> sdl, ReadOnlySpan<GamePadState> steam, int steamBound, Span<GamePadState> result)
        {
            bool useSteam = steamBound > 0;
            for (int i = 0; i < result.Length; i++)
                result[i] = useSteam ? (i < steam.Length ? steam[i] : default) : (i < sdl.Length ? sdl[i] : default);
            return useSteam;
        }
    }
}
