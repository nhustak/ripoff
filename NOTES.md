# Rip-Off: Planning Notes and Open Questions (2026-10-16)

This file holds everything from today's calls that isn't already in `RIPOFF.md` or `STACK.md`. The project is **paused until the ROM is uploaded**.

## How the idea started (Nick, on the call)

- "We could start a brand new application, me and you right here going down the road."
- "I wanna write a game." The game is Rip-Off, an old vector arcade game.
- "I wanna start with a remake that we then put our own spin on." The original is the basis, "since we've got it." That turned out to mean the ROM Nick owns.
- On graphics: "Let's go ahead and use modern graphics... whatever we do we're gonna be able to change later. So let's start with mimicking the vector graphics. I wanna basically make a faithful recreation."
- **Later decision (supersedes the above):** use regular graphics with the original shapes, not an authentic vector-monitor look. Shaders are deferred. See `STACK.md`.

## Research behind the engine choice

These figures come from the GitHub API and SteamDB on 2026-10-16.

### MonoGame ([MonoGame/MonoGame](https://github.com/MonoGame/MonoGame))
- v3.8.6 went stable on 2026-10-07. It followed v3.8.6-preview.1 (2026-09-30) and preview.2 (2026-10-02). The release before that was v3.8.5.1 on 2026-08-14.
- The last commit was 2026-10-11: "DesktopGL: Fix stale scissor state after Clear (#9587)".
- About 14,500 stars, 768 open issues and PRs combined, and not archived. It had 55 commits from 15 authors in the last 90 days.
- **Caveats, from general knowledge and not verified on the call (UNCHECKED):**
  - There is no visual editor; everything is code-first.
  - The content pipeline tool (MGCB) is a common pain point.
  - The vector-monitor glow and bloom effect would need a custom shader.

### Alternatives looked at
- **FNA** ([FNA-XNA/FNA](https://github.com/FNA-XNA/FNA)) is very active, with monthly releases (26.10 came out 2026-10-01). It has about 3,000 stars and is the closest code-only alternative.
- **Godot** ([godotengine/godot](https://github.com/godotengine/godot)) has about 118,500 stars. 4.7.2 is stable and 4.8 is in beta. It's a full editor, and C# is one of its language options. It's the main alternative if we want an editor.
- **Stride** ([stride3d/stride](https://github.com/stride3d/stride)) was considered but not chosen. The reasons are in `STACK.md`. Its history:
  - It was first called Paradox, then Xenko, and has been Stride since April 2020. The rename was needed so it could join the .NET Foundation. The exact year Paradox became Xenko is UNCHECKED.
  - The first public alpha came out in September 2014.
  - Xenko 2.0 shipped under a commercial license in April 2017. In August 2018, with Xenko 3.0, Silicon Studio ended support and relicensed the engine and editor under MIT.
  - Later versions: 4.0 in February 2021, 4.1 in July 2022, 4.2 in February 2024, 4.3 in November 2025, and 4.4.0 stable on 2026-10-09.
  - Shipped games are scarce. The one notable commercial title is Distant Worlds 2 (March 2022, about 30,000 follows on SteamDB). Others include Children of the Galaxy (2017, the first Xenko game on Steam), 8089: The Next Action RPG, and FPS Infinite. Stride's community list also has a few Asteroids clones.

## Open questions
1. **ROM upload:** Decided 2026-10-16 by the Rip-Off bot. The ROM is never committed to this repo or any GitHub repo. When Nick uploads it, it stays on the Rip-Off bot's computer under `/workspace/ripoff-rom/`, outside any clone, as a disassembly reference only. `.gitignore` blocks common ROM file types as a safety net. Nick can override this.
2. **Input:** RawInput or SDL2. The Rip-Off bot will decide and flag the choice when input work starts.
3. **Line-drawing layer:** the approach, either MonoGame line primitives or textured quads for thickness. Not decided yet.
4. **Steam:** whether a Steamworks app ID or partner account exists yet. UNCHECKED.
5. **The "own spin" makeover:** what changes after the faithful remake. Not discussed yet.

## Repo housekeeping
- This repo is private, and its default branch is `main` (GitHub's starter README). Nick's other repos use `master`.
