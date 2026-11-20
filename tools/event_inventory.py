#!/usr/bin/env python3
"""ゲームの「出来事の順番」と「選択の文脈」の棚卸し（event inventory）を作る。

state inventory（tools/state_inventory.py）はモデルクラスの *項目* を数える。こちらは項目ではない情報、
つまり戦闘中に起きる通知・選択画面・履歴・山の移動・決定時に計算される値を数える。

使い方（リポジトリのルートから）:

    /Users/anshinin/workspace/repos/sts2/bin/sts2-python -B tools/event_inventory.py \
        --decompile ~/.local/state/sts2/decompile/sts2-a671e9ef --game-version v0.113.1

    # 文書を書かずに生成し直して比べる（違う・未分類・stale・被覆の引用切れがあれば終了コード 1）
    ... tools/event_inventory.py --decompile ... --check

入力:
  --decompile  ILSpy の出力（ゲーム本体 sts2.dll の -p 展開）。ディレクトリ名 sts2-<sha8> を版の印に使う。
  --mod        MOD のソース（既定はリポジトリのルート）。*.cs の参照を機械的に集めて、分類の手がかりにする。
  docs/event-inventory-classification.json
               人の判断。items（キー → 分類）と rules（キーの正規表現 → 分類。items に無いキーに順に当てる）。

機械的に数えるもの（カテゴリ）:
  hook      Hook.* の Before*/After* 通知（Task を返すもの）。分配先の AbstractModel のメソッド（Early/Late を含む）、
            listener の列（combat = IterateCombatHookListeners、combat_all = ICombatState.IterateHookListeners、
            run = IRunState.IterateHookListeners）、PlayerChoiceContext を渡すか（= 中で選択画面が開きうる）、
            上書きしているモデル（スコープ別）、上書きの本体（と同じクラスの 1 段下の呼び出し）が CardSelectCmd を呼ぶモデル。
  computed  Hook.* の Modify*/Should*/Try* と、生成の工場（CardFactory/PotionFactory/RelicFactory）の public static。
  select    選択の入口（CardSelectCmd.* / RelicSelectCmd.*）と、選択画面の設定（CardSelectorPrefs の public プロパティ）。
  history   CombatHistory の記録メソッドと *Entry クラス（項目つき）。
  cmd       Commands の *Cmd クラスの public static（CardSelectCmd/RelicSelectCmd を除く）。
  phase     CombatManager の手番の段階のメソッドのうち、Hook.* か CardPileCmd.* を呼ぶもの（呼び出し順つき）。
  rng       RunRngSet / PlayerRngSet の乱数の系列（すべて非公開の前提の確認用）。
  manual    分類表の manual_items（Cmd でない決定点など。存在は decompile の grep で確かめる）。

スコープ（自動）: 参照する（上書きする・呼ぶ）モデルを state_inventory.assign_pools のプールに分け、
  アイアンクラッドの範囲（ironclad/colorless/curse/status/shared/ancient/event-only/enchantment/affliction/monster）に
  1 つでもあれば ironclad、他キャラ専用だけなら other-only、モデルの参照が無ければ core（ゲーム本体の流れだけ）。

分類（手）: exposed / public-missing / hidden / cosmetic / out_of_scope / internal。
  exposed は exposed_by（"<MOD のファイル>: <その行に含まれる文字列>"）を必須にし、文字列がファイルに無ければ
  引用切れとして --check で落とす。public-missing は proposed_key と where を持つ。in_progress（例 "#1115"）は
  進行中のブランチが扱うもの。internal は、プレイヤーにもエージェントにも意味の無い内部の都合（Clear・デバッグなど）。

出力: docs/event-inventory.md（要約）と docs/event-inventory-items.tsv（全候補の機械向けの表。版の差分を取る用）。
MOD の *.cs を直すと被覆の file:line が動くので、MOD の PR の前に回し直す（--check で確かめる）。

標準ライブラリだけを使う。C# の構文解析はしない（ILSpy の書式〔タブの字下げ・1 行 1 宣言〕に頼る行単位の抽出）。
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import state_inventory as si  # noqa: E402  プールの割り当てを共有する

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_CLASSIFICATION = ROOT / "docs" / "event-inventory-classification.json"
DEFAULT_TSV = ROOT / "docs" / "event-inventory-items.tsv"
DEFAULT_MD = ROOT / "docs" / "event-inventory.md"

IRONCLAD_SCOPES = {"ironclad", "colorless", "curse", "status", "shared", "ancient", "event-only",
                   "enchantment", "affliction", "monster"}
CLASSES = ["exposed", "public-missing", "hidden", "cosmetic", "internal", "out_of_scope", "unknown"]
CATEGORIES = ["hook", "computed", "select", "history", "cmd", "phase", "rng", "manual"]
CATEGORY_LABEL = {
    "hook": "通知（Hook の Before/After）",
    "computed": "決定時の計算（Modify/Should・生成）",
    "select": "選択の入口と画面の設定",
    "history": "戦闘の履歴",
    "cmd": "山の移動・生成・コマンド",
    "phase": "手番の段階（CombatManager）",
    "rng": "乱数の系列",
    "manual": "その他の決定点（手で登録）",
}
EXCLUDED_MOD_FILES = {"McpMod.StateAllowlist.Generated.cs"}
CMD_SELECT = {"CardSelectCmd", "RelicSelectCmd"}


# ---------------------------------------------------------------------------
# C# の行単位の抽出
# ---------------------------------------------------------------------------

def read_lines(p: Path) -> list[str]:
    return p.read_text(encoding="utf-8-sig").splitlines()


def tabs(s: str) -> int:
    return len(s) - len(s.lstrip("\t"))


def body_end(lines: list[str], header: int) -> int:
    """header 行の宣言の本体の終わり（含む）の行番号。式本体（=>）は 1 行。"""
    ind = tabs(lines[header])
    if lines[header].rstrip().endswith(";"):
        return header
    j = header + 1
    while j < len(lines) and not lines[j].strip():
        j += 1
    if j >= len(lines) or lines[j].strip() != "{":
        # 複数行の引数・=> の続き
        k = header
        while k < len(lines) and not lines[k].rstrip().endswith((";", "{")) and lines[k].strip() != "{":
            k += 1
        if k < len(lines) and lines[k].rstrip().endswith(";"):
            return k
        j = k
    for k in range(j + 1, len(lines)):
        if lines[k].strip() == "}" and tabs(lines[k]) == ind:
            return k
    return len(lines) - 1


STATIC_RE = re.compile(r"^\tpublic static (?:async )?(?:[\w<>\[\],.? ()]+?) (\w+)(?:<[^>]*>)?\((.*)$")
PROP_RE = re.compile(r"^\tpublic (?:static )?([\w<>\[\],.? ]+?) (\w+) (?:\{|=>)")


def public_statics(p: Path) -> list[dict]:
    lines = read_lines(p)
    out = []
    for i, l in enumerate(lines):
        m = STATIC_RE.match(l)
        if not m or " class " in l:
            continue
        out.append({"name": m.group(1), "line": i + 1, "sig": l.strip(), "end": body_end(lines, i) + 1})
    return out


# ---------------------------------------------------------------------------
# モデル（上書き・呼び出し）
# ---------------------------------------------------------------------------

OVERRIDE_RE = re.compile(r"^\t(?:public|protected|internal) override (?:async )?[\w<>\[\],.? ()]+? (\w+)\(")
CALL_RE = re.compile(r"\b(Hook|CardSelectCmd|RelicSelectCmd|CardPileCmd|CardCmd|PowerCmd|CreatureCmd|DamageCmd|"
                     r"PlayerCmd|PotionCmd|RelicCmd|OrbCmd|ForgeCmd|OstyCmd|ArrowCmd|MelodyCmd|MapCmd|RewardsCmd|"
                     r"SfxCmd|VfxCmd|TalkCmd|ThinkCmd|Cmd|CardFactory|PotionFactory|RelicFactory)\.(\w+)\b")
METHOD_DECL_RE = re.compile(r"^\t(?:public|private|protected|internal)[\w<>\[\],.? ()]*? (\w+)\([^;]*$")


class ModelScan:
    def __init__(self):
        self.overrides: dict[str, set[str]] = defaultdict(set)          # method → classes
        self.override_selects: dict[str, set[str]] = defaultdict(set)   # method → classes whose override opens a selection
        self.calls: dict[str, set[str]] = defaultdict(set)              # "Cls.Method" → model classes
        self.pool: dict[str, str] = {}


def scan_models(decompile: Path) -> ModelScan:
    classes, _members, _lc = si.collect(decompile)
    pool_of = si.assign_pools(decompile, classes)
    ms = ModelScan()
    files: dict[Path, str] = {}
    for ci in classes.values():
        if ci.outer is None:
            files.setdefault(ci.file, ci.name)
            ms.pool[ci.name] = pool_of.get(ci.name, "unknown")
    for p, cname in files.items():
        lines = read_lines(p)
        # クラス内のメソッドの本体（1 段下の呼び出しを追うため）
        bodies: dict[str, tuple[int, int]] = {}
        for i, l in enumerate(lines):
            m = METHOD_DECL_RE.match(l)
            if m:
                bodies.setdefault(m.group(1), (i, body_end(lines, i)))
        def opens_select(a: int, b: int, depth: int = 0) -> bool:
            seg = "\n".join(lines[a:b + 1])
            if "CardSelectCmd." in seg or "RelicSelectCmd." in seg:
                return True
            if depth >= 1:
                return False
            for name in set(re.findall(r"\b(\w+)\(", seg)):
                if name in bodies and bodies[name][0] != a:
                    x, y = bodies[name]
                    if opens_select(x, y, depth + 1):
                        return True
            return False
        for i, l in enumerate(lines):
            m = OVERRIDE_RE.match(l)
            if m:
                name = m.group(1)
                ms.overrides[name].add(cname)
                end = body_end(lines, i)
                if opens_select(i, end):
                    ms.override_selects[name].add(cname)
        for l in lines:
            for cls, meth in CALL_RE.findall(l):
                ms.calls[f"{cls}.{meth}"].add(cname)
    return ms


def scope_of(names: set[str], ms: ModelScan) -> tuple[str, list[str], list[str]]:
    ic = sorted(n for n in names if ms.pool.get(n) in IRONCLAD_SCOPES)
    other = sorted(n for n in names if ms.pool.get(n) not in IRONCLAD_SCOPES)
    if ic:
        return "ironclad", ic, other
    if other:
        return "other-only", ic, other
    return "core", ic, other


# ---------------------------------------------------------------------------
# ゲーム全体の呼び出し元（モデル以外も含む）
# ---------------------------------------------------------------------------

def global_callers(decompile: Path) -> dict[str, set[str]]:
    out: dict[str, set[str]] = defaultdict(set)
    hist_re = re.compile(r"\bHistory\.(\w+)\(")
    for d in decompile.iterdir():
        if not d.is_dir() or not d.name.startswith("MegaCrit.Sts2"):
            continue
        for p in d.glob("*.cs"):
            try:
                text = p.read_text(encoding="utf-8-sig")
            except OSError:
                continue
            for cls, meth in set(CALL_RE.findall(text)):
                if p.stem != cls:
                    out[f"{cls}.{meth}"].add(p.stem)
            for meth in set(hist_re.findall(text)):
                if p.stem != "CombatHistory":
                    out[f"CombatHistory.{meth}"].add(p.stem)
    return out


# ---------------------------------------------------------------------------
# 候補の列挙
# ---------------------------------------------------------------------------

def rel(decompile: Path, p: Path, line: int) -> str:
    return f"{p.parent.name}/{p.name}:{line}"


def collect_items(decompile: Path, ms: ModelScan, callers: dict[str, set[str]]) -> list[dict]:
    items: list[dict] = []
    absm = read_lines(decompile / "MegaCrit.Sts2.Core.Models" / "AbstractModel.cs")
    virtuals = set(re.findall(r"public virtual [\w<>\[\],.? ()]+? (\w+)\(", "\n".join(absm)))

    # --- Hook ---
    hp = decompile / "MegaCrit.Sts2.Core.Hooks" / "Hook.cs"
    hl = read_lines(hp)
    private_bodies = {}
    for i, l in enumerate(hl):
        m = re.match(r"^\tprivate static [\w<>\[\],.? ()]+? (\w+)\(", l)
        if m:
            private_bodies[m.group(1)] = "\n".join(hl[i:body_end(hl, i) + 1])
    seen: Counter = Counter()
    for st in public_statics(hp):
        name = st["name"]
        seen[name] += 1
        if seen[name] > 1:
            continue
        body = "\n".join(hl[st["line"] - 1:st["end"]])
        # Hook の private の下請け（ModifyDamage → 内部の集計など）を 1 段だけたどる
        for helper in set(re.findall(r"\b(\w+)\(", body)) & set(private_bodies) - {name, "IterateCombatHookListeners"}:
            body += "\n" + private_bodies[helper]
        dispatch = []
        for v in re.findall(r"\.(\w+)\(", body):
            if v in virtuals and v not in dispatch:
                dispatch.append(v)
        lst = []
        if "IterateCombatHookListeners(" in body:
            lst.append("combat")
        if "combatState.IterateHookListeners()" in body:
            lst.append("combat_all")
        if "runState.IterateHookListeners(" in body:
            lst.append("run")
        notify = st["sig"].startswith(("public static async Task ", "public static Task ")) and \
            re.match(r"(Before|After)", name) is not None
        cat = "hook" if notify else "computed"
        overriders = set().union(*(ms.overrides.get(d, set()) for d in dispatch)) if dispatch else set()
        sel = set().union(*(ms.override_selects.get(d, set()) for d in dispatch)) if dispatch else set()
        scope, ic, other = scope_of(overriders, ms)
        items.append({
            "key": f"{cat}:Hook.{name}", "category": cat, "name": f"Hook.{name}",
            "source": rel(decompile, hp, st["line"]),
            "dispatch": dispatch, "listeners": lst,
            "choice_context": "PlayerChoiceContext" in st["sig"],
            "scope_auto": scope, "ironclad_refs": ic, "other_refs": other,
            "ironclad_selects": sorted(n for n in sel if ms.pool.get(n) in IRONCLAD_SCOPES),
            "callers": sorted(callers.get(f"Hook.{name}", set())),
        })

    # --- Commands ---
    cmd_dir = decompile / "MegaCrit.Sts2.Core.Commands"
    for p in sorted(cmd_dir.glob("*Cmd.cs")):
        cls = p.stem
        seen = Counter()
        for st in public_statics(p):
            seen[st["name"]] += 1
        done = set()
        for st in public_statics(p):
            if st["name"] in done:
                continue
            done.add(st["name"])
            full = f"{cls}.{st['name']}"
            refs = ms.calls.get(full, set())
            scope, ic, other = scope_of(refs, ms)
            lines = read_lines(p)
            body = "\n".join(lines[st["line"] - 1:st["end"]])
            cat = "select" if cls in CMD_SELECT else "cmd"
            items.append({
                "key": f"{cat}:{full}", "category": cat, "name": full,
                "source": rel(decompile, p, st["line"]), "overloads": seen[st["name"]],
                "scope_auto": scope, "ironclad_refs": ic, "other_refs": other,
                "hooks_called": sorted(set(re.findall(r"\bHook\.(\w+)\(", body))),
                "history_called": sorted(set(re.findall(r"\bHistory\.(\w+)\(", body))),
                "choice_context": "PlayerChoiceContext" in st["sig"],
                "callers": sorted(callers.get(full, set())),
            })

    # --- CardSelectorPrefs ---
    pp = decompile / "MegaCrit.Sts2.Core.CardSelection" / "CardSelectorPrefs.cs"
    for i, l in enumerate(read_lines(pp)):
        m = PROP_RE.match(l)
        if m and " static " not in l:
            items.append({"key": f"select:CardSelectorPrefs.{m.group(2)}", "category": "select",
                          "name": f"CardSelectorPrefs.{m.group(2)}", "source": rel(decompile, pp, i + 1),
                          "scope_auto": "core", "type": m.group(1)})

    # --- CombatHistory ---
    hpth = decompile / "MegaCrit.Sts2.Core.Combat.History" / "CombatHistory.cs"
    for i, l in enumerate(read_lines(hpth)):
        m = re.match(r"^\tpublic void (\w+)\(", l)
        if m:
            full = f"CombatHistory.{m.group(1)}"
            items.append({"key": f"history:{full}", "category": "history", "name": full,
                          "source": rel(decompile, hpth, i + 1), "scope_auto": "core",
                          "callers": sorted(callers.get(full, set()))})
    edir = decompile / "MegaCrit.Sts2.Core.Combat.History.Entries"
    for p in sorted(edir.glob("*Entry.cs")):
        props = [m.group(2) for l in read_lines(p) if (m := PROP_RE.match(l)) and m.group(2) != "Description"]
        items.append({"key": f"history:{p.stem}", "category": "history", "name": p.stem,
                      "source": rel(decompile, p, 1), "scope_auto": "core", "fields": props})

    # --- Factories ---
    fdir = decompile / "MegaCrit.Sts2.Core.Factories"
    for p in sorted(fdir.glob("*Factory.cs")):
        done = set()
        for st in public_statics(p):
            if st["name"] in done:
                continue
            done.add(st["name"])
            full = f"{p.stem}.{st['name']}"
            refs = ms.calls.get(full, set())
            scope, ic, other = scope_of(refs, ms)
            items.append({"key": f"computed:{full}", "category": "computed", "name": full,
                          "source": rel(decompile, p, st["line"]), "scope_auto": scope,
                          "ironclad_refs": ic, "other_refs": other,
                          "callers": sorted(callers.get(full, set()))})

    # --- CombatManager の段階 ---
    cmp = decompile / "MegaCrit.Sts2.Core.Combat" / "CombatManager.cs"
    cml = read_lines(cmp)
    for i, l in enumerate(cml):
        m = re.match(r"^\t(?:public|private|internal) (?:static )?(?:async )?[\w<>\[\],.? ()]+? (\w+)\(", l)
        if not m:
            continue
        end = body_end(cml, i)
        body = "\n".join(cml[i:end + 1])
        seq = []
        for cls, meth in re.findall(r"\b(Hook|CardPileCmd|CardCmd|PowerCmd|CreatureCmd|PlayerCmd)\.(\w+)\(", body):
            s = f"{cls}.{meth}"
            if s not in seq:
                seq.append(s)
        if not seq:
            continue
        key = f"phase:CombatManager.{m.group(1)}"
        if any(it["key"] == key for it in items):
            continue
        items.append({"key": key, "category": "phase", "name": f"CombatManager.{m.group(1)}",
                      "source": rel(decompile, cmp, i + 1), "scope_auto": "core", "sequence": seq})

    # --- 乱数の系列 ---
    for p in [decompile / "MegaCrit.Sts2.Core.Runs" / "RunRngSet.cs",
              decompile / "MegaCrit.Sts2.Core.Random" / "PlayerRngSet.cs"]:
        if not p.exists():
            continue
        for i, l in enumerate(read_lines(p)):
            m = re.match(r"^\tpublic Rng (\w+) ", l)
            if m:
                items.append({"key": f"rng:{p.stem}.{m.group(1)}", "category": "rng",
                              "name": f"{p.stem}.{m.group(1)}", "source": rel(decompile, p, i + 1),
                              "scope_auto": "core"})
    return items


def add_manual_items(items: list[dict], manual: dict, decompile: Path) -> list[str]:
    """分類表の manual_items を候補に足す。grep で存在を確かめ、見つからなければ stale として返す。"""
    missing = []
    for key, spec in manual.get("manual_items", {}).items():
        path = decompile / spec["file"]
        lines = read_lines(path) if path.exists() else []
        hit = next((i for i, l in enumerate(lines) if spec["grep"] in l), None)
        if hit is None:
            missing.append(key)
            continue
        items.append({"key": key, "category": "manual", "name": key.split(":", 1)[1],
                      "source": rel(decompile, path, hit + 1), "scope_auto": spec.get("scope", "core")})
    return missing


# ---------------------------------------------------------------------------
# MOD の参照
# ---------------------------------------------------------------------------

def load_mod(mod_dir: Path) -> dict[str, list[str]]:
    return {p.name: read_lines(p) for p in sorted(mod_dir.glob("*.cs")) if p.name not in EXCLUDED_MOD_FILES}


def mod_refs(mod: dict[str, list[str]], item: dict) -> list[str]:
    name = item["name"]
    cls, _, meth = name.rpartition(".")
    pats = []
    if item["category"] in ("hook", "computed") and cls == "Hook":
        pats.append(re.compile(rf"\bHook\.{meth}\b"))
        for d in item.get("dispatch", []):
            pats.append(re.compile(rf"\"{d}\""))
    elif cls:
        pats.append(re.compile(rf"\b{re.escape(cls)}\.{meth}\b"))
        pats.append(re.compile(rf"nameof\({re.escape(cls)}\.{meth}\)"))
        if cls in CMD_SELECT:
            pats.append(re.compile(rf"\(\"{meth}\","))
    else:
        pats.append(re.compile(rf"\b{re.escape(name)}\b"))
    out = []
    for f, lines in mod.items():
        for i, l in enumerate(lines):
            if l.lstrip().startswith("//"):
                continue
            if any(p.search(l) for p in pats):
                out.append(f"{f}:{i + 1}")
    return out


def cite_line(mod: dict[str, list[str]], cite: str) -> str | None:
    """"<file>: <文字列>" を file:line にする。見つからなければ None。"""
    f, _, needle = cite.partition(":")
    needle = needle.strip()
    lines = mod.get(f.strip())
    if lines is None or not needle:
        return None
    for i, l in enumerate(lines):
        if needle in l:
            return f"{f.strip()}:{i + 1}"
    return None


# ---------------------------------------------------------------------------
# 分類
# ---------------------------------------------------------------------------

def classify(items: list[dict], manual: dict, mod: dict[str, list[str]]) -> tuple[list[str], list[str]]:
    rules = [(re.compile(r["match"]), r) for r in manual.get("rules", [])]
    explicit = manual.get("items", {})
    keys = {it["key"] for it in items}
    stale = sorted(k for k in explicit if k not in keys)
    errors = []
    for it in items:
        it["mod_refs"] = mod_refs(mod, it)
        c = explicit.get(it["key"])
        origin = "item"
        if c is None:
            for rx, r in rules:
                if rx.search(it["key"]) and (("scope_auto" not in r) or r["scope_auto"] == it["scope_auto"]):
                    c, origin = r, f"rule:{r['id']}"
                    break
        if c is None:
            it["class"] = "unclassified"
            continue
        it["class"] = c["class"]
        it["origin"] = origin
        for k in ("reason", "visible", "proposed_key", "where", "in_progress", "exposed_by", "issues",
                  "scope", "order", "decision"):
            if k in c:
                it[k] = c[k]
        if it["class"] not in CLASSES:
            errors.append(f"{it['key']}: 分類が不正 {it['class']}")
        if it["class"] == "exposed":
            cites = it.get("exposed_by")
            if not cites:
                errors.append(f"{it['key']}: exposed なのに exposed_by が無い")
            else:
                for cite in (cites if isinstance(cites, list) else [cites]):
                    loc = cite_line(mod, cite)
                    if loc is None:
                        errors.append(f"{it['key']}: exposed_by の引用切れ {cite!r}")
                    it.setdefault("exposed_at", []).append(loc or "?")
        if it["class"] == "public-missing" and not (it.get("proposed_key") and it.get("where")):
            errors.append(f"{it['key']}: public-missing なのに proposed_key / where が無い")
        it["scope_final"] = it.get("scope") or ("out_of_scope" if it["class"] == "out_of_scope" else
                                                ("other-only" if it["scope_auto"] == "other-only" else "ironclad"))
    return stale, errors


# ---------------------------------------------------------------------------
# 出力
# ---------------------------------------------------------------------------

def short(xs: list[str], n: int = 6) -> str:
    xs = list(xs)
    if not xs:
        return ""
    return ", ".join(xs[:n]) + (f" …(+{len(xs) - n})" if len(xs) > n else "")


def md_escape(s: str) -> str:
    return str(s).replace("|", "\\|").replace("\n", " ")


def render_tsv(items: list[dict]) -> str:
    cols = ["key", "class", "origin", "scope_auto", "source", "n_ironclad_refs", "n_other_refs",
            "choice_context", "ironclad_selects", "mod_refs"]
    out = ["\t".join(cols)]
    for it in sorted(items, key=lambda x: x["key"]):
        out.append("\t".join([
            it["key"], it.get("class", ""), it.get("origin", ""), it.get("scope_auto", ""), it["source"],
            str(len(it.get("ironclad_refs", []))), str(len(it.get("other_refs", []))),
            "1" if it.get("choice_context") else "",
            ",".join(it.get("ironclad_selects", [])),
            ",".join(it.get("mod_refs", [])[:5]),
        ]))
    return "\n".join(out) + "\n"


def issue_links(xs) -> str:
    return " ".join(f"sts2#{x}" for x in xs) if xs else ""


def render_md(items: list[dict], manual: dict, version: str, decompile: Path, stale: list[str]) -> str:
    L: list[str] = []
    w = L.append
    w("# 出来事の順番と選択の文脈の棚卸し（event inventory）")
    w("")
    w("> 生成物。`tools/event_inventory.py` が decompile と `docs/event-inventory-classification.json` から作る。手で書き換えない。")
    w(f"> ゲーム {version}（decompile `{decompile.name}`）。MOD の参照は作業ツリーの *.cs から機械的に集める。")
    w("")
    w(manual.get("preamble", "").rstrip())
    w("")
    # 件数
    w("## 件数（カテゴリ × 分類）")
    w("")
    cols = CLASSES + ["unclassified"]
    w("| カテゴリ | " + " | ".join(cols) + " | 計 |")
    w("|---|" + "---:|" * (len(cols) + 1))
    tot = Counter()
    for cat in CATEGORIES:
        c = Counter(it["class"] for it in items if it["category"] == cat)
        if not c:
            continue
        tot.update(c)
        w(f"| {CATEGORY_LABEL[cat]} | " + " | ".join(str(c.get(k, 0)) for k in cols) + f" | {sum(c.values())} |")
    w("| **計** | " + " | ".join(f"**{tot.get(k, 0)}**" for k in cols) + f" | **{sum(tot.values())}** |")
    w("")
    pm = [it for it in items if it["class"] == "public-missing"]
    pm_ic = [it for it in pm if it["scope_final"] == "ironclad"]
    prog = [it for it in pm_ic if it.get("in_progress")]
    w(f"public-missing は {len(pm)} 件、うちアイアンクラッドの範囲は {len(pm_ic)} 件"
      f"（進行中のブランチ `entropy-stratagem-stages` が扱うもの {len(prog)} 件を含む）。")
    w("")
    w("スコープ（自動）の内訳: " + ", ".join(f"{k} {v}" for k, v in sorted(Counter(it['scope_auto'] for it in items).items())))
    w("")
    if stale:
        w("**stale（分類表にあって今の版に無い）:** " + ", ".join(f"`{s}`" for s in stale))
        w("")
    unc = [it for it in items if it["class"] == "unclassified"]
    if unc:
        w("**unclassified:** " + ", ".join(f"`{it['key']}`" for it in unc))
        w("")

    # public-missing の表
    w("## public-missing（プレイヤーに見えるが MOD が出していないもの）")
    w("")
    w("「出す先」は phase 2 の MOD PR での置き場所の提案。「Issue」はそれが揃うと進められる sts2 の open Issue。")
    w("「IC の参照」はアイアンクラッドの範囲で上書き・呼び出しをするモデルの数（例を最大 4 つ）。")
    w("")
    w("| キー | 見えるもの | 提案キー | 出す先 | Issue | IC の参照 | 選択が開くモデル | 出典 |")
    w("|---|---|---|---|---|---|---|---|")
    def sort_key(it):
        return (-len(it.get("issues", [])), CATEGORIES.index(it["category"]), it["key"])
    for it in sorted(pm, key=sort_key):
        tag = " **進行中（" + it["in_progress"] + "）**" if it.get("in_progress") else ""
        oos = "" if it["scope_final"] == "ironclad" else f"（{it['scope_final']}）"
        refs = it.get("ironclad_refs", [])
        w(f"| `{it['key']}`{oos} | {md_escape(it.get('visible', it.get('reason', '')))}{tag} | `{md_escape(it['proposed_key'])}` "
          f"| {md_escape(it['where'])} | {issue_links(it.get('issues', []))} "
          f"| {len(refs)}{(' ' + short(refs, 4)) if refs else ''} | {short(it.get('ironclad_selects', []), 4)} | {it['source']} |")
    w("")

    # Issue ごとの集計
    by_issue = defaultdict(list)
    for it in pm:
        for n in it.get("issues", []):
            by_issue[n].append(it["key"])
    if by_issue:
        w("### Issue ごとの public-missing")
        w("")
        w("| Issue | 件数 | キー |")
        w("|---|---:|---|")
        titles = manual.get("issue_titles", {})
        for n, ks in sorted(by_issue.items(), key=lambda kv: (-len(kv[1]), kv[0])):
            t = titles.get(str(n), "")
            w(f"| sts2#{n} {md_escape(t)} | {len(ks)} | " + ", ".join(f"`{k}`" for k in ks) + " |")
        w("")

    if manual.get("phase2"):
        w(manual["phase2"].rstrip())
        w("")
    # exposed
    w("## exposed（MOD が出しているもの）")
    w("")
    w("| キー | JSON キー | MOD の箇所 |")
    w("|---|---|---|")
    for it in sorted((x for x in items if x["class"] == "exposed"), key=lambda x: x["key"]):
        w(f"| `{it['key']}` | {md_escape(it.get('proposed_key') or it.get('reason', ''))} | {', '.join(it.get('exposed_at', []))} |")
    w("")

    # hidden / cosmetic / internal / out_of_scope は理由ごとにまとめる
    for cls, title in [("hidden", "hidden（プレイヤーに見えないもの）"), ("cosmetic", "cosmetic（表示だけのもの）"),
                       ("internal", "internal（内部の都合）"), ("out_of_scope", "out_of_scope（アイアンクラッドの範囲外）")]:
        grp = defaultdict(list)
        for it in items:
            if it["class"] == cls:
                grp[it.get("reason", "")].append(it["key"])
        if not grp:
            continue
        w(f"## {title}")
        w("")
        w("| 理由 | 件数 | キー |")
        w("|---|---:|---|")
        for reason, ks in sorted(grp.items(), key=lambda kv: (-len(kv[1]), kv[0])):
            w(f"| {md_escape(reason)} | {len(ks)} | " + ", ".join(f"`{k}`" for k in sorted(ks)) + " |")
        w("")

    # 通知の一覧（機械の情報）
    w("## 付録: 通知と計算の hook の機械的な情報")
    w("")
    w("listener: combat = `IterateCombatHookListeners`（終戦中は空）、combat_all = `ICombatState.IterateHookListeners()`、"
      "run = `IRunState.IterateHookListeners`。ctx = PlayerChoiceContext を受け取る（中で選択画面が開きうる）。")
    w("")
    w("| キー | 分類 | 分配先 | listener | ctx | IC 上書き | 他キャラ上書き | 呼び出し元 |")
    w("|---|---|---|---|---|---:|---:|---|")
    for it in sorted((x for x in items if x["category"] in ("hook", "computed") and x["name"].startswith("Hook.")),
                     key=lambda x: x["key"]):
        w(f"| `{it['key']}` | {it['class']} | {', '.join(it.get('dispatch', []))} | {', '.join(it.get('listeners', []))} "
          f"| {'✓' if it.get('choice_context') else ''} | {len(it.get('ironclad_refs', []))} | {len(it.get('other_refs', []))} "
          f"| {short(it.get('callers', []), 4)} |")
    w("")
    w("## 付録: 手番の段階（CombatManager）の呼び出し順")
    w("")
    w("| メソッド | 分類 | 呼び出し（出現順・重複除く） |")
    w("|---|---|---|")
    for it in sorted((x for x in items if x["category"] == "phase"), key=lambda x: int(x["source"].rsplit(":", 1)[1])):
        w(f"| `{it['name']}` ({it['source'].split(':')[-1]}) | {it['class']} | {', '.join(it.get('sequence', []))} |")
    w("")
    return "\n".join(L).rstrip() + "\n"


# ---------------------------------------------------------------------------

def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--decompile", type=Path, required=True)
    ap.add_argument("--mod", type=Path, default=ROOT)
    ap.add_argument("--game-version", default="未確認")
    ap.add_argument("--classification", type=Path, default=DEFAULT_CLASSIFICATION)
    ap.add_argument("--out-tsv", type=Path, default=DEFAULT_TSV)
    ap.add_argument("--out-md", type=Path, default=DEFAULT_MD)
    ap.add_argument("--check", action="store_true", help="書かずに生成し直して比べる")
    ap.add_argument("--list-unclassified", action="store_true")
    ap.add_argument("--dump", action="store_true", help="候補の機械の情報を JSON で標準出力に出す")
    a = ap.parse_args()
    decompile = a.decompile.expanduser()
    manual = json.loads(a.classification.read_text(encoding="utf-8")) if a.classification.exists() else {}
    ms = scan_models(decompile)
    callers = global_callers(decompile)
    items = collect_items(decompile, ms, callers)
    missing_manual = add_manual_items(items, manual, decompile)
    mod = load_mod(a.mod)
    stale, errors = classify(items, manual, mod)
    stale += [f"{k}（manual_items の grep が外れた）" for k in missing_manual]
    if a.dump:
        json.dump(items, sys.stdout, ensure_ascii=False, indent=1)
        return 0
    unc = [it for it in items if it["class"] == "unclassified"]
    if a.list_unclassified:
        for it in unc:
            print(it["key"], it["scope_auto"], it["source"], "IC=" + short(it.get("ironclad_refs", []), 5),
                  "sel=" + short(it.get("ironclad_selects", []), 3), "mod=" + short(it["mod_refs"], 3), sep="\t")
        return 0
    md = render_md(items, manual, a.game_version, decompile, stale)
    tsv = render_tsv(items)
    rc = 0
    for e in errors:
        print("error:", e, file=sys.stderr)
        rc = 1
    if stale:
        print("stale:", ", ".join(stale), file=sys.stderr)
        rc = 1
    if unc:
        print(f"unclassified: {len(unc)} 件（--list-unclassified で一覧）", file=sys.stderr)
        rc = 1
    if a.check:
        for p, text in ((a.out_md, md), (a.out_tsv, tsv)):
            if not p.exists() or p.read_text(encoding="utf-8") != text:
                print(f"差分: {p} が生成し直した内容と違う", file=sys.stderr)
                rc = 1
        return rc
    a.out_md.write_text(md, encoding="utf-8")
    a.out_tsv.write_text(tsv, encoding="utf-8")
    c = Counter(it["class"] for it in items)
    print(f"{len(items)} 件: " + ", ".join(f"{k} {v}" for k, v in sorted(c.items())), file=sys.stderr)
    return rc


if __name__ == "__main__":
    sys.exit(main())
