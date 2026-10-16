import numpy as np
rb=np.load('/tmp/rb.npy'); N=len(rb)
def qlevels(t,n):
    lv=np.full(N+2,-1); cur=None; last={}
    for l in open(f'runs/{t}/events.log'):
        p=l.split()
        if len(p)>=2 and p[1].startswith(f'OUT{n}='):
            last[int(p[0])]=int(p[1][-1])
    v=1
    for f in range(N+2):
        if f in last: v=last[f]
        lv[f]=v
    return lv
fc=np.zeros(N+2,dtype=int)
for l in open('runs/sl_base/ram.log'):
    p=l.split(); f=int(p[0])
    if f<N+2: fc[f]=int(p[1+0xfc],16)
# audio frame i covers emulated frame i+1 approx (frame counter increments at frame end)
def report(t,lev,name):
    rv=np.load(f'/tmp/{t}_rv.npy'); rd=np.load(f'/tmp/{t}_rd.npy')
    L=lev[1:N+1]
    for val in (0,1):
        m=L==val
        if m.sum()==0: continue
        print(f'  {t}: frames where base {name}={val}: n={m.sum():5d} base rms={rb[m].mean():7.0f} forced rms={rv[m].mean():7.0f} diff rms={rd[m].mean():7.0f}')
for n,ts in ((4,['sl_q4_1','sl_q4_0']),(3,['sl_q3_1','sl_q3_0']),(7,['sl_q7_1'])):
    lev=qlevels('sl_base',n)
    for t in ts: report(t,lev,f'Q{n}')
for b,ts in ((2,['sl_fc2_0','sl_fc2_1']),(3,['sl_fc3_0','sl_fc3_1']),(4,['sl_fc4_0','sl_fc4_1'])):
    lev=(fc>>b)&1
    for t in ts: report(t,lev,f'FCbit{b}')
