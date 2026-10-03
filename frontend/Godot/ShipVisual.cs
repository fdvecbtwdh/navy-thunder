using Godot;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;
using NavyThunder.Core.Ships;

namespace NavyThunder.Frontend;

/// <summary>
/// Phase 02 presentation node for one simulation ship. Reads Core state, never writes it:
/// position/heading come from <see cref="Ship.WorldTransform"/> (Phase 01) with the
/// Core→Godot mapping being IDENTITY — Core world X/Y/Z = Godot world X/Y/Z, and the hull
/// placeholder is built with local +Z = bow so the node's rotation.y = heading radians
/// reproduces the simulation orientation exactly (PROJECT_DESIGN §8.2).
/// The placeholder mesh is DATA-DRIVEN (length/beam/part layout) and is NOT collision or
/// armor geometry — hit detection lives entirely in Core (three-layer separation).
/// </summary>
public partial class ShipVisual : Node3D
{
    private readonly Ship _ship;
    private readonly Dictionary<string, List<Node3D>> _turrets = [];
    private readonly List<CpuParticles3D> _fireFx = [];
    private CpuParticles3D? _smokeFx;
    private float _sinkAge = -1f;

    // Phase 03 real-model support (registry-driven; procedural when absent).
    private bool _useRealModel;
    private readonly List<Node3D> _lodNodes = [];
    private readonly List<float> _lodRanges = [];
    private double _lodTimer;

    private static readonly Color UsnHull = new(0.36f, 0.47f, 0.58f);
    private static readonly Color IjnHull = new(0.55f, 0.48f, 0.42f);
    private static readonly Color NeutralHull = new(0.42f, 0.44f, 0.45f);

    public Ship Ship => _ship;

    /// <summary>True when a converted WT model is driving the visuals.</summary>
    public bool UsesRealModel => _useRealModel;

    public ShipVisual(Ship ship)
    {
        _ship = ship;
    }

    public override void _Ready()
    {
        BuildFireSmoke();
        if (!TryBuildRealModel())
        {
            BuildProcedural();
        }
    }

    /// <summary>
    /// Tries to mount the converted WT model (PHASE_03): registers turret nodes
    /// for the Core's turret groups by nearest-z matching (model turret node
    /// pivot ↔ group mount centroid, tolerance = 25% hull length) and enables
    /// distance-based LOD switching. Returns false when no model is registered
    /// or loading fails — the caller then builds the procedural placeholder.
    /// </summary>
    private bool TryBuildRealModel()
    {
        try
        {
            if (!ShipAssetRegistry.TryGet(_ship.TargetId, out var entry) || entry.Lods.Count == 0)
            {
                return false;
            }

            float halfL = (float)(_ship.Definition.LengthM / 2);
            // Mount LODs far-to-near so the nearest (highest-detail) instance ends on top.
            foreach (var (lodIdx, (file, range)) in entry.Lods.OrderByDescending(kv => kv.Value.RangeM))
            {
                var model = ShipVisualFactory.LoadModel(_ship.TargetId, file);
                if (model is null)
                {
                    GD.PushWarning($"ShipVisual: model load failed for {_ship.TargetId} lod{lodIdx}");
                    return false;
                }
                model.Visible = false;
                AddChild(model);
                _lodNodes.Add(model);
                _lodRanges.Add(range);
            }
            _useRealModel = true;

            RegisterRealTurrets(halfL);
            AppEnv.Info($"ShipVisual: {_ship.TargetId} real model ({_lodNodes.Count} LODs, {_turrets.Count} turret groups bound)");
            return true;
        }
        catch (Exception ex)
        {
            AppEnv.Error($"ShipVisual: real-model path failed for {_ship.TargetId}: {ex.Message}");
            foreach (var lod in _lodNodes)
            {
                lod.QueueFree();
            }
            _lodNodes.Clear();
            _lodRanges.Clear();
            _useRealModel = false;
            return false;
        }
    }

    /// <summary>
    /// Matches rotatable model nodes (turret_*/main_caliber_*) to Core turret
    /// groups. Core mount centroids are Tier-abstract and can sit far from the
    /// real WT barbette positions, so instead of distance matching we cluster
    /// candidate nodes by fore-aft position (10 m gap = new turret), sort both
    /// sides bow→stern and pair them in order ("A" = foremost main turret).
    /// Secondary groups ("S*") stay static in Phase 03.
    /// </summary>
    private void RegisterRealTurrets(float halfL)
    {
        var model = _lodNodes[^1];
        var candidates = new List<(Node3D Node, float Z)>();
        CollectTurretNodes(model, candidates);
        if (candidates.Count == 0)
        {
            var sample = string.Join(",", Enumerable.Range(0, Math.Min(12, model.GetChildCount()))
                .Select(i => model.GetChild(i).Name));
            AppEnv.Info($"ShipVisual: {_ship.TargetId} no turret candidates among {model.GetChildCount()} nodes; sample [{sample}]");
            return;
        }

        // cluster bow→stern: sort by Z desc, split where the gap exceeds 10 m
        var ordered = candidates.OrderByDescending(c => c.Z).ToList();
        var clusters = new List<List<(Node3D Node, float Z)>> { new() };
        foreach (var cand in ordered)
        {
            if (clusters[^1].Count > 0 && clusters[^1][^1].Z - cand.Z > 10f)
            {
                clusters.Add([]);
            }
            clusters[^1].Add(cand);
        }

        var groups = _ship.TurretGroups
            .Where(g => !g.StartsWith('S'))
            .Select(g => (Group: g, Mount: MainBatteryMountZ(g)))
            .Where(g => g.Mount is not null)
            .OrderByDescending(g => g.Mount!.Value)
            .ToList();

        int pairs = Math.Min(clusters.Count, groups.Count);
        for (int i = 0; i < pairs; i++)
        {
            var list = _turrets.TryGetValue(groups[i].Group, out var existing) ? existing : _turrets[groups[i].Group] = [];
            foreach (var (node, _) in clusters[i])
            {
                list.Add(node); // one group heading drives the whole cluster
            }
        }
        AppEnv.Info($"ShipVisual: {_ship.TargetId} turrets: {candidates.Count} candidate nodes in {clusters.Count} "
                    + $"clusters, groups [{string.Join(",", groups.Select(g => g.Group))}], bound {pairs}");
    }

    private static void CollectTurretNodes(Node root, List<(Node3D, float)> into)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Node3D n3)
            {
                var name = n3.Name.ToString();
                bool isTurret = name.StartsWith("turret_", StringComparison.Ordinal)
                                || name.StartsWith("main_caliber_turret", StringComparison.Ordinal)
                                || name.StartsWith("main_caliber_gun", StringComparison.Ordinal);
                if (isTurret)
                {
                    into.Add((n3, n3.Position.Z));
                }
            }
            CollectTurretNodes(child, into);
        }
    }

    /// <summary>Fore-aft centroid of a turret group's Core parts (ship local, +Z bow).</summary>
    private double? MainBatteryMountZ(string group)
    {
        var parts = _ship.Parts.Values
            .Where(p => p.Definition.TurretGroup == group)
            .ToList();
        if (parts.Count == 0)
        {
            return null;
        }
        return parts.Average(p => p.Center.Z);
    }

    /// <summary>Procedural placeholder (unchanged Phase 02 look, fallback only).</summary>
    private void BuildProcedural()
    {
        _useRealModel = false;
        var def = _ship.Definition;
        float halfL = (float)(def.LengthM / 2);
        float halfB = (float)(def.BeamM / 2);

        var hullColor = _ship.Team?.Id switch
        {
            "usn" => UsnHull,
            "ijn" => IjnHull,
            _ => NeutralHull,
        };
        var hullMat = new StandardMaterial3D { AlbedoColor = hullColor, Roughness = 0.75f };
        var darkMat = new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.17f, 0.18f), Roughness = 0.9f };

        // Hull body: waterline at y=0 (freeboard ~2 m, hull extends draft below).
        float draft = (float)Mathf.Clamp(def.DraftM, 2.0, 12.0);
        AddBox(new Vector3(halfB * 2, draft + 2f, halfL * 2 * 0.94f), new Vector3(0, (2f - draft) / 2f, 0), hullMat);

        // Bow wedge (points +Z = bow) and stern block.
        var bow = new MeshInstance3D
        {
            Mesh = new PrismMesh { Size = new Vector3(halfB * 2, draft + 2f, halfL * 0.30f), LeftToRight = 0.5f },
            MaterialOverride = hullMat,
        };
        bow.Position = new Vector3(0, (2f - draft) / 2f, halfL * 0.94f + halfL * 0.15f);
        bow.RotationDegrees = new Vector3(-90, 180, 0); // prism edge faces +Z
        AddChild(bow);

        // Superstructure block + bridge.
        AddBox(new Vector3(halfB * 1.1f, 5f, halfL * 0.22f), new Vector3(0, 3.5f, -halfL * 0.05f), hullMat);
        AddBox(new Vector3(halfB * 0.7f, 3.5f, halfL * 0.10f), new Vector3(0, 7f, -halfL * 0.02f), hullMat);

        // Funnels from boiler parts (data-driven placement).
        foreach (var part in _ship.Parts.Values)
        {
            if (part.Definition.Kind == PartKind.Boiler)
            {
                var funnel = new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = halfB * 0.22f, BottomRadius = halfB * 0.28f, Height = 7f },
                    MaterialOverride = darkMat,
                    Position = PartNodePosition(part),
                };
                AddChild(funnel);
            }
        }

        // Heading verification markers: bow cone (fore-and-aft unambiguous) + port (red)
        // / starboard (green) light dots — catches 90° reversal and port/starboard mirroring.
        var bowMarker = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = halfB * 0.4f, Height = 6f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.85f, 0.2f),
                EmissionEnabled = true,
                Emission = new Color(0.9f, 0.7f, 0.1f),
            },
            Position = new Vector3(0, draft / 2f + 5f, halfL * 0.75f),
        };
        AddChild(bowMarker);
        AddLightDot(new Vector3(-halfB * 0.9f, 4f, halfL * 0.45f), new Color(1f, 0.1f, 0.1f));   // port
        AddLightDot(new Vector3(halfB * 0.9f, 4f, halfL * 0.45f), new Color(0.1f, 1f, 0.2f));    // starboard

        // Turret visuals: one node per turret group, mounted at the group's part centroid
        // (same ship-local frame as the simulation mount — zero conversion).
        foreach (var group in _ship.TurretGroups)
        {
            var parts = _ship.Parts.Values
                .Where(p => p.Definition.TurretGroup == group && p.Definition.Kind == PartKind.Turret)
                .ToList();
            if (parts.Count == 0)
            {
                continue;
            }

            Vec3 centroid = parts[0].Center;
            var turret = new Node3D { Position = new Vector3((float)centroid.X, (float)centroid.Y + 1.5f, (float)centroid.Z) };
            float turretR = Mathf.Max(1.6f, halfB * 0.42f);
            turret.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = turretR, BottomRadius = turretR * 1.15f, Height = 2.6f },
                MaterialOverride = darkMat,
            });
            // Barrel block pointing +Z (Visual Placeholder: core stores no per-gun
            // elevation, so barrels keep a fixed slight elevation — Phase 04 data).
            turret.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.7f, 0.7f, turretR * 3.4f) },
                MaterialOverride = darkMat,
                Position = new Vector3(0, 0.6f, turretR * 1.6f),
                RotationDegrees = new Vector3(-8, 0, 0),
            });
            AddChild(turret);
            (_turrets.TryGetValue(group, out var list) ? list : _turrets[group] = []).Add(turret);
        }
    }

    /// <summary>Fire / smoke emitters (visual only; positions polled from Core each frame).
    /// Anchors live on the ShipVisual root in both real-model and procedural modes —
    /// the Core burning-part positions ARE the damage visual anchors (PHASE_03 §14).</summary>
    private void BuildFireSmoke()
    {
        for (int i = 0; i < 2; i++)
        {
            var fire = new CpuParticles3D
            {
                Emitting = false,
                Amount = 24,
                Lifetime = 0.9f,
                Mesh = new SphereMesh { Radius = 1.4f, Height = 2.8f },
                Direction = new Vector3(0, 1, 0),
                Spread = 20f,
                InitialVelocityMin = 4f,
                InitialVelocityMax = 9f,
                ScaleAmountMin = 0.6f,
                ScaleAmountMax = 1.6f,
                Color = new Color(1f, 0.5f, 0.1f),
            };
            var mat = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.45f, 0.08f),
                EmissionEnabled = true,
                Emission = new Color(1f, 0.4f, 0.05f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            };
            fire.MaterialOverride = mat;
            AddChild(fire);
            _fireFx.Add(fire);
        }

        _smokeFx = new CpuParticles3D
        {
            Emitting = false,
            Amount = 32,
            Lifetime = 2.6f,
            Mesh = new SphereMesh { Radius = 2.2f, Height = 4.4f },
            Direction = new Vector3(0, 1, 0),
            Spread = 14f,
            InitialVelocityMin = 5f,
            InitialVelocityMax = 12f,
            ScaleAmountMin = 1.2f,
            ScaleAmountMax = 3f,
            Color = new Color(0.22f, 0.22f, 0.24f),
        };
        _smokeFx.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.2f, 0.2f, 0.22f, 0.55f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        AddChild(_smokeFx);
    }

    private void AddBox(Vector3 size, Vector3 pos, Material mat)
    {
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = mat, Position = pos });
    }

    private void AddLightDot(Vector3 pos, Color color)
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.5f, Height = 1f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                EmissionEnabled = true,
                Emission = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            Position = pos,
        });
    }

    private static Vector3 PartNodePosition(ShipPartState part)
    {
        var c = part.Center;
        return new Vector3((float)c.X, (float)c.Y, (float)c.Z);
    }

    /// <summary>Syncs the node from Core state (position/heading/turrets/fire/sink).</summary>
    public void UpdateFromCore(double simTime, IReadOnlyDictionary<string, GunSystem.GunVisualState> gunStates,
        IReadOnlyList<Vec3> burningParts, double delta = 0.0)
    {
        var ship = _ship;

        // Simulation → presentation: identity coordinate mapping (documented contract).
        Position = new Vector3((float)ship.WorldPosition.X, (float)ship.WorldPosition.Y, (float)ship.WorldPosition.Z);
        Rotation = new Vector3(0, (float)(ship.HeadingDeg * Math.PI / 180.0), 0);

        // Distance LOD (PHASE_03 §15): pick the first level whose switch range
        // covers the camera distance; re-evaluated a few times per second.
        if (_useRealModel && _lodNodes.Count > 0)
        {
            _lodTimer -= delta;
            if (_lodTimer <= 0)
            {
                _lodTimer = 0.25;
                float camDist = 300f;
                var cam = GetViewport().GetCamera3D();
                if (cam is not null)
                {
                    camDist = cam.GlobalPosition.DistanceTo(GlobalPosition);
                }
                int chosen = _lodNodes.Count - 1; // farthest LOD by default
                for (int i = 0; i < _lodNodes.Count; i++)
                {
                    // _lodNodes is ordered far→near; first entry with range >= dist wins.
                    if (_lodRanges[i] >= camDist)
                    {
                        chosen = i;
                        break;
                    }
                }
                for (int i = 0; i < _lodNodes.Count; i++)
                {
                    _lodNodes[i].Visible = i == chosen;
                }
            }
        }

        foreach (var gun in gunStates.Values)
        {
            if (_turrets.TryGetValue(gun.TurretGroup, out var turrets))
            {
                foreach (var turret in turrets)
                {
                    turret.Rotation = new Vector3(0, (float)(gun.TurretHeadingDeg * Math.PI / 180.0), 0);
                }
            }
        }

        // Fire / smoke: place emitters on burning parts (Core FireSystem state).
        for (int i = 0; i < _fireFx.Count; i++)
        {
            bool emit = i < burningParts.Count && ship.Alive;
            _fireFx[i].Emitting = emit;
            if (emit)
            {
                var p = burningParts[i];
                _fireFx[i].Position = new Vector3((float)p.X, (float)p.Y + 2f, (float)p.Z);
            }
        }
        bool burning = burningParts.Count > 0 && ship.Alive;
        _smokeFx!.Emitting = burning;
        if (burning)
        {
            var p = burningParts[0];
            _smokeFx.Position = new Vector3((float)p.X, (float)p.Y + 4f, (float)p.Z);
        }

        // Sink pose: presentation-only interpolation from Core KillState (PHASE_02 §31).
        if (!ship.Alive)
        {
            _sinkAge = _sinkAge < 0 ? 0f : _sinkAge;
            float since = (float)(simTime - (ship.DestroyedTime ?? simTime));
            float t = Mathf.Clamp(since / 30f, 0f, 1f);
            Position = new Vector3(Position.X, Position.Y - t * 12f, Position.Z);
            Rotation = new Vector3(Rotation.X, Rotation.Y, Mathf.Clamp(since * 0.03f, 0f, 0.9f));
            if (since > 30f)
            {
                Visible = false;
            }
        }
    }
}
