# 舰船建造系统参考（跨来源综合）

> 定位：Phase 06 建造器的设计输入汇总。NavalArt 细节见 `NAVALART_RESEARCH.md`（Level C 反编译+本机文件）；WT 内部结构事实见 `WAR_THUNDER_NAVAL_RESEARCH.md`；NT 现状依据=全仓调查（Level A）与 `docs/mechanisms/MDR-0016`。

## 1. 三方建造范式

| 维度 | War Thunder | NavalArt | Navy Thunder 现状 | NT 最终（DESIGN DECISION） |
|---|---|---|---|---|
| 范式 | 无建造（成品舰数据） | 部件实例表（prefab+位姿+armor），派生值运行时重算 | 无 | 部件/块实例表 → **确定性编译器** → ShipDefinition |
| 存档 | 服务端数据 | 明文 XML，无版本号，容错逐部件加载 | — | JSON+版本号+迁移器；容错加载；派生值不落盘 |
| 船体 | — | 8 参数可调分段+8 顶点变形+负 scale 镜像 | 无 | 模块级参数分段起步；顶点级编辑 Post-1.x |
| 装甲 | 板层+材质（内部数据） | 部件单一 armor 标量（按体积计重） | 显式 ArmorPlates | **保留显式装甲板/区**（WT 对齐）+ 块装甲计重规则借鉴 |
| 重量/平衡 | — | weight=体积×密度，density=armor×0.008 | 无 | 采纳派生计重；CG/CoB 指示器第一天做（MDR-0016 教训） |
| 射界 | 炮塔数据（内部） | 36×10° 分区可用性+局部死区+自定义射角 | 无射界字段 | 射界字段（分区+死区）进 ShipDefinition |
| 供弹链 | 炮塔-扬弹机-弹药库（x-ray 可视） | Dyk-Feeder-Turret 结构链（殉爆=可达性） | TurretGroup（Turret/Hoist/ReadyRack 联动） | 保留 TurretGroup+吸收"结构可达"殉爆语义 |
| 试航 | 无 | 无（直接进任务） | 无 | 编译后一键 headless 试航（SimRunner 场景注入） |

## 2. 编译器派生规则（Phase 06 核心设计输入）

```
输入：BuilderDesign（块/部件/装甲，局部坐标）
派生（全部确定性纯函数，可解释可显示）：
  体积 → 净浮量（块级累积，阶段曲线→吃水求逆，NA §4 方法）
  重量 → 体积×密度（密度=材质默认，装甲块密度=f(armor)，NA 计重规则）
  CoM  = 质量加权；CoB = 体积加权（NA 方法）
  校验：储备浮力>0 / CoM-CoB 力矩合理 / 动力>排水量下限 / 舵面积≥下限
输出：ShipDefinition（与史实舰同构）+ 校验报告
```

约束继承：NT 公式必须"可解释、面板可见"（NA 反面教训：幂律不可心算）；所有派生常量进 `data/calibration/` 显式标记。

## 3. 部件清单广度（Phase 06 部件面板参照）

以 NA 部件表（NAVALART_RESEARCH §5）为广度基准 × WT 模块页（W2）功能语义 × NT 现有 PartKind：
一期（Phase 06）：Hull 块 / Armor 板与块 / Compartment / Magazine(+Feeder) / Engine / Boiler / Turbine / Steering(面积) / Pump / Turret(射界/俯仰界) / Hoist / 螺旋桨(浸水率) / FuelTank / FireControl / Radar。
二期（Post-1.x）：弹射器/机库/甲板/压载/索具/旗帜/潜艇载具。

## 4. 与模拟核心的契约

- 建造器**不产生任何模拟规则**：编译产物=纯数据，战斗语义 100% 由 Core 既有系统解释（PROJECT_DESIGN §3.2-1）。
- 编译器在 `NavyThunder.Core.Builder`（非前端），保证 headless 批量编译测试可行。
- 造出的舰进战斗/AI 观战/战报统计零特殊分支（§1 NT 最终列的同构要求）。
