import collections,sys
def s12(v): return v-0x1000 if v>=0x800 else v
tot=collections.Counter()
for tag in sys.argv[1:]:
    R={}
    for l in open(f'runs/{tag}/ram.log'):
        p=l.split(); R[int(p[0])]=[int(x,16) for x in p[1:]]
    las=collections.defaultdict(set)
    for l in open(f'runs/{tag}/writes.log'):
        p=l.split()
        if p[1]=='0fc' and p[3]=='pc=0e1f': las[int(p[0])].add(int(p[4][3:],16))
    c=collections.Counter(); other=[]
    for f in sorted(R):
        if f-1 not in R or f+1 not in R: continue
        w,nw=R[f],R[f+1]
        e=w[0xe8]
        if e not in (0,0x10,0x20) or w[e+0xa]!=0x18: continue
        x,y=s12(nw[e]),s12(nw[e+1]); on=0<=x<0x400 and 0<=y<0x300
        L=e in las[f]
        if L and on: k='line drawn, on-field'
        elif L: k='line drawn, OFF-field'
        elif not on: k='no line: off-field at draw'
        elif any((w[P+0xa]&0x10) and not (nw[P+0xa]&0x10) for P in (0x30,0x40)) and nw[e+0xa]==0x1c: k='no line: player killed this pass (A->01C)'
        elif nw[e+0xa]==0x14: k='no line: picked up canister (A->014)'
        else: k='no line: OTHER'; other.append((tag,f,hex(e)))
        c[k]+=1; tot[k]+=1
    print(tag,dict(c))
print('TOTAL',dict(tot)); print('other',other[:20])
