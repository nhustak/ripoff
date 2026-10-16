using System; using System.IO; using System.Linq; using System.Collections.Generic; using RipOff.Core;
var tag = args[0]; var mode = args.Length>4? args[4] : "idle"; int startF = int.Parse(args[1]); int maxF = int.Parse(args[2]);
var R = new Dictionary<int,int[]>();
foreach (var l in File.ReadLines(Path.Combine(Environment.GetEnvironmentVariable("RUNS") ?? "/workspace/mame/runs", tag, "ram.log"))) { var p=l.Split(' ',StringSplitOptions.RemoveEmptyEntries); R[int.Parse(p[0])]=p.Skip(1).Select(x=>Convert.ToInt32(x,16)).ToArray(); }
int S(int v){v&=0xFFF;return v>=0x800?v-0x1000:v;}
var sim = new Simulation(); var st = sim.State;
var pre = R[startF-1];
st.CanisterDip = 3; st.Credits = 1;
st.Enemies[0].B = pre[0x0B]; st.Enemies[1].B = pre[0x1B]; st.Enemies[2].B = pre[0x2B]; st.Players[0].B = pre[0x3B];
st.PassCounter = pre[0xA8];
if (mode.StartsWith("2p:")) { st.Credits=2; sim.StartGame(false); st.Players[1].B = pre[0x4B]; } else sim.StartGame(true);
int firstDiff=-1; int reported=0;
var inp = new FrameInput();
var q = new Queue<FrameInput>(); q.Enqueue(new FrameInput()); q.Enqueue(new FrameInput());
string m1 = mode, m2 = "idle"; bool two = false;
if (mode.StartsWith("2p:")) { var pp = mode.Split(':'); m1 = pp[1]; m2 = pp[2]; two = true; }
PlayerInput Drive(int frame, int pi_, string md) {
  var pi = new PlayerInput(); var me = st.Players[pi_];
  if (md=="idle" || (two && (me.A & 0x10)==0)) return pi;
  if (md=="wander") { long k = (((long)(frame/40) * 2654435761L) + (pi_==0?0x30:0x40)) % 4294967296L; if(!two) k = ((long)(frame/40) * 2654435761L) % 4294967296L;
     pi.Left=((k>>3)&1)==1; pi.Right=((k>>3)&1)==0 && ((k>>5)&1)==1; pi.Thrust=((k>>7)%4)!=0; return pi; }
  int px=S(me.X&0xFFF), py=S(me.Y&0xFFF); int tx, ty;
  if (md=="pvp") { var o=st.Players[1-pi_]; tx=S(o.X&0xFFF); ty=S(o.Y&0xFFF); }
  else { double best=double.MaxValue; bool any=false; tx=ty=0;
    foreach (var e in st.Enemies) { int a=e.A; if ((a&1)!=0 || (a&0x10)==0) continue; int ex=S(e.X&0xFFF), ey=S(e.Y&0xFFF); double d=Math.Max(Math.Abs(ex-px),Math.Abs(ey-py)); if(d<best){best=d;tx=ex;ty=ey;any=true;} }
    if(!any) return pi; }
  double want = Math.Atan2(tx-px, -(ty-py))*180/Math.PI; want = ((want%360)+360)%360;
  double diff = ((want - me.Heading + 540) % 360) - 180;
  if (diff>4) pi.Left=true; else if (diff< -4) pi.Right=true;
  if ((md=="shoot"||md=="pvp") && Math.Abs(diff)<10 && frame%4<2) pi.Fire=true;
  if (md=="ram" && Math.Abs(diff)<20) pi.Thrust=true;
  return pi;
}
for (int f=startF; f<=maxF; f++) {
  sim.Step(q.Dequeue());
  inp = new FrameInput();
  if (f > 260) { inp.P1 = Drive(f,0,m1); if (two) inp.P2 = Drive(f,1,m2); }
  q.Enqueue(inp);
  if (!R.TryGetValue(f, out var w)) continue;
  var diffs = new List<string>();
  for (int i=0;i<3;i++){ var e=st.Enemies[i]; int b=0x10*i;
    void chk(string n,int sv,int rv){ if(sv!=rv) diffs.Add($"e{i}.{n} sim={sv} ram={rv}"); }
    chk("A",e.A,w[b+0xA]);
    if (!EnemyState.IsFree(e.A)) { chk("X",e.X,S(w[b])); chk("Y",e.Y,S(w[b+1])); chk("H",e.Heading,w[b+5]); chk("Sp",e.Speed,w[b+6]); chk("C",e.C,S(w[b+0xC])); chk("D",e.D,S(w[b+0xD])); chk("F",e.F,w[b+0xF]); chk("B",e.B,w[b+0xB]); chk("can",0xB0+2*e.Word2,w[b+2]); }
  }
  for (int pp=0; pp<2; pp++) { var r=st.Players[pp]; int b=0x30+0x10*pp; void chk(string n,int sv,int rv){ if(sv!=rv) diffs.Add($"p{pp+1}.{n} sim={sv} ram={rv}"); }
    chk("A",r.A,w[b+0xA]); if(!EnemyState.IsFree(r.A)){chk("X",r.X,w[b]);chk("Y",r.Y,w[b+1]);chk("H",r.Heading,w[b+5]);chk("Sp",r.Speed,w[b+6]);} }
  for (int j=0;j<16;j++){ if(st.Canisters[j].X2!=w[0xB0+2*j]||st.Canisters[j].Y2!=w[0xB1+2*j]) diffs.Add($"can{j} sim=({st.Canisters[j].X2},{st.Canisters[j].Y2}) ram=({w[0xB0+2*j]},{w[0xB1+2*j]})"); }
  if (st.WaveCounter!=w[0xEC]) diffs.Add($"EC sim={st.WaveCounter} ram={w[0xEC]}");
  if (st.Score[1]!=w[0xF2]) diffs.Add($"score2 sim={st.Score[1]} ram={w[0xF2]}");
  if (st.Score[0]!=w[0xF1]) diffs.Add($"score sim={st.Score[0]} ram={w[0xF1]}");
  if (diffs.Count>0 && reported<int.Parse(args[3])) { Console.WriteLine($"f{f}: "+string.Join("; ",diffs.Take(8))); reported++; if(firstDiff<0) firstDiff=f; }
}
Console.WriteLine($"first diff {firstDiff}");
