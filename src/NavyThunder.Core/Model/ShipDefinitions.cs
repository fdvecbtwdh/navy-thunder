namespace NavyThunder.Core.Model;

public enum ShipClass
{
    SmallCraft,   // any hull section destroyed = sunk (WT Hornet's Sting rules)
    Destroyer,
    Frigate,      // frigate and above: loss of unsinkability needs >= 2 mid sections
    Cruiser,
    Battlecruiser,
    Battleship,
    Carrier,
}

public static class ShipClassRules
{
    /// <summary>Frigate and above use the capital-ship unsinkability rule (MDR-0007).</summary>
    public static bool IsCapital(this ShipClass shipClass) => shipClass >= ShipClass.Frigate;
}

public enum HullSectionRole
{
    Bow,
    Mid,
    Stern,
}

public sealed record HullSectionDefinition
{
    public required string Id { get; init; }
    public HullSectionRole Role { get; init; } = HullSectionRole.Mid;
    public required double Hp { get; init; }

    /// <summary>Longitudinal extent along the keel (ship-local Z, bow +Z — Phase 01
    /// convention); damage events are routed to sections by Z.</summary>
    public double ZMinM { get; init; }
    public double ZMaxM { get; init; }
}

public enum PartKind
{
    Compartment,
    Magazine,
    FuelTank,
    Engine,
    Boiler,
    Turbine,
    Steering,
    FireControl,
    Radar,
    Pump,
    Turret,
    Hoist,
    TorpedoTube,
    ReadyRack,
    Funnel,
    AntiTorpedo,
}

/// <summary>
/// One damageable ship part (compartment or module). Parts are axis-aligned boxes in
/// ship-local space: Z = longitudinal (bow +), Y = vertical (waterline 0), X = lateral
/// (starboard +; PROJECT_DESIGN §8.2). Crew convert damage via the official 125 HP/crew
/// anchor (wtReference crew_hp_per_member).
/// </summary>
public sealed record ShipPartDefinition
{
    public required string Id { get; init; }
    public required PartKind Kind { get; init; }

    /// <summary>Hull section this part belongs to (structure + unsinkability linkage).</summary>
    public required string SectionId { get; init; }

    public required double Hp { get; init; }

    /// <summary>Crew stationed in this part (0 for unmanned machinery).</summary>
    public int Crew { get; init; }

    public required double XMinM { get; init; }
    public required double XMaxM { get; init; }
    public required double YMinM { get; init; }
    public required double YMaxM { get; init; }
    public required double ZMinM { get; init; }
    public required double ZMaxM { get; init; }

    /// <summary>Open parts (turrets, AA mounts, rangefinders) are the ONLY parts whose
    /// crews are subject to overpressure on ships (MDR-0005).</summary>
    public bool Open { get; init; }

    /// <summary>Share of total buoyancy (%) lost when this part floods; sums to 100 per ship.</summary>
    public double BuoyancySharePct { get; init; }

    /// <summary>Turret group id for turrets/hoists/ready racks (first-stage ammo bookkeeping).</summary>
    public string? TurretGroup { get; init; }

    /// <summary>
    /// Fraction (0..1) of hydroShock damage this part absorbs for the interior behind it
    /// (Phase 04 TDS, MDR-0012: geometry + compartment absorption layer; NEVER applies to
    /// underwater AP — that travels on the kinetic channel). AntiTorpedo parts only.
    /// </summary>
    public double HydroShockAbsorptionPct { get; init; }
}

/// <summary>
/// One data-driven AA battery group (Phase 04, MDR-0014): the runtime spawns one
/// AAMount per authored entry. AA is an independent dps channel — no penetration math.
/// </summary>
public sealed record AaMountDefinition
{
    public required string ShellId { get; init; }

    /// <summary>How many physical mounts this entry expands to at battle build.</summary>
    public int Count { get; init; } = 1;

    public required double RangeM { get; init; }
    public required double MuzzleVelocityMs { get; init; }
    public double RoundsPerMinute { get; init; } = 60;

    /// <summary>Barrage dispersion (milliradians; crude on purpose, MDR-0014).</summary>
    public double HorizontalMrad { get; init; } = 15.0;
    public double VerticalMrad { get; init; } = 12.0;
}

public sealed record ShipDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required ShipClass Class { get; init; }

    public required double DisplacementT { get; init; }
    public required double LengthM { get; init; }
    public required double BeamM { get; init; }
    public required double DraftM { get; init; }

    /// <summary>Total crew; must be >= the crew authored on parts.</summary>
    public required int CrewTotal { get; init; }

    /// <summary>Below this alive-crew count no damage control can run (WT repair threshold).</summary>
    public required int CrewRepairThreshold { get; init; }

    /// <summary>Below this alive-crew count the ship is scuttled (WT minimum surviving crew).</summary>
    public required int CrewSurviveThreshold { get; init; }

    public required HullSectionDefinition[] HullSections { get; init; }
    public required ShipPartDefinition[] Parts { get; init; }

    /// <summary>First-stage (ready-use) rounds per turret group.</summary>
    public int FirstStageRoundsPerTurret { get; init; } = 30;

    /// <summary>Resupply duration from main magazine (community: ~30-40 s, paused while firing).</summary>
    public double ResupplySeconds { get; init; } = 35.0;

    /// <summary>Damage-control generation: newer ships handle DC slightly better (Spearhead).</summary>
    public int DcGeneration { get; init; } = 1;

    /// <summary>Water level fraction pumped out per second by the full pump outfit.</summary>
    public double PumpCapacityPerSecond { get; init; } = 0.02;

    /// <summary>List angle at which the ship capsizes (approximation, calibration).</summary>
    public double CapsizeAngleDeg { get; init; } = 40.0;

    public ArmorPlateDefinition[] ArmorPlates { get; init; } = [];

    // ---------------- mobility (R0.2) ----------------

    /// <summary>Design full speed in knots (data; AI/throttle target = this × throttle × engine health).</summary>
    public double MaxSpeedKnots { get; init; } = 30.0;

    /// <summary>Rudder-hard turn rate at cruising speed (deg/s, data).</summary>
    public double TurnRateDegPerS { get; init; } = 2.0;

    /// <summary>Acceleration fraction of max speed per second (0..1, approximation).</summary>
    public double AccelerationFactor { get; init; } = 0.06;

    // ---------------- weapons (R0.1) ----------------

    public NavalGunDefinition[] Guns { get; init; } = [];

    /// <summary>Data-driven AA battery (Phase 04): empty = the ship carries no AA mounts.</summary>
    public AaMountDefinition[] AaMounts { get; init; } = [];
}

/// <summary>One naval gun mount: linked to its turret-group parts (reload/ammo/destruction).</summary>
public sealed record NavalGunDefinition
{
    public required string Id { get; init; }
    public required string TurretGroup { get; init; }

    public required string ShellId { get; init; }

    /// <summary>Optional alternate shell (HE) the crew can switch to (R3 player toggle).</summary>
    public string? HeShellId { get; init; }

    public required int Barrels { get; init; }

    /// <summary>Cyclic rate per barrel (rounds/minute) with ready-rack supply.</summary>
    public required double RoundsPerMinute { get; init; }

    public double RangeM { get; init; } = 30000.0;

    /// <summary>Turret traverse speed (deg/s); guns only fire when on target.</summary>
    public double TraverseDegPerS { get; init; } = 6.0;

    public double HorizontalMrad { get; init; } = 2.0;
    public double VerticalMrad { get; init; } = 1.5;
}

public enum BoxFace
{
    XMin,
    XMax,
    YMin,
    YMax,
    ZMin,
    ZMax,
}

/// <summary>
/// One armor plate on the ship, authored as the face of an axis-aligned box region
/// (e.g. the citadel side belt = XMin or XMax face of the citadel box).
/// </summary>
public sealed record ArmorPlateDefinition
{
    public required string Id { get; init; }
    public required double XMinM { get; init; }
    public required double XMaxM { get; init; }
    public required double YMinM { get; init; }
    public required double YMaxM { get; init; }
    public required double ZMinM { get; init; }
    public required double ZMaxM { get; init; }
    public required BoxFace Face { get; init; }
    public required double ThicknessMm { get; init; }
}

public sealed record ShipSetDocument
{
    public int SchemaVersion { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string Kind => "shipSet";

    public required ShipDefinition[] Ships { get; init; }
}
