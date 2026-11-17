# 戦闘中の choose-a-card（3 択ポーションの選択画面）の契約試験

本番の `McpMod.ChooseCardSelection.cs` / `McpMod.ChoiceContinuationState.cs` / `McpMod.CardStarCost.cs` /
`McpMod.InternalState.cs` / `McpMod.Warnings.cs` / `McpMod.CardSelectionFormatting.cs`（と Markdown の部品）を直接リンクする。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/libexec/dotnet run --project tests/ChooseCardContract -c Release -p:BuildInParallel=false -p:UseSharedCompilation=false
```

行: `index` が holder の位置（札の無い holder は行を出さず位置を詰めない・warning）、uid registry の更新、
手札と同じ読取順（情報 → details → コスト → 可否）、実個体の uid、`exhaust_on_next_play`、選択前のコストを 0 に加工しないこと、
スターの基底・現在値・一時コストの列、読取で札を変えないこと、戦闘外は従来の行だけ、スターの読取失敗の項目別 null・warning。

継続の listener: hook の二重定義はすべて呼ばれると例外を投げるので、読取が hook を呼ばないことも確かめる。
候補自身と affliction の override、4 通知の順、override した listener だけを列の位置付きで出すこと、
別の overload だけを持つ型を除くこと、所有者の entity_id / player_id、戦闘パイルの札の uid とパイル、
run scope のデッキの札に戦闘 uid を発行せずデッキ位置を出すこと、potion の slot、`listener_count`、
山札の listener（札 2 枚と付属効果）の隠れた並びを入れ替えても行・`order` が完全に同じこと（`order: null`・`order_group_start`・表示順の並び・山札の後ろの `order` は公開の件数）、候補の `energy_cost.global_modifiers_applied: false`、未知の種類の listener での `known: false`、`ModHelper` の subscriber があると列挙せず `known: false`、復旧、Markdown の 1 行。
配線（choose の行の作り方・`continuation_context`・GET に無料化・生成・選択が無いこと・戦闘中の choose だけの `battle`・
マルチ・手札と 4 パイルの `star_cost_state`・`select_card` が札の無い holder を覆いを外す前に拒否すること・schema 4）は
本番ソースの文字列検査。details・可否・エナジーコスト・uid・entity_id は境界ダブル（各契約は別の試験）。
本体の hook の列（`IterateHookListeners`）の中身と順序は境界ダブルで、実際の列は実機で確かめる。

実機（調整役が v0.113.1 の検証用ランで、アタック・スキル・パワー・無色の 4 種のポーションそれぞれで確認する）:

1. 飲んだ直後の choose の state に `battle` があり、`round` / `enemies` が飲む前と同じであること。
   `player.potions` から飲んだポーションが消えていること（`PotionModel.OnUseWrapper` の `RemoveBeforeUse` は `OnUse` の前）。
2. 3 枚の候補に `uid`（`<ID>#g<数>`）・`energy_cost`・`star_cost_state`・`type` などの詳細・`can_play`・`exhaust_on_next_play` があり、
   `cost` と `energy_cost.base` が札の通常のコストのままであること（無料化は選択後。`AttackPotion.OnUse` などの `SetToFreeThisTurn`）。
   アタック・スキル・パワーはキャラの解放済み pool の各型、無色は無色の pool からの 3 枚（`CardFactory.GetDistinctForCombat`）。
3. 1 枚を `select_card index N` で選んだ後の state で、同じ `uid` の札が手札（手札が 10 枚なら捨て札）にあり、
   `energy_cost.local_modifiers` の末尾に `{amount: 0, type: Absolute, expiration: "EndOfTurn, WhenPlayed", reduce_only: false}`、
   `star_cost_state.temporary_costs` の末尾に `{cost: 0, clears_when_turn_ends: true, clears_when_card_is_played: true}` が増えていること
   （`CardModel.SetToFreeThisTurn` → `CardEnergyCost.SetThisTurnOrUntilPlayed(0)` / `SetStarCostThisTurn(0)`）。X コストの札では
   `with_*` が null のまま（`GetWithModifiers` は X を評価しない）。
4. 山札に listener になる札（`STOMP` など）があるとき、その行が `order: null`・`order_group: "draw_pile"` で、`draw_pile` の表示と同じ順に並ぶこと。
   `continuation_context.known: true`。留め具（`BELT_BUCKLE`）や爬虫類の装身具（`REPTILE_TRINKET`）を持てば `after_potion_used` に、
   回転コマ（`UNCEASING_TOP`）を持てば `after_hand_emptied` に行があること。持たない局面では該当の通知の `listeners` が空であること。
   スキップ（`cancel_selection`）では札が増えず、使用後の通知だけが起きること（留め具の `combat_state` で確かめる）。
5. 戦闘外のイベントの choose（あれば）に `battle` や `uid` が付かないこと。
