using NavyThunder.Data;

// Protection Analysis CLI — Phase 0: dataset summary only.
// Phase 1 adds the scenario command:
//   nt-pa scenario --shell <id> --target <shipId> --distance <m> --angle <deg> --hit <location>
// producing the full-chain report (impact speed/angle, LoS, penetration, residual energy,
// fragments, modules, crew, fire, flooding, final state).

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("""
        Navy Thunder — Protection Analysis
        Usage:
          nt-pa summary [--data <dir>]   Load the dataset and print a summary
        """);
    return 0;
}

string dataDir = RepoLocator.FindDataDirectory();
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--data")
    {
        dataDir = args[i + 1];
    }
}

switch (args[0])
{
    case "summary":
    {
        var repo = DataRepository.LoadFromDirectory(dataDir);
        Console.WriteLine($"Data directory: {dataDir}");
        Console.WriteLine($"  shells:       {repo.Shells.Count}");
        foreach (var shell in repo.Shells.Values.OrderBy(s => s.Id))
        {
            Console.WriteLine($"    - {shell.Id}: {shell.DisplayName} [{shell.Category}] " +
                              $"{shell.CaliberMm}mm {shell.MassKg}kg @ {shell.MuzzleVelocityMs} m/s " +
                              $"K={shell.DemarrePenetrationK} fuze={shell.FuseDelayS}s/{shell.ExplodeThresholdMm}mm" +
                              (shell.ProximityFuse is { } pf ? $" VT(r={pf.RadiusM}m arm={pf.ArmDistanceM}m)" : ""));
        }

        Console.WriteLine($"  torpedoes:    {repo.Torpedoes.Count}");
        foreach (var torpedo in repo.Torpedoes.Values.OrderBy(t => t.Id))
        {
            Console.WriteLine($"    - {torpedo.Id}: {torpedo.DisplayName} " +
                              $"{torpedo.WarheadMassKg}kg @ {torpedo.SpeedMs * 1.94384:0.#} kn, range {torpedo.RangeM} m, " +
                              $"depth {torpedo.RunningDepthM} m, arm {torpedo.ArmDistanceM} m");
        }

        Console.WriteLine($"  wtReferences: {repo.WtReferences.Count}");
        Console.WriteLine($"  calibration:  {repo.Calibration.Count} " +
                          $"({repo.Calibration.Values.Count(c => c.Approximation)} marked as approximations)");
        return 0;
    }

    default:
        Console.Error.WriteLine($"Unknown command '{args[0]}'. See --help.");
        return 2;
}
