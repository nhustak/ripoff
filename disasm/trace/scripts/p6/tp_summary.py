import sys
def s12(v): return v-0x1000 if v>=0x800 else v
for tag in sys.argv[1:]:
    R={}
    for l in open(f'runs/{tag}/ram.log'):
        p=l.split(); R[int(p[0])]=[int(x,16) for x in p[1:]]
    fs=sorted(R)
    print('=====',tag, 'frames',fs[-1])
    close=[]; pdeaths=[]; chase=[]; resp=[]; score=[]
    for f in fs[1:]:
        w,pw=R[f],R[f-1]
        # shots vs other player
        for base,victim in ((0x50,0x40),(0x60,0x30)):
            if (w[victim+0xa]&0x10)==0: continue
            vx,vy=s12(w[victim]),s12(w[victim+1])
            for s in range(base,base+16,4):
                if w[s]&1: continue
                sx,sy=s12(w[s])/2,s12(w[s+1])
                d=max(abs(sx-vx),abs(sy-vy))
                if d<=20: close.append((f,hex(s),(sx,sy),hex(victim),(vx,vy),d))
        for P in (0x30,0x40):
            if (pw[P+0xa]&0x10) and not (w[P+0xa]&0x10):
                pdeaths.append((f,hex(P),hex(pw[P+0xa]),'->',hex(w[P+0xa]),'scale',hex(w[P+4]),'other',hex(w[(0x70-P)+0xa]),
                   'enemies',[hex(w[e+0xa]) for e in (0,0x10,0x20)]))
            if (pw[P+0xa]&1) and not (w[P+0xa]&1):
                resp.append((f,hex(P),(s12(w[P]),s12(w[P+1])),'ang',w[P+5],'spd',w[P+6]))
        for e in (0,0x10,0x20):
            if w[e+0xa]==0x18 and pw[e+0xa]!=0x18:
                ex,ey=s12(w[e]),s12(w[e+1]); cd=(s12(w[e+0xc]),s12(w[e+0xd]))
                ds={}
                for P in (0x30,0x40):
                    ds[hex(P)]=(max(abs(ex-s12(w[P])),abs(ey-s12(w[P+1]))), (s12(w[P]),s12(w[P+1])), hex(w[P+0xa]))
                chase.append((f,hex(e),(ex,ey),'CD',cd,ds))
        for a in (0xf1,0xf2,0xfe,0xff):
            if w[a]!=pw[a]: score.append((f,hex(a),pw[a],w[a]))
    print('shot passes within 20 of other player:',len(close)); [print(' ',c) for c in close[:12]]
    print('player deaths:',len(pdeaths)); [print(' ',c) for c in pdeaths]
    print('respawns:'); [print(' ',c) for c in resp[:6]]
    print('chase starts:',len(chase)); [print(' ',c) for c in chase[:40]]
    import collections
    print('score changes by addr:',collections.Counter(a for _,a,_,_ in score))
