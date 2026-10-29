"""Migrate the two legacy feature Mods to one verified PerformanceImprove install."""
import hashlib
import json
import math
import re
import shutil
import xml.etree.ElementTree as ET
import zipfile
from datetime import datetime, timezone, timedelta
from pathlib import Path

import psutil

ROOT = Path(__file__).resolve().parents[1]
GAME = Path(r"D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure").resolve()
LEGACY = ("WrathLifecycleFix", "WrathDeferredNpcViews")
NEW = "PerformanceImprove"


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def stopped():
    assert not any((p.info["name"] or "").lower() == "wrath.exe" for p in psutil.process_iter(["name"])), "Exit the game before migration"


def dlls():
    return {str(p.relative_to(GAME)): digest(p) for folder in (GAME / "Mods", GAME / "Wrath_Data/Managed") for p in sorted(folder.rglob("*.dll"))}


def main():
    stopped()
    mods = (GAME / "Mods").resolve()
    target = (mods / NEW).resolve()
    assert target.parent == mods and not target.exists(), "Existing new install requires an update procedure"
    legacy_paths = [(mods / name).resolve() for name in LEGACY if (mods / name).exists()]
    assert legacy_paths, "No legacy installation found to migrate"
    for path in legacy_paths:
        assert path.parent == mods and path.name in LEGACY
        assert json.loads((path / "Info.json").read_text(encoding="utf-8-sig"))["Id"] == path.name
    archive = ROOT / "dist/PerformanceImprove-0.2.0.zip"
    archive_hash = digest(archive)
    binary_path = ROOT / "src/PerformanceImprove/bin/Release/net481/PerformanceImprove.dll"
    binary_hash = digest(binary_path)
    assert digest(GAME / "Wrath_Data/Managed/Assembly-CSharp.dll") == "2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953"
    checks = {}
    for name in ("WrathLifecycleFix.Checks", "WrathDeferredNpcViews.Checks", "PerformanceImprove.Checks"):
        text = (ROOT / f"analysis/performance-improve-{name}.txt").read_text(encoding="utf-8-sig")
        count = len(re.findall(r"^PASS ", text, re.M))
        assert f"{count} checks passed" in text or f"Checks passed: {count}" in text
        checks[name] = count
        folder = "bin/Release/net481" if name == "PerformanceImprove.Checks" else "bin/PerformanceImprove/Release/net481"
        assert digest(ROOT / f"tests/{name}/{folder}/PerformanceImprove.dll") == binary_hash, "Checks used a stale binary"
    assert sum(checks.values()) == 105
    params = GAME / "Wrath_Data/Managed/UnityModManager/Params.xml"
    original = params.read_bytes()
    original_xml = ET.fromstring(original)
    assert original_xml.find(f"ModParams/Mod[@Id='{NEW}']") is None
    original_mods = {node.get("Id"): ET.tostring(node).strip() for node in original_xml.find("ModParams")}
    updated = original
    for name in LEGACY:
        pattern = rb'\s*<Mod Id="' + name.encode() + rb'"\s+Enabled="(?:true|false)">.*?</Mod>'
        updated, count = re.subn(pattern, b"", updated, flags=re.S)
        assert count <= 1, "Ambiguous UMM legacy entries"
    newline = b"\r\n" if b"\r\n" in original else b"\n"
    marker = b"  </ModParams>"
    assert updated.count(marker) == 1
    block = newline.join([b'    <Mod Id="PerformanceImprove" Enabled="true">', b'      <Hotkey>', b'        <keyCode>None</keyCode>',
                          b'        <modifiers>0</modifiers>', b'      </Hotkey>', b'    </Mod>', b''])
    updated = updated.replace(marker, block + marker)
    updated_xml = ET.fromstring(updated)
    for node in updated_xml.find("ModParams"):
        if node.get("Id") != NEW:
            assert ET.tostring(node).strip() == original_mods[node.get("Id")], "Unrelated UMM settings changed"
    configuration = ET.Element("Configuration")
    enabled_legacy = {n.get("Id"): n.get("Enabled") != "false" for n in original_xml.find("ModParams")}
    for key, value in (("LifecycleCleanup", enabled_legacy.get(LEGACY[0], True)), ("DeferredNpcViews", enabled_legacy.get(LEGACY[1], True))):
        ET.SubElement(configuration, key).text = str(value).lower()
    npc = ET.SubElement(configuration, "Npc")
    settings = {"ProfileOnly": "false", "AllNonPartyNpcs": "true", "PrewarmRadius": "45"}
    legacy_settings = mods / LEGACY[1] / "Settings.xml"
    if legacy_settings.exists():
        old = ET.parse(legacy_settings).getroot()
        for key in settings:
            value = old.findtext(key)
            if value is not None:
                if key == "PrewarmRadius":
                    number = float(value); settings[key] = str(number if math.isfinite(number) else 45)
                else:
                    assert value in ("true", "false"); settings[key] = value
    for key, value in settings.items(): ET.SubElement(npc, key).text = value
    ET.indent(configuration, space="  ")
    settings_bytes = ET.tostring(configuration, encoding="utf-8", xml_declaration=True)
    stamp = datetime.now(timezone(timedelta(hours=8))).strftime("%Y%m%d-%H%M%S")
    session = ROOT / "analysis/installations" / (stamp + "-performance-improve")
    session.mkdir(parents=True, exist_ok=False)
    before = session / "before"; before.mkdir()
    assert before.resolve().is_relative_to((ROOT / "analysis/installations").resolve())
    shutil.copy2(params, before / "UMM-Params.xml")
    assert (before / "UMM-Params.xml").read_bytes() == original
    inventory = dlls()
    (session / "dll-inventory-before.json").write_text(json.dumps(inventory, indent=2), encoding="utf-8")
    with zipfile.ZipFile(archive) as z:
        entries = {n.replace("\\", "/"): n for n in z.namelist()}
        assert set(entries) == {f"{NEW}/Info.json", f"{NEW}/README.md", f"{NEW}/{NEW}.dll"}
        assert hashlib.sha256(z.read(entries[f"{NEW}/{NEW}.dll"])).hexdigest() == binary_hash
        assert z.read(entries[f"{NEW}/README.md"]) == (ROOT / "docs/performance-improve.md").read_bytes()
        z.extractall(session / "staging")
    (session / "staging" / NEW / "Settings.xml").write_bytes(settings_bytes)
    moved = []
    stopped()
    try:
        # All resolved move targets were checked above; sources are only the two explicitly named legacy directories.
        for source in legacy_paths:
            destination = (before / source.name).resolve()
            assert source.parent == mods and destination.is_relative_to(before.resolve()) and not destination.exists()
            manifest = {str(p.relative_to(source)): digest(p) for p in source.rglob("*") if p.is_file()}
            shutil.move(str(source), str(destination))
            moved.append((source, destination))
            assert all(digest(destination / name) == value for name, value in manifest.items())
        shutil.copytree(session / "staging" / NEW, target)
        params.write_bytes(updated)
        assert params.read_bytes() == updated and digest(target / f"{NEW}.dll") == binary_hash
        after = dlls()
        retired = {name for name in inventory if any(name.startswith(f"Mods\\{old}\\") for old in LEGACY)}
        changes = [name for name, value in inventory.items() if name not in retired and after.get(name) != value]
        assert changes == [], f"Unrelated DLL changes: {changes}"
        assert set(inventory) - set(after) == retired
        assert set(after) - set(inventory) == {f"Mods\\{NEW}\\{NEW}.dll"}
    except Exception:
        if target.exists():
            assert target.resolve().parent == mods
            shutil.move(str(target), str(session / "failed-new-install"))
        for source, destination in reversed(moved):
            assert source.parent == mods and destination.is_relative_to(before.resolve())
            if not source.exists(): shutil.move(str(destination), str(source))
        if params.read_bytes() == updated: params.write_bytes(original)
        raise
    report = {"installed_at": datetime.now(timezone(timedelta(hours=8))).isoformat(), "mod_id": NEW, "version": "0.2.0",
              "installed_to": str(target), "archive_sha256": archive_hash, "dll_sha256": binary_hash,
              "checks": checks, "checks_total": sum(checks.values()), "umm_enabled": True,
              "configuration": {**{n.tag: n.text for n in configuration if n.tag != "Npc"}, "Npc": settings},
              "retired_mods": [source.name for source, _ in moved], "backup_folder": str(before),
              "existing_dlls_checked": len(inventory), "unrelated_dlls_changed": changes,
              "deployed_to_game": True, "game_started": False, "game_runtime_tested": False,
              "rollback": "With game stopped, preserve any new PerformanceImprove logs/settings, move its directory out of Mods, restore retired directories from backup_folder, and restore UMM-Params.xml to the original UMM path. Do not overwrite later user changes blindly."}
    (session / "installation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    (ROOT / "analysis/installations/latest-performance-improve.txt").write_text(str(session), encoding="utf-8")
    (ROOT / "analysis/performance-improve-verification.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
