# 资产系统现状审计（Phase 03 开工盘点）

> 日期：2026-10-03（Phase 03 第一阶段产出）。回答 PHASE_03 §5 的五个审计问题，并记录审计时的真实代码状态。

## 0. 审计时的组件清单

| 组件 | 状态（审计时） | 位置 |
|---|---|---|
| ShipVisual | 占位程序化船体（Phase 02），无资产接口实现（只有注释预留） | `frontend/Godot/ShipVisual.cs` |
| ShipDefinition / Ship | Core 纯数据（id=如 `uss_iowa`，长/宽/吃水、parts、turretGroups） | `src/NavyThunder.Core/` |
| 舰队数据 | `generated_fleet.json` 30 舰；`wtUnitId` 字段记录 WT 来源 | `data/ships/` |
| 资产目录 | `assets/models/<id>/hull.obj`（参数化船体，旧 2D 前端用） | `assets/models/` |
| WT 提取 | GRP2 解析器 + 条目识别（`extract_ship_model.py`）、598 艘清单 | `tools/`、`assets/raw/models/model_inventory.json` |
| BIM2 解析 | **无**（Phase 03 核心工作） | — |

## 1. 舰船 ID 如何定义？

- NT id：`data/ships/generated_fleet.json` 的 `id` 字段（`uss_iowa`/`rms_bismarck`/...），Core `Ship.TargetId` 即它。
- WT 对应：`tools/generate_ships.py` 的 `WT_ID` 显式映射表（30 舰中 24 舰有 WT 单位 id；`dkm_z23`/`rn_edinburgh`/`uss_california`/`uss_new_mexico`/`uss_penelope`/`uss_pennsylvania` 无可信匹配 = None）。
- WT 单位 id → 客户端 `.grp` 文件名前缀映射：`us_→usa_`、`jp_→jap_`、`germ_→ger_`、`uk_→uk_`（实测 2026-10-03）。

## 2. 模型路径如何映射？

（Phase 03 交付后的最终形态）

```
Ship.TargetId
  → assets/models/<ship_id>/metadata.json        （数据驱动注册表条目）
      → lods: {"0": model_lod0.glb, "1": ..., "2": ..., "3": ...}   按 range_m 切换
      → node_map: nodeMap.json                    （挂点表）
      → bbox_nt_min/max                           （转换时实测包围盒）
```

- 注册表扫描：`frontend/Godot/ShipAssetRegistry.cs`（env `NT_ASSET_ROOT` / exe 向上 8 级找 `assets/models`）。
- 加载：`ShipVisualFactory`（PackedScene 缓存）→ `GlbLoader`（自研 GLB 读取：GodotSharp 4.7 不给 C# 暴露 GLTFDocument，assets 也不在 res:// 内，无编辑器导入步骤——自研管线配自研读取器）。

## 3. 是否支持一个舰船多个视觉版本？

- **LOD**：支持，每舰最多 4 级（WT 原生 LOD0/1/2/3，range 200/600/2000/13000m），`ShipVisual.UpdateFromCore` 按相机距离每 0.25s 切换 Visible。
- **多版本（variant）**：目录结构天然支持（metadata 指路径），P03 未做 variant 选择逻辑（未来建造器需求，Phase 06）。

## 4. 是否支持缺失模型 fallback？

- 支持，两级：
  1. 注册表无条目（无 metadata.json / converted=false）→ 程序化占位；
  2. glb 加载抛异常/返回 null → 清理 LOD 节点 → 程序化占位。
- 兜底原则：**没有模型游戏必须可玩**（PHASE_03 铁律），`TryBuildRealModel()` 返回 false 即走 Phase 02 占位路径，战斗逻辑零依赖模型。

## 5. 炮塔、挂点、烟火位置如何定义？

- **炮塔（真实模型）**：glb 内 `main_caliber_turret_NN`/`turret_NN`（按舰自适应，见转换器 `_TURRET_RE`/`_MC_TURRET_RE`）保留节点变换（pivot=炮塔原点），`main_caliber_gun_NN` 挂为最近炮塔的子节点；前端 `RegisterRealTurrets` 按 z 最近贪心绑定 Core `TurretGroup`（"A"/"B"/...，副炮 "S*" P03 保持静态），距离容差 = 25% 舰长。
- **炮塔（程序化占位）**：Core part 质心放置（Phase 02 逻辑不变）。
- **烟火挂点**：Core `FireSystem` 的 burning-part 世界坐标即锚点（数据驱动），发射器挂在 ShipVisual 根上（两种模式共用）。`nodeMap.json` 保存全部 skeleton 节点的模型空间坐标（1600+ 节点/舰），为 Phase 04 模块损伤、Phase 06 建造器的挂点查询提供数据。
- **Core 无炮管俯仰数据**：模型自带静态仰角，前端不模拟（遵守 PHASE_03 §13）。

## 6. 坐标标准（NT_STANDARD）

```
+Z = Bow    +Y = Up    +X = Starboard
```

- WT 模型实测：+X=纵轴（艏向 +X）、+Y=上、+Z=横轴 → 转换 `NT = (-WT.z, +WT.y, +WT.x)`（行列式 +1，保手性）。舰艏方向由 Bismarck 上层建筑/艏柱侧影验证。
- 转换器矩阵版本：平移 `to_nt()`；旋转 `R_NT = P·R·Pᵀ`（`_mat_nt`）；glTF 节点四元数 `_quat_from_mat`。
- 单位：米（WT 原生），无缩放。

## 7. 风险与已知限制（审计结论）

1. Oodle DLL：转换器依赖本机 `daKernel-dev.dll`（ordinal 574）。探测顺序：`NT_OODLE_DLL` → `_wt_audit/dakernel/` → `%TEMP%/dk_try1.dll` → WT 客户端目录。**DLL 不入库**（LICENSE_AUDIT 假设③：用户自有客户端）。注意网络上流传的副本常有截断（`.reloc` 完整大小 2,353,664B 可作完整性判据）。
2. 贴图管线未建（P03-2 尾任务）：模型无贴图（灰 placeholder 材质），`dynModelDesc.bin` 解析与 dxp 解包待做（`wt_tools` 有 ddsx/dxp 参照实现）。
3. `*_dmg`（击毁态）/`xray` 模型：v6 头差异未验证，P03 尾任务。
4. 特效小网格（炮口焰等 40 顶点级）packed IB 越界 >2% 时整 elem 丢弃（有 warning 记录），LOD2/3 视觉无影响。
5. 首战加载：同步 GLB 解析（每舰 ~1s，PackedScene 缓存后免费）；异步加载/进度条列 Phase 08。
