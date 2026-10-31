# PerformanceImprove (v0.5.2)

[![Game Version](https://img.shields.io/badge/WotR-2.7.0x-blue.svg)](https://store.steampowered.com/app/1184370/Pathfinder_Wrath_of_the_Righteous/)
[![Mod Loader](https://img.shields.io/badge/Loader-UMM%20%2B%20Harmony-orange.svg)](https://www.nexusmods.com/site/mods/21)
[![Status](https://img.shields.io/badge/Status-Experimental-yellow.svg)](#)

> **Targeted loading, lifecycle optimizations, and diagnostics for *Pathfinder: Wrath of the Righteous*.**

PerformanceImprove is an experimental Unity Mod Manager (UMM) + Harmony mod designed to reduce save-load stalls, area-transition overhead, Drezen NPC rendering cost, level-up UI lifecycle leftovers and merchant inventory bloat. All modules can be toggled independently in the UMM GUI.

---

## ⚡ Features

### Core Modules (Default: Enabled)

| Feature | Default | Description |
| :--- | :---: | :--- |
| **Save-Upgrade Fast Path** | `On` | Skips the TabletopTweaks `-117` JSON migration when a save file contains no TabletopTweaks type names, so a provably no-op migration no longer parses and rewrites the whole save. |
| **Level-Up Subscription Cleanup** | `On` | Captures and disposes of orphaned level-up controller subscriptions after the interface closes (does not trigger forced GC). |
| **Drezen NPC Models On-Demand** | `On` | Defers rendering of distant NPC models in standard Drezen while preserving logical game entities. Models dynamically warm up near the camera or party (default radius: 45 m). |
| **Sold-Item Cleanup** | `On` | Automatically purges safe, stackable sold items from completed transactions before they bloat merchant inventories. Quest/unique items and original vendor stocks remain untouched. |
| **Skip Duplicate Area Cleanup** | `On` | Bypasses the redundant second resource-cleanup step during standard area transitions. Save loading keeps both cleanups by default. |
| **Accelerated Texture Queue** | `On` | Steps the character atlas queue multiple times per frame during texture-compression waits (default burst 4, clamped 1–8). In-game A/B showed no measurable gain; treat as experimental and disable if you want to avoid the extra per-frame calls. |
| **Session Diagnostics** | `On` | Collects transition timings, preview-copy stats, loading stages, memory snapshots and installed mod inventories into `.jsonl` logs. |

> [!WARNING]
> **Sold-Item Cleanup is irreversible:** purged items **cannot** be bought back. Disable this module in UMM if you rely on merchant buyback mechanics.

### Experimental Flags (Default: Disabled)

*   `SpellbookCopyFast`: Skips re-adding known spells during character preview generation. *(Requires Diagnostics to remain enabled).*
*   `SkipFirstCleanupOnSaveLoad`: Bypasses the initial cleanup pass during save loading **only** when system memory exceeds the 12 GB threshold gate.

### Safeguards & Exceptions
*   **NPC Entity Protection:** Party members, companion pets, unique/special model rigs, scripted interactables, cutscene actors, dialogue triggers and active combat units are excluded from deferred loading.
*   **Excluded Zones:** The story-altered variant of Drezen (late-game / mythic-specific return) is bypassed by default to avoid script desyncs.

---

## 🚀 What's new in 0.5.2 — save-load fast path

`SaveManager.LoadRoutine` took **96.93 s** on a 32 MB save (152 MB of JSON, ~500 zip entries). The cost was not deserialization: TabletopTweaks-Core registers JSON upgrader `-117`, whose `WillUpgrade()` returns true for every file and whose `Upgrade()` parses each JSON, walks every token, rewrites it and calls `GC.Collect()` — once per file. Saves created while TabletopTweaks-Core was disabled do not record `-117`, so every load re-ran the migration even though the save contained no `TabletopTweaks` string at all (0 hits in 151,752,001 characters).

0.5.2 adds `SkipNoOpSaveUpgrades`: a Harmony prefix on `JsonUpgradeSystem.Upgrade` that returns the original JSON only when the file's upgrader chain contains nothing but the TabletopTweaks migration and the text has no `TabletopTweaks` marker. Any other upgrader, a marker hit, or a reflection failure falls back to the game method.

Measured on the same campaign content:

| Scenario | LoadRoutine |
| :--- | ---: |
| Older save without `-117` (0.5.1) | 96.93 s |
| Same content, fast path **on** (0.5.2) | **5.09 s** |
| Same content, fast path **off** (0.5.2) | 102.83 s |
| Current save that already records `-117` | 2.17 s |

Affected saves drop by roughly 20×; saves that already record `-117` never ran the migration and are unaffected. The feature only skips work that provably cannot change the file; it never edits the save on disk.

0.5.1 also fixed the texture-wait window (it now follows `LoadingProcess.TickLoading`) and 0.5.0's factory wrapper was proven inactive; the window works, but measured no timing gain.

---

## 📋 Requirements & Compatibility

*   **Game Version:** Built and tested against local **v2.7.0x**.
    *   Target `Assembly-CSharp.dll` SHA256:  
        `2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953`
    *   *Compatibility with earlier or future patches is not guaranteed.*
*   **Dependencies:** [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) (Doorstop or Assembly injection).
*   **Mod Compatibility:** The save-upgrade fast path was checked against *Performance Fixes*, *WrathPatches* and *ModMenu*: none of them patches `JsonUpgradeSystem.Upgrade`, and the in-game log shows this mod as the only patch owner on that method. Other combinations are not exhaustively benchmarked; test configurations individually.

---

## 📦 Installation & Updating

### First-Time Install
1. Fully exit the game and **back up your save files** (`%UserProfile%\AppData\LocalLow\Owlcat Games\Pathfinder Wrath Of The Righteous\Saved Games`).
2. Extract the `PerformanceImprove` folder into `<GameDirectory>/Mods/`, or drag-and-drop the release `.zip` into the UMM interface.
3. Launch the game, open the UMM menu (<kbd>Ctrl</kbd> + <kbd>F10</kbd>), verify version **0.5.2**, and configure feature toggles as desired.

### Updating
*   Overwrite existing mod binaries, but **preserve** `Mods/PerformanceImprove/Settings.xml` and the `Logs/` directory to retain your custom configurations and diagnostic histories.
*   0.5.2 adds the `SkipNoOpSaveUpgrades` key; older settings files keep working and the key is added with its default (`true`).

### Uninstallation
*   Delete the `Mods/PerformanceImprove/` directory.
*   *Note: Removing the mod does not restore items previously erased by the Sold-Item Cleanup module. Revert to a manual save prior to the sales if necessary.*

---

## 🛠️ Diagnostics & Bug Reporting

When reporting issues or performance regressions, please supply:
1. Exact game build and mod version.
2. Active feature configuration (`Settings.xml`).
3. Steps to reproduce the issue.
4. Relevant diagnostic logs located at:  
   `Mods/PerformanceImprove/Logs/perf-*.jsonl` (capped at 32 MiB per session).

> [!NOTE]
> Diagnostic logs contain a manifest of installed mod names to aid conflict debugging. Review and redact logs before sharing them in public forums.

---

## ✅ Validation

The build runs six managed suites against the merged DLL: lifecycle (32), NPC views (51), integration (22), merchant (40), diagnostics (31) and load cleanup (88) — **264 independent checks**, including real Harmony registration, game IL/signature checks, XML configuration, enable/disable and unload paths. These checks do not execute the full Unity loading pipeline or every mod combination; in-game gains are only claimed for the measurements above.

Known limits of the 0.5.2 measurement: one machine, 52 mods, one campaign; the "older save" comparison used a copy of the same campaign with the header version marker removed to reproduce a pre-TabletopTweaks save. Normal area transitions, combat, long-session memory growth and main-menu startup were not re-tested in this round.

---

## 🇨🇳 中文说明 / Chinese Overview

**PerformanceImprove — 读档、区域加载与生命周期优化 (v0.5.2)**

针对《开拓者：正义之怒》的读档、场景切换、眷泽城 NPC 渲染与 UI 生命周期残留进行定向优化的实验性 UMM + Harmony Mod，内置轻量级诊断监控。

### 主要功能
*   **读档跳过无效存档升级**：当 TabletopTweaks 的 `-117` 迁移对文件不可能产生任何改动时直接跳过，避免整份 JSON 的解析、重写与强制 GC。实测受影响旧档读档约 **97–103 秒降到约 5 秒**；已带该版本标记的存档不受影响。
*   **眷泽城按需加载**：根据与队伍/视角的距离（默认 45 米）动态预热普通眷泽城的 NPC 模型，保留底层逻辑实体与剧情触发，减轻主城显存与渲染峰值。
*   **升级控制器订阅清理**：升级界面关闭后主动注销遗留的事件订阅，避免空闲生命周期泄露。
*   **过图冗余清理优化**：跳过普通区域切换时的二次资源清理阶段；存档读取默认维持双重清理以保证稳定。
*   **商人售出物堆叠精简**：自动清理玩家已完成交易的安全可堆叠杂物，防止后期商人清单无限膨胀。*(注：被清理物品无法回购，保留任务与唯一物品)*。
*   **贴图压缩队列加速**：在贴图压缩等待阶段提高每帧对角色图集队列的处理频次（默认 4 次）；游戏内 A/B 未测出收益，属实验项。
*   **会话级结构化日志**：将过图耗时、预览拷贝开销、内存采样与 Mod 清单输出至 `.jsonl` 文件，便于性能溯源。

### 注意事项
*   目标适配版本为 **2.7.0x**；与 Performance Fixes、WrathPatches、ModMenu 的补丁目标已核对无重叠，但未做全 Mod 组合的长测。
*   升级时请保留 `Settings.xml` 以沿用个人偏好设置。
