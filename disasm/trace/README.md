# MAME trace evidence (pass 5)

Evidence for NOTES §18 and the pass-5 rows in GAME_RULES.md. Recorded 2026-10-16 with MAME 0.276 (Debian 13 `mame` package), driver `ripoff`, ROM set verified good (`mame -verifyroms ripoff`). No ROM data and no MAME binaries are stored here. The excerpts contain data-RAM values only.

## How it was run

```
SDL_VIDEODRIVER=dummy SDL_AUDIODRIVER=dummy MODE=<idle|shoot|ram|wander> FRAMES=12000 TAG=<run> \
  mame ripoff -rompath <roms> -video none -sound none -nothrottle -skip_gameinfo -seconds_to_run 3600 \
  -snapshot_directory runs/<run>/snaps -autoboot_script scripts/trace.lua -autoboot_delay 0
```

(`scripts/run.sh` wraps this. The scripts use absolute paths under `/workspace/mame/`; adjust them to rerun elsewhere.)

- `scripts/trace.lua`:
  - inserts a coin at f120-f126 and presses START1 at f200-f206 (one-player game, default DIPs = 8 canisters);
  - logs all 256 data-RAM words every frame (`runs/<run>/ram.log`, not committed);
  - installs a write tap on the `data` space (`runs/<run>/writes.log`, not committed) and an `io` write tap (`events.log`);
  - takes snapshots on enemy word-A changes or at listed frames (`SNAPAT`).
  - Modes: `idle` = no input; `shoot` = turn toward the nearest enemy and fire; `ram` = turn toward it and thrust; `wander` = scripted pseudo-random turn/thrust.
  - **The `pc=` in the write logs is the store instruction address + 1** (CURPC after the store), mapped to region addresses.
- Analysis scripts (Python 3, read the run logs): `summarize.py`, `checks.py`, `events.py`, `analyze.py`, `canisters.py`, `extras.py`, `respawn.py`, `misc.py`, `exitrule.py`. `discover.lua` lists devices, address spaces and input fields.
- Determinism: replays of `idle1` (`idle_snap`, 1600 frames; `idle_states`, 1340 frames) produced byte-identical `ram.log` prefixes.

## Runs

| run | input | frames | outcome |
|---|---|---|---|
| idle1 | none | 12,000 | 3 canisters stolen by f1333, then one chaser orbits the idle P1 to the end; no game over |
| shoot1 | aim + fire | 12,000 | game over f4790, score 1190 |
| ram1 | aim + thrust | 12,000 | game over f3434 |
| wander1 | pseudo-random | 12,000 | game over f4462 |

## Files

- `excerpts/word_a_transitions.txt`: every enemy/player word-A change, with store PC+1, count and first frame, per run.
- `excerpts/idle1_state_changes.txt`, `excerpts/idle1_transition_context.txt`: idle1 state changes with positions and targets.
- `excerpts/idle1_records_f1010-1080.txt`: per-frame record view around the first chase, the fire line and the kill.
- `excerpts/idle_state_writes_with_E_page.txt`: state stores with RAM[$E0-$EF] at the moment of the store.
- `excerpts/exit_chase_kill_checks.txt`: chase triggers (E-page), maximum chase distance, all player kills by enemies (E8, \|d\|).
- `excerpts/exit_point_rule.txt`: exit point vs word B & 7 for all 189 pick-ups.
- `excerpts/laser_windows.txt`: FC bit 1 (Laser) sets by record/state/frame mod 16; the RAM[$E8] cycle; RAM[$A8] per frame.
- `excerpts/sound_bits.txt`: writers of FC bits 1 and 2; player shot count.
- `excerpts/rng_11B8_fit.txt`: RNG fit (1,328 pairs, unique solution) and period.
- `excerpts/p1_respawns.txt`: all 28 P1 deaths → respawns with timing and position.
- `excerpts/misc_checks.txt`: no chase switch for towing/at-canister enemies near the player; RAM words that change in the idle game; shot-slot liveness.
- `excerpts/mame_discover.txt`: MAME devices, address spaces (`program`, `data`, `io`) and ioport fields.
- `snaps/idle_states_montage.png`: idle1 f400 approach, f560 at canister, f700 towing, f790 exiting, f1029 chase + fire line, f1078 P1 explosion.
- `snaps/idle_beam_kill_montage.png`: idle1 f1024-f1036 (fire line f1029-f1032) and f1070-f1080 (kill at f1076).
- `snaps/idle_f1029_f1075_f1076.png`: full-resolution f1029, f1075, f1076.

Screen orientation in the snapshots: RAM y grows upward (the enemy at RAM (801, 89) is near the bottom).
