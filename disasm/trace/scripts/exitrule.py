# At each pick-up store ($0B88, logged pc=0b89): exit point C/D vs ROM spawn table index (word B & 7).
# Uses the C/D stores at $0BCD/$0BD8 (logged pc=0bce/0bd9) in the same frame, and word B from the frame log.
SP=[(0,0),(304,0),(896,0),(592,0),(1024,768),(720,768),(128,768),(432,768)]
tot=ok=0
for t in ['idle_snap','shoot1','ram1','wander1']:
    ram={}
    for l in open(f'runs/{t}/ram.log'):
        p=l.split(); ram[int(p[0])]=[int(x,16) for x in p[1:]]
    pend=None; c=d=None
    for l in open(f'runs/{t}/writes.log'):
        p=l.split(); f=int(p[0])
        if p[1]=='0da' and p[3]=='pc=0b89': pend=(f,int(p[4][3:],16)); c=d=None
        elif pend and p[1]=='0dc' and p[3]=='pc=0bce': c=int(p[2].split('->')[1],16)
        elif pend and p[1]=='0dd' and p[3]=='pc=0bd9':
            d=int(p[2].split('->')[1],16); f0,rec=pend
            b=ram[f0+1][rec+0xB]; tot+=1; good=(c,d)==SP[b&7]; ok+=good
            print(f'{t} f{f0} rec{rec:02x} exit=({c},{d}) wordB={b:03x} B&7={b&7} table={SP[b&7]} {"OK" if good else "MISMATCH"}')
            pend=None
print(f'total {tot}, match {ok}')
