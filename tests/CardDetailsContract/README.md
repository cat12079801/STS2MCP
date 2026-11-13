# 手札とパイルの詳細 state の契約試験

本番の `McpMod.CardDetails.cs` / `McpMod.CardDetailsFormatting.cs` /
`McpMod.PileFormatting.cs` / `McpMod.CardEnergyCostFormatting.cs` /
`McpMod.Warnings.cs` を直接リンクする。追加パッケージ・ゲーム・Godot は不要。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec \
  /opt/homebrew/opt/dotnet@9/libexec/dotnet run \
  --project tests/CardDetailsContract -c Release \
  -p:BuildInParallel=false -p:UseSharedCompilation=false
```

型・対象の enum 名、災禍・エンチャントの ID / Amount、正常な未付与の null、
エンチャントの Status の Normal / Disabled と、読取失敗時の null・理由・他項目の保持、
変数の BaseValue の小数・0・負数、数値変数なしの空辞書を検査する。
StringVar の除外、PreviewValue / EnchantedValue と区別した値の取得と状態の保持、
ゲーム側の成長・付与解除後の再読取と snapshot の独立を確認する。

各 getter、添付モデルの ID / Amount、変数一覧の取得・列挙、要素の失敗で、
null・パス別理由・warnings と、他項目の保持・復旧を検査する。
CanPlay 内の対象 getter・カードロジックの失敗も手札の行に局所化する。
JSON は本番と同じ WhenWritingNull・SnakeCaseLower・Encoder 設定で確認し、
辞書の null、ゲームの変数名の大文字小文字、小数・整数の型を検査する。

Markdown は4パイルの本行末尾の `[Attack/AnyEnemy]` と、
札ごとに1行にまとめた `base Damage 6, …`、Disabled のときだけ付く `(disabled)`、
正常な未付与・空辞書の省略、失敗時の理由、旧形式の行も表示できることを確認する。
巨大な state builder はリンクせず、ソースの文字列検査で手札と4パイルの共通経路、
山札のコピー上での rarity → card ID ソート、非戦闘の対象範囲、マルチの共通経路、
Markdown の配線、schema 4 を確認する。

ゲームの getter は境界ダブルで、返却値・例外・列挙失敗を設定する。
実際の効果適用・パイル移動・成長処理の実機照合は調整役の検証で行う。
v0.113.0 の検証用ランで同じ uid の手札・山札・捨て札・廃棄札を照合し、
Play pile は解決中の snapshot で確認する。
ゲーム build・MOD commit・schema と前後の state を記録する。
