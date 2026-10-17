# ripoff
C# remake of the 1980 Cinematronics arcade game Rip-Off

## Building and running

The remake is four .NET projects in `RipOff.sln`:

| Project | What it is |
|---|---|
| `src/RipOff.Core` | Pure .NET 10 library: the whole game simulation, one deterministic pass per 38 Hz frame, from `disasm/GAME_RULES.md` (code comments cite the rules and `ripoff.lst` lines). Also the shape data (`Shapes/ShapeData.g.cs`, generated from our decoded docs by `tools/remake/gen_shapes.py`) and the scene builder that turns the state into lines. |
| `src/RipOff.Game` | MonoGame 3.8.6 DesktopGL window: fixed 38 Hz step, keyboard and game-pad input (`PadMapper`), custom line renderer, sound output (`SoundOutput`). |
| `tests/RipOff.Core.Tests` | xUnit tests: rule checks, replays of the scripted MAME runs against arcade RAM checkpoints (including the sound command byte), sound generator tests, and a headless render-to-PNG check. |
| `tests/RipOff.Game.Tests` | xUnit tests for the controller mapping (fake `GamePadState` values) and the PCM output path; headless. |

Sound (`src/RipOff.Core/Audio`) is synthesized at runtime from a model of the arcade sound board (MAME's netlist values plus measurements of its output, `disasm/NOTES.md` §21). No audio is recorded from MAME, and no sound files are shipped.

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

Sound: **M** mutes / unmutes, **−** / **+** (main keyboard or keypad) lower / raise the volume in 10 % steps (changing the volume also unmutes). The window title shows the volume, "muted", or why there is no audio, and which pads are connected.

### Controller

Any controller SDL2 knows as a GameController (Xbox, PlayStation, Switch Pro, most others) through MonoGame's `GamePad` API. The first pad Windows reports (MonoGame index 0) is player 1, the second (index 1) player 2. Keyboard and pads work at the same time (their inputs are OR'd).

| Action | Pad button (Xbox names; PlayStation in brackets) |
|---|---|
| Turn left / right | Left stick left / right (dead zone 0.35), or D-pad left / right |
| Thrust | A [Cross], or right trigger RT [R2] pressed past 30 % |
| Fire | X [Square], or right bumper RB [R1]. One shot per press, like the arcade button |
| Coin | Back / View [Share / Create], on either pad |
| 1-player start | Start / Menu [Options] on pad 1 |
| 2-player start | Start on pad 2, or hold LB [L1] and press Start on pad 1 |

Pads can be plugged in or pulled out at any time; a pulled pad just stops steering, and a button held while plugging in does not count as a press.

### Testing on Windows

1. `dotnet test RipOff.sln` (both test projects, no window, no audio device needed).
2. `dotnet run --project src/RipOff.Game`. The title should read "volume 100%". If it shows "no audio hardware", the game still runs silently.
3. Controller: connect a pad before or after starting. Back = coin, Start = 1-player game; steer with the stick and the D-pad, thrust with A and with RT, fire with X and RB (holding fire gives one shot). With a second pad: coin twice, Start on pad 2 for a 2-player game (or LB + Start on pad 1). Pull a pad out mid-game: the game keeps running; plug it back in.
4. Each sound (what triggers it in the game):

| Sound | How to hear it |
|---|---|
| Background drone | Starts with the game; pulses and rises in pitch as enemies steal canisters. Silent in attract mode and on the BONUS screen |
| Motor | Low two-tone engine hum while a wave of enemies is on the field; stops in the short gap between waves |
| Torpedo | Each of your shots: a falling "pew" |
| Explosion | Shoot or ram an enemy, or get destroyed: a noise burst that holds about 0.35 s and then fades |
| Laser | An enemy that chases you fires its beam (the dashed fire line): a fast downward sweep |
| Beep | The BONUS LEVEL screen, shown after the sixth enemy type has been beaten (the level goes up): beeps about 10 times a second |

5. **M**, **−**, **+** change mute and volume while playing.

### Dev tools

- `tools/remake/gen_shapes.py` regenerates `src/RipOff.Core/Shapes/ShapeData.g.cs` from `disasm/shapes/objects.txt` and `shapes.txt`.
- `tools/remake/Replay` (not in the solution) replays a full MAME RAM log through RipOff.Core frame by frame: `dotnet run -c Release --project tools/remake/Replay -- idle1 202 12000 5 idle`. It needs the local trace runs (`RUNS`, default `/workspace/mame/runs`), which are not in the repo.
