using Godot;
using NavyThunder.Core.Builder;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;
using NavyThunder.Data;

namespace NavyThunder.Frontend;

/// <summary>
/// Phase 06 ship builder (PHASE_06 §5.3 MVP): grid-box editor over BuilderDesign with
/// the compiler as the live derived-values source. Panel picks the part kind, click
/// places it on the waterline grid, G/R/X/B mirror Phase-05 muscle memory (grab/rotate/
/// delete/buy… kept minimal: LMB place, RMB delete, R rotates, Ctrl+Z/Y undo/redo,
/// Ctrl+S save). CG/CoB markers + draft preview recompile on every edit (compiler is
/// deterministic and fast). 试航 = compile, register, jump into the battle scene.
/// All editing state lives in BuilderDesign (pure data) — the scene tree is a view.
/// </summary>
public partial class BuilderScene : Node3D
{
    private BuilderDesign _design = new()
    {
        Meta = new DesignMeta { Id = "my_design", Name = "MY DESIGN" },
    };

    private string _selectedKind = "Compartment";
    private readonly Stack<BuilderDesign> _undo = [];
    private readonly Stack<BuilderDesign> _undoRedoPool = [];
    private Camera3D _camera = null!;
    private MeshInstance3D? _ghost;
    private Label _stats = null!;
    private Label _issues = null!;
    private PanelContainer? _designsPanel;
    private CompileResult _lastCompile = new() { Ok = false, Issues = [] };
    private DataRepository? _repo;
    private double _gridZ = 0; // placement cursor along the keel

    private static readonly string[] Palette =
    [
        "Compartment", "Engine", "Magazine", "Turret", "Funnel", "Steering",
    ];

    public override void _Ready()
    {
        AppEnv.Install();
        var env = new WorldEnvironment { Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            AmbientLightEnergy = 0.7f,
            Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
        } };
        AddChild(env);
        var sun = new DirectionalLight3D { RotationDegrees = new Vector3(-50, 30, 0) };
        AddChild(sun);

        // Sea plane + 10 m grid reference (visual only).
        var sea = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(2000, 2000) } };
        sea.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.07f, 0.23f, 0.34f), Roughness = 0.2f,
        };
        sea.Position = new Vector3(0, 0.02f, 0);
        AddChild(sea);

        _camera = new Camera3D { Position = new Vector3(45, 40, 45), Current = true };
        AddChild(_camera);
        _camera.LookAt(new Vector3(0, 0, 0));

        BuildHud();
        RebuildDesignView();
        Recompile();

        // Verification hook (same env var contract as the menu/battle scenes).
        if (OS.GetEnvironment("NT_FRONTEND_SHOT") is { } shot && shot.Length > 0)
        {
            var tree = GetTree();
            tree.CreateTimer(2.0).Timeout += () =>
            {
                var img = ((Viewport)tree.Root).GetTexture().GetImage();
                img.SavePng(shot);
                GD.Print("builder shot saved: " + shot);
            };
        }
    }

    private void BuildHud()
    {
        var hud = new CanvasLayer { Layer = 10 };
        AddChild(hud);

        var palette = new HBoxContainer { Position = new Vector2(12, 12) };
        foreach (var kind in Palette)
        {
            var b = new Button { Text = L10n.Tr($"builder.{kind}") };
            string kind1 = kind;
            b.Pressed += () => _selectedKind = kind1;
            palette.AddChild(b);
        }

        hud.AddChild(palette);

        _stats = new Label { Position = new Vector2(12, 54) };
        _stats.AddThemeFontSizeOverride("font_size", 14);
        hud.AddChild(_stats);

        _issues = new Label { Position = new Vector2(12, 210) };
        _issues.AddThemeFontSizeOverride("font_size", 12);
        _issues.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.4f));
        hud.AddChild(_issues);

        var actions = new HBoxContainer
        {
            AnchorLeft = 0, AnchorTop = 1, AnchorRight = 1, AnchorBottom = 1,
            OffsetTop = -44, OffsetBottom = -12, OffsetLeft = 12,
        };
        hud.AddChild(actions);
        AddAction(actions, L10n.Tr("builder.undo"), Undo);
        AddAction(actions, L10n.Tr("builder.redo"), Redo);
        AddAction(actions, L10n.Tr("builder.save"), SaveDesign);
        AddAction(actions, L10n.Tr("builder.load"), () => _designsPanel!.Visible = !_designsPanel!.Visible);
        AddAction(actions, L10n.Tr("builder.seatrial"), SeaTrial);
        AddAction(actions, L10n.Tr("builder.menu"), () => GetTree().ChangeSceneToFile("res://Menu.tscn"));

        _designsPanel = new PanelContainer
        {
            Visible = false,
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
        };
        var list = new VBoxContainer();
        _designsPanel.AddChild(list);
        hud.AddChild(_designsPanel);
    }

    private static void AddAction(HBoxContainer row, string label, System.Action action)
    {
        var b = new Button { Text = label };
        b.Pressed += action;
        row.AddChild(b);
    }

    // ------------------------------------------------------------ editing ops

    private void PushUndo()
    {
        _undo.Push(_design);
        _undoRedoPool.Clear();
        _design = _design with
        {
            Meta = _design.Meta,
            HullBlocks = [.. _design.HullBlocks],
            ArmorSlabs = [.. _design.ArmorSlabs],
            Parts = [.. _design.Parts],
            Guns = [.. _design.Guns],
        };
    }

    private void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _undoRedoPool.Push(_design);
        _design = _undo.Pop();
        RebuildDesignView();
        Recompile();
    }

    private void Redo()
    {
        if (_undoRedoPool.Count == 0)
        {
            return;
        }

        _undo.Push(_design);
        _design = _undoRedoPool.Pop();
        RebuildDesignView();
        Recompile();
    }

    private Vec3 CursorWorldPoint()
    {
        var mouse = GetViewport().GetMousePosition();
        var origin = _camera.ProjectRayOrigin(mouse);
        var normal = _camera.ProjectRayNormal(mouse);
        if (normal.Y >= -1e-3)
        {
            return new Vec3(0, 0, (float)_gridZ);
        }

        var hit = origin + normal * (-origin.Y / normal.Y);
        _gridZ = System.Math.Round(hit.Z / 2.0) * 2.0; // 2 m keel grid
        return new Vec3(hit.X, hit.Y, hit.Z);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton m && m.Pressed)
        {
            var p = CursorWorldPoint();
            if (m.ButtonIndex == MouseButton.Left)
            {
                PlacePart(p);
            }
            else if (m.ButtonIndex == MouseButton.Right)
            {
                RemovePartAt(p);
            }
        }
        else if (e is InputEventKey k && k.Pressed && !k.Echo)
        {
            if (k.Keycode == Key.R)
            {
                // Rotate = swap the ghost's X/Z extents (90° step on the next placement).
                _rotated = !_rotated;
            }
            else if (k.CtrlPressed && k.Keycode == Key.Z)
            {
                Undo();
            }
            else if (k.CtrlPressed && (k.Keycode == Key.Y || (k.ShiftPressed && k.Keycode == Key.Z)))
            {
                Redo();
            }
            else if (k.CtrlPressed && k.Keycode == Key.S)
            {
                SaveDesign();
            }
        }
    }

    private bool _rotated;

    private void PlacePart(Vec3 at)
    {
        if (!PartDefaultsBox(_selectedKind, out double hx, out _))
        {
            return;
        }

        double zLen = _rotated ? hx : 2;
        double xLen = _rotated ? 2 : hx;
        PushUndo();
        _design.Parts.Add(new PartPlacement
        {
            Guid = Guid.NewGuid().ToString("N")[..8],
            Kind = _selectedKind,
            XMinM = System.Math.Round(at.X - xLen, 1), XMaxM = System.Math.Round(at.X + xLen, 1),
            YMinM = 0, YMaxM = 2,
            ZMinM = System.Math.Round(at.Z - zLen, 1), ZMaxM = System.Math.Round(at.Z + zLen, 1),
            TurretGroup = _selectedKind == "Turret" ? "A" : null,
        });
        RebuildDesignView();
        Recompile();
    }

    private static bool PartDefaultsBox(string kind, out double halfX, out double height)
    {
        // MVP placement sizes (metres); parameter editing lands with the schema doc round.
        (halfX, height) = kind switch
        {
            "Compartment" => (1.5, 2.0),
            "Engine" => (2.0, 2.0),
            "Magazine" => (1.5, 2.0),
            "Turret" => (2.0, 2.0),
            "Funnel" => (1.0, 3.0),
            "Steering" => (1.0, 1.5),
            _ => (1.5, 2.0),
        };
        return true;
    }

    private void RemovePartAt(Vec3 at)
    {
        int best = -1;
        double bestD = double.MaxValue;
        for (int i = 0; i < _design.Parts.Count; i++)
        {
            var p = _design.Parts[i];
            if (at.X >= p.XMinM - 0.5 && at.X <= p.XMaxM + 0.5 &&
                at.Z >= p.ZMinM - 0.5 && at.Z <= p.ZMaxM + 0.5)
            {
                double d = System.Math.Abs((p.ZMinM + p.ZMaxM) / 2 - at.Z);
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
        }

        if (best < 0)
        {
            return;
        }

        PushUndo();
        var parts = new List<PartPlacement>(_design.Parts);
        parts.RemoveAt(best);
        _design = _design with { Parts = parts };
        RebuildDesignView();
        Recompile();
    }

    // ------------------------------------------------------------ design → scene view

    private readonly List<Node3D> _blockViews = [];

    private void EnsureHullBlocks()
    {
        if (_design.HullBlocks.Count == 0)
        {
            _design.HullBlocks.Add(new HullBlock
            {
                Guid = "hull", ZMinM = -25, ZMaxM = 25, YBottomM = -2, YTopM = 2, WidthFrac = 1,
            });
        }
    }

    private void RebuildDesignView()
    {
        EnsureHullBlocks();
        foreach (var v in _blockViews)
        {
            v.QueueFree();
        }

        _blockViews.Clear();

        // Hull blocks: translucent boxes.
        foreach (var b in _design.HullBlocks)
        {
            double halfBeam = MaxHalfBeam();
            var mesh = new MeshInstance3D
            {
                Mesh = new BoxMesh
                {
                    Size = new Vector3((float)(2 * halfBeam * b.WidthFrac),
                        (float)(b.YTopM - b.YBottomM), (float)(b.ZMaxM - b.ZMinM)),
                },
                Position = new Vector3(0, (float)((b.YTopM + b.YBottomM) / 2), (float)((b.ZMaxM + b.ZMinM) / 2)),
            };
            mesh.MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.7f, 0.75f, 0.8f, 0.45f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            };
            AddChild(mesh);
            _blockViews.Add(mesh);
        }

        // Parts: colored boxes by kind.
        foreach (var p in _design.Parts)
        {
            var mesh = new MeshInstance3D
            {
                Mesh = new BoxMesh
                {
                    Size = new Vector3((float)(p.XMaxM - p.XMinM), (float)(p.YMaxM - p.YMinM),
                        (float)(p.ZMaxM - p.ZMinM)),
                },
                Position = new Vector3((float)((p.XMinM + p.XMaxM) / 2), (float)((p.YMinM + p.YMaxM) / 2),
                    (float)((p.ZMinM + p.ZMaxM) / 2)),
            };
            mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = KindColor(p.Kind) };
            AddChild(mesh);
            _blockViews.Add(mesh);
        }
    }

    private double MaxHalfBeam() =>
        _design.HullBlocks.Count == 0 ? 4 : _design.HullBlocks.Max(b => b.WidthFrac) * DesignLength() * 0.12;

    private double DesignLength() =>
        _design.HullBlocks.Count == 0 ? 50 : _design.HullBlocks.Max(b => b.ZMaxM) - _design.HullBlocks.Min(b => b.ZMinM);

    private static Color KindColor(string kind) => kind switch
    {
        "Compartment" => new Color(0.55f, 0.75f, 0.55f),
        "Engine" => new Color(0.9f, 0.6f, 0.2f),
        "Magazine" => new Color(0.9f, 0.3f, 0.3f),
        "Turret" => new Color(0.4f, 0.6f, 0.9f),
        "Funnel" => new Color(0.5f, 0.5f, 0.5f),
        "Steering" => new Color(0.7f, 0.5f, 0.9f),
        _ => new Color(0.8f, 0.8f, 0.8f),
    };

    // ------------------------------------------------------------ compile feedback

    private void Recompile()
    {
        _lastCompile = ShipCompiler.Compile(_design);
        var d = _lastCompile.Derived;
        _stats.Text = _lastCompile.Ok
            ? $"{L10n.Tr("builder.weight")} {d!.WeightT:0} t   {L10n.Tr("builder.draft")} {d.StaticDraftM:0.00} m   " +
              $"{L10n.Tr("builder.reserve")} {d.ReserveBuoyancyFrac:0.00}   {L10n.Tr("builder.length")} {d.LengthM:0} m"
            : L10n.Tr("builder.invalid");

        // Draft waterline preview: translucent blue plane at the solved waterline.
        if (_waterline is null)
        {
            _waterline = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3((float)(MaxHalfBeam() * 2 + 4), 0.05f, (float)(DesignLength() + 4)) },
            };
            _waterline.MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.3f, 0.6f, 1f, 0.35f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            };
            AddChild(_waterline);
            _blockViews.Add(_waterline);
        }

        _waterline.Visible = _lastCompile.Ok;
        if (_lastCompile.Ok)
        {
            _waterline.Position = new Vector3(0, (float)d!.StaticDraftM, 0);
        }

        var errors = _lastCompile.Issues.Where(i => i.Severity == "Error").ToList();
        _issues.Text = string.Join("\n", errors.Take(4).Select(i => i.Message));

        // CG/CoB markers.
        UpdateMarker(ref _comMarker, d?.CenterOfMass, new Color(1f, 0.3f, 0.2f));
        UpdateMarker(ref _cobMarker, d?.CenterOfBuoyancy, new Color(0.3f, 0.7f, 1f));
    }

    private MeshInstance3D? _waterline;
    private MeshInstance3D? _comMarker;
    private MeshInstance3D? _cobMarker;

    private void UpdateMarker(ref MeshInstance3D? marker, (double X, double Y, double Z)? at, Color color)
    {
        if (at is null)
        {
            if (marker is not null)
            {
                marker.Visible = false;
            }

            return;
        }

        marker ??= new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.8f, Height = 1.6f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color, EmissionEnabled = true, Emission = color * 1.5f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        if (marker.GetParent() is null)
        {
            AddChild(marker);
        }

        marker.Visible = true;
        marker.Position = new Vector3((float)at.Value.X, (float)at.Value.Y, (float)at.Value.Z);
    }

    // ------------------------------------------------------------ persistence + sea trial

    private string DesignsDir =>
        Path.Combine(AppEnv.UserDir, "saves", "designs");

    private void SaveDesign()
    {
        var meta = _design.Meta with { Id = string.IsNullOrWhiteSpace(_design.Meta.Id) ? "my_design" : _design.Meta.Id };
        _design = _design with { Meta = meta };
        _design.Save(Path.Combine(DesignsDir, $"{meta.Id}.json"));
        _issues.AddThemeColorOverride("font_color", new Color(0.5f, 1f, 0.5f));
        _issues.Text = L10n.Tr("builder.saved") + $" saves/designs/{_design.Meta.Id}.json";
        GD.Print($"P06 design saved: {_design.Meta.Id}");
    }

    private void LoadDesign(string path)
    {
        try
        {
            _design = BuilderDesign.Load(path);
            _undo.Clear();
            _undoRedoPool.Clear();
            RebuildDesignView();
            Recompile();
        }
        catch (Exception ex)
        {
            _issues.Text = $"load failed: {ex.Message}";
        }
    }

    public override void _Process(double delta)
    {
        // Refresh the saved-designs list while the panel is open (cheap, ≤ a few dozen files).
        if (_designsPanel is { Visible: true } panel && panel.GetChildCount() > 0)
        {
            var list = (VBoxContainer)panel.GetChild(0);
            foreach (var child in list.GetChildren())
            {
                list.RemoveChild(child);
                child.QueueFree();
            }

            try
            {
                foreach (var f in Directory.EnumerateFiles(DesignsDir, "*.json"))
                {
                    string path = f;
                    var b = new Button { Text = Path.GetFileNameWithoutExtension(f) };
                    b.Pressed += () =>
                    {
                        LoadDesign(path);
                        panel.Visible = false;
                    };
                    list.AddChild(b);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private void SeaTrial()
    {
        var result = ShipCompiler.Compile(_design);
        if (!result.Ok || result.Ship is null)
        {
            _issues.Text = string.Join("\n", result.Issues
                .Where(i => i.Severity == "Error").Select(i => i.Message));
            return;
        }

        // Save, then hand off to the battle scene with the compiled ship selected. The
        // battle scene registers compiled designs through SessionState (same data path).
        SaveDesign();
        SessionState.CompiledDesign = _design;
        SessionState.SelectedShipId = result.Ship.Id;
        GetTree().ChangeSceneToFile("res://Main.tscn");
    }
}
