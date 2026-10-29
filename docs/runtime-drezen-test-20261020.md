# 大地图进入眷泽城运行实测：2026-10-20

这次从 WorldWoundGMArea 进入 DrezenCapital 的加载流水线为 **48.812 秒**，未复现用户此前描述的几分钟等待。主要时间在区域加载/状态恢复、昼夜环境匹配和两次资源清理；DXT 没有卡死，自动存档也不是本次主耗时。

这是保留现有 Mod 配置、同一游戏会话中的一次测量，没有安装优化补丁，不能当作优化前后对比，也不能排除长时间游玩后出现更慢情况。

## 边界与验证

- 用户全程操作，录制没有输入游戏界面或修改设置。PID 31800、WrathPerformanceLab 0.2.0。
- 独立段：`analysis/runtime/20261020-230635/drezen-loading-20261020-2332/`；原洗点数据未覆盖。
- 165 个进程外样本，间隔约 1 秒，含准备、加载和进入后约 44 秒；已结束额外录制。分析末次核对时游戏进程已退出，本轮脚本没有关闭游戏。
- 按诊断会话起点换算，实际加载约为香港时间 **23:34:22.447—23:35:11.259**。墙钟对齐有采样/初始化误差，阶段耗时来自游戏原 Stopwatch。
- `loading.csv` 首个相关 start 到 AreaLoadingComplete 为 48.8121441 秒。游戏自身 GameLogFull 的 `Loading is ended in 48.8121396s` 与之仅相差约 4.5 微秒，形成独立日志交叉验证。
- 输入已冻结在 start/、end/、samples.csv，SHA-256 保存于 analysis.json。分析脚本 `scripts/analyze_loading_recording.py`。

## 加载分解

| 阶段 | 墙钟时间 | 占本次加载 |
|---|---:|---:|
| LoadAreaCoroutine：区域加载与状态恢复 | 28.569 秒 | 58.5% |
| MatchTimeOfDayCoroutine：昼夜/光照/音频环境匹配 | 9.442 秒 | 19.3% |
| 两次 UnloadUnusedAssetsCoroutine | 6.142 秒 | 12.6% |
| 两次 AwaitTextureCompression | 1.661 秒 | 3.4% |
| 自动存档 SaveRoutine | 1.326 秒 | 2.7% |
| 预处理、旧区域卸载、回调及其他阶段 | 1.673 秒 | 3.4% |
| 合计 | **48.812 秒** | 100%（四舍五入有误差） |

这些是串行加载阶段的墙钟，包含 yield、异步完成等待及调度。不能把它们当作各方法纯 CPU 时间。

### 区域加载：场景匹配完成之后还有 22.92 秒

GameLogFull 原生时钟的关键点：

| 事件 | 原生日志秒数 |
|---|---:|
| 开始 LoadAreaCoroutine | 1603.4149 |
| Scene set matched | 1609.0653 |
| LoadAreaCoroutine 完成 | 1631.9836 |

进入该阶段约 **5.65 秒**时已经完成场景集合匹配，之后仍有 **22.918 秒**。该后段包含 AreaPersistentState.PostLoad、单位/地图对象资源预加载、动态状态 TurnOn、实体生成、导航、跨场景对象和区域 spawner 等，本轮没有分别计时，不能把它统一命名为“磁盘加载场景”。

本段已测同步入口：

| 方法 | 调用数 | 累计包含耗时 | 单次最高 |
|---|---:|---:|---:|
| BundlesLoadService.RequestBundle | 1,566 | 1,386.05 ms | 205.79 ms |
| AreaDataStash.UnstashAreaState | 1 | 399.44 ms | 399.44 ms |
| SceneLoader.MatchStateWithScene | 28 | 100.27 ms | 61.55 ms |
| SceneLoader.LoadSceneEntities | 27 | 96.13 ms | 61.52 ms |
| ResourcesLibrary.CleanupLoadedCache | 2 | 10.28 ms | 10.21 ms |

以上是同步方法体计时，嵌套项不能相加；RequestBundle 的同步返回耗时不覆盖后续 Unity 异步载入，LoadSceneEntities 也不代表后续所有实体激活和生成。它们目前不能解释完整的 22.92 秒，需要细分调用和 coroutine 子阶段，而不是直接重写字典匹配或存档解压。

另记录到 1,552 次 LevelUpController.Start/构造/Stop，构造自身累计约 13.93 ms。区域 NPC 初始化会走自动升级链，不能仅凭次数认定升级 UI 被打开或出现 1,552 条 UI 泄漏。已有的 LateUpdate 可达 Stopped 控制器在这轮保持 **18→18**。

### 环境匹配：反射探针相关区间约 9.15 秒

日志在 1632.8331 设置 `DrezenCapital_Outdoor_Light` 为活动场景，到 1641.9833 才记录等待探针烘焙结束，间隔 **9.1502 秒**，覆盖了环境匹配阶段的大部分时间。

源码 `SceneLoader.MatchLightAndAudioScenesCoroutine` 在这里调用 LightController.ChangeDayTime，并轮询 IsProbeBaking；LightController 的 BakingProbe 包装的是 **ReflectionProbe.RenderProbe / IsFinishedRendering**，使用 IndividualFaces 分帧方式。因此这条主要线索是实时反射探针更新，不能与随后 LightProbes.TetrahedralizeAsync 的四面体重建混成同一过程。

该区间还包含隐藏动态对象、ChangeDayTime 本身和其他准备工作，不是精确的纯等待计时。未出现 `Light probes baking process takes too long!` 超时标记；本次没有碰到代码中的 60 秒保护分支。没有 GPU 栈或逐探针完成数据，不能据此断言显卡故障，也没有关闭反射或修改画质。

下一次应记录反射探针数量、待完成数量、各探针分辨率/分帧方式、RenderProbe 发起与完成时刻，以及 ChangeDayTime 是否重复触发。先辨别必要计算、重复更新和等待，再决定缓存或调度方案。

### 两次资源清理

两次清理分别为 **3.094 秒、3.048 秒**。本体该 coroutine 包含若干帧等待、缓存/头像/声音回调清理、GC.Collect 和 Resources.UnloadUnusedAssets。6.14 秒不是简单的两次托管 GC 时长。

可以调查同一次区域切换是否有可合并的重复清理，但不能直接删掉：本次进入眷泽城后的常驻资源显著增加，省略卸载可能提高峰值并加重用户的长时间内存问题。

## 内存、I/O 和压缩

| 进程外观测 | 录制段首：大地图 | 录制段末：城内 | 录制段最高 |
|---|---:|---:|---:|
| Private Bytes | 7.53 GiB | 10.06 GiB | 10.88 GiB |
| Working Set | 4.88 GiB | 6.01 GiB | 6.16 GiB |
| 托管使用量估计（内部低频样本） | 1.92 GiB | 2.11 GiB | 2.11 GiB |

10.88 GiB 的进程私有内存峰值出现在流水线完成后的录制范围内；严格落在加载区间的 1 秒样本最高约 **10.28 GiB**。这些是已采样值，不保证抓到瞬时峰值。录制期间最低仍有约 **10.39 GiB** 系统可用物理内存，本轮没有 OOM 或闪退证据。

实际仍加载的 Bundle 计数 **473→822**，说明区域常驻资源确有增加；Private Bytes 上升约 2.53 GiB 不能直接当作泄漏量，也不能由 Bundle 个数反算具体字节。还需要离开眷泽城并在相同场景循环后比较稳定基线。

按会话起点和 1 秒外部样本插值，加载区间进程累计 CPU 时间约 **17.83 秒**、进程读取约 **1.71 GiB**、写入约 **16.41 MiB**。这些不是物理磁盘延迟或吞吐基准，读取可能命中系统缓存；累计 CPU 又包含多个线程，不能单独用它断定 GPU/I/O/主线程哪一个是瓶颈。

DXT 新增 **48 个工作任务，48 个正常完成并 Clear**，无异常事件、丢事件或容量淘汰。观察到后台工作最长约 **159 ms**、从 Run 到 Clear 最长约 **732 ms**。最终 Atlas/DXT 队列均为零。本轮不支持“DXT 工作失败导致长时间不结束”解释。

1 秒进程样本中的队列计数均为零，但 graphics.csv 实际捕获过短暂 queued_snapshot；这是采样频率差异，不能声称全程没有队列。1.66 秒的 AwaitTextureCompression 也可能包含角色状态等待，并不等于 DXT 工作者累计 CPU 时长。

## 其他发现与限制

- 存在 QuickCast 两类订阅者异常，发生在 AfterSceneLoaderFinishedArea 内；这个完整阶段仅约 0.532 秒，不能把它直接当作本次 48.8 秒的主因。计时探针异常为零不代表游戏中没有被捕获的异常。
- `NARR_CH3_DrezenCapitalOutdoor.bnk` 加载失败，以及光照贴图模式、导航和粒子 Mesh 警告已保存在原始日志。尚未测量它们的重试/失败成本，没有据此修改音频文件或游戏安装。
- 本轮现有的预览弱引用计数 **55→55**、VA2 设置 **2410→2410**、LateUpdate 可达旧控制器 **18→18**，切图没有清掉先前那条保留链，但本次也未见这些项继续增加。未完成所有对象的完整保留根审计。
- 扩展采样主体平均约 2.93 ms、最高约 5.45 ms，无组级 capture_error 或预算截断；这不包括全部 Harmony、文件最终 Flush 和 OS 开销，仍缺关闭诊断的同操作对照。
- Mono 的 Process 内存列仍为无效零值，报告使用进程外采样；EventBus 两列 -1 未用于判断订阅。
- 数据来自同一已有 Mod 环境的一次进入，没有原版对照、不同昼夜对照或多小时后的重复路线。**未复现几分钟加载，不代表用户此前的情况已修复。**
- 录制结束后的 Player.log 出现 `EscMenuVM.OnQuitAction → SystemUtil.ApplicationQuit` 路径及 CameraRig.OnDisable 等退出阶段异常。它与本段成功加载分开保存于 post-recording/，不能把退出阶段的 Crash Report 字样直接当作此次加载 OOM。

![加载分项与资源趋势](../analysis/runtime/20261020-230635/drezen-loading-20261020-2332/drezen-loading.png)

后续优先细分 **场景匹配后的区域 PostLoad/生成/激活阶段**，以及 **反射探针更新与等待**。两次资源卸载作为第三项候选，需同时验证内存峰值；本轮未部署新优化补丁或回滚用户操作。
