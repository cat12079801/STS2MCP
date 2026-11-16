# パワーの内部状態

`status[]` の各行は player・敵・ペットで共通。画面に見えるパワーに限り、
許可リストの項目を付ける。HUD 非表示・可視性不明のパワーの追加状態は省略する。
既存の `amount`・`display_amount`・`hidden_in_ui` の扱いは維持する。
追加契約のため `schema_version` は 4。項目の有無で利用可否を判別する。

以下の出典は v0.113.0 のデコンパイル `sts2-d009478a`。
人間の観測可能性は画面表示または画面で起きた出来事の計数に基づく。
MOD は履歴から再計算せず、その出来事を保持する現在のゲーム値を読む。

| 対象／項目 | ゲーム側の出典・意味 | 人間が知りうる根拠 |
|---|---|---|
| 全可視パワー `skip_next_duration_tick` | `PowerModel.SkipNextDurationTick`。`PowerCmd.Apply` は敵手番中の player への Debuff の新規インスタンス付与で true（既存インスタンスへの加算は `ModifyAmount` に戻り、このフラグを変更しない）、`TickDownDuration` は true を false に戻して減算を飛ばす。`Doubt.OnTurnEndInHand` / `Shame.OnTurnEndInHand` は手番終了時に手札にあり、対応する弱体／脆弱をもともと持たず、付与に成功した場合に true にする。非 duration でも元の bool を出す。 | 表示された新規付与と既存パワーへの加算の区別、手番終了時の手札の疑念／羞恥の効果、持続数の減少を数えられる。未表示のパワーへの付与はこの根拠を満たさないため対象外。 |
| 円環の強靭 `block_base_value` | `ToricToughnessPower.DynamicVars.Block.BaseValue`。`SetBlock` が設定し、`AfterBlockCleared` で使用。Amount は残り回数。 | `ToricToughness.OnPlay` が画面で獲得したブロック量を `SetBlock` に渡す。表示された獲得量に対応する値。decimal のまま。 |
| 包囲 `facing` | `SurroundedPower.Facing` の enum 名。 | `FaceDirection` / `FlipScale` が player・ペットの Body と FormVfxHolder の向きを反転する。画面に向きが見える。 |
| `PossessStrengthPower.stolen_strength` / `PossessSpeedPower.stolen_dexterity` | private `Data.stolenStrength` / `stolenDexterity`。`AfterPowerAmountChanged` でその敵による player の減少分（decimal、負数）を累積し、死亡時に対象 player ごとに逆符号で返す。 | 筋力／敏捷の減少と、減少させた敵の行動は画面に出る。対象別にその出来事を数えられる。総能力値から推測せず記録された減少だけを出す。 |
| 臆病 `has_gained_block_this_turn` | private `Data.hasGainedBlockThisTurn`。`AfterAttack` の初回非ブロックダメージで true、相手側 `AfterSideTurnEnd` で false。 | ブロック獲得、引っ込み／伸びるアニメーション（`BlockStart` / `BlockEnd`）が見える。 |
| 拘束の鎖（`CHAINS_OF_BINDING_POWER`） `bound_card_played` | private `ChainsOfBindingPower.Data.boundCardPlayed`。`BeforeCardPlayed` は IsDupe を除き、所有者の Bound 札で true（自動プレイも対象）。`ShouldPlay` が参照し、所有者が participants に含まれる `BeforeSideTurnEnd` で false。 | 表示された所有者の Bound 札のプレイを手番内で数えられる。複製札は除外し、現在の手札枚数からは推測しない。 |
| ジャグリング `attacks_played_this_turn` | private `Data.attacksPlayedThisTurn`。`AfterApplied` で当手番の開始済みアタック数に設定、`BeforeCardPlayed` で増加、所有者参加の `AfterSideTurnEnd` で 0。3 で複製を予約。3 に丸めない。 | 表示されたアタックのプレイを、付与前も含め手番内で数えられる。 |
| ジャグリング `card_to_clone_uid` / `card_to_clone_readable` | private `Data.cardToClone` を手札・パイル・履歴と同じ uid registry に通す。3回目の `BeforeCardPlayed` で対象を記録し、対象の `AfterCardPlayed` で参照を消して複製。 | 表示された3回目のアタックと複製の出来事から分かる予約状態。uid はその公開された札の個体を照合する。 |
| まるくなる（`CURL_UP_POWER`） `played_card_uid` / `played_card_readable` | private `CurlUpPower.Data.playedCard` を同じ uid registry に通す（出典は v0.113.1 `sts2-a671e9ef`。v0.113.0 `sts2-d009478a` と同一）。`AfterDamageReceived` は所有者への `IsPoweredAttack` かつ `cardSource` ありの被弾で、未記録または同じ札なら記録（別の札が記録済みなら無視）。`AfterCardPlayed` は `cardPlay.Card` が記録の札と同じときに参照を消し、Amount のブロックを得てパワーを外す。 | 画面に出た、その敵へのアタック札の命中から分かる追跡状態。uid はその公開された札の個体を照合する。 |

盗難の配列の各行は `{player_id, amount}`。`player_id` は `player.player_id` と同じ文字列。
マルチでも受取人を失わないよう合算しない。辞書が空なら空配列を出す。
将来の盗難量や敵の行動予定は出さない。

private Data は `PowerModel._internalData`、対象クラスの private nested `Data`、
許可したフィールドの宣言型を確認してリフレクションで読む。
`GetInternalData` / `InitInternalData`、ゲームフック、ゲームの乱数は呼ばない。
生成札の uid の採番には既存 registry の OS 乱数を使う。
`SkittishPower.HasGainedBlockThisTurn` の getter 経由でも読めるが、Data の境界は同じ方式で扱う。
読み取った辞書は player ID と decimal の行にコピーし、ゲームの参照を返さない。

各 getter／フィールドの欠落・型違い・予期しない null・コピー中の失敗は、その項目を `null` とし、
行内 `unavailable_reasons.<key>` とトップレベル `warnings` の
`status.<POWER_ID>.<key>` に理由を付ける。0 / false で補完しない。
説明文の取得に失敗した最小行も同じ読取経路を通り、他項目は保持する。

拘束の鎖の `bound_card_played` は読み取れた false も明示し、Markdown では
`bound card played: no` / `yes`、読取不能は `null (理由)` と表示する。
`PowerModel._internalData` を既存の `ReadPowerDataField<bool>` で読むだけで Data は初期化しない。
同じ手番の Bound 付与件数は別契約の `player.combat_history.bound_afflictions_this_turn`
（`ChainsOfBindingPower.AfterCardDrawn` と同じ履歴条件）であり、使用済みフラグと相互に代用しない。
どちらも schema 4 の追加項目なので、必要な各項目の存在と読取可否を確認する。

ジャグリングの予約は読み取れた場合に `card_to_clone_readable=true`、待ちなしは
`card_to_clone_uid=null` とし、この正常な null に理由や警告を付けない。
読取失敗は `card_to_clone_readable=false`、uid は `null` と
`unavailable_reasons.card_to_clone_uid`・`warnings`。カウンターの成否とは独立する。
Data の初期化や参照の変更は行わず、ゲームのカード参照を文字列 uid に置き換えて返す。
Markdown の正常な待ちなしは `none`、読取失敗は `null (理由)` と表示する。
`card_to_clone_readable=true` は Markdown で省略し、JSON に保持する。false は表示する。

まるくなる（`CURL_UP_POWER`）の追跡も同じ規則で、読み取れた場合に `played_card_readable=true`、追跡なしは
`played_card_uid=null`（理由・警告なし）。読取失敗は `played_card_readable=false`、
uid は `null` と `unavailable_reasons.played_card_uid`・`warnings`。
Markdown は追跡なしを `played card uid: none`、読取失敗を `null (理由)` と表示し、
`played_card_readable=true` は省略、false は表示する。
uid は既存 registry の `GetStableCardUid` で、デッキの札は `CARD_ID#<デッキ位置>`、
デッキに無い札（生成札・複製）は `CARD_ID#g<番号>` を新たに割り当てる。
uid が付かない札は無いので、札が分からないことによる null は起きない。
記録は `CardModel.OnPlayWrapper` の各プレイの `OnPlay`（とエンチャント・affliction の `OnPlay`）中の被弾で付き、同じプレイの
`Hook.AfterCardPlayed` で消える。このため解決後の snapshot では通常 `null` で、
`null` でない値が見えるのはその札の解決中（プレイ途中の選択を含む）か、
プレイを経ない `cardSource` 付きの被弾があった場合（該当する札は未確認）だけ。
消えると同時にブロックを得てパワーが外れるので、発動した後は status の行ごと無くなる。

Markdown は情報がある場合だけ既存 status 行に追記する。skip=false と空配列は省略し、
対象別の 0 / false は表示、失敗時は null と理由を表示する。
解決中の値はフック間の過渡状態なので、計画には解決後の snapshot を使う。
ジャグリングの予約参照とまるくなるの追跡は、解決後の snapshot では通常 `null`。
ジャグリングで `null` でない値が見えるのはその札の解決中（プレイ途中の選択を含む）だけ。

実機の起動・照合は行っていない。契約試験はゲーム境界のダブルに対して
本番の reader・formatter・warnings を直接実行する。
