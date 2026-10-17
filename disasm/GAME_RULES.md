# Rip-Off (Cinematronics, 1980): game rules for the remake

Rules come from static reading of `disasm/ripoff_program.bin` and, from pass 5, from running the ROM in MAME 0.276 (NOTES §18, §19; evidence in `disasm/trace/`, cited as `trace:<file>` with frame numbers). Pass 7 checked them by replaying the traced runs through the C# simulation (`src/RipOff.Core`, NOTES §20). Citations are `lst:N` (lines of `disasm/ripoff.lst`), `NOTES §n` (`disasm/NOTES.md`) or `ccpu.cpp`/`cinemat.cpp` (MAME, see NOTES §1-2). Anything not proven is marked **UNCHECKED**. Treat those items as design choices for the remake, not as facts about the arcade game.

Units:

- **Coordinates:** 1024×768 field; x 0..`$3FF`, y 0..`$2FF`.
- **Angles:** degrees 0..359.
- **Heading convention:** a tank at angle θ moves by (speed·sin θ, −speed·cos θ) per update (lst:2256-2287, NOTES §11.1).
- **Time:** "pass" means one main-loop pass = one video frame at 38 Hz (26.316 ms) in MAME (RAM[$A8] +1 per frame, NOTES §18.8).

## 1. Cabinet setup

| Rule | Evidence |
|---|---|
| The "Lives" DIP is the **canister count**: DIP 0x03/0x01/0x02/0x00 → 8/4/16/12 canisters | lst:2868-2916, cinemat.cpp:720-724, NOTES §3 |
| Canister start positions: the first N (x,y) byte pairs of ROM `$0F9A`, each ×8, stored at RAM `$B0`, drawn at (x/2−13, y/2−46): a cluster in the centre. In field units, in order: (512,360), (512,408), (536,384), (488,384), (544,352), (480,352), (544,416), (480,416), (568,384), (512,328), (456,384), (512,440), (456,328), (456,440), (568,328), (568,440). Unused slots hold 1 in both words (gone). | lst:2919-2939, lst:3685-3690, lst:4613-4624, NOTES §10.3, §20.2 |
| The Scores DIP selects individual or combined score display | lst:1112, lst:1158-1198 |
| The Demo Sounds DIP is read at `$0120` | lst:275 |

## 2. Credits and starting a game

| Rule | Evidence |
|---|---|
| Credits are in RAM[$F3] | lst:236-238 |
| START1 begins a **one-player** game and costs 1 credit | lst:202-210, lst:236-238 |
| START2 begins a **two-player** (cooperative) game, needs ≥ 2 credits and costs 2 | lst:215-230, lst:223-225, lst:236-238 |
| Game start: both scores = 0, enemy type = 1, level = 1 | lst:246-249, lst:253-255 |
| P1 starts at (1024, 320) with angle 270 (moving left); P2 starts at (0, 448) with angle 90 (moving right) | lst:899-932, NOTES §10.1 |
| The game ends when **every canister is gone**. There is no player-lives counter in the paths read. The test runs right after the frame wait, before the next pass's wave check (pass 7). | lst:1446-1481, lst:94-99, NOTES §20.1 (g) |
| After the game, attract mode shows credits and the high scores | lst:1205-1211, lst:1356-1443 |

## 3. Player controls and movement

| Rule | Evidence |
|---|---|
| Left/right rotate; the turn step rises by 1 on even passes up to 8°/pass while held. Left adds to the angle, right subtracts. | lst:2141-2218, NOTES §11.1, §20.1 (b) |
| **BUTTON2 = thrust**: +1 speed up to 10; released, −1 down to 0 (coasting). Speed changes only on every 4th pass (RAM[$A8] bits 0-1 clear; corrected in pass 7, 1,663 of 1,663 traced changes). | lst:2219-2255, `trace:excerpts/p7_checks.txt` (c), NOTES §20.1 (b) |
| Position += speed along the heading every pass | lst:2256-2287 |
| **Walls:** a move off the field clamps x to 0/`$3FF` (y to 0/`$2FF`) and sets the heading from a table by the quarter q of (heading − 90): x walls 186/174/6/354, y walls 84/276/264/96 | lst:2288-2334, lst:3689-3690, `trace:excerpts/p7_checks.txt` (d), NOTES §20.3 |
| **BUTTON1 = fire**, edge-triggered (one shot per press) | lst:2569-2582 |
| Each player has **4 shot slots**; no free slot means no shot | lst:2584-2602 |
| Shot velocity = (speed + 15) along the tank's heading, from the tank position | lst:2606-2651 |
| Shots fly straight and vanish when off-field (x ≥ 1024 or y ≥ 768, negatives included) | lst:3432-3466 |
| Shots hit **only enemies** (no friendly fire), box radius 20 | lst:3482-3484, lst:3587-3591 |

## 4. Waves and difficulty

| Rule | Evidence |
|---|---|
| Up to 3 enemies per wave (records `$00`, `$10`, `$20`) | lst:741-766, NOTES §14.1 |
| **One-player game, level 1:** only 2 enemies spawn; the third counts as already killed, and the theft counter starts at 1 | lst:843-861 |
| The next wave starts when all enemies are gone, after a delay of about 50 passes (counter +2 per pass up to `$65`) | lst:483-498 |
| End of wave, with K = kills this wave: one-player, K = 3 → **advance**; any game, K ≥ 6 → **advance**; K = 4 or 5 → K := 3 (carried over); K < 3 → K := 0; two-player, K = 3 → kept | lst:500-573 |
| Advance: enemy type +1. After type 6 comes type 1, the level goes +1, and the BONUS LEVEL screen is shown. | lst:529-547 |
| Step-back: if **3 or more canisters were stolen** in the wave and the type is > 1, the type goes −1. The theft counter then resets. | lst:558-571 |
| The 6 enemy types each have their own sprite | NOTES §10.2 |

## 5. Enemy spawning and parameters

| Rule | Evidence |
|---|---|
| All 3 enemies spawn at one point: entry k of an 8-entry table (ROM `$0F82`, ×8), with heading 2·ROM[`$0F92`+k] degrees. The points sit on the top or bottom edge or the corners and always face into the field (table in NOTES §14.2). | lst:786-842 |
| PRNG step (`$11B8`): x ← ((x<<1) & `$FFF`) \| (bit10(x) XOR bit8(x)); seed `$387`, period 2047. Two steps per use. | lst:4111-4149, `trace:excerpts/rng_11B8_fit.txt`, NOTES §18.6 |
| k comes from a pseudo-random word (record `$00` word B), and so does the hop offset in §6 (PRNG: row below) | lst:786-794, lst:1911-2069 |
| Max speed v = type + ⌊type/2⌋ + level + 1 (type 1, level 1 → 3; type 6, level 1 → 11) | lst:666-674 |
| Speed starts at v. Current speed recovers by 1 toward v every 4th pass (it can be lowered by word F, min 2: lst:1813-1830) | lst:1842-1854, lst:4587-4590 |
| Turn step = ⌊v/2⌋ + 1 degrees per pass, snapping onto the bearing when closer than one step | lst:675-677, NOTES §14.3 |
| Enemies move like the player (same heading formula) | lst:1795-1841 |

## 6. Enemy behaviour (what is decoded)

| Rule | Evidence |
|---|---|
| Enemy states (word A): `$01C` approach, `$014` at canister, `$01E` towing, `$018` chase, `$001` free; `A & $F` = exploding | NOTES §18.2, `trace:excerpts/word_a_transitions.txt` |
| Each enemy picks a canister; if its canister is gone, it takes the first remaining one | lst:1528-1573 |
| **Approach:** it travels in hops. On reaching within 72 (`$48`) of its current hop point, it picks a new point at canister position + a random offset. The offset shrinks with each hop. The point is clamped to x 0..`$400`, y 0..`$300`. | lst:1911-2069, lst:2037-2068, NOTES §20.1 (c) |
| **Separation:** an enemy within 96 (`$60`) of another enemy steers away from it first. Literally (pass 7): it compares properly only with lower-index enemies (exploding ones count); after its own record the walk is one word off and compares with pseudo-records (x = word F of record m, y = x of record m+1, skipped when bit 0 of word 9 of record m+1 is set). The remake reproduces this walk. | lst:1611-1675, lst:4151-4186, NOTES §20.1 (e) |
| **Chase mode** (word-A bit 3 set, bit 2 clear): steer at P1 in a one-player game. In a two-player game, steer at P1 if \|dx to P1\| < max(\|dx\|,\|dy\|) to P2, else at P2 (ties → P2). The arcade code measures P1 by x only (`SUB $0` at `$074F` where P2's branch has `SUB $A`). This matched all 18,771 traced frames; a symmetric max(\|dx\|,\|dy\|) rule failed 252. Dead players are not excluded. The steer point is the chosen player's position at the previous frame. (Corrected in pass 6; what gates the per-frame refresh is **UNCHECKED**, 401 frames kept an older point.) | lst:1679-1774, `trace:excerpts/p6_two_player.txt` (e), NOTES §19.4 |
| When a player tank is destroyed, **all** enemies switch to the bit-2 mode: chasers go back to approaching (`$018` → `$01C`) | lst:2457-2480, lst:4065-4107, idle_snap f1076 |
| **Pick-up:** an approaching (or chasing) enemy that comes within 20 (box, inclusive) of **any** live canister (canister table coords / 2) stops (speed 0) and takes that canister (`$014`). Its exit point becomes spawn-table entry (word B & 7), not its own spawn point. | lst:2666-2798, `trace:excerpts/exit_point_rule.txt` (189/189), NOTES §18.3 |
| **Turn, then tow:** it turns in place toward the bearing of (own position − canister/2), i.e. away from the canister (corrected in pass 7; not the exit point). When the heading snaps onto it (within one turn step), the tow (`$01E`) starts on the next pass and it accelerates back to its max speed toward the exit point. The canister is drawn 20 behind it: own + R(heading)·(0, `$20`)·word4/128. | lst:1587-1609, lst:3858-3877, lst:3182-3228, `trace:excerpts/p7_checks.txt` (b), (e), NOTES §20.1 (a), §20.3 |
| **Canister contact while towing:** a towing enemy (or a player) within 20 of a free canister does not take it; word B steps, word 4 += B & `$F`, heading += (B & `$F`) − 8 | lst:2804-2843, lst:2335-2349, NOTES §20.3 |
| **Theft:** an enemy towing a canister (word-A bit 1) that leaves the field removes the canister for good and frees its record | lst:1856-1904, store `$0810` (y word bit 0) |
| **Dropped canister:** if a towing enemy is destroyed, the canister stays where it was dropped and can be picked up again from there | lst:3117-3121, shoot1 f1818-f4789, NOTES §18.3 |
| **Chase starts** when a live player comes within 256 (box, inclusive) of an enemy that is approaching (`$01C`). It is tested in each player's pass, so P2 triggers it too (tp_pvp1 f363). Enemies that are towing or at a canister never switch to chase. Chase does **not** end on distance (seen 770 away); it ended only when that enemy died, picked up a canister it touched (`$018` → `$014`), or a player was destroyed. | lst:2519-2545, NOTES §18.3, `trace:excerpts/misc_checks.txt` |
| **Enemy fire (no projectile object):** each enemy record has a 4-frame fire window every 16 frames (record `$00`: frames 0-3 mod 16, `$10`: 4-7, `$20`: 8-11). A chasing enemy in its window destroys the player when its heading snaps onto the bearing to the player and the player is within 150 of it (strict: \|dx\| < 150 and \|dy\| < 150; corrected in pass 7) and the enemy itself is on the field. All 13 kills by enemies in the trace match this. | lst:3972-4109, lst:3980-4036, NOTES §20.1 (d), `trace:excerpts/exit_chase_kill_checks.txt`, NOTES §18.4 |
| **Fire line:** in its window a chasing enemy is drawn with a line along its heading, with the Laser sound bit set. The line always reaches 145 from the enemy; its near end steps 25 → 55 → 85 → 115 over the 4 frames (shapes `$1F89`/`$1F92`/`$1F9B`/`$1FA4` × scale 80/128). It is drawn in **every** window frame, except when the enemy is off the field at draw time (the whole draw is skipped), when a player was destroyed in that pass (the enemy is already back to `$01C`), or when the enemy picked up a canister in that pass. This accounts for all 11,973 traced window frames, with none left over. **The line is cosmetic.** Kills come only from the fire test (next row), only players can be victims, and kills also happen without a drawn line. | lst:3125-3145, lst:3292-3338, lst:3169-3175, `trace:excerpts/p6_fireline_gating.txt`, `p6_beam_shapes.txt`, NOTES §19.1 |
| A single chaser can orbit a motionless player indefinitely without killing it. This is real game behaviour: the whole RAM state repeats every 720 frames (except the pass counter `$A8`) in idle1 (f1400-f12000), after a thrust nudge (orb_nudge f3100-f6000) and after wandering then stopping (orb_wstop f3100-f12000). It is not guaranteed: with more enemies the idle player is still killed (orb_wstop f2081, f2821), or the canisters run out (orb_sstop, game over f3165). Any thrust breaks a loop, but a new one can form. | `trace:excerpts/p6_orbit_period.txt`, NOTES §19.6 |

## 7. Collisions

| Rule | Evidence |
|---|---|
| All tests are boxes: hit when \|dx\| ≤ r and \|dy\| ≤ r (inclusive; corrected in pass 5) | lst:4172-4180, NOTES §18.1 |
| Player shot vs enemy: r = 20. The enemy explodes, the shot is freed, and the shooter scores. | lst:3482-3549 |
| Player tank vs any other tank (enemies or the other player): r = 24. **Both** explode; ramming an enemy scores, ramming the other player does not (trace: tp_wander f2758, P1 and P2 both destroyed, no score store). | lst:2335-2494, lst:2423-2427, `trace:excerpts/p6_two_player.txt` (d) |
| **Player shots never hit the other player** (the shot test covers enemy records only). In tp_pvp1/tp_pvp2, shots passed within 20 of the other player in 1,556 frames with no effect. | lst:3587-3591, `trace:excerpts/p6_two_player.txt` (a), NOTES §19.4 |
| Besides tank contact (row above, tested in the player's pass, both explode), an enemy destroys a player through its fire test (§6 "Enemy fire"). Player deaths in the trace were all one of these two (store `$118A` or `$0A86`). | lst:3972-4109, NOTES §18.4 |
| **Player respawn:** the explosion counts word 4 down from `$300` by `$20` per pass (24 passes; frame-end logs show 23 because the first falls in the kill pass), then the record is free. While free, word 3 counts passes. At 25 (`$19`) the player respawns with speed 5 and turn step 0: **P1 at (1024, 320) angle 270, P2 at (0, 448) angle 90**. P2 is only handled in a two-player game, and nothing happens in attract mode. There are unlimited respawns. Trace: all P1 and P2 respawns in 7 runs took 23 + 25 frames (one 24-frame case: a tank-vs-tank kill in P2's pass). | lst:862-932 (`$03B2-$03BF`, `$03C6-$03CF`, `$03E2-$03EC`), lst:3093-3106, `trace:excerpts/p6_respawn_timing.txt`, NOTES §19.2 |
| Enemy explosion lasts 16 frames after a shot kill (scale `$200`) and 24 after ramming (`$300`), −`$20` per pass, then the record is free | lst:3093-3106, shoot1 f424-f440, ram1 f345-f369 |
| A kill also stores the cause in the victim's word C: 0 shot, 1 ram (both records), 2 enemy fire | lst:3526-3549, lst:2393-2421, lst:4065-4073, NOTES §20.1 (f) |

## 8. Scoring

| Rule | Evidence |
|---|---|
| **Points per enemy = 10 × (type + level − 1)**: level 1 pays 10..60 for types 1..6, and each level adds 10 | lst:3550-3573, lst:2433-2455 |
| Shot kills score for the shot's owner; ramming kills score for the rammer (trace: shot-kill stores at `$0F11` to F1 for P1 / F2 for P2, ram-kill stores at `$0A62` in that player's pass) | lst:3556-3573, lst:2423-2455, `trace:excerpts/p6_two_player.txt` (c) |
| Scores are stored in tens (thousands + units words) and shown with a trailing 0 | lst:1052-1080, lst:4820-4832 |
| Individual display: P2 at x=416 (left), P1 at x=608 (right), y=374. Combined: one total at (512, 460). | lst:1112-1198 |
| BONUS LEVEL screen: "BONUS LEVEL + (L−1)0", the per-kill increase for new level L | lst:1252-1353 |
| High scores are kept separately for two-player team totals and for one-player games (P1 only) | lst:107-188 |

## 9. Sound cues

| Rule | Evidence |
|---|---|
| Background drone: command bits 7-5 come from a value that rises by one step per stolen canister. The step is chosen so the full game sweeps the whole range: 8 canisters × 31, 4 × 63, 16 × 15, 12 × 21. | lst:292-309, lst:2892-2898, lst:1885-1888 |
| Torpedo (fire) sound for 5 passes on each player shot. **Sound plays while Q3 = 0** (Q3 → IC18 74LS393 CLR; forcing Q3 = 1 removed it) | lst:2606-2609, lst:343-353, nl_ripoff.cpp:625, `trace:excerpts/p6_sound_polarity.txt` |
| Explosion pulse on every destruction (shot kill, ramming, player killed). **Triggered by the falling edge of Q7** (Q7 → IC4 LM555 trigger; Q7 written 0 then 1; forcing Q7 = 1 removed the explosions) | NOTES §15, nl_ripoff.cpp:342, `trace:excerpts/p6_sound_polarity.txt` |
| Beep toggling during the BONUS screen. **Beep sounds while command bit 3 = 0** | lst:317-338, nl_ripoff.cpp:489-497, `trace:excerpts/p6_sound_polarity.txt` |
| Attract mode sets command bit 4. **Bit 4 = 1 silences the background drone** (bit 4 → IC22 74LS393 CLR); it is also 1 on the BONUS screen | lst:313-315, nl_ripoff.cpp:403, `trace:excerpts/p6_sound_polarity.txt` |
| **Laser** (Q4) cue = enemy fire line: command bit 1 is set only while a chasing enemy is in its fire window and its line is drawn. **Sound plays while Q4 = 0** (Q4 → IC18 CLR; forcing Q4 = 0 added it) | lst:3338, lst:939, nl_ripoff.cpp:563, `trace:excerpts/p6_sound_polarity.txt`, NOTES §19.3 |
| **Motor 1** (command bit 2) is set when a wave has ended and cleared when the next wave spawns. It does not follow player thrust. **It sounds while bit 2 = 0**, so the motor drone plays during waves and stops in the gaps between them (bit 2 → PNP pair → IC12 CA3080 bias; forcing 0 in the gaps added sound, forcing 1 during waves removed it) | lst:777-782, lst:576-581, nl_ripoff.cpp:523-531, `trace:excerpts/p6_sound_polarity.txt`, NOTES §19.3 |
| **Background pitch:** bits 7-5 = v (bit 7 the most significant) set both the tone and its pulsing rate; a higher v is a higher, faster drone. MAME output: v = 0..6 → 161, 182, 208, 246, 296, 378, 519 Hz, pulsing at about 5-10 Hz. Bit 7 → IC9.2 (R25/R26, 2k), bit 6 → IC9.5 (4k), bit 5 → IC9.6 (8k) into the IC19 summer; v = 7 never occurred in the logs | lst:354-384 (shift order), nl_ripoff.cpp:404-454, `trace:excerpts/p8_sound.txt` (5), NOTES §21.1/§21.3 |
| **Beep phase:** on the BONUS screen bit 3 = 0 when RAM[$A8] bit 1 = 0 at the sound routine (start of pass, before the increment): 2 passes on, 2 off. Not on the pass that sets BONUS | lst:317-338, `trace:excerpts/p8_sound.txt` (7), NOTES §21.4 |
| **Command byte timing:** the byte is built and shifted out at the start of the pass, so a bit the wave code changes later in the pass (bit 2, motor) reaches the board one pass later; checked frame by frame against `$FC` in 5 runs | lst:283-384, NOTES §21.4 |
