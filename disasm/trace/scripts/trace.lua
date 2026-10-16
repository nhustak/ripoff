-- Rip-Off enemy-state trace. Env: MODE=idle|shoot|ram, FRAMES=N, TAG=name
-- Outputs in /workspace/mame/runs/<TAG>/: ram.log (per-frame full data RAM),
-- writes.log (selected RAM writes with PC), events.log, snaps/ (snapshots)
local mode   = os.getenv("MODE") or "idle"
local maxf   = tonumber(os.getenv("FRAMES") or "6000")
local tag    = os.getenv("TAG") or mode
local dir    = "/workspace/mame/runs/"..tag
os.execute("mkdir -p "..dir)
local m = manager.machine
local cpu = m.devices[":maincpu"]
local data = cpu.spaces["data"]
local io_ = cpu.spaces["io"]
local ramlog = io.open(dir.."/ram.log","w")
local wlog   = io.open(dir.."/writes.log","w")
local ev     = io.open(dir.."/events.log","w")
local IN, SW = m.ioport.ports[":INPUTS"].fields, m.ioport.ports[":SWITCHES"].fields
local coin, start1 = SW["Coin 1"], IN["1 Player Start"]
local left, right, fire, thrust = IN["P1 Left"], IN["P1 Right"], IN["P1 Button 1"], IN["P1 Button 2"]
local frame = 0
local function rd(a) return data:read_u16(a) & 0xfff end
local function lstpc()  -- listing address from CCPU PC (low 12 bits + bank bit 13)
  local pc = cpu.state["CURPC"].value
  return (pc & 0xfff) | (((pc & 0x2000) ~= 0) and 0x1000 or 0)
end
-- which words to log writes for: enemy/player word A (state), word B (rng), word 2 (target ptr),
-- C/D (target point), F (hop count), canister table B0-CF, globals F0-FF, 78
local watch = {}
for _, base in ipairs({0x00,0x10,0x20,0x30,0x40,0xD0}) do
  for _, o in ipairs({0x2,0xA,0xB,0xC,0xD,0xF}) do watch[base+o] = true end
end
for a = 0xB0, 0xCF do watch[a] = true end
for a = 0xF0, 0xFF do watch[a] = true end
watch[0x78] = true
local last = {}
_G.wtap = data:install_write_tap(0x00, 0xff, "trace", function(offset, value, mask)
  local a = offset & 0xff
  if watch[a] then
    local pc = lstpc()
    if pc < 0xCAA or pc > 0xCC5 then  -- skip the $0CAA block copy
      local v = value & 0xfff
      local old = rd(a)
      local ctx = ""
      if (a & 0xf) == 0xA and old ~= v then
        local t = {}
        for e = 0xE0, 0xEF do t[#t+1] = string.format("%03x", rd(e)) end
        ctx = " E=" .. table.concat(t, ",")
      end
      wlog:write(string.format("%d %03x %03x->%03x pc=%04x EF=%03x%s\n", frame, a, old, v, pc, rd(0xEF), ctx))
    end
  end
end)
-- io writes (sound/outputs): log changes per output bit
local outs = {}
_G.iotap = io_:install_write_tap(0x0, 0x7, "outs", function(offset, value, mask)
  local b = value & 1
  if outs[offset] ~= b then outs[offset] = b
    ev:write(string.format("%d OUT%d=%d pc=%04x\n", frame, offset, b, lstpc())) end
end)
local function snap(why)
  m.video:snapshot()
  ev:write(string.format("%d SNAP %s\n", frame, why))
end
-- player autopilot (P1 record at $30: x=$30, y=$31, angle=$35)
local function nearest_enemy()
  local px, py = rd(0x30), rd(0x31)
  local best, bx, by
  for _, b in ipairs({0x00,0x10,0x20}) do
    local a = rd(b+0xA)
    if (a & 1) == 0 and (a & 0x10) ~= 0 then
      local ex, ey = rd(b), rd(b+1)
      if ex >= 0x800 then ex = ex - 0x1000 end
      if ey >= 0x800 then ey = ey - 0x1000 end
      local d = math.max(math.abs(ex-px), math.abs(ey-py))
      if not best or d < best then best, bx, by = d, ex, ey end
    end
  end
  return best, bx, by
end
local function autopilot()
  local d, ex, ey = nearest_enemy()
  left:set_value(0); right:set_value(0); fire:set_value(0); thrust:set_value(0)
  if not d then return end
  local px, py = rd(0x30), rd(0x31)
  -- heading convention (NOTES 11.1): moves (sin t, -cos t) in RAM coords
  local want = math.deg(math.atan(ex - px, -(ey - py))) % 360
  local ang = rd(0x35)
  local diff = (want - ang + 540) % 360 - 180   -- + means need to increase angle (Left adds)
  if diff > 4 then left:set_value(1) elseif diff < -4 then right:set_value(1) end
  if mode == "shoot" then
    if math.abs(diff) < 10 and frame % 4 < 2 then fire:set_value(1) end
  elseif mode == "ram" then
    if math.abs(diff) < 20 then thrust:set_value(1) end
  end
end
local prevA = {}
local snapat = {}
for a, b in (os.getenv("SNAPAT") or ""):gmatch("(%d+)-(%d+)") do
  for i = tonumber(a), tonumber(b) do snapat[i] = true end
end
local maxauto = tonumber(os.getenv("AUTOSNAPS") or "80")
local nsnap = 0
_G.fsub = emu.add_machine_frame_notifier(function()
  frame = frame + 1
  if frame == 120 then coin:set_value(1) end
  if frame == 126 then coin:set_value(0) end
  if frame == 200 then start1:set_value(1); ev:write("200 START1\n") end
  if frame == 206 then start1:set_value(0) end
  if frame > 260 and (mode == "shoot" or mode == "ram") then autopilot() end
  if frame > 260 and mode == "wander" then
    -- deterministic pseudo-random input changes every 40 frames
    local k = (frame // 40) * 2654435761 % 4294967296
    left:set_value((k >> 3) & 1); right:set_value(((k >> 3) & 1 == 0) and ((k >> 5) & 1) or 0)
    thrust:set_value(((k >> 7) % 4 ~= 0) and 1 or 0); fire:set_value(0)
  end
  local t = {}
  for a = 0, 0xff do t[#t+1] = string.format("%03x", rd(a)) end
  ramlog:write(frame, " ", table.concat(t, " "), "\n")
  -- snapshot on enemy word-A change (capped)
  for _, b in ipairs({0x00,0x10,0x20}) do
    local a = rd(b+0xA)
    if prevA[b] and prevA[b] ~= a and nsnap < maxauto then
      nsnap = nsnap + 1
      snap(string.format("rec%02x A %03x->%03x", b, prevA[b], a))
    end
    prevA[b] = a
  end
  if snapat[frame] then snap("at") end
  if frame % 500 == 0 then ramlog:flush(); wlog:flush(); ev:flush() end
  if frame >= maxf then ramlog:close(); wlog:close(); ev:close(); m:exit() end
end)
