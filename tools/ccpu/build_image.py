#!/usr/bin/env python3
"""
Build the 8 KB Rip-Off "maincpu" region exactly as MAME loads it.

MAME src/mame/cinematronics/cinemat.cpp:1414-1419 (commit 7c11d9e0):
    ROM_REGION( 0x2000, "maincpu", 0 )
    ROM_LOAD16_BYTE( "ripoff.t7", 0x0000, 0x0800, ... )
    ROM_LOAD16_BYTE( "ripoff.p7", 0x0001, 0x0800, ... )
    ROM_LOAD16_BYTE( "ripoff.u7", 0x1000, 0x0800, ... )
    ROM_LOAD16_BYTE( "ripoff.r7", 0x1001, 0x0800, ... )
ROM_LOAD16_BYTE = ROMX_LOAD(..., ROM_SKIP(1))       (src/emu/romentry.h:145)
romload.cpp:850  skip += groupsize  (groupsize 1 -> stride 2)
romload.cpp:871-872  for each file byte: *base = byte; base += 2
So region[off + 2*i] = file[i].

usage: build_image.py ROMDIR OUT.bin
"""
import hashlib, sys

LOADS = [("ripoff.t7", 0x0000), ("ripoff.p7", 0x0001),
         ("ripoff.u7", 0x1000), ("ripoff.r7", 0x1001)]
SHA1 = {"ripoff.t7": "bc1f3b540475c9868443a72790a959b1f36b93c6",
        "ripoff.p7": "ea362494855be27a07014832b01e65c1645385d0",
        "ripoff.u7": "5e7672deffac1fa8f289686a5527adf7e51eb0bb",
        "ripoff.r7": "e1e2f0dfec4f53d8ff67b0e990514c304f496b3a"}


def build(romdir):
    img = bytearray(0x2000)
    for name, off in LOADS:
        data = open("%s/%s" % (romdir, name), "rb").read()
        assert len(data) == 0x800, name
        assert hashlib.sha1(data).hexdigest() == SHA1[name], name + " SHA-1 mismatch"
        for i, b in enumerate(data):
            img[off + 2 * i] = b
    return bytes(img)


if __name__ == "__main__":
    img = build(sys.argv[1])
    open(sys.argv[2], "wb").write(img)
    print("wrote %s, %d bytes, sha1 %s" % (sys.argv[2], len(img), hashlib.sha1(img).hexdigest()))
