# MDR-0003: 引信系统（含 VT 近炸）

- 状态: 已决议
- 置信度: **高**（三参数与 VT 参数 datamine 实值）
- 参考优先级: War Thunder（战斗计算）

## 决议

引信 = 三参数状态机：

1. **灵敏度 `explodeThresholdMm`**：命中等效厚度低于该值的板不起爆（可过穿为哑弹）。HE=0.1mm、SAP=6mm、大口径 APC=38mm（WT 实值）。
2. **延迟 `fuseDelayS`**：触发后按存速继续飞行该时长再爆（HE=0.001、SAP=0.01、大口径 APC=0.035）。→ 哑弹机制：高动能+长延迟+低灵敏 → 整船穿出不炸（战列舰互穿现象，必须复刻）。
3. **跳弹不引爆**（`fuseOnRicochet: false`）；水面触发（`fuseOnWater: true`）。

海军炮弹**无出膛解保距离**（blk 无 arming 字段，出膛即可爆）。VT 弹例外：`proximityFuse { radius, armDistance, air-only }`——基准 127mm Mk31：radius 23m、armDistance 457m，只对空中目标检测，空爆后按标准破片锥结算。

## WT 行为与证据

- datamine（2026-09）blk 原文：上述全部字段与数值（navalmodels_weapons/*.blkx）。
- 社区实测：460mm 弹 fuseDelay 0.025-0.03s 可整穿驱逐舰不炸（论坛 2024-12）。
- 官方 wiki：VT "Proximity fuse requires distance before activating against air targets"。

## 历史变化

无引信大改记录（2024-03 Alpha Strike 改的是破片分配）。已知问题：hpThresholdForFuse 语义未公开（暂不实现）。

## 未知点

引信对多层板/结构板的取值方式；惯性引信（日系 91/13 号）是否独立实现；VT 多目标选择逻辑。

## 实现与替换方案

- Phase 1：`FuzeState { Armed, AwaitingImpact, DelayCountdown, Detonated, Dud }`，参数来自 shell 定义。
- Phase 5：VT 检测走空中目标查询（`AirTargetsOnly`）。
