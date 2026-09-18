# 机制决策记录（MDR — Mechanism Decision Records）

本目录是 Navy Thunder 全部机制决策的可审计档案。每条机制按统一格式记录：

```
状态 | 置信度(高/中/低) | 参考优先级(War Thunder / NavalArt)
决议(Chosen behavior)
War Thunder 行为与证据(官方 wiki/changelog/dev blog/datamine，带 URL 与日期)
NavalArt 行为(参考，建造侧优先)
历史变化(机制被修改/重做的记录)
未知点(Unknowns — 官方未公开的部分)
实现与替换方案(参数化位置与替换成本)
```

规则（与项目总纲一致）：

1. **战斗计算以 War Thunder 当前版本为准；舰船建造/设计/部件/沙盒以 NavalArt 为准。**
2. 冲突时按领域裁决；两者皆无定论时联网多源调查；仍无法确认则参数化近似并标记。
3. **禁止**把未公开公式伪装成"就是 War Thunder 的公式"——一律进 `data/calibration/`，标 `approximation:true`。
4. 来源冲突时：查发布时间、判断版本演变，以当前版本为准，不选"看起来合理"的那个。

## 索引与全局机制对照表

| MDR | 机制 | 决议摘要 | 置信度 | 近似参数 |
|---|---|---|---|---|
| [MDR-0001](MDR-0001-penetration-de-marre.md) | 穿深 | de Marre：`K·v^1.43·m^0.71/d^1.07`，逐弹 K + 全局指数（datamine） | 高 | 绝对常数（对数据卡校准） |
| [MDR-0002](MDR-0002-ballistics.md) | 弹道 | 点质量+重力+单弹常数 Cd；大口径落速按射表 | 高 | Cd 高马赫段修正（可选） |
| [MDR-0003](MDR-0003-fuzes.md) | 引信/VT | 灵敏度(mm)+延迟(s)+跳弹不引爆；过穿哑弹；VT=armDistance+radius 仅对空 | 高 | — |
| [MDR-0004](MDR-0004-normalization-ricochet.md) | 转正/跳弹/overmatch | 厚度乘数表(弹种×口径:板厚 0.5–2.5)；≥7:1 无视角度；~70° 跳弹带 | 规则高/数值中 | 乘数表、跳弹概率带 |
| [MDR-0005](MDR-0005-explosion-overpressure.md) | 爆炸/破片/超压 | 冲击波+brisant+30–45°破片锥；超压仅 HE 系(APHE≥170g)，对舰只作用露天炮位 | 结构高/数值中 | 破片数/初速公式 |
| [MDR-0006](MDR-0006-crew-compartments.md) | 舰员/舱室 | 舱室 HP→线性扣员(125HP/人基准)；不可回填；损管/自沉双阈值；0% 即毁 | 高 | 逐舰舱室数值 |
| [MDR-0007](MDR-0007-hull-sections.md) | 船体分区 | 分段 HP；小艇任一段毁即沉；大舰中段≥2 段毁→丧失不沉性 | 高 | 逐舰段 HP |
| [MDR-0008](MDR-0008-flooding-buoyancy.md) | 进水/浮力 | 仅水下破口进水；泵排水；浮力损失≥100% 沉没；横倾→倾覆；物理沉没 | 高 | 流量公式、倾覆角 |
| [MDR-0009](MDR-0009-damage-control.md) | 损管 | 双轨：Spearhead 自动三流程+优先级+DC 系数(默认)；手动三键(开关) | 框架高/数值中 | DC 系数公式 |
| [MDR-0010](MDR-0010-fire.md) | 火灾 | 独立掷骰起火(与舱室 HP 无关)；炮塔→电梯→弹药库蔓延；进水淹灭 | 规则高/数值低 | 起火概率表、燃烧 DPS |
| [MDR-0011](MDR-0011-ammo-magazine.md) | 弹药/殉爆 | 一级弹药容量+30–40s 补给(开火暂停)；弹药库毁→按剩余弹量殉爆 | 高 | 逐舰容量、殉爆概率 |
| [MDR-0012](MDR-0012-torpedoes.md) | 鱼雷 | 接触引信+固定深度+armDistance；hydroShock 通道；TDS=衰减层不拦水下 AP | 高 | 破口尺寸公式 |
| [MDR-0013](MDR-0013-aircraft-damage.md) | 飞机伤害 | 逐段翼梁+Critical G；缆线/液压/自封油箱/飞行员；损伤写回飞行模型；击杀判定分层 | 高 | 断翼 G 阈值系数 |
| [MDR-0014](MDR-0014-radar-fire-control.md) | 雷达/火控/防空 | FCS 提前量+散布；雷达毁→散布惩罚；AI 炮组；VT 弹幕 | 中高 | 散布公式、AI 参数 |
| [MDR-0015](MDR-0015-missiles-guidance.md) | 导弹/现代层 | 独立制导层：IR/SARH/ARH+IOG/数据链；chaff/flare/IRCCM；RWR | 中高 | 逐弹导引参数 |
| [MDR-0016](MDR-0016-navalart-builder.md) | NavalArt 建造/物理 | 设计侧基准：可调模块+顶点编辑、块状 mm 装甲(重量按体积)、体积浮力+CG/CoB、物理沉没 | 高 | — |

## 主要证据源（2026-09-18 抓取）

- **官方 wiki**：[HE 与超压机制](https://wiki.warthunder.com/mechanics/4236-the-mechanics-of-high-explosive-effect-and-overpressure)（2025-07 更新）、[舰员机制](https://wiki.warthunder.com/640-ship-crew-mechanics)（2024-12 更新）、[舰船模块](https://wiki.warthunder.com/mechanics/5245-ship-modules)（2025-11 更新）、[转正机制](https://wiki.warthunder.com/661-shell-normalization-against-angled-armor-in-war-thunder)（2024-12）、[de Marre 计算器](https://wiki.warthunder.com/jacob_de_marre)、[击杀规则 destroy_rules](https://wiki.warthunder.com/mechanics/destroy_rules)
- **官方公告**：[2019 穿深计算改进（de Marre/Lanz-Odermatt）](https://warthunder.com/en/news/6010-development-improved-calculation-of-armour-penetration-in-the-game-en)、[2025-11 损管重做 dev blog](https://warthunder.com/en/news/9795-development-the-new-damage-control-mechanic-for-naval)、[2.45 Hornet's Sting changelog](https://forum.warthunder.com/t/war-thunder-hornets-sting-changelog/221524)、[2.47 Leviathans changelog](https://warthunder.com/en/game/changelog/current/1749)、[2.51 Spearhead changelog](https://forum.warthunder.com/t/war-thunder-spearhead-changelog/283027)
- **公开 datamine**：[gszabi99/War-Thunder-Datamine](https://github.com/gszabi99/War-Thunder-Datamine)（de Marre 系数、引信三参、VT/鱼雷 blk 参数）
- **NavalArt**：[Steam 商店页](https://store.steampowered.com/app/842780/NavalArt/)、[官方公告页](https://steamcommunity.com/app/842780/announcements)（1.0/1.1/1.52/1.53/1.6 全文）、[SteamDB](https://steamdb.info/app/842780/)
