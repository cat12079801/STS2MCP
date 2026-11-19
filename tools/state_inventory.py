#!/usr/bin/env python3
"""ゲームのモデルクラスが持つ内部状態の棚卸し（state inventory）を作る。

使い方（リポジトリのルートから）:

    /Users/anshinin/workspace/repos/sts2/bin/sts2-python -B tools/state_inventory.py \
        --decompile ~/.local/state/sts2/decompile/sts2-a671e9ef \
        --mod . --game-version v0.113.1

入力:
  --decompile  ILSpy の出力（ゲーム本体 sts2.dll の -p 展開）。ディレクトリ名 sts2-<sha8> を版の印に使う。
  --mod        MOD のソース（ルートの *.cs）。どの内部状態がすでに state に出ているかを機械的に検出する。
  docs/state-inventory-classification.json
               人の判断（分類・根拠・提案キー）。キーは "Class.member"（Data の欄は "Power.Data.field"）。

出力:
  docs/state-inventory-members.tsv  全候補の一覧（版の差分を取るための機械向けの表。行は安定に並ぶ）
  docs/state-inventory.md           要約（種別×分類の件数、public の未公開の表、unknown、cosmetic）

分類が無い候補は unclassified として数え、--list-unclassified で書き込み・読み取りのメソッド付きで出す。
分類表にあるが今の版に無い候補は stale として報告する（版が上がって消えた・名前が変わった印）。
分類表の pool は、参照元から機械的に決めた範囲を上書きする（基底クラスの欄でも他キャラ専用の仕組みなど）。

標準ライブラリだけを使う。C# の構文解析はしない。ILSpy の出力の書式（タブの字下げ・1 行 1 宣言）に頼った
行単位の抽出で、外れたら --list-unparsed で落ちた行を確かめる。
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from pathlib import Path

# ---------------------------------------------------------------------------
# 対象
# ---------------------------------------------------------------------------

MODEL_NAMESPACES = {
    "Monsters": "monster",
    "Relics": "relic",
    "Powers": "power",
    "Cards": "card",
    "Potions": "potion",
    "Enchantments": "enchantment",
    "Afflictions": "affliction",
    "Orbs": "orb",
}

# 基底クラス（名前空間ディレクトリ → ファイル名）。family はこれを継ぐ具象クラスの種別。
BASE_CLASS_FILES = [
    ("MegaCrit.Sts2.Core.Models", "AbstractModel", "base"),
    ("MegaCrit.Sts2.Core.Models", "MonsterModel", "monster"),
    ("MegaCrit.Sts2.Core.Models", "RelicModel", "relic"),
    ("MegaCrit.Sts2.Core.Models", "PowerModel", "power"),
    ("MegaCrit.Sts2.Core.Models", "CardModel", "card"),
    ("MegaCrit.Sts2.Core.Models", "PotionModel", "potion"),
    ("MegaCrit.Sts2.Core.Models", "EnchantmentModel", "enchantment"),
    ("MegaCrit.Sts2.Core.Models", "AfflictionModel", "affliction"),
    ("MegaCrit.Sts2.Core.Models", "OrbModel", "orb"),
    ("MegaCrit.Sts2.Core.Entities.Creatures", "Creature", "entity"),
    ("MegaCrit.Sts2.Core.Entities.Players", "Player", "entity"),
    ("MegaCrit.Sts2.Core.Entities.Players", "PlayerCombatState", "entity"),
    ("MegaCrit.Sts2.Core.Entities.Players", "ExtraPlayerFields", "entity"),
    ("MegaCrit.Sts2.Core.Combat", "CombatState", "entity"),
    ("MegaCrit.Sts2.Core.Entities.Cards", "CardEnergyCost", "entity"),
    ("MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine", "MonsterMoveStateMachine", "entity"),
]
BASE_CLASS_NAMES = {name for _, name, _ in BASE_CLASS_FILES}

POOL_NAMESPACES = {
    "card": "MegaCrit.Sts2.Core.Models.CardPools",
    "relic": "MegaCrit.Sts2.Core.Models.RelicPools",
    "potion": "MegaCrit.Sts2.Core.Models.PotionPools",
}

# プール名（ファイル名から Pool を除いたもの）→ スコープの区分
POOL_SCOPE = {
    "Ironclad": "ironclad",
    "Colorless": "colorless",
    "Curse": "curse",
    "Status": "status",
    "Event": "event-only",
    "Quest": "event-only",
    "Shared": "shared",
    "Fallback": "shared",
    "Token": None,  # 生成物。参照元から決める
    "Silent": "other-character",
    "Defect": "other-character",
    "Regent": "other-character",
    "Necrobinder": "other-character",
    "Fool": "other-character",
    "Deprived": "other-character",
    "Deprecated": "deprecated",
    "Mock": "mock",
}
IN_SCOPE_POOLS = {"ironclad", "colorless", "curse", "status", "event-only", "shared", "ancient",
                  "monster", "enchantment", "affliction", "base", "entity"}

ACCESS = r"(?:public|private|protected|internal)"
MODIFIER_WORDS = {"public", "private", "protected", "internal", "readonly", "volatile", "new",
                  "required", "sealed", "override", "virtual", "abstract", "static", "const",
                  "extern", "unsafe", "partial", "async", "event"}

CLASS_RE = re.compile(
    r"^(?P<indent>\t*)(?:(?:public|private|protected|internal|sealed|abstract|static|partial|file|readonly)\s+)*"
    r"(?P<kw>class|struct|record|interface|enum)\s+(?P<name>\w+)(?:<[^>]*>)?(?:\s*:\s*(?P<bases>[^{]+))?\s*$")
FIELD_RE = re.compile(
    r"^(?P<mods>(?:(?:public|private|protected|internal|readonly|volatile|new|required)\s+)+)"
    r"(?P<type>[\w.<>\[\],?() ]+?)\s+(?P<name>\w+)\s*(?:=\s*(?P<init>.*))?;$")
AUTO_PROP_RE = re.compile(
    r"^(?P<mods>(?:(?:public|private|protected|internal|new|required|virtual|override|sealed|abstract)\s+)+)"
    r"(?P<type>[\w.<>\[\],?() ]+?)\s+(?P<name>\w+)\s*\{\s*(?:(?:public|private|protected|internal)\s+)?get;"
    r"\s*(?P<setter>(?:(?:public|private|protected|internal)\s+)*(?:set|init);)?\s*\}(?:\s*=\s*(?P<init>.*);)?$")
PROP_HEAD_RE = re.compile(
    r"^(?P<mods>(?:(?:public|private|protected|internal|new|virtual|override|sealed|abstract|static)\s+)+)"
    r"(?P<type>[\w.<>\[\],?() ]+?)\s+(?P<name>\w+)\s*$")
METHOD_HEAD_RE = re.compile(r"^(?:[\w.<>\[\],?() ]+?\s+)?(?P<name>[\w.]+|operator\s*\S+)\s*(?:<[^>]*>)?\s*\(")
EXPR_MEMBER_RE = re.compile(r"^(?P<head>[^=(]*?\b(?P<name>\w+))\s*=>")
ATTR_RE = re.compile(r"^\[(?P<attr>[\w.]+)")

WRITE_SUFFIX = r"\s*(?:=(?!=)|\+=|-=|\*=|/=|%=|\|=|&=|\^=|\?\?=|<<=|>>=|\+\+|--)"
COLLECTION_MUTATORS = ("Add", "AddRange", "Remove", "RemoveAll", "RemoveAt", "RemoveWhere", "Clear",
                       "Insert", "Push", "Pop", "Enqueue", "Dequeue", "TryAdd", "Set", "UnionWith",
                       "ExceptWith", "Sort", "Reverse")

# 機械的に決めてよい分類（判断を要しないもの）。分類表があればそちらが勝つ。
HIDDEN_TYPE_RE = re.compile(r"\b(?:Rng|RunRngSet|PlayerRngSet|Random)\b")
COSMETIC_TYPE_RE = re.compile(
    r"\b(?:Node\w*|N[A-Z]\w*|Tween|Callable|Texture\w*|Vector2|Color|Sprite\w*|AnimationPlayer|"
    r"MegaSprite|MegaSkeleton\w*|Material|Shader\w*|SfxCmd|LocString|IHoverTip)\b")
COSMETIC_NAME_RE = re.compile(r"(?i)(?:sfx|vfx|anim|skin|iconpath|bigicon|loc[a-z]*string|hovertip|speech|bark|tween)")


@dataclass
class ClassInfo:
    name: str
    family: str
    file: Path
    bases: list[str]
    namespace: str
    is_abstract: bool
    outer: str | None = None  # Data などの入れ子の外側
    members: list["Member"] = field(default_factory=list)
    spans: list[tuple[int, int, str]] = field(default_factory=list)  # (start, end, member name)
    prop_backing: dict[str, str] = field(default_factory=dict)  # field -> property
    prop_info: dict[str, dict] = field(default_factory=dict)


@dataclass
class Member:
    owner: str          # "Class" or "Power.Data"
    name: str
    ctype: str
    kind: str           # field / auto_property / getonly_auto_property / data_field / dynamic_var_mutation
    access: str
    line: int
    family: str
    declared_on: str    # 宣言したクラス（中間の基底なら具象とは別）
    attrs: list[str] = field(default_factory=list)
    wrapper: str | None = None
    wrapper_access: str | None = None
    saved: bool = False
    readonly: bool = False
    init: str | None = None
    writers: set[str] = field(default_factory=set)
    readers: set[str] = field(default_factory=set)
    external: set[str] = field(default_factory=set)

    @property
    def key(self) -> str:
        return f"{self.owner}.{self.name}"


# ---------------------------------------------------------------------------
# 抽出
# ---------------------------------------------------------------------------

def strip_line(raw: str) -> str:
    s = raw.rstrip("\n")
    return s


def code_only(s: str) -> str:
    """文字列・文字のリテラルとコメントを空白に置き換える（参照の検出用）。"""
    out = []
    i = 0
    n = len(s)
    while i < n:
        c = s[i]
        if c == "/" and i + 1 < n and s[i + 1] == "/":
            break
        if c == '"' or (c == "@" and i + 1 < n and s[i + 1] == '"') or (c == "$" and i + 1 < n and s[i + 1] == '"'):
            j = i + (1 if c == '"' else 2)
            while j < n and s[j] != '"':
                if s[j] == "\\":
                    j += 1
                j += 1
            out.append('""')
            i = j + 1
            continue
        if c == "'":
            j = i + 1
            while j < n and s[j] != "'":
                if s[j] == "\\":
                    j += 1
                j += 1
            out.append("' '")
            i = j + 1
            continue
        out.append(c)
        i += 1
    return "".join(out)


def indent_of(s: str) -> int:
    return len(s) - len(s.lstrip("\t"))


def parse_file(path: Path, family: str, namespace: str) -> list[ClassInfo]:
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    classes: list[ClassInfo] = []
    # まずクラスの範囲を字下げで決める
    regions = []  # (cls, start_body, end_body, indent)
    for i, raw in enumerate(lines):
        m = CLASS_RE.match(raw)
        if not m or m.group("kw") not in ("class", "record", "struct"):
            continue
        ind = len(m.group("indent"))
        # 次の行が "{"
        if i + 1 >= len(lines) or lines[i + 1].strip() != "{" or indent_of(lines[i + 1]) != ind:
            continue
        end = None
        for j in range(i + 2, len(lines)):
            if lines[j].strip() == "}" and indent_of(lines[j]) == ind:
                end = j
                break
        if end is None:
            continue
        bases = [b.strip().split("<")[0].split(".")[-1] for b in (m.group("bases") or "").split(",") if b.strip()]
        head = raw.strip()
        regions.append((m.group("name"), i + 2, end, ind, bases, "abstract " in head))
    region_by_name = {}
    for name, s, e, ind, bases, is_abs in regions:
        outer = None
        for oname, os_, oe, oind, _, _ in regions:
            if oind < ind and os_ <= s and e <= oe:
                outer = oname
        ci = ClassInfo(name=name if outer is None else f"{outer}.{name}", family=family, file=path,
                       bases=bases, namespace=namespace, is_abstract=is_abs, outer=outer)
        classes.append(ci)
        region_by_name[ci.name] = (s, e, ind)
    for ci in classes:
        s, e, ind = region_by_name[ci.name]
        parse_class_body(ci, lines, s, e, ind + 1, region_by_name)
    return classes


def parse_class_body(ci: ClassInfo, lines: list[str], start: int, end: int, mind: int, regions) -> None:
    nested = [(s, e) for n, (s, e, ind) in regions.items() if ind == mind and start <= s and e <= end]
    attrs: list[str] = []
    i = start
    while i < end:
        raw = lines[i]
        if any(s - 2 <= i <= e for s, e in nested):
            i += 1
            continue
        if not raw.strip() or indent_of(raw) != mind:
            i += 1
            continue
        s = raw.strip()
        am = ATTR_RE.match(s)
        if am and s.endswith("]"):
            attrs.append(am.group("attr"))
            i += 1
            continue
        cur_attrs, attrs = attrs, []
        # 本体付きの宣言の範囲を取る
        body_end = i
        if i + 1 < end and lines[i + 1].strip() == "{" and indent_of(lines[i + 1]) == mind:
            for j in range(i + 2, end + 1):
                if lines[j].strip().startswith("}") and indent_of(lines[j]) == mind:
                    body_end = j
                    break
        elif not s.endswith(";") and not s.endswith("}"):
            # 複数行の式（=> の続き）など。字下げが深い行が続く間
            j = i + 1
            while j < end and (not lines[j].strip() or indent_of(lines[j]) > mind):
                j += 1
            body_end = j - 1
        words = set(re.findall(r"\b\w+\b", s.split("=")[0].split("(")[0]))
        is_static = "static" in words or "const" in words
        if "event" in words:
            i = body_end + 1
            continue
        fm = FIELD_RE.match(s) if "=>" not in s.split(";")[0].split("{")[0] else None
        if fm and ({"override", "virtual", "abstract"} & set(fm.group("type").split())):
            fm = None
        if fm and "(" not in s.split("=")[0] and body_end == i:
            if not is_static:
                mods = fm.group("mods").split()
                ci.members.append(Member(
                    owner=ci.name, name=fm.group("name"), ctype=fm.group("type").strip(),
                    kind="data_field" if ci.outer else "field",
                    access=next((w for w in mods if w in ("public", "private", "protected", "internal")), "private"),
                    line=i + 1, family=ci.family, declared_on=ci.name, attrs=cur_attrs,
                    saved="SavedProperty" in cur_attrs, readonly="readonly" in mods,
                    init=(fm.group("init") or None)))
            ci.spans.append((i, i, fm.group("name")))
            i += 1
            continue
        pm = AUTO_PROP_RE.match(s)
        if pm:
            mods = pm.group("mods").split()
            name = pm.group("name")
            if not is_static and not ({"virtual", "override", "abstract"} & set(mods) and not pm.group("setter")):
                ci.members.append(Member(
                    owner=ci.name, name=name, ctype=pm.group("type").strip(),
                    kind="auto_property" if pm.group("setter") else "getonly_auto_property",
                    access=next((w for w in mods if w in ("public", "private", "protected", "internal")), "private"),
                    line=i + 1, family=ci.family, declared_on=ci.name, attrs=cur_attrs,
                    saved="SavedProperty" in cur_attrs, init=pm.group("init")))
            ci.spans.append((i, i, name))
            i += 1
            continue
        hm = PROP_HEAD_RE.match(s)
        if hm and body_end > i:
            name = hm.group("name")
            body = [code_only(x) for x in lines[i + 2:body_end]]
            backing = None
            joined = "\n".join(body)
            gm = re.search(r"\bget\b[\s\S]*?return\s+(_\w+)\s*;", joined) or re.search(r"\bget\s*=>\s*(_\w+)\s*;", joined)
            if gm:
                backing = gm.group(1)
            setter_access = None
            sm = re.search(r"(?:(public|private|protected|internal)\s+)?\bset\b", joined)
            if sm:
                setter_access = sm.group(1) or next((w for w in hm.group("mods").split()
                                                      if w in ("public", "private", "protected", "internal")), "private")
            if backing and re.search(rf"\b{re.escape(backing)}\s*=\s*value\b", joined):
                ci.prop_backing[backing] = name
            elif backing:
                ci.prop_backing.setdefault(backing, name)
            ci.prop_info[name] = {
                "access": next((w for w in hm.group("mods").split() if w in ("public", "private", "protected", "internal")), "private"),
                "setter": setter_access, "saved": "SavedProperty" in cur_attrs, "static": is_static}
            ci.spans.append((i, body_end, name))
            i = body_end + 1
            continue
        # メソッド・式本体のプロパティ・コンストラクタ
        name = None
        em = EXPR_MEMBER_RE.match(s)
        if em and "(" not in em.group("head"):
            name = em.group("name")
        else:
            mm = METHOD_HEAD_RE.match(re.sub(r"^(?:(?:%s)\s+)+" % "|".join(MODIFIER_WORDS), "", s))
            if mm:
                name = mm.group("name").split(".")[-1]
            elif em:
                name = em.group("name")
        ci.spans.append((i, body_end, name or f"<line{i + 1}>"))
        i = body_end + 1


def enclosing(ci: ClassInfo, line_idx: int) -> str | None:
    for s, e, name in ci.spans:
        if s <= line_idx <= e:
            return name
    return None


_CODE_CACHE: dict[int, list[str]] = {}


def code_lines(lines: list[str]) -> list[str]:
    key = id(lines)
    if key not in _CODE_CACHE:
        _CODE_CACHE[key] = [code_only(x) for x in lines]
    return _CODE_CACHE[key]


def find_refs(ci: ClassInfo, outer: ClassInfo | None, lines: list[str], m: Member) -> None:
    """同じファイルの中の参照を、書き込み・読み取りに分けてメソッド名で集める。"""
    names = [m.name]
    if m.wrapper:
        names.append(m.wrapper)
    host = outer if outer is not None else ci
    codes = code_lines(lines)
    for nm in names:
        if m.kind == "data_field":
            pat = re.compile(rf"\.{re.escape(nm)}\b")
        elif nm.startswith("_") or (nm == m.wrapper and ci.family not in ("base", "entity") and ci.name not in BASE_CLASS_NAMES):
            # private フィールド（_x）は別インスタンス越し（clone._x = ...）にも書かれ、
            # 包みのプロパティはキャスト越し（((TheShot)card).X）にも読まれる
            pat = re.compile(rf"\b{re.escape(nm)}\b")
        else:
            pat = re.compile(rf"(?<![\w.])(?:this\.|base\.)?{re.escape(nm)}\b|(?:this|base)\.{re.escape(nm)}\b")
        for idx, code in enumerate(codes):
            if nm not in code:
                continue
            for mt in pat.finditer(code):
                where = enclosing(host, idx)
                if where is None:
                    continue
                if where in (m.name, m.wrapper) and m.kind != "data_field":
                    continue
                rest = code[mt.end():]
                before = code[:mt.start()]
                is_write = bool(re.match(WRITE_SUFFIX, rest)) or bool(re.search(r"(\+\+|--)\s*(?:this\.|base\.)?$", before))
                if not is_write:
                    cm = re.match(r"\s*\.\s*(\w+)\s*\(", rest)
                    if cm and cm.group(1) in COLLECTION_MUTATORS:
                        is_write = True
                    elif re.match(r"\s*\[[^\]]*\]" + WRITE_SUFFIX, rest):
                        is_write = True
                (m.writers if is_write else m.readers).add(where)


def find_dynamic_var_mutations(ci: ClassInfo, lines: list[str]) -> list[Member]:
    """実行時に DynamicVars の BaseValue を書き換える箇所（OnUpgrade 以外）。"""
    found: dict[str, Member] = {}
    pat = re.compile(r"DynamicVars(?:\.(\w+)|\[\"(\w+)\"\])\.BaseValue" + WRITE_SUFFIX)
    for idx, raw in enumerate(lines):
        for mt in pat.finditer(raw):
            var = mt.group(1) or mt.group(2)
            where = enclosing(ci, idx)
            if where is None or where in ("OnUpgrade",):
                continue
            mem = found.get(var)
            if mem is None:
                mem = found[var] = Member(owner=ci.name, name=f"DynamicVars.{var}", ctype="DynamicVar.BaseValue",
                                          kind="dynamic_var_mutation", access="public", line=idx + 1,
                                          family=ci.family, declared_on=ci.name)
            mem.writers.add(where)
    return list(found.values())


def load_pools(root: Path) -> dict[str, dict[str, str]]:
    out: dict[str, dict[str, str]] = {"card": {}, "relic": {}, "potion": {}}
    for fam, ns in POOL_NAMESPACES.items():
        d = root / ns
        if not d.is_dir():
            continue
        for f in sorted(d.glob("*.cs")):
            pool = re.sub(r"(Card|Relic|Potion)Pool$", "", f.stem)
            text = f.read_text(encoding="utf-8-sig")
            for mt in re.finditer(r"ModelDb\.(?:Card|Relic|Potion)<(\w+)>\(\)", text):
                out[fam].setdefault(mt.group(1), pool)
    return out


def simple_name(name: str) -> str:
    """Name[family] の修飾を外す（ゲームの型名）。"""
    return re.sub(r"\[\w+\]", "", name)


def collect(decompile: Path):
    classes: dict[str, ClassInfo] = {}
    lines_cache: dict[Path, list[str]] = {}
    for ns_dir, name, fam in BASE_CLASS_FILES:
        p = decompile / ns_dir / f"{name}.cs"
        if not p.exists():
            print(f"warning: 基底クラスのファイルが無い: {p}", file=sys.stderr)
            continue
        for ci in parse_file(p, fam if name in BASE_CLASS_NAMES and fam != "entity" else fam, ns_dir):
            ci.family = fam
            classes[ci.name] = ci
            lines_cache[p] = p.read_text(encoding="utf-8-sig").splitlines()
    parsed: list[ClassInfo] = []
    for sub, fam in MODEL_NAMESPACES.items():
        d = decompile / f"MegaCrit.Sts2.Core.Models.{sub}"
        for p in sorted(d.glob("*.cs")):
            parsed.extend(parse_file(p, fam, d.name))
            lines_cache[p] = p.read_text(encoding="utf-8-sig").splitlines()
    # 種別をまたいで同名のクラス（ペットのレリックと同名のモンスターなど）は Name[family] にする
    name_count = Counter(ci.name for ci in parsed if ci.outer is None)
    for ci in parsed:
        top = ci.outer or ci.name
        if name_count[top] > 1:
            qual = f"{top}[{ci.family}]"
            ci.name = qual if ci.outer is None else f"{qual}.{ci.name.split('.', 1)[1]}"
            if ci.outer:
                ci.outer = qual
            for m in ci.members:
                m.owner = ci.name
                m.declared_on = ci.name
        if ci.name not in classes:
            classes[ci.name] = ci
    # 参照を集める
    members: list[Member] = []
    for ci in classes.values():
        lines = lines_cache[ci.file]
        outer = classes.get(ci.outer) if ci.outer else None
        for m in ci.members:
            if m.kind != "data_field" and m.name in ci.prop_backing:
                m.wrapper = ci.prop_backing[m.name]
                info = ci.prop_info.get(m.wrapper, {})
                m.wrapper_access = info.get("access")
                if info.get("saved"):
                    m.saved = True
            find_refs(ci, outer, lines, m)
            members.append(m)
        if ci.outer is None and ci.family not in ("entity", "base"):
            members.extend(find_dynamic_var_mutations(ci, lines))
    return classes, members, lines_cache


def build_token_index(decompile: Path) -> dict[str, set[str]]:
    """モデル・エンティティ全体で `.Name` の形で参照される名前 → 参照するファイル（クラス名）。"""
    idx: dict[str, set[str]] = defaultdict(set)
    dirs = [d for d in decompile.iterdir() if d.is_dir() and d.name.startswith("MegaCrit.Sts2.Core.")
            and not d.name.startswith("MegaCrit.Sts2.Core.Nodes")]
    for d in dirs:
        for p in d.glob("*.cs"):
            try:
                text = p.read_text(encoding="utf-8-sig")
            except OSError:
                continue
            for tok in set(re.findall(r"\.([A-Z]\w+)\b", text)):
                idx[tok].add(p.stem)
    return idx


# ---------------------------------------------------------------------------
# スコープ（プール）
# ---------------------------------------------------------------------------

def assign_pools(decompile: Path, classes: dict[str, ClassInfo]) -> dict[str, str]:
    pools = load_pools(decompile)
    pool_of: dict[str, str] = {}
    for ci in classes.values():
        if ci.outer:
            continue
        fam = ci.family
        if ci.name in BASE_CLASS_NAMES:
            pool_of[ci.name] = "base" if fam != "entity" else "entity"
        elif fam == "monster":
            pool_of[ci.name] = "monster"
        elif fam == "enchantment":
            pool_of[ci.name] = "enchantment"
        elif fam == "affliction":
            pool_of[ci.name] = "affliction"
        elif fam == "orb":
            pool_of[ci.name] = "other-character"
        elif fam in pools and simple_name(ci.name) in pools[fam]:
            sc = POOL_SCOPE.get(pools[fam][simple_name(ci.name)], "other")
            if sc:
                pool_of[ci.name] = sc
    # Mocks の名前空間
    for ci in classes.values():
        if ci.outer is None and ci.namespace.endswith(".Mocks"):
            pool_of[ci.name] = "mock"
    # 残り（パワー・トークンのカード・プール外のレリック等）は参照元から決める
    unresolved = [c for c in classes.values() if c.outer is None and c.name not in pool_of]
    want = {simple_name(c.name) for c in classes.values() if c.outer is None}
    refs: dict[str, set[str]] = defaultdict(set)
    events_dir = decompile / "MegaCrit.Sts2.Core.Models.Events"
    if events_dir.is_dir():
        for p in events_dir.glob("*.cs"):
            t = p.read_text(encoding="utf-8-sig")
            is_ancient = bool(re.search(r":\s*AncientEventModel\b", t))
            for tok in set(re.findall(r"\b([A-Z]\w+)\b", t)) & want:
                refs[tok].add("@ancient" if is_ancient else "@event")
    # イベントのプールのレリックのうち、エンシェント（AncientEventModel の派生）が出すものは ancient
    for ci in classes.values():
        if ci.outer is None and ci.family == "relic" and pool_of.get(ci.name) == "event-only" \
                and "@ancient" in refs.get(simple_name(ci.name), ()):
            pool_of[ci.name] = "ancient"
    referencing_dirs = [f"MegaCrit.Sts2.Core.Models.{s}" for s in MODEL_NAMESPACES]
    for d in referencing_dirs:
        for p in (decompile / d).glob("*.cs"):
            t = p.read_text(encoding="utf-8-sig")
            for tok in set(re.findall(r"\b([A-Z]\w+)\b", t)) & want:
                if tok != p.stem:
                    refs[tok].add(p.stem)
    # 反復で解決（パワー → それを付けるカード → プール）
    order = ["ironclad", "colorless", "curse", "status", "shared", "ancient", "event-only", "enchantment",
             "affliction", "monster", "other-character", "deprecated", "mock"]
    for _ in range(4):
        changed = False
        for c in unresolved:
            if c.name in pool_of:
                continue
            scopes = set()
            for r in refs.get(simple_name(c.name), ()):
                if r == "@ancient":
                    scopes.add("ancient")
                elif r == "@event":
                    scopes.add("event-only")
                elif r in pool_of:
                    scopes.add(pool_of[r])
            if scopes:
                pool_of[c.name] = next(s for s in order if s in scopes) if any(s in scopes for s in order) else sorted(scopes)[0]
                changed = True
        if not changed:
            break
    for c in unresolved:
        pool_of.setdefault(c.name, "unreferenced")
    return pool_of


def owner_pool(m: Member, classes: dict[str, ClassInfo], pool_of: dict[str, str]) -> str:
    top = m.owner.split(".")[0]
    return pool_of.get(top, "unknown")


# ---------------------------------------------------------------------------
# MOD の被覆
# ---------------------------------------------------------------------------

@dataclass
class ModSource:
    files: dict[str, list[str]]
    index: dict[str, list[tuple[str, int, str]]]  # 名前 → (file, line idx, "lit"|"acc")


def load_mod(mod_dir: Path) -> ModSource:
    files = {}
    index: dict[str, list[tuple[str, int, str]]] = defaultdict(list)
    for p in sorted(mod_dir.glob("*.cs")):
        lines = p.read_text(encoding="utf-8-sig").splitlines()
        files[p.name] = lines
        for idx, raw in enumerate(lines):
            st = raw.lstrip()
            if st.startswith("//"):
                continue
            for lit in set(re.findall(r'"(\w+)"', raw)):
                index[lit].append((p.name, idx, "lit"))
            for acc in set(re.findall(r"\.([A-Z]\w*)\b(?!\s*[(<])", code_only(raw))):
                index[acc].append((p.name, idx, "acc"))
    return ModSource(files, index)


CASE_RE = re.compile(r"^\s*case\s+(\w+)(?:\s+\w+)?\s*(?::|when\b)")


def block_classes(lines: list[str], idx: int) -> set[str]:
    """MOD の行 idx を含む switch の case ラベルと、同じ行の typeof/is/nameof の型。"""
    found = set(re.findall(r"(?:typeof|nameof)\((\w+)\)|\bis\s+(\w+)", lines[idx]) and
                [a or b for a, b in re.findall(r"(?:typeof|nameof)\((\w+)\)|\bis\s+(\w+)", lines[idx])])
    seen_case = False
    for j in range(idx, max(-1, idx - 60), -1):
        s = lines[j]
        cm = CASE_RE.match(s)
        if cm:
            found.add(cm.group(1))
            seen_case = True
            continue
        if seen_case:
            break
        st = s.strip()
        if j != idx and (st.startswith("break;") or st.startswith("switch") or st.startswith("default:")
                         or re.match(r"^(private|public|internal|protected)\s", st)):
            break
        for a, b in re.findall(r"(?:typeof|nameof)\((\w+)\)|\bis\s+(\w+)", s):
            found.add(a or b)
    return found


def mod_coverage(mod: ModSource, m: Member, classes: dict[str, ClassInfo]) -> list[str]:
    """MOD がこの候補を読んでいる箇所（file:line）。"""
    top = simple_name(m.owner.split(".")[0])
    names = [m.name] + ([m.wrapper] if m.wrapper else [])
    if m.kind == "dynamic_var_mutation":
        return []
    hits = []
    for nm in names:
        for fname, idx, how in mod.index.get(nm, ()):
            if how == "acc" and (m.kind == "data_field" or not nm[0].isupper()):
                continue
            bc = block_classes(mod.files[fname], idx)
            if top in bc or (top in BASE_CLASS_NAMES and (how == "acc" or not bc or top in bc)):
                hits.append(f"{fname}:{idx + 1}")
    return sorted(set(hits))


def ancestors(name: str, classes: dict[str, ClassInfo]) -> list[str]:
    out = []
    cur = classes.get(name)
    guard = 0
    while cur and guard < 10:
        guard += 1
        base = next((b for b in cur.bases if b in classes or b.endswith("Model")), None)
        if not base:
            break
        out.append(base)
        cur = classes.get(base)
    return out


def generic_dynamic_var_coverage(mod: ModSource) -> dict[str, str]:
    """DynamicVars を種別ごとに丸ごと出している箇所。"""
    out = {}
    pats = {
        "card": r"card\.DynamicVars",
        "relic": r"typeof\(RelicModel\), \"_dynamicVars\"",
        "power": r"typeof\(PowerModel\), \"_dynamicVars\"",
        "enchantment": r"typeof\(EnchantmentModel\), \"_dynamicVars\"",
        "potion": r"typeof\(PotionModel\), \"_dynamicVars\"|potion\.DynamicVars",
    }
    for fam, pat in pats.items():
        for fname, lines in mod.files.items():
            for idx, raw in enumerate(lines):
                if re.search(pat, raw) and not raw.lstrip().startswith("//"):
                    out.setdefault(fam, f"{fname}:{idx + 1}")
    return out


# ---------------------------------------------------------------------------
# 分類
# ---------------------------------------------------------------------------

CLASSES = ("exposed", "public", "hidden", "cosmetic", "static/derived", "unknown")


def auto_classify(m: Member) -> tuple[str, str] | None:
    if HIDDEN_TYPE_RE.search(m.ctype):
        return "hidden", "型が乱数（Rng 系）"
    if m.name == "_canonicalInstance":
        return "static/derived", "正規インスタンスへの参照（ModelDb の定義。インスタンスごとの状態ではない）"
    if COSMETIC_TYPE_RE.search(m.ctype) or COSMETIC_NAME_RE.search(m.name):
        return "cosmetic", "型・名前が表示用（ノード・文字列リソース・効果音）"
    return None


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--decompile", required=True, type=Path)
    ap.add_argument("--mod", required=True, type=Path)
    ap.add_argument("--game-version", default="未確認")
    ap.add_argument("--classification", type=Path, default=None)
    ap.add_argument("--out-tsv", type=Path, default=None)
    ap.add_argument("--out-md", type=Path, default=None)
    ap.add_argument("--list-unclassified", action="store_true",
                    help="分類の無い候補を、宣言・書き込み・読み取りのメソッド付きで標準出力に出す")
    ap.add_argument("--filter", default=None, help="--list-unclassified の対象を owner の正規表現で絞る")
    args = ap.parse_args()

    mod_dir = args.mod.resolve()
    cls_path = args.classification or mod_dir / "docs" / "state-inventory-classification.json"
    out_tsv = args.out_tsv or mod_dir / "docs" / "state-inventory-members.tsv"
    out_md = args.out_md or mod_dir / "docs" / "state-inventory.md"
    decompile = args.decompile.expanduser().resolve()

    classes, members, _ = collect(decompile)
    pool_of = assign_pools(decompile, classes)
    tok_index = build_token_index(decompile)
    mod = load_mod(mod_dir)
    generic_dv = generic_dynamic_var_coverage(mod)

    manual = {}
    if cls_path.exists():
        manual = json.loads(cls_path.read_text(encoding="utf-8"))
        manual.pop("_comment", None)

    rows = []
    for m in members:
        top = m.owner.split(".")[0]
        # 外部（別クラス）からの参照。公開の包みのプロパティ・公開フィールドだけ
        pub = m.wrapper if (m.wrapper and m.wrapper_access in ("public", "internal")) else (
            m.name if m.access in ("public", "internal") and m.kind != "data_field" else None)
        if pub and pub in tok_index:
            ext = sorted(x for x in tok_index[pub] if x != top and x not in (m.owner,))
            m.external = set(ext[:8]) | ({f"(+{len(ext) - 8})"} if len(ext) > 8 else set())
        cov = mod_coverage(mod, m, classes)
        if m.kind == "dynamic_var_mutation" and m.family in generic_dv:
            cov = [generic_dv[m.family] + "（DynamicVars を丸ごと出す）"]
        entry = manual.get(m.key)
        if entry:
            cls = entry.get("class", "unknown")
            source = "manual"
        elif cov:
            cls, source = "exposed", "mod-scan"
            entry = {"note": "MOD のソースで読み取りを検出"}
        else:
            ac = auto_classify(m)
            if ac:
                cls, source = ac[0], "rule"
                entry = {"note": ac[1]}
            else:
                cls, source = "unclassified", ""
                entry = {}
        if cls not in CLASSES and cls != "unclassified":
            print(f"warning: 不明な分類 {cls!r}（{m.key}）", file=sys.stderr)
        rows.append({
            "key": m.key, "owner": m.owner, "member": m.name, "type": m.ctype, "kind": m.kind,
            "access": m.access, "wrapper": m.wrapper or "", "saved": m.saved,
            "declared_on": m.declared_on, "family": m.family,
            "pool": (entry or {}).get("pool") or owner_pool(m, classes, pool_of),
            "line": f"{classes[top].file.name}:{m.line}",
            "writers": sorted(m.writers), "readers": sorted(m.readers), "external": sorted(m.external),
            "coverage": cov, "class": cls, "source": source,
            "note": entry.get("note", ""), "json_key": entry.get("json_key", ""),
            "effect": entry.get("effect", ""), "written": entry.get("written", ""),
        })
    rows.sort(key=lambda r: (r["family"], r["owner"], r["member"]))
    stale = sorted(set(manual) - {r["key"] for r in rows})

    if args.list_unclassified:
        flt = re.compile(args.filter) if args.filter else None
        for r in rows:
            if r["class"] != "unclassified" or (flt and not flt.search(r["owner"])):
                continue
            print(f"{r['key']}\t{r['type']}\t{r['kind']}\t{r['pool']}\tW={','.join(r['writers'])}\t"
                  f"R={','.join(r['readers'])}\tX={','.join(r['external'])}\t{r['line']}")
        print(f"# unclassified: {sum(1 for r in rows if r['class'] == 'unclassified')}", file=sys.stderr)
        return 0

    write_tsv(out_tsv, rows)
    write_md(out_md, rows, stale, decompile, args.game_version, mod_dir)
    counts = Counter(r["class"] for r in rows)
    print(f"候補 {len(rows)} 件: " + ", ".join(f"{k} {v}" for k, v in sorted(counts.items())))
    if stale:
        print(f"stale（分類表にあるが今の版に無い）: {len(stale)} 件", file=sys.stderr)
    return 0


def write_tsv(path: Path, rows: list[dict]) -> None:
    cols = ["key", "family", "pool", "kind", "type", "access", "wrapper", "saved", "declared_on", "line",
            "class", "source", "coverage", "writers", "readers", "external", "json_key"]
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as f:
        f.write("\t".join(cols) + "\n")
        for r in rows:
            vals = []
            for c in cols:
                v = r[c]
                if isinstance(v, list):
                    v = ",".join(v)
                vals.append(str(v).replace("\t", " ").replace("\n", " "))
            f.write("\t".join(vals) + "\n")


def md_escape(s: str) -> str:
    return str(s).replace("|", "\\|").replace("\n", " ")


def write_md(path: Path, rows: list[dict], stale: list[str], decompile: Path, version: str, mod_dir: Path) -> None:
    fams = ["monster", "relic", "power", "card", "potion", "enchantment", "affliction", "orb", "base", "entity"]
    cls_cols = list(CLASSES) + ["unclassified"]
    out = []
    out.append("# ゲームの内部状態の棚卸し（state inventory）\n")
    out.append("> この文書は `tools/state_inventory.py` が生成する。手で書き換えない。"
               "分類は `docs/state-inventory-classification.json` に書き、生成し直す。\n")
    out.append(f"- ゲームの版: **{version}**（デコンパイル `{decompile.name}`）")
    out.append(f"- 全候補の機械向けの表: [`state-inventory-members.tsv`](state-inventory-members.tsv)")
    out.append("- 生成: `bin/sts2-python -B tools/state_inventory.py --decompile <dir> --mod . --game-version <版>`\n")
    out.append("## 分類の意味\n")
    out.append("| 分類 | 意味 |\n|---|---|")
    out.append("| exposed | すでに MOD の state に出ている（MOD のソースで読み取りを検出、または分類表で確認） |")
    out.append("| public | 人間が見える・見たものから確定できる。まだ出ていない（出す対象） |")
    out.append("| hidden | 人間に見えない（乱数・未公開の山の順・まだ見せていない生成物）。出さない |")
    out.append("| cosmetic | 戦闘・進行に効かない（台詞・演出・UI）。出す必要は無い |")
    out.append("| static/derived | 定数、または出ている値・アセンション・強化から決まる。出す必要は無い |")
    out.append("| unknown | ソースからは決めきれない（理由を備考に書く） |")
    out.append("| unclassified | 分類表に無い（版が上がって増えた候補など） |\n")
    out.append("種別（kind）: `field` = インスタンスのフィールド、`auto_property` = 自動実装プロパティ（setter あり）、"
               "`getonly_auto_property` = get だけの自動実装プロパティ、`data_field` = パワーの入れ子 `Data` の欄、"
               "`dynamic_var_mutation` = 実行時に `DynamicVars.<名前>.BaseValue` を書き換える箇所（強化 `OnUpgrade` を除く）。\n")

    out.append("## 件数（種別 × 分類）\n")
    out.append("| 種別 | " + " | ".join(cls_cols) + " | 計 |")
    out.append("|---|" + "---:|" * (len(cls_cols) + 1))
    tot = Counter()
    for fam in fams:
        c = Counter(r["class"] for r in rows if r["family"] == fam)
        if not c:
            continue
        tot.update(c)
        out.append(f"| {fam} | " + " | ".join(str(c.get(k, 0)) for k in cls_cols) + f" | {sum(c.values())} |")
    out.append("| **計** | " + " | ".join(str(tot.get(k, 0)) for k in cls_cols) + f" | {sum(tot.values())} |\n")

    def in_scope(r):
        return r["pool"] in IN_SCOPE_POOLS

    out.append("### アイアンクラッドの範囲だけ（他キャラ・廃止・モックを除く）\n")
    out.append("| 種別 | " + " | ".join(cls_cols) + " | 計 |")
    out.append("|---|" + "---:|" * (len(cls_cols) + 1))
    tot = Counter()
    for fam in fams:
        c = Counter(r["class"] for r in rows if r["family"] == fam and in_scope(r))
        if not c:
            continue
        tot.update(c)
        out.append(f"| {fam} | " + " | ".join(str(c.get(k, 0)) for k in cls_cols) + f" | {sum(c.values())} |")
    out.append("| **計** | " + " | ".join(str(tot.get(k, 0)) for k in cls_cols) + f" | {sum(tot.values())} |\n")
    out.append("範囲（pool）: `ironclad`・`colorless`・`curse`・`status`・`event-only`・`shared`・`ancient`・`monster`・"
               "`enchantment`・`affliction`・`base`（モデルの基底）・`entity`（Creature・Player など）を範囲に入れる。"
               "`other-character`・`deprecated`・`mock`・`unreferenced` は範囲外（一覧には残す）。"
               "パワー・トークンのカード・プール外のレリックは、参照するクラスのプールから決める（アイアンクラッドの札・共有のレリックが付けるならその範囲）。\n")

    def table(title, sel, cols, intro=""):
        rs = [r for r in rows if sel(r)]
        out.append(f"## {title}（{len(rs)} 件）\n")
        if intro:
            out.append(intro + "\n")
        if not rs:
            out.append("（なし）\n")
            return
        out.append("| " + " | ".join(h for h, _ in cols) + " |")
        out.append("|" + "---|" * len(cols))
        for r in rs:
            out.append("| " + " | ".join(md_escape(f(r)) for _, f in cols) + " |")
        out.append("")

    def methods(xs, limit=6):
        xs = list(xs)
        return ", ".join(xs[:limit]) + (f" …(+{len(xs) - limit})" if len(xs) > limit else "")

    pub_cols = [
        ("クラス.メンバー", lambda r: f"`{r['key']}`"),
        ("型", lambda r: r["type"]),
        ("kind", lambda r: r["kind"] + (" · saved" if r["saved"] else "")),
        ("範囲", lambda r: r["pool"]),
        ("書き込み", lambda r: r["written"] or methods(r["writers"])),
        ("効果（読む側）", lambda r: r["effect"] or methods(r["readers"] + r["external"])),
        ("提案キー", lambda r: f"`{r['json_key']}`" if r["json_key"] else ""),
        ("備考", lambda r: r["note"]),
    ]
    table("public（まだ出ていない・出す対象）— アイアンクラッドの範囲", lambda r: r["class"] == "public" and in_scope(r), pub_cols,
          "書き込み・読む側の列は、分類表に記述があればそれを、無ければ同じファイルの中で検出したメソッド名（と別クラスからの参照）を出す。")
    table("public — 範囲外（他キャラ・廃止）", lambda r: r["class"] == "public" and not in_scope(r), pub_cols)
    table("unknown（ソースから決めきれない）", lambda r: r["class"] == "unknown", [
        ("クラス.メンバー", lambda r: f"`{r['key']}`"), ("型", lambda r: r["type"]), ("範囲", lambda r: r["pool"]),
        ("書き込み", lambda r: methods(r["writers"])), ("読む側", lambda r: methods(r["readers"] + r["external"])),
        ("理由", lambda r: r["note"])])
    table("unclassified（分類表に無い）", lambda r: r["class"] == "unclassified", [
        ("クラス.メンバー", lambda r: f"`{r['key']}`"), ("型", lambda r: r["type"]), ("kind", lambda r: r["kind"]),
        ("範囲", lambda r: r["pool"]), ("書き込み", lambda r: methods(r["writers"])),
        ("読む側", lambda r: methods(r["readers"] + r["external"]))])
    table("hidden（出さない）", lambda r: r["class"] == "hidden", [
        ("クラス.メンバー", lambda r: f"`{r['key']}`"), ("型", lambda r: r["type"]), ("範囲", lambda r: r["pool"]),
        ("理由", lambda r: r["note"])])
    out.append("## cosmetic（効果なし。engine は効果なしとして扱ってよい）\n")
    cos = [r for r in rows if r["class"] == "cosmetic"]
    out.append(f"{len(cos)} 件。型・名前の規則で決めたもの（ノード・効果音・アイコンの path など）は `source=rule`、"
               "判断したものは `source=manual`。全件は TSV。判断したものだけ列挙する。\n")
    for r in cos:
        if r["source"] == "manual":
            out.append(f"- `{r['key']}`（{md_escape(r['type'])}）: {md_escape(r['note'])}")
    out.append("")
    table("exposed（出ている）", lambda r: r["class"] == "exposed", [
        ("クラス.メンバー", lambda r: f"`{r['key']}`"), ("範囲", lambda r: r["pool"]),
        ("MOD の箇所", lambda r: ", ".join(r["coverage"][:3]) + (" …" if len(r["coverage"]) > 3 else "") or r["note"]),
        ("判定", lambda r: r["source"])])
    table("static/derived（出す必要が無い）", lambda r: r["class"] == "static/derived", [
        ("クラス.メンバー", lambda r: f"`{r['key']}`"), ("型", lambda r: r["type"]), ("範囲", lambda r: r["pool"]),
        ("何から決まるか", lambda r: r["note"])])
    if stale:
        out.append(f"## stale（分類表にあるが今の版に無い。{len(stale)} 件）\n")
        for k in stale:
            out.append(f"- `{k}`")
        out.append("")
    path.write_text("\n".join(out) + "\n", encoding="utf-8")


if __name__ == "__main__":
    sys.exit(main())
