#!/bin/sh
# Regenerate disasm/ from rom/. Deterministic; re-running gives identical files.
set -e
cd "$(dirname "$0")/../.."
python3 tools/ccpu/build_image.py rom disasm/ripoff_program.bin
python3 tools/ccpu/listing.py disasm/ripoff_program.bin disasm/ripoff.lst disasm/ripoff_xref.json
python3 tools/ccpu/shapes.py disasm/ripoff_program.bin disasm/shapes > /dev/null
