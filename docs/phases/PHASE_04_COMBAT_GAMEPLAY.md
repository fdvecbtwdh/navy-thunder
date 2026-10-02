# Phase 04 — 完整海战 Gameplay（机制补全）

> 状态：`[PLANNED]`
> 前置依赖：Phase 01（斜角命中让转正/跳弹真实化）、Phase 02（反馈可见）。部分任务可提前。

## 1. 阶段目标
把 `docs/research/SHIP_DAMAGE_REFERENCE.md` 中"WT 官方明文而 NT 缺失"的机制逐条补齐，并对两处文档冲突做出裁决。完成后，NT 的战斗机制面与 WT（2.45–2.51 海战改线）对齐，除明确放弃项。

## 2. 当前基础（Level A）
损伤链全通（起火/进水/损管/殉爆/6 通道击沉，验收测试断言）；缺口：TDS 零实现、AA 硬编码单 VT 座、破口单一化、舵毁冻死、无烟囱/无线电、舰桥无特例、机动不影响精度、横倾纯记账、火蔓延无结构链、殉爆威力雏形。

## 3. 外部参考与来源
`WAR_THUNDER_NAVAL_RESEARCH.md` §4-§9（Level B）+ `NAVALART_RESEARCH.md` §4/§6（Level C）+ `SHIP_DAMAGE_REFERENCE.md` 全表（综合）。

## 4. 不做什么
- 玩家损管 UI（Phase 05）；AI 编队（Phase 07）；新武器类型；数值平衡 pass（本阶段只做机制结构，平衡归 Phase 08 前调）。

## 5. 任务与技术设计

| # | 任务 | 设计要点 | 来源依据 |
|---|---|---|---|
| P04-1 | **TDS 鱼雷防护** | `AntiTorpedoProtection` 部件（Kind 新增）：吸收水爆（hydroShock/爆炸通道按设计承受值削减），超限被摧毁（对齐 W5 bugfix 语义）；**不拦水下 AP**（MDR-0012）；数据面=每舰可选部件 | W5 官方确认实体存在；MDR-0012 |
| P04-2 | **AA 数据化** | AA 挂载进 `ShipDefinition`（口径/射程/射速/VT/座数），废 `BattleRunner` 硬编码 `usn_127mm_mk31_aa_vt`；AA 走独立弹幕通道不进穿深公式 | W2、NA §5（AA=独立 dps 通道） |
| P04-3 | **破口三类化** | 动能破口=仅最大口径弹；爆炸破口∝强度；鱼雷破口最大+修补最久；修补时间分档 5-20s | W5 |
| P04-4 | **丧失不沉性段数裁决** | 官方原文 3 段（W4）vs 代码 2 段。**裁决建议：改为 3 段**（对齐官方；战斗节奏差异用段 HP 校准吸收）；MDR-0007 增补记录 | W4 vs MDR-0007 |
| P04-5 | **模块后果四件套** | 扬弹机装填惩罚按损伤分档 10-20%；舵毁=偏航漂航+可修理（非冻死）；烟囱 −15% 速度（新部件）；FCR 毁=散布惩罚+炮塔本地射击（后两者新部件） | W2 |
| P04-6 | **火蔓延结构链** | 炮塔火→电梯自顶向底→弹药库（沿 TurretGroup）；电梯底部命中=立即底层起火；持续殉爆掷骰 | W5、NA Dyk 链 |
| P04-7 | **横倾浮态输出** | 进水不均衡→list→超临界倾覆升级为可读浮态（Phase 01 的 ShipTransform 预留 RX/RZ 位启用，只读）；沉没姿态从损伤分布涌现（弃 NA 随机姿态） | NA §4、W7 |
| P04-8 | **机动精度惩罚** | 机动（自舰+目标）进 `Gunnery` 散布项，对称作用于 AI/玩家；数值进 calibration | W7 官方原文 |
| P04-9 | **弹药库殉爆验收** | 威力∝剩余弹药语义对照 W5 验收（`TntPerRoundKg` 已有雏形） | W5 |
| P04-10 | **舰员阈值技能面** | 修理/自沉双阈值预留修正系数（AI 难度轴复用），默认=现值 | W1 |
| P04-11 | **鱼雷深度复核** | 用当前客户端 blk 核 `diveDepth` 可调性；按结论改数据面或维持 | B9 vs MDR-0012 冲突台账 |
| P04-12 | **过穿/轻甲手感** | 评估 NA 的 OverPen=1/30 伤分配（轻甲活得下来挨得疼）是否引入；先做对照模拟 | NA §6、MDR-0005 备选 |

## 6. 文件修改范围
`src/NavyThunder.Core/Ships/`（Ship/PartKind/DamageControl/KillAdjudicator/GunSystem）、`Torpedoes/`、`AntiAir/`、`Ballistics/Gunnery`、`Model/ShipDefinitions.cs`、`src/NavyThunder.Data/`（schema+校验）、`data/ships|calibration/`、`tools/generate_ships.py`（新部件模板）、`tests/`。

## 7. 依赖 / 并行
- 依赖：Phase 01（P04-7/8/12 强依赖；P04-1/2/3/5/6/9/10/11 弱依赖可先行）。
- 并行：Phase 05 UI 组（P04-5/6 的 UI 面另开）；W3 数据扩录。

## 8. 风险
| 风险 | 对策 |
|---|---|
| 大改伤 golden/验收 | 每任务独立 commit；行为级验收测试随改随更；golden 按惯例再生成 |
| 3 段裁决改变战斗时长 | 用 6v6 场景统计击沉时长分布对照（Phase 08 平衡 pass 输入） |
| 新部件导致 30 舰数据迁移 | 模板生成器补默认值；schema 版本号+1 |

## 9. 测试
- TDS：带/不带 TDS 同雷对照（水爆伤害差+TDS 毁坏态）；水下 AP 不受 TDS 影响断言。
- AA 数据化：移除硬编码后行为等价性测试（同场景同结果）。
- 破口三类：三来源同口径命中→进水率排序断言。
- 段数：3 中段毁→丧失不沉性；2 段不触发。
- 蔓延：炮塔火 240s 内经电梯达弹药库的概率事件断言（对照 W5 时间感）。
- 横倾：单侧舱室注满→list≈预期→倾覆通道触发。
- 全部进 `HeadlessAcceptanceTests` 或新场景测试（行为级）。

## 10. 验收标准（PASS/FAIL）
- PASS：同一鱼雷命中带 TDS 与不带 TDS 的同型舰，核心舱伤害与进水显著不同，且 TDS 在超限时被摧毁。
- PASS：`BattleRunner` 中不再存在硬编码 AA 挂载；防空来自 `ShipDefinition`。
- PASS：三种破口来源在同舰上产生可区分的进水/修补时间。
- PASS：机动目标对固定射击方与自身命中率的惩罚可测（calibration 参数生效）。
- PASS：横倾/沉没姿态随损伤侧分布变化（非随机）。
- PASS：113 现有测试迁移后全绿+新增行为测试全绿；golden 按惯例再生成。
- PASS：MDR-0007/0012 冲突闭环（裁决记录在案）。

## 11. 完成后状态
机制面与 WT 2.51 对齐（明确放弃项除外）；Phase 05 的 UI 有真实机制可展示；Phase 06 建造器的部件语义稳定。
