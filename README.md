# Navy Thunder

一套完整的**舰船 + 飞机 + 武器 + 弹药 + 装甲 + 物理 + 模块 + 舰员 + 损伤 + 弹道 + 火控 + 防空 + 鱼雷 + 导弹**统一海空战斗模拟体系。

- **战斗计算**：以 War Thunder 当前实际机制为主要参考（官方 wiki / 更新日志 / 公开 datamine 交叉验证）。
- **舰船设计/建造/物理**：以 NavalArt（Steam，Rigidbody/RZ Entertainment）的机制与设计理念为主要参考。
- 两者冲突时：战斗计算 → War Thunder 优先；建造/设计/部件/沙盒 → NavalArt 优先。
- 未公开的机制：参数化近似 + 证据记录 + 标记 `approximation`，保证可替换（见 `docs/mechanisms/`）。

## 当前状态

**Phase 0–7 全部完成，93 测试全绿，校准报告 12/12。当前处于 R0「战斗闭环」阶段（见 ROADMAP）。**

- [x] 穿深管线：官方 de Marre 公式 + 转正/跳弹/overmatch + 引信状态机 + 射表阻力校准（±1.7%）
- [x] 舰船/飞机伤害模型、爆炸/破片/超压、进水/损管/击沉判定、鱼雷、防空 VT、导弹层
- [x] 数据驱动：32 艘舰、8 弹种、Type 93、16 份 MDR 机制决策记录
- [ ] R0 战斗闭环进行中：舰炮开火系统、舰船运动、起火接线、战斗框架/战报

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

Phase 0 骨架与数据基线 → Phase 1 弹道+穿甲 → Phase 2 爆炸/破片/超压 → Phase 3 舰船伤害模型 → Phase 4 飞机与空战 → Phase 5 防空/雷达/火控 → Phase 6 导弹与现代层 → Phase 7 数据管线与校准收束 —— **全部完成**。

数据扩充工作流（持续）：

```bash
python tools/extract_datamine.py --dir <datamine>/gamedata/weapons --selftest  # WT 数值提取
python tools/generate_ships.py                                                 # 模板舰队扩充
dotnet run --project src/NavyThunder.ProtectionAnalysis -- report              # 校准报告
```

详细机制依据见 [docs/mechanisms/README.md](docs/mechanisms/README.md)。校准状态见 [docs/calibration-report.json](docs/calibration-report.json)。后续工作安排见 [docs/ROADMAP.md](docs/ROADMAP.md)。

## License

本项目以 [GPL-3.0](LICENSE) 协议开源。
