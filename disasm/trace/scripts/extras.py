# Produces small evidence excerpts from runs/* (RAM logs only; no ROM data).
import collections, sys
OUT = sys.argv[1]
def s12(v): return v-0x1000 if v>=0x800 else v
def load(t):
    ram={}
    for l in open(f'runs/{t}/ram.log'):
        p=l.split(); ram[int(p[0])]=[int(x,16) for x in p[1:]]
    return ram
def writes(t):
    for l in open(f'runs/{t}/writes.log'): yield l.split()
# 1. RNG ($11B8) fit from word-B stores at $0860 (two $11B8 calls per store)
pairs=set()
for t in ['idle1','shoot1','ram1','wander1']:
    for p in writes(t):
        if p[1] in ('0db',) and p[3]=='pc=0861':
            o,n=p[2].split('->'); pairs.add((int(o,16),int(n,16)))
def step(x,i,j,inv): return ((x<<1)&0xfff)|((((x>>i)^(x>>j))&1)^inv)
sol=[(i,j,inv) for i in range(12) for j in range(i+1,12) for inv in (0,1) if all(step(step(o,i,j,inv),i,j,inv)==n for o,n in pairs)]
with open(f'{OUT}/rng_11B8_fit.txt','w') as f:
    f.write(f'{len(pairs)} distinct (old,new) word-B pairs stored at $0860 (store PC+1 = $0861), runs idle1/shoot1/ram1/wander1.\n')
    f.write('Searched: new = s(s(old)), s(x) = ((x<<1)&$FFF) | (bit_i(x) XOR bit_j(x) XOR inv)\n')
    f.write(f'Solutions (i,j,inv): {sol}\n')
    x=0x387; seen={}; k=0
    while x not in seen: seen[x]=k; x=step(x,8,10,0); k+=1
    f.write(f'Period of s from reset seed $387: {k-seen[x]}\n')
    for o,n in sorted(pairs)[:40]: f.write(f'{o:03x} -> {n:03x}\n')
# 2. Laser (FC bit 1 set at $0E1E) vs enemy state and 16-frame window
with open(f'{OUT}/laser_windows.txt','w') as f:
    for t in ['idle1','shoot1','ram1','wander1']:
        ram=load(t); c=collections.Counter()
        for p in writes(t):
            if p[1]=='0fc' and p[3]=='pc=0e1f':
                fr=int(p[0]); rec=int(p[4][3:],16); w=ram.get(fr) or ram[fr+1]
                c[(f'rec{rec:02x}', f'A={w[rec+0xA]:03x}', f'frame%16={fr%16}')]+=1
        f.write(f'{t}: FC bit1 set at $0E1E by (record, word A at frame end, frame mod 16): {dict(sorted(c.items()))}\n')
    ram=load('ram1'); e8=collections.Counter((fr%16,w[0xE8]) for fr,w in ram.items())
    f.write('ram1: RAM[$E8] at frame end, by frame mod 16: '+str(dict(sorted(e8.items())))+'\n')
    a8=collections.Counter((ram[fr][0xA8]-ram[fr-1][0xA8])&0xfff for fr in ram if fr-1 in ram)
    f.write('ram1: RAM[$A8] change per frame: '+str(dict(a8))+'\n')
# 3. Sound command bits 1 (Laser) and 2 (Motor 1) writers
with open(f'{OUT}/sound_bits.txt','w') as f:
    for t in ['idle1','shoot1','ram1','wander1']:
        pcs=collections.Counter()
        for p in writes(t):
            if p[1]=='0fc':
                o,n=[int(x,16) for x in p[2].split('->')]
                for b in (1,2):
                    if (o^n)>>b&1: pcs[(f'bit{b}', p[3], 'set' if n>>b&1 else 'clr')]+=1
        nt=sum(1 for p in writes(t) if p[1]=='0fd' and p[2].endswith('->005'))
        f.write(f'{t}: FC bit changes by store PC+1: {dict(sorted(pcs.items()))}; RAM[$FD]:=5 stores (player shots): {nt}\n')
# 4. idle1 frames 1010-1080, compact record view
ram=load('idle1')
with open(f'{OUT}/idle1_records_f1010-1080.txt','w') as f:
    f.write('frame | rec: A x y angle speed tgt C D F | P1: A x y | RAM[$E8] RAM[$FC] at frame end\n')
    for fr in range(1010,1081):
        w=ram[fr]
        rs=' | '.join(f'r{b:02x}:{w[b+0xA]:03x} {s12(w[b])} {s12(w[b+1])} {w[b+5]} {w[b+6]} {w[b+2]:02x} {w[b+0xC]} {w[b+0xD]} {w[b+0xF]}' for b in (0,0x10,0x20))
        f.write(f'{fr} | {rs} | P1:{w[0x3A]:03x} {s12(w[0x30])} {s12(w[0x31])} | E8={w[0xE8]:02x} FC={w[0xFC]:02x}\n')
