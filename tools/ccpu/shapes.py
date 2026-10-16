#!/usr/bin/env python3
"""
Decode Rip-Off vector shape data from the program image, following the two
draw routines identified in disasm/ripoff.lst (see disasm/NOTES.md):

GLYPH format, drawn by the routine at region $157F (entry $1591 with a
pointer already in RAM[$A4]), listing lines for $157F-$15E3:
  x0 y0                 start point (beam moved there with IV, no line)
  then repeated:
    00 x y              move (no line) to (x,y)       ($15B2-$15B6 -> $1596)
    FF                  end of glyph                  ($15B7-$15BC -> $15DC)
    x y  (x not 00/FF)  draw line to (x,y)            ($15BD-$15CF DV)
  all coordinates are unsigned bytes added to the origin RAM[$A0],RAM[$A1].
  Glyph index table: 13 bytes at region $1620, glyph n data at $162D+table[n]
  ($1584-$1590).

ROTATED format, drawn by the routine at region $1287/$128C (entry $12A6
skips the first pair), every pair passed through the MUL routine at $135A:
  ex ey                 offset pair (only when entered at $1287/$128C)
  x0 y0                 start point
  then repeated:
    00 x y              move to (x,y)                 ($12DA-$12DC, $1331-$1335)
    FE x y              move to (x,y) after OUT $6 with A=0 ($1336)
    FF                  end                           ($132B-$1330)
    x y                 draw line                     ($12E1-$1314)
  Pass 2 decoded the math (disasm/NOTES.md section 9): bytes are UNSIGNED
  0..255; the header pair is the pivot (origin -= R(header) at $12A6-$12AC,
  origin += R(header) at $1350-$1355), so a point p is drawn at
  origin + R(angle)(p - header), R counter-clockwise, angle in degrees.
  After FF, a byte 01 n is a link: RAM[$A4] -= n ($1338-$134C); otherwise
  RAM[$A4] is left at the next frame.

  Object sprites (pass 2): bodies at RAM[$x7] (with header, $0D65-$0D76),
  animation frames at RAM[$x8] (no header, same pivot, $0DB9-$0DC2).

usage: shapes.py IMAGE.bin OUTDIR
"""
import os, sys
from PIL import Image, ImageDraw


def glyph(img, p):
    segs, cur = [], (img[p], img[p + 1]); p += 2
    while True:
        b = img[p]; p += 1
        if b == 0xff:
            return segs, p
        if b == 0x00:
            cur = (img[p], img[p + 1]); p += 2
            continue
        nxt = (b, img[p]); p += 1
        segs.append((cur, nxt)); cur = nxt


def rotated(img, p, header):
    hdr = None
    if header:
        hdr = (img[p], img[p + 1]); p += 2
    segs, cur = [], (img[p], img[p + 1]); p += 2
    while True:
        b = img[p]; p += 1
        if b == 0xff:
            return hdr, segs, p
        if b in (0x00, 0xfe):
            cur = (img[p], img[p + 1]); p += 2
            continue
        nxt = (b, img[p]); p += 1
        segs.append((cur, nxt)); cur = nxt


def rotated_segs(img, p, header):
    """Like rotated() but keeps the FE flag: returns hdr, [(a, b, dim)], end."""
    hdr = None
    if header:
        hdr = (img[p], img[p + 1]); p += 2
    segs, cur, dim = [], (img[p], img[p + 1]), False; p += 2
    while True:
        b = img[p]; p += 1
        if b == 0xff:
            return hdr, segs, p
        if b in (0x00, 0xfe):
            dim = dim or b == 0xfe        # $1336: OUT $6 with A=0 -> Q6=1 = dim
            cur = (img[p], img[p + 1]); p += 2
            continue
        nxt = (b, img[p]); p += 1
        segs.append((cur, nxt, dim)); cur = nxt


def plot_obj(layers, path, scale=6, pad=12, pivot=True):
    """layers: [(segs, colour)], segs relative to the pivot, y up (as plot())."""
    pts = [pt for segs, _ in layers for s in segs for pt in s[:2]] + ([(0, 0)] if pivot else [])
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
    minx, maxx, miny, maxy = min(xs), max(xs), min(ys), max(ys)
    im = Image.new("RGB", ((maxx - minx) * scale + 2 * pad, (maxy - miny) * scale + 2 * pad), "black")
    d = ImageDraw.Draw(im)
    f = lambda pt: (pad + (pt[0] - minx) * scale, pad + (maxy - pt[1]) * scale)
    for segs, col in layers:
        for s in segs:
            c = col if not (len(s) > 2 and s[2]) else tuple(v // 2 for v in col)
            d.line([f(s[0]), f(s[1])], fill=c, width=2)
    cx, cy = f((0, 0))
    if pivot:
        d.line([(cx - 4, cy), (cx + 4, cy)], fill=(255, 0, 0)); d.line([(cx, cy - 4), (cx, cy + 4)], fill=(255, 0, 0))
    im.save(path)


# Object sprite pointer sets, all from code (see disasm/NOTES.md section 10):
#   player records: reset code stores RAM[$37..$39] and RAM[$47..$49] (lst: $0019-$0032)
#   enemy records: RAM[$D7..$D9] from the table walk at $02D1-$0317 (6 types)
def enemy_ptrs(img):
    out = []
    for m in range(6):                       # RAM[$F5] = 1..6 (UNCHECKED range)
        i = 0xFFF - 8 * m
        b = [img[i - k] for k in range(8)]
        aa = ((((b[1] - b[0]) & 0xfff) << 4) + ((b[3] - b[2]) & 0xfff)) & 0xfff
        d7 = (aa + 0xB8D) & 0xfff
        out.append((m + 1, d7, (((b[5] - b[4]) & 0xfff) + d7) & 0xfff, (((b[7] - b[6]) & 0xfff) + d7) & 0xfff))
    return out


def frames(img, p):
    """Animation sequence starting at p (no header): list of (start, segs) until a 01 n link."""
    fr, seen = [], set()
    while p not in seen and len(fr) < 32:
        seen.add(p)
        _, segs, end = rotated_segs(img, p, False)
        fr.append((p, segs))
        if img[end] == 0x01:
            break
        p = end
    return fr


def render_objects(img, out, rep):
    objs = [("player_rec30", 0x1A8B, 0x19FB, 0x1A43), ("player_rec40", 0x1B48, 0x1ACC, 0x1B0A)]
    objs += [("enemy_type%d" % m, 0x1000 + d7, 0x1000 + d8, 0x1000 + d9) for m, d7, d8, d9 in enemy_ptrs(img)]
    for name, body, a8, a9 in objs:
        hdr, segs, end = rotated_segs(img, body, True)
        rel = lambda ss: [((a[0] - hdr[0], a[1] - hdr[1]), (b[0] - hdr[0], b[1] - hdr[1]), dm) for a, b, dm in ss]
        f8, f9 = frames(img, a8), frames(img, a9)
        plot_obj([(rel(segs), (255, 255, 255)), (rel(f8[0][1]), (0, 255, 0)), (rel(f9[0][1]), (0, 160, 255))],
                 "%s/obj_%s_%04X.png" % (out, name, body))
        plot_obj([(rel(segs), (255, 255, 255))], "%s/obj_%s_%04X_body.png" % (out, name, body))
        rep.append("object %s body $%04X-$%04X pivot %s %d lines; anim8 $%04X %d frames; anim9 $%04X %d frames"
                   % (name, body, end - 1, hdr, len(segs), a8, len(f8), a9, len(f9)))


def render_canisters(img, out, rep):
    """Canister cluster as drawn by $13A1-$13F6: entry i of the RAM $B0 table
    (from $0F9A, byte*8 at $0C5D-$0C63) is drawn as a glyph with origin
    (x/2 - $0D, y/2 - $2E) ($13C0-$13C8); glyph $173E for even i, $172C for
    odd i ($13E6-$13EF; $1735/$1747 instead when RAM[$A8] bit 1 is set,
    $13D6-$13E3). Coordinates here are screen units, y up."""
    for n in (8, 16):
        layers = []
        for i in range(n):
            x, y = img[0xF9A + 2 * i] * 8, img[0xF9B + 2 * i] * 8
            ox, oy = x // 2 - 0x0D, y // 2 - 0x2E
            g = 0x173E if i % 2 == 0 else 0x172C
            segs, _ = glyph(img, g)
            layers.append(([((a[0] + ox, a[1] + oy), (b[0] + ox, b[1] + oy)) for a, b in segs], (255, 255, 0)))
        plot_obj(layers, "%s/canisters_%d.png" % (out, n), scale=3, pivot=False)
        rep.append("canisters: first %d entries of $0F9A drawn with $173E/$172C -> canisters_%d.png" % (n, n))
    for g in (0x172C, 0x1735, 0x173E, 0x1747):
        segs, _ = glyph(img, g)
        plot_obj([(segs, (255, 255, 0))], "%s/canister_glyph_%04X.png" % (out, g), scale=8, pivot=False)


def s8(v):
    return v - 256 if v & 0x80 else v


def plot(segs, path, conv=lambda v: v, scale=3):
    pts = [conv(c) for s in segs for pt in s for c in pt]
    xs = [conv(pt[0]) for s in segs for pt in s]
    ys = [conv(pt[1]) for s in segs for pt in s]
    minx, maxx, miny, maxy = min(xs), max(xs), min(ys), max(ys)
    W, H = (maxx - minx) * scale + 20, (maxy - miny) * scale + 20
    im = Image.new("RGB", (max(W, 40), max(H, 40)), "black")
    d = ImageDraw.Draw(im)
    for (a, b) in segs:
        # screen Y flipped: the game is ORIENTATION_FLIP_Y (cinemat.cpp:1633)
        f = lambda pt: (10 + (conv(pt[0]) - minx) * scale, 10 + (maxy - conv(pt[1])) * scale)
        d.line([f(a), f(b)], fill="white", width=2)
    im.save(path)


def main():
    img = open(sys.argv[1], "rb").read()
    out = sys.argv[2]
    os.makedirs(out, exist_ok=True)
    rep = []
    tab = [img[0x1620 + i] for i in range(13)]
    for n, o in enumerate(tab):
        p = 0x162D + o
        segs, end = glyph(img, p)
        plot(segs, "%s/glyph%02d_%04X.png" % (out, n, p))
        rep.append("glyph %2d  $%04X-$%04X  %d lines  %s" % (n, p, end - 1, len(segs), segs))
    for p in (0x172C, 0x1735, 0x173E, 0x1747):   # pointers set at $13DC/$13E2 (+$12 at $13EC)
        segs, end = glyph(img, p)
        plot(segs, "%s/glyphptr_%04X.png" % (out, p))
        rep.append("glyph@  $%04X-$%04X  %d lines  %s" % (p, end - 1, len(segs), segs))
    for p, hdr in ((0x1F68, True), (0x1F89, True), (0x1F92, True), (0x1F9B, True), (0x1FA4, True), (0x1FAE, False)):
        h, segs, end = rotated(img, p, hdr)
        plot(segs, "%s/rot_%04X_unsigned.png" % (out, p))
        plot(segs, "%s/rot_%04X_signed.png" % (out, p), conv=s8)
        rep.append("rotated $%04X-$%04X  header %s  %d lines  %s" % (p, end - 1, h, len(segs), segs))
    render_objects(img, out, rep)
    render_canisters(img, out, rep)
    open("%s/shapes.txt" % out, "w").write("\n".join(rep) + "\n")
    print("\n".join(r[:110] for r in rep))


if __name__ == "__main__":
    main()
