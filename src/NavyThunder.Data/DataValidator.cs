using NavyThunder.Data.Schema;

namespace NavyThunder.Data;

/// <summary>
/// Hard rules every document must satisfy. Rules encode physics sanity and engine
/// invariants (e.g. VT shells must carry a proximity fuse); soft historical checks do
/// not belong here.
/// </summary>
public static class DataValidator
{
    public static void Validate(ShellDefinition shell, IList<string> errors)
    {
        if (string.IsNullOrWhiteSpace(shell.Id))
        {
            errors.Add("shell: Id is required");
        }

        if (shell.CaliberMm <= 0)
        {
            errors.Add($"shell '{shell.Id}': CaliberMm must be > 0");
        }

        if (shell.MassKg <= 0)
        {
            errors.Add($"shell '{shell.Id}': MassKg must be > 0");
        }

        if (shell.MuzzleVelocityMs <= 0)
        {
            errors.Add($"shell '{shell.Id}': MuzzleVelocityMs must be > 0");
        }

        if (shell.ExplosiveMassKg < 0)
        {
            errors.Add($"shell '{shell.Id}': ExplosiveMassKg must be >= 0");
        }

        if (shell.ExplosiveMassKg > shell.MassKg)
        {
            errors.Add($"shell '{shell.Id}': ExplosiveMassKg cannot exceed MassKg");
        }

        if (shell.FuseDelayS < 0)
        {
            errors.Add($"shell '{shell.Id}': FuseDelayS must be >= 0");
        }

        if (shell.ExplodeThresholdMm < 0)
        {
            errors.Add($"shell '{shell.Id}': ExplodeThresholdMm must be >= 0");
        }

        if (shell.DemarrePenetrationK < 0)
        {
            errors.Add($"shell '{shell.Id}': DemarrePenetrationK must be >= 0");
        }

        if (shell.Category is ShellCategory.AAVT && shell.ProximityFuse is null)
        {
            errors.Add($"shell '{shell.Id}': VT shells must define ProximityFuse");
        }

        if (shell.ProximityFuse is { } pf)
        {
            if (pf.RadiusM <= 0)
            {
                errors.Add($"shell '{shell.Id}': ProximityFuse.RadiusM must be > 0");
            }

            if (pf.ArmDistanceM <= 0)
            {
                errors.Add($"shell '{shell.Id}': ProximityFuse.ArmDistanceM must be > 0");
            }
        }
    }

    public static void Validate(TorpedoDefinition torpedo, IList<string> errors)
    {
        if (string.IsNullOrWhiteSpace(torpedo.Id))
        {
            errors.Add("torpedo: Id is required");
        }

        if (torpedo.MassKg <= 0)
        {
            errors.Add($"torpedo '{torpedo.Id}': MassKg must be > 0");
        }

        if (torpedo.WarheadMassKg <= 0)
        {
            errors.Add($"torpedo '{torpedo.Id}': WarheadMassKg must be > 0");
        }

        if (torpedo.SpeedMs <= 0)
        {
            errors.Add($"torpedo '{torpedo.Id}': SpeedMs must be > 0");
        }

        if (torpedo.RangeM <= 0)
        {
            errors.Add($"torpedo '{torpedo.Id}': RangeM must be > 0");
        }

        if (torpedo.RunningDepthM <= 0)
        {
            errors.Add($"torpedo '{torpedo.Id}': RunningDepthM must be > 0");
        }

        if (torpedo.ArmDistanceM <= 0)
        {
            errors.Add($"torpedo '{torpedo.Id}': ArmDistanceM must be > 0");
        }
    }

    public static void Validate(WtReferenceEntry entry, IList<string> errors)
    {
        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            errors.Add("wtReference: Id is required");
        }

        if (string.IsNullOrWhiteSpace(entry.SourceUrl))
        {
            errors.Add($"wtReference '{entry.Id}': SourceUrl is required (values without provenance are rejected)");
        }

        if (string.IsNullOrWhiteSpace(entry.SourceKind))
        {
            errors.Add($"wtReference '{entry.Id}': SourceKind is required");
        }
    }

    public static void Validate(CalibrationEntry entry, IList<string> errors)
    {
        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            errors.Add("calibration: Id is required");
        }

        if (entry.Approximation && string.IsNullOrWhiteSpace(entry.SourceUrl) && string.IsNullOrWhiteSpace(entry.Known))
        {
            errors.Add($"calibration '{entry.Id}': approximations must document SourceUrl or Known basis");
        }
    }
}
