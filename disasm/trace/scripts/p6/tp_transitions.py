import sys
tag=sys.argv[1]
def s12(v): return v-0x1000 if v>=0x800 else v
prev=None
for l in open(f'runs/{tag}/ram.log'):
    p=l.split(); f=int(p[0]); w=[int(x,16) for x in p[1:]]
    if prev:
        ch=[]
        for a in (0x3a,0x4a,0xf0,0xf1,0xf2,0xfe,0xff,0x0a,0x1a,0x2a,0xda):
            if w[a]!=prev[a]: ch.append(f'{a:02x}:{prev[a]:03x}->{w[a]:03x}')
        if ch:
            print(f'f{f}', ' '.join(ch), f'P1=({s12(w[0x30])},{s12(w[0x31])}) P2=({s12(w[0x40])},{s12(w[0x41])})')
    prev=w
