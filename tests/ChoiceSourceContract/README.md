# 選択を開いた元（`choice_source`）・エントロピーの変化の候補（`transformation_options`）の契約試験

追加パッケージ不要。fish では次で実行する。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/libexec/dotnet run --project tests/ChoiceSourceContract -c Release -p:BuildInParallel=false -p:UseSharedCompilation=false
```

本番の `McpMod.ChoiceSourceTracker.cs`（開いている選択の記録・画面と入口の対応・Markdown の 1 行）、
`McpMod.AsyncChain.cs`（await の鎖の読み取り・段の初めの山札の記録・残りの listener の並べ方）、
`McpMod.TransformationOptionsTable.cs`（変化の候補のまとめ方・Markdown の 1 行）、`McpMod.Warnings.cs` をリンクする。ゲームの型は使わない。

- 記録: 開いた順の番号、最も新しい開いている記録、player・戦闘・画面の種類での絞り込み（別の戦闘の古い記録を使わない）、
  候補の実体と順の照合、閉じた記録を返さないこと、上限を超えた古い記録を捨てること、同じ context の外側の入口（`outer_selectors`）、終わった戦闘・別の戦闘の記録を捨て戦闘外の記録を残すこと。戦闘中 choose の厳密な照合は `CombatLogContract` で検査する。
- 画面と `CardSelectCmd` の入口の対応（`hand` = `FromHand` / `FromHandForDiscard` / `FromHandForUpgrade`、`choose` = `FromChooseACardScreen`、
  `combat_pile` = `FromCombatPile`、`simple_grid` = `FromSimpleGrid` / `FromSimpleGridForRewards`）
- 変化の候補: 同じ ID・同じ候補の札を 1 行にまとめ uid を序数順に並べる（山札の順を出さない）、候補の重複除去と昇順、
  同じ ID でも候補が違えば別の行、本体の例外（候補なし）は `options: null` と理由（正常な状態なので warning にしない）、列挙の失敗は `known: false`、wire の null
- Markdown（`Choice source: …`・`Transformation options (Entropy): …`）
- ソースの文字列: `CardSelectCmd` の 8 つの patch 対象と引数の型、prefix が void・postfix が `ref` で Task を差し替えないこと、
  完了で記録を閉じること、初期化で patch を当てること、通知の表（24 件・列の種類）、残りの listener を既存の
  `BuildHookListenerRows`（山札の位置を隠す）で出すこと、`ModHelper` の購読者がいれば出さないこと、`models[0]` が山札の listener なら残りを出さないこと、使用中のポーション・オーブで通知を決めないこと、外側の入口を出すこと、state を作るたびに終わった戦闘の記録を捨てること、乱数・抽選に触らないこと、
  hand_select・choose・grid への配線と戦闘中の条件、変化の候補の配線・エントロピーの戦闘だけで出すこと・
  本体の `GetDefaultTransformationOptions` に `CardTransformation` と同じ `isInCombat` を渡すこと、金の斧の件数の式、版の履歴の 1.5.0

- await の鎖（`AwaitChainChecks.cs`、schema 1.9.0）: ゲームと同じ形の本物の async メソッド（選択を await する listener、
  2 段の iterator で列を回す Hook 風の段、シャッフル、2 枚目の前にシャッフルするドローのループ）を止めて、選択の Task から
  外へたどる順・宣言した型・各段の awaiter が内側の Task を指すこと・hoisted のループの添字と引数・段が取った列と走っている
  listener の位置（`List<T>.Enumerator._index - 1`）・読むだけで何も進めないこと・選択を終えれば最後まで進むこと、
  SynchronizationContext を捕まえた await、ContinueWith の継続を box と数えないこと、複数が待つときと深さの上限で止めること、
  無いフィールドで例外になること。段の初めの山札の記録（開いた Hook の間・その戦闘だけ・終わった戦闘を捨てる・上限）、
  残りの listener の並べ方（山札にあった札の塊は公開の順に並べ替えて `order: null`、他は列の位置、いま走らない・override
  しない listener は出さない）、Markdown、ソースの文字列（乱数に触らない・列挙子を進めない・patch が本体を飛ばさない・
  await の順の表・配線）。csproj は `<Optimize>true</Optimize>` で、構成によらずゲームの Release と同じ形の状態機械・
  iterator を作る（最適化なしでは iterator の一時変数の名前が違う）。
- `BeforeHandDrawChecks.cs` は 2 段の iterator を通る通常段・Late 段を Task で止め、本番のフレームと現在位置の読み取りを動かす。
  列を作ったあとでモデルを外す・足す操作を行い、有効な残り、実測した空列、フレームと列挙子の欠測、`OnPlay` の別 choose を検査する。
  `PatchTargetsContract` ではゲーム DLL の `Hook.BeforeHandDraw` への patch と状態機械のフィールドを確認する。

Harmony の patch の当たり方・`PlayerChoiceContext` の連鎖・通知の列の読み直し・本体の状態機械のフィールドはゲームが無いと動かせない。実機の確認（調整役が行う）:

1. 計略（`STRATAGEM_POWER`）を持ち、手番の初めのドローの途中で山札が尽きてシャッフルした選択（`NCombatPileCardSelectScreen`）の
   `card_select.choice_source` が `known: true`・`selector: "FromCombatPile"`・`models[0].model_id: "STRATAGEM_POWER"`・
   `hook.name: "AfterShuffle"` で、`remaining_listeners` にそれより後ろの `AfterShuffle` の listener（そろばん等を持っていれば）が出ること。
   ドロー札（ポメルストライクなど）の本文の途中のシャッフルでは `card_play.uid` がその札で `models` に札が続くこと。
2. 捨てる選択（`FromHandForDiscard`）では `selector: "FromHand"`・`outer_selectors: ["FromHandForDiscard"]` であること。
   エントロピーの手番の初めの `hand_select` で `selector: "FromHand"`・`hand_source.model_id: "ENTROPY_POWER"`・
   `models[0].model_id: "ENTROPY_POWER"`・`hook.name: "AfterPlayerTurnStart"`・`card_play: null`、`contexts[0].kind: "hook"` であること。
   `player.transformation_options` が出て、選んだ札の変化先がその札の行の `options` に含まれること。
3. スプラッシュの choose で `selector: "FromChooseACardScreen"`・`models[0]` がスプラッシュの札（uid）・`card_play.uid` が同じ uid・
   `card_play.model_index: 0`・`hook.name: null`、複製で 2 回目なら `play_index: 1` であること。
4. 戦闘外の選択画面（休憩所の強化など）に `choice_source` が付かないこと。
5. 計略を持ち、手番の初めのドロー（`from_hand_draw: true`）の途中でシャッフルした選択の `choice_source.await_chain` が
   `known: true` で、`frames` が `StratagemPower.AfterShuffle` < `Hook.AfterShuffle`（`kind: "hook"`・`awaiting: "AfterShuffle"`・
   `model_index: 0`・`listener.position` が計略のパワーの位置）< `CardPileCmd.Shuffle` < `ShuffleIfNecessary` <
   `DrawInternal`（`remaining_draws` が引き残りの枚数〔今回の 1 枚を含む〕）< `CombatManager.SetupPlayerTurn`
   （`awaiting: "HandDraw"`）の順で、`shuffle_origin: "draw"` であること。選択を確定した後の手札の枚数が `remaining_draws` と
   合うこと。ポメルストライクなどのドロー札の途中では `from_hand_draw: false`、カスケードなど山札からの自動プレイの途中では
   `auto_play_from_draw_pile` の `reservation_call_id` が `card_resolution_events` の `auto_play_reservation` の `call_id` と同じで
   `shuffle_origin: "auto_play_from_draw_pile"`、リブートでは `shuffle_origin: "explicit"` であること。
6. エントロピーの手番の初めの選択の `await_chain` が `EntropyPower.AfterPlayerTurnStart` < `Hook.AfterPlayerTurnStart`
   （`awaiting: "AfterPlayerTurnStart"`・`following: ["AfterPlayerTurnStartLate"]`）< `SetupPlayerTurn`（`awaiting: "AfterPlayerTurnStart"`・
   `following: []`）< `RunAutoPrePlayPhase`（`awaiting: "SetupPlayerTurn"`・`following: ["CheckForEmptyHand", "AfterAutoPrePlayPhaseEntered"]`）
   であること。
