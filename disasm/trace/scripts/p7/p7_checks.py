#!/usr/bin/env python3
"""Pass 7 / Milestone 1 checks against the MAME RAM logs (/workspace/mame/runs/<tag>/ram.log; RAM only).
usage: python3 p7_checks.py > ../../excerpts/p7_checks.txt   (run where runs/ is reachable)"""
import math, collections, os
RUNS = os.environ.get("RUNS", "/workspace/mame/runs")
TAGS = ["idle1", "shoot1", "ram1", "wander1", "tp_idle", "tp_wander", "tp_shoot2", "tp_pvp1", "tp_pvp2"]
def s12(v): return v - 0x1000 if v >= 0x800 else v
T = [min(255, int(math.floor(256 * math.sin(math.radians(k)) + 0.5))) for k in range(91)]; T[38] = 159; T[70] = 240
def sc(a, scale, rnd=True):
    if a >= 360: a -= 360
    q, phi = a // 90, a % 90
    f = (lambda t: (t * scale + 128) >> 8) if rnd else (lambda t: (t * scale) >> 8)
    s, c = f(T[phi]), f(T[90 - phi])
    if q % 2: s, c = c, s
    if q >= 2: s = -s
    if q in (1, 2): c = -c
    return s, c, q
def disp(h, sp, rnd=True):
    a = h - 90
    if a < 0: a += 360
    s, c, q = sc(a, sp, rnd); return c, s, q
A = [[0] * (m + 1) for m in range(32)]
for mx in range(32):
    for mn in range(mx + 1):
        A[mx][mn] = 45 if mx == 0 else int(math.floor(90 - math.degrees(math.atan(mn / mx)) + 0.5))
for (i, j, v) in ((10, 1, 85), (22, 6, 76), (24, 21, 50), (27, 8, 74), (30, 23, 54), (31, 19, 59), (31, 23, 55)): A[i][j] = v
def bearing(dx, dy):
    if dx > 0: ax = dx; ay, q = (dy, 1) if dy > 0 else (-dy, 4)
    else: ax = -dx; ay, q = (dy, 2) if dy > 0 else (-dy, 3)
    while ax >= 32 or ay >= 32: ax >>= 1; ay >>= 1
    v = A[max(ax, ay)][min(ax, ay)]
    e = v if ax < ay else 90 - v
    if q % 2 == 0: e = 90 - e
    return e + 90 * (0 if q == 4 else q)
def load(tag):
    R = {}
    for l in open(f"{RUNS}/{tag}/ram.log"):
        p = l.split(); R[int(p[0])] = [int(x, 16) for x in p[1:]]
    return R
def adiff(a, b): return (a - b + 540) % 360 - 180
WX, WY = [186, 174, 6, 354], [84, 276, 264, 96]
SPAWN = [(0, 0, 134), (304, 0, 180), (896, 0, 224), (592, 0, 180), (1024, 768, 314), (720, 768, 0), (128, 768, 44), (432, 768, 0)]
out = collections.defaultdict(collections.Counter); ex = collections.defaultdict(list)
for tag in TAGS:
    R = load(tag); F = sorted(R)
    first_p1 = next((f for f in F if f >= 200 and R[f][0x3A] == 0x1C), None)
    first_sp = next((f for f in F if f > 202 and R[f][0x0A] == 0x1C and R[f - 1][0x0A] == 0x01), None)
    ex["f"].append(f"{tag}: P1 word A first $01C at f{first_p1}; first enemy spawn at f{first_sp} ({first_sp - first_p1} frames later)")
    for f in F:
        if f - 1 not in R or f < 202: continue
        w, p = R[f], R[f - 1]
        for b in (0, 0x10, 0x20):
            a, pa = w[b + 0xA], p[b + 0xA]
            x, y, h, sp = s12(w[b]), s12(w[b + 1]), w[b + 5], w[b + 6]
            # (a) rounding of $1204: the move of a moving enemy equals disp(h, s) for some speed s
            # (frames with a heading change after the move, e.g. canister-contact jitter, are excluded:
            #  heading unchanged and word 4 = $50)
            if a == pa and a in (0x1C, 0x1E, 0x18) and h == p[b + 5] and w[b + 4] == 0x50:
                dx, dy = x - s12(p[b]), y - s12(p[b + 1])
                if (dx, dy) != (0, 0):
                    out["a"]["moving frames"] += 1
                    out["a"]["round-half-up fits"] += any(disp(h, s)[:2] == (dx, dy) for s in range(0, 24))
                    out["a"]["floor fits"] += any(disp(h, s, False)[:2] == (dx, dy) for s in range(0, 24))
            # (b) at canister: heading turns toward bearing(own + (-X2)>>1, own + (-Y2)>>1) of its canister
            if a == 0x14 and pa == 0x14:
                c = w[b + 2]; X2, Y2 = w[c], w[c + 1]
                t = bearing(x + ((-X2) >> 1), y + ((-Y2) >> 1))
                d0, d1 = abs(adiff(p[b + 5], t)), abs(adiff(h, t))
                out["b"]["at-canister frames"] += 1
                out["b"]["heading == target or closer"] += (d1 == 0 or d1 < d0)
            # (e) tow offset: canister = own + R(h)(0,$20)·word4/128, ×2 with taken bit
            if a == 0x1E and 0 <= x < 1024 and 0 <= y < 768:
                s_, c_, _ = sc(h, w[b + 4]); c = w[b + 2]
                pred = (((x - (s_ * 32) // 128) << 1) | 1, (y + (c_ * 32) // 128) << 1)
                out["e"]["towing frames on field"] += 1
                out["e"]["match with scale = word 4"] += pred == (w[c], w[c + 1])
            # (g) spawn pass: enemies start at a spawn point heading, then turn toward bearing(0,0) = 315
            # record $00 only: records $10/$20 sit on a lower-index enemy and take the separation path
            if pa == 0x01 and a == 0x1C and b == 0:
                sp_ = [s for s in SPAWN if abs(s[0] - x) <= 16 and abs(s[1] - y) <= 16]
                if sp_:
                    h0 = sp_[0][2]; step = (w[b + 3] >> 1) + 1
                    exp = 315 if abs(adiff(h0, 315)) < step else (h0 + (step if adiff(315, h0) > 0 else -step)) % 360
                    out["g"]["spawn passes"] += 1; out["g"]["heading = spawn heading turned toward 315"] += exp == h
        for b in (0x30, 0x40):
            a, pa = w[b + 0xA], p[b + 0xA]
            if a != 0x1C or pa != 0x1C: continue
            # (c) thrust cadence: player speed changes only in passes with A8 & 3 == 0 (frame-end A8 & 3 == 1)
            if w[b + 6] != p[b + 6]: out["c"][f"speed change at frame-end A8&3={w[0xA8] & 3}"] += 1
            # (d) wall: x clamped to 0/$3FF or y to 0/$2FF with a new heading from the tables by quarter q
            x, y, h = s12(w[b]), s12(w[b + 1]), w[b + 5]
            px_, py_ = s12(p[b]), s12(p[b + 1])
            if (x in (0, 0x3FF) and px_ != x) or (y in (0, 0x2FF) and py_ != y):
                out["d"]["frames arriving at a wall"] += 1
                if h in WX + WY: out["d"]["heading in $0FBA/$0FBE tables"] += 1
                elif disp(h, w[b + 6])[:2] == (x - px_, y - py_):
                    out["d"]["other: plain move landing exactly on the edge (no clamp)"] += 1
print("# p7_checks.py over", ", ".join(TAGS))
names = {"a": "(a) $1204 rounding", "b": "(b) at-canister turn target", "c": "(c) player thrust cadence",
         "d": "(d) player wall bounce", "e": "(e) tow offset", "f": "(f) game-start timing", "g": "(g) spawn-pass turn (atan2(0,0) = 315)"}
for k in "abcdefg":
    print(names[k])
    for kk, v in sorted(out[k].items()): print(f"  {kk}: {v}")
    for l in ex[k]: print("  " + l)
