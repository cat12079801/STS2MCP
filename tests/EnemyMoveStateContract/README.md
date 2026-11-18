# 敵の行動状態の契約試験

追加パッケージ・ゲームへの接続は不要。fish での実行例:

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/bin/dotnet run --project tests/EnemyMoveStateContract -c Release -p:BuildInParallel=false -p:UseSharedCompilation=false
```

本番の `McpMod.EnemyMoveState.cs`・`McpMod.EnemyStunHandler.cs`・表示 helper・内部状態の読取 helper・warnings をリンクする。
ゲームの型は getter と、MOD が読む private フィールド（`MonsterMoveStateMachine._currentState` / `_initialState` /
`_performedFirstMove`、`MoveState._onPerform`）の名前・型だけを写し、抽選・遷移・実行メソッドは用意しない。
`CreatureCmd.Stun` は本体と同じ形（stunMove を creature と stunMove を捕捉するローカル関数 Wrapper で包み、
引数なしの版は `_ => Task.CompletedTask` を渡す）で書き、コンパイラが作る名前・捕捉フィールド・IL を本体と揃える。

- 始祖虫の通常三手・任意の別モンスターの ID、参照先を優先する follow-up と ID の fallback。
- スタンの ID・明示復帰先・復帰先と異なるログ末尾・遷移不可の false。MoveState 以外のログ末尾も保持。
- 参照・ログ・末尾・各 ID の不在、空ログ、各 getter の例外。項目別の null・理由・個体別 warnings と成功項目の保持。
- 四値の型・欠落・null に対する純粋な readable 判定。空文字列の FollowUpStateId は初期状態への遷移指定なので保持。
- 反復読取の不変性、前回 snapshot の独立性、次回取得での復旧、JSON の bool / null と warnings。
- schema 6 の追加項目（`cat12079801/sts2#1018`）: `has_follow_up`（終端の行動は false の実値で、`follow_up_id` の読取失敗と区別）・
  `must_perform_once`・`current_state_id` / `current_state_registered`（同じ ID でも参照が違えば false、強制遷移した STUNNED も false）/
  `initial_state_id`（分岐状態の現在地点を含む）・`performed_first_move`・`spawned_this_turn`・
  `state_log`（順序・重複・`is_move`。空は `[]` の実値、途中の要素の失敗・末尾と `last_logged_id` の不一致は全体を null）・`inputs_readable`。
  四値の `readable` は追加項目の失敗で落ちない。
- スタンの処理: 引数なしの `CreatureCmd.Stun` は IL で no-op と照合、同じ復帰先の通常スタンと起床（`SlumberingBeetle.WakeUpMove`）を区別、
  no-op と許可リストの 7 処理、CreatureCmd の外の no-op・別の個体の Wrapper・別の個体のメソッド・所有者が別の個体のパワーは未観測（null と理由）。
  Wrapper ではない delegate の STUNNED と、Wrapper なのに ID が STUNNED ではない行動は `is_stun` を null と理由、
  Wrapper ではない通常の行動は `is_stun=false`。delegate を呼ばない。
- Markdown の ID・follow-up・スタンの処理・ログ末尾・読取不能の理由。全敵・協力プレイ・表示への配線と現行の schema 9 をソース文字列で確認。

根拠は v0.113.1（`sts2-a671e9ef`）の `MonsterModel.NextMove/MoveStateMachine/SetMoveImmediate`、
`MoveState.Id/FollowUpState/FollowUpStateId/CanTransitionAway/GetNextState`、
`MonsterMoveStateMachine.StateLog/FindNextMoveState`、`Creature.StunInternal`、
`LouseProgenitor.GenerateMoveStateMachine`。ゲームの遷移そのものを模擬する試験ではない。
実機での通常三手・スタンと復帰・他敵の確認は取得担当へ引き渡す。
