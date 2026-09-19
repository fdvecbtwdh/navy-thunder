# Navy Thunder

[![CI](https://github.com/fdvecbtwdh/navy-thunder/actions/workflows/ci.yml/badge.svg)](https://github.com/fdvecbtwdh/navy-thunder/actions/workflows/ci.yml)

一款正在开发中的**单机海战游戏**(终版目标 **Navy Thunder 1.0**),当前已交付的是一个**确定性海空战斗模拟引擎**:
舰船 + 飞机 + 武器 + 弹药 + 装甲 + 物理 + 模块 + 舰员 + 损伤 + 弹道 + 火控 + 防空 + 鱼雷 + 导弹的统一体系,
headless 可跑完整 AI 对战并输出战报。

## 项目定位

| 决策 | 结论 |
|---|---|
| 游戏形态 | 单机海战遭遇战(PvE):玩家操舰 + AI 友军/敌军;附全 AI 观战与靶场 |
| 时代/阵营 | 二战为主;首发**美 / 日 / 英 / 德**四阵营 |
| 战斗规模 | 3v3 ~ 6v6 舰船 + 舰载机小队 |
| 前端 | Godot 4(.NET/C#),直接复用零依赖的 Core 引擎 |
| 语言 | 简体中文 + English |

多人同步、科技树、潜艇等明确列为 Post-Release(引擎的确定性 tick + 事件日志已 lockstep 就绪)。

## 机制基准原则

- **战斗计算**:以 War Thunder 当前实际机制为主要参考(官方 wiki / 更新日志 / 公开 datamine 交叉验证)。
- **舰船设计/建造/物理**:以 NavalArt(Steam,Rigidbody/RZ Entertainment)的机制与设计理念为主要参考。
- 两者冲突时按领域裁决:战斗计算 → War Thunder 优先;建造/设计/部件/沙盒 → NavalArt 优先。
- **未公开的机制一律参数化近似 + 证据记录 + 标记 `approximation`**,保证可替换——绝不把猜测伪装成"War Thunder 的公式"。

每一条机制决策都以 **MDR(Mechanism Decision Record)** 形式归档在 [`docs/mechanisms/`](docs/mechanisms/README.md):
决议、双方行为与证据(带 URL 与日期)、历史变化、未知点、实现与替换方案。目前已有 **16 份 MDR**。

## 当前状态

**Phase 0–7 全部完成;R0「战斗闭环」进行中(8 项已完成 6 项);100 测试全绿;校准报告 12/12。**

已完成:

- [x] 确定性核心:固定步长世界、xoshiro256** 确定性 RNG(命名流)、事件日志
- [x] 穿深管线:官方 de Marre 公式 + 转正/跳弹/overmatch + 引信状态机(接触/延迟/哑弹/VT)
- [x] 弹道:点质量 + RK4 + 逐弹阻力,对官方射表校准 Mk8 ±3%(实测最大偏差 0.48%)
- [x] 爆炸/破片/超压、舰船伤害模型(舱室/舰员/分区/进水/浮力/损管/击沉判定)
- [x] 火灾系统(掷骰起火、蔓延、损管灭火)、鱼雷(舰载直线雷 + hydroShock)
- [x] 飞机伤害模型与击杀分层、防空 VT 弹幕、雷达/火控解算、导弹制导层(IR/SARH/ARH)
- [x] R0.1 舰炮武器系统(装填→弹药消耗→散布→弹道)、R0.2 舰船运动(操舵/油门/航速积分)
- [x] R0.3 起火接入战斗管线、R0.6 真实齐射散布(弹着沿舰体剖面分布,跨射/近弹/远弹)
- [x] R0.7 战斗框架(阵营/胜利条件/战斗时钟/战报 JSON)、R0.8 SimRunner v2(场景驱动)
- [x] W4.1 CI:构建 + 全量测试 + 数据 schema 校验 + 校准报告门槛

进行中:

- [ ] R0.4 TDS 鱼雷防护(隔舱吸收水爆,不拦水下 AP)
- [ ] R0.5 航空武器投送(炸弹/火箭/空投鱼雷从飞机实体投出)

数据集:32 艘舰(美/日/英/德模板舰队)、8 弹种、Type 93 鱼雷、飞机与导弹数据扩录中(W3 并行工作流)。

## 快速开始

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。

```bash
# 构建与测试
dotnet build NavyThunder.slnx
dotnet test NavyThunder.slnx

# 跑一场完整 AI 对战(战列舰对决 → 战报 JSON,同 seed 逐字节可重放)
dotnet run --project src/NavyThunder.SimRunner -- --scenario scenarios/bb_duel.json --out battle-report.json

# 确定性真空弹道演示
dotnet run --project src/NavyThunder.SimRunner -- 800 45

# 数据集摘要与校准报告(穿深 vs 官方射表回归)
dotnet run --project src/NavyThunder.ProtectionAnalysis -- summary
dotnet run --project src/NavyThunder.ProtectionAnalysis -- report
```

每次 push/PR 由 GitHub Actions 执行完整门禁:构建 → 100 测试 → 全数据集 schema 加载 → 校准报告回归。

## 目录结构

```
src/NavyThunder.Core                # 战斗引擎(零引擎依赖、确定性、headless)
  Mathematics/                      #   Vec3、确定性 RNG(xoshiro256** + 命名流)
  World/                            #   固定步长世界、系统、实体、事件日志
  Ballistics/                       #   点质量弹道、RK4、阻力模型、gunnery
  Armor/  Protection/  Damage/      #   穿深管线、防护、伤害通道
  Explosions/  Fire/  Torpedoes/    #   爆炸/破片/超压、火灾、鱼雷
  Ships/                            #   舰炮系统、航行、进水、损管、击沉判定、简单 AI
  Aircraft/  AntiAir/  Missiles/    #   飞机伤害与击杀分层、防空 VT、导弹制导
  FireControl/  Battle/             #   火控解算、战斗框架(阵营/胜负/战报)
  Model/  Geometry/                 #   舱室/分区模型、命中几何
src/NavyThunder.Data                # JSON schema、加载器、校验器、路径定位
src/NavyThunder.ProtectionAnalysis  # CLI:数据集摘要、穿深校准报告
src/NavyThunder.SimRunner           # nt-sim:场景驱动批量模拟(战报 JSON/CSV)
tests/NavyThunder.Core.Tests        # 确定性 xUnit 测试(100 个)
data/                               # 数据驱动内容(带溯源)
  ships/  shells/  torpedoes/  aircraft/
  reference/                        #   WT 基准值(事实,带 URL + 版本戳)
  calibration/                      #   近似参数(approximation:true + 依据)
docs/mechanisms/                    # 16 份 MDR 机制决策记录
docs/ROADMAP.md                     # Release Roadmap v2(终点 = Navy Thunder 1.0)
scenarios/                          # 战斗场景定义(bb_duel.json 等)
tools/                              # WT datamine 提取、模板舰队生成、vromfs 解包
```

## 数据与校准

- 所有数值基于 **War Thunder 官方基准**(`data/reference/`,带 URL 与版本戳);官方未公开的参数进 `data/calibration/`,显式标记 `approximation` 并记录依据。
- 校准报告对官方射表做逐点回归:Mk 8 穿帽弹 6 个距离点全部 ±3% 内,当前最大偏差 **0.48%**;Mk 46 / Mk 13 HE 按噪声水平放宽容差。报告见 [`docs/calibration-report.json`](docs/calibration-report.json),CI 将其作为合并门槛。
- 数据扩录工具(持续使用):

```bash
python tools/unpack_vromfs.py <aces.vromfs.bin> <outdir>   # 本地 WT 客户端解包(自研 zstd+XOR)
python tools/extract_datamine.py --dir <datamine>/gamedata/weapons --selftest
python tools/generate_ships.py                             # 模板舰队扩充
```

本地 WT 客户端解包已打通:2,344 艘舰的 blk 单位参数可离线提取,后续数据扩录不再依赖网络 datamine。

## 路线图

完整版见 [`docs/ROADMAP.md`](docs/ROADMAP.md)。终点为 **Navy Thunder 1.0 正式发布**,每个阶段以 Release Gate(玩法/机制/AI/内容/数据/资源/UI/性能/工程/发布工程十项)逐项验收:

- **R0 战斗闭环**(进行中):headless 完整可跑的一整场战斗
- **R1 AI 与 headless 完整游戏**:舰船 AI、飞机任务链、飞行模型积分、性能预算基准
- **R2 前端垂直切片**:Godot 4 + Core,玩家操舰、瞄准开火、损伤 HUD
- **R3 内容与体验**:WT 资源提取管线(模型/贴图/音效)、特效、UI 全流程、zh-CN/en 本地化
- **R4 打磨与稳定**:性能达标、2h soak、崩溃处理、平衡 pass
- **R5 发布工程**:打包安装、授权审计、RC 全量测试 → 1.0

## License

本项目代码以 [GPL-3.0](LICENSE) 协议开源。
