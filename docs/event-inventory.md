# 出来事の順番と選択の文脈の棚卸し（event inventory）

> 生成物。`tools/event_inventory.py` が decompile と `docs/event-inventory-classification.json` から作る。手で書き換えない。
> ゲーム v0.113.1（decompile `sts2-a671e9ef`）。MOD の参照は作業ツリーの *.cs から機械的に集める。

## これは何か

`docs/state-inventory.md` はモデルの **項目** を数える。ここは項目ではない情報、つまり戦闘中に **何がどの順で起きたか**
（通知・履歴・山の移動・手番の段階）と、**選択画面がどの文脈で開いたか**（開いたもの・段階・残り回数・候補）を数える。
MOD に 1 件ずつ足す PR（#110〜#115）を止め、public-missing を 1 本の MOD PR（phase 2）でまとめて出すための台帳。

分類:

| 分類 | 意味 |
|---|---|
| exposed | MOD が出している（`exposed_by` の MOD の行を引用。文字列が消えると `--check` が落ちる） |
| public-missing | プレイヤーに見える（または公開のモデルだけから決まる）が MOD が出していない。提案キーと置き場所を持つ |
| hidden | プレイヤーに見えない（山札の順・乱数・抽選の重み） |
| cosmetic | 表示・演出だけ |
| internal | 判定・下請けなど、それ自体は情報でないもの（入力はモデルの項目、結果は別の行に出る） |
| out_of_scope | アイアンクラッドの範囲外（他キャラ・マルチプレイ） |
| unknown | 確かめられていない |

public-missing だった行は phase 2（schema 1.10.0）ですべて exposed にした。出した先は `battle.combat_log`（戦闘ごとの通し列）、
各選択画面の `decision_context`（いまの `choice_source` を内包する）、`player.potion_generation_pool`、手札の `energy_cost.x_value_if_played`。
sts2#1115 が扱っていた行（段階・ドローの残り・シャッフル）は `choice_source.await_chain`（schema 1.9.0）を exposed_by に引く。
`card_resolution_events` は互換のために今の形のまま残す。exposed の表の「JSON キー」は出した先のキー。
「Issue」は open の sts2 の Issue のうち、その情報が揃うと進められるもの（本文と engine の「未観測」の箇所から対応を取った）。

## 件数（カテゴリ × 分類）

| カテゴリ | exposed | public-missing | hidden | cosmetic | internal | out_of_scope | unknown | unclassified | 計 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 通知（Hook の Before/After） | 67 | 0 | 0 | 0 | 0 | 17 | 0 | 0 | 84 |
| 決定時の計算（Modify/Should・生成） | 52 | 0 | 16 | 0 | 12 | 6 | 0 | 0 | 86 |
| 選択の入口と画面の設定 | 19 | 0 | 0 | 4 | 0 | 0 | 0 | 0 | 23 |
| 戦闘の履歴 | 14 | 0 | 0 | 0 | 19 | 4 | 0 | 0 | 37 |
| 山の移動・生成・コマンド | 68 | 0 | 0 | 27 | 1 | 34 | 1 | 0 | 131 |
| 手番の段階（CombatManager） | 12 | 0 | 0 | 2 | 2 | 2 | 0 | 0 | 18 |
| 乱数の系列 | 0 | 0 | 16 | 0 | 0 | 0 | 0 | 0 | 16 |
| その他の決定点（手で登録） | 11 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 11 |
| **計** | **243** | **0** | **32** | **33** | **34** | **63** | **1** | **0** | **406** |

public-missing は 0 件、うちアイアンクラッドの範囲は 0 件。

スコープ（自動）の内訳: core 137, ironclad 221, other-only 48

## public-missing（プレイヤーに見えるが MOD が出していないもの）

無い（phase 2 ですべて exposed にした。下の exposed の表の「JSON キー」が出した先）。

### Issue ごとの行

その情報が揃うと進められる sts2 の open Issue（phase 1 で対応を取った）と、いまの分類。

| Issue | 件数 | うち exposed | キー |
|---|---:|---:|---|
| sts2#1088 複合ドローの区間の strict 照合 | 18 | 18 | `hook:Hook.AfterCardExhausted`, `hook:Hook.BeforeFlush`, `hook:Hook.BeforeHandDraw`, `hook:Hook.AfterModifyingHandDraw`, `hook:Hook.AfterPlayerTurnStart`, `hook:Hook.AfterAutoPostPlayPhaseEntered`, `hook:Hook.BeforeSideTurnEnd`, `hook:Hook.AfterSideTurnEnd`, `computed:Hook.ModifyHandDraw`, `cmd:CardCmd.Exhaust`, `history:CardExhaustedEntry`, `phase:CombatManager.SetupPlayerTurn`, `phase:CombatManager.EndPlayerTurnPhaseOneInternal`, `phase:CombatManager.DoTurnEnd`, `phase:CombatManager.ResolveTurnEndCardEffects`, `phase:CombatManager.EndPlayerTurnPhaseTwoInternal`, `phase:CombatManager.FlushPlayerHand`, `manual:CardPileCmd.DrawInternal` |
| sts2#771 開始時の自動プレイの観測・郷愁 | 13 | 13 | `hook:Hook.AfterCardChangedPiles`, `hook:Hook.BeforeHandDraw`, `hook:Hook.AfterModifyingHandDraw`, `hook:Hook.AfterPlayerTurnStart`, `hook:Hook.AfterAutoPrePlayPhaseEntered`, `hook:Hook.BeforeSideTurnStart`, `hook:Hook.AfterSideTurnStart`, `computed:Hook.ModifyCardPlayResultLocation`, `computed:Hook.ModifyHandDraw`, `cmd:CardPileCmd.Add`, `phase:CombatManager.StartTurn`, `phase:CombatManager.RunAutoPrePlayPhase`, `phase:CombatManager.SetupPlayerTurn` |
| sts2#1115 エントロピー・計略・スプラッシュ（進行中） | 11 | 11 | `hook:Hook.BeforeHandDraw`, `hook:Hook.AfterPlayerTurnStart`, `hook:Hook.AfterShuffle`, `cmd:CardCmd.TransformToRandom`, `cmd:CardPileCmd.Shuffle`, `cmd:CardPileCmd.ShuffleIfNecessary`, `phase:CombatManager.SetupPlayerTurn`, `manual:CardPileCmd.DrawInternal`, `manual:EntropyPower.selection_stage`, `manual:StratagemPower.selection_stage`, `manual:CardSelectCmd.skip_and_result` |
| sts2#991 エントロピー醸造 | 9 | 9 | `hook:Hook.AfterPotionDiscarded`, `hook:Hook.AfterPotionProcured`, `hook:Hook.BeforePotionUsed`, `hook:Hook.AfterPotionUsed`, `cmd:PotionCmd.TryToProcure`, `cmd:PotionCmd.Discard`, `history:PotionUsedEntry`, `computed:PotionFactory.GetPotionOptions`, `manual:PotionModel.OnUseWrapper` |
| sts2#327 山札・捨て札の見立てずれ（再現不能 25 件） | 6 | 6 | `hook:Hook.AfterCardChangedPiles`, `hook:Hook.AfterShuffle`, `computed:Hook.ModifyCardPlayResultLocation`, `cmd:CardPileCmd.Add`, `cmd:CardPileCmd.Shuffle`, `cmd:CardPileCmd.ShuffleIfNecessary` |
| sts2#997 エンシェントカード 11 枚の fixture | 3 | 3 | `hook:Hook.AfterCardChangedPiles`, `cmd:CardPileCmd.Add`, `manual:CardSelectCmd.skip_and_result` |
| sts2#242 engine 改良の第 2 弾（G01 捨て札からの移動） | 2 | 2 | `hook:Hook.AfterCardChangedPiles`, `cmd:CardPileCmd.Add` |
| sts2#790 ギャンブラーの醸造酒 | 2 | 2 | `cmd:CardCmd.DiscardAndDraw`, `history:CardDiscardedEntry` |
| sts2#792 未来視の雫 | 2 | 2 | `hook:Hook.AfterCardChangedPiles`, `cmd:CardPileCmd.Add` |
| sts2#996 ネオーの怒りの回収の選択 | 2 | 2 | `hook:Hook.AfterCardChangedPiles`, `cmd:CardPileCmd.Add` |
| sts2#783 彼方からの遠吠え（ExhaustOnNextPlay） | 1 | 1 | `hook:Hook.AfterAutoPostPlayPhaseEntered` |
| sts2#848 X コストの多段の回数 | 1 | 1 | `computed:Hook.ModifyXValue` |
| sts2#994 エンシェントカードの評価（追跡） | 1 | 1 | `manual:CardSelectCmd.skip_and_result` |
| sts2#1175 クイーン「燃え上がれ」の味方への筋力 | 1 | 1 | `computed:Hook.ShouldAllowHitting` |

## phase 2 の形（schema 1.10.0 で出したもの）

public-missing を行ごとの Harmony の patch にせず、次の 3 つの共通の仕組みにまとめた（契約は `docs/raw-full.md`「戦闘の出来事の通し列」「選択の文脈」）。

1. **`battle.combat_log`（戦闘ごとの通し列）**。行は確定したら変えない（`seq` の順。前の読みが後の読みの先頭になる）。保持は 1 戦闘 2000 行（古い行から捨てる）、state は既定で最後の 100 行、`?combat_log_since=<seq>` でその seq から。新しい戦闘で初めから。行の出どころ:
   - `CombatHistory` の公開の記録メソッド 18 件（それぞれ *Entry を 1 つ作って private の `Add` に渡す）の prefix・postfix で、履歴の全種（draw・play・generated・damage_received・attacked・block_gained・power_received・energy_spent・potion_used・monster_move・card_exhausted・card_discarded・card_afflicted）
   - Hook の通知 53 件は static メソッドの prefix で `kind=hook` の行（通知が始まった時点の列で、巡ごとに上書きしている listener。上書きの無い通知は行にしない。山札の listener の位置は `BuildHookListenerRows` で隠す）。`RelicModel.Flash` / `PowerModel.Flash` は `kind=flash` の行
   - 履歴に残らない命令（`CardPile.AddInternal` での山の移動〔`CardPileCmd.Add` の位置・cloned_by〕・`Shuffle`・`RemoveFromCombat`・`CardCmd.Exhaust` / `DiscardAndDraw` / 変化・`CreatureCmd` の Add / Kill / Escape / HP / ブロック・エナジーの setter・`PotionCmd`・`PowerCmd`）は命令ごとの prefix の行
   - `Hook.ModifyHandDraw` / `ModifyHpLost` / `ModifyPowerAmountGiven` / `ModifyPowerAmountReceived` / `ModifyCardPlayResultLocation` は結果と修正したモデルの `kind=modify` の行（手札の枚数は毎回、他は修正したモデルがあるときだけ。PowerVar のプレビューは除く）
   - 手番の段階 12 件は `CombatManager` の private メソッドの prefix で `kind=turn_phase` の行、選択の結果は `kind=selection` の行
   - シャッフルの `CardPileCmd.Add` の間の行は、新しい山札の順に並ぶので出さず、件数だけを `kind=hidden_rows` で出す
2. **`decision_context`（全選択画面）**。入口（`selector`・`outer_selectors`）、開いた元（呼出し履歴でいちばん近いゲームのフレーム）、開いた時点の `combat_log` の seq、戦闘中は `choice_source`（await_chain で段階・ドローの残り）
`--check` は `exposed_by` の文字列が MOD にあるかしか見ない。patch が実際に当たるかは `tests/PatchTargetsContract`（本物の `sts2.dll` に対して install のメソッドを呼ぶ）と、実機の state の `combat_log.unavailable_reasons`（群ごとに `install: <群>: …`）で見る。
3. **計算値**: `player.potion_generation_pool`（`PotionFactory.GetPotionOptions`）と、手札の X の札の `energy_cost.x_value_if_played`（`Hook.ModifyXValue`）。

## exposed（MOD が出しているもの）

| キー | JSON キー | MOD の箇所 |
|---|---|---|
| `cmd:CardCmd.Afflict` | カードの項目（is_upgraded・enchantment・affliction・keywords） | McpMod.CardDetails.cs:45 |
| `cmd:CardCmd.ApplyKeyword` | カードの項目（is_upgraded・enchantment・affliction・keywords） | McpMod.StateBuilder.cs:1864 |
| `cmd:CardCmd.ApplySingleTurnRetain` | カードの項目（is_upgraded・enchantment・affliction・keywords） | McpMod.StateBuilder.cs:1864 |
| `cmd:CardCmd.AutoPlay` | card_resolution_events[kind=play].is_auto_play | McpMod.CombatHistory.cs:282 |
| `cmd:CardCmd.ClearAffliction` | カードの項目（is_upgraded・enchantment・affliction・keywords） | McpMod.CardDetails.cs:45 |
| `cmd:CardCmd.ClearEnchantment` | カードの項目（is_upgraded・enchantment・affliction・keywords） | McpMod.CardDetails.cs:63 |
| `cmd:CardCmd.DiscardAndDraw` | battle.combat_log[kind=discard_and_draw]{cards, draw_count} と後続の card_discarded・draw の行 | McpMod.CombatLogPatches.cs:795 |
| `cmd:CardCmd.Downgrade` | カードの項目（is_upgraded・enchantment・affliction・keywords） | McpMod.StateBuilder.cs:1860 |
| `cmd:CardCmd.Enchant` | カードの項目（is_upgraded・enchantment・affliction・keywords） | McpMod.CardDetails.cs:63 |
| `cmd:CardCmd.Exhaust` | battle.combat_log[kind=exhaust]{card, caused_by_ethereal} と card_exhausted の行 | McpMod.CombatLogPatches.cs:783, McpMod.CombatLogPatches.cs:382 |
| `cmd:CardCmd.RemoveKeyword` | カードの項目（is_upgraded・enchantment・affliction・keywords） | McpMod.StateBuilder.cs:1864 |
| `cmd:CardCmd.Transform` | battle.combat_log[kind=card_change,change=transform]{pile, from, to} | McpMod.CombatLogPatches.cs:810 |
| `cmd:CardCmd.TransformTo` | battle.combat_log[kind=card_change,change=transform]{pile, from, to} | McpMod.CombatLogPatches.cs:810 |
| `cmd:CardCmd.TransformToRandom` | battle.combat_log[kind=card_change,change=transform]{pile, from, to} | McpMod.CombatLogPatches.cs:810 |
| `cmd:CardCmd.Upgrade` | カードの項目（is_upgraded・enchantment・affliction・keywords） | McpMod.StateBuilder.cs:1860 |
| `cmd:CardPileCmd.Add` | battle.combat_log[kind=pile_move]{card\|hidden_reason, from, to, requested_to, position, cloned_by} | McpMod.CombatLogPatches.cs:715 |
| `cmd:CardPileCmd.AddCurseToDeck` | player.deck | McpMod.StateBuilder.cs:1832 |
| `cmd:CardPileCmd.AddCursesToDeck` | player.deck | McpMod.StateBuilder.cs:1832 |
| `cmd:CardPileCmd.AddGeneratedCardToCombat` | card_resolution_events[kind=generated] | McpMod.CombatHistory.cs:235 |
| `cmd:CardPileCmd.AddGeneratedCardsToCombat` | card_resolution_events[kind=generated] | McpMod.CombatHistory.cs:235 |
| `cmd:CardPileCmd.AddToCombatAndPreview` | card_resolution_events[kind=generated] | McpMod.CombatHistory.cs:235 |
| `cmd:CardPileCmd.AutoPlayFromDrawPile` | card_resolution_events[kind=auto_play_reservation] | McpMod.CombatHistory.cs:183 |
| `cmd:CardPileCmd.Draw` | card_resolution_events[kind=draw] | McpMod.CombatHistory.cs:228, McpMod.CombatHistory.cs:230 |
| `cmd:CardPileCmd.DrawWithoutBlockingOnOtherPlayers` | card_resolution_events[kind=draw] | McpMod.CombatHistory.cs:228 |
| `cmd:CardPileCmd.RemoveFromCombat` | battle.combat_log[kind=removed_from_combat]{cards} | McpMod.CombatLogPatches.cs:737 |
| `cmd:CardPileCmd.RemoveFromDeck` | player.deck | McpMod.StateBuilder.cs:1832 |
| `cmd:CardPileCmd.Shuffle` | battle.combat_log[kind=shuffle]{reason, discard_count, draw_count} と choice_source.await_chain の shuffle | McpMod.CombatLogPatches.cs:755, McpMod.ChoiceAwaitChain.cs:61 |
| `cmd:CardPileCmd.ShuffleIfNecessary` | battle.combat_log[kind=shuffle,reason=draw_pile_empty] と choice_source.await_chain の shuffle_if_necessary | McpMod.CombatLogPatches.cs:748, McpMod.ChoiceAwaitChain.cs:62 |
| `cmd:CreatureCmd.Add` | battle.combat_log[kind=creature,action=added] | McpMod.CombatLogPatches.cs:824 |
| `cmd:CreatureCmd.Damage` | battle.combat_log[kind=damage_received]{dealer, card_source, result} | McpMod.CombatLogPatches.cs:385 |
| `cmd:CreatureCmd.Escape` | battle.combat_log[kind=creature,action=escape] | McpMod.CombatLogPatches.cs:848 |
| `cmd:CreatureCmd.GainBlock` | battle.combat_log[kind=block_gained]{amount, props, card_play_uid} | McpMod.CombatLogPatches.cs:384 |
| `cmd:CreatureCmd.GainMaxHp` | battle.combat_log[kind=hp,action=gain_max_hp]{target, amount, hp_before, max_hp_before} | McpMod.CombatLogPatches.cs:283 |
| `cmd:CreatureCmd.Heal` | battle.combat_log[kind=hp,action=heal]{target, amount, hp_before, max_hp_before} | McpMod.CombatLogPatches.cs:281 |
| `cmd:CreatureCmd.Kill` | battle.combat_log[kind=creature,action=kill]{creatures, card_source, force} | McpMod.CombatLogPatches.cs:837 |
| `cmd:CreatureCmd.LoseBlock` | battle.combat_log[kind=block,action=lose_block]{target, amount, block_before, remover} | McpMod.CombatLogPatches.cs:287 |
| `cmd:CreatureCmd.LoseMaxHp` | battle.combat_log[kind=hp,action=lose_max_hp]{target, amount, hp_before, max_hp_before} | McpMod.CombatLogPatches.cs:284 |
| `cmd:CreatureCmd.SetCurrentHp` | battle.combat_log[kind=hp,action=set_current_hp]{target, amount, hp_before, max_hp_before} | McpMod.CombatLogPatches.cs:282 |
| `cmd:CreatureCmd.SetMaxAndCurrentHp` | battle.combat_log[kind=hp,action=set_max_and_current_hp]{target, amount, hp_before, max_hp_before} | McpMod.CombatLogPatches.cs:286 |
| `cmd:CreatureCmd.SetMaxHp` | battle.combat_log[kind=hp,action=set_max_hp]{target, amount, hp_before, max_hp_before} | McpMod.CombatLogPatches.cs:285 |
| `cmd:CreatureCmd.Stun` | enemies[].is_stun | McpMod.EnemyStunHandler.cs:61 |
| `cmd:DamageCmd.Attack` | battle.combat_log[kind=attacked]{actor, hits[]} | McpMod.CombatLogPatches.cs:386 |
| `cmd:MapCmd.SetBossEncounter` | map.boss | McpMod.StateBuilder.cs:2819 |
| `cmd:PlayerCmd.AddPet` | player.pets | McpMod.StateBuilder.cs:1731 |
| `cmd:PlayerCmd.GainEnergy` | battle.combat_log[kind=energy]{player_id, before, after}（PlayerCombatState.Energy の setter。どの命令でも） | McpMod.CombatLogPatches.cs:883 |
| `cmd:PlayerCmd.GainGold` | player.gold | McpMod.StateBuilder.cs:1735 |
| `cmd:PlayerCmd.GainMaxPotionCount` | player.max_potion_slots | McpMod.StateBuilder.cs:1793 |
| `cmd:PlayerCmd.LoseEnergy` | battle.combat_log[kind=energy]{player_id, before, after}（PlayerCombatState.Energy の setter。どの命令でも） | McpMod.CombatLogPatches.cs:883 |
| `cmd:PlayerCmd.LoseGold` | player.gold | McpMod.StateBuilder.cs:1735 |
| `cmd:PlayerCmd.LoseMaxPotionCount` | player.max_potion_slots | McpMod.StateBuilder.cs:1793 |
| `cmd:PlayerCmd.MimicRestSiteHeal` | rest_site | McpMod.StateBuilder.cs:603 |
| `cmd:PlayerCmd.SetEnergy` | battle.combat_log[kind=energy]{player_id, before, after}（PlayerCombatState.Energy の setter。どの命令でも） | McpMod.CombatLogPatches.cs:883 |
| `cmd:PlayerCmd.SetGold` | player.gold | McpMod.StateBuilder.cs:1735 |
| `cmd:PotionCmd.Discard` | battle.combat_log[kind=potion,action=discard] | McpMod.CombatLogPatches.cs:921 |
| `cmd:PotionCmd.TryToProcure` | battle.combat_log[kind=potion,action=procure] と action=added{success, failure_reason} | McpMod.CombatLogPatches.cs:897, McpMod.CombatLogPatches.cs:913 |
| `cmd:PowerCmd.Apply` | battle.combat_log[kind=power,action=apply]{power_id, target, amount, applier, card_source} と power_received の行 | McpMod.CombatLogPatches.cs:932 |
| `cmd:PowerCmd.Decrement` | battle.combat_log[kind=power,action=decrement] | McpMod.CombatLogPatches.cs:957 |
| `cmd:PowerCmd.ModifyAmount` | battle.combat_log[kind=power,action=modify_amount]{power, offset, applier, card_source} | McpMod.CombatLogPatches.cs:945 |
| `cmd:PowerCmd.Remove` | battle.combat_log[kind=power,action=remove] | McpMod.CombatLogPatches.cs:978 |
| `cmd:PowerCmd.TickDownDuration` | battle.combat_log[kind=power,action=tick_down]{power, skipped} | McpMod.CombatLogPatches.cs:967 |
| `cmd:RelicCmd.Melt` | player.relics[].melted | McpMod.StateBuilder.cs:1758 |
| `cmd:RelicCmd.Obtain` | player.relics | McpMod.StateBuilder.cs:1780 |
| `cmd:RelicCmd.Remove` | player.relics | McpMod.StateBuilder.cs:1780 |
| `cmd:RelicCmd.Replace` | player.relics | McpMod.StateBuilder.cs:1780 |
| `cmd:RewardsCmd.GenerateCustom` | rewards | McpMod.StateBuilder.cs:479 |
| `cmd:RewardsCmd.GenerateForRoomEnd` | rewards | McpMod.StateBuilder.cs:479 |
| `cmd:RewardsCmd.OfferCustom` | rewards | McpMod.StateBuilder.cs:479 |
| `cmd:RewardsCmd.OfferForRoomEnd` | rewards | McpMod.StateBuilder.cs:479 |
| `computed:CardFactory.CreateForMerchant` | shop | McpMod.StateBuilder.cs:590 |
| `computed:CardFactory.CreateForReward` | card_reward | McpMod.StateBuilder.cs:474 |
| `computed:CardFactory.CreateRandomCardForTransform` | player.transformation_options | McpMod.StateBuilder.cs:1693 |
| `computed:CardFactory.FilterForCombat` | player.generation_pool | McpMod.StateBuilder.cs:1639 |
| `computed:CardFactory.GetDefaultTransformationOptions` | player.transformation_options | McpMod.StateBuilder.cs:1693 |
| `computed:CardFactory.GetDistinctForCombat` | player.generation_pool | McpMod.StateBuilder.cs:1639 |
| `computed:CardFactory.GetForCombat` | player.generation_pool | McpMod.StateBuilder.cs:1639 |
| `computed:Hook.ModifyAttackHitCount` | hand[].preview.hits | McpMod.CardPreview.cs:20 |
| `computed:Hook.ModifyBlock` | hand[].preview.block | McpMod.CardPreview.cs:27 |
| `computed:Hook.ModifyCardBeingAddedToDeck` | player.deck | McpMod.StateBuilder.cs:1832 |
| `computed:Hook.ModifyCardPlayCount` | card_resolution_events[kind=play].play_count | McpMod.CombatHistory.cs:285 |
| `computed:Hook.ModifyCardPlayResultLocation` | battle.combat_log[kind=modify,hook=ModifyCardPlayResultLocation]{card, original, result, modifiers} | McpMod.CombatLogPatches.cs:628 |
| `computed:Hook.ModifyCardRewardAlternatives` | card_reward | McpMod.StateBuilder.cs:474 |
| `computed:Hook.ModifyCardRewardCreationOptions` | card_reward | McpMod.StateBuilder.cs:474 |
| `computed:Hook.ModifyDamage` | hand[].vs_targets / enemies[].intents | McpMod.StateBuilder.cs:1924, McpMod.StateBuilder.cs:2224 |
| `computed:Hook.ModifyEnergyCostInCombat` | hand[].energy_cost | McpMod.StateBuilder.cs:1914 |
| `computed:Hook.ModifyExtraRestSiteHealText` | rest_site | McpMod.StateBuilder.cs:603 |
| `computed:Hook.ModifyExtraRestSiteMendText` | rest_site | McpMod.StateBuilder.cs:603 |
| `computed:Hook.ModifyGeneratedMap` | map | McpMod.StateBuilder.cs:2813 |
| `computed:Hook.ModifyGeneratedMapLate` | map | McpMod.StateBuilder.cs:2813 |
| `computed:Hook.ModifyGoldGained` | player.gold | McpMod.StateBuilder.cs:1735 |
| `computed:Hook.ModifyHandDraw` | battle.combat_log[kind=modify,hook=ModifyHandDraw]{original, result, modifiers} | McpMod.CombatLogPatches.cs:553 |
| `computed:Hook.ModifyHpLost` | battle.combat_log[kind=modify,hook=ModifyHpLost]{phase, target, original, result, modifiers} | McpMod.CombatLogPatches.cs:571 |
| `computed:Hook.ModifyKeywordsInCombat` | hand[].keywords | McpMod.StateBuilder.cs:1864 |
| `computed:Hook.ModifyMaxEnergy` | player.max_energy | McpMod.StateBuilder.cs:1660 |
| `computed:Hook.ModifyMerchantCardCreationResults` | shop | McpMod.StateBuilder.cs:590 |
| `computed:Hook.ModifyMerchantPrice` | shop.*.price | McpMod.StateBuilder.cs:2497 |
| `computed:Hook.ModifyPowerAmountGiven` | battle.combat_log[kind=modify,hook=ModifyPowerAmountGiven]{power_id, giver, target, original, result, modifiers} | McpMod.CombatLogPatches.cs:591 |
| `computed:Hook.ModifyPowerAmountReceived` | battle.combat_log[kind=modify,hook=ModifyPowerAmountReceived]{power_id, giver, target, original, result, modifiers} | McpMod.CombatLogPatches.cs:599 |
| `computed:Hook.ModifyPreBlockDamageTarget` | battle.combat_log[kind=damage_received].result{receiver, original_target}・attacked.hits[] | McpMod.CombatLogPatches.cs:462 |
| `computed:Hook.ModifyRestSiteHealAmount` | rest_site | McpMod.StateBuilder.cs:603 |
| `computed:Hook.ModifyRestSiteHealRewards` | rest_site | McpMod.StateBuilder.cs:603 |
| `computed:Hook.ModifyRestSiteOptions` | rest_site | McpMod.StateBuilder.cs:603 |
| `computed:Hook.ModifyRewards` | rewards | McpMod.StateBuilder.cs:479 |
| `computed:Hook.ModifyStarCost` | hand[].star_cost_state | McpMod.StateBuilder.cs:1917 |
| `computed:Hook.ModifyUnblockedDamageTarget` | battle.combat_log[kind=damage_received].result{receiver, original_target}・attacked.hits[] | McpMod.CombatLogPatches.cs:462 |
| `computed:Hook.ModifyXValue` | player.hand[].energy_cost.x_value_if_played | McpMod.CardXValue.cs:29 |
| `computed:Hook.ShouldAddToDeck` | player.deck | McpMod.StateBuilder.cs:1832 |
| `computed:Hook.ShouldAllowFreeTravel` | map | McpMod.StateBuilder.cs:2813 |
| `computed:Hook.ShouldAllowHitting` | enemies[].hittable, enemies[].can_receive_powers | McpMod.EnemyHittable.cs:18, McpMod.EnemyCanReceivePowers.cs:23 |
| `computed:Hook.ShouldAllowMerchantCardRemoval` | shop | McpMod.StateBuilder.cs:590 |
| `computed:Hook.ShouldAllowSelectingMoreCardRewards` | rewards | McpMod.StateBuilder.cs:479 |
| `computed:Hook.ShouldAllowTargeting` | potions[].valid_target_entity_ids | McpMod.PotionUsabilityLive.cs:251 |
| `computed:Hook.ShouldDisableRemainingRestSiteOptions` | rest_site | McpMod.StateBuilder.cs:603 |
| `computed:Hook.ShouldForcePotionReward` | rewards | McpMod.StateBuilder.cs:479 |
| `computed:Hook.ShouldGenerateTreasure` | rewards | McpMod.StateBuilder.cs:479 |
| `computed:Hook.ShouldPlay` | hand[].can_play | McpMod.CardDetails.cs:22 |
| `computed:Hook.ShouldProceedToNextMapPoint` | map | McpMod.StateBuilder.cs:2813 |
| `computed:Hook.ShouldRefillMerchantEntry` | shop | McpMod.StateBuilder.cs:590 |
| `computed:Hook.TryModifyCardRewardOptions` | card_reward | McpMod.StateBuilder.cs:474 |
| `computed:Hook.TryReplaceDefaultRoomRewards` | rewards | McpMod.StateBuilder.cs:479 |
| `computed:PotionFactory.GetPotionOptions` | player.potion_generation_pool{potions[{id, rarity, can_be_generated_in_combat}]} | McpMod.PotionGenerationPool.cs:33 |
| `history:BlockGainedEntry` | battle.combat_log[kind=block_gained,source=history] | McpMod.CombatLogPatches.cs:384 |
| `history:CardAfflictedEntry` | battle.combat_log[kind=card_afflicted,source=history] | McpMod.CombatLogPatches.cs:383 |
| `history:CardDiscardedEntry` | battle.combat_log[kind=card_discarded,source=history] | McpMod.CombatLogPatches.cs:381 |
| `history:CardDrawnEntry` | card_resolution_events[kind=draw] | McpMod.CombatHistory.cs:228 |
| `history:CardExhaustedEntry` | battle.combat_log[kind=card_exhausted,source=history] | McpMod.CombatLogPatches.cs:382 |
| `history:CardGeneratedEntry` | card_resolution_events[kind=generated] | McpMod.CombatHistory.cs:235 |
| `history:CardPlayFinishedEntry` | card_resolution_events[kind=play,phase=finished] | McpMod.CombatHistory.cs:282 |
| `history:CardPlayStartedEntry` | card_resolution_events[kind=play,phase=started] | McpMod.CombatHistory.cs:282 |
| `history:CreatureAttackedEntry` | battle.combat_log[kind=attacked,source=history] | McpMod.CombatLogPatches.cs:386 |
| `history:DamageReceivedEntry` | battle.combat_log[kind=damage_received,source=history] | McpMod.CombatLogPatches.cs:385 |
| `history:EnergySpentEntry` | battle.combat_log[kind=energy_spent,source=history] | McpMod.CombatLogPatches.cs:387 |
| `history:MonsterPerformedMoveEntry` | battle.combat_log[kind=monster_move,source=history] | McpMod.CombatLogPatches.cs:388 |
| `history:PotionUsedEntry` | battle.combat_log[kind=potion_used,source=history] | McpMod.CombatLogPatches.cs:389 |
| `history:PowerReceivedEntry` | battle.combat_log[kind=power_received,source=history] | McpMod.CombatLogPatches.cs:390 |
| `hook:Hook.AfterActEntered` | map | McpMod.StateBuilder.cs:2813 |
| `hook:Hook.AfterAttack` | battle.combat_log[kind=hook,hook=AfterAttack]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:36 |
| `hook:Hook.AfterAutoPostPlayPhaseEntered` | battle.combat_log[kind=hook,hook=AfterAutoPostPlayPhaseEntered]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:37 |
| `hook:Hook.AfterAutoPrePlayPhaseEntered` | battle.combat_log[kind=hook,hook=AfterAutoPrePlayPhaseEntered]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:38 |
| `hook:Hook.AfterBlockBroken` | battle.combat_log[kind=hook,hook=AfterBlockBroken]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:39 |
| `hook:Hook.AfterBlockCleared` | battle.combat_log[kind=hook,hook=AfterBlockCleared]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:40 |
| `hook:Hook.AfterBlockGained` | battle.combat_log[kind=hook,hook=AfterBlockGained]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:41 |
| `hook:Hook.AfterCardChangedPiles` | battle.combat_log[kind=hook,hook=AfterCardChangedPiles]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:42 |
| `hook:Hook.AfterCardDrawn` | battle.combat_log[kind=hook,hook=AfterCardDrawn]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:43 |
| `hook:Hook.AfterCardEnteredCombat` | battle.combat_log[kind=hook,hook=AfterCardEnteredCombat]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:44 |
| `hook:Hook.AfterCardExhausted` | battle.combat_log[kind=hook,hook=AfterCardExhausted]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:45 |
| `hook:Hook.AfterCardGeneratedForCombat` | battle.combat_log[kind=hook,hook=AfterCardGeneratedForCombat]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:46 |
| `hook:Hook.AfterCardPlayed` | battle.combat_log[kind=hook,hook=AfterCardPlayed]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:47 |
| `hook:Hook.AfterCombatEnd` | battle.combat_log[kind=hook,hook=AfterCombatEnd]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:48 |
| `hook:Hook.AfterCombatVictory` | battle.combat_log[kind=hook,hook=AfterCombatVictory]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:49 |
| `hook:Hook.AfterCreatureAddedToCombat` | battle.combat_log[kind=hook,hook=AfterCreatureAddedToCombat]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:50 |
| `hook:Hook.AfterCurrentHpChanged` | battle.combat_log[kind=hook,hook=AfterCurrentHpChanged]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:51 |
| `hook:Hook.AfterDamageGiven` | battle.combat_log[kind=hook,hook=AfterDamageGiven]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:52 |
| `hook:Hook.AfterDamageReceived` | battle.combat_log[kind=hook,hook=AfterDamageReceived]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:53 |
| `hook:Hook.AfterDeath` | battle.combat_log[kind=hook,hook=AfterDeath]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:54 |
| `hook:Hook.AfterEnergyReset` | battle.combat_log[kind=hook,hook=AfterEnergyReset]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:55 |
| `hook:Hook.AfterGoldGained` | player.gold | McpMod.StateBuilder.cs:1735 |
| `hook:Hook.AfterHandEmptied` | battle.combat_log[kind=hook,hook=AfterHandEmptied]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:56 |
| `hook:Hook.AfterItemPurchased` | shop | McpMod.StateBuilder.cs:590 |
| `hook:Hook.AfterMapGenerated` | map.nodes | McpMod.StateBuilder.cs:2813 |
| `hook:Hook.AfterModifyingBlockAmount` | battle.combat_log[kind=hook,hook=AfterModifyingBlockAmount]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:57 |
| `hook:Hook.AfterModifyingCardPlayCount` | battle.combat_log[kind=hook,hook=AfterModifyingCardPlayCount]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:58 |
| `hook:Hook.AfterModifyingCardRewardOptions` | card_reward | McpMod.StateBuilder.cs:474 |
| `hook:Hook.AfterModifyingDamageAmount` | battle.combat_log[kind=hook,hook=AfterModifyingDamageAmount]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:59 |
| `hook:Hook.AfterModifyingGoldGained` | player.gold | McpMod.StateBuilder.cs:1735 |
| `hook:Hook.AfterModifyingHandDraw` | battle.combat_log[kind=hook,hook=AfterModifyingHandDraw]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:60 |
| `hook:Hook.AfterModifyingHpLostAfterOsty` | battle.combat_log[kind=hook,hook=AfterModifyingHpLostAfterOsty]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:61 |
| `hook:Hook.AfterModifyingHpLostBeforeOsty` | battle.combat_log[kind=hook,hook=AfterModifyingHpLostBeforeOsty]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:62 |
| `hook:Hook.AfterModifyingPowerAmountReceived` | battle.combat_log[kind=hook,hook=AfterModifyingPowerAmountReceived]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:63 |
| `hook:Hook.AfterModifyingRewards` | rewards | McpMod.StateBuilder.cs:479 |
| `hook:Hook.AfterPlayerTurnStart` | battle.combat_log[kind=hook,hook=AfterPlayerTurnStart]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:64, McpMod.ChoiceAwaitChain.cs:32 |
| `hook:Hook.AfterPotionDiscarded` | battle.combat_log[kind=hook,hook=AfterPotionDiscarded]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:65 |
| `hook:Hook.AfterPotionProcured` | battle.combat_log[kind=hook,hook=AfterPotionProcured]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:66 |
| `hook:Hook.AfterPotionUsed` | battle.combat_log[kind=hook,hook=AfterPotionUsed]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:67 |
| `hook:Hook.AfterPowerAmountChanged` | battle.combat_log[kind=hook,hook=AfterPowerAmountChanged]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:68 |
| `hook:Hook.AfterPreventingBlockClear` | battle.combat_log[kind=hook,hook=AfterPreventingBlockClear]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:69 |
| `hook:Hook.AfterPreventingDeath` | battle.combat_log[kind=hook,hook=AfterPreventingDeath]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:70 |
| `hook:Hook.AfterPreventingDraw` | battle.combat_log[kind=hook,hook=AfterPreventingDraw]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:71 |
| `hook:Hook.AfterRestSiteHeal` | rest_site | McpMod.StateBuilder.cs:603 |
| `hook:Hook.AfterRestSiteSmith` | rest_site / player.deck | McpMod.StateBuilder.cs:603 |
| `hook:Hook.AfterRewardTaken` | rewards | McpMod.StateBuilder.cs:479 |
| `hook:Hook.AfterRoomEntered` | player.relics / hp / gold | McpMod.StateBuilder.cs:1780 |
| `hook:Hook.AfterShuffle` | battle.combat_log[kind=hook,hook=AfterShuffle]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:72, McpMod.ChoiceAwaitChain.cs:33 |
| `hook:Hook.AfterSideTurnEnd` | battle.combat_log[kind=hook,hook=AfterSideTurnEnd]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:73 |
| `hook:Hook.AfterSideTurnStart` | battle.combat_log[kind=hook,hook=AfterSideTurnStart]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:74 |
| `hook:Hook.AfterTakingExtraTurn` | battle.combat_log[kind=hook,hook=AfterTakingExtraTurn]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:75 |
| `hook:Hook.BeforeAttack` | battle.combat_log[kind=hook,hook=BeforeAttack]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:76 |
| `hook:Hook.BeforeBlockGained` | battle.combat_log[kind=hook,hook=BeforeBlockGained]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:77 |
| `hook:Hook.BeforeCardAutoPlayed` | battle.combat_log[kind=hook,hook=BeforeCardAutoPlayed]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:78 |
| `hook:Hook.BeforeCardPlayed` | battle.combat_log[kind=hook,hook=BeforeCardPlayed]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:79 |
| `hook:Hook.BeforeCardRemoved` | player.deck | McpMod.StateBuilder.cs:1832 |
| `hook:Hook.BeforeCombatRewardOffered` | rewards | McpMod.StateBuilder.cs:479 |
| `hook:Hook.BeforeCombatStart` | battle.combat_log[kind=hook,hook=BeforeCombatStart]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:80 |
| `hook:Hook.BeforeDamageReceived` | battle.combat_log[kind=hook,hook=BeforeDamageReceived]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:81 |
| `hook:Hook.BeforeDeath` | battle.combat_log[kind=hook,hook=BeforeDeath]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:82 |
| `hook:Hook.BeforeFlush` | battle.combat_log[kind=hook,hook=BeforeFlush]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:83 |
| `hook:Hook.BeforeHandDraw` | battle.combat_log[kind=hook,hook=BeforeHandDraw]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:84, McpMod.ChoiceAwaitChain.cs:38 |
| `hook:Hook.BeforePotionUsed` | battle.combat_log[kind=hook,hook=BeforePotionUsed]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:85 |
| `hook:Hook.BeforePowerAmountChanged` | battle.combat_log[kind=hook,hook=BeforePowerAmountChanged]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:86 |
| `hook:Hook.BeforeRoomEntered` | map / state_type | McpMod.StateBuilder.cs:2813 |
| `hook:Hook.BeforeSideTurnEnd` | battle.combat_log[kind=hook,hook=BeforeSideTurnEnd]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:87 |
| `hook:Hook.BeforeSideTurnStart` | battle.combat_log[kind=hook,hook=BeforeSideTurnStart]{scope, args, passes[{method, listeners[]}]} | McpMod.CombatLogPatches.cs:88 |
| `manual:CardModel.OnPlayWrapper` | card_resolution_events[kind=play] | McpMod.CombatHistory.cs:282 |
| `manual:CardPileCmd.DrawInternal` | choice_source.await_chain の draw{draws_requested, draw_index, remaining_draws, from_hand_draw}（選択の間） | McpMod.ChoiceAwaitChain.cs:59 |
| `manual:CardReward` | card_reward | McpMod.StateBuilder.cs:474 |
| `manual:CardSelectCmd.skip_and_result` | battle.combat_log[kind=selection]{selector, opener, opened_at_seq, offered\|offered_hidden_reason, chosen, skipped} | McpMod.DecisionContext.cs:259 |
| `manual:CombatState.IterateHookListeners` | <screen>.choice_source.hook.remaining_listeners | McpMod.ChoiceSource.cs:343 |
| `manual:EntropyPower.selection_stage` | <screen>.decision_context.choice_source.await_chain の hook{awaiting, following, listener} | McpMod.ChoiceAwaitChain.cs:57, McpMod.DecisionContext.cs:196 |
| `manual:EventModel.CurrentOptions` | event.options | McpMod.StateBuilder.cs:564 |
| `manual:PotionModel.OnUseWrapper` | battle.combat_log[kind=potion_used] と hook BeforePotionUsed・AfterPotionUsed の行 | McpMod.CombatLogPatches.cs:389, McpMod.CombatLogPatches.cs:85, McpMod.CombatLogPatches.cs:67 |
| `manual:PowerModel.Flash` | battle.combat_log[kind=flash]{model} | McpMod.CombatLogPatches.cs:326 |
| `manual:RelicModel.Flash` | battle.combat_log[kind=flash]{model} | McpMod.CombatLogPatches.cs:325 |
| `manual:StratagemPower.selection_stage` | <screen>.decision_context.choice_source.await_chain の hook・shuffle・draw | McpMod.ChoiceAwaitChain.cs:58, McpMod.DecisionContext.cs:196 |
| `phase:CombatManager.DoTurnEnd` | battle.combat_log[kind=turn_phase,phase=do_turn_end] | McpMod.CombatLogPatches.cs:102 |
| `phase:CombatManager.EndCombatInternal` | battle.combat_log[kind=turn_phase,phase=end_combat_internal] | McpMod.CombatLogPatches.cs:108 |
| `phase:CombatManager.EndEnemyTurnInternal` | battle.combat_log[kind=turn_phase,phase=end_enemy_turn_internal] | McpMod.CombatLogPatches.cs:107 |
| `phase:CombatManager.EndPlayerTurnPhaseOneInternal` | battle.combat_log[kind=turn_phase,phase=end_player_turn_phase_one_internal] | McpMod.CombatLogPatches.cs:101 |
| `phase:CombatManager.EndPlayerTurnPhaseTwoInternal` | battle.combat_log[kind=turn_phase,phase=end_player_turn_phase_two_internal] | McpMod.CombatLogPatches.cs:105 |
| `phase:CombatManager.FlushPlayerHand` | battle.combat_log[kind=turn_phase,phase=flush_player_hand] | McpMod.CombatLogPatches.cs:104 |
| `phase:CombatManager.ResolveTurnEndCardEffects` | battle.combat_log[kind=turn_phase,phase=resolve_turn_end_card_effects] | McpMod.CombatLogPatches.cs:103 |
| `phase:CombatManager.RunAutoPrePlayPhase` | battle.combat_log[kind=turn_phase,phase=run_auto_pre_play_phase] | McpMod.CombatLogPatches.cs:99 |
| `phase:CombatManager.SetupPlayerTurn` | battle.combat_log[kind=turn_phase,phase=setup_player_turn] | McpMod.CombatLogPatches.cs:100, McpMod.ChoiceAwaitChain.cs:38 |
| `phase:CombatManager.StartCombatInternal` | battle.combat_log[kind=turn_phase,phase=start_combat_internal] | McpMod.CombatLogPatches.cs:97 |
| `phase:CombatManager.StartTurn` | battle.combat_log[kind=turn_phase,phase=start_turn] | McpMod.CombatLogPatches.cs:98 |
| `phase:CombatManager.SwitchFromPlayerToEnemySide` | battle.combat_log[kind=turn_phase,phase=switch_from_player_to_enemy_side] | McpMod.CombatLogPatches.cs:106 |
| `select:CardSelectCmd.FromChooseABundleScreen` | bundle_select | McpMod.StateBuilder.cs:454 |
| `select:CardSelectCmd.FromChooseACardScreen` | <screen>.choice_source（開いたモデル・hook・後続の listener・親プレイ） | McpMod.ChoiceSource.cs:32 |
| `select:CardSelectCmd.FromCombatPile` | <screen>.choice_source（開いたモデル・hook・後続の listener・親プレイ） | McpMod.ChoiceSource.cs:35 |
| `select:CardSelectCmd.FromDeckForEnchantment` | card_select.decision_context{selector, outer_selectors, opener{kind, model_type, model_id, method, call_stack}} | McpMod.DecisionContext.cs:35 |
| `select:CardSelectCmd.FromDeckForRemoval` | card_select.decision_context{selector, outer_selectors, opener{kind, model_type, model_id, method, call_stack}} | McpMod.DecisionContext.cs:38 |
| `select:CardSelectCmd.FromDeckForTransformation` | card_select.decision_context{selector, outer_selectors, opener{kind, model_type, model_id, method, call_stack}} | McpMod.DecisionContext.cs:34 |
| `select:CardSelectCmd.FromDeckForUpgrade` | card_select.decision_context{selector, outer_selectors, opener{kind, model_type, model_id, method, call_stack}} | McpMod.DecisionContext.cs:33 |
| `select:CardSelectCmd.FromDeckGeneric` | card_select.decision_context{selector, outer_selectors, opener{kind, model_type, model_id, method, call_stack}} | McpMod.DecisionContext.cs:39 |
| `select:CardSelectCmd.FromHand` | <screen>.choice_source（開いたモデル・hook・後続の listener・親プレイ） | McpMod.ChoiceSource.cs:37 |
| `select:CardSelectCmd.FromHandForDiscard` | <screen>.choice_source（開いたモデル・hook・後続の listener・親プレイ） | McpMod.ChoiceSource.cs:38 |
| `select:CardSelectCmd.FromHandForUpgrade` | <screen>.choice_source（開いたモデル・hook・後続の listener・親プレイ） | McpMod.ChoiceSource.cs:39 |
| `select:CardSelectCmd.FromSimpleGrid` | <screen>.choice_source（開いたモデル・hook・後続の listener・親プレイ） | McpMod.ChoiceSource.cs:34 |
| `select:CardSelectCmd.FromSimpleGridForRewards` | <screen>.choice_source（開いたモデル・hook・後続の listener・親プレイ） | McpMod.ChoiceSource.cs:33 |
| `select:CardSelectorPrefs.Cancelable` | <screen>.can_cancel | McpMod.StateBuilder.cs:3303 |
| `select:CardSelectorPrefs.MaxSelect` | <screen>.max_select | McpMod.StateBuilder.cs:3267 |
| `select:CardSelectorPrefs.MinSelect` | <screen>.min_select | McpMod.StateBuilder.cs:3267 |
| `select:CardSelectorPrefs.Prompt` | <screen>.prompt | McpMod.StateBuilder.cs:3245 |
| `select:CardSelectorPrefs.RequireManualConfirmation` | <screen>.can_confirm | McpMod.StateBuilder.cs:3323 |
| `select:RelicSelectCmd.FromChooseARelicScreen` | relic_select | McpMod.StateBuilder.cs:459 |

## hidden（プレイヤーに見えないもの）

| 理由 | 件数 | キー |
|---|---:|---|
| 乱数の状態。結果は出たときに公開される | 16 | `rng:PlayerRngSet.NonCombatGeneration`, `rng:PlayerRngSet.Rewards`, `rng:PlayerRngSet.Shops`, `rng:PlayerRngSet.Transformations`, `rng:RunRngSet.CombatCardGeneration`, `rng:RunRngSet.CombatCardSelection`, `rng:RunRngSet.CombatEnergyCosts`, `rng:RunRngSet.CombatOrbGeneration`, `rng:RunRngSet.CombatPotionGeneration`, `rng:RunRngSet.CombatTargets`, `rng:RunRngSet.MonsterAi`, `rng:RunRngSet.Niche`, `rng:RunRngSet.Shuffle`, `rng:RunRngSet.TreasureRoomRelics`, `rng:RunRngSet.UnknownMapPoint`, `rng:RunRngSet.UpFront` |
| 確率・抽選の重み（結果は出たときに公開） | 7 | `computed:Hook.ModifyCardRewardUpgradeOdds`, `computed:Hook.ModifyMerchantCardPool`, `computed:Hook.ModifyMerchantCardRarity`, `computed:Hook.ModifyNextEvent`, `computed:Hook.ModifyOddsIncreaseForUnrolledRoomType`, `computed:Hook.ModifyUnknownMapPointRoomTypes`, `computed:Hook.ShouldAllowAncient` |
| レリックの袋の順・レア度の抽選（出たときに公開） | 5 | `computed:RelicFactory.PullNextRelicForRewardFromFront`, `computed:RelicFactory.PullNextRelicFromBack`, `computed:RelicFactory.PullNextRelicFromFront`, `computed:RelicFactory.RollRarity`, `computed:RelicFactory.RollRarityForReward` |
| 乱数の結果（出たときに公開）。候補は GetPotionOptions の行 | 3 | `computed:PotionFactory.CreateRandomPotionInCombat`, `computed:PotionFactory.CreateRandomPotionOutOfCombat`, `computed:PotionFactory.CreateRandomPotionsOutOfCombat` |
| 山札の並び（シャッフルの結果）。PerfectFit の並べ替えも山札の順なので非公開 | 1 | `computed:Hook.ModifyShuffleOrder` |

## cosmetic（表示だけのもの）

| 理由 | 件数 | キー |
|---|---:|---|
| 音・演出・吹き出し | 19 | `cmd:SfxCmd.Play`, `cmd:SfxCmd.PlayCardSwooshSfx`, `cmd:SfxCmd.PlayDamage`, `cmd:SfxCmd.PlayDeath`, `cmd:SfxCmd.PlayLoop`, `cmd:SfxCmd.SetParam`, `cmd:SfxCmd.StopLoop`, `cmd:TalkCmd.Play`, `cmd:ThinkCmd.Play`, `cmd:VfxCmd.GetSideCenter`, `cmd:VfxCmd.GetSideCenterFloor`, `cmd:VfxCmd.PlayFullScreenInCombat`, `cmd:VfxCmd.PlayNonCombatVfx`, `cmd:VfxCmd.PlayOnCreature`, `cmd:VfxCmd.PlayOnCreatureCenter`, `cmd:VfxCmd.PlayOnCreatureCenters`, `cmd:VfxCmd.PlayOnCreatures`, `cmd:VfxCmd.PlayOnSide`, `cmd:VfxCmd.PlayVfx` |
| 選択画面の並び・見た目（候補と選べる数は変わらない） | 4 | `select:CardSelectorPrefs.Comparison`, `select:CardSelectorPrefs.PretendCardsCanBePlayed`, `select:CardSelectorPrefs.ShouldGlowGold`, `select:CardSelectorPrefs.UnpoweredPreviews` |
| 札を画面に見せる演出（効果は別のコマンド） | 3 | `cmd:CardCmd.AfflictAndPreview`, `cmd:CardCmd.Preview`, `cmd:CardCmd.PreviewCardPileAdd` |
| 演出の待ち | 3 | `cmd:Cmd.CustomScaledWait`, `cmd:Cmd.TryWait`, `cmd:Cmd.Wait` |
| 終了時の札の演出（待ち） | 2 | `phase:CombatManager.AddTurnEndCardToPlayPileWithDelay`, `phase:CombatManager.AwaitTurnEndCardTween` |
| アニメーション | 1 | `cmd:CreatureCmd.TriggerAnim` |
| 札の移動の演出 | 1 | `cmd:CardPileCmd.GetTweenForCardsChangingPiles` |

## internal（内部の都合）

| 理由 | 件数 | キー |
|---|---:|---|
| 履歴を記録するメソッド。分類は対応する *Entry の行で扱う | 19 | `history:CombatHistory.ArrowShot`, `history:CombatHistory.BlockGained`, `history:CombatHistory.CardAfflicted`, `history:CombatHistory.CardDiscarded`, `history:CombatHistory.CardDrawn`, `history:CombatHistory.CardExhausted`, `history:CombatHistory.CardGenerated`, `history:CombatHistory.CardPlayFinished`, `history:CombatHistory.CardPlayStarted`, `history:CombatHistory.Clear`, `history:CombatHistory.CreatureAttacked`, `history:CombatHistory.DamageReceived`, `history:CombatHistory.EnergySpent`, `history:CombatHistory.MonsterPerformedMove`, `history:CombatHistory.OrbChanneled`, `history:CombatHistory.PotionUsed`, `history:CombatHistory.PowerReceived`, `history:CombatHistory.StarsModified`, `history:CombatHistory.Summoned` |
| 判定。入力はモデルの項目（state inventory の範囲）、結果は状態か combat_log の行に出る。判定そのものは画面に出ない | 12 | `computed:Hook.ShouldAfflict`, `computed:Hook.ShouldClearBlock`, `computed:Hook.ShouldCreatureBeRemovedFromCombatAfterDeath`, `computed:Hook.ShouldDie`, `computed:Hook.ShouldDraw`, `computed:Hook.ShouldEtherealTrigger`, `computed:Hook.ShouldFlush`, `computed:Hook.ShouldPlayerResetEnergy`, `computed:Hook.ShouldPowerBeRemovedOnDeath`, `computed:Hook.ShouldProcurePotion`, `computed:Hook.ShouldStopCombatFromEnding`, `computed:Hook.ShouldTakeExtraTurn` |
| 手札が空かの確認。結果は hook:Hook.AfterHandEmptied の行 | 1 | `phase:CombatManager.CheckForEmptyHand` |
| 終戦中かの判定（ended_combat で終戦は出る） | 1 | `phase:CombatManager.IsCombatEnding` |
| 重ねる先を探す下請け | 1 | `cmd:PowerCmd.FindExistingInstanceForStacking` |

## out_of_scope（アイアンクラッドの範囲外）

| 理由 | 件数 | キー |
|---|---:|---|
| アイアンクラッドの範囲に上書きが無い（他キャラの札・パワーだけ）。共通の仕組み（combat_log の hook の行）を入れれば自動で出る | 23 | `computed:Hook.ModifyEnergyGain`, `computed:Hook.ModifyOrbPassiveTriggerCount`, `computed:Hook.ModifyOrbValue`, `computed:Hook.ModifySummonAmount`, `computed:Hook.ShouldGainStars`, `computed:Hook.ShouldPayExcessEnergyCostWithStars`, `hook:Hook.AfterArrowDislodged`, `hook:Hook.AfterArrowImpaled`, `hook:Hook.AfterArrowShot`, `hook:Hook.AfterCardDiscarded`, `hook:Hook.AfterDiedToDoom`, `hook:Hook.AfterEnergySpent`, `hook:Hook.AfterFlush`, `hook:Hook.AfterForge`, `hook:Hook.AfterModifyingEnergyGain`, `hook:Hook.AfterModifyingOrbPassiveTriggerCount`, `hook:Hook.AfterModifyingPowerAmountGiven`, `hook:Hook.AfterOrbChanneled`, `hook:Hook.AfterOrbEvoked`, `hook:Hook.AfterOstyRevived`, `hook:Hook.AfterStarsGained`, `hook:Hook.AfterStarsSpent`, `hook:Hook.AfterSummon` |
| 他キャラの仕組み（矢・鍛造・オスティ・旋律・オーブ） | 23 | `cmd:ArrowCmd.CreatureIsImpaledByArrow`, `cmd:ArrowCmd.CreatureIsImpaledByPlayersArrow`, `cmd:ArrowCmd.GainArrowSlot`, `cmd:ArrowCmd.GetAvailableArrowCount`, `cmd:ArrowCmd.ImpaleTarget`, `cmd:ArrowCmd.PlayerHasAnyArrows`, `cmd:ArrowCmd.ShootArrowAtTarget`, `cmd:ArrowCmd.TryShootArrow`, `cmd:ForgeCmd.Forge`, `cmd:ForgeCmd.PlayCombatRoomForgeVfx`, `cmd:MelodyCmd.IsFirstPlayThisTurn`, `cmd:MelodyCmd.MelodiesThisTurn`, `cmd:MelodyCmd.MelodyCardsPlayedThisTurn`, `cmd:MelodyCmd.PlayMelodyVfx`, `cmd:MelodyCmd.PlayedChoThisTurn`, `cmd:MelodyCmd.PlayedKiThisTurn`, `cmd:MelodyCmd.PlayedThisTurn`, `cmd:MelodyCmd.PlayedWahThisTurn`, `cmd:OrbCmd.AddSlots`, `cmd:OrbCmd.EvokeLast`, `cmd:OrbCmd.EvokeNext`, `cmd:OrbCmd.Passive`, `cmd:OrbCmd.RemoveSlots` |
| 他キャラの仕組み（オーブ・矢・星・召喚） | 4 | `history:ArrowShotEntry`, `history:OrbChanneledEntry`, `history:StarsModifiedEntry`, `history:SummonedEntry` |
| 星（他キャラの資源） | 3 | `cmd:PlayerCmd.GainStars`, `cmd:PlayerCmd.LoseStars`, `cmd:PlayerCmd.SetStars` |
| マルチプレイ（味方の死亡） | 2 | `phase:CombatManager.HandlePlayerDeath`, `phase:CombatManager.RemoveDeadPlayerCardsFromCombat` |
| 他キャラの札だけが呼ぶ | 2 | `cmd:CardPileCmd.AddDuringManualCardPlay`, `cmd:PlayerCmd.EndTurn` |
| オスティ（他キャラの仕組み） | 1 | `cmd:OstyCmd.Summon` |
| オーブ（他キャラの仕組み）。InfusedCore から呼ばれるが、オーブ自体は他キャラの仕組み | 1 | `cmd:OrbCmd.Channel` |
| マルチプレイ | 1 | `cmd:CardPileCmd.GiveToAnotherPlayer` |
| 呼ぶのは HandTrick（他キャラの札）だけ | 1 | `cmd:CardCmd.ApplySingleTurnSly` |
| 直接呼ぶのは他キャラの札。アイアンクラッドの範囲は CardCmd.DiscardAndDraw の行（Discard もそこへ委ねる） | 1 | `cmd:CardCmd.Discard` |
| 矢（FoolArrows の名前空間）。参照は ImpaledPower だけ | 1 | `cmd:ArrowCmd.DislodgeArrow` |

## 付録: 通知と計算の hook の機械的な情報

listener: combat = `IterateCombatHookListeners`（終戦中は空）、combat_all = `ICombatState.IterateHookListeners()`、run = `IRunState.IterateHookListeners`。ctx = PlayerChoiceContext を受け取る（中で選択画面が開きうる）。

| キー | 分類 | 分配先 | listener | ctx | IC 上書き | 他キャラ上書き | 呼び出し元 |
|---|---|---|---|---|---:|---:|---|
| `computed:Hook.ModifyAttackHitCount` | exposed | ModifyAttackHitCount | combat |  | 1 | 0 | AttackCommand, HitCountVar |
| `computed:Hook.ModifyBlock` | exposed | ModifyBlockAdditive, ModifyBlockMultiplicative | combat |  | 7 | 3 | BlockVar, CalculatedBlockVar, CreatureCmd, Glitterstream |
| `computed:Hook.ModifyCardBeingAddedToDeck` | exposed | TryModifyCardBeingAddedToDeck, TryModifyCardBeingAddedToDeckLate | run |  | 4 | 0 | CardCmd, CardPileCmd |
| `computed:Hook.ModifyCardPlayCount` | exposed | ModifyCardPlayCount | combat |  | 4 | 4 | CardModel |
| `computed:Hook.ModifyCardPlayResultLocation` | exposed | ModifyCardPlayResultLocation | combat |  | 3 | 1 | CardModel, CardPileCmd |
| `computed:Hook.ModifyCardRewardAlternatives` | exposed | TryModifyCardRewardAlternatives | run |  | 1 | 0 | CardRewardAlternative |
| `computed:Hook.ModifyCardRewardCreationOptions` | exposed | ModifyCardRewardCreationOptions, ModifyCardRewardCreationOptionsLate | run |  | 2 | 0 | CardFactory, ScrollBoxes |
| `computed:Hook.ModifyCardRewardUpgradeOdds` | hidden | ModifyCardRewardUpgradeOdds | run |  | 0 | 0 | CardFactory |
| `computed:Hook.ModifyDamage` | exposed | ModifyDamageAdditive, ModifyDamageMultiplicative, ModifyDamageMultiplicativeLate, ModifyDamageCap | run |  | 25 | 19 | AttackIntent, CalculatedDamageVar, CreatureCmd, DamageVar …(+4) |
| `computed:Hook.ModifyEnergyCostInCombat` | exposed | TryModifyEnergyCostInCombat, TryModifyEnergyCostInCombatLate | combat |  | 6 | 9 | CardEnergyCost |
| `computed:Hook.ModifyEnergyGain` | out_of_scope | ModifyEnergyGain | combat |  | 0 | 1 | PlayerCmd |
| `computed:Hook.ModifyExtraRestSiteHealText` | exposed | ModifyExtraRestSiteHealText | run |  | 4 | 0 | HealRestSiteOption |
| `computed:Hook.ModifyExtraRestSiteMendText` | exposed | ModifyExtraRestSiteMendText | run |  | 0 | 0 | MendRestSiteOption |
| `computed:Hook.ModifyGeneratedMap` | exposed | ModifyGeneratedMap | run |  | 2 | 0 | RunManager |
| `computed:Hook.ModifyGeneratedMapLate` | exposed | ModifyGeneratedMapLate | run |  | 2 | 0 | RunManager |
| `computed:Hook.ModifyGoldGained` | exposed | ModifyGoldGained | run |  | 2 | 0 | PlayerCmd |
| `computed:Hook.ModifyHandDraw` | exposed | ModifyHandDraw, ModifyHandDrawLate | combat |  | 13 | 7 | CombatManager |
| `computed:Hook.ModifyHpLost` | exposed | ModifyHpLostBeforeOsty, ModifyHpLostBeforeOstyLate, ModifyHpLostAfterOsty, ModifyHpLostAfterOstyLate | run |  | 7 | 0 | CreatureCmd |
| `computed:Hook.ModifyKeywordsInCombat` | exposed | TryModifyKeywordsInCombat | combat_all |  | 1 | 1 | CardModel |
| `computed:Hook.ModifyMaxEnergy` | exposed | ModifyMaxEnergy | combat |  | 14 | 2 | PlayerCombatState |
| `computed:Hook.ModifyMerchantCardCreationResults` | exposed | ModifyMerchantCardCreationResults | run |  | 4 | 0 | MerchantCardEntry |
| `computed:Hook.ModifyMerchantCardPool` | hidden | ModifyMerchantCardPool | run |  | 0 | 0 | CardFactory |
| `computed:Hook.ModifyMerchantCardRarity` | hidden | ModifyMerchantCardRarity | run |  | 0 | 0 | CardFactory |
| `computed:Hook.ModifyMerchantPrice` | exposed | ModifyMerchantPrice | run |  | 2 | 0 | MerchantEntry |
| `computed:Hook.ModifyNextEvent` | hidden | ModifyNextEvent | run |  | 1 | 0 | ActModel |
| `computed:Hook.ModifyOddsIncreaseForUnrolledRoomType` | hidden | ModifyOddsIncreaseForUnrolledRoomType | run |  | 0 | 0 | UnknownMapPointOdds |
| `computed:Hook.ModifyOrbPassiveTriggerCount` | out_of_scope | ModifyOrbPassiveTriggerCounts | combat |  | 0 | 1 | OrbModel |
| `computed:Hook.ModifyOrbValue` | out_of_scope | ModifyOrbValue | combat |  | 1 | 1 | OrbModel |
| `computed:Hook.ModifyPowerAmountGiven` | exposed | ModifyPowerAmountGivenAdditive, ModifyPowerAmountGivenMultiplicative | combat |  | 1 | 1 | PowerCmd, PowerVar |
| `computed:Hook.ModifyPowerAmountReceived` | exposed | TryModifyPowerAmountReceived | combat |  | 2 | 0 | PowerCmd |
| `computed:Hook.ModifyPreBlockDamageTarget` | exposed | ModifyPreBlockDamageTarget |  |  | 0 | 1 | CreatureCmd |
| `computed:Hook.ModifyRestSiteHealAmount` | exposed | ModifyRestSiteHealAmount | run |  | 1 | 0 | HealRestSiteOption, MendRestSiteOption |
| `computed:Hook.ModifyRestSiteHealRewards` | exposed | TryModifyRestSiteHealRewards | run |  | 2 | 0 | HealRestSiteOption |
| `computed:Hook.ModifyRestSiteOptions` | exposed | TryModifyRestSiteOptions | run |  | 8 | 0 | RestSiteOption |
| `computed:Hook.ModifyRewards` | exposed | TryModifyRewards, TryModifyRewardsLate | run |  | 7 | 0 | RewardsSet |
| `computed:Hook.ModifyShuffleOrder` | hidden | ModifyShuffleOrder | combat |  | 1 | 0 | CardPile, CardPileCmd |
| `computed:Hook.ModifyStarCost` | exposed | TryModifyStarCost | combat |  | 1 | 1 | CardModel |
| `computed:Hook.ModifySummonAmount` | out_of_scope | ModifySummonAmount | combat |  | 0 | 0 | OstyCmd, SummonVar |
| `computed:Hook.ModifyUnblockedDamageTarget` | exposed | ModifyUnblockedDamageTarget |  |  | 0 | 1 | CreatureCmd |
| `computed:Hook.ModifyUnknownMapPointRoomTypes` | hidden | ModifyUnknownMapPointRoomTypes | run |  | 4 | 0 | UnknownMapPointOdds |
| `computed:Hook.ModifyXValue` | exposed | ModifyXValue | combat |  | 1 | 0 | CardModel |
| `computed:Hook.ShouldAddToDeck` | exposed | ShouldAddToDeck | run |  | 0 | 0 | CardPileCmd |
| `computed:Hook.ShouldAfflict` | internal | ShouldAfflict | combat |  | 0 | 0 | CardCmd |
| `computed:Hook.ShouldAllowAncient` | hidden | ShouldAllowAncient | run |  | 0 | 0 | AncientEventModel |
| `computed:Hook.ShouldAllowFreeTravel` | exposed | ShouldAllowFreeTravel | run |  | 1 | 0 | MapTravel |
| `computed:Hook.ShouldAllowHitting` | exposed | ShouldAllowHitting | combat |  | 3 | 2 | Creature |
| `computed:Hook.ShouldAllowMerchantCardRemoval` | exposed | ShouldAllowMerchantCardRemoval | run |  | 0 | 0 | MerchantInventory |
| `computed:Hook.ShouldAllowSelectingMoreCardRewards` | exposed | ShouldAllowSelectingMoreCardRewards | run |  | 0 | 0 | CardReward |
| `computed:Hook.ShouldAllowTargeting` | exposed | ShouldAllowTargeting | combat |  | 0 | 0 | NTargetManager |
| `computed:Hook.ShouldClearBlock` | internal | ShouldClearBlock | combat |  | 4 | 0 | Creature |
| `computed:Hook.ShouldCreatureBeRemovedFromCombatAfterDeath` | internal | ShouldCreatureBeRemovedFromCombatAfterDeath | combat_all |  | 5 | 2 | CreatureCmd, MonsterModel, PlayerCombatState |
| `computed:Hook.ShouldDie` | internal | ShouldDie, ShouldDieLate | run |  | 2 | 0 | CreatureCmd, DoomPower |
| `computed:Hook.ShouldDisableRemainingRestSiteOptions` | exposed | ShouldDisableRemainingRestSiteOptions | run |  | 1 | 0 | RestSiteSynchronizer |
| `computed:Hook.ShouldDraw` | internal | ShouldDraw | combat |  | 2 | 0 | CardPileCmd |
| `computed:Hook.ShouldEtherealTrigger` | internal | ShouldEtherealTrigger | combat |  | 0 | 0 | CombatManager |
| `computed:Hook.ShouldFlush` | internal | ShouldFlush | combat |  | 2 | 1 | CombatManager |
| `computed:Hook.ShouldForcePotionReward` | exposed | ShouldForcePotionReward | run |  | 1 | 0 | PotionRewardOdds |
| `computed:Hook.ShouldGainStars` | out_of_scope | ShouldGainStars | combat |  | 0 | 0 | PlayerCmd |
| `computed:Hook.ShouldGenerateTreasure` | exposed | ShouldGenerateTreasure | run |  | 1 | 0 | OneOffSynchronizer, TreasureRoomRelicSynchronizer |
| `computed:Hook.ShouldPayExcessEnergyCostWithStars` | out_of_scope | ShouldPayExcessEnergyCostWithStars | combat |  | 0 | 0 | CardModel, PlayerCombatState |
| `computed:Hook.ShouldPlay` | exposed | ShouldPlay | combat |  | 7 | 1 | CardCmd, CardModel, ImitationLearningPower |
| `computed:Hook.ShouldPlayerResetEnergy` | internal | ShouldPlayerResetEnergy | combat |  | 1 | 0 | CombatManager |
| `computed:Hook.ShouldPowerBeRemovedOnDeath` | internal | ShouldPowerBeRemovedOnDeath |  |  | 1 | 0 | Creature |
| `computed:Hook.ShouldProceedToNextMapPoint` | exposed | ShouldProceedToNextMapPoint | run |  | 0 | 0 | NMapScreen, NRewardsScreen |
| `computed:Hook.ShouldProcurePotion` | internal | ShouldProcurePotion | run |  | 1 | 0 | PotionCmd |
| `computed:Hook.ShouldRefillMerchantEntry` | exposed | ShouldRefillMerchantEntry | run |  | 1 | 0 | MerchantEntry |
| `computed:Hook.ShouldStopCombatFromEnding` | internal | ShouldStopCombatFromEnding | combat_all |  | 5 | 0 | CombatManager |
| `computed:Hook.ShouldTakeExtraTurn` | internal | ShouldTakeExtraTurnEarly, ShouldTakeExtraTurn | combat |  | 2 | 0 | CombatManager |
| `computed:Hook.TryModifyCardRewardOptions` | exposed | TryModifyCardRewardOptions, TryModifyCardRewardOptionsLate | run |  | 10 | 0 | CardFactory, CardReward |
| `computed:Hook.TryReplaceDefaultRoomRewards` | exposed | TryReplaceDefaultRoomRewards | run |  | 0 | 0 | RewardsSet |
| `hook:Hook.AfterActEntered` | exposed | AfterActEntered | run |  | 0 | 0 | RunManager |
| `hook:Hook.AfterArrowDislodged` | out_of_scope | AfterArrowDislodged | combat | ✓ | 0 | 3 | ArrowCmd |
| `hook:Hook.AfterArrowImpaled` | out_of_scope | AfterArrowImpaled | combat | ✓ | 0 | 1 | ArrowCmd |
| `hook:Hook.AfterArrowShot` | out_of_scope | AfterArrowShot | combat | ✓ | 0 | 3 | ArrowCmd |
| `hook:Hook.AfterAttack` | exposed | AfterAttack | combat | ✓ | 6 | 3 | AttackCommand, AttackContext |
| `hook:Hook.AfterAutoPostPlayPhaseEntered` | exposed | AfterAutoPostPlayPhaseEntered | combat | ✓ | 2 | 2 | CombatManager, PlayerTurnPhase |
| `hook:Hook.AfterAutoPrePlayPhaseEntered` | exposed | AfterAutoPrePlayPhaseEnteredEarly, AfterAutoPrePlayPhaseEntered, AfterAutoPrePlayPhaseEnteredLate | combat | ✓ | 4 | 1 | CombatManager, PlayerCombatState, PlayerTurnPhase |
| `hook:Hook.AfterBlockBroken` | exposed | AfterBlockBroken | combat_all | ✓ | 2 | 0 | CreatureCmd |
| `hook:Hook.AfterBlockCleared` | exposed | AfterBlockCleared | combat |  | 6 | 0 | CombatManager |
| `hook:Hook.AfterBlockGained` | exposed | AfterBlockGained | combat |  | 2 | 0 | CreatureCmd |
| `hook:Hook.AfterCardChangedPiles` | exposed | AfterCardChangedPiles, AfterCardChangedPilesLate | run |  | 6 | 1 | CardCmd, CardPileCmd |
| `hook:Hook.AfterCardDiscarded` | out_of_scope | AfterCardDiscarded | combat | ✓ | 0 | 2 | CardCmd |
| `hook:Hook.AfterCardDrawn` | exposed | AfterCardDrawnEarly, AfterCardDrawn | combat | ✓ | 6 | 7 | CardPileCmd |
| `hook:Hook.AfterCardEnteredCombat` | exposed | AfterCardEnteredCombat | combat |  | 10 | 4 | CardCmd, CardPileCmd |
| `hook:Hook.AfterCardExhausted` | exposed | AfterCardExhausted | combat | ✓ | 8 | 0 | CardCmd |
| `hook:Hook.AfterCardGeneratedForCombat` | exposed | AfterCardGeneratedForCombat | combat |  | 1 | 9 | CardCmd, CardPileCmd |
| `hook:Hook.AfterCardPlayed` | exposed | AfterCardPlayed, AfterCardPlayedLate | combat_all | ✓ | 45 | 31 | BranchingPlayerChoiceContext, CardModel |
| `hook:Hook.AfterCombatEnd` | exposed | AfterCombatEnd | run |  | 39 | 6 | CombatManager |
| `hook:Hook.AfterCombatVictory` | exposed | AfterCombatVictoryEarly, AfterCombatVictory | run |  | 6 | 0 | CombatManager |
| `hook:Hook.AfterCreatureAddedToCombat` | exposed | AfterCreatureAddedToCombat | combat_all |  | 3 | 0 | CreatureCmd |
| `hook:Hook.AfterCurrentHpChanged` | exposed | AfterCurrentHpChanged | run |  | 5 | 1 | CreatureCmd |
| `hook:Hook.AfterDamageGiven` | exposed | AfterDamageGiven | combat_all | ✓ | 3 | 6 | CreatureCmd |
| `hook:Hook.AfterDamageReceived` | exposed | AfterDamageReceived, AfterDamageReceivedLate | run | ✓ | 20 | 2 | CreatureCmd |
| `hook:Hook.AfterDeath` | exposed | AfterDeath | run |  | 30 | 5 | CreatureCmd, Player |
| `hook:Hook.AfterDiedToDoom` | out_of_scope | AfterDiedToDoom | combat_all |  | 0 | 1 | DoomPower |
| `hook:Hook.AfterEnergyReset` | exposed | AfterEnergyReset, AfterEnergyResetLate | combat |  | 5 | 5 | BoundPhylactery, CombatManager |
| `hook:Hook.AfterEnergySpent` | out_of_scope | AfterEnergySpent | combat |  | 0 | 1 | CardModel |
| `hook:Hook.AfterFlush` | out_of_scope | AfterFlush | combat | ✓ | 0 | 1 | CombatManager |
| `hook:Hook.AfterForge` | out_of_scope | AfterForge | combat |  | 0 | 1 | ForgeCmd |
| `hook:Hook.AfterGoldGained` | exposed | AfterGoldGained | run |  | 1 | 0 | PlayerCmd |
| `hook:Hook.AfterHandEmptied` | exposed | AfterHandEmptied | combat | ✓ | 1 | 0 | CombatManager |
| `hook:Hook.AfterItemPurchased` | exposed | AfterItemPurchased | run |  | 1 | 0 | MerchantEntry |
| `hook:Hook.AfterMapGenerated` | exposed | AfterMapGenerated | run |  | 1 | 0 | RunManager |
| `hook:Hook.AfterModifyingBlockAmount` | exposed | AfterModifyingBlockAmount | combat |  | 3 | 0 | CreatureCmd |
| `hook:Hook.AfterModifyingCardPlayCount` | exposed | AfterModifyingCardPlayCount | combat |  | 4 | 4 | CardModel |
| `hook:Hook.AfterModifyingCardRewardOptions` | exposed | AfterModifyingCardRewardOptions | run |  | 2 | 0 | CardFactory, CardReward |
| `hook:Hook.AfterModifyingDamageAmount` | exposed | AfterModifyingDamageAmount | run |  | 3 | 0 | CreatureCmd |
| `hook:Hook.AfterModifyingEnergyGain` | out_of_scope | AfterModifyingEnergyGain | combat |  | 0 | 1 | PlayerCmd |
| `hook:Hook.AfterModifyingGoldGained` | exposed | AfterModifyingGoldGained | run |  | 2 | 0 | PlayerCmd |
| `hook:Hook.AfterModifyingHandDraw` | exposed | AfterModifyingHandDraw | combat |  | 4 | 0 | CombatManager |
| `hook:Hook.AfterModifyingHpLostAfterOsty` | exposed | AfterModifyingHpLostAfterOsty | run |  | 5 | 0 | CreatureCmd |
| `hook:Hook.AfterModifyingHpLostBeforeOsty` | exposed | AfterModifyingHpLostBeforeOsty | run |  | 1 | 0 | CreatureCmd |
| `hook:Hook.AfterModifyingOrbPassiveTriggerCount` | out_of_scope | AfterModifyingOrbPassiveTriggerCount | combat |  | 0 | 1 | OrbModel |
| `hook:Hook.AfterModifyingPowerAmountGiven` | out_of_scope | AfterModifyingPowerAmountGiven | combat |  | 0 | 1 | PowerCmd |
| `hook:Hook.AfterModifyingPowerAmountReceived` | exposed | AfterModifyingPowerAmountReceived | combat |  | 2 | 0 | PowerCmd |
| `hook:Hook.AfterModifyingRewards` | exposed | AfterModifyingRewards | run |  | 2 | 0 | RewardsSet |
| `hook:Hook.AfterOrbChanneled` | out_of_scope | AfterOrbChanneled | combat | ✓ | 0 | 1 | OrbCmd |
| `hook:Hook.AfterOrbEvoked` | out_of_scope | AfterOrbEvoked | combat | ✓ | 0 | 1 | OrbCmd |
| `hook:Hook.AfterOstyRevived` | out_of_scope | AfterOstyRevived | combat |  | 1 | 0 | OstyCmd |
| `hook:Hook.AfterPlayerTurnStart` | exposed | AfterPlayerTurnStartEarly, AfterPlayerTurnStart, AfterPlayerTurnStartLate | combat | ✓ | 18 | 9 | CombatManager |
| `hook:Hook.AfterPotionDiscarded` | exposed | AfterPotionDiscarded | run |  | 1 | 0 | PotionCmd |
| `hook:Hook.AfterPotionProcured` | exposed | AfterPotionProcured | run |  | 1 | 0 | PotionCmd |
| `hook:Hook.AfterPotionUsed` | exposed | AfterPotionUsed | run |  | 2 | 0 | PotionModel |
| `hook:Hook.AfterPowerAmountChanged` | exposed | AfterPowerAmountChanged | combat | ✓ | 7 | 5 | PowerCmd |
| `hook:Hook.AfterPreventingBlockClear` | exposed | AfterPreventingBlockClear | combat |  | 2 | 0 | Creature |
| `hook:Hook.AfterPreventingDeath` | exposed | AfterPreventingDeath | run |  | 2 | 0 | CreatureCmd |
| `hook:Hook.AfterPreventingDraw` | exposed | AfterPreventingDraw | combat |  | 1 | 0 | CardPileCmd |
| `hook:Hook.AfterRestSiteHeal` | exposed | AfterRestSiteHeal | run |  | 2 | 0 | HealRestSiteOption, MendRestSiteOption |
| `hook:Hook.AfterRestSiteSmith` | exposed | AfterRestSiteSmith | run |  | 0 | 0 | SmithRestSiteOption |
| `hook:Hook.AfterRewardTaken` | exposed | AfterRewardTaken | run |  | 0 | 0 | Reward |
| `hook:Hook.AfterRoomEntered` | exposed | AfterRoomEntered | run |  | 33 | 4 | CombatRoom, EventRoom, MerchantRoom, RestSiteRoom …(+1) |
| `hook:Hook.AfterShuffle` | exposed | AfterShuffle | combat | ✓ | 3 | 1 | CardPileCmd |
| `hook:Hook.AfterSideTurnEnd` | exposed | AfterSideTurnEnd, AfterSideTurnEndLate | combat |  | 46 | 31 | CombatManager, PlayerTurnPhase |
| `hook:Hook.AfterSideTurnStart` | exposed | AfterSideTurnStart, AfterSideTurnStartLate | combat |  | 32 | 22 | CombatManager, PlayerTurnPhase |
| `hook:Hook.AfterStarsGained` | out_of_scope | AfterStarsGained | combat |  | 0 | 1 | PlayerCmd |
| `hook:Hook.AfterStarsSpent` | out_of_scope | AfterStarsSpent | combat |  | 0 | 3 | CardModel |
| `hook:Hook.AfterSummon` | out_of_scope | AfterSummon | combat | ✓ | 0 | 0 | OstyCmd |
| `hook:Hook.AfterTakingExtraTurn` | exposed | AfterTakingExtraTurn | combat |  | 2 | 0 | CombatManager |
| `hook:Hook.BeforeAttack` | exposed | BeforeAttack | combat |  | 4 | 1 | AttackCommand, AttackContext |
| `hook:Hook.BeforeBlockGained` | exposed | BeforeBlockGained | combat |  | 0 | 0 | CreatureCmd |
| `hook:Hook.BeforeCardAutoPlayed` | exposed | BeforeCardAutoPlayed | combat |  | 0 | 0 | CardCmd |
| `hook:Hook.BeforeCardPlayed` | exposed | BeforeCardPlayed | combat |  | 14 | 17 | CardModel |
| `hook:Hook.BeforeCardRemoved` | exposed | BeforeCardRemoved | run |  | 1 | 0 | CardPileCmd |
| `hook:Hook.BeforeCombatRewardOffered` | exposed | BeforeCombatRewardOffered | run |  | 1 | 0 | CombatRoom |
| `hook:Hook.BeforeCombatStart` | exposed | BeforeCombatStart, BeforeCombatStartLate | run |  | 24 | 1 | CombatManager |
| `hook:Hook.BeforeDamageReceived` | exposed | BeforeDamageReceived | run | ✓ | 1 | 0 | CreatureCmd |
| `hook:Hook.BeforeDeath` | exposed | BeforeDeath | run |  | 4 | 0 | CreatureCmd |
| `hook:Hook.BeforeFlush` | exposed | BeforeFlush, BeforeFlushLate | combat |  | 1 | 0 | CombatManager |
| `hook:Hook.BeforeHandDraw` | exposed | BeforeHandDraw, BeforeHandDrawLate | combat | ✓ | 9 | 15 | CombatManager |
| `hook:Hook.BeforePotionUsed` | exposed | BeforePotionUsed | run |  | 1 | 0 | PotionModel |
| `hook:Hook.BeforePowerAmountChanged` | exposed | BeforePowerAmountChanged | combat |  | 1 | 1 | PowerCmd |
| `hook:Hook.BeforeRoomEntered` | exposed | BeforeRoomEntered | run |  | 1 | 0 | RunManager |
| `hook:Hook.BeforeSideTurnEnd` | exposed | BeforeSideTurnEndVeryEarly, BeforeSideTurnEndEarly, BeforeSideTurnEnd | combat |  | 17 | 2 | CombatManager, PlayerTurnPhase |
| `hook:Hook.BeforeSideTurnStart` | exposed | BeforeSideTurnStart | combat |  | 21 | 10 | BoundPhylactery, CombatManager |

## 付録: 手番の段階（CombatManager）の呼び出し順

| メソッド | 分類 | 呼び出し（出現順・重複除く） |
|---|---|---|
| `CombatManager.IsCombatEnding` (444) | internal | Hook.ShouldStopCombatFromEnding |
| `CombatManager.StartCombatInternal` (606) | exposed | Hook.BeforeCombatStart |
| `CombatManager.StartTurn` (709) | exposed | Hook.BeforeSideTurnStart, Hook.AfterBlockCleared, Hook.AfterSideTurnStart |
| `CombatManager.RunAutoPrePlayPhase` (884) | exposed | Hook.AfterAutoPrePlayPhaseEntered |
| `CombatManager.SetupPlayerTurn` (902) | exposed | Hook.ShouldPlayerResetEnergy, Hook.AfterEnergyReset, Hook.BeforeHandDraw, Hook.ModifyHandDraw, Hook.AfterModifyingHandDraw, CardPileCmd.Draw, Hook.AfterPlayerTurnStart |
| `CombatManager.CheckForEmptyHand` (1231) | internal | Hook.AfterHandEmptied |
| `CombatManager.HandlePlayerDeath` (1301) | out_of_scope | PlayerCmd.SetEnergy, PlayerCmd.SetStars |
| `CombatManager.RemoveDeadPlayerCardsFromCombat` (1325) | out_of_scope | CardPileCmd.RemoveFromCombat |
| `CombatManager.EndCombatInternal` (1380) | exposed | Hook.AfterCombatEnd, Hook.AfterCombatVictory |
| `CombatManager.EndPlayerTurnPhaseOneInternal` (1614) | exposed | Hook.AfterAutoPostPlayPhaseEntered, Hook.BeforeSideTurnEnd, Hook.BeforeFlush |
| `CombatManager.DoTurnEnd` (1698) | exposed | Hook.ShouldEtherealTrigger, CardCmd.Exhaust |
| `CombatManager.AddTurnEndCardToPlayPileWithDelay` (1764) | cosmetic | CardPileCmd.Add |
| `CombatManager.ResolveTurnEndCardEffects` (1770) | exposed | CardPileCmd.Add, CardCmd.Exhaust |
| `CombatManager.AwaitTurnEndCardTween` (1787) | cosmetic | CardPileCmd.GetTweenForCardsChangingPiles |
| `CombatManager.EndEnemyTurnInternal` (1806) | exposed | Hook.BeforeSideTurnEnd, Hook.AfterSideTurnEnd |
| `CombatManager.EndPlayerTurnPhaseTwoInternal` (1856) | exposed | Hook.AfterSideTurnEnd |
| `CombatManager.FlushPlayerHand` (1895) | exposed | Hook.ShouldFlush, CardPileCmd.Add, Hook.AfterFlush |
| `CombatManager.SwitchFromPlayerToEnemySide` (1935) | exposed | Hook.ShouldTakeExtraTurn, Hook.AfterTakingExtraTurn |
