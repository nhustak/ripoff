# Enhancements

Nick's running list of enhancement ideas for the **Enhanced** mode.

- **Arcade** is the faithful 1980 Rip-Off. It stays as it is: `GameRules.Arcade` is frozen, and `ArcadeDeterminismTests` pins its checksums.
- **Enhanced** is where the game gets changed: give it legs, make it fun, not quarter-eating. Every tweak is a knob in `GameRules.Enhanced` (`src/RipOff.Core/GameRules.cs`). Its Arcade value is today's arcade behaviour.

Background and measurements: [disasm/NOTES.md §27](disasm/NOTES.md) and the ENHANCED section of the [README](README.md).

## Done (in Enhanced)

The first eight knobs shipped in PR #18 (first in the `enhanced-preview-1` pre-release) and were released in **2026.10.17.1**. Warp-in and the options page came in **2026.10.17.3**; random warp-in and warp kills in **2026.10.17.5**.

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
| `WarpInPasses` / `WarpInSpeed` | 0 (drive in from the edge at speed 5) | 30 passes (0.8 s), speed 0 | Players warp in at their spawn point with a vector effect. They can't be hit, collide or fire until they materialize (NOTES §28) | 2026.10.17.3 |
| `WarpLocation` | Fixed | Random | Warp in at a random spot picked when the warp starts: at least 160 units from the edges, clear of the canisters, at least 240 from the other player. Enemies aren't avoided (NOTES §29). Toggle RANDOM WARP-IN | 2026.10.17.5 |
| `WarpKills` | false | true | Materializing on an enemy destroys it: normal score, counts as a kill, a towed canister drops, the player survives. Materialize moment only (NOTES §29). Toggle WARP KILLS | 2026.10.17.5 |
| ENHANCED OPTIONS page | (not applicable) | WARP-IN, ENEMY AIM ERROR, SLOWER RAMP, SLOWER TOWING (2026.10.17.3), RANDOM WARP-IN, WARP KILLS (2026.10.17.5), all ON by default | Each Enhanced variation can be switched off (off = Arcade values for its knobs). Saved in settings.json; online, the host's options are used (protocol v4) | 2026.10.17.3 |

The result: games last about 2.3-2.6× longer with the strong and medium test bots, and enemy fire hits about half the time instead of always (NOTES §27.3).

## Ideas / backlog

Status: idea / planned / in progress / done. Source: Nick or bot-suggested.

| Idea | Status | Source |
|---|---|---|
| Visible miss: angle the laser line by the aim error so misses look like misses. Today the line is drawn along the enemy's heading even when the shot misses (NOTES §27.4.3) | idea | bot-suggested |
| Two-player Enhanced balance measurement. The knobs apply, but it was never measured; `ClearsPerTypeStep` also applies to the two-player 6-kill advance (NOTES §27.4.2) | idea | bot-suggested |
| Chase turn-speed knob (`ChaseTurnPercent`, still 100 in Enhanced). 75 % was tried, but chasers circled a still player until the 30-minute cap (stall) in 1-2 of 20 games. It needs a fix for the circling before it can be used (NOTES §27.2) | idea | bot-suggested |
| Human playtest feedback on the hit rate. The ~50 % hit rate was measured with bots that never thrust, and a moving human may see a different rate (NOTES §27.4.1) | idea | bot-suggested |

### Respawn / warp-in

| Idea | Status | Source |
|---|---|---|
| Players "warp" in instead of coming in from off screen, to allow more options | done (2026.10.17.3, toggle WARP-IN) | Nick |
| Warp-in delay depends on how you were killed and on the time since your last death (for example, a collision death takes longer to warp in from). Hook ready: `GameRules.DelayRule` / `Simulation.RespawnDelayFor` | idea | Nick |
| Random warp-in location after death | done (2026.10.17.5, toggle RANDOM WARP-IN) | Nick |
| Warping in on top of an enemy kills it (normal score, counts as a kill, player survives) | done (2026.10.17.5, toggle WARP KILLS) | Nick |

### Feedback

| Idea | Status | Source |
|---|---|---|
| Show a quick little "SAVED!!" message when you shoot an enemy that is carting off a canister | idea | Nick |

### Visuals

| Idea | Status | Source |
|---|---|---|
| Colorize | idea | Nick |

### Ships

| Idea | Status | Source |
|---|---|---|
| Ship select: each player chooses which ship they play, and both players can't pick the same ship (there are currently two ship shapes) | idea | Nick |
| Additional ships beyond the current two | idea | Nick |
| Ships have different abilities (examples below) | idea | Nick |
| Ship type: Scout. Fast; one hit kills it (an example of different abilities) | idea | Nick |
| Ship type: Cruiser. Not as fast; it takes two hits to kill (an example of different abilities, with armor) | idea | Nick |

Ability examples from Nick for "Ships have different abilities":
- Speed
- Damage
- Armor. This means ships can take more than one hit, unlike the arcade, where one hit kills.
- Other abilities to be decided.

### Ship add-ons

| Idea | Status | Source |
|---|---|---|
| Tug: the ship can move a canister, but its speed is cut in half and it can't fire while towing | idea | Nick |
| Drop a mine | idea | Nick |
| Drop a turret | idea | Nick |

### Enemy variations

| Idea | Status | Source |
|---|---|---|
| Tank: a slower enemy that takes two hits to kill | idea | Nick |
| Hunter: pursues the players instead of going for a canister. Arcade enemies already switch to chasing when a player comes close, so Hunter means an enemy that targets only the players | idea | Nick |
