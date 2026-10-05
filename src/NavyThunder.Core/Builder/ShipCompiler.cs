using NavyThunder.Core.Model;

namespace NavyThunder.Core.Builder;

/// <summary>Compile diagnostics: Errors refuse compilation, Warnings pass through
/// (PHASE_06 §5.2 — "不合理设计给校验错误而非战斗中崩溃").</summary>
public sealed record CompileIssue(string Severity, string Code, string Message)
{
    public override string ToString() => $"{Severity} {Code}: {Message}";
}

public sealed record DerivedQuantities
{
    /// <summary>Σ hull-block displaced volume at full hull height (m³) — the buoyancy pool.</summary>
    public required double HullVolumeM3 { get; init; }
    /// <summary>Volume below the static waterline (m³) = displacement in tonnes (ρ=1).</summary>
    public required double DisplacedVolumeM3 { get; init; }
    public required double StaticDraftM { get; init; }
    public required double WeightT { get; init; }
    /// <summary>Reserve buoyancy: (hull volume − displaced volume) / displacement. The
    /// life-or-death pool (MDR-0008 alignment), reported for the editor panel.</summary>
    public required double ReserveBuoyancyFrac { get; init; }
    public required double LengthM { get; init; }
    public required double BeamM { get; init; }
    public required (double X, double Y, double Z) CenterOfMass { get; init; }
    public required (double X, double Y, double Z) CenterOfBuoyancy { get; init; }
}

public sealed record CompileResult
{
    public required bool Ok { get; init; }
    public required List<CompileIssue> Issues { get; init; }
    public ShipDefinition? Ship { get; init; }
    public DerivedQuantities? Derived { get; init; }
}

/// <summary>
/// Deterministic BuilderDesign → ShipDefinition compiler (PHASE_06 §5.2, PROJECT_DESIGN
/// §10.3). Pure function: same input bytes → same output bytes; no RNG, no wall clock.
/// Derived rules: weight = Σ volume×density with density = armor×0.008 clamped (NA rule),
/// static draft by 1D volume-height ladder inversion, life pool = reserve buoyancy.
/// </summary>
public static class ShipCompiler
{
    // ---- calibration-tagged constants (tunable, surfaced to the editor panel) ----

    /// <summary>Hull steel effective density (t/m³) including structure/fitting margin.</summary>
    public const double HullDensityTPerM3 = 0.55;
    /// <summary>NA part-density rule: density = armorMm × 0.008, clamped (NAVALART §3).</summary>
    public const double PartDensityPerArmorMm = 0.008;
    public const double PartDensityMin = 0.1;
    public const double PartDensityMax = 1.2;
    /// <summary>Default armor for parts without an explicit slab (NA default 50 mm).</summary>
    public const double DefaultPartArmorMm = 50.0;
    /// <summary>Seaworthiness check: Froude-ish wake limit √L(m)×2.43 kn (NA check only).</summary>
    public const double WakeSpeedFactor = 2.43;
    /// <summary>Reserve buoyancy below this fraction refuses to float a fight (ErrBuoyancy).</summary>
    public const double MinReserveBuoyancyFrac = 0.15;

    private static readonly Dictionary<string, (double HpPerM3, int CrewPerPart, bool Open)> PartDefaults =
        new()
        {
            // kind: hp scales with box volume; crew per installed part (125 HP/crew anchor lives in Ship)
            ["Compartment"] = (40.0, 12, false),
            ["Magazine"] = (80.0, 6, false),
            ["FuelTank"] = (30.0, 0, false),
            ["Engine"] = (60.0, 4, false),
            ["Boiler"] = (60.0, 6, false),
            ["Turbine"] = (60.0, 4, false),
            ["Steering"] = (50.0, 4, false),
            ["FireControl"] = (30.0, 6, true),
            ["Radar"] = (20.0, 2, true),
            ["Pump"] = (25.0, 2, false),
            ["Turret"] = (100.0, 24, true),
            ["Hoist"] = (40.0, 6, false),
            ["TorpedoTube"] = (50.0, 8, true),
            ["ReadyRack"] = (30.0, 4, false),
            ["Funnel"] = (20.0, 0, true),
            ["AntiTorpedo"] = (40.0, 0, false),
        };

    public static CompileResult Compile(BuilderDesign design)
    {
        var issues = new List<CompileIssue>();
        if (design.HullBlocks.Count == 0)
        {
            issues.Add(new CompileIssue("Error", "ErrNoHull", "design has no hull blocks"));
        }

        double refHalfBeam = 0;
        foreach (var b in design.HullBlocks)
        {
            if (b.ZMaxM <= b.ZMinM || b.YTopM <= b.YBottomM)
            {
                issues.Add(new CompileIssue("Error", "ErrBlockDegenerate",
                    $"hull block {b.Guid}: empty extent (z {b.ZMinM}..{b.ZMaxM}, y {b.YBottomM}..{b.YTopM})"));
            }

            if (b.WidthFrac is <= 0 or > 1)
            {
                issues.Add(new CompileIssue("Error", "ErrBlockWidth",
                    $"hull block {b.Guid}: widthFrac {b.WidthFrac} outside (0..1]"));
            }

            refHalfBeam = Math.Max(refHalfBeam, b.WidthFrac);
        }

        if (refHalfBeam <= 0)
        {
            issues.Add(new CompileIssue("Error", "ErrNoBeam", "no hull block defines a beam"));
        }

        if (issues.Any(i => i.Severity == "Error"))
        {
            return new CompileResult { Ok = false, Issues = issues };
        }

        // ---- hull volume / ladder (deterministic order: sort by YBottom, tie-break by Guid) ----
        var blocks = design.HullBlocks
            .OrderBy(b => b.YBottomM).ThenBy(b => b.Guid, StringComparer.Ordinal)
            .ToList();
        double zMin = design.HullBlocks.Min(b => b.ZMinM);
        double zMax = design.HullBlocks.Max(b => b.ZMaxM);
        double length = zMax - zMin;
        double halfBeamM = refHalfBeam * length * 0.12; // widest block: beam = 12% of length (NA-ish ratio, calibration)
        if (halfBeamM < 2)
        {
            halfBeamM = 2;
        }

        double hullVolume = 0;
        foreach (var b in blocks)
        {
            double widthFrac = b.WidthFrac;
            // Volume of a symmetric slab with half-beam tapering linearly to 60% at the
            // ends (rough hull form factor; deterministic constant, calibration-tagged).
            double formFactor = 0.8 + 0.2 * widthFrac;
            hullVolume += (b.ZMaxM - b.ZMinM) * (b.YTopM - b.YBottomM) * 2 * halfBeamM * widthFrac * formFactor;
        }

        // ---- weight: hull steel + parts (NA density = armor×0.008 clamp) ----
        double weight = hullVolume * HullDensityTPerM3;
        foreach (var p in design.Parts)
        {
            if (!PartDefaults.ContainsKey(p.Kind))
            {
                issues.Add(new CompileIssue("Error", "ErrPartKind", $"part {p.Guid}: unknown kind '{p.Kind}'"));
                continue;
            }

            double vol = Math.Max(0, (p.XMaxM - p.XMinM)) * Math.Max(0, (p.YMaxM - p.YMinM)) *
                         Math.Max(0, (p.ZMaxM - p.ZMinM));
            // Parts displace hull volume: their weight ADDS but their buoyancy is the
            // hull's (they sit inside it) — model as added weight at part density.
            weight += vol * PartDensityPerArmorMmMmToDensity(DefaultPartArmorMm);
        }

        if (weight > hullVolume)
        {
            issues.Add(new CompileIssue("Error", "ErrBuoyancy",
                $"weight {weight:0.0} t exceeds hull buoyancy {hullVolume:0.0} t — the design cannot float; " +
                "enlarge the hull or remove parts"));
        }

        double reserve = hullVolume > 0 ? (hullVolume - weight) / Math.Max(1e-9, weight) : 0;
        if (reserve < MinReserveBuoyancyFrac && weight <= hullVolume)
        {
            issues.Add(new CompileIssue("Warning", "WarnLowReserve",
                $"reserve buoyancy {reserve:0.00} below {MinReserveBuoyancyFrac:0.00} — one flooding hit may sink her"));
        }

        // ---- static draft: 1D volume-height ladder inversion (NAVALART §4) ----
        double draft = SolveDraft(blocks, weight, halfBeamM);
        if (draft <= 0)
        {
            issues.Add(new CompileIssue("Error", "ErrDraft", "static draft solve failed (degenerate hull ladder)"));
        }

        // ---- propulsion / crew sanity ----
        int engines = design.Parts.Count(p => p.Kind is "Engine" or "Boiler" or "Turbine");
        if (engines == 0)
        {
            issues.Add(new CompileIssue("Error", "ErrNoPower", "no propulsion parts (Engine/Boiler/Turbine)"));
        }

        int compartments = design.Parts.Count(p => p.Kind == "Compartment");
        if (compartments == 0)
        {
            issues.Add(new CompileIssue("Warning", "WarnNoCompartment", "no compartments — crew has no protected space"));
        }

        double maxSpeed = design.MaxSpeedKnots ?? Math.Clamp(14 + engines * 4, 12, 36);
        double wakeLimit = WakeSpeedFactor * Math.Sqrt(Math.Max(1, length));
        if (maxSpeed > wakeLimit)
        {
            issues.Add(new CompileIssue("Warning", "WarnWakeLimit",
                $"speed {maxSpeed:0} kn over the wake check √L×{WakeSpeedFactor:0.##} = {wakeLimit:0} kn for {length:0} m"));
        }

        if (issues.Any(i => i.Severity == "Error"))
        {
            return new CompileResult { Ok = false, Issues = issues };
        }

        // ---- hull sections: thirds of the keel (Bow/Mid/Stern, +Z = bow) ----
        double third = length / 3.0;
        var sections = new List<HullSectionDefinition>
        {
            new() { Id = "bow", Role = HullSectionRole.Bow, Hp = SectionHp(design, zMax - third, zMax), ZMinM = zMax - third, ZMaxM = zMax },
            new() { Id = "mid", Role = HullSectionRole.Mid, Hp = SectionHp(design, zMin + third, zMax - third), ZMinM = zMin + third, ZMaxM = zMax - third },
            new() { Id = "stern", Role = HullSectionRole.Stern, Hp = SectionHp(design, zMin, zMin + third), ZMinM = zMin, ZMaxM = zMin + third },
        };

        // ---- parts → ShipPartDefinition (section by centroid Z) ----
        var parts = new List<ShipPartDefinition>();
        var partVols = new Dictionary<string, double>(); // guid -> box volume
        int crewTotal = 0;
        foreach (var p in design.Parts.OrderBy(p => p.Guid, StringComparer.Ordinal))
        {
            if (!PartDefaults.TryGetValue(p.Kind, out var dflt))
            {
                continue; // already reported
            }

            double cx = (p.XMinM + p.XMaxM) / 2;
            double cy = (p.YMinM + p.YMaxM) / 2;
            double cz = (p.ZMinM + p.ZMaxM) / 2;
            string sectionId = cz >= zMax - third ? "bow" : cz >= zMin + third ? "mid" : "stern";
            double vol = Math.Max(0.1, (p.XMaxM - p.XMinM) * (p.YMaxM - p.YMinM) * (p.ZMaxM - p.ZMinM));
            int crew = p.Crew ?? dflt.CrewPerPart;
            crewTotal += crew;
            // Buoyancy share: compartments carry the ship's float reserve — spread the
            // 100% across compartments by volume (deterministic tie-break by Guid).
            double share = p.Kind == "Compartment" ? 1.0 : 0.0;
            parts.Add(new ShipPartDefinition
            {
                Id = $"{p.Kind}/{p.Guid}",
                Kind = Enum.Parse<PartKind>(p.Kind),
                SectionId = sectionId,
                Hp = p.Hp ?? Math.Max(10, dflt.HpPerM3 * vol),
                Crew = crew,
                XMinM = p.XMinM, XMaxM = p.XMaxM,
                YMinM = p.YMinM, YMaxM = p.YMaxM,
                ZMinM = p.ZMinM, ZMaxM = p.ZMaxM,
                Open = p.Open || dflt.Open,
                BuoyancySharePct = 0, // normalized below
                TurretGroup = p.TurretGroup,
            });
            partVols[p.Guid] = vol;
            _ = (cx, cy); // centroid x/y kept for future CoM weighting
        }

        // Buoyancy share: compartments carry the float reserve — spread the 100% across
        // compartments by volume (deterministic: same ordering as the parts loop).
        double compVolSum = partVols
            .Where(kv => design.Parts.Any(p => p.Guid == kv.Key && p.Kind == "Compartment"))
            .Sum(kv => kv.Value);
        for (int i = 0; i < parts.Count; i++)
        {
            var src = design.Parts.First(p => parts[i].Id.EndsWith("/" + p.Guid, StringComparison.Ordinal));
            if (src.Kind == "Compartment" && compVolSum > 0)
            {
                parts[i] = parts[i] with { BuoyancySharePct = 100.0 * partVols[src.Guid] / compVolSum };
            }
        }

        if (compVolSum <= 0)
        {
            // No compartments: spread over all parts so flooding still books buoyancy loss.
            double even = parts.Count > 0 ? 100.0 / parts.Count : 0;
            for (int i = 0; i < parts.Count; i++)
            {
                parts[i] = parts[i] with { BuoyancySharePct = even };
            }
        }

        // ---- armor slabs → plates ----
        var plates = new List<ArmorPlateDefinition>();
        foreach (var a in design.ArmorSlabs.OrderBy(a => a.Guid, StringComparer.Ordinal))
        {
            if (!Enum.TryParse<BoxFace>(a.Face, out var face))
            {
                issues.Add(new CompileIssue("Warning", "WarnArmorFace",
                    $"armor slab {a.Guid}: unknown face '{a.Face}', skipped"));
                continue;
            }

            plates.Add(new ArmorPlateDefinition
            {
                Id = $"armor/{a.Guid}",
                XMinM = a.XMinM, XMaxM = a.XMaxM,
                YMinM = a.YMinM, YMaxM = a.YMaxM,
                ZMinM = a.ZMinM, ZMaxM = a.ZMaxM,
                Face = face,
                ThicknessMm = a.ThicknessMm,
            });
        }

        // ---- guns (validate turret groups exist) ----
        var guns = new List<NavalGunDefinition>();
        foreach (var g in design.Guns.OrderBy(g => g.Guid, StringComparer.Ordinal))
        {
            bool hasTurret = design.Parts.Any(p => p.Kind == "Turret" && p.TurretGroup == g.TurretGroup);
            if (!hasTurret)
            {
                issues.Add(new CompileIssue("Warning", "WarnGunNoTurret",
                    $"gun {g.Guid}: no Turret part in group '{g.TurretGroup}' — the mount will never fire"));
            }

            guns.Add(new NavalGunDefinition
            {
                Id = $"gun/{g.TurretGroup}/{g.Guid}",
                TurretGroup = g.TurretGroup,
                ShellId = g.ShellId,
                HeShellId = g.HeShellId,
                Barrels = g.Barrels,
                RoundsPerMinute = g.RoundsPerMinute,
                RangeM = g.RangeM,
                TraverseDegPerS = g.TraverseDegPerS,
            });
        }

        // ---- derived quantities report (editor panel shows these live) ----
        var derived = new DerivedQuantities
        {
            HullVolumeM3 = hullVolume,
            DisplacedVolumeM3 = weight, // ρ = 1 t/m³
            StaticDraftM = draft,
            WeightT = weight,
            ReserveBuoyancyFrac = reserve,
            LengthM = length,
            BeamM = halfBeamM * 2,
            CenterOfMass = (0, draft, CenterOfMassZ(design)),
            CenterOfBuoyancy = (0, draft / 2, 0),
        };

        var ship = new ShipDefinition
        {
            Id = design.Meta.Id,
            DisplayName = design.Meta.Name,
            Class = Classify(length, design.Guns.Count),
            DisplacementT = weight,
            LengthM = length,
            BeamM = halfBeamM * 2,
            DraftM = draft,
            CrewTotal = Math.Max(crewTotal, 8),
            CrewRepairThreshold = Math.Max(4, crewTotal / 10),
            CrewSurviveThreshold = Math.Max(2, crewTotal / 20),
            HullSections = [.. sections],
            Parts = [.. parts],
            ArmorPlates = [.. plates],
            Guns = [.. guns],
            MaxSpeedKnots = maxSpeed,
            TurnRateDegPerS = design.TurnRateDegPerS ?? TurnRateFor(length),
            DcGeneration = 1,
        };

        return new CompileResult { Ok = true, Issues = issues, Ship = ship, Derived = derived };
    }

    private static double PartDensityPerArmorMmMmToDensity(double armorMm) =>
        Math.Clamp(armorMm * PartDensityPerArmorMm, PartDensityMin, PartDensityMax);

    private static double SectionHp(BuilderDesign design, double zLo, double zHi)
    {
        // Section structure HP ∝ hull volume in the section's z-band.
        double vol = 0;
        foreach (var b in design.HullBlocks)
        {
            double lo = Math.Max(b.ZMinM, zLo);
            double hi = Math.Min(b.ZMaxM, zHi);
            if (hi > lo)
            {
                vol += (hi - lo) * (b.YTopM - b.YBottomM);
            }
        }

        return Math.Max(200, vol * 12.0);
    }

    private static double CenterOfMassZ(BuilderDesign design)
    {
        if (design.Parts.Count == 0)
        {
            return 0;
        }

        double sum = 0, wsum = 0;
        foreach (var p in design.Parts)
        {
            double vol = Math.Max(0.1, (p.XMaxM - p.XMinM) * (p.YMaxM - p.YMinM) * (p.ZMaxM - p.ZMinM));
            sum += (p.ZMinM + p.ZMaxM) / 2 * vol;
            wsum += vol;
        }

        return wsum > 0 ? sum / wsum : 0;
    }

    private static double TurnRateFor(double length) =>
        Math.Clamp(120.0 / Math.Max(30, length), 0.8, 3.5);

    private static ShipClass Classify(double length, int gunMounts)
    {
        if (length < 40)
        {
            return ShipClass.SmallCraft;
        }

        if (length < 110)
        {
            return ShipClass.Destroyer;
        }

        if (length < 150)
        {
            return ShipClass.Cruiser;
        }

        if (length < 200)
        {
            return gunMounts >= 4 ? ShipClass.Battlecruiser : ShipClass.Cruiser;
        }

        return ShipClass.Battleship;
    }

    /// <summary>1D static draft: walk the volume-height ladder bottom-up until the
    /// cumulative displaced volume reaches the weight; lerp inside the crossing slab.</summary>
    private static double SolveDraft(List<HullBlock> ordered, double weightT, double halfBeamM)
    {
        double acc = 0;
        double prevY = ordered.Count > 0 ? ordered[0].YBottomM : 0;
        foreach (var b in ordered)
        {
            if (b.YBottomM > prevY)
            {
                // Gap: no hull surface at this band → displacement is flat; weight cannot
                // be held here unless already satisfied.
                if (acc >= weightT)
                {
                    return prevY;
                }

                prevY = b.YBottomM;
            }

            double formFactor = 0.8 + 0.2 * b.WidthFrac;
            // Volume per metre of height = plan area = z-length × beam width (the ladder
            // integrates VOLUMES, so the slab's longitudinal extent must be included).
            double areaPerM = (b.ZMaxM - b.ZMinM) * 2 * halfBeamM * b.WidthFrac * formFactor;
            double slabTop = b.YTopM;
            double volToTop = acc + (slabTop - prevY) * areaPerM;
            if (volToTop >= weightT)
            {
                return prevY + (weightT - acc) / Math.Max(1e-9, areaPerM);
            }

            acc = volToTop;
            prevY = slabTop;
        }

        return prevY > 0 ? prevY : 0; // weight exceeds buoyancy — caller reports ErrBuoyancy
    }
}
