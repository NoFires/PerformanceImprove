"""Compare immutable start/end snapshots from the user-operated test segment."""
import csv
import hashlib
import json
from collections import defaultdict
from datetime import datetime, timezone, timedelta
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    with path.open(encoding='utf-8-sig', newline='') as stream:
        return list(csv.DictReader(stream))


def latest_groups(rows):
    result, times = {}, {}
    for row in rows:
        group = row['group']
        if times.get(group) != row['elapsed_s']:
            result[group] = {}
            times[group] = row['elapsed_s']
        result[group][row['metric']] = row['value']
    return result, times


def main():
    session = Path((ROOT / 'analysis/runtime/current-session.txt').read_text(encoding='utf-8'))
    recording = session / 'user-operation-recording'
    metadata = json.loads((recording / 'metadata.json').read_text(encoding='utf-8'))
    actions = [json.loads(line) for line in (session / 'actions.jsonl').read_text(encoding='utf-8').splitlines()]
    end_action = next(x for x in reversed(actions) if x['action'] == 'user_finished_operations')
    start_time = datetime.fromisoformat(metadata['start_utc'])
    end_time = datetime.fromisoformat(end_action['utc'])
    zone = timezone(timedelta(hours=8))
    samples = read(recording / 'samples.csv')
    snapshots = {boundary: {name: read(recording / boundary / (name + '.csv'))
                            for name in ('timing', 'memory', 'diagnostics', 'graphics', 'loading')}
                 for boundary in ('start', 'end')}
    timings = defaultdict(lambda: {'calls': 0, 'inclusive_total_ms': 0., 'inclusive_max_ms': 0., 'exceptions': 0})
    old, new = snapshots['start']['timing'], snapshots['end']['timing']
    assert new[:len(old)] == old, 'Timing file changed before its immutable prefix'
    for row in new[len(old):]:
        item = timings[row['method']]
        item['calls'] += int(row['calls_in_window'])
        item['inclusive_total_ms'] += float(row['inclusive_total_ms'])
        item['inclusive_max_ms'] = max(item['inclusive_max_ms'], float(row['inclusive_max_ms']))
        item['exceptions'] += int(row['exceptions_in_window'])
    method_rows = []
    for method, value in timings.items():
        method_rows.append({'method': method, **value, 'inclusive_mean_ms': value['inclusive_total_ms'] / value['calls']})
    method_rows.sort(key=lambda item: item['inclusive_total_ms'], reverse=True)
    metrics = {}
    group_times = {}
    for boundary in ('start', 'end'):
        metrics[boundary], group_times[boundary] = latest_groups(snapshots[boundary]['diagnostics'])
    resource_stats = {}
    for key in ('private_bytes', 'working_set_bytes', 'managed_estimate_bytes', 'system_available_ram_bytes',
                'fabric_nodes', 'stopped_controller_targets_found', 'controller_alive', 'preview_alive', 'va2_settings'):
        values = [float(row[key]) for row in samples if row.get(key) not in ('', None)]
        resource_stats[key] = {'first': values[0], 'last': values[-1], 'min': min(values), 'max': max(values)}
    graphics_before = snapshots['start']['graphics']
    graphics_after = snapshots['end']['graphics']
    assert graphics_after[:len(graphics_before)] == graphics_before
    graphics_delta = graphics_after[len(graphics_before):]
    clears = [float(r['observed_age_s']) for r in graphics_delta if r['event'] == 'before_clear']
    workers = [float(r['since_worker_start_s']) for r in graphics_delta if r['event'] == 'worker_end']
    copy = next(v for v in method_rows if ' CreatePreview(' in v['method'])
    spells = next(v for v in method_rows if ' CopySpellbook(' in v['method'])
    sampler_times = [float(r['value']) for r in snapshots['end']['diagnostics'][len(snapshots['start']['diagnostics']):]
                     if r['group'] == 'sampler' and r['metric'] == 'capture_ms']
    result = {
        'start_hong_kong': start_time.astimezone(zone).isoformat(), 'end_hong_kong': end_time.astimezone(zone).isoformat(),
        'user_operation_seconds': (end_time - start_time).total_seconds(), 'recorder_elapsed_seconds': metadata.get('elapsed_seconds'),
        'process_samples': len(samples), 'timing_window_note': '15-second inclusive aggregates: first/last windows may straddle the operator boundary; nested totals must not be summed.',
        'metric_window_note': 'Start/end counts are the nearest available asynchronous diagnostic samples, not synchronous operation-boundary reads.',
        'timing': method_rows, 'metrics': metrics, 'metric_elapsed_s': group_times,
        'resource_stats': resource_stats, 'gc2_counter_delta': int(samples[-1]['gc2_cumulative']) - int(samples[0]['gc2_cumulative']),
        'copy_spellbook_share_of_preview_percent': spells['inclusive_total_ms'] / copy['inclusive_total_ms'] * 100,
        'dxt': {'events_in_delta': len(graphics_delta), 'before_clear_events': len(clears), 'max_observed_age_at_clear_s': max(clears, default=None),
                'worker_end_events': len(workers), 'max_observed_worker_duration_s': max(workers, default=None),
                'exception_events': [r for r in graphics_delta if r['exception_type']]},
        'new_loading_rows': len(snapshots['end']['loading']) - len(snapshots['start']['loading']),
        'sampler_ms': {'max': max(sampler_times, default=None), 'mean': sum(sampler_times) / len(sampler_times) if sampler_times else None},
        'invalid_fields_in_mod_memory_csv': ['private_bytes=0', 'working_set_bytes=0', 'global_subscriber_slots=-1', 'unit_subscriber_groups=-1'],
        'process_memory_source': 'Out-of-process psutil samples; Mono Process memory properties returned invalid zero values.',
        'hashes': {str(p.relative_to(recording)): hashlib.sha256(p.read_bytes()).hexdigest()
                   for boundary in ('start', 'end') for p in (recording / boundary).glob('*.csv')},
        'no_vanilla_comparison': True, 'oom_or_crash_reproduced': False, 'optimization_patch_deployed': False}
    (recording / 'analysis.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    try:
        import os
        os.environ.setdefault('MPLCONFIGDIR', r'E:\Program\Caches\Matplotlib')
        import matplotlib
        matplotlib.use('Agg')
        import matplotlib.pyplot as plt
        minutes = [float(r['recording_elapsed_s']) / 60 for r in samples]
        fig, axes = plt.subplots(3, 1, figsize=(10, 8), sharex=True, constrained_layout=True)
        for field, label in [('private_bytes', 'Process private'), ('managed_estimate_bytes', 'Managed estimate')]:
            axes[0].plot(minutes, [float(r[field]) / 2**30 for r in samples], label=label)
        previous = samples[0]['gc2_cumulative']
        for x, row in zip(minutes, samples):
            if row['gc2_cumulative'] != previous:
                axes[0].axvline(x, color='gray', alpha=.35, linewidth=.8)
                previous = row['gc2_cumulative']
        axes[0].set_ylabel('GiB'); axes[0].legend(loc='upper left'); axes[0].set_title('User-operated respec test; gray lines mark GC counter changes')
        axes[1].plot(minutes, [float(r['stopped_controller_targets_found']) for r in samples], label='Stopped controllers reachable via LateUpdate')
        axes[1].plot(minutes, [float(r['preview_disposed_alive']) for r in samples], label='Disposed previews still weak-alive')
        axes[1].set_ylabel('Observed objects'); axes[1].legend(loc='upper left')
        baseline = float(samples[0]['va2_settings'])
        axes[2].plot(minutes, [float(r['va2_settings']) - baseline for r in samples], label='VA2 settings added')
        axes[2].plot(minutes, [float(r['dxt_queue']) for r in samples], label='DXT queue')
        axes[2].set_ylabel('Entries / requests'); axes[2].set_xlabel('Minutes since passive recording began'); axes[2].legend(loc='upper left')
        for axis in axes:
            axis.grid(alpha=.2)
        fig.savefig(recording / 'respec-trends.png', dpi=150)
        plt.close(fig)
    except ImportError:
        pass
    print(json.dumps({k: result[k] for k in ('user_operation_seconds', 'gc2_counter_delta', 'copy_spellbook_share_of_preview_percent', 'dxt', 'new_loading_rows', 'sampler_ms')}, indent=2))
    print(recording / 'analysis.json')


if __name__ == '__main__':
    main()
