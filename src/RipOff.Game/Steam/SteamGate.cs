using System;

namespace RipOff.Game.Steam
{
    /// <summary>
    /// Lazy Steam: <see cref="SteamPlatform.Init"/> (SteamAPI_Init) runs only on <see cref="EnsureInit"/>, i.e. when
    /// the player picks a Steam online option, or at launch when Options → STEAM AT LAUNCH is on or Steam passed
    /// +connect_lobby. Reason (NOTES §24): with steam_appid.txt = 480, an initialized Steam client treats the game as
    /// Spacewar and its Steam Input can take the controller away from XInput/SDL. Trade-off: the Steam overlay
    /// (Shift+Tab, invite dialog) hooks the graphics device only when SteamAPI_Init ran before the device was
    /// created (Steamworks overlay docs), so after a lazy init the overlay — and with it INVITE A STEAM FRIEND — may
    /// not appear; lobby codes still work. UNCHECKED on a real Steam client.
    /// </summary>
    public sealed class SteamGate : ISteamUi, IDisposable
    {
        private readonly string[] _args;
        private readonly bool _disabled;
        /// <summary>Called once with the init result (input log, notice).</summary>
        public Action<SteamPlatform, bool>? Initializing;

        public SteamGate(string[] args, bool disabled = false) { _args = args; _disabled = disabled; }

        public SteamPlatform? Platform { get; private set; }
        /// <summary>True when Steam was initialized before the graphics device (overlay can work).</summary>
        public bool InitializedAtLaunch { get; private set; }
        public bool Initialized => Platform != null;

        /// <summary>Called by Program before the window exists.</summary>
        public void InitAtLaunch() { if (!_disabled && Platform == null) { InitializedAtLaunch = true; EnsureInit(); } }

        public bool EnsureInit()
        {
            if (_disabled) return false;
            if (Platform == null)
            {
                Platform = SteamPlatform.Init(_args);
                Initializing?.Invoke(Platform, InitializedAtLaunch);
            }
            return Platform.Available;
        }

        public bool Available => Platform?.Available == true;
        public string Status => _disabled ? "Steam off (--no-steam)" : Platform?.Status ?? "Steam not started (starts when you pick a Steam option)";
        public string? Code => Platform?.Code;
        public ulong? TakePendingJoin() => Platform?.TakePendingJoin();
        public void OpenInviteOverlay() => Platform?.OpenInviteOverlay();
        public void RunCallbacks() => Platform?.RunCallbacks();
        public void Dispose() => Platform?.Dispose();
    }
}
