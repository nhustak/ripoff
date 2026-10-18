using RipOff.Core;

namespace RipOff.Core.Tests;

/// <summary>
/// A game with the options of 2026.10.17.8 (0x3FF: everything up to HUNTER ENEMIES, SAVED!! POPUP and SMART RESPAWN
/// off) still plays exactly like 2026.10.17.8, with the arcade's ships and with CRUISER (P1) + SCOUT (P2). Full state
/// hashes recorded on main at d79384b (2026.10.17.8).
/// </summary>
public class Enhanced20261017_8Tests
{
    public const EnhancedOption Release8 = (EnhancedOption)0x3FF;

    static readonly Dictionary<int, uint[]> ClassicShips = new()
    {
        [1] = new uint[] { 0x78889A13u, 0x3477EEE7u, 0x3CF41DD2u, 0x3344DA7Du, 0x3C526F8Au, 0xEACD4892u, 0x15A0608Cu, 0x30FC3CB8u, 0x1643C627u, 0x19804242u, 0x88FE958Fu, 0x088057C7u },
        [2] = new uint[] { 0x0C797116u, 0xC3DD7284u, 0x927C17E2u, 0x640F53B6u, 0x6E4E2774u, 0xC0953F99u, 0xF74ADCA0u, 0xBC7CE7DFu, 0x497CEACDu, 0x5D1187F2u, 0x4BEB327Bu, 0x62BF85C2u },
        [3] = new uint[] { 0x3CA32C5Eu, 0x7DEB6A30u, 0xF07D1DD1u, 0x47201C16u, 0x6F9DEB7Eu, 0x1128C400u, 0x1277E0C7u, 0xEB14C47Fu, 0x0984AC58u, 0x092DA050u, 0x13569FECu, 0xAC2AB3FAu },
        [4] = new uint[] { 0x737F6518u, 0x1E1E86A9u, 0xC68B56FFu, 0x20E1E90Cu, 0x7CA3A9EFu, 0x41DCD52Cu, 0x3B567CDEu, 0xDE3FB129u, 0x2E8403E2u, 0x94E77A07u, 0x97A78FEAu, 0xEC68420Du },
        [5] = new uint[] { 0xA5E43963u, 0x4B2EEA49u, 0xEAA01FE2u, 0x53EE655Fu, 0x3AA002D5u, 0xD0F5F39Du, 0x033E9F13u, 0xE22775C0u, 0xEE76C97Bu, 0xB7055D5Eu, 0xDDE7183Au, 0xEA614168u },
    };

    static readonly Dictionary<int, uint[]> CruiserAndScout = new()
    {
        [1] = new uint[] { 0x2D9F9800u, 0x8C6BD3FAu, 0x502D0CCBu, 0x5ADA7AEDu, 0x571C1F70u, 0x21480F4Eu, 0x4E159493u, 0xDF7C2EF1u, 0x92561B68u, 0xB2CA3667u, 0x39BA0ED0u, 0x59A74C32u },
        [2] = new uint[] { 0x99030832u, 0x12AEAD07u, 0x21DEB08Cu, 0x0ED2E0B0u, 0xA2E801CBu, 0x22BFB6CEu, 0x102CD551u, 0xA2E9D31Bu, 0x64B6B588u, 0xD340AC1Bu, 0x6D1EF7D8u, 0x02D6DE04u },
        [3] = new uint[] { 0xDFD98AABu, 0x64385D8Du, 0x48813539u, 0x2D822235u, 0x94D3076Du, 0x6D53BDAEu, 0xD88F690Eu, 0xE7CAD93Cu, 0x037C54F9u, 0x256330DFu, 0x1C036640u, 0xC377FA83u },
        [4] = new uint[] { 0x31419A99u, 0x7C0ECC39u, 0x269BBF1Au, 0x5B4821C8u, 0x32FF4E24u, 0x9651CA6Fu, 0x317F702Fu, 0xDFBE7D5Cu, 0xF3DF1D14u, 0x74494B0Du, 0x4A29C322u, 0xDBE95EF3u },
        [5] = new uint[] { 0x8C41CBADu, 0xB4E179C6u, 0x7C54FB20u, 0x77DB57F8u, 0x169CF0FEu, 0x5A8F9FCCu, 0xD63C6F28u, 0x5213C5F7u, 0xA24EC331u, 0x0174503Du, 0x0590F38Du, 0xA49CE0B0u },
    };

    [Theory]
    [MemberData(nameof(ArcadeDeterminismTests.Cases), MemberType = typeof(ArcadeDeterminismTests))]
    public void NewTogglesOffReproduce2026_10_17_8(int seed, bool onePlayer, int canisters, bool autoFire)
    {
        Assert.Equal(Enhanced20261017_7Tests.Release7 | EnhancedOption.TankEnemies | EnhancedOption.HunterEnemies, Release8);
        var cfg = new GameConfig(canisters, autoFire, PlayMode.Enhanced, Release8);
        Assert.Equal(ClassicShips[seed], ArcadeDeterminismTests.Run(seed, onePlayer, cfg));
        Assert.Equal(CruiserAndScout[seed], ArcadeDeterminismTests.Run(seed, onePlayer, cfg with { Ship1 = Ships.Cruiser, Ship2 = Ships.Scout }));
    }
}
