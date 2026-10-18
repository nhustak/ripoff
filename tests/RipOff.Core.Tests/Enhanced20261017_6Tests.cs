using RipOff.Core;

namespace RipOff.Core.Tests;

/// <summary>
/// A game with the options of 2026.10.17.6 (0x7F: the six warp/tuning toggles + SHIP SELECT, every newer toggle off)
/// still plays exactly like 2026.10.17.6. Full state hashes recorded on main at d1023cc (2026.10.17.6).
/// </summary>
public class Enhanced20261017_6Tests
{
    public const EnhancedOption Release6 = (EnhancedOption)0x7F;

    static readonly Dictionary<int, uint[]> EnhancedAt20261017_6 = new()
    {
        [1] = new uint[] { 0x7E7F9EA7u, 0xAAAD94E7u, 0xD5B8CF62u, 0xFC4CC342u, 0xE09F722Au, 0xB1D3EEE0u, 0x78B5A2FDu, 0x22BB5355u, 0xC3F4EC68u, 0xA91E5A10u, 0x734C0083u, 0x05BEFC89u },
        [2] = new uint[] { 0xA9534216u, 0x0B69DA68u, 0xB75A8582u, 0x2BCEF2D5u, 0x3030927Bu, 0xC8E67F72u, 0x21C94D38u, 0xD2AC2030u, 0xEB52152Bu, 0xA1B70BB6u, 0x6C1CA4E2u, 0x7C8566A5u },
        [3] = new uint[] { 0x25A81E2Eu, 0x0FC147A4u, 0x804D0321u, 0x6FEC3E66u, 0xDFD94D7Au, 0x4E6E5E68u, 0xFD90C733u, 0xC14BC871u, 0x6AEF0DB6u, 0xBA2CA72Du, 0xAA32E498u, 0x6B252415u },
        [4] = new uint[] { 0x5E933E6Cu, 0x42805F05u, 0xD175B130u, 0x3150FCDAu, 0xDB6B430Fu, 0x8228B69Eu, 0x269DAA8Bu, 0xFE9760D8u, 0xCEADF017u, 0x4FCC3FA1u, 0x95C74426u, 0x22DF6C6Fu },
        [5] = new uint[] { 0x3EB16957u, 0x93EA7965u, 0xF2B1607Au, 0xCDBB3197u, 0x74BCCEE9u, 0x73F79655u, 0x4FE4E194u, 0xECC58A0Cu, 0x4B6A21E7u, 0xBE41898Cu, 0x855620E7u, 0xAF4C9E94u },
    };

    [Theory]
    [MemberData(nameof(ArcadeDeterminismTests.Cases), MemberType = typeof(ArcadeDeterminismTests))]
    public void NewTogglesOffReproduce2026_10_17_6(int seed, bool onePlayer, int canisters, bool autoFire)
    {
        Assert.Equal(Enhanced20261017_5Tests.Release5 | EnhancedOption.ShipSelect, Release6);
        var cfg = new GameConfig(canisters, autoFire, PlayMode.Enhanced, Release6);
        Assert.Equal(EnhancedAt20261017_6[seed], ArcadeDeterminismTests.Run(seed, onePlayer, cfg));
    }
}
