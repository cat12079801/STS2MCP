# レリックの戦闘中の内部状態

`player.relics[].combat_state` は、戦闘の判断に使う内部値の追加フィールド。
単体とマルチのローカル player は共通の `BuildPlayerState` を通る。
生きた戦闘と PlayerCombatState があるときだけ出し、戦闘外と対象外レリックでは省略する。
戦闘上のカード選択・ポーズ等でも、同じ player が構築されれば含む。
既存項目の意味は変えないため、版は上げていない（schema 4 のときの追加）。旧 MOD・旧 dump では追加項目が省略される。

`combat_state`・`run_state` とは別に、全所有レリックの行に `internal_state`（`status`・`is_wax`・`floor_added_to_deck` と、PenNib・Vambrace・UnsettlingLamp・MusicBox・PaelsLegion の解決中のカード、PaelsTooth の預けたカード、GoldenCompass の幕）を出す。棚卸しの分類表から生成した許可リストで読む（schema 1.2.0、[入力契約](raw-full.md#棚卸しから生成した内部状態internal_stateschema-120)）。

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
公開 getter がある CentennialPuzzle・LavaLamp・PenNib・JossPaper の値は直接読む。
それ以外は `McpMod.RelicCombatState.cs` のクラス別 switch に列挙した private
フィールドと毛皮のコートの private プロパティだけを、名前・型を確認してリフレクションで読む。フィールドの一律列挙はしない。
読み取りでゲームを変更する getter、フック、乱数は呼ばない。
private 値の読取失敗・フィールド削除・型変更では該当キーを省略し、既存の
`warnings` に `player.relics.<ID>.combat_state.<key>` を記録する。
この省略契約は既存項目に適用する。下記のペン先・冥銭・毛皮のコート・鮮やかなスカーフは失敗したキーを
`null` にし、`combat_state.unavailable_reasons.<key>` に理由も付ける。
この4種では、全キーの失敗時にも `combat_state` を保持する。
呼び出し側も例外を捕捉し、型・公開 getter の変更等で読み取り全体が失敗した場合は
`combat_state` を `null` にし、レリック行の `unavailable_reasons.combat_state` と `warnings` の `player.relics.<ID>.combat_state` に記録する
（schema 12。`run_state` も同じ。対象外のレリックはキーが無く、失敗とは区別できる。Markdown は `(combat state: null (理由))`）。
そのレリックの基本情報や player の他の項目の構築は続ける。
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
| 孫子兵法 `ArtOfWar` | `any_attacks_played_this_turn` ← `_anyAttacksPlayedThisTurn`、`any_attacks_played_last_turn` ← `_anyAttacksPlayedLastTurn` | エナジー加算判定用の内部フラグ。`AfterCardPlayed`、`AfterSideTurnEnd` で当手番から前手番へ移す。`AfterEnergyReset` は 2 手番目以降に加算判定後、両方 false。`AfterCombatEnd` も両方 false。**所有者が行動を選ぶ時点の `any_attacks_played_last_turn` は常に false**（初手番は初期値、以降は `CombatManager.SetupPlayerTurn` の `AfterEnergyReset` で消費済み）。次の手番のエナジー加算条件には `any_attacks_played_this_turn` を使う。前手番フラグ true 中の `AfterCardPlayed` は早期 return するので一般的なプレイ履歴として扱わない。 |
| パエルの涙 `PaelsTears` | `had_leftover_energy` ← `_hadLeftoverEnergy` | 前回の所有者手番終了時の余剰エナジー有無。`BeforeSideTurnEnd` で上書き、`AfterSideTurnStart` の加算判定に利用。加算後も true が残り得る（未受領を意味しない）。`AfterCombatEnd` で false。 |
| ヴァンプレイス `Vambrace` | `block_gained_this_combat` ← `_blockGainedThisCombat` | 初回のカードのブロック倍化が消費されたか。`ModifyBlockMultiplicative`／`AfterModifyingBlockAmount`／`AfterCardPlayed`。`BeforeCombatStart` と `AfterCombatEnd` で false。 |
| 悪魔の舌 `DemonTongue` | `triggered_this_turn` ← `_triggeredThisTurn` | 所有者側手番中の初回非ブロックダメージの回復が消費されたか。`AfterDamageReceived` で true、所有者が参加する `BeforeSideTurnStart` で false。終了フックによるリセットはない。 |
| 懐中時計 `Pocketwatch` | `cards_played_last_turn` ← `_cardsPlayedLastTurn` | `ModifyHandDraw` の 3 枚以下判定。所有者が参加する `BeforeSideTurnStart` で当手番数を前手番数に移し、当手番数を 0 にする。`AfterCombatEnd` で両方 0。**行動を選ぶ時点の `cards_played_last_turn` は、すでに配り終えた今の手札のドロー判定に使われた値**（初手番は判定対象外）。`CombatManager.StartTurn` の `BeforeSideTurnStart` 後、`SetupPlayerTurn` が `AfterEnergyReset` → `BeforeHandDraw` → `ModifyHandDraw` → Draw の順で処理する。次の手番のドロー判定には当手番数の既存 `counter`（`_cardsPlayedThisTurn`）を使う。 |
| 赤い頭蓋骨 `RedSkull` | `strength_applied` ← `_strengthApplied` | このレリック由来の筋力が付与済みか。総筋力からは分離できない。`ModifyStrengthIfNecessary`（`AfterRoomEntered`／`AfterCurrentHpChanged`）で HP 閾値に応じ加除し、`AfterCombatEnd` で false。 |
| ベルトの留め具 `BeltBuckle` | `dexterity_applied` ← `_dexterityApplied` | このレリック由来の敏捷が付与済みか。`ApplyDexterity`／`RemoveDexterity`、ポーション取得・破棄・使用フックで変化。`BeforeCombatStart` と `AfterCombatVictory` で false にする。`AfterCombatEnd` は表示更新のみ。 |
| 護摩木 `BurningSticks` | `used_this_combat` ← `_wasUsedThisCombat` | 初回スキル廃棄のコピー作成が消費されたか。`AfterCardExhausted` で true、CombatRoom への `AfterRoomEntered` と `AfterCombatEnd` で false。 |
| オルゴール `MusicBox` | `used_this_turn` ← `_wasUsedThisTurn` | 初回アタックのコピー作成が消費されたか。`BeforeCardPlayed`／`AfterCardPlayed`。所有者が参加する `BeforeSideTurnStart` と `AfterCombatEnd` で false。 |
| パエルの眼 `PaelsEye` | `used_this_combat` ← `_usedThisCombat`、`was_owner_part_of_last_player_turn` ← `_wasOwnerPartOfLastPlayerTurn` | v0.114.0 `sts2-ae309f85` の `ShouldTakeExtraTurnEarly`／`BeforeSideTurnEndEarly` の追加手番条件。使用は `AfterTakingExtraTurn` で true、`AfterCombatEnd` で false。参加フラグは `AfterObtained` と未使用時の `AfterSideTurnStart` で更新する。後者は他プレイヤーのみの追加手番を区別し、終了時に戻さない。 |
| 手投げ斧 `ThrowingAxe` | `used_this_combat` ← `_usedThisCombat` | 初回カードの追加プレイを消費したか。`ModifyCardPlayCount`／`AfterModifyingCardPlayCount`。CombatRoom への `AfterRoomEntered` と `AfterCombatEnd` で false。 |
| パエルの肉兵 `PaelsLegion` | `cooldown` ← `_cooldown` | `ModifyBlockMultiplicative` は 0 以下なら倍化可能。`AfterCardPlayed` で 2、所有者が参加する `AfterSideTurnStart` で毎回 1 減算、`AfterCombatEnd` で 0。負値も原値のまま（0 に丸めない）。正数は `DisplayAmount` にも出るが、非正数は既存 `counter` が null となるため明示する。 |
| 脈打つ亡骸 `BeatingRemnant` | `damage_received_this_turn` ← `_damageReceivedThisTurn` | `ModifyHpLostAfterOsty` の残り HP 損失上限を決める decimal。`AfterDamageReceived` で `UnblockedDamage` を累積、所有者が参加する `BeforeSideTurnStart` で 0。敵側の手番開始では戻らない。 |
| ちびリージェント `MiniRegent` | `used_this_turn` ← `_usedThisTurn` | 初回の星消費に伴う筋力付与の消費。`AfterStarsSpent` で true、所有者が参加する `BeforeSideTurnStart` と `AfterCombatEnd` で false。 |
| レガライト `Regalite` | `used_this_turn` ← `_usedThisTurn` | 初回のカード生成に伴うブロックの消費。`AfterCardGeneratedForCombat` で true、所有者が参加する `BeforeSideTurnStart` と `AfterCombatEnd` で false。 |
| 朽ちた兜 `RuinedHelmet` | `used_this_combat` ← `_usedThisCombat` | 初回の正の筋力付与の倍化が消費されたか。`TryModifyPowerAmountReceived`／`AfterModifyingPowerAmountReceived`。`AfterCombatEnd` で false。 |
| 不気味なランプ `UnsettlingLamp` | `used_this_combat` ← `_isFinishedTriggering` | 名前は処理フラグに見えるが、`BeforePowerAmountChanged`／`ModifyPowerAmountGivenMultiplicative` が参照し、最初の対象カードの `AfterCardPlayed` 以降ずっと倍化を禁止する戦闘内消費。`BeforeCombatStart` と `AfterCombatEnd` で false。 |
| 溶岩のランプ `LavaLamp`（追加） | `took_damage_this_combat` ← 公開 `TookDamageThisCombat` (`_tookDamageThisCombat`) | 無傷を維持して報酬アップグレードを守る戦闘判断に使う。`AfterDamageReceived` は CombatRoom 内の所有者への正の非ブロックダメージを記録するが `Unblockable` は除外。`TryModifyCardRewardOptionsLate` が参照。`AfterRoomEntered` で部屋種別にかかわらず false。 |

## パエルの眼の手動プレイ判定（v0.114.0）

`PaelsEye.AnyCardsPlayedThisTurn` は本体の `CombatHistory.CardPlaysFinished` を読み、
`e.Actor == Owner.Creature && e.HappenedThisTurn(Owner.Creature.CombatState) && !e.CardPlay.IsAutoPlay`
を満たす完了 entry の有無を調べる。履歴側にこの値の元データがあるため、
`player.combat_history.any_manual_cards_played_this_turn`（bool）として公開する。
既存のレリック側の2フラグと併用する。false も明示し、読取失敗は null・キー別の理由・warnings を出す。

`AnyCardsPlayedThisTurn` は `PlayerCombatState.TurnNumber == 1` かつ `WhisperingEarring` 所持時に
履歴を調べる前に true を返す。この初手番は手動プレイが0件でも追加手番の条件を満たさない。
公開する履歴値はその場合も手動プレイの実際の有無を示す。
`ShouldTakeExtraTurnEarly` は未使用・この判定 false・前回参加・対象 Player が所有者の条件、
`BeforeSideTurnEndEarly` は所有者が participants に含まれる条件も検査する。
出典は `sts2-ae309f85` の `MegaCrit.Sts2.Core.Models.Relics.PaelsEye` の上記3メソッド。
式・履歴 entry の Actor・評価時点・失敗契約は
[戦闘履歴の要約](raw-full.md#戦闘履歴の要約追加schema-4) を参照。
所持の判定は `player.relics` に `WHISPERING_EARRING` があるかで行い、`melted` は見ない（本体は `Relics.Any(r => r is WhisperingEarring)` で、溶けたレリックも `_relics` に残る）。

## ペン先・冥銭・毛皮のコート・鮮やかなスカーフ（v0.113.0）

以下の出典は `sts2-d009478a` の同名クラス。
ペン先の `AttacksPlayed` と冥銭の `CardsExhausted` は副作用のない公開 getter を直接読む。
冥銭の `_etherealCount`、スカーフの `_cardsPlayedThisTurn` と毛皮のコートの private プロパティは名前・宣言型まで確認し、
読取失敗は `null`・`combat_state.unavailable_reasons.<key>`・トップレベル `warnings` の
`player.relics.<ID>.combat_state.<key>` にする。他項目の取得は続ける。
既存の `counter` や古い項目の失敗時省略は変更しない。

| レリック | 出す項目 ← 元の値／意味 | 人間が知りうる根拠 |
|---|---|---|
| ペン先 `PenNib` | `attacks_played` ← 公開 `AttacksPlayed` (`_attacksPlayed`)（int）。`BeforeCardPlayed` → `NotifyAttackPlayed` で増加し、setter で 10 の剰余。0〜9。戦闘をまたぐ。 | 通常の `DisplayAmount` と同じ画面カウンタ。発動演出中は表示が10になるので、原値を別項目に出す。アタックのプレイからも数えられる。 |
| 冥銭 `JossPaper` | `cards_exhausted` ← 公開 `CardsExhausted` (`_cardsExhausted`)（int）。非エセリアルの `AfterCardExhausted` で加算、`DrawIfThresholdMet` で閾値の剰余。 | 通常の表示カウンタ。発動中は閾値の表示に変わるが、見えた廃棄とドローから原値を数えられる。 |
| 冥銭 `JossPaper` | `ethereal_count` ← `_etherealCount`（int）。エセリアル廃棄を蓄積し、所有者参加の `AfterSideTurnEnd` で `CardsExhausted` に移して0。`AfterCombatEnd` も0。 | 手札のエセリアル札の廃棄が画面に出る。手番終了処理中の廃棄を数えた値。通常の次の行動時は0でも、解決中に値を作り替えない。 |
| 毛皮のコート `FurCoat` | `affects_current_room`（bool）。private プロパティ `FurCoatCoordsSet`・`FurCoatCoordCols` / `Rows` と `Owner.RunState.CurrentMapPoint.coord` を比較。未設定はfalse、配列長の不一致や現在座標の欠落はnull。 | 地図の部屋に `AddQuest(this)` の印が付き、入室時はコートの発動と敵HPを1にする演出が見える。現在の部屋だけを示す。 |
| 鮮やかなスカーフ `BrilliantScarf` | `cards_played_this_turn` ← private `_cardsPlayedThisTurn`（int）。`AfterCardPlayed` が戦闘中の所有者の非 AutoPlay を加算。所有者参加の `BeforeSideTurnStart` と `AfterCombatEnd` で0。 | 画面に出た手動プレイの計数。`ShowCounter` は `Cards`（この版では6）未満で表示、`ShouldModifyCost` は `Cards - 1`（5）で手札・Play の札を無料にする。6以降も実数を保持する。 |

毛皮のコートの `FurCoatActIndex`（`_furCoatActIndex`）は `AddMarkedRooms` が
印を設定する act を限定する値。v0.113.0 の `BeforeCombatStart` と
`AfterCreatureAddedToCombat` は **act を比較せず** `GetMarkedCoords` と現在座標だけで発動する。
そのため `affects_current_room` に act の追加条件を入れない。
別 act で同じ座標があっても、実際の戦闘フックに合わせた値を出す。
この場合も現在の部屋での発動・HP変化が観測根拠であり、将来の部屋の判定は公開しない。
座標配列・設定済みフラグには公開 getter がなく、private プロパティをリフレクションで読む。
コンパイラが生成する backing field 名には依存しない。
`GetMarkedCoords` は配列長の不整合を検査せずに座標へ変換するため、直接は呼ばず配列長を確認する。
マップを生成・変更する処理や乱数も呼ばない。

座標配列・act index・将来の適用先は出さない。
`_isActivating`、`PenNib._attackToDouble` も出さない。前者は演出用フラグ、後者は
カードの解決中だけ保持する参照であり、画面の永続状態として公開しない。
Markdown は `(attacks played: 9)`、`(cards exhausted: 4, ethereal count: 0)`、
`(affects current room: yes)` のように既存レリック行に付ける。
失敗時は `null (理由)`、空の `combat_state` は追記しない。

## 手番の数・オリハルコン（schema 12）

v0.114.0（`sts2-ae309f85`）の同名クラスを出典とする（`cat12079801/sts2#1020`）。private フィールドを名前・宣言型で読み、
失敗はキーを `null`・`combat_state.unavailable_reasons.<key>`・`warnings` の `player.relics.<ID>.combat_state.<key>` にする。
`counter` は `DisplayAmount` の表示値（数 % 閾値、`_isActivating` の演出中は閾値）なので、数そのものはこちらを読む。閾値・量は `vars`。

| レリック | 出す項目 ← 元の値 | 意味・戻る時機 |
|---|---|---|
| 苦無 `Kunai`・手裏剣 `Shuriken` | `attacks_played_this_turn` ← `_attacksPlayedThisTurn` | `AfterCardPlayed` が戦闘中の所有者のアタックを数え、`vars.Cards` の倍数で発動。所有者が参加する `BeforeSideTurnStart` と `AfterCombatEnd` で 0。 |
| 扇子 `OrnamentalFan` | `attacks_played_this_turn` ← `_attacksPlayedThisTurn` | 数え方は同じ。0 に戻すのは所有者が参加する `BeforeSideTurnStart` だけ。`AfterCombatEnd` は演出の印を戻すだけで数を残し、次の戦闘の最初の所有者の `BeforeSideTurnStart` まで前の戦闘の数が見える。 |
| 鎖鎌 `Kusarigama` | `attacks_played_this_turn` ← `_attacksPlayedThisTurn` | 数え方は同じ（発動はランダムな当たる敵へのダメージ）。0 に戻すのは `BeforeCombatStart`・所有者が参加する `AfterSideTurnEnd`・`AfterCombatEnd`。`BeforeSideTurnStart` では戻さないので、敵の手番の間は 0。 |
| レターオープナー `LetterOpener` | `skills_played_this_turn` ← `_skillsPlayedThisTurn` | `AfterCardPlayed` が戦闘中の所有者のスキルを数え、`vars.Cards` の倍数で発動。0 に戻すのは `BeforeCombatStart` と、所有者が参加する `AfterSideTurnStart`（`TurnNumber == 1` の手番は飛ばす）。`AfterCombatEnd` では戻さない。手番の初めのフックより前に数えたスキルは、その `AfterSideTurnStart` で消える。 |
| 懐中時計 `Pocketwatch` | `cards_played_this_turn` ← `_cardsPlayedThisTurn` | `AfterCardPlayed` が戦闘中の所有者の札を数える。所有者が参加する `BeforeSideTurnStart` で `_cardsPlayedLastTurn`（既存の `cards_played_last_turn`）へ移して 0。`ModifyHandDraw` は 2 手番目以降、前手番の数が `vars.CardThreshold` 以下なら `vars.Cards` を足す。 |
| オリハルコン `Orichalcum`・偽物 `FakeOrichalcum` | `should_trigger` ← `_shouldTrigger` | `BeforeSideTurnEndVeryEarly` が所有者のブロック 0 を見て true、`BeforeSideTurnEnd` が `vars.Block` を得て false、`BeforeSideTurnStart` も false。プレイヤーが行動する間は false で、true は手番終了の 2 つのフックの間だけ。 |

## 保存カウンタ（`run_state`、schema 12）

戦闘をまたいで残る `[SavedProperty]` の数・印は、戦闘中・戦闘外とも `player.relics[].run_state` に出す（`combat_state` は戦闘中だけ）。
private の裏のフィールドを名前・宣言型で読み、getter・setter・フック・乱数は呼ばない。失敗はキーを `null`・`run_state.unavailable_reasons.<key>`・
`warnings` の `player.relics.<ID>.run_state.<key>` にする。組み立て全体が失敗したとき（型の読み込み・JIT など）は `run_state: null` と
レリック行の `unavailable_reasons.run_state`・`warnings` の `player.relics.<ID>.run_state`。対象外のレリックにはキーが無い。出典は v0.114.0（`sts2-ae309f85`）の同名クラス。

| レリック | 出す項目 ← 元の値 | 意味・戻る時機 |
|---|---|---|
| ヌンチャク `Nunchaku` | `attacks_played` ← `_attacksPlayed` | `AfterCardPlayed` が所有者のアタックを数え、戦闘中に `vars.Cards` の倍数でエナジー。戦闘の終わりに戻さない。 |
| 音叉 `TuningFork` | `skills_played` ← `_skillsPlayed` | 所有者のスキルを数え、`vars.Cards`（`SkillsThreshold`）に届いたらブロックを得て閾値を引く。 |
| `IronClub` | `cards_played` ← `_cardsPlayed` | 所有者の札を数え、戦闘中に `vars.Cards` の倍数で 1 枚引く。 |
| `HappyFlower`・`FakeHappyFlower` | `turns_seen` ← `_turnsSeen` | 所有者の `BeforeSideTurnStart` で `(数 + 1) % vars.Turns`。0 なら `internal_state.grant_energy_this_turn` に公開するフラグを立て、所有者の `AfterPlayerTurnStartEarly` でその予約を解除してエナジーを付与する。手番数は戦闘終了でリセットしない。 |
| `Pendulum` | `turns_seen` ← `_turnsSeen` | `BeforeHandDraw` で `(数 + 1) % vars.Turns`、0 の手番の `ModifyHandDraw` で `vars.Cards` を足す。 |
| `PollinousCore` | `turns_seen` ← `_turnsSeen` | `BeforeHandDraw` で 1 足し、`vars.Turns` 以上なら `ModifyHandDraw` で `vars.Cards` を足し、`AfterModifyingHandDraw` で 0。 |
| `Girya` | `times_lifted` ← `_timesLifted` | 休憩所で持ち上げた回数。戦闘部屋に入ると同じ量の筋力を得る。3 未満の間は休憩所に選択肢を足す。 |
| `LastingCandy` | `combat_rewards_seen` ← `_combatRewardsSeen` | カード報酬のある戦闘報酬ごとに 1 足す（`BeforeCombatRewardOffered`）。奇数の間はカード報酬に 1 枚足す。 |
| `BookOfFiveRings` | `cards_added` ← `_cardsAdded` | デッキに入った札を数え（`AfterCardChangedPiles`）、`vars.Cards` の倍数で回復。 |
| `VenerableTeaSet`・`FakeVenerableTeaSet` | `gain_energy_in_next_combat` ← `_gainEnergyInNextCombat` | 休憩所に入ると true、次の戦闘の `AfterEnergyReset` で `vars.Energy` を得て false。 |
| `PumpkinCandle` | `kindle_count` ← `_kindleCount` | 0 より大きい間 `ModifyMaxEnergy` が `vars.Energy` を足す。`AfterCombatEnd` で 1 減る。 |
| `SwordOfStone` | `elites_defeated` ← `_elitesDefeated` | 倒したエリートの数。 |
| `WingedBoots` | `times_used` ← `_timesUsed` | 使った回数（`counter` は残り回数）。 |
| `SilverCrucible` | `times_used` ← `_timesUsed`、`treasure_rooms_entered` ← `_treasureRoomsEntered` | 使った回数と、入った宝箱部屋の数。 |
| `FishingRod` | `combats_seen` ← `_combatsSeen` | `AfterCombatEnd` が `RoomType.Monster` の戦闘だけを数える（エリート・ボスは数えない）。`counter` は `vars.Combats` の剰余。 |
| `ToyBox` | `combats_seen` ← `_combatsSeen` | `AfterCombatEnd` が、使い切る（`IsUsedUp`）までのすべての戦闘を数え、`vars.Combats` の倍数で発動。`counter` は使い切ると出ない。 |
| `WongosMysteryTicket` | `combats_finished` ← `_combatsFinished` | 終えた戦闘の数（`counter` は残り）。 |
| `PaelsWing` | `rewards_sacrificed` ← `_rewardsSacrificed` | 捧げた報酬の数（`counter` は `vars.Sacrifices` の剰余）。 |
| `LavaRock` | `has_triggered` ← `_hasTriggered` | 第 1 層ボスのレリック報酬を足し終えたか（`TryModifyRewards`）。 |

`_isActivating` は演出用なので出さない。Markdown は `(attacks played: 7)` の形で、`combat_state` の後に足す。

## `vars` の読み方（schema 12）

`vars` は private `RelicModel._dynamicVars` をそのまま読む（null なら作る `DynamicVars` の getter は呼ばない）。
集合が null・列挙の失敗は `vars: null`・行の `unavailable_reasons.vars`・`warnings` の `player.relics.<ID>.vars`、
1 変数の失敗はその名前だけ null・`unavailable_reasons["vars.<名前>"]`。以前はキーごと省略していた。

## 出さない値

| レリック／値 | 理由と出典 |
|---|---|
| パエルの肉兵 `PaelsLegion._triggeredBlockLastTurn` | `AfterCardPlayed` で true、`AfterSideTurnStart` で睡眠アニメーションの要否だけを判定して false。`AfterCombatEnd` も false。倍化判定に効くのは出力した `cooldown`。 |
| Vambrace／UnsettlingLamp の `_triggeringCard`、MusicBox の `_cardBeingPlayed`、PaelsLegion の `_affectedCardPlay`、UnsettlingLamp の `_doubledPowers` | 同じカードの解決中に効果をまとめる参照。各クラスの `BeforeCardPlayed`／`BeforePowerAmountChanged`／`AfterModifyingBlockAmount`／`AfterCardPlayed`、UnsettlingLamp の `HasDoubledTemporaryPowerSource`。次の行動用の消費フラグだけを出す。 |
| `_isActivating`（JossPaper、PenNib 等） | `DoActivateVisuals` の待機中の表示切替。戦闘能力の未使用／使用済みフラグではない。PenNib の `_attackToDouble` も `BeforeCardPlayed`〜`AfterCardPlayed` の解決中参照。 |
| 黄金の羅針盤 `GoldenCompass._goldenPathAct` | `AfterObtained`／`ModifyGeneratedMap`／`ModifyUnknownMapPointRoomTypes` のマップ・部屋設定用。戦闘中の行動に応じた消費状態ではない。 |
| `VelvetChoker` の手番の枚数 | private の数を持たず、`GetCardsPlayedThisTurn` が戦闘履歴から数える（v0.114.0）。`player.combat_history` と `counter` で読める。 |
| `StoneCalendar` | `_isActivating`（演出）しか無い。発動の手番は公開の `TurnNumber`（`combat_history.turn_number`）と `vars.DamageTurn` で決まる。 |
| `BoneTea`・`EmberTea`・`TeaOfDiscourtesy` の `_combatsLeft` | setter が `vars.Combats` に同じ値を書くので `vars` で読める（v0.114.0 の `CombatsLeft`）。 |
| `MawBank._hasItemBeenBought`、`BathRobes._wasUsed` | `IsUsedUp` が既存 `used_up` に出る。 |
| `Byrdpip._swoopsPlayed` | 休憩所の進化の選択肢（`TryModifyRestSiteOptions`、6 以上）に効くが、`counter`（`DisplayAmount => Math.Min(SwoopsPlayed, 6)`）で閾値の 6 までは正確に読める。 |
| `Byrdpip._skin`、`Byrdine._hair`、`PaelsLegion._skin` | ペットの外見。 |
| `Wuppy._currentHp` / `_maxHp`・`_combatWuppy` | 保存 HP は `counter` と `vars.CurrentHp` / `vars.MaxHp`。戦闘中の個体は `Relics.Wuppy.CombatWuppy` と照合して [所有者の `pets[]`](raw-full.md#戦闘中のワッピーschema-1160) に出す。 |
| `ArchaicTooth`・`DustyTome`・`PaelsTooth`・`TouchOfOrobas`・`SeaGlass` の保存データ | 取得時のカード・レリックの選択の記録。戦闘・報酬の判断の入力ではない。 |
| `SneckoEye`・`FakeSneckoEye._testEnergyCostOverride` | テスト用のコスト上書き。 |
| `SilkenTress._isUsed`、`WongosMysteryTicket._gaveRelic`、`LizardTail._wasUsed` | それぞれの `IsUsedUp` が既存 `used_up` に出るため重複追加しない。 |

v0.114.0（`sts2-ae309f85`）のアイアンクラッド・共通・イベントの pool の全レリックで、private フィールド宣言と自動プロパティを横断して候補を再確認した
（`cat12079801/sts2#1020`）。上記以外は `combat_state` か `run_state` に出す。他キャラの pool のレリックは対象外。
解決中（`is_resolving: true`）は各フック間の状態が見え得るため、行動計画には解決後の
snapshot を使う。これはゲームの全内部状態を復元する契約ではない。

## 実機確認

2026-11-04、調整役が MOD `d1c3a46`（schema 3）の単独プレイで確認した記録に基づく。

- 百年パズル: 初回の HP 損失で `used_this_combat` が false→true、3 枚ドロー。
  2 回目は true のままで追加ドローなし。Markdown の未使用時の注記も確認。
- 虹の指輪: アタック→スキル→パワーで 4 値が `(1,0,0,0)` → `(1,1,0,0)` → `(1,1,1,1)`。
  筋力・敏捷を各 +1。追加プレイ後も値は据え置き、次手番に全値 0。
- 永久氷晶: 初回パワーで `activated_this_combat` が false→true、ブロック +7。
  2 枚目では追加ブロックなし。次手番も true。
- 戦闘終了後は `combat_state` を省略し、次戦闘では百年パズル false、虹の指輪全値 0、
  永久氷晶 false。次戦闘開始時の `warnings` なし。

今回の例外処理追加後の実機再確認、他のレリック、最新安定版との一致は未確認。
