#!/bin/bash
# usage: TAG=x FRAMES=n [PLAYERS=2 P1MODE=.. P2MODE=.. ...] ./run2.sh [extra mame args e.g. -debug -debugger none, -wavwrite f.wav]
mkdir -p /workspace/mame/runs/$TAG
SDL_VIDEODRIVER=dummy SDL_AUDIODRIVER=dummy mame ripoff -rompath /workspace/mame/roms -video none -sound none -nothrottle \
  -skip_gameinfo -seconds_to_run 3600 -snapshot_directory /workspace/mame/runs/$TAG/snaps \
  -autoboot_script /workspace/mame/trace2.lua -autoboot_delay 0 "$@" 2>&1 | grep -v ALSA
echo "exit=${PIPESTATUS[0]}"
