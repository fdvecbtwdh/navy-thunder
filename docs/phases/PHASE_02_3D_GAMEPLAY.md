# Phase 02 — 3D Gameplay Foundation

> 状态：`[DONE]`（2026-10-03 实现骨架与表现层全链路；视觉资产=程序化占位，真实模型归 Phase 03。验收记录见文末 §12）
> 前置依赖：Phase 00；命中语义部分依赖 Phase 01（可部分并行：Ocean/相机/HUD 框架先行，ShipVisual 姿态与命中可视化等 01）。

## 0. 实施记录（2026-10-03）

**场景结构**（`frontend/Godot/`，`Main.tscn` 根=Node3D+`BattleScene3D.cs`）：
```
Main (Node3D, BattleScene3D)         模拟驱动/事件游标/输入命令/冒烟 harness
├─ Environment (WorldEnvironment)    程序化天空+环境光
├─ Sun (DirectionalLight3D)
├─ Ocean (MeshInstance3D)            160km 平面，视觉 only
├─ Islands ×3                        装饰占位
├─ Ships (Node3D) → ShipVisual ×N    数据驱动占位船体（艏楔/上层建筑/烟囱=Boiler 部件位/
│  │                                 炮塔=GunVisualState 回旋/艏标+左右舷灯/火/烟/沉没姿态）
├─ Aircraft (Node3D) → AircraftVisual
├─ Projectiles (ProjectileTracers)   弹丸+鱼雷轨迹池化（只读 Ballistics/DebugTorpedoes）
├─ FxLayer (Node3D)                  96 池化 TTL 特效：炮口焰/命中/水花/爆炸/殉爆/沉没
├─ CameraRig (Camera3D)              追击+自由（Q/E 轨道、滚轮、F 切换、PgUp/Dn）
├─ Hud (HudPanel, CanvasLayer)       文本 HUD+教程卡+战报 overlay（R2.6 行为移植）
└─ TacticalMap (CanvasLayer)         旧 2D 战场图降级为只读战术地图（M 切换）
```

**坐标契约（已测试）**：Core→Godot **恒等映射**（X/Y/Z 一一对应）；ShipVisual 局部 +Z=舰艏 与 Core 舰体局部帧一致；`node.Rotation.Y = HeadingDeg·π/180`。验证=headless 冒烟每 30 模拟秒同步断言（位置 0.05m/航向 0.01rad）+ 四航向编队截图（`docs/screenshots/phase02/heading_check_formation.png`：h90 与 h270 横舷长条方向相反=无镜像直接证据）。

**Core 侧配套改动**（均为表现层消费所需，零战斗语义变化）：
- `GunSystem.GunVisualState` 只读访问器（炮塔局部朝向/炮位）；
- **发现并修复既有 bug**：`HandControlToPlayer` 只从 `NavalAi.Ships` 移除玩家舰，但 AI 更新遍历 `_minds`——玩家舰一直被 AI 抢舵（实测 30s 内 0 舵令下航向漂移 30°+）。新增 `ReleaseMind` 并在交接时调用；回归测试 `PlayerHandoverTests`（交接后 120s 航向漂移 <1°）。

**冒烟体系**（`NT_FRONTEND_SMOKE=1`）：结构断言（Battle3D/Ocean/Environment/Ships×N/Camera3D/FX/Projectiles/HUD/TacticalMap 全存在）→ 整场 AI 战斗推进 → 每 30s Core→Visual 同步断言 → 退出码=失败数。结果：bb_duel/heading_check/fleet_3v3/fleet_battle_6v6 全 PASS。

**验证截图**（`docs/screenshots/phase02/`）：四航向编队+特写×4、1v1/3v3/6v6 战斗中。窗口模式验证辅助环境变量：`NT_FRONTEND_SCENARIO/SHIP/CAMDIST/CAMYAW/CAMFOCUS/SHOTTIME/SHOT`。

**已知表现层事实**：窗口模式重场景帧率约 1×（12 舰+粒子，未优化——性能门禁归 Phase 08）；战术地图默认隐藏；音效（合成）已接事件流但音色为占位。

## 1. 阶段目标
把 Godot 前端从 2D 俯视占位（`BattleView` Node2D）升级为 **Godot 4 3D 场景**：可见的海面/舰船/炮塔/弹道/特效 + 追击相机 + 图形化 HUD 骨架；现有 2D 渲染降级为战术地图资产复用。**本阶段用程序化船体（保底轨），不依赖 WT 资产。**

## 2. 当前基础（Level A 事实）
- 前端约 1,275 行：`BattleView`（694 行，2D `_Draw()`）、`MenuView`、`AudioManager`（`PlayGun/PlayExplosion` 已实现**未接线**）、`AppEnv`（user://+崩溃日志）、`L10n`。
- 事件消费已有游标模式（`_fxCursor`）；音效 API 就绪；`hull.obj`（41 站线程序化船体+甲板轮廓）32 舰在 `assets/models/`。
- 工程配置：`Godot.NET.Sdk/4.7.2`、net8.0、主场景 Menu.tscn；前端不在 slnx（独立 csproj）。

## 3. 外部参考与来源
- WT 表现层基线（Level B/D）：`WAR_THUNDER_NAVAL_RESEARCH.md` §11（hit cam/伤害面板/沉没视觉）、`NAVAL_COMBAT_REFERENCE.md` §2（镜头/反馈）。
- NT 结构设计：`PROJECT_DESIGN.md` §8（节点树/同步机制/HUD 清单）。

## 4. 不做什么
- 不做玩家瞄准流程与损管 UI（Phase 05）；不接 WT 资产（Phase 03）；不做小地图（Phase 05 战术地图）；不追求 60fps 达标（那是 Phase 08 门禁，本阶段只立基准）。

## 5. 技术设计

### 5.1 场景结构（PROJECT_DESIGN §8.1 落地）
```
Main3D.tscn
├─ Battle3D (Node3D, 脚本 BattleScene3D.cs)
│  ├─ WorldEnvironment + DirectionalLight3D（海天光照）
│  ├─ Ocean（着色器海面：法线波+近岸渐变；与模拟零耦合）
│  ├─ Islands（程序化岛屿占位）
│  ├─ Ships/ShipVisual(Node3D)×N
│  │   ├─ HullMesh（MeshInstance3D：程序化船体 v2——由 hull 站线+上层建筑体块生成 glTF/OBJ）
│  │   ├─ Turrets/TurretVisual×N（回旋跟随 GunState.TurretHeadingDeg）
│  │   ├─ Wake（导航速度驱动）
│  │   └─ FireFX/SmokeFX（事件驱动 GPUParticles）
│  ├─ Projectiles（曳光/弹丸 Instancing 采样 Ballistics.Projectiles）
│  ├─ FXLayer（爆炸/水花/炮口焰——事件游标消费）
│  ├─ CameraRig（追击/自由两档，滚轮距离）
│  └─ HUDLayer（CanvasLayer：仪表骨架）
└─ TacticalMap（现 BattleView 改造保留：小地图级呈现）
```

### 5.2 同步机制（PROJECT_DESIGN §8.2 落地）
- `_Process`: 累积 delta → `World.Step()`（固定 0.02s）→ 读状态渲染。
- 事件：`Events.After(cursor)` 唯一消费方式（推广现模式）；FX 生命周期按事件类型表驱动。
- 舰船姿态：`ShipVisual.Transform = shipTransform`（Phase 01 的 `WorldTransform` 直接驱动；01 未合并前用旧语义+视觉倾斜过渡）。
- 沉没：读 `KillState`/`DestroyedTime`/`ListDeg` → 下沉+倾侧表现插值（对照 WT 2.47 沉没视觉重启用）。

### 5.3 类与模块
| 单元 | 类型 | 说明 |
|---|---|---|
| `BattleScene3D` | 新 | 顶栈：runner 接线、系统节点管理 |
| `ShipVisual` | 新 | 姿态/炮塔/FX 挂点；从 ShipDefinition 实例化 |
| `HullMeshBuilder`（前端） | 新 | 站线 OBJ → ArrayMesh + 上层建筑体块（数据源=Parts 盒） |
| `OceanShader` | 新 | Godot shader material |
| `FxLibrary` | 新 | 事件类型→粒子模板映射表 |
| `CameraRig` | 新 | 追击/自由 |
| `Hud3D` | 新 | 仪表骨架（速度/舵/装填/分段血条图形化起步） |
| `AudioHook` | 新 | **把 PlayGun/PlayExplosion 接进 FXLayer 事件流**（R3.3 遗留欠账） |
| `TacticalMap` | 改 | 现 BattleView 降级复用 |

## 6. 文件修改范围
`frontend/Godot/`（新增 3D 场景与类、改 BattleView 为 TacticalMap、AudioManager 接线）；`tools/generate_hull_obj.py`（升级为 v2 含上层建筑，可选）；`assets/models/`（重新生成）。

## 7. 依赖 / 并行
- 依赖 Phase 00；部分依赖 Phase 01（姿态/命中可视化）。
- 并行：BIM2 逆向研究（Phase 03 前置侦察）；W3 数据扩录。

## 8. 风险
| 风险 | 对策 |
|---|---|
| Godot 3D 性能（6v6+特效） | Instancing+粒子预算；Phase 08 才设 60fps 门禁，本阶段先立 profile 基线 |
| 程序化船体观感不足 | 接受——本阶段目标是"3D 可读战场"；观感升级归 Phase 03 |
| 2D→3D 重写破坏现有 smoke | `NT_FRONTEND_SMOKE` 语义保留（headless 跑通战斗退出），新增 3D 节点存在性断言 |

## 9. 测试
- Godot headless 冒烟（现有模式扩展）：菜单→战斗→战报→退出，断言 3D 节点树存在。
- `AudioHook`：发射/爆炸事件触发对应音效（headless 下记录调用计数）。
- 帧率基准：记录 6v6 headless 渲染帧率基线（不设门禁）。

## 10. 验收标准（PASS/FAIL）— 2026-10-03 核验

- PASS：加载 bb_duel 后，每艘舰以 Node3D 实例存在，位置/航向每帧同步 `WorldTransform`（冒烟同步断言 0.05m/0.01rad 全绿；改 Core 状态后视觉同步变化由同步检查机制保证）。
- PASS：炮塔回旋角在 3D 中可见地跟踪目标（`GunVisualState.TurretHeadingDeg` 驱动，局部帧叠加）。
- PASS：主炮开火/炮弹命中/起火/进水/沉没均有对应 3D 视觉与声音反馈（EventLog 事件→FX/音频映射全接：GunFired/ShellDetonation/ProjectileArmorImpact/MagazineDetonation/ShipDestroyed + FireSystem 轮询）。
- PASS：滚轮/相机在追击与自由模式间切换（Q/E 轨道、F 自由、滚轮距离）。
- PASS：headless 冒烟测试通过（bb_duel/heading_check/fleet_3v3 全 PASS，6v6 见 §12 收尾记录）。
- PASS：旧 2D 渲染以战术地图形式保留可用（M 键切换，数据同源 Core）。

## 11. 完成后状态
3D 可玩骨架成立；Phase 03 换真模型不换结构；Phase 05 在 HUDLayer 上做全量 UI。

## 12. 6v6 冒烟收尾记录
6v6 headless 冒烟首跑因本地脚本超时被截断（非测试失败）；随后发现前端 Debug 构建下 6v6 模拟仅 ~0.3× 实时（QUICK 预算被模拟速度封顶），改用 Release 构建重跑：**完整 2400s 战斗、12 ShipVisual 全程同步零失败、退出码 0 = PASS**（日志结论 "P02 SMOKE PASS (structure + core→visual sync)"，2026-10-03）。经验：headless 冒烟性能验证一律用 Release 构建。
