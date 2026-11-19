#!/usr/bin/env python3
"""tools/state_inventory.py の被覆の判定（基底のメンバーの partial）の自己試験。

デコンパイルもゲームも要らない。合成した MOD のソースと、合成したクラスの継承（PowerModel ← HexPower・
StrengthPower）で、基底のメンバーの読み取りが全派生に届くか（exposed）・派生の分岐の中だけか（partial）を確かめる。

    /Users/anshinin/workspace/repos/sts2/bin/sts2-python -B tools/state_inventory_selftest.py
"""
from __future__ import annotations

import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import state_inventory as si  # noqa: E402

# 入口から深さの上限より長い呼び出しの鎖（Step0 → … → StepLast）
CHAIN = "".join(f"""
    private static void Step{i}(Dictionary<string, object?> state, PowerModel power)
    {{
        {f"Step{i + 1}" if i + 1 < si.SCOPE_DEPTH + 5 else "StepLast"}(state, power);
    }}
""" for i in range(si.SCOPE_DEPTH + 5))
HEADER = "namespace STS2_MCP;\n\npublic static partial class McpMod\n{\n"
FOOTER = "}\n"


def classes() -> dict[str, si.ClassInfo]:
    def ci(name, bases):
        return si.ClassInfo(name=name, family="power", file=Path(f"{name}.cs"), bases=bases,
                            namespace="MegaCrit.Sts2.Core.Models.Powers", is_abstract=False)
    return {"PowerModel": si.ClassInfo(name="PowerModel", family="power", file=Path("PowerModel.cs"), bases=["AbstractModel"],
                                       namespace="MegaCrit.Sts2.Core.Models", is_abstract=True),
            "HexPower": ci("HexPower", ["PowerModel"]),
            "StrengthPower": ci("StrengthPower", ["PowerModel"]),
            # 受け手の型を引くためのプロパティの宣言（CardModel.Affliction は AfflictionModel?）
            "CardModel": si.ClassInfo(name="CardModel", family="card", file=Path("CardModel.cs"), bases=["AbstractModel"],
                                      namespace="MegaCrit.Sts2.Core.Models", is_abstract=True,
                                      prop_types={"Affliction": "AfflictionModel?"})}


def applier() -> si.Member:
    return si.Member(owner="PowerModel", name="_applier", ctype="Creature?", kind="field", access="private",
                     line=1, family="power", declared_on="PowerModel", wrapper="Applier", wrapper_access="public")


def coverage(body: str, prefix: str = "") -> tuple[list[str], list[str]]:
    with tempfile.TemporaryDirectory() as tmp:
        Path(tmp, "McpMod.Test.cs").write_text(prefix + HEADER + body + FOOTER, encoding="utf-8")
        mod = si.load_mod(Path(tmp))
        cls = classes()
        return si.mod_coverage(mod, applier(), cls, si.build_scope_index(mod), si.descendant_names(cls))


CASES = [
    ("case の節からしか呼ばれない補助の中の読み取りは partial（PR #108 の呪術の付与元）", "partial", """
    private static void Attach(Dictionary<string, object?> state, PowerModel power)
    {
        switch (power)
        {
            case HexPower:
                AttachApplier(state, power);
                break;
        }
    }

    private static void AttachApplier(Dictionary<string, object?> state, PowerModel power)
    {
        state["applier"] = power.Applier;
    }
"""),
    ("分岐の外からも呼ばれれば exposed", "full", """
    private static void Attach(Dictionary<string, object?> state, PowerModel power)
    {
        switch (power)
        {
            case HexPower:
                AttachApplier(state, power);
                break;
        }
        AttachApplier(state, power);
    }

    private static void AttachApplier(Dictionary<string, object?> state, PowerModel power)
    {
        state["applier"] = power.Applier;
    }
"""),
    ("if (x is T) の中は partial", "partial", """
    private static void Attach(Dictionary<string, object?> state, PowerModel power)
    {
        if (power is HexPower hex)
        {
            state["applier"] = hex.Applier;
        }
    }
"""),
    ("else の中は否定の型（T 以外）で partial", "partial", """
    private static void Attach(Dictionary<string, object?> state, PowerModel power)
    {
        if (power is HexPower)
            state["x"] = 1;
        else
            state["applier"] = power.Applier;
    }
"""),
    ("「if (x is not T) return;」の見張りの後ろは partial", "partial", """
    private static void Attach(Dictionary<string, object?> state, PowerModel power)
    {
        if (power is not HexPower)
            return;
        state["applier"] = power.Applier;
    }
"""),
    ("ローカル関数を case の中だけで呼ぶなら partial", "partial", """
    private static void Attach(Dictionary<string, object?> state, PowerModel power)
    {
        switch (power)
        {
            case HexPower:
                Read();
                break;
        }

        void Read()
        {
            state["applier"] = power.Applier;
        }
    }
"""),
    ("複数行の if の条件の継続行にある型の判定も見る（PR #109 のレビューの P2）", "partial", """
    private static void Attach(Dictionary<string, object?> state, PowerModel power, bool flag)
    {
        if (flag
            && power is HexPower)
        {
            state["applier"] = power.Applier;
        }
    }
"""),
    ("else の否定も、前の if の複数行の条件から取る", "partial", """
    private static void Attach(Dictionary<string, object?> state, PowerModel power, bool flag)
    {
        if (flag
            || power is HexPower)
        {
            state["x"] = 1;
        }
        else
        {
            state["applier"] = power.Applier;
        }
    }
"""),
    ("case A or B の or パターン", "partial", """
    private static void Attach(Dictionary<string, object?> state, PowerModel power)
    {
        switch (power)
        {
            case HexPower or StrengthPower:
                state["applier"] = power.Applier;
                break;
        }
    }
"""),
    ("case のプロパティパターン", "partial", """
    private static void Attach(Dictionary<string, object?> state, PowerModel power)
    {
        switch (power)
        {
            case HexPower { Amount: > 0 }:
                state["applier"] = power.Applier;
                break;
        }
    }
"""),
    ("{ return; } の括弧付きの見張り", "partial", """
    private static void Attach(Dictionary<string, object?> state, PowerModel power)
    {
        if (power is not HexPower)
        {
            return;
        }
        state["applier"] = power.Applier;
    }
"""),
    ("!(x is T) の見張り", "partial", """
    private static void Attach(Dictionary<string, object?> state, PowerModel power)
    {
        if (!(power is HexPower)) return;
        state["applier"] = power.Applier;
    }
"""),
    ("GetType() != typeof(T) の見張り", "partial", """
    private static void Attach(Dictionary<string, object?> state, PowerModel power)
    {
        if (power.GetType() != typeof(HexPower)) return;
        state["applier"] = power.Applier;
    }
"""),
    ("default: return; の switch の後ろは残りの case の型", "partial", """
    private static void Attach(Dictionary<string, object?> state, PowerModel power)
    {
        switch (power)
        {
            case HexPower:
                break;
            default:
                return;
        }
        state["applier"] = power.Applier;
    }
"""),
    ("抜けない default の switch の後ろは制限にならない", "full", """
    private static void Attach(Dictionary<string, object?> state, PowerModel power)
    {
        switch (power)
        {
            case HexPower:
                state["x"] = 1;
                break;
            default:
                break;
        }
        state["applier"] = power.Applier;
    }
"""),
    ("自分を呼ぶ補助（循環の経路は捨てる）を case の中だけで呼ぶなら partial", "partial", """
    private static void Attach(Dictionary<string, object?> state, PowerModel power)
    {
        switch (power)
        {
            case HexPower:
                Walk(state, power, 0);
                break;
        }
    }

    private static void Walk(Dictionary<string, object?> state, PowerModel power, int depth)
    {
        state["applier"] = power.Applier;
        if (depth < 1) Walk(state, power, depth + 1);
    }
"""),
    ("呼び出し元が深さの上限を超えるなら判定できない（full にしない）", "undecidable", """
    private static void Entry(Dictionary<string, object?> state, PowerModel power)
    {
        Step0(state, power);
    }

""" + CHAIN + """
    private static void StepLast(Dictionary<string, object?> state, PowerModel power)
    {
        state["applier"] = power.Applier;
    }
"""),
    ("var の受け手は右辺の型で絞る（別の型の変数・プロパティ）", "none", """
    private static void Attach(Dictionary<string, object?> state, CardPlay play, CardModel card)
    {
        var p = play;
        var affliction = card.Affliction;
        state["t"] = p.Applier;
        state["u"] = affliction?.Applier;
    }
"""),
    ("入れ子の型のメソッドを case の中から x.Get(…) で呼ぶなら partial（PR #109 の再レビューの P3）", "partial", """
    private sealed class Helper(int seed)
    {
        public object? Get(PowerModel power)
        {
            return power.Applier;
        }
    }

    private static void Attach(Dictionary<string, object?> state, PowerModel power)
    {
        switch (power)
        {
            case HexPower:
                state["a"] = new Helper(0).Get(power);
                break;
        }
    }
"""),
    ("入れ子の型のメソッドを分岐の外から呼べば exposed", "full", """
    private sealed class Helper
    {
        public object? Get(PowerModel power)
        {
            return power.Applier;
        }
    }

    private static void Attach(Dictionary<string, object?> state, PowerModel power)
    {
        state["a"] = new Helper().Get(power);
    }
"""),
    ("数えられる呼び出し元が無く x.Name(…) の参照だけあるメソッドは入口にしない（判定できない）", "undecidable", """
    private static object? Read(PowerModel power)
    {
        return power.Applier;
    }

    private static void Attach(Dictionary<string, object?> state, Reader reader, PowerModel power)
    {
        if (power is HexPower)
            state["a"] = reader.Read(power);
    }
"""),
    ("型の判定の無い読み取りは exposed", "full", """
    private static void Attach(Dictionary<string, object?> state, PowerModel power)
    {
        state["applier"] = power.Applier;
    }
"""),
    ("基底そのものの判定（is PowerModel）は制限にならない", "full", """
    private static void Attach(Dictionary<string, object?> state, object model)
    {
        if (model is PowerModel power)
            state["applier"] = power.Applier;
    }
"""),
    ("別の型の同名のプロパティ（宣言の型が無関係な受け手）は数えない", "none", """
    private static void Attach(Dictionary<string, object?> state, Recorder recorder)
    {
        state["applier"] = recorder.Applier;
    }
"""),
]


def main() -> int:
    failed = 0
    for title, want, body, *prefix in CASES:
        full, partial = coverage(body, *prefix)
        got = "full" if full else "partial" if partial else "none"
        if got == "partial" and all("判定できない" in p for p in partial):
            got = "undecidable"
        ok = got == want
        if want == "partial" and ok:
            ok = all("[only " in p and "HexPower" in p for p in partial)
        failed += not ok
        print(f"{'ok' if ok else 'NG'}\t{title}\t(want {want}, got {got}: {full or partial})")
    print(f"state_inventory_selftest: {len(CASES) - failed}/{len(CASES)} ok")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
