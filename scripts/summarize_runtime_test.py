"""Read live/saved diagnostic CSVs without modifying the game's state."""
import csv
import json
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LOGS = Path(r'D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure\Mods\WrathPerformanceLab\Logs')


def rows(path):
    if not path.is_file():
        return []
    with path.open(encoding='utf-8-sig', errors='replace', newline='') as stream:
        return list(csv.DictReader(stream))


def main():
    session = Path((ROOT / 'analysis/runtime/current-session.txt').read_text(encoding='utf-8'))
    result = {'session': str(session), 'diagnostic_sessions': [], 'processes': []}
    for folder in sorted(LOGS.iterdir()):
        patch_file = folder / 'patches.txt'
        patches = patch_file.read_text(encoding='utf-8', errors='replace') if patch_file.exists() else ''
        memory = rows(folder / 'memory.csv')
        timing = rows(folder / 'timing.csv')
        loading = rows(folder / 'loading.csv')
        diagnostics = rows(folder / 'diagnostics.csv')
        graphics = rows(folder / 'graphics.csv')
        aggregates = defaultdict(lambda: {'calls': 0, 'inclusive_total_ms': 0, 'max_ms': 0, 'exceptions': 0})
        for row in timing:
            if not row.get('method') or row.get('exceptions_in_window') is None:
                continue  # Ignore an incomplete final row while a writer is active.
            stats = aggregates[row['method']]
            stats['calls'] += int(row['calls_in_window'])
            stats['inclusive_total_ms'] += float(row['inclusive_total_ms'])
            stats['max_ms'] = max(stats['max_ms'], float(row['inclusive_max_ms']))
            stats['exceptions'] += int(row['exceptions_in_window'])
        latest = defaultdict(dict)
        latest_group_time = {}
        for row in diagnostics:
            if row.get('status') is not None:
                if latest_group_time.get(row['group']) != row['elapsed_s']:
                    latest[row['group']] = {}
                    latest_group_time[row['group']] = row['elapsed_s']
                latest[row['group']][row['metric']] = {'value': row['value'], 'status': row['status'], 'elapsed_s': row['elapsed_s']}
        result['diagnostic_sessions'].append({
            'name': folder.name, 'rows': {'memory': len(memory), 'timing': len(timing), 'loading': len(loading),
                                         'diagnostics': len(diagnostics), 'graphics': len(graphics)},
            'extended_probes': [line for line in patches.splitlines() if line.startswith('EXTENDED')],
            'missing_probes': [line for line in patches.splitlines() if 'MISSING' in line or 'SKIPPED' in line],
            'first_memory': memory[0] if memory else None, 'last_memory': memory[-1] if memory else None,
            'loading_events': loading, 'latest_metrics': latest,
            'timing_by_inclusive_cost': sorted(({'method': k, **v} for k, v in aggregates.items()), key=lambda x: x['inclusive_total_ms'], reverse=True),
            'graphics_failure_rows': [r for r in graphics if r.get('exception_type') or r.get('worker_observed_state') == 'failed'][:50]})
    for path in sorted(session.glob('process*.csv')):
        samples = rows(path)
        if samples:
            result['processes'].append({'file': path.name, 'samples': len(samples), 'first': samples[0], 'last': samples[-1],
                                        'peak_private_bytes': max(int(r['private_bytes']) for r in samples),
                                        'peak_working_set_bytes': max(int(r['working_set_bytes']) for r in samples)})
    (session / 'summary.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'sessions': [{'name': s['name'], 'rows': s['rows'], 'missing_probes': s['missing_probes']} for s in result['diagnostic_sessions']],
                      'processes': result['processes'], 'summary': str(session / 'summary.json')}, ensure_ascii=True, indent=2))


if __name__ == '__main__':
    main()
