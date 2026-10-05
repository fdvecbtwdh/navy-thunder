using System.Text.Json;
using System.Text.Json.Serialization;

namespace NavyThunder.Core.Builder;

/// <summary>
/// BuilderDesign v1 (PHASE_06 §5.1): the editor's intermediate format. Users author
/// THIS, never a ShipDefinition — every derived value (weight, draft, displacement,
/// sections) is recomputed by the deterministic compiler on load (NA lesson: derived
/// values never persist, PROJECT_DESIGN §10.2). Coordinates are ship-local with the
/// Phase 01 convention: +Z bow, +Y up (waterline 0), +X starboard, metres.
/// </summary>
public sealed record BuilderDesign
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    [JsonIgnore]
    public string Kind => "builderDesign";

    public DesignMeta Meta { get; init; } = new();
    public List<HullBlock> HullBlocks { get; init; } = [];
    public List<ArmorSlab> ArmorSlabs { get; init; } = [];
    public List<PartPlacement> Parts { get; init; } = [];
    public List<GunMountDesign> Guns { get; init; } = [];

    /// <summary>Optional mobility overrides; compiler derives defaults when null.</summary>
    public double? MaxSpeedKnots { get; init; }
    public double? TurnRateDegPerS { get; init; }

    public static BuilderDesign Load(string path)
    {
        var doc = JsonSerializer.Deserialize<BuilderDesign>(File.ReadAllText(path), JsonOpts)
                  ?? throw new InvalidDataException($"design {path} deserialized to null");
        DesignMigrator.Migrate(doc);
        return doc;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));
    }

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed record DesignMeta
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";
}

/// <summary>
/// One hull block: an axis-aligned full-beam slab (NA AdjustableHull in its simplest
/// box form). WidthFrac (0..1) scales the slab's half-beam so tapered bows/sterns are
/// buildable; MirrorX is reserved for asymmetric future blocks (v1 renders both sides
/// symmetric — naval hulls are mirror-symmetric by construction).
/// </summary>
public sealed record HullBlock
{
    public required string Guid { get; init; }
    public required double ZMinM { get; init; }
    public required double ZMaxM { get; init; }

    /// <summary>Bottom/top relative to the waterline (y=0); bottoms may go below 0 (hull below water).</summary>
    public required double YBottomM { get; init; }
    public required double YTopM { get; init; }

    /// <summary>Half-beam fraction (0..1] of the design's widest block — the compiler
    /// normalizes to absolute metres so a single wide reference block sets the beam.</summary>
    public double WidthFrac { get; init; } = 1.0;

    public bool MirrorX { get; init; } = true;
}

/// <summary>An explicit armor region: a box + which face carries the plate (PHASE_06
/// keeps NT's explicit-plate concept, NOT NA's per-part scalar).</summary>
public sealed record ArmorSlab
{
    public required string Guid { get; init; }
    public required double XMinM { get; init; }
    public required double XMaxM { get; init; }
    public required double YMinM { get; init; }
    public required double YMaxM { get; init; }
    public required double ZMinM { get; init; }
    public required double ZMaxM { get; init; }
    public required string Face { get; init; } // "XMin"|"XMax"|"YMin"|"YMax"|"ZMin"|"ZMax"
    public required double ThicknessMm { get; init; }
}

/// <summary>A part instance. Box in ship-local metres; kind = PartKind name. HP/crew
/// default from the compiler's per-kind table; author may override (NA overrideWeight
/// escape hatch, kept minimal).</summary>
public sealed record PartPlacement
{
    public required string Guid { get; init; }
    public required string Kind { get; init; }
    public required double XMinM { get; init; }
    public required double XMaxM { get; init; }
    public required double YMinM { get; init; }
    public required double YMaxM { get; init; }
    public required double ZMinM { get; init; }
    public required double ZMaxM { get; init; }

    public string? TurretGroup { get; init; }
    public int? Crew { get; init; }
    public double? Hp { get; init; }
    public bool Open { get; init; }
}

/// <summary>A gun mount authored on a turret group (shell ids reference data/shells).</summary>
public sealed record GunMountDesign
{
    public required string Guid { get; init; }
    public required string TurretGroup { get; init; }
    public required string ShellId { get; init; }
    public string? HeShellId { get; init; }
    public int Barrels { get; init; } = 1;
    public double RoundsPerMinute { get; init; } = 20;
    public double RangeM { get; init; } = 15000;
    public double TraverseDegPerS { get; init; } = 8;
}

/// <summary>v1 has a single schema version; the migrator exists from day one (NA
/// no-version lesson, PHASE_06 §8) and throws on future/unknown versions.</summary>
public static class DesignMigrator
{
    public static void Migrate(BuilderDesign design)
    {
        if (design.SchemaVersion > BuilderDesign.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"design schema v{design.SchemaVersion} is newer than this build supports (v{BuilderDesign.CurrentSchemaVersion})");
        }
        // v1 → current: identity (add per-version steps here as the schema evolves).
    }
}
