# Phase 05 — 战术 UI / 玩家体验

> 状态：`[MOSTLY DONE 2026-10-05]`（P05-1 命令对象化、P05-2 两档瞄准+试射修正、P05-3 图形 HUD、
> P05-4 图形损伤面板、P05-5 损管面板+教程文案修正、P05-6 五态命中反馈、P05-7 战术地图标记、
> P05-8 暂停菜单、P05-9 设置补全（Ambient 音量+键位重绑定）、P05-10 本地化补全 = DONE。
> 遗留：流程走查清单（§10 第 1 条 PASS）需人工操作一遍，自动断言已覆盖其余各项；画质档归 Phase 08）
> 前置依赖：Phase 02（3D HUD 骨架）、Phase 04（机制面）。

## 1. 阶段目标
新玩家**无文档**完成一场战斗：瞄准→开火→观察→损管→终局，全部有 UI 支撑；玩家命令对象化收编。

## 2. 当前基础（Level A，开工时）
文本 HUD（航速/舵/装填/分段血条+FIRE/FLOOD 标记）；悬停自动交战；R 弹种；教程卡文案与损管实现不符；`FireControl.FcsSolver` 在库未接；`DamageControlSystem.Mode/Priority` 后端就绪无 UI；UserSettings 无 AmbientVolume（硬编码 0.6）；无暂停/小地图/命中反馈图标。

## 3. 外部参考
`WAR_THUNDER_NAVAL_RESEARCH.md` §7/§11（瞄准两模式/HUD/hit cam，Level B）+ `NAVAL_COMBAT_REFERENCE.md` §2/§3（循环与 UX 形状，Level B/D/E）。

## 4. 不做什么
舰桥第一人称视角（后续评估）；多人相关 UI；设置项不求全（图像质量分级 Phase 08）。

## 5. 任务实现记录（2026-10-05）

| # | 任务 | 实现要点 | 落点 |
|---|---|---|---|
| P05-1 | **命令对象化** | `GameplayCommand` 系列（Helm/GunEngage/GunManualAim/GunCeaseFire/ShellSelect/DcOrder/TargetAssign）；`BattleRunner.Submit` 唯一入口；前端 `ApplyHelm/ApplyShellToggle/ApplyFireControl` 直写路径全部收编（`ApplyHelm` 受控例外已删除）；命令定位到 live 舰只的解析在 BattleRunner 内（lambdas 属于 Core 侧） | `src/NavyThunder.Core/Commands/GameplayCommand.cs`、`BattleRunner.cs` |
| P05-2 | **玩家瞄准两档** | G 切换。自动档=锁定目标+全 battery Engage，**试射修正**（W7 AB 带宽）：首齐喷带 4% 光学测距偏置，每齐喷 ×0.35 衰减，~3 齐喷收敛；换目标重置；手动 laying 不进修正环。手动档=光标海平面落点+滚轮 ±100m 调距（相机缩放挂起）；T 锁定（悬停丢失时保持交战）；提前量指示器（FcsSolver 只读表现层解算） | `GunSystem`（SpotState/SpotOf 诊断访问器）、`BattleScene3D` |
| P05-3 | **图形 HUD** | HelmGauge（节/航向/油门条/舵角标记/舰员条）、ReloadRing（装填环+弹种+瞄准模式）、TargetCard（目标 id/距离/航速/航向）、提示行 | `frontend/Godot/HudWidgets.cs` |
| P05-4 | **图形损伤面板** | 分段图（艏/中/艉 HP 色块+火/进水圆点+摧毁叉）+模块状态列表（按 PartKind 分组，红=有毁伤、黄=受损、绿=完好，本地化名） | `frontend/Godot/DamagePanel.cs` |
| P05-5 | **损管面板** | 自动/手动模式钮、三优先级预设、手动单流程选择、逐流程活动点+破口计数；全部经 `DcOrderCommand` 提交；教程卡文案改为与实现一致（损管自动+右下面板可调） | `frontend/Godot/DcPanel.cs` |
| P05-6 | **命中反馈五态** | 击穿◆/跳弹↗/过穿◇/未穿✕/水柱≈（数据源 `ProjectileArmorImpact.Outcome`+`FuzeTriggered`，映射纯函数）；hit cam 快照（最新命中 shell→plate 厚度行）；冒烟含映射自检 | `HudWidgets.HitFeedback` |
| P05-7 | **战术地图** | 玩家白环+锁定目标橙环+连线；标签去 "ship:" 前缀+聚簇防重叠错位（12px 行步进） | `TacticalMap.cs` |
| P05-8 | **暂停/退出** | ESC 菜单（继续/重开/回主菜单/退出）；暂停=表现层停步进（确定性 Core 无暂停概念）；战报显示时忽略 ESC | `frontend/Godot/PauseMenu.cs` |
| P05-9 | **设置补全** | `UserSettings.AmbientVolume`（废 0.6 硬编码）+滑条；键位重绑定最小集（10 动作，点击捕获，settings.json 持久化） | `KeyBinds.cs`、`AppEnv.cs`、`MenuView.cs` |
| P05-10 | **本地化补全** | 全部新 UI 双语（损管/暂停/键位/命中/教程/战报统计/沉没原因）；战报舰名改 DisplayName（弃内部 TargetId） | `L10n.cs` |

**顺手修复（视觉验收发现）**：`_hud.Bind()` 自 Phase 02 3D 化起从未被调用（旧文本 HUD 一直在空转）——
新面板依赖 Bind 后缺陷显形；战报层遮挡（显示战报时隐藏 gameplay HUD）；战报/地图标签暴露内部 id；
地图标签聚簇重叠。视觉验收（visual judge 三截图）后修到全 PASS。

## 6. 文件修改范围
`frontend/Godot/`（HudWidgets/DamagePanel/DcPanel/PauseMenu/KeyBinds 新增；BattleScene3D/HudPanel/MenuView/TacticalMap/CameraRig/L10n/AppEnv 改造）、`src/NavyThunder.Core/Commands/`（新增）、`src/NavyThunder.Core/Ships/GunSystem.cs`（试射修正）、`src/NavyThunder.Data/BattleRunner.cs`（Submit/EngageGunsOn）、`tests/`（Phase05CommandTests 11 项 + BucketCoverageTests 审计）。

## 7. 依赖 / 并行
- 依赖 Phase 02/04；P05-7 可与 04 并行。

## 8. 风险
| 风险 | 对策 | 结果 |
|---|---|---|
| 命令对象化破坏现输入 | 双轨后删直写（测试锚定行为） | ✅ 单步直切，11 项命令测试锚定 |
| UI 工作量膨胀 | 每个 PASS 小步交付 | ✅ |
| 试射修正与 AI 行为冲突 | AI 炮手=自动档同一循环 | ✅ 对称（同一 GunSystem 路径） |

## 9. 测试
- `Phase05CommandTests`（Bucket=Integration，11 项）：Helm 设置+钳制；无玩家舰 no-op；Engage 建序/停火撤序；ManualAim 落点；弹种切换；目标锁存清；损管模式/优先级/手动流程生效；**手动损管流程改变修理结果**（修破口 vs 排水对照）；**试射修正 3 齐喷收敛**（偏差单调缩小）；手动 laying 不进修正环；**脚本化命令序列确定性重放**（240s 切片双跑逐位一致）。
- `BucketCoverageTests`（Fast）：全测试集 Bucket 标签审计（tests/README §1 契约：六档白名单，未声明即红）。
- Godot 冒烟结构断言扩到 PauseMenu+命中反馈映射自检；bb_duel 3600s SMOKE PASS（同步检查零失败）。

## 10. 验收标准（PASS/FAIL）
- PASS：自动档下对目标连续齐喷出现跨射并逐步收敛（试射修正测试断言偏差单调缩小→场景可复现）。
- PASS：损管面板操作真实改变修理顺序与结果（对照测试）。
- PASS：五种命中反馈图标与事件类型一一对应（映射自检+分类纯函数）。
- PASS：暂停菜单可用；设置含 Ambient 音量且持久化（键位重绑定持久化同路径）。
- PASS：教程卡文案与实现一致（损管可操作，文案改写）。
- **PENDING（人工）**：新玩家不看文档完成一场战斗的流程走查清单——自动断言已覆盖各单项，整体走查留待下一阶段人工验证。

## 11. 完成后状态
垂直切片级体验成立；Phase 06 建造器 UI 可复用本轮 UI 框架（PanelContainer/HUD 组件风格 + KeyBinds + L10n）。
