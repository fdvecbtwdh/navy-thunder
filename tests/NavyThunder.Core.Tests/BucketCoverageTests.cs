using System.Reflection;
using Xunit;

namespace NavyThunder.Core.Tests;

/// <summary>
/// tests/README.md §1 contract enforcement: every test must belong to one of the six
/// documented buckets (Fast/Integration/Golden/Acceptance/Performance/Soak). A test
/// class (or its fact methods) without a Bucket trait is invisible to every
/// `--filter Bucket=…` gate — it gets silently skipped by CI. This audit turns that
/// failure mode into a red Fast test. Found during Phase 05: ShipNavigationTests and
/// the legacy "Slow" labels predated the 2026-10-04 bucket scheme.
/// </summary>
[Trait("Bucket", "Fast")]
public class BucketCoverageTests
{
    private static readonly HashSet<string> KnownBuckets = new()
    {
        "Fast", "Integration", "Golden", "Acceptance", "Performance", "Soak",
    };

    [Fact]
    public void Every_Test_Has_A_Documented_Bucket()
    {
        var offenders = new List<string>();
        foreach (var type in typeof(BucketCoverageTests).Assembly.GetTypes())
        {
            // Only look at concrete types that own [Fact]/[Theory] methods.
            var facts = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.GetCustomAttributes(true).Any(a => a is FactAttribute))
                .ToList();
            if (facts.Count == 0)
            {
                continue;
            }

            bool classBucket = BucketOf(type) is not null;
            bool allMethodBuckets = facts.All(m => BucketOf(m) is not null);
            if (!classBucket && !allMethodBuckets)
            {
                offenders.Add(type.Name);
                continue;
            }

            // A declared bucket must also be one of the documented ones (catches
            // typos and pre-refactor legacy labels like "Slow").
            var declared = new List<string?>();
            if (classBucket)
            {
                declared.Add(BucketOf(type));
            }

            declared.AddRange(facts.Select(BucketOf));
            var unknown = declared.Where(b => b is not null && !KnownBuckets.Contains(b!)).ToList();
            if (unknown.Count > 0)
            {
                offenders.Add($"{type.Name} (unknown bucket: {string.Join(",", unknown)})");
            }
        }

        Assert.True(offenders.Count == 0,
            "tests without a documented Bucket trait (tests/README.md §1): " + string.Join(", ", offenders));
    }

    private static string? BucketOf(MemberInfo provider)
    {
        // xUnit's [Trait("name","value")] — read the constructor args straight from the
        // attribute usage metadata so this works regardless of the TraitAttribute shape.
        foreach (var data in provider.GetCustomAttributesData())
        {
            if (!data.AttributeType.Name.StartsWith("TraitAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            var args = data.ConstructorArguments;
            if (args.Count >= 2 && args[0].Value is string name && name == "Bucket" && args[1].Value is string value)
            {
                return value;
            }
        }

        return null;
    }
}
