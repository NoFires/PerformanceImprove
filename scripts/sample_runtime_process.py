"""Out-of-process sampler: continues recording during Unity main-thread stalls."""
import argparse
import csv
import time
from datetime import datetime, timezone
from pathlib import Path

import psutil  # Existing Anaconda installation; no new dependency installation.


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--pid', type=int, required=True)
    parser.add_argument('--seconds', type=float, default=1800)
    parser.add_argument('--interval', type=float, default=2)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    session = Path((root / 'analysis/runtime/current-session.txt').read_text(encoding='utf-8'))
    process = psutil.Process(args.pid)
    birth = process.create_time()
    start = time.monotonic()
    output = session / f'process-{args.pid}.csv'
    with output.open('w', newline='', encoding='utf-8') as stream:
        writer = csv.writer(stream)
        writer.writerow(['utc', 'sample_elapsed_s', 'process_age_s', 'cpu_total_s', 'working_set_bytes',
                         'private_bytes', 'peak_working_set_bytes', 'read_bytes_cumulative', 'write_bytes_cumulative',
                         'threads', 'handles', 'system_available_ram_bytes', 'system_ram_percent'])
        while time.monotonic() - start < args.seconds:
            try:
                if process.create_time() != birth:
                    break
                with process.oneshot():
                    memory = process.memory_info()
                    cpu = process.cpu_times()
                    io = process.io_counters()
                    system = psutil.virtual_memory()
                    writer.writerow([datetime.now(timezone.utc).isoformat(), round(time.monotonic() - start, 3),
                                     round(time.time() - birth, 3), cpu.user + cpu.system, memory.rss,
                                     memory.private, memory.peak_wset, io.read_bytes, io.write_bytes,
                                     process.num_threads(), process.num_handles(), system.available, system.percent])
                    stream.flush()
            except psutil.NoSuchProcess:
                print('Game process exited; sample file flushed.')
                break
            time.sleep(args.interval)
    print(output)


if __name__ == '__main__':
    main()
