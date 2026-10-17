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

        // ---- player (re)spawn (Simulation.RespawnPlayers, NOTES §28) ----
        /// <summary>Passes of the warp-in effect after the respawn delay (0 = arcade: the tank drives in from the
        /// field edge at <see cref="RespawnSpeed"/>). While warping in, the player record stays free: it can't be hit,
        /// doesn't collide and can't fire, and it materializes at the end at the <see cref="WarpLocation"/> point.</summary>
        public int WarpInPasses { get; init; }
        /// <summary>Speed when a warp-in materializes (the arcade's drive-in uses <see cref="RespawnSpeed"/>).</summary>
        public int WarpInSpeed { get; init; }
        /// <summary>Where a warp-in happens: <see cref="WarpInLocation.Fixed"/> (GameData.WarpInPoints) or
        /// <see cref="WarpInLocation.Random"/> (Simulation.RandomWarpPoint, NOTES §29). Only used when WarpInPasses &gt; 0.</summary>
        public WarpInLocation WarpLocation { get; init; } = WarpInLocation.Fixed;
        /// <summary>Warp kill (NOTES §29): when a warp-in materializes, enemies overlapping the player (tank contact
        /// box, TankHitRadius) are destroyed as if rammed, scoring for that player; the player survives. Only used when
        /// WarpInPasses &gt; 0.</summary>
        public bool WarpKills { get; init; }
        /// <summary>How long a dead player waits before (re)spawning (hook: only <see cref="RespawnDelayRule.Fixed"/>
        /// exists today, = <see cref="RespawnDelay"/>).</summary>
        public RespawnDelayRule DelayRule { get; init; } = RespawnDelayRule.Fixed;

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
            // Respawn: warp in at the spawn point instead of driving in from the edge (NOTES §28). 30 passes = 0.79 s.
            WarpInPasses = 30, WarpInSpeed = 0,
            WarpLocation = WarpInLocation.Random, WarpKills = true,
        };

        /// <summary>
        /// The Enhanced variations a player can switch off (ENHANCED OPTIONS page, GameConfig.Options). Each row
        /// names the knobs it owns; switching it off sets those knobs back to their <see cref="Arcade"/> values.
        /// A future variation = one new <see cref="EnhancedOption"/> bit + one row here.
        /// </summary>
        public static readonly EnhancedVariation[] Variations =
        {
            new(EnhancedOption.WarpIn, "WARP-IN", "PLAYERS WARP IN INSTEAD OF DRIVING IN FROM THE EDGE",
                r => r with { WarpInPasses = Arcade.WarpInPasses, WarpInSpeed = Arcade.WarpInSpeed }),
            new(EnhancedOption.EnemyAimError, "ENEMY AIM ERROR", "ENEMY SHOTS CAN MISS - ENEMIES RELOAD AFTER A MISS AND REACT LATER",
                r => r with
                {
                    EnemyAimErrorDeg = Arcade.EnemyAimErrorDeg, EnemyAimErrorPerLevel = Arcade.EnemyAimErrorPerLevel,
                    EnemyAimErrorMin = Arcade.EnemyAimErrorMin, EnemyHitRadius = Arcade.EnemyHitRadius,
                    EnemyMissCooldown = Arcade.EnemyMissCooldown, ChaseReactionPasses = Arcade.ChaseReactionPasses,
                }),
            new(EnhancedOption.SlowerRamp, "SLOWER RAMP", "TWO CLEARS PER ENEMY TYPE, 3-ENEMY WAVES FROM LEVEL 3, SPEED CAP",
                r => r with
                {
                    ClearsPerTypeStep = Arcade.ClearsPerTypeStep, FullWaveFromLevel = Arcade.FullWaveFromLevel,
                    EnemySpeedCapBase = Arcade.EnemySpeedCapBase,
                }),
            new(EnhancedOption.SlowerTowing, "SLOWER TOWING", "ENEMIES TOWING A CANISTER MOVE AT 75 %",
                r => r with { TowSpeedPercent = Arcade.TowSpeedPercent }),
            new(EnhancedOption.RandomWarpIn, "RANDOM WARP-IN", "WARP IN AT A RANDOM SPOT AWAY FROM THE CANISTERS AND THE OTHER PLAYER",
                r => r with { WarpLocation = Arcade.WarpLocation }, Requires: EnhancedOption.WarpIn),
            new(EnhancedOption.WarpKills, "WARP KILLS", "WARPING IN ON TOP OF AN ENEMY DESTROYS IT",
                r => r with { WarpKills = Arcade.WarpKills }, Requires: EnhancedOption.WarpIn),
        };

        private static readonly GameRules?[] EnhancedByOptions = new GameRules?[(int)EnhancedOption.All + 1];

        /// <summary>Enhanced with the variations not in <paramref name="options"/> switched off (cached; options outside
        /// <see cref="EnhancedOption.All"/> are ignored).</summary>
        public static GameRules EnhancedWith(EnhancedOption options)
        {
            int k = (int)(options & EnhancedOption.All);
            var cached = EnhancedByOptions[k];
            if (cached != null) return cached;
            var r = Enhanced;
            foreach (var v in Variations) if ((options & v.Option) == 0) r = v.Off(r);
            return EnhancedByOptions[k] = r;
        }

        public static bool IsDefined(PlayMode m) => m is PlayMode.Arcade or PlayMode.Enhanced;

        public static GameRules For(PlayMode m) => For(m, EnhancedOption.All);

        /// <summary>Rules for a mode and its Enhanced options. Arcade ignores the options.</summary>
        public static GameRules For(PlayMode m, EnhancedOption options) => m switch
        {
            PlayMode.Arcade => Arcade,
            PlayMode.Enhanced => options == EnhancedOption.All ? Enhanced : EnhancedWith(options),
            _ => throw new ArgumentOutOfRangeException(nameof(m), m, "unknown play mode"),
        };
    }

    /// <summary>Enhanced variations that can be switched off (bit set = on). Saved in settings.json and sent in the
    /// online game config (one byte), so new bits must be appended and <see cref="All"/> widened.</summary>
    [Flags]
    public enum EnhancedOption : byte
    {
        None = 0,
        WarpIn = 1,
        EnemyAimError = 2,
        SlowerRamp = 4,
        SlowerTowing = 8,
        RandomWarpIn = 16,
        WarpKills = 32,
        All = WarpIn | EnemyAimError | SlowerRamp | SlowerTowing | RandomWarpIn | WarpKills,
    }

    /// <summary>One row of the ENHANCED OPTIONS page: its bit, label, one-line description, and what "off" does to the rules.</summary>
    /// <param name="Requires">Another option this one only means something with (the page dims it while that one is off).</param>
    public sealed record EnhancedVariation(EnhancedOption Option, string Label, string Info, Func<GameRules, GameRules> Off,
        EnhancedOption Requires = EnhancedOption.None)
    {
        /// <summary>False when <see cref="Requires"/> is off in <paramref name="options"/>: the setting is kept but ignored.</summary>
        public bool Applies(EnhancedOption options) => (options & Requires) == Requires;
    }

    /// <summary>Warp-in location strategy (Simulation.WarpInPoint). Random draws from the Enhanced RNG.</summary>
    public enum WarpInLocation : byte { Fixed = 0, Random = 1 }

    /// <summary>Respawn delay strategy (Simulation.RespawnDelayFor). Later: e.g. by cause of death (the player record's
    /// word C: 1 = ram, 2 = enemy fire) and time since the previous death.</summary>
    public enum RespawnDelayRule : byte { Fixed = 0 }
}
