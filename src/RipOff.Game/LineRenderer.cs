using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RipOff.Core;

namespace RipOff.Game
{
    /// <summary>
    /// Custom line layer (recorded decision: lines drawn ourselves, shaders deferred). Each vector is a 1×1 white
    /// texture stretched into a thin quad by SpriteBatch with additive blending, which gives a simple glow where
    /// lines overlap. Field coordinates (1024×768, y up) are letterboxed into the back buffer at 4:3.
    /// </summary>
    public sealed class LineRenderer : IDisposable
    {
        readonly SpriteBatch _batch;
        readonly Texture2D _pixel;
        public float Thickness = 1.6f;
        public Color BrightColor = new Color(255, 255, 255);
        public Color DimColor = new Color(140, 140, 140); // FE-move segments (NOTES §5). UNCHECKED: real dim level

        public LineRenderer(GraphicsDevice gd)
        {
            _batch = new SpriteBatch(gd);
            _pixel = new Texture2D(gd, 1, 1);
            _pixel.SetData(new[] { Color.White });
        }

        public void Draw(IReadOnlyList<Line> lines, Rectangle target)
        {
            // Letterbox to 4:3.
            float sx = target.Width / (float)Scene.Width, sy = target.Height / (float)Scene.Height;
            float k = Math.Min(sx, sy);
            float ox = target.X + (target.Width - Scene.Width * k) / 2f;
            float oy = target.Y + (target.Height - Scene.Height * k) / 2f;
            float th = Math.Max(1f, Thickness * k);

            _batch.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp);
            foreach (var l in lines)
            {
                var a = new Vector2(ox + l.X1 * k, oy + (Scene.Height - l.Y1) * k); // y up → screen y down
                var b = new Vector2(ox + l.X2 * k, oy + (Scene.Height - l.Y2) * k);
                var d = b - a;
                float len = d.Length();
                float ang = (float)Math.Atan2(d.Y, d.X);
                // Extend by half a thickness on each end so joints close and zero-length lines show as dots.
                var start = a - (len > 0 ? d / len : Vector2.UnitX) * (th / 2);
                _batch.Draw(_pixel, start, null, l.Bright ? BrightColor : DimColor, ang, new Vector2(0, 0.5f),
                            new Vector2(len + th, th), SpriteEffects.None, 0);
            }
            _batch.End();
        }

        public void Dispose() { _batch.Dispose(); _pixel.Dispose(); }
    }
}
