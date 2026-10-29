"""Generate a source-grounded evidence index and focused historical marker counts."""
import hashlib
import json
from collections import Counter
from pathlib import Path
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'analysis/decompiled'
GAME = Path(r'D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure')
LOGS = Path.home() / 'AppData/LocalLow/Owlcat Games/Pathfinder Wrath Of The Righteous'

# Markers locate evidence, not an automatic judgment that a source is defective.
CASES = [
    ('R01', '升级控制器的 LateUpdate 静态保留链', 'strong_static_risk', [
        ('Assembly-CSharp/Kingmaker.UnitLogic.Class.LevelUp/LevelUpController.cs', 'ObservableExtensions.Subscribe(UpdateCommandOnLateUpdate'),
        ('Owlcat.Runtime.UniRx/Owlcat.Runtime.UniRx/LastValueOnLateUpdateObservableFabric.cs', 'DontDestroyOnLoad'),
        ('Owlcat.Runtime.UniRx/Owlcat.Runtime.UniRx/LastValueOnLateUpdateObservable.cs', 'Instance.Remove(m_Node)')]),
    ('R02', '中间 ReadOnlyReactiveProperty 未释放的上游订阅', 'semantics_verified', [
        ('Owlcat.Runtime.UniRx/Owlcat.Runtime.UniRx/UniRxExtensionMethods.cs', 'return new ReadOnlyReactiveProperty<TProperty>'),
        ('UniRx/UniRx/ReadOnlyReactiveProperty.cs', 'sourceConnection.Dispose();'),
        ('Assembly-CSharp/Kingmaker.UI.MVVM._VM.CharGen.Phases.Class/CharGenClassPhaseVM.cs', 'GetReactiveProperty')]),
    ('R03', 'LateUpdate 观察者完成时未执行取消清理', 'semantics_verified', [
        ('Owlcat.Runtime.UniRx/Owlcat.Runtime.UniRx/LastValueOnLateUpdateObserver.cs', 'public override void OnCompleted()'),
        ('UniRx/UniRx.Operators/OperatorObserverBase.cs', 'Interlocked.Exchange(ref cancel, null)?.Dispose();'),
        ('Owlcat.Runtime.UniRx/Owlcat.Runtime.UniRx/LastValueOnLateUpdateObservable.cs', 'm_Observers.Remove(lateUpdateObserver);')]),
    ('U01', '预览角色复制和升级动作重放', 'confirmed_behavior_needs_timing', [
        ('Assembly-CSharp/Kingmaker.UnitLogic.Class.LevelUp/LevelUpController.cs', 'Preview = RequestPreview(m_NeedUpdateView);'),
        ('Assembly-CSharp/Kingmaker.UnitLogic/UnitHelper.cs', 'CopyFacts(unit, unitEntityData);')]),
    ('U02', '角色进展 VM 构造模式的重复初始刷新', 'semantics_verified', [
        ('Assembly-CSharp/Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo/CharInfoComponentVM.cs', 'AddDisposable(Unit?.Subscribe'),
        ('Assembly-CSharp/Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo.Sections.Progression.Main/UnitProgressionVM.cs', 'AddDisposable(m_JustGotClass.Subscribe')]),
    ('U03', 'AddDisposable 线性查重形成批量二次成本', 'semantics_verified', [
        ('Owlcat.Runtime.UI/Owlcat.Runtime.UI.MVVM/BaseDisposable.cs', '!m_Disposables.Contains(disposable)'),
        ('Assembly-CSharp/Kingmaker.UI.MVVM._VM.CharGen.Phases.FeatureSelector/CharGenFeatureSelectorStateVM.cs', 'source.Select((IFeatureSelectionItem item) => CreateNewSelectedVM(item))')]),
    ('U04', '推荐标记替换后的暂存累积', 'strong_static_risk', [
        ('Assembly-CSharp/Kingmaker.UI.MVVM._VM.CharGen.Phases.FeatureSelector/CharGenFeatureSelectorItemVM.cs', 'new RecommendationMarkerVM(recommendation)'),
        ('Owlcat.Runtime.UI/Owlcat.Runtime.UI.MVVM/ViewBase.cs', 'public void Unbind()')]),
    ('U05', '专长资格与嵌套选择反复遍历', 'confirmed_behavior_needs_timing', [
        ('Assembly-CSharp/Kingmaker.Blueprints.Classes.Selection/BlueprintFeatureSelection.cs', '!AllFeatures.Contains(feature)'),
        ('Assembly-CSharp/Kingmaker.Blueprints.Classes.Selection/FeatureSelectionExtensions.cs', 'return selection.Items.Any'),
        ('Assembly-CSharp/Kingmaker.UnitLogic.Class.LevelUp/FeatureSelectionViewState.cs', 'RefreshCanSelectState')]),
    ('U06', '角色面板纸娃娃与模型/装备合图', 'confirmed_behavior_needs_timing', [
        ('Assembly-CSharp/Kingmaker.UI.ServiceWindow/DollRoom.cs', 'private Character CreateAvatar('),
        ('Assembly-CSharp/Kingmaker.UI.ServiceWindow/DollRoom.cs', 'private void Cleanup()')]),
    ('L01', '贴图压缩队列等待与后台任务异常完成风险', 'conditional_static_risk', [
        ('Assembly-CSharp/Kingmaker/Game.cs', 'while (atlasService.RequestsCount > 0'),
        ('DXTCompressor/Owlcat.Runtime.Visual.Dxt/DxtCompressorService.cs', 'new Task(delegate'),
        ('DXTCompressor/Owlcat.Runtime.Visual.Dxt/DxtCompressorService.cs', 'm_IsComplete = true;')]),
    ('L02', 'DXT 回读失败后的 Persistent NativeArray 清理', 'conditional_static_risk', [
        ('DXTCompressor/Owlcat.Runtime.Visual.Dxt/DxtCompressorService.cs', 'Allocator.Persistent'),
        ('DXTCompressor/Owlcat.Runtime.Visual.Dxt/DxtCompressorService.cs', 'if (request.hasError)'),
        ('DXTCompressor/Owlcat.Runtime.Visual.Dxt/DxtCompressorService.cs', 'public void Clear()')]),
    ('L03', '光照反射探针最长约一分钟等待路径', 'conditional_static_risk', [
        ('Assembly-CSharp/Kingmaker.EntitySystem.Persistence.Scenes/SceneLoader.cs', 'float timeout = 60f;'),
        ('Assembly-CSharp/Kingmaker.EntitySystem.Persistence.Scenes/SceneLoader.cs', 'while (lightController.IsProbeBaking)')]),
    ('L04', '区域反序列化与多阶段实体恢复', 'confirmed_behavior_needs_timing', [
        ('Assembly-CSharp/Kingmaker.EntitySystem.Persistence/AreaDataStash.cs', 'Serializer.Deserialize<AreaPersistentState>'),
        ('Assembly-CSharp/Kingmaker.EntitySystem/SceneEntitiesState.cs', 'using (CodeTimer.New("ApplyPostLoadFixes"))'),
        ('Assembly-CSharp/Kingmaker.EntitySystem.Persistence.Scenes/SceneLoader.cs', 'private void LoadSceneEntities(')]),
    ('L05', '自动存档、截图和提交等待', 'conditional_static_risk', [
        ('Assembly-CSharp/Kingmaker.EntitySystem.Persistence.Scenes/SceneLoader.cs', 'while (Game.Instance.SaveManager.CommitInProgress)'),
        ('Assembly-CSharp/Kingmaker.EntitySystem.Persistence/SaveManager.cs', 'while (SaveScreenshotManager.Instance.HasTasksRunning)')]),
    ('L06', '切图资源清理和原生卸载等待', 'confirmed_behavior_needs_timing', [
        ('Assembly-CSharp/Kingmaker/Game.cs', 'public IEnumerator<object> UnloadUnusedAssetsCoroutine()'),
        ('Assembly-CSharp/Kingmaker.Blueprints/ResourcesLibrary.cs', 's_LoadedResource.Value.RequestCounter = 0;')]),
    ('L07', '有限种类的 FX 池与高水位保留', 'bounded_cache_needs_measurement', [
        ('Assembly-CSharp/Kingmaker.Visual.Particles/GameObjectsPool.cs', 'LRUCache<int, Queue<PooledGameObject>>'),
        ('Assembly-CSharp/Kingmaker.Blueprints/ResourcesLibrary.cs', 'GameObjectsPool.HasPoolInstances(component)')]),
    ('L08', '逐单位头像预载和同步 PNG 解码', 'conditional_static_risk', [
        ('Assembly-CSharp/Kingmaker.ResourceLinks/ResourcesPreload.cs', 'unitEntityData.Descriptor.Portrait.Preload();'),
        ('Assembly-CSharp/Kingmaker.ResourceManagement/SpriteLoadingRequest.cs', 'File.ReadAllBytes(m_FilePath)')]),
    ('M01', 'VisualAdjustments2 持久化角色设置累积', 'local_data_and_static_risk', [
        ('VisualAdjustments2/VisualAdjustments2.Infrastructure/GlobalCharacterSettings.cs', 'PerCharacter.Add(uniqueId, value);'),
        ('VisualAdjustments2/VisualAdjustments2.Infrastructure/GetVisualSettingsProvider_Patch.cs', 'owner.Unit.GetSettings();'),
        ('VisualAdjustments2/VisualAdjustments2.Infrastructure/SaveHooker.cs', 'jsonSerializer.Serialize(stringWriter, GlobalCharacterSettings.Instance)')]),
    ('M02', 'VisualAdjustments2 FX 字典强引用 ItemEntity', 'strong_static_risk', [
        ('VisualAdjustments2/VisualAdjustments2.Infrastructure/CharacterSettings.cs', 'Dictionary<ItemEntity, List<IFxHandle>> CurrentFXs'),
        ('VisualAdjustments2/VisualAdjustments2.Infrastructure/UnitViewHandSlotData_UpdateWeaponEnchantmentFX_Patch.cs', 'settings.CurrentFXs[__instance.Slot.MaybeItem].Clear();')]),
    ('M03', 'ToyBox 增强法术书的事件和 LateUpdate 释放', 'strong_static_risk', [
        ('0ToyBox0/ToyBox/EnhancedSpellbookController.cs', 'EventBus.Subscribe(this);'),
        ('0ToyBox0/ToyBox/EnhancedSpellbookController.cs', 'public void OnDestroy()'),
        ('0ToyBox0/ToyBox/EnhancedSpellbookController.cs', '.ViewModel.RefreshCommand.ObserveLastValueOnLateUpdate()')]),
    ('M04', '战术战斗先攻视图的未保存 LateUpdate 订阅', 'strong_static_risk', [
        ('Assembly-CSharp/Kingmaker.UI.MVVM._PCView.TacticalCombat.InitiativeTracker/TacticalCombatInitiativeTrackerBaseView.cs', 'ObservableExtensions.Subscribe(base.ViewModel.UnitsUpdated.ObserveLastValueOnLateUpdate()'),
        ('Assembly-CSharp/Kingmaker.UI.MVVM._PCView.TacticalCombat.InitiativeTracker/TacticalCombatInitiativeTrackerPCView.cs', 'ObservableExtensions.Subscribe(base.ViewModel.UnitsUpdated.ObserveLastValueOnLateUpdate()')]),
    ('M05', '自定义头像请求移除不等同于 Destroy 贴图', 'conditional_static_risk', [
        ('Assembly-CSharp/Kingmaker.ResourceManagement/ResourceStorage.cs', 'public void Unload(string id)'),
        ('CustomNpcPortraits/CustomNpcPortraits/GetPortrait_Patch.cs', 'Storage.Unload(Path.Combine(unitPortraitPath, "Medium.png"))')]),
    ('M06', 'VisualAdjustments2 颜色与眼睛贴图静态缓存', 'bounded_by_usage_needs_measurement', [
        ('VisualAdjustments2/VisualAdjustments2.Infrastructure/EeInfraStructure.cs', 'Dictionary<Color, Texture2D> ColorToTex'),
        ('VisualAdjustments2/VisualAdjustments2.Infrastructure/CustomEyePictures.cs', 'Dictionary<string, Sprite> loadedSprites')]),
    ('C01', 'WrathPatches 类型绑定启动日志', 'historical_cause_identified', [
        ('WrathPatches/WrathPatches/UmmModsToGuidClassBinder.cs', 'I told kuru this would happen')]),
    ('C02', 'Isekai 进展树防循环属于蓝图构建线索', 'historical_cause_identified', [
        ('IsekaiMod/IsekaiMod.Utilities/PatchTools.cs', 'Attempt to patch Progression Tree stopped at Level 10')]),
    ('C03', 'DarkCodex Prebuild 调试 I/O 不能假定已启用', 'deprioritized_unproven_activation', [
        ('DarkCodex/DarkCodex/Patch_Prebuilds.cs', 'Severity.WIP | Severity.Hidden'),
        ('DarkCodex/DarkCodex/Patch_Prebuilds.cs', 'new StreamWriter(Path.Combine(Main.ModPath, "Log-LevelUp.txt")')]),
    ('F01', '排除子 VM Dispose 后必然被父列表继续持有', 'false_positive_excluded', [
        ('Owlcat.Runtime.UI/Owlcat.Runtime.UI.MVVM/BaseDisposable.cs', 'Owner.RemoveDisposable(this);')]),
    ('F02', '单位 Dispose 已清单位订阅并注销实体', 'existing_cleanup_confirmed', [
        ('Assembly-CSharp/Kingmaker.EntitySystem.Entities/UnitEntityData.cs', 'EventBus.ClearUnitSubscriptions(this);'),
        ('Assembly-CSharp/Kingmaker.EntitySystem/EntityDataBase.cs', 'EntityService.Instance?.Unregister(this);')]),
    ('F03', '专长列表每个阶段有首次创建保护', 'existing_optimization_confirmed', [
        ('Assembly-CSharp/Kingmaker.UI.MVVM._VM.CharGen.Phases.FeatureSelector/CharGenFeatureSelectorPhaseVM.cs', 'if (!m_FeatureListIsCreated)')]),
    ('F04', 'PC 上 UnitAtlas LOD 服务和图集预算分支未启用', 'platform_path_excluded', [
        ('Assembly-CSharp/Kingmaker.Controllers.Units/UnitAtlasService.cs', 'if (PlatformManager.IsConsole)'),
        ('Assembly-CSharp/Kingmaker.Visual.CharacterSystem/CharacterAtlas.cs', 'if (PlatformManager.IsConsole)')]),
]


def main():
    cases = []
    markdown = ['# 深入分析源码证据索引', '', '此索引定位证据，不代表所有条目都是已发生的故障。完整解释见 deep-analysis.md。', '']
    for ident, title, status, refs in CASES:
        evidence = []
        markdown += [f'## {ident}：{title}', '', f'证据类别：`{status}`', '']
        for relative, marker in refs:
            path = SOURCE / relative
            raw = path.read_bytes()
            lines = raw.decode('utf-8-sig', errors='replace').splitlines()
            found = [(n, line) for n, line in enumerate(lines, 1) if marker in line]
            if not found:
                raise ValueError(f'Marker not found: {relative}: {marker}')
            number, line = found[0]
            evidence.append({'path': relative, 'line': number, 'marker': marker, 'sha256': hashlib.sha256(raw).hexdigest()})
            markdown.append(f'- [{relative}:{number}]({path.as_posix()}:{number})')
        markdown.append('')
        cases.append({'id': ident, 'title': title, 'status': status, 'evidence': evidence})
    (ROOT / 'analysis/deep-audit-findings.json').write_text(json.dumps(cases, ensure_ascii=False, indent=2), encoding='utf-8')
    (ROOT / 'docs/evidence-index.md').write_text('\n'.join(markdown), encoding='utf-8')
    patterns = ['AsyncGPUReadback failed', 'Failed to compress atlas to DXT', 'Light probes baking process takes too long',
                'Cannot Cleanup cache in preloading mode', 'Subscriber removed while RaiseEvent', "Can't subscribe on events twice",
                'OutOfMemoryException', 'Could not allocate memory', 'Templist (of', 'I told kuru this would happen',
                'Attempt to patch Progression Tree stopped at Level 10']
    logs = []
    for name in ('GameLogFull.txt', 'GameLogFullPrev.txt', 'Player.log', 'Player-prev.log'):
        path = LOGS / name
        counts, samples = Counter(), {}
        with path.open(encoding='utf-8-sig', errors='replace') as stream:
            for number, line in enumerate(stream, 1):
                for marker in patterns:
                    if marker in line:
                        counts[marker] += 1
                        if len(samples.setdefault(marker, [])) < 3:
                            samples[marker].append({'line': number, 'text': line.strip()[:400]})
        logs.append({'file': name, 'counts': dict(counts), 'samples': samples})
    enabled = ElementTree.parse(GAME / 'Wrath_Data/Managed/UnityModManager/Params.xml')
    toybox = ElementTree.parse(GAME / 'Mods/0ToyBox0/Settings.xml')
    config = {'umm_saved_flags': {x.attrib['Id']: x.attrib.get('Enabled') for x in enabled.findall('.//Mod')},
              'toybox_saved_flags': {key: toybox.findtext(key) for key in ('toggleEnhancedSpellbook', 'toggleMulticlass', 'toggleMulticlassInGameUI')},
              'notice': 'Saved configuration is not proof of successful runtime patch application.'}
    (ROOT / 'analysis/focused-log-markers.json').write_text(json.dumps(logs, ensure_ascii=False, indent=2), encoding='utf-8')
    (ROOT / 'analysis/mod-relevant-config.json').write_text(json.dumps(config, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'cases': len(cases), 'evidence_locations': sum(len(c['evidence']) for c in cases),
                      'toybox_flags': config['toybox_saved_flags'], 'log_markers': [{l['file']: l['counts']} for l in logs]}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
