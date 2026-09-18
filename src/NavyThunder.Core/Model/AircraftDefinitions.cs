namespace NavyThunder.Core.Model;

public enum AircraftSection
{
    Fuselage,
    LeftWing,
    RightWing,
    Tail,
}

public enum AircraftPartKind
{
    Airframe,
    WingSpar,
    ControlSurface,
    ControlCable,
    Hydraulics,
    FuelTank,
    OilSystem,
    Radiator,
    Engine,
    Pilot,
    AmmoRack,
    LandingGear,
}

/// <summary>One damageable aircraft part (MDR-0013 module list) as an axis-aligned box.</summary>
public sealed record AircraftPartDefinition
{
    public required string Id { get; init; }
    public required AircraftPartKind Kind { get; init; }
    public required AircraftSection Section { get; init; }

    public required double Hp { get; init; }

    /// <summary>Skin thickness (mm) of the part's box — plates are generated from it.</summary>
    public required double SkinThicknessMm { get; init; }

    public required double XMinM { get; init; }
    public required double XMaxM { get; init; }
    public required double YMinM { get; init; }
    public required double YMaxM { get; init; }
    public required double ZMinM { get; init; }
    public required double ZMaxM { get; init; }

    /// <summary>Fuel tanks only: self-sealing tanks resist ignition (MDR-0013).</summary>
    public bool SelfSealing { get; init; }
}

public sealed record AircraftDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }

    public required double EmptyMassKg { get; init; }
    public required double FuelKg { get; init; }

    /// <summary>Design G of the wing spars at full health (Critical G baseline).</summary>
    public required double DesignG { get; init; }

    public required AircraftPartDefinition[] Parts { get; init; }
}

public sealed record AircraftSetDocument
{
    public int SchemaVersion { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string Kind => "aircraftSet";

    public required AircraftDefinition[] Aircraft { get; init; }
}
