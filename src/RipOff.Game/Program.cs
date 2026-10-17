// RipOff.Game entry point.
//   dotnet run --project src/RipOff.Game                         play (menu: local, Steam online, direct IP)
//   dotnet run --project src/RipOff.Game -- --no-steam           skip Steam entirely (local / direct IP only)
//   dotnet run --project src/RipOff.Game -- --shot out.png 400   run 400 scripted passes, save one frame, exit
//   +connect_lobby <id>                                          (passed by Steam when joining from the friends list)
using System;
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

// Steam first: the overlay hooks the graphics device only if SteamAPI_Init ran before it was created
// (Steamworks "Steam Overlay" docs). Without Steam the game still starts, with local and direct-IP play.
SteamPlatform? steam = null;
if (shot == null && !noSteam)
{
    steam = SteamPlatform.Init(args);
    Console.WriteLine("RipOff: " + steam.Status + (steam.Available ? "" : " - online play via Steam disabled; local and direct IP still work"));
}
try
{
    using var game = new RipOff.Game.RipOffGame(shot, shotPasses, steam);
    game.Run();
}
finally { steam?.Dispose(); }
