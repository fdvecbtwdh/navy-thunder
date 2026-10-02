# NavalArt 机制研究笔记（建造系统设计基准）

> 研究目的：为 Navy Thunder 舰船建造系统（Phase 06）与物理/伤害模型提供 NavalArt 一手依据。
> **合规声明**：本文所有结论来自 **NavalArt 本机客户端反编译（Level C，研究用途）与本机文件读取**，仅限本机研究；未复制任何代码/资源进 Navy Thunder 仓库，所有逻辑描述为研究者自己的话总结。反编译产物保留在本机 `_na_decomp\`（不提交 git）。与 MDR-0016（Steam 官方页面一手信息，Level B）互为补充。
> 反编译工具：ilspycmd 11.1；源：`Assembly-CSharp.dll`（Unity Mono）+ `NAModToolCore.dll`。抓取日期：2026-10-02。

---

## 1. .na 文件格式（存档）

FACT（本机文件读取 + `ShipSaveHelper`/`Builder_Camera.SaveShip` 反编译佐证）：
- **.na 是未加密明文 XML**（UTF-8 BOM），可直接文本编辑。
- 结构：`<root ship author description hornType hornPitch tracerCol>` + 每部件一个 `<part id="N">`。**部件 id = 原版零件 prefab 名**（字符串数字），对应 `Localization\parts.csv` 的 `nN` 键（共 1,629 个原版零件：n0=可调船体、n1=1/2 船体、n7=螺旋桨、n12=203mm 双联装炮塔…）；mod 部件带 `modname`。
- 部件通用子元素：`position`（**世界坐标**）、`rotation`（欧拉角）、`scale`（**镜像用负缩放**）、`color hex`（可带 gloss/自发光标记）、`armor value`（mm）。
- 专用扩展：id=0 可调船体存 10 参数 `<data length/height/frontWidth/backWidth/frontSpread/backSpread/upCurve/downCurve/heightScale/heightOffset>`；id=701 顶点编辑船体存 8 顶点 `<vertexData>`；另有炮塔手动控制/自定义射界、旗帜、索具、配重、弹射器角度、参考图。
- 部件级属性：`ignorePhysics`（纯装饰）、`instanceId/mirroredPartId/parentId`（组/镜像/组合件）。
- `<squadron>` 存舰载机队（机型/涂装/编制/技能）；任务文件 `.namission` 也是 XML（节点图+触发链，`spawnName` 引用任务文件夹里的普通 .na 船档）。
- **存档不存任何派生数值**（重量/HP/吃水/价格全部加载后按几何实时重算）。

对 NT 的直接启发：**存档=部件实例表（类型+位姿+装甲+颜色），派生值不落盘**——杜绝"改装甲忘改重量"类失同步，天然向后兼容。

---

## 2. 建造数据模型

FACT（`PartAttribute`、`AdjustableHull`、`VertexEditHull`、`Physics_Manager`）：
- 每部件 = prefab 实例 + BoxCollider + `PartAttribute`。核心字段：`armor`（默认 50mm）、`density/volume`、`overrideWeight/overrideHP`、`price`、`year`、`cubeClass`（Hull/Weapon/Engine/Decoration/Module/Aircraft/Hide/CustomParts）、`weaponType`（Turrets/Torpedos/AAGuns/DPGuns/Missiles/CIWS/ASW/Others）。
- **重量公式：weight = density × Volume**；非模块原版件密度不由玩家填，而是 **density = armor × 0.008（钳 0.1~100）——装甲越厚自动越重**。`overrideWeight/overrideHP` 是特殊件逃生口。
- 船体拼装：8 参数程序化分段（前后宽度/外飘/上下曲线拟合舰艏球艉），相邻分段 `forwardHull/backwardHull` 引用对齐；块间无拓扑约束，纯坐标对齐。
- 镜像：负 scale 实现左右对称，省一半工作量。
- 组合件：`CustomPart` 打包多部件，加载时展开。

---

## 3. 装甲模型

FACT（`PartAttribute.CalculateData`、`ArmorVsProjectileSolver.GetEquivalentArmor`）：
- **装甲 = 部件上的一个标量（mm）**，没有独立的装甲带/装甲盒对象；重甲区=把那片块的 armor 调高（Fuso.na 实测：1,500+ 块默认 50mm、359 块 16mm 上层建筑、最高 356mm 炮塔/座圈）。
- **等效装甲**：`armor / max(0.33, cosθ)`（斜面增益封顶 3 倍）。
- **重量↔HP↔密度三联动**：armor 同时决定密度（重量）、对全船耐久池的贡献（`armor^0.3 × Volume × 系数`）、自体 HP（= 2×toughness）。
- **弹药库是独立部件 `Part_Dyk`**（"DyK"=拼音弹药库）：被毁时对全船耐久池追加爆发伤害并**连锁摧毁同炮塔的 feeder（供弹井）与炮塔本体**——殉爆是**结构可达性**结果，不是随机数。

对 NT 的裁决（`DESIGN DECISION` 预告，详见 PROJECT_DESIGN §10）：借鉴"密度由装甲推导"与"供弹链结构"；**不照抄**"每部件一个标量"（NT 保留显式装甲板/装甲区概念，与 WT 对齐）。

---

## 4. 浮力 / 沉没物理

FACT（`Data_Manager.CalculateShipData`、`Ship_Manager`）：
- **静态求解**（初始化一次）：遍历部件收集 (高度, 体积) 对；总重=Σ密度×体积；CoM=密度加权平均；CoB=体积加权平均；**静态吃水=体积-高度阶梯曲线的自低向高累积求逆**（1D 静水力逆解，无体素）。
- 汇总 24 项 shipData：重量、maxHp（Σtoughness 模块/非模块分权）、总浮量、包络、`shipDrag = (H×W)^0.06 − 0.74` 等。
- **速度涌现**：无"最高航速"字段——总推力（多机收益递减幂律合成）− 水面二次阻力 − 超过兴波界限 `√L × 2.43` 后的平方阻力；加速/满舵时间也是吨位幂律。
- **运行时浮体**：Rigidbody + **4 个浮力点（艏/艉×左右）**，每点力=浸深×0.25×g×总浮量+阻尼+离水/落水项；海浪仅大风速生效。
- **损伤进水**：命中点按相对船体盒中心的**象限**归属，`buoyancyIndex[q] -= damage/(2×maxHp)`——单侧浮力永久下降 → 横倾/艏倾由物理涌现。
- **沉没判定**：全船 HP 池归零 → Sunk()；**随机选 4 种沉没姿态之一**（艏倾/艉倾/左倾/右倾），对应侧两象限浮力 60s 线性降到 −0.2；另有横摇 ≥90° 直接判沉。
- 潜艇：`Part_SubTank` 配重下潜。

对 NT：借鉴四象限浮力指数（一个 float[4] 做出横倾戏剧性）与 1D 吃水求逆；**不照抄**随机沉没姿态（NT 沉没应从损伤分布涌现）与"damage/2maxHp 均匀折扣"（NT 按实际舱室）。

---

## 5. 部件清单（部件广度基准）

| 类别 | 关键字段/机制 | 对 NT 启发 |
|---|---|---|
| 船体 | 8 参数分段 + 8 顶点变形、镜像 | 块级连续变形手感核心 |
| 主机 | 只给 power，速度涌现 | 功率-阻力模型 vs NT 的 MaxSpeedKnots 指令模型（NT 保留指令制+浮态惩罚） |
| 螺旋桨 | force/radius/rpm + **浸水率**（尾沉掉速） | 浸水率值得吸收 |
| 舵/鳍 | maxAngle/**area**（舵效面积化）、减摇鳍 | 直接可抄 |
| 主炮 | caliber/spreadX,Y/traverse/俯仰界 max-minAngle/heDamage/apDamage/reloadTime/bulletSpeed/穿深修正/弹重覆盖/低伸-高拋双弹道/dpGunMode | 炮塔-供弹井-弹药库三件套独立可损 |
| 防空 | maxAAAngle=60°、maxAARange≈7160、isCIWS | **AA 走独立 dps 通道，不进穿深公式** |
| 鱼雷 | cruiseHeight=−2、maxDamage=8000、penetrationIndex | 水下巡航高度参数 |
| 导弹 | LauncherTube[]（数量/预热/筒盖/碰撞延迟） | VLS 完整支持 |
| 探测 | Probe/测距仪/旋转雷达 accuracyGain：`4/(√min(9,g)+2)` | **探测=精度修正而非视野** |
| 航空 | 跑道 800m 门槛/弹射器/机库容量 10/直升机坪 | 甲板长度真的影响起降 |
| 特殊 | 配重（可调密度压载）、Dyk、Feeder、SubTank | 配重让玩家手动配平 |

损伤判定（`PartAttribute.PartDamaged`）：单发伤害 ≤ 2×自体 HP → 瘫痪入维修队列（40% 起火）；> 2× → 永久摧毁（90% 起火、炮塔炸飞成刚体）；**舵/桨豁免摧毁只瘫痪**（保命余地）。

---

## 6. 战斗/伤害模型（1.52 版，仅参考不采用数值）

FACT（`ArmorVsProjectileSolver`、`WeaponConfig`）：
- 配置层级：WeaponConfig(SO) → 按弹种取 AVPConfig → 按 AP/SAP/HE 取 AmmunitionAVPConfig。
- **穿深 = 1940 年代美海军幂律经验公式**（弹重磅×口径^−0.65×速度^1.1 × 穿深修正），**没有 Krupp/de Marre**；弹重默认圆柱体积公式。
- **逐层穿甲**：RaycastAll ≤100 命中排序，逐层结算；剩余速度 `v' = v·√(1−(装甲/穿深)^1.1)`；穿不透=NonPen；穿透到底引信没炸=**OverPen，总伤 1/30**，每个被穿模块各吃 damage/30。
- **爆炸结算**：OverlapSphere 候选 → 对每部件反向 raycast 收集遮挡层（≤8 层，0.05m 内合并取最厚）→ 逐层衰减 `T=armor^m2/m1`、`D'=D(1−(T/D)^p)`（m1/m2/p 免疫三参数，官方甚至提供样本反解拟合工具）→ 模块按距离平方权重直接吃，非模块共享池；对已毁部件耐久池只吃 1/6 过量伤害。
- **全船耐久池**：直接扣减的"沉没进度条"，无部位-沉没判定；战力损失走模块摧毁。
- 命中反馈：伤害贴花投影到 per-ship 1024² damage texture（透明度随装甲变淡）+ 击穿/过穿/未穿/HE 四种文字+镜头；弹药库"核心区"震屏。
- 全部伤害**服务器权威**（Mirror），客户端只收 RPC 表现。

对 NT：MDR-0005 已裁决"逐层衰减+免疫阈值"思想作为爆炸-装甲交互备选——本节给出其完整实现形态（遮挡层 raycast + 三参数），Phase 04 评估是否引入第二参数组。**数值不采用**（NT 用 de Marre 官方公式，Level B 证据更强）。

---

## 7. 保存/版本兼容

FACT（`SaveShip`/`LoadShip`）：
- 保存：强制 en-US culture（小数点）；同步缩略图；自动 QuickSave。
- 加载：XmlReader 容错；**逐部件 try/catch，单个部件损坏只跳过并上报**；所有数值 `ParseAttribute(key, default)`。
- **无版本号字段**——兼容 = 属性缺省兜底 + 未知部件跳过 + modname 提示。

对 NT：借鉴容错加载；**不照抄无版本号**（NT 存档必须带版本号+迁移器，破坏性 schema 变更才可能）。

---

## 8. ModTool 参数面（官方认可的参数化白名单）

FACT（`NAModToolCore.dll` 反编译 + ModTool 教程 PDF 0.9/Port docx）：
- PartAttributeProxy：id/armor/density/price/builderClass/weaponType/nation/basePartId（变体）。
- PartTurretProxy：caliber/rotateSpeed/range/bulletSpeed/loadTime/俯仰界/shellWeight/散布/muzzles/**localDeadZones（死区）**/heMaxDamage=1000/apMaxDamage=1500/apPenetrationDepthModifier/isDpGun/aa 参数/音效替换；Inspector 暴露 HE/AP 跳弹角限制（85/89°、45/60°）。
- PartTorpedoLauncherProxy：caliber/range/速度(节)/canLaunchUnderwater/cruiseHeight/maxDamage=8000。
- PartAAGunProxy：maxAngle=80/rpm=400/dps/isCIWS。
- PartMissileProxy：喷速/助推分离/机动开始/最大速度/过载/射程(km)/巡航高/弹头类型/拦截率；发射管参数。
- PartThrusterProxy（直径/螺距/叶数/maxRpm）、PartRudderProxy（maxAngle/area）、PartEngineProxy（power）、PartRadarProxy。
- **边界**：官方只开放部件级参数；伤害公式系数、装甲免疫参数、浮力/HP 公式指数全部锁死——mod 改内容不改规则。

对 NT：NT 的数据 schema（`data/ships|shells|...`）按这张白名单的**广度**设计字段面，但用 JSON + 校验器而非编辑器内嵌。

---

## 9. Navy Thunder：借鉴 vs 不照抄（裁决清单）

**应借鉴**：
1. 存档=明文结构化文本+派生值不落盘（防失同步、可 diff、天然兼容）。
2. 重量=体积×密度、密度由装甲推导（一条公式解决平衡/配重/浮心；玩家永远造得出能浮的船）。
3. 部件自体 HP 与全船池双轨（战斗反馈与生死判定解耦）→ NT 形态：部件 HP（已有）+ 储备浮力当生死池（对齐 MDR-0008）。
4. LOS 等效装甲+逐层穿透+引信阈值→过穿/半伤（NT 已有等价链：ArmorResolver+Fuze；参考其 1/30 过穿伤门槛的"轻甲活得下来"手感）。
5. 四象限浮力指数+物理涌现倾斜（Phase 04 横倾的候选实现）。
6. 静态吃水=体积-高度阶梯求逆（1D，无需体素）。
7. 船体块连续参数变形+镜像对（建造器手感核心）。
8. 速度上限与船长挂钩（兴波界限 √L 硬约束值得保留为校核规则）。
9. 炮塔射界=分区可用性+局部死区+自定义射角（Phase 06 射界字段设计输入）。
10. 弹药库-扬弹井-炮塔供弹链：殉爆=布局问题（NT 已有 TurretGroup 链，补"结构可达"语义）。
11. mod 参数白名单模式（NT 数据 schema 广度参照）。
12. 探测=精度修正、AA=独立 dps 通道（避免万物进穿深公式）。

**不应照抄**：
1. 世界坐标存部件位置（父物体移动/精度漂移；NT 用局部坐标+稳定 GUID）。
2. damage/(2×maxHp) 均匀象限折扣（与实际舱室无关；NT 按舱室/破口）。
3. HP 归零后随机沉没姿态（与损伤分布脱节；NT 从累计损伤涌现）。
4. 每部件单一 armor 标量（玩家负担重；NT 保留显式装甲板/区）。
5. 不可读幂律公式群（NT 公式必须"可解释、面板可见"）。
6. maxHp 隐式池（NT 用显式储备浮力）。
7. ScriptableObject 内嵌配置（NT 全 JSON 热改）。
8. Unity 网格合并/贴图集实现（技术不相关）。
9. God class/魔法数/半成品残留（反面教材）。
10. 无版本号隐式兼容（NT 存档带版本+迁移器）。

---

## 10. 来源清单

**反编译源**（Level C，产物本机 `_na_decomp\`，不提交 git）：
- 数据/物理：`Data_Manager.cs`（CalculateShipData/CalculateStaticDraft）、`PartAttribute.cs`、`AdjustableHull.cs`、`VertexEditHull.cs`、`ShipConfig.cs`
- 战斗：`Zig.ArmorVsProjectile\ArmorVsProjectileSolver.cs`、`AmmunitionAVPConfig.cs`、`Ship_Manager.cs`（Damage/ApplyDamage/Sunk/浮体/推进）、`Physics_Manager.cs`、`Weapon_Turrent.cs`、`Weapon_AntiAir.cs`、`Module.cs`、`Part_Dyk.cs`、`TurretDeadzone.cs`
- 部件：`Part_Engine/Propeller/Rudder/Fin/Feeder/Runway/Hangar/SubTank/Catapult/Counterweight/RangeFinder.cs`
- 存档：`ShipSaveHelper.cs`、`Builder_Camera.cs`、`ShipSaveQuotationCalculator.cs`
- ModTool：`NAModToolCore.dll` 反编译（PartTurretProxy 等 23 类）

**本机数据文件**（Level C）：
- `NavalArt_Data\Presets\SaveFolder.json`、`Presets\List.csv`（264 预设舰）、`Localization\parts.csv`（1,629 零件）
- `Missions\Encounter Battleship Yamato'41\*.namission` + `Ships\Yamato1941.na`
- `ModTool\NavalArt ModTools Tutorial(095).pdf`、`Port ModTool.docx`、`NAModTool 0.20.4.unitypackage`
- 工坊样本：`D:\SteamLibrary\steamapps\workshop\content\842780\1628969974\IJN Fuso.na`、`2398454220\IJN YAMATO 大和.na`

**数值边界**：WeaponConfig/ShipConfig 具体曲线在 Unity ScriptableObject 资产内，反编译只取得字段面与代码默认值（FuseTime=0.035、gravityConstant=2.5、DykDamageRatio=1、maxAARange=7160 等）；未取得的曲线值不做臆测。
