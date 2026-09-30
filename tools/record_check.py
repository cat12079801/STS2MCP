#!/usr/bin/env python3
"""Structural check of combat records written by the MOD's recorder (docs/recording.md).

Reads only the record files. It does not decode the game's packets (that needs the game's
PacketReader: GET /api/v1/record/inspect, or the M3 importer in cat12079801/sts2) - it checks
that a record is whole: the line chains, the seq numbers, the pairing of public and hidden
lines, how the record started and ended, and the recorder's own self-check against the
game's replay.

    record_check.py <record dir or run dir or records root> [--json]

Exit status: 0 when every record is complete, 1 when any is not, 2 on usage errors.
"""
from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path

SCHEMA = "sts2-mod-record/1"


def read_chain(path: Path) -> tuple[list[dict], list[str]]:
    """Lines of a chained jsonl and the problems found in the chain itself."""
    problems: list[str] = []
    lines: list[dict] = []
    if not path.exists():
        return lines, [f"{path.name}: missing"]
    prev = None
    raw_lines = path.read_bytes().split(b"\n")
    if raw_lines and raw_lines[-1] == b"":
        raw_lines.pop()
    else:
        problems.append(f"{path.name}: last line is not terminated (cut short)")
    for n, raw in enumerate(raw_lines, 1):
        try:
            obj = json.loads(raw)
        except json.JSONDecodeError as e:
            problems.append(f"{path.name}:{n}: not JSON ({e})")
            prev = hashlib.sha256(raw).hexdigest()
            continue
        if obj.get("prev") != prev:
            problems.append(f"{path.name}:{n}: prev chain broken (expected {prev}, got {obj.get('prev')})")
        prev = hashlib.sha256(raw).hexdigest()
        lines.append(obj)
    return lines, problems


def check_record(d: Path) -> dict:
    pub, problems = read_chain(d / "public.jsonl")
    hid, hp = read_chain(d / "hidden.jsonl")
    problems += hp

    seqs = [l.get("seq") for l in pub]
    if seqs != list(range(len(seqs))):
        gaps = [i for i, s in enumerate(seqs) if s != i]
        problems.append(f"public seq not 0..{len(seqs) - 1} (first off at line {gaps[0] + 1 if gaps else '?'})")

    want_hidden = {l["seq"] for l in pub if l.get("hidden")}
    have_hidden = [l.get("seq") for l in hid]
    if len(have_hidden) != len(set(have_hidden)):
        problems.append("hidden: duplicate seq")
    if set(have_hidden) != want_hidden:
        missing = sorted(want_hidden - set(have_hidden))[:5]
        extra = sorted(set(have_hidden) - want_hidden)[:5]
        problems.append(f"hidden/public pairing: missing {missing} extra {extra}")

    opened = pub[0] if pub and pub[0].get("kind") == "record_open" else None
    closed = pub[-1] if pub and pub[-1].get("kind") == "record_close" else None
    if opened is None:
        problems.append("first line is not record_open")
    elif opened.get("schema") != SCHEMA:
        problems.append(f"schema {opened.get('schema')!r} is not {SCHEMA}")
    start = (opened or {}).get("start") or {}
    if start.get("boundary") != "room_entry" or start.get("initial_state") != "room_entry":
        problems.append(f"started late: boundary={start.get('boundary')} initial_state={start.get('initial_state')} "
                        f"game_events_before_open={start.get('game_events_before_open')}")
    inputs = start.get("inputs_outside_replay")
    if inputs != "complete":
        # "tainted": a console command ran in this process (outside the action queue); "unknown":
        # the recorder was installed mid-run and could not see what came before.
        problems.append(f"inputs outside the game's replay: {inputs} {start.get('console_in_process') or ''}".rstrip())
    if closed is None:
        problems.append("no record_close (the game stopped, crashed, or is still in this combat)")
    else:
        sc = closed.get("self_check") or {}
        for part in ("events", "checksums"):
            p = sc.get(part)
            if not p:
                problems.append(f"self_check.{part} missing (close without the game's replay: {closed.get('reason')})")
            elif not p.get("complete"):
                problems.append(f"self_check.{part} not complete: {p}")
        if closed.get("faults"):
            problems.append(f"faults: {closed['faults']}")
        if closed.get("sentry_captures"):
            problems.append(f"sentry_captures: {closed['sentry_captures']}")
        if closed.get("reason") not in ("combat_won", "combat_lost"):
            problems.append(f"ended by {closed.get('reason')} (combat not finished in this attempt)")
        mcr = d / "replay.mcr"
        if closed.get("game_replay_captured"):
            if not mcr.exists():
                problems.append("replay.mcr missing")
            elif sc.get("replay_mcr_sha256") and hashlib.sha256(mcr.read_bytes()).hexdigest() != sc["replay_mcr_sha256"]:
                problems.append("replay.mcr does not match self_check.replay_mcr_sha256")
    for l in pub:
        if l.get("kind") in ("recorder_fault", "enqueue_unrecorded"):
            problems.append(f"seq {l.get('seq')}: {l.get('kind')} {l.get('where') or l.get('action_id')}")

    kinds: dict[str, int] = {}
    for l in pub:
        kinds[l.get("kind")] = kinds.get(l.get("kind"), 0) + 1
    return {
        "record": str(d),
        "record_id": (opened or {}).get("record_id"),
        "attempt": (opened or {}).get("attempt"),
        "boundary": start.get("boundary"),
        "close": (closed or {}).get("reason"),
        "lines": len(pub),
        "kinds": kinds,
        "complete": not problems,
        "problems": problems,
    }


def find_records(root: Path) -> list[Path]:
    if (root / "public.jsonl").exists():
        return [root]
    return sorted(p.parent for p in root.rglob("public.jsonl"))


def main(argv: list[str]) -> int:
    args = [a for a in argv if not a.startswith("--")]
    if len(args) != 1:
        print(__doc__, file=sys.stderr)
        return 2
    records = find_records(Path(args[0]).expanduser())
    if not records:
        print(f"no records under {args[0]}", file=sys.stderr)
        return 2
    results = [check_record(r) for r in records]
    if "--json" in argv:
        print(json.dumps(results, ensure_ascii=False, indent=2))
    else:
        for r in results:
            mark = "OK " if r["complete"] else "NG "
            print(f"{mark}{r['record_id']} attempt={r['attempt']} boundary={r['boundary']} close={r['close']} lines={r['lines']}")
            for p in r["problems"]:
                print(f"     - {p}")
    return 0 if all(r["complete"] for r in results) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
