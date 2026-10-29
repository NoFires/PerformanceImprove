using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Blueprints.Items;
using Kingmaker.Blueprints.Items.Ecnchantments;
using Kingmaker.Blueprints.Items.Armors;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.Blueprints.Items.Shields;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.EntitySystem;
using Kingmaker.Items;
using Kingmaker.Localization;
using Newtonsoft.Json;

namespace PerformanceImprove.Merchant
{
    internal sealed class ItemSpec
    {
        public string Id, Type, Ability, Activatable;
        public bool Gear;
        public string[] Enchantments;
        public ItemSpec[] Links;
    }

    internal static class ItemProtection
    {
        private static readonly FieldInfo[] textFields = new[] { "m_DisplayNameText", "m_DescriptionText", "m_FlavorText" }
            .Select(n => AccessTools.Field(typeof(BlueprintItem), n)).ToArray();
        private static Dictionary<string, ItemSpec> catalog;
        internal static IReadOnlyDictionary<string, ItemSpec> Catalog
        {
            get
            {
                if (catalog == null)
                    using (var stream = typeof(ItemProtection).Assembly.GetManifestResourceStream("PerformanceImprove.Merchant.ApprovedItems.json"))
                    using (var reader = new StreamReader(stream ?? throw new InvalidDataException("Missing sale protection catalog")))
                        catalog = JsonConvert.DeserializeObject<Dictionary<string, ItemSpec>>(reader.ReadToEnd());
                return catalog;
            }
        }
        internal static bool HasDedicatedText(LocalizedString text) => text == null || !string.IsNullOrEmpty(text.Key) || !ReferenceEquals(text.Shared, null);

        internal static bool Safe(ItemEntity item, bool requireStackable = true)
        {
            if (item == null || item.IsDisposed || item.IsDisposingNow || item.Count <= 0 || (requireStackable && !item.IsStackable) ||
                item.IsNonRemovable || item.HoldingSlot != null || item.Owner != null || item.Wielder != null || !item.IsIdentified ||
                item.HasOverridenVisual || item.GetType().Assembly != typeof(ItemEntity).Assembly ||
                !Catalog.TryGetValue(item.Blueprint.AssetGuid.ToString(), out var spec)) return false;
            return CheckEntity(item, spec, 0);
        }
        private static bool CheckEntity(ItemEntity item, ItemSpec spec, int depth)
        {
            if (depth > 4 || item == null || item.IsDisposed || item.IsNonRemovable || item.HoldingSlot != null ||
                item.Parts.GetAll<EntityPart>().Count != 0 || !CheckBlueprint(item.Blueprint, spec, depth)) return false;
            if (item.Facts.List.Any(f => !(f is ItemEnchantment))) return false;
            if (!item.Enchantments.Select(e => e.Blueprint.AssetGuid.ToString()).OrderBy(s => s, StringComparer.Ordinal)
                .SequenceEqual(spec.Enchantments)) return false;
            if (!string.Equals(item.Name, item.Blueprint.Name, StringComparison.Ordinal)) return false;
            if (item is ItemEntityShield shield)
            {
                foreach (var link in spec.Links)
                {
                    ItemEntity child = link.Type == "BlueprintItemArmor" ? (ItemEntity)shield.ArmorComponent : shield.WeaponComponent;
                    if (child?.Blueprint.AssetGuid.ToString() != link.Id || !CheckEntity(child, link, depth + 1)) return false;
                }
            }
            if (item is ItemEntityWeapon weapon && weapon.Second != null)
            {
                var link = spec.Links.SingleOrDefault(l => l.Id == weapon.Second.Blueprint.AssetGuid.ToString());
                if (link == null || !CheckEntity(weapon.Second, link, depth + 1)) return false;
            }
            return true;
        }
        private static bool CheckBlueprint(BlueprintItem blueprint, ItemSpec spec, int depth)
        {
            if (depth > 4 || blueprint == null || blueprint.GetType().Assembly != typeof(BlueprintItem).Assembly ||
                blueprint.GetType().Name != spec.Type || blueprint.IsNotable || blueprint is BlueprintItemKey || blueprint is BlueprintItemNote ||
                blueprint.ComponentsArray == null || blueprint.ComponentsArray.Length != 0) return false;
            bool equipment = blueprint is BlueprintItemEquipment && !(blueprint is BlueprintItemEquipmentUsable);
            if (equipment && textFields.Any(f => f == null || HasDedicatedText((LocalizedString)f.GetValue(blueprint)))) return false;
            if (blueprint is BlueprintItemEquipment gear && (gear.IsNonRemovable ||
                gear.Ability?.AssetGuid.ToString() != spec.Ability || gear.ActivatableAbility?.AssetGuid.ToString() != spec.Activatable)) return false;
            if (!blueprint.Enchantments.Select(e => e.AssetGuid.ToString()).OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(spec.Enchantments)) return false;
            if (blueprint is BlueprintItemWeapon weapon && weapon.IsNatural) return false;
            foreach (var link in spec.Links)
            {
                BlueprintItem child = null;
                if (blueprint is BlueprintItemShield shield) child = link.Type == "BlueprintItemArmor" ? (BlueprintItem)shield.ArmorComponent : shield.WeaponComponent;
                if (blueprint is BlueprintItemWeapon doubleWeapon) child = doubleWeapon.SecondWeapon;
                if (child?.AssetGuid.ToString() != link.Id || !CheckBlueprint(child, link, depth + 1)) return false;
            }
            return true;
        }
    }
}
