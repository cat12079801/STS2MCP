# 生成した許可リスト（internal_state）の契約試験

本番の `McpMod.StateAllowlist.cs`・`McpMod.StateAllowlistFormatting.cs`・`McpMod.StateAllowlist.Generated.cs` と、
`McpMod.InternalState.cs`・`McpMod.Warnings.cs`・`McpMod.CardIdentity.cs` を直接リンクする。ゲーム・Godot・追加パッケージは不要。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec \
  /opt/homebrew/opt/dotnet@9/libexec/dotnet run \
  --project tests/StateAllowlistContract -c Release \
  -p:BuildInParallel=false -p:UseSharedCompilation=false
```

`Stubs.cs` は v0.113.1（sts2-a671e9ef）のフィールド名・型だけを持つ境界の替え玉で、許可リストは型の完全名でこの試験のアセンブリから型を引く。

確かめること:

- 生成物: 33 項目、section が card / relic / power / player / battle のどれか、分類表の備考が残る、宣言型がモデルの型かその入れ子、
  隠す情報（乱数・`RelicGrabBag`・`PlayerOdds`・山・エンシェントの提示の前の中身）を指さない、同じ section・キーが同じ行に 2 つ当たらない
- カード: 基底の 9 項目の既定値（null は参照が無い・未設定の実値）、複製元の uid は同じ読み取りの山の行と同じ、山に無いカードには uid を振らず null・理由・warnings、
  combat_id の無い対象は null と理由でほかの項目は残る、キーワードは列挙の値の順、派生（Rampage など）の項目は派生の行だけ
- カードの `deck_version_id`（`CardModel._deckVersion`、sts2#1133）: デッキから写した札は元の札のモデル id、同じ id の生成札は理由なしの null、
  参照先のデッキの札に uid を振らない、参照先の id が読めないと null・理由・warnings でほかの項目は残る、markdown は読めたときは出さず読めないときは理由
- パワー: 全行の `amount_on_turn_start`・`applier_combat_id`・`target_combat_id`（無ければ null で理由なし、0 も実在する id、派生の行にも付く、`CombatId` の無い付与元は null・理由・warnings）、呪術の行の直下への写し（`CopyAllowlistedReference`。値・付与元なし・読取失敗で同じ理由と warnings 1 件）、かばう（`InterceptPower`）の `Data` の combat_id の昇順、`Data` が無い・型が違うときは null と理由
- レリック: 基底の `status`・`is_wax`・`floor_added_to_deck`、`CardPlay` をカードの uid に、預けたカードを id の順に、未作成のリストを空に、2 倍にしたパワーの id と持ち主
- player・battle: 自動実装プロパティの読取、味方・逃げた敵の combat_id、当てはまる項目が無い行には `internal_state` を付けない
- 読む処理の守り: 宣言の型が変換と違う・メンバーが無い・宣言した型にしか探さない（`DeclaredOnly`）
- markdown: 既定値を省く（幕の番号は常に出す）、`floor_added_to_deck`・付与元・対象を出さない（読めなかったときは理由を出す）、`amount_on_turn_start` は `amount` と違うときだけ、読めない値は理由を出す
- 本番の組み立てと markdown が各行で呼ぶこと、パワーの 2 経路とも呪術の写しが許可リストの後にあり、パワーの個別の読み取りが付与元を読まないこと（ソースの検査）

- 生成物が分類表と一致すること: `tools/state_inventory.py --check-allowlist` をそのまま回し、終了コードが 0 でなければ落ちる。
  Python は環境変数 `STS2_PYTHON`、無ければ PATH の `python3`（標準ライブラリだけ・3.9 以降）。
- 自動実装プロパティを getter でなく backing field から読むこと（手書きの getter は null と理由）、ラン中に読まれない `wongo_points` を出さないこと、
  幕の番号（`golden_path_act`・`spoils_act_index`）を markdown に常に出すこと

棚卸しの文書（md・tsv）の古さはデコンパイルが要るので、この試験では見ない。`tools/state_inventory.py --check-docs` で確かめる（リポジトリの README）。

パエルの歯では保管札の空・重複・異常な null を区別する。ランの修飾子は、歯の所持、空列、HOARDER を
含む順序と重複、異常な null、報酬・選択・通常画面から共通の run 行への経路と FTUE の早期経路を検査する。
