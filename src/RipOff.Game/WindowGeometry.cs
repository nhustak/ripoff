using System;
using System.Collections.Generic;

namespace RipOff.Game
{
    /// <summary>A rectangle in desktop pixels (window client area or a display's usable bounds).</summary>
    public readonly record struct PixelRect(int X, int Y, int Width, int Height)
    {
        public int Right => X + Width;
        public int Bottom => Y + Height;
        public bool IsEmpty => Width <= 0 || Height <= 0;

        public PixelRect Intersect(PixelRect o)
        {
            int x = Math.Max(X, o.X), y = Math.Max(Y, o.Y), r = Math.Min(Right, o.Right), b = Math.Min(Bottom, o.Bottom);
            return r > x && b > y ? new PixelRect(x, y, r - x, b - y) : default;
        }
    }

    /// <summary>
    /// Where the windowed game goes at start-up and when leaving fullscreen (pure, so it can be unit tested).
    /// The saved rect (settings.json WindowX/Y/Width/Height, client area) is used if it is sane and still on a display:
    /// - size at least <see cref="MinWidth"/>×<see cref="MinHeight"/> and at most <see cref="MaxSize"/> on each side;
    /// - the display it overlaps most must show at least <see cref="MinVisible"/> px of its width and its top
    ///   <see cref="GrabBand"/> px (where the title bar is), so it can still be grabbed and moved.
    /// A sane rect is then shrunk to fit that display and nudged fully onto it. Anything else (no saved geometry, a
    /// missing monitor, a changed resolution, garbage) gives the default: 1024×768 (scaled down to fit if the display
    /// is smaller), centered on the first (primary) display.
    /// </summary>
    public static class WindowGeometry
    {
        public const int DefaultWidth = 1024, DefaultHeight = 768;
        public const int MinWidth = 320, MinHeight = 240;
        public const int MaxSize = 16384;
        public const int MinVisible = 100, GrabBand = 32;

        /// <summary>Resolved geometry; <see cref="Position"/> is null when no display is known (let the OS place it).</summary>
        public readonly record struct Placement(int Width, int Height, (int X, int Y)? Position, bool FromSaved);

        public static bool IsSaneSize(int w, int h) => w >= MinWidth && h >= MinHeight && w <= MaxSize && h <= MaxSize;

        public static Placement Resolve(PixelRect? saved, IReadOnlyList<PixelRect>? displays)
        {
            var ds = new List<PixelRect>();
            if (displays != null) foreach (var d in displays) if (!d.IsEmpty) ds.Add(d);

            if (ds.Count == 0)
            {
                // No display information (SDL not reachable): trust a sane saved size, let the OS position the window.
                if (saved is { } s0 && IsSaneSize(s0.Width, s0.Height)) return new Placement(s0.Width, s0.Height, null, true);
                return new Placement(DefaultWidth, DefaultHeight, null, false);
            }

            if (saved is { } s && IsSaneSize(s.Width, s.Height))
            {
                PixelRect best = default; long bestArea = 0;
                foreach (var d in ds)
                {
                    var i = s.Intersect(d);
                    long a = (long)i.Width * i.Height;
                    if (a > bestArea) { bestArea = a; best = d; }
                }
                if (bestArea > 0)
                {
                    var band = new PixelRect(s.X, s.Y, s.Width, Math.Min(GrabBand, s.Height)).Intersect(best);
                    if (band.Width >= Math.Min(MinVisible, s.Width) && band.Height >= Math.Min(GrabBand, s.Height))
                    {
                        int w = Math.Min(s.Width, best.Width), h = Math.Min(s.Height, best.Height);
                        int x = Math.Clamp(s.X, best.X, best.Right - w), y = Math.Clamp(s.Y, best.Y, best.Bottom - h);
                        return new Placement(w, h, (x, y), true);
                    }
                }
            }
            return Default(ds[0]);
        }

        /// <summary>1024×768 centered on <paramref name="d"/>; on a smaller display the largest 4:3 size within 90 % of it.</summary>
        public static Placement Default(PixelRect d)
        {
            int w = DefaultWidth, h = DefaultHeight;
            if (w > d.Width * 9 / 10 || h > d.Height * 9 / 10)
            {
                double k = Math.Min(d.Width * 0.9 / DefaultWidth, d.Height * 0.9 / DefaultHeight);
                w = Math.Max(MinWidth, (int)(DefaultWidth * k)); h = Math.Max(MinHeight, (int)(DefaultHeight * k));
            }
            return new Placement(w, h, (d.X + (d.Width - w) / 2, d.Y + (d.Height - h) / 2), false);
        }
    }
}
