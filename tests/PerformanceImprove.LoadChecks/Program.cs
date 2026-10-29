using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using PerformanceImprove;
using PerformanceImprove.LoadCleanup;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        checks++;
        Console.WriteLine("PASS " + name);
    }

    private static int Main()
    {
        try
        {
            // 1. Verify the exact retail layout the patch depends on.
            string managed = Environment.GetEnvironmentVariable("WRATH_MANAGED") ??
                @"D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure\Wrath_Data\Managed";
            string gamePath = Path.Combine(managed, "Assembly-CSharp.dll");
            Check(File.Exists(gamePath), "Assembly-CSharp.dll is available for metadata checks");

            using (var module = ModuleDefMD.Load(gamePath))
            {
                TypeDef game = module.Find("Kingmaker.Game", false);
                Check(game != null, "Kingmaker.Game type is present");

                MethodDef loadArea = game.Methods.SingleOrDefault(m => m.Name.String == "LoadArea" && m.ParamDefs.Count == 6);
                Check(loadArea != null && loadArea.IsPrivate, "six-parameter private Game.LoadArea is present");
                string[] names = loadArea.ParamDefs.Select(p => p.Name.String).ToArray();
                Check(names.SequenceEqual(new[] { "area", "enterPoint", "autoSaveMode", "forceUnload", "saveInfo", "callback" }),
                    "LoadArea parameter order is area, enterPoint, autoSaveMode, forceUnload, saveInfo, callback");
                Check(loadArea.MethodSig.Params[4].TypeName == "SaveInfo", "LoadArea parameter 4 is SaveInfo");
                Check(loadArea.MethodSig.Params[5].TypeName == "Action", "LoadArea parameter 5 is Action");

                MethodDef unload = game.Methods.SingleOrDefault(m => m.Name.String == "UnloadUnusedAssetsCoroutine" && m.ParamDefs.Count == 0);
                Check(unload != null && unload.IsPublic, "Game.UnloadUnusedAssetsCoroutine is public and parameterless");
                Check(unload.ReturnType.TypeName == "IEnumerator`1", "UnloadUnusedAssetsCoroutine returns IEnumerator<T>");

                int unloadCalls = loadArea.Body.Instructions.Count(i => i.OpCode.Code == Code.Call &&
                    i.Operand is MethodDef target && target.Name.String == "UnloadUnusedAssetsCoroutine");
                Check(unloadCalls >= 2, "LoadArea still registers at least two UnloadUnusedAssets passes (observed " + unloadCalls + ")");
                Console.WriteLine("INFO LoadArea UnloadUnusedAssets call sites: " + unloadCalls);
            }

            // 2. State machine: only the second call of a normal (non-save) LoadArea is skipped.
            State.End();
            Check(!State.ShouldSkip(), "state outside LoadArea never skips");
            State.Begin(true);
            Check(!State.ShouldSkip(), "normal transition: first cleanup is kept");
            Check(State.ShouldSkip(), "normal transition: second cleanup is skipped");
            Check(!State.ShouldSkip(), "normal transition: a third cleanup (UnloadAllOnEnter) is kept");
            State.End();
            Check(!State.ShouldSkip(), "state reset does not leak a skip");

            State.Begin(false);
            Check(!State.ShouldSkip() && !State.ShouldSkip() && !State.ShouldSkip(), "save transition keeps every cleanup");
            State.End();

            State.Begin(true);
            State.End();
            State.Begin(false);
            Check(!State.ShouldSkip(), "a new save transition clears the previous normal counter");
            State.End();

            // 2b. Save-load first pass: skipped only when configured and the machine has enough memory.
            State.Begin(false, true);
            Check(State.ShouldSkip(), "save transition with save skip: first cleanup is skipped");
            Check(!State.ShouldSkip() && !State.ShouldSkip(), "save transition with save skip: later cleanups are kept");
            Check(State.SkippedIndex == 0 || State.SkippedIndex == 1, "save transition records which pass was skipped");
            State.End();

            State.Begin(false, false);
            Check(!State.ShouldSkip() && !State.ShouldSkip(), "save transition without save skip keeps every cleanup");
            State.End();

            State.Begin(true, true);
            Check(!State.ShouldSkip() && State.ShouldSkip(), "normal transition ignores the save-load switch and still skips the second pass");
            State.End();

            Check(PerformanceImprove.LoadCleanup.Main.ShouldSkipSaveLoadCleanup(true, 32000), "32 GB machine skips the save-load first pass");
            Check(PerformanceImprove.LoadCleanup.Main.ShouldSkipSaveLoadCleanup(true, 12000), "12 GB threshold is inclusive");
            Check(!PerformanceImprove.LoadCleanup.Main.ShouldSkipSaveLoadCleanup(true, 11999), "below 12 GB the save-load first pass is kept");
            Check(!PerformanceImprove.LoadCleanup.Main.ShouldSkipSaveLoadCleanup(false, 32000), "the switch alone controls the save-load skip");
            Check(PerformanceImprove.LoadCleanup.Main.SaveLoadSkipMinimumMemoryMb == 12000, "memory threshold is a named constant");

            // 3. Configuration round-trips the new independent switch.
            var configuration = new Configuration { SkipDuplicateLoadCleanup = false, LifecycleCleanup = false };
            var serializer = new XmlSerializer(typeof(Configuration));
            string xml;
            using (var writer = new StringWriter()) { serializer.Serialize(writer, configuration); xml = writer.ToString(); }
            Configuration restored;
            using (var reader = new StringReader(xml)) restored = (Configuration)serializer.Deserialize(reader);
            Check(!restored.SkipDuplicateLoadCleanup && !restored.LifecycleCleanup, "load-cleanup switch round-trips independently");
            Check(new Configuration().SkipDuplicateLoadCleanup, "load-cleanup switch defaults to true");
            Check(!new Configuration().SkipFirstCleanupOnSaveLoad, "save-load skip is experimental and defaults to off");
            var saveConfig = new Configuration { SkipFirstCleanupOnSaveLoad = false };
            using (var writer = new StringWriter()) { serializer.Serialize(writer, saveConfig); xml = writer.ToString(); }
            Configuration saveRestored;
            using (var reader = new StringReader(xml)) saveRestored = (Configuration)serializer.Deserialize(reader);
            Check(!saveRestored.SkipFirstCleanupOnSaveLoad && saveRestored.SkipDuplicateLoadCleanup, "save-load switch round-trips independently");

            // 4. FeatureGroup owns and tears down the fifth slot without disturbing the existing sequence.
            var sequence = new List<string>();
            FeatureSlot Slot(string name) => new FeatureSlot(
                () => { sequence.Add(name + ":init"); return true; },
                value => { sequence.Add(name + ":" + value); return true; },
                () => { sequence.Add(name + ":unload"); return true; },
                _ => { });
            var group = new FeatureGroup(Slot("life"), Slot("npc"), null, null, Slot("load"));
            Check(group.Enable(true, true, false, false, true) && group.LoadCleanup.Active, "fifth feature initializes under the existing host");
            Check(sequence.Contains("load:init"), "load-cleanup slot initialization runs once");
            Check(group.Disable() && !group.LoadCleanup.Active, "master disable turns the load-cleanup slot off");
            Check(sequence.Contains("load:False"), "master disable calls the load-cleanup toggle");
            Check(group.Enable(true, true, false, false, true) && group.LoadCleanup.Active, "load-cleanup slot re-enables with the master switch");
            Check(group.Unload() && sequence.Contains("load:unload"), "unload releases the load-cleanup slot");

            var onlyLoad = new FeatureGroup(Slot("life2"), Slot("npc2"), null, null, Slot("load2"));
            Check(onlyLoad.Enable(false, false, false, false, true) && onlyLoad.Enabled && onlyLoad.LoadCleanup.Active && !onlyLoad.Lifecycle.Active,
                "load-cleanup can be the only enabled performance feature");
            var failedLoad = new FeatureSlot(() => false, _ => true, () => true, _ => { });
            var tolerant = new FeatureGroup(Slot("life3"), Slot("npc3"), null, null, failedLoad);
            Check(tolerant.Enable(true, true, false, false, true) && tolerant.Lifecycle.Active && !tolerant.LoadCleanup.Active && tolerant.LoadCleanup.Error != null,
                "a rejected load-cleanup patch leaves the other features usable");
            tolerant.Unload();

            // 5. The feature lives in the single merged assembly and uses its own Harmony owner.
            var assembly = typeof(PerformanceImprove.Main).Assembly;
            Check(typeof(State).Assembly == assembly && typeof(PerformanceImprove.LoadCleanup.Main).Assembly == assembly,
                "load-cleanup implementation is compiled into PerformanceImprove.dll");
            Check(!assembly.GetReferencedAssemblies().Any(a => a.Name == "WrathLifecycleFix" || a.Name == "WrathDeferredNpcViews"),
                "load-cleanup does not add a standalone feature dependency");

            // 6. Harmony injection depends on the exact synthetic parameter names.
            var patchType = typeof(PerformanceImprove.LoadCleanup.Main);
            var loadPrefix = patchType.GetMethod("LoadAreaPrefix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Check(loadPrefix != null && loadPrefix.GetParameters().Length == 1 &&
                loadPrefix.GetParameters()[0].Name == "__args" && loadPrefix.GetParameters()[0].ParameterType == typeof(object[]),
                "LoadArea prefix exposes Harmony object[] __args");
            var unloadPrefix = patchType.GetMethod("UnloadPrefix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Check(unloadPrefix != null && unloadPrefix.GetParameters().Length == 1 &&
                unloadPrefix.GetParameters()[0].Name == "__result" && unloadPrefix.GetParameters()[0].ParameterType.IsByRef,
                "UnloadUnusedAssets prefix exposes ref __result");

            // 7. Texture-wait burst: exact coroutine layout, pure gating, and configuration.
            using (var module = ModuleDefMD.Load(gamePath))
            {
                TypeDef game = module.Find("Kingmaker.Game", false);
                MethodDef await = game.Methods.SingleOrDefault(m => m.Name.String == "AwaitTextureCompression" && m.ParamDefs.Count == 0);
                Check(await != null && await.IsPrivate, "Game.AwaitTextureCompression is a private parameterless method");
                Check(await.ReturnType.TypeName == "IEnumerator", "AwaitTextureCompression returns the non-generic IEnumerator");
                TypeDef atlas = module.Find("Kingmaker.Visual.CharacterSystem.CharacterAtlasService", false);
                Check(atlas != null, "CharacterAtlasService type is present");
                MethodDef update = atlas.Methods.SingleOrDefault(m => m.Name.String == "Update" && m.ParamDefs.Count == 0);
                Check(update != null && update.IsPublic, "CharacterAtlasService.Update is public and parameterless");
            }
            Check(BurstPolicy.ClampBurst(0) == 1 && BurstPolicy.ClampBurst(1) == 1, "burst lower bound is one update per frame");
            Check(BurstPolicy.ClampBurst(9) == 8 && BurstPolicy.ClampBurst(4) == 4, "burst upper bound is eight updates per frame");
            Check(BurstPolicy.ShouldPump(true, true, true, false), "pump runs only inside the wait window");
            Check(!BurstPolicy.ShouldPump(false, true, true, false), "disabled burst never pumps");
            Check(!BurstPolicy.ShouldPump(true, false, true, false), "feature toggle off stops pumping");
            Check(!BurstPolicy.ShouldPump(true, true, false, false), "pump does not run outside AwaitTextureCompression");
            Check(!BurstPolicy.ShouldPump(true, true, true, true), "re-entrant Update is not pumped again");
            Check(new Configuration().FastTextureWait, "texture burst defaults to enabled");
            Check(new Configuration().TextureWaitBurst == 4, "texture burst defaults to four updates per frame");
            var burstConfig = new Configuration { FastTextureWait = false, TextureWaitBurst = 6 };
            using (var writer = new StringWriter()) { serializer.Serialize(writer, burstConfig); xml = writer.ToString(); }
            Configuration burstRestored;
            using (var reader = new StringReader(xml)) burstRestored = (Configuration)serializer.Deserialize(reader);
            Check(!burstRestored.FastTextureWait && burstRestored.TextureWaitBurst == 6, "texture burst settings round-trip independently");

            Console.WriteLine(checks + " checks passed");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}

