using System.Text.Json;
using NavyThunder.Core.Battle;
using NavyThunder.Core.Builder;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;
using NavyThunder.Data;
using Xunit;
using Xunit.Abstractions;

namespace NavyThunder.Core.Tests;

/// <summary>
/// PHASE_06 §9 compiler tests: identities (Σ volumes = report, Σ weights = report),
/// the rejection matrix, byte-determinism, hand-calculated draft, and the end-to-end
/// BuilderDesign → compiled ship → real battle → battle report chain.
/// </summary>
[Trait("Bucket", "Fast")]
public class ShipCompilerTests(ITestOutputHelper output)
{
    private static BuilderDesign Gunboat(
        double weightT = 400,
        string id = "test_gunboat",
        int guns = 2)
    {
        // Simple 60×8×6 m box hull: full volume 60*6*8 = 2880 m³ → steel 1584 t …
        // too heavy. Use a smaller hull so tests control weight precisely:
        // 40 m long, 4 m tall, 6 m wide ⇒ 960 m³.
        var design = new BuilderDesign
        {
            Meta = new DesignMeta { Id = id, Name = id.Replace('_', ' ').ToUpperInvariant() },
            HullBlocks =
            [
                new HullBlock { Guid = "h1", ZMinM = -20, ZMaxM = 20, YBottomM = -2, YTopM = 2, WidthFrac = 1 },
            ],
            Parts =
            [
                new PartPlacement { Guid = "e1", Kind = "Engine", XMinM = -1, XMaxM = 1, YMinM = -1, YMaxM = 0, ZMinM = -8, ZMaxM = -4 },
                new PartPlacement { Guid = "c1", Kind = "Compartment", XMinM = -1, XMaxM = 1, YMinM = -1, YMaxM = 1, ZMinM = -3, ZMaxM = 3 },
                new PartPlacement { Guid = "t1", Kind = "Turret", XMinM = -1.5, XMaxM = 1.5, YMinM = 1.2, YMaxM = 2.4, ZMinM = 12, ZMaxM = 15, TurretGroup = "A" },
                new PartPlacement { Guid = "m1", Kind = "Magazine", XMinM = -1, XMaxM = 1, YMinM = -1, YMaxM = 0, ZMinM = 8, ZMaxM = 11, TurretGroup = "A" },
            ],
            Guns = [],
        };
        for (int i = 0; i < guns; i++)
        {
            design = design with { };
            design.Guns.Add(new GunMountDesign
            {
                Guid = $"g{i}",
                TurretGroup = "A",
                ShellId = "usn_127mm_mk46_special_common",
                HeShellId = "wt_127mm_he",
                Barrels = 2,
                RoundsPerMinute = 15,
                RangeM = 12000,
            });
        }

        return design;
    }

    // ------------------------------------------------------------ identities (§9)

    [Fact]
    public void Hull_Volume_Identity_Holds()
    {
        var design = Gunboat();
        var result = ShipCompiler.Compile(design);
        Assert.True(result.Ok, string.Join("; ", result.Issues));

        // Recompute Σ blocks with the compiler's own formula (form factor documented).
        double sum = 0;
        foreach (var b in design.HullBlocks)
        {
            double halfBeamM = 1.0 * 40 * 0.12;
            double formFactor = 0.8 + 0.2 * b.WidthFrac;
            sum += (b.ZMaxM - b.ZMinM) * (b.YTopM - b.YBottomM) * 2 * halfBeamM * b.WidthFrac * formFactor;
        }

        Assert.Equal(sum, result.Derived!.HullVolumeM3, 6);
        Assert.Equal(sum * ShipCompiler.HullDensityTPerM3,
            result.Derived!.WeightT - PartWeight(design), 6); // hull steel = Σvol × density
        Assert.Equal(result.Derived!.HullVolumeM3 * ShipCompiler.HullDensityTPerM3 + PartWeight(design),
            result.Derived!.WeightT, 6);
    }

    private static double PartWeight(BuilderDesign design) => design.Parts.Sum(p =>
        Math.Max(0.1, (p.XMaxM - p.XMinM) * (p.YMaxM - p.YMinM) * (p.ZMaxM - p.ZMinM)) *
        Math.Clamp(ShipCompiler.DefaultPartArmorMm * ShipCompiler.PartDensityPerArmorMm,
            ShipCompiler.PartDensityMin, ShipCompiler.PartDensityMax));

    // ------------------------------------------------------------ draft (§9 hand calc)

    [Fact]
    public void Static_Draft_Matches_Hand_Calculation()
    {
        var design = Gunboat();
        var result = ShipCompiler.Compile(design);
        Assert.True(result.Ok);

        // Ladder: one block y=-2..2, 40 m long; volume per metre of height =
        // z-length × beam width = 40 × (2×4.8×1×1.0) = 384 m³/m.
        double areaPerM = 40 * 2 * 4.8 * 1.0 * 1.0;
        double weight = result.Derived!.WeightT;
        // Draft measured from the block bottom (-2 m).
        double expectedDraftAboveBottom = weight / areaPerM;
        double expectedWaterlineY = -2 + expectedDraftAboveBottom;
        output.WriteLine($"weight={weight:0.0}t area={areaPerM} → waterline y={expectedWaterlineY:0.00} " +
                         $"(block spans -2..2; deck at +2)");
        Assert.InRange(expectedWaterlineY, -2, 2); // must sit inside the hull, else the test itself is wrong
        Assert.Equal(expectedWaterlineY, result.Derived!.StaticDraftM, 3);
    }

    // ------------------------------------------------------------ rejection matrix (§9)

    [Fact]
    public void Rejection_Matrix()
    {
        // Empty design
        var empty = new BuilderDesign { Meta = new DesignMeta { Id = "x", Name = "X" } };
        var r1 = ShipCompiler.Compile(empty);
        Assert.False(r1.Ok);
        Assert.Contains(r1.Issues, i => i.Code == "ErrNoHull");

        // Degenerate block
        var degenerate = Gunboat();
        degenerate.HullBlocks[0] = degenerate.HullBlocks[0] with { ZMaxM = -20 };
        var r2 = ShipCompiler.Compile(degenerate);
        Assert.False(r2.Ok);
        Assert.Contains(r2.Issues, i => i.Code == "ErrBlockDegenerate");

        // No propulsion
        var noEngine = Gunboat();
        noEngine.Parts.RemoveAll(p => p.Kind == "Engine");
        var r3 = ShipCompiler.Compile(noEngine);
        Assert.False(r3.Ok);
        Assert.Contains(r3.Issues, i => i.Code == "ErrNoPower");

        // Cannot float: lots of heavy parts inside a small hull
        var sinking = Gunboat();
        for (int i = 0; i < 400; i++)
        {
            sinking.Parts.Add(new PartPlacement
            {
                Guid = $"ballast{i:D3}", Kind = "Magazine",
                XMinM = -1.5, XMaxM = 1.5, YMinM = -1.5, YMaxM = 0, ZMinM = -19 + i * 0.1, ZMaxM = -17 + i * 0.1,
            });
        }

        var r4 = ShipCompiler.Compile(sinking);
        Assert.False(r4.Ok);
        Assert.Contains(r4.Issues, i => i.Code == "ErrBuoyancy");

        // Unknown part kind
        var badKind = Gunboat();
        badKind.Parts.Add(new PartPlacement
        {
            Guid = "warp", Kind = "WarpDrive",
            XMinM = -1, XMaxM = 1, YMinM = -1, YMaxM = 0, ZMinM = 0, ZMaxM = 1,
        });
        var r5 = ShipCompiler.Compile(badKind);
        Assert.False(r5.Ok);
        Assert.Contains(r5.Issues, i => i.Code == "ErrPartKind");

        foreach (var r in new[] { r1, r2, r3, r4, r5 })
        {
            Assert.All(r.Issues, i => Assert.NotEqual("", i.Message));
        }
    }

    // ------------------------------------------------------------ determinism (§9)

    [Fact]
    public void Compile_Is_Byte_Deterministic()
    {
        var design = Gunboat(guns: 3);
        for (int i = 0; i < 12; i++)
        {
            design.Parts.Add(new PartPlacement
            {
                Guid = $"comp{i:D2}", Kind = "Compartment",
                XMinM = -1, XMaxM = 1, YMinM = -1, YMaxM = 1, ZMinM = -14 + i * 2, ZMaxM = -13 + i * 2,
            });
        }

        var a = ShipCompiler.Compile(design);
        var b = ShipCompiler.Compile(design);
        Assert.True(a.Ok && b.Ok);

        var ja = JsonSerializer.Serialize(a.Ship, BuilderDesign.JsonOpts);
        var jb = JsonSerializer.Serialize(b.Ship, BuilderDesign.JsonOpts);
        Assert.Equal(ja, jb); // byte-identical JSON — no RNG, no wall clock, ordered loops
    }

    [Fact]
    public void Design_Save_Load_Roundtrip_Is_Byte_Identical()
    {
        var design = Gunboat();
        design.Parts.Add(new PartPlacement
        {
            Guid = "arm", Kind = "Compartment",
            XMinM = -1, XMaxM = 1, YMinM = -1, YMaxM = 1, ZMinM = 4, ZMaxM = 7,
        });
        design.ArmorSlabs.Add(new ArmorSlab
        {
            Guid = "belt", XMinM = -2, XMaxM = 2, YMinM = -1, YMaxM = 1, ZMinM = -10, ZMaxM = 10,
            Face = "XMax", ThicknessMm = 50,
        });

        string path = Path.Combine(Path.GetTempPath(), $"nt_design_rt_{Guid.NewGuid():N}.json");
        try
        {
            design.Save(path);
            var loaded = BuilderDesign.Load(path);
            Assert.Equal(JsonSerializer.Serialize(design, BuilderDesign.JsonOpts),
                         JsonSerializer.Serialize(loaded, BuilderDesign.JsonOpts));
            // Derived values never persisted: the saved JSON must not contain displacement/draft fields.
            string text = File.ReadAllText(path);
            Assert.DoesNotContain("DisplacementT", text);
            Assert.DoesNotContain("StaticDraft", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ------------------------------------------------------------ end-to-end (§9/§10)

    [Fact]
    public void Compiled_Ship_Fights_In_A_Real_Battle_And_Reports()
    {
        var design = Gunboat(id: "built_gunboat", guns: 2);
        var result = ShipCompiler.Compile(design);
        Assert.True(result.Ok, string.Join("; ", result.Issues));

        var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
        var scenario = BattleScenario.Load(Path.Combine(RepoLocator.FindRepoRoot()!, "scenarios", "bb_duel.json"));
        // Swap team 1's ship for the compiled gunboat (retains the scenario's seed/geometry).
        var teams = scenario.Teams.ToArray();
        var t0 = teams[0];
        teams[0] = t0 with { Ships = [t0.Ships[0] with { Ship = result.Ship!.Id }] };
        var patched = new BattleScenario
        {
            Name = scenario.Name, MaxDurationS = 240, Seed = scenario.Seed, Teams = teams,
        };

        repo.RegisterCompiled(result.Ship); // builder ships join the repo like historical ones
        var runner = new BattleRunner(repo, patched);
        Assert.Contains(runner.Ships, s => s.Definition.Id == "built_gunboat");

        int steps = 0;
        while (runner.World.Time < 120 && runner.Battle.Result == BattleResult.Running && steps < 120 * 50 + 10)
        {
            runner.World.Step();
            steps++;
        }

        var report = BattleReportGenerator.Generate(runner.World, runner.Ships, runner.Battle);
        Assert.True((double)report["durationS"]! >= 0);
        Assert.Contains(runner.Ships, s => s.Definition.Id == "built_gunboat" && s.CrewAlive >= 0);
        output.WriteLine($"gunboat fought {runner.World.Time:0}s, result={runner.Battle.Result}, " +
                         $"crew={runner.Ships.First(s => s.Definition.Id == "built_gunboat").CrewAlive}");
    }

    // ------------------------------------------------------------ randomized batch (§10)

    public static IEnumerable<object[]> RandomLegalDesigns()
    {
        for (int i = 0; i < 12; i++)
        {
            yield return new object[] { i };
        }
    }

    [Theory]
    [MemberData(nameof(RandomLegalDesigns))]
    public void Random_Legal_Designs_Compile_And_Fight(int seed)
    {
        // Deterministic PRNG (no Random.Shared per §3.2; any seed → same design).
        var rng = new DeterministicRandom((ulong)(seed * 0x9E3779B9 + 12345));
        double Next(double lo, double hi) => lo + (hi - lo) * rng.NextDouble();

        double length = Next(35, 90);
        var design = new BuilderDesign
        {
            Meta = new DesignMeta { Id = $"random_{seed}", Name = $"RANDOM {seed}" },
            HullBlocks =
            [
                new HullBlock { Guid = "h", ZMinM = -length / 2, ZMaxM = length / 2, YBottomM = -2, YTopM = 2, WidthFrac = 1 },
            ],
            Parts =
            [
                new PartPlacement { Guid = "e", Kind = "Engine", XMinM = -1, XMaxM = 1, YMinM = -1, YMaxM = 0, ZMinM = -length / 2 + 2, ZMaxM = -length / 2 + 6 },
                new PartPlacement { Guid = "c", Kind = "Compartment", XMinM = -1, XMaxM = 1, YMinM = -1, YMaxM = 1, ZMinM = -2, ZMaxM = 2 },
                new PartPlacement { Guid = "t", Kind = "Turret", XMinM = -1.5, XMaxM = 1.5, YMinM = 1.2, YMaxM = 2.4, ZMinM = length / 2 - 6, ZMaxM = length / 2 - 3, TurretGroup = "A" },
            ],
            Guns =
            [
                new GunMountDesign
                {
                    Guid = "g", TurretGroup = "A", ShellId = "usn_127mm_mk46_special_common",
                    Barrels = 2, RoundsPerMinute = Next(10, 25), RangeM = Next(9000, 16000),
                },
            ],
        };

        var result = ShipCompiler.Compile(design);
        Assert.True(result.Ok, $"seed {seed}: {string.Join("; ", result.Issues)}");
        Assert.NotNull(result.Ship);
        Assert.True(result.Derived!.StaticDraftM > 0);
        Assert.True(result.Derived!.ReserveBuoyancyFrac >= ShipCompiler.MinReserveBuoyancyFrac);
    }
}

/// <summary>
/// PHASE_06 §10 acceptance: 100 random legal designs all compile AND fight. Compilation
/// (Fast batch) covers all 100; battle runs are sampled (a full battle per design would
/// put 100 battles in Integration — tests/README §4).
/// </summary>
[Trait("Bucket", "Integration")]
public class ShipCompilerBatchAcceptance(ITestOutputHelper output)
{
    public static BuilderDesign RandomDesign(int seed)
    {
        var rng = new DeterministicRandom((ulong)(seed * 0x9E3779B9 + 12345));
        double Next(double lo, double hi) => lo + (hi - lo) * rng.NextDouble();
        double length = Next(35, 90);
        var design = new BuilderDesign
        {
            Meta = new DesignMeta { Id = $"random_{seed}", Name = $"RANDOM {seed}" },
            HullBlocks =
            [
                new HullBlock { Guid = "h", ZMinM = -length / 2, ZMaxM = length / 2, YBottomM = -2, YTopM = 2, WidthFrac = 1 },
                new HullBlock { Guid = "h2", ZMinM = -length / 2, ZMaxM = length / 2, YBottomM = -2, YTopM = Next(2.5, 4), WidthFrac = Next(0.5, 0.95) },
            ],
            Parts =
            [
                new PartPlacement { Guid = "e", Kind = "Engine", XMinM = -1, XMaxM = 1, YMinM = -1, YMaxM = 0, ZMinM = -length / 2 + 2, ZMaxM = -length / 2 + 6 },
                new PartPlacement { Guid = "c", Kind = "Compartment", XMinM = -1, XMaxM = 1, YMinM = -1, YMaxM = 1, ZMinM = -2, ZMaxM = 2 },
                new PartPlacement { Guid = "t", Kind = "Turret", XMinM = -1.5, XMaxM = 1.5, YMinM = 1.2, YMaxM = 2.4, ZMinM = length / 2 - 6, ZMaxM = length / 2 - 3, TurretGroup = "A" },
                new PartPlacement { Guid = "m", Kind = "Magazine", XMinM = -1, XMaxM = 1, YMinM = -1, YMaxM = 0, ZMinM = length / 2 - 9, ZMaxM = length / 2 - 6, TurretGroup = "A" },
            ],
            Guns =
            [
                new GunMountDesign
                {
                    Guid = "g", TurretGroup = "A", ShellId = "usn_127mm_mk46_special_common",
                    Barrels = 2, RoundsPerMinute = Next(10, 25), RangeM = Next(9000, 16000),
                },
            ],
            ArmorSlabs =
            [
                new ArmorSlab
                {
                    Guid = "belt", XMinM = -2, XMaxM = 2, YMinM = -1.5, YMaxM = 1.5,
                    ZMinM = -length / 4, ZMaxM = length / 4, Face = "XMax", ThicknessMm = Next(20, 120),
                },
            ],
        };
        return design;
    }

    [Fact]
    public void Hundred_Random_Designs_All_Compile()
    {
        int ok = 0;
        for (int seed = 0; seed < 100; seed++)
        {
            var result = ShipCompiler.Compile(RandomDesign(seed));
            Assert.True(result.Ok, $"seed {seed}: {string.Join("; ", result.Issues)}");
            Assert.NotNull(result.Ship);
            Assert.True(result.Derived!.StaticDraftM > 0);
            Assert.InRange(result.Derived!.StaticDraftM, -2.5, result.Derived!.BeamM);
            ok++;
        }

        output.WriteLine($"{ok}/100 compiled");
        Assert.Equal(100, ok);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(17)]
    [InlineData(64)]
    public void Sampled_Designs_Fight_In_A_Real_Battle(int seed)
    {
        var result = ShipCompiler.Compile(RandomDesign(seed));
        Assert.True(result.Ok);

        var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
        var scenario = BattleScenario.Load(Path.Combine(RepoLocator.FindRepoRoot()!, "scenarios", "bb_duel.json"));
        var teams = scenario.Teams.ToArray();
        teams[0] = teams[0] with { Ships = [teams[0].Ships[0] with { Ship = result.Ship!.Id }] };
        var patched = new BattleScenario
        {
            Name = scenario.Name, MaxDurationS = 240, Seed = scenario.Seed, Teams = teams,
        };
        repo.RegisterCompiled(result.Ship);
        var runner = new BattleRunner(repo, patched);
        while (runner.World.Time < 90 && runner.Battle.Result == BattleResult.Running)
        {
            runner.World.Step();
        }

        var built = runner.Ships.First(s => s.Definition.Id == result.Ship!.Id);
        output.WriteLine($"seed {seed}: fought {runner.World.Time:0}s result={runner.Battle.Result} " +
                         $"built crew {built.CrewAlive}/{built.Definition.CrewTotal}");
        Assert.True(runner.World.Time >= 90 || runner.Battle.Result != BattleResult.Running);
    }
}
