# 本家との差分（この fork で直した・足したこと）

この fork（`cat12079801/STS2MCP`）が本家 [`Gennadiyev/STS2MCP`](https://github.com/Gennadiyev/STS2MCP) の `main` と何が違うかを、
機能・API・state の項目ごとに現在形でまとめる。変更履歴ではない。経緯は各 PR に、API の正確な契約は
[`raw-simplified.md`](raw-simplified.md) と [`raw-full.md`](raw-full.md) にある。

- **基点**: 本家 `main` の `55e0648`（Fix game API compatibility with STS2 v0.107, #123）。本家はそこから進んでいない（この文書を書いた時点）
- **方針**: 本家には PR も issue コメントも出さない。この fork の内部の記録として持つ（使う側のリポジトリ `cat12079801/sts2` の方針）
- **PR 番号**: `#N` はこの fork の PR（`cat12079801/STS2MCP#N`）。本家の PR は「本家 #N」と書く。
- **更新のしかた**: MOD の PR で差分を足した・変えたら、同じ PR でこの文書の該当する項目を書き換える。
  使う側で見つけた未解決の問題は `cat12079801/sts2` の `MCP_ISSUES.md` にあり、直したらそちらから消してここに書く

## ゲームの版

- **v0.113.0 に対応する。** 商人の在庫取得と購入は `MerchantSynchronizer` を通す（`Task<bool>` の失敗・選択待ちを扱う）。
  多段攻撃の回数は `HitCount` → `CalculatedHits` → `Repeat` の順で解決する。詳細は [`v0.113-compat.md`](v0.113-compat.md)。参照 DLL を新版にしてビルドするので、旧版向けのバイナリ互換は無い（#74）
- **v0.111 のロビー対応は本家 #132 の方式を取り込んでいる**（`LoadRunLobby.IsAboutToBeginGame()`。本家 `main` には未 merge）（#43）

## HTTP・応答の共通

- **版の情報**: すべての state と `GET /` が `mod_version` / `mod_commit` / `schema_version` を返す。`mod_commit` は焼いた木の git の版で、汚れた木なら `-dirty` が付く（#47）。
  state はゲームの `release_info.json` から `game_version` / `game_commit` も返す（#74）
- **`schema_version`**: 既存の項目・アクションの意味を変えたり消したりしたときだけ上げる（足すだけなら上げない。例外は 6・7・8）。現在は `8`。
  2 = 死亡中の敵を `enemies[]` に残す（#55）、3 = `map.next_encounters` の撤去（#57）、4 = `card_select` の候補と index を全候補にした（#73）、
  5 = 戦闘の全行の `upgrade_preview` を常時の status 付きにした（`cat12079801/sts2#1003`）、
  6 = 敵の行動の入力契約（追加だけだが、読み手が一式を必須として扱えるように上げた。`cat12079801/sts2#1018`）、
  7 = 札の効果の入力契約（同じく追加だけ。`cat12079801/sts2#1019`）、
  8 = 弱化（Dampen）の入力契約（同じく追加だけ。`cat12079801/sts2#1031`）
- **POST の応答は必ず `status` を持つ**（転送の失敗も含む）（#50）
- **メインスレッド待ちの上限**: メインスレッドが 10 秒応答しないか、待ちの列が 32 件を超えたら、アクションを実行せず 503 と `retry: true` を返す。クライアントが諦めた要求は捨てる（#51）
- **`wait: true`**: POST がゲームの落ち着くまで応答を保留し、落ち着いた state ごと返す（`settled` / `changed` / `waited_ms` / `polls` / `state`。`wait_timeout_ms` で上限）（#52）
- **`is_resolving` / `resolving_reasons`**: アクションの解決中かを state で示す。呼び出し側の入力を待っている画面では false（#39・#41）。
  手番の初めのドローと手番開始時のフックの間も、戦闘の画面では `player_turn_starting` で解決中と返す（ゲームはこの間 `PlayerActionsDisabled` を戻し、action の列の外でドローする）（#87）
  戦闘の部屋に入ってから戦闘が始まるまでと、戦闘が終わってから報酬画面などが出るまでの `battle` の無い state も、`combat_starting` / `combat_ending` で解決中と返し、
  `message` を局面に合わせる（開始中は `Combat is starting...`。本家は開始中も `Combat ended. Waiting for rewards...` と返す）（#88）
  マップの移動中（`map_traveling`）・閉じる演出の間（`map_closing`）の `map` と、ラン中の部屋の切り替え（`room_changing`）・読み込み中（`run_loading`）の `unknown` も
  解決中と返し、`wait: true` もそこで落ち着いたとみなさない（本家はどれも `is_resolving: false` で返し、`choose_map_node` / `continue` の `wait: true` が途中で返る）（#89）
- **`warnings`**: state の一部を組めなかったとき、その箇所と例外を一覧で返す（黙って欠かさない）（#54）
- **JSON は既定でコンパクト**。`?pretty=1` で字下げ（#45）
- **`GET /api/v1/singleplayer?passive=1`**: 読むだけで UI に触らない（ショップの在庫を開き直す・偽商人や宝箱のボタンを押す、をしない）（#59）
- **ブラウザからの要求を拒否する**: `Origin` や `Sec-Fetch-*`・`Referer` がある要求は処理前に 403。`Host` は `127.0.0.1:<port>` / `localhost:<port>` だけ。POST は JSON の `Content-Type` が必須（#69）
- **開発コンソール API（検証用・既定は無効）**: `STS2_MCP.conf` の `dev_console_enabled: true` で、許可リスト内のコマンドを `dev_console` アクションで送れる。詳細は [`dev-console.md`](dev-console.md)（#68）

## パラメータと識別子

- **`entity_id` は戦闘中に固定**（初見順に割り当て、撃破しても振り直さない）。数値の `combat_id` も出し、`target` にはどちらでも渡せる（#1）
- **カードの `uid`**（例 `STRIKE_IRONCLAD#…`）: 手札・山札・捨て札・廃棄札の全部で戦闘中不変。`play_card card_uid <uid>` で撃てる（#38）。
  uid の番号は乱数で振り、山札の引く順を漏らさない（#79）
- **index 系のパラメータ名の別名**: カード系は `card_index` / `index` の両方を受ける。文字列の数値も受ける。エラー文に受理する名前を書く（#25）
- **ポーションのスロット**: `use_potion` / `discard_potion` は `slot` / `potion_index` / `index` を受ける。`slot` はポーション欄の固定番号。空スロットのエラーは欄の中身を返す（#26）
- **対象の要るポーション・カード**: `target` を省くと、敵が 1 体なら自動で選び、複数ならエラー（黙って消費しない）（#27）
- **マップ**: `choose_map_node` は `col` / `row` を受ける。到達できない座標はエラーで、到達できるノードを列挙する（#29）

## メニュー・ランの制御

- **年代記の開封**: `timeline_reveal_epochs` で未開封の断章を 1 段階ずつ開封し、解放画面を確かめてメインメニューへ戻る。`done: true` まで繰り返す。開封は `NEpochSlot.RevealEpoch` を通る（#21）。
  メインメニューの state の `message` が未開封の本数を示す
- **アセンション**: `menu_select` に `ascension` を渡せる。`character_select` の state に `ascension` / `max_ascension` / `ascension_selectable`（#30）
- **一時停止メニュー**: `open_pause_menu`（別名 `pause`）で開き、`menu_screen: "pause"` として選択肢（`resume` / `settings` / `compendium` / `give_up` / `save_and_quit` など、見えているボタンだけ）を返す。
  開いている間は、ゲーム操作のアクションをエラーにする（#53）
- **設定画面**: `menu_screen: "settings"` と `in_run` を返し、`menu_select option back` で必ず出られる（設定値そのものは変えられない）（#48）
- **確認ポップアップ**: タイトルと本文を state と markdown に出す（#67）

## 戦闘

- **`vs_targets`**: 手札の敵対象カードに、敵ごとの解決後ダメージ・ヒット数・その敵向けの本文を出す（弱体・脱力・筋力込み。UI のホバーと同じ経路）（#37・#42）
- **戦闘の `upgrade_preview`（手札と全パイル）**: すべての行に、その札そのものを1段強化した結果を `status`（`ready` / `not_upgradable` / `unavailable`）付きで出す。
  `ready` は隔離した複製に `UpgradeInternal` → `FinalizeUpgradeInternal` を当てた絶対値（数値変数の BaseValue・型・対象・キーワード〔local と実効〕・`energy_cost`・`star_cost_state`・付与）。
  複製は現物の購読を外した浅いコピーから作り、UID・パイル・履歴・乱数に触れない。行には `upgrade_level` / `max_upgrade_level` / `local_keyword_ids` / `keyword_ids` / `retain_this_turn` / `single_turn_retain` も出す。
  `?perf=1` で構築時間と複製数を返す。[入力契約と出典](raw-full.md#戦闘の強化結果upgrade_previewschema-5)（`cat12079801/sts2#1003`）
- **札の詳細（手札と全パイル共通）**:
  - `type` / `target_type`、`affliction`、`enchantment`（`{id, amount, status}`。モメンタムは `extra_damage` も）、`dynamic_vars`（現在の `BaseValue`）（#77・#83）
  - 呪詛（`HEXED`）の札だけ `local_ethereal`: 札自身のエセリアル（`GetKeywordsWithSources(KeywordSources.Local)`）。呪術が Global に足すエセリアルを含まないので、呪術が外れた後も残るかが分かる。読取失敗は null・理由・warnings（`cat12079801/sts2#990`）
  - `is_upgraded` / `is_upgradable`、`tags`、パイルにも `can_play` / `unplayable_reason`（#82）
  - 札の効果の入力: `retain_this_turn` / `single_turn_retain` / `sly_this_turn` / `single_turn_sly`（単ターンの保留・Sly の期限。private のフラグ）、
    `base_replay_count` / `extra_replay_this_turn` / `enchanted_replay_count`（`GetEnchantedReplayCount`。Glam の `Disabled` は回数に反映済み）、
    Mad Science の行だけ `tinker_time_rider`、`enchantment` に付与自身の `dynamic_vars`（Glam / Spiral の `Times`）。全行・`ready` に必ずあり、
    false・0 は実値、読取失敗は null・理由・warnings（`ready` は `unavailable`）。読むだけで、付与の数値変数が未作成なら作らずに失敗とする。
    [入力契約と出典](raw-full.md#札の効果の入力schema-7)（schema 7、`cat12079801/sts2#1019`）
  - `energy_cost`: コストの修正（local / global、種別・期限）と、素のコスト・X の捕捉値（#75・#80）
  - `exhaust_on_next_play`（#72）
  - `star_cost_state`: スターコストの基底・canonical・X か・現在値と、一時コストの列（`TemporaryCardCost` の値・ターン終了/プレイで消えるか）を順に。履歴から作らない。[入力契約と出典](raw-full.md#star-cost-state-object追加schema-4)（#95、`cat12079801/sts2#958`）
  - 山札の表示順はゲームの並べ方（`Rarity` → `CardModel.CompareTo`）で、同順位は uid の順。引く順を漏らさない（#79）
- **戦闘中の `card_select`**: 戦闘のパイルから選ぶ行に、戦闘中の `uid` を出す（#70）
- **戦闘中の選択候補の個体（`combat_instance`）**: 戦闘中の `card_select`（grid・choose）・`hand_select`・`card_reward` の候補行に、
  戦闘のパイルのちょうど1つにある札は `{source: "pile", pile, uid}`（パイルの行と同じ uid）、パイルに無い候補（3 択の生成札など）は
  `{source: "detached", pile, values}`（uid を発行せず、型・段階・数値・付与・キーワード・単ターンの期限・リプレイ・コストを候補から直接読む。
  可否・対象は評価しない）を付ける。2 つのパイルにある札は対応付けず理由付きの null。`index` と画面の識別は変えない。戦闘外の画面は従来どおり。
  [入力契約と出典](raw-full.md#選択候補の個体combat_instanceschema-7)（schema 7、`cat12079801/sts2#1019`）
- **戦闘中の choose-a-card（3 択ポーションなど）**: 候補の実個体に手札と同じ `uid`・`energy_cost`・`star_cost_state`・札の詳細と `can_play`・`exhaust_on_next_play` を付け、画面に `battle` を付ける。
  選択後の継続（生成時・使用後・空手札の通知）に参加する listener を `card_select.continuation_context` に、候補自身の override を `cards[].continuation_listeners` に出す（通知の hook は呼ばない）。山札にある listener は引く順を漏らさないよう `order: null` と山札のまとまりの開始位置で出し、表示順に並べる。候補の `energy_cost` には `global_modifiers_applied`（パイルに入る前は false）を付ける。
  GET で選択・無料化・生成・抽選をしない。`index` は `select_card` と同じ holder の位置で、札の無い holder は選ばない。版は上げていない（schema 4 のときの追加）。[入力契約と出典](raw-full.md#戦闘中の-choose-a-card追加schema-4)（#95、`cat12079801/sts2#958`）
- **死亡中の敵**: 復活を待つ敵（再結合など）を `alive: false` で `enemies[]` に残し、`revive: {in_turns, hp, power_id, power_name}` を付ける（#55）
- **敵に当てられるか**: `battle.enemies[].hittable` に `Creature.IsHittable`（死亡なら false、それ以外は `Hook.ShouldAllowHitting`。復活中の体節・幻影・実験体は false）の bool をそのまま出す。生死で絞らず全ての敵の行に付け、HP・alive・パワー名から作らない。読めなければ null・理由・warnings。Markdown の HP 行に `Hittable`。版は上げていない（schema 4 のときの追加）。[入力契約と出典](raw-full.md#敵に当てられるかhittable追加schema-4)（#95、`cat12079801/sts2#941`）
- **敵の行動状態**: 全モンスターの `battle.enemies[].move_state` に現在の行動 ID・follow-up ID・ゲームの行動ログ末尾 ID・遷移可否を公開する。スタン時も実際の復帰先とロックを保持。四値の取得成功時だけ `readable=true`、失敗項目は null・項目別理由・個体別 warnings とし、履歴から推測しない。分岐の事前抽選やゲームの状態変更はせず、Markdown に ID と follow-up を表示する。単独・協力プレイと死亡中の敵で共通、版は上げていない（schema 4 のときの追加）。[入力契約と出典](raw-full.md#敵の行動状態追加schema-4)（#91、`cat12079801/sts2#910`）
- **敵の戦闘内部状態**: 滝の巨人の `battle.enemies[].combat_state` に `is_about_to_blow`（private `_isAboutToBlow`）・`steam_eruption_damage`（private `_steamEruptionDamage`）・`current_pressure_gun_damage`（private `_currentPressureGunDamage`）と、getter の現在値 `steam_boost_amount`（`SteamBoostAmount`。Dread 込み）・`pressurize_amount`・`stomp_damage`・`ram_damage`・`pressure_up_damage`・`siphon_heal`（人数を掛ける前）を公開する。false・0 も実値で、HP・行動 ID・Ascension から作らない。ラン外（getter が Ascension を無視する）・負の値は null。読取失敗は null・項目別理由・warnings。Markdown に `Combat state:` 行を表示。他の敵には出さない。版は上げていない（schema 4 のときの追加）。[入力契約と出典](raw-full.md#敵の戦闘内部状態追加schema-4)（#92・#94、`cat12079801/sts2#954`・`cat12079801/sts2#968`）
- **敵の行動の入力**（schema 6）: ソースから生成した敵の行動表を現在の状態から評価するための入力を、読むだけで公開する。
  `move_state` に `has_follow_up`（終端の行動は false の実値）・`must_perform_once`・状態機械の現在地点と初期地点（`current_state_id` / `current_state_registered` / `initial_state_id`）・
  `performed_first_move`・`spawned_this_turn`・行動ログの全要素 `state_log`（`[{id, is_move}]`、空は `[]`、途中までの配列は返さない）・
  `is_stun` / `stun_handler`（`CreatureCmd.Stun` の Wrapper が包んだ処理を、宣言型・delegate 型を検査して `CreatureCmd.Stun.no_op`・`SlumberingBeetle.WakeUpMove` などの許可リストに対応付ける。未対応は null と理由）・`inputs_readable`。
  敵の行に `slot_name`、`battle.enemy_move_context` に Ascension の段階（`RunManager.HasAscension`）・`is_dread`・`game_mode`・人数・遭遇のスロットと占有個体・本体 DLL の SHA-256。
  `combat_state` を行動の分岐・効果量に効く private の現在値の敵（`Nibbit`・`Toadpole` の前列、`Fabricator` の直前の召喚、`TwoTailedRat` の召喚の待ち・回数、`TestSubject` の復活回数など 12 種）に広げ、
  儀式の status 行に `was_just_applied_by_enemy`。`GetNextState`・`RollMove`・乱数・delegate は呼ばない。項目ごとに成否（null・理由・warnings）を持つ。
  [入力契約と出典](raw-full.md#敵の行動の入力schema-6)（`cat12079801/sts2#1018`）
- **敵の攻撃の意図**: `battle.enemies[].intents[]` の Attack / DeathBlow（`AttackIntent`）に `attack: {readable, base_damage, hits, resolved_damage}` を公開する。`base_damage` は現在の `AttackIntent.DamageCalc()` の補正前の decimal（invariant culture の文字列）、`hits` は `Repeats`、`resolved_damage` はローカルのプレイヤーが対象のときの `GetSingleDamage`（筋力・脆弱・脱力など込み）。ラベル・本文から逆算しない。ローカルのプレイヤーが居ないときの補正前の値を解決値として出さない。三値の取得成功時だけ `readable=true`、失敗項目は null・項目別理由・個体と意図の位置を識別する warnings。Markdown の意図の行に `[attack: base … | hits … | resolved …]` を表示。単独・協力プレイと死亡中の敵で共通、版は上げていない（schema 4 のときの追加）。[入力契約と出典](raw-full.md#敵の攻撃の意図追加schema-4)（#93、`cat12079801/sts2#956`）
- **パワー（`status[]`）**:
  - `amount` は実効のスタック数（`PowerModel.Amount`）。UI の表示値と違うときだけ `display_amount`（#23）
  - 説明の解決に失敗しても、パワーを捨てずに id・名前・スタック数を返す。UI に出ないが残っているものは `hidden_in_ui`（#24）
  - 観測できる内部状態を許可リストで出す（持続のスキップ・ブロックの基底値・包囲の向き・盗んだ量・拘束の鎖の使用済み・まるくなるが追跡中の札の uid・泥棒が盗んだゴールドと泥棒・強奪の対象 player など）。泥棒・強奪の対象別の項目は `hidden_in_ui` の行にも付ける。活力の `vigor_attack`（private `VigorPower.Data` の攻撃との結合の有無・結合先の攻撃者が所有者か・開始時の量。未結合と読取失敗を区別）を player・敵、`hidden_in_ui`・最小行の活力に付ける。呪術の付与元（`PowerModel.Applier` の `CombatId` を `applier_combat_id`、読取の成否を `applier_readable`。付与元なしと読取失敗を区別）を可視性によらず付ける（`cat12079801/sts2#990`）。儀式の `was_just_applied_by_enemy`（`cat12079801/sts2#1018`、schema 6）も可視性によらず付ける。爆弾の `damage`（`SetDamage` の威力）、インフェルノ・深紅の衣の `self_damage`、威風堂々（Panache）の `cards_left` / `already_applied`（`cat12079801/sts2#1019`、schema 7）も可視性によらず付ける。マギナイトの弱化（`DAMPEN_POWER`）の private `DampenPower.Data` を、付与した敵の `caster_combat_ids`（`enemies[].combat_id`、昇順）と、弱化で下げた札の `downgraded_cards`（戦闘の uid registry の uid と元の強化段階。registry に無い札は uid を新たに振らず、行ごとに `uid_known=false` と理由）として可視性によらず付ける（`cat12079801/sts2#1031`、schema 8）。schema 4 の項目は版を上げずに足した。schema 6・7・8 の項目は版を上げて足したので、版で必須にしてよい。詳細は [`power-internal-state.md`](power-internal-state.md)（#78・#81・#86・#92・#96、`cat12079801/sts2#980`）
- **戦闘の履歴の要約**（`player.combat_history`）: 開始・完了・廃棄・被ダメ・ブロック獲得の件数、前の手番の uid、この手番の束縛の付与（#76・#81）。
  この手番の開始・完了の明細 `card_play_events_this_turn`（履歴順。各件に uid・自動プレイの印・実際の対象の `combat_id` / `entity_id`・`play_index` / `play_count`）。荒廃などの自動プレイの照合用。読めなければ配列ごと null（#95、`cat12079801/sts2#937`）
- **生成の候補**:
  - `player.generation_pool`: オロビックの酸液の抽選前の候補。本体の `CardModel.IsAllowedInRun`（`CardFactory.FilterForRun`。人数と Blitz）→ `FilterForCombat` を通した一覧で、`filter_version: 2`（#63・#95、`cat12079801/sts2#940`）
  - `player.colorless_generation_pool`: 何でも屋の抽選前の候補（無色の解放済み pool から自身を除き、`IsAllowedInRun` → `FilterForCombat`）。戦闘中だけ（#95、`cat12079801/sts2#940`）
  - `player.unlocked_pools`: 何でも屋・ジャックポット・錬金術と同じ関数で取った、解放済みのカード・無色カード・ポーションの ID（戦闘中だけ）（#71）
- **`end_turn` と `discard_potion` は本体の UI と同じ GameAction を積む**（`EndPlayerTurnAction` / `DiscardPotionGameAction`）。本体の戦闘リプレイに残る（#61）

## レリック

- `used_up`（`RelicModel.IsUsedUp`）と `melted`（#62）
- `vars`: 所有レリックの `DynamicVars` の名前 → 現在の値（戦闘中・戦闘外）（#66）
- `combat_state`: 戦闘中の内部状態（使用フラグ・カウンタ）をクラス別の許可リストで出す。詳細は [`relic-combat-state.md`](relic-combat-state.md)（#65・#78）
- **パエルの眼の手動プレイ判定**は `player.combat_history.any_manual_cards_played_this_turn`（bool）に出す。v0.113.1 の `PaelsEye.AnyCardsPlayedThisTurn` と同じ Actor・手番・非 AutoPlay の完了履歴条件を読み、false も保持する。取得失敗は null・理由・warnings。`WhisperingEarring` 所持時の初手番の例外は所持レリック・`turn_number` と合わせて評価する。版は上げていない（schema 4 のときの追加）（#90、`cat12079801/sts2#911`）

## 報酬

- **`reward_id` / `parent_reward_id` と `screen_revision`**: 報酬に安定した ID を付け、古い一覧での取得を拒否する。カード報酬のスキップ状態（`skipped`）を出す（#64）
- **ポーション欄が満杯なら `claim_reward` はエラー**（成功を返して何もしない、をしない）（#28）
- **カード報酬の別の選択肢**: `card_reward.alternatives`（`option_id` / `title` / `enabled`）を出し、`select_card_reward_alternative option_id <ID>`（パエルの翼の `SACRIFICE` など）で選べる。`skip_card_reward` は id で `Skip` を選ぶ（#40）

## 戦闘外の画面

- **デッキ**: 戦闘外の全画面で `player.deck`、全画面で `deck_count`（#49）
- **`upgrade_preview`**: 強化画面・強化モードの `hand_select`・`card_reward` のカードに強化後の本文・コスト（#44）。`status` は持たない（戦闘の行の契約とは別）。現物の札から作る複製も、戦闘と同じく購読を外して作る
- **デッキからのカード選択（`card_select`）**:
  - 候補と `select_card` の index は画面の全候補（`NCardGrid._cards`）から作る。画面外の札も選べる（#73）
  - `cards[].selected` / `selected_count` / `required_count` / `min_select` / `max_select` と `screen_type` を出す。`select_card` はトグル。
    `confirm_selection` は枚数の不一致・空の選択をエラーにする。エンチャント画面のプレビュー板も扱う（#56）
  - マップ（または戦闘中の覗き見）に隠れた選択画面は、`select_card` / `confirm_selection` / `cancel_selection` が、人の操作と同じ経路（マップの戻る矢印・覗き見の解除）で 1 回だけ表に戻してから続け、外したものを `revealed` で返す。矢印が無効・移動中・capstone・原因不明なら何も押さずに error。state に `screen_visible` / `hidden_by` / `reveal_on_action`（#84）
- **休憩所**: `choose_rest_option` は `option_id`（`HEAL` / `SMITH` / `DIG` …）を受け、id で選択肢を決めてからボタンを引く（並び順に依存しない）。`options[].has_button` を出す（#46）
- **ショップ**: `sell_potion slot N` で悪臭ポーションなどを商人に売れる。売れるものは `shop.sellable_potions`（#20）
- **マップ**:
  - `map.encounters_seen`: この層でもう戦った通常戦・エリート・イベント（`elite_cycle` 付き）。次の遭遇（隠し情報）は出さない（#34 で足した `next_encounters` を #57 で撤去した）
  - レリックが付けた印: ノードの `marked_by` / `quests` / `can_be_modified`、`map.marked_nodes`（#33）
  - markdown に `## Map Graph`（行ごとの隣接リスト）（#35）と `**Boss:**` / `**Boss candidates:**`（#22）
  - `choose_map_node` は、人のクリックと同じく選択を受け付けるマップでだけ押す。閉じる演出中・移動中・部屋が終わる前に開いた（`IsTravelEnabled` が false の）マップでは何も押さずに error。
    `map.travel_enabled` で移動が有効かを返す（本家は閉じかけのマップや、トップバーで開いたマップからも移動できる）（#89）

## 索引・用語

- **キーワードの説明**: 説明が空なら `CanonicalModel` の本文に落ち、それも無ければ `description_missing` を立てて用語集に必ず載せる（#31）
- **`search_wiki` の `scope=all`**: 未発見のカード・レリックも引ける（既定は発見済みだけ）。結果に `discovered`（#32）

## markdown

- `message` を持つ画面で、その 1 行だけ出して残りを捨てることをしない（宝箱画面が空になっていた）。敵は `combat_id` で重複を除く（#36）
- 上に挙げた項目（ボス・マップのグラフ・`→ Upgraded:` 行・死亡中の敵・札の詳細の子行など）は markdown にも出る

## 戦闘の記録器

- 本体の戦闘リプレイ（`CombatReplay`）を写し、API 要求との対応・選択の親子・ターンの文脈・公開観測を足して、`record_dir`（既定 `~/.local/state/sts2/records`）に `public.jsonl` / `hidden.jsonl` で残す。
  本体の処理は変えない（読むだけの patch）。`STS2_MCP.conf` の `record`（既定 `true`）、実行時の `set_recording`、`GET /api/v1/record` / `GET /api/v1/record/inspect`。
  詳細は [`recording.md`](recording.md)、検査は `tools/record_check.py`（#61）

## MCP サーバー（`mcp/server.py`）

- 足したツール: `open_pause_menu`・`timeline_reveal_epochs`・`sell_potion`・`rewards_card_alternative`
- 足した引数: `menu_select` の `ascension`、`search_wiki` の `scope`、`combat_play_card` の `card_uid`、`rest_choose_option` の `option_id`、
  `combat_play_card` / `combat_end_turn` / `use_potion` / `proceed_to_map` の `wait`

## 試験

- `tests/*Contract/` に、ゲームの DLL を使わない契約試験を置いている（札の詳細・コスト・戦闘の強化結果・uid・カード選択・戦闘中の choose-a-card・敵に当てられるか・戦闘の履歴・開発コンソール・HTTP の要求の検査・パワーとレリックの内部状態・報酬・生成の候補・v0.113 対応・画面の遷移の解決中）
