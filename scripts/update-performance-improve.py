"""Update the installed Mod after verified checks, preserving config and backing up saves."""
import hashlib
import json
import re
import shutil
import xml.etree.ElementTree as ET
import zipfile
from datetime import datetime, timezone, timedelta
from pathlib import Path

import psutil

ROOT = Path(__file__).resolve().parents[1]
GAME = Path(r"D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure").resolve()


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def stopped():
    assert not any((p.info["name"] or "").lower() == "wrath.exe" for p in psutil.process_iter(["name"])), "Exit the game before updating"


def inventory():
    return {str(p.relative_to(GAME)): digest(p) for folder in (GAME / "Mods", GAME / "Wrath_Data/Managed") for p in sorted(folder.rglob("*.dll"))}


def main():
    stopped()
    target = (GAME / "Mods/PerformanceImprove").resolve()
    assert target.parent == (GAME / "Mods").resolve() and target.is_dir()
    info = json.loads((ROOT / "src/PerformanceImprove/Info.json").read_text(encoding="utf-8"))
    old = json.loads((target / "Info.json").read_text(encoding="utf-8"))
    assert old["Id"] == info["Id"] == "PerformanceImprove" and old["Version"] == "0.2.0" and info["Version"] == "0.3.0"
    binary = ROOT / "src/PerformanceImprove/bin/Release/net481/PerformanceImprove.dll"
    binary_hash = digest(binary)
    archive = ROOT / f"dist/PerformanceImprove-{info['Version']}.zip"
    checks = {}
    for name in ("WrathLifecycleFix.Checks", "WrathDeferredNpcViews.Checks", "PerformanceImprove.Checks", "PerformanceImprove.MerchantChecks"):
        text = (ROOT / f"analysis/performance-improve-{name}.txt").read_text(encoding="utf-8-sig")
        count = len(re.findall(r"^PASS ", text, re.M))
        assert f"{count} checks passed" in text or f"Checks passed: {count}" in text
        checks[name] = count
        folder = "bin/Release/net481" if name.startswith("PerformanceImprove.") else "bin/PerformanceImprove/Release/net481"
        assert digest(ROOT / f"tests/{name}/{folder}/PerformanceImprove.dll") == binary_hash
    assert sum(checks.values()) == 145
    game_hash = digest(GAME / "Wrath_Data/Managed/Assembly-CSharp.dll")
    assert game_hash == "2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953"
    stamp = datetime.now(timezone(timedelta(hours=8))).strftime("%Y%m%d-%H%M%S")
    session = ROOT / "analysis/installations" / (stamp + "-performance-improve-0.3.0")
    session.mkdir(parents=True, exist_ok=False)
    before = session / "before"; before.mkdir()
    shutil.copytree(target, before / "PerformanceImprove")
    umm = GAME / "Wrath_Data/Managed/UnityModManager/Params.xml"
    shutil.copy2(umm, before / "UMM-Params.xml")
    before_dlls = inventory()
    (session / "dll-inventory-before.json").write_text(json.dumps(before_dlls, indent=2), encoding="utf-8")
    saves = Path.home() / "AppData/LocalLow/Owlcat Games/Pathfinder Wrath Of The Righteous/Saved Games"
    shutil.copytree(saves, before / "Saved Games")
    save_manifest = [{"name": str(p.relative_to(saves)), "sha256": digest(p), "bytes": p.stat().st_size} for p in sorted(saves.rglob("*")) if p.is_file()]
    assert all(digest(before / "Saved Games" / record["name"]) == record["sha256"] for record in save_manifest)
    (session / "save-backup-manifest.json").write_text(json.dumps(save_manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    with zipfile.ZipFile(archive) as z:
        entries = {n.replace("\\", "/"): n for n in z.namelist()}
        assert set(entries) == {"PerformanceImprove/Info.json", "PerformanceImprove/PerformanceImprove.dll", "PerformanceImprove/README.md", "PerformanceImprove/merchant-sale-cleanup.md"}
        assert hashlib.sha256(z.read(entries["PerformanceImprove/PerformanceImprove.dll"])).hexdigest() == binary_hash
        assert z.read(entries["PerformanceImprove/README.md"]) == (ROOT / "docs/performance-improve.md").read_bytes()
        z.extractall(session / "staging")
    config = target / "Settings.xml"
    original_config = config.read_bytes()
    xml = ET.fromstring(original_config)
    if xml.find("MerchantSaleCleanup") is None:
        newline = b"\r\n" if b"\r\n" in original_config else b"\n"
        insertion = b"  <MerchantSaleCleanup>true</MerchantSaleCleanup>" + newline
        marker = b"  <Npc>" if b"  <Npc>" in original_config else b"</Configuration>"
        assert original_config.count(marker) == 1
        updated_config = original_config.replace(marker, insertion + marker)
    else:
        updated_config = original_config
    updated_xml = ET.fromstring(updated_config)
    for node in xml:
        assert ET.tostring(node).strip() == ET.tostring(updated_xml.find(node.tag)).strip(), "Existing settings changed"
    stopped()
    try:
        for name in ("Info.json", "PerformanceImprove.dll", "README.md", "merchant-sale-cleanup.md"):
            shutil.copy2(session / "staging/PerformanceImprove" / name, target / name)
        config.write_bytes(updated_config)
        assert config.read_bytes() == updated_config
        after_dlls = inventory()
        changed = [name for name in before_dlls if before_dlls[name] != after_dlls.get(name)]
        assert changed == [r"Mods\PerformanceImprove\PerformanceImprove.dll"]
        assert set(before_dlls) == set(after_dlls)
        assert digest(target / "PerformanceImprove.dll") == binary_hash
        assert umm.read_bytes() == (before / "UMM-Params.xml").read_bytes()
        assert all(digest(saves / record["name"]) == record["sha256"] for record in save_manifest)
    except Exception:
        for name in ("Info.json", "PerformanceImprove.dll", "README.md", "Settings.xml"):
            shutil.copy2(before / "PerformanceImprove" / name, target / name)
        raise
    report = {"installed_at": datetime.now(timezone(timedelta(hours=8))).isoformat(), "mod_id": info["Id"], "version": info["Version"],
              "previous_version": old["Version"], "installed_to": str(target), "archive_sha256": digest(archive), "dll_sha256": binary_hash,
              "game_sha256": game_hash, "checks": checks, "checks_total": sum(checks.values()), "deployed_to_game": True,
              "game_started": False, "game_runtime_tested": False, "backup_folder": str(before),
              "saves_backed_up": len(save_manifest), "save_backup_bytes": sum(r["bytes"] for r in save_manifest), "save_files_changed": False,
              "configuration_preserved": True, "merchant_cleanup_enabled": updated_xml.findtext("MerchantSaleCleanup") == "true",
              "existing_dlls_checked": len(before_dlls), "changed_dlls": changed, "unrelated_dlls_changed": [],
              "scope": "Only completed player-sold stackable batches, before entering vendor inventory; protected and unknown items use native transfer",
              "rollback": "Exit game; restore the prior Mod and configuration from backup. If testing saved a cleanup you want to undo, restore the appropriate backed-up save separately; do not blindly overwrite later saves."}
    (session / "installation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    (ROOT / "analysis/installations/latest-performance-improve.txt").write_text(str(session), encoding="utf-8")
    (ROOT / "analysis/performance-improve-verification.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
