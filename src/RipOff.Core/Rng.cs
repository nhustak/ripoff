namespace RipOff.Core
{
    /// <summary>
    /// The pseudo-random step at $11B8 (lst:4110-4149, NOTES §18.6, GAME_RULES §5):
    /// x ← ((x &lt;&lt; 1) &amp; $FFF) | (bit10(x) XOR bit8(x)). Seed $387 for record $00
    /// (lst:59-61). Period 2047. Callers use it once or twice per event.
    /// </summary>
    public static class Rng
    {
        public const int SeedRecord0 = 0x387; // lst:59-61 ($0034-$0037)
        public const int SeedRecord1 = 0x555; // lst:63-65 ($0039-$003C)
        public const int SeedOthers = 0xF20;  // lst:67-73 ($003E-$0045): records $20, $30, $40

        public static int Step(int x)
        {
            int fb = ((x >> 10) ^ (x >> 8)) & 1;
            return ((x << 1) & 0xFFF) | fb;
        }
    }
}
