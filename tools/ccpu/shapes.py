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
  The rotation math and whether bytes are signed are NOT decoded (UNCHECKED);
  this tool plots raw unsigned bytes and, separately, as signed 8-bit.

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
    open("%s/shapes.txt" % out, "w").write("\n".join(rep) + "\n")
    print("\n".join(r[:110] for r in rep))


if __name__ == "__main__":
    main()
