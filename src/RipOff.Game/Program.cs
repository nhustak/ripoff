// RipOff.Game entry point.
//   dotnet run --project src/RipOff.Game                         play
//   dotnet run --project src/RipOff.Game -- --shot out.png 400   run 400 scripted passes, save one frame, exit
string? shot = null; int shotPasses = 400;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--shot" && i + 1 < args.Length)
    {
        shot = args[++i];
        if (i + 1 < args.Length && int.TryParse(args[i + 1], out var n)) { shotPasses = n; i++; }
    }
}
using var game = new RipOff.Game.RipOffGame(shot, shotPasses);
game.Run();
