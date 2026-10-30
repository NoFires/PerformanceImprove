# PerformanceImprove 0.5.2

**Targeted loading and lifecycle optimizations for Pathfinder: Wrath of the Righteous, with built-in diagnostics.**

PerformanceImprove is an experimental Unity Mod Manager + Harmony mod focused on save loading, area transitions, Drezen NPC models, level-up controller cleanup, and sold-item accumulation. Features have separate settings in the UMM panel.

## Features

| Feature | Default | What it does |
| --- | --- | --- |
| Level-up subscription cleanup | On | Captures the level-up controller subscription and disposes it after the controller stops. Does not force garbage collection. |
| Drezen NPC models on demand | On | Defers eligible distant NPC models in ordinary Drezen while retaining logical entities. Warms models near the party or camera; default party radius is 45 m. |
| Sold-item cleanup | On | Removes approved stackable items from the player's completed sale before they accumulate in merchant inventory. Existing merchant stock is left alone. **Removed items cannot be bought back.** |
| Duplicate area cleanup skip | On | Skips the second resource cleanup during ordinary area transitions. Save loading keeps both cleanups by default. |
| Save-upgrade fast path | On | Skips the TabletopTweaks -117 JSON migration when the file contains no TabletopTweaks type names, so a no-op migration no longer parses and rewrites the whole save. |
| Faster character texture queue | On | Advances the character atlas queue several times per frame during texture-compression waits; default burst 4, clamped to 1-8. In-game A/B showed no measurable gain; treat as experimental. |
| Session diagnostics | On | Writes timings, preview-copy statistics, loading phases, memory samples and mod inventory to JSONL. |

Two experiments are **off by default**: `SpellbookCopyFast` skips already-known spell re-additions during preview copying; `SkipFirstCleanupOnSaveLoad` skips the first save-load cleanup only when the system memory gate (at least 12 GB) passes. SpellbookCopyFast uses the diagnostics patch path, so keep Diagnostics enabled when testing it.

NPC safeguards cover party members, pets, special models, scripted access, dialogue, cutscenes and combat. The separate story-return Drezen map is excluded; this has not been verified against every story variant.

## Requirements and compatibility

- Windows installation of Pathfinder: Wrath of the Righteous with Unity Mod Manager installed.
- Current code targets the locally inspected **2.7.0x** game build. Compatibility guards use Assembly-CSharp SHA256 `2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953`; do not assume other builds work.
- Remove the old standalone WrathLifecycleFix and WrathDeferredNpcViews installations before using this merged mod. Standalone WrathPerformanceLab diagnostics are also superseded.
- Compatibility with other mods has not been exhaustively tested. The save-upgrade fast path was checked against Performance Fixes, WrathPatches and ModMenu: none of them patches `JsonUpgradeSystem.Upgrade`, and the in-game log shows this mod as the only patch owner on that method.

## Installation and removal

1. Exit the game and back up your saves.
2. Install the packaged `PerformanceImprove` folder under the game's `Mods` directory, or install the release ZIP using UMM.
3. Start the game, verify version 0.5.2 in UMM and review its individual settings. Disable sold-item cleanup if you want to retain buyback access.
4. When upgrading, preserve `Mods/PerformanceImprove/Settings.xml` and `Logs`.

To remove the mod, exit the game and remove its folder. Removing it cannot restore items already discarded by sold-item cleanup; use a prior save if necessary. Restart between A/B runs.

## Logs and feedback

Diagnostics are saved to `Mods/PerformanceImprove/Logs/perf-*.jsonl` (default limit 32 MiB per session). Include the game/mod versions, enabled features, reproduction steps and relevant log when reporting a problem. Logs include the installed mod list; review them before sharing publicly.

## Validation and current limits

The build workflow runs six suites against the merged DLL: lifecycle (32), NPC views (51), integration (22), merchant (40), diagnostics (31), and load cleanup (88): **264 independent checks**. These cover managed behavior, actual game IL/signatures, patching and configuration, not the full Unity runtime.

**Save-upgrade fast path (0.5.2):** the 2026-10-30 in-game measurement found `SaveManager.LoadRoutine` at 96.93 s on a 32 MB save, dominated by TabletopTweaks-Core's `-117` migration that rewrote ~500 JSON files although none of them contained a TabletopTweaks type name. On a copy of the same campaign with the `-117` marker removed, the fixed build loaded in **5.09 s** with the fast path on and **102.83 s** with it off (333 of 338 upgradeable files skipped). Saves that already record `-117` never run the migration and stay fast (2.17 s in this session).

**Texture-wait window (0.5.1):** the burst is active while `LoadingProcess.TickLoading` reports the `AwaitTextureCompression` iterator as the current stage. Wrapping the coroutine factory's return value (0.5.0) never activated the window; patching the iterator's `MoveNext` is rejected by the runtime (`InvalidProgramException`). With the window working, the stage took 4.63-5.19 s with the pump on and 4.78-5.21 s with it off, so the switch shows no measurable gain on save loads and should be treated as experimental.

## Build from source

Use a .NET SDK capable of building net481 projects and .NET Framework 4.8.1 reference assemblies. Game, Unity, UMM and Harmony DLLs must come from your own installation; they are not included. The projects contain local paths for `Managed` and NuGet caches; adjust them for your machine or pass `-p:Managed="<game>/Wrath_Data/Managed"`.

```powershell
# Run from the repository root on the original development machine:
powershell -ExecutionPolicy Bypass -File scripts/build-performance-improve.ps1
```

The script builds, runs the six suites and produces `dist/PerformanceImprove-0.5.2.zip`. `analysis/` and `dist/` are generated/local directories excluded from source control.

## Source layout

- `src/PerformanceImprove/`: UMM entry, feature switches, merchant cleanup, diagnostics, save-upgrade fast path and load optimizations.
- `src/WrathLifecycleFix/` and `src/WrathDeferredNpcViews/`: shared feature source linked into the merged DLL; old standalone entry points remain for historical builds.
- `tests/`: managed verification suites.
- `docs/`: feature rules and development analysis.

Detailed rules: [mod guide](docs/performance-improve.md), [save-load analysis](docs/runtime-saveupgrade-20261030.md), [sold-item protections](docs/merchant-sale-cleanup.md), [load cleanup](docs/load-cleanup.md), [diagnostics](docs/diagnostics-log.md).

## 中文简介

PerformanceImprove 是一个实验性的 UMM + Harmony 优化 Mod，针对读档、区域切换、眷泽城 NPC 模型、升级控制器订阅清理和已成交卖出物品。所有功能独立开关，默认开启会话诊断日志。

- 读档时跳过无效果的 TabletopTweaks `-117` 存档升级：受影响旧档实测从约 97–103 秒降到约 5 秒；已带该版本的存档不受影响。
- 普通眷泽城按距离延迟远处 NPC 的完整模型，保留逻辑实体，靠近队伍或镜头时恢复。
- 升级控制器停止后释放其遗失的订阅，不强制 GC。
- 普通区域切换跳过重复的第二次 `UnloadUnusedAssets`；读档默认保留两次清理。
- 加载等待期间每帧多推进几次角色贴图队列；2026-10-30 游戏内 A/B 未测出收益，属实验项。
- 已成交的可堆叠卖出物品可自动清理，**清理后无法回购**，独特与任务物品保留。

264 项独立检查覆盖托管行为、游戏 IL/签名、补丁与配置，但不能替代真实游戏与全 Mod 组合测试。请备份存档，并在 UMM 中确认需要开启的功能。
