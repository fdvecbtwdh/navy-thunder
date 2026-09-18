using NavyThunder.Data;
using NavyThunder.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace NavyThunder.Core.Tests;

public class DataRepositoryTests(ITestOutputHelper output)
{
    [Fact]
    public void Repository_Data_Directory_Loads_And_Validates()
    {
        string dataDir = RepoLocator.FindDataDirectory();
        var repo = DataRepository.LoadFromDirectory(dataDir);

        output.WriteLine($"shells={repo.Shells.Count} torpedoes={repo.Torpedoes.Count} " +
                         "references=" + repo.WtReferences.Count + " calibration=" + repo.Calibration.Count);

        // The Phase 0 baseline dataset must be present and coherent.
        Assert.True(repo.Shells.Count >= 8, "expected the baseline naval + air-gun shells");
        Assert.True(repo.Torpedoes.Count >= 1, "expected the Type 93 baseline torpedo");
        Assert.True(repo.WtReferences.Count >= 20, "expected the WT reference table");
        Assert.True(repo.Calibration.Count >= 10, "expected calibration parameters");
        Assert.True(repo.Ships.Count >= 30, "expected the generated fleet (Tier-2 templates)");
        Assert.True(repo.Aircraft.Count >= 1, "expected the baseline test fighter");

        var mk8 = repo.RequireShell("usn_406mm_mk8_mod6_apcbc");
        Assert.Equal(1225, mk8.MassKg);
        Assert.Equal(762, mk8.MuzzleVelocityMs);
        Assert.Equal(1.0, mk8.DemarrePenetrationK);

        var vt = repo.RequireShell("usn_127mm_mk31_aa_vt");
        Assert.NotNull(vt.ProximityFuse);
        Assert.Equal(23.0, vt.ProximityFuse!.RadiusM);
        Assert.Equal(457.0, vt.ProximityFuse.ArmDistanceM);

        var type93 = repo.Torpedoes["ijn_610mm_type93_mod1_mod2"];
        Assert.Equal(50.0, type93.ArmDistanceM);
        Assert.Equal(1.0, type93.RunningDepthM);

        Assert.Equal(1.43, repo.WtReferences["demarre_speed_pow"].Value);
    }

    [Fact]
    public void Negative_Mass_Fails_Validation_With_File_Context()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "kind": "shellSet",
              "shells": [
                {
                  "id": "bad_shell",
                  "displayName": "Bad",
                  "caliberMm": 127,
                  "massKg": -5,
                  "muzzleVelocityMs": 792
                }
              ]
            }
            """;

        var ex = Assert.Throws<DataValidationException>(
            () => DataRepository.LoadFromDocuments([("shells/bad.json", json)]));

        Assert.Contains(ex.Errors, e => e.Contains("bad.json") && e.Contains("MassKg"));
    }

    [Fact]
    public void Vt_Shell_Without_Proximity_Fuse_Fails_Validation()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "kind": "shellSet",
              "shells": [
                {
                  "id": "vt_no_fuse",
                  "displayName": "VT",
                  "category": "AAVT",
                  "caliberMm": 127,
                  "massKg": 25,
                  "muzzleVelocityMs": 792
                }
              ]
            }
            """;

        var ex = Assert.Throws<DataValidationException>(
            () => DataRepository.LoadFromDocuments([("shells/vt.json", json)]));

        Assert.Contains(ex.Errors, e => e.Contains("ProximityFuse"));
    }

    [Fact]
    public void Duplicate_Ids_Fail_Validation()
    {
        const string shell = """
            {
              "id": "dup",
              "displayName": "Dup",
              "caliberMm": 127,
              "massKg": 25,
              "muzzleVelocityMs": 792
            }
            """;
        string json = $$"""
            {
              "schemaVersion": 1,
              "kind": "shellSet",
              "shells": [ {{shell}}, {{shell}} ]
            }
            """;

        var ex = Assert.Throws<DataValidationException>(
            () => DataRepository.LoadFromDocuments([("shells/dup.json", json)]));

        Assert.Contains(ex.Errors, e => e.Contains("duplicate id 'dup'"));
    }

    [Fact]
    public void Reference_Entry_Without_Source_Url_Is_Rejected()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "kind": "wtReference",
              "entries": [
                { "id": "no_source", "value": 1.0, "sourceKind": "wt_datamine", "sourceUrl": "" }
              ]
            }
            """;

        var ex = Assert.Throws<DataValidationException>(
            () => DataRepository.LoadFromDocuments([("reference/bad.json", json)]));

        Assert.Contains(ex.Errors, e => e.Contains("SourceUrl"));
    }

    [Fact]
    public void Wrong_Schema_Version_Is_Rejected()
    {
        const string json = """{ "schemaVersion": 99, "kind": "calibration", "entries": [] }""";

        var ex = Assert.Throws<DataValidationException>(
            () => DataRepository.LoadFromDocuments([("calibration/bad.json", json)]));

        Assert.Contains(ex.Errors, e => e.Contains("schemaVersion"));
    }
}
