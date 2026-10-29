"""Build searchable structure and historical evidence reports from local files."""
import json
import re
from collections import Counter
from pathlib import Path
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'analysis/decompiled'
GAME = Path(r'D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure')


def main():
    stats = []
    for folder in sorted(SOURCE.iterdir()):
        files = list(folder.rglob('*.cs'))
        namespaces = Counter()
        for path in files:
            match = re.search(r'^namespace ([^;{]+)', path.read_text(encoding='utf-8-sig', errors='replace'), re.M)
            if match:
                namespaces[match[1]] += 1
        projects = list(folder.glob('*.csproj'))
        references = []
        framework = None
        if projects:
            project = ElementTree.parse(projects[0])
            references = [e.attrib['Include'] for e in project.findall('.//Reference')]
            framework = project.findtext('.//TargetFramework')
        stats.append({'assembly': folder.name, 'source_files': len(files),
                      'decompiler_inferred_framework': framework, 'references': references,
                      'namespaces_by_file_count': dict(namespaces.most_common())})
    (ROOT / 'analysis/structure.json').write_text(json.dumps(stats, ensure_ascii=False, indent=2), encoding='utf-8')
    factory = SOURCE / 'Assembly-CSharp/Kingmaker.GameModes/GameModesFactory.cs'
    registrations = [{'line': n, 'registration': line.strip()} for n, line in enumerate(factory.read_text(encoding='utf-8-sig').splitlines(), 1)
                     if line.strip().startswith('Register(')]
    (ROOT / 'analysis/controller-registrations.json').write_text(json.dumps(registrations, ensure_ascii=False, indent=2), encoding='utf-8')
    log = GAME / 'Wrath_Data/Managed/UnityModManager/Log.txt'
    lines = log.read_text(encoding='utf-8-sig', errors='replace').splitlines()
    timer = [{'line': n, 'text': line} for n, line in enumerate(lines, 1) if line.startswith('[!!ModTimer]')]
    errors = Counter(re.sub(r'0x[0-9a-fA-F]+', '<address>', s)[:400] for s in lines if re.search(r'\[(?:Error|Exception)\]|Exception:|\[.*\]\s*\[Error\]', s, re.I))
    historical = {'source': str(log), 'source_last_write_local': __import__('datetime').datetime.fromtimestamp(log.stat().st_mtime).isoformat(),
                  'notice': 'Historical single-run evidence; not a fresh benchmark and not proof of current Mod activation.',
                  'timer_lines': timer, 'frequent_error_lines': errors.most_common(20)}
    (ROOT / 'analysis/historical-log-summary.json').write_text(json.dumps(historical, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'assemblies': [{k: s[k] for k in ('assembly', 'source_files')} for s in stats],
                      'controller_registrations': len(registrations), 'historical_timer_lines': len(timer)}, ensure_ascii=False))


if __name__ == '__main__':
    main()
