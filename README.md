# Navy Thunder

一套完整的**舰船 + 飞机 + 武器 + 弹药 + 装甲 + 物理 + 模块 + 舰员 + 损伤 + 弹道 + 火控 + 防空 + 鱼雷 + 导弹**统一海空战斗模拟体系。

- **战斗计算**：以 War Thunder 当前实际机制为主要参考（官方 wiki / 更新日志 / 公开 datamine 交叉验证）。
- **舰船设计/建造/物理**：以 NavalArt（Steam，Rigidbody/RZ Entertainment）的机制与设计理念为主要参考。
- 两者冲突时：战斗计算 → War Thunder 优先；建造/设计/部件/沙盒 → NavalArt 优先。
- 未公开的机制：参数化近似 + 证据记录 + 标记 `approximation`，保证可替换（见 `docs/mechanisms/`）。

## 当前状态

**Phase 0 — 项目骨架与数据基线（进行中）**

- [x] .NET 10 solution 骨架：Core（引擎）/ Data（数据）/ ProtectionAnalysis（CLI）/ SimRunner（批量模拟）/ Tests
- [x] 确定性模拟核心：固定步长 50Hz、全序系统更新、xoshiro256** 命名流 RNG、RK4 弹道积分
- [x] 数据层：版本化 JSON schema（shell/torpedo/wtReference/calibration）、加载器、硬校验（无溯源的参考值直接拒绝）
- [x] 首批基准数据：6 种 USN 舰炮弹药（含 de Marre 系数与引信参数）、Type 93 鱼雷、14 条 WT 参考值、4 条校准近似
- [x] 25 个单元测试全绿（数学/RNG/世界确定性/真空弹道/数据校验）

## 构建与测试

```bash
dotnet build NavyThunder.slnx
dotnet test NavyThunder.slnx

# 数据集摘要
dotnet run --project src/NavyThunder.ProtectionAnalysis -- summary

# 确定性真空弹道演示
dotnet run --project src/NavyThunder.SimRunner
```

## 目录结构

```
src/NavyThunder.Core                # 战斗引擎（零引擎依赖、确定性）
  Mathematics/                      #   Vec3、确定性 RNG（xoshiro256** + 命名流）
  World/                            #   固定步长世界、系统、实体、事件日志
  Ballistics/                       #   点质量弹道、RK4、阻力模型、落地事件
src/NavyThunder.Data                # JSON schema、加载器、校验器、路径定位
src/NavyThunder.ProtectionAnalysis  # Protection Analysis CLI（Phase 1 起输出全链路报告）
src/NavyThunder.SimRunner           # 场景化批量模拟（输出可比 CSV/JSON）
data/                               # 数据驱动内容
  shells/  torpedoes/               #   弹药/鱼雷定义（带溯源）
  reference/                        #   WT 基准值（事实，带 URL + 版本戳）
  calibration/                      #   近似参数（approximation:true + 依据）
tests/NavyThunder.Core.Tests        # 确定性 xUnit 测试
docs/mechanisms/                    # MDR 机制决策记录（行为/历史/证据/置信度/未知点/替换方案）
```

## 开发阶段

Phase 0 骨架与数据基线 → **Phase 1 弹道+穿甲（de Marre/转正/跳弹/引信）** → Phase 2 爆炸/破片/超压 → Phase 3 舰船伤害模型 → Phase 4 飞机与空战 → Phase 5 防空/雷达/火控 → Phase 6 导弹与现代层 → Phase 7 大规模数据录入与校准收束。

详细机制依据见 [docs/mechanisms/README.md](docs/mechanisms/README.md)。
