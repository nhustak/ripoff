using RipOff.Core;

namespace RipOff.Core.Tests;

/// <summary>
/// A game with the options of 2026.10.17.7 (0xFF: everything up to SHIP ABILITIES, every newer toggle off) still plays
/// exactly like 2026.10.17.7, with the arcade's ships and with CRUISER (P1) + SCOUT (P2). Full state hashes recorded on
/// main at 8e27c4f (2026.10.17.7).
/// </summary>
public class Enhanced20261017_7Tests
{
    public const EnhancedOption Release7 = (EnhancedOption)0xFF;

    static readonly Dictionary<int, uint[]> ClassicShips = new()
    {
        [1] = new uint[] { 0xAD81B4C7u, 0x76899807u, 0x1C954C82u, 0xFA7E4A62u, 0x3CC6834Au, 0x4F209980u, 0x176EA99Du, 0x46873EF5u, 0xD8C5EA08u, 0xBD8430B0u, 0x89BC74A3u, 0x7BDD9D29u },
        [2] = new uint[] { 0xD09E0936u, 0xEE363508u, 0xFCCA09A2u, 0x69195E75u, 0x149C429Bu, 0xD0A43692u, 0xB0726CD8u, 0x38C4EFD0u, 0xDAF2134Bu, 0x9DA497D6u, 0xDD031302u, 0x33268445u },
        [3] = new uint[] { 0xD44ACA4Eu, 0x9AF56C44u, 0x5B63FBC1u, 0xD2665886u, 0x571B639Au, 0x6F74AF08u, 0xC867F753u, 0xB98CB211u, 0x10BD0AD6u, 0xF217C9CDu, 0x55B1FC38u, 0x1B00CFB5u },
        [4] = new uint[] { 0x4326230Cu, 0x8957D5A5u, 0x033431D0u, 0x984F39FAu, 0x2B84B12Fu, 0x96431BBEu, 0x6FD780ABu, 0x5EF65A78u, 0xB2DF7137u, 0x6E236541u, 0xEF6D9D46u, 0x07BD058Fu },
        [5] = new uint[] { 0x13D0C977u, 0xBA41BB05u, 0x8B1D769Au, 0x41D922B7u, 0x92E81689u, 0x51DA41F5u, 0x6840B434u, 0x3ABA87ACu, 0xA0870607u, 0xBE2EC92Cu, 0xD1F0C507u, 0x7B7DE134u },
    };

    static readonly Dictionary<int, uint[]> CruiserAndScout = new()
    {
        [1] = new uint[] { 0x864723CCu, 0x5941C492u, 0xF6E242EFu, 0x3FE07FE5u, 0xF85C5D78u, 0x2FAD1232u, 0x803F5EDBu, 0x90BE1643u, 0x1664B5B6u, 0x5D045718u, 0x52092C8Au, 0x0ED5A9DCu },
        [2] = new uint[] { 0xEA706096u, 0x8586AB67u, 0x804537B0u, 0x7958C92Cu, 0x6BBC6BC1u, 0x49BDC26Cu, 0xD40EFF6Du, 0x0690712Du, 0xC8D93898u, 0xCE3AA6FBu, 0x7ED91C39u, 0xCDFD8D48u },
        [3] = new uint[] { 0x7D2D88FBu, 0x8B5D7769u, 0x381DFF55u, 0x4448AF95u, 0x189BCB65u, 0xB3B899CDu, 0x624268F0u, 0xBEF5E936u, 0xB07C31FFu, 0xAEB1A066u, 0x1E005D67u, 0xF66546B1u },
        [4] = new uint[] { 0x9A854C5Du, 0xBCEC6639u, 0xF59768C0u, 0x906CAA54u, 0xD849ADC3u, 0x7A00BC5Au, 0x9377E51Bu, 0xE03DD454u, 0xFCCB3069u, 0x5CA1B965u, 0x8C7AE570u, 0x502D1240u },
        [5] = new uint[] { 0x34E2E5B5u, 0x8169008Eu, 0x3F27771Cu, 0x6B3BE868u, 0x497AA0B2u, 0x1C915598u, 0x532C5A50u, 0x57A40C03u, 0xA7AB6B19u, 0xC5198B56u, 0x6B635009u, 0x5215C131u },
    };

    [Theory]
    [MemberData(nameof(ArcadeDeterminismTests.Cases), MemberType = typeof(ArcadeDeterminismTests))]
    public void NewTogglesOffReproduce2026_10_17_7(int seed, bool onePlayer, int canisters, bool autoFire)
    {
        Assert.Equal(Enhanced20261017_6Tests.Release6 | EnhancedOption.ShipAbilities, Release7);
        var cfg = new GameConfig(canisters, autoFire, PlayMode.Enhanced, Release7);
        Assert.Equal(ClassicShips[seed], ArcadeDeterminismTests.Run(seed, onePlayer, cfg));
        Assert.Equal(CruiserAndScout[seed], ArcadeDeterminismTests.Run(seed, onePlayer, cfg with { Ship1 = Ships.Cruiser, Ship2 = Ships.Scout }));
    }
}
