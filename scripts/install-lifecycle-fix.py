"""Install the verified local package while the game is stopped; retain rollback data."""
import hashlib
import json
import shutil
import xml.etree.ElementTree as ET
import zipfile
from datetime import datetime, timezone, timedelta
from pathlib import Path

import psutil

ROOT = Path(__file__).resolve().parents[1]
GAME = Path(r"D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure")
MOD_ID = "WrathLifecycleFix"


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def assert_game_stopped():
    for process in psutil.process_iter(["pid", "name"]):
        if (process.info["name"] or "").lower() == "wrath.exe":
            raise RuntimeError(f"Exit the game before installing (PID {process.pid})")


def dll_inventory():
    return {
        str(path.relative_to(GAME)): digest(path)
        for folder in (GAME / "Mods", GAME / "Wrath_Data/Managed")
        for path in sorted(folder.rglob("*.dll"))
    }


def main():
    assert_game_stopped()
    verification = json.loads((ROOT / "analysis/lifecycle-fix-verification.json").read_text(encoding="utf-8"))
    archive = ROOT / "dist/WrathLifecycleFix-0.1.0.zip"
    assert digest(archive) == verification["archive_sha256"], "Package hash changed"
    assembly = GAME / "Wrath_Data/Managed/Assembly-CSharp.dll"
    assert digest(assembly) == verification["supported_game_sha256"], "Unsupported game assembly"
    target = GAME / "Mods" / MOD_ID
    assert not target.exists(), "Existing install requires explicit update handling"
    params = GAME / "Wrath_Data/Managed/UnityModManager/Params.xml"
    original_params = params.read_bytes()
    xml = ET.fromstring(original_params)
    assert xml.find("ModParams") is not None
    assert xml.find(f"ModParams/Mod[@Id='{MOD_ID}']") is None, "Unexpected existing settings"
    newline = b"\r\n" if b"\r\n" in original_params else b"\n"
    marker = b"  </ModParams>"
    assert original_params.count(marker) == 1
    entry = newline.join([
        b'    <Mod Id="WrathLifecycleFix" Enabled="true">',
        b'      <Hotkey>', b'        <keyCode>None</keyCode>',
        b'        <modifiers>0</modifiers>', b'      </Hotkey>', b'    </Mod>', b''
    ])
    updated_params = original_params.replace(marker, entry + marker)
    updated_xml = ET.fromstring(updated_params)
    assert updated_xml.find(f"ModParams/Mod[@Id='{MOD_ID}']").get("Enabled") == "true"
    assert updated_params.replace(entry, b"", 1) == original_params, "Unrelated settings changed"
    stamp = datetime.now(timezone(timedelta(hours=8))).strftime("%Y%m%d-%H%M%S")
    session = ROOT / "analysis/installations" / (stamp + "-lifecycle-fix")
    session.mkdir(parents=True, exist_ok=False)
    before = dll_inventory()
    (session / "dll-inventory-before.json").write_text(json.dumps(before, indent=2), encoding="utf-8")
    backed_up = []
    for source in sorted((GAME / "Mods").rglob("*")):
        if source.is_file() and source.suffix.lower() in {".json", ".xml", ".cfg", ".ini"} and "logs" not in [p.lower() for p in source.relative_to(GAME / "Mods").parts]:
            backup = session / "before" / source.relative_to(GAME)
            backup.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, backup)
            assert digest(source) == digest(backup)
            backed_up.append(str(source.relative_to(GAME)))
    backup_params = session / "before/UMM-Params.xml"
    shutil.copy2(params, backup_params)
    assert backup_params.read_bytes() == original_params
    expected_names = {f"{MOD_ID}/Info.json", f"{MOD_ID}/README.md", f"{MOD_ID}/WrathLifecycleFix.dll"}
    with zipfile.ZipFile(archive) as package:
        assert set(package.namelist()) == expected_names, "Unexpected archive entries"
        package.extractall(session / "staging")
    staging = session / "staging" / MOD_ID
    assert digest(staging / "WrathLifecycleFix.dll") == verification["dll_sha256"]
    info = json.loads((staging / "Info.json").read_text(encoding="utf-8-sig"))
    assert info["Id"] == MOD_ID and info["Version"] == "0.1.0"
    assert_game_stopped()
    shutil.copytree(staging, target)
    for source in staging.iterdir():
        assert digest(source) == digest(target / source.name)
    params.write_bytes(updated_params)
    assert params.read_bytes() == updated_params
    after = dll_inventory()
    changed = [name for name, value in before.items() if after.get(name) != value]
    added = sorted(set(after) - set(before))
    assert changed == [], f"Existing DLLs changed: {changed}"
    assert added == [f"Mods\\{MOD_ID}\\WrathLifecycleFix.dll"]
    report = {
        "installed_at": datetime.now(timezone(timedelta(hours=8))).isoformat(),
        "version": info["Version"], "installed_to": str(target),
        "dll_sha256": digest(target / "WrathLifecycleFix.dll"),
        "archive_sha256": digest(archive), "game_sha256": digest(assembly),
        "umm_enabled": True, "backup_folder": str(session / "before"),
        "config_files_backed_up": len(backed_up) + 1,
        "existing_dlls_checked": len(before), "existing_dlls_changed": changed,
        "new_dlls": added, "game_started": False, "game_runtime_tested": False,
        "rollback": f"With game stopped, remove {target} and restore {backup_params} to {params}",
    }
    (session / "installation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    (ROOT / "analysis/installations/latest-lifecycle-fix.txt").write_text(str(session), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
