# ゲーム版の互換契約試験

追加パッケージ不要。fish では次で実行する。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/libexec/dotnet run --project tests/V0113CompatContract -c Release
```

本番の `McpMod.CardPreview.cs`・`McpMod.StateStamp.cs`・`McpMod.ShopPurchase.cs` をリンクする。
新版の固定／計算回数、0・1 回、Repeat だけなら回数を出さないこと、複数名の優先順、
解体の弱体あり／なし・悪意の今ターンの HP 喪失あり／なし、親指締め具・強化で増えた
プレビュー回数でも条件不成立なら 1 回に戻すこと、観測済みの 1 ヒットの値と回数の積を検証する。
上限は説明用に出し、ブロックを含むゲームのプレビュー値に再適用しないことも試す。
ゲームの版の有無、singleton／版情報取得の例外時の応答継続と復旧、MOD の版との独立、
購入の成功・拒否・例外・キャンセル・未完了、
未完了 Task の失敗観測を検証する。ソース文字列検査は state/action の配線だけに使う。

ゲーム/Godot の型はダブルであり、ゲームや MOD API に接続しない。
本体の DynamicVar プレビューは [PatchTargetsContract](../PatchTargetsContract/README.md) で照合する。
ショップの通信同期・商品 ID・UI、ターン遷移、実際の release_info.json 読み込みの統合確認は調整役が実機で行う。
商人 API の調査根拠と実機の確認項目は [v0.113 対応メモ](../../docs/v0.113-compat.md) を参照。
