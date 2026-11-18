# 画面の遷移の解決中の契約検証

追加パッケージ不要。fish では次で実行する。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/libexec/dotnet run --project tests/TransitionResolvingContract -c Release
```

本番の `McpMod.TransitionResolving.cs`（`MapTransitionReason`・`RunTransitionReason`・`ChooseMapNodeRefusal`）をリンクする。ゲームの型は使わない。
マップの移動中（`map_traveling`）・閉じる演出中（`map_closing`）・ランの読み込み中（`run_loading`）・部屋の切り替え中（`room_changing`）を
どの値で判定するかと、入力待ちのマップ（部屋の終わりに開いたもの・トップバーで開いたもの）・残った `IsTraveling` で理由が付かないこと、
`choose_map_node` が閉じかけ・移動中・移動の無効なマップを拒否することを検証する。

`AddResolvingState` で入力待ちの画面の消去の後に足すこと、理由を読むのが `map` とラン中の `unknown` だけであること、
入力待ちの画面の一覧・メニューの早い return・A-12 の選択画面の判定順・`ExecuteChooseMapNode` の配線・`map.travel_enabled`・
`IsSettledState`・現行の schema 9 をソース文字列で検査する。

ゲームに接続しない。実際の移動・矢印で閉じる・再開での確認（GET を撃ち続ける）は調整役が実機で行う。
