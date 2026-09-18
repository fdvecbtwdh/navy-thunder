# MDR-0004: 转正 / 跳弹 / Overmatch

- 状态: 已决议（规则高置信；数值表中等）
- 参考优先级: War Thunder（战斗计算）

## 决议

- **转正（normalization）= 穿深-厚度乘数**：按 弹种 × 口径:板厚比 查表，比值在 0.5:1 → 2.5:1 平滑过渡，比值越大等效板厚越薄。各弹种独立乘数表；APDS/APFSDS 最好、APCR 最差、HEAT 无转正。
- **Overmatch**：口径:板厚 ≥ **7:1** 时厚度乘数恒为 1（完全无视角度）。
- **跳弹**：着角（自法线）~70° 起大概率跳弹；实现为 65–75° 概率带（`calibration/ricochet_angle_start_deg=65`、`ricochet_angle_full_deg=75`）。APC/APCBC 因被帽跳弹角略优。"转正可让本应跳弹的角度不跳弹"——先判转正后判跳弹。

## WT 行为与证据

- 官方 wiki《Shell Normalization Against Angled Armor》（2024-12-28，wiki.warthunder.com/661）：上述全部规则；**乘数表完整数值未公开**（只给区间与原则）。
- 社区 "14.3×" 表述（DevStrike 2025-11）= 7:1 的倒数，同一规则。

## 历史变化

- 2019 穿深改版同步重算坡效应（30° 提高穿深、60° 下降，相对旧值）。

## 未知点

乘数表完整数值（弹种 × 全比例点）；跳弹是硬阈值还是概率过渡（wiki 用词 "likely" → 按概率带处理）。

## 实现与替换方案

- Phase 1：`ArmorResolver` 先 overmatch → 转正乘数 → 跳弹 roll。
- 乘数表进 `data/calibration/`（`approximation:true`，依据 wiki 区间）；用 WT 数据卡 30°/60° 列反推拟合（Phase 1 校准任务）。
- 替换成本低：查表函数 + 数据。
