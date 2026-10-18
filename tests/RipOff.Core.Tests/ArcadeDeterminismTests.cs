using RipOff.Core;

namespace RipOff.Core.Tests;

/// <summary>
/// Arcade-mode lock: fixed seeds + pseudo-random inputs must give exactly the StateHash checksums recorded on
/// main at tag arcade-v1.0 (f45583a), before game modes existed. Any change that alters Arcade behaviour (or the
/// hash of an Arcade state) fails here. Enhanced mode may diverge freely; it is not pinned.
/// </summary>
public class ArcadeDeterminismTests
{
    public const int Frames = 6000, Every = 500;

    /// <summary>Same start as LockstepSession.CreateSimulation: per-game pass counter and five RNG words.</summary>
    public static Simulation Create(int seed, bool onePlayer, GameConfig cfg)
    {
        uint x = (uint)seed * 2654435761u + 0x9E3779B9u;
        int Next() { x ^= x << 13; x ^= x >> 17; x ^= x << 5; return (int)(x & 0xFFF); }
        var sim = new Simulation(); var s = sim.State;
        for (int i = 0; i < 5; i++) { int v = Next(); s.Record(i).B = v == 0 ? 0xF20 : v; }
        s.PassCounter = Next() & 0xFF;
        sim.StartGame(onePlayer, cfg);
        return sim;
    }

    /// <summary>Runs <see cref="Frames"/> passes of scripted input (held for random spans, both players) and returns the
    /// hash every <see cref="Every"/> passes.</summary>
    public static uint[] Run(int seed, bool onePlayer, GameConfig cfg)
    {
        var sim = Create(seed, onePlayer, cfg);
        uint r = (uint)seed * 747796405u + 2891336453u;
        uint Rand() { r ^= r << 13; r ^= r >> 17; r ^= r << 5; return r; }
        var held = new byte[2]; var left = new int[2];
        var hashes = new List<uint>();
        for (int f = 1; f <= Frames; f++)
        {
            for (int p = 0; p < 2; p++) if (--left[p] <= 0) { held[p] = (byte)(Rand() & 0xF); left[p] = 1 + (int)(Rand() % 40); }
            PlayerInput In(byte b) => new() { Left = (b & 1) != 0, Right = (b & 2) != 0 && (b & 1) == 0, Thrust = (b & 4) != 0, Fire = (b & 8) != 0 && (f & 1) == 0 };
            sim.Step(new FrameInput { P1 = In(held[0]), P2 = In(held[1]) });
            if (f % Every == 0) hashes.Add(StateHash.Compute(sim.State));
        }
        return hashes.ToArray();
    }

    public static IEnumerable<object[]> Cases() => new[]
    {
        new object[] { 1, true, 8, false },
        new object[] { 2, false, 8, false },
        new object[] { 3, false, 4, true },
        new object[] { 4, true, 16, true },
        new object[] { 5, false, 12, false },
    };

    /// <summary>Recorded on f45583a (arcade-v1.0) with the code before PlayMode/GameRules existed.</summary>
    static readonly Dictionary<int, uint[]> Golden = new()
    {
        [1] = new uint[] { 0x0F574AC5u, 0xEF3814DAu, 0x4A08EF65u, 0xB6AD17ACu, 0xF407B9F0u, 0x3582DBF3u, 0xF6342655u, 0x0B4C505Eu, 0x9C0631E0u, 0x614B09F2u, 0xF3D42BECu, 0xD5B098C6u },
        [2] = new uint[] { 0x69EAF56Eu, 0xA77BF62Du, 0x6B1173DBu, 0x095A9146u, 0xA19A7DFAu, 0xC8BDE943u, 0x78B6E025u, 0x1660C559u, 0xF674816Eu, 0xBE7B2683u, 0x3273DDD1u, 0x28510467u },
        [3] = new uint[] { 0x92618A1Du, 0xD3FFB77Cu, 0xBB5F5540u, 0x7931C2FBu, 0xDA15EAECu, 0x1A3649C6u, 0x042C4CC4u, 0x99F81A13u, 0xA29BEBA9u, 0xB1B79F4Fu, 0xBC42D5FDu, 0x3E2436EBu },
        [4] = new uint[] { 0x67DCBEC7u, 0xEB4DA5A2u, 0x79D9F67Bu, 0x5A4EC182u, 0x81158835u, 0xE72C320Eu, 0xC8F070B2u, 0x3F19A005u, 0x0CA422C3u, 0x7564127Du, 0x257FE70Fu, 0x78682C03u },
        [5] = new uint[] { 0x40433494u, 0x891D359Eu, 0xCF60A4D1u, 0x6D51D967u, 0xA4F74B2Au, 0x2B73CD20u, 0x1E658943u, 0x2DD45C96u, 0x6FEBD1A6u, 0x18244556u, 0xC1467326u, 0x4CB79609u },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ArcadeMatchesTheChecksumsRecordedBeforeGameModes(int seed, bool onePlayer, int canisters, bool autoFire)
    {
        Assert.Equal(Golden[seed], Run(seed, onePlayer, new GameConfig(canisters, autoFire)));                     // default = Arcade
        Assert.Equal(Golden[seed], Run(seed, onePlayer, new GameConfig(canisters, autoFire, PlayMode.Arcade)));    // explicit
    }

    [Fact]
    public void ArcadeRulesAreTheArcadeConstants()
    {
        var a = GameRules.Arcade;
        Assert.Equal(PlayMode.Arcade, a.Mode);
        Assert.Equal(GameData.AutoFireInterval, a.AutoFireInterval);
        Assert.Equal(GameData.RespawnDelay, a.RespawnDelay);
        Assert.Equal(GameData.RespawnSpeed, a.RespawnSpeed);
        Assert.Same(a, GameRules.For(PlayMode.Arcade));
        Assert.Same(GameRules.Enhanced, GameRules.For(PlayMode.Enhanced, EnhancedOption.All));
    }

    [Fact]
    public void EnhancedIsDeterministicAndDiverges()
    {
        var a = Run(2, false, new GameConfig(8, Mode: PlayMode.Enhanced));
        var b = Run(2, false, new GameConfig(8, Mode: PlayMode.Enhanced));
        Assert.Equal(a, b);                                   // same seed + inputs → same checksums
        Assert.NotEqual(Golden[2], a);                        // and not the Arcade game
        Assert.NotEqual(a, Run(3, false, new GameConfig(8, Mode: PlayMode.Enhanced)));
    }
}
