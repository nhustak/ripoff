namespace RipOff.Game
{
    /// <summary>What the front end needs from Steam (implemented by Steam.SteamGate / SteamPlatform; faked in tests).</summary>
    public interface ISteamUi
    {
        /// <summary>SteamAPI_Init has been attempted. Steam starts lazily (only when a Steam option is picked) unless
        /// Options → STEAM AT LAUNCH is on: with App ID 480, Steam Input can take the controller away from the game
        /// once Steam is initialized (README, NOTES §24), so local play never touches Steam by default.</summary>
        bool Initialized { get; }
        /// <summary>Initializes Steam if that has not been tried yet; true when Steam is usable.</summary>
        bool EnsureInit();
        bool Available { get; }
        /// <summary>Why Steam is unavailable, "Steam OK", or that it has not been started.</summary>
        string Status { get; }
        /// <summary>The hosted lobby's 6-character code, once created.</summary>
        string? Code { get; }
        /// <summary>A lobby the player chose to join from Steam (invite, friends list, +connect_lobby), once.</summary>
        ulong? TakePendingJoin();
        void OpenInviteOverlay();
    }
}
