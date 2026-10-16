import wave, numpy as np
SPF=48000*0.026316
def load(t):
    w=wave.open(f'runs/{t}.wav'); a=np.frombuffer(w.readframes(w.getnframes()),dtype=np.int16)[0::2].astype(float); return a
def frms(a):
    n=int(len(a)/SPF); return np.array([a[int(i*SPF):int((i+1)*SPF)].std() for i in range(n)])
def ramdiff(t):
    A=open('runs/sl_base/ram.log').read().split('\n'); B=open(f'runs/{t}/ram.log').read().split('\n')
    words=set(); nd=0
    for x,y in zip(A,B):
        if x!=y:
            nd+=1
            px,py=x.split(),y.split()
            for i,(u,v) in enumerate(zip(px[1:],py[1:])):
                if u!=v: words.add(hex(i))
    return nd,sorted(words)
base=load('sl_base'); rb=frms(base)
np.save('/tmp/rb.npy',rb)
for t in ['sl_q4_1','sl_q4_0','sl_q3_1','sl_q3_0','sl_q7_1','sl_fc2_0','sl_fc2_1','sl_fc3_0','sl_fc3_1','sl_fc4_0','sl_fc4_1']:
    v=load(t); rv=frms(v); rd=frms(v-base)
    np.save(f'/tmp/{t}_rv.npy',rv); np.save(f'/tmp/{t}_rd.npy',rd)
    print(t,'ramdiff',ramdiff(t),'overall rms base %.0f var %.0f diff %.0f'%(base.std(),v.std(),(v-base).std()))
