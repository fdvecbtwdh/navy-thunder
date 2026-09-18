# MDR-0012: 鱼雷

- 状态: 已决议
- 置信度: **高**（blk 实值 + 官方模型间接确认）
- 参考优先级: War Thunder（战斗计算）

## 决议

- **引信**：**纯接触引信**（Type 93 `detonationRadius: 0.1`）；**无磁性引信**（WT 有意简化，照抄）。`armDistance`（Type 93=50m）内不引爆。
- **深度**：**固定运行深度**（Type 93 `diveDepth: 1.0m`，不可调）；战列舰主装带可挡住 1m 深度鱼雷造成 non-penetration。近失（不接触的水下路径）**不**引爆。
- **水下爆炸**：独立 `hydroShock` 伤害通道（blk `pressureDamage → damageType: "hydroShock"`）；水面破口 `explosionPatchRadius: [4,12]m` → 直接驱动进水/浮力/横倾（MDR-0008）。
- **TDS（防雷凸起）**：有几何建模（x-ray 可见）；实现为几何+舱室衰减层，吸收/削减水爆伤害；**不拦截水下 AP 弹**。
- 航空鱼雷：投放超速/超高 → "溺死"（失效）；入水保险（舰雷 50m 级；空投社区实测水下 100–300m 解保）。

## WT 行为与证据

- datamine（2026-09）：`jp_610mm_type93_model_1_mod2_torpedo.blkx` 全参数（2700kg、装药 490kg tp_97、25.2m/s≈49kn、射程 20km）。
- 社区一致结论（2019 起）：全游戏接触引信+固定深度；TDS 讨论（2024-2026）。
- 官方 wiki《Mastering The Art Of Torpedo Bombing》（2025-09 更新）：投放包线与溺死。

## 历史变化

2024-2026 无鱼雷机制大改公开记录（TDS 建模随新舰逐步完善）。潜艇主游戏未实装（2024-12 官方 Q&A"有计划"；WT Mobile 更超前）。

## 未知点

hydroShock 对结构件的伤害公式；破口尺寸与装药关系（blk 只有 FX 半径）；声导/线导雷（文件中存在、无载体）。

## 实现与替换方案

- Phase 3：`TorpedoSystem`（直线航行+固定深度+arm 状态+接触检测）→ 复用 `ExplosionSystem` hydroShock 通道。
- Phase 4 扩展航空投放包线。参数来自 data/torpedoes/。
