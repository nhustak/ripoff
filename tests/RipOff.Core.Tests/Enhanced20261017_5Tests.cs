using RipOff.Core;

namespace RipOff.Core.Tests;

/// <summary>
/// Later Enhanced features never change a game played with the options of 2026.10.17.5 (all of WARP-IN, ENEMY AIM ERROR,
/// SLOWER RAMP, SLOWER TOWING, RANDOM WARP-IN, WARP KILLS = 0x3F, every newer toggle off): their state is hashed only while
/// their own toggle is on (StateHash). Full state hashes recorded on main at 5734079 (2026.10.17.5).
/// </summary>
public class Enhanced20261017_5Tests
{
    public const EnhancedOption Release5 = (EnhancedOption)0x3F;

    static readonly Dictionary<int, uint[]> EnhancedAt20261017_5 = new()
    {
        [1] = new uint[] { 0x0CFA37B7u, 0x0C48CF77u, 0x0CB6F946u, 0xF13970A6u, 0x9223558Eu, 0xB5F9F5BCu, 0xC74554ADu, 0xD80DF1C5u, 0xA2E8ECC4u, 0x12EBC52Cu, 0x98937D23u, 0x00944DC9u },
        [2] = new uint[] { 0xB905FA0Au, 0x140EDAC4u, 0xDBB8A966u, 0x8FE8A045u, 0x020911DBu, 0xCA1DED96u, 0x157D0214u, 0xC8144DCCu, 0xD4A9B20Bu, 0xE0E3C8AAu, 0x8EF02146u, 0x06820895u },
        [3] = new uint[] { 0x4B017EA2u, 0x486B09A0u, 0x24AB4021u, 0xC45DA7DAu, 0xC724D21Eu, 0x69F1C644u, 0x55839813u, 0xCB0BAE71u, 0x7BAC262Au, 0x4BD1059Du, 0x1F6032F4u, 0x7E9CB685u },
        [4] = new uint[] { 0xB2C214A8u, 0xED2A22F5u, 0x2467AACCu, 0x701113FEu, 0x6EB0385Fu, 0xC355CA52u, 0x2DAE096Bu, 0x8DB03FB4u, 0xD61548A7u, 0x2C9798A1u, 0xF9D4141Au, 0xEDED1E3Fu },
        [5] = new uint[] { 0xA2C176E7u, 0xE1AC2E55u, 0xC0155E1Eu, 0xDC786227u, 0x79DAD029u, 0x403ECE45u, 0xBD3ABDD0u, 0x5B05D6C8u, 0x307054F7u, 0x68255148u, 0xC30A63F7u, 0x1EF72C50u },
    };

    [Theory]
    [MemberData(nameof(ArcadeDeterminismTests.Cases), MemberType = typeof(ArcadeDeterminismTests))]
    public void NewTogglesOffReproduce2026_10_17_5(int seed, bool onePlayer, int canisters, bool autoFire)
    {
        Assert.Equal(EnhancedOption.WarpIn | EnhancedOption.EnemyAimError | EnhancedOption.SlowerRamp | EnhancedOption.SlowerTowing
            | EnhancedOption.RandomWarpIn | EnhancedOption.WarpKills, Release5);
        var cfg = new GameConfig(canisters, autoFire, PlayMode.Enhanced, Release5);
        Assert.Equal(EnhancedAt20261017_5[seed], ArcadeDeterminismTests.Run(seed, onePlayer, cfg));
        // the ships picked do not matter while SHIP SELECT is off
        Assert.Equal(EnhancedAt20261017_5[seed], ArcadeDeterminismTests.Run(seed, onePlayer, cfg with { Ship1 = Ships.Classic2, Ship2 = Ships.Classic1 }));
    }
}
