using RipOff.Core;
using RipOff.Core.Audio;

namespace RipOff.Core.Tests;

/// <summary>Sound board generators (NOTES §21): lengths, determinism, envelopes, levels, edge logic.</summary>
public class AudioTests
{
    static readonly SoundState Quiet = new() { BackgroundMuted = true };

    static float[] Run(SoundBoard b, Func<int, SoundState> state, int passes)
    {
        var all = new List<float>(); var buf = new float[SoundBoard.MaxPassSamples];
        for (int p = 0; p < passes; p++) { int n = b.RenderPass(state(p), buf); all.AddRange(buf.Take(n)); }
        return all.ToArray();
    }

    static double Rms(float[] x, int from, int len) { double s = 0; for (int i = from; i < from + len; i++) s += x[i] * (double)x[i]; return Math.Sqrt(s / len); }

    static double F0(float[] x, int from, int len)
    {
        // zero crossings (rising) of the mean-removed signal
        double m = 0; for (int i = from; i < from + len; i++) m += x[i]; m /= len;
        int c = 0; int first = -1, last = -1;
        for (int i = from + 1; i < from + len; i++) if (x[i - 1] - m < 0 && x[i] - m >= 0) { c++; if (first < 0) first = i; last = i; }
        return c < 2 ? 0 : (c - 1) * (double)SoundBoard.SampleRate / (last - first);
    }

    [Fact]
    public void PassLengthsAverage38Hz()
    {
        var b = new SoundBoard(); long total = 0;
        for (int i = 0; i < 38 * 10; i++) { int n = b.NextPassLength(); Assert.InRange(n, 1263, 1264); total += n; }
        Assert.Equal(480000, total); // 10 s at 48 kHz, exactly
        Assert.Equal(38.0, GameData.PassesPerSecond); // NextPassLength assumes 38 passes/s
        Assert.True(SoundBoard.MaxPassSamples >= 1264);
        Assert.Throws<ArgumentException>(() => new SoundBoard().RenderPass(Quiet, new float[100]));
    }

    [Fact]
    public void Noise17MatchesMm5837Register()
    {
        // Reference: nld_mm5837.cpp shift, seed 0x1ffff; period of x^17 + x^14 + 1 style LFSR = 2^17 − 1.
        var n = new Noise17(); int s = 0x1FFFF; int ones = 0;
        for (int i = 0; i < 131071; i++)
        {
            s = (s >> 1) | (((s & 1) ^ ((s >> 3) & 1)) << 16);
            int o = n.Next(); Assert.Equal(s & 1, o); ones += o;
        }
        Assert.Equal(0x1FFFF, s);       // maximal length: back at the seed after 2^17 − 1 shifts
        Assert.Equal(65536, ones);
    }

    [Fact]
    public void DeterministicAndBounded()
    {
        SoundState St(int p) => new()
        {
            Explosions = p % 13 == 0 ? 1 : 0, Laser = p % 9 < 4, Torpedo = p % 17 < 9, Motor = p > 50,
            Beep = (p & 2) == 0 && p > 200, BackgroundMuted = p < 20, BackgroundLevel = (p * 7) & 0xFF,
        };
        var a = Run(new SoundBoard(), St, 400); var b = Run(new SoundBoard(), St, 400);
        Assert.Equal(a, b);
        Assert.All(a, v => { Assert.False(float.IsNaN(v) || float.IsInfinity(v)); Assert.True(Math.Abs(v) < 1f); });
        // everything at once, explosion retriggered: the limiter keeps |x| < 1
        var c = Run(new SoundBoard(), p => new SoundState { Explosions = 3, Laser = true, Torpedo = true, Motor = true, Beep = true, BackgroundLevel = 0xE0 }, 200);
        Assert.All(c, v => Assert.True(Math.Abs(v) < 1f && !float.IsNaN(v)));
    }

    [Fact]
    public void SilentWhenNothingPlays()
    {
        var x = Run(new SoundBoard(), _ => Quiet, 100);
        Assert.True(x.Max(v => Math.Abs(v)) < 1e-4, "idle board should be silent");
    }

    [Fact]
    public void MuteAndVolume()
    {
        SoundState St(int p) => new() { Motor = true, BackgroundMuted = true };
        var full = Run(new SoundBoard(), St, 40);
        var half = Run(new SoundBoard { Volume = 0.5 }, St, 40);
        var mute = Run(new SoundBoard { Muted = true }, St, 40);
        for (int i = 0; i < full.Length; i++) Assert.Equal(full[i] * 0.5f, half[i], 5);
        Assert.All(mute, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void ExplosionHoldsThenDecays()
    {
        var x = Run(SoundBoard.ForCues(SoundBoard.CueX), p => p == 0 ? new SoundState { Explosions = 1, BackgroundMuted = true } : Quiet, 60);
        int w = 2400; // 50 ms windows, like p8_sound.txt (6)
        double[] r = Enumerable.Range(0, 30).Select(k => Rms(x, k * w, w)).ToArray();
        double plateau = r.Skip(2).Take(6).Average();
        Assert.True(r[7] > 0.6 * plateau, "still loud near the end of the 0.35 s hold");
        Assert.True(r[12] < 0.2 * plateau, "decayed by 0.6 s");
        Assert.True(r[20] < 0.02 * plateau, "silent by 1 s");
        for (int k = 9; k < 25; k++) Assert.True(r[k + 1] < r[k] * 1.25 + 1e-9, $"decay not monotone at {k}");
        // calibrated level: plateau rms = 12500/32768 × master
        Assert.InRange(plateau, 0.8 * 12500 / 32768.0 * SoundBoard.Master, 1.25 * 12500 / 32768.0 * SoundBoard.Master);
    }

    [Fact]
    public void ExplosionRetriggerDoesNotExtendHold()
    {
        // trigger at pass 0 and again at pass 6 (inside the 0.35 s ≈ 13.4-pass hold) vs a single trigger
        var once = Run(SoundBoard.ForCues(SoundBoard.CueX), p => new SoundState { Explosions = p == 0 ? 1 : 0, BackgroundMuted = true }, 40);
        var twice = Run(SoundBoard.ForCues(SoundBoard.CueX), p => new SoundState { Explosions = p == 0 || p == 6 ? 1 : 0, BackgroundMuted = true }, 40);
        Assert.Equal(once, twice);
        // a trigger after the hold restarts it
        var late = Run(SoundBoard.ForCues(SoundBoard.CueX), p => new SoundState { Explosions = p == 0 || p == 20 ? 1 : 0, BackgroundMuted = true }, 40);
        int at = 1263 * 26;
        Assert.True(Rms(late, at, 2400) > 5 * Rms(once, at, 2400));
    }

    [Fact]
    public void LaserSweepsDownAndRestartsOnEachSwitchOn()
    {
        Assert.Equal(0.279, SoundBoard.LaserPeriodMs(0), 6);
        Assert.Equal(SoundBoard.LaserPeriodMs(107), SoundBoard.LaserPeriodMs(500), 9);
        var x = Run(SoundBoard.ForCues(SoundBoard.CueL), p => new SoundState { Laser = p < 5 || (p >= 10 && p < 15), BackgroundMuted = true }, 20);
        int ms = 48;
        double f40 = F0(x, 35 * ms, 10 * ms), f95 = F0(x, 90 * ms, 10 * ms);
        Assert.InRange(f40, 0.85 * 1000 / SoundBoard.LaserPeriodMs(40), 1.15 * 1000 / SoundBoard.LaserPeriodMs(40));
        Assert.InRange(f95, 0.85 * 1000 / SoundBoard.LaserPeriodMs(95), 1.15 * 1000 / SoundBoard.LaserPeriodMs(95));
        Assert.True(f40 > f95);
        // second burst starts at pass 10: same sweep again
        int start2 = (int)Math.Round(10 * SoundBoard.SampleRate / GameData.PassesPerSecond);
        Assert.InRange(F0(x, start2 + 35 * ms, 10 * ms), 0.9 * f40, 1.1 * f40);
        // counter held in CLR while off: no tone between the bursts after the coupling filter settles
        Assert.True(Rms(x, start2 - 40 * ms, 30 * ms) < 0.25 * Rms(x, 35 * ms, 30 * ms));
    }

    [Fact]
    public void TorpedoSweepsToPlateau()
    {
        Assert.Equal(8.55, SoundBoard.TorpedoPeriodMs(1000), 9);
        Assert.Equal((2.75 + 4.06) / 2, SoundBoard.TorpedoPeriodMs(31), 9);
        var x = Run(SoundBoard.ForCues(SoundBoard.CueT), _ => new SoundState { Torpedo = true, BackgroundMuted = true }, 40);
        Assert.InRange(F0(x, 48 * 400, 48 * 300), 105, 130); // measured plateau 117 Hz
    }

    [Theory]
    [InlineData(0, 161)]
    [InlineData(3, 246)]
    [InlineData(6, 519)]
    public void BackgroundPitchFollowsBits75(int v, double hz)
    {
        // Count QA cycles directly: QA toggles on each falling edge of the OR'd clock, so f(QA) = tone f0.
        var x = Run(SoundBoard.ForCues(SoundBoard.CueBg), _ => new SoundState { BackgroundLevel = v << 5 }, 76);
        Assert.True(Rms(x, 0, x.Length) > 0.005);
        Assert.Equal(hz, SoundBoard.BackgroundToneHz[v]);
        // AM (gating by BGCLK1) period from the netlist formula: 1/(2 HP1)
        double am = 1 / (2 * SoundBoard.BackgroundHp1(v));
        Assert.InRange(am, 4.9, 16);
    }

    [Fact]
    public void BackgroundNetlistVoltagesAndRates()
    {
        Assert.Equal(-4.1125, SoundBoard.BackgroundVolts(0), 6);
        Assert.Equal(0, SoundBoard.BackgroundVolts(7), 9);
        Assert.Equal(0.1, SoundBoard.BackgroundHp1(0), 9);        // clamped: 5 Hz
        Assert.Equal(1 / (2 * 5.75), SoundBoard.BackgroundHp1(2), 2);
        // muted = CLR: silent
        var x = Run(SoundBoard.ForCues(SoundBoard.CueBg), _ => new SoundState { BackgroundMuted = true, BackgroundLevel = 0x60 }, 40);
        Assert.True(x.Max(v => Math.Abs(v)) < 1e-6);
    }

    [Fact]
    public void BeepAndMotorGatedAndPitched()
    {
        var beep = Run(SoundBoard.ForCues(SoundBoard.CueBeep), p => new SoundState { Beep = p >= 10, BackgroundMuted = true }, 40);
        int s10 = (int)(10 * SoundBoard.SampleRate / GameData.PassesPerSecond);
        Assert.True(beep.Take(s10).Max(v => Math.Abs(v)) < 1e-6);
        Assert.InRange(F0(beep, s10 + 4800, 24000), 1400, 1520); // 555: 1455 Hz (measured 1462)
        var a = new Astable555(4.7e3, 8.2e3, 0.047e-6, 48000);
        Assert.InRange(a.Frequency(48000), 1450, 1460);
        Assert.InRange(a.Duty, 0.60, 0.62);
        var m1 = new Astable555(68e3, 30e3, 0.22e-6, 48000); var m2 = new Astable555(56e3, 24e3, 0.1e-6, 48000);
        Assert.InRange(m1.Frequency(48000), 50.5, 51.8);  // measured line 51/52 Hz
        Assert.InRange(m2.Frequency(48000), 138, 139.5);  // measured line 139 Hz
        var motor = Run(SoundBoard.ForCues(SoundBoard.CueMotor), p => new SoundState { Motor = p < 20, BackgroundMuted = true }, 60);
        int s20 = (int)(20 * SoundBoard.SampleRate / GameData.PassesPerSecond);
        Assert.True(Rms(motor, s20 + 9600, 9600) < 0.02 * Rms(motor, 4800, 9600));
    }

    [Fact]
    public void CalibratedLevelsMatchMame()
    {
        (int cue, SoundState st, double rms, double skip, double len)[] cases =
        {
            (SoundBoard.CueT, new SoundState { Torpedo = true, BackgroundMuted = true }, SoundBoard.RmsTorpedo, 0.5, 1.0),
            (SoundBoard.CueBg, new SoundState(), SoundBoard.RmsBackground, 0.5, 2.0),
            (SoundBoard.CueBeep, new SoundState { Beep = true, BackgroundMuted = true }, SoundBoard.RmsBeep, 0.5, 1.0),
            (SoundBoard.CueMotor, new SoundState { Motor = true, BackgroundMuted = true }, SoundBoard.RmsMotor, 0.5, 1.0),
        };
        foreach (var (cue, st, rms, skip, len) in cases)
        {
            var x = Run(SoundBoard.ForCues(cue), _ => st, 120);
            double got = Rms(x, (int)(skip * 48000), (int)(len * 48000)), want = rms / 32768 * SoundBoard.Master;
            Assert.InRange(got, want * 0.9, want * 1.1);
        }
    }

    [Fact]
    public void CounterDacWeightsAndClear()
    {
        var d = new CounterDac(82e3, 39e3, 10e3, 20e3);
        for (int i = 0; i < 4 * 2; i++) d.Step(1.0, false); // half-period 1 sample: one falling edge per 2 samples
        Assert.Equal(4, d.Count);
        Assert.True(d.Value > 0.4 && d.Value < 0.6); // QC = R 10k: the heaviest bit
        d.Step(1.0, true); Assert.Equal(0, d.Count); Assert.Equal(0, d.Value);
        Assert.InRange(SoundBoard.Limit(5), 0.98, 0.99); Assert.True(SoundBoard.Limit(1e9) <= 0.99 && SoundBoard.Limit(-1e9) >= -0.99);
        Assert.Equal(0.5, SoundBoard.Limit(0.5)); Assert.Equal(0, SoundBoard.Limit(double.NaN));
    }
}
