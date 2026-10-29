# 多 Mod 启动进入主菜单为什么特别长（本轮实测 + 反编译分析）

分析日期：2026-10-21（Asia/Hong_Kong）。对象：本机 `2.7.0x`、UMM `0.32.4.0`、当前 52 个 UMM Mod + 4 个 Owlcat Modifications（OMM）Mod 的组合。

本文把三类证据分开写：

- **本轮实测**：2026-10-21 23:27:51 启动的进程（PID 37504），来自 `GameLogFullPrev.txt` 的原生时间戳、`Player-prev.log` 和 ModTimer 汇总。UMM 自己的 `Log.txt` 已被下一次启动截断，汇总已冻结在 `analysis/runtime/20261021-232751/`。
- **静态分析**：`analysis/decompiled/` 下的游戏/UMM/Mod 源码，以及用 dnlib 做的只读元数据普查；没有执行 Mod 代码。
- **历史对照**：2026-05-02 的旧日志（112.5 秒）只用于说明结构，不作为本轮基线。

## 0. 结论摘要

1. 进程启动到 `MainMenu.Awake` 实测 **84.27 秒**；ModTimer 从它自己载入时开始计时，得到 **83.03 秒**。
2. 其中 **63.80 秒（75.7%）** 可以直接归因到 Mod：
   - UMM Mod 的 `ModEntry.Load`：**27.93 秒**（UMM 部分）
   - OMM Mod 的 `OwlcatModification.Apply`：**4.64 秒**
   - 挂在 `BlueprintsCache.Init` 上的 Mod postfix：**28.48 秒**
   - 挂在 `StartGameLoader.LoadPackTOC` 上的 Mod patch：**1.70 秒**
   - 挂在 `LocalizationManager.OnLocaleChanged` 上的 Mod patch：**1.06 秒**
3. 主菜单本身不是瓶颈：`MainMenu.Awake` 从开始到结束只有 **0.008 秒**。时间全部花在它之前的串行启动流水线上，玩家看到的是启动画面/进度条长时间不动。
4. 最大的一段是**蓝图库阶段**：`Init Library` 到 `Loading Library Finished` 共 **44.89 秒**，其中 30.18 秒是 Mod 的 patch 体；游戏自身的 TOC、蓝图 JSON、字体等约 8.6 秒。
5. “多 Mod”会被四类共享成本放大，而不是简单相加：
   - 多个 Mod 各自内嵌一份 BlueprintCore 之类的大型库（已安装程序集普查 **60,308** 个重复类型，本轮实际加载约 54,000 个）；
   - UMM、Harmony、Cheats、WrathPatches、Unity 更新器/Volume/Astar 都会**遍历所有程序集的全部类型**；
   - 每个蓝图 `Load` 都要在**全局锁**下反序列化、`OnEnable`，并对每个 OMM Mod 走一遍回调；
   - 若某个 Mod 扫描全蓝图库或预加载数万条蓝图，成本随“总蓝图数 × 扫描 Mod 数”增长。

## 1. 本轮运行与测量口径

### 1.1 运行条件

| 项 | 值 |
|---|---|
| 进程/PID | `Wrath.exe` / 37504，启动于 2026-10-21 23:27:51 |
| 游戏版本 | `2.7.0x`（检测为 2.7.0） |
| UMM | 0.32.4.0 |
| UMM Mod | 52 个（`Mods/` 下 53 个目录，含运行中安装的 `!!!FirstStart`） |
| OMM Mod | 4 个启用（GraciousFriendships、HomebrewArchetypes、AC、ModWithNoBalance）+ DragonChanges 内置的 WoolooMod |
| 采样工具 | ModTimer 1.4.0；ManyModsPerformanceFix 1.8.5（启用） |
| 主菜单时工作集 | 约 3.998 GB（23:30 采样） |

### 1.2 证据来源

| 证据 | 用途 | 限制 |
|---|---|---|
| `GameLogFullPrev.txt` 原生 `[秒 - 频道]` 时间戳 | 整条启动流水线的绝对时间轴 | 已被下一次启动轮转；关键行已冻结到 JSON |
| ModTimer 汇总 | 按 Mod、按阶段的耗时 | 只测 Mod 的 patch 体，不测游戏方法体 |
| `Player-prev.log` | UMM 控制台输出的完整副本 | 不含 PFLog 的原生时间戳 |
| 反编译源码 | 解释“为什么” | 反编译是推断，不等于原始源码 |
| dnlib 元数据普查 | 统计类型/方法/Patch 类/重复库 | 静态计数，不等于运行时 JIT 数 |

冻结文件：

- `analysis/runtime/20261021-232751/modtimer-summary.json`：169 条 ModTimer 明细 + 四个阶段总计
- `analysis/runtime/20261021-232751/native-startup-timeline.json`：原生时间戳关键点与阶段差值
- `analysis/runtime/20261021-232751/Player-prev.log`：上一轮 Unity 日志（2.4 MB）
- `analysis/mod-metadata-census-20261021.csv`、`analysis/mod-library-census-20261021.csv`

## 2. 本轮时间轴（原生日志）

时间单位：秒，相对进程/日志开始。

| 时间 | 事件 | 距上一节点 |
|---:|---|---:|
| 0.00 | 进程/日志开始 | — |
| 29.39 | `GameStarter.Awake Logs initialized`（UMM 前缀结束，Mod 全部 Load 完） | 29.39 |
| 29.85 | `GameStarter.Awake finished` | 0.46 |
| 34.03 | `GameStarter.StartGame started` | 4.18（未打点区间） |
| 34.49 | `Loading common bundles` | 0.46 |
| 36.33 | `Init Library`（`LoadDirectReferencesList` 前） | 1.84 |
| 37.82 | `Load modifications' descriptors`（OMM `Start()` 开始） | 1.49 |
| 38.60 | `Apply modification: HomebrewArchetypes` | 0.78 |
| 42.42 | OMM 四个 Mod 全部 Apply 完 | 3.82 |
| 81.22 | `Loading Library Finished` | **38.80** |
| 82.89 | `Localization Initialized` | 1.67 |
| 83.66 | `MainMenuView` 加载完 | 0.77 |
| 84.27 | `MainMenu.Awake` 开始/结束 | 0.61 / **0.008** |
| 85.63 | `GameStarter.StartGame finished` | 1.36 |

阶段汇总：

| 阶段 | 时长 | 其中可归因 Mod |
|---|---:|---:|
| 进程开始 → UMM Mod 载入完成（0→29.39） | 29.39 s | 27.93 s（UMM `ModEntry.Load`） |
| `GameStarter.Awake` 方法体（29.39→29.85） | 0.46 s | — |
| `Awake` 结束 → `StartGame` 开始（29.85→34.03） | 4.18 s | —（未打点） |
| `StartGame` 开始 → `Init Library`（34.03→36.33） | 2.30 s | — |
| `LoadDirectReferencesList`（36.33→37.82） | 1.49 s | — |
| OMM `Start()`（37.82→42.42） | 4.60 s | 4.64 s（OMM `Apply`） |
| TOC + 蓝图 JSON + 字体（42.42→81.22） | 38.80 s | 30.18 s（BP Init 28.48 + LoadPackTOC 1.70） |
| 本地化等待（81.22→82.89） | 1.67 s | 1.06 s |
| 场景/主菜单 UI（82.89→84.27） | 1.38 s | — |
| **合计到 MainMenu.Awake** | **84.27 s** | **63.80 s（75.7%）** |

> ModTimer 的 83.03 s 从它自己载入时开始（约 1.24 s），所以 `1.24 + 83.03 ≈ 84.27`，与原生时间戳对得上。

## 3. ModTimer 分项

### 3.1 四个阶段

| 阶段 | 总计 | 占 83.03 s |
|---|---:|---:|
| `ModEntry.Load / OwlcatModification.Apply` | 32,569 ms | 39.2% |
| `BlueprintsCache.Init`（仅 Mod patch 体） | 28,475 ms | 34.3% |
| `StartGameLoader.LoadPackTOC`（仅 Mod patch 体） | 1,700 ms | 2.0% |
| `OnLocaleChanged`（仅 Mod patch 体） | 1,057 ms | 1.3% |
| **Mod 合计** | **63,801 ms** | **76.8%** |

### 3.2 Top ModEntry.Load / OMM Apply

| Mod | ms | 说明 |
|---|---:|---|
| ToggleableThrowingWeapons | 6,658 | 内嵌 BlueprintCore，`PatchAll` + `PostPatchInitializer` |
| ModTagEx | 4,629 | 同步更新检查 + 读 GUID/TOC + PatchAll |
| HomebrewArchetypes（OMM） | 3,808 | OMM `Apply`，走 `LoadBlueprints`/入口 |
| VoidHeadWOTRNineSwords | 2,190 | 内嵌 BlueprintCore |
| ToyBox | 2,068 | 315 个 Patch 类 |
| RanRomance | 1,349 | 内嵌 BlueprintCore |
| SparsSorcerousSundries | 1,189 | 内嵌 BlueprintCore |
| PrestigePlus | 1,124 | 内嵌 BlueprintCore |
| Epilogue | 1,103 | 内嵌 BlueprintCore |
| GraciousFriendships（OMM） | 770 | OMM `Apply` |
| TomeOfTheFirebird | 732 | 内嵌 BlueprintCore |
| MythicMagicMayhem | 713 | 内嵌 BlueprintCore |
| PerformanceImprove（本工作区） | 603 | 诊断/补丁初始化 |

### 3.3 Top BlueprintsCache.Init（Mod patch 体）

| Mod | ms | 说明 |
|---|---:|---|
| CustomNpcPortraits | 5,416 | 复制头像 + 加载图片 + 全蓝图库 `popUnitNames` |
| TabletopTweaks-Base | 3,726 | 新增 1,439 个蓝图 |
| ExpandedContent | 3,070 | 新增 4,084 个蓝图 |
| CodexLib | 2,815 | 预加载 45,652 个蓝图 |
| PrestigePlus | 2,583 | 内嵌 BlueprintCore 内容 |
| KRXLib | 1,770 | 库初始化 |
| RandomReinforcementsPerEncounter | 1,651 | 内容生成 |
| DragonChanges | 1,216 | 内容生成 |
| TomeOfTheFirebird | 1,169 | 内容生成 |
| TabletopTweaks-Core | 873 | 核心库初始化 |
| VoidHeadWOTRNineSwords | 767 | 内嵌 BlueprintCore |
| RanRomance | 636 | 内嵌 BlueprintCore |
| Epilogue | 463 | 内嵌 BlueprintCore |

### 3.4 LoadPackTOC / Locale

| 阶段 | 主要 Mod | ms |
|---|---|---:|
| `LoadPackTOC` | MythicMagicMayhem 770、TTW-Core 662、PrestigePlus 97 | 1,700 |
| `OnLocaleChanged` | KRXLib 672、ExpandedContent 225、VoidHead 45、RanRomance 40 | 1,057 |

## 4. 启动链与源码定位

### 4.1 UMM 在 `GameStarter.Awake` 之前串行载入全部 Mod

`UnityModManager/Config.xml` 把 `StartingPoint` 配成 `[Assembly-CSharp.dll]Kingmaker.GameStarter.Awake:Before`。因此 UMM 的 `_Start()` 是 `Awake` 的 Harmony 前缀，整个 Mod 载入在主线程上完成，游戏自己的进度条此时还没有开始推进。

- `analysis/decompiled/UnityModManager/UnityModManagerNet/UnityModManager.cs:6441` `_Start()`
- 同文件 `:6454` 起枚举 `Mods/*/Info.json`，`:6547` 排序，`:6550` 起逐个 `Active = true`
- 同文件 `:769` `ModEntry.Load()`：`Assembly.LoadFrom`（或 `.cache` 副本）、首次载入时 `mAssembly.GetTypes()`（`:937`，用于扫描 `EnableReloadingAttribute`），最后反射调用入口（`:1028`）
- 同文件 `:937` 的 `GetTypes()` 会把该 Mod 程序集里的**全部类型**加载进运行时；对 5,000+ 类型的 Mod 来说，这本身就是数百毫秒级的工作。

这解释了玩家体感：点启动后长时间卡在 Logo/启动画面，因为 UMM 在 `Awake` 之前就把主线程占住了，游戏连“Loading common bundles”都还没开始。

### 4.2 每个 Mod 自己的 Load 在做什么

- `ToggleableThrowingWeapons/Main.cs:15`：`new ModContextTTW`、`harmony.PatchAll()`、`PostPatchInitializer.Initialize(...)`；程序集有 5,490 个类型。
- `TabletopTweaks-Base/Main.cs:15`：同样 `PatchAll` + `PostPatchInitializer`；程序集 1,330 个类型，但 `PatchAll` 要扫全部类型/方法并应用 176 个 Patch 类。
- `ModTagEx/Main.cs:22`：先 `Updater.Update`（同步 HTTPS 检查 GitHub，见 `Updater.cs:15/47`，超时 5 s），再 `LoadModBlueprintGuids` 读 `all_blueprints.txt`（25,121 条），再 `LoadBaseGameGuids` 用 `BinaryReader` 把 **236,661** 条 TOC GUID 读进 `HashSet`（`Main.cs:59`），最后 `PatchAll`。
- `OwlcatModification.cs:160`：OMM 的 `Apply()` 依次 `LoadAssemblies`、`LoadBundles`、`LoadBlueprints`、反射找入口；HomebrewArchetypes 的 3.8 秒就走这条路径。
- `OwlcatModification.cs:227`：每个 `Blueprints/*.jbp` 都 `BlueprintJsonWrapper.Load` → `TryPatchBlueprint` → `OnEnable` → `AddCachedBlueprint`，是逐文件、逐蓝图的同步工作。

### 4.3 全局反射扫描：类型越多越慢

这类成本不隶属于某一个 Mod，而是所有已加载程序集的总和：

1. **CheatsManager**（`Core.Cheats/CheatsManager.cs:99`）：遍历 `AppDomain.CurrentDomain.GetAssembliesSafe()` 的每个程序集、每个类型、每个静态成员，再对每个候选成员调用 `GetCustomAttribute<CheatAttribute>()`。构造函数 `:132` 还会先 `CollectAll()` 一次，再 `GetInternals()`（`:135`）在静态缓存为空时再扫一次。
   - 本轮 `CheatsManager Constructor took 0ms`，因为 `ManyModsPerformanceFix` 用缓存注入了它（`CheatsCache.cs`）。**这不是“本来就不慢”，而是已经被第三方 Mod 绕过。**
2. **UMM 的 reload 扫描**：`ManyModsPerformanceFix` 的 `UmmReloadCache.cs` 专门给 `ModEntry.Load` 里的 `Assembly.GetTypes()` 做缓存，目标是避免每个 Mod 都重新扫全部类型找 `EnableReloadingAttribute`。
3. **Harmony `PatchAll`**：每个 Mod 都会扫描自己程序集的所有类型和方法，找 Harmony 特性并构造 `PatchClassProcessor`。`PatchAllCache.cs:217` 在缓存命中时用元数据 token 重建 processor，跳过 `AccessTools.GetTypesFromAssembly`（`:236`）；这就是社区对“多 Mod 时 PatchAll 太慢”的直接回应。
4. **WrathPatches 的 Guid 绑定**：`WrathPatches.UmmModsToGuidClassBinder` 会把每个 Mod 程序集里的 `[TypeId]` 类型加入 `GuidClassBinder`；`WrathPatchesBinderCache.cs` 缓存这一扫描。
5. **Unity/Owlcat 运行时的类型发现**：`RuntimeTypeDiscoveryCache.cs:77-90` 给 `UpdateCaller.OnAfterFirstSceneLoaded`（找 `IUpdatable`/`ILateUpdatable`）、`VolumeManager.ReloadBaseTypes`（找 `VolumeComponent` 子类）、`AstarData.FindGraphTypes` 做缓存。这些方法原本都会 `Assembly.GetTypes()` 遍历全部程序集。
6. **本地化标签扫描**：`LocalizationTagCache.cs` 给 `TagEncyclopediaEntries` 做缓存，避免每次启动重新扫描百科全书/描述标签。

这些缓存文件就在 `Mods/!ManyModsPerformanceFix/`：`PatchAllCache.json`（约 1.0 MB）、`RuntimeTypeDiscoveryCache.json`（约 301 KB）、`UmmReloadCache.json`、`WrathPatchesBinderCache.json`、`LocalizationTagCache.json`、`CheatsCache`（`Cache.json`）。本轮它们的 mtime 都在 23:28–23:29，说明启动过程中确实在读写。

> 旁证价值：如果这些扫描不贵，社区不会为它们分别写缓存、反序列化 Harmony 内部结构、甚至用 transpiler 替换 Unity 的扫描。它们的存在本身就是“多 Mod 启动被全局类型枚举拖慢”的强证据。

### 4.4 蓝图库：单锁、逐条反序列化、OnEnable、每 Mod 回调

`BlueprintsCache`（`Assembly-CSharp/Kingmaker.Blueprints.JsonSystem/BlueprintsCache.cs`）：

- `:32` `Init()`：打开 `Bundles/blueprints-pack.bbp`（本机 239.9 MB），读入 **236,661** 条 TOC 到 `Dictionary<BlueprintGuid, BlueprintCacheEntry>`。
- `:57` `Load(guid)`：`lock (m_Lock)`（`:59`）→ 在共享 `FileStream` 上 `Seek`（`:71`）→ 反射式 `m_PackSerializer.Blueprint(ref bp)` → `OwlcatModificationsManager.OnResourceLoaded`（`:86`）→ `bp.OnEnable()`（`:89`）。
- 也就是说，**所有蓝图加载共用一把锁和一个文件流**，无法并行；`OnEnable` 还会解析引用并可能递归触发更多 `Load`。
- `OwlcatModificationsManager.OnResourceLoaded`（`OwlcatModificationsManager.cs:270`）对**每一条蓝图加载**都遍历所有 OMM Mod；每个 OMM Mod 再查自己的 `BlueprintPatchesDictionary`，命中就读取 `.jbp` 补丁并应用（`OwlcatModification.cs:466`）。

Mod 侧的模式：

- 几乎每个内容 Mod 都挂 `[HarmonyPatch(typeof(BlueprintsCache), "Init")]` 的 postfix；Harmony 会按优先级依次执行它们。
- TabletopTweaks-Base 的 `ContentAdder`（`TabletopTweaks-Base/TabletopTweaks.Base.NewContent/ContentAdder.cs:39`）在一个 postfix 里调用了数百个 `Add*()`，本轮日志记录 **1,439** 条 `Added:`。
- ExpandedContent 记录 **4,084** 条 `Added:`；IsekaiMod 690；TTW-Reworks 349；MythicArcanist 134；ToggleableThrowingWeapons 107。
- 每新增一个蓝图都要 `AddCachedBlueprint` 并执行 `OnEnable`，后者会解析引用；内容之间互相引用时，还会把更多蓝图提前拉进来。

### 4.5 全库扫描/预加载：内容越全，启动越慢

| Mod | 机制 | 本轮证据 |
|---|---|---|
| CodexLib | `BpCache.LoadResources`（`CodexLib/CodexLib/BpCache.cs:160`）读取内嵌 `Blueprints.bin` 的 GUID 列表，对 6 类蓝图逐条 `blueprintsCache.Load`（`:192`） | 日志 `Loaded blueprints in 2811ms`，计数：Ability 6,827、ActivatableAbility 1,625、Buff 9,597、Feature 18,793、Item 6,994、ItemEnchantment 1,816，合计 **45,652** |
| CustomNpcPortraits | `BlueprintsCache.Init` postfix（`CustomNpcPortraits/Main.cs:60`）复制整个 `Portraits` 目录、解码图片（`:76`），再 `popUnitNames`（`:274`）遍历 `BlueprintList` 的全部条目（`:280`），对每个 `BlueprintCue`/`BlueprintBuff` 调 `GetBlueprintByGuid`（`:309`） | ModTimer 5,416 ms；日志中该 postfix 持续到 59.6 s，并出现 `Exception in string`/`{name}` 模板异常（被 `SafeLoad` 捕获） |
| ModTagEx | 启动时同步访问 GitHub API，再读 236,661 条 TOC GUID（`Main.cs:59`） | ModTimer 4,629 ms；Mod 自己的两个 Stopwatch 只解释 62.5 ms + 30.0 ms，其余约 4.5 s 落在同步更新检查、设置加载、PatchAll 与程序集/类型加载上；同步 HTTPS 是最可疑项，但未单独计时 |
| TabletopTweaks-Base / ExpandedContent | 启动时创建全部新内容，而不是等到第一次用到 | 3,726 ms / 3,070 ms；`Added:` 计数 1,439 / 4,084 |

### 4.6 重复内嵌的库：60,308 个 BlueprintCore 类型（已安装程序集普查）

dnlib 普查（`Mods/` 下 52 个带 `Info.json` 的 UMM 程序集，含当前禁用的 LevelableAivu、MDGA、PuzzleSkip）：

| 指标 | 数值 |
|---|---:|
| 程序集总大小 | 142.7 MB |
| 类型总数 | 71,760 |
| 方法总数 | 284,587 |
| 其中 `BlueprintCore.*` 类型 | **60,308（84%）** |
| Harmony Patch 类（含特性的类型） | 1,168 |
| 方法级 Harmony Patch | 498 |
| 内嵌 BlueprintCore 的程序集 | 11 个，每个约 5,400 个类型 |

11 个程序集各自打包了一份 BlueprintCore（约 5,400 类型/个），CLR 把它们视为不同程序集里的不同类型，不能自动合并。LevelableAivu 当前禁用，所以本轮实际随启动加载的是约 10 份（约 54,000 个类型）；报告中的 60,308 是“已安装程序集”的普查值，用来说明重复规模，不等同于本轮全部载入。后果：

- 每个 Mod 的 `GetTypes()` 都要加载约 5,500 个类型；
- `PatchAll`、WrathPatches 的 `[TypeId]` 扫描、Cheats 的成员扫描都要处理这些类型；
- 类型元数据、属性、JIT 代码和静态字段都增加内存与 GC 压力；
- 首次运行还要为每个 DLL 用 dnlib 解析并写 `.cache` 副本（`UnityModManager.cs:914` 起）。

这不是“Mod 作者写了很多功能”，而是**同一个库被复制了很多份**。

### 4.7 本地化、日志、磁盘与内存

- 本地化：`OnLocaleChanged` 的 Mod patch 共 1,057 ms，其中 KRXLib 672 ms、ExpandedContent 225 ms。每个 Mod 都要加载/合并自己的 `Localization/<locale>.json`。
- 日志：本轮 `GameLogFullPrev.txt` 有 109.4 MB、328,608 行；IsekaiMod 一个频道 10,754 行，HomebrewArchetypes 3,849 行，Mods 2,022 行。异常栈、逐蓝图 `Load blueprint:`/`guid=`/`I told kuru this would happen` 等调试输出会带来格式化、字符串分配和磁盘写入成本（具体占比未单独测量）。
- 内存：主菜单时工作集约 4.0 GB；蓝图对象、Mod 内容、重复程序集都在托管堆/Unity 原生堆上，GC 标记和分页会进一步拖慢启动尾段。
- 首次/更新后更慢：UMM 的 `.cache` 以 DLL 修改时间 + 版本为键；ManyMods 的缓存以游戏版本/程序集哈希为键；游戏更新或 Mod 更新后首启会重建，第二次启动才接近稳态。

## 5. 为什么“多 Mod”比单个 Mod 慢得多

单看每个 Mod 的 `Load` 是线性的；真正的放大来自共享路径：

1. **O(总类型数) 的全局扫描**：Cheats、PatchAll、WrathPatches binder、Unity UpdateCaller/Volume/Astar、本地化标签，都会枚举“所有已加载程序集的所有类型”。加一个内嵌 5,400 类型的 Mod，不只是它自己慢，而是所有扫描器都多扫 5,400 个类型。
2. **O(总蓝图数 × Mod 数) 的回调**：每条 `BlueprintsCache.Load` 都要过 `OwlcatModificationsManager.OnResourceLoaded` 的所有 OMM Mod；某个 Mod 触发一次全库扫描（CodexLib 45,652 次、CustomNpcPortraits 全 `BlueprintList`），就会把其他 Mod 的回调也一起放大。
3. **内容依赖的递归**：新增蓝图的 `OnEnable` 会解析引用，引用未加载时继续 `Load`；内容越多，递归越深，且都在同一把 `m_Lock` 下串行。
4. **内存/GC/JIT 的二次效应**：约 54,000 个实际加载的重复 BlueprintCore 类型（已安装普查 60,308）、数万蓝图、142.7 MB 已安装 Mod DLL，导致元数据表、JIT 代码、静态字段和托管对象增长；主菜单时 4 GB 工作集，GC 和分页会拉长尾段。
5. **首次运行缓存重建**：dnlib 改写、PatchAll 缓存、Cheats 缓存、Unity 类型发现缓存、WrathPatches binder 缓存都可能在同一轮重建；之后才进入较快的稳态。

## 6. 排除项与测量边界

- **不是主菜单 UI 的锅**：`MainMenu.Awake` 本体 0.008 s；场景加载 1.38 s。玩家感知的“卡”发生在菜单出现之前。
- **本轮 CheatsManager 不是瓶颈**：0 ms，因为 ManyModsPerformanceFix 已注入缓存。若移除该 Mod、或游戏版本变化导致缓存失效，Cheats 的全程序集成员扫描会重新出现；本轮没有测到它未缓存时的真实耗时，不能给数字。
- **游戏本体也有固定成本**：Mod 计时之外的约 20.5 s 包括 UMM 解析、`GameStarter.Awake`、未打点的 4.18 s、`StartGame` 前段、游戏自身 TOC/JSON/字体（约 8.6 s）、本地化等待、场景加载。没有 Mod 时这些仍然存在，所以“原版也慢”与“多 Mod 更慢”并不矛盾。
- **ModTimer 有盲区**：只测 Mod 的 patch 体；不测 `BlueprintsCache.Init` 游戏方法体、`LoadAllJson`、字体、场景、UI，也不测线程/锁等待。上面的 30.18 s 是“Mod patch 体”的下界，不是蓝图阶段全部成本。
- **不是所有 Mod 时间都可直接削减**：部分 Mod 的 `Load` 是规则内容构建，延迟到第一次使用可能影响菜单/开卡；是否可延迟要按 Mod 语义审核。
- **历史 112.5 s 不能当本轮基线**：2026-05-02 的 Mod 组合、UMM/游戏版本、缓存状态都不同。

## 7. 建议的下一步（不改游戏文件）

### 7.1 先做逐项 A/B（按预期收益排序）

| 顺序 | 关闭/调整 | 本轮对应耗时 | 预期（仅按测得的 Mod 时间估算） |
|---:|---|---:|---:|
| 1 | ModTagEx（或关闭其自动更新库） | 4,629 ms | 省约 4.6 s；若跳过同步 HTTPS 更快 |
| 2 | CustomNpcPortraits | 5,416 ms | 省约 5.4 s；头像功能不可用 |
| 3 | ToggleableThrowingWeapons | 7,077 ms（总） | 省约 7.1 s；投掷武器功能不可用 |
| 4 | ExpandedContent（注意 CodexLib 仍可能被 DarkCodex/SpellPouch 加载） | 3,818 ms（总） | 省约 3.8 s 的内容部分；CodexLib 2.8 s 不一定消失 |
| 5 | TabletopTweaks-Base | 4,269 ms（总） | 省约 4.3 s |
| 6 | PrestigePlus / VoidHead / RanRomance / Epilogue / Tome / Spars / MythicMagicMayhem 等内嵌 BlueprintCore 的 Mod | 每个 1–3 s | 逐个关闭，观察类型扫描与内容构建的叠加效应 |
| 7 | 保留 ManyModsPerformanceFix，确认缓存有效 | — | 避免 Cheats/PatchAll/类型发现回退 |

推荐协议：同一地点、同一存档、同一驱动；每次只改一个变量；用 ModTimer 的四个阶段 + `GameLogFull` 时间戳记录；至少两次取中位数。注意 ModTimer 本身也会增加少量开销，A/B 时保持它始终启用。

### 7.2 对 Mod 作者/上游的具体方向

- **CodexLib**：把 `BpCache.LoadResources` 的 45,652 次预加载改成按需/分批；至少不要在主菜单前加载全部六类。
- **CustomNpcPortraits**：把 `popUnitNames` 的全库扫描和头像复制移出 `BlueprintsCache.Init`，改为首次打开相关 UI 时执行；避免在启动时解码图片。
- **ModTagEx**：把 GitHub 更新检查移出主线程/启动路径，或默认关闭自动更新；TOC GUID 集合可以持久化缓存，而不是每次重建。
- **所有内容 Mod**：把“菜单不需要的内容构建”延迟到第一次使用；避免在 `Load`/`Init` 里做文件递归复制、图片解码、网络请求。
- **BlueprintCore 重复打包**：推动上游做共享程序集/去重；短期无法改变，但它是 60,308 个重复类型的根源。
- **Harmony**：继续使用/完善 PatchAll 缓存；Mod 作者应避免在 `Load` 里做 `PatchAll` 之外的重活。

### 7.3 本工作区可以做的诊断增强

现有 PerformanceImprove 0.4.0 只覆盖运行期探针；若要精确定位“游戏自身 8.6 s”和每个 Mod 的 `Init` 内部，需要新增启动阶段探针：

- 给 `GameStarter.StartGameCoroutine` 的各 `yield` 节点打时间戳；
- 统计 `BlueprintsCache.Load` 的调用次数、锁等待、`OnEnable` 时间；
- 给 `BlueprintsCache.Init` 的游戏方法体（而非仅 Mod patch）打点；
- 区分 `LoadAllJson`、字体、OMM `OnResourceLoaded`、`PatchAll`、`GetTypes`。

这需要用户再操作一次游戏；当前不自动启动。

## 8. 证据索引

| 主题 | 位置 |
|---|---|
| UMM 串行载入 | `analysis/decompiled/UnityModManager/UnityModManagerNet/UnityModManager.cs:6441`、`:6550`、`:769`、`:937` |
| UMM 注入点 | `Wrath_Data/Managed/UnityModManager/Config.xml`（`GameStarter.Awake:Before`） |
| 启动协程 | `analysis/decompiled/Assembly-CSharp/Kingmaker/GameStarter.cs:191`、`:230`、`:256`、`:259`、`:262`、`:265`、`:415` |
| 主菜单 | `analysis/decompiled/Assembly-CSharp/Kingmaker/MainMenu.cs:49` |
| 蓝图缓存 | `analysis/decompiled/Assembly-CSharp/Kingmaker.Blueprints.JsonSystem/BlueprintsCache.cs:32`、`:57` |
| OMM 管理 | `analysis/decompiled/Assembly-CSharp/Kingmaker.Modding/OwlcatModificationsManager.cs:85`、`:270` |
| OMM 应用 | `analysis/decompiled/Assembly-CSharp/Kingmaker.Modding/OwlcatModification.cs:160`、`:227` |
| Cheats 扫描 | `analysis/decompiled/Core.Cheats/Core.Cheats/CheatsManager.cs:99`、`:132` |
| PatchAll 缓存 | `analysis/decompiled/ManyModsPerformanceFix-installed/ManyModsPerformanceFix/PatchAllCache.cs:217` |
| 类型缓存 | `.../AssemblyTypesCache.cs:22`、`.../RuntimeTypeDiscoveryCache.cs:77` |
| CodexLib 预加载 | `analysis/decompiled/CodexLib/CodexLib/BpCache.cs:160`、`:192`、`:233` |
| CustomNpcPortraits | `analysis/decompiled/CustomNpcPortraits/CustomNpcPortraits/Main.cs:60`、`:274` |
| ModTagEx | `analysis/decompiled/ModTagEx/ModTagEx/Main.cs:22`、`:59`；`Updater.cs:15` |
| TabletopTweaks 内容 | `analysis/decompiled/TabletopTweaks-Base/TabletopTweaks.Base.NewContent/ContentAdder.cs:39` |
| ToggleableThrowingWeapons | `analysis/decompiled/ToggleableThrowingWeapons/ToggleableThrowingWeapons/Main.cs:15`、`BuildEarly.cs:12` |
| 本轮冻结数据 | `analysis/runtime/20261021-232751/` |
| 元数据普查 | `analysis/mod-metadata-census-20261021.csv`、`analysis/mod-library-census-20261021.csv` |

## 9. 未验证与下一步

- 未做“同一 Mod 组合、关闭全部 Mod”的本轮对照；原版基线来自历史与静态推断，不是本轮测量。
- 未单独计时 CheatsManager 未缓存时的耗时；0 ms 只是本轮被缓存后的结果。
- 未对每个 Mod 的内部做逐方法 profiler；ModTimer 的粒度是“Mod 的 patch 体”。
- 未验证延迟初始化对菜单/开卡语义的影响；任何优化都应按 Mod 分别审核。
- 游戏当前已再次启动（23:35:47，ModTimer 被禁用），没有新的可比值；下一步若要 A/B，应先恢复 ModTimer 并保持同一操作路径。
