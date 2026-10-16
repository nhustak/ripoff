# Which player does a chasing enemy steer at in a 2-player game?  For every frame where an enemy is in $018
# in two consecutive frames and its C/D equals one player's position at the previous frame end, test
# distance rules (12-bit wrapped differences), separately for P1 and P2, with a tie rule.
import collections, itertools, sys
def w12(v): v&=0xfff; return v-0x1000 if v>=0x800 else v
M={'cheb':lambda dx,dy:max(abs(dx),abs(dy)),'absdx':lambda dx,dy:abs(dx),'max(|dx|,-dy)':lambda dx,dy:max(abs(dx),-dy),
   'max(|dx|,dy)':lambda dx,dy:max(abs(dx),dy),'|dy|':lambda dx,dy:abs(dy),'manhattan':lambda dx,dy:abs(dx)+abs(dy),'euclid2':lambda dx,dy:dx*dx+dy*dy}
rows=[]; unmatched=0
for tag in sys.argv[1:]:
    R={}
    for l in open(f'runs/{tag}/ram.log'):
        p=l.split(); R[int(p[0])]=[int(x,16) for x in p[1:]]
    for f in sorted(R):
        if f-1 not in R: continue
        w,pw=R[f],R[f-1]
        for e in (0,0x10,0x20):
            if w[e+0xa]!=0x18 or pw[e+0xa]!=0x18: continue
            cd=(w[e+0xc],w[e+0xd])
            m=0x30 if cd==(pw[0x30],pw[0x31]) else 0x40 if cd==(pw[0x40],pw[0x41]) else None
            if not m: unmatched+=1; continue
            rows.append((tag,f,e,m,w12(pw[e]-pw[0x30]),w12(pw[e+1]-pw[0x31]),w12(pw[e]-pw[0x40]),w12(pw[e+1]-pw[0x41])))
print('rows with C/D = a player position (prev frame end):',len(rows),' rows with C/D = neither:',unmatched)
res=[]
for k1,k2 in itertools.product(M,M):
    for tie in (0x30,0x40):
        good=sum((0x30 if M[k1](r[4],r[5])<M[k2](r[6],r[7]) else 0x40 if M[k2](r[6],r[7])<M[k1](r[4],r[5]) else tie)==r[3] for r in rows)
        res.append((good,k1,k2,'tie->P%d'%(1 if tie==0x30 else 2)))
res.sort(reverse=True)
print('best fits (matches, P1 metric, P2 metric, tie):')
for r in res[:6]: print('  ',r)
print('same metric for both players:')
for r in res:
    if r[1]==r[2]: print('  ',r)
