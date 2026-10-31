using System;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Blueprints.Area;
using Kingmaker.EntitySystem.Persistence;
using UnityEngine;
using UnityModManagerNet;
using Lifecycle = WrathLifecycleFix.Main;
using NpcViews = WrathDeferredNpcViews.Main;
using MerchantCleanup = PerformanceImprove.Merchant.Main;
using LoadFeature = PerformanceImprove.LoadCleanup.Main;

namespace PerformanceImprove
{
    public sealed class Configuration : UnityModManager.ModSettings
    {
        public bool LifecycleCleanup = true;
        public bool DeferredNpcViews = true;
        public bool MerchantSaleCleanup = true;
        public bool Diagnostics = true;
        public bool SkipDuplicateLoadCleanup = true;
        public bool SkipFirstCleanupOnSaveLoad = false;
        // Skip the TabletopTweaks -117 JSON migration when the file has no TabletopTweaks type names.
        public bool SkipNoOpSaveUpgrades = true;
        public int DiagnosticsSampleSeconds = 15;
        public bool FastTextureWait = true;
        public int TextureWaitBurst = 4;
        public int DiagnosticsMaxMegabytes = 32;
        // Experiment: skip re-adding spells the preview unit already knows. Off until A/B proves it.
        public bool SpellbookCopyFast = false;
        // Test hook: F11 reloads the current area as a normal transition (saveInfo null) for A/B.
        public bool DebugReloadAreaKey = false;
        public WrathDeferredNpcViews.Settings Npc = new WrathDeferredNpcViews.Settings();
        public override void Save(UnityModManager.ModEntry entry) => Save(this, entry);
    }

    public static class Main
    {
        internal const string LifecycleOwner = "PerformanceImprove.Lifecycle";
        internal const string NpcOwner = "PerformanceImprove.NpcViews";
        private static UnityModManager.ModEntry entry;
        private static Configuration configuration;
        private static FeatureGroup features;
        private static Diagnostics.ProbeHost diagnostics;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            entry = modEntry;
            try
            {
                foreach (string legacy in new[] { "WrathLifecycleFix", "WrathDeferredNpcViews" })
                    if (UnityModManager.FindMod(legacy)?.Enabled == true || Harmony.HasAnyPatches(legacy))
                        throw new InvalidOperationException("Legacy Mod conflict: " + legacy + ". Remove the old installation and restart the game.");
                configuration = UnityModManager.ModSettings.Load<Configuration>(entry);
                configuration.Npc = configuration.Npc ?? new WrathDeferredNpcViews.Settings();
                LoadFeature.Configure(configuration.SkipFirstCleanupOnSaveLoad);
                LoadFeature.ConfigureTexture(configuration.FastTextureWait, configuration.TextureWaitBurst);
                LoadFeature.ConfigureSaveUpgrade(configuration.SkipNoOpSaveUpgrades);
                diagnostics = new Diagnostics.ProbeHost(entry, configuration.DiagnosticsSampleSeconds, configuration.DiagnosticsMaxMegabytes)
                { SkipKnownReadd = configuration.SpellbookCopyFast };
                features = new FeatureGroup(
                    new FeatureSlot(() => Lifecycle.Initialize(entry, LifecycleOwner, "lifecycle-"), value => Lifecycle.Toggle(entry, value),
                        () => Lifecycle.Unload(entry), delta => Lifecycle.Update(entry, delta)),
                    new FeatureSlot(() => NpcViews.Initialize(entry, NpcOwner, configuration.Npc, "npc-"), value => NpcViews.Toggle(entry, value),
                        () => NpcViews.Unload(entry), delta => NpcViews.Update(entry, delta)),
                    new FeatureSlot(() => MerchantCleanup.Initialize(entry), MerchantCleanup.Toggle, MerchantCleanup.Unload, _ => { }),
                    new FeatureSlot(() => diagnostics.Install(), value => diagnostics.SetEnabled(value),
                        () => { diagnostics.Unload(); return true; }, delta => diagnostics.Tick(delta)),
                    new FeatureSlot(() => LoadFeature.Initialize(entry), LoadFeature.Toggle, LoadFeature.Unload, _ => { }));
                if (!features.Enable(configuration.LifecycleCleanup, configuration.DeferredNpcViews, configuration.MerchantSaleCleanup, configuration.Diagnostics, configuration.SkipDuplicateLoadCleanup))
                    throw new InvalidOperationException("No selected performance feature could initialize");
                entry.OnToggle = Toggle;
                entry.OnUnload = _ => features.Unload();
                entry.OnUpdate = (_, delta) =>
                {
                    features.Tick(delta);
                    if (configuration.DebugReloadAreaKey && Input.GetKeyDown(KeyCode.F11))
                        ReloadCurrentArea();
                };
                entry.OnGUI = GUI;
                entry.OnSaveGUI = e => configuration.Save(e);
                ReportErrors();
                entry.Logger.Log("PerformanceImprove 0.5.3: lifecycle cleanup, deferred NPC views, stackable player sale cleanup, area-load cleanup skip, save-upgrade fast path, session diagnostics.");
                return true;
            }
            catch (Exception ex)
            {
                features?.Unload();
                entry.Logger.LogException(ex); return false;
            }
        }
        private static void ReloadCurrentArea()
        {
            try
            {
                Game game = Game.Instance;
                BlueprintArea area = game?.CurrentlyLoadedArea;
                if (area == null) { entry?.Logger.Log("Debug reload: no currently loaded area."); return; }
                System.Reflection.MethodInfo load = AccessTools.Method(typeof(Game), "LoadArea",
                    new[] { typeof(BlueprintArea), typeof(BlueprintAreaEnterPoint), typeof(AutoSaveMode), typeof(bool), typeof(SaveInfo), typeof(Action) });
                if (load == null) { entry?.Logger.Log("Debug reload: LoadArea method not found."); return; }
                entry?.Logger.Log("Debug reload: reloading " + area.AssetGuidThreadSafe + " as a normal transition (no save).");
                load.Invoke(game, new object[] { area, null, AutoSaveMode.None, false, null, null });
            }
            catch (Exception ex)
            {
                entry?.Logger.LogException(ex);
            }
        }

        private static bool Toggle(UnityModManager.ModEntry _, bool value)
        {
            bool result = value ? features.Enable(configuration.LifecycleCleanup, configuration.DeferredNpcViews, configuration.MerchantSaleCleanup, configuration.Diagnostics, configuration.SkipDuplicateLoadCleanup) : features.Disable();
            ReportErrors(); return result;
        }
        private static void ReportErrors()
        {
            if (features?.Lifecycle.Error != null) entry.Logger.Log("Lifecycle: " + features.Lifecycle.Error);
            if (features?.Npc.Error != null) entry.Logger.Log("NPC views: " + features.Npc.Error);
            if (features?.Merchant.Error != null) entry.Logger.Log("Merchant cleanup: " + features.Merchant.Error);
            if (features?.LoadCleanup.Error != null) entry.Logger.Log("Load cleanup: " + features.LoadCleanup.Error);
            if (features?.Diagnostics.Error != null) entry.Logger.Log("Diagnostics: " + features.Diagnostics.Error);
            if (diagnostics != null && diagnostics.Missing.Count > 0)
                entry.Logger.Log("Diagnostics: " + diagnostics.Missing.Count + " probe(s) unavailable on this game build.");
        }
        private static void GUI(UnityModManager.ModEntry _)
        {
            GUILayout.Label("PerformanceImprove 0.5.3 — experimental; restart for a clean comparison.");
            bool lifecycle = GUILayout.Toggle(configuration.LifecycleCleanup, "Level-up subscription cleanup");
            if (lifecycle != configuration.LifecycleCleanup && (!features.Enabled || features.Lifecycle.Set(lifecycle))) configuration.LifecycleCleanup = lifecycle;
            bool npc = GUILayout.Toggle(configuration.DeferredNpcViews, "Drezen NPC models on demand");
            if (npc != configuration.DeferredNpcViews && (!features.Enabled || features.Npc.Set(npc))) configuration.DeferredNpcViews = npc;
            bool skipLoad = GUILayout.Toggle(configuration.SkipDuplicateLoadCleanup, "跳过区域加载中重复的 UnloadUnusedAssets");
            if (skipLoad != configuration.SkipDuplicateLoadCleanup && (!features.Enabled || features.LoadCleanup.Set(skipLoad))) configuration.SkipDuplicateLoadCleanup = skipLoad;
            bool fastTexture = GUILayout.Toggle(configuration.FastTextureWait, "加载时加速角色贴图图集队列（不改变结果，只是每帧多推进几次）");
            if (fastTexture != configuration.FastTextureWait) { configuration.FastTextureWait = fastTexture; LoadFeature.ConfigureTexture(fastTexture, configuration.TextureWaitBurst); }
            bool skipSave = GUILayout.Toggle(configuration.SkipFirstCleanupOnSaveLoad, "实验：读档时跳过第一次清理（需 ≥12 GB 内存，默认关）");
            if (skipSave != configuration.SkipFirstCleanupOnSaveLoad) { configuration.SkipFirstCleanupOnSaveLoad = skipSave; LoadFeature.Configure(skipSave); }
            bool skipUpgrade = GUILayout.Toggle(configuration.SkipNoOpSaveUpgrades, "读档时跳过无效果的 TabletopTweaks 存档升级（内容不含该类型名时）");
            if (skipUpgrade != configuration.SkipNoOpSaveUpgrades) { configuration.SkipNoOpSaveUpgrades = skipUpgrade; LoadFeature.ConfigureSaveUpgrade(skipUpgrade); }
            bool debugReload = GUILayout.Toggle(configuration.DebugReloadAreaKey, "测试：F11 把当前区域当普通切换重新加载（不存档，用于 A/B）");
            if (debugReload != configuration.DebugReloadAreaKey) configuration.DebugReloadAreaKey = debugReload;
            GUILayout.Label("普通切换跳第二次清理；贴图加速只在加载等待期间生效。");
            GUILayout.Label($"已跳过清理：普通 {LoadFeature.Skipped} 次 / 读档 {LoadFeature.SkippedSaveLoad} 次；贴图队列额外推进 {LoadFeature.PumpedTextures} 次；跳过无效存档升级 {LoadFeature.SkippedUpgradeFiles} 个文件 / {LoadFeature.SkippedUpgradeMegabytes} MB");
            bool merchant = GUILayout.Toggle(configuration.MerchantSaleCleanup, "自动清理已成交的可堆叠卖出物品（保留独特装备和任务道具）");
            if (merchant != configuration.MerchantSaleCleanup && (!features.Enabled || features.Merchant.Set(merchant))) configuration.MerchantSaleCleanup = merchant;
            GUILayout.Label("清理后无法回购；商人原有库存、非堆叠物品和无法确认的物品保持原样。");
            GUILayout.Label($"卖出清理：{MerchantCleanup.RemovedStacks} 堆 / {MerchantCleanup.RemovedUnits} 件；暂停={MerchantCleanup.Halted}");
            bool diagnosticsOn = GUILayout.Toggle(configuration.Diagnostics, "记录诊断日志（可发给作者分析）");
            if (diagnosticsOn != configuration.Diagnostics && (!features.Enabled || features.Diagnostics.Set(diagnosticsOn))) configuration.Diagnostics = diagnosticsOn;
            bool fast = GUILayout.Toggle(configuration.SpellbookCopyFast, "实验：复制法术书时跳过已学法术（默认关，用于 A/B）");
            if (fast != configuration.SpellbookCopyFast) { configuration.SpellbookCopyFast = fast; if (diagnostics != null) diagnostics.SkipKnownReadd = fast; }
            GUILayout.Label($"Active: lifecycle={features.Lifecycle.Active}, NPC={features.Npc.Active}, load={features.LoadCleanup.Active}, saveUpgrade={PerformanceImprove.LoadCleanup.SaveUpgrade.Enabled && features.LoadCleanup.Active}, diagnostics={features.Diagnostics.Active}");
            if (diagnostics != null && diagnostics.LogPath != null)
                GUILayout.Label("日志：" + diagnostics.LogPath + (diagnostics.LimitReached ? "（已达上限）" : ""));
            if (features.Lifecycle.Error != null) GUILayout.Label("Lifecycle: " + features.Lifecycle.Error);
            if (features.Npc.Error != null) GUILayout.Label("NPC: " + features.Npc.Error);
            if (features.LoadCleanup.Error != null) GUILayout.Label("Load cleanup: " + features.LoadCleanup.Error);
            if (features.Merchant.Error != null) GUILayout.Label("Merchant: " + features.Merchant.Error);
            if (features.Diagnostics.Error != null) GUILayout.Label("Diagnostics: " + features.Diagnostics.Error);
            if (features.Npc.Initialized) NpcViews.GUI(entry);
        }
    }
}
