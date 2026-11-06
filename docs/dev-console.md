# 開発コンソール API（検証用）

`POST /api/v1/singleplayer` に `dev_console` アクションを追加する。MCP ツールの追加はない。
検証用のランでだけ有効にする。コマンドはランや保存内容に影響しうる。
マルチプレイのエンドポイントでは提供しない。既存のランモード違いの HTTP 409 も適用される。

## 有効にする方法

1. MOD DLL と同じディレクトリの既存の `STS2_MCP.conf`（JSON）を編集する。
2. 既存の `port` や録画設定を残して、`"dev_console_enabled": true` を追加する。
3. ゲームを再起動する。起動時だけ読み込む設定で、API や設定画面からは切り替えない。

```json
{
  "port": 15526,
  "dev_console_enabled": true
}
```

新規生成する設定では `dev_console_enabled: false`。設定・キーがない場合も無効。
JSON の真偽値 `true` だけを受け付け、文字列 `"true"` や数値 `1` は有効化しない。
無効へ戻すときは `false` にして再起動する。ゲーム側のデバッグ設定を強制変更しない。

## リクエストと応答

```json
{"action":"dev_console","command":"card BURNING_PACT Draw","wait":true}
```

```json
{
  "command": "card BURNING_PACT Draw",
  "success": true,
  "message": "Added card 'BURNING_PACT' to 'Draw'",
  "status": "ok"
}
```

上はコマンド結果の部分だけの例。`wait: true` の成功時は通常の `settled`、`changed`、
`waited_ms`、`polls` と、既定で `state` が加わる。`include_state: false` で state を省略できる。
`wait: true` の `wait_timeout_ms`（500〜30000 ms、既定10000）は、事前 state 取得・
メインスレッドへの配送・`CmdResult.task` の完了待ち・状態安定待ちで **1つの締め切りを共有する**。
Task 待ちに使った時間だけ、後続の settle の予算が減る。state 取得の各メインスレッド待ちも
残り時間以内に制限する。応答はこの期限とログ・JSON 処理などの少しの余裕の中で返す。
`waited_ms` はこの一連の処理の経過時間。`wait: false` でも Task は期限付きで待つが、
専用の上限10000 msを使い、`wait_timeout_ms` は使わず、事前 state 取得・settle は行わない。
待つのは HTTP ワーカーで、ゲームのメインスレッドはブロックしない。

Task がある場合は `task_completed` を返す。期限切れは HTTP 200、`status: "error"`、
`task_completed: false` とし、`success` / `message` はゲームの `CmdResult` の値を保つ。
`error` に「ゲームへ配送したが Task が期限内に終わらなかった。再送せず state を読み直す」
ことを明記し、`retry` は付けない。Task をキャンセルせず、応答後にも効果が出る可能性がある。
選択操作を待つ Task でも期限内に応答するため、state を読み直して必要な選択を続けられる。
Task 完了後に settle の期限だけ切れた場合は、従来どおり `settled: false` で判断する。
実機での全コマンドの所要時間は未確認。

`command` は送信文字列をそのまま返す。未指定・文字列以外なら応答は `"command": null`。
ログの行では null の command は省略される。
`success` と `message` はゲームの `CmdResult.success` / `msg` に基づく。
Task の例外・キャンセルは `task_completed: true`、`success: false` にし、例外の型とメッセージを返す。
Task が返らないコマンドの完了範囲はゲームの実装次第であり、成功だけで全副作用の完了は保証しない。
また Task の戻り値は解釈しないため、`success: true` は最終 state の検証を置き換えない。

拒否・ゲームの失敗・Task の例外は既存契約どおり HTTP 200 の `status: "error"` と `error` に加え、
`command`、`success: false`、`message` を返す（上記の Task 期限切れは `success` を保持する）。例:

```json
{
  "command": "unlock",
  "success": false,
  "message": "Console command 'unlock' is not allowed by dev_console.",
  "status": "error",
  "error": "Console command 'unlock' is not allowed by dev_console."
}
```

拒否条件: 設定が無効、command が空/非文字列、許可リスト外、コンソールが未生成・未初期化・
シーンツリー外、内部フィールドが読めない、コマンドがゲームの登録済み一覧に存在しない、
登録された実装がゲーム本体のアセンブリ以外、マルチプレイ中、ランモードを確認できない。
無効・空/非文字列・許可リスト外は HTTP スレッドで拒否し、main への配送も事前 state 取得も行わない。
ランモードは main 上で `IsInProgress && !IsSingleplayerOrFakeMultiplayer` を検査し、取得例外でも拒否する。
登録がない場合はデバッグコマンド無効の可能性を明記する。拒否時は `ProcessCommand` を呼ばない。
メインスレッド待ちの失敗は既存契約の HTTP 503 と `retry: true`。
ただし配送開始とタイムアウトの競合でコマンドが実行済みの場合があるため、
**dev_console の 503 では、再送の前に state とログを読み直す**。加算・トグル操作を自動再送しない。
配送後の state 取得失敗は 503 にせず `wait_error` / `settled` で返す。その他の予期しない例外は HTTP 500。
JSON 不正・ルート違いなどアクション到達前のエラーは共通のエラー形式に従う。

送信コマンドと結果は MOD ログの `[STS2 MCP] dev_console` に JSON として記録する。
拒否・Task 失敗・期限切れも記録し、改行は JSON エスケープする。
ゲーム側の `DevConsole.ProcessCommand` は、アカウント範囲の `console_history.log`
（`UserDataPathProvider.GetAccountScopedBasePath`、modded のセーブスコープ外）にも履歴を追記・保存する。
MOD が配送前に拒否した場合は履歴に入らない。ゲームに配送した後の失敗は履歴に入りうる。
Task の例外時は、ゲームの `TaskHelper.LogTaskExceptions` がログを出し、
`SentryService.CaptureException` を呼ぶ（手動コンソールと同じ挙動。キャンセル例外は除く）。
期限切れ後の Task もゲーム側の `TaskHelper.RunSafely` が観測し、MOD 側で追加の観測はしない。

## 許可リストと実名

局面作成と、設計で指定された `dump` / `log` の診断用途に限定する。
以下は参照ゲームソースの `MegaCrit.Sts2.Core.DevConsole.ConsoleCommands` 配下で照合した実名。

| 許可する名前 | 出典クラス・プロパティ |
|---|---|
| `card` | `CardConsoleCmd.CmdName` |
| `draw` | `DrawConsoleCmd.CmdName` |
| `energy` | `EnergyConsoleCmd.CmdName` |
| `block` | `BlockConsoleCmd.CmdName` |
| `relic` | `RelicConsoleCmd.CmdName` |
| `potion` | `PotionConsoleCmd.CmdName` |
| `power` | `ApplyPowerConsoleCmd.CmdName` |
| `damage` | `DamageConsoleCmd.CmdName` |
| `heal` | `HealConsoleCmd.CmdName` |
| `kill` | `KillConsoleCmd.CmdName` |
| `fight` | `FightConsoleCmd.CmdName` |
| `gold` | `GoldConsoleCmd.CmdName` |
| `upgrade` | `UpgradeCardConsoleCmd.CmdName` |
| `enchant` | `EnchantConsoleCmd.CmdName` |
| `afflict` | `AfflictConsoleCmd.CmdName` |
| `remove_card` | `RemoveCardConsoleCmd.CmdName` |
| `stars` | `StarsConsoleCmd.CmdName` |
| `godmode` | `GodModeConsoleCmd.CmdName` |
| `room` | `RoomConsoleCmd.CmdName` |
| `event` | `EventConsoleCmd.CmdName` |
| `act` | `ActConsoleCmd.CmdName` |
| `travel` | `TravelConsoleCmd.CmdName` |
| `win` | `WinConsoleCmd.CmdName` |
| `die` | `DieConsoleCmd.CmdName` |
| `dump` | `DumpConsoleCmd.CmdName` |
| `log` | `LogConsoleCmd.CmdName` |

設計上の `remove` は存在せず、`RemoveCardConsoleCmd.CmdName` は **`remove_card`**。
`remove` の別名は作らず拒否する。`ApplyPowerConsoleCmd` は `power`、
`UpgradeCardConsoleCmd` は `upgrade`。その他の指定許可名はすべて確認済み。

許可以外はすべて拒否する。設計にある拒否名は各 `*ConsoleCmd.CmdName` で確認した。
`help` はゲームの特別処理、`clear` / `exit` はコンソール UI 側の処理だが、本 API ではいずれも拒否する。

| 拒否する名前 | 理由 |
|---|---|
| `unlock` | プロフィール進捗を変更し `SaveProgressFile` で保存するため |
| `cloud` / `leaderboard` / `achievement` / `sentry` / `getlogs` | 外部サービスとの連携・データ送信を行うため |
| `multiplayer` | マルチプレイの操作は本 API の局面作成の範囲外のため |
| `instant` / `trailer` / `art` / `bestiary` / `open` | 画面・設定・表示モード等を操作するため |
| `ancient` | 局面作成には許可済みの `event` で代替できるため |
| `help` / `clear` / `exit` / 未知の名前 | 局面作成・指定の診断用途に含まれないため |

他 MOD の新規名に加え、許可名と同名で上書きした実装も拒否する。
登録実装の `GetType().Assembly == typeof(DevConsole).Assembly` を main 上で確認する。
`log` は設計で明示された例外で、ログレベルの変更を許可する。

`godmode` と `travel` は、送るたびに有効・無効が切り替わる **トグル**。
応答の `message` で切り替え結果を確認する。`GodModeConsoleCmd._godModeActive` はコンソールの
インスタンスとともにゲーム終了まで残るため、別のランでの送信が無効化として働く場合もある。
無効化では Strength / Buffer / Regen の Power を除去し、本来持っていた筋力も消えうる。

名前の判定は `DevConsole.ProcessCommand(string)` と同じ `Trim().Split(' ')[0].ToLowerInvariant()`。
コマンド名の大小文字は区別しない。ASCII スペースだけで分割し、内部のタブや改行を別コマンドの
区切りと解釈しない。引数の引用符・エスケープ・連続空白を MOD で再解釈したり書き換えたりせず、
元の文字列をゲームへ渡す。複数コマンドの実行やシェルとしての解釈は行わない。

## コンソールの生存期間とゲーム側の条件（ソース根拠）

- `NGame.GameStartup` が `NDevConsole.Create` の返すノードを追加する。
  `NDevConsole.Create` は `TestMode.IsOn` で null を返すため、常に存在するとは限らない。
- `NDevConsole._Ready` は `HideConsole()` の後で private `_devConsole` を構築する。
  `HideConsole` は表示・入力を切り替えるだけで、このインスタンスを破棄しない。
  したがってコンソールを開くキー入力や入力欄のフォーカスは不要。
- 同 `_Ready` の登録条件は
  `OS.HasFeature("editor") || TestMode.IsOn || ModManager.IsRunningModded() || SaveManager.Instance.SettingsSave.FullConsole`。
  `DevConsole(bool shouldAllowDebugCommands)` は `DebugOnly` のコマンドを、この条件が true の場合だけ登録する。
  `AbstractConsoleCmd.DebugOnly` は既定 true、許可対象の `LogConsoleCmd` は false。
- MOD は `NDevConsole.Instance` の生存・準備状態を確認し、その `_devConsole` と
  `DevConsole._commands` を非公開フィールドの reflection で読む。
  登録が確認できないときは拒否し、別インスタンスの作成や登録条件の迂回はしない。
- `DevConsole.ProcessCommand` は一部のマルチプレイコマンドをネットワークへ enqueue し、
  Task を返さない。その enqueue を実行完了と扱わないため、本 API はシングルプレイ側だけで提供する。

実機の動作確認は別途必要。ここでの根拠はゲームソースと MOD の実装であり、ライブ確認済みという意味ではない。
