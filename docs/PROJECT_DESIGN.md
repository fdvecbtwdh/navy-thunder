# Navy Thunder 总体设计基准（PROJECT_DESIGN）

> **文档地位**：Navy Thunder 的最高层产品与技术设计基准。后续所有阶段文档（`docs/phases/`）、机制决议（`docs/mechanisms/`）与开发工作以本文档为锚。
> **成立前提**：本文档基于 2026-10-02 全仓调查（113/113 测试、SimRunner 双跑确定性验证、Godot 前端实测截图）与外部资料研究（`docs/research/`）。
> **书写规则**：关键论断标注 `FACT`（代码/可靠资料直接证明）、`INFERENCE`（多事实推导）、`DESIGN DECISION`（Navy Thunder 自己的选择）；引用外部资料标注来源等级（A=本项目代码实测 / B=官方资料 / C=本地提取数据 / D=可靠社区 / E=论坛视频 / F=推测）。资料索引见 `docs/research/SOURCE_INDEX.md`。

---

## 1. 项目定位

### 1.1 一句话定位

**FACT（继承自 ROADMAP §0 与 MDR 全局规则，经本轮复核仍然成立）**：Navy Thunder 是一款单机海战遭遇战游戏（PvE 为主）：玩家操舰作战 + AI 友军/敌军，附带舰船建造系统与全 AI 观战模式；战斗机制以 War Thunder 海战为基准，舰船建造/设计/物理以 NavalArt 为基准；二战为主，首发美/日/英/德四阵营。

### 1.2 Navy Thunder 自己的设计转化（不照搬任何一方）

`DESIGN DECISION` — Navy Thunder 的差异化定位是三方经验的融合体，每一条都是显式选择：

| 维度 | War Thunder 给什么 | NavalArt 给什么 | Navy Thunder 的选择 |
|---|---|---|---|
| 战斗数值 | 官方公式（de Marre、引信三参、超压边界）与 datamine 数值 | 不采用其战斗数值 | 战斗计算层=WT 机制形状 + 校准层近似（MDR 全局规则③） |
| 舰船构造表达 | 固定成品舰（玩家不拆解内部结构） | 玩家逐块建造、参数可调 | **两轨**：内置史实舰队（WT 数据驱动，成品）；建造器（NavalArt 式，产出同构 ShipDefinition） |
| 物理 | 简化浮态（横倾/吃水为结算值） | 体积排水浮力、物理沉没 | 浮力求解器走 NavalArt 理念（MDR-0008 已裁决），战斗判定叠加 WT 规则 |
| 确定性 | 服务端权威、客户端表现 | 未公开 | **全模拟确定性**（NT 独有价值，lockstep 就绪），WT/NA 均不具备 |
| 规模 | 12v12+ 在线 | 单舰对决/小规模 | 3v3–6v6 遭遇战（单机性能与 AI 复杂度平衡，FACT 继承） |

### 1.3 玩家体验目标

`DESIGN DECISION`：
1. **开箱即玩**：主菜单 → 选舰（或用建造器造舰）→ 一场 15–30 分钟的遭遇战 → 战报。全程无文档依赖。
2. **读得懂的战斗**：打穿/跳弹/过穿/起火/进水/殉爆都有明确视觉与文字反馈；损伤面板按船体分段展示（对齐 WT 2.45 UI，`docs/research/WAR_THUNDER_NAVAL_RESEARCH.md` §4）。
3. **造得出自己的舰队**：建造器产出的舰船与史实舰走**同一条** Simulation Core 数据通路——造出来的船能进战斗、能进 AI 对战、能被战报统计。
4. **AI 是对手不是稻草人**：AI 与玩家走同一命令接口、同一命中几何（FACT：现有 `SimpleNavalAISystem` 已遵守此原则，予以保留并扩展）。

### 1.4 明确不做（Post-1.x）

多人同步（lockstep 就绪但独立大工程）、潜艇、战役/科技树、创意工坊分发（建造器本地分享先行）。

---

## 2. 最终产品结构（含实测状态标签）

状态图例：`[DONE]` 实测可用 · `[PARTIAL]` 有实现未达验收 · `[PROTOTYPE]` 占位/接线不全 · `[PLANNED]` 未开始 · `[BLOCKED]` 有前置依赖未解决 · `[DEPRECATED]` 将被替代。

| # | 功能 | 状态 | 说明（FACT 依据） |
|---|---|---|---|
| 1 | 主菜单（标题/版本/开始/退出） | `[DONE]` | `frontend/Godot/MenuView.cs`；选舰+设置已通 |
| 2 | 选舰（从舰队数据） | `[DONE]` | 30 舰下拉框，`SessionState.SelectedShipId` → `BattleRunner(playerShipOverride)` |
| 3 | 设置（音量/语言/键位/持久化） | `[DONE]` | Master/Effects/Ambient 音量+zh/en+键位重绑定（最小集）已通，settings.json 持久化（P05-9）；图像质量分级 Phase 08 |
| 4 | 战斗场景 | `[PARTIAL]` | **3D 骨架已立 + 真实舰船模型已接入**（Phase 02 骨架 + Phase 03 资产管线：23 舰 WT 模型 4 级 LOD、程序化双轨回退、nodeMap 挂点，见 PHASE_03）；贴图/击毁态归 WS 资产 VA-2/VA-3（2026-10-05 修订去向） |
| 5 | 玩家操舰（舵/油门） | `[PARTIAL]` | WASD 已通（R2.2；**P02 修复 AI 抢舵 bug**）；无油门档位 UI、无倒车 |
| 6 | 瞄准与开火 | `[DONE]` | 两档火控（P05-2）：自动=锁定+试射修正（W7 AB 带，3 齐喷收敛）+提前量指示；手动=光标落点+滚轮调距；G 切换、T 锁定 |
| 7 | 弹种切换 | `[DONE]` | R 键 AP/HE（R3） |
| 8 | 损伤 HUD（分段血条/FIRE/FLOOD 标记） | `[PARTIAL]` | 文本面板已通（R2.4 简化版）；无图形化分段图、无模块 x-ray |
| 9 | **玩家损管操作** | `[DONE]` | 损管面板（P05-5）：自动/手动模式、三优先级预设、手动单流程；经 DcOrderCommand 提交；教程卡文案已与实现一致 |
| 10 | 战报页 | `[DONE]` | 胜负/时长/损失/逐舰存活，返回菜单（R2.6） |
| 11 | 全 AI 观战 | `[PARTIAL]` | headless `SimRunner` 完整可跑 `[DONE]`；前端"观战模式"入口 `[PLANNED]` |
| 12 | 战术地图 | `[DONE]` | 旧 2D 战场图降级为只读战术地图（P02，M 键切换），数据同源 Core |
| 13 | 音效 | `[PARTIAL]` | 合成音效 6 种已生成并**已接入战斗事件**（P02：GunFired/ShellDetonation/MagazineDetonation/ShipDestroyed→播放）；音色为程序合成占位，真实音源归 WS 资产 VA-7（2026-10-05 修订去向；播放机制可用、资产缺失，详见 §2.5） |
| 14 | 小地图 | `[DONE]` | 战术地图兼任（P05-7 增强：玩家环+锁定标记+标签防重叠） |
| 15 | 舰船建造器 | `[DONE-MVP]` | BuilderDesign v1+确定性编译器（Core.Builder）+BuilderScene（放置/撤销/CG-CoB/试航）；schema 见 `docs/BUILDER_DESIGN_SCHEMA.md`；参数编辑/镜像/装甲 UI 列 MVP+ |
| 16 | 舰船管理（收藏/导入/导出） | `[PLANNED]` | 无 |
| 17 | 存档/用户数据目录 | `[PARTIAL]` | `AppEnv` user://（logs/crash/saves/settings.json）已通；无战斗存档 |
| 18 | 教程引导 | `[DONE]` | 开场渐隐提示卡；文案与两档瞄准/损管面板实现一致（P05-5/P05-10） |
| 19 | 本地化 zh-CN/en | `[PARTIAL]` | UI 框架+菜单/HUD 双语已通；长文案待补 |
| 20 | 战斗中暂停/退出 | `[DONE]` | ESC 菜单（继续/重开/回主菜单/退出，P05-8）；暂停=表现层停步进 |
| 21 | 3D 渲染 | `[DONE-灰模]` | Phase 02/03 已交付 3D 场景+真实舰船模型（无贴图）；贴图/海面/环境/特效升级归 WS 资产工作流 |
| 22 | 真实舰船资产 | `[DONE-P3]` | BIM2→glTF 转换器交付，23 舰批量转换（4 级 LOD+nodeMap 挂点），前端真实模型/程序化双轨；贴图管线（dxp）与击毁态 dmg 未做（→ WS 资产 VA-2/VA-3，2026-10-05 修订去向）；P03.2 已修复全舰队几何解码错误 |
| 23 | 飞机（玩家/AI 舰载机） | `[PARTIAL]` | AI 雷击/俯冲任务链 `[DONE]`（headless）；机型数据仅 1；航母系统 `[PLANNED]` |
| 24 | 导弹 | `[BLOCKED]` | `MissileSystem` 制导代码+测试在库，**未接入 BattleRunner**，无数据集（MDR-0015 架构已定） |

### 2.5 已知限制（2026-10-05 全仓核查，代码级验证）

> 本节为现状核查结论的正式登记；逐项的承接任务见括号内去向。已完成 Phase 的历史结论不因此改动。

**战斗机制静默失效（→ PHASE_04 §12 P04-13，最优先）**
1. 炮弹最大口径动能小破口的进水路径断线（组装顺序缺陷，接了空引用）——爆炸/鱼雷破口正常；
2. AI 动态损管优先级失效（同类型组装缺陷）——玩家手动损管不受影响；
3. 防空挂载未设置雷达状态——防空命中恒按"无雷达"较差散布档；
4. 战报 damageTaken/hits 两栏无写入方，恒为 0。

**视觉/音频资产（→ WS_VISUAL_AUDIO_ASSETS）**
5. 全部舰船模型无贴图（纯灰占位材质），贴图管线（dxp）未建；
6. 击毁态 dmg 模型未接入；炮塔仅偏航无俯仰动画；副炮/防空静态；
7. 海面=平面色块；岛屿=圆柱占位；飞机=体块占位；特效=发光渐隐球；
8. 音效全部程序合成（播放机制可用、资产缺失）；无音乐/UI 音。

**玩法/内容（→ WS_GAMEPLAY_COMPLETION）**
9. 主菜单仅能进固定 1v1；3v3/6v6 场景与观战无入口；无"再战"按钮；
10. GUI 固定 1x 实时无加速；当前平衡下 1v1 全 AI 常打满 60 分钟平局（黄金对局=双方存活）；
11. 玩家无鱼雷（引擎机制在、舰载鱼雷管数据与玩家指令缺失）、无倒车/油门档位、无独立开火键、副炮不可独立指挥；
12. 内容量：1 机型、1 型鱼雷、无航母、30 舰中 7 艘无真实模型。

**AI（→ PHASE_07）**
13. AI=单舰级基础可用（"会打但不太聪明"）：无编队协同/目标分配/集火，AI 水面舰不用鱼雷，飞机无空战格斗。

**产品化（→ PHASE_08）**
14. 1.0.0 安装包（2026-09-22）已落后 Phase 01–05 全部改动，须重新出包验证；
15. 6v6 偶发长帧/GC 峰值/首次 GLB 同步加载 ~1s/艘（3v3 卡顿已由 P04.6 修复，不再列为问题）。

**已解除的重大限制（历史记录）**
- ~~全舰队模型几何爆炸（拉伸三角/顶点解码错误）~~——2026-10-05 P03.2 修复，回归门常态化（WS 资产 VA-1）。

---

## 3. Simulation Core 架构

### 3.1 现状（全部 FACT，2026-10-02 实测）

```
NavyThunder.Core（零引擎依赖，net8/net10 multi-target）
├─ World/SimulationWorld      固定步长 0.02s；系统按注册序 Update；xoshiro256** 命名 RNG 流
│   └─ EventLog               有界环（20 万条），序号游标增量消费
├─ Systems（BattleRunner 注册序）
│   ballistics → AA → explosions → damageBridge → flooding → fire
│   → navigation → navalAI → guns → damageControl → killAdjudicator
│   → aircraftAdjudicator → flightModel → torpedoes → battle
├─ Ships/Ship                 纯状态：舱室部件盒+分段+浮力记账+舰员+备用弹药架
├─ Ballistics/BallisticsSystem RK4×4 子步+逐弹阻力+引信状态机+VT(仅对空)+水面引信
├─ Armor/ArmorResolver        官方 de Marre + 转正/跳弹/overmatch（校准 12/12）
├─ Damage/DamageRegistry      7 通道统一账本（Kinetic/Chemical/Overpressure/HydroShock/Fragment/Fire/Flood）
├─ Explosions/Fire/Torpedoes  破片+超压 / 独立掷骰起火 / 接触引信+hydroShock
├─ Ships/DamageControl/Flooding/KillAdjudicator  三流程损管 / 泵+浮力+横倾 / 6 通道击沉判定
├─ Aviation/FlightModel       简化气动+任务链（雷击/俯冲）+损伤写回
├─ Missiles/MissileSystem     制导层（IR/SARH/ARH）——⚠️ 未接入战斗
└─ Battle/BattleSystem        队伍/胜负/增量聚合/BattleReportGenerator

NavyThunder.Data     JSON schema、加载校验、BattleRunner（组装点）、场景 schema
NavyThunder.SimRunner    nt-sim --scenario：headless 全链路战报（确定性已验证）
NavyThunder.ProtectionAnalysis   数据摘要+校准报告 CLI
frontend/Godot   2D 占位前端（仅消费：World.Step() + Events 游标 + 实体状态读取）
```

### 3.2 架构裁决

`DESIGN DECISION`：
1. **Godot 永远不是战斗规则的实现位置**。前端只允许：推进 tick、读事件日志、读实体状态、提交玩家命令。任何"为了渲染好写"而把数值逻辑搬进 Godot 的改动一律拒绝。这是对现有架构（FACT：Core 零引擎依赖）的确认而非新设计。
2. **确定性是不可谈判的底线**：无墙钟、无 `Random.Shared`、RNG 只走命名流；任何新系统进 `SimulationWorld` 必须过黄金文件测试（golden 会捕捉行为分叉）。
3. **玩家与 AI 同接口**：命令面 = 舵/油门/炮塔指向/开火许可/弹种/损管优先级/目标指定。AI 系统产出的与玩家输入产出的必须是同一批命令对象。
4. **场景即配置**：`BattleScenario` JSON（编成/出生/AI/胜利条件）是战斗唯一入口；建造器产物编译为 ShipDefinition 后与史实舰同构。

---

## 4. 舰船模型（Ship Model）

### 4.1 层次结构（目标态）

```
ShipDefinition（数据，可来自史实录入或建造器编译）
├─ Identity        id / class(小艇→航母) / 排水量 / 长/宽/吃水 / 舰员总额
├─ HullSections[]  Bow/Mid/Stern 分段：HP、纵向范围、Role
├─ Parts[]         轴对齐盒部件（局部坐标）：Compartment/Magazine/FuelTank/
│                  Engine/Boiler/Turbine/Steering/FireControl/Radar/Pump/
│                  Turret/Hoist/TorpedoTube/ReadyRack
│                  · Hp / Crew / Open(露天，超压可伤员) / BuoyancySharePct
│                  · TurretGroup（炮组链：Turret+Hoist+ReadyRack 联动）
├─ ArmorPlates[]   板=盒的某个面（局部坐标 + 面法线 + 厚度mm + 材质）
├─ Guns[]          NavalGunDefinition：炮组/弹种(AP+HE)/身管数/射速/射程/
│                  回旋速度/水平垂直散布(mrad)
└─ Mobility        MaxSpeedKnots / TurnRateDegPerS / AccelerationFactor
```

FACT：该结构与现有 `Model/ShipDefinitions.cs` 一致；本节将其定为长期稳定 schema。
`DESIGN DECISION`：
1. **舱室布局是数据不是代码**：逐舰布局进 `data/ships/`（现状如此，WT 复杂布局=中线矩形舱段已对齐官方 wiki，来源 W1）。
2. **新增部件需求**（对照 WT 模块页，来源 W2）：`Funnel`（烟囱，-15% 速度）与 `Radio`（态势感知）为 Phase 04 候选；`AntiTorpedoProtection`（TDS）为 Phase 04 正式任务（RELEASE_CHECKLIST 已修正"未实现"记载）。
3. **舰桥特例**（来源 W1）：WT 舰桥可反复修理——NT 暂不采纳（保持"部件毁即毁"），在 MDR-0006 补记差异与理由（简化优先）。

### 4.2 数据分级（不可混淆）

| 级别 | 含义 | 现状例子（FACT） |
|---|---|---|
| Tier-1 | WT 客户端 blk 原始数值（单位换算而已） | `wt_ship_units.json` 的 displacementT/maxSpeedKnots（653/647 艘）；`wt_ship_weapons.json` 24 舰弹重/初速/装药/demarreK |
| Tier-2 | WT 原始值 + 模板推导混合 | `generated_fleet.json` 24 艘 `wt_client_extract+hand_template`（舱室布局=类模板，source.notes 自认） |
| Tier-3 | 史实/手工 | 6 艘 `hand_authored`；shell 引信参数（extract_shell_set.py 自认引擎侧近似） |
| 校准 | 近似参数显式标记 | `data/calibration/` 14 条（跳弹带 65/75°、破片锥 37.5°、起火 0.05 等） |

`DESIGN DECISION`：任何数据字段的 `source` 块必须保留四级标注；**禁止把 Tier-2/3 写成 "WT 数据"**。战斗中只允许 Tier-1/2/校准参与。

---

## 5. 空间几何（最高优先级技术设计）

### 5.1 问题陈述

FACT（2026-10-02 调查）：当前命中几何**不随舰船朝向旋转**——装甲板法线固定为世界 ±X/±Y/±Z（`ShipFactory.MakePlate`），部件盒 `Contains()` 是世界对齐轴测（船无旋转，局�=世界平移），`GunSystem.cs` 注释自认 "plates and turret offsets translate, never rotate"。后果：舷侧接敌与舰艏对敌的弹道几何完全相同，T 字横头、甲板/舷侧角分布、倾角效应全部失效。**这是全项目最大的单一保真度缺陷。**

### 5.2 三层空间与变换链（目标态）

```
World Space（仿真世界，Y 上，舰船在 XZ 平面航行）
   │  shipTransform = Translate(WorldPosition) × RotateY(HeadingDeg)
   │                 [× RotateX(Pitch) × RotateZ(Roll) —— 阶段二，见 5.5]
   ▼
Ship Local Space（船体局部坐标：+X 右舷 / +Y 上 / +Z 舰艏，原点=水线面中心）
   │  partTransform = 部件在 ShipDefinition 中的盒定义（静止，随船数据不变）
   ▼
Module Local Space（部件盒/装甲板自身坐标，用于面法线与入射角）
```

`DESIGN DECISION`（核心裁决）：
1. **装甲板与部件盒永久定义在 Ship Local Space**，数据文件不改语义（现有 30 舰数据无需重录）。
2. **命中检测变换射线，不变换几何**：`BallisticsSystem` 对每艘候选舰先算 `localRay = inverse(shipTransform) ∘ worldRay`，再对局部坐标的板/盒做 raycast（现行 `ArmorPlate.IntersectRay` 数学完全复用）。旋转成本 = 每 tick 每舰一次 2D 旋转矩阵（Phase 1 无 pitch/roll），远小于变换所有板。
3. **伤害事件的坐标也走同一条链**：`DamageBridge` 的内部追踪（interior trace）在局部空间进行，命中点/方向以局部坐标进入 `Ship.ApplyDamage`（现状 `PartAt(localPoint)` 接口可直接复用——它的参数本来就是"局部点"，只是现在局=世界）。
4. **炮塔**：`GunSystem.MountPosition` 定义在局部空间；炮塔回旋角（`TurretHeadingDeg`）叠加在船体航向之上求世界射向。炮塔射界（arc）数据字段预留（NavalArt 1.6 有射界编辑器，MDR-0016）。
5. **舰艏/舰艉/舷侧获得真实区分**：船体坐标 +Z=艏，则舷侧板=±X 面、艏艉壁=±Z 面、甲板=+Y 面。**数据审计任务**：现有 30 舰的装甲板定义几乎全是 ±Z 舷侧面（test_battleship 7 板全舷侧、uss_nevada 3 板两舷+甲板）——Phase 01 需为每舰补艏艉隔壁/甲板/炮座板数据（Tier-3 手工，按史实装甲图）。

### 5.3 受影响的系统与迁移顺序

| 系统 | 现状 | Phase 01 改动 |
|---|---|---|
| `ShipNavigationSystem` | 平移 WorldPosition；`Track()` 无人用 | 输出 shipTransform；删除 `Track`/`Translate` 双轨 |
| `GunSystem.FollowHullArmor` | 位移补偿（`Translate(displacement)`） | 随双轨删除而消失（板回到局部定义） |
| `BallisticsSystem.HandleArmorHits` | 世界坐标 raycast 全部板 | localRay 变换后 raycast；broadphase 用世界包围球（随船变换） |
| `DamageBridge.ApplyInteriorTrace` | 世界→局部减法（无旋转） | 用 inverse(shipTransform) 全变换 |
| `ExplosionSystem` | 世界坐标球查询 | 爆心变换入局部再查部件（或板/部件给世界 AABB——实现细节阶段定） |
| AI/导航 | 不受影响 | 不受影响 |

`INFERENCE`：该改动会改变战斗结果 → 黄金文件必失效 → Phase 01 的迁移策略是"行为锚点测试先行 + golden 有意识再生成 + changelog 记录原因"（不允许静默 rebase golden）。

### 5.4 舰船机动浮态

FACT：现有运动学 = 节×航向平移积分，无惯性无浮态；`ListDeg` 是进水不均衡记账。
`DESIGN DECISION`（分阶段）：
- **Phase 01–02**：保持运动学（确定性优先，改动集中在命中几何）；表现层对转向做视觉倾斜。
- **Phase 04**：横倾进入模拟（进水不均衡→list→超临界倾覆，MDR-0008 已有记账骨架）；纵倾（trim）随艏艉进水记账；pitch/roll 作为**只读输出**暴露给渲染与命中（局部变换加 RX/RZ）。
- **不做的**：实时流体力学、波面耦合（NavalArt 1.0 有波浪物理——NT 明确降级为视觉表现，理由：确定性与性能预算，MDR-0008 已裁决"物理沉没理念吸收、数值 WT 优先"）。

### 5.5 命中几何与视觉/碰撞三分离

| 层 | 内容 | 归属 |
|---|---|---|
| Armor/Damage Geometry | 板+部件盒（局部、数据文件） | Core（§4/§5.2） |
| Collision | 简化外壳体（弹道粗判、水线、鱼雷接触） | Core：Phase 01 从舱段盒派生凸壳，Phase 03 可选升级为资产外壳 |
| Visual Mesh | glTF（WT 提取或程序化） | 前端 only，与 Core 零耦合 |

`DESIGN DECISION`：三层严格分离；视觉网格**永远不是**命中依据（WT 亦然——其装甲是独立数据层，来源 W2/装甲视图）。

---

## 6. 战斗系统设计（逐系统五问）

> 格式：WT 怎么做 → NavalArt 怎么做 → NT 现状 → NT 最终设计（`DESIGN DECISION`）→ 为什么。资料来源标注见 `docs/research/`。

### 6.1 火炮与装填
- **WT**：炮塔组（Turret+Hoist+炮座装甲）；炮管损→射速降/散布增/卡死；扬弹机损→装填 -10~20%；一级弹药（ready rack）打空→主库补给 ~35s（开火暂停）；弹药库毁→按剩余弹药殉爆（W2/W4/W5）。
- **NavalArt**：可调炮塔+Ripple Firing+扬弹机部件（MDR-0016）。
- **NT 现状**：`GunSystem` 装填/回旋/散布/齐射纵轴分布+ready rack 补给 35s+炮塔毁降级（`Ship.ReadyRackReloadFactor=2.5`）。
- **NT 最终**：保留现框架；补齐——①炮管单独受损态（散布/射速惩罚曲线，Phase 04）；②炮塔回旋死角/射界字段（Phase 06 随建造器）；③弹药库殉爆威力∝剩余弹药（`KillAdjudicator.TntPerRoundKg` 已有雏形，对齐 W5 语义，Phase 04 验收）。
- **为什么**：现框架与 WT 机制形状一致（MDR-0011），增量补齐成本低于重写。

### 6.2 弹道
- **WT**：点质量+阻力（内部形式未公开，`ballisticsModel` 字段仅记录）；2.45 重做 100mm+ 全弹道阻力；散布椭圆与弹道解耦（W4、MDR-0002/0014）。
- **NT 现状**：RK4+逐弹二次阻力（`DragCoefficientScale` 对射表校准 ±3%）+引信状态机+VT 仅对空。
- **NT 最终**：保持 `IDragModel` 接口；不追求未公开的 Cd(M) 曲线（校准已达标）；散布模型保持数据驱动 mrad。
- **为什么**：校准是唯一可验证的真相源（FACT：12/12），未公开曲线属不可证伪复杂度。

### 6.3 装甲与穿透
- **WT**：de Marre 官方公式+转正乘数表（数值未公开）+overmatch 7:1+跳弹 ~70° 带；多层板逐层结算；破片锥 30–45°。
- **NT 现状**：全链路已实现且校准 12/12（`ArmorResolver`/`DeMarre`/calibration 14 条）。
- **NT 最终**：结构冻结；Phase 04 前用 WT 数据卡 30°/60° 列拟合转正乘数表（MDR-0004 遗留近似），为斜角命中（§5.2 旋转后才有意义）做准备。
- **为什么**：旋转命中几何让入射角真实化后，转正/跳弹才从"装饰"变"核心"。

### 6.4 内部损伤（模块/舰员）
- **WT**：模块清单+功能后果（§4.1 已对照）；舱室 125HP/人；乘员不回填。
- **NT 现状**：7 通道账本+内部 trace（残能定穿深）+部件盒伤害+舰员换算。
- **NT 最终**：补齐 WT 明文而 NT 缺失的四项——扬弹机装填惩罚改按损伤程度（10–20% 分档）、舵机损毁偏航（不冻死）、烟囱 -15%、FCR 毁→散布惩罚+炮塔本地射击（MDR-0014 框架已有）。全部进 Phase 04。
- **为什么**：这些是玩家可感知的功能反馈，优先于视觉。

### 6.5 火灾 / 6.6 进水 / 6.7 横倾 / 6.8 沉没
- **WT**：起火独立掷骰（与伤害量无关，W2 官方原文）；电梯火自顶向底；进水三破口类（动能仅最大口径/爆炸按强度/鱼雷最大最难补）；浮力损失→沉没；横倾→倾覆；泵实体模块。
- **NavalArt**：体积排水浮力、CoB/CG 指示、物理沉没（MDR-0016）。
- **NT 现状**：全链路已通且验收测试断言起火/进水/倾覆/沉没发生（`HeadlessAcceptanceTests`）。
- **NT 最终**：结构保留；Phase 04 增量——①破口按三类差异化（现有 breach 单一化）；②横倾从记账升级为 §5.4 的只读浮态输出；③Ammo Wetting 类改装位（数据字段）。
- **为什么**：MDR-0008/0010 已裁决框架；增量按官方差异表（research §5）逐条补。

### 6.9 鱼雷 / 6.10 防空 / 6.11 飞机
- **WT**：接触引信+可设深度（未确认冲突见 research §8）+hydroShock；防空 AI 炮手+VT；飞机三层任务。
- **NT 现状**：直线雷+接触引信+hydroShock+空投包线（`TorpedoSystem`）；AA 挂载是**硬编码单 VT 座**（`BattleRunner` 注释自认 R1.4 半成品）；飞机 1 机型。
- **NT 最终**：①TDS 正式实现（MDR-0012 几何+隔舱衰减，不拦水下 AP；WT 官方确认 TDS 为可摧毁实体，research §5）；②AA 挂载数据化（进 ShipDefinition，废硬编码）；③机型扩录（→ WS_GAMEPLAY GP-5，2026-10-05 修订去向）；④鱼雷深度可调性用 blk 复核后定。
- **为什么**：TDS 是 RELEASE_CHECKLIST 修正后的明确欠账；AA 数据化是"数据驱动"原则的补课。

### 6.12 导弹
- **WT**：现代层（MDR-0015 架构决议）。
- **NT 现状**：制导代码+测试在库，**未接线**。
- **NT 最终**：Phase 04 仅接"发射平台+数据集"最小闭环（或明确推迟到 Phase 07 后，取决于 Phase 04 结束时的优先级——**不在 Phase 01–03 排期**）。
- **为什么**：核心海战体验不依赖导弹；避免分散。

---

## 7. 三方对照总表（WT / NavalArt / NT 现状 / NT 最终）

> "未确认"= 未找到可靠来源；不编造。行明细的证据链在 `docs/research/` 各文档。

| 系统 | War Thunder | NavalArt | Navy Thunder 现状 | Navy Thunder 最终 |
|---|---|---|---|---|
| 舰船建造 | 无（成品舰） | 部件实例表 XML+派生值运行时重算+8 参数分段变形 | 无 | 块/部件实例表→确定性编译器→ShipDefinition（Phase 06，详 §10） |
| 舰船结构 | 舱室/模块/分段（x-ray 可视） | 部件=实体块 | 舱室+模块+分段（数据） | 保留+烟囱/无线电/TDS 增件 |
| 装甲 | 板层+材质+角度机制，x-ray 展示 | 块状 mm 装甲按体积计重 | 轴对齐板（**不随航向转**） | 局部坐标板随船旋转（Phase 01）；建造器逐块装甲（Phase 06） |
| 火炮 | 炮塔组+扬弹机+一级弹药 | 可调炮塔/Ripple | 全链路+ready rack | +炮管损/射界字段 |
| 弹道 | 点质量+阻力（未公开形式） | 1.41 重做（仅借鉴表现） | RK4+校准阻力 | 结构冻结，拟合转正表 |
| 损伤 | 模块化+功能后果+hit cam | 1.52 双轨 HP（仅参考） | 7 通道+内部 trace | +四项 WT 明文补齐 |
| 进水 | 三类破口+泵+浮力% | 四象限浮力指数+物理涌现横倾 | 单一破口+泵+记账 | 三类破口+横倾浮态输出（四象限思想吸收，保确定性） |
| 火灾 | 独立掷骰+电梯蔓延 | 有火系统（数值不采用） | 掷骰+分区 | +蔓延路径（炮塔→电梯→库）对齐 W5 |
| 损管 | 2.51 三流程自动+手动开关 | 无 | 自动三流程（无玩家交互） | +玩家优先级 UI/手动模式开关（Phase 05） |
| 鱼雷 | 接触引信+深度设置（未确认） | 鱼雷部件 | 直线雷+空投 | +TDS+深度复核 |
| 防空 | 数据化 AA+VT+AI 炮手 | AA 点射部件 | 硬编码单 VT 座 | AA 数据化进 ShipDefinition |
| 飞机 | 舰载机任务+航母 | 中队体系（V1.0） | 任务链+1 机型 | 机型扩录；航母系统 Post-1.x |
| 导弹 | 现代层 | 1.0 导弹重做 | 制导代码未接线 | 最小闭环或推迟（§6.12） |
| AI | 服务端 AI（参数未公开） | — | 威胁加权+四态机动 | +编队/目标分配/损管优先级（Phase 07） |
| HUD | 速度/舵/装填/损管面板/分段刻度 | 简洁 | 文本 HUD | 图形化 HUD（Phase 05，对照 W7/W12） |
| 小地图 | 有（细节未确认） | 有 | 无 | 战术地图兼小地图（Phase 05） |
| 3D 表现 | Dagor 3D+水面/特效全套 | Unity 3D | **2D 俯视色块** | Godot 4 3D（Phase 02/03） |
| 确定性 | 无（服务端权威） | 无 | **有（独有）** | 永久保持 |

---

## 8. 3D 前端设计（Phase 02 起的目标结构）

### 8.1 节点结构

```
Battle3D (Node3D)
├─ Ocean            海面：Godot 着色器（Gerstner 波视觉级），与模拟零耦合
├─ Sky/Environment  天空+光照（WorldEnvironment）
├─ Islands          岛屿占位网格（数据驱动坐标）
├─ Ships/           每舰一 ShipVisual(Node3D)
│   ├─ HullMesh     MeshInstance3D（glTF 资产或程序化船体，§9 三分离）
│   ├─ Turrets/     每 TurretGroup 一 TurretVisual（回旋/俯仰跟随 GunState）
│   ├─ Wake         尾流（GPUParticles 或着色器带）
│   ├─ FireFX/SmokeFX  事件驱动粒子
│   └─ SinkPose     沉没姿态（读 KillState+横倾，表现层插值）
├─ Projectiles/     曳光/弹丸可视化（只读 Ballistics.Projectiles 采样）
├─ Aircraft/        AircraftVisual（任务链状态驱动）
├─ FXLayer          爆炸/水花/炮口焰（事件日志游标消费——沿用现 CollectEffects 模式）
├─ CameraRig        追击相机/自由相机/瞄准视角（对照 W7 命中相机）
├─ HUDLayer(CanvasLayer)  图形化 HUD（§8.3）
└─ TacticalMap      2D 战术地图（**复用现 BattleView 降级改造**——2D 投入不废弃）
```

### 8.2 Simulation → Presentation 同步机制

`DESIGN DECISION`：
1. **主循环**：Godot `_Process(delta)` 累积真实时间 → 固定步长驱动 `World.Step()`（可多步/帧）→ 渲染读最新状态。
2. **事件**：`Events.After(cursor)` 游标模式（现有 `_fxCursor` 模式推广为唯一事件消费方式）。
3. **状态**：每帧直接读实体属性（位置/航向/装填/KillState）；`ShipVisual.Transform` 从 shipTransform 构造——**前端必须使用与 Core 相同的变换语义**（Phase 01 提供共享的变换工具或前端重复实现并测试对齐）。
4. **平滑**：tick 间不做模拟插值（确定性显示优先，60Hz tick 足够平滑）；相机与粒子允许视觉平滑。
5. **性能预算**：6v6 全武器 ≥60fps（Phase 08 验收；Phase 02 起 CI 加 Godot headless 冒烟）。

### 8.3 HUD 元素清单（对照 WT，来源 W1/W4/W5/W7 + UX 基线）

| 元素 | WT 依据 | NT 状态→目标 |
|---|---|---|
| 航速/舵角/油门 | HUD/Controls 页（节流阀为轴） | 文本已有→图形仪表（Phase 05） |
| 装填条+弹种 | — | 文本已有→环形装填指示 |
| 伤害面板（分段刻度+火/进水图标） | 2.45/2.47 明文 | 文本血条→图形分段图（Phase 05） |
| 损管面板（三流程+优先级） | 2.51 dev blog | **无→Phase 05 新做** |
| 提前量/瞄准线 | 2017 瞄准系统 | 无→Phase 05（`FcsSolver` 接入） |
| 命中反馈（命中/跳弹/过穿图标） | hit icons 开关（2.45） | 无→Phase 05 |
| x-ray/命中相机 | W12/2017 公告 | 无→Phase 05+（依赖模块数据可视化） |
| 小地图 | 存在（细节未确认） | 无→战术地图（Phase 05） |

---

## 8.5 测试分层（权威：`tests/README.md`）

测试按 `[Trait("Bucket", ...)]` 分六档：**Fast**(单元/局部机制,秒级,默认+PR CI)、
**Integration**(≤300 s 短真实战斗,PR CI)、**Golden**(3600 s 确定性回归,Nightly)、
**Acceptance**(AI 编队/r1 行为验收,Nightly)、**Performance**(预算门,Nightly+Release)、
**Soak**(`tools/soak.sh`,Release 前)。禁止黑名单定义档位;禁止完整战斗进入 Fast;
确定性由 Golden 一处背书 + 短切片双跑对比。规则与自检清单见 `tests/README.md`。

## 9. 真实舰船资产管线（三分离 §5.5 的资产侧）

### 9.1 现状（FACT）
- 原始资产在位：`D:\WarThunder\content\base\res\ships\` 598 个 .grp（3.9GB）；aces.vromfs 17.9MB；`_wt_audit/` 已解包 2,344 舰 blk + 382 炮 blk + DagorEngine 参考源码。
- 工具链：GRP2 容器**完整解析**（`tools/extract_ship_model.py`）；**BIM2 v7 dynmodel 全链路已反序列化并交付**（`tools/convert_bim2_gltf.py`，2026-10-03：Oodle 解压/两种实测顶点布局/packed IB/GeomNodeTree+skinNodes 桥接/4 级 LOD/glTF 输出）；碰撞=量化 BVH（0xace50003）未解析（Phase 08 可选）。
- 保底轨在役：`tools/generate_hull_obj.py` 程序化船体（32 舰 hull.obj，738 顶点/1360 面），前端已消费甲板轮廓。
- **[2026-10-05]** P03.2 修复全舰队几何爆炸（顶点布局 storageFormat 表驱动 + packed IB 块边界）；23 舰×4 LOD 零索引违规+8 舰侧影核对。**管线已具备基础生产能力，但最终视觉资产质量未达发布目标**：贴图（dxp）未建=全灰模、dmg 击毁态未接入、7 艘无模型——缺口与 WT 资产可用性评估正式纳入 docs/phases/WS_VISUAL_AUDIO_ASSETS.md（含 LICENSE_AUDIT 许可边界）。

### 9.2 双轨策略

`DESIGN DECISION`：
- **目标轨（Phase 03）**：Python BIM2→glTF 转换器。**可行性已定**（`docs/research/DAGOR_ASSET_RESEARCH.md`）：BIM2=`DynamicRenderableSceneLodsResource` 结构 dump（开源引擎读写两端源码互证，格式破解约 95%）；顶点块 100% OODLE 压缩（本机实测解压成功，Bismarck 4.65MB→23.7MB 样本留档）；骨架=GeomNodeTree（Bismarck 771 节点）可直接服务模块化损伤挂点。工作量 M（2-4 天）。风险=Oodle DLL 依赖（三级降级兜底）。
- **保底轨（Phase 02 即用）**：程序化 3D 船体升级——现 hull.obj（41 站线光壳）升级为含上层建筑/炮塔座的分层船体（数据源=ShipDefinition 部件盒），直接映射 §5.2 局部坐标。**Phase 02/04 全部验收不依赖 BIM2 成败**。
- 视觉与命中三分离保证：资产轨失败不伤玩法；资产轨成功不碰 Core。
- 合规：研究用途提取（LICENSE_AUDIT 假设①-④）；**分发包不含 WT 原始文件**；转换器输入=用户自有客户端。

---

## 10. 舰船建造系统（Phase 06）

### 10.1 设计基线

- **基准**：NavalArt（MDR-0016 全文为设计决议 + 反编译一手细节 `docs/research/NAVALART_RESEARCH.md`）：可调船体模块（米制 0.05m 刻度、8 参数分段变形+镜像对）、块状 mm 装甲（按体积计重）、部件清单广度（分档主机/螺旋桨/舵/容积泵/可调炮塔/扬弹机/AA/VLS 预留/机库预留）。
- **NT 现状**：零实现；`ShipDefinition` 即编译目标格式。

### 10.2 建造器数据流

```
BuilderScene（Godot UI）
   │ 编辑 BuilderDesign（NT 自己的中间格式，JSON+版本号，局部坐标+稳定实例 GUID）
   │   hullBlocks[] / armorBlocks[] / parts[] / 名字/元数据
   ▼  "编译"（确定性纯函数，Core 内实现——不在前端！）
ShipDefinition（与史实舰同构）+ 校验报告（浮力/重量/平衡）
   │  存档 AppEnv saves/designs/<id>.json（派生值不落盘——加载后重算，NA 经验）
   ▼
战斗 / AI 对战 / 分享（本地文件导入导出）
```

### 10.3 编译器派生规则（吸收 NA 反编译结论）

```
体积   → 块级累积 → 静态吃水 = 体积-高度阶梯曲线求逆（NA 的 1D 静水力逆解，无需体素）
重量   = 体积 × 密度；装甲块密度 = f(armor)（NA 计重规则：armor 越厚自动越重；
         派生常量进 calibration 显式标记）
CoM    = 质量加权；CoB = 体积加权（NA 方法）
生死池 = 全舰储备浮力（对齐 MDR-0008；不用 NA 的隐式幂律耐久池）
校验   ：储备浮力 > 0 / CoM-CoB 力矩合理 / 动力 ≥ 排水量下限 / 舵面积 ≥ 下限 /
         兴波界限 √L×2.43 作航速上限校核（NA 公式只作校核，不作推进模型）
```

`DESIGN DECISION`：
1. **编译器进 Core**（`NavyThunder.Core.Builder`）：不合理设计（浮力<重量、无动力）给校验错误而非战斗中崩溃。
2. **CG/CoB 指示器第一天就做**（MDR-0016 教训：NavalArt 1.53 才补齐，NT 抢跑）。
3. 操作集：放置/移动/旋转/删除/复制/撤销/重做/参数编辑/保存/加载/试航（headless 战斗测试入口）。
4. **不做**：顶点级编辑（Phase 06 先模块级；NavalArt V1.1 的顶点编辑列为后续）、多人共建、工坊上传。
5. 对 NA 存档格式的取舍（NAVALART_RESEARCH §9 全表）：采纳派生值不落盘/容错逐部件加载/镜像对；拒绝世界坐标存部件（NT 用局部坐标+GUID）与无版本号（NT 带版本+迁移器）；保留显式装甲板/区（不学"每部件单一 armor 标量"）。

---

## 11. 玩家操作链

```
Godot Input（键鼠）
  → GameplayCommand（结构化命令：Helm/Rudder/Throttle/GunOrder/ShellSelect/
                     DcPriority/TargetAssign —— 与 AI 产出同构，§3.2-3）
  → Simulation Core（World.Step 内生效，确定性）
  → Simulation State + EventLog
  → Godot Presentation（HUD/相机/特效/战术地图）
```

命令清单（Phase 05 全量）：舵/油门（含档位与倒车）/主炮瞄准（目标锁定+测距+试射修正，对齐 W7 流程）/弹种/副炮自动策略/防空策略/鱼雷射击/损管优先级/目标指定/暂停。**所有命令经 `BattleRunner` 提交，禁止前端直改 `Ship` 字段以外的新路径**（现状 `ApplyHelm` 直写 `ThrottleCommand` 属于受控例外——命令对象化时一并收编，Phase 05 任务）。

---

## 12. AI 设计

- **现状（FACT）**：`SimpleNavalAISystem` 威胁加权选目标+四态机动+zigzag+停火纪律；飞机任务链+重定向；全部同接口无作弊。
- **当前水平（2026-10-05 如实评级）**：**单舰级基础可用——"会打，但不太聪明"**。能航行/选目标/走位/按纪律开火，但无编队协同、无目标分配与集火、AI 水面舰不用鱼雷、飞机无空战；AI 动态损管因接线缺陷暂未生效（修复归 P04-13）。该评级是 Phase 07 的出发点，不是其验收结果。
- **最终（Phase 07）**：单舰层保留；新增编队层（目标分配/占位阵型/鱼雷齐射角/集中火力）；损管优先级策略化；撤退/增援决策；难度=可调参数（瞄准误差/决策延迟）而非规则不对称。
- **不变量**：AI 与玩家同一命令接口；AI 不读玩家不可知信息（现状已满足，保持）。

---

## 13. 测试体系

### 13.1 必须保留（不可破坏，另见 §16）

| 门禁 | 文件 | 作用（FACT） |
|---|---|---|
| 黄金文件 | `GoldenFileTests` + `tests/golden/bb_duel_summary.json` | 确定性指纹，行为分叉警报 |
| 校准回归 | `StatCardCalibrationTests` + `docs/calibration-report.json` | 官方射表 12 点 |
| headless 验收 | `HeadlessAcceptanceTests`（双场景） | 起火/进水/殉爆/击落/胜负行为级断言 |
| 性能预算 | `PerformanceBudgetTests` | 6v6 ≥1×；**Phase 00 加 Debug 快速模式**（现 Debug 全套 52 分钟） |
| 数据校验 | `DataRepositoryTests` + CI schema 加载 | 全数据集可载 |

### 13.2 新增（按阶段）
- Phase 01：**几何旋转测试**（同射线不同航向命中面改变/局部-世界往返变换/正交性）；golden 有意识再生成。
- Phase 02：Godot headless 3D 冒烟（NT_FRONTEND_SMOKE 扩展：Node3D 存在性+战斗跑通+退出码）。
- Phase 03：资产校验（glTF 加载/网格 AABB 在数据范围内/材质引用存在）。
- Phase 04：TDS 对照测试（带/不带 TDS 水爆伤害差）；AA 数据化对照（去硬编码后行为不变性）。
- Phase 05：HUD 状态映射测试（Simulation 状态→HUD 文案/图标映射表）；命令对象化回归。
- Phase 06：建造器编译器测试（净重/浮力恒等式、非法设计拒绝、编译结果可进战斗）。
- Phase 07：编队场景测试（非单元：编队行为出现在战报统计）。
- Phase 08：2h soak（已有 `tools/soak.sh`）扩展到 3D 前端；崩溃恢复测试。

---

## 14. 不可破坏资产清单

> 重构这些必须先写迁移方案（为什么/如何迁移/如何证明能力不丢失），并在 MDR 或 PROJECT_DESIGN 增补记录。

| 资产 | 保护理由 |
|---|---|
| `SimulationWorld` + `EventLog` + `DeterministicRandom` | 确定性根基，lockstep 前提 |
| `BallisticsSystem`（RK4/引信/水面引信） | 校准 12/12 的载体 |
| `ArmorResolver`/`DeMarre` | 官方公式实现+测试锚 |
| `DamageRegistry` 7 通道 | 全损伤链统一账本（ProtectionAnalysis 依赖） |
| `DamageBridge/Fire/Flooding/DamageControl/KillAdjudicator` | MDR-0005~0011 的实现载体 |
| `SimpleNavalAISystem` | 同接口 AI 原则的现成实现 |
| `BattleRunner` + `BattleScenario` schema | 场景→战斗唯一组装点 |
| `SimRunner` + `tests/golden/` | 确定性可复现的外部证明 |
| `tools/unpack_vromfs.py`/`blk_decode.py`/`extract_*.py` | 自研 WT 数据管线（99.5% 抢救率） |
| `StatCardCalibrationTests`/`HeadlessAcceptanceTests`/`PerformanceBudgetTests` | 三大行为门禁 |
| MDR-0001~0016 | 机制决议审计链 |

---

## 15. 开发阶段总览

详细设计在 `docs/phases/PHASE_00..08`（每份含 PASS/FAIL 可测断言）。依赖链：

```
Phase 00 基础与文档统一 ✅
   └→ Phase 01 Simulation Geometry（局部坐标命中）★地基 ✅
         ├→ Phase 02 3D Gameplay Foundation ✅
         │     └→ Phase 03 真实舰船资产 ✅(尾项→WS 资产)
         │           └→ Phase 04 完整海战 Gameplay ✅大部分(收尾=P04-13 缺陷修复)
         │                 └→ Phase 05 战术 UI / 玩家体验 ✅(2026-10-05)
         │                       ├→ P04-13 战斗接线缺陷修复（当前最优先）
         │                       ├→ WS_GAMEPLAY_COMPLETION 玩家体验补全 ∥
         │                       └→ WS_VISUAL_AUDIO_ASSETS 视觉与音频资产 ∥
         │                             ↓
         │                       Phase 06 舰船建造（依赖 04 的稳定 ShipDefinition + 01 几何）
         │                             └→ Phase 07 AI / 大规模战斗
         │                                   └→ Phase 08 性能 / 稳定性 / 产品化（汇入 WS 两工作流产物）
```

> [2026-10-05 增补] 两个 WS 工作流与 Phase 06/07 **并行**而非串行尾巴：直接影响当前观感/听感/节奏的里程碑（VA-2 贴图、VA-7 核心战斗音、GP-1 节奏、GP-2 模式选择）先行，装饰性与氛围项靠后，全部在 Phase 08 汇总验收。缺口按性质分流，不回填已完成 Phase。

并行轨道：W3 数据扩录（持续）、W2 校准深化（依赖 Phase 01 场景输出）、BIM2 逆向研究（Phase 02 期间后台进行）。

---

## 16. 修订记录

| 日期 | 修订 |
|---|---|
| 2026-10-02 | 初版：基于全仓调查 + WT/NavalArt/资产技术研究建立 |
| 2026-10-02 | Phase 01 实施完成：§5 空间几何裁决落地（`Geometry/ShipTransform` + 局部坐标板 + 逆变换射线命中）；坐标约定 +Z=舰艏 已随数据迁移生效；新增 FACT——数据帧 = 船体局部（+X 右舷/+Y 上/+Z 舰艏），所有舰船/飞机数据与命中几何遵守；行为基线变化（对称编成出现合法平局）记录于 PHASE_01 §0 |
| 2026-10-03 | Phase 02 实施完成：§8 3D 前端结构落地（Battle3D 根 + ShipVisual/炮塔/弹丸/飞机/FX/相机/海面 + HUD + TacticalMap 降级复用）；Core→Godot 恒等坐标映射经冒烟同步断言与四航向截图验证；**修复既有 bug——玩家交接后 AI 仍经 mind 循环抢舵**（`SimpleNavalAISystem.ReleaseMind`，回归测试 `PlayerHandoverTests`）；产品结构表状态更新：#4 战斗场景 [PROTOTYPE]→3D 骨架 [DONE-占位资产]、#12 战术地图 [DONE]、#13 音效接线 [DONE-合成音] |
| 2026-10-05 | **[插入] Phase 03 资产几何修复**：BIM2 顶点布局（storageFormat 字段驱动）与 packed IB 块边界（1B 头）双双解错导致全舰队模型出现跨区域拉伸三角；修复后 23 舰×4 LOD 3800 万索引 0 窗口违规 + 8 舰光栅侧影核对 + GUI 三舰截图；详见 `ASSET_PIPELINE_CURRENT.md` §6.5 |
| 2026-10-05 | Phase 06 实施（MVP）：§10 建造器数据流落地——`BuilderDesign` v1（版本号+迁移器从 day one）→ `ShipCompiler` 确定性编译（体积-高度阶梯吃水求逆、NA 密度公式、储备浮力生死池、校验错误/警告分级）→ `RegisterCompiled` 同构注入战斗；BuilderScene MVP（放置/旋转/删除/撤销重做/CG-CoB/吃水预览/存取/试航）。测试：恒等式/手算吃水/拒绝矩阵/字节确定性/端到端战斗/100 份随机批，22 项全绿。schema 权威文档 `docs/BUILDER_DESIGN_SCHEMA.md` |
| 2026-10-05 | Phase 05 实施完成：§11 玩家操作链全量落地——`GameplayCommand` 命令对象化（`BattleRunner.Submit` 唯一入口，`ApplyHelm` 直写例外收编）、两档瞄准（自动=锁定+试射修正 W7 AB 带、手动=光标落点+滚轮调距）、图形 HUD/损伤面板/损管面板、五态命中反馈、暂停菜单、Ambient 音量+键位重绑定、全 UI 双语；测试分层契约新增自动审计（`BucketCoverageTests`）。产品结构表：#6 瞄准与开火 [PARTIAL→DONE-两档]、#9 玩家损管操作 [PLANNED→DONE]、#14 小地图 [PLANNED→DONE-战术地图兼任]、#20 暂停 [PLANNED→DONE]、#3 设置 [PARTIAL→DONE-键位+Ambient]、#18 教程 [PARTIAL→DONE-文案一致] |
| 2026-10-05 | **现状核查与文档同步**：全仓代码级核查（玩家体验面/战斗机制/资产/数据/测试，Fast 136 全绿）后——README 按"项目现状说明"重写；§2 产品结构表修正 #21（3D 渲染 [PLANNED]→[DONE-灰模]，Phase 02/03 已交付而该行未同步）；新增 §2.5 已知限制 15 项（含 4 项战斗接线静默失效→P04-13）；§9 补 P03.2 几何修复与"管线有生产能力、资产质量未达发布目标"结论；§12 AI 如实评级"单舰级基础可用（会打但不太聪明）"；§15 依赖链纳入两个并行工作流（WS_VISUAL_AUDIO_ASSETS / WS_GAMEPLAY_COMPLETION）；PHASE_03（尾项去向）/PHASE_04（P04-13）/PHASE_07（AI 基线）/PHASE_08（性能基线+打包欠账）同步增补，ROADMAP 加指向横幅（1.0 历史不改） |
