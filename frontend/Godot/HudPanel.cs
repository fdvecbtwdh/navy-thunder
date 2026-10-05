using Godot;
using NavyThunder.Core.Battle;
using NavyThunder.Core.Ships;
using NavyThunder.Data;

namespace NavyThunder.Frontend;

/// <summary>
/// P05-3/4/5/6 HUD layer: graphical damage panel (top-left), helm gauge + reload ring
/// (bottom-left), target card (top-right), damage-control panel (bottom-right), five-state
/// hit feedback beside the crosshair, hint line and the tutorial card. Everything is
/// state-driven from Core reads pushed in per frame; commands travel through
/// BattleRunner.Submit. The battle-report overlay also lives here.
/// </summary>
public partial class HudPanel : CanvasLayer
{
    private BattleRunner? _runner;
    private Ship? _player;
    private bool _playerHe;
    private double _lastSimTime;

    private DamagePanel _damagePanel = null!;
    private HelmGauge _gauge = null!;
    private ReloadRing _reloadRing = null!;
    private TargetCard _targetCard = null!;
    private DcPanel _dcPanel = null!;
    private HitIndicators _hitIndicators = null!;
    private Label _tutorial = null!;
    private Label _hint = null!;

    public override void _Ready()
    {
        Layer = 10;

        _damagePanel = new DamagePanel { Position = new Vector2(12, 12) };
        AddChild(_damagePanel);

        var bottomLeft = new HBoxContainer
        {
            AnchorLeft = 0, AnchorTop = 1, AnchorRight = 0, AnchorBottom = 1,
            OffsetLeft = 12, OffsetTop = -160, OffsetRight = 320, OffsetBottom = -12,
        };
        bottomLeft.AddThemeConstantOverride("separation", 10);
        _reloadRing = new ReloadRing();
        _gauge = new HelmGauge();
        bottomLeft.AddChild(_reloadRing);
        bottomLeft.AddChild(_gauge);
        AddChild(bottomLeft);

        _targetCard = new TargetCard
        {
            AnchorLeft = 1, AnchorTop = 0, AnchorRight = 1, AnchorBottom = 0,
            OffsetLeft = -260, OffsetTop = 12, OffsetRight = -12, OffsetBottom = 140,
        };
        AddChild(_targetCard);

        _dcPanel = new DcPanel
        {
            AnchorLeft = 1, AnchorTop = 1, AnchorRight = 1, AnchorBottom = 1,
            OffsetLeft = -250, OffsetTop = -230, OffsetRight = -12, OffsetBottom = -12,
        };
        AddChild(_dcPanel);

        _hitIndicators = new HitIndicators
        {
            AnchorLeft = 0, AnchorTop = 0, AnchorRight = 1, AnchorBottom = 1,
        };
        _hitIndicators.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_hitIndicators);

        _hint = new Label
        {
            AnchorLeft = 0, AnchorTop = 1, AnchorRight = 1, AnchorBottom = 1,
            OffsetTop = -26, OffsetBottom = -6,
            Text = L10n.Tr("hud.hint"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(1, 1, 1, 0.55f),
        };
        _hint.AddThemeFontSizeOverride("font_size", 12);
        AddChild(_hint);

        _tutorial = new Label
        {
            AnchorLeft = 0, AnchorTop = 1, AnchorRight = 1, AnchorBottom = 1,
            OffsetTop = -110, OffsetBottom = -40,
            Text = $"{L10n.Tr("tutorial.move")}\n{L10n.Tr("tutorial.aim")}\n{L10n.Tr("tutorial.dc")}",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(1, 1, 1, 1),
        };
        _tutorial.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_tutorial);
    }

    public void Bind(BattleRunner runner, Ship? player)
    {
        (_runner, _player) = (runner, player);
        _damagePanel.Bind(runner.Fire);
        _dcPanel.Bind(runner);
    }

    public void SetShellSelection(bool he) => _playerHe = he;

    public void SetAimMode(bool manual)
    {
        _aimModeManual = manual;
        _reloadRing.UpdateFrom(1, _playerHe, manual);
    }

    public void PushImpact(NavyThunder.Core.Ballistics.ProjectileArmorImpact impact) => _hitIndicators.PushImpact(impact);

    public void PushSplash() => _hitIndicators.PushSplash();

    public void UpdateHud(double simTime, double reloadRemaining)
    {
        _lastSimTime = simTime;
        _gauge.UpdateFrom(_player);
        _damagePanel.UpdateFrom(_player);
        _dcPanel.UpdateFrom(_player);
        _targetCard.UpdateFrom(_aimTarget, _player?.WorldPosition ?? default);

        // Reload ring: worst remaining reload over the main battery as a fraction of the
        // first gun's full cycle (presentation approximation — per-gun rings land later).
        double cycleS = 12;
        var firstGun = _player?.Definition.Guns.FirstOrDefault();
        if (firstGun is not null)
        {
            cycleS = 60.0 / System.Math.Max(0.1, firstGun.RoundsPerMinute);
        }

        double fraction = cycleS > 0 ? 1.0 - System.Math.Clamp(reloadRemaining / cycleS, 0, 1) : 1;
        _reloadRing.UpdateFrom(fraction, _playerHe, _aimModeManual);

        // Tutorial fades out after the opening seconds (R3.4 behaviour).
        float fade = System.Math.Clamp((20f - (float)simTime) / 4f, 0f, 1f);
        _tutorial.Modulate = new Color(1, 1, 1, fade);
    }

    private bool _aimModeManual;

    private Ship? _aimTarget;

    /// <summary>The BattleScene pushes the current engagement target for the card.</summary>
    public void SetAimTarget(Ship? target) => _aimTarget = target;

    /// <summary>Battle report overlay (R2.6 behaviour, P05-10 localized). The gameplay
    /// HUD hides while the report is up — it used to occlude the survival list.</summary>
    public void ShowReport(BattleRunner runner, string shotPath)
    {
        _damagePanel.Visible = false;
        _dcPanel.Visible = false;
        _gauge.Visible = false;
        _reloadRing.Visible = false;
        _hitIndicators.Visible = false;
        _hint.Visible = false;
        _tutorial.Visible = false;
        _targetCard.Visible = false;

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
            Text = string.Format(L10n.Tr("report.stats"), runner.World.Time, lost, runner.Ships.Count,
                       runner.Battle.GunsFired) + "\n" +
                   string.Join("\n", runner.Ships.Select(s => s.Lost
                       ? $"{L10n.ShipLine(s)}: {L10n.Tr("report.lost")} · {L10n.KillReason(s.KillReason ?? "")}"
                       : $"{L10n.ShipLine(s)}: {L10n.Tr("report.alive")} {s.CrewAlive}/{s.Definition.CrewTotal}")),
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
