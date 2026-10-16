#!/usr/bin/env python3
"""
Static (never executes the ROM) recursive-descent listing for the Rip-Off
CCPU program image.

Instruction text comes from ccpudasm.py (verified byte-identical to MAME's
ccpudasm.cpp, see verify/compare.sh). Control flow and register effects
follow MAME's CCPU core, src/devices/cpu/ccpu/ccpu.cpp at mamedev/mame
7c11d9e0a86996c66be9ee8fa7f1cf0e7fd8d2f1; the line numbers in comments below
refer to that file.

Analysis model (abstract interpretation over a flat constant lattice):
  * one state per (address, active accumulator A/B); states are joined
    (differing values become unknown) until a fixpoint is reached
  * tracked: active accumulator, A, B, J, P, I, X, Y and RAM words holding
    known constants
  * reset state: everything zero, accumulator A (ccpu.cpp:173-197)
Anything the analysis cannot resolve (jump with unknown J, table lookup
with unknown accumulator) is reported, not guessed.

Address model (cinemat.cpp:280-285, program_map_8k): region offsets
0x0000-0x0FFF appear at CPU $0000-$0FFF (mirror $1000-$1FFF); region offsets
0x1000-0x1FFF appear at CPU $2000-$2FFF (mirror $3000-$3FFF). Jumps keep the
current 4 KB page (ccpu.cpp:348 "(m_PC - 1) & 0xf000"), so every jump and
LKP stays in the same region half. T4K jumps to (P<<12)+J (ccpu.cpp:301).
The listing shows region offsets; "bank 1" lines are CPU $2xxx.

usage: listing.py IMAGE.bin OUT.lst [OUT.json]
"""
import json, sys
from ccpudasm import disasm

M12 = 0xfff
UNK = None


def asr(x):                       # ccpu.cpp:50
    return ((x >> 1) | (x & 0x800)) & M12


def cpu_to_region(cpu):           # cinemat.cpp:282-284 (global_mask 0x3fff)
    cpu &= 0x3fff
    return (cpu & 0x0fff) | (0x1000 if cpu & 0x2000 else 0)


def region_to_cpu(r):
    return (r & 0x0fff) | (0x2000 if r & 0x1000 else 0)


# --- I/O meaning tables (all from MAME, cinemat.cpp / cinemat_a.cpp / nl_ripoff.cpp)
INPUT_BITS = {   # cinemat.cpp:705-717 ripoff INPUTS, read via inputs_r (cinemat.cpp:83-86,313)
    0: "P2 left (active low) [cinemat.cpp:707]",
    1: "START1 (active low) [cinemat.cpp:708]",
    2: "P2 right (active low) [cinemat.cpp:709]",
    3: "START2 (active low) [cinemat.cpp:710]",
    4: "P2 BUTTON2 (active low) [cinemat.cpp:711]",
    5: "P2 BUTTON1 (active low) [cinemat.cpp:712]",
    12: "P1 left (active low) [cinemat.cpp:714]",
    13: "P1 BUTTON1 (active low) [cinemat.cpp:715]",
    14: "P1 right (active low) [cinemat.cpp:716]",
    15: "P1 BUTTON2 (active low) [cinemat.cpp:717]",
}
for _b in range(6, 12):
    INPUT_BITS[_b] = "unused bit %d [cinemat.cpp:713]" % _b
SW_SHUFFLE = [2, 5, 4, 3, 0, 1, 6, 7]   # cinemat.cpp:91
SW_BITS = {0: "DIP Lives bit0 (0x01) [cinemat.cpp:720-724]", 1: "DIP Lives bit1 (0x02) [cinemat.cpp:720-724]",
           2: "DIP Coinage bit0 (0x04) [cinemat.cpp:725-729]", 3: "DIP Coinage bit1 (0x08) [cinemat.cpp:725-729]",
           4: "DIP Demo Sounds (0x10, 1=On) [cinemat.cpp:730-732]", 5: "DIP Scores (0x20, 1=Combined) [cinemat.cpp:733-735]",
           6: "Service (0x40, active low) [cinemat.cpp:736]"}
OUT_BITS = {     # LS259 Q0-Q7 via io_map (cinemat.cpp:317)
    0: "sound shift-reg DATA -> 74LS164 IC8.B [nl_ripoff.cpp:380-382]",
    1: "sound shift-reg CLOCK -> 74LS164 IC8.CLK [nl_ripoff.cpp:383-385]",
    2: "sound latch STROBE -> 74LS377 IC9.CP [nl_ripoff.cpp:388-390]",
    3: "sound 'Torpedo' [nl_ripoff.cpp:621-625]",
    4: "sound 'Laser' [nl_ripoff.cpp:559-563]",
    5: "coin reset (rising edge clears coin flag) [cinemat.cpp:1027,124-129]",
    6: "vector brightness: 1=dim 0x80, 0=bright 0xff [cinemat.cpp:1028, cinemat_v.cpp:54-58]",
    7: "sound 'Explosion' [nl_ripoff.cpp:338-342]",
}


# Data regions identified from the code that reads them (see disasm/NOTES.md).
# Printed as comments; each starts a new DB row.
DATA_LABELS = {
    0x0F82: "enemy wave spawn points: 8 (x,y) byte pairs, x8, indexed by RAM[$0B]&$E at $034C-$0378 (NOTES 14.2)",
    0x0F92: "enemy wave spawn headings: 8 bytes, x2 = degrees, read at $037D-$0389 (NOTES 14.2)",
    0x0F9A: "16 (x,y) byte pairs read by LKP at $0C5D, scaled x8 into RAM $B0..; canister positions (drawn as triangles by $13A1, NOTES 10.3)",
    0x0FC2: "4-byte table indexed by the Lives DIP value, read by LKP at $0C3D into RAM[$78]/[$7A]: background-sound step per stolen canister (NOTES 15)",
    0x0FC6: "end of $0FC2 table",
    0x0FD0: "odd bytes $0FD1-$0FFF spell ASCII '(C)1980CINEMATRONICSINC.'; byte differences here also give the enemy shape pointers ($02D1-$0317, NOTES 10.2)",
    0x15E9: "byte-pair table read by LKP at $1557/$155C (use UNCHECKED)",
    0x1620: "glyph offset index, 13 bytes, read by LKP at $1589; glyph n at $162D+index[n]",
    0x162D: "glyph vector data (format: see tools/ccpu/shapes.py), glyphs 0-9, BONUS, LEVEL, +",
    0x172C: "glyph-format triangles; pointers set at $13DC/$13E2 (+$12 at $13EC)",
    0x1750: "sine table, 91 bytes: round(256*sin(k deg)) k=0..90 (255 at k>=87), read by LKP at $1229/$1247 (NOTES 9.1)",
    0x17AB: "atan row-offset table: 32 pairs, (b0<<4)+b1 = n(n+1)/2, read by LKP at $1054/$105E (NOTES 9.3)",
    0x17EB: "triangular atan table: [max][min] = 90 - atan(min/max) in degrees, read by LKP at $1068 (NOTES 9.3)",
    0x19FB: "object sprite stream (rotated format): player/enemy bodies with headers + headerless tread frames (NOTES 10)",
    0x1F68: "rotated-format shape read via LKP at $128E (header pair, then points)",
    0x1F89: "rotated-format shapes $1F89/$1F92/$1F9B/$1FA4 (header pair each)",
    0x1FAE: "rotated-format shape entered at $12A6 (no header); draws with OUT $6 toggling ($1315-$1321)",
}


class State:
    """Abstract CPU state. Values are int or None (unknown). `w` holds the
    names of registers whose value is WEAK: derived from a RAM slot that an
    earlier store to an unknown address might have overwritten. RAM entries
    are addr -> (value, weak)."""
    __slots__ = ("acc", "A", "B", "J", "P", "I", "X", "Y", "Jslot", "Aslot", "Bslot", "ram", "w")
    # Jslot: RAM slot J was loaded from by LPAP; Aslot/Bslot: RAM slot the
    # accumulator was copied from unchanged (LDA/LDAP), used to follow saved
    # return addresses from slot to slot
    REGS = ("A", "B", "J", "P", "I", "X", "Y", "Jslot", "Aslot", "Bslot")

    def __init__(self, **kw):
        for k in self.__slots__:
            setattr(self, k, kw.get(k))
        if self.w is None:
            self.w = frozenset()

    def copy(self):
        s = State(**{k: getattr(self, k) for k in self.__slots__})
        s.ram = dict(self.ram)
        return s

    def key(self):
        return (self.acc,) + tuple(getattr(self, k) for k in self.REGS) + (
            tuple(sorted(self.ram.items())), tuple(sorted(self.w)))

    def join(self, o):
        s = self.copy()
        w = set(self.w) | set(o.w)
        for k in self.REGS:
            if getattr(s, k) != getattr(o, k):
                setattr(s, k, UNK)
        ram = {}
        for a, (v, wk) in self.ram.items():
            if a in o.ram and o.ram[a][0] == v:
                ram[a] = (v, wk or o.ram[a][1])
        s.ram = ram
        s.w = frozenset(k for k in w if getattr(s, k) is not UNK)
        return s

    def get(self):
        return getattr(self, self.acc)

    def put(self, v, weak=False, slot=None):
        setattr(self, self.acc, v)
        setattr(self, self.acc + "slot", slot)
        self.setw(self.acc, weak and v is not UNK)

    def isw(self, r):
        return r in self.w

    def setw(self, r, weak):
        self.w = (self.w | {r}) if weak else (self.w - {r})


def reset_state():                # ccpu.cpp:173-197 (RAM contents unknown)
    return State(acc="A", A=0, B=0, J=0, P=0, I=0, X=0, Y=0, ram={})


class Analyzer:
    def __init__(self, img):
        self.img = img
        self.key_slots = set()    # RAM slots used by LPAP; states are not joined across their values

    def rd(self, r):
        return self.img[r & 0x1fff]

    def reset_records(self):
        self.targets = {}         # target addr -> set of source addrs
        self.weak_targets = set() # (src, dst) resolved through a WEAK value
        self.unresolved = {}      # addr -> reason
        self.lkp = {}             # addr -> set of table addrs (None = unknown index)
        self.lpap_slots = set()
        self.wild_stores = set()  # addrs of stores to an unknown RAM address
        self.outvals = {}         # addr -> set of written bit values (None = unknown)
        self.slot_consts = {}     # RAM slot -> set of known constants stored there
        self.copy_edges = set()   # (src slot, dst slot) copies via LDA/LDAP then STA/STAP
        self.calls = {}           # jump addr -> (target, return point): A == address after the jump
        self.slotset_targets = set()  # (src, dst) taken from slot_consts for an unknown J
        self.store_log = {}       # store site -> set of (slot or None, value or None)
        self.retsites = {}        # JMP/T4K site whose J came from LPAP -> set of slots

    def indirect(self, a, s, how, mk):
        """J unknown: report it, never guess a target."""
        self.unresolved[a] = how + " with unknown J" + (
            "" if s.Jslot is UNK else " (J loaded from RAM[$%02X] by LPAP)" % s.Jslot)
        return []

    def maybe_call(self, a, st, tgt, out):
        """Calling convention seen in this ROM (e.g. region $004D-$0052 ->
        $0C1E-$0C1F): the caller puts the address of the instruction after
        the jump into A, the callee saves it with STA and later returns with
        LPAP + JMP/T4K. If A holds exactly ((a+1) & 0xfff) at a JMP/T4K, the
        byte after the jump is entered as a RETURN POINT with an all-unknown
        state. The accumulator there is A for a same-bank callee (JMP return,
        NEXT_ACC_A) and B for a cross-bank callee (T4K return toggles,
        ccpu.cpp:302); that last part is an inference (UNCHECKED)."""
        if st.A is not UNK and st.A == ((a + 1) & M12) and not st.isw("A"):
            ret = a + 1
            self.calls[a] = (tgt, ret)
            acc = "A" if (tgt & 0x1000) == (a & 0x1000) else "B"
            out.append((ret, State(acc=acc, ram={})))

    def add_target(self, src, dst, weak=False):
        self.targets.setdefault(dst, set()).add(src)
        if weak:
            self.weak_targets.add((src, dst))

    def step(self, a, st):
        """Apply the instruction at region addr a to state st; return [(next_addr, State)]."""
        op = self.rd(a)
        n_mame, text, _ = disasm(self.rd, a)
        hi, lo = op >> 4, op & 15
        bank = a & 0x1000
        s = st.copy()
        nxt = a + n_mame
        out = []

        def toggle():              # NEXT_ACC_B, ccpu.cpp:46
            s.acc = "B" if s.acc == "A" else "A"

        def to_a():                # NEXT_ACC_A, ccpu.cpp:45
            s.acc = "A"

        def acc_op(fn, v=0, vweak=False):
            """STANDARD_ACC_OP: *m_acc = fn(*m_acc, v) & 0xfff (ccpu.cpp:52-59)."""
            x = s.get()
            if x is UNK or v is UNK:
                s.put(UNK)
            else:
                s.put(fn(x, v) & M12, s.isw(s.acc) or vweak)

        def ramv(i):
            if i is UNK or i not in s.ram:
                return UNK, False
            v, wk = s.ram[i]
            return v, wk or s.isw("I")

        def jump(src, t, weak):
            self.add_target(src, t, weak)

        if hi == 0x0:              # LDAI ccpu.cpp:233-240
            s.put(lo << 8); to_a()
        elif hi == 0x1:            # INP ccpu.cpp:243-253
            s.put(UNK); to_a()
        elif op == 0x20:           # A8I ccpu.cpp:256-260
            acc_op(lambda x, v: x + v, self.rd(a + 1)); to_a()
        elif hi == 0x2:            # A4I ccpu.cpp:263-270
            acc_op(lambda x, v: x + v, lo); to_a()
        elif op == 0x30:           # S8I ccpu.cpp:273-277
            acc_op(lambda x, v: x + (v ^ M12) + 1, self.rd(a + 1)); to_a()
        elif hi == 0x3:            # S4I ccpu.cpp:280-287
            acc_op(lambda x, v: x + (v ^ M12) + 1, lo); to_a()
        elif hi == 0x4:            # LPAI ccpu.cpp:290-297
            t = self.rd(a + 1)
            s.J = lo + (t & 0xf0) + ((t & 0x0f) << 8); s.setw("J", False); s.Jslot = UNK; to_a()
        elif op == 0x50:           # T4K ccpu.cpp:300-303
            if s.Jslot is not UNK:
                self.retsites.setdefault(a, set()).add(s.Jslot)
            if s.P is UNK:
                self.unresolved[a] = "T4K with unknown P"
            elif s.J is UNK:
                toggle()
                return self.indirect(a, s, "T4K", lambda v: cpu_to_region((s.P << 12) + v))
            else:
                tgt = cpu_to_region((s.P << 12) + s.J)
                jump(a, tgt, s.isw("J"))
                self.maybe_call(a, st, tgt, out)
                toggle(); out.append((tgt, s))
            return out
        elif op in (0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x59, 0x5a, 0x5b, 0x5c, 0x5d, 0x5e):
            # conditional jumps ccpu.cpp:306-386; B forms toggle the accumulator, A forms select A
            (toggle if op < 0x58 else to_a)()
            if s.J is UNK:
                self.unresolved[a] = "conditional jump with unknown J"
            else:
                jump(a, bank + s.J, s.isw("J")); out.append((bank + s.J, s))
            out.append((nxt, s))
            return out
        elif op == 0x57:           # SSA ccpu.cpp:342-344
            toggle()
        elif op == 0x58:           # JMP ccpu.cpp:347-350
            if s.Jslot is not UNK:
                self.retsites.setdefault(a, set()).add(s.Jslot)
            to_a()
            if s.J is UNK:
                return self.indirect(a, s, "JMP", lambda v: bank + v)
            else:
                jump(a, bank + s.J, s.isw("J")); out.append((bank + s.J, s))
                self.maybe_call(a, st, bank + s.J, out)
            return out
        elif op == 0x5f:           # NOP ccpu.cpp:389-391
            to_a()
        elif hi in (0x6, 0x7, 0xa, 0xb, 0xc, 0xd):
            # I = (P<<4)+n; m_I is uint8_t (ccpu.h:81)
            s.I = UNK if s.P is UNK else ((s.P << 4) + lo) & 0xff
            s.setw("I", False)
            v, vw = ramv(s.I)
            if hi == 0x6:          # ADD ccpu.cpp:394-402
                acc_op(lambda x, y: x + y, v, vw)
            elif hi == 0x7:        # SUB ccpu.cpp:405-413
                acc_op(lambda x, y: x + (y ^ M12) + 1, v, vw)
            elif hi == 0xa:        # LDA ccpu.cpp:435-443
                s.put(v, vw, s.I)
            elif hi == 0xc:        # WS ccpu.cpp:463-470
                s.I = UNK if v is UNK else v & 0xff
                s.setw("I", vw and v is not UNK)
            elif hi == 0xd:        # STA ccpu.cpp:473-480
                self._store(a, s, s.get(), s.isw(s.acc))
            to_a()                 # TST (0xb) only sets flags, ccpu.cpp:446-460
        elif hi == 0x8:            # SETP ccpu.cpp:416-422
            s.P = lo; to_a()
        elif hi == 0x9:            # OUT ccpu.cpp:425-432
            if s.acc == "A":
                self.outvals.setdefault(a, set()).add(UNK if s.A is UNK else (~s.A) & 1)
            to_a()
        elif op == 0xe0:           # DV ccpu.cpp:483-498 (afterwards A,B := X,Y)
            s.A = UNK if s.X is UNK else s.X & M12
            s.B = UNK if s.Y is UNK else s.Y & M12
            s.setw("A", s.isw("X")); s.setw("B", s.isw("Y"))
            s.Aslot = s.Bslot = UNK
            to_a()
        elif op == 0xe1:           # LPAP ccpu.cpp:501-504
            v, vw = ramv(s.I)
            if s.I is not UNK:
                self.lpap_slots.add(s.I)
            s.J = v; s.setw("J", vw and v is not UNK); s.Jslot = s.I; to_a()
        elif op == 0xf1:           # WSP ccpu.cpp:507-510
            v, vw = ramv(s.I)
            s.I = UNK if v is UNK else v & 0xff; s.setw("I", vw and v is not UNK); to_a()
        elif op in (0xe2, 0xf2):   # LKP ccpu.cpp:513-519 (note the extra m_PC++ at :518)
            x = s.get()
            if x is UNK:
                self.lkp.setdefault(a, set()).add(None)
                s.put(UNK)
            else:
                self.lkp.setdefault(a, set()).add(bank + x)
                s.put(self.rd(bank + x), s.isw(s.acc))
            to_a()
            nxt = a + 2
        elif op in (0xe3, 0xf3, 0xe4, 0xf4):   # MUL ccpu.cpp:522-563, NV ccpu.cpp:566-579
            s.A = s.B = UNK; s.setw("A", False); s.setw("B", False)
            s.Aslot = s.Bslot = UNK; to_a()
        elif op in (0xe5, 0xf5):   # FRM ccpu.cpp:582-593 (an immediately repeated FRM is skipped)
            to_a()
            if self.rd(a + 1) == op:
                nxt = a + 2
        elif op in (0xe6, 0xf6):   # STAP ccpu.cpp:596-600
            self._store(a, s, s.get(), s.isw(s.acc)); to_a()
        elif op in (0xe7, 0xf7, 0xe8, 0xf8, 0xe9, 0xf9, 0xea, 0xfa):
            v, vw = ramv(s.I)      # CST/ADDP, SUBP, ANDP, LDAP ccpu.cpp:603-635
            if op in (0xe7, 0xf7):
                acc_op(lambda x, y: x + y, v, vw)
            elif op in (0xe8, 0xf8):
                acc_op(lambda x, y: x + (y ^ M12) + 1, v, vw)
            elif op in (0xe9, 0xf9):
                acc_op(lambda x, y: x & y, v, vw)
            else:
                s.put(v, vw, s.I)  # LDAP
            to_a()
        elif op in (0xeb, 0xfb):   # SHR ccpu.cpp:638-644
            isa = s.acc == "A"
            acc_op(lambda x, _: (x >> 1) if isa else asr(x)); to_a()
        elif op in (0xec, 0xfc):   # SHL ccpu.cpp:647-653
            acc_op(lambda x, _: x << 1); to_a()
        elif op in (0xed, 0xfd):   # ASR ccpu.cpp:656-661
            acc_op(lambda x, _: asr(x)); to_a()
        elif op in (0xee, 0xfe):   # SHRB ccpu.cpp:664-676
            if s.acc == "A":
                A, B = s.A, s.B
                s.A = UNK if (A is UNK or B is UNK) else ((A >> 1) | ((B << 11) & 0x800)) & M12
                s.setw("A", s.A is not UNK and (s.isw("A") or s.isw("B")))
            s.B = UNK if s.B is UNK else asr(s.B)
            s.Aslot = s.Bslot = UNK
            to_a()
        elif op in (0xef, 0xff):   # SHLB ccpu.cpp:679-691
            if s.acc == "A":
                s.A = UNK if s.A is UNK else (s.A << 1) & M12
            s.B = UNK if s.B is UNK else (s.B << 1) & M12
            s.Aslot = s.Bslot = UNK
            to_a()
        elif op == 0xf0:           # IV ccpu.cpp:694-698
            s.X, s.Y = s.A, s.B
            s.setw("X", s.isw("A")); s.setw("Y", s.isw("B")); to_a()
        out.append((nxt, s))
        return out

    def _store(self, a, s, v, weak):
        i = s.I
        src = getattr(s, s.acc + "slot")
        if i is not UNK and src is not None:
            self.copy_edges.add((src, i))
        self.store_log.setdefault(a, set()).add((None if i is UNK else i, None if v is UNK else v))
        if i is UNK:
            # store to an unknown address: keep known slots but mark them WEAK
            self.wild_stores.add(a)
            s.ram = {k: (val, True) for k, (val, _) in s.ram.items()}
        elif v is UNK:
            s.ram.pop(i, None)
        else:
            s.ram[i] = (v, weak or s.isw("I"))
            self.slot_consts.setdefault(i, set()).add(v)

    KPART = 64   # max distinct return-slot contexts kept apart per (address, accumulator)

    def pkey(self, a, st):
        ctx = tuple(st.ram.get(k, (None,))[0] for k in sorted(self.key_slots))
        parts = self.parts.setdefault((a, st.acc), set())
        if ctx not in parts:
            if len(parts) >= self.KPART:
                ctx = "*"        # too many contexts: fall back to one joined state
            parts.add(ctx)
        return (a, st.acc, ctx)

    def fixpoint(self, entry):
        self.reset_records()
        self.states = {}
        self.parts = {}
        work = [(entry, reset_state())] + [(t, State(acc=acc, ram={})) for t, acc in sorted(self.seeds)]
        while work:
            a, st = work.pop()
            a &= 0x1fff
            k = self.pkey(a, st)
            old = self.states.get(k)
            new = st if old is None else old.join(st)
            if old is not None and new.key() == old.key():
                continue
            self.states[k] = new
            for na, ns in self.step(a, new):
                work.append((na, ns))
            if len(self.states) > 400000:
                raise RuntimeError("state explosion")

    def run(self, entry=0):
        # iterate: discover LPAP slots, then re-analyse keeping states with
        # different values in those slots apart (context sensitivity for
        # "return address in RAM" subroutine calls)
        self.prev_slot_consts = {}
        self.seeds = set()        # (addr, acc) entered from reaching-definition return targets
        self.rd_targets = {}      # return site -> {target: [def sites]}
        self.rd_open = {}         # return site -> reason it stays unresolved
        for _round in range(10):
            for _ in range(20):
                self.fixpoint(entry)
                new = self.key_slots | self.lpap_slots
                if new == self.key_slots:
                    break
                self.key_slots = new
            # final recording pass over fixpoint states only
            self.reset_records()
            self.add_target(-1, entry)
            self.succ = {}
            for k, st in self.states.items():
                for na, _ in self.step(k[0], st):
                    self.succ.setdefault(k[0], set()).add(na & 0x1fff)
            for t, _acc in self.seeds:
                self.add_target(-2, t)
            self.reaching_returns()
            # RD targets are a context-insensitive MAY set; they are reported
            # but not used as new analysis entries (doing so pulled in bogus
            # targets such as L0000/L0001 from unrelated uses of the slot).
            break
        self.retpoints = {r: c for c, (t, r) in self.calls.items()}
        self.code = {}
        self.accs = {}
        for k, st in self.states.items():
            a = k[0]
            n, text, _ = disasm(self.rd, a)
            op = self.rd(a)
            ln = 2 if op in (0xe2, 0xf2) else n
            if op in (0xe5, 0xf5) and self.rd(a + 1) == op:
                ln = 2
            self.code[a] = (ln, text)
            self.accs.setdefault(a, []).append(st)


def _reaching_returns(self):
    """Resolve RAM-based returns by reaching definitions of the return slot.

    For a return site X (JMP/T4K whose J came from LPAP of RAM[S]) walk the
    control-flow graph backwards from X. A store to S ends a path and is a
    definition; its value (if constant) gives a return target: bank(X)+v for
    JMP (ccpu.cpp:347-350), (P<<12)+v for T4K (ccpu.cpp:300-303, P known at X).
    A return point r of a CALL c is passed backwards to c directly (the callee
    body is skipped), unless the callee body stores to S, in which case the
    path is marked open. Reaching the reset entry, or a store with an unknown
    value, also leaves the site open. The result is a context-insensitive MAY
    set; it is used only for sites the state analysis left unresolved."""
    pred = {}
    for a, ns in self.succ.items():
        for n in ns:
            pred.setdefault(n, set()).add(a)
    retp = {r: c for c, (t, r) in self.calls.items()}

    def callee_body(t):
        seen, todo = set(), [t]
        while todo:
            n = todo.pop()
            if n in seen:
                continue
            seen.add(n)
            if n in self.retsites:
                continue
            if n in self.calls:
                todo.append(self.calls[n][1])
                continue
            todo.extend(self.succ.get(n, ()))
        return seen

    def stores_to(n, S):
        return [v for (i, v) in self.store_log.get(n, ()) if i == S]

    new_seeds = set()
    self.rd_targets, self.rd_open = {}, {}
    for X in sorted(self.unresolved):
        if X not in self.retsites:
            continue
        sts = [st for k, st in self.states.items() if k[0] == X]
        slots = self.retsites[X]
        Ps = set(st.P for st in sts)
        op = self.rd(X)
        if len(slots) != 1 or (op == 0x50 and (UNK in Ps or len(Ps) != 1)):
            self.rd_open[X] = "return slot or P not unique"
            continue
        S = next(iter(slots))
        P = next(iter(Ps))
        defs, why = {}, set()
        seen, todo = set(), [(p, X) for p in pred.get(X, ())]
        while todo:
            n, frm = todo.pop()
            if n in seen:
                continue
            seen.add(n)
            if n == X:
                continue
            vals = stores_to(n, S)
            if vals:
                for v in vals:
                    if v is None:
                        why.add("store of unknown value at %04X" % n)
                    else:
                        defs.setdefault(v, []).append(n)
                continue
            if n == 0:
                why.add("reaches reset entry without a store")
            if n in retp:
                c = retp[n]
                body = callee_body(self.calls[c][0])
                if any(stores_to(b, S) for b in body):
                    # callee body may set S; its own stores are definitions too
                    for b in body:
                        for v in stores_to(b, S):
                            if v is None:
                                why.add("store of unknown value at %04X" % b)
                            else:
                                defs.setdefault(v, []).append(b)
                todo.append((c, n))
                continue
            for p in pred.get(n, ()):
                todo.append((p, n))
        tg = {}
        for v, ws in defs.items():
            t = (X & 0x1000) + v if op == 0x58 else cpu_to_region((P << 12) + v)
            tg[t & 0x1fff] = sorted(ws)
        self.rd_targets[X] = tg
        if why:
            self.rd_open[X] = "; ".join(sorted(why))
        acc_after = set()
        for st in sts:
            acc_after.add("A" if op == 0x58 else ("B" if st.acc == "A" else "A"))
        for t in tg:
            if not why:
                self.add_target(X, t)
            for acc in acc_after:
                new_seeds.add((t, acc))
    for X in list(self.rd_targets):
        if X not in self.rd_open and self.rd_targets[X]:
            self.unresolved[X] = self.unresolved[X] + " -> RESOLVED by reaching definitions of RAM[$%02X]" % next(iter(self.retsites[X]))
    return new_seeds


Analyzer.reaching_returns = _reaching_returns


def describe(an, a):
    """Annotation text for the instruction at a (from fixpoint states only)."""
    op = an.rd(a)
    hi, lo = op >> 4, op & 15
    sts = an.accs.get(a, [])
    accs = sorted(set(st.acc for st in sts))
    c = []
    if hi == 0x1:
        for acc in accs:
            if acc == "A":
                c.append("acc A: read INPUTS bit %d = %s" % (lo, INPUT_BITS[lo]))
            else:
                port = 16 + (lo & 7)
                if port == 0x17:
                    c.append("acc B: read port $17 coin_input_r = !coin_detected [cinemat.cpp:315,111-114]")
                else:
                    c.append("acc B: read port $%02X = SWITCHES bit %d, %s [switches_r shuffle cinemat.cpp:91]"
                             % (port, SW_SHUFFLE[lo & 7], SW_BITS[SW_SHUFFLE[lo & 7]]))
    elif hi == 0x9:
        if "A" in accs:
            vals = an.outvals.get(a, set())
            v = "/".join("?" if x is None else str(x) for x in sorted(vals, key=lambda z: -1 if z is None else z))
            c.append("acc A: Q%d <= %s (= ~A&1) : %s" % (lo & 7, v or "?", OUT_BITS[lo & 7]))
        if "B" in accs:
            c.append("acc B: no port write (ccpu.cpp:429)")
    elif op == 0xe0:
        c.append("DV: draw vector from (X,Y) toward (A,B) scaled by T [ccpu.cpp:483-498]")
    elif op == 0xf0:
        pts = sorted(set((sx(st.A), sx(st.B)) for st in sts if st.A is not UNK and st.B is not UNK))
        c.append("IV: beam (X,Y) <= (A,B)" + ((" = " + " ".join("(%d,%d)" % p for p in pts[:6])) if pts else "")
                 + " [ccpu.cpp:694-698]")
    elif op in (0xe4, 0xf4):
        c.append("NV: normalize A,B, set T [ccpu.cpp:566-579]")
    elif op in (0xe2, 0xf2):
        tabs = an.lkp.get(a, set())
        known = sorted(t for t in tabs if t is not None)
        if known:
            c.append("LKP reads ROM $" + ",$".join("%04X" % t for t in known[:8])
                     + (" ..." if len(known) > 8 else ""))
        if None in tabs or not tabs:
            c.append("LKP index not known statically")
        c.append("skips next byte [ccpu.cpp:518]")
    elif op in (0xe5, 0xf5):
        c.append("FRM: wait for next frame [ccpu.cpp:582-593]")
    elif op == 0xf7:
        c.append("CST: clears watchdog [ccpu.cpp:603-605]")
    elif op == 0xe1:
        slots = sorted(set(st.I for st in sts if st.I is not UNK))
        if slots:
            c.append("J <= RAM[$" + ",$".join("%02X" % x for x in slots) + "]")
    if op in (0x51, 0x59):
        c.append("ripoff = jmi_8k: tests MI (sign) flag [cinemat.cpp:1167,1042; ccpu.cpp:87-92]")
    if op in (0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x58, 0x59, 0x5a, 0x5b, 0x5c, 0x5d, 0x5e):
        tg = sorted(t for t, srcs in an.targets.items() if a in srcs)
        if tg:
            c.append("-> " + ",".join(("L%04X" % t) + ("(SLOTSET)" if (a, t) in an.slotset_targets else "(ASSUMED)" if (a, t) in an.weak_targets else "")
                                      for t in tg))
        if a in an.rd_targets and an.rd_targets[a]:
            c.append("RD (may-set, context-insensitive): " + ",".join("L%04X(def %s)" % (t, "/".join("%04X" % w for w in ws[:3]))
                                        for t, ws in sorted(an.rd_targets[a].items())))
        if a in an.unresolved:
            if "RESOLVED by" in an.unresolved[a]:
                c.append("was " + an.unresolved[a])
            else:
                c.append("UNRESOLVED: " + an.unresolved[a] + ((" [RD open: " + an.rd_open[a] + "]") if a in an.rd_open else ""))
    if a in an.calls:
        t, r = an.calls[a]
        c.append("CALL L%04X, returns to L%04X (A=$%03X = next address)" % (t, r, r & 0xfff))
    if accs == ["B"]:
        c.append("acc=B")
    elif len(accs) > 1:
        c.append("acc=A|B")
    return "; ".join(c)


def n_open(an):
    return sum(1 for v in an.unresolved.values() if "RESOLVED by" not in v)


def sx(v):
    return v - 0x1000 if v & 0x800 else v


def write_listing(an, path, imgsha):
    code = an.code
    covered = set()
    for a, (ln, _) in code.items():
        for i in range(ln):
            covered.add(a + i)
    lines = []
    W = lines.append
    W("; Rip-Off (Cinematronics, 1980) CCPU program listing -- passes 1-2")
    W("; Generated by tools/ccpu/listing.py from disasm/ripoff_program.bin (sha1 %s)" % imgsha)
    W("; Mnemonics: MAME ccpudasm.cpp @ 7c11d9e0 (Python port verified byte-identical).")
    W("; Columns: region offset | CPU address | bytes | instruction | ; notes")
    W("; Code = reached by static recursive descent from reset (PC=0). Everything else is")
    W("; shown as DB (unreached: data, or code reached only via unresolved jumps).")
    W(";")
    W("; code bytes: %d of 8192; unresolved control transfers: %d; targets marked ASSUMED: %d"
      % (len(covered), n_open(an), len(an.weak_targets)))
    W("; ASSUMED = jump target taken from a RAM slot that a store to an unknown address")
    W(";           might have overwritten; check by hand before relying on it.")
    W("")
    a = 0
    while a < 0x2000:
        if a in (0x0000, 0x1000):
            W("; ================= region $%04X-$%04X (CPU $%04X-$%04X) =================" %
              (a, a + 0xfff, region_to_cpu(a), region_to_cpu(a) + 0xfff))
        if a in code:
            if a in an.retpoints:
                W("L%04X:    ; RETURN POINT of CALL at %04X" % (a, an.retpoints[a]))
            elif a in an.targets:
                srcs = sorted(x for x in an.targets[a] if x >= 0)
                W("L%04X:%s" % (a, ("    ; from " + ",".join("%04X" % x for x in srcs[:10]) +
                                    (" ..." if len(srcs) > 10 else "")) if srcs else "    ; RESET entry"))
            ln, text = code[a]
            b = " ".join("%02X" % an.rd(a + i) for i in range(ln))
            note = describe(an, a)
            W("%04X  %04X  %-8s  %-12s%s" % (a, region_to_cpu(a), b, text, ("; " + note) if note else ""))
            a += ln
        else:
            start = a
            if start in DATA_LABELS:
                W("; ---- $%04X: %s" % (start, DATA_LABELS[start]))
            a += 1
            while a < 0x2000 and a not in covered and a % 8 != 0 and a not in DATA_LABELS and a != 0x1000:
                a += 1
            bs = [an.rd(x) for x in range(start, a)]
            W("%04X  %04X  %-8s  DB %s" % (start, region_to_cpu(start), "", ",".join("$%02X" % x for x in bs)))
    open(path, "w").write("\n".join(lines) + "\n")
    return covered


def main():
    import hashlib
    img = open(sys.argv[1], "rb").read()
    an = Analyzer(img)
    an.run(0)
    covered = write_listing(an, sys.argv[2], hashlib.sha1(img).hexdigest())
    if len(sys.argv) > 3:
        rep = {
            "code_bytes": len(covered),
            "instructions": len(an.code),
            "unresolved": {"%04X" % k: v for k, v in sorted(an.unresolved.items())},
            "rd_return_targets": {"%04X" % x: {"%04X" % t: ["%04X" % w for w in ws] for t, ws in sorted(tg.items())}
                                  for x, tg in sorted(an.rd_targets.items())},
            "rd_open": {"%04X" % x: why for x, why in sorted(an.rd_open.items())},
            "assumed_targets": sorted("%04X->%04X" % st for st in an.weak_targets - an.slotset_targets),
            "slotset_targets": sorted("%04X->%04X" % st for st in an.slotset_targets),
            "wild_stores": sorted("%04X" % x for x in an.wild_stores),
            "lpap_slots": sorted("%02X" % x for x in an.key_slots),
            "calls": {"%04X" % c: ["%04X" % t, "%04X" % r] for c, (t, r) in sorted(an.calls.items())},
            "lkp": {"%04X" % k: sorted(("%04X" % t) if t is not None else "unknown" for t in v)
                    for k, v in sorted(an.lkp.items())},
            "inp": sorted("%04X" % a for a in an.code if an.rd(a) >> 4 == 1),
            "out": sorted("%04X" % a for a in an.code if an.rd(a) >> 4 == 9),
            "dv": sorted("%04X" % a for a in an.code if an.rd(a) == 0xe0),
            "iv": sorted("%04X" % a for a in an.code if an.rd(a) == 0xf0),
            "nv": sorted("%04X" % a for a in an.code if an.rd(a) in (0xe4, 0xf4)),
            "frm": sorted("%04X" % a for a in an.code if an.rd(a) in (0xe5, 0xf5)),
        }
        json.dump(rep, open(sys.argv[3], "w"), indent=1)
    print("code bytes %d, insns %d, unresolved %d" % (len(covered), len(an.code), n_open(an)))


if __name__ == "__main__":
    main()
