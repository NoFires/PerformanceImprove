"""Back up saves/logs and install only the local diagnostic Mod before launch."""
import hashlib
import json
import shutil
from datetime import datetime, timezone, timedelta
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
GAME = Path(r'D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure')
DATA = Path.home() / 'AppData/LocalLow/Owlcat Games/Pathfinder Wrath Of The Righteous'


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def main():
    stamp = datetime.now(timezone(timedelta(hours=8))).strftime('%Y%m%d-%H%M%S')
    session = ROOT / 'analysis/runtime' / stamp
    session.mkdir(parents=True, exist_ok=False)
    saves = DATA / 'Saved Games'
    # Copy all originals before the game can rotate quick/autosaves.
    shutil.copytree(saves, session / 'before/Saved Games')
    snapshot = []
    for source in saves.glob('*.zks'):
        backup = session / 'before/Saved Games' / source.name
        assert digest(source) == digest(backup)
        snapshot.append({'name': source.name, 'bytes': source.stat().st_size, 'sha256': digest(source)})
    for source in DATA.glob('*'):
        if source.is_file() and (source.suffix in ('.log', '.txt', '.json')):
            shutil.copy2(source, session / 'before' / source.name)
    umm = GAME / 'Wrath_Data/Managed/UnityModManager'
    for name in ('Params.xml', 'Log.txt'):
        if (umm / name).is_file():
            shutil.copy2(umm / name, session / 'before' / ('UMM-' + name))
    target = GAME / 'Mods/WrathPerformanceLab'
    if target.exists():
        shutil.copytree(target, session / 'before/WrathPerformanceLab')
    target.mkdir(exist_ok=True)
    for name in ('Info.json', 'WrathPerformanceLab.dll', 'README.md'):
        source = ROOT / 'dist/WrathPerformanceLab' / name
        shutil.copy2(source, target / name)
        assert digest(source) == digest(target / name)
    manifest = {'session': str(session), 'local_time': stamp, 'saves_backed_up': snapshot,
                'installed': str(target), 'version': json.loads((target / 'Info.json').read_text())['Version'],
                'dll_sha256': digest(target / 'WrathPerformanceLab.dll'), 'existing_mods_changed': False}
    (session / 'preparation.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    (ROOT / 'analysis/runtime/current-session.txt').write_text(str(session), encoding='utf-8')
    print(json.dumps({k: v for k, v in manifest.items() if k != 'saves_backed_up'}, indent=2))
    print('Verified save backups:', len(snapshot))


if __name__ == '__main__':
    main()
