# パワー・レリックの内部状態の契約試験

本番の `McpMod.InternalState.cs`、`McpMod.PowerInternalState.cs`、
`McpMod.PowerInternalStateFormatting.cs`、`McpMod.RelicCombatState.cs`、
`McpMod.Warnings.cs`、`McpMod.CardIdentity.cs` を直接リンクする。ゲーム・Godot・追加パッケージは不要。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec \
  /opt/homebrew/opt/dotnet@9/libexec/dotnet run \
  --project tests/PowerRelicInternalStateContract -c Release \
  -p:BuildInParallel=false -p:UseSharedCompilation=false
```

公開 getter と private Data／レリックフィールド・プロパティの読取、false・0・小数・負数、
両方向、対象 player 別の盗難量と文字列 ID、空配列、snapshot の独立、
ゲーム側の更新・リセット後の再読取を確認する。
拘束の鎖（`CHAINS_OF_BINDING_POWER`）は private Data の `bound_card_played` の false／true／リセット、
snapshot の独立と非破壊読取、非表示時の省略、null／異型 Data・Data 欠落・
フィールド欠落／型変更時の null・理由・warnings と他項目の保持を確認する。
JSON の bool／null と理由、Markdown の yes／no／null 表示も確認する。
儀式（`RITUAL_POWER`）の `was_just_applied_by_enemy`（private `_wasJustAppliedByEnemy`、schema 6・`cat12079801/sts2#1018`）は
false／true を実値で、非表示の行にも出し、読取でフィールドを変えず、他のパワーに付けず、欠落フィールドは null・理由・warnings。
null Data・型不一致・フィールド欠落・宣言型変更・getter失敗・盗難対象異常・
座標配列不整合・現在地欠落は null・理由・warnings と他項目の保持を確認する。
ペン先・冥銭の公開 getter の正常値と例外、毛皮のコートの手書き private プロパティ
（コンパイラの backing field がなくても成功）と各 getter の例外・欠落・型変更も検査する。
スカーフの0・5・6・7と表示カウンターが消えた後の実数、リセット・snapshot の独立、
フィールド欠落／型違い時の null・理由・warnings を検査する。
ジャグリングは待ちなし／予約／解除、デッキ札と同 ID の生成札を本番の uid registry に通し、
他経路の uid と一致することを確認する。Data の null・異型・フィールド欠落／型変更で
readable=false と null・理由になり、正常な待ちなしの readable=true と区別できることを
wire と Markdown でも確認する。
readable=true は待ちなし・予約ありのどちらでも Markdown で省略し、JSON で保持すること、
readable=false は Markdown に表示することも確認する。
まるくなる（`CURL_UP_POWER`）の `played_card_uid` / `played_card_readable` も同じ読取関数で、
追跡なし（readable=true の null と Markdown の `none`）／デッキ札と生成札の uid／解除後の再読取と
snapshot の独立、Data の null・異型・Data 欠落・フィールド欠落／型変更での readable=false と
null・理由・warnings、Data を初期化・書換えしないこと、非表示時の省略、wire での string／null を確認する。
泥棒（`THIEVERY_POWER`）は `stolen_gold`（`DynamicVars.Gold.IntValue`）の 0 と amount と異なる値、
`target_player_id`（`Target.Player.NetId` の文字列）を対象別の複数行で確認し、
強奪（`HEIST_POWER`）は `target_player_id` だけを足して amount を変えないことを確認する。
どちらも HUD 非表示（他の player が対象）の行でも付けること、Target 欠落・player でない Target・
Gold の読取失敗で null・理由・warnings と他項目の保持、wire の int / string / null を確認する。
活力（`VIGOR_POWER`）の `vigor_attack` は、新規の付与（未結合の readable=true・has_command=false・
attacker_is_owner=null・amount_when_started=0）、所有者の攻撃への結合（true / true / 6）、他の攻撃者・
Attacker が null の結合（false）、未結合で残る開始時の量の実値、snapshot の独立と非破壊読取（参照・量を変えない）、
HUD 非表示の行にも付くこと、未結合では Owner を読まないことを確認する。Data の null・異型・Data 欠落で
3 項目とも null・`unavailable_reasons.vigor_attack.<項目>`・warnings、コマンドのフィールドの欠落／型変更で
has_command・attacker_is_owner が null（開始時の量は保持）、開始時の量の型変更で量だけ null（未結合は保持）、
Attacker・Owner の getter 失敗で attacker_is_owner だけ null になり、どれも readable=false になることを確認する。
wire の bool / null / int と行の理由、Markdown の `none`・`readable: no`・`null (理由)` も確認する。
呪術（`HEX_POWER`）の行の直下の `applier_combat_id` / `applier_readable` は、許可リストの `internal_state.applier_combat_id`
（ここでは手で置いた値。読み取りそのものは `StateAllowlistContract`）を `AttachHexApplierCompat` が写したものとして、
付与元の `CombatId`（7 と実在する 0）、付与元なし（readable=true の null・理由なし・Markdown の `none`）、
読取失敗（readable=false・null・同じ理由で warnings を足さない・Markdown の `null (理由)` と `applier readable: no`）、
許可リストの値が無いとき（readable=false・null・理由・warnings）、HUD 非表示の行にも付くこと、他のパワーには付かないこと、
`AttachPowerInternalState` が付与元を読まないこと、wire の uint 0・null と readable の bool を確認する。
札の効果の入力（`cat12079801/sts2#1019`、schema 7）: 爆弾（`TheBombPower`）の `damage`（`DynamicVars.Damage.BaseValue`。
正準の 40・`SetDamage` 後の値・0、Instanced の行ごとの値、非表示の行にも付く、snapshot の独立、変数の欠落・読取失敗は null・理由・warnings で 0 にしない）、
インフェルノ・深紅の衣の `self_damage`（`DynamicVars["SelfDamage"].BaseValue`。0 と `IncrementSelfDamage` 後の値、wire の decimal、失敗）、
威風堂々（Panache）の `cards_left`（`DynamicVars["CardsLeft"].IntValue`）と `already_applied`（private Data の bool。新規の 5・false、
適用後の値、Instanced の独立、非表示の行、Data が null なら初期化せずに null・理由・warnings で `cards_left` は保持）、
Markdown の `damage:` / `self damage:` / `cards left:` / `already applied: yes`、他のパワーにキーが無いこと、
private `PowerModel._dynamicVars` が null の 4 種で getter が作らず（初期化しない）null・理由・warnings になること（威風堂々の `already_applied` は保持）を検査する。
弱化の入力（`cat12079801/sts2#1031`、schema 8）: `DampenPower` の private Data の `caster_combat_ids`
（caster 0・1・2 体、昇順、CombatId 0 も実値、snapshot の独立、非表示の行にも付く、`CombatId` の無い caster は項目の失敗で ID を補わない）と
`downgraded_cards`（registry の uid・`card_id`・元の段階、辞書の挿入順でなく uid 順、registry に無い札は行ごとに
`uid=null`・`uid_known=false`・理由・warnings で項目は失敗にせず、読取で uid を採番しない）、Markdown、wire、
Data が null・フィールドの欠落・型違いは null・理由・warnings で Data を初期化しないこと、読取で Data・中身・`_dynamicVars`・`Applier` に触れないことを検査する。
破壊の入力（`cat12079801/sts2#1034`、schema 9）: 全パワーの `type_for_current_amount`（`PowerModel.TypeForCurrentAmount`）と
`is_temporary_power`（`ITemporaryPower` の実装）。弱体・脆弱（Debuff）と筋力（Buff）、負の筋力で `type` が `Buff` のまま
`Debuff` になること（Markdown に出る・snapshot の独立）、一時パワー（Debuff 型の `ManglePower`、Buff 型の `FlexPotionPower`・
`SpeedPotionPower`）が非表示の行でも true、`type` と同じ値・false は Markdown に出さないこと、wire の文字列と bool、
getter の失敗は `type` で補わず null・理由・warnings で他の項目を保持すること、読取が getter 1 回だけで Data・`_dynamicVars`・
`Applier` に触れないこと、`BuildPowersState` が非表示・量 0 でも現在の型が Debuff（または読めない）の行を落とさないこと（ソース文字列）を検査する。ダブルは `GetTypeForAmount` を計算せず、ゲームが返す値を fixture が与える。
パワーの量の入力（`cat12079801/sts2#1020`、schema 12）: 全パワーの `dynamic_vars`（private `PowerModel._dynamicVars` の `{変数名: BaseValue}`）。
変数の無いパワーは理由・warnings の無い `{}`（Markdown に出さない、wire は空 object）、2 変数（`TankPower`）の名前・小数の値・集合の順、
wire で変数名を snake_case にしないこと、snapshot の独立と 0 の実値、非表示の行にも付くこと、`StringVar` を出さないこと、
型ごとの既存項目（`self_damage`・`damage`・`block_base_value`）が残り同じ値になること、1 変数の読取失敗はその名前だけ null で
`unavailable_reasons["dynamic_vars.<名前>"]`・warnings・他の変数を保持（Markdown・wire も）、`_dynamicVars` が null なら
可視・非表示の行とも getter で作らず項目ごと null・理由・warnings で他の項目を保持（`{}` にしない。爆弾の `damage` も同じく失敗）、
列挙の失敗も項目ごとの失敗になることを検査する。
パワーの private Data・フィールド（`cat12079801/sts2#1020`、schema 12）: 血の沸騰（`played_cards` / `pending_strength`）・カラミティ・絞殺
（`amounts_for_played_cards` / `amount`）の札→数の辞書を、空は `[]`（非表示の行にも付く・Markdown は `none`）、registry の uid で uid 順・0 も実値、
registry に無い札は `uid_known=false`・理由・warnings で uid を採番しないこと、Data を変えず snapshot が独立、wire の int、Data が null なら初期化せず
null・理由・warnings で検査する。ヘル・レイザーの `infinite_auto_plays_this_turn` と `auto_playing_cards`（`showedCapReachedMessage` は出さない）、
オートメーションの `cards_left`（`dynamic_vars.BaseCards` は 10 のまま）、闇の抱擁の `ethereal_count`、硬い殻の `damage_received_this_turn`（decimal）、
適応・再結合・幻影の `is_reviving` の読取・非表示の行・Markdown・Data が null の失敗、巨大化の `has_command_to_modify`（未結合の false・結合の true・失敗）、
幻影の `follow_up_state_id`（記録なしは readable=true の null と Markdown の `none`、getter の失敗は readable=false）、
ネメシス・怠惰・軟化・希望の道標の private フィールドの 0 / false と値、盗む（`SWIPE_POWER`）の `stolen_card`（なしは readable=true の null、
registry の uid、無い札は採番しない）、他のパワーにキーが無いことも確かめる。
レリックの手番の数と保存カウンタ（schema 12）: 苦無・手裏剣・扇子・鎖鎌の `attacks_played_this_turn`、レターオープナーの `skills_played_this_turn`、
懐中時計の `cards_played_this_turn` が private の数そのもの（表示値の剰余でない）で 0 も実値、snapshot の独立、`run_state` に出ないこと、
オリハルコン（偽物も）の `should_trigger` の false / true、フィールド欠落の null・理由・warnings。`run_state` は 20 種
（ヌンチャク・音叉・IronClub・HappyFlower・偽 HappyFlower・Pendulum・PollinousCore・Girya・LastingCandy・BookOfFiveRings・VenerableTeaSet・偽物・
PumpkinCandle・SwordOfStone・WingedBoots・SilverCrucible〔2 項目〕・FishingRod・ToyBox・WongosMysteryTicket・PaelsWing・LavaRock）の
初期値と値の読取、読取でフィールドを変えないこと、`combat_state` に出ないこと、Markdown、型違いのフィールドの null・理由・warnings、他のレリックに付かないことを検査する。
レリックの `vars` は private `RelicModel._dynamicVars` から名前・BaseValue・StringVar を除くこと、空の `{}`、1 変数の失敗はその名前だけ null で
`unavailable_reasons["vars.<名前>"]`・warnings・他の変数を保持、集合が null なら getter で作らず `vars: null`・理由・warnings で行を保つこと、
Markdown は失敗だけを表示することを検査する。`AttachRelicSection` は対象のレリックだけに `run_state` / `combat_state` を付け、対象外ではキーを付けず、
組み立てが例外を投げたらキーを null・`unavailable_reasons.<key>`・warnings にして行を保つこと（Markdown の `(run state: null (理由))`）を検査する。state builder の配線（`vars` と `run_state` が戦闘の条件の外、旧 `ReadRelicDynamicVarValues` が無い）はソース文字列で確かめる。
内部データを初期化・書換えしないこと、HUD非表示／可視性不明の追加状態を省略すること（破壊の入力と `dynamic_vars` は除く）、
JSON の本番設定でも null と理由が残ること、Markdown の空・通常の skip=false・
空の盗難配列の省略と、値／失敗理由の短い表示を確認する。

巨大な state builder はソース文字列で、通常行・説明失敗時の最小行、player・敵・ペット、
マルチの共通経路、戦闘中だけのレリック出力、Markdown の配線、schema 12 を検査する。
フィールドと getter はゲーム境界のダブル。効果の適用やリセットを MOD 側では模倣しない。
毛皮のコートは v0.113.0 の戦闘フックどおり、座標を比較し act の条件を足さない。
実ゲームに対する 0警告0エラーのビルドを別途実行し、ゲーム起動・mods への配置はしない。

実機では調整役が profile2 の検証用新規ランで、拘束の鎖の使用前 false →
所有者の Bound 札の使用後 true → 所有者参加の手番終了後 false を前後の state で照合する。
自動プレイも true、IsDupe や別所有者の札はフラグを変えないことを
`ChainsOfBindingPower.BeforeCardPlayed` と照合する。`AfterCardDrawnEarly` で Bound 付与前に
自動使用された札はフラグを立てず、その後の付与は履歴件数に入る境界も対象とする
（`ChainsOfBindingPower.AfterCardDrawn`）。付与件数の確認手順は CombatHistoryContract を参照。
まるくなる（`CURL_UP_POWER`）は、始祖虫（`LouseProgenitor.AfterAddedToRoom` が付与）にアタック札を撃つ前後と、
プレイ途中の選択で止まる局面の state で、`played_card_uid` が none → 撃った札の uid（解決中）→
行ごと消える（発動でパワーが外れる）と変わることを照合する（`CurlUpPower.AfterDamageReceived` / `AfterCardPlayed`）。
呪術（`HEX_POWER`）は、スペクトルナイト（`SpectralKnight.HexMove`）の HEX の後の state で、
player の呪術の行の `applier_combat_id` がそのナイトの `enemies[].combat_id` と一致すること、
騎士が複数いる局面で別の騎士を倒しても呪術と付与元が残り、付与元のナイトを倒すと呪術の行と札の `HEXED` が消えることを照合する
（`HexPower.AfterDeath` / `AfterRemoved`）。呪術の無い戦闘では行ごと無いことも確かめる。
