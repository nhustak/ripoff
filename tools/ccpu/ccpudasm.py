#!/usr/bin/env python3
"""
Cinematronics CCPU single-instruction disassembler.

A line-for-line Python port of MAME's src/devices/cpu/ccpu/ccpudasm.cpp
(mamedev/mame commit 7c11d9e0a86996c66be9ee8fa7f1cf0e7fd8d2f1, BSD-3-Clause,
Aaron Giles). Mnemonics, operand formatting and returned lengths are kept
identical to MAME so the output can be diffed against the compiled C++
original (see tools/ccpu/verify/).

disasm(read8, pc) -> (length, text, is_conditional_step)
  read8(addr) returns the byte at program address addr.
"""

# ccpudasm.cpp lines 87-166: conditional jumps flagged STEP_COND
_COND = {0x51: "JMIB/JEHB", 0x52: "JVNB", 0x53: "JLTB", 0x54: "JEQB",
         0x55: "JCZB", 0x56: "JOSB",
         0x59: "JMI/JEH", 0x5a: "JVN", 0x5b: "JLT", 0x5c: "JEQ",
         0x5d: "JCZ", 0x5e: "JOS"}

# ccpudasm.cpp lines 82-84, 122-129, 168-171, 237-343: single-byte, no operand
_FIXED = {0x50: "T4K", 0x57: "SSA", 0x58: "JMP", 0x5f: "NOP",
          0xe0: "DV", 0xe1: "LPAP", 0xf1: "WSP",
          0xe2: "LKP", 0xf2: "LKP", 0xe3: "MUL", 0xf3: "MUL",
          0xe4: "NV", 0xf4: "NV", 0xe5: "FRM", 0xf5: "FRM",
          0xe6: "STAP", 0xf6: "STAP", 0xf7: "CST", 0xe7: "ADDP",
          0xe8: "SUBP", 0xf8: "SUBP", 0xe9: "ANDP", 0xf9: "ANDP",
          0xea: "LDAP", 0xfa: "LDAP", 0xeb: "SHR", 0xfb: "SHR",
          0xec: "SHL", 0xfc: "SHL", 0xed: "ASR", 0xfd: "ASR",
          0xee: "SHRB", 0xfe: "SHRB", 0xef: "SHLB", 0xff: "SHLB",
          0xf0: "IV"}

# ccpudasm.cpp lines 30-44, 51-70, 173-235: high nibble selects op, low nibble is operand
_NIBBLE = {0x0: "LDAI $%X", 0x1: "INP  $%X", 0x6: "ADD  $%X", 0x7: "SUB  $%X",
           0x8: "SETP $%X", 0x9: "OUT  $%X", 0xa: "LDA  $%X", 0xb: "TST  $%X",
           0xc: "WS   $%X", 0xd: "STA  $%X"}


def disasm(read8, pc):
    op = read8(pc)
    hi, lo = op >> 4, op & 0x0f
    if hi in _NIBBLE:
        return 1, _NIBBLE[hi] % lo, False
    if op == 0x20:                                   # A8I  (ccpudasm.cpp:47-49)
        return 2, "A8I  $%X" % read8(pc + 1), False
    if hi == 0x2:                                    # A4I  (ccpudasm.cpp:52-57)
        return 1, "A4I  $%X" % lo, False
    if op == 0x30:                                   # S8I  (ccpudasm.cpp:60-62)
        return 2, "S8I  $%X" % read8(pc + 1), False
    if hi == 0x3:                                    # S4I  (ccpudasm.cpp:65-70)
        return 1, "S4I  $%X" % lo, False
    if hi == 0x4:                                    # LPAI (ccpudasm.cpp:73-79)
        t = read8(pc + 1)
        return 2, "LPAI $%03X" % (lo + (t & 0xf0) + ((t & 0x0f) << 8)), False
    if op in _COND:
        return 1, _COND[op], True
    return 1, _FIXED[op], False


if __name__ == "__main__":
    # Mode used by verify/compare.sh: disassemble at EVERY byte offset of an
    # image (address mask 0x1fff), printing "addr len cond text".
    import sys
    img = open(sys.argv[1], "rb").read()
    mask = len(img) - 1
    rd = lambda a: img[a & mask]
    for pc in range(len(img)):
        n, txt, c = disasm(rd, pc)
        print("%04X %d %d %s" % (pc, n, int(c), txt))
