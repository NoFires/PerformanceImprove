# 读档与主菜单卡顿分析（2026-10-30 晚）

本轮目标：`SaveManager.LoadRoutine` 的 86–147 s 读档时间。结论是**不是反序列化慢，而是 TabletopTweaks-Core 的 -117 存档升级器在每次读档时把整份 JSON 全量重跑一遍**；0.5.2 已实现安全快速路径，并在游戏内做了 ON/OFF 对照。另外附主菜单启动时间的分解、可做的方向，以及本轮新增的输入录制/回放工具。

## 1. 根因

- 存档 `Manual_432`（同上战役，31.9 MB zip，解压后 152 MB JSON、497 个条目）在 0.5.1 下读档耗时 **96.93 s**。游戏日志里同时出现 **338 次** `[TabletopTweaks-Core] Migrating from TabletopTweaks$ to TabletopTweaks-Core`。
- `TabletopTweaks-Core` 在启动时注册 JSON 升级器 `-117`（`TabletopTweaksMigration`）：`WillUpgrade()` 对所有文件名返回 true；`Upgrade()` 对每个文件执行 `JObject.Parse` → `SelectTokens("..$type")` → 正则替换 → `ToString` → **每文件一次 `GC.Collect()`**。
- 该存档是在 TTT Core 未启用时保存的（`Versions` 只有 `10,20,30,40,50,60`），所以每次读档都会重跑；而升级结果只写到临时 stash，不回写存档，于是**每次读档都重跑**。
- 直接证据：扫描该存档全部 151,752,001 个字符，**0 个文件**包含 `TabletopTweaks`。这次迁移对它是纯空操作。

## 2. 修复（PerformanceImprove 0.5.2）

- 新开关 `SkipNoOpSaveUpgrades`（默认开，可在 UMM 关闭）。
- Harmony prefix 挂在 `JsonUpgradeSystem.Upgrade(SaveInfo, string, string)`：
  - 先用 `json.IndexOf("TabletopTweaks")` 快速判断；出现就直接走原方法。
  - 否则通过私有 `GetUpgraders(SaveInfo, fileName)` 检查升级链，只有“唯一升级器是 `TabletopTweaks.Core.SaveUpgrades.TabletopTweaksMigration`”时才跳过，返回原 JSON。
  - 其它升级器（原版 10–60、未来其它 Mod 注册的）或反射失败一律回退原方法。
- 跳过的是“解析 + 遍历 + 重写 + 强制 GC”，输出与输入在内容上完全等价；不替换枚举器，不影响阶段名与诊断。
- 与已装 Mod 的冲突检查：Performance Fixes（ManyModsPerformanceFix）只碰粒子/作弊器补丁；WrathPatches 没有 Save/加载路径补丁；ModMenu 只对 `ShouldUpgrade` 跳过 `header.json` 并读取记录 Mod 列表，和本补丁正交。游戏内日志确认 `JsonUpgradeSystem.Upgrade` 的补丁 owner 只有 `PerformanceImprove.LoadCleanup.SaveUpgrade`。

## 3. 实测

同一份战役内容（用户 20:59 的 Manual_2824；另做了一份去掉 header `-117` 的合成副本，用来模拟“启用 TTT 前保存的旧档”，内容 502 条目、153 MB JSON）：

| 场景 | 版本/开关 | LoadRoutine | 迁移次数 | 跳过 |
| --- | --- | --- | --- | --- |
| 真实旧档（Manual_432，缺 -117） | 0.5.1，无快速路径 | **96.93 s** | 338 | 0 |
| 合成旧档 | 0.5.2，快速路径开 | **5.09 s** | 5（含 TTT 类型名的 5 个文件按设计回退） | 333 / 338 |
| 合成旧档 | 0.5.2，快速路径关 | **102.83 s** | 338 | 0 |
| 用户当前新档（已含 -117） | 0.5.2 | **2.17 s** | 0 | — |

- 受影响存档：约 **97–98 s 降到约 5 s**（约 20 倍）。
- 已经带 `-117` 的存档本来就不跑迁移，读档 2 s 级，本开关对它们没有副作用。
- 合成副本中 `party.json`、`DrezenCapital` 的 4 个区域文件、`header.json.ModList.json` 含 TTT 类型名，快速路径按设计回退，这 5 个文件仍走原迁移；其余 333 个全部跳过。

## 4. 主菜单启动（约 63 s）

`!!ModTimer` 统计（两次启动取较大值）：

| 项目 | 耗时 |
| --- | --- |
| Startup 总计 | 62.9–64.4 s |
| Mods 总计 | 49.1–50.9 s（约 78–79%） |
| 非 Mod（Unity/蓝图/引擎） | 约 13.5 s |

最重的 Mod：

| Mod | 耗时 |
| --- | --- |
| ToggleableThrowingWeapons | 10.15 s |
| CustomNpcPortraits | 4.38 s |
| TabletopTweaks-Base | 3.92 s |
| ExpandedContent | 3.65 s |
| PrestigePlus | 2.98 s |
| CodexLib | 2.62 s |
| Homebrew Archetypes | 2.60 s |
| KRXLib | 2.18 s |
| TomeOfTheFirebird | 1.84 s |
| MythicMagicMayhem | 1.72 s |
| ToyBox | 1.63 s |
| PerformanceImprove 自身 | 约 0.6–0.8 s |

可做的方向（本轮未实施）：

1. **精简 Mod 列表**：启动时间是 52 个 Mod 串行初始化的总和，禁用不用的重 Mod 是最直接、最安全的收益。
2. **定点优化个别 Mod 的 Load**：`ToggleableThrowingWeapons` 的 Load 主要是一次 `harmony.PatchAll()`（10 s）；`CustomNpcPortraits` 在启动时复制/解码头像图片（4.4 s）。这类热点可以像本次 TTT 迁移一样针对性打补丁，但每个只值几秒，需要单独分析其副作用。
3. **不建议延迟 Mod 初始化**：蓝图补丁必须在蓝图加载前完成，延后到主菜单之后会改变语义或直接失效。
4. UMM 的更新检查（`Params.xml` 的 `CheckUpdates=1`）影响很小，可关但不解决主要问题。

## 5. 输入录制/回放工具

新增：

- `scripts/ingame-input-recorder.ps1`：全局轮询键盘与鼠标按键（8 ms），输出 JSONL 时间线（`keydown/keyup/mouse/move/end`），DPI 感知；能记录注入的点击（验证：自动点击被记录为 `mouse down/up`）。
- `scripts/ingame-input-player.ps1`：按原时间线回放，支持 `-Speed` 调速；鼠标用 `SetCursorPos + mouse_event`，键盘用 `keybd_event`。

限制：轮询可能漏掉极短按键；鼠标移动按 1 s 采样（点击坐标是精确的）；回放依赖游戏窗口在前台。

## 6. 限制与注意事项

- “合成旧档”只改了 header 的 `Versions`、时间和名字，内容来自真实存档；测试后已删除，原存档未动。
- 本轮 OFF 运行的点击日志坐标异常（`-29962,-31700`，窗口最小化时读到旧 rect），但该次加载确实发生，且 338 次迁移 / 0 跳过 / 102.8 s 与“无快速路径”的预期一致；ON 运行点击正常（5.09 s）。
- 未测：普通区域切换、战斗、长时间内存、主菜单优化。
- 游戏已关闭，`Settings.xml` 已恢复默认（`SkipNoOpSaveUpgrades=true`）。

## 7. 产物

- 0.5.2 包：`dist/PerformanceImprove-0.5.2.zip`，DLL 304,640 字节；245→**264 项**检查（LoadChecks 88）。
- 说明：`docs/performance-improve.md`；验证记录：`analysis/performance-improve-0.5.2-verification.json`；安装记录：`analysis/installations/20261030-205342-performance-improve-0.5.2/`。
