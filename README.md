# ripoff
C# remake of the 1980 Cinematronics arcade game Rip-Off

## Building and running

The remake is six .NET projects in `RipOff.sln`:

| Project | What it is |
|---|---|
| `src/RipOff.Core` | Pure .NET 10 library: the whole game simulation, one deterministic pass per 38 Hz frame, from `disasm/GAME_RULES.md` (code comments cite the rules and `ripoff.lst` lines). Also the shape data (`Shapes/ShapeData.g.cs`, generated from our decoded docs by `tools/remake/gen_shapes.py`) and the scene builder that turns the state into lines. |
| `src/RipOff.Net` | Online play without MonoGame or Steam: input-sync lockstep (`LockstepSession`), wire protocol, the `ITransport` abstraction, a LiteNetLib 2.1.4 UDP transport and an in-memory loopback network for tests. |
| `src/RipOff.Game` | MonoGame 3.8.6 DesktopGL window: fixed 38 Hz step, input (`InputReader`, `PadMapper`), the front end (`FrontEnd`: splash, menus, options, pause, on-screen keyboard), settings (`Settings`), custom line renderer, sound output (`SoundOutput`), the online API (`OnlineController`) and Steam (`Steam/`, Steamworks.NET 2025.164.1). |
| `tests/RipOff.Core.Tests` | xUnit tests: rule checks, replays of the scripted MAME runs against arcade RAM checkpoints (including the sound command byte), sound generator tests, and a headless render-to-PNG check. |
| `tests/RipOff.Net.Tests` | Lockstep over a simulated link with latency, jitter, loss, duplication and reordering (6000 ticks, per-frame state hashes identical), desync detection, version/key and game-config checks, and LiteNetLib over localhost UDP. |
| `tests/RipOff.Game.Tests` | xUnit tests that drive every screen of the front end with fake pad states and keys (navigation, pause, Exit as the only quit, F10/Esc/Start routing, options, on-screen keyboard, an online direct-IP game between two front ends), plus settings files, pad mapping, the PCM path and starting without Steam; headless. |

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

### Menus

The game opens on a splash screen (the title from `Branding.cs`); any key or pad button skips it, and it moves on by itself after 6 s.

```
MAIN MENU
├─ START 1 PLAYER            → MODE SELECT, then starts at once
├─ START 2 PLAYERS           → MODE SELECT, then starts at once (local, same screen)
├─ ONLINE
│  ├─ HOST ONLINE (STEAM)    → MODE SELECT; lobby + 6-character code; INVITE A STEAM FRIEND / CANCEL
│  ├─ JOIN BY CODE (STEAM)   on-screen keyboard (no mode select: the host's mode is used)
│  ├─ HOST DIRECT IP         → MODE SELECT; shows this PC's addresses and UDP port 47815
│  ├─ JOIN DIRECT IP         on-screen keyboard (ip or ip:port; the host's mode is used)
│  └─ BACK
├─ OPTIONS
│  ├─ AUTO FIRE: OFF/ON           (default OFF = arcade, one shot per press)
│  ├─ CANISTERS: 4/8/12/16        (default 8)
│  ├─ VOLUME: 0-100%              (steps of 10)
│  ├─ MUTE: OFF/ON
│  ├─ DISPLAY: WINDOWED/FULLSCREEN
│  ├─ STICK DEAD ZONE: 10-60%     (default 35%, steps of 5)
│  ├─ STEAM AT LAUNCH: OFF/ON     (default OFF; applies on the next start; see Controller troubleshooting)
│  ├─ ENHANCED OPTIONS: ALL ON    → ENHANCED OPTIONS page (below)
│  ├─ INPUT TEST                  all 4 pad slots live (hold B or press Esc to leave)
│  └─ BACK
└─ EXIT                      the only way out, with EXIT TO DESKTOP below

MODE SELECT (after a local start or a host item)
├─ ARCADE                    the original 1980 game (tag arcade-v1.0), never changed
├─ ENHANCED                  longer games: slower ramp, enemies that miss (below); shows the Enhanced options
└─ BACK

ENHANCED OPTIONS (OPTIONS → ENHANCED OPTIONS; Enhanced games only, Arcade ignores them)
├─ WARP-IN: ON/OFF           players warp in at their spawn point instead of driving in from the edge
├─ ENEMY AIM ERROR: ON/OFF   enemy shots can miss; reload after a miss; later first shot
├─ SLOWER RAMP: ON/OFF       two clears per enemy type, 3-enemy waves from level 3, speed cap
├─ SLOWER TOWING: ON/OFF     towing enemies at 75 %
├─ RANDOM WARP-IN: ON/OFF    warp in at a random spot, away from the canisters and the other player (needs WARP-IN)
├─ WARP KILLS: ON/OFF        warping in on top of an enemy destroys it; you score and survive (needs WARP-IN)
├─ ALL ON
└─ BACK
```

All Enhanced options default ON. RANDOM WARP-IN and WARP KILLS only matter with WARP-IN on: while it's off they're dimmed and ignored, and they keep their setting. They're saved in settings.json as `"EnhancedOff": [...]` (the ones switched off), so older files and options added later read as ON. Each toggle sets its knobs back to the Arcade values when off; all off plays like Arcade. Online, the guest uses the host's options.

The last mode picked is highlighted next time and saved in settings.json (`"Mode": "Arcade"` / `"Enhanced"`).

**Game modes** (`src/RipOff.Core/GameRules.cs`): `PlayMode { Arcade, Enhanced }` is part of `GameConfig`, and the `Simulation` reads its tunables from `GameRules.For(mode)` instead of constants. `GameRules.Arcade` holds the arcade values and must not change: `ArcadeDeterminismTests` pins the state checksums of scripted Arcade runs to the values recorded at `arcade-v1.0`. `GameRules.Enhanced` is where expanded-mode tweaks go. The state hash includes the mode (and Enhanced-only state) only when it is not Arcade, so Arcade hashes are unchanged.

**ENHANCED** (disasm/NOTES.md §27): same game, tuned so it lasts about 2-2.5× longer with a scripted player.
- Enemy shots can miss: ±20° aim spread at level 1, 2° tighter per level down to ±10°, and the shot must pass within 12 units. About half of the shots miss; close range still kills.
- After a miss an enemy waits 32 passes before it can fire again, and a fresh chaser waits 0.5 s.
- Two cleared waves per enemy-type step. Three-enemy waves start at level 3. Enemy speed is capped at 8 + level.
- Towing enemies move at 75 %.
- Warp-in (NOTES §28): after the respawn delay a player warps in over 0.8 s, with a contracting vector ring. While warping it can't be hit, doesn't collide and can't fire. It then materializes standing still, facing the canisters.
- Random warp-in (NOTES §29): the spot is picked when the warp starts, so the effect shows where you'll appear. It is at least 160 units from every edge, clear of the canister cluster (96-unit gap) and at least 240 units from the other player or their warp spot. Enemies aren't avoided. Each local game gets a fresh random seed.
- Warp kills (NOTES §29): when the warp completes, any enemy touching you (the normal tank contact size) is destroyed. You get the normal points, it counts as a kill for the wave, a towed canister is dropped, and you survive. It only happens at the moment you materialize, not during the warp, so an enemy that drives out of the effect in time is spared.

Enhanced changes done so far, and the ideas backlog: [ENHANCEMENTS.md](ENHANCEMENTS.md).

All randomness comes from a seeded generator in the game state, so online games stay in lockstep. Measure with `tools/remake/Waves stats [seeds] [minutes]`.

```
PAUSE MENU (in a game: Esc or pad Start)
├─ RESUME
├─ OPTIONS
├─ QUIT TO MAIN MENU         (online: LEAVE ONLINE GAME)
└─ EXIT TO DESKTOP

GAME OVER (when all canisters are gone)
├─ PLAY AGAIN                (online: either player starts the next game)
└─ MAIN MENU                 (online: LEAVE ONLINE GAME)
```

**Scores** are at the top of the screen in both modes: P1 flush left and P2 flush right (one-player: P1 only), 16 field units from the edges of the 4:3 play field. In fullscreen on a wide monitor they sit at the corners of the letterboxed field. The arcade drew them mid-field, with P1 on the right (disasm/NOTES.md §13).

There are no coins, credits or start buttons: picking a game in the menu starts it. Nothing quits the game except **EXIT** and **EXIT TO DESKTOP** (or closing the window). Auto fire and canisters apply when a game starts; online, the host's values are used for both players.

Settings are saved as JSON in `%APPDATA%\RipOffRemake\settings.json` on Windows (`~/.config/RipOffRemake/settings.json` on Linux), after every change. A missing file means defaults. An unreadable file is renamed to `settings.json.corrupt`, and the game starts with defaults and says so at the bottom of the screen.

**Window position and size.** In windowed mode the game remembers where its window was and how big it was (`WindowX`, `WindowY`, `WindowWidth`, `WindowHeight` in settings.json, client area in desktop pixels, plus `WindowMaximized`). It saves about a second after you stop moving or resizing, and again on exit. Fullscreen never overwrites these, so leaving fullscreen puts the window back where it was. At start the saved rect is checked against the displays connected now. If it's too small (under 320×240), garbage, or no longer reachable (at least 100 px of its width and the 32 px title-bar strip must be on one display), the game uses the default instead: 1024×768 centered on the primary display, scaled down to fit a small screen. A rect that's partly off the edge, or bigger than the display, is shrunk and moved fully onto the display it overlaps most. To reset, delete those keys from settings.json.

### Controls

Everything works with a controller alone, and everything works with the keyboard alone.

**Menus**

| Action | Controller (Xbox names; PlayStation in brackets) | Keyboard |
|---|---|---|
| Move | D-pad or left stick | Arrow keys |
| Select | A [Cross] | Enter or Space |
| Back / close menu | B [Circle] | Esc |
| Change an option | D-pad / stick left-right, or A | ← / →, or Enter |
| Open / close pause menu (in a game) | Start / Menu [Options] | Esc |
| Options from anywhere | (via the menus) | F10 |
| Volume | (Options) | − / + lower / raise, M mute |

The on-screen keyboard (lobby code, IP address): move with the D-pad/stick and press A on a key. X deletes a character, B cancels, and A on OK confirms. A full 6-character code jumps the cursor to OK. On a keyboard you can also just type: Backspace deletes, Enter confirms, Esc cancels.

**Playing**

| Action | Player 1 keys | Player 2 keys | Controller |
|---|---|---|---|
| Turn left / right | ← / → | A / D | Left stick (dead zone from Options) or D-pad |
| Thrust | ↑ | W | A [Cross] or LT [L2] past 30 % |
| Fire | Right Ctrl | Left Ctrl | X [Square], RB [R1] or RT [R2] past 30 % |

- In a 1-player game (and online), both keyboard sets and every pad steer your tank.
- In a 2-player game, player 1 uses the arrows, Right Ctrl and the first connected pad, and player 2 uses WASD, Left Ctrl and the second connected pad.
- The pads are taken in the order MonoGame lists them (indices 0-3); empty slots are skipped, so a pad that Windows lists second still counts as the first pad.
- Pads can be plugged in or pulled out at any time; a button held while plugging in does not count as a press.
- **Fire**: one shot per press, as in the arcade. With **AUTO FIRE: ON**, holding fire repeats a shot every 6 passes, about 6.3 shots/s at 38 Hz. Each player still has at most 4 shots in flight: with all 4 out, the next shot leaves as soon as one ends.

The window title shows the volume (or why there is no audio), how many pads are connected, and whether Steam is on.

### Controller troubleshooting

**Steam starts only when you need it.** From M3 (PR #11) to M4 (PR #12) the game initialized Steam at every launch, before the window. Whether that is what stopped Nick's controller is UNCHECKED (NOTES §25). With `steam_appid.txt` = 480, an initialized Steam client treats the game as Spacewar, and its Steam Input can redirect the controller away from XInput. Sources:

- A Valve input engineer in [Steamworks.NET #380](https://github.com/rlabrecque/Steamworks.NET/issues/380).
- Reports from [Unreal](https://forums.unrealengine.com/t/gamepad-not-working-when-connected-to-steam/448003), [GodotSteam](https://github.com/GodotSteam/GodotSteam/discussions/568) ("inputs work until Steam.run_callbacks() kicks in") and [Godot](https://forum.godotengine.org/t/disabling-steam-input-in-godotsteam/118350), all under App ID 480.

So by default the game never touches Steam for local play, the menus or direct IP. Steam is initialized only when you pick **HOST ONLINE (STEAM)** or **JOIN BY CODE (STEAM)**.

Trade-off: the Steam overlay (Shift+Tab, **INVITE A STEAM FRIEND**) works only if Steam was initialized before the window was created. To get the overlay, set **Options → STEAM AT LAUNCH: ON** and restart, or let Steam start the game with an invite (`+connect_lobby`). Lobby codes work either way. `--no-steam` never starts Steam.

**Steam Input is supported in the game (no need to turn it off).** Once Steam is initialized, the game also reads controllers through Steamworks **ISteamInput**, using Spacewar's (App 480) action sets: `ship_controls` in a game and `menu_controls` in menus. If Steam Input delivers a controller, the game uses it, and the SDL/XInput pads are ignored for that tick so the same press is not counted twice. Otherwise the SDL pads are used as before. With Steam not started, the game reads the pad through SDL (MonoGame GamePad).

Which physical buttons do what under Steam Input depends on Steam's Spacewar configuration for your controller, not on this game. In Valve's sample Xbox configuration:
- RT fires.
- A / B are select / back in menus.
- The D-pad moves in menus.
- The left stick steers.

The Steam Input path is UNCHECKED: it has not been run against a real Steam client. Disabling Steam Input for Spacewar (Library → Spacewar → Properties → Controller) is only a fallback, not a requirement.

**What Windows sees:**

- **Options → INPUT TEST** lists pad slots 0-3. For each it shows connected or not, the controller name and type, the buttons held, both sticks and both triggers. It also shows what the game makes of it ("GAME SEES: LEFT THRUST FIRE"), the **PAD SOURCE** (SDL or Steam Input) and the Steam state. Every button can be tested, so leave by holding **B** for 1 s or pressing **Esc**.
- **`%APPDATA%\RipOffRemake\input-log.txt`** is rewritten at every launch, 2 s after the window opens (SDL reports pads only once the window runs). It records:
  - Windows and .NET version, arguments, settings path, dead zone and STEAM AT LAUNCH.
  - Steam-related environment variables (`SteamAppId`, `SDL_GAMECONTROLLER_IGNORE_DEVICES`, …).
  - All 4 slots with name, type, buttons and axes.
  - Later events: pads connecting or disconnecting, Steam starting with its result, and which **pad source** is in use (SDL, or Steam Input with how many controllers are bound).
  - It contains no Steam IDs or tokens.

UNCHECKED: whether Steam Input is the cause on Nick's PC. The log and INPUT TEST will show it: a pad that is listed before Steam starts and disappears after would confirm it.

### Testing on Windows

1. `dotnet test RipOff.sln` (all three test projects; no window, audio device or Steam needed).
2. `dotnet run --project src/RipOff.Game` (or `RipOff.exe` from the zip). The splash appears; press any button.
   - The window title should end in "N pad(s), Steam not started".
   - If a pad does nothing: open **Options → INPUT TEST** with the keyboard (arrows, Enter) and check which slot shows the pad and whether its buttons light up. Then send `%APPDATA%\RipOffRemake\input-log.txt`.
3. Controller only, keyboard untouched:
   1. On the main menu, move with the D-pad and the stick and open **OPTIONS** with A.
   2. Change every option with left/right and A. **DISPLAY** switches the window to fullscreen and back. Go back with B.
   3. Pick **START 1 PLAYER**. Thrust with A and LT, fire with X, RB and RT.
   4. Press Start: the pause menu opens and the game freezes. Press Start or B to resume. Then use **QUIT TO MAIN MENU**.
   5. Turn **AUTO FIRE** on in Options, start again, and hold RT: shots repeat, never more than 4 in flight.
   6. Plug in a second pad and pick **START 2 PLAYERS**: pad 2 drives player 2.
   7. Let the enemies take all canisters: **GAME OVER** offers PLAY AGAIN / MAIN MENU.
   8. Finally choose **EXIT** on the main menu.
4. Keyboard only: the same tour with the arrows, Enter, Esc and F10. Esc and B never quit the game.
5. Restart: your options are kept. To test a corrupt settings file, put garbage in `%APPDATA%\RipOffRemake\settings.json`. The game starts with defaults, mentions it at the bottom of the screen, and keeps the bad file as `settings.json.corrupt`.
4. Each sound (what triggers it in the game):

| Sound | How to hear it |
|---|---|
| Background drone | Starts with the game; pulses and rises in pitch as enemies steal canisters. Silent in attract mode and on the BONUS screen |
| Motor | Low two-tone engine hum while a wave of enemies is on the field; stops in the short gap between waves |
| Torpedo | Each of your shots: a falling "pew" |
| Explosion | Shoot or ram an enemy, or get destroyed: a noise burst that holds about 0.35 s and then fades |
| Laser | An enemy that chases you fires its beam (the dashed fire line): a fast downward sweep |
| Beep | The BONUS LEVEL screen, shown after the sixth enemy type has been beaten (the level goes up): beeps about 10 times a second |

6. **M**, **−**, **+** change mute and volume while playing.

### Online play

Main menu → **ONLINE** (tree above). Steam starts the first time you pick a Steam item. If it cannot start, the reason is shown and the Steam items are greyed out; direct IP still works. `--no-steam` skips Steam entirely.

- **Pausing online:** the shared game never pauses (a choice; it is flagged in NOTES §23). Esc or Start opens the menu as an overlay. The game keeps running, dimmed, behind it, and your tank gets no input until you close the menu.
- **Rules:** the host's mode (**ARCADE** / **ENHANCED**), **AUTO FIRE** and **CANISTERS** settings are sent to the guest and checked by both sides before the first frame. The guest adopts the host's mode for that session (its own saved mode is unchanged). The connecting screen shows the mode; Steam lobbies also advertise it (`mode` lobby key). For Enhanced, the host's **ENHANCED OPTIONS** are sent too and adopted by the guest. RANDOM WARP-IN and WARP KILLS are two more bits in the same options byte, so the protocol is still v4. A 2026.10.17.3/.4 guest refuses a newer host that has either of them on ("unknown enhanced options"), while a newer guest joining an older host simply plays with both off. Protocol version 4: builds from before the options (v3: 2026.10.17.1, 2026.10.17.2, `enhanced-preview-1`) and before game modes (v2, `arcade-v1.0`) are refused at connect with a version-mismatch message naming both builds.
- **After a game over:** choose **PLAY AGAIN**; either player can start the next game. There are no coins online either.

How it works (`disasm/NOTES.md` §22):

- **Lockstep**: both machines run the same deterministic simulation and exchange only inputs. Input delay **3 frames** (79 ms at 38 Hz); every 19th frame (twice a second) both sides compare a hash of the whole game state. **No rollback**: on a slow link the game waits ("WAITING FOR OTHER PLAYER") instead of predicting.
- **Steam**: Steamworks.NET with Valve's test App ID **480** (Spacewar). Traffic goes through `ISteamNetworkingMessages` over Steam Datagram Relay, with direct (ICE) connections turned off, so nobody forwards ports and neither player sees the other's IP. Lobbies carry the game key and protocol version; every lobby search filters on both, and a peer with another version or key is refused.
- **Game config**: the host's `GameConfig` (canister count and auto-fire) goes to the guest at session start. The guest refuses a config it cannot run and echoes a hash of what it received; the host refuses a mismatch. No frame runs before that check.
- **Desync**: if the hashes differ, both games stop with "DESYNC at frame N" and each writes `desync-<date>-<time>-host|guest.txt` next to the exe (versions, start config, every frame's inputs).

#### Online API (for the front end)

`OnlineController` (src/RipOff.Game) holds no UI and reads no keyboard. Codes and addresses are plain strings, so a pad-driven on-screen keyboard can supply them:

| Call | Does |
|---|---|
| `HostSteam(GameConfig?)` / `JoinSteamCode(code)` / `JoinSteamLobby(id)` | Steam lobby host / join by code / join from an invite (the front end passes the Options values) |
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
| `ModeSelectTitle`, `ModeArcade`, `ModeEnhanced`, `ModeArcadeInfo`, `ModeEnhancedInfo` | `SELECT MODE`, `ARCADE`, `ENHANCED`, ... | MODE SELECT screen, online status, in-game "YOU: P1 ARCADE" |

Also change `AssemblyName` in `src/RipOff.Game/RipOff.Game.csproj` (exe name). A real release also needs its own App ID in place of 480. Sounds are synthesized in `src/RipOff.Core/Audio`.

### Publishing a Windows build

```powershell
pwsh tools/publish.ps1                      # next release version for today
pwsh tools/publish.ps1 -Version 2026.10.17.2 # a given version
pwsh tools/publish.ps1 -Version dev         # dev build (0.0.0.0, "dev+<sha>")
```

This writes `artifacts/RipOff-<version>-win-x64.zip` (e.g. `RipOff-2026.10.17.1-win-x64.zip`) (about 37 MB): a self-contained win-x64 build, so the other machine needs no .NET install. It works from Windows, Linux or macOS. Without the script: `dotnet publish src/RipOff.Game -p:PublishProfile=win-x64` gives the same folder in `artifacts/publish/win-x64/` (a dev build unless you add `-p:RipOffVersion=...`). The zip is flat (204 files). Next to `RipOff.exe` are `steam_api64.dll`, `Steamworks.NET.dll` (the Windows build of it), `steam_appid.txt` (`480`), `LiteNetLib.dll`, `SDL2.dll`, `openal.dll`, `MonoGame.Framework.dll`, the `RipOff*.dll` files, and the .NET runtime. The script fails if the exe, the Steam files, or `480` are missing. Checked by cross-publishing on the Linux box; UNCHECKED: running the zip on Windows.

### Versioning

Versions are **YYYY.MM.DD.N**: the release date in America/New_York, zero-padded, and the release number of that day starting at 1 (`2026.10.17.1`, then `2026.10.17.2`). Tags are `v` + version (`v2026.10.17.1`).

- **Single source:** `Directory.Build.props` (all projects). `-p:RipOffVersion=2026.10.17.1` or `-p:Version=2026.10.17.1` sets it.
  - `Version`, `AssemblyVersion` and `FileVersion` hold the four numbers, each ≤ 65535 (a leading zero is dropped there: 2027.01.05.1 becomes 2027.1.5.1).
  - `InformationalVersion` is `2026.10.17.1+<short sha>` (`Directory.Build.targets` asks git).
  - Without a version, a build is a **dev build**: `0.0.0.0`, informational `dev+<sha>` (`nogit` outside a git checkout).
- **Rules and next number:** `RipOff.Core.ReleaseVersion` (strict format, real dates, N ≥ 1). `tools/publish.ps1` without `-Version` lists the tags `v*` locally and on `origin` and asks `tools/release/NextVersion` for today's next N.
- **In the game:** `RipOff.Core.BuildInfo`.
  - The main menu and the pause menu show `V2026.10.17.1` (or `DEV ABC1234`) in the bottom-right corner, and the window title shows it too.
  - It is the first line of `input-log.txt` and is in desync reports.
  - Online, each peer sends its build in Hello (display only, shown as "OTHER PLAYER: V…"), and Steam lobbies carry a `build` key. **Compatibility is still decided by the protocol version** (`Protocol.Version`, now 3); a mismatch message names both builds.

### Releases / distribution

Releases are cut from a tag on `main`, and the zip is attached to a GitHub release. Exact steps (run from a clean, up-to-date `main`):

```powershell
dotnet test RipOff.sln                                    # everything must pass
pwsh tools/publish.ps1                                    # prints the version, e.g. 2026.10.17.1; builds artifacts/RipOff-2026.10.17.1-win-x64.zip
unzip -l artifacts/RipOff-2026.10.17.1-win-x64.zip        # RipOff.exe, steam_api64.dll, steam_appid.txt (480) present
git tag -a v2026.10.17.1 -m "Rip-Off 2026.10.17.1"        # annotated, by Nick Hustak <nhustak@gmail.com>; never moved or re-pushed
git push origin v2026.10.17.1
gh release create v2026.10.17.1 artifacts/RipOff-2026.10.17.1-win-x64.zip --verify-tag --title "Rip-Off 2026.10.17.1" --notes-file notes.md
```

Build first and tag second. The script picks N from the tags that already exist, so tagging first would make it pick N + 1. Notes should say how to play locally and online (see Testing with a friend), and that both online players need the same build.

**Pre-scheme releases** (kept as they are):
- [`arcade-v1.0`](https://github.com/nhustak/ripoff/releases/tag/arcade-v1.0): the faithful Arcade game, protocol v2.
- [`enhanced-preview-1`](https://github.com/nhustak/ripoff/releases/tag/enhanced-preview-1): Arcade + Enhanced, protocol v3, pre-release.

The repo is private: a friend can only download a release asset with access to the repo. Otherwise send them the zip directly.

### Testing with a friend

Steam (each person needs their own Steam account):

1. Both: start Steam and log in (two different accounts; you do not need to own Spacewar). Unzip `RipOff-win-x64.zip` anywhere and run `RipOff.exe` directly (not through Steam). Steam should then show you as playing "Spacewar". That is App ID 480.
2. Host: **ONLINE → HOST ONLINE (STEAM)**. The screen shows a code such as `K7QX2M`.
3. Invite in one of these ways:
   - Overlay (needs **STEAM AT LAUNCH: ON** and a restart): press **Shift+Tab** or pick **INVITE A STEAM FRIEND**, then invite the friend.
   - Steam friends list: invite them or have them join you.
   - Code: tell them the code. They pick **ONLINE → JOIN BY CODE (STEAM)** and enter it on the on-screen keyboard (or type it).
4. **The friend must already have `RipOff.exe` running before accepting an invite.** With App ID 480, accepting an invite while the game is closed starts Spacewar instead ([Unreal forum](https://forums.unrealengine.com/t/steam-session-invite-starts-spacewar/500975), [Unity forum](https://discussions.unity.com/t/steamworks-implementation-launches-spacewar/829009)). If that happens, close Spacewar, start `RipOff.exe`, and use the code.
5. The game starts as soon as both are connected. After a game over, either player chooses **PLAY AGAIN**.

480 caveats:

- The game appears as "Spacewar" in Steam.
- Outside the Steam client the overlay is loaded by `SteamAPI_Init`, which must run before the graphics device exists. The game does that only with **Options → STEAM AT LAUNCH: ON** (default OFF, see Controller troubleshooting). If the overlay still doesn't open, Valve's advice is to launch through Steam ([Steamworks overlay docs](https://partner.steamgames.com/doc/features/overlay)). Here, use the code or the friends list instead.
- Do not add `SteamAPI.RestartAppIfNecessary(480)`: it would relaunch as Spacewar.
- `steam_appid.txt` must stay next to the exe, and Steam must be running ([Steamworks API example](https://partner.steamgames.com/doc/sdk/api/example)).

Direct IP / LAN (no Steam needed):

1. Host: **ONLINE → HOST DIRECT IP**. The screen lists this PC's IPv4 addresses and the port (47815).
2. Allow `RipOff.exe` through Windows Firewall when Windows asks (UDP 47815 inbound). Over the internet, also forward UDP 47815 on the host's router, or use a virtual-LAN tool both of you trust.
3. Friend: **ONLINE → JOIN DIRECT IP**, then enter the host's address, e.g. `192.168.1.20` or `203.0.113.5:47815`.

If a **DESYNC** appears: both of you go back to the menu and start a new session. Send Nick the `desync-*.txt` files from both machines (they are next to `RipOff.exe`). Also make sure both machines run the same zip; a different protocol version is refused at connect, but two different builds with the same version are not.

If the connection drops, the game gives up after 10 s without data and shows why.

### Dev tools

- `tools/padtest`: an end-to-end controller test on Linux.
  - It runs the real game under Xvfb with software GL.
  - A startup hook attaches an SDL2 virtual game controller inside the process (`SDL_JoystickAttachVirtual`).
  - It then plays a button script and saves screenshots. Usage is at the top of `tools/padtest/run.py`.
  - It exercises MonoGame's real GamePad path (SDL 2.32.10). Windows' XInput/RawInput backends and Steam are not covered.


- `tools/remake/gen_shapes.py` regenerates `src/RipOff.Core/Shapes/ShapeData.g.cs` from `disasm/shapes/objects.txt` and `shapes.txt`.
- `tools/remake/Replay` (not in the solution) replays a full MAME RAM log through RipOff.Core frame by frame: `dotnet run -c Release --project tools/remake/Replay -- idle1 202 12000 5 idle`. It needs the local trace runs (`RUNS`, default `/workspace/mame/runs`), which are not in the repo.
