# 诊断日志（PerformanceImprove 0.4.0）

给玩家（或自己）用的一页说明：打开游戏、做几个固定动作、把日志发回来。日志用于分析卡顿发生在哪一步，不是崩溃报告，也不包含存档内容。

## 日志在哪

`Mods/PerformanceImprove/Logs/perf-<yyyyMMdd-HHmmss>.jsonl`

一次游戏进程一个文件，退出游戏后写入完成。默认开启、默认 32 MiB 上限（达到上限后停止写入，其他功能日志不受影响）。UMM 面板会显示当前会话的日志路径。

## 记录了什么

| 记录 | 内容 |
|---|---|
| `session` | Mod 版本、游戏/Unity 版本、系统、内存、`Assembly-CSharp` SHA256、已装 Mod 清单（Id@版本，是否启用）、采样设置 |
| `probes` | 本次成功挂载的探针数量；`missing` 列出该游戏版本上没找到的类型/方法（换版本时第一时间看这条） |
| `copy` | 每次预览复制：总耗时、法术书数、已学法术数、`add_known` 次数、其中“已学过又被加一次”的 `add_known_duplicate`、`add_special`、`add_base_level`、各段耗时、`learn_ms`、重复比例 `redundant_share`、当时区域 |
| `timing` | 每 15 秒窗口的聚合：方法、调用次数、总耗时、最大耗时、异常数 |
| `loading` / `loading_start` | 加载阶段结束：阶段类型、耗时、区域 |
| `memory` | 每 15 秒与加载开始时的进程内存、GC 计数、Unity 分配/保留、Mono 堆 |

`timing` 覆盖的方法：`UnitHelper.CreatePreview/Copy/CopyFacts/CopyItems`、`LevelUpController` 的构造/Start/UpdatePreview/Commit/Stop/ApplyLevelUpActions、`UnitProgressionVM` 的构造/RefreshData、`ClassProgressionVM`/`ProgressionVM` 构造、`CharGenVM`、`CharGenFeatureSelectorStateVM`、`CharacterInfoVM`、`AreaDataStash`、`SceneLoader.MatchStateWithScene/LoadSceneEntities`、`ResourcesLibrary.CleanupLoadedCache`、`BundlesLoadService.RequestBundle`。

## 怎么采一份有用的日志

1. 进游戏，读一个**有高级别角色**的存档（20 级左右最能体现复制成本）。
2. 打开角色信息面板 → 进阶页，停留两秒，关闭。
3. 升一级：选职业、改两次选择、提交。
4. 打开新建角色/洗点界面，随便选几步再退出。
5. 从大地图进入眷泽城，等完全读完。
6. 找商人卖几堆可堆叠物品。
7. 正常退出游戏（让文件写完），把 `Logs` 下本次的 `perf-*.jsonl` 发回来。

做 A/B 时：先按上面做一遍（`SpellbookCopyFast` 关），再在面板打开该开关、重启游戏、用同一存档做**同样**动作，得到第二份日志。两份一起发。

## 分析

```
python scripts/analyze_performance_log.py <日志.jsonl> [更多日志...] --out report.md
```

输出：会话环境、每次复制的耗时分布（均值/中位/P95/最大）、重复 `AddKnown` 比例与可节省时间估算、按方法聚合的耗时表、加载阶段表、内存首末与峰值、缺失探针清单。可一次传多份日志做并排比较。

## 边界

- 日志只做观测；除默认关闭的 `SpellbookCopyFast` 外不改变游戏行为。
- 不记录存档内容、角色名或对话文本。
- 探针按方法名挂载，游戏版本不同可能缺失；缺失项会写在 `probes` 记录里，分析脚本会提示。
- 写日志失败只停日志，不影响游戏。
