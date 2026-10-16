#!/bin/bash
# usage: MODE=idle FRAMES=12000 TAG=idle1 ./run.sh [extra mame args]
set -u
mkdir -p runs/$TAG
SDL_VIDEODRIVER=dummy SDL_AUDIODRIVER=dummy MODE=$MODE FRAMES=$FRAMES TAG=$TAG \
  mame ripoff -rompath /workspace/mame/roms -video none -sound none -nothrottle -skip_gameinfo \
  -seconds_to_run 3600 -snapshot_directory /workspace/mame/runs/$TAG/snaps \
  -autoboot_script /workspace/mame/trace.lua -autoboot_delay 0 "$@" 2>&1 | grep -v ALSA
echo "exit=${PIPESTATUS[0]}"
