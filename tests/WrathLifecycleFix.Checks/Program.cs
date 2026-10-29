using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using HarmonyLib;
using UniRx;
using Owlcat.Runtime.UniRx;
using WrathLifecycleFix;
using Fix = WrathLifecycleFix.Main;

internal static class Program
{
    private const string Managed = @"D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure\Wrath_Data\Managed";
    private static readonly Exception Original = new InvalidOperationException("original Stop failure");
    private static int checks;
    private static LastValueOnLateUpdateObservableFabric testFabric;
    private static LastValueOnLateUpdateObservableFabric ProvideTestFabric() { return testFabric; }
    private static IEnumerable<CodeInstruction> RedirectTestFabric(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method &&
                method.DeclaringType == typeof(LastValueOnLateUpdateObservableFabric) && method.Name == "get_Instance")
            {
                var replacement = new CodeInstruction(instruction);
                replacement.operand = AccessTools.Method(typeof(Program), nameof(ProvideTestFabric));
                yield return replacement;
            }
            else yield return instruction;
        }
    }
    private sealed class Source : IObservable<Unit>
    {
        private readonly List<IObserver<Unit>> observers = new List<IObserver<Unit>>();
        internal int Count => observers.Count;
        public IDisposable Subscribe(IObserver<Unit> observer)
        {
            observers.Add(observer);
            return Disposable.Create(() => observers.Remove(observer));
        }
        internal void Emit() { foreach (var observer in observers.ToArray()) observer.OnNext(Unit.Default); }
    }
    private sealed class Controller
    {
        internal readonly Source Source;
        private ReactiveCommand command;
        internal int Notifications;
        internal Action Callback;
        internal bool FailBefore, FailAfter;
        internal bool Stopped;
        internal bool UseShippedLateUpdate;
        internal Controller(Source source = null) { Source = source ?? new Source(); }
        public ReactiveCommand UpdateCommand
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get
            {
                if (command == null)
                {
                    command = new ReactiveCommand();
                    IObservable<Unit> updates = Source;
                    if (UseShippedLateUpdate) updates = updates.ObserveLastValueOnLateUpdate();
                    ObservableExtensions.Subscribe(updates, delegate(Unit _) { Notifications++; command.Execute(); });
                }
                return command;
            }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void Stop()
        {
            if (Stopped) return;
            if (FailBefore) throw Original;
            Stopped = true;
            Callback?.Invoke();
            if (FailAfter) throw Original;
        }
    }
    private sealed class Token : IDisposable
    {
        internal bool Throw;
        internal int Disposals;
        public void Dispose() { Disposals++; if (Throw) throw Original; }
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAILED: " + message);
        Console.WriteLine("PASS " + message); checks++;
    }
    private static void Set(string name, object value) { typeof(Fix).GetField(name, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value); }
    private static SubscriptionLifetime NewLifetime()
    {
        var lifetime = new SubscriptionLifetime(value => ((Controller)value).Stopped);
        Set("lifetime", lifetime); Set("enabled", true);
        return lifetime;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference MakeUnrootedController()
    {
        var controller = new Controller();
        _ = controller.UpdateCommand;
        return new WeakReference(controller);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference MakeStoppedController(Source source)
    {
        var controller = new Controller(source);
        _ = controller.UpdateCommand;
        controller.Stop();
        return new WeakReference(controller);
    }
    private static int Main()
    {
        var harmony = new Harmony("WrathLifecycleFix.Checks");
        try
        {
            // Validate the original DLL's IL without constructing or executing a game object.
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                string file = Path.Combine(Managed, new AssemblyName(args.Name).Name + ".dll");
                return File.Exists(file) ? Assembly.LoadFrom(file) : null;
            };
            Assembly game = Assembly.LoadFrom(Path.Combine(Managed, "Assembly-CSharp.dll"));
            Check(GameCompatibility.Matches(game.Location) && !GameCompatibility.Matches(typeof(Program).Assembly.Location),
                "version guard accepts the analysed game DLL and rejects a different assembly");
            Type type = game.GetType("Kingmaker.UnitLogic.Class.LevelUp.LevelUpController", true);
            MethodInfo actualGetter = AccessTools.PropertyGetter(type, "UpdateCommand");
            var original = PatchProcessor.GetOriginalInstructions(actualGetter);
            var candidate = CaptureRewriter.Rewrite(original, AccessTools.Method(typeof(Fix), nameof(Fix.CaptureSubscription)), out bool applied);
            if (!applied)
                for (int i = 0; i < original.Count; i++)
                    if (original[i].operand is MethodInfo method)
                        Console.WriteLine("IL " + i + " " + method.DeclaringType.FullName + "." + method + " NEXT " + (i + 1 < original.Count ? original[i + 1].opcode.ToString() : "none"));
            Check(applied && candidate.Count == original.Count + 2, "actual installed getter IL accepts exactly one ownership capture insertion");
            Check(actualGetter.GetMethodBody().ExceptionHandlingClauses.Count == 0 && AccessTools.Field(type, "<Stopped>k__BackingField").FieldType == typeof(bool),
                "actual Stop state and getter guard match the installed game layout");
            Check(original.Count(c => c.opcode == OpCodes.Call && CaptureRewriter.IsExpectedSubscribe(c.operand as MethodInfo)) == 1,
                "capture targets the exact UniRx Subscribe<Unit> overload, not arbitrary subscribers");
            bool unchanged = true;
            for (int i = 0, j = 0; i < original.Count; i++, j++)
            {
                if (!ReferenceEquals(original[i], candidate[j])) unchanged = false;
                if (original[i].opcode == OpCodes.Call && CaptureRewriter.IsExpectedSubscribe(original[i].operand as MethodInfo)) j += 2;
            }
            Check(unchanged, "original game instructions, branches and exception metadata remain untouched");
            var missing = new List<CodeInstruction> { new CodeInstruction(OpCodes.Ret) };
            var skipped = CaptureRewriter.Rewrite(missing, AccessTools.Method(typeof(Fix), nameof(Fix.CaptureSubscription)), out applied);
            Check(!applied && ReferenceEquals(skipped[0], missing[0]), "unrecognized getter fails closed without changing behavior");
            var repeated = original.Concat(original).ToList();
            skipped = CaptureRewriter.Rewrite(repeated, AccessTools.Method(typeof(Fix), nameof(Fix.CaptureSubscription)), out applied);
            Check(!applied && skipped.Count == repeated.Count, "ambiguous subscription layouts are never partially rewritten");

            var lifetime = NewLifetime();
            var getter = AccessTools.PropertyGetter(typeof(Controller), nameof(Controller.UpdateCommand));
            var stop = AccessTools.Method(typeof(Controller), nameof(Controller.Stop));
            harmony.Patch(getter, transpiler: new HarmonyMethod(typeof(Fix), nameof(Fix.CaptureTranspiler)));
            harmony.Patch(stop, prefix: new HarmonyMethod(typeof(Fix), nameof(Fix.BeforeStop)), finalizer: new HarmonyMethod(typeof(Fix), nameof(Fix.AfterStop)));
            var owner = new Controller(); var command = owner.UpdateCommand;
            Check(ReferenceEquals(command, owner.UpdateCommand) && owner.Source.Count == 1 && lifetime.Snapshot()[0] == 1,
                "real Harmony getter preserves cached command identity and single subscription");
            // Run the shipped Owlcat operator against an uninitialized managed fabric shell.
            // Only its plain linked-list methods execute; no Unity MonoBehaviour constructor,
            // singleton getter, object search, game object or native engine method is invoked.
            testFabric = (LastValueOnLateUpdateObservableFabric)FormatterServices.GetUninitializedObject(typeof(LastValueOnLateUpdateObservableFabric));
            var nodesField = AccessTools.Field(typeof(LastValueOnLateUpdateObservableFabric), "m_ObservablesToLateUpdate");
            var nodes = (ICollection)Activator.CreateInstance(nodesField.FieldType);
            nodesField.SetValue(testFabric, nodes);
            // Redirect only factory reads in the operator/cancellation closure; compiling the
            // original Unity factory getter itself would resolve unavailable native ECalls.
            Type lateType = typeof(LastValueOnLateUpdateObservableFabric).Assembly.GetType("Owlcat.Runtime.UniRx.LastValueOnLateUpdateObservable`1").MakeGenericType(typeof(Unit));
            var factoryReaders = new List<MethodInfo> { AccessTools.DeclaredMethod(lateType, "SubscribeCore") };
            foreach (Type nestedDefinition in lateType.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
            {
                Type nested = nestedDefinition.ContainsGenericParameters ? nestedDefinition.MakeGenericType(typeof(Unit)) : nestedDefinition;
                foreach (MethodInfo method in nested.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    if (method.Name.StartsWith("<SubscribeCore>b__", StringComparison.Ordinal)) factoryReaders.Add(method);
            }
            foreach (MethodInfo reader in factoryReaders)
                harmony.Patch(reader, transpiler: new HarmonyMethod(typeof(Program), nameof(RedirectTestFabric)));
            var shipped = new Controller { UseShippedLateUpdate = true };
            var shippedOther = new Controller { UseShippedLateUpdate = true };
            _ = shipped.UpdateCommand; _ = shippedOther.UpdateCommand;
            shipped.Source.Emit();
            Check(nodes.Count == 2 && shipped.Notifications == 0, "shipped Owlcat operator preserves deferral until LateUpdate");
            AccessTools.Method(typeof(LastValueOnLateUpdateObservableFabric), "LateUpdate").Invoke(testFabric, null);
            Check(shipped.Notifications == 1, "real shipped LateUpdate delivery is preserved before Stop");
            shipped.Stop();
            Check(nodes.Count == 1 && shipped.Source.Count == 0 && shippedOther.Source.Count == 1,
                "Stop cancellation removes the real Owlcat Fabric node and leaves unrelated nodes intact");
            shippedOther.Stop();
            Check(nodes.Count == 0, "all finished test owners leave the shipped Fabric at its original baseline");
            foreach (MethodInfo reader in factoryReaders) harmony.Unpatch(reader, HarmonyPatchType.Transpiler, harmony.Id);
            testFabric = null;
            long disposedBeforeOwnerStop = lifetime.Snapshot()[1];
            int notifications = 0;
            using (command.Subscribe(_ => notifications++))
            {
                owner.Source.Emit();
                Check(owner.Notifications == 1 && notifications == 1, "live controller events still reach the real UniRx command");
                owner.Callback = () => owner.Source.Emit();
                owner.Stop();
                Check(owner.Notifications == 2 && notifications == 2, "Stop callbacks retain original notification ordering until finalization");
                Check(owner.Source.Count == 0 && lifetime.Snapshot()[1] == disposedBeforeOwnerStop + 1, "successful Stop disposes only its captured subscription");
                owner.Source.Emit();
                Check(owner.Notifications == 2, "stopped controller no longer receives upstream callbacks");
                command.Execute();
                Check(notifications == 3, "cleanup does not dispose the public command or downstream UI subscribers");
            }
            owner.Stop();
            Check(lifetime.Snapshot()[1] == disposedBeforeOwnerStop + 1, "repeated Stop never double-disposes a captured subscription");

            var first = new Controller(); var other = new Controller();
            _ = first.UpdateCommand; _ = other.UpdateCommand;
            first.Stop(); other.Source.Emit();
            Check(other.Notifications == 1 && other.Source.Count == 1, "cleanup leaves unrelated active controllers connected");
            other.Stop();
            var failed = new Controller { FailBefore = true }; _ = failed.UpdateCommand;
            Exception seen = null;
            try { failed.Stop(); } catch (Exception ex) { seen = ex; }
            failed.Source.Emit();
            Check(ReferenceEquals(seen, Original) && !failed.Stopped && failed.Source.Count == 1 && failed.Notifications == 1,
                "Stop failure before transition preserves the original exception and live subscription");
            failed.FailBefore = false; failed.Stop();
            Check(failed.Source.Count == 0, "a later successful retry still releases the subscription");
            var lateFailure = new Controller { FailAfter = true }; _ = lateFailure.UpdateCommand;
            seen = null; try { lateFailure.Stop(); } catch (Exception ex) { seen = ex; }
            Check(ReferenceEquals(seen, Original) && lateFailure.Stopped && lateFailure.Source.Count == 0,
                "post-transition Stop exceptions propagate while finished controller ownership is released");

            var late = new Controller(); late.Stop(); _ = late.UpdateCommand;
            Check(late.Source.Count == 0, "first getter use after a completed Stop cannot add a permanent root");
            var callbackGetter = new Controller(); int during = -1;
            callbackGetter.Callback = () => { _ = callbackGetter.UpdateCommand; during = callbackGetter.Source.Count; };
            callbackGetter.Stop();
            Check(during == 1 && callbackGetter.Source.Count == 0, "getter first used inside Stop callback is released only after callback completion");
            var recursive = new Controller(); _ = recursive.UpdateCommand;
            recursive.Callback = () => { recursive.Stop(); recursive.Source.Emit(); };
            recursive.Stop();
            Check(recursive.Notifications == 1 && recursive.Source.Count == 0, "reentrant Stop does not release subscriptions before the outer callback finishes");

            var disabled = new Controller(); _ = disabled.UpdateCommand;
            lifetime.DropTracking(); Set("enabled", false); disabled.Source.Emit(); disabled.Stop();
            Check(disabled.Notifications == 1 && disabled.Source.Count == 1,
                "disabling restores original behavior without disconnecting an active UI");
            lifetime = NewLifetime();
            var strongSource = new Source(); WeakReference stoppedWeak = MakeStoppedController(strongSource);
            WeakReference activeWeak = MakeUnrootedController();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check(strongSource.Count == 0 && !stoppedWeak.IsAlive, "stopped controller is collectible even when its old upstream source remains rooted");
            Check(!activeWeak.IsAlive, "ephemeron tracking does not root an otherwise unreferenced controller through its token");
            GC.KeepAlive(strongSource);

            var rawOwner = new Controller(); var bad = new Token { Throw = true }; var good = new Token();
            Check(ReferenceEquals(lifetime.Capture(bad, rawOwner), bad), "capture returns the exact original disposable");
            lifetime.Capture(good, rawOwner); lifetime.Capture(good, rawOwner);
            rawOwner.Stopped = true; lifetime.BeginStop(rawOwner); lifetime.EndStop(rawOwner);
            Check(bad.Disposals == 1 && good.Disposals == 1 && lifetime.Snapshot()[2] == 1,
                "disposal exceptions are counted and do not prevent other owned subscriptions from being released");
            lifetime.BeginStop(rawOwner); lifetime.EndStop(rawOwner);
            Check(good.Disposals == 1, "released owner metadata is removed, not retained for repeated disposal");
            Set("enabled", false); harmony.UnpatchAll(harmony.Id);
            var originalOwner = new Controller(); _ = originalOwner.UpdateCommand; originalOwner.Stop(); originalOwner.Source.Emit();
            Check(originalOwner.Notifications == 1 && originalOwner.Source.Count == 1, "removing patches restores the original getter and Stop behavior");
            Console.WriteLine("Checks passed: " + checks);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Set("enabled", false); harmony.UnpatchAll(harmony.Id); }
    }
}
