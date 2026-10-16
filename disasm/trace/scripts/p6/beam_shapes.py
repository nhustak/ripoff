# Geometry of the four fire-line shapes selected by RAM[$ED] (= $F89/$F92/$F9B/$FA4 in the trace, region $1F89-$1FA4),
# decoded with tools/ccpu/shapes.py (ROTATED format with header pivot) from an image built locally from the MAME ROM set.
# Prints derived distances only (no ROM bytes). usage: beam_shapes.py IMAGE.bin  (image built with tools/ccpu/build_image.py, not committed)
import sys, math
sys.path.insert(0, '/workspace/ripoff-view/tools/ccpu')
import shapes
img = open(sys.argv[1], 'rb').read()
SCALE = 0x50  # enemy draw scale (word 4) in every chase frame of idle1/ram1/wander1/tp_pvp1 (16,239 frames)
for p in (0x1F89, 0x1F92, 0x1F9B, 0x1FA4):
    hdr, segs, end = shapes.rotated(img, p, True)
    pts = {a for s in segs for a in s}
    d = sorted(round(math.hypot(x - hdr[0], y - hdr[1]), 1) for x, y in pts)
    print(f'${p:04X}: {len(segs)} lines, points at {d} shape units from the pivot; '
          f'x {SCALE}/128 -> {[round(v * SCALE / 128, 1) for v in d]} field units')
