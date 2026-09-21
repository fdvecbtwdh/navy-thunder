# 授权审计清单（R5.3）

书面化时点：2026-09-22。项目整体：GPL-3.0（见仓库 LICENSE）。

## 1. 代码与资产来源

| 组件 | 来源 | 授权 | 审计结论 |
|---|---|---|---|
| NavyThunder.Core/Data/SimRunner/前端 | 本项目原创 | GPL-3.0（随项目） | 无外部授权约束 |
| 程序化音效（Sfx.cs 合成） | 本项目运行时生成，零采样资产 | GPL-3.0（随项目） | 无第三方素材，无授权风险 |
| 参数化船体网格（tools/generate_hull_obj.py） | 由舰队参数表程序化生成 | GPL-3.0（随项目） | 非扫描/非提取几何，无风险 |
| Godot 4.7.2 引擎（编辑器+运行时+export templates） | godotengine.org | MIT | 商用/分发合法；export templates 随引擎同授权 |
| DagorEngine 参考（格式逆向时只读源码比对） | github.com/GaijinEntertainment/DagorEngine | BSD-3 | 仅作格式知识参考，未复制代码；无授权传染 |
| klensy/wt-tools、社区工具 | 仅作格式知识参考 | 各自开源授权 | 未复制代码 |

## 2. War Thunder 客户端提取物（关键授权假设，书面化）

**提取内容**：`data/reference/wt_ship_units.json`（2,322 舰参数：排水量/航速/武器摘要）、
`data/reference/wt_ship_weapons.json`（24 舰实测射速/弹重/口径/旋回/弹种数据）、
`data/ships/generated_fleet.json`（由上述参数+史实参数经模板程序化生成的舰船定义）。

**授权假设（本项目立场，须持续成立）**：
1. 数据提取自用户本人合法拥有的 WT 客户端副本，仅用于个人研究/非商业项目，
   遵循 Gaijin Entertainment EULA 中"不商业分发游戏素材"的边界假设。
2. 提取物仅为**数值参数与字符串**（不含模型/贴图/音效/文本等创意资产）；
   数值事实（排水量、射速）不受版权保护，但整理格式存在最低限度创造性。
3. **分发的隐含承诺**：1.0 发布时游戏数据包中不含任何 WT 客户端原始文件
   （blk/vromfs/模型/贴图/音效均零包含）；第三方使用者需自行从自己的客户端
   重新提取（tools/ 管线提供完整可复现路径）。
4. 若 Gaijin 明确反对，承诺移除相关提取数据并回退到纯史实手工参数
   （FLEET 表 + 史实值即为该回退形态）。

**禁止项**：不得分发 `data/reference/wt_*.json` 于任何商业渠道；不得分发 WT
客户端本身或其解包素材包；不得使用 WT 商标作推广。

## 3. 待办（R5.4 前完成）

- [ ] dist 包内容审计：确认无 WT 原始文件混入（package_release.sh 打包后核对清单）
- [ ] LICENSES/ 目录汇总第三方授权全文（Godot MIT、DagorEngine BSD-3）
- [ ] 关于页/README 声明数据来源与重建方法
