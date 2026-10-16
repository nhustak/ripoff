# Branch taken through the enemy draw / fire-line code, per main-loop pass, from a debugger trace.
# Markers: 0138 = start of a pass (sound command output), 0D39 = draw entry for a live object,
# 0D47/0D4F = off-field skip (x >= $400 / y >= $300), 0E0D = not this record's window (RAM[$E8] != RAM[$EF]),
# 0E03 reached and fell through to 0E23 without 0E0D = word-A bit 3/2 test failed (not chasing),
# 0E1F = line drawn (FC |= 2 at $0E1E).
import re, sys
def reg(pc): return (pc&0xfff)|(0x1000 if pc&0x2000 else 0)
names={0x0138:'|pass',0x0D39:'draw',0x0D47:'OFF(x)',0x0D4F:'OFF(y)',0x0E0D:'not-window',0x0E1F:'LINE',0x0DF5:'FA-skip'}
out=[]
for l in open(sys.argv[1]):
    m=re.match(r'([0-9A-F]{4}): ',l)
    if m:
        r=reg(int(m.group(1),16))
        if r in names: out.append(names[r])
print(' '.join(out))
