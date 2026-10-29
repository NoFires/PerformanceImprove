using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using HarmonyLib;
using PerformanceImprove;
using PerformanceImprove.Merchant;

internal static class Program
{
    private const string Managed = @"D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure\Wrath_Data\Managed";
    private static int checks;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
    private static int Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            var path = Path.Combine(Managed, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try { Routing(); HarmonyRouting(); CatalogAndIl(); FeatureSwitch(); Console.WriteLine($"{checks} checks passed"); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private sealed class Item
    {
        internal int Count = 3;
        internal bool Stackable = true, Named, Quest, Disposed;
        internal Collection Collection;
        internal string Blueprint = "common";
        internal int SellTime { get; set; }
    }
    private sealed class Collection
    {
        internal readonly List<Item> Items = new List<Item>();
        internal void Add(Item item)
        {
            var existing = Items.FirstOrDefault(i => i.Blueprint == item.Blueprint && i.Stackable && item.Stackable);
            if (existing == null) { Items.Add(item); item.Collection = this; }
            else { existing.Count += item.Count; item.Disposed = true; }
        }
        internal Item Remove(Item item) { Items.Remove(item); item.Collection = null; return item; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal Item Transfer(Item item, Collection to) { Remove(item); to.Add(item); return item; }
    }
    private sealed class Operation : ISaleOperation<Item>
    {
        internal readonly Collection Source = new Collection(), Store = new Collection(), Other = new Collection();
        internal readonly List<string> Calls = new List<string>();
        internal bool Enabled = true, JournalOk = true, MutateNamed, MoveToOther, ThrowRemoveBefore, ThrowRemoveAfter, ThrowDispose;
        internal Exception OriginalError;
        internal int OriginalCalls, Removed, Disposals, Errors;
        internal Operation(Item item) { Source.Add(item); }
        internal Operation(Collection source, Collection store) { Source = source; Store = store; }
        public bool Eligible(Item item) => Enabled && item.Stackable && !item.Named && !item.Quest && item.Collection == Source;
        public bool Journal(Item item) { Calls.Add("intent"); return JournalOk; }
        public Item Original(Item item)
        {
            OriginalCalls++; Calls.Add("native"); if (OriginalError != null) throw OriginalError;
            return Source.Transfer(item, Store);
        }
        public Item Remove(Item item)
        {
            Calls.Add("remove"); if (ThrowRemoveBefore) throw new Exception("before");
            Source.Remove(item); Removed++;
            if (ThrowRemoveAfter) throw new Exception("after");
            if (MutateNamed) item.Named = true;
            if (MoveToOther) Other.Add(item);
            return item;
        }
        public bool CanDispose(Item original, Item removed) => original == removed && !removed.Named && !removed.Quest && removed.Collection == null;
        public void Dispose(Item item) { Calls.Add("dispose"); if (ThrowDispose) throw new Exception("dispose"); item.Disposed = true; Disposals++; }
        public Item Recover(Item item)
        {
            Calls.Add("recover");
            if (item.Collection == Source) return Original(item);
            if (item.Collection == null && !item.Disposed) Store.Add(item);
            return item;
        }
        public void Completed(Item item) => Calls.Add("completed");
        public void Failed(Exception error) { Errors++; Calls.Add("error"); }
    }
    private static void Routing()
    {
        var item = new Item(); var op = new Operation(item);
        Check(SaleDiscard.Run(item, op) == item && item.Disposed && op.Store.Items.Count == 0, "sold stack deleted before entering merchant stock");
        Check(op.Calls.SequenceEqual(new[] { "intent", "remove", "dispose", "completed" }), "journal precedes deletion; original transfer not repeated");
        foreach (var flag in new[] { "named", "quest", "not-stackable", "disabled" })
        {
            item = new Item { Named = flag == "named", Quest = flag == "quest", Stackable = flag != "not-stackable" };
            op = new Operation(item) { Enabled = flag != "disabled" };
            SaleDiscard.Run(item, op);
            Check(!item.Disposed && op.Store.Items.Contains(item) && op.OriginalCalls == 1 && op.Removed == 0, flag + " uses original sale without deletion");
        }
        item = new Item(); op = new Operation(item) { JournalOk = false }; SaleDiscard.Run(item, op);
        Check(op.OriginalCalls == 1 && op.Removed == 0 && !item.Disposed, "unavailable audit journal preserves native transfer");
        item = new Item(); op = new Operation(item) { MutateNamed = true }; SaleDiscard.Run(item, op);
        Check(!item.Disposed && item.Collection == op.Store && op.Disposals == 0, "item made unique by removal callback is retained");
        item = new Item(); op = new Operation(item) { MoveToOther = true }; SaleDiscard.Run(item, op);
        Check(!item.Disposed && item.Collection == op.Other && op.Disposals == 0, "callback moving item to another inventory prevents disposal");
        item = new Item(); op = new Operation(item) { ThrowRemoveBefore = true }; SaleDiscard.Run(item, op);
        Check(op.Errors == 1 && op.OriginalCalls == 1 && item.Collection == op.Store && !item.Disposed, "removal failure before detachment falls back once");
        item = new Item(); op = new Operation(item) { ThrowRemoveAfter = true }; SaleDiscard.Run(item, op);
        Check(op.Errors == 1 && op.OriginalCalls == 0 && op.Removed == 1 && item.Collection == op.Store, "failure after detachment does not remove or transfer twice");
        item = new Item(); op = new Operation(item) { ThrowDispose = true }; SaleDiscard.Run(item, op);
        Check(op.Errors == 1 && !item.Disposed && item.Collection == op.Store, "disposal failure retains an undisposed detached object");
        item = new Item(); var original = new Exception("original transfer exception");
        op = new Operation(item) { Enabled = false, OriginalError = original };
        Exception observed = null; try { SaleDiscard.Run(item, op); } catch (Exception ex) { observed = ex; }
        Check(observed == original && op.OriginalCalls == 1 && op.Errors == 0, "original transfer exception propagates with no retry");
        item = new Item(); op = new Operation(item); var stock = new Item { Count = 11 }; op.Store.Add(stock);
        SaleDiscard.Run(item, op);
        Check(stock.Count == 11 && op.Store.Items.Count == 1 && item.Disposed, "identical native stock remains unchanged even when sale could have merged");
    }
    private sealed class Money
    {
        internal int Value;
        [MethodImpl(MethodImplOptions.NoInlining)] internal void GainMoney(int amount) { Value += amount; }
        [MethodImpl(MethodImplOptions.NoInlining)] internal void SpendMoney(int amount) { Value -= amount; }
    }
    private sealed class Vendor
    {
        internal readonly Money Money = new Money();
        internal readonly Collection Sell = new Collection(), Buy = new Collection(), Store = new Collection(), Inventory = new Collection();
        internal Item Sold = new Item(), Bought = new Item { Count = 1, Blueprint = "purchased" };
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void Deal(bool possible)
        {
            if (!possible) return;
            Money.GainMoney(Sold.Count); Sold.SellTime = 1; Sell.Transfer(Sold, Store);
            Money.SpendMoney(Bought.Count); Buy.Transfer(Bought, Inventory);
        }
    }
    private static int redirects;
    private static Item Redirect(Collection source, Item item, Collection store) { redirects++; return SaleDiscard.Run(item, new Operation(source, store)); }
    private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> code)
    {
        var result = SaleRewriter.Rewrite(code, AccessTools.Method(typeof(Collection), "Transfer"), AccessTools.Method(typeof(Program), nameof(Redirect)), out bool applied);
        if (!applied) throw new Exception("Surrogate deal mismatch"); return result;
    }
    private static void HarmonyRouting()
    {
        var harmony = new Harmony("PerformanceImprove.MerchantChecks");
        var method = AccessTools.Method(typeof(Vendor), "Deal");
        harmony.Patch(method, transpiler: new HarmonyMethod(typeof(Program), nameof(Rewrite)));
        var vendor = new Vendor(); vendor.Sell.Add(vendor.Sold); vendor.Buy.Add(vendor.Bought); redirects = 0;
        vendor.Deal(true);
        Check(redirects == 1 && vendor.Sold.Disposed && !vendor.Store.Items.Any(), "real Harmony redirects only the player sale transfer");
        Check(!vendor.Bought.Disposed && vendor.Inventory.Items.Contains(vendor.Bought), "buy transaction remains the original transfer");
        Check(vendor.Money.Value == 2 && vendor.Sold.SellTime == 1, "native payment and sale marking execute unchanged before cleanup");
        vendor = new Vendor(); vendor.Sell.Add(vendor.Sold); vendor.Buy.Add(vendor.Bought); redirects = 0; vendor.Deal(false);
        Check(redirects == 0 && !vendor.Sold.Disposed && vendor.Money.Value == 0, "rejected deal never clears staged player goods");
        harmony.UnpatchAll(harmony.Id);
        vendor = new Vendor(); vendor.Sell.Add(vendor.Sold); vendor.Buy.Add(vendor.Bought); vendor.Deal(true);
        Check(!vendor.Sold.Disposed && vendor.Store.Items.Contains(vendor.Sold), "unpatch restores native vendor sale");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CatalogAndIl()
    {
        Check(ItemProtection.Catalog.Count == 1190, "embedded metadata allowlist loaded");
        Check(ItemProtection.Catalog.ContainsKey("193972650bc0e40409a6773e924ad612") && ItemProtection.Catalog["193972650bc0e40409a6773e924ad612"].Enchantments.Length > 0,
            "ordinary longsword +2 eligible despite enchantment");
        Check(ItemProtection.Catalog.ContainsKey("7453fb8aa1cd7f3428a14eeadc2022d7"), "ordinary longsword +5 not excluded by enhancement level");
        Check(!ItemProtection.Catalog.ContainsKey("cf813d525f47495f9fa6f0173568c85e"), "equipment with explicit dedicated name excluded");
        Check(!ItemProtection.Catalog.ContainsKey("03d706655c07d804cb9d5a5583f9aec5"), "story-referenced unmarked item preserved conservatively");
        Check(!ItemProtection.Catalog.ContainsKey("11111111111111111111111111111111"), "unknown Mod item protected by default");
        var text = new Kingmaker.Localization.LocalizedString { Key = "missing-translation-but-named" };
        Check(ItemProtection.HasDedicatedText(text), "name protection uses localization key even when current translation is missing");
        Check(!ItemProtection.HasDedicatedText(new Kingmaker.Localization.LocalizedString { Key = "" }), "empty generic type name does not masquerade as unique text");
        var transfer = AccessTools.Method(typeof(Kingmaker.Items.ItemsCollection), "Transfer", new[] { typeof(Kingmaker.Items.ItemEntity), typeof(Kingmaker.Items.ItemsCollection) });
        var deal = AccessTools.Method(typeof(Kingmaker.Items.VendorLogic), "Deal");
        var original = PatchProcessor.GetOriginalInstructions(deal).ToList();
        var output = SaleRewriter.Rewrite(original, transfer, AccessTools.Method(typeof(PerformanceImprove.Merchant.Main), "TransferSold"), out bool applied);
        Check(applied && output.Count == original.Count, "actual game Deal IL accepts an in-place sale-only replacement");
        Check(original.Where((c, i) => !Equals(c.operand, output[i].operand) || c.opcode != output[i].opcode).Count() == 1,
            "exactly one instruction changes; prices, events and remaining trade instructions preserved");
        Check(output.Count(c => c.Calls(transfer)) == 1, "actual buy transfer remains native");
        Check(output.SelectMany(c => c.blocks).Count() == original.SelectMany(c => c.blocks).Count() &&
            output.SelectMany(c => c.labels).Count() == original.SelectMany(c => c.labels).Count(), "real game exception boundaries and branches preserved");
        SaleRewriter.Rewrite(original.Where(c => !c.Calls(transfer)), transfer, AccessTools.Method(typeof(PerformanceImprove.Merchant.Main), "TransferSold"), out applied);
        Check(!applied, "unrecognized vendor layout rejects cleanup");
        var withoutPayment = original.Where(c => !(c.operand is MethodInfo m && m.Name == "GainMoney"));
        SaleRewriter.Rewrite(withoutPayment, transfer, AccessTools.Method(typeof(PerformanceImprove.Merchant.Main), "TransferSold"), out applied);
        Check(!applied, "sale placement without confirmed payment instruction rejected");
    }
    private static void FeatureSwitch()
    {
        var calls = new List<string>();
        FeatureSlot Slot(string name) => new FeatureSlot(() => { calls.Add(name + ":init"); return true; },
            value => { calls.Add(name + ":" + value); return true; }, () => { calls.Add(name + ":unload"); return true; }, _ => { });
        var group = new FeatureGroup(Slot("life"), Slot("npc"), Slot("merchant"));
        Check(group.Enable(true, true, true) && group.Merchant.Active, "new cleanup participates in the unified master switch");
        Check(group.Merchant.Set(false) && group.Lifecycle.Active && group.Npc.Active, "cleanup can be disabled without changing previous features");
        group.Merchant.Set(true); calls.Clear(); group.Disable();
        Check(calls.SequenceEqual(new[] { "npc:False", "merchant:False", "life:False" }), "master disable restores models then stops sale deletion and lifecycle feature");
        group.Enable(false, false, true); Check(group.Merchant.Active && !group.Lifecycle.Active && !group.Npc.Active, "merchant-only configuration works");
        calls.Clear(); group.Unload();
        Check(calls.SequenceEqual(new[] { "npc:unload", "merchant:unload", "life:unload" }), "unload includes the new feature independently");
        var serializer = new XmlSerializer(typeof(Configuration));
        using (var reader = new StringReader("<Configuration><LifecycleCleanup>true</LifecycleCleanup><DeferredNpcViews>false</DeferredNpcViews></Configuration>"))
            Check(((Configuration)serializer.Deserialize(reader)).MerchantSaleCleanup, "old configuration enables the explicitly requested new feature by default");
        var config = new Configuration { MerchantSaleCleanup = false }; string xml;
        using (var writer = new StringWriter()) { serializer.Serialize(writer, config); xml = writer.ToString(); }
        using (var reader = new StringReader(xml)) Check(!((Configuration)serializer.Deserialize(reader)).MerchantSaleCleanup, "new cleanup off switch persists across restart");
    }
}
