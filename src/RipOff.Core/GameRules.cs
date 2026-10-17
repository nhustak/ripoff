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

        // ---- enemy fire (Simulation.FireTest / AimHits). Arcade: the kill test always hits (NOTES §18.4). ----
        /// <summary>Aim spread in degrees at level 1 (0 = exact, the arcade). Each enemy shot leaves at a random angle
        /// within ±spread of the bearing and hits only when it passes within <see cref="EnemyHitRadius"/> of the player.</summary>
        public int EnemyAimErrorDeg { get; init; }
        /// <summary>Spread lost per level above 1, down to <see cref="EnemyAimErrorMin"/>.</summary>
        public int EnemyAimErrorPerLevel { get; init; }
        public int EnemyAimErrorMin { get; init; }
        /// <summary>Miss distance (field units) that still hits; 24 = the tank contact box (GameData.TankHitRadius).</summary>
        public int EnemyHitRadius { get; init; }
        /// <summary>Passes an enemy cannot fire after a miss (0 = arcade: it may retry every pass of its window).</summary>
        public int EnemyMissCooldown { get; init; }
        /// <summary>Passes after an enemy starts chasing before it may fire (0 = arcade).</summary>
        public int ChaseReactionPasses { get; init; }
        /// <summary>Turn step of a chasing enemy, percent of ⌊v/2⌋ + 1 (min 1). 100 = arcade.</summary>
        public int ChaseTurnPercent { get; init; } = 100;

        // ---- escalation (Simulation.WaveEnd / SpawnWave, GAME_RULES §4-5, NOTES §26) ----
        /// <summary>Cleared waves needed for one enemy-type step (arcade 1).</summary>
        public int ClearsPerTypeStep { get; init; } = 1;
        /// <summary>First level with 3 enemies per wave in a one-player game (arcade 2; below it the third record is
        /// the one-player handicap).</summary>
        public int FullWaveFromLevel { get; init; } = 2;
        /// <summary>Enemy max speed cap = this + level (0 = no cap, the arcade: v = type + ⌊type/2⌋ + level + 1).</summary>
        public int EnemySpeedCapBase { get; init; }

        // ---- canisters ----
        /// <summary>Towing speed, percent of the normal move (min 2). 100 = arcade.</summary>
        public int TowSpeedPercent { get; init; } = 100;

        /// <summary>Aim spread at <paramref name="level"/>.</summary>
        public int AimErrorAt(int level) => EnemyAimErrorDeg <= 0 ? 0 : Math.Max(EnemyAimErrorMin, EnemyAimErrorDeg - EnemyAimErrorPerLevel * Math.Max(0, level - 1));

        /// <summary>The original game. Do not edit: Arcade must stay byte-identical (ArcadeDeterminismTests).</summary>
        public static readonly GameRules Arcade = new()
        {
            Mode = PlayMode.Arcade,
            AutoFireInterval = GameData.AutoFireInterval,
            RespawnDelay = GameData.RespawnDelay,
            RespawnSpeed = GameData.RespawnSpeed,
        };

        /// <summary>
        /// ENHANCED MODE TWEAKS GO HERE (change values with <c>Arcade with { ... }</c>). Tuned 2026-10-17 so games last
        /// about 2-2.5x longer and enemy fire misses about half the time (NOTES §27, measured with tools/remake/Waves stats).
        /// </summary>
        public static readonly GameRules Enhanced = Arcade with
        {
            Mode = PlayMode.Enhanced,
            // Enemy fire: ±20° spread at level 1, 2° tighter per level down to ±10°; a shot must pass within 12 units.
            EnemyAimErrorDeg = 20, EnemyAimErrorPerLevel = 2, EnemyAimErrorMin = 10, EnemyHitRadius = 12,
            EnemyMissCooldown = 32,     // after a miss: two fire windows (each enemy gets one 4-pass window per 16 passes)
            ChaseReactionPasses = 19,   // 0.5 s from starting a chase to the first shot
            ChaseTurnPercent = 100,     // kept at arcade: 75 made chasers orbit a still player forever (NOTES §27)
            // Escalation: two cleared waves per type step; 3-enemy waves from level 3; speed cap 8 + level.
            ClearsPerTypeStep = 2,
            FullWaveFromLevel = 3,
            EnemySpeedCapBase = 8,      // type 6: 9 at level 1 (arcade 11), 10 at level 2 (arcade 12)
            // Canisters: towing enemies move at 75 % (min 2).
            TowSpeedPercent = 75,
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
