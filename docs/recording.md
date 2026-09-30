# 戦闘の記録（`sts2-mod-record/1`）

実戦の戦闘を、あとで本体 DLL で再生できる形で残す機能。追跡 Issue は cat12079801/sts2#122（移行計画 M2-a）。
取込と strict replay は親リポジトリの M3（cat12079801/sts2#124）が行う。この MOD は**記録を取るだけ**で、分類（再現可能／参考観測／不完全）はしない。

対象: ゲーム v0.111.0 / commit 41cef1ea（macOS ARM64、`sts2.dll` SHA-256 `9cb4f1ad…12b4`）。出典のクラス・メソッドはこの版の逆コンパイル結果。

## 仕組み

本体は戦闘ごとに、自前のリプレイ（`Multiplayer.Replay.CombatReplay`）をメモリに作っている:

- 部屋に入るとき（`RunManager.EnterMapPointInternal`、`SaveRun` の直後・部屋の生成の前）に `CombatReplayWriter.RecordInitialState(ToSave(null))` — その時点のランの全体（乱数・山札を含む）
- 戦闘中（`CombatManager.IsInProgress`）に積まれた全 GameAction（Hook は id だけ）・選択の結果・選択後の再開（`ActionQueueSet.ActionEnqueued` / `ActionResumed`、`PlayerChoiceSynchronizer.PlayerChoiceReceived`）
- 各 action の完了直前の全状態とその checksum（`RunManager.SendPostActionChecksum` → `ChecksumTracker.ChecksumGenerated`）

これを戦闘の終わり（`CombatManager.EndCombatInternal`）と `RunManager.CleanUp`（保存して終了など）で `replays/latest.mcr` に**上書き**する。
本体の再生器（`NMultiplayerTest.RunReplay`）はこの 1 ファイルから戦闘を再現する。

記録器はこの本体のリプレイを、本体が 1 件積むたびに写し出す（`CombatReplayWriter` の各記録メソッドへの Harmony の postfix。本体が積んだ要素を本体の `Anonymized()` と `PacketWriter` で直列化するだけ）。
加えて、本体のリプレイに無い相互参照（どの API 要求から来たか・選択がどの action のものか・ターンの文脈・公開観測）を別の postfix とイベント購読で読む。

### 本体の処理を変えないための約束

- **読むだけ。** patch はすべて void の prefix / postfix で、元のメソッドを飛ばさない・戻り値や引数を書き換えない・`await` しない。
  呼ぶのはプロパティの getter と本体の直列化（`Serialize`）だけ。`ToSave`・`ToNetAction` などを記録器が呼び直さない
- **戦闘中に `Anonymized()` を呼ばない。** `Logging.IdAnonymizer.Anonymize` は初めて見る id ごとに `Rng.Chaotic` を 1 回引く
  （`Rng.Chaotic` は見た目の乱択とイベント Trial の番号などに使われる）。記録器が呼ぶのは close の 1 回だけで、それは本体が
  `WriteReplay` の中で同じ replay に対して直後に呼ぶもの（同じ id を同じ順に引くので、引く回数も順序も変わらない）
- **小さいメソッドを patch しない。** JIT がインライン化し得る数行のメソッド（`GameAction.Cancel` など）は patch せず、本体の公開イベント（`GameAction.BeforeCancelled`）を読む
- **本体に例外を投げない。** 入口はすべて 1 つのラッパー（`CombatRecorder.Guard`）を通る。本体側の `ActionResumed`・`PlayerChoiceReceived` の呼び出しには try/catch が無く、
  `ActionEnqueued` のものは捕まえた例外を Sentry に送る（`ActionQueueSet.EnqueueWithoutSynchronizing`）ので、ここで止める。失敗はその記録の `faults` と `recorder_fault` 行に残る。
  記録中の `SentryService.CaptureException` の回数を数えて `record_close.sentry_captures` に書く（0 であるべき）
- **メインスレッドの時間を測る。** フックごとの処理時間を `record_close.main_thread_us`（件数・p50・p99・最大・合計）に書く
- **ファイルの書込みは専用スレッド。** メインスレッドでは JSON の組立てと本体の直列化だけを行う
- **`"record": false` なら patch を当てない。** 実行時の切替（`set_recording`）は、当たった patch が先頭で return するだけ。
  「記録なし」の比較には設定で起動し直した状態を使う

### MOD の操作経路を本体の UI にそろえた（この変更で直したこと）

本体のリプレイに残るのは、アクションキューを通った入力だけ。MOD の次の 2 つはキューを通さずに本体の処理を直接呼んでいたため、
その操作がリプレイに残らず、**MOD で操作した戦闘は本体の再生器で再現できなかった**:

| action | 前 | 今（UI と同じ） |
|---|---|---|
| `end_turn` | `PlayerCmd.EndTurn(player, canBackOut: false)` | `RequestEnqueue(new EndPlayerTurnAction(player, turn))`（`NEndTurnButton.CallReleaseLogic`） |
| `discard_potion` | `PotionCmd.Discard(potion)` | `RequestEnqueue(new DiscardPotionGameAction(player, slot, inCombat))`（`NPotionPopup.OnDiscardButtonPressed`） |

記録器は、実行中の action が無いのにターンが終わったこと（`CombatManager.PlayerEndedTurn`）を `end_turn_outside_action_queue` として記録の `faults` に出す（別の MOD や古いビルドの経路を拾うため）。

## 置き場所と設定

`STS2_MCP.conf`:

| キー | 既定 | 意味 |
|---|---|---|
| `record` | `true` | `false` なら記録器の patch を当てない |
| `record_dir` | `~/.local/state/sts2/records` | 記録の根 |

**セーブの木（`modded/profile*/`）には書かない。** ディレクトリ名に seed を入れない。

```
<record_dir>/<ランの start_time>/<NNN>-a<act>f<floor>/
  public.jsonl   公開の行。1 行 1 イベント
  hidden.jsonl   非公開の行（public の seq に対応する分だけ）
  replay.mcr     close 時の本体の CombatReplay（Anonymized）。本体が latest.mcr に書くのと同じバイト列（実測で一致）
```

- `start_time` は本体のランの開始時刻（`RunManager._startTime`。`history/<start_time>.run` と同じ値）
- `NNN` はそのランの中で 1 から。`act` は 0 始まり、`floor` はその幕の中の階（`RunState.ActFloor`）
- 記録を作るのは戦闘が始まった部屋だけ（部屋入りで開始データを保留し、`CombatManager.CombatSetUp` で開く）

## 行の形

どちらのファイルも 1 行 1 JSON。**`prev` は同じファイルの直前の行（改行を除くバイト列）の SHA-256**（先頭は null）。
public の `seq` は記録の中で 0 から欠番なく増える。hidden の行は同じ `seq` を持ち、public の `"hidden": true` の行と 1 対 1 に対応する。

public の共通フィールド: `seq`・`kind`・`prev`・`t_ms`（記録を開いてからの経過。非意味的）・`at`（`round`・`turn`・`side`・`phase`・`in_progress`。戦闘外は null。HTTP スレッドの行には無い）

| kind | いつ | 主な中身 |
|---|---|---|
| `record_open` | 記録を開いた（先頭） | `schema`・`record_id`・`trigger`・`attempt`・`session`（起動ごとの乱数 id・pid・起動時刻）・`mod`・`game`（version・commit・model_id_hash・sts2_dll_sha256）・`profile_id`・`run`・`start` |
| `initial_state` | 開いた直後（本体の replay があるとき） | `ids`（header の値＝部屋入りの時点: next_action_id・next_hook_id・next_checksum_id・choice_ids・reward_ids）と `ids_at_open`（開いた時点の本体の値）。hidden: 本体の CombatReplay の header（events・checksums は空）の packet |
| `combat_setup` | `CombatManager.CombatSetUp` | encounter・room_type・room_class・parent_event・room_count・combat_local_id（`CombatManager.CurrentCombatId`。プロセスの中の通し番号）・敵の combat_id と monster・観測 |
| `turn_started` | `CombatManager.TurnStarted` | 観測（public と hidden）。**判断の境界** |
| `combat_began` / `turn_ended` / `player_ended_turn` / `player_unended_turn` | CombatManager の同名のイベント | `player_ended_turn` は実行中の action |
| `combat_lost` | `CombatManager.CombatEnded`（勝ったときは記録が閉じた後に来るので、記録に残るのは負けたときだけ） | outcome・観測 |
| `api_request` / `api_response` | MOD が POST を受けた／返した | `request`（起動ごとの通し番号）・action・引数／status |
| `game_event` | 本体が replay の events に 1 件積んだ（prefix と postfix で件数が増えたときだけ） | `index`（本体の events の位置）・`type`・`player_slot`。GameAction は `action_id`・`action_key`・`class`・`game_action_type`・`action`・`origin`・`cause_action_id`。PlayerChoice は `choice_id`・`result`・`origin`・`owner`・`paused_action`・`pending_choice_ids`。hidden: その event の packet（プレイヤー id は本体が持つ値のまま） |
| `checksum` | 本体が replay の checksumData に 1 件積んだ | `index`・`checksum_id`・`context`・`action_id`（null あり）。hidden: checksum の値。全状態は `replay.mcr` にだけある |
| `enqueue_unrecorded` | 戦闘中に積まれた action を本体の記録器が積まなかった | action。**記録は再現できない**（`faults` に入る） |
| `enqueue_outside_combat` | 戦闘の準備中（`IsInProgress` の前）に積まれた action | 本体のリプレイは記録しない（開始データから再生で作り直される）。一覧を完全にするため |
| `choice_reserved` | `PlayerChoiceSynchronizer.ReserveChoiceId` | `choice_id`・`player_slot`・`running_action`（その時点で実行中の action） |
| `choice_begun` | `PlayerChoiceContext.SignalPlayerChoiceBegun`（GameAction・Hook・Blocking・Throwing の各実装。Branching は中の文脈へ渡すだけ） | `choice_id`（prefix の時点でそのプレイヤーが最後に予約した id）・`context`・`owner`（選択を持つ action。Hook なら生成した Hook action） |
| `choice_paused` | `ActionQueueSet.PauseActionForPlayerChoice` | 止まった action・`action_id_before`・`options`・`state_after`・`pending_choice_ids`・観測（public と hidden）。**選択待ちの境界** |
| `action_resumed` | `ActionQueueSet.ResumeActionWithoutSynchronizing` | `old_action_id`・`new_action_id`（再開で振り直される id）・`action_key` |
| `action_cancelled` | `GameAction.BeforeCancelled`（記録した action に購読） | action |
| `action_finished` | `ActionExecutor.AfterActionFinished`（完了したとき） | action。プレイヤー由来の action（`ActionQueueSet.IsGameActionPlayerDriven`）なら公開観測つき（**判断の境界**） |
| `recorder_fault` | 記録器の例外 | `where`・例外 |
| `record_close` | 記録を閉じた（末尾） | `reason`・`outcome`・`game_replay_captured`・`journal`・`self_check`・`sentry_captures`・`main_thread_us`・`faults` |

### 行動の表し方

`game_event.action` は、本体が replay に入れた `INetAction` から作る（記録器が action から作り直さない）。
`play_card` と `end_turn` は [`docs/native-ipc.md`](https://github.com/cat12079801/sts2/blob/main/docs/native-ipc.md)「行動」の形:

```json
{"type": "play_card", "combat_card_id": 4, "card": "ONE_TWO_PUNCH", "target": 1}
{"type": "end_turn", "turn": 3}
```

それ以外は `{"type": "<INetAction の型名>", <公開フィールド>…}`（例: `NetUsePotionAction` の `potionIndex`・`targetId`）。
プレイヤー id のフィールドは `<名前>_slot`（そのランでのプレイヤーの位置）に置き換える。**public にプラットフォームの id（Steam id）は書かない。**

### 識別子と相互参照

| 識別子 | 出どころ | 使い方 |
|---|---|---|
| `record_id` | `<start_time>/<NNN>-a<act>f<floor>` | 記録 1 つ（= 戦闘の 1 attempt） |
| `attempt` | 同じランの同じ (act, floor) の記録の数 + 1 | 保存して終了→再開、再起動後の再開で増える。`session` が変われば別プロセス |
| `seq` | 記録の中の通し番号 | public と hidden の対応、欠落の検出 |
| `index`（game_event / checksum） | 本体の replay の events / checksumData の位置 | 本体の `replay.mcr` の同じ位置と対応 |
| `action_id` | 本体の `GameAction.Id`（積んだときに振られる。選択の後の再開で振り直される） | `action_resumed` が旧→新を結ぶ。`cause_action_id`・`running_action` はその時点で実行中の action |
| `action_key` | その action が最初に振られた `action_id` | 再開で `action_id` が変わっても同じ action を指す |
| `(player_slot, choice_id)` | 本体の `PlayerChoiceSynchronizer.ReserveChoiceId`（**プレイヤーのスロットごと**の通し番号） | `choice_reserved`→`choice_begun`（`owner`）→`choice_paused`→`game_event`（PlayerChoice の結果。ここで親子を確定: `owner` と、その時点で止まっている `paused_action`）→`action_resumed` |
| `hook_id` | 本体の `ActionQueueSynchronizer.GenerateHookAction` | Hook の action（開始時の誘発の選択など） |
| `turn` / `round` | `PlayerCombatState.TurnNumber` / `CombatState.RoundNumber` | 追加ターンでは `turn` だけが増える（worker の観測と同じ） |
| `combat_card_id` | `NetCombatCardDb`（= `NetCombatCard.CombatCardIndex`） | カードの同一性 |
| `request` | MOD の POST の通し番号（起動ごと） | `api_request` と `origin` を結ぶ |
| `combat_local_id` | `CombatManager.CurrentCombatId` | プロセスの中の戦闘の通し番号（再起動で振り直されるので永続の鍵にはしない） |

単独プレイでは、選択の結果が来た時点で止まっている action は高々 1 つ（`ActionQueueSet.GetReadyAction` は選択を集めている queue を飛ばし、queue はプレイヤーごとに 1 本）。
止まらない文脈（`BlockingPlayerChoiceContext`・`ThrowingPlayerChoiceContext`）の選択は `paused_action: null` で来る。
予約していない id の結果が来たら `patch_missed:reserve` を `faults` に入れる（patch が効かなかった印）。

`origin` の意味:

- `{"kind": "mod_api", "request": N}` — MOD の handler が作った action そのもの（オブジェクトで結ぶので、敵ターン中に保留されて後で積まれても外れない）
- `{"kind": "not_mod_api"}` — それ以外のプレイヤー由来の action（手で押した UI、開発コンソールなど）
- `{"kind": "game"}` — Hook など本体が自分で積んだ action
- 選択の結果は `{"kind": "in_api_window", "request": N}` / `{"kind": "outside_api_window"}`。**MOD の POST の受付から応答までの時間の窓**で付けるもので、因果ではない（本体の選択画面は MOD の UI 操作の後のフレームで結果を出すことがある）

## 欠落の検出

記録器が付ける印（分類は M3 が行う）:

- **始まり**: `record_open.start.boundary` が `room_entry` 以外（`event_room` / `mid_room` / `mid_combat`）。`reproducible_from_start` が false。
  - `event_room`: `CombatRoom.ParentEventId` がある、または部屋が重なっている（`RunState.CurrentRoomCount > 1`）。イベントから戦闘に入ると本体は `RecordInitialState` を呼ばず（`RunManager.EnterRoomWithoutExitingCurrentRoom`）、
    本体の replay の開始データはイベントの部屋に入った時点のまま、イベント中の選択は replay に入らない。部屋に入ってからの MOD の要求を `pre_combat_requests` に付ける（手で押したイベントの選択は取れない）
  - `mid_combat` / `mid_room`: 記録を途中で有効にした。本体の replay は部屋入りから積んでいるので、それまでの event と checksum を `backfilled: true` で写す（`initial_state: "read_back"`）。MOD 自身の行（出どころ・ターンの文脈・観測）はその区間だけ無い。`game_events_before_open` / `checksums_before_open` に件数
- **途中**: `seq` の欠番、`prev` の鎖の切れ、public と hidden の対応漏れ、`enqueue_unrecorded`、`recorder_fault`、`faults`
- **終わり**: `record_close` が無い（ゲームが落ちた・強制終了）。`reason` は `combat_won`（`EndCombatInternal` が replay を書いた）/ `combat_lost`（負けた後の `CleanUp`）/ `cleanup`（保存して終了など、戦闘の途中で抜けた）/ `superseded_by_room_entry` / `recording_disabled` / `process_exit`
- **自己検査**（`record_close.self_check`）: close の時点の本体の replay と、写してきたものを位置ごとに比べる（events は packet の SHA-256、checksums は id・値・context）。`complete` が true なのは不一致 0・件数一致のときだけ。`replay_mcr_sha256` は `replay.mcr` の SHA-256

[`tools/record_check.py`](../tools/record_check.py) がこれらをまとめて検査する（packet の中身は読まない。読むのは下の inspect か M3）。

## 公開と非公開

| 置き場所 | 中身 |
|---|---|
| `public.jsonl` | 版・識別子・行動・選択の結果・ターンの文脈・出どころ・公開観測（worker の `public` と同じフィールド: HP・ブロック・エナジー・パワー・レリック・ポーション・敵の HP とインテントの種類・手札・山札の枚数と中身（種類と枚数）・捨て札・廃棄札） |
| `hidden.jsonl` | 開始時のラン（`SerializableRun`: 乱数・山札順を含む）の packet、本体の各 event の packet、checksum の値（全状態のハッシュなので、隠れた状態の推測を照合できてしまう）、境界ごとの非公開観測（乱数の seed とカウンタ・敵の内部の move id・山札の順序）。**packet のプレイヤー id は本体が持つ値（ローカルの Steam id）のまま**（戦闘中に匿名化しないため。本体のセーブの置き場所と同じく手元だけのもの） |
| `replay.mcr` | 本体の CombatReplay そのもの（上の非公開をすべて含む） |

- 通常の state（`GET /api/v1/singleplayer`）には何も足していない
- `GET /api/v1/record` は記録器の状態（有効か・patch・今の記録の id と件数・故障）だけを返す
- `GET /api/v1/record/inspect` は非公開の checksum を返す**検証用**。判断の経路（`sts2-turn` など）から呼ばない

## API

| 経路 | 内容 |
|---|---|
| `GET /api/v1/record` | 記録器の状態 |
| `POST /api/v1/singleplayer {"action": "set_recording", "enabled": true\|false}` | 実行時の切替。ラン外でも可。戦闘中に有効にすると、その戦闘は途中開始として記録される |
| `GET /api/v1/record/inspect?file=<.mcr>` | 本体の `PacketReader` で .mcr を読み、header・events（型・packet の SHA-256・プレイヤー id を除いた SHA-256・行動）・checksums（id・値・context・SHA-256）を返す。`record_dir` の下か現在のプロファイルの `replays/` の下だけ |

### 記録あり／なしの比較（A/B/A）

同じ戦闘を、保存して終了→再開でやり直し、`"record": false`（起動し直して patch なし）と `true` で同じ操作列を流して、本体の `latest.mcr` を inspect で比べる。
比べるもの: header の version・git_commit・model_id_hash・choice_ids・reward_ids・next_*、events の型と `sha256_without_player`、checksums の id・値・context。
除くもの: プレイヤー id（`IdAnonymizer` がプロセスごとに乱数で振る）、`serializable_run_sha256`（`SaveTime`・`RunTime`・`NumReloads`（再開で増える）・`MapDrawings` を含む）、ファイル全体の SHA-256。

## 未確認・範囲外

- **イベントの部屋から始まる戦闘**: 本体の開始データは部屋入り（イベントの前）で取られ、戦闘前のイベントの選択肢はリプレイに入らない。
  記録は `combat_setup.room_class` / `parent_event` で見分けられるようにしてあるが、再生できるかは M3 で確かめる
- **選択の結果の出どころ**は時間の窓で付けている（上記）
- 協力プレイは対象外（記録器は単独プレイでしか確かめていない）
