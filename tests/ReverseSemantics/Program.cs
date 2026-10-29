using System;
using System.Collections;
using System.Reflection;
using Owlcat.Runtime.UI.MVVM;
using Owlcat.Runtime.UniRx;
using UniRx;

internal static class Program
{
    private sealed class Updatable : ICanConvertPropertiesToReactive
    {
        public ReactiveCommand UpdateCommand { get; } = new ReactiveCommand();
        public int GetterCalls;
        public int GetValue() { GetterCalls++; return GetterCalls; }
    }
    private sealed class Owner : BaseDisposable
    {
        public void Add(IDisposable child) { AddDisposable(child); }
        public int Children => ((ICollection)typeof(BaseDisposable).GetField("m_Disposables", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(this)).Count;
        protected override void DisposeImplementation() { }
    }
    private sealed class CountedDisposable : IDisposable
    {
        public static long Comparisons;
        public override bool Equals(object value) { Comparisons++; return ReferenceEquals(this, value); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public void Dispose() { }
    }
    private sealed class CancelFlag : IDisposable
    {
        public bool Cancelled;
        public void Dispose() { Cancelled = true; }
    }
    private class RefreshBase : BaseDisposable
    {
        public int RefreshCalls;
        public RefreshBase(ReactiveProperty<int> source) { AddDisposable(source.Subscribe(_ => RefreshCalls++)); }
        protected override void DisposeImplementation() { }
    }
    private sealed class RefreshDerived : RefreshBase
    {
        private readonly ReactiveProperty<int> ownSource = new ReactiveProperty<int>();
        public RefreshDerived(ReactiveProperty<int> source) : base(source) { AddDisposable(ownSource.Subscribe(_ => RefreshCalls++)); }
    }
    private static int checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAILED " + message);
        Console.WriteLine("PASS " + message);
        checks++;
    }
    private static int Main()
    {
        try
        {
            var source = new Updatable();
            var property = source.GetReactiveProperty(s => s.GetValue());
            IDisposable downstream = property.Subscribe(_ => { });
            downstream.Dispose();
            int before = source.GetterCalls;
            source.UpdateCommand.Execute();
            Check(source.GetterCalls == before + 1, "disposing downstream subscription leaves GetReactiveProperty upstream active");
            property.Dispose();
            before = source.GetterCalls;
            source.UpdateCommand.Execute();
            Check(source.GetterCalls == before, "disposing intermediate ReadOnlyReactiveProperty disconnects upstream");
            source.UpdateCommand.Dispose();
            var parent = new Owner();
            var child = new Owner();
            parent.Add(child);
            Check(parent.Children == 1, "BaseDisposable parent owns child after AddDisposable");
            child.Dispose();
            Check(parent.Children == 0, "BaseDisposable.Dispose removes child from parent (leak false positive excluded)");
            parent.Dispose();
            var bulk = new Owner();
            const int n = 1000;
            CountedDisposable.Comparisons = 0;
            for (int i = 0; i < n; i++) bulk.Add(new CountedDisposable());
            Check(CountedDisposable.Comparisons == (long)n * (n - 1) / 2, "1000 distinct AddDisposable calls perform 499500 equality checks");
            bulk.Dispose();
            using (var unit = new ReactiveProperty<int>(1))
            using (var refresh = new RefreshDerived(unit))
                Check(refresh.RefreshCalls == 2, "base subscription plus own subscription emit twice during construction pattern");
            // Execute the shipped observer directly, without constructing a Unity MonoBehaviour/factory.
            bool completed = false;
            var flag = new CancelFlag();
            var observerType = typeof(ICanConvertPropertiesToReactive).Assembly.GetType("Owlcat.Runtime.UniRx.LastValueOnLateUpdateObserver`1").MakeGenericType(typeof(int));
            var observer = (IObserver<int>)Activator.CreateInstance(observerType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new object[] { Observer.Create<int>(_ => { }, () => completed = true), flag }, null);
            observer.OnCompleted();
            Check(completed && !flag.Cancelled, "shipped LateUpdate observer forwards completion without disposing cancellation token");
            ((IDisposable)observer).Dispose();
            Check(flag.Cancelled, "explicit LateUpdate observer disposal invokes cancellation token");
            Console.WriteLine("Checks passed: " + checks);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
