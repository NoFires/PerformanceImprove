"""Build a conservative metadata-only sale allowlist from the local reference archive."""
import collections
import hashlib
import json
import re
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
GAME = Path(r"D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure")
GUID = re.compile(rb"!bp_([0-9a-f]{32})", re.I)


def ref(value):
    return value[4:] if isinstance(value, str) and value.startswith("!bp_") else None


def text_set(value):
    return not isinstance(value, dict) or bool(value.get("m_Key") or value.get("Shared"))


def main():
    records, candidates, paths = {}, {}, {}
    with zipfile.ZipFile(GAME / "blueprints.zip") as archive:
        names = [n for n in archive.namelist() if n.endswith(".jbp")]
        # Type blueprints contribute default weapon enchantments; retain only small metadata dictionaries.
        for name in names:
            if not name.startswith(("Weapons/", "Equipment/", "Items/")):
                continue
            raw = archive.read(name)
            if b'"m_IsNotable"' not in raw and b'"m_Enchantments"' not in raw:
                continue
            parsed = json.loads(raw); data = parsed.get("Data", {})
            ident = parsed.get("AssetId"); records[ident] = data; paths[ident] = name
        for ident, data in records.items():
            name = paths[ident]
            kind = data.get("$type", "").split(", ")[-1]
            if not kind.startswith("BlueprintItem") or data.get("m_IsNotable") is not False:
                continue
            if kind in {"BlueprintItemKey", "BlueprintItemNote", "BlueprintItemThiefTool"}:
                continue
            if re.search(r"unique|artisan|relic|quest|notable|finnean|artifact|naturalweapon", name, re.I):
                continue
            if data.get("Components") or data.get("IsNonRemovable"):
                continue
            consumable = kind == "BlueprintItemEquipmentUsable"
            equipment = any(word in kind for word in ("Equipment", "Weapon", "Armor", "Shield")) and not consumable
            if equipment and any(text_set(data.get(key)) for key in ("m_DisplayNameText", "m_DescriptionText", "m_FlavorText")):
                continue
            if equipment and (ref(data.get("m_Ability")) or ref(data.get("m_ActivatableAbility"))):
                continue
            candidates[ident] = {"Type": kind, "Gear": equipment, "Ability": ref(data.get("m_Ability")),
                                 "Activatable": ref(data.get("m_ActivatableAbility"))}
        # Referenced in story content is a reason to preserve, including unmarked quest/reward objects.
        story_references = set()
        for name in names:
            if any(segment in name for segment in ("/Dialogs/", "/Quests/", "/Etudes/", "/Cutscenes/")):
                story_references.update(m.decode().lower() for m in GUID.findall(archive.read(name)))
    rejected_story = set(candidates) & story_references
    for ident in rejected_story:
        del candidates[ident]

    def describe(ident, depth=0):
        if depth > 4 or ident not in records:
            raise ValueError("Unknown item dependency")
        data = records[ident]; kind = data.get("$type", "").split(", ")[-1]
        enchantments = [ref(e) for e in data.get("m_Enchantments", []) if ref(e)]
        if kind == "BlueprintItemWeapon":
            type_id = ref(data.get("m_Type"))
            if type_id not in records: raise ValueError("Unknown weapon type")
            enchantments += [ref(e) for e in records[type_id].get("m_Enchantments", []) if ref(e)]
        links = []
        for field in ("m_ArmorComponent", "m_WeaponComponent", "m_SecondWeapon"):
            linked = ref(data.get(field))
            if linked:
                if linked in story_references: raise ValueError("Story-linked component")
                child = records.get(linked, {})
                if child.get("m_IsNotable") or child.get("Components") or any(text_set(child.get(k)) for k in ("m_DisplayNameText", "m_DescriptionText", "m_FlavorText")):
                    raise ValueError("Protected item component")
                links.append({"Id": linked, **describe(linked, depth + 1)})
        if kind == "BlueprintItemShield":
            enchantments = [e for link in links for e in link["Enchantments"]]
        return {"Type": kind, "Enchantments": sorted(enchantments), "Links": links}

    allowed = {}
    for ident, definition in candidates.items():
        try:
            allowed[ident] = {**definition, **describe(ident)}
        except ValueError:
            pass
    destination = ROOT / "src/PerformanceImprove/Merchant/ApprovedItems.json"
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(allowed, separators=(",", ":"), sort_keys=True), encoding="utf-8")
    report = {"source": str(GAME / "blueprints.zip"), "approved": len(allowed), "story_references_excluded": len(rejected_story),
              "types": dict(collections.Counter(d["Type"] for d in allowed.values())),
              "approved_enchanted": sum(bool(d["Enchantments"]) for d in allowed.values()),
              "catalog_sha256": hashlib.sha256(destination.read_bytes()).hexdigest(),
              "basis": "Conservative original-item metadata, explicit naming and story-reference guards; not a runtime Mod compatibility guarantee"}
    (ROOT / "analysis/vendor-item-catalog.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
