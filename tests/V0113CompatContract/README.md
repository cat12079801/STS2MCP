# v0.113 互換の契約検証

追加パッケージ不要。fish では次で実行する。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/libexec/dotnet run --project tests/V0113CompatContract -c Release
```

本番の `McpMod.CardPreview.cs`・`McpMod.StateStamp.cs`・`McpMod.ShopPurchase.cs` をリンクする。
新版の固定／計算回数、0・1 回、旧 Repeat、複数名の優先順、上限適用打点、
ゲームの版の有無と MOD の版との独立、購入の成功・拒否・例外・キャンセル・未完了、
未完了 Task の失敗観測を検証する。ソース文字列検査は state/action の配線だけに使う。

ゲーム/Godot の型はダブルであり、ゲームや MOD API に接続しない。
本体の DynamicVar プレビュー、ショップの通信同期・商品 Id・UI、ターン遷移、
実際の release_info.json 読み込みの統合確認は調整役が実機で行う。
調査根拠と実機の確認項目は [対応メモ](../../docs/v0.113-compat.md) を参照。
