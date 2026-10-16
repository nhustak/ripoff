# Rip-Off Remake: Project Notes

Notes from Nick's planning call on 2026-10-16.

**Status: paused until the ROM is uploaded.**

## What it is

- A C# remake of Rip-Off, the 1980 vector arcade game by Cinematronics.
- It starts as a faithful recreation. Our own spin on it comes later.
- Nick will write most of the code, working with the Rip-Off bot (his Grok Bot assistant for this repo).

## Repo ownership

- The Rip-Off bot owns this repo end to end (decided 2026-10-16).
- It may commit and push straight to `main`; PRs are optional. It may open and merge its own PRs.
- It never force-pushes or rewrites pushed history.

## Original ROM

- Nick has the original ROM and will upload it from his machine when he's home.
- The ROM is a disassembly reference only. It won't be run or shipped.
- It is committed to this repo under `rom/` (Nick, 2026-10-16).
- Things to pull from it:
  - logic tables
  - saucer and tank behavior
  - sound cues
  - scoring
- First-pass disassembly: `disasm/ripoff.lst`, with findings in `disasm/NOTES.md` and tools in `tools/ccpu/` (2026-10-16).

## Engine: MonoGame (C#)

- MonoGame is actively maintained as of October 2026. Figures checked against GitHub on 2026-10-16:
  - v3.8.6 was released October 7.
  - The last commit was October 11.
  - The repo has about 14,500 stars.
- Stride was considered but not chosen.

## Visuals

- Regular graphics that keep the original shapes, not an authentic vector-monitor look.
- Shaders such as glow, bloom, and CRT are deferred.
- We'll build a custom line-drawing layer, because MonoGame's built-in vector support is thin.

## Online co-op

- [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) handles lobbies, P2P, invites, and the overlay.
- [LiteNetLib](https://github.com/RevenantX/LiteNetLib) is the general networking option.
- We'll use everything from Steam that's useful.

## Input

- Use RawInput or SDL2's input layer instead of MonoGame's built-in input wherever it's the better fit.

## Library rule

- Prefer established libraries that the community uses most.
- When the options are split, the Rip-Off bot decides.
- When choosing between two solid options, the Rip-Off bot flags the choice instead of silently picking one.

See also `STACK.md` in this repo for the stack decisions.
