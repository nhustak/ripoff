// Runs MAME's ccpu_disassembler at every byte offset of an image and prints
// "addr len cond text", the same format as ccpudasm.py's __main__ mode.
#include "emu.h"
#include "ccpudasm.h"
#include <fstream>
#include <iostream>
#include <iterator>
int main(int argc, char **argv)
{
	std::ifstream f(argv[1], std::ios::binary);
	std::vector<u8> img((std::istreambuf_iterator<char>(f)), std::istreambuf_iterator<char>());
	util::disasm_interface::data_buffer buf; buf.img = &img;
	ccpu_disassembler d;
	for (offs_t pc = 0; pc < img.size(); pc++) {
		std::ostringstream s;
		offs_t r = d.disassemble(s, pc, buf, buf);
		std::printf("%04X %u %d %s\n", pc, r & util::disasm_interface::LENGTHMASK,
			(r & util::disasm_interface::STEP_COND) ? 1 : 0, s.str().c_str());
	}
	return 0;
}
