# レリックの戦闘中の内部状態

`player.relics[].combat_state` は、戦闘の判断に使う内部値の追加フィールド。
単体とマルチのローカル player は共通の `BuildPlayerState` を通る。
生きた戦闘と PlayerCombatState があるときだけ出し、戦闘外と対象外レリックでは省略する。
戦闘上のカード選択・ポーズ等でも、同じ player が構築されれば含む。
既存項目の意味は変えないため、schema は 3 のまま。旧 MOD・旧 dump では省略される。

```json
{
  "id": "CENTENNIAL_PUZZLE",
  "used_up": false,
  "melted": false,
  "combat_state": { "used_this_combat": true }
}
```

値は bool / int / decimal のまま出す。false と 0 も省略しない。
`used_this_combat` は当該戦闘での消費であり、ラン全体の `used_up` とは独立する。
公開 getter がある CentennialPuzzle と LavaLamp は直接読む。
それ以外は `McpMod.RelicCombatState.cs` のクラス別 switch に列挙した private
フィールドだけを、名前・型を確認してリフレクションで読む。フィールドの一律列挙はしない。
読み取りでゲームを変更する getter、フック、乱数は呼ばない。
private 値の読取失敗・フィールド削除・型変更では該当キーを省略し、既存の
`warnings` に `player.relics.<ID>.combat_state.<key>` を記録する。
全キーの読取に失敗した場合は `combat_state` 自体も省略する。
欠落を false / 0 と解釈しないこと。

Markdown は `(used this combat: yes)`、`(used this turn: no)`、
`(attacks this turn: 1, skills this turn: 1, powers this turn: 0, activations this turn: 0)`
等を既存の `(used)` / `(melted)` に追加する。false も `no` として表示する。
既存の `counter` は維持する。

以下は sts2-9cb4f1ad の `MegaCrit.Sts2.Core.Models.Relics` のクラスを出典とする。
和名はゲーム日本語文に基づくレリック一覧と照合した。
Issue 草稿の「永久凍土」「燃える枝」「パエルの目」「ミニリージェント」「リガライト」は、
それぞれ「永久氷晶」「護摩木」「パエルの眼」「ちびリージェント」「レガライト」と記す。

## 出す値（許可リスト）

表の項目名はすべて `combat_state` 内。引用元は同じ行のクラスのメソッド／プロパティ。
リセットはフックの実装どおりに記し、戦闘開始・終了で一律に補正しない。

| レリック／クラス | 出す項目 ← 元の値 | 判断に使う意味・戻る時機・出典 |
|---|---|---|
| 百年パズル `CentennialPuzzle` | `used_this_combat` ← 公開 `UsedThisCombat` (`_usedThisCombat`) | 初回の非ブロックダメージによるドローの消費。`AfterDamageReceived` で true、`AfterCombatEnd` で false。 |
| 虹の指輪 `RainbowRing` | `attacks_played_this_turn` ← `_attacksPlayedThisTurn`、`skills_played_this_turn` ← `_skillsPlayedThisTurn`、`powers_played_this_turn` ← `_powersPlayedThisTurn`、`activation_count_this_turn` ← `_activationCountThisTurn` | 未達のカード種別と発動済みかを判別。`AfterCardPlayed` は発動回数が 1 未満の間だけ加算するため、発動後の総プレイ枚数ではない。所有者が参加する `BeforeSideTurnStart` と `AfterCombatEnd` で全値 0。 |
| 永久氷晶 `Permafrost` | `activated_this_combat` ← `_activatedThisCombat` | 初回パワーの 7 ブロック消費。`AfterCardPlayed` で true。`AfterRoomEntered` の CombatRoom 入室で false（終了時リセットはない）。 |
| 孫子兵法 `ArtOfWar` | `any_attacks_played_this_turn` ← `_anyAttacksPlayedThisTurn`、`any_attacks_played_last_turn` ← `_anyAttacksPlayedLastTurn` | 次のエナジー加算条件。`AfterCardPlayed`、`AfterSideTurnEnd` で当手番から前手番へ移す。`AfterEnergyReset` は 2 手番目以降に加算判定後、両方 false。`AfterCombatEnd` も両方 false。前手番フラグ true 中の `AfterCardPlayed` は早期 return するので一般的なプレイ履歴として扱わない。 |
| パエルの涙 `PaelsTears` | `had_leftover_energy` ← `_hadLeftoverEnergy` | 前回の所有者手番終了時の余剰エナジー有無。`BeforeSideTurnEnd` で上書き、`AfterSideTurnStart` の加算判定に利用。加算後も true が残り得る（未受領を意味しない）。`AfterCombatEnd` で false。 |
| ヴァンプレイス `Vambrace` | `block_gained_this_combat` ← `_blockGainedThisCombat` | 初回のカードのブロック倍化が消費されたか。`ModifyBlockMultiplicative`／`AfterModifyingBlockAmount`／`AfterCardPlayed`。`BeforeCombatStart` と `AfterCombatEnd` で false。 |
| 悪魔の舌 `DemonTongue` | `triggered_this_turn` ← `_triggeredThisTurn` | 所有者側手番中の初回非ブロックダメージの回復が消費されたか。`AfterDamageReceived` で true、所有者が参加する `BeforeSideTurnStart` で false。終了フックによるリセットはない。 |
| 懐中時計 `Pocketwatch` | `cards_played_last_turn` ← `_cardsPlayedLastTurn` | `ModifyHandDraw` の 3 枚以下判定。所有者が参加する `BeforeSideTurnStart` で当手番数を前手番数に移し、当手番数を 0 にする。`AfterCombatEnd` で両方 0。当手番数は既存 `counter` を使う。 |
| 赤い頭蓋骨 `RedSkull` | `strength_applied` ← `_strengthApplied` | このレリック由来の筋力が付与済みか。総筋力からは分離できない。`ModifyStrengthIfNecessary`（`AfterRoomEntered`／`AfterCurrentHpChanged`）で HP 閾値に応じ加除し、`AfterCombatEnd` で false。 |
| ベルトの留め具 `BeltBuckle` | `dexterity_applied` ← `_dexterityApplied` | このレリック由来の敏捷が付与済みか。`ApplyDexterity`／`RemoveDexterity`、ポーション取得・破棄・使用フックで変化。`BeforeCombatStart` と `AfterCombatVictory` で false にする。`AfterCombatEnd` は表示更新のみ。 |
| 護摩木 `BurningSticks` | `used_this_combat` ← `_wasUsedThisCombat` | 初回スキル廃棄のコピー作成が消費されたか。`AfterCardExhausted` で true、CombatRoom への `AfterRoomEntered` と `AfterCombatEnd` で false。 |
| オルゴール `MusicBox` | `used_this_turn` ← `_wasUsedThisTurn` | 初回アタックのコピー作成が消費されたか。`BeforeCardPlayed`／`AfterCardPlayed`。所有者が参加する `BeforeSideTurnStart` と `AfterCombatEnd` で false。 |
| パエルの眼 `PaelsEye` | `used_this_combat` ← `_usedThisCombat`、`was_owner_part_of_last_player_turn` ← `_wasOwnerPartOfLastPlayerTurn` | `ShouldTakeExtraTurn`／`BeforeSideTurnEndEarly` の追加手番条件。使用は `AfterTakingExtraTurn` で true、`AfterCombatEnd` で false。参加フラグは `AfterObtained` と未使用時の `AfterSideTurnStart` で更新する。後者は他プレイヤーのみの追加手番を区別し、終了時に戻さない。 |
| 手投げ斧 `ThrowingAxe` | `used_this_combat` ← `_usedThisCombat` | 初回カードの追加プレイを消費したか。`ModifyCardPlayCount`／`AfterModifyingCardPlayCount`。CombatRoom への `AfterRoomEntered` と `AfterCombatEnd` で false。 |
| パエルの肉兵 `PaelsLegion` | `cooldown` ← `_cooldown` | `ModifyBlockMultiplicative` は 0 以下なら倍化可能。`AfterCardPlayed` で 2、所有者が参加する `AfterSideTurnStart` で毎回 1 減算、`AfterCombatEnd` で 0。負値も原値のまま（0 に丸めない）。正数は `DisplayAmount` にも出るが、非正数は既存 `counter` が null となるため明示する。 |
| 脈打つ亡骸 `BeatingRemnant` | `damage_received_this_turn` ← `_damageReceivedThisTurn` | `ModifyHpLostAfterOsty` の残り HP 損失上限を決める decimal。`AfterDamageReceived` で `UnblockedDamage` を累積、所有者が参加する `BeforeSideTurnStart` で 0。敵側の手番開始では戻らない。 |
| ちびリージェント `MiniRegent` | `used_this_turn` ← `_usedThisTurn` | 初回の星消費に伴う筋力付与の消費。`AfterStarsSpent` で true、所有者が参加する `BeforeSideTurnStart` と `AfterCombatEnd` で false。 |
| レガライト `Regalite` | `used_this_turn` ← `_usedThisTurn` | 初回のカード生成に伴うブロックの消費。`AfterCardGeneratedForCombat` で true、所有者が参加する `BeforeSideTurnStart` と `AfterCombatEnd` で false。 |
| 朽ちた兜 `RuinedHelmet` | `used_this_combat` ← `_usedThisCombat` | 初回の正の筋力付与の倍化が消費されたか。`TryModifyPowerAmountReceived`／`AfterModifyingPowerAmountReceived`。`AfterCombatEnd` で false。 |
| 不気味なランプ `UnsettlingLamp` | `used_this_combat` ← `_isFinishedTriggering` | 名前は処理フラグに見えるが、`BeforePowerAmountChanged`／`ModifyPowerAmountGivenMultiplicative` が参照し、最初の対象カードの `AfterCardPlayed` 以降ずっと倍化を禁止する戦闘内消費。`BeforeCombatStart` と `AfterCombatEnd` で false。 |
| 溶岩のランプ `LavaLamp`（追加） | `took_damage_this_combat` ← 公開 `TookDamageThisCombat` (`_tookDamageThisCombat`) | 無傷を維持して報酬アップグレードを守る戦闘判断に使う。`AfterDamageReceived` は CombatRoom 内の所有者への正の非ブロックダメージを記録するが `Unblockable` は除外。`TryModifyCardRewardOptionsLate` が参照。`AfterRoomEntered` で部屋種別にかかわらず false。 |

## 出さない値

| レリック／値 | 理由と出典 |
|---|---|
| オリハルコン `Orichalcum._shouldTrigger` | 手番終了中の二つのフック間だけの予約。`BeforeSideTurnEndVeryEarly` がブロック 0 を確認して true、`BeforeSideTurnEnd` が消費して false。所有者の `BeforeSideTurnStart` も false。行動選択時点の条件は現在ブロックで判断できる。 |
| オリハルコン？ `FakeOrichalcum._shouldTrigger` | 本物と同じ予約・リセット順。`BeforeSideTurnEndVeryEarly`／`BeforeSideTurnEnd`／`BeforeSideTurnStart`。 |
| 冥銭 `JossPaper._etherealCount` | エセリアル廃棄のドローを手札フラッシュ後まで遅延するための数。`AfterCardExhausted` で蓄積し、同じ手番終了処理の `AfterSideTurnEnd` で `CardsExhausted` に移して 0、`AfterCombatEnd` も 0。次の行動選択まで持ち越す状態ではない。永続廃棄数は `DisplayAmount`／既存 `counter`。 |
| パエルの肉兵 `PaelsLegion._triggeredBlockLastTurn` | `AfterCardPlayed` で true、`AfterSideTurnStart` で睡眠アニメーションの要否だけを判定して false。`AfterCombatEnd` も false。倍化判定に効くのは出力した `cooldown`。 |
| Vambrace／UnsettlingLamp の `_triggeringCard`、MusicBox の `_cardBeingPlayed`、PaelsLegion の `_affectedCardPlay`、UnsettlingLamp の `_doubledPowers` | 同じカードの解決中に効果をまとめる参照。各クラスの `BeforeCardPlayed`／`BeforePowerAmountChanged`／`AfterModifyingBlockAmount`／`AfterCardPlayed`、UnsettlingLamp の `HasDoubledTemporaryPowerSource`。次の行動用の消費フラグだけを出す。 |
| `_isActivating`（JossPaper、PenNib 等） | `DoActivateVisuals` の待機中の表示切替。戦闘能力の未使用／使用済みフラグではない。PenNib の `_attackToDouble` も `BeforeCardPlayed`〜`AfterCardPlayed` の解決中参照。 |
| 古びたティーセット `VenerableTeaSet._gainEnergyInNextCombat` と偽物 `FakeVenerableTeaSet` | `AfterRoomEntered(RestSiteRoom)` で予約、`AfterEnergyReset` でエナジー付与後 false。最初の行動選択では付与済みで既存 `energy` に反映済み。戦闘外の次戦闘開始条件は今回の戦闘中状態の対象外。公開 `GainEnergyInNextCombat` があることも確認。 |
| 毛皮のコート `FurCoat._furCoatActIndex` | `AfterObtained`／`AddMarkedRooms` のマップ設定用 act。戦闘では `BeforeCombatStart`／`AfterCreatureAddedToCombat` が別の `GetMarkedCoords` を利用し、act 値自体は参照しない。マップ設定・座標の公開は今回のスカラー状態の対象外。 |
| 黄金の羅針盤 `GoldenCompass._goldenPathAct` | `AfterObtained`／`ModifyGeneratedMap`／`ModifyUnknownMapPointRoomTypes` のマップ・部屋設定用。戦闘中の行動に応じた消費状態ではない。 |
| 溶岩石 `LavaRock._hasTriggered` | `TryModifyRewards` の第 1 層ボス報酬を一度だけ追加する保存フラグ。戦闘行動の結果による分岐ではない。 |
| `BrilliantScarf._cardsPlayedThisTurn`、`VelvetChoker._cardsPlayedThisTurn` 等、既存カウンタに出る数 | `DisplayAmount` で取得済み。BrilliantScarf は `ShowCounter` が 5 枚以降 false になるが、`ShouldModifyCost` は 4 枚のときだけ true なので、隠れた後の正確な枚数に次の効果差がない。重複追加しない。 |
| `SilkenTress._isUsed`、`WongosMysteryTicket._gaveRelic`、`LizardTail._wasUsed` | それぞれの `IsUsedUp` が既存 `used_up` に出るため重複追加しない。 |

全レリッククラスの private フィールド宣言と自動プロパティを横断して候補を再確認した。
上記以外の保存カウンタ、外見、カード選択の保存データ、テスト用コスト上書きは対象外。
実機での値変化・最新安定版との一致は未確認。実機確認は別途行う。
解決中（`is_resolving: true`）は各フック間の状態が見え得るため、行動計画には解決後の
snapshot を使う。これはゲームの全内部状態を復元する契約ではない。
