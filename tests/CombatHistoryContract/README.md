# 戦闘履歴 state の契約試験

本番の `McpMod.CombatHistory.cs` / `McpMod.CombatHistoryFormatting.cs` / `McpMod.CardIdentity.cs` /
`McpMod.Warnings.cs` を直接リンクする。追加パッケージ・ゲーム・Godot は不要。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/libexec/dotnet run --project tests/CombatHistoryContract -c Release -p:BuildInParallel=false -p:UseSharedCompilation=false
```

金の斧の倍率と同じ `card_plays_finished_this_combat`（`History.CardPlaysFinished.Count()`。別手番・他 Player・リプレイも数え、getter の失敗は 0 でなく null）、開始／完了の区別、全 CardType と 0、リプレイの重複、所有者の絞り込み、
本体のターン判定への引数、このターンと前手番の uid の順序・重複・他 Player のプレイを検査する。
廃棄は entry の数、被ダメは正の UnblockedDamage の件数をターン／戦闘で区別する。
不動のブロックは CardPlay・プレイした Player・Move を検査し、Receiver で絞らず、
同じ CardPlay の複数件をそのまま数える。
`Bound` は `CardAfflictedEntry` のこの手番・記録済み Actor が所有者・Affliction が Bound
という条件を検査し、別 Actor・null Actor・別手番・別 Affliction を除外する。
同じ札への複数回付与と現在の Affliction 解除後の履歴保持、明示的な 0、
履歴の再読取、途中の判定失敗による部分件数の破棄、wire の整数／null と理由・warnings、
Markdown の正の付与件数／失敗を検査する。既知の 0 は既存の Markdown 規約どおり省略する。

`any_manual_cards_played_this_turn` は `PaelsEyeHistoryChecks.cs` で空履歴の false、
開始だけの札、自動プレイ・他 Actor・null Actor・別手番の除外、
Actor と CardPlay.Player の食い違い、全 CardType・リプレイ、履歴リセットを検査する。
取得時に本体の `HappenedThisTurn` へ所有者の CombatState を渡すこと、
履歴とプレイ情報を変更しないこと、snapshot の独立も確認する。
本体 History／CardPlaysFinished／IsAutoPlay getter と手番判定の失敗・CombatState 欠落で
null・キー別理由・warnings と独立した値を保持し、復旧することを確認する。
wire の bool true／false／null と理由、Markdown の yes／no／null を確認する。
手動プレイの false は Markdown にも明示するため、空履歴でも History 行を表示する。

履歴のリセット後の再読取と snapshot の独立、History／Entries／開始・完了 getter／
型 getter／ターン判定の失敗、combat state／PlayerCombatState の欠落での
null・理由・warnings と他項目の保持、復旧を確認する。
JSON は本番の `_jsonOptions` と同じ WhenWritingNull・SnakeCaseLower・Encoder 設定で検査し、
数値／文字列／null 型、辞書の null が残ることと `by_type` の Attack 等のキーが変わらないことを確認する。
Markdown の `this turn=[...]` / `last turn=[...]`、非空・空・失敗表示も検査する。
巨大な state builder はリンクせず、ソースの文字列検査で戦闘限定・共通の Player builder・
UID registry の更新・マルチ・Markdown の配線と schema 12 を確認する。

`card_play_events_this_turn` は `CardPlayEventChecks.cs` で、空履歴の `[]`、本番の entity_id registry
（`McpMod.EntityIdentity.cs` をリンク）での敵の初見順の ID・戦闘から消えた敵の ID の保持・非敵の `entity_id: null`、
履歴順（親の開始 → 自動プレイの子の開始・完了 → 親の完了）、他 Player・別手番・プレイ以外の entry の除外、
リプレイの同 uid と `play_index` / `play_count`、開始だけの札、wire の `target: null`・bool・数値、
Markdown の `play events=[...]`、対象の `CombatId` 欠落・`IsAutoPlay` 取得失敗・途中の手番判定失敗での配列全体の null・理由・warning、
他の項目の保持、復旧・履歴リセット・前の snapshot の独立を検査する。

`card_resolution_events`（schema 1.6.0）は `ResolutionEventChecks.cs` で、空履歴の `[]`、前の手番の終わりの内側のドロー（`from_hand_draw: false`）と
自動プレイ・敵の手番の生成（`creator: null`）・次の手番の通常のドローと同じ札の再ドロー・自動プレイを、手番の条件なしに履歴順のまま 1 本で出すこと、
他 Player のドロー・生成・プレイとプレイ以外の entry の除外、所有者・他 Player が作った生成札の `creator`、開始だけの札、
後の snapshot の列が前の snapshot の列で始まること（接頭部）、既存の `card_play_events_this_turn` が今の手番だけのままであること、
play の行が既存の要素に `kind` を足した形であること、各行のキーと wire の bool・null・数値、Markdown の種類ごとの件数を検査する。
`FromHandDraw` の取得失敗・対象の `CombatId` 欠落・combat state の欠落で配列全体が null・理由・warning になり、他の項目を保持し、
復旧・履歴のリセットで空配列に戻り、前の snapshot が独立であることも確認する。

山札からの自動プレイの予約（schema 1.8.0）は `ReservationChecks.cs` で、本番の `McpMod.AutoPlayReservation.cs`（記録の台帳）を直接動かして検査する
（Harmony の patch の `McpMod.AutoPlayReservationPatches.cs` はリンクしない。patch が呼ぶ順に台帳の関数を呼ぶ）。カスケード X=4 で
荒廃・火傷（プレイ不可）・ストライク（リプレイで 2 回、終戦）・強打（終戦で残る）を確保し、荒廃が自分の予約（呼出し 2）を作る局面で、
呼出し・確保・消化の行が記録した時点の履歴の位置に入ること、親の呼出し・親の札・親プレイの開始行の添字、子のプレイの `reservation`、
プレイ不可の札の消化の後に起きたドローの順、終戦で消化されない札は確保の行だけになること、確保の途中の snapshot が後の列の接頭部であること、
別の context・順の違う札・対象付きの `AutoPlay`・他 Player の Play・保留していない札の移動を予約として扱わないこと、消化の後の別の自動プレイに
`reservation` が付かないこと、パワーの呼出し・山札にある親札（名前と uid を出さない）・モデルの無い context、各行のキーと wire の null・bool・数値、
Markdown の種類ごとの件数、patch が入っていない・記録の失敗・履歴より後ろの位置で配列全体が null になり、履歴のリセットで戻ることを確かめる。

ターン判定は境界ダブル。判定結果を設定し、本体メソッドへの委譲を確かめる。
uid registry は本番を実行し、パイルとこのターン・前手番の履歴が同じ個体を同じ uid で参照することを確認する。
本体のターン進行・履歴記録を再現した証拠ではない。
現行 DLL へのビルドとは別に、実機確認は調整役が v0.113.0 の検証用ランで行う。

1. 開始直後の 0／空配列と、Attack・Skill・Power の開始／完了数を照合する。
   リプレイ・自動プレイも entry ごとに増えることを確認する。
2. 廃棄のない手番／廃棄後で忘れられし儀式・イービルアイの判定を照合する。
3. ブロックで全て防いだ攻撃、複数 hit、自傷などで UnblockedDamage > 0 の件数を
   悪意・八つ裂きと照合する。ターン限定値と戦闘全体値の境界も確認する。
4. ボーラ／共鳴の手斧の前手番の uid を各パイル・手札と照合する。
   手番中に手斧を撃って完了したあとの `card_plays_finished_this_turn_card_uids` と、
   次手番の開始時に同じ uid の札が回収されるかを照合する。
   追加ターンでは player の TurnNumber と battle.round を区別する。
5. 不動を持ち、カードでの獲得とカード外の獲得、同じ CardPlay の複数回獲得を照合する。
   `block_gains_this_turn` は次のプレイの null 文脈の件数なので、落ち着いた state を使う。
6. 非戦闘で省略、新しい戦闘でリセット、マルチのローカル Player のみの集計を確認する
   （このターン／前手番の uid 集合は他 Player の札も含む）。
7. 拘束の鎖（`CHAINS_OF_BINDING_POWER`）の作用中、`bound_afflictions_this_turn` が付与前 0、付与ごとに +1、
   Amount 到達時にその件数となることを `ChainsOfBindingPower.AfterCardDrawn` と照合する。
   Bound 札を使用・移動して手札の Bound 枚数が減っても履歴件数は減らないこと、
   次手番ではその手番の新規付与だけを数えることも確認する。
8. v0.113.1 の検証戦闘で `relic add PAELS_EYE` を使い、
   手動プレイ前 false → 完了後 true、次手番の新規履歴で false を照合する。
   未使用のまま札を撃たずに手番を終えると手札廃棄・追加手番・`used_this_combat=true`、
   手動で札を撃ってから終えると通常の敵手番へ進み `used_this_combat=false` を確認する。
   自動プレイだけなら履歴値 false のまま、手動プレイを加えると true。
   `WhisperingEarring` 所持の初手番は履歴値 false でもパエルの眼の判定は true となり、
   追加手番を得ないことも確認する。
   マルチでは別 Player だけの手動プレイはローカルの履歴値を変えず、
   所有者の不参加フラグと消費済みフラグが追加手番を抑止することを確認する。

9. 荒廃（`HAVOC`）の自動プレイがある手番で、荒廃の前後の `card_play_events_this_turn` を照合する。
   山札の先頭が通常の札なら、荒廃の後に「`started HAVOC`（手動）→ `started` 子（`is_auto_play: true`）→ `finished` 子 → `finished HAVOC`」が追加され、
   子は山札から廃棄札へ移る（`Havoc.OnPlay` → `CardPileCmd.AutoPlayFromDrawPile(..., forceExhaust: true)` → `CardCmd.AutoPlay` → `CardModel.OnPlayWrapper`）。
   子が敵 1 体を取る札で敵が複数なら、`target` が実際に当たった敵の `entity_id` と一致すること（`CardCmd.AutoPlay` の `CombatTargets` の抽選）。
   怠惰（`SLOTH_POWER`）の上限で子が拒否される局面では、親の開始・完了だけが追加され子の event は出ない（`SlothPower.ShouldPlay`、
   `CardModel.MoveToResultPileWithoutPlaying`）。山札が空なら捨て札のシャッフルの後に予約される（`CardPileCmd.ShuffleIfNecessary`）。
   リプレイを持つ札では同じ uid の `play_index` 0..`play_count`-1 が順に並ぶこと。

ゲーム build・MOD commit・schema・前後の state を記録する。

醸造の `potion_uses` は、初回前の空、同じ枠への同じ ID の補充、使用者の切替、記録順、対象の安定 ID、
醸造がベルトから消えた後の履歴、読取失敗の null・理由・警告を検査する。

## Issue #1296 の多段攻撃用の入力

`ThumbscrewHistoryChecks.cs` は同じ本番 reader で、所有者と手番境界、CardPlay の実参照と支払い、星の正の増分、完了時のエセリアル、オスティ、旋律を検査する。同 round の側交代・追加手番・別戦闘、0・false・空・欠測も検査する。
