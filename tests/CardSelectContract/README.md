# card_select 全候補の契約検証（Issue #803）

.NET 9 の実行プロジェクト。ゲーム DLL・Godot 実行環境・外部パッケージを使わず、
本番の `McpMod.CardSelection.cs` / `McpMod.CardSelectionFormatting.cs` を直接リンクする。
強化プレビューの Markdown 行も本番 `McpMod.RewardFormatting.cs` を共有する。

```fish
dotnet run --project tests/CardSelectContract/CardSelectContract.csproj -c Release
```

復元時もネットワークを使わない場合は、`packageSources` を `clear` した一時的な
NuGet.Config を `--property:RestoreConfigFile=/path/to/NuGet.Config` で渡せる。

0 / 1 / 25 / 26 / 35 / 172 候補の全件・連続 index、holder の並び・可視性・古い割当からの独立、
空の画面側 `_cards` と非空の grid 側 `_cards`、ゲームの候補順と同 ID の別個体を検査する。
action は画面外の index 30、具体 override への一回のモデル引き渡し、選択と解除、
選択 HashSet の置き換え、プレビュー・Peek・上限による拒否、即時クローズ、読取失敗、
呼出例外の内部診断と再試行なしを検査する。メソッド解決の欠落・abstract・異なる引数型も検査する。
候補更新後は state/action が新しい一覧を再取得する。強化画面だけのプレビュー付与、
戦闘条件下だけの uid / exhaust フラグ、全行の Markdown・選択印・null 表示も対象。

ゲームのモデル・ノード・combat registry・カード情報作成・強化プレビュー作成は境界ダブル。
実ゲームのソート、描画、ハイライト、シグナル、効果適用、uid の registry 本体を動かした証拠にはならない。
schema 4、旧 `selected_outside_grid` の削除、state/action からリンク対象への配線は、
巨大な state/action 全体をリンクせず、ビルド出力へコピーした本番ソースへの文字列検査で確認する。
choose は formatter の即時選択案内・見出しを検査し、choose action / bundle / 手札の統合回帰は実機で行う。

実機は調整役が次を確認する（本試験はゲームや MOD API に接続しない）。

1. 35 枚の全札が除去可能なデッキで EMPTY_CAGE を開き、schema 4、35 候補、index 0–34 を確認。
   先頭・中央・末尾へのスクロールと hover 後も候補順・件数・選択印が同じか照合する。
2. 最上部のまま index 30 を選択・解除。再選択して対象までスクロールし、ハイライトを確認。
   別札を加えた 2 枚プレビューで追加 select が拒否され、cancel で解除されることを確認。
   再選択・confirm 後に 35→33 枚となり、指定した 2 個体だけが除去されたか確認する。
3. 強化可能候補を 30 枚以上にして後半の `upgrade_preview` と対象を照合。cancel でデッキ不変、
   confirm で対象だけが強化されることを確認。変化・エンチャント・ドリーの鏡も実効果まで確認する。
4. 戦闘 pile 選択を 30 候補以上にし、ゲームの表示ソート順、画面外選択と本来の効果、
   同名別個体の uid と exhaust フラグ、pile 更新後の state 再取得を確認する。
5. 25 枚以下、simple の手動確認／即時完了、choose、bundle、手札選択、負数／Count index の回帰。
   172 枚は追加の欠落・処理時間・応答サイズ試験とする。

各試験でゲーム build、MOD commit（未コミット版は dirty）、schema、言語、画面寸法、
デッキ枚数と実候補数、前後の state/action、確定前後のデッキを記録する。
戦闘 pile のゲーム側 min/max 判定が生成済み holder 数で切り詰められる既存制約は今回の修正範囲外。
sts2 側の利用コードと full selftest も別途調整役で確認する。
