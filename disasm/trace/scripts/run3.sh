#!/bin/bash
# usage: TAG=x [FRAMES=n FT= PRI= SHOT=] ./run3.sh   (NOTES §26; logs to /workspace/mame/runs/$TAG/ram.log, not committed)
mkdir -p /workspace/mame/runs/$TAG
SDL_VIDEODRIVER=dummy SDL_AUDIODRIVER=dummy mame ripoff -rompath /workspace/mame/roms -video none -sound none -nothrottle \
  -skip_gameinfo -seconds_to_run 7200 -autoboot_script "$(dirname "$(readlink -f "$0")")/trace3.lua" -autoboot_delay 0 "$@" 2>&1 | grep -v ALSA
echo "exit=${PIPESTATUS[0]}"
