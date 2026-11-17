# 敵の戦闘内部状態の契約試験

追加パッケージ・ゲームへの接続は不要。fish での実行例:

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/bin/dotnet run --project tests/EnemyCombatStateContract -c Release -p:BuildInParallel=false -p:UseSharedCompilation=false
```

本番の `McpMod.EnemyCombatState.cs`（builder と Markdown）・内部状態の読取 helper・warnings をリンクする。
ゲームの型は滝の巨人（`WaterfallGiant`）の private フィールドの名前と型だけを写し、
move の実行・`TriggerAboutToBlowState`・乱数は用意しない。値は試験がフィールドへ直接入れる。

- `is_about_to_blow` / `steam_eruption_damage` の false・0・true・正の値を実値として出す。理由・警告なし。
- 読取がフィールドを変更しないこと、前回の snapshot の独立、本番の wire 設定で bool / int のまま送ること。
- Markdown の `**Combat state:**` 行（false=no、0 を表示）と、欠落・空の省略。
- Monster が無い敵・別の敵（同名フィールドを持つものを含む）には `combat_state` を付けず、警告も出さない。
- 読取失敗の形（null・`unavailable_reasons`・個体と項目を識別する warnings・Markdown の理由）は、
  共通の `ReadInternalStateValue` / `ReadDeclaredStateField` に欠落フィールド・型違いを与えて確認する
  （本番の `WaterfallGiant` は sealed で、同じ型に壊れたフィールドを持たせた二重は作れないため）。
- 全敵行への配線（生死で絞らない）・協力プレイの共通 builder・Markdown の配線・schema 4 の維持はソース文字列で確認。

根拠は v0.113.1（`sts2-a671e9ef`）の `WaterfallGiant._isAboutToBlow` / `_steamEruptionDamage`、
`WaterfallGiant.TriggerAboutToBlowState` / `AboutToBlowMove` / `ExplodeMove`、`SteamEruptionPower.AfterDeath`。
実機での段階ごとの値の確認は取得担当へ引き渡す。
