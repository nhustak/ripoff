# tools/ccpu: Cinematronics CCPU disassembly tools

Static analysis only. Nothing here executes the ROM.

| File | What it does |
|---|---|
| `build_image.py` | Builds the 8 KB `maincpu` region the way MAME's `ROM_LOAD16_BYTE` does, and checks every file's SHA-1. |
| `ccpudasm.py` | Line-for-line Python port of MAME's `src/devices/cpu/ccpu/ccpudasm.cpp` (mamedev/mame `7c11d9e0a86996c66be9ee8fa7f1cf0e7fd8d2f1`, BSD-3-Clause, Aaron Giles). |
| `listing.py` | Static recursive-descent listing that follows MAME's `ccpu.cpp` semantics. It writes `disasm/ripoff.lst` and `disasm/ripoff_xref.json`. |
| `shapes.py` | Decodes the vector shape tables, using the formats read off the draw routines, into `disasm/shapes/`. |
| `verify/compare.sh` | Downloads the **unmodified** MAME `ccpudasm.cpp` at the pinned commit and compiles it against a tiny stub (`verify/emu.h`, `verify/harness.cpp`). It then diffs the C++ output against `ccpudasm.py` at every byte offset of the Rip-Off image (8,192 offsets) and of a synthetic image holding every opcode/operand pair (131,072 offsets). Needs `curl`, `g++`, `python3`. |
| `regen.sh` | Rebuilds everything in `disasm/`. |

## Why MAME's disassembler

MAME's CCPU core (`ccpu.cpp`) is the emulator that actually runs this ROM set, and `ccpudasm.cpp` is the disassembler that ships with it. Porting it and diffing the port against the compiled original gives an objective, repeatable check (`verify/compare.sh` prints `MATCH`). A deliberately broken port (TST renamed to CMP) fails the check with 266 differing lines, so the check isn't vacuous.

## One difference from MAME's disassembler

`ccpudasm.cpp` reports `LKP` (`E2`/`F2`) as 1 byte long (ccpudasm.cpp:252-256). The execution core, however, skips one extra byte after `LKP` (`m_PC++`, ccpu.cpp:518). The listing walks code the way the core executes it, so it shows `LKP` with its skipped byte (e.g. `E2 5F`). The mnemonic text is still MAME's.
