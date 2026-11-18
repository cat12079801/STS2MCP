# 戦闘の前後の解決中の契約検証

追加パッケージ不要。fish では次で実行する。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/libexec/dotnet run --project tests/CombatBoundaryResolvingContract -c Release
```

本番の `McpMod.CombatBoundary.cs`（`CombatBoundaryReason`・`CombatBoundaryMessage`）をリンクする。ゲームの型は使わない。
戦闘の部屋で戦闘が進行中でないときの各段階（部屋を積んでから `SetUpCombat` まで・`IsStarting` の間・勝ち・負け・
保存から再開した終わった戦闘）で、`combat_starting` / `combat_ending` のどちらになるかと、message を検証する。
`BuildGameStateCore` への配線（入力待ちの画面とマップを戦闘の部屋より前で拾うこと・理由は `battle` の無い分岐でだけ
決めること）、`AddResolvingState` で入力待ちの画面の消去より前に足すこと、入力待ちの画面の一覧・`IsSettledState`・
現行の schema 10 をソース文字列で検査する。

ゲームに接続しない。実際の戦闘の開始・終了での確認（GET を撃ち続ける）は調整役が実機で行う。
