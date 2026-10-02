# Phase 05 — 战术 UI / 玩家体验

> 状态：`[PLANNED]`
> 前置依赖：Phase 02（3D HUD 骨架）、Phase 04（机制面）。

## 1. 阶段目标
新玩家**无文档**完成一场战斗：瞄准→开火→观察→损管→终局，全部有 UI 支撑；玩家命令对象化收编。

## 2. 当前基础（Level A）
文本 HUD（航速/舵/装填/分段血条+FIRE/FLOOD 标记）；悬停自动交战；R 弹种；教程卡文案与损管实现不符；`FireControl.FcsSolver` 在库未接；`DamageControlSystem.Mode/Priority` 后端就绪无 UI；UserSettings 无 AmbientVolume（硬编码 0.6）；无暂停/小地图/命中反馈图标。

## 3. 外部参考
`WAR_THUNDER_NAVAL_RESEARCH.md` §7/§11（瞄准两模式/HUD/hit cam，Level B）+ `NAVAL_COMBAT_REFERENCE.md` §2/§3（循环与 UX 形状，Level B/D/E）。

## 4. 不做什么
舰桥第一人称视角（后续评估）；多人相关 UI；设置项不求全（图像质量分级 Phase 08）。

## 5. 任务与技术设计

| # | 任务 | 设计要点 |
|---|---|---|
| P05-1 | **命令对象化** | `GameplayCommand`（Helm/GunOrder/ShellSelect/DcPriority/TargetAssign/Pause）；`BattleRunner.Submit(command)`；`ApplyHelm` 直写字段收编进命令；AI 产出同构（PROJECT_DESIGN §11） |
| P05-2 | **玩家瞄准** | 两档火控（MDR-0014 裁决落地）：自动档=锁定目标→FcsSolver 提前量指示→试射修正循环（对齐 W7 AB：3 发锁区间）；手动档=方向+距离滚轮手调；换目标重置修正 |
| P05-3 | **图形 HUD** | 航速/舵角仪表、装填环、弹种、目标信息卡（类型/距离/航速） |
| P05-4 | **图形损伤面板** | 船体分段图（分段刻度+火/进水图标，对齐 W4/W5）；模块状态列表（对照 x-ray 数据面） |
| P05-5 | **损管面板** | 三流程状态+优先级预设（接线 `DamageControlSystem`）+自动/手动开关（对齐 W6）；修教程卡文案 |
| P05-6 | **命中反馈图标** | 击穿/跳弹/过穿/未穿/水柱 五态（数据源 `ProjectileArmorImpact.Outcome`）；hit cam 雏形（命中点模块快照） |
| P05-7 | **战术地图/小地图** | 现 2D BattleView 改造：敌我位置/航向/占位目标；全可见原则（NAVAL_COMBAT §2） |
| P05-8 | **暂停/退出** | ESC 菜单（继续/重开/回主菜单/退出） |
| P05-9 | **设置补全** | AmbientVolume 字段+滑条（废 0.6 硬编码）；键位重绑定（最小集）；画质档（Phase 08 细化） |
| P05-10 | **本地化补全** | 全部新 UI 双语；长文案（教程/战报）zh+en |

## 6. 文件修改范围
`frontend/Godot/`（大量新增 UI 类+改造）、`src/NavyThunder.Core/FireControl/`（FcsSolver 面向玩家的解算接口）、`src/NavyThunder.Data/BattleRunner.cs`（命令入口）、`tests/`。

## 7. 依赖 / 并行
- 依赖 Phase 02/04；P05-7 可与 04 并行。

## 8. 风险
| 风险 | 对策 |
|---|---|
| 命令对象化破坏现输入 | 分两步：先加命令层并行双轨，再删直写路径（测试锚定行为不变） |
| UI 工作量膨胀 | 每个 PASS 小步交付；先键盘可用再打磨观感 |
| 试射修正循环与 AI 行为冲突 | AI 炮手=自动档实现复用同一循环（误差参数不同） |

## 9. 测试
- 命令层：同命令序列（脚本化输入）headless 重放结果确定。
- 瞄准：自动档 3 发修正后散布中心对准目标（仿真断言）；手动档滚轮调距生效。
- HUD 映射表：Simulation 状态→HUD 元素映射测试（状态驱动，不测像素）。
- 损管：优先级切换改变修理顺序（对照 `DamageControlSystem` 语义）。

## 10. 验收标准（PASS/FAIL）
- PASS：新玩家不看文档，从菜单到战报完成一场战斗（流程走查清单勾完）。
- PASS：自动档下对 8km 机动目标连续齐射出现水柱跨射并逐步收敛（场景回放可见）。
- PASS：损管面板操作真实改变修理顺序与结果。
- PASS：五种命中反馈图标与事件类型一一对应（映射测试全绿）。
- PASS：暂停菜单可用；设置含 Ambient 音量且持久化。
- PASS：教程卡文案与实现一致（损管可操作）。

## 11. 完成后状态
垂直切片级体验成立；Phase 06 建造器 UI 可复用本轮 UI 框架。
