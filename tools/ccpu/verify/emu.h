// Minimal stand-in for MAME's emu.h so that the UNMODIFIED ccpudasm.cpp
// from mamedev/mame compiles standalone. Only what ccpudasm.cpp uses.
#pragma once
#include <cstdint>
#include <ostream>
#include <sstream>
#include <cstdio>
#include <string>
#include <vector>
typedef uint8_t u8; typedef uint16_t u16; typedef uint32_t u32; typedef uint32_t offs_t;
namespace util {
template <typename... Args> void stream_format(std::ostream &s, const char *fmt, Args... args)
{ char buf[256]; std::snprintf(buf, sizeof(buf), fmt, args...); s << buf; }
class disasm_interface {
public:
	static constexpr u32 SUPPORTED = 0x80000000, STEP_COND = 0x40000000, LENGTHMASK = 0x0000ffff;
	class data_buffer {
	public:
		const std::vector<u8> *img;
		u8 r8(offs_t pc) const { return (*img)[pc & (img->size() - 1)]; }
	};
	virtual ~disasm_interface() = default;
	virtual u32 opcode_alignment() const = 0;
	virtual offs_t disassemble(std::ostream &stream, offs_t pc, const data_buffer &opcodes, const data_buffer &params) = 0;
};
}
