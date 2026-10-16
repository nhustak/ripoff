# Rip-Off (Cinematronics, 1980): game rules for the remake

Every rule here comes from static reading of `disasm/ripoff_program.bin`. The ROM was never run. Citations are `lst:N` (lines of `disasm/ripoff.lst`), `NOTES §n` (`disasm/NOTES.md`) or `ccpu.cpp`/`cinemat.cpp` (MAME, see NOTES §1-2). Anything not proven is marked **UNCHECKED**. Treat those items as design choices for the remake, not as facts about the arcade game.

Units:

- **Coordinates:** 1024×768 field; x 0..`$3FF`, y 0..`$2FF`.
- **Angles:** degrees 0..359.
- **Heading convention:** a tank at angle θ moves by (speed·sin θ, −speed·cos θ) per update (lst:2256-2287, NOTES §11.1).
- **Time:** "pass" means one main-loop pass. That it equals one video frame is **UNCHECKED** (NOTES §17).

## 1. Cabinet setup

| Rule | Evidence |
|---|---|
| The "Lives" DIP is the **canister count**: DIP 0x03/0x01/0x02/0x00 → 8/4/16/12 canisters | lst:2868-2916, cinemat.cpp:720-724, NOTES §3 |
| Canister start positions: the first N (x,y) byte pairs of ROM `$0F9A`, each ×8, stored at RAM `$B0`, drawn at (x/2−13, y/2−46): a cluster in the centre | lst:2919-2939, lst:4613-4624, NOTES §10.3 |
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
| The game ends when **every canister is gone**. There is no player-lives counter in the paths read. | lst:1460-1481, lst:94-99 |
| After the game, attract mode shows credits and the high scores | lst:1205-1211, lst:1356-1443 |

## 3. Player controls and movement

| Rule | Evidence |
|---|---|
| Left/right rotate; the turn step ramps up to 8°/pass while held. Left adds to the angle, right subtracts. | lst:2157-2195, NOTES §11.1 |
| **BUTTON2 = thrust**: +1 speed per pass up to 10; released, −1 per pass down to 0 (coasting) | lst:2226-2254 |
| Position += speed along the heading every pass | lst:2256-2287 |
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
| k comes from a pseudo-random word (record `$00` word B), and so does the hop offset in §6. The PRNG function is **UNCHECKED**. | lst:786-794, lst:1911-2069 |
| Max speed v = type + ⌊type/2⌋ + level + 1 (type 1, level 1 → 3; type 6, level 1 → 11) | lst:666-674 |
| Speed starts at v. Current speed recovers by 1 toward v every 4th pass (it can be lowered by word F, min 2: lst:1813-1830) | lst:1842-1854, lst:4587-4590 |
| Turn step = ⌊v/2⌋ + 1 degrees per pass, snapping onto the bearing when closer than one step | lst:675-677, NOTES §14.3 |
| Enemies move like the player (same heading formula) | lst:1795-1841 |

## 6. Enemy behaviour (what is decoded)

| Rule | Evidence |
|---|---|
| Each enemy picks a canister; if its canister is gone, it takes the first remaining one | lst:1528-1573 |
| **Approach:** it travels in hops. On reaching within 72 (`$48`) of its current hop point, it picks a new point at canister position + a random offset. The offset shrinks with each hop. | lst:1911-2069 |
| **Separation:** an enemy within 96 (`$60`) of another live enemy steers away from it first | lst:1611-1675, lst:4151-4186 |
| **Chase mode** (word-A bit 3 set, bit 2 clear): steer at P1 (one-player game) or at the nearer player by max(\|dx\|,\|dy\|) (two-player game) | lst:1686-1774 |
| When a player tank is destroyed, **all** enemies switch to the bit-2 mode (steer to their stored point) | lst:2457-2480 |
| **Theft:** an enemy towing a canister (word-A bit 1) that leaves the field removes the canister for good and frees its record | lst:1856-1904 |
| Not decoded (**UNCHECKED**): the exact pick-up condition (how bit 1 gets set), when chase mode starts, and whether enemies fire. No enemy projectile was found in the code read. | NOTES §16 |

## 7. Collisions

| Rule | Evidence |
|---|---|
| All tests are boxes: hit when \|dx\| < r and \|dy\| < r | lst:4151-4186 |
| Player shot vs enemy: r = 20. The enemy explodes, the shot is freed, and the shooter scores. | lst:3482-3549 |
| Player tank vs any other tank (enemies or the other player): r = 24. **Both** explode; ramming an enemy scores, ramming the other player does not. | lst:2335-2494, lst:2423-2427 |
| An enemy reaching a player's position destroys the player (trigger thresholds **UNCHECKED**) | NOTES §14.5 |
| Player respawn delay/rules: **UNCHECKED** (the player spawn path is at lst:863-932) | lst:863-868 |

## 8. Scoring

| Rule | Evidence |
|---|---|
| **Points per enemy = 10 × (type + level − 1)**: level 1 pays 10..60 for types 1..6, and each level adds 10 | lst:3550-3573, lst:2433-2455 |
| Shot kills score for the shot's owner; ramming kills score for the rammer | lst:3556-3573, lst:2423-2455 |
| Scores are stored in tens (thousands + units words) and shown with a trailing 0 | lst:1052-1080, lst:4820-4832 |
| Individual display: P2 at x=416 (left), P1 at x=608 (right), y=374. Combined: one total at (512, 460). | lst:1112-1198 |
| BONUS LEVEL screen: "BONUS LEVEL + (L−1)0", the per-kill increase for new level L | lst:1252-1353 |
| High scores are kept separately for two-player team totals and for one-player games (P1 only) | lst:107-188 |

## 9. Sound cues

| Rule | Evidence |
|---|---|
| Background drone: command bits 7-5 come from a value that rises by one step per stolen canister. The step is chosen so the full game sweeps the whole range: 8 canisters × 31, 4 × 63, 16 × 15, 12 × 21. | lst:292-309, lst:2892-2898, lst:1885-1888 |
| Torpedo (fire) sound for 5 passes on each player shot | lst:2606-2609, lst:343-353 |
| Explosion pulse on every destruction (shot kill, ramming, player killed) | NOTES §15 |
| Beep toggling during the BONUS screen | lst:317-338 |
| Attract mode sets command bit 4 | lst:313-315 |
| Laser and Motor cue triggers: **UNCHECKED** | NOTES §15 |
