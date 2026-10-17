using RipOff.Core;

namespace RipOff.Core.Tests;

/// <summary>
/// Difficulty escalation against the arcade (NOTES §26). The MAME runs in Data/*.esc used the closed-loop "hunt" bot
/// of disasm/trace/scripts/trace3.lua; the same bot (ported below, as in tools/remake/Waves) drives the simulation from
/// the run's frame-201 seeds, and every 50 frames the wave state must equal the arcade's RAM: enemy type RAM[$F5],
/// level RAM[$FB], kills RAM[$F8], thefts RAM[$F9], the wave gap counter RAM[$EC], score, pass counter, the three enemy
/// states and enemy 0's max speed / turn step. Game over must fall on the same frame.
/// </summary>
public class EscalationTests
{
    static int S(int v) { v &= 0xFFF; return v >= 0x800 ? v - 0x1000 : v; }

    /// <summary>trace3.lua hunt(), reading the remake state as frame-end RAM words.</summary>
    static PlayerInput Hunt(GameState s, int frame, double ft, double pri, double shot, int idleT)
    {
        var pi = new PlayerInput();
        var me = s.Players[0];
        if ((me.A & 1) != 0 || (me.A & 0x10) == 0 || s.EnemyType >= idleT) return pi;
        double px = S(me.X), py = S(me.Y);
        double best = 1e9, tx = 0, ty = 0; bool any = false;
        foreach (var e in s.Enemies)
        {
            int a = e.A;
            if ((a & 1) != 0 || (a & 0x10) == 0) continue;
            double ex = S(e.X), ey = S(e.Y);
            double d = Math.Max(Math.Abs(ex - px), Math.Abs(ey - py));
            double score = d - (((a & 2) != 0 || a == 0x14) ? pri : 0);
            if (score < best)
            {
                best = score; any = true;
                double h = e.Heading * Math.PI / 180, sp = e.Speed, t = d / shot;
                tx = ex + sp * Math.Sin(h) * t; ty = ey - sp * Math.Cos(h) * t;
            }
        }
        if (!any) return pi;
        double want = Math.Atan2(tx - px, -(ty - py)) * 180 / Math.PI; want = ((want % 360) + 360) % 360;
        double diff = ((want - me.Heading + 540) % 360) - 180;
        if (diff > 3) pi.Left = true; else if (diff < -3) pi.Right = true;
        if (Math.Abs(diff) < ft && frame % 4 < 2) pi.Fire = true;
        return pi;
    }

    [Theory]
    [InlineData("w3_hunt", 99, 28, 4)]  // 28 waves, reaches level 4 (types 1-6 three times)
    [InlineData("w3_back", 3, 9, 1)]    // bot idle at type ≥ 3: three thefts → type steps back 3 → 2, four times
    public void WaveProgressionMatchesArcade(string tag, int idleT, int waves, int maxLevel)
    {
        int[] pre = Array.Empty<int>(); var chk = new Dictionary<int, int[]>(); int end = 0;
        foreach (var l in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Data", tag + ".esc")))
        {
            if (l.StartsWith('#')) continue;
            var p = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p[0] == "pre") pre = p.Skip(1).Select(x => Convert.ToInt32(x, 16)).ToArray();
            else if (p[0] == "c") chk[int.Parse(p[1])] = p.Skip(2).Select(x => Convert.ToInt32(x, 16)).ToArray();
            else if (p[0] == "end") end = int.Parse(p[1]);
        }
        var sim = new Simulation(); var st = sim.State;
        st.CanisterDip = 3;
        st.PassCounter = pre[0];
        for (int i = 0; i < 3; i++) st.Enemies[i].B = pre[1 + i];
        st.Players[0].B = pre[4]; st.Players[1].B = pre[5];
        sim.StartGame(true);
        var q = new Queue<FrameInput>(); q.Enqueue(default); q.Enqueue(default);
        int spawns = 0, topLevel = 1, gameOver = -1; bool wasFree = true;
        for (int f = 202; f <= end && gameOver < 0; f++)
        {
            sim.Step(q.Dequeue());
            if (st.Mode != GameMode.Playing) { gameOver = f; break; }
            q.Enqueue(f > 260 ? new FrameInput { P1 = Hunt(st, f, 8, 600, 20, idleT) } : default);
            bool free = st.Enemies.All(e => EnemyState.IsFree(e.A));
            if (wasFree && !free && st.WaveCounter == 0) spawns++;
            wasFree = free; topLevel = Math.Max(topLevel, st.Level);
            if (!chk.TryGetValue(f, out var w)) continue;
            int[] mine = { st.EnemyType, st.Level, st.KillsThisWave, st.TheftsThisWave, st.WaveCounter & 0xFFF, st.Score[0],
                           st.ScoreThousands[0], st.PassCounter, st.Enemies[0].A, st.Enemies[1].A, st.Enemies[2].A };
            string[] names = { "type F5", "level FB", "kills F8", "thefts F9", "EC", "score F1", "score FE", "A8", "rec00 A", "rec10 A", "rec20 A" };
            for (int i = 0; i < mine.Length; i++) Assert.True(mine[i] == w[i], $"{tag} f{f} {names[i]} remake={mine[i]:X} arcade={w[i]:X}");
            if (!EnemyState.IsFree(st.Enemies[0].A) && (w[8] & 1) == 0)
                Assert.True(st.Enemies[0].Word3 == w[11] && st.Enemies[0].TurnStep == w[12], $"{tag} f{f} rec00 max speed/turn");
        }
        Assert.Equal(end, gameOver);
        Assert.Equal(waves, spawns);
        Assert.Equal(maxLevel, topLevel);
    }

    /// <summary>Max speed / turn step per type and level as observed at every wave spawn of four MAME runs
    /// (trace:excerpts/p9_escalation.txt): v = type + type/2 + level + 1, turn = v/2 + 1 (lst:666-677).</summary>
    [Theory]
    [InlineData(1, new[] { 3, 5, 6, 8, 9, 11 })]
    [InlineData(2, new[] { 4, 6, 7, 9, 10, 12 })]
    [InlineData(3, new[] { 5, 7, 8, 10, 11, 13 })]
    [InlineData(4, new[] { 6, 8, 9 })]
    public void EnemySpeedTableMatchesArcade(int level, int[] speeds)
    {
        for (int t = 1; t <= speeds.Length; t++)
        {
            Assert.Equal(speeds[t - 1], Simulation.EnemyMaxSpeed(t, level));
            Assert.Equal(speeds[t - 1] / 2 + 1, Simulation.EnemyTurnStep(speeds[t - 1]));
        }
    }
}
