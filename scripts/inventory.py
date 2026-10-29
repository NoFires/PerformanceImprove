"""Record installed game files without loading or executing game code."""
import hashlib
import json
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

GAME = Path(r'D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure')
ROOT = Path(__file__).resolve().parents[1]
MANAGED = GAME / 'Wrath_Data/Managed'


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def main():
    assemblies = [{'path': str(p.relative_to(GAME)), 'bytes': p.stat().st_size,
                   'sha256': digest(p)} for p in sorted(MANAGED.rglob('*.dll'))]
    mods = []
    for folder in sorted((GAME / 'Mods').iterdir()):
        if not folder.is_dir():
            continue
        info = next((p for p in folder.iterdir() if p.name.lower() == 'info.json'), None)
        item = {'folder': folder.name}
        if info:
            try:
                item.update(json.loads(info.read_text(encoding='utf-8-sig')))
            except (ValueError, UnicodeError) as exc:
                item['parse_error'] = str(exc)
        item['dlls'] = [{'name': str(p.relative_to(folder)), 'sha256': digest(p)}
                        for p in sorted(folder.rglob('*.dll'))]
        mods.append(item)
    log = MANAGED / 'UnityModManager/Log.txt'
    historical = [line for line in log.read_text(encoding='utf-8-sig', errors='replace').splitlines()[:15]] if log.exists() else []
    data = {'generated_utc': datetime.now(timezone.utc).isoformat(), 'game_root': str(GAME),
            'version_info': (GAME / 'Wrath_Data/StreamingAssets/Version.info').read_text(),
            'historical_log_header': historical, 'assemblies': assemblies, 'mods': mods}
    (ROOT / 'analysis/inventory.json').write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'managed_assemblies': len(assemblies), 'installed_mod_folders': len(mods),
                      'version': data['version_info'].strip()}, ensure_ascii=False))


if __name__ == '__main__':
    main()
