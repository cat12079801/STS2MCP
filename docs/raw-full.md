# Full API Reference

HTTP API served by the STS2_MCP mod on `localhost:15526`. No authentication. Local use only.

**Endpoints:**
- `GET  /` — health check: `{ "message", "status", "version", "commit", "schema", "schema_version", "scenario_capabilities" }`
- `GET  /api/v1/singleplayer` — read current game state
- `POST /api/v1/singleplayer` — perform a game action
- `GET  /api/v1/multiplayer` — read multiplayer game state
- `POST /api/v1/multiplayer` — perform a multiplayer action
- `GET  /api/v1/profile` — read current profile progress
- `GET  /api/v1/compendium` — read Compendium-shaped profile progress
- `GET  /api/v1/wiki` — fuzzy-search discovered card/relic wiki entries
- `GET  /api/v1/profiles` — list profile slots
- `POST /api/v1/profiles` — switch or delete profile slots

The endpoints are mutually exclusive: calling singleplayer during a multiplayer run (or vice versa) returns HTTP 409.

## HTTP 要求の制限

HttpListener を通過した全ルート（ヘルスチェック・未知のパスを含む）で、本文の読み取り・ゲーム状態の参照・
メインスレッドへの配送より前に次を順番に検査する。

| 条件 | HTTP | エラー |
|---|---|---|
| `Origin` ヘッダーが存在する | 403 | `Requests with an Origin header are not allowed` |
| `Sec-Fetch-Site` / `Sec-Fetch-Mode` / `Sec-Fetch-Dest` のいずれか、または `Referer` が存在する | 403 | `Requests with Fetch Metadata or Referer headers are not allowed` |
| `Host` が `127.0.0.1:<port>` / `localhost:<port>` 以外 | 403 | `Host must be 127.0.0.1 or localhost with the configured port` |
| OPTIONS | 405 | `Method not allowed` |
| POST の Content-Type が `application/json` 以外 | 415 | `POST requests require Content-Type: application/json` |

`<port>` はサーバーの設定ポート（`STS2_MCP.conf` の `port`、既定 15526）。
Host の名前の大文字小文字は区別しないが、ポート省略・別ポート・別名・IPv6 は許可しない。
macOS（マネージド実装）で確認: `example.invalid:<port>`・`localhost.:<port>`・
`127.0.0.1.:<port>` は HttpListener が先に 404、`[::1]:<port>` や HTTP/1.1 の Host 未指定は
400 を返す。これらは HTML 応答で、MOD の JSON エラー形式ではない。
リスナーを通過するポート省略・別ポート・`127.1:<port>` などは MOD が 403 で拒否する。
Content-Type は大文字小文字を区別せず、`; charset=utf-8` などのパラメーターを許可する。
`application/jsonp` などの類似型、未指定、`text/plain`、フォーム形式は拒否する。
GET には Content-Type を要求しない。

MOD による拒否時は既存の JSON エラー形式（`status: "error"` と `error`）を返し、
`?pretty=1` に対応する。例:

```json
{"status":"error","error":"Requests with an Origin header are not allowed"}
```

`Access-Control-Allow-*` は成功・失敗とも一切返さない。Origin は値を問わず拒否するため、
`Origin: null`（`file://`）、空値、localhost の Origin も 403。
Fetch Metadata と Referer も値を問わず、空値や same-origin の要求でも拒否する。
これにより Origin のない `<img>` や no-cors fetch の GET も、これらのヘッダーがあれば
状態構築より前に 403 となる。OPTIONS を含む複数の
違反がある場合は先に該当する規則が優先される。ブラウザからの直接利用と CORS preflight は
サポートしない。Origin の許可リスト設定は設けない。既存の urllib・curl・Python MCP サーバーの
httpx はこれらのブラウザ用ヘッダーを送らず、POST に JSON の Content-Type を付けるため、そのまま利用できる。
これはブラウザ由来の要求と DNS rebinding への対策であり、ローカルクライアントの認証ではない。

---

## GET — Read Game State

### Query Parameters

| Parameter | Values                                        | Default | Description                                                     |
|-----------|-----------------------------------------------|---------|-----------------------------------------------------------------|
| `format`  | `json`, `markdown`                            | `json`  | Response format                                                 |
| `pretty`  | `1`, `true`, `yes` (or present with no value) | off     | Indent the JSON. Off by default — compact output is ~32% smaller |
| `passive` | `1`, `true`, `yes`                            | off     | Read without UI side effects (no auto-opened merchant inventory, no fake-merchant / chest press; `shop.sellable_potions` is answered without closing the inventory — schema 11) |
| `scenario_setup_guard` | `1`, `true`, `yes`               | off     | With `passive` only: add [`scenario_setup_guard`](#戦闘外の準備の判定scenario_setup_guardschema-10) (verification runs) |
| `combat_log_since` | a `seq` (non-negative integer)               | —       | `battle.combat_log` carries every kept row from that `seq` on instead of the last `default_window` rows ([戦闘の出来事の通し列](#戦闘の出来事の通し列battlecombat_logschema-1100), schema 1.10.0) |

JSON responses are compact by default; `pretty` opts into indentation for that one request. It works on
every JSON-returning endpoint, on POST as well as GET (e.g. `POST /api/v1/singleplayer?pretty=1`), and on
error responses. It has no effect on `format=markdown`.

### Common Top-Level Fields

Every state response (anything with a `state_type`: `menu`, the multiplayer `error` state, and the `state` a
`wait: true` POST returns included) carries the build stamp below; `GET /` carries `version` / `commit` / `schema` /
`schema_version` / `scenario_capabilities`. Other responses do not: the 500 of a failed state read, the 503
`retry: true`, POST action results and errors, and the profile / compendium / wiki endpoints.

```jsonc
{
  "state_type": "...",      // Screen identifier (see sections below)
  "mod_version": "0.4.0",   // Mod version
  "mod_commit": "2de67cd",  // Git commit built from ("<sha>-dirty" from an edited tree); null if unknown
  "game_version": "v0.113.0", // release_info.json version; null if unavailable
  "game_commit": "16ac0fe3",  // release_info.json commit; null if unavailable
  "schema": "1.13.0",       // Contract version (semver) — see below
  "schema_version": 12,     // Deprecated integer, frozen at 12 — see below
  "scenario_capabilities": { "setup_guard": 1, "selection_uid": 1, "potion_availability": 1 }
}
```

`scenario_capabilities` names the verification contracts this build implements, as contract versions
(`0` = not implemented): `setup_guard` = the passive [`scenario_setup_guard`](#戦闘外の準備の判定scenario_setup_guardschema-10)
and the guarded `dev_console`; `selection_uid` = `hand_select` rows / `selected_cards` by uid at the holder index
and `expected_uid` on `combat_select_card` / `select_card`; `potion_availability` = the potion can-use-now contract
([`player.potions[]`](#ポーションをいま使えるかplayerpotionsschema-11) and `use_potion`'s `expected_potion_id`, `cat12079801/sts2#1012`). It is a build constant on every state (and `GET /`). A client gates
on it, not on the schema number: an older build ignores unknown request parameters, so a guard or `expected_uid`
sent to one would run unchecked.

`schema` is the contract version as semantic versioning (`MAJOR.MINOR.PATCH`, currently `"1.13.0"`). This section is
the canonical statement of the rules; the other documents point here.

- **MAJOR** — a backward-incompatible change: an existing field's meaning, type or shape changes; a field, action, enum
  value or `state_type` is removed or renamed; or the structure changes (a field moves, an object becomes a list, a
  per-screen object is split or merged). A client requires only the MAJOR it was written against.
- **MINOR** — a backward-compatible addition: new fields, actions, optional parameters or `state_type`s, and new values
  of an existing enum-like field (`state_type`, `resolving_reasons`, `upgrade_preview.status`, `can_use_now_reason`,
  `unavailable_reasons` codes and the like). Clients detect added features by field presence and the existing
  `unavailable_reasons` / `warnings` conventions, not by comparing versions. **A client must treat a value it does not
  know as unobserved** — stop or fall back on the safe side — and never map it to a known value.
- **PATCH** — a fix that does not change the contract: a value that was wrong per the documented meaning becomes right,
  or a docs / formatting fix.

Partial contracts that carry their own version — `generation_pool.filter_version`, each value of
`scenario_capabilities`, and the recorder's `sts2-mod-record/N` — are judged by that version, not by `schema`. A change
to one of them still moves `schema` by the ordinary rules for what changed: adding fields or a new capability is MINOR,
and changing the meaning or shape of an existing field is MAJOR.

`1.0.0` is exactly the contract of `schema_version` 12. The Markdown header shows both:
`mod 0.4.0+<commit> · schema 1.13.0 (schema_version 12)`. Since `1.0.0`:

- **1.1.0** — `battle.enemies[].combat_state` covers GremlinMerc (`has_spoken`) and KinPriest (`speech_used`)
  (`cat12079801/sts2#1065`). See [敵の台詞の印](#敵の台詞の印schema-110).
- **1.2.0** — rows carry `internal_state`, the internal state the state inventory classified as public, read through an
  allowlist generated from `docs/state-inventory-classification.json` (card, power, relic, player and battle rows). See
  [棚卸しから生成した内部状態](#棚卸しから生成した内部状態internal_stateschema-120).
- **1.3.0** — every power row's `internal_state` carries `applier_combat_id` (`PowerModel._applier`) and
  `target_combat_id` (`PowerModel._target`), whatever the power's subclass. The Hex row's own `applier_combat_id` /
  `applier_readable` are a copy of that value. See [棚卸しから生成した内部状態](#棚卸しから生成した内部状態internal_stateschema-120).
- **1.4.0** — every selection state (`state_type` ending in `_select`) shown while a fight is in progress carries
  `battle`, built the same way as the fight's own screen: the grid `card_select` (`NCombatPileCardSelectScreen` of
  Stratagem and the like), `bundle_select` and `relic_select` too, not only `hand_select` and the choose-a-card screen.
  See [戦闘中の選択画面の battle](#戦闘中の選択画面の-battleschema-140).
- **1.5.0** — the public inputs of four colorless cards (`cat12079801/sts2#1084`):
  `player.combat_history.card_plays_finished_this_combat` (Gold Axe), `player.other_character_attack_generation_pool`
  (Splash), `player.transformation_options` (Entropy; only in a fight with Entropy) and `choice_source` on the in-combat
  `hand_select` / `card_select` screens that `CardSelectCmd` opens (who opened the selection, the card play it belongs
  to, the hook the top model runs and the listeners left in it; Stratagem, Entropy, Splash). See
  [スプラッシュの生成候補](#スプラッシュの生成候補playerother_character_attack_generation_poolschema-150filter_version-2),
  [エントロピーの変化の候補](#エントロピーの変化の候補playertransformation_optionsschema-150) and
  [選択を開いた元](#選択を開いた元choice_sourceschema-150).
- **1.6.0** — `player.combat_history.card_resolution_events`: the owner's draw, play (started / finished) and
  generated-card entries of the whole combat, in History order, across the end of a turn, the enemy turn and the next
  turn's start (`cat12079801/sts2#1088`). See [札の解決の通し列](#札の解決の通し列card_resolution_eventsschema-160).
- **1.7.0** — `battle.enemies[].hp_display_is_infinite`: the game's `HpDisplayExtensions.IsInfinite` over
  `Creature.HpDisplay` on every enemy row (HellraiserPower's per-turn cap, HardenedShellPower reaching its cap,
  WaterfallGiant's death continuation; `cat12079801/sts2#1088`). See [HP の無限表示](#hp-の無限表示hp_display_is_infiniteschema-170).
- **1.8.0** — `card_resolution_events` に `CardPileCmd.AutoPlayFromDrawPile`（カスケード・荒廃・騒乱など）の予約を出す。
  呼出しごとの `auto_play_reservation`（呼出し ID・親の呼出し ID・親のモデル・親プレイの開始行の添字）、Play に移した札ごとの
  `auto_play_reserved`（確保の順の添字と uid）、`CardCmd.AutoPlay` に渡した札ごとの `auto_play_dispatch` の行と、play の行の
  `reservation`。単独の POST の応答には、その操作の間に終わった戦闘の最後の列を `ended_combat` で付ける（`cat12079801/sts2#1113`）。
  [自動プレイの予約](#自動プレイの予約card_resolution_events-の予約の行schema-180) と [戦闘を終えた操作の応答](#戦闘を終えた操作の応答ended_combatschema-180)。
- **1.9.0** — `choice_source.await_chain`: 開いている選択を待っている async の呼出しを、いま止まっている Task から内側から外へ
  読んだ鎖（`cat12079801/sts2#1115`）。`models[0]` を走らせている `Hook.AfterPlayerTurnStart` / `AfterShuffle` の段・その段が
  始まったときの列での位置と後ろに残る listener、シャッフルとそれを起こしたドロー（残りの枚数・手番の初めのドローか）または
  自動プレイの予約（`card_resolution_events` の `call_id`）、手番の初め（`SetupPlayerTurn`・`RunAutoPrePlayPhase`）と、
  それぞれの後に続く段（エントロピー・計略）。[選択を待つ処理の鎖](#選択を待つ処理の鎖choice_sourceawait_chainschema-190)。
- **1.10.0** — 出来事の棚卸し（[`docs/event-inventory.md`](event-inventory.md)）で public-missing だった 128 件を出す。`battle.combat_log`
  （戦闘の出来事の通し列: 通知と上書きしている listener・履歴の全種・山の移動・シャッフル・札／敵／HP／ブロック／エナジー／ポーション／
  パワーの命令・`Modify*` の結果と修正したモデル・光った効果・手番の段階・選択の結果。`?combat_log_since=`）、全選択画面の
  `decision_context`（入口・開いた元・開いた時点の seq・`choice_source`）、`player.potion_generation_pool`、手札の
  `energy_cost.x_value_if_played`、`ended_combat.combat_log`。[戦闘の出来事の通し列](#戦闘の出来事の通し列battlecombat_logschema-1100)・
  [選択の文脈](#選択の文脈decision_contextschema-1100)・[ポーションの生成候補](#ポーションの生成候補playerpotion_generation_poolschema-1100)。
- **1.11.0** — `battle.enemies[].can_receive_powers`: 敵のすべての行に本体の `Creature.CanReceivePowers`（`PowerCmd.Apply` が
  付与の前に見る条件）の bool を出す。クイーンの「燃え上がれ」が味方に筋力を付けるか（`cat12079801/sts2#1175`）。
  [パワーを付けられるか](#パワーを付けられるかcan_receive_powersschema-1110)。
- **1.12.0** — 戦闘の札の行（手札・4 パイル）の `internal_state.deck_version_id`（`CardModel._deckVersion`。戦闘の開始時にデッキから
  写した札は元の札のモデル id、戦闘中に作られた札は null）と、`draw_pile[]`・`discard_pile[]`・`exhaust_pile[]`・`play_pile[]` の
  行の `rarity`（手札と同じ `CardModel.Rarity` の列挙名）。泥棒バッタの窃盗（`ThievingHopper.ThieveryMove`）が盗む候補を決める入力
  （`cat12079801/sts2#1133`）。[棚卸しから生成した内部状態](#棚卸しから生成した内部状態internal_stateschema-120)・
  [戦闘カードの詳細](#戦闘カードの詳細追加schema-4)。
- **1.13.0** — エントロピー醸造の抽選前の候補・枠ごとの条件付き使用可否・通知の受け手を `player.entropic_brew_pool` に、使用履歴を `player.combat_history.potion_uses` に出す。パエルの歯を所持するランは `run.internal_state.modifier_ids` を持つ。
  [エントロピー醸造](#エントロピー醸造playerentropic_brew_poolschema-1130)・[ランの修飾子](#パエルの歯のあるランの修飾子runinternal_statemodifier_idsschema-1130)。

`schema_version` is **deprecated**: the integer is frozen at `12` and no longer changes, and it is removed in the next
MAJOR (`2.0.0`) once sts2 reads `schema`. Its history: it was bumped when an existing field or action changed meaning or
was removed, and also for **6**, **7**, **8**, **9**, **10**, **11** and **12**: additive contracts that a client treated as
required as a whole, gated on the version instead of probing each field. **10** (`cat12079801/sts2#1037`, the verification-capture
contract) also changes two `hand_select` meanings: `cards[].index` is now the `NPlayerHand.ActiveHolders` position that
`combat_select_card` presses (a holder without a card leaves a gap instead of shifting later rows), and `selected_cards`
is always present (`[]` when none, `null` when unreadable) and lists the hand's `_selectedCards` (the upgrade preview's card
included) instead of the selected-card container's holders.

`game_version` and `game_commit` come from the game's `ReleaseInfoManager`, which reads
`release_info.json` (`.app/Contents/Resources/release_info.json` on macOS). They identify the
running game's release, independently of the mod build. Singleplayer, multiplayer and menu
states carry them; markdown also shows the game stamp. Dump consumers should copy these
fields into their own `meta.json` (dump metadata is not written by this mod).

- **12** — the power / relic amount input contract (`cat12079801/sts2#1020`). Every status row (player, enemy, pet;
  `hidden_in_ui` and minimal rows too) carries `dynamic_vars`: the power's own `DynamicVars` as
  `{ name: BaseValue }` (`StringVar` left out; `{}` for a power without vars; `null` with a reason when the set
  cannot be read). Powers whose state lives in private Data / fields (Rupture, Calamity, Strangle, Hellraiser,
  Automation, Dark Embrace, Gigantification, Hardened Shell, Adaptable / Reattach / Illusion, Nemesis, Sloth,
  Tender, Beacon of Hope, Swipe) carry per-type keys. Relics: `combat_state` gains the per-turn counts of Kunai,
  Shuriken, Ornamental Fan, Kusarigama, Letter Opener and Pocketwatch and Orichalcum's `should_trigger`; saved
  counters that outlive a combat are `relics[].run_state`, in and out of combat. `relics[].vars` is read from
  `RelicModel._dynamicVars` and is now `null` with `unavailable_reasons.vars` when unreadable (it used to be
  omitted) — the one change of meaning. The per-type keys (`damage`, `self_damage`, `cards_left`,
  `block_base_value`, `stolen_gold`) keep their meaning. See [パワーの変数](#パワーの変数dynamic_varsschema-12),
  [レリックの保存カウンタ](#レリックの保存カウンタrun_stateschema-12).
- **11** — the potion can-use-now contract (`cat12079801/sts2#1012`). Every held `player.potions[]` row
  carries `can_use_now`, `valid_target_entity_ids` and `can_use_now_reason`; `use_potion` takes
  `expected_potion_id`; `scenario_capabilities.potion_availability` is `1`; a `?passive=1` read no longer
  closes the shop inventory to fill `shop.sellable_potions`. Existing fields keep their meaning (additive);
  the version is bumped so a client can require the whole contract. See
  [ポーションをいま使えるか](#ポーションをいま使えるかplayerpotionsschema-11).
- **10** — the verification-capture contract (`cat12079801/sts2#1037`, described above).
- **9** — the Rend input contract (`cat12079801/sts2#1034`). Every status row (player, enemy, pet;
  `hidden_in_ui` and minimal rows too) carries `type_for_current_amount` (`PowerModel.TypeForCurrentAmount`:
  a negative Strength / Dexterity / Focus reads `Debuff` while `type` stays `Buff`) and
  `is_temporary_power` (the instance implements `ITemporaryPower`). Existing fields keep their meaning
  (`type` is still `PowerModel.Type`); the version is bumped so a client can require the whole
  contract. See [破壊の入力](#破壊rendの入力schema-9).
- **8** — the Dampen input contract (`cat12079801/sts2#1031`). `DAMPEN_POWER` status rows carry
  `caster_combat_ids` (the casters in private `DampenPower.Data.casters`, as `enemies[].combat_id`)
  and `downgraded_cards` (Data's card → original upgrade level map, as combat uids; a card the uid
  registry has not seen is reported per row with `uid_known: false`, never given a new uid).
  Existing fields keep their meaning (additive); the version is bumped so a client can require the
  whole contract. See [弱化の入力](#弱化dampen_powerの入力schema-8).
- **7** — the card-effect input contract (`cat12079801/sts2#1019`). Every combat card row (hand, the
  four piles, choose rows) and the `ready` combat `upgrade_preview` carry `retain_this_turn`,
  `single_turn_retain`, `sly_this_turn`, `single_turn_sly`, `base_replay_count`,
  `extra_replay_this_turn` and `enchanted_replay_count` (Mad Science rows also `tinker_time_rider`);
  `enchantment` objects carry their `dynamic_vars`; The Bomb, Inferno, Crimson Mantle and Panache
  status rows carry `damage` / `self_damage` / `cards_left` and `already_applied`; in-combat selection
  rows (`card_select`, choose, `hand_select`, a `card_reward` during combat) carry `combat_instance`.
  Existing fields keep their meaning (additive); the version is bumped so a client can require the
  whole contract. See [札の効果の入力](#札の効果の入力schema-7) and
  [選択候補の個体](#選択候補の個体combat_instanceschema-7).
- **6** — the enemy-move input contract (`cat12079801/sts2#1018`). `battle.enemies[].move_state`
  gains the full move log, the state machine's current/initial state, the first-move and
  spawned-this-turn flags, the terminal-move flag and the `CreatureCmd.Stun` handler; every enemy
  row gains `slot_name`; `battle` gains `enemy_move_context`; `combat_state` covers more enemies;
  Ritual rows carry `was_just_applied_by_enemy`. Existing fields keep their meaning (additive);
  the version is bumped so a client can require the whole contract. See
  [敵の行動の入力](#敵の行動の入力schema-6).
- **5** — every combat card row (`player.hand[]` and the draw/discard/exhaust/play piles) always
  carries `upgrade_preview` with a `status` (`ready` / `not_upgradable` / `unavailable`).
  It used to be a hand-only display object attached only while an upgrade card was in reach,
  absent alike for "cannot upgrade" and "failed". `ready` now holds the upgraded clone's
  machine-readable absolute values. Out-of-combat previews keep their shape. See
  [戦闘の強化結果](#戦闘の強化結果upgrade_previewschema-5).
- **4** — `card_select.cards` and `select_card` indices cover all grid candidates in display order,
  independent of scrolling. `selected_outside_grid` was removed.
- **3** — `map.next_encounters` was removed because it exposed future rooms before the player
  could see them. `map.encounters_seen` reports only travelled encounters.
- **2** — `battle.enemies` is no longer "the living enemies": a corpse the game keeps in the
  fight (a Decimillipede segment waiting to reattach) is listed with `alive: false`. Filter on
  the new `alive` field instead of assuming every entry is a valid target.
- **1** — initial contract.

Every response except `menu` additionally includes these top-level fields alongside the
state-specific data:

```jsonc
{
  "state_type": "...",      // Screen identifier (see sections below)
  "run": {
    "act": 1,               // Current act (1-indexed)
    "floor": 3,             // Total floors visited
    "player_count": 1,
    "card_multiplayer_constraint": "SingleplayerOnly",
    "ascension": 0          // Ascension level
  },
  "player": { ... },        // Full player state (see Player Object below)
  // ... state-specific fields
}
```

### 解決中の判定（`is_resolving` / `resolving_reasons`）

単独プレイの state は `is_resolving`（bool）を必ず持ち、true のときだけ `resolving_reasons`（文字列の配列）を足す。
true の間の手札・エナジー・敵の HP・`combat_history` などは解決途中の値で、計画には false の snapshot を使う。
markdown では先頭に `STILL RESOLVING (理由)` の警告が出る。理由は次のとおり（複数が同時に出ることがある）。

| 理由 | 立つ条件（ゲーム側の値） |
|---|---|
| `action_queue_not_empty` | `ActionQueueSet` が空でない。ただし選択待ちで止まっている action だけなら立てない |
| `player_actions_disabled` | `CombatManager.PlayerActionsDisabled`。手番の終了（`PlayerCmd.EndTurn` → `OnEndedTurnLocally`）から、次の手番の `Phase` が `Start` になるまで |
| `combat_ending` | 戦闘中は `CombatManager.IsOverOrEnding`（敵が全滅して `EndCombatInternal` が走る前など）。戦闘が終わってから報酬画面などが出るまでの `battle` の無い state でも立つ（下の「戦闘の前後」） |
| `combat_starting` | 戦闘の部屋に入ってから戦闘が始まる（`CombatManager.IsInProgress` が立つ）までの `battle` の無い state（下の「戦闘の前後」） |
| `player_turn_starting` | 戦闘の画面（`monster` / `elite` / `boss`）で、手番の初め（ブロックの消去・エナジーの再設定・手札のドロー・手番開始時のフック）が終わっていない |
| `card_still_being_played` | `NPlayerHand.InCardPlay` |
| `reward_selection_resolving` | 報酬・カード報酬の選択を送った後、完了の合図が出るまで |
| `map_traveling` | `map` の state で、マップがノードへ移動している（下の「画面の遷移」） |
| `map_closing` | `map` の state で、閉じたマップが閉じる演出の間まだ見えている（下の「画面の遷移」） |
| `room_changing` | ラン中の `unknown` の state で、`RunManager.IsChangingRooms`（前の部屋を出て次の部屋に入るまで。再開したランが保存の部屋に入るときも） |
| `run_loading` | ラン中の `unknown` の state で、ランの状態はあるが部屋がまだ無い（`continue` や新しいランの読み込み中） |

**手番の初め（`player_turn_starting`）。** ゲームの `CombatManager.StartTurn` は、Player の手番に入ると
`PlayerCombatState.Phase` を `Start` にしてすぐ `PlayerActionsDisabled` を false に戻し、ドローを含む
`SetupPlayerTurn`（`ResetEnergy` → `BeforeHandDraw` → `CardPileCmd.Draw` → `AfterPlayerTurnStart`）を
action の列の外で進める。このため `action_queue_not_empty` と `player_actions_disabled` は、手札が増えている途中でも立たない。
`player_turn_starting` は、戦闘中（開始中・終了中を除く）で `CurrentSide` が Player のとき、次のどちらかで立つ。

- ローカルの Player（生きている）の `PlayerCombatState.Phase` が `None` / `Start` / `AutoPrePlay`。
  `RunAutoPrePlayPhase` の最後で `Play` になるまでで、手番の初めの選択に答えた後に残りのドローやフックが続く間も含む
- `ActionQueueSynchronizer.CombatState` が `NotPlayPhase`（ゲームの定義で「敵の手番と、Player の手番のドローの局面」）。
  `PlayPhase` へ戻るのは `StartTurn` の最後（選択で止まらなければ、全員の `AutoPrePlay` と `CheckWinCondition` の後）なので、`Phase` が `Play` になってから列が開くまでの間も覆う

`Phase` が `AutoPostPlay` / `End`（手番の終わり）のときは立てない（`player_actions_disabled` が受け持つ）。
死んだローカルの Player は `Phase` が `Start` のまま手番を過ごすので、`Phase` では判定せず同期器だけを見る。この判定は単独プレイの state 専用で、協力プレイ（自分が参加しない追加の手番など）は扱わない。
手番の初めのフックが開いた選択画面（`hand_select` / `card_select` など）は、下の規則で入力待ちとして false になる。
未対応のオーバーレイ（`overlay`）にはこの理由を付けない。

**戦闘の前後（`combat_starting` / `combat_ending`）。** 戦闘の部屋で `CombatManager.IsInProgress` が false で、
マップ・報酬・ゲームオーバー・選択画面などが前に出ていないとき、state は `state_type` が `monster` / `elite` / `boss` で
`battle` を持たず、`message` と次の理由で解決中（`is_resolving: true`）を返す。ゲームの値は公開のものだけを読む。

| 局面 | 理由 | `message` | ゲームの値（デコンパイル `sts2-a671e9ef`） |
|---|---|---|---|
| 部屋に入ってから戦闘を組むまで | `combat_starting` | `Combat is starting. Waiting for the first turn...` | `RunManager.EnterRoomInternal` が部屋を積んだ後、`CombatRoom.StartCombat` が `CombatManager.SetUpCombat` を呼ぶ前。ターンの状態が無い（`CurrentCombatId` が null）で、部屋は終わっていない（`CombatRoom.IsPreFinished` が false） |
| 戦闘を組んでから始まるまで | `combat_starting` | 同上 | `CombatManager.IsStarting`（`SetUpCombat` で true、`StartCombatInternal` が `IsInProgress` と同時に false にする） |
| 勝ってから報酬画面が出るまで | `combat_ending` | `Combat ended. Waiting for rewards...`（報酬の無い戦闘では `Combat ended. This encounter gives no rewards; waiting to leave the fight...`） | `EndCombatInternal` が `IsInProgress` を false にし、後で `CombatRoom.MarkPreFinished`。`NCombatUi.OnCombatWon` が死亡の演出を待って `NRewardsScreen` を出す（`EncounterModel.ShouldGiveRewards` が false なら `ProceedWithoutRewards` で 1 秒後にマップか元のイベントへ） |
| 保存から再開した終わった戦闘で、報酬画面が出るまで | `combat_ending` | 同上 | `CombatRoom.StartPreFinishedCombat`。ターンの状態を作らず、報酬を出し直す |

ターンの状態があって、開始中でも戦闘中でもなければ、その戦闘は終わっている（`CombatManager.CancelCombat` がターンの状態を消すのは
部屋を出るとき〔`RunManager.ExitCurrentRoom` が部屋を降ろした後〕だけ）。負けたとき（`ProcessPendingLoss`）は、
`CreatureCmd.Kill` が先にゲームオーバーの画面を積むので `game_over`（入力待ち）が返る。
行き先（報酬・カード報酬・マップ・元のイベント・ゲームオーバー）はどれも入力待ちの画面で、この分岐より前で判定するので、
ゲーム側の遷移が例外で止まった場合（ゲーム自体が進まない）を除き、理由がそのまま残って待つ側が行き詰まることはない。読み手は待ちに時間の上限を持つ。
戦闘中（`IsInProgress` が true）の判定に `IsStarting` は使わない（`StartCombatInternal` が `IsInProgress` と同時に `IsStarting` を false にするので立たない）。
`message` は協力プレイの state でも同じ文言を返す（協力プレイの state は `is_resolving` を持たない）。

**画面の遷移（`map_traveling` / `map_closing` / `room_changing` / `run_loading`）。** 入力を受け付けない遷移の途中を、
入力待ちの画面や落ち着いた state と取り違えないための理由。ゲームの値（デコンパイル `sts2-a671e9ef`）は次のとおり。

| 局面 | 理由 | ゲームの値 |
|---|---|---|
| `choose_map_node` の後、移動の演出と暗転の間（`map` のまま約 1.2 秒） | `map_traveling` | `NMapScreen.OnMapPointSelectedLocally` が `VoteForMapCoordAction` を積み、その実行（`MapSelectionSynchronizer.PlayerVotedForMapCoord` → `MoveToMapCoord`）が `MoveToMapCoordAction` を積む。後者が `NMapScreen.TravelToMapCoord` を待ち、そこで `IsTraveling = true` と `IsTravelEnabled = false` を同時に立て、経路の演出と暗転の後に `RunManager.EnterMapCoord` → `EnterMapPointInternal` へ進む。`IsTraveling` が true で `IsTravelEnabled` が false の間か、この 2 つの action が実行中か（戦闘の外で）列にある間 |
| マップを戻る矢印・トップバーのボタンで閉じた直後（約 0.35 秒） | `map_closing` | `NMapScreen.Close(animateOut: true)` は `IsOpen` をすぐ false にし、`AnimClose` の tween（遅延 0.1 秒＋0.25 秒）が終わってから `Visible` を false にする。`IsOpen` が false で画面が見えている間 |
| 移動・部屋の切り替えで、前の部屋を出てから次の部屋の画面が出るまで（`unknown`） | `room_changing` | `RunManager.IsChangingRooms`（`EnterMapPointInternal` などの開始から `finally` まで。`ExitCurrentRooms` の後は部屋が無い） |
| 再開（`continue`）・新しいランの読み込み中（`unknown`・`room_type: null`。約 2 秒） | `run_loading`（保存の部屋に入る段階は `room_changing`） | `NMainMenu.OnContinueButtonPressedAsync` が `RunManager.SetUpSavedSingleplayer` でランの状態を先に置き（`IsInProgress` が true になる）、暗転・`NGame.LoadRun`（資源の読み込み・`NRun` の生成・地図の生成）の後で `LoadIntoLatestMapCoord` が部屋に入る。`RunState.CurrentRoom` が null で `IsChangingRooms` でない間 |

- 移動が部屋に入ると、`EnterMapPointInternal` が `ClearScreens` でマップを演出なしで閉じ、`EnterRoom` の後に `IsTraveling = false` にする。
  その後は部屋の state（戦闘の開始中なら `combat_starting`、イベントなら入力待ち）になる
- 理由が残って行き詰まらないための条件: `map_traveling` は `IsTravelEnabled` が false の間だけ `IsTraveling` を数える。
  次の部屋の終わり（`RunManager.Proceed` → `SetTravelEnabled(true)`）で移動が有効に戻ったマップは、人がクリックで移動できる
  （`NMapPoint.IsTravelable` は `IsTravelEnabled` と点の状態だけを読む）ので、移動が途中で止まって `IsTraveling` が残っても、そのマップを解決中にしない。
  戦闘中は `MoveToMapCoordAction`（`NonCombat`）が戦闘の終わりまで待つ（`ActionQueueSet.GetReadyAction`）ので、列にあるだけでは数えない。
  `map_closing` は `Open` が tween を止めて `IsOpen` を戻すか、tween の終わりで画面が消えるので続かない。
  `run_loading` は読み込みに失敗すると `RunManager.CleanUp` がランの状態を消す（`menu` に戻る）
- 部屋の終わりに開いたマップ（入力待ち）と、トップバーで開いたマップ（部屋が終わっていない）には付けない。選択画面・イベント・報酬・メニュー・
  ゲームオーバーなどほかの入力待ちの画面にも付けない。マップに隠れた選択画面（`card_select` の `hidden_by: "map"`）は、矢印で表に戻した直後も
  overlay の判定がマップより先なので `card_select` で返る。選択が終わって画面が閉じ、マップの閉じる演出がまだ続いていれば `map`（`map_closing`）になる

**入力待ちの画面。** `card_select` / `hand_select` / `bundle_select` / `relic_select` / `card_reward` / `rewards` /
`event` / `rest_site` / `shop` / `fake_merchant` / `treasure` / `crystal_sphere` / `map` / `menu` / `game_over` では、
上の理由をすべて消して false にする（ゲームは呼び出し側の答えを待っていて、解決中と返すと待つ側が行き詰まるため）。
`reward_selection_resolving` と、`map` の `map_traveling` / `map_closing` だけはその後に足す（どちらも入力を受け付けない間だけ立つ）。
`unknown` は入力待ちの画面ではない（理由が無ければ従来どおり false）。メニュー（ランの外）の state は `is_resolving` を持たない。

POST の `wait: true` は、`is_resolving` が false で、戦闘なら `battle.is_play_phase` も true の state が
3 回続くまで待つ（[Waiting for the Game to Settle](#waiting-for-the-game-to-settle)）。
`player_turn_starting` は既存の `is_resolving` の意味（解決途中の値を返している）に沿って理由を足したものなので、版は上げていない（schema 4 のときの追加）。
戦闘の前後の `combat_starting` / `combat_ending` も、既にあった理由の名前を同じ意味（戦闘の開始中・終了中で、ゲームが自分で先へ進む）で
`battle` の無い state に付けたものなので、版は上げていない（schema 4 のときの追加）。この state は `battle` が無いので、`wait: true` は以前から落ち着いたとみなさない。
`map_traveling` / `map_closing` / `room_changing` / `run_loading` も、既存の `is_resolving` の意味（ゲームが自分で先へ進む途中で、入力を受け付けない）に沿って
理由を足したもので、版は上げていない（schema 4 のときの追加）。これまで `is_resolving: false` で返っていた移動中の `map`・読み込み中の `unknown` が true になり、
`wait: true` もそこで落ち着いたとみなさなくなる（`choose_map_node` は次の部屋の state、`continue` は再開した部屋の state まで待つ）。ただし待ちの「3 秒変化なし」の打ち切りは `is_resolving` を見ないので、演出が 3 秒を超えて state が変わらないとき（協力プレイの投票の演出など）は、`is_resolving: true` のまま `settled: true, changed: false` で返り得る。読み手は応答の state の `is_resolving` も確かめる。

### 解放済みの戦闘生成 pool（追加、schema 3）

`player.generation_pool` は単独プレイとマルチのローカル `player` に入る。
`BuildPlayerState` と同じ snapshot で、その Player 自身の `UnlockState` と `RunState` を使う。
対象の照合用に `player.character_id` / `player.player_id`、`run.player_count` /
`run.card_multiplayer_constraint` も追加した。ID は表示名や配列添字ではない。

```jsonc
"generation_pool": {
  "character_id": "IRONCLAD",
  "player_id": "1", // Player.NetId.ToString()
  "players": 1,
  "multiplayer_constraint": "SingleplayerOnly",
  "known": true,
  "cards": [ { "id": "UPPERCUT", "type": "Attack" } ], // 例示。実際には全候補
  "game_assembly_sha256": "a671e9efb86a7b80405133105665867b053c787670d475b4347cf5ccc15cb5fb",
  "filter_version": 2,
  "filter_source": "CardPoolModel.GetUnlockedCards(Player.UnlockState,IRunState.CardMultiplayerConstraint);CardFactory.FilterForRun;CardFactory.FilterForCombat;OrobicAcid.OnUse:Attack,Skill,Power"
}
```

オロビックの酸液（`OrobicAcid.OnUse`）は、キャラの解放済み pool を型ごとに
`CardFactory.GetDistinctForCombat` へ渡す。v0.113.1 の `GetDistinctForCombat` は
private な `CardFactory.FilterForRun`（`c.IsAllowedInRun(runState)`）→ `FilterForCombat` の順に絞ってから抽選する。
この欄は `GetUnlockedCards` の結果に、本体の public な `CardModel.IsAllowedInRun(player.RunState)`、
`CardFactory.FilterForCombat`、Attack/Skill/Power の型 filter を適用し、ID の昇順で出す。
`IsAllowedInRun` は人数（2 人以上で SingleplayerOnly、1 人以下で MultiplayerOnly を除く）と、
`GameMode.Blitz` のとき `IsAllowedInBlitz` が false の札を除く。MOD は条件を写さず本体の判定を呼ぶので、
state にゲームモードが無くても Blitz の除外は反映される。
抽選、戦闘カード作成、RNG の読み取りはしない。ID と型だけを載せ、本文・コストは複製しない。
`game_assembly_sha256` は実際にロードした `CardModel` の assembly ファイルから初回に計算して保持する。

`filter_version` はこの pool 契約の版であり、state の schema とは別。
**版 2 は候補の条件の意味を変えた**（版 1 は人数の条件だけを MOD に写し、`filter_source` に
`CardFactory.FilterForPlayerCount` と書いていた。v0.113.1 の本体の `FilterForRun` とは Blitz の条件が違う）。
利用側は `filter_version` と `filter_source` の完全一致で契約を判定し、版 1 の観測を版 2 として扱わない。
利用側はこの契約を `filter_version` で判定する。`filter_version` が変わる更新でも、state の `schema` は
[通常の規則](#common-top-level-fields)で動かす（項目の追加は MINOR、既存の項目の意味・形の変更は MAJOR）。
非推奨の整数 `schema_version` は 12 で固定している（版 2 のときも上げなかった）。

取得中に例外が出た場合は `known: false, cards: []` と `reason`（例外の型とメッセージ）、
state の `warnings` を返す。取得前に失敗したメタデータは欠落しうる。部分一覧を known=true にしない。
旧 MOD にはこの欄が無い。取得不能と空一覧を同一視せず、欄の存在を検出すること。
Markdown は単独・マルチのローカル player について件数・人数、または unknown と理由の1行だけを出す。

出典は v0.113.1 のデコンパイル `sts2-a671e9ef` の `OrobicAcid.OnUse`、`CardPoolModel.GetUnlockedCards`、
`CardFactory.GetDistinctForCombat` / `FilterForRun` / `FilterForCombat`、`CardModel.IsAllowedInRun` / `IsAllowedInBlitz`。
これは discovered_cards とは別の情報。
`NCardLibraryGrid.GetCardVisibility` は profile の解放条件と発見条件を分け、解放済みでも未発見なら
NotSeen にする。図鑑がこのラン固有の候補ID一覧をそのまま見せるとは言えない。
解放対象札は各 Epoch の `QueueUnlocks` → `NTimelineScreen.QueueCardUnlock` →
`NUnlockCardsScreen.Open` で表示される。保存ランの `Player.UnlockState` は profile の現在値と
異なりうる（`UnlockState` の契約、`Player.FromSerializable`）。未来の抽選結果・山札順は含まない。

#### 何でも屋の生成候補（`player.colorless_generation_pool`、追加。filter_version 2）

戦闘中の単独・マルチのローカル `player` に、何でも屋（`JackOfAllTrades.OnPlay`）の抽選前の候補を出す。
戦闘外・旧 MOD では省略する。形は `generation_pool` と同じ（`character_id` / `player_id` / `players` /
`multiplayer_constraint` / `known` / `cards` / `game_assembly_sha256` / `filter_version` / `filter_source`、失敗時 `reason`）。

```jsonc
"colorless_generation_pool": {
  "known": true,
  "cards": [ { "id": "FLASH_OF_STEEL", "type": "Attack" } ], // 例示。実際には全候補
  "filter_version": 2,
  "filter_source": "ColorlessCardPool.GetUnlockedCards(Player.UnlockState,IRunState.CardMultiplayerConstraint);CardFactory.FilterForRun;CardFactory.FilterForCombat;JackOfAllTrades.OnPlay:ExcludeSelf"
  // character_id / player_id / players / multiplayer_constraint / game_assembly_sha256 も同じ
}
```

`ModelDb.CardPool<ColorlessCardPool>().GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)`
から `JackOfAllTrades` 自身を除き（`OnPlay` の `where !(c is JackOfAllTrades)`）、
`CardModel.IsAllowedInRun` → `CardFactory.FilterForCombat` を通した ID の昇順。何でも屋は型で絞らないので
型の filter はかけない。通常モードでは解放済みの `MY_SHINIES` を含み、Blitz では本体の判定で除かれる。
失敗時の契約・抽選をしないことは `generation_pool` と同じ（warning のパスは `player.colorless_generation_pool`）。
Markdown は `Colorless generation pool: N cards` または unknown と理由の 1 行。
解放済み一覧の `unlocked_pools.colorless_cards`（後段の filter 前）は従来どおり残す。
出典は `sts2-a671e9ef` の `JackOfAllTrades.OnPlay`、上記の `CardFactory` / `CardModel` のメソッド。

#### スプラッシュの生成候補（`player.other_character_attack_generation_pool`、schema 1.5.0。filter_version 2）

戦闘中の単独・マルチのローカル `player` に、スプラッシュ（`Splash.OnPlay`）の抽選前の候補を出す。
戦闘外・旧 MOD では省略する。形は `generation_pool` と同じ（`character_id` / `player_id` / `players` /
`multiplayer_constraint` / `known` / `cards` / `game_assembly_sha256` / `filter_version` / `filter_source`、失敗時 `reason`）。

```jsonc
"other_character_attack_generation_pool": {
  "known": true,
  "cards": [ { "id": "SWEEPING_BEAM", "type": "Attack" } ], // 例示。実際には全候補
  "filter_version": 2,
  "filter_source": "UnlockState.CharacterCardPools(Character.CardPool removed when Count>1).GetUnlockedCards(Player.UnlockState,IRunState.CardMultiplayerConstraint);CardFactory.FilterForRun;CardFactory.FilterForCombat;Splash.OnPlay:Attack"
}
```

`Splash.OnPlay` と同じく `player.UnlockState.CharacterCardPools` を列挙し、2 つ以上あれば自キャラの
`player.Character.CardPool` を除く（1 つだけなら除かない）。各 pool の
`GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)` から `Type == Attack` の札を、
`CardFactory.GetDistinctForCombat` と同じく `CardModel.IsAllowedInRun`（= private の `FilterForRun`）→
`CardFactory.FilterForCombat` に通した ID の昇順。スプラッシュはここから 3 枚を `Rng.CombatCardGeneration` で
抽選し、強化版なら 3 枚とも強化して choose-a-card に出す。その抽選・強化・結果は出さない（候補の集合だけ）。
解放済みのキャラは図鑑・キャラ選択で見え、各 pool の解放済みの札も公開の情報なので、集合は人間が見える範囲で決まる。
失敗時の契約・抽選をしないことは `generation_pool` と同じ（warning のパスは `player.other_character_attack_generation_pool`）。
Markdown は `Other-character attack pool: N cards` または unknown と理由の 1 行。
出典は `sts2-a671e9ef` の `Splash.OnPlay`、`UnlockState.CharacterCardPools`、`CardFactory.GetDistinctForCombat` /
`FilterForCombat`、`CardModel.IsAllowedInRun`。契約試験は [`tests/GenerationPoolContract`](../tests/GenerationPoolContract/README.md)。

#### エントロピーの変化の候補（`player.transformation_options`、schema 1.5.0）

戦闘中の単独・マルチのローカル `player` に、エントロピー（`EntropyPower`）が手番の初めに変化させうる札ごとの、
変化先の候補を出す。**エントロピーの戦闘だけ**（`player.Creature.Powers` に `EntropyPower` がある、または
戦闘のパイルのどれかに `Entropy` の札がある）。それ以外の戦闘・戦闘外・旧 MOD では省略する。

```jsonc
"transformation_options": {
  "known": true,
  "source": "EntropyPower.AfterPlayerTurnStart;CardCmd.TransformToRandom;CardTransformation(original).IsInCombat=original.CombatState!=null;CardFactory.GetDefaultTransformationOptions(original,isInCombat)",
  "cards": [
    { "id": "DEFEND_IRONCLAD", "uids": ["DEFEND_IRONCLAD#1", "DEFEND_IRONCLAD#3"], "options": ["ANGER", "ASHEN_STRIKE", "..."] },
    { "id": "BURN", "uids": ["BURN#9"], "options": null, "reason": "InvalidOperationException: All transformation options provided are invalid! …" }
  ]
}
```

- 経路: `EntropyPower.AfterPlayerTurnStart` → `CardSelectCmd.FromHand`（手札から `Amount` 枚）→ 選んだ札ごとに
  `CardCmd.TransformToRandom(item, Rng.CombatCardGeneration)` → `CardTransformation(original)`（候補の指定なし、
  `IsInCombat = original.CombatState != null`）→ `CardFactory.CreateRandomCardForTransform` →
  `GetDefaultTransformationOptions(original, isInCombat)` から 1 枚。MOD はこの `GetDefaultTransformationOptions` を
  各札に対して同じ引数で呼んだ結果（ID）を出す。条件（元の札の pool、Quest・Event・Ancient・Token なら無色の pool、
  元が Status・Curse でなければ Common・Uncommon・Rare だけ、戦闘中は `CanBeGeneratedInCombat`、自分と同じ ID を除く、
  `FilterForRun`）は本体のメソッドの中にあり、MOD には写さない。
- 対象は所有者の戦闘のパイル（`PlayerCombatState.AllPiles`: 手札・山札・捨て札・廃棄札・Play）のすべての札。
  手番の初めの手札は引くまで分からないので、引きうる札をすべて出す。同じ ID で候補も同じ札は 1 行にまとめ、
  `uids` はその個体の uid の**序数順**（山札の順は出さない）。同じ ID でも候補が違えば別の行。行は ID の昇順。
- `options` は重複を除いた ID の昇順。本体が候補なしで例外を投げる札は `options: null` と `reason`
  （本体の変化もその札では例外になる）。これは正常な状態なので warning にしない。
- 列挙そのものの失敗は `known: false`・`cards: []`・`reason`、warning `player.transformation_options`。
- 抽選・`CardCmd.Transform`・乱数は呼ばない。候補の集合は元の札の pool・解放状態・ランから決まる公開の情報で、
  どの札に変わるか（抽選の結果）は出さない。
- Markdown は `Transformation options (Entropy): DEFEND_IRONCLAD 70, …`（ID と候補の数）の 1 行。
出典は `sts2-a671e9ef` の `EntropyPower.AfterPlayerTurnStart`、`CardCmd.TransformToRandom` / `Transform`、
`CardTransformation`、`CardFactory.CreateRandomCardForTransform` / `GetDefaultTransformationOptions` /
`GetFilteredTransformationOptions`、`PlayerCombatState.AllPiles`。契約試験は [`tests/ChoiceSourceContract`](../tests/ChoiceSourceContract/README.md)。

#### ポーションの生成候補（`player.potion_generation_pool`、schema 1.10.0）

単独・マルチのローカル `player` に、ランダムなポーションを作るときの候補を出す（戦闘の内外とも）。
`PotionFactory.GetPotionOptions(player)`（キャラのポーションの pool と共有の pool の解放済み）を本体の public static
メソッドでそのまま呼び、条件を MOD に写さない。`CreateRandomPotionsOutOfCombat` はこの一覧から、`CreateRandomPotionInCombat` は
さらに `CanBeGeneratedInCombat` のものから、レアリティの抽選（`PotionFactory` の確率）で選ぶ（錬金術・エントロピーの醸造など）。

```jsonc
"potion_generation_pool": {
  "known": true,
  "source": "PotionFactory.GetPotionOptions(Player)=Character.PotionPool.GetUnlockedPotions(UnlockState)+SharedPotionPool.GetUnlockedPotions(UnlockState)",
  "character_id": "IRONCLAD",
  "player_id": "1",
  "potions": [
    { "id": "ATTACK_POTION", "rarity": "Common", "can_be_generated_in_combat": true },
    { "id": "BLOOD_POTION", "rarity": "Common", "can_be_generated_in_combat": false }
  ]
}
```

- `potions` は ID の序数順。抽選の結果・乱数・重みの値は出さない。読めなければ `known: false` と `reason`（warning は
  `player.potion_generation_pool`）。
- 出典は `sts2-a671e9ef` の `PotionFactory.GetPotionOptions` / `CreateRandomPotionsOutOfCombat` / `CreateRandomPotionInCombat`、
  `PotionModel.CanBeGeneratedInCombat`。

#### エントロピー醸造（`player.entropic_brew_pool`、schema 1.13.0）

戦闘中にローカルプレイヤーが `ENTROPIC_BREW` を所持するときに出す。マルチでもその `player` のベルトと抽選候補を表す。別のプレイヤーを対象にしたときの抽選元はそのプレイヤー自身の pool（`EntropicBrew.OnUse`）。

| キー | 意味 |
| --- | --- |
| `known`, `unavailable_reasons` | 抽選母集団と抽選規則の検査結果。読めない入力があれば `false` と項目別の理由。候補の本文、枠の可否、listener の読取結果は各行や各欄で確認する |
| `filter_version`, `filter_source`, `game_assembly_sha256` | 生成契約の版 1、使用する本体メソッド、実際に読み込んだ DLL の SHA-256。規則を照合済みの DLL は v0.113.1 の `sts2-a671e9ef` |
| `player_id`, `character_id`, `players` | 候補の所有者・キャラ・参加人数。所有者は `RunState.Players` の同一参照で、同じ `NetId` の参加者が一人であることを確かめる |
| `rarity_thresholds` | `Rare: 0.1`, `Uncommon: 0.35`, `Common: 1` の累積上限。 `order: ["Rare", "Uncommon", "Common"]`、`comparison: "first_upper_bound_inclusive"`、`precision: "float32"`。 `within_rarity: "uniform_by_occurrence"`、`between_slots: "with_replacement"` |
| `options` | `PotionFactory.GetPotionOptions(player)` の全要素を ID の序数順で出す。同一 ID の複数要素も残し、`unlocked_pools.potions.character` と `shared` の連結と多重集合で照合する。解放済み 0 件の群は空列。読取不能な列は `null`。正の質量を持つレアリティに候補がなければ、候補列を残して `known: false` |
| `options[]` | `id`・`rarity`・`usage`・`target_type`・`name`・`description`・`vars`・`slot_verdicts`。レアリティ・使用区分・対象は本体の列挙名。表示は所有者を設定した分離済みの `PotionModel.ToMutable` で読み、`vars` は所持行と同じ `DynamicVars` の数値 `BaseValue`（文字列変数を除く）。項目の失敗は当該項目の `null` と理由を残す |
| `preview` | 仮定の適用範囲と `combat_id`（出来事の通し列と同じ戦闘 ID）・`floor`・`round`・所有者の `turn_number`、`slots[]`。空き枠と所持枠を全て列挙する。各枠の `current_potion_id` は空なら `null`、`requires` は `empty` または `remove_used_potion`。前提を読めない場合は `unavailable_reason` または pool の項目別理由 |
| `options[].slot_verdicts[]` | 同じ判定の物理枠を `slots: [番号, ...]` でまとめる。各判定は `can_use_if_generated`・`can_use_if_generated_reason`・`valid_target_entity_ids`・`target_optional`。可否は `true` / `false` / `null`（未観測）。不許可と対象不要の可に空列、未観測には `null` と `unavailable_detail` |
| `listeners` | `known`・`unavailable_reasons` と `notifications`。通知は `before_potion_used`・`should_procure_potion`・`after_potion_procured`・`after_potion_used`。各列は観測時点の `RunState.IterateHookListeners(combatState)` に含まれる override を実順に並べたもの。空列は有効な override がない通知 |

`EntropicBrew.OnUse` は戦闘中も `PotionFactory.CreateRandomPotionOutOfCombat` を 1 枠ずつ呼ぶ。群分けは `PotionFactory.CreateRandomPotions` の単精度の比較と
`Rng.NextItem` が使う要素の添字に従う。公称の群質量は Rare 0.10、Uncommon 0.25、Common 0.65。実際の取得は `PotionCmd.TryToProcure` と `Player.AddPotionInternal` が行う。
MOD が読むのは抽選前の入力と公開済みの値。乱数器・抽選結果の先読み・取得操作は契約の対象外。

枠の可否は、同じ戦闘と所有者のプレイ段階で、新品が指定枠に入り、解決後も権限と対象条件が観測時点と同じ場合に使える。
`NPotionHolder.RemoveUsedPotion` が占有枠の無効化を解除し、`AddPotion` は枠のフラグを維持する。空き枠には現在のフラグを使う。
新品は未キューで、旧瓶の popup の判定は引き継がない。権限・ロック・使用区分・対象判定は所持行と同じ読取
（`Player.CanUseOrRemovePotions`、`RunManager.IsPotionUseLocked`、`NPotionPopup.CanEnableUseButton`、
`PotionModel.PassesCustomUsabilityCheck` / `IsValidTarget`、`NTargetManager` の対象規則、`Hook.ShouldAllowTargeting` と `Creature.IsHittable`）。
一時停止中、解決中、選択中、未知の listener、空手札で有効な `UnceasingTop` の追加ドローがありうる場合は、可否が未観測になる。
枝で対象・ノードの操作可否・所有者の権限・手番等が変わった場合は再観測が必要。実送信は現物の holder と照合する `use_potion(expected_potion_id=...)` を使う。

通知列の各行には `model_id`・`model_type`・`declared_by`・`kind`・所有者の ID と `owner_index`（所有者のレリック列またはパワー列の添字）が入る。
`order` は観測時の本体の列挙位置。既知の override は `SurroundedPower.BeforePotionUsed`、`Sozu.ShouldProcurePotion`、
`BeltBuckle.AfterPotionProcured / AfterPotionUsed`、`ReptileTrinket.AfterPotionUsed`。溶融レリックは本体の `CombatState.IterateHookListeners` が除く。
`ShouldProcurePotion` は最初の拒否で止まる。列は発火前の入力で、発火後の事実は既存の `battle.combat_log` に残る。
未同定の override や外部購読があれば `known: false` と理由を出す。山札の listener には `order: null` と公開順を使う。

### 戦闘中の解放済み pool（追加、schema 3）

戦闘中のローカル `player.unlocked_pools` に、解放済みのモデル ID (`Id.Entry`) の一覧を追加する。
単独・マルチとも同じ Player Object に出し、戦闘外・旧 MOD では省略する。

```jsonc
"unlocked_pools": {
  "colorless_cards": [ /* 無色カードの ID */ ],
  "character_cards": [ /* 所有キャラのカード ID */ ],
  "potions": {
    "character": [ /* 所有キャラのポーション ID */ ],
    "shared": [ /* 共有 pool のポーション ID */ ]
  }
}
```

すべての配列は ID の ordinal 昇順。解放条件や multiplayer の条件は MOD に写さず、
ゲームの以下の関数の結果を列挙する。出典はデコンパイル `sts2-9cb4f1ad`。

| 項目 | 呼び出すゲーム関数と引数 | 生成処理の出典（デコンパイルのファイル） |
|---|---|---|
| `colorless_cards` | `ModelDb.CardPool<ColorlessCardPool>().GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)` | `MegaCrit.Sts2.Core.Models.Cards/JackOfAllTrades.cs` の `OnPlay` |
| `character_cards` | `player.Character.CardPool.GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)` | `MegaCrit.Sts2.Core.Models.Cards/Jackpot.cs` の `OnPlay` |
| `potions.character` | `player.Character.PotionPool.GetUnlockedPotions(player.UnlockState)` | `MegaCrit.Sts2.Core.Factories/PotionFactory.cs` の `GetPotionOptions` |
| `potions.shared` | `ModelDb.PotionPool<SharedPotionPool>().GetUnlockedPotions(player.UnlockState)` | 同上 |

何でも屋が実際に呼ぶのは `GetUnlockedCards`。この関数
（`MegaCrit.Sts2.Core.Models/CardPoolModel.cs`）が `AllCards` と `FilterThroughEpochs` を使い、
無色 pool の `GenerateAllCards` / `FilterThroughEpochs`
（`MegaCrit.Sts2.Core.Models.CardPools/ColorlessCardPool.cs`）を経由する。
multiplayer 制約は札の `Owner.RunState.CardMultiplayerConstraint` と同じ取り方で、
`MegaCrit.Sts2.Core.Runs/IRunState.cs` の getter は人数が 1 以下なら `SingleplayerOnly`、
それ以外なら `MultiplayerOnly` を返す。solo 固定にはしない。
UnlockState も札の `Owner.UnlockState` と同じものを使う。
現在の profile や全プレイヤーの解放状態の和集合で置き換えない。
`MegaCrit.Sts2.Core.Unlocks/UnlockState.cs` の説明と
`MegaCrit.Sts2.Core.Entities.Players/Player.cs` の `FromSerializable` によれば、
保存ランの解放状態は現在の profile と異なる場合がある。

この欄は**解放済み pool**であり、各札の最終的な抽選候補一覧ではない。
何でも屋の自己除外、ジャックポットの 0 コスト・非 X 条件、後段の
`CardFactory.GetDistinctForCombat` / `GetForCombat` による戦闘生成 filter は適用しない。
錬金術は `MegaCrit.Sts2.Core.Models.Cards/Alchemize.cs` の `OnPlay` から
`PotionFactory.CreateRandomPotionInCombat(Owner, Owner.RunState.Rng.CombatPotionGeneration)`
を呼び、その関数が `GetPotionOptions` に `CanBeGeneratedInCombat` の条件を追加してから抽選する。
この欄のポーション一覧には、その追加条件を適用しない。
抽選・RNG・モデルの mutable 化や戦闘カード作成は行わない。
既存の `generation_pool` は別の filter を持つため、同一視しない。
何でも屋の抽選前の最終候補は `player.colorless_generation_pool`（上記）を読む。

各配列の読み取り・遅延列挙中に例外が出た場合、その配列だけを `null` にし、
`warnings` に `player.unlocked_pools.colorless_cards` / `character_cards` /
`potions.character` / `potions.shared` の該当パスと例外を記録する。
他の配列は取得を続ける。`[]` は解放済み項目なし、`null` は取得不能なので区別する。
戦闘 state のオブジェクト参照と Player の参照ごとに結果と失敗情報をキャッシュし、
次の戦闘・別 Player では作り直す。キャッシュされた失敗も state 取得ごとに警告へ載せる。
Markdown は `Unlocked pools: colorless cards N | character cards N | potions character N, shared N`
の 1 行のみで、取得不能は該当件数を `unknown` とし、ID 一覧は出さない。
schema 3 で追加した項目であり、その追加時には `schema_version` を変更していない。

### Player Object

Always present at the top level (except `menu`). Contains everything about the local player.

```jsonc
{
  "character": "The Ironclad",
  "character_id": "IRONCLAD", // Stable model ID (not localized)
  "player_id": "1",          // Player.NetId as a string
  "hp": 72,
  "max_hp": 80,
  "block": 0,
  "gold": 99,

  // --- Combat-only fields (present only when CombatManager.IsInProgress) ---
  "unlocked_pools": { // 解放済み pool。最終的な抽選候補ではない。上記の契約を参照
    "colorless_cards": [ /* IDs */ ],
    "character_cards": [ /* IDs */ ],
    "potions": { "character": [ /* IDs */ ], "shared": [ /* IDs */ ] }
  },
  "combat_history": { /* Combat History Object below; unavailable values are null */ },
  "energy": 3,
  "max_energy": 3,
  "stars": 2,               // Regent only; omitted if 0 and character doesn't always show stars
  "hand": [ /* Card Objects */ ],
  "draw_pile_count": 15,
  "discard_pile_count": 3,
  "exhaust_pile_count": 1,
  "play_pile_count": 0,
  "draw_pile": [ /* Pile Card Objects (Rarity, CardModel.CompareTo, opaque uid) */ ],
  "discard_pile": [ /* Pile Card Objects */ ],
  "exhaust_pile": [ /* Pile Card Objects */ ],
  "play_pile": [ /* Pile Card Objects; temporary, during play resolution */ ],
  "orbs": [ /* Orb Objects */ ],   // Defect only; omitted if orb capacity is 0
  "orb_slots": 3,
  "orb_empty_slots": 1,
  "pets": [                        // Necrobinder only; omitted if no pets
    {
      "id": "OSTY",
      "name": "Otsy",
      "alive": true,
      "hp": 12,
      "max_hp": 12,
      "block": 0,
      "status": [ /* Power Objects */ ]
    }
  ],

  // --- Always present ---
  "status": [ /* Power Objects */ ],
  "relics": [
    {
      "id": "BURNING_BLOOD",
      "name": "Burning Blood",
      "description": "At the end of combat, heal 6 HP.",
      "vars": { "Heal": 6 }, // DynamicVar name -> numeric BaseValue (decimals preserved); null if unreadable (schema 12)
      // "run_state": { "attacks_played": 7 } — saved counters of some relics, in and out of combat (schema 12)
      // "combat_state": { ... } — in-combat internal values of allowlisted relics
      "counter": null,       // Number if relic shows a counter, null otherwise
      "used_up": false,      // RelicModel.IsUsedUp: the relic has been used up
      "melted": false,       // RelicModel.IsMelted: a separate melted-wax state
      "keywords": [ /* Keyword Objects */ ]
    }
  ],
  "potions": [
    {
      "id": "SWIFT_POTION",
      "name": "Swift Potion",
      "description": "Draw 3 cards.",
      "slot": 0,             // Potion slot index (use this for use_potion action)
      "can_use_in_combat": true,
      "target_type": "None", // None, Self, AnyEnemy, AnyAlly, AnyPlayer, etc.
      "keywords": [ /* Keyword Objects */ ],
      "can_use_now": true,                       // schema 11: true / false / null (unreadable)
      "valid_target_entity_ids": ["player:1"],   // enemy entity_ids, "player:<player_id>", [] or null
      "can_use_now_reason": null                 // null / ban code / "unobserved:<stage>"
    }
  ],
  "max_potion_slots": 3      // Belt capacity. Default 3, grows with belt-expanding relics (e.g. Potion Belt: +2). Use to detect a full belt: len(potions) >= max_potion_slots.
}
```

Owned relics always include boolean `used_up` and `melted`, in singleplayer and the
multiplayer local `player` object (both use `BuildPlayerState`). Markdown marks true
values with `(used)` and `(melted)` in singleplayer outside combat, and during combat.
Multiplayer outside combat does not list relics in Markdown. These fields are additive:
feature-detect their presence; they were added under schema 3 without a bump. Older builds omit them.
`melted` is separate from consumption: `RelicModel.HoverTip` exposes the melted
notice, and `UpdateTexture` gives it a dark-red tint. The grey inventory tint comes
from `RelicStatus.Disabled` in `NRelicInventoryHolder.RefreshStatus`; for example,
`LizardTail.WasUsed` sets that status when consumed. These booleans do not claim to
encode every reason a relic can be disabled.

### ポーションをいま使えるか（`player.potions[]`、schema 11）

`cat12079801/sts2#1012`。単独・マルチのローカル `player` の所持行すべて（空き枠の行は作らない。戦闘中・戦闘外とも）に、
ベルトの使用ボタンをいま押せるか・押したらどこへ使えるかを出す。ゲームの判定を読むだけで、popup の生成・`RefreshButtons`・
対象選択の開始・拒否の VFX・enqueue・在庫の開閉は呼ばない（`McpMod.PotionUsability.cs` が判定、`McpMod.PotionUsabilityLive.cs` が読取）。

| 読取の結果 | `can_use_now` | `valid_target_entity_ids` | `can_use_now_reason` |
|---|---|---|---|
| 使用可、creature の宛先あり | `true` | 許可された安定 ID の重複の無い配列（本体の列挙順） | `null` |
| 使用可、creature を指定しない | `true` | `[]`（全体効果、商人宛ての悪臭ポーション） | `null` |
| 既知の使用不可 | `false` | `[]` | 最初に確定した禁止の理由 |
| 判定に要る読取の失敗・未知の形式 | `null` | `null` | `unobserved:<段>`。行の `unavailable_reasons.can_use_now` とトップレベルの `warnings`（`player.potions.<slot>.can_use_now`）にも出す |

`null` を `false` や `[]` として扱わないこと。1 本の読取の失敗はその行だけに留まり、別の瓶・プレイヤー・戦闘の情報は返る。
既存の `can_use_in_combat`（`Usage` が CombatOnly / AnyTime）と `target_type`（いまの `TargetType`）は意味を変えない。
それらの getter が例外を投げても、その項目だけ `null` と `unavailable_reasons` にして行と `player` を残す。

**ID の形。** 敵は `battle.enemies[].entity_id` と同じ registry（`RefreshEntityIdRegistry` / `GetStableEntityId`）の ID。
CombatId か monster が無い敵、2 体が同じ ID になった場合は、許可される敵に限らず `unknown_0` や位置で代用せず `unobserved:entity_id` /
`unobserved:entity_id_duplicate`。プレイヤーはこの配列の中だけ `player:<player_id>`（`Player.NetId` の 10 進）で表す。商人には ID を振らない。

**判定の順（最初の禁止が理由になり、それより後の読取はしない）。** 出典は v0.113.1（sts2-a671e9ef）。

| 順 | 読むもの（出典） | 理由 |
|---|---|---|
| 1 | ローカルのベルト `NRun.GlobalUi.TopBar.PotionContainer.Holders[slot].Potion.Model` がこの個体と同一（`NPotionContainer.Add`） | 違えば・UI が無ければ `unobserved:holder_model` / `unobserved:holder`（禁止と断定しない） |
| 2 | `PotionModel.IsQueued`（`NPotionPopup._Ready`） | `queued` |
| 3 | 所有者の `Creature.IsDead`（同） | `owner_dead` |
| 4 | `RunState.IsGameOver`（`NPotionHolder.OpenPotionPopup`） | `game_over` |
| 5 | `Player.CanUseOrRemovePotions`（`_Ready` / `RefreshButtons`） | `owner_locked` |
| 6 | `RunManager.IsPotionUseLocked`（部屋の切り替え中か戦闘の開始中。同） | `room_locked` |
| 7 | holder の private `_isUsable`（`NPotionHolder.OnRelease`。型を確かめて読む） | `holder_not_usable` |
| 8 | holder の private `_disabledUntilPotionRemoved`（`OpenPotionPopup`） | `holder_disabled` |
| 9 | 公開 static の `NPotionPopup.CanEnableUseButton(Usage, canUseInCombatNow, PassesCustomUsabilityCheck)` をそのまま呼ぶ | 個別条件の false は `custom_check_failed`、Automatic は `automatic`。`None`・未知の使用区分は本体が例外を投げるので `unobserved:can_enable_use_button` |
| 9a | CombatOnly の `canUseInCombatNow`（`RefreshButtons` の式を本体の順に）: `CombatManager.IsInProgress` → `CombatState.CurrentSide == Side` → `IsAlive` → `InACardSelectScreen`（`NPlayerHand.IsInCardSelection` か `NOverlayStack.Peek() is ICardSelectionScreen`）→ `PlayerActionsDisabled` | `outside_combat` / `not_player_turn` / `owner_dead` / `card_selection` / `actions_disabled`。AnyTime にはかけない |
| 10 | 生きた popup（holder の private `_popup` が有効で `IsMarkedForRemoval` でない）が開いていれば `IsUsable` と照合。閉じた holder の `IsPotionUsable` は `_popup` を参照するので呼ばない。1〜9 で禁止が決まったときは popup を見ない（古いまま有効な popup が開いていても `false` と理由を返す） | 規則が許すのに popup が無効なら `unobserved:popup` |
| 11 | 対象（下） | `targeting_blocked` / `no_valid_target` |

**対象（`NPotionHolder.UsePotion` の分岐）。** AnyEnemy・TargetedNoCreature・`PotionModel.CanThrowAtAlly()`（AnyPlayer かつ複数人かつ戦闘中）は対象の選択へ、それ以外は直接使う。

- 直接使う: Self は所有者、他は null を `EnqueueManualUse` に渡し、所有者が `IsValidTarget` なら null を所有者に置き換える（ソロの AnyPlayer）。
  所有者になるなら `["player:<自分>"]`（`use_potion` は `target` を省ける）。null のままなら `UsePotionAction.ExecuteAction` が、戦闘中は
  単体型でない限り null のまま `IsValidTarget(null)` を、戦闘外は TargetedNoCreature 以外に所有者を当てて判定するので、戦闘中で `IsValidTarget(null)` なら `[]`、
  そうでなければ `no_valid_target`。
- 選択を伴う場合、戦闘中は `NPotionHolder.ShouldCancelTargeting`（`NOverlayStack.ScreenCount > 0` か `NCapstoneContainer.InUse`）で選択がすぐ終わるので `targeting_blocked`。
- AnyEnemy（戦闘中）: 所有者の `CombatState.Enemies` を本体の順に走査し、`NTargetManager.AllowedToTargetCreature`（敵側・死んでいない）、`PotionModel.IsValidTarget`、
  `NCombatRoom.GetCreatureNode` が有り `NCreature.IsInteractable`（ホバーには操作できるノードが要る。`SetIsInteractableInternal` が hitbox のマウス入力を切る。例: `ThievingHopper.EscapeMove` / `FatGremlin.FleeMove`）、`Hook.ShouldAllowTargeting`（`OnCreatureHovered`。拒否の VFX は呼ばない）、`Creature.IsHittable`
  （`NTargetManager._Process` がホバー中の当てられない敵で選択を取り消す）をすべて通った敵だけ。0 体なら `no_valid_target`。戦闘外の AnyEnemy は `unobserved:target_type`。
- TargetedNoCreature: 悪臭ポーション（戦闘外）だけ。`FoulPotion.GetFoulPotionMerchantTarget` が商人のボタンを返し `IsValidTarget(null)` なら `[]`、無ければ `no_valid_target`
  （在庫を開いていると的は無い。そもそも `PassesCustomUsabilityCheck` が false になり `custom_check_failed`）。それ以外は `unobserved:target_type`。
- マルチで味方に投げる（AnyPlayer）: `RunState.Players` を順に、`AllowedToTargetCreature`（AnyPlayer）と `IsValidTarget` を満たし、`NMultiplayerPlayerStateContainer` の
  private `_nodes` にその player のノードがあるか、creature のノードが `IsInteractable` で `Hook.ShouldAllowTargeting` と `IsHittable` を通る player を `player:<player_id>` で。

可否は加算値・強化段階・同名の瓶の数と関係しない（同名 2 本も別スロットで別に判定する）。予約に成功しても消費は確定しない
（`UsePotionAction.ExecuteAction` が空き・無効な対象で cancel し、`PotionModel.AfterUsageCanceled` が予約を外す）。

**ゲームのボタンと MOD の受付は別。** 例えば AnyTime の瓶は手札の選択中・敵の手番でもボタンが有効（`can_use_now: true`）だが、
MOD は戦闘中の `PlayerActionsDisabled` の間は `use_potion` を受けない。拒否は `expected_potion_id` 付きなら `potion_check.stage: "mod_acceptance"` で区別できる。

**passive と非 passive。** 判定そのものは常に読むだけだが、非 passive の読み取りはショップの在庫を自動で開き（`NMerchantRoom.OpenInventory`）、
`sellable_potions` のために閉じることもある（`CanSellPotionToMerchant`）。`player` はその後に組むので、非 passive の `player.potions[]` の可否は
同じ読み取りが行った在庫の開閉の後の状態を表す（悪臭ポーションの `custom_check_failed` / `true` がそれで変わりうる）。在庫に触れない答えは `?passive=1` で読む。

Markdown はポーションの行の末尾に ` | Use now: yes → ID, …` / ` | Use now: yes (no target)` / ` | Use now: no (理由)` / ` | Use now: unobserved (段: 理由)` を付ける（旧 state の行には付けない）。

### ポーションの使用履歴（`player.combat_history.potion_uses`、schema 1.13.0）

戦闘中、ローカルプレイヤーが醸造を持つか、その戦闘で本人の醸造の使用が記録されているときに付く。
`CombatManager.History.Entries` の `PotionUsedEntry` を `Actor == owner.Creature` で絞り、戦闘開始からの記録順にすべて出す。
各行は `potion_id` と `target_entity_id`。対象がプレイヤーなら `player:<player_id>`、敵なら戦闘の安定 `entity_id`、
ほかのクリーチャーなら `combat:<combat_id>`、対象なしは `null`。同じ枠が同じ ID で補充されても 1 回の使用につき 1 行増える。
初回使用前の `[]` と読取失敗時の `null`（`unavailable_reasons.potion_uses` と warning）を区別する。
記録時点は `PotionModel.OnUseWrapper` が効果実行後、所有者が生存し同じ戦闘が有効な場合に呼ぶ `CombatHistory.PotionUsed`。
解決後の在庫と合わせて読み、終端を含む出来事は `battle.combat_log` / `ended_combat.combat_log` の契約に従う。

### 戦闘履歴の要約（追加・schema 4）

戦闘中の単独・マルチのローカル `player` に `combat_history` を出す。
条件は既存の戦闘用フィールドと同じ `PlayerCombatState != null && CombatManager.IsInProgress`。
戦闘外・旧 MOD では省略し、欠落は未観測として扱う。
各 snapshot で `CombatManager.Instance.History` を読み直す。`turn_number` だけは
`PlayerCombatState.TurnNumber` を直接読む。操作ログ・パイルの差分・レリックの表示値から復元せず、
未発生の出来事や山札の順序は含めない。公開された戦闘中の出来事の要約である。

```json
{
  "turn_number": 2,
  "card_plays_started_this_turn": {
    "total": 3,
    "by_type": { "None": 0, "Attack": 2, "Skill": 1, "Power": 0, "Status": 0, "Curse": 0, "Quest": 0 }
  },
  "card_plays_finished_this_turn": {
    "total": 2,
    "by_type": { "None": 0, "Attack": 1, "Skill": 1, "Power": 0, "Status": 0, "Curse": 0, "Quest": 0 }
  },
  "any_manual_cards_played_this_turn": true,
  "card_plays_finished_this_combat": 7,
  "card_plays_finished_this_turn_card_uids": ["THRUMMING_HATCHET#0", "THRUMMING_HATCHET#0"],
  "card_play_events_this_turn": [
    { "phase": "started", "uid": "HAVOC#10", "is_auto_play": false, "target": null, "play_index": 0, "play_count": 1 },
    { "phase": "started", "uid": "TAUNT#20", "is_auto_play": true,
      "target": { "combat_id": 12, "entity_id": "WRIGGLER_1" }, "play_index": 0, "play_count": 1 },
    { "phase": "finished", "uid": "TAUNT#20", "is_auto_play": true,
      "target": { "combat_id": 12, "entity_id": "WRIGGLER_1" }, "play_index": 0, "play_count": 1 },
    { "phase": "finished", "uid": "HAVOC#10", "is_auto_play": false, "target": null, "play_index": 0, "play_count": 1 }
  ],
  "card_resolution_events": [
    { "kind": "draw", "uid": "STRIKE_IRONCLAD#1", "from_hand_draw": false },
    { "kind": "generated", "card_id": "BURN", "uid": "BURN#g1182", "creator": null },
    { "kind": "draw", "uid": "CASCADE#10", "from_hand_draw": true },
    { "kind": "play", "phase": "started", "uid": "CASCADE#10", "is_auto_play": false, "target": null, "play_index": 0, "play_count": 1, "reservation": null },
    { "kind": "auto_play_reservation", "call_id": 1, "parent_call_id": null,
      "parent": { "kind": "card", "model_id": "CASCADE", "uid": "CASCADE#10", "hidden_reason": null },
      "parent_play_started_index": 3, "requested_count": 2, "position": "top", "force_exhaust": false },
    { "kind": "auto_play_reserved", "call_id": 1, "reserve_index": 0, "uid": "TAUNT#20" },
    { "kind": "auto_play_reserved", "call_id": 1, "reserve_index": 1, "uid": "BURN#g1182" },
    { "kind": "auto_play_dispatch", "call_id": 1, "reserve_index": 0, "uid": "TAUNT#20" },
    { "kind": "play", "phase": "started", "uid": "TAUNT#20", "is_auto_play": true,
      "target": { "combat_id": 12, "entity_id": "WRIGGLER_1" }, "play_index": 0, "play_count": 1,
      "reservation": { "call_id": 1, "reserve_index": 0 } }
  ],
  "last_player_turn_card_uids": ["BOLAS#0", "BOLAS#0", "STRIKE_IRONCLAD#1"],
  "cards_exhausted_this_turn": 1,
  "bound_afflictions_this_turn": 2,
  "unblocked_damage_events_this_turn": 1,
  "unblocked_damage_events_this_combat": 3,
  "block_gains_this_turn": 2,
  "unavailable_reasons": {}
}
```

`owner` はローカル Player、`creature` は `owner.Creature`、`combat` は
`creature.CombatState`。以下は v0.113.0 のデコンパイル `sts2-d009478a` を出典とする。
件数は entry の数であり、札の個体数・ダメージ量・ブロック量ではない。

| 項目 | 絞り込みとゲーム側の出典（クラス・メソッド／getter） |
|---|---|
| `turn_number` | `owner.PlayerCombatState.TurnNumber`。`StoneCalendar.DisplayAmount` / `AfterSideTurnStart` / `BeforeSideTurnEnd`、`MrStruggles.AfterPlayerTurnStart`。追加ターンでは `battle.round` と一致しない。 |
| `card_plays_started_this_turn` | `History.CardPlaysStarted` で `e.HappenedThisTurn(combat) && e.CardPlay.Player == owner`。型は `e.CardPlay.Card.Type`。`NostalgiaPower.ModifyCardPlayResultLocation` の Attack + Skill、`JugglingPower.AfterApplied` の Attack、`VelvetChoker.GetCardsPlayedThisTurn` の全型と同じ。 |
| `card_plays_finished_this_turn` | `History.CardPlaysFinished` で同じターン・Player 条件。`RippleBasin.BeforeSideTurnEnd` の Attack、`LunarBlast.CanonicalVars` の Skill と同じ。 |
| `card_plays_finished_this_combat` | int（schema 1.5.0）。`History.CardPlaysFinished.Count()`。戦闘全体・**全 Player**（ターン・Player の条件なし）。v0.113.1 `sts2-a671e9ef` の `GoldAxe.CanonicalVars`（`CalculatedDamageVar.WithMultiplier` の式そのもの）。完了（`CardModel.OnPlayWrapper` の `CardPlayFinished`）だけを数えるので、解決中の金の斧自身や、本文の途中の親の札は入らない。リプレイ・自動プレイ・スキップした選択の札も完了の entry の数だけ数える。 |
| `any_manual_cards_played_this_turn` | bool。v0.113.1 `sts2-a671e9ef` の `PaelsEye.AnyCardsPlayedThisTurn` の履歴条件。`History.CardPlaysFinished.Any(e => e.Actor == creature && e.HappenedThisTurn(combat) && !e.CardPlay.IsAutoPlay)`。空履歴は false。 |
| `card_plays_finished_this_turn_card_uids` | `History.CardPlaysFinished.Where(e => e.HappenedThisTurn(combat))` の `CardPlay.Card` を既存の `GetStableCardUid` で変換する。次手番の `Bolas.BeforeHandDraw` / `ThrummingHatchet.BeforeHandDraw` の個体判定用。`CardPlay.Player == owner` を追加しない。 |
| `card_play_events_this_turn` | `History.Entries` を元の順のまま走査し、`CardPlayStartedEntry` / `CardPlayFinishedEntry` のうち `e.HappenedThisTurn(combat) && e.CardPlay.Player == owner` のもの（開始・完了の集計と同じ条件）。各要素は下記。v0.113.1 `sts2-a671e9ef` の `CombatHistory.CardPlayStarted/CardPlayFinished`、`CardPlay.IsAutoPlay/Target/PlayIndex/PlayCount`、`CardModel.OnPlayWrapper`。 |
| `card_resolution_events` | schema 1.6.0。`History.Entries` を元の順のまま走査し、所有者の `CardDrawnEntry`・`CardPlayStartedEntry` / `CardPlayFinishedEntry`・`CardGeneratedEntry` を戦闘全体（手番の条件なし）で出す。下の「[札の解決の通し列](#札の解決の通し列card_resolution_eventsschema-160)」。schema 1.8.0 で、山札からの自動プレイの予約の行と play の行の `reservation` を足した（「[自動プレイの予約](#自動プレイの予約card_resolution_events-の予約の行schema-180)」）。 |
| `last_player_turn_card_uids` | `History.CardPlaysFinished.Where(e => e.HappenedLastPlayerTurn(owner))` の `CardPlay.Card` を既存の `GetStableCardUid` で変換する。`Bolas.BeforeHandDraw` / `ThrummingHatchet.BeforeHandDraw` がこの集合の札との個体一致を調べる。両クラスと同じく `CardPlay.Player == owner` を追加しない。 |
| `cards_exhausted_this_turn` | `History.Entries.OfType<CardExhaustedEntry>()` で `e.HappenedThisTurn(combat) && e.Actor == creature`。`ForgottenRitual.WasCardExhaustedThisTurn` / `EvilEye.WasCardExhaustedThisTurn` の Any と同じ集合の件数。 |
| `bound_afflictions_this_turn` | `History.Entries.OfType<CardAfflictedEntry>()` で `e.HappenedThisTurn(combat) && e.Actor == creature && e.Affliction is Bound`。`ChainsOfBindingPower.AfterCardDrawn` と同じ集合の件数。 |
| `unblocked_damage_events_this_turn` | `History.Entries.OfType<DamageReceivedEntry>()` で `e.HappenedThisTurn(combat) && e.Receiver == creature && e.Result.UnblockedDamage > 0`。`Spite.LostHpThisTurn` の Any と同じ集合の件数。 |
| `unblocked_damage_events_this_combat` | 同じ DamageReceivedEntry の Receiver・UnblockedDamage 条件で、ターン条件なし。`TearAsunder.CanonicalVars` の CalculatedHits の `1 + Count(...)` の Count 部分。 |
| `block_gains_this_turn` | `History.Entries.OfType<BlockGainedEntry>()` で `e.HappenedThisTurn(combat) && e.CardPlay != null && e.CardPlay.Player.Creature == creature && e.Props.IsCardOrMonsterMove()`。`UnmovablePower.ModifyBlockMultiplicative` を **`cardPlay = null`（次のプレイを評価する文脈）** とした式そのもの。 |

開始・完了は独立に読むため、解決中には差が出る。`by_type` は `CardType` の全 enum 名をキーにし、
未発生の型も 0。`total` は全型の合計。リプレイ・自動プレイも entry の数だけ数え、
重複を除かない。本体と同じく現在の `CardPlay.Card.Type` を読み、過去の型の snapshot は作らない。

`card_play_events_this_turn` は本体が記録した開始・完了の entry の明細で、MOD は蓄積・並べ替え・重複除去をしない。各要素:

| キー | 値 |
|---|---|
| `phase` | `"started"`（`CardPlayStartedEntry`）/ `"finished"`（`CardPlayFinishedEntry`） |
| `uid` | `CardPlay.Card` を手札・各パイルと共通の `GetStableCardUid` で変換。山から消えた Power・生成札・同じ札の再プレイも記録する |
| `is_auto_play` | `CardPlay.IsAutoPlay`（bool）。荒廃・カスケード・騒乱などの自動プレイが true |
| `target` | `CardPlay.Target` が null のときだけ `null`。非 null は `{combat_id, entity_id}`。`combat_id` は `Creature.CombatId`、`entity_id` は敵（`Creature.IsEnemy`）なら `battle.enemies[]` と共通の安定 ID（死んで戦闘から消えた敵も最初に割り当てた ID）、敵でなければ `null` |
| `play_index` / `play_count` | `CardPlay.PlayIndex` / `PlayCount`。リプレイの各回は別の要素（同じ uid で index が増える） |

順序は `CardModel.OnPlayWrapper` が記録したとおり（`BeforeCardPlayed` → 開始の記録 → 本文 → enchantment → affliction → 完了の記録 → `AfterCardPlayed`）。
本文の中で自動プレイを待つ札（荒廃 `Havoc.OnPlay` → `CardPileCmd.AutoPlayFromDrawPile` → `CardCmd.AutoPlay`）では
「親の開始 → 子の開始 → 子の完了 → 親の完了」になり、完了の順は予約の順ではない。
`CardCmd.AutoPlay` が `ShouldPlay` 偽・Unplayable・対象なしで本文を実行せず結果の山へ送った札（`MoveToResultPileWithoutPlaying`）は
`OnPlayWrapper` に入らないので、開始・完了のどちらにも出ない（プレイされずに動いた札はパイルの差分で読む）。
読取（History・entry の判定・`CardPlay` の各値・対象の `CombatId`）のどれかに失敗したら、配列全体を `null` にし、
`unavailable_reasons.card_play_events_this_turn` と warning `player.combat_history.card_play_events_this_turn` を出す。
未知の対象を `null` に化けさせない（`target: null` は「対象なし」だけを意味する）。
既存の集計・完了 uid 列は残す。`card_plays_finished_this_turn_card_uids` は全 Player の完了を含むので、マルチではこの配列の完了と同一視しない。
Markdown の History 行に `play events=[start HAVOC#10, start auto TAUNT#20 -> WRIGGLER_1, …]` と出す（リプレイは `(2/2)`）。

`any_manual_cards_played_this_turn` は記録済み Actor が所有者である手動プレイ完了の有無を公開する。
各 snapshot で本体の履歴を再読取する。札の開始だけでは false のまま、手動プレイ完了後は true。
履歴の持ち主である `CombatHistory` に合わせて `combat_history` に置く。
`CardPlayFinishedEntry` のコンストラクタは `cardPlay.Card.Owner.Creature` を Actor に記録するため、
MOD も記録済み Actor と `CardPlay.IsAutoPlay` を読む。
`HappenedThisTurn` は本体のラウンド・side・全 Player の TurnNumber 判定に委譲する。

パエルの眼の `AnyCardsPlayedThisTurn()` は、先に
`owner.PlayerCombatState?.TurnNumber == 1` かつ `owner.Relics` に `WhisperingEarring` がある場合に true を返す。
その局面では手動プレイがなくてもこの履歴値は false を保ち、レリックの判定値は true になる。
利用側は公開された `turn_number`・所持レリックとこの履歴値からその条件を評価できる。
`PaelsEye.ShouldTakeExtraTurnEarly` は未使用・判定値 false・所有者の前回参加・対象 Player が所有者の条件を使い、
`BeforeSideTurnEndEarly` は所有者が participants に含まれる条件も検査して手札を廃棄する。
既存の `PAELS_EYE.combat_state.used_this_combat` と `was_owner_part_of_last_player_turn` も合わせて読む。
参照元は v0.113.1 `sts2-a671e9ef` の `MegaCrit.Sts2.Core.Models.Relics.PaelsEye`、
`MegaCrit.Sts2.Core.Combat.History.CombatHistory.CardPlaysFinished`、
`CardPlayFinishedEntry` のコンストラクタ、`CombatHistoryEntry.HappenedThisTurn`、`CardPlay.IsAutoPlay`。
副作用のあるフックを呼ばず、trace の履歴の保持期間にも依存しない。
追加手番の開始後も本体の手番判定に従う。行動計画には Player の play phase かつ
`is_resolving: false` の snapshot を使う。
所持の判定は `player.relics` に `WHISPERING_EARRING` があるかで行い、`melted` は見ない（本体は `Relics.Any(r => r is WhisperingEarring)` で、溶けたレリックも `_relics` に残る）。

開始数の Attack は `JugglingPower.AfterApplied` の初期化式とは一致するが、
ジャグリングの実際の判定は private な `Data.attacksPlayedThisTurn` を使う。
その後は `BeforeCardPlayed` が `cardPlay.Card.Owner == Owner.Player` の条件で加算し、
`AfterSideTurnEnd` がリセットするため、`CardPlay.Player` で絞る履歴とは
所有者が変わる札などで一致しない。この履歴をジャグリングの内部値の代用にしない。
正確な値は `JUGGLING_POWER` の `status[].attacks_played_this_turn` を読む。

`bound_afflictions_this_turn` は`Bound`（札の状態）の付与 entry 数で、手札や各パイルに
現在残る Bound の枚数ではない。同じ札への複数回付与も別件として数え、
その札が移動したり Affliction が解除されたりしても当該手番の履歴は減らさない。
パワーの有無・Amount で絞ったり上限に丸めたりせず、未発生なら 0 を明示する。
使用済み状態は別の値であり、`CHAINS_OF_BINDING_POWER` の `status[].bound_card_played` を読む。

このターンと前手番の uid 配列はどちらも履歴順で重複を保つ。他 Player のプレイも含む。
これは札の所有者がプレイ後に変わり得るため、Bolas / ThrummingHatchet が
`CardPlay.Player` ではなく札の個体を調べる式に合わせている。
`CardPlay` の注釈もプレイ時の Player と現在の `Card.Owner` の違いを明記している。
uid は手札・各パイル・grid と共通の戦闘単位の registry を使い、履歴にしか残らない札にも
同じ uid を割り当てる。`HappenedLastPlayerTurn` は Player の TurnNumber が
現在値−1である entry を返すため、単純な前ラウンドや前の player side の意味に置き換えない。
`HappenedThisTurn` もラウンド・side・記録された全 Player の TurnNumber を検査する本体の関数を使う
（`CombatHistoryEntry.HappenedThisTurn` / `HappenedLastPlayerTurn`）。
`*_this_turn` は state を読んだ時点の side の手番を指すため、敵の手番の snapshot では
敵の手番の出来事を数え、Player の手番の件数は含めない。計画は Player の play phase の snapshot で読む。

Player 側の play phase で読む `card_plays_finished_this_turn_card_uids` は、
次手番の `BeforeHandDraw` で `HappenedLastPlayerTurn(owner)` が見る集合のうち、
これまでに起きた分と一致する。このターンに手斧などを撃ったあと観測が途切れても、
次手番で回収する個体をこの配列から判定できる。前手番の配列だけでは、現在の手番に
完了した個体は分からない。`PaelsEye` による追加ターンも Player の TurnNumber で区別する
（`CombatManager.SwitchSides` の `IncrementTurnNumber` 呼出し、`PlayerCombatState.IncrementTurnNumber`）。
敵の手番の snapshot のこの配列は、次の Player 手番の回収の判定には使わない。

`block_gains_this_turn` は不動用のカード由来の獲得回数で、レリックなどを含む全獲得回数ではない。
本体の式には Receiver・Amount の条件も distinct もないため追加しない。
同一 CardPlay が複数回ブロックを得たら複数件、別 Player のカードが自分に与えたブロックは除外し、
自分のカードが別の対象に与えたブロックは含める。
本体は実行中の `cardPlay` が非 null の場合、その CardPlay と同一の entry を除く。
この要約はその実行中の文脈を再現する値ではなく、次のプレイ用の null 文脈の件数である。
計画には既存の契約どおり `is_resolving: false` の snapshot を使う。

正常な空履歴は 0 と `[]` と空の `unavailable_reasons`。
読取・列挙・型や getter の変更等で取得できない項目は、その**項目全体を `null`** にし、
`unavailable_reasons[項目名]` に `ExceptionType: message`、state の `warnings` に
`player.combat_history.<項目名>: ...` を出す。部分的な合計・配列や代用の 0 は返さない。
他の取得可能な値と state は保持する。特に combat state が無ければターン判定を false として扱わず
該当項目を null にする。private フィールドは不要であり、本体の public API を使う。

Markdown は手動プレイの bool・正の件数・非空の uid 配列・取得失敗を短い History 行に出す。
手動プレイは false も `manual card played=no`、true は `manual card played=yes` と表示する。
このターンの非空 uid は `this turn=[...]`、前手番の非空 uid は `last turn=[...]` と区別する。
`Bound` の正の付与件数は `bound afflictions=2` のように表示する。
既知の 0 と空配列は省略し、TurnNumber 単独では行を増やさない。
失敗時は null と理由を表示する。既存の意味の変更・削除を伴わない追加なので
版は上げていない（schema 4 のときの追加）。利用側は `combat_history` と必要な各項目の有無で検出する。
`Bound` の付与件数を出さない旧 MOD の欠落を 0 と解釈しない。
手動プレイの項目も旧 MOD では欠落し、欠落・null は unknown として扱う。
読取不能時は `any_manual_cards_played_this_turn: null` と同名の `unavailable_reasons`、
`player.combat_history.any_manual_cards_played_this_turn` の warning を出す。

#### 札の解決の通し列（card_resolution_events、schema 1.6.0）

`card_play_events_this_turn` は今の手番のプレイだけなので、前の手番の終わり（闇の抱擁のエセリアルのドローと、そこで引いた札の
ヘルレイザーの自動プレイ）・敵の手番（敵が捨て札に入れる火傷）・次の手番の初め（通常のドロー、その中の自動プレイ、鉄の棍棒のドロー）が
どの順に起きたかは、次の入力の state の 4 山の最終位置からは決まらない（同じ札が引かれ、自動プレイで捨て札に行き、シャッフルでまた引かれると、
最終位置には 1 回しか現れない）。`card_resolution_events` は、プレイヤーが画面で見たこれらの出来事を、本体が記録した順のまま 1 本の列で出す
（`cat12079801/sts2#1088`）。

- **範囲**: `CombatManager.Instance.History.Entries` の全件を元の順のまま走査する。手番の条件は付けない（戦闘全体）。
  本体の履歴は戦闘の終わり（`CombatManager.EndCombatInternal`）と戦闘の破棄（`CombatManager.CancelCombat`）で `History.Clear()` され、
  `combat_history` 自体も戦闘中だけ出るので、列は 1 つの戦闘の中だけで続き、次の戦闘は空の列から始まる。
- **選ぶ entry と所有者**:
  - ドロー: `CardDrawnEntry` で `Actor == owner.Creature`。`CardPileCmd.DrawInternal` が札を手札に移した直後、`Hook.AfterCardDrawn` の前に
    `CombatHistory.CardDrawn` で記録する。entry の Actor はコンストラクタが `card.Owner.Creature` を入れる。
  - プレイ: `CardPlayStartedEntry` / `CardPlayFinishedEntry` で `CardPlay.Player == owner`（`card_play_events_this_turn` と同じ条件）。
  - 生成: `CardGeneratedEntry` で `Actor == owner.Creature`。`CardPileCmd.AddGeneratedCardsToCombat` が山に入れる直前に記録する（戦闘中の変化
    `CardCmd.Transform` の置き換え後の札も `CombatHistory.CardGenerated` を通る）。Actor は札の持ち主なので、敵の行動が作った火傷
    （`Creator == null`）も、それが入った山の持ち主の列に出る。
  - それ以外の entry（廃棄・捨てる・ダメージ・ブロックなど）は出さない。
- **各行**（`kind` で種類を分ける）:

  | `kind` | キー | 値 |
  |---|---|---|
  | `draw` | `uid` | `CardDrawnEntry.Card` を手札・各パイルと共通の `GetStableCardUid` で変換 |
  | | `from_hand_draw` | `CardDrawnEntry.FromHandDraw`（bool）。手番の初めの通常のドロー（`CombatManager.SetupPlayerTurn` の `fromHandDraw: true`）なら true、札・レリック・パワーが引かせたドローは false |
  | `play` | `phase` ほか | `card_play_events_this_turn` の要素と同じ `phase`・`uid`・`is_auto_play`・`target`・`play_index`・`play_count`（同じ `BuildCardPlayEvent` で作る） |
  | | `reservation` | schema 1.8.0。このプレイが山札からの自動プレイの予約の消化（`auto_play_dispatch`）から始まったなら `{ call_id, reserve_index }`、それ以外は null。下の「自動プレイの予約」 |
  | `auto_play_reservation` ほか | | schema 1.8.0。下の「自動プレイの予約」 |
  | `generated` | `card_id` | `CardGeneratedEntry.Card.Id.Entry` |
  | | `uid` | 同じく `GetStableCardUid`（生成札は `#g<number>` の空間） |
  | | `creator` | `CardGeneratedEntry.Creator`（`Player?`）。所有者なら `"owner"`、ほかのプレイヤーなら `"other_player"`、プレイヤーでない（敵の行動など）なら `null` |

- **接頭部が変わらない**: entry は追加されるだけで、各行の値（entry・`CardPlay`・戦闘の uid / entity id の registry）は記録の後に変わらない。
  したがって同じ戦闘の後の snapshot の列は、前の snapshot の列で始まる。差分は「前の列の長さ以降」で取れる。
  手番を示す値（`RoundNumber`・`CurrentSide` は entry の private）や、読むたびに変わる「この手番か」の印は付けない。
  手番の境界は `from_hand_draw` と、今の手番の `card_play_events_this_turn` との突き合わせで読む。
- **出さないもの**: 山札の順・まだ引いていない札・乱数・次の対象。ドローの行は手札に入った札（画面に表を向けて出た札）だけで、
  生成の行は札の ID と uid だけ（山の中の位置は出さない。生成札は `draw_pile` などの一覧にもすでに出ている）。
  また、この列は札の移動のすべてではない。シャッフル（`CardPileCmd.Shuffle`）と、ドローでない山の移動（`CardPileCmd.Add` で
  山札・捨て札・廃棄札から手札へ、または山札の上へ動かす効果。`Wish`・`LockedIn`・`SeekerStrike` などの札）は `CardDrawnEntry` を作らないので行にならない。
  手札に入った経路のすべて・山の中身の変化のすべてとして読まず、山の中身は 4 山の一覧（`hand` / `draw_pile` / `discard_pile` / `exhaust_pile`）で読む。
- **重複・順序**: 同じ札の再ドロー・同じ札の複数回のプレイ（自動プレイ・リプレイ）は行ごとに残す。並べ替え・重複除去・要約をしない。
  入れ子（親の開始 → 子のドローと自動プレイ → 親の完了）も記録の順のまま。
- **失敗**: 読取（History・entry の判定・各 getter・対象の `CombatId`）のどれかに失敗したら配列全体を `null` にし、
  `unavailable_reasons.card_resolution_events` と warning `player.combat_history.card_resolution_events` を出す。1 行を飛ばした短い列は返さない。
  正常な空の戦闘履歴は `[]`。行は戦闘の終わりまで消えずに残るので、ある行が読めなくなったら、その戦闘の後の snapshot も
  `null` のままになりうる（v0.113.1 では起きる経路を確認していない。`CombatId` は消されず、uid は必ず振れる）。
  利用側はこれを未観測として扱い、止まる側に倒す（短い列や空の列に読み替えない）。
- **戦闘の終わり**: 勝利の後の state（報酬・マップ）・敗北の後の state には `combat_history` が無く、勝利では本体の履歴も消える。
  戦闘を終えた単独の POST の応答には、戦闘が終わった時点の列を `ended_combat` で付ける（下の「戦闘を終えた操作の応答」。schema 1.8.0）。
  終局の HP などは報酬の画面の公開値で読む。
- **Markdown**: 列が長いので、History 行に `resolution events=13 (draw:4, play:5, generated:4)` のように種類ごとの件数だけを出す
  （`play` は開始・完了の行の数）。行そのものは JSON で読む。

出典は v0.113.1 `sts2-a671e9ef` の `CombatHistory.CardDrawn / CardGenerated / CardPlayStarted / CardPlayFinished / Clear`、
`CardDrawnEntry.Card / FromHandDraw`、`CardGeneratedEntry.Card / Creator`、`CombatHistoryEntry.Actor`、
`CardPileCmd.DrawInternal / AddGeneratedCardsToCombat`、`CardCmd.Transform`、`CombatManager.EndCombatInternal / CancelCombat`。

契約試験と実機確認の観点は [`tests/CombatHistoryContract`](../tests/CombatHistoryContract/README.md)。

#### 自動プレイの予約（card_resolution_events の予約の行、schema 1.8.0）

カスケード（`Cascade.OnPlay`）・荒廃（`Havoc.OnPlay`）・悪魔（`TheDevil.OnPlay`）・騒乱（`MayhemPower.AfterAutoPrePlayPhaseEntered`）・
`IAmInvincible.AfterAutoPostPlayPhaseEntered`・`DistilledChaos.OnUse`・`LostLutePower` は、どれも
`CardPileCmd.AutoPlayFromDrawPile` の 6 引数の版（5 引数の版はそのまま 6 引数の版を呼ぶ）を通る。この版は 2 段で動く:

1. **確保**: `CombatManager.IsOverOrEnding` なら何もせずに戻る。そうでなければ、最大 `count` 回、`ShuffleIfNecessary` の後に
   山札の先頭（`CardPilePosition.Top`）の札を選び、`cardModification` を呼んでゲームの一覧 `cards` に積み、`CardPileCmd.Add(card, PileType.Play)` で
   Play に移す。山札と捨て札が両方とも空になると、そこで確保を打ち切る。
2. **消化**: `cards` の順に、所有者が死んでいれば打ち切り（残りの札は Play に残る）、そうでなければ `ExhaustOnNextPlay = forceExhaust` を付けて
   `CardCmd.AutoPlay(choiceContext, card, null)` を呼ぶ。`AutoPlay` は `IsOverOrEnding` か所有者の死亡で何もせずに戻り（子が戦闘を終わらせると、
   後ろの札は Play に残ったまま消化されない）、プレイ不可（`CardKeyword.Unplayable`）・`Hook.ShouldPlay` の拒否・対象が無いときは、開始の履歴を作らずに
   `CardCmd.MoveToResultPileWithoutPlaying` → `CardModel.MoveToResultPileWithoutPlaying` で結果の山（廃棄〔`ExhaustOnNextPlay`・廃棄のキーワード〕・
   捨て札・複製は戦闘から除外）に送る。それ以外は `CardModel.OnPlayWrapper` で普通にプレイする（開始・完了の行が出る）。

**プレイヤーに見えるもの**: 確保の 1 枚ごとに、`CardPileCmd.Add` が札の node を作って表を向けて描き（`CreateCardNodeAndUpdateVisuals` →
`NCard.UpdateVisuals`。絵・名前・本文。`NCard.Visibility` は戦闘中つねに `Visible`）、山札の位置から画面中央の Play の位置
（`PileType.Play.GetTargetPosition`）へ飛ばす tween（`AppendPlayPileLerpTween`）の終わりを待ってから次の札に進む。したがって確保した札と、
その確保の順は、移した時点で画面に表で出る（後の札が前の札に重なる）。既存の `play_pile` の一覧も、同じ時点の `PlayPile.Cards` を同じ順で出している。
まだ移していない札（山札の残りの順）・選ぶのに使う乱数（`CardPilePosition.Random` の `CombatCardSelection`。いまの呼び手はどれも `Top`）は出さない。

**行**（`card_resolution_events` の中に、記録した時点の `History.Entries` の件数の後ろ、つまりその時点までの entry の後・その後に記録された entry の前に置く。
同じ位置の行は記録の順。記録の後に変わらないので、接頭部の性質はそのまま保たれる）:

| `kind` | キー | 値 |
|---|---|---|
| `auto_play_reservation` | `call_id` | 戦闘ごとの呼出しの通し番号（1 から。全 Player の呼出しで数える） |
| | `parent_call_id` | 親のプレイが別の呼出しの消化から始まったなら、その呼出しの `call_id`（カスケードで出た荒廃の確保など）。それ以外は null |
| | `parent` | 呼出しの時点の `PlayerChoiceContext.LastInvolvedModel`。札の `OnPlay` なら、`CardModel.OnPlayWrapper` が札を積んだ `BranchingPlayerChoiceContext` の先頭がその札。フックなら `Hook` が積んだ listener（騒乱など）、ポーションなら `PotionModel.OnUseWrapper` が積んだポーション。`{ kind, model_id, uid, hidden_reason }`。`kind` は `card` / `potion` / `power` / `relic` / `other`、`uid` は札のときだけ。親の札が山札にある（`IAmInvincible` の自分のフック）ときは、移す前の山札の先頭を明かさないよう `kind`・`model_id`・`uid` を null、`hidden_reason: "draw_pile"`（その手のフックを持つ札は 1 種類なので、`kind` だけでも、確保が失敗して確保の行が続かないときに山札の先頭が推せる。行は記録の後に変えないので、確保の行が続いても伏せたまま）。context にモデルが無ければ `parent` 自体が null |
| | `parent_play_started_index` | 親の札の、まだ完了していないプレイ（`CardPlayStartedEntry` があって同じ `CardPlay` の `CardPlayFinishedEntry` が無い、最後のもの）の開始の行の、この配列での添字。親が札のプレイでない・そのプレイが別の Player の列にあるときは null |
| | `requested_count` | 引数 `count`（カスケードは X〔強化で +1〕、荒廃は 1） |
| | `position` | 引数 `position` の小文字（`top` / `bottom` / `random`） |
| | `force_exhaust` | 引数 `forceExhaust`（荒廃は true） |
| `auto_play_reserved` | `call_id`・`reserve_index`・`uid` | 札が所有者の Play に入った（`CardPile.AddInternal`）時点。`reserve_index` はゲームの一覧 `cards` での位置（0 から）。`Add` が失敗した札（戦闘の山への移動で `CombatManager.IsEnding`・`!CombatManager.IsInProgress`、札ごとに `HasBeenRemovedFromState`・所有者の死亡）は Play に入らず、表にも出ないので行を作らない（そのとき `reserve_index` が飛ぶ） |
| `auto_play_dispatch` | `call_id`・`reserve_index`・`uid` | 消化のループが `CardCmd.AutoPlay` を呼び、`AutoPlay` が冒頭で戻らなかった（戦闘が続いていて所有者が生きている）時点。この後に `reservation` の付いた開始の行が無ければ、札はプレイされずに結果の山に送られた（プレイ不可など）か、開始の前に戦闘が終わった |

- **子のプレイ**: 消化から始まったプレイ（リプレイで複数回になるものも）の開始・完了の行には `reservation: { call_id, reserve_index }` が付く。
  対象は既存の `target`。消化の外の自動プレイ（予約を通らないもの）は `reservation: null`。
- **残った予約**: `auto_play_reserved` があって `auto_play_dispatch` が無い札は、まだ消化されていない（解決の途中の snapshot）か、
  所有者の死亡・終戦で Play に残った。戦闘が続いているあいだ、消化されない札は残らない。
- **入れ子**: 子の荒廃の確保は、子の開始の行の後に、`parent_call_id` を親の呼出しにした `auto_play_reservation` として出る。
  `parent_call_id` は親が札のプレイのときだけ決まる。フックが呼ぶ予約（子のプレイの `AfterCardPlayed` から呼ぶ `LostLutePower` など）は、
  親が札でないので `parent_call_id: null` になり、どの子のプレイの中で起きたかは行の位置（その子の開始と完了の間）からしか読めない。
  アイアンクラッドの範囲（カスケード・荒廃・悪魔・騒乱）では、フックの呼出しが子のプレイの中で起きることはない。
- **読めないとき**: 下の読取専用の patch が入らなかったとき（版の更新で名前が変わったなど）と、この戦闘で記録に 1 回でも失敗したときは、
  予約の無い短い列を返さず、配列全体を null・理由・warning にする（記録の失敗は次の戦闘で消える）。
- **ゲームへの影響**: Harmony の patch はすべて読み取りで、ゲームの呼出しを飛ばさず・順を変えず・乱数を引かない。
  `AutoPlayFromDrawPile` の前置（呼出しの記録と、`cardModification` を「元の delegate を同じ札で呼んでから札を控える」delegate に包む）・
  後置（`Task` の完了で呼出しを閉じる）、`CardPile.AddInternal` の後置、`CardCmd.AutoPlay` の前置・後置、`CombatHistory.CardPlayStarted` の前置
  （開始の entry に予約を結ぶ）、`CombatHistory.Clear` の前置・後置（戦闘の終わりの列を取ってから予約を捨てる）、`CombatManager.ProcessPendingLoss` の前置。
- **Markdown**: 既存の History 行の件数に `auto_play_reservation:1, auto_play_reserved:3, auto_play_dispatch:3` のように種類ごとに足される。

出典は v0.113.1 `sts2-a671e9ef` の `CardPileCmd.AutoPlayFromDrawPile`（5・6 引数）/ `ShuffleIfNecessary` / `Add` / `GetTweenForCardsChangingPiles` /
`CreateCardNodeAndUpdateVisuals` / `AppendPlayPileLerpTween`、`PileTypeExtensions.GetTargetPosition`、`NCard.UpdateVisuals`、
`CardCmd.AutoPlay` / `MoveToResultPileWithoutPlaying`、`CardModel.OnPlayWrapper` / `MoveToResultPileWithoutPlaying`、`CardPile.AddInternal`、
`PlayerChoiceContext.LastInvolvedModel`、`BranchingPlayerChoiceContext`、`Hook.AfterCardPlayed` などの `PushModel` / `PopModel`、
`Cascade.OnPlay`、`Havoc.OnPlay`、`TheDevil.OnPlay`、`MayhemPower`、`IAmInvincible`、`DistilledChaos`、`LostLutePower`。

#### 戦闘を終えた操作の応答（ended_combat、schema 1.8.0）

戦闘の勝利では `CombatManager.EndCombatInternal` が `History.Clear()` を呼び、敗北では `CombatManager.ProcessPendingLoss` が `IsInProgress` を下ろすので、
戦闘を終えた操作の後の state には `combat_history` が無い。最後の子のプレイで戦闘が終わったカスケードの予約・子の解決を同じ操作から読めるように、
戦闘が終わった時点のローカル Player の `card_resolution_events` を取っておき、**その間に戦闘が終わった単独の POST の応答**に付ける:

```jsonc
{
  "status": "ok",
  "...": "...",
  "ended_combat": {
    "outcome": "victory",
    "card_resolution_events": [ /* 戦闘が終わった時点の列。行は上と同じ */ ],
    "combat_log": { /* 戦闘が終わった時点の battle.combat_log（seq 0 から保持している全行。schema 1.10.0） */ },
    "unavailable_reasons": {}
  }
}
```

- 取る時点: 勝利は `CombatHistory.Clear` の前（`EndCombatInternal` の中。turn state がまだある）で `outcome: "victory"`、敗北は
  `ProcessPendingLoss(turnState)` の前で、引数の turn state の `PendingLoss` があるときにその `State` から `outcome: "loss"`
  （現在の turn state でなく引数を読むので、古い turn state が再開して自分の戦闘を終えるときに別の戦闘を取らない）。
  戦闘の破棄（`CancelCombat`。turn state を先に捨てる）では取らない。
- 敗北の取得は別の patch（Harmony ID `com.sts2mcp.endedcombatloss`）。これが入らなくても失うのは敗北の `ended_combat` だけで、
  `card_resolution_events` は出る（ゲームのログに `Ended combat capture of a loss unavailable`）。敗北の応答に `ended_combat` が無いことを、
  列が空だった証拠にしない。
- 付ける応答: 操作の前に控えた取得の通し番号より後に取ったものがあれば付ける。`wait: true` なら落ち着くまでの間に終わった戦闘も入る。
  `wait` なしの応答は、メインスレッドでの操作の実行の中で終わった戦闘だけ（ふつうは後で終わるので付かない）。後の操作には繰り返さない。マルチの POST には付けない。
- **engine は `wait: true` で落ち着いた（`settled: true`）応答の `ended_combat` だけを信じる。** `wait` なし・待ちの上限（`settled: false`）では、
  `EndCombatInternal` の `History.Clear` より前に応答が返ることがあり、そのときの取得は応答に付かず、戦闘の終わりと期間が重なっただけの
  次の POST に付くことがある（その POST の操作の結果ではない）。
- 列が読めなければ `card_resolution_events: null` と `unavailable_reasons.card_resolution_events`。
- `combat_log`（schema 1.10.0）は同じ時点の [戦闘の出来事の通し列](#戦闘の出来事の通し列battlecombat_logschema-1100) を `combat_log_since=0` で
  読んだもの（保持している全行。捨てた行があれば `since_truncated: true`）。勝利では `AfterCombatEnd` の通知までの行が入り、
  その後の `AfterCombatVictory` の行は入らない。読めなければ `null` と `unavailable_reasons.combat_log`。

出典は v0.113.1 `sts2-a671e9ef` の `CombatManager.EndCombatInternal` / `ProcessPendingLoss` / `CancelCombat` / `DebugOnlyGetState` / `IsAboutToLose`、
`CombatHistory.Clear`、`LocalContext.GetMe`。試験は [`tests/DevConsoleContract`](../tests/DevConsoleContract/README.md)（応答への付け方）。

### 戦闘の出来事の通し列（`battle.combat_log`、schema 1.10.0）

戦闘中の `battle`（戦闘の画面と、戦闘中の選択画面の `battle`）に、その戦闘で起きたことを起きた順に並べた列を出す。
棚卸し（[`docs/event-inventory.md`](event-inventory.md)）で public-missing だった、通知の順・履歴の各種・山の移動・
手番の段階・選択の結果などを 1 本にしたもの。`card_resolution_events` はそのまま残す（同じドロー・プレイの行がこちらにも入る）。

```jsonc
"combat_log": {
  "version": 1,
  "known": true,
  "combat_id": "3",            // CombatManager.CurrentCombatId
  "capacity": 2000,            // 1 戦闘で保持する行数（超えたら古い行から捨てる）
  "default_window": 100,       // combat_log_since が無いときに返す行数（最後の N 行）
  "first_seq": 0,              // 保持している最も古い行の seq
  "next_seq": 182,             // 次の行の seq（= この戦闘で書いた行数）
  "window_first_seq": 82,      // rows[0] の seq
  "since_truncated": false,    // combat_log_since が first_seq より古かった（間の行は捨てた）
  "rows": [
    { "seq": 140, "kind": "turn_phase", "round": 2, "side": "player", "phase": "setup_player_turn", "player_id": "1" },
    { "seq": 141, "kind": "energy", "round": 2, "side": "player", "player_id": "1", "before": 0, "after": 3 },
    { "seq": 142, "kind": "modify", "round": 2, "side": "player", "hook": "ModifyHandDraw", "player_id": "1",
      "original": 5, "result": 5, "modifiers": [] },
    // CardPileCmd.DrawInternal は札を手札へ移して（pile_move と AfterCardChangedPiles）から履歴に書く（draw）
    { "seq": 143, "kind": "pile_move", "round": 2, "side": "player",
      "card": { "card_id": "BASH", "uid": "BASH#9" }, "owner_player_id": "1",
      "from": "Draw", "to": "Hand", "requested_to": "Hand", "position": "bottom", "via": "card_pile_cmd_add", "cloned_by": null },
    { "seq": 144, "kind": "draw", "round": 2, "side": "player", "source": "history", "entry": "CardDrawnEntry",
      "actor": { "combat_id": 1, "player_id": "1" },
      "card": { "card_id": "BASH", "uid": "BASH#9", "pile": "Hand" }, "from_hand_draw": true },
    { "seq": 145, "kind": "hook", "round": 2, "side": "player", "hook": "AfterCardDrawn", "scope": "combat",
      "args": { "card": { "card_id": "BASH", "uid": "BASH#9", "pile": "Hand" }, "from_hand_draw": true },
      "passes": [
        { "method": "AfterCardDrawnEarly", "listeners": [] },
        { "method": "AfterCardDrawn", "listeners": [
          { "model_type": "ChainsOfBindingPower", "model_id": "CHAINS_OF_BINDING_POWER", "kind": "power",
            "owner_combat_id": 1, "owner_player_id": "1", "declared_by": "ChainsOfBindingPower", "order": 3 } ] } ] },
    { "seq": 146, "kind": "flash", "round": 2, "side": "player", "model": { "model_id": "CHAINS_OF_BINDING_POWER", "kind": "power", "...": "..." } },
    { "seq": 160, "kind": "shuffle", "round": 2, "side": "player", "player_id": "1", "reason": "draw_pile_empty", "discard_count": 31, "draw_count": 0 },
    { "seq": 161, "kind": "hidden_rows", "round": 2, "side": "player", "reason": "shuffle_order", "count": 31 }
  ]
}
```

- 行は記録したら変わらない（`seq` の順。同じ戦闘の後の読みは、前の読みの行を同じ `seq` で含む）。行の順はゲームが
  その処理を行った順で、対になる行が隣り合うとは限らない（例: ドローは `pile_move` → `AfterCardChangedPiles` の `hook` →
  `draw` → `AfterCardDrawn` の `hook`）。対は `seq` の隣でなく札の `uid` で結ぶ。新しい戦闘（`CombatState` が
  替わる）で空になり `seq` は 0 から。戦闘の外・戦闘の始まる前には行を書かない。
- `?combat_log_since=<seq>`（GET の state だけ）: その `seq` から、保持している全行を返す。無ければ最後の `default_window` 行
  （100 行。state と dump を重くしないため。全行は `combat_log_since=0` と `ended_combat.combat_log` で取る）。
  捨てた行より前を求めたら `since_truncated: true` で、保持している先頭から返す。読み取りのたびに戻す（次の要求に持ち越さない）。
- 共通の項目: `seq`・`kind`・`round`（`CombatState.RoundNumber`）・`side`（`CurrentSide`）。札は `{card_id, uid, pile}`
  （uid は state の他の行と同じ登録簿）、生き物は `{combat_id, entity_id | player_id | monster_id}`、モデル（listener・修正した
  モデル・光ったもの・`cloned_by`）は `choice_source` の listener の行と同じ形（`BuildHookListenerRow`）。
- `known: false` と `unavailable_reasons`: patch の群を当てられなかった（`install: <群>: …`。その群の行は出ない）、または行を
  作れなかった（`<kind>: …`。その戦闘の列に穴がある）。穴のある列を完全な列として読まない。群は history・hooks・modify・
  piles・cards・creatures・energy・potions・powers・flash・phases で、それぞれ独立に当てる。どの群が当たったかは
  この `unavailable_reasons` だけで分かる（棚卸しの `--check` は文字列があるかしか見ない）。当て先が本物の `sts2.dll` で
  解決するかは [`tests/PatchTargetsContract`](../tests/PatchTargetsContract/README.md) が確かめる。

`kind` の一覧（出典は v0.113.1 `sts2-a671e9ef`）:

| kind | いつ（patch） | 主な項目 |
|---|---|---|
| `draw` `generated` `play` `card_discarded` `card_exhausted` `card_afflicted` `block_gained` `damage_received` `attacked` `energy_spent` `monster_move` `potion_used` `power_received` `history` | 履歴に記録したとき（`CombatHistory` の公開の記録メソッド 18 件〔`CardPlayStarted` … `Summoned`。それぞれ *Entry を 1 つ作って private の `Add` に渡す〕の prefix・postfix。呼出しで記録が 1 件増えたときだけ行にする。`IsLiveCombat` で捨てられた記録は出さない。private の `Add` には当てない: 先に別の patch で作られた `CardPlayStarted` の置き換えには `Add` がインライン化されていて、`Add` の patch を通らなかった） | `source: "history"`・`entry`（*Entry の型）・`actor`。`play` は `phase`（started / finished）と `card_play_events_this_turn` と同じ項目、`damage_received.result` と `attacked.hits[]` は `receiver`・`original_target`（`Hook.ModifyPreBlockDamageTarget` / `ModifyUnblockedDamageTarget` の差し替えの前）・`blocked`・`unblocked`・`overkill`・`block_broken`・`fully_blocked`・`killed`・`props`、`monster_move` は `move_id`（`MoveState.StateId`）・`targets`、`power_received` は `power`・`amount`・`applier` |
| `hook` | 通知 53 件（`Hook.After*` / `Before*` のうち棚卸しで public-missing だったもの）の始まり（static メソッドの prefix） | `hook`・`scope`（`combat` = `IterateCombatHookListeners`〔`ShouldSkipCombatEffect` の間は空〕、`combat_all`、`run`）・`args`（引数。札・生き物・パワー・ポーション・プレイ・ダメージの結果・修正したモデルの列・数値・列挙）・`passes`（巡ごと〔Early・通常・Late〕の、その巡のメソッドを上書きしている listener） |
| `modify` | `Hook.ModifyHandDraw`（毎回）、`ModifyHpLost` / `ModifyPowerAmountGiven` / `ModifyPowerAmountReceived` / `ModifyCardPlayResultLocation`（修正したモデルがあるときだけ）の postfix | `hook`・`original`・`result`・`modifiers`。パワーの量は `PowerCmd` が渡す可変のパワーのときだけ（カードの説明の `PowerVar` のプレビューは正準のモデルを渡すので出来事にしない） |
| `pile_move` | 札が山に入ったとき（`CardPile.AddInternal` の prefix。`CardPileCmd.Add` が渡された札を置くときと、手動のプレイの Hand → Play。Add の listener の中で別の命令〔`CardCmd.Transform` など〕が置いた札は Add の行にしない） | `card`・`from`（直前の `RemoveInternal`）・`to`（手札が満杯なら捨て札）・`requested_to`・`position`（`top` / `bottom` / `random`）・`via`・`cloned_by` |
| `shuffle` `hidden_rows` | `CardPileCmd.Shuffle` の prefix | `reason`（`draw_pile_empty` = `ShuffleIfNecessary` が呼んだシャッフル、`explicit` = それ以外〔`AfterShuffle` の listener の中のシャッフルも〕）・`discard_count`・`draw_count`。シャッフル自身の `CardPileCmd.Add` の間の行（山の移動・`AfterCardChangedPiles`・その listener の行）は新しい山札の順に並ぶので書かず、Add が終わったときに件数を `hidden_rows`（`reason: "shuffle_order"`・`count`）で出す。その後の `AfterShuffle` の listener がする移動（計略の Draw → Hand など）は通常どおり行になる |
| `removed_from_combat` `exhaust` `discard_and_draw` `card_change` | `CardPileCmd.RemoveFromCombat`・`CardCmd.Exhaust`（`caused_by_ethereal`）・`CardCmd.DiscardAndDraw`（`cards`・`draw_count`）の prefix、`CardTransformation.GetReplacement` の postfix（`change: "transform"`・`pile`・`from`・`to`） | `CardCmd.Discard` は `DiscardAndDraw(…, 0)` に渡すので、ただの捨て札も `discard_and_draw`（`draw_count: 0`）の行になる |
| `creature` `hp` `block` | `CreatureCmd.Add`（`added`）/ `Kill`（`kill`・`force`）/ `Escape`、`Heal` / `SetCurrentHp` / `GainMaxHp` / `LoseMaxHp` / `SetMaxHp` / `SetMaxAndCurrentHp` / `LoseBlock` の prefix | `action`・`target`・`amount`・`hp_before`・`max_hp_before`・`block_before`（変わった後の値は次の行・state） |
| `energy` | `PlayerCombatState.Energy` の setter（変わったときだけ。どの命令でも） | `player_id`・`before`・`after` |
| `potion` | `PotionCmd.TryToProcure`（`procure`）・`Player.AddPotionInternal`（`added`・`success`・`failure_reason`）・`PotionCmd.Discard` | |
| `power` | `PowerCmd.Apply`（`apply`・要求した `amount`）/ `ModifyAmount` / `Decrement` / `TickDownDuration`（`skipped` = `SkipNextDurationTick`）/ `Remove` の prefix | 与えた後の量は `power_received` の行 |
| `flash` | `RelicModel.Flash` / `PowerModel.Flash`（効果が働いた合図） | `model` |
| `turn_phase` | `CombatManager` の手番の段階 12 件（表の並びは宣言の順。実際の順は下の注）（`StartCombatInternal`・`StartTurn`・`RunAutoPrePlayPhase`・`SetupPlayerTurn`・`EndPlayerTurnPhaseOneInternal`・`DoTurnEnd`・`ResolveTurnEndCardEffects`・`FlushPlayerHand`・`EndPlayerTurnPhaseTwoInternal`・`SwitchFromPlayerToEnemySide`・`EndEnemyTurnInternal`・`EndCombatInternal`）の始まり | `phase`（snake_case）・`player_id`・`card` |
| `selection` | `CardSelectCmd` / `RelicSelectCmd` の入口の Task が終わったとき（内側の入口だけ） | `selector`・`outer_selectors`・`opener`・`opened_at_seq`（開いたときの `next_seq`）・`offered`・`offered_from`・`chosen`・`skipped`。`offered` は入口に渡された札（`offered_from: "arguments"`）か、札の列を取らない入口では開いたときの手札・戦闘の山の札をその順で（`offered_from: "Hand"` / `"Discard"` など。選べるかはゲームが決める〔`FromHandForUpgrade` は強化できる札、`filter` の引数〕）。山札からの選択は `offered: null` と `offered_hidden_reason: "draw_pile"`、どちらも記録が無ければ `offered: null` と `offered_unavailable_reason` |

行にならないもの・順の注:

- 手番の段階の実際の順（プレイヤーの手番の終わりから）: `end_player_turn_phase_one_internal` → `do_turn_end`（→ 終了時の効果の札ごとに
  `resolve_turn_end_card_effects`）→ `end_player_turn_phase_two_internal` → `flush_player_hand`（`EndPlayerTurnPhaseTwoInternal` の中で
  呼ぶ）→ 手札の `pile_move` → `switch_from_player_to_enemy_side` → 敵の `start_turn` … → `end_enemy_turn_internal`。行は各メソッドの入口で書く。
- 手番の初めのブロックの消失（`Creature.ClearBlock`）は行にならない。`block` の行は `CreatureCmd.LoseBlock` だけ（消えたことは
  `AfterBlockCleared` の通知〔上書きする listener があるとき〕と state で見る）。
- 戦闘を終わらせる一撃（とどめ）には `damage_received` の行が無い。ゲームが `CreatureCmd.Damage` で `!CombatManager.IsEnding` のときだけ
  `History.DamageReceived` を書くため。`attacked` と `creature`（`kill`）の行はある。
- 戦闘の初めに山札へ札を入れる処理（`Player.PopulateCombatState`）と初回のシャッフルは行にならない。

隠すもの（山札の順・乱数・敵の非公開の情報には触らない）:

- 山札の中の移動（Draw → Draw）の `pile_move` は `card: null`・`hidden_reason: "draw_pile"`。`removed_from_combat` の山札の札も同じ。
- 置いた位置の添字は出さない（`CardPilePosition.Random` の添字は乱数から決まる）。`position` は呼出しの引数だけ。
- シャッフルの間の行は上のとおり件数だけ。
- 山札から選ぶ選択（`FromCombatPile` の Draw）の `offered` は `null`・`offered_hidden_reason: "draw_pile"`（渡された順が山札の順）。
  ゲームが自分で決めた選択（候補が `MinSelect` 以下）の `chosen` も山札の順で返るので、山札の表示順に並べ替える。
- `modify` の `modifiers` と通知の引数のモデルの列は listener の順なので、山札の札が 2 つ以上あればその枠の中を山札の表示順に並べ替える。
- 通知の listener のうち山札の札は `order: null` で、山札の表示順に並べる（`BuildHookListenerRows`、`choice_source` と同じ）。
- どの patch も読むだけ（呼出しを飛ばさない・引数と戻り値を変えない・乱数を引かない）。

`listeners` は通知が始まった時点の列で、巡（Early・通常・Late）ごとに、その巡のメソッドを上書きしているもの（上書きの無い
listener は AbstractModel の何もしない既定を走る）。ゲームは巡ごとに列を作り直すので、前の巡で増えた・減った listener は
違うことがある。上書きしていても何もしないことがある（働いたかは `flash` と後の行で見る）。上書きしている listener が 1 つも無い
通知は行にしない。

Markdown は `battle` の末尾に `### Combat Log (last 15 of seq A-B)` と 1 行ずつの要約（詳細は JSON）。

契約試験は [`tests/CombatLogContract`](../tests/CombatLogContract/README.md)。行が実機の順で出るか・patch が当たるかは実機で確かめる。

### レリックの数値変数（追加、schema 3）

`player.relics[]` の各所有レリックに `vars`（変数名 → JSON 数値）を追加する。
戦闘中・戦闘外、単独・マルチのローカル player に共通の `BuildPlayerState` から出す。
例: 骨付き肉は `"vars": {"HpThreshold": 50, "Heal": 12}`、
バーニングブラッドは `{"Heal": 6}`、ブラックブラッドは `{"Heal": 12}`。
変数名はゲームの `DynamicVars` のキーをそのまま使い、ローカライズしない。

カードの `vs_targets[].values` と同じ名前 → 数値のオブジェクトだが、
レリックではプレビューの整数値ではなく、現在の `DynamicVar.BaseValue`（decimal）を
丸めずに読む。例えば `BowlerHat` の `GoldIncrease` は `1.25`、
`UndyingSigil` の `DamageDecrease` は `0.5`。本文では百分率などに整形されることがある。
残り回数などゲームが更新する値も含み、初期定数だけの一覧ではない
（例: `WingedBoots.TimesUsed` が更新する `Rooms`）。数値を読むために
プレビュー更新・ゲームのフック・RNG・setter を呼ばない。集合は private `RelicModel._dynamicVars` をそのまま読み、
null なら作る `DynamicVars` の getter は呼ばない（schema 12、`cat12079801/sts2#1020`）。

`StringVar` は表示文字列で、継承した `BaseValue` の 0 はダミーのため除く。
数値変数がない、または文字列だけなら `vars: {}`。
集合が null・列挙の失敗のときは `vars` を null にし、レリック行の `unavailable_reasons.vars` と
`warnings` の `player.relics.<ID>.vars` に理由を書く（schema 12。それより前はキーごと省略していた）。
1 つの変数が読めなければその名前を null にし、`unavailable_reasons["vars.<名前>"]` と `warnings` の
`player.relics.<ID>.vars.<名前>` に理由を書いて、他の変数は残す。代用の 0 は返さない。null は unknown と扱い、空オブジェクトと区別する。
項目は schema 3 のときの追加。Markdown は読めた `vars` を出さず、失敗だけ `(vars: null (理由))` / `(vars: 名前=null (理由))` を付ける。

出典（sts2-9cb4f1ad）: `RelicModel.DynamicVars / CanonicalVars / DynamicDescription`、
`DynamicVar.BaseValue / IntValue / PreviewValue`、`DynamicVarSet.GetEnumerator / InitializeWithOwner`、
`StringVar` のコンストラクタと `ToString`、各レリックの `CanonicalVars`。
回復量と条件は `MeatOnTheBone.WillHealOnCombatFinished / AfterCombatVictoryEarly`、
`BurningBlood.AfterCombatVictory`、`BlackBlood.AfterCombatVictory`、
倍率は `BowlerHat.ModifyGoldGained`、`UndyingSigil.ModifyDamageMultiplicative` を参照。

### レリックの戦闘中状態（追加、schema 3）

`player.relics[]` の対象レリックに `combat_state` を追加する。
生きた戦闘と PlayerCombatState がある間だけ出し、戦闘外・対象外では省略する。
単体とマルチのローカル player は共通の実装。例えば百年パズルは
`"combat_state": {"used_this_combat": false}` が、初回の非ブロックダメージ後に true になる。
戦闘単位の消費は、ラン単位の `used_up` から独立している。

クラス別の許可リストにある bool / int / decimal だけを読み、false / 0 も出す。
公開 getter を優先し、private フィールドは名前・型を指定して読む。
private フィールドが読めなければキーを省略し、`warnings` に
`player.relics.<ID>.combat_state.<key>` を記録する。欠落は unknown と扱うこと。
ペン先 `attacks_played`、冥銭 `cards_exhausted` / `ethereal_count`、毛皮のコート
`affects_current_room` は、失敗時にキーを `null` にし、`combat_state.unavailable_reasons`
にもキーごとの理由を出す。成功した他の値は保持する。
追加項目なので版は上げていない（schema 4 のときの追加）。旧 MOD・旧 dump は追加項目を持たない。
Markdown にも `(used this combat: yes)`、`(used this turn: no)` 等を付ける。
虹の指輪は各種別の枚数と発動数、肉兵は cooldown、脈打つ亡骸は HP 損失累積を表示する。

schema 12（`cat12079801/sts2#1020`）で、手番ごとの数（苦無・手裏剣・扇子・鎖鎌の `attacks_played_this_turn`、
レターオープナーの `skills_played_this_turn`、懐中時計の `cards_played_this_turn`）と、オリハルコン（偽物も）の `should_trigger` を足した。
これらは失敗時にキーを `null` にし、`combat_state.unavailable_reasons` に理由を出す。`counter` は表示値（数 % 閾値、発動演出の間は閾値）なので、数そのものはこれを読む。

全項目名・意味・戻る時機と、出す／出さない判断のソース出典は
[レリックの戦闘中の内部状態](relic-combat-state.md) を参照。

### レリックの保存カウンタ（`run_state`、schema 12）

戦闘をまたいで残る `[SavedProperty]` の数・印を、戦闘中・戦闘外とも `player.relics[].run_state` に出す（`cat12079801/sts2#1020`）。
次の発動が次の戦闘・休憩所・報酬画面にあり得るため、`combat_state`（戦闘中だけ）とは分ける。
private の裏のフィールドを名前・宣言型で読み、getter・setter・フック・乱数は呼ばない。読めなければキーを `null` にし、
`run_state.unavailable_reasons.<key>` と `warnings` の `player.relics.<ID>.run_state.<key>` に理由を書く。対象外のレリックには付かない。
組み立て全体が失敗したら（型の読み込み・JIT など）`run_state: null` とレリック行の `unavailable_reasons.run_state`・`warnings` の
`player.relics.<ID>.run_state`。`combat_state` も schema 12 から同じ形（以前はキーごと省略）。

| レリック | 項目 ← private フィールド |
|---|---|
| ヌンチャク `Nunchaku` | `attacks_played` ← `_attacksPlayed` |
| 音叉 `TuningFork` | `skills_played` ← `_skillsPlayed` |
| `IronClub` | `cards_played` ← `_cardsPlayed` |
| `HappyFlower`・`FakeHappyFlower`・`Pendulum`・`PollinousCore` | `turns_seen` ← `_turnsSeen` |
| `Girya` | `times_lifted` ← `_timesLifted` |
| `LastingCandy` | `combat_rewards_seen` ← `_combatRewardsSeen` |
| `BookOfFiveRings` | `cards_added` ← `_cardsAdded` |
| `VenerableTeaSet`・`FakeVenerableTeaSet` | `gain_energy_in_next_combat` ← `_gainEnergyInNextCombat` |
| `PumpkinCandle` | `kindle_count` ← `_kindleCount` |
| `SwordOfStone` | `elites_defeated` ← `_elitesDefeated` |
| `WingedBoots` | `times_used` ← `_timesUsed` |
| `SilverCrucible` | `times_used` ← `_timesUsed`、`treasure_rooms_entered` ← `_treasureRoomsEntered` |
| `FishingRod` | `combats_seen` ← `_combatsSeen`（`RoomType.Monster` の戦闘だけ。エリート・ボスは数えない） |
| `ToyBox` | `combats_seen` ← `_combatsSeen`（使い切るまでのすべての戦闘） |
| `WongosMysteryTicket` | `combats_finished` ← `_combatsFinished` |
| `PaelsWing` | `rewards_sacrificed` ← `_rewardsSacrificed` |
| `LavaRock` | `has_triggered` ← `_hasTriggered` |

閾値・量は `vars`（例: ヌンチャクの `Cards`・`Energy`）にある。Markdown は `combat_state` と同じ形で `(attacks played: 7)` を足す。
各項目の意味・リセットの時機・出典は [レリックの戦闘中の内部状態](relic-combat-state.md#保存カウンタrun_stateschema-12) を参照。
解決中のフック間では過渡的な値が見え得るため、行動計画は解決後の snapshot を使う。

### Card Object (in hand)

```jsonc
{
  "index": 0,
  "uid": "STRIKE_IRONCLAD#0",
  "id": "STRIKE_IRONCLAD",
  "name": "Strike",
  "type": "Attack",          // Attack, Skill, Power, Status, Curse
  "cost": "1",               // Energy cost as string ("X" for X-cost)
  "star_cost": null,         // Regent star cost as string, null if N/A
  "description": "Deal 6 damage.",
  "target_type": "AnyEnemy", // None, Self, AnyEnemy, AllEnemies, etc.
  "affliction": null,
  "enchantment": null,
  "dynamic_vars": { "Damage": 6 }, // Current decimal BaseValue, by game variable name
  "unavailable_reasons": {}, // Card detail read failures, by field path
  "can_play": true,
  "unplayable_reason": null, // UnplayableReason flag names, e.g. "EnergyCostTooHigh"; null if playable
  "exhaust_on_next_play": false, // Combat-only CardModel.ExhaustOnNextPlay; see below
  "energy_cost": { /* Energy Cost Object below; combat only */ },
  "is_upgraded": false,
  "is_upgradable": true,
  "upgrade_level": 0,        // CardModel.CurrentUpgradeLevel
  "max_upgrade_level": 1,    // CardModel.MaxUpgradeLevel
  "local_keyword_ids": [],   // CardKeyword names on the card itself; see 戦闘の強化結果
  "keyword_ids": [],         // Effective in combat (local + Hex, Phantom Blades ...)
  "retain_this_turn": false, // CardModel.ShouldRetainThisTurn
  "single_turn_retain": false,
  "upgrade_preview": { "status": "ready", /* see 戦闘の強化結果 */ },
  "tags": ["Strike"],        // CardTag enum names; [] if none
  "keywords": [ /* Keyword Objects */ ]
}
```

Combat attack previews in `vs_targets[]` use the target's previewed `values` to report
`damage`, `hits`, `total_damage` and optional `damage_cap`. Hit count is read in the order
`HitCount`, `CalculatedHits`; `Repeat` is not an attack count. Explicit 0 and 1 counts are also reported.
Conditional attacks are corrected for the target and current situation before reporting
`hits`, `total_damage` or `damage_cap`: Dismantle hits once without Vulnerable on the target;
Spite hits once unless its owner lost HP this turn. When the condition is met, the previewed
multi-hit count (including upgrades and Thumbscrew) is retained. Thumbscrew does not increase
the single hit on a failed condition.
`total_damage` is `damage * hits`, and per-hit caps use that same corrected count. Without a count
variable the cap calculation assumes one hit. For random-target attacks, the total is
the whole attack's damage, not a guarantee that every hit lands on this enemy.

### Pile Card Object (draw/discard/exhaust/play piles)

```jsonc
{
  "uid": "STRIKE_IRONCLAD#0",
  "name": "Strike",
  "type": "Attack",
  "rarity": "Basic",         // CardModel.Rarity enum name, as on hand rows (schema 1.12.0)
  "target_type": "AnyEnemy",
  "is_upgraded": false,
  "is_upgradable": true,
  "tags": ["Strike"],
  "affliction": null,
  "enchantment": null,
  "dynamic_vars": { "Damage": 6 },
  "unavailable_reasons": {},
  "can_play": true,          // CardModel.CanPlay at the card's current pile (not "playable from here")
  "unplayable_reason": null,
  "cost": "1",               // Energy cost as string ("X" for X-cost)
  "star_cost": null,          // Regent star cost as string, null if N/A
  "exhaust_on_next_play": false,
  "energy_cost": { /* Energy Cost Object below */ },
  "description": "Deal 6 damage.",
  "upgrade_level": 0,
  "max_upgrade_level": 1,
  "local_keyword_ids": [],
  "keyword_ids": [],
  "retain_this_turn": false,
  "single_turn_retain": false,
  "upgrade_preview": { "status": "ready", /* see 戦闘の強化結果 */ }
}
```

### 戦闘カードの詳細（追加、schema 4）

`player.hand[]` / `draw_pile[]` / `discard_pile[]` / `exhaust_pile[]` /
`play_pile[]` は同じ読取処理で次の項目を出す。
単体・マルチのローカル player は共通の `BuildPlayerState` を通る。
対象は戦闘中の手札と4パイルで、各行は既存の `uid` で照合できる。

- `type` / `target_type` は `CardModel.Type` / `TargetType` の enum 名。
- `rarity` は `CardModel.Rarity`（`CardRarity`）の enum 名（`Basic` / `Common` / `Uncommon` / `Rare` / `Ancient` / `Event` /
  `Curse` / `Status` / `Quest` など、ゲームの `ToString()` のまま）。手札の行は共通の表示項目（`BuildCardInfo`）で
  同じ値を出し、4 パイルの行も同じ値を出す（schema 1.12.0）。札の表に描かれている（帯の色）公開の値。
  泥棒バッタの窃盗（`ThievingHopper.ThieveryMove`）は山札と捨て札の札から `Rarity != Quest` と `_stealPriorities`
  （アンコモン → コモン・レア・イベント → ベーシック → エンシェントかインビュード）で盗む札を絞る。
  4 パイルの行では読取失敗を null と `unavailable_reasons.rarity`・`warnings` にする。山札の行の並びは変えない（表示順のまま）。
- `is_upgraded` / `is_upgradable` は `CardModel.IsUpgraded`（`CurrentUpgradeLevel > 0`）/
  `IsUpgradable`（`CurrentUpgradeLevel < MaxUpgradeLevel`）の bool。手札では既存の値と同じ。
- `tags` は `CardModel.Tags` の `CardTag` enum 名の配列（`Strike` / `Defend` / `Minion` /
  `OstyAttack` / `Shiv` / `Wah` / `Cho` / `Ki` / `Shoot`）。ゲームは HashSet で持つので、
  enum の数値順に並べる。タグの無い札は `[]`。キーワード（`keywords`）とは別物で、
  `PerfectedStrike` や `PandorasBox` などが他の札を探すための目印（`CardTag` のコメント）。
- `can_play` / `unplayable_reason` は手札と同じ読取（`CardModel.CanPlay(out reason, out _)`）を
  4パイルの札にも行う。`unplayable_reason` は `UnplayableReason`（Flags）の enum 名で、
  複数なら `"HasUnplayableKeyword, EnergyCostTooHigh"` のようにゲームの `ToString()` のまま。
  値は `HasUnplayableKeyword` / `BlockedByHook` / `BlockedByCardLogic` / `EnergyCostTooHigh` /
  `StarCostTooHigh` / `NoLivingAllies`。
- `affliction` は `CardModel.Affliction` の `{ id, amount }`。
  `id` は `Id.Entry`、`amount` は現在の `Amount`（整数）。
- `local_ethereal` は災禍が呪詛（`Hexed`）の札だけに付く bool。札自身のエセリアル
  （`CardModel.GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Ethereal)`。
  正準のキーワードと `CardCmd.ApplyKeyword` で付いたもの。オルゴール・幽霊の種など）。
  `HexPower.TryModifyKeywordsInCombat` は所有者の Hexed の札に Ethereal を Global として足すので、
  札のキーワード・本文にはこの寄与が混ざり、札自身の分と区別できない。呪術が外れると
  （`HexPower.AfterRemoved` が Hexed を消す）Global の分は無くなり、Local の分は残る。
  false も出す。災禍が無い・別の災禍の札にはキーが無い（呪詛の札で読めたときだけ bool）。
  災禍の読取に失敗した札にもキーは無い（`affliction` が null と理由になるので、呪詛かどうかが未確認）。
  `local_ethereal` 自体の読取失敗は null と `unavailable_reasons.local_ethereal`・`warnings`。
  `Local` だけを指定するので `Hook.ModifyKeywordsInCombat` は呼ばれず、キーワードの付け外しもしない
  （戦闘中の札は変更可能なモデルで、Local の集合は `MutableClone` → `CardModel.DeepCloneFields` で作成済みなので、この読取は何も作らない）。
  例は `{ "affliction": { "id": "HEXED", "amount": 2 }, "local_ethereal": false }`。
  Markdown は災禍の行に `affliction HEXED (amount 2, local ethereal no)`。
- `enchantment` は `CardModel.Enchantment` の `{ id, amount, status }`。
  `status` は `EnchantmentModel.Status` の enum 名（`Normal` / `Disabled`）。
  例は `{ "id": "VIGOROUS", "amount": 3, "status": "Disabled" }`。
  モメンタム（`Momentum`）だけ `extra_damage`（整数）を足す。`Momentum.EnchantDamageAdditive` が
  PoweredAttack に返す private の `_extraDamage` で、`Momentum.OnPlay` がプレイのたびに `Amount` を足す。
  現在値は、次のプレイ（再演の 1 回目）で PoweredAttack の各ダメージに加わる値。`CardModel.OnPlayWrapper` は再演の各回で札の `OnPlay` の後に `Momentum.OnPlay` を呼ぶので、増えるのは各回のダメージの後（1 回目のプレイには、そのプレイの増分は乗らない）。
  手札の `vs_targets` などのプレビュー値（`DamageVar.UpdateCardPreview` / `Hook.ModifyDamage`）には、この加算が既に含まれる。足し直さない。
  宣言されたフィールドを読むだけで、getter・setter（`AssertMutable`）・フックは呼ばない。
  未プレイの 0 も出す。例は `{ "id": "MOMENTUM", "amount": 5, "status": "Normal", "extra_damage": 10 }`。
  読めない（フィールドの欠落・型違い）ときは null とし、`enchantment.extra_damage` の理由と `warnings` を付け、
  `id` / `amount` / `status` は保持する。他のエンチャントと災禍にはこのキーが無い。
- `dynamic_vars` は `CardModel.DynamicVars` の変数名から現在の `BaseValue` への辞書。
  小数・0・負数をそのまま JSON 数値で出す。`StringVar` は文字列用のため除外する。
  数値変数が0件なら `{}`。成長・強化を反映した現在値を毎回読む。
  EnchantedValue / PreviewValue ではない。CalculatedVar は計算前の基底値。
- `unavailable_reasons` は読取失敗のパスから理由への辞書。正常時は `{}`。

災禍・エンチャントの getter が正常に null を返したときは、その項目を null とし、
理由の辞書は空。読取例外が出たときは該当値を null にし、理由と `warnings` を付ける。
例として `affliction.amount`、`enchantment.status`、`enchantment.extra_damage`、`dynamic_vars.Damage` の失敗では、
同じオブジェクト内の読めた項目を保持する。変数一覧の取得・列挙の失敗は
`dynamic_vars` 全体を null にする。手札の `CanPlay` の例外も局所化し、
`can_play` / `unplayable_reason` を null と理由にする（パイルの札も同じ）。
項目の欠落がある旧 snapshot は未観測として扱う。

パイルの札の `can_play` は手札と同じ名前・同じ関数の値だが、「いまその札を出せるか」ではない。
`CardModel.CanPlay` は札の場所を見ない（`UnplayableReason` に「手札に無い」に当たる値は無い）。
手で出すには手札にあることが別に要る（MOD の `play_card` は手札の札だけを受け付ける）。
パイルの値は、その札の**いまの場所のまま**、いまの戦闘状態で `CanPlay` を評価した結果で、
次の点で「引いたときに出せるか」とも一致しない:

- エネルギー・星は**いま**の値で比べる（`PlayerCombatState.HasEnoughResourcesFor`）。
  引いた時点のエネルギーとは違い得るので、`EnergyCostTooHigh` / `StarCostTooHigh` は将来の予測に使えない。
  コストも札の**いまの場所**で評価する。手札・Play pile にある札だけに効くコストの修正（`FreeAttackPower` / `FreeSkillPower` / `FreePowerPower` / `FreeCardPower` / `VoidFormPower` / `EnsemblePower` / `VeilpiercerPower` の `TryModifyEnergyCostInCombat(Late)`、`BrilliantScarf`）は、山札・捨て札・廃棄札の札には効かない。パイルの行の `energy_cost.with_all_modifiers` も同じ。
- 札自身のロジック（`IsPlayable`）は札の場所を前提にしない。`GrandFinale.IsPlayable` は
  山札が0枚のときだけ真なので、山札にある間は自分が数に入り、必ず `BlockedByCardLogic` になる。
  `Clash.IsPlayable` は手札の全札を見るので、パイルにある間は自分を含まない手札で判定される。
- フック（`Hook.ShouldPlay`）は戦闘全体のリスナーを回す。`Normality` / `Enthralled` は
  **止める側の札自身が手札にあるとき**だけ他の札を止め、評価される札の場所は見ない。
  `ChainsOfBindingPower` / `RingingPower` / `SmoggyPower` は災禍の付いた札を、それぞれの条件のもとで場所に関係なく止める。
  `SlothPower` / `EncorePower` / `VelvetChoker` の枚数の制限も、場所に関係なく `BlockedByHook` にする。
- Play pile の札は解決中の snapshot なので、支払い済みのエネルギー・使用回数を反映した値になり得る。

ゲーム側の札の場所に依存しない理由（`HasUnplayableKeyword`、災禍による `BlockedByHook` など）は、
パイルでもそのまま読める。戦闘外は `CanPlay` が理由なしで false を返すが、パイルの行は戦闘中だけ出る。

山札の表示順は `Rarity` → `CardModel.CompareTo` → `uid`（ordinal 文字列比較）。
一次・二次比較にゲームの `NCardPileScreen.OnPileContentsChanged` の
`OrderBy(Rarity).ThenBy(card => card)` を使う。同順位は uid 順にして引く順を意図して隠す。
ゲームの安定ソートが同順位に残す格納順を、MOD はこの区間で正規化する。`CardModel.CompareTo` は
ID → 強化段階 → エンチャント ID → 災禍 ID → 解決後のエネルギーコスト →
BaseReplayCount → キーの ordinal 順で各 DynamicVar.BaseValue を比較する。
比較中の例外は `draw_pile.order` の warning とし、`Rarity` → `Id.Entry`（ordinal）→
`uid`（ordinal）の順に並べて返す。通常・例外時とも同順位の引く順を隠す。
捨て札・廃棄札は、ゲームと同じ格納順を保つ（`NCardGrid.SetCards` の `Ascending` は
入力順を保つ）。`play_pile` も格納順を保つ。
Markdown はパイルの札の本行末尾に `[Attack/AnyEnemy]` の形で型・対象を付ける。
存在する災禍・エンチャントと読取失敗の理由は短い子行で表示する。
モメンタムは `enchantment MOMENTUM (amount 5, extra damage 10)` の形で同じ子行に出す。
エンチャントは `status` が `Disabled` のときだけ `(disabled)` を付ける。
基底値は `base Damage 6, Vulnerable 2` の形で札ごとに1行にまとめる。
手札の型・対象は既存の行に表示し、プレイ可否が読めなければ `?` を付ける。
パイルの札は `can_play` が false のときだけ `  - can_play false (HasUnplayableKeyword)` の子行を付ける。
タグがあれば手札・パイルとも `  - tags Strike` の子行を付ける。強化済みは札名の `+` で分かる。
`energy_cost.unavailable_reasons` はコスト専用、行の `unavailable_reasons` は本節の項目用。

本節の項目は schema 4 で追加した（追加時に版は上げていない）。
同じ行の `upgrade_preview` の意味を変えたので schema 5 に上げた（次々節）。

出典（v0.113.0、`sts2-d009478a` デコンパイル）は `CardModel.Type` / `TargetType` /
`Affliction` / `Enchantment` / `DynamicVars` / `CanPlay`、`AfflictionModel.Amount`、
`EnchantmentModel.Amount` / `Status`、`EnchantmentStatus`、`DynamicVar.BaseValue`、`DynamicVarSet.GetEnumerator`、
`StringVar`。契約試験は詳細の取得・失敗・表示を検査する。
`extra_damage` の出典（v0.113.1、`sts2-a671e9ef` デコンパイル）は `Momentum._extraDamage` /
`ExtraDamage` / `OnPlay` / `EnchantDamageAdditive`。
パイルの `can_play`・`is_upgraded` / `is_upgradable`・`tags` の出典（v0.113.1、`sts2-a671e9ef` デコンパイル）は
`CardModel.CanPlay(out UnplayableReason, out AbstractModel)` / `IsPlayable` / `IsUpgraded` / `IsUpgradable` / `Tags`、
`UnplayableReason`、`CardTag`、`PlayerCombatState.HasEnoughResourcesFor`、`Hook.ShouldPlay`、
`Clash.IsPlayable`、`GrandFinale.IsPlayable`、`Normality.ShouldPlay`、`Enthralled.ShouldPlay`、
`ChainsOfBindingPower.ShouldPlay`、`RingingPower.ShouldPlay`、`SmoggyPower.ShouldPlay`。
[CardDetailsContract](../tests/CardDetailsContract/README.md)

### 札の効果の入力（schema 7）

戦闘中の札の行（`player.hand[]` と4パイル、戦闘中の choose の候補行、`upgrade_preview` の `ready`、
選択候補の `combat_instance.values`）は、[戦闘カードの詳細](#戦闘カードの詳細追加schema-4)と同じ読取で
次の項目も出す（`cat12079801/sts2#1019`）。本文・キーワードの表示からは分からない、その個体のプレイの入力。
どのキーも**全行に必ずあり**、読めた false・0 は実値、読取失敗は null と行の `unavailable_reasons.<key>`・`warnings`
（`ready` では失敗が1つでもあれば `unavailable`）。

| キー | 型 | 出典（v0.113.1、`sts2-a671e9ef`） |
|---|---|---|
| `retain_this_turn` | bool | `CardModel.ShouldRetainThisTurn`（Retain キーワード〔実効〕、または単ターン保留） |
| `single_turn_retain` | bool | private `CardModel._hasSingleTurnRetain`（`GiveSingleTurnRetain` が立てる） |
| `sly_this_turn` | bool | `CardModel.IsSlyThisTurn`（Sly キーワード〔実効〕、または単ターンの Sly） |
| `single_turn_sly` | bool | private `CardModel._hasSingleTurnSly`（`GiveSingleTurnSly`。`CardCmd.ApplySingleTurnSly`〔`HandTrick`〕が呼ぶ） |
| `base_replay_count` | int | `CardModel.BaseReplayCount`（`SoldiersStew`・`Transfigure`・`HiddenGem`・`SwordSagePower` が足す。ターン終わりに戻らない） |
| `extra_replay_this_turn` | int | `CardModel.ExtraReplayThisTurn`（今ターンだけの追加。v0.113.1 では書き手が本体に無く、常に 0 の実値） |
| `enchanted_replay_count` | int | `CardModel.GetEnchantedReplayCount()` = `Enchantment?.EnchantPlayCount(BaseReplayCount + ExtraReplayThisTurn)`、付与が無ければその和 |
| `tinker_time_rider` | enum 名 | `MadScience.TinkerTimeRider`（`TinkerTime.RiderEffect`: `None` / `Sapping` / `Violence` / `Choking` / `Energized` / `Wisdom` / `Chaos` / `Expertise` / `Curious` / `Improvement`）。Mad Science（`MAD_SCIENCE`）の行だけ |

- 単ターンの2つは private フィールドをそのまま読む（getter の `AssertMutable`・setter・フックを呼ばない）。
  `EndOfTurnCleanup` が両方を false に戻し、`ExtraReplayThisTurn` を 0 にする。`BaseReplayCount` は戻らない。
  キーワードの Sly / Retain による実効の true と、単ターンだけの true は `single_turn_*` で区別する。
- `enchanted_replay_count` は、`CardModel.OnPlayWrapper` の再演の回数（`GetEnchantedReplayCount() + 1` 回のうち追加分）のうち、
  札自身と付与の分。他のモデルの `ModifyCardPlayCount`（パワー・レリック）は含まない。
  `Glam.EnchantPlayCount`は `Status` が `Disabled` でなければ `Times` を足す。`Glam.AfterCardPlayed` が
  その札の最初のプレイの後に `Disabled` にするので、同じ戦闘の2回目以降は足さない。`Spiral.EnchantPlayCount` は常に `Times` を足す。
  付与の数値変数を読む前に private `EnchantmentModel._dynamicVars` が作成済みか確かめ、未作成なら getter で作らずに失敗とする
  （戦闘中の付与は `EnchantmentModel.DeepCloneFields` で必ず作成済み）。
- `enchantment` の行に `dynamic_vars` を足す: 付与自身の数値変数の名前から現在の `BaseValue` への辞書
  （`StringVar` を除く。変数の無い付与は `{}`）。`GLAM` / `SPIRAL` の `Times` は、上の回数に足される値で、
  公開の `status`（`Normal` / `Disabled`）と合わせて読む（`GLAM` の `Disabled` は `Times` が残っていても足されない）。
  private `_dynamicVars` を読み、未作成なら null と `enchantment.dynamic_vars` の理由。1変数の失敗は
  `enchantment.dynamic_vars.<名前>`。例は `{ "id": "GLAM", "amount": 1, "status": "Normal", "dynamic_vars": { "Times": 1 } }`。
- `tinker_time_rider` は、Mad Science の `dynamic_vars` が全ライダーの変数（`ViolenceHits`・`SappingWeak` など）を持つので、
  どれが効くかを決めるために出す（`MadScience.OnPlay` / `ExecuteAttack` / `ExecutePower` がこの値で分岐する）。
  型は既存の `type`（`TinkerTimeType`）。他の札にはキーが無い。
- 複製（`ready`）は `MemberwiseClone` で単ターンのフラグ・リプレイ回数・ライダーを写し、`DeepCloneFields` で付与の変数も複製する。
  強化が変えるのは `OnUpgrade` が変える分だけ。
- Markdown は、単ターンの付与があれば `  - this turn only: retain, sly`、リプレイのどれかが 0 でなければ
  `  - replays: base 1, extra this turn 0, with enchantment 2`、Mad Science は `  - tinker time rider Violence`、
  付与の行に `Times 1` を付ける。読取失敗は理由付きの子行。

出典: `CardModel.ShouldRetainThisTurn` / `HasSingleTurnRetain` / `_hasSingleTurnRetain` / `IsSlyThisTurn` / `HasSingleTurnSly` /
`_hasSingleTurnSly` / `GiveSingleTurnRetain` / `GiveSingleTurnSly` / `BaseReplayCount` / `ExtraReplayThisTurn` /
`GetEnchantedReplayCount` / `EndOfTurnCleanup` / `OnPlayWrapper` / `DeepCloneFields`、`CardCmd.ApplySingleTurnSly`、
`EnchantmentModel.DynamicVars` / `_dynamicVars` / `DeepCloneFields` / `EnchantPlayCount`、`Glam.EnchantPlayCount` / `AfterCardPlayed`、
`Spiral.EnchantPlayCount`、`MadScience.TinkerTimeRider` / `OnPlay` / `CanonicalVars`、`TinkerTime.RiderEffect`、
`SoldiersStew.OnUse`、`Transfigure`、`HiddenGem`、`SwordSagePower`。
[CardDetailsContract](../tests/CardDetailsContract/README.md)・[CombatUpgradePreviewContract](../tests/CombatUpgradePreviewContract/README.md)

### 戦闘の強化結果（`upgrade_preview`、schema 5）

戦闘中の `player.hand[]` / `draw_pile[]` / `discard_pile[]` / `exhaust_pile[]` / `play_pile[]` の
**すべての行**に、その札の強化の状態と、その札そのものを1段強化した結果を出す（sts2#1003）。
強化の手段（神格化・武装・鍛冶の祝福・剃刀の歯・闘争心など）が手札にあるかどうかに関わらず出す。
単体・マルチのローカル player は共通の `BuildPlayerState` を通る。

行に足す項目（行の `unavailable_reasons` に読取失敗の理由が入る）:

- `upgrade_level` / `max_upgrade_level`: `CardModel.CurrentUpgradeLevel` / `MaxUpgradeLevel`。
  手札の行は以前から持っていた。上限は通常1、傷・めまい・アセンダーの災厄は0、汚れた短剣は 999999999。
- `local_keyword_ids`: `CardModel.GetKeywordsWithSources(KeywordSources.Local)`。札そのものの
  キーワード（印刷＋ `AddKeyword` − `RemoveKeyword`）。`CardKeyword` の enum 名を enum の数値順に並べる。
- `keyword_ids`: `CardModel.Keywords`（`KeywordSources.All`）。戦闘中の `Hook.ModifyKeywordsInCombat`
  （呪術の付いた札の Ethereal、ファントムブレードの Shiv の Retain など）を含む実効のキーワード。
  hover tips（`keywords`）とは別物で、関連札の説明に出る「保留」「廃棄」を対象札に付けない。
- `retain_this_turn` / `single_turn_retain` と Sly・リプレイの項目は、詳細と同じ読取で全行に出る
  （[札の効果の入力](#札の効果の入力schema-7)）。

`upgrade_preview` はどの行にも必ずあり、`status` が次の3値のどれかになる。
`from_level` は強化の始点（親の段階）、`unavailable_reasons` はプレビュー自身の失敗理由（正常時 `{}`）。

| `status` | 条件 | 中身 |
|---|---|---|
| `ready` | 親の `IsUpgradable` が真で、複製の段階が親＋1、上限・ID が親と一致 | 強化後の絶対値（下記） |
| `not_upgradable` | 親の `IsUpgradable` が偽で、段階が上限（上限0を含む）。複製しない | `status` / `from_level` / `unavailable_reasons` だけ |
| `unavailable` | 複製の失敗、必須の getter の例外、不整合（段階・上限・ID）、親の段階・可否が読めない | `status` / `from_level`（読めなければ null）/ `unavailable_reasons`（失敗した項目のパス）。部分的な値は出さない |

`unavailable` は「強化できない」ではなく「観測できなかった」。他の札と state の残りはそのまま返す。
多段の札（汚れた短剣）は**次の1段だけ**を出す。その先の段は、実際に強化したあとの state を読み直して得る。

`ready` の項目（すべて強化後の複製の値）:

```jsonc
"upgrade_preview": {
  "status": "ready",
  "from_level": 0,
  "id": "STRIKE_IRONCLAD",
  "name": "Strike+",            // 表示名（段階の付いた名前）
  "description": "Deal 9 damage.", // 親のパイルでの通常表示の本文（「強化前→後」の比較表示ではない）
  "type": "Attack", "target_type": "AnyEnemy", "tags": ["Strike"],
  "upgrade_level": 1, "max_upgrade_level": 1,
  "is_upgraded": true, "is_upgradable": false,   // 次の強化ができるか（強化後の getter）
  "dynamic_vars": { "Damage": 9 },              // 全数値変数の BaseValue（StringVar を除く）
  "local_keyword_ids": [], "keyword_ids": [],
  "affliction": null, "enchantment": null,      // 戦闘カードの詳細と同じ形
  "retain_this_turn": false, "single_turn_retain": false, // 札の効果の入力（schema 7）
  "sly_this_turn": false, "single_turn_sly": false,
  "base_replay_count": 0, "extra_replay_this_turn": 0, "enchanted_replay_count": 0,
  "energy_cost": { "base": 1, "with_local_modifiers": 1, "with_all_modifiers": null,
                   "unavailable_reasons": { "with_all_modifiers": "detached_preview_context", ... }, ... },
  "star_cost_state": { "base": -1, "current": -1, "temporary_costs": [], ... },
  "cost": "1", "star_cost": null,               // 表示用（下記）
  "unavailable_reasons": {}
}
```

- 機械が読むのは `dynamic_vars`・`type`・`target_type`・`tags`・`local_keyword_ids`・`keyword_ids`・
  `energy_cost`・`star_cost_state`・`affliction`・`enchantment`・段階と可否と、schema 7 の単ターンの期限・リプレイ回数
  （Mad Science は `tinker_time_rider`）。どれも複製の現在値で、差分ではない。
  `dynamic_vars` は空の `{}` も正常（数値の無い強化、たとえば幻影の Ethereal を外す強化）。
- `energy_cost` は [Energy Cost Object](#energy-cost-object追加schema-4) と同じ形・同じ読取。
  `CardEnergyCost.UpgradeBy` による基礎の低下と、Absolute の修正の切り詰めが入った値になる。
  ただし複製はパイルに居ないので、パイルで決まる大域の修正（手札・Play pile だけに効く `FreeAttackPower` など）は
  分からない。`with_all_modifiers` は常に null で、理由は `detached_preview_context`。
  実際に払う額は、強化後に読み直した親の行で確かめる。X コストの `base` の欠落（`x_cost`）、
  エネルギーコストの無い札（`no_energy_cost`）、X 以外の `captured_x_value`（`not_x_cost`）は正常。
  `costs_x` / `base_raw` / `canonical` / `local_modifiers`（と、通常の札の `with_local_modifiers`、
  X の札の `captured_x_value`）が読めなければ `unavailable` になる。
- `star_cost_state` は [Star Cost State Object](#star-cost-state-object追加schema-4) と同じ。
  `CardModel.UpgradeStarCostBy` が基礎の低下で外す一時コストも反映される。読取失敗は `unavailable`。
- `name` / `description` / `cost` / `star_cost` は表示用。`cost` / `star_cost` はゲーム自身の強化プレビューの
  カード（`NUpgradePreview`）と同じく、パイルの無い複製で計算した表示値で、読めなくても `ready` のまま
  （理由は `unavailable_reasons.cost` などに残る）。`description` は数値変数の基底値で書いた本文で、
  所有者の筋力などの補正は入らない。読めなければ `unavailable`（代替の本文で埋めない）。
- `cost` は戦闘の文脈（`UpgradePreviewType=Combat`）で計算するので、パイルを見ない大域のコストの修正
  （`CorruptionPower`・`SpikedGauntlets`・`TangledPower`・`CuriousPower`・`VoidFormPower`・`ChoLimbs`・`Coda` など）が入る。
  パイルを見る修正（`FreeAttackPower`・`BrilliantScarf` など）は入らない。schema 4 までの手札のプレビューの
  `cost` は大域の修正が入らない値だったので、同じ札でも値が違い得る。
- 戦闘の `ready` には表示用の `keywords`（hover tips）を出さない（schema 4 までの手札のプレビューにはあった）。
  強化後のキーワードは `local_keyword_ids` / `keyword_ids` で読む。
- 戦闘の `ready` の `description`（と `name`）も、パイルの無い複製の素の本文で、筋力・脱力・札自身のエンチャント（例: テスカタラの残り火）の加算を含まない。強化後に読み直した手札の行の本文とは違い得る。機械が読む値は `dynamic_vars` を使う（2026-11-18 の実機確認で 85 組中 11 組がこの差）。
- 戦闘外の画面（報酬・鍛冶の `card_select`・`hand_select` の `upgrade_select`・図鑑）の `upgrade_preview` は
  従来どおり `{ name, cost, star_cost, description, keywords }` で、`status` を持たない。
  戦闘中の `hand_select` の候補行も同じ従来の形のまま（同じ札の機械向けの値は `player.hand[]` にある。
  候補とその行は `combat_instance` で結ぶ。[選択候補の個体](#選択候補の個体combat_instanceschema-7)）。

**複製と隔離。** 強化はゲームの `CardCmd.Upgrade` と同じく `CardModel.UpgradeInternal` →
`FinalizeUpgradeInternal` を複製に1回ずつ適用する。現物には何もしない（`CardCmd.Upgrade`・
`ICardScope.CloneCard`〔`AddCard` をする〕は使わない）。複製は `AbstractModel.MutableClone` で作るが、
`CardModel.DeepCloneFields` は付与を貼り直す（`EnchantInternal` / `AfflictInternal`）ときに、
まだ現物から浅くコピーされたままの購読（`EnchantmentChanged` / `AfflictionChanged`。
`CombatStateTracker.Subscribe` や `NCard` が付ける）を呼ぶ。`CardModel.AfterCloned` が購読を外すのはその後。
そこで、登録されていない浅いコピーを作り、そのコピーの event の欄だけを空にしてから、
コピーに対してゲームの `MutableClone` を呼ぶ。現物と、現物と共有する集合（キーワード・数値変数・コストと修正・
一時スターコスト・付与）には書き込まない。`DeepCloneFields` が強化前にそれらを複製に置き換える。
event に同名の delegate の欄が無い（独自の add/remove）型では隔離できないので、`unavailable`（理由 `clone`）にする。
複製には UID を振らず、パイル・戦闘履歴・乱数・パイルの順序に触れない。
読取の文脈は、複製の `UpgradePreviewType` を `Combat` にし（`NUpgradePreview.Reload` と同じ）、
本文は親のパイルの種類で `GetDescriptionForPile` を呼ぶ。戦闘外の `upgrade_preview` も、
現物の札から作るときは同じ隔離した複製を使う（出力の形は変えない）。

Markdown は `ready` の行を出さない（値は JSON にある。1 枚ごとに 1 行増えるのを避ける）。
`unavailable` は手札・パイルとも `→ upgrade preview unavailable (理由)` を出す。`not_upgradable` は何も出さない。
行の段階・キーワードの読取失敗は、パイルの子行（`  - upgrade_level null (理由)` など）に出す。

**計測。** `GET /api/v1/singleplayer?perf=1`（マルチは `/api/v1/multiplayer?perf=1`）で、state に
`state_build_perf: { build_ms, upgrade_preview: { rows, clones, ready, not_upgradable, unavailable, elapsed_ms } }`
を足す。`build_ms` は state 全体の構築時間、`elapsed_ms` はその内のプレビュー（複製と読取）の時間、
`clones` は作った複製の数（`ready` と、複製後に失敗した `unavailable`）。既定では出さない。
raw JSON のバイト数は、`perf` を付けない通常の読取の応答の大きさで測る。

出典（v0.113.1、`sts2-a671e9ef` デコンパイル）: `CardCmd.Upgrade(IEnumerable<CardModel>, CardPreviewStyle)`、
`CardModel.UpgradeInternal` / `FinalizeUpgradeInternal` / `DeepCloneFields` / `AfterCloned` / `EnchantInternal` /
`AfflictInternal` / `CurrentUpgradeLevel` / `MaxUpgradeLevel` / `IsUpgradable` / `GetKeywordsWithSources` /
`Keywords` / `ShouldRetainThisTurn` / `UpgradePreviewType` / `CombatState` / `GetDescriptionForPile` /
`UpgradeStarCostBy`、`AbstractModel.MutableClone`、`EnchantmentModel.DeepCloneFields` / `ApplyInternal`、
`AfflictionModel.AfterCloned` / `Amount`、`CardEnergyCost.UpgradeBy` / `Clone` / `GetWithModifiers`、
`DynamicVar.Clone` / `UpgradeValueBy`、`CombatStateTracker.Subscribe`、`NCard.SubscribeToModel`、
`NUpgradePreview.Reload`、`HexPower.TryModifyKeywordsInCombat`、`PhantomBladesPower.TryModifyKeywordsInCombat`、
`FreeAttackPower.TryModifyEnergyCostInCombatLate`、`DingyDagger.MaxUpgradeLevel` / `OnUpgrade`、
`Apparition.OnUpgrade`、`Wound` / `Dazed` / `AscendersBane.MaxUpgradeLevel`。
全札の `OnUpgrade` は数値変数・エネルギーコスト・キーワード（と `ShowOff.UpdateAim` の自分の欄）だけを変える。
[CombatUpgradePreviewContract](../tests/CombatUpgradePreviewContract/README.md)

### 戦闘カードの uid とパイル順序

`uid` は `<card_id>#<suffix>` 形式の戦闘内の個体識別子。
デッキ由来の札は `CardModel.DeckVersion` で元の札を照合し、`Player.Deck.Cards` の
0 始まりの位置を suffix にする（例 `STRIKE_IRONCLAD#2`）。位置はカード ID ごとでなく
デッキ全体で数える。マルチは Player の NetId を添えた `<位置>p<NetId>`
（例 `STRIKE_IRONCLAD#2p17`）を使う。
戦闘の最初の registry 更新時に全 Player のデッキから対応を作る。
デッキ位置は、ゲームのデッキ画面の初期表示・入手順の昇順で見える情報である。

生成札・複製札（DeckVersion が無い札）の suffix は `g<乱数>`
（例 `STRIKE_IRONCLAD#g900`）。乱数は OS の暗号学的乱数から選ぶ
0 以上 `Int32.MaxValue` 未満の10進整数で、衝突時に引き直す。
発行済みの uid は消えた札も含めて戦闘終了まで予約する。

各 CardModel インスタンスの uid は初回参照時に一度だけ付け、手札・山札・捨て札・
廃棄札・プレイ中・grid・戦闘履歴・`play_card.card_uid` で共有する。
強化・コスト変更・所有者変更・パイル移動や同じ戦闘内の state 再取得では変わらない。
戦闘中に生成された札、手札に直接入る札、複製された札も `#g<乱数>` で個別に識別する。
registry は combat state のインスタンスが変わったときに初期化する。
保存して終了 → continue で新しい combat state とカードを作り直しても、
デッキ由来の札は同じデッキ位置から同じ uid を再現する。
旧 MOD は初回観測順の連番で採番していたため、戦闘開始時に生成される札の uid も
再ロード後に同じ番号を再現していた。この版では、その札の乱数 uid は再ロードで変わる。
開始時の生成札が手札にある場合のやり直し検出は後退している。
engine 側は sts2 #873 で、uid に依らないやり直し検出に修正する。

engine の `tools/turnplan/cli.py` の `turn_sig` は手札 uid の集合を署名に含める。
round 1 の途中から保存して終了 → continue したとき、初手の署名への復帰を
外からのやり直しとして検出するため、デッキ由来の uid の再現が必要である。
デッキ由来の札だけの初手では、その前提をデッキ位置による採番で満たす。

利用側は uid を不透明な文字列として照合する。旧 fixture・dump の具体的な番号は
新しい MOD と異なる場合があるため、必要な観測は新しい MOD で取り直す。
この採番は schema 4 で入れた（追加時に版は上げていない）。

出典は v0.113.0（`sts2-d009478a`）の `NCardPileScreen.OnPileContentsChanged`、
`CardModel.CompareTo`、`NCardGrid.SetCards`。戦闘開始時は
`CombatManager.SetUpCombat` → `Player.PopulateCombatState` がデッキ順に複製した後で
`DrawPile.RandomizeOrderInternal` を呼ぶ。
`NDeckViewScreen` は `PileType.Deck.GetPile(_player)` の `Cards` を表示し、
初期状態と `OnObtainedSort` の昇順で `SortingOrders.Ascending` を使う。
`Player.ToSerializable` / `SyncWithSerializedPlayer` はデッキの順序を保存・復元する。
契約試験は `tests/CardIdentityContract`、共有 registry と履歴の照合は
`tests/CombatHistoryContract` で確認する。

### Energy Cost Object（追加、schema 4）

戦闘中の `player.hand[]` / `draw_pile[]` / `discard_pile[]` / `exhaust_pile[]` /
`play_pile[]` と、戦闘中の `card_select.cards[]` の grid 行に `energy_cost` を出す。
grid は `uid` と同じ条件（`CombatManager.IsInProgress` と取得可能な combat state）。
単独・マルチのローカル player は共通の `BuildPlayerState` を通る。
戦闘中の choose-a-card の候補にも出す（[戦闘中の choose-a-card](#戦闘中の-choose-a-card追加schema-4)）。
戦闘外のデッキ・選択、戦闘外の choose-a-card、`hand_select.cards[]`、各 preview には出さない。
手札選択中も同じ札は `player.hand[]` で読む。

```jsonc
{
  "costs_x": false,
  "base": 2,
  "base_raw": 2,
  "canonical": 2,
  "captured_x_value": null,
  "with_local_modifiers": 1,
  "with_all_modifiers": 1,
  "local_modifiers": [
    { "amount": -1, "type": "Relative", "expiration": "WhenPlayed", "reduce_only": false }
  ],
  "unavailable_reasons": { "captured_x_value": "not_x_cost" }
}
```

- `base_raw` は `CardEnergyCost.GetWithModifiers(CostModifiers.None)` の整数をそのまま返す。
  X・負の sentinel・0 も保持し、強化や `SetCustomBaseCost` 後の基礎値を読む。
  `costs_x` の読取に失敗しても独立して取得する。
- `canonical` は `CardEnergyCost.Canonical` の整数。ゲームが保持する印刷時の開始コスト。
  強化・カスタム基礎値変更の前の値を保持し、X の Canonical はゲームの定義で 0。
- `captured_x_value` は `costs_x=true` のときだけ `CardEnergyCost.CapturedXValue` を読む。
  `SpendEnergy` での支払額、`AutoPlay` 時点のエナジー（`skipXCapture=true` では書き込まない）、
  `Clone` での元の値の引き継ぎを保持し、0 に戻す処理はない。
  `AutoPlay` はエナジーを支払わずにその時点の値を保存し、初期値の 0 も保持する。
  X の効果量への補正を含めるときはゲームが別途 `ResolveEnergyXValue` を使う。
  非 X では `null` と理由 `not_x_cost`、X 種別が読めない場合は `null` と
  `costs_x_unavailable`。いずれも CapturedXValue getter を呼ばず、警告を追加しない。
- `x_value_if_played`（schema 1.10.0。`player.hand[]` の X の札だけ）: いま手札から撃ったときに効果が読む X。手動のプレイは
  `CardEnergyCost.GetAmountToSpend()`（X は現在のエナジー全部）を `CardModel.SpendEnergy` で `CapturedXValue` にし、効果は
  `CardModel.ResolveEnergyXValue()` = `Hook.ModifyXValue(CombatState, card, CapturedXValue)`（ケミカルX が `Increase` を足す）を読む。
  同じ純粋な計算を現在のエナジーで行う（何も記録・支払いしない）。手札に無い・戦闘中でないときは `null` と
  `unavailable_reasons.x_value_if_played`。非 X の札・手札以外の行には付けない。
- `base` は固定コスト用の現在の基礎コスト。通常は `base_raw` と同じ値を使う。
  X・負コストでは既存契約どおり理由付き `null`。
- `with_local_modifiers`: `GetWithModifiers(Local)`。local のみ適用した値。
- `with_all_modifiers`: `GetWithModifiers(All)`。global のフックも含む現在の値。
  この2値は MOD で再計算せず、ゲームの適用順・reduceOnly・最終 clamp に従う。
- `local_modifiers`: private の `CardEnergyCost._localModifiers` をリフレクションで読む。
  各要素は public の `Amount` / `Type` / `Expiration` / `IsReduceOnly`。
  `amount` は整数、`reduce_only` は bool、`type` / `expiration` はゲームの enum の
  `ToString()`。リストの適用順・重複を保持し、効果名やプレイ履歴から補完しない。
  空リスト `[]` は「修正なし」、`null` は「読取失敗」。X やコストなしの札でも
  実リストは読む（修正が保持されていても固定コスト計算に適用されるとは限らない）。
- `expiration` は `EndOfCombat`（enum 値 0）、`EndOfTurn`、`WhenPlayed`、または
  `"EndOfTurn, WhenPlayed"`。複合はどちらか先に到来した時点で消える。
  `EndOfTurnCleanup` は EndOfTurn の旗だけ、`AfterCardPlayedCleanup` は WhenPlayed の旗だけを除去する。
  `WhenPlayed` だけの修正はターンをまたいで残る。MOD は期限の推測・削除を行わない。
- `reduce_only: true` は、適用時に現在の値より上げない修正。
  現在は値を変えていない要素も、別の修正が失効すると効く場合があるのでリストに残す。
- `unavailable_reasons`: `null` のキー名 → 理由の文字列。正常な固定コストでは `captured_x_value: "not_x_cost"` を含む。
  X は `base` / `with_local_modifiers` / `with_all_modifiers` を `null` にして各理由を `x_cost`、`None` が負（エナジーコストなし）は
  `no_energy_cost` とする。カードの Curse/Status 種別や Unplayable keyword からは判断しない。
  `costs_x` 自体の読取失敗は `costs_x: null`、数値側は `costs_x_unavailable`。
  基礎値の読取失敗は `base_raw` と固定コストの `base` に例外の理由、修正後の数値は `base_unavailable`。
  基礎値の失敗は `warnings` の `card.energy_cost.base_raw` に記録する。
  その他の読取・計算失敗も該当値だけ `null` と例外の理由にし、`warnings` に
  `card.energy_cost[.<key>]` を出す。private リストが読めなくても public のコストは読む。
  `EnergyCost` 自体が取得できない場合もオブジェクトを残し、8項目を `null` と理由にする。
  通常の X / コストなしには警告を出さない。

`player.play_pile` / `play_pile_count` は戦闘中の一時的な Play pile。
行は他のパイルと同じ形（`uid`、`exhaust_on_next_play` も含む）。
プレイの合間には通常空で、解決中には過渡的な値が見えるため、計画は `is_resolving: false` の
snapshot を使う。山札は既存どおり表示用のソート順で、内部のドロー順を出さない。

Markdown は local modifier が非空、修正後のコストが基礎値と異なる、X、
`base_raw` と `canonical` が異なる、または通常の適用対象外以外の理由で `null` がある場合に、
手札・各パイル・grid 行の下に base / local / all、raw / canonical / captured X と
非空の修正一覧（値・種別・期限・reduce_only）を適用順で表示する。
通常の適用対象外の理由は `x_cost` / `no_energy_cost` / `not_x_cost`。
`null` には理由を添える。Play Pile の節は `play_pile_count` が1以上の場合だけ表示する。
既存の `cost`（文字列、X は `"X"`、それ以外は `GetAmountToSpend()`）と `star_cost` は変更しない。
スターコストの一時値は別の `star_cost_state`（下記）で出す。
既存フィールドの意味・削除・action の変更がないため版は上げていない（schema 4 のときの追加）。
利用側は `energy_cost` と各追加キーの有無で検出する。古い state の欠落は「修正なし」ではなく未観測。

出典（v0.113.0、ローカルの `sts2-d009478a` デコンパイル）:

- `MegaCrit.Sts2.Core.Entities.Cards/CardEnergyCost.cs`: `_localModifiers`（25行）、
  `Canonical` / `CapturedXValue`、`Clone`、`GetWithModifiers`（94–121行）、`SetThisTurnOrUntilPlayed`（197–203行）、
  `SetThisCombat`（238–244行）、`AddUntilPlayed`（258–264行）、
  `EndOfTurnCleanup` / `AfterCardPlayedCleanup`（333–347行）、`UpgradeBy` / `SetCustomBaseCost`。
- 同ディレクトリの `LocalCostModifier.cs` の各 getter（48–76行）と `Modify`（89行）、
  `LocalCostType.cs` / `LocalCostModifierExpiration.cs` / `CostModifiers.cs` の enum。
- `MegaCrit.Sts2.Core.Models.Cards/ExpectAFight.cs` の `OnPlay`（31–36行）は
  X 以外の手札アタックへ `AddUntilPlayed(-Energy.IntValue)`。
  `Enlightenment.cs` の `OnPlay`（16–24行）は通常 `SetThisTurnOrUntilPlayed(1, true)`、
  強化後 `SetThisCombat(1, true)`。
- `MegaCrit.Sts2.Core.Entities.Players/PlayerCombatState.cs` の `PlayPile` / `AllPiles`（71–85行）。
- `MegaCrit.Sts2.Core.Models/CardModel.cs` の `SpendEnergy`（1984–1995行）、
  `MegaCrit.Sts2.Core.Commands/CardCmd.cs` の `AutoPlay`（99–101行）。

契約試験は [`tests/CardEnergyCostContract`](../tests/CardEnergyCostContract/README.md)。
効果適用・ターン遷移の実機検証とは区別する。

### Star Cost State Object（追加、schema 4）

戦闘中の `player.hand[]` / `draw_pile[]` / `discard_pile[]` / `exhaust_pile[]` / `play_pile[]` と、
戦闘中の choose-a-card の候補に `star_cost_state` を出す（`energy_cost` と同じ行）。

```jsonc
"star_cost_state": {
  "costs_x": false,          // CardModel.HasStarCostX
  "canonical": -1,           // CardModel.CanonicalStarCost（-1 はスターコストなし）
  "base": -1,                // CardModel.BaseStarCost
  "current": -1,             // CardModel.CurrentStarCost（最後の一時値。負の基底に 0 の一時値は基底のまま）
  "temporary_costs": [       // private CardModel._temporaryStarCosts を順に（最後が有効）
    { "cost": 0, "clears_when_turn_ends": true, "clears_when_card_is_played": true }
  ],
  "unavailable_reasons": {}
}
```

- 値は本体の getter をそのまま読む。`BaseStarCost` の getter（`CurrentStarCost` からも呼ばれる）は、未設定なら
  `CanonicalStarCost`（各札の定数）を private の `_baseStarCost` に写して `_starCostSet` を立てる遅延初期化をする。
  写す値は定数（`CanonicalStarCost` の override はすべて定数）で、`_starCostSet` を読むのはこの getter と setter だけなので、
  外から見える状態は変わらない（冪等）。`IsMutable` が偽の札では初期化せず `CanonicalStarCost` を返す。
- `temporary_costs` は `TemporaryCardCost.Cost` / `ClearsWhenTurnEnds` / `ClearsWhenCardIsPlayed` を順序・重複ごと出す。
  `SetToFreeThisTurn` は `SetStarCostThisTurn(0)`（`TemporaryCardCost.ThisTurn(0)`）を末尾に足す。
  `[]` は一時値なし、`null` は読取失敗。効果名やプレイ履歴から補わない。
- 読取失敗はその項目だけ `null`、`unavailable_reasons` に理由、warning `card.star_cost_state.<key>`。
- Markdown には出さない（既存の `star_cost` 表示は変えない）。追加だけなので版は上げていない（schema 4 のときの追加）。

出典は `sts2-a671e9ef` の `CardModel.BaseStarCost` / `CanonicalStarCost` / `HasStarCostX` / `CurrentStarCost` /
`_temporaryStarCosts` / `SetToFreeThisTurn` / `SetStarCostThisTurn`、`TemporaryCardCost`。

### 次回プレイ時の廃棄フラグ（追加、schema 3）

`exhaust_on_next_play` は `CardModel.ExhaustOnNextPlay` の現在の bool 値。
戦闘中の `player.hand[]`、`draw_pile[]` / `discard_pile[]` / `exhaust_pile[]` / `play_pile[]`、
および `card_select.cards[]` の grid 行に出す。grid は `uid` と同じ条件、
すなわち `CombatManager.IsInProgress` が true で combat state が取得できる場合だけ。
戦闘中の choose-a-card 行にも同じ条件で出す。戦闘外のデッキ・選択画面・choose-a-card 行、`preview_cards` / `upgrade_preview` には出さない。
共通の `BuildCardInfo` には追加せず、`BuildCardState` / `BuildPileCardList` /
`BuildCardSelectState` / `BuildChooseCardRows` の戦闘用の行で読む。正常時は false も出し、
読み取り例外時は既存の `SafeGetBool` により `null` と `warnings` を返す（false と区別）。
Markdown は手札・パイル・grid の行で true の場合だけ `[exhaust-next]` を付ける。
false・null・項目なしでは印を付けない。schema 3 で追加した項目であり、その追加時には `schema_version` を変更していない。

出典はローカルの `sts2-9cb4f1ad` デコンパイル：

- `MegaCrit.Sts2.Core.Models/CardModel.cs:560-571` の getter は
  `_exhaustOnNextPlay` を返すだけで、副作用はない。
- `MegaCrit.Sts2.Core.Commands/CardPileCmd.cs:1145-1182` の
  `AutoPlayFromDrawPile` が AutoPlay 直前に `ExhaustOnNextPlay = forceExhaust` を設定する。
  この版で `forceExhaust: true` を渡す呼び出し元は
  `MegaCrit.Sts2.Core.Models.Cards/Havoc.cs:19-21` の `Havoc.OnPlay` だけ。
- `CardModel.EndOfTurnCleanup`（1613 行）は false に戻す。
  `GetResultLocationForCardPlay`（2090-2093 行）はこのフラグまたは Exhaust keyword を判定し、
  false に戻して Exhaust pile を返す。ただし、その前に IsDupe / Power の分岐がある。
  `MoveToResultPileWithoutPlaying`（2112-2114 行）も廃棄判定に使うが、
  この箇所には false の代入はない。`CreateClone`（2191 行）は複製側を false にする。

フラグ値そのものを返し、Exhaust keyword の有無や最終的な移動先から値を推定しない。

**true がプレイの合間に残るのは `exhaust_pile[]` だけ。** 荒廃の自動プレイが拒否された札
（`CardKeyword.Unplayable` の状態異常・呪い、`Hook.ShouldPlay` の拒否〈凡庸・怠惰・苦難など〉、
攻撃できる敵がいない。`CardCmd.AutoPlay` の `CardCmd.cs:58-96`）は、
`MoveToResultPileWithoutPlaying` → `CardCmd.Exhaust` で廃棄札に入り、旗は true のまま残る。
消えるのはプレイヤーの手番の終わりの `PlayerCombatState.EndOfTurnCleanup`
（`CombatManager.FlushPlayerHand`、`CombatManager.cs:1813`）。これは手番終了の AutoPostPlay の段
（`AfterAutoPostPlayPhaseEntered`、`CombatManager.cs:1543-1545`）より後なので、
その段で廃棄札の札が自分をプレイするとき（彼方からの遠吠え）に旗が効く。
普通にプレイされた札は、`OnPlay` より前の `GetResultLocationForCardPlay` で false に戻る。
実機（0.4.0+3fd74e0）: 凡庸を手札に持って荒廃を 3 枚目に撃つと、拒否された山札の一番上の防御が
`exhaust_on_next_play: true` で廃棄札に入り、Markdown に `[exhaust-next]` が出た。

`hand_select.cards[]` には出さない（`uid` と同じ。同じ札は `player.hand[]` で読む）。

### Orb Object

```jsonc
{
  "id": "LIGHTNING",
  "name": "Lightning",
  "description": "Passive: Deal 3 damage to a random enemy. Evoke: Deal 8 damage.",
  "passive_val": 3,
  "evoke_val": 8,
  "keywords": [ /* Keyword Objects */ ]
}
```

### Power Object (status effects)

Used for both player `status` and enemy `status`.
Pets also share the same builder.

```jsonc
{
  "id": "STRENGTH",
  "name": "Strength",
  "amount": 3,
  "type": "Buff",            // PowerModel.Type: "Buff", "Debuff" or "None"
  "type_for_current_amount": "Buff", // PowerModel.TypeForCurrentAmount (schema 9; negative Strength → "Debuff")
  "is_temporary_power": false,       // implements ITemporaryPower (schema 9)
  "dynamic_vars": {},                // the power's DynamicVars { name: BaseValue } (schema 12; {} = none)
  "description": "Increases attack damage by 3.",
  "keywords": [ /* Keyword Objects */ ]
}
```

画面に見えるパワーには次の内部状態を追加する（schema 4）。
可視性が不明、または `hidden_in_ui` のパワーには追加しない。
ただし `THIEVERY_POWER` / `HEIST_POWER` の項目、`VIGOR_POWER` の `vigor_attack`、`HEX_POWER` の付与元、`RITUAL_POWER` の印と、
schema 7 の `THE_BOMB_POWER` / `INFERNO_POWER` / `CRIMSON_MANTLE_POWER` / `PANACHE_POWER` の項目、schema 8 の `DAMPEN_POWER` の項目、
schema 9 の全パワーの `type_for_current_amount` / `is_temporary_power`、schema 12 の全パワーの `dynamic_vars` と下表の schema 12 の項目は、`status[]` に出る行なら可視性によらず付ける。
各項目の意味・人間が知りうる根拠・非公開の範囲は
[パワーの内部状態](power-internal-state.md) を参照。

| 対象 | 追加項目 | JSON の型 |
|---|---|---|
| 全パワー | `type_for_current_amount` | enum 名 `None` / `Buff` / `Debuff` / null（schema 9。可視性によらず付ける） |
| 全パワー | `is_temporary_power` | bool（schema 9。同上） |
| 全パワー | `dynamic_vars` | `{"SelfDamage": 2}`（変数名 → decimal。変数が無ければ `{}`、読めなければ null。schema 12。同上） |
| 全可視パワー | `skip_next_duration_tick` | bool |
| `TORIC_TOUGHNESS_POWER` | `block_base_value` | decimal |
| `SURROUNDED_POWER` | `facing` | enum 名 `Right` / `Left` |
| `POSSESS_STRENGTH_POWER` | `stolen_strength` | `[{"player_id":"17","amount":-2.75}]` |
| `POSSESS_SPEED_POWER` | `stolen_dexterity` | 同上 |
| `SKITTISH_POWER` | `has_gained_block_this_turn` | bool |
| `CHAINS_OF_BINDING_POWER` | `bound_card_played` | bool |
| `JUGGLING_POWER` | `attacks_played_this_turn` | int |
| `JUGGLING_POWER` | `card_to_clone_uid` | string / null |
| `JUGGLING_POWER` | `card_to_clone_readable` | bool |
| `CURL_UP_POWER` | `played_card_uid` | string / null |
| `CURL_UP_POWER` | `played_card_readable` | bool |
| `THIEVERY_POWER` | `stolen_gold` | int |
| `THIEVERY_POWER` | `target_player_id` | string（`player.player_id` と同じ） |
| `HEIST_POWER` | `target_player_id` | string（同上） |
| `VIGOR_POWER` | `vigor_attack` | `{"readable":true,"has_command":false,"attacker_is_owner":null,"amount_when_started":0}` |
| `HEX_POWER` | `applier_combat_id` | uint / null（`enemies[].combat_id` と同じ。`internal_state.applier_combat_id` の写し） |
| `HEX_POWER` | `applier_readable` | bool |
| `THE_BOMB_POWER` | `damage` | decimal（schema 7。可視性によらず付ける） |
| `INFERNO_POWER` / `CRIMSON_MANTLE_POWER` | `self_damage` | decimal（schema 7。同上） |
| `PANACHE_POWER` | `cards_left` | int（schema 7。同上） |
| `PANACHE_POWER` | `already_applied` | bool（schema 7。同上） |
| `DAMPEN_POWER` | `caster_combat_ids` | uint の配列（昇順）/ null（schema 8。可視性によらず付ける） |
| `DAMPEN_POWER` | `downgraded_cards` | `[{"uid","uid_known","card_id","original_upgrade_level"}]` / null。uid が無い行だけ `uid_unavailable_reason`（schema 8。同上） |
| `RUPTURE_POWER` | `played_cards` | `[{"uid","uid_known","card_id","pending_strength"}]` / null（schema 12。可視性によらず付ける。uid は弱化と同じ扱い） |
| `CALAMITY_POWER` / `STRANGLE_POWER` | `amounts_for_played_cards` | `[{"uid","uid_known","card_id","amount"}]` / null（schema 12。同上） |
| `HELLRAISER_POWER` | `infinite_auto_plays_this_turn` | int（schema 12。同上） |
| `HELLRAISER_POWER` | `auto_playing_cards` | `[{"uid","uid_known","card_id"}]` / null（schema 12。同上） |
| `AUTOMATION_POWER` | `cards_left` | int（schema 12。同上。引きの残り。`dynamic_vars.BaseCards` は本文の 10） |
| `DARK_EMBRACE_POWER` | `ethereal_count` | int（schema 12。同上） |
| `GIGANTIFICATION_POWER` | `has_command_to_modify` | bool（schema 12。同上） |
| `HARDENED_SHELL_POWER` | `damage_received_this_turn` | decimal（schema 12。同上） |
| `ADAPTABLE_POWER` / `REATTACH_POWER` / `ILLUSION_POWER` | `is_reviving` | bool（schema 12。同上） |
| `ILLUSION_POWER` | `follow_up_state_id` / `follow_up_state_readable` | string / null と bool（schema 12。同上。readable=true の null は記録なし。v0.113.1 では代入箇所が無く常に null で、復活の行き先は復活の行動の `move_state` から読む） |
| `NEMESIS_POWER` | `should_apply_intangible` | bool（schema 12。同上） |
| `SLOTH_POWER` / `TENDER_POWER` | `cards_played_this_turn` | int（schema 12。同上） |
| `BEACON_OF_HOPE_POWER` | `has_already_been_given_block` | bool（schema 12。同上） |
| `SWIPE_POWER` | `stolen_card` / `stolen_card_readable` | `{"uid","uid_known","card_id"}` / null と bool（schema 12。同上。readable=true の null は盗んでいない） |

拘束の鎖（`CHAINS_OF_BINDING_POWER`）の `bound_card_played` は private `ChainsOfBindingPower.Data.boundCardPlayed`。
`BeforeCardPlayed` は IsDupe を除き、所有者の Bound 札が使用されると true にする
（自動プレイも対象）。`ShouldPlay` が参照し、所有者が participants に含まれる
`BeforeSideTurnEnd` で false に戻る。`PowerModel._internalData` を読み、Data を初期化せず、
履歴・手札からフラグを再構成しない。読取不能は null と理由・warnings。
付与件数の `player.combat_history.bound_afflictions_this_turn` とは独立した値である。

ジャグリングの `card_to_clone_uid` は private `Data.cardToClone` の参照を既存の
手札・パイル・grid・履歴共通の uid registry に通した値。3 回目のアタックの
`BeforeCardPlayed` で予約し、対象の `AfterCardPlayed` で参照を消して複製する。
解決後の snapshot では通常 `null`。`null` でない値が見えるのはその札の解決中
（プレイ途中の選択を含む）だけ。
読取成功は `card_to_clone_readable=true`。待ちがなければ uid は `null` で理由なし。
読取失敗は `card_to_clone_readable=false`、uid は `null` とキー別理由・warnings。
Data を初期化せず、アタック数から予約を再構成しない。画面に出たプレイと複製の
出来事に対応する過渡状態で、解決後の snapshot を計画に使う。

まるくなる（`CURL_UP_POWER`）の `played_card_uid` は private `CurlUpPower.Data.playedCard` の参照を
同じ uid registry に通した値。`AfterDamageReceived` が所有者への `IsPoweredAttack` かつ
`cardSource` ありの被弾で、未記録か同じ札なら記録し（別の札が記録済みなら無視）、
`AfterCardPlayed` が `cardPlay.Card` と一致したときに参照を消して Amount のブロックを得、パワーを外す。
解決後の snapshot では通常 `null`。読取成功・失敗と Markdown の扱いは
`card_to_clone_uid` / `card_to_clone_readable` と同じ（`played_card_readable`）。
デッキに無い札も registry が `CARD_ID#g<番号>` を割り当てるので、札が分からない null は無い。

泥棒（`THIEVERY_POWER`）の `stolen_gold` は `ThieveryPower.DynamicVars.Gold.IntValue`。
`GoldVar(0)` から始まり、`ThieveryPower.Steal` が実際に盗んだ額（`min(Amount, 所持金)`）を足す。
`amount` は 1 回に盗む上限で、累計ではない。0 も実値として出す。
`target_player_id` は `PowerModel.Target.Player.NetId` の文字列。泥棒は対象 player ごとの
別インスタンス（`Instanced`）で、行ごとに対象を持つ。`SurprisePower.AfterDeath` が各インスタンスの
`Target` と `stolen_gold` を強奪（`HEIST_POWER`）の `Target` と `amount` へ写し、
強奪の `BeforeDeath` がその player へ `amount` のゴールドを返す。強奪には `target_player_id` だけを足す。
`stolen_gold` が 0 のインスタンスからは強奪を付与しない（`PowerCmd.Apply` が amount 0 を無視して返る）ので、強奪の行は盗んだ額が正の対象だけに出る。
同じ対象の行も合算しない。他の player が対象の行は `hidden_in_ui` だが、この 2 項目は付ける。
`Target` が無い・player でない場合と読取失敗は null・理由・warnings で、0 や自分の ID で補わない。

```json
{ "id": "THIEVERY_POWER", "amount": 20, "stolen_gold": 0, "target_player_id": "1", "skip_next_duration_tick": false }
```

（行の一部を抜粋。Markdown は `(stolen gold: 0; target player id: 1)`。0 も表示する。）

活力（`VIGOR_POWER`）の `vigor_attack` は private `VigorPower.Data` の攻撃との結合。
`has_command` は `commandToModify` が非 null か、`attacker_is_owner` は結合先の `Attacker == Owner`（未結合と読取失敗は null）、
`amount_when_started` は `amountWhenAttackStarted` の実値（未結合でもそのまま）。`readable` は必要な項目をすべて読めたとき true。
`BeforeAttack` が所有者の powered attack の開始で未結合なら結合して開始時の `Amount` を記録し、
`ModifyDamageAdditive` は結合先の `Attacker` が dealer と違う攻撃には足さず、
`AfterAttack` は同じコマンドのときだけ開始時の量を引く。参照は null に戻らないので、
攻撃の後に活力が残った行は結合したままになる。player・敵の行、`hidden_in_ui`、最小行のいずれにも付ける。
player の札の攻撃では、結合先の `ModelSource` と札の一致も加算の条件になる。この一致は出さないので、結合済み（`has_command=true`）の player の活力では、札の攻撃に足すかどうかは決まらない。敵の攻撃（`cardSource` が null）には `attacker_is_owner` で足りる。
読取失敗は `readable=false`、項目は null、理由は行の `unavailable_reasons.vigor_attack.<項目>`、
`warnings` は `status.VIGOR_POWER.vigor_attack.<項目>`。未結合（`has_command=false`）と区別し、
履歴・ラベル・解決値との差から補わない。Data の初期化やフックの呼び出しはしない。

```json
{ "id": "VIGOR_POWER", "amount": 6, "vigor_attack": { "readable": true, "has_command": false, "attacker_is_owner": null, "amount_when_started": 0 } }
```

（行の一部を抜粋。Markdown は `(vigor attack: has command: no, attacker is owner: none, amount when started: 0)`。）

呪術（`HEX_POWER`）の行の直下の `applier_combat_id` は、全パワーの行の `internal_state.applier_combat_id`（`PowerModel._applier` の `Creature.CombatId`。
`enemies[].combat_id` と同じ識別子で、0 も実在する ID）を写したもの。付与元を読むのは許可リストの 1 回だけで、ここでは読み直さない。
`PowerCmd.Apply` が新規のインスタンスにだけ設定し、既存の呪術への再付与（`InstanceType` が `None`。`FindExistingInstanceForStacking` → `ModifyAmount`）は付与元を差し替えない。
`HexPower.AfterDeath` は死んだ creature が `Applier` で除去が防がれなければ呪術を外し、`AfterRemoved` が所有者の全戦闘札から Hexed を消す。
付与元なし（読めた null）は `applier_readable=true` と null で理由なし。参照があるのに `CombatId` が null のとき（許可リストの読取失敗）は
`applier_readable=false`、null、行の `unavailable_reasons.applier_combat_id`（`internal_state.unavailable_reasons` と同じ理由）、
`warnings` の `status.HEX_POWER.internal_state.applier_combat_id`（1 件だけ）。
`hidden_in_ui`・最小行にも付ける。付与元の生死は `enemies[]` との照合で判断し、MOD は補わない。

```json
{ "id": "HEX_POWER", "amount": 2, "applier_readable": true, "applier_combat_id": 3 }
```

（行の一部を抜粋。Markdown は `(applier combat id: 3)`。付与元なしは `none`、読取失敗は `null (理由); applier readable: no`。）

`stolen_strength` / `stolen_dexterity` の盗んだ量はゲームの符号付き値（減少なので負数）。`player_id` は既存の player と同じ
文字列 ID。空の配列は盗んだ記録なしを表し、合算・正数化・既定値補完はしない。
読めない項目は `null`、同じ status 行の `unavailable_reasons.<key>` に理由、
トップレベル `warnings` に `status.<POWER_ID>.<key>` を出す。他項目・説明失敗時の
最小行でも内部状態の読取を続ける。false / 0 と不明を区別する。
Markdown は既存行に短く追記し、通常の skip=false と空の盗難配列は省略する。
`card_to_clone_readable=true` / `played_card_readable=true` も Markdown では省略し、JSON に保持する。false は表示する。
例は `(facing: Left)`、`(stolen strength: 17: -2.75)`。
拘束の鎖の使用済みフラグは false も `(bound card played: no)`、true は `yes` と表示する。
失敗は `null (理由)` と表示する。解決中はフック間の値なので行動計画は解決後に行う。
既存項目の意味を変えない追加のため版は上げていない（schema 4 のときの追加）。

#### パワーの変数（`dynamic_vars`、schema 12）

パワーの量のうち `amount` に無いもの（インフェルノ・深紅の衣の自傷、爆弾の威力、脆弱・脱力の倍率、プレートアーマーの減り方など）を、
説明文から読まずに済むように、player・敵・ペットのすべての `status[]` の行（`hidden_in_ui` の行・説明失敗時の最小行も）に
パワー自身の `DynamicVars` を付ける（出典は v0.113.1 `sts2-a671e9ef`、`cat12079801/sts2#1020`）。

- `dynamic_vars` — `{変数名: BaseValue}`。変数名は `CanonicalVars` の名前のまま（`SelfDamage`・`DamageIncrease` など。snake_case にしない）、
  並びは集合の順、値は decimal。`PowerModel.DynamicVars` は `CanonicalVars`（既定は空）から作られ、生きたパワーには `DeepCloneFields` が複製を持たせる。
  効果が読み書きするのは `DynamicVar.BaseValue`（ただのフィールド）なので、その値を出す。数値の意味を持たない `StringVar`
  （バリケードの `ApplierName`、`CoveredPower` などの `Applier`）は出さない（札・エンチャントの `dynamic_vars` と同じ）。変数を持たないパワーは `{}`。
- private `PowerModel._dynamicVars` をそのまま読み、null なら作る getter は呼ばない。フィールドが null なら `dynamic_vars` を null にし、
  行の `unavailable_reasons.dynamic_vars` と `warnings` の `status.<POWER_ID>.dynamic_vars` に理由を書く（`{}` にしない）。
  1 つの変数が読めなければ、その名前を null にして `unavailable_reasons["dynamic_vars.<名前>"]`・`warnings` の
  `status.<POWER_ID>.dynamic_vars.<名前>` に理由を書き、他の変数は残す。
- 型ごとの既存の項目（`damage`・`self_damage`・`cards_left`・`block_base_value`・`stolen_gold`）は同じ変数から読んだ値として残す。
  パワーの量が private `Data` にあるもの（活力の結合・弱化の記録など）は型ごとの項目で出し、`dynamic_vars` には入らない。
- Markdown は変数がある行だけ `(vars: SelfDamage=2)` を足す（空の `{}` は JSON だけ）。読取失敗は `vars: null (理由)`、
  変数ごとの失敗は `名前=null (理由)`。

```json
{ "id": "INFERNO_POWER", "amount": 12, "type": "Buff",
  "type_for_current_amount": "Buff", "is_temporary_power": false,
  "dynamic_vars": { "SelfDamage": 2 }, "self_damage": 2 }
```

#### 破壊（`Rend`）の入力（schema 9）

破壊（`Rend`）の倍率は対象の `Creature.Powers` のうち `Rend.ShouldCountPower` を満たすパワーの数
（`power.TypeForCurrentAmount == PowerType.Debuff && !(power is ITemporaryPower)`。出典は v0.113.1 `sts2-a671e9ef`、
`cat12079801/sts2#1034`）。その 2 つの入力を、player・敵・ペットのすべての `status[]` の行（`hidden_in_ui` の行・説明失敗時の最小行も）に付ける。
`status[]` が `Creature.Powers` から落とす行は、`IsVisible` が false・`Amount` が 0・`TypeForCurrentAmount` が `Debuff` でない、の 3 つがそろう行だけ
（`TypeForCurrentAmount` が読めない行も落とさない）。破壊が数えるパワーは、HUD に出なくても必ず `status[]` に出る。

- `type_for_current_amount` — `PowerModel.TypeForCurrentAmount`（`GetTypeForAmount(Amount)`）の enum 名。
  `GetTypeForAmount` は、`StackType` が `Counter` で `AllowNegative` のパワーの負の量を `Debuff`、`AllowNegative` でない
  `Debuff` 型のパワーの負の量を `Buff` とし、それ以外は `Type`。v0.113.1 で `AllowNegative` なのは `StrengthPower`・
  `DexterityPower`・`FocusPower`（`Type` は `Buff`）と `ShriekPower`・`ShrinkPower`（`Type` は `Debuff`）なので、
  `type` と食い違うのは負の筋力・敏捷・集中（`Debuff`）。`AllowNegative` でないパワーは量が 0 以下になると外れる（`ShouldRemoveDueToAmount`）。
  既存の `type` は `PowerModel.Type` のままで意味を変えない（負の筋力も `Buff`）。
- `is_temporary_power` — インスタンスが `ITemporaryPower` を実装するか。v0.113.1 では `TemporaryStrengthPower`・
  `TemporaryDexterityPower`・`TemporaryFocusPower`・`TemporaryThornsPower` とその派生。一時パワーは付与の前（`BeforeApplied`）に
  内部の筋力などを別の行（`STRENGTH_POWER` など。こちらは一時パワーではない）として付ける。

getter は `_amount` と `Type` / `StackType` / `AllowNegative` を読むだけで、フック・乱数・書き込みは無い
（`MonologuePower.StackType` は生きたパワーが持つ `DynamicVars` を読む）。`is_temporary_power` は型の検査。
読取失敗は `type` で補わず、項目を null・行の `unavailable_reasons.type_for_current_amount`・
`warnings` の `status.<POWER_ID>.type_for_current_amount` にする。Markdown は `type` と違うときと失敗のときだけ
`(type for current amount: Debuff)` / `null (理由)`、一時パワーは `(is temporary power: yes)` を足す（`type` と同じ値・false は JSON だけ）。

```json
{ "id": "STRENGTH_POWER", "amount": -2, "type": "Buff",
  "type_for_current_amount": "Debuff", "is_temporary_power": false }
```

#### 弱化（`DAMPEN_POWER`）の入力（schema 8）

マギナイト（`MagiKnight`）の `DAMPEN_MOVE` が付ける弱化の private `DampenPower.Data`（出典は v0.113.1 `sts2-a671e9ef`、
`cat12079801/sts2#1031`）。`PowerModel._internalData` を `ReadPowerData` で読み、`GetInternalData` / `InitInternalData`・フック・乱数は呼ばない。

- `caster_combat_ids` — `Data.casters`（`HashSet<Creature>`）の各 `Creature.CombatId`（`enemies[].combat_id` と同じ。0 も実在する ID）を昇順にした配列。
  `CombatId` の無い caster が居れば項目全体を読取失敗にする（ID を補わず、caster を落とさない）。
- `downgraded_cards` — `Data.downgradedCardsToOldUpgradeLevels`（`Dictionary<CardModel, int>`）の各組を
  `{ uid, uid_known, card_id, original_upgrade_level }` の行にした配列。`uid` は戦闘の uid registry が既に振った値
  （パイルの行は status の行より先に組むので、戦闘のパイルにある札は振られている）。registry に無い札は新たに採番せず、
  `uid: null`・`uid_known: false`・`uid_unavailable_reason` とし、`warnings` に `status.DAMPEN_POWER.downgraded_cards.uid` を出す。
  `card_id` は `CardModel.Id.Entry`。行は `uid` の順（uid の無い行は後ろで `card_id`・段階の順）。辞書の挿入順は
  `PlayerCombatState.AllCards`（山札の順を含む）なので出さない。

ゲーム側の意味:

- `MagiKnight.DampenMove` は対象ごとに、既存の弱化があればその `AddCaster(base.Creature)` だけを呼び（`PowerCmd.Apply` はしない）、
  無ければ新しいインスタンスに `AddCaster` してから `PowerCmd.Apply`（amount 1）する。`StackType` は `None`。
- `DampenPower.AfterApplied` は所有者の `PlayerCombatState.AllCards`（手札・山札・捨て札・廃棄札・プレイ中）のうち `IsUpgraded` の札ごとに、
  `CurrentUpgradeLevel` を記録して `CardCmd.Downgrade`（`DowngradeInternal`。段階 0 に戻し、数値・コスト・キーワードを基底に戻す）する。
  付与後に手に入った札・付与後に強化された札は記録されない。
- `DampenPower.AfterDeath` は死んだ creature が caster で、除去が防がれていなければ caster から外し、caster が空になれば弱化を外す。
- `DampenPower.AfterRemoved` は記録した各札を元の段階の回数だけ `CardCmd.Upgrade` する（`IsUpgradable` でない札は飛ばす）。
- `CombatManager.IsEnding` の間は `Downgrade`・`Upgrade` とも何もしない。

空の配列は読めた空（理由・警告なし）。Data が無い・型違い・フィールドの欠落は、項目を null・行の `unavailable_reasons.<key>`・
`warnings` の `status.DAMPEN_POWER.<key>` にする。Markdown は
`(caster combat ids: 3; downgraded cards: BASH#1 from +1, STRIKE_R#0 from +1)` の形で、空は `none`、
registry に無い札は `CARD_ID (uid unknown) from +1`、読取失敗は `null (理由)`。

```json
{ "id": "DAMPEN_POWER", "amount": 1, "caster_combat_ids": [3],
  "downgraded_cards": [ { "uid": "BASH#1", "uid_known": true, "card_id": "BASH", "original_upgrade_level": 1 } ] }
```

### パエルの歯のあるランの修飾子（`run.internal_state.modifier_ids`、schema 1.13.0）

ランの参加者に `PAELS_TOOTH` の所持者がいるとき、戦闘・選択・報酬を含む run 行に適用中の `ModifierModel.Id.Entry` を並べる。
所持は溶融中や保管札 0 枚を含む。順序と重複は `RunState.Modifiers` のまま。空列 `[]` は修飾子なし。
読取不能は `null` と `run.internal_state.unavailable_reasons.modifier_ids`・`warnings`。

`RunState.IterateHookListeners(null)` は `Hook.AfterCardChangedPiles` の Deck 追加通知にこのランの列を使う。
`Hoarder.AfterCardChangedPiles` が受け手の一つで、`RunState.AddModifierDebug` で列が差し替わった場合も現在の列を読む。
読み元は `RunState.Modifiers` の backing field。パエルの歯の `stored_cards` は `PaelsTooth._serializableCards` の候補列で、
重複を保持し、実際の空列は `[]`、異常な null は `null`・理由・warning を出す。
五輪書の通算は既存の `player.relics[].run_state.cards_added`（`BookOfFiveRings.AfterCardChangedPiles`）を読む。

### 棚卸しから生成した内部状態（`internal_state`、schema 1.2.0）

ゲームのモデルが持つ内部状態の棚卸し（[`state-inventory.md`](state-inventory.md)）で `public`（人間に見える、または見たものから確定できる）と分類し、
分類表（`docs/state-inventory-classification.json`）で `section` を付けた項目を、行ごとの `internal_state` に出す。
項目の一覧は分類表から `tools/state_inventory.py --write-allowlist` が生成する `McpMod.StateAllowlist.Generated.cs` で、項目ごとに分類の根拠（分類表の備考）を持つ。
生成物が分類表と一致するかは `--check-allowlist` で確かめる。読む処理は `McpMod.StateAllowlist.cs`。

付く行:

| section | 行 | 読むモデル |
| --- | --- | --- |
| `card` | `hand[]`・`draw_pile[]`・`discard_pile[]`・`exhaust_pile[]`・`play_pile[]` の行と、戦闘外の `player.deck[]` の行 | `CardModel`（派生の項目は派生の行だけ） |
| `power` | 全員の `status[]` の全行（`hidden_in_ui` の行・説明を読めない最小行を含む） | `PowerModel`（`InterceptPower` は private `Data`） |
| `relic` | `player.relics[]` | `RelicModel` |
| `player` | `player` 本体 | `Player.ExtraFields`（`ExtraPlayerFields`） |
| `battle` | `battle` 本体 | `CombatState` |
| `run` | パエルの歯を所持するランの `run` 本体 | `RunState` |

形と読み方:

- `internal_state` は `{キー: 値}`。行のモデルに当てはまる項目が無ければ `internal_state` を省く。
- 読むのは、宣言した型の宣言フィールド（`DeclaredOnly`）だけ。自動実装プロパティも getter を呼ばず、コンパイラが作る backing field（`<Name>k__BackingField`）を読む（無ければ null と理由）。getter・フック・遅延初期化・乱数は呼ばない。
  パワーの `Data` は private `PowerModel._internalData` をそのまま読み、型が入れ子の `Data` と違えば読まない。
- 宣言の型が値の変換の前提と違う（版が上がって型が変わった）とき、メンバーが無いときは、その項目を `null` にし、`internal_state.unavailable_reasons` に理由、`warnings` に `<行>.internal_state.<キー>` を出す。ほかの項目は残す。
  `null` で理由が無いのは、参照が無い・未設定という実値。型そのものが見つからない版では、`warnings` に出してその項目を付けない。
- 参照型は既存の id にする。カードは戦闘の uid（この読み取りで採番済みのもの。まだなら、持ち主の戦闘の山〔手札・山札・捨て札・廃棄・プレイ中〕にあって同じ読み取りの山の行に出るカードに限って採番する。山に無いカードには新たに振らず、`null` と理由）。
  クリーチャーは `combat_id`、カードのモデルは id（`Id.Entry`）、`CardPlay` はそのカードの uid、パワーは `{id, owner_combat_id}`。
- 人間に見えない情報は載せない。山の順番・これから出るレリックの袋（`RelicGrabBag`）・報酬の出現率のオフセット（`PlayerOdds`）・エンシェントが提示する前に決めた中身は、許可リストに入れない（契約試験が名前で検査する）。
  リストは並びに意味を持たせない順にそろえる（`combat_id` の昇順、id の順、キーワードは列挙の値の順）。

| section | キー | 型 | 読取元 | 意味 |
| --- | --- | --- | --- | --- |
| `card` | `clone_of_uid` | string \| null | `CardModel._cloneOf` | 複製元のカード（`CreateClone` が入れる。`IsClone`・`DupeOf` の判定） |
| `card` | `current_play_index` | int | `CardModel._currentPlayIndex` | 解決中の繰り返しプレイの何回目か（`OnPlayWrapper` が入れる。`LethalityPower` が読む） |
| `card` | `current_target_combat_id` | uint \| null | `CardModel._currentTarget` | 解決中の対象（`OnPlayWrapper`） |
| `card` | `deck_version_id` | string \| null | `CardModel._deckVersion` | 戦闘の札の元になったデッキ側の札のモデル id（schema 1.12.0）。入れるのは戦闘の開始時の `Player.PopulateCombatState`（デッキの各札を写した戦闘の札に元の札を入れる）だけで、`CardModel.AfterCloned`（複製・生成）と `DeprecatedCard.OnPlay` は null にする。戦闘中に作られた札（生成・複製・変化）と戦闘外の `player.deck[]` の行は null（理由なし）。参照先に uid は振らない。`ThievingHopper.ThieveryMove` が盗む候補（`DeckVersion != null`）に、`SwipePower.Steal` がデッキからの除去に、`SwipePower.BeforeDeath` が返す報酬に使う |
| `card` | `floor_added_to_deck` | int \| null | `CardModel._floorAddedToDeck` | デッキに入った階（`CardPileCmd`・`CardCmd` が `TotalFloor` を入れる。効果に読む箇所は無く、保存と履歴に使う） |
| `card` | `is_dupe` | bool | `CardModel._isDupe` | 複製（Dupe）の印（`CreateDupe`）。結果の山（`GetResultLocationForCardPlay`）と `ChainsOfBindingPower`・`FeralPower` が読む |
| `card` | `keywords_added` / `keywords_removed` | string[] | `CardModel._keywordsAddedFromOutside` / `_keywordsRemovedFromOutside` | 効果が後から付けた・外したキーワード（`AddKeyword`・`RemoveKeyword`）。強化の取り消し（`DowngradeInternal`）で残すかを決める |
| `card` | `transform_into_after_play_id` | string \| null | `CardModel._transformIntoAfterPlay` | プレイの後に変化する先のモデル id（`MourningBlow` が `SADNESS` を入れる） |
| `card` | `extra_damage` | decimal | `Rampage._extraDamageFromPlays`・`Thrash._extraDamage`・`TheBall._extraDamageFromPlays`・`Maul._extraDamageFromMaulPlays` | プレイ・効果で `DynamicVars.Damage` に足した累計。`AfterDowngraded`（弱化などの強化の取り消し）で基礎値に足し戻す |
| `card` | `spoils_act_index` | int | `SpoilsMap._spoilsActIndex` | 宝の地図が印を付ける幕（`AfterCreated` で入れる。未設定は -1） |
| `card` | `cards_in_hand_at_turn_end` | int | `Regret._cardsInHand` | 後悔（呪い）がターン終了時に写した手札の枚数（`BeforeSideTurnEnd`）。`OnTurnEndInHand` がその量のダメージを与えて 0 に戻す |
| `power` | `amount_on_turn_start` | int | `PowerModel._amountOnTurnStart` | ターンの開始の前（`Creature.BeforeTurnStart`）に `Amount` を写した値。その後に付いたパワーは 0 のまま。`DrawCardsNextTurnPower.ModifyHandDraw` は 0 なら引く枚数を増やさない |
| `power` | `covered_creature_ids` | uint[] | `InterceptPower.Data.coveredCreatures` | かばっている味方の `combat_id`（マルチプレイ） |
| `power` | `applier_combat_id` | uint \| null | `PowerModel._applier` | パワーを付けた creature（schema 1.3.0。全パワー）。`PowerCmd.Apply` が新しい個体にだけ入れ、既存の個体に足すときは最初の付与元のまま。付与元の死で外れる（`ShrinkPower`・`ConstrictPower`・`HexPower`・`CoveredPower`・`GuardedPower`・`MagicBombPower` の `AfterDeath`）、付与元の攻撃だけに効く（`FlankingPower`・`KnockdownPower` の `ModifyDamageMultiplicative`、`ImpaledPower.AfterDamageReceived`）、付与元の札だけ数える（`StranglePower`・`OblivionPower` の `BeforeCardPlayed`、`TagTeamPower.ModifyCardPlayCount`）。付与元なしは null（理由なし） |
| `power` | `target_combat_id` | uint \| null | `PowerModel._target` | インスタンスごとの対象のプレイヤーの creature（schema 1.3.0。全パワー）。付けるとき（`GremlinMerc`→泥棒、`TheInsatiable`→砂地獄、`Aeonglass`→`WitheringPresencePower`）か `SwipePower.Steal`・`SurprisePower` が入れる。`ThieveryPower.Steal`・`HeistPower.BeforeDeath`・`SwipePower.BeforeDeath`・`SandpitPower`・`WitheringPresencePower.AfterCardPlayed`・`PowerModel.IsVisible` が読む。対象なしは null（理由なし） |
| `relic` | `status` | string | `RelicModel._status` | `Normal` / `Active` / `Disabled`（画面ではレリックの光り方）。`PaelsFlesh` が条件に使う |
| `relic` | `is_wax` | bool | `RelicModel._isWax` | 蝋のレリック（`ToyBox` が作る。溶けたかは既存の `melted`） |
| `relic` | `floor_added_to_deck` | int | `RelicModel._floorAddedToDeck` | 取得した階（`RelicCmd`。保存と履歴に使う） |
| `relic` | `golden_path_act` | int | `GoldenCompass._goldenPathAct` | 黄金の道を引いた幕（`AfterObtained`）。その幕の地図と不明マスの部屋の種類（`ModifyUnknownMapPointRoomTypes`）を変える |
| `relic` | `card_being_played_uid` | string \| null | `MusicBox._cardBeingPlayed` | このターン最初の自分のアタックで、解決中のもの（`BeforeCardPlayed` で入れ、`AfterCardPlayed` で複製を手札に加えて null に戻す） |
| `relic` | `affected_card_uid` | string \| null | `PaelsLegion._affectedCardPlay`（の `Card`） | ブロックを 2 倍にする解決中のカード |
| `relic` | `stored_cards` | `[{id, upgrade_level, enchantment_id}]` | `PaelsTooth._serializableCards` | 預けたカード（人間が選んだもの）。戦闘の終わりに乱数で 1 枚を返すので、並びは id・強化段階・エンチャント ID の順。空列は `[]`、読取失敗は `null` と理由 |
| `relic` | `attack_to_double_uid` | string \| null | `PenNib._attackToDouble` | 2 倍にする解決中のアタック |
| `relic` | `triggering_card_uid` | string \| null | `UnsettlingLamp._triggeringCard`・`Vambrace._triggeringCard` | 戦闘で最初にデバフを付けた（UnsettlingLamp）・ブロックを得た（Vambrace）カード。そのカードの解決中だけ 2 倍にする。消すのは `BeforeCombatStart`・`AfterCombatEnd` だけなので、安定した state でも戦闘の終わりまで残る。効くかは `combat_state` の `used_this_combat`（UnsettlingLamp）・`block_gained_this_combat`（Vambrace）で決まる |
| `relic` | `doubled_power_ids` | `[{id, owner_combat_id}]` | `UnsettlingLamp._doubledPowers` | 2 倍にしたパワー（一時パワーの内側を二重に倍にしないため）。リストが未作成なら空 |
| `player` | `card_shop_removals_used` | int | `ExtraPlayerFields.CardShopRemovalsUsed` | ショップでカードを除去した回数（除去の値段が上がる） |
| `run` | `modifier_ids` | string[] \| null | `RunState.Modifiers` | 適用中の修飾子 ID。適用順と重複を保持（schema 1.13.0） |
| `battle` | `ally_combat_ids` | uint[] | `CombatState._allies` | 味方側のクリーチャー（プレイヤー・ペット・マルチプレイの味方） |
| `battle` | `escaped_creature_ids` | uint[] | `CombatState._escapedCreatures` | 逃げた敵（`CreatureEscaped`） |

解決中の印（`current_play_index`・`current_target_combat_id`・`transform_into_after_play_id`・`cards_in_hand_at_turn_end`・`card_being_played_uid`・`affected_card_uid`・`attack_to_double_uid`・`doubled_power_ids`）は、
カードのプレイ・攻撃・ターン終了の解決の途中でだけ値を持ち、安定した state では既定値（null・0・空）が多い。
解決の途中で値を観測できるのは、戦闘中の選択画面（`hand_select`・`card_select` の grid と choose-a-card など。
[`battle` を付ける](#戦闘中の選択画面の-battleschema-140)）だけ。
`triggering_card_uid` は解決の途中でなくても戦闘の終わりまで残る（上の表）。

Markdown は既定値（false・0・空・理由の無い null・`Normal`）を省き、`(internal: is dupe yes, extra damage 5)` の形で行に足す。
カードは詳細の行（`  - internal: …`）、レリック・パワーは行の末尾、player はステータスの行、battle は `Round` の行。
`floor_added_to_deck`・`deck_version_id`（デッキ由来の全札に値がある）・`applier_combat_id`・`target_combat_id`（ほぼ全パワーの行に値がある）は読めたときは出さず、`amount_on_turn_start` は `amount` と違うときだけ出す。`golden_path_act`・`spoils_act_index` は 0（第 1 幕）も -1（未設定）も実値なので常に出す。読めなかった値は `null (理由)`。
デッキの markdown は同じ札を束ねて表示するので、デッキの行の `internal_state` は JSON だけ。

```json
{ "uid": "RAMPAGE#4", "internal_state": { "clone_of_uid": null, "current_play_index": 0, "current_target_combat_id": null,
  "deck_version_id": "RAMPAGE", "floor_added_to_deck": 6, "is_dupe": false, "keywords_added": [], "keywords_removed": [],
  "transform_into_after_play_id": null, "extra_damage": 5 } }
```

### Keyword Object

```jsonc
{
  "name": "Vulnerable",
  "description": "Takes 50% more damage from attacks."
}
```

---

### 戦闘外の準備の判定（`scenario_setup_guard`、schema 10）

検証用の GET（`?passive=1&scenario_setup_guard=1`）だけで、state に戦闘外で準備（開発コンソール）してよいかの根拠を足す。
`passive` の無い GET には付けない。state と同じメインスレッドの呼び出しの中で読む。

```jsonc
"scenario_setup_guard": {
  "version": 1,
  "screen": "map",              // 判定した state_type
  "room": "MapRoom",            // RunState.CurrentRoom の実型名。部屋なしは null
  "combat_starting": false,     // CombatManager.IsStarting
  "combat_in_progress": false,  // CombatManager.IsInProgress
  "run_over": false,            // RunState.IsGameOver
  "allowed": true,
  "reason": null                // allowed が false のときの理由（下表）。true なら null
  // 読めない値は null にし、"unavailable_reasons": ["<項目>: <例外>"] を付ける
}
```

`allowed` が true になるのは、ランが進行中で終わっておらず、戦闘の開始中でも進行中でもなく、解決中（`is_resolving`）でもなく、
前面の画面とその部屋が次の組のどれかのときだけ: `map` と `MapRoom`、`event` と Neow（`EventRoom.CanonicalEvent is Neow`）の `EventRoom`、
`rest_site` と `RestSiteRoom`。選択・報酬・ショップ・メニュー・`game_over`・未対応の画面、戦闘や別の部屋に重なった `map` は許さない。
値が読めないときは許さない。true 以外は準備不可として扱う。

| `reason` | 意味 |
|---|---|
| `no_run` | ランが進行していない（メニュー） |
| `unreadable` | 判定に要る値が読めない（`unavailable_reasons`） |
| `run_over` | `RunState.IsGameOver` |
| `combat_in_progress` / `combat_starting` | 戦闘の進行中 / 開始中 |
| `screen_not_setup` | 前面が `map` / `event` / `rest_site` 以外 |
| `resolving` | `is_resolving` が true |
| `no_current_room` | `CurrentRoom` が無い（移動中など） |
| `room_not_setup` | 部屋が `MapRoom` / `EventRoom` / `RestSiteRoom` 以外（戦闘・ショップ・宝箱の部屋に重なった map など） |
| `screen_room_mismatch` | 準備用の部屋だが、前面の画面がその部屋のものでない（Neow の部屋に重なった map など） |
| `event_not_neow` | Neow 以外のイベント |

同じ判定を、`scenario_setup_guard` 付きの `dev_console` がコマンドを実行する直前にも行う（[検証用の開発コンソール](#検証用の開発コンソール既定は無効)）。
出典（v0.113.1、`sts2-a671e9ef`）: `RunState.CurrentRoom` / `IsGameOver`、`CombatManager.IsStarting` / `IsInProgress`、`EventRoom.CanonicalEvent`、`Neow`。
GET は何も押さず、乱数・hook を使わない。

## State Types

### `menu`

No run in progress, or a blocking menu/popup shown during a run.

```jsonc
{
  "state_type": "menu",
  "message": "No run in progress. Player is in the main menu.",
  "menu_screen": "main",
  "options": ["continue", "singleplayer", "multiplayer", "compendium", "timeline", "settings", "quit"]
}
```

Use `menu_select` with one of the advertised options. Options are accepted case-insensitively.
If a visible option is intentionally withheld from automation, the state may also include `blocked_options` with a reason. For example, Timeline can be blocked while obtained epochs still need manual reveal because opening that game state through automation can trigger invalid unlock-state errors.

Menu sub-screens expose their own options:

- `singleplayer`: `standard`, `daily`, `custom`, `back`
- `multiplayer`: `host`, `join`, `load`, `abandon`, `back`
- `multiplayer_host`: `standard`, `daily`, `custom`, `back`
- `multiplayer_join`: `refresh`, `back`, `join_<index>`, `join_<player_id>`
- `multiplayer_load_lobby`: `confirm` / `embark`, `unready`, `back`
- `profile_select`: `profile_1`, `profile_2`, `profile_3`, `back`
- `character_select`: character IDs/names, `back`, `confirm` / `embark`, `unready` (MP, after readying)
- `tutorial_prompt`: `no`, `yes`
- `popup`: advertised popup button labels, normalized to lowercase words such as `ignore` or `back`
- `timeline`: `advance`, `back`

The `multiplayer` submenu is gated on whether a multiplayer save exists: with no save, `host` is shown and `load` / `abandon` are hidden; with a save, those are shown and `host` is hidden. `abandon` opens a vertical popup — the next state turn surfaces the popup's `confirm` / `cancel` options, not a transition.

#### `popup` — 確認ポップアップの表示文言（schema 3）

`NVerticalPopup` の画面上のタイトルを `message`、本文を `body` に返す。
ゲームの現在の言語・書式展開後のラベルから読み、リッチテキストのタグを除去する。
Markdown もタイトル、本文、選択肢の順で表示する。

```jsonc
{
  "state_type": "menu",
  "menu_screen": "popup",
  "message": "ゲーム終了",
  "body": "本当に終了しますか？",
  "options": [
    { "name": "はい", "enabled": true },
    { "name": "いいえ", "enabled": true }
  ]
}
```

これは日本語の終了確認の文言例で、実機取得の fixture ではない。
`message` / `body` は省略されることがあるため、存在を確認して読むこと。
縦型ポップアップの読み取りが失敗した場合、その項目だけを省略し、
`warnings` に `menu.popup.message` または `menu.popup.body` を記録する。
もう一方の文言と選択肢は維持する。空文字列を正常に読めた場合はそのまま返す。
`NVerticalPopup` を特定できずボタンだけを見つけた場合は、従来どおり
`message: "Popup active."` / `body: null` を返す。この場合も確認の種類は不明と扱う。
選択肢は表示ラベルを正規化した名前なので、現在の `options` を使う。
肯定の選択肢を自動で押すことはない。

出典: `NVerticalPopup.EnsureNodesAreSet` は private の `TitleLabel` / `BodyLabel` に
`Header` / `Description` を設定し、`SetText(LocString, LocString)` は
`GetFormattedText()` の結果をラベルに設定する。
終了確認は `NMainMenu.ConfirmAndQuit` → `NGenericPopup.WaitForConfirmation`、
ラン放棄の確認は `NPauseMenu.OnGiveUpButtonPressed` → `NAbandonRunConfirmPopup._Ready`。
終了確認の上記文言は `localization/jpn/main_menu_ui.json` の
`QUIT_CONFIRM_POPUP.header` / `.body`、ボタンは `GENERIC_POPUP.confirm` / `.cancel` に基づく。
既存の文言欄の取得修正と Markdown への表示追加なので版は上げていない（schema 3 のときの追加）。

#### `multiplayer_join` — Friend list / FastMP

```jsonc
{
  "state_type": "menu",
  "menu_screen": "multiplayer_join",
  "message": "Pick a friend to join, or 'refresh' to update the list.",
  "fast_mp": false,             // true when --fastmp / Steam not initialized; auto-joins 127.0.0.1:33771
  "loading": false,             // true while refreshing or while a join handshake is in flight
  "no_friends": false,          // true when refresh returned an empty list
  "friends": [
    { "index": 0, "name": "Bob", "player_id": "76561198000000000", "enabled": true }
  ],
  "options": [
    { "name": "join_0",  "enabled": true },
    { "name": "refresh", "enabled": true },
    { "name": "back",    "enabled": true }
  ]
}
```

`menu_select` accepts either `join_<index>` (e.g. `join_0`) or `join_<player_id>` (e.g. `join_76561198000000000`) — both click the same button. With `fast_mp: true`, the screen auto-joins localhost on open; you typically won't need to issue a join action in that mode.

#### `multiplayer_load_lobby` — Resume saved MP run

Reached via the `multiplayer` submenu's `load` option (host) or by joining a host that already loaded a save (client). Mirrors the standard MP character-select ready/unready flow but on a fixed (saved) party.

```jsonc
{
  "state_type": "menu",
  "menu_screen": "multiplayer_load_lobby",
  "message": "Multiplayer load lobby. Confirm to ready up; ...",
  "lobby": {
    "type": "host",                // or "client"
    "game_mode": "standard",       // or "daily" / "custom"
    "ascension": 0,
    "act": 2,
    "floor": 13,
    "character_id": "REGENT",      // local player's saved character
    "current_hp": 64,
    "max_hp": 80,
    "gold": 240,
    "expected_player_count": 3,
    "connected_player_count": 2,
    "players": [
      {
        "id": "76561198000000000",
        "is_local": true,
        "character_id": "REGENT",
        "is_connected": true,
        "is_ready": false,
        "platform_name": "Alice"
      }
    ]
  },
  "options": [
    { "name": "confirm", "enabled": true },
    { "name": "embark",  "enabled": true },   // alias of confirm
    { "name": "back",    "enabled": true },
    { "name": "unready", "enabled": false }   // becomes enabled after confirm/embark
  ]
}
```

#### `character_select` — extended for MP

The same screen drives SP, MP host, and MP client. In MP, an additional `lobby` block appears, and the `unready` option becomes available after the local player has hit `confirm`/`embark`:

```jsonc
{
  "state_type": "menu",
  "menu_screen": "character_select",
  "message": "Select a character.",
  "characters": [ /* same shape as SP */ ],
  "lobby": {                       // present only in MP
    "type": "host",                // "host" | "client" | "singleplayer"
    "game_mode": "standard",       // "standard" | "daily" | "custom"
    "max_players": 4,
    "ascension": 0,
    "max_ascension": 5,            // min across all lobby members' MaxMultiplayerAscension
    "all_ready": false,
    "is_about_to_begin": false,    // mirrors StartRunLobby.IsAboutToBeginGame()
    "is_local_ready": false,
    "local_player_id": "76561198000000000",
    "player_count": 2,
    "seed": "...",                 // optional, present only for daily/custom
    "players": [
      {
        "id": "76561198000000000",
        "slot_id": 0,
        "is_local": true,
        "is_host": true,           // only positively true for self when hosting; otherwise false
        "character": "The Regent",
        "character_id": "REGENT",
        "is_ready": false,
        "platform_name": "Alice"
      }
    ]
  },
  "options": [
    { "name": "REGENT",  "enabled": true },
    { "name": "IRONCLAD","enabled": true },
    /* ... other characters and lockable RANDOM ... */
    { "name": "confirm", "enabled": true },
    { "name": "embark",  "enabled": true },
    { "name": "back",    "enabled": true },
    { "name": "unready", "enabled": false }
  ]
}
```

In SP the `lobby` field is omitted, and `unready` does not appear (the unready button is only enabled after MP ready).

### `unknown`

Run state or room type not recognized.

```jsonc
{
  "state_type": "unknown",
  "room_type": "SomeUnknownRoom"  // May be present
}
```

### `monster` / `elite` / `boss` — Combat

```jsonc
{
  "state_type": "monster",  // or "elite", "boss"
  "battle": {
    "round": 1,
    "turn": "player",       // "player" or "enemy"
    "is_play_phase": true,
    "enemies": [
      {
        "entity_id": "JAW_WORM_0",    // Synthesized ID for targeting
        "combat_id": 42,               // Internal combat ID
        "name": "Jaw Worm",
        "alive": true,                 // Always present. false = dead but still in the fight
        "hittable": true,              // Creature.IsHittable; null when unreadable (see below)
        "can_receive_powers": true,    // Creature.CanReceivePowers; null when unreadable (schema 1.11.0)
        "hp_display_is_infinite": false, // HpDisplayExtensions.IsInfinite(Creature.HpDisplay) (schema 1.7.0)
        "slot_name": "front",          // Creature.SlotName; null = no slot (schema 6)
        "hp": 44,
        "max_hp": 44,
        "block": 0,
        "status": [ /* Power Objects */ ],
        "intents": [
          {
            "type": "Attack",          // Attack, Defend, Buff, Debuff, Sleep, etc.
            "label": "11",             // Damage number or short label
            "title": "Attack",         // Hover tip title
            "description": "Deals 11 damage.",  // Hover tip description
            "attack": {                // Attack / DeathBlow only (see below)
              "readable": true,
              "base_damage": "11",     // DamageCalc() before modifiers, decimal as a string
              "hits": 1,               // AttackIntent.Repeats
              "resolved_damage": 11    // GetSingleDamage against the local player
            }
          }
        ]
      },
      {
        // A dead enemy the game keeps in the fight (a Decimillipede segment waiting to
        // reattach). Listed so the revive is visible; it cannot be targeted while dead.
        "entity_id": "DECIMILLIPEDE_SEGMENT_1",
        "combat_id": 44,
        "name": "Decimillipede Segment",
        "alive": false,
        "hittable": false,             // IsDead -> not hittable
        "can_receive_powers": false,   // ReattachPower: its owner is reviving (no IsDead check here)
        "hp": 0,
        "max_hp": 46,
        "block": 0,
        "status": [ /* still carries REATTACH_POWER */ ],
        "revive": {                    // Present only when it is actually coming back
          "in_turns": 2,               // Enemy turns until it heals back up; null if unknown
          "power_id": "REATTACH_POWER",
          "power_name": "Reattach",
          "hp": 25                     // HP it returns with; null when not readable
        },
        "intents": []                  // Becomes a Heal intent on the turn before the revive
      }
    ],
    "enemy_move_context": { /* schema 6: ascension levels, Dread, encounter slots (see below) */ }
  },
  "run": { ... },
  "player": { ... }  // Includes hand, energy, piles, orbs during combat
}
```

**Dead enemies.** `enemies[]` normally holds only living creatures, because the game drops a
killed enemy from the fight. A few powers refuse that — `ReattachPower` on a Decimillipede
segment is the one that matters — and their owner stays in the fight as a corpse. Those
corpses are listed with `alive: false` and `hp: 0`, keeping the `entity_id` they already had,
so a caller that never meets one sees no change. Filter on `alive` (not on `hp > 0`) to get
the targetable enemies; `play_card` and `use_potion` already refuse a dead target.

`revive` is present only when the corpse is actually scheduled to come back: `in_turns` is
read off the monster's move chain (2 the turn it dies, 1 the turn before it stands up, and
its `intents` then show `Heal`), and `hp` is the amount it heals for. A corpse held in the
fight by something that never revives it (`DieForYouPower`, `IllusionPower`) has `alive: false`
and no `revive`, and neither does the last segment of a Decimillipede — with no living peer
left to reattach to, the fight is over rather than about to restart.

#### 敵に当てられるか（`hittable`、追加。schema 4）

`battle.enemies[]` の**すべての行**（通常の敵・ミニオン・復活待ちの死亡個体・復活後）に `hittable` を出す。
値は `Creature.IsHittable` の bool をそのまま。HP・`alive`・パワー名から作らない。
本体の対象の抽選（`CombatState.HittableEnemies` → `CardCmd.AutoPlay` の `CombatTargets` の抽選。ジャガーノートなどのランダムな対象）と同じ getter である。
`targetable`（手で狙えるか）とは別で、本体の注釈どおり「当てられない」のではない全体攻撃もある。

- getter（v0.113.1 `sts2-a671e9ef` の `Creature.IsHittable`）: `IsDead`（`CurrentHp <= 0`）なら false、
  そうでなければ `Hook.ShouldAllowHitting(CombatState, creature)`（戦闘の hook listener の `ShouldAllowHitting` が 1 つでも false なら false）。
  override は敵では `ReattachPower`（ムカデの体節）・`IllusionPower`（パラフライト `PARAFRIGHT`・牙むく眼 `EYE_WITH_TEETH`）・`AdaptablePower`（実験体 `TEST_SUBJECT`）で、
  どれも「その所有者が復活中（private の `isReviving`）なら false」。味方の `DieForYouPower`・`WuppysLoyaltyPower` は対象が生存しているかを返す。
- 読み取りは状態を書き換えない（`IsDead` は HP の比較、各 override は private の内部データか `IsAlive` を読むだけ）。
  `Hook.ShouldAllowHitting` は `Hook.IterateCombatHookListeners` を通るので、`CanPlay` と同じく他の MOD の hook subscriber を通りうる。
  `CombatManager.ShouldSkipCombatEffect` の間は listener が空になり、生存個体は true になる（本体の値のまま）。
- 読めなければ `hittable: null`、敵の行の `unavailable_reasons.hittable` に理由、warning `battle.enemies.<entity_id>.hittable`。
  false と区別する。旧 MOD では欠落し、未観測として扱う。
- Markdown は HP の行に `| Hittable: yes|no`（読めなければ `null (理由)`）。追加だけなので版は上げていない（schema 4 のときの追加）。
- 契約試験は [`tests/EnemyHittableContract`](../tests/EnemyHittableContract/README.md)。

#### パワーを付けられるか（`can_receive_powers`、schema 1.11.0）

`battle.enemies[]` の**すべての行**（生存・戦闘に残る死亡個体、単独・協力プレイで共通）に `can_receive_powers` を出す。
値は本体の `Creature.CanReceivePowers` の bool をそのまま。`hittable`・HP・`alive`・パワー名から作らない（`cat12079801/sts2#1175`）。

- getter（v0.113.1 `sts2-a671e9ef` の `Creature.CanReceivePowers`）: `CombatState == null` なら false、
  そうでなければ `Hook.ShouldAllowHitting(CombatState, creature)`。`hittable`（`Creature.IsHittable`）との違いは、先頭の `IsDead` の検査が無く、
  代わりに `CombatState` の有無を見ること。本体の注釈も「死んだ個体は当てられないが、パワーは付けられる」と書く。
  override は `hittable` と同じ（`ReattachPower`・`IllusionPower`・`AdaptablePower` は所有者が復活中なら false、
  `DieForYouPower`・`WuppysLoyaltyPower` は対象が生存しているか）。
- 効果で読む箇所: `PowerCmd.Apply<T>` は `CombatManager.IsEnding` の次にこれを見て、false なら付与しない（null を返す）。
  `PowerCmd.Apply(power, …)` も入口と `power.BeforeApplied` の後の 2 回見る。どのパワーのどの付与元でも同じ。
  例: クイーンの `BurnBrightForMeMove` は `GetTeammatesOf` の各個体（自分を除く）に `PowerCmd.Apply<StrengthPower>` を呼ぶので、
  付くのはこの値が true の個体だけ（拒否された個体があっても、後の自分へのブロックは行う）。
- `CombatState` を持つ生存中の行では `hittable` と同じ値になる（どちらも同じ `Hook.ShouldAllowHitting` を読む）。ただし MOD はこれを前提に値を作らず、両方とも本体の getter を読む。
  違いが出るのは戦闘に残る死亡個体で、`hittable` は常に false、`can_receive_powers` は listener の答え
  （復活中のムカデの体節は false、`DieForYouPower`・`WuppysLoyaltyPower` が戦闘にいれば false、それ以外は true）。
  `hittable` から作れないのはこのためで、listener の答えを engine が写すと「推測」になるので getter の値を出す。
- 読み取りは状態を書き換えない（`Hook.IterateCombatHookListeners` は新しい列を作って返すだけ、各 override は参照の比較と
  private の bool・`IsAlive` を読むだけ。乱数を引かず、hook の効果を走らせない）。費用は `hittable` と同じ（listener の列を 1 回作る）。
  `CombatManager.ShouldSkipCombatEffect` の間は listener が空になり true になる（本体の値のまま）。
- 読めなければ `can_receive_powers: null`、敵の行の `unavailable_reasons.can_receive_powers` に理由、
  warning `battle.enemies.<entity_id>.can_receive_powers`。false と区別する。旧 MOD（schema 1.11.0 より前）では欠落し、未観測として扱う。
- プレイヤー（`player`・味方の行）には出さない（いま engine が読むのは敵の側だけ）。
- 全ての戦闘の全ての敵の行に付ける。クイーンの戦闘に限らないのは、条件が `PowerCmd.Apply` のすべての付与に掛かり
  （プレイヤーの札・ポーションの対象、敵どうしの付与）、読み取りの費用が `hittable` と同じで、戦闘ごとに有無が変わる契約にすると
  engine が「無いのは対象外か、旧 MOD か」を見分ける必要が出るため。
- Markdown は HP の行の `Hittable` の後に `| Receives powers: yes|no`（読めなければ `null (理由)`）。
- 契約試験は [`tests/EnemyCanReceivePowersContract`](../tests/EnemyCanReceivePowersContract/README.md)。

#### HP の無限表示（`hp_display_is_infinite`、schema 1.7.0）

`battle.enemies[]` の**すべての行**（生存・戦闘に残る死亡個体、単独・協力プレイで共通）に `hp_display_is_infinite` を出す。
値は本体の拡張 `HpDisplayExtensions.IsInfinite(Creature.HpDisplay)` の bool をそのまま。HP・最大 HP・パワー名・行動 ID から作らない
（`cat12079801/sts2#1088`）。

- 出典（v0.113.1 `sts2-a671e9ef`）: `Creature.HpDisplay` は列挙 `HpDisplay`（`Normal` / `InfiniteWithNumbers` / `InfiniteWithoutNumbers`）の
  public な自動プロパティ。`HpDisplayExtensions.IsInfinite` は `InfiniteWithNumbers` と `InfiniteWithoutNumbers` で true、`Normal` で false。
  2 つの無限の違い（数字を見せるか）は `HpDisplayExtensions.ShowsNumbers` で、読むのは `NHealthBar`（HP バーの見た目）だけなので出さない。
- 書き込み（本体の全件）:
  - `HardenedShellPower.AfterDamageReceived`: その手番に受けた防がれなかったダメージの累計が `Amount` に達したら `InfiniteWithNumbers`。
    `HardenedShellPower.BeforeSideTurnStart`（どちらの陣営の手番の開始でも）で `Normal` に戻す。付けるのは `SkulkingColony.AfterAddedToRoom`（自分自身に。量の引数は 20）
  - `WaterfallGiant.TriggerAboutToBlowState`: 最大 HP と HP を 999999999 にしたうえで `InfiniteWithoutNumbers`。呼ぶのは `SteamEruptionPower.AfterDeath`
    （所有者の死亡で、除去が防がれていないとき）で、死亡の継続にあたる。以後 `Normal` に戻す箇所は無い
- 効果で読む箇所: `HellraiserPower.AfterCardDrawnEarly` が `CombatState.HittableEnemies.All(c => c.HpDisplay.IsInfinite())` のとき、
  private の `infiniteAutoPlaysThisTurn`（ヘル・レイザーの行の `status[].infinite_auto_plays_this_turn`）を数え、それが 9 に達した後に引いたストライクは自動プレイしない（その手番の 10 本目以降）。
  条件が成り立たないドローと、所有者の陣営の手番の終わり（`AfterSideTurnEnd`）で数を 0 に戻す。
  判定は当てられる敵（`hittable: true` の行）だけに掛かるので、engine は `hittable` と組にして読む。
  `hittable: true` の行が 1 つも無いときも条件は成り立つ（`Enumerable.All` は空の集合で true）ので、その手番のドローも数えて上限に掛かる。
  `Creature` の注釈も「表示用だが、HellraiserPower などいくつかの効果がダメージを受けうるかの判定に使う」と書いている。
- 読み取りは状態を書き換えない（自動プロパティの getter と、列挙の値だけを見る静的メソッド。乱数を引かない）。
- 読めなければ `hp_display_is_infinite: null`、敵の行の `unavailable_reasons.hp_display_is_infinite` に理由、
  warning `battle.enemies.<entity_id>.hp_display_is_infinite`。false と区別する。旧 MOD（schema 1.7.0 より前）では欠落し、未観測として扱う。
- プレイヤー（`player`・味方の行）には出さない。本体で `HpDisplay` を書くのは上の 2 か所だけで、どちらも敵自身に掛かる
  （`HardenedShellPower` の所有者は付けた `SkulkingColony` 自身、`WaterfallGiant` は自分の `Creature`）。プレイヤーにこの値を変える
  通常の経路は無く（開発コンソールの `ApplyPowerConsoleCmd` で任意のパワーを付けた場合を除く）、効果で読む `HellraiserPower` も
  `CombatState.HittableEnemies`（敵の側）しか見ない。
- Markdown は HP の数字の直後に ` ∞`（例 `HP: 999999999/999999999 ∞ | Block: 0`）。false なら何も付けない。読めなければ ` ∞: null (理由)`。
- 契約試験は [`tests/EnemyHpDisplayContract`](../tests/EnemyHpDisplayContract/README.md)。

#### 敵の行動状態（追加、schema 4。入力の追加は schema 6）

`battle.enemies[].move_state` は全モンスターに出す。生存敵・戦闘に残る死亡敵、
単独・協力プレイで共通。四値は schema 4 のときの追加、残りの項目は schema 6（[敵の行動の入力](#敵の行動の入力schema-6)）。旧 MOD の欠落は unknown。
意図の type・label・round や MOD 独自の履歴から値を補完しない。

```json
{
  "readable": true,
  "id": "WEB_CANNON_MOVE",
  "follow_up_id": "CURL_AND_GROW_MOVE",
  "last_logged_id": "WEB_CANNON_MOVE",
  "can_transition_away": true
}
```

| 項目 | 読取元・意味 |
| --- | --- |
| `readable` | 四値すべてが取得できたときだけ true。失敗が一つでもあれば false |
| `id` | `MonsterModel.NextMove.Id`。スタン中は `STUNNED` などゲームの実値 |
| `follow_up_id` | `NextMove.FollowUpState?.Id ?? NextMove.FollowUpStateId`。スタン時も実際の復帰先。通常時には次の分岐状態の ID の場合もある |
| `last_logged_id` | `MonsterModel.MoveStateMachine.StateLog` の実際の末尾の `Id`。MOD の観測履歴でも「最後に実行した行動」の再構築でもない |
| `can_transition_away` | `NextMove.CanTransitionAway` の bool。false も必ず出す |

**時機（`id` がいつ次の行動か）:** プレイヤーが入力できる落ち着いた局面では、`id` は次の敵手番で実行する行動。次の行動を抽選する `RollMove` は、プレイヤー側の追加ターンでない手番の始め（`CombatManager.StartTurn` → `Creature.PrepareForNextTurn`）と、プレイヤーの手番中に敵が追加されたとき（`CombatManager.AfterCreatureAdded`）にだけ呼ばれる。同じ手番の中でも `MonsterModel.SetMoveImmediate` が置き換えることがある（スタン・`Queen` の激昂・`ReattachPower`・`IllusionPower` など）。敵が行動してから次のプレイヤー手番が始まるまでは、実行済みの行動の ID が残る。追加ターンでは抽選しない（敵はまだ行動していないので `id` は次の行動のまま）。

**次の行動を持たない行動:** `GasBomb` の `EXPLODE_MOVE`（`GasBomb.GenerateMoveStateMachine`。爆発のあと自分を倒す）のように、ゲームが `FollowUpState` も `FollowUpStateId` も設定しない行動では、`follow_up_id` は null で `readable` は false、warnings に `follow_up_id` の理由が出る（四値の意味は変えていない）。読取の失敗とは schema 6 の `has_follow_up`（終端なら false の実値、読取失敗なら null と理由）で区別する。

参照・ログ末尾がない場合や読取例外では、該当項目だけ `null` とし、
`move_state.unavailable_reasons.<key>` に `ExceptionType: message`、トップレベルの
`warnings` に `battle.enemies.<entity_id>.move_state.<key>` を付ける。成功した項目は保持する。
復帰先の両参照が null の場合も取得不能で、通常行動に置き換えない。
空文字列の follow-up はゲームが初期状態に戻る指定として扱う実値なので、そのまま出す。
`unavailable_reasons` は失敗がなければ省略する。

```json
{
  "readable": false,
  "id": "STUNNED",
  "follow_up_id": "POUNCE_MOVE",
  "last_logged_id": null,
  "can_transition_away": false,
  "unavailable_reasons": {
    "last_logged_id": "InvalidOperationException: MoveStateMachine.StateLog が空です"
  }
}
```

Markdown は敵の HP 行の次に `Move: STUNNED | follow-up: POUNCE_MOVE` のように短く表示する。
取得不能は `unknown` と `unreadable (項目: 理由)` を添える。
これは次行動の事前抽選ではない。公開 getter のみを読み、`RollMove`・`GetNextState`・
`PerformMove`・遷移メソッドを呼ばず、乱数・行動ロック・ゲームのログを変更しない。

根拠（v0.113.1、逆コンパイル `sts2-a671e9ef`）:

- `MonsterModel.NextMove/MoveStateMachine` は現在の参照を返す getter。
- `MoveState.Id/FollowUpState/FollowUpStateId` と `CanTransitionAway` は ID・参照・
  `MustPerformOnceBeforeTransitioning` と実行済みフラグを読む。`GetNextState` は上記と同じ
  follow-up 式を使うが、MOD は呼ばない。`MonsterMoveStateMachine.FindNextMoveState` は
  分岐を解決してログを追加するので、これも呼ばない。
- `Creature.StunInternal` は `STUNNED`、復帰先、実行までの遷移ロックを設定する。
  明示復帰先がなければ実際の `StateLog.Last().Id` を使う。
  `MonsterModel.SetMoveImmediate` → `MonsterMoveStateMachine.ForceCurrentState/SetCurrentState`
  はログへ追加しないため、スタンの ID・復帰先・ログ末尾は同一とは限らない。
- `LouseProgenitor.GenerateMoveStateMachine` は `WEB_CANNON_MOVE` → `CURL_AND_GROW_MOVE`
  → `POUNCE_MOVE` → WEB の参照を設定する。`Curled` は今回の公開対象に含めない。

#### 敵の行動の入力（schema 6）

敵の行動表（ソースの `GenerateMoveStateMachine` と各 Move から生成したもの。`cat12079801/sts2#1018`）を、
ゲームの現在の状態から確定的に評価するための入力。既存の項目の意味は変えていない（追加）が、
読み手がこの契約一式を必須として扱えるように `schema_version` を 6 に上げた。
すべてゲームスレッドで読むだけで、`GetNextState`・`RollMove`・`GenerateMoveStateMachine`・乱数を使う getter・
行動の delegate を呼ばず、状態を変えない。各項目は個別に成否を持ち、失敗した項目だけ `null` と
`unavailable_reasons.<key>`・warnings（`battle.enemies.<entity_id>.move_state.<key>` など）にする。
死亡中・復活待ちの敵の行にも同じ契約で出す。

**`move_state` の追加項目**（`readable` は従来どおり四値だけで決まり、追加項目の成否は `inputs_readable` にまとめる）:

```json
{
  "readable": true, "id": "STUNNED", "follow_up_id": "ROLL_OUT_MOVE",
  "last_logged_id": "SLEEP_MOVE", "can_transition_away": false,
  "has_follow_up": true,
  "must_perform_once": true,
  "current_state_id": "STUNNED",
  "current_state_registered": false,
  "initial_state_id": "SLEEP_MOVE",
  "performed_first_move": true,
  "spawned_this_turn": false,
  "state_log": [{"id": "SLEEP_MOVE", "is_move": true}, {"id": "SLEEP_MOVE", "is_move": true}],
  "is_stun": true,
  "stun_handler": "SlumberingBeetle.WakeUpMove",
  "inputs_readable": true
}
```

| 項目 | 読取元・意味 |
| --- | --- |
| `has_follow_up` | `NextMove.FollowUpState?.Id ?? NextMove.FollowUpStateId`（`MoveState.GetNextState` と同じ式）が null でないか。false は「次の行動が無い」終端の実値（`GasBomb.EXPLODE_MOVE`）。空文字列の follow-up（初期地点へ戻る）は true |
| `must_perform_once` | `NextMove.MustPerformOnceBeforeTransitioning`。true の間、`CanTransitionAway` は実行済みフラグを返す（スタン・`CeremonialBeast.STUN_MOVE` など） |
| `current_state_id` | private `MonsterMoveStateMachine._currentState.Id`。状態機械の現在地点で、分岐状態（`ConditionalBranchState` / `RandomBranchState`）のこともある。実行予定の `id`（`NextMove`）とは別に保持する |
| `current_state_registered` | 現在地点が `MonsterMoveStateMachine.States` に登録された状態そのものか（`States.TryGetValue(current_state_id)` で得た参照と `_currentState` が同じ参照）。false は `SetMoveImmediate` → `ForceCurrentState` で入った動的な状態（`STUNNED`、`IllusionPower` の `REVIVE_MOVE` など）か、登録外の分岐状態。同じ ID 文字列だけで登録済みの状態と同一視しないための値 |
| `initial_state_id` | private `MonsterMoveStateMachine._initialState.Id`。`FindNextMoveState` は空文字列の follow-up をここへの遷移として扱う。`Chomper._screamFirst`・`CorpseSlug._starterMoveIdx` などの初期地点の選択はこの値で読める |
| `performed_first_move` | private `MonsterMoveStateMachine._performedFirstMove`。`OnMovePerformed` で true。false の間、現在地点が `MoveState` なら `FindNextMoveState` は遷移しない（最初の行動の保持） |
| `spawned_this_turn` | `MonsterModel.SpawnedThisTurn`。`SetUpForCombat` で true、`OnSideSwitch` で false。`Creature.TakeTurn` は true の敵の行動を飛ばす |
| `state_log` | `MonsterMoveStateMachine.StateLog` の全要素を順序・重複のまま `{id, is_move}`（`MonsterState.Id` / `IsMove`）で。`RandomBranchState` の `UseOnlyOnce` は全ログ、`CannotRepeat` / `CanRepeatXTimes` は末尾の連続、cooldown は `is_move` の要素だけを末尾から数える。空配列 `[]` はログが本当に空（分岐状態から始まり未抽選）という実値で、`last_logged_id` の読取失敗（空）と区別する。要素が一つでも読めなければ全体を `null`（途中までの配列は返さない）。末尾が `last_logged_id` と一致しなければ `null` と理由 |
| `is_stun` | `NextMove` の private `MoveState._onPerform` が `CreatureCmd.Stun` のローカル関数 Wrapper（v0.113.1 では `CreatureCmd+<>c__DisplayClass30_0.<Stun>g__Wrapper\|0`）か。`Creature.StunInternal` が `STUNNED` に入れる delegate。false は「スタンではない」実値。v0.113.1 で `MonsterModel.stunnedMoveId`（`STUNNED`）を使うのは `StunInternal` だけなので、`id` が `STUNNED` なのに Wrapper と判定できない（Wrapper の名前・形が変わった版）とき、また Wrapper なのに `id` が `STUNNED` でないときは、false にせず `null` と理由（未観測） |
| `stun_handler` | `is_stun` のとき、Wrapper が捕捉した `stunMove`（宣言型と delegate 型を検査して読む。呼ばない）を許可リストの名前に対応付ける。`CreatureCmd.Stun.no_op`（引数なしの `CreatureCmd.Stun` の `_ => Task.CompletedTask`。宣言型が `CreatureCmd` の入れ子で IL が `call Task.get_CompletedTask; ret` だけであることを確かめる）、`BowlbugRock.DizzyMove`、`SlumberingBeetle.WakeUpMove`、`Tunneler.StillDizzyMove`、`CeremonialBeast.StunnedMove`、`LagavulinMatriarch.WakeUpMove`、`RavenousPower.StunnedMove`、`FlutterPower.StunnedMove`。Wrapper の `creature` がこの個体でない・敵のメソッドの Target がこの個体でない・パワーの処理の所有者（`PowerModel.Owner`）がこの個体でない・許可リストに無い delegate は `null` と理由（未観測）。`is_stun=false` なら理由なしの `null`。同じ復帰先（例: `ROLL_OUT_MOVE`）でも通常のスタンと起床はこの値で区別し、復帰 ID の一致では起床扱いにしない |
| `inputs_readable` | 上の項目がすべて読めたときだけ true |

`stun_handler` の許可リストは、v0.113.1 で `CreatureCmd.Stun(creature, stunMove, nextMoveId)` に処理を渡す呼び出しの全件
（`BowlbugRock.Stun`、`SlumberPower.AfterDamageReceived`、`BurrowedPower.AfterBlockBroken`、`PlowPower.AfterDamageReceived`、
`AsleepPower.AfterDamageReceived`、`RavenousPower.AfterDeath`、`FlutterPower.AfterDamageReceived`）。引数なしの呼び出し
（`Whistle`、`PlowPower` の他の敵、`ImbalancedPower`、`ShriekPower`）は `CreatureCmd.Stun.no_op`。

Markdown の行動の行は、スタン中は `| stun: ` の後に処理の名前、ログは `| log(5): … ` の後に末尾 4 件の ID と件数、
終端の行動は `(none: terminal move)` を足し、追加項目だけの失敗は `| inputs unreadable (項目: 理由)` と表示する。

**敵の行の `slot_name`:** `Creature.SlotName`（auto-property）の文字列。`null` はスロット無しの実値で、読取失敗は `null` と
行の `unavailable_reasons.slot_name`・warning `battle.enemies.<entity_id>.slot_name`。Markdown の HP 行に `| Slot: front`。
`Exoskeleton`・`Myte`・`PhantasmalGardener`・`Wriggler` の初期地点の条件分岐がこの値を読む。

**`battle.enemy_move_context`:**

```json
{
  "game_assembly_sha256": "a671e9efb86a7b80…",
  "game_mode": "Standard",
  "is_dread": false,
  "ascension_levels": ["SwarmingElites", "WearyTraveler", "…", "Scarcity"],
  "combat_player_count": 1,
  "has_encounter": true,
  "encounter_id": "NIBBITS_WEAK",
  "encounter_slots": [{"name": "front", "occupant_ids": ["NIBBIT_0"]}, {"name": "back", "occupant_ids": []}],
  "readable": true
}
```

| 項目 | 読取元・意味 |
| --- | --- |
| `game_assembly_sha256` | ロードした本体 DLL 全体の SHA-256（`player.generation_pool.game_assembly_sha256` と同じ値）。行動表の版との照合用 |
| `game_mode` | `CombatState.RunState.GameMode`（`Standard` / `Daily` / `Dread` / `Blitz`）。出典が戦闘の `RunState` なので、`RunManager.IsInProgress` に依らず読む（下の 3 項目と違い、ラン外でも null にしない） |
| `is_dread` | `RunManager.IsDread()`（`AscensionHelper.IsDread` の参照先） |
| `ascension_levels` | `AscensionLevel` の各値（`None` を除く）のうち `RunManager.HasAscension(level)` が true のものを列挙順で。`AscensionHelper.GetValueIfAscension` の参照先そのもの（Blitz の段階の対応 `GetLevelForBlitz` も含まれる）。`run.ascension` の数値から作らない |
| `combat_player_count` | `CombatState.Players.Count`。`KnowledgeDemon.PonderMove`・`WaterfallGiant.SiphonMove` などが掛ける人数 |
| `has_encounter` | `CombatState.Encounter` があるか |
| `encounter_id` | `Encounter.Id.Entry`。遭遇が無ければ理由なしの `null` |
| `encounter_slots` | `Encounter.Slots` の並び（`GetNextSlot` は先頭の空き、`Ovicopter.LayEggsMove`・`TwoTailedRat.CallForBackup` は末尾の空き）と、各スロットの `CombatState.Enemies` 全体（戦闘に残る死亡個体を含み、`battle.enemies` の絞り込みではない）の占有個体の `entity_id`。遭遇が無ければ `[]`。一人でも `SlotName` が読めなければ全体を `null` |
| `readable` | 上の項目がすべて読めたときだけ true |

`RunManager.IsInProgress` が false のとき、ゲームの getter は Ascension・Dread を「無し」として返すので、`is_dread`・
`ascension_levels`・`encounter_slots`（`TheKinBoss.Slots` は `IsDread` を読む）は読まずに `null` と理由にする。
Markdown は `## Enemies` の下に `Enemy move context: mode Standard | dread no | ascension … | encounter … | slots front=NIBBIT_0, back=empty` の 1 行。

**公開しないもの:** まだ表示されていない抽選の結果（乱数器・seed・抽選済みの次の分岐先）は出さない。ランダム分岐は
`state_log` と公開値から分布として扱う。各敵の private の現在値は [敵の戦闘内部状態](#敵の戦闘内部状態追加schema-4) の許可リストに出す。
儀式（`RITUAL_POWER`）の付与ターンの印は status 行の `was_just_applied_by_enemy`（[`power-internal-state.md`](power-internal-state.md)）。

契約試験は [`tests/EnemyMoveStateContract`](../tests/EnemyMoveStateContract/README.md)（`move_state`・スタンの処理）と
[`tests/EnemyCombatStateContract`](../tests/EnemyCombatStateContract/README.md)（`combat_state`・`slot_name`・`enemy_move_context`）。

#### 敵の攻撃の意図（追加、schema 4）

`battle.enemies[].intents[].attack` は、意図が `AttackIntent`（`SingleAttackIntent`・`MultiAttackIntent`・
`DeathBlowIntent`。`type` は `Attack` か `DeathBlow`）のときだけ出す。攻撃以外の意図・旧 MOD では省略（欠落は unknown）。
読むのは `MonsterModel.NextMove.Intents` の現在の意図そのもので、生存敵・戦闘に残る死亡敵、単独・協力プレイで共通。
追加契約なので版は上げていない（schema 4 のときの追加）。`label`・`description`・履歴・パワーから値を補完しない。

```json
{ "readable": true, "base_damage": "20", "hits": 1, "resolved_damage": 36 }
```

| 項目 | 読取元・意味 |
| --- | --- |
| `readable` | 三値すべてが取得できたときだけ true。失敗が一つでもあれば false |
| `base_damage` | `AttackIntent.DamageCalc.Invoke()` の decimal を `ToString(CultureInfo.InvariantCulture)` した 10 進文字列。`Hook.ModifyDamage`（筋力・脆弱・脱力など）を通す前の 1 ヒットの素値。丸めず、scale もそのまま。`DamageCalc` が null・例外なら null |
| `hits` | `AttackIntent.Repeats` の int。`SingleAttackIntent`（`DeathBlowIntent` を含む）は 1、`MultiAttackIntent` は `_repeatCalc?.Invoke() ?? _repeat`（`TestSubject` の `MULTI_CLAW_MOVE` のように現在値で変わる）。0 も実値。負・例外なら null |
| `resolved_damage` | `AttackIntent.GetSingleDamage(PlayerCreatures, creature)` の int。`label` と hover の数値と同じ経路で、ローカルのプレイヤー（`LocalContext.GetMe(creature.CombatState)`）が対象のときの 1 ヒットの解決値。協力プレイでも他のプレイヤーへの値ではない。ローカルのプレイヤーが居ないとき、ゲームは補正前の値を返すので、MOD は同じ判定を先に行って null にする |

`resolved_damage` の計算（v0.113.1）: `GetSingleDamage` は `DamageCalc()` を
`Hook.ModifyDamage(..., target: 自分, dealer: 敵, ValueProp.Move, ModifyDamageHookType.All)` に通し、
`Math.Max(0, (int)num)` で int にする（小数は 0 方向へ切り捨て）。`Hook.ModifyDamageInternal` の順序は
加算（`StrengthPower.ModifyDamageAdditive` など）→ 倍率（`VulnerablePower` / `WeakPower.ModifyDamageMultiplicative` など）
→ 後段の倍率 → 上限。たとえば素 20・敵の筋力 4・自分が脆弱なら (20+4)×1.5=36、筋力 6 なら 39。
敵が脱力なら (20+4)×0.75=18、筋力 6 なら 19.5→19。素 21・筋力 4・脱力でも 18 になるので、`label` から素値は一意に戻らない。

読取は副作用を持たない。`DamageCalc` はモンスターが行動の定義で渡した delegate で、v0.113.1 では
定数を返す closure（`SingleAttackIntent(int)` / `MultiAttackIntent(int, …)`）か、フィールド・パワー量・アセンションを読む getter
（`WaterfallGiant.CurrentPressureGunDamage` / `SteamEruptionDamage`、`TheForgotten.DreadDamage`、`GasBomb.ExplodeDamage`）だけ。
`GetSingleDamage` はゲームの意図の表示（`GetIntentLabel`・`GetHoverTip`・`GetTexture`）が毎回呼ぶのと同じ読取で、
`Hook.ModifyDamage` は各モデルの `ModifyDamage*` を集計するだけ。乱数・行動の抽選・状態の書き込みは行わない。

読めない値は該当項目だけ `null` とし、`attack.unavailable_reasons.<key>` に `ExceptionType: message`、
トップレベルの `warnings` に `battle.enemies.<entity_id>.intents.<意図の位置>.attack.<key>` を出す。取得できた項目は保持する。
`unavailable_reasons` は失敗がなければ省略する。

```json
{
  "readable": false,
  "base_damage": "20",
  "hits": 1,
  "resolved_damage": null,
  "unavailable_reasons": {
    "resolved_damage": "InvalidOperationException: ローカルのプレイヤーがいません（LocalContext.GetMe が null）"
  }
}
```

Markdown は意図の行の `label` の後に `[attack: base 20 | hits 1 | resolved 36]` を足す。
取得不能の項目は `null (理由)` と表示する。

根拠（v0.113.1、逆コンパイル `sts2-a671e9ef`）: `AttackIntent.DamageCalc` / `Repeats` / `GetSingleDamage`、
`SingleAttackIntent` / `MultiAttackIntent` / `DeathBlowIntent` のコンストラクタと `Repeats`・`GetIntentLabel`、
`Hook.ModifyDamage` / `ModifyDamageInternal`、`StrengthPower.ModifyDamageAdditive`、
`VulnerablePower` / `WeakPower.ModifyDamageMultiplicative`、`LocalContext.GetMe(ICombatState?)`。

#### 敵の戦闘内部状態（追加、schema 4）

`battle.enemies[].combat_state` は許可リストの敵にだけ出す。滝の巨人（`WaterfallGiant`、schema 4 のときの追加）と、
schema 6 で足した行動の入力の敵（下の表）と、schema 1.1.0 で足した台詞の印の敵（傭兵グレムリン・キンの司祭）。他の敵・旧 MOD では省略する（欠落は unknown）。生存敵・戦闘に残る死亡中の敵、単独・協力プレイで共通。
宣言型 `WaterfallGiant` の private フィールドと、副作用の無い getter を宣言型・型を検査して読むだけで、
move の実行・初期化・乱数・履歴の集計はしない。HP・行動 ID・パワー・Ascension の値から作らない。
すべて非負の int（`is_about_to_blow` だけ bool）で、false・0 も実値。

```json
{
  "is_about_to_blow": false, "steam_eruption_damage": 0,
  "current_pressure_gun_damage": 20, "steam_boost_amount": 3, "pressurize_amount": 15,
  "stomp_damage": 15, "ram_damage": 10, "pressure_up_damage": 13, "siphon_heal": 10
}
```

| 項目 | 読取元・意味 |
| --- | --- |
| `is_about_to_blow` | private bool `WaterfallGiant._isAboutToBlow`。`SteamEruptionPower.AfterDeath`（所有者の死亡、`wasRemovalPrevented` でないとき）が呼ぶ `TriggerAboutToBlowState` で true。最大 HP・HP を 999999999 にして `ABOUT_TO_BLOW_MOVE` へ強制遷移する。false に戻す処理は無い。false も実値 |
| `steam_eruption_damage` | private int `WaterfallGiant._steamEruptionDamage`。`AboutToBlowMove` がそのときの `SteamEruptionPower` の量を写してパワーを外し、`EXPLODE_MOVE` の `DeathBlowIntent` と `ExplodeMove` の攻撃（攻撃後に自身を倒す）に使う。写す前は 0 の実値 |
| `current_pressure_gun_damage` | private int `WaterfallGiant._currentPressureGunDamage`（`CurrentPressureGunDamage` の実体）。`AfterAddedToRoom` が `BasePressureGunDamage`（DeadlyEnemies で 23、無しで 20）を入れ、`PressureGunMove` が攻撃の後に `PressureGunIncrease`（5）を足す。次の `PRESSURE_GUN_MOVE` の攻撃の素値。戦闘の準備中（`AfterAddedToRoom` の前。`is_resolving` の間）は 0 の実値 |
| `steam_boost_amount` | private getter `WaterfallGiant.SteamBoostAmount`（`AscensionHelper.IsDread()` で 4、それ以外 3）。STOMP・RAM・SIPHON・PRESSURE_GUN・PRESSURE_UP の各行動の最後に `SteamEruptionPower` へ足す量 |
| `pressurize_amount` | private getter `WaterfallGiant.PressurizeAmount`（DeadlyEnemies で 20、無しで 15）。`PressurizeMove` が付ける `SteamEruptionPower` の量 |
| `stomp_damage` | private getter `WaterfallGiant.StompDamage`（16 / 15）。`StompMove` の攻撃の素値 |
| `ram_damage` | private getter `WaterfallGiant.RamDamage`（11 / 10）。`RamMove` の攻撃の素値 |
| `pressure_up_damage` | private getter `WaterfallGiant.PressureUpDamage`（14 / 13）。`PressureUpMove` の攻撃の素値 |
| `siphon_heal` | public getter `WaterfallGiant.SiphonHeal`（ToughEnemies で 15、無しで 10）。`SiphonMove` はこれに `CombatState.Players.Count` を掛けて回復する。ここは掛ける前の 1 人分 |

getter は `AscensionHelper.GetValueIfAscension` / `IsDread` を経て `RunManager.HasAscension`（`AscensionManager.HasLevel`、
`List.Contains`）・`RunManager.IsDread`（`RunState.GameMode`）を読むだけで、状態を変えない。
`RunManager.IsInProgress` が false のとき getter は Ascension・Dread を「無し」として返すので、そのときは getter を呼ばず、
getter の 6 項目を `null`（理由付き）にする。括弧内の値は v0.113.1 のソースの分岐で、MOD はこれを使わず getter の現在値を出す。

読取失敗は該当項目だけ `null` とし、`combat_state.unavailable_reasons.<key>` に理由、
トップレベルの `warnings` に `battle.enemies.<entity_id>.combat_state.<key>` を出す。0 / false で補わない。
負の値は該当項目を `null`・理由にする（0 に丸めない）。
Markdown は行動の行の次に `Combat state: is about to blow: no | steam eruption damage: 0 | current pressure gun damage: 20 | …` と表示し、
失敗は `null (理由)` と表示する。

**行動の入力の敵（schema 6、`cat12079801/sts2#1018`）:** 行動の分岐・効果量に効く現在値のうち、ほかの state（HP・status・
`move_state`・`enemy_move_context`）に出ないもの。宣言型・型を検査した private フィールド（Axebot だけ public getter）をそのまま読む。
false・0・負の値も実値（`turns_until_summonable` は行動のたびに減り、負になりうる）。

| 敵 | 項目 | 読取元・使われ方 |
| --- | --- | --- |
| `Aeonglass` | `additional_strength`・`wither_upgrade_count` | `_additionalStrength`（INCREASING_INTENSITY の筋力 = `IncreasingIntensityBaseStrength` + この値。実行のたびに +1）・`_witherUpgradeCount`（実行のたびに +1。`AfterCardGeneratedForCombat` が生成した Wither をこの回数だけ `FakeUpgrade`） |
| `Axebot` | `stock_amount` | getter `StockAmount`（`_stockOverrideAmount ?? 2`）。BOOT_UP の筋力は `BootUpStrGain × (2 − StockAmount)` |
| `BowlbugRock` | `is_off_balance` | `_isOffBalance`。POST_HEADBUTT の条件分岐と、HEADBUTT の攻撃の後のスタン（`DizzyMove` が false に戻す） |
| `Fabricator` | `last_spawned_id`・`last_spawned_readable` | `_lastSpawned`（`MonsterModel?`）の `Id.Entry`。`SpawnBot` は候補から直前と同じモデルを除いて抽選する。読取に成功した null は「まだ召喚なし」（`last_spawned_readable=true`・`last_spawned_id=null`、Markdown は `none`）、読取失敗は `last_spawned_readable=false`・null・理由。同種の別個体や召喚履歴から補わない |
| `FrogKnight` | `has_beetle_charged` | `_hasBeetleCharged`。条件分岐 `HasBeetleCharged \|\| CurrentHp >= MaxHp / 2` |
| `KnowledgeDemon` | `curse_of_knowledge_counter` | `_curseOfKnowledgeCounter`。条件分岐（3 未満なら CURSE_OF_KNOWLEDGE）と、`ChooseCurse` の呪いの組・`DisintegrationDamageValues` の添字 |
| `LivingFog` | `bloat_amount` | `_bloatAmount`。BLOAT が GasBomb を足す回数（空きスロットがある限り） |
| `Nibbit` | `is_alone`・`is_front` | `_isAlone`・`_isFront`。初期編成で入る値で、INIT_MOVE の条件分岐が読む。生存数から遡って決めない |
| `Queen` | `has_amalgam_died` | `_hasAmalgamDied`。条件分岐（`AfterDeath` で TorchHeadAmalgam が死ぬと true） |
| `TestSubject` | `respawns`・`extra_multi_claw_count` | `_respawns`（条件分岐 `Respawns < 2` と RESPAWN の段階）・`_extraMultiClawCount`（MULTI_CLAW の回数 = `BaseMultiClawCount` + この値。実行のたびに +1） |
| `Toadpole` | `is_front` | `_isFront`。初期編成で入る値で、INIT_MOVE の条件分岐が読む |
| `TwoTailedRat` | `turns_until_summonable`・`call_for_backup_count` | `_turnsUntilSummonable`・`_callForBackupCount`。`CanSummon`（前者が正、後者が 3 以上なら召喚不可）が分岐の重みを決める |

表示・効果音・アニメーションだけに使うフィールド（`LouseProgenitor._curled`、`CubexConstruct._isBurrowed` など）と、
初期地点の選択だけに使うフィールド（`move_state.initial_state_id` で読める）は載せない。

#### 敵の台詞の印（schema 1.1.0）

`cat12079801/sts2#1065`。行動の本体の条件分岐が読む台詞の印。分岐の先は台詞（`TalkCmd`。傭兵グレムリンの Blitz 以外の側は演出の待ちも）だけで、
攻撃・盗み・付与の量と行動の遷移は変わらないが、ソースから生成した行動表の条件を未観測のまま残さないために出す。
宣言型・型を検査した private bool フィールドを読むだけで、`AssertMutable` の setter を持つ property は呼ばない。
false も実値（まだ話していない）。行動ログ・`move_state.state_log` から作らない。読取失敗は上と同じく該当項目を `null`・理由・warnings にし、false で補わない。

| 敵 | 項目 | 読取元・使われ方 |
| --- | --- | --- |
| `GremlinMerc` | `has_spoken` | private bool `_hasSpoken`（`HasSpoken` の実体）。`GimmeMove` の最初と最後の条件分岐（`!HasSpoken && GameMode != Blitz` で攻撃の前、`== Blitz` で `Steal` の後）が true にして台詞を出す。初期値 false、戦闘中に false へ戻す処理は無い。`GameMode` は `battle.enemy_move_context.game_mode`、`Steal` の入力は泥棒（`THIEVERY_POWER`）の status 行（`amount`・`stolen_gold`・`target_player_id`）と player の所持金 |
| `KinPriest` | `speech_used` | private bool `_speechUsed`（`SpeechUsed` の実体）。`RitualMove` の条件分岐 `!SpeechUsed` が true にして台詞を出す。筋力の付与は分岐の外。初期値 false、戻す処理は無い |

Markdown は `Combat state: has spoken: no` の形（読取失敗は `null (理由)`）。

### `hand_select` — In-Combat Card Selection

Appears when a card effect prompts "Select a card to exhaust/discard/upgrade". **Not** an overlay — happens within the combat hand.

```jsonc
{
  "state_type": "hand_select",
  "hand_select": {
    "mode": "simple_select",     // "simple_select" or "upgrade_select"
    "prompt": "Select a card to Exhaust.",
    "peeking": false,            // schema 10: PeekButton.IsPeeking (null if unreadable)
    "cards": [
      {
        "index": 0,              // schema 10: position in NPlayerHand.ActiveHolders (see below)
        "uid": "STRIKE_R#0",     // schema 10: same uid as the card's player.hand row
        "enabled": true,         // schema 10: false while peeking (the game ignores presses)
        "id": "STRIKE_R",
        "name": "Strike",
        "type": "Attack",
        "cost": "1",
        "star_cost": null,       // Regent star cost as string, null if N/A
        "description": "Deal 6 damage.",
        "is_upgraded": false,
        "keywords": [ /* Keyword Objects */ ],
        "combat_instance": { "source": "pile", "pile": "Hand", "uid": "STRIKE_R#0", "unavailable_reasons": {} } // schema 7
      }
    ],
    "selected_cards": [          // schema 10: always present; [] = none selected, null = unreadable
      { "index": 0, "id": "DEFEND_R", "name": "Defend", "uid": "DEFEND_R#4" }
    ],
    "min_select": 1,             // schema 10: NPlayerHand._prefs.MinSelect (null if unreadable)
    "max_select": 1,             // schema 10: NPlayerHand._prefs.MaxSelect (null if unreadable)
    "can_confirm": false
  },
  "battle": { ... },  // Full battle state included for context
  "run": { ... },
  "player": { ... }
}
```

#### 手札選択の候補と uid（schema 10）

`cards[]` は `NPlayerHand.ActiveHolders`（見えている holder。選択の絞り込みに通らない札は隠れるので含まれない）の順で、
`index` はその **holder の位置**（`combat_select_card` が押す位置と同じ）。`CardModel` の無い holder は行を出さず
（`warnings` に `hand_select.cards[<index>]`）、後ろの行の index を詰めない。`uid` は `NPlayerHand.ActiveHolders[i].CardModel` を
`player.hand[]` と同じ registry（`GetStableCardUid`）に渡した値で、戦闘の state が読めないときは null。
`enabled` は押下が効くか。`NPlayerHand.OnHolderPressed` が押下を無視する条件のどれかに当たれば false: 覗き見中
（`PeekButton.IsPeeking`。このあいだは絞り込みも外れて全札が見える）、オーバーレイが積まれている（`NOverlayStack.Instance.ScreenCount > 0`）、
戦闘が進行中でない（`CombatManager.Instance.IsInProgress` が false）。どれかの値が読めないときも false。
holder の `CardNode` が無い条件は、その holder に行が無いことで表す（`NCardHolder.CardModel` は `CardNode?.Model`）。
`combat_select_card` は同じ条件のとき、`expected_uid` の有無によらず押さずに理由付きの error を返す。`player.hand[]` とは配列の位置・名前で対応付けず、`uid` で対応付ける。

`selected_cards` は `NPlayerHand._selectedCards`（確定でゲームが返すモデルの列。強化モードの preview の札も含む）を選んだ順に
`{index, id, name, uid}` で返す。読めた空は `[]`、読めないときは `null` と `selected_cards_unavailable_reason`・`warnings`。
選択済みの並びは照合に使わず、uid の集合で比べる。`min_select` / `max_select` は `NPlayerHand._prefs`（`CardSelectorPrefs`）で、
ゲームは選択数がこの範囲にあるときだけ確定ボタンを有効にする（`RefreshSelectModeConfirmButton`）。読めないときは null と
`select_range_unavailable_reason`。`can_confirm` は `%SelectModeConfirmButton` が有効かつ表示中で、`combat_confirm_selection` も同じ判定で押す。

出典（v0.113.1、`sts2-a671e9ef`）: `NPlayerHand.ActiveHolders` / `_selectedCards` / `_prefs` / `PeekButton` /
`OnHolderPressed`（覗き見中・オーバーレイあり・戦闘外・CardNode なしは押下を無視）/ `UpdateSelectModeCardVisibility`（覗き見中以外は絞り込みで holder を隠す）/
`SelectCardInSimpleMode` / `SelectCardInUpgradeMode` / `RefreshSelectModeConfirmButton`。

### `rewards` — Rewards Screen

Appears after combat ends or when triggered by events (e.g. TheFutureOfPotions, Draft).

```jsonc
{
  "state_type": "rewards",
  "rewards": {
    "items": [
      {
        "index": 0,
        "type": "gold",              // gold, potion, relic, card, special_card, card_removal
        "description": "Obtain 25 gold.",
        "gold_amount": 25            // Only for gold rewards
      },
      {
        "index": 1,
        "type": "potion",
        "description": "Obtain a potion.",
        "potion_id": "SWIFT_POTION", // Only for potion rewards
        "potion_name": "Swift Potion"
      },
      {
        "index": 2,
        "type": "card",
        "description": "Add a card to your deck."
        // Card rewards open the card_reward screen when claimed
      }
    ],
    "can_proceed": true
  },
  "run": { ... },
  "player": { ... }
}
```

### 報酬 ID と選択状態（加算契約、schema 3）

`rewards.items` の既存キーと有効ボタンだけを列挙する挙動は維持する。
各 item に `reward_id` / `selection_state` / `claimable` / `skipped` /
`reward_consumed` / `last_selection` / `last_option_id` を追加する。
`rewards.screen_revision` は現在の一覧と選択状態を表す opaque な文字列。
`rewards.selection_history` は同じセッションで観測した報酬の ID と上記状態を保持し、
取得後に `items` から消えた報酬も `claimed` として確認できる。履歴に報酬の中身は含めない。
`claimable` は現在の有効ボタンの有無であり、ポーション枠など取得条件の保証ではない。

`reward_id` はプロセスごとのランダム epoch と単調番号から生成する。
シード・抽選・メモリアドレスとは無関係で、同じ画面の同じ Reward オブジェクトに対して固定。
他の報酬取得で index が詰まっても残存 ID は変わらない。
報酬画面の終了、マップの開封、別の報酬画面、ランの変更で登録を破棄し、再起動でも別 epoch となる。
一時的なカード三択・デッキ選択・一時停止の表示だけでは親セッションを破棄しない。

| selection_state | 意味 |
|---|---|
| `unopened` | 画面生成から追跡できた未開封の報酬 |
| `opened` | 開封・選択中。操作送信だけで取得済みにはしない |
| `skipped` | Skip の選択結果と親の RewardSkipped 通知を観測。親は未消費で再開封可能 |
| `claimed` | 親の RewardClaimed 通知を観測。カード取得と alternative による完了は last_selection で区別 |
| `unknown` | 観測欠落、中断、選択失敗等。未閲覧／完了へ決め打ちしない |

`skipped` は selection_state が `skipped` のとき true。
`reward_consumed` は `claimed` で true、`skipped` で false、それ以外は null（または欠落）。
`last_selection` は `card` / `skip` / `alternative` / `direct` / `interrupted` / `unknown` 等。
`last_option_id` に `Skip` / `SACRIFICE` / `REROLL` 等の表示中の option ID が入る。
REROLL は同じ親のまま `opened` に戻り、SACRIFICE は親の完了通知が来るまでは完了にしない。
手動の再開封は許可し、再開封すると `opened` になる。GET は Skip 状態を消去せず、報酬を選択／完了しない。

`card_reward.parent_reward_id` は対応する親 ID（親を特定できない直接表示では null／欠落）。
`card_reward.selection_state` も追加する。対応が不明なら `unknown`。
未開封カードの中身、次の報酬、生成候補・抽選結果の先読みは追加しない。
Markdown にも ID・revision・選択状態・親 ID を表示する。新項目のない入力も表示できる。

### `card_reward` — Card Reward Selection

Pick one card to add to your deck. Appears after claiming a card reward, or directly during events (e.g. Draft modifier at Neow).

```jsonc
{
  "state_type": "card_reward",
  "card_reward": {
    "cards": [
      {
        "index": 0,
        "id": "UPPERCUT",
        "name": "Uppercut",
        "type": "Attack",
        "cost": "2",
        "star_cost": null,
        "description": "Deal 13 damage. Apply 1 Weak. Apply 1 Vulnerable.",
        "rarity": "Uncommon",
        "is_upgraded": false,
        "keywords": [ /* Keyword Objects */ ]
        // 戦闘中に開いた報酬だけ "combat_instance"（schema 7。選択候補の個体）
      }
    ],
    "can_skip": true
  },
  "run": { ... },
  "player": { ... }
}
```

### `map` — Map Navigation

```jsonc
{
  "state_type": "map",
  "map": {
    "current_position": {
      "col": 3, "row": 2, "type": "Monster"
    },
    "visited": [
      { "col": 3, "row": 0, "type": "Start" },
      { "col": 3, "row": 1, "type": "Monster" },
      { "col": 3, "row": 2, "type": "Monster" }
    ],
    "travel_enabled": true,      // NMapScreen.IsTravelEnabled. false: choose_map_node is refused
    "next_options": [
      {
        "index": 0,
        "col": 2, "row": 3,
        "type": "RestSite",
        "leads_to": [            // 1-level lookahead (children)
          { "col": 1, "row": 4, "type": "Elite" },
          { "col": 3, "row": 4, "type": "Shop" }
        ]
      }
    ],
    "nodes": [                   // Full map DAG
      {
        "col": 3, "row": 0,
        "type": "Start",
        "children": [ [2, 1], [3, 1], [4, 1] ]  // [col, row] pairs
      }
    ],
    "boss": {
      "col": 3,
      "row": 15,
      "id": "ENCOUNTER.VANTOM_BOSS",
      "name": "Vantom"
    },
    "bosses": [
      { "col": 3, "row": 15, "id": "ENCOUNTER.VANTOM_BOSS", "name": "Vantom" }
    ],
    // What this act has ALREADY served. There is no "next encounter" field: the game does
    // not show that before the room is entered. Omitted entirely before the first room.
    "encounters_seen": {
      "act": 1,
      "monster": [
        { "floor": 2, "id": "NIBBITS_WEAK", "name": "Nibbits", "monsters": ["Nibbit", "Nibbit"] }
      ],
      "elite": [
        { "floor": 8, "id": "BYRDONIS_ELITE", "name": "Byrdonis", "monsters": ["Byrdonis"] }
      ],
      "event": [
        { "floor": 1, "id": "NEOW", "name": "Neow" }
      ]
      // "elite_cycle": 1   // only once the act's elite pool has wrapped at least once
    }
  },
  "run": { ... },
  "player": { ... }
}
```

### `event` — Event / Ancient Encounter

```jsonc
{
  "state_type": "event",
  "event": {
    "event_id": "NEOW",
    "event_name": "Neow",
    "is_ancient": true,
    "in_dialogue": false,        // true = click to advance dialogue first
    "body": "Event description text...",
    "options": [
      {
        "index": 0,
        "title": "Draft",
        "description": "Choose 10 card rewards to replace your starting deck.",
        "is_locked": false,
        "is_proceed": false,
        "was_chosen": false,
        "relic_name": "Relic Name",         // Only if option has a relic
        "relic_description": "Relic desc.",  // Only if option has a relic
        "keywords": [ /* Keyword Objects */ ]
      }
    ]
  },
  "run": { ... },
  "player": { ... }
}
```

### `rest_site` — Rest Site

```jsonc
{
  "state_type": "rest_site",
  "rest_site": {
    "options": [
      {
        "index": 0,
        "id": "HEAL",
        "name": "Rest",
        "description": "Heal 30% of max HP.",
        "is_enabled": true,
        "has_button": true   // false = the option exists in the run model but no
                             // button is drawn; choose_rest_option refuses it
      }
    ],
    "can_proceed": false
  },
  "run": { ... },
  "player": { ... }
}
```

### `shop` — Shop

Shop inventory is auto-opened when state is queried.

```jsonc
{
  "state_type": "shop",
  "shop": {
    "items": [
      // Card entry
      {
        "index": 0,
        "category": "card",
        "price": 75,               // Gold price in the shop
        "is_stocked": true,
        "can_afford": true,
        "on_sale": false,
        "card_id": "OFFERING",
        "card_name": "Offering",
        "card_type": "Skill",
        "card_cost": "1",          // Energy cost as string ("X" for X-cost)
        "card_star_cost": null,    // Regent star cost as string, null if N/A
        "card_rarity": "Rare",
        "card_description": "Lose 6 HP. Gain 2 Energy. Draw 3 cards.",
        "keywords": [ /* Keyword Objects */ ]
      },
      // Relic entry
      {
        "index": 5,
        "category": "relic",
        "price": 150,
        "is_stocked": true,
        "can_afford": false,
        "relic_id": "VAJRA",
        "relic_name": "Vajra",
        "relic_description": "At the start of each combat, gain 1 Strength.",
        "keywords": [ /* Keyword Objects */ ]
      },
      // Potion entry
      {
        "index": 8,
        "category": "potion",
        "price": 50,
        "is_stocked": true,
        "can_afford": true,
        "potion_id": "FIRE_POTION",
        "potion_name": "Fire Potion",
        "potion_description": "Deal 20 damage to target enemy.",
        "keywords": [ /* Keyword Objects */ ]
      },
      // Card removal entry
      {
        "index": 10,
        "category": "card_removal",
        "price": 75,
        "is_stocked": true,
        "can_afford": true
      }
    ],
    "can_proceed": true,
    "error": "..."             // Only present if inventory isn't ready; retry in a moment
  },
  "run": { ... },
  "player": { ... }
}
```

### `fake_merchant` — Fake Merchant Event

A relic-only shop disguised as an event. Uses `shop_purchase` and `proceed` actions (same as regular shop). If the player triggers a fight, the merchant disappears.

```jsonc
{
  "state_type": "fake_merchant",
  "fake_merchant": {
    "event_id": "FAKE_MERCHANT",
    "event_name": "Fake Merchant",
    "started_fight": false,         // true after triggering the foul potion fight
    "shop": {
      "items": [
        {
          "index": 0,
          "category": "relic",
          "cost": 150,
          "is_stocked": true,
          "can_afford": true,
          "relic_id": "VAJRA",
          "relic_name": "Vajra",
          "relic_description": "At the start of each combat, gain 1 Strength.",
          "keywords": [ /* Keyword Objects */ ]
        }
      ],
      "can_proceed": true
    },
    // After fight:
    // "started_fight": true,
    // "shop": { "items": [], "can_proceed": true },
    // "message": "The fake merchant has been defeated. Proceed to map."
  },
  "run": { ... },
  "player": { ... }
}
```

**Note:** The fake merchant only sells relics — no cards, potions, or card removal. The `shop_purchase` action works the same as for a regular shop.

### `treasure` — Treasure Room

Chest is auto-opened on first state query.

```jsonc
{
  "state_type": "treasure",
  "treasure": {
    "message": "Opening chest...",  // Transitional state; query again
    // OR, once opened:
    "relics": [
      {
        "index": 0,
        "id": "LANTERN",
        "name": "Lantern",
        "description": "Gain 1 Energy on the first turn of each combat.",
        "rarity": "Uncommon",
        "keywords": [ /* Keyword Objects */ ]
      }
    ],
    "can_proceed": true
  },
  "run": { ... },
  "player": { ... }
}
```

### `card_select` — Card Selection Overlay

Covers deck transforms, upgrades, enchants, removals, and choose-a-card effects. Appears on top of any room.

For grid screens, `cards` contains **all candidates** after the game's filtering and sorting,
in the full grid's display order (top to bottom, left to right), with the draw-pile tie normalization below. The zero-based `index` addresses
that full list, including cards without a generated holder; no scrolling is required. Scrolling
or hovering does not change indices. Re-read state after candidates change, are reordered, or the
screen changes; an index is not a persistent card identifier. Same-ID instances stay separate.
`cards: []` means no candidates. `cards: null` with a `card_select.cards` warning means the candidate
list could not be read; do not select using a partial or stale list. Schema 4 removes
`selected_outside_grid`: selection marks now cover the full list. A mismatch with `selected_count`
produces a `card_select.selected` warning without rewriting the game's selection.
Combat-pile screens retain their game class name as `screen_type` (e.g. `NCombatPileCardSelectScreen`).

#### 戦闘中の選択画面の battle（schema 1.4.0）

戦闘中（現在の部屋が `CombatRoom` で `CombatManager.IsInProgress`）に出る**選択画面の state すべて**に、戦闘の画面
（`monster` / `elite` / `boss`）と同じ作り方の `battle`（単独は `BuildBattleState`、マルチは `BuildMultiplayerBattleState`）を付ける。
選択画面は `state_type` が `_select` で終わるもの（`hand_select`、grid と choose-a-card の `card_select`、`bundle_select`、
`relic_select`）。画面のクラスでなく `state_type` で決めるので、計略（Stratagem）のシャッフル後の選択・未来視の雫などの
`NCombatPileCardSelectScreen`（`screen_type` はクラス名のまま）も、今後 `_select` の state に入る画面も同じく付く。

- `state_type` は変えない。`player` は従来どおり戦闘中なら 4 つの山・手札・エナジー・status を持つ（`battle` の有無によらない）。
- `battle` を作れなかったときは warning `battle.selection` を出して `battle` を省き、選択画面の残りの state はそのまま返す。
- `battle` の中身と隠す情報は戦闘の画面と同じ（山札の順は出さない）。手番の初めのドローの途中で開いた画面では
  `is_play_phase` が false のことがある。
- 戦闘の前後（`IsInProgress` が false）・戦闘外の部屋の選択画面、選択画面でない state（`map`・`menu`・`game_over`・
  `card_reward`・`rewards`・未対応の `overlay`）には付けない。
- Markdown は `battle` があると戦闘の表示（敵・手札・山）を出し、トップレベルの player の要約を省く（戦闘の画面と同じ）。

出典（v0.113.1、`sts2-a671e9ef`）: `NCombatPileCardSelectScreen : NCardGridSelectionScreen`。
[SelectionBattleContract](../tests/SelectionBattleContract/README.md)

#### 選択を開いた元（`choice_source`、schema 1.5.0）

戦闘中（`CombatManager.IsInProgress`）に `CardSelectCmd` が開く選択画面の state に、その選択を開いた元と、
選択の後に続く処理の手がかりを出す（`cat12079801/sts2#1084`）。付くのは `hand_select`（`FromHand` /
`FromHandForDiscard` / `FromHandForUpgrade`）、choose-a-card の `card_select`（`FromChooseACardScreen`）、grid の
`card_select` のうち `NCombatPileCardSelectScreen`（`FromCombatPile`）と `NSimpleCardSelectScreen`（`FromSimpleGrid` /
`FromSimpleGridForRewards`）。戦闘外の画面・それ以外の画面には付けない。

MOD は `CardSelectCmd` の上の 8 つの入口（`PlayerChoiceContext` を取るもの。`FromCombatPile` は 2 つの多重定義とも）に
読むだけの Harmony の prefix・postfix を当て、呼ばれたときの引数（`context`・`player`・`FromHand` の `source`・
choose / grid の札・`FromCombatPile` の `pile`）を記録し、戻り値の Task が終わったら記録を消す。state を作るときは、
ローカル player・同じ戦闘・その画面を出す入口のうち、開いている最も新しい記録を使う（choose は画面の札が記録の札と
同じ実体・同じ順のときだけ）。記録は、別の戦闘で選択が開いたときと、state を作るときに戦闘が終わっている
（またはいまの戦闘と違う）ときに捨てる（選択の途中で保存して終了すると Task が終わらないため）。戦闘外の記録は残す。

```jsonc
"choice_source": {
  "version": 1,
  "known": true,
  "selector": "FromCombatPile",                 // 選択を開いた CardSelectCmd のメソッド（同じ選択の一番内側）
  "outer_selectors": [],                        // 同じ context で外側から転送した入口（例: ["FromHandForDiscard"]）
  "chooser_player_id": "1",
  "hand_source": null,                          // FromHand* の source 引数（例: EntropyPower）の行
  "contexts": [                                 // PlayerChoiceContext の連鎖。0 が CardSelectCmd に渡された context
    { "index": 0, "owner_player_id": "1", "kind": "branching", "source": { "kind": "card", "uid": "POMMEL_STRIKE#4", "pile": "Play", "model_type": "PommelStrike", "model_id": "POMMEL_STRIKE" }, "branched": false },
    { "index": 1, "owner_player_id": "1", "kind": "game_action",
      "action": { "type": "PlayCardAction", "id": 12, "owner_player_id": "1", "state": "GatheringPlayerChoice", "card_uid": "POMMEL_STRIKE#4", "target_combat_id": 3 } }
  ],
  "models": [                                   // 各 context の ModelStack を上から（context 0 から順に）
    { "kind": "power", "model_type": "StratagemPower", "model_id": "STRATAGEM_POWER", "owner_combat_id": 1, "owner_player_id": "1", "context_index": 0 },
    { "kind": "card", "model_type": "PommelStrike", "model_id": "POMMEL_STRIKE", "uid": "POMMEL_STRIKE#4", "pile": "Play", "context_index": 0 },
    { "kind": "card", "model_type": "PommelStrike", "model_id": "POMMEL_STRIKE", "uid": "POMMEL_STRIKE#4", "pile": "Play", "context_index": 1 }
  ],
  "card_play": { "phase": "started", "uid": "POMMEL_STRIKE#4", "is_auto_play": false, "target": { "combat_id": 3, "entity_id": "NIBBIT_0" },
                 "play_index": 0, "play_count": 1, "model_index": 1, "player_id": "1" },
  "hook": { "model_index": 0, "declared": ["AfterShuffle"], "name": "AfterShuffle", "name_unavailable_reason": null, "scope": "combat",
            "remaining_listeners": [ { "order": 3, "model_type": "TheAbacus", "model_id": "THE_ABACUS", "declared_by": "TheAbacus", "kind": "relic", "owner_player_id": "1" } ] }
}
```

- `selector` / `outer_selectors`: 外側の入口が同じ context で内側の入口に転送するとき（`FromHandForDiscard` →
  `FromHand`、`FromCombatPile` の 4 引数 → 5 引数）、`selector` は内側（最も新しい記録）、`outer_selectors` は同じ
  context・同じ player で開いている外側の入口（古い順）。捨てる選択は `selector: "FromHand"`・
  `outer_selectors: ["FromHandForDiscard"]` になる。
- `contexts`: `BranchingPlayerChoiceContext`（`kind: "branching"`。`Source`、別の chooser の context に分岐したかの
  `branched`、次は private の `_originalContext`）、`HookPlayerChoiceContext`（`"hook"`。`Source` と、選択で作った
  `GenericHookGameAction` の `action`）、`GameActionPlayerChoiceContext`（`"game_action"`。`Action`）、
  `BlockingPlayerChoiceContext`（`"blocking"`）、`ThrowingPlayerChoiceContext`（`"throwing"`）。`action` は
  `GameAction` の型名・`Id`・`OwnerId`・`State`、`PlayCardAction` なら `card_uid`（`NetCombatCard.ToCardModelOrNull`）と
  `target_combat_id`（`TargetId`）。手番の初めの処理（`CombatManager.SetupPlayerTurn`）の context は
  `Source` の無い `"hook"`。
- `models`: 各 context の `PlayerChoiceContext.ModelStack`（`Stack` なので上から）を、context 0 から順に並べたもの。
  `Hook` の各通知（`Hook.AfterShuffle`・`AfterPlayerTurnStart` など）は listener を走らせる間 `PushModel` し、
  `CardModel.OnPlayWrapper` は札を積む（本文は札を積んだ `BranchingPlayerChoiceContext` で走る）。よって `models[0]` が
  いま選択を開いた model（計略なら `StratagemPower`、エントロピーなら `EntropyPower`、スプラッシュなら札）。
  行の形は `continuation_context` の listener の行と同じ（`kind` と所有者・uid・パイル。山札の札も uid とパイルだけ）。
- `card_play`: `models` を上から見て最初の、プレイが進行中の札（`History.CardPlaysStarted` にあり、同じ `CardPlay` の
  `CardPlaysFinished` が無い）。形は `combat_history.card_play_events_this_turn` の行と同じに、`models` の中の位置
  `model_index` とプレイした `player_id` を足したもの。`play_index` / `play_count` がその札の何回目の解決か
  （複製などのリプレイ）。無ければ `null`。
- `hook`: `models[0]` について。`declared` は `Hook` が model を積んで走らせる 24 の通知（`AfterShuffle`・
  `AfterPlayerTurnStartEarly` / `AfterPlayerTurnStart` / `AfterPlayerTurnStartLate`・`AfterCardPlayed`・`AfterCardDrawn` など）
  のうち、その型が `AbstractModel` の既定から override しているもの（リフレクションだけ。呼ばない）。`name` は、
  `models[0]` がプレイ進行中の札（本文の `OnPlay`）でも使用中のポーション（`PotionModel.OnUseWrapper` が自分を積む）でも
  オーブ（`OrbCmd` が積む）でもなく、`declared` がちょうど 1 つのときのその通知。それ以外は `null`（どの通知の中かを
  推測しない）で、`name_unavailable_reason` に `card_in_play` / `potion_pushes_itself` / `orb_pushes_itself` /
  `no_declared_hook` / `several_declared_hooks` のどれか（`name` があれば `null`）。`scope` はその通知が回す列（`combat` = `Hook.IterateCombatHookListeners`、
  `combat_all` = `ICombatState.IterateHookListeners()`、`run` = `IRunState.IterateHookListeners(combat)`）。
- `remaining_listeners`: `name` が決まったとき、その通知の列を**いま読み直し**、`models[0]` より後ろで同じ通知を
  override している listener。行は `continuation_context` と同じで、山札の listener は `order: null` と `order_group`
  （山札の順を出さない）。`order` は `models[0]` の次を 0 とする位置。本体はその通知を始めたときに取った列を回すので、
  始めてから `models[0]` までの listener が足した・消した model の分は違いうる（選択そのものはまだ何も動かしていない）。
  `name` が `null`、`models[0]` が山札の listener（後ろの listener の集合が山札の順の一部になる）、列に `models[0]` が
  無い、`ModHelper` に hook の subscriber がいるときは `null`（`name` が `null` 以外の 3 つは `unavailable_reasons` も）。
- 公開の情報だけ: 選択を開いた model は画面で光る・プレイ中の札として見えるもの、listener は場に出ているパワー・
  レリック・ポーション・札で、山札の順は出さない。抽選の結果・乱数・山札の順には触らない。
- `await_chain`（schema 1.9.0）: 選択を待っている async の呼出しの鎖。下の「選択を待つ処理の鎖」。`hook.remaining_listeners`
  がいま読み直した列なのに対し、こちらは本体がその段で実際に回している列から読む。
- 分からないとき（記録が無い、patch を当てられなかった、知らない種類の context・listener）は `known: false` と
  `unavailable_reasons`（文字列の配列）。warning は `choice_source`。
- Markdown は `Choice source: FromHand; models ENTROPY_POWER; hook AfterPlayerTurnStart (2 listeners left)` の 1 行
  （プレイ中の札は `card play SPLASH#15 (1/1)`、未知なら理由）。

出典（v0.113.1、`sts2-a671e9ef`）: `CardSelectCmd` の各入口、`PlayerChoiceContext.ModelStack` / `PushModel`、
`BranchingPlayerChoiceContext`、`HookPlayerChoiceContext`、`GameActionPlayerChoiceContext`、`GenericHookGameAction`、
`PlayCardAction`、`Hook.AfterShuffle` / `AfterPlayerTurnStart` ほか、`CardModel.OnPlayWrapper`、`CombatManager.SetupPlayerTurn`、
`CardPileCmd.Shuffle`、`StratagemPower.AfterShuffle`、`EntropyPower.AfterPlayerTurnStart`、`Splash.OnPlay`。
契約試験は [`tests/ChoiceSourceContract`](../tests/ChoiceSourceContract/README.md)。

#### 選択を待つ処理の鎖（`choice_source.await_chain`、schema 1.9.0）

`choice_source` には、開いている選択の後に続く処理を、いま止まっている async の呼出しから読んだ `await_chain` が付く
（`cat12079801/sts2#1115`）。エントロピー（手番の初めの `AfterPlayerTurnStart` の通常の段）と計略（シャッフルの後の
`AfterShuffle`）の選択で、どの段のどの位置で止まっているか、その段の後に残る listener、シャッフルを起こしたドローの残り枚数・
手番の初めのドローか・自動プレイの予約か、手番の初めの後に続く段を、推測なしで決めるための入力。

MOD は `CardSelectCmd` の postfix で、入口が返した Task を記録に残す。state を作るときは、その Task から外へ、それを
`await` している async メソッドを 1 段ずつたどる（.NET 9 の `Task.m_continuationObject` が await している状態機械の box
〔SynchronizationContext を捕まえた await では `AwaitTaskContinuation.m_action` の先〕を指す）。各段では、その状態機械の
awaiter（`TaskAwaiter.m_task`）が内側の Task を指していることを確かめる。読むのはフィールドだけで、何も呼ばず、列挙子も
進めない。

```jsonc
"await_chain": {
  "version": 1,
  "known": true,
  "frames": [                                   // 内側から外へ。0 が選択を await しているメソッド
    { "index": 0, "type": "StratagemPower", "method": "AfterShuffle", "kind": null },
    { "index": 1, "type": "Hook", "method": "AfterShuffle", "kind": "hook",
      "awaiting": "AfterShuffle", "following": [], "player_id": "1", "context_index": 0, "model_index": 0,
      "listener": { "position": 0, "count": 41,
                    "remaining_listeners": [ { "order": 3, "kind": "relic", "model_type": "TheAbacus", "model_id": "THE_ABACUS", "owner_player_id": "1", "declared_by": "TheAbacus" } ] } },
    { "index": 2, "type": "CardPileCmd", "method": "Shuffle", "kind": "shuffle",
      "awaiting": "AfterShuffle", "following": [], "player_id": "1" },
    { "index": 3, "type": "CardPileCmd", "method": "ShuffleIfNecessary", "kind": "shuffle_if_necessary",
      "awaiting": "Shuffle", "following": [], "player_id": "1" },
    { "index": 4, "type": "CardPileCmd", "method": "DrawInternal", "kind": "draw",
      "awaiting": "ShuffleIfNecessary", "following": ["AddToHand", "AfterCardDrawn"], "player_id": "1",
      "from_hand_draw": true, "draws_requested": 5, "draw_index": 3, "remaining_draws": 2 },
    { "index": 5, "type": "CombatManager", "method": "SetupPlayerTurn", "kind": "setup_player_turn",
      "awaiting": "HandDraw", "following": ["AfterPlayerTurnStart"], "player_id": "1" },
    { "index": 6, "type": "CombatManager", "method": "RunAutoPrePlayPhase", "kind": "auto_pre_play_phase",
      "awaiting": "SetupPlayerTurn", "following": ["CheckForEmptyHand", "AfterAutoPrePlayPhaseEntered"], "player_id": "1" }
  ],
  "end": "no async method awaits CombatManager.RunAutoPrePlayPhase (other continuations: …)",
  "shuffle_origin": "draw"
}
```

- `frames`: 選択を待っている async メソッドを内側から外へ（最大 16 段）。どの段にも `index`・`type`（宣言した型の名前）・
  `method`（`<DrawInternal>d__21` なら `DrawInternal`）・`kind`。次の 8 つは `kind` と、止まっている await（`awaiting`）、
  その後にこのメソッドが続けて走らせる段（`following`、順に）と、`player_id`（そのメソッドの player 引数。
  `Hook.AfterShuffle` は `shuffler`）を持つ。`awaiting` / `following` はこの DLL の各 `MoveNext` の IL で
  `<>1__state` と await の対応を確かめた表による（表と内側の段が合わなければ理由を出す）。その他のメソッド（listener の本体、
  札の `OnPlay` など）は `kind: null` で型と名前だけ。`following` は表で後ろにある段を並べたもので、ループの繰り返しは
  含まない。`DrawInternal`・`AutoPlayFromDrawPile` がループで次の 1 枚に戻る分は `remaining_draws` /
  `remaining_reservations` で表す。

  | `kind` | メソッド | `awaiting` の値（`<>1__state` の順） |
  |---|---|---|
  | `hook` | `Hook.AfterPlayerTurnStart` | `AfterPlayerTurnStartEarly` / `AfterPlayerTurnStart` / `AfterPlayerTurnStartLate`（走っている段） |
  | `hook` | `Hook.AfterShuffle` | `AfterShuffle` |
  | `draw` | `CardPileCmd.DrawInternal` | `AfterPreventingDraw` / `ShuffleIfNecessary` / `AddToHand` / `AfterCardDrawn` |
  | `auto_play_from_draw_pile` | `CardPileCmd.AutoPlayFromDrawPile`（6 引数） | `ShuffleIfNecessary` / `AddToPlay` / `AutoPlay` |
  | `shuffle` | `CardPileCmd.Shuffle` | `AddToDrawPile` / `Wait` / `Tween` / `CustomScaledWait` / `AfterShuffle` |
  | `shuffle_if_necessary` | `CardPileCmd.ShuffleIfNecessary` | `ShuffleFtueCheck` / `Shuffle` |
  | `setup_player_turn` | `CombatManager.SetupPlayerTurn` | `AfterEnergyReset` / `BeforeHandDraw` / `AfterModifyingHandDraw` / `HandDraw` / `AfterPlayerTurnStart` |
  | `auto_pre_play_phase` | `CombatManager.RunAutoPrePlayPhase` | `SetupPlayerTurn`（手番の初めの処理の完了待ち）/ `CheckForEmptyHand` / `AfterAutoPrePlayPhaseEntered`（Early・通常・Late の 3 段） |

- `hook`: `context_index` はその呼出しの `choiceContext` が `contexts` の何番目か、`model_index` は走らせている
  listener（`<model>5__N`）が `models` の何番目か（無ければ `null`）。`listener` はその段が始まったときに本体が作った列
  （`CombatState.IterateHookListeners` の `List<AbstractModel>`。いま読み直した列ではない）から:
  `position` = 走っている listener の位置（0 始まり）、`count` = 列の長さ、`remaining_listeners` = その後ろで同じ段を
  override し、いま本体が走らせる（本体が順番に来たときに確かめる `CombatState.Contains` が真の）listener。行の形は
  `choice_source.hook.remaining_listeners` と同じで、`order` は次を 0 とする列の位置。段が始まったときに山札にあった札
  （とその付着物・エンチャント）は `order: null`・`order_group: "draw_pile_at_stage_start"`・`order_group_start`
  （その連続した塊の始まり）で、塊の中は山札の表示順（いまも山札にある札）、続いて uid の順に並べ直す（山札の順を出さない）。
  段の後に足された model はその段では走らないので含まない。後の段（Late など）の列は、その段が始まるときに作られるので
  出さない（変化で入った札も後の段の listener になりうる）。走っている listener 自身が段の初めに山札にあった、段の初めの山札を
  記録できなかった、`ModHelper` に hook の subscriber がいるときは `position`・`count`・`remaining_listeners` が `null` で、
  理由を出す。
- 手番の初めの選択（`setup_player_turn` の段がある）: 本体の `CombatManager.StartTurn` は、`SetupPlayerTurn` が選択で止まると
  （`HookPlayerChoiceContext.WaitForPauseOrCompletionWithoutAssigningTask` が止まりで戻る）先へ進み、`Hook.AfterSideTurnStart` と
  `OrbQueue.AfterTurnStart` を回してから `RunAutoPrePlayPhase` を作る。よって `auto_pre_play_phase` の段があれば、
  `AfterSideTurnStart` と `OrbQueue.AfterTurnStart` は済んでいる。その後は `SetupPlayerTurn` の残り、続いて
  `auto_pre_play_phase` の `following` の順に走る。
- `draw`: `from_hand_draw`（手番の初めのドローか）、`draws_requested`（`ceil(count)`）、`draw_index`（いま引いている
  ループの添字）、`remaining_draws`（まだ山札から取っていないドローの数。`ShuffleIfNecessary` の待ちでは今回の 1 枚を含む
  `draws_requested - draw_index`、`AddToHand` / `AfterCardDrawn` の待ちではそれより 1 少ない。`AfterPreventingDraw` の待ちは
  ドローが止められたので 0 で、`draws_requested`・`draw_index` は `null`）。手札が満杯・山札が空になれば本体は残りを
  打ち切る。
- `auto_play_from_draw_pile`: `count`・`position`（`top` / `bottom` / `random`）・`force_exhaust`、`reserve_index`
  （予約のループの添字。`AutoPlay` の待ちでは `null`）、`remaining_reservations`（まだ Play に移していない予約の数。
  `ShuffleIfNecessary` の待ちでは今回の 1 枚を含む）、`reservation_call_id`（`card_resolution_events` の
  `auto_play_reservation` の行の `call_id`。同じ context・所有者・引数で開いている記録がちょうど 1 つのとき）。
  予約した札そのもの・抽選の結果は出さない。
- `shuffle_origin`: 鎖に `shuffle` があるとき、そのシャッフルを起こした呼出し。`draw`（ドローの途中）、
  `auto_play_from_draw_pile`（山札からの自動プレイの予約の途中）、`if_necessary`（それ以外から `ShuffleIfNecessary`。
  `KiChant`・`ForegoneConclusionPower`）、`explicit`（`CardPileCmd.Shuffle` を直接。`Reboot`・`KiChant`・`BottledPotential`）。
  鎖が途中で切れて決められなければ `null`。`shuffle` が無ければ `null`。
- `end`: 鎖が止まった理由（どの async メソッドも await していない〔WhenAny などの別の継続は型の名前〕、複数が await
  している、`completed`、`depth limit 16`）。
- 公開の情報だけ: 段の名前・ループの添字・残りの枚数・引数は、札の本文と画面で見えたドロー・予約から決まる。山札の順・
  予約した札・乱数・抽選の結果には触らない。
- 分からないとき（入口の Task を記録できなかった、知らない状態、表と内側の段の食い違い、予約の記録が合わない、
  Task に await として登録された box が別の Task で止まっている〔`end` に `is not suspended on`〕）は
  `known: false` と `unavailable_reasons`（`frames` は読めた分。全体が読めなければ `null`）。`choice_source` の他の
  項目と `known` には影響しない。warning は `choice_source.await_chain`。
- Markdown は `Choice source:` の行の末尾に `awaited by hook AfterShuffle (1 listeners left) < shuffle <
  shuffle_if_necessary < draw (2 left, hand draw) < setup_player_turn < auto_pre_play_phase`（`kind` のある段だけ。
  無ければ足さない。未知なら理由）。

出典（v0.113.1、`sts2-a671e9ef`）: `Hook.AfterPlayerTurnStart` / `AfterShuffle`（`<AfterPlayerTurnStart>d__59` /
`<AfterShuffle>d__76` の `<>7__wrap1`・`<model>5__3`）、`Hook.IterateCombatHookListeners`（`<>7__wrap1`）、
`CombatState.IterateHookListeners`（`<IterateHookListeners>d__74` の `<>7__wrap1` = `List<AbstractModel>.Enumerator`、
`Contains`）、`CardPileCmd.DrawInternal`（`<drawsRequested>5__6`・`<i>5__7`・`fromHandDraw`）・`AutoPlayFromDrawPile`
（`count`・`<i>5__4`・`position`・`forceExhaust`）・`Shuffle`・`ShuffleIfNecessary`、`CombatManager.SetupPlayerTurn` /
`RunAutoPrePlayPhase` / `StartTurn`、`EntropyPower.AfterPlayerTurnStart`、`StratagemPower.AfterShuffle`。段の初めの山札は、
`Hook.AfterPlayerTurnStart` / `AfterShuffle` の間だけ、`<IterateHookListeners>d__74.MoveNext` の最初の呼出しの前に読むだけの
Harmony の prefix で記録する（列を作るのはその呼出し）。契約試験は [`tests/ChoiceSourceContract`](../tests/ChoiceSourceContract/README.md)。

#### 選択の文脈（`decision_context`、schema 1.10.0）

すべての選択画面（`hand_select`、`card_select`〔choose・grid・デッキの画面〕、`bundle_select`、`relic_select`）に、
その画面を開いた入口と、誰が開いたかを出す。戦闘外の画面にも付ける。

```jsonc
"decision_context": {
  "version": 1,
  "known": true,
  "screen": "deck",                      // hand / choose / combat_pile / simple_grid / deck / bundle / relic（他の grid は unknown）
  "in_combat": false,
  "selector": "FromDeckForUpgrade",      // 画面を出した入口（CardSelectCmd / RelicSelectCmd のメソッド）
  "outer_selectors": [],                 // 同じ選択の外側の入口（FromDeckForRemoval → FromDeckGeneric など。同じ名前の多重定義も入る: FromCombatPile の 4 引数の版は 5 引数の版に渡すので ["FromCombatPile"]）
  "chooser_player_id": "1",
  "opener": {
    "kind": "rest_site_option",          // relic / card / potion / power / event / enchantment / affliction / monster / model / rest_site_option / other
    "model_type": "SmithRestSiteOption",
    "model_id": null,                    // モデルの型なら ModelDb.GetEntry(型)（= Id.Entry）
    "method": "OnSelect",
    "call_stack": ["SmithRestSiteOption.OnSelect", "RestSiteRoom.…"]
  },
  "opened_at_combat_log_seq": null,      // 戦闘中なら、開いたときの combat_log.next_seq
  "choice_source": null                  // 戦闘中は画面の choice_source と同じもの（await_chain で段階・ドローの残り）
}
```

- 入口の記録: 戦闘中の 8 つの入口は `choice_source` と同じ prefix（`McpMod.ChoiceSource.cs`）、`PlayerChoiceContext` を取らない
  入口（`FromDeckForUpgrade` / `FromDeckForTransformation` / `FromDeckForEnchantment`〔3 つの多重定義〕/ `FromDeckForRemoval` /
  `FromDeckGeneric` / `FromChooseABundleScreen` / `RelicSelectCmd.FromChooseARelicScreen`）は別の読むだけの prefix・postfix
  （Harmony ID `com.sts2mcp.decisioncontext`。player を context の代わりにする）。state を作るときは、ローカル player・
  同じ戦闘（戦闘外なら戦闘の無い記録）・その画面を出す入口のうち、開いている最も新しい記録を使う。
- `opener`: 入口が呼ばれたときの呼出し履歴（`System.Diagnostics.StackTrace`）でいちばん近いゲームのフレーム。async メソッドの
  同期の部分は呼んだ側のスタックで走るので、レリックの `AfterObtained`・イベントの選択肢・休憩所の `OnSelect`・札の `OnPlay`・
  パワーの通知がそこにある。コンパイラが作る状態機械・ラムダのフレームは元のメソッドの名前にする。MOD・Harmony・ランタイム・
  選択の命令自身のフレームは飛ばす（Harmony が patch したゲームのメソッドも動的メソッドになるので出ない）。JIT が
  インライン化したフレームはスタックに無いので、そのときは次に近いフレームになる。`call_stack` は内側から最大 6 つ。
  ゲームのフレームが無ければ `kind: null` と理由（`known: false`）。
- 記録の戦闘は進行中の戦闘（`CombatManager.IsInProgress` のときの combat state）。戦闘後の報酬の画面（まだ `CombatRoom` で、
  プレイヤーの creature は終わった戦闘を指したまま）でレリックの取得が開くデッキの選択（Astrolabe・EmptyCage・PaelsTooth など）は
  戦闘外の選択として扱う。
- 記録が無い・合わない（`choice_source` と同じ条件）ときは `known: false` と `unavailable_reasons`。warning は `decision_context`。
- 戦闘中の選択が終わると、`combat_log` に `kind: "selection"` の行（選んだもの・skip）を書く（上の表）。
- Markdown は `Decision context: FromDeckForUpgrade; opener rest_site_option SmithRestSiteOption.OnSelect` の 1 行。

出典は v0.113.1 `sts2-a671e9ef` の `CardSelectCmd.FromDeck*` / `FromChooseABundleScreen` / `FromChooseACardScreen` / `FromSimpleGrid*` /
`FromCombatPile` / `FromHand*`、`RelicSelectCmd.FromChooseARelicScreen`、`NDeckCardSelectScreen` / `NDeckUpgradeSelectScreen` /
`NDeckTransformSelectScreen` / `NDeckEnchantSelectScreen`、`ModelDb.GetEntry`。契約試験は [`tests/CombatLogContract`](../tests/CombatLogContract/README.md)。

#### 選択候補の個体（`combat_instance`、schema 7）

戦闘中（`CombatManager.IsInProgress` で combat state が取れる間）の選択画面の候補行（`card_select` の grid・choose、
`hand_select`、戦闘中に開いた `card_reward`）に、その候補がどの個体かを示す `combat_instance` を付ける（`cat12079801/sts2#1019`）。
行の `index` と画面の識別（`screen_type` / `mode`）はそのまま。戦闘外の画面の行には付けず、形も変えない。

```jsonc
"combat_instance": { "source": "pile", "pile": "Draw", "uid": "STRIKE_IRONCLAD#3", "unavailable_reasons": {} }
"combat_instance": { "source": "detached", "pile": null, "values": { /* 下記 */ }, "unavailable_reasons": {} }
```

- 所有者の全パイル（`Player.Piles`）を参照の一致で数え、**戦闘のパイル（Hand / Draw / Discard / Exhaust / Play）のちょうど1つ**に
  あれば `source: "pile"`。`pile` はそのパイル、`uid` はそのパイルの行と同じ uid（手札・パイルと共通の registry）。
  効果の入力はそのパイルの行で読む（`values` は付けない）。
- どの戦闘のパイルにも無ければ `source: "detached"`（3 択の生成候補など）。uid は**発行しない**。デッキなど戦闘外のパイルにある札は
  `pile` にその名前（`"Deck"`）、どこにも無ければ null。`values` に、候補の `CardModel` から直接読んだ効果の入力を出す:
  `id`、[戦闘カードの詳細](#戦闘カードの詳細追加schema-4)の全項目（`type` / `target_type` / `is_upgraded` / `is_upgradable` / `tags` /
  `affliction` / `local_ethereal` / `enchantment` / `dynamic_vars`）、[札の効果の入力](#札の効果の入力schema-7)の全項目
  （単ターンの保留・Sly の期限、リプレイ回数、Mad Science のライダー）、`upgrade_level` / `max_upgrade_level`、
  `local_keyword_ids` / `keyword_ids`、`exhaust_on_next_play`、`energy_cost`（パイルが無いので `with_all_modifiers` は null と
  `detached_preview_context`。[Energy Cost Object](#energy-cost-object追加schema-4)）、`star_cost_state`、`unavailable_reasons`。
  合法な対象・使用可否（`can_play`）・対象別のプレビュー・強化結果は detached の候補では評価しない。
- 戦闘中の choose・grid の行に以前からある行の `uid` は、その個体の registry の uid（パイルに入った後も同じ値）。
  ただし detached の間はどのパイルの行にも無いので、パイルの行への参照として使わない（参照は `source: "pile"` の `combat_instance.uid` だけ）。
  choose の行に以前からある `can_play` / `unplayable_reason` は表示用の既存の値で、detached の候補の評価には使わない。
- 2つ以上のパイルにある札は対応付けず、`source` / `pile` を null にして理由 `ambiguous: in N piles (…)` と warning。
  所有者・パイルが読めなければ同じく null と理由。
- 読むだけ: パイル・札を動かさず、複製・プレイ・フック・乱数を使わない（`Player.Piles` の getter はデッキの配列を一度だけ作って持つ）。
- Markdown は候補の行の下に `  - instance: Draw `STRIKE_IRONCLAD#3`` / `  - instance: detached, values in JSON` /
  `  - instance: unknown (理由)` の1行。

選ばれた候補の取得後の効果は、ready な戦闘 state のパイルの行で（uid で）照合する。
出典（v0.113.1、`sts2-a671e9ef`）: `CardModel.Owner` / `Pile`、`Player.Piles`、`PlayerCombatState.AllPiles`、`CardPile.Cards` / `IsCombatPile`、
`AttackPotion.OnUse`（`CardFactory.GetDistinctForCombat` の候補を `CardSelectCmd.FromChooseACardScreen` に渡す）。
[CombatUpgradePreviewContract](../tests/CombatUpgradePreviewContract/README.md)

#### 隠れている選択画面（`screen_visible` / `hidden_by` / `reveal_on_action`、追加、schema 4）

選択画面は overlay stack の一番上にあっても見えていないことがある。`state_type` は `card_select` のまま、
grid と choose の両方に次を出す。

| 項目 | 型 | 意味 |
|---|---|---|
| `screen_visible` | bool | 画面（grid 画面は `_grid` も）が表示されているか（`Visible` と `IsVisibleInTree`。choose 画面は覗き見中でないことも含む） |
| `hidden_by` | string \| null | 見えていない理由。`map`・`peek`・`capstone`・`unknown`。見えていれば `null` |
| `reveal_on_action` | bool | `select_card` / `confirm_selection` / `cancel_selection` が、押す前に覆いを外すか |

ゲーム側の仕組み（v0.113.1 のデコンパイル）:

- `NOverlayStack.Push` は、`StackIsCovered`（`NMapScreen.Instance.IsOpen` か `NCapstoneContainer.InUse`）のときに
  積んだ画面の `AfterOverlayHidden` を呼び、画面を `Visible = false` にする。マップの `Opened` シグナルは
  `NOverlayStack.HideOverlays` につながっていて、開いている選択画面も同じく隠れる
- 例外: `NDeckCardSelectScreen`（除去などのデッキ選択）は `AfterOverlayShown` / `AfterOverlayHidden` を override して base を呼ばず、`_closeButton` の有効・無効だけを切り替える。そのためマップの下にあっても `Visible` が落ちず、`screen_visible: true`・`hidden_by: null` になる。`select_card` / `confirm_selection` はマップを閉じずにその下で押し、`cancel_selection` は `_closeButton` が無効なので error になる
- 矢印で閉じる経路は `Close(animateOut: true)` なので、閉じる演出（最長約 0.35 秒）の間は `NMapScreen.Visible` が true のまま残る。この間、部屋の state が `map` を返すことがある（sts2 の MCP_ISSUES に未解決として記録）
- マップの左下の戻る矢印は `NMapScreen._backButton`（`NBackButton`）。押すと `OnBackButtonPressed` → `NMapScreen.Close`
  → `Closed` シグナル → `NOverlayStack.ShowOverlays` → `AfterOverlayShown` で画面が戻る。この矢印は `NMapScreen.Open` で
  `ActFloor > 0` のときだけ有効になる
- 戦闘中は画面の `NPeekButton`（`_peekButton`）の覗き見で grid（choose は札の列）が隠れる。戻すのは `SetPeeking(false)`
- `NCapstoneContainer.Open`（デッキの一覧など）も `HideOverlays` を呼ぶ

`hidden_by` の判定の順は `capstone` → `map` → `peek` → `unknown`。`reveal_on_action` が true になるのは、
`map` でマップの戻る矢印が有効かつ `IsTraveling` でないとき、または `peek` のとき。

`reveal_on_action` が true のとき、選択の 3 つの action は先に覆いを外してから続ける。マップは戻る矢印の
`ForceClick`（人がクリックしたのと同じ `Released` → `OnBackButtonPressed` の経路）、覗き見は `SetPeeking(false)`。
それぞれ 1 回までで、外したものを結果の `revealed`（`["map"]`・`["peek"]`・`["map", "peek"]`）に出す。
外しても見えなければ、もう押さずに error を返す。`select_card` は index と候補一覧を先に確かめ、
誤った index ではマップを閉じない。`confirm_selection` / `cancel_selection` は覆いを外してから押すので、押下が
失敗してもマップは閉じたままになる。`reveal_on_action` が false の隠れた画面では、何も押さずに error を返す。

マップを閉じると、その下の部屋（イベント・ショップなど）が見える。選択を終えたら、ふだんどおりその部屋を
出る（`proceed` など）とマップがまた開く。

この状態は、マップを開いたまま開発コンソールで `relic add`（取得時に選択画面を開くレリック）を送ると起きる
（2026-11-16 の検証用ランで観測）。人がトップバーのマップ（`NTopBarMapButton.OnRelease` → `NMapScreen.Open`）を
開いても、開いている選択画面は隠れる。API だけで操作するふだんのランでこの状態になるかは未確認
（`RelicCmd.Obtain` は `AfterObtained` まで await を挟まず、`CardSelectCmd` → `ShowScreen` が `Push` するので、
部屋で取ったレリックの選択画面はマップを開く前に積まれる。ただし全経路は確かめていない）。

山札由来の `NCombatPileCardSelectScreen` は、grid の比較キー（Rarity・Title・Id）が
等しい極大区間ごとに `CardModel.CompareTo == 0` の同値類に分ける。
各同値類が占める位置へ uid の ordinal 順で配り直し、離れた同値札の引く順も意図して隠す。
行の uid・選択印と `select_card.index` のクリック先は、同じ正規化した候補一覧で対応する。
比較キーの取得・比較に失敗した場合は `cards: null` と `card_select.cards` の warning を返す。

During a live combat with an available combat state, grid selection rows (`cards[]`) also carry
`uid`, using the same per-card-instance registry as the combat hand and piles. This includes
combat-pile selection grids. Markdown displays the uid after the row index, as in the hand.
Outside combat, `uid` is omitted. `preview_cards` do not carry uids. Combat choose-a-card rows are
described below. Selection actions still use `index`.

同じ戦闘中の grid 行には `exhaust_on_next_play` も含む。
意味・例外時の扱い・ゲーム側の出典は [次回プレイ時の廃棄フラグ](#次回プレイ時の廃棄フラグ追加schema-3) を参照。
`energy_cost` も同じ条件で含む。値・修正一覧・読取失敗の契約は
[Energy Cost Object](#energy-cost-object追加schema-4) を参照。

```jsonc
{
  "state_type": "card_select",
  "card_select": {
    "screen_type": "transform",  // transform, upgrade, enchant, select, simple_select, choose
    "prompt": "Choose 2 cards to Transform.",
    "cards": [
      {
        "index": 0,
        "id": "STRIKE_R",
        "name": "Strike",
        "type": "Attack",
        "cost": "1",
        "star_cost": null,       // Regent star cost as string, null if N/A
        "description": "Deal 6 damage.",
        "rarity": "Common",
        "is_upgraded": false,
        "selected": false,       // in the screen's own selection set (grid screens only)
        "keywords": [ /* Keyword Objects */ ]
      }
    ],
    "selected_count": 0,         // how many cards the screen currently holds selected
    "required_count": 2,         // the N of a "choose N" prompt; null when a range is accepted
    "min_select": 2,             // the two ends of that range (equal when required_count is set)
    "max_select": 2,
    "preview_showing": false,    // true when the preview panel is up (step two, see below)
    "can_confirm": false,        // true when confirm button is available
    "can_cancel": true,          // true when close/cancel button is available
    "screen_visible": true,      // false when the screen is hidden (under the map, peeking, ...)
    "hidden_by": null,           // "map" | "peek" | "capstone" | "unknown" | null
    "reveal_on_action": false    // selection actions first undo the cover (map back button / peek)

    // For "choose" type: picking is immediate (no confirm needed).
    // can_skip indicates if a skip button exists.
  },
  "run": { ... },
  "player": { ... }
}
```

#### 戦闘中の choose-a-card（追加、schema 4）

`screen_type: "choose"` の `cards[].index` は `select_card` が押す holder 列
（`FindAllSortedByPosition<NGridCardHolder>`）の位置。`CardModel` の無い holder は行を出さず位置を詰めない
（warning `card_select.cards[<index>]`）。`select_card` はその index を覆いを外す前に拒否する。

`CombatManager.IsInProgress` で combat state が取れる間（3 択ポーションなどの選択画面）は、候補の各行に
手札と同じ読取で次を付ける。候補は `CardFactory.GetDistinctForCombat` → `CombatState.CreateCard` で作られた実個体で、
選ばれると同じ個体が `SetToFreeThisTurn` → `CardPileCmd.AddGeneratedCardToCombat` で手札（満杯なら捨て札）に入る
（`AttackPotion` / `SkillPotion` / `PowerPotion` / `ColorlessPotion.OnUse`）。

| キー | 読み方 |
|---|---|
| `uid` | `GetStableCardUid`（手札・パイルと共通の registry。選択後のパイルでも同じ uid） |
| `type` / `target_type` / `is_upgraded` / `is_upgradable` / `tags` / `affliction` / `local_ethereal`（呪詛の札だけ） / `enchantment` / `dynamic_vars` | `AttachCardDetails`（[戦闘カードの詳細](#戦闘カードの詳細追加schema-4)） |
| `can_play` / `unplayable_reason` | `AttachCardPlayability`（`CardModel.CanPlay(out reason, out _)`）。details の `unavailable_reasons` に理由を足す |
| `exhaust_on_next_play` | `CardModel.ExhaustOnNextPlay` |
| `energy_cost` | `BuildCardEnergyCostState`（[Energy Cost Object](#energy-cost-object追加schema-4)） |
| `star_cost_state` | [Star Cost State Object](#star-cost-state-object追加schema-4) |
| `continuation_listeners` | 候補自身・その affliction・enchantment が下記 4 通知を override していれば `{notification, kind, model_type, model_id, declared_by}`。無ければ `[]`、読めなければ `null` |
| `retain_this_turn` / `single_turn_retain` / `sly_this_turn` / `single_turn_sly` / `base_replay_count` / `extra_replay_this_turn` / `enchanted_replay_count`（Mad Science は `tinker_time_rider`） | `AttachCardDetails`（[札の効果の入力](#札の効果の入力schema-7)、schema 7） |
| `combat_instance` | [選択候補の個体](#選択候補の個体combat_instanceschema-7)（schema 7。生成候補は `detached` と `values`） |

値は**選択前**の候補のもの。`cost` / `energy_cost` を 0 に加工しない（無料化は選択の後）。
候補はまだ戦闘のパイルに無いため `CardModel.CombatState` が null で、`CardEnergyCost.GetWithModifiers` は
global のフック（`Hook.ModifyEnergyCostInCombat`）を適用しない。よって候補の `with_all_modifiers` と `can_play` の
コスト判定は local の修正までの値で、手札に入った後の値と違いうる。手札での値は選択後の state で読む。
これを機械的に読み分けられるよう、choose の候補の `energy_cost` にだけ `global_modifiers_applied`
（`CardModel.CombatState != null`。候補では通常 `false`、読めなければ `null`）を付ける。
`false` の行の `with_all_modifiers` は `with_local_modifiers` と同じ範囲の値で、手札の行の同名キーと同じ意味ではない。

画面には戦闘中のほかの選択画面と同じく `battle` が付き（[戦闘中の選択画面の battle](#戦闘中の選択画面の-battleschema-140)）、
同じ条件で `card_select.continuation_context` に選択後の継続で通知を受ける listener を出す。

この欄は**戦闘中のすべての choose 画面**に付く（3 択ポーションのほか、発見系の札・豊穣・知識の呪いなど敵の手番に出る画面も）。
MOD は画面を開いた親の操作を区別しない。4 通知はポーション（`AttackPotion` などの `OnUse` → `OnUseWrapper`）の継続の定義で、
どの画面の継続かと、どの通知が実際に続くかはクライアントが自分の送った親の操作から決める
（例: 札の効果の choose では `after_potion_used` は起きない）。

```jsonc
"continuation_context": {
  "version": 1,
  "known": true,
  "should_skip_combat_effect": false,   // CombatManager.ShouldSkipCombatEffect（true なら戦闘の通知は誰にも届かない）
  "notifications": {
    "after_card_entered_combat":       { "listener_count": 31, "listeners": [ /* rows */ ] },
    "after_card_generated_for_combat": { "listener_count": 31, "listeners": [] },
    "after_potion_used":               { "listener_count": 44, "listeners": [
      { "order": 13, "model_type": "BeltBuckle", "model_id": "BELT_BUCKLE", "declared_by": "BeltBuckle",
        "kind": "relic", "owner_player_id": "1" } ] },
    "after_hand_emptied":              { "listener_count": 31, "listeners": [] }
  }
}
```

- 4 通知は本体の解決順（`CardPileCmd.AddGeneratedCardsToCombat` / `Add` の `Hook.AfterCardEnteredCombat` →
  `Hook.AfterCardGeneratedForCombat` → `PotionModel.OnUseWrapper` の `Hook.AfterPotionUsed` → `CombatManager.CheckForEmptyHand` の
  `Hook.AfterHandEmptied`）。`after_potion_used` は `IRunState.IterateHookListeners(combat)`（デッキの札・enchantment が先頭）、
  他は `ICombatState.IterateHookListeners()` の列（`Hook.IterateCombatHookListeners` と同じ列）。
- `listener_count` はその列の全件、`listeners` はそのうち該当の hook を `AbstractModel` の既定（何もしない）から
  override している型だけ。`order` は列の中の位置（0 始まり）、`declared_by` は実装を宣言した型。
  判定はリフレクションだけで、hook を呼ばない。同じ ID の複数行も潰さない。
- **山札にある listener 同士の順序は非公開。** 列は各 player の `PlayerCombatState.AllPiles`（手札・山札・捨て札・廃棄札・Play）を
  札ごとに並べるので、山札の札（とその affliction・enchantment）の位置は引く順そのものになる。そのため、
  札（または付属効果の親の札）が山札にある行は `order: null` とし、代わりに `order_group: "draw_pile"`、
  `order_group_start`（その player の山札のまとまりが列の中で始まる位置）、`order_group_owner_player_id` を出す。
  行の並びも山札の表示順（`OrderPileCardsForDisplay`。`Rarity` → `CardModel.CompareTo` → uid）に並べ替え、付属効果は親の札の直後に置く。
  他の player の山札も同じ。山札より前・後ろの行の `order` は公開の件数（山札の枚数と付属効果の数を含む）から決まるのでそのまま出す。
  山札の中の listener が実際にどの順で通知を受けるかは分からないので、利用側は順序に依存しない（可換な）場合だけ扱う。
- 行の識別: `kind` は `power` / `relic` / `potion`（`slot`）/ `orb` / `card`（`uid` と `pile`）/ `affliction` / `enchantment`
  （`parent_uid` と `parent_pile`）/ `monster` / `modifier` / `badge` / `run_model` / `dominion` / `achievement`。
  power と monster は所有者の `combat_id` と、敵なら `entity_id`、プレイヤーなら `player_id`（power は `owner_` 接頭辞）。
  relic / potion / orb は `owner_player_id`。戦闘のパイルに無いデッキの札（run scope）は戦闘 uid を発行せず
  `pile: "Deck"` と `deck_index`（`Player.Deck.Cards` の位置）。
- 未選択の候補はまだどの列にも無い。選ばれた候補が加わる分は各行の `continuation_listeners` で読む。
- 通知の条件に要る内部状態（留め具の `_dexterityApplied` など）は既存の公開値（`relics[].combat_state` など）で読む。
  この欄は listener の列であり、通知の結果を計算しない。
- 公開状態に結べない種類の listener があれば `known: false` と `unavailable_reasons`（文字列の配列）。
  他の MOD が `ModHelper` に hook の subscriber を登録しているときは、その subscriber が足す MOD 製の model を
  公開状態に結べないので、列挙せず `known: false`（`notifications` なし）。delegate を呼ばないことの保証ではない
  （同じ GET の可否判定 `CanPlay` → `Hook.ShouldPlay` などの問い合わせの hook が、列挙の中でその delegate を呼びうる）。
  読取失敗も同じく `known: false` で、warning `card_select.continuation_context`。
- Markdown は `Continuation listeners: after_card_entered_combat=N, …` の 1 行（未知なら理由）。

戦闘外の choose（イベントの報酬など）は従来どおり `BuildCardInfo` の行だけで、`battle` も付けない。
GET では選択・無料化・カードの追加・抽選と、通知の hook（`After…`）の呼び出しをしない。`AttachCardPlayability` の `CanPlay` は
手札・パイルの行と同じ読取で、可否の問い合わせの hook（`Hook.ShouldPlay`）を通る。追加だけなので版は上げていない（schema 4 のときの追加）。旧 MOD の欠落は未観測として扱う。
出典は `sts2-a671e9ef` の各ポーションの `OnUse`、`CardFactory.GetDistinctForCombat`、`CombatState.CreateCard` /
`IterateHookListeners`、`RunState.IterateHookListeners`、`Hook.IterateCombatHookListeners` / `AfterCardEnteredCombat` /
`AfterCardGeneratedForCombat` / `AfterPotionUsed` / `AfterHandEmptied`、`CardModel.CombatState` / `CanPlay`、
`CardEnergyCost.GetWithModifiers`、`ModHelper.IterateAllCombatStateSubscribers` / `IterateAllRunStateSubscribers`。
契約試験は [`tests/ChooseCardContract`](../tests/ChooseCardContract/README.md)。

**Deck screens with a preview have two steps.** Cards are picked first; then a preview panel opens — by itself once
`max_select` is reached, or when `confirm_selection` presses the screen's own confirm — and only the
preview's own confirm applies the selection. `selected` comes from the screen's selection set, not from the
grid highlight, so it stays `true` during the preview (the game drops the highlight there but keeps the card).
Simple and combat-pile grid screens instead follow their game preferences: manual confirmation
applies the selection, or selecting enough cards completes immediately without a preview.
Choose-a-card picks immediately. Do not assume every grid screen needs two confirmations.

### `bundle_select` — Card Bundle Selection Overlay

Choose between bundles of cards (e.g. from Scroll Boxes relic).

```jsonc
{
  "state_type": "bundle_select",
  "bundle_select": {
    "screen_type": "bundle",
    "prompt": "Choose a bundle.",
    "bundles": [
      {
        "index": 0,
        "card_count": 3,
        "cards": [
          {
            "index": 0,
            "id": "UPPERCUT",
            "name": "Uppercut",
            "type": "Attack",
            "cost": "2",
            "star_cost": null,   // Regent star cost as string, null if N/A
            "description": "Deal 13 damage. Apply 1 Weak. Apply 1 Vulnerable.",
            "rarity": "Uncommon",
            "is_upgraded": false,
            "keywords": [ /* Keyword Objects */ ]
          }
        ]
      }
    ],
    "preview_showing": false,
    "preview_cards": [ /* Card Objects — shown when a bundle is selected */ ],
    "can_cancel": false,
    "can_confirm": false
  },
  "run": { ... },
  "player": { ... }
}
```

### `relic_select` — Relic Choice Overlay

Boss relic selection. Pick is immediate.

```jsonc
{
  "state_type": "relic_select",
  "relic_select": {
    "prompt": "Choose a relic.",
    "relics": [
      {
        "index": 0,
        "id": "BLACK_STAR",
        "name": "Black Star",
        "description": "Elites drop an additional relic.",
        "rarity": "Rare",
        "keywords": [ /* Keyword Objects */ ]
      }
    ],
    "can_skip": true
  },
  "run": { ... },
  "player": { ... }
}
```

### `crystal_sphere` — Crystal Sphere Minigame

```jsonc
{
  "state_type": "crystal_sphere",
  "crystal_sphere": {
    "instructions_title": "Crystal Sphere",
    "instructions_description": "Use divination tools to reveal cells...",
    "grid_width": 9,
    "grid_height": 11,
    "cells": [
      {
        "x": 0, "y": 0,
        "is_hidden": true,       // true = unrevealed
        "is_clickable": true,    // true = can be clicked to reveal
        "is_highlighted": false,
        "is_hovered": false,
        "item_type": "CrystalSphereGold",  // Only on revealed cells
        "is_good": true                     // Only on revealed cells
      }
    ],
    "clickable_cells": [
      { "x": 4, "y": 7 }       // Convenience list of clickable cells
    ],
    "revealed_items": [
      {
        "item_type": "CrystalSphereGold",
        "x": 2, "y": 3,
        "width": 1, "height": 1,
        "is_good": true
      }
    ],
    "tool": "big",               // "big", "small", or "none"
    "can_use_big_tool": true,
    "can_use_small_tool": true,
    "divinations_left_text": "3 divinations remaining",
    "can_proceed": false
  },
  "run": { ... },
  "player": { ... }
}
```

### `game_over`

Run has ended.

```jsonc
{
  "state_type": "game_over",
  "game_over": {
    "message": "Run ended.",
    "options": ["main_menu"]
  },
  "run": { ... },
  "player": { ... }
}
```

Use `menu_select` with `main_menu` to return to the main menu. `continue` is not advertised because it is not an actionable game-over option.

### `overlay` — Unhandled Overlay (catch-all)

Prevents soft-locks when an unrecognized overlay is active.

```jsonc
{
  "state_type": "overlay",
  "overlay": {
    "screen_type": "NSomeUnknownScreen",
    "message": "An overlay (NSomeUnknownScreen) is active. It may require manual interaction in-game."
  },
  "run": { ... },
  "player": { ... }
}
```

---

## Profiles

Profile endpoints are independent of the singleplayer and multiplayer run endpoints.

### `GET /api/v1/profile`

Returns the active profile's persistent progress summary, including character stats, card stats, encounter stats, discovered content, achievements, epochs, and global totals.

### `GET /api/v1/compendium`

Returns the active profile's progress grouped by the in-game Compendium cards:

- `current_run`: present while a run is active. Includes a derived `run_id` in `{save_scope}:profile{profile_id}:{start_time}` format, plus `start_time`, `seed`, `save_time`, `run_time`, and run metadata read from `current_run.save`.
- `card_library`: discovered card IDs and card pick/skip/win/loss stats. This endpoint intentionally reports profile-level progress; card rules text remains available through game state when cards are visible in a run.
- `relic_collection`: discovered relic IDs. The profile save exposes discovery, but not a typed per-relic description or obtained-count catalog.
- `potion_lab`: discovered potion IDs. The profile save exposes discovery, but not a typed per-potion rules text or lab metadata catalog.
- `bestiary`: profile encounter/enemy fight stats. The in-game Bestiary card is currently marked future/locked, so this section is stats-only and does not expose a full enemy metadata catalog.
- `character_stats`: per-character and global profile totals.
- `run_history`: summaries of the active profile's saved `saves/history/*.run` files. If more than 20 files exist, the response includes the 20 most recent entries and the local `history_path`.

The response is built from `SaveManager.Progress`, the active profile's `progress.save` path, and local run-history files under the same active Steam account. It does not scan every Steam account under the save root. Run-history parsing is bounded to the 20 most recent `.run` files in the response so profile pages with many saved runs do not return unbounded payloads.

`current_run.run_id` is derived from save scope, profile id, and run `start_time`. Use it to identify one concrete run attempt. Use `seed` separately when grouping runs by generated content, because multiple attempts can share the same seed.

```jsonc
{
  "profile_id": 1,
  "current_run": {
    "is_in_progress": true,
    "profile_id": 1,
    "save_scope": "modded",
    "run_id": "modded:profile1:1778295706",
    "start_time": 1778295706,
    "seed": "2450ZAR9EF",
    "run_time": 137
  },
  "sections": {
    "card_library": { "status": "exposed", "discovered_ids": ["BASH"], "stats": [] },
    "relic_collection": { "status": "partially_exposed", "discovered_ids": ["BURNING_BLOOD"] },
    "potion_lab": { "status": "partially_exposed", "discovered_ids": [] },
    "bestiary": { "status": "locked_in_ui", "encounter_stats": [], "enemy_stats": [] },
    "character_stats": { "status": "exposed", "characters": [], "global": {} },
    "run_history": {
      "status": "exposed",
      "entry_count": 10,
      "entries": [
        {
          "id": "1774869148",
          "run_id": "modded:profile1:1774869148",
          "players": [{ "id": 1, "character": "CHARACTER.IRONCLAD" }],
          "ascension": 0,
          "win": false,
          "run_time": 6541
        }
      ]
    }
  }
}
```

### `GET /api/v1/wiki`

Searches wiki-style card and relic entries available to the active profile. The endpoint requires a query, filters to the profile's discovered card and relic IDs, fuzzy-ranks the matches, and returns a bounded result set. It does not expose the full game catalog.

Query parameters:

| Parameter | Values | Default | Description |
|---|---|---|---|
| `query` or `q` | text | required | Fuzzy search text, such as `ironclad perfect strike` or `silver spoon`. |
| `item_type` or `type` | `all`, `card`, `relic` | `all` | Restricts the search to one wiki kind. |
| `limit` | integer | `10` | Maximum returned entries. The mod clamps values below 1 and above its internal maximum. |

Card results include both `base` and `upgraded` variants when the card can be upgraded. The upgraded variant is built from a cloned preview, so the catalog model is not mutated.

```http
GET /api/v1/wiki?query=ironclad%20perfect%20strike&item_type=card&limit=3
```

```jsonc
{
  "status": "ok",
  "profile_id": 1,
  "query": "ironclad perfect strike",
  "item_type": "card",
  "limit": 3,
  "scope": "active_profile_discovered_cards_and_relics",
  "selection_policy": "Searches only cards and relics discovered by the active profile, then returns the best fuzzy matches instead of exposing the full catalog.",
  "counts": {
    "discovered_cards": 42,
    "discovered_relics": 18,
    "searched": 42,
    "returned": 1
  },
  "results": [
    {
      "item_type": "card",
      "id": "PERFECTED_STRIKE",
      "name": "Perfected Strike",
      "score": 775.123,
      "rarity": "Common",
      "type": "Attack",
      "is_upgradable": true,
      "base": {
        "variant": "base",
        "cost": "2",
        "description": "Deal ... damage.",
        "is_upgraded": false,
        "keywords": []
      },
      "upgraded": {
        "variant": "upgraded",
        "cost": "2",
        "description": "Deal ... damage.",
        "is_upgraded": true,
        "keywords": []
      }
    }
  ]
}
```

Relic searches use the same profile filter and ranking:

```http
GET /api/v1/wiki?query=silver%20spoon&item_type=relic
```

Relic results include `id`, `name`, `rarity`, `description`, and `keywords`.

### `GET /api/v1/profiles`

Lists the three profile slots and identifies the active slot.

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

### `POST /api/v1/profiles`

Switch to a profile slot through the game's profile UI:

```json
{ "action": "switch", "profile_id": 2 }
```

Delete an inactive profile slot:

```json
{ "action": "delete", "profile_id": 2 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `switch` or `delete` |
| `profile_id` | int | Yes | Profile slot, from 1 to 3 |

Switching is rejected during a run. Deleting the active profile is rejected; switch away first if you need to remove a slot.

---

## POST — Perform Actions

All POST requests use a JSON body with an `"action"` field and action-specific parameters.

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
503 / `retry: true` の場合も、再送前に state とログを確認する。
マルチプレイには提供しない。許可リスト外は実行しない。
`remove` の実名は `remove_card`。設定・全許可名・拒否条件・出典は
[開発コンソール API](dev-console.md) を参照。

検証用の戦闘外の準備では body に `"scenario_setup_guard": {"version": 1, "expected_screen": "map" | "event" | "rest_site"}` を付けられる
（schema 10、`scenario_capabilities.setup_guard`）。型・版・画面・未知のキーが合わなければ実行せずに拒否する。
受理したらメインスレッドでコマンドを実行する直前に、同じフレームで passive の state を組んで
[`scenario_setup_guard`](#戦闘外の準備の判定scenario_setup_guardschema-10) を判定し、`allowed` が true で `screen` が `expected_screen` と同じときだけ実行する。
GET のあとに画面が動いた・戦闘が始まった・読めないときは `success: false`・`guard_refused: true`・その時点の `scenario_setup_guard` を返し、何も実行しない。
空のコマンドは実行しないが、ガード付きなら判定を通したうえで従来の拒否文（`dev_console requires a non-empty string 'command'.`）を返す
（判定に通らなければガードの拒否）。キーの無い要求は従来どおり。


### Success Response

```jsonc
{ "status": "ok", "message": "Playing 'Strike' targeting Jaw Worm" }
```

### Error Response

Action failures — the action was understood but could not be performed — are returned with
**HTTP 200**:

```jsonc
{ "status": "error", "error": "Card requires a target. Provide 'target' with an entity_id." }
```

Transport failures — invalid JSON, a missing `action` field, an unknown route, a wrong HTTP method,
the wrong run mode for the endpoint, or an unhandled exception — use an **HTTP 4xx/5xx** code but the
same body shape:

```jsonc
// HTTP 400
{ "status": "error", "error": "Missing 'action' field" }
```

So every POST response body carries `status` (`"ok"` or `"error"`), and `error` is present if and
only if `status` is `"error"`. Branching on `status` alone is enough; the HTTP code only distinguishes
*where* the failure happened.

### Unavailable Response (HTTP 503)

```jsonc
{ "error": "timed out after 10000 ms waiting for the game's main thread", "status": "error", "retry": true }
```

Every request — GET included — is executed on the game's main thread, which is pumped once per
rendered frame. A `503` with `"retry": true` means the request **was not executed**: the main thread
did not answer within 10 s, or more than 32 requests are already queued, which only happens when the
game is not processing frames (scene load, hang, minimized or paused app). Nothing was applied to the
run, so the same request may be sent again once the game is responsive. Contrast with `500`, where
the action ran and threw.

### Waiting for the Game to Settle

A POST returns as soon as the action has been dispatched on the main thread. Whether the animation,
card resolution and redraw it started have finished is *not* in the response, so a client that acts
on the reply is reading pre-resolution numbers — enemy HP before a multi-hit landed, a hand that has
not been redrawn. The usual workaround is a GET poll loop on `is_resolving`; `"wait": true` moves
that loop into the server, so one round trip both performs the action and returns the settled state.

```json
{ "action": "play_card", "card_uid": "STRIKE_IRONCLAD#1", "target": "NIBBIT_0", "wait": true }
```

| Parameter | Type | Default | Meaning |
|---|---|---|---|
| `wait` | bool | `false` | Wait for the game to settle before answering. Truthy forms: `true`, `1`, `"1"`, `"true"`, `"yes"`, `"on"`. Also readable from the query string (`?wait=1`, or a bare `?wait`). |
| `wait_timeout_ms` | int | `10000` | Upper bound on the wait, clamped to `500`–`30000`. |
| `include_state` | bool | `true` when `wait` is set | Whether to embed the settled state. |

Response, with the wait's own fields appended after the action's:

```jsonc
{
  "status": "ok",
  "message": "Playing 'Strike' targeting Nibbit",
  "waited_ms": 431,
  "polls": 6,
  "settled": true,
  "changed": true,
  "state": { "state_type": "monster", "...": "..." }
}
```

| Field | Meaning |
|---|---|
| `waited_ms` | Wall-clock duration of the wait. |
| `polls` | Number of state reads performed. |
| `settled` | `true` if the game came to rest; `false` means the timeout was reached and the state may still be mid-resolution. |
| `changed` | Whether anything observable moved compared with the state read just before the action. `false` means the action was a no-op. |
| `state` | The final state, identical in shape to `GET /api/v1/singleplayer`. Omitted when `include_state` is `false`, and when the wait could not read a state at all. |
| `wait_error` | Present only when the wait itself failed (the main thread stopped answering, or the state could not be built). The action already ran, so this is not a `503`. |
| `ended_combat` | schema 1.8.0。この操作の間に戦闘が終わったときだけ。戦闘が終わった時点のローカル Player の `card_resolution_events`（[戦闘を終えた操作の応答](#戦闘を終えた操作の応答ended_combatschema-180)）。`wait` なしの応答にも、操作の実行の中で終わったなら付く。 |

**The settle rule.** Every ~60 ms the server re-reads the state on the main thread and calls it
settled when `is_resolving` is false and, for `monster` / `elite` / `boss`, `battle.is_play_phase`
is true. It must look settled for **3 consecutive polls** (~180 ms) before the wait returns: a click
only takes effect on the following frame, and between two queued actions the game looks idle for a
single frame — one poll would routinely return a mid-resolution state. This mirrors the
empirically tuned loop every operator client ends up writing.

**The no-change rule.** A cheap fingerprint (state type, menu screen, floor, round, player HP /
block / energy, the uids in hand, each enemy's HP and block, the number of options on screen) is
taken just before the action and compared on every poll. If it has not moved within the first
**3000 ms**, the wait stops and returns `settled: true, changed: false` rather than burning the
whole timeout on an action the game ignored.

The wait never runs when the action returned `"status": "error"`, and a state that cannot be built
(it throws while a scene is swapping) counts as "not settled" and is simply polled again.

This is a **singleplayer-only** feature: `POST /api/v1/multiplayer` ignores `wait`,
`wait_timeout_ms` and `include_state`.

---

### `menu_select`

Select an option from the main menu, a menu submenu, profile select, character select, tutorial prompt, blocking popup, timeline screen, or game-over screen.

```json
{ "action": "menu_select", "option": "singleplayer" }
```

```json
{ "action": "menu_select", "option": "main_menu" }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `option` | string | Yes | One of the current state's advertised menu options. Matching is case-insensitive. |
| `seed` | string | No | Only supported in menu contexts that expose a real seeded flow. Standard singleplayer character select currently returns an error without starting a run when `seed` is supplied. |

`game_over` advertises only `main_menu`. `continue` is not actionable on that screen and returns an error.
If `timeline` is blocked by pending obtained epochs, `menu_select` returns an error with `manual_action_required: true` and `pending_epoch_ids` instead of opening Timeline.

---

### `play_card`

Play a card from hand during combat.

```json
{ "action": "play_card", "card_index": 0, "target": "JAW_WORM_0" }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `card_index` | int | Yes | 0-based index in hand |
| `target` | string | For `AnyEnemy` cards | `entity_id` of the target enemy |

**Errors:** Not in combat, not play phase, card unplayable, invalid index, missing target.

**Warning:** Playing a card removes it from hand — all higher indices shift left. Play from highest index first, or re-query state between plays.

### `use_potion`

Use a potion from the potion belt.

```json
{ "action": "use_potion", "slot": 0, "target": "JAW_WORM_0" }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `slot` | int | Yes | Potion slot index |
| `target` | string | For `AnyEnemy` potions | `entity_id` of the target enemy |
| `expected_potion_id` | string | No | Schema 11. The potion id read in that slot (e.g. `FIRE_POTION`); turns on the checks below |

**Errors:** Empty slot, combat-only potion used outside combat, automatic potion, already queued, player dead.

With `expected_potion_id` (`cat12079801/sts2#1012`), on the game thread right before `EnqueueManualUse`:

1. `identity` — the slot still holds a potion with that id.
2. `can_use_now` — the [can-use-now verdict](#ポーションをいま使えるかplayerpotionsschema-11) read again now is `true` (`false` and `null` both refuse).
3. `target` — `target` is one of `valid_target_entity_ids`, as written there (an enemy `entity_id`, `player:<player_id>`; a digits-only
   `combat_id` is refused as a format error, not as a stale target). It may be omitted only for a potion used directly (the list is your own `player:<id>`) and must be omitted when the list is `[]`.
   No enemy is chosen for you and the shop inventory is not closed.
4. `mod_acceptance` — the MOD's own conditions, separate from the game's button: combat-only potions only in the play phase, nothing while
   `PlayerActionsDisabled` in combat.

The creature handed to `EnqueueManualUse` is the one the belt would hand over (direct use: the owner for Self, else null; an enemy by its id;
a player by its NetId; null for `[]`). Any refusal uses nothing and returns
`{"status": "error", "error": "...", "potion_check": {stage, slot, expected_potion_id, actual_potion_id, can_use_now, can_use_now_reason, valid_target_entity_ids}}`;
success adds `potion_check.stage: "passed"`. Without `expected_potion_id` the call behaves as before (auto-picks the only living enemy, closes the
merchant inventory for Foul Potion).

### `discard_potion`

Discard a potion to free up the slot. Use when potion slots are full and you need room for incoming potions (rewards, Cauldron, etc.).

```json
{ "action": "discard_potion", "slot": 0 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `slot` | int | Yes | Potion slot index |

**Errors:** Empty slot, out-of-range slot.

### `end_turn`

End the player's combat turn.

```json
{ "action": "end_turn" }
```

**Errors:** Not in combat, not play phase, card mid-play, hand in selection mode.

### `combat_select_card`

Select a card during in-combat hand selection (exhaust, discard, upgrade prompts).

```json
{ "action": "combat_select_card", "card_index": 0, "expected_uid": "STRIKE_R#0" }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `card_index` (alias `index`) | int | Yes | `hand_select.cards[].index`: the `NPlayerHand.ActiveHolders` position (schema 10) |
| `expected_uid` | string | No | `hand_select.cards[].uid` (schema 10). When given, the card at that holder must still have this uid; otherwise nothing is pressed and `status: error` |

Refused with a reason and nothing pressed, with or without `expected_uid`: a holder without a card, and every press
`NPlayerHand.OnHolderPressed` would silently ignore — while peeking, with an overlay on the stack
(`NOverlayStack.ScreenCount > 0`), or outside a combat in progress (unreadable values count as blocked). These are the
rows with `enabled: false`.

### `combat_confirm_selection`

Confirm the in-combat hand card selection.

```json
{ "action": "combat_confirm_selection" }
```

**Errors:** No selection active, confirm button not enabled or not shown (select more cards first) — the same check as `hand_select.can_confirm`.

### `claim_reward`

現在の報酬画面で ID を解決して操作を送る。推奨は ID 指定。

```json
{ "action": "claim_reward", "reward_id": "<rewards.items[].reward_id>", "wait": true }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `reward_id` | string | ID または index | 現在の画面の有効報酬 ID。推奨 |
| `index` | int | ID または index | 互換の現在一覧の 0 始まり位置。別名 `reward_index` |
| `screen_revision` | string | No | state の値をそのまま送る。古い値は ID 指定でも拒否 |

存在しない／別画面／取得済み／現在無効な ID はエラー。index へのフォールバックはしない。
ID と index を併記して別の報酬を指す場合もエラー。revision + index で古い一覧を検出できる。
**revision なしの旧 index 要求は古いか識別できず、現在位置として実行する。完全な安全保証はない。**
ポーション満杯のエラーは維持する。金・ポーション・レリック等は直接取得を開始し、card は三択を開く。
`special_card` は特定カードの直接取得であり、card の三択とは別の報酬。

成功応答の `status:ok` と `action_stage:submitted` は操作送信を表し、取得完了の保証ではない。
claim 応答に `reward_id` / `screen_revision`、カード選択／alternative 応答に `parent_reward_id` を追加する。
`wait:true` では UI の解決待ち後に `outcome_observed` / `selection_state` / `reward_consumed` を付ける。
`claimed` または `skipped` を確認できた場合のみ `action_stage:resolved` と
`claimable` / `selection_closed` を出す。タイムアウト・観測欠落・セッション終了時は成功を取得完了へ読み替えず、
`outcome_observed:false` とし、消費状態は不明にする。`settled:true` だけも取得完了を保証しない。
開封された三択・デッキ選択は入力待ちであり、その入力を待つためにサーバの settle 待ちを継続しない。

出典: `NRewardButton.GetReward`、`NRewardsScreen.RewardCollectedFrom / RewardSkippedFrom`、
`CardReward.OnSelect / Populate`、`NCardRewardSelectionScreen.OptionSelected`、
`CardRewardAlternative.Generate`、`SpecialCardReward.OnSelect`。

### `select_card_reward`

Pick a card from the card reward selection screen.

```json
{ "action": "select_card_reward", "card_index": 1 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `card_index` | int | Yes | 0-based card index |

### `skip_card_reward`

表示中の `Skip` option を選ぶ。カード三択を閉じ、親報酬は未消費で残り得る。
SACRIFICE や他の alternative の代用にはしない。
応答の `closes_selection_without_consuming_reward:true` は UI option の動作契約を表す。
実際の完了確認は `wait:true` 後の `outcome_observed:true` / `selection_closed:true` /
`reward_consumed:false` と同じ `parent_reward_id` で行う。待機なしの `submitted` は送信段階。
自動進行側は同じ親 ID の `skipped` を自動再開封対象から外し、他の報酬は個別に処理する。
明示的な再開封は可能。新項目のない旧 state は unknown と扱う。

```json
{ "action": "skip_card_reward" }
```

### `proceed`

Leave the current screen and open the map. Works from: rewards, rest site, shop, treasure room.

```json
{ "action": "proceed" }
```

**Note:** Does NOT work for events — use `choose_event_option` with the Proceed option's index instead.

### `choose_event_option`

Choose an event option.

```json
{ "action": "choose_event_option", "index": 0 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `index` | int | Yes | 0-based index matching the option's `index` from state (locked options return an error) |

Works for both regular events and Ancients (after dialogue ends).

### `advance_dialogue`

Click through Ancient dialogue.

```json
{ "action": "advance_dialogue" }
```

Call repeatedly until `in_dialogue` becomes `false` and event options appear.

### `choose_rest_option`

Choose a rest site option (rest, smith, etc.).

```json
{ "action": "choose_rest_option", "option_id": "SMITH" }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `option_id` | string | One of the two | Matches the option's `id` from state, case-insensitively (`HEAL`, `SMITH`, `DIG`, …). Aliases: `id`, `option`. **Preferred** |
| `index` | int | One of the two | 0-based index matching the option's `index` from state. Alias: `option_index` |

The option is resolved in `RestSiteRoom.Options` (the same list the state numbers) and the
button is then looked up from that option, so the on-screen order of the buttons cannot make
this press a different option than the one asked for. Disabled options, unknown ids, and an
`option_id` that disagrees with a supplied `index` all return an error listing every option as
`[index] id (enabled/disabled)`.

Success adds the `option_id` and `index` of what was clicked:

```json
{ "status": "ok", "message": "Selecting rest site option: Smith", "option_id": "SMITH", "index": 1 }
```

### `shop_purchase`

Purchase a shop item.

```json
{ "action": "shop_purchase", "index": 0 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `index` | int | Yes | 0-based index in the flat items list |

**Errors:** Not in shop, item sold out, not enough gold, inventory not ready.

As of v0.113 the purchase goes through `MerchantSynchronizer.PurchaseLocalEntry`, including
at the fake merchant. This is a network-synchronized **async** `Task<bool>`, not a synchronous
success flag. A completed false/faulted/cancelled task returns an error. A completed successful
purchase adds `purchase_completed: true` and `purchase_success: true`. These fields describe
the task at dispatch, not after an optional settle wait. While the task is pending,
`status: ok` means the purchase was submitted, with `purchase_completed: false` and no
`purchase_success`. Re-read state for stock/gold changes or a card selection; never assume
the purchase has completed from `status` or `settled` alone. Later failures/cancellation are
logged. The main thread is never blocked waiting for a removal/relic selection.

### `choose_map_node`

Travel to a map node.

```json
{ "action": "choose_map_node", "col": 2, "row": 3 }
{ "action": "choose_map_node", "index": 0 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `col` + `row` | int | One selector | Coordinates of a node in `next_options` (preferred: stable) |
| `index` (alias `node_index`) | int | One selector | 0-based index from `next_options` |

マップが選択を受け付けるときだけ押す。人のクリック（`NMapPoint.OnRelease` → `IsTravelable`）と同じ条件で、次のときは何も押さずに error を返す（単独・協力プレイとも）。

| 状況 | error | ゲームの値 |
|---|---|---|
| マップが開いていない | `Map screen is not open` | `NMapScreen.IsOpen` が false で画面も見えていない |
| 閉じる演出の途中（state は `map`・`map_closing`） | `Map screen is closing. ...` | `IsOpen` が false で画面が見えている（`Close(animateOut: true)` → `AnimClose`） |
| 移動中（state は `map`・`map_traveling`） | `The map is already traveling to a node. ...` | 上の「画面の遷移」の `map_traveling` と同じ条件 |
| 部屋が終わる前に開いたマップ（トップバー） | `Travel is not enabled on this map ...` | `IsTravelEnabled` が false（開発用の `IsDebugTravelEnabled` が立っていれば受け付ける） |

移動が有効かは state の `map.travel_enabled`（`NMapScreen.IsTravelEnabled`）で分かる。markdown は false のときだけ `**Travel:** not enabled` の行を出す。

### `select_card`

Select a card in a card selection overlay.

```json
{ "action": "select_card", "index": 0 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `index` (alias `card_index`) | int | Yes | 0-based index in the latest full `card_select.cards` candidate list; no scrolling required |
| `expected_uid` | string | No | `card_select.cards[].uid` (schema 10; rows have uids only during a combat). When given, the grid candidate (same normalized snapshot the click uses) or choose-a-card holder at `index` must have this uid. A mismatch never presses. The uid is checked before anything is revealed; on a grid hidden under the map or a peek it is checked again after revealing, on the snapshot the click uses, and if that second check fails nothing is pressed but the screen stays revealed and the error carries `revealed`. Out of combat (no uids) it is always refused |

**Behavior varies by screen type:**
- Grid screens: pass the original model at the full candidate index to the concrete game's
  `OnCardClicked(CardModel)` once, whether on screen or off screen. Already selected cards toggle off
  when the game permits it. Deck screens with previews open them at `max_select` or on confirm.
- Simple / combat-pile grids: honor manual confirmation or immediate completion according to the
  screen's preferences. Immediate completion returns success without reading the closed screen.
- Choose-a-card screens (potions, effects): picks the card immediately.

On grid screens the result carries the outcome read back from the screen: `selected` (true when the card is
now in the selection), `selected_count`, `min_select` / `max_select`, and `preview_showing` when the press
opened the preview.

Returns `status: error` when the press would not land: while a preview is open (use `confirm_selection` or
`cancel_selection` — pressing through it corrupts the preview row), and when the screen refuses the press
because its selection is already full. A hidden screen is first revealed when `reveal_on_action` is true
(see [隠れている選択画面](#隠れている選択画面screen_visible--hidden_by--reveal_on_action追加schema-4));
the result then carries `revealed`. A screen that cannot be revealed or stays hidden, an unavailable candidate
list, invalid index, or missing click method also returns error. The index and candidates are checked before
the map is closed. A click exception returns error with its underlying cause;
the game may already have changed state, so re-read state before another action. There is no retry
through holders, scrolling, or direct selection-set mutation.

### `confirm_selection`

Confirm card selection (grid screens only).

```json
{ "action": "confirm_selection" }
```

With a preview open, presses the preview's own confirm, which applies the selection. Otherwise presses the
screen's confirm, which on most of these screens **opens the preview** rather than applying anything — the
message says which of the two happened, so a two-step screen needs two calls. Not needed for choose-a-card
screens.

Returns `status: error` rather than an `ok` that does nothing when fewer than `min_select` or more than
`max_select` cards are selected, when no confirm button is enabled, or when the press was swallowed (the
screen is still open and no preview appeared).

A hidden screen (`reveal_on_action: true`) is revealed first, and the result carries `revealed`; one that cannot
be revealed returns error without pressing anything.

### `cancel_selection`

Cancel or close the card selection overlay.

```json
{ "action": "cancel_selection" }
```

**Behavior:**
- If a preview is showing: cancels back to the selection grid. This also **clears the selection** — the game's
  own cancel empties the set, so the grid comes back with nothing selected.
- For choose-a-card screens: clicks the skip button (if available).
- Otherwise: closes the selection screen (if cancellation is allowed).
- A hidden screen (`reveal_on_action: true`) is revealed first (its skip and preview buttons are disabled or
  invisible while hidden), and the result carries `revealed`; one that cannot be revealed returns error.

### `select_bundle`

Open a bundle preview in the bundle selection screen.

```json
{ "action": "select_bundle", "index": 0 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `index` | int | Yes | 0-based bundle index |

### `confirm_bundle_selection`

Confirm the currently previewed bundle.

```json
{ "action": "confirm_bundle_selection" }
```

### `cancel_bundle_selection`

Cancel the bundle preview.

```json
{ "action": "cancel_bundle_selection" }
```

### `select_relic`

Pick a relic from the relic choice screen. Selection is immediate.

```json
{ "action": "select_relic", "index": 0 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `index` | int | Yes | 0-based relic index |

### `skip_relic_selection`

Skip the relic choice.

```json
{ "action": "skip_relic_selection" }
```

### `claim_treasure_relic`

Claim a relic from an opened treasure chest.

```json
{ "action": "claim_treasure_relic", "index": 0 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `index` | int | Yes | 0-based relic index |

### `crystal_sphere_set_tool`

Switch divination tool in the Crystal Sphere minigame.

```json
{ "action": "crystal_sphere_set_tool", "tool": "big" }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `tool` | string | Yes | `"big"` or `"small"` |

### `crystal_sphere_click_cell`

Reveal a cell in the Crystal Sphere.

```json
{ "action": "crystal_sphere_click_cell", "x": 4, "y": 7 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `x` | int | Yes | Cell x-coordinate |
| `y` | int | Yes | Cell y-coordinate |

### `crystal_sphere_proceed`

Finish the Crystal Sphere minigame.

```json
{ "action": "crystal_sphere_proceed" }
```

---

## Multiplayer Additions

### GET — Additional Top-Level Fields

```jsonc
{
  "game_mode": "multiplayer",
  "net_type": "SteamMultiplayer",
  "player_count": 2,
  "local_player_slot": 0,      // Index of local player in players array
  "players": [                  // Summary of all players
    {
      "character": "The Ironclad",
      "hp": 72, "max_hp": 80,
      "gold": 99,
      "is_alive": true,
      "is_local": true
    }
  ]
}
```

### Battle State Additions

```jsonc
{
  "battle": {
    "all_players_ready": false
    // Same shape as singleplayer (round, turn, is_play_phase, enemies).
    // Player state lives in top-level "player" (local) and "players" (all).
  },
  "players": [
    {
      "character": "The Ironclad",
      "hp": 72, "max_hp": 80,
      "gold": 99,
      "is_alive": true,
      "is_local": true,
      "is_ready_to_end_turn": false   // Only present during combat
    },
    {
      "character": "The Necrobinder",
      "hp": 60, "max_hp": 66,
      "gold": 80,
      "is_alive": true,
      "is_local": false,
      "is_ready_to_end_turn": false,
      "pets": [                       // Omitted for local player (pets are under top-level "player")
        {
          "id": "OSTY",
          "name": "Otsy",
          "alive": true,
          "hp": 12, "max_hp": 12,
          "block": 0,
          "status": [ /* Power Objects */ ]
        }
      ]
    }
  ]
}
```

### Map State Additions

```jsonc
{
  "map": {
    "votes": [
      {
        "player": "The Ironclad",
        "is_local": true,
        "voted": true,
        "vote_col": 2, "vote_row": 3
      }
    ],
    "all_voted": false
  }
}
```

### Event State Additions

```jsonc
{
  "event": {
    "is_shared": true,
    "votes": [
      {
        "player": "The Ironclad",
        "is_local": true,
        "voted": true,
        "vote_option": 0
      }
    ],
    "all_voted": false
  }
}
```

### Treasure State Additions

```jsonc
{
  "treasure": {
    "is_bidding_phase": true,
    "bids": [
      {
        "player": "The Ironclad",
        "is_local": true,
        "voted": true,
        "vote_relic_index": 0
      }
    ],
    "all_bid": false
  }
}
```

### Multiplayer-Only Actions

**End turn (vote):**
```json
{ "action": "end_turn" }
```
In multiplayer, this is a vote. The turn ends only when all players submit.

**Undo end turn:**
```json
{ "action": "undo_end_turn" }
```
Retract the end-turn vote before all players have committed.

All other actions work identically to singleplayer.
