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
  McpMod.StateAllowlist.Generated.cs
                                    --write-allowlist で、分類表の section または reader_key を持つ public 項目から生成する。
                                    section の項目は internal_state、reader_key の項目は専用 reader が公開する。
                                    --check-allowlist は生成し直した内容と一致しなければ終了コード 1。
                                    この 2 つだけなら --decompile は要らない。棚卸しを回すと、許可リストの
                                    項目が今の版の宣言（型の完全名・C# の型・種別）と合うかも確かめ、
                                    合わなければ終了コード 2。
  --check-docs                      md・tsv を書かずに生成し直して比べ、違えば終了コード 3（--decompile が要る）。
                                    MOD の *.cs を直すと被覆の file:line が動くので、PR の前に回す。

分類が無い候補は unclassified として数え、--list-unclassified で書き込み・読み取りのメソッド付きで出す。

基底クラスのメンバーの被覆（exposed）は、MOD の読み取りがすべての派生の行に届くときだけ数える。MOD の行から
外側のブロックの見出しを字下げでたどり、型の判定を集める（case の型パターン〔A or B・プロパティパターン・when〕・
is / is not / !(x is T)・as・typeof・GetType() != typeof(T)・switch 式の腕・複数行の条件の継続行・else〔前の if の否定〕・
同じ深さの前の文の見張り〔if (…) return; と { … return; }〕・default が抜ける switch の後ろ）。見出しがメソッドなら
呼び出し元ごとにたどる。McpMod のメソッドは修飾なし・McpMod. の呼び出しとメソッドグループの参照だけを数え
（x.Name() は別の型のメンバー。拡張メソッドは数える）、入れ子の型のメソッドは型の中の呼び出しと x.Name(…) を数える。
数えられる呼び出し元が無く x.Name(…) の参照だけがあるメソッドは、入口にせず「判定できない」とする
（[ModInitializer("…")] が指す入口は除く）。循環の経路は捨て、
入口に着かない経路（循環だけ・深さ SCOPE_DEPTH を超える）は「判定できない」として警告し partial に数える。
どの経路でも派生の型の判定に当たる読み取りは partial（派生の分岐の中だけ）として報告する。partial は、分類表の section（許可リストで全派生に出す）か、partial_reason（ほかの派生が要らない理由。
class は exposed）で解消する。解消していない partial、または当てはまらない partial_reason があれば、棚卸しの実行と
--check-docs は終了コード 4 で終わる（文書は書く）。プロパティの読み取り（.Name）は、受け手の変数の宣言の型が
基底と無関係（別の型の同名のプロパティ）なら数えない。var の受け手は右辺（new T・キャスト・as T・別の変数と
そのプロパティ〔デコンパイルの宣言の型〕）から型を引く。using・namespace の行は読み取りに数えない。
型でない条件（id の文字列・可視性など）での出し分け、同じ名前の多重定義の取り違え、state に出さない読み取り
（操作の検査など）は見分けない。
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
import tempfile
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
    ("MegaCrit.Sts2.Core.Runs", "RunState", "entity"),
    ("MegaCrit.Sts2.Core.Entities.Cards", "CardEnergyCost", "entity"),
    ("MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine", "MonsterMoveStateMachine", "entity"),
]
BASE_CLASS_NAMES = {name for _, name, _ in BASE_CLASS_FILES}
# ランの棚卸しは、パエルの歯の Deck 追加通知に関わる修飾子列を対象とする。
BASE_MEMBER_SCOPE = {"RunState": {"Modifiers"}}

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
    prop_types: dict[str, str] = field(default_factory=dict)  # プロパティ名 -> 宣言の型（MOD の受け手の型を引く）


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
            ci.prop_types[name] = hm.group("type").strip()
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
            head = re.sub(r"<[^<>]*(?:<[^<>]*>[^<>]*)*>", "", em.group("head")).split()
            if len(head) >= 2 and head[-2] not in MODIFIER_WORDS:
                ci.prop_types[name] = head[-2]
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
            if name in BASE_MEMBER_SCOPE:
                ci.members = [m for m in ci.members if m.name in BASE_MEMBER_SCOPE[name]]
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


USING_RE = re.compile(r"^\s*(?:global\s+)?(?:using\s+(?:static\s+)?(?:\w+\s*=\s*)?[\w.<>, ]+|namespace\s+[\w.]+)\s*;")


def load_mod(mod_dir: Path) -> ModSource:
    files = {}
    index: dict[str, list[tuple[str, int, str]]] = defaultdict(list)
    for p in sorted(mod_dir.glob("*.cs")):
        if p.name in ALLOWLIST_SOURCE_FILES:
            # 生成した許可リストとその読み取り・整形は、被覆を分類表から決める。
            # 文字列・プロパティの一致では数えない（SerializableCard.Enchantment などを基底の項目と取り違える）。
            continue
        lines = p.read_text(encoding="utf-8-sig").splitlines()
        files[p.name] = lines
        for idx, raw in enumerate(lines):
            st = raw.lstrip()
            if st.startswith("//") or USING_RE.match(raw):
                # using の行（using MegaCrit.Sts2.Core.Localization.DynamicVars; の .DynamicVars）は読み取りではない
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


# --- 読み取りの範囲（どの型の行で読むか） -------------------------------------------------
#
# 基底クラスのメンバーを MOD が読んでいても、その読み取りが特定の派生だけの分岐（case HexPower:・
# if (x is HexPower)・その分岐からしか呼ばれない補助メソッド）の中なら、ほかの派生の行には出ていない。
# MOD の行から外側へ（字下げで）ブロックの見出しをたどり、型の判定を集める。見出しがメソッドなら、
# その呼び出し元ごとに同じことをする。どの呼び出しの経路でも派生の判定に当たるなら partial。

TYPE_TEST_RE = re.compile(r"\bis\s+((?:not\s+)?[A-Z]\w*(?:\s*\{[^{}]*\})?(?:\s+(?:or|and)\s+(?:not\s+)?[A-Z]\w*(?:\s*\{[^{}]*\})?)*)"
                          r"|\bas\s+([A-Z]\w*)|\btypeof\(([A-Z]\w*)\)")
# 否定の判定: !(x is T)・x.GetType() != typeof(T)・typeof(T) != x.GetType()
NEG_TEST_RE = re.compile(r"!\s*\(\s*[\w.?\[\]]+\s+is\s+([A-Z]\w*)[^()]*\)"
                         r"|GetType\(\)\s*!=\s*typeof\(([A-Z]\w*)\)|typeof\(([A-Z]\w*)\)\s*!=\s*[\w.?]+\.GetType\(\)")
SWITCH_ARM_RE = re.compile(r"^\s*((?:not\s+)?[A-Z]\w*(?:\s*\{[^{}]*\})?(?:\s+[a-z_]\w*)?"
                           r"(?:\s+(?:or|and)\s+(?:not\s+)?[A-Z]\w*(?:\s*\{[^{}]*\})?)*)\s*(?:\bwhen\b.*)?=>")
CASE_LINE_RE = re.compile(r"^\s*case\s+(?P<pat>.+?)\s*(?:\bwhen\b.*)?:\s*$")
DEFAULT_RE = re.compile(r"^\s*default\s*:")
EXIT_RE = re.compile(r"^\s*(?:return\b|throw\b|continue\s*;|yield\s+break\b)")
NOT_HEADER_WORDS = {"return", "new", "await", "throw", "yield", "else", "case", "using", "lock", "foreach", "for",
                    "while", "if", "switch", "catch", "typeof", "nameof", "default", "var", "in", "is", "as", "when"}
TYPE_DECL_RE = re.compile(r"\b(?:class|struct|record|interface|enum)\s+\w+")
SCOPE_DEPTH = 40
UNDECIDABLE = "?"  # 呼び出し元を深さの上限までたどっても決まらない（判定できない）


def mod_indent(s: str) -> int:
    return len(s) - len(s.lstrip(" \t"))


def pattern_types(pattern: str) -> set[str]:
    """型パターン（`A or B`・`not A`・`A { X: 1 }`・`A a`）の型。否定は '!T'。"""
    out = set()
    pattern = re.sub(r"\{[^{}]*\}", " ", pattern)
    for piece in re.split(r"\b(?:or|and)\b", pattern):
        mt = re.match(r"\s*\(?\s*(not\s+)?([A-Z]\w*)", piece)
        if mt:
            out.add(("!" if mt.group(1) else "") + mt.group(2))
    return out


def type_tests(code: str) -> set[str]:
    """型の判定（is / is not / is A or B / as / typeof / !(x is T) / GetType() != typeof(T) / switch 式の腕）。否定は '!T'。"""
    out = set()
    for a, b, c in NEG_TEST_RE.findall(code):
        out.add("!" + (a or b or c))
    code = NEG_TEST_RE.sub(" ", code)
    for pat, b, c in TYPE_TEST_RE.findall(code):
        if pat:
            out.update(pattern_types(pat))
        elif b or c:
            out.add(b or c)
    am = SWITCH_ARM_RE.match(code)
    if am:
        out.update(pattern_types(am.group(1)))
    return out


def negate(labels: set[str]) -> set[str]:
    return {x[1:] if x.startswith("!") else "!" + x for x in labels}


def case_labels(line: str) -> set[str] | None:
    """case の行の型（when 句の型の判定も含む）。case の行でなければ None。"""
    cm = CASE_LINE_RE.match(line)
    if not cm:
        return None
    return pattern_types(cm.group("pat")) | type_tests(line[cm.end("pat"):])


def header_span(code: list[str], j: int, limit: int) -> tuple[str, int]:
    """j 行から始まる見出し（if / while / switch / case … when など）を、括弧が閉じるか && / || の継続が終わるまで
    続けた文字列と、その最後の行。limit 行（読み取りの行）は含めない。"""
    text, depth, m = "", 0, j
    if not re.match(r"^\s*(?:if|else\s+if|while|switch|case|foreach|for)\b", code[j]):
        return code[j], j  # ラムダを渡す呼び出しなどは 1 行だけ（括弧の中の本体は見出しではない）
    for k in range(j, min(limit, j + 12)):
        t = code[k]
        if k > j and (not t.strip() or t.strip().startswith("{")):
            break
        text += " " + t.strip()
        depth += t.count("(") - t.count(")")
        m = k
        nxt = next((code[n].strip() for n in range(k + 1, min(limit, k + 3)) if code[n].strip()), "")
        if depth <= 0 and not re.search(r"(?:&&|\|\||\bor|\band)\s*$", t.rstrip()) \
                and not nxt.startswith(("&&", "||", "or ", "and ")):
            break
    return text, m


def _exits(code: list[str], m: int, limit: int, tail: str) -> bool:
    """見出しの後ろ（同じ行の残り・次の 1 文・{ … } の最後の文）が return / throw / continue で終わるか。"""
    if re.search(r"\b(?:return|throw|continue)\b", tail):
        return True
    n = next((k for k in range(m + 1, limit) if code[k].strip()), None)
    if n is None:
        return False
    if code[n].strip() != "{":
        return bool(EXIT_RE.match(code[n])) and mod_indent(code[n]) > mod_indent(code[m])
    ind = mod_indent(code[n])
    close = next((k for k in range(n + 1, limit) if code[k].strip() == "}" and mod_indent(code[k]) == ind), None)
    if close is None:
        return False
    last = next((code[k] for k in range(close - 1, n, -1) if code[k].strip()), "")
    return bool(EXIT_RE.match(last))


def _guard(code: list[str], j: int, limit: int) -> set[str]:
    """同じ深さの前の文が「if (…) return;」の見張りなら、後ろの行に効く判定（見出しの判定の否定）。"""
    if not re.match(r"^\s*if\s*\(", code[j]):
        return set()
    text, m = header_span(code, j, limit)
    tests = type_tests(text)
    if not tests:
        return set()
    # 見出しの括弧の後ろ（同じ行の本体）
    depth, tail = 0, ""
    for i, ch in enumerate(text):
        depth += ch == "("
        depth -= ch == ")"
        if depth == 0 and ch == ")":
            tail = text[i + 1:]
            break
    return negate(tests) if _exits(code, m, limit, tail) else set()


def _switch_after(code: list[str], j: int, limit: int) -> set[str]:
    """同じ深さの前の文が switch なら、その後ろに届く節の判定（default が抜けるなら残りの case の型、
    抜ける case があればその否定）。"""
    if not re.match(r"^\s*switch\s*\(", code[j]):
        return set()
    _, m = header_span(code, j, limit)
    ind = mod_indent(code[j])
    open_ = next((k for k in range(m + 1, limit) if code[k].strip()), None)
    if open_ is None or code[open_].strip() != "{":
        return set()
    close = next((k for k in range(open_ + 1, limit) if code[k].strip() == "}" and mod_indent(code[k]) == ind), None)
    if close is None:
        return set()
    sections: list[tuple[set[str], bool, bool]] = []  # (型, default を含むか, 抜けるか)
    labels: set[str] = set()
    has_default, body = False, []
    lab_ind = None

    def flush():
        if labels or has_default:
            last = next((x for x in reversed(body) if x.strip()), "")
            sections.append((set(labels), has_default, bool(EXIT_RE.match(last))))

    for k in range(open_ + 1, close):
        t = code[k]
        if not t.strip():
            continue
        cl = case_labels(t)
        is_label = cl is not None or bool(DEFAULT_RE.match(t))
        if is_label and (lab_ind is None or mod_indent(t) == lab_ind):
            lab_ind = mod_indent(t)
            if body:
                flush()
                labels, has_default, body = set(), False, []
            if cl is not None:
                labels |= cl
            else:
                has_default = True
                rest = t.split(":", 1)[1]
                if rest.strip():
                    body.append(rest)
            continue
        body.append(t)
    flush()
    out: set[str] = set()
    for labs, dflt, exits in sections:
        if dflt and not exits:
            # default が抜けない: 後ろに届くのは抜けない節すべて（型の制限にならない）。抜ける case の否定だけ
            return {"!" + x for s_labs, s_dflt, s_exits in sections if s_exits and not s_dflt for x in s_labs
                    if not x.startswith("!")}
    if any(d for _, d, _ in sections):
        # default が抜ける: 後ろに届くのは抜けない case の型だけ
        for labs, dflt, exits in sections:
            if not exits and not dflt:
                out |= labs
        return out
    return {"!" + x for labs, _, exits in sections if exits for x in labs if not x.startswith("!")}


@dataclass
class ScopeIndex:
    mod: ModSource
    code: dict[str, list[str]]
    memo: dict[tuple[str, int], frozenset] = field(default_factory=dict)
    occurrences: dict[str, list[tuple[str, int]]] = field(default_factory=dict)
    prop_types: dict[tuple[str, str], str] = field(default_factory=dict)
    undecidable: set[str] = field(default_factory=set)
    method_memo: dict[tuple[str, int], frozenset] = field(default_factory=dict)
    entry_names: set[str] = field(default_factory=set)  # [ModInitializer("…")] が名前で指す入口


def build_scope_index(mod: ModSource) -> ScopeIndex:
    code = {f: [code_only(x) for x in lines] for f, lines in mod.files.items()}
    occ: dict[str, list[tuple[str, int]]] = defaultdict(list)
    for f, lines in code.items():
        for idx, s in enumerate(lines):
            for tok in set(re.findall(r"\b([A-Za-z_]\w*)\b", s)):
                occ[tok].append((f, idx))
    entries = {m for lines in mod.files.values() for line in lines
               for m in re.findall(r"\[\s*ModInitializer\s*\(\s*\"(\w+)\"\s*\)\s*\]", line)}
    return ScopeIndex(mod, code, occurrences=occ, entry_names=entries)


def _is_member_decl(s: str) -> str | None:
    """メソッド（ローカル関数を含む）の宣言の行なら名前。"""
    st = s.strip()
    if "(" not in st or '"' in st.split("(")[0] or "=" in st.split("(")[0]:
        return None
    head = re.sub(r"<[^<>]*(?:<[^<>]*>[^<>]*)*>", "", st.split("(")[0]).split()
    if len(head) < 2 or not re.fullmatch(r"[A-Za-z_]\w*", head[-1]):
        return None
    if head[0] in NOT_HEADER_WORDS or head[-1] in NOT_HEADER_WORDS:
        return None
    if {"class", "struct", "record", "interface"} & set(head):
        return None  # 主コンストラクタ付きの型の宣言（class X(Func<int> f)）はメソッドではない
    return head[-1]


def _local_scope(code: list[str], header: int) -> tuple[int, int]:
    """ローカル関数の見出しから、外側のメソッドの範囲（見出しの行, 閉じ括弧の行）。"""
    ind = mod_indent(code[header])
    for j in range(header - 1, -1, -1):
        s = code[j]
        if s.strip() and mod_indent(s) < ind and _is_member_decl(s):
            outer_ind = mod_indent(s)
            body = next((k for k in range(j + 1, len(code))
                         if code[k].strip() == "{" and mod_indent(code[k]) == outer_ind), None)
            if body is None:
                return j, len(code)
            for k in range(body + 1, len(code)):
                if code[k].strip() == "}" and mod_indent(code[k]) == outer_ind:
                    return j, k
            return j, len(code)
    return 0, len(code)


def _local_tests(code: list[str], idx: int) -> tuple[set[str], tuple[str, int] | None]:
    """行 idx から外側へ字下げでたどった型の判定と、たどり着いたメソッド（名前, 見出しの行）。"""
    local = set(type_tests(code[idx]))
    cur = mod_indent(code[idx]) if code[idx].strip() else 10 ** 6
    j = idx - 1
    while j >= 0:
        s = code[j]
        st = s.strip()
        if not st:
            j -= 1
            continue
        ind = mod_indent(s)
        if ind == cur:
            # 同じ深さの前の文: 見張り（if (…) return;）と、default・case が抜ける switch
            local |= _guard(code, j, idx)
            local |= _switch_after(code, j, idx)
        elif ind < cur:
            if st == "{":
                cur = ind + 1
                j -= 1
                continue
            if TYPE_DECL_RE.search(st) and not st.startswith(("if", "else", "case", "return", "var")):
                return local, None
            name = _is_member_decl(s)
            if name:
                return local, (name, j)
            if case_labels(s) is not None or DEFAULT_RE.match(s):
                # 同じ switch の節の見出し（連なる case）を集める。default は兄弟の case を否定で持つ
                labels: set[str] = set()
                is_default = False
                k = j
                while k >= 0 and code[k].strip() and mod_indent(code[k]) == ind and (
                        case_labels(code[k]) is not None or DEFAULT_RE.match(code[k])):
                    cl = case_labels(code[k])
                    if cl is None:
                        is_default = True
                    else:
                        labels |= cl
                    k -= 1
                if is_default:
                    for k2 in range(j - 1, -1, -1):
                        t = code[k2]
                        if t.strip() and mod_indent(t) < ind:
                            break
                        cl = case_labels(t)
                        if cl and mod_indent(t) == ind:
                            local |= {"!" + x for x in cl if not x.startswith("!")}
                else:
                    local |= labels
                j = k
                cur = ind
                continue
            if st.startswith("else"):
                # else / else if: 前の if・else if の判定の否定（複数行の条件も含める）
                for k in range(j - 1, -1, -1):
                    t = code[k]
                    if not t.strip():
                        continue
                    if mod_indent(t) < ind:
                        break
                    if mod_indent(t) == ind and re.match(r"^\s*(?:else\s+)?if\b", t):
                        local |= negate(type_tests(header_span(code, k, j)[0]))
                        if not t.strip().startswith("else"):
                            break
            local |= type_tests(header_span(code, j, idx)[0])
            cur = ind
        j -= 1
    return local, None


def scope_paths(sx: ScopeIndex, fname: str, idx: int, depth: int = 0, visiting: set | None = None) -> frozenset:
    """MOD の行 idx に届く経路ごとの型の判定の集合（frozenset of frozenset）。空の集合の経路は制限なし。

    呼び出し元が自分に戻る循環の経路は捨てる（制限なしにしない）。呼び出し元を SCOPE_DEPTH までたどっても
    入口（呼び出し元の無いメソッド）に着かない経路は UNDECIDABLE を持つ。"""
    top = visiting is None
    key = (fname, idx)
    if top and key in sx.memo:
        return sx.memo[key]
    code = sx.code[fname]
    local, method = _local_tests(code, idx)
    own = frozenset(local)
    if method is None:
        result = frozenset({own})
    else:
        callers = _caller_paths(sx, fname, method, depth, set() if visiting is None else visiting)
        result = frozenset(own | p for p in callers)
    if top:
        if not result:
            # 呼び出し元が循環だけ（外から呼ばれない）: 決められない
            result = frozenset({own | {UNDECIDABLE}})
        sx.memo[key] = result
    return result


def _container(code: list[str], header: int) -> tuple:
    """メソッドの見出しの外側: ("local", lo, hi)（ローカル関数）か ("type", 名前, lo, hi)（型のメンバー）。"""
    ind = mod_indent(code[header])
    for j in range(header - 1, -1, -1):
        t = code[j]
        if not t.strip() or mod_indent(t) >= ind or t.strip() == "{":
            continue
        tm = re.search(r"\b(?:class|struct|record|interface)\s+(\w+)", t)
        if tm:
            tind = mod_indent(t)
            body = next((k for k in range(j + 1, len(code)) if code[k].strip() == "{" and mod_indent(code[k]) == tind), j)
            close = next((k for k in range(body + 1, len(code)) if code[k].strip() == "}" and mod_indent(code[k]) == tind),
                         len(code))
            return ("type", tm.group(1), j, close)
        if _is_member_decl(t):
            lo, hi = _local_scope(code, header)
            return ("local", lo, hi)
    return ("type", "McpMod", 0, len(code))


def _caller_paths(sx: ScopeIndex, fname: str, method: tuple[str, int], depth: int, visiting: set) -> frozenset:
    """メソッドの呼び出し元ごとの経路（メソッド単位でまとめて覚える）。循環は空、深さの上限は UNDECIDABLE。

    循環を切った結果も覚えるので、循環の中のメソッドの経路は欠けうる。欠けるのは経路だけなので、
    全派生に届く経路を見落として partial にする側（安全側）にしか倒れない。"""
    name, header = method
    key = (fname, header)
    if key in sx.method_memo:
        return sx.method_memo[key]
    if key in visiting:
        return frozenset()
    if depth >= SCOPE_DEPTH:
        return frozenset({frozenset({UNDECIDABLE})})
    code = sx.code[fname]
    container = _container(code, header)
    esc = re.escape(name)
    member_ref = re.compile(rf"\.{esc}\b")
    if container[0] == "local":
        lo, hi = container[1], container[2]
        call = re.compile(rf"(?<![\w.]){esc}\b")
        sites = [(fname, k) for f, k in sx.occurrences.get(name, ()) if f == fname and lo <= k < hi]
    elif container[0] == "type" and container[1] != "McpMod":
        # 入れ子の型のメソッド: 型の中の修飾なしの呼び出しと、どこからでもの x.Name(…)・Type.Name(…)
        lo, hi = container[2], container[3]
        call = re.compile(rf"(?<![\w.]){esc}\b|\.{esc}\b")
        sites = [(f, k) for f, k in sx.occurrences.get(name, ())
                 if member_ref.search(sx.code[f][k]) or (f == fname and lo <= k < hi)]
    else:
        # McpMod のメソッド: 修飾なし・McpMod. の呼び出しとメソッドグループの参照（拡張メソッドは x.Name() も）
        extension = "(this " in code[header].replace("( this ", "(this ")
        call = re.compile(rf"(?:(?<![\w.])|\bMcpMod\.){esc}\b" + (rf"|\.{esc}\b" if extension else ""))
        sites = list(sx.occurrences.get(name, ()))
    nameof = re.compile(rf"nameof\(\s*{esc}\s*\)")
    refs = [(f, k) for f, k in sites if (f, k) != (fname, header) and not nameof.search(sx.code[f][k])]
    sites = [(f, k) for f, k in refs if call.search(sx.code[f][k])]
    is_entry = name in sx.entry_names and container[0] == "type" and container[1] == "McpMod"
    if not sites and not is_entry and any(
            member_ref.search(sx.code[f][k]) for f, k in sx.occurrences.get(name, ()) if (f, k) != (fname, header)):
        # 数えられる呼び出し元は無いが x.Name(…) の参照がある: 別の型のメンバーか、たどれない呼び出し。
        # 入口（制限なし）にせず、判定できないとする（安全側）
        sx.method_memo[key] = frozenset({frozenset({UNDECIDABLE})})
        return sx.method_memo[key]
    if not sites:
        result = frozenset({frozenset()})  # 入口（呼び出し元が無い）: 制限なし
    else:
        visiting.add(key)
        paths = set()
        for f, k in sites:
            paths |= scope_paths(sx, f, k, depth + 1, visiting)
        visiting.discard(key)
        result = frozenset(paths)
    sx.method_memo[key] = result
    return result


def descendant_names(classes: dict[str, ClassInfo]) -> dict[str, set[str]]:
    """基底の名前 → 派生（中間の抽象クラスを含む）と、派生だけが実装するインターフェースの名前。"""
    out: dict[str, set[str]] = defaultdict(set)
    for ci in classes.values():
        if ci.outer:
            continue
        anc = ancestors(ci.name, classes)
        for a in anc:
            out[a].add(simple_name(ci.name))
            out[a].update(b for b in ci.bases if re.match(r"^I[A-Z]", b))
    for name in list(out):
        own = set(classes[name].bases) if name in classes else set()
        for a in ancestors(name, classes):
            own.update(classes[a].bases if a in classes else ())
        out[name] -= own
    return out


def restricting(labels: frozenset, owner: str, desc: dict[str, set[str]]) -> set[str]:
    sub = desc.get(owner, set())
    return {x for x in labels if x == UNDECIDABLE or x.lstrip("!") in sub}


def _bare_type(t: str) -> str:
    t = re.sub(r"<.*>", "", t.strip()).rstrip("?!").strip()
    return t.split(".")[-1]


def _prop_type(sx: ScopeIndex, classes: dict[str, ClassInfo], typ: str, prop: str) -> str | None:
    """デコンパイルの宣言から、型 typ（とその基底）のプロパティ・フィールド prop の型。"""
    if not sx.prop_types:
        for ci in classes.values():
            for name, t in ci.prop_types.items():
                sx.prop_types.setdefault((simple_name(ci.name), name), t)
            for m in ci.members:
                if m.owner == ci.name:
                    sx.prop_types.setdefault((simple_name(ci.name), m.name), m.ctype)
        sx.prop_types.setdefault(("", ""), "")
    for c in [typ] + [simple_name(a) for a in ancestors(typ, classes)]:
        t = sx.prop_types.get((c, prop))
        if t:
            return _bare_type(t)
    return None


def _declared_type(code: list[str], idx: int, var: str, sx: ScopeIndex | None = None,
                   classes: dict[str, ClassInfo] | None = None, depth: int = 0) -> str | None:
    """MOD の行 idx から上へ、変数 var の宣言の型（単純名）を探す。メソッドの見出しで止まる。分からなければ None。

    `var x = <式>;` は、右辺が new T・(T) のキャスト・as T・別の変数・その変数のプロパティ（デコンパイルの宣言の型）
    なら、その型にする。"""
    v = re.escape(var)
    var_pat = re.compile(rf"\bvar\s+{v}\s*=\s*(?P<rhs>[^;]+);")
    pats = [re.compile(rf"\b(?:is|case)\s+([A-Z]\w*)\s+{v}\b"),
            re.compile(rf"\bforeach\s*\(\s*([A-Z][\w.]*)(?:<[^>]*>)?\??\s+{v}\s+in\b"),
            re.compile(rf"(?:^|[(,]\s*|^\s*)(?:out\s+|ref\s+|in\s+)?([A-Za-z_][\w.]*)(?:<[^()]*?>)?\??\s+{v}\s*(?:=(?!=)|[,);]|$)")]
    for j in range(idx, max(-1, idx - 400), -1):
        s = code[j]
        if var in s:
            vm = var_pat.search(s)
            if vm:
                return _rhs_type(code, j, vm.group("rhs").strip(), sx, classes, depth)
            for p in pats:
                mt = p.search(s)
                if mt:
                    t = mt.group(1).split(".")[-1]
                    if t in NOT_HEADER_WORDS or t in ("await", "new", "throw"):
                        continue  # return x; などは宣言ではない
                    return None if t in ("object", "dynamic", "T") else t
        if j != idx and _is_member_decl(s) and mod_indent(s) <= 4:
            break
    return None


def _rhs_type(code: list[str], j: int, rhs: str, sx: ScopeIndex | None, classes: dict[str, ClassInfo] | None,
              depth: int) -> str | None:
    if depth >= 3:
        return None
    rhs = re.sub(r"\s*\?\?.*$", "", rhs).rstrip("!").strip()
    mt = re.match(r"^new\s+([A-Z][\w.]*)", rhs) or re.match(r"^\(\s*([A-Z][\w.]*)\??\s*\)", rhs) \
        or re.search(r"\bas\s+([A-Z][\w.]*)\s*$", rhs)
    if mt:
        return mt.group(1).split(".")[-1]
    mt = re.fullmatch(r"([a-z_]\w*)((?:\??\.[A-Z]\w*)*)", rhs)
    if not mt:
        return None
    t = _declared_type(code, j - 1, mt.group(1), sx, classes, depth + 1) \
        if mt.group(1) not in ("this", "base") else None
    for prop in re.findall(r"\.([A-Z]\w*)", mt.group(2)):
        if t is None or sx is None or classes is None:
            return None
        t = _prop_type(sx, classes, t, prop)
    return t


def receiver_unrelated(sx: ScopeIndex, fname: str, idx: int, name: str, owner: str,
                       classes: dict[str, ClassInfo], desc: dict[str, set[str]]) -> bool:
    """行 idx の `.name` の受け手が、宣言の型から見て owner と無関係な型の変数だけなら True（名前の一致の取り違え）。"""
    code = sx.code[fname]
    anc = ancestors(owner, classes)
    related = {owner} | desc.get(owner, set()) | {simple_name(a) for a in anc}
    for a in [owner] + anc:
        # 基底が実装するインターフェース（ICombatState など）越しの読み取りも、その基底の読み取り
        related.update(classes[a].bases if a in classes else ())
    occ = list(re.finditer(rf"(?:(\b[a-z_]\w*)\??|(\S))\.{re.escape(name)}\b(?!\s*[(<])", code[idx]))
    if not occ:
        return False
    for mt in occ:
        var = mt.group(1)
        if not var or var in ("this", "base"):
            return False
        t = _declared_type(code, idx, var, sx, classes)
        if t is None or t in related:
            return False
    return True


def mod_coverage(mod: ModSource, m: Member, classes: dict[str, ClassInfo], sx: ScopeIndex | None = None,
                 desc: dict[str, set[str]] | None = None) -> tuple[list[str], list[str]]:
    """MOD がこの候補を読んでいる箇所（file:line）。(全派生を覆う読み取り, 派生の分岐の中だけの読み取り)。

    派生の分岐の中だけの読み取りは file:line[only T,...] の形にする（!T は T 以外）。"""
    top = simple_name(m.owner.split(".")[0])
    names = [m.name] + ([m.wrapper] if m.wrapper else [])
    if m.kind == "dynamic_var_mutation":
        return [], []
    full, partial = [], []
    for nm in names:
        for fname, idx, how in mod.index.get(nm, ()):
            if how == "acc" and (m.kind == "data_field" or not nm[0].isupper()):
                continue
            bc = block_classes(mod.files[fname], idx)
            # 基底クラスの候補も、文字列の一致はブロックのクラス（case・typeof・is）にその型があるときだけ数える。
            # プロパティの参照（.Name）は型を問わず数える（基底のメンバーはどの派生の変数からも読まれる）。
            if not (top in bc or (top in BASE_CLASS_NAMES and how == "acc")):
                continue
            if how == "acc" and sx is not None and desc is not None and top in BASE_CLASS_NAMES \
                    and receiver_unrelated(sx, fname, idx, nm, top, classes, desc):
                continue
            where = f"{fname}:{idx + 1}"
            if sx is None or desc is None or top not in desc:
                full.append(where)
                continue
            paths = scope_paths(sx, fname, idx)
            limits = [restricting(p, top, desc) for p in paths]
            if any(not lim for lim in limits):
                full.append(where)
            else:
                only = sorted(set().union(*limits))
                if UNDECIDABLE in only:
                    sx.undecidable.add(f"{m.key}: {where}")
                    only = [x for x in only if x != UNDECIDABLE] + ["判定できない"]
                partial.append(f"{where}[only {','.join(only)}]")
    return sorted(set(full)), sorted(set(partial))


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

CLASSES = ("exposed", "partial", "public", "hidden", "cosmetic", "static/derived", "out_of_scope", "unknown")


def auto_classify(m: Member) -> tuple[str, str] | None:
    if HIDDEN_TYPE_RE.search(m.ctype):
        return "hidden", "型が乱数（Rng 系）"
    if m.name == "_canonicalInstance":
        return "static/derived", "正規インスタンスへの参照（ModelDb の定義。インスタンスごとの状態ではない）"
    if COSMETIC_TYPE_RE.search(m.ctype) or COSMETIC_NAME_RE.search(m.name):
        return "cosmetic", "型・名前が表示用（ノード・文字列リソース・効果音）"
    return None


def type_full_name(ci: ClassInfo) -> str:
    """ゲームの型の完全名（入れ子は +）。"""
    top = simple_name(ci.outer or ci.name)
    full = f"{ci.namespace}.{top}"
    if ci.outer:
        full += "+" + ci.name.split(".", 1)[1]
    return full


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--decompile", type=Path, default=None,
                    help="棚卸しを回すときに要る。許可リストの生成・検査だけなら省ける")
    ap.add_argument("--mod", required=True, type=Path)
    ap.add_argument("--game-version", default="未確認")
    ap.add_argument("--classification", type=Path, default=None)
    ap.add_argument("--out-tsv", type=Path, default=None)
    ap.add_argument("--out-md", type=Path, default=None)
    ap.add_argument("--list-unclassified", action="store_true",
                    help="分類の無い候補を、宣言・書き込み・読み取りのメソッド付きで標準出力に出す")
    ap.add_argument("--filter", default=None, help="--list-unclassified の対象を owner の正規表現で絞る")
    ap.add_argument("--write-allowlist", action="store_true",
                    help=f"分類表の section 付きの項目から {ALLOWLIST_FILE} を書く")
    ap.add_argument("--check-docs", action="store_true",
                    help="棚卸しの文書（md・tsv）を書かずに生成し直して比べ、違えば終了コード 3。--decompile が要る。"
                         "--game-version を省くと今の md の版を使う")
    ap.add_argument("--check-allowlist", action="store_true",
                    help=f"{ALLOWLIST_FILE} が分類表から生成した内容と一致するかを調べる（違えば終了コード 1）")
    args = ap.parse_args()

    mod_dir = args.mod.resolve()
    cls_path = args.classification or mod_dir / "docs" / "state-inventory-classification.json"
    out_tsv = args.out_tsv or mod_dir / "docs" / "state-inventory-members.tsv"
    out_md = args.out_md or mod_dir / "docs" / "state-inventory.md"
    allowlist_path = mod_dir / ALLOWLIST_FILE

    manual = {}
    if cls_path.exists():
        manual = json.loads(cls_path.read_text(encoding="utf-8"))
        manual.pop("_comment", None)

    entries = allowlist_entries(manual)
    dedicated_keys = dedicated_reader_keys(manual)
    generated = render_allowlist(entries, dedicated_keys)
    allowlist_count = len(entries) + len(dedicated_keys)
    status = 0
    if args.write_allowlist:
        allowlist_path.write_text(generated, encoding="utf-8")
        print(f"{ALLOWLIST_FILE}: {allowlist_count} 項目を書いた")
    if args.check_allowlist:
        current = allowlist_path.read_text(encoding="utf-8") if allowlist_path.exists() else ""
        if current != generated:
            print(f"{ALLOWLIST_FILE} が分類表と一致しない。--write-allowlist で作り直す", file=sys.stderr)
            status = 1
        else:
            print(f"{ALLOWLIST_FILE}: 分類表と一致（{allowlist_count} 項目）")
    if args.decompile is None:
        if not (args.write_allowlist or args.check_allowlist):
            ap.error("--decompile が要る（許可リストの生成・検査だけなら --write-allowlist / --check-allowlist）")
        return status
    decompile = args.decompile.expanduser().resolve()

    classes, members, _ = collect(decompile)
    pool_of = assign_pools(decompile, classes)
    tok_index = build_token_index(decompile)
    mod = load_mod(mod_dir)
    generic_dv = generic_dynamic_var_coverage(mod)
    allow_lines = allowlist_lines(allowlist_path)
    sx = build_scope_index(mod)
    desc = descendant_names(classes)

    rows = []
    partial_errors: list[str] = []
    for m in members:
        top = m.owner.split(".")[0]
        ci = classes[m.owner] if m.owner in classes else classes[top]
        # 外部（別クラス）からの参照。公開の包みのプロパティ・公開フィールドだけ
        pub = m.wrapper if (m.wrapper and m.wrapper_access in ("public", "internal")) else (
            m.name if m.access in ("public", "internal") and m.kind != "data_field" else None)
        if pub and pub in tok_index:
            ext = sorted(x for x in tok_index[pub] if x != top and x not in (m.owner,))
            m.external = set(ext[:8]) | ({f"(+{len(ext) - 8})"} if len(ext) > 8 else set())
        cov, partial_cov = mod_coverage(mod, m, classes, sx, desc)
        if m.kind == "dynamic_var_mutation" and m.family in generic_dv:
            cov = [generic_dv[m.family] + "（DynamicVars を丸ごと出す）"]
        entry = manual.get(m.key)
        if entry and entry.get("partial_reason") and (entry.get("section") or entry.get("class") != "exposed"
                                                        or cov or not partial_cov):
            partial_errors.append(f"{m.key}: partial_reason は、MOD の読み取りが派生の分岐の中だけで、"
                                  "分類が exposed（section なし）の項目にだけ書く（今は当てはまらない）")
        if entry and (entry.get("section") or entry.get("reader_key")):
            # 分類（人間に見えるか）は public のまま、出す仕組みは生成した許可リスト
            cls, source = "exposed", "allowlist"
            cov = [f"{ALLOWLIST_FILE}:{allow_lines[m.key]}"] if m.key in allow_lines else []
        elif entry and entry.get("class", "unknown") != "exposed":
            cls = entry.get("class", "unknown")
            source = "manual"
        elif not cov and partial_cov:
            # 基底のメンバーを、派生の分岐の中（とその分岐からしか呼ばれない補助）でだけ読んでいる。
            # ほかの派生の行には出ていない。分類表の partial_reason（ほかの派生が要らない理由）で解消するか、
            # 許可リスト（section）で全派生に出す。
            cov = partial_cov
            if entry and entry.get("partial_reason"):
                cls, source = "exposed", "partial-resolved"
                entry = dict(entry, note=entry["partial_reason"])
            else:
                cls, source = "partial", "mod-scan"
                entry = {"note": "基底のメンバーを派生の分岐の中だけで読んでいる（ほかの派生の行に出ていない）"}
        elif entry:
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
            "declared_on": m.declared_on, "family": m.family, "type_full": type_full_name(ci),
            "pool": (entry or {}).get("pool") or owner_pool(m, classes, pool_of),
            "line": f"{classes[top].file.name}:{m.line}",
            "writers": sorted(m.writers), "readers": sorted(m.readers), "external": sorted(m.external),
            "coverage": cov, "class": cls, "source": source,
            "note": entry.get("note", ""), "json_key": entry.get("json_key", ""),
            "effect": entry.get("effect", ""), "written": entry.get("written", ""),
            "section": entry.get("section", ""),
        })
    rows.sort(key=lambda r: (r["family"], r["owner"], r["member"]))
    stale = sorted(set(manual) - {r["key"] for r in rows})
    for where in sorted(sx.undecidable):
        print(f"warning: 呼び出し元をたどっても入口に着かない（循環だけ、または深さ {SCOPE_DEPTH} を超える）。全派生に届くか判定できず partial として扱う: {where}",
              file=sys.stderr)
    partial_errors += [f"{r['key']}: 基底のメンバーを派生の分岐の中だけで読んでいる（{', '.join(r['coverage'])}）。"
                       "許可リスト（section）で全派生に出すか、partial_reason にほかの派生が要らない理由を書く"
                       for r in rows if r["class"] == "partial"]
    if partial_errors:
        print("partial（基底のメンバーの読み取りが派生の分岐の中だけ）を解消していない:", file=sys.stderr)
        for line in partial_errors:
            print("  " + line, file=sys.stderr)
        status = status or 4

    # 許可リストの項目が今の版の宣言と合うか（型の完全名・C# の型・種別）
    by_key = {r["key"]: r for r in rows}
    problems = []
    for e in entries + [dict(entry, key=member) for member, _, entry in dedicated_keys]:
        r = by_key.get(e["key"])
        if r is None:
            problems.append(f"{e['key']}: 今の版に無い")
            continue
        if r["type_full"] != e["type_full"]:
            problems.append(f"{e['key']}: 型の完全名 {e['type_full']} ≠ {r['type_full']}")
        if r["type"] != e["cs_type"]:
            problems.append(f"{e['key']}: C# の型 {e['cs_type']} ≠ {r['type']}")
        if r["kind"] != e["kind"]:
            problems.append(f"{e['key']}: 種別 {e['kind']} ≠ {r['kind']}")
    if problems:
        print("許可リストの項目が今の版の宣言と合わない:", file=sys.stderr)
        for line in problems:
            print("  " + line, file=sys.stderr)
        status = 2

    if args.list_unclassified:
        flt = re.compile(args.filter) if args.filter else None
        for r in rows:
            if r["class"] != "unclassified" or (flt and not flt.search(r["owner"])):
                continue
            print(f"{r['key']}\t{r['type']}\t{r['kind']}\t{r['pool']}\tW={','.join(r['writers'])}\t"
                  f"R={','.join(r['readers'])}\tX={','.join(r['external'])}\t{r['line']}")
        print(f"# unclassified: {sum(1 for r in rows if r['class'] == 'unclassified')}", file=sys.stderr)
        return status

    if args.check_docs:
        version = args.game_version
        if version == "未確認" and out_md.exists():
            m = re.search(r"ゲームの版: \*\*(.+?)\*\*", out_md.read_text(encoding="utf-8"))
            if m:
                version = m.group(1)
        with tempfile.TemporaryDirectory() as tmp:
            tmp_tsv, tmp_md = Path(tmp) / out_tsv.name, Path(tmp) / out_md.name
            write_tsv(tmp_tsv, rows)
            write_md(tmp_md, rows, stale, decompile, version, mod_dir)
            stale_docs = [str(dst) for src, dst in ((tmp_tsv, out_tsv), (tmp_md, out_md))
                          if not dst.exists() or src.read_bytes() != dst.read_bytes()]
        if stale_docs:
            print("棚卸しの文書が古い（--check-docs を外して生成し直す）: " + ", ".join(stale_docs), file=sys.stderr)
            return 3
        print("棚卸しの文書: 生成し直した内容と一致")
        return status
    write_tsv(out_tsv, rows)
    write_md(out_md, rows, stale, decompile, args.game_version, mod_dir)
    counts = Counter(r["class"] for r in rows)
    print(f"候補 {len(rows)} 件: " + ", ".join(f"{k} {v}" for k, v in sorted(counts.items())))
    if stale:
        print(f"stale（分類表にあるが今の版に無い）: {len(stale)} 件", file=sys.stderr)
    return status


# ---------------------------------------------------------------------------
# 生成する許可リスト（MOD の C#）
# ---------------------------------------------------------------------------

ALLOWLIST_FILE = "McpMod.StateAllowlist.Generated.cs"
ALLOWLIST_SOURCE_FILES = (ALLOWLIST_FILE, "McpMod.StateAllowlist.cs", "McpMod.StateAllowlistFormatting.cs")
ALLOWLIST_SECTIONS = ("card", "relic", "power", "player", "battle", "run")
ALLOWLIST_KINDS = {"field": "Field", "auto_property": "AutoProperty",
                   "getonly_auto_property": "AutoProperty", "data_field": "DataField"}
ALLOWLIST_CONVERTERS = ("Bool", "Int", "NullableInt", "Decimal", "EnumName", "CardUid", "CardModelId",
                        "CardPlayCardUid", "CreatureCombatId", "CreatureCombatIds", "KeywordNames",
                        "PowerRefs", "SerializableCards", "ModifierIds")
ALLOWLIST_MARKDOWN = ("NonDefault", "Always", "Never", "DiffersFromAmount")
# 種別をまたいで同じ section・キーを使ってよいのは、互いに継承関係の無い具象クラスどうしだけ
BASE_TYPE_FULL = {f"{ns}.{name}" for ns, name, _ in BASE_CLASS_FILES}


def allowlist_entries(manual: dict) -> list[dict]:
    """分類表の section 付きの項目（第 2 段で出すもの）。キーの順に並べる。"""
    out, errors = [], []
    for key, e in sorted(manual.items()):
        if not isinstance(e, dict) or not e.get("section"):
            continue
        need = ("json_key", "converter", "type_full", "cs_type", "kind")
        miss = [n for n in need if not e.get(n)]
        if miss:
            errors.append(f"{key}: {', '.join(miss)} が無い")
            continue
        if e.get("class") != "public":
            errors.append(f"{key}: section を付けてよいのは class=public だけ（{e.get('class')}）")
        if e["section"] not in ALLOWLIST_SECTIONS:
            errors.append(f"{key}: 不明な section {e['section']}")
        if e["converter"] not in ALLOWLIST_CONVERTERS:
            errors.append(f"{key}: 不明な converter {e['converter']}")
        if e["kind"] not in ALLOWLIST_KINDS:
            errors.append(f"{key}: 許可リストに載せられない種別 {e['kind']}")
        md = e.get("markdown", "NonDefault")
        if md not in ALLOWLIST_MARKDOWN:
            errors.append(f"{key}: 不明な markdown {md}")
        model_type = e["type_full"].split("+")[0]
        member = key.rsplit(".", 1)[1]
        out.append({"key": key, "section": e["section"], "model_type": model_type, "type_full": e["type_full"],
                    "member": member, "kind": e["kind"], "json_key": e["json_key"], "cs_type": e["cs_type"],
                    "converter": e["converter"], "markdown": md, "note": e.get("note", "")})
    seen: dict[tuple[str, str], list[dict]] = defaultdict(list)
    for e in out:
        seen[(e["section"], e["json_key"])].append(e)
    for (section, json_key), group in seen.items():
        if len(group) > 1 and any(g["model_type"] in BASE_TYPE_FULL for g in group):
            errors.append(f"{section}.{json_key}: 基底クラスの項目と同じキーを別の項目が使っている")
    if errors:
        raise SystemExit("分類表の許可リストの項目に誤り:\n  " + "\n  ".join(errors))
    return out


def cs_string(s: str) -> str:
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"').replace("\n", " ") + '"'


def dedicated_reader_keys(manual: dict) -> list[tuple[str, str, dict]]:
    """既定の internal_state と異なる形を持つ専用 reader の公開キー。"""
    entries = []
    seen = set()
    for member, entry in sorted(manual.items()):
        if not isinstance(entry, dict) or "reader_key" not in entry:
            continue
        name = entry["reader_key"]
        if (entry.get("class") != "public" or entry.get("section") or not isinstance(name, str)
                or not re.fullmatch(r"[A-Z][A-Za-z0-9]*Key", name) or name in seen):
            raise SystemExit(f"{member}: 専用 reader は class=public、section なし、固有の reader_key が必要")
        required = ("json_key", "note", "type_full", "cs_type", "kind")
        missing = [key for key in required if not isinstance(entry.get(key), str) or not entry[key]]
        if missing:
            raise SystemExit(f"{member}: 専用 reader に {', '.join(missing)} が無い")
        entries.append((member, name, entry))
        seen.add(name)
    return entries


def render_allowlist(entries: list[dict], dedicated_keys: list[tuple[str, str, dict]]) -> str:
    lines = [
        "// <auto-generated>",
        "// tools/state_inventory.py --write-allowlist が docs/state-inventory-classification.json から生成する。手で書き換えない。",
        "// 項目を足す・変えるときは分類表を直して生成し直し、--check-allowlist で一致を確かめる。",
        "// 各項目の最後の文字列は分類表の備考（人間に見える根拠）。レビューではここを読む。",
        "// </auto-generated>",
        "namespace STS2_MCP;",
        "",
        "public static partial class McpMod",
        "{",
        "    internal static readonly StateAllowlistEntry[] StateAllowlist =",
        "    {",
    ]
    for e in entries:
        lines.append(f"        // {e['key']}（{e['cs_type']}）")
        lines.append(
            f"        new({cs_string(e['section'])}, {cs_string(e['model_type'])}, {cs_string(e['type_full'])}, "
            f"{cs_string(e['member'])}, StateMemberKind.{ALLOWLIST_KINDS[e['kind']]}, {cs_string(e['json_key'])}, "
            f"StateValueConverter.{e['converter']}, StateMarkdown.{e['markdown']},")
        lines.append(f"            {cs_string(e['note'])}),")
    lines.append("    };")
    if dedicated_keys:
        lines += ["", "    // 通常の internal_state と別の形で出す専用 reader のキー。"]
    for member, name, entry in dedicated_keys:
        lines.append(f"    // {member}（{entry['note']}）")
        lines.append(f"    private const string {name} = {cs_string(entry['json_key'])};")
    lines += ["}", ""]
    return "\n".join(lines)


def allowlist_lines(path: Path) -> dict[str, int]:
    """生成した許可リストの各項目の行番号（棚卸しの被覆の出典）。"""
    out = {}
    if not path.exists():
        return out
    for i, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        m = re.match(r"\s*// (\S+)（", line)
        if m:
            out[m.group(1)] = i + 1
    return out


def write_tsv(path: Path, rows: list[dict]) -> None:
    cols = ["key", "family", "pool", "kind", "type", "access", "wrapper", "saved", "declared_on", "line",
            "class", "source", "coverage", "writers", "readers", "external", "json_key", "section", "type_full"]
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
    out.append("- 生成: `bin/sts2-python -B tools/state_inventory.py --decompile <dir> --mod . --game-version <版>`")
    out.append(f"- 許可リスト: `--write-allowlist` が public の項目から [`../{ALLOWLIST_FILE}`](../{ALLOWLIST_FILE}) を作る。"
               "`section` の項目は各行の `internal_state`、`reader_key` の項目は専用 reader が備考の公開パスに出す。"
               "`--check-allowlist` が分類表との一致を、棚卸しの実行が今の版の宣言との一致を確かめる\n")
    out.append("## 分類の意味\n")
    out.append("| 分類 | 意味 |\n|---|---|")
    out.append("| exposed | すでに MOD の state に出ている（MOD のソースで読み取りを検出、分類表で確認、または生成した許可リストを使う `internal_state`・専用 reader で出す〔判定 allowlist〕） |")
    out.append("| partial | 基底クラスのメンバーを、MOD が派生の分岐（case・is・その分岐からしか呼ばれない補助）の中だけで読んでいる。"
               "ほかの派生の行には出ていない。許可リストで全派生に出すか、分類表の `partial_reason` で解消する（判定 partial-resolved）。"
               "解消していないと棚卸しは終了コード 4 |")
    out.append("| public | 人間が見える・見たものから確定できる。まだ出ていない（出す対象） |")
    out.append("| hidden | 人間に見えない（乱数・未公開の山の順・まだ見せていない生成物）。出さない |")
    out.append("| cosmetic | 戦闘・進行に効かない（台詞・演出・UI）。出す必要は無い |")
    out.append("| static/derived | 定数、または出ている値・アセンション・強化から決まる。出す必要は無い |")
    out.append("| out_of_scope | 標準のモード以外（Blitz・Dread・カスタム）だけのもの。対象外 |")
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
               "ランの `RunState` は、Deck の追加通知に使う `Modifiers` を対象にする。"
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
    table("partial（未解消。棚卸しは終了コード 4）", lambda r: r["class"] == "partial", [
        ("クラス.メンバー", lambda r: f"`{r['key']}`"), ("型", lambda r: r["type"]), ("範囲", lambda r: r["pool"]),
        ("MOD の箇所（only は読む派生）", lambda r: ", ".join(r["coverage"])),
        ("読む側（ゲーム）", lambda r: methods(r["readers"] + r["external"]))])
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
    table("out_of_scope（対象外）", lambda r: r["class"] == "out_of_scope", [
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
    table("partial-resolved（派生の分岐の中だけで読み、ほかの派生は要らないと判断した基底のメンバー）",
          lambda r: r["source"] == "partial-resolved", [
              ("クラス.メンバー", lambda r: f"`{r['key']}`"), ("範囲", lambda r: r["pool"]),
              ("MOD の箇所（only は読む派生）", lambda r: ", ".join(r["coverage"])), ("ほかの派生が要らない理由", lambda r: r["note"])],
          "MOD の箇所の `[only T]` は、その読み取りが届く派生の型（`!T` は T 以外）。")
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
