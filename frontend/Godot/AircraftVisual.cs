using Godot;
using NavyThunder.Core.Aviation;

namespace NavyThunder.Frontend;

/// <summary>
/// Presentation node for one Core aircraft (PHASE_02 §29 scope: AI strike aircraft must
/// be visible). Airframe placeholder: capsule fuselage along local +Z (the Core airframe
/// convention after the Phase 01 data migration) + wing box; identity coordinate mapping.
/// </summary>
public partial class AircraftVisual : Node3D
{
    private readonly Aircraft _aircraft;

    public Aircraft Aircraft => _aircraft;

    public AircraftVisual(Aircraft aircraft)
    {
        _aircraft = aircraft;
    }

    public override void _Ready()
    {
        var mat = new StandardMaterial3D { AlbedoColor = new Color(0.52f, 0.55f, 0.5f), Roughness = 0.6f };

        AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.7f, Height = 9f },
            MaterialOverride = mat,
            RotationDegrees = new Vector3(90, 0, 0), // capsule Y-axis → local Z (nose)
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(11f, 0.25f, 1.8f) },
            MaterialOverride = mat,
            Position = new Vector3(0, -0.1f, 2.4f),
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.22f, 1.6f, 1.2f) },
            MaterialOverride = mat,
            Position = new Vector3(0, 0.6f, -3.6f),
        });
    }

    public void UpdateFromCore()
    {
        var a = _aircraft;
        Position = new Vector3((float)a.WorldPosition.X, (float)a.WorldPosition.Y, (float)a.WorldPosition.Z);
        Rotation = new Vector3(0, (float)(a.HeadingDeg * Math.PI / 180.0), 0);
        Visible = a.State != AircraftState.Destroyed;
    }
}
