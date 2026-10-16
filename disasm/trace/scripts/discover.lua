local f = io.open("/workspace/mame/discover.txt","w")
local m = manager.machine
f:write("MAME "..emu.app_version().."\n")
for tag,dev in pairs(m.devices) do
  f:write("device "..tag.." "..(dev.shortname or "?").."\n")
  if dev.spaces then for sn,sp in pairs(dev.spaces) do
    f:write("  space "..tostring(sn).." name="..tostring(sp.name).." databits="..tostring(sp.data_width).." addrbits="..tostring(sp.addr_width).." shift="..tostring(sp.shift).."\n")
    for _,e in ipairs(sp.map.entries) do
      f:write(string.format("    map %x-%x r=%s w=%s\n", e.address_start, e.address_end, tostring(e.read.handlertype), tostring(e.write.handlertype)))
    end
  end end
end
for ptag,port in pairs(m.ioport.ports) do
  for fname,fld in pairs(port.fields) do
    f:write(string.format("port %s field '%s' mask=%x type=%s\n", ptag, fname, fld.mask, tostring(fld.type)))
  end
end
f:close()
m:exit()
