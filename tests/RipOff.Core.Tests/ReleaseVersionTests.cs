using RipOff.Core;

namespace RipOff.Core.Tests;

/// <summary>Release versions YYYY.MM.DD.N (README "Versioning"; tools/publish.ps1 uses ReleaseVersion through
/// tools/release/NextVersion).</summary>
public class ReleaseVersionTests
{
    [Theory]
    [InlineData("2026.10.17.1", 2026, 10, 17, 1)]
    [InlineData("2027.01.05.12", 2027, 1, 5, 12)]
    [InlineData("2028.02.29.65535", 2028, 2, 29, 65535)]
    [InlineData(" 2026.10.17.3 ", 2026, 10, 17, 3)]
    public void ParsesTheZeroPaddedForm(string s, int y, int m, int d, int n)
    {
        Assert.True(ReleaseVersion.TryParse(s, out var v));
        Assert.Equal(new ReleaseVersion(y, m, d, n), v);
        Assert.Equal(s.Trim(), v.ToString());
        Assert.Equal("v" + s.Trim(), v.Tag);
        Assert.Equal(new Version(y, m, d, n), v.ToVersion());
    }

    [Theory]
    [InlineData("2026.1.17.1")]       // month not zero-padded
    [InlineData("2026.10.7.1")]       // day not zero-padded
    [InlineData("2026.10.17.0")]      // N starts at 1
    [InlineData("2026.10.17.01")]     // N not padded
    [InlineData("2026.10.17")]
    [InlineData("2026.10.17.1.2")]
    [InlineData("2026.13.01.1")]
    [InlineData("2026.02.30.1")]
    [InlineData("2027.02.29.1")]      // not a leap year
    [InlineData("2026.10.17.65536")]  // > 65535 (Version component limit)
    [InlineData("v2026.10.17.1")]
    [InlineData("0.0.0.0")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsEverythingElse(string? s)
    {
        Assert.False(ReleaseVersion.TryParse(s, out _));
        if (s != null) Assert.Throws<FormatException>(() => ReleaseVersion.Parse(s));
    }

    [Fact]
    public void NextNumberForTheDay()
    {
        var day = new DateOnly(2026, 10, 17);
        Assert.Equal("2026.10.17.1", ReleaseVersion.Next(day, Array.Empty<string>()).ToString());
        Assert.Equal("2026.10.17.1", ReleaseVersion.Next(day, new[] { "arcade-v1.0", "enhanced-preview-1", "v2026.10.16.4" }).ToString());
        Assert.Equal("2026.10.17.4", ReleaseVersion.Next(day, new[] { "v2026.10.17.1", "v2026.10.17.3", "v2026.10.18.9" }).ToString());
        // local tag names and remote refs (git ls-remote, peeled ^{} entries), duplicates
        Assert.Equal("2026.10.17.11", ReleaseVersion.Next(day, new[] { "refs/tags/v2026.10.17.10", "refs/tags/v2026.10.17.10^{}", "v2026.10.17.2" }).ToString());
        // malformed look-alikes are ignored
        Assert.Equal("2026.10.17.1", ReleaseVersion.Next(day, new[] { "v2026.10.17.x", "v2026.10.17.0", "2026.10.17.5", "v2026.10.17" }).ToString());
        Assert.Equal("2027.01.05.1", ReleaseVersion.Next(new DateOnly(2027, 1, 5), new[] { "v2026.10.17.1" }).ToString());
    }

    [Fact]
    public void TodayIsNewYorkTime()
    {
        // 03:30 UTC on Oct 18 is still Oct 17 in New York (UTC-4, EDT); 05:30 UTC on Jan 6 is Jan 6 00:30 EST (UTC-5).
        Assert.Equal(new DateOnly(2026, 10, 17), ReleaseVersion.Today(new DateTimeOffset(2026, 10, 18, 3, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2027, 1, 6), ReleaseVersion.Today(new DateTimeOffset(2027, 1, 6, 5, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2027, 1, 5), ReleaseVersion.Today(new DateTimeOffset(2027, 1, 6, 4, 30, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData("2026.10.17.1+abc1234", "V2026.10.17.1", "2026.10.17.1", "abc1234")]
    [InlineData("dev+abc1234", "DEV ABC1234", null, "abc1234")]
    [InlineData("dev+nogit", "DEV NOGIT", null, "nogit")]
    public void BuildInfoText(string informational, string display, string? release, string commit)
    {
        Assert.Equal(display, BuildInfo.DisplayOf(informational));
        Assert.Equal(release, BuildInfo.ReleaseOf(informational));
        Assert.Equal(commit, BuildInfo.CommitOf(informational));
    }

    [Fact]
    public void UnknownBuildText()
    {
        Assert.Equal("UNKNOWN", BuildInfo.DisplayOf(""));
        Assert.Equal("UNKNOWN", BuildInfo.DisplayOf(null));
    }

    /// <summary>Directory.Build.props/.targets: a test build is either a dev build or a release build, always with a commit.</summary>
    [Fact]
    public void ThisBuildCarriesAVersionAndACommit()
    {
        Assert.Matches(@"^(dev|\d{4}\.\d{2}\.\d{2}\.[1-9]\d*)\+[0-9a-z]+$", BuildInfo.Informational);
        var asm = typeof(BuildInfo).Assembly.GetName().Version!;
        if (BuildInfo.Release is { } r) Assert.Equal(ReleaseVersion.Parse(r).ToVersion(), asm);
        else Assert.Equal(new Version(0, 0, 0, 0), asm);
    }
}
