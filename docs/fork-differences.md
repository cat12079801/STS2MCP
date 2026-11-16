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
- **`schema_version`**: 既存の項目・アクションの意味を変えたり消したりしたときだけ上げる（足すだけなら上げない）。現在は `4`。
  2 = 死亡中の敵を `enemies[]` に残す（#55）、3 = `map.next_encounters` の撤去（#57）、4 = `card_select` の候補と index を全候補にした（#73）
- **POST の応答は必ず `status` を持つ**（転送の失敗も含む）（#50）
- **メインスレッド待ちの上限**: メインスレッドが 10 秒応答しないか、待ちの列が 32 件を超えたら、アクションを実行せず 503 と `retry: true` を返す。クライアントが諦めた要求は捨てる（#51）
- **`wait: true`**: POST がゲームの落ち着くまで応答を保留し、落ち着いた state ごと返す（`settled` / `changed` / `waited_ms` / `polls` / `state`。`wait_timeout_ms` で上限）（#52）
- **`is_resolving` / `resolving_reasons`**: アクションの解決中かを state で示す。呼び出し側の入力を待っている画面では false（#39・#41）。
  手番の初めのドローと手番開始時のフックの間も、戦闘の画面では `player_turn_starting` で解決中と返す（ゲームはこの間 `PlayerActionsDisabled` を戻し、action の列の外でドローする）（#87）
  戦闘の部屋に入ってから戦闘が始まるまでと、戦闘が終わってから報酬画面などが出るまでの `battle` の無い state も、`combat_starting` / `combat_ending` で解決中と返し、
  `message` を局面に合わせる（開始中は `Combat is starting...`。本家は開始中も `Combat ended. Waiting for rewards...` と返す）（#88）
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
- **手札の `upgrade_preview`**: 手札を強化できる手段があるターンだけ、手札の各札に強化後の本文・コストを付ける（#58）
- **札の詳細（手札と全パイル共通）**:
  - `type` / `target_type`、`affliction`、`enchantment`（`{id, amount, status}`。モメンタムは `extra_damage` も）、`dynamic_vars`（現在の `BaseValue`）（#77・#83）
  - `is_upgraded` / `is_upgradable`、`tags`、パイルにも `can_play` / `unplayable_reason`（#82）
  - `energy_cost`: コストの修正（local / global、種別・期限）と、素のコスト・X の捕捉値（#75・#80）
  - `exhaust_on_next_play`（#72）
  - 山札の表示順はゲームの並べ方（`Rarity` → `CardModel.CompareTo`）で、同順位は uid の順。引く順を漏らさない（#79）
- **戦闘中の `card_select`**: 戦闘のパイルから選ぶ行に、戦闘中の `uid` を出す（#70）
- **死亡中の敵**: 復活を待つ敵（再結合など）を `alive: false` で `enemies[]` に残し、`revive: {in_turns, hp, power_id, power_name}` を付ける（#55）
- **パワー（`status[]`）**:
  - `amount` は実効のスタック数（`PowerModel.Amount`）。UI の表示値と違うときだけ `display_amount`（#23）
  - 説明の解決に失敗しても、パワーを捨てずに id・名前・スタック数を返す。UI に出ないが残っているものは `hidden_in_ui`（#24）
  - 観測できる内部状態を許可リストで出す（持続のスキップ・ブロックの基底値・包囲の向き・盗んだ量・拘束の鎖の使用済み・まるくなるが追跡中の札の uid など）。詳細は [`power-internal-state.md`](power-internal-state.md)（#78・#81・#86）
- **戦闘の履歴の要約**（`player.combat_history`）: 開始・完了・廃棄・被ダメ・ブロック獲得の件数、前の手番の uid、この手番の束縛の付与（#76・#81）
- **生成の候補**:
  - `run.generation_pool`: ランの解放済みの戦闘生成の候補（#63）
  - `player.unlocked_pools`: 何でも屋・ジャックポット・錬金術と同じ関数で取った、解放済みのカード・無色カード・ポーションの ID（戦闘中だけ）（#71）
- **`end_turn` と `discard_potion` は本体の UI と同じ GameAction を積む**（`EndPlayerTurnAction` / `DiscardPotionGameAction`）。本体の戦闘リプレイに残る（#61）

## レリック

- `used_up`（`RelicModel.IsUsedUp`）と `melted`（#62）
- `vars`: 所有レリックの `DynamicVars` の名前 → 現在の値（戦闘中・戦闘外）（#66）
- `combat_state`: 戦闘中の内部状態（使用フラグ・カウンタ）をクラス別の許可リストで出す。詳細は [`relic-combat-state.md`](relic-combat-state.md)（#65・#78）

## 報酬

- **`reward_id` / `parent_reward_id` と `screen_revision`**: 報酬に安定した ID を付け、古い一覧での取得を拒否する。カード報酬のスキップ状態（`skipped`）を出す（#64）
- **ポーション欄が満杯なら `claim_reward` はエラー**（成功を返して何もしない、をしない）（#28）
- **カード報酬の別の選択肢**: `card_reward.alternatives`（`option_id` / `title` / `enabled`）を出し、`select_card_reward_alternative option_id <ID>`（パエルの翼の `SACRIFICE` など）で選べる。`skip_card_reward` は id で `Skip` を選ぶ（#40）

## 戦闘外の画面

- **デッキ**: 戦闘外の全画面で `player.deck`、全画面で `deck_count`（#49）
- **`upgrade_preview`**: 強化画面・強化モードの `hand_select`・`card_reward` のカードに強化後の本文・コスト（#44）
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

- `tests/*Contract/` に、ゲームの DLL を使わない契約試験を置いている（札の詳細・コスト・uid・カード選択・戦闘の履歴・開発コンソール・HTTP の要求の検査・パワーとレリックの内部状態・報酬・生成の候補・v0.113 対応）
