# BuilderDesign Schema（Phase 06）

> 建造器的中间格式（PHASE_06 §5.1）。玩家只编辑本文档；所有派生值（重量/吃水/排水量/分段/
> 部件 HP/浮量份额）由确定性编译器 `ShipCompiler` 在加载时重算，**永不落盘**（NA 教训）。
> 编译产物 `ShipDefinition` 与史实舰同构——下游（战斗/AI/战报）零特殊分支。

## 文件位置与版本

- 用户存档：`user://saves/designs/<id>.json`（Godot user 目录）
- `schemaVersion` 从 1 起；`DesignMigrator` 从 v1 就存在：更高版本拒绝并提示（前向兼容保护），
  旧版本逐级迁移（当前 v1 为恒等迁移）。

## JSON 结构（v1）

```jsonc
{
  "schemaVersion": 1,
  "meta": {
    "id": "my_gunboat",          // 全局唯一；与史实舰冲突时 RegisterCompiled 抛异常
    "name": "MY GUNBOAT",
    "author": "", "description": ""
  },
  "hullBlocks": [                 // 全宽板条（NA AdjustableHull 的盒基元）
    {
      "guid": "hull",
      "zMinM": -25, "zMaxM": 25,  // 纵向（+Z=艏, 米, Phase 01 局部约定）
      "yBottomM": -2, "yTopM": 2, // 垂向（0=水线；底可为负）
      "widthFrac": 1.0,           // 半宽比例 (0..1]；1 = 设计最宽处
      "mirrorX": true             // v1 恒对称（预留）
    }
  ],
  "armorSlabs": [                 // 显式装甲（板=盒的一面；块装甲计重见编译器）
    {
      "guid": "belt",
      "xMinM": -2, "xMaxM": 2, "yMinM": -1, "yMaxM": 1, "zMinM": -10, "zMaxM": 10,
      "face": "XMax",             // XMin|XMax|YMin|YMax|ZMin|ZMax
      "thicknessMm": 50
    }
  ],
  "parts": [                      // 部件实例（Kind = PartKind 名）
    {
      "guid": "e1", "kind": "Engine",
      "xMinM": -1, "xMaxM": 1, "yMinM": 0, "yMaxM": 2, "zMinM": -8, "zMaxM": -4,
      "turretGroup": null,        // Turret/Hoist/ReadyRack/Magazine 可挂组
      "crew": null, "hp": null,   // null = 编译器按种类/体积默认
      "open": false               // 露天件（超压可伤员）
    }
  ],
  "guns": [                       // 炮位（引用 data/shells 的弹种 id）
    {
      "guid": "g1", "turretGroup": "A",
      "shellId": "usn_127mm_mk46_special_common",
      "heShellId": "wt_127mm_he",
      "barrels": 2, "roundsPerMinute": 15, "rangeM": 12000, "traverseDegPerS": 8
    }
  ],
  "maxSpeedKnots": null,          // null = 编译器按动力部件数推导
  "turnRateDegPerS": null
}
```

## 编译器派生规则（`ShipCompiler`，全部确定性纯函数）

| 量 | 规则 | 校准标记 |
|---|---|---|
| 船体体积 | Σ块 (Δz·Δy·2·halfBeam·widthFrac·form)，form=0.8+0.2·widthFrac | `HullDensityTPerM3=0.55` |
| 重量 | 船壳=体积×HullDensity + 部件=体积×density(默认装甲 50mm) | NA 公式 `armor×0.008` 钳 [0.1,1.2] |
| 静态吃水 | 体积-高度阶梯求逆（每块体积/米高 = Δz×梁宽，自低向高累积线性插值） | — |
| 储备浮力 | (船体体积−重量)/重量；<0.15 → 警告 | `MinReserveBuoyancyFrac` |
| 分段 | 龙骨三等分 Bow/Mid/Stern（+Z=艏），部件按质心 Z 归段 | — |
| 浮量份额 | 100% 按舱室体积比例分摊（无舱室则全件均摊） | MDR-0008 |
| 部件 HP | max(10, hpPerM3×体积)，种类表见 `ShipCompiler.PartDefaults`；显式 hp 覆盖 | — |
| 舰员 | 种类表每人/件；显式 crew 覆盖；CrewTotal=max(Σcrew,8) | — |
| 航速 | null → clamp(14+动力件数×4, 12, 36) kn；兴波校核 √L×2.43 警告 | `WakeSpeedFactor` |
| 舰级 | 按长度/主炮数 → SmallCraft..Battleship | — |

## 校验规则（Error=拒绝编译，Warning=放行）

| 级别 | 代码 | 条件 |
|---|---|---|
| Error | ErrNoHull / ErrBlockDegenerate / ErrBlockWidth | 无船体块；块退化（Δz/Δy≤0）；widthFrac∉(0,1] |
| Error | ErrBuoyancy | 重量 > 船体浮力（设计浮不起来） |
| Error | ErrNoPower | 无 Engine/Boiler/Turbine |
| Error | ErrPartKind / ErrDraft | 未知部件种类；吃水求解失败 |
| Warning | WarnLowReserve / WarnNoCompartment / WarnWakeLimit / WarnGunNoTurret / WarnArmorFace | 见编译器 |

## 战斗接入（同构验证链）

`DataRepository.RegisterCompiled(ship)` 把编译舰注入数据仓库（id 冲突即抛——自建舰不得
遮蔽史实舰）；之后 `BattleRunner`/SimRunner/Godot 战斗场景全部无感知消费。试航 =
BuilderScene 保存 → `SessionState.CompiledDesign` 携带 → BattleScene3D 启动时编译注册
→ 按选舰流程进战斗。

## 测试覆盖（PHASE_06 §9）

`ShipCompilerTests`（Fast 16 项）：体积/重量恒等式、手算吃水对照、拒绝矩阵 5 例、
字节级确定性双编译、存取往返逐字节一致+派生值不落盘、端到端（编译舰进 bb_duel 战斗出战报）、
随机合法设计批。`ShipCompilerBatchAcceptance`（Integration）：**100 份随机设计全部可编译**
+ 3 份抽样实战 90s。
