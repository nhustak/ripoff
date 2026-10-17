-- Rip-Off trace v3 (NOTES §26, difficulty escalation). One-player game, the "hunt" autopilot, a data-RAM log per frame.
-- Observation only: inputs go through the normal ioport fields; no RAM is written.
-- Env: TAG, FRAMES, IDLET (bot idle while enemy type >= IDLET), FT (fire tolerance, deg, default 8), PRI (towing/at-canister priority, default 600),
--      SHOT (lead divisor, default 20). The hunt() logic is ported line for line in tools/remake/Waves/Program.cs.
local tag    = os.getenv("TAG") or "run"
local maxf   = tonumber(os.getenv("FRAMES") or "60000")
local FT     = tonumber(os.getenv("FT") or "8")
local PRI    = tonumber(os.getenv("PRI") or "600")
local SHOT   = tonumber(os.getenv("SHOT") or "20")
local IDLET  = tonumber(os.getenv("IDLET") or "99") -- the bot does nothing while RAM[$F5] >= IDLET (step-back test)
local dir    = "/workspace/mame/runs/"..tag
os.execute("mkdir -p "..dir)
local m = manager.machine
local cpu = m.devices[":maincpu"]
local data = cpu.spaces["data"]
local ramlog = io.open(dir.."/ram.log","w")
local IN, SW = m.ioport.ports[":INPUTS"].fields, m.ioport.ports[":SWITCHES"].fields
local coin, start = SW["Coin 1"], IN["1 Player Start"]
local L, R, F, T = IN["P1 Left"], IN["P1 Right"], IN["P1 Button 1"], IN["P1 Button 2"]
local function rd(a) return data:read_u16(a) & 0xfff end
local function sgn(v) if v >= 0x800 then return v - 0x1000 end return v end
local frame = 0
local function hunt()
  L:set_value(0); R:set_value(0); F:set_value(0); T:set_value(0)
  local pa = rd(0x3A)
  if (pa & 1) ~= 0 or (pa & 0x10) == 0 or rd(0xF5) >= IDLET then return end
  local px, py = sgn(rd(0x30)) + 0.0, sgn(rd(0x31)) + 0.0
  local best, tx, ty, any = 1e9, 0.0, 0.0, false
  for _, b in ipairs({0x00, 0x10, 0x20}) do
    local a = rd(b + 0xA)
    if (a & 1) == 0 and (a & 0x10) ~= 0 then
      local ex, ey = sgn(rd(b)) + 0.0, sgn(rd(b + 1)) + 0.0
      local d = math.max(math.abs(ex - px), math.abs(ey - py))
      local score = d - ((((a & 2) ~= 0) or a == 0x14) and PRI or 0)
      if score < best then
        best = score; any = true
        local h, sp, t = rd(b + 5) * math.pi / 180, rd(b + 6) + 0.0, d / SHOT
        tx = ex + sp * math.sin(h) * t; ty = ey - sp * math.cos(h) * t
      end
    end
  end
  if not any then return end
  local want = math.atan(tx - px, -(ty - py)) * 180 / math.pi; want = want % 360
  local diff = ((want - rd(0x35) + 540) % 360) - 180
  if diff > 3 then L:set_value(1) elseif diff < -3 then R:set_value(1) end
  if math.abs(diff) < FT and frame % 4 < 2 then F:set_value(1) end
end
_G.fsub = emu.add_machine_frame_notifier(function()
  frame = frame + 1
  if frame == 120 then coin:set_value(1) end
  if frame == 126 then coin:set_value(0) end
  if frame == 200 then start:set_value(1) end
  if frame == 206 then start:set_value(0) end
  if frame > 260 then hunt() end
  local t = {}
  for a = 0, 0xff do t[#t+1] = string.format("%03x", rd(a)) end
  ramlog:write(frame, " ", table.concat(t, " "), "\n")
  if frame % 1000 == 0 then ramlog:flush() end
  -- stop at game over (RAM[$F0] bit 1 set again after the start) or at FRAMES
  if frame >= maxf or (frame > 300 and (rd(0xF0) & 2) ~= 0) then ramlog:close(); m:exit() end
end)
