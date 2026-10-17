using System.IO.Compression;
using RipOff.Core;

namespace RipOff.Core.Tests;

/// <summary>Headless render check: rasterise the scene lines into a PNG (no GPU, no MonoGame).</summary>
public class RenderTests
{
    [Fact]
    public void RenderSceneToPng()
    {
        var sim = new Simulation(); var st = sim.State;
        sim.StartGame(false);
        for (int f = 0; f < 400; f++)
            sim.Step(new FrameInput { P1 = new PlayerInput { Left = f % 60 < 15, Thrust = f % 5 < 3, Fire = f % 11 == 0 } });
        var lines = Scene.Build(st);
        Assert.NotEmpty(lines);

        const int W = Scene.Width / 2, H = Scene.Height / 2;
        var px = new byte[W * H];
        int lit = 0;
        foreach (var l in lines)
        {
            byte v = l.Bright ? (byte)255 : (byte)128;
            float dx = l.X2 - l.X1, dy = l.Y2 - l.Y1;
            int n = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)) / 2) + 1;
            for (int i = 0; i <= n; i++)
            {
                int x = (int)((l.X1 + dx * i / n) / 2), y = H - 1 - (int)((l.Y1 + dy * i / n) / 2); // y up → PNG rows
                if (x < 0 || x >= W || y < 0 || y >= H) continue;
                if (px[y * W + x] == 0) lit++;
                px[y * W + x] = Math.Max(px[y * W + x], v);
            }
        }
        Assert.True(lit > 200, $"only {lit} pixels lit");

        var dir = Path.Combine(AppContext.BaseDirectory, "render");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "scene.png");
        File.WriteAllBytes(path, Png.Gray8(W, H, px));
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
    }
}

/// <summary>Minimal 8-bit greyscale PNG encoder (zlib via System.IO.Compression).</summary>
public static class Png
{
    public static byte[] Gray8(int w, int h, byte[] px)
    {
        var raw = new byte[(w + 1) * h];
        for (int y = 0; y < h; y++) { raw[y * (w + 1)] = 0; Buffer.BlockCopy(px, y * w, raw, y * (w + 1) + 1, w); }
        byte[] z;
        using (var ms = new MemoryStream())
        {
            using (var zs = new ZLibStream(ms, CompressionLevel.Optimal, true)) zs.Write(raw);
            z = ms.ToArray();
        }
        using var o = new MemoryStream();
        o.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BE(ihdr, 0, w); BE(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = 0;
        Chunk(o, "IHDR", ihdr); Chunk(o, "IDAT", z); Chunk(o, "IEND", Array.Empty<byte>());
        return o.ToArray();
    }

    static void BE(byte[] b, int i, int v) { b[i] = (byte)(v >> 24); b[i + 1] = (byte)(v >> 16); b[i + 2] = (byte)(v >> 8); b[i + 3] = (byte)v; }

    static void Chunk(Stream o, string type, byte[] data)
    {
        var len = new byte[4]; BE(len, 0, data.Length); o.Write(len);
        var td = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) td[i] = (byte)type[i];
        Buffer.BlockCopy(data, 0, td, 4, data.Length);
        o.Write(td);
        var c = new byte[4]; BE(c, 0, (int)Crc(td)); o.Write(c);
    }

    static uint Crc(byte[] d)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in d) { c ^= b; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; }
        return ~c;
    }
}
