# DagorEngine 资产格式技术调研（BIM2 dynmodel / 碰撞 BVH / 骨架）

> 研究目的：确定 WT 舰船模型→Godot 可用资产（glTF）的转换路线（Phase 03 目标轨）。
> 方法：本地二进制实测（Bismarck 条目）× 开源 DagorEngine 源码逐字段互证（GitHub raw 实取 22 文件）× 社区工具对照（Dagor-Asset-Explorer）。抓取日期 2026-10-02。
> 结论摘要先行：**BIM2 格式已破解约 95%，Python 转换器工作量 M（2-4 天）；最大依赖 Oodle 解压已有本机实测可用的解；骨架=GeomNodeTree，可直接服务 NT 模块化损伤。**

---

## 1. 格式知识现状（FACT 表，带出处）

| # | 结构 | 程度 | 关键事实 | 出处 |
|---|---|---|---|---|
| F1 | GRP2 容器 | 完整 | `GrpHeader{label,descOnlySize,fullDataSize,restFileSize}`+`GrpData{nameMap,resTable,resData}`；NT 已实现（`tools/extract_ship_model.py`） | 开源 `prog/engine/gameRes/grpData.h` 互证 |
| F2 | BIM2 头 | 完整 | `u32@0` dump 尺寸；`u32@4,8=-1,-1`（材质分离，材质在 dynModelDesc.bin）；`u32@12` vdataFullCount；`u32@16` matVdataHdrSz（高 2 位=压缩标记）；版本 主=7/dmg=6/xray=1 | 本地 Bismarck 实测 × 开源 `dynSceneRes.cpp::loadResource` 逐字段吻合 |
| F3 | 顶点压缩块 | 完整 | 块头 u32=`(compr<<30)\|size`；compr 0=NONE/1=ZSTD/2=OODLE。**实测 598 grp×1926 个 BIM2 条目 100% OODLE**（Bismarck 块@20=OODLE 4,655,258B → 解压 23,702,170B，首字节 0x8C Kraken） | `dag_btagCompr.h`；本机全量扫描+解压验证（`_wt_audit/bismarck_mvd_decompressed.bin` 23.7MB 留档） |
| F4 | 'BIM2'@60 | 观测事实 | 压缩流内特征（解压载荷不含该串），仅作条目分类，不参与解析 | 实测 |
| F5 | MatVdataHdr（320B） | 完整 | `{mat tab, gvdOfs, gvdCnt}`；Bismarck matCnt=0（分离材质）、gvdCnt=7 | `matVdataLoad.cpp` + 实测吻合 |
| F6 | VdataHdr（32B×7） | 完整 | `{vertNum; stride:8\|packedIdxSizeLo:24; idxSize:28\|packedIdxSizeHi:4; flags; vDecl{ptr,cnt}}`；Bismarck 7 块合计 ≈94 万顶点（最大块 578,381 顶点/stride 24）；索引有裸 u16 与 packed（增量编码）两种 | `dag_shaderMesh.h` + 实测；packed IB 解码参照 DAE `mesh.py` |
| F7 | 顶点通道 vDecl | 基本完整 | `CompiledShaderChannelId`（pos f32x3/uv f32·half/法线打包等） | `dag_shaderMesh.h`、`shaderMeshData.h` |
| F8 | 碰撞条目 | 完整（格式） | `label=0xACE50000\|ver`（Bismarck=3）+ btag ZSTD 块；v3=uint16 量化 SoA4 TLAS/BLAS+叶子体+physMat 池；开源加载器原生支持 v0/1/3；**叶子→三角形解码需自实现** | `collisionGameResLoad.cpp`、`daBVH/dag_quadLeafEncode.h` |
| F9 | *_skeleton | 基本完整 | =**GeomNodeTree**（DClass 0x56F81B6D）：节点{tm,wtm,父子,名字}；Bismarck 771 节点 ≈134,848B | `geomNodeTreeGameRes.cpp`；DAE `realres.py` 有完整解析器 |
| F10 | *_sections(.bat) | 部分 | 头 `0x5dff175`+树（AABB/哈希名，无明文名）；**格式闭源**（游戏侧伤害分区） | 本地实测；tree.json 无对应源码 |
| F11 | fph3/char | 完整 | fph3=u32 size+'fph3'+count+节点参数；char='chr1'+包围球 | 实测 |
| F12 | 材质描述 | 完整可得 | `content/base/res/dynModelDesc.bin`（本机在位）：v7 分离材质的 tex/matR 来源 | DAE README+本机确认；`gameResDescBin.cpp` |

**核心认知修正**：BIM2 **不是** btag 序列化的 DAG 树（此前 `docs/asset_pipeline.md` 的推测），而是 `DynamicRenderableSceneLodsResource` 的"结构体 dump+指针 patch"流，与 .dag 资源同族。读取顺序：resSize → 16B 头 → OODLE 压缩 matVdata 块（hdr+7 组 VB/IB）→ 名字表块 → LOD rigids dump（每 rigid 28B：mesh 偏移+包围球+nodeId）→ skins。

## 2. 开源代码参照清单（GaijinEntertainment/DagorEngine，BSD-3-Clause）

读路径（解析器直接对照逻辑）：`gameRes/grpData.h`、`gameResSystem.cpp`、`ioSys/dag_btagCompr.h`、`shaders/dynSceneRes.cpp`（**BIM2 顶层**）、`shaders/matVdataLoad.cpp`、`gameRes/collisionGameResLoad.cpp`、`gameRes/geomNodeTreeGameRes.cpp`。
写路径（官方规格书）：`tools/libTools/shaderResBuilder/dynSceneResSrc.cpp`（save() 逐字节布局）、`exp_dynModel.cpp`、`shaderMeshData.{h,cpp}`。
工具内核：`daKernel.cpp`（Oodle SDK 二进制本体不在开源仓）。

## 3. 核心问题结论

- **Q1 结构**：见上；未知项仅剩 'BIM2'@60 语义（不影响解析）与 v7 与开源 HEAD 的微小代差（以实测为准）。
- **Q2 工作量**：**M（约 1,000-1,300 行 Python，2-4 天）**。剩余风险：Oodle DLL 依赖（DAE 的 `daKernel-dev.dll` ctypes ordinal 574 **本机实测成功**，但需备 zstd 降级+系统 oo2core 兜底）；vDecl 特殊通道；packed IB（DAE 有现成实现）；skins（舰船以 rigid 为主，低风险）。
- **Q3 碰撞 BVH**：可行（v3 线格式开源全解，叶子解码+逆量化自实现，M）。注意开源引擎比 DAE 新（DAE issue #20 尚未支持新碰撞格式）——**以引擎源码为准**。优先级 P2（渲染网格已含几何）。
- **Q4 skeleton/sections 价值**：**高**——GeomNodeTree 771 节点=炮塔/舰桥/雷达的挂点层级，`dynmodel rigid.nodeId` 挂接 → **NT 模块化损伤可直接复用该层级**（Phase 03 的damage 对齐任务）。`*_dmg`=击毁态替换网格、`xray`=轮廓——与官方实现同构。sections 闭源，先只取 skeleton，sections 列 P2。
- **Q5 替代路线**：CDK 无舰船源模型不可作来源；程序化船体保底轨保留；Sketchfab 社区提取品仅对照参考（版权）。**推荐：自研 `tools/convert_bim2_gltf.py`（P0）+ skeleton 接损伤（P1）+ 碰撞（P2）**。

## 4. 本地资料状态备注

- `_wt_audit/dagor.tar.gz` 为截断下载（仅 _docs 140 文件，无 C++ 源码）——已由调研从 GitHub raw 重取 22 个关键源文件（暂存 Temp，建议复制入 `_wt_audit/dagor_src/`）。
- Bismarck 解压顶点样本 `_wt_audit/bismarck_mvd_decompressed.bin`（23.7MB）留作解析器开发对照。
- `D:/WarThunder/content/base/res/dynModelDesc.bin`（材质描述）确认在位。

## 5. 来源清单

| # | 来源 | 类型 | 用于 |
|---|---|---|---|
| DG1 | GaijinEntertainment/DagorEngine @main | Level B（官方开源，BSD-3） | 格式规格 |
| DG2 | 本地 Bismarck 二进制实测（GRP2 全条目） | Level C | 互证 |
| DG3 | Dagor-Asset-Explorer（quentin-dh）+ issue #1/#12/#16/#20/#23/#24 | Level D | 参照实现/风险清单 |
| DG4 | Gredwitch/Dagor-Asset-Explorer-Tools（Blender 导入器） | Level D | 验证基准 |
| DG5 | WT CDK wiki（creation_model / Blender 导出） | Level B | 替代路线排除 |
| DG6 | ZenHAX 15887 / ResHax 8855 / 社区提取教程与 Sketchfab 合集 | Level E | 先例佐证 |
