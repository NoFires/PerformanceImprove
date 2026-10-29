# 优化候选、兼容性和验收

本阶段完成结构分析和工具配置；以下是后续实施方案，尚未实现性能补丁，也没有可以宣称的 FPS 提升。

## 已存在的补丁重叠

| 安装的 Mod | 本轮反编译确认的行为 | 新 Mod 应处理的重叠 |
|---|---|---|
| `!ManyModsPerformanceFix` 1.4.0 | CheatsCache 缓存控制台反射收集；回退路径修改 CheatsManager 构造器；Transpiler 修改 SnapMapBase.UpdateRuntimeData 的矩阵读取 | 启动反射收集、粒子 SnapMap 方法 |
| `!!ModTimer` 1.4.0 | UMM Load、Owlcat Apply、蓝图初始化/LoadPackTOC/语言补丁计时 | 是启动分析工具，不能代表战斗帧分析；本身可能影响启动计时 |
| `WrathPatches` 1.19.1 | ActionBarVM 重复订阅、ActionBarSpellGroupPCView 的 disposable、InitiativeTrackerUnitVM BuffVM 释放；还包含蓝图/Mod 资源相关补丁 | UI 生命周期、事件泄漏、蓝图替换与加载 |
| `BubbleTweaks` 1.18.3 | 蓝图 Init 和范围指示/PathVisualizer 等补丁 | UI、路线显示、范围效果相关方法 |

这是静态补丁清单，只证明 DLL 中存在这些逻辑；最终是否成功应用、开关是否开启、与其他 47 个 Mod 的交互，都需要在游戏中用 `Harmony.GetPatchInfo` / `Harmony.GetAllPatchedMethods` 验证。装有同名 DLL 不等于已经验证运行兼容。

## 历史启动证据

来源：游戏目录 `Wrath_Data/Managed/UnityModManager/Log.txt`，文件修改时间 2026-05-02 18:13:34（主机显示时间）。摘录保存在 `analysis/historical-log-summary.json`。

| ModTimer 阶段 | 历史记录 |
|---|---:|
| ModEntry.Load / OwlcatModification.Apply | 47,159 ms |
| BlueprintsCache.Init 的被计时补丁 | 26,655 ms |
| StartGameLoader.LoadPackTOC 的被计时补丁 | 1,324 ms |
| OnLocaleChanged 的被计时补丁 | 998 ms |
| ModTimer 合计栏 | 76,136 ms，占其启动计时 67.69% |

历史 Mod.Load 前列包括 ToggleableThrowingWeapons 13,035 ms、ToyBox 4,944 ms、VoidHeadWOTRNineSwords 3,540 ms。蓝图初始化前列包括 CustomNpcPortraits 4,196 ms、PrestigePlus 3,955 ms、TabletopTweaks-Base 3,354 ms。

这些是一次旧配置运行的仪表记录，阶段可能嵌套，不能作为互斥区间简单相加估算完整启动时间，也不能直接认定当前慢启动由同一个 Mod 引起。记录中还存在 BadImageFormatException 和 BuffIt2TheLimit 的 NullReferenceException；应复测是否仍会发生，再区分启动等待、异常重试和真正算法开销。

## 先测什么

| 优先级 | 候选入口 | 采样内容 | 主要正确性约束 |
|---|---|---|---|
| P0 | GameMode 各具体控制器 Tick | 调用次数、包含耗时、最大值、采样分布；模式/单位/效果数量 | 保留顺序和异常语义；统计嵌套包含时间不应直接求和 |
| P0 | Harmony 实际补丁清单 | 原方法、Owner、Prefix/Postfix/Transpiler/Finalizer 与优先级 | 识别重复补丁；不能通过禁用别人 Mod 来冒充性能修复 |
| P1 | UnitVisionRangeController.TickUnits | 每帧空间查询数、AwakeUnits、候选目标数与耗时 | 隐身、迷雾、进出视野、传送和战斗状态 |
| P1 | SleepingUnitsController / BaseUnitController | 全单位/清醒单位规模、休眠切换、各子控制器耗时 | 过场、召唤、死亡、暂停和世界地图 |
| P1 | AiBrainController.SelectAction / CalculateActionScore | 决策频率、动作数、目标数、路径请求数 | 保留随机数和决策结果，缓存按动作/状态正确失效 |
| P1 | UnitMovementAgent.FindPath / PathTo / UpdateAvoidance | 寻路次数、重复请求、避障候选规模 | 回合移动距离、障碍/体型/门、Path Claim/Release |
| P1 | UI/BuffVM 生命周期 | UI 打开次数、订阅者/BuffVM 存活数量、GC 趋势 | 先核对 WrathPatches 的修复已启用 |
| P2 | 蓝图 Init/Load / Mod 初始化 | 命中/未命中、反射收集、OnEnable、Mod 回调耗时 | GUID 替换、失效、共享流位置和初始化顺序 |
| P2 | SaveRoutine / SerializeAndSaveThread | 快照、序列化、压缩、截图、提交各阶段 | 存档一致性、完成/失败处理、版本升级与读回 |

另有一个需检查的静态线索：AiBrainController.Activate 对 Difficulty.LimitedAI.OnValueChanged 使用 `+=`，Deactivate 方法体为空。需要核实 ReactiveProperty 的订阅实现、模式切换次数、实际处理器重复次数以及其他 Mod 是否补丁修复后，才能认定为泄漏并实施修改。

## 性能采样 Mod 的实施约束

建议继续使用本机已安装的 UMM + Harmony。先以独立 ID（例如 `WrathPerformanceLab`）建立诊断模块和开关；可关闭采样，按固定窗口写 CSV/JSON。安装包不打包游戏 DLL 或新的 Harmony 副本。

下一阶段构建目标必须依据实际程序集 API 兼容性验证。ILSpy 给游戏核心生成的 csproj 推断为 net40，而本机若干现有 Mod 为 net481；这不是要求新 Mod 使用现代 .NET 10 作为游戏运行时。现有 .NET 10 SDK 只是构建和反编译工具，不能把 net10 DLL 直接当作兼容的 Unity Mod。若需 .NET Framework reference assemblies，通过 NuGet 下载并将缓存放入 `E:\Program\Caches\NuGet`。

计时使用 Stopwatch.GetTimestamp、值类型状态或预分配存储；避免每次 Tick 都 new Stopwatch、写日志或分配大量字符串。Harmony Finalizer 应保留原始异常。先采样非泛型的控制器边界，再对少数热点深入，避免一开始给整个游戏方法集打补丁。

每个优化开关需能独立启用/关闭；发生版本/签名不匹配时跳过该优化并记录一次原因。Transpiler 需验证匹配数量和预期 IL，避免把错误位置替换后继续执行。需要列出与现有 Owner 的重叠，并记录检测结果。

## 基准与验收

固定相同存档、场景、队伍、Mod 配置、分辨率、垂直同步/帧率限制。先预热，再采样至少 60–120 秒，每个开关条件重复至少三次。按地图静止、全队移动、多人战斗、大量 Buff/范围效果、反复开关 UI、存读档分别记录。

报告帧时间中位数、P95/P99、最大尖峰、GC 次数、可用的分配指标、总内存趋势及目标方法耗时；将采样开关自身的开销纳入对照。若本机 Mono 不支持所选分配 API，应明确降级指标，不能用 GC.GetTotalMemory 的变化冒充准确分配字节数。

补丁须验证命中、伤害、Buff 剩余时间、回合推进、寻路结果、隐身/战争迷雾和存档读回。只有实测收益且回归通过后才默认开启。当前没有启动游戏，因此这些运行阶段验收尚未执行。
