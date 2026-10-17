using System;

namespace RipOff.Core.Audio
{
    /// <summary>
    /// Runtime synthesis of the Rip-Off sound board from the simulation's per-pass <see cref="SoundState"/>
    /// (NOTES §21). Each cue follows the circuit in MAME's nl_ripoff.cpp (mame0276); where the netlist itself uses a
    /// fitted high-level model (the laser and torpedo VCOs) the sweep is taken from measurements of MAME's output
    /// (trace:excerpts/p8_sound.txt). Levels are calibrated so each cue's rms matches the measured MAME rms.
    /// No samples are recorded from MAME or loaded from disk. Mono, 48 kHz, float in (−1, 1).
    /// </summary>
    public sealed class SoundBoard
    {
        public const int SampleRate = 48000;

        /// <summary>Overall level: MAME full scale (32768) maps to this. Leaves headroom for overlapping cues.</summary>
        public const double Master = 0.6;

        // ---- cue levels: rms of each cue alone in MAME, full scale 32768 (p8_sound.txt (6), (8), (10)) ----
        public const double RmsExplosion = 12500; // (6) median rms of 50 ms windows 100-400 ms after the trigger
        public const double RmsLaser = 1672;      // (8) in-game laser burst, t = 30-105 ms
        public const double RmsTorpedo = 1453;    // (10) sl_q3_0 - base
        public const double RmsBackground = 1375; // (10) base - sl_fc4_1
        public const double RmsBeep = 1002;       // (10) sl_fc3_0 - base
        public const double RmsMotor = 751;       // (10) sl_fc2_0 - base

        // ---- explosion (NOTES §21.2) ----
        /// <summary>IC4 LM555 monostable, R8 470k, C6 0.68 µF: T = 1.1 R C = 0.3516 s.</summary>
        public const double ExplosionHoldSeconds = 1.1 * 470e3 * 0.68e-6;
        /// <summary>C8 4.7 µF discharging through R10 20k into the IC3 CA3080 bias input gives R10·C8 = 94 ms, but
        /// MAME's output decays with τ = 141 ms (p8 (6), fit over 350-800 ms, 18 triggers); the measured value is
        /// used. UNCHECKED: why the netlist output is slower than R10·C8 (CA3080 bias non-linearity?).</summary>
        public const double ExplosionDecaySeconds = 0.141;
        public const double ExplosionRcSeconds = 20e3 * 4.7e-6;
        /// <summary>C8 charge through Q1 (R9 330 base). UNCHECKED: not derived, chosen short.</summary>
        public const double ExplosionAttackSeconds = 0.002;

        // ---- laser / torpedo sweeps (measured; netlist HLE_LASER_VCO / HLE_TORPEDO_VCO) ----
        /// <summary>Laser tone period P(t) = 0.279 ms + 0.0189 t (p8 (8): linear fit over t = 37-107 ms, one burst).</summary>
        public const double LaserP0Ms = 0.279, LaserSlope = 0.0189;
        /// <summary>UNCHECKED: beyond the measured 107 ms the period is held at its 107 ms value.</summary>
        public const double LaserHoldAfterMs = 107;
        /// <summary>Torpedo tone period (ms) vs time since Q3 went on (p8 (8), 103 bursts, window centres). The
        /// t = 0 point is a linear extrapolation (UNCHECKED); the final value is the 117 Hz plateau of p8 (9).</summary>
        public static readonly (double tMs, double pMs)[] TorpedoPeriod =
        {
            (0, 1.37), (21, 2.75), (41, 4.06), (61, 5.42), (81, 6.56), (101, 7.42), (121, 7.94), (141, 8.33), (161, 8.55),
        };

        // ---- background (NOTES §21.3) ----
        /// <summary>Measured tone f0 for bits 7-5 = 0..6 (p8 (5)); 7 never occurs in the logs, so it uses the
        /// netlist value 1/(4 HP2(0 V)) = 975 Hz (UNCHECKED).</summary>
        public static readonly double[] BackgroundToneHz = { 161, 182, 208, 246, 296, 378, 519, 1 / (4 * 0.000256399) };

        private readonly Noise17 _noise = new();
        private LowPass _xLp1, _xLp2; private HighPass _xHp1, _xHp2;
        private Astable555 _beep, _motor1, _motor2;
        private HighPass _beepHp, _m1Hp, _m2Hp;
        private readonly CounterDac _laser, _torpedo, _bg;
        private VarClock _bgClk1, _bgClk2;
        private bool _bgOr;
        private HighPass _outA, _outB;

        private double _xEnv, _xHold; // explosion envelope 0..1 and remaining hold time (samples)
        private double _laserT, _torpT; // samples since the cue switched on
        private bool _laserWas, _torpWas;
        private long _passes;
        private readonly double _aAttack, _aDecay;

        // per-cue gains, calibrated once (see Calibrate)
        private readonly double _gX, _gL, _gT, _gBg, _gBeep, _gMotor;

        /// <summary>0..1 output volume. Applied after the mix.</summary>
        public double Volume { get; set; } = 1.0;
        public bool Muted { get; set; }

        public SoundBoard() : this(Gains.Value) { }

        private SoundBoard((double x, double l, double t, double bg, double beep, double motor) g)
        {
            const double fs = SampleRate;
            // Explosion noise chain (netlist "Explosion" block):
            _xLp1 = new LowPass(LowPass.Corner(47e3, 0.1e-6), fs);  // R1 47k, C2 0.1 µF shunt (loading by R2 ignored: UNCHECKED)
            _xHp1 = new HighPass(LowPass.Corner(47e3, 0.1e-6), fs); // R2 47k + C3 0.1 µF into IC2's virtual ground
            _xLp2 = new LowPass(LowPass.Corner(330e3, 0.02e-6), fs); // IC2 feedback R3 330k ‖ C4 0.02 µF (gain R3/R2)
            _xHp2 = new HighPass(LowPass.Corner(10e3 + 2.7e3, 0.1e-6), fs); // R4 10k, C5 0.1 µF, R5 2.7k into IC3
            // Beep: IC5 555 astable, R13 4.7k, R14 8.2k, C9 0.047 µF; R15 15k / C11 0.1 µF / R16 2.7k coupling.
            _beep = new Astable555(4.7e3, 8.2e3, 0.047e-6, fs);
            _beepHp = new HighPass(LowPass.Corner(15e3 + 2.7e3, 0.1e-6), fs);
            // Motor: IC11 555 (R31 68k, R32 30k, C14 0.22 µF) and IC13 555 (R37 56k, R38 24k, C17 0.1 µF), each via
            // 15k + 0.1 µF (R33/C16, R39/C19) into R34 2.7k, i.e. equal weights. D12/D13 clamp not modelled (UNCHECKED).
            _motor1 = new Astable555(68e3, 30e3, 0.22e-6, fs);
            _motor2 = new Astable555(56e3, 24e3, 0.1e-6, fs);
            _m1Hp = new HighPass(LowPass.Corner(15e3 + 2.7e3, 0.1e-6), fs);
            _m2Hp = new HighPass(LowPass.Corner(15e3 + 2.7e3, 0.1e-6), fs);
            // 74LS393 DACs: laser IC18 2QA..2QD → R65 82k, R64 39k, R62 10k, R63 20k; torpedo 1QA..1QD → R75, R74,
            // R72, R73 (same values); background IC22 QA..QD → R86 10k, R87 20k, R88 39k, R89 82k.
            _laser = new CounterDac(82e3, 39e3, 10e3, 20e3);
            _torpedo = new CounterDac(82e3, 39e3, 10e3, 20e3);
            _bg = new CounterDac(10e3, 20e3, 39e3, 82e3);
            // Output node OUTPUT = R45.1 = R49.2 (netlist ALIAS, :517). IC7 → C12 0.1 µF → R45 27k and the DACs →
            // R48 2.7k (R91 2.7k) → C21 (C32) 0.68 µF → R49 470 meet there; with the node unloaded (UNCHECKED: MAME's
            // output stage) each coupling capacitor sees the whole loop R45 + R49 + R48 + DAC source (R47 1k ‖ DAC
            // ≈ 0.85k) ≈ 31k: high-pass 51 Hz (C12) and 7.5 Hz (C21/C32).
            const double loopR = 27e3 + 470 + 2.7e3 + 850;
            _outA = new HighPass(LowPass.Corner(loopR, 0.1e-6), fs);
            _outB = new HighPass(LowPass.Corner(loopR, 0.68e-6), fs);
            _aAttack = 1 - Math.Exp(-1 / (ExplosionAttackSeconds * fs));
            _aDecay = Math.Exp(-1 / (ExplosionDecaySeconds * fs));
            (_gX, _gL, _gT, _gBg, _gBeep, _gMotor) = g;
        }

        /// <summary>Samples in the next pass at 38 passes/s (1263 or 1264; exact over time).</summary>
        public int NextPassLength()
        {
            const long passesPerSecond = 38; // GameData.PassesPerSecond
            long a = _passes * SampleRate / passesPerSecond, b = (_passes + 1) * SampleRate / passesPerSecond;
            _passes++; return (int)(b - a);
        }

        /// <summary>Largest buffer one pass can need.</summary>
        public static int MaxPassSamples => (int)Math.Ceiling(SampleRate / GameData.PassesPerSecond) + 1;

        /// <summary>Renders one simulation pass into <paramref name="buf"/>; returns the sample count written.</summary>
        public int RenderPass(in SoundState s, float[] buf)
        {
            int n = NextPassLength();
            if (buf.Length < n) throw new ArgumentException("buffer too small", nameof(buf));
            Render(s, buf, n, true);
            return n;
        }

        // Cue gates for calibration / tests: bit mask of cues to output (all by default).
        internal const int CueX = 1, CueL = 2, CueT = 4, CueBg = 8, CueBeep = 16, CueMotor = 32, CueAll = 63;
        internal int CueMask = CueAll;

        internal void Render(in SoundState s, float[] buf, int n, bool final)
        {
            const double fs = SampleRate;
            // Explosion: a Q7 falling edge triggers the 555 (pin 2). A trigger while the 555 is timing does not
            // extend it (NE555 behaviour; UNCHECKED against the real board).
            if (s.Explosions > 0 && _xHold <= 0) _xHold = ExplosionHoldSeconds * fs;
            // Laser / torpedo: sweep restarts each time the cue switches on (counter leaves CLR).
            if (s.Laser && !_laserWas) _laserT = 0;
            if (s.Torpedo && !_torpWas) _torpT = 0;
            _laserWas = s.Laser; _torpWas = s.Torpedo;
            int v = (s.BackgroundLevel >> 5) & 7;
            double hp1 = BackgroundHp1(v) * fs, hp2 = 1 / (4 * BackgroundToneHz[v]) * fs;
            double gain = Muted ? 0 : Math.Clamp(Volume, 0, 1);

            for (int i = 0; i < n; i++)
            {
                // explosion: noise (0/1) → filters → CA3080 gain ∝ envelope
                double nz = _noise.Next() - 0.5;
                double xs = _xHp2.Step(_xLp2.Step(_xHp1.Step(_xLp1.Step(nz))));
                if (_xHold > 0) { _xEnv += _aAttack * (1 - _xEnv); _xHold -= 1; }
                else _xEnv *= _aDecay;
                double explosion = xs * _xEnv;

                double beep = _beepHp.Step(_beep.Step()) * (s.Beep ? 1 : 0); // Q2/Q3 hard gate via R18
                double motor = (_m1Hp.Step(_motor1.Step()) + _m2Hp.Step(_motor2.Step())) * (s.Motor ? 1 : 0); // Q4/Q5 via R36

                double lp = LaserPeriodMs(_laserT / fs * 1000) / 1000 * fs / 16; // half-period = P/16 (f0 = clk/8, QC heaviest)
                double laser = _laser.Step(lp, !s.Laser);
                double tp = TorpedoPeriodMs(_torpT / fs * 1000) / 1000 * fs / 16;
                double torpedo = _torpedo.Step(tp, !s.Torpedo);
                if (s.Laser) _laserT += 1;
                if (s.Torpedo) _torpT += 1;

                // background: two clocks OR'd (AFUNC max) clock IC22 on falling edges; CLR = command bit 4
                _bgClk1.Step(hp1); _bgClk2.Step(hp2);
                bool or = _bgClk1.Level || _bgClk2.Level;
                if (_bgOr && !or) _bg.Edge(s.BackgroundMuted);
                if (s.BackgroundMuted) _bg.Clear();
                _bgOr = or;
                double bg = _bg.Value;

                double a = ((CueMask & CueX) != 0 ? _gX * explosion : 0) + ((CueMask & CueBeep) != 0 ? _gBeep * beep : 0)
                         + ((CueMask & CueMotor) != 0 ? _gMotor * motor : 0);
                double b = ((CueMask & CueL) != 0 ? _gL * laser : 0) + ((CueMask & CueT) != 0 ? _gT * torpedo : 0)
                         + ((CueMask & CueBg) != 0 ? _gBg * bg : 0);
                double y = _outA.Step(a) + _outB.Step(b);
                buf[i] = (float)(final ? Limit(y) * gain : y);
            }
        }

        /// <summary>Laser tone period (ms) at <paramref name="tMs"/> after switch-on.</summary>
        public static double LaserPeriodMs(double tMs) => LaserP0Ms + LaserSlope * Math.Min(tMs, LaserHoldAfterMs);

        /// <summary>Torpedo tone period (ms): linear interpolation of <see cref="TorpedoPeriod"/>, held at the end.</summary>
        public static double TorpedoPeriodMs(double tMs)
        {
            var t = TorpedoPeriod;
            if (tMs <= t[0].tMs) return t[0].pMs;
            for (int i = 1; i < t.Length; i++)
                if (tMs < t[i].tMs) return t[i - 1].pMs + (t[i].pMs - t[i - 1].pMs) * (tMs - t[i - 1].tMs) / (t[i].tMs - t[i - 1].tMs);
            return t[^1].pMs;
        }

        /// <summary>V(IC19.6) for background bits 7-5: IC10 (7406) outputs off for a 0 bit let current from the
        /// D1 10 V zener (1N5240, R23 160 Ω) flow through R25+R26 (2k), R27+R28 (4k), R29+R30 (8k) into the
        /// IC19 inverting summer (R76 470 Ω feedback). UNCHECKED: ideal open collector and an exact 10 V zener.</summary>
        public static double BackgroundVolts(int v) =>
            -470 * 10.0 * ((((v >> 2) & 1) == 0 ? 1 / 2000.0 : 0) + (((v >> 1) & 1) == 0 ? 1 / 4000.0 : 0) + ((v & 1) == 0 ? 1 / 8000.0 : 0));

        /// <summary>BGCLK1 half-period (s): netlist VARCLOCK "max(1e-6, min(0.1, −0.0235033 A0 + 0.0179360))".</summary>
        public static double BackgroundHp1(int v) => Math.Clamp(-0.0235033 * BackgroundVolts(v) + 0.0179360, 1e-6, 0.1);

        /// <summary>Soft knee: unity up to 0.8, then 0.8 + 0.19 tanh((|x| − 0.8) / 0.19); |output| &lt; 0.99 always.</summary>
        public static double Limit(double x)
        {
            double a = Math.Abs(x);
            if (double.IsNaN(x)) return 0;
            if (a <= 0.8) return x;
            return Math.Sign(x) * (0.8 + 0.19 * Math.Tanh((a - 0.8) / 0.19));
        }

        // ---------------------------------------------------------------- calibration

        private static readonly Lazy<(double, double, double, double, double, double)> Gains = new(Calibrate);

        /// <summary>Renders each cue alone at unit gain in a fixed scenario and scales it to its MAME rms.</summary>
        private static (double, double, double, double, double, double) Calibrate()
        {
            double Rms(int cue, SoundState st, double skipS, double lenS, bool explosionTrigger)
            {
                var b = new SoundBoard((1, 1, 1, 1, 1, 1)) { CueMask = cue };
                var buf = new float[MaxPassSamples];
                double sum = 0; long cnt = 0; long t = 0;
                long skip = (long)(skipS * SampleRate), len = (long)(lenS * SampleRate);
                bool first = true;
                while (t < skip + len)
                {
                    var s = st; if (explosionTrigger && first) s.Explosions = 1;
                    first = false;
                    int n = b.NextPassLength(); b.Render(s, buf, n, false);
                    for (int i = 0; i < n; i++, t++) if (t >= skip && t < skip + len) { sum += buf[i] * (double)buf[i]; cnt++; }
                }
                return Math.Sqrt(sum / cnt);
            }
            double G(double mameRms, double unitRms) => mameRms / 32768 * Master / unitRms;
            var quiet = new SoundState { BackgroundMuted = true };
            // Same windows as the measurements: explosion 100-400 ms after the trigger; laser 30-105 ms after
            // switch-on; the others in steady state (the background at bits 7-5 = 0).
            double x = Rms(CueX, quiet, 0.1, 0.3, true);
            double l = Rms(CueL, new SoundState { BackgroundMuted = true, Laser = true }, 0.03, 0.075, false);
            double tq = Rms(CueT, new SoundState { BackgroundMuted = true, Torpedo = true }, 0.5, 1.0, false);
            double bg = Rms(CueBg, new SoundState { BackgroundMuted = false }, 0.5, 2.0, false);
            double be = Rms(CueBeep, new SoundState { BackgroundMuted = true, Beep = true }, 0.5, 1.0, false);
            double mo = Rms(CueMotor, new SoundState { BackgroundMuted = true, Motor = true }, 0.5, 1.0, false);
            return (G(RmsExplosion, x), G(RmsLaser, l), G(RmsTorpedo, tq), G(RmsBackground, bg), G(RmsBeep, be), G(RmsMotor, mo));
        }

        /// <summary>Calibrated per-cue gains (explosion, laser, torpedo, background, beep, motor), for tests/docs.</summary>
        public static (double, double, double, double, double, double) CueGains => Gains.Value;

        /// <summary>Test hook: a board that outputs only the given cues (CueX…CueMotor) without the limiter.</summary>
        internal static SoundBoard ForCues(int mask) => new() { CueMask = mask };
    }
}
