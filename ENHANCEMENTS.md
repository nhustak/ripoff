# Enhancements

Nick's running list of enhancement ideas for the **Enhanced** mode.

- **Arcade** is the faithful 1980 Rip-Off. It stays as it is: `GameRules.Arcade` is frozen, and `ArcadeDeterminismTests` pins its checksums.
- **Enhanced** is where the game gets changed: give it legs, make it fun, not quarter-eating. Every tweak is a knob in `GameRules.Enhanced` (`src/RipOff.Core/GameRules.cs`). Its Arcade value is today's arcade behaviour.

Background and measurements: [disasm/NOTES.md §27](disasm/NOTES.md) and the ENHANCED section of the [README](README.md).

## Done (in Enhanced)

Shipped in PR #18 (first in the `enhanced-preview-1` pre-release) and released in **2026.10.17.1**.

| Knob (`GameRules`) | Arcade | Enhanced | Effect | Version |
|---|---|---|---|---|
| `EnemyAimErrorDeg` / `EnemyAimErrorPerLevel` / `EnemyAimErrorMin` | 0 (exact aim) | ±20° at level 1, 2° tighter per level, down to ±10° | Enemy shots leave at a random angle within the spread | 2026.10.17.1 |
| `EnemyHitRadius` | unused (always hits) | 12 units | A shot hits only if it passes within 12 units. About half the shots miss; close range still kills | 2026.10.17.1 |
| `EnemyMissCooldown` | 0 | 32 passes | After a miss, the enemy skips two fire windows | 2026.10.17.1 |
| `ChaseReactionPasses` | 0 | 19 passes (0.5 s) | A fresh chaser can't fire right away | 2026.10.17.1 |
| `ClearsPerTypeStep` | 1 | 2 | Two cleared waves per enemy-type step | 2026.10.17.1 |
| `FullWaveFromLevel` | 2 | 3 | Three-enemy waves start at level 3 (one-player) | 2026.10.17.1 |
| `EnemySpeedCapBase` | 0 (no cap) | 8 | Enemy speed is capped at 8 + level | 2026.10.17.1 |
| `TowSpeedPercent` | 100 | 75 | Towing enemies move at 75 % (min 2), leaving more time to rescue canisters | 2026.10.17.1 |

The result: games last about 2.3-2.6× longer with the strong and medium test bots, and enemy fire hits about half the time instead of always (NOTES §27.3).

## Ideas / backlog

Status: idea / planned / in progress / done. Source: Nick or bot-suggested.

| Idea | Status | Source |
|---|---|---|
| Visible miss: angle the laser line by the aim error so misses look like misses. Today the line is drawn along the enemy's heading even when the shot misses (NOTES §27.4.3) | idea | bot-suggested |
| Two-player Enhanced balance measurement. The knobs apply, but it was never measured; `ClearsPerTypeStep` also applies to the two-player 6-kill advance (NOTES §27.4.2) | idea | bot-suggested |
| Chase turn-speed knob (`ChaseTurnPercent`, still 100 in Enhanced). 75 % was tried, but chasers circled a still player until the 30-minute cap (stall) in 1-2 of 20 games. It needs a fix for the circling before it can be used (NOTES §27.2) | idea | bot-suggested |
| Human playtest feedback on the hit rate. The ~50 % hit rate was measured with bots that never thrust, and a moving human may see a different rate (NOTES §27.4.1) | idea | bot-suggested |

### Ships

| Idea | Status | Source |
|---|---|---|
| Ship select: each player chooses which ship they play, and both players can't pick the same ship (there are currently two ship shapes) | idea | Nick |
| Additional ships beyond the current two | idea | Nick |
| Ships have different abilities (examples below) | idea | Nick |

Ability examples from Nick for "Ships have different abilities":
- Speed
- Damage
- Armor. This means ships can take more than one hit, unlike the arcade, where one hit kills.
- Other abilities to be decided.
