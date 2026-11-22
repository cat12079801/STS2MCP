# 戦闘の出来事の通し列（`battle.combat_log`）・選択の文脈（`decision_context`）の契約試験

追加パッケージ不要。fish では次で実行する。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/libexec/dotnet run --project tests/CombatLogContract -c Release -p:BuildInParallel=false -p:UseSharedCompilation=false
```

本番の `McpMod.CombatLogStore.cs`（行の保持・seq・窓・上限・戦闘ごとの初期化・欠けの印）、`McpMod.CombatLogFormatting.cs`
（Markdown）、`McpMod.ChoiceSourceTracker.cs`（内側の入口の印・デッキ／束／レリックの画面）をリンクする。ゲームの型は使わない。

- 行の保持: seq を 0 から振る、既定の窓は最後の N 行、`combat_log_since` はその seq から、上限を超えた古い行を捨てて
  `first_seq` を進める、捨てた行より前を求めたら `since_truncated`、新しい戦闘で初めから、別の戦闘の行を返さない、
  行を作れなかった・patch を当てられなかったら理由を出す（`known: false`）、記録した行は後から変わらない
- 選択の記録: 同じ context の内側の入口が開いたら外側に印（`combat_log` の selection の行は内側だけ）、`FromDeck*`・束・レリックの画面
- `ChoiceIdentityChecks.cs` は本番の `CombatAttempt`・選択の記録・store を固定セッション P/Q と制御できる Task で動かす。
  同じ画面の再読、同じ試行の別呼出し、同じ遭遇の再試行、別プロセスでの番号の衝突、確定行から通常 battle への結合を検査する。
  同名の札を別の実体と UID で照合し、曖昧な画面・提示外の札・取消・失敗・0 候補・二重通知を取り違えないことを確かめる。
  A の完了を消費してから B へ切り替え、B の初行前と初行後で遅延追記・欠測理由の両方を拒む。
- Markdown: `### Combat Log` の見出しと最後の 15 行、listener のいる巡だけ、山札の中の移動は札を出さない、unknown の理由、`Decision context:` の 1 行
- ソースの文字列: 通知の表 53 件・手番の段階 12 件、prefix が bool を返さない（呼出しを飛ばさない）、乱数に触らない、
  ゲームの引数を書き換えない、シャッフル自身の Add の間の行だけを伏せて `hidden_rows` で件数を出す（一度しか使えない印。`AfterShuffle` の listener の移動は伏せない・`draw_pile_empty` は `ShuffleIfNecessary` が呼んだシャッフルだけ）、Add の listener の中で別の命令が置いた札に Add の位置を付けない、手番の段階を引数の型で 1 つに決めて当てる、選択の記録の戦闘は進行中の戦闘、山札から自動で選ばれた札は表示順、既定の窓は 100 行、山札の中の移動と山札からの選択の候補の順を出さない、
  置いた位置の添字を出さない、PowerVar のプレビューを出来事にしない、listener を `BuildHookListenerRows` で出す、
  行を作れなければ `known: false`、初期化・`battle`・5 つの選択画面・`player`・手札の `energy_cost`・`ended_combat`・Markdown への配線、
  `?combat_log_since=` を読むたびに戻すこと、schema 1.10.0

ゲームに接続しない。patch の当て先が本物の `sts2.dll` で解決して当たるかは [`tests/PatchTargetsContract`](../PatchTargetsContract/README.md)。行が実機の順で出るかは調整役が実機（開発コンソールを使った検証用のラン）で確かめる。
