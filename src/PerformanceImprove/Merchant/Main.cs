using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Items;
using UnityModManagerNet;
using WrathLifecycleFix;
using WrathPerformanceLab;

namespace PerformanceImprove.Merchant
{
    internal static class Main
    {
        private const string Owner = "PerformanceImprove.Merchant";
        private static UnityModManager.ModEntry entry;
        private static Harmony harmony;
        private static BoundedCsv journal;
        private static bool enabled, rewritten, halted;
        internal static long RemovedStacks, RemovedUnits, KeptStacks, Failures;
        internal static bool Halted => halted;

        internal static bool Initialize(UnityModManager.ModEntry modEntry)
        {
            entry = modEntry;
            try
            {
                if (!GameCompatibility.Matches(typeof(VendorLogic).Assembly.Location)) throw new InvalidOperationException("Unsupported game hash");
                if (ItemProtection.Catalog.Count == 0) throw new InvalidOperationException("Empty protection catalog");
                OpenJournal();
                harmony = new Harmony(Owner); rewritten = false;
                harmony.Patch(AccessTools.Method(typeof(VendorLogic), "Deal"), transpiler: new HarmonyMethod(typeof(Main), nameof(Rewrite)) { priority = Priority.Last });
                if (!rewritten) throw new InvalidOperationException("Expected paid sale transfer did not match; no cleanup installed");
                enabled = true;
                entry.Logger.Log("Merchant cleanup enabled: only the player's completed, stackable sale batch is discarded; protected items transfer normally.");
                return true;
            }
            catch (Exception ex)
            {
                enabled = false; harmony?.UnpatchAll(Owner); journal?.Dispose(); journal = null;
                entry.Logger.LogException(ex); return false;
            }
        }
        private static void OpenJournal()
        {
            journal?.Dispose();
            var directory = Path.Combine(entry.Path, "Logs"); Directory.CreateDirectory(directory);
            journal = new BoundedCsv(Path.Combine(directory, "merchant-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".csv"),
                2 * 1024 * 1024, "utc", "event", "item_id", "blueprint_id", "count");
            halted = false;
        }
        public static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> input)
        {
            var output = SaleRewriter.Rewrite(input, AccessTools.Method(typeof(ItemsCollection), "Transfer", new[] { typeof(ItemEntity), typeof(ItemsCollection) }),
                AccessTools.Method(typeof(Main), nameof(TransferSold)), out rewritten);
            if (!rewritten) { halted = true; entry?.Logger.Log("Deal IL changed; player sale cleanup is suspended."); }
            return output;
        }
        public static ItemEntity TransferSold(ItemsCollection source, ItemEntity item, ItemsCollection store) =>
            SaleDiscard.Run(item, new Operation(source, store));

        private sealed class Operation : ISaleOperation<ItemEntity>
        {
            private readonly ItemsCollection source, store;
            private int count;
            internal Operation(ItemsCollection source, ItemsCollection store) { this.source = source; this.store = store; }
            public bool Eligible(ItemEntity item)
            {
                if (!enabled || halted || !rewritten || source == null || store == null || source == store || !store.IsPushEnable ||
                    !store.IsVendorTable || store.IsPlayerInventory || store.IsSharedStash ||
                    source != Game.Instance.Player.UISettings.ItemsForSell || item.Collection != source || !item.SellTime.HasValue ||
                    !ItemProtection.Safe(item)) { KeptStacks++; return false; }
                count = item.Count; return true;
            }
            public bool Journal(ItemEntity item) => Record("discard_intent", item, count);
            public ItemEntity Original(ItemEntity item) => source.Transfer(item, store);
            public ItemEntity Remove(ItemEntity item) => source.Remove(item);
            public bool CanDispose(ItemEntity original, ItemEntity removed) => ReferenceEquals(original, removed) && removed.Collection == null &&
                removed.Count == count && ItemProtection.Safe(removed);
            public void Dispose(ItemEntity item) => item.Dispose();
            public ItemEntity Recover(ItemEntity item)
            {
                // Removal events may move or modify the item. Never dispose another collection's object or transfer twice.
                if (item != null && !item.IsDisposed)
                {
                    if (item.Collection == source) return source.Transfer(item, store);
                    if (item.Collection == null) store.Add(item);
                }
                Record(item?.IsDisposed == true ? "disposed_with_error" : "kept_after_callback_or_error", item, count); return item;
            }
            public void Completed(ItemEntity item)
            {
                if (!item.IsDisposed) throw new InvalidOperationException("Item disposal did not finish");
                RemovedStacks++; RemovedUnits += count; Record("discarded", item, count);
            }
            public void Failed(Exception error)
            {
                Failures++; halted = true; Report("Merchant cleanup suspended: " + error);
            }
        }
        private static bool Record(string reason, ItemEntity item, int count)
        {
            try
            {
                if (journal == null || journal.LimitReached) { halted = true; return false; }
                journal.Row(DateTime.UtcNow.ToString("O"), reason, item?.UniqueId, item?.Blueprint?.AssetGuid.ToString(), count);
                journal.Flush();
                if (journal.LimitReached) { halted = true; return false; }
                return true;
            }
            catch (Exception ex) { halted = true; Failures++; Report("Merchant journal failed; cleanup suspended: " + ex.Message); return false; }
        }
        private static void Report(string message) { try { entry?.Logger.Log(message); } catch { } }
        internal static bool Toggle(bool value)
        {
            if (value && (journal == null || halted))
            {
                try { OpenJournal(); } catch (Exception ex) { entry.Logger.LogException(ex); return false; }
            }
            enabled = value; return true;
        }
        internal static bool Unload()
        {
            enabled = false; harmony?.UnpatchAll(Owner);
            try { journal?.Dispose(); } catch { }
            journal = null; return true;
        }
    }
}
