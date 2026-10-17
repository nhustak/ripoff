# Rip-Off CCPU disassembly: notes (passes 1-5)

Status: first pass and second decode pass, both done 2026-10-16. Passes 1-4 are static analysis only. Pass 5 (section 18) ran the ROM in MAME to settle the enemy state machine. Line numbers (`lst:N`) refer to the listing as regenerated in pass 2; pass-1 citations were remapped by address.

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
- **Cross-bank returns.** Code in region `$1xxx` returns to bank 0 with `SETP $D` + `T4K`; for example lst:4509, which follows `SETP $D` at 0x1358. `$D000 & 0x3fff` = `$1000`, which is the bank-0 mirror (cinemat.cpp:282-283).
- **RAM.** RAM is 256 12-bit words at data addresses `$00-$FF` (cinemat.cpp:300-303). The address is `I = (P<<4)+n` with `I` 8 bits wide (ccpu.cpp:398, ccpu.h:81).
- **Watchdog and frame wait.** `FRM` waits for vblank and `CST` clears the watchdog. Vblank drives `wdt_trigger`, which resets the CPU after 3 frames without a `CST` (cinemat.cpp:1034; ccpu.cpp:95-104, :582-593, :603-605). The only `FRM` site is `$062E` (`FRM FRM`), followed by `CST` at `$0630` (lst:1450-1451).

## 2. Disassembler and how it was verified

- **The port.** `tools/ccpu/ccpudasm.py` is a line-for-line port of MAME's own CCPU disassembler (mame:src/devices/cpu/ccpu/ccpudasm.cpp). MAME's core is the emulator that runs this exact ROM set, so its disassembler is the reference.
- **The check.** `tools/ccpu/verify/compare.sh` fetches the unmodified `ccpudasm.cpp` at the pinned commit and compiles it against a minimal stub `emu.h`. It then diffs the C++ output against the Python output (address, length, conditional flag, text) at:
  - every byte offset of the Rip-Off image: 8,192 offsets, **MATCH**;
  - every opcode/operand pair of a synthetic image: 131,072 offsets, **MATCH**.
- **Negative test.** A port with TST deliberately renamed to CMP fails the check with 266 differing lines, so the check isn't vacuous.
- **Known MAME quirk, handled.** `ccpudasm.cpp` gives `LKP` a length of 1 (ccpudasm.cpp:252-256), but the core does `m_PC++` after it (ccpu.cpp:518). The listing walks code with the core's length, so `LKP` shows as `E2 5F`.
- **Listing method.** `tools/ccpu/listing.py` does a static recursive descent from reset (PC=0; reset state from ccpu.cpp:173-197).
  - It tracks the active accumulator (the A/B toggling of ccpu.cpp:45-46), J, P, I, A, B, X, Y and known RAM constants as a flat constant lattice. Each instruction's effect is coded from the cited ccpu.cpp lines.
  - Result: 5,483 of 8,192 bytes are reached as code (4,732 instructions); pass 1 had 5,384 / 4,648 (section 8).
  - Unreached bytes are printed as `DB`.
  - The listing is deterministic: two runs produced identical output.
- **Calling convention used to find return points.** The caller loads A with the address right after its jump. For example, `$004D-$0052` loads A=`$053` and jumps to `$0C1E` (lst:80-83). The callee saves A with `STA` (`$0C1F`, lst:2867) and returns with `LPAP`+`JMP`/`T4K` (`$0CA7-$0CA9`, lst:2991-2993).
  - When A equals the jump address + 1, the listing marks a `CALL` and enters the next byte as a `RETURN POINT`. There are 22 such sites.
  - The accumulator assumed at a return point is A for a same-bank callee and B for a cross-bank (`T4K`) callee. **UNCHECKED** (inferred from ccpu.cpp:302 and :349).
- **Unresolved control flow.** Pass 1 left 12 indirect returns unresolved; pass 2 resolves 5 and leaves 7 (section 8). They are listed under `unresolved` / `rd_open` in `disasm/ripoff_xref.json`.
  - Targets marked `(ASSUMED)` came from a RAM slot that an indirect store might have overwritten.

## 3. Inputs (all reads found; mapping verified)

- **How INP reads.** `INP n` reads `inputs_r` bit n when the accumulator is A. When it is B, it reads port `16+(n&7)`: `switches_r` with shuffle `{2,5,4,3,0,1,6,7}`, or `coin_input_r` at `$17` (ccpu.cpp:247-250; cinemat.cpp:83-93, :111-114, :313-315). Bit names come from cinemat.cpp:705-737.
- **Active level.** Every INPUTS bit is active low (cinemat.cpp:707-717).

| Site | Read | Source |
|---|---|---|
| `$00D3` | START1 | lst:202, cinemat.cpp:708 |
| `$00E1` | START2 | lst:215, cinemat.cpp:710 |
| `$08FD` / `$08FF` / `$0901` | P2 left / P2 right / P2 BUTTON2 | lst:2119-2123, cinemat.cpp:707,709,711 |
| `$0907` / `$0909` / `$090B` | P1 left / P1 right / P1 BUTTON2 | lst:2129-2133, cinemat.cpp:714,716,717 |
| `$0AE3` / `$0AE7` | P2 BUTTON1 / P1 BUTTON1 | lst:2570, lst:2574, cinemat.cpp:712,715 |
| `$0197` | coin (`!coin_detected`) | lst:389, cinemat.cpp:111-114 |
| `$01A5`, `$01B9` | Coinage DIP bits 0x04, 0x08 | lst:401, lst:419 |
| `$0120` | Demo Sounds DIP 0x10 | lst:275 |
| `$04B8` | Scores DIP 0x20 (Individual/Combined) | lst:1112 |
| `$03F7` | Service 0x40 | lst:942 |
| `$0C27`, `$0C2F` | Lives DIP bits 0x02, 0x01 | lst:2874, lst:2883 |

- Which of BUTTON1/BUTTON2 is fire and which is thrust is not stated in MAME's ripoff port definitions. Pass 2 settles it from the code: **BUTTON1 = fire, BUTTON2 = thrust** (section 11.2).
- **The "Lives" DIP is a count of 4/8/12/16, and that count sizes a table copy** (lst:2868-2939):
  1. RAM[$E9] starts at 3 (lst:2871).
  2. It is decremented by 1 if DIP bit 0x02 reads 0 (lst:2874-2880) and by 2 if bit 0x01 reads 0 (lst:2883-2890). This gives index 3/2/1/0 for DIP 0x03/0x01/0x02/0x00.
  3. It becomes `4*idx+12` (lst:2900-2904), minus `$10` when the value is 17 or more (lst:2905-2912). For DIP 0x03/0x01/0x02/0x00 that yields **8/4/16/12**, exactly MAME's "Lives" labels (cinemat.cpp:720-724).
  4. It is doubled (lst:2914-2916) and used as the loop bound for copying that many bytes (N (x,y) pairs, each byte ×8 by three `SHL`s) from the table at `$0F9A` into RAM starting at `$B0` (lst:2919-2939; table at lst:3684-3689).
  5. Unused entries up to 32 bytes are filled with 1 (lst:2941-2957).
  - Interpretation: the table at `$0F9A` holds the start positions of N fuel canisters, and the "Lives" DIP is the canister count. Pass 2 found the draw loop for this table (triangles, section 10.3), which supports the reading.
- The 4-byte table at `$0FC2` (`15 0F 3F 1F`) is indexed by the same DIP index and stored to RAM[$78] and RAM[$7A] (lst:2892-2898, lst:3691-3692). Its use as the background-sound step is decoded in section 15.

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
- **Explosion triggers.** Q7 is pulsed 0 then 1 at `$0A39/$0A3D`, `$0A89/$0A8C`, `$0EF4/$0EF8` and `$1188/$118E` (lst:2417/2421, 2430/2433, 3484/3488, 4004/4010). Which game event each site is: **UNCHECKED**.
- **Coin.** `$0197` reads the coin input. If a coin was detected (input 0), Q5 is pulsed 0→1 to clear it (lst:389-398), then the Coinage DIPs are read (lst:401, :419).

## 5. Vector drawing

- **CCPU primitives.**
  - `IV` sets beam X,Y from A,B (ccpu.cpp:694-698).
  - `NV` normalizes A,B and sets the shift T (ccpu.cpp:566-579).
  - `DV` draws from (X,Y) toward (A,B) scaled by T (ccpu.cpp:483-498).
  - `DV` is used at `$0DB4`, `$0F3D`, `$1314` and `$15CF` only (ripoff_xref.json `dv`).
  - In MAME, `JVN` never branches, because `m_drflag` is only ever zeroed (ccpu.cpp:43, :193). So the `JVN` self-loops before `IV` (e.g. lst:5064) fall straight through in emulation. On hardware they presumably wait for the vector generator. **UNCHECKED.**
- **Glyph routine `$157F`** (lst:5023-5122).
  - Inputs: return address in A (saved to RAM[$A7], lst:5026), glyph number in B (RAM[$A4], lst:5028), origin in RAM[$A0]/RAM[$A1] (lst:5040-5043).
  - Pointer = `$162D + ROM[$1620+n]` (lst:5029-5038).
  - Data format, per byte:
    - two bytes: start point, moved to without drawing (lst:5047-5065);
    - `00 x y`: move (lst:5072-5075);
    - `FF`: end (lst:5076-5079);
    - otherwise `x y`: draw a line with `NV`/`DV` (lst:5080-5097).
  - Coordinates are unsigned bytes added to the origin.
  - On exit X is advanced by `$22` (lst:5112-5113).
  - Entry `$1591` takes a ready pointer (from `$13F6`, lst:4660).
  - `tools/ccpu/shapes.py` decodes the 13 glyphs (index at lst:5131-5132, data from lst:5134). Rendered PNGs are in `disasm/shapes/`. Visual check of those renders: glyphs 0-9 are the **digits 0-9**, glyph 10 is **"BONUS"**, glyph 11 is **"LEVEL"** and glyph 12 is **"+"**.
  - `$172C`/`$1735`/`$173E`/`$1747` (selected at lst:4638-4652) are four small triangles in the same format. Pass 2: they are the canister glyphs drawn for the RAM `$B0` table (section 10.3).
- **Number printer `$14BB`** (lst:4839-4955).
  - It converts RAM[$A9] to decimal by repeated subtraction of 1000, 100 and 10 (constants `$3E7`, `$63`, `9` with `TST`/`JLT`; lst:4853, :4812, :4832).
  - It draws hundreds, tens and units through the glyph routine, with leading-zero suppression (lst:4913-4951).
  - It is called twice from `$1499`/`$14A5` (lst:4806, :4757). `$14AE-$14B5` can append a literal glyph 0 (lst:4827-4832).
  - Interpretation: score display, with scores kept divided by 10. **UNCHECKED.**
- **Rotated-shape routine `$1287`/`$128C`/`$12A6`** (lst:4312-4509).
  - It reads a pointer in RAM[$A4] and passes each byte pair through the multiply routine `$135A`. That routine runs four 8-step `MUL` sequences using RAM[$A2]/RAM[$A3] as factors, which suggests a 2x2 rotation (lst:4511-4576). Pass 2 decoded it as a counter-clockwise 2x2 rotation (section 9.4).
  - Format:
    - an offset pair (only via `$1287`/`$128C`, lst:4319-4337);
    - a start pair (lst:4351-4368);
    - then `00 x y` move, `FE x y` move after `OUT $6` with A=0 (lst:4393-4396, :4406-4414);
    - `FF` end (lst:4467-4471);
    - otherwise `x y` draw (lst:4397-4445).
  - Zero-length segments are skipped (lst:4412-4433).
  - For pointers ≥ `$FAE`, Q6 brightness is toggled from the coordinate's low bit while the line is drawn (lst:4446-4456).
  - After `FF`, a following byte `01 n` moves the pointer back by n (lst:4479-4496).
  - Shapes decoded by `shapes.py`: `$1F68` (lst:5440) and `$1F89/$1F92/$1F9B/$1FA4` (lst:5446) with header; `$1FAE` (lst:5452) without.
  - Rendered shapes (visual, **UNCHECKED** identification): `$1F68` is a large notched rectangle; `$1F89`-`$1FA4` are four thin spikes of decreasing length; `$1FAE` is a starburst (an explosion is likely).
- **Tanks, enemy tanks and fuel canisters:** identified in pass 2 (section 10). `$1750-$19FA` are trig tables (section 9) and `$19FB-$1F67` is the object sprite stream.

## 6. Other data

- The odd bytes `$0FD1-$0FFF` (from ripoff.p7) spell the ASCII string `(C)1980CINEMATRONICSINC.` (lst:3696-3697, decoded with `build_image.py`'s byte layout).

## 7. What is not verified yet (pass 1 list; see section 12 for the current list)

1. **Unresolved indirect returns.** There are 12; see `unresolved` in `ripoff_xref.json`. The unreached `$0579-$05C7` (lst:1284) and `$1542-$1555` (lst:4969) are probably code reached only through them. **UNCHECKED.**
2. **Accumulator at cross-bank return points.** The A/B choice is an inference (section 2).
3. **Sound levels and events.** Which level of each sound bit means "on", and which game event fires each sound.
4. **Shape semantics.** The rotation math, and whether shape bytes are signed.
5. **Undecoded tables.** Object shapes for tanks, saucers and canisters in `$1750-$1F67`; the tables at `$0F82`, `$0FC2` and `$15E9`.
6. **Scoring.** Point values per enemy and the bonus/level logic. Not found yet.

## 8. Pass 2: return resolution and coverage

- **Context limit raised.** `listing.py` keeps states apart per value of the LPAP return slots (`pkey`). Pass 1 allowed 16 contexts per address (`KPART`); joining beyond that lost saved return addresses. One example: the `E7=$579` context set by `$056E` (lst:1274) entered the glyph routine at `$157F` but was joined away at `$1580`. With `KPART = 64`:
  - `$1286` resolves (lst:4311);
  - `$0579-$05C7` becomes code (lst:1284-1353), reached from `$15E8` (lst:5122);
  - `$1542-$1555` becomes code (lst:4969-4985), reached from `$15E3` (lst:5116).
  - Raising `KPART` to 256 gave the same coverage (5,483 bytes) and took 55 s against 21 s.
- **Reaching definitions for return slots** (`_reaching_returns` in `tools/ccpu/listing.py`).
  - How it works: for each unresolved `JMP`/`T4K` whose J came from `LPAP` of RAM[S], it walks the control-flow graph backwards to every store to S. A `CALL` return point is passed straight back to its call site, unless the callee body also stores to S.
  - A site counts as **resolved** only when every reaching definition is a constant and no path reaches reset without a store. Only then are its targets added to the listing (`RD (may-set, context-insensitive)` notes; `rd_return_targets` in the xref JSON).
  - RD targets are **not** used as new analysis entries. When they were, unrelated uses of the same slot brought in bogus targets such as `L0000`/`L0001`: RAM[$E5] is also the angle variable of `$1204` (lst:4190).
- **Result: 12 → 7 open.** 5,384 → 5,483 code bytes; 4,648 → 4,732 instructions.

  | site | slot | state | evidence |
  |---|---|---|---|
  | `$1286` | E7 | resolved by context analysis | lst:4311 |
  | `$1203` | E7 | resolved by RD (6 targets, one store each) | lst:4186 |
  | `$139B` | AB | resolved by RD (6 targets) | lst:4576 |
  | `$140A` | F7 | resolved by RD (`L0458`, store `$13A3`) | lst:4679 |
  | `$14BA` | E7 | resolved by RD (5 targets) | lst:4838 |
  | `$0457` | - | open: T4K with P unknown in some states (target `L13A1` is known) | lst:1024 |
  | `$0C0A` | E5 | open: E5 is reused as data | lst:2846 |
  | `$0CA9` | E7 | open: callee saves an unknown A at `$0C1F` | lst:2993 |
  | `$0CC5` | E7 | open: unknown A saved at `$032D`/`$0E40` | lst:3021 |
  | `$1535` | AB | open: unknown A saved at `$14BE` | lst:4955 |
  | `$15E3` | A7 | open: unknown A saved at `$1581` (glyph routine entry) | lst:5116 |
  | `$15E8` | E7 | open: two-level return, see below | lst:5122 |

- **Two-level return seen in the glyph routine** (lst:5023-5122).
  - Bank-0 callers store the real return address in RAM[$E7] first, e.g. `$056E` stores `$579` (lst:1274). They then enter `$157F` with A=`$5E4`.
  - The routine saves A to RAM[$A7] (`$1581`).
  - It returns with `JMP` through A7 to `$15E4`, which does `LPAP` E7, `SETP $D`, `T4K` back to bank 0 (lst:5119-5122).
  - Bank-1 callers pass A=`$533` and come back at `$1533` (lst:4952).
  - The open sites above are all "callee saves the caller's A" cases where the A value was lost at a join. Their listed targets are correct but may be incomplete. **UNCHECKED** whether more targets exist.

## 9. Pass 2: trig tables and the rotation math (`$1750-$19FA`, `$1000`, `$1204`, `$135A`)

- **MUL semantics** (ccpu.cpp:522-563).
  - With acc A, each step shifts B:A right (B arithmetically). If the old A bit 0 was 1, it adds M = RAM[I] to B.
  - After 8 steps from B=0: **B = M·(A & $FF) / 128**, truncated per step.
  - The multiplier (A's low byte) is **unsigned**. M is a **signed** 12-bit value.
- **9.1 Sine table `$1750-$17AA`** (91 bytes, lst:5174).
  - The byte at `$1750+k` equals `min(255, round(256·sin(k°)))` for k = 0..90. Checked numerically against every entry; the only ±1 differences are at k=38 (159 vs 158) and k=70 (240 vs 241).
  - Read by `LKP` at `$1229` (index `$750+φ`) and `$1247` (index `$7AA−φ`, i.e. cos) (lst:4221, :4249).
- **9.2 `$1204` = scaled sin/cos** (lst:4188-4311, page E throughout).
  - Inputs: B = angle → RAM[$E5], A = scale → RAM[$E4] (lst:4190-4191).
  - It subtracts 360 once if angle ≥ 360 (`$E98` = −360, lst:4192-4198).
  - It counts whole 90° steps into RAM[$E6] (lst:4203-4215).
  - RAM[$E2] = table[φ]·scale, via 8 `MUL` + `SHRB` (lst:4217-4243). RAM[$E3] = table[90−φ]·scale (lst:4245-4261). A step compares the low word with `$800`, apparently for rounding; the direction of that compare is **UNCHECKED**.
  - Quadrant fix-up (lst:4274-4309):
    - E2/E3 are swapped when the quarter count is odd;
    - E2 is negated for q ≥ 2 (compare polarity **UNCHECKED**);
    - E3 is negated exactly for q = 1 or q = 2.
  - These match the trig identities, so the result is **RAM[$E2] = scale·sin(angle), RAM[$E3] = scale·cos(angle)**, with angle in **degrees, 0..359**.
  - Return: B = E2, A = E3 (lst:4306-4307), then `LPAP` E7, `SETP $D`, `T4K` (lst:4309-4311).
- **9.3 `$1000` = atan2 in degrees** (lst:3705-3854, page E).
  - Inputs: A = dx → RAM[$E0], B = dy → RAM[$E1] (lst:3707-3708).
  - It takes absolute values with sign flags in RAM[$E6], and scales both down until they fit the table.
  - It orders max/min into RAM[$E9]/RAM[$EA] (lst:3767-3775).
  - Row-offset table `$17AB` (lst:5187): 32 two-byte entries, `(b0<<4)+b1` = n(n+1)/2 (checked for all 32). Read at `$1054`/`$105E` (lst:3785, :3794).
  - Triangular table `$17EB-$19FA` (lst:5197), read at `$1068` (lst:3802): entry [max][min] = 90 − atan(min/max) in degrees. All 527 entries are within 1.6° of that value; 4 differ by more than 1°.
  - Then `$5A − v` is applied depending on a flag bit (lst:3813-3820), and 90 is added per quadrant (lst:3824-3846). The result goes to **RAM[$E4]** (lst:3847).
  - Callers:
    - `$06DF`: target via the pointer in RAM[$D2] (lst:1589-1609);
    - `$071C`;
    - `$0797`: dx,dy = (player record x,y) − (own x,y), with the player record at page 3 or page 4 (lst:1760-1784).
  - The exact flag and quadrant logic, and whether its zero direction matches `$1204`'s, are **UNCHECKED**. The table contents are verified.
- **9.4 `$135A` = rotation** (lst:4511-4576, page A).
  - Inputs: x → RAM[$A5], y → RAM[$A6] (lst:4513-4514). RAM[$A2] = scale·sin and RAM[$A3] = scale·cos are stored by `$0D5F-$0D63` from `$1204`'s result (lst:3159-3163).
  - It computes y·s → RAM[$AA], x·c − y·s, y·c, and x·s + y·c (lst:4515-4573).
  - Returns **A = (x·c − y·s)/128, B = (x·s + y·c)/128** through RAM[$AB].
  - That is a **counter-clockwise rotation (x right, y up) with scale/128** (scale = RAM[$D4] of the object, lst:3150-3157; players use 100, set at `$08EC-$08EE`).
  - **Shape bytes are unsigned** (they are the MUL multiplier).
  - **Pivot.** The first pair of a shape with a header is rotated (lst:4319-4337). `$12A6` subtracts the rotated pair from the origin RAM[$A0]/[$A1] (lst:4343-4349) and `$1350-$1355` adds it back (lst:4500-4505). So point p is drawn at `origin + R(angle)·(p − header)`.

## 10. Pass 2: object sprites (`$19FB-$1F67`) and their owners

- **Stream format.** Same as the rotated format of section 5, plus links:
  - after `FF`, a byte `01 n` makes RAM[$A4] −= n (`$1338-$134C`, lst:4479-4496);
  - any other byte leaves RAM[$A4] at the next frame.
  - Bodies have a header (pivot) and are drawn from RAM[$D7] through `$1287` (`$0D65-$0D76`, lst:3165-3180).
  - Headerless frame loops are drawn from RAM[$D8] through `$12A6` (`$0DB9-$0DC2`, lst:3245-3252), reusing the body's pivot (RAM[$AE]/[$AF]). Whether `$0D77-$0DB8` leaves AE/AF untouched is **UNCHECKED**.
  - `$0DC3-$0DC7` saves the advanced pointer to RAM[$E9] (lst:3254-3258). Where it is written back to the record is **UNCHECKED**.
- **10.1 Player tanks** (code-supported).
  - Reset code stores (lst:38-57):
    - RAM[$37] = `$A8B`, RAM[$38] = `$9FB`, RAM[$39] = `$A43`;
    - RAM[$47] = `$B48`, RAM[$49] = `$B0A`, RAM[$48] = `$ACC`.
    - These are bank-1 pointers: region `$1A8B`, `$19FB`, ...
  - Object records are 16 words. `$0CAA-$0CC5` copies 16 words from the source base RAM[$EF] to the destination (`$D0` from `$08C6-$08CF`) (lst:2995-3021, lst:2071-2078). So offsets 7/8/9 of a record become RAM[$D7]/[$D8]/[$D9].
  - The input routine reads **P1** controls when RAM[$EF] < `$40` and **P2** controls otherwise (`$08F8-$090C`, lst:2115-2134).
  - Therefore **record `$30` = player 1, body `$1A8B`** (pivot (26,38), 24 lines) with tread frames `$19FB`/`$1A43`. **Record `$40` = player 2, body `$1B48`** (pivot (26,32), 26 lines) with frames `$1ACC`/`$1B0A`.
  - Start positions and angles (`$03C6-$03EC`, lst:899-932):
    - P1 at (`$400`,`$140`), angle 270;
    - P2 at (0,`$1C0`), angle 90.
  - With the heading formula of 11.1 that sends P1 left from the right edge and P2 right from the left edge, a consistency check of 9.2 and 11.1.
  - Whether the pointers are rewritten later in the game is **UNCHECKED**.
- **10.2 Enemy tanks** (six types; code-supported).
  - `$02D1-$0317` (lst:679-740) builds an enemy record in page D. With m = RAM[$F5]−1, it reads 8 bytes b0..b7 at `$0FFF−8m−k` with `LKP` (bank-0 page, ccpu.cpp:515).
  - It sets:
    - RAM[$D7] = `$B8D` + (((b1−b0)<<4) + (b3−b2));
    - RAM[$D8] = RAM[$D7] + (b5−b4);
    - RAM[$D9] = RAM[$D7] + (b7−b6).
  - These bytes are the copyright string region `$0FD0-$0FFF` (lst:3696-3697), so the enemy pointers are derived from the copyright text. Evaluated for m = 0..5:

    | RAM[$F5] | body | frames D8 | frames D9 |
    |---|---|---|---|
    | 1 | `$1CDD` | `$1D0D` | `$1D46` |
    | 2 | `$1B8D` | `$1BBD` | `$1C0A` |
    | 3 | `$1E44` | `$1E6D` | `$1EA1` |
    | 4 | `$1ED5` | `$1F00` | `$1F34` |
    | 5 | `$1C52` | `$1C75` | `$1CA9` |
    | 6 | `$1DB6` | `$1DEB` | `$1E1A` |

  - All six bodies have pivot (22,42), and they are exactly the remaining headed shapes of the stream.
  - The record is then copied out by `$0CAA` to RAM[$E6]−`$10` (`$0318-$0338`, lst:741-766).
  - These are the enemy ("saucer"/pirate) tanks. In this ROM they are tank-shaped, with treads.
  - **UNCHECKED:** the range of RAM[$F5] (assumed 1..6; 7 and 8 would give junk pointers), how it relates to wave number, and the enemy record base addresses.
- **10.3 Fuel canisters** (code-supported).
  - `$13A1-$13FF` (lst:4584-4662) walks the RAM `$B0` pair table that `$0C38-$0C6E` filled from `$0F9A` (count from the "Lives" DIP, section 3).
  - It skips an entry whose y has bit 0 set (`$13BE-$13BF`, lst:4612).
  - It draws each remaining entry as a triangle glyph at (x/2−`$0D`, y/2−`$2E`) (lst:4613-4624): `$173E` for even entries and `$172C` for odd ones (lst:4647-4654). When RAM[$A8] bit 1 is set it uses the smaller `$1735`/`$1747` instead (lst:4633-4644); what RAM[$A8] is, is **UNCHECKED**.
  - So the cluster of N triangles at screen centre (table ×4 ≈ (456-568, 328-440)) is the canister set. The DIP named "Lives" in MAME is the canister count.
- **Renders** (`tools/ccpu/shapes.py`, `disasm/shapes/`). All renders are at angle 0, pivot at the red cross, y up, the same transform that renders the digit glyphs upright.
  - `obj_player_rec30_1A8B.png`, `obj_player_rec40_1B48.png`, `obj_enemy_typeN_*.png`: body in white, lines after `FE` (dim, Q6=1 at `$1336`, lst:4476) in grey, first frame of D8 in green and of D9 in blue. `_body.png` files show the body alone.
  - `canisters_8.png` and `canisters_16.png` show the cluster; `canister_glyph_*.png` show the four triangle glyphs.
  - At angle 0 the movement heading is −y (11.1), so the sprites face down in these PNGs.

## 11. Pass 2: controls and the player record

- **11.1 Turning and thrust** (`$08F5-$09B0`, lst:2112-2290).
  - The code packs left, right and BUTTON2 into RAM[$E6] bits 0..2 with `INP`+`MUL` (lst:2119-2137). Bits are active low (cinemat.cpp:707-717).
  - Left adds the turn step RAM[$DE] to angle RAM[$D5]; right subtracts it (lst:2183-2195). The step ramps up to 8 while turning (lst:2157-2171).
  - The angle is wrapped to 0..359 (lst:2199-2217).
  - **BUTTON2 held** raises speed RAM[$D6] by 1 up to `$0A`. Released, it decays by 1 to 0 (lst:2226-2254).
  - Then `$1204` is called with angle−90 and scale = speed (lst:2256-2279). x += scale·cos(angle−90) and y += scale·sin(angle−90) (lst:2281-2287), i.e. heading = (sin θ, −cos θ).
  - **BUTTON2 = thrust.**
- **11.2 Fire** (`$0ADB-$0B3B`, lst:2560-2651).
  - BUTTON1 (P1 bit 13, P2 bit 5, chosen by RAM[$EF] < `$40`) is edge-detected against RAM[$D2] (lst:2569-2582).
  - On a press it looks for a free slot (first word bit 0 set) among 4 four-word slots at record+`$20`..+`$2C` (lst:2584-2602). That is P1 shots in `$50-$5F` and P2 shots in `$60-$6F`.
  - It initialises the slot from the tank position and sets its velocity by `$1204` with angle−90 and scale = speed+15 (lst:2606-2651).
  - **BUTTON1 = fire.**
- **Player record layout so far** (offsets from `$30`/`$40`, via the `$D0` work copy):

  | offset | meaning | evidence |
  |---|---|---|
  | 0, 1 | x, y | lst:899-932, lst:2283-2287 |
  | 2 | last BUTTON1 state | lst:2578 |
  | 4 | draw scale (100) | lst:2105-2106 |
  | 5 | angle, in degrees | lst:2217 |
  | 6 | speed | lst:2226-2254 |
  | 7, 8, 9 | body, frame pointers | 10.1 |
  | E | turn step | lst:2157-2171 |

  Everything else is **UNCHECKED**.
- **Enemy behaviour (start).** `$0787-$0797` computes atan2 from the enemy to a player record, page 3 or 4, chosen at `$0780-$0786` (lst:1760-1784). `$06C8-$06DF` does the same toward the object addressed by RAM[$D2] (lst:1586-1609). What the result is compared with (from `$109C`, lst:3848) is **UNCHECKED**.

## 12. What is still not verified (after pass 2)

1. 7 open indirect returns (section 8 table).
2. The accumulator at cross-bank return points (section 2).
3. Sound levels and events (section 4).
4. The `$1204` rounding compare and q ≥ 2 compare polarity; the `$1000` flag/quadrant logic and whether its zero direction matches `$1204`.
5. Whether RAM[$AE]/[$AF] survive between the body and frame draws, and how the frame pointer is written back; when tread frames advance.
6. The range of RAM[$F5] (enemy type); enemy record addresses; how enemies choose targets; enemy shots.
7. The meaning of RAM[$A8] (canister glyph size), the `$0FC2` table, `$0F82`, `$15E9`, and shapes `$1F68`-`$1FAE`.
8. **Scoring.** Not mapped yet: point values, the bonus/level logic and where the score RAM is (the `$14BB` printer reads RAM[$A9]).

## 13. Pass 3: scoring

Comparison semantics used below: `TST $n` + `JLT` branches when RAM[n] < accumulator, unsigned 12-bit (ccpu.cpp:445-457, :34, :40, :365-367). The `SHR...JOS` bit tests use the A0 flag, i.e. the A value before the last `SHR` or `A4I` (ccpu.cpp:33, :52-59).

- **Score storage.** Each score is kept in two words: thousands, and units below 1000. Both count in **tens of points**.
  - **Player 1** (record `$30`): units RAM[$F1], thousands RAM[$FE].
  - **Player 2** (record `$40`): units RAM[$F2], thousands RAM[$FF].
  - Both are zeroed at reset (lst:20-23) and at game start (lst:246-249).
  - Carry: while units ≥ 1000 (`$3E8`), subtract 1000 (`+$C18`) and add 1 to the thousands word (lst:1052-1080).
- **Which player scores.**
  - **Shot kill:** the shot slot pointer RAM[$A4] < `$60` (P1's slots `$50-$5F`) adds to RAM[$F1]; otherwise RAM[$F2] (lst:3556-3573).
  - **Ramming kill** (tank collides with an enemy record, RAM[$EA] < `$30`): RAM[$EF] = `$30` (P1) adds to RAM[$F1]; otherwise RAM[$F2] (lst:2423-2455). Hitting the other player scores nothing: the score code is skipped when RAM[$EA] ≥ `$30` (lst:2423-2427).
- **Points per kill = 10 × (RAM[$F5] + RAM[$FB] − 1).** The code adds `RAM[$F5] + RAM[$FB] − 1` tens (lst:3550-3573 for shots, lst:2433-2455 for ramming).
  - RAM[$F5] is the enemy type of the current wave, 1..6 (section 14.1).
  - RAM[$FB] is the level, 1 at game start (lst:253-255) and +1 after each cycle of six types (lst:539-541).
  - So level 1 pays 10, 20, ... 60 for types 1..6, and level L adds 10·(L−1) to each.
- **Kill counter.** Every kill also does RAM[$F8] += 1 (lst:3574-3576 shot, lst:2429-2432 ram). It drives wave progression (14.1).
- **Score display** (`$140B`, lst:4681-4838).
  - Input: B = thousands → RAM[$EA], A = units → RAM[$E9]. The thousands are printed only when non-zero, and the units are printed after them; the 1000/100/10 digit logic is section 5's `$14BB` printer.
  - A trailing glyph "0" is appended when RAM[$E6] bit 2 is clear (lst:4820-4832). That bit is clear for every caller (E6 = 3/1/0/1 at lst:1124-1126, lst:1140-1142, lst:1191-1192, lst:1340-1342), so **scores display ×10**.
  - **Individual** (Scores DIP 0x20 = 0) (lst:1112-1156; the JMI is taken when the input bit is 1, ccpu.cpp:242-251, :664-676, :36, :219-221):
    - (RAM[$FF], RAM[$F2]) = P2 at origin (`$1A0`, `$176`);
    - then (RAM[$FE], RAM[$F1]) = P1 at (`$260`, `$176`).
    - That puts P1 on the right, matching P1's start at the right edge (10.1).
  - **Combined** (DIP = 1) (lst:1158-1198): P1 + P2 with the same carry, shown once at (`$200`, `$1CC`).
  - Path `$04B6`: when RAM[$F0] bit 0 is set (one-player game, below), only the `$0505` single display runs. What it shows on that path is **UNCHECKED**.
- **High scores** (lst:107-188), checked at game over (`$0053` path).
  - **Two-player game** (RAM[$F0] bit 0 clear): team total (FE+FF, F1+F2, carried) → RAM[$75]/[$74]. It replaces RAM[$73]/[$72] when higher (lst:110-148).
  - **One-player game:** P1 (FE, F1) → RAM[$75]/[$74], compared with RAM[$71]/[$70] (lst:159-184).
  - RAM[$F4] is set to 5 (team record) or 3 (1P record) (lst:152-155, lst:185-188). Its use in display brightness at lst:1376-1388 and lst:1421-1435 is **UNCHECKED** in detail.
  - In attract mode (RAM[$F0] bit 1 set): RAM[$71]/[$70] is drawn at (`$200`, `$10`) (lst:1363-1396) and RAM[$73]/[$72] at (`$200`, `$2DC`) (lst:1398-1443).
- **"BONUS LEVEL" screen.**
  - When a six-type cycle completes, RAM[$FC] bit 0 is set (lst:542-547).
  - While it is set, `$0554` draws glyphs 10 "BONUS" and 11 "LEVEL", then 12 "+", then the number RAM[$FB]−1 with the trailing 0 (lst:1252-1353). I.e. "+ (L−1)0" for the new level L, which is exactly the per-kill increase above.
  - How long it stays is **UNCHECKED**: RAM[$EC] counts down at lst:322-325.

## 14. Pass 3: game flow, waves and enemies

- **14.0 Mode bits in RAM[$F0].**
  - **Bit 1 = attract/game over.**
    - Set at game over (lst:94-99).
    - Cleared at game start, which does F0 &= 9 (lst:239-243).
    - When set, the code skips player spawn (lst:863-868), skips kill counting (lst:500-505), and shows credits (lst:1205-1211) and high scores (lst:1356-1362).
  - **Bit 0 = one-player game:**
    - START1 sets it (lst:202-210).
    - START2 clears it and needs ≥ 2 credits (lst:215-230).
    - Credits are RAM[$F3]; each start subtracts one (lst:236-238), and START2 subtracts one more first (lst:223-225).
  - **Game over** when no canister is left: the loop over the `$B0` table finds none with bit 0 clear, then jumps to `$0053` (lst:1460-1481).
- **14.1 Waves.**
  - Enemy records are `$00`, `$10` and `$20`. They are built by `$02BE-$0338` and copied to RAM[$E6]−`$10` while RAM[$E6] < `$30` (lst:741-766). Their word-A bit 0 marks a free record (lst:464-482).
  - When all three are free, RAM[$EC] += 2 per pass (lst:483-485).
    - At EC = 2 the wave-end bookkeeping runs once (lst:486-490 → `$0211`).
    - At EC ≥ `$65` a new wave spawns (lst:491-498 → `$025F`).
  - **Wave-end bookkeeping** (lst:500-573), when not in attract mode. RAM[$F8] = kills this wave.
    - F8 < 3: F8 := 0.
    - F8 = 3 in a one-player game: **advance**.
    - F8 = 3 in a two-player game: F8 is kept.
    - F8 = 4 or 5: F8 := 3.
    - F8 ≥ 6: **advance**.
  - **Advance:** RAM[$F5] += 1. When it reaches 7: F5 := 1, RAM[$FB] += 1, and the BONUS flag is set (lst:529-547). Then F8 := 0.
  - So **RAM[$F5] ranges 1..6.** It starts at 1 (lst:253-254). The attract path also cycles it 1..6 each wave (lst:599-609).
  - Interpretation, **UNCHECKED** as gameplay wording: a one-player wave must be cleared completely (3 kills, see the one-enemy handicap below); two-player needs two full clears in a row.
  - **Difficulty step-back:** if F5 ≠ 1 and RAM[$F9] ≥ 3, then F5 −= 1. RAM[$F9] counts canisters carried off this wave (14.4) and is then reset (lst:558-571).
  - **One-player handicap:** in a one-player game at level 1 (RAM[$FB] = 1), the third record (`$20`) is created inactive (RAM[$2A] = 1) and pre-counted as a kill (RAM[$F8] += 1). RAM[$F9] is also set to 1, so two thefts in such a wave already reach the step-back threshold of 3. The first level therefore has 2 enemies per wave (lst:843-861).
- **14.2 Spawn.**
  - Index k = RAM[$0B] & `$E` (record `$00`, word B) (lst:786-794).
  - All three enemies get the spawn point (`ROM[$0F82+k]·8`, `ROM[$0F83+k]·8`) as position **and** as target point (words C/D) (lst:795-827).
  - Heading = `2·ROM[$0F92+k/2]` degrees (lst:829-842).
  - Decoded (all head into the 1024×768 field by the heading formula of 11.1):

    | k/2 | spawn (x,y) | heading |
    |---|---|---|
    | 0 | (0,0) | 134° |
    | 1 | (304,0) | 180° |
    | 2 | (896,0) | 224° |
    | 3 | (592,0) | 180° |
    | 4 | (1024,768) | 314° |
    | 5 | (720,768) | 0° |
    | 6 | (128,768) | 44° |
    | 7 | (432,768) | 0° |

  - Where RAM[$0B] varies (records are copied back from page D by `$0CAA`) is **UNCHECKED**. So is how the three enemies separate, beyond the repulsion in 14.3.
- **14.3 Enemy parameters and behaviour** (record built in page D, lst:661-740).
  - Word A = `$1C` (bits 4, 3, 2) (lst:661-665).
  - **Speed:** v = F5 + (F5>>1) + FB + 1 into both word 6 (current) and word 3 (maximum) (lst:666-674). E.g. type 1 at level 1 is 3; type 6 at level 1 is 11.
  - **Turn step:** word E = (v>>1)+1 (lst:675-677).
  - **Draw scale:** `$50` (lst:1526-1527).
  - Word 6 rises by 1 toward word 3 on frames where RAM[$A8] bits 0 and 1 are both clear (lst:1842-1854). RAM[$A8] += 1 once per main-loop pass at lst:4587-4590 (section 17), so that is every 4th pass.
  - Speed below 3 after subtracting word F is clamped to 2 (lst:1813-1830).
  - **Movement:** same as the player, sin/cos with angle−90 (lst:1795-1841).
  - **AI is skipped** while word-A bit 0 = 1 (free), word-A bit 4 = 0, or RAM[$FA] ≠ 0 (lst:1507-1524).
  - **Target canister:** if the canister at RAM[$D2] is gone (y bit 0 set), the first remaining canister in the `$B0` table is chosen, and word F := 0 (lst:1528-1573).
  - **Word-A bit 3 clear:** steer toward the target canister via atan2 (lst:1581-1609). The canister coordinates are halved with `ASR`, like the canister draw.
  - **Word-A bit 3 set:**
    - First, repulsion: for each other enemy alive within |dx|,|dy| < `$60` (box test `$11E0`, lst:4151-4186), steer away from it (lst:1611-1675).
    - Otherwise, if bit 2 is set, steer to the target point in words C/D (lst:1676-1685 → `$078E`).
    - Otherwise chase a player: P1 in a one-player game; in two-player, the player with the smaller max(|dx|,|dy|) (lst:1686-1766). Words C/D := that player's position (lst:1768-1774).
  - **Steering** (`$109C-$1122`): turn by ±word E toward the bearing, snapping when the remaining difference is smaller, with wrap to 0..359.
  - **Hops toward the canister** (lst:1911-2069):
    - With word-A bits 3 and 2 set and bit 1 clear, an enemy that comes within `$48` of its target point (words C/D) calls `$11B8` twice on word B (a pseudo-random update; exact function **UNCHECKED**).
    - It scales the two results down by 4 + word F shifts.
    - It sets words C/D = (target canister position / 2) + that offset, then word F += 1, with C ≥ 4 and D ≥ 3 enforced.
    - So enemies approach their canister in random hops whose spread shrinks with each hop.
    - Word B of record `$00` is the spawn index source (14.2), so spawn points are pseudo-random too.
  - The meaning of the word-A bit transitions (`$11B8`, lst:4111-4149; arrival tests lst:1911-1957 and lst:3972-4109) is **UNCHECKED**: canister pick-up, drag, and when bit 3/bit 2 change. See section 16.
- **14.4 Canister theft.**
  - With word-A bit 1 set (interpretation: towing), an enemy that leaves the field (x or y negative, x ≥ `$400`, or y ≥ `$300`) does the following (lst:1856-1904):
    - RAM[$F9] += 1;
    - RAM[$78] += RAM[$7A] (15);
    - its own word A := 1 (free);
    - the canister's y word (RAM[$D2]+1; corrected in 18.3 from the trace, store at `$0810`) += 1, which sets the "gone" bit 0 that the canister draw loop and game-over test check.
  - How bit 1 gets set (the pick-up) is **UNCHECKED**.
- **14.5 Collisions** (all use `$11E0`: hit when |dx| ≤ r and |dy| ≤ r, r in RAM[$E4]; inclusive, corrected in 18.1).
  - **Player shots vs enemies:** r = `$14` (20) (lst:3482-3484), for records `$00-$2F` only (lst:3587-3591); players are never hit by shots.
    - On a hit: the enemy's word A &= `$F`, word 4 (draw scale) := 2, word C := 0; the shot slot is freed; Explosion is pulsed (lst:3513-3549); score as in section 13.
  - **Player tank vs any other record** (`$00-$4F` except itself): r = `$18` (24) (lst:2335-2367, loop lst:2350-2557).
    - Victim: word A &= `$F`, word 4 := 3, word C := 0, Explosion (lst:2398-2421).
    - Enemy victims score.
    - Then every enemy gets word-A bit 2 set: (A & `$1B`)+4 (lst:2457-2480).
    - The player's own scale := 3 and its word A &= `$F`, with a second Explosion pulse (lst:2481-2494). Interpretation, **UNCHECKED**: both tanks are destroyed and the remaining enemies switch to their bit-2 behaviour.
  - **`$1158-$11B7`** (corrected in 18.4: the "4 and 3" are `$400`/`$300` field bounds, and the range test is `$96` at `$1145`): when an enemy is close to its target point and that point equals a live player's position, the player record gets word A &= `$F` and scale 3, Explosion pulses, and all enemies get bit 2 set. This is the enemy destroying the player; the trigger is resolved in 18.4.
- **14.6 Shots** (`$0E4A-$0F54`).
  - 8 slots of 4 words at `$50-$6F`: P1 `$50-$5F`, P2 `$60-$6F` (11.2; loop lst:3614-3623).
  - Each frame a live slot moves by its velocity words (lst:3432-3445). It is freed when y ≥ `$300` or x ≥ `$400` (unsigned, so negatives too) (lst:3446-3466).

## 15. Pass 3: sound events

- **Command byte RAM[$FC]**, shifted out every frame by `$0174-$0193` (section 4):
  - In attract mode it gets +`$10` (bit 4) (lst:313-315).
  - In game: FC = (FC & `$F`) + (RAM[$78] & `$E0`), i.e. **background bits 7-5 = RAM[$78] bits 7-5** (lst:292-309).
  - While RAM[$FA] ≠ 0, FC = `$1C` (lst:283-288).
- **Background tempo/pitch rises as canisters are stolen.**
  - RAM[$78] starts at `ROM[$0FC2+idx]` (idx from the Lives DIP; lst:2892-2898) and grows by the same step RAM[$7A] for each canister carried off (lst:1885-1888, 14.4).
  - The steps `$1F`/`$3F`/`$0F`/`$15` for 8/4/16/12 canisters make N steps sum to 248/252/240/252, i.e. one full sweep of bits 7-5 over the game. This is arithmetic from the table; the audible effect is **UNCHECKED**.
- **Torpedo (Q3):** RAM[$FD] := 5 when a player shot is spawned (lst:2606-2609). Q3 = 0 while it counts down, then 1 (lst:343-353; section 4). So Q3 is low for 5 frames per player shot. Which level is "sound on" is still **UNCHECKED** (section 4).
- **Explosion (Q7) pulses:**
  - `$0A39` (rammed victim);
  - `$0A89` (the ramming player);
  - `$0EF4` (enemy hit by a shot);
  - `$1188` (player destroyed by an enemy, 14.5).
- **Beep (bit 3) and FC bit 0.**
  - When FC bit 0 is set (BONUS flag, section 13), the routine decrements RAM[$EC], sets FC = `$1D`, and clears bit 3 again on frames where RAM[$A8] bit 1 = 0 (lst:317-338). So bit 3 toggles every 2 frames while the BONUS screen is up.
  - Which polarity beeps is **UNCHECKED**.
- **Laser (Q4)** = FC bit 1 (lst:340-342). FC is `$1A` (bit 1 set) after reset (lst:74-79). No in-game setter of FC bit 1 was found, so what the Laser cue signals is **UNCHECKED**.
- **Motor (command bit 2):** set in `$1C`/`$1D`. Whether it follows thrust is **UNCHECKED**: no thrust-dependent write to FC was found.

## 16. Stopper: the enemy state machine

The remaining enemy behaviour sits in the word-A bits 1-3 transitions:

- `$11B8`: the pseudo-random update of word B (exact function **UNCHECKED**);
- `$081D-$0850`: arrival within `$48` of the target point;
- `$1123-$1160`: close-range tests, including `$96`;
- `$0850-$08C5`: random hops, now read (14.3).

These decide when an enemy switches from canister-seeking to dragging, when it chases a player, and when enemies fire. Is there enemy fire? None of the paths read so far creates a projectile for an enemy. **UNCHECKED**.

Decoding this needs the full data flow of word B and words C/D across the copy-back in `$0CAA`, through several indirect stores (`WS`/`WSP`) that the static analysis cannot resolve. It is possible statically, but it is long hand-tracing with a real risk of misreading the bit tests. Running the game would settle it quickly, and that is forbidden here.

**Resolved in pass 5 (section 18)** by running the ROM in MAME: the states and transitions (18.2-18.3), chase trigger (18.3), enemy fire (18.4) and `$11B8` (18.6).

## 17. Pass 4: small items

- **RAM[$A8] is a pass counter**, not a size variable.
  - It is incremented by 1 at the start of the canister draw `$13A1` (lst:4587-4590), which the main loop calls at `$0457` (lst:1021-1024).
  - Its low bits gate three things:
    - bit 1 selects the small canister glyphs (10.3), so the canisters pulse every 2 passes;
    - bits 0+1 both clear give enemy acceleration every 4th pass (14.3);
    - bit 1 toggles the BONUS beep (15).
  - That one pass equals one displayed frame: verified in MAME (18.8).
- **Canister count vs background step** (15): DIP index 0..3 = 12/16/4/8 canisters, step `$15`/`$0F`/`$3F`/`$1F`. The total rise is 252/240/252/248.
- **Not decoded this pass (UNCHECKED):**
  - the 7 open indirect returns (section 8);
  - tread-frame advance (10, the RAM[$E9] write-back);
  - shapes `$1F68-$1FAE`;
  - the `$15E9` data read by `$1536-$1580`.
- **`disasm/GAME_RULES.md`** collects the gameplay rules from sections 3, 10, 11 and 13-15, with citations, for the remake.

## 18. Pass 5: MAME trace of the enemy state machine

Status: done 2026-10-16. **This pass ran the ROM** in MAME 0.276 (Debian `mame` package, driver `ripoff`, `-verifyroms`: good), headless (`-video none -sound none -nothrottle`), default DIPs (8 canisters). Method, scripts and every excerpt cited below are in `disasm/trace/` (see `disasm/trace/README.md`). Trace citations are `trace:<file>` plus frame numbers `fN`. A frame is one MAME video frame of 26.316 ms (38 Hz, measured from `machine.time`).

How the evidence was collected:

- A Lua frame notifier logs all 256 data-RAM words every frame (`data` space, 16-bit, word addressed, values `& $FFF`).
- A write tap on the `data` space logs every store to word 2/A/B/C/D/F of records `$00-$40` and the work copy `$D0`, the canister table `$B0-$CF` and `$F0-$FF`, with the CPU PC and RAM[$EF] (the record being processed; `$0CAA` copies from RAM[$EF] to RAM[$EE], lst:2995-3021). Stores by the `$0CAA` copy loop are dropped.
  - **PC convention:** the tap sees `CURPC` after the store, so the logged `pc=` is the store instruction's address **+1** (e.g. `pc=0b89` is the `STAP` at `$0B88`). Region address = CURPC & `$FFF`, +`$1000` when CPU bit 13 is set (same mapping as `tools/ccpu/listing.py:43`).
  - On every state-word change the tap also logs RAM[$E0-$EF] (`E=` in `trace:excerpts/idle_state_writes_with_E_page.txt`).
- Runs, 12,000 frames each, one-player game (coin f120, START1 f200): `idle1` (no input), `shoot1` (P1 turns to the nearest enemy and fires), `ram1` (turns and thrusts at it), `wander1` (scripted pseudo-random turning/thrust). `idle_snap`/`idle_states` replay `idle1` with snapshots; their RAM logs are byte-identical to `idle1` for the frames they cover, so the emulation is deterministic for a given input script.
- Listing reminder: `LDAI $n` in `ripoff.lst` prints the nibble, but the instruction loads **n<<8** (ccpu.cpp:233-240; `listing.py:246-247`). So `LDAI $1` = `$100`, `LDAI $4` = `$400`. Section 14.5 read `$1158`/`$1161` as "thresholds 4 and 3"; they are `$400`/`$300` (see 18.4).

### 18.1 Box test `$11E0` is inclusive

`$11E0` sets RAM[$E6] = 1, then clears it (hit) unless r < |dx| or r < |dy| (`TST $4` + `JLT` at `$11F7-$11FC`, lst:4172-4180). So **hit when |dx| ≤ r and |dy| ≤ r**. Sections 14.5 and GAME_RULES §7 said `<`. Trace: the first chase trigger in `idle_snap` has E0,E1 = `$0C0`,`$100` with r = `$100` (f1016); the f596 pick-up has E1 = `$014` with r = `$14` (`trace:excerpts/idle_state_writes_with_E_page.txt`).

### 18.2 Enemy word A values (observed)

All values seen in word A of enemy records `$00/$10/$20` in the four runs (`trace:excerpts/word_a_transitions.txt`):

| word A | meaning (from the trace) |
|---|---|
| `$001` | free / inactive |
| `$005` | free record that received the "all enemies +bit 2" broadcast (18.5); still free (bit 0) |
| `$01C` | **approach**: hopping toward the target canister (bits 4,3,2) |
| `$014` | **at canister**: stopped (speed word 6 = 0), turning toward its exit point (bit 3 clear) |
| `$01E` | **towing**: carrying the canister to the exit point (bits 4,3,2,1) |
| `$018` | **chase**: steering at the player (bit 2 clear) |
| `$00C/$004/$00E/$008` | exploding (bit 4 cleared by `A & $F` from `$01C/$014/$01E/$018`), then `$001` |

### 18.3 Transitions, with the code that stores them

Every transition below was seen in the trace with the given store PC; counts and first frames are in `trace:excerpts/word_a_transitions.txt`.

| from → to | store | condition (code + trace) |
|---|---|---|
| `$001` → `$01C` | `$02C3` (lst:665) | wave spawn (14.1). In a one-player level-1 wave the third record is then set back to `$001` at `$039A` (14.1) |
| `$01C` → `$014` | `$0B88` (lst:2723) | **pick-up**: `$0B49` loops over the canister table; for a live canister (y bit 0 clear) it tests the enemy position against (x/2, y/2) with the box test, r = `$14` (lst:2666-2707). On a hit: word A := (A & `$13`) + 4, word 2 := that canister's address, canister x += 1 (bit 0 = "carried", `$0B93-$0B96`, lst:2734-2737), speed and hop count := 0, and words C/D := exit point (next row) (lst:2719-2798). E-page at the pick-ups: \|dx\|,\|dy\| = (3,19), (1,20) (idle_snap f537, f596). The enemy takes **any** live canister it touches, not only its target: idle1 f537 r00 targeted `$B0` and took `$B8` (`trace:excerpts/idle1_transition_context.txt`). A chasing enemy also picks up (`$018` → `$014` at `$0B88`, ram1, 2 times) |
| (exit point) | `$0BB7-$0BD8` (lst:2768-2798) | k = word B & 7; C = ROM[`$0F82`+2k]·8, D = ROM[`$0F83`+2k]·8: one of the 8 spawn points of 14.2. Trace: all 189 pick-ups in idle_snap/shoot1/ram1/wander1 match (C/D from the stores at `$0BCD`/`$0BD8`, word B from the frame log; `trace:excerpts/exit_point_rule.txt`). It is **not** the enemy's own spawn point (`trace:excerpts/exit_chase_kill_checks.txt`) |
| `$014` → `$01E` | `$10BD` (lst:3877) | **tow starts** when the steering routine `$109C` snaps the heading onto the bearing to C/D (bearing reached exactly, `$10A3`, or within one turn step, `$10DF`) while bit 3 is clear: A := (A & `$15`) + `$A` (lst:3858-3877). idle1: f537 → f615 and f596 → f663, the turn-in-place took 78 and 67 frames at speed 0 |
| `$01E` → `$001` | `$0809` (lst:1892) | **theft**: leaves the field (14.4). Then canister **y** += 1 (store `$0810`, lst:1899), i.e. the "gone" flag is bit 0 of the y word (14.4 said word 0) |
| `$01C` → `$018` | `$0ABF` (lst:2537) | **chase starts**: in the player's pass (RAM[$EF] = `$30`), for each enemy record whose word A has bit 1 clear and bit 3 set (`$0A9F-$0AA7`), the box test against the player with r = `$100` (`LDAI $1`, `$0AAC`, lst:2522) clears bit 2: A := A & `$1B` (lst:2537). It is re-evaluated every frame while the player is inside the box (`018->018` stores every frame in idle_snap f1017-f1599). Towing (`$01E`) and at-canister (`$014`) enemies never switched: 545 frames with such an enemy within 200 of a live player in ram1/wander1, and no `$014`/`$01E` → `$018` change (`trace:excerpts/misc_checks.txt` (a)) |
| `$018` → `$01C` | `$1199-$11B4`, `$0A6B`, `$0A74` | only by the "player destroyed" broadcasts (18.5). **Chase never ends on distance:** wander1 had a chasing enemy 770 away from P1 |
| any → exploding | `$0EE5` (lst:3530) | shot kill (14.5); `$0A2D`/`$0A6B` ram kill |
| exploding → `$001` | `$0D23` (lst:3106) | after 16 frames (shot kill, scale 2: shoot1 f424 → f440) or 24 frames (ramming, scale 3: ram1 f345 → f369). If the enemy was towing, the canister x bit 0 is cleared (`$0D30-$0D35`, lst:3117-3121) and the canister **stays where it was dropped** (shoot1: canister 2 dropped at (572,415) f1818, still there f2500/f3000, later picked up again from there and moved to (582,428), finally stolen f4789) |

### 18.4 Enemy fire

There is **no enemy projectile object**. Shot slots `$50-$6F` were never live in idle1/ram1/wander1, and P2's slots `$60-$6F` never in any run. The only RAM words that changed during the idle game are in the records `$00-$3F`, `$78`, scratch `$A0-$AF`, the canister table, `$D0-$ED` and `$F8/$F9/$FC` (`trace:excerpts/misc_checks.txt` (b), (c)). Instead:

- **Fire window.** RAM[$E8] = `$10`·((frame >> 2) & 3) at every frame end (`trace:excerpts/laser_windows.txt`, all 12,000 frames of ram1). The record whose base equals RAM[$E8] may fire, so each record gets a **4-frame window every 16 frames** (`$00`: f%16 = 0-3, `$10`: 4-7, `$20`: 8-11).
- **Kill test** `$1123-$11B7` (lst:3972-4109). It is entered from the steering routine when the heading snaps onto the bearing (`$10AF`, `$10EB`, lst:3865, :3920), for an enemy with bit 3 set and bit 2 clear (chase). It requires:
  - RAM[$E8] = RAM[$EF] (its window, `$1126-$1128`);
  - \|enemy − C/D\| ≤ `$96` (150) on both axes (`$112C-$1152`; the test is `$96` < value → no kill);
  - C/D inside the field (< `$400`, < `$300`, `$1158-$1168`, lst:4023-4036);
  - C/D equal to P1's or P2's position, and that player alive (`$1169-$1185`).
  Then that player's word A &= `$F`, scale := 3, Explosion pulse, and every enemy gets bit 2 (`$1186-$11B4`, lst:4065-4107).
- **Trace:** 13 player kills by enemies (idle_snap 1, ram1 7, wander1 5). At every one, the killer was in `$018` at the previous frame end, RAM[$E8] equalled the killer's record, the frame fell in its window, and \|d\| ≤ (145,137) (`trace:excerpts/exit_chase_kill_checks.txt`).
- **Visible "shot".** During a chasing enemy's window the draw code at `$0DFC-$0E1E` (lst:3308-3338) draws an extra line and sets RAM[$FC] bit 1 (Laser, Q4; cleared every frame at `$03F3`, lst:939). The snapshots show a straight line from the enemy's nose along its heading, shorter in each of the 4 frames (`trace/snaps/idle_beam_kill_montage.png`, f1029-f1032; enemy at (801,89), heading 144°, pointing at P1 (969,320) but out of reach).
  - In the trace, bit 1 was set **only** for an enemy in `$018` and only inside its window (all 2,404 sets).
  - It was not set in every window: 1,083 chase-window frames (idle1 1,036, ram1 14, wander1 33) had no line. That extra condition is **UNCHECKED** (resolved in 19.1: off-field draw skip, kill pass, pick-up).
  - Whether the line itself can hit is **UNCHECKED** (resolved in 19.1: it cannot; it is cosmetic). All 13 kills match the window/aim/150 test above. At the idle_snap f1076 kill, no line was drawn in f1075/f1076 and the explosion starts f1077.
- **Orbit.** In idle1 the last enemy circles the motionless P1 from f1124 to f12000 without a kill (speed 3, turn step 2°: it never snaps onto the bearing inside its window). So **an idle player can survive indefinitely against a single chaser**, and the idle game never ends (idle1: 3 canisters lost, then no change in the canister table, word A, F8/F9 or score for 10,600 frames, `trace:excerpts/idle1_state_changes.txt`).

### 18.5 Player death, respawn, game over

- Player destroyed by an enemy: P1 word A `$01C` → `$00C` at `$118A`. All enemies get bit 2 (`$018` → `$01C` at `$1199/$11A2/$11B4`; free records `$001` → `$005` at `$11AB`) (idle_snap f1076). Chasers go back to approaching their canister.
- P1 record freed at `$0D23` 23 frames later (f1099). P1 respawns at `$03BC` (lst:890) 25 frames after that (f1124), at (1024, 320) with angle 270. All 28 respawns in idle1/ram1/wander1 had the same timing (death → free 23 frames, free → respawn 25 frames) and, one frame later, position (1018-1020, 320), angle 269-271, speed 4-6 (`trace:excerpts/p1_respawns.txt`). What sets the 25-frame delay and the initial speed is **UNCHECKED** (resolved in 19.2).
- There is no life counter: ram1 alone had 20 respawns (`trace:excerpts/p1_respawns.txt`). Game over came only when the canisters were gone (shoot1 f4790, ram1 f3434, wander1 f4462: RAM[$F0] bit 1 set).

### 18.6 RNG `$11B8`

- One step: **s(x) = ((x << 1) & `$FFF`) | (bit10(x) XOR bit8(x))**.
- Code: `SHL` + `JMI` tests bit 10, and the `$200` mask + 2×`SHL` + `JMI` tests bit 8 (lst:4111-4149).
- Trace: the word-B stores at `$0860` (two `$11B8` calls, lst:1955, :1966, :1972). A search over all 12-bit two-tap shift-feedback functions found this as the **only** fit, over 1,328 distinct (old, new) pairs (`trace:excerpts/rng_11B8_fit.txt`).
- From the reset seed `$387` (record `$00` word B, set at `$0037`) the sequence has period **2047**.

### 18.7 Sound cues seen in the trace (`trace:excerpts/sound_bits.txt`)

- **Laser (Q4 = ~(FC>>1)&1, lst:340-342):** FC bit 1 is set only at `$0E1E` (the enemy-fire line, 18.4) and cleared each frame at `$03F3`. So the Laser cue is the **enemy fire**. Whether Q4 = 0 is audible is **UNCHECKED** (resolved in 19.3: yes).
- **Motor 1 (command bit 2):**
  - set at `$0348` (lst:777-782) when a wave has ended (all records free; the inter-wave countdown of 14.1);
  - cleared at `$0266` (lst:576-581) when the next wave spawns;
  - also set at `$004D`, `$00D1` and `$0130` (game start/attract paths).
  It does **not** follow player thrust: bit 2 was 0 in 1,589 frames with P1 moving and 1 in 125 frames with P1 stopped (ram1). The audible meaning is **UNCHECKED** (resolved in 19.3: on while bit 2 = 0).
- **Torpedo (Q3):** RAM[$FD] := 5 at `$0B0D` on each P1 shot (250 shots in shoot1). Q3 toggled 245 times in shoot1, never in the other runs.

### 18.8 Other checks

- **One main-loop pass = one frame** (open item in 17): RAM[$A8] increased by exactly 1 in each of 11,999 frame steps of ram1 (`trace:excerpts/laser_windows.txt`).
- Address spaces, inputs and DIP fields found by Lua: `trace:excerpts/mame_discover.txt`.

### 18.9 Still UNCHECKED after pass 5

(Items 1-4 resolved in §19, item 5 for the exercised paths; see 19.7 for what remains.)

1. What else gates the fire line in a chase window (18.4), its length/range, and whether the line itself can destroy a player.
2. What sets the 25-frame respawn delay and the initial respawn speed.
3. The audible polarity of Laser/Motor/Torpedo/Beep.
4. Two-player behaviour (target choice between players, P2 respawn); no two-player run was made.
5. The 7 open indirect returns (section 8) are not affected by this pass.

## 19. Pass 6: MAME runs on the pass-5 open items

Status: done 2026-10-16. Same setup as §18 (MAME 0.276, driver `ripoff`, headless, default DIPs). New Lua script `trace/scripts/trace2.lua`: one- or two-player games (`PLAYERS=2` adds a second coin at f150 and presses "2 Players Start" at f200), per-player input modes (`idle`, `shoot`, `ram`, `wander`, `pvp` = turn to the other player and fire), debugger traces over frame ranges (`TRACE=`, needs `-debug -debugger none`), and forced latch/command bits for the sound tests (`FORCEQ=`, `FORCEFC=`). trace2.lua with the `idle1` input reproduces `idle1`'s `ram.log` exactly for 1,340 frames, and every pass-6 replay of a pass-5 input script (fl_idle, rt1, rt2, rt4) reproduced its pass-5 `ram.log` byte for byte. New runs are listed in `trace/README.md`. Excerpts are `trace:excerpts/p6_*.txt`.

Two reading notes used throughout:

- **Frame-end positions lag the draw by one step.** A record is moved and drawn from its `$D0` work copy, and the copy goes back to the record afterwards (`$0CAA`, lst:2995-3021). The frame notifier fires between the two, so the position the draw used is the one logged at the **next** frame end. The debugger trace below shows this (f1558: logged x = 1022, but the draw took the x ≥ `$400` exit).
- **Explosion length (correction to 14.5/18.3).** "Scale 2/3" in 14.5 is `LDAI $2`/`LDAI $3`, which loads `$200`/`$300` (18 intro). `$0D14-$0D23` (lst:3093-3106) subtracts `$20` from word 4 every pass and frees the record (word A := 1) when it reaches 0. That gives 16 passes for a shot kill and 24 for a ram or player kill. In the trace, P1's word 4 goes `$2E0` → 0 in steps of `$20` (idle1 f1077-f1100). That is 23 frame-end steps, because the first subtraction falls in the kill pass.

### 19.1 Fire line: what gates it, its range, and what it can hit

- **Gating (resolved).** `trace:excerpts/p6_fireline_gating.txt`, classifier `p6/fireline_gating.py`, 12 runs. We looked at every frame where RAM[$E8] names an enemy in `$018`, using the position at draw time. Results:
  - 7,841 frames: line drawn, enemy on the field.
  - 4,059 frames: no line, enemy **off the field at draw time**.
  - 72 frames: no line, a **player was destroyed in that pass**. The kill broadcast sets the enemy to `$01C` before it is drawn.
  - 1 frame: no line, the enemy **picked up a canister in that pass** (`$018` → `$014`, ram1 f2902).
  - 0 frames left unexplained. No line was ever drawn for an off-field enemy.

  The code, in order:
  - `$0CC6` skips free records.
  - `$0D39-$0D51` (lst:3125-3145) skip the whole draw, line included, when x < 0, y < 0, x ≥ `$400` or y ≥ `$300`.
  - `$0DEC-$0DF7` (lst:3292-3305) skip if the speed is non-zero and RAM[$FA] ≠ 0. RAM[$FA] was 0 in every chase-window frame. It only counted down (from 123) while F0 bit 1 (attract/game over) was set: ram1 f1, f3434, f4743 … So this branch never applied in play.
  - `$0DFC-$0E03` (lst:3308-3314): word-A bit 2 must be clear.
  - `$0E07-$0E0C` (lst:3318-3322): RAM[$E8] = RAM[$EF].
  - `$0E10-$0E1E` (lst:3326-3338): RAM[$A4] := RAM[$ED] (the shape), FC |= 2 (Laser), then draw.

  So the 1,083 "no line" frames of 18.4 were off-field enemies plus kill frames. The debugger trace in the same excerpt (run `fl_idle`, f1554-f1559) shows the branch taken in each pass: not-window, not-window, LINE, LINE, OFF(x), OFF(x).
- **Shape and range (resolved).**
  - RAM[$ED] cycles `$F89`, `$F92`, `$F9B`, `$FA4` (region `$1F89-$1FA4`), one per frame (`p6_fireline_gating.txt`, ED column).
  - Decoded with `tools/ccpu/shapes.py` (`p6/beam_shapes.py`, distances only, `trace:excerpts/p6_beam_shapes.txt`), each is a thin two-line spike. Its tip is 232 shape units from the pivot. Its base is at 40, 88, 136 or 184.
  - The draw origin is the record's x,y unscaled (`$0D69-$0D6F`, lst:3169-3175). The scale is word 4 (`$0D59-$0D5E`, lst:3153-3157) over 128 (§9.3). Word 4 was `$50` in all 16,239 chase frames sampled.
  - So the line always reaches **145 field units** along the heading. Its near end steps out 25 → 55 → 85 → 115 over the 4 frames. This matches the kill reach of 150 (`$96`, 18.4).
- **What it can hit (resolved: only players, and the line itself is cosmetic).**
  - The kill comes from the test at `$1123-$11B7` (18.4), entered from steering, not from the draw. Its only victims are player records that C/D matches (`$1169-$1185`, lst:4038-4064).
  - Over 9 runs, every enemy explosion was stored at `$0EE5` (player shot) or `$0A2D` (ramming). No enemy was ever destroyed at `$118A`.
  - Canister-table words were changed only by stores at `$0C63`, `$0C79`, `$0DA2`, `$0DA9`, `$0B96` (pick-up), `$0D35` (drop) and `$0810` (theft) (18.3; `trace:excerpts/p6_two_player.txt` (b)). None of these is in the fire-line or kill code. What the first four do exactly was not traced.
  - Kills also happen in frames where no line was drawn: idle1 f1076, and all 72 kill-pass frames above.

### 19.2 Respawn delay and speed (resolved from code + trace)

`$039E-$03EC` (lst:862-932) runs every pass:

- It is skipped while RAM[$F0] bit 1 (attract/game over) is set (`$03A1-$03A4`).
- For P1 (P=3), and then P2 (P=4) only when F0 bit 0 is clear (two-player game, `$03D3-$03D5`), it does the following when the record is free (word A bit 0, `$03AC-$03AE`):
  - word 3 += 1;
  - while word 3 < `$19` (25) it waits (`$03B2-$03B6`);
  - then word 3 := 0, word E := 0, word A := `$01C` (`$03BC`, lst:890) and **speed word 6 := 5** (`$03BD-$03BF`, lst:891-893).
- Position comes from RAM[$E6] (0 for the first record, 1 for the second, `$03A6`, `$03D6-$03D9`, `$03C3-$03C5`):
  - **P1:** x = `$400`, y = `$140`, angle `$140` − `$32` = 270 (`$03C6-$03CF`, lst:899-906);
  - **P2:** x = 0, y = `$1C0`, angle `$5A` = 90 (`$03E2-$03EC`, lst:924-932).
- So the delay is the word-3 counter (`$19` passes). The initial speed is 5, then the usual thrust decay applies (−1 per pass, 11.1).

Trace: `trace:excerpts/p6_respawn_timing.txt`.
- Across 7 runs, every death → free took 23 frames and every free → respawn took 25 (word 3 runs 0..24), for P1 and for P2.
- The one 24-frame exception is tp_wander f3878: a tank-vs-tank kill in P2's pass, where word 4 was still `$300` at the frame end.
- One frame after respawn (99 respawns after f260 in 8 runs), P1 was at (1012-1016, 320), angle 268-272, speed 4-6 (64 respawns). P2 was at (8-10, 448), angle 90, speed 4-5 (35 respawns).

### 19.3 Sound polarity (resolved by forced-bit runs + netlist)

**Method.** `trace:excerpts/p6_sound_polarity.txt`; scripts `p6/soundlab.sh`, `p6/sound_rms.py`, `p6/sound_polarity.py`.
- We made 12 identical 4,000-frame one-player runs with the `shoot` script and `-wavwrite` (48 kHz). The netlist sound is still computed with `-sound none`.
- In each run, one bit was forced:
  - `FORCEQ=n:v` makes the `io` write tap return v for every OUT n;
  - `FORCEFC=mask:value` forces those bits in every store to RAM[$FC], the serial command byte.
- Game logic was unaffected: the latch-forced runs have `ram.log` identical to the base run, and the FC-forced runs differ only in word `$FC`.
- Per-frame RMS (1,263 samples per frame) was compared to the base run, split by the bit's base value.

**Netlist wiring** (MAME `src/mame/cinematronics/nl_ripoff.cpp` at tag mame0276; `cinemat_a.cpp:243-246`):
- `ripoff_audio` takes latch outputs Q0-Q4 and Q7 (mask `$9F`, scale 0.61), all as "active high" TTL inputs (nl_ripoff.cpp:55-60).
- Q5 and Q6 do not reach the sound board.

| bit | forced-run result (mean frame RMS) | netlist | **sound on when** |
|---|---|---|---|
| Q4 Laser | Q4 forced 0 in the 3,995 frames where it was 1: 2440 → 2978. Q4 forced 1: no change outside the 4 laser frames (diff RMS 1) | `I_OUT_4` → IC18.12, the CLR of a 74LS393 counter (nl_ripoff.cpp:563, IC18 at :311). CLR high holds the counter, so it is silent | **Q4 = 0** (FC bit 1 = 1, 18.7) |
| Q3 Torpedo | Q3 = 0 frames are louder in the base run (3232 vs 2207). Forcing Q3 = 1 there: 3232 → 2747 | `I_OUT_3` → IC18.2 (CLR) (:625) | **Q3 = 0** (5 passes per shot, 18.7) |
| Q7 Explosion | Q7 forced 1 (no low pulse): overall RMS 4733 → 2003, frame RMS 2440 → 1481 | `I_OUT_7` → IC4.2, the LM555 trigger (:342, IC4 at :263) | **falling edge of Q7.** `$1188` writes 0, then `$118E` writes 1 (idle1 f1076 `OUT7=0 pc=1189`, `OUT7=1 pc=118f`) |
| FC bit 2 Motor | bit-2 = 1 frames are quiet (1069 vs 2897). Forcing 0 there: 1069 → 1446. Forcing 1 in bit-2 = 0 frames: 2897 → 2652 | IC9.15 → Q4 (2N3906) → Q5 → IC12 CA3080 bias (:523-531). A low output turns the PNP pair on and enables the OTA | **bit 2 = 0.** So the motor drone plays **during waves** and is off in the inter-wave gaps (bit 2 set at `$0348`, lst:777-782, cleared at `$0266`, lst:576-581) |
| FC bit 3 Beep | base bit 3 = 0 only on the BONUS screen (49 frames, f3266-f3363, every other pair). Forcing 1 there: 1106 → 202. Forcing 0 elsewhere: 2457 → 2776 | IC9.12 → Q2 → Q3 → IC6 CA3080 bias (:489-497); IC5 LM555 tone | **bit 3 = 0** (the BONUS beeps, lst:317-338) |
| FC bit 4 background enable | bit 4 = 1 frames (attract/pre-game f1-201 and BONUS f3266-3364) are quiet (656). Forcing 0 there: 656 → 1402. Forcing 1 in play: 2585 → 2048 | IC9.9 → IC22.2, the CLR of a 74LS393 (:403, IC22 at :327) | **bit 4 = 0.** Bit 4 = 1 silences the background drone |

- Which serial command bit drives which IC9 output was established by these runs. Each match (bit 4 ↔ IC22 clear, bit 3 ↔ the Beep path, bit 2 ↔ the Motor path) is the only one consistent with both the RMS changes and the game events. The shift order itself was not derived from the netlist.
- The pitch mapping of background bits 7-5 (IC9.2/.5/.6 → IC10, :404-408) was not tested here: **UNCHECKED** (resolved in 21.3).

### 19.4 Two-player games (resolved items)

Runs: tp_idle, tp_pvp1, tp_pvp2, tp_shoot2, tp_wander (6,000-12,000 frames each; `trace:excerpts/p6_two_player.txt`).

- **Player shots never hit the other player** (code: the shot test loops over records `$00-$2F` only, 14.5).
  - Trace: 787 (tp_pvp1) and 769 (tp_pvp2) frames had a live shot within 20 of the other live player (box; shot word 0 is 2·x, which also matches the shot-kill positions in shoot1). Example: tp_pvp1 f326, shot (54, 442), P2 (55, 448).
  - None of those frames had a player death, a score store, or a shot slot freed early. Of the 39 player deaths in the two pvp runs (16 + 23), 36 were enemy fire kills stored at `$118A` and 3 were player-vs-enemy rams (tp_pvp1 f3953, tp_pvp2 ×2; enemy store `$0A2D`) (`p6_two_player.txt` (a), (b)).
- **Tank vs tank:** both players explode, nobody scores.
  - tp_wander f2758, in P1's pass: P2 word A `$01C` → `$00C` at `$0A2D` and P1 → `$00E` at `$0A86`, at \|dx\|,\|dy\| = 23,21 ≤ 24.
  - No store to F1/F2 that frame. Free enemy records got the bit-2 broadcast (`$001` → `$005`). Both respawned at f2807.
- **Scoring per player:**
  - shot kills store the score at `$0F11` (lst:3573) to F1 for P1's slots and F2 for P2's slots (tp_pvp1: 32 F1 stores, tp_shoot2: 28 F2 stores);
  - ram kills store at `$0A62` (lst:2455), to F1 in P1's pass (EF = `$30`) and F2 in P2's pass (EF = `$40`) (tp_wander 4 + 2).
- **Chase start in either player's pass:** `$0ABF` fires with EF = `$40` as well as `$30` (tp_pvp1 f363, f468, f1365 …). The box is 256, as in 18.3. Correction to 18.3, which saw only EF = `$30` because it only had one-player runs.
- **Which player a chaser steers at:** `$072C-$078D` (lst:1679-1774).
  - In a one-player game (F0 bit 0 set, `$0734-$073A`) it is always P1.
  - Otherwise it compares a distance to P1 (`$073B-$075B`) with one to P2 (`$075C-$077C`) and copies the chosen player's x,y into C/D (`$0787-$078D`).
  - P2's distance is max(\|dx\|, \|dy\|).
  - P1's computation takes \|dx\|, then at `$074E-$074F` computes `0 − RAM[$E0]` (`SUB $0`) where the P2 branch has `SUB $A` (`$076F-$0770`). The listing therefore never takes \|dy\| for P1. Read literally, the result is \|dx\|.
  - **Trace check** (`p6/chase_target_fit.py`): in 18,771 chase frames whose C/D equalled one player's previous-frame position, the rule "P1 if \|dx₁\| < max(\|dx₂\|,\|dy₂\|), else P2 (tie → P2)" matched all 18,771. So did "P1 by max(\|dx\|, −dy)", which the data cannot tell apart from it. The symmetric rule "nearer by max(\|dx\|,\|dy\|) for both", which GAME_RULES §6 stated, fails 252 times. Example: tp_wander f1698, P1 \|d\| = (250, 313), P2 (256, 81), the enemy chose P1.
  - Dead or free players are not excluded: C/D kept pointing at a dead P1's last position.
  - In 401 further chase frames, C/D matched neither player. Example: tp_pvp1 f3130-f3134 kept the old hop point (505,349) for the first frames after a chase start. What gates the per-frame C/D refresh is **UNCHECKED**.
- **Enemy fire kills either player.** C/D equal to a player's position is the kill condition (18.4). P2 was destroyed by enemies at `$118A` 10 times in tp_pvp1 and 14 times in tp_pvp2.
- **P2 respawn:** (0, 448) at angle 90 with the same 23 + 25 frame timing (19.2).

### 19.5 The 7 open indirect returns (resolved for the paths exercised)

`trace:excerpts/p6_indirect_returns.txt`, from debugger traces of four runs (rt1, rt2 and rt4 replay shoot1 exactly; rt3 is a 2P game). These are observed target sets; whether they are complete is **UNCHECKED**.

| site | lst | observed targets (count) | compared with the listing |
|---|---|---|---|
| `$0457` T4K (unknown P) | 1024 | `$13A1` only, with P = `$E` (trace `D457` → `E3A1`, 1,211×) | resolves the unknown P: the callee is region `$13A1` |
| `$0C0A` JMP via RAM[$E5] | 2846 | `$081D` (5×, rt4 f4341-4345 and f5056-5060, the `$0BDD` branch from `$0B82`, lst:2718/2804) | one of the two RD targets. `$09EE` was not seen. `$0BDC`, which returns through the same RAM[$E5], also went only to `$081D` |
| `$0CA9` JMP via RAM[$E7] | 2993 | `$01E6`, `$0110` | the two ASSUMED targets, both seen |
| `$0CC5` JMP via RAM[$E7] | 3021 | `$066A`, `$0411`, `$08D2`, `$0428`, `$0437`, `$0287`, `$034C`, **`$026E`** | `$026E` was not in the listing's ASSUMED set |
| `$1535` JMP via RAM[$AB] | 4955 | `$14A6` only | `$149A` not seen |
| `$15E3` JMP via RAM[$A7] | 5116 | `$13F7`, `$1533`, `$15E4`, `$1542`, **`$1523`**, **`$152B`** | two targets not in the listing's set |
| `$15E8` T4K via RAM[$E7] | 5122 | `$0579`, `$058E`, `$05B6`, `$05C8`, **`$04D6`**, **`$051B`**, **`$05F7`**, **`$0629`** | four targets beyond the listing's narrowed set. All four are in its RD may-set |

### 19.6 The orbit: real game behaviour, an exact cycle

`trace:excerpts/p6_orbit_period.txt` (`p6/orbit_period.py`):

- **idle1** (no input), from f1400 to the end (f12000): **RAM(f) = RAM(f + 720) for every frame, except the pass counter RAM[$A8]**. One chaser (`$10`, speed 3, turn step 2) stays 31-136 from the motionless P1.
- **orb_nudge** (idle, plus P1 thrust f3000-f3010): P1 moved from x 984 to 968 and stopped. The chaser went into a new loop, and from f3100 to f6000 RAM repeats every 720 frames except RAM[$A8] (distance 26-136).
- **orb_wstop** (wander until f1500, then idle): two more kills of the stopped P1 (f2081, f2821), then from f3100 to f12000 RAM repeats every 720 frames except RAM[$A8] and RAM[$19] (word 9, a sprite pointer of the chaser record) (distance 35-129).
- **orb_sstop** (shoot until f2500, then idle): no orbit. The enemies took the remaining canisters, and the game ended at f3165 (F0 bit 1 set).
- **Conclusion:**
  - The cycle is the game's own deterministic logic, not an emulation artifact, and not specific to one input history.
  - While the input stays idle the state repeats exactly. The only differing word is the pass counter, whose bits 0-1 (the only uses found, NOTES §17/§18) are themselves 720-periodic. So the loop does not end.
  - Each lap brings the chaser within 26-35 of the player. That is outside the ram radius (24), and its window never coincides with a heading snap onto the bearing (the kill test, 18.4).
  - It is not guaranteed: with more enemies, or before the last chaser, an idle player is still killed (idle1 f1076, orb_wstop f2081/f2821), and without a chaser the canisters are stolen (orb_sstop). Any thrust breaks the cycle, but a new one can form (orb_nudge).
  - That RAM[$A8] has no other use is **UNCHECKED** (no full reference search was made).

### 19.7 Still UNCHECKED after pass 6

1. The condition that refreshes a chaser's C/D every frame (401 frames kept an older point, 19.4).
2. Whether P1's chase distance is exactly \|dx\| (literal reading of `$074F`) or max(\|dx\|, −dy). Both fit all trace frames.
3. The pitch mapping of background command bits 7-5. (Resolved in 21.3.)
4. Completeness of the observed indirect-target sets (19.5).
5. Other uses of RAM[$A8], beyond bits 0-1 (19.6).

## 20. Pass 7 / Milestone 1: the C# simulation checked against the trace

Pass 7 ported GAME_RULES into `src/RipOff.Core` and replayed the scripted MAME runs through it frame by frame (`tools/remake/Replay`; checkpoints in `tests/RipOff.Core.Tests/Data/*.chk`). Every disagreement was traced back to the listing. Evidence: `trace:excerpts/p7_checks.txt` (`scripts/p7/p7_checks.py`).

### 20.1 Corrections to GAME_RULES

- **(a) At-canister turn target.** An enemy at its canister (`$014`) turns toward the bearing of (own − canister/2), i.e. away from the canister, not toward the exit point (lst:1587-1609). The code negates the stored ×2 word and halves it with ASR before adding the own position, so the taken bit rounds x down by one more. 12,162 of 12,162 at-canister frames turn toward this target (p7_checks (b)). The tow (`$01E`) starts on the pass **after** the heading snaps, through the "heading = bearing" path (lst:3858-3877, lst:3910-3921).
- **(b) Player thrust and turn cadence.** Thrust changes speed by ±1 only in passes where RAM[$A8] bits 0-1 are clear, i.e. every 4th pass (lst:2219-2255). All 1,663 traced player speed changes have frame-end A8 & 3 = 1 (the counter is incremented after the player pass) (p7_checks (c)). The turn step rises by 1 on even passes up to 8 (lst:2141-2218).
- **(c) Hop point clamp.** C is clamped to 0..`$400` and D to 0..`$300` (lst:2037-2068). The earlier "C ≥ 4, D ≥ 3" misread `LDAI n`, which loads n·`$100`.
- **(d) Enemy fire reach.** The kill test is strict: |dx| < 150 and |dy| < 150 (`$112C-$1152`, lst:3980-4013), not inclusive. The on-field test that follows (`$1158-$1168`, lst:4023-4036) is on the enemy's own position, not on C/D.
- **(e) Separation.** The record walk at lst:1611-1667 steps by `$10`, but the self-skip at `$06EA` jumps past the "+1" at `$06F2`. After the enemy's own record the walk runs one word off (`$0F`, `$1F`, `$2F`). In effect an enemy compares itself properly only with **lower-index** enemies (free ones skipped, exploding ones counted), then with pseudo-records whose x is word F of record m, whose y is word 0 of record m+1, and whose "free" bit is bit 0 of word 9 of record m+1 (a tread-frame pointer: its parity is fixed per sprite set, P1 odd, P2 even, enemy types 3 and 5 odd, the others even; `disasm/shapes/objects.txt`). The first hit steers along (own − other) and skips the C/D update. The literal walk is what makes the replays match (20.5).
- **(f) Kills write word C.** A shot kill stores C := 0 (lst:3526-3549), a ram stores C := 1 in both records (lst:2393-2421), an enemy-fire kill stores C := 2 in the victim (lst:4065-4073). The replays diverged at the first kill until this was modelled.
- **(g) Game-over test position.** The "every canister gone" test (`$0631-$0653`, lst:1446-1481) runs right after the frame wait, before the sound routine and the wave check of the next pass. In the game-over pass RAM[$EC] therefore does not get the wave check's +2 (ram1 f3434, wander1 f4462, tp_shoot2 f4784).
- **(h) BONUS flag.** It is cleared when the next wave spawns: FC := (FC & `$F2`) + 8 (lst:575-581). While it is set, RAM[$EC] −1 per pass (lst:317-338).
- **(i) Game start.** Word 3 of the player records is `$19` before the start (set on the game-over/attract path, lst:86-90), so P1 (and P2) respawn in the start pass: P1 word A becomes `$01C` at f202 in all 9 runs, and the first wave spawns 50 frames later at f252 (p7_checks (f)).

### 20.2 Canister start table

The 16 byte pairs at `$0F9A` (lst:3685-3690), ×4 = field position (×8 in RAM `$B0`, which stores position ×2), in table order: (512,360), (512,408), (536,384), (488,384), (544,352), (480,352), (544,416), (480,416), (568,384), (512,328), (456,384), (512,440), (456,328), (456,440), (568,328), (568,440). A game uses the first N (DIP: 12/16/4/8). Unused slots hold 1 in both words (gone).

### 20.3 Player walls, canister contact, tow offset

- **Wall clamp and bounce** (lst:2288-2334). When a move takes a player off the field, x is clamped to 0/`$3FF` (y to 0/`$2FF`) and the heading is set from a 4-entry table indexed by the quarter count q that `$1204` returns for (heading − 90): x walls 186, 174, 6, 354; y walls 84, 276, 264, 96 (bytes at `$0FBA`/`$0FBE` ×2, lst:3689-3690). Of 142 traced frames arriving at a wall, 123 have a table heading, 18 are plain moves that land exactly on the edge without leaving, and 1 (tp_wander f4532) is reproduced exactly by the replay (p7_checks (d)).
- **Canister contact jitter** (`$0BDD-$0C0A`, lst:2804-2843). A towing enemy, or a player (whose word A has bit 1 set during its call, lst:2335-2349), within 20 of a free canister does not pick it up. Its word B steps once, draw scale (word 4) += B & `$F`, heading += (B & `$F`) − 8.
- **Tow offset** (lst:3182-3228). The towed canister is drawn at own + R(heading)·(0, `$20`)·word4/128, i.e. 20 behind the enemy at scale `$50`. Word 4 includes this pass's jitter. 28,491 of 28,493 on-field towing frames match. The other 2 differ only in the taken bit: in the same frame another record with the same canister pointer ended its explosion, which clears x bit 0 (lst:3117-3121) (p7_checks (e)).

### 20.4 atan2 at `$1000`, decoded

- Table at `$17EB` (rows from `$17AB`), indexed [max][min] for 0 ≤ min ≤ max ≤ 31: floor(90 − atan(min/max)° + 0.5), with [0][0] = 45. Seven entries differ from that formula: [10][1] = 85, [22][6] = 76, [24][21] = 50, [27][8] = 74, [30][23] = 54, [31][19] = 59, [31][23] = 55.
- Quadrant code from the signs (lst:3707-3740): dx > 0 and dy > 0 → 1; dx > 0, dy ≤ 0 → 4; dx ≤ 0, dy > 0 → 2; otherwise 3. Both magnitudes are halved while either is ≥ 32 (lst:3741-3766). v = T[max][min]; e = v if |dx| < |dy| else 90 − v; for an even quadrant code e = 90 − e; result = e + 90·(0 for code 4, else the code) (lst:3809-3846).
- So (0, 0) gives 315, and the result can be 360. At the spawn pass C/D equal the own position, so record `$00` turns from the table heading toward 315: 154 of 154 traced spawn passes (p7_checks (g)).
- **$1204 rounding** is round-half-up, (t·scale + 128) >> 8. It fits all 32,057 moving enemy frames without a heading change; floor fits 24,798 (p7_checks (a)).

### 20.5 Replay result

`tools/remake/Replay` compares, every frame from f202, enemy words A, X, Y, heading, speed, B, C, D, F and the canister pointer; P1/P2 A, X, Y, heading and speed; all 16 canister slots; RAM[$EC]; and both scores. idle1, tp_idle, tp_pvp1 and tp_pvp2 match in every frame to the end of the log (f12000 / f6000). shoot1, ram1, wander1, tp_wander and tp_shoot2 match through game over and diverge only 123-127 frames later, when the attract demo starts (not simulated) (p7_checks "replay"). The unit tests replay seven of these runs against 100-frame checkpoints.

### 20.6 Still UNCHECKED after pass 7

1. The chase C/D refresh gate (19.7 item 1). The simulation follows the literal code: C/D are refreshed every chase pass unless separation steers. The replays compare C/D in every frame and match, so in these 9 runs that reading accounts for every frame. Whether the 401 frames of 19.4 all come from these runs was not checked.
2. P1's chase distance |dx| vs max(|dx|, −dy) (19.7 item 2).
3. The background drone pitch mapping, the BONUS beep phase. (Resolved in 21.3/21.4.)
4. The jitter heading when h ≥ 360: the literal reading of `$0BFB-$0C00` stores 360.
5. The fire-line frame index (taken as A8 & 3).
6. Coinage (one coin = one credit assumed), the credit limit, the attract demo, high-score handling, tread and explosion animation, and the exact score/BONUS glyph layout.

## 21. Pass 8 / Milestone 2: sound board model and controller input

Pass 8 adds runtime-synthesized sound (`src/RipOff.Core/Audio`) and game-pad input (`src/RipOff.Game/PadMapper.cs`). No new MAME runs: the pass-6 sound-lab WAVs and RAM logs (19.3) were re-measured by `trace:scripts/p8/sound_measure.py`; the derived numbers are in `trace:excerpts/p8_sound.txt` (sections (1)-(10)). No audio from MAME is committed or shipped; every sample is computed by our code from the parameters below. Netlist = MAME `src/mame/cinematronics/nl_ripoff.cpp` at tag mame0276 (the box copy is identical to the tag); MM5837 = `src/lib/netlist/devices/nld_mm5837.cpp` at mame0276.

### 21.1 Command byte → board

- The sound routine shifts RAM[$FC] out LSB first (`SHRB` loop `$017D-$0193`, lst:364-384) into IC8 74LS164 and strobes IC9 74LS377 (lst:356). After 8 clocks QA..QF hold bits 7..2, so IC9.2 = bit 7, IC9.5 = bit 6, IC9.6 = bit 5, IC9.9 = bit 4 (IC22 CLR), IC9.12 = bit 3 (beep), IC9.15 = bit 2 (motor) (netlist :380-408, and IC9.12 / IC9.15 in the Beep / Motor blocks). This agrees with the forced-bit results of 19.3, which established bits 2-4 empirically.
- Timing: the byte is built and latched at the start of the pass, so a bit written later in the pass by the wave code (bit 2: `$0348`, `$0266`) reaches the board one pass later; in the game-over pass the latch still holds the in-play value (21.4).

### 21.2 Cue models (`SoundBoard`, `Circuits`)

48 kHz mono, one buffer per pass (1263/1264 samples, exact 48000/38 on average). Levels: each cue alone is scaled at start-up (deterministic calibration render) so its rms equals the MAME rms in p8 (6)/(8)/(10) × master 0.6 (MAME full scale 32768 = 0.6); then a soft limiter (unity to 0.8, tanh knee, |y| < 0.99). The MAME base run itself clips at 32767 (p8 (10)).

| Cue | Trigger (sim → board) | Model and source of each parameter |
|---|---|---|
| Explosion | `Explosions > 0` (Q7 falling edge) | Noise: MM5837 17-bit register, seed `0x1FFFF`, taps bits 0 and 3, one shift per sample (FREQ default 48 kHz) — exact port. Filters (one-pole approximations of the netlist RC network): R1 47k/C2 0.1 µF low-pass 33.9 Hz; R2 47k + C3 0.1 µF into IC2 high-pass 33.9 Hz; IC2 feedback R3 330k ‖ C4 0.02 µF low-pass 24.1 Hz; R4 10k + C5 0.1 µF + R5 2.7k high-pass 125 Hz. Envelope: IC4 555 monostable T = 1.1·R8·C6 = 1.1·470k·0.68 µF = 0.3516 s hold; a trigger while timing does not extend it; decay τ = 141 ms **measured** (p8 (6) fit; the netlist RC R10·C8 = 20k·4.7 µF = 94 ms is slower in MAME's output — UNCHECKED why); attack 2 ms UNCHECKED |
| Laser | `Laser` (Q4 = 0; counter IC18 out of CLR) | IC18 2QA..2QD → R65 82k, R64 39k, R62 10k, R63 20k (DAC weights). The netlist's VCO is itself an offline-fitted HLE (`HLE_LASER_VCO`, :568-597) of an unknown control voltage, so the sweep is **measured**: tone period P(t) = 0.279 ms + 0.0189·t (p8 (8), 14 windows 37-107 ms, one in-game burst, max residual 0.035 ms); clock half-period = P/16 (QC, the 10k bit, toggles every 8 clocks). Restarts at each off→on. After 107 ms held (UNCHECKED). |
| Torpedo | `Torpedo` (Q3 = 0) | IC18 1QA..1QD → R75 82k, R74 39k, R72 10k, R73 20k. Sweep **measured** (p8 (8), 103 bursts): period 2.75 ms at 21 ms → 4.06, 5.42, 6.56, 7.42, 7.94, 8.33 → 8.55 ms (117 Hz plateau, p8 (9)) at 161 ms; linear interpolation; t = 0 value 1.37 ms extrapolated (UNCHECKED). |
| Background | `!BackgroundMuted` (bit 4 = 0 releases IC22 CLR), `BackgroundLevel` bits 7-5 | Two VARCLOCKs OR'd (AFUNC max, :440-454) clock IC22 (falling edges) → R86 10k (QA), R87 20k, R88 39k, R89 82k. Slow clock half-period HP1 = clamp(−0.0235033·V + 0.0179360, 1e-6, 0.1) (netlist formula). V = −470·10 V·Σ(off bits) (1/2k, 1/4k, 1/8k): ideal open-collector 7406 and a 10 V D1 zener assumed (UNCHECKED). Tone: **measured** f0 for v = 0..6 (p8 (5)), clock half-period = 1/(4 f0); v = 7 from the netlist HP2 at 0 V (975 Hz, UNCHECKED: never seen). |
| Motor | `Motor` (bit 2 = 0) | IC11 555 astable R31 68k, R32 30k, C14 0.22 µF (51.2 Hz, duty 0.77) + IC13 555 R37 56k, R38 24k, C17 0.1 µF (138.7 Hz, duty 0.77), equal weights (R33 = R39 = 15k, C16 = C19 = 0.1 µF into R34 2.7k), ~90 Hz coupling high-pass; hard gate (Q4/Q5 → R36 bias). D12/D13 zener clamp not modelled (UNCHECKED). |
| Beep | `Beep` (bit 3 = 0) | IC5 555 astable R13 4.7k, R14 8.2k, C9 0.047 µF: 1455 Hz, duty 0.61 (MAME: 1462 Hz, 0.59, p8 (4)); R15 15k/C11 0.1 µF/R16 2.7k coupling ~90 Hz; hard gate (Q2/Q3 → R18). |
| Output | — | OUTPUT = R45.1 = R49.2. IC7 path (explosion, beep, motor) → C12 0.1 µF → R45 27k; DAC paths → R48 2.7k (R91 2.7k) → C21 (C32) 0.68 µF → R49 470. With the output node unloaded (UNCHECKED) each capacitor sees the loop ≈ 31k (R45 + R49 + R48 + R47 ‖ DAC): high-pass 51 Hz (C12) and 7.5 Hz (C21/C32). |

Comparison of the synthesized cues with p8 (probe run, not committed): laser 1016 Hz at 37 ms → 462 Hz at 101 ms (MAME 1017 → 454); torpedo 333 → 117 Hz (MAME 363 → 114-117); background v0 158 Hz / 5.1 Hz pulsing (MAME 161 / 5.1; FFT bin 2.9 Hz), v5 378 / 11.0 (MAME 378 / 10.3); motor lines 51, 102, 138-139, 277 Hz (MAME 51-52, 102-103, 139, 278). Explosion: hold then decay as in p8 (6), but the synthesized noise has less energy below 50 Hz (0.33 of the total vs 0.64 in MAME): **UNCHECKED** which part of the network (or the CA3080 bias step) produces MAME's extra low end.

### 21.3 Background pitch (resolves 19.7 item 3, 20.6 item 3)

Bits 7-5 = v, bit 7 most significant. Netlist prediction 1/(4·HP2(V)): 164, 186, 215, 255, 312, 404, 571 Hz for v = 0..6; MAME output 161, 182, 208, 246, 296, 378, 519 Hz (p8 (5)). Pulsing 1/(2·HP1): predicted 5.0, 5.0, 5.75, 6.8, 8.4, 11.0 Hz, measured 5.1, 5.1, 5.9, 6.6, 8.1, 10.3 (v = 6 measured 3.7 Hz, taken as a measurement artifact: UNCHECKED). The remake uses the measured tone and the netlist pulsing rate.

### 21.4 Command byte replay

`ReplayTests.SoundCommandByte` replays sl_base (the pass-6 shoot run: 6 waves and the BONUS screen), shoot1, ram1, tp_wander and tp_pvp1 and compares the simulation's sound flags with frame-end RAM[$FC] in **every** frame (`tests/RipOff.Core.Tests/Data/*.fc`, run-length RAM values only) through game over: motor vs the previous frame's bit 2, beep vs bit 3, background-off vs bit 4, pitch vs bits 7-5. All match after two fixes in `Simulation.Step`:

- the beep, motor and background flags are computed at the sound routine (start of pass) from the previous pass's state: the beep is on when RAM[$A8] bit 1 = 0 **before** the pass increment, and not on the pass that sets BONUS (sl_base f3265; p8 (7): 49 of 49 later BONUS frames);
- the motor in the game-over pass keeps its in-play value (ram1 f3434, shoot1 f4790, wander1 f4462, tp_wander f5007, tp_shoot2 f4784 in `tools/remake/Replay`).

The laser flag is still set in the pass that draws the fire line; the arcade writes OUT4 early in the pass (`$0168`), from the previous pass's bit 1, so the audible laser may lag the sim flag by one pass (26 ms): UNCHECKED, not compared (Q4 is not in RAM).

### 21.5 Controller input

MonoGame `GamePad` (SDL2 GameController on DesktopGL), pad index 0 → P1, index 1 → P2, read with `GamePadDeadZone.None`. Mapping in `PadMapper` (README "Controller"): turn = left stick X beyond 0.35 or D-pad; thrust = A or left trigger > 0.3; fire = X, RB or right trigger > 0.3 (one level signal; the sim's fire edge logic is unchanged). Remapped after M2: RT moved from thrust to fire, LT added as thrust; coin = Back/View (edge, either pad); start = Start/Menu on pad 0 → 1P, on pad 1 or LB + Start on pad 0 → 2P (edges). Pad and keyboard are OR'd. A disconnected pad reads neutral; a newly connected pad gets no edge on its first pass; `GamePad.GetState` failures read as disconnected. The 0.35 dead zone and 0.3 trigger threshold are design choices.
- **Held fire** (checked for the remap): one shot per press, no auto-repeat, for keyboard and pad alike. The arcade reads BUTTON1 (`$0AE3`/`$0AE7`, lst:2570-2574), compares it with the level saved in word 2 (`TST $2` / `JEQ`, lst:2578-2579: unchanged → no shot), stores it (lst:2580) and fires only if the new level is pressed (`SHR` / `JOS`, lst:2581-2582); GAME_RULES §3 row "BUTTON1 = fire, edge-triggered" (lst:2569-2582). `Simulation.cs` mirrors this (fire block at Simulation.cs:659-676). Holding fire for 200 passes gives exactly 1 shot (`PadMapperTests.HeldFireGivesOneShotInTheSimulation`).

### 21.6 Still UNCHECKED after pass 8

1. Explosion: decay τ (measured 141 ms vs R10·C8 = 94 ms), the 2 ms attack, the missing low end (21.2), the one-pole approximation of the filter network (loading between stages ignored), 555 retrigger behaviour.
2. Laser sweep beyond 107 ms and before 37 ms (one measured burst), and the possible one-pass lag of Q4.
3. Torpedo period at t < 21 ms (extrapolated).
4. Background: v = 7 tone (never observed), the v = 6 pulsing rate, the ideal-open-collector voltage assumption (only used for the pulsing rate).
5. Motor zener clamp; relative timing of 555 phases at power-up (all start at phase 0).
6. Items 1, 2, 4, 5, 6 of 20.6 (unchanged).

## 22. Milestone 3: online two-player co-op

Design decisions (code: `src/RipOff.Net`, `src/RipOff.Game/OnlineController.cs`, `src/RipOff.Game/Steam/`):

1. **Input-sync lockstep, no rollback.** The simulation is deterministic (§20), so peers exchange only one input byte per frame (L/R/thrust/fire/start). Input delay **3 frames** (79 ms at 38 Hz): about one-way latency on a typical relay plus jitter, still below what reads as sluggish for a tank game. The host sends it in the start message, so it can change without a protocol change. Rollback was not built (flagged): it needs state snapshots and a re-simulation of up to ~8 frames per tick. The sim is cheap, so it is possible later, but stalling ("waiting for other player") is simpler and cannot produce visual corrections.
2. **Transport.** Inputs are sent unreliably, every tick, with all unacknowledged frames repeated (≤ 64 per packet), so loss and reordering only cost latency. Hello/Start/StartAck/Reject/Bye are sent reliably. `ITransport` has three implementations: Steam (`ISteamNetworkingMessages`, channel 0, relay only: ICE disabled through `k_ESteamNetworkingConfig_P2P_Transport_ICE_Enable = Disable`), LiteNetLib 2.1.4 UDP (ReliableOrdered / Unreliable), and an in-memory loopback with latency, jitter, loss and duplication for tests.
3. **Desync detection.** FNV-1a over the whole `GameState` (`StateHash`) every 19 frames (twice a second). The last 4 hashes ride on every input packet. A mismatch stops both games ("DESYNC at frame N") and each side writes a report (versions, start config, every frame's inputs) so the run can be replayed offline with `LockstepSession.StepFrame`. A desynced peer keeps sending its hashes so the other side detects it too.
4. **Handshake.** Hello carries protocol version (1), game key (`Branding.LobbyGameKey`) and role; a mismatch is rejected with the reason. The host then sends Start (input delay, `GameConfig`, pass counter, 5 RNG seeds). The guest validates the `GameConfig` and echoes a hash of the start message; the host compares it, and no frame is simulated before that matches. `GameConfig` = canister count + `AutoFire` (reserved: this build refuses `AutoFire = true` until the simulation implements it).
5. **No coins online.** The simulation still has the arcade credit logic for local play. Online sessions never send coins or credits: in attract mode a Start press from either player calls `Simulation.StartGame(two-player)` directly. Game over is the arcade one: all canisters gone.
6. **Steam.** Steamworks.NET 2025.164.1 standalone (latest GitHub release on 2026-10-16; NuGet only has 2024.8.0), App ID 480. Public lobby, 2 members, data `game` / `ver` / `code`. Every `RequestLobbyList` filters on `game` and `ver` (and `code`) with `AddRequestLobbyListStringFilter`, worldwide distance filter. A joiner also re-checks `game` and `ver` after entering. The 6-character codes avoid 0/O/1/I. Invites: `ActivateGameOverlayInviteDialog`, `GameLobbyJoinRequested_t`, rich presence `connect = +connect_lobby <id>` → `GameRichPresenceJoinRequested_t`, and `+connect_lobby` on the command line. The host accepts networking sessions only from its lobby member. `SteamAPI.InitEx` runs in `Program.cs` before the graphics device exists (overlay requirement). On failure the game continues with local/direct-IP play and shows the reason.

UNCHECKED (no Steam client or Windows machine on the build box):

1. Every Steam runtime path: lobby create/search/join, codes, invites through the overlay and friends list, rich-presence join, `+connect_lobby`, session accept, relay traffic and its latency, ICE disable taking effect, overlay showing with 480 outside the Steam client. Only "Steam missing → clean fallback" was run (Linux box: `InitEx` returns FailedGeneric, no steamclient.so).
2. The published win-x64 zip running on Windows (contents verified by cross-publishing on Linux only). The MonoGame window/menu is also not run on the box (no OpenGL); the menu was checked by rendering its line list to PNG.
3. Whether 3 frames of delay feels right over real relays; LAN direct-IP across two real machines and Windows Firewall prompts (only localhost UDP was tested).

## 23. Milestone 4: game front end, no credits, auto-fire

Code: `src/RipOff.Game/FrontEnd.cs` (state machine), `InputReader.cs`, `PadMapper.cs`, `OnScreenKeyboard.cs`, `Settings.cs`; Core changes in `Simulation.cs`, `State.cs`, `GameConfig.cs`.

1. **Credits removed.** `FrameInput` has no coin/start fields and `GameState.Credits` is gone. `Simulation.Step` no longer models the coin/start code (lst:197-255). Attract mode just idles, and the front end starts games with `Simulation.StartGame(onePlayer, GameConfig)`. The replay tests already injected the start with `StartGame` and are unchanged apart from dropping `Credits`; they still match the arcade RAM checkpoints, since the RAM comparison never covered `$F3`. The attract screen draws no credit count. Online, a Start bit in attract mode calls `StartGame` (§22.5); the front end sends it from GAME OVER → PLAY AGAIN.
2. **Auto-fire (remake option, not arcade).** `GameConfig.AutoFire` → `GameState.AutoFire`. The press edge fires exactly as before (lst:2562-2656, so OFF = arcade). With it ON, a held button also fires every `GameData.AutoFireInterval` = **6 passes** (158 ms, ≈ 6.3 shots/s). Per-player counter `AutoFireTimer`, reset on release and on every shot. With all 4 slots busy the timer stays due and the shot leaves when a slot frees, so the 4-shot cap holds and the rate never exceeds 1 per 6 passes. Both values are in `StateHash`, so a desync would show. Protocol version 2 (v1 builds refused auto-fire).
3. **Menu tree / routing.** Screens form a stack: Splash, Main, Online, Options, Keyboard, Connecting, LocalGame, OnlineGame, Pause, GameOver.
   - Back (Esc / B) pops the stack. It does nothing on the main menu, and Esc never quits.
   - In a game, Esc or pad Start pushes Pause. Pad B does nothing in a game; it is not a game control, so a stray press cannot pause.
   - In Pause, Esc / B / Start resume.
   - F10 pushes Options from any screen except the keyboard (from the splash it goes to Main first).
   - Pad Start does nothing in other menus. Flagged choice: Nick asked that Start only open the pause menu.
   - Only the EXIT items set `ExitRequested`.
   - A local game runs only while it is the top screen, so every menu pauses it.
4. **Online pausing (flagged).** The shared simulation never pauses. The menu is drawn over the dimmed, still-running game, and the local player's input reads as released while it is open. Pausing both sides would let either player freeze the other indefinitely and would need protocol messages. The overlay costs the pausing player nothing extra beyond their own idle tank.
5. **Options (flagged choices):**
   - Volume 0-100 % in 10 % steps.
   - Dead zone 10-60 % in 5 % steps (default 35 %).
   - Fullscreen is borderless at desktop resolution (`HardwareModeSwitch = false`), which avoids a display mode change.
   - STEAM AT LAUNCH (applies on the next start) was added because of the Steam Input issue in 6.
   - Canisters 4/8/12/16 are the arcade DIP values.
   - Auto-fire and canisters apply at the next game start and are the host's values online.
   - Settings are JSON under `Environment.SpecialFolder.ApplicationData/RipOffRemake/settings.json`, saved after every change via a temporary file. A corrupt file is renamed `.corrupt` and defaults are used; out-of-range values are clamped.
6. **Controller in menus (Nick's report on M3).** Two causes were found and both are fixed or documented:
   - **Code:** pads were read only at MonoGame indices 0 and 1, so a pad that SDL put at another index was invisible; the stick-as-direction depended on MonoGame's `LeftThumbstick*` button mapping. M4 reads indices 0-3 for menus, takes the first two *connected* pads as P1/P2, and uses its own stick threshold (0.5). `InputReader` is pure and tested with fake pad states on every screen.
   - **Steam Input:** with App ID 480, Steam treats the game as Spacewar and redirects controller input to Steam Input (Valve engineer, Steamworks.NET issue #380). The workarounds are documented in the README, plus the STEAM AT LAUNCH option. UNCHECKED: which of the two it was on Nick's machine.

UNCHECKED: the MonoGame window itself (fullscreen switching, TextInput, real pads through SDL) has not run on the box (no OpenGL). The front end is tested through `InputReader` + `FrontEnd` with fake states, and the screens were checked by rendering their line lists to PNG.
