# PerformanceImprove 0.5.0

**Targeted loading and lifecycle optimizations for Pathfinder: Wrath of the Righteous, with built-in diagnostics.**

PerformanceImprove is an experimental Unity Mod Manager + Harmony mod focused on area transitions, Drezen NPC models, level-up controller cleanup, and sold-item accumulation. Features have separate settings in the UMM panel.

## Features

| Feature | Default | What it does |
| --- | --- | --- |
| Level-up subscription cleanup | On | Captures the level-up controller subscription and disposes it after the controller stops. Does not force garbage collection. |
| Drezen NPC models on demand | On | Defers eligible distant NPC models in ordinary Drezen while retaining logical entities. Warms models near the party or camera; default party radius is 45 m. |
| Sold-item cleanup | On | Removes approved stackable items from the player's completed sale before they accumulate in merchant inventory. Existing merchant stock is left alone. **Removed items cannot be bought back.** |
| Duplicate area cleanup skip | On | Skips the second resource cleanup during ordinary area transitions. Save loading keeps both cleanups by default. |
| Faster character texture queue | On | Advances the character atlas queue several times per frame during texture-compression waits; default burst 4, clamped to 1–8. In-game performance still requires A/B testing. |
| Session diagnostics | On | Writes timings, preview-copy statistics, loading phases, memory samples and mod inventory to JSONL. |

Two experiments are **off by default**: `SpellbookCopyFast` skips already-known spell re-additions during preview copying; `SkipFirstCleanupOnSaveLoad` skips the first save-load cleanup only when the system memory gate (at least 12 GB) passes. These require controlled in-game testing. SpellbookCopyFast uses the diagnostics patch path, so keep Diagnostics enabled when testing it.

NPC safeguards cover party members, pets, special models, scripted access, dialogue, cutscenes and combat. The separate story-return Drezen map is excluded; this has not been verified against every story variant.

## Requirements and compatibility

- Windows installation of Pathfinder: Wrath of the Righteous with Unity Mod Manager installed.
- Current code targets the locally inspected **2.7.0x** game build. Compatibility guards use Assembly-CSharp SHA256 `2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953`; do not assume other builds work.
- Remove the old standalone WrathLifecycleFix and WrathDeferredNpcViews installations before using this merged mod. Standalone WrathPerformanceLab diagnostics are also superseded.
- Compatibility with other mods, including Performance Fixes, has not been exhaustively tested.

## Installation and removal

1. Exit the game and back up your saves.
2. Install the packaged `PerformanceImprove` folder under the game's `Mods` directory, or install the release ZIP using UMM.
3. Start the game, verify version 0.5.0 in UMM and review its individual settings. Disable sold-item cleanup if you want to retain buyback access.
4. When upgrading, preserve `Mods/PerformanceImprove/Settings.xml` and `Logs`.

To remove the mod, exit the game and remove its folder. Removing it cannot restore items already discarded by sold-item cleanup; use a prior save if necessary. Restart between A/B runs.

## Logs and feedback

Diagnostics are saved to `Mods/PerformanceImprove/Logs/perf-*.jsonl` (default limit 32 MiB per session). Include the game/mod versions, enabled features, reproduction steps and relevant log when reporting a problem. Logs include the installed mod list; review them before sharing publicly. Individual lifecycle, NPC and merchant logs have separate limits.

## Validation and current limits

The existing build workflow runs six suites against the merged DLL: lifecycle (32), NPC views (51), integration (22), merchant (40), diagnostics (31), and load cleanup (61): **237 independent checks**. These cover managed behavior, actual game IL/signatures, patching and configuration, not the full Unity runtime.

Historical 0.4.0 game logs recorded 21 captured/released lifecycle subscriptions and 28 deferred/restored NPC models without reported failures in those counters. That is a limited observation, not proof of universal compatibility or a benchmark. No measured FPS increase or fixed percentage loading improvement is claimed. Version 0.5.0 loading changes still need in-game A/B testing.

**Texture-wait fix:** the coroutine is now wrapped in a Harmony postfix after the original method returns. A regression test reproduced the inactive window with the old prefix, then passed with the fix. It uses the production patch registration on a managed coroutine factory and verifies yield preservation, completion and early disposal; it does not execute the Unity loading pipeline.

## Build from source

Use a .NET SDK capable of building net481 projects and .NET Framework 4.8.1 reference assemblies. Game, Unity, UMM and Harmony DLLs must come from your own installation; they are not included.

The projects currently contain local paths for `Managed` and NuGet caches. Adjust those properties for your machine (or pass `-p:Managed="<game>/Wrath_Data/Managed"` to direct dotnet builds). The packaging script also sets local cache directories; review it before running elsewhere.

```powershell
# Run from the repository root on the original development machine:
powershell -ExecutionPolicy Bypass -File scripts/build-performance-improve.ps1
```

The script builds, runs the six suites and produces `dist/PerformanceImprove-0.5.0.zip`. `analysis/` and `dist/` are generated/local directories excluded from source control. The script creates its local `analysis/` output directory when needed.

## Source layout

- `src/PerformanceImprove/`: UMM entry, feature switches, merchant cleanup, diagnostics and load optimizations.
- `src/WrathLifecycleFix/` and `src/WrathDeferredNpcViews/`: shared feature source linked into the merged DLL; old standalone entry points remain for historical builds.
- `src/WrathPerformanceLab/Telemetry.cs`: shared telemetry source.
- `tests/`: managed verification suites.
- `docs/`: feature rules and development analysis. Some documents describe historical versions and reference local evidence excluded from this repository.

Detailed rules: [mod guide](docs/performance-improve.md), [sold-item protections](docs/merchant-sale-cleanup.md), [load cleanup](docs/load-cleanup.md), [diagnostics](docs/diagnostics-log.md).

## Nexus description / 中文介绍

**名称：PerformanceImprove — 区域加载与生命周期优化**

针对《开拓者：正义之怒》的区域切换、眷泽城 NPC 模型加载及升级控制器订阅清理提供可单独开关的优化，并内置诊断日志，方便定位卡顿来源。

- 普通眷泽城按距离和镜头预热 NPC 模型，保留逻辑实体与剧情恢复入口。
- 升级控制器停止后释放其遗失的订阅，减少无效生命周期残留。
- 普通区域切换跳过第二次资源清理；读档默认维持原有清理次数。
- 可自动清理玩家已经成交的合规可堆叠卖出物品，保留任务、独特与无法确认安全的物品。**清理后无法回购，请按需关闭。**
- 提供加载、升级预览和内存诊断日志。贴图队列在加载等待期间每帧多推进几次；协程挂接问题已修复并通过回归测试，实际收益仍需游戏内验证。

这是实验性 Mod。当前针对 2.7.0x 本机程序集验证，不承诺固定加速比例、解决所有内存增长或闪退。237 项独立检查不能替代真实游戏及全 Mod 组合测试。请备份存档，并在 UMM 中确认需要开启的功能。

介绍结构参考 [ADDB 的 Performance Fixes](https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/797)：简介、功能、依赖、安装和兼容性。本项目关注的优化路径不同，未沿用该 Mod 的性能数字或分发其文件。
