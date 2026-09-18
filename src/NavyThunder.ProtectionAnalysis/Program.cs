using System.Text.Json;
using System.Text.Json.Serialization;
using NavyThunder.Core.Armor;
using NavyThunder.Core.Protection;
using NavyThunder.Data;
using NavyThunder.Core.Model;

// Protection Analysis CLI.
// Commands:
//   nt-pa summary  [--data <dir>]
//   nt-pa scenario --shell <id> --plate <mm> --angle <deg> [--range <m>] [--data <dir>]
// The scenario command is the seed of the full-chain Protection Analysis report
// (impact speed/angle, penetration, outcome, residual, fuze); ships/crew/flood join in
// Phases 2-3.

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("""
        Navy Thunder — Protection Analysis
        Usage:
          nt-pa summary   [--data <dir>]
          nt-pa scenario  --shell <id> --plate <mm> --angle <deg> [--range <m>] [--data <dir>]
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

var repo = DataRepository.LoadFromDirectory(dataDir);

// TODO(calibration): replaced by de_marre_constant from data/calibration once fitted to
// stat cards (research in flight); default keeps the resolver functional meanwhile.
var calibration = PenetrationCalibration.FromRepository(
    repo.WtReferences.TryGetValue("de_marre_constant_calibrated", out var c) ? c.Value : 1.0);

switch (args[0])
{
    case "summary":
    {
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

    case "scenario":
    {
        string? shellId = null;
        double plate = -1, angle = -1, range = 0;
        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--shell": shellId = args[i + 1]; break;
                case "--plate" when double.TryParse(args[i + 1], out var v): plate = v; break;
                case "--angle" when double.TryParse(args[i + 1], out var v): angle = v; break;
                case "--range" when double.TryParse(args[i + 1], out var v): range = v; break;
            }
        }

        if (shellId is null || !repo.Shells.ContainsKey(shellId) || plate <= 0 || angle < 0)
        {
            Console.Error.WriteLine("scenario requires --shell <id> --plate <mm> --angle <deg> [--range <m>]");
            return 2;
        }

        var shell = repo.RequireShell(shellId);

        double impactSpeed;
        double fallAngleDeg = 0;
        if (range > 0)
        {
            var strike = ProtectionScenario.StrikeAtRange(shell, range);
            impactSpeed = strike.ImpactSpeed;
            fallAngleDeg = strike.ImpactFallAngleDeg;
        }
        else
        {
            impactSpeed = shell.MuzzleVelocityMs;
        }

        var result = ProtectionScenario.ResolveAtImpact(shell, calibration, plate, angle, impactSpeed);

        var report = new Dictionary<string, object?>
        {
            ["shell"] = shellId,
            ["rangeM"] = range,
            ["impactSpeedMs"] = Math.Round(impactSpeed, 2),
            ["fallAngleDeg"] = Math.Round(fallAngleDeg, 2),
            ["plateThicknessMm"] = plate,
            ["requestedAngleDeg"] = angle,
            ["penetrationMm"] = Math.Round(result.PenetrationMm, 1),
            ["effectiveThicknessMm"] = Math.Round(result.EffectiveThicknessMm, 1),
            ["outcome"] = result.Outcome.ToString().ToLowerInvariant(),
            ["fuzeTriggered"] = result.FuzeTriggered,
            ["overmatchApplied"] = result.OvermatchApplied,
            ["ricochetProbability"] = Math.Round(ProtectionScenario.RicochetProbability(calibration, angle), 3),
            ["residualEnergyFraction"] = Math.Round(result.ResidualEnergyFraction, 3),
        };

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };
        Console.WriteLine(JsonSerializer.Serialize(report, jsonOptions));
        return 0;
    }

    default:
        Console.Error.WriteLine($"Unknown command '{args[0]}'. See --help.");
        return 2;
}

