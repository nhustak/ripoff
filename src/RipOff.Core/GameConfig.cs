using System;

namespace RipOff.Core
{
    /// <summary>
    /// Every setting that changes the simulation (and so must be identical on both peers online). Online, the host's
    /// config is sent at session start, validated by the guest (a config this build cannot run is refused) and its
    /// hash echoed back and compared, so both peers provably run the same rules (RipOff.Net LockstepSession).
    /// Settings that do not touch the simulation (volume, key bindings, window) do not belong here.
    /// Game over stays as in the arcade: when all canisters are gone.
    /// </summary>
    /// <param name="Canisters">Canister count, one of the arcade DIP values 12/16/4/8 (GameData.CanistersForDip).</param>
    /// <param name="AutoFire">RESERVED for the front-end milestone: this build does not implement auto-fire, so
    /// <see cref="Validate"/> refuses true (a peer that has it on cannot silently desync against one that doesn't).</param>
    public readonly record struct GameConfig(int Canisters, bool AutoFire = false)
    {
        /// <summary>Layout version of <see cref="Encode"/>; bump when fields are added.</summary>
        public const byte SchemaVersion = 1;
        /// <summary>Whether this build's simulation honours <see cref="AutoFire"/>. Set true together with the implementation.</summary>
        public const bool AutoFireSupported = false;

        private const byte FlagAutoFire = 1;

        public static GameConfig Default => new(GameData.CanistersForDip[GameData.DefaultCanisterDip]);

        /// <summary>Canister DIP index for <see cref="Canisters"/> (-1 if the arcade has no such setting).</summary>
        public int CanisterDip => Array.IndexOf(GameData.CanistersForDip, Canisters);

        /// <summary>Null when this build can run the config, else the reason it cannot.</summary>
        public string? Validate()
        {
            if (CanisterDip < 0) return $"unsupported canister count {Canisters} (allowed: {string.Join("/", GameData.CanistersForDip)})";
            if (AutoFire && !AutoFireSupported) return "auto-fire is not supported by this build";
            return null;
        }

        /// <summary>Applies the config to a fresh state before StartGame.</summary>
        public void ApplyTo(GameState s)
        {
            if (Validate() is { } err) throw new ArgumentException(err);
            s.CanisterDip = CanisterDip;
        }

        /// <summary>Wire form: schema, canisters, flags (bit 0 auto-fire; other bits reserved, must be 0).</summary>
        public byte[] Encode() => new[] { SchemaVersion, (byte)Canisters, (byte)(AutoFire ? FlagAutoFire : 0) };

        public static GameConfig Decode(ReadOnlySpan<byte> b)
        {
            if (b.Length < 3) throw new FormatException("game config too short");
            if (b[0] != SchemaVersion) throw new FormatException($"game config schema {b[0]}, this build reads {SchemaVersion}");
            if ((b[2] & ~FlagAutoFire) != 0) throw new FormatException($"unknown game config flags 0x{b[2]:X2}");
            return new GameConfig(b[1], (b[2] & FlagAutoFire) != 0);
        }
    }
}
