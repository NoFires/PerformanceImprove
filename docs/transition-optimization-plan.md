# 进图/出图优化计划（全区域，不限眷泽城）

目标：缩短所有区域切换的加载时间，包括“出图”（卸载当前区域、销毁实体/视图、第一次资源清理）与“进图”（第二次清理、场景加载、实体/视图创建、昼夜/探针、贴图等待、完成回调）。眷泽城 NPC 延迟只是其中一条，不把它当作全部。

## 1. 切换阶段图（`Kingmaker.Game.LoadArea`）

非读档切换（世界地图/传送/门）在 `LoadingProcess` 中依次注册：

**出图**
1. `ConsolePreloadAreaCoroutine`（实机为空，~0）
2. `SceneLoader.UnloadAreaCoroutine`（卸载旧场景、音频 bank 标记）
3. `UnloadMainMenuRoutine`（仅从主菜单进入时）
4. `SceneLoader.UnloadEntitiesCoroutine`（销毁实体与视图）
5. `UnloadUnusedAssetsCoroutine` 第 1 次

**进图**
6. `UnloadUnusedAssetsCoroutine` 第 2 次（本 Mod 的 `SkipDuplicateLoadCleanup` 默认跳过）
7. `SceneLoader.LoadAreaCoroutine`（场景集加载 + 实体创建）
8. `AfterSceneLoaderFinishedArea`、`PreloadUnitResources`、`ApplyUpgrades`
9. `MatchTimeOfDayCoroutine`（光照/反射探针）
10. `AwaitTextureCompression` ×2（角色图集/DXT）
11. `AreaLoadingComplete`、自动存档

## 2. 已有数据（历史证据，非当前组合基准）

- 2026-10-22（3 个 Mod）：6 次 `LoadAreaCoroutine` 共 21.4 s（单次 2.5–6.85 s）；12 次 `UnloadUnusedAssets` 共 34.3 s（每次切换两次，各约 2.85 s）；纹理等待 13.1 s；自动存档 3.1 s；进眷泽城约 17.2 s。
- 2026-10-30（52 个 Mod，读档进眷泽城）：`LoadRoutine` 2.2 s；两次清理 2.4–3.2 s ×2；`LoadAreaCoroutine` 10.1–18.1 s；`MatchTimeOfDay` 2.1 s；纹理等待 4.6–5.2 s；`UnloadEntitiesCoroutine` 约 0.07–0.12 s（读档进入时旧区域为空）。
- 结论：**切换里最大头是两次资源清理（约 5–6 s）和 `LoadAreaCoroutine`（约 10–18 s，内部尚未细分）**；纹理等待已证明靠“多推进 Update”没有收益。

## 3. 候选优化（按收益/风险排序）

| # | 方案 | 预期收益 | 风险 | 状态 |
| --- | --- | --- | --- | --- |
| 1 | 跳过普通切换第 2 次 `UnloadUnusedAssets` | 约 2.4–3.2 s / 次 | 峰值内存略升；已按“第二次前无新资源”论证 | 已实现（0.4.1+，默认开），**未做游戏内 A/B** |
| 2 | 读档进入时跳过第 1 次清理（≥12 GB） | 约 2.4–3.2 s / 次读档 | 第一次的工作推给第二次，峰值内存上升 | 已实现（实验开关，默认关），未 A/B |
| 3 | 细分 `LoadAreaCoroutine` 内部（场景加载 / 实体创建 / 视图创建 / 区域激活） | 决定后续方向 | 只读探针 | **下一步** |
| 4 | 把“远处 NPC 延迟完整模型”推广到眷泽城以外区域 | 视图创建成本的一部分，量级未知 | 其它区域剧情/NPC 依赖更强，需要白名单/保护 | 未做；先看 #3 的数据 |
| 5 | 减少 `MatchTimeOfDay` 的反射探针刷新 | 最多约 1–2 s | 画面（反射/光照）变化，需你确认 | 未做 |
| 6 | 精简 `UnloadEntitiesCoroutine` 里的重复销毁/扫描 | 未知 | 改本体卸载逻辑，风险中高 | 未做；先测 |
| 7 | 贴图等待加速 | 0（已验证） | 无 | 建议关闭 |

## 4. 测量设计（Phase 1，只读）

在现有 `perf-*.jsonl` 的 `loading` 记录之外，补一组 `timing` 记录（Harmony 前后缀包裹真实方法，窗口内生成本归到当前 area）：

- `SceneLoader.LoadAreaCoroutine`：场景集加载、实体创建、视图创建分别计时（优先找 `LoadSceneCoroutine`/`BundledSceneLoader` 聚合点、`AreaPersistentState` 恢复、`UnitEntityView.CreateView`）。
- `SceneLoader.UnloadAreaCoroutine`：场景卸载、音频 bank 标记、UI 卸载。
- `SceneLoader.UnloadEntitiesCoroutine`：实体销毁、视图销毁、额外视图清理。
- 每次切换输出一条汇总：进入区域、来源区域、各阶段耗时、`LoadAreaCoroutine` 内部拆分、峰值内存。

输出到 `Logs/perf-*.jsonl` 的 `timing` 与一条 `transition` 汇总，**不改任何行为**，默认随诊断开关（可关）。

## 5. A/B 协议

同一存档、同一路线，进图与出图各跑一遍：

1. 关闭相关开关跑一轮（记录 `UnloadAreaCoroutine`、`UnloadEntitiesCoroutine`、两次清理、`LoadAreaCoroutine`、纹理、总时长、峰值内存）。
2. 打开开关再跑一轮。
3. 至少各 2 次取中位数；切换后确认单位/NPC/对话/任务正常，峰值内存不显著上升。
4. 读档进入另算一组（含 `LoadRoutine`）。

出图/进图需要游戏内真实切换区域，不能只靠主菜单读档；测试时要由用户操作或脚本走图。

## 6. 风险与回退

- 每个优化独立开关、默认关或条件开，不达标就回退。
- 不删除 `GC.Collect`/资源清理本身，只跳过可证明重复的整次调用；任何“减少清理”的改动都必须同时看峰值内存。
- 不延迟 Mod 初始化；不在战斗/过场中触发切换优化。
- 每次改动更新 `Settings.xml` 键、文档、验证 JSON 与安装记录。

## 7. 下一步

1. 实现 Phase 1 探针（0.5.3，只读），跑一次真实进图/出图，拿到 `LoadAreaCoroutine` 内部拆分。
2. 根据数据决定：#1/#2 是否值得默认开启、#4 是否推广、#5/#6 是否有明确收益。
3. 每项改动单独 A/B，通过后再进默认配置。

## 8. 2026-10-31 首个实测：DLC2 存档重载（0.5.2，GameLogFull 阶段）

TTW 被放弃后（40 个存档引用了它，加载会失败），改用不含 TTW 的 `DLC2` 存档（只读副本 `TEST DLC2`，区域 `DLC2_ApproachesToTheTavern`）。在游戏内 Esc → 载入游戏 → 同一存档，触发完整“出图 + 进图”：

| 阶段 | 耗时 |
| --- | ---: |
| UnloadAreaCoroutine（卸场景） | 0.400 s |
| UnloadEntitiesCoroutine（销毁实体） | 0.017 s |
| UnloadUnusedAssets 第 1 次 | 3.264 s |
| LoadRoutine（读档，1.9 MB 小档） | 0.162 s |
| UnloadUnusedAssets 第 2 次 | 3.017 s |
| LoadAreaCoroutine（场景+实体+视图） | 2.137 s |
| MatchTimeOfDayCoroutine | 1.371 s |
| AwaitTextureCompression ×2 | 1.028 + 0.050 s |
| 总计（Loading is ended） | **12.62 s** |

结论：即使是很小的 DLC2 区域，**两次 UnloadUnusedAssets 占 6.28 s（约 50%）**，是当前最值得动的通用项；`LoadAreaCoroutine` 只占 2.1 s。对普通切换（无读档），本 Mod 已默认跳过第 2 次清理（预计省 ~3 s，仍缺 A/B）；对读档进入，实验开关 `SkipFirstCleanupOnSaveLoad` 可跳过第 1 次（预计省 ~3.2 s，需验证内存峰值）。

本轮同时发现：当前游戏进程的诊断 `perf-*.jsonl` 为 0 字节（同一份 0.5.2 DLL 在上一进程正常）。下一轮先修这个，再装 0.5.3 探针取 `LoadAreaCoroutine` 内部拆分，并做清理开关的 A/B。

## 9. 2026-10-31 第二轮：清理跳过修复与验证

发现 `UnloadUnusedAssets` 跳过在此前版本从未生效：补丁挂在 `Game.UnloadUnusedAssetsCoroutine()` 工厂上，调用点在正式版被内联，前缀不执行；日志始终是两次真实清理（2.4–3.3 s × 2），UMM 计数 0。0.5.3 改在 `LoadingProcess.StartLoadingProcess` 前缀拦截：按“当前 LoadArea 的清理序号 + 迭代器类型 `<UnloadUnusedAssetsCoroutine>d__*`”把目标那一次换成空迭代器；类型不匹配不消耗计数。离线 97 项加载清理检查、全部 273 项通过。

游戏内验证（DLC2 存档，同区域）：

| 项 | 0.5.2 | 0.5.3 |
| --- | ---: | ---: |
| 第 1 次清理 | 3.264 s | 0.003 s（空） |
| 第 2 次清理 | 3.017 s | 2.449–2.556 s |
| 同进程第二次读档总时长 | 12.62 s | 7.85 s |

注意：新进程首次读档会叠加约 10 s 的 `MatchTimeOfDayCoroutine`（冷启动），总时长 21–22 s，与跳过无关；同启动内第二次回落到约 1.3 s。峰值内存：跳过开启时进程 WS 峰值约 3.9 GB（从启动开始采样），还需与关闭时对照后才能定默认值。

下一步：对比 OFF 的峰值内存；用真实普通切换（非读档）验证“跳过第 2 次清理”；再决定默认值与是否处理 `MatchTimeOfDay`。

## 10. 2026-10-31 第三轮：普通切换 A/B 与内存

为可复现地做“非读档切换”，0.5.3 加了测试钩子 `DebugReloadAreaKey`（默认关）：开启后 F11 用当前区域调用 `Game.LoadArea(area, null, AutoSaveMode.None, false, null, null)`，即 `saveInfo == null` 的普通切换，不写存档。DLC2 同区域结果：

| 项 | 跳过第 2 次清理（默认） | 不跳过 |
| --- | ---: | ---: |
| 第 1 次清理 | 2.549 s | 2.589 s |
| 第 2 次清理 | **0.0045 s（空）** | 2.565 s |
| LoadAreaCoroutine | 1.140 s | 1.852 s |
| MatchTimeOfDay | 1.349 s | 1.348 s |
| 总时长 | **6.59 s** | **9.91 s** |

普通切换收益约 **3.3 s**，和读档跳过第 1 次清理（11.24 s → 7.85 s，约 3.4 s）一致。

内存：读档路径跳过第 1 次清理时，进程 WS 峰值 3.90 GB；不跳过 3.87 GB（DLC2 小区域，差异在噪声内）。更大区域（眷泽城）暂时无法测：主战役存档都引用了已放弃的 TTW。

默认值决定：`SkipDuplicateLoadCleanup=true` 保留（已验证、通用）；`SkipFirstCleanupOnSaveLoad` 保持默认关（实验项，32 GB 机器上可手动开，收益约 3.4 s/读档，大区域峰值仍未知）。

冷启动 `MatchTimeOfDay`（每进程首次加载约 10 s，后续约 1.3 s）与清理无关，仍未处理。
