# API Quick Reference

HTTP API on `localhost:15526`. No authentication.

ローカルの非ブラウザクライアント向け。`Origin` がある要求は値を問わず 403、
Origin がなくても `Sec-Fetch-Site` / `Sec-Fetch-Mode` / `Sec-Fetch-Dest` のいずれか、
または `Referer` があれば空値も含めて 403（`<img>`・no-cors fetch の GET も対象）。
Host は `127.0.0.1:<port>` / `localhost:<port>` のみ許可（設定ポート以外は 403）。
macOS（マネージド実装）で確認: 別名・末尾ドット付き Host は HttpListener が先に 404、
IPv6 や HTTP/1.1 の Host 未指定は 400（HTML 応答）を返す場合がある。
リスナーを通過するポート省略・別ポート・`127.1:<port>` などは MOD の 403（JSON）になる。
POST は `Content-Type: application/json`（charset などのパラメーター可）が必須で、
未指定・他の型は 415。CORS 許可ヘッダーは返さず、OPTIONS は 405（上記ブラウザ用ヘッダーがあれば 403）。
Origin の例外設定はない。[HTTP 契約](raw-full.md#http-要求の制限) を参照。

- `GET /` — health check; returns `{ message, status, version, commit, schema, schema_version, scenario_capabilities }`
- `GET /api/v1/singleplayer` — read game state
- `POST /api/v1/singleplayer` — perform action
- `GET /api/v1/multiplayer` — read multiplayer state
- `POST /api/v1/multiplayer` — perform multiplayer action
- `GET /api/v1/profile` — read current profile progress
- `GET /api/v1/compendium` — read Compendium-shaped profile progress
- `GET /api/v1/wiki` — fuzzy-search discovered card/relic wiki entries
- `GET /api/v1/profiles` — list profile slots
- `POST /api/v1/profiles` — switch or delete profile slots
- `GET /api/v1/record` — combat recorder status (see [recording.md](recording.md))
- `GET /api/v1/record/inspect?file=<.mcr>` — decode a combat replay with the game's reader (verification only; contains hidden checksums)
- `POST /api/v1/singleplayer {"action": "set_recording", "enabled": true|false}` — turn the combat recorder on/off (works without a run)

Singleplayer and multiplayer endpoints are mutually exclusive (HTTP 409 if mismatched).

## GET — Query Parameters

| Parameter | Values                                        | Default | Description                                                     |
|-----------|-----------------------------------------------|---------|-----------------------------------------------------------------|
| `format`  | `json`, `markdown`                            | `json`  | Response format                                                 |
| `pretty`  | `1`, `true`, `yes` (or present with no value) | off     | Indent the JSON. Off by default — compact output is ~32% smaller |

`pretty` applies to POST requests as well (e.g. `POST /api/v1/singleplayer?pretty=1`) and to error
responses. It has no effect on `format=markdown`.

## GET — State Types

Every JSON response includes:
- `state_type` — which screen the game is on (see below)
- `mod_version` — mod version string, e.g. `0.4.0`
- `mod_commit` — git commit the mod was built from, e.g. `2de67cd` (`2de67cd-dirty` for a build from an edited tree), `null` when the build could not determine one
- `game_version`, `game_commit` — running game's version and commit from `release_info.json` (e.g. `v0.113.0`, `16ac0fe3`), `null` if unavailable. Present on menu, singleplayer and multiplayer states; dump consumers should preserve them in `meta.json`.
- `schema` — the contract version as semantic versioning, currently `"1.5.0"` (`1.0.0` is exactly the contract of `schema_version` 12; `1.1.0` adds GremlinMerc / KinPriest `combat_state`; `1.2.0` adds `internal_state` on card, power, relic, player and battle rows; `1.3.0` adds `applier_combat_id` / `target_combat_id` to every power row's `internal_state`; `1.4.0` adds `battle` to every `*_select` state shown while a fight is in progress; `1.5.0` adds the inputs of four colorless cards: `combat_history.card_plays_finished_this_combat`, `other_character_attack_generation_pool`, `transformation_options` and `choice_source`). **MAJOR**: a backward-incompatible change (an existing field's meaning, type or shape changes, a field, action, enum value or `state_type` is removed or renamed, the structure changes); a client requires only the same MAJOR. **MINOR**: a backward-compatible addition (new fields, actions, optional parameters, `state_type`s, new values of an existing enum-like field such as `resolving_reasons` or `can_use_now_reason`); detect added features by field presence and the existing `unavailable_reasons` / `warnings` conventions, not by comparing versions, and treat an unknown value as unobserved (stop on the safe side), never as a known one. Partial contracts with their own version (`generation_pool.filter_version`, `scenario_capabilities` values, the recorder's `sts2-mod-record/N`) are judged by that version; `schema` still moves by the ordinary rules. The canonical rules are in [raw-full.md](raw-full.md#common-top-level-fields). **PATCH**: a fix that does not change the contract (a value that was wrong per the documented meaning becomes right, docs / formatting fixes). The Markdown header shows `mod 0.4.0+<commit> · schema 1.5.0 (schema_version 12)`.
- `schema_version` — **deprecated** integer version, frozen at `12`: it no longer changes and is removed in the next MAJOR (`2.0.0`) once sts2 reads `schema`. Its history: it was bumped when an existing field or action changed meaning or was removed, and also for `12`, `11`, `10`, `9`, `8`, `7` and `6`, so a client could require each additive contract as a whole. `12` is the power / relic amount input contract (`cat12079801/sts2#1020`): every status row carries `dynamic_vars`, the power's own `DynamicVars` as `{ name: BaseValue }`, powers whose state lives in private Data carry per-type keys, relics gain per-turn counts in `combat_state` and saved counters in `run_state`, so a client never reads amounts from the description or infers counters; `relics[].vars` is `null` with a reason when unreadable instead of being omitted. `11` is the potion can-use-now contract (`cat12079801/sts2#1012`, additive): every held `player.potions[]` row carries `can_use_now` / `valid_target_entity_ids` / `can_use_now_reason`, `use_potion` takes `expected_potion_id`, and `scenario_capabilities.potion_availability` is `1`. `10` is the verification-capture contract (`cat12079801/sts2#1037`): `scenario_capabilities` on every state, `scenario_setup_guard` on `?passive=1&scenario_setup_guard=1` and in the `dev_console` body, `expected_uid` on `combat_select_card` / `select_card`, and `hand_select` rows by holder position with `uid` / `enabled` / `selected_cards[].uid` / `min_select` / `max_select` / `peeking` — `hand_select.cards[].index` and `selected_cards` change meaning (see below). `6` to `9` are additive and existing fields keep their meaning. `9` is the Rend input contract (`cat12079801/sts2#1034`: every status row carries `type_for_current_amount`, `PowerModel.TypeForCurrentAmount`, so a negative Strength reads `Debuff` while `type` stays `Buff`, and `is_temporary_power`, whether the power implements `ITemporaryPower`); `8` is the Dampen input contract (`cat12079801/sts2#1031`: `DAMPEN_POWER` status rows carry `caster_combat_ids` and `downgraded_cards`); `7` is the card-effect input contract (`cat12079801/sts2#1019`: the single-turn Retain / Sly flags, replay counts and Mad Science's `tinker_time_rider` on every combat card row and `ready` preview, enchantment `dynamic_vars`, The Bomb / Inferno / Crimson Mantle / Panache status values, and `combat_instance` on in-combat selection rows); `6` is the enemy-move input contract (`move_state` additions, `slot_name`, `battle.enemy_move_context`, more `combat_state` enemies, Ritual's `was_just_applied_by_enemy`; `cat12079801/sts2#1018`). `5` makes every combat card row (hand and the four piles) always carry `upgrade_preview` with a `status` (`ready` / `not_upgradable` / `unavailable`) and machine-readable upgraded values; it used to be a hand-only display object present only while an upgrade card was in reach. `4` makes grid `card_select.cards` and `select_card` indices cover all candidates in display order and removes `selected_outside_grid`. `3` removed `map.next_encounters`, which exposed future rooms before the player could see them; `map.encounters_seen` reports only travelled encounters. `2` changed enemy targeting: `battle.enemies` can now contain a dead enemy the game keeps in the fight (`alive: false`), so it is no longer the same thing as "the enemies you can target".
- `scenario_capabilities` — `{ setup_guard, selection_uid, potion_availability }`: which verification contracts this build implements, as contract versions (`0` = not implemented; this build: `1`, `1`, `1`). A build constant, also on `GET /`. Gate the guarded `dev_console`, `expected_uid` and `expected_potion_id` on it: an older build ignores unknown parameters. Schema 10 (`potion_availability`: schema 11).
- `scenario_setup_guard` — only on `GET ?passive=1&scenario_setup_guard=1` (verification runs): `{ version: 1, screen, room, combat_starting, combat_in_progress, run_over, allowed, reason }`. `room` is `RunState.CurrentRoom`'s runtime type name, the flags are `CombatManager.IsStarting` / `IsInProgress` and `RunState.IsGameOver`. `allowed` is true only for `map` in a `MapRoom`, Neow's `event` in its `EventRoom` or `rest_site` in a `RestSiteRoom`, with the run going, no fight starting or in progress and nothing resolving; an unreadable value is `null` and not allowed (`reason: "unreadable"`, `unavailable_reasons`). Schema 10. [Reasons and sources](raw-full.md#戦闘外の準備の判定scenario_setup_guardschema-10).
- `is_resolving` / `resolving_reasons` — 解決途中の値を返しているか（単独プレイ）。true の間の手札・エナジー・HP は途中の値なので、計画には false の snapshot を使う。理由は `action_queue_not_empty` / `player_actions_disabled` / `combat_starting`（戦闘の部屋に入ってから戦闘が始まるまで。`battle` の無い `monster` などの state）/ `combat_ending`（戦闘中に終わりかけているときと、終わってから報酬画面などが出るまでの `battle` の無い state）/ `player_turn_starting`（戦闘の画面で、手番の初めのドローと手番開始時のフックが終わっていない。`PlayerCombatState.Phase` が `Play` より前か、`ActionQueueSynchronizer.CombatState` が `NotPlayPhase`）/ `card_still_being_played` / `reward_selection_resolving` / `map_traveling`（`map` のまま移動の演出をしている。`NMapScreen.IsTraveling` か、地図の投票・移動の action）/ `map_closing`（`map` を閉じる演出の間。`IsOpen` が false で画面が見えている）/ `room_changing`（ラン中の `unknown` で `RunManager.IsChangingRooms`）/ `run_loading`（ラン中の `unknown` で、部屋がまだ無い。`continue` の読み込みなど）。入力待ちの画面（`hand_select` / `card_select` / `rewards` / `map` など）では false（`map` の `map_traveling` / `map_closing` を除く）。版は上げていない（schema 4 のときの追加）。[条件と出典](raw-full.md#解決中の判定is_resolving--resolving_reasons) を参照。
- `warnings` — **present only when something went wrong.** A list of strings, one per part of the state that could not be built, as `"<field or section>: <ExceptionType>: <message>"` (e.g. `"map.encounters_seen: NullReferenceException: ..."`). The state builders read the live game through reflection and swallow whatever throws, so a member the game renamed simply stops appearing; this list says which one. **A warning naming a field means that field may be absent, partial or stale below** — the rest of the payload is still valid. Duplicates are collapsed, and the list stops at 50 entries plus a final `"... N more warnings suppressed"`. Markdown renders the count and the first three under the `STILL RESOLVING` block. Not every swallowed exception is reported: a few catches are expected to fire during normal play (node-tree churn, probing alternative property names) and are deliberately silent.
- `run` — `{ act, floor, ascension, player_count, card_multiplayer_constraint }` (absent for `menu`)
- `player` — full player state: character, HP, gold, relics, potions, `max_potion_slots` (belt capacity, grows with relics), and during combat: energy, hand, piles, orbs (absent for `menu`)
  - `potions[]` — one row per held slot (`slot` is the fixed belt slot; empty slots have no row). Every row carries (schema 11, `cat12079801/sts2#1012`):
    `can_use_now` — whether the belt's use button would be enabled for it now **and** it has a legal destination, as the game decides it (`NPotionPopup.CanEnableUseButton`, the holder, `Player.CanUseOrRemovePotions`, `RunManager.IsPotionUseLocked`, then the targeting rules);
    `valid_target_entity_ids` — the destinations `use_potion` may name: enemy `entity_id`s (alive, targetable, hittable — the same ids as `battle.enemies`), `"player:<player_id>"` for a player (solo AnyPlayer / Self potions: your own), `[]` when no creature is named (all-enemy potions, Foul Potion at the merchant);
    `can_use_now_reason` — `null` when usable, else the first ban (`queued`, `owner_dead`, `game_over`, `owner_locked`, `room_locked`, `holder_not_usable`, `holder_disabled`, `custom_check_failed`, `automatic`, `outside_combat`, `not_player_turn`, `card_selection`, `actions_disabled`, `targeting_blocked`, `no_valid_target`).
    When a needed read fails or the form is unknown: `can_use_now: null`, `valid_target_entity_ids: null`, `can_use_now_reason: "unobserved:<stage>"`, plus `unavailable_reasons.can_use_now` and a `warnings` entry `player.potions.<slot>.can_use_now`. **Never treat null as false or [].** `can_use_in_combat` / `target_type` keep their meaning (usage class / current target type); a failed read of one of them is `null` with `unavailable_reasons` instead of dropping the player. Markdown appends `| Use now: yes → ids` / `no (reason)` / `unobserved (reason)` to each potion line. [Contract and sources](raw-full.md#ポーションをいま使えるかplayerpotionsschema-11).
  - `character_id` / `player_id` — 非ローカライズのモデルID / `Player.NetId` の文字列。追加項目。
  - `generation_pool` — 同じ snapshot のローカル Player 自身の解放済み戦闘生成候補。`{ character_id, player_id, players, multiplayer_constraint, known, cards: [{id,type}], game_assembly_sha256, filter_version: 2, filter_source }`。単独・マルチともに出す。取得失敗は `known:false, cards:[], reason` と warnings。`GetUnlockedCards(Player.UnlockState, run.CardMultiplayerConstraint)` に本体の `CardModel.IsAllowedInRun`（`CardFactory.FilterForRun`。人数と Blitz）・`FilterForCombat`・Attack/Skill/Power の filter を適用する（オロビックの酸液の抽選前の候補）。版 2 は版 1（人数の条件だけ・`FilterForPlayerCount`）から候補の条件の意味を変えたので、`filter_version` と `filter_source` の完全一致で判定する。抽選/RNG/カード作成は行わない。SHA256はロードした本体DLLの版、filter出典の正確な文字列と公開範囲の注意は [raw-full.md](raw-full.md#解放済みの戦闘生成-pool追加schema-3) を参照。図鑑の discovered_cards と同一視しない。図鑑は未発見札を NotSeen にし、ラン固有の一覧を直接見せない。年代記の解放画面は解放対象札を表示する。Markdown は件数・人数か unknown と理由だけ。schema 3 での追加項目（その追加時に schema 変更なし）で、旧 MOD は欄を省略。
  - `unlocked_pools` — 戦闘中だけ、単独・マルチのローカル Player に `{ colorless_cards: [ID], character_cards: [ID], potions: { character: [ID], shared: [ID] } }` を追加。カードは何でも屋・ジャックポットと同じ `GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)`、ポーションは `PotionFactory.GetPotionOptions` と同じ各 pool の `GetUnlockedPotions(player.UnlockState)` を使い、ID (`Id.Entry`) の昇順で返す。solo ではゲームの制約で multiplayer 専用札を除外。解放済み pool のため、自己除外・0 コスト・戦闘生成可否など札の後段の条件は適用しない。最終抽選候補や `generation_pool` と同一視しない。各配列の取得失敗はその配列だけ `null` とし `warnings` に該当パスを記録する（空の `[]` と区別）。戦闘・Player の参照ごとにキャッシュし、失敗の警告も毎回返す。Markdown は件数（取得不能は `unknown`）だけの 1 行。戦闘外・旧 MOD では省略し、版は上げていない（schema 3 のときの追加）。[契約・ゲーム側の出典](raw-full.md#戦闘中の解放済み-pool追加schema-3) を参照。
  - `colorless_generation_pool` — 戦闘中だけ、何でも屋の抽選前の候補（無色の解放済み pool から `JackOfAllTrades` を除き、`IsAllowedInRun` → `FilterForCombat`。型では絞らない）。形・失敗時の契約は `generation_pool` と同じで `filter_version: 2`。[契約・出典](raw-full.md#何でも屋の生成候補playercolorless_generation_pool追加filter_version-2)。
  - `other_character_attack_generation_pool` — 戦闘中だけ、スプラッシュ（`Splash.OnPlay`）の抽選前の候補。`UnlockState.CharacterCardPools` から（2 つ以上なら）自キャラの pool を除いた各 pool の解放済みの札のうち Attack を、`IsAllowedInRun` → `FilterForCombat` に通した ID の昇順。抽選・強化・結果は出さない。形・失敗時の契約は `generation_pool` と同じで `filter_version: 2`（schema 1.5.0）。[契約・出典](raw-full.md#スプラッシュの生成候補playerother_character_attack_generation_poolschema-150filter_version-2)。
  - `transformation_options` — エントロピーの戦闘だけ（`EntropyPower` を持つか、戦闘のパイルに `Entropy` の札がある）、戦闘のパイルの札ごとの変化の候補 `{ known, source, cards: [{ id, uids, options, reason? }] }`。各札に本体の `CardFactory.GetDefaultTransformationOptions(original, isInCombat)` を呼んだ結果の ID（`CardCmd.TransformToRandom` が 1 枚を抽選する集合）。同じ ID・同じ候補はまとめ、uid は序数順（山札の順を出さない）。本体が候補なしで例外になる札は `options: null` と理由（正常な状態なので warning にしない）。抽選の結果は出さない（schema 1.5.0）。[契約・出典](raw-full.md#エントロピーの変化の候補playertransformation_optionsschema-150)。
  - `relics[]` — owned relic entries include boolean `used_up` (`RelicModel.IsUsedUp`) and `melted` (`RelicModel.IsMelted`), including the multiplayer local player. Markdown adds `(used)` / `(melted)` when true in singleplayer outside combat, and during combat. Multiplayer outside combat does not list relics in Markdown. Feature-detect these additive fields; they were added under schema 3 without a bump. Older builds omit them. Melted wax is a separate state, not a synonym for consumed or every disabled relic.
    - `vars` — 戦闘中・戦闘外の全所有レリックに、`DynamicVars` の名前 → 現在の数値 `BaseValue` を出す。例: 骨付き肉 `{"HpThreshold":50,"Heal":12}`。小数も丸めず保持し、表示文字列の `StringVar` は除外。数値なしは `{}`。private `_dynamicVars` を読み（getter で作らない）、取得失敗は `vars: null` とレリック行の `unavailable_reasons.vars`・`warnings` の `player.relics.<ID>.vars`、1 変数の失敗はその名前だけ null と `unavailable_reasons["vars.<名前>"]`（schema 12。以前は省略）。null は unknown。Markdown は失敗だけ表示（schema 3 のときの追加）。[契約・出典](raw-full.md#レリックの数値変数追加schema-3) を参照。
    - `combat_state` — 戦闘中の対象レリックだけに追加するクラス別の bool / int / decimal。例: 百年パズルの `used_this_combat`、虹の指輪の種別別プレイ枚数・発動数。false / 0 も出す。戦闘外・対象外・旧 MOD では省略。既存項目の取得失敗はキーを省略して `warnings` に記録する。ペン先 `attacks_played`・冥銭 `cards_exhausted` / `ethereal_count`・毛皮のコート `affects_current_room`・鮮やかなスカーフ `cards_played_this_turn` の取得失敗は該当キーを `null` にし、`combat_state.unavailable_reasons.<key>` に理由、トップレベルに `warnings` を出し、他の値を保持する。欠落・null は unknown と扱う。Markdown も短い注記を追加。追加契約なので版は上げていない（schema 4 のときの追加）。schema 12 で苦無・手裏剣・扇子・鎖鎌の `attacks_played_this_turn`、レターオープナーの `skills_played_this_turn`、懐中時計の `cards_played_this_turn`、オリハルコン（偽物も）の `should_trigger` を足した（失敗は null と理由。`counter` は表示値なので数はこちらを読む）。[全項目・戻る時機・出典](relic-combat-state.md) を参照。
    - `run_state` — 戦闘をまたいで残る保存カウンタ（schema 12、`cat12079801/sts2#1020`）。戦闘中・戦闘外とも、対象のレリックだけに出す: ヌンチャク `attacks_played`、音叉 `skills_played`、`IronClub` `cards_played`、`HappyFlower`（偽物も）・`Pendulum`・`PollinousCore` `turns_seen`、`Girya` `times_lifted`、`LastingCandy` `combat_rewards_seen`、`BookOfFiveRings` `cards_added`、`VenerableTeaSet`（偽物も）`gain_energy_in_next_combat`、`PumpkinCandle` `kindle_count`、`SwordOfStone` `elites_defeated`、`WingedBoots` `times_used`、`SilverCrucible` `times_used` / `treasure_rooms_entered`、`FishingRod`・`ToyBox` `combats_seen`、`WongosMysteryTicket` `combats_finished`、`PaelsWing` `rewards_sacrificed`、`LavaRock` `has_triggered`。読取失敗はキーを null にし `run_state.unavailable_reasons.<key>`・`warnings` の `player.relics.<ID>.run_state.<key>`。[意味と出典](relic-combat-state.md#保存カウンタrun_stateschema-12) を参照。
  - `internal_state`（schema 1.2.0）— 棚卸し（[`state-inventory.md`](state-inventory.md)）で public と分類した内部状態を、分類表から生成した許可リスト（`McpMod.StateAllowlist.Generated.cs`）で読んで行ごとに出す。カード（手札・4 つの山・戦闘外のデッキの行）に `clone_of_uid`・`current_play_index`・`current_target_combat_id`・`floor_added_to_deck`・`is_dupe`・`keywords_added`・`keywords_removed`・`transform_into_after_play_id`（Rampage・Thrash・TheBall・Maul は `extra_damage`、SpoilsMap は `spoils_act_index`、後悔〔Regret〕は `cards_in_hand_at_turn_end`）、全パワーの行に `amount_on_turn_start` と、付与元・対象の `applier_combat_id`・`target_combat_id`（schema 1.3.0。無ければ null。`InterceptPower` は `covered_creature_ids`）、レリックに `status`・`is_wax`・`floor_added_to_deck` と、PenNib・Vambrace・UnsettlingLamp・MusicBox・PaelsLegion の解決中のカードの uid、PaelsTooth の `stored_cards`、GoldenCompass の `golden_path_act`、player に `card_shop_removals_used`、battle に `ally_combat_ids`・`escaped_creature_ids`。参照はカードの uid（山に無いカードには振らず null と理由）・`combat_id`・モデルの id にする。自動実装プロパティも getter を呼ばず backing field を読む。解決中の印は戦闘中の選択画面（`battle` を付ける `*_select`）でだけ観測できる。読取失敗は null・`internal_state.unavailable_reasons`・`warnings`。山の順・これから出るレリック・報酬の出現率は出さない。[意味と出典](raw-full.md#棚卸しから生成した内部状態internal_stateschema-120)。
  - `status[]` — player・敵・ペットの可視パワーに `skip_next_duration_tick`、円環の強靭に `block_base_value`、包囲に `facing`、泥棒に `stolen_strength` / `stolen_dexterity`（`[{player_id, amount}]`、符号付きdecimal）、臆病に `has_gained_block_this_turn`、ジャグリングに `attacks_played_this_turn` と `card_to_clone_uid` / `card_to_clone_readable`、まるくなるに `played_card_uid` / `played_card_readable`、泥棒（`THIEVERY_POWER`）に `stolen_gold`（実際に盗んだ累計の int。`amount` は 1 回の上限）と `target_player_id`、強奪（`HEIST_POWER`）に `target_player_id`、活力（`VIGOR_POWER`）に `vigor_attack`（`readable` / `has_command` / `attacker_is_owner` / `amount_when_started`）、呪術（`HEX_POWER`）に付与元の `applier_combat_id`（`enemies[].combat_id` と同じ uint。付与元なしは null。`internal_state.applier_combat_id` の写し）と `applier_readable`、儀式（`RITUAL_POWER`）に `was_just_applied_by_enemy`（private `_wasJustAppliedByEnemy`。敵の手番に付与された直後の手番の終わりは筋力を足さない印。可視性によらず付ける。schema 6）、爆弾（`THE_BOMB_POWER`）に `damage`（`SetDamage` の威力、decimal）、インフェルノ・深紅の衣に `self_damage`（`DynamicVars["SelfDamage"]`、decimal）、威風堂々（`PANACHE_POWER`）に `cards_left`（int）と `already_applied`（private Data の bool。初期化しない）（いずれも可視性によらず付ける。schema 7）、弱化（`DAMPEN_POWER`）に付与した敵の `caster_combat_ids`（`enemies[].combat_id` の昇順の配列）と下げた札の `downgraded_cards`（`[{uid, uid_known, card_id, original_upgrade_level}]`。uid は既存の registry にある値だけで、無い札は `uid=null`・`uid_known=false`・`uid_unavailable_reason`・warnings。uid 順）（可視性によらず付ける。schema 8）、全パワーに破壊（`Rend`）の入力の `type_for_current_amount`（`PowerModel.TypeForCurrentAmount` の enum 名。負の筋力・敏捷・集中は `type` が `Buff` のまま `Debuff`）と `is_temporary_power`（`ITemporaryPower` を実装するか）（可視性によらず付ける。schema 9）、全パワーにパワー自身の `DynamicVars` の `dynamic_vars`（`{変数名: BaseValue}`。変数名は `SelfDamage` などそのまま、`StringVar` は除く、変数が無ければ `{}`。private `_dynamicVars` を読み、null なら項目ごと null と `unavailable_reasons.dynamic_vars`、変数ごとの失敗はその名前だけ null と `unavailable_reasons["dynamic_vars.<名前>"]`。可視性によらず付ける。schema 12）、private Data・フィールドの値として血の沸騰（`RUPTURE_POWER`）に `played_cards`（`[{uid, uid_known, card_id, pending_strength}]`）、カラミティ・絞殺に `amounts_for_played_cards`（`[{uid, uid_known, card_id, amount}]`）、ヘル・レイザーに `infinite_auto_plays_this_turn` と `auto_playing_cards`、オートメーションに `cards_left`、闇の抱擁に `ethereal_count`、巨大化に `has_command_to_modify`、硬い殻に `damage_received_this_turn`、適応・再結合・幻影に `is_reviving`（幻影は `follow_up_state_id` / `follow_up_state_readable` も）、ネメシスに `should_apply_intangible`、怠惰・軟化に `cards_played_this_turn`、希望の道標に `has_already_been_given_block`、盗む（`SWIPE_POWER`）に `stolen_card`（`{uid, uid_known, card_id}`。なしは null）と `stolen_card_readable`（いずれも可視性によらず付ける。札の uid は既存の registry を引くだけで、無い札は `uid_known=false`。schema 12）を追加。泥棒・強奪は対象 player ごとの別の行で合算せず、この 2 項目は `hidden_in_ui` の行にも付ける。`vigor_attack` は player・敵の行、`hidden_in_ui`・最小行にも付け、未結合は `has_command=false`・`attacker_is_owner=null`、読取失敗は `readable=false`・null・`unavailable_reasons.vigor_attack.<項目>`。uid は既存の個体 registry を共用し、待ちなしは null と readable=true、読取失敗は null と readable=false・理由・warnings。非表示・可視性不明のパワーでは追加項目を省略（泥棒・強奪の対象別の項目、活力の `vigor_attack`、呪術の付与元、儀式の印、schema 7 の爆弾・インフェルノ・深紅の衣・威風堂々の項目、schema 8 の弱化の項目、schema 9 の破壊の入力、schema 12 の `dynamic_vars` は除く）。呪術の付与元の読取失敗は `applier_readable=false`・null・`unavailable_reasons.applier_combat_id`。読取失敗は該当キーを `null` と行内 `unavailable_reasons.<key>` の理由・`warnings` にし、他項目を保持。通常の skip=false・空の盗難配列・空の `dynamic_vars` はMarkdownで省略（変数があれば `vars: SelfDamage=2` の形）。schema 4 の追加は版を上げていない（schema 6・7・8・9・12 の項目は版を上げたときの追加）。[意味・観測可能性・出典](power-internal-state.md) を参照。
  - `deck_count` — number of cards in the master deck. Present on every screen, combat included (combat clones the deck into the draw pile, so the deck itself stays intact).
  - `hand[]` / `draw_pile[]` / `discard_pile[]` / `exhaust_pile[]` の `exhaust_on_next_play` — 戦闘中だけ、`CardModel.ExhaustOnNextPlay` の現在の bool 値を返す（false も出す）。戦闘中の `card_select.cards[]` の grid 行と choose-a-card 行にも `uid` と同じ条件で出す。戦闘外のデッキ・選択画面・choose-a-card、`hand_select.cards[]`（同じ札は `hand[]` で読む）、各 preview には出さない。true がプレイの合間に残るのは `exhaust_pile[]` だけ（荒廃の自動プレイが拒否された札。手番終了の `EndOfTurnCleanup` で消える）。getter は backing field を返すだけで副作用なし。`CardPileCmd.AutoPlayFromDrawPile` が `forceExhaust` を設定し、このデコンパイル版で true を渡すのは `Havoc.OnPlay` だけ。Exhaust keyword から推定しない。既存の `SafeGetBool` で読取失敗は `null` と warnings にし、state 全体を落とさない。Markdown は手札・パイル・grid 行で true のときだけ `[exhaust-next]`。追加なので版は上げていない（schema 3 のときの追加）。[契約・各メソッドと行番号](raw-full.md#次回プレイ時の廃棄フラグ追加schema-3) を参照。
  - `hand[]` / `draw_pile[]` / `discard_pile[]` / `exhaust_pile[]` / `play_pile[]` の `energy_cost` — 戦闘中だけ `{ costs_x, base, base_raw, canonical, captured_x_value, with_local_modifiers, with_all_modifiers, local_modifiers, unavailable_reasons }`。基礎（強化を含む）・local 適用後・global 込みのコストはゲームの `GetWithModifiers(None/Local/All)`。private の `_localModifiers` を適用順に読み、各 `{ amount, type, expiration, reduce_only }` をそのまま出す。`Relative` / `Absolute`、`WhenPlayed` / `EndOfTurn` / `EndOfCombat`（値0）と複合の `"EndOfTurn, WhenPlayed"` は enum 名。`base_raw` は X・負コストも含む None の整数、`canonical` は Canonical（Xでは0）。`captured_x_value` は X のときだけ CapturedXValue を読み、非 X は null と `not_x_cost`、種別不明は null と `costs_x_unavailable`。既存の base / local / all は X で null と `x_cost`、基礎値が負のコストなしは null と `no_energy_cost` にする。修正なしは `[]`、読取失敗は null と `unavailable_reasons` のキー別理由・warnings。X/コストなしでも実際の修正一覧は読む。戦闘中の grid 選択行・choose-a-card 行にも `uid` と同じ条件で出し、戦闘外・hand_select・preview には出さない。Markdown は修正一覧が非空・修正後のコストが基礎値と異なる・X・raw と canonical の差・通常の適用対象外以外の理由で null がある場合に、数値・修正一覧・null の理由を表示。既存の `cost` / `star_cost` は変更せず、追加のみなので版は上げていない（schema 4 のときの追加）。[契約・出典](raw-full.md#energy-cost-object追加schema-4) を参照。
  - 同じ行の `star_cost_state` — 戦闘中だけ `{ costs_x, canonical, base, current, temporary_costs: [{cost, clears_when_turn_ends, clears_when_card_is_played}], unavailable_reasons }`。`CardModel.HasStarCostX` / `CanonicalStarCost` / `BaseStarCost` / `CurrentStarCost` と private の `_temporaryStarCosts`（`TemporaryCardCost`、最後が有効）を順に読む。choose-a-card の戦闘中の候補にも出す。読取失敗は項目別 null と理由・warnings。[契約・出典](raw-full.md#star-cost-state-object追加schema-4)。
  - `hand[]` / `draw_pile[]` / `discard_pile[]` / `exhaust_pile[]` / `play_pile[]` の詳細は共通。
    `type` / `target_type` はゲームの enum 名、`affliction` は現在の `{ id, amount }`
    （呪詛〔`HEXED`〕の札だけ、札自身のエセリアルを bool の `local_ethereal` で足す。呪術の Global のエセリアルを含まない。読取失敗は null と理由）、
    `enchantment` は `{ id, amount, status }`（正常に未付与と確認できた場合は null）。
    `status` は `EnchantmentModel.Status` の enum 名（`Normal` / `Disabled`）。
    モメンタムだけ `extra_damage`（`Momentum._extraDamage`。再演を含むプレイの 1 回ごとに、そのダメージの後で `amount` ずつ伸びる追加ダメージ。手札の `vs_targets` には既に含まれる）を足す。
    `is_upgraded` / `is_upgradable`、`tags`（`CardTag` の enum 名を enum の数値順。無ければ `[]`）、
    `can_play` / `unplayable_reason`（手札と同じ `CardModel.CanPlay`。パイルでは「いま出せるか」ではない。
    [パイルの `can_play`](raw-full.md) の節を参照）も同じく出す。
    `dynamic_vars` は変数名 → 現在の `BaseValue`（小数を含む数値、`StringVar` を除く）。
    EnchantedValue / PreviewValue ではない。CalculatedVar は計算前の基底値。
    数値変数なしは `{}`。読取失敗は該当値を null とし、行の `unavailable_reasons` に
    `enchantment.status` / `enchantment.extra_damage` / `dynamic_vars.Damage` 等のパス別理由を入れ、`warnings` に記録する。
    山札は一次・二次比較にゲームの `Rarity` → `CardModel.CompareTo` を使い、
    同順位は固定 uid の ordinal 順にして引く順を意図して隠す。
    比較の例外は `draw_pile.order` の warning を出し、`Rarity` → `Id.Entry` → uid で返す。
    捨て札・廃棄札・プレイ中の札は格納順。Markdown はパイルの本行末尾に `[Attack/AnyEnemy]`、
    `Disabled` のエンチャントにだけ `(disabled)` を付ける。
    基底値は `base Damage 6, Vulnerable 2` の形で札ごとに1行にまとめる。
    追加項目のため版は上げていない（schema 4 のときの追加）。詳細と出典は [契約](raw-full.md#戦闘カードの詳細追加schema-4) を参照。
  - 同じ行（と choose の候補行・`ready`）の札の効果の入力（schema 7、`cat12079801/sts2#1019`）: `retain_this_turn` / `single_turn_retain` /
    `sly_this_turn` / `single_turn_sly`（実効の保留・Sly と、今ターンだけの付与〔private のフラグ。`EndOfTurnCleanup` で消える〕）、
    `base_replay_count` / `extra_replay_this_turn` / `enchanted_replay_count`（`GetEnchantedReplayCount`: 札と付与による追加の実行回数。
    Glam は `Disabled` で数えない）、Mad Science の行だけ `tinker_time_rider`（`TinkerTime.RiderEffect` の enum 名。`None` も実値）。
    `enchantment` に付与自身の `dynamic_vars`（Glam / Spiral の `Times`）。全行に必ずあり、false・0 は実値、読取失敗は null・理由・warnings。
    Markdown は単ターンの付与・0 以外のリプレイ・ライダーだけ子行。[契約・出典](raw-full.md#札の効果の入力schema-7)。
  - 戦闘中の `uid` は `<card_id>#<suffix>`。デッキ由来は `DeckVersion` で元の札を照合し、
    `Player.Deck.Cards` の0始まりの位置を使う。マルチは `<位置>p<NetId>`。
    デッキ位置はゲームのデッキ画面で見られ、保存して終了 → continue 後も同じ uid を再現する。
    生成札・手札に直接入る札・複製札は `g<OSの乱数>` を使い、衝突時に再採番する。
    同じ戦闘の同じ個体では固定。旧 MOD は初回観測順の連番で、戦闘開始時に生成される札の
    uid も再ロード後に同じ番号を再現していた。この版ではその札の乱数 uid は再ロードで変わり、
    開始時の生成札を含む初手のやり直し検出は後退する。engine 側は sts2 #873 で
    uid に依らない検出に修正する。engine の `turn_sig` が再ロード検出に使う
    uid の再現は、デッキ由来の初手で満たす。旧 fixture・dump の番号は異なる場合があり、必要なら取り直す。
    [uid とパイル順序の契約](raw-full.md#戦闘カードの-uid-とパイル順序) を参照。
  - `play_pile` / `play_pile_count` — 戦闘中の `PlayerCombatState.PlayPile`。
    他のパイルと同じ行（`uid`・`exhaust_on_next_play`・`energy_cost`・上記の詳細を含む）。
    解決中に一時的に入る札で、通常は空。Markdown の Play Pile の節は件数が1以上の場合だけ表示。
    計画は `is_resolving: false` の snapshot を使う。
  - `combat_history` — 戦闘中だけ、単独・マルチのローカル Player に `{ turn_number, card_plays_started_this_turn: { total, by_type }, card_plays_finished_this_turn: { total, by_type }, card_plays_finished_this_combat, any_manual_cards_played_this_turn, card_plays_finished_this_turn_card_uids, card_play_events_this_turn, last_player_turn_card_uids, cards_exhausted_this_turn, unblocked_damage_events_this_turn, unblocked_damage_events_this_combat, block_gains_this_turn, unavailable_reasons }`。TurnNumber は PlayerCombatState、その他は本体の History とターン判定を読む。型別キーは None/Attack/Skill/Power/Status/Curse/Quest、0 も出す。このターン／前手番の uid は既存の registry 共通・履歴順・重複ありで、ボーラ／共鳴の手斧と同じ個体照合のため他 Player のプレイも含む。Player の play phase ではこのターンの uid が次手番の回収の判定用（これまでの分）。`*_this_turn` は読取時の side の手番を指すので、計画には Player の play phase の snapshot を使い、敵の手番の uid から回収を判定しない。`card_play_events_this_turn` はこの手番・所有者の開始／完了 entry を履歴順のまま `{phase, uid, is_auto_play, target: null|{combat_id, entity_id}, play_index, play_count}` で出す（荒廃の子は「親の開始→子の開始→子の完了→親の完了」。拒否されてプレイされなかった札は出ない。読めなければ配列全体を null）。ジャグリングの開始数は内部 Data と常に同じではなく、`JUGGLING_POWER` の `status[].attacks_played_this_turn` を正確な値として読む。被ダメは自分が受けた UnblockedDamage > 0 の件数。block_gains は不動の式に合わせ、自分の CardPlay による Move の件数（Receiver 条件なし、次のプレイ用の cardPlay=null 文脈）。レリックを含む全ブロック獲得回数ではない。取得失敗は項目全体を null とキー別理由・warnings にし、他の値を保持。Markdown は手動プレイの bool・正の件数・非空 uid・失敗を短い行に出し、uid は `this turn=[...]` / `last turn=[...]` と区別。戦闘外・旧 MOD は省略、追加のみなので版は上げていない（schema 4 のときの追加）。[各式・出典・評価時点](raw-full.md#戦闘履歴の要約追加schema-4) を参照。
    `card_plays_finished_this_combat`（schema 1.5.0）は金の斧（`GoldAxe.CanonicalVars`）の倍率と同じ `History.CardPlaysFinished.Count()`。戦闘全体・全 Player の完了の entry の数で、解決中の札（金の斧自身・本文の途中の親）は入らない。読取失敗は null・理由・warnings。Markdown は `finished plays (combat)=N`。
    `any_manual_cards_played_this_turn` は v0.113.1 の `PaelsEye.AnyCardsPlayedThisTurn` と同じ履歴条件の bool。記録済み Actor がローカル Player の Creature・本体の `HappenedThisTurn` が true・`IsAutoPlay` が false の完了 entry があれば true、空なら false。開始だけの札は含めず、読取失敗は null・キー別理由・warnings。Markdown は false も `manual card played=no` と表示する。パエルの眼は既存の `combat_state.used_this_combat`・`was_owner_part_of_last_player_turn` と合わせて判定する。`WhisperingEarring` 所持時の `turn_number == 1` は本体が履歴によらず「プレイ済み」と扱うため、その例外も公開値から評価する。所持の判定は `player.relics` に `WHISPERING_EARRING` があるかで、`melted` は見ない。旧 MOD の欠落・null は unknown として扱う。
  - `deck` — the master deck itself, one entry per card: `index` (position in this list), `id`, `name`, `type`, `cost`, `star_cost`, `description`, `rarity`, `is_upgraded`, `keywords`. **Outside combat only** — during a fight the hand plus `draw_pile` / `discard_pile` / `exhaust_pile` / `play_pile` enumerate every card. Empty (and `deck_count` 0) before a run's starting deck is dealt out. Markdown renders it as `### Deck (N cards)` with identical copies collapsed into one `×N` line.
- `battle` — combat state (`monster` / `elite` / `boss`): `round`, `turn`, `is_play_phase`, `enemies`. Every selection state (`state_type` ending in `_select`: `hand_select`, the grid and choose-a-card `card_select` such as Stratagem's `NCombatPileCardSelectScreen`, `bundle_select`, `relic_select`) shown while a fight is in progress carries the same `battle` (schema 1.4.0); a selection screen outside a live fight and non-selection states (`map`, `menu`, `game_over`, `card_reward`, `rewards`, `overlay`) do not. [契約](raw-full.md#戦闘中の選択画面の-battleschema-140)。
  - `enemies[].move_state` — 全モンスターに `{ readable, id, follow_up_id, last_logged_id, can_transition_away }` を出す。`MonsterModel.NextMove` の現在 ID・follow-up・遷移可否と、ゲームの `MoveStateMachine.StateLog` 末尾 ID を読み取る。スタン時も実際の復帰先とロックを保持し、四値がすべて取得できたときだけ readable=true。取得不能の項目は null・`unavailable_reasons.<key>`・個体を識別する warnings とし、成功項目は保持。履歴からの推測・次行動の事前抽選はしない。follow-up は次の分岐状態の ID の場合もあり、次の実行行動の確定値とは限らない。Markdown は ID と follow-up、失敗時の理由を短く表示する。単独・協力プレイと死亡中の敵に共通（四値は schema 4 のときの追加）。旧 MOD の欠落は unknown。[契約・ソースの根拠](raw-full.md#敵の行動状態追加schema-4) を参照。
    schema 6 で `has_follow_up`（終端の行動なら false の実値）・`must_perform_once`・`current_state_id` / `initial_state_id`（private `MonsterMoveStateMachine._currentState` / `_initialState`）・`current_state_registered`（現在地点が `States` に登録された状態そのものか。強制遷移した `STUNNED` などは false）・`performed_first_move`・`spawned_this_turn`・`state_log`（`StateLog` の全要素を順序・重複のまま `[{id, is_move}]`。空は `[]` の実値、一つでも読めなければ全体を null）・`is_stun` / `stun_handler`（`CreatureCmd.Stun` の Wrapper が包んだ処理を許可リストの名前に対応付ける。`CreatureCmd.Stun.no_op`・`SlumberingBeetle.WakeUpMove` など。未対応・`STUNNED` と Wrapper の食い違いは null と理由）・`inputs_readable` を足した。`readable` は四値のまま。[契約・出典](raw-full.md#敵の行動の入力schema-6)。
  - `enemies[].slot_name` — `Creature.SlotName`。null はスロット無し、読取失敗は null・`unavailable_reasons.slot_name`・warnings。Markdown は HP 行に `| Slot: …`（schema 6）。
  - `enemy_move_context` — 戦闘全体の `{ game_assembly_sha256, game_mode, is_dread, ascension_levels, combat_player_count, has_encounter, encounter_id, encounter_slots: [{name, occupant_ids}], readable }`。`RunManager.HasAscension` が true の段階・`IsDread`・遭遇のスロットと `CombatState.Enemies` 全体の占有個体。ラン外は理由付きの null。Markdown は `## Enemies` の下に 1 行（schema 6）。[契約・出典](raw-full.md#敵の行動の入力schema-6)。
  - `enemies[].combat_state` — 許可リストの敵だけ。schema 6 で行動の分岐・効果量に効く private の現在値を足した（`Aeonglass` の `additional_strength` / `wither_upgrade_count`、`Axebot` の `stock_amount`、`BowlbugRock` の `is_off_balance`、`Fabricator` の `last_spawned_id` / `last_spawned_readable`（まだ召喚なしは readable=true・null）、`FrogKnight` の `has_beetle_charged`、`KnowledgeDemon` の `curse_of_knowledge_counter`、`LivingFog` の `bloat_amount`、`Nibbit` の `is_alone` / `is_front`、`Queen` の `has_amalgam_died`、`TestSubject` の `respawns` / `extra_multi_claw_count`、`Toadpole` の `is_front`、`TwoTailedRat` の `turns_until_summonable` / `call_for_backup_count`）。schema 1.1.0 で台詞の印を足した（`GremlinMerc` の `has_spoken`〔private `_hasSpoken`〕、`KinPriest` の `speech_used`〔private `_speechUsed`〕。分岐の先は台詞だけ、false も実値、`cat12079801/sts2#1065`）。滝の巨人（`WATERFALL_GIANT`）には `{ is_about_to_blow, steam_eruption_damage, current_pressure_gun_damage, steam_boost_amount, pressurize_amount, stomp_damage, ram_damage, pressure_up_damage, siphon_heal }` を出す。private `WaterfallGiant._isAboutToBlow`（`TriggerAboutToBlowState` で true）・`_steamEruptionDamage`（`AboutToBlowMove` が蒸気噴出の量を写し、`ExplodeMove` で使う）・`_currentPressureGunDamage`（次のプレッシャーガンの素値）と、getter `SteamBoostAmount`（Dread 込み）・`PressurizeAmount`・`StompDamage`・`RamDamage`・`PressureUpDamage`・`SiphonHeal`（人数を掛ける前）の現在値で、false・0 も出す。HP・行動 ID・Ascension から作らない。ラン外・負の値は null。読取失敗は null・`combat_state.unavailable_reasons.<key>`・warnings。他の敵・旧 MOD では省略（unknown）。Markdown は行動の行の次に `Combat state:` 行。版は上げていない（schema 4 のときの追加）。[契約・出典](raw-full.md#敵の戦闘内部状態追加schema-4) を参照。
  - `enemies[].intents[].attack` — Attack / DeathBlow の意図（`AttackIntent`）だけに `{ readable, base_damage, hits, resolved_damage }` を出す。`base_damage` は現在の `AttackIntent.DamageCalc()` の補正前の decimal を invariant culture の 10 進文字列（例 `"20"`）、`hits` は `Repeats`（非負 int。単発・DeathBlow は 1）、`resolved_damage` は 1 ヒットあたりの `GetSingleDamage`（ローカルのプレイヤーが対象。筋力・脆弱・脱力など込みの int で、`label` の数値と同じ経路）。ローカルのプレイヤーが居ないときは解決値を null にする（補正前の値を出さない）。三値の取得成功時だけ readable=true、失敗は null・`attack.unavailable_reasons.<key>`・warnings。ラベル・本文・履歴から補完しない。攻撃以外の意図・旧 MOD では省略（unknown）。Markdown は意図の行に `[attack: base 20 | hits 1 | resolved 36]`。版は上げていない（schema 4 のときの追加）。[契約・出典](raw-full.md#敵の攻撃の意図追加schema-4) を参照。
  - `enemies[].alive` — **on every enemy**. Almost always `true`: a killed enemy leaves the fight and stops being listed. The exception is a corpse the game refuses to remove — a Decimillipede segment held by `REATTACH_POWER` — which stays listed with `alive: false` and `hp: 0`, keeping the `entity_id` it already had. **Filter targets on `alive`, not on `hp > 0`**; `play_card` / `use_potion` already refuse a dead target.
  - `enemies[].hittable` — **on every enemy** (alive or dead). `Creature.IsHittable` as the game answers it (`IsDead` → false; otherwise `Hook.ShouldAllowHitting`, false while a reviving power's owner is reviving). The same getter the game uses for random targets (`CombatState.HittableEnemies`). `null` with `unavailable_reasons.hittable` and a warning when unreadable — never derived from `hp`/`alive`. Markdown adds `| Hittable: yes/no` to the HP line. [契約・出典](raw-full.md#敵に当てられるかhittable追加schema-4)。
  - `enemies[].revive` — present only when that corpse is scheduled to come back: `{ in_turns, hp, power_id, power_name }`. `in_turns` counts enemy turns until it heals itself back up (2 on the turn it dies, 1 on the turn before it stands up, when its `intents` also show `Heal`); `hp` is what it returns with. Both are `null` when they cannot be read. A corpse kept in the fight by a power that never revives it has `alive: false` and no `revive`. Markdown marks the enemy heading `— DEAD` and prints a `⚠ WAITING TO REVIVE in N enemy turn(s) at H HP` line.

Card objects carry `is_upgraded`, `is_upgradable`, `upgrade_level` and `max_upgrade_level` (a card can have more than one upgrade level, so `is_upgraded` does not mean "no upgrade left").

**Combat rows** (`player.hand[]`, `draw_pile[]`, `discard_pile[]`, `exhaust_pile[]`, `play_pile[]`) also carry `local_keyword_ids` (the card's own `CardKeyword` names), `keyword_ids` (effective in combat, e.g. Ethereal from Hex), the card-effect inputs above (`retain_this_turn`, `single_turn_retain`, `sly_this_turn`, `single_turn_sly`, the replay counts), and **always** an `upgrade_preview` with a `status` (schema 5):
- `ready` — the result of upgrading that very card once, as absolute values read off a detached clone: `id`, `name`, `description`, `type`, `target_type`, `tags`, `upgrade_level`, `max_upgrade_level`, `is_upgraded`, `is_upgradable` (can it be upgraded again), `dynamic_vars` (every numeric `BaseValue`), `local_keyword_ids`, `keyword_ids`, `energy_cost`, `star_cost_state`, `affliction`, `enchantment`, the card-effect inputs (schema 7), plus display `cost` / `star_cost`. `energy_cost.with_all_modifiers` is always `null` (`detached_preview_context`): the clone is in no pile, so pile-dependent cost changes are unknown. Multi-level cards show the next level only.
- `not_upgradable` — at the cap (cap 0 included: Wound, Dazed, Ascender's Bane); only `status` / `from_level` / `unavailable_reasons`.
- `unavailable` — could not be observed (clone or read failure, inconsistency); `unavailable_reasons` names the failed paths. Not the same as "cannot upgrade".

`from_level` is the level the preview starts from. The clone is isolated: no uid, pile, history or RNG is touched and the live card's listeners are not notified. `?perf=1` on the state GET adds `state_build_perf` (build time, clones, preview time). Details: [raw-full.md](raw-full.md#戦闘の強化結果upgrade_previewschema-5).

**Out of combat**, on the screens where an upgrade or a pick is being decided — `card_select` with `screen_type: "upgrade"`, `hand_select` with `mode: "upgrade_select"`, and `card_reward` — a card that still has an upgrade left also carries the older display-only `upgrade_preview`: `{ name, cost, star_cost, description, keywords }` of the card as it would read after one upgrade, without `status`. The in-combat `hand_select` candidates keep this display shape; the machine-readable values for the same cards are in `player.hand[]`.

**In-combat selection rows** (`card_select` grid and choose, `hand_select`, a `card_reward` opened during combat) carry `combat_instance` (schema 7): `{source: "pile", pile, uid}` when the candidate is in exactly one combat pile (read its effect inputs from that pile row), or `{source: "detached", pile, values}` when it is in none (e.g. three-choice generated candidates): no uid is issued and `values` holds the candidate's own type, target type, level, numbers, enchantment, keyword ids, single-turn expiry, replay counts, rider and costs. Legal targets and playability are not evaluated for a detached candidate. A card in two piles is not mapped (`source: null` with a reason). `index` and the screen identity are unchanged; out-of-combat rows do not get the key. Details: [raw-full.md](raw-full.md#選択候補の個体combat_instanceschema-7).

| `state_type` | Screen | Available Actions |
|---|---|---|
| `menu` | Main menu, menu submenu (incl. multiplayer host/join/load lobby), character select, `menu_screen: settings`, the in-run pause menu (`menu_screen: pause`) and the screens it opens (`menu_screen: compendium`, …), or a blocking FTUE/tutorial/popup that can also appear mid-run | `menu_select` |
| `unknown` | Unrecognized room or null state. In a run, a room change (`room_changing`) or a loading run (`run_loading`) reports `is_resolving: true` | None — re-read |
| `monster` / `elite` / `boss` | In combat | `play_card`, `use_potion`, `end_turn` |
| `hand_select` | In-combat card selection (exhaust, discard, upgrade) | `combat_select_card`, `combat_confirm_selection` |
| `rewards` | Rewards screen (post-combat or event-triggered) | `claim_reward`, `proceed` |
| `card_reward` | Pick a card to add to deck | `select_card_reward`, `skip_card_reward`, `select_card_reward_alternative` |
| `map` | Map navigation | `choose_map_node` |
| `event` | Event or Ancient encounter | `choose_event_option`, `advance_dialogue` |
| `rest_site` | Rest site | `choose_rest_option`, `proceed` |
| `shop` | Shop (auto-opens inventory) | `shop_purchase`, `sell_potion`, `proceed` |
| `fake_merchant` | Fake Merchant event (relic-only shop) | `shop_purchase`, `sell_potion`, `proceed` |
| `treasure` | Treasure room (auto-opens chest) | `claim_treasure_relic`, `proceed` |
| `card_select` | Deck card selection overlay (transform, upgrade, enchant, remove, choose-a-card) | `select_card`, `confirm_selection`, `cancel_selection` |
| `bundle_select` | Card bundle choice overlay | `select_bundle`, `confirm_bundle_selection`, `cancel_bundle_selection` |
| `relic_select` | Relic choice overlay (boss relics) | `select_relic`, `skip_relic_selection` |
| `crystal_sphere` | Crystal Sphere minigame | `crystal_sphere_set_tool`, `crystal_sphere_click_cell`, `crystal_sphere_proceed` |
| `game_over` | Run ended | `menu_select` with `main_menu` |
| `overlay` | Unhandled overlay (catch-all, prevents soft-lock) | None (manual interaction needed) |

`menu_screen: "popup"` では、縦型ポップアップの表示タイトルを `message`、本文を `body` に返す。
現在のゲーム言語・書式展開後の文字列で、リッチテキストのタグは除去する。Markdown にも両方を表示する。
読み取り失敗は該当項目だけを省略し、`warnings` の `menu.popup.message` / `menu.popup.body` に記録する。
欠落は unknown と扱い、選択肢は現在の `options` を参照する。ボタンだけを検出した場合は従来どおり
`message: "Popup active."` / `body: null`。取得修正・表示追加なので版は上げていない（schema 3 のときの追加）。
[文言例・出典・契約](raw-full.md#popup--確認ポップアップの表示文言schema-3) を参照。

**Note:** `use_potion` and `discard_potion` work during any state where potions are accessible (combat, map, events, etc.).

**Note:** the settings screen is reported as `state_type: menu`, `menu_screen: "settings"` wherever it is
open, with `in_run: true` when a run is paused underneath (`run` and `player` are still included there) and
`in_run: false` on the main menu. Its only option is `back`; changing settings values is not exposed.

**Note:** the in-run pause menu is reported as `state_type: menu`, `menu_screen: "pause"`, `in_run: true`
(`run` and `player` are still included), with one `{name, enabled}` option per visible button: `resume`,
`settings`, `compendium`, `give_up`, `disconnect`, `save_and_quit` (`give_up` / `save_and_quit` are hidden
for multiplayer clients, `disconnect` only appears for them). Open it with the `open_pause_menu` action.
Anything the pause menu opens on the same stack is reported the same way with a derived `menu_screen`
(e.g. `compendium`, `card_library`), `screen_class` naming the game's node type, and a single `back`
option — so a screen the API can enter is always a screen it can leave. While any of these is open,
gameplay actions are refused with an error instead of being applied to the room underneath.

## POST — Actions

All POST requests use a JSON body with an `"action"` field.

### 検証用の開発コンソール（既定は無効）

`POST /api/v1/singleplayer` に
`{"action":"dev_console","command":"card BURNING_PACT Draw","wait":true}` を送る。
MOD DLL と同じ場所の `STS2_MCP.conf` に `"dev_console_enabled": true` を追加して再起動すると有効。
省略・false・文字列の true は無効。検証用のランで使う。MCP ツールの追加はない。
応答は `command`、`success`、`message`、`status` を含み、失敗時は `error` も返す。
`wait: true` は配送・Task 待ち・状態安定待ちで `wait_timeout_ms`（500〜30000 ms、既定10000）の
締め切りを共有する。`wait: false` の配送・Task 待ちは専用の上限10000 ms。
Task の期限切れは HTTP 200 / `status: "error"` / `task_completed: false`、`success` はゲームの値を保持し、
`retry` は付けない。ゲームには配送済みなので再送せず state を読み直す。
検証用の準備では `"scenario_setup_guard": {"version": 1, "expected_screen": "map"|"event"|"rest_site"}` を付けられる（schema 10）。
実行の直前にメインスレッドで `scenario_setup_guard` を判定し直し、許可されないか画面が `expected_screen` と違えば実行せず `guard_refused: true` で返す。
未知の版・画面・キーは拒否。空のコマンドはガードを通したうえで従来の拒否文を返す。[詳細](raw-full.md#検証用の開発コンソール既定は無効)。
503 / `retry: true` の場合も、再送前に state とログを確認する。
マルチプレイには提供しない。許可リスト外は実行しない。
`remove` の実名は `remove_card`。設定・全許可名・拒否条件・出典は
[開発コンソール API](dev-console.md) を参照。


**Response contract — one shape for every POST.** Every POST response body carries a `status` field
that is either `"ok"` or `"error"`. A client only needs that one key:

| Outcome | HTTP | Body |
|---|---|---|
| Success | 200 | `{ "status": "ok", "message": "...", ... }` |
| Action failure (the action was understood but could not be performed) | 200 | `{ "status": "error", "error": "..." }` |
| Transport failure (invalid JSON, missing `action`, unknown route, wrong method, wrong run mode, unhandled exception) | 4xx / 5xx | `{ "status": "error", "error": "..." }` |

`error` is present if and only if `status` is `"error"`. GET responses return state payloads and
are not covered by this contract (`GET /` is the exception and reports `"status": "ok"`).

### Waiting for the Game to Settle

By default a POST returns as soon as the action has been dispatched, so the animation, card
resolution and redraw it triggers may still be running. Add `"wait": true` and the server does the
settle loop for you and answers with the settled state — one round trip instead of a GET poll loop.

| Parameter | Type | Default | Meaning |
|---|---|---|---|
| `wait` | bool | `false` | Wait for the game to settle before answering. Also accepts `1`, `"1"`, `"true"`, or the query string `?wait=1`. |
| `wait_timeout_ms` | int | `10000` | How long to wait, clamped to `500`–`30000`. |
| `include_state` | bool | `true` when `wait` is set | Include the settled state in the response. |

```json
{ "action": "end_turn", "wait": true }
```

Extra response fields, appended after the action's own keys:

| Field | Meaning |
|---|---|
| `waited_ms` | How long the wait took. |
| `polls` | How many state reads it took. |
| `settled` | `true` if the game came to rest, `false` on timeout. |
| `changed` | `false` means nothing observable moved — the action was a no-op. |
| `state` | The settled state, same shape as `GET /api/v1/singleplayer` (omitted when `include_state` is `false`). |
| `wait_error` | Only present if the wait itself failed; the action still ran. |

A state counts as settled when `is_resolving` is false and — in `monster` / `elite` / `boss` — the
battle is back in its play phase, and that has held for 3 consecutive polls (~180 ms). One poll is
not enough: a click takes effect a frame later, and the game looks idle for a frame between two
queued actions. If the state has not changed at all within the first 3 s the wait stops early and
returns `settled: true, changed: false`.

Nothing is waited on when the action returned `"status": "error"`. Singleplayer only —
`POST /api/v1/multiplayer` ignores these parameters.

### Errors

| Status | Meaning |
|---|---|
| `200` with `"status": "error"` | The action ran and was rejected (bad parameters, illegal move). Retrying the same request will fail the same way. |
| `500` | The action ran and threw. |
| `503` with `"retry": true` | **The action did NOT run.** The game's main thread did not answer within 10 s, or more than 32 requests are already queued — the game is not processing frames (scene load, hang, minimized/paused app). Body: `{ "error": "...", "status": "error", "retry": true }`. Nothing was applied to the run, so the request may be sent again once the game is responsive. |

This applies to GET endpoints too; every request is served from the game's main thread.

### Menu / Game Over

| Action | Parameters | When to Use |
|---|---|---|
| `menu_select` | `option`: string, `seed`?: string, `ascension`?: int | Choose an advertised menu option. Options are case-insensitive. Submenus include `back` where visible, including `profile_select` options `profile_1`, `profile_2`, `profile_3`, and `back`. Blocking popups expose normalized button labels such as `ignore` or `back`. On `settings` (opened with `settings` from the main menu, or from the pause menu during a run) the only option is `back` (aliases `close`, `resume`), which leaves the screen and returns to whatever was underneath; settings values themselves cannot be changed through the API. On `pause` the options are `resume` (aliases `back`, `close`, `continue`) to close the menu and return to the run, `settings` and `compendium` (both left again with `back`), `give_up` (alias `abandon`) which **abandons the run permanently** — it opens the game's confirmation popup, reported next as `menu_screen: "popup"`, and is never auto-confirmed, so answer it with `yes` / `no` — `save_and_quit` (alias `main_menu`) which saves and returns to the main menu, where `continue` resumes the run, and `disconnect` in multiplayer. On the screens the pause menu opens (`compendium` and below) the only option is `back`. `game_over` supports `main_menu` only; `continue` returns an error. Supplying `seed` in unsupported contexts such as standard singleplayer character select returns an error and does not start a run. Timeline is no longer blocked when epochs are pending; use the `timeline_reveal_epochs` action to clear them. Multiplayer flow: on `multiplayer_join` use `refresh` / `back` / `join_<index>` / `join_<player_id>`. On `multiplayer_load_lobby` use `confirm` (or `embark`) to ready up, `unready` to retract, `back` to leave. On `character_select` while in MP, an additional `unready` option becomes available after readying, plus a `lobby` block in state lists ascension, all_ready, and per-player roster. `ascension` sets the ascension level on character select (applied before `option`, so it can ride along with the character pick or with `confirm`, or be sent alone with an empty `option`); character select state reports the current `ascension`, the unlocked `max_ascension`, and `ascension_selectable`. |
| `open_pause_menu` (alias `pause`) | (none) | Open the in-run pause menu (the top-bar gear / Esc), the only API route out of a run. Requires a run in progress; returns `already_open: true` if it was already up, and an error if the compendium or settings is stacked on top of it (leave that with `menu_select` `back` first) or if a card is still being played. The next state reports `menu_screen: "pause"`. ⚠️ From there `give_up` **abandons the run permanently** (it asks for confirmation first) and `save_and_quit` ends the session back at the main menu — neither is undoable from the API. |
| `timeline_reveal_epochs` | (none) | Reveal every obtained-but-unrevealed epoch. A finished run leaves epochs obtained, and the main menu hides `singleplayer` until they are revealed. Call from the main menu and repeat until the response has `done: true`; `pending_epoch_ids` lists what is left. Opens the Timeline, reveals each pending epoch through the game's own reveal path (granting its unlocks and writing progress), then returns to the main menu. Works with no run in progress. |

### Profiles

`GET /api/v1/profile` returns persistent progress for the active profile, including character stats, discoveries, achievements, epochs, and global run totals.

`GET /api/v1/compendium` returns the active profile grouped like the in-game Compendium:

When a run is active, the response includes `current_run.run_id` in `{save_scope}:profile{profile_id}:{start_time}` format. This identifies the specific run attempt, while `seed` identifies the generated run content.

| Section | Status |
|---|---|
| `card_library` | Discovered cards plus pick/skip/win/loss stats. Card rules text is supplied by game state when cards are visible in a run. |
| `relic_collection` | Discovered relic IDs. Profile data does not expose a typed per-relic description or obtained-count catalog. |
| `potion_lab` | Discovered potion IDs. Profile data does not expose a typed per-potion rules text or lab metadata catalog. |
| `bestiary` | Encounter/enemy profile fight stats. The game UI marks Bestiary as future/locked, so this is stats-only rather than a full enemy catalog. |
| `character_stats` | Per-character and global totals. |
| `run_history` | Summaries of the active profile's saved `saves/history/*.run` files, capped to the 20 most recent entries. |

Run history is resolved from the active Steam account's profile save root. `current_run.run_id` identifies one concrete attempt; `seed` identifies generated run content and can repeat across attempts.

`GET /api/v1/wiki?query=<text>&item_type=<all|card|relic>&limit=<n>&scope=<discovered|all>` returns a bounded fuzzy-search result over cards and relics. `scope` defaults to `discovered` (the active profile's discovered entries only); `scope=all` searches the full catalog, which is what you need when an offered card or relic generates something this profile has never held. Every result carries a `discovered` flag. `query` is required because this endpoint is intentionally selective and does not dump the full catalog. `item_type` defaults to `all`, and `limit` defaults to 10 with an internal maximum. Card results include both `base` and `upgraded` objects when the card can be upgraded.

Example searches:

- `/api/v1/wiki?query=ironclad%20perfect%20strike&item_type=card`
- `/api/v1/wiki?query=silver%20spoon&item_type=relic&limit=5`

`GET /api/v1/profiles` returns the three profile slots:

```json
{
  "current_profile_id": 1,
  "profiles": [
    { "id": 1, "is_current": true, "has_data": true },
    { "id": 2, "is_current": false, "has_data": false },
    { "id": 3, "is_current": false, "has_data": true }
  ]
}
```

`POST /api/v1/profiles` supports:

| Action | Parameters | When to Use |
|---|---|---|
| `switch` | `profile_id`: 1-3 | Switch through the game profile UI. Empty slots can be used for fresh-profile testing. Cannot be used during a run. |
| `delete` | `profile_id`: 1-3 | Delete an inactive profile slot. The active profile is rejected. |

### Combat

| Action | Parameters | When to Use |
|---|---|---|
| `play_card` | `card_uid`?: string, `card_index`?: int, `target`?: string | Play a card from hand. Identify the card by `card_uid` (from state, e.g. `"STRIKE#2"`) — it names the card instance and stays valid for the whole combat — or by `card_index`, which shifts whenever a card leaves the hand. `target` is an `entity_id` (`"JAW_WORM_0"`) or a `combat_id` (`3`), required for single-target cards unless exactly one enemy is alive, in which case it is chosen automatically. |
| `use_potion` | `slot`: int (aliases `potion_index`, `index`), `target`?: string, `expected_potion_id`?: string | Use a potion. `slot` is the belt slot from state `player.potions[].slot`, not a position in the list. `target` (an `entity_id` or `combat_id`) is required for enemy-targeting potions unless exactly one enemy is alive, in which case it is chosen automatically; a targeted potion is never consumed against nothing. Works outside combat for non-combat-only potions. With `expected_potion_id` (schema 11) the slot must still hold that potion id, the can-use-now verdict is read again right before the potion is used and must be `true`, and `target` must be one of `valid_target_entity_ids` — a digits-only `combat_id` is refused as a format error — (omit it only for a potion used directly — your own `player:<id>` — or one with `[]`); no enemy is picked for you and no shop panel is closed. Then the MOD's own conditions apply (combat-only potions in the play phase only, nothing while player actions are disabled). Any refusal uses nothing and the error carries `potion_check` (`stage`: `identity` / `can_use_now` / `target` / `mod_acceptance`). |
| `sell_potion` | `slot`: int (aliases `potion_index`, `index`) | Sell a potion to the merchant by using it on them (Foul Potion's 100-gold mode). Only valid in a shop or fake merchant, and only for potions with a merchant interaction; shop state lists those under `sellable_potions` (a `?passive=1` read lists them only while the inventory is closed: it does not close it to ask). |
| `discard_potion` | `slot`: int | Discard a potion to free up the slot. Use when slots are full and you need room for incoming potions. |
| `end_turn` | _(none)_ | End the player's turn. |

### In-Combat Hand Selection (`hand_select`)

| Action | Parameters | When to Use |
|---|---|---|
| `combat_select_card` | `card_index`: int (alias `index`), optional `expected_uid`: string | Select/deselect a card during "choose a card to exhaust/discard" prompts. With `expected_uid` the card at that holder must still have that uid, or nothing is pressed (error). A press the game would ignore (peeking, an overlay on the stack, no combat in progress, an empty holder) is refused with the reason, with or without `expected_uid`. |
| `combat_confirm_selection` | _(none)_ | Confirm the hand card selection (refused unless `can_confirm`). |

`hand_select.cards[].index` is the position in `NPlayerHand.ActiveHolders` — the holder `combat_select_card` presses; a holder without a card gets no row and the later indices are not shifted (schema 10). Each row carries `uid` (same as the card's `player.hand[]` row; null without a readable combat) and `enabled` (false while peeking, with an overlay on the stack or outside a combat in progress — the game ignores presses then — or when one of those cannot be read). `selected_cards` is always present: the hand's `_selectedCards` in selection order as `{index, id, name, uid}`, `[]` when none, `null` plus `selected_cards_unavailable_reason` when unreadable. `min_select` / `max_select` come from the selection's prefs (null plus `select_range_unavailable_reason` when unreadable), `peeking` from the peek button, and `can_confirm` is the confirm button enabled and shown. Match by uid, not by position or name in `player.hand`. [Details](raw-full.md#手札選択の候補と-uidschema-10).

When `mode` is `upgrade_select`, each selectable card with an upgrade left carries `upgrade_preview` (its upgraded form).

### Rewards (`rewards`)

| Action | Parameters | When to Use |
|---|---|---|
| `claim_reward` | `reward_id`: string (推奨)、または `index`: int + `screen_revision`: string | 現在の有効報酬を指定。古い revision、ID/index の矛盾、無効 ID はエラー。revision なしの旧 index は現在位置として互換受理。card は三択を開く。 |
| `proceed` | _(none)_ | Leave the rewards screen. |

各 item に `reward_id` / `selection_state` / `claimable` / `skipped` /
`reward_consumed` / `last_selection` / `last_option_id` を追加。
`rewards.screen_revision` は一覧と選択状態の版、`selection_history` は同じ画面内の取得済みを含む ID と状態。
ID は金取得で index が詰まっても固定。画面終了・マップ表示・別報酬画面・ラン変更で破棄し、再起動でも無効になる。
`selection_state` は `unopened / opened / skipped / claimed / unknown`。観測欠落は unknown。
既存キーの変更はなく、版は上げていない（schema 3 のときの追加）。詳細は raw-full.md の報酬 ID 契約を参照。

### Card Reward (`card_reward`)

| Action | Parameters | When to Use |
|---|---|---|
| `select_card_reward` | `card_index`: int | Pick a card to add to deck. |
| `skip_card_reward` | _(none)_ | Skip the card reward (if allowed). Picks the `Skip` option by id, not whichever button is first — a relic can add its own alternative, and skipping does not take it. |
| `select_card_reward_alternative` | `option_id`: string (or `index`: int) | Take one of the card reward's alternative options, listed in state under `card_reward.alternatives`. `Skip` is one; relics add others (Pael's Wing adds `SACRIFICE`, which turns the reward into progress towards a relic — skipping does **not** count as sacrificing). |

Each offered card with an upgrade left carries `upgrade_preview`, so the upgraded form can weigh into the pick.

`card_reward.parent_reward_id` と `selection_state` で親報酬を識別する（不明なら null／unknown）。
Skip は三択を閉じるが親報酬を消費しない。応答に親 ID と
`closes_selection_without_consuming_reward:true` を追加。`action_stage:submitted` は送信段階で、
`wait:true` 後に `outcome_observed:true` / `selection_closed:true` / `reward_consumed:false` を確認する。
Skip 後も `claimable:true, skipped:true` となり再開封できる。
SACRIFICE の完了、REROLL の継続、中断はそれぞれ別に記録する。GET では状態を消去しない。

### Map (`map`)

`map.boss` includes the upcoming boss coordinate plus stable `id` and readable `name` fields when the run map exposes boss identity. `map.bosses` contains all boss entries for maps with more than one boss. The boss is shown on the game's own map, so reporting it reveals nothing the player cannot see.

`map.encounters_seen` lists what **this act has already served** — the rooms entered so far, grouped as `monster` / `elite` / `event`, each with the 1-based `floor` within the act, the encounter `id` and `name`, and `monsters` (the line-up the encounter actually resolved to, which differs between rooms sharing an id). A room entered from an event carries `via_event: true`. `elite_cycle` appears only once the act's elite pool has been exhausted at least once, and says how many complete passes have happened — an elite already fought does not come back until the pool wraps, so this is the field to read for "can that elite show up again". It mirrors what the game shows when the player hovers a travelled node on the map.

> **There is deliberately no "what comes next".** A previous version exposed `map.next_encounters`, read out of the act's pre-rolled room list. The game does not reveal the next monster / elite / event until the room is entered, so that field let a client route around matchups no human could see; it was removed in schema 3. For the same reason `encounters_seen` never reports the *unused* pool — listing it would give the next encounter away by elimination.

| Action | Parameters | When to Use |
|---|---|---|
| `choose_map_node` | `col` + `row`: int (or `index`: int) | Travel to a node from `next_options`. Refused (error, nothing pressed) while the map is closing, traveling, or `map.travel_enabled` is false (opened before the room is finished). |

### Event (`event`)

| Action | Parameters | When to Use |
|---|---|---|
| `choose_event_option` | `index`: int | Choose an event option by index from state. Locked options return an error. Also used for "Proceed" options. |
| `advance_dialogue` | _(none)_ | Click through Ancient dialogue until `in_dialogue` is false. |

### Rest Site (`rest_site`)

| Action | Parameters | When to Use |
|---|---|---|
| `choose_rest_option` | `option_id`: str (aliases `id`, `option`) — **preferred**; or `index`: int (alias `option_index`) | Choose rest, smith, or another option. `option_id` matches `rest_site.options[].id` case-insensitively (`HEAL`, `SMITH`, `DIG`, …). `index` is `rest_site.options[].index`, i.e. the position in the game's option list — *not* the on-screen button order. Both are resolved to the same model option and must agree if both are sent. Disabled options are refused; errors list every option as `[index] id (enabled/disabled)`. Replies with `option_id` and `index` of what was clicked. |
| `proceed` | _(none)_ | Leave the rest site. |

`rest_site.options[]` carries `index`, `id`, `name`, `description`, `is_enabled` and `has_button`
(false when the option exists in the run model but no button is drawn yet — markdown appends
`(no button on screen)` in that case, and `choose_rest_option` refuses it instead of pressing a neighbour).

### Shop (`shop`)

| Action | Parameters | When to Use |
|---|---|---|
| `shop_purchase` | `index`: int | Buy an item by its index. Must be stocked and affordable. |
| `proceed` | _(none)_ | Leave the shop. |

### Treasure (`treasure`)

| Action | Parameters | When to Use |
|---|---|---|
| `claim_treasure_relic` | `index`: int | Claim a relic from the opened chest. |
| `proceed` | _(none)_ | Leave the treasure room. |

### Card Selection Overlay (`card_select`)

Grid `cards` includes **all filtered candidates**, in full display order (top to bottom, left to right), with draw-pile ties normalized as described below.
`index` is zero-based in this list, even for cards off screen; **no scrolling is required**.
Scrolling/hover does not change indices. Re-read state after candidate changes, reordering, or screen changes.
Same-ID instances remain separate. `cards: []` means zero candidates; `cards: null` plus a
`card_select.cards` warning means the list could not be read. Do not act on an old index then.
Schema 4 removes `selected_outside_grid`; every candidate has its selection mark. A mismatch
with `selected_count` produces a warning. Combat-pile `screen_type` retains its game class name.

山札から選ぶ画面は、Rarity・Title・Id が等しい極大区間を `CompareTo == 0` の同値類に分け、
各類が占める位置へ uid の ordinal 順で配り直す。離れた同値札の引く順も意図して隠し、
行と index のクリック先は同じ対応を使う。

Grid rows (`cards[]`) include a combat-stable `uid` only while combat is in progress and its state
is available, including combat-pile selections. It uses the same card-instance registry as the
hand and piles, and is displayed in Markdown after the index. It is absent outside combat
and on `preview_cards`. Actions still use `index`.

戦闘中の choose-a-card（3 択ポーションなど）の候補行は、選ばれると手札に入る実個体として手札と同じ `uid`・`energy_cost`・`star_cost_state`・
札の詳細・`can_play`・`exhaust_on_next_play` を持ち（値は選択前。コストを 0 に加工しない）、画面に `battle` と
選択後の通知の listener の列 `card_select.continuation_context`、各行に候補自身の `continuation_listeners` が付く。
`index` は `select_card` と同じ holder の位置。候補の `energy_cost` はパイルに入る前なので global の修正を含まず、`global_modifiers_applied: false` で示す。継続の listener のうち山札にあるものは引く順を漏らさないため `order: null`（`order_group: "draw_pile"` とまとまりの開始位置）で、行は山札の表示順に並べ替える。4 通知はポーションの継続の定義で、どの画面の継続かはクライアントが親の操作から決める。[契約・出典](raw-full.md#戦闘中の-choose-a-card追加schema-4)。

戦闘中に `CardSelectCmd` が開く選択画面（`hand_select`、choose-a-card の `card_select`、grid の `NCombatPileCardSelectScreen` / `NSimpleCardSelectScreen`）には `choice_source`（schema 1.5.0）が付く。選択を開いた入口（`selector`。転送した外側の入口は `outer_selectors`、例: `FromHandForDiscard`）、`PlayerChoiceContext` の連鎖（`contexts`）と各 context の model stack を上から並べた `models`（`models[0]` が選択を開いた model: 計略の `StratagemPower`、エントロピーの `EntropyPower`、スプラッシュの札）、進行中のカードのプレイ（`card_play`: uid・`play_index` / `play_count`）、`models[0]` が走らせている通知（`hook.name`。override がちょうど 1 つで、プレイ中の札・使用中のポーション・オーブでないときだけ。違えば `name_unavailable_reason`）と、その通知の列でそれより後ろの listener（`hook.remaining_listeners`。いま読み直した列で、山札の listener の順は出さない。`models[0]` が山札の listener なら null）。MOD は `CardSelectCmd` の入口に読むだけの Harmony の patch を当てて引数を記録する。分からないときは `known: false` と理由。Markdown は `Choice source: …` の 1 行。[契約・出典](raw-full.md#選択を開いた元choice_sourceschema-150)。

同じ戦闘中の grid 行には `exhaust_on_next_play`（`CardModel.ExhaustOnNextPlay`）も出し、
true のときだけ Markdown に `[exhaust-next]` を付ける。戦闘外には出さない。
`energy_cost` も同じ条件で出す（[値・修正一覧・失敗時の契約](raw-full.md#energy-cost-object追加schema-4)）。

| Action | Parameters | When to Use |
|---|---|---|
| `select_card` | `index`: int (alias `card_index`), optional `expected_uid`: string | Grid: toggle any full-list candidate without scrolling. Choose-a-card: pick immediately. With `expected_uid` (schema 10; rows have uids only in combat) the candidate at `index` must have that uid; a mismatch never presses. It is checked before revealing a hidden screen and again after revealing; if the second check fails, nothing is pressed but the screen stays revealed and the error carries `revealed`. |
| `confirm_selection` | _(none)_ | Open/apply a deck preview, or apply a grid with manual confirmation. Not needed for immediate completion or choose-a-card. |
| `cancel_selection` | _(none)_ | Cancel preview, skip (choose-a-card), or close screen. |

On the upgrade screen (`screen_type: "upgrade"`), each card with an upgrade left carries `upgrade_preview` (its upgraded form). The transform/remove screens do not.

**Selection state.** Every grid screen reports what it currently holds selected, so a caller never has to
count its own presses:

| Field | Meaning |
|---|---|
| `cards[].selected` | Whether that card is in the screen's selection. Stays `true` while a preview is open, where the game drops the grid highlight but keeps the card. |
| `selected_count` | How many cards the screen has selected. |
| `required_count` | The `N` of a "select N cards" prompt, or `null` when the screen accepts a range. |
| `min_select` / `max_select` | The two ends of that range (equal when `required_count` is set). |

**Deck screens with previews have two steps.** Picking cards comes first; then a preview panel opens - by itself once
`max_select` is reached, or when `confirm_selection` presses the screen's own confirm - and only the preview's
own confirm applies the selection. `confirm_selection` says which of the two it did
(`"Selection preview opened … call confirm_selection again to apply it"` vs `"Confirming selection…"`), and
`preview_showing` says where the screen stands.
Simple / combat-pile grids follow their preferences: manual confirm applies the selection, or
selecting enough cards completes immediately. Choose-a-card also picks immediately.

While a preview is open the game takes the grid out of the input path, so `select_card` returns `status: error`
rather than pressing through it (pressing through appends duplicate cards to the preview row).
`cancel_selection` closes the preview **and clears the selection**, putting you back on an empty grid.
**A screen can be on top and still hidden.** A selection opened while the map is open (for example by
the dev console's `relic add` on the map) is pushed hidden under the map, and in combat the peek button hides
the grid. `state_type` still says `card_select`; `screen_visible: false` and `hidden_by` (`map`, `peek`,
`capstone`, `unknown`) say so. When `reveal_on_action` is true, `select_card`, `confirm_selection` and
`cancel_selection` first undo the cover the player's way — the map's own back button (only while it is
enabled and the map is not traveling), or the peek button's `SetPeeking(false)` — and report it in
`revealed` (`["map"]`, `["peek"]`). Closing the map shows the room underneath again; after the selection,
leave that room as usual (e.g. `proceed`). Otherwise the action returns error without pressing anything.
An invalid index, unreadable candidates, missing click method, a screen that stays hidden, or a rejected toggle
returns error. If the click throws, re-read state before acting again: it may already have changed.
The MOD calls the game's model selection path once and does not mutate selection sets itself.

`confirm_selection` returns `status: error` - instead of an `ok` that does nothing - when the selection is
short of `min_select`, over `max_select`, or when the press was swallowed by the screen.

### Bundle Selection Overlay (`bundle_select`)

| Action | Parameters | When to Use |
|---|---|---|
| `select_bundle` | `index`: int | Open a bundle preview. |
| `confirm_bundle_selection` | _(none)_ | Confirm the previewed bundle. |
| `cancel_bundle_selection` | _(none)_ | Cancel the bundle preview. |

### Relic Selection Overlay (`relic_select`)

| Action | Parameters | When to Use |
|---|---|---|
| `select_relic` | `index`: int | Pick a relic (immediate). |
| `skip_relic_selection` | _(none)_ | Skip the relic choice. |

### Crystal Sphere (`crystal_sphere`)

| Action | Parameters | When to Use |
|---|---|---|
| `crystal_sphere_set_tool` | `tool`: `"big"` or `"small"` | Switch divination tool. |
| `crystal_sphere_click_cell` | `x`: int, `y`: int | Reveal a cell. |
| `crystal_sphere_proceed` | _(none)_ | Finish the minigame. |

## Multiplayer Additions

`POST /api/v1/multiplayer` supports all singleplayer actions plus:

| Action | Parameters | When to Use |
|---|---|---|
| `end_turn` | _(none)_ | Vote to end turn. Turn ends when all players vote. |
| `undo_end_turn` | _(none)_ | Retract end-turn vote (before all players committed). |
