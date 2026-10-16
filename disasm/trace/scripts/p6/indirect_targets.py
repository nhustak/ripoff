import sys,re,collections,glob
sites={0x0BDC:"JMP(aux)",0x0457:'T4K',0x0C0A:'JMP',0x0CA9:'JMP',0x0CC5:'JMP',0x1535:'JMP',0x15E3:'JMP',0x15E8:'T4K'}
def reg(pc): return (pc&0xfff)|(0x1000 if pc&0x2000 else 0)
res=collections.defaultdict(collections.Counter); raw=collections.defaultdict(collections.Counter)
other=collections.Counter()
for fn in sorted(glob.glob(sys.argv[1]+'/cpu_trace_*.log')):
    prev=None
    for l in open(fn):
        m=re.match(r'([0-9A-F]{4}): ',l)
        if not m:
            other[l.strip()[:40]]+=1
            if "loops" in l: prev=None
            continue
        pc=int(m.group(1),16)
        if prev is not None and reg(prev) in sites:
            res[reg(prev)][reg(pc)]+=1; raw[reg(prev)][(f'{prev:04X}',f'{pc:04X}')]+=1
        prev=pc
for s in sorted(sites):
    print(f'${s:04X} {sites[s]}: targets', {f'${k:04X}':v for k,v in res[s].items()}, ' raw',dict(raw[s]))
print('non-instruction lines',other.most_common(5))
