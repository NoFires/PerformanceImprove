# PerformanceImprove (v0.5.0)

[![Game Version](https://img.shields.io/badge/WotR-2.7.0x-blue.svg)](https://store.steampowered.com/app/1184370/Pathfinder_Wrath_of_the_Righteous/)
[![Mod Loader](https://img.shields.io/badge/Loader-UMM%20%2B%20Harmony-orange.svg)](https://www.nexusmods.com/site/mods/21)
[![Status](https://img.shields.io/badge/Status-Experimental-yellow.svg)](#)

> **Targeted loading, lifecycle optimizations, and diagnostics for *Pathfinder: Wrath of the Righteous*.**

PerformanceImprove is an experimental Unity Mod Manager (UMM) + Harmony mod designed to alleviate stutter and memory pressure during area transitions, NPC rendering in Drezen, level-up UI workflows, and merchant inventory bloat. All modules can be toggled independently via the UMM GUI.

---

## ⚡ Features

### Core Modules (Default: Enabled)

| Feature | Default | Description |
| :--- | :---: | :--- |
| **Level-Up Subscription Cleanup** | `On` | Captures and disposes of orphaned level-up controller subscriptions after the interface closes (does not trigger forced GC). |
| **Drezen NPC Models On-Demand** | `On` | Defers rendering of distant NPC models in standard Drezen while preserving logical game entities. Models dynamically warm up near the camera or party (default radius: 45m). |
| **Sold-Item Cleanup** | `On` | Automatically purges safe, stackable sold items from completed transactions before they bloat merchant inventories. Quest/unique items and original vendor stocks remain untouched. |
| **Skip Duplicate Area Cleanup** | `On` | Bypasses the redundant second resource-cleanup step during standard area transitions. |
| **Accelerated Texture Queue** | `On` | Steps the character atlas queue multiple times per frame during texture-compression wait cycles (default burst: 4, clamped: 1–8). |
| **Session Diagnostics** | `On` | Collects transition timings, preview-copy stats, loading stages, memory usage snapshots, and installed mod inventories into `.jsonl` logs. |

> [!WARNING]
> **Sold-Item Cleanup is irreversible:** Purged items **cannot** be bought back. Disable this module in UMM if you rely on merchant buyback mechanics.

### Experimental Flags (Default: Disabled)

The following experimental features require manual testing and may vary in impact:

*   `SpellbookCopyFast`: Skips re-adding known spells during character preview generation. *(Requires Diagnostics to remain enabled).*
*   `SkipFirstCleanupOnSaveLoad`: Bypasses the initial cleanup pass during save loading **only** when system available memory exceeds the 12 GB threshold gate.

### Safeguards & Exceptions
*   **NPC Entity Protection:** Party members, companion pets, unique/special model rigs, scripted interactables, cutscene actors, dialogue triggers, and active combat units are completely excluded from deferred loading.
*   **Excluded Zones:** The story-altered variant of Drezen (late-game / mythic-specific return) is bypassed by default to avoid script desyncs.

---

## 📋 Requirements & Compatibility

*   **Game Version:** Built and tested against local **v2.7.0x**.
    *   Target `Assembly-CSharp.dll` SHA256:  
        `2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953`
    *   *Compatibility with earlier or future patches is not guaranteed.*
*   **Dependencies:** [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) (Doorstop or Assembly injection).
*   **Mod Compatibility:** Not exhaustively benchmarked alongside other performance mods (e.g., *Performance Fixes*). Test configurations individually.

---

## 📦 Installation & Updating

### First-Time Install
1. Fully exit the game and **back up your save files** (`%UserProfile%\AppData\LocalLow\Owlcat Games\Pathfinder Wrath Of The Righteous\Saved Games`).
2. Extract the `PerformanceImprove` folder into `<GameDirectory>/Mods/`, or drag-and-drop the release `.zip` into the UMM interface.
3. Launch the game, open the UMM menu (<kbd>Ctrl</kbd> + <kbd>F10</kbd>), verify version **0.5.0**, and configure feature toggles as desired.

### Updating
*   Overwrite existing mod binaries, but **preserve** `Mods/PerformanceImprove/Settings.xml` and the `Logs/` directory to retain your custom configurations and diagnostic histories.

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

## 🇨🇳 中文说明 / Chinese Overview

**PerformanceImprove — 区域加载与生命周期优化 (v0.5.0)**

针对《开拓者：正义之怒》的场景切换、眷泽城 NPC 渲染与 UI 生命周期残留进行定向优化的实验性 UMM + Harmony Mod，内置轻量级诊断监控。

### 主要功能
*   **眷泽城按需加载**：根据与队伍/视角的距离（默认 45 米）动态预热普通眷泽城的 NPC 模型，保留底层逻辑实体与剧情触发，减轻主城显存与渲染峰值。
*   **升级控制器订阅清理**：升级界面关闭后主动注销遗留的事件订阅，避免空闲生命周期泄露。
*   **过图冗余清理优化**：跳过普通区域切换时的二次垃圾回收/资源清理阶段；存档读取默认维持双重清理以保证稳定。
*   **商人售出物堆叠精简**：自动清理玩家已完成交易的安全可堆叠杂物，防止后期存档商人清单无限膨胀。*(注：被清理物品无法回购，保留任务与唯一物品)*。
*   **贴图压缩队列加速**：在贴图压缩等待阶段提高每帧对角色图集队列的处理频次（默认 4 次）。
*   **会话级结构化日志**：将过图耗时、预览拷贝开销、内存采样与 Mod 清单输出至 `.jsonl` 文件，便于性能溯源。

### 注意事项
*   目标适配版本为 **2.7.0x**，暂未与其他大型优化 Mod（如 Performance Fixes）进行交叉兼容测试。
*   升级时请保留 `Settings.xml` 以沿用个人偏好设置。
