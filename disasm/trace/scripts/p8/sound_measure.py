#!/usr/bin/env python3
"""Pass 8: measure each sound cue of MAME's Rip-Off netlist (nl_ripoff.cpp, mame0276) from the pass-6
sound-lab WAVs (scripts/p6/soundlab.sh: identical 4,000-frame shoot runs, one bit forced per run).
A cue = difference of two runs whose game logic is identical (NOTES §19.3). Prints derived numbers only;
no audio is written or committed. usage: python3 sound_measure.py  (RUNS=/workspace/mame/runs)"""
import wave, os, numpy as np
RUNS = os.environ.get("RUNS", "/workspace/mame/runs"); SR = 48000; SPF = SR / 38.0
def load(t):
    w = wave.open(f"{RUNS}/{t}.wav"); a = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16)
    return a[0::w.getnchannels()].astype(float)
def ram(t):
    R = {}
    for l in open(f"{RUNS}/{t}/ram.log"):
        p = l.split(); R[int(p[0])] = [int(x, 16) for x in p[1:]]
    return R
def f0(seg):
    n = 16384; sp = np.abs(np.fft.rfft(seg * np.hanning(len(seg)), n)); fr = np.fft.rfftfreq(n, 1 / SR)
    sp[fr < 60] = 0; return fr[np.argmax(sp)]
def bursts(d, thr=200, minlen=0.06):
    env = np.sqrt(np.convolve(d * d, np.ones(480) / 480, "same")); on = env > thr
    st = np.where(on[1:] & ~on[:-1])[0] + 1; en = np.where(~on[1:] & on[:-1])[0] + 1
    out = []
    for s in st:
        e = en[en > s]
        if len(e) and (e[0] - s) / SR >= minlen: out.append((s, e[0]))
    return out
base = load("sl_base"); rb = ram("sl_base")
print("# p8 sound_measure.py: MAME netlist cue measurements (sl_* runs, 48 kHz, 1 frame = %.1f samples)" % SPF)

def sweep(name, other, win=1024, step=512, maxms=110):
    d = base - load(other); bs = bursts(d)
    print(f"\n{name}: base - {other}, {len(bs)} bursts >= 60 ms")
    tracks = []
    for s, e in bs:
        tr = []
        for k in range(s, min(e, s + int(maxms / 1000 * SR)) - win, step):
            tr.append(((k + win / 2 - s) / SR * 1000, f0(d[k:k + win])))
        tracks.append(tr)
    n = min(len(t) for t in tracks)
    print("  t(ms, window centre)  median f0 (Hz) over bursts   median period (ms)")
    for i in range(n):
        fs = np.median([t[i][1] for t in tracks]); print("  %6.1f  %7.0f  %6.3f" % (tracks[0][i][0], fs, 1000 / fs))
    return d
sweep("(1) laser (Q4 = 0 on)", "sl_q4_1")
sweep("(2) torpedo (Q3 = 0 on)", "sl_q3_1")

print("\n(3) motor (FC bit 2 = 0 on): sl_fc2_0 - base in base bit-2 = 1 frames")
d = load("sl_fc2_0") - base
fr_on = [f for f in rb if (rb[f][0xFC] >> 2) & 1 and f > 260]
seg = np.concatenate([d[int((f - 1) * SPF):int(f * SPF)] for f in fr_on[:200]])
sp = np.abs(np.fft.rfft(seg * np.hanning(len(seg)))); fr = np.fft.rfftfreq(len(seg), 1 / SR)
print("  strongest spectral lines (Hz):", sorted({int(round(fr[i])) for i in np.argsort(sp)[::-1][:12]}))

print("\n(4) beep (FC bit 3 = 0 on): sl_fc3_0 - base in base bit-3 = 1 frames")
d = load("sl_fc3_0") - base
fr_on = [f for f in rb if (rb[f][0xFC] >> 3) & 1 and f > 260][:200]
seg = np.concatenate([d[int((f - 1) * SPF):int(f * SPF)] for f in fr_on])
print("  f0 = %.0f Hz" % f0(seg[:SR]))
x = seg[:4096]; thr = (x.max() + x.min()) / 2; hi = np.mean(x > thr)
print("  fraction of samples above mid-level (duty, after coupling filters) = %.2f" % hi)

print("\n(5) background (FC bit 4 = 0 on): base - sl_fc4_1, grouped by FC bits 7-5")
d = base - load("sl_fc4_1")
groups = {}
for f in rb:
    if f > 260 and not (rb[f][0xFC] >> 4) & 1: groups.setdefault(rb[f][0xFC] >> 5 & 7, []).append(f)
for v in sorted(groups):
    fs = groups[v]
    if len(fs) < 40: print(f"  bits 7-5 = {v}: {len(fs)} frames (too few)"); continue
    seg = np.concatenate([d[int((f - 1) * SPF):int(f * SPF)] for f in fs[:300]])
    env = np.abs(seg); n = 8192
    sp = np.abs(np.fft.rfft(env[:SR * 2] - env[:SR * 2].mean(), 65536)); fr = np.fft.rfftfreq(65536, 1 / SR); sp[fr < 2] = 0; sp[fr > 60] = 0
    print(f"  bits 7-5 = {v}: {len(fs)} frames, tone f0 = {f0(seg[:SR]):.0f} Hz, amplitude-modulation f = {fr[np.argmax(sp)]:.1f} Hz, rms {np.sqrt(np.mean(seg**2)):.0f}")

print("\n(6) explosion (Q7 falling edge): base - sl_q7_1, per trigger (events OUT7=0)")
d = base - load("sl_q7_1")
trig = sorted({int(l.split()[0]) for l in open(f"{RUNS}/sl_base/events.log") if "OUT7=0" in l and int(l.split()[0]) > 202})
iso = [f for i, f in enumerate(trig) if (i + 1 == len(trig) or trig[i + 1] - f > 40) and (i == 0 or f - trig[i - 1] > 40)]
print(f"  {len(trig)} triggers, {len(iso)} isolated (no other within 40 frames)")
envs = []
for f in iso[:20]:
    s = int((f - 1) * SPF); x = d[s:s + int(SR * 1.0)]
    if len(x) < SR: continue
    envs.append([np.sqrt(np.mean(x[i:i + 2400] ** 2)) for i in range(0, SR - 2400, 2400)])
e = np.median(np.array(envs), axis=0)
print("  median rms per 50 ms from the trigger frame:", " ".join("%d" % v for v in e))
k = np.arange(len(e)); m = (k >= 7) & (k <= 15); slope, icpt = np.polyfit(k[m] * 50 + 25, np.log(e[m]), 1)
print(f"  decay fit over windows 350-800 ms: rms ~ exp(-t / {-1 / slope:.0f} ms)")
x = np.concatenate([d[int((f - 1) * SPF):int((f - 1) * SPF) + SR // 3] for f in iso[:20]])
sp = np.abs(np.fft.rfft(x * np.hanning(len(x)))); fr = np.fft.rfftfreq(len(x), 1 / SR)
cs = np.cumsum(sp ** 2); cs /= cs[-1]
print("  spectral energy below 50/100/200/400/800 Hz: " + " ".join("%.2f" % cs[np.searchsorted(fr, h)] for h in (50, 100, 200, 400, 800)))

print("\n(7) beep phase: FC bit 3 on BONUS frames (FC bit 0 = 1) vs frame-end RAM[$A8] & 3 (sl_base)")
import collections
c = collections.Counter(); bon = [f for f in sorted(rb) if rb[f][0xFC] & 1]
for f in bon: c[((rb[f][0xFC] >> 3) & 1, rb[f][0xA8] & 3)] += 1
for k in sorted(c): print(f"  bit3={k[0]} A8&3={k[1]}: {c[k]} frames")
odd = [f for f in bon if (rb[f][0xFC] >> 3) & 1 and (rb[f][0xA8] & 3) in (1, 2)]
print(f"  BONUS frames f{bon[0]}-f{bon[-1]}; frames with bit3=1 although A8&3 in (1,2): {odd}")

print("\n(8) sweep detail (window 512 samples, step 256) and linear/exponential period fits")
def track(d, s, e, win=512, step=256):
    return [((k + win / 2 - s) / SR * 1000, f0(d[k:k + win])) for k in range(s, e - win, step)]
d = base - load("sl_q4_1"); bs = bursts(d)
for s, e in bs:
    tr = [(t, f) for t, f in track(d, s, e) if f > 300]
    print(f"  laser burst at f{s / SPF + 1:.0f}, {1000 * (e - s) / SR:.0f} ms: " + " ".join(f"{t:.0f}ms:{f:.0f}" for t, f in tr))
    t = np.array([x[0] for x in tr]); P = 1000 / np.array([x[1] for x in tr]); a, b = np.polyfit(t, P, 1)
    print(f"    linear fit period(ms) = {b:.3f} + {a:.5f} * t(ms); max |residual| {np.max(np.abs(P - (a * t + b))):.3f} ms")
    x = d[s + int(0.03 * SR):s + int(0.105 * SR)]; print(f"    rms over t = 30-105 ms: {np.sqrt(np.mean(x ** 2)):.0f}")
d = base - load("sl_q3_1"); bs = bursts(d)
print(f"  torpedo: {len(bs)} bursts (lengths median {np.median([(e - s) / SR * 1000 for s, e in bs]):.0f} ms); 2048-sample windows")
print("    window centre t(ms)  median f0 (Hz)  period (ms)  n")
for lo in range(0, 260, 20):
    fs = [f0(d[s + int(lo / 1000 * SR):s + int(lo / 1000 * SR) + 2048]) for s, e in bs if s + int(lo / 1000 * SR) + 2048 <= e]
    if fs: print("    %5.0f  %5.0f  %6.2f  %3d" % (lo + 1024 / SR * 1000, np.median(fs), 1000 / np.median(fs), len(fs)))

print("\n(9) continuous cue (bit forced on for the whole run) minus base: plateau tone")
for name, t in (("laser Q4=0", "sl_q4_0"), ("torpedo Q3=0", "sl_q3_0")):
    d = load(t) - base; seg = d[int(1000 * SPF):int(1000 * SPF) + SR]
    print(f"  {name} ({t} - sl_base, 1 s from f1000): f0 = {f0(seg):.0f} Hz, rms {np.sqrt(np.mean(seg ** 2)):.0f}")

print("\n(10) cue level (rms of forced-minus-base difference while the cue sounds; MAME output, full scale 32768)")
def lv(name, d, frames):
    seg = np.concatenate([d[int((f - 1) * SPF):int(f * SPF)] for f in frames]); print(f"  {name}: {np.sqrt(np.mean(seg ** 2)):.0f} ({len(frames)} frames)")
lv("motor  (sl_fc2_0 - base, base bit 2 = 1)", load("sl_fc2_0") - base, [f for f in rb if (rb[f][0xFC] >> 2) & 1 and f > 260][:400])
lv("beep   (sl_fc3_0 - base, base bit 3 = 1)", load("sl_fc3_0") - base, [f for f in rb if (rb[f][0xFC] >> 3) & 1 and f > 260][:400])
lv("laser  (sl_q4_0 - base, f1000-f1400)", load("sl_q4_0") - base, list(range(1000, 1400)))
lv("torpedo(sl_q3_0 - base, f1000-f1400)", load("sl_q3_0") - base, list(range(1000, 1400)))
lv("background (base - sl_fc4_1, bit 4 = 0)", base - load("sl_fc4_1"), [f for f in rb if not (rb[f][0xFC] >> 4) & 1 and f > 260][:400])
print(f"  base run peak |sample| {np.max(np.abs(base)):.0f}")
