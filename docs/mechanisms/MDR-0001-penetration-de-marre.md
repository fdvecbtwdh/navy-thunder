# MDR-0001: 舰炮穿深模型（de Marre）

- 状态: 已决议（官方公式已完整还原）
- 置信度: **高**（官方计算器 JS 源码 + datamine 逐弹系数 + 官方距离表三点互证）
- 参考优先级: War Thunder（战斗计算）

## 决议

采用**官方 calculator 的精确实现**（wiki.warthunder.com/jacob_de_marre 页面内嵌 JS，2026-09-19 抓取源码逐字还原）：

```
pen_mm = 100 · v^1.43 · m^0.71 / (1900^1.43 · (d_mm/100)^1.07) · K_shell · knap(炸药%)
```

- 绝对常数 = 参考速度 **1900 m/s**（APCR 家族为 3000）。此前"绝对常数未知待校准"的占位方案被此发现取代。
- 幂指数 1.43/0.71/1.07：官方 JS = datamine 逐弹字段 = 多个社区实现，四处一致。
- **knap（炸药占比惩罚）全弹种适用**：官方分段函数
  - 炸药% < 0.65 → 1.0；< 1.6 → 线性降到 0.93；< 2.0 → 0.90；< 3.0 → 0.85；< 4.0 → 0.75（线性）；≥ 4 → 0.75
- 验证锚点：
  - 75mm M61 数据卡 10m/0° = 104mm，公式（含 knap）= 103.95 ✓（0.05mm 误差）
  - Mk8 炮口 882mm（官方计算器口径）；127mm Mk46 计算值 170 ≈ 社区口径
  - **406mm Mk8 官方距离表 0° 列（1000/2500/5000/7500/10000/15000m = 857/821/765/714/666/578）全部在 ±1.7% 内复现**（配合 MDR-0002 的阻力缩放）

## WT 行为与证据

- 官方公式页（内嵌 JS）：wiki.warthunder.com/jacob_de_marre（"All calculations are given for a distance of 0 meters and at a right angle"）
- datamine：每弹 `demarrePenetrationK`（Mk8=1.0、Mk32 SAP=0.87、Mk46=1.0、Mk13 HC=0.18、Mk34=0.15）+ 全局幂指数字段
- 官方距离表（Iowa wiki 页，Gaijin 生成）：0°/30°/60° 三列 × 1000–15000m 六档
- 社区互证：JareelSkaj/wt-wiki-tools（精确移植）、wt_datamine_extractor/demarre.rs（同一参考点）

## 历史与冲突记录

- 2019-01-30 官方公告引入该体系（动机：玩家可复现穿深）。
- 冲突一（已裁决）：早期调研读数 Mk13 K=0.15 vs 0.18 —— 以直接读取 blk 的 0.18 为准（与 460mm Type 0 HE 的 0.18 跨弹一致）。
- 冲突二（已裁决）：调研中"海军 K 已含炸药折扣（不加 knap）"的候选被距离表全面拟合否定——knap 全弹适用 + 阻力缩放才能同时满足 127mm（两者都严格吻合）与 406mm。
- 角度未解项：30°/60° 列无解析公式（游戏内 slope/ricochet preset 行为），只能查表/边界校验（MDR-0004）。

## 未知点

30°/60° 转正乘数表完整数值（官方只给机制与 0.5–2.5 比例窗）；`armorClass` 内部修正。

## 实现与替换方案

- `NavyThunder.Core.Armor.DeMarre`：官方公式 + knap；K 与炸药数据来自弹定义。
- 校准测试：`StatCardCalibrationTests`（0° 距离表 ±3%，角度列上界）。替换成本低：单文件公式。
