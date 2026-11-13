# Full API Reference

HTTP API served by the STS2_MCP mod on `localhost:15526`. No authentication. Local use only.

**Endpoints:**
- `GET  /` — health check: `{ "message", "status", "version", "commit", "schema_version" }`
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

JSON responses are compact by default; `pretty` opts into indentation for that one request. It works on
every JSON-returning endpoint, on POST as well as GET (e.g. `POST /api/v1/singleplayer?pretty=1`), and on
error responses. It has no effect on `format=markdown`.

### Common Top-Level Fields

Every response — `menu` and error-ish states included — carries the build stamp:

```jsonc
{
  "state_type": "...",      // Screen identifier (see sections below)
  "mod_version": "0.4.0",   // Mod version
  "mod_commit": "2de67cd",  // Git commit built from ("<sha>-dirty" from an edited tree); null if unknown
  "game_version": "v0.113.0", // release_info.json version; null if unavailable
  "game_commit": "16ac0fe3",  // release_info.json commit; null if unavailable
  "schema_version": 4       // Contract version — see below
}
```

`schema_version` is bumped **only** when an existing field or action changes meaning or is removed.
Purely additive changes (new fields, actions, parameters, `state_type`s) do not bump it, so
feature-detect additions by presence and use `schema_version` only to notice a breaking change.

`game_version` and `game_commit` come from the game's `ReleaseInfoManager`, which reads
`release_info.json` (`.app/Contents/Resources/release_info.json` on macOS). They identify the
running game's release, independently of the mod build. Singleplayer, multiplayer and menu
states carry them; markdown also shows the game stamp. Dump consumers should copy these
fields into their own `meta.json` (dump metadata is not written by this mod).

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

### 解放済みの戦闘生成 pool（追加、schema 3）

`player.generation_pool` は単独プレイとマルチのローカル `player` に入る。
`BuildPlayerState` と同じ snapshot で、その Player 自身の `UnlockState` を使う。
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
  "game_assembly_sha256": "9cb4f1ad8c9f284aa8fec3122ffd6d780bbf543d875c817abdd12ff63fbf12b4",
  "filter_version": 1,
  "filter_source": "CardPoolModel.GetUnlockedCards(Player.UnlockState,IRunState.CardMultiplayerConstraint);CardFactory.FilterForPlayerCount;CardFactory.FilterForCombat;OrobicAcid.OnUse:Attack,Skill,Power"
}
```

`GetUnlockedCards` の結果に実人数の filter、`CardFactory.FilterForCombat`、
Attack/Skill/Power の型 filter を適用し、ID の昇順で出す。`FilterForPlayerCount` は private
なので、人数 > 1 では SingleplayerOnly、そうでなければ MultiplayerOnly を除く同じ条件を実装した。
抽選、戦闘カード作成、RNG の読み取りはしない。ID と型だけを載せ、本文・コストは複製しない。
`game_assembly_sha256` は実際にロードした `CardModel` の assembly ファイルから初回に計算して保持する。
`filter_version` はこの pool 契約の版であり、state の schema とは別。

取得中に例外が出た場合は `known: false, cards: []` と `reason`（例外の型とメッセージ）、
state の `warnings` を返す。取得前に失敗したメタデータは欠落しうる。部分一覧を known=true にしない。
旧 MOD にはこの欄が無い。取得不能と空一覧を同一視せず、欄の存在を検出すること。
Markdown は単独・マルチのローカル player について件数・人数、または unknown と理由の1行だけを出す。
追加項目なので schema は 3 のまま。

出典は sts2-9cb4f1ad の `OrobicAcid.OnUse`、`CardPoolModel.GetUnlockedCards`、
`CardFactory.FilterForCombat/FilterForPlayerCount`。これは discovered_cards とは別の情報。
`NCardLibraryGrid.GetCardVisibility` は profile の解放条件と発見条件を分け、解放済みでも未発見なら
NotSeen にする。図鑑がこのラン固有の候補ID一覧をそのまま見せるとは言えない。
解放対象札は各 Epoch の `QueueUnlocks` → `NTimelineScreen.QueueCardUnlock` →
`NUnlockCardsScreen.Open` で表示される。保存ランの `Player.UnlockState` は profile の現在値と
異なりうる（`UnlockState` の契約、`Player.FromSerializable`）。未来の抽選結果・山札順は含まない。

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
  "draw_pile": [ /* Pile Card Objects (sorted by rarity, matching in-game display) */ ],
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
      "vars": { "Heal": 6 }, // DynamicVar name -> numeric BaseValue (decimals preserved)
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
      "keywords": [ /* Keyword Objects */ ]
    }
  ],
  "max_potion_slots": 3      // Belt capacity. Default 3, grows with belt-expanding relics (e.g. Potion Belt: +2). Use to detect a full belt: len(potions) >= max_potion_slots.
}
```

Owned relics always include boolean `used_up` and `melted`, in singleplayer and the
multiplayer local `player` object (both use `BuildPlayerState`). Markdown marks true
values with `(used)` and `(melted)` in singleplayer outside combat, and during combat.
Multiplayer outside combat does not list relics in Markdown. These fields are additive:
feature-detect their presence; the schema stays at 3. Older builds omit them.
`melted` is separate from consumption: `RelicModel.HoverTip` exposes the melted
notice, and `UpdateTexture` gives it a dark-red tint. The grey inventory tint comes
from `RelicStatus.Disabled` in `NRelicInventoryHolder.RefreshStatus`; for example,
`LizardTail.WasUsed` sets that status when consumed. These booleans do not claim to
encode every reason a relic can be disabled.

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
  "card_plays_finished_this_turn_card_uids": ["THRUMMING_HATCHET#0", "THRUMMING_HATCHET#0"],
  "last_player_turn_card_uids": ["BOLAS#0", "BOLAS#0", "STRIKE_IRONCLAD#1"],
  "cards_exhausted_this_turn": 1,
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
| `card_plays_finished_this_turn_card_uids` | `History.CardPlaysFinished.Where(e => e.HappenedThisTurn(combat))` の `CardPlay.Card` を既存の `GetStableCardUid` で変換する。次手番の `Bolas.BeforeHandDraw` / `ThrummingHatchet.BeforeHandDraw` の個体判定用。`CardPlay.Player == owner` を追加しない。 |
| `last_player_turn_card_uids` | `History.CardPlaysFinished.Where(e => e.HappenedLastPlayerTurn(owner))` の `CardPlay.Card` を既存の `GetStableCardUid` で変換する。`Bolas.BeforeHandDraw` / `ThrummingHatchet.BeforeHandDraw` がこの集合の札との個体一致を調べる。両クラスと同じく `CardPlay.Player == owner` を追加しない。 |
| `cards_exhausted_this_turn` | `History.Entries.OfType<CardExhaustedEntry>()` で `e.HappenedThisTurn(combat) && e.Actor == creature`。`ForgottenRitual.WasCardExhaustedThisTurn` / `EvilEye.WasCardExhaustedThisTurn` の Any と同じ集合の件数。 |
| `unblocked_damage_events_this_turn` | `History.Entries.OfType<DamageReceivedEntry>()` で `e.HappenedThisTurn(combat) && e.Receiver == creature && e.Result.UnblockedDamage > 0`。`Spite.LostHpThisTurn` の Any と同じ集合の件数。 |
| `unblocked_damage_events_this_combat` | 同じ DamageReceivedEntry の Receiver・UnblockedDamage 条件で、ターン条件なし。`TearAsunder.CanonicalVars` の CalculatedHits の `1 + Count(...)` の Count 部分。 |
| `block_gains_this_turn` | `History.Entries.OfType<BlockGainedEntry>()` で `e.HappenedThisTurn(combat) && e.CardPlay != null && e.CardPlay.Player.Creature == creature && e.Props.IsCardOrMonsterMove()`。`UnmovablePower.ModifyBlockMultiplicative` を **`cardPlay = null`（次のプレイを評価する文脈）** とした式そのもの。 |

開始・完了は独立に読むため、解決中には差が出る。`by_type` は `CardType` の全 enum 名をキーにし、
未発生の型も 0。`total` は全型の合計。リプレイ・自動プレイも entry の数だけ数え、
重複を除かない。本体と同じく現在の `CardPlay.Card.Type` を読み、過去の型の snapshot は作らない。

開始数の Attack は `JugglingPower.AfterApplied` の初期化式とは一致するが、
ジャグリングの実際の判定は private な `Data.attacksPlayedThisTurn` を使う。
その後は `BeforeCardPlayed` が `cardPlay.Card.Owner == Owner.Player` の条件で加算し、
`AfterSideTurnEnd` がリセットするため、`CardPlay.Player` で絞る履歴とは
所有者が変わる札などで一致しない。engine が履歴をジャグリングに使うのは近似の代用に留まる。
正確な値は sts2 #863（パワーの内部状態）で `Data` を読み、そちらを使う。

このターンと前手番の uid 配列はどちらも履歴順で重複を保つ。他 Player のプレイも含む。
これは札の所有者がプレイ後に変わり得るため、Bolas / ThrummingHatchet が
`CardPlay.Player` ではなく札の個体を調べる式に合わせている。
`CardPlay` の注釈もプレイ時の Player と現在の `Card.Owner` の違いを明記している。
uid は手札・各パイル・grid と共通の戦闘単位の registry を使い、履歴にしか残らない札にも
同じ `<card_id>#<n>` を割り当てる。`HappenedLastPlayerTurn` は Player の TurnNumber が
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

Markdown は正の件数・非空の uid 配列・取得失敗があるときだけ短い History 行を出す。
このターンの非空 uid は `this turn=[...]`、前手番の非空 uid は `last turn=[...]` と区別する。
既知の 0 と空配列だけなら省略し、TurnNumber 単独では行を増やさない。
失敗時は null と理由を表示する。既存の意味の変更・削除を伴わない追加なので
`schema_version` は **4 のまま**。利用側は `combat_history` の有無で検出する。

契約試験と実機確認の観点は [`tests/CombatHistoryContract`](../tests/CombatHistoryContract/README.md)。

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
プレビュー更新・ゲームのフック・RNG・setter を呼ばない。

`StringVar` は表示文字列で、継承した `BaseValue` の 0 はダミーのため除く。
数値変数がない、または文字列だけなら `vars: {}`。
初期化・列挙・数値読み取りが失敗した場合は、そのレリックの `vars` 全体だけを省略し、
`warnings` に `player.relics.<ID>.vars` を記録する。部分的な一覧や代用の 0 は返さない。
欠落は unknown と扱い、空オブジェクトと区別する。旧 MOD にもこの項目はない。
追加項目なので schema は 3 のまま。Markdown は既存の本文表示を使う。

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
追加項目なので現在の schema は 4 のまま。旧 MOD・旧 dump は追加項目を持たない。
Markdown にも `(used this combat: yes)`、`(used this turn: no)` 等を付ける。
虹の指輪は各種別の枚数と発動数、肉兵は cooldown、脈打つ亡骸は HP 損失累積を表示する。

全項目名・意味・戻る時機と、出す／出さない判断のソース出典は
[レリックの戦闘中の内部状態](relic-combat-state.md) を参照。
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
  "unplayable_reason": null, // e.g. "NotEnoughEnergy", "Unplayable", null if playable
  "exhaust_on_next_play": false, // Combat-only CardModel.ExhaustOnNextPlay; see below
  "energy_cost": { /* Energy Cost Object below; combat only */ },
  "is_upgraded": false,
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
  "target_type": "AnyEnemy",
  "affliction": null,
  "enchantment": null,
  "dynamic_vars": { "Damage": 6 },
  "unavailable_reasons": {},
  "cost": "1",               // Energy cost as string ("X" for X-cost)
  "star_cost": null,          // Regent star cost as string, null if N/A
  "exhaust_on_next_play": false,
  "energy_cost": { /* Energy Cost Object below */ },
  "description": "Deal 6 damage."
}
```

### 戦闘カードの詳細（追加、schema 4）

`player.hand[]` / `draw_pile[]` / `discard_pile[]` / `exhaust_pile[]` /
`play_pile[]` は同じ読取処理で次の項目を出す。
単体・マルチのローカル player は共通の `BuildPlayerState` を通る。
対象は戦闘中の手札と4パイルで、各行は既存の `uid` で照合できる。

- `type` / `target_type` は `CardModel.Type` / `TargetType` の enum 名。
- `affliction` は `CardModel.Affliction` の `{ id, amount }`。
  `id` は `Id.Entry`、`amount` は現在の `Amount`（整数）。
- `enchantment` は `CardModel.Enchantment` の `{ id, amount, status }`。
  `status` は `EnchantmentModel.Status` の enum 名（`Normal` / `Disabled`）。
  例は `{ "id": "VIGOROUS", "amount": 3, "status": "Disabled" }`。
- `dynamic_vars` は `CardModel.DynamicVars` の変数名から現在の `BaseValue` への辞書。
  小数・0・負数をそのまま JSON 数値で出す。`StringVar` は文字列用のため除外する。
  数値変数が0件なら `{}`。成長・強化を反映した現在値を毎回読む。
  EnchantedValue / PreviewValue ではない。CalculatedVar は計算前の基底値。
- `unavailable_reasons` は読取失敗のパスから理由への辞書。正常時は `{}`。

災禍・エンチャントの getter が正常に null を返したときは、その項目を null とし、
理由の辞書は空。読取例外が出たときは該当値を null にし、理由と `warnings` を付ける。
例として `affliction.amount`、`enchantment.status`、`dynamic_vars.Damage` の失敗では、
同じオブジェクト内の読めた項目を保持する。変数一覧の取得・列挙の失敗は
`dynamic_vars` 全体を null にする。手札の `CanPlay` の例外も局所化し、
`can_play` / `unplayable_reason` を null と理由にする。
項目の欠落がある旧 snapshot は未観測として扱う。

山札はコピーを rarity → card ID の順にソートして表示する。
Markdown はパイルの札の本行末尾に `[Attack/AnyEnemy]` の形で型・対象を付ける。
存在する災禍・エンチャントと読取失敗の理由は短い子行で表示する。
エンチャントは `status` が `Disabled` のときだけ `(disabled)` を付ける。
基底値は `base Damage 6, Vulnerable 2` の形で札ごとに1行にまとめる。
手札の型・対象は既存の行に表示し、プレイ可否が読めなければ `?` を付ける。
`energy_cost.unavailable_reasons` はコスト専用、行の `unavailable_reasons` は本節の項目用。

追加項目で、既存の項目・action の意味を保つため `schema_version` は **4**。
利用側は項目の有無で追加機能を検出する。

出典（v0.113.0、`sts2-d009478a` デコンパイル）は `CardModel.Type` / `TargetType` /
`Affliction` / `Enchantment` / `DynamicVars` / `CanPlay`、`AfflictionModel.Amount`、
`EnchantmentModel.Amount` / `Status`、`EnchantmentStatus`、`DynamicVar.BaseValue`、`DynamicVarSet.GetEnumerator`、
`StringVar`。契約試験は詳細の取得・失敗・表示を検査する。
[CardDetailsContract](../tests/CardDetailsContract/README.md)

### Energy Cost Object（追加、schema 4）

戦闘中の `player.hand[]` / `draw_pile[]` / `discard_pile[]` / `exhaust_pile[]` /
`play_pile[]` と、戦闘中の `card_select.cards[]` の grid 行に `energy_cost` を出す。
grid は `uid` と同じ条件（`CombatManager.IsInProgress` と取得可能な combat state）。
単独・マルチのローカル player は共通の `BuildPlayerState` を通る。
戦闘外のデッキ・選択、choose-a-card、`hand_select.cards[]`、各 preview には出さない。
手札選択中も同じ札は `player.hand[]` で読む。

```jsonc
{
  "costs_x": false,
  "base": 2,
  "with_local_modifiers": 1,
  "with_all_modifiers": 1,
  "local_modifiers": [
    { "amount": -1, "type": "Relative", "expiration": "WhenPlayed", "reduce_only": false }
  ],
  "unavailable_reasons": {}
}
```

- `base`: `CardEnergyCost.GetWithModifiers(CostModifiers.None)`。現在の基礎コストであり、
  強化や `SetCustomBaseCost` を含む。印刷時の値 `Canonical` とは区別する。
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
- `unavailable_reasons`: `null` のキー名 → 理由の文字列。正常な固定コストでは `{}`。
  X は3つの数値を `null` にして各理由を `x_cost`、`None` が負（エナジーコストなし）は
  `no_energy_cost` とする。カードの Curse/Status 種別や Unplayable keyword からは判断しない。
  `costs_x` 自体の読取失敗は `costs_x: null`、数値側は `costs_x_unavailable`。
  基礎値の読取失敗は `base` に例外の理由、修正後の数値は `base_unavailable`。
  その他の読取・計算失敗も該当値だけ `null` と例外の理由にし、`warnings` に
  `card.energy_cost[.<key>]` を出す。private リストが読めなくても public のコストは読む。
  `EnergyCost` 自体が取得できない場合もオブジェクトを残し、5項目を `null` と理由にする。
  通常の X / コストなしには警告を出さない。

`player.play_pile` / `play_pile_count` は戦闘中の一時的な Play pile。
行は他のパイルと同じ形（`uid`、`exhaust_on_next_play` も含む）。
プレイの合間には通常空で、解決中には過渡的な値が見えるため、計画は `is_resolving: false` の
snapshot を使う。山札は既存どおり表示用のソート順で、内部のドロー順を出さない。

Markdown は local modifier が非空、修正後のコストが基礎値と異なる、または
`x_cost` / `no_energy_cost` 以外の理由の `null` がある場合だけ、手札・各パイル・grid 行の下に
base / local / all と非空の修正一覧（値・種別・期限・reduce_only）を適用順で表示する。
`null` には理由を添える。Play Pile の節は `play_pile_count` が1以上の場合だけ表示する。
既存の `cost`（文字列、X は `"X"`、それ以外は `GetAmountToSpend()`）と `star_cost` は変更しない。
ここで追加するのはエナジーコストだけで、star cost の修正は対象外。
既存フィールドの意味・削除・action の変更がないため `schema_version` は **4 のまま**。
利用側は番号ではなく `energy_cost` の有無で検出する。古い state の欠落は「修正なし」ではなく未観測。

出典（v0.113.0、ローカルの `sts2-d009478a` デコンパイル）:

- `MegaCrit.Sts2.Core.Entities.Cards/CardEnergyCost.cs`: `_localModifiers`（25行）、
  `GetWithModifiers`（94–121行）、`SetThisTurnOrUntilPlayed`（197–203行）、
  `SetThisCombat`（238–244行）、`AddUntilPlayed`（258–264行）、
  `EndOfTurnCleanup` / `AfterCardPlayedCleanup`（333–347行）、`UpgradeBy` / `SetCustomBaseCost`。
- 同ディレクトリの `LocalCostModifier.cs` の各 getter（48–76行）と `Modify`（89行）、
  `LocalCostType.cs` / `LocalCostModifierExpiration.cs` / `CostModifiers.cs` の enum。
- `MegaCrit.Sts2.Core.Models.Cards/ExpectAFight.cs` の `OnPlay`（31–36行）は
  X 以外の手札アタックへ `AddUntilPlayed(-Energy.IntValue)`。
  `Enlightenment.cs` の `OnPlay`（16–24行）は通常 `SetThisTurnOrUntilPlayed(1, true)`、
  強化後 `SetThisCombat(1, true)`。
- `MegaCrit.Sts2.Core.Entities.Players/PlayerCombatState.cs` の `PlayPile` / `AllPiles`（71–85行）。

契約試験は [`tests/CardEnergyCostContract`](../tests/CardEnergyCostContract/README.md)。
効果適用・ターン遷移の実機検証とは区別する。

### 次回プレイ時の廃棄フラグ（追加、schema 3）

`exhaust_on_next_play` は `CardModel.ExhaustOnNextPlay` の現在の bool 値。
戦闘中の `player.hand[]`、`draw_pile[]` / `discard_pile[]` / `exhaust_pile[]` / `play_pile[]`、
および `card_select.cards[]` の grid 行に出す。grid は `uid` と同じ条件、
すなわち `CombatManager.IsInProgress` が true で combat state が取得できる場合だけ。
戦闘外のデッキ・選択画面、choose-a-card 行、`preview_cards` / `upgrade_preview` には出さない。
共通の `BuildCardInfo` には追加せず、`BuildCardState` / `BuildPileCardList` /
`BuildCardSelectState` の戦闘用の行で読む。正常時は false も出し、
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
  "type": "Buff",            // "Buff" or "Debuff"
  "description": "Increases attack damage by 3.",
  "keywords": [ /* Keyword Objects */ ]
}
```

画面に見えるパワーには次の内部状態を追加する（schema 4）。
可視性が不明、または `hidden_in_ui` のパワーには追加しない。
各項目の意味・人間が知りうる根拠・非公開の範囲は
[パワーの内部状態](power-internal-state.md) を参照。

| 対象 | 追加項目 | JSON の型 |
|---|---|---|
| 全可視パワー | `skip_next_duration_tick` | bool |
| `TORIC_TOUGHNESS_POWER` | `block_base_value` | decimal |
| `SURROUNDED_POWER` | `facing` | enum 名 `Right` / `Left` |
| `POSSESS_STRENGTH_POWER` | `stolen_strength` | `[{"player_id":"17","amount":-2.75}]` |
| `POSSESS_SPEED_POWER` | `stolen_dexterity` | 同上 |
| `SKITTISH_POWER` | `has_gained_block_this_turn` | bool |
| `JUGGLING_POWER` | `attacks_played_this_turn` | int |

盗んだ量はゲームの符号付き値（減少なので負数）。`player_id` は既存の player と同じ
文字列 ID。空の配列は盗んだ記録なしを表し、合算・正数化・既定値補完はしない。
読めない項目は `null`、同じ status 行の `unavailable_reasons.<key>` に理由、
トップレベル `warnings` に `status.<POWER_ID>.<key>` を出す。他項目・説明失敗時の
最小行でも内部状態の読取を続ける。false / 0 と不明を区別する。
Markdown は既存行に短く追記し、通常の skip=false と空の盗難配列は省略する。
例は `(facing: Left)`、`(stolen strength: 17: -2.75)`。
失敗は `null (理由)` と表示する。解決中はフック間の値なので行動計画は解決後に行う。
既存項目の意味を変えない追加のため schema は 4 のまま。

### Keyword Object

```jsonc
{
  "name": "Vulnerable",
  "description": "Takes 50% more damage from attacks."
}
```

---

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
既存の文言欄の取得修正と Markdown への表示追加なので schema は 3 のまま。

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
        "hp": 44,
        "max_hp": 44,
        "block": 0,
        "status": [ /* Power Objects */ ],
        "intents": [
          {
            "type": "Attack",          // Attack, Defend, Buff, Debuff, Sleep, etc.
            "label": "11",             // Damage number or short label
            "title": "Attack",         // Hover tip title
            "description": "Deals 11 damage."  // Hover tip description
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
    ]
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

### `hand_select` — In-Combat Card Selection

Appears when a card effect prompts "Select a card to exhaust/discard/upgrade". **Not** an overlay — happens within the combat hand.

```jsonc
{
  "state_type": "hand_select",
  "hand_select": {
    "mode": "simple_select",     // "simple_select" or "upgrade_select"
    "prompt": "Select a card to Exhaust.",
    "cards": [
      {
        "index": 0,
        "id": "STRIKE_R",
        "name": "Strike",
        "type": "Attack",
        "cost": "1",
        "star_cost": null,       // Regent star cost as string, null if N/A
        "description": "Deal 6 damage.",
        "is_upgraded": false,
        "keywords": [ /* Keyword Objects */ ]
      }
    ],
    "selected_cards": [          // Only present if cards have been selected
      { "index": 0, "name": "Defend" }
    ],
    "can_confirm": false
  },
  "battle": { ... },  // Full battle state included for context
  "run": { ... },
  "player": { ... }
}
```

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
in the full grid's display order (top to bottom, left to right). The zero-based `index` addresses
that full list, including cards without a generated holder; no scrolling is required. Scrolling
or hovering does not change indices. Re-read state after candidates change, are reordered, or the
screen changes; an index is not a persistent card identifier. Same-ID instances stay separate.
`cards: []` means no candidates. `cards: null` with a `card_select.cards` warning means the candidate
list could not be read; do not select using a partial or stale list. Schema 4 removes
`selected_outside_grid`: selection marks now cover the full list. A mismatch with `selected_count`
produces a `card_select.selected` warning without rewriting the game's selection.
Combat-pile screens retain their game class name as `screen_type` (e.g. `NCombatPileCardSelectScreen`).

During a live combat with an available combat state, grid selection rows (`cards[]`) also carry
`uid`, using the same per-card-instance registry as the combat hand and piles. This includes
combat-pile selection grids. Markdown displays the uid after the row index, as in the hand.
Outside combat, `uid` is omitted. Choose-a-card (`screen_type: "choose"`) rows and `preview_cards`
do not carry uids; the repository code does not establish that a generated choice remains the
same card instance when it enters the hand. Selection actions still use `index`.

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
    "can_cancel": true           // true when close/cancel button is available

    // For "choose" type: picking is immediate (no confirm needed).
    // can_skip indicates if a skip button exists.
  },
  "run": { ... },
  "player": { ... }
}
```

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

**Errors:** Empty slot, combat-only potion used outside combat, automatic potion, already queued, player dead.

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
{ "action": "combat_select_card", "card_index": 0 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `card_index` | int | Yes | Index in the selectable hand cards |

### `combat_confirm_selection`

Confirm the in-combat hand card selection.

```json
{ "action": "combat_confirm_selection" }
```

**Errors:** No selection active, confirm button not enabled (select more cards first).

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
{ "action": "choose_map_node", "index": 0 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `index` | int | Yes | 0-based index from `next_options` |

### `select_card`

Select a card in a card selection overlay.

```json
{ "action": "select_card", "index": 0 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `index` (alias `card_index`) | int | Yes | 0-based index in the latest full `card_select.cards` candidate list; no scrolling required |

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
because its selection is already full. A hidden/peeking grid, unavailable candidate list, invalid index,
or missing click method also returns error. A click exception returns error with its underlying cause;
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
