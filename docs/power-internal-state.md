# パワーの内部状態

`status[]` の各行は player・敵・ペットで共通。画面に見えるパワーに限り、
許可リストの項目を付ける。HUD 非表示・可視性不明のパワーの追加状態は省略する。
例外は泥棒（`THIEVERY_POWER`）と強奪（`HEIST_POWER`）の対象別の項目と、活力（`VIGOR_POWER`）の
`vigor_attack`、呪術（`HEX_POWER`）の付与元、儀式（`RITUAL_POWER`）の `was_just_applied_by_enemy`（schema 6）、
札の効果の数値（爆弾〔`THE_BOMB_POWER`〕の `damage`、`INFERNO_POWER` / `CRIMSON_MANTLE_POWER` の `self_damage`、
`PANACHE_POWER` の `cards_left` / `already_applied`。schema 7）、
弱化（`DAMPEN_POWER`）の `caster_combat_ids` / `downgraded_cards`（schema 8）、
全パワーの `type_for_current_amount` / `is_temporary_power`（schema 9）と `dynamic_vars`、下表の schema 12 の private Data・フィールドの項目で、
`status[]` に出る行には可視性によらず付ける（下記）。
既存の `amount`・`display_amount`・`hidden_in_ui` の扱いは維持する。
schema 4 の項目は版を上げずに足したので、項目の有無で利用可否を判別する。schema 6・7・8・9・12 の項目は版を上げて足したので、版で必須にしてよい。

以下の出典は v0.113.0 のデコンパイル `sts2-d009478a`。
人間の観測可能性は画面表示または画面で起きた出来事の計数に基づく。
MOD は履歴から再計算せず、その出来事を保持する現在のゲーム値を読む。

| 対象／項目 | ゲーム側の出典・意味 | 人間が知りうる根拠 |
|---|---|---|
| 全パワー `type_for_current_amount` | `PowerModel.TypeForCurrentAmount`（`GetTypeForAmount(Amount)`）の enum 名 `None` / `Buff` / `Debuff`（出典は v0.113.1 `sts2-a671e9ef`。schema 9、`cat12079801/sts2#1034`）。`GetTypeForAmount` は、`StackType` が `Counter` で `AllowNegative` のパワーの負の量を `Debuff`、`AllowNegative` でない `Debuff` 型の負の量を `Buff` とし、それ以外は `Type` を返す。`AllowNegative` なのは `StrengthPower`・`DexterityPower`・`FocusPower`（`Type` は `Buff`）と、もともと `Debuff` の `ShriekPower`・`ShrinkPower` なので、型が入れ替わるのは負の筋力・敏捷・集中（`Debuff`）。`AllowNegative` でないパワーは量が 0 以下で外れる（`ShouldRemoveDueToAmount`）。既存の `type` は従来どおり `PowerModel.Type` で、負の筋力も `Buff`。破壊（`Rend.ShouldCountPower`）は対象の `Creature.Powers` のうち、これが `Debuff` で一時パワーでないものを数える。`status[]` が落とすのは `IsVisible` が false・`Amount` が 0・現在の型が `Debuff` でない行だけ（現在の型が読めない行も残す）なので、数えるパワーは必ず行に出る。getter は `_amount` と `Type` / `StackType` / `AllowNegative` を読むだけ（`MonologuePower.StackType` は生きたパワーが持つ `DynamicVars` を読む）で、フック・乱数・書き込みは無い。読取失敗は `Type` で補わず null・理由・warnings。 | 筋力などの数字の符号と色（`AmountLabelColor` は `GetTypeForAmount(Amount)` が `Debuff` なら debuff の色）は画面に出る。 |
| 全パワー `is_temporary_power` | パワーのインスタンスが `ITemporaryPower` を実装するか（v0.113.1、schema 9）。実装するのは `TemporaryStrengthPower`・`TemporaryDexterityPower`・`TemporaryFocusPower`・`TemporaryThornsPower` とその派生（`FlexPotionPower`・`ManglePower`・`DarkShacklesPower`・`ShacklingPotionPower` など）。これらは付与の前（`BeforeApplied`）に内部の筋力などを別の行として付け、手番の終わりに自分を外して戻す。`Rend`・`DebufferModel`・`SleightOfFleshPower`・`IllusionPower` は一時パワーを数えない。型の検査なので読取失敗は起きないが、同じ読取経路を通す。 | パワーの名前・説明と、付けた札・ポーション・レリックは画面に出る。 |
| 全パワー `dynamic_vars` | パワー自身の `DynamicVars` を `{変数名: BaseValue}` の object で（出典は v0.113.1 `sts2-a671e9ef`。schema 12、`cat12079801/sts2#1020`）。`PowerModel.DynamicVars` は `CanonicalVars`（既定は空）から作られ、`DeepCloneFields` が生きたパワーごとに複製する。効果は `DynamicVar.BaseValue`（ただのフィールド）を読み書きする（インフェルノ・深紅の衣の `SelfDamage`、爆弾の `Damage`、威風堂々の `CardsLeft`、脆弱の `DamageIncrease`・脱力の `DamageDecrease`、プレートアーマーの `Decrement`、`SlowPower` の `SlowAmount` など）。変数名は `CanonicalVars` の名前のまま（snake_case にしない）、並びは集合の順。数値の意味を持たない `StringVar`（バリケードの `ApplierName` など）は出さない（札・エンチャントの `dynamic_vars` と同じ）。private `PowerModel._dynamicVars` をそのまま読み、作りながら返す getter は呼ばない。フィールドが null なら項目ごと null・`unavailable_reasons.dynamic_vars`・warnings（`{}` にしない）。読めた空の集合は `{}`。1 つの変数が読めなければその名前を null・`unavailable_reasons["dynamic_vars.<名前>"]`・warnings とし、他の変数は残す。型ごとの既存の項目（`damage`・`self_damage`・`cards_left`・`block_base_value`・`stolen_gold`）は同じ値をそのまま出し続ける。 | 説明文の組み立て（`PowerModel` の説明文で `DynamicVars.AddTo`）に渡される値で、ホバーの説明文の数値として画面に出る（説明文が使わない変数もある）。 |
| 全可視パワー `skip_next_duration_tick` | `PowerModel.SkipNextDurationTick`。`PowerCmd.Apply` は敵手番中の player への Debuff の新規インスタンス付与で true（既存インスタンスへの加算は `ModifyAmount` に戻り、このフラグを変更しない）、`TickDownDuration` は true を false に戻して減算を飛ばす。`Doubt.OnTurnEndInHand` / `Shame.OnTurnEndInHand` は手番終了時に手札にあり、対応する弱体／脆弱をもともと持たず、付与に成功した場合に true にする。非 duration でも元の bool を出す。 | 表示された新規付与と既存パワーへの加算の区別、手番終了時の手札の疑念／羞恥の効果、持続数の減少を数えられる。未表示のパワーへの付与はこの根拠を満たさないため対象外。 |
| 円環の強靭 `block_base_value` | `ToricToughnessPower.DynamicVars.Block.BaseValue`。`SetBlock` が設定し、`AfterBlockCleared` で使用。Amount は残り回数。 | `ToricToughness.OnPlay` が画面で獲得したブロック量を `SetBlock` に渡す。表示された獲得量に対応する値。decimal のまま。 |
| 包囲 `facing` | `SurroundedPower.Facing` の enum 名。 | `FaceDirection` / `FlipScale` が player・ペットの Body と FormVfxHolder の向きを反転する。画面に向きが見える。 |
| `PossessStrengthPower.stolen_strength` / `PossessSpeedPower.stolen_dexterity` | private `Data.stolenStrength` / `stolenDexterity`。`AfterPowerAmountChanged` でその敵による player の減少分（decimal、負数）を累積し、死亡時に対象 player ごとに逆符号で返す。 | 筋力／敏捷の減少と、減少させた敵の行動は画面に出る。対象別にその出来事を数えられる。総能力値から推測せず記録された減少だけを出す。 |
| 臆病 `has_gained_block_this_turn` | private `Data.hasGainedBlockThisTurn`。`AfterAttack` の初回非ブロックダメージで true、相手側 `AfterSideTurnEnd` で false。 | ブロック獲得、引っ込み／伸びるアニメーション（`BlockStart` / `BlockEnd`）が見える。 |
| 拘束の鎖（`CHAINS_OF_BINDING_POWER`） `bound_card_played` | private `ChainsOfBindingPower.Data.boundCardPlayed`。`BeforeCardPlayed` は IsDupe を除き、所有者の Bound 札で true（自動プレイも対象）。`ShouldPlay` が参照し、所有者が participants に含まれる `BeforeSideTurnEnd` で false。 | 表示された所有者の Bound 札のプレイを手番内で数えられる。複製札は除外し、現在の手札枚数からは推測しない。 |
| ジャグリング `attacks_played_this_turn` | private `Data.attacksPlayedThisTurn`。`AfterApplied` で当手番の開始済みアタック数に設定、`BeforeCardPlayed` で増加、所有者参加の `AfterSideTurnEnd` で 0。3 で複製を予約。3 に丸めない。 | 表示されたアタックのプレイを、付与前も含め手番内で数えられる。 |
| ジャグリング `card_to_clone_uid` / `card_to_clone_readable` | private `Data.cardToClone` を手札・パイル・履歴と同じ uid registry に通す。3回目の `BeforeCardPlayed` で対象を記録し、対象の `AfterCardPlayed` で参照を消して複製。 | 表示された3回目のアタックと複製の出来事から分かる予約状態。uid はその公開された札の個体を照合する。 |
| 泥棒（`THIEVERY_POWER`） `stolen_gold` | `ThieveryPower.DynamicVars.Gold.IntValue`（出典は v0.113.1 `sts2-a671e9ef`）。`CanonicalVars` の `GoldVar(0)` から始まり、`Steal` が対象の player から実際に失わせた額（`min(Amount, 所持金)`。対象が死亡・所持金 0 なら何もしない）を `Gold.BaseValue` に足す。`SurprisePower.AfterDeath` はこの値を強奪の量として引き継ぐ。`Amount` は 1 回の上限で、盗んだ累計ではない。 | 盗まれたゴールドは player の所持金の減少として画面に出る。対象別に数えられる。 |
| 泥棒 `target_player_id` / 強奪（`HEIST_POWER`） `target_player_id` | `PowerModel.Target.Player.NetId` の文字列。泥棒は `GremlinMerc` が player ごとに 1 インスタンス（`Instanced`）付け、`SurprisePower.AfterDeath` が各インスタンスの `Target` を強奪へ写す。強奪の `BeforeDeath` はこの player へ `Amount` のゴールドを報酬として返す。`stolen_gold` が 0 のインスタンスからは強奪を付与しない（`PowerCmd.Apply` が amount 0 を無視）ので、強奪の行は盗んだ額が正の対象だけに出る。 | どの player から盗んだかは所持金の減少で見える。 |
| 活力（`VIGOR_POWER`） `vigor_attack` | private `VigorPower.Data` の `commandToModify`（`AttackCommand?`）と `amountWhenAttackStarted`（int）（出典は v0.113.1 `sts2-a671e9ef`）。`BeforeAttack` は所有者の powered attack で、未結合かつ `ModelSource` が null（敵の `AttackCommand.FromMonster`）か札のときに、そのコマンドと開始時の `Amount` を記録する。`ModifyDamageAdditive` は結合先の `Attacker` が dealer と違う（札なら `ModelSource` が違う）攻撃には足さない。`AfterAttack` は同じコマンドのときだけ開始時の量を引き、参照は null に戻さない。新規の付与は `PowerCmd.Apply` が正準モデルから複製し、`DeepCloneFields` が `InitInternalData` で未結合の Data を作る。 | 結合は所有者の攻撃の開始で起き、攻撃の終わりの減少は画面の活力の数字と被ダメージに出る。攻撃で結合した活力は、`AfterAttack` が開始時の量を引いて 0 になれば行ごと無くなり（`ShouldRemoveDueToAmount`）、攻撃中に量が増えて残れば結合したまま残る。活力が無い状態で攻撃の後に付与された活力（テラーウツボの `ThrashMove` は攻撃の `Execute` の後に `PowerCmd.Apply<VigorPower>` を呼ぶ）は新しいインスタンスで、未結合の行になる。 |
| 呪術（`HEX_POWER`） `applier_combat_id` / `applier_readable` | `PowerModel.Applier`（`Creature?`）の `Creature.CombatId`（出典は v0.113.1 `sts2-a671e9ef`）。`PowerCmd.Apply` は新規のインスタンスにだけ `Applier` を設定し、既存のインスタンスへの再付与は、呪術の `InstanceType` が既定の `None` なので `FindExistingInstanceForStacking` が既存のインスタンスを返し、`ModifyAmount` に回って付与元を差し替えない。`SpectralKnight.HexMove` は自身の `Creature` を付与元に渡す。`HexPower.AfterDeath` は死んだ creature が `Applier` で、除去が防がれなければ呪術を外し、`HexPower.AfterRemoved` が所有者の全戦闘札から Hexed を消す。 | 呪術を付けた敵の行動（スペクトルナイトの HEX）は画面に出る。付与元の死亡と呪術の解除も画面で見える。 |
| 儀式（`RITUAL_POWER`） `was_just_applied_by_enemy` | private bool `RitualPower._wasJustAppliedByEnemy`（出典は v0.113.1 `sts2-a671e9ef`。schema 6、`cat12079801/sts2#1018`）。`AfterApplied` は所有者が敵で、敵側の手番（`CombatState.CurrentSide == Enemy`）に付与されたとき true にする。`AfterSideTurnEnd`（所有者が participants に含まれる）は true なら false に戻すだけで筋力を足さず、false なら `Amount` の筋力を足す。狂信者などの初回の儀式は付与した敵の手番の終わりに筋力を足さず、次の手番から足す。false も実値。 | 儀式を付ける敵の行動と、その手番の終わりに筋力が増えないことは画面に出る。 |
| 爆弾（`THE_BOMB_POWER`） `damage` | `TheBombPower.DynamicVars.Damage.BaseValue`（出典は v0.113.1 `sts2-a671e9ef`。schema 7、`cat12079801/sts2#1019`）。schema 7 の 4 種の数値変数は private `PowerModel._dynamicVars` をそのまま読み、null なら getter で作らずに読取失敗（null・理由・warnings）とする。`Instanced` で、札ごとの別インスタンス。`CanonicalVars` の `DamageVar(40)` から始まり、`SetDamage` が札の威力を書く。`Amount` は爆発までの残りターンで、`BeforeSideTurnEnd` は `Amount` が 1 以下のとき全敵にこの変数のダメージを与えて外れる。decimal のまま。 | 爆弾を置いた札とその威力は画面に出る（パワーの説明にも出る）。 |
| `INFERNO_POWER` / `CRIMSON_MANTLE_POWER` `self_damage` | `DynamicVars["SelfDamage"].BaseValue`（v0.113.1、schema 7）。`CanonicalVars` の 0 から始まり、`IncrementSelfDamage` が 1 ずつ上げる。`AfterPlayerTurnStart` は所有者の手番の初めにこの値（Unblockable・Unpowered）で所有者を傷つける（`CrimsonMantlePower` はその後 `Amount` のブロック）。 | 札を重ねて使った回数と、手番の初めの自傷は画面に出る。 |
| `PANACHE_POWER` `cards_left` / `already_applied` | `DynamicVars["CardsLeft"].IntValue` と private `PanachePower.Data.alreadyApplied`（v0.113.1、schema 7）。`Instanced`。`AfterCardPlayed` は所有者の札のプレイで、`alreadyApplied` が true なら `CardsLeft` を 1 減らし、0 以下で全敵に `Amount` のダメージを与えて 5 に戻す。その後 `alreadyApplied` を true にする（パワーを付けた札自身のプレイの通知は数えない）。`AfterSideTurnEnd` は `CardsLeft` を 5 に戻し、`alreadyApplied` は変えない。`DisplayAmount` は `CardsLeft`。Data は初期化しない。 | 残り枚数は画面のパワーの数字（`DisplayAmount`）で見える。付けた札のプレイ後かどうかは画面のプレイの出来事。 |
| 血の沸騰（`RUPTURE_POWER`） `played_cards` | private `RupturePower.Data.playedCards`（`Dictionary<CardModel, int>`）を `{uid, uid_known, card_id, pending_strength}` の行に（出典は v0.113.1 `sts2-a671e9ef`。schema 12、`cat12079801/sts2#1020`）。`BeforeCardPlayed` が自分の側の手番の所有者の札を 0 で記録し、`AfterDamageReceived` はその札が源の非ブロックダメージのたびに `Amount` を足す（記録に無い源ならその場で筋力）。`AfterCardPlayed` が札を外して合計の筋力を付ける。札の解決中だけ中身がある。 | 自傷の札を撃つと、解決後に筋力が増える。 |
| カラミティ（`CALAMITY_POWER`）・絞殺（`STRANGLE_POWER`） `amounts_for_played_cards` | private `Data.amountsForPlayedCards`（`Dictionary<CardModel, int>`）を `{uid, uid_known, card_id, amount}` の行に（v0.113.1、schema 12）。カラミティは `BeforeCardPlayed` が所有者のアタックを `Amount` で記録し、`AfterCardPlayed` が外してランダムなアタックを `Amount` 枚手札に足す。絞殺（`InstancedPerApplier`、敵側）は付与した player の札を `Amount` で記録し、`AfterCardPlayed` が外して記録の値を所有者に与える。札の解決中だけ中身がある。 | 札の解決後に札・ダメージが画面に出る。 |
| ヘル・レイザー（`HELLRAISER_POWER`） `infinite_auto_plays_this_turn` / `auto_playing_cards` | private `HellraiserPower.Data` の `infiniteAutoPlaysThisTurn`（int）と `autoPlayingCards`（`HashSet<CardModel>` を `{uid, uid_known, card_id}` の行に）（v0.113.1、schema 12）。`AfterCardDrawnEarly` は引いたストライクを自動プレイする。当たる敵がすべて無限の HP 表示なら数を 1 足し、9 に達していれば自動プレイしない。有限の敵がいる引きと `AfterSideTurnEnd` で 0。自動プレイ中の札が集合に入る。`showedCapReachedMessage` は ThinkCmd の表示の印なので出さない。 | ストライクの自動プレイは画面に出る。 |
| オートメーション（`AUTOMATION_POWER`） `cards_left` | private `AutomationPower.Data.cardsLeft`（int、v0.113.1、schema 12）。`Instanced`。`AfterCardDrawn` が所有者の引きごとに 1 減らし、0 で `Amount` エナジーを得て 10 に戻す。`DisplayAmount` はこの値。`dynamic_vars.BaseCards`（10）は本文の定数。 | パワーの数字として画面に出る。 |
| 闇の抱擁（`DARK_EMBRACE_POWER`） `ethereal_count` | private `DarkEmbracePower.Data.etherealCount`（int、v0.113.1、schema 12）。`AfterCardExhausted` が所有者のエセリアルによる廃棄を数え（他の廃棄はその場で `Amount` 枚引く）、所有者の `AfterSideTurnEnd` で `Amount` × 数を引いて 0。 | 手番の終わりのエセリアルの廃棄は画面に出る。 |
| 巨大化（`GIGANTIFICATION_POWER`） `has_command_to_modify` | private `GigantificationPower.Data.commandToModify`（`AttackCommand?`）が結合済みか（v0.113.1、schema 12）。`BeforeAttack` が所有者の powered なアタック札の最初の攻撃を結合し、`ModifyDamageMultiplicative` は結合中はその札だけ（未結合ならどれでも）3 倍、`AfterAttack` が外して 1 減らす。読めた false は未結合。 | 3 倍の攻撃は画面に出る。 |
| 硬い殻（`HARDENED_SHELL_POWER`） `damage_received_this_turn` | private `HardenedShellPower.Data.damageReceivedThisTurn`（decimal、v0.113.1、schema 12）。`ModifyHpLostBeforeOstyLate` は HP 損失を `Amount` − この値で止め、`AfterDamageReceived` が非ブロックダメージを足し、どちらの側でも `BeforeSideTurnStart` で 0。`DisplayAmount` は残り。 | 受けたダメージは画面に出る。 |
| 適応（`ADAPTABLE_POWER`）・再結合（`REATTACH_POWER`）・幻影（`ILLUSION_POWER`） `is_reviving` | private `Data.isReviving`（bool、v0.113.1、schema 12）。所有者が倒れて復活を待つ間 true。この間は `ShouldAllowHitting` が false（`battle.enemies[].hittable`）。 | 倒れた敵が復活を待つ様子は画面に出る。 |
| `ILLUSION_POWER` `follow_up_state_id` / `follow_up_state_readable` | 公開 `IllusionPower.FollowUpStateId`（private `_followUpStateId` を返すだけ、v0.113.1、schema 12）。読めた null は「記録なし」（readable=true）で、読取失敗（readable=false・null・理由）と区別する。**v0.113.1 ではこのプロパティに代入する箇所が無く、常に null**。`IllusionPower.AfterDeath` は復活の行動（`REVIVE_MOVE`）の `MoveState.FollowUpStateId` を `FollowUpStateId ?? StateLog の最後の ID` にするので、実際の行き先は死亡時の `StateLog` の最後で、`battle.enemies[].move_state` の復活の行動の follow-up から読む。この項目を行き先として使わない。 | 復活後の行動は画面に出る。 |
| ネメシス（`NEMESIS_POWER`） `should_apply_intangible` | private `NemesisPower._shouldApplyIntangible`（bool、v0.113.1、schema 12）。所有者が参加する `AfterSideTurnEnd` で反転し、true になったら霊体（`IntangiblePower`）1 を付け、false になったら外す。 | 霊体の付け外しは毎手番画面に出る。 |
| 怠惰（`SLOTH_POWER`）・軟化（`TENDER_POWER`） `cards_played_this_turn` | private `_cardsPlayedThisTurn`（int、v0.113.1、schema 12）。怠惰は `ShouldPlay` が所有者の札をこの値が `Amount` に達したら止め、`BeforeCardPlayed` で 1 足し、`BeforeSideTurnStart` で 0。`TenderPower` は `AfterCardPlayed` で 1 足して筋力・敏捷を −1、`AfterSideTurnEnd` で同じ数だけ戻して 0。どちらも `DisplayAmount` はこの値。 | パワーの数字として画面に出る。 |
| 希望の道標（`BEACON_OF_HOPE_POWER`） `has_already_been_given_block` | private `BeaconOfHopePower._hasAlreadyBeenGivenBlock`（bool、v0.113.1、schema 12）。`AfterBlockGained` が仲間にブロックの半分を渡す間だけ true（相互の無限の受け渡しを止める）。行動を選ぶ時点では false。 | — |
| 盗む（`SWIPE_POWER`） `stolen_card` / `stolen_card_readable` | private `SwipePower._stolenCard`（`CardModel?`）を `{uid, uid_known, card_id}` に（v0.113.1、schema 12）。`Steal` が盗んだ札を記録してデッキ版を外し、`BeforeDeath` がデッキ版を報酬として返す。読めた null は「盗んでいない」。 | 盗まれた札は画面に出る。 |
| 弱化（`DAMPEN_POWER`） `caster_combat_ids` | private `DampenPower.Data.casters`（`HashSet<Creature>`）の各 `Creature.CombatId` を昇順に（出典は v0.113.1 `sts2-a671e9ef`。schema 8、`cat12079801/sts2#1031`）。`MagiKnight.DampenMove` は対象に弱化があれば `AddCaster` だけ、無ければ新しいインスタンスに `AddCaster` してから `PowerCmd.Apply`。`AfterDeath` は死んだ caster を除去が防がれなければ外し、空になれば弱化を外す。`CombatId` の無い caster が居れば読取失敗（ID を補わない）。 | 弱化を使った敵の行動（マギナイトの DAMPEN）と、その敵の死亡は画面に出る。 |
| 弱化 `downgraded_cards` | private `DampenPower.Data.downgradedCardsToOldUpgradeLevels`（`Dictionary<CardModel, int>`）を `{uid, uid_known, card_id, original_upgrade_level}` の行に（v0.113.1、schema 8）。`AfterApplied` が所有者の `PlayerCombatState.AllCards` の強化済みの札ごとに `CurrentUpgradeLevel` を記録して `CardCmd.Downgrade` し、`AfterRemoved` が記録の段階の回数だけ `CardCmd.Upgrade` する（`CombatManager.IsEnding` の間は `Downgrade`・`Upgrade` とも何もしない）。uid は registry が既に振った値だけを使い（`TryGetRegisteredCardUid`）、無ければ新たに採番せず `uid=null`・`uid_known=false`・`uid_unavailable_reason`・warnings。行は uid 順（辞書の挿入順は山札の順を含むので出さない）。 | 付与の瞬間に強化済みの札が `+` を失うのは画面（札・パイル）で見える。元の段階は付与前の札の表示。 |
| まるくなる（`CURL_UP_POWER`） `played_card_uid` / `played_card_readable` | private `CurlUpPower.Data.playedCard` を同じ uid registry に通す（出典は v0.113.1 `sts2-a671e9ef`。v0.113.0 `sts2-d009478a` と同一）。`AfterDamageReceived` は所有者への `IsPoweredAttack` かつ `cardSource` ありの被弾で、未記録または同じ札なら記録（別の札が記録済みなら無視）。`AfterCardPlayed` は `cardPlay.Card` が記録の札と同じときに参照を消し、Amount のブロックを得てパワーを外す。 | 画面に出た、その敵へのアタック札の命中から分かる追跡状態。uid はその公開された札の個体を照合する。 |

盗難の配列の各行は `{player_id, amount}`。`player_id` は `player.player_id` と同じ文字列。
マルチでも受取人を失わないよう合算しない。辞書が空なら空配列を出す。
将来の盗難量や敵の行動予定は出さない。

泥棒・強奪は対象 player ごとの別インスタンスで、`status[]` にも別々の行として出る。
同じ対象の行も合算しない。マルチでは他の player が対象の行は `PowerModel.IsVisible` が false
（`hidden_in_ui`）になるが、スタックが残る行は出るので、`stolen_gold` / `target_player_id` は
可視性によらず付ける（`skip_next_duration_tick` などの他の項目は従来どおり付けない）。
強奪の `amount` は既存のまま（返すゴールドの量）。`Target` が無い・player でない場合は、
自分の ID や 0 で補わず、null と理由・warnings にする。0 の `stolen_gold` は実値。

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

活力（`VIGOR_POWER`）の `vigor_attack` は player・敵の活力のどちらの行にも、`hidden_in_ui`・可視性不明・
説明取得失敗の最小行にも付ける 4 項目のオブジェクト。

| 項目 | 型 | 意味 |
|---|---|---|
| `readable` | bool | 下の 3 項目のうち必要なものをすべて読めたら true |
| `has_command` | bool / null | `Data.commandToModify` が非 null か。読めなければ null |
| `attacker_is_owner` | bool / null | 結合先の `AttackCommand.Attacker == PowerModel.Owner`（`Creature` は等値演算子を持たないので参照の比較で、`ModifyDamageAdditive` と同じ）。未結合（`has_command=false`）と読取失敗は null |
| `amount_when_started` | int / null | `Data.amountWhenAttackStarted` の実値。未結合でもフィールドの値をそのまま出す（新規の付与なら 0）。読めなければ null |

未結合は `readable=true, has_command=false, attacker_is_owner=null` で、理由・警告を付けない。
読取失敗は `readable=false` とし、失敗した項目を null、行の `unavailable_reasons` に
`vigor_attack.<項目>`、`warnings` に `status.VIGOR_POWER.vigor_attack.<項目>` を付ける。
Data が読めないときは 3 項目とも null と理由。null を未結合や 0 に読み替えない。
コマンドの参照・アドレス・ID は出さない。履歴・意図のラベル・解決値との差からは作らない。
player の札の攻撃では、結合先の `ModelSource` と札の一致も加算の条件になる。この一致は出さないので、結合済み（`has_command=true`）の player の活力では、札の攻撃に足すかどうかは決まらない。敵の攻撃（`cardSource` が null）には `attacker_is_owner` で足りる。
Markdown は `(vigor attack: has command: no, attacker is owner: none, amount when started: 0)` の形で、
未結合の `attacker_is_owner` を `none`、読取失敗を `readable: no` と `null (理由)` で表示する。
`readable=true` は Markdown で省略する。

読取は `PowerModel._internalData` のリフレクション（`ReadPowerData`。`Data` の型を確かめる）と、
`Data` の 2 つのフィールドの `FieldInfo.GetValue`、自動プロパティ `AttackCommand.Attacker`、
`PowerModel.Owner`（`AssertMutable` のあと `_owner` を返すだけ）の getter に限る。
`GetInternalData` / `InitInternalData`・`BeforeAttack` / `AfterAttack` / `ModifyDamageAdditive` は呼ばず、Data も書き換えない。

呪術（`HEX_POWER`）の付与元は player の呪術の行に、`hidden_in_ui`・可視性不明・説明取得失敗の最小行にも付ける
（`HexPower.AfterDeath` は `IsVisible` によらず `Applier` を見る）。

| 項目 | 型 | 意味 |
|---|---|---|
| `applier_readable` | bool | `Applier` の参照と、参照があればその `CombatId` を読めたら true |
| `applier_combat_id` | uint / null | 付与元の `Creature.CombatId`。`enemies[].combat_id` と同じ識別子で、0 も実在する ID。付与元が無い（読めた null）ときと読取失敗は null |

付与元なしは `applier_readable=true, applier_combat_id=null` で、理由・警告を付けない。
`Applier` の getter の失敗と、参照があるのに `CombatId` が null のときは読取失敗とし、
`applier_readable=false`、`applier_combat_id=null`、行の `unavailable_reasons.applier_combat_id`、
`warnings` の `status.HEX_POWER.applier_combat_id` を付ける。ID を補わない。
付与元が死んでも参照は残る（呪術はふつう同時に外れて行ごと無くなる。除去が防がれて残った場合は、死んだ付与元の ID のまま）。
付与元が生きているか・敵の一覧に居るかは、この ID を `enemies[]` の `combat_id` と照合して判断する。
読取は `PowerModel.Applier` の getter（`_applier` を返すだけ。`AssertMutable` は setter のみで、getter の失敗の経路は防御）と
`Creature.CombatId`（自動プロパティ）に限る。フックの呼び出しや書き換えはしない。
Markdown は `(applier combat id: 3)`、付与元なしは `none`、読取失敗は `null (理由); applier readable: no`。
`applier_readable=true` は Markdown で省略する。

Markdown は情報がある場合だけ既存 status 行に追記する。skip=false と空配列は省略し、
対象別の 0 / false は表示、失敗時は null と理由を表示する。
解決中の値はフック間の過渡状態なので、計画には解決後の snapshot を使う。
ジャグリングの予約参照とまるくなるの追跡は、解決後の snapshot では通常 `null`。
ジャグリングで `null` でない値が見えるのはその札の解決中（プレイ途中の選択を含む）だけ。

弱化（`DAMPEN_POWER`）の 2 項目は player の弱化の行に、`hidden_in_ui`・可視性不明・説明取得失敗の最小行にも付ける
（`AfterDeath` / `AfterRemoved` は `IsVisible` を見ない）。空の配列は読めた空で、理由・警告を付けない。
Data が無い・型違い・フィールドの欠落・`CombatId` の無い caster は、項目を null・`unavailable_reasons.<key>`・
`warnings` の `status.DAMPEN_POWER.<key>` にする。uid の無い札は項目の失敗ではなく、その行だけ `uid_known=false`。warnings の `status.DAMPEN_POWER.downgraded_cards.uid` は uid の無い行がある印で、項目の読取失敗ではない（`downgraded_cards` は読めている）。どの行かは各行の `uid_known` / `uid_unavailable_reason` で判断し、その札が registry に載らない間は state を読むたびに出続ける。
読取は `ReadPowerData`（`_internalData` と nested `Data` の型の確認）と 2 つのフィールドの `FieldInfo.GetValue`、
`Creature.CombatId`・`CardModel.Id` の getter に限り、`AddCaster`・`GetInternalData`・`InitInternalData`・フックは呼ばない。
uid registry にも書かない（`TryGetRegisteredCardUid` は既存の uid を引くだけ）。
Markdown は `(caster combat ids: 3; downgraded cards: STRIKE_R#0 from +1)`、空は `none`、uid の無い札は
`CARD_ID (uid unknown) from +1`、読取失敗は `null (理由)`。

`dynamic_vars`（schema 12）は全パワーの行に、`hidden_in_ui`・可視性不明・説明取得失敗の最小行にも付ける
（パワーの効果は `IsVisible` を見ずに変数を使う）。`status[]` が行ごと落とす量 0 の非表示の行には付かない。
読取は `_dynamicVars` の `FieldInfo.GetValue` と、集合の列挙・各 `DynamicVar.BaseValue` の getter に限る。
`DynamicVars` の getter（null なら作る）・`InitializeWithOwner`・`Clone`・フックは呼ばない。
クライアントは説明文の数値を読まず、この値と ID でパワーの量を決める。変数を持たないパワーは `{}` で、
量は `amount` だけ。パワーの量が `_internalData`（private `Data`）にあるものは上の型ごとの項目で出し、`dynamic_vars` には入らない。
Markdown は変数がある行だけ `(vars: SelfDamage=2, Decrement=1)` の形で追記し、空の `{}` は省略する。
集合の読取失敗は `vars: null (理由)`、変数ごとの失敗は `名前=null (理由)`。

schema 12 の private Data・フィールドの項目（上表）は、`status[]` に出る行なら可視性によらず付ける（効果は `IsVisible` を見ない）。
Data は `ReadPowerData`（`_internalData` と nested `Data` の型の確認）で読み、`GetInternalData`・`InitInternalData`・フックは呼ばない。
Data が null・型違い・フィールドの欠落は、その項目を null・`unavailable_reasons.<key>`・`warnings` の `status.<POWER_ID>.<key>` にする。
札の参照（血の沸騰・カラミティ・絞殺の記録、ヘル・レイザーの自動プレイ中の札、盗まれた札）は弱化と同じく既存の uid registry を引くだけで、
registry に無い札は行ごとに `uid=null`・`uid_known=false`・`uid_unavailable_reason`・`warnings` の `status.<POWER_ID>.<key>.uid` にし、uid を新しく振らない。
行は uid 順（無いものは後ろに card_id 順）。
敵側のパワー（絞殺の `amounts_for_played_cards`、盗むの `stolen_card`）は `battle` の中で組むので、自分の手札・パイルの行（registry に登録する側）より先に読まれる。
戦闘の最初の state（registry が空）では、まだ登録されていない札が `uid_known=false` になり得る。ふつうは前の state で手札・山札として登録済み。
uid が要るなら次の state を読み直す（registry の順序は採番の規則に関わるので、組む順は変えていない）。Markdown は `played cards: STRIKE_R#0=3`、空は `none`、盗まれた札は `stolen card: BASH#0`（なしは `none`）。

実機の起動・照合は行っていない。契約試験はゲーム境界のダブルに対して
本番の reader・formatter・warnings を直接実行する。
