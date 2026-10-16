import sys
tag=sys.argv[1]
def s12(v): return v-0x1000 if v>=0x800 else v
ram={}
for l in open(f'runs/{tag}/ram.log'):
    p=l.split(); ram[int(p[0])]=[int(x,16) for x in p[1:]]
for l in open(f'runs/{tag}/writes.log'):
    f,a,ch,pc,ef=l.split()
    o,n=ch.split('->')
    if a in ('0da','00a','01a','02a','03a','04a') and o!=n:
        if pc in ('pc=09e8','pc=0adb'): continue
        rec=int(ef[3:],16) if a=='0da' else int(a,16)&0xf0
        w=ram.get(int(f)-1) or ram.get(int(f)) or ram[1]
        e=rec
        px,py=s12(w[0x30]),s12(w[0x31])
        ex,ey=s12(w[e]),s12(w[e+1])
        print(f'f{f} {a} {o}->{n} {pc} rec={rec:02x} enemy=({ex},{ey}) P1=({px},{py}) P1A={w[0x3a]:03x} dPx={abs(ex-px)} dPy={abs(ey-py)} CD=({w[e+0xC]},{w[e+0xD]}) tgt={w[e+2]:02x} can={w[w[e+2]] if w[e+2]>=0xb0 else -1},{w[w[e+2]+1] if w[e+2]>=0xb0 else -1} F={w[e+0xF]} v={w[e+6]}')
