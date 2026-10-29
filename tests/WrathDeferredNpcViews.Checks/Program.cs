using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using WrathDeferredNpcViews;
using WrathLifecycleFix;

internal static class Program
{
    private const string Managed = @"D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure\Wrath_Data\Managed";
    private static int count;
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); count++; Console.WriteLine("PASS " + message); }
    private static int Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string path = System.IO.Path.Combine(Managed, new AssemblyName(e.Name).Name + ".dll");
            return System.IO.File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try { RunPolicy(); RunWeakSet(); RunRewrite(); RunWarmup(); RunActualIl(); Console.WriteLine($"{count} checks passed"); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void RunPolicy()
    {
        Check(Policy.Blueprints.Count == 4 && Policy.Blueprints.All(id => id.Length == 32), "four GUIDs retained for optional narrow mode");
        Check(Policy.Blueprints.All(id => Policy.Allowed(Policy.AreaId, id)), "allowlisted capital commoners accepted");
        Check(!Policy.Allowed("other", Policy.Blueprints.First()), "other areas rejected");
        Check(!Policy.Allowed(Policy.AreaId, "story-npc"), "malformed blueprint identifier rejected");
        Check(!Policy.Allowed(Policy.AreaId, null), "unknown blueprint rejected");
        Check(Policy.Allowed(Policy.AreaId, "11111111111111111111111111111111"), "broad mode includes unlisted NPC blueprints");
        Check(!Policy.Allowed(Policy.AreaId, "11111111111111111111111111111111", false), "narrow mode still restricts its allowlist");
        Check(!Policy.Allowed(Policy.StoryReturnAreaId, Policy.Blueprints.First()), "separate story return map excluded");
        Check(Policy.KnownChapter(3) && Policy.KnownChapter(4) && Policy.KnownChapter(5) && Policy.KnownChapter(6) && !Policy.KnownChapter(2),
            "chapter four not blanket-disabled; unknown/siege chapters excluded");
        Check(!Policy.NeedsFull(true, true, true, false, 46 * 46, 45), "distant safe commoner stays deferred");
        Check(Policy.NeedsFull(true, true, true, false, 45 * 45, 45), "radius boundary prewarms");
        Check(Policy.NeedsFull(true, true, true, true, 100000, 45), "camera overrides party distance");
        Check(Policy.NeedsFull(true, false, true, false, 100000, 45), "missing party fails open to full view");
        Check(Policy.NeedsFull(true, true, false, false, 100000, 45), "missing camera fails open to full view");
        Check(Policy.NeedsFull(false, true, true, false, 100000, 45), "changed safety restores full view");
        Check(!Policy.Allowed(Policy.AreaId, new string('0', 32)), "empty blueprint GUID rejected in broad mode");
        Check(Policy.NeedsFull(true, true, true, false, double.NaN, 45), "invalid NaN position cannot leave a model deferred");
        Check(Policy.NeedsFull(true, true, true, false, double.PositiveInfinity, 45), "invalid infinite position restores full view");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AddUnrooted(WeakWorkSet<object> set)
    { var value = new object(); set.Add(value); return new WeakReference(value); }
    private static void RunWeakSet()
    {
        var set = new WeakWorkSet<object>(2);
        var first = new object(); var second = new object(); var third = new object();
        Check(set.Add(first) && !set.Add(first), "duplicate capture rejected");
        Check(set.Add(second) && !set.Add(third), "bounded queue rejects capacity overflow");
        set.Complete(first);
        Check(!set.Contains(first) && set.Seen(first) && !set.Add(first), "completed owner never deferred twice");
        Check(set.Add(third) && set.Snapshot().Count == 2, "completed slot reusable");
        var weakSet = new WeakWorkSet<object>(2);
        WeakReference reference = AddUnrooted(weakSet);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(!reference.IsAlive && weakSet.Snapshot().Count == 0, "pending work and completion records do not root units");
        GC.KeepAlive(first); GC.KeepAlive(second); GC.KeepAlive(third);
    }
    private sealed class Surrogate
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal string View()
        { string handle = Request("normal", false); if (handle == null) handle = Request("legacy", false); return handle; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal string Spawn(string blueprint, int position, int rotation, object state, string uniqueId)
        { string handle = Request("spawn", false); if (handle == null) handle = Request("legacy", false); return handle; }
    }
    private static bool lastHold;
    private static object expectedState;
    private static string Request(string asset, bool hold) { lastHold = hold; return asset; }
    private static string SelectView(string original, Surrogate instance) => instance == null ? "invalid" : "dummy:" + original;
    private static string SelectSpawn(string original, string bp, int position, object state) => bp == "bp" && position == 73 && state == expectedState ? "dummy:" + original : "invalid";
    private sealed class Sequence : IEnumerator, IDisposable
    {
        internal int Disposals;
        internal Exception Failure;
        private int index = -1;
        internal readonly object Token = new object();
        public object Current => index == 0 ? Token : null;
        public bool MoveNext() { if (Failure != null) throw Failure; return ++index < 2; }
        public void Reset() => throw new NotSupportedException();
        public void Dispose() => Disposals++;
    }
    private static void RunWarmup()
    {
        var source = new Sequence(); int calls = 0, completed = 0;
        var wrapped = WarmupSequence.Append(source, () => true, () => ++calls <= 2, () => completed++);
        Check(wrapped.MoveNext() && wrapped.Current == source.Token && calls == 0, "original loading yield forwarded before warmup");
        Check(wrapped.MoveNext() && wrapped.Current == null && calls == 0, "original null yield unchanged");
        int extraFrames = 0; while (wrapped.MoveNext()) extraFrames++;
        Check(extraFrames == 2 && calls == 3 && completed == 1 && source.Disposals == 1, "one warm operation per added frame and original disposed");
        source = new Sequence(); calls = 0;
        wrapped = WarmupSequence.Append(source, () => false, () => { calls++; return true; }, () => { });
        while (wrapped.MoveNext()) { }
        Check(calls == 0 && source.Disposals == 1, "changed area generation cancels warmup");
        source = new Sequence(); wrapped = WarmupSequence.Append(source, () => true, () => true, () => { });
        wrapped.MoveNext(); ((IDisposable)wrapped).Dispose();
        Check(source.Disposals == 1, "cancelled loading iterator disposes original");
        var originalError = new InvalidOperationException("original loading error");
        source = new Sequence { Failure = originalError }; calls = 0;
        wrapped = WarmupSequence.Append(source, () => true, () => { calls++; return true; }, () => { });
        Exception observed = null; try { wrapped.MoveNext(); } catch (Exception ex) { observed = ex; }
        Check(observed == originalError && calls == 0 && source.Disposals == 1, "original loading exception preserved without starting warmup");
    }
    private static IEnumerable<CodeInstruction> ViewRewrite(IEnumerable<CodeInstruction> code)
    {
        var output = ResourceRewriter.Rewrite(code, AccessTools.Method(typeof(Program), nameof(Request)),
            AccessTools.Method(typeof(Program), nameof(SelectView)), false, out bool applied);
        if (!applied) throw new Exception("surrogate view layout rejected"); return output;
    }
    private static IEnumerable<CodeInstruction> SpawnRewrite(IEnumerable<CodeInstruction> code)
    {
        var output = ResourceRewriter.Rewrite(code, AccessTools.Method(typeof(Program), nameof(Request)),
            AccessTools.Method(typeof(Program), nameof(SelectSpawn)), true, out bool applied);
        if (!applied) throw new Exception("surrogate spawn layout rejected"); return output;
    }
    private static void RunRewrite()
    {
        var harmony = new Harmony("WrathDeferredNpcViews.Checks");
        var instance = new Surrogate();
        Check(instance.View() == "normal", "surrogate original view");
        harmony.Patch(AccessTools.Method(typeof(Surrogate), "View"), transpiler: new HarmonyMethod(typeof(Program), nameof(ViewRewrite)));
        Check(instance.View() == "dummy:normal" && !lastHold, "real Harmony preserves Request(string,bool) stack and hold flag");
        harmony.Patch(AccessTools.Method(typeof(Surrogate), "Spawn"), transpiler: new HarmonyMethod(typeof(Program), nameof(SpawnRewrite)));
        expectedState = new object();
        Check(instance.Spawn("bp", 73, 1, expectedState, "id") == "dummy:spawn" && !lastHold, "real Harmony passes exact spawning arguments");
        harmony.UnpatchAll(harmony.Id);
        Check(instance.View() == "normal", "unpatch restores original request selection");
        var request = AccessTools.Method(typeof(Program), nameof(Request));
        var selector = AccessTools.Method(typeof(Program), nameof(SelectView));
        var one = new List<CodeInstruction> { new CodeInstruction(OpCodes.Ldstr, "one"), new CodeInstruction(OpCodes.Ldc_I4_0), new CodeInstruction(OpCodes.Call, request) };
        ResourceRewriter.Rewrite(one, request, selector, false, out bool applied);
        Check(!applied, "unexpected single request rejected");
        var three = one.Concat(one).Concat(one).ToArray();
        ResourceRewriter.Rewrite(three, request, selector, false, out applied);
        Check(!applied, "unexpected three requests rejected");
        var two = one.Concat(one).Select(c => new CodeInstruction(c)).ToArray();
        two[1].opcode = OpCodes.Ldc_I4_1;
        ResourceRewriter.Rewrite(two, request, selector, false, out applied);
        Check(!applied, "changed hold producer rejected");
        two = one.Concat(one).Select(c => new CodeInstruction(c)).ToArray();
        two[1].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        ResourceRewriter.Rewrite(two, request, selector, false, out applied);
        Check(!applied && two[1].blocks.Count == 1, "unexpected EH boundary rejected without input mutation");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunActualIl()
    {
        var unit = typeof(Kingmaker.EntitySystem.Entities.UnitEntityData);
        Check(GameCompatibility.Matches(unit.Assembly.Location), "exact installed game hash accepted");
        Check(!GameCompatibility.Matches(typeof(Program).Assembly.Location), "different assembly hash rejected");
        var request = AccessTools.Method(typeof(Kingmaker.ResourceManagement.BundledResourceHandle<Kingmaker.View.UnitEntityView>), "Request");
        var getter = AccessTools.Method(unit, "CreateView");
        var original = PatchProcessor.GetOriginalInstructions(getter).ToList();
        var rewritten = ResourceRewriter.Rewrite(original, request, AccessTools.Method(typeof(WrathDeferredNpcViews.Main), "ChooseViewAsset"), false, out bool applied);
        Check(applied && rewritten.Count == original.Count + 2, "actual game CreateView IL matched without executing Unity");
        Check(original.Count(c => c.Calls(request)) == rewritten.Count(c => c.Calls(request)), "original resource requests preserved");
        var spawner = AccessTools.Method(typeof(Kingmaker.Controllers.EntityCreationController), "SpawnUnit", new[] {
            typeof(Kingmaker.Blueprints.BlueprintUnit), typeof(UnityEngine.Vector3), typeof(UnityEngine.Quaternion), typeof(Kingmaker.EntitySystem.SceneEntitiesState), typeof(string) });
        original = PatchProcessor.GetOriginalInstructions(spawner).ToList();
        rewritten = ResourceRewriter.Rewrite(original, request, AccessTools.Method(typeof(WrathDeferredNpcViews.Main), "ChooseSpawnAsset"), true, out applied);
        Check(applied && rewritten.Count == original.Count + 4, "actual game SpawnUnit IL matched without executing Unity");
        Check(original.SelectMany(c => c.blocks).Count() == rewritten.SelectMany(c => c.blocks).Count(), "actual game EH metadata preserved");
        Check(AccessTools.Field(unit, "<HasDummyView>k__BackingField")?.FieldType == typeof(bool), "dummy flag exact field available");
        Check(AccessTools.Method(typeof(Kingmaker.Game), "AfterSceneLoaderFinishedArea")?.ReturnType == typeof(System.Collections.IEnumerator), "loading warmup hook exact return type");
        Check(AccessTools.Method(typeof(Kingmaker.ElementsSystem.Evaluator<Kingmaker.EntitySystem.Entities.UnitEntityData>), "TryGetValue") != null, "TryGetValue escape hook available");
        Check(AccessTools.Method(typeof(Kingmaker.AreaLogic.Cutscenes.CutscenePlayerData), "Start") != null &&
            AccessTools.Method(typeof(Kingmaker.Controllers.Dialog.DialogController), "StartDialog") != null, "cutscene and dialog barrier hooks available");
        Check(AccessTools.Method(unit, "JoinCombat", new[] { typeof(bool) }) != null, "combat escape hook available before unit joins combat");
        Check(AccessTools.Property(typeof(Kingmaker.Player), "Chapter")?.PropertyType == typeof(int) &&
            AccessTools.Method(typeof(Kingmaker.Player), "SetChapter", new[] { typeof(int) }) != null, "chapter guard and transition hook available");
        Check(AccessTools.Method(typeof(Kingmaker.UnitLogic.Commands.Base.UnitCommand), "Init", new[] { unit }) != null &&
            AccessTools.Method(typeof(Kingmaker.UnitLogic.Commands.Base.UnitCommand), "Start", Type.EmptyTypes) != null,
            "command initialization and execution escape hooks available");
        var tryMethod = AccessTools.Method(typeof(Kingmaker.ElementsSystem.Evaluator<Kingmaker.EntitySystem.Entities.UnitEntityData>), "TryGetValue");
        Check(!PatchProcessor.GetOriginalInstructions(tryMethod).Any(c => c.operand is MethodInfo m && m.Name == "HandleGetValue"),
            "actual TryGetValue bypasses HandleGetValue and needs its own escape hook");
    }
}
