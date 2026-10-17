using System;
using System.Collections.Generic;
using System.Globalization;
using RipOff.Core;

// See NextVersion.csproj for usage.
if (args.Length >= 1 && args[0] == "check")
{
    if (args.Length == 2 && ReleaseVersion.TryParse(args[1], out var v)) { Console.WriteLine(v); return 0; }
    Console.Error.WriteLine($"not a release version YYYY.MM.DD.N: {(args.Length > 1 ? args[1] : "(none)")}");
    return 1;
}
if (args.Length >= 1 && args[0] == "next")
{
    DateOnly date = ReleaseVersion.Today();
    var tags = new List<string>();
    for (int i = 1; i < args.Length; i++)
    {
        if (args[i] == "--date" && i + 1 < args.Length)
        {
            if (!DateOnly.TryParseExact(args[++i], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            { Console.Error.WriteLine("bad --date (want YYYY-MM-DD): " + args[i]); return 1; }
        }
        else tags.AddRange(args[i].Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries));
    }
    Console.WriteLine(ReleaseVersion.Next(date, tags));
    return 0;
}
Console.Error.WriteLine("usage: NextVersion next [--date YYYY-MM-DD] [tag ...] | check <version>");
return 2;
