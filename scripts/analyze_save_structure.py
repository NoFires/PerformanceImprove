"""Read-only save ZIP metadata and structural counts; never rewrite or clean saves."""
import json
import hashlib
import re
import zipfile
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
GAME = Path(r'D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure')
SAVES = Path.home() / 'AppData/LocalLow/Owlcat Games/Pathfinder Wrath Of The Righteous/Saved Games'


def main():
    files = sorted(SAVES.glob('*.zks'), key=lambda p: p.stat().st_mtime, reverse=True)
    reports = []
    # ZIP central directory only for all saves. Selected small JSON files are parsed separately.
    for path in files:
        with zipfile.ZipFile(path) as archive:
            infos = archive.infolist()
            entries = {i.filename: i for i in infos}
            drezen = [i for i in infos if 'DrezenCapital' in i.filename or i.filename.startswith('2570015799edf594daf2f076f2f975d8')]
            report = {'file': path.name, 'modified_local': datetime.fromtimestamp(path.stat().st_mtime).isoformat(),
                      'compressed_bytes': path.stat().st_size, 'uncompressed_bytes': sum(i.file_size for i in infos),
                      'entries': len(infos), 'history_bytes': entries['history'].file_size if 'history' in entries else 0,
                      'party_json_bytes': entries['party.json'].file_size if 'party.json' in entries else 0,
                      'player_json_bytes': entries['player.json'].file_size if 'player.json' in entries else 0,
                      'drezen_entries': [{'name': i.filename, 'bytes': i.file_size} for i in drezen],
                      'largest_entries': [{'name': i.filename, 'bytes': i.file_size} for i in sorted(infos, key=lambda i: i.file_size, reverse=True)[:8]]}
            if 'header.json' in entries:
                header = json.loads(archive.read('header.json'))
                report['campaign_fingerprint'] = hashlib.sha256(str(header.get('GameId', '')).encode()).hexdigest()[:12]
            if len(reports) < 3:
                key = 'header.json.VisualAdjustments2.json'
                if key in entries:
                    data = json.loads(archive.read(key))
                    records = data.get('PerCharacter', {})
                    report['visual_adjustments'] = {'json_bytes': entries[key].file_size, 'per_character_records': len(records),
                                                    'field_names_example': list(next(iter(records.values()), {}).keys())}
                # Entity ID membership is evidence of presence in serialized game files only;
                # absence does not establish that a settings entry is invalid or safe to delete.
                if 'visual_adjustments' in report:
                    ids = set(records)
                    present = set()
                    for info in infos:
                        if info.filename.endswith('.json') and 'VisualAdjustments2' not in info.filename and info.file_size < 40_000_000:
                            text = archive.read(info).decode('utf-8-sig', errors='replace')
                            # Unit IDs are often dashed 36-character GUIDs, not blueprint GUIDs.
                            present.update(ids.intersection(re.findall(r'"([^"\\\r\n]{1,100})"', text)))
                    report['visual_adjustments']['record_ids_found_in_other_json'] = len(present)
                    report['visual_adjustments']['record_ids_not_found_in_other_json'] = len(ids - present)
                unit_ids = set()
                drezen_units = 0
                for info in infos:
                    if not info.filename.endswith('.json') or 'VisualAdjustments2' in info.filename or info.file_size > 40_000_000:
                        continue
                    try:
                        data = json.loads(archive.read(info))
                    except (ValueError, UnicodeError):
                        continue
                    stack = [data]
                    while stack:
                        item = stack.pop()
                        if isinstance(item, dict):
                            if 'UnitEntityData,' in item.get('$type', ''):
                                unit_ids.add(item.get('UniqueId'))
                                if info in drezen:
                                    drezen_units += 1
                            stack.extend(item.values())
                        elif isinstance(item, list):
                            stack.extend(item)
                unit_ids.discard(None)
                report['serialized_unit_ids'] = len(unit_ids)
                report['drezen_serialized_unit_definitions'] = drezen_units
                if 'visual_adjustments' in report:
                    report['visual_adjustments']['record_ids_matching_unit_definitions'] = len(ids & unit_ids)
            reports.append(report)
    # Identify Drezen area blueprints from the shipped reference archive. Do not dump story content.
    blueprints = []
    with zipfile.ZipFile(GAME / 'blueprints.zip') as archive:
        names = [name for name in archive.namelist() if name.endswith('.jbp') and
                 re.search(r'/DrezenCapital(?:/|[^/]*\.jbp$)', name) and '/Areas/' in name]
        for name in names:
            if not any(k in name.lower() for k in ('area', 'drezen')):
                continue
            # Only the area's own blueprint, not all nested units, dialog or etudes.
            if name.rsplit('/', 1)[-1] not in ('DrezenCapital.jbp', 'DrezenCapital_Default.jbp'):
                continue
            try:
                data = json.loads(archive.read(name))
                fields = data.get('Data', {})
                selected = {k: v for k, v in fields.items() if k in ('m_DynamicScene', 'm_StaticScene', 'm_LightScene', 'm_Parts', 'IsSingleLightScene', 'm_SoundBankNames')}
                blueprints.append({'path': name, 'root_keys': list(data.keys()), 'data': {'AssetId': data.get('AssetId'), 'Data': selected}})
            except (ValueError, UnicodeError):
                pass
    result = {'notice': 'ZIP metadata and read-only structural inspection. Settings ID absence does not justify deletion.',
              'save_count': len(files), 'saves': reports, 'drezen_area_reference_blueprints': blueprints}
    (ROOT / 'analysis/save-structure.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    brief = [{k: r[k] for k in ('file', 'compressed_bytes', 'uncompressed_bytes', 'history_bytes')}
             | {'drezen_bytes': sum(i['bytes'] for i in r['drezen_entries']), 'visual_adjustments': r.get('visual_adjustments')}
             for r in reports[:3]]
    print(json.dumps({'saves': len(files), 'latest': brief, 'drezen_area_blueprints': [b['path'] for b in blueprints]}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
