# PerformanceImprove 0.5.2

Pathfinder: Wrath of the Righteous 的实验性 UMM + Harmony 优化 Mod。当前面向本机 2.7.0x 程序集（Assembly-CSharp SHA256 `2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953`），所有功能有独立开关，诊断日志默认开启。

## 功能与开关

- **Level-up subscription cleanup**（默认开）：保存升级控制器遗失的订阅句柄，在原 Stop 回调完成且 `Stopped=true` 后释放；不强制 GC，保持原异常、重入与通知顺序。
- **Drezen NPC models on demand**（默认开）：普通眷泽城延后远处非玩家 NPC 的完整模型，保留逻辑实体；45 米队伍半径 + 镜头预热的按需恢复，队伍/跨场景/宠物/过场/战斗等保护。独立剧情回城地图 `83a099db95e0e6e4485a20b10ce7c28d` 被排除；普通眷泽城区域 `2570015799edf594daf2f076f2f975d8` 生效。
- **自动清理已成交的可堆叠卖出物品**（默认开）：只处理玩家已成交、已发钱的可堆叠卖出批次；保留独特/命名、任务、钥匙/文书、非堆叠和无法确认安全的物品。**清理后无法回购**，可按需关闭。
- **普通区域切换跳过第二次 `UnloadUnusedAssets`**（默认开）：`Game.LoadArea` 在非读档切换里连续注册两次清理，第二次之前没有任何新资源加载，只跳过第二次；读档切换保持两次。
- **读档跳过无效果的 TabletopTweaks -117 升级**（`SkipNoOpSaveUpgrades`，默认开，2026-10-30 新增）：当某文件的升级链只有 TabletopTweaks 的 `-117` 迁移、且文件文本完全不含 `TabletopTweaks` 时，直接返回原 JSON，省掉解析、遍历、重写和每文件一次的强制 GC。详见下节。
- **加载时加速角色贴图图集队列**（`FastTextureWait`，默认开，实验）：等待窗口内每帧多推进几次 `CharacterAtlasService.Update`；请求顺序与结果不变，DXT 积压时 Update 自行提前返回。2026-10-30 游戏内 A/B 未测出收益，见下节，可按需关闭。
- **会话诊断日志**（默认开）：`Logs/perf-<时间>.jsonl`，记录预览复制、升级/开卡刷新、加载阶段、内存采样与已装 Mod 清单。

实验开关默认关：`SkipFirstCleanupOnSaveLoad`（读档跳过第一次清理，需 ≥12 GB 内存）、`SpellbookCopyFast`（预览复制跳过已学法术）。

## 2026-10-30 读档优化：跳过无效的 TabletopTweaks 迁移

问题：`SaveManager.LoadRoutine` 对同一 32 MB 存档（解压 152 MB JSON、497 个条目）耗时 **96.93 s**。根因不是反序列化，而是 TabletopTweaks-Core 注册的 JSON 升级器 `-117`：`WillUpgrade()` 对所有文件返回 true，`Upgrade()` 对每个文件 `JObject.Parse` → `SelectTokens("..$type")` → 正则替换 → `ToString` → `GC.Collect()`。存档在 TTT Core 未启用时保存（`Versions` 只有 `10,20,30,40,50,60`），所以每次读档都重跑；结果只写入临时 stash，不写回存档。该存档 1.52 亿字符里没有任何 `TabletopTweaks` 字样，迁移是纯空操作。

修复（0.5.2）：Harmony prefix 挂在 `JsonUpgradeSystem.Upgrade(SaveInfo, string, string)`。先用 `IndexOf("TabletopTweaks")` 快速判断；无该子串时再检查私有 `GetUpgraders` 的结果，只有“唯一升级器是 `TabletopTweaks.Core.SaveUpgrades.TabletopTweaksMigration`”时才跳过。其它升级器、混合升级链或反射失败一律回退原方法。与 Performance Fixes、WrathPatches、ModMenu 无冲突（源码核对；游戏内日志确认 `Upgrade` 的补丁 owner 只有本 Mod；ModMenu 只跳过 header.json 的 ShouldUpgrade）。

实测（同一战役内容，502 条目、153 MB JSON）：

| 场景 | 版本/开关 | LoadRoutine | 迁移次数 | 跳过 |
| --- | --- | --- | --- | --- |
| 真实旧档（缺 -117） | 0.5.1 | 96.93 s | 338 | 0 |
| 合成旧档（去掉 header -117） | 0.5.2 开 | **5.09 s** | 5（含 TTT 名的文件回退） | 333 / 338 |
| 合成旧档 | 0.5.2 关 | 102.83 s | 338 | 0 |
| 用户当前新档（已含 -117） | 0.5.2 | 2.17 s | 0 | — |

结论：受影响的旧档从约 97–103 s 降到约 5 s（约 20 倍）；已含 `-117` 的存档本来就不跑迁移，没有副作用。合成旧档中 `party.json`、DrezenCapital 的 4 个区域文件、`header.json.ModList.json` 含 TTT 类型名，按设计回退原迁移。

## 2026-10-30 贴图加速修复（0.5.1）

0.5.0 的窗口挂在 `Game.AwaitTextureCompression()` 的工厂返回值上，但加载管理器拿到的是原始迭代器 `<AwaitTextureCompression>d__267`，推进计数恒 0；给迭代器 `MoveNext` 打补丁会被运行时以 `InvalidProgramException` 拒绝。0.5.1 改为在 `LoadingProcess.TickLoading` 前后读取私有 `m_CurrentLoadingProcess`，只有当前阶段是贴图迭代器时开启窗口；不替换枚举器，阶段名与日志格式不变。

游戏内结果：窗口生效（burst 4 推进 903 次、burst 8 推进 2 170 次），但 `AwaitTextureCompression` 时长开启 4.63–5.19 s、关闭 4.78–5.21 s，没有可测收益；burst 8 也不比 burst 4 快。等待更像由 DXT 压缩决定。**不要宣称该开关缩短读档时间**，可关闭以避免额外每帧调用。

## 配置与日志

统一配置 `Mods/PerformanceImprove/Settings.xml`：`LifecycleCleanup`、`DeferredNpcViews`、`MerchantSaleCleanup`、`Diagnostics`、`SkipDuplicateLoadCleanup`、`SkipFirstCleanupOnSaveLoad`、`SkipNoOpSaveUpgrades`、`FastTextureWait`、`TextureWaitBurst`、`DiagnosticsSampleSeconds`、`DiagnosticsMaxMegabytes`、`SpellbookCopyFast`、`Npc` 子设置。各功能日志在 `Logs/` 下，诊断日志 `perf-*.jsonl` 默认 32 MiB 上限。Harmony owner：`PerformanceImprove.Lifecycle`、`.NpcViews`、`.Merchant`、`.Diagnostics`、`.LoadCleanup`、`.LoadCleanup.SaveUpgrade`。

## 验证边界

构建脚本对合并后的 DLL 运行 6 个套件：订阅清理 32、NPC 51、整合 22、商人 40、诊断 31、加载清理 88，共 **264 项**独立检查，含真实 Harmony、游戏 IL/签名、UniRx 路径、XML 配置、启停与卸载。没有执行真实 Unity 载入管线、全 Mod 组合、任务或剧情现场；游戏内收益只以本轮记录为准，不外推。

## 安装与回退

退出游戏，把 `PerformanceImprove/` 整个文件夹放进 `Mods/`（覆盖旧版），保留 `Settings.xml` 与 `Logs/`。回退时换回旧文件夹并删除新增配置键即可。安装记录与回退路径在 `analysis/installations/`。

## 下一步

- 读档剩余成本：本次同一存档 `LoadAreaCoroutine` 10–18 s、两次 `UnloadUnusedAssets` 各约 2.4–3.2 s、贴图等待约 5 s，主因已从 LoadRoutine 转到场景与清理。
- 主菜单启动约 63 s，其中 Mods 约 50 s（78%）；最重的是 ToggleableThrowingWeapons 10.2 s、CustomNpcPortraits 4.4 s、TabletopTweaks-Base 3.9 s。先精简 Mod，再考虑对个别 Mod 的 Load 做定点优化；不要延迟 Mod 初始化（蓝图补丁必须先于蓝图加载）。
