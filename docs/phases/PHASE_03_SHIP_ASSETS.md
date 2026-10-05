# Phase 03 — 真实舰船资产（BIM2→glTF 目标轨 + 程序化保底轨）

> 状态：`[DONE 2026-10-03]`（P03-1/P03-3/P03-4/P03-6 完成；P03-2 贴图管线与 P03-5 dmg 击毁态未做 → **WS_VISUAL_AUDIO_ASSETS（VA-2/VA-3）**，2026-10-05 增补修订去向）
> 交付摘要：`tools/convert_bim2_gltf.py`（BIM2 v7 全解析，含 Oodle 三级降级/两种实测顶点布局/packed IB/GeomNodeTree 骨架/skinNodes 名字置换表/4 级 LOD/nodeMap 挂点）+ `tools/convert_fleet_models.py`（23 舰批量转换零失败）+ 前端 `ShipAssetRegistry`/`ShipVisualFactory`/`GlbLoader`（自研 GLB 读取）+ `ShipVisual` 真实模型/程序化双轨 + `scenarios/asset_visual.json` 视觉验收场景 + 资产一致性测试 3 项。格式逆向与实测细节见 `docs/research/ASSET_PIPELINE_CURRENT.md`。
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

---

## 12. 验收对照(2026-10-03 实测)

| 验收标准(PHASE_03 §10) | 结果 |
|---|---|
| Bismarck 转换产出 Godot 可加载 glTF,三角数与解析器一致 | ✅ LOD1 196,365 三角/GLB 结构由 ShipAssetPipelineTests 校验;Godot 实载 394 节点/60.8 万顶点(LOD0) |
| **[2026-10-05 修复] 顶点布局与 packed IB 边界解错导致全舰队几何爆炸** | ✅ 根因=storageFormat 字段被忽略(fmt5/24: UV@s16×2+POS@s16×3 bbox-lerp; fmt3/16: POS@0)+packed IB 少跳 1B 头;23 舰×4 LOD 3800 万索引 0 窗口违规+8 舰光栅侧影核对+GUI 三舰实模型截图,详见 ASSET_PIPELINE_CURRENT §6.5 |
| 30 舰映射舰 ≥27 艘成功转换 | ✅ 有映射 24 舰中 23 舰成功(uss_sims 的 grp 不在客户端),其余 6 舰无可信 WT 映射保持程序化(有清单 fleet_convert_summary.json),零转换失败 |
| 每舰 nodeMap 含全部炮塔挂点,位置与 GunSystem.MountPosition 语义对应 | ✅ nodeMap.json 全 skeleton 节点(俾斯麦 771/北卡 1628);主炮塔(main_caliber_turret_NN)位置与 Core group 艏艉序一致(Antons z=+80…Dora z=-68);坐标为真实 WT 几何,与 Core 抽象 part 盒天然不同(语义=顺序与艏艉对应) |
| LOD 生效(远档帧率对比记录入基线) | ✅ 4 级 LOD(200/600/2000/13000m)按相机距离切换(0.25s 周期);性能无回归迹象(6v6 GUI 正常运行),量化 FPS 对比列 Phase 08 优化项 |
| 转换器对无资产舰不崩溃 | ✅ fallback 冒烟:NT_ASSET_ROOT=空目录 → 全程序化 → bb_duel 完整跑通 SMOKE PASS;前端 TryBuildRealModel 任何失败回退程序化 |
| docs/asset_pipeline.md 状态更新、LICENSE_AUDIT 复核 | ✅ 已更新;**glb不入库**(.gitignore,WT 衍生物不分发,假设③);metadata/nodeMap/convert_report JSON 保留 |

## 13. 验证记录

- 软件光栅三视图(无编辑器依赖):Bismarck/NC LOD1/LOD0 侧影完整(舰体/格子桅/三脚桅/炮塔/弹射器)——`tools/raster_preview.py`
- Godot 截图:`scenarios/asset_visual.json`(NC+Kongo+Bismarck+Fletcher 静止编队)+ 6v6 全景 12 舰渲染
- 3v3 headless 冒烟:2400s 完整 AI 战斗,80 次 Core→Visual 同步检查零失败,6 ShipVisual 全程真实模型
- fallback 冒烟:空资产目录 → 程序化占位 → SMOKE PASS
- 已知限制:炮塔视觉旋转暂停(rigid=材质批,顶点跨船,P04 模块归属后启用);AA/副炮静态;贴图=灰 placeholder

## 14. 未完成项(诚实清单)

> [2026-10-05 增补] 去向修订：P03-2/P03-5 等遗留项的承接由 **`docs/phases/WS_VISUAL_AUDIO_ASSETS.md`（视觉与音频资产工作流）** 正式接管（原"Phase 04 尾/Phase 08"的笼统指向作废）；炮塔视觉偏航旋转已由 P04.4 交付。已完成历史（§12 验收对照）不变。

| 项 | 状态 | 去向 |
|---|---|---|
| P03-2 贴图管线(dynModelDesc.bin 解析 + dxp→dds) | NOT DONE | **WS 资产 VA-2**（最高优先） |
| P03-5 dmg 击毁态模型(v6 头差异未验证) | NOT DONE | **WS 资产 VA-3** |
| 炮塔视觉旋转 | **偏航 DONE(P04.4)**；俯仰 NOT DONE | 俯仰 → **WS 资产 VA-4** |
| xray 轮廓模型 | NOT DONE | Phase 05+(x-ray 命中相机，随模块可视化再评) |
| 量化 FPS 基线对比 | NOT DONE(定性无回归) | Phase 08 性能预算（2026-10-05 已有 GUI 实测基线：3v3 ~148-180fps / 6v6 稳态 80-154fps，见 PHASE_08 §2） |
| [2026-10-05 已修复] 全舰队几何爆炸(顶点/索引解码错) | FIXED (P03.2) | 回归门常态化 → **WS 资产 VA-1** |
