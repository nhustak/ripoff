using System;
using Microsoft.Xna.Framework.Input;
using Steamworks;

namespace RipOff.Game.Steam
{
    /// <summary>
    /// Reads controllers through Steamworks ISteamInput once Steam is initialized. Why (NOTES §25): with
    /// steam_appid.txt = 480 an initialized Steam client treats the game as Spacewar, "which actually has Steam Input
    /// mappings and controller input is redirected to Steam Input instead of XInput" (Valve, Steamworks.NET #380).
    /// Reading Steam Input is the Valve-supported way to get that input back (Spacewar does it: SteamworksExample/
    /// Main.cpp:254 SteamInput()->Init(false), gameenginesdl.cpp:1234-1268), so the pad works with Steam running and
    /// Steam Input on. Init(false): Steam Input is updated by SteamAPI_RunCallbacks (Steamworks.NET isteaminput.cs:19-24;
    /// ISteamInput docs, RunFrame: "performed automatically by SteamAPI_RunCallbacks").
    /// Uses Spacewar's action sets as configured by Valve for App 480 (no manifest of our own; see NOTES §25 for the
    /// manifest alternative). UNCHECKED: everything here against a real Steam client on Windows.
    /// </summary>
    public sealed class SteamInputPads
    {
        public const int MaxControllers = InputReader.MaxPads;
        private readonly InputHandle_t[] _handles = new InputHandle_t[Constants.STEAM_INPUT_MAX_COUNT];
        private InputActionSetHandle_t _shipSet, _menuSet;
        private InputDigitalActionHandle_t _turnL, _turnR, _thrust, _fire, _pause, _mUp, _mDown, _mLeft, _mRight, _mSelect, _mCancel;
        private InputAnalogActionHandle_t _analog;
        private int _resolveCountdown;

        public bool Initialized { get; private set; }
        public string Status { get; private set; } = "Steam Input not started";
        /// <summary>Controllers Steam reports this tick, and how many of them have bound actions.</summary>
        public int Connected { get; private set; }
        public int Bound { get; private set; }
        public bool HandlesResolved { get; private set; }
        public readonly string?[] Types = new string?[MaxControllers];

        public static SteamInputPads Start()
        {
            var p = new SteamInputPads();
            try
            {
                p.Initialized = SteamInput.Init(false);
                p.Status = p.Initialized ? "Steam Input on" : "SteamInput.Init failed";
                if (p.Initialized) p.Resolve();
            }
            catch (Exception e) { p.Status = "Steam Input error: " + e.GetType().Name; }
            return p;
        }

        /// <summary>Action handles can read 0 until Steam has loaded the configuration; retried about once a second.</summary>
        private void Resolve()
        {
            _shipSet = SteamInput.GetActionSetHandle("ship_controls");
            _menuSet = SteamInput.GetActionSetHandle("menu_controls");
            _turnL = SteamInput.GetDigitalActionHandle("turn_left");
            _turnR = SteamInput.GetDigitalActionHandle("turn_right");
            _thrust = SteamInput.GetDigitalActionHandle("forward_thrust");
            _fire = SteamInput.GetDigitalActionHandle("fire_lasers");
            _pause = SteamInput.GetDigitalActionHandle("pause_menu");
            _mUp = SteamInput.GetDigitalActionHandle("menu_up");
            _mDown = SteamInput.GetDigitalActionHandle("menu_down");
            _mLeft = SteamInput.GetDigitalActionHandle("menu_left");
            _mRight = SteamInput.GetDigitalActionHandle("menu_right");
            _mSelect = SteamInput.GetDigitalActionHandle("menu_select");
            _mCancel = SteamInput.GetDigitalActionHandle("menu_cancel");
            _analog = SteamInput.GetAnalogActionHandle("analog_controls");
            HandlesResolved = _shipSet.m_InputActionSetHandle != 0 && _menuSet.m_InputActionSetHandle != 0;
        }

        /// <summary>Fills <paramref name="states"/> (up to <see cref="MaxControllers"/>) for this tick. Call after
        /// SteamAPI.RunCallbacks.</summary>
        public void Read(bool inGame, Span<GamePadState> states)
        {
            states.Clear();
            Connected = Bound = 0;
            if (!Initialized) return;
            try
            {
                if (!HandlesResolved && --_resolveCountdown <= 0) { Resolve(); _resolveCountdown = 38; }
                int n = SteamInput.GetConnectedControllers(_handles);
                Connected = n;
                for (int i = 0; i < n && i < states.Length && i < MaxControllers; i++)
                {
                    var h = _handles[i];
                    SteamInput.ActivateActionSet(h, inGame ? _shipSet : _menuSet);
                    var a = new SteamActions();
                    bool D(InputDigitalActionHandle_t d)
                    {
                        if (d.m_InputDigitalActionHandle == 0) return false;
                        var data = SteamInput.GetDigitalActionData(h, d);
                        if (data.bActive != 0) a.AnyBound = true;
                        return data.bActive != 0 && data.bState != 0;
                    }
                    if (inGame)
                    {
                        a.TurnLeft = D(_turnL); a.TurnRight = D(_turnR); a.Thrust = D(_thrust); a.Fire = D(_fire); a.Pause = D(_pause);
                        if (_analog.m_InputAnalogActionHandle != 0)
                        {
                            var an = SteamInput.GetAnalogActionData(h, _analog);
                            if (an.bActive != 0) { a.AnyBound = true; a.AnalogX = an.x; a.AnalogY = an.y; }
                        }
                    }
                    else
                    {
                        a.MenuUp = D(_mUp); a.MenuDown = D(_mDown); a.MenuLeft = D(_mLeft); a.MenuRight = D(_mRight);
                        a.MenuSelect = D(_mSelect); a.MenuCancel = D(_mCancel);
                    }
                    if (a.AnyBound) Bound++;
                    states[i] = a.AnyBound ? SteamPadMapping.ToState(a, inGame) : default;
                    Types[i] = SteamInput.GetInputTypeForHandle(h).ToString().Replace("k_ESteamInputType_", "");
                }
                for (int i = n; i < Types.Length; i++) Types[i] = null;
            }
            catch (Exception e) { Status = "Steam Input error: " + e.GetType().Name; Initialized = false; }
        }

        public void Shutdown() { if (Initialized) { try { SteamInput.Shutdown(); } catch (Exception) { } Initialized = false; } }
    }
}
