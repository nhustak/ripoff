using System; using System.IO; using System.Linq; using System.Collections.Generic; using RipOff.Core;

// Difficulty-escalation measurement, NOTES §26. The "hunt" bot here is a line-for-line port of disasm/trace/scripts/
// trace3.lua's hunt(); both read the same RAM words (frame-end values) and decide with the same double arithmetic.
static class W
{
    public static int S(int v) { v &= 0xFFF; return v >= 0x800 ? v - 0x1000 : v; }

    /// <summary>Frame-end RAM words the bot and the wave table use, built from the remake state.</summary>
    public static int[] Ram(GameState s)
    {
        var w = new int[256];
        for (int i = 0; i < 5; i++)
        {
            var r = i < 3 ? s.Enemies[i] : s.Players[i - 3]; int b = 0x10 * i;
            w[b] = r.X & 0xFFF; w[b + 1] = r.Y & 0xFFF; w[b + 3] = r.Word3; w[b + 5] = r.Heading; w[b + 6] = r.Speed;
            w[b + 0xA] = r.A; w[b + 0xB] = r.B; w[b + 0xC] = r.C & 0xFFF; w[b + 0xD] = r.D & 0xFFF; w[b + 0xE] = r.TurnStep; w[b + 0xF] = r.F;
            if (i < 3) w[b + 2] = 0xB0 + 2 * r.Word2;
        }
        for (int j = 0; j < 16; j++) { w[0xB0 + 2 * j] = s.Canisters[j].X2 & 0xFFF; w[0xB1 + 2 * j] = s.Canisters[j].Y2 & 0xFFF; }
        w[0xA8] = s.PassCounter; w[0xEC] = s.WaveCounter & 0xFFF; w[0xF1] = s.Score[0]; w[0xF5] = s.EnemyType; w[0xFB] = s.Level;
        w[0xF8] = s.KillsThisWave; w[0xF9] = s.TheftsThisWave; w[0xFE] = s.ScoreThousands[0];
        return w;
    }

    /// <summary>The hunt bot (trace3.lua hunt): pick an enemy (towing / at-canister first), lead it by its velocity,
    /// turn onto it, fire when within 6 degrees (fire toggles every 2 frames: one shot per press). Never thrusts.</summary>
    public static double FireTol = double.Parse(Environment.GetEnvironmentVariable("FT") ?? "8"), Pri = double.Parse(Environment.GetEnvironmentVariable("PRI") ?? "600"), Shot = double.Parse(Environment.GetEnvironmentVariable("SHOT") ?? "20"), IdleT = double.Parse(Environment.GetEnvironmentVariable("IDLET") ?? "99");
    public static bool Lead = (Environment.GetEnvironmentVariable("LEAD") ?? "1") != "0";
    public static PlayerInput Hunt(int[] w, int frame)
    {
        var pi = new PlayerInput();
        int pa = w[0x3A];
        if ((pa & 1) != 0 || (pa & 0x10) == 0 || w[0xF5] >= IdleT) return pi;
        double px = S(w[0x30]), py = S(w[0x31]);
        double best = 1e9, tx = 0, ty = 0; bool any = false;
        for (int b = 0; b <= 0x20; b += 0x10)
        {
            int a = w[b + 0xA];
            if ((a & 1) != 0 || (a & 0x10) == 0) continue;
            double ex = S(w[b]), ey = S(w[b + 1]);
            double d = Math.Max(Math.Abs(ex - px), Math.Abs(ey - py));
            double score = d - (((a & 2) != 0 || a == 0x14) ? Pri : 0);
            if (score < best)
            {
                best = score; any = true;
                double h = w[b + 5] * Math.PI / 180, sp = w[b + 6], t = d / Shot;
                if (!Lead) t = 0;
                tx = ex + sp * Math.Sin(h) * t; ty = ey - sp * Math.Cos(h) * t;
            }
        }
        if (!any) return pi;
        double want = Math.Atan2(tx - px, -(ty - py)) * 180 / Math.PI; want = ((want % 360) + 360) % 360;
        double diff = ((want - w[0x35] + 540) % 360) - 180;
        if (diff > 3) pi.Left = true; else if (diff < -3) pi.Right = true;
        if (Math.Abs(diff) < FireTol && frame % 4 < 2) pi.Fire = true;
        return pi;
    }

    public static Dictionary<int, int[]> Load(string path)
    {
        var R = new Dictionary<int, int[]>();
        foreach (var l in File.ReadLines(path))
        {
            var p = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 257) continue;
            R[int.Parse(p[0])] = p.Skip(1).Select(x => Convert.ToInt32(x, 16)).ToArray();
        }
        return R;
    }
}

/// <summary>Per-wave metrics from a sequence of frame-end RAM images (the same code reads MAME and the remake).</summary>
sealed class WaveTable
{
    public sealed class Wave
    {
        public int N, Start, End = -1, NextStart = -1, Enemies, Type, Level, MaxSpeed, Turn, FirstGrab = -1, FirstTheft = -1;
        public int Kills, Thefts; public string Speeds = "";
    }
    public readonly List<Wave> Waves = new();
    int[]? prev; Wave? cur;
    public int LastFrame, PassGaps; int prevA8 = -1;
    static bool Free(int a) => (a & 1) != 0;

    public void Add(int f, int[] w)
    {
        if (prevA8 >= 0 && ((prevA8 + 1) & 0xFFF) != w[0xA8]) PassGaps++;
        prevA8 = w[0xA8];
        LastFrame = f;
        if (prev != null)
        {
            bool spawned = false;
            for (int b = 0; b <= 0x20; b += 0x10) if (Free(prev[b + 0xA]) && w[b + 0xA] == 0x1C && w[0xEC] == 0) spawned = true;
            if (spawned && f >= 202)
            {
                if (cur != null) cur.NextStart = f;
                cur = new Wave { N = Waves.Count + 1, Start = f, Type = w[0xF5], Level = w[0xFB] };
                var sp = new List<string>();
                for (int b = 0; b <= 0x20; b += 0x10) if (!Free(w[b + 0xA])) { cur.Enemies++; cur.MaxSpeed = w[b + 3]; cur.Turn = w[b + 0xE]; sp.Add($"{w[b + 3]}/{w[b + 0xE]}"); }
                cur.Speeds = string.Join(",", sp);
                Waves.Add(cur);
            }
            if (cur != null && cur.End < 0)
            {
                for (int b = 0; b <= 0x20; b += 0x10)
                {
                    if (cur.FirstGrab < 0 && prev[b + 0xA] != 0x14 && w[b + 0xA] == 0x14) cur.FirstGrab = f;
                }
                if (cur.FirstTheft < 0 && f > cur.Start && w[0xF9] > prev[0xF9]) cur.FirstTheft = f;
                bool allFree = Free(w[0xA]) && Free(w[0x1A]) && Free(w[0x2A]);
                // Wave-end bookkeeping runs in the pass where EC becomes 2 (lst:486-490): read F8/F9 just before it.
                if (allFree && w[0xEC] == 2 && prev[0xEC] == 0) { cur.End = f; cur.Kills = prev[0xF8]; cur.Thefts = prev[0xF9]; }
            }
        }
        prev = w;
    }

    public void Print(string title)
    {
        Console.WriteLine($"# {title}: frames to {LastFrame}, A8 gaps (passes not +1 per frame) {PassGaps}");
        Console.WriteLine("# wave start_f type level enemies maxspeed/turn(per enemy) start->first_grab start->first_theft wave_len(frames) kills(F8 incl. handicap) thefts(F9) gap_to_next");
        foreach (var v in Waves)
            Console.WriteLine($"{v.N} {v.Start} T{v.Type} L{v.Level} {v.Enemies} {v.Speeds} {(v.FirstGrab < 0 ? "-" : (v.FirstGrab - v.Start).ToString())} {(v.FirstTheft < 0 ? "-" : (v.FirstTheft - v.Start).ToString())} {(v.End < 0 ? "-" : (v.End - v.Start).ToString())} {v.Kills} {v.Thefts} {(v.NextStart < 0 || v.End < 0 ? "-" : (v.NextStart - v.End).ToString())}");
    }
}

static class P
{
    static Simulation NewSim(int[]? pre)
    {
        var sim = new Simulation(); var st = sim.State;
        st.CanisterDip = 3; // 8 canisters, the arcade default DIP
        if (pre != null)
        {
            for (int i = 0; i < 3; i++) st.Enemies[i].B = pre[0x10 * i + 0xB];
            st.Players[0].B = pre[0x3B]; st.Players[1].B = pre[0x4B]; st.PassCounter = pre[0xA8];
        }
        return sim;
    }

    static int Main(string[] a)
    {
        string cmd = a[0];
        if (cmd == "mame")
        {
            var R = W.Load(a[1]); var t = new WaveTable();
            foreach (var f in R.Keys.OrderBy(k => k)) t.Add(f, R[f]);
            t.Print("MAME " + a[1]); return 0;
        }
        if (cmd == "stats") return Stats.Run(a);
        Dictionary<int, int[]>? log = cmd == "lock" ? W.Load(a[1]) : null;
        int frames = cmd == "lock" ? log!.Keys.Max() : int.Parse(a[1]);
        // Seeds: the log's frame 201 (lock), or a 2nd argument log (sim with arcade seeds); else power-on defaults.
        int[]? pre = log != null ? log[201] : (a.Length > 2 && File.Exists(a[2]) ? W.Load(a[2])[201] : null);
        var sim = NewSim(pre); var st = sim.State;
        if (pre == null) st.PassCounter = 201 & 0xFFF; // frame-201 phase of a power-on run (UNCHECKED without a log)
        sim.StartGame(true);
        var q = new Queue<FrameInput>(); q.Enqueue(default); q.Enqueue(default);
        var table = new WaveTable(); int firstDiff = -1; var cmp = new[] { 0xF5, 0xFB, 0xF8, 0xF9, 0xEC, 0xF1, 0xA8 };
        for (int f = 202; f <= frames; f++)
        {
            sim.Step(q.Dequeue());
            var w = W.Ram(st);
            if (st.Mode != GameMode.Playing) { Console.WriteLine($"# remake game over at f{f}"); break; }
            q.Enqueue(f > 260 ? new FrameInput { P1 = W.Hunt(w, f) } : default);
            table.Add(f, w);
            if (log != null && firstDiff < 0 && log.TryGetValue(f, out var m))
            {
                var d = new List<string>();
                foreach (int x in cmp) if (w[x] != m[x]) d.Add($"${x:X2} remake={w[x]:X} mame={m[x]:X}");
                for (int i = 0; i < 5; i++)
                {
                    int b = 0x10 * i; if (w[b + 0xA] != m[b + 0xA]) d.Add($"rec{b:X2}.A remake={w[b + 0xA]:X} mame={m[b + 0xA]:X}");
                    if ((w[b + 0xA] & 1) == 0) foreach (int o in new[] { 0, 1, 3, 5, 6 }) if (W.S(w[b + o]) != W.S(m[b + o])) d.Add($"rec{b:X2}.w{o:X} remake={W.S(w[b + o])} mame={W.S(m[b + o])}");
                }
                for (int j = 0xB0; j < 0xD0; j++) if (w[j] != m[j]) { d.Add($"can${j:X2} remake={w[j]:X} mame={m[j]:X}"); break; }
                if (d.Count > 0) { firstDiff = f; Console.WriteLine($"# FIRST DIFF f{f}: " + string.Join("; ", d.Take(10))); }
            }
        }
        if (log != null && firstDiff < 0) Console.WriteLine($"# lockstep: remake == MAME on every compared word, f202-f{frames}");
        table.Print(cmd == "lock" ? "remake (lockstep seeds from " + a[1] + ")" : "remake sim");
        return 0;
    }
}

/// <summary>
/// Arcade vs Enhanced (NOTES §27): <c>Waves stats [seeds] [maxMinutes]</c> plays one-player games with the hunt bot
/// (FT/LEAD/PRI env as above) from per-seed start words (as LockstepSession.DefaultConfig) until game over or the
/// time cap, in both modes, and prints one row per game plus means.
/// </summary>
static class Stats
{
    public static int Run(string[] a)
    {
        int seeds = a.Length > 1 ? int.Parse(a[1]) : 10;
        double maxMin = a.Length > 2 ? double.Parse(a[2]) : 30;
        int cap = (int)(maxMin * 60 * 38);
        Console.WriteLine($"# bot FT={W.FireTol} PRI={W.Pri} SHOT={W.Shot} LEAD={(W.Lead ? 1 : 0)}; 1 player, 8 canisters, cap {maxMin} min");
        Console.WriteLine("# mode seed length_s over waves type level tL2_s tL3_s tL4_s deaths fire_events hits hit% deaths/min kills");
        foreach (var mode in new[] { PlayMode.Arcade, PlayMode.Enhanced })
        {
            var rows = new List<double[]>();
            for (int seed = 1; seed <= seeds; seed++)
            {
                uint x = (uint)seed * 2654435761u + 0x9E3779B9u;
                int Next() { x ^= x << 13; x ^= x >> 17; x ^= x << 5; return (int)(x & 0xFFF); }
                var sim = new Simulation(); var st = sim.State;
                for (int i = 0; i < 5; i++) { int v = Next(); st.Record(i).B = v == 0 ? 0xF20 : v; }
                st.PassCounter = Next() & 0xFF;
                sim.StartGame(true, new GameConfig(8, false, mode));
                var q = new Queue<FrameInput>(); q.Enqueue(default); q.Enqueue(default);
                double[] lv = { -1, -1, -1, -1, -1 }; int waves = 0, deaths = 0, kills = 0, prevWc = 0; bool wasAlive = false; int f = 0; bool over = false;
                int prevKills = 0;
                for (f = 1; f <= cap; f++)
                {
                    sim.Step(q.Dequeue());
                    if (st.Mode != GameMode.Playing) { over = true; break; }
                    var w = W.Ram(st);
                    q.Enqueue(f > 58 ? new FrameInput { P1 = W.Hunt(w, f) } : default);
                    if (st.Level < lv.Length && lv[st.Level] < 0) lv[st.Level] = f / 38.0;
                    if (prevWc != 0 && st.WaveCounter == 0) waves++;
                    prevWc = st.WaveCounter;
                    bool alive = EnemyState.IsAlive(st.Players[0].A);
                    if (wasAlive && !alive) deaths++;
                    wasAlive = alive;
                    int k = 0; foreach (var e in st.Enemies) if (EnemyState.IsExploding(e.A)) k++;
                    if (k > prevKills) kills += k - prevKills; prevKills = k;
                }
                double len = f / 38.0, ev = st.EnemyFireEvents, hits = st.EnemyFireHits;
                var row = new[] { len, over ? 1 : 0, waves, st.EnemyType, st.Level, lv[2], lv[3], lv[4], deaths, ev, hits, ev > 0 ? 100 * hits / ev : 0, deaths / (len / 60), kills };
                rows.Add(row);
                Console.WriteLine($"{mode} {seed} {len:F0} {(over ? "over" : "CAP")} {waves} T{st.EnemyType} L{st.Level} {T(lv[2])} {T(lv[3])} {T(lv[4])} {deaths} {ev} {hits} {row[11]:F0} {row[12]:F2} {kills}");
            }
            double M(int c) => rows.Average(r => r[c]);
            string ML(int c) { var v = rows.Where(r => r[c] >= 0).Select(r => r[c]).ToList(); return v.Count == 0 ? "-" : $"{v.Average():F0}({v.Count}/{rows.Count})"; }
            var lens = rows.Select(r => r[0]).OrderBy(v => v).ToList();
            double med = lens.Count % 2 == 1 ? lens[lens.Count / 2] : (lens[lens.Count / 2 - 1] + lens[lens.Count / 2]) / 2;
            Console.WriteLine($"MEAN {mode}: median length {med:F0} s, mean length {M(0):F0} s, ended {rows.Count(r => r[1] == 1)}/{rows.Count}, waves {M(2):F1}, level {M(4):F1}, tL2 {ML(5)} tL3 {ML(6)} tL4 {ML(7)}, deaths {M(8):F1}, fire events {M(9):F1}, hit% {rows.Sum(r => r[10]) * 100 / Math.Max(1, rows.Sum(r => r[9])):F0}, deaths/min {M(12):F2}, kills {M(13):F1}");
        }
        return 0;
    }
    static string T(double v) => v < 0 ? "-" : v.ToString("F0");
}
