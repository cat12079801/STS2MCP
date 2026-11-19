# 開発コンソールの契約検証

`dotnet run --project tests/DevConsoleContract` で実行する。追加パッケージは不要。
本体の `McpMod.DevConsole.cs` をリンクし、設定の明示的有効化、許可リストとトークン境界、
未生成・未準備・未登録時の拒否、元文字列・ゲーム結果の保持、Task 待機・例外・キャンセルを検証する。

ゲーム/Godot の境界はダブルで置き換える。HTTP ルーティング、ゲームの実際のフィールド構造、
メインスレッドへの配送、状態安定待ちを含む統合動作は実機で別途確認する。

`scenario_setup_guard`（`cat12079801/sts2#1037`）: 本体の `McpMod.ScenarioGuard.cs` もリンクし、型・版・画面・未知のキーの誤りを HTTP の側で（配送・state の読取なしに）拒否すること、
実行直前の判定をメインスレッドの配送の中で `ProcessCommand` より前に読み、画面が動いた・許可されない・読めないときに実行しないこと、
空入力はガード付きなら判定を通したうえで従来の拒否文をそのまま返すこと、ガードの無い要求・許可リスト外・無効化の挙動が変わらないことを検証する。
実行直前の判定の読取（`ReadScenarioSetupGuardNow`）はダブル。

`ended_combat`（schema 1.7.0、`cat12079801/sts2#1113`）: `McpMod.EndedCombatCapture.cs` もリンクし、操作の間に戦闘が終わった（取得の通し番号が進んだ）ときだけ
その応答に `ended_combat` を付け、後の操作には繰り返さないこと、`wait` なしでも操作の実行の中で終わったものは付くことを検証する。
戦闘の終わりの列を取る patch（`CombatHistory.Clear` / `CombatManager.ProcessPendingLoss` の前置。敗北の方は別の Harmony ID で入れ、失敗しても列は残る）は実機で確かめる。
