# Rip-Off CCPU disassembly: notes (passes 1 and 2)

Status: first pass and second decode pass, both done 2026-10-16. This is static analysis only; the ROM was never executed. Line numbers (`lst:N`) refer to the listing as regenerated in pass 2; pass-1 citations were remapped by address.

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
- The 4-byte table at `$0FC2` (`15 0F 3F 1F`) is indexed by the same DIP index and stored to RAM[$78] and RAM[$7A] (lst:2892-2898, lst:3691-3692). Its meaning is **UNCHECKED**.

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
  - Word 6 rises by 1 toward word 3 on frames where RAM[$A8] bits 0 and 1 are both clear (lst:1842-1854). RAM[$A8] += 1 each frame at lst:4587-4590, so that is every 4th frame.
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
    - word 0 of its canister (via RAM[$D2]) += 1, which sets the "gone" bit 0 that the canister draw loop and game-over test check.
  - How bit 1 gets set (the pick-up) is **UNCHECKED**.
- **14.5 Collisions** (all use `$11E0`: hit when |dx| < r and |dy| < r, r in RAM[$E4]).
  - **Player shots vs enemies:** r = `$14` (20) (lst:3482-3484), for records `$00-$2F` only (lst:3587-3591); players are never hit by shots.
    - On a hit: the enemy's word A &= `$F`, word 4 (draw scale) := 2, word C := 0; the shot slot is freed; Explosion is pulsed (lst:3513-3549); score as in section 13.
  - **Player tank vs any other record** (`$00-$4F` except itself): r = `$18` (24) (lst:2335-2367, loop lst:2350-2557).
    - Victim: word A &= `$F`, word 4 := 3, word C := 0, Explosion (lst:2398-2421).
    - Enemy victims score.
    - Then every enemy gets word-A bit 2 set: (A & `$1B`)+4 (lst:2457-2480).
    - The player's own scale := 3 and its word A &= `$F`, with a second Explosion pulse (lst:2481-2494). Interpretation, **UNCHECKED**: both tanks are destroyed and the remaining enemies switch to their bit-2 behaviour.
  - **`$1158-$11B7`:** when an enemy is close to its target point and that point equals a live player's position, the player record gets word A &= `$F` and scale 3, Explosion pulses, and all enemies get bit 2 set. This looks like the enemy destroying the player. The exact trigger (the `$1158`/`$1161` thresholds 4 and 3) is **UNCHECKED**.
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
  - The steps `$1F`/`$3F`/`$0F`/`$15` for 8/4/16/12 canisters make N steps sum to about 248-252, i.e. one full sweep of bits 7-5 over the game. This is arithmetic from the table; the audible effect is **UNCHECKED**.
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

- `$11B8`: shifts and updates the state from word B;
- `$081D-$0850`: arrival within `$48` of the target point;
- `$1123-$1160`: close-range tests, including `$96`;
- `$0850-$08C5`: random hops, now read (14.3).

These decide when an enemy switches from canister-seeking to dragging, when it chases a player, and when enemies fire. Is there enemy fire? None of the paths read so far creates a projectile for an enemy. **UNCHECKED**.

Decoding this needs the full data flow of word B and words C/D across the copy-back in `$0CAA`, through several indirect stores (`WS`/`WSP`) that the static analysis cannot resolve. It is possible statically, but it is long hand-tracing with a real risk of misreading the bit tests. Running the game would settle it quickly, and that is forbidden here.
