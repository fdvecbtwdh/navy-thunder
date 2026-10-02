# Phase 03 — 真实舰船资产（BIM2→glTF 目标轨 + 程序化保底轨）

> 状态：`[PLANNED]`
> 前置依赖：Phase 02（3D 框架消费资产）。**可行性已被 Phase 0 研究确定（`docs/research/DAGOR_ASSET_RESEARCH.md`：BIM2 破解约 95%，工作量 M）**，本阶段从"探索"升级为"实现"。

## 1. 阶段目标
主力舰获得真实 3D 资产：`tools/convert_bim2_gltf.py` 批量把 WT .grp 转为 glTF（含材质贴图），Phase 02 的 ShipVisual 直接加载；骨架层级接入模块化损伤；程序化船体保留为无资产舰 fallback。

## 2. 当前基础（Level A/C）
- GRP2 容器完整解析（`tools/extract_ship_model.py`，598 艘清单 model_inventory.json）。
- BIM2 格式破解表（DAGOR_ASSET_RESEARCH F1-F12）：头/压缩块/MatVdata/VdataHdr/vDecl 全部已知，Bismarck 顶点解压实测成功（23.7MB 样本留档 `_wt_audit/bismarck_mvd_decompressed.bin`）。
- 开源引擎读/写两端源码可参照（22 文件已取，入 `_wt_audit/dagor_src/`）。
- 程序化船体 v1 在役（32 舰 hull.obj）。
- 材质描述 `dynModelDesc.bin` 在位；高清贴图 dxp 管线待建（ROADMAP R3.1 已盘点）。

## 3. 外部参考与来源
`docs/research/DAGOR_ASSET_RESEARCH.md`（Level B 开源引擎 + Level C 实测 + Level D 社区工具）；`docs/asset_pipeline.md`（历史盘点）；LICENSE_AUDIT（合规边界）。

## 4. 不做什么
- 不解析 *_sections 闭源树（P2）；不做碰撞 BVH 解码（P2，渲染网格已含几何）；
- 不追求全 598 艘——按 NT 舰队 30 艘映射的 WT 单位优先（`generate_ships.py` 的 WT_ID 表）；
- 不分发 WT 资产（LICENSE_AUDIT 假设③：转换器输入=用户自有客户端）。

## 5. 任务与技术设计

| # | 任务 | 设计要点 |
|---|---|---|
| P03-1 | **BIM2→glTF 转换器** | 按 F2-F7 顺序解析：头→OODLE/ZSTD 解压（主路径 ctypes daKernel-dev.dll + 降级 zstd + oo2core）→MatVdata→VdataHdr→vDecl 通道→packed IB 解码→glTF 2.0 输出（需 `pip install pygltflib` 或手写最小写出器）；CLI 批处理+清单模式 |
| P03-2 | **材质与贴图** | dynModelDesc.bin 解析（F12）→tex 路径→dxp 解包（wt-tools 有 ddsx/dxp 解码器可参照）→KTX/PNG 进 glTF；无贴图回退纯色材质 |
| P03-3 | **骨架层级接入** | GeomNodeTree 解析（F9）→rigid.nodeId→挂点表（炮塔/舰桥/雷达）→**对齐 NT PartKind**：为每舰产出 `nodeMap.json`（WT 节点名→NT 部件 id），Phase 02 的 TurretVisual 挂点从猜测变为数据 |
| P03-4 | **LOD 与降载** | BIM2 多 LOD（LODS 表）→导出 2-3 级；网格>50 万顶点的舰取中 LOD+实例化；资产校验（AABB 在数据范围/顶点数上限） |
| P03-5 | **dmg 击毁态** | `*_dmg`（v6）+dmg_skeleton 解析→击毁替换网格（Phase 02 的 SinkPose 消费）——列为本阶段尾任务 |
| P03-6 | **批量生产与回归** | 30 舰全转换；资产校验测试进 CI（文件存在性+glTF 可加载+AABB 合理）；`assets/models/<id>/model.glTF` 目录规范替代 hull.obj（保留 hull.obj 兼容） |

## 6. 文件修改范围
`tools/convert_bim2_gltf.py`（新）、`tools/extract_dxp.py`（新，可参照 wt-tools）、`assets/models/`（新产物）、`_wt_audit/dagor_src/`（参照源码入库，BSD-3 全文已随 LICENSES 登记）、`frontend/Godot/`（ShipVisual 加载 glTF+nodeMap）、`docs/asset_pipeline.md`（更新状态）。

## 7. 依赖 / 并行
- 依赖 Phase 02（消费端）。
- 并行：贴图管线（P03-2）可与转换器（P03-1）分工；P03-3 与 Phase 04 的模块对齐协商。

## 8. 风险
| 风险 | 对策 |
|---|---|
| Oodle DLL 在部分机器加载失败 | 三级降级（DLL→系统 oo2core→zstd 块直解）；失败舰回退程序化船体 |
| v7 与开源 HEAD 代差 | 以 Bismarck 实测为准；解析器带断言与错误报告（逐舰失败清单） |
| 贴图管线（dxp）工时超预期 | 无贴图纯色材质先行（几何价值已足够）；贴图独立交付 |
| 骨架 nodeMap 对不齐 NT 部件 | 人工映射 30 舰×关键挂点（炮塔/舰桥）起步；其余自动+人工抽查 |

## 9. 测试
- 单元：packed IB 解码（已知序列对照）；vDecl 通道偏移计算。
- 端到端：Bismarck 转换→Godot 加载→顶点数/包围盒与解析报告一致。
- 回归：30 舰批转换零失败清单；资产校验测试进 CI。
- 兜底：任一舰转换失败→自动回退程序化船体（警告清单）。

## 10. 验收标准（PASS/FAIL）
- PASS：`python tools/convert_bim2_gltf.py ger_battleship_bismarck` 产出可被 Godot 4.7 加载的 glTF，三角数与解析器报告一致。
- PASS：30 艘舰队映射舰 ≥27 艘成功转换（其余自动回退程序化并有失败报告）。
- PASS：每舰 `nodeMap.json` 至少包含全部炮塔挂点，Godot 内炮塔节点位置与 `GunSystem.MountPosition` 语义对应（误差 < 舰长 2%）。
- PASS：LOD 生效（远档帧率对比记录入基线）。
- PASS：转换器对无资产舰不崩溃（回退路径测试）。
- PASS：`docs/asset_pipeline.md` 状态更新为"转换器已交付"，LICENSE_AUDIT 复核（分发不含 WT 原始文件）。

## 11. 完成后状态
视觉层从占位色块升级为真实舰船；Phase 02 场景零结构改动换装；Phase 05 的 x-ray/命中相机获得数据基础（骨架层级）。
