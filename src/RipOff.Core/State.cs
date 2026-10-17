using System.Collections.Generic;

namespace RipOff.Core
{
    /// <summary>Word-A state values and bits (NOTES §18.2, GAME_RULES §6).</summary>
    public static class EnemyState
    {
        public const int Free = 0x001;
        public const int Approach = 0x01C;   // bits 4,3,2
        public const int AtCanister = 0x014; // bits 4,2
        public const int Tow = 0x01E;        // bits 4,3,2,1
        public const int Chase = 0x018;      // bits 4,3
        public const int PlayerAlive = 0x01C; // players use $01C while alive (lst:890)

        public const int BitFree = 0x01, BitTow = 0x02, BitAim = 0x04, BitMoving = 0x08, BitAlive = 0x10;

        public static bool IsFree(int a) => (a & BitFree) != 0;
        /// <summary>Alive = bit 0 clear and bit 4 set; exploding = bit 0 clear and bit 4 clear (lst:3023-3035).</summary>
        public static bool IsAlive(int a) => (a & BitFree) == 0 && (a & BitAlive) != 0;
        public static bool IsExploding(int a) => (a & BitFree) == 0 && (a & BitAlive) == 0;
    }

    /// <summary>
    /// One object record (RAM $00/$10/$20 enemies, $30/$40 players), 16 words.
    /// Field names follow the word numbers used in NOTES.
    /// </summary>
    public sealed class TankRecord
    {
        public int Base;        // record base: $00,$10,$20,$30,$40 (used by the fire window)
        public int X, Y;        // words 0,1
        public int Word2;       // enemy: target canister index; player: last fire-button level
        public int Word3;       // enemy: max speed v; player: respawn counter
        public int Scale = 0;   // word 4: draw scale, or explosion scale while exploding
        public int Heading;     // word 5, degrees
        public int Speed;       // word 6
        public int Type;        // sprite set (enemy type 1..6; 0 = P1, 7 = P2) — words 7-9 in the arcade
        public int A = EnemyState.Free; // word A
        public int B;           // word B: RNG state
        public int C, D;        // words C/D: target point; at a kill C := 0 shot / 1 ram / 2 enemy fire (lst:3526-3549, 2393-2421, 4065-4073)
        public int TurnStep;    // word E
        public int F;           // word F: hop counter

        public TankRecord Clone() => (TankRecord)MemberwiseClone();
    }

    /// <summary>
    /// Canister slot, stored like RAM $B0: X2/Y2 = 2·position, with x bit 0 = taken
    /// (being turned/towed) and y bit 0 = gone (stolen or unused slot) (NOTES §10.3, §18.3, §20).
    /// </summary>
    public struct Canister
    {
        public int X2, Y2;
        public bool Taken => (X2 & 1) != 0;
        public bool Gone => (Y2 & 1) != 0;
        public int X => X2 >> 1;
        public int Y => Y2 >> 1;
    }

    /// <summary>Shot slot (RAM $50-$6F, 4 words): x stored ×2 with bit 0 = free (lst:3432-3466, 2606-2656).</summary>
    public struct Shot
    {
        public bool Live;
        public int X, Y, Vx, Vy;
    }

    public enum GameMode { Attract, Playing }

    /// <summary>Sound cues of one pass (GAME_RULES §9). Polarity is resolved to "playing" flags.</summary>
    public struct SoundState
    {
        public bool Torpedo;      // RAM[$FD] > 0 (Q3 = 0 = on)
        public int Explosions;    // number of Q7 falling edges this pass
        public bool Laser;        // enemy fire line drawn this pass (Q4 = 0 = on)
        public bool Motor;        // command bit 2 = 0 (during waves)
        public bool Beep;         // command bit 3 = 0 (BONUS screen)
        public bool BackgroundMuted; // command bit 4 = 1
        public int BackgroundLevel;  // RAM[$78]; bits 7-5 = drone pitch/tempo (NOTES §21.3)
    }

    /// <summary>Per-pass inputs. Booleans are "pressed"; the arcade reads them active-low.</summary>
    public struct PlayerInput
    {
        public bool Left, Right, Thrust, Fire;
    }

    /// <summary>Both players' controls for one pass. There is no coin or start input: the remake has no credit
    /// system; games are started by the front end with <see cref="Simulation.StartGame"/> (NOTES §23).</summary>
    public struct FrameInput
    {
        public PlayerInput P1, P2;
    }

    /// <summary>Fire-line draw request (cosmetic, GAME_RULES §6 "Fire line").</summary>
    public struct FireLine
    {
        public int Record;   // enemy index 0..2
        public int Frame;    // 0..3 → shapes $1F89/$1F92/$1F9B/$1FA4
    }

    public sealed class GameState
    {
        public GameMode Mode = GameMode.Attract;
        public bool OnePlayer = true;           // RAM[$F0] bit 0
        public int CanisterDip = GameData.DefaultCanisterDip;
        public int PassCounter;                 // RAM[$A8]
        public int WaveCounter;                 // RAM[$EC]
        public int EnemyType = 1;               // RAM[$F5], 1..6
        public int Level = 1;                   // RAM[$FB]
        public int KillsThisWave;               // RAM[$F8]
        public int TheftsThisWave;              // RAM[$F9]
        public bool BonusFlag;                  // command byte bit 0
        public bool MotorOff = true;            // command byte bit 2 (1 = off)
        public int TorpedoTimer;                // RAM[$FD]
        public int Background;                  // RAM[$78]
        public int BackgroundStep;              // RAM[$7A]
        public int[] Score = new int[2];        // units word in tens, 0..999 after the carry (RAM[$F1]/[$F2])
        public int[] ScoreThousands = new int[2]; // thousands word (RAM[$FE]/[$FF]), each = 1000 tens
        public readonly TankRecord[] Enemies = new TankRecord[3];
        public readonly TankRecord[] Players = new TankRecord[2];
        public readonly Canister[] Canisters = new Canister[GameData.CanisterSlots];
        public readonly Shot[] Shots = new Shot[2 * GameData.ShotSlotsPerPlayer];
        public SoundState Sound;
        public readonly List<FireLine> FireLines = new List<FireLine>();
        public bool GameOverThisPass;
        /// <summary>Remake option (GameConfig.AutoFire, not in the arcade): holding fire repeats shots every
        /// <see cref="GameData.AutoFireInterval"/> passes.</summary>
        public bool AutoFire;
        /// <summary>Rule set of this game (GameConfig.Mode). Arcade by default; the hash includes it only when it is
        /// not Arcade, so Arcade checksums are unchanged from arcade-v1.0.</summary>
        public PlayMode PlayMode = PlayMode.Arcade;
        /// <summary>The rules the simulation reads for <see cref="PlayMode"/>.</summary>
        public GameRules Rules => GameRules.For(PlayMode);

        // ---- Enhanced-mode state (never changed in Arcade; hashed only when PlayMode is not Arcade) ----
        /// <summary>Cleared waves since the last type step (GameRules.ClearsPerTypeStep).</summary>
        public int ClearsTowardStep;
        /// <summary>Passes until each enemy may fire again (miss cooldown, chase reaction delay).</summary>
        public readonly int[] EnemyFireCooldown = new int[3];
        /// <summary>xorshift32 state for Enhanced randomness (aim error). Seeded at StartGame from the start seeds
        /// (record words B and the pass counter), so both online peers draw the same numbers. 0 in Arcade.</summary>
        public uint EnhancedRng;

        // ---- statistics (not part of the simulation, not hashed) ----
        /// <summary>Enemy fire tests that found a live player in reach this game (Arcade: every one is a kill).</summary>
        public int EnemyFireEvents;
        /// <summary>Of <see cref="EnemyFireEvents"/>, the ones that destroyed the player.</summary>
        public int EnemyFireHits;

        public void SeedEnhancedRng()
        {
            if (PlayMode == PlayMode.Arcade) { EnhancedRng = 0; return; }
            uint x = 0x9E3779B9u;
            for (int i = 0; i < 5; i++) x = (x ^ (uint)Record(i).B) * 0x01000193u;
            x = (x ^ (uint)PassCounter) * 0x01000193u;
            EnhancedRng = x == 0 ? 0x2545F491u : x;
        }

        public uint NextEnhancedRandom()
        {
            uint x = EnhancedRng; x ^= x << 13; x ^= x >> 17; x ^= x << 5; EnhancedRng = x; return x;
        }
        /// <summary>Passes fire has been held since the last shot (auto-fire only), per player.</summary>
        public readonly int[] AutoFireTimer = new int[2];
        /// <summary>Shots fired this game, per player (remake statistic; used by tests and the game-over screen).</summary>
        public readonly int[] ShotsFired = new int[2];

        public GameState()
        {
            for (int i = 0; i < 3; i++) Enemies[i] = new TankRecord { Base = 0x10 * i };
            for (int i = 0; i < 2; i++) Players[i] = new TankRecord { Base = 0x30 + 0x10 * i, Type = i == 0 ? 0 : 7 };
            // Reset seeds (lst:59-73).
            Enemies[0].B = Rng.SeedRecord0;
            Enemies[1].B = Rng.SeedRecord1;
            Enemies[2].B = Rng.SeedOthers;
            Players[0].B = Rng.SeedOthers;
            Players[1].B = Rng.SeedOthers;
            for (int i = 0; i < Canisters.Length; i++) Canisters[i] = new Canister { X2 = 1, Y2 = 1 };
        }

        public TankRecord Record(int index) => index < 3 ? Enemies[index] : Players[index - 3];
    }
}
