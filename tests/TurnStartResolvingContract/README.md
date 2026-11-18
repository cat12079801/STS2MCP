# 手番の初めの解決中の契約検証

追加パッケージ不要。fish では次で実行する。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/libexec/dotnet run --project tests/TurnStartResolvingContract -c Release
```

本番の `McpMod.TurnStartResolving.cs`（`IsLocalPlayerTurnStarting`）をリンクし、
ゲームの enum（`CombatSide`・`PlayerTurnPhase`・`ActionSynchronizerCombatState`）は写しを使う。
手番の初めの各段階（None・Start・AutoPrePlay、Play になってから同期器が PlayPhase に戻るまで、
手番の初めの選択に答えた後の残り）で真、通常のプレイの局面・手番の終わり・敵の手番・
死んだローカルの Player の手番の残りで偽になることを検証する。
`AddResolvingState` への配線（戦闘の画面に限ること・入力待ちの画面の消去より前に足すこと）と、
現行の schema 11 であることはソース文字列で検査する。

ゲームに接続しない。実際の手番の進行での確認（`end_turn` と並行して GET を撃つ）は調整役が実機で行う。
