# Player-1 death -> free -> respawn timing and respawn position (one frame after), runs idle1/ram1/wander1.
import collections
def s12(v): return v-0x1000 if v>=0x800 else v
for t in ['idle1','ram1','wander1']:
    ram={}
    for l in open(f'runs/{t}/ram.log'):
        p=l.split(); ram[int(p[0])]=[int(x,16) for x in p[1:]]
    kill=free=None
    for l in open(f'runs/{t}/writes.log'):
        p=l.split(); f=int(p[0])
        if p[1] in('03a','0da') and p[2] in ('01c->00c','01e->00e') and (p[1]=='03a' or p[4]=='EF=030'): kill=(f,p[3])
        if p[1]=='0da' and p[3]=='pc=0d24' and p[4]=='EF=030' and p[2].endswith('->001'): free=f
        if p[1]=='03a' and p[2]=='001->01c' and p[3]=='pc=03bd' and f>210:
            w=ram[f+1]
            print(f'{t} death f{kill[0]} ({kill[1]}) freed f{free} (+{free-kill[0]}) respawn f{f} (+{f-free}) at ({s12(w[0x30])},{s12(w[0x31])}) angle {w[0x35]} speed {w[0x36]} one frame later')
