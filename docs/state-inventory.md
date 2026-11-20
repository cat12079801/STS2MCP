# ゲームの内部状態の棚卸し（state inventory）

> この文書は `tools/state_inventory.py` が生成する。手で書き換えない。分類は `docs/state-inventory-classification.json` に書き、生成し直す。

- ゲームの版: **v0.113.1**（デコンパイル `sts2-a671e9ef`）
- 全候補の機械向けの表: [`state-inventory-members.tsv`](state-inventory-members.tsv)
- 生成: `bin/sts2-python -B tools/state_inventory.py --decompile <dir> --mod . --game-version <版>`
- 許可リスト: 分類表で `section` を付けた public の項目から `--write-allowlist` が [`../McpMod.StateAllowlist.Generated.cs`](../McpMod.StateAllowlist.Generated.cs) を作り、state の各行の `internal_state` に出す。`--check-allowlist` が分類表との一致を、棚卸しの実行が今の版の宣言との一致を確かめる

## 分類の意味

| 分類 | 意味 |
|---|---|
| exposed | すでに MOD の state に出ている（MOD のソースで読み取りを検出、分類表で確認、または分類表の section から生成した許可リスト `internal_state` で出す〔判定 allowlist〕） |
| partial | 基底クラスのメンバーを、MOD が派生の分岐（case・is・その分岐からしか呼ばれない補助）の中だけで読んでいる。ほかの派生の行には出ていない。許可リストで全派生に出すか、分類表の `partial_reason` で解消する（判定 partial-resolved）。解消していないと棚卸しは終了コード 4 |
| public | 人間が見える・見たものから確定できる。まだ出ていない（出す対象） |
| hidden | 人間に見えない（乱数・未公開の山の順・まだ見せていない生成物）。出さない |
| cosmetic | 戦闘・進行に効かない（台詞・演出・UI）。出す必要は無い |
| static/derived | 定数、または出ている値・アセンション・強化から決まる。出す必要は無い |
| out_of_scope | 標準のモード以外（Blitz・Dread・カスタム）だけのもの。対象外 |
| unknown | ソースからは決めきれない（理由を備考に書く） |
| unclassified | 分類表に無い（版が上がって増えた候補など） |

種別（kind）: `field` = インスタンスのフィールド、`auto_property` = 自動実装プロパティ（setter あり）、`getonly_auto_property` = get だけの自動実装プロパティ、`data_field` = パワーの入れ子 `Data` の欄、`dynamic_var_mutation` = 実行時に `DynamicVars.<名前>.BaseValue` を書き換える箇所（強化 `OnUpgrade` を除く）。

## 件数（種別 × 分類）

| 種別 | exposed | partial | public | hidden | cosmetic | static/derived | out_of_scope | unknown | unclassified | 計 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| monster | 25 | 0 | 0 | 2 | 38 | 22 | 0 | 0 | 0 | 87 |
| relic | 95 | 0 | 3 | 0 | 29 | 15 | 0 | 0 | 0 | 142 |
| power | 57 | 0 | 25 | 0 | 9 | 5 | 0 | 0 | 0 | 96 |
| card | 44 | 0 | 10 | 0 | 7 | 17 | 0 | 0 | 0 | 78 |
| potion | 1 | 0 | 0 | 0 | 1 | 5 | 0 | 0 | 0 | 7 |
| enchantment | 5 | 0 | 0 | 0 | 1 | 4 | 0 | 0 | 0 | 10 |
| affliction | 1 | 0 | 0 | 0 | 0 | 2 | 0 | 0 | 0 | 3 |
| orb | 1 | 0 | 2 | 0 | 0 | 2 | 0 | 0 | 0 | 5 |
| base | 1 | 0 | 0 | 0 | 0 | 3 | 0 | 0 | 0 | 4 |
| entity | 50 | 0 | 1 | 3 | 8 | 16 | 4 | 0 | 0 | 82 |
| **計** | 280 | 0 | 41 | 5 | 93 | 91 | 4 | 0 | 0 | 514 |

### アイアンクラッドの範囲だけ（他キャラ・廃止・モックを除く）

| 種別 | exposed | partial | public | hidden | cosmetic | static/derived | out_of_scope | unknown | unclassified | 計 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| monster | 25 | 0 | 0 | 2 | 38 | 22 | 0 | 0 | 0 | 87 |
| relic | 93 | 0 | 0 | 0 | 27 | 15 | 0 | 0 | 0 | 135 |
| power | 51 | 0 | 0 | 0 | 5 | 3 | 0 | 0 | 0 | 59 |
| card | 36 | 0 | 0 | 0 | 5 | 14 | 0 | 0 | 0 | 55 |
| potion | 1 | 0 | 0 | 0 | 0 | 5 | 0 | 0 | 0 | 6 |
| enchantment | 5 | 0 | 0 | 0 | 1 | 4 | 0 | 0 | 0 | 10 |
| affliction | 1 | 0 | 0 | 0 | 0 | 2 | 0 | 0 | 0 | 3 |
| orb | 1 | 0 | 0 | 0 | 0 | 2 | 0 | 0 | 0 | 3 |
| base | 1 | 0 | 0 | 0 | 0 | 3 | 0 | 0 | 0 | 4 |
| entity | 50 | 0 | 0 | 3 | 8 | 16 | 4 | 0 | 0 | 81 |
| **計** | 264 | 0 | 0 | 5 | 84 | 86 | 4 | 0 | 0 | 443 |

範囲（pool）: `ironclad`・`colorless`・`curse`・`status`・`event-only`・`shared`・`ancient`・`monster`・`enchantment`・`affliction`・`base`（モデルの基底）・`entity`（Creature・Player など）を範囲に入れる。`other-character`・`deprecated`・`mock`・`unreferenced` は範囲外（一覧には残す）。パワー・トークンのカード・プール外のレリックは、参照するクラスのプールから決める（アイアンクラッドの札・共有のレリックが付けるならその範囲）。

## public（まだ出ていない・出す対象）— アイアンクラッドの範囲（0 件）

書き込み・読む側の列は、分類表に記述があればそれを、無ければ同じファイルの中で検出したメソッド名（と別クラスからの参照）を出す。

（なし）

## public — 範囲外（他キャラ・廃止）（41 件）

| クラス.メンバー | 型 | kind | 範囲 | 書き込み | 効果（読む側） | 提案キー | 備考 |
|---|---|---|---|---|---|---|---|
| `CardModel._lastStarsSpent` | int | field | other-character | SpendStars | ResolveStarXValue | `last_stars_spent` | 直前に払った星（SpendStars）。星を X に使うカードの値（ResolveStarXValue）。星はリージェント専用 |
| `Claw._extraDamageFromClawPlays` | decimal | field | other-character | BuffFromClawPlay | AfterDowngraded | `extra_damage` | BuffFromClawPlay で DynamicVars.Damage と同じだけ足す累計。AfterDowngraded（DampenPower などの強化の取り消し）で基礎値に足し戻す。今の Damage は dynamic_vars に出ているが、取り消し後の値はこれが無いと決まらない |
| `GeneticAlgorithm._increasedBlock` | int | field · saved | other-character | BuffFromPlay | Update* | `increased_amount` | プレイのたびに増える累計（保存される）。Update* が基礎値に足して今の値を作る |
| `KinglyPunch._extraDamage` | decimal | field | other-character | AfterCardDrawn | AfterDowngraded | `extra_damage` | AfterCardDrawn で DynamicVars.Damage と同じだけ足す累計。AfterDowngraded（DampenPower などの強化の取り消し）で基礎値に足し戻す。今の Damage は dynamic_vars に出ているが、取り消し後の値はこれが無いと決まらない |
| `ShowOff._increasedAim` | int | field · saved | other-character | IncreaseAim | Update* | `increased_amount` | プレイのたびに増える累計（保存される）。Update* が基礎値に足して今の値を作る |
| `SovereignBlade._createdThroughForge` | bool | field | other-character | AfterCloned | AfterCardChangedPiles | `created_through_forge` | 鍛造（ForgeCmd）で作られたか。AfterCardChangedPiles が読む |
| `SovereignBlade._currentDamage` | decimal | field | other-character | AddDamage | AfterDowngraded | `current_damage` | AddDamage の累計。AfterDowngraded で戻す |
| `SovereignBlade._currentRepeats` | decimal | field | other-character | SetRepeats | AfterDowngraded | `current_repeats` | SetRepeats の値。AfterDowngraded で戻す |
| `TheScythe._increasedDamage` | int | field · saved | other-character | BuffFromPlay | Update* | `increased_amount` | プレイのたびに増える累計（保存される）。Update* が基礎値に足して今の値を作る |
| `TheShot._cardsExhaustedByFlorescence` | decimal | field | other-character | BuffFromFlorescence | CanonicalVars（CalculatedDamageVar.WithMultiplier） | `cards_exhausted_by_florescence` | BuffFromFlorescence の累計。CalculatedDamageVar の倍率で、今のダメージはカードの表示値に出るが、倍率そのものは出ていない |
| `PlayerCombatState._arrows` | List<ArrowSlot> | field | other-character | GainArrow, PlayerCombatState | Arrows・AvailableArrowCount | `arrows` | 矢の枠（GainArrow。フール専用の仕組み） |
| `DarkOrb._evokeVal` | decimal | field | other-character | Passive | EvokeVal | `evoke_value` | Passive のたびに増える発動値 |
| `GlassOrb._passiveVal` | decimal | field | other-character | Passive | Passive・PassiveVal | `passive_value` | Passive のたびに減る値 |
| `AfterimagePower.Data.amountsForPlayedCards` | Dictionary<CardModel, int> | data_field | other-character | AfterCardPlayed, BeforeCardPlayed | AfterCardPlayed | `amounts_for_played_cards` | カードのプレイ・攻撃の解決の途中だけ意味を持つ値（書き込み・読む側のメソッドは表の列）。解決の途中で選択画面が開いたときに効くので出す。BeforeCardPlayed でプレイ中のカードと量を記録し、AfterCardPlayed で使う（Calamity・Strangle と同じ形で、そちらは出ている） |
| `AimPower.Data.amountWhenAttackStarted` | int | data_field | other-character | BeforeAttack | AfterAttack | `amount_when_started` | カードのプレイ・攻撃の解決の途中だけ意味を持つ値（書き込み・読む側のメソッドは表の列）。解決の途中で選択画面が開いたときに効くので出す。攻撃の開始時の量（Vigor と同じ形）。AimPower を付けるのは ShowOff（他キャラのカード）と PrecisionPotion（どのポーションのプールにも無い）。範囲は other-character に上書きする。 |
| `AimPower.Data.commandToModify` | AttackCommand? | data_field | other-character | BeforeAttack | ModifyDamageAdditive・AfterAttack | `has_command_to_modify` | カードのプレイ・攻撃の解決の途中だけ意味を持つ値（書き込み・読む側のメソッドは表の列）。解決の途中で選択画面が開いたときに効くので出す。BeforeAttack で修正する攻撃を記録する（Vigor・Gigantification と同じ形で、そちらは出ている）。AimPower を付けるのは ShowOff（他キャラのカード）と PrecisionPotion（どのポーションのプールにも無い）。範囲は other-character に上書きする。 |
| `EncorePower.Data.cardsBeingPlayed` | List<CardModel> | data_field | other-character | AfterCardPlayed, BeforeCardPlayed | ShouldPlay | `cards_being_played` | カードのプレイ・攻撃の解決の途中だけ意味を持つ値（書き込み・読む側のメソッドは表の列）。解決の途中で選択画面が開いたときに効くので出す。 |
| `EncorePower.Data.numberOfCardsPlayedSinceApplied` | int | data_field | other-character | AfterCardPlayed | PlaysRemaining・ShouldPlay | `cards_played_since_applied` | 付いてからプレイしたカードの数。PlaysRemaining・ShouldPlay が読む |
| `FeralPower.Data.zeroCostAttacksPlayed` | int | data_field | other-character | SetZeroCostAttacksPlayed | ModifyCardPlayResultLocation | `zero_cost_attacks_played` | 0 コストのアタックをプレイした回数（表示の DisplayAmount にも使う） |
| `GravityPower.Data.amountsForPlayedCards` | Dictionary<CardModel, int> | data_field | unreferenced | AfterCardPlayed, BeforeCardPlayed | AfterCardPlayed | `amounts_for_played_cards` | カードのプレイ・攻撃の解決の途中だけ意味を持つ値（書き込み・読む側のメソッドは表の列）。解決の途中で選択画面が開いたときに効くので出す。BeforeCardPlayed でプレイ中のカードと量を記録し、AfterCardPlayed で使う（Calamity・Strangle と同じ形で、そちらは出ている） |
| `ImitationLearningPower._playerTarget` | Player? | field | other-character | PlayerTarget | BeforeCardPlayed | `player_target_id` | 対象のプレイヤー（マルチプレイ） |
| `ImpaledPower.Data.arrowSources` | List<ArrowSlot> | data_field | other-character | AddArrow, ForceRemoveArrow, RemoveFirstArrow | Arrows・RemoveFirstArrow | `arrow_sources` | 刺さった矢の出どころ（AddArrow・RemoveFirstArrow）。ImpaledPower を付けるのはフールのカード（Nail・BowSmack など）で、ToughEgg.cs:161 は孵化時に除外するだけなので、範囲は other-character に上書きする |
| `LullabyPower._proccdThisTurn` | bool | field | other-character | AfterApplied, AfterCardPlayed, BeforeSideTurnStart | AfterCardPlayed | `procced_this_turn` | このターンに発動したか |
| `MonologuePower.Data.amountsForPlayedCards` | Dictionary<CardModel, int> | data_field | other-character | AfterCardPlayed, BeforeCardPlayed | AfterCardPlayed | `amounts_for_played_cards` | カードのプレイ・攻撃の解決の途中だけ意味を持つ値（書き込み・読む側のメソッドは表の列）。解決の途中で選択画面が開いたときに効くので出す。BeforeCardPlayed でプレイ中のカードと量を記録し、AfterCardPlayed で使う（Calamity・Strangle と同じ形で、そちらは出ている） |
| `NightmarePower.Data.selectedCard` | CardModel? | data_field | other-character | SetSelectedCard | BeforeHandDraw | `selected_card_uid` | 選んだカード（SetSelectedCard）。BeforeHandDraw で複製する。選んだのは人間 |
| `OblivionPower.Data.amountsForPlayedCards` | Dictionary<CardModel, int> | data_field | other-character | AfterCardPlayed, BeforeCardPlayed | AfterCardPlayed | `amounts_for_played_cards` | カードのプレイ・攻撃の解決の途中だけ意味を持つ値（書き込み・読む側のメソッドは表の列）。解決の途中で選択画面が開いたときに効くので出す。BeforeCardPlayed でプレイ中のカードと量を記録し、AfterCardPlayed で使う（Calamity・Strangle と同じ形で、そちらは出ている） |
| `OrbitPower.Data.energySpent` | int | data_field | other-character | AfterEnergySpent | AfterEnergySpent | `energy_spent` | 使ったエネルギーの累計（表示にも使う） |
| `OrbitPower.Data.triggerCount` | int | data_field | other-character | AfterEnergySpent | AfterEnergySpent | `trigger_count` | 発動した回数 |
| `PaleBlueDotPower.Data.alreadyActivatedThisTurn` | bool | data_field | other-character | AfterCardPlayed, AfterSideTurnEnd | AfterCardPlayed | `already_activated_this_turn` | このターンに発動したか |
| `QuietusPower.Data.commandToModify` | AttackCommand? | data_field | other-character | BeforeAttack | ModifyDamageMultiplicative・AfterAttack | `has_command_to_modify` | カードのプレイ・攻撃の解決の途中だけ意味を持つ値（書き込み・読む側のメソッドは表の列）。解決の途中で選択画面が開いたときに効くので出す。BeforeAttack で修正する攻撃を記録する（Vigor・Gigantification と同じ形で、そちらは出ている） |
| `SerpentFormPower.Data.amountsForPlayedCards` | Dictionary<CardModel, int> | data_field | other-character | AfterCardPlayed, BeforeCardPlayed | AfterCardPlayed | `amounts_for_played_cards` | カードのプレイ・攻撃の解決の途中だけ意味を持つ値（書き込み・読む側のメソッドは表の列）。解決の途中で選択画面が開いたときに効くので出す。BeforeCardPlayed でプレイ中のカードと量を記録し、AfterCardPlayed で使う（Calamity・Strangle と同じ形で、そちらは出ている） |
| `StormPower.Data.amountsForPlayedCards` | Dictionary<CardModel, int> | data_field | other-character | AfterCardPlayed, BeforeCardPlayed | AfterCardPlayed | `amounts_for_played_cards` | カードのプレイ・攻撃の解決の途中だけ意味を持つ値（書き込み・読む側のメソッドは表の列）。解決の途中で選択画面が開いたときに効くので出す。BeforeCardPlayed でプレイ中のカードと量を記録し、AfterCardPlayed で使う（Calamity・Strangle と同じ形で、そちらは出ている） |
| `StretchPower.Data.amountWhenCardStarted` | int | data_field | other-character | BeforeCardPlayed | AfterCardPlayed | `amount_when_card_started` | カードのプレイ・攻撃の解決の途中だけ意味を持つ値（書き込み・読む側のメソッドは表の列）。解決の途中で選択画面が開いたときに効くので出す。 |
| `StretchPower.Data.cardPlay` | CardPlay? | data_field | other-character | AfterCardPlayed, BeforeCardPlayed | ModifyBlockAdditive・AfterCardPlayed | `card_play_in_progress` | カードのプレイ・攻撃の解決の途中だけ意味を持つ値（書き込み・読む側のメソッドは表の列）。解決の途中で選択画面が開いたときに効くので出す。 |
| `StretchPower.Data.gaveBonusBlock` | bool | data_field | other-character | AfterCardPlayed, ModifyBlockAdditive | AfterCardPlayed | `gave_bonus_block` | カードのプレイ・攻撃の解決の途中だけ意味を持つ値（書き込み・読む側のメソッドは表の列）。解決の途中で選択画面が開いたときに効くので出す。 |
| `SubroutinePower.Data.amountsForPlayedCards` | Dictionary<CardModel, int> | data_field | other-character | AfterCardPlayed, BeforeCardPlayed | AfterCardPlayed | `amounts_for_played_cards` | カードのプレイ・攻撃の解決の途中だけ意味を持つ値（書き込み・読む側のメソッドは表の列）。解決の途中で選択画面が開いたときに効くので出す。BeforeCardPlayed でプレイ中のカードと量を記録し、AfterCardPlayed で使う（Calamity・Strangle と同じ形で、そちらは出ている） |
| `VoidFormPower.Data.cardsPlayedThisTurn` | int | data_field | other-character | AfterCardPlayed, BeforeSideTurnStart, HideTemporaryZeroCostVisual | AfterCardPlayed・ShouldSkip | `cards_played_this_turn` | このターンにプレイしたカードの数 |
| `YahPower.Data.triggeredThisTurn` | bool | data_field | other-character | AfterArrowDislodged, BeforeSideTurnStart | AfterArrowDislodged | `triggered_this_turn` | このターンに発動したか |
| `ConductorsBaton._triggeredThisTurn` | bool | field | other-character | AfterCardPlayed, BeforeSideTurnStart | AfterCardPlayed | `triggered_this_turn` | このターンに発動したか |
| `GalacticDust._starsSpent` | int | field · saved | other-character | AfterStarsSpent | AfterStarsSpent | `stars_spent` | 使った星の累計（DisplayAmount にも使う。星はリージェント専用） |
| `Metronome._orbsChanneled` | int | field | other-character | AfterCombatEnd, AfterOrbChanneled, AfterRoomEntered | AfterOrbChanneled | `orbs_channeled` | チャネルしたオーブの数（ディフェクト専用） |

## partial（未解消。棚卸しは終了コード 4）（0 件）

（なし）

## unknown（ソースから決めきれない）（0 件）

（なし）

## unclassified（分類表に無い）（0 件）

（なし）

## hidden（出さない）（5 件）

| クラス.メンバー | 型 | 範囲 | 理由 |
|---|---|---|---|
| `Player.PlayerOdds` | PlayerOddsSet | entity | カードのレア度・ポーションの出現率のオフセット（CardRarityOdds・PotionRewardOdds）。見た報酬の履歴から決まるが、画面には出ないので出さない（人間に見えない情報で遊ばない。調整役の判断 2026-11-19） |
| `Player.PlayerRng` | PlayerRngSet | entity | 型が乱数（Rng 系） |
| `Player.RelicGrabBag` | RelicGrabBag | entity | これから出るレリックの袋（順番を含む）。まだ見せていない生成物 |
| `MonsterModel._rng` | Rng? | base | 型が乱数（Rng 系） |
| `MonsterModel._runRng` | RunRngSet? | base | 型が乱数（Rng 系） |

## out_of_scope（対象外）（4 件）

| クラス.メンバー | 型 | 範囲 | 理由 |
|---|---|---|---|
| `CombatState.BadgeModels` | IReadOnlyList<BadgeModel> | entity | ゲームモード（Blitz・Dread・カスタムの修飾子・バッジ）のモデルの一覧。IterateHookListeners でフックに入るので効果を持ちうるが、どのモードで何が入り何に効くかは各モデル（Models.Modifiers・Models.Dread・Models.Badges）を読まないと決められない。標準のモードだけを遊ぶので対象外（調整役の判断 2026-11-19） |
| `CombatState.Dominion` | DominionModel? | entity | ゲームモード（Blitz・Dread・カスタムの修飾子・バッジ）のモデルの一覧。IterateHookListeners でフックに入るので効果を持ちうるが、どのモードで何が入り何に効くかは各モデル（Models.Modifiers・Models.Dread・Models.Badges）を読まないと決められない。標準のモードだけを遊ぶので対象外（調整役の判断 2026-11-19） |
| `CombatState.Modifiers` | IReadOnlyList<ModifierModel> | entity | ゲームモード（Blitz・Dread・カスタムの修飾子・バッジ）のモデルの一覧。IterateHookListeners でフックに入るので効果を持ちうるが、どのモードで何が入り何に効くかは各モデル（Models.Modifiers・Models.Dread・Models.Badges）を読まないと決められない。標準のモードだけを遊ぶので対象外（調整役の判断 2026-11-19） |
| `CombatState.RunModels` | IReadOnlyList<RunModel> | entity | ゲームモード（Blitz・Dread・カスタムの修飾子・バッジ）のモデルの一覧。IterateHookListeners でフックに入るので効果を持ちうるが、どのモードで何が入り何に効くかは各モデル（Models.Modifiers・Models.Dread・Models.Badges）を読まないと決められない。標準のモードだけを遊ぶので対象外（調整役の判断 2026-11-19） |

## cosmetic（効果なし。engine は効果なしとして扱ってよい）

93 件。型・名前の規則で決めたもの（ノード・効果音・アイコンの path など）は `source=rule`、判断したものは `source=manual`。全件は TSV。判断したものだけ列挙する。

- `CardModel._isEnchantmentPreview`（bool）: エンチャントのプレビュー表示用のコピーの印（DamageVar などの表示値の計算を切り替える）
- `CardModel._upgradePreviewType`（CardUpgradePreviewType）: 強化プレビューの表示の種類（UI）。MOD は強化プレビューの組み立ての検査に読むだけ
- `CardModel._wasStarCostJustUpgraded`（bool）: 強化直後の星のコストを強調表示する印
- `UpMySleeve._timesPlayedThisCombat`（int）: OnPlay が足すだけで、読む箇所が無い
- `Wither._fakeUpgradeLevel`（int）: Title（+N）と絵だけ。ダメージは FakeUpgrade が DynamicVars.Damage を直接上げる（dynamic_vars に出る）
- `CardEnergyCost.WasJustUpgraded`（bool）: 強化直後のコストを強調表示する印（CalculatedVar の表示）
- `ExtraPlayerFields.CccomboBadgeUnlocked`（bool）: 実績・バッジ・統計の集計（CccCombo・DamageLeader・Debuffer・ProgressSaveManager）。ランの判断に効かない
- `ExtraPlayerFields.DamageDealt`（int）: 実績・バッジ・統計の集計（CccCombo・DamageLeader・Debuffer・ProgressSaveManager）。ランの判断に効かない
- `ExtraPlayerFields.DebuffsApplied`（int）: 実績・バッジ・統計の集計（CccCombo・DamageLeader・Debuffer・ProgressSaveManager）。ランの判断に効かない
- `ExtraPlayerFields.IsPlayingRotatingCharacter`（bool）: 実績・バッジ・統計の集計（CccCombo・DamageLeader・Debuffer・ProgressSaveManager）。ランの判断に効かない
- `ExtraPlayerFields.WongoPoints`（int）: ラン中に得たウォンゴのポイント。WelcomeToWongos は書くだけ（WelcomeToWongos.cs:131。条件に読むのはプロファイルの SaveManager.Instance.Progress.WongoPoints）で、ランの終わりに進捗へ足す（ProgressSaveManager）。ラン中に読む効果は無い統計（DamageDealt・DebuffsApplied と同じ）。PR #108 のレビュー P3-1
- `Player.DiscoveredEnemies`（List<ModelId>）: 図鑑・年代記の発見の記録。ランの判断に効かない
- `Player.DiscoveredEpochs`（List<string>）: 図鑑・年代記の発見の記録。ランの判断に効かない
- `CeremonialBeast._inMidCharge`（bool）: アニメーション（GenerateAnimator の遷移条件）・効果音だけが読む。行動の選択・数値に効かない
- `CeremonialBeast._isInSecondPhase`（bool）: SetStunned が書くだけで、読む箇所が無い
- `CeremonialBeast._isStunnedByPlowRemoval`（bool）: アニメーション（GenerateAnimator の遷移条件）・効果音だけが読む。行動の選択・数値に効かない
- `CorpseSlug._isRavenous`（bool）: アニメーション（GenerateAnimator の遷移条件）・効果音だけが読む。行動の選択・数値に効かない
- `CrossbowRubyRaider._isCrossbowReloaded`（bool）: アニメーション（GenerateAnimator の遷移条件）・効果音だけが読む。行動の選択・数値に効かない
- `CubexConstruct._isBurrowed`（bool）: アニメーション（GenerateAnimator の遷移条件）・効果音だけが読む。行動の選択・数値に効かない
- `CubexConstruct._isCharging`（bool）: アニメーション（GenerateAnimator の遷移条件）・効果音だけが読む。行動の選択・数値に効かない
- `FatGremlin._isAwake`（bool）: アニメーション（GenerateAnimator の遷移条件）・効果音だけが読む。行動の選択・数値に効かない
- `FuzzyWurmCrawler._isPuffed`（bool）: アニメーション（GenerateAnimator の遷移条件）・効果音だけが読む。行動の選択・数値に効かない
- `GasBomb._hasExploded`（bool）: アニメーション（GenerateAnimator の遷移条件）・効果音だけが読む。行動の選択・数値に効かない
- `KnowledgeDemon._isBurnt`（bool）: アニメーション（GenerateAnimator の遷移条件）・効果音だけが読む。行動の選択・数値に効かない
- `LagavulinMatriarch._isAwake`（bool）: WakeUpMove の効果音・アニメーションの重複を防ぐだけ（目覚めの効果は AsleepPower の除去と行動の遷移で決まり、status・move_state に出る）
- `LagavulinMatriarch._isShellAwake`（bool）: AfterDamageReceived で目を開くアニメーションを一度だけ流す印
- `LouseProgenitor._curled`（bool）: PounceMove・WebMove が丸まりを解くアニメーション・効果音を流すかだけ。丸まりの効果は CurlUpPower（status）
- `MechaKnight._isWoundUp`（bool）: アニメーション（GenerateAnimator の遷移条件）・効果音だけが読む。行動の選択・数値に効かない
- `OwlMagistrate._isFlying`（bool）: 飛行中の効果音のループ（DeathSfx・HurtSfx・BeforeRemovedFromRoom）だけ
- `PhantasmalGardener.CurrentScale`（float）: EnlargeMove の見た目の大きさ（SetDefaultScaleTo）だけ
- `PhantasmalGardener._enlargeTriggers`（int）: EnlargeMove の見た目の大きさ（SetDefaultScaleTo）だけ
- `Queen._amalgam`（Creature?）: AfterAddedToRoom・AfterDeath が書くだけで、読む箇所が無い（分岐は _hasAmalgamDied）
- `SlumberingBeetle._isAwake`（bool）: アニメーション（GenerateAnimator の遷移条件）・効果音だけが読む。行動の選択・数値に効かない
- `SneakyGremlin._isAwake`（bool）: アニメーション（GenerateAnimator の遷移条件）・効果音だけが読む。行動の選択・数値に効かない
- `SoulFysh._isInvisible`（bool）: アニメーション（GenerateAnimator の遷移条件）・効果音だけが読む。行動の選択・数値に効かない
- `SpinyToad._isSpiny`（bool）: アニメーション（GenerateAnimator の遷移条件）・効果音だけが読む。行動の選択・数値に効かない
- `TheInsatiable._hasLiquified`（bool）: アニメーション（GenerateAnimator の遷移条件）・効果音だけが読む。行動の選択・数値に効かない
- `TheObscura._hasSummoned`（bool）: アニメーション（GenerateAnimator の遷移条件）・効果音だけが読む。行動の選択・数値に効かない
- `ThievingHopper._isHovering`（bool）: 効果音（AttackSfx・FleeSfx のホバー版）とループの停止だけ
- `ToughEgg._hatched`（bool）: Title・DeathSfx（孵化後の名前・効果音）だけ
- `Tunneler._isStunned`（bool）: アニメーション（GenerateAnimator の遷移条件）・効果音だけが読む。行動の選択・数値に効かない
- `WaterfallGiant._pressureBuildupIdx`（int）: BGM のトラック（IncrementBuildUpAnimationTrack）と死亡時のフェード（ShouldFadeAfterDeath）だけ
- `HellraiserPower.Data.showedCapReachedMessage`（bool）: 自動プレイの上限に達したメッセージを一度だけ出す印
- `SandpitPower._initialAmount`（int）: UpdateCreaturePositions（クリーチャーの表示位置）だけ
- `SandpitPower._initialTargetPosition`（float）: UpdateCreaturePositions（クリーチャーの表示位置）だけ
- `BookOfFiveRings._isActivating`（bool）: DoActivateVisuals の発動演出の間だけ true で、DisplayAmount を満了の値で見せる表示用
- `Byrdine._hair`（string）: 見た目（ペットの髪型）
- `FakeHappyFlower._isActivating`（bool）: DoActivateVisuals の発動演出の間だけ true で、DisplayAmount を満了の値で見せる表示用
- `GalacticDust._isActivating`（bool）: DoActivateVisuals の発動演出の間だけ true で、DisplayAmount を満了の値で見せる表示用
- `HappyFlower._isActivating`（bool）: DoActivateVisuals の発動演出の間だけ true で、DisplayAmount を満了の値で見せる表示用
- `IronClub._isActivating`（bool）: DoActivateVisuals の発動演出の間だけ true で、DisplayAmount を満了の値で見せる表示用
- `JossPaper._isActivating`（bool）: DoActivateVisuals の発動演出の間だけ true で、DisplayAmount を満了の値で見せる表示用
- `Kunai._isActivating`（bool）: DoActivateVisuals の発動演出の間だけ true で、DisplayAmount を満了の値で見せる表示用
- `Kusarigama._isActivating`（bool）: DoActivateVisuals の発動演出の間だけ true で、DisplayAmount を満了の値で見せる表示用
- `LastingCandy._isActivating`（bool）: DoActivateVisuals の発動演出の間だけ true で、DisplayAmount を満了の値で見せる表示用
- `LetterOpener._isActivating`（bool）: DoActivateVisuals の発動演出の間だけ true で、DisplayAmount を満了の値で見せる表示用
- `Metronome._isActivating`（bool）: DoActivateVisuals の発動演出の間だけ true で、DisplayAmount を満了の値で見せる表示用
- `Nunchaku._isActivating`（bool）: DoActivateVisuals の発動演出の間だけ true で、DisplayAmount を満了の値で見せる表示用
- `OrnamentalFan._isActivating`（bool）: DoActivateVisuals の発動演出の間だけ true で、DisplayAmount を満了の値で見せる表示用
- `PaelsLegion[relic]._triggeredBlockLastTurn`（bool）: AfterSideTurnStart でペットの眠りのアニメーションを選ぶだけ
- `PaelsWing._isActivating`（bool）: DoActivateVisuals の発動演出の間だけ true で、DisplayAmount を満了の値で見せる表示用
- `PenNib._isActivating`（bool）: DoActivateVisuals の発動演出の間だけ true で、DisplayAmount を満了の値で見せる表示用
- `Pendulum._isActivating`（bool）: DoActivateVisuals の発動演出の間だけ true で、DisplayAmount を満了の値で見せる表示用
- `PollinousCore._isActivating`（bool）: DoActivateVisuals の発動演出の間だけ true で、DisplayAmount を満了の値で見せる表示用
- `Shuriken._isActivating`（bool）: DoActivateVisuals の発動演出の間だけ true で、DisplayAmount を満了の値で見せる表示用
- `StoneCalendar._isActivating`（bool）: DoActivateVisuals の発動演出の間だけ true で、DisplayAmount を満了の値で見せる表示用
- `ToyBox._isActivating`（bool）: DoActivateVisuals の発動演出の間だけ true で、DisplayAmount を満了の値で見せる表示用
- `TuningFork._isActivating`（bool）: DoActivateVisuals の発動演出の間だけ true で、DisplayAmount を満了の値で見せる表示用

## partial-resolved（派生の分岐の中だけで読み、ほかの派生は要らないと判断した基底のメンバー）（1 件）

MOD の箇所の `[only T]` は、その読み取りが届く派生の型（`!T` は T 以外）。

| クラス.メンバー | 範囲 | MOD の箇所（only は読む派生） | ほかの派生が要らない理由 |
|---|---|---|---|
| `PowerModel._internalData` | base | McpMod.PowerInternalState.cs:393[only AdaptablePower,AutomationPower,CalamityPower,ChainsOfBindingPower,CurlUpPower,DampenPower,DarkEmbracePower,GigantificationPower,HardenedShellPower,HellraiserPower,IllusionPower,JugglingPower,PanachePower,PossessSpeedPower,PossessStrengthPower,ReattachPower,RupturePower,SkittishPower,StranglePower,VigorPower] | 容器そのものは値ではない。中身は派生ごとの Data の欄（Power.Data.field の行、kind=data_field）として一つずつ分類し、出すものはその行の判断で出す（PowerInternalState の各 case・許可リストの DataField）。Data を持たない派生には読むものが無い |

## exposed（出ている）（280 件）

| クラス.メンバー | 範囲 | MOD の箇所 | 判定 |
|---|---|---|---|
| `AfflictionModel._amount` | base | McpMod.CardDetails.cs:50, McpMod.CardEnergyCost.cs:114, McpMod.CardPreview.cs:104 … | mod-scan |
| `AbstractModel.Id` | base | McpMod.Actions.cs:1014, McpMod.Actions.cs:1729, McpMod.Actions.cs:2607 … | mod-scan |
| `CardModel.Affliction` | base | McpMod.CardDetails.cs:47, McpMod.ChoiceContinuationState.cs:168, McpMod.CombatHistory.cs:105 | mod-scan |
| `CardModel.Enchantment` | base | McpMod.CardDetails.cs:117, McpMod.CardDetails.cs:65, McpMod.ChoiceContinuationState.cs:169 | mod-scan |
| `CardModel._baseReplayCount` | base | McpMod.CardDetails.cs:110 | mod-scan |
| `CardModel._baseStarCost` | base | McpMod.CardStarCost.cs:23 | mod-scan |
| `CardModel._cloneOf` | base | McpMod.StateAllowlist.Generated.cs:13 | allowlist |
| `CardModel._currentPlayIndex` | base | McpMod.StateAllowlist.Generated.cs:16 | allowlist |
| `CardModel._currentTarget` | base | McpMod.StateAllowlist.Generated.cs:19 | allowlist |
| `CardModel._currentUpgradeLevel` | base | McpMod.CombatUpgradePreview.cs:165, McpMod.CombatUpgradePreview.cs:52, McpMod.CombatUpgradePreview.cs:99 … | mod-scan |
| `CardModel._dynamicVars` | base | McpMod.CardDetails.cs:72, McpMod.StateBuilder.cs:1974, McpMod.StateBuilder.cs:2001 … | mod-scan |
| `CardModel._energyCost` | base | McpMod.CardEnergyCost.cs:36, McpMod.CardXValue.cs:29, McpMod.StateBuilder.cs:1836 | mod-scan |
| `CardModel._exhaustOnNextPlay` | base | McpMod.CardSelection.cs:143, McpMod.ChooseCardSelection.cs:43, McpMod.SelectionCandidate.cs:92 … | mod-scan |
| `CardModel._extraReplayThisTurn` | base | McpMod.CardDetails.cs:111 | mod-scan |
| `CardModel._floorAddedToDeck` | base | McpMod.StateAllowlist.Generated.cs:22 | allowlist |
| `CardModel._hasSingleTurnRetain` | base | McpMod.CardDetails.cs:106 | mod-scan |
| `CardModel._hasSingleTurnSly` | base | McpMod.CardDetails.cs:109 | mod-scan |
| `CardModel._isDupe` | base | McpMod.StateAllowlist.Generated.cs:25 | allowlist |
| `CardModel._keywords` | base | キーワードはカードの keywords（HoverTips。McpMod.StateBuilder.cs:1826）に出る | manual |
| `CardModel._keywordsAddedFromOutside` | base | McpMod.StateAllowlist.Generated.cs:28 | allowlist |
| `CardModel._keywordsRemovedFromOutside` | base | McpMod.StateAllowlist.Generated.cs:31 | allowlist |
| `CardModel._temporaryStarCosts` | base | McpMod.CardStarCost.cs:26 | mod-scan |
| `CardModel._transformIntoAfterPlay` | base | McpMod.StateAllowlist.Generated.cs:34 | allowlist |
| `Claw.DynamicVars.Damage` | other-character | McpMod.CardDetails.cs:72（DynamicVars を丸ごと出す） | mod-scan |
| `Dowsing.DynamicVars.Rooms` | event-only | McpMod.CardDetails.cs:72（DynamicVars を丸ごと出す） | mod-scan |
| `GeneticAlgorithm.DynamicVars.Block` | other-character | McpMod.CardDetails.cs:72（DynamicVars を丸ごと出す） | mod-scan |
| `Guilty.DynamicVars.Combats` | curse | McpMod.CardDetails.cs:72（DynamicVars を丸ごと出す） | mod-scan |
| `KinglyPunch.DynamicVars.Damage` | other-character | McpMod.CardDetails.cs:72（DynamicVars を丸ごと出す） | mod-scan |
| `MadScience._tinkerTimeRider` | event-only | McpMod.CardDetails.cs:125 | mod-scan |
| `MadScience._tinkerTimeType` | event-only | Type の getter がこの値を返し、カードの type に出る（McpMod.StateBuilder.cs:1829） | manual |
| `Maul.DynamicVars.Damage` | event-only | McpMod.CardDetails.cs:72（DynamicVars を丸ごと出す） | mod-scan |
| `Maul._extraDamageFromMaulPlays` | event-only | McpMod.StateAllowlist.Generated.cs:52 | allowlist |
| `Monologue.DynamicVars.Strength` | other-character | McpMod.CardDetails.cs:72（DynamicVars を丸ごと出す） | mod-scan |
| `Rampage.DynamicVars.Damage` | ironclad | McpMod.CardDetails.cs:72（DynamicVars を丸ごと出す） | mod-scan |
| `Rampage._extraDamageFromPlays` | ironclad | McpMod.StateAllowlist.Generated.cs:76 | allowlist |
| `Regret._cardsInHand` | curse | McpMod.StateAllowlist.Generated.cs:79 | allowlist |
| `ShowOff.DynamicVars.AimPower` | other-character | McpMod.CardDetails.cs:72（DynamicVars を丸ごと出す） | mod-scan |
| `SovereignBlade.DynamicVars.Damage` | other-character | McpMod.CardDetails.cs:72（DynamicVars を丸ごと出す） | mod-scan |
| `SovereignBlade.DynamicVars.HitCount` | other-character | McpMod.CardDetails.cs:72（DynamicVars を丸ごと出す） | mod-scan |
| `SpoilsMap.SpoilsCoord` | event-only | ModifyGeneratedMapLate で地図の点に AddQuest する。地図の点の quests に出る（McpMod.StateBuilder.cs:2976） | manual |
| `SpoilsMap._spoilsActIndex` | event-only | McpMod.StateAllowlist.Generated.cs:91 | allowlist |
| `TheBall.DynamicVars.Damage` | colorless | McpMod.CardDetails.cs:72（DynamicVars を丸ごと出す） | mod-scan |
| `TheBall._extraDamageFromPlays` | colorless | McpMod.StateAllowlist.Generated.cs:94 | allowlist |
| `TheScythe.DynamicVars.Damage` | other-character | McpMod.CardDetails.cs:72（DynamicVars を丸ごと出す） | mod-scan |
| `Thrash.DynamicVars.Damage` | ironclad | McpMod.CardDetails.cs:72（DynamicVars を丸ごと出す） | mod-scan |
| `Thrash._extraDamage` | ironclad | McpMod.StateAllowlist.Generated.cs:97 | allowlist |
| `Adroit.DynamicVars.Block` | enchantment | McpMod.CardDetails.cs:130（DynamicVars を丸ごと出す） | mod-scan |
| `EnchantmentModel._amount` | base | McpMod.CardDetails.cs:67, McpMod.CardEnergyCost.cs:114, McpMod.CardPreview.cs:104 … | mod-scan |
| `EnchantmentModel._dynamicVars` | base | McpMod.CardDetails.cs:130 | mod-scan |
| `EnchantmentModel._status` | base | McpMod.CardDetails.cs:140 | mod-scan |
| `Momentum._extraDamage` | enchantment | McpMod.CardDetails.cs:162 | mod-scan |
| `CardEnergyCost.Canonical` | entity | McpMod.CardEnergyCost.cs:47 | mod-scan |
| `CardEnergyCost.CostsX` | entity | McpMod.CardEnergyCost.cs:45, McpMod.StateBuilder.cs:1836 | mod-scan |
| `CardEnergyCost._base` | entity | energy_cost.base_raw（GetWithModifiers(None)。McpMod.CardEnergyCost.cs:46） | manual |
| `CardEnergyCost._capturedXValue` | entity | McpMod.CardEnergyCost.cs:49 | mod-scan |
| `CardEnergyCost._localModifiers` | entity | energy_cost の local_modifiers（McpMod.CardEnergyCost.cs:52。ReadLocalEnergyCostModifiers〔同 :103〕が cost.GetType() で読むので型の文脈が無く、mod-scan では数えない） | manual |
| `CombatState.CurrentSide` | entity | McpMod.CombatLog.cs:77, McpMod.Helpers.cs:46, McpMod.MultiplayerState.cs:292 … | mod-scan |
| `CombatState.RoundNumber` | entity | McpMod.CombatLog.cs:76, McpMod.MultiplayerActions.cs:127, McpMod.MultiplayerActions.cs:99 … | mod-scan |
| `CombatState._allies` | entity | McpMod.StateAllowlist.Generated.cs:37 | allowlist |
| `CombatState._encounter` | entity | McpMod.EnemyMoveContext.cs:59 | mod-scan |
| `CombatState._enemies` | entity | battle.enemies（McpMod.StateBuilder.cs:1603） | manual |
| `CombatState._escapedCreatures` | entity | McpMod.StateAllowlist.Generated.cs:40 | allowlist |
| `Creature.CombatId` | entity | McpMod.ChoiceContinuationState.cs:296, McpMod.ChoiceContinuationState.cs:299, McpMod.CombatHistory.cs:269 … | mod-scan |
| `Creature.HpDisplay` | entity | McpMod.EnemyHpDisplay.cs:22 | manual |
| `Creature.Side` | entity | McpMod.CombatLogPatches.cs:792, McpMod.PotionUsabilityLive.cs:126, McpMod.PotionUsabilityLive.cs:244 | mod-scan |
| `Creature.SlotName` | entity | McpMod.EnemyMoveContext.cs:106, McpMod.EnemyMoveContext.cs:94 | mod-scan |
| `Creature._block` | entity | McpMod.CombatLogPatches.cs:835, McpMod.MultiplayerState.cs:449, McpMod.PowerInternalState.cs:218 … | mod-scan |
| `Creature._currentHp` | entity | McpMod.CombatLogPatches.cs:833, McpMod.MultiplayerState.cs:442, McpMod.StateBuilder.cs:1512 … | mod-scan |
| `Creature._maxHp` | entity | McpMod.CombatLogPatches.cs:834, McpMod.MultiplayerState.cs:443, McpMod.StateBuilder.cs:1513 … | mod-scan |
| `Creature._powers` | entity | status（McpMod.StateBuilder.cs:1710・2129） | manual |
| `ExtraPlayerFields.CardShopRemovalsUsed` | entity | McpMod.StateAllowlist.Generated.cs:43 | allowlist |
| `MonsterMoveStateMachine.StateLog` | entity | McpMod.EnemyMoveState.cs:103, McpMod.EnemyMoveState.cs:45 | mod-scan |
| `MonsterMoveStateMachine.States` | entity | McpMod.EnemyMoveState.cs:72 | mod-scan |
| `MonsterMoveStateMachine._currentState` | entity | McpMod.EnemyMoveState.cs:71 | mod-scan |
| `MonsterMoveStateMachine._initialState` | entity | McpMod.EnemyMoveState.cs:76 | mod-scan |
| `MonsterMoveStateMachine._performedFirstMove` | entity | McpMod.EnemyMoveState.cs:78 | mod-scan |
| `Player.Character` | entity | McpMod.Actions.cs:2606, McpMod.Actions.cs:2607, McpMod.Actions.cs:2608 … | mod-scan |
| `Player.Deck` | entity | McpMod.CardIdentity.cs:104, McpMod.ChoiceContinuationState.cs:282, McpMod.StateBuilder.cs:1814 … | mod-scan |
| `Player.DiscoveredCards` | entity | McpMod.Compendium.cs:85, McpMod.Profile.cs:345, McpMod.Wiki.cs:74 | mod-scan |
| `Player.DiscoveredPotions` | entity | McpMod.Compendium.cs:87, McpMod.Profile.cs:347 | mod-scan |
| `Player.DiscoveredRelics` | entity | McpMod.Compendium.cs:86, McpMod.Profile.cs:346, McpMod.Wiki.cs:78 | mod-scan |
| `Player.MaxEnergy` | entity | McpMod.StateBuilder.cs:1173 | mod-scan |
| `Player.NetId` | entity | McpMod.CardIdentity.cs:110, McpMod.ChoiceAwaitChain.cs:216, McpMod.ChoiceAwaitChain.cs:234 … | mod-scan |
| `Player.UnlockState` | entity | McpMod.GenerationPool.cs:39, McpMod.GenerationPool.cs:46, McpMod.GenerationPool.cs:57 … | mod-scan |
| `Player._canUseOrRemovePotions` | entity | McpMod.PotionUsabilityLive.cs:115 | mod-scan |
| `Player._gold` | entity | McpMod.MultiplayerState.cs:444, McpMod.PowerInternalState.cs:47, McpMod.StateBuilder.cs:1514 … | mod-scan |
| `Player._potionSlots` | entity | player.potions（McpMod.StateBuilder.cs:1755） | manual |
| `Player._relics` | entity | player.relics（McpMod.StateBuilder.cs:1743） | manual |
| `Player._runPiles` | entity | player.deck（McpMod.StateBuilder.cs:1794） | manual |
| `PlayerCombatState.DiscardPile` | entity | McpMod.StateBuilder.cs:1681, McpMod.StateBuilder.cs:1687 | mod-scan |
| `PlayerCombatState.DrawPile` | entity | McpMod.ChoiceAwaitChain.cs:127, McpMod.ChoiceAwaitChain.cs:365, McpMod.StateBuilder.cs:1680 … | mod-scan |
| `PlayerCombatState.ExhaustPile` | entity | McpMod.StateBuilder.cs:1682, McpMod.StateBuilder.cs:1688 | mod-scan |
| `PlayerCombatState.Hand` | entity | McpMod.Actions.cs:189, McpMod.Actions.cs:261, McpMod.CardXValue.cs:26 … | mod-scan |
| `PlayerCombatState.OrbQueue` | entity | McpMod.StateBuilder.cs:1696 | mod-scan |
| `PlayerCombatState.PlayPile` | entity | McpMod.StateBuilder.cs:1683, McpMod.StateBuilder.cs:1689 | mod-scan |
| `PlayerCombatState.TurnNumber` | entity | McpMod.Actions.cs:271, McpMod.CombatHistory.cs:29 | mod-scan |
| `PlayerCombatState._energy` | entity | McpMod.CombatLogPatches.cs:290, McpMod.CombatLogPatches.cs:842, McpMod.CombatLogPatches.cs:846 … | mod-scan |
| `PlayerCombatState._pets` | entity | player.pets（McpMod.StateBuilder.cs:1700） | manual |
| `PlayerCombatState._phase` | entity | McpMod.Helpers.cs:26, McpMod.Helpers.cs:36, McpMod.StateBuilder.cs:761 | mod-scan |
| `PlayerCombatState._piles` | entity | McpMod.TransformationOptions.cs:29, McpMod.TransformationOptions.cs:44 | manual |
| `PlayerCombatState._stars` | entity | McpMod.StateBuilder.cs:1663, McpMod.StateBuilder.cs:1665 | mod-scan |
| `Aeonglass._additionalStrength` | monster | McpMod.EnemyCombatState.cs:52 | mod-scan |
| `Aeonglass._witherUpgradeCount` | monster | McpMod.EnemyCombatState.cs:54 | mod-scan |
| `Axebot._stockOverrideAmount` | monster | public getter StockAmount（_stockOverrideAmount ?? 2）として combat_state.stock_amount に出ている（McpMod.EnemyCombatState.cs:58） | manual |
| `BowlbugRock._isOffBalance` | monster | McpMod.EnemyCombatState.cs:62 | mod-scan |
| `Fabricator._lastSpawned` | monster | McpMod.EnemyCombatState.cs:127, McpMod.EnemyCombatState.cs:129 | mod-scan |
| `FrogKnight._hasBeetleCharged` | monster | McpMod.EnemyCombatState.cs:69 | mod-scan |
| `GremlinMerc._hasSpoken` | monster | McpMod.EnemyCombatState.cs:74 | mod-scan |
| `KinPriest._speechUsed` | monster | McpMod.EnemyCombatState.cs:78 | mod-scan |
| `KnowledgeDemon.DynamicVars.DisintegrationPower` | monster | ChooseCurse が生成したカード Disintegration の DynamicVars を書き換える（モンスター自身の変数ではない）。カードの dynamic_vars に出る（McpMod.CardDetails.cs:72） | manual |
| `KnowledgeDemon._curseOfKnowledgeCounter` | monster | McpMod.EnemyCombatState.cs:82 | mod-scan |
| `LivingFog._bloatAmount` | monster | McpMod.EnemyCombatState.cs:86 | mod-scan |
| `MonsterModel.NextMove` | base | McpMod.EnemyMoveState.cs:30, McpMod.EnemyStunHandler.cs:48, McpMod.StateBuilder.cs:2188 … | mod-scan |
| `MonsterModel._moveStateMachine` | base | McpMod.EnemyMoveState.cs:32 | mod-scan |
| `MonsterModel._spawnedThisTurn` | base | McpMod.EnemyMoveState.cs:80 | mod-scan |
| `Nibbit._isAlone` | monster | McpMod.EnemyCombatState.cs:90 | mod-scan |
| `Nibbit._isFront` | monster | McpMod.EnemyCombatState.cs:91 | mod-scan |
| `Queen._hasAmalgamDied` | monster | McpMod.EnemyCombatState.cs:95 | mod-scan |
| `TestSubject._extraMultiClawCount` | monster | McpMod.EnemyCombatState.cs:101 | mod-scan |
| `TestSubject._respawns` | monster | McpMod.EnemyCombatState.cs:99 | mod-scan |
| `Toadpole._isFront` | monster | McpMod.EnemyCombatState.cs:105 | mod-scan |
| `TwoTailedRat._callForBackupCount` | monster | McpMod.EnemyCombatState.cs:111 | mod-scan |
| `TwoTailedRat._turnsUntilSummonable` | monster | McpMod.EnemyCombatState.cs:109 | mod-scan |
| `WaterfallGiant._currentPressureGunDamage` | monster | McpMod.EnemyCombatState.cs:39 | mod-scan |
| `WaterfallGiant._isAboutToBlow` | monster | McpMod.EnemyCombatState.cs:34 | mod-scan |
| `WaterfallGiant._steamEruptionDamage` | monster | McpMod.EnemyCombatState.cs:36 | mod-scan |
| `OrbModel._owner` | base | McpMod.Actions.cs:321, McpMod.AutoPlayReservation.cs:162, McpMod.AutoPlayReservation.cs:223 … | mod-scan |
| `PotionModel.IsQueued` | base | McpMod.Actions.cs:319, McpMod.PotionUsabilityLive.cs:110 | mod-scan |
| `AdaptablePower.Data.isReviving` | monster | McpMod.PowerInternalState.cs:159 | mod-scan |
| `AutomationPower.Data.cardsLeft` | colorless | McpMod.PowerInternalState.cs:137 | mod-scan |
| `BeaconOfHopePower._hasAlreadyBeenGivenBlock` | colorless | McpMod.PowerInternalState.cs:192 | mod-scan |
| `CacophonyPower.DynamicVars.Cards` | other-character | McpMod.PowerInternalState.cs:290（DynamicVars を丸ごと出す） | mod-scan |
| `CalamityPower.Data.amountsForPlayedCards` | colorless | McpMod.PowerInternalState.cs:123 | mod-scan |
| `ChainsOfBindingPower.Data.boundCardPlayed` | affliction | McpMod.PowerInternalState.cs:233 | mod-scan |
| `CrimsonMantlePower.DynamicVars.SelfDamage` | ironclad | McpMod.PowerInternalState.cs:290（DynamicVars を丸ごと出す） | mod-scan |
| `CurlUpPower.Data.playedCard` | monster | McpMod.PowerInternalState.cs:240 | mod-scan |
| `DampenPower.Data.casters` | monster | status の caster_combat_ids（McpMod.PowerInternalState.cs:430） | manual |
| `DampenPower.Data.downgradedCardsToOldUpgradeLevels` | monster | status の downgraded_cards（McpMod.PowerInternalState.cs:446） | manual |
| `DarkEmbracePower.Data.etherealCount` | ironclad | McpMod.PowerInternalState.cs:142 | mod-scan |
| `EncorePower.DynamicVars.PlaysRemaining` | other-character | McpMod.PowerInternalState.cs:290（DynamicVars を丸ごと出す） | mod-scan |
| `EnnuiPower.DynamicVars.Cards` | other-character | McpMod.PowerInternalState.cs:290（DynamicVars を丸ごと出す） | mod-scan |
| `GigantificationPower.Data.commandToModify` | shared | status の has_command_to_modify（McpMod.PowerInternalState.cs:154） | manual |
| `HardenedShellPower.Data.damageReceivedThisTurn` | monster | McpMod.PowerInternalState.cs:153 | mod-scan |
| `HellraiserPower.Data.autoPlayingCards` | ironclad | McpMod.PowerInternalState.cs:132 | mod-scan |
| `HellraiserPower.Data.infiniteAutoPlaysThisTurn` | ironclad | McpMod.PowerInternalState.cs:131 | mod-scan |
| `IllusionPower._followUpStateId` | monster | McpMod.PowerInternalState.cs:169 | mod-scan |
| `IllusionPower.Data.isReviving` | monster | McpMod.PowerInternalState.cs:159 | mod-scan |
| `ImpaledPower.DynamicVars.HitCounter` | monster | McpMod.PowerInternalState.cs:290（DynamicVars を丸ごと出す） | mod-scan |
| `InfernoPower.DynamicVars.SelfDamage` | ironclad | McpMod.PowerInternalState.cs:290（DynamicVars を丸ごと出す） | mod-scan |
| `InsatiableDominionPower.DynamicVars.Damage` | unreferenced | McpMod.PowerInternalState.cs:290（DynamicVars を丸ごと出す） | mod-scan |
| `InterceptPower.Data.coveredCreatures` | colorless | McpMod.StateAllowlist.Generated.cs:49 | allowlist |
| `JugglingPower.Data.attacksPlayedThisTurn` | ironclad | McpMod.PowerInternalState.cs:236 | mod-scan |
| `JugglingPower.Data.cardToClone` | ironclad | McpMod.PowerInternalState.cs:237 | mod-scan |
| `MonologuePower.DynamicVars.StrengthApplied` | other-character | McpMod.PowerInternalState.cs:290（DynamicVars を丸ごと出す） | mod-scan |
| `MythicFormPower.DynamicVars.Upgraded` | other-character | McpMod.PowerInternalState.cs:290（DynamicVars を丸ごと出す） | mod-scan |
| `NemesisPower._shouldApplyIntangible` | monster | McpMod.PowerInternalState.cs:178 | mod-scan |
| `PanachePower.DynamicVars.CardsLeft` | colorless | McpMod.PowerInternalState.cs:290（DynamicVars を丸ごと出す） | mod-scan |
| `PanachePower.Data.alreadyApplied` | colorless | McpMod.PowerInternalState.cs:93 | mod-scan |
| `PlatingPower.DynamicVars.Decrement` | ironclad | McpMod.PowerInternalState.cs:290（DynamicVars を丸ごと出す） | mod-scan |
| `PossessSpeedPower.Data.stolenDexterity` | monster | McpMod.PowerInternalState.cs:227 | mod-scan |
| `PossessStrengthPower.Data.stolenStrength` | monster | McpMod.PowerInternalState.cs:224 | mod-scan |
| `PowerModel._amount` | base | McpMod.CardEnergyCost.cs:114, McpMod.CardPreview.cs:104, McpMod.CardPreview.cs:83 … | mod-scan |
| `PowerModel._amountOnTurnStart` | base | McpMod.StateAllowlist.Generated.cs:67 | allowlist |
| `PowerModel._applier` | base | McpMod.StateAllowlist.Generated.cs:70 | allowlist |
| `PowerModel._dynamicVars` | base | McpMod.PowerInternalState.cs:290 | mod-scan |
| `PowerModel._internalData` | base | McpMod.PowerInternalState.cs:393[only AdaptablePower,AutomationPower,CalamityPower,ChainsOfBindingPower,CurlUpPower,DampenPower,DarkEmbracePower,GigantificationPower,HardenedShellPower,HellraiserPower,IllusionPower,JugglingPower,PanachePower,PossessSpeedPower,PossessStrengthPower,ReattachPower,RupturePower,SkittishPower,StranglePower,VigorPower] | partial-resolved |
| `PowerModel._skipNextDurationTick` | base | McpMod.CombatLogPatches.cs:934, McpMod.PowerInternalState.cs:214 | mod-scan |
| `PowerModel._target` | base | McpMod.StateAllowlist.Generated.cs:73 | allowlist |
| `ReattachPower.Data.isReviving` | monster | McpMod.PowerInternalState.cs:159 | mod-scan |
| `RitualPower._wasJustAppliedByEnemy` | shared | McpMod.PowerInternalState.cs:67 | mod-scan |
| `RupturePower.Data.playedCards` | ironclad | McpMod.PowerInternalState.cs:115 | mod-scan |
| `SkittishPower.Data.hasGainedBlockThisTurn` | monster | McpMod.PowerInternalState.cs:230 | mod-scan |
| `SlothPower._cardsPlayedThisTurn` | monster | McpMod.PowerInternalState.cs:183 | mod-scan |
| `SlowPower.DynamicVars.DisplayAmount` | monster | McpMod.PowerInternalState.cs:290（DynamicVars を丸ごと出す） | mod-scan |
| `SlowPower.DynamicVars.SlowAmount` | monster | McpMod.PowerInternalState.cs:290（DynamicVars を丸ごと出す） | mod-scan |
| `StranglePower.Data.amountsForPlayedCards` | event-only | McpMod.PowerInternalState.cs:123 | mod-scan |
| `SurroundedPower._facing` | monster | McpMod.PowerInternalState.cs:221 | mod-scan |
| `SwipePower._stolenCard` | monster | McpMod.PowerInternalState.cs:200, McpMod.PowerInternalState.cs:202 | mod-scan |
| `TenderPower._cardsPlayedThisTurn` | monster | McpMod.PowerInternalState.cs:188 | mod-scan |
| `TheBombPower.DynamicVars.Damage` | colorless | McpMod.PowerInternalState.cs:290（DynamicVars を丸ごと出す） | mod-scan |
| `ThieveryPower.DynamicVars.Gold` | monster | McpMod.PowerInternalState.cs:290（DynamicVars を丸ごと出す） | mod-scan |
| `ToricToughnessPower.DynamicVars.Block` | event-only | McpMod.PowerInternalState.cs:290（DynamicVars を丸ごと出す） | mod-scan |
| `VigorPower.Data.amountWhenAttackStarted` | colorless | status の vigor_attack.amount_when_started（McpMod.PowerInternalState.cs:353） | manual |
| `VigorPower.Data.commandToModify` | colorless | status の vigor_attack.has_command・attacker_is_owner（McpMod.PowerInternalState.cs:378） | manual |
| `WitheringPresencePower.DynamicVars.CardsLeft` | monster | McpMod.PowerInternalState.cs:290（DynamicVars を丸ごと出す） | mod-scan |
| `ArtOfWar._anyAttacksPlayedLastTurn` | shared | McpMod.RelicCombatState.cs:36 | mod-scan |
| `ArtOfWar._anyAttacksPlayedThisTurn` | shared | McpMod.RelicCombatState.cs:35 | mod-scan |
| `BathRobes._wasUsed` | event-only | IsUsedUp => _wasUsed。レリックの used_up に出ている（McpMod.StateBuilder.cs:1722） | manual |
| `BeatingRemnant._damageReceivedThisTurn` | shared | McpMod.RelicCombatState.cs:76 | mod-scan |
| `BeltBuckle._dexterityApplied` | shared | McpMod.RelicCombatState.cs:57 | mod-scan |
| `BoneTea.DynamicVars.Combats` | event-only | McpMod.RelicCombatState.cs:271（DynamicVars を丸ごと出す） | mod-scan |
| `BoneTea._combatsLeft` | event-only | setter が DynamicVars["Combats"] に写す。レリックの vars.Combats に出ている（McpMod.RelicCombatState.cs:271） | manual |
| `BookOfFiveRings._cardsAdded` | shared | McpMod.RelicCombatState.cs:194 | mod-scan |
| `BrilliantScarf._cardsPlayedThisTurn` | ancient | McpMod.RelicCombatState.cs:98 | mod-scan |
| `BurningSticks._wasUsedThisCombat` | shared | McpMod.RelicCombatState.cs:60 | mod-scan |
| `Byrdpip[relic]._swoopsPlayed` | event-only | DisplayAmount = Math.Min(SwoopsPlayed, 6)。AfterCardPlayed は 6 以上なら数えないので値は 6 を超えず、counter と一致（McpMod.StateBuilder.cs:1721） | manual |
| `CentennialPuzzle._usedThisCombat` | shared | McpMod.RelicCombatState.cs:23 | mod-scan |
| `DemonTongue._triggeredThisTurn` | ironclad | McpMod.RelicCombatState.cs:45 | mod-scan |
| `EmberTea.DynamicVars.Combats` | event-only | McpMod.RelicCombatState.cs:271（DynamicVars を丸ごと出す） | mod-scan |
| `EmberTea._combatsLeft` | event-only | setter が DynamicVars["Combats"] に写す。レリックの vars.Combats に出ている（McpMod.RelicCombatState.cs:271） | manual |
| `FakeHappyFlower._turnsSeen` | event-only | McpMod.RelicCombatState.cs:180 | mod-scan |
| `FakeOrichalcum._shouldTrigger` | event-only | McpMod.RelicCombatState.cs:131 | mod-scan |
| `FakeVenerableTeaSet._gainEnergyInNextCombat` | event-only | McpMod.RelicCombatState.cs:199 | mod-scan |
| `FishingRod._combatsSeen` | ancient | McpMod.RelicCombatState.cs:223 | mod-scan |
| `FurCoat.FurCoatCoordCols` | ancient | McpMod.RelicCombatState.cs:296 | mod-scan |
| `FurCoat.FurCoatCoordRows` | ancient | McpMod.RelicCombatState.cs:297 | mod-scan |
| `FurCoat.FurCoatCoordsSet` | ancient | McpMod.RelicCombatState.cs:294 | mod-scan |
| `FurCoat._furCoatActIndex` | ancient | FurCoatActIndex として地図の印の絞り込みに使い、印のある部屋が map に出る（McpMod.StateBuilder.cs:3012） | manual |
| `Girya._timesLifted` | shared | McpMod.RelicCombatState.cs:185 | mod-scan |
| `GoldenCompass._goldenPathAct` | ancient | McpMod.StateAllowlist.Generated.cs:46 | allowlist |
| `HappyFlower._turnsSeen` | shared | McpMod.RelicCombatState.cs:180 | mod-scan |
| `IronClub._cardsPlayed` | ancient | McpMod.RelicCombatState.cs:170 | mod-scan |
| `JossPaper._cardsExhausted` | shared | McpMod.RelicCombatState.cs:101 | mod-scan |
| `JossPaper._etherealCount` | shared | McpMod.RelicCombatState.cs:103 | mod-scan |
| `Kunai._attacksPlayedThisTurn` | shared | McpMod.RelicCombatState.cs:121 | mod-scan |
| `Kusarigama._attacksPlayedThisTurn` | shared | McpMod.RelicCombatState.cs:121 | mod-scan |
| `LastingCandy._combatRewardsSeen` | event-only | McpMod.RelicCombatState.cs:190 | mod-scan |
| `LavaLamp._tookDamageThisCombat` | shared | McpMod.RelicCombatState.cs:91 | mod-scan |
| `LavaRock._hasTriggered` | ancient | McpMod.RelicCombatState.cs:232 | mod-scan |
| `LetterOpener._skillsPlayedThisTurn` | shared | McpMod.RelicCombatState.cs:124 | mod-scan |
| `LizardTail._wasUsed` | shared | IsUsedUp => _wasUsed。レリックの used_up に出ている（McpMod.StateBuilder.cs:1722） | manual |
| `MawBank._hasItemBeenBought` | event-only | IsUsedUp => HasItemBeenBought。レリックの used_up に出ている（McpMod.StateBuilder.cs:1722） | manual |
| `MiniRegent._usedThisTurn` | other-character | McpMod.RelicCombatState.cs:79 | mod-scan |
| `MusicBox._cardBeingPlayed` | ancient | McpMod.StateAllowlist.Generated.cs:55 | allowlist |
| `MusicBox._wasUsedThisTurn` | ancient | McpMod.RelicCombatState.cs:63 | mod-scan |
| `Nunchaku._attacksPlayed` | shared | McpMod.RelicCombatState.cs:164 | mod-scan |
| `Orichalcum._shouldTrigger` | shared | McpMod.RelicCombatState.cs:131 | mod-scan |
| `OrnamentalFan._attacksPlayedThisTurn` | shared | McpMod.RelicCombatState.cs:121 | mod-scan |
| `PaelsEye._usedThisCombat` | ancient | McpMod.RelicCombatState.cs:66 | mod-scan |
| `PaelsEye._wasOwnerPartOfLastPlayerTurn` | ancient | McpMod.RelicCombatState.cs:67 | mod-scan |
| `PaelsLegion[relic]._affectedCardPlay` | ancient | McpMod.StateAllowlist.Generated.cs:58 | allowlist |
| `PaelsLegion[relic]._cooldown` | ancient | McpMod.RelicCombatState.cs:73 | mod-scan |
| `PaelsTears._hadLeftoverEnergy` | ancient | McpMod.RelicCombatState.cs:39 | mod-scan |
| `PaelsTooth._serializableCards` | ancient | McpMod.StateAllowlist.Generated.cs:61 | allowlist |
| `PaelsWing._rewardsSacrificed` | ancient | McpMod.RelicCombatState.cs:229 | mod-scan |
| `PenNib._attackToDouble` | shared | McpMod.StateAllowlist.Generated.cs:64 | allowlist |
| `PenNib._attacksPlayed` | shared | McpMod.RelicCombatState.cs:94 | mod-scan |
| `Pendulum._turnsSeen` | shared | McpMod.RelicCombatState.cs:180 | mod-scan |
| `Permafrost._activatedThisCombat` | shared | McpMod.RelicCombatState.cs:32 | mod-scan |
| `Pocketwatch._cardsPlayedLastTurn` | shared | McpMod.RelicCombatState.cs:48 | mod-scan |
| `Pocketwatch._cardsPlayedThisTurn` | shared | McpMod.RelicCombatState.cs:51 | mod-scan |
| `PollinousCore._turnsSeen` | event-only | McpMod.RelicCombatState.cs:180 | mod-scan |
| `PumpkinCandle._kindleCount` | ancient | McpMod.RelicCombatState.cs:203 | mod-scan |
| `RainbowRing._activationCountThisTurn` | shared | McpMod.RelicCombatState.cs:29 | mod-scan |
| `RainbowRing._attacksPlayedThisTurn` | shared | McpMod.RelicCombatState.cs:26 | mod-scan |
| `RainbowRing._powersPlayedThisTurn` | shared | McpMod.RelicCombatState.cs:28 | mod-scan |
| `RainbowRing._skillsPlayedThisTurn` | shared | McpMod.RelicCombatState.cs:27 | mod-scan |
| `RedSkull._strengthApplied` | ironclad | McpMod.RelicCombatState.cs:54 | mod-scan |
| `Regalite._usedThisTurn` | other-character | McpMod.RelicCombatState.cs:82 | mod-scan |
| `RelicModel._dynamicVars` | base | McpMod.RelicCombatState.cs:271 | mod-scan |
| `RelicModel._floorAddedToDeck` | base | McpMod.StateAllowlist.Generated.cs:82 | allowlist |
| `RelicModel._isMelted` | base | McpMod.StateBuilder.cs:1758 | mod-scan |
| `RelicModel._isWax` | base | McpMod.StateAllowlist.Generated.cs:85 | allowlist |
| `RelicModel._status` | base | McpMod.StateAllowlist.Generated.cs:88 | allowlist |
| `RuinedHelmet._usedThisCombat` | ironclad | McpMod.RelicCombatState.cs:85 | mod-scan |
| `Shuriken._attacksPlayedThisTurn` | shared | McpMod.RelicCombatState.cs:121 | mod-scan |
| `SilkenTress._isUsed` | ancient | IsUsedUp => IsUsed。レリックの used_up に出ている（McpMod.StateBuilder.cs:1722） | manual |
| `SilverCrucible._timesUsed` | ancient | McpMod.RelicCombatState.cs:218 | mod-scan |
| `SilverCrucible._treasureRoomsEntered` | ancient | McpMod.RelicCombatState.cs:219 | mod-scan |
| `SwordOfStone._elitesDefeated` | event-only | McpMod.RelicCombatState.cs:212 | mod-scan |
| `TeaOfDiscourtesy.DynamicVars.Combats` | event-only | McpMod.RelicCombatState.cs:271（DynamicVars を丸ごと出す） | mod-scan |
| `TeaOfDiscourtesy._combatsLeft` | event-only | setter が DynamicVars["Combats"] に写す。レリックの vars.Combats に出ている（McpMod.RelicCombatState.cs:271） | manual |
| `ThrowingAxe._usedThisCombat` | ancient | McpMod.RelicCombatState.cs:70 | mod-scan |
| `ToyBox._combatsSeen` | ancient | McpMod.RelicCombatState.cs:223 | mod-scan |
| `TuningFork._skillsPlayed` | shared | McpMod.RelicCombatState.cs:167 | mod-scan |
| `UnsettlingLamp._doubledPowers` | shared | McpMod.StateAllowlist.Generated.cs:100 | allowlist |
| `UnsettlingLamp._isFinishedTriggering` | shared | McpMod.RelicCombatState.cs:88 | mod-scan |
| `UnsettlingLamp._triggeringCard` | shared | McpMod.StateAllowlist.Generated.cs:103 | allowlist |
| `Vambrace._blockGainedThisCombat` | shared | McpMod.RelicCombatState.cs:42 | mod-scan |
| `Vambrace._triggeringCard` | shared | McpMod.StateAllowlist.Generated.cs:106 | allowlist |
| `VenerableTeaSet._gainEnergyInNextCombat` | shared | McpMod.RelicCombatState.cs:199 | mod-scan |
| `WingedBoots.DynamicVars.Rooms` | ancient | McpMod.RelicCombatState.cs:271（DynamicVars を丸ごと出す） | mod-scan |
| `WingedBoots._timesUsed` | ancient | McpMod.RelicCombatState.cs:215 | mod-scan |
| `WongosMysteryTicket.DynamicVars.RemainingCombats` | event-only | McpMod.RelicCombatState.cs:271（DynamicVars を丸ごと出す） | mod-scan |
| `WongosMysteryTicket._combatsFinished` | event-only | McpMod.RelicCombatState.cs:226 | mod-scan |
| `WongosMysteryTicket._gaveRelic` | event-only | IsUsedUp => GaveRelic。レリックの used_up に出ている（McpMod.StateBuilder.cs:1722） | manual |
| `Wuppy[relic].DynamicVars.CurrentHp` | event-only | McpMod.RelicCombatState.cs:271（DynamicVars を丸ごと出す） | mod-scan |
| `Wuppy[relic].DynamicVars.MaxHp` | event-only | McpMod.RelicCombatState.cs:271（DynamicVars を丸ごと出す） | mod-scan |
| `Wuppy[relic]._currentHp` | event-only | setter が DynamicVars（CurrentHp・MaxHp）に写す。レリックの vars に出ている（McpMod.RelicCombatState.cs:271） | manual |
| `Wuppy[relic]._maxHp` | event-only | setter が DynamicVars（CurrentHp・MaxHp）に写す。レリックの vars に出ている（McpMod.RelicCombatState.cs:271） | manual |

## static/derived（出す必要が無い）（91 件）

| クラス.メンバー | 型 | 範囲 | 何から決まるか |
|---|---|---|---|
| `AfflictionModel._canonicalInstance` | AfflictionModel | base | 正規インスタンスへの参照（ModelDb の定義。インスタンスごとの状態ではない） |
| `AfflictionModel._card` | CardModel? | base | 他のモデルへの参照（所属）。state では入れ子・id で表す |
| `AbstractModel.CategorySortingId` | int | base | ModelDb の並び順（InitId）。ゲームの状態ではない |
| `AbstractModel.EntrySortingId` | int | base | ModelDb の並び順（InitId）。ゲームの状態ではない |
| `AbstractModel.IsMutable` | bool | base | インスタンスが可変（正規インスタンスでない）かの印。MOD の内部の検査に使うだけで、ゲームの状態ではない |
| `CardModel.HasBeenRemovedFromState` | bool | base | state から外れたモデルの印（RemoveFromState）。外れたモデルは state の一覧に出ない |
| `CardModel.ShouldShowInCardLibrary` | bool | base | コンストラクタで決まる定数（図鑑の表示） |
| `CardModel._canonicalInstance` | CardModel? | base | 正規インスタンスへの参照（ModelDb の定義。インスタンスごとの状態ではない） |
| `CardModel._deckVersion` | CardModel? | base | デッキ側の同じカードへの参照（戦闘のコピーの元）。MOD は uid の採番に使うだけ |
| `CardModel._isUpgrading` | bool | base | UpgradeInternal の実行中だけ true の一時値。state を読む安定点では false |
| `CardModel._owner` | Player? | base | 他のモデルへの参照（所属）。state では入れ子・id で表す |
| `CardModel._pool` | CardPoolModel? | base | カードの定義から決まる（プール・CanonicalTags・星のコストの初期化の印） |
| `CardModel._starCostSet` | bool | base | カードの定義から決まる（プール・CanonicalTags・星のコストの初期化の印） |
| `CardModel._tags` | HashSet<CardTag>? | base | カードの定義から決まる（プール・CanonicalTags・星のコストの初期化の印） |
| `Discovery._mockSelectedCard` | CardModel? | colorless | テスト用の上書き（SetupForTests・Mock*・Set*Test* からだけ書く）。通常のランでは既定値のまま |
| `Dowsing._roomsEntered` | int | event-only | setter が DynamicVars["Rooms"] = max(1, 4 - RoomsEntered) に写す。4 で変化するので 0〜3 の範囲で dynamic_vars.Rooms から一意に決まる |
| `GeneticAlgorithm._currentBlock` | int | other-character | CanonicalVars の初期値に使う写し（Update* で更新）。今の値は DynamicVars.Block として dynamic_vars に出る |
| `Guilty._combatsSeen` | int | curse | setter が DynamicVars["Combats"] = 5 - CombatsSeen に写す。dynamic_vars.Combats から決まる |
| `MadScience._mockedChaosCard` | CardModel? | event-only | テスト用の上書き（SetupForTests・Mock*・Set*Test* からだけ書く）。通常のランでは既定値のまま |
| `ShowOff._currentAim` | int | other-character | CanonicalVars の初期値に使う写し（Update* で更新）。今の値は DynamicVars（AimPower） として dynamic_vars に出る |
| `Splash._mockGeneratedCard` | CardModel? | colorless | テスト用の上書き（SetupForTests・Mock*・Set*Test* からだけ書く）。通常のランでは既定値のまま |
| `TheScythe._currentDamage` | int | other-character | CanonicalVars の初期値に使う写し（Update* で更新）。今の値は DynamicVars.Damage として dynamic_vars に出る |
| `EnchantmentModel.Props` | SavedProperties? | base | 保存用の SavedProperties の一時置き場（ToSerializable/FromSerializable）。mod-scan の検出（.Props）は戦闘履歴の別の型の誤検出 |
| `EnchantmentModel._canonicalInstance` | EnchantmentModel | base | 正規インスタンスへの参照（ModelDb の定義。インスタンスごとの状態ではない） |
| `EnchantmentModel._card` | CardModel? | base | 他のモデルへの参照（所属）。state では入れ子・id で表す |
| `Slither._testEnergyCostOverride` | int | enchantment | テスト用の上書き（SetupForTests・Mock*・Set*Test* からだけ書く）。通常のランでは既定値のまま |
| `CardEnergyCost._card` | CardModel | entity | 他のモデルへの参照（所属） |
| `CombatState.RunState` | IRunState | entity | 他のモデルへの参照（所属）。state では入れ子・id で表す |
| `CombatState._allCards` | List<CardModel> | entity | 戦闘中の全カード（AddCard・RemoveCard）。各山と手札の和で、山は出ている |
| `CombatState._nextCreatureId` | uint | entity | 次に振る combat_id の番号（AttachCreature）。採番の内部 |
| `Creature.CombatState` | ICombatState? | entity | 他のモデルへの参照（所属）。state では入れ子・id で表す |
| `Creature.Monster` | MonsterModel? | entity | 他のモデルへの参照（所属）。state では入れ子・id で表す |
| `Creature.MonsterMaxHpBeforeModification` | int? | entity | 修飾前の最大 HP（SetUniqueMonsterHpValue）。修飾後の最大 HP は出ている |
| `Creature.Player` | Player? | entity | 他のモデルへの参照（所属）。state では入れ子・id で表す |
| `Creature._petOwner` | Player? | entity | 他のモデルへの参照（所属） |
| `Player.BaseOrbSlotCount` | int | entity | オーブの枠の初期値（キャラクターの定義）。ディフェクト専用 |
| `Player.Creature` | Creature | entity | 他のモデルへの参照（所属）。state では入れ子・id で表す |
| `Player.ExtraFields` | ExtraPlayerFields | entity | 他のモデルへの参照（所属） |
| `Player.IsActiveForHooks` | bool | entity | フックの対象かの印（ActivateHooks・DeactivateHooks。マルチプレイの切断など） |
| `Player.PlayerCombatState` | PlayerCombatState? | entity | 他のモデルへの参照（所属）。state では入れ子・id で表す |
| `Player._runState` | IRunState | entity | 他のモデルへの参照（所属）。state では入れ子・id で表す |
| `PlayerCombatState._player` | Player | entity | 他のモデルへの参照（所属） |
| `CeremonialBeast._beastCryState` | MoveState | monster | 行動の状態機械の状態オブジェクトへの参照（GenerateMoveStateMachine で作る）。今の状態・遷移は move_state に出る |
| `Chomper._screamFirst` | bool | monster | 行動の状態機械の初期状態を選ぶだけ（GenerateMoveStateMachine）。選ばれた初期状態は move_state.initial_state_id に出る |
| `CorpseSlug._starterMoveIdx` | int | monster | 行動の状態機械の初期状態を選ぶだけ（GenerateMoveStateMachine）。選ばれた初期状態は move_state.initial_state_id に出る |
| `DecimillipedeSegment._deadState` | MoveState | monster | 行動の状態機械の状態オブジェクトへの参照（GenerateMoveStateMachine で作る）。今の状態・遷移は move_state に出る |
| `DecimillipedeSegment._starterMoveIdx` | int | monster | 行動の状態機械の初期状態を選ぶだけ（GenerateMoveStateMachine）。選ばれた初期状態は move_state.initial_state_id に出る |
| `Inklet._middleInklet` | bool | monster | 行動の状態機械の初期状態を選ぶだけ（GenerateMoveStateMachine）。選ばれた初期状態は move_state.initial_state_id に出る |
| `KinFollower._index` | int | monster | 行動の状態機械の初期状態を選ぶだけ（GenerateMoveStateMachine）。選ばれた初期状態は move_state.initial_state_id に出る |
| `MonsterModel._canonicalInstance` | MonsterModel | base | 正規インスタンスへの参照（ModelDb の定義。インスタンスごとの状態ではない） |
| `MonsterModel._creature` | Creature? | base | 他のモデルへの参照（所属） |
| `MonsterModel._isPerformingMove` | bool | base | PerformMove の実行中だけ true の一時値（CreatureCmd が死亡時の除去を遅らせる）。state を読む安定点では false |
| `PunchConstruct._startingHpReduction` | int | monster | AfterAddedToRoom で HP を減らすだけ（イベントの戦闘 PunchOffEventEncounter が設定）。減らした後の HP は出ている |
| `PunchConstruct._startsWithFastPunch` | bool | monster | 行動の状態機械の初期状態を選ぶだけ（GenerateMoveStateMachine）。選ばれた初期状態は move_state.initial_state_id に出る |
| `Queen._burnBrightForMeState` | MoveState | monster | 行動の状態機械の状態オブジェクトへの参照（GenerateMoveStateMachine で作る）。今の状態・遷移は move_state に出る |
| `Queen._enragedState` | MoveState | monster | 行動の状態機械の状態オブジェクトへの参照（GenerateMoveStateMachine で作る）。今の状態・遷移は move_state に出る |
| `ScrollOfBiting._starterMoveIdx` | int | monster | 行動の状態機械の初期状態を選ぶだけ（GenerateMoveStateMachine）。選ばれた初期状態は move_state.initial_state_id に出る |
| `TerrorEel._terrorState` | MoveState | monster | 行動の状態機械の状態オブジェクトへの参照（GenerateMoveStateMachine で作る）。今の状態・遷移は move_state に出る |
| `TestSubject._deadState` | MoveState | monster | 行動の状態機械の状態オブジェクトへの参照（GenerateMoveStateMachine で作る）。今の状態・遷移は move_state に出る |
| `ToughEgg._afterHatchedState` | MonsterState? | monster | 行動の状態機械の状態オブジェクトへの参照（GenerateMoveStateMachine で作る）。今の状態・遷移は move_state に出る |
| `ToughEgg._isHatched` | bool | monster | AfterAddedToRoom で孵化済みなら HatchPower を付けずに孵化後の状態へ飛ばす（部屋に入る時だけ）。孵化したかは HatchPower の有無と move_state で分かる |
| `TwoTailedRat._starterMoveIndex` | int | monster | 行動の状態機械の初期状態を選ぶだけ（GenerateMoveStateMachine）。選ばれた初期状態は move_state.initial_state_id に出る |
| `WaterfallGiant._aboutToBlowState` | MoveState | monster | 行動の状態機械の状態オブジェクトへの参照（GenerateMoveStateMachine で作る）。今の状態・遷移は move_state に出る |
| `Wriggler._startStunned` | bool | monster | 行動の状態機械の初期状態を選ぶだけ（GenerateMoveStateMachine）。選ばれた初期状態は move_state.initial_state_id に出る |
| `OrbModel.HasBeenRemovedFromState` | bool | base | state から外れたモデルの印（RemoveFromState）。外れたモデルは state の一覧に出ない |
| `OrbModel._canonicalInstance` | OrbModel | base | 正規インスタンスへの参照（ModelDb の定義。インスタンスごとの状態ではない） |
| `PotionModel.HasBeenRemovedFromState` | bool | base | state から外れたモデルの印（RemoveFromState）。外れたモデルは state の一覧に出ない |
| `PotionModel._canonicalInstance` | PotionModel | base | 正規インスタンスへの参照（ModelDb の定義。インスタンスごとの状態ではない） |
| `PotionModel._dynamicVars` | DynamicVarSet? | base | ポーションの DynamicVars を実行時に書き換える箇所が無く（dynamic_var_mutation 0 件）、定義の値のまま。mod-scan の検出（.DynamicVars）はカードの誤検出 |
| `PotionModel._owner` | Player? | base | 他のモデルへの参照（所属）。state では入れ子・id で表す |
| `SneckoOil._testEnergyCostOverride` | int | shared | テスト用の上書き（SetupForTests・Mock*・Set*Test* からだけ書く）。通常のランでは既定値のまま |
| `ConfusedPower._testEnergyCostOverride` | int | ancient | テスト用の上書き（SetupForTests・Mock*・Set*Test* からだけ書く）。通常のランでは既定値のまま |
| `PowerModel._canonicalInstance` | PowerModel | base | 正規インスタンスへの参照（ModelDb の定義。インスタンスごとの状態ではない） |
| `PowerModel._owner` | Creature? | base | 他のモデルへの参照（所属）。state では入れ子・id で表す |
| `ShowOffPower._showOffInstance` | ShowOff? | other-character | 元のカード ShowOff への参照（AfterDeath で使う） |
| `SoulboundPower._isAddingSoul` | bool | other-character | AfterCardGeneratedForCombat の再入を防ぐ一時値。安定点では false |
| `ArchaicTooth._serializableAncientCard` | SerializableCard? | ancient | エンシェントが提示するとき（SetupForPlayer）に決め、取得時（AfterObtained）にだけ効く。取得後は表示（ホバー・タイトル）にしか使わない |
| `ArchaicTooth._serializableStarterCard` | SerializableCard? | ancient | エンシェントが提示するとき（SetupForPlayer）に決め、取得時（AfterObtained）にだけ効く。取得後は表示（ホバー・タイトル）にしか使わない |
| `DustyTome._ancientCard` | ModelId? | ancient | エンシェントが提示するとき（SetupForPlayer）に決め、取得時（AfterObtained）にだけ効く。取得後は表示（ホバー・タイトル）にしか使わない |
| `FakeSneckoEye._testEnergyCostOverride` | int | event-only | テスト用の上書き（SetupForTests・Mock*・Set*Test* からだけ書く）。通常のランでは既定値のまま |
| `LordsParasol.LastPurchaseEverythingTask` | Task? | ancient | 非同期処理の参照（AfterRoomEntered）。ゲームの状態ではない |
| `RelicModel.HasBeenRemovedFromState` | bool | base | state から外れたモデルの印（RemoveFromState）。外れたモデルは state の一覧に出ない |
| `RelicModel.StackCount` | int | base | IncrementStackCount の呼び出しが v0.113.1 のどこにも無く、常に 1 |
| `RelicModel._canonicalInstance` | RelicModel? | base | 正規インスタンスへの参照（ModelDb の定義。インスタンスごとの状態ではない） |
| `RelicModel._owner` | Player? | base | 他のモデルへの参照（所属）。state では入れ子・id で表す |
| `SeaGlass._characterId` | ModelId? | ancient | エンシェントが提示するとき（SetupForPlayer）に決め、取得時（AfterObtained）にだけ効く。取得後は表示（ホバー・タイトル）にしか使わない |
| `SneckoEye._testEnergyCostOverride` | int | ancient | テスト用の上書き（SetupForTests・Mock*・Set*Test* からだけ書く）。通常のランでは既定値のまま |
| `TouchOfOrobas._starterRelic` | ModelId? | ancient | エンシェントが提示するとき（SetupForPlayer）に決め、取得時（AfterObtained）にだけ効く。取得後は表示（ホバー・タイトル）にしか使わない |
| `TouchOfOrobas._upgradedRelic` | ModelId? | ancient | エンシェントが提示するとき（SetupForPlayer）に決め、取得時（AfterObtained）にだけ効く。取得後は表示（ホバー・タイトル）にしか使わない |
| `Wuppy[relic]._combatWuppy` | Creature? | event-only | 召喚したペットのクリーチャーへの参照。ペットは pets に出る |
| `Wuppy[relic]._currentHpSet` | bool | event-only | CurrentHp の初期化済みの印（AfterObtained） |

