# Is the "last chaser circles a stationary player" state a true cycle?  Search the smallest period P
# (multiple of 16 frames, the fire-window period) such that every RAM word except at most 3 is equal at f
# and f+P for every frame f from START to the end of the run.
import sys
for arg in sys.argv[1:]:
    tag,start=arg.split(':'); start=int(start)
    R={}
    for l in open(f'runs/{tag}/ram.log'):
        p=l.split(); f=int(p[0])
        if f>=start: R[f]=p[1:]
    last=max(R); found=False
    for P in range(16,last-start-200,16):
        d=[i for i in range(256) if R[start][i]!=R[start+P][i]]
        if len(d)<=3:
            ok=all([i for i in range(256) if R[f][i]!=R[f+P][i]]==d for f in range(start,last-P+1))
            print(f'{tag}: from f{start} to f{last}: RAM(f) == RAM(f+{P}) for every f, except words {[hex(i) for i in d]}; holds for every frame: {ok}')
            found=True; break
    if not found: print(f'{tag}: no period found')

# distance of the chaser from P1 over one cycle, and the orb_* input scripts' context
def s12(v): return v-0x1000 if v>=0x800 else v
for tag,a in (('idle1',1400),('orb_nudge',3100),('orb_wstop',3100)):
    R={}
    for l in open(f'runs/{tag}/ram.log'):
        p=l.split(); f=int(p[0])
        if a<=f<a+720: R[f]=[int(x,16) for x in p[1:]]
    ds=[]; 
    for f,w in R.items():
        for e in (0,0x10,0x20):
            if w[e+0xa]==0x18: ds.append(max(abs(s12(w[e])-s12(w[0x30])),abs(s12(w[e+1])-s12(w[0x31]))))
    w=R[a]
    print(f'{tag} cycle f{a}-f{a+719}: P1 at ({s12(w[0x30])},{s12(w[0x31])}) speed {w[0x36]}; chaser frames {len(ds)}, Chebyshev distance min {min(ds)} max {max(ds)}; enemy word A = {[hex(w[e+0xa]) for e in (0,0x10,0x20)]}')
