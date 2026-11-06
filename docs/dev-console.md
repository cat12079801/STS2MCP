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
`wait_timeout_ms`（500〜30000 ms、既定10000）は Task 完了後の状態安定待ちに適用する。
`CmdResult.task` は `wait: false` でも完了まで待つ。Task 自体の待機にはこのタイムアウトを
適用しない。ゲームのメインスレッドはブロックせず、HTTP ワーカー側で待つ。
Task が選択操作などを待つ場合、応答もその完了まで待つ。実機での全コマンドの所要時間は未確認。

`command` は送信文字列をそのまま返す。未指定・文字列以外なら `null`（共通 JSON 設定により省略されうる）。
`success` と `message` はゲームの `CmdResult.success` / `msg` に基づく。
Task の例外・キャンセルは `success: false` にし、例外の型とメッセージを返す。
Task が返らないコマンドの完了範囲はゲームの実装次第であり、成功だけで全副作用の完了は保証しない。
また Task の戻り値は解釈しないため、`success: true` は最終 state の検証を置き換えない。

操作失敗は既存契約どおり HTTP 200 の `status: "error"` と `error` に加え、
`command`、`success: false`、`message` を返す。例:

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
シーンツリー外、内部フィールドが読めない、コマンドがゲームの登録済み一覧に存在しない。
登録がない場合はデバッグコマンド無効の可能性を明記する。拒否時は `ProcessCommand` を呼ばない。
メインスレッド待ちの失敗は既存契約の HTTP 503 と `retry: true`。その他の予期しない例外は HTTP 500。
JSON 不正・ルート違いなどアクション到達前のエラーは共通のエラー形式に従う。

送信コマンドと結果は MOD ログの `[STS2 MCP] dev_console` に JSON として記録する。
拒否・Task 失敗も記録し、改行は JSON エスケープする。ゲーム側の履歴保存も
`DevConsole.ProcessCommand` の通常動作として発生する（拒否時には保存されない）。

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

許可以外はすべて拒否する。特に `unlock`、`cloud`、`achievement`、`leaderboard`、
`multiplayer`、`sentry`、`getlogs`、`open`、`art`、`trailer`、`bestiary`、`ancient`、`instant`
は各 `*ConsoleCmd.CmdName` で存在を確認したが、進捗・外部連携・別画面・診断設定等を
広く操作するため、この API の許可対象にしない。`help`、`clear`、`exit`、未知の名前や
他 MOD の新規コマンドも拒否する。`log` は設計で明示された例外で、ログレベルの変更を許可する。

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
