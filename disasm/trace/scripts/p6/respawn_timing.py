# Player death -> free -> respawn timing and respawn state, for P1 ($30) and P2 ($40); word 3 = respawn counter.
import sys, collections
def s12(v): return v-0x1000 if v>=0x800 else v
for tag in sys.argv[1:]:
    R={}
    for l in open(f'runs/{tag}/ram.log'):
        p=l.split(); R[int(p[0])]=[int(x,16) for x in p[1:]]
    stats=collections.Counter(); first=[]
    for P in (0x30,0x40):
        death=free=None
        for f in sorted(R):
            if f-1 not in R: continue
            w,pw=R[f],R[f-1]
            if pw[P+0xa]&0x10 and not w[P+0xa]&0x10 and not w[P+0xa]&1: death=f
            if death and not pw[P+0xa]&1 and w[P+0xa]&1: free=f
            if free and pw[P+0xa]&1 and w[P+0xa]==0x1c and f+1 in R:
                n=R[f+1]; w3=[R[g][P+3] for g in range(free,f) if g in R]
                k=(f'P{1 if P==0x30 else 2}', f'death->free {free-death}', f'free->respawn {f-free}', f'pos@respawn+1 ({s12(n[P])},{s12(n[P+1])})', f'angle {n[P+5]}', f'speed {n[P+6]}')
                stats[(k[0],k[1],k[2])]+=1
                if sum(1 for x in first if x[0]==k[0])<2: first.append(k+(f'f{death}/f{free}/f{f}', f'word3 {w3[0]}..{w3[-1]}'))
                death=free=None
    print(tag, dict(stats))
    for x in first: print('   ', x)
