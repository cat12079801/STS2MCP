#!/usr/bin/env python3
"""Regression cases for tools/record_check.py: each way a record can be cut short or damaged
must be reported, and a whole record must pass. Builds synthetic records in a temp dir; needs
no game.

    /usr/bin/python3 tools/record_check_selftest.py
"""
from __future__ import annotations

import hashlib
import json
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import record_check  # noqa: E402


def chain(lines: list[dict]) -> bytes:
    out, prev = [], None
    for obj in lines:
        obj = dict(obj, prev=prev)
        raw = json.dumps(obj, ensure_ascii=False, separators=(",", ":")).encode()
        prev = hashlib.sha256(raw).hexdigest()
        out.append(raw)
    return b"\n".join(out) + b"\n"


def whole() -> tuple[list[dict], list[dict], bytes]:
    mcr = b"\x01\x02\x03"
    pub = [
        {"seq": 0, "kind": "record_open", "schema": "sts2-mod-record/1", "record_id": "1/001-a0f01", "attempt": 1,
         "start": {"boundary": "room_entry", "initial_state": "room_entry", "game_events_before_open": 0,
                   "inputs_outside_replay": "complete"}},
        {"seq": 1, "kind": "initial_state", "hidden": True},
        {"seq": 2, "kind": "game_event", "hidden": True, "type": "GameAction"},
        {"seq": 3, "kind": "record_close", "reason": "combat_won", "game_replay_captured": True, "faults": [],
         "sentry_captures": 0,
         "self_check": {"events": {"complete": True}, "checksums": {"complete": True},
                        "replay_mcr_sha256": hashlib.sha256(mcr).hexdigest()}},
    ]
    hid = [{"seq": 1, "kind": "initial_state", "packet": "AA=="}, {"seq": 2, "kind": "game_event", "packet": "AA=="}]
    return pub, hid, mcr


def write(d: Path, pub, hid, mcr, pub_bytes: bytes | None = None):
    d.mkdir(parents=True)
    (d / "public.jsonl").write_bytes(pub_bytes if pub_bytes is not None else chain(pub))
    (d / "hidden.jsonl").write_bytes(chain(hid))
    if mcr is not None:
        (d / "replay.mcr").write_bytes(mcr)


def main() -> int:
    cases = []

    def case(name, expect_ok, mutate, needle=None):
        cases.append((name, expect_ok, mutate, needle))

    case("whole record passes", True, lambda p, h, m: (p, h, m, None))
    case("missing record_close (crash / kill)", False, lambda p, h, m: (p[:-1], h, m, None), "no record_close")
    case("line deleted in the middle", False,
         lambda p, h, m: (p, h, m, chain(p).split(b"\n")[0] + b"\n" + b"\n".join(chain(p).split(b"\n")[2:])),
         "prev chain broken")
    case("line edited in place", False,
         lambda p, h, m: (p, h, m, chain(p).replace(b'"GameAction"', b'"HookAction"')), "prev chain broken")
    case("last line cut short", False, lambda p, h, m: (p, h, m, chain(p)[:-5]), "not terminated")
    case("hidden line missing", False, lambda p, h, m: (p, h[:1], m, None), "pairing")
    case("started mid-combat", False,
         lambda p, h, m: ([dict(p[0], start={"boundary": "mid_combat", "initial_state": "read_back",
                                              "game_events_before_open": 5, "inputs_outside_replay": "complete"})] + p[1:], h, m, None), "started late")
    case("self check mismatch", False,
         lambda p, h, m: (p[:-1] + [dict(p[-1], self_check=dict(p[-1]["self_check"], events={"complete": False}))], h, m, None),
         "self_check.events not complete")
    case("left mid-combat (save and quit)", False,
         lambda p, h, m: (p[:-1] + [dict(p[-1], reason="cleanup")], h, m, None), "ended by cleanup")
    case("replay.mcr altered", False, lambda p, h, m: (p, h, b"\x09", None), "does not match")
    case("recorder fault", False,
         lambda p, h, m: (p[:-1] + [{"seq": 3, "kind": "recorder_fault", "where": "x"}, dict(p[-1], seq=4)], h, m, None),
         "recorder_fault")
    case("unrecorded enqueue", False,
         lambda p, h, m: (p[:-1] + [{"seq": 3, "kind": "enqueue_unrecorded", "action_id": 9}, dict(p[-1], seq=4, faults=["enqueue_unrecorded:9"])], h, m, None),
         "enqueue_unrecorded")
    case("console command earlier in the process", False,
         lambda p, h, m: ([dict(p[0], start=dict(p[0]["start"], inputs_outside_replay="tainted",
                                                  console_in_process=[{"command": "godmode"}]))] + p[1:], h, m, None),
         "inputs outside the game's replay: tainted")
    case("recorder installed mid-run", False,
         lambda p, h, m: ([dict(p[0], start=dict(p[0]["start"], inputs_outside_replay="unknown"))] + p[1:], h, m, None),
         "inputs outside the game's replay: unknown")
    case("sentry capture during the record", False,
         lambda p, h, m: (p[:-1] + [dict(p[-1], sentry_captures=1)], h, m, None), "sentry_captures")

    failed = 0
    with tempfile.TemporaryDirectory() as tmp:
        for i, (name, expect_ok, mutate, needle) in enumerate(cases):
            p, h, m = whole()
            p, h, m, raw = mutate(p, h, m)
            d = Path(tmp) / f"{i:02d}"
            write(d, p, h, m, raw)
            r = record_check.check_record(d)
            ok = r["complete"] == expect_ok and (needle is None or any(needle in x for x in r["problems"]))
            print(("PASS " if ok else "FAIL ") + name + ("" if ok else f"  -> {r['problems']}"))
            failed += not ok
    print(f"{len(cases) - failed}/{len(cases)} passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
