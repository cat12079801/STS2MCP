# 敵の行動状態の契約試験

追加パッケージ・ゲームへの接続は不要。fish での実行例:

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/bin/dotnet run --project tests/EnemyMoveStateContract -c Release -p:BuildInParallel=false -p:UseSharedCompilation=false
```

本番の `McpMod.EnemyMoveState.cs`・表示 helper・内部状態の読取 helper・warnings をリンクする。
ゲームの型は getter の形だけを写し、抽選・遷移・実行メソッドは用意しない。

- 始祖虫の通常三手・任意の別モンスターの ID、参照先を優先する follow-up と ID の fallback。
- スタンの ID・明示復帰先・復帰先と異なるログ末尾・遷移不可の false。MoveState 以外のログ末尾も保持。
- 参照・ログ・末尾・各 ID の不在、空ログ、各 getter の例外。項目別の null・理由・個体別 warnings と成功項目の保持。
- 四値の型・欠落・null に対する純粋な readable 判定。空文字列の FollowUpStateId は初期状態への遷移指定なので保持。
- 反復読取の不変性、前回 snapshot の独立性、次回取得での復旧、JSON の bool / null と warnings。
- Markdown の ID・follow-up・読取不能の理由。全敵・協力プレイ・表示への配線と schema 4 の維持はソース文字列で確認。

根拠は v0.113.1（`sts2-a671e9ef`）の `MonsterModel.NextMove/MoveStateMachine/SetMoveImmediate`、
`MoveState.Id/FollowUpState/FollowUpStateId/CanTransitionAway/GetNextState`、
`MonsterMoveStateMachine.StateLog/FindNextMoveState`、`Creature.StunInternal`、
`LouseProgenitor.GenerateMoveStateMachine`。ゲームの遷移そのものを模擬する試験ではない。
実機での通常三手・スタンと復帰・他敵の確認は取得担当へ引き渡す。
