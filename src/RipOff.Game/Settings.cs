using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RipOff.Core;

namespace RipOff.Game
{
    /// <summary>
    /// Player settings, saved as JSON (<see cref="SettingsStore"/>). Out-of-range values from a hand-edited file are
    /// clamped by <see cref="Normalize"/>. AutoFire, Canisters and Mode form the <see cref="GameConfig"/> of games this
    /// machine starts (local, or online as host); the rest is local only.
    /// </summary>
    public sealed class Settings
    {
        public const double VolumeStep = 0.1;
        public const float DeadzoneMin = 0.10f, DeadzoneMax = 0.60f, DeadzoneStep = 0.05f;

        public int Version { get; set; } = 2;
        /// <summary>Default off = the arcade's one shot per press.</summary>
        public bool AutoFire { get; set; }
        /// <summary>Default 8 = the arcade's default DIP.</summary>
        public int Canisters { get; set; } = GameConfig.Default.Canisters;
        public double Volume { get; set; } = 1.0;
        public bool Muted { get; set; }
        public bool Fullscreen { get; set; }
        /// <summary>Last windowed client-area position and size in desktop pixels (never the fullscreen size). Null in
        /// older files or before the first run: default 1024×768 centered. Validated against the displays at start
        /// (WindowGeometry.Resolve).</summary>
        public int? WindowX { get; set; }
        public int? WindowY { get; set; }
        public int? WindowWidth { get; set; }
        public int? WindowHeight { get; set; }
        /// <summary>The window was maximized when last windowed (the X/Y/Width/Height above stay the un-maximized ones).</summary>
        public bool WindowMaximized { get; set; }

        /// <summary>The saved windowed rect, or null if any part is missing.</summary>
        [JsonIgnore]
        public PixelRect? WindowRect =>
            WindowX is int x && WindowY is int y && WindowWidth is int w && WindowHeight is int h ? new PixelRect(x, y, w, h) : null;
        public float StickDeadzone { get; set; } = PadMapper.DefaultStickDeadzone;
        /// <summary>Initialize Steam at launch, before the window (takes effect on the next start), so the Steam overlay
        /// and its invite dialog work. Default OFF (NOTES §24): Steam then starts only when a Steam online option is
        /// picked, because an initialized Steam client under App ID 480 can take the controller away (Steam Input).
        /// Replaces v1's "SteamEnabled" (default on), which is ignored when an old file is read.</summary>
        public bool SteamAtLaunch { get; set; }
        /// <summary>Last choice on the MODE SELECT screen (default Arcade = the original game). Saved by name
        /// ("Arcade" / "Enhanced"); a missing value or an unknown number reads as Arcade.</summary>
        [JsonConverter(typeof(JsonStringEnumConverter<PlayMode>))]
        public PlayMode Mode { get; set; } = PlayMode.Arcade;

        /// <summary>Enhanced variations switched OFF on the ENHANCED OPTIONS page, by name (EnhancedOption, e.g.
        /// "WarpIn"). Stored as the "off" list so that a missing list (older files) and variations added later
        /// default ON. Unknown names are ignored.</summary>
        public List<string> EnhancedOff { get; set; } = new();

        [JsonIgnore]
        public EnhancedOption EnhancedOptions
        {
            get
            {
                var o = EnhancedOption.All;
                foreach (var v in GameRules.Variations) if (EnhancedOff?.Contains(v.Option.ToString()) == true) o &= ~v.Option;
                return o;
            }
            set
            {
                EnhancedOff = new List<string>();
                foreach (var v in GameRules.Variations) if ((value & v.Option) == 0) EnhancedOff.Add(v.Option.ToString());
            }
        }

        /// <summary>Last ships picked on the SHIP SELECT screen (Ships.All ids): P1 (also "your ship" online) and P2 of a
        /// local 2-player game. Defaults = the arcade's ships; Normalize keeps them known and different.</summary>
        public int Ship1 { get; set; } = Ships.Classic1;
        public int Ship2 { get; set; } = Ships.Classic2;

        public GameConfig GameConfig => new(Canisters, AutoFire, Mode, EnhancedOptions, Ship1, Ship2);

        public Settings Normalize()
        {
            Version = 2;
            if (Array.IndexOf(GameData.CanistersForDip, Canisters) < 0) Canisters = GameConfig.Default.Canisters;
            if (!GameRules.IsDefined(Mode)) Mode = PlayMode.Arcade;
            EnhancedOptions = EnhancedOptions; // drops unknown or duplicate names from EnhancedOff
            if (!Ships.IsDefined(Ship1)) Ship1 = Ships.Classic1;
            if (!Ships.IsDefined(Ship2) || Ship2 == Ship1) Ship2 = Ships.FirstOtherThan(Ship1);
            Volume = double.IsFinite(Volume) ? Math.Round(Math.Clamp(Volume, 0, 1) / VolumeStep) * VolumeStep : 1.0;
            StickDeadzone = float.IsFinite(StickDeadzone) ? MathF.Round(Math.Clamp(StickDeadzone, DeadzoneMin, DeadzoneMax) / DeadzoneStep) * DeadzoneStep : PadMapper.DefaultStickDeadzone;
            return this;
        }

        public Settings Clone() { var c = (Settings)MemberwiseClone(); c.EnhancedOff = new List<string>(EnhancedOff ?? new List<string>()); return c; }
    }

    /// <summary>
    /// Loads and saves <see cref="Settings"/> as JSON in %APPDATA%\&lt;Branding.SettingsFolder&gt;\settings.json
    /// (Environment.SpecialFolder.ApplicationData; ~/.config on Linux). A missing file gives defaults. A corrupt
    /// file is renamed to settings.json.corrupt (kept for inspection), defaults are used, and the reason is
    /// reported. Saving writes a temporary file and then replaces the old one.
    /// </summary>
    public sealed class SettingsStore
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        public string FilePath { get; }

        public SettingsStore(string? directory = null)
        {
            directory ??= Path.Combine(AppDataRoot(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create)), Branding.SettingsFolder);
            FilePath = Path.Combine(directory, "settings.json");
        }

        /// <summary>
        /// The per-user folder, never relative. GetFolderPath returns "" when the folder does not exist (and Create
        /// can fail), which used to put settings.json and input-log.txt under the current directory (found with the
        /// SDL end-to-end run on Linux, NOTES §25). Falls back to the folder next to the exe.
        /// </summary>
        public static string AppDataRoot(string? folder) =>
            !string.IsNullOrEmpty(folder) && Path.IsPathRooted(folder) ? folder : AppContext.BaseDirectory;

        /// <summary>Never throws. <paramref name="warning"/> explains a reset (null when the file loaded or was missing).</summary>
        public Settings Load(out string? warning)
        {
            warning = null;
            try
            {
                if (!File.Exists(FilePath)) return new Settings();
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? throw new JsonException("empty settings file");
                return s.Normalize();
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                warning = "settings file unreadable (" + e.GetType().Name + "), defaults used";
                try { File.Move(FilePath, FilePath + ".corrupt", overwrite: true); warning += "; old file kept as settings.json.corrupt"; }
                catch (Exception) { /* read-only folder: keep going with defaults */ }
                return new Settings();
            }
        }

        /// <summary>Never throws; returns the error text or null.</summary>
        public string? Save(Settings s)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(s.Clone().Normalize(), Json));
                File.Move(tmp, FilePath, overwrite: true);
                return null;
            }
            catch (Exception e) { return "could not save settings: " + e.Message; }
        }
    }
}
