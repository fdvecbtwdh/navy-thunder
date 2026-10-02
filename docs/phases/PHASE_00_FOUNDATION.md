# Phase 00 — 项目基础与文档统一

> 状态：`[IN PROGRESS]`（本阶段的大部分文档工作已随 2026-10-02 设计基准任务完成）
> 前置依赖：无。后续所有阶段的前置。

## 1. 阶段目标
建立"文档可信、门禁更快、变更可审计"的开发基线，让 Phase 01+ 可以安全动手。

## 2. 当前基础（Level A 事实）
- PROJECT_DESIGN.md + docs/research/（6 文件）+ docs/phases/ 已建立（本阶段任务主体）。
- README/RELEASE_CHECKLIST/ROADMAP 三处失实已修正（commit 558890b）：TDS 未实现、音效未接线、测试数 113、R0.5 已完成。
- ROADMAP.md 已加历史横幅，后续开发以 PROJECT_DESIGN + phases 为准。

## 3. 外部参考
- 无外部依赖；本阶段只消费本仓调查结论（SOURCE_INDEX A1-A10）。

## 4. 不做什么
- 不写任何产品功能代码；不改模拟行为（golden 不动）。

## 5. 剩余任务清单
| 任务 | 说明 | 验收 |
|---|---|---|
| P00-1 MDR 冲突增补 | 在 MDR-0007 增补"官方原文 3 段 vs 代码 2 段"冲突注记；MDR-0012 增补鱼雷深度可调性冲突（SOURCE_INDEX 冲突台账） | 两份 MDR 各含一条修订记录 |
| P00-2 性能测试快速模式 | `PerformanceBudgetTests` 在 Debug 下支持 `NT_PERF_QUICK=1` 短跑（200s 模拟）以加速本地迭代；CI Release 门禁不变 | Debug 全套测试 < 10 分钟；Release 门禁语义不变 |
| P00-3 .gitignore 补齐 | `__pycache__/`、`_na_decomp/`（后者已加） | `git status` 干净 |
| P00-4 变更审计惯例 | 黄金文件再生成必须：独立 commit + commit message 说明原因 + 关联 MDR/阶段文档 | 惯例写入 PROJECT_DESIGN §5.3（已完成） |

## 6. 文件修改范围
`docs/mechanisms/MDR-0007`、`MDR-0012`、`tests/.../PerformanceBudgetTests.cs`、`.gitignore`。

## 7. 依赖 / 并行
- 无依赖。P00-2 可与 Phase 01 并行。

## 8. 风险
- 低。唯一注意点：P00-2 不得改变 Release 门禁判定逻辑（只加 Debug 短跑路径）。

## 9. 测试
- P00-2 自身有测试含义：CI 中 Release 过滤器继续跑完整预算。

## 10. 验收标准（PASS/FAIL）
- PASS：MDR-0007 与 MDR-0012 各含一条 2026-10 冲突注记。
- PASS：`NT_PERF_QUICK=1 dotnet test`（Debug）全套 ≤ 10 分钟，且不设该变量时行为与现在一致。
- PASS：`git status` 无意外未跟踪产物。

## 11. 完成后状态
文档体系与 CI 基线可信；进入 Phase 01（模拟几何）。
