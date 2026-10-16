import sys
tag=sys.argv[1]
def s12(v): return v-0x1000 if v>=0x800 else v
rows=[]
for l in open(f'runs/{tag}/ram.log'):
    p=l.split(); f=int(p[0]); w=[int(x,16) for x in p[1:]]
    rows.append((f,w))
prev=None
glob=[0xF0,0xF3,0xF5,0xF8,0xF9,0xFA,0xFB,0xEC,0x78,0xFC]
for f,w in rows:
    key=tuple(w[b+0xA] for b in (0,0x10,0x20))+(w[0x3A],)+tuple(w[g] for g in (0xF0,0xF5,0xF8,0xF9,0xFB))+tuple(w[0xB0+2*i+1]&1 for i in range(16))
    if key!=prev:
        out=[f'f{f}']
        for b in (0,0x10,0x20):
            out.append(f'r{b:02x}:A={w[b+0xA]:03x} xy=({s12(w[b])},{s12(w[b+1])}) a={w[b+5]} v={w[b+6]} tgt={w[b+2]:02x} CD=({w[b+0xC]},{w[b+0xD]}) F={w[b+0xF]}')
        out.append(f'P1:A={w[0x3A]:03x} xy=({s12(w[0x30])},{s12(w[0x31])}) s4={w[0x34]}')
        out.append('G:'+' '.join(f'{g:02x}={w[g]:x}' for g in glob))
        out.append('can:'+''.join(str(w[0xB0+2*i+1]&1) for i in range(16)))
        print(' | '.join(out))
        prev=key
