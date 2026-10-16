import collections,sys
names={0:'enemy',0x10:'enemy',0x20:'enemy',0x30:'P1',0x40:'P2'}
for t in sys.argv[1:]:
    c=collections.Counter(); canpc=collections.Counter()
    for l in open(f'runs/{t}/writes.log'):
        p=l.split(); a=int(p[1],16); o,n=[int(x,16) for x in p[2].split('->')]
        if (a&0xf)==0xa and a<0x50 and o&0x10 and not n&0x10 and not n&1:
            c[(names[a&0xf0],p[3])]+=1
        if 0xb0<=a<=0xcf and o!=n: canpc[p[3]]+=1
    print(t,dict(c),'canister-store pcs',dict(canpc))
