import sys
tag=sys.argv[1]; f0=int(sys.argv[2]); f1=int(sys.argv[3])
ram={}
for l in open(f'runs/{tag}/ram.log'):
    p=l.split(); f=int(p[0])
    if f0<=f<=f1: ram[f]=[int(x,16) for x in p[1:]]
prev=None
for f in sorted(ram):
    w=ram[f]
    c=tuple((w[0xB0+2*i],w[0xB0+2*i+1]) for i in range(8))
    st=tuple((w[b+0xA],w[b+2]) for b in (0,0x10,0x20))
    if c!=prev:
        print(f, ' '.join(f'{i}:({x//2},{y//2}){"G" if y&1 else ""}' for i,(x,y) in enumerate(c)), '| enemies(A,tgt):', ' '.join(f'{a:03x}/{t:02x}' for a,t in st), 'F0=%x'%w[0xF0])
    prev=c
