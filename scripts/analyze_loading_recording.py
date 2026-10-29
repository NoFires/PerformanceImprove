"""Analyze immutable user-operated map-loading snapshots and native log milestones."""
import bisect
import csv
import hashlib
import json
import re
from collections import defaultdict, deque
from datetime import datetime, timedelta, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    with path.open(encoding='utf-8-sig', newline='') as stream:
        return list(csv.DictReader(stream))


def delta(folder, name):
    before, after = read(folder / 'start' / name), read(folder / 'end' / name)
    assert after[:len(before)] == before, f'Changed CSV prefix: {name}'
    return before, after, after[len(before):]


def latest(rows):
    values, times = {}, {}
    for row in rows:
        group = row['group']
        if times.get(group) != row['elapsed_s']:
            values[group] = {}
            times[group] = row['elapsed_s']
        values[group][row['metric']] = row['value']
    return values


def main():
    session = Path((ROOT / 'analysis/runtime/current-session.txt').read_text(encoding='utf-8'))
    folder = Path((session / 'current-recording.txt').read_text(encoding='utf-8'))
    metadata = json.loads((folder / 'metadata.json').read_text(encoding='utf-8'))
    assert metadata['segment_name'].startswith('drezen-loading-') and metadata.get('end_utc')
    samples = read(folder / 'samples.csv')
    before_loading, after_loading, events = delta(folder, 'loading.csv')
    assert events and events[-1]['area'] == 'DrezenCapital'
    start = float(events[0]['elapsed_s'])
    finish_event = next(row for row in reversed(events) if row['event'] == 'end' and 'AreaLoadingComplete' in row['stage'])
    finish = float(finish_event['elapsed_s'])
    total = finish - start
    patch_text = (folder / 'start/patches.txt').read_text(encoding='utf-8')
    origin = datetime.fromisoformat(re.search(r'session UTC ([^\r\n]+)', patch_text)[1].replace('Z', '+00:00'))
    zone = timezone(timedelta(hours=8))
    utc_start = origin + timedelta(seconds=start)
    utc_end = origin + timedelta(seconds=finish)
    pending, stages = defaultdict(deque), []
    for event in events:
        if event['event'] == 'start':
            pending[event['stage']].append(float(event['elapsed_s']))
        elif event['event'] in ('end', 'exception'):
            observed_start = pending[event['stage']].popleft() if pending[event['stage']] else None
            seconds = float(event['wall_ms_including_wait']) / 1000 if event['wall_ms_including_wait'] else None
            stages.append({'stage': event['stage'], 'start_elapsed_s': observed_start,
                           'end_elapsed_s': float(event['elapsed_s']), 'wall_seconds': seconds,
                           'area': event['area'], 'exception': event['exception_type']})
    aggregates = defaultdict(lambda: {'calls': 0, 'inclusive_total_ms': 0., 'inclusive_max_ms': 0., 'exceptions': 0})
    _, _, new_timings = delta(folder, 'timing.csv')
    for row in new_timings:
        value = aggregates[row['method']]
        value['calls'] += int(row['calls_in_window'])
        value['inclusive_total_ms'] += float(row['inclusive_total_ms'])
        value['inclusive_max_ms'] = max(value['inclusive_max_ms'], float(row['inclusive_max_ms']))
        value['exceptions'] += int(row['exceptions_in_window'])
    before_d, after_d, new_d = delta(folder, 'diagnostics.csv')
    before_g, after_g, new_g = delta(folder, 'graphics.csv')
    _, _, new_m = delta(folder, 'memory.csv')
    resource_stats = {}
    loading_resource_stats = {}
    loading_samples = [row for row in samples if utc_start <= datetime.fromisoformat(row['utc']) <= utc_end]
    for field in ('private_bytes', 'working_set_bytes', 'managed_estimate_bytes', 'system_available_ram_bytes'):
        values = [float(row[field]) for row in samples if row.get(field) not in ('', None)]
        resource_stats[field] = {'first': values[0], 'last': values[-1], 'min': min(values), 'max': max(values)}
        active_values = [float(row[field]) for row in loading_samples if row.get(field) not in ('', None)]
        loading_resource_stats[field] = {'min': min(active_values), 'max': max(active_values)}
    timestamps = [datetime.fromisoformat(row['utc']).timestamp() for row in samples]
    def interpolate(at, key):
        index = bisect.bisect_left(timestamps, at)
        if index == 0:
            return float(samples[0][key])
        if index == len(samples):
            return float(samples[-1][key])
        ratio = (at - timestamps[index - 1]) / (timestamps[index] - timestamps[index - 1])
        return float(samples[index - 1][key]) * (1 - ratio) + float(samples[index][key]) * ratio
    def process_window(first, last):
        values = {key: interpolate(last.timestamp(), key) - interpolate(first.timestamp(), key)
                  for key in ('cpu_total_s', 'read_bytes_cumulative', 'write_bytes_cumulative')}
        values['wall_seconds'] = (last - first).total_seconds()
        return values
    game_text = (folder / 'end/GameLogFull.txt').read_bytes().replace(b'\x00', b'').decode('utf-8', errors='replace')
    native_total_matches = list(re.finditer(r'\[(\d+(?:\.\d+)?) - System\]: Loading is ended in ([\d.Ee+-]+)s', game_text))
    native_total = next(match for match in reversed(native_total_matches) if abs(float(match[2]) - total) < .01)
    native_end = float(native_total[1])
    native_start = native_end - float(native_total[2])
    header = re.compile(r'^\[(\d+(?:\.\d+)?) - ([^]]+)\]: (.*)')
    native_rows = []
    for line in game_text.splitlines():
        match = header.match(line)
        if match and native_start - .01 <= float(match[1]) <= native_end + .01:
            native_rows.append({'elapsed_s': float(match[1]), 'category': match[2], 'message': match[3]})
    milestones = {}
    for key, phrase in [('scene_set_matched', 'Scene set matched'),
                        ('active_light_scene', 'Set active scene: DrezenCapital_Outdoor_Light'),
                        ('probe_wait_finished', 'Ended waiting for light probes baking.')]:
        milestones[key] = next(row for row in native_rows if phrase in row['message'])
    load_area = next(stage for stage in stages if '<LoadAreaCoroutine>' in stage['stage'])
    native_load_area_end = next(row for row in native_rows if "Loading process finished '<LoadAreaCoroutine>" in row['message'])
    offset = native_load_area_end['elapsed_s'] - load_area['end_elapsed_s']
    post_match_seconds = native_load_area_end['elapsed_s'] - milestones['scene_set_matched']['elapsed_s']
    probe_interval = milestones['probe_wait_finished']['elapsed_s'] - milestones['active_light_scene']['elapsed_s']
    durations = {
        'load_area': sum(x['wall_seconds'] for x in stages if '<LoadAreaCoroutine>' in x['stage']),
        'time_of_day_environment': sum(x['wall_seconds'] for x in stages if 'MatchTimeOfDayCoroutine' in x['stage']),
        'unused_asset_cleanup': sum(x['wall_seconds'] for x in stages if 'UnloadUnusedAssetsCoroutine' in x['stage']),
        'texture_wait': sum(x['wall_seconds'] for x in stages if 'AwaitTextureCompression' in x['stage']),
        'autosave': sum(x['wall_seconds'] for x in stages if 'SaveRoutine' in x['stage'])}
    durations['other'] = total - sum(durations.values())
    errors = [row for row in native_rows if 'Exception in subscriber' in row['message'] or 'failed' in row['message'].lower() or 'cannot open file' in row['message'].lower()]
    worker_ends = [row for row in new_g if row['event'] == 'worker_end']
    clears = [row for row in new_g if row['event'] == 'before_clear']
    captures = [float(row['value']) for row in new_d if row['group'] == 'sampler' and row['metric'] == 'capture_ms']
    result = {
        'segment': str(folder), 'samples': len(samples), 'loading_seconds': total,
        'loading_start_hong_kong_approx': utc_start.astimezone(zone).isoformat(),
        'loading_end_hong_kong_approx': utc_end.astimezone(zone).isoformat(),
        'clock_note': 'Wall-clock alignment uses the Mod log origin; origin creation and 1-second process sampling add alignment uncertainty.',
        'native_loading_seconds': float(native_total[2]), 'native_vs_probe_difference_seconds': total - float(native_total[2]),
        'stage_totals_seconds': durations, 'stages': stages,
        'method_timings': sorted(({'method': key, **value} for key, value in aggregates.items()), key=lambda x: x['inclusive_total_ms'], reverse=True),
        'metrics_before': latest(before_d), 'metrics_after': latest(after_d),
        'resource_stats_recording_window': resource_stats,
        'resource_stats_loading_window_samples': loading_resource_stats,
        'approx_process_counters_during_loading': process_window(utc_start, utc_end),
        'milestones_native_clock': milestones, 'native_minus_mod_clock_seconds': offset,
        'seconds_after_scene_set_matched': post_match_seconds,
        'seconds_active_light_to_probe_wait_finished': probe_interval,
        'dxt': {'workers_completed_in_segment': len(worker_ends), 'clear_events': len(clears),
                'max_observed_worker_seconds': max((float(row['since_worker_start_s']) for row in worker_ends), default=0),
                'max_observed_age_before_clear_seconds': max((float(row['observed_age_s']) for row in clears), default=0),
                'exception_events': [row for row in new_g if row['exception_type']],
                'queued_snapshots': len([row for row in new_g if row['event'] == 'queued_snapshot'])},
        'sampler_ms': {'max': max(captures), 'mean': sum(captures) / len(captures)},
        'partial_scan_rows': len([row for row in new_d if row['status'] == 'partial']),
        'capture_errors': [row for row in new_d if row['metric'] == 'capture_error'],
        'selected_error_log_headers': errors, 'native_log_rows_in_loading': native_rows,
        'oom_markers': {marker: len(re.findall(re.escape(marker), game_text, re.I))
                        for marker in ('OutOfMemoryException', 'Could not allocate memory', 'Light probes baking process takes too long!')},
        'input_csv_sha256': {str(p.relative_to(folder)): hashlib.sha256(p.read_bytes()).hexdigest()
                             for boundary in ('start', 'end') for p in (folder / boundary).glob('*.csv')}}
    (folder / 'analysis.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    import os
    os.environ.setdefault('MPLCONFIGDIR', r'E:\Program\Caches\Matplotlib')
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    fig, axes = plt.subplots(3, 1, figsize=(11, 9), constrained_layout=True, gridspec_kw={'height_ratios': [1, 1.6, 1.3]})
    names = ['Area load / restore', 'Time of day / environment', 'Unused assets (twice)', 'Texture wait', 'Autosave', 'Other']
    seconds = list(durations.values())
    axes[0].barh(names[::-1], seconds[::-1]); axes[0].set_xlabel('Wall seconds; stages include waits')
    axes[0].set_title(f'Drezen load: {total:.2f} s (single run with existing Mods)')
    for i, value in enumerate(seconds[::-1]):
        axes[0].text(value + .2, i, f'{value:.2f}', va='center', fontsize=9)
    x = [timestamp - utc_start.timestamp() for timestamp in timestamps]
    axes[1].plot(x, [float(row['private_bytes']) / 2**30 for row in samples], label='Process private')
    axes[1].plot(x, [float(row['working_set_bytes']) / 2**30 for row in samples], label='Working set')
    axes[1].plot(x, [float(row['managed_estimate_bytes']) / 2**30 for row in samples], label='Managed estimate (may be stale)')
    axes[1].set_ylabel('GiB'); axes[1].legend(loc='upper left')
    initial_reads = float(samples[0]['read_bytes_cumulative'])
    axes[2].plot(x, [(float(row['read_bytes_cumulative']) - initial_reads) / 2**30 for row in samples], label='Process read bytes since recording start')
    axes[2].set_ylabel('GiB read (includes cache)'); axes[2].legend(loc='upper left'); axes[2].set_xlabel('Seconds from first loading stage (negative = baseline)')
    for axis in axes[1:]:
        axis.axvspan(0, total, alpha=.12, color='orange', label='Loading')
        axis.axvline(0, color='gray', linewidth=.8); axis.axvline(total, color='gray', linewidth=.8); axis.grid(alpha=.2)
    axes[0].grid(axis='x', alpha=.2)
    fig.savefig(folder / 'drezen-loading.png', dpi=150); plt.close(fig)
    print(json.dumps({key: result[key] for key in ('loading_seconds', 'loading_start_hong_kong_approx', 'loading_end_hong_kong_approx', 'stage_totals_seconds',
                                                   'approx_process_counters_during_loading', 'seconds_after_scene_set_matched',
                                                   'seconds_active_light_to_probe_wait_finished', 'dxt', 'sampler_ms', 'oom_markers')}, indent=2))


if __name__ == '__main__':
    main()
