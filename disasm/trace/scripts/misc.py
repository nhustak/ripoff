# (a) frames with a non-chase enemy (A=$014/$01E) within Chebyshev 200 of a live P1, and whether any switched to $018;
# (b) RAM words that change during the idle1 game (f>=210); (c) shot-slot liveness per run.
import collections, sys
def s12(v): return v-0x1000 if v>=0x800 else v
c=collections.Counter()
for t in ['shoot1','ram1','wander1']:
    prev=None
    for l in open(f'runs/{t}/ram.log'):
        p=l.split(); w=[int(x,16) for x in p[1:]]
        if not (w[0xF0]&2) and (w[0x3A]&0x10):
            for b in (0,0x10,0x20):
                a=w[b+0xA]
                if a in (0x14,0x1e,0x1c):
                    d=max(abs(s12(w[b])-s12(w[0x30])),abs(s12(w[b+1])-s12(w[0x31])))
                    if d<=200: c[(t,'A=%03x'%a)]+=1
            if prev:
                for b in (0,0x10,0x20):
                    if prev[b+0xA] in (0x14,0x1e) and w[b+0xA]==0x18: c[(t,'switch %03x->018'%prev[b+0xA])]+=1
        prev=w
print('(a) frames with enemy within 200 of live P1, by state; and 014/01E->018 switches:', dict(c))
rows=[[int(x,16) for x in l.split()[1:]] for l in open('runs/idle1/ram.log') if int(l.split()[0])>=210]
print('(b) idle1 words changing after f210:', ' '.join(f'{a:02x}' for a in range(256) if len(set(r[a] for r in rows))>1))
for t in ['idle1','shoot1','ram1','wander1']:
    rows=[[int(x,16) for x in l.split()[1:]] for l in open(f'runs/{t}/ram.log')]
    print(f'(c) {t}: frames with a live P1 shot slot ($50-$5F word0 bit0=0): {sum(1 for w in rows if any((w[a]&1)==0 for a in range(0x50,0x60,4)))}; live P2 slot ($60-$6F): {sum(1 for w in rows if any((w[a]&1)==0 for a in range(0x60,0x70,4)))}')
