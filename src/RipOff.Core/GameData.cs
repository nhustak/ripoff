namespace RipOff.Core
{
    /// <summary>
    /// Fixed tables of the game, re-expressed as our own data from the docs
    /// (GAME_RULES.md, NOTES.md §14.2 and §20). The ROM is never loaded.
    /// </summary>
    public static class GameData
    {
        public const int FieldWidth = 1024;  // x 0..$3FF (GAME_RULES §Units)
        public const int FieldHeight = 768;  // y 0..$2FF
        public const double PassesPerSecond = 38.0; // one pass = one frame at 38 Hz (NOTES §18.8)

        /// <summary>
        /// The 8 spawn points with their headings (NOTES §14.2 table; lst:786-842).
        /// Also the exit points used after a pick-up (NOTES §18.3, lst:2768-2798).
        /// </summary>
        public static readonly (int X, int Y, int Heading)[] SpawnPoints =
        {
            (0, 0, 134),
            (304, 0, 180),
            (896, 0, 224),
            (592, 0, 180),
            (1024, 768, 314),
            (720, 768, 0),
            (128, 768, 44),
            (432, 768, 0),
        };

        /// <summary>
        /// Canister start positions in field units, in table order (NOTES §20.2: the 16
        /// byte pairs at $0F9A, ×8 into RAM $B0 = field position ×2, lst:2919-2939).
        /// A game uses the first N entries.
        /// </summary>
        public static readonly (int X, int Y)[] CanisterStarts =
        {
            (512, 360), (512, 408), (536, 384), (488, 384),
            (544, 352), (480, 352), (544, 416), (480, 416),
            (568, 384), (512, 328), (456, 384), (512, 440),
            (456, 328), (456, 440), (568, 328), (568, 440),
        };

        public const int CanisterSlots = 16; // RAM $B0-$CF, 2 words each (lst:1543-1573)

        /// <summary>
        /// Canister DIP ("Lives" DIP, GAME_RULES §1): index 0..3 → 12/16/4/8 canisters,
        /// background step $15/$0F/$3F/$1F (NOTES §15, §17). Default DIP 0x03 → 8.
        /// </summary>
        public static readonly int[] CanistersForDip = { 12, 16, 4, 8 };
        public static readonly int[] BackgroundStepForDip = { 0x15, 0x0F, 0x3F, 0x1F };
        public const int DefaultCanisterDip = 3;

        /// <summary>
        /// Player wall bounce (NOTES §20.3): when the move takes a player off the field the
        /// coordinate is clamped (x to 0/$3FF, y to 0/$2FF) and the heading is set from a
        /// 4-entry table indexed by the quarter count q of (heading−90) from $1204
        /// (lst:2288-2334; bytes at $0FBA/$0FBE ×2, lst:3689-3690).
        /// </summary>
        public static readonly int[] WallHeadingX = { 186, 174, 6, 354 };
        public static readonly int[] WallHeadingY = { 84, 276, 264, 96 };

        public const int PlayerScale = 100;   // lst:2105 ($08EC-$08EE)
        public const int EnemyScale = 0x50;   // lst:1526-1527
        public const int ExplosionShot = 0x200;   // lst:3526-3549 (word 4 := 2·$100)
        public const int ExplosionRam = 0x300;    // lst:2393-2421, lst:4065-4073
        public const int ExplosionStep = 0x20;    // lst:3093-3106

        /// <summary>P1/P2 (re)spawn: lst:899-932, NOTES §19.2.</summary>
        public static readonly (int X, int Y, int Heading)[] PlayerStarts = { (1024, 320, 270), (0, 448, 90) };
        public const int RespawnDelay = 0x19;  // 25 passes ($03B2-$03B6, lst:882-886, NOTES §19.2)
        public const int RespawnSpeed = 5;     // lst:891-893

        public const int ShotSlotsPerPlayer = 4; // lst:2584-2602
        /// <summary>Auto-fire repeat interval in passes (remake option, not arcade): 6 passes = 158 ms, about
        /// 6.3 shots/s while fire is held; the 4-slot cap still applies (a held button waits for a free slot).</summary>
        public const int AutoFireInterval = 6;
        public const int ShotSpeedBonus = 15;    // lst:2647-2648 ($0B36-$0B37: speed + $F)
        public const int TorpedoPasses = 5;      // lst:2606-2609 (RAM[$FD] := 5)

        public const int ShotHitRadius = 20;      // lst:3481-3484 ($14)
        public const int TankHitRadius = 24;      // lst:2350-2367 ($18)
        public const int ChaseRadius = 256;       // lst:2519-2531 ($100), inclusive box
        public const int SeparationRadius = 0x60; // lst:1611-1675
        public const int HopArrivalRadius = 0x48; // lst:1924-1945
        public const int PickupRadius = 20;       // lst:2666-2680 ($14)
        public const int FireReach = 0x96;        // lst:4005-4013 ($1145-$1152): kill needs |d| < 150 (strict, NOTES §20.1)
        public const int TowOffset = 0x20;        // lst:3182-3228: canister drawn at (0,$20) rotated, ×scale/128

        /// <summary>
        /// Bit 0 of record word 9 (the tread-frame pointer). It is the same for every frame of a sprite set
        /// (disasm/shapes/objects.txt anim9 start addresses): P1 odd, P2 even, enemy types 3 and 5 odd, others
        /// even. Only used by the separation walk's misaligned reads (NOTES §20.1). Records never built read 0.
        /// </summary>
        public static int Word9Parity(TankRecord r)
        {
            if (r.Base == 0x30) return 1;
            if (r.Base == 0x40) return 0;
            return r.Type == 3 || r.Type == 5 ? 1 : 0;
        }

        public const int WaveGapEnd = 0x65;       // lst:464-498 (RAM[$EC] += 2 until ≥ $65)
    }
}
