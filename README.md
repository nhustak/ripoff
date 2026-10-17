# ripoff
C# remake of the 1980 Cinematronics arcade game Rip-Off

## Building and running

The remake is six .NET projects in `RipOff.sln`:

| Project | What it is |
|---|---|
| `src/RipOff.Core` | Pure .NET 10 library: the whole game simulation, one deterministic pass per 38 Hz frame, from `disasm/GAME_RULES.md` (code comments cite the rules and `ripoff.lst` lines). Also the shape data (`Shapes/ShapeData.g.cs`, generated from our decoded docs by `tools/remake/gen_shapes.py`) and the scene builder that turns the state into lines. |
| `src/RipOff.Net` | Online play without MonoGame or Steam: input-sync lockstep (`LockstepSession`), wire protocol, the `ITransport` abstraction, a LiteNetLib 2.1.4 UDP transport and an in-memory loopback network for tests. |
| `src/RipOff.Game` | MonoGame 3.8.6 DesktopGL window: fixed 38 Hz step, keyboard and game-pad input (`PadMapper`), custom line renderer, sound output (`SoundOutput`), a minimal menu, the online API (`OnlineController`) and Steam (`Steam/`, Steamworks.NET 2025.164.1). |
| `tests/RipOff.Core.Tests` | xUnit tests: rule checks, replays of the scripted MAME runs against arcade RAM checkpoints (including the sound command byte), sound generator tests, and a headless render-to-PNG check. |
| `tests/RipOff.Net.Tests` | Lockstep over a simulated link with latency, jitter, loss, duplication and reordering (6000 ticks, per-frame state hashes identical), desync detection, version/key and game-config checks, and LiteNetLib over localhost UDP. |
| `tests/RipOff.Game.Tests` | xUnit tests for the controller mapping (fake `GamePadState` values), the PCM output path, the menu, the online API over localhost (direct IP, desync report), and starting without Steam; headless. |

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

   The first build downloads the NuGet packages (MonoGame.Framework.DesktopGL 3.8.6, LiteNetLib 2.1.4, xunit) and the Steamworks.NET 2025.164.1 standalone zip from its GitHub release (`build/Steamworks.targets`: SHA-256 pinned, unpacked to `.cache/`, never committed). No MonoGame content tools are needed: there is no content pipeline.
4. Optional: `dotnet run --project src/RipOff.Game -- --shot shot.png 400` plays 400 scripted passes, saves one frame to `shot.png` through the real MonoGame renderer, and exits. UNCHECKED: this path has not been run yet (the Linux build box has no OpenGL device; `dotnet test` covers rendering with a software rasteriser instead).

The same commands work on Linux and macOS with the .NET 10 SDK installed.

### Keys

| Action | Player 1 | Player 2 |
|---|---|---|
| Turn left / right | ← / → | A / D |
| Thrust | ↑ | W |
| Fire | Right Ctrl | Left Ctrl |

**5** inserts a coin, **1** starts a one-player game (1 credit), **2** a two-player game (2 credits), **Esc** goes back to the menu. Default cabinet setting: 8 canisters.

Menu: ↑/↓ (or D-pad / left stick) move, **Enter** (or A / Start) selects, **Esc** (or B / Back) goes back. Code and address entry take typed text, or on a pad: ↑/↓ change the last character, → adds one, ← deletes one. The menu is a deliberately minimal stopgap; the front-end milestone replaces it.

Online, each player uses either keyboard set (arrows + Right Ctrl, or WASD + Left Ctrl) or the first pad. There are no coins: after a game over either player presses Start (**1**, **2** or pad Start) for a new game. The host is player 1, the guest player 2. **Esc** leaves the online game.

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

1. `dotnet test RipOff.sln` (all three test projects, no window, no audio device, no Steam needed).
2. `dotnet run --project src/RipOff.Game`, then pick **LOCAL PLAY**. The title should read "volume 100%". If it shows "no audio hardware", the game still runs silently.
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

### Online play

Menu items: **LOCAL PLAY**, **HOST ONLINE (STEAM)** (creates a lobby and shows a 6-character code), **JOIN BY CODE (STEAM)**, **HOST DIRECT IP (LAN)** (UDP port 47815) and **JOIN DIRECT IP** (`ip` or `ip:port`). The Steam items are greyed out, with the reason, when Steam is not running; the rest still works. Start with `--no-steam` to skip Steam entirely.

How it works (`disasm/NOTES.md` §22):

- **Lockstep**: both machines run the same deterministic simulation and exchange only inputs. Input delay **3 frames** (79 ms at 38 Hz); every 19th frame (twice a second) both sides compare a hash of the whole game state. **No rollback**: on a slow link the game waits ("WAITING FOR OTHER PLAYER") instead of predicting.
- **Steam**: Steamworks.NET with Valve's test App ID **480** (Spacewar). Traffic goes through `ISteamNetworkingMessages` over Steam Datagram Relay, with direct (ICE) connections turned off, so nobody forwards ports and neither player sees the other's IP. Lobbies carry the game key and protocol version; every lobby search filters on both, and a peer with another version or key is refused.
- **Game config**: the host's `GameConfig` (canister count; an auto-fire field reserved for the next milestone) goes to the guest at session start. The guest refuses a config it cannot run and echoes a hash of what it received; the host refuses a mismatch. No frame runs before that check.
- **Desync**: if the hashes differ, both games stop with "DESYNC at frame N" and each writes `desync-<date>-<time>-host|guest.txt` next to the exe (versions, start config, every frame's inputs).

#### Online API (for the front end)

`OnlineController` (src/RipOff.Game) holds no UI and reads no keyboard. Codes and addresses are plain strings, so a pad-driven on-screen keyboard can supply them:

| Call | Does |
|---|---|
| `HostSteam(GameConfig?)` / `JoinSteamCode(code)` / `JoinSteamLobby(id)` | Steam lobby host / join by code / join from an invite |
| `HostLan(GameConfig?, port)` / `JoinLan("ip[:port]")` | Direct-IP host / join (LiteNetLib) |
| `Update(localInput, start)` | One 38 Hz tick |
| `Running`, `Session.Sim`, `Session.IsHost` | What to draw and which player you are |
| `Describe()`, `Error`, `Session.Phase`, `DesyncReportPath` | Status text for screens |
| `Stop()` | Leave (also leaves the lobby) |

Steam invites (overlay, friends list, `+connect_lobby`) arrive through `SteamPlatform.PendingJoin`.

### Branding

Everything a re-themed release changes lives in `src/RipOff.Game/Branding.cs`:

| Constant | Now | Used for |
|---|---|---|
| `GameName` | `RIP-OFF REMAKE` | Menu heading, Steam rich presence text |
| `WindowTitle` | `Rip-Off (remake)` | Window title |
| `LobbyGameKey` | `nhustak.ripoff-remake` | Steam lobby tag and search filter, LiteNetLib connection key. Must be unique, because App ID 480 is shared by everyone testing with it |
| `SteamAppId` | `480` | Must match `src/RipOff.Game/steam_appid.txt` (a test checks this) |
| `ShapeSet` | `RipOff.Core.Shapes.ShapeData` | The vector art (generated by `tools/remake/gen_shapes.py`) |

Also change `AssemblyName` in `src/RipOff.Game/RipOff.Game.csproj` (exe name). A real release also needs its own App ID in place of 480. Sounds are synthesized in `src/RipOff.Core/Audio`.

### Publishing a Windows build

```powershell
pwsh tools/publish.ps1
```

This writes `artifacts/RipOff-win-x64.zip` (about 37 MB): a self-contained win-x64 build, so the other machine needs no .NET install. It works from Windows, Linux or macOS. Without the script: `dotnet publish src/RipOff.Game -p:PublishProfile=win-x64` gives the same folder in `artifacts/publish/win-x64/`. The zip is flat (204 files). Next to `RipOff.exe` are `steam_api64.dll`, `Steamworks.NET.dll` (the Windows build of it), `steam_appid.txt` (`480`), `LiteNetLib.dll`, `SDL2.dll`, `openal.dll`, `MonoGame.Framework.dll`, the `RipOff*.dll` files, and the .NET runtime. The script fails if the exe, the Steam files, or `480` are missing. Checked by cross-publishing on the Linux box; UNCHECKED: running the zip on Windows.

### Testing with a friend

Steam (each person needs their own Steam account):

1. Both: start Steam and log in (two different accounts; you do not need to own Spacewar). Unzip `RipOff-win-x64.zip` anywhere and run `RipOff.exe` directly (not through Steam). Steam should then show you as playing "Spacewar". That is App ID 480.
2. Host: **HOST ONLINE (STEAM)**. The screen shows a code such as `K7QX2M`.
3. Invite in one of these ways:
   - Overlay: press **Shift+Tab** or pick **INVITE A STEAM FRIEND**, then invite the friend.
   - Steam friends list: invite them or have them join you.
   - Code: tell them the code. They pick **JOIN BY CODE (STEAM)** and type it.
4. **The friend must already have `RipOff.exe` running before accepting an invite.** With App ID 480, accepting an invite while the game is closed starts Spacewar instead ([Unreal forum](https://forums.unrealengine.com/t/steam-session-invite-starts-spacewar/500975), [Unity forum](https://discussions.unity.com/t/steamworks-implementation-launches-spacewar/829009)). If that happens, close Spacewar, start `RipOff.exe`, and use the code.
5. The game starts as soon as both are connected. Press Start after a game over to play again.

480 caveats:

- The game appears as "Spacewar" in Steam.
- Outside the Steam client the overlay is loaded by `SteamAPI_Init`, which must run before the graphics device exists. The game does this (`Program.cs`). If the overlay still doesn't open, Valve's advice is to launch through Steam ([Steamworks overlay docs](https://partner.steamgames.com/doc/features/overlay)). Here, use the code or the friends list instead.
- Do not add `SteamAPI.RestartAppIfNecessary(480)`: it would relaunch as Spacewar.
- `steam_appid.txt` must stay next to the exe, and Steam must be running ([Steamworks API example](https://partner.steamgames.com/doc/sdk/api/example)).

Direct IP / LAN (no Steam needed):

1. Host: **HOST DIRECT IP (LAN)**. The screen lists this PC's IPv4 addresses and the port (47815).
2. Allow `RipOff.exe` through Windows Firewall when Windows asks (UDP 47815 inbound). Over the internet, also forward UDP 47815 on the host's router, or use a virtual-LAN tool both of you trust.
3. Friend: **JOIN DIRECT IP**, then enter the host's address, e.g. `192.168.1.20` or `203.0.113.5:47815`.

If a **DESYNC** appears: both of you go back to the menu and start a new session. Send Nick the `desync-*.txt` files from both machines (they are next to `RipOff.exe`). Also make sure both machines run the same zip; a different protocol version is refused at connect, but two different builds with the same version are not.

If the connection drops, the game gives up after 10 s without data and shows why.

### Dev tools

- `tools/remake/gen_shapes.py` regenerates `src/RipOff.Core/Shapes/ShapeData.g.cs` from `disasm/shapes/objects.txt` and `shapes.txt`.
- `tools/remake/Replay` (not in the solution) replays a full MAME RAM log through RipOff.Core frame by frame: `dotnet run -c Release --project tools/remake/Replay -- idle1 202 12000 5 idle`. It needs the local trace runs (`RUNS`, default `/workspace/mame/runs`), which are not in the repo.
