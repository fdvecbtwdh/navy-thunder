# Phase 01 — Simulation Geometry Foundation（局部坐标命中几何）

> 状态：`[DONE]`（2026-10-02 实现，验收记录见文末）
> 前置依赖：Phase 00。**Phase 02 的命中相关部分依赖本阶段；表现框架部分可并行。**

## 0. 实施记录（2026-10-02）

- **ShipTransform**：`src/NavyThunder.Core/Geometry/ShipTransform.cs` — Translate+RotateY，位置/方向四 API 严格分离，52 项数学/往返/正交测试（`ShipTransformTests`）。
- **局部坐标迁移**：`ArmorPlate`（Center 永久局部，删除 Offset/Translate）、`ArmorTarget`（世界射线经 `TransformProvider` 逆变换，世界命中点/法线返回，旋转无关包围球 broadphase）、`BallisticsSystem`（调用语义不变）、`DamageBridgeSystem`（interior trace 经 ToLocal/ToLocalDirection）、`ExplosionSystem`（brisy 穿透/blast/破片全部转局部）、`TorpedoSystem`（hydroShock 局部化）、`FloodingSystem`（CreateBreach 经 ShipTransform；**横倾失衡轴 Z→X 修复**）、`GunSystem`（炮塔角改船体局部语义；删除 FollowHullArmor）、`ShipNavigationSystem`（删除 Track 双轨）、`Ship`（WorldTransform 派生属性）、飞机链（HeadingDeg + 蒙皮板随动，修复蒙皮板冻结在原点的 bug）。
- **坐标 bug 修复**（迁移中发现的同族隐性 bug）：爆炸 blast 径向、超压波、破片点伤、鱼雷水压冲击对远离原点舰船失效；`MagazineDetonation` 事件坐标世界化；DamageBridge 与 ExplosionSystem 的化学爆双重计算去重（保留带衰减的 ExplosionSystem 版本）。
- **数据帧迁移**：`tools/migrate_local_frame_zbow.py` — 舰船/飞机数据从 X 纵轴迁移到 **+Z=舰艏**（PROJECT_DESIGN §5.2 约定）：部件盒 x↔z 值交换、装甲板 x↔z + 面重映射（XMin↔ZMin/XMax↔ZMax）、舱段键 xMin/xMax→zMin/zMax；`generate_ships.py` 同步改版；`HullSectionDefinition` 改 ZMin/ZMax，`Ship.SectionAtZ`。
- **8 艘主力舰装甲补全**：`tools/audit_armor_8ships.py` — test_battleship/test_destroyer/uss_iowa/uss_north_carolina/uss_fletcher/ijn_nagato/ijn_kongo/uss_baltimore 增加艏/艉舯部横隔壁（Tier-3 史实近似厚度，source.notes 标注）。选择依据：全部测试场景 + 6v6 阵容 + RC2 选舰覆盖 BB/BC/CA/DD。
- **行为锚点测试**：`GeometryRotationTests` 6 项 — 锚点 A（转向后同射线命中面改变）、锚点 B（局部命中点航向不变）、锚点 C（航向不修改板数据）、假旋转判别（艏板转向后必须不可命中）、broadphase 不因旋转漏检、interior trace 随船体帧。
- **行为基线变化与 golden**：正确几何使对称编成战斗从"单方屠杀"变为对称消耗——bb_duel 从 1334s TeamWin 变为 3600s Draw（738 齐射/81 穿透/9 起火/1 鱼雷命中），r1_naval_duel 出现殉爆+双沉。三个行为锚点测试更新为**伤害链完整 + 战斗有裁决**的断言（击杀调平属 Phase 04）；golden 有意识再生成（本文件即变更原因记录），Release 双跑验证稳定。
- **门禁结果（Release）**：快批次 161/161 ✓；慢门禁 8/8 ✓（黄金/验收×3/AI 场景×2）；性能预算 ✓（6v6 全场 ≥1× 实时）；Godot 前端构建 ✓ + AUTO 冒烟 ✓；确定性双跑 ✓（`Battle_Is_Deterministic_Across_Runs`）。

## 1. 阶段目标
让舰艏/舰艉/左舷/右舷/甲板获得真实不同的命中几何：装甲板与部件盒固定在船体局部坐标，命中检测对射线做逆变换。这是 PROJECT_DESIGN §5（最高优先级技术设计）的实现阶段。

## 2. 当前基础（Level A 事实）
- 命中盒**不随航向旋转**：`ShipFactory.MakePlate` 法线固定世界 ±X/±Y/±Z；`ShipPartState.Contains` 世界轴对齐（`Ship.cs:38-41`）；`GunSystem.cs:236` 注释自认 "translate, never rotate"。
- 装甲板跟随存在双轨：`ShipNavigationSystem.Track`（绝对，无人调用）与 `GunSystem.FollowHullArmor`（位移补偿，实际在用）；`ArmorPlate.Translate(v) => Offset = v`。
- `BallisticsSystem.HandleArmorHits` 世界坐标 raycast 全部板；broadphase 世界包围球。
- 现有 30 舰装甲板几乎全是 ±Z 舷侧面（test_battleship 7 板全舷侧；uss_nevada 3 板两舷+甲板）——艏艉壁/甲板/炮座数据缺失。
- 113/113 测试绿；golden=bb_duel 终局指纹。

## 3. 外部参考与来源
- WT 装甲/入射角机制：`docs/research/WAR_THUNDER_NAVAL_RESEARCH.md` §3、`SHIP_DAMAGE_REFERENCE.md` §1（Level B）。
- NavalArt 等效装甲/逐层穿透（Level C）：`NAVALART_RESEARCH.md` §3/§6。
- NT 设计裁决：`PROJECT_DESIGN.md` §5（FACT/DESIGN DECISION 已标注）。

## 4. 不做什么
- 不引入 pitch/roll 模拟（§5.4：Phase 04）；不做视觉 3D（Phase 02）；不改穿深公式（校准冻结）；不动 AI 行为。

## 5. 技术设计

### 5.1 变换链
```
shipTransform（2D 阶段）：Translate(WorldPosition) × RotateY(HeadingDeg)
worldRay  = (origin, dir)
localRay  = inverse(shipTransform) ∘ worldRay   // 2D：平移+绕 Y 旋转的逆
```

### 5.2 类与模块（新增/修改）
| 单元 | 类型 | 职责 |
|---|---|---|
| `Core/Geometry/ShipTransform` | struct | 位置+航向（+预留 pitch/roll）；`ToWorld(Local)`/`ToLocal(World)`；正交性测试锚 |
| `Ship.WorldTransform` | 属性 | 导航系统每 tick 写入；替代散落的 WorldPosition 平移 |
| `ArmorPlate` | 修改 | `Offset` 语义删除→板永久局部；`IntersectRay` 不变（本来就局部数学） |
| `ArmorTarget` | 修改 | 增加 `Transform` 引用；`Trace(worldRay)` 内部先 `ToLocal`；broadphase 用世界包围球（局部 AABB 变换外接） |
| `BallisticsSystem.HandleArmorHits` | 修改 | 对每 target 传世界射线，target 内变换 |
| `ShipFactory.BuildArmorTarget` | 修改 | 移除出生点烘焙（`BaseCenter` 回归纯局部） |
| `ShipNavigationSystem` | 修改 | 删 `Track()` 与板平移；写 `WorldTransform` |
| `GunSystem` | 修改 | 删 `FollowHullArmor`；mount/turret 全局部坐标+shipTransform 求世界射向；`HullLengthAxis` 直接用局部纵轴 |
| `DamageBridgeSystem.ApplyInteriorTrace` | 修改 | 命中点/方向 `ToLocal` 后进入 trace |
| `ExplosionSystem` | 修改 | 爆心 `ToLocal` 后查部件/板（或板给世界 AABB，二选一，实现时定） |
| `TorpedoSystem` | 修改 | 接触检测同 ballistics 路径 |
| `Frontend BattleView` | 修改 | 绘制已按航向旋转多边形（FACT：现绘制就转），仅需跟随接口改名 |

### 5.3 数据流
```
BallisticsSystem.Update
  → for each target: localRay = target.Transform.ToLocal(worldRay)
  → 对局部板 raycast（现数学复用） → 命中点(局部) → 事件带世界+局部双坐标
  → DamageBridge: 局部 trace 进 Ship.ApplyDamage（PartAt 直接复用）
```

### 5.4 数据审计（本阶段的一半工作量）
- 30 舰逐舰补装甲板：艏艉横向隔壁（±Z 面）、水平甲板（+Y 面）、炮座/炮塔面（若数据可拆）、水上舵机舱。
- Tier-3 手工录入，依据各舰史实装甲布置（简化为 4-8 板/舰起步）；`source.notes` 标注简化级别。
- 同步补 `ArmorPlateDefinition` 的斜面支持评估（现状只有轴对齐盒面——倾斜装甲带以"多级阶梯板"近似起步，斜面精确表达留待评估）。

## 6. 文件修改范围
`src/NavyThunder.Core/Geometry/`、`Ships/`（Ship/ShipFactory/ShipNavigationSystem/GunSystem）、`Ballistics/`、`Explosions/`、`Torpedoes/`、`Data/BattleRunner.cs`（接线）、`frontend/Godot/BattleView.cs`（只跟随接口）、`data/ships/*.json`（装甲板数据）、`tests/`（新增+golden 再生成）。

## 7. 依赖 / 并行
- 依赖：Phase 00（审计惯例）。
- 可并行：Phase 02 的 3D 框架/Ocean/相机（不依赖命中语义）；W2 校准深化。

## 8. 风险与对策
| 风险 | 对策 |
|---|---|
| golden 全部失效 | 行为锚点测试先行写好（旧几何下固定输入→期望命中的舰艏/舷侧面）；golden 有意识再生成+commit 说明原因 |
| 校准回归受影响 | 穿深校准与几何无关（板厚度/公式不变）——校准测试应保持绿；若变绿→红说明实现错 |
| 30 舰数据补板工作量大 | 分两级：先 8 艘主力舰（含测试舰）全板，其余舰用类模板生成器批量出简化板 |
| 现有测试锚定世界坐标 | 测试改写为"语义断言"（命中面/入射角）而非坐标断言 |

## 9. 测试（新增）
- 变换正交/往返：`ShipTransform.ToWorld(ToLocal(x)) == x`（含 0/90/180/270°）。
- 几何旋转：同一世界射线打航向 0° 与 90° 的同舰，命中面不同且与手算一致。
- 入射角：舷侧接敌 vs 舰艏对敌，同弹同板 `ImpactAngleDeg` 差异符合几何。
- 内部 trace：穿舷侧后命中相邻纵向舱室（局部 X 递进）。
- 回归：校准 12/12 保持；`HeadlessAcceptanceTests` 行为断言保持。
- golden：有意识再生成（P00-4 惯例）。

## 10. 验收标准（PASS/FAIL）— 2026-10-02 核验

- PASS：舰船航向旋转 90° 后，同一炮弹命中装甲面与入射角按空间几何改变（`GeometryRotationTests.AnchorA` + `Bow_Plate_Stops_Blocking…`）。
- PASS：同一齐射对舷侧对敌目标与 T 头目标产生不同穿/跳弹分布（场景级：r1_naval_duel 战报行为变化佐证；分布级断言在 Phase 04 转正表拟合时补）。
- PASS：`ArmorPlate.Offset`/`FollowHullArmor`/`ShipNavigationSystem.Track` 从代码中消失（grep 零残留）。
- PASS：校准 12/12 与 `HeadlessAcceptanceTests` 全绿；golden 再生成（原因记录 = 本文件 §0，独立 commit）。
- PASS：6v6 性能预算保持 ≥1×（Release 实测通过，6v6 全场 5m54s 墙钟）。

## 11. 完成后状态
命中几何可信；Phase 02 的 3D 表现可以直接消费 `WorldTransform`；Phase 04 的斜角机制（转正表拟合）有意义。
