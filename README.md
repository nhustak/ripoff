# ripoff
C# remake of the 1980 Cinematronics arcade game Rip-Off

## Building and running (Milestone 1)

The remake is three .NET projects in `RipOff.sln`:

| Project | What it is |
|---|---|
| `src/RipOff.Core` | Pure .NET 10 library: the whole game simulation, one deterministic pass per 38 Hz frame, from `disasm/GAME_RULES.md` (code comments cite the rules and `ripoff.lst` lines). Also the shape data (`Shapes/ShapeData.g.cs`, generated from our decoded docs by `tools/remake/gen_shapes.py`) and the scene builder that turns the state into lines. |
| `src/RipOff.Game` | MonoGame 3.8.6 DesktopGL window: fixed 38 Hz step, keyboard input, custom line renderer. Sound is stubbed (TODOs). |
| `tests/RipOff.Core.Tests` | xUnit tests: rule checks, replays of the scripted MAME runs against arcade RAM checkpoints, and a headless render-to-PNG check. |

The game never loads or ships the ROM. Shapes and tables are re-expressed from the docs in `disasm/`.

### Windows

1. Install the **.NET 10 SDK** (x64) from <https://dotnet.microsoft.com/download/dotnet/10.0>, or run `winget install Microsoft.DotNet.SDK.10` (package id from Microsoft Learn, "Install .NET on Windows").
2. Check it: `dotnet --version` should print 10.0.x.
3. From the repo root:

   ```powershell
   dotnet build RipOff.sln
   dotnet test RipOff.sln
   dotnet run --project src/RipOff.Game
   ```

   The first build downloads the NuGet packages (MonoGame.Framework.DesktopGL 3.8.6, xunit). No MonoGame content tools are needed: there is no content pipeline.
4. Optional: `dotnet run --project src/RipOff.Game -- --shot shot.png 400` plays 400 scripted passes, saves one frame to `shot.png` through the real MonoGame renderer, and exits. UNCHECKED: this path has not been run yet (the Linux build box has no OpenGL device; `dotnet test` covers rendering with a software rasteriser instead).

The same commands work on Linux and macOS with the .NET 10 SDK installed.

### Keys

| Action | Player 1 | Player 2 |
|---|---|---|
| Turn left / right | ← / → | A / D |
| Thrust | ↑ | W |
| Fire | Right Ctrl | Left Ctrl |

**5** inserts a coin, **1** starts a one-player game (1 credit), **2** a two-player game (2 credits), **Esc** quits. Default cabinet setting: 8 canisters.

### Dev tools

- `tools/remake/gen_shapes.py` regenerates `src/RipOff.Core/Shapes/ShapeData.g.cs` from `disasm/shapes/objects.txt` and `shapes.txt`.
- `tools/remake/Replay` (not in the solution) replays a full MAME RAM log through RipOff.Core frame by frame: `dotnet run -c Release --project tools/remake/Replay -- idle1 202 12000 5 idle`. It needs the local trace runs (`RUNS`, default `/workspace/mame/runs`), which are not in the repo.
