# Navy Thunder 测试体系规则(权威来源)

> 2026-10-04 测试体系重构后确立。本文档是测试分类的**权威来源**;`PROJECT_DESIGN.md` §10 引用此处。
> 本次重构的背景与原因记录在文末 §6,防止未来回归。

## 1. 测试分层(Bucket Trait)

每个测试类/方法用 `[Trait("Bucket", ...)]` 声明所属层级。**新增测试必须显式声明 Bucket**;
未声明的测试不会出现在任何一档的过滤结果里(会被静默漏跑,CI 的 bucket 计数断言防这个)。

| Bucket | 目的 | 允许的规模 | 完整战斗? | 默认 `dotnet test`? | PR CI? | 何时执行 |
|---|---|---|---|---|---|---|
| **Fast** | 开发者快速反馈:纯逻辑、几何、数据解析、单元机制、局部伤害 | 无完整战斗;单测目标 <100 ms | ❌ | ✅(默认) | ✅ | 每次构建 |
| **Integration** | 多系统协同:真实 `BattleRunner`/伤害栈的短战斗 | 模拟 ≤300 s(墙钟秒级) | ❌ | ❌(显式) | ✅ | PR CI |
| **Golden** | 确定性回归:完整战斗 vs 提交的 golden 摘要 | 允许 3600 s | ✅ | ❌ | ❌ | Nightly |
| **Acceptance** | 行为级验收:AI 编队战、报告一致性、r1 全链 | 允许 2400 s 完整战斗 | ✅ | ❌ | ❌ | Nightly |
| **Performance** | R1.6 预算门(6v6 ≥1x 实时、破片有界) | 6v6 至预算点 | 部分 | ❌ | ❌ | Nightly + Release |
| **Soak** | 长时稳定性(`tools/soak.sh` 脚本,非 xunit bucket) | 循环完整战斗数小时 | ✅ | ❌ | ❌ | Release 前 |

## 2. 执行命令

```bash
# 开发日常(目标:秒级;实测 136 项 / ~1 s,2026-10-05)
dotnet test tests/NavyThunder.Core.Tests --filter "Bucket=Fast"

# PR CI(Fast + Integration,目标:分钟级)
dotnet test tests/NavyThunder.Core.Tests --filter "Bucket=Fast|Bucket=Integration"

# Nightly(Golden + Acceptance + Performance)
dotnet test tests/NavyThunder.Core.Tests --filter "Bucket=Golden|Bucket=Acceptance|Bucket=Performance"

# Release 前全量(含 Soak)
dotnet test tests/NavyThunder.Core.Tests
```

**禁止用黑名单(`FullyQualifiedName!~XXX`)定义任何一档**——黑名单随新文件腐烂,2026-10 前的
"Fast batch" 就是这么变成 1h38m 的。

## 3. Fast 的硬性原则

- Fast 用于快速反馈,**不得包含完整战斗模拟**(2400s/3600s 级)。
- 归类依据是**实际运行性质**(模拟时长、启动的系统栈),不是文件名/Phase 归属。
  (例:`Phase04MechanicsTests` 名字像 Phase 04 专属,但最大 300 s 模拟墙钟 <1 s——归 Integration 而非 Fast。)
- Fast 整体预算:**分钟级以内**(当前实测 2 s);若最慢测试从毫秒涨到秒级、或总量突破 1 分钟,
  按基础设施/性能回归排查,不要默认接受。

## 4. 长战斗测试原则

完整战役模拟是高成本资源,仅在以下场景使用:

- **确定性回归引用**(Golden:完整 3600s bb_duel vs 提交的摘要);
- **行为级验收**(Acceptance:AI 编队战必须出现机动/换目标/战报一致——这些是涌现行为,无法用
  小 fixture 复现);
- **性能/稳定性预算**(Performance/Soak)。

如果测试只验证"某次命中起火 / 某模块损毁 / 某 AI 决策",构造最小可复现状态
(`CombatHarness` + `ApplyDamage` + 少量 tick),不要跑数千秒等事件自然发生。

## 5. 禁止无意义重复模拟

- 同一大型完整战斗**不得**在多个普通测试中重复运行(bb_duel 3600s 曾经被完整跑 7 次/轮)。
- 验证确定性:引擎级确定性由 **GoldenFileTests 一处**背书(完整战斗 vs 提交摘要);
  其它确定性测试用**同种子短切片双跑对比**(180-240 s 切片,校验导航/炮/损状态逐位一致)。
- 禁止 `a.Run(); b.Run();` 双完整战斗只为比较两份报告。
- 可共享的只读数据(DataRepository 等)可缓存;SimulationWorld/RNG/舰船状态**永不共享**。

## 6. 本次重构原因(2026-10-04)

- "Fast batch" 名不副实:黑名单排除法 + 无分类机制,实际含 7 次 bb_duel 3600s + 3 次 fleet_6v6
  2400s,单轮 1h27m–1h38m;
- `Battle_Is_Deterministic_Across_Runs`、`Scenario_With_Aircraft_Is_Deterministic` 为对比确定性
  各完整跑两遍同一场 3600s 战斗;
- `Fire_Ignition_Rolls_Wire_Into_Combat` 跑完整 3600s 战斗只为 `Assert.NotNull(runner.Fire)`;
- CI unit-gates 同样包含长战斗。

重构后实测(2026-10-04,Debug 构建):

```text
FAST         136 项   ~1 s      (2026-10-05 实测,P04/P05 新增后)
INTEGRATION   40 项   ~3-4 m    最慢 ~70 s(轰炸链 300 s 切片)
GOLDEN         1 项   ~12 m     bb_duel 3600s
ACCEPTANCE     5 项   ~40 m     6v6 2400s ×2 + r1 2400s + bb_duel 3600s
PERFORMANCE    2 项   ~5 m
```

开发日常:`Bucket=Fast`(秒级)。PR CI:Fast+Integration(分钟级)。完整测试仅 Nightly/Release。

## 7. 新增测试自检清单

1. 验证什么?能否用 ≤300 s 模拟或纯逻辑复现?
2. 需要真实 SimulationWorld 吗?需要完整战斗吗?
3. 最大模拟时间?
4. Bucket = ?
5. 进默认 `dotnet test` 吗?(只有 Fast)
6. 进 PR CI 吗?(Fast + Integration)
7. 若确需长战斗:动机属于 Golden/Acceptance/Performance/Soak 中的哪一个?
