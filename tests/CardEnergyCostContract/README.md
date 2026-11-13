# カードのエナジーコスト state の契約試験

本番の `McpMod.CardEnergyCost.cs` / `McpMod.CardEnergyCostFormatting.cs` /
`McpMod.PileFormatting.cs` / `McpMod.CardDetailsFormatting.cs` /
`McpMod.Warnings.cs` を直接リンクする。追加パッケージ・ゲーム・Godot は不要。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/libexec/dotnet run --project tests/CardEnergyCostContract -c Release
```

基礎・local・global の取得先の区別、`base_raw` / `canonical` の独立、0、負の sentinel、X、修正の順序・重複、
Relative −1 / WhenPlayed、Absolute 1 / reduceOnly と複合期限、EndOfCombat（値0）、
リストの置換・要素の変更後の再読込と snapshot の独立を検証する。
null／異型／欠落フィールド／壊れた要素、public getter／計算の例外での null・理由・warnings、
他の値の保持と復旧、JSON の数値・bool 型、Markdown の値・期限・理由も確認する。
Markdown は修正一覧が非空・コストの差・通常の適用対象外以外の null で表示し、
通常の未修正札は省略することを検査する。X と raw / canonical の差は表示する。
X の CapturedXValue の0・正数・更新と snapshot の独立、非 X／種別不明では getter を
呼ばず理由付き null にすること、新規 getter／raw計算の失敗時の独立性・warnings・wire null も確認する。Play Pile の節は件数が1以上で表示し、
欠落・負数・0では省略すること、他のパイルの節と JSON は変えないことも確認する。
巨大な state builder はリンクせず、本番ソースの文字列検査で手札・4パイル・
マルチの共通経路、非戦闘の除外、schema 4 を確認する。
grid の戦闘条件・全行への追加と Markdown の配線は `tests/CardSelectContract` で検証する。

ゲームのコスト計算は設定済みの結果を返す境界ダブル。ゲームの計算アルゴリズムは複製しない。
cleanup 後のリスト置換・削除も境界で模擬するため、実際の効果適用・ターン遷移の証拠ではない。
ビルドは現在のゲーム DLL に対して行い、実機検証は調整役が v0.113.0 の検証用ランで行う。

1. 戦いの予感をプレイした後、対象の手札アタックに amount −1 / Relative /
   WhenPlayed / reduce_only false が出ること。次のターンとパイル移動後も残り、
   対象のプレイ後に消えることを同じ uid で照合する。
2. 啓発の通常版は Absolute 1 / EndOfTurn, WhenPlayed / reduce_only true、
   強化版は EndOfCombat が出ること。通常版の失効と強化版のターン越しを照合する。
3. X、コストなし、0コスト、強化で基礎値が変わる札、複数修正、global 修正を照合する。
4. 手札・山札・捨て札・廃棄札・grid の同じ uid の出力と、各 Markdown 表示を照合する。
   Play pile は解決中だけの過渡状態として確認する。

ゲーム build・MOD commit・schema・前後の state を記録する。
