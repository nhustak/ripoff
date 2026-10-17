// RipOff.Game entry point.
//   dotnet run --project src/RipOff.Game                         play (splash, then the main menu)
//   dotnet run --project src/RipOff.Game -- --no-steam           never start Steam this run (by default it starts only when a Steam option is picked)
//   dotnet run --project src/RipOff.Game -- --shot out.png 400   run 400 scripted passes, save one frame, exit
//   +connect_lobby <id>                                          (passed by Steam when joining from the friends list)
using System;
using System.IO;
using RipOff.Game;
using RipOff.Game.Steam;

string? shot = null; int shotPasses = 400; bool noSteam = false;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--no-steam") noSteam = true;
    else if (args[i] == "--shot" && i + 1 < args.Length)
    {
        shot = args[++i];
        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var n)) { shotPasses = n; i++; }
    }
}

var store = new SettingsStore();
var settings = store.Load(out var warning);
if (warning != null) Console.Error.WriteLine("RipOff: " + warning + " (" + store.FilePath + ")");

// Steam is lazy by default (NOTES §24): SteamAPI_Init runs only when a Steam online option is picked, because an
// initialized Steam client under App ID 480 can take the controller away (Steam Input). It runs here, before the
// window, only when Options -> STEAM AT LAUNCH is on or Steam launched us with +connect_lobby: the overlay (invite
// dialog) hooks the graphics device only if SteamAPI_Init ran before the device was created (Steamworks overlay docs).
var log = new InputLog(Path.GetDirectoryName(store.FilePath)!);
log.Header($"os: {Environment.OSVersion}, .NET {Environment.Version}, 64-bit process: {Environment.Is64BitProcess}");
log.Header($"args: {(args.Length == 0 ? "(none)" : string.Join(' ', args))}");
log.Header($"settings: {store.FilePath} ({(warning ?? "ok")}), stick dead zone {settings.StickDeadzone:0.00}, steam at launch {settings.SteamAtLaunch}");
foreach (var name in new[] { "SteamAppId", "SteamGameId", "SteamClientLaunch", "SDL_GAMECONTROLLER_IGNORE_DEVICES", "SDL_GAMECONTROLLER_IGNORE_DEVICES_EXCEPT", "SDL_GAMECONTROLLERCONFIG" })
    log.Header($"env {name}: {(Environment.GetEnvironmentVariable(name) is { } v ? (v.Length > 300 ? v[..300] + "..." : v) : "(not set)")}");
var steam = shot == null ? new SteamGate(args, disabled: noSteam) : null;
if (steam != null)
{
    steam.Initializing = (p, atLaunch) =>
    {
        log.Event($"steam init ({(atLaunch ? "at launch" : "lazy, after the window")}): {p.Status}");
        Console.WriteLine("RipOff: " + p.Status + (p.Available ? "" : " - online play via Steam disabled; local and direct IP still work"));
    };
    bool connectArg = SteamPlatform.ParseConnect(string.Join(' ', args)) != null;
    if (!noSteam && (settings.SteamAtLaunch || connectArg)) steam.InitAtLaunch();
    else log.Header("steam: not started at launch (" + (noSteam ? "--no-steam" : "STEAM AT LAUNCH off") + ")");
}
try
{
    using var game = new RipOffGame(shot, shotPasses, steam, settings, store, warning, shot == null ? log : null);
    game.Run();
}
finally { steam?.Dispose(); }
