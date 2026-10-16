# Rip-Off CCPU disassembly: first-pass notes

Status: first pass, done 2026-10-16. This is static analysis only; the ROM was never executed.

## Citation conventions

- `lst:N` means line N of `disasm/ripoff.lst`.
- `mame:path:N` means line N of that file in mamedev/mame at commit `7c11d9e0a86996c66be9ee8fa7f1cf0e7fd8d2f1`. Every MAME file used was fetched at that commit.
- Addresses are **region offsets** `$0000-$1FFF` unless the text says "CPU". Region `$1xxx` is CPU `$2xxx` (see section 1). The listing prints both.
- **UNCHECKED** marks anything inferred but not proven from the code or MAME source.

## 1. Program image and how the CCPU fetches it

- **ROM loading.** The ROM set is `ROM_REGION(0x2000,"maincpu")` with `ROM_LOAD16_BYTE` of t7@0x0000, p7@0x0001, u7@0x1000 and r7@0x1001 (mame:src/mame/cinematronics/cinemat.cpp:1414-1419).
  - `ROM_LOAD16_BYTE` is `ROMX_LOAD(..., ROM_SKIP(1))` (mame:src/emu/romentry.h:145, :99-100).
  - The loader sets the stride to `skip += groupsize` (= 2) and writes `*base = *bufptr++` per file byte (mame:src/emu/romload.cpp:850, :868-872).
  - So `region[off + 2*i] = file[i]`. `tools/ccpu/build_image.py` does exactly this. It produces `disasm/ripoff_program.bin`, 8192 bytes, SHA-1 `a7a2bb1f5f3991d5091664460cfc4c573c84bd3a`.
- **Fetch width.** The CCPU program space is 8 bits wide with 15 address bits (mame:src/devices/cpu/ccpu/ccpu.cpp:69). Opcodes are fetched one byte at a time with `READOP(m_PC)` (ccpu.cpp:25, :224-228). The byte stream is therefore t7[0], p7[0], t7[1], p7[1], ... for the first 4 KB, and u7/r7 the same way for the second.
- **Address map.** Rip-Off uses `cinemat_jmi_8k` (cinemat.cpp:1165-1167, :1051-1055) with `program_map_8k` (cinemat.cpp:280-285):
  - region `$0000-$0FFF` appears at CPU `$0000` and is mirrored at `$1000`;
  - region `$1000-$1FFF` appears at CPU `$2000` and is mirrored at `$3000`.
- **Jumps.** All jumps keep the current 4 KB page: `(m_PC-1) & 0xf000` (ccpu.cpp:307-384). `LKP` table reads stay in the same page too (ccpu.cpp:515). Only `T4K` changes page, jumping to `(P<<12)+J` (ccpu.cpp:301).
- **Cross-bank returns.** Code in region `$1xxx` returns to bank 0 with `SETP $D` + `T4K`; for example lst:4446, which follows `SETP $D` at 0x1358. `$D000 & 0x3fff` = `$1000`, which is the bank-0 mirror (cinemat.cpp:282-283).
- **RAM.** RAM is 256 12-bit words at data addresses `$00-$FF` (cinemat.cpp:300-303). The address is `I = (P<<4)+n` with `I` 8 bits wide (ccpu.cpp:398, ccpu.h:81).
- **Watchdog and frame wait.** `FRM` waits for vblank and `CST` clears the watchdog. Vblank drives `wdt_trigger`, which resets the CPU after 3 frames without a `CST` (cinemat.cpp:1034; ccpu.cpp:95-104, :582-593, :603-605). The only `FRM` site is `$062E` (`FRM FRM`), followed by `CST` at `$0630` (lst:1389-1390).

## 2. Disassembler and how it was verified

- **The port.** `tools/ccpu/ccpudasm.py` is a line-for-line port of MAME's own CCPU disassembler (mame:src/devices/cpu/ccpu/ccpudasm.cpp). MAME's core is the emulator that runs this exact ROM set, so its disassembler is the reference.
- **The check.** `tools/ccpu/verify/compare.sh` fetches the unmodified `ccpudasm.cpp` at the pinned commit and compiles it against a minimal stub `emu.h`. It then diffs the C++ output against the Python output (address, length, conditional flag, text) at:
  - every byte offset of the Rip-Off image: 8,192 offsets, **MATCH**;
  - every opcode/operand pair of a synthetic image: 131,072 offsets, **MATCH**.
- **Negative test.** A port with TST deliberately renamed to CMP fails the check with 266 differing lines, so the check isn't vacuous.
- **Known MAME quirk, handled.** `ccpudasm.cpp` gives `LKP` a length of 1 (ccpudasm.cpp:252-256), but the core does `m_PC++` after it (ccpu.cpp:518). The listing walks code with the core's length, so `LKP` shows as `E2 5F`.
- **Listing method.** `tools/ccpu/listing.py` does a static recursive descent from reset (PC=0; reset state from ccpu.cpp:173-197).
  - It tracks the active accumulator (the A/B toggling of ccpu.cpp:45-46), J, P, I, A, B, X, Y and known RAM constants as a flat constant lattice. Each instruction's effect is coded from the cited ccpu.cpp lines.
  - Result: 5,384 of 8,192 bytes are reached as code (4,648 instructions).
  - Unreached bytes are printed as `DB`.
  - The listing is deterministic: two runs produced identical output.
- **Calling convention used to find return points.** The caller loads A with the address right after its jump. For example, `$004D-$0052` loads A=`$053` and jumps to `$0C1E` (lst:80-83). The callee saves A with `STA` (`$0C1F`, lst:2806) and returns with `LPAP`+`JMP`/`T4K` (`$0CA7-$0CA9`, lst:2930-2932).
  - When A equals the jump address + 1, the listing marks a `CALL` and enters the next byte as a `RETURN POINT`. There are 22 such sites.
  - The accumulator assumed at a return point is A for a same-bank callee and B for a cross-bank (`T4K`) callee. **UNCHECKED** (inferred from ccpu.cpp:302 and :349).
- **Unresolved control flow.** 12 indirect returns stay unresolved because their J comes from a RAM slot whose value isn't known statically. They are listed under `unresolved` in `disasm/ripoff_xref.json`.
  - Targets marked `(ASSUMED)` came from a RAM slot that an indirect store might have overwritten.

## 3. Inputs (all reads found; mapping verified)

- **How INP reads.** `INP n` reads `inputs_r` bit n when the accumulator is A. When it is B, it reads port `16+(n&7)`: `switches_r` with shuffle `{2,5,4,3,0,1,6,7}`, or `coin_input_r` at `$17` (ccpu.cpp:247-250; cinemat.cpp:83-93, :111-114, :313-315). Bit names come from cinemat.cpp:705-737.
- **Active level.** Every INPUTS bit is active low (cinemat.cpp:707-717).

| Site | Read | Source |
|---|---|---|
| `$00D3` | START1 | lst:202, cinemat.cpp:708 |
| `$00E1` | START2 | lst:215, cinemat.cpp:710 |
| `$08FD` / `$08FF` / `$0901` | P2 left / P2 right / P2 BUTTON2 | lst:2058-2062, cinemat.cpp:707,709,711 |
| `$0907` / `$0909` / `$090B` | P1 left / P1 right / P1 BUTTON2 | lst:2068-2072, cinemat.cpp:714,716,717 |
| `$0AE3` / `$0AE7` | P2 BUTTON1 / P1 BUTTON1 | lst:2509, lst:2513, cinemat.cpp:712,715 |
| `$0197` | coin (`!coin_detected`) | lst:389, cinemat.cpp:111-114 |
| `$01A5`, `$01B9` | Coinage DIP bits 0x04, 0x08 | lst:401, lst:419 |
| `$0120` | Demo Sounds DIP 0x10 | lst:275 |
| `$04B8` | Scores DIP 0x20 (Individual/Combined) | lst:1112 |
| `$03F7` | Service 0x40 | lst:942 |
| `$0C27`, `$0C2F` | Lives DIP bits 0x02, 0x01 | lst:2813, lst:2822 |

- Which of BUTTON1/BUTTON2 is fire and which is thrust is not stated in MAME's ripoff port definitions. **UNCHECKED.**
- **The "Lives" DIP is a count of 4/8/12/16, and that count sizes a table copy** (lst:2807-2878):
  1. RAM[$E9] starts at 3 (lst:2810).
  2. It is decremented by 1 if DIP bit 0x02 reads 0 (lst:2813-2819) and by 2 if bit 0x01 reads 0 (lst:2822-2829). This gives index 3/2/1/0 for DIP 0x03/0x01/0x02/0x00.
  3. It becomes `4*idx+12` (lst:2839-2843), minus `$10` when the value is 17 or more (lst:2844-2851). For DIP 0x03/0x01/0x02/0x00 that yields **8/4/16/12**, exactly MAME's "Lives" labels (cinemat.cpp:720-724).
  4. It is doubled (lst:2853-2855) and used as the loop bound for copying that many bytes (N (x,y) pairs, each byte ×8 by three `SHL`s) from the table at `$0F9A` into RAM starting at `$B0` (lst:2858-2878; table at lst:3621-3626).
  5. Unused entries up to 32 bytes are filled with 1 (lst:2880-2896).
  - Interpretation: the table at `$0F9A` holds the start positions of N fuel canisters, all near (`$80`,`$60`), and the "Lives" DIP is the canister count. **UNCHECKED** interpretation; the arithmetic itself is verified.
- The 4-byte table at `$0FC2` (`15 0F 3F 1F`) is indexed by the same DIP index and stored to RAM[$78] and RAM[$7A] (lst:2831-2837, lst:3628-3629). Its meaning is **UNCHECKED**.

## 4. Outputs and sound (all OUT sites listed in `ripoff_xref.json`)

- **How OUT writes.** `OUT n` with accumulator A writes `~A & 1` to LS259 latch output Q(n&7). With accumulator B it writes nothing (ccpu.cpp:429-430; cinemat.cpp:317; mame:src/devices/machine/74259.cpp:228-242). The listing gives the written value where A is known.
- **Latch map for Rip-Off.** The sound board takes Q0-Q4 and Q7 (mask `0x9f`, mame:src/mame/cinematronics/cinemat_a.cpp:247, :59-75).

| Q | Function | MAME source |
|---|---|---|
| Q5 | coin reset (rising edge clears the coin flag) | cinemat.cpp:1027, :124-129 |
| Q6 | vector brightness: 1 = dim `0x80`, 0 = bright `0xff` | cinemat.cpp:1028; cinemat_v.cpp:54-58 |
| Q7 | netlist "Explosion" input | mame:src/mame/cinematronics/nl_ripoff.cpp:338-342 |
| Q4 | "Laser" | nl_ripoff.cpp:559-563 |
| Q3 | "Torpedo" | nl_ripoff.cpp:621-625 |
| Q0 / Q1 / Q2 | data / clock / strobe of a serial sound register | nl_ripoff.cpp:376-397 |

- **The serial sound register.**
  - Q0 goes through two 7414 inverters to the 74LS164 B input; Q1 the same way to its CLK; Q2 the same way to the 74LS377 CP (nl_ripoff.cpp:380-390; 7414 pinout mame:src/lib/netlist/macro/nlm_ttl74xx_lib.cpp:364-372).
  - The 164's QA-QF drive the 377's D0-D5 (nl_ripoff.cpp:392-397; pinouts nlm_ttl74xx_lib.cpp:2170-2179 and :2656-2668; aliases nl_cinemat_common.h:59,62).
  - The 377 outputs feed Background (Q0-Q2 to IC10 inputs, Q3 to IC22.2), "Beep" (Q4, IC9.12) and "Motor 1" (Q5, IC9.15) (nl_ripoff.cpp:403-410, :489, :523).
- **The routine that drives it, `$0155-$0194`** (lst:323-386):
  - It builds a command byte in RAM[$FC] (starts as `$1D`, lst:327-329).
  - It pulls Q2 and Q1 low (lst:356-357).
  - It then shifts **8 bits, LSB first**: `SHRB` moves B bit 0 into A bit 11, and `JMI` tests that sign bit (lst:364-371; ccpu.cpp:664-676, :87-92, :219-221). Q0 is set to the bit, then Q1 is pulsed 1→0 (lst:373-375), with a counter of 8 in RAM[$F6] (lst:358-359, :376-382).
  - Finally it raises Q2 to latch (lst:386).
  - With LSB-first shifting into a 74164, bit 7 ends up in QA and bit 2 in QF. So the command byte maps as follows (bits 1-0 are not latched): **bit7, bit6, bit5 → Background inputs; bit4 → IC22.2; bit3 → Beep; bit2 → Motor 1**. That bit order is derived from the 74164 shift behaviour in its netlist function table (nlm_ttl74xx_lib.cpp:2160-2163). Which level means "sound on" is **UNCHECKED**.
- The same routine writes Q4 = `~(RAM[$FC]>>1)&1` (lst:340-342). It writes Q3 = 1 when RAM[$FD]==0; otherwise it decrements RAM[$FD] and writes Q3 = 0 (lst:343-353). RAM[$FD] therefore acts as a countdown for the Q3 "Torpedo" line. **UNCHECKED** which level is audible.
- **Explosion triggers.** Q7 is pulsed 0 then 1 at `$0A39/$0A3D`, `$0A89/$0A8C`, `$0EF4/$0EF8` and `$1188/$118E` (lst:2356/2360, 2430/2433, 3484/3488, 4004/4010). Which game event each site is: **UNCHECKED**.
- **Coin.** `$0197` reads the coin input. If a coin was detected (input 0), Q5 is pulsed 0→1 to clear it (lst:389-398), then the Coinage DIPs are read (lst:401, :419).

## 5. Vector drawing

- **CCPU primitives.**
  - `IV` sets beam X,Y from A,B (ccpu.cpp:694-698).
  - `NV` normalizes A,B and sets the shift T (ccpu.cpp:566-579).
  - `DV` draws from (X,Y) toward (A,B) scaled by T (ccpu.cpp:483-498).
  - `DV` is used at `$0DB4`, `$0F3D`, `$1314` and `$15CF` only (ripoff_xref.json `dv`).
  - In MAME, `JVN` never branches, because `m_drflag` is only ever zeroed (ccpu.cpp:43, :193). So the `JVN` self-loops before `IV` (e.g. lst:4986) fall straight through in emulation. On hardware they presumably wait for the vector generator. **UNCHECKED.**
- **Glyph routine `$157F`** (lst:4945-5044).
  - Inputs: return address in A (saved to RAM[$A7], lst:4948), glyph number in B (RAM[$A4], lst:4950), origin in RAM[$A0]/RAM[$A1] (lst:4962-4965).
  - Pointer = `$162D + ROM[$1620+n]` (lst:4951-4960).
  - Data format, per byte:
    - two bytes: start point, moved to without drawing (lst:4969-4987);
    - `00 x y`: move (lst:4994-4997);
    - `FF`: end (lst:4998-5001);
    - otherwise `x y`: draw a line with `NV`/`DV` (lst:5002-5019).
  - Coordinates are unsigned bytes added to the origin.
  - On exit X is advanced by `$22` (lst:5034-5035).
  - Entry `$1591` takes a ready pointer (from `$13F6`, lst:4597).
  - `tools/ccpu/shapes.py` decodes the 13 glyphs (index at lst:5053-5054, data from lst:5056). Rendered PNGs are in `disasm/shapes/`. Visual check of those renders: glyphs 0-9 are the **digits 0-9**, glyph 10 is **"BONUS"**, glyph 11 is **"LEVEL"** and glyph 12 is **"+"**.
  - `$172C`/`$1735`/`$173E`/`$1747` (selected at lst:4575-4589) are four small triangles in the same format. What they are used for is **UNCHECKED**.
- **Number printer `$14BB`** (lst:4776-4892).
  - It converts RAM[$A9] to decimal by repeated subtraction of 1000, 100 and 10 (constants `$3E7`, `$63`, `9` with `TST`/`JLT`; lst:4790, :4810, :4830).
  - It draws hundreds, tens and units through the glyph routine, with leading-zero suppression (lst:4850-4888).
  - It is called twice from `$1499`/`$14A5` (lst:4743, :4755). `$14AE-$14B5` can append a literal glyph 0 (lst:4764-4769).
  - Interpretation: score display, with scores kept divided by 10. **UNCHECKED.**
- **Rotated-shape routine `$1287`/`$128C`/`$12A6`** (lst:4249-4446).
  - It reads a pointer in RAM[$A4] and passes each byte pair through the multiply routine `$135A`. That routine runs four 8-step `MUL` sequences using RAM[$A2]/RAM[$A3] as factors, which suggests a 2x2 rotation (lst:4448-4513). That interpretation and the sign convention are **UNCHECKED**.
  - Format:
    - an offset pair (only via `$1287`/`$128C`, lst:4256-4274);
    - a start pair (lst:4288-4305);
    - then `00 x y` move, `FE x y` move after `OUT $6` with A=0 (lst:4330-4333, :4404-4414);
    - `FF` end (lst:4404-4408);
    - otherwise `x y` draw (lst:4334-4382).
  - Zero-length segments are skipped (lst:4349-4370).
  - For pointers ≥ `$FAE`, Q6 brightness is toggled from the coordinate's low bit while the line is drawn (lst:4383-4393).
  - After `FF`, a following byte `01 n` moves the pointer back by n (lst:4416-4433).
  - Shapes decoded by `shapes.py`: `$1F68` (lst:5356) and `$1F89/$1F92/$1F9B/$1FA4` (lst:5362) with header; `$1FAE` (lst:5368) without.
  - Rendered shapes (visual, **UNCHECKED** identification): `$1F68` is a large notched rectangle; `$1F89`-`$1FA4` are four thin spikes of decreasing length; `$1FAE` is a starburst (an explosion is likely).
- **Tanks, saucers and fuel canisters: NOT yet identified.** Their shape data is probably in the undecoded `$1750-$1F67` region (lst:5096). That region starts with a monotonic byte ramp `00 04 09 0D 12 16 1B 1F ...`, possibly a sine table (**UNCHECKED**), and is indexed from `$104E`/`$1062` via `$17AB`/`$17EB`.

## 6. Other data

- The odd bytes `$0FD1-$0FFF` (from ripoff.p7) spell the ASCII string `(C)1980CINEMATRONICSINC.` (lst:3633-3634, decoded with `build_image.py`'s byte layout).

## 7. What is not verified yet

1. **Unresolved indirect returns.** There are 12; see `unresolved` in `ripoff_xref.json`. The unreached `$0579-$05C7` (lst:1283) and `$1542-$1555` (lst:4905) are probably code reached only through them. **UNCHECKED.**
2. **Accumulator at cross-bank return points.** The A/B choice is an inference (section 2).
3. **Sound levels and events.** Which level of each sound bit means "on", and which game event fires each sound.
4. **Shape semantics.** The rotation math, and whether shape bytes are signed.
5. **Undecoded tables.** Object shapes for tanks, saucers and canisters in `$1750-$1F67`; the tables at `$0F82`, `$0FC2` and `$15E9`.
6. **Scoring.** Point values per enemy and the bonus/level logic. Not found yet.
