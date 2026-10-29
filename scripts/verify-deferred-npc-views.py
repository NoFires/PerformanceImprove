"""Verify only the new package/current game evidence; do not rewrite historical baselines."""
import hashlib
import json
import re
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
GAME = Path(r"D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure")


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    mod_id = "WrathDeferredNpcViews"
    archive = ROOT / "dist/WrathDeferredNpcViews-0.1.0.zip"
    result = {"version": "0.1.0", "archive_sha256": digest(archive), "game_runtime_tested": False,
              "deployed_to_game": (GAME / "Mods" / mod_id).exists()}
    with zipfile.ZipFile(archive) as z:
        entries = {name.replace("\\", "/"): name for name in z.namelist()}
        assert set(entries) == {f"{mod_id}/Info.json", f"{mod_id}/README.md", f"{mod_id}/{mod_id}.dll"}
        binary = z.read(entries[f"{mod_id}/{mod_id}.dll"])
        assert binary == (ROOT / f"src/{mod_id}/bin/Release/net481/{mod_id}.dll").read_bytes()
        assert z.read(entries[f"{mod_id}/README.md"]) == (ROOT / "docs/deferred-npc-views.md").read_bytes()
        assert json.loads(z.read(entries[f"{mod_id}/Info.json"]))["Version"] == result["version"]
        result["dll_sha256"] = hashlib.sha256(binary).hexdigest()
        result["package_entries"] = sorted(entries)
    result["game_sha256"] = digest(GAME / "Wrath_Data/Managed/Assembly-CSharp.dll")
    assert result["game_sha256"] == "2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953"
    text = (ROOT / "analysis/deferred-npc-views-checks.txt").read_text(encoding="utf-8-sig")
    result["checks_passed"] = int(re.search(r"(\d+) checks passed", text).group(1))
    assert len(re.findall(r"^PASS ", text, re.M)) == result["checks_passed"]
    baseline_folder = ROOT / "analysis/installations/20261021-001418-lifecycle-fix"
    baseline = json.loads((baseline_folder / "dll-inventory-before.json").read_text(encoding="utf-8"))
    install = json.loads((baseline_folder / "installation.json").read_text(encoding="utf-8"))
    baseline[r"Mods\WrathLifecycleFix\WrathLifecycleFix.dll"] = install["dll_sha256"]
    result["existing_dlls_checked"] = len(baseline)
    result["changed_since_lifecycle_install"] = [name for name, value in baseline.items() if not (GAME / name).is_file() or digest(GAME / name) != value]
    audit = {"source": str(GAME / "blueprints.zip"), "kind": "shipped reference archive, not runtime object inspection", "areas": [], "narrow_mode_blueprints": []}
    area_names = ["World/Areas/Act_3_DemonsHerecy/DrezenCapital/DrezenCapital.jbp",
                  "World/Areas/Act_5_HeraldOfTheIvoryLabyrinth/ReturnToDrezen/ReturnToDrezen.jbp"]
    policy = (ROOT / f"src/{mod_id}/Policy.cs").read_text(encoding="utf-8")
    with zipfile.ZipFile(GAME / "blueprints.zip") as z:
        for name in area_names:
            data = json.loads(z.read(name))
            assert data["AssetId"] in policy
            audit["areas"].append({"name": name, "asset_id": data["AssetId"], "type": data["Data"]["$type"]})
        for name in z.namelist():
            if name.startswith("Units/NPC/Commoners/Act_3_DemonsHerecy/Drezen/Commoner") and name.endswith(".jbp"):
                data = json.loads(z.read(name)); assert data["AssetId"] in policy
                audit["narrow_mode_blueprints"].append({"name": name, "asset_id": data["AssetId"],
                    "component_types": [c.get("$type") for c in data["Data"]["Components"]]})
    assert len(audit["narrow_mode_blueprints"]) == 4
    (ROOT / "analysis/drezen-view-policy.json").write_text(json.dumps(audit, indent=2), encoding="utf-8")
    (ROOT / "analysis/deferred-npc-views-verification.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
