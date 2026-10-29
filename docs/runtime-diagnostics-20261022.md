# 2026-10-22 诊断日志首轮运行分析

数据来源：`Mods/PerformanceImprove/Logs/` 下两段 0.4.0 会话日志，一名玩家（本机）操作。本文只写日志里能读到的东西，区分“已测”“静态推断”和“未验证”。

## 1. 数据有效性

| 项 | 第一段 | 第二段 |
|---|---|---|
| 文件 | `perf-20261021-232808.jsonl` | `perf-20261021-233551.jsonl` |
| 时长 | 约 3.7 分钟（23:28–23:32） | 约 38.2 分钟（23:35–00:14） |
| 记录 | 71 条 | 873 条 |
| 解析失败行 | 0 | 0 |
| 探针缺失 | 0 | 0 |
| 游戏哈希 | `2cb7160b…`（与分析基线一致） | 同左 |
| 启用 Mod | 49 / 52 | **3 / 52**（ToyBox、ModTagEx、PerformanceImprove） |
| 实验开关 `SpellbookCopyFast` | 关 | 关 |

`Assembly-CSharp` 哈希与静态分析基线一致，**所有探针都挂载成功**（`probes` 记录缺失 0 项），说明 0.4.0 在本机版本上可用。

两段环境不同：第一段是 49 个 Mod 的启动/主菜单，第二段把 49 个 Mod 关掉、只剩 3 个，做了实际游戏操作。**两段不能互相比较**，也不能直接和 2026-10-20 的旧记录比较。

## 2. 结论

1. **近原版（3 Mod）下，预览复制不是瓶颈**：55 次复制合计 227.6 ms（均值 4.1 ms，最大 28.4 ms），其中法术书部分合计约 3.0 ms（最大 0.8 ms/次）。2026-10-20 那次测得 `CopySpellbook` 均值 291 ms、峰值 805 ms——那次是 49 Mod + 高等级洗点，这次是 3 Mod + 小法术书角色。**两个变量同时变了，本轮无法把差异归因给 Mod 数量或角色等级中的某一个。**
2. **重复 `AddKnown` 确实存在，但在这个量级下不值钱**：1482 次 `AddKnown` 中 446 次是“已学过又被加一次”（30.1%），全部耗时合计 1.1 ms。按重复比例估算的节省上限约 0.3 ms。真正决定这条优化价值的是高等级、多法术书的角色。
3. **卡顿的大头是加载与资源清理，而且近原版就有**：一次区域切换要付两次 `UnloadUnusedAssets`（各约 2.8–2.9 s，合计约 5.7 s）；6 次区域加载中 `LoadArea` 合计 21.4 s（最大 6.85 s），纹理等待 13.1 s，昼夜环境 3.0 s。进眷泽城这一段从开始到可动约 **17.2 s**（清理 5.67 + LoadArea 6.85 + 昼夜 2.13 + 纹理 0.84 + 预载/卸载约 0.9），随后自动存档再花 **3.12 s**。这与“原版也卡”的反馈一致。
4. **两个既有优化在游戏内首次得到验证**（仅验证行为，不验证收益）：NPC 延迟视图在眷泽城延迟 28 个、加载末尾全部恢复 28 个，失败 0、容量跳过 0；订阅清理捕获 21 个、释放 21 个、失败 0。
5. **“开卡卡顿”在这段日志里没有复现**：角色面板 `RefreshData` 131 次共 131 ms（最大 16.8 ms），`ClassProgressionVM` 构造 138 次 82 ms，`ProgressionVM` 构造 177 次 76 ms，开卡界面 `CreateFeaturesList` 19 次 171 ms（最大 27.5 ms）。这和 2026-10-20 记录的 1.308 s 单次峰值差两个数量级，原因待查——最可能是角色等级/职业数量与 Mod 组合不同。

## 3. 数字明细

### 3.1 预览复制（第二段）

| 指标 | 值 |
|---|---|
| 复制次数 | 55 |
| `UnitHelper.Copy` 合计 / 均值 / 最大 | 227.6 ms / 4.1 ms / 28.4 ms |
| 法术书部分合计 / 均值 / 最大 | 约 3.0 ms / 0.05 ms / 0.76 ms |
| `AddKnown` 调用 / 重复 | 1482 / 446（30.1%） |
| 单次最多已知法术 | 65 条（2 本） |
| 按区域 | DrezenCapital 30 次、DLC6_TavernRebuilded 3 次、加载期 22 次（无法术书） |

对比：2026-10-20 洗点段 `CopySpellbook` 25 次 7 287 ms。**同一方法、不同场景，差距约三个数量级**；本轮角色只有 65 条已知法术，旧记录的角色约 400 条。

### 3.2 加载阶段（第二段，仅阶段结束记录）

| 阶段 | 次数 | 合计 | 最大 |
|---|---:|---:|---:|
| `Game+<UnloadUnusedAssetsCoroutine>` | 12 | 34 321 ms | 2 945 ms |
| `SceneLoader+<LoadAreaCoroutine>` | 6 | 21 427 ms | 6 845 ms |
| `Game+<AwaitTextureCompression>` | 12 | 13 101 ms | 3 345 ms |
| `SaveManager+<LoadRoutine>`（读档） | 4 | 4 971 ms | 1 432 ms |
| `SaveManager+<SaveRoutine>`（自动存档） | 1 | 3 116 ms | 3 116 ms |
| `SceneLoader+<ConsolePreloadAreaCoroutine>` | 7 | 3 055 ms | 511 ms |
| `Game+<MatchTimeOfDayCoroutine>` | 6 | 2 992 ms | 2 133 ms |
| `SceneLoader+<UnloadAreaCoroutine>` | 5 | 2 869 ms | 1 233 ms |
| `WrathDeferredNpcViews.WarmupSequence` | 6 | 1 132 ms | 337 ms |

每次区域切换的清理记录都成对出现（约 2.85 s + 约 2.85 s），这是“每次切换两次资源清理”的直接证据；旧报告里“两次清理 6.14 s”现在有了名字。

### 3.3 功能自证（第二段 CSV）

- `lifecycle`：`captured_total=21`、`disposed_total=21`、`dispose_failures=0`、`outstanding_tracked=0`。
- `npc`：眷泽城 `deferred_total=28`、`warmed_total=28`、`failures=0`、`capacity_skips=0`；warmup 屏障在 `2126.52`–`2126.65` 之间把 pending 从 22 清到 0（约 0.2 s）；`create_calls=462`/628 ms、`spawn_calls=829`/2 828 ms（会话累计）。

### 3.4 内存（Unity/Mono 口径）

- Mono 堆 0.84 → 1.45 GiB（峰值 1.45）；Mono 已用 0.83 → 1.24（峰值 1.43）；Unity 已保留 0.73 → 1.52 GiB。
- GC：gen0/gen1/gen2 同步增长 58 → 145（38 分钟内 87 次），与 12 次清理 + 6 次区域加载同量级。
- `private_bytes`/`working_set_bytes` **恒为 0**：Mono 下 `Process` 这两个属性不可用，这是已知限制，不能当内存读数用。

## 4. 局限

- 第二段几乎是原版（3 Mod），**不能代表 49 Mod 的实际体验**；第一段没有进入游戏玩法。
- 第二段角色法术书很小（≤65 条），**没有覆盖开卡/洗点最重的场景**。
- 两段都没有打开 `SpellbookCopyFast`，所以**没有 A/B**。
- 加载阶段只有聚合值，没有调用点信息；两次清理是否在处理同一批资源仍未确认。
- 进程内存没有可用读数。

## 5. 下一步

1. **补一次全 Mod + 高等级施法者的对照**：49 Mod 全开、用 20 级施法者（最好带神秘神学士/多本法术书），做升级/洗点/开卡，再进眷泽城。这是唯一能判断“Mod 放大”和“角色等级放大”哪一个是主因的数据。
2. **同一次会话里做 A/B**：先关 `SpellbookCopyFast` 做一遍，再打开做一遍（重启游戏），对比 `copy` 记录里的 `ms`、`add_known`、`duplicate`。
3. **0.4.1 诊断增强**（按本轮暴露的问题排优先级）：
   - 进程内存改用 `GetProcessMemoryInfo`（P/Invoke），替代恒为 0 的 `Process` 属性；
   - `copy` 记录补法术书标识（Blueprint guid/职业名）与角色等级，便于把日志和角色对上；
   - 给 `UnloadUnusedAssets` 记录调用者（`SceneLoader.UnloadAreaCoroutine` 还是别处），确认两次清理是否重复；
   - 加载阶段同时记录开始时间，能量化“阶段之间的空档”。

## 6. 复现命令

```
python scripts\analyze_performance_log.py "…\perf-20261021-232808.jsonl" "…\perf-20261021-233551.jsonl" --out report.md
```

原始日志保留在游戏目录 `Mods/PerformanceImprove/Logs/`；本报告只做读取，没有修改日志或存档。
