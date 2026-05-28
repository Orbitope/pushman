#!/usr/bin/env python3
"""Pushman training + match report — the single on-demand reporting tool.

Reads two existing data sources, adds zero logging overhead:
  1. TensorBoard event files under results/<run>/   (training progress)
  2. The newest CSV in match_logs/                  (match permutation analysis)

Usage:
    python tools/report.py                 # newest run + newest match log
    python tools/report.py Pushman_Master_v1
    python tools/report.py --matches-only
    python tools/report.py --training-only

Run from the project root.
"""
import csv
import glob
import os
import sys
from collections import defaultdict

# --------------------------------------------------------------------------
# Training progress (TensorBoard tfevents)
# --------------------------------------------------------------------------

KEY_SCALARS = [
    "Environment/Cumulative Reward",
    "Environment/Episode Length",
    "Self-play/ELO",
    "Policy/Entropy",
    "Losses/Value Loss",
    "Losses/Policy Loss",
]


def newest_run():
    runs = [d for d in glob.glob("results/*") if os.path.isdir(d)]
    runs.sort(key=os.path.getmtime)
    return os.path.basename(runs[-1]) if runs else None


def find_event_dir(run_id):
    base = os.path.join("results", run_id)
    for root, _, files in os.walk(base):
        if any(f.startswith("events.out.tfevents") for f in files):
            return root
    return None


def summarize_scalar(tag, events):
    vals = [e.value for e in events]
    steps = [e.step for e in events]
    if not vals:
        return
    n = max(1, len(vals) // 10)
    early = sum(vals[:n]) / n
    recent = sum(vals[-n:]) / n
    arrow = "UP" if recent > early else "DOWN" if recent < early else "FLAT"
    print(f"  {tag}")
    print(f"    step {steps[0]:,} -> {steps[-1]:,}   "
          f"value {vals[0]:.3f} -> {vals[-1]:.3f}   "
          f"[{arrow}: early {early:.3f} -> recent {recent:.3f}]")
    print(f"    range min {min(vals):.3f} / max {max(vals):.3f}")


def training_report(run_id):
    print("=" * 68)
    print(f"TRAINING PROGRESS  -  {run_id}")
    print("=" * 68)
    ev_dir = find_event_dir(run_id)
    if not ev_dir:
        print(f"  No tfevents found under results/{run_id}/ (run not started?)")
        return
    try:
        from tensorboard.backend.event_processing.event_accumulator import EventAccumulator
    except ImportError:
        print("  tensorboard not importable — activate venv-mlagents first.")
        return

    acc = EventAccumulator(ev_dir)
    acc.Reload()
    available = acc.Tags().get("scalars", [])
    if not available:
        print("  No scalar data logged yet.")
        return

    shown = False
    for tag in KEY_SCALARS:
        if tag in available:
            summarize_scalar(tag, acc.Scalars(tag))
            shown = True
    if not shown:
        print(f"  Scalars present but none recognized: {', '.join(available[:8])}")

    # Health heuristic: real learning = reward up AND episode length down.
    if ("Environment/Cumulative Reward" in available
            and "Environment/Episode Length" in available):
        rew = [e.value for e in acc.Scalars("Environment/Cumulative Reward")]
        ep = [e.value for e in acc.Scalars("Environment/Episode Length")]
        if len(rew) >= 4 and len(ep) >= 4:
            q = max(1, len(rew) // 4)
            rew_up = sum(rew[-q:]) / q > sum(rew[:q]) / q
            ep_down = sum(ep[-q:]) / q < sum(ep[:q]) / q
            print()
            if rew_up and ep_down:
                print("  HEALTH: reward rising + episodes shortening = real learning.")
            elif rew_up and not ep_down:
                print("  HEALTH: reward rising but episodes NOT shortening — "
                      "possible reward-shaping stall (check Section 4).")
            else:
                print("  HEALTH: reward not clearly rising — inspect run.")


# --------------------------------------------------------------------------
# Match permutation analysis (match_logs CSV)
# --------------------------------------------------------------------------

def newest_match_log():
    files = sorted(
        glob.glob("match_logs/*.csv") + glob.glob("builds/match_logs/*.csv"),
        key=os.path.getmtime)
    return files[-1] if files else None


def build_matrix(rows, win_key, lose_key):
    wins = defaultdict(lambda: defaultdict(int))
    labels = set()
    for r in rows:
        a, b = r[win_key], r[lose_key]
        wins[a][b] += 1
        labels.add(a)
        labels.add(b)
    return wins, sorted(labels)


def print_matrix(title, wins, labels):
    if len(labels) < 2:
        print(f"\n{title}")
        print(f"  Only one value present ({labels[0] if labels else 'none'}) "
              f"— matrix skipped (expected for single-personality self-play).")
        return
    print(f"\n{title}")
    print("  (cell = row's win rate vs column; '.' = no matches yet)")
    w = max(len(l) for l in labels) + 2
    print(" " * w + "".join(f"{l[:9]:>10}" for l in labels))
    for a in labels:
        cells = []
        for b in labels:
            if a == b:
                cells.append(f"{'--':>10}")
                continue
            aw, bw = wins[a][b], wins[b][a]
            tot = aw + bw
            cells.append(f"{'.':>10}" if tot == 0 else f"{aw / tot * 100:>9.0f}%")
        print(f"{a:<{w}}" + "".join(cells))


def print_overall(title, wins, labels):
    print(f"\n{title}")
    ranked = []
    for a in labels:
        aw = sum(wins[a].values())
        al = sum(wins[b][a] for b in labels)
        tot = aw + al
        ranked.append((a, aw, al, aw / tot * 100 if tot else 0.0))
    for a, aw, al, wr in sorted(ranked, key=lambda x: -x[3]):
        print(f"  {a:<18} {wr:5.1f}%   ({aw}W / {al}L)")


def print_coverage(rows):
    print("\nMatch count per personality pairing (balance check):")
    pair = defaultdict(int)
    for r in rows:
        key = tuple(sorted([r["winner_personality"], r["loser_personality"]]))
        pair[key] += 1
    for (a, b), n in sorted(pair.items()):
        flag = "   << LOW COVERAGE" if n < 30 else ""
        print(f"  {a} vs {b}: {n}{flag}")


def match_report(path):
    print()
    print("=" * 68)
    print("MATCH PERMUTATION ANALYSIS")
    print("=" * 68)
    if not path or not os.path.exists(path):
        print("  No match log found in match_logs/ (no training run logged yet).")
        return
    with open(path) as f:
        rows = list(csv.DictReader(f))
    print(f"  Source: {path}   ({len(rows)} matches)")
    if not rows:
        print("  Log is empty.")
        return

    pw, pl = build_matrix(rows, "winner_personality", "loser_personality")
    print_matrix("PERSONALITY win-rate matrix:", pw, pl)
    if len(pl) >= 2:
        print_overall("PERSONALITY overall ranking:", pw, pl)
        print_coverage(rows)

    sw, sl = build_matrix(rows, "winner_stats", "loser_stats")
    print_matrix("CHARACTERSTATS win-rate matrix:", sw, sl)
    if len(sl) >= 2:
        print_overall("CHARACTERSTATS overall ranking:", sw, sl)

    durs = [float(r["duration_s"]) for r in rows if r.get("duration_s")]
    if durs:
        print(f"\nMatch duration: avg {sum(durs) / len(durs):.1f}s   "
              f"(min {min(durs):.1f}, max {max(durs):.1f})")


# --------------------------------------------------------------------------

def main():
    args = sys.argv[1:]
    do_training = "--matches-only" not in args
    do_matches = "--training-only" not in args
    run_id = next((a for a in args if not a.startswith("-")), None) or newest_run()

    if do_training:
        if run_id:
            training_report(run_id)
        else:
            print("No runs found in results/.")
    if do_matches:
        match_report(newest_match_log())


if __name__ == "__main__":
    main()
