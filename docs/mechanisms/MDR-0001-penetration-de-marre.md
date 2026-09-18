# MDR-0001: 舰炮穿深模型（de Marre）

- 状态: 已决议
- 置信度: **高**（公式与系数公开可复刻；绝对常数为近似）
- 参考优先级: War Thunder（战斗计算）

## 决议

采用 de Marre 体系，与 WT 相同的全局指数 + 逐弹 K 系数：

```
penetration = C · K_shell · v_impact^1.43 · m^0.71 / d^1.07
```

- `v_impact` 为弹着点存速（弹道积分输出）→ 距离衰减仅由落速产生，**无隐藏距离项**（官方 2019 公告明确大口径 AP 落速按真实射表重设）。
- 炸药占比惩罚：炸药质量/弹重比越高穿深越低（官方规则；具体函数未公开 → 校准参数 `de_marre_he_penalty`，Phase 1 定义形状）。
- `C`（绝对归一化常数）官方未公开 → `calibration/de_marre_constant`，用 WT 数据卡 0m 穿深点反解校准。

## WT 行为与证据

- 2019-01-30 官方公告《Improved Calculation of Armour Penetration》：AP/APC/APBC/APCBC 与 APCR/HVAP 使用 **Jacob de Marre 公式**；APFSDS 用 Lanz-Odermatt；动机是"让玩家可自行复现穿深"。（warthunder.com/en/news/6010）
- datamine（gszabi99/War-Thunder-Datamine，2026-09 抓取）：海军弹 blk 携带 `demarrePenetrationK`（Mk8=1.0、Mk32 SAP=0.87、Mk34 HE=0.15）与全局 `demarreSpeedPow=1.43 / demarreMassPow=0.71 / demarreCaliberPow=1.07`。海军与坦克共用同一体系，**不存在第二套保密舰炮公式**。
- 官方 wiki 计算器页：wiki.warthunder.com/jacob_de_marre。
- Stat card 0° 值为实际穿深；30°/60° 列为"该角度可击穿的板厚"（非 LoS 厚度）。

## NavalArt 参考

有 AP 穿深概念与 ModTool 穿深修正/弹重参数（1.53），但公式未公开。建造侧借鉴其"分列 HE/AP 伤害显示"的编辑器体验；战斗侧不采用。

## 历史变化

- 2019-01 公告 → 1.85/1.87 落地；此前为各弹各算的旧制。
- 2021-03 Ixwa Strike（2.5）配合 HE/超压改版微调穿深-伤害交互。

## 未知点

绝对常数 C；炸药占比惩罚函数形状；`armorClass`（如 ship_structural_steel）内部修正系数。

## 实现与替换方案

- 实现：`NavyThunder.Core.Armor.DeMarrePenetration`，K 与指数来自数据，C 来自 calibration。
- 校准：对 WT 数据卡抽样弹 0°/30°/60° 穿深点，CI 容差 ±5%（Phase 1 完成标准）。
- 替换成本低：单文件公式 + 数据驱动参数。
