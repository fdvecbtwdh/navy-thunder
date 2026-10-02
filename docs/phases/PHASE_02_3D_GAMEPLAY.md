# Phase 02 — 3D Gameplay Foundation

> 状态：`[PLANNED]`
> 前置依赖：Phase 00；命中语义部分依赖 Phase 01（可部分并行：Ocean/相机/HUD 框架先行，ShipVisual 姿态与命中可视化等 01）。

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

## 10. 验收标准（PASS/FAIL）
- PASS：加载 bb_duel 后，每艘舰以 Node3D 实例存在，位置/航向每帧同步 `WorldTransform`（改 Core `Ship.WorldPosition` 后视觉位置同步变化）。
- PASS：炮塔回旋角在 3D 中可见地跟踪目标（与 `GunState.TurretHeadingDeg` 一致）。
- PASS：主炮开火/炮弹命中/起火/进水/沉没均有对应 3D 视觉与声音反馈（事件→表现映射表全绿）。
- PASS：滚轮/相机在追击与自由模式间切换，视野覆盖 3v3 全场。
- PASS：headless 冒烟测试在 CI 环境通过（零渲染退出码 0）。
- PASS：旧 2D 渲染以战术地图形式保留可用。

## 11. 完成后状态
3D 可玩骨架成立；Phase 03 换真模型不换结构；Phase 05 在 HUDLayer 上做全量 UI。
