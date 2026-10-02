using Godot;
using NavyThunder.Core.Battle;
using NavyThunder.Core.Ships;
using NavyThunder.Data;

namespace NavyThunder.Frontend;

/// <summary>
/// HUD layer (PHASE_02 §25): the existing text HUD + tutorial card + battle-report
/// overlay, carried over from the Phase R2 2D view. Lives on its own CanvasLayer and
/// never touches 3D coordinates. Final graphical HUD is Phase 05.
/// </summary>
public partial class HudPanel : CanvasLayer
{
    private Label _hud = null!;
    private Label _tutorial = null!;
    private BattleRunner? _runner;
    private Ship? _player;
    private bool _playerHe;

    public override void _Ready()
    {
        Layer = 10;
        _hud = new Label { Position = new Vector2(16, 10), Text = "" };
        _hud.AddThemeFontSizeOverride("font_size", 15);
        AddChild(_hud);

        _tutorial = new Label
        {
            Position = new Vector2(200, 640),
            Text = $"{L10n.Tr("tutorial.move")}\n{L10n.Tr("tutorial.aim")}\n{L10n.Tr("tutorial.dc")}",
            Modulate = new Color(1, 1, 1, 1),
        };
        _tutorial.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_tutorial);
    }

    public void Bind(BattleRunner runner, Ship? player) => (_runner, _player) = (runner, player);

    public void SetShellSelection(bool he) => _playerHe = he;

    public void UpdateHud(double simTime, double reloadRemaining)
    {
        if (_player is null || _runner is null)
        {
            return;
        }

        var ship = _player;
        string helm = ship.Alive
            ? $"{ship.TargetId}  SPD {ship.SpeedKnots:0.0} kn  HDG {ship.HeadingDeg:0}  " +
              $"THR {ship.ThrottleCommand:0.00}  RUD {ship.RudderCommand:+0.00;-0.00;0.00}"
            : $"{ship.TargetId} DESTROYED";
        string shellType = _playerHe ? "HE" : "AP";
        string guns = ship.Alive
            ? (reloadRemaining > 0
                ? string.Format(L10n.Tr("hud.guns.reloading"), reloadRemaining)
                : L10n.Tr("hud.guns.ready")) +
              $"  SHELL {shellType}  |  {L10n.Tr("hud.aim")}"
            : "";

        var lines = new List<string> { helm, guns };
        foreach (var section in ship.Sections)
        {
            double frac = System.Math.Clamp(section.Hp / section.Definition.Hp, 0.0, 1.0);
            bool fire = _runner!.Fire.Fires.Any(f => f.Active && ship.Parts.Values.Any(pt =>
                pt.Definition.SectionId == section.Definition.Id &&
                f.HostId == $"{ship.TargetId}/{pt.Definition.Id}"));
            bool flood = ship.Parts.Values.Any(pt =>
                pt.Definition.SectionId == section.Definition.Id && pt.WaterLevel > 0.05);
            string marker = (fire ? " [FIRE]" : "") + (flood ? " [FLOOD]" : "");
            lines.Add($"{section.Definition.Id,-10} {frac,4:P0}  " +
                      new string('#', (int)System.Math.Round(frac * 20)).PadRight(20) + marker +
                      (section.Destroyed ? "  DESTROYED" : ""));
        }

        lines.Add($"CREW {ship.CrewAlive}/{ship.Definition.CrewTotal}  |  {L10n.Tr("hud.helm")}  |  t={simTime:0}s");
        _hud.Text = string.Join("\n", lines);

        // Tutorial fades out after the opening seconds (R3.4 behaviour, ported).
        float fade = System.Math.Clamp((18f - (float)simTime) / 4f, 0f, 1f);
        _tutorial.Modulate = new Color(1, 1, 1, fade);
    }

    /// <summary>Battle report overlay (R2.6 behaviour ported verbatim).</summary>
    public void ShowReport(BattleRunner runner, string shotPath)
    {
        var report = new CanvasLayer { Layer = 20 };
        AddChild(report);

        var box = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f,
            AnchorTop = 0.5f, AnchorBottom = 0.5f,
        };
        box.AddThemeConstantOverride("separation", 14);
        report.AddChild(box);

        var winner = runner.Battle.WinnerTeamId ?? "none";
        var title = new Label
        {
            Text = runner.Battle.Result == BattleResult.TeamWin
                ? $"{L10n.Tr("report.victory")}{winner.ToUpperInvariant()}"
                : L10n.Tr("report.draw"),
        };
        title.AddThemeFontSizeOverride("font_size", 36);
        box.AddChild(title);

        int lost = runner.Ships.Count(s => s.Lost);
        var detail = new Label
        {
            Text = $"duration {runner.World.Time:0}s  |  ships lost {lost}/{runner.Ships.Count}  |  " +
                   $"salvos {runner.Battle.GunsFired}\n" +
                   string.Join("\n", runner.Ships.Select(s =>
                       $"{s.TargetId}: {(s.Lost ? $"LOST ({s.KillReason})" : $"alive, crew {s.CrewAlive}/{s.Definition.CrewTotal}")}")),
        };
        detail.AddThemeColorOverride("font_color", Colors.LightGray);
        box.AddChild(detail);

        var again = new Button { Text = L10n.Tr("report.back") };
        again.Pressed += () => GetTree().ChangeSceneToFile("res://Menu.tscn");
        box.AddChild(again);

        box.Ready += () => box.Position = -box.Size / 2f;
        if (shotPath.Length > 0)
        {
            var tree = GetTree();
            var path = shotPath;
            tree.CreateTimer(0.5).Timeout += () =>
            {
                var img = ((Viewport)tree.Root).GetTexture().GetImage();
                img.SavePng(path);
            };
        }
        AppEnv.Info($"R2.6 full-chain PASS: report shown winner={winner} shipsLost={lost} salvos={runner.Battle.GunsFired}");
        if (OS.GetEnvironment("NT_FRONTEND_AUTO") == "1")
        {
            GetTree().CreateTimer(2.0).Timeout += () => GetTree().Quit();
        }
    }
}
