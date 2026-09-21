using System.Collections.Generic;

namespace NavyThunder.Frontend;

/// <summary>R5: single version source for menu/report/packaging.</summary>
public static class GameVersion
{
    public const string Version = "0.9.0";
    public const string Channel = "rc";
    public static string Full => Channel.Length > 0 ? $"{Version}-{Channel}" : Version;
}

/// <summary>R3.5: zh-CN/en UI strings; falls back to the key when missing.</summary>
public static class L10n
{
    public static string Language { get; set; } = "zh-CN";

    private static readonly Dictionary<string, (string Zh, string En)> Strings = new()
    {
        ["menu.title"] = ("雷神海军", "NAVY THUNDER"),
        ["menu.subtitle"] = ("选择你的战舰", "pick your ship"),
        ["menu.start"] = ("开始战斗", "START BATTLE"),
        ["menu.settings"] = ("设置", "SETTINGS"),
        ["menu.quit"] = ("退出", "QUIT"),
        ["settings.master"] = ("主音量", "Master"),
        ["settings.effects"] = ("效果音量", "Effects"),
        ["settings.language"] = ("语言 (R3.5 文案):", "Language:"),
        ["report.victory"] = ("胜利 - ", "VICTORY - "),
        ["report.draw"] = ("战斗结束 - 平局", "BATTLE OVER - DRAW"),
        ["report.back"] = ("返回主菜单", "BACK TO MENU"),
        ["hud.guns.ready"] = ("火炮就绪", "GUNS ready"),
        ["hud.guns.reloading"] = ("火炮装填中 {0:0.0}s", "GUNS reloading {0:0.0}s"),
        ["hud.aim"] = ("准星悬停敌舰以开火", "hover an enemy to engage"),
        ["hud.helm"] = ("W/S 油门  A/D 舵  X 回中  R 弹种", "W/S throttle  A/D rudder  X center  R shell"),
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
}
