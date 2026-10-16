# Stack Decisions: Rip-Off Remake

Decisions from Nick's planning call on 2026-10-16. The game starts as a faithful remake of the vector arcade game Rip-Off. A makeover with our own spin comes later.

Nick will write most of the code going forward, working with the Rip-Off bot (his Grok Bot assistant for this repo).

## 1. Engine: MonoGame (C#)

**Chosen: [MonoGame](https://github.com/MonoGame/MonoGame).**

Why:
- It's C#, the same language as Nick's other projects (e.g. yaas-mcp).
- It's actively maintained. Figures checked against GitHub on 2026-10-16:
  - The latest stable release is v3.8.6, released 2026-10-07.
  - The last commit was 2026-10-11.
  - The repo has about 14,500 stars.
  - In the last 90 days it had 55 commits from 15 authors.
- It suits a 2D vector-style game.

**Considered: [Stride](https://github.com/stride3d/stride).** Stride was formerly called Xenko, and before that Paradox. It's about 12 years old (first public alpha in September 2014), has been MIT-licensed since 2018, and v4.4.0 went stable on 2026-10-09.
- Its appeal was the richer graphics suite.
- It was rejected for now. The makeover plan uses regular graphics with the same shapes, not authentic vector-monitor effects.
- Shaders can be added to MonoGame later without a rewrite.

## 2. Vector rendering

- Build a custom line-drawing layer on top of MonoGame, because MonoGame's built-in vector support is thin.
- Glow, bloom, and CRT shaders are deferred.

## 3. Networking and co-op

- Use [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) for Steam integration: lobbies, P2P transport, invites, and the overlay.
- Use [LiteNetLib](https://github.com/RevenantX/LiteNetLib) as the general networking option.
- Use everything from Steam that's useful.

## 4. Input

- Prefer RawInput or SDL2's input layer for clean controller and keyboard handling.
- Use either one over MonoGame's built-in input wherever the Rip-Off bot judges it the better fit.
- Which of the two to use is not decided yet. It will be made and flagged when input work starts.

## 5. General rule for libraries

- Prefer established libraries that the community uses most.
- When the options are split, the Rip-Off bot decides.
- When choosing between two solid options, the Rip-Off bot flags the choice instead of silently picking one.
