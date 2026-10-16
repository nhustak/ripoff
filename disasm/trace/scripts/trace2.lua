-- Rip-Off trace v2. Env:
--  TAG, FRAMES, PLAYERS=1|2, P1MODE/P2MODE = idle|shoot|ram|wander|pvp, P1STOP/P2STOP = frame after which that player is idle,
--  P1THRUST="a-b,c-d" extra thrust frame ranges, SNAPAT="a-b,...", AUTOSNAPS=n, TRACE="a-b,..." (needs -debug)
local tag    = os.getenv("TAG") or "run"
local maxf   = tonumber(os.getenv("FRAMES") or "6000")
local nplay  = tonumber(os.getenv("PLAYERS") or "1")
local dir    = "/workspace/mame/runs/"..tag
os.execute("mkdir -p "..dir)
local m = manager.machine
local dbg = m.debugger
if dbg then dbg.execution_state = "run" end
local cpu = m.devices[":maincpu"]
local data = cpu.spaces["data"]
local io_ = cpu.spaces["io"]
local ramlog = io.open(dir.."/ram.log","w")
local wlog   = io.open(dir.."/writes.log","w")
local ev     = io.open(dir.."/events.log","w")
local IN, SW = m.ioport.ports[":INPUTS"].fields, m.ioport.ports[":SWITCHES"].fields
local coin = SW["Coin 1"]
local start = (nplay == 2) and IN["2 Players Start"] or IN["1 Player Start"]
local ctl = {
  [0x30] = {left=IN["P1 Left"], right=IN["P1 Right"], fire=IN["P1 Button 1"], thrust=IN["P1 Button 2"],
            mode=os.getenv("P1MODE") or "idle", stop=tonumber(os.getenv("P1STOP") or "999999"), other=0x40},
  [0x40] = {left=IN["P2 Left"], right=IN["P2 Right"], fire=IN["P2 Button 1"], thrust=IN["P2 Button 2"],
            mode=os.getenv("P2MODE") or "idle", stop=tonumber(os.getenv("P2STOP") or "999999"), other=0x30},
}
local function ranges(s) local t = {}
  for a, b in (s or ""):gmatch("(%d+)-(%d+)") do for i = tonumber(a), tonumber(b) do t[i] = true end end
  return t end
local snapat, traceat, p1thrust = ranges(os.getenv("SNAPAT")), ranges(os.getenv("TRACE")), ranges(os.getenv("P1THRUST"))
local maxauto = tonumber(os.getenv("AUTOSNAPS") or "0")
local frame = 0
-- sound-polarity lab: FORCEFC="mask:value" (hex) forces bits of every store to RAM[$FC];
-- FORCEQ="n:v" forces latch output Qn to v on every OUT n.
local ffm, ffv = (os.getenv("FORCEFC") or ""):match("(%x+):(%x+)")
ffm = ffm and tonumber(ffm, 16); ffv = ffv and tonumber(ffv, 16)
local fqn, fqv = (os.getenv("FORCEQ") or ""):match("(%d+):(%d+)")
fqn = fqn and tonumber(fqn); fqv = fqv and tonumber(fqv)
local function rd(a) return data:read_u16(a) & 0xfff end
local function lstpc()
  local pc = cpu.state["CURPC"].value
  return (pc & 0xfff) | (((pc & 0x2000) ~= 0) and 0x1000 or 0)
end
local watch = {}
for _, base in ipairs({0x00,0x10,0x20,0x30,0x40,0xD0}) do
  for _, o in ipairs({0x2,0x3,0xA,0xB,0xC,0xD,0xF}) do watch[base+o] = true end
end
for a = 0x50, 0x6F do watch[a] = true end
for a = 0xB0, 0xCF do watch[a] = true end
for a = 0xF0, 0xFF do watch[a] = true end
watch[0x78] = true; watch[0xE8] = true; watch[0xED] = true
_G.wtap = data:install_write_tap(0x00, 0xff, "trace", function(offset, value, mask)
  local a = offset & 0xff
  if watch[a] then
    local pc = lstpc()
    if pc < 0xCAA or pc > 0xCC5 then
      local v, old = value & 0xfff, rd(a)
      local ctx = ""
      if (a & 0xf) == 0xA and old ~= v then
        local t = {}
        for e = 0xE0, 0xEF do t[#t+1] = string.format("%03x", rd(e)) end
        ctx = " E=" .. table.concat(t, ",")
      end
      wlog:write(string.format("%d %03x %03x->%03x pc=%04x EF=%03x%s\n", frame, a, old, v, pc, rd(0xEF), ctx))
    end
  end
  if a == 0xFC and ffm then return (value & ~ffm) | (ffv & ffm) end
end)
local outs = {}
_G.iotap = io_:install_write_tap(0x0, 0x7, "outs", function(offset, value, mask)
  if fqn and offset == fqn then return fqv end
  if offset == 0 or offset == 1 or offset == 2 then return end  -- serial lines: too many
  local b = value & 1
  if outs[offset] ~= b then outs[offset] = b
    ev:write(string.format("%d OUT%d=%d pc=%04x\n", frame, offset, b, lstpc())) end
end)
local function snap(why) m.video:snapshot(); ev:write(string.format("%d SNAP %s\n", frame, why)) end
local function sgn(v) if v >= 0x800 then return v - 0x1000 end return v end
local function aim(pb, tx, ty)
  local px, py = sgn(rd(pb)), sgn(rd(pb+1))
  local want = math.deg(math.atan(tx - px, -(ty - py))) % 360
  return (want - rd(pb+5) + 540) % 360 - 180, math.max(math.abs(tx-px), math.abs(ty-py))
end
local function nearest_enemy(pb)
  local px, py = sgn(rd(pb)), sgn(rd(pb+1))
  local best, bx, by
  for _, b in ipairs({0x00,0x10,0x20}) do
    local a = rd(b+0xA)
    if (a & 1) == 0 and (a & 0x10) ~= 0 then
      local ex, ey = sgn(rd(b)), sgn(rd(b+1))
      local d = math.max(math.abs(ex-px), math.abs(ey-py))
      if not best or d < best then best, bx, by = d, ex, ey end
    end
  end
  return best, bx, by
end
local function drive(pb)
  local c = ctl[pb]
  c.left:set_value(0); c.right:set_value(0); c.fire:set_value(0); c.thrust:set_value(0)
  local mode = c.mode
  if frame > c.stop then mode = "idle" end
  if pb == 0x30 and p1thrust[frame] then c.thrust:set_value(1) end
  if mode == "idle" or (rd(pb+0xA) & 0x10) == 0 then return end
  if mode == "wander" then
    local k = ((frame // 40) * 2654435761 + pb) % 4294967296
    c.left:set_value((k >> 3) & 1); c.right:set_value(((k >> 3) & 1 == 0) and ((k >> 5) & 1) or 0)
    c.thrust:set_value(((k >> 7) % 4 ~= 0) and 1 or 0)
    return
  end
  local tx, ty
  if mode == "pvp" then tx, ty = sgn(rd(c.other)), sgn(rd(c.other+1))
  else local d; d, tx, ty = nearest_enemy(pb); if not d then return end end
  local diff, dist = aim(pb, tx, ty)
  if diff > 4 then c.left:set_value(1) elseif diff < -4 then c.right:set_value(1) end
  if (mode == "shoot" or mode == "pvp") and math.abs(diff) < 10 and frame % 4 < 2 then c.fire:set_value(1) end
  if mode == "ram" and math.abs(diff) < 20 then c.thrust:set_value(1) end
end
local prevA, nsnap, tracing = {}, 0, false
_G.fsub = emu.add_machine_frame_notifier(function()
  frame = frame + 1
  if frame == 120 or (nplay == 2 and frame == 150) then coin:set_value(1) end
  if frame == 126 or (nplay == 2 and frame == 156) then coin:set_value(0) end
  if frame == 200 then start:set_value(1); ev:write("200 START"..nplay.."\n") end
  if frame == 206 then start:set_value(0) end
  if frame > 260 then drive(0x30); if nplay == 2 then drive(0x40) end end
  if dbg then
    if traceat[frame] and not tracing then dbg:command("trace "..dir.."/cpu_trace_"..frame..".log,:maincpu"); tracing = true end
    if tracing and not traceat[frame] then dbg:command("trace off,:maincpu"); tracing = false end
  end
  local t = {}
  for a = 0, 0xff do t[#t+1] = string.format("%03x", rd(a)) end
  ramlog:write(frame, " ", table.concat(t, " "), "\n")
  for _, b in ipairs({0x00,0x10,0x20,0x30,0x40}) do
    local a = rd(b+0xA)
    if prevA[b] and prevA[b] ~= a and nsnap < maxauto then
      nsnap = nsnap + 1; snap(string.format("rec%02x A %03x->%03x", b, prevA[b], a))
    end
    prevA[b] = a
  end
  if snapat[frame] then snap("at") end
  if frame % 500 == 0 then ramlog:flush(); wlog:flush(); ev:flush() end
  if frame >= maxf then
    if tracing then dbg:command("trace off,:maincpu") end
    ramlog:close(); wlog:close(); ev:close(); m:exit() end
end)
