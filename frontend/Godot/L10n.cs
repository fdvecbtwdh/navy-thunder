using System.Collections.Generic;
using NavyThunder.Core.Model;
using NavyThunder.Core.Ships;

namespace NavyThunder.Frontend;

/// <summary>R5: single version source for menu/report/packaging.</summary>
public static class GameVersion
{
    public const string Version = "1.0.0";
    public const string Channel = "";
    public static string Full => Channel.Length > 0 ? $"{Version}-{Channel}" : Version;
}

/// <summary>R3.5: zh-CN/en UI strings; falls back to the key when missing.</summary>
public static class L10n
{
    public static string Language { get; set; } = "zh-CN";

    private static readonly Dictionary<string, (string Zh, string En)> Strings = new()
    {
        // menu / settings
        ["menu.title"] = ("雷神海军", "NAVY THUNDER"),
        ["menu.subtitle"] = ("选择你的战舰", "pick your ship"),
        ["menu.start"] = ("开始战斗", "START BATTLE"),
        ["menu.settings"] = ("设置", "SETTINGS"),
        ["menu.quit"] = ("退出", "QUIT"),
        ["settings.master"] = ("主音量", "Master"),
        ["settings.effects"] = ("效果音量", "Effects"),
        ["settings.ambient"] = ("环境音量", "Ambient"),
        ["settings.language"] = ("语言:", "Language:"),
        ["settings.keys"] = ("键位 (点击后按下新键):", "Keys (click, then press a key):"),
        ["key.press"] = ("按任意键…", "press a key…"),
        ["key.throttle_up"] = ("油门加大", "Throttle up"),
        ["key.throttle_down"] = ("油门减小", "Throttle down"),
        ["key.rudder_port"] = ("左舵", "Rudder port"),
        ["key.rudder_stbd"] = ("右舵", "Rudder starboard"),
        ["key.rudder_center"] = ("舵回中", "Rudder center"),
        ["key.shell_toggle"] = ("切换弹种", "Shell toggle"),
        ["key.aim_mode"] = ("瞄准模式", "Aim mode"),
        ["key.lock"] = ("目标锁定", "Target lock"),
        ["key.map"] = ("战术地图", "Tactical map"),
        ["key.pause"] = ("暂停", "Pause"),

        // battle report
        ["report.victory"] = ("胜利 - ", "VICTORY - "),
        ["report.draw"] = ("战斗结束 - 平局", "BATTLE OVER - DRAW"),
        ["report.back"] = ("返回主菜单", "BACK TO MENU"),
        ["report.stats"] = ("时长 {0:0}s  |  损失 {1}/{2}  |  齐射 {3}",
            "duration {0:0}s  |  ships lost {1}/{2}  |  salvos {3}"),
        ["report.lost"] = ("沉没", "LOST"),
        ["report.alive"] = ("存活, 舰员", "alive, crew"),

        // HUD (P05-3)
        ["hud.hint"] = ("W/S 油门  A/D 舵  X 回中  R 弹种  T 锁定  G 瞄准模式  滚轮 调距  M 地图  ESC 暂停",
            "W/S throttle  A/D rudder  X center  R shell  T lock  G aim mode  wheel range  M map  ESC pause"),
        ["hud.destroyed"] = ("已沉没", "DESTROYED"),
        ["hud.crew"] = ("舰员", "CREW"),
        ["hud.aim.auto"] = ("自动火控", "AUTO FC"),
        ["hud.aim.manual"] = ("手动瞄准", "MANUAL"),
        ["hud.target.range"] = ("距离", "RANGE"),
        ["hud.target.speed"] = ("航速", "SPEED"),
        ["hud.target.heading"] = ("航向", "HDG"),

        // damage control panel (P05-5)
        ["dc.title"] = ("损管", "DAMAGE CONTROL"),
        ["dc.auto"] = ("自动", "AUTO"),
        ["dc.manual"] = ("手动", "MANUAL"),
        ["dc.preset.0"] = ("修理优先", "REPAIR 1st"),
        ["dc.preset.1"] = ("灭火优先", "FIRE 1st"),
        ["dc.preset.2"] = ("排水优先", "PUMP 1st"),
        ["dc.flow.repair"] = ("修理破口", "REPAIR"),
        ["dc.flow.extinguishing"] = ("灭火", "EXTINGUISH"),
        ["dc.flow.unwatering"] = ("排水", "UNWATER"),

        // ship builder (P06)
        ["menu.builder"] = ("舰船建造器", "SHIP BUILDER"),
        ["builder.Compartment"] = ("舱室", "COMPARTMENT"),
        ["builder.Engine"] = ("主机", "ENGINE"),
        ["builder.Magazine"] = ("弹药库", "MAGAZINE"),
        ["builder.Turret"] = ("炮塔", "TURRET"),
        ["builder.Funnel"] = ("烟囱", "FUNNEL"),
        ["builder.Steering"] = ("舵机", "STEERING"),
        ["builder.weight"] = ("重量", "WEIGHT"),
        ["builder.draft"] = ("吃水", "DRAFT"),
        ["builder.reserve"] = ("储备浮力", "RESERVE"),
        ["builder.length"] = ("舰长", "LENGTH"),
        ["builder.invalid"] = ("设计无效（见错误）", "DESIGN INVALID (see errors)"),
        ["builder.undo"] = ("撤销", "UNDO"),
        ["builder.redo"] = ("重做", "REDO"),
        ["builder.save"] = ("保存", "SAVE"),
        ["builder.load"] = ("载入", "LOAD"),
        ["builder.seatrial"] = ("试航", "SEA TRIAL"),
        ["builder.saved"] = ("已保存：", "SAVED: "),
        ["builder.menu"] = ("回主菜单", "MAIN MENU"),

        // pause menu (P05-8)
        ["pause.title"] = ("暂停", "PAUSED"),
        ["pause.resume"] = ("继续战斗", "RESUME"),
        ["pause.restart"] = ("重新开始", "RESTART"),
        ["pause.menu"] = ("回主菜单", "MAIN MENU"),
        ["pause.quit"] = ("退出游戏", "QUIT"),

        // tutorial (P05-5 copy fix: damage control is automatic with a real panel)
        ["tutorial.move"] = ("W/S 油门 · A/D 转舵 · X 回正舵", "W/S throttle · A/D rudder · X center"),
        ["tutorial.aim"] = ("自动档：准星悬停敌舰开火 · T 锁定 · G 切手动(滚轮调距) · R 切换 AP/HE",
            "Auto: hover an enemy to engage · T lock · G manual (wheel = range) · R toggles AP/HE"),
        ["tutorial.dc"] = ("损管自动进行 · 右下面板调整模式与优先级", "Damage control runs automatically · tune mode & priority bottom-right"),
    };

    public static string Tr(string key, params object[] args)
    {
        if (!Strings.TryGetValue(key, out var pair))
        {
            return key;
        }

        var text = Language == "en" ? pair.En : pair.Zh;
        return args.Length > 0 ? string.Format(text, args) : text;
    }

    /// <summary>P05-4: module kind names for the x-ray list.</summary>
    public static string ModuleName(PartKind kind) => kind switch
    {
        PartKind.Compartment => Language == "en" ? "Compartment" : "舱室",
        PartKind.Magazine => Language == "en" ? "Magazine" : "弹药库",
        PartKind.FuelTank => Language == "en" ? "Fuel tank" : "油箱",
        PartKind.Engine => Language == "en" ? "Engine" : "主机",
        PartKind.Boiler => Language == "en" ? "Boiler" : "锅炉",
        PartKind.Turbine => Language == "en" ? "Turbine" : "轮机",
        PartKind.Steering => Language == "en" ? "Steering" : "舵机",
        PartKind.FireControl => Language == "en" ? "Fire control" : "火控",
        PartKind.Radar => Language == "en" ? "Radar" : "雷达",
        PartKind.Pump => Language == "en" ? "Pump" : "水泵",
        PartKind.Turret => Language == "en" ? "Turret" : "炮塔",
        PartKind.Hoist => Language == "en" ? "Hoist" : "扬弹机",
        PartKind.TorpedoTube => Language == "en" ? "Torpedo tube" : "鱼雷管",
        PartKind.ReadyRack => Language == "en" ? "Ready rack" : "待发弹架",
        PartKind.Funnel => Language == "en" ? "Funnel" : "烟囱",
        PartKind.AntiTorpedo => Language == "en" ? "TDS" : "防雷凸舱",
        _ => kind.ToString(),
    };

    /// <summary>P05-4: hull-section captions from the role (section ids are per-ship
    /// data strings like "uss_fletcher_bow" — too long for the 52 px block).</summary>
    public static string SectionName(HullSectionRole role) => role switch
    {
        HullSectionRole.Bow => Language == "en" ? "BOW" : "艏部",
        HullSectionRole.Stern => Language == "en" ? "STERN" : "艉部",
        _ => Language == "en" ? "MID" : "中部",
    };

    /// <summary>P05 visual pass: localized, player-facing sink reasons.</summary>
    public static string KillReason(string reason) => reason switch
    {
        "magazine_detonation" => Language == "en" ? "magazine detonation" : "弹药库殉爆",
        "unsinkability_lost" => Language == "en" ? "unsinkability lost" : "丧失不沉性",
        "buoyancy_lost" => Language == "en" ? "buoyancy lost" : "储备浮力耗尽",
        "capsize" => Language == "en" ? "capsized" : "倾覆",
        "crew_annihilated" => Language == "en" ? "crew annihilated" : "舰员伤亡殆尽",
        "below_survival_crew" => Language == "en" ? "survival crew lost" : "损管舰员不足",
        _ => reason,
    };

    /// <summary>"ship:uss_fletcher#usn-0" → ("uss_fletcher", "usn-0").</summary>
    public static (string DefinitionId, string? Instance) SplitTargetId(string targetId)
    {
        string s = targetId.StartsWith("ship:", StringComparison.Ordinal) ? targetId[5..] : targetId;
        int hash = s.IndexOf('#');
        return hash < 0 ? (s, null) : (s[..hash], s[(hash + 1)..]);
    }

    /// <summary>Player-facing ship line for the report: display name + spawn instance.</summary>
    public static string ShipLine(Ship ship)
    {
        var (_, instance) = SplitTargetId(ship.TargetId);
        return instance is null ? ship.Definition.DisplayName : $"{ship.Definition.DisplayName} ({instance})";
    }

    /// <summary>Compact map label: definition id + instance key (no "ship:" noise).</summary>
    public static string ShipMapLabel(Ship ship)
    {
        var (defId, instance) = SplitTargetId(ship.TargetId);
        return instance is null ? defId : $"{defId} {instance}";
    }
}
