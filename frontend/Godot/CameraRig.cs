using Godot;
using NavyThunder.Core.Mathematics;

namespace NavyThunder.Frontend;

/// <summary>
/// Chase + free observation camera (PHASE_02 §22). The rig tracks the player ship by
/// default (orbit with Q/E, zoom with the wheel); F toggles free observation (arrow keys
/// pan the focus point, PgUp/PgDn height). Pure presentation — never writes Core state.
/// </summary>
public partial class CameraRig : Node3D
{
    private Camera3D _camera = null!;
    private Node3D? _target;
    private float _yaw = MathF.PI;      // behind the ship (ship bow faces +Z at heading 0)
    private float _distance = 320f;
    private bool _freeMode;
    private Vector3 _freeFocus;

    public Camera3D Camera => _camera;

    public override void _Ready()
    {
        _camera = new Camera3D
        {
            Current = true,
            Far = 200000,
            Fov = 55,
        };
        AddChild(_camera);
        ApplyPose();
    }

    public void Track(Node3D target, Vec3 initialFocus)
    {
        _target = target;
        _freeFocus = new Vector3((float)initialFocus.X, 0, (float)initialFocus.Z);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton m && m.Pressed)
        {
            float factor = m.ButtonIndex switch
            {
                MouseButton.WheelUp => 0.85f,
                MouseButton.WheelDown => 1f / 0.85f,
                _ => 1f,
            };
            _distance = Mathf.Clamp(_distance * factor, 60f, 4000f);
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        if (Input.IsKeyPressed(Key.Q))
        {
            _yaw += 1.6f * dt;
        }
        if (Input.IsKeyPressed(Key.E))
        {
            _yaw -= 1.6f * dt;
        }
        if (Input.IsKeyPressed(Key.F) && !_freeModeLatch)
        {
            _freeMode = !_freeMode;
            _freeModeLatch = true;
        }
        if (!Input.IsKeyPressed(Key.F))
        {
            _freeModeLatch = false;
        }

        if (_freeMode)
        {
            float pan = _distance * 0.6f * dt;
            var fwd = new Vector3(MathF.Sin(_yaw), 0, MathF.Cos(_yaw));
            if (Input.IsKeyPressed(Key.Up))
            {
                _freeFocus += fwd * pan;
            }
            if (Input.IsKeyPressed(Key.Down))
            {
                _freeFocus -= fwd * pan;
            }
            if (Input.IsKeyPressed(Key.Left))
            {
                _freeFocus += new Vector3(fwd.Z, 0, -fwd.X) * pan;
            }
            if (Input.IsKeyPressed(Key.Right))
            {
                _freeFocus -= new Vector3(fwd.Z, 0, -fwd.X) * pan;
            }
            if (Input.IsKeyPressed(Key.Pageup))
            {
                _freeFocus.Y += pan;
            }
            if (Input.IsKeyPressed(Key.Pagedown))
            {
                _freeFocus.Y = MathF.Max(5f, _freeFocus.Y - pan);
            }
        }
        else if (_target is not null)
        {
            _freeFocus = _target.GlobalPosition;
        }

        ApplyPose();
    }

    private bool _freeModeLatch;

    private void ApplyPose()
    {
        GlobalPosition = _freeFocus;
        Rotation = new Vector3(0, _yaw, 0);
        float pitch = 24f * MathF.PI / 180f;
        _camera.Position = new Vector3(0, MathF.Sin(pitch) * _distance, -MathF.Cos(pitch) * _distance);
        _camera.Rotation = Vector3.Zero;
        _camera.LookAt(_freeFocus + new Vector3(0, 6f, 0));
    }
}
