# 眷泽城 NPC 按需加载可行性

2026-10-21，本机现有 Assembly-CSharp 反编译代码静态检查。本文为最初可行性分析；之后已按用户“实现”及扩大范围、只排除首次剧情进城的修正制作独立试验包，当前实现与限制见 [deferred-npc-views.md](deferred-npc-views.md)。尚未安装或游戏内验证；之前48.812秒记录仍是历史样本，不代表当前 Mod 组合的表现。

## 结论

可研究保留 NPC 逻辑实体、延迟完整模型和外观构建。游戏已有轻量 dummy view 和恢复路径，但当前选择条件主要是隐藏/区域分区，不是通用的玩家距离流式加载。不能仅修改一个“距离”参数就完整实现。

完全推迟 NPC 逻辑创建会改变生成事件、任务查询和存档语义；第一版应只针对明确审核过的环境 NPC，保留逻辑与原始生成顺序。镜头可离开队伍，激活判断必须结合所有受控单位、镜头和脚本需求。

## 本机源码证据

以下路径相对 `analysis/decompiled/Assembly-CSharp/`：

- `Kingmaker.Controllers/EntityCreationController.cs:200`：ShouldSpawnOptimizedDummy 排除跨场景状态，遵守 DisableUnitOptimization；隐藏且可简化，或在当前 AreaPart MechanicBounds 外时选 dummy。SpawnUnit 仍创建 UnitEntityData，并执行原生成管线；并非完全没有 NPC。
- `Kingmaker.View.Spawners/UnitSpawner.cs:29`：读取 SpawnerOptimizedUnit 的 SimplifyWhenHidden/SpawnHidden，并进入 EntityCreator。SpawnHidden 会影响 IsInGame，不能用来冒充纯粹延迟模型的开关。
- `Kingmaker.EntitySystem.Entities/UnitEntityData.cs:1077`：UpdateHasDummyView 根据 Suppressed、OptimizeWhenHidden/IsInGame 和休眠状态决定是否替换，恢复会 AttachToViewOnLoad、更新动画和可见性，销毁旧 view。
- `Kingmaker.EntitySystem.Entities/UnitEntityData.cs:1483`：CreateView 的常规路径按 HasDummyView 选择 dummy。变形与 Doll 分支在此前返回，不能假定每个 NPC 都经过同一模型入口。
- `Kingmaker.Controllers/SleepingUnitsController.cs:16`：睡眠判定与战斗、指令、过场、战争迷雾、同伴和镜头有关；醒来会调用 UpdateHasDummyView。扩展距离策略必须与这些条件共同工作，避免原控制器每帧把 dummy 提前还原。
- `Kingmaker.EntitySystem.Persistence.Scenes/SceneLoader.cs:1424`：存档现存实体走 AttachToViewOnLoad；只改新生成器会漏掉回城/读档的单位。
- `Kingmaker.EntitySystem.Persistence.Scenes/SceneLoader.cs:799`：生成阶段末尾会给缺失 view 的单位补建 view；简单返回 null 会被补回。
- `Kingmaker.Designers.EventConditionActionSystem.Evaluators/UnitFromSpawner.cs:23`：任务查询直接返回生成器的 SpawnedUnit；整体推迟实体时可能为 null。UnitSpawnerBase.MyData 也记录 HasSpawned/HasDied 并执行初始化器。
- `Kingmaker.ResourceLinks/ResourcesPreload.cs:39`：本版本名为 PreloadUnitViews 的状态遍历实际预加载头像；PreloadMapObjectViews 为空。不能根据方法名就把此处当成完整 NPC 模型预载而跳过。

## 建议实施边界

1. 先计时：区分首次生成和存档恢复的单位数据、完整模型/装备构建、dummy 恢复；统计眷泽城现有完整/dummy view、候选白名单数量及独占资源。确认可推迟工作确实占加载时间。
2. 限制眷泽城和经过审核的环境 NPC。队伍、宠物、同伴、战斗单位、过场演员与剧情需要的单位保持完整；名称、阵营或“当前没对话”不能单独作为安全分类依据。
3. 逻辑实体继续正常创建和注册；远处候选使用轻量 view，接近队伍或镜头前预热完整模型。保留唯一 ID、位置和原任务状态，不伪造 IsInGame/Suppressed。
4. 主线程按帧预算处理模型建立，提前触发并设置进入/退出不同阈值，减少走近时跳出和边界反复切换。单次 Unity 实例化可能超过预算，异步资源请求也不能把整个实体创建搬到后台线程。
5. 脚本取用、过场、传送、战斗和跨区域切换必须能立即满足完整 view 需求；逐个审查依赖，不能仅依靠一个全局 getter 拦截保证兼容。首次实验优先只延迟一次、不在远离时反复销毁重建。
6. 对比当前 Mod 组合下同存档、同入口的总加载时间、走动帧时间、完整模型建立时间和内存；验证进出城、城内存读档、对话与过场。按需创建不保证资源包立即卸载或内存下降。

上次48.812秒加载中，综合区域阶段28.569秒尚未分解，不能视为全部 NPC 成本；昼夜环境9.442秒、清理6.142秒等也不会因延迟 NPC 自动消失。当前无法承诺缩短多少秒。
