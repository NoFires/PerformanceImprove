"""Summarize PerformanceImprove JSONL diagnostics.

Usage:
    python scripts/analyze_performance_log.py LOG.jsonl [MORE.jsonl ...] [--out report.md]

Reads the session log written by PerformanceImprove 0.4.0 (Mods/PerformanceImprove/Logs/perf-*.jsonl).
Only observations are reported; nothing is inferred beyond the recorded records.
"""
from __future__ import annotations

import argparse
import json
import statistics
from collections import defaultdict
from datetime import datetime
from pathlib import Path


def read_records(path: Path):
    records = []
    bad = 0
    with path.open(encoding="utf-8-sig", errors="replace") as stream:
        for line in stream:
            line = line.strip()
            if not line:
                continue
            try:
                records.append(json.loads(line))
            except json.JSONDecodeError:
                bad += 1
    return records, bad


def fmt_ms(value):
    return f"{value:,.1f}"


def percentile(values, fraction):
    if not values:
        return 0.0
    ordered = sorted(values)
    index = min(len(ordered) - 1, max(0, int(round(fraction * (len(ordered) - 1)))))
    return ordered[index]


def summarize(path: Path, records, bad_lines) -> list[str]:
    out = []
    sessions = [r for r in records if r.get("k") == "session"]
    copies = [r for r in records if r.get("k") == "copy"]
    timings = [r for r in records if r.get("k") == "timing"]
    loadings = [r for r in records if r.get("k") == "loading"]
    memories = [r for r in records if r.get("k") == "memory"]
    probes = [r for r in records if r.get("k") == "probes"]

    out.append(f"## {path.name}")
    out.append("")
    if not sessions:
        out.append("- 没有 `session` 记录：日志可能被截断，或仍是旧版 Mod 写的。")
    for session in sessions[:1]:
        out.append(f"- 版本：PerformanceImprove {session.get('version')}；游戏 {session.get('game_version')}；Unity {session.get('unity_version')}")
        out.append(f"- 系统：{session.get('os')}；CPU {session.get('cpu_count')} 核；内存 {session.get('system_memory_mb')} MB")
        out.append(f"- Assembly-CSharp SHA256：`{session.get('assembly_sha256')}`")
        out.append(f"- 采样间隔：{session.get('sample_seconds')} s；跳过已学法术：{session.get('skip_known_readd')}")
        mods = [m.strip() for m in str(session.get("mods") or "").split(";") if m.strip()]
        enabled = [m for m in mods if not m.endswith("(off)")]
        out.append(f"- 已装 Mod：{len(enabled)} 启用 / {len(mods)} 个（启用：{'; '.join(enabled[:10])}）")
    for probe in probes[:1]:
        missing = str(probe.get("missing") or "").strip()
        out.append(f"- 探针：缺失 {probe.get('count')} 项" + (f"（{missing[:300]}）" if missing else "（全部挂载成功）"))
    if bad_lines:
        out.append(f"- 警告：{bad_lines} 行无法解析（写入中断或文件被截断）")
    out.append("")

    if copies:
        ms = [float(c.get("ms") or 0) for c in copies]
        add_known = sum(int(c.get("add_known") or 0) for c in copies)
        duplicates = sum(int(c.get("add_known_duplicate") or 0) for c in copies)
        skipped = sum(int(c.get("add_known_skipped") or 0) for c in copies)
        add_known_ms = sum(float(c.get("add_known_ms") or 0) for c in copies)
        learn_ms = sum(float(c.get("learn_ms") or 0) for c in copies)
        saving = sum(float(c.get("add_known_ms") or 0) * float(c.get("redundant_share") or 0) for c in copies)
        share = (duplicates / add_known * 100) if add_known else 0.0
        out.append("### 预览复制（开卡/升级）")
        out.append("")
        out.append(f"- 次数 {len(copies)}；合计 {fmt_ms(sum(ms))} ms；均值 {fmt_ms(statistics.fmean(ms))} ms；"
                   f"中位 {fmt_ms(statistics.median(ms))} ms；P95 {fmt_ms(percentile(ms, 0.95))} ms；最大 {fmt_ms(max(ms))} ms")
        out.append(f"- AddKnown 调用 {add_known} 次，其中已学过又被再加 {duplicates} 次"
                   f"（{share:.1f}%）；本次实验已跳过 {skipped} 次")
        out.append(f"- AddKnown 累计 {fmt_ms(add_known_ms)} ms；AddBaseLevel 段累计 {fmt_ms(learn_ms)} ms")
        out.append(f"- 若跳过重复再添加，本次可省约 {fmt_ms(saving)} ms（按每次复制的重复比例估算，不是实测收益）")
        areas = defaultdict(int)
        for copy in copies:
            areas[str(copy.get("area"))] += 1
        if areas:
            out.append("- 区域分布：" + "，".join(f"{k}={v}" for k, v in sorted(areas.items(), key=lambda kv: -kv[1])))
        errors = [c for c in copies if c.get("error")]
        out.append(f"- 复制中异常：{len(errors)}")
        out.append("")
        worst = sorted(copies, key=lambda c: -float(c.get("ms") or 0))[:5]
        out.append("最慢 5 次：")
        out.append("")
        out.append("| 秒 | ms | 法术书 | 已学法术 | AddKnown | 重复 | 跳过 | AddKnown ms | 区域 |")
        out.append("|---:|---:|---:|---:|---:|---:|---:|---:|---|")
        for copy in worst:
            out.append(f"| {copy.get('t')} | {fmt_ms(float(copy.get('ms') or 0))} | {copy.get('spellbooks')} | {copy.get('known_spells')} | "
                       f"{copy.get('add_known')} | {copy.get('add_known_duplicate')} | {copy.get('add_known_skipped')} | "
                       f"{fmt_ms(float(copy.get('add_known_ms') or 0))} | {copy.get('area')} |")
        out.append("")

    if timings:
        grouped = {}
        for row in timings:
            key = str(row.get("method"))
            entry = grouped.setdefault(key, {"calls": 0, "total": 0.0, "max": 0.0, "exceptions": 0})
            entry["calls"] += int(row.get("calls") or 0)
            entry["total"] += float(row.get("total_ms") or 0)
            entry["max"] = max(entry["max"], float(row.get("max_ms") or 0))
            entry["exceptions"] += int(row.get("exceptions") or 0)
        ranked = sorted(grouped.items(), key=lambda kv: -kv[1]["total"])[:20]
        out.append("### 方法耗时（前 20）")
        out.append("")
        out.append("| 方法 | 次数 | 合计 ms | 单次最大 ms | 异常 |")
        out.append("|---|---:|---:|---:|---:|")
        for name, entry in ranked:
            out.append(f"| `{name}` | {entry['calls']} | {fmt_ms(entry['total'])} | {fmt_ms(entry['max'])} | {entry['exceptions']}")
        out.append("")

    if loadings:
        stages = defaultdict(lambda: {"calls": 0, "total": 0.0, "max": 0.0})
        for row in loadings:
            key = str(row.get("stage"))
            stages[key]["calls"] += 1
            stages[key]["total"] += float(row.get("ms") or 0)
            stages[key]["max"] = max(stages[key]["max"], float(row.get("ms") or 0))
        out.append("### 加载阶段")
        out.append("")
        out.append("| 阶段 | 次数 | 合计 ms | 最大 ms |")
        out.append("|---|---:|---:|---:|")
        for name, entry in sorted(stages.items(), key=lambda kv: -kv[1]["total"])[:15]:
            out.append(f"| `{name}` | {entry['calls']} | {fmt_ms(entry['total'])} | {fmt_ms(entry['max'])}")
        out.append("")

    if memories:
        private = [float(m.get("private_bytes") or 0) for m in memories if float(m.get("private_bytes") or 0) > 0]
        out.append("### 内存")
        out.append("")
        if private:
            gib = [v / (1024 ** 3) for v in private]
            out.append(f"- 进程私有内存（Mod 内采样）：{len(gib)} 个样本；首 {gib[0]:.2f} GiB；末 {gib[-1]:.2f} GiB；峰值 {max(gib):.2f} GiB")
        else:
            out.append("- 进程私有/工作集读数为 0：Mono 下 `Process` 这两个属性不可用（已知限制），下面是 Unity/Mono 口径。")
            for key, label in (("mono_used", "Mono 已用"), ("mono_heap", "Mono 堆"), ("unity_allocated", "Unity 已分配"), ("unity_reserved", "Unity 已保留")):
                values = [float(m.get(key) or 0) / (1024 ** 3) for m in memories if float(m.get(key) or 0) > 0]
                if values:
                    out.append(f"- {label}：首 {values[0]:.2f} GiB；末 {values[-1]:.2f} GiB；峰值 {max(values):.2f} GiB")
        gc_first, gc_last = memories[0], memories[-1]
        out.append(f"- GC 计数：gen0 {gc_first.get('gc0')}→{gc_last.get('gc0')}；gen1 {gc_first.get('gc1')}→{gc_last.get('gc1')}；gen2 {gc_first.get('gc2')}→{gc_last.get('gc2')}")
        out.append("")

    if not copies and not timings and not loadings:
        out.append("- 没有计时记录：确认诊断开关是打开的，并且这次会话确实做过升级/开卡/进城操作。")
    return out


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("logs", nargs="+")
    parser.add_argument("--out", default="")
    args = parser.parse_args()

    report = [f"# PerformanceImprove 诊断报告", "", f"生成时间：{datetime.now().isoformat(timespec='seconds')}", ""]
    for name in args.logs:
        path = Path(name)
        if not path.is_file():
            report.append(f"## {path.name}\n\n- 文件不存在\n")
            continue
        records, bad = read_records(path)
        report.extend(summarize(path, records, bad))
    text = "\n".join(report)
    if args.out:
        Path(args.out).write_text(text, encoding="utf-8")
        print(f"wrote {args.out}")
    else:
        print(text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
