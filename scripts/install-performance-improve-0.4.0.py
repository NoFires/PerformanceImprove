"""Install PerformanceImprove 0.4.0 over the existing installation.

The diagnostics that used to live in WrathPerformanceLab are now part of PerformanceImprove, so the
old Lab folder is moved out of the game directory. Everything is backed up first; the install
refuses to run while the game is running and re-verifies every step.
"""
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
VERSION = "0.4.0"
PREVIOUS = "0.3.0"
FILES = ("Info.json", "PerformanceImprove.dll", "README.md", "merchant-sale-cleanup.md")


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def game_running():
    return any((p.info["name"] or "").lower() in ("wrath.exe", "pathfinder.exe") for p in psutil.process_iter(["name"]))


def inventory():
    return {
        str(p.relative_to(GAME)): digest(p)
        for folder in (GAME / "Mods", GAME / "Wrath_Data/Managed")
        for p in sorted(folder.rglob("*.dll"))
    }


def read_text(path):
    data = path.read_bytes()
    for encoding in ("utf-8-sig", "utf-16", "utf-8"):
        try:
            return data.decode(encoding)
        except UnicodeDecodeError:
            continue
    return data.decode("latin-1")


def check_counts():
    checks = {}
    for name in ("WrathLifecycleFix.Checks", "WrathDeferredNpcViews.Checks", "PerformanceImprove.Checks",
                 "PerformanceImprove.MerchantChecks", "PerformanceImprove.DiagnosticsChecks"):
        text = read_text(ROOT / f"analysis/performance-improve-{name}.txt")
        count = len(re.findall(r"^PASS ", text, re.M))
        assert f"{count} checks passed" in text or f"Checks passed: {count}" in text or f"ALL {count} CHECKS PASSED" in text
        checks[name] = count
    return checks


def main():
    assert not game_running(), "Exit the game before installing"
    target = (GAME / "Mods/PerformanceImprove").resolve()
    lab = (GAME / "Mods/WrathPerformanceLab").resolve()
    umm = GAME / "Wrath_Data/Managed/UnityModManager/Params.xml"
    assert target.parent == (GAME / "Mods").resolve() and target.is_dir()
    info = json.loads((ROOT / "src/PerformanceImprove/Info.json").read_text(encoding="utf-8"))
    old = json.loads((target / "Info.json").read_text(encoding="utf-8"))
    assert old["Id"] == info["Id"] == "PerformanceImprove"
    assert old["Version"] == PREVIOUS and info["Version"] == VERSION, (old["Version"], info["Version"])

    binary = ROOT / "src/PerformanceImprove/bin/Release/net481/PerformanceImprove.dll"
    binary_hash = digest(binary)
    archive = ROOT / f"dist/PerformanceImprove-{VERSION}.zip"
    checks = check_counts()
    assert sum(checks.values()) == 176, checks
    game_hash = digest(GAME / "Wrath_Data/Managed/Assembly-CSharp.dll")
    assert game_hash == "2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953"

    stamp = datetime.now(timezone(timedelta(hours=8))).strftime("%Y%m%d-%H%M%S")
    session = ROOT / "analysis/installations" / f"{stamp}-performance-improve-{VERSION}"
    session.mkdir(parents=True, exist_ok=False)
    before = session / "before"
    before.mkdir()
    shutil.copytree(target, before / "PerformanceImprove")
    if lab.is_dir():
        shutil.copytree(lab, before / "WrathPerformanceLab")
    shutil.copy2(umm, before / "UMM-Params.xml")
    before_dlls = inventory()
    (session / "dll-inventory-before.json").write_text(json.dumps(before_dlls, indent=2), encoding="utf-8")
    saves = Path.home() / "AppData/LocalLow/Owlcat Games/Pathfinder Wrath Of The Righteous/Saved Games"
    save_hashes = {str(p.relative_to(saves)): digest(p) for p in sorted(saves.rglob("*")) if p.is_file()}

    with zipfile.ZipFile(archive) as z:
        entries = {n.replace("\\", "/"): n for n in z.namelist()}
        assert set(entries) == {f"PerformanceImprove/{name}" for name in FILES}, sorted(entries)
        assert hashlib.sha256(z.read(entries["PerformanceImprove/PerformanceImprove.dll"])).hexdigest() == binary_hash
        assert z.read(entries["PerformanceImprove/README.md"]) == (ROOT / "docs/performance-improve.md").read_bytes()
        z.extractall(session / "staging")

    config = target / "Settings.xml"
    original_config = config.read_bytes() if config.exists() else b""
    umm_text = umm.read_text(encoding="utf-8-sig")
    assert 'Id="PerformanceImprove"' in umm_text
    updated_umm = re.sub(r'\s*<Mod Id="WrathPerformanceLab".*?</Mod>', "", umm_text, flags=re.S)
    assert 'Id="WrathPerformanceLab"' not in updated_umm
    updated_umm = updated_umm.replace('<Mod Id="PerformanceImprove" Enabled="false">',
                                      '<Mod Id="PerformanceImprove" Enabled="true">')
    assert re.search(r'<Mod Id="PerformanceImprove" Enabled="true">', updated_umm)

    assert not game_running(), "Game started during installation"
    try:
        for name in FILES:
            shutil.copy2(session / "staging/PerformanceImprove" / name, target / name)
        if lab.is_dir():
            shutil.rmtree(lab)
        umm.write_text(updated_umm, encoding="utf-8-sig")
        assert digest(target / "PerformanceImprove.dll") == binary_hash
        assert json.loads((target / "Info.json").read_text(encoding="utf-8"))["Version"] == VERSION
        assert not lab.exists()
        assert config.exists() and config.read_bytes() == original_config, "Settings.xml must be preserved"
        after_dlls = inventory()
        changed = [name for name in before_dlls if name in after_dlls and before_dlls[name] != after_dlls[name]]
        removed = [name for name in before_dlls if name not in after_dlls]
        added = [name for name in after_dlls if name not in before_dlls]
        assert changed == [r"Mods\PerformanceImprove\PerformanceImprove.dll"], changed
        assert all(name.startswith("Mods\\WrathPerformanceLab\\") for name in removed) and added == [], (removed, added)
        assert all(digest(saves / name) == value for name, value in save_hashes.items()), "Saves must not change"
    except Exception:
        for name in FILES:
            backup = before / "PerformanceImprove" / name
            if backup.exists():
                shutil.copy2(backup, target / name)
        if (before / "WrathPerformanceLab").is_dir() and not lab.exists():
            shutil.copytree(before / "WrathPerformanceLab", lab)
        umm.write_bytes((before / "UMM-Params.xml").read_bytes())
        raise

    report = {
        "installed_at": datetime.now(timezone(timedelta(hours=8))).isoformat(),
        "mod_id": info["Id"], "version": VERSION, "previous_version": PREVIOUS,
        "installed_to": str(target), "archive_sha256": digest(archive), "dll_sha256": binary_hash,
        "game_sha256": game_hash, "checks": checks, "checks_total": sum(checks.values()),
        "deployed_to_game": True, "game_started": False, "game_runtime_tested": False,
        "backup_folder": str(before), "configuration_preserved": True,
        "saves_hashed": len(save_hashes), "save_files_changed": False,
        "save_backup_note": "0.4.0 adds observation only; existing 0.3.0 save backup was reused, saves were hashed before and after instead of copied again",
        "wratperformance_lab_removed": not lab.exists(),
        "lab_function_moved_to": "PerformanceImprove.Diagnostics",
        "existing_dlls_checked": len(before_dlls), "changed_dlls": changed,
        "removed_dlls": removed, "unrelated_dlls_changed": [],
        "new_settings": {"Diagnostics": True, "SpellbookCopyFast": False, "DiagnosticsSampleSeconds": 15, "DiagnosticsMaxMegabytes": 32},
        "log_location": str(target / "Logs" / "perf-<timestamp>.jsonl"),
        "analyzer": "python scripts/analyze_performance_log.py <log.jsonl> --out report.md",
        "rollback": "Exit game; copy before/PerformanceImprove back to Mods/PerformanceImprove, copy before/WrathPerformanceLab back to Mods/WrathPerformanceLab, restore before/UMM-Params.xml. Settings.xml was preserved.",
    }
    (session / "installation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    (ROOT / "analysis/installations/latest-performance-improve.txt").write_text(str(session), encoding="utf-8")
    (ROOT / "analysis/performance-improve-verification.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
