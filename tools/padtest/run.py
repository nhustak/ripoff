# End-to-end pad test (NOTES §25): runs the real game under Xvfb (software GL) with an SDL2 virtual game controller
# attached inside the process by the PadHook startup hook, plays a button script and saves screenshots.
#   dotnet build tools/padtest -c Release && dotnet build src/RipOff.Game
#   python3 tools/padtest/run.py src/RipOff.Game/bin/Debug/net10.0/RipOff.dll "0.1:shot:0;1:press:A;2:press:DOWN;3:shot:1" out
# Script steps "seconds:action[:args]": press:BTN, hold:BTN, release:BTN, axis:N:VALUE (SDL axis 0-5), shot:N.
# Buttons: A B X Y BACK GUIDE START LS RS LB RB UP DOWN LEFT RIGHT. Linux only; needs Xvfb, Mesa swrast, Pillow, xdotool.
import subprocess, time, os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.environ.get('PADTEST_OUT', '/tmp/padtest'); os.makedirs(OUT, exist_ok=True)
from PIL import ImageGrab, Image
dll, script, tag = sys.argv[1], sys.argv[2], sys.argv[3]
extra = sys.argv[4:]
disp=':%d' % (150 + os.getpid() % 50)
xv = subprocess.Popen(['Xvfb', disp, '-screen', '0', '1024x768x24', '+extension', 'GLX', '-ac'], stderr=subprocess.DEVNULL)
time.sleep(1)
env = dict(os.environ, DISPLAY=disp, MESA_LOADER_DRIVER_OVERRIDE='swrast', LIBGL_ALWAYS_SOFTWARE='1',
           DOTNET_STARTUP_HOOKS=os.path.join(HERE, 'bin', 'Release', 'net10.0', 'PadHook.dll'), RIPOFF_PADSCRIPT=script,
           XDG_CONFIG_HOME=OUT + '/cfg-'+tag, SDL_AUDIODRIVER='dummy', SDL_VIDEODRIVER='x11')
logp=OUT + f'/{tag}.log'; log = open(logp,'w')
g = subprocess.Popen([os.environ.get('DOTNET', os.path.expanduser('~/.dotnet/dotnet')), os.path.abspath(dll)]+extra, env=env, stdout=log, stderr=subprocess.STDOUT)
done=set(); t0=time.time(); shots=[]
while time.time()-t0 < 40 and g.poll() is None:
    txt=open(logp).read()
    for ln in txt.splitlines():
        if 'SNAP:shot:' in ln and ln not in done:
            done.add(ln); n=ln.split(':shot:')[1].strip()
            ImageGrab.grab(xdisplay=disp).save(OUT + f'/{tag}-{n}.png'); shots.append(n)
            title = subprocess.run(['xdotool','search','--name','.','getwindowname'], env=env, capture_output=True, text=True).stdout.strip().splitlines()
            print(f'shot {n}: title={title[-1:]}')
        if 'script done' in ln: break
    else:
        time.sleep(0.05); continue
    break
g.kill(); xv.kill(); log.close()
ims=[Image.open(OUT + f'/{tag}-{n}.png').resize((400,300)) for n in shots]
if ims:
    s=Image.new('RGB',(400*len(ims),300)); [s.paste(im,(400*i,0)) for i,im in enumerate(ims)]; s.save(OUT + f'/{tag}-sheet.png')
for ln in open(logp).read().splitlines():
    if 'padhook' in ln or 'Exception' in ln or 'RipOff:' in ln: print(ln)
