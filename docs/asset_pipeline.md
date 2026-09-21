# WT 资产提取管线笔记（R3.1）

状态：GRP2 容器完整解析；渲染网格（BIM2）反序列化待实现。

## 模型在哪里

- 舰船 3D 模型**不在 vromfs 里**（vromfs 只有游戏参数 blk：`gamedata/units/ships/*.blk`，已由 extract_ship_units.py 提取）。
- 模型在本地客户端 `content/base/res/ships/*.grp`（598 个包，Dagor GRP2 格式，Bismarck 10.6MB）。
- 本机客户端：`D:/WarThunder`。

## GRP2 容器（已完整逆向并实现于 tools/extract_ship_model.py）

```
header (16B): u32 'GRP2' | u32 descOnlySize(912) | u32 fullDataSize(920) | u32 restFileSize
directory [16, 16+912):
  [16]  u32 名称偏移表位置 (432)
  [20]  u32 条目数 (13)
  [64..432)               z-string 条目名（表内存绝对偏移，首个=64）
  [432..484)              u32[13] 名称偏移表
  [484..628)              12B 记录 x12: u32 hash | u32 绝对数据偏移 | u32 序号
  [628..912)              哈希查找表 (hash, idx<<16|idx)
  [912..920)              尾部: u32 data_base(928), u32 4
data: [16+920 .. EOF)，条目按绝对偏移取用
```

注意：第 13 个条目名（main_ship_animtree）无数据记录（hash/offset=0），处理时跳过。

## 条目格式识别（以 Bismarck 为例）

| 条目 | 格式 | 大小 |
|---|---|---|
| *.sections | .bat 伤害分区树（未解析） | 7.5K |
| *.char | 'chr1' 字符定义 | 32B |
| *_skeleton | u32 数量 + 骨矩阵浮点（名字非明文） | 135K |
| **（主模型）** | **'BIM2' dynmodel 容器**（头部: u32 136, -1,-1, 版本 7, GUID, …'BIM2'@60） | 4.7MB |
| *_collision | u32 0xace50003 + **zstd** 帧 → 量化 BVH（int16 量化坐标+哨兵，非裸三角） | 209K→421K |
| *_dmg | 'BIM2' 损伤模型 | 4.5MB |
| *fastphys | 'fph3' 节点物理参数 | 2.8K |

## BIM2（下一步工作）

- 主渲染网格在 BIM2 dynmodel 容器内，顶点流压缩存储（容器内嵌多个压缩块）。
- 解析它需要实现 DagorEngine 的 btag/dag2Tree 运行时反序列化
  （参考 BSD 开源的 GaijinEntertainment/DagorEngine：gameResSystem.cpp 的 GrpHeader、
  dagFileFormat.h 的经典 DAG 记录格式、ioSys/dag_btagCompr.h 的 NONE/ZSTD/OODLE 块压缩）。
- 社区路径：klensy/wt-tools（vromfs/blk）、dag4blend（面向源 .dag 资产，非编译 dynmodel）。
- 碰撞 BVH（0xace50003）同样需要专用解析；启发式捞浮点只得到量化网格与哨兵。

## 参数驱动船体网格（已交付）

`tools/generate_hull_obj.py` 从舰队数据（长/宽/吃水）程序化生成 3D 船体网格
（41 站线 x 9 列 x 双舷 + 甲板封盖 + 甲板轮廓 `l` 环）→ `assets/models/<ship_id>/hull.obj`。
全部 30 艘舰队 + 2 测试舰已生成（738 顶点/1360 面每舰），preview.png 为软件光栅化验证图。
BattleView 前端已接入：对有 hull.obj 的舰加载甲板轮廓环，以真实剪影替换占位多边形。

## 当前管线

`python tools/extract_ship_model.py <file.grp> --out assets/raw/models` → 按条目提取+格式识别+解压。
`--inventory` 批量扫描 598 艘 → `assets/raw/models/model_inventory.json`（ provenance 清单）。
