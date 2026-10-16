import sys, collections
tag=sys.argv[1]
def s12(v): return v-0x1000 if v>=0x800 else v
ram={}
for l in open(f'runs/{tag}/ram.log'):
    p=l.split(); ram[int(p[0])]=[int(x,16) for x in p[1:]]
trans=collections.Counter(); first={}
PLAYERPC={'pc=09e8','pc=0adb'}
for l in open(f'runs/{tag}/writes.log'):
    p=l.split()
    if p[1][-1]!='a' or int(p[1],16)>0x4f and p[1]!='0da': continue
    o,n=p[2].split('->')
    if o==n or p[3] in PLAYERPC: continue
    rec=int(p[4][3:],16) if p[1]=='0da' else int(p[1],16)&0xf0
    kind='enemy' if rec<0x30 else 'player'
    k=(kind,o,n,p[3]); trans[k]+=1; first.setdefault(k,int(p[0]))
print('== word-A transitions (kind, old, new, store pc+1): count, first frame')
for k,v in sorted(trans.items(), key=lambda kv: first[kv[0]]): print(k,v,first[k])
last=ram[max(ram)]
print('== end: score P1 units F1=%d thousands FE=%d, type F5=%d level FB=%d, canisters gone=%d, F9=%d'%(last[0xF1],last[0xFE],last[0xF5],last[0xFB],sum(last[0xB0+2*i+1]&1 for i in range(16)) - 8, last[0xF9]))
# shots active?
act=sum(1 for f,w in ram.items() if any((w[a]&1)==0 for a in range(0x50,0x70,4)))
print('frames with any shot slot live:',act, ' P2 slot live frames:', sum(1 for f,w in ram.items() if any((w[a]&1)==0 for a in range(0x60,0x70,4))))
