"""Passive recording only: no UI input, memory writes, GC or game-state changes."""
import argparse
import csv
import json
import shutil
import time
from datetime import datetime, timezone
from pathlib import Path

import psutil

ROOT = Path(__file__).resolve().parents[1]
LOGS = Path(r'D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure\Mods\WrathPerformanceLab\Logs')


def read_rows(path):
    if not path.is_file():
        return []
    with path.open(encoding='utf-8-sig', errors='replace', newline='') as stream:
        return list(csv.DictReader(stream))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--pid', required=True, type=int)
    parser.add_argument('--seconds', default=7200, type=float)
    parser.add_argument('--name', default='user-operation-recording')
    parser.add_argument('--interval', default=5, type=float)
    args = parser.parse_args()
    if Path(args.name).name != args.name or args.name in ('.', '..') or args.interval < 0.25:
        parser.error('Use a single directory name and an interval >= 0.25 seconds')
    session = Path((ROOT / 'analysis/runtime/current-session.txt').read_text(encoding='utf-8'))
    folder = max(LOGS.iterdir(), key=lambda p: p.name)
    destination = session / args.name
    # A new segment must never overwrite a prior test or reuse its STOP marker.
    destination.mkdir(exist_ok=False)
    start = time.monotonic()
    process = psutil.Process(args.pid)
    birth = process.create_time()
    utc_start = datetime.now(timezone.utc).isoformat()
    metadata = {'start_utc': utc_start, 'pid': args.pid, 'process_created_unix': birth,
                'diagnostic_log_folder': str(folder), 'operator': 'user', 'input_automation': False,
                'maximum_seconds': args.seconds, 'sample_interval_seconds': args.interval,
                'segment_name': args.name, 'process_io_note': 'Process read/write counters include cached I/O; not physical disk latency.'}
    (destination / 'metadata.json').write_text(json.dumps(metadata, indent=2), encoding='utf-8')
    (session / 'current-recording.txt').write_text(str(destination), encoding='utf-8')
    with (session / 'actions.jsonl').open('a', encoding='utf-8') as stream:
        stream.write(json.dumps({'utc': utc_start, 'action': 'user_takes_control_passive_recording_only', 'segment': args.name}) + '\n')
    # Save an exact boundary snapshot separately from the continuously updated copies.
    boundary = destination / 'start'
    boundary.mkdir(exist_ok=True)
    for source in folder.iterdir():
        if source.is_file():
            shutil.copy2(source, boundary / source.name)
    with (destination / 'samples.csv').open('w', newline='', encoding='utf-8') as stream:
        writer = csv.writer(stream)
        writer.writerow(['utc', 'recording_elapsed_s', 'process_age_s', 'cpu_total_s', 'private_bytes',
                         'working_set_bytes', 'system_available_ram_bytes', 'threads', 'handles',
                         'last_mod_elapsed_s', 'managed_estimate_bytes', 'mono_heap_bytes', 'gc2_cumulative',
                         'fabric_nodes', 'stopped_controller_targets_found', 'controller_alive', 'preview_alive',
                         'preview_disposed_alive', 'preview_collected_observed', 'va2_settings', 'dxt_queue',
                         'dxt_worker_failures_total', 'atlas_queue', 'last_memory_elapsed_s',
                         'read_bytes_cumulative', 'write_bytes_cumulative', 'read_operations_cumulative',
                         'write_operations_cumulative', 'peak_working_set_bytes', 'last_loading_event',
                         'last_loading_stage', 'last_loading_elapsed_s', 'diagnostics_file_age_s', 'memory_file_age_s',
                         'dxt_metrics_elapsed_s', 'atlas_metrics_elapsed_s', 'dxt_metrics_status', 'atlas_metrics_status'])
        cache = {}
        last_copy = -10
        def cached_rows(name):
            source = folder / name
            stat = source.stat()
            stamp = (stat.st_mtime_ns, stat.st_size)
            if name not in cache or cache[name][0] != stamp:
                cache[name] = (stamp, read_rows(source))
            return cache[name][1]
        while time.monotonic() - start < args.seconds:
            if (destination / 'STOP').exists():
                break
            try:
                if process.create_time() != birth:
                    break
                diagnostics = cached_rows('diagnostics.csv')
                groups, group_times, group_status = {}, {}, {}
                for row in diagnostics:
                    if row.get('status') is None:
                        continue  # Ignore an incomplete final CSV row during a write.
                    group = row['group']
                    if group_times.get(group) != row['elapsed_s']:
                        groups[group] = {}
                        group_times[group] = row['elapsed_s']
                    groups[group][row['metric']] = row['value']
                    group_status[group] = row['status']
                memory = cached_rows('memory.csv')
                loading = cached_rows('loading.csv')
                last_loading = loading[-1] if loading else {}
                latest_memory = memory[-1] if memory else {}
                info = process.memory_info()
                cpu = process.cpu_times()
                io = process.io_counters()
                def metric(group, key):
                    return groups.get(group, {}).get(key, '')
                writer.writerow([datetime.now(timezone.utc).isoformat(), round(time.monotonic() - start, 3),
                                 round(time.time() - birth, 3), cpu.user + cpu.system, info.private, info.rss,
                                 psutil.virtual_memory().available, process.num_threads(), process.num_handles(),
                                 diagnostics[-1].get('elapsed_s', '') if diagnostics else '',
                                 latest_memory.get('managed_live_estimate_bytes', ''), latest_memory.get('mono_heap_bytes', ''),
                                 latest_memory.get('gc2_cumulative', ''), metric('late_update', 'fabric_nodes'),
                                 metric('late_update', 'stopped_controller_targets_found'), metric('controllers', 'alive_tracked'),
                                 metric('previews', 'alive_tracked'), metric('previews', 'alive_disposed'),
                                 metric('previews', 'collected_observed'), metric('va2', 'settings_total'),
                                 metric('dxt', 'queue_count'), metric('dxt', 'worker_failures_observed_total'),
                                 metric('atlas', 'queue_count'), latest_memory.get('elapsed_s', ''),
                                 io.read_bytes, io.write_bytes, io.read_count, io.write_count, info.peak_wset,
                                 last_loading.get('event', ''), last_loading.get('stage', ''), last_loading.get('elapsed_s', ''),
                                 round(time.time() - (folder / 'diagnostics.csv').stat().st_mtime, 3),
                                 round(time.time() - (folder / 'memory.csv').stat().st_mtime, 3),
                                 group_times.get('dxt', ''), group_times.get('atlas', ''),
                                 group_status.get('dxt', ''), group_status.get('atlas', '')])
                stream.flush()
                # Keep a recoverable snapshot even if the game later rotates its log files.
                if time.monotonic() - start - last_copy >= 5:
                    for source in folder.iterdir():
                        if source.is_file():
                            shutil.copy2(source, destination / source.name)
                    last_copy = time.monotonic() - start
            except psutil.NoSuchProcess:
                break
            time.sleep(args.interval)
    metadata['end_utc'] = datetime.now(timezone.utc).isoformat()
    metadata['elapsed_seconds'] = time.monotonic() - start
    (destination / 'metadata.json').write_text(json.dumps(metadata, indent=2), encoding='utf-8')
    print(destination)


if __name__ == '__main__':
    main()
