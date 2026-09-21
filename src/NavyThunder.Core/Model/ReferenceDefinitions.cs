using System.Text.Json.Serialization;

namespace NavyThunder.Core.Model;

/// <summary>
/// A reference value extracted from War Thunder's public resources (official wiki pages,
/// changelogs, or the community datamine). Reference values are *facts about WT* used to
/// calibrate our engine; they never get loaded into combat entities directly.
/// </summary>
public sealed record WtReferenceEntry
{
    public required string Id { get; init; }
    public required double Value { get; init; }
    public string? Unit { get; init; }

    /// <summary>"wt_datamine" | "wt_official_wiki" | "wt_official_changelog" | "wt_community".</summary>
    public required string SourceKind { get; init; }
    public required string SourceUrl { get; init; }
    public string? RetrievedOn { get; init; }
    public string? Notes { get; init; }
}

public sealed record WtReferenceDocument
{
    public int SchemaVersion { get; init; }

    [JsonIgnore]
    public string Kind => "wtReference";

    public required WtReferenceEntry[] Entries { get; init; }
}

/// <summary>One extracted War Thunder ship unit record (Tier-1 reference data).</summary>
public sealed record WtShipUnitRecord
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Nation { get; init; }
    public double? DisplacementT { get; init; }
    public double? MaxSpeedKnots { get; init; }
    public string? WeaponsSummary { get; init; }
    public string? SourcePath { get; init; }
}

public sealed record WtShipUnitsDocument
{
    public int SchemaVersion { get; init; }

    [JsonIgnore]
    public string Kind => "wtShipUnits";

    public required WtShipUnitRecord[] Ships { get; init; }
}

/// <summary>Main-battery weapon facts measured from the WT client unit + weapon blks.</summary>
public sealed record WtShipWeaponMain
{
    public double CaliberMm { get; init; }
    public int Turrets { get; init; }
    public double? ReloadS { get; init; }
    public double? ShellMassKg { get; init; }
    public double? MuzzleVelMs { get; init; }
    public double? ExplosiveMassKg { get; init; }
    public double? TraverseDegPerS { get; init; }
}

public sealed record WtShipWeaponsRecord
{
    public required string Id { get; init; }
    public WtShipWeaponMain? Main { get; init; }
}

public sealed record WtShipWeaponsDocument
{
    public int SchemaVersion { get; init; }

    [JsonIgnore]
    public string Kind => "wtShipWeapons";

    public required WtShipWeaponsRecord[] Ships { get; init; }
}

/// <summary>
/// A tunable engine parameter whose formula/number is NOT publicly confirmed
/// (approximation:true) plus any adjustable constant. All approximated WT behavior must
/// live here — never hard-coded — so it can be replaced as better evidence arrives (MDR format).
/// </summary>
public sealed record CalibrationEntry
{
    public required string Id { get; init; }
    public required double Value { get; init; }
    public string? Unit { get; init; }

    /// <summary>True when the value approximates an unpublished WT formula.</summary>
    public bool Approximation { get; init; }

    /// <summary>high | medium | low — confidence in this approximation.</summary>
    public string? Confidence { get; init; }

    /// <summary>What is known, and what specifically is unknown.</summary>
    public string? Known { get; init; }
    public string? Unknown { get; init; }

    public string? SourceUrl { get; init; }

    /// <summary>MDR doc that owns this parameter.</summary>
    public string? Mdr { get; init; }
}

public sealed record CalibrationDocument
{
    public int SchemaVersion { get; init; }

    [JsonIgnore]
    public string Kind => "calibration";

    public required CalibrationEntry[] Entries { get; init; }
}
