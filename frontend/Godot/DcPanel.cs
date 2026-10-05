using Godot;
using NavyThunder.Core.Commands;
using NavyThunder.Core.Ships;
using NavyThunder.Data;

namespace NavyThunder.Frontend;

/// <summary>
/// P05-5 damage-control panel (bottom-right): AUTO/MANUAL mode switch, the three flow
/// presets (priority order), manual flow selection, and live flow-activity dots. All
/// changes travel through DcOrderCommand via BattleRunner.Submit (P05-1) — the panel
/// reads the resulting orders back from DamageControlSystem.
/// </summary>
public sealed partial class DcPanel : PanelContainer
{
    private BattleRunner? _runner;
    private readonly Label _title = new();
    private readonly Button _autoBtn = new();
    private readonly Button _manualBtn = new();
    private readonly Button[] _presetBtns = new Button[3];
    private readonly Button[] _flowBtns = new Button[3];
    private readonly Label[] _status = new Label[3];

    private static readonly DcFlow[] Flows = [DcFlow.Repair, DcFlow.Extinguishing, DcFlow.Unwatering];
    private static readonly DcFlow[][] Presets =
    [
        [DcFlow.Repair, DcFlow.Extinguishing, DcFlow.Unwatering],     // 修理优先
        [DcFlow.Extinguishing, DcFlow.Repair, DcFlow.Unwatering],     // 灭火优先
        [DcFlow.Unwatering, DcFlow.Repair, DcFlow.Extinguishing],     // 排水优先
    ];

    public DcPanel()
    {
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.09f, 0.12f, 0.72f),
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 6, ContentMarginBottom = 8,
        });

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 4);
        AddChild(root);

        _title.AddThemeFontSizeOverride("font_size", 12);
        _title.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.7f));
        root.AddChild(_title);

        var modeRow = new HBoxContainer();
        modeRow.AddThemeConstantOverride("separation", 6);
        _autoBtn.Text = L10n.Tr("dc.auto");
        _autoBtn.Pressed += () => Submit(mode: DcMode.Automatic);
        _manualBtn.Text = L10n.Tr("dc.manual");
        _manualBtn.Pressed += () => Submit(mode: DcMode.Manual);
        modeRow.AddChild(_autoBtn);
        modeRow.AddChild(_manualBtn);
        root.AddChild(modeRow);

        var presetRow = new HBoxContainer();
        presetRow.AddThemeConstantOverride("separation", 6);
        for (int i = 0; i < Presets.Length; i++)
        {
            int preset = i;
            _presetBtns[i] = new Button { Text = L10n.Tr($"dc.preset.{i}") };
            _presetBtns[i].Pressed += () => Submit(priority: Presets[preset]);
            presetRow.AddChild(_presetBtns[i]);
        }

        root.AddChild(presetRow);

        var flowRow = new HBoxContainer();
        flowRow.AddThemeConstantOverride("separation", 6);
        for (int i = 0; i < Flows.Length; i++)
        {
            int flow = i;
            _flowBtns[i] = new Button { Text = L10n.Tr($"dc.flow.{Flows[i].ToString().ToLowerInvariant()}") };
            _flowBtns[i].Pressed += () => Submit(manualFlow: Flows[flow]);
            flowRow.AddChild(_flowBtns[i]);
        }

        root.AddChild(flowRow);

        for (int i = 0; i < Flows.Length; i++)
        {
            _status[i] = new Label();
            _status[i].AddThemeFontSizeOverride("font_size", 11);
            root.AddChild(_status[i]);
        }
    }

    public void Bind(BattleRunner runner)
    {
        _runner = runner;
        _fire = runner.Fire;
    }

    private NavyThunder.Core.Fire.FireSystem? _fire;

    private void Submit(DcMode? mode = null, IReadOnlyList<DcFlow>? priority = null, DcFlow? manualFlow = null)
    {
        _runner?.Submit(new DcOrderCommand(mode, priority, manualFlow));
    }

    /// <summary>Refreshes button states and per-flow activity lines from live Core state.</summary>
    public void UpdateFrom(Ship? player)
    {
        if (_runner is null)
        {
            Visible = false;
            return;
        }

        var dc = _runner.DamageControl;
        var (mode, priority) = dc.GetOrders(player?.TargetId ?? "");

        _title.Text = L10n.Tr("dc.title");
        Highlight(_autoBtn, mode == DcMode.Automatic);
        Highlight(_manualBtn, mode == DcMode.Manual);
        for (int i = 0; i < Presets.Length; i++)
        {
            Highlight(_presetBtns[i], priority.SequenceEqual(Presets[i]));
        }

        for (int i = 0; i < Flows.Length; i++)
        {
            DcFlow flow = Flows[i];
            bool busy = player is { Alive: true } && IsFlowBusy(player, _fire, flow);
            bool selected = mode == DcMode.Automatic
                ? priority.Contains(flow)
                : dc.ManualFlow == flow;
            // ● = there is work for this flow right now; (n) = breaches waiting for repair.
            string breaches = flow == DcFlow.Repair && player is { Alive: true }
                ? player.Parts.Values.Count(p => p.Breached && !p.Destroyed) is { } n && n > 0 ? $" ({n})" : ""
                : "";
            _status[i].Text = $"{(busy ? "●" : "○")} {L10n.Tr($"dc.flow.{flow.ToString().ToLowerInvariant()}")}{breaches}" +
                              (selected ? " ✓" : "");
        }
    }

    private static bool IsFlowBusy(Ship player, NavyThunder.Core.Fire.FireSystem? fire, DcFlow flow) => flow switch
    {
        DcFlow.Repair => player.Parts.Values.Any(p => p.Breached && !p.Destroyed),
        DcFlow.Extinguishing => fire is not null && fire.Fires.Any(f =>
            f.Active && f.HostId.StartsWith(player.TargetId + "/", StringComparison.Ordinal)),
        DcFlow.Unwatering => player.Parts.Values.Any(p => p.WaterLevel > 0.02),
        _ => false,
    };

    private static void Highlight(Button button, bool on)
    {
        button.Modulate = on ? new Color(0.55f, 1f, 0.6f) : Colors.White;
    }
}
