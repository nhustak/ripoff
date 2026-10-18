using System;

namespace RipOff.Core
{
    /// <summary>
    /// Every setting that changes the simulation (and so must be identical on both peers online). Online, the host's
    /// config is sent at session start, validated by the guest (a config this build cannot run is refused) and its
    /// hash echoed back and compared, so both peers provably run the same rules (RipOff.Net LockstepSession).
    /// Settings that do not touch the simulation (volume, dead zone, window) do not belong here.
    /// Game over stays as in the arcade: when all canisters are gone.
    /// </summary>
    /// <param name="Canisters">Canister count, one of the arcade DIP values 12/16/4/8 (GameData.CanistersForDip).</param>
    /// <param name="AutoFire">Remake option (default off = arcade one shot per press): holding fire repeats a shot
    /// every <see cref="GameData.AutoFireInterval"/> passes, within the 4-shot cap (NOTES §23).</param>
    /// <param name="Mode">Rule set (<see cref="PlayMode"/>, default Arcade). Online the guest adopts the host's mode.</param>
    /// <param name="Options">Enhanced variations that are on (default all). Arcade ignores them. Online the guest
    /// adopts the host's options like the mode.</param>
    /// <param name="Ship1">P1's ship (Ships.All id), used in Enhanced with SHIP SELECT on. Default the arcade's P1 ship.</param>
    /// <param name="Ship2">P2's ship; never the same as <paramref name="Ship1"/>. Online the host resolves it from the
    /// guest's wish (LockstepSession).</param>
    public readonly record struct GameConfig(int Canisters, bool AutoFire = false, PlayMode Mode = PlayMode.Arcade,
        EnhancedOption Options = EnhancedOption.Defaults, int Ship1 = Ships.Classic1, int Ship2 = Ships.Classic2)
    {
        /// <summary>Layout version of <see cref="Encode"/>; bump when fields are added.</summary>
        public const byte SchemaVersion = 4; // 2: play mode byte; 3: Enhanced options byte; 4: options 16-bit + ships
        public const int EncodedLength = 8;

        private const byte FlagAutoFire = 1;

        public static GameConfig Default => new(GameData.CanistersForDip[GameData.DefaultCanisterDip]);

        /// <summary>Canister DIP index for <see cref="Canisters"/> (-1 if the arcade has no such setting).</summary>
        public int CanisterDip => Array.IndexOf(GameData.CanistersForDip, Canisters);

        /// <summary>Null when this build can run the config, else the reason it cannot.</summary>
        public string? Validate()
        {
            if (CanisterDip < 0) return $"unsupported canister count {Canisters} (allowed: {string.Join("/", GameData.CanistersForDip)})";
            if (!GameRules.IsDefined(Mode)) return $"unknown play mode {(int)Mode}";
            if ((Options & ~EnhancedOption.All) != 0) return $"unknown enhanced options 0x{(int)Options:X4}";
            if (!Ships.IsDefined(Ship1) || !Ships.IsDefined(Ship2)) return $"unknown ship {(Ships.IsDefined(Ship1) ? Ship2 : Ship1)}";
            if (Ship1 == Ship2) return $"both players on ship {Ships.Get(Ship1).Name}";
            return null;
        }

        /// <summary>Applies the config to a state before StartGame.</summary>
        public void ApplyTo(GameState s)
        {
            if (Validate() is { } err) throw new ArgumentException(err);
            s.CanisterDip = CanisterDip;
            s.AutoFire = AutoFire;
            s.PlayMode = Mode;
            s.Options = Options;
            bool pick = GameRules.For(Mode, Options).ShipSelect;
            s.Ships[0] = pick ? Ship1 : Ships.Default(0);
            s.Ships[1] = pick ? Ship2 : Ships.Default(1);
        }

        /// <summary>The ships the players actually fly (the arcade pairing unless Enhanced with SHIP SELECT on).</summary>
        public (int P1, int P2) EffectiveShips => GameRules.For(Mode, Options).ShipSelect ? (Ship1, Ship2) : (Ships.Default(0), Ships.Default(1));

        /// <summary>Wire form: schema, canisters, flags (bit 0 auto-fire; other bits reserved, must be 0), play mode,
        /// Enhanced options (EnhancedOption bits, 16-bit little-endian), P1 ship, P2 ship.</summary>
        public byte[] Encode() => new[] { SchemaVersion, (byte)Canisters, (byte)(AutoFire ? FlagAutoFire : 0), (byte)Mode,
            (byte)(int)Options, (byte)((int)Options >> 8), (byte)Ship1, (byte)Ship2 };

        public static GameConfig Decode(ReadOnlySpan<byte> b)
        {
            if (b.Length < 1) throw new FormatException("game config too short");
            if (b[0] != SchemaVersion) throw new FormatException($"game config schema {b[0]}, this build reads {SchemaVersion}");
            if (b.Length < EncodedLength) throw new FormatException("game config too short");
            if ((b[2] & ~FlagAutoFire) != 0) throw new FormatException($"unknown game config flags 0x{b[2]:X2}");
            // An unknown mode decodes as is: Validate() then refuses it with a reason the host is told (like canisters).
            // Unknown option bits decode as is too: Validate() refuses them.
            // Unknown ships decode as is too: Validate() refuses them (and duplicates).
            return new GameConfig(b[1], (b[2] & FlagAutoFire) != 0, (PlayMode)b[3], (EnhancedOption)(b[4] | (b[5] << 8)), b[6], b[7]);
        }
    }
}
