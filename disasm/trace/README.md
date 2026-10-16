# MAME trace evidence (passes 5 and 6)

Evidence for NOTES §18 (pass 5), NOTES §19 (pass 6) and the matching rows in GAME_RULES.md. Recorded 2026-10-16 with MAME 0.276 (Debian 13 `mame` package), driver `ripoff`, ROM set verified good (`mame -verifyroms ripoff`). No ROM data and no MAME binaries are stored here. The excerpts contain data-RAM values only.

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

## Pass 6 (NOTES §19)

Run with `scripts/trace2.lua` through `scripts/run2.sh`, which passes extra MAME arguments through, e.g. `-debug -debugger none` for `TRACE=` or `-wavwrite f.wav`. Environment variables:

- `TAG`, `FRAMES`;
- `PLAYERS=1|2`. A two-player game inserts a second coin at f150 and presses "2 Players Start" at f200;
- `P1MODE`/`P2MODE` = `idle|shoot|ram|wander|pvp` (`pvp`: turn to the other player and fire); `P1STOP`/`P2STOP` = the frame after which that player is idle; `P1THRUST="a-b"` = extra thrust frames;
- `TRACE="a-b,..."` = MAME debugger `trace` to `runs/<tag>/cpu_trace_<a>.log`. Trace PCs carry the CCPU P register in the top nibble;
- `FORCEQ=n:v` = the io write tap returns v for every OUT n; `FORCEFC=mask:value` = forces bits of every store to RAM[$FC] (sound tests only).

With the same input script, trace2.lua reproduced the pass-5 `ram.log` byte for byte (idle1 1,340 frames, fl_idle 1,560, rt1/rt2/rt4 vs shoot1 3,700/5,000/5,062).

Analysis scripts are in `scripts/p6/`. They read `runs/<tag>/` relative to `/workspace/mame`.

| run | input | frames | used for |
|---|---|---|---|
| tp_idle | 2P, both idle | 12,000 | chase target, respawn |
| tp_pvp1 / tp_pvp2 | 2P, P1 fires at P2 / P2 fires at P1 | 6,000 | shots vs players, kills, scoring |
| tp_shoot2 | 2P, P2 shoots enemies, P1 idle | 12,000 | per-player scoring |
| tp_wander | 2P, both scripted wander | 12,000 | tank-vs-tank (f2758), chase target |
| orb_nudge | 1P idle, thrust f3000-f3010 | 6,000 | orbit re-forms after a nudge |
| orb_wstop | 1P wander until f1500, then idle | 12,000 | orbit after play stops |
| orb_sstop | 1P shoot until f2500, then idle | 12,000 | no orbit; game over f3165 |
| sl_* (12 runs) | 1P shoot, one bit forced each, `-wavwrite` | 4,000 | sound polarity |
| fl_idle | idle1 input, TRACE 1554-1559 | 1,560 | fire-line branch per pass |
| rt1, rt2, rt4 | shoot1 input with TRACE ranges | 3,700-5,062 | indirect targets |
| rt3 | 2P, P1 shoot, P2 wander, TRACE ranges | 1,500 | indirect targets |

Files:

- `excerpts/p6_fireline_gating.txt`: every chase-window frame in 12 runs, classified; the f1554-f1559 debugger branch path.
- `excerpts/p6_beam_shapes.txt`: fire-line shape distances (derived numbers only; the image is built locally with `tools/ccpu/build_image.py` and not committed).
- `excerpts/p6_respawn_timing.txt`: death → free → respawn timing and respawn state, P1 and P2.
- `excerpts/p6_sound_polarity.txt`: forced-bit runs, `ram.log` equality, per-frame RMS by bit value.
- `excerpts/p6_two_player.txt`: shot passes near the other player, deaths, chase starts, kill and canister store PCs, score stores, the tank-vs-tank frame, the chase-target fit.
- `excerpts/p6_chase_target_fit.txt`: the distance-rule fit alone.
- `excerpts/p6_indirect_returns.txt`: observed targets of the 7 open indirect jumps/calls.
- `excerpts/p6_orbit_period.txt`: exact 720-frame RAM period of the orbit in three runs.

### Pass 7 (Milestone 1)

No new MAME runs. `scripts/p7/p7_checks.py` re-reads 9 existing RAM logs; `tools/remake/Replay` replays them through `src/RipOff.Core`.

- `excerpts/p7_checks.txt`: `$1204` rounding fit, at-canister turn target, player thrust cadence, wall bounce, tow offset, game-start timing, spawn-pass turn, and the full-frame replay result (NOTES §20).
- `tests/RipOff.Core.Tests/Data/*.chk`: RAM checkpoints every 100 frames from 7 runs (words `$00-$4F`, `$A8`, `$B0-$CF`, `$EC`, `$F1`, `$F2`), used by the unit tests. RAM only, no ROM data.

The WAV files, RAM logs and CPU traces are not committed (they are large, and the traces contain opcode text from the program ROM).
