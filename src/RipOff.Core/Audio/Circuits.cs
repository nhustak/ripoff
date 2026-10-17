using System;

namespace RipOff.Core.Audio
{
    // Building blocks for the Rip-Off sound board model (NOTES §21). All parameters are taken from MAME's
    // netlist src/mame/cinematronics/nl_ripoff.cpp (tag mame0276) unless marked as measured (trace:excerpts/
    // p8_sound.txt) or UNCHECKED. Pure and deterministic: no clocks, threads or randomness from the runtime.

    /// <summary>MM5837 / AMI S2688 noise (IC1): 17-bit shift register, taps bits 0 and 3, one shift per clock.
    /// netlist lib nld_mm5837.cpp (mame0276): FREQ default 24000*2 = 48 kHz, so one shift per output sample.</summary>
    public sealed class Noise17
    {
        public const int Seed = 0x1FFFF; // nld_mm5837.cpp reset: m_shift = 0x1ffff
        private int _s = Seed;

        /// <summary>Advances one clock; returns the output bit (VDD = +15 V when 1, VSS = GND when 0).</summary>
        public int Next()
        {
            _s = (_s >> 1) | (((_s & 1) ^ ((_s >> 3) & 1)) << 16);
            return _s & 1;
        }
    }

    /// <summary>One-pole RC low-pass, y += a (x − y), a = 1 − exp(−2π fc / fs).</summary>
    public struct LowPass
    {
        private readonly double _a;
        private double _y;
        public LowPass(double fc, double fs) { _a = 1 - Math.Exp(-2 * Math.PI * fc / fs); _y = 0; }
        public static double Corner(double r, double c) => 1 / (2 * Math.PI * r * c);
        public double Step(double x) { _y += _a * (x - _y); return _y; }
    }

    /// <summary>One-pole RC high-pass (series C, shunt R): x minus its low-pass.</summary>
    public struct HighPass
    {
        private LowPass _lp;
        public HighPass(double fc, double fs) { _lp = new LowPass(fc, fs); }
        public double Step(double x) => x - _lp.Step(x);
    }

    /// <summary>555 astable: t_high = 0.693 (RA + RB) C, t_low = 0.693 RB C (NE555 datasheet). Output 0/1.</summary>
    public struct Astable555
    {
        public readonly double HighSamples, LowSamples;
        private double _t;
        public Astable555(double ra, double rb, double c, double fs)
        {
            HighSamples = 0.693 * (ra + rb) * c * fs;
            LowSamples = 0.693 * rb * c * fs;
            _t = 0;
        }
        public double Frequency(double fs) => fs / (HighSamples + LowSamples);
        public double Duty => HighSamples / (HighSamples + LowSamples);
        public int Step()
        {
            int o = _t < HighSamples ? 1 : 0;
            _t += 1; if (_t >= HighSamples + LowSamples) _t -= HighSamples + LowSamples;
            return o;
        }
    }

    /// <summary>
    /// Square clock with a variable half-period (netlist VARCLOCK) driving a 74LS393 4-bit counter that counts
    /// falling clock edges, with an active-high clear, read through a 4-resistor DAC. Several clock edges per
    /// sample are allowed (the laser clock runs faster than the 48 kHz sample rate at the start of its sweep).
    /// </summary>
    public sealed class CounterDac
    {
        private readonly double[] _w; // weight of QA, QB, QC, QD = 1/R, normalised so all-ones = 1
        private double _phase;        // samples since the last clock toggle
        private bool _clk;
        public int Count { get; private set; }

        public CounterDac(double rQA, double rQB, double rQC, double rQD)
        {
            double sum = 1 / rQA + 1 / rQB + 1 / rQC + 1 / rQD;
            _w = new[] { 1 / rQA / sum, 1 / rQB / sum, 1 / rQC / sum, 1 / rQD / sum };
        }

        public double Value => ((Count & 1) * _w[0]) + (((Count >> 1) & 1) * _w[1]) + (((Count >> 2) & 1) * _w[2]) + (((Count >> 3) & 1) * _w[3]);

        /// <summary>CLR high: counter held at 0 (the clock keeps running).</summary>
        public void Clear() => Count = 0;

        /// <summary>Advances one sample with the clock half-period <paramref name="halfPeriodSamples"/>.</summary>
        public double Step(double halfPeriodSamples, bool clear)
        {
            _phase += 1;
            while (_phase >= halfPeriodSamples)
            {
                _phase -= halfPeriodSamples;
                _clk = !_clk;
                if (!_clk && !clear) Count = (Count + 1) & 15; // 74LS393 counts on the falling edge
            }
            if (clear) Count = 0;
            return Value;
        }

        /// <summary>Counts one externally generated falling edge (used where two clocks are OR'd).</summary>
        public void Edge(bool clear) { Count = clear ? 0 : (Count + 1) & 15; }
    }

    /// <summary>
    /// Toggling clock with a variable half-period, exposing its level (for the OR of the two background clocks).
    /// </summary>
    public struct VarClock
    {
        private double _phase;
        public bool Level;
        public void Step(double halfPeriodSamples)
        {
            _phase += 1;
            while (_phase >= halfPeriodSamples) { _phase -= halfPeriodSamples; Level = !Level; }
        }
    }
}
