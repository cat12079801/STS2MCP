# ポーションをいま使えるか・誰に使えるか（`player.potions[]`、`cat12079801/sts2#1012`）の契約試験

本番の `McpMod.PotionUsability.cs`（ゲームの型を使わない判定）/ `McpMod.Warnings.cs` を直接リンクする。ゲーム DLL・Godot は不要。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/libexec/dotnet run --project tests/PotionUsabilityContract -c Release -p:BuildInParallel=false -p:UseSharedCompilation=false
```

読取（`PotionUsabilityReads`）は呼ばれた回数を数える境界ダブル。`CanEnableUseButton` は本体の `NPotionPopup.CanEnableUseButton`
（sts2-a671e9ef）の分岐を写したもので、本体を動かした証拠ではない。次を検査する。

- 4 通りの契約: 使用可で宛先あり（`true`・安定 ID の配列・`null`）、宛先指定なし（`true`・`[]`）、既知の禁止（`false`・`[]`・理由）、
  取得失敗・未知の形式（`null`・`null`・`unobserved:<段>`）。未観測を `false` や `[]` にしない
- 判定順（holder が同じ瓶を示す → 予約 → 所有者の死亡 → ラン終了 → `CanUseOrRemovePotions` → `IsPotionUseLocked` → holder の `_isUsable` →
  `_disabledUntilPotionRemoved` → 個別条件・使用区分・CombatOnly の戦闘条件 → 開いている popup → 対象）。禁止が決まった後の読取（対象の UI を含む）をしないこと、
  複数の禁止は先の段の理由
- AnyTime に CombatOnly の手番条件をかけない、Automatic は `automatic`、個別条件の false は `custom_check_failed`、`None`・未知の使用区分は未観測
- 対象: 許可された敵だけを本体の列挙順で、ID 欠測・重複・候補の判定の例外は未観測、0 件は `no_valid_target`、選択の中断条件は `targeting_blocked`
- 行の `unavailable_reasons`（既存の理由を保つ）とトップレベルの `warnings`（`player.potions.<slot>.can_use_now`）、wire の true / false / null / `[]`、Markdown
- `use_potion` の `expected_potion_id`: 読取（無ければ従来どおり、空・非文字列は拒否）と照合（空き枠・差し替え・未観測・禁止・target の欠落と許可外・
  数字だけの target〔combat_id〕は書式のエラー・
  全体効果への ID・直接使用の省略）、拒否の `potion_check`（ゲームのボタン可否と MOD の受付を `stage` で分ける）

本番の state・action・Markdown・MCP サーバーへの配線はゲームの型が要るのでソース文字列で検査する:
`BuildPlayerState` が所持スロットごとに `BuildPotionRow` を通すこと（マルチのローカルプレイヤーも同じ builder）、
観測の経路が popup の生成・Refresh・選択の開始・拒否の VFX・enqueue・在庫の開閉を呼ばないこと、`NPotionPopup.CanEnableUseButton` を直接使うこと、
敵が `IsValidTarget`・操作できるノード（`NCreature.IsInteractable`。味方のクリーチャー経路も）・`Hook.ShouldAllowTargeting`・`IsHittable` を通り battle と同じ ID の registry を使うこと、
passive の `sellable_potions` が在庫を閉じないこと、照合付きの `use_potion` が最新の判定 → 照合 → MOD の受付 → enqueue の順で、敵の補完・在庫の操作をしないこと、
従来の呼び出しの前に分岐すること、`potion_availability: 1`、schema 12。

実機（調整役が v0.113.1 の検証用ランで確認する）:

1. 通常の手番で火炎ポーション 2 本と複数の敵: 各 slot が独立し、`valid_target_entity_ids` が生きて選べる敵の ID と一致する。
2. 敵を 1 体倒した直後: その ID が外れ、他の ID は変わらない。最後の敵を失うと `no_valid_target`（戦闘が終わればそもそも `outside_combat`）。
3. ブロックポーション（AnyPlayer）: `["player:<player_id>"]`。爆発の小瓶: `true`・`[]`。ブラッドポーション: マップでも `true`。妖精の瓶: `false`・`automatic`。
4. 戦闘外の CombatOnly は `outside_combat`、敵の手番は `not_player_turn` か `actions_disabled`、手札・グリッドの選択中は CombatOnly だけ `card_selection`
   （AnyTime はボタン有効のまま。MOD の受付は戦闘中の `PlayerActionsDisabled` で別に拒否する）。使った直後の予約中は `queued`。
5. 悪臭ポーション: 戦闘中は AllEnemies で `[]`、通常のマップは `custom_check_failed`、商店の在庫を閉じていれば `true`・`[]`、開いていれば `custom_check_failed`。
   passive の読取を続けても在庫・所持・予約が変わらない。
