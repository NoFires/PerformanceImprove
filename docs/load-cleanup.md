# 区域加载中的重复资源清理（0.4.1）

本文说明 PerformanceImprove 0.4.1 新增的 `SkipDuplicateLoadCleanup`：在**非读档**的区域切换中，跳过 `Game.LoadArea` 注册的第二次 `UnloadUnusedAssetsCoroutine`。它针对的是加载耗时，不是主菜单启动；与 `!ManyModsPerformanceFix` 的启动缓存没有重叠。

## 1. 证据：每次普通切换都清理两次

数据来自 `Mods/PerformanceImprove/Logs/perf-20261021-233551.jsonl`（2026-10-21 第二段，3 个 Mod，用户实际操作）。

普通区域切换（没有 `SaveManager.LoadRoutine`）：

| 时刻 | 事件 | 耗时 |
|---:|---|---:|
| 2082.092 | `Game+<UnloadUnusedAssetsCoroutine>` #1 开始 | — |
| 2085.028 | #1 结束 | **2,935.9 ms** |
| 2085.028 | #2 开始 | — |
| 2087.888 | #2 结束 | **2,860.3 ms** |
| 2087.934 | `SceneLoader+<LoadAreaCoroutine>` 开始 | — |

| 时刻 | 事件 | 耗时 |
|---:|---|---:|
| 2114.192 | `Game+<UnloadUnusedAssetsCoroutine>` #1 开始 | — |
| 2117.053 | #1 结束 | **2,861.3 ms** |
| 2117.053 | #2 开始 | — |
| 2119.862 | #2 结束 | **2,809.5 ms** |
| 2119.892 | `SceneLoader+<LoadAreaCoroutine>` 开始（进眷泽城，6,845 ms） | — |

两次清理之间只有 `LoadingProcess` 的进度回调/空枚举器，**没有任何新资源加载**。整段日志 12 次清理合计 34,321 ms，其中这两次普通切换的第二次清理就占约 **5.67 s**。

读档切换不同：`SaveManager.LoadRoutine` 在两次清理之间加载存档资源，第二次清理是有意义的：

| 时刻 | 事件 | 耗时 |
|---:|---|---:|
| 59.619→62.469 | #1 | 2,850.0 ms |
| 62.469→63.901 | `SaveManager+<LoadRoutine>` | 1,431.9 ms |
| 63.901→66.615 | #2 | 2,712.8 ms |

因此补丁只跳过 `saveInfo == null` 的普通切换，读档保留两次。

## 2. 游戏代码路径

`Assembly-CSharp/Kingmaker/Game.cs`：

- `LoadArea(...)`（私有六参数）在 `UnloadEntitiesCoroutine` 之后注册 **第一次** `UnloadUnusedAssetsCoroutine()`（约第 1456 行），在 `SaveManager.LoadRoutine` 之后注册 **第二次**（约第 1475 行）；`UnloadAllOnEnter` 分支还有第三次（约第 1537 行）。
- `UnloadUnusedAssetsCoroutine()`（约第 1332 行）每次执行：`ResourcesLibrary.CleanupLoadedCache()` → `CustomPortraitsManager.Cleanup()` → `AkCallbackManager.CleanupEventCallbacks()` → `GC.Collect()` → `Resources.UnloadUnusedAssets()`，并等待异步操作完成。

普通切换没有 `LoadRoutine`，两次调用在同一个同步 `LoadArea` 调用里先后创建，创建后才由 `LoadingProcess` 执行；第二次执行时没有新资源，属于重复的全量清理。

## 3. 实现与安全边界

- Harmony 前缀在 `Game.LoadArea` 进入时记录 `saveInfo` 是否为 null；postfix 清除状态。`LoadArea` 本身不 yield，两个清理调用在同一个同步窗口内被观察到。
- 前缀在 `Game.UnloadUnusedAssetsCoroutine` 上计数：只跳过普通切换的**第 2 次**；第 1 次保留，`UnloadAllOnEnter` 的第 3 次保留。
- 读档（`saveInfo != null`）不计数、不跳过。
- `LoadArea` 提前返回、异常或被其他 Mod 跳过时，postfix 仍会清理状态；不会泄漏到下一次加载。
- 若 `Game.LoadArea` 的六参数签名或 `UnloadUnusedAssetsCoroutine` 不存在，功能初始化失败，其他功能继续工作；不会在未知布局上强行改 IL。
- 只跳过整段第二次清理，不改 `Resources.UnloadUnusedAssets` 的语义，不改 `CleanupLoadedCache`/GC 代码，不做 transpiler。

代价：第二次清理原本可能释放第一次扫描之后才变成无人引用的少量资源；跳过会把它们留到下一次区域切换的第一次清理。功能可关闭；如果观察到加载后内存异常增长，应关闭并重新测。

## 4. 与 !ManyModsPerformanceFix 的关系

`!ManyModsPerformanceFix` 处理的是**启动**阶段的共享扫描：`CheatsManager`、Harmony `PatchAll`、`Assembly.GetTypes`、UMM reload 扫描、WrathPatches binder、Unity/Owlcat 类型发现、本地化标签。它不修改 `Game.LoadArea`，也没有 `UnloadUnusedAssets` 相关字符串。

本功能只修改 `Game.LoadArea` / `Game.UnloadUnusedAssetsCoroutine`，两者没有重叠的 Harmony 目标，也没有共享状态。它也不改蓝图、Mod 加载或 Cheats 缓存，因此不会与 ManyMods 的缓存失效/重建逻辑冲突。

## 5. 预期与验证

按本轮日志，普通区域切换的第二次清理约 2.8 s；进眷泽城总时长约 17.2 s，跳过它预计省 **约 2.8 s（约 16%）**。读档切换的两次清理都保留，不受影响；所以“读档进入”不会因本功能直接变快。

已做验证（2026-10-22，0.4.1 离线）：

- 30 项独立检查通过：`Game.LoadArea` 六参数顺序、`saveInfo` 第 4 位、`UnloadUnusedAssetsCoroutine` 返回类型、`LoadArea` 仍有 3 个清理调用点、状态机（普通 1 留 2 跳 3 留、读档全留、重置不泄漏）、配置 XML、FeatureGroup 第五开关、程序集整合，以及 Harmony 注入参数名 `__args`/`__result`。
- 构建 0 警告 0 错误；未启动游戏，未安装到游戏目录。

尚未验证：真实 Unity 协程/加载管线中的实际节省、跳过后的内存峰值、与全 Mod 组合的交互。必须由用户操作做 A/B 后才能声称游戏内收益。

## 6. A/B 方法

同一存档、同一路线（例如 WorldWoundGMArea → DrezenCapital），同一游戏会话或两次重启：

1. 关闭 `SkipDuplicateLoadCleanup`，做一次区域切换；记录 `perf-*.jsonl` 中 `UnloadUnusedAssets` 的次数/总耗时、`LoadAreaCoroutine`、`LoadingProcess` 总时长和切换后内存。
2. 打开开关，重复同一路线；应只看到一次 `UnloadUnusedAssets`（普通切换），读档仍两次。
3. 比较总加载时间、切换后 `mono_heap`/`unity_allocated`、下一次切换前的内存；若内存明显升高或出现资源缺失，关闭开关。

## 7. 0.5.0 追加：贴图图集队列加速

2026-10-21 日志里每次区域加载的 `AwaitTextureCompression` 是 0.84–3.35 s（6 次加载共 13.1 s）。原因在 `Game.AwaitTextureCompression`：

- 循环每帧调用一次 `CharacterAtlasService.Update()`；
- `Update()` 只取队首的**一个**请求处理（`m_Requests.RemoveAt(0)`），然后在 DXT 队列有积压时直接返回；
- 所以队列里有 N 个角色图集请求时，光排队就要 N 帧，外加真正的压缩时间。

0.5.0 的做法：在该协程运行期间（Harmony 前缀把返回的 `IEnumerator` 包一层，进入时开窗、结束即关），给 `CharacterAtlasService.Update` 加一个 postfix，每帧再调用 `Update()` 最多 `TextureWaitBurst-1` 次（默认 4 次/帧，上限 8）。门控：

- 只在 `AwaitTextureCompression` 的存活期内推进，游戏其它时间不变；
- 每次仍走同一个 FIFO 队首请求，顺序与结果不变；
- DXT 队列有积压时 `Update()` 自己会提前返回，不会越过限流堆积压缩任务；
- 重入保护：postfix 内部的 `Update()` 调用不会再次展开；
- 关闭开关或整个加载清理功能时立即停止推进。

代价：加载期间每帧的 CPU 峰值更高（仍在加载画面内），不改任何缓存/资源状态。真实节省取决于图集数量与 DXT 耗时占比，**未做游戏内 A/B**；下一次日志里的 `AwaitTextureCompression` 阶段时长会直接反映效果。

## 8. 0.5.0 实验项：读档跳过第一次清理（默认关）

读档切换的顺序是：`UnloadEntitiesCoroutine` → 清理 #1 → `SaveManager.LoadRoutine` → 清理 #2 → 区域加载。清理 #1 释放的是上一个场景遗留的资源；跳过它可以把约 2.85 s 挪到后面的清理 #2，但 #2 因此要做更多工作，净收益未经验证，内存峰值也会升高。

因此该开关默认关闭，并且只对系统内存 ≥ 12000 MB 的机器生效；普通切换的第二次清理跳过不受影响。开启后如果加载后内存明显升高或出现资源缺失，请关闭并反馈日志。
