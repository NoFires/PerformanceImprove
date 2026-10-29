# 深入分析源码证据索引

此索引定位证据，不代表所有条目都是已发生的故障。完整解释见 deep-analysis.md。

## R01：升级控制器的 LateUpdate 静态保留链

证据类别：`strong_static_risk`

- [Assembly-CSharp/Kingmaker.UnitLogic.Class.LevelUp/LevelUpController.cs:195](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.UnitLogic.Class.LevelUp/LevelUpController.cs:195)
- [Owlcat.Runtime.UniRx/Owlcat.Runtime.UniRx/LastValueOnLateUpdateObservableFabric.cs:30](E:/Program/Development/WrathPerformance/analysis/decompiled/Owlcat.Runtime.UniRx/Owlcat.Runtime.UniRx/LastValueOnLateUpdateObservableFabric.cs:30)
- [Owlcat.Runtime.UniRx/Owlcat.Runtime.UniRx/LastValueOnLateUpdateObservable.cs:44](E:/Program/Development/WrathPerformance/analysis/decompiled/Owlcat.Runtime.UniRx/Owlcat.Runtime.UniRx/LastValueOnLateUpdateObservable.cs:44)

## R02：中间 ReadOnlyReactiveProperty 未释放的上游订阅

证据类别：`semantics_verified`

- [Owlcat.Runtime.UniRx/Owlcat.Runtime.UniRx/UniRxExtensionMethods.cs:99](E:/Program/Development/WrathPerformance/analysis/decompiled/Owlcat.Runtime.UniRx/Owlcat.Runtime.UniRx/UniRxExtensionMethods.cs:99)
- [UniRx/UniRx/ReadOnlyReactiveProperty.cs:112](E:/Program/Development/WrathPerformance/analysis/decompiled/UniRx/UniRx/ReadOnlyReactiveProperty.cs:112)
- [Assembly-CSharp/Kingmaker.UI.MVVM._VM.CharGen.Phases.Class/CharGenClassPhaseVM.cs:97](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.UI.MVVM._VM.CharGen.Phases.Class/CharGenClassPhaseVM.cs:97)

## R03：LateUpdate 观察者完成时未执行取消清理

证据类别：`semantics_verified`

- [Owlcat.Runtime.UniRx/Owlcat.Runtime.UniRx/LastValueOnLateUpdateObserver.cs:32](E:/Program/Development/WrathPerformance/analysis/decompiled/Owlcat.Runtime.UniRx/Owlcat.Runtime.UniRx/LastValueOnLateUpdateObserver.cs:32)
- [UniRx/UniRx.Operators/OperatorObserverBase.cs:28](E:/Program/Development/WrathPerformance/analysis/decompiled/UniRx/UniRx.Operators/OperatorObserverBase.cs:28)
- [Owlcat.Runtime.UniRx/Owlcat.Runtime.UniRx/LastValueOnLateUpdateObservable.cs:41](E:/Program/Development/WrathPerformance/analysis/decompiled/Owlcat.Runtime.UniRx/Owlcat.Runtime.UniRx/LastValueOnLateUpdateObservable.cs:41)

## U01：预览角色复制和升级动作重放

证据类别：`confirmed_behavior_needs_timing`

- [Assembly-CSharp/Kingmaker.UnitLogic.Class.LevelUp/LevelUpController.cs:517](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.UnitLogic.Class.LevelUp/LevelUpController.cs:517)
- [Assembly-CSharp/Kingmaker.UnitLogic/UnitHelper.cs:125](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.UnitLogic/UnitHelper.cs:125)

## U02：角色进展 VM 构造模式的重复初始刷新

证据类别：`semantics_verified`

- [Assembly-CSharp/Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo/CharInfoComponentVM.cs:15](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo/CharInfoComponentVM.cs:15)
- [Assembly-CSharp/Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.Main/UnitProgressionVM.cs:31](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.Main/UnitProgressionVM.cs:31)

## U03：AddDisposable 线性查重形成批量二次成本

证据类别：`semantics_verified`

- [Owlcat.Runtime.UI/Owlcat.Runtime.UI.MVVM/BaseDisposable.cs:54](E:/Program/Development/WrathPerformance/analysis/decompiled/Owlcat.Runtime.UI/Owlcat.Runtime.UI.MVVM/BaseDisposable.cs:54)
- [Assembly-CSharp/Kingmaker.UI.MVVM._VM.CharGen.Phases.FeatureSelector/CharGenFeatureSelectorStateVM.cs:177](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.UI.MVVM._VM.CharGen.Phases.FeatureSelector/CharGenFeatureSelectorStateVM.cs:177)

## U04：推荐标记替换后的暂存累积

证据类别：`strong_static_risk`

- [Assembly-CSharp/Kingmaker.UI.MVVM._VM.CharGen.Phases.FeatureSelector/CharGenFeatureSelectorItemVM.cs:160](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.UI.MVVM._VM.CharGen.Phases.FeatureSelector/CharGenFeatureSelectorItemVM.cs:160)
- [Owlcat.Runtime.UI/Owlcat.Runtime.UI.MVVM/ViewBase.cs:62](E:/Program/Development/WrathPerformance/analysis/decompiled/Owlcat.Runtime.UI/Owlcat.Runtime.UI.MVVM/ViewBase.cs:62)

## U05：专长资格与嵌套选择反复遍历

证据类别：`confirmed_behavior_needs_timing`

- [Assembly-CSharp/Kingmaker.Blueprints.Classes.Selection/BlueprintFeatureSelection.cs:77](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.Blueprints.Classes.Selection/BlueprintFeatureSelection.cs:77)
- [Assembly-CSharp/Kingmaker.Blueprints.Classes.Selection/FeatureSelectionExtensions.cs:12](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.Blueprints.Classes.Selection/FeatureSelectionExtensions.cs:12)
- [Assembly-CSharp/Kingmaker.UnitLogic.Class.LevelUp/FeatureSelectionViewState.cs:43](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.UnitLogic.Class.LevelUp/FeatureSelectionViewState.cs:43)

## U06：角色面板纸娃娃与模型/装备合图

证据类别：`confirmed_behavior_needs_timing`

- [Assembly-CSharp/Kingmaker.UI.ServiceWindow/DollRoom.cs:530](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.UI.ServiceWindow/DollRoom.cs:530)
- [Assembly-CSharp/Kingmaker.UI.ServiceWindow/DollRoom.cs:200](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.UI.ServiceWindow/DollRoom.cs:200)

## L01：贴图压缩队列等待与后台任务异常完成风险

证据类别：`conditional_static_risk`

- [Assembly-CSharp/Kingmaker/Game.cs:1707](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker/Game.cs:1707)
- [DXTCompressor/Owlcat.Runtime.Visual.Dxt/DxtCompressorService.cs:196](E:/Program/Development/WrathPerformance/analysis/decompiled/DXTCompressor/Owlcat.Runtime.Visual.Dxt/DxtCompressorService.cs:196)
- [DXTCompressor/Owlcat.Runtime.Visual.Dxt/DxtCompressorService.cs:220](E:/Program/Development/WrathPerformance/analysis/decompiled/DXTCompressor/Owlcat.Runtime.Visual.Dxt/DxtCompressorService.cs:220)

## L02：DXT 回读失败后的 Persistent NativeArray 清理

证据类别：`conditional_static_risk`

- [DXTCompressor/Owlcat.Runtime.Visual.Dxt/DxtCompressorService.cs:186](E:/Program/Development/WrathPerformance/analysis/decompiled/DXTCompressor/Owlcat.Runtime.Visual.Dxt/DxtCompressorService.cs:186)
- [DXTCompressor/Owlcat.Runtime.Visual.Dxt/DxtCompressorService.cs:176](E:/Program/Development/WrathPerformance/analysis/decompiled/DXTCompressor/Owlcat.Runtime.Visual.Dxt/DxtCompressorService.cs:176)
- [DXTCompressor/Owlcat.Runtime.Visual.Dxt/DxtCompressorService.cs:167](E:/Program/Development/WrathPerformance/analysis/decompiled/DXTCompressor/Owlcat.Runtime.Visual.Dxt/DxtCompressorService.cs:167)

## L03：光照反射探针最长约一分钟等待路径

证据类别：`conditional_static_risk`

- [Assembly-CSharp/Kingmaker.EntitySystem.Persistence.Scenes/SceneLoader.cs:1124](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.EntitySystem.Persistence.Scenes/SceneLoader.cs:1124)
- [Assembly-CSharp/Kingmaker.EntitySystem.Persistence.Scenes/SceneLoader.cs:1133](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.EntitySystem.Persistence.Scenes/SceneLoader.cs:1133)

## L04：区域反序列化与多阶段实体恢复

证据类别：`confirmed_behavior_needs_timing`

- [Assembly-CSharp/Kingmaker.EntitySystem.Persistence/AreaDataStash.cs:103](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.EntitySystem.Persistence/AreaDataStash.cs:103)
- [Assembly-CSharp/Kingmaker.EntitySystem/SceneEntitiesState.cs:178](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.EntitySystem/SceneEntitiesState.cs:178)
- [Assembly-CSharp/Kingmaker.EntitySystem.Persistence.Scenes/SceneLoader.cs:1410](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.EntitySystem.Persistence.Scenes/SceneLoader.cs:1410)

## L05：自动存档、截图和提交等待

证据类别：`conditional_static_risk`

- [Assembly-CSharp/Kingmaker.EntitySystem.Persistence.Scenes/SceneLoader.cs:1493](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.EntitySystem.Persistence.Scenes/SceneLoader.cs:1493)
- [Assembly-CSharp/Kingmaker.EntitySystem.Persistence/SaveManager.cs:813](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.EntitySystem.Persistence/SaveManager.cs:813)

## L06：切图资源清理和原生卸载等待

证据类别：`confirmed_behavior_needs_timing`

- [Assembly-CSharp/Kingmaker/Game.cs:1332](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker/Game.cs:1332)
- [Assembly-CSharp/Kingmaker.Blueprints/ResourcesLibrary.cs:367](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.Blueprints/ResourcesLibrary.cs:367)

## L07：有限种类的 FX 池与高水位保留

证据类别：`bounded_cache_needs_measurement`

- [Assembly-CSharp/Kingmaker.Visual.Particles/GameObjectsPool.cs:23](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.Visual.Particles/GameObjectsPool.cs:23)
- [Assembly-CSharp/Kingmaker.Blueprints/ResourcesLibrary.cs:399](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.Blueprints/ResourcesLibrary.cs:399)

## L08：逐单位头像预载和同步 PNG 解码

证据类别：`conditional_static_risk`

- [Assembly-CSharp/Kingmaker.ResourceLinks/ResourcesPreload.cs:39](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.ResourceLinks/ResourcesPreload.cs:39)
- [Assembly-CSharp/Kingmaker.ResourceManagement/SpriteLoadingRequest.cs:33](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.ResourceManagement/SpriteLoadingRequest.cs:33)

## M01：VisualAdjustments2 持久化角色设置累积

证据类别：`local_data_and_static_risk`

- [VisualAdjustments2/VisualAdjustments2.Infrastructure/GlobalCharacterSettings.cs:22](E:/Program/Development/WrathPerformance/analysis/decompiled/VisualAdjustments2/VisualAdjustments2.Infrastructure/GlobalCharacterSettings.cs:22)
- [VisualAdjustments2/VisualAdjustments2.Infrastructure/GetVisualSettingsProvider_Patch.cs:20](E:/Program/Development/WrathPerformance/analysis/decompiled/VisualAdjustments2/VisualAdjustments2.Infrastructure/GetVisualSettingsProvider_Patch.cs:20)
- [VisualAdjustments2/VisualAdjustments2.Infrastructure/SaveHooker.cs:39](E:/Program/Development/WrathPerformance/analysis/decompiled/VisualAdjustments2/VisualAdjustments2.Infrastructure/SaveHooker.cs:39)

## M02：VisualAdjustments2 FX 字典强引用 ItemEntity

证据类别：`strong_static_risk`

- [VisualAdjustments2/VisualAdjustments2.Infrastructure/CharacterSettings.cs:31](E:/Program/Development/WrathPerformance/analysis/decompiled/VisualAdjustments2/VisualAdjustments2.Infrastructure/CharacterSettings.cs:31)
- [VisualAdjustments2/VisualAdjustments2.Infrastructure/UnitViewHandSlotData_UpdateWeaponEnchantmentFX_Patch.cs:51](E:/Program/Development/WrathPerformance/analysis/decompiled/VisualAdjustments2/VisualAdjustments2.Infrastructure/UnitViewHandSlotData_UpdateWeaponEnchantmentFX_Patch.cs:51)

## M03：ToyBox 增强法术书的事件和 LateUpdate 释放

证据类别：`strong_static_risk`

- [0ToyBox0/ToyBox/EnhancedSpellbookController.cs:92](E:/Program/Development/WrathPerformance/analysis/decompiled/0ToyBox0/ToyBox/EnhancedSpellbookController.cs:92)
- [0ToyBox0/ToyBox/EnhancedSpellbookController.cs:82](E:/Program/Development/WrathPerformance/analysis/decompiled/0ToyBox0/ToyBox/EnhancedSpellbookController.cs:82)
- [0ToyBox0/ToyBox/EnhancedSpellbookController.cs:365](E:/Program/Development/WrathPerformance/analysis/decompiled/0ToyBox0/ToyBox/EnhancedSpellbookController.cs:365)

## M04：战术战斗先攻视图的未保存 LateUpdate 订阅

证据类别：`strong_static_risk`

- [Assembly-CSharp/Kingmaker.UI.MVVM._PCView.TacticalCombat.InitiativeTracker/TacticalCombatInitiativeTrackerBaseView.cs:122](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.UI.MVVM._PCView.TacticalCombat.InitiativeTracker/TacticalCombatInitiativeTrackerBaseView.cs:122)
- [Assembly-CSharp/Kingmaker.UI.MVVM._PCView.TacticalCombat.InitiativeTracker/TacticalCombatInitiativeTrackerPCView.cs:122](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.UI.MVVM._PCView.TacticalCombat.InitiativeTracker/TacticalCombatInitiativeTrackerPCView.cs:122)

## M05：自定义头像请求移除不等同于 Destroy 贴图

证据类别：`conditional_static_risk`

- [Assembly-CSharp/Kingmaker.ResourceManagement/ResourceStorage.cs:52](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.ResourceManagement/ResourceStorage.cs:52)
- [CustomNpcPortraits/CustomNpcPortraits/GetPortrait_Patch.cs:123](E:/Program/Development/WrathPerformance/analysis/decompiled/CustomNpcPortraits/CustomNpcPortraits/GetPortrait_Patch.cs:123)

## M06：VisualAdjustments2 颜色与眼睛贴图静态缓存

证据类别：`bounded_by_usage_needs_measurement`

- [VisualAdjustments2/VisualAdjustments2.Infrastructure/EeInfraStructure.cs:8](E:/Program/Development/WrathPerformance/analysis/decompiled/VisualAdjustments2/VisualAdjustments2.Infrastructure/EeInfraStructure.cs:8)
- [VisualAdjustments2/VisualAdjustments2.Infrastructure/CustomEyePictures.cs:14](E:/Program/Development/WrathPerformance/analysis/decompiled/VisualAdjustments2/VisualAdjustments2.Infrastructure/CustomEyePictures.cs:14)

## C01：WrathPatches 类型绑定启动日志

证据类别：`historical_cause_identified`

- [WrathPatches/WrathPatches/UmmModsToGuidClassBinder.cs:48](E:/Program/Development/WrathPerformance/analysis/decompiled/WrathPatches/WrathPatches/UmmModsToGuidClassBinder.cs:48)

## C02：Isekai 进展树防循环属于蓝图构建线索

证据类别：`historical_cause_identified`

- [IsekaiMod/IsekaiMod.Utilities/PatchTools.cs:583](E:/Program/Development/WrathPerformance/analysis/decompiled/IsekaiMod/IsekaiMod.Utilities/PatchTools.cs:583)

## C03：DarkCodex Prebuild 调试 I/O 不能假定已启用

证据类别：`deprioritized_unproven_activation`

- [DarkCodex/DarkCodex/Patch_Prebuilds.cs:20](E:/Program/Development/WrathPerformance/analysis/decompiled/DarkCodex/DarkCodex/Patch_Prebuilds.cs:20)
- [DarkCodex/DarkCodex/Patch_Prebuilds.cs:213](E:/Program/Development/WrathPerformance/analysis/decompiled/DarkCodex/DarkCodex/Patch_Prebuilds.cs:213)

## F01：排除子 VM Dispose 后必然被父列表继续持有

证据类别：`false_positive_excluded`

- [Owlcat.Runtime.UI/Owlcat.Runtime.UI.MVVM/BaseDisposable.cs:145](E:/Program/Development/WrathPerformance/analysis/decompiled/Owlcat.Runtime.UI/Owlcat.Runtime.UI.MVVM/BaseDisposable.cs:145)

## F02：单位 Dispose 已清单位订阅并注销实体

证据类别：`existing_cleanup_confirmed`

- [Assembly-CSharp/Kingmaker.EntitySystem.Entities/UnitEntityData.cs:1735](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.EntitySystem.Entities/UnitEntityData.cs:1735)
- [Assembly-CSharp/Kingmaker.EntitySystem/EntityDataBase.cs:551](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.EntitySystem/EntityDataBase.cs:551)

## F03：专长列表每个阶段有首次创建保护

证据类别：`existing_optimization_confirmed`

- [Assembly-CSharp/Kingmaker.UI.MVVM._VM.CharGen.Phases.FeatureSelector/CharGenFeatureSelectorPhaseVM.cs:177](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.UI.MVVM._VM.CharGen.Phases.FeatureSelector/CharGenFeatureSelectorPhaseVM.cs:177)

## F04：PC 上 UnitAtlas LOD 服务和图集预算分支未启用

证据类别：`platform_path_excluded`

- [Assembly-CSharp/Kingmaker.Controllers.Units/UnitAtlasService.cs:103](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.Controllers.Units/UnitAtlasService.cs:103)
- [Assembly-CSharp/Kingmaker.Visual.CharacterSystem/CharacterAtlas.cs:70](E:/Program/Development/WrathPerformance/analysis/decompiled/Assembly-CSharp/Kingmaker.Visual.CharacterSystem/CharacterAtlas.cs:70)
