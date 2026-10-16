SP={(0,0),(304,0),(896,0),(592,0),(1024,768),(720,768),(128,768),(432,768)}
import collections
def s12(v): return v-0x1000 if v>=0x800 else v
for t in ['idle_snap','shoot1','ram1','wander1']:
    ram={}
    for l in open(f'runs/{t}/ram.log'):
        p=l.split(); ram[int(p[0])]=[int(x,16) for x in p[1:]]
    spawn={}; res=collections.Counter(); trig=[]; kills=[]
    for l in open(f'runs/{t}/writes.log'):
        p=l.split(); f=int(p[0])
        if p[1]!='0da' and not (p[1][-1]=='a' and int(p[1],16)<0x50): continue
        o,n=p[2].split('->')
        if o==n: continue
        rec=int(p[4][3:],16) if p[1]=='0da' else int(p[1],16)&0xf0
        if rec>=0x30: 
            if p[3]=='pc=118b':
                E=[int(x,16) for x in p[5][2:].split(',')]
                kills.append((f,'by rec %02x'%E[15],'|d|=(%d,%d)'%(E[0],E[1]),'E8=%02x'%E[8], 'f%%16=%d'%(f%16)))
            continue
        w=ram.get(f+1)
        if p[3]=='pc=02c4' and w: spawn[rec]=(w[rec+0xC],w[rec+0xD])
        if p[3]=='pc=10be' and w:
            res[('exit in spawn table', (w[rec+0xC],w[rec+0xD]) in SP)]+=1; res[('exit==own wave spawn', (w[rec+0xC],w[rec+0xD])==spawn.get(rec))]+=1
        if p[3]=='pc=0ac0' and o=='01c':
            E=[int(x,16) for x in p[5][2:].split(',')]
            trig.append((f,E[0],E[1],E[2],E[3]))
    # chase distances
    mx=0
    for f,w in ram.items():
        for b in (0,0x10,0x20):
            if w[b+0xA]==0x18:
                d=max(abs(s12(w[b])-s12(w[0x30])),abs(s12(w[b+1])-s12(w[0x31]))); mx=max(mx,d)
    print(t, dict(res), 'max chebyshev dist enemy-P1 while 018:',mx)
    print('  chase triggers (frame, E0,E1,E2,E3):',[tuple(hex(x) if i else x for i,x in enumerate(r)) for r in trig[:12]])
    print('  player kills by enemy:',kills)
