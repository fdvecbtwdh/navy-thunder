# MDR-0005: 爆炸 / 破片 / 超压

- 状态: 已决议（结构高置信；数值公式未公开 → 参数化）
- 参考优先级: War Thunder（战斗计算）

## 决议

爆炸三成分：**冲击波 + Brisant（粉碎）效应 + 破片喷射**。

- **破口判定**：HE 能否穿破爆心最近板（或穿透路径末端的板）决定内爆/外爆；能穿透 → 破片在 ~30–45° 锥（`calibration/fragment_cone_half_angle_deg=37.5`）内无阻碍喷射；不能 → 只有外板破坏。
- **破片**：数量/初速公式未公开 → 参数化生成（按命中角 0–30°/30–90°/90–180° 三段分配，datamine `shatter` 块证实分段结构）；破片对后续板做穿深判定。
- **超压（overpressure）**：仅 HE 系触发（HEAT 射流、HESH spall 不触发）；APHE 需 **≥170g TNT 当量**（`wt_reference/overpressure_aphe_tnt_threshold_kg=0.17`）。**海战边界（关键）**：对舰只超压只作用于开放式模块（主炮/副炮/防空/测距仪/火控）的暴露炮组；**舱内乘员与模块免疫**。

## WT 行为与证据

- 官方 wiki《The Mechanics of High-Explosive Effect and Overpressure》（2025-07-31 更新）：三成分、破片锥 30–45°、Ka-Chi 多层板案例、超压规则全文。
- datamine：`shatter { useRealShatters, countPortion, realShattersSplicing }` + 命中角三段（Mk8 的 30–90° 段 `onHitChanceMultFire=100`）。
- 2017 官方海战测试公告：舱室浮力损失按百分比合计、单侧进水致横倾。

## NavalArt 参考

1.52（2026-09-02）伤害模型重做：逐层装甲衰减爆炸伤害、厚甲对低伤免疫、小口径 HE 半径加大。**借鉴其"逐层衰减+免疫阈值"思想用于爆炸-装甲交互的备选实现**；战斗数值让位 WT。

## 历史变化

- 2021-03（2.5 Ixwa Strike）HE/超压体系重做。
- 2023-03 Sky Guardians 修复舰船破片伤害偏低。
- 2024-03 Alpha Strike 改海军破片分配方案（最大伤害不变、分布重排）。

## 未知点

破片数-装药公式、破片初速、超压半径-炸药量公式、爆口尺寸公式（stat card 层有展示值但无公式）。

## 实现与替换方案

- Phase 2：`ExplosionSystem`（blast 衰减 + brisant + 破片锥生成）+ `OverpressureRules`（HE-only、170g 门槛、露天限定）。
- 全部数值进 calibration；`NavalArt` 式逐层衰减作为爆炸 vs 多层装甲的默认交互（两层机制一致时无冲突）。
