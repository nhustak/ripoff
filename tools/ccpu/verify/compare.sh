#!/bin/sh
# Verifies tools/ccpu/ccpudasm.py against MAME's own ccpudasm.cpp (unmodified,
# fetched at a pinned commit) by disassembling at every byte offset of
# (1) the Rip-Off program image and (2) a synthetic image holding every
# opcode 0x00-0xFF followed by every operand byte. Needs curl, g++, python3.
set -e
HERE=$(cd "$(dirname "$0")" && pwd)
REPO=$(cd "$HERE/../../.." && pwd)
MAME=7c11d9e0a86996c66be9ee8fa7f1cf0e7fd8d2f1
W=$(mktemp -d)
for f in ccpudasm.cpp ccpudasm.h; do
  curl -sfL -o "$W/$f" "https://raw.githubusercontent.com/mamedev/mame/$MAME/src/devices/cpu/ccpu/$f"
done
cp "$HERE/emu.h" "$HERE/harness.cpp" "$W/"
g++ -std=c++17 -O1 -I"$W" -o "$W/harness" "$W/harness.cpp" "$W/ccpudasm.cpp"
python3 "$REPO/tools/ccpu/build_image.py" "$REPO/rom" "$W/ripoff.bin" >/dev/null
python3 - "$W/synth.bin" <<'PY'
import sys
# 131072 bytes: for each opcode o and operand byte x, the pair (o, x)
open(sys.argv[1], "wb").write(bytes(b for o in range(256) for x in range(256) for b in (o, x)))
PY
for img in ripoff.bin synth.bin; do
  "$W/harness" "$W/$img" > "$W/$img.cpp.txt"
  python3 "$REPO/tools/ccpu/ccpudasm.py" "$W/$img" > "$W/$img.py.txt"
  if cmp -s "$W/$img.cpp.txt" "$W/$img.py.txt"; then
    echo "MATCH $img: $(wc -l < "$W/$img.py.txt") offsets identical (MAME $MAME)"
  else
    echo "MISMATCH $img"; diff "$W/$img.cpp.txt" "$W/$img.py.txt" | head; exit 1
  fi
done
rm -rf "$W"
