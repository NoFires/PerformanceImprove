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
