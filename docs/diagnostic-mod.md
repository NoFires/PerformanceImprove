# Wrath Performance Lab 0.2.0 诊断版

用于调查升级/开角色界面卡顿、地图加载缓慢和长时间运行内存增长。此版本只采样，不实施性能修复，也不会自动清理角色、Buff、存档或资源。

## 安装和使用

构建包位于工作区 `dist/WrathPerformanceLab-0.2.0.zip`。可用当前 Unity Mod Manager 安装；或把包中的 `WrathPerformanceLab` 文件夹解压到游戏的 `Mods` 目录，保证 `Mods/WrathPerformanceLab/Info.json` 与 DLL 同级。请退出游戏后安装，再启动游戏。

本轮仅生成安装包，没有把新 Mod 复制进游戏。需要已有 UMM 0.32.4 和其 Harmony，本包不附带替换版 Harmony 或游戏 DLL。

在 UMM 中启用后开始记录，关闭此 Mod 开关会移除它自己的采样补丁并停止记录。若游戏提示加载失败，查看该 Mod 的日志和 `patches.txt`，不要把静态构建通过当作游戏内兼容性已验证。

输出位置为游戏目录：

```text
Mods/WrathPerformanceLab/Logs/<启动日期时间>-<会话编号>/
    timing.csv
    loading.csv
    memory.csv
    diagnostics.csv
    graphics.csv
    patches.txt
```

## 怎样复现这三个问题

1. 刚启动进入存档，静置约一分钟。打开角色面板/建卡或洗点界面，进入升级；重现卡顿的选择、撤销或翻页，记下大致时间和具体操作。
2. 从大地图进入眷泽城，等待加载结束，再离开并重复同一路线。最好保留“刚启动第一次进入”和“玩久了再次进入”两组记录。
3. 正常游玩到开始明显卡顿，尽量覆盖切图和反复开关角色界面。诊断记录每 15 秒落盘；若闪退，最后一行通常在崩溃前，不能据此保证抓到瞬时内存峰值。
4. 记录卡顿发生时的画面/操作，以及游戏当前加载的 Mod；保留整个会话目录，分析时把它与对应 Player.log / GameLogFull.txt 对齐。

用户已确认：打开已有角色面板，以及新建角色／洗点两种操作都会卡。当前同时覆盖角色详情、升级、建卡、洗点相关入口，无需为此更换诊断 DLL。

用户进一步说明：无 Mod 原版也有卡顿，安装 Mod 后更严重。调查优先验证游戏本体的同步重建、订阅生命周期、预览角色复制、Atlas/DXT 与区域恢复，再量化 Mod 的增量。仅启用诊断 Mod 的测量仍包含 UMM/Harmony/诊断开销，应与关闭诊断的操作对照区分；不同角色、剧情、存档规模或战役的结果不能当作同一组性能差值。

## 记录内容与解释

`timing.csv` 每 15 秒写一个累计窗口。记录调用次数、包含子调用的总耗时、单次最大耗时和经过该最终补丁的异常次数。覆盖预览角色复制、Facts/法术书/装备复制、升级动作重放、专长列表建立/状态刷新、角色信息 VM、进展树刷新、资源请求/清理和部分实体恢复入口。

这些时间包含已有 Mod 补丁，嵌套方法不能直接相加计算总时间。仅计时同步方法体；不会把创建 IEnumerator 对象的时间错误当作整个地图加载时长。不是 CPU 栈采样，也没有每次调用的详细追踪或准确分配字节数。

`loading.csv` 在 LoadingProcess.StartLoadingProcessInternal 与 TickLoading 的生命周期边界读游戏已有 Stopwatch，记录 start、end 或 exception。耗时包含 yield/异步等待；保留原 IEnumerator 和异常。end 对应正常完成路径；异常或中断时可能只有 start，回调启动新任务也可能使新 start 行出现在旧 end 行前面。停用诊断或游戏直接结束时，不会编造一个完成事件。

`memory.csv` 在加载阶段开始和周期点记录进程 Private Bytes/Working Set、GC.GetTotalMemory(false)、Unity 分配/保留量、Mono 使用量/堆容量、GC 计数、资源缓存条目数、Bundle 缓存条目数/实际仍加载数量、全局订阅槽数和单位订阅组数。

这些内存指标会重叠，不能相加。托管堆容量或进程 Working Set 单独上涨并不等于泄漏；需要比较相同场景和操作循环后的趋势。资源/Bundle 字典可能保留空记录，所以缓存条目数也不等于存活资源数。订阅槽按接口统计，包含重复接口计数和等待清理的空槽，不是唯一监听对象数。字段不存在或不可读取的计数记为 -1；更严重的采样错误会停止本次 I/O 并在 UMM 日志提示。

`patches.txt` 列出采样目标当时已有的 Harmony Owner 和补丁方法、优先级，以及找不到/无法打补丁的目标。后加载的 Mod 可能再更改补丁列表，此文件不是永远实时的完整兼容性证明。

## 0.2.0 新增指标

`diagnostics.csv` 使用长表：`elapsed_s, reason, group, metric, value, status`。与其他 CSV 使用同一会话秒数。普通情况每 15 秒、加载活动期间每秒采样；预览建立、控制器建立/关闭、UI 关闭及加载边界请求一次延迟到 OnUpdate 的采样，多次请求合并，最快每秒一次。因此 `reason` 是最近合并的触发标签，不是完整操作日志。

| group | 主要指标 | 解读 |
|---|---|---|
| controllers / previews | 总观察数、当前弱引用存活数、观察到的回收数、存活且 Stopped/IsDisposed 的数量、超过 60 秒数量 | 从探针成功启用后开始跟踪，未强制 GC；存活不自动等于泄漏 |
| late_update | Fabric 节点总数、扫描节点/观察者数、找到的控制器/Stopped 控制器数、委托目标类型 | 只检查已存在 Fabric；在白名单边中最多走 4 层，不是完整 GC 保留根分析 |
| atlas | 合图队列、WaitCalculatedCount、OccupiedBuildBudget | 读取已注册服务与静态字段，不请求建立合图服务 |
| dxt | 队列长度、观察到的工作开始/成功/失败总数、活着的运行/失败请求、回读/错误总数、丢事件和追踪淘汰数 | 后台工作状态是探针观察状态，不是 Task.Status；异常不改为成功，不修改完成字段 |
| va2 | 设置总数、当前实体注册表匹配数、FX 键槽/唯一键数、已 disposed 的键、无槽/集合的键、已知预览/已 disposed 槽主人的键 | 注册表缺席并不等于可删；键槽计数可包含同一物品在多份设置中的重复 |
| va2 | 设置新增数、为已知预览新增数、与已观察预览 ID 匹配的设置数 | 新增是在 ForCharacter 调用前后观察的；在预览构建内部发生的新增可能早于预览返回，所以新增归属是下界，匹配 ID 是补充证据 |
| probes / sampler | 每项探针安装状态、单次采样毫秒数 | 可选探针缺失不会停止其他组；按实际开销决定是否继续常驻 |

VA2 从实际 `ForCharacter` 调用记录现有设置实例的弱引用，首次观察调用前该组为 `unavailable`，未加载或未使用该 Mod 时也为 `unavailable`。不为采样去读取可能触发可选 Mod 初始化的静态 Instance。设置实例切换后在下一次实际调用更新观察源。

Fabric、VA2 各自限制 12,000 个扫描步骤和约 5 ms，预算检查不是实时系统的硬时限；冷反射和单次操作可越界，实际总耗时见 sampler。`scan_truncated=true` / `status=partial` 时只能把扫描计数当作本次找到的下界，不能与完整扫描直接比较。Fabric 委托调用列表最多 8 项、目标类型直方图最多 32 类，达到深度或范围限制计入 `scope_limit_hits`；即使没有达到预算也不是完整对象图。

`graphics.csv` 保存 DXT 的 run、readback、error、before_clear、worker_start、worker_end 事件和 queued_snapshot。每行含会话内请求 ID、已观察年龄、年龄是否仅为下界、后台工作状态、距工作开始时间、回读回调数、原始完成位、HasError、mip 描述符数/字节、异常类型。run 探针命中后年龄从该时刻计算，启用前已存在或先通过其他入口发现的请求只知道年龄下界。

`descriptor_bytes_not_live_allocation` **不是仍分配的原生内存**：原代码释放 NativeArray 的局部结构体副本后，字典里的副本仍可能保留长度。这里不访问指针、IsCreated 或内容，不能据此宣称发现 Persistent 内存泄漏。worker 行不枚举数组，两个描述符字段为 -1；后台事件只缓存标量，文件写入仍在 OnUpdate。before_clear 描述的是清理前的状态，不证明 Clear 已成功完成。

每组控制器最多跟踪 4,096 个、预览 8,192 个、DXT 请求 4,096 个，均用弱引用；单独保留最多 8,192 个预览 ID 字符串用于与设置匹配。活对象因容量被淘汰记为 evicted，不冒充已回收。DXT 事件队列 512 条，满后丢最旧事件并累加 events_dropped；每次最多扫描队列前 512 个请求且检查约 5 ms 预算。极端情况下事件/后部请求不完整，应查看对应计数。所有指标都不覆盖探针启用前的完整生命周期。

## 开销与边界

- 不逐帧写文件，不强制 GC，不调用 Resources.UnloadUnusedAssets，不遍历所有 Unity 对象，不永久保存角色/VM 实例引用。
- 每个 CSV 最大 8 MiB，达到上限后停止追加；五个 CSV 每会话最多约 40 MiB，另有小型补丁清单。旧会话目录不会自动删除，长时间使用后需自行归档。
- 计时有 Stopwatch 和锁的开销，加载开始采样有进程/内存查询开销，需与关闭诊断的对照运行比较。卡住主线程时，周期采样同样无法继续运行。
- 未确认崩溃原因前不会声称已经发生内存溢出。显存不足、原生分配失败和 Mod 异常可能需要其他证据。
- 已完成本机编译和独立执行检查；尚未启动游戏验证其加载、所有探针是否命中、实际性能开销和完整 Mod 列表下的兼容性。

## 构建及检查

在工作区运行 `scripts/build-mod.ps1`。用现有 .NET SDK 构建 net481，reference assemblies 和 NuGet 缓存在 `E:\Program`。检查项目为 `tests/WrathPerformanceLab.Checks`，31 项通过；包括真实 Harmony 返回/异常保持、并发事件、弱引用回收、容量淘汰、部分扫描、自动属性字段读取、VA2 键归属、DXT 失败与完成位分离，以及后台工作异常原实例传播。记录在 `analysis/diagnostic-checks.txt`。使用合成对象布局与真实 Harmony，没有执行游戏/Unity 原生逻辑；游戏内命中、Mono 下补丁兼容和实际开销仍待验证。
