# 手札とパイルの詳細 state の契約試験

本番の `McpMod.CardDetails.cs` / `McpMod.CardDetailsFormatting.cs` /
`McpMod.PileFormatting.cs` / `McpMod.CardEnergyCostFormatting.cs` /
`McpMod.Warnings.cs` / `McpMod.InternalState.cs` を直接リンクする。追加パッケージ・ゲーム・Godot は不要。

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

`is_upgraded` / `is_upgradable` の未強化・最大段階・多段階の途中、`tags` の enum 名と数値順・
タグなしの `[]`・列挙途中の失敗、`UnplayableReason` の複数フラグの名前（ゲームの Flags の値を写した境界ダブル）、
パイルの Markdown の `can_play false (…)` 子行（出せる札には付かない）と `tags` の1行を検査する。

モメンタムの `extra_damage`（private の `_extraDamage`）は、未プレイの 0・プレイ後の伸び・
`amount` と別の値であること・読取で値が変わらないこと・前の snapshot の独立・Disabled でも表示されること、
フィールドの欠落・型違いで null・`enchantment.extra_damage` の理由・warnings と他項目の保持を検査する。
他のエンチャントと災禍にキーが無いことも確かめる。

呪詛（`Hexed`）の札の `local_ethereal` は、札自身に Ethereal が無く呪術の Global の Ethereal だけがある札で false、
札自身に Ethereal がある札で true、読取は `KeywordSources.Local` だけ（Global のフックを通さない）・キーワードの集合を変えないこと、
snapshot の独立、災禍なし・別の災禍・災禍の読取失敗の札にはキーが無くキーワードも読まないこと、
災禍の amount の失敗でも保持されること、読取失敗の null・`local_ethereal` の理由・warnings と災禍の保持、
Markdown の `local ethereal yes / no / null (理由)`、wire の bool・null と理由・省略を検査する。

各 getter、添付モデルの ID / Amount、変数一覧の取得・列挙、要素の失敗で、
null・パス別理由・warnings と、他項目の保持・復旧を検査する。
CanPlay 内の対象 getter・カードロジックの失敗も手札の行に局所化する。
JSON は本番と同じ WhenWritingNull・SnakeCaseLower・Encoder 設定で確認し、
辞書の null、ゲームの変数名の大文字小文字、小数・整数の型を検査する。

Markdown は4パイルの本行末尾の `[Attack/AnyEnemy]` と、
札ごとに1行にまとめた `base Damage 6, …`、Disabled のときだけ付く `(disabled)`、
正常な未付与・空辞書の省略、失敗時の理由、旧形式の行も表示できることを確認する。
巨大な state builder はリンクせず、ソースの文字列検査で手札と4パイルの共通経路
（パイルも詳細の後に手札と同じ `AttachCardPlayability` を通ること）、
パイルの共通の表示順ヘルパー（動作の検査は `CardIdentityContract`）、非戦闘の対象範囲、マルチの共通経路、
Markdown の配線、schema 6 を確認する。

ゲームの getter は境界ダブルで、返却値・例外・列挙失敗を設定する。
実際の効果適用・パイル移動・成長処理の実機照合は調整役の検証で行う。
v0.113.0 の検証用ランで同じ uid の手札・山札・捨て札・廃棄札を照合し、
Play pile は解決中の snapshot で確認する。
ゲーム build・MOD commit・schema と前後の state を記録する。
呪詛の `local_ethereal` は、呪術を受けた戦闘で、札自身にエセリアルが無い札（攻撃・防御など）が false、
正準のキーワードにエセリアルを持つ札（めまい〔`Dazed`〕・アセンダーの災厄〔`AscendersBane`〕など。どちらも `CanonicalKeywords` に Ethereal と Unplayable を持ち、呪詛は `CanAfflictUnplayableCards` が既定の true なので付く）が true になること、
呪術が外れた後（付与元の死亡）にその札から `HEXED` と呪術のエセリアルが消え、true の札だけ手番終了で廃棄されることを照合する。
