"""Summarize existing logs for UI, scene-loading and memory symptoms; no game execution."""
import json
import re
from collections import Counter
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LOGS = Path.home() / 'AppData/LocalLow/Owlcat Games/Pathfinder Wrath Of The Righteous'
STAMP = re.compile(r'^\[([\d.]+) - ([^]]+)\]: (.*)')
FINISH = re.compile(r"Loading process finished '(.*?)' duration: ([\d.]+)s")
OOM = re.compile(r'OutOfMemoryException|Could not allocate memory|out of memory|DynamicHeapAllocator.*failed|Crash!!!|Fatal error', re.I)


def main():
    reports = []
    for name in ('GameLogFull.txt', 'GameLogFullPrev.txt', 'Player.log', 'Player-prev.log'):
        path = LOGS / name
        if not path.is_file():
            continue
        stages, failures, relevant = [], [], []
        messages = Counter()
        last = None
        gaps = []
        categories = Counter()
        total = 0
        with path.open(encoding='utf-8-sig', errors='replace') as stream:
            for number, raw in enumerate(stream, 1):
                total = number
                line = raw.rstrip('\n')
                if OOM.search(line):
                    failures.append({'line': number, 'text': line[:800]})
                parsed = STAMP.match(line)
                if not parsed:
                    continue
                at, channel, message = float(parsed[1]), parsed[2], parsed[3]
                categories[channel] += 1
                # Values/IDs vary. Count literal messages rather than merging unrelated exceptions.
                messages[(channel, message[:300])] += 1
                if last and at > last[0] + 1:
                    gaps.append({'seconds': round(at - last[0], 4), 'before_line': last[1], 'after_line': number,
                                 'before': last[2][:300], 'after': message[:300]})
                last = (at, number, message)
                completed = FINISH.search(message)
                if completed:
                    stages.append({'line': number, 'ended_at_s': at, 'process': completed[1], 'duration_s': float(completed[2])})
                if len(relevant) < 120 and re.search(r'Drezen|Capital.*(?:load|scene)|(?:load|scene).*Capital|LevelUp|CreatePreview', message, re.I):
                    relevant.append({'line': number, 'at_s': at, 'text': message[:700]})
        reports.append({'file': str(path), 'bytes': path.stat().st_size,
                        'modified_local': datetime.fromtimestamp(path.stat().st_mtime).isoformat(),
                        'line_count': total, 'last_timestamp_s': last[0] if last else None,
                        'channels': dict(categories.most_common()),
                        'top_repeated_messages': [{'channel': k[0], 'text': k[1], 'count': v} for k, v in messages.most_common(20)],
                        'loading_stages': sorted(stages, key=lambda x: x['duration_s'], reverse=True),
                        'largest_timestamp_gaps': sorted(gaps, key=lambda x: x['seconds'], reverse=True)[:15],
                        'memory_failure_markers': failures, 'context_samples': relevant})
    result = {'notice': 'Existing historical logs only. Timestamp gaps are not CPU profiles. No failure marker does not exclude OOM.', 'logs': reports}
    (ROOT / 'analysis/symptom-log-evidence.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    for report in reports:
        print(json.dumps({k: report[k] for k in ('file', 'bytes', 'line_count', 'last_timestamp_s')}, ensure_ascii=False))
        print('Top loading stages:', json.dumps(report['loading_stages'][:6], ensure_ascii=False))
        print('Repeated:', json.dumps(report['top_repeated_messages'][:4], ensure_ascii=False))
        print('OOM/crash markers:', len(report['memory_failure_markers']))


if __name__ == '__main__':
    main()
