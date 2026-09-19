using System.Text.Json;
using System.Text.Json.Serialization;
using NavyThunder.Core.Armor;
using NavyThunder.Core.Armor;
using NavyThunder.Core.Protection;
using NavyThunder.Data;

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
          nt-pa report    [--data <dir>]   calibration report vs WT stat-card references
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
var calibration = repo.ToPenetrationCalibration();

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
                case "--plate" when double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out var v): plate = v; break;
                case "--angle" when double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out var v): angle = v; break;
                case "--range" when double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out var v): range = v; break;
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
            impactSpeed = strike.Trajectory.ImpactSpeed;
            fallAngleDeg = strike.Trajectory.ImpactFallAngleDeg;
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

    case "report":
    {
        // Calibration report: every *_pen_0deg_* reference value is recomputed through
        // the full chain (ballistics strike velocity -> de Marre) and compared against
        // the official table with a per-shell tolerance. Per-shell tolerances are
        // declared in the report itself (MDR-0001/0002).
        var shellByPrefix = new Dictionary<string, (string Id, double Tol)>
        {
            ["mk8"] = ("usn_406mm_mk8_mod6_apcbc", 0.03),
            ["mk13"] = ("usn_406mm_mk13_hc", 0.12),
            ["mk46"] = ("usn_127mm_mk46_special_common", 0.07),
        };

        var entries = new List<object>();
        int passed = 0, total = 0;
        foreach (var (key, refEntry) in repo.WtReferences.Where(r => r.Key.EndsWith("_pen_0deg_1000m", StringComparison.Ordinal)
                     || r.Key.EndsWith("_pen_0deg_2500m", StringComparison.Ordinal)
                     || r.Key.EndsWith("_pen_0deg_5000m", StringComparison.Ordinal)
                     || r.Key.EndsWith("_pen_0deg_7500m", StringComparison.Ordinal)
                     || r.Key.EndsWith("_pen_0deg_10000m", StringComparison.Ordinal)
                     || r.Key.EndsWith("_pen_0deg_15000m", StringComparison.Ordinal)))
        {
            var prefix = shellByPrefix.FirstOrDefault(kv => key.StartsWith(kv.Key, StringComparison.Ordinal));
            if (prefix.Key is null || !repo.Shells.ContainsKey(prefix.Value.Id))
            {
                continue;
            }

            string marker = "_pen_0deg_";
            int m0 = key.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            double range = double.Parse(key[m0..^1], System.Globalization.CultureInfo.InvariantCulture); // strip trailing 'm'
            var shell = repo.RequireShell(prefix.Value.Id);
            double computed = NavyThunder.Core.Armor.DeMarre.PenetrationMm(shell,
                ProtectionScenario.ImpactSpeedAtRange(shell, range));
            double expected = refEntry.Value;
            double deviation = (computed - expected) / expected;
            bool ok = Math.Abs(deviation) <= prefix.Value.Tol;
            total++;
            passed += ok ? 1 : 0;
            entries.Add(new Dictionary<string, object?>
            {
                ["reference"] = key,
                ["shell"] = shell.Id,
                ["rangeM"] = range,
                ["expectedMm"] = expected,
                ["computedMm"] = Math.Round(computed, 1),
                ["deviationPct"] = Math.Round(deviation * 100, 2),
                ["tolerancePct"] = Math.Round(prefix.Value.Tol * 100, 1),
                ["pass"] = ok,
            });
        }

        var report = new Dictionary<string, object?>
        {
            ["touchedReferenceVersion"] = "WT 2.59 era stat cards (Iowa wiki page, 2026-09-19)",
            ["total"] = total,
            ["passed"] = passed,
            ["passRatePct"] = total == 0 ? 0 : Math.Round(passed * 100.0 / total, 1),
            ["toleranceStatement"] = "0-deg penetration vs official range table: Mk8 ±3% (all six ranges), Mk46 ±7%, Mk13 HE ±12% (noisier HE table, Phase 7 refinement)",
            ["entries"] = entries,
        };
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
        var reportJson = JsonSerializer.Serialize(report, jsonOptions);
        File.WriteAllText("docs/calibration-report.json", reportJson);
        Console.WriteLine(reportJson);
        Console.Error.WriteLine($"calibration: {passed}/{total} passed -> docs/calibration-report.json");
        return passed == total ? 0 : 1;
    }

    default:
        Console.Error.WriteLine($"Unknown command '{args[0]}'. See --help.");
        return 2;
}

