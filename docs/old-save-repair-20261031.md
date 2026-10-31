# 旧存档修复记录（2026-10-31）

## 目标存档

`Saved Games/Manual_420_Secret_Path_Angel.zks`（原文件未改动）

- 保存于 2021-11-17（游戏 2.0 时代），`Versions` = 10/20/30/40/50/60，没有 `header.json.ModList.json`
- 区域 `3ffcc27e3b3ccbe41973bc0edb0ecdc4`（`ThresholdIndoor_SecondFloor`），名称 Secret Path Angel
- 内容扫描未发现 `TabletopTweaks` / `ToggleableThrowingWeapons` / `BubbleBuffs` / `HomebrewArchetypes` 等类型名，说明没有绑定到这些 Mod 的蓝图

## 现象

在装有 49 个 Mod 的当前环境直接载入：

1. 第一次失败：`ItemEntity.ApplyEnchantments` 在恢复/移除旧附魔（日志里的 `RadianceGoodEnchantment6`）时，`AddFactContextActions.OnDispose` 调用 `ItemEnchantment.RunActionInContext`，因为附魔上下文尚未激活而抛
   `You can't RunActionInContext while ItemEnchantment is not active`，读档中断。
2. 给 0.5.3 加了 `LegacySaveEnchantmentTolerance`（默认开，跳过“上下文为空”的 Dispose 动作）后，读档继续推进到玩家升级器（`History.System: Applying player upgrader from blueprint: PF-...`），随后游戏崩溃（Player.log 出现 `Uploading Crash Report`）。

结论：旧档本身能被当前版本读取，是**旧档数据 + 全部 Mod 的相互作用**导致失败；单靠一个附魔容错补丁不够。

## 有效修复流程（已实测）

1. 关闭全部 Mod（UMM `Params.xml` 中所有 `Mod.Enabled=false`，修改前已备份到 `analysis/installations/params-20261031-122554.xml`）。
2. 启动游戏，载入旧档：**成功**，`Load Area ThresholdIndoor_SecondFloor` → `Loading is ended in 14.49s`。
3. 在无 Mod 状态下快速保存（F5），得到 `Quick_160.zks`（时间 2026-10-31 12:29）。
4. 恢复全部 49 个 Mod 的启用状态，重启。
5. 载入 `Quick_160`：**成功**，`Loading is ended in 29.67s`，进入世界。
6. 由快速存档复制出正式存档 `Manual_9904_SecretPath_Repaired.zks`（header：`Name="SecretPath 修复"`、`Type="Manual"`），再次载入验证：**成功**，`Loading is ended in 29.50s`。

所以“修复”的本质是：用**无 Mod 的干净进程**把旧档读进来并重新保存，生成一份没有旧版本遗留问题、也不包含任何 Mod 类型的新存档，再回到全 Mod 环境使用。

## 产物

- 可载入存档：`Saved Games/Manual_9904_SecretPath_Repaired.zks`（23,497,452 字节，列表显示 `SecretPath 修复`）
- 备份：`Saved Games/Quick_160.zks`（无 Mod 环境下的原始快速存档）
- 原档：`Saved Games/Manual_420_Secret_Path_Angel.zks` 保持不变
- Mod 侧改动：`PerformanceImprove 0.5.3` 新增 `LegacySaveEnchantmentTolerance`（默认开，可在 UMM 关闭）；它能让旧档读得更远，但单独使用不足以让这份旧档在全 Mod 环境完成载入，真正生效的是上面的干净读取流程。

## 注意

- 干净保存后的新档只会保留原档里存在的数据；旧档里不存在的 Mod 内容不会凭空出现，这是正常代价。
- 如果以后还想让旧档直接在全 Mod 环境载入，需要逐个 Mod 二分定位是哪一个（或哪几个）在旧数据上崩溃；本次没有做二分。
