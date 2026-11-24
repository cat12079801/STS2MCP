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

- **版の情報**: state の応答（`state_type` を持つもの。`menu`、マルチの `error`、`wait: true` の POST が返す `state` を含む）と `GET /` が `mod_version`（`GET /` は `version`）/ `mod_commit`（同 `commit`）/ `schema` / `schema_version` を返す。state の読み取りに失敗した 500、503 の `retry: true`、POST のアクションの結果とエラー、profile・図鑑・wiki のエンドポイントは返さない。`mod_commit` は焼いた木の git の版で、汚れた木なら `-dirty` が付く（#47）。
  state はゲームの `release_info.json` から `game_version` / `game_commit` も返す（#74）
- **`schema`**: state・アクションの契約の版を semver の文字列で返す。現在は `"1.17.0"`（`1.0.0` が整数の `schema_version` 12 の契約そのもので、`1.1.0` は敵の台詞の印の `combat_state`、`1.2.0` は棚卸しから生成した `internal_state`、`1.3.0` は全パワーの付与元・対象の `combat_id`、`1.4.0` は戦闘中の選択画面すべての `battle`、`1.5.0` は無色 4 札（金の斧・エントロピー・スプラッシュ・計略）の入力、`1.6.0` は戦闘全体の札の解決の通し列 `card_resolution_events`、`1.7.0` は敵の HP の無限表示 `hp_display_is_infinite`、`1.8.0` は札の解決の通し列への山札からの自動プレイの予約の行と、戦闘を終えた POST の応答の `ended_combat`、`1.9.0` は選択を待つ処理の鎖 `choice_source.await_chain`、`1.10.0` は出来事の棚卸しの public-missing（戦闘の出来事の通し列 `battle.combat_log`・全選択画面の `decision_context`・`player.potion_generation_pool`・手札の `energy_cost.x_value_if_played`）、`1.11.0` は敵のパワーを付けられるか `can_receive_powers`、`1.12.0` は戦闘の札の `internal_state.deck_version_id` と 4 パイルの行の `rarity`、`1.13.0` は醸造の候補・通知・使用履歴と歯のあるランの修飾子、`1.14.0` は試行と choose の識別子・初手ドロー前の待機段と残りの listener、`1.15.0` は多段攻撃用の戦闘履歴・戦闘の実カード個体の星 X・矢の枠と刺突、`1.16.0` は戦闘中のワッピーの個体行、`1.17.0` は提示中のエンシェント6候補の `deck_targets` を足す）。Markdown は先頭の行に `mod … · schema 1.17.0 (schema_version 12)` と出す。
  - MAJOR: 後方互換の無い変更（既存の項目の意味・型・形を変える、項目・アクション・列挙の値・`state_type` を消す・名前を変える、構造を変える）。クライアントは MAJOR が同じことだけを求める
  - MINOR: 後方互換の追加（項目・アクション・任意の引数・`state_type`、既存の列挙の項目〔`resolving_reasons`・`upgrade_preview.status`・`can_use_now_reason` など〕の新しい値を足す）。クライアントは足された機能を版で比べず、項目の有無と既存の `unavailable_reasons` / `warnings` の慣習で検出する。知らない値は未観測として安全側で止め、既知の値に読み替えない
  - PATCH: 契約を変えない直し（文書の意味のとおりでなかった値を正す、文書・表示の直し）
  - 版を独自に持つ部分契約（`generation_pool.filter_version`・`scenario_capabilities` の各値・Recorder の `sts2-mod-record/N`）は、その版で判定する。それらの版が変わる更新でも、`schema` は変更の中身で通常どおり動かす（項目の追加は MINOR、意味の変更は MAJOR）
  - 規則の正本は [`raw-full.md`](raw-full.md#common-top-level-fields)
- **`schema_version`（非推奨）**: 整数の版。12 で固定し、もう上げない。sts2 が `schema` を読むようになったあと、次の MAJOR（2.0.0）で消す。
  以下は整数の版の履歴（既存の項目・アクションの意味を変えたり消したりしたときに上げた。6〜12 は追加だけでも、読み手が一式を必須として扱えるように上げた）。
  2 = 死亡中の敵を `enemies[]` に残す（#55）、3 = `map.next_encounters` の撤去（#57）、4 = `card_select` の候補と index を全候補にした（#73）、
  5 = 戦闘の全行の `upgrade_preview` を常時の status 付きにした（`cat12079801/sts2#1003`）、
  6 = 敵の行動の入力契約（追加だけだが、読み手が一式を必須として扱えるように上げた。`cat12079801/sts2#1018`）、
  7 = 札の効果の入力契約（同じく追加だけ。`cat12079801/sts2#1019`）、
  8 = 弱化（Dampen）の入力契約（同じく追加だけ。`cat12079801/sts2#1031`）、
  9 = 破壊（Rend）の入力契約（同じく追加だけ。`cat12079801/sts2#1034`）、
  10 = 検証用の採取の契約（`cat12079801/sts2#1037`。`hand_select.cards[].index` を holder の位置にし、`selected_cards` を常に返すよう意味を変えた）、
  11 = ポーションの使用可否の契約（追加だけだが、読み手が所持行すべてに必須として扱えるように上げた。`cat12079801/sts2#1012`）、
  12 = パワー・レリックの量の入力契約（全 status 行の `dynamic_vars`、パワーの private Data の項目、レリックの `run_state` と手番の数。`cat12079801/sts2#1020`。レリックの `vars` は読めないとき省略から null と理由に変えた）
- **POST の応答は必ず `status` を持つ**（転送の失敗も含む）（#50）
- **メインスレッド待ちの上限**: メインスレッドが 10 秒応答しないか、待ちの列が 32 件を超えたら、アクションを実行せず 503 と `retry: true` を返す。クライアントが諦めた要求は捨てる（#51）
- **`wait: true`**: POST がゲームの落ち着くまで応答を保留し、落ち着いた state ごと返す（`settled` / `changed` / `waited_ms` / `polls` / `state`。`wait_timeout_ms` で上限）（#52）
- **`ended_combat`**: 単独の POST の間に戦闘が終わったら、戦闘が終わった時点（勝利は `CombatHistory.Clear` の前、敗北は `CombatManager.ProcessPendingLoss` の前）のローカル Player の `card_resolution_events` を、その応答の `ended_combat`（`outcome`・`card_resolution_events`・`unavailable_reasons`）で返す。終戦後の state には `combat_history` が無いため。信じてよいのは `wait: true` で落ち着いた応答のものだけ（`wait` なし・待ちの上限では次の POST にずれて付くことがある）。敗北の取得は別の patch で、入らなくても列は出る（schema 1.8.0、`cat12079801/sts2#1113`）
- **`is_resolving` / `resolving_reasons`**: アクションの解決中かを state で示す。呼び出し側の入力を待っている画面では false（#39・#41）。
  手番の初めのドローと手番開始時のフックの間も、戦闘の画面では `player_turn_starting` で解決中と返す（ゲームはこの間 `PlayerActionsDisabled` を戻し、action の列の外でドローする）（#87）
  戦闘の部屋に入ってから戦闘が始まるまでと、戦闘が終わってから報酬画面などが出るまでの `battle` の無い state も、`combat_starting` / `combat_ending` で解決中と返し、
  `message` を局面に合わせる（開始中は `Combat is starting...`。本家は開始中も `Combat ended. Waiting for rewards...` と返す）（#88）
  マップの移動中（`map_traveling`）・閉じる演出の間（`map_closing`）の `map` と、ラン中の部屋の切り替え（`room_changing`）・読み込み中（`run_loading`）の `unknown` も
  解決中と返し、`wait: true` もそこで落ち着いたとみなさない（本家はどれも `is_resolving: false` で返し、`choose_map_node` / `continue` の `wait: true` が途中で返る）（#89）
- **`warnings`**: state の一部を組めなかったとき、その箇所と例外を一覧で返す（黙って欠かさない）（#54）
- **JSON は既定でコンパクト**。`?pretty=1` で字下げ（#45）
- **`GET /api/v1/singleplayer?passive=1`**: 読むだけで UI に触らない（ショップの在庫を開き直す・偽商人や宝箱のボタンを押す、をしない）（#59）。
  `shop.sellable_potions` も在庫を閉じずにそのときの答えを返す（在庫を開いている間は悪臭ポーションに商人の的が無いので出ない。`sell_potion` は従来どおり自分で閉じる）（`cat12079801/sts2#1012`）
- **戦闘外の準備の判定（検証用）**: `?passive=1&scenario_setup_guard=1` の GET に `scenario_setup_guard`
  （`version`・`screen`・`room`〔`RunState.CurrentRoom` の実型名〕・`combat_starting` / `combat_in_progress`〔`CombatManager.IsStarting` / `IsInProgress`〕・
  `run_over`〔`RunState.IsGameOver`〕・`allowed`・`reason`）を足す。許すのは MapRoom の `map`・Neow の EventRoom の `event`・RestSiteRoom の `rest_site` で、
  ラン継続中・戦闘の外・解決中でないときだけ。読めない値は null で許さない。[判定と出典](raw-full.md#戦闘外の準備の判定scenario_setup_guardschema-10)（schema 10、`cat12079801/sts2#1037`）
- **`scenario_capabilities`**: すべての state と `GET /` に、この build が実装する検証用の契約の版を返す（`setup_guard: 1`・`selection_uid: 1`・
  `potion_availability: 1`。0 は未実装）。古い build は未知の引数を無視するので、クライアントはこれで機能を確かめてから条件付きの要求を送る（schema 10、`cat12079801/sts2#1037`。
  `potion_availability` は schema 11、`cat12079801/sts2#1012`）
- **ブラウザからの要求を拒否する**: `Origin` や `Sec-Fetch-*`・`Referer` がある要求は処理前に 403。`Host` は `127.0.0.1:<port>` / `localhost:<port>` だけ。POST は JSON の `Content-Type` が必須（#69）
- **開発コンソール API（検証用・既定は無効）**: `STS2_MCP.conf` の `dev_console_enabled: true` で、許可リスト内のコマンドを `dev_console` アクションで送れる。詳細は [`dev-console.md`](dev-console.md)（#68）。
  body に `scenario_setup_guard: {version: 1, expected_screen}` を付けると、実行の直前にメインスレッドで上の判定をやり直し、許されないか画面が動いていれば実行しない。
  未知の版・画面・キーは拒否する（schema 10、`cat12079801/sts2#1037`）

## パラメータと識別子

- **`entity_id` は戦闘中に固定**（初見順に割り当て、撃破しても振り直さない）。数値の `combat_id` も出し、`target` にはどちらでも渡せる（#1）
- **カードの `uid`**（例 `STRIKE_IRONCLAD#…`）: 手札・山札・捨て札・廃棄札の全部で戦闘中不変。`play_card card_uid <uid>` で撃てる（#38）。
  uid の番号は乱数で振り、山札の引く順を漏らさない（#79）
- **index 系のパラメータ名の別名**: カード系は `card_index` / `index` の両方を受ける。文字列の数値も受ける。エラー文に受理する名前を書く（#25）
- **ポーションのスロット**: `use_potion` / `discard_potion` は `slot` / `potion_index` / `index` を受ける。`slot` はポーション欄の固定番号。空スロットのエラーは欄の中身を返す（#26）
- **対象の要るポーション・カード**: `target` を省くと、敵が 1 体なら自動で選び、複数ならエラー（黙って消費しない）（#27）
- **ポーションをいま使えるか**: `player.potions[]` の所持行すべてに `can_use_now`・`valid_target_entity_ids`・`can_use_now_reason` を出す。
  ゲームのベルトの使用ボタン（`NPotionPopup.CanEnableUseButton` と holder・所有者・部屋のロック）と対象の選び方（`NTargetManager`・`PotionModel.IsValidTarget`）を
  読むだけで判定し、popup・選択・enqueue・在庫の開閉は起こさない。読めないときは `null`・`null`・`unobserved:<段>` で、使用不可と区別する。
  `use_potion` に `expected_potion_id` を渡すと、使う直前に slot の個体・判定・`target` を照合し、違えば何も使わずに `potion_check` 付きのエラーを返す
  （敵の自動補完・在庫を閉じる操作をしない）。[契約と出典](raw-full.md#ポーションをいま使えるかplayerpotionsschema-11)（schema 11、`cat12079801/sts2#1012`）
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
  - パイルの行にも手札と同じ `rarity`（`CardModel.Rarity` の enum 名。札の表の帯に出る公開の値）。読取失敗は null・理由・warnings。山札の行の並びは変えない（schema 1.12.0、`cat12079801/sts2#1133`）
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
- **戦闘中の手札選択（`hand_select`）**: `cards[].index` は `NPlayerHand.ActiveHolders` の位置（`combat_select_card` が押す位置）で、札の無い holder は
  行を出さず後ろを詰めない。行に `uid`（`player.hand[]` と同じ）と `enabled`（覗き見中・オーバーレイあり・戦闘外など、ゲームが押下を無視するときは false）、画面に `min_select` / `max_select`（`_prefs`）と `peeking` を出す。
  `selected_cards` は手札の `_selectedCards`（強化モードの preview の札も含む）を `{index, id, name, uid}` で常に返し、空は `[]`、読めなければ null と理由。
  `can_confirm` と `combat_confirm_selection` は表示中の有効な確定ボタンで判定する。[契約と出典](raw-full.md#手札選択の候補と-uidschema-10)（schema 10、`cat12079801/sts2#1037`）
- **uid で照合する選択**: `combat_select_card` と `select_card`（grid・choose-a-card）に `expected_uid` を渡すと、その index の現在の個体の uid と照合し、
  違えば（同名の別個体・並べ替え後・uid の無い戦闘外を含む）押さずに error を返す。照合は覆いを外す前に行い、grid は覆いを外した後に
  クリックに使う候補でもう一度行う。2 回目で違えば押さないが、画面は表に戻ったままで、error に `revealed` が付く。
  `combat_select_card` は札の無い holder と、ゲームが押下を無視する状態（覗き見中・オーバーレイあり・戦闘外。読めないときも）を、
  `expected_uid` の有無によらず押さずに理由付きで拒否する（schema 10、`cat12079801/sts2#1037`）
- **戦闘中の選択候補の個体（`combat_instance`）**: 戦闘中の `card_select`（grid・choose）・`hand_select`・`card_reward` の候補行に、
  戦闘のパイルのちょうど1つにある札は `{source: "pile", pile, uid}`（パイルの行と同じ uid）、パイルに無い候補（3 択の生成札など）は
  `{source: "detached", pile, values}`（uid を発行せず、型・段階・数値・付与・キーワード・単ターンの期限・リプレイ・コストを候補から直接読む。
  可否・対象は評価しない）を付ける。2 つのパイルにある札は対応付けず理由付きの null。`index` と画面の識別は変えない。戦闘外の画面は従来どおり。
  [入力契約と出典](raw-full.md#選択候補の個体combat_instanceschema-7)（schema 7、`cat12079801/sts2#1019`）
- **戦闘中の選択画面の `battle`**（schema 1.4.0）: 戦闘中（`CombatRoom` で `CombatManager.IsInProgress`）に出る選択画面の state（`state_type` が `_select` で終わるもの。`hand_select`、grid と choose-a-card の `card_select`、`bundle_select`、`relic_select`）すべてに、戦闘の画面と同じ作り方の `battle` を付ける（単独・マルチとも）。画面のクラスでなく `state_type` で決めるので、計略・未来視の雫の山からの選択（`NCombatPileCardSelectScreen`）も、今後の選択画面も付く。`state_type`・隠す情報は変えず、戦闘の外の選択画面と選択画面でない state には付けない。[入力契約と出典](raw-full.md#戦闘中の選択画面の-battleschema-140)（`cat12079801/sts2` の MCP_ISSUES D-15）
- **選択を開いた元（`choice_source`）**（schema 1.5.0）: 戦闘中に `CardSelectCmd` が開く選択画面（`hand_select`、choose-a-card と `NCombatPileCardSelectScreen` / `NSimpleCardSelectScreen` の `card_select`）に、選択を開いた入口（転送した外側の入口も）・`PlayerChoiceContext` の連鎖と各 context の model stack（上が選択を開いた model。計略の `StratagemPower`・エントロピーの `EntropyPower`・スプラッシュの札）・進行中のカードのプレイ（uid・何回目の解決か）・上の model が走らせている通知とその列で後ろに残る listener（山札の順は出さない）を付ける。`CardSelectCmd` の 8 つの入口に読むだけの Harmony の patch を当てて引数を記録し、選択の Task が終わったら消す（終わった戦闘の記録も捨てる）。分からないときは `known: false` と理由。[入力契約と出典](raw-full.md#選択を開いた元choice_sourceschema-150)（`cat12079801/sts2#1084`）
- **選択を待つ処理の鎖（`choice_source.await_chain`）**（schema 1.9.0）: `choice_source` に、開いている選択を待っている async の呼出しを内側から外へ並べた鎖を付ける。`CardSelectCmd` の入口が返した Task から、それを `await` している状態機械を 1 段ずつたどり（.NET の Task の継続と各状態機械の awaiter を読むだけ）、`Hook.AfterPlayerTurnStart` / `AfterShuffle` / `BeforeHandDraw` の走っている段と後の段、その段が始まったときに本体が作った列での位置と後ろでいま走る listener（山札にあった札の順は出さない）、`CardPileCmd.Shuffle` とそれを起こしたドロー（残りの枚数・手番の初めのドローか）または山札からの自動プレイの予約（`card_resolution_events` の `call_id`）、`CombatManager.SetupPlayerTurn` / `RunAutoPrePlayPhase` の残りの段を出す（エントロピー・計略の続き）。段の初めの山札の札は、その 3 つの Hook の間だけ `CombatState.IterateHookListeners` の列を作る前に読むだけの Harmony の prefix で記録する。分からないときは `known: false` と理由。[入力契約と出典](raw-full.md#選択を待つ処理の鎖choice_sourceawait_chainschema-190)（`cat12079801/sts2#1115`）
- **戦闘の出来事の通し列（`battle.combat_log`）**（schema 1.10.0）: 戦闘中の `battle` に、その戦闘で起きたことを起きた順に並べた列を付ける。通知（`Hook` の After/Before 53 件。巡ごとに上書きしている listener と引数）、履歴の全種（ドロー・プレイ・生成・被ダメージ〔差し替え前の対象〕・攻撃・ブロック・パワー・エナジーの消費・ポーション・敵の行動・廃棄・捨て札・苦悩）、山の移動（行き先・位置の種類・cloned_by）、シャッフル、札の変化・戦闘からの除外、敵の追加・撃破・逃走、HP・最大 HP・ブロックの命令、エナジーの増減、ポーションの取得・破棄、パワーの付与・増減・消滅、`Modify*` の結果と修正したモデル、光った効果、手番の段階 12 件、選択の結果。行は確定したら変えず `seq` で並べ、1 戦闘 2000 行を保持して state は既定で最後の 100 行（`?combat_log_since=<seq>` でその seq から）。山札の中の移動・山札からの選択の候補・置いた位置の添字は出さず、シャッフルの間の行は新しい山札の順に並ぶので件数だけ（`hidden_rows`）にする。読むだけの Harmony の patch（ID `com.sts2mcp.combatlog`。呼出しを飛ばさない・引数を変えない・乱数に触らない）で記録し、patch の群が当たらない・行を作れないときは `known: false` と理由。戦闘を終えた POST の `ended_combat` にも付ける。[入力契約と出典](raw-full.md#戦闘の出来事の通し列battlecombat_logschema-1100)（台帳は `docs/event-inventory.md`）
- **選択の文脈（`decision_context`）**（schema 1.10.0）: すべての選択画面（`hand_select`・`card_select`〔choose・grid・戦闘外のデッキの画面〕・`bundle_select`・`relic_select`）に、画面を出した入口（`CardSelectCmd` / `RelicSelectCmd` のメソッドと外側の入口）・開いた元（呼出し履歴でいちばん近いゲームのフレーム: レリック・イベント・休憩所の選択肢・札・パワーなど）・開いた時点の `combat_log` の seq・戦闘中は `choice_source` を付ける。戦闘外のデッキ・束・レリックの入口には別の読むだけの patch（ID `com.sts2mcp.decisioncontext`）を当てる。[入力契約と出典](raw-full.md#選択の文脈decision_contextschema-1100)
- **戦闘の試行と choose の呼出し**（schema 1.14.0）: 単独・マルチの `battle.attempt_identity` は MOD の起動セッションと `CombatManager.CurrentCombatId` の組で、ログの初行前にも試行を識別する。戦闘中 choose の `decision_context.choice_identity` と確定した `combat_log.rows[]` の `selection` 行は、同じ `CardSelectCmd.FromChooseACardScreen` の記録を共有する。画面で照合した提示個体の UID を確定行で再利用する。試行を終えたあとの通知は追記から外す。[入力契約と出典](raw-full.md#戦闘の試行と-choose-の識別schema-1140)（`cat12079801/sts2#1203`）
- **初手ドロー前の待機段**（schema 1.14.0）: `choice_source.await_chain` は `CombatManager.SetupPlayerTurn` が待つ `Hook.BeforeHandDraw` の通常段・Late 段を区別する。その段が実際に回す列の現在位置と、後ろで有効な listener を出す。通常段で待つ間は `following` に Late の名前が入り、Late の列は開始時に作られる。関連フレームや列を照合できなければ理由付きで未観測。[入力契約と出典](raw-full.md#選択を待つ処理の鎖choice_sourceawait_chainschema-190)（`cat12079801/sts2#1204`）
- **ポーションの生成候補（`player.potion_generation_pool`）・X の値（`energy_cost.x_value_if_played`）**（schema 1.10.0）: `player` に、ランダムなポーションの候補（`PotionFactory.GetPotionOptions` をそのまま呼ぶ。レアリティと戦闘中に生成できるか）を付ける。手札の X の札の `energy_cost` に、いま撃ったときに効果が読む X（現在のエナジーに `Hook.ModifyXValue` をかけた値。ケミカルX 込み）を付ける。[ポーションの生成候補](raw-full.md#ポーションの生成候補playerpotion_generation_poolschema-1100)・[Energy Cost Object](raw-full.md#energy-cost-object追加schema-4)
- **戦闘中の choose-a-card（3 択ポーションなど）**: 候補の実個体に手札と同じ `uid`・`energy_cost`・`star_cost_state`・札の詳細と `can_play`・`exhaust_on_next_play` を付け、画面に `battle` を付ける（上の「戦闘中の選択画面の `battle`」）。
  選択後の継続（生成時・使用後・空手札の通知）に参加する listener を `card_select.continuation_context` に、候補自身の override を `cards[].continuation_listeners` に出す（通知の hook は呼ばない）。山札にある listener は引く順を漏らさないよう `order: null` と山札のまとまりの開始位置で出し、表示順に並べる。候補の `energy_cost` には `global_modifiers_applied`（パイルに入る前は false）を付ける。
  GET で選択・無料化・生成・抽選をしない。`index` は `select_card` と同じ holder の位置で、札の無い holder は選ばない。版は上げていない（schema 4 のときの追加）。[入力契約と出典](raw-full.md#戦闘中の-choose-a-card追加schema-4)（#95、`cat12079801/sts2#958`）
- **死亡中の敵**: 復活を待つ敵（再結合など）を `alive: false` で `enemies[]` に残し、`revive: {in_turns, hp, power_id, power_name}` を付ける（#55）
- **敵に当てられるか**: `battle.enemies[].hittable` に `Creature.IsHittable`（死亡なら false、それ以外は `Hook.ShouldAllowHitting`。復活中の体節・幻影・実験体は false）の bool をそのまま出す。生死で絞らず全ての敵の行に付け、HP・alive・パワー名から作らない。読めなければ null・理由・warnings。Markdown の HP 行に `Hittable`。版は上げていない（schema 4 のときの追加）。[入力契約と出典](raw-full.md#敵に当てられるかhittable追加schema-4)（#95、`cat12079801/sts2#941`）
- **敵にパワーを付けられるか**（schema 1.11.0）: `battle.enemies[].can_receive_powers` に本体の `Creature.CanReceivePowers`（`CombatState` が無ければ false、それ以外は `Hook.ShouldAllowHitting`。`hittable` と違い死亡では false にしない）の bool をそのまま出す。`PowerCmd.Apply` が付与の前に見る条件で、クイーンの「燃え上がれ」の筋力が付く味方を決める。生死で絞らず全ての戦闘の全ての敵の行に付け、`hittable`・HP・alive・パワー名から作らない。読めなければ null・理由・warnings。Markdown の HP 行に `| Receives powers: yes/no`。[契約・出典](raw-full.md#パワーを付けられるかcan_receive_powersschema-1110)（`cat12079801/sts2#1175`）
- **敵の HP の無限表示**（schema 1.7.0）: `battle.enemies[].hp_display_is_infinite` に、本体の `HpDisplayExtensions.IsInfinite(Creature.HpDisplay)` の bool をそのまま出す（硬い殻 `HardenedShellPower` の上限到達から次の陣営の手番の開始まで、滝の巨人の `TriggerAboutToBlowState` 以後の死亡の継続で true）。ヘル・レイザー（`HellraiserPower.AfterCardDrawnEarly`）が、当てられる敵がすべて無限表示のときに自動プレイの回数を 9 で止める判定の入力。生死で絞らず全ての敵の行に付け、HP・最大 HP・パワー名から作らない。読めなければ null・理由・warnings。プレイヤーの行には出さない（本体でプレイヤーの `HpDisplay` を変える通常の経路が無い）。Markdown は HP の数字の後に ` ∞`。[入力契約と出典](raw-full.md#hp-の無限表示hp_display_is_infiniteschema-170)（`cat12079801/sts2#1088`）
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
- **敵の台詞の印**（schema 1.1.0）: 行動の本体の条件分岐が読む台詞の印を `combat_state` に出す。傭兵グレムリン（`GremlinMerc`）の `has_spoken`（private `_hasSpoken`。`GimmeMove` が初回に true にして台詞を出す）と、キンの司祭（`KinPriest`）の `speech_used`（private `_speechUsed`。`RitualMove` が初回に true にして台詞を出す）。どちらも分岐の先は台詞と待ちだけで、攻撃・盗み・筋力の量と行動の遷移は変わらない。false も実値で、行動ログから作らない。読取失敗は null・理由・warnings。[入力契約と出典](raw-full.md#敵の台詞の印schema-110)（`cat12079801/sts2#1065`）
- **棚卸しから生成した内部状態**（schema 1.2.0・1.3.0・1.12.0・1.13.0）: ゲームのモデルの内部状態を棚卸し（[`state-inventory.md`](state-inventory.md)。`tools/state_inventory.py` がデコンパイルと MOD のソースから候補と被覆を出し、人の判断を `state-inventory-classification.json` に持つ）、人間に見える（public）と分類して `section` を付けた 34 項目を、分類表から生成した許可リスト `McpMod.StateAllowlist.Generated.cs` で読んで、カード・パワー・レリック・player・battle・run の行の `internal_state` に出す。項目ごとに分類の根拠の備考を生成物に持ち、`--check-allowlist`（契約試験 `StateAllowlistContract` も回す）が分類表との一致を、棚卸しの実行が今の版の宣言（型の完全名・C# の型・種別）との一致を、`--check-docs` が棚卸しの文書の再生成との一致を確かめる。宣言フィールド（自動実装プロパティは backing field）だけを読み、参照はカードの uid（山に無いカードには振らない）・`combat_id`・モデルの id にする。読取失敗は null・理由・warnings。山の順・これから出るレリック・報酬の出現率・エンシェントの提示の前の中身は載せない。全パワーの行には付与元・対象の `applier_combat_id`・`target_combat_id`（`PowerModel._applier`・`_target`。派生によらず出す。schema 1.3.0）が付く。戦闘の札の行には、戦闘の開始時にデッキから写した札かを表す `deck_version_id`（`CardModel._deckVersion`。`Player.PopulateCombatState` が入れた元の札のモデル id、戦闘中に作られた札は null。泥棒バッタの窃盗の候補。schema 1.12.0、`cat12079801/sts2#1133`）が付く。棚卸しは、基底クラスのメンバーを MOD が派生の分岐（`case`・`is`・その分岐からしか呼ばれない補助）の中だけで読んでいると `partial` と報告し、許可リストで全派生に出すか分類表の `partial_reason` で解消するまで終了コード 4 で終わる。別の型の同名のプロパティの読み取り（受け手の変数の宣言の型が無関係なもの）は被覆に数えない。[入力契約と出典](raw-full.md#棚卸しから生成した内部状態internal_stateschema-120)
- **敵の攻撃の意図**: `battle.enemies[].intents[]` の Attack / DeathBlow（`AttackIntent`）に `attack: {readable, base_damage, hits, resolved_damage}` を公開する。`base_damage` は現在の `AttackIntent.DamageCalc()` の補正前の decimal（invariant culture の文字列）、`hits` は `Repeats`、`resolved_damage` はローカルのプレイヤーが対象のときの `GetSingleDamage`（筋力・脆弱・脱力など込み）。ラベル・本文から逆算しない。ローカルのプレイヤーが居ないときの補正前の値を解決値として出さない。三値の取得成功時だけ `readable=true`、失敗項目は null・項目別理由・個体と意図の位置を識別する warnings。Markdown の意図の行に `[attack: base … | hits … | resolved …]` を表示。単独・協力プレイと死亡中の敵で共通、版は上げていない（schema 4 のときの追加）。[入力契約と出典](raw-full.md#敵の攻撃の意図追加schema-4)（#93、`cat12079801/sts2#956`）
- **パワー（`status[]`）**:
  - `amount` は実効のスタック数（`PowerModel.Amount`）。UI の表示値と違うときだけ `display_amount`（#23）
  - 説明の解決に失敗しても、パワーを捨てずに id・名前・スタック数を返す。UI に出ないが残っているものは `hidden_in_ui`（#24）
  - 観測できる内部状態を許可リストで出す（持続のスキップ・ブロックの基底値・包囲の向き・盗んだ量・拘束の鎖の使用済み・まるくなるが追跡中の札の uid・泥棒が盗んだゴールドと泥棒・強奪の対象 player など）。泥棒・強奪の対象別の項目は `hidden_in_ui` の行にも付ける。活力の `vigor_attack`（private `VigorPower.Data` の攻撃との結合の有無・結合先の攻撃者が所有者か・開始時の量。未結合と読取失敗を区別）を player・敵、`hidden_in_ui`・最小行の活力に付ける。呪術の付与元（`applier_combat_id`、読取の成否を `applier_readable`。付与元なしと読取失敗を区別）を可視性によらず付ける（`cat12079801/sts2#990`。値は全パワーの `internal_state.applier_combat_id` の写しで、付与元を読むのは許可リストの 1 回だけ）。儀式の `was_just_applied_by_enemy`（`cat12079801/sts2#1018`、schema 6）も可視性によらず付ける。爆弾の `damage`（`SetDamage` の威力）、インフェルノ・深紅の衣の `self_damage`、威風堂々（Panache）の `cards_left` / `already_applied`（`cat12079801/sts2#1019`、schema 7）も可視性によらず付ける。マギナイトの弱化（`DAMPEN_POWER`）の private `DampenPower.Data` を、付与した敵の `caster_combat_ids`（`enemies[].combat_id`、昇順）と、弱化で下げた札の `downgraded_cards`（戦闘の uid registry の uid と元の強化段階。registry に無い札は uid を新たに振らず、行ごとに `uid_known=false` と理由）として可視性によらず付ける（`cat12079801/sts2#1031`、schema 8）。破壊（`Rend`）の入力として、すべての行に `PowerModel.TypeForCurrentAmount` の `type_for_current_amount`（負の筋力・敏捷・集中は既存の `type` が `Buff` のまま `Debuff`）と、`ITemporaryPower` を実装するかの `is_temporary_power` を可視性によらず付ける（`cat12079801/sts2#1034`、schema 9）。すべての行に、パワー自身の `DynamicVars` を `{変数名: BaseValue}` にした `dynamic_vars` を可視性によらず付ける（private `PowerModel._dynamicVars` をそのまま読み、getter で作らない。`StringVar` は除く。集合が読めなければ null と理由、変数ごとの失敗はその名前だけ null と理由。`cat12079801/sts2#1020`、schema 12）。量・記録が private Data・フィールドにあるパワー（血の沸騰・カラミティ・絞殺の札ごとの記録、ヘル・レイザーの自動プレイ、オートメーションの残り枚数、闇の抱擁のエセリアルの数、巨大化の結合、硬い殻の受けたダメージ、適応・再結合・幻影の復活待ちと幻影の戻る行動、ネメシスの霊体の予定、怠惰・軟化の枚数、希望の道標の受け渡し中、盗むの盗んだ札）にも型ごとの項目を可視性によらず付ける（札は既存の uid registry を引くだけで、無い札は `uid_known=false`。同じく schema 12）。schema 4 の項目は版を上げずに足した。schema 6・7・8・9・12 の項目は版を上げて足したので、版で必須にしてよい。詳細は [`power-internal-state.md`](power-internal-state.md)（#78・#81・#86・#92・#96、`cat12079801/sts2#980`）
- **戦闘の履歴の要約**（`player.combat_history`）: 開始・完了・廃棄・被ダメ・ブロック獲得の件数、前の手番の uid、この手番の束縛の付与（#76・#81）、金の斧の倍率と同じ戦闘全体・全員の完了したカードの数 `card_plays_finished_this_combat`（schema 1.5.0、`cat12079801/sts2#1084`）。
  この手番の開始・完了の明細 `card_play_events_this_turn`（履歴順。各件に uid・自動プレイの印・実際の対象の `combat_id` / `entity_id`・`play_index` / `play_count`）。荒廃などの自動プレイの照合用。読めなければ配列ごと null（#95、`cat12079801/sts2#937`）
  戦闘全体の札の解決の通し列 `card_resolution_events`（schema 1.6.0）: 所有者のドロー（uid・`from_hand_draw`）・プレイの開始と完了（`card_play_events_this_turn` の要素と同じ形）・生成（`card_id`・uid・`creator`〔`owner` / `other_player` / 敵などプレイヤー以外は null〕）を、本体の `CombatHistory.Entries` の順のまま、前の手番の終わり・敵の手番・次の手番の初めをまたいで 1 本で出す。行は記録されたら変わらないので、後の snapshot の列は前の snapshot の列で始まる。同じ札の再ドローも行ごとに残す。山札の順・まだ引いていない札は出さない。1 行でも読めなければ配列ごと null。Markdown は種類ごとの件数だけ（`cat12079801/sts2#1088`）
  この列に、山札からの自動プレイ（`CardPileCmd.AutoPlayFromDrawPile`。カスケード・荒廃・騒乱など）の予約を出す（schema 1.8.0）: 呼出しごとの `auto_play_reservation`（`call_id`・親の呼出しの `parent_call_id`・親のモデル `parent`・親プレイの開始行の添字 `parent_play_started_index`・`requested_count`・`position`・`force_exhaust`）、Play に移して画面に表で出した札ごとの `auto_play_reserved`（確保の順の `reserve_index` と uid）、`CardCmd.AutoPlay` に渡した札ごとの `auto_play_dispatch`。子のプレイの開始・完了の行には `reservation: { call_id, reserve_index }` が付く。プレイ不可で開始の行の無い札・終戦で Play に残った札も、確保と消化の行で区別できる。まだ移していない山札の札・乱数は出さない。読取専用の Harmony の patch で記録し、patch が入らない・記録に失敗した戦闘では配列ごと null（`cat12079801/sts2#1113`）
- **ワッピー**（schema 1.16.0、`cat12079801/sts2#841`）は所有者の `pets[]` に戦闘中の生存・死亡個体を出す。
  `combat_id` は既存の戦闘ログの受け手と対応し、`power_ids` は表示で落ちるパワーも含む。所持時は空配列・HP 0 の行・読取失敗の null を区別する。
  戦闘外はレリック行から保存 HP を読める。[公開契約と出典](raw-full.md#戦闘中のワッピーschema-1160)
- **親指締め具の多段攻撃の入力**（schema 1.15.0、`cat12079801/sts2#1296`）を、戦闘中のローカル所有者の通常・選択入力に出す。
  - `player.combat_history` に当手番のエナジー消費・最新の未完了 play の uid 別支払い・正の星獲得・オスティの攻撃・旋律 3 種、当戦闘のエセリアル完了件数を出す。[集計契約と出典](raw-full.md#多段攻撃用の戦闘履歴schema-1150)
  - 手札と 4 パイルの星 X 札に保存額・保存 X、手札には手動支払見込み・その補正後 X も出す。[星 X の契約と出典](raw-full.md#戦闘カード個体の星-xschema-1150)
  - `player.arrow_state` に矢枠の順・使用可否・破壊予定、対象別の刺突順と回収カウンタを出す。項目ごとの読取失敗を空や 0 と区別する。[矢の契約と出典](raw-full.md#矢の枠と刺突schema-1150)
- **エントロピー醸造**（schema 1.13.0、`cat12079801/sts2#991`）がベルトにある戦闘では、`PotionFactory.GetPotionOptions` の候補を所有者・DLL・レアリティの累積閾値付きで出す。同一 ID の多重度を保持し、所持行と同じ公開変数と、全物理枠での条件付き可否・対象を添える。本体の列挙順で BeforePotionUsed・ShouldProcurePotion・AfterPotionProcured・AfterPotionUsed の override を識別する。本人の使用済みポーションは `PotionUsedEntry` の順で読み、醸造が補充されなかった直後にも履歴を保持する。[入力と出典](raw-full.md#エントロピー醸造playerentropic_brew_poolschema-1130)
- **生成の候補**:
  - `player.generation_pool`: オロビックの酸液の抽選前の候補。本体の `CardModel.IsAllowedInRun`（`CardFactory.FilterForRun`。人数と Blitz）→ `FilterForCombat` を通した一覧で、`filter_version: 2`（#63・#95、`cat12079801/sts2#940`）
  - `player.colorless_generation_pool`: 何でも屋の抽選前の候補（無色の解放済み pool から自身を除き、`IsAllowedInRun` → `FilterForCombat`）。戦闘中だけ（#95、`cat12079801/sts2#940`）
  - `player.other_character_attack_generation_pool`: スプラッシュの抽選前の候補（`UnlockState.CharacterCardPools` から〔2 つ以上なら〕自キャラの pool を除いた解放済みの Attack を `IsAllowedInRun` → `FilterForCombat`）。戦闘中だけ。抽選・強化・結果は出さない（schema 1.5.0、`cat12079801/sts2#1084`）
  - `player.transformation_options`: エントロピーの変化の候補。戦闘のパイルの札ごとに本体の `CardFactory.GetDefaultTransformationOptions(original, isInCombat)` を呼んだ結果の ID（同じ ID・同じ候補はまとめ、uid は序数順）。エントロピーの戦闘だけ。抽選の結果は出さない（schema 1.5.0、`cat12079801/sts2#1084`）
  - `player.unlocked_pools`: 何でも屋・ジャックポット・錬金術と同じ関数で取った、解放済みのカード・無色カード・ポーションの ID（戦闘中だけ）（#71）
- **`end_turn` と `discard_potion` は本体の UI と同じ GameAction を積む**（`EndPlayerTurnAction` / `DiscardPotionGameAction`）。本体の戦闘リプレイに残る（#61）

## レリック

- **パエルの歯のあるランの修飾子**（schema 1.13.0、`cat12079801/sts2#1214`）を `RunState.Modifiers` から `run.internal_state.modifier_ids` に出す。保管札 `stored_cards` と修飾子の列は、空を `[]`、読取失敗を `null`・理由・警告で表す。[入力と出典](raw-full.md#パエルの歯のあるランの修飾子runinternal_statemodifier_idsschema-1130)

- `used_up`（`RelicModel.IsUsedUp`）と `melted`（#62）
- `vars`: 所有レリックの `DynamicVars` の名前 → 現在の値（戦闘中・戦闘外）（#66）。private `RelicModel._dynamicVars` を読み（getter で作らない）、読めなければ null と `unavailable_reasons.vars`、1 変数の失敗はその名前だけ null と理由（`cat12079801/sts2#1020`、schema 12）
- `run_state`: 戦闘をまたいで残る保存カウンタ（ヌンチャク・音叉・`IronClub` の数、`HappyFlower`・`Pendulum`・`PollinousCore` の手番数、`Girya`・`LastingCandy`・`BookOfFiveRings`・`VenerableTeaSet`・`PumpkinCandle`・`SwordOfStone`・`WingedBoots`・`SilverCrucible`・`FishingRod`・`ToyBox`・`WongosMysteryTicket`・`PaelsWing`・`LavaRock`）を戦闘中・戦闘外とも出す。詳細は [`relic-combat-state.md`](relic-combat-state.md#保存カウンタrun_stateschema-12)（`cat12079801/sts2#1020`、schema 12）
- `combat_state`: 戦闘中の内部状態（使用フラグ・カウンタ。苦無・手裏剣・扇子・鎖鎌・レターオープナー・懐中時計の手番の数とオリハルコンの予約を含む）をクラス別の許可リストで出す。詳細は [`relic-combat-state.md`](relic-combat-state.md)（#65・#78）
- **パエルの眼の手動プレイ判定**は `player.combat_history.any_manual_cards_played_this_turn`（bool）に出す。v0.113.1 の `PaelsEye.AnyCardsPlayedThisTurn` と同じ Actor・手番・非 AutoPlay の完了履歴条件を読み、false も保持する。取得失敗は null・理由・warnings。`WhisperingEarring` 所持時の初手番の例外は所持レリック・`turn_number` と合わせて評価する。版は上げていない（schema 4 のときの追加）（#90、`cat12079801/sts2#911`）

## 報酬

- **`reward_id` / `parent_reward_id` と `screen_revision`**: 報酬に安定した ID を付け、古い一覧での取得を拒否する。カード報酬のスキップ状態（`skipped`）を出す（#64）
- **ポーション欄が満杯なら `claim_reward` はエラー**（成功を返して何もしない、をしない）（#28）
- **カード報酬の別の選択肢**: `card_reward.alternatives`（`option_id` / `title` / `enabled`）を出し、`select_card_reward_alternative option_id <ID>`（パエルの翼の `SACRIFICE` など）で選べる。`skip_card_reward` は id で `Skip` を選ぶ（#40）

## 戦闘外の画面

- **デッキ**: 戦闘外の全画面で `player.deck`、全画面で `deck_count`（#49）
- **エンシェントのデッキ対象**（schema 1.17.0）: 表示中の対象6候補の `event.options[].deck_targets` に、
  `player.deck` と同じ順で札ごとの判定と既存のエンチャント ID を出す。ゲームの `CanEnchant` や `CardModel` の判定を読み、
  空のデッキと取得失敗を区別する。Markdown に読取枚数または失敗箇所を表示する。
  [契約と出典](raw-full.md#エンシェントのデッキ対象deck_targetsschema-1170)（`cat12079801/sts2#1348`）
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
  `combat_play_card` / `combat_end_turn` / `use_potion` / `proceed_to_map` の `wait`、`combat_select_card` / `deck_select_card` の `expected_uid`

## 試験

- `tests/*Contract/` に、ゲームの DLL を使わない契約試験を置いている（札の詳細・コスト・戦闘の強化結果・uid・カード選択・戦闘中の choose-a-card・敵に当てられるか・戦闘の履歴・戦闘の出来事の通し列と選択の文脈・開発コンソール・HTTP の要求の検査・パワーとレリックの内部状態・報酬・生成の候補・v0.113 対応・画面の遷移の解決中・検証用の採取の契約・ポーションの使用可否・ワッピーの戦闘中の状態）
- `tests/PatchTargetsContract/` だけはゲームの DLL（`sts2.dll`・`0Harmony.dll`）を参照する。MOD の install のメソッドを本物の `sts2.dll` に対して呼び、すべての Harmony の patch の当て先が解決して当たること（群ごとの失敗が無いこと・ID ごとの patch の数・履歴の *Entry がすべて patch した記録メソッドから出ること・先に作られた置き換えに後から patch したメソッドがインライン化されうる呼出しが無いこと）を確かめる。ゲームは起動しない
