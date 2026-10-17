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
    public readonly record struct GameConfig(int Canisters, bool AutoFire = false, PlayMode Mode = PlayMode.Arcade,
        EnhancedOption Options = EnhancedOption.All)
    {
        /// <summary>Layout version of <see cref="Encode"/>; bump when fields are added.</summary>
        public const byte SchemaVersion = 3; // 2: play mode byte added; 3: Enhanced options byte added

        private const byte FlagAutoFire = 1;

        public static GameConfig Default => new(GameData.CanistersForDip[GameData.DefaultCanisterDip]);

        /// <summary>Canister DIP index for <see cref="Canisters"/> (-1 if the arcade has no such setting).</summary>
        public int CanisterDip => Array.IndexOf(GameData.CanistersForDip, Canisters);

        /// <summary>Null when this build can run the config, else the reason it cannot.</summary>
        public string? Validate()
        {
            if (CanisterDip < 0) return $"unsupported canister count {Canisters} (allowed: {string.Join("/", GameData.CanistersForDip)})";
            if (!GameRules.IsDefined(Mode)) return $"unknown play mode {(int)Mode}";
            if ((Options & ~EnhancedOption.All) != 0) return $"unknown enhanced options 0x{(int)Options:X2}";
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
        }

        /// <summary>Wire form: schema, canisters, flags (bit 0 auto-fire; other bits reserved, must be 0), play mode,
        /// Enhanced options (EnhancedOption bits).</summary>
        public byte[] Encode() => new[] { SchemaVersion, (byte)Canisters, (byte)(AutoFire ? FlagAutoFire : 0), (byte)Mode, (byte)Options };

        public static GameConfig Decode(ReadOnlySpan<byte> b)
        {
            if (b.Length < 1) throw new FormatException("game config too short");
            if (b[0] != SchemaVersion) throw new FormatException($"game config schema {b[0]}, this build reads {SchemaVersion}");
            if (b.Length < 5) throw new FormatException("game config too short");
            if ((b[2] & ~FlagAutoFire) != 0) throw new FormatException($"unknown game config flags 0x{b[2]:X2}");
            // An unknown mode decodes as is: Validate() then refuses it with a reason the host is told (like canisters).
            // Unknown option bits decode as is too: Validate() refuses them.
            return new GameConfig(b[1], (b[2] & FlagAutoFire) != 0, (PlayMode)b[3], (EnhancedOption)b[4]);
        }
    }
}
