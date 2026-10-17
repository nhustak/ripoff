namespace RipOff.Game
{
    /// <summary>What the front end needs from Steam (implemented by Steam.SteamPlatform; faked in tests).</summary>
    public interface ISteamUi
    {
        bool Available { get; }
        /// <summary>Why Steam is unavailable, or "Steam OK".</summary>
        string Status { get; }
        /// <summary>The hosted lobby's 6-character code, once created.</summary>
        string? Code { get; }
        /// <summary>A lobby the player chose to join from Steam (invite, friends list, +connect_lobby), once.</summary>
        ulong? TakePendingJoin();
        void OpenInviteOverlay();
    }
}
