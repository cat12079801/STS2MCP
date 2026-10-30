# 報酬契約の非依存検査

ゲームを起動せず、本番と同じ `Rewards/RewardSession.cs` と報酬 Markdown formatter を使う。
外部パッケージもゲーム DLL も不要。.NET 9 で実行する。

```fish
dotnet run --project tests/RewardContract/RewardContract.csproj -c Release
```

引数に既存の公開 state JSON のパスを渡すと、`rewards` / `card_reward` だけを読み、
新項目のない旧 state を表示できることも検査する。入力ファイルには書き込まない。
合成テストの新項目を既存観測に付け足さない。

検査対象: 現在一覧の共通列挙・位置変動、ID の維持、未知／取得済み／別画面／別 epoch の拒否、
revision と ID/index の整合性、Skip・SACRIFICE・REROLL・直接取得・中断の状態遷移、
GET 相当の繰り返し列挙、serializer と Markdown の加算互換。
UI の Harmony 接続、実際の非同期解決、ポーション枠チェックはゲーム実機で別途確認する。
