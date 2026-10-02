# War Thunder 海战机制研究笔记

> 研究目的：为 Navy Thunder 的战斗机制设计提供官方来源依据。
> 与 `docs/mechanisms/`（MDR）的关系：MDR 是**已裁决的机制决议**；本文档是**研究底稿**——记录来源、原文要点、与 MDR/代码的冲突点，供后续修订 MDR 与设计文档使用。
> 来源等级：B=官方（wiki/官方公告/changelog）· D=可靠社区 · E=论坛视频观察。
> 抓取日期：2026-10-02（WebFetch 全文抓取，非搜索摘要）。

---

## 1. 舰员与舱室（MDR-0006 交叉验证）

**来源**：官方 wiki《Ship Crew Mechanics》，作者 wolftale，2024-12-10，<https://wiki.warthunder.com/640-ship-crew-mechanics>（Level B）

FACT：
- 舱室按总耐久分摊舰员损失：官方示例"500 HP 舱室含 4 人 → 每 125 点伤害损失 1 人"（125 HP/人是由示例推导的通用换算，与 wt_reference `crew_hp_per_member=125` 一致）。
- 舱室**不可修复**，舰员**不会回填**已损毁舱室；例外：**舰桥可反复修理**（官方举例：G-5 鱼雷艇可通过反复摧毁舰桥 5 次击沉而不伤船体）。
- 双阈值（以 USS Mitscher 350 人为例）：**最低修理阈值**——满技能剩 122 人（约 5%）、无技能剩 171 人（约 14%）时无法修理/损管，开始进水下沉；**0%（毁灭）阈值**——满技能对应 105 人、无技能 147 人，归 0 立即沉没。阈值随舰员技能浮动。
- 舱室布局分两型：**简单布局**（快艇/部分炮艇：舰员舱横跨整段水密船体）与**复杂布局**（蓝水舰：中线上矩形舱段，需穿透更深才伤员）。
- 装甲分析器有 "Show crew distribution" 员密度热图（红=高收益目标）。
- 鱼雷发射管只有带雷且有人值守时才含舰员。

与 NT 现状对照：`Ship.CrewHpPer=125`、`CrewRepairThreshold`/`CrewSurviveThreshold` 双阈值已实现（Level A）；**舰桥可反复修理**未实现（NT 舰桥=普通部件，毁即毁）；简单/复杂布局二型未区分（NT 统一复杂布局）。

---

## 2. 模块清单与功能后果（MDR-0006/0014 交叉验证）

**来源**：官方 wiki《Ship Modules》，2020-09-02（**注意：早于 2.45/2.51 重做，个别细节可能过时**），<https://wiki.warthunder.com/mechanics/5245-ship-modules>（Level B）

FACT（航行类）：
- 舰桥：失效失去航向/航速控制，直到其他舱室船员接管；大型舰多名军官，通常"黑"才算丢。
- 舵机：受损偏航，**自动修复**（无需手动修理）。
- 发动机：可燃，起火概率取决于引擎类型与来袭弹种；线性损毁三态（橙/红/黑）。
- 传动：**不会起火**，难以命中；红=临时修理可恢复最低机动。
- 烟囱：损坏后最大航速**约 -15%**。

FACT（战斗类）：
- 炮塔/炮座：炮管受损→射速降低、散布增大；严重损伤可卡死回旋；彻底摧毁无法开火。
- **扬弹机（hoist）：损坏→装填速度 -10~20%**（视损伤程度）。
- 鱼雷发射管：非线性损毁；命中仅小概率殉爆。
- 深水炸弹：黑状态才进行爆炸判定。

FACT（生存类）：
- 舱室：**高度易燃，起火概率与损伤程度无关**（官方原文）。
- 燃油舱：大型内部库很少灾难性殉爆；小型/外置油箱更易起爆；风险主要取决于弹种。
- 泵：受损降低排水速率，**不影响堵漏速度**。
- 无线电：削弱侦察报点/态势感知；雷达组件与其关联。
- 火控室（FCR）：受损→**散布增大、丢失弹道修正（距离/提前量）、目标获取变慢、多炮塔集中火控失效、炮塔转独立（本地）射击**。

与 NT 现状对照：NT 有 Engine/Boiler/Turbine/Steering/FuelTank/FireControl/Radar/Pump/Turret/Hoist/Magazine/Compartment（Level A，`PartKind`）。**未实现**：烟囱模块、传动（与轮机合并）、舵机自动修复（NT 是舵毁即冻）、扬弹机装填惩罚（NT 是炮塔组 hoist 毁→组装填 ×2.5）、无线电模块、FCR 毁→炮塔本地射击模式。

---

## 3. 爆炸/破片/超压（MDR-0005 交叉验证）

**来源**：官方 wiki《The Mechanics of High-Explosive Effect and Overpressure》，Wiki Team，2025-07-31，<https://wiki.warthunder.com/mechanics/4236-the-mechanics-of-high-explosive-effect-and-overpressure>（Level B）

FACT：
- 三成分：冲击波（超压区，无破片也可伤乘员）+ 碎裂效应（brisant，破碎障碍物）+ 破片喷洒。2.5 "Ixwa Strike" 重做取代旧 hull break。
- 破片从破口在 **约 30–45° 弧**内扩散；可继续穿透后续装甲板。
- 破口判定：HE 效应足以穿透命中处装甲（或离爆心最近的、法线朝向爆心的板）→ 形成破口，破片无阻碍喷入。
- 超压：**仅由 HE 效应触发**；APHE 需 **≥170 g TNT 当量**；仅在被击穿的舱室内结算；半径随距离衰减；HEAT 射流与 HESH 崩落不触发。
- **舰船边界**：超压只影响**露天模块**（主炮/副炮/防空炮/测距仪/射击指挥仪）并击瘫其炮组；船体内乘员与模块免疫。
- 高动能+低灵敏度延时引信可穿薄板不炸继续飞（过穿哑弹）。

与 NT 现状对照：`ExplosionSystem`/`ApplyOverpressure`（只伤 `Open` 部件）与该规则一致（Level A，`ShipDefinitions.Open` 字段）；破片锥 37.5° 在 calibration（MDR-0005）。

---

## 4. 船体分区与丧失不沉性（MDR-0007 交叉验证）

**来源**：官方 changelog《Hornet's Sting》（2.45，2025-03），<https://warthunder.com/en/game/changelog/current/1716>（Level B）

FACT：
- 船体强度机制"回归、扩展并重新设计"；新增"摧毁船体使其丧失不沉性"的击毁途径。
- **小艇/中型艇**：任一分段被摧毁即可击毁；单段累积伤害≥全部分段总耐久同样致死。
- **大型舰（护卫舰及以上）**：需摧毁数个分段后才失去不沉性并进水直至淹没；**首尾两段不计入**；**changelog 原文："现在你需要摧毁三个分段才能触发致命进程"**。
- ⚠️ **冲突点**：MDR-0007 写"中段 ≥2 段毁→丧失不沉性"，NT 代码 `Ship.CheckUnsinkability()` 实现 `destroyedMid >= 2`。官方 changelog 文本是 3 段。**MDR 记载 ≠ 官方原文**，需在 PROJECT_DESIGN 裁决（保持 2 段的战斗节奏 vs 对齐官方 3 段）。
- 段强度取决于舰级与尺寸；不会取代其他主要击毁方式。
- UI：左下伤害面板新增分段刻度；敌方在命中镜头下同刻度显示。
- 旧"不可修复破洞"（hull break）机制随本版**移除**。

其他 2.45 机制（同来源）：
- 100mm+ 弹道：全弹道阻力计算改进，中远距降速更真实、弹道更高；散布参数修正。
- 20mm+ HE 新增动能穿甲（0.001s 延迟起爆）。
- 鱼雷/水雷新增 **hydraulic shock** 对船体效果（范围取决于炸药质量与类型）。
- 修正：封闭式副炮塔不再被近爆压力波损伤（**仅完全无防护炮或仅护盾炮受爆炸伤害**）。

---

## 5. 破口/殉爆/电梯火/反鱼雷防护（2.47 Leviathans）

**来源**：官方 changelog《Leviathans》（2025-06-25），<https://warthunder.com/en/game/changelog/current/1749>（Level B）

FACT：
- 动能穿击侧舷：**只有最大口径炮弹的破口才进水**；爆炸破口尺寸取决于爆炸强度。
- 修补时间：炮弹破口 5–20 s（按大小）；**鱼雷破口最大、修补最久**。改动前大洞 1–5 s 即可补上。
- 弹药库殉爆：**威力基于剩余弹药量**（炮弹+发射药装药量），打空的库殉爆更弱。
- 弹药电梯火：从炮塔蔓延→自顶向底烧（给反应时间）；炮弹击中电梯底部→直接底部起火（几乎无反应时间）；**只适用主炮塔大型载人电梯**；每次火作用于模块都持续掷殉爆骰；Ammo Wetting 改件降低殉爆率。
- **反鱼雷防护（anti-torpedo protection）作为可摧毁实体存在**：修复了"鱼雷当量低于设计承受值时反鱼雷防护不会被摧毁"的 bug → 现在会正确被摧毁。**这是 WT 存在 TDS 模块的官方原文证据**（NT 当前 TDS 完全未实现，见 RELEASE_CHECKLIST 修正）。
- 沉没视觉效果重新启用/调整；不同弹种×舰级的爆炸/起火视觉区分。
- AI 炮手：修正过高精度，加入估算误差；机动飞机大幅降低被 AI 防空击落率。
- 127–380 mm 最大散布增加 **5–13%**。
- UI：弹药耗尽指示；**损伤面板新增火灾位置图标**。

---

## 6. 损管（2.51 Spearhead）

**来源**：官方 dev blog《The New Damage Control Mechanic for Naval》（2025-11-05），<https://warthunder.com/en/news/9795-development-the-new-damage-control-mechanic-for-naval-en>（Level B）；官方 changelog《Spearhead》 <https://warthunder.com/en/game/changelog/current/1797>（未全文抓取，MDR-0009 已核）

FACT：
- 三个 DC 流程（**灭火/模块修理/抽水**）受损后**自动同时开始**；推进系统修理需求移除（自动修）。
- 玩家预设三套方案并在受损后调整优先级。
- **DC 系数**：受舰船尺寸、乘员数、**舰船代数（generation，越新越先进）**影响；相对乘员量小船更有效；系数降低第二/第三流程的时间惩罚（幅度不大）。
- 模块修理不再可手动跳过（移除"省员不修"战术，官方明确该战术非设计意图）。
- **旧手动系统保留**：机库 Menu > Options > Naval Battle Settings 切换新旧（2025-11-07 开发者置顶确认）。
- blog 未列：乘员不足硬规则、模式切换时间（社区实测 ~4s/取消 ~3s，Level E，MDR-0009 已记录为社区数据）。

---

## 7. 瞄准与火控

**来源**：官方公告《Naval Test: New aiming system, hydrodynamics, and other changes》（2017-07-28），<https://warthunder.com/en/news/4871-naval-test-new-aiming-system-hydrodynamics-and-other-changes-en>（Level B）；论坛学院指南（Level E/D）

FACT（2017 公告，仍是现行框架基础）：
- **街机**：锁定目标→提示试射；**距离自动修正但有误差**；每次射击后虚拟乘员分析落点区间（strike zone）微调；**三发命中该区间后**以设备最高精度锁定距离，此后自动修正；换目标重新开始。水平瞄准自动按目标速度修正；垂直可选择打击区（甲板设施 vs 水线）。
- **真实**：测距员报告距离+误差；玩家**看水柱手动修正**（滚轮）；不会自动跟距离；"手动修正瞄准"指令有效性随距离衰减。
- **任何机动都显著降低射击精度——对射击方和被射击方皆然**（官方原文）。
- 命中摄像机：显示浮性（buoyancy）、需摧毁的隔舱数、需瘫痪的乘员百分比。
- 街机加成：引擎推力/舵机扭矩/鱼雷速度按舰种加成。
- 副武器手动指定目标指令（小口径/防空）。

2025 补充（Hornet's Sting，Level B）：AB 瞄准改为自动提前量（炮手自行测算并取提前量），禁用弹着点标记与火控进度条。

社区补充（Level D/E）：测距员自动给出锁定目标距离但玩家须手动设定火炮射距；垂直鼠标移动调距离（视角副作用）；"Ranging shot" 可绑定按键。

与 NT 现状对照：NT 无玩家瞄准系统（悬停自动交战，Level A）；`FireControl.FcsSolver` 存在但前端未用。此节是 Phase 05 战术 UI 的直接设计输入。

---

## 8. 鱼雷

**来源**：官方 wiki《Mastering The Art Of Torpedo Bombing》（Level B 指南）、《Torpedoes》分类页（Level B）；datamine（MDR-0012 已核，Type 93 blk 全参数）

FACT：
- 空投鱼雷可**设置运行深度**：对驱逐舰/巡洋舰 1 m、对战列舰类 4 m（wiki 指南原文）。
- ⚠️ **冲突点**：MDR-0012 写"固定运行深度（Type 93 diveDepth 1.0 不可调）"。wiki 指南说深度可设。两种解释：a) 指空投前选择投放深度（投放参数而非 blk 参数）；b) 后来版本开放了深度调节。**未确认**——需在 Phase 04 前用当前客户端 blk 复核 `diveDepth` 是否有玩家可调接口。
- 鱼雷对船体有 hydraulic shock 通道（2.45 起）。
- 鱼雷破口最大、修补最久（2.47 changelog）。

---

## 9. 防空与飞机对舰

**来源**：2.47 changelog（AI 炮手误差）、MDR-0013/0014（datamine 与 destroy_rules wiki，已核）

FACT：
- AI 防空精度已削弱；机动飞机大概率突破防空火力（2.47）。
- VT（无线电近炸）自 2017 起在海军弹种中存在（Fletcher 主炮 VT，2017 公告）。
- 飞机对舰：炸弹/鱼雷/火箭均可；伤害走既有爆炸/破片/hydroShock 通道（MDR-0012/0005 已核）。
- 20mm+ HE 有动能穿甲（2.45）。

---

## 10. 击杀判定与结算（海军侧）

**来源**：官方 destroy_rules wiki（MDR-0013 已核，Level B）；论坛（Level E）

FACT：
- 舰船击毁通道（综合 2.45/2.47/模块页）：乘员 0%、自沉阈值、浮力损失、丧失不沉性、倾覆、弹药库殉爆。
- 飞机的 Severe Damage/Finished Off 分层见 MDR-0013；海战侧击沉/伤害奖励在 2.45 中对 AI 与玩家一致化。

---

## 11. 表现层与 UI（部分，其余待 UX 研究线补全）

**来源**：2.45/2.47 changelog（Level B）、社区（Level D/E）

FACT（官方）：
- 命中摄像机显示：浮性、需摧毁隔舱数、需瘫痪乘员百分比（2017 起）。
- 伤害面板（左下）：乘组指示+分段刻度（2.45）；火灾位置图标（2.47）。
- 弹药耗尽指示（2.47）。
- 沉没视觉（下沉姿态/气泡）2.47 重新启用调整。
- x-ray 视图海军与陆战共用（键位绑定），显示舱室/舰员/模块（社区确认，bug 单佐证；Level D）。

未确认（待 UX 线补）：小地图信息范围（敌我全显？鱼雷标线？）、镜头模式全集、海浪技术级别、HUD 元素完整清单。

---

## 12. 海战模式规则

**来源**：官方公告/论坛（Level B/D/E）

- 胜利条件：占点（多个占领点，2017 起加入）、歼灭、分数；重生消耗 spawn points（BR 制）。
- 2017 公告：街机加成（推力/舵扭矩/鱼雷速度）按舰种。
- 详细重生经济与模式规则**未深入调研**（对 Navy Thunder 单机 PvE 参考价值有限，标记"不再深入"）。

---

## 来源清单

| # | 名称 | URL | 类型 | 日期 | 等级 |
|---|---|---|---|---|---|
| W1 | Ship Crew Mechanics | https://wiki.warthunder.com/640-ship-crew-mechanics | 官方 wiki | 2024-12-10 | B |
| W2 | Ship Modules | https://wiki.warthunder.com/mechanics/5245-ship-modules | 官方 wiki | 2020-09-02 | B（早于 2.45） |
| W3 | HE Effect and Overpressure | https://wiki.warthunder.com/mechanics/4236-the-mechanics-of-high-explosive-effect-and-overpressure | 官方 wiki | 2025-07-31 | B |
| W4 | Hornet's Sting changelog | https://warthunder.com/en/game/changelog/current/1716 | 官方 changelog | 2025-03-18 | B |
| W5 | Leviathans changelog | https://warthunder.com/en/game/changelog/current/1749 | 官方 changelog | 2025-06-25 | B |
| W6 | New Damage Control Mechanic dev blog | https://warthunder.com/en/news/9795-development-the-new-damage-control-mechanic-for-naval-en | 官方 dev blog | 2025-11-05 | B |
| W7 | Naval Test: aiming + hydrodynamics | https://warthunder.com/en/news/4871-naval-test-new-aiming-system-hydrodynamics-and-other-changes-en | 官方公告 | 2017-07-28 | B |
| W8 | Spearhead changelog | https://warthunder.com/en/game/changelog/current/1797 | 官方 changelog | 2025-11-11 | B（MDR 已核，本轮未全文抓取） |
| W9 | Mastering The Art Of Torpedo Bombing | https://wiki.warthunder.com/4620-mastering-the-art-of-torpedo-bombing | 官方 wiki 指南 | — | B |
| W10 | Naval Aiming Rework 论坛帖 | https://forum.warthunder.com/t/naval-aiming-rework/289373 | 官方论坛（玩家建议帖，勿当实装） | 2025-12 | E |
| W11 | Ranging shot 学院指南 | https://forum.warthunder.com/t/war-thunder-guide-using-ranging-shots-in-naval/80501 | 官方论坛指南 | — | D |
| W12 | Detailed X-Ray dev news | https://warthunder.com/en/news/10054-development-the-new-update-in-progress-detailed-x-ray-and-new-mfd-screens-en | 官方 dev news | — | B |

## 与 MDR / 代码的冲突记录汇总

| 冲突 | 来源 A | 来源 B | 处置建议 |
|---|---|---|---|
| 大舰丧失不沉性所需毁段数 | changelog 原文"三个分段"（W4） | MDR-0007 与代码 `destroyedMid >= 2` | PROJECT_DESIGN 裁决：对齐 3 或保留 2（战斗节奏），MDR-0007 增补注记 |
| 鱼雷运行深度可调性 | wiki 指南"可设深度"（W9） | MDR-0012 "blk 固定 diveDepth" | 用当前客户端 blk 复核（Phase 04 前），可能是空投参数而非舰载雷参数 |
| 舰桥可反复修理 | wiki 舰员页（W1） | NT 代码舰桥=普通部件 | 记为 WT 特性；NT 是否采纳进 Phase 04 设计 |
| 舵机自动修复 | 模块页（W2，2020 版） | NT 舵毁即冻 | 现行版本未复核；标记待验证 |
