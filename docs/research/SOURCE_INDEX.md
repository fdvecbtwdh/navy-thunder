# 研究来源索引（SOURCE_INDEX）

> 用途：全部设计决策的来源台账。等级定义：**A**=本项目代码/测试/运行实测 · **B**=官方资料（wiki/公告/changelog/dev blog）· **C**=本地提取的真实游戏数据与反编译（研究用途）· **D**=可靠社区资料 · **E**=论坛讨论/视频观察 · **F**=推测（设计文档中不允许以 F 支撑 FACT）。
> 冲突裁决规则：A > B > C > D > E；同等级冲突按发布时间取新；无法裁决→参数化近似+标记（与 MDR 全局规则一致）。

## Level A — 本项目代码与运行实测（最高优先）

| # | 来源 | 主题 | 用于哪个决策 |
|---|---|---|---|
| A1 | 全仓源码 87 文件/约 7,620 行（2026-10-02 全读） | Core 架构、命中几何轴对齐、AI、损伤链 | PROJECT_DESIGN §3-§13 全部"NT 现状" |
| A2 | `dotnet test` 113/113（Debug 52 分钟） | 测试基线与性能测试耗时 | PROJECT_DESIGN §13、PHASE_00 |
| A3 | SimRunner bb_duel 双跑战报逐字节一致 | 确定性验证 | PROJECT_DESIGN §3.2-2 |
| A4 | Godot 前端实际运行截图（菜单/战斗） | 2D 占位画面、文本 HUD、无小地图 | PROJECT_DESIGN §2/§8 状态标注 |
| A5 | `AudioManager.PlayGun/PlayExplosion` 零调用点（grep） | 音效未接线 | RELEASE_CHECKLIST Gate 7 修正、§2-13 |
| A6 | `grep -i TDS` 源码零匹配 | TDS 未实现 | RELEASE_CHECKLIST Gate 2 修正、Phase 04 |
| A7 | 数据溯源分析（wt_ship_units 653/647 原始值；fleet 24+6 来源分布） | 数据分级 | PROJECT_DESIGN §4.2 |
| A8 | `D:\WarThunder` 在位：aces.vromfs 17.9MB、598 .grp 3.9GB | 资产可用性 | §9 资产双轨 |
| A9 | `_wt_audit/`：2,344 舰 blk+382 炮 blk+DagorEngine 参考源码 | 数据管线现状 | §9、PHASE_03 |
| A10 | `tools/extract_ship_model.py` 注释自认 BIM2 未反序列化 | 工具链边界 | §9、PHASE_03 |

## Level B — 官方资料

| # | 名称 | URL | 日期 | 主题 | 用于 |
|---|---|---|---|---|---|
| B1 | Ship Crew Mechanics | https://wiki.warthunder.com/640-ship-crew-mechanics | 2024-12-10 | 125HP/人、双阈值、舰桥可修 | MDR-0006 交叉、§4.1 |
| B2 | Ship Modules | https://wiki.warthunder.com/mechanics/5245-ship-modules | 2020-09-02 | 模块清单/后果（早于 2.45 注意） | §6.4、SHIP_DAMAGE §2 |
| B3 | HE Effect and Overpressure | https://wiki.warthunder.com/mechanics/4236-the-mechanics-of-high-explosive-effect-and-overpressure | 2025-07-31 | 三成分/破片锥/超压边界 | MDR-0005 交叉 |
| B4 | Hornet's Sting changelog | https://warthunder.com/en/game/changelog/current/1716 | 2025-03-18 | 分区重做/丧失不沉性 3 段/100mm+ 弹道 | MDR-0007 冲突记录、§6 |
| B5 | Leviathans changelog | https://warthunder.com/en/game/changelog/current/1749 | 2025-06-25 | 破口三类/殉爆∝剩余弹药/电梯火方向/**TDS 官方确认** | SHIP_DAMAGE §2-3、Phase 04 |
| B6 | New Damage Control dev blog | https://warthunder.com/en/news/9795-development-the-new-damage-control-mechanic-for-naval-en | 2025-11-05 | 三流程自动/DC 系数/手动开关 | §6.8、Phase 05 |
| B7 | Naval Test: aiming + hydrodynamics | https://warthunder.com/en/news/4871-naval-test-new-aiming-system-hydrodynamics-and-other-changes-en | 2017-07-28 | 瞄准两模式/机动降精度/横倾倾覆/hit cam | §8.3、NAVAL_COMBAT §2 |
| B8 | Spearhead changelog | https://warthunder.com/en/game/changelog/current/1797 | 2025-11-11 | DC 机制随版 | MDR-0009（MDR 已核） |
| B9 | Mastering The Art Of Torpedo Bombing | https://wiki.warthunder.com/4620-mastering-the-art-of-torpedo-bombing | — | 鱼雷深度设置 | MDR-0012 冲突记录 |
| B10 | Torpedoes 分类页 | https://wiki.warthunder.com/torpedo | — | 鱼雷数据页索引 | Phase 04 |
| B11 | Steam 商店页 NavalArt | https://store.steampowered.com/app/842780/NavalArt | 2018-06 EA | 基本信息 | MDR-0016 |
| B12 | DagorEngine 开源仓 | https://github.com/GaijinEntertainment/DagorEngine | 2023-10 开源 | btag/dag2Tree 参照 | DAGOR_ASSET_RESEARCH、Phase 03 |
| B13 | WT CDK 模型工作流 | https://wiki.warthunder.com/cdk/creation_model | — | .dag 源格式入口 | DAGOR_ASSET_RESEARCH |
| B14 | NavalArt 官方公告页/SteamDB | （见 MDR-0016 引用） | 至 1.6 公测 | 版本史 | MDR-0016 |
| B15 | MDR-0001~0016 已核官方来源 | 见 docs/mechanisms/ 各文件 | 2026-09-18 起 | 16 机制决议证据链 | 全部 MDR |

## Level C — 本地提取数据与反编译（研究用途）

| # | 来源 | 主题 | 用于 |
|---|---|---|---|
| C1 | NavalArt `Assembly-CSharp.dll` 反编译（ilspycmd 11.1，本机 `_na_decomp\`） | 建造数据模型/浮力/伤害/存档 | NAVALART_RESEARCH 全文、Phase 06 |
| C2 | NavalArt `NAModToolCore.dll` 反编译 + ModTool 教程 PDF/docx | 参数白名单 | NAVALART §8、数据 schema |
| C3 | 工坊 .na 样本（IJN Fuso / YAMATO 等 334 项）+ 264 预设舰 + parts.csv（1,629 件） | .na 格式/部件清单 | NAVALART §1/§5 |
| C4 | `_wt_audit/units` 2,344 舰 blk（BBF3 全解码） | WT 单位参数 | 数据扩录、Phase 03 |
| C5 | `_wt_audit/weapons` 382 炮 blk | 武器参数 | 数据扩录 |
| C6 | `data/reference/wt_ship_units.json`（2,322 舰） | Tier-1 数值 | §4.2 数据分级 |
| C7 | `data/reference/wt_ship_weapons.json`（24 舰实测） | Tier-1 数值 | §4.2 |
| C8 | `assets/raw/models/ger_battleship_bismarck/`（BIM2/collision/dmg 原始条目） | 格式逆向对象 | DAGOR_ASSET_RESEARCH |
| C9 | `_wt_audit/dagor.tar.gz` + `dagor_tree.json`（DagorEngine @7572366） | 格式参照 | DAGOR_ASSET_RESEARCH |
| C10 | klensy/wt-tools 克隆（`_wt_audit/wt_tools_repo/`） | 格式对照基准 | 工具链 |

## Level D — 可靠社区资料

| # | 名称 | URL | 主题 | 用于 |
|---|---|---|---|---|
| D1 | quentin-dh/Dagor-Asset-Explorer | https://github.com/quentin-dh/Dagor-Asset-Explorer | DynModel/GeomNodeTree 解析 | DAGOR_ASSET_RESEARCH |
| D2 | ZenHAX dynmodel 讨论 | https://zenhax.com/viewtopic.php@t=15887.html | .grp/.dynmodel 格式 | DAGOR_ASSET_RESEARCH |
| D3 | gszabi99/War-Thunder-Datamine | https://github.com/gszabi99/War-Thunder-Datamine | 逐弹参数（demarreK/Cx/引信） | MDR-0001~0003 |
| D4 | WT 社区 wiki/guide 生态（Steam guides） | https://steamcommunity.com/sharedfiles/filedetails/?id=2798697627 等 | 海战入门/损管实操 | NAVAL_COMBAT（Level D 佐证） |
| D5 | NavalArt 社区教程（Steam guides/YouTube/r/NavalArt） | https://steamcommunity.com/sharedfiles/filedetails/?id=2441560024 等 | 建造手法 | NAVALART（佐证，机制以 C1 为准） |

## Level E — 论坛/视频观察（仅 UX 形状参考）

| # | 名称 | 主题 | 用于 |
|---|---|---|---|
| E1 | WT 官方论坛 naval aiming/hud 帖（forum.warthunder.com/t/289373 等） | 变焦无 FOV、C 键半望远镜、测距实操 | NAVAL_COMBAT §2 |
| E2 | WoWS/Sea Power UX 观察 | 锁定 UI/消耗品损管/战术地图 | NAVAL_COMBAT §3（不采纳消耗品模型） |
| E3 | 社区 DC 时长实测（取消 ~3s/冷却 ~30s） | Spearhead 数值空缺补充 | MDR-0009（已标社区数据） |

## 冲突台账

| 冲突 | 双方 | 裁决状态 |
|---|---|---|
| 大舰丧失不沉性毁段数 | B4 changelog"三段" vs MDR-0007/代码 `destroyedMid>=2` | **待裁决**（PROJECT_DESIGN §6.8 → Phase 04） |
| 鱼雷深度可调 | B9 wiki"可设" vs MDR-0012 blk 固定 diveDepth | **待复核**（当前客户端 blk，Phase 04 前） |
| 舵机自动修复 | B2（2020 页） vs NT 冻死 | 现行版本未复核；Phase 04 按"损毁偏航+可修"设计 |
| NavalArt 密度公式 | C1 反编译 `armor×0.008` | 采纳为 NT 建造计重参考（DESIGN DECISION） |
| 125HP/人 | B1 示例推导 | 与 wt_reference 一致，维持（FACT） |
