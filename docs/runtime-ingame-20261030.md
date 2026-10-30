# 游戏内验证：PerformanceImprove 0.5.0 / 0.5.1 贴图加速

时间：2026-10-30 13:34–14:34（Asia/Hong_Kong）
游戏：Pathfinder: Wrath of the Righteous 2.7.0x（Assembly-CSharp SHA256 `2cb7160b…8953`），52/52 Mod 加载成功
存档：`Manual_432_Defender_s_Heart_Final_celebreation_Gold_Dragon.zks`（31,927,896 字节），固定进入 `DrezenCapital`
操作：每个进程全新启动 → UMM 窗口关闭 → 主菜单“继续” → 等加载完成；未手动移动、未交易、未升级
诊断：`Mods/PerformanceImprove/Logs/perf-20261030-*.jsonl`；游戏自身 `GameLogFull.txt` 的 “Loading process finished” 作为交叉核对

## 结果

| 运行 | 版本 | 贴图加速 | LoadRoutine | LoadAreaCoroutine | AwaitTextureCompression | 额外推进计数 |
| --- | --- | --- | --- | --- | --- | --- |
| A | 0.5.0 | 开（未生效） | 88.23 s | 18.09 s | 5.057 s | 0 |
| B | 0.5.0 | 关 | 92.51 s | 10.23 s | 4.776 s | — |
| C | 0.5.0 | 开（未生效） | 87.64 s | 10.42 s | 5.160 s | 0 |
| D | 0.5.1 | 开，burst 4 | 146.50 s | 10.06 s | 4.634 s | 903 |
| E | 0.5.1 | 开，burst 8 | 86.35 s | 10.35 s | 5.192 s | 2 170 |
| F | 0.5.1 | 关 | 89.64 s | 10.39 s | 5.209 s | 0 |

每次加载都还有两次 `UnloadUnusedAssetsCoroutine`，2.41–3.16 s；总加载约 110–190 s，主要由 `SaveManager.LoadRoutine` 决定。

## 结论

1. **0.5.0 的贴图加速在游戏里从未生效。** 三次运行（A/C 开了开关）推进计数都是 0，`AwaitTextureCompression` 阶段仍是 5.06/5.16 s。游戏自身日志显示加载管理器运行的是原始类型 `<AwaitTextureCompression>d__267`，说明工厂方法的 postfix 没有改变交给加载管理器的枚举器（调用点很可能被内联）。
2. **0.5.1 的窗口生效了。** 新日志 `Texture burst installed: wait window follows LoadingProcess.TickLoading for <AwaitTextureCompression>d__267`；D 推进 903 次、E 推进 2 170 次，符合 burst 4/8 的预期；阶段类型名保持 `<AwaitTextureCompression>d__267`，没有替换枚举器，诊断与游戏日志格式不变。
3. **但加速没有可测收益。** 开启（D 4.63 s / E 5.19 s，均值 4.91 s）与关闭（B 4.78 s / F 5.21 s，均值 4.99 s）差异落在噪声内，burst 8 反而没有比 burst 4 更快。等待时间看起来由 DXT 压缩/图集实际工作决定，而不是每帧只推进一个请求的排队开销；多调用 `CharacterAtlasService.Update` 在 DXT 积压时本身会提前返回。
4. **总加载时间的真正大头是读档反序列化。** 同一 32 MB 存档的 `LoadRoutine` 在 86.35–146.50 s 之间波动，远大于区域场景（约 10–18 s）、两次资源清理（约 5.5–6.0 s）和贴图等待（约 5 s）。下一轮优化若要缩短“读档进眷泽城”，应针对 SaveManager 阶段，而不是贴图队列。

## 未覆盖

- 普通区域切换（非读档）的 `SkipDuplicateLoadCleanup` A/B：主菜单只能读档，本轮没有进入世界地图切换区域。
- NPC 延迟加载：本存档进入 Drezen 时 `deferred_total=0`、`create_calls=23`；可能该存档属于特殊 Drezen 变体（剧情回城图被排除），本轮没有验证延迟模型本身。
- 升级订阅清理：本轮没有升级，`captured_total=0`。
- 商人卖出清理：没有交易。
- 帧率、长时间内存增长、闪退：本轮不涉及。

## 文件

- 运行截图与 UMM 面板：`analysis/runtime/ingame-20261030/`
- 6 个诊断会话：`Mods/PerformanceImprove/Logs/perf-20261030-133400 / 135200 / 135925 / 141158 / 141744 / 142424.jsonl`
- 0.5.0 贴图加速未生效的直接证据：0.5.0 会话的 UMM 面板计数 0（`umm-c1.png`）与游戏日志阶段类型。
- 0.5.1 生效证据：`Player.log` 的 “Texture burst: pumped 903 / 2170 extra Update calls during the wait window.”
