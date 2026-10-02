# Phase 06 — 舰船建造系统

> 状态：`[PLANNED]`
> 前置依赖：Phase 01（几何语义）、Phase 04（部件语义稳定）、Phase 05（UI 框架）。

## 1. 阶段目标
交付 NavalArt 式舰船建造器：玩家在编辑器里拼船 → 确定性编译为 ShipDefinition → 直接进战斗/AI 观战/战报统计，与史实舰零特殊分支（PROJECT_DESIGN §10）。

## 2. 当前基础（Level A）
零实现。可用资产：`ShipDefinition` schema（编译目标）、Core 确定性架构（编译器可 headless 批量测试）、Phase 02 UI 框架、`AppEnv saves/` 用户目录。

## 3. 外部参考与来源
`NAVALART_RESEARCH.md` 全文（Level C 反编译：.na XML 格式/部件模型/计重/浮力求解/存档兼容/ModTool 白名单）+ `SHIP_BUILDING_REFERENCE.md`（综合裁决）+ MDR-0016（Level B 官方版本史）。

## 4. 不做什么
顶点级编辑（模块级先行）、多人共建、工坊上传、潜艇载具部件、历史preset导入（.na 格式不兼容——仅设计思想借鉴）。

## 5. 技术设计

### 5.1 数据模型
```
BuilderDesign（JSON v1，用户可读可编辑）
├─ meta        id / name / author / description / version
├─ hullBlocks[]     8 参数分段（NA AdjustableHull 参数集）+ 局部位姿 + 镜像对
├─ armorBlocks[]    板/块 + mm 厚度（显式装甲区优先，块装甲计重）
├─ parts[]          部件引用（PartKind + 参数）+ 局部位姿 + 稳定 GUID
└─ attachments      供弹链/炮塔组/电路（TurretGroup 显式化）
```

### 5.2 编译器（`NavyThunder.Core.Builder`，确定性纯函数）
按 PROJECT_DESIGN §10.3：体积→阶梯吃水求逆；重量=体积×密度（装甲块密度=f(armor)）；CoM/CoB 加权；生死池=储备浮力；校验规则组（浮力/力矩/动力/舵面积/兴波校核）。输出 `ShipDefinition` + 校验报告（错误/警告分级）。

### 5.3 编辑器 UX
部件面板（按 `cubeClass` 分组）/ 放置-移动-旋转-删除-复制 / 镜像 / 撤销重做（命令栈）/ 参数内联编辑 / **CG-CoB 即时指示** / 浮态预览（吃水/横倾）/ 试航按钮（注入 SimRunner 场景 headless 跑 + 战报回显）/ 保存加载（AppEnv saves/designs/）。

### 5.4 操作集与撤销
所有编辑=不可变命令对象（与 Phase 05 命令层同构）；撤销栈=命令逆操作；存档=当前设计快照。

## 6. 文件修改范围
新增 `src/NavyThunder.Core/Builder/`（设计模型+编译器+校验）、`frontend/Godot/Builder*`（场景/UI）、`AppEnv`（designs 目录）、`tests/`（编译器+端到端）、`docs/`（BuilderDesign schema 说明）。

## 7. 依赖 / 并行
依赖 01/04/05。并行：部件清单二期（弹射器/机库——数据字段预留即可）。

## 8. 风险
| 风险 | 对策 |
|---|---|
| 编译产物破坏战斗平衡 | 编译器输出物进"自建舰"标记；平衡 pass（Phase 08）单独处理；price/tier 字段预留 |
| 浮力/重量公式手感差 | 面板可见的中间量（重量/浮量/吃水实时显示）；常量进 calibration 可调 |
| UI 范围失控 | MVP=方块+六类部件；扩展部件随数据 schema 渐进 |
| 存档兼容 | 版本号+迁移器从 v1 就做（NA 无版本教训） |

## 9. 测试
- 编译器：恒等式（Σ块体积=报告体积；Σ重量=报告重量）；非法设计拒绝矩阵；确定性（同输入双编译逐字节同）。
- 端到端：BuilderDesign→编译→SimRunner 跑通→战报含该舰。
- 浮力：已知简单船型手算吃水对照。

## 10. 验收标准（PASS/FAIL）
- PASS：用建造器拼一艘"双炮炮艇"，编译零错误，进 bb_duel 场景替换一方战斗并出现在战报。
- PASS：给同一船增加装甲 → 编译报告重量/吃水同步变化（派生值不落盘验证）。
- PASS：浮力<重量的设计被拒绝并给出可读错误。
- PASS：撤销/重做/镜像/复制全可用；保存后重载逐字节一致。
- PASS：编译器 headless 批量测试（100 份随机合法设计全部可编译可战斗）。

## 11. 完成后状态
Navy Thunder 完成定位闭环（玩 + 造）；Phase 07 的 AI 大规模战斗可直接消费自建舰。
