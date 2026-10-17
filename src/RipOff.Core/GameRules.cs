using System;

namespace RipOff.Core
{
    /// <summary>
    /// Which rule set a game runs. Part of <see cref="GameConfig"/> (so online both peers run the host's mode) and of
    /// <see cref="GameState"/>. Not to be confused with <see cref="GameMode"/> (attract / playing).
    /// </summary>
    public enum PlayMode : byte
    {
        /// <summary>The faithful 1980 Cinematronics rules (tag arcade-v1.0). Must never change behaviour:
        /// ArcadeDeterminismTests pins its checksums.</summary>
        Arcade = 0,
        /// <summary>The expanded mode. Starts as an exact copy of Arcade; future tweaks go in <see cref="GameRules.Enhanced"/>.</summary>
        Enhanced = 1,
    }

    /// <summary>
    /// The tunable rules the <see cref="Simulation"/> reads instead of hard-coded constants, one instance per
    /// <see cref="PlayMode"/>. Arcade's values are the arcade constants in <see cref="GameData"/> and are frozen;
    /// modes diverge by changing <see cref="Enhanced"/> (and adding knobs here, with Arcade set to today's value).
    /// </summary>
    public sealed record GameRules
    {
        public PlayMode Mode { get; init; }
        /// <summary>Auto-fire repeat interval in passes (remake option, GameConfig.AutoFire).</summary>
        public int AutoFireInterval { get; init; }
        /// <summary>Passes before a destroyed player respawns (and before the first spawn of a game).</summary>
        public int RespawnDelay { get; init; }
        /// <summary>Player speed on (re)spawn.</summary>
        public int RespawnSpeed { get; init; }

        /// <summary>The original game. Do not edit: Arcade must stay byte-identical (ArcadeDeterminismTests).</summary>
        public static readonly GameRules Arcade = new()
        {
            Mode = PlayMode.Arcade,
            AutoFireInterval = GameData.AutoFireInterval,
            RespawnDelay = GameData.RespawnDelay,
            RespawnSpeed = GameData.RespawnSpeed,
        };

        /// <summary>
        /// ENHANCED MODE TWEAKS GO HERE. For now an identical copy of <see cref="Arcade"/> (only the mode differs);
        /// change values with <c>Arcade with { ... }</c> as the expanded mode is designed.
        /// </summary>
        public static readonly GameRules Enhanced = Arcade with
        {
            Mode = PlayMode.Enhanced,
        };

        public static bool IsDefined(PlayMode m) => m is PlayMode.Arcade or PlayMode.Enhanced;

        public static GameRules For(PlayMode m) => m switch
        {
            PlayMode.Arcade => Arcade,
            PlayMode.Enhanced => Enhanced,
            _ => throw new ArgumentOutOfRangeException(nameof(m), m, "unknown play mode"),
        };
    }
}
