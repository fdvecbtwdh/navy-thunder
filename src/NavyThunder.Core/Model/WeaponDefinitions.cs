using System.Text.Json.Serialization;

namespace NavyThunder.Core.Model;

/// <summary>Provenance for every data entry: where a value came from and when it was captured.</summary>
public sealed record SourceInfo
{
    /// <summary>"wt_datamine" | "wt_official" | "historical" | "hand_authored" | "community_test".</summary>
    public required string Origin { get; init; }

    public string? Url { get; init; }

    /// <summary>E.g. "WT 2.59 datamine 2026-09" or "NavalArt 1.53 2026-09".</summary>
    public string? VersionStamp { get; init; }

    public string? RetrievedOn { get; init; }

    public string? Notes { get; init; }
}

public enum ShellCategory
{
    Unknown,
    AP,
    APC,
    APBC,
    APCBC,
    SAP,
    Common,
    SpecialCommon,
    HE,
    AACommon,
    AAVT,
}

public sealed record ProximityFuseDefinition
{
    /// <summary>Detonation trigger distance to an air target (m), e.g. 23 for 127mm Mk31.</summary>
    public required double RadiusM { get; init; }

    /// <summary>Distance flown before the fuse arms (m), e.g. 457 for 127mm Mk31.</summary>
    public required double ArmDistanceM { get; init; }

    public bool AirTargetsOnly { get; init; } = true;
}

public sealed record ShellDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public ShellCategory Category { get; init; } = ShellCategory.Unknown;

    public required double CaliberMm { get; init; }
    public required double MassKg { get; init; }
    public required double MuzzleVelocityMs { get; init; }

    public string? ExplosiveType { get; init; }
    public double ExplosiveMassKg { get; init; }

    /// <summary>Base fuze delay after passing a plate thick enough to trigger (s).</summary>
    public double FuseDelayS { get; init; }

    /// <summary>Minimum plate thickness (mm) that triggers the fuze; thinner plates = dud/pass-through.</summary>
    public double ExplodeThresholdMm { get; init; }

    /// <summary>Per-shell de Marre coefficient K (WT datamine demarrePenetrationK).</summary>
    public double DemarrePenetrationK { get; init; }

    public double? DragCoefficient { get; init; }

    /// <summary>WT datamine ballisticsModel tag, e.g. "LAW_1943" or "ADVANCED_DYNAMIC_KV" (informational).</summary>
    public string? BallisticsModel { get; init; }

    public ProximityFuseDefinition? ProximityFuse { get; init; }

    public SourceInfo? Source { get; init; }
}

public sealed record ShellSetDocument
{
    public int SchemaVersion { get; init; }

    [JsonIgnore]
    public string Kind => "shellSet";

    public required ShellDefinition[] Shells { get; init; }
}

public sealed record TorpedoDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }

    public required double MassKg { get; init; }
    public required double WarheadMassKg { get; init; }
    public required double SpeedMs { get; init; }

    /// <summary>Max range (m), e.g. 20000 for Type 93.</summary>
    public required double RangeM { get; init; }

    /// <summary>Fixed running depth (m); WT naval torpedoes run at a fixed depth (contact-fuze only).</summary>
    public required double RunningDepthM { get; init; }

    public required double ArmDistanceM { get; init; }

    /// <summary>Surface breach patch radius range (m) driving flooding, e.g. [4, 12] for Type 93.</summary>
    public double[]? ExplosionPatchRadiusM { get; init; }

    public SourceInfo? Source { get; init; }
}

public sealed record TorpedoSetDocument
{
    public int SchemaVersion { get; init; }

    [JsonIgnore]
    public string Kind => "torpedoSet";

    public required TorpedoDefinition[] Torpedoes { get; init; }
}
