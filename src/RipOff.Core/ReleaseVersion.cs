using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace RipOff.Core
{
    /// <summary>
    /// Release version YYYY.MM.DD.N: the release date (America/New_York) and the release number of that day, from 1.
    /// The display/tag form keeps month and day zero-padded (2026.10.17.1, tag v2026.10.17.1). The numeric .NET
    /// version (AssemblyVersion/FileVersion) is the same four numbers; every part must be ≤ 65535.
    /// tools/publish.ps1 picks the next one through tools/release/NextVersion (this class).
    /// </summary>
    public readonly record struct ReleaseVersion(int Year, int Month, int Day, int N)
    {
        public const string TagPrefix = "v";
        public const string TimeZoneId = "America/New_York";
        private static readonly Regex Pattern = new(@"^(\d{4})\.(\d{2})\.(\d{2})\.([1-9]\d{0,4})$", RegexOptions.CultureInvariant);

        public DateOnly Date => new(Year, Month, Day);

        /// <summary>Zero-padded display/tag form, e.g. 2026.10.17.1.</summary>
        public override string ToString() => $"{Year:D4}.{Month:D2}.{Day:D2}.{N}";
        public string Tag => TagPrefix + ToString();
        /// <summary>Numeric form for AssemblyVersion/FileVersion.</summary>
        public Version ToVersion() => new(Year, Month, Day, N);

        /// <summary>Strict parse of the display form (zero-padded month/day, N ≥ 1, a real calendar date, N ≤ 65535).</summary>
        public static bool TryParse(string? s, out ReleaseVersion v)
        {
            v = default;
            if (s == null) return false;
            var m = Pattern.Match(s.Trim());
            if (!m.Success) return false;
            int y = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), mo = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            int d = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), n = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            if (y < 2000 || mo < 1 || mo > 12 || d < 1 || d > DateTime.DaysInMonth(y, mo) || n > 65535) return false;
            v = new ReleaseVersion(y, mo, d, n);
            return true;
        }

        public static ReleaseVersion Parse(string s) =>
            TryParse(s, out var v) ? v : throw new FormatException($"'{s}' is not a release version YYYY.MM.DD.N (e.g. 2026.10.17.1)");

        /// <summary>Tag "v2026.10.17.3" (or a ref "refs/tags/v2026.10.17.3", "^{}" suffix ignored) → version.</summary>
        public static bool TryParseTag(string? tag, out ReleaseVersion v)
        {
            v = default;
            if (tag == null) return false;
            tag = tag.Trim();
            if (tag.EndsWith("^{}", StringComparison.Ordinal)) tag = tag[..^3];
            int slash = tag.LastIndexOf('/');
            if (slash >= 0) tag = tag[(slash + 1)..];
            return tag.StartsWith(TagPrefix, StringComparison.Ordinal) && TryParse(tag[TagPrefix.Length..], out v);
        }

        /// <summary>Next release on <paramref name="date"/>: highest N among existing tags of that date + 1, or 1.</summary>
        public static ReleaseVersion Next(DateOnly date, IEnumerable<string> existingTags)
        {
            int max = 0;
            foreach (var t in existingTags)
                if (TryParseTag(t, out var v) && v.Date == date && v.N > max) max = v.N;
            if (max >= 65535) throw new InvalidOperationException("release number would exceed 65535");
            return new ReleaseVersion(date.Year, date.Month, date.Day, max + 1);
        }

        /// <summary>Today in America/New_York.</summary>
        public static DateOnly Today(DateTimeOffset? now = null)
        {
            var utc = now ?? DateTimeOffset.UtcNow;
            var tz = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId); // IANA id; .NET 6+ maps it on Windows too (ICU)
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, tz).DateTime);
        }
    }

    /// <summary>
    /// This build's version, from the assembly attributes that Directory.Build.props/.targets write: InformationalVersion
    /// "2026.10.17.1+abc1234" for a release, "dev+abc1234" for a dev build (no version given).
    /// </summary>
    public static class BuildInfo
    {
        public static string Informational { get; } =
            typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev+unknown";

        /// <summary>The release version, or null for a dev build.</summary>
        public static string? Release => ReleaseOf(Informational);
        public static string Commit => CommitOf(Informational);

        /// <summary>Short text for screens: "V2026.10.17.1" or "DEV ABC1234".</summary>
        public static string Display => DisplayOf(Informational);

        public static string? ReleaseOf(string informational)
        {
            string v = informational.Split('+')[0];
            return ReleaseVersion.TryParse(v, out var r) ? r.ToString() : null;
        }

        public static string CommitOf(string informational)
        {
            int p = informational.IndexOf('+');
            return p >= 0 && p + 1 < informational.Length ? informational[(p + 1)..] : "unknown";
        }

        /// <summary>"V2026.10.17.1" for a release, "DEV ABC1234" for a dev build, "UNKNOWN" for nothing.</summary>
        public static string DisplayOf(string? informational)
        {
            if (string.IsNullOrWhiteSpace(informational)) return "UNKNOWN";
            if (ReleaseOf(informational) is { } r) return "V" + r;
            return informational.Contains('+') ? "DEV " + CommitOf(informational).ToUpperInvariant() : informational.ToUpperInvariant();
        }
    }
}
