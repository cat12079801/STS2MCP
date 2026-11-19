# 戦闘中の選択画面の battle の契約検証

追加パッケージ不要。fish では次で実行する。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/libexec/dotnet run --project tests/SelectionBattleContract -c Release
```

本番の `McpMod.SelectionBattle.cs`（`IsSelectionStateType`・`ShouldAttachSelectionBattle`）をリンクする。ゲームの型は使わない。
戦闘中（戦闘の部屋で `CombatManager.IsInProgress`）の選択画面（`state_type` が `_select` で終わるもの。`hand_select`・`card_select`・
`bundle_select`・`relic_select`・今後の `*_select`）に `battle` を付けること、選択画面でない state（`monster` などの戦闘の画面・`map`・
`menu`・`game_over`・`card_reward`・`rewards`・`overlay` など）・戦闘の前後・戦闘の部屋でない選択画面には付けないこと、
既にある `battle` を作り直さないことを検証する。

単独（`McpMod.StateBuilder.cs`）とマルチ（`McpMod.MultiplayerState.cs`）の両方で、画面の振り分けの後・run と player の前の 1 か所で
付けること、戦闘の部屋かどうかを計算して判定に渡すこと（部屋の取り出しは判定の後）、`battle` を作れなければ
warning `battle.selection` にして `battle` だけを省くこと、振り分けの中で `battle` を付けるのは戦闘の画面だけ（`hand_select`・choose も共通の配線）であること、
`BuildBattleState` が山の中身を持たないこと、版の履歴に 1.4.0 があることをソースの文字列で検査する。

ゲームに接続しない。実機の確認（調整役が行う）:

1. 計略（Stratagem）のシャッフル後の選択・未来視の雫の選択（`screen_type: "NCombatPileCardSelectScreen"`）の state に `battle` があり、
   `round` / `enemies` が直前の戦闘の state と同じであること。
2. スプラッシュの choose・エントロピーの `hand_select` にも従来どおり `battle` があること。
3. 戦闘外の `card_select`（休憩所の強化・ショップの除去など）に `battle` が付かないこと。
