namespace RipOff.Core.Shapes
{
    /// <summary>One vector segment. Dim = drawn after an FE move (NOTES §5).</summary>
    public readonly record struct Seg(short X1, short Y1, short X2, short Y2, bool Dim)
    {
        public Seg(int x1, int y1, int x2, int y2, bool dim) : this((short)x1, (short)y1, (short)x2, (short)y2, dim) { }
    }
}
