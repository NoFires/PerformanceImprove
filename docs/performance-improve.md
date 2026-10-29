# PerformanceImprove 0.5.0

2026-10-22，在 0.4.1 的区域加载清理之上加入**贴图图集队列加速**：加载等待期间每帧多推进几次角色图集队列，改变的是吞吐而不是结果。加上原有的升级订阅清理、眷泽城 NPC 模型按需加载、玩家已成交可堆叠卖出物品清理、普通区域切换的重复资源清理跳过（`SkipDuplicateLoadCleanup`），以及 0.4.0 的会话诊断日志与默认关闭的实验开关 `SpellbookCopyFast`。诊断日志详见 [诊断日志](diagnostics-log.md)，加载清理详见 [区域加载清理](load-cleanup.md)。

UMM 标识、显示名称、文件夹与主程序集分别为 `PerformanceImprove`、`PerformanceImprove`、`Mods/PerformanceImprove/`、`PerformanceImprove.dll`；入口是 `PerformanceImprove.Main.Load`。只需一个 DLL，不依赖或分发两个旧版 DLL。诊断功能已并入本 Mod，不再需要单独的 WrathPerformanceLab。

## 功能与开关

- **Level-up subscription cleanup**：保存升级控制器遗失的订阅句柄，在原 Stop 回调完成且 Stopped=true 后释放。保持原异常、重入和通知顺序，不强制 GC。对应原 WrathLifecycleFix。
- **Drezen NPC models on demand**：普通眷泽城延后远处非玩家 NPC 的完整模型，保留逻辑实体；接近队伍或镜头时预热。对应原 WrathDeferredNpcViews。默认广范围候选、45 米预热，可切换为四种市民的窄范围或 Profile only。
- **自动清理已成交的可堆叠卖出物品**：在原游戏发放卖出金钱、记录成交后，仅将玩家实际卖出的、符合保护规则的可堆叠物品直接移出并释放；普通附魔装备也可以清理。商人原有同类库存保持不变，买入流程不改。详见 [商人清理规则](merchant-sale-cleanup.md)。
- **跳过普通区域切换中重复的 UnloadUnusedAssets**（默认开）：`Game.LoadArea` 在非读档切换里连续注册两次 `UnloadUnusedAssetsCoroutine`，第二次之前没有任何新资源加载；只跳过第二次，第一次和 `UnloadAllOnEnter` 的第三次保留。读档切换（`saveInfo != null`）不跳过。2026-10-21 日志中普通切换每次两次清理约 2.8 s + 2.8 s，进眷泽城约 17.2 s；预计省约 2.8 s，未做游戏内 A/B。详见 [load-cleanup.md](load-cleanup.md)。

- **会话诊断日志**：`Logs/perf-<时间>.jsonl`，记录每次预览复制（含法术书子步骤计数）、升级/开卡界面刷新耗时、加载阶段、周期内存采样和已装 Mod 清单。用于玩家把日志发给作者分析，默认开启，可在面板关闭。

上述前四项性能开关默认开启（贴图加速默认开，读档清理跳过默认关）；诊断默认开启。卖出清理会让已清理物品无法回购；独特/命名装备、任务/钥匙/文书、非堆叠物品和不能确认安全的物品按原流程交给商人。NPC 模型功能仍保留玩家/跨场景/宠物、特殊模型、过场、指令与战斗等保护，并不保证所有 NPC 都可延迟。普通眷泽城区域为 `2570015799edf594daf2f076f2f975d8`，独立剧情回城地图 `83a099db95e0e6e4485a20b10ce7c28d` 被排除；第四章没有整章禁用。用户所述“第四章第一次进城”是否完全对应该剧情地图，仍需现场核对。

总开关关闭时先恢复本 Mod 待处理的 NPC 模型，再关闭订阅清理、加载清理和商人清理。模型恢复失败会拒绝关闭或卸载，并保留追踪；不能丢弃所有权后仍留着临时替身。订阅清理关闭时保留活动角色界面原有订阅；已释放的订阅不会重新建立。切换后重新打开只能捕获后续的新订阅，因此正式比较应重启游戏。

如果某项因版本/IL 不匹配而初始化失败，其他项可独立工作，UMM 面板会显示错误。若所有已选择的功能均失败，则报告整个 Mod 加载失败。存在已启用或仍在打补丁的旧 WrathLifecycleFix/WrathDeferredNpcViews 时拒绝加载，避免重复改写。

## 配置、日志与迁移

统一配置为 `Mods/PerformanceImprove/Settings.xml`，保存 LifecycleCleanup、DeferredNpcViews、MerchantSaleCleanup、Diagnostics、SkipDuplicateLoadCleanup、DiagnosticsSampleSeconds、DiagnosticsMaxMegabytes、SpellbookCopyFast 和 Npc 子设置。各功能日志在同一文件夹的 Logs 下，分别以 lifecycle-、npc-、merchant- 开头，每文件 2 MiB 上限；诊断日志为 perf-*.jsonl，默认 32 MiB 上限。商人日志写入失败或达到上限时暂停清理，物品恢复走原生售出流程；重新开关可创建新日志。内部 Harmony 所有权分别为 `PerformanceImprove.Lifecycle`、`PerformanceImprove.NpcViews`、`PerformanceImprove.Merchant`、`PerformanceImprove.Diagnostics`、`PerformanceImprove.LoadCleanup`，便于独立开关，仍只有一个 UMM 项目。

包 `dist/PerformanceImprove-0.5.0.zip` 包含 PerformanceImprove/ 下的 Info.json、PerformanceImprove.dll、README.md、merchant-sale-cleanup.md 和 load-cleanup.md。沿用现有 UMM/Harmony，不包含游戏 DLL。

0.4.1 升级到 0.5.0 仍是替换本 Mod 的同一组发行文件，保留 `Settings.xml`（新增 `FastTextureWait` 默认 true、`TextureWaitBurst` 默认 4、`SkipFirstCleanupOnSaveLoad` 默认 false）和 `Logs/`。给别人试用时只需整个 `PerformanceImprove/` 文件夹；回退就换回旧文件夹并删除新增的配置项。

迁移脚本 `scripts/install-performance-improve.py` 和 `scripts/install-performance-improve-0.4.0.py` 是历史安装路径，不要用它们覆盖当前新版本；已有安装记录在 `analysis/installations/`。

## 验证边界

构建脚本 `scripts/build-performance-improve.ps1` 对**合并后的 PerformanceImprove.dll**运行原订阅清理 32 项、NPC 功能 51 项、整合 22 项、商人功能 40 项、诊断日志 31 项和加载清理 56 项，共 **232 项**独立检查。新增检查包括 `Game.LoadArea` 六参数签名与 3 个清理调用点、状态机（普通第 2 次跳过、读档默认全保留、实验开关下读档跳第 1 次、12 GB 内存门槛、状态重置）、`Game.AwaitTextureCompression` 的私有 `IEnumerator` 签名、`CharacterAtlasService.Update` 公共签名，以及贴图加速的纯门控规则（窗口内才推进、重入不递归、1–8 上限）。

这些检查包含真实 Harmony、游戏 IL、UniRx 取消路径、弱引用、协程、配置 XML、启停控制与日志格式，但没有执行真实 Unity 加载管线、全 Mod 组合、任务或剧情现场，也没有在游戏内跑过加载清理 A/B。独立检查通过与文件打包完成，不代表已经确认游戏内性能收益。当前游戏程序集哈希保护仍为 `2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953`。

## 下一步

由用户操作做加载清理 A/B：同一存档、同一路线，先关闭 `SkipDuplicateLoadCleanup` 做一次区域切换，再打开做一次；比较 `perf-*.jsonl` 中 `UnloadUnusedAssets` 次数/耗时、`LoadAreaCoroutine`、总加载时间和切换后内存。读档切换应保持两次清理，不参与该优化。若出现资源缺失或内存明显升高，关闭开关并回退。现有旧组合 48.812 秒加载记录和其他历史计时只作参考，不与本轮混用。

## 给试用的玩家

1. 退出游戏，把 `PerformanceImprove` 整个文件夹放进 `Pathfinder Second Adventure\Mods\`（覆盖旧版）。
2. 启动游戏，UMM 里确认版本是 0.5.0、开关是你要试的状态。
3. 正常玩，遇到读档/切图卡顿就照常操作即可；诊断日志会自己写到 `Mods\PerformanceImprove\Logs\perf-*.jsonl`。
4. 想反馈问题，把最新的 `perf-*.jsonl` 发回来，并说明是否开了“贴图加速”和“读档跳过第一次清理”。
5. 想回退：换回旧文件夹即可；`Settings.xml` 里新增的三个键可以留着或删掉。