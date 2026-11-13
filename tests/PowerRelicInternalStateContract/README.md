# パワー・レリックの内部状態の契約試験

本番の `McpMod.InternalState.cs`、`McpMod.PowerInternalState.cs`、
`McpMod.PowerInternalStateFormatting.cs`、`McpMod.RelicCombatState.cs`、
`McpMod.Warnings.cs`、`McpMod.CardIdentity.cs` を直接リンクする。ゲーム・Godot・追加パッケージは不要。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec \
  /opt/homebrew/opt/dotnet@9/libexec/dotnet run \
  --project tests/PowerRelicInternalStateContract -c Release \
  -p:BuildInParallel=false -p:UseSharedCompilation=false
```

公開 getter と private Data／レリックフィールド・プロパティの読取、false・0・小数・負数、
両方向、対象 player 別の盗難量と文字列 ID、空配列、snapshot の独立、
ゲーム側の更新・リセット後の再読取を確認する。
null Data・型不一致・フィールド欠落・宣言型変更・getter失敗・盗難対象異常・
座標配列不整合・現在地欠落は null・理由・warnings と他項目の保持を確認する。
ペン先・冥銭の公開 getter の正常値と例外、毛皮のコートの手書き private プロパティ
（コンパイラの backing field がなくても成功）と各 getter の例外・欠落・型変更も検査する。
スカーフの0・5・6・7と表示カウンターが消えた後の実数、リセット・snapshot の独立、
フィールド欠落／型違い時の null・理由・warnings を検査する。
ジャグリングは待ちなし／予約／解除、デッキ札と同 ID の生成札を本番の uid registry に通し、
他経路の uid と一致することを確認する。Data の null・異型・フィールド欠落／型変更で
readable=false と null・理由になり、正常な待ちなしの readable=true と区別できることを
wire と Markdown でも確認する。
内部データを初期化・書換えしないこと、HUD非表示／可視性不明の追加状態を省略すること、
JSON の本番設定でも null と理由が残ること、Markdown の空・通常の skip=false・
空の盗難配列の省略と、値／失敗理由の短い表示を確認する。

巨大な state builder はソース文字列で、通常行・説明失敗時の最小行、player・敵・ペット、
マルチの共通経路、戦闘中だけのレリック出力、Markdown の配線、schema 4 を検査する。
フィールドと getter はゲーム境界のダブル。効果の適用やリセットを MOD 側では模倣しない。
毛皮のコートは v0.113.0 の戦闘フックどおり、座標を比較し act の条件を足さない。
実ゲームに対する 0警告0エラーのビルドを別途実行し、ゲーム起動・mods への配置はしない。
