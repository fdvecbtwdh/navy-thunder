using Godot;
using NavyThunder.Core.Armor;
using NavyThunder.Core.Builder;
using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Commands;
using NavyThunder.Core.FireControl;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Ships;
using NavyThunder.Data;

namespace NavyThunder.Frontend;

/// <summary>
/// Phase 02: the 3D battle scene root. Drives the Core simulation from the frame loop
/// and presents it with Node3D layers — the Core is the ONLY source of battle truth
/// (PROJECT_DESIGN §3.2): this class reads state, consumes the EventLog through a
/// cursor, submits player helm/fire commands, and renders. It never computes combat.
///
/// Environment knobs (carried over from the Phase R2 harness):
///   NT_FRONTEND_SMOKE=1     headless smoke: structural + Core→Visual sync assertions,
///                           progress prints, clean exit at battle end (exit code = failures)
///   NT_FRONTEND_QUICK=1     fast-forward (~200x) to reach the report quickly
///   NT_FRONTEND_SHOT=x.png  save a viewport capture once the battle reaches shot time
///   NT_FRONTEND_AUTO=1      menu-less auto flow (menu still handles it; battle quits after report)
///   NT_FRONTEND_SCENARIO=f  scenario file (default bb_duel.json)
/// </summary>
public partial class BattleScene3D : Node3D
{
    private BattleRunner? _runner;
    // Normal GUI mode runs the battle at 1x real time (smooth rendering first — the
    // sim budget cap bounds ticks per frame and catch-up spreads over frames).
    // NT_FRONTEND_QUICK / SMOKE keep their accelerated paths (test harnesses only).
    private double _simSpeed = 1.0;
    private double _smokeAccumulator;
    private double _nextReportAt = 30;
    private double _nextTimePrintAt = 10;
    private double _nextSyncCheckAt = 10;
    private int _syncFailures;
    private bool _smokeStructureChecked;
    private bool _shotTaken;
    private string _shotPath = "";
    private bool _playerHe;
    private bool _rHeld;
    private bool _tHeld;
    private bool _gHeld;
    private bool _escHeld;
    private bool _manualAiming;          // P05-2: false = auto tier (lock+lead), true = manual laying
    private double _manualRangeOffsetM;  // P05-2: wheel-adjusted range bias on the cursor point
    private Vec3? _manualAimPoint;       // live manual aim point (marker + order)
    private Ship? _aimTarget;

    private readonly Dictionary<string, ShipVisual> _shipVisuals = [];
    private readonly Dictionary<string, AircraftVisual> _aircraftVisuals = [];
    private readonly List<(Vec3 Pos, double Time, int Kind)> _effects = [];
    private long _fxCursor;
    private readonly Dictionary<string, Vec3> _shipMounts = [];
    private readonly Dictionary<string, double> _gunCaliberByShip = [];

    private CameraRig _cameraRig = null!;
    private FxLayer _fx = null!;
    private ProjectileTracers _tracers = null!;
    private Node3D _shipsRoot = null!;
    private Node3D _aircraftRoot = null!;
    private HudPanel _hud = null!;
    private TacticalMap _tacticalMap = null!;
    private PauseMenu _pauseMenu = null!;
    private AudioManager? _audio;
    private bool _reportShown;

    // Islands are decorative scenery (same layout the 2D view used).
    private static readonly (float X, float Z, float Radius)[] Islands =
    {
        (-9000f, -12000f, 900f),
        (11000f, 8000f, 1200f),
        (6000f, -15000f, 700f),
    };

    public override void _Ready()
    {
        AppEnv.Install();
        string repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            GD.PushError("repo root not found above the Godot project");
            return;
        }

        var repo = DataRepository.LoadFromDirectory(Path.Combine(repoRoot, "data"));
        // Phase 06 sea trial: compile + register the carried builder design (same data
        // path as historical ships — the runner sees no difference).
        if (SessionState.CompiledDesign is { } carried)
        {
            var compiled = ShipCompiler.Compile(carried);
            if (compiled.Ok && compiled.Ship is not null)
            {
                repo.RegisterCompiled(compiled.Ship);
                GD.Print($"P06 sea trial: '{compiled.Ship.Id}' compiled " +
                         $"({compiled.Derived!.WeightT:0} t, draft {compiled.Derived.StaticDraftM:0.00} m)");
            }
            else
            {
                GD.Print("P06 sea trial: design failed to compile — falling back to scenario ships");
                foreach (var issue in compiled.Issues)
                {
                    GD.Print($"  {issue}");
                }
            }

            SessionState.CompiledDesign = null;
        }

        string scenarioFile = OS.GetEnvironment("NT_FRONTEND_SCENARIO");
        if (scenarioFile.Length == 0)
        {
            scenarioFile = "bb_duel.json";
        }
        var scenario = BattleScenario.Load(Path.Combine(repoRoot, "scenarios", scenarioFile));
        var pick = OS.GetEnvironment("NT_FRONTEND_SHIP") is { } envShip && envShip.Length > 0
            ? envShip
            : SessionState.SelectedShipId;
        _runner = pick is not null && repo.Ships.ContainsKey(pick)
            ? new BattleRunner(repo, scenario, playerShipOverride: pick)
            : new BattleRunner(repo, scenario);
        _shotPath = OS.GetEnvironment("NT_FRONTEND_SHOT");

        BuildEnvironment();
        BuildShips();
        BuildAircraft();

        _fx = new FxLayer { Name = "FxLayer" };
        AddChild(_fx);
        _tracers = new ProjectileTracers { Name = "Projectiles" };
        AddChild(_tracers);

        _cameraRig = new CameraRig { Name = "CameraRig" };
        AddChild(_cameraRig);
        _cameraRig.Track(this, _runner.Ships[0].WorldPosition);

        _hud = new HudPanel { Name = "Hud" };
        AddChild(_hud);
        _tacticalMap = new TacticalMap { Name = "TacticalMap" };
        AddChild(_tacticalMap);
        _tacticalMap.Bind(_runner);
        // P05-3: the HUD reads the runner + player ship through Bind. This call had been
        // missing since the Phase-02 3D port (the old text HUD silently never updated —
        // the Phase-05 graphical panels made the latent bug visible).
        _hud.Bind(_runner, _runner.Ships[0]);
        _pauseMenu = new PauseMenu { Name = "PauseMenu" };
        AddChild(_pauseMenu);

        _audio = new AudioManager();
        AddChild(_audio);
        var settings = UserSettings.Load();
        KeyBinds.Load(settings);
        L10n.Language = settings.Language;
        AudioManager.ApplyVolumes(settings.MasterVolume, settings.EffectsVolume, settings.AmbientVolume);
        _audio.StartAmbient();

        _runner.HandControlToPlayer(_runner.Ships[0].TargetId);

        GD.Print($"P02: 3D battle wired, ships={_runner.Ships.Count}, " +
                 $"player={_runner.PlayerShip?.TargetId ?? "none"}, scenario={scenarioFile}, " +
                 $"result={_runner.Battle.Result}");
        AppEnv.Info($"3D battle wired: ships={_runner.Ships.Count} player={_runner.PlayerShip?.TargetId ?? "none"} scenario={scenarioFile}");

        if (OS.GetEnvironment("NT_FRONTEND_SMOKE") == "1")
        {
            RunSmokeStructureCheck();
        }
    }

    private void BuildEnvironment()
    {
        // Daylight sea environment.
        var env = new WorldEnvironment { Name = "Environment" };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            AmbientLightEnergy = 0.7f,
        };
        var sky = new Sky { SkyMaterial = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.38f, 0.55f, 0.78f),
            SkyHorizonColor = new Color(0.72f, 0.80f, 0.86f),
            GroundBottomColor = new Color(0.06f, 0.12f, 0.16f),
            GroundHorizonColor = new Color(0.70f, 0.78f, 0.84f),
        } };
        environment.Sky = sky;
        env.Environment = environment;
        AddChild(env);

        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-42, 35, 0),
            LightEnergy = 1.2f,
        };
        sun.LightColor = new Color(1f, 0.96f, 0.88f);
        AddChild(sun);

        // Ocean: a huge flat plane at y=0 (visual only; Core already owns buoyancy/flooding).
        var ocean = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(160000, 160000) },
        };
        ocean.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.07f, 0.23f, 0.34f),
            Roughness = 0.15f,
            Metallic = 0.1f,
        };
        AddChild(ocean);

        foreach (var (x, z, r) in Islands)
        {
            var island = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = r * 0.7f, BottomRadius = r, Height = 140f },
                Position = new Vector3(x, -30f, z),
            };
            island.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.30f, 0.36f, 0.22f), Roughness = 1f };
            AddChild(island);
        }

        _shipsRoot = new Node3D { Name = "Ships" };
        AddChild(_shipsRoot);
        _aircraftRoot = new Node3D { Name = "Aircraft" };
        AddChild(_aircraftRoot);
    }

    private void BuildShips()
    {
        foreach (var ship in _runner!.Ships)
        {
            var visual = new ShipVisual(ship);
            _shipsRoot.AddChild(visual);
            _shipVisuals[ship.TargetId] = visual;
            _shipMounts[ship.TargetId] = ship.WorldPosition;
        }
    }

    private void BuildAircraft()
    {
        foreach (var aircraft in _runner!.Aircraft)
        {
            var visual = new AircraftVisual(aircraft);
            _aircraftRoot.AddChild(visual);
            _aircraftVisuals[aircraft.TargetId] = visual;
        }
    }

    private Ship? _playerShip => _runner?.PlayerShip;

    // ------------------------------------------------------------- player commands

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton m && m.Pressed && _manualAiming)
        {
            // P05-2 manual tier: the wheel adjusts firing range (±100 m per notch);
            // camera zoom is suspended while manual aiming (CameraRig.WheelZoomEnabled).
            if (m.ButtonIndex == MouseButton.WheelUp)
            {
                _manualRangeOffsetM = System.Math.Min(2000, _manualRangeOffsetM + 100);
            }
            else if (m.ButtonIndex == MouseButton.WheelDown)
            {
                _manualRangeOffsetM = System.Math.Max(-2000, _manualRangeOffsetM - 100);
            }
        }
    }

    /// <summary>Per-frame helm input: W/S throttle, A/D rudder, X centers rudder.
    /// P05-1: rates accumulate locally, then the intent travels through HelmCommand —
    /// the frontend never writes Ship fields directly.</summary>
    private void ApplyHelm(double delta)
    {
        if (_playerShip is null || !_playerShip.Alive || _runner is null)
        {
            return;
        }

        const double throttleRate = 0.25;
        const double rudderRate = 0.8;
        double throttle = _playerShip.ThrottleCommand;
        double rudder = _playerShip.RudderCommand;
        bool changed = false;
        if (KeyBinds.Down(KeyBinds.ThrottleUp))
        {
            throttle = System.Math.Min(1.0, throttle + throttleRate * delta);
            changed = true;
        }
        if (KeyBinds.Down(KeyBinds.ThrottleDown))
        {
            throttle = System.Math.Max(0.0, throttle - throttleRate * delta);
            changed = true;
        }
        if (KeyBinds.Down(KeyBinds.RudderPort))
        {
            rudder = System.Math.Max(-1.0, rudder - rudderRate * delta);
            changed = true;
        }
        if (KeyBinds.Down(KeyBinds.RudderStbd))
        {
            rudder = System.Math.Min(1.0, rudder + rudderRate * delta);
            changed = true;
        }
        if (KeyBinds.Down(KeyBinds.RudderCenter))
        {
            rudder = 0;
            changed = true;
        }

        if (changed)
        {
            _runner.Submit(new HelmCommand(throttle, rudder));
        }
    }

    /// <summary>R toggles the main battery between AP and HE (R3 contract) — P05-1: via
    /// ShellSelectCommand.</summary>
    private void ApplyShellToggle()
    {
        if (_playerShip is null || _runner is null)
        {
            return;
        }

        bool rHeld = KeyBinds.Down(KeyBinds.ShellToggle);
        if (rHeld && !_rHeld)
        {
            _playerHe = !_playerHe;
            _runner.Submit(new ShellSelectCommand(_playerHe));
            _hud.SetShellSelection(_playerHe);
        }

        _rHeld = rHeld;
    }

    /// <summary>G toggles the two fire-control tiers (P05-2): auto = lock a target and
    /// the battery solves lead with the spotter correction loop; manual = the player lays
    /// direction (cursor sea point) + range (wheel) herself, no spotter.</summary>
    private void ApplyAimModeToggle()
    {
        bool gHeld = KeyBinds.Down(KeyBinds.AimMode);
        if (gHeld && !_gHeld)
        {
            _manualAiming = !_manualAiming;
            _manualRangeOffsetM = 0;
            _manualAimPoint = null;
            if (_cameraRig is not null)
            {
                _cameraRig.WheelZoomEnabled = !_manualAiming;
            }
            _runner?.Submit(new GunCeaseFireCommand());
            _hud?.SetAimMode(_manualAiming);
            GD.Print($"P05 aim mode: {(_manualAiming ? "MANUAL (cursor+wheel)" : "AUTO (lock+lead)")}");
        }

        _gHeld = gHeld;
    }

    /// <summary>T locks/unlocks the currently hovered enemy (P05-2 auto tier keeps the
    /// battery engaged when the cursor drifts off a locked target).</summary>
    private void ApplyTargetLock()
    {
        if (_runner is null)
        {
            return;
        }

        bool tHeld = KeyBinds.Down(KeyBinds.Lock);
        if (tHeld && !_tHeld)
        {
            string? hovered = _aimTarget?.TargetId;
            string? current = _runner.PlayerLockedTargetId;
            string? next = hovered is not null && hovered != current ? hovered : null;
            _runner.Submit(new TargetAssignCommand(next));
            GD.Print($"P05 lock: {next ?? "cleared"}");
        }

        _tHeld = tHeld;
    }

    /// <summary>Fire control (P05-2 two tiers):
    /// auto — engage the enemy nearest the cursor ray (or the locked one);
    /// manual — lay the battery on the cursor's sea point, wheel-adjusted range.</summary>
    private void ApplyFireControl()
    {
        if (_playerShip is null || !_playerShip.Alive || _runner is null)
        {
            return;
        }

        if (_manualAiming)
        {
            ApplyManualAim();
            return;
        }

        var cam = _cameraRig.Camera;
        var mouse = GetViewport().GetMousePosition();
        var rayOrigin = cam.ProjectRayOrigin(mouse);
        var rayNormal = cam.ProjectRayNormal(mouse);
        var origin = new Vec3(rayOrigin.X, rayOrigin.Y, rayOrigin.Z);
        var normal = new Vec3(rayNormal.X, rayNormal.Y, rayNormal.Z);

        Ship? hover = null;
        double best = double.MaxValue;
        foreach (var ship in _runner.Ships)
        {
            if (!ship.Alive || ship.Team?.Id == _playerShip.Team?.Id)
            {
                continue;
            }

            // Closest approach of the ship centre to the camera ray (screen-space picking).
            var rel = ship.WorldPosition - origin;
            double along = rel.Dot(normal);
            if (along <= 0)
            {
                continue;
            }

            Vec3 closest = origin + normal * along;
            double d = Vec3.Distance(ship.WorldPosition, closest);
            double capture = System.Math.Max(120, ship.Definition.LengthM);
            if (d < capture && d < best)
            {
                best = d;
                hover = ship;
            }
        }

        // P05-2 auto tier: the hovered enemy wins; a T-locked target keeps the battery
        // engaged when the cursor drifts off her.
        Ship? effective = hover ?? LockedTargetShip();
        _aimTarget = effective;
        _manualAimPoint = null;
        if (effective is null)
        {
            _runner.Submit(new GunCeaseFireCommand());
        }
        else
        {
            _runner.Submit(new GunEngageCommand(effective.TargetId));
        }
    }

    /// <summary>P05-2 manual tier: direction from the cursor's intersection with the sea
    /// plane, fine range from the wheel offset. Submitted every frame the point exists.</summary>
    private void ApplyManualAim()
    {
        var cam = _cameraRig.Camera;
        var mouse = GetViewport().GetMousePosition();
        var rayOrigin = cam.ProjectRayOrigin(mouse);
        var rayNormal = cam.ProjectRayNormal(mouse);
        var origin = new Vec3(rayOrigin.X, rayOrigin.Y, rayOrigin.Z);
        var normal = new Vec3(rayNormal.X, rayNormal.Y, rayNormal.Z);
        if (normal.Y >= -1e-3)
        {
            _manualAimPoint = null; // looking at the sky
            _runner.Submit(new GunCeaseFireCommand());
            return;
        }

        Vec3 point = origin + normal * (-origin.Y / normal.Y); // y = 0 sea plane
        var rel = point - _playerShip.WorldPosition;
        double range = System.Math.Sqrt(rel.X * rel.X + rel.Z * rel.Z);
        double adjusted = System.Math.Max(200, range + _manualRangeOffsetM);
        var dir = new Vec3(rel.X, 0, rel.Z).Normalized();
        point = _playerShip.WorldPosition + dir * adjusted;

        _manualAimPoint = point;
        _aimTarget = null;
        _runner.Submit(new GunManualAimCommand(point));
    }

    private Ship? LockedTargetShip() =>
        _runner?.PlayerLockedTargetId is { } id
            ? _runner.Ships.FirstOrDefault(s => s.TargetId == id && s.Alive)
            : null;

    // ------------------------------------------------------------------ sim + fx

    public override void _Process(double delta)
    {
        if (_runner is null)
        {
            return;
        }

        bool smoke = OS.GetEnvironment("NT_FRONTEND_SMOKE") == "1";
        long profFrameStart = _profil ? (long)Time.GetTicksUsec() : 0;
        double budget = OS.GetEnvironment("NT_FRONTEND_QUICK") == "1"
            ? delta * 200.0
            : smoke ? 0.25 : delta * _simSpeed;
        // Phase 04 perf: the accumulator loop previously had NO per-frame cap — one slow
        // frame inflates the next frame's budget into dozens of World.Step calls (death
        // spiral). Normal GUI mode now simulates on a bounded budget; leftover time
        // carries to the next frame (smooth rendering first, sim catch-up spread out).
        int maxSteps = OS.GetEnvironment("NT_FRONTEND_QUICK") == "1" || smoke
            ? int.MaxValue
            : _maxStepsPerFrame;
        long profSimStart = _profil ? (long)Time.GetTicksUsec() : 0;
        int steps = 0;
        _smokeAccumulator += budget;
        while (_smokeAccumulator >= _runner.World.FixedDeltaTime
               && _runner.Battle.Result == NavyThunder.Core.Battle.BattleResult.Running
               && steps < maxSteps
               && !_pauseMenu.Paused)
        {
            _runner.World.Step();
            _smokeAccumulator -= _runner.World.FixedDeltaTime;
            steps++;
        }
        long profSimEnd = _profil ? (long)Time.GetTicksUsec() : 0;

        // P05-8: ESC toggles the pause overlay; a paused scene freezes the simulation
        // (deterministic Core has no pause — the presentation loop just stops stepping).
        bool escHeld = KeyBinds.Down(KeyBinds.Pause);
        if (escHeld && !_escHeld && !_reportShown)
        {
            _pauseMenu.Toggle();
        }

        _escHeld = escHeld;

        if (!_pauseMenu.Paused)
        {
            ApplyHelm(delta);
            ApplyAimModeToggle();
            ApplyFireControl();
            ApplyTargetLock();
            ApplyShellToggle();
            CollectEffects();
        }
        long profEventEnd = _profil ? (long)Time.GetTicksUsec() : 0;

        if (_runner.World.Time >= _nextTimePrintAt)
        {
            _nextTimePrintAt += 10;
            GD.Print($"P02 t={_runner.World.Time:0}s projectiles={_runner.Ballistics.Projectiles.Count}");
        }

        foreach (var ship in _runner.Ships)
        {
            _shipMounts[ship.TargetId] = ship.WorldPosition;
        }

        // Simulation → presentation sync (identity mapping; PHASE_02 §8).
        foreach (var (targetId, visual) in _shipVisuals)
        {
            var gunStates = _runner.Guns.VisualStatesOf(targetId)
                .ToDictionary(g => g.GunId, g => g);
            var burning = _runner.Fire.Fires
                .Where(f => f.Active && f.HostId.StartsWith(targetId + "/"))
                .Select(f => f.Position)
                .ToList();
            visual.UpdateFromCore(_runner.World.Time, gunStates, burning, delta);
        }

        foreach (var visual in _aircraftVisuals.Values)
        {
            visual.UpdateFromCore();
        }

        _tracers.Sync(_runner.Ballistics.Projectiles, _runner.Torpedoes.DebugTorpedoes);
        long profFrameEnd = _profil ? (long)Time.GetTicksUsec() : 0;
        if (_profil)
        {
            ProfSample(delta, steps, profFrameStart, profSimEnd, profEventEnd, profFrameEnd);
        }

        if (_playerShip is not null)
        {
            _cameraRig.Track(_shipVisuals[_playerShip.TargetId], _playerShip.WorldPosition);
            _audio?.SetListener(new Vector2((float)_playerShip.WorldPosition.X, (float)_playerShip.WorldPosition.Z));
        }
        if (OS.GetEnvironment("NT_FRONTEND_CAMFOCUS") is { } focusEnv && focusEnv.Length > 0)
        {
            // Verification hook: lock the camera focus to a world point (e.g. a formation
            // centre) instead of following the player ship.
            var parts = focusEnv.Split(',');
            if (parts.Length == 2 && float.TryParse(parts[0], out float fx) && float.TryParse(parts[1], out float fz))
            {
                _cameraRig.LockFocus(new Vector3(fx, 0, fz));
            }
        }
        _hud.SetAimTarget(_aimTarget);
        _hud.UpdateHud(_runner.World.Time, _runner.Guns.ReloadRemainingOf(_playerShip?.TargetId ?? ""));
        _tacticalMap.PushEffects(_effects, _runner.World.Time, showFx: true);

        // Aim indicator (3D): a marker ring over the hovered enemy.
        UpdateAimMarker();

        if (smoke)
        {
            RunSmokeSyncChecks();
            if (_runner.World.Time >= _nextReportAt)
            {
                GD.Print($"P02 smoke: t={_runner.World.Time:0}s " +
                         $"alive={_runner.Ships.Count(s => s.Alive)} result={_runner.Battle.Result}");
                _nextReportAt += 60;
            }
            if (_runner.Battle.Result != NavyThunder.Core.Battle.BattleResult.Running)
            {
                GD.Print($"P02 smoke done: result={_runner.Battle.Result} " +
                         $"winner={_runner.Battle.WinnerTeamId ?? "none"} at t={_runner.World.Time:0}s; " +
                         $"syncFailures={_syncFailures}");
                GD.Print(_syncFailures == 0
                    ? "P02 SMOKE PASS (structure + core→visual sync)"
                    : $"P02 SMOKE FAIL (syncFailures={_syncFailures})");
                GetTree().Quit(_syncFailures == 0 ? 0 : 1);
            }
            return;
        }

        if (!_shotTaken && _shotPath.Length > 0 && _runner.World.Time >= ShotTime())
        {
            var img = GetViewport().GetTexture().GetImage();
            img.SavePng(_shotPath);
            _shotTaken = true;
            var ps = _playerShip!;
            var pv = _shipVisuals[ps.TargetId];
            GD.Print($"SHOT at t={_runner.World.Time:0}s shipHeading={ps.HeadingDeg:0.#}° " +
                     $"nodeYaw={pv.Rotation.Y * 180 / Math.PI:0.#}° nodePos={pv.Position} " +
                     $"camGlobal={_cameraRig.GlobalPosition} camFwd={_cameraRig.Camera.GlobalTransform.Basis.Z}");
        }

        if (_runner.Battle.Result != NavyThunder.Core.Battle.BattleResult.Running && !_reportShown)
        {
            _reportShown = true;
            _hud.ShowReport(_runner, _shotPath);
            AppEnv.Info($"P02: report shown winner={_runner.Battle.WinnerTeamId ?? "none"}");
        }
    }

    private MeshInstance3D? _aimMarker;
    private MeshInstance3D? _leadMarker;   // P05-2: predicted aim point (auto tier)
    private MeshInstance3D? _manualMarker; // P05-2: manual aim point on the sea

    private void UpdateAimMarker()
    {
        if (_aimTarget is { Alive: true } aim)
        {
            _aimMarker ??= new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = 0.86f, OuterRadius = 1f },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(1f, 0.55f, 0.1f),
                    EmissionEnabled = true,
                    Emission = new Color(1f, 0.5f, 0.1f) * 1.4f,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
            };
            if (_aimMarker.GetParent() is null)
            {
                AddChild(_aimMarker);
            }

            _aimMarker.Visible = true;
            float r = (float)(aim.Definition.LengthM * 0.65f);
            _aimMarker.Scale = new Vector3(r, r, r);
            _aimMarker.Position = new Vector3(
                (float)aim.WorldPosition.X, 2f, (float)aim.WorldPosition.Z);

            // P05-2 lead indicator: where the ballistic solution expects the shells to
            // arrive (FcsSolver iteration over muzzle speed + target velocity; read-only
            // presentation math — the battery's own solution stays inside the Core).
            double muzzle = _runner!.Guns.MuzzleSpeedOf(_playerShip!.TargetId);
            if (muzzle > 0)
            {
                var solution = FcsSolver.SolveLead(
                    _playerShip.WorldPosition, muzzle, aim.WorldPosition, TargetVelocityOf(aim));
                _leadMarker ??= new MeshInstance3D
                {
                    Mesh = new SphereMesh { Radius = 1f, Height = 2f },
                    MaterialOverride = new StandardMaterial3D
                    {
                        AlbedoColor = new Color(1f, 0.85f, 0.2f),
                        EmissionEnabled = true,
                        Emission = new Color(1f, 0.8f, 0.15f) * 1.6f,
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    },
                };
                if (_leadMarker.GetParent() is null)
                {
                    AddChild(_leadMarker);
                }

                _leadMarker.Visible = true;
                _leadMarker.Position = new Vector3(
                    (float)solution.AimPoint.X, 4f, (float)solution.AimPoint.Z);
            }
        }
        else
        {
            if (_aimMarker is not null)
            {
                _aimMarker.Visible = false;
            }

            if (_leadMarker is not null)
            {
                _leadMarker.Visible = false;
            }
        }

        if (_manualAimPoint is { } point)
        {
            _manualMarker ??= new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = 0.8f, OuterRadius = 1f },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.4f, 1f, 0.5f),
                    EmissionEnabled = true,
                    Emission = new Color(0.3f, 1f, 0.4f) * 1.4f,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
            };
            if (_manualMarker.GetParent() is null)
            {
                AddChild(_manualMarker);
            }

            _manualMarker.Visible = true;
            _manualMarker.Scale = new Vector3(45f, 45f, 45f);
            _manualMarker.Position = new Vector3((float)point.X, 2f, (float)point.Z);
        }
        else if (_manualMarker is not null)
        {
            _manualMarker.Visible = false;
        }
    }

    private static Vec3 TargetVelocityOf(Ship ship) => new(
        System.Math.Sin(ship.HeadingDeg * System.Math.PI / 180.0) * ship.SpeedKnots * 0.514444,
        0,
        System.Math.Cos(ship.HeadingDeg * System.Math.PI / 180.0) * ship.SpeedKnots * 0.514444);

    /// <summary>EventLog cursor → 3D FX + audio (extends the Phase R2 CollectEffects).</summary>
    // ------------------------------------------------------------------ profiling (NT_FRONTEND_PROFIL=1)

    private bool _profil = OS.GetEnvironment("NT_FRONTEND_PROFIL") == "1";
    private int _maxStepsPerFrame = 4; // normal GUI cap: ~2x real time @60fps worst case
    private double _profWindow;
    private double _profFrames;
    private double _profFrameMsSum, _profFrameMsMax;
    private double _profSimMsSum;
    private double _profEventMsSum, _profSyncMsSum;
    private double _profStepsSum;
    private long _profLastCpu;
    private double _profNextPrint = 4.0;

    private void ProfSample(double delta, int steps, long frameStart, long simEnd, long eventEnd, long frameEnd)
    {
        _profWindow += delta;
        _profFrames++;
        _profStepsSum += steps;
        double frameMs = (frameEnd - frameStart) / 1000.0;
        _profFrameMsSum += frameMs;
        _profFrameMsMax = System.Math.Max(_profFrameMsMax, frameMs);
        _profSimMsSum += (simEnd - frameStart) / 1000.0;
        _profEventMsSum += (eventEnd - simEnd) / 1000.0;
        _profSyncMsSum += (frameEnd - eventEnd) / 1000.0;

        if (_profWindow < _profNextPrint)
        {
            return;
        }

        double wall = _profWindow;
        var proc = System.Diagnostics.Process.GetCurrentProcess();
        long cpuNow = proc.TotalProcessorTime.Ticks;
        double cpuPct = _profLastCpu > 0 ? (cpuNow - _profLastCpu) / (double)System.Diagnostics.Stopwatch.Frequency / wall * 100.0 : 0;
        _profLastCpu = cpuNow;
        var vp = GetViewport();
        int drawCalls = (int)vp.GetRenderInfo(Godot.Viewport.RenderInfoType.Visible, Godot.Viewport.RenderInfo.DrawCallsInFrame);
        int objects = (int)vp.GetRenderInfo(Godot.Viewport.RenderInfoType.Visible, Godot.Viewport.RenderInfo.ObjectsInFrame);
        double fps = Engine.GetFramesPerSecond();

        GD.Print($"PROF simSpeed={_simSpeed:0.#} fps={fps:0} frameMs(avg)={_profFrameMsSum / _profFrames:0.0} " +
                 $"frameMs(max)={_profFrameMsMax:0.0} simMs={_profSimMsSum / _profFrames:0.0} " +
                 $"eventMs={_profEventMsSum / _profFrames:0.0} syncMs={_profSyncMsSum / _profFrames:0.0} " +
                 $"steps/frame={_profStepsSum / _profFrames:0.0} cpu%={cpuPct:0} drawCalls={drawCalls} objects={objects}");
        _profWindow = 0;
        _profFrames = 0;
        _profFrameMsSum = _profFrameMsMax = _profSimMsSum = _profEventMsSum = _profSyncMsSum = _profStepsSum = 0;
        _profNextPrint = 4.0;
    }

    private void CollectEffects()
    {
        if (_runner is null)
        {
            return;
        }

        foreach (var e in _runner.World.Events.After(_fxCursor))
        {
            switch (e)
            {
                case ShellDetonation d:
                {
                    bool isSplash = d.OnWaterSurface || d.TargetId.Length == 0;
                    _fx.Spawn(isSplash ? FxLayer.Kind.Splash : FxLayer.Kind.Explosion, d.Position);
                    _effects.Add((d.Position, d.Time, isSplash ? 0 : 1));
                    _audio?.PlayExplosion(d.Position, big: d.AfterPenetration);
                    // P05-6: a player shell splashing short/long feeds the splash indicator.
                    if (isSplash && d.ShooterId == _playerShip?.TargetId)
                    {
                        _hud.PushSplash();
                    }

                    break;
                }
                case ProjectileArmorImpact impact:
                    _fx.Spawn(FxLayer.Kind.HitFlash, impact.Position);
                    _effects.Add((impact.Position, impact.Time, 1));
                    // P05-6: five-state hit feedback for hits the player makes or takes.
                    if (impact.ShooterId == _playerShip?.TargetId || impact.TargetId == _playerShip?.TargetId)
                    {
                        _hud.PushImpact(impact);
                    }

                    break;
                case GunFired gf:
                {
                    var mount = _shipMounts.TryGetValue(gf.ShipId, out var m)
                        ? m
                        : Vec3.Zero;
                    _fx.Spawn(FxLayer.Kind.MuzzleFlash, mount);
                    _effects.Add((mount, gf.Time, 2));
                    _audio?.PlayGun(GunCaliberOf(gf.ShipId, gf.GunId), mount);
                    break;
                }
                case NavyThunder.Core.Ships.MagazineDetonation mag:
                    _fx.Spawn(FxLayer.Kind.BigExplosion, mag.Position);
                    _effects.Add((mag.Position, mag.Time, 3));
                    _audio?.PlayExplosion(mag.Position, big: true);
                    break;
                case NavyThunder.Core.Ships.ShipDestroyed destroyed:
                {
                    if (_shipMounts.TryGetValue(destroyed.ShipId, out var pos))
                    {
                        _fx.Spawn(FxLayer.Kind.Sink, pos);
                        _audio?.PlayExplosion(pos, big: true);
                    }
                    break;
                }
            }
        }

        _fxCursor = _runner.World.Events.TotalRecorded;
    }

    private float GunCaliberOf(string shipId, string gunId)
    {
        var ship = _runner!.Ships.FirstOrDefault(s => s.TargetId == shipId);
        var gun = ship?.Definition.Guns.FirstOrDefault(g => g.Id == gunId);
        if (ship is null || gun is null)
        {
            return 127f;
        }

        // NavalGunDefinition exposes no caliber; derive it from the shell id prefix
        // ("wt_356mm_ap" → 356) with a sane fallback (presentation only).
        var shellId = gun.ShellId;
        int mm = shellId.IndexOf("mm", StringComparison.Ordinal);
        if (mm > 0 && float.TryParse(shellId.AsSpan(0, mm), out float caliber))
        {
            return caliber;
        }

        return gun.Barrels >= 3 ? 406f : 127f;
    }

    // ------------------------------------------------------------------ smoke tests

    /// <summary>PHASE_02 §32/33: verify the 3D structure exists before the battle runs.</summary>
    private void RunSmokeStructureCheck()
    {
        var missing = new List<string>();
        if (GetNodeOrNull<Node3D>("Ships") is null) missing.Add("Ships(Node3D)");
        if (GetNodeOrNull<Node3D>("Aircraft") is null) missing.Add("Aircraft(Node3D)");
        if (GetNodeOrNull<WorldEnvironment>("Environment") is null) missing.Add("WorldEnvironment");
        if (GetNodeOrNull<DirectionalLight3D>("Sun") is null) missing.Add("DirectionalLight3D");
        if (GetNodeOrNull<CameraRig>("CameraRig") is null || GetNodeOrNull<CameraRig>("CameraRig")!.Camera is null) missing.Add("CameraRig/Camera3D");
        if (GetNodeOrNull<FxLayer>("FxLayer") is null) missing.Add("FxLayer(Node3D)");
        if (GetNodeOrNull<ProjectileTracers>("Projectiles") is null) missing.Add("Projectiles(Node3D)");
        if (GetNodeOrNull<HudPanel>("Hud") is null) missing.Add("HUD(CanvasLayer)");
        if (GetNodeOrNull<TacticalMap>("TacticalMap") is null) missing.Add("TacticalMap(CanvasLayer)");
        if (GetNodeOrNull<PauseMenu>("PauseMenu") is null) missing.Add("PauseMenu(CanvasLayer)");
        if (_shipVisuals.Count != _runner!.Ships.Count) missing.Add($"ShipVisual count {_shipVisuals.Count} != {_runner.Ships.Count}");
        if (_shipVisuals.Values.Any(v => v is not Node3D)) missing.Add("ShipVisual not Node3D");
        if (_runner!.World.FixedDeltaTime <= 0) missing.Add("Simulation not wired");

        // P05-6: hit-feedback classifier mapping self-check (pure, five states).
        string classifierFault = HitFeedbackSmokeCheck();
        if (classifierFault.Length > 0)
        {
            missing.Add(classifierFault);
        }

        _smokeStructureChecked = true;
        GD.Print(missing.Count == 0
            ? "P02 SMOKE STRUCT PASS: Battle3D root + Ocean/Environment + " +
              $"{_shipVisuals.Count} ShipVisual + Camera3D + FX + Projectiles + HUD + TacticalMap + PauseMenu + hit feedback"
            : $"P02 SMOKE STRUCT FAIL: missing [{string.Join(", ", missing)}]");
        if (missing.Count > 0)
        {
            _syncFailures += missing.Count;
        }
    }

    /// <summary>P05-6 smoke: the event→icon mapping must be exactly these five states.</summary>
    private static string HitFeedbackSmokeCheck()
    {
        static ProjectileArmorImpact Impact(PlateResolution outcome, bool fuze) => new()
        {
            Outcome = outcome, FuzeTriggered = fuze, Position = Vec3.Zero,
        };

        bool ok =
            HitFeedback.Classify(Impact(PlateResolution.Penetrated, true)) == HitFeedback.Kind.Penetration &&
            HitFeedback.Classify(Impact(PlateResolution.Penetrated, false)) == HitFeedback.Kind.OverPen &&
            HitFeedback.Classify(Impact(PlateResolution.Ricocheted, false)) == HitFeedback.Kind.Ricochet &&
            HitFeedback.Classify(Impact(PlateResolution.Stopped, false)) == HitFeedback.Kind.NoPen;
        return ok ? "" : "HitFeedback classifier mapping broken";
    }

    /// <summary>PHASE_02 §34: Core position/heading must equal ShipVisual transform.</summary>
    private void RunSmokeSyncChecks()
    {
        if (!_smokeStructureChecked || _runner is null || _runner.World.Time < _nextSyncCheckAt)
        {
            return;
        }

        _nextSyncCheckAt += 30;
        int bad = 0;
        foreach (var (targetId, visual) in _shipVisuals)
        {
            var ship = visual.Ship;
            if (!ship.Alive)
            {
                continue; // sunk ships carry an intentional presentation sink offset
            }
            var corePos = ship.WorldPosition;
            var nodePos = visual.Position;
            double dist = new Vec3(nodePos.X - corePos.X, nodePos.Y - corePos.Y, nodePos.Z - corePos.Z).Length;
            double coreYaw = NormalizeRad(ship.HeadingDeg * Math.PI / 180.0);
            double nodeYaw = NormalizeRad(visual.Rotation.Y);
            double yawDiff = System.Math.Abs(AngleDiff(coreYaw, nodeYaw));
            if (dist > 0.05 || yawDiff > 0.01)
            {
                bad++;
                GD.Print($"SYNC MISMATCH {targetId}: dist={dist:0.000} yawDiff={yawDiff * 180 / Math.PI:0.000}°");
            }
        }

        _syncFailures += bad;
        if (bad == 0)
        {
            var first = _shipVisuals.Values.First();
            GD.Print($"P02 SYNC CHECK t={_runner.World.Time:0}s: PASS ({_shipVisuals.Count} ships, 0.05 m / 0.01 rad) " +
                     $"[dbg ship heading={first.Ship.HeadingDeg:0.#}° nodeYaw={first.Rotation.Y * 180 / Math.PI:0.#}° " +
                     $"camGlobal={_cameraRig.GlobalPosition} camFwd={_cameraRig.Camera.GlobalTransform.Basis.Z}]");
        }
    }

    private static double NormalizeRad(double rad)
    {
        rad %= System.Math.PI * 2;
        return rad < 0 ? rad + System.Math.PI * 2 : rad;
    }

    private static double AngleDiff(double a, double b)
    {
        double d = (b - a) % (System.Math.PI * 2);
        if (d > System.Math.PI)
        {
            d -= System.Math.PI * 2;
        }
        if (d < -System.Math.PI)
        {
            d += System.Math.PI * 2;
        }
        return d;
    }

    private static float ShotTime() =>
        OS.GetEnvironment("NT_FRONTEND_SHOTTIME") is { } s && float.TryParse(s, out float t) ? t : 60f;

    /// <summary>Packaged builds keep data/ next to the exe; dev runs walk to the sln.</summary>
    private static string FindRepoRoot()
    {
        var exeDir = Path.GetDirectoryName(OS.GetExecutablePath());
        if (exeDir is not null && Directory.Exists(Path.Combine(exeDir, "data")))
        {
            return exeDir;
        }

        var dir = ProjectSettings.GlobalizePath("res://");
        while (dir is not null && !File.Exists(Path.Combine(dir, "NavyThunder.slnx")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        return dir;
    }
}
