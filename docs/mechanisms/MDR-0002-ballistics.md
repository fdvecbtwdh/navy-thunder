# MDR-0002: 外弹道模型

- 状态: 已决议
- 置信度: **高**（模型形状 datamine 可证；内部阻力律细节未公开）
- 参考优先级: War Thunder（战斗计算）

## 决议

点质量弹道：初速 + 重力 + 单弹常数 Cd，数值积分（RK4、固定子步）。落速/落角由积分自然产生。Cd 随马赫数的修正作为**可选开关**（默认关），不影响基准手感。

## WT 行为与证据

- datamine（2026-09）：每弹 `mass`、`caliber`、`speed`（初速）、`Cx`（单一常数：406mm Mk8=1.02746、Mk13 HC=1.02223、127mm/38≈0.35192）；无马赫断点表。
- `ballisticsModel` 存在多套（406mm=`LAW_1943`，127mm/38=`ADVANCED_DYNAMIC_KV`），内部数学形式未公开 → 记录为信息性字段。
- 2019-01-30 官方公告：大口径 AP 的距离-落速取自真实射表（无射表的弹借用相似弹参数）。
- 实例参数与史实一致（Navweaps 交叉验证：Mk8 1225kg/762m/s；Type 93 见 MDR-0012）。

## NavalArt 参考

1.41（2026-03-31）重做炮弹弹道。设计侧仅借鉴表现；战斗侧不采用。

## 历史变化

- 2025-2026 海军散布多次调整（与弹道解耦，散布归 MDR-0014）。
- 2026-07 论坛报告水线下命中"消失无伤害"（当前已知问题，不照抄）。

## 射表校准层（2026-09-19 已落地）

用官方距离表（Iowa wiki 页 0° 穿深列反推存速）标定每弹的 `DragCoefficientScale`（乘在 datamine Cx 上）：

- **406mm Mk8（LAW_1943）**：scale=0.302 → 六个距离点 857/821/765/714/666/578 全部复现于 **±1.7%**。物理含义：LAW_1943 在跨马赫段 Cd 显著低于文件静态 Cx=1.027（隐含 Cd≈0.31，符合史实 16" 弹在 Mach 2 的阻力系数量级）。
- **127mm Mk46（ADVANCED_DYNAMIC_KV）**：scale=1.0（原始 Cx=0.35192 即吻合，1km 误差 <1%）。
- **406mm Mk13 HC**：scale=0.27，中段距离 ±3%，HE 表较噪（±12% 门限，Phase 7 细化）。

这是"射表落速校准层"的数据驱动形态：scale 存于弹定义，`QuadraticDrag` 消费；将来若提取出 Cx(M) 折线可直接替换。

## 未知点

`LAW_1943`/`ADVANCED_DYNAMIC_KV` 确切数学形式（含 Cd(M) 曲线形状）；散布公式（归 MDR-0014）。

## 实现与替换方案

- `NavyThunder.Core.Ballistics`：`QuadraticDrag`（常数 Cd × DragCoefficientScale）已落地；`IDragModel` 接口预留 `Cd(M)` 折线。
- 校准测试：`StatCardCalibrationTests` 对官方 0° 距离表全点 ±3%（406mm）/±7%（127mm）。
