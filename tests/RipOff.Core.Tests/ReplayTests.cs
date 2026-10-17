using RipOff.Core;

namespace RipOff.Core.Tests;

/// <summary>
/// Replays the scripted MAME runs (disasm/trace/scripts/trace.lua, trace2.lua) through the simulation and compares
/// against RAM checkpoints taken from the arcade every 100 frames (Data/*.chk). The scripts' inputs take effect two
/// passes after the frame they are computed on, so the input queue starts with two empty frames.
/// </summary>
public class ReplayTests
{
    const int IdxA8 = 0x50, IdxCan = 0x51, IdxEC = 0x71, IdxF1 = 0x72, IdxF2 = 0x73;

    static Dictionary<int, int[]> Load(string tag)
    {
        var d = new Dictionary<int, int[]>();
        foreach (var l in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Data", tag + ".chk")))
        {
            if (l.StartsWith('#')) continue;
            var p = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            d[int.Parse(p[0])] = p.Skip(1).Select(x => Convert.ToInt32(x, 16)).ToArray();
        }
        return d;
    }

    static int S(int v) { v &= 0xFFF; return v >= 0x800 ? v - 0x1000 : v; }

    /// <summary>Port of the trace scripts' autopilot (trace.lua autopilot/wander, trace2.lua drive).</summary>
    static PlayerInput Drive(GameState st, int frame, int p, string md, bool two, bool alive = false)
    {
        var pi = new PlayerInput();
        var me = st.Players[p];
        if (md == "idle" || ((two || alive) && (me.A & 0x10) == 0)) return pi;
        if (md == "wander")
        {
            long k = ((long)(frame / 40) * 2654435761L + (two ? (p == 0 ? 0x30 : 0x40) : 0)) % 4294967296L;
            pi.Left = ((k >> 3) & 1) == 1; pi.Right = ((k >> 3) & 1) == 0 && ((k >> 5) & 1) == 1; pi.Thrust = ((k >> 7) % 4) != 0;
            return pi;
        }
        int px = S(me.X), py = S(me.Y), tx = 0, ty = 0;
        if (md == "pvp") { var o = st.Players[1 - p]; tx = S(o.X); ty = S(o.Y); }
        else
        {
            double best = double.MaxValue; bool any = false;
            foreach (var e in st.Enemies)
            {
                if ((e.A & 1) != 0 || (e.A & 0x10) == 0) continue;
                int ex = S(e.X), ey = S(e.Y); double d = Math.Max(Math.Abs(ex - px), Math.Abs(ey - py));
                if (d < best) { best = d; tx = ex; ty = ey; any = true; }
            }
            if (!any) return pi;
        }
        double want = Math.Atan2(tx - px, -(ty - py)) * 180 / Math.PI; want = ((want % 360) + 360) % 360;
        double diff = ((want - me.Heading + 540) % 360) - 180;
        if (diff > 4) pi.Left = true; else if (diff < -4) pi.Right = true;
        if ((md == "shoot" || md == "pvp") && Math.Abs(diff) < 10 && frame % 4 < 2) pi.Fire = true;
        if (md == "ram" && Math.Abs(diff) < 20) pi.Thrust = true;
        return pi;
    }

    [Theory]
    [InlineData("idle1", "idle", "idle", false)]
    [InlineData("shoot1", "shoot", "idle", false)]
    [InlineData("ram1", "ram", "idle", false)]
    [InlineData("wander1", "wander", "idle", false)]
    [InlineData("tp_pvp1", "pvp", "idle", true)]
    [InlineData("tp_wander", "wander", "wander", true)]
    [InlineData("tp_shoot2", "idle", "shoot", true)]
    public void MatchesArcadeRam(string tag, string m1, string m2, bool two)
    {
        var chk = Load(tag);
        var pre = chk[201];
        var sim = new Simulation(); var st = sim.State;
        st.CanisterDip = 3;
        for (int i = 0; i < 3; i++) st.Enemies[i].B = pre[0x10 * i + 0xB];
        st.Players[0].B = pre[0x3B]; st.Players[1].B = pre[0x4B];
        st.PassCounter = pre[IdxA8];
        sim.StartGame(!two);
        var q = new Queue<FrameInput>(); q.Enqueue(default); q.Enqueue(default);
        int last = chk.Keys.Max(), checkedFrames = 0;
        for (int f = 202; f <= last; f++)
        {
            sim.Step(q.Dequeue());
            var inp = new FrameInput();
            if (f > 260) { inp.P1 = Drive(st, f, 0, m1, two); if (two) inp.P2 = Drive(st, f, 1, m2, two); }
            q.Enqueue(inp);
            if (!chk.TryGetValue(f, out var w)) continue;
            checkedFrames++;
            for (int i = 0; i < 5; i++)
            {
                var r = st.Record(i); int b = 0x10 * i;
                Assert.True(r.A == w[b + 0xA], $"{tag} f{f} rec{i} A sim={r.A:X} ram={w[b + 0xA]:X}");
                if (EnemyState.IsFree(r.A)) continue;
                Assert.True(S(r.X) == S(w[b]) && S(r.Y) == S(w[b + 1]), $"{tag} f{f} rec{i} pos sim=({r.X},{r.Y}) ram=({S(w[b])},{S(w[b + 1])})");
                Assert.True(r.Heading == w[b + 5] && r.Speed == w[b + 6], $"{tag} f{f} rec{i} H/Sp");
                if (i < 3) Assert.True(S(r.C) == S(w[b + 0xC]) && S(r.D) == S(w[b + 0xD]) && r.B == w[b + 0xB] && r.F == w[b + 0xF], $"{tag} f{f} rec{i} B/C/D/F");
            }
            for (int j = 0; j < 16; j++)
                Assert.True(st.Canisters[j].X2 == w[IdxCan + 2 * j] && st.Canisters[j].Y2 == w[IdxCan + 2 * j + 1], $"{tag} f{f} can{j}");
            Assert.Equal(w[IdxEC], st.WaveCounter);
            Assert.Equal(w[IdxF1], st.Score[0]);
            Assert.Equal(w[IdxF2], st.Score[1]);
            Assert.Equal(w[IdxA8], st.PassCounter);
        }
        Assert.True(checkedFrames > 30);
    }

    /// <summary>
    /// Sound cues against the arcade's command byte RAM[$FC] on every frame (Data/*.fc, run-length encoded):
    /// bit 2 = 0 motor (latched one pass late: written by the wave code after the sound routine), bit 3 = 0 beep,
    /// bit 4 = 1 background off, bits 7-5 = background pitch (NOTES §19.3, §21.4).
    /// </summary>
    [Theory]
    [InlineData("sl_base", "shoot", "idle", false, true)]   // trace2.lua one-player: 6 waves, BONUS screen
    [InlineData("shoot1", "shoot", "idle", false, false)]
    [InlineData("ram1", "ram", "idle", false, false)]
    [InlineData("tp_wander", "wander", "wander", true, false)]
    [InlineData("tp_pvp1", "pvp", "idle", true, false)]
    public void SoundCommandByte(string tag, string m1, string m2, bool two, bool alive)
    {
        int[] pre = Array.Empty<int>(); var changes = new SortedDictionary<int, int>(); int end = 0;
        foreach (var l in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Data", tag + ".fc")))
        {
            if (l.StartsWith('#')) continue;
            var p = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p[0] == "pre") pre = p.Skip(1).Select(x => Convert.ToInt32(x, 16)).ToArray();
            else if (p[0] == "end") end = int.Parse(p[1]);
            else changes[int.Parse(p[0])] = Convert.ToInt32(p[1], 16);
        }
        int Fc(int f) { int v = 0; foreach (var kv in changes) { if (kv.Key > f) break; v = kv.Value; } return v; }

        var sim = new Simulation(); var st = sim.State;
        st.CanisterDip = 3;
        for (int i = 0; i < 3; i++) st.Enemies[i].B = pre[i];
        st.Players[0].B = pre[3]; st.Players[1].B = pre[4];
        st.PassCounter = pre[5];
        sim.StartGame(!two);
        var q = new Queue<FrameInput>(); q.Enqueue(default); q.Enqueue(default);
        int motorOn = 0, beepOn = 0, pitches = 0, seenPitch = 0;
        for (int f = 202; f <= end; f++)
        {
            sim.Step(q.Dequeue());
            var inp = new FrameInput();
            if (f > 260) { inp.P1 = Drive(st, f, 0, m1, two, alive); if (two) inp.P2 = Drive(st, f, 1, m2, two); }
            q.Enqueue(inp);
            int fc = Fc(f), fcPrev = Fc(f - 1); var snd = st.Sound;
            Assert.True(snd.Motor == (((fcPrev >> 2) & 1) == 0), $"{tag} f{f} motor sim={snd.Motor} fc(prev)={fcPrev:X2}");
            Assert.True(snd.Beep == (((fc >> 3) & 1) == 0), $"{tag} f{f} beep sim={snd.Beep} fc={fc:X2}");
            Assert.True(snd.BackgroundMuted == (((fc >> 4) & 1) == 1), $"{tag} f{f} bg off sim={snd.BackgroundMuted} fc={fc:X2}");
            if (!snd.BackgroundMuted)
            {
                Assert.True(((snd.BackgroundLevel >> 5) & 7) == ((fc >> 5) & 7), $"{tag} f{f} pitch sim={snd.BackgroundLevel:X2} fc={fc:X2}");
                seenPitch |= 1 << ((fc >> 5) & 7);
            }
            if (snd.Motor) motorOn++;
            if (snd.Beep) beepOn++;
        }
        for (int b = 0; b < 8; b++) if ((seenPitch >> b & 1) != 0) pitches++;
        Assert.True(motorOn > 100, $"{tag}: motor frames {motorOn}");
        Assert.True(pitches >= 2, $"{tag}: pitch values {pitches}");
        if (tag == "sl_base") Assert.True(beepOn > 40, $"beep frames {beepOn}");
    }
}
