using Godot;
using NavyThunder.Core.Mathematics;

namespace NavyThunder.Frontend;

/// <summary>
/// R3.3: procedural sound effects - cannon booms, AA cracks, explosions and a sea
/// ambience loop are synthesized at startup (no external assets), then played through
/// dedicated AudioServer buses so the settings sliders control them (R3.4).
/// </summary>
public static class Sfx
{
    public const int SampleRate = 22050;

    private static AudioStreamWav? _mainGun, _secondary, _aa, _explosion, _bigExplosion, _sea;

    private static float Rand() => (float)Random.Shared.NextDouble() * 2f - 1f;

    /// <summary>Low boom: decaying sine + noise burst (naval main gun).</summary>
    private static AudioStreamWav MakeBoom(float baseFreq, float seconds, float noiseMix)
    {
        int n = (int)(SampleRate * seconds);
        var data = new byte[n * 2];
        float phase = 0, lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float env = (1f - t) * (1f - t);
            float freq = baseFreq * (1f + 0.3f * (1f - t)); // pitch drops as the boom decays
            phase += 2f * MathF.PI * freq / SampleRate;
            float tone = MathF.Sin(phase) + 0.5f * MathF.Sin(phase * 2f);
            float noise = Rand();
            lp += 0.25f * (noise - lp); // low-passed rumble
            float v = (tone * (1f - noiseMix) + lp * 3f * noiseMix) * env * 0.9f;
            short s = (short)Math.Clamp(v, -1f, 1f * 0.99f * 32767);
            data[i * 2] = (byte)s;
            data[i * 2 + 1] = (byte)(s >> 8);
        }

        return ToWav(data);
    }

    /// <summary>Sharp AA crack: fast noise burst, minimal tone.</summary>
    private static AudioStreamWav MakeCrack(float seconds)
    {
        int n = (int)(SampleRate * seconds);
        var data = new byte[n * 2];
        float lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float env = (1f - t) * (1f - t) * (1f - t);
            lp += 0.5f * (Rand() - lp);
            short s = (short)Math.Clamp(lp * 2.2f * env * 32767f, -32767f, 32767f);
            data[i * 2] = (byte)s;
            data[i * 2 + 1] = (byte)(s >> 8);
        }

        return ToWav(data);
    }

    /// <summary>Sea ambience: loopable filtered noise swell.</summary>
    private static AudioStreamWav MakeSea(float seconds)
    {
        int n = (int)(SampleRate * seconds);
        var data = new byte[n * 2];
        float lp = 0, lp2 = 0;
        for (int i = 0; i < n; i++)
        {
            float swell = 0.6f + 0.4f * MathF.Sin(2f * MathF.PI * i / n); // seamless loop
            lp += 0.02f * (Rand() - lp);
            lp2 += 0.08f * (lp + Rand() * 0.3f - lp2);
            short s = (short)Math.Clamp(lp2 * swell * 2f, -32767f, 32767f);
            data[i * 2] = (byte)s;
            data[i * 2 + 1] = (byte)(s >> 8);
        }

        return ToWav(data);
    }

    private static AudioStreamWav ToWav(byte[] pcm16)
    {
        var wav = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate,
            Stereo = false,
        };
        wav.Data = pcm16;
        return wav;
    }

    /// <summary>Built once per process; returns the shared streams.</summary>
    public static (AudioStreamWav mainGun, AudioStreamWav secondary, AudioStreamWav aa,
        AudioStreamWav explosion, AudioStreamWav bigExplosion, AudioStreamWav sea) Build()
    {
        _mainGun ??= MakeBoom(70f, 1.1f, 0.55f);
        _secondary ??= MakeBoom(120f, 0.5f, 0.5f);
        _aa ??= MakeCrack(0.18f);
        _explosion ??= MakeBoom(90f, 0.7f, 0.7f);
        _bigExplosion ??= MakeBoom(55f, 1.8f, 0.8f);
        _sea ??= MakeSea(8f);
        return (_mainGun, _secondary, _aa, _explosion, _bigExplosion, _sea);
    }
}

/// <summary>R3.3/R3.4: event-driven playback over Effects/Ambient buses with distance falloff.</summary>
public partial class AudioManager : Node
{
    private const int Voices = 10;
    private readonly AudioStreamPlayer[] _voices = new AudioStreamPlayer[Voices];
    private int _nextVoice;
    private AudioStreamPlayer? _seaPlayer;
    private Vector2 _listenerPos;

    private AudioStreamWav _mainGun = null!, _secondary = null!, _aa = null!,
        _explosion = null!, _bigExplosion = null!, _sea = null!;

    public override void _Ready()
    {
        EnsureBus("Effects");
        EnsureBus("Ambient");
        (_mainGun, _secondary, _aa, _explosion, _bigExplosion, _sea) = Sfx.Build();

        for (int i = 0; i < Voices; i++)
        {
            var p = new AudioStreamPlayer { Bus = "Effects" };
            AddChild(p);
            _voices[i] = p;
        }

        _seaPlayer = new AudioStreamPlayer { Bus = "Ambient", Stream = _sea };
        AddChild(_seaPlayer);
    }

    private static void EnsureBus(string name)
    {
        if (AudioServer.GetBusIndex(name) == -1)
        {
            AudioServer.AddBus();
            int idx = AudioServer.BusCount - 1;
            AudioServer.SetBusName(idx, name);
            AudioServer.SetBusSend(idx, "Master");
        }
    }

    /// <summary>Applies persisted volumes to the buses (R3.4 settings).</summary>
    public static void ApplyVolumes(float master, float effects, float ambient)
    {
        AudioServer.SetBusVolumeDb(0, LinearToDb(master));
        SetBusVolume("Effects", effects);
        SetBusVolume("Ambient", ambient);
    }

    private static void SetBusVolume(string bus, float linear)
    {
        int idx = AudioServer.GetBusIndex(bus);
        if (idx >= 0)
        {
            AudioServer.SetBusVolumeDb(idx, LinearToDb(linear));
        }
    }

    private static float LinearToDb(float linear) =>
        linear <= 0.0001f ? -80f : 20f * MathF.Log10(linear);

    public void StartAmbient()
    {
        if (_seaPlayer is { Playing: false })
        {
            _seaPlayer.Play();
        }
    }

    public void SetListener(Vector2 worldPos) => _listenerPos = worldPos;

    public void PlayGun(float caliberMm, Vec3 worldPos) =>
        Play(caliberMm >= 280 ? _mainGun : caliberMm >= 100 ? _secondary : _aa, worldPos);

    public void PlayExplosion(Vec3 worldPos, bool big) => Play(big ? _bigExplosion : _explosion, worldPos);

    private void Play(AudioStreamWav stream, Vec3 worldPos)
    {
        float dist = _listenerPos.DistanceTo(Plane(worldPos));
        float atten = Math.Clamp(1f - dist / 1400f, 0.05f, 1f);
        var p = _voices[_nextVoice];
        _nextVoice = (_nextVoice + 1) % Voices;
        p.Stop();
        p.Stream = stream;
        p.VolumeDb = LinearToDb(atten);
        p.PitchScale = 0.9f + (float)Random.Shared.NextDouble() * 0.2f;
        p.Play();
    }

    private static Vector2 Plane(Vec3 world) => new((float)world.X, (float)world.Z);
}
