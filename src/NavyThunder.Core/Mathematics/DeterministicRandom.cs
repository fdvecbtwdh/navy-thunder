using System.Numerics;

namespace NavyThunder.Core.Mathematics;

/// <summary>
/// Deterministic xoshiro256** PRNG. The same seed produces the same sequence on every
/// platform and run, which is the foundation of replayable combat tests.
/// Sub-streams (per-subsystem generators) are derived via FNV-1a hashed names so that
/// consuming order changes in one system never perturb another system's randomness.
/// </summary>
public sealed class DeterministicRandom
{
    private ulong _s0, _s1, _s2, _s3;

    public DeterministicRandom(ulong seed)
    {
        ulong sm = seed;
        _s0 = SplitMix64(ref sm);
        _s1 = SplitMix64(ref sm);
        _s2 = SplitMix64(ref sm);
        _s3 = SplitMix64(ref sm);
        if ((_s0 | _s1 | _s2 | _s3) == 0)
        {
            _s0 = _s1 = _s2 = 1;
        }
    }

    public static ulong SplitMix64(ref ulong state)
    {
        ulong z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public ulong NextUInt64()
    {
        ulong result = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = BitOperations.RotateLeft(_s3, 45);
        return result;
    }

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Range must be non-empty.");
        }

        ulong range = (ulong)((long)maxExclusive - minInclusive);
        return (int)(minInclusive + (long)(NextUInt64() % range));
    }

    /// <summary>Uniformly distributed point on the unit sphere.</summary>
    public Vec3 NextUnitVector()
    {
        double z = NextDouble() * 2.0 - 1.0;
        double azimuth = NextDouble() * 2.0 * Math.PI;
        double r = Math.Sqrt(Math.Max(0.0, 1.0 - z * z));
        return new Vec3(r * Math.Cos(azimuth), r * Math.Sin(azimuth), z);
    }
}

public static class RngStreams
{
    /// <summary>Derives an independent per-name stream seed from the world master seed.</summary>
    public static DeterministicRandom Create(ulong masterSeed, string streamName)
    {
        return new DeterministicRandom(HashSeed(masterSeed, streamName));
    }

    public static ulong HashSeed(ulong masterSeed, string streamName)
    {
        const ulong fnvOffset = 14695981039346656037UL;
        const ulong fnvPrime = 1099511628211UL;
        ulong h = fnvOffset ^ masterSeed;
        foreach (char c in streamName)
        {
            h ^= c;
            h *= fnvPrime;
        }

        return h;
    }
}
