# 出来事の順番と選択の文脈の棚卸し（event inventory）

> 生成物。`tools/event_inventory.py` が decompile と `docs/event-inventory-classification.json` から作る。手で書き換えない。
> ゲーム v0.113.1（decompile `sts2-a671e9ef`）。MOD の参照は作業ツリーの *.cs から機械的に集める。

## これは何か

`docs/state-inventory.md` はモデルの **項目** を数える。ここは項目ではない情報、つまり戦闘中に **何がどの順で起きたか**
（通知・履歴・山の移動・手番の段階）と、**選択画面がどの文脈で開いたか**（開いたもの・段階・残り回数・候補）を数える。
MOD に 1 件ずつ足す PR（#110〜#114、進行中の #1115）を止め、phase 2 で一度に出すための台帳。

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

提案キーの `battle.combat_log` は phase 2 で新設する戦闘ごとの通し列、`decision_context` は各選択画面に付ける文脈
（いまの `choice_source` を内包する）。`card_resolution_events` は互換のために今の形のまま残す想定。
「進行中」は sts2#1115（MOD のブランチ `entropy-stratagem-stages`）が扱う予定のもの。棚卸しの時点（2026-11-20）で
このブランチは main（828239c）から commit が無く、扱う範囲は sts2#1115 の本文から取った。
「Issue」は open の sts2 の Issue のうち、その情報が揃うと進められるもの（本文と engine の「未観測」の箇所から対応を取った）。

## 件数（カテゴリ × 分類）

| カテゴリ | exposed | public-missing | hidden | cosmetic | internal | out_of_scope | unknown | unclassified | 計 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 通知（Hook の Before/After） | 14 | 53 | 0 | 0 | 0 | 17 | 0 | 0 | 84 |
| 決定時の計算（Modify/Should・生成） | 43 | 9 | 16 | 0 | 12 | 6 | 0 | 0 | 86 |
| 選択の入口と画面の設定 | 14 | 5 | 0 | 4 | 0 | 0 | 0 | 0 | 23 |
| 戦闘の履歴 | 4 | 10 | 0 | 0 | 19 | 4 | 0 | 0 | 37 |
| 山の移動・生成・コマンド | 36 | 32 | 0 | 27 | 1 | 34 | 1 | 0 | 131 |
| 手番の段階（CombatManager） | 0 | 12 | 0 | 2 | 2 | 2 | 0 | 0 | 18 |
| 乱数の系列 | 0 | 0 | 16 | 0 | 0 | 0 | 0 | 0 | 16 |
| その他の決定点（手で登録） | 4 | 7 | 0 | 0 | 0 | 0 | 0 | 0 | 11 |
| **計** | **115** | **128** | **32** | **33** | **34** | **63** | **1** | **0** | **406** |

public-missing は 128 件、うちアイアンクラッドの範囲は 128 件（進行中のブランチ `entropy-stratagem-stages` が扱うもの 10 件を含む）。

スコープ（自動）の内訳: core 137, ironclad 221, other-only 48

## public-missing（プレイヤーに見えるが MOD が出していないもの）

「出す先」は phase 2 の MOD PR での置き場所の提案。「Issue」はそれが揃うと進められる sts2 の open Issue。
「IC の参照」はアイアンクラッドの範囲で上書き・呼び出しをするモデルの数（例を最大 4 つ）。

| キー | 見えるもの | 提案キー | 出す先 | Issue | IC の参照 | 選択が開くモデル | 出典 |
|---|---|---|---|---|---|---|---|
| `hook:Hook.AfterCardChangedPiles` | 札が山を移った後の効果。札の移動そのものも見える。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） | sts2#327 sts2#771 sts2#792 sts2#242 sts2#996 sts2#997 | 6 BingBong, BookOfFiveRings, DarkstonePeriapt, LuckyFysh …(+2) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:206 |
| `cmd:CardPileCmd.Add` | ドローでない札の移動（選択で回収・山札の上へ戻す・NostalgiaPower の往復・Bolas の返却）。どの札がどこへ行くかが見える（Random の位置だけ非公開） | `battle.combat_log[kind=pile_move]{uid, from, to, position(top/bottom/random), cloned_by, source}` | combat_log（新設） | sts2#327 sts2#771 sts2#792 sts2#242 sts2#996 sts2#997 | 37 AggressionPower, Anointed, ArcaneScroll, BingBong …(+33) |  | MegaCrit.Sts2.Core.Commands/CardPileCmd.cs:323 |
| `hook:Hook.AfterPlayerTurnStart` | 手番開始後の効果の順（Early・通常・Late）。ChoicesParadox・EntropyPower・GamblingChip などの上書きで選択が開く。光る効果の順と、その間に開く選択の段階 **進行中（#1115（段階・残り枚数の部分））** | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） | sts2#771 sts2#1115 sts2#1088 | 18 BathTowel, Bellows, BloodVial, BoneTea …(+14) | ChoicesParadox, EntropyPower, GamblingChip, ToastyMittens | MegaCrit.Sts2.Core.Hooks/Hook.cs:933 |
| `hook:Hook.BeforeHandDraw` | 手番開始の通常ドローの直前の効果（通常と Late）。Toolbox の上書きで選択が開く。光る効果の順と、その間に開く選択の段階 **進行中（#1115（段階・残り枚数の部分））** | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） | sts2#771 sts2#1115 sts2#1088 | 9 BlessedAntler, Bolas, HelloWorldPower, JeweledMask …(+5) | Toolbox | MegaCrit.Sts2.Core.Hooks/Hook.cs:639 |
| `phase:CombatManager.SetupPlayerTurn` | プレイヤーの手番の準備（エナジー・BeforeHandDraw・通常ドロー・AfterPlayerTurnStart）。今は round・is_play_phase だけ **進行中（#1115（手番開始の段階の一部））** | `battle.combat_log[kind=turn_phase]{phase, side, round, player}` | combat_log（新設） | sts2#771 sts2#1115 sts2#1088 | 0 |  | MegaCrit.Sts2.Core.Combat/CombatManager.cs:902 |
| `manual:CardSelectCmd.skip_and_result` | 選択の結果（選んだ札・skip した提示・何回目の解決か）。画面が閉じた後には残らない | `battle.combat_log[kind=selection]{opener, source, offered_uids\|ids, chosen, skipped, stage}` | combat_log（新設） | sts2#997 sts2#994 sts2#1115 | 0 |  | MegaCrit.Sts2.Core.Commands/CardSelectCmd.cs:68 |
| `hook:Hook.AfterAutoPostPlayPhaseEntered` | 手番終了の自動プレイ段階の効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） | sts2#783 sts2#1088 | 2 HowlFromBeyond, StampedePower |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:961 |
| `hook:Hook.AfterModifyingHandDraw` | 手番開始の手札の枚数を修正した効果が働いた通知。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） | sts2#771 sts2#1088 | 4 DrawCardsNextTurnPower, MindRotPower, Pocketwatch, PollinousCore |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:790 |
| `hook:Hook.AfterShuffle` | シャッフル後の効果。StratagemPower の上書きで選択が開く。光る効果の順と、その間に開く選択の段階 **進行中（#1115（段階・残り枚数の部分））** | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） | sts2#327 sts2#1115 | 3 BiiigHug, StratagemPower, TheAbacus | StratagemPower | MegaCrit.Sts2.Core.Hooks/Hook.cs:1181 |
| `computed:Hook.ModifyCardPlayResultLocation` | 撃った札の行き先（CorruptionPower・NostalgiaPower・ReboundPower）。札が飛ぶのが見える | `battle.combat_log[kind=pile_move]{uid, from, to, position, reason=play_result, modifier}` | combat_log（新設） | sts2#771 sts2#327 | 3 CorruptionPower, NostalgiaPower, ReboundPower |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:1445 |
| `computed:Hook.ModifyHandDraw` | 手番開始の通常ドローの枚数と、それを変えた効果（AfterModifyingHandDraw の通知が出る） | `battle.combat_log[kind=turn_phase,phase=hand_draw]{amount, modifiers[]}` | combat_log（新設） | sts2#771 sts2#1088 | 13 BagOfPreparation, BigMushroom, BoomingConch, ClarityPower …(+9) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:1747 |
| `cmd:CardPileCmd.Shuffle` | 明示のシャッフル。枚数と起きた時点が見える（並びは非公開） **進行中（#1115（明示のシャッフルと予約の区別））** | `battle.combat_log[kind=shuffle]{reason=explicit, count, source}` | combat_log（新設） | sts2#327 sts2#1115 | 1 BottledPotential |  | MegaCrit.Sts2.Core.Commands/CardPileCmd.cs:1076 |
| `cmd:CardPileCmd.ShuffleIfNecessary` | 山札が尽きたときのシャッフル（ドローの途中） **進行中（#1115（ドローの途中のシャッフル））** | `battle.combat_log[kind=shuffle]{reason=draw_pile_empty, count}` | combat_log（新設） | sts2#327 sts2#1115 | 0 |  | MegaCrit.Sts2.Core.Commands/CardPileCmd.cs:1198 |
| `manual:CardPileCmd.DrawInternal` | 1 回のドロー命令の残り枚数（drawsRequested - i）。from_hand_draw は exposed **進行中（#1115）** | `card_resolution_events[kind=draw]{request_id, remaining}` | card_resolution_events の draw の行に足す | sts2#1115 sts2#1088 | 0 |  | MegaCrit.Sts2.Core.Commands/CardPileCmd.cs:1010 |
| `hook:Hook.AfterAutoPrePlayPhaseEntered` | 手番開始の自動プレイ段階の効果（Early・通常・Late）。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） | sts2#771 | 4 HistoryCourse, Imbued, MayhemPower, WhisperingEarring |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:979 |
| `hook:Hook.AfterCardExhausted` | 廃棄に反応する効果の順。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） | sts2#1088 | 8 BurningSticks, CharonsAshes, DarkEmbracePower, DrumOfBattle …(+4) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:276 |
| `hook:Hook.AfterPotionDiscarded` | ポーションを捨てた後の効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） | sts2#991 | 1 BeltBuckle |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:1011 |
| `hook:Hook.AfterPotionProcured` | ポーション取得後の効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） | sts2#991 | 1 BeltBuckle |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:1023 |
| `hook:Hook.AfterPotionUsed` | ポーション使用後の効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） | sts2#991 | 2 BeltBuckle, ReptileTrinket |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:1047 |
| `hook:Hook.AfterSideTurnEnd` | 陣営の手番終了後の効果の順。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） | sts2#1088 | 46 ArtOfWar, AsleepPower, BattlewornDummyTimeLimitPower, ColossusPower …(+42) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:1321 |
| `hook:Hook.AfterSideTurnStart` | 陣営の手番開始後の効果の順。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） | sts2#771 | 32 Akabeko, BlurPower, BoomingConch, Bread …(+28) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:1214 |
| `hook:Hook.BeforeFlush` | 手番終了の手札処理の直前の効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） | sts2#1088 | 1 SlumberingEssence |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:583 |
| `hook:Hook.BeforePotionUsed` | ポーション使用直前の効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） | sts2#991 | 1 SurroundedPower |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:1035 |
| `hook:Hook.BeforeSideTurnEnd` | 陣営の手番終了直前の効果の順。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） | sts2#1088 | 17 AsleepPower, ChainsOfBindingPower, CloakClasp, FakeOrichalcum …(+13) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:1286 |
| `hook:Hook.BeforeSideTurnStart` | 陣営の手番開始直前の効果の順。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） | sts2#771 | 21 AggressionPower, BagOfMarbles, BeatingRemnant, BrilliantScarf …(+17) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:1195 |
| `computed:Hook.ModifyXValue` | 撃つ前の X の値（ChemicalX の修正込み）と、X 多段の札の回数 | `hand[].preview.x_value と X 多段の hits` | 手札の札の preview（McpMod.CardPreview.cs）。撃った後の captured_x_value は exposed | sts2#848 | 1 ChemicalX |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:2177 |
| `computed:PotionFactory.GetPotionOptions` | ランダム生成のポーションの候補（公開のプール）。Alchemize・EntropicBrew の評価に要る | `player.potion_generation_pool` | player（generation_pool の隣。McpMod.GenerationPool.cs と同じ形） | sts2#991 | 0 |  | MegaCrit.Sts2.Core.Factories/PotionFactory.cs:92 |
| `history:CardDiscardedEntry` | カードの効果で捨てた札（個体と順。CardCmd.DiscardAndDraw が記録。手番終了の手札処理は CardPileCmd.Add なので記録されない） | `battle.combat_log[kind=discard]{uid, reason}` | combat_log（新設） | sts2#790 | 0 |  | MegaCrit.Sts2.Core.Combat.History.Entries/CardDiscardedEntry.cs:1 |
| `history:CardExhaustedEntry` | 廃棄された札（個体と順。CardCmd.Exhaust が記録）。今は手番内の件数だけ | `battle.combat_log[kind=exhaust]{uid, caused_by_ethereal, source}` | combat_log（新設） | sts2#1088 | 0 |  | MegaCrit.Sts2.Core.Combat.History.Entries/CardExhaustedEntry.cs:1 |
| `history:PotionUsedEntry` | ポーションの使用（種類・対象） | `battle.combat_log[kind=potion,action=used]` | combat_log（新設） | sts2#991 | 0 |  | MegaCrit.Sts2.Core.Combat.History.Entries/PotionUsedEntry.cs:1 |
| `cmd:CardCmd.DiscardAndDraw` | 捨てて引く（GamblingChip・GamblersBrew） | `battle.combat_log[kind=discard] と card_resolution_events[kind=draw] の対応` | combat_log（新設） | sts2#790 | 2 GamblersBrew, GamblingChip |  | MegaCrit.Sts2.Core.Commands/CardCmd.cs:172 |
| `cmd:CardCmd.Exhaust` | 廃棄（札・原因。手番終了のエセリアルを含む）。今は手番内の件数だけ | `battle.combat_log[kind=exhaust]{uid, caused_by_ethereal, source}` | combat_log（新設） | sts2#1088 | 12 Brand, BurningPact, Cinder, FiendFire …(+8) |  | MegaCrit.Sts2.Core.Commands/CardCmd.cs:238 |
| `cmd:CardCmd.TransformToRandom` | 変化（元の札 → 新しい札）。新しい札は見えるが元の uid との対応が出ていない **進行中（#1115（エントロピーの段階））** | `battle.combat_log[kind=card_change,change=transform]{from_uid, to_uid, to_id}` | combat_log（新設） | sts2#1115 | 2 EntropyPower, NewLeaf |  | MegaCrit.Sts2.Core.Commands/CardCmd.cs:330 |
| `cmd:PotionCmd.Discard` | ポーションを捨てる | `battle.combat_log[kind=potion,action=discarded]` | combat_log（新設） | sts2#991 | 0 |  | MegaCrit.Sts2.Core.Commands/PotionCmd.cs:55 |
| `cmd:PotionCmd.TryToProcure` | ポーションの取得（戦闘中の生成を含む） | `battle.combat_log[kind=potion,action=procured]` | combat_log（新設） | sts2#991 | 7 AlchemicalCoffer, Alchemize, DelicateFrond, EntropicBrew …(+3) |  | MegaCrit.Sts2.Core.Commands/PotionCmd.cs:16 |
| `phase:CombatManager.DoTurnEnd` | 手番終了の札の処理（エセリアルの判定と廃棄、終了時の効果を持つ札）。今は round・is_play_phase だけ | `battle.combat_log[kind=turn_phase]{phase, side, round, player}` | combat_log（新設） | sts2#1088 | 0 |  | MegaCrit.Sts2.Core.Combat/CombatManager.cs:1698 |
| `phase:CombatManager.EndPlayerTurnPhaseOneInternal` | 手番終了の第 1 段（AfterAutoPostPlayPhaseEntered・BeforeSideTurnEnd・BeforeFlush）。今は round・is_play_phase だけ | `battle.combat_log[kind=turn_phase]{phase, side, round, player}` | combat_log（新設） | sts2#1088 | 0 |  | MegaCrit.Sts2.Core.Combat/CombatManager.cs:1614 |
| `phase:CombatManager.EndPlayerTurnPhaseTwoInternal` | 手番終了の第 2 段（AfterSideTurnEnd）。今は round・is_play_phase だけ | `battle.combat_log[kind=turn_phase]{phase, side, round, player}` | combat_log（新設） | sts2#1088 | 0 |  | MegaCrit.Sts2.Core.Combat/CombatManager.cs:1856 |
| `phase:CombatManager.FlushPlayerHand` | 手札の処理（保留と捨て札。CardPileCmd.Add で移すので CardDiscardedEntry は残らない）。どの札が残りどの札が流れたかが見える。今は round・is_play_phase だけ | `battle.combat_log[kind=turn_phase]{phase, side, round, player}` | combat_log（新設） | sts2#1088 | 0 |  | MegaCrit.Sts2.Core.Combat/CombatManager.cs:1895 |
| `phase:CombatManager.ResolveTurnEndCardEffects` | 終了時の効果を持つ札 1 枚ずつの解決（CardPileCmd.Add・CardCmd.Exhaust）。今は round・is_play_phase だけ | `battle.combat_log[kind=turn_phase]{phase, side, round, player}` | combat_log（新設） | sts2#1088 | 0 |  | MegaCrit.Sts2.Core.Combat/CombatManager.cs:1770 |
| `phase:CombatManager.RunAutoPrePlayPhase` | 手番開始の自動プレイ段階。今は round・is_play_phase だけ | `battle.combat_log[kind=turn_phase]{phase, side, round, player}` | combat_log（新設） | sts2#771 | 0 |  | MegaCrit.Sts2.Core.Combat/CombatManager.cs:884 |
| `phase:CombatManager.StartTurn` | 陣営の手番の開始（ブロックの消失・BeforeSideTurnStart/AfterSideTurnStart）。今は round・is_play_phase だけ | `battle.combat_log[kind=turn_phase]{phase, side, round, player}` | combat_log（新設） | sts2#771 | 0 |  | MegaCrit.Sts2.Core.Combat/CombatManager.cs:709 |
| `manual:EntropyPower.selection_stage` | エントロピーの選択の段階（何枚目・後続の段階） **進行中（#1115）** | `<screen>.decision_context{stage, stages_total, parent}` | decision_context（新設） | sts2#1115 | 0 |  | MegaCrit.Sts2.Core.Models.Powers/EntropyPower.cs:28 |
| `manual:PotionModel.OnUseWrapper` | ポーションの使用の順（BeforePotionUsed → 効果 → AfterPotionUsed → 空の手札の確認） | `battle.combat_log[kind=potion,action=used] と hook の行` | combat_log（新設） | sts2#991 | 0 |  | MegaCrit.Sts2.Core.Models/PotionModel.cs:301 |
| `manual:StratagemPower.selection_stage` | 計略の選択の段階（シャッフル後の選択・続きの解決） **進行中（#1115）** | `<screen>.decision_context{stage, stages_total, parent}` | decision_context（新設） | sts2#1115 | 0 |  | MegaCrit.Sts2.Core.Models.Powers/StratagemPower.cs:24 |
| `hook:Hook.AfterAttack` | 攻撃の後に働く効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 6 AimPower, GigantificationPower, PainfulStabsPower, SkittishPower …(+2) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:131 |
| `hook:Hook.AfterBlockBroken` | ブロックが割れたときの効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 2 BurrowedPower, HandDrill |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:146 |
| `hook:Hook.AfterBlockCleared` | 手番開始でブロックが消えた後の効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 6 BlockNextTurnPower, CaptainsWheel, HornCleat, SelfFormingClayPower …(+2) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:158 |
| `hook:Hook.AfterBlockGained` | ブロック獲得後の効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 2 BeaconOfHopePower, JuggernautPower |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:182 |
| `hook:Hook.AfterCardDrawn` | 引いた札に反応する効果の順（Early と通常の 2 巡）。ドロー自体は exposed。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 6 AutomationPower, ChainsOfBindingPower, ConfusedPower, HellraiserPower …(+2) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:241 |
| `hook:Hook.AfterCardEnteredCombat` | 札が戦闘に入ったときの効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 10 GalvanicPower, GhostSeed, HexPower, Hexed …(+6) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:262 |
| `hook:Hook.AfterCardGeneratedForCombat` | 生成札に反応する効果。生成自体は exposed。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 1 Aeonglass |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:290 |
| `hook:Hook.AfterCardPlayed` | プレイ後に働く効果の順（通常と Late の 2 巡）。play の行自体は exposed。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 45 ArtOfWar, BrilliantScarf, Byrdpip[relic], CalamityPower …(+41) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:317 |
| `hook:Hook.AfterCombatEnd` | 戦闘終了時の効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 39 ArtOfWar, BeltBuckle, BrilliantScarf, BurningSticks …(+35) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:367 |
| `hook:Hook.AfterCombatVictory` | 勝利時の効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 6 BeltBuckle, BlackBlood, BurningBlood, MeatOnTheBone …(+2) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:391 |
| `hook:Hook.AfterCreatureAddedToCombat` | 敵・召喚が戦闘に加わったときの効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 3 FurCoat, PhilosophersStone, SandpitPower |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:413 |
| `hook:Hook.AfterCurrentHpChanged` | HP の変化に反応する効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 5 Crusher, MeatOnTheBone, RedSkull, Rocket …(+1) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:425 |
| `hook:Hook.AfterDamageGiven` | 与ダメージ後の効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 3 ImbalancedPower, MightyWings, PaperCutsPower |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:440 |
| `hook:Hook.AfterDamageReceived` | 被ダメージ後の効果の順（通常と Late）。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 20 AsleepPower, BeatingRemnant, CentennialPuzzle, CurlUpPower …(+16) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:468 |
| `hook:Hook.AfterDeath` | 死亡後の効果の順。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 30 AdaptablePower, Aeonglass, ConstrictPower, CoveredPower …(+26) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:501 |
| `hook:Hook.AfterEnergyReset` | エナジーの再設定後の効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 5 ArtOfWar, EnergyNextTurnPower, FakeVenerableTeaSet, RadiancePower …(+1) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:554 |
| `hook:Hook.AfterHandEmptied` | 手札が空になったときの効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 1 UnceasingTop |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:662 |
| `hook:Hook.AfterModifyingBlockAmount` | ブロック量を修正した効果が働いた通知。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 3 FastenPower, PaelsLegion[relic], Vambrace |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:700 |
| `hook:Hook.AfterModifyingCardPlayCount` | プレイ回数を増やした効果が働いた通知。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 4 DuplicationPower, OneTwoPunchPower, TagTeamPower, ThrowingAxe |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:715 |
| `hook:Hook.AfterModifyingDamageAmount` | ダメージを修正した効果が働いた通知。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 3 HardToKillPower, IntangiblePower, SlowPower |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:745 |
| `hook:Hook.AfterModifyingHpLostAfterOsty` | HP 損失を修正した効果が働いた通知（後段）。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 5 BeatingRemnant, BufferPower, IntangiblePower, TheBoot …(+1) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:820 |
| `hook:Hook.AfterModifyingHpLostBeforeOsty` | HP 損失を修正した効果が働いた通知（前段）。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 1 HardenedShellPower |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:805 |
| `hook:Hook.AfterModifyingPowerAmountReceived` | 付与量を修正した効果が働いた通知。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 2 ArtifactPower, RuinedHelmet |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:865 |
| `hook:Hook.AfterPowerAmountChanged` | パワーの量が変わった後の効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 7 PossessSpeedPower, PossessStrengthPower, SandpitPower, TemporaryDexterityPower …(+3) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:1071 |
| `hook:Hook.AfterPreventingBlockClear` | ブロックの消失を止めた効果が働いた通知。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 2 BlurPower, SturdyClamp |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:1083 |
| `hook:Hook.AfterPreventingDeath` | 死亡を防いだ効果が働いた通知。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 2 FairyInABottle, LizardTail |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:1095 |
| `hook:Hook.AfterPreventingDraw` | ドローを止めた効果が働いた通知。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 1 Fiddle |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:1107 |
| `hook:Hook.AfterTakingExtraTurn` | 追加の手番の後の効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 2 ExtraTurnPower, PaelsEye |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:1271 |
| `hook:Hook.BeforeAttack` | 攻撃の直前に働く効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 4 AimPower, GigantificationPower, HellraiserPower, VigorPower |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:119 |
| `hook:Hook.BeforeBlockGained` | ブロック獲得の直前の通知（アイアンクラッドの範囲に上書きは無い。CreatureCmd.GainBlock の中）。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 0 |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:170 |
| `hook:Hook.BeforeCardAutoPlayed` | 自動プレイの直前の通知（上書きは無い。CardCmd.AutoPlay の中）。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 0 |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:194 |
| `hook:Hook.BeforeCardPlayed` | プレイ直前に働く効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 14 CalamityPower, ChainsOfBindingPower, ChemicalX, FreeAttackPower …(+10) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:302 |
| `hook:Hook.BeforeCombatStart` | 戦闘開始時の効果の順。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 24 Anchor, BathRobes, BeltBuckle, Byrdine …(+20) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:350 |
| `hook:Hook.BeforeDamageReceived` | 被ダメージ直前の効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 1 ThornsPower |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:454 |
| `hook:Hook.BeforeDeath` | 死亡直前の効果（盗んだ札の返却など）。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 4 Crusher, HeistPower, Rocket, SwipePower |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:489 |
| `hook:Hook.BeforePowerAmountChanged` | パワーの量が変わる直前の効果。光る効果の順と、その間に開く選択の段階 | `battle.combat_log[kind=hook]{hook, phase(early/normal/late), listeners[{kind,id,owner,uid\|hidden_reason}]}` | combat_log（新設） |  | 1 UnsettlingLamp |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:1059 |
| `computed:Hook.ModifyHpLost` | HP の損失量と、それを変えた効果 | `battle.combat_log[kind=damage]{blocked, hp_lost, modifiers[]}` | combat_log（新設） |  | 7 BeatingRemnant, BufferPower, HardenedShellPower, IntangiblePower …(+3) |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:1780 |
| `computed:Hook.ModifyPowerAmountGiven` | 与える側の付与量の修正（UnsettlingLamp） | `battle.combat_log[kind=power]{amount_requested, amount_applied, modifiers[]}` | combat_log（新設） |  | 1 UnsettlingLamp |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:1951 |
| `computed:Hook.ModifyPowerAmountReceived` | 付与量の修正（ArtifactPower・RuinedHelmet） | `battle.combat_log[kind=power]{amount_requested, amount_applied, modifiers[]}` | combat_log（新設） |  | 2 ArtifactPower, RuinedHelmet |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:1980 |
| `computed:Hook.ModifyPreBlockDamageTarget` | 攻撃の受け手の差し替え。誰が受けたかは見える | `battle.combat_log[kind=damage]{receiver, original_target}` | combat_log（新設） |  | 0 |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:2154 |
| `computed:Hook.ModifyUnblockedDamageTarget` | ブロック後のダメージの受け手の差し替え | `battle.combat_log[kind=damage]{receiver, original_target}` | combat_log（新設） |  | 0 |  | MegaCrit.Sts2.Core.Hooks/Hook.cs:2138 |
| `select:CardSelectCmd.FromDeckForEnchantment` | 戦闘外のデッキ選択。候補・prompt・枚数は exposed（card_select）。開いたもの（レリック・イベント・休憩所）は見えるが出ていない | `card_select.decision_context{opener, source{kind,id}}` | decision_context（新設） |  | 7 ElectricShrymp, GnarledHammer, Kifuda, PaelsGrowth …(+3) |  | MegaCrit.Sts2.Core.Commands/CardSelectCmd.cs:475 |
| `select:CardSelectCmd.FromDeckForRemoval` | 戦闘外のデッキ選択。候補・prompt・枚数は exposed（card_select）。開いたもの（レリック・イベント・休憩所）は見えるが出ていない | `card_select.decision_context{opener, source{kind,id}}` | decision_context（新設） |  | 6 BiiigHug, EmptyCage, PaelsTooth, PrecariousShears …(+2) |  | MegaCrit.Sts2.Core.Commands/CardSelectCmd.cs:573 |
| `select:CardSelectCmd.FromDeckForTransformation` | 戦闘外のデッキ選択。候補・prompt・枚数は exposed（card_select）。開いたもの（レリック・イベント・休憩所）は見えるが出ていない | `card_select.decision_context{opener, source{kind,id}}` | decision_context（新設） |  | 3 Astrolabe, Claws, NewLeaf |  | MegaCrit.Sts2.Core.Commands/CardSelectCmd.cs:419 |
| `select:CardSelectCmd.FromDeckForUpgrade` | 戦闘外のデッキ選択。候補・prompt・枚数は exposed（card_select）。開いたもの（レリック・イベント・休憩所）は見えるが出ていない | `card_select.decision_context{opener, source{kind,id}}` | decision_context（新設） |  | 2 Pomander, YummyCookie |  | MegaCrit.Sts2.Core.Commands/CardSelectCmd.cs:365 |
| `select:CardSelectCmd.FromDeckGeneric` | 戦闘外のデッキ選択。候補・prompt・枚数は exposed（card_select）。開いたもの（レリック・イベント・休憩所）は見えるが出ていない | `card_select.decision_context{opener, source{kind,id}}` | decision_context（新設） |  | 1 DollysMirror |  | MegaCrit.Sts2.Core.Commands/CardSelectCmd.cs:588 |
| `history:BlockGainedEntry` | ブロック獲得（受け手・量・札）。今は件数だけ | `battle.combat_log[kind=block]` | combat_log（新設） |  | 0 |  | MegaCrit.Sts2.Core.Combat.History.Entries/BlockGainedEntry.cs:1 |
| `history:CardAfflictedEntry` | 苦悩が付いた札（個体と種類）。今は拘束の件数だけ | `battle.combat_log[kind=card_change,change=afflict]{uid, affliction}` | combat_log（新設） |  | 0 |  | MegaCrit.Sts2.Core.Combat.History.Entries/CardAfflictedEntry.cs:1 |
| `history:CreatureAttackedEntry` | 攻撃（攻撃者・各ヒットの結果） | `battle.combat_log[kind=attack]` | combat_log（新設） |  | 0 |  | MegaCrit.Sts2.Core.Combat.History.Entries/CreatureAttackedEntry.cs:1 |
| `history:DamageReceivedEntry` | 被ダメージ（受け手・与え手・札・ブロック・HP 損失・撃破）。今は件数だけ | `battle.combat_log[kind=damage]` | combat_log（新設） |  | 0 |  | MegaCrit.Sts2.Core.Combat.History.Entries/DamageReceivedEntry.cs:1 |
| `history:EnergySpentEntry` | エナジーの消費（量・札） | `battle.combat_log[kind=energy,change=spent]` | combat_log（新設） |  | 0 |  | MegaCrit.Sts2.Core.Combat.History.Entries/EnergySpentEntry.cs:1 |
| `history:MonsterPerformedMoveEntry` | 敵が行った行動（move id・対象）。今は move_state の直前の行動だけ | `battle.combat_log[kind=monster_move]` | combat_log（新設） |  | 0 |  | MegaCrit.Sts2.Core.Combat.History.Entries/MonsterPerformedMoveEntry.cs:1 |
| `history:PowerReceivedEntry` | パワーの付与（対象・量・付与元） | `battle.combat_log[kind=power]` | combat_log（新設） |  | 0 |  | MegaCrit.Sts2.Core.Combat.History.Entries/PowerReceivedEntry.cs:1 |
| `cmd:CardCmd.Transform` | 変化（元の札 → 新しい札）。新しい札は見えるが元の uid との対応が出ていない | `battle.combat_log[kind=card_change,change=transform]{from_uid, to_uid, to_id}` | combat_log（新設） |  | 7 ArchaicTooth, Astrolabe, Byrdine, Claws …(+3) |  | MegaCrit.Sts2.Core.Commands/CardCmd.cs:355 |
| `cmd:CardCmd.TransformTo` | 変化（元の札 → 新しい札）。新しい札は見えるが元の uid との対応が出ていない | `battle.combat_log[kind=card_change,change=transform]{from_uid, to_uid, to_id}` | combat_log（新設） |  | 2 Byrdpip[relic], Dowsing |  | MegaCrit.Sts2.Core.Commands/CardCmd.cs:342 |
| `cmd:CardPileCmd.RemoveFromCombat` | 札が戦闘から取り除かれる（ThievingHopper） | `battle.combat_log[kind=pile_move]{to=removed}` | combat_log（新設） |  | 1 ThievingHopper |  | MegaCrit.Sts2.Core.Commands/CardPileCmd.cs:97 |
| `cmd:CreatureCmd.Add` | 敵・召喚が加わる（時点と順）。今の一覧は enemies に出る | `battle.combat_log[kind=creature,action=added]` | combat_log（新設） |  | 9 Fabricator, Fogmog, InfestedPower, LivingFog …(+5) |  | MegaCrit.Sts2.Core.Commands/CreatureCmd.cs:39 |
| `cmd:CreatureCmd.Damage` | ダメージ（受け手・量・ブロック・HP 損失） | `battle.combat_log[kind=damage]` | combat_log（新設） |  | 50 BadLuck, Beckon, BloodWall, Bloodletting …(+46) |  | MegaCrit.Sts2.Core.Commands/CreatureCmd.cs:100 |
| `cmd:CreatureCmd.Escape` | 逃走 | `battle.combat_log[kind=creature,action=escaped]` | combat_log（新設） |  | 3 BattlewornDummyTimeLimitPower, FatGremlin, ThievingHopper |  | MegaCrit.Sts2.Core.Commands/CreatureCmd.cs:690 |
| `cmd:CreatureCmd.GainBlock` | ブロック獲得 | `battle.combat_log[kind=block]` | combat_log（新設） |  | 82 Adroit, Aeonglass, Anchor, Armaments …(+78) |  | MegaCrit.Sts2.Core.Commands/CreatureCmd.cs:727 |
| `cmd:CreatureCmd.GainMaxHp` | HP・最大 HP の変化（量と原因）。結果の値は state に出る | `battle.combat_log[kind=hp]{target, change, amount, source}` | combat_log（新設） |  | 14 BigMushroom, ChosenCheese, DarkstonePeriapt, DragonFruit …(+10) |  | MegaCrit.Sts2.Core.Commands/CreatureCmd.cs:918 |
| `cmd:CreatureCmd.Heal` | HP・最大 HP の変化（量と原因）。結果の値は state に出る | `battle.combat_log[kind=hp]{target, change, amount, source}` | combat_log（新設） |  | 25 Ambergris, BathRobes, BathTowel, BlackBlood …(+21) |  | MegaCrit.Sts2.Core.Commands/CreatureCmd.cs:815 |
| `cmd:CreatureCmd.Kill` | 撃破・即死 | `battle.combat_log[kind=creature,action=killed]` | combat_log（新設） |  | 4 GasBomb, SandpitPower, TheGambitPower, WaterfallGiant |  | MegaCrit.Sts2.Core.Commands/CreatureCmd.cs:496 |
| `cmd:CreatureCmd.LoseBlock` | ブロックの減少 | `battle.combat_log[kind=block,change=lost]` | combat_log（新設） |  | 2 BurrowedPower, SturdyClamp |  | MegaCrit.Sts2.Core.Commands/CreatureCmd.cs:790 |
| `cmd:CreatureCmd.LoseMaxHp` | HP・最大 HP の変化（量と原因）。結果の値は state に出る | `battle.combat_log[kind=hp]{target, change, amount, source}` | combat_log（新設） |  | 4 BrightestFlame, LeafyPoultice, PaperCutsPower, SereTalon |  | MegaCrit.Sts2.Core.Commands/CreatureCmd.cs:942 |
| `cmd:CreatureCmd.SetCurrentHp` | HP・最大 HP の変化（量と原因）。結果の値は state に出る | `battle.combat_log[kind=hp]{target, change, amount, source}` | combat_log（新設） |  | 2 FurCoat, Wuppy[relic] |  | MegaCrit.Sts2.Core.Commands/CreatureCmd.cs:894 |
| `cmd:CreatureCmd.SetMaxAndCurrentHp` | HP・最大 HP の変化（量と原因）。結果の値は state に出る | `battle.combat_log[kind=hp]{target, change, amount, source}` | combat_log（新設） |  | 3 DecimillipedeSegment, ToughEgg, WaterfallGiant |  | MegaCrit.Sts2.Core.Commands/CreatureCmd.cs:987 |
| `cmd:CreatureCmd.SetMaxHp` | HP・最大 HP の変化（量と原因）。結果の値は state に出る | `battle.combat_log[kind=hp]{target, change, amount, source}` | combat_log（新設） |  | 2 TestSubject, Wuppy[relic] |  | MegaCrit.Sts2.Core.Commands/CreatureCmd.cs:970 |
| `cmd:DamageCmd.Attack` | 攻撃（攻撃者・対象・各ヒット） | `battle.combat_log[kind=attack]` | combat_log（新設） |  | 174 Aeonglass, Anger, AshenStrike, AssassinRubyRaider …(+170) |  | MegaCrit.Sts2.Core.Commands/DamageCmd.cs:14 |
| `cmd:PlayerCmd.GainEnergy` | エナジーの増減（量と原因）。結果は player.energy に出る | `battle.combat_log[kind=energy]` | combat_log（新設） |  | 31 ArtOfWar, AutomationPower, BelieveInYou, Bloodletting …(+27) |  | MegaCrit.Sts2.Core.Commands/PlayerCmd.cs:29 |
| `cmd:PlayerCmd.LoseEnergy` | エナジーの増減（量と原因）。結果は player.energy に出る | `battle.combat_log[kind=energy]` | combat_log（新設） |  | 2 Bread, Void |  | MegaCrit.Sts2.Core.Commands/PlayerCmd.cs:50 |
| `cmd:PlayerCmd.SetEnergy` | エナジーの増減（量と原因）。結果は player.energy に出る | `battle.combat_log[kind=energy]` | combat_log（新設） |  | 0 |  | MegaCrit.Sts2.Core.Commands/PlayerCmd.cs:69 |
| `cmd:PowerCmd.Apply` | パワーの付与・増減・消滅（対象・量・付与元）。結果は status に出る | `battle.combat_log[kind=power]` | combat_log（新設） |  | 254 Aeonglass, Aggression, Akabeko, Ambergris …(+250) |  | MegaCrit.Sts2.Core.Commands/PowerCmd.cs:36 |
| `cmd:PowerCmd.Decrement` | パワーの付与・増減・消滅（対象・量・付与元）。結果は status に出る | `battle.combat_log[kind=power]` | combat_log（新設） |  | 28 ArtifactPower, AsleepPower, BattlewornDummyTimeLimitPower, BlurPower …(+24) |  | MegaCrit.Sts2.Core.Commands/PowerCmd.cs:184 |
| `cmd:PowerCmd.ModifyAmount` | パワーの付与・増減・消滅（対象・量・付与元）。結果は status に出る | `battle.combat_log[kind=power]` | combat_log（新設） |  | 4 AimPower, FranticEscape, PlatingPower, VigorPower |  | MegaCrit.Sts2.Core.Commands/PowerCmd.cs:219 |
| `cmd:PowerCmd.Remove` | パワーの付与・増減・消滅（対象・量・付与元）。結果は status に出る | `battle.combat_log[kind=power]` | combat_log（新設） |  | 42 AsleepPower, BlockNextTurnPower, BurrowedPower, ConstrictPower …(+38) |  | MegaCrit.Sts2.Core.Commands/PowerCmd.cs:282 |
| `cmd:PowerCmd.TickDownDuration` | パワーの付与・増減・消滅（対象・量・付与元）。結果は status に出る | `battle.combat_log[kind=power]` | combat_log（新設） |  | 3 FrailPower, VulnerablePower, WeakPower |  | MegaCrit.Sts2.Core.Commands/PowerCmd.cs:194 |
| `phase:CombatManager.EndCombatInternal` | 戦闘の終了。今は round・is_play_phase だけ | `battle.combat_log[kind=turn_phase]{phase, side, round, player}` | combat_log（新設） |  | 0 |  | MegaCrit.Sts2.Core.Combat/CombatManager.cs:1380 |
| `phase:CombatManager.EndEnemyTurnInternal` | 敵の手番の終了。今は round・is_play_phase だけ | `battle.combat_log[kind=turn_phase]{phase, side, round, player}` | combat_log（新設） |  | 0 |  | MegaCrit.Sts2.Core.Combat/CombatManager.cs:1806 |
| `phase:CombatManager.StartCombatInternal` | 戦闘開始（BeforeCombatStart）。今は round・is_play_phase だけ | `battle.combat_log[kind=turn_phase]{phase, side, round, player}` | combat_log（新設） |  | 0 |  | MegaCrit.Sts2.Core.Combat/CombatManager.cs:606 |
| `phase:CombatManager.SwitchFromPlayerToEnemySide` | 敵の手番へ（追加の手番の判定・AfterTakingExtraTurn）。今は round・is_play_phase だけ | `battle.combat_log[kind=turn_phase]{phase, side, round, player}` | combat_log（新設） |  | 0 |  | MegaCrit.Sts2.Core.Combat/CombatManager.cs:1935 |
| `manual:PowerModel.Flash` | パワーが光る（効果が働いた合図） | `battle.combat_log[kind=hook].listeners[].flashed（または kind=flash の行）` | combat_log（新設） |  | 0 |  | MegaCrit.Sts2.Core.Models/PowerModel.cs:449 |
| `manual:RelicModel.Flash` | レリックが光る（効果が働いた合図）。hook の行の listener のうち実際に働いたものの印 | `battle.combat_log[kind=hook].listeners[].flashed（または kind=flash の行）` | combat_log（新設） |  | 0 |  | MegaCrit.Sts2.Core.Models/RelicModel.cs:489 |

### Issue ごとの public-missing

| Issue | 件数 | キー |
|---|---:|---|
| sts2#1088 複合ドローの区間の strict 照合 | 18 | `hook:Hook.AfterCardExhausted`, `hook:Hook.BeforeFlush`, `hook:Hook.BeforeHandDraw`, `hook:Hook.AfterModifyingHandDraw`, `hook:Hook.AfterPlayerTurnStart`, `hook:Hook.AfterAutoPostPlayPhaseEntered`, `hook:Hook.BeforeSideTurnEnd`, `hook:Hook.AfterSideTurnEnd`, `computed:Hook.ModifyHandDraw`, `cmd:CardCmd.Exhaust`, `history:CardExhaustedEntry`, `phase:CombatManager.SetupPlayerTurn`, `phase:CombatManager.EndPlayerTurnPhaseOneInternal`, `phase:CombatManager.DoTurnEnd`, `phase:CombatManager.ResolveTurnEndCardEffects`, `phase:CombatManager.EndPlayerTurnPhaseTwoInternal`, `phase:CombatManager.FlushPlayerHand`, `manual:CardPileCmd.DrawInternal` |
| sts2#771 開始時の自動プレイの観測・郷愁 | 13 | `hook:Hook.AfterCardChangedPiles`, `hook:Hook.BeforeHandDraw`, `hook:Hook.AfterModifyingHandDraw`, `hook:Hook.AfterPlayerTurnStart`, `hook:Hook.AfterAutoPrePlayPhaseEntered`, `hook:Hook.BeforeSideTurnStart`, `hook:Hook.AfterSideTurnStart`, `computed:Hook.ModifyCardPlayResultLocation`, `computed:Hook.ModifyHandDraw`, `cmd:CardPileCmd.Add`, `phase:CombatManager.StartTurn`, `phase:CombatManager.RunAutoPrePlayPhase`, `phase:CombatManager.SetupPlayerTurn` |
| sts2#1115 エントロピー・計略・スプラッシュ（進行中） | 11 | `hook:Hook.BeforeHandDraw`, `hook:Hook.AfterPlayerTurnStart`, `hook:Hook.AfterShuffle`, `cmd:CardCmd.TransformToRandom`, `cmd:CardPileCmd.Shuffle`, `cmd:CardPileCmd.ShuffleIfNecessary`, `phase:CombatManager.SetupPlayerTurn`, `manual:CardPileCmd.DrawInternal`, `manual:EntropyPower.selection_stage`, `manual:StratagemPower.selection_stage`, `manual:CardSelectCmd.skip_and_result` |
| sts2#991 エントロピー醸造 | 9 | `hook:Hook.AfterPotionDiscarded`, `hook:Hook.AfterPotionProcured`, `hook:Hook.BeforePotionUsed`, `hook:Hook.AfterPotionUsed`, `cmd:PotionCmd.TryToProcure`, `cmd:PotionCmd.Discard`, `history:PotionUsedEntry`, `computed:PotionFactory.GetPotionOptions`, `manual:PotionModel.OnUseWrapper` |
| sts2#327 山札・捨て札の見立てずれ（再現不能 25 件） | 6 | `hook:Hook.AfterCardChangedPiles`, `hook:Hook.AfterShuffle`, `computed:Hook.ModifyCardPlayResultLocation`, `cmd:CardPileCmd.Add`, `cmd:CardPileCmd.Shuffle`, `cmd:CardPileCmd.ShuffleIfNecessary` |
| sts2#997 エンシェントカード 11 枚の fixture | 3 | `hook:Hook.AfterCardChangedPiles`, `cmd:CardPileCmd.Add`, `manual:CardSelectCmd.skip_and_result` |
| sts2#242 engine 改良の第 2 弾（G01 捨て札からの移動） | 2 | `hook:Hook.AfterCardChangedPiles`, `cmd:CardPileCmd.Add` |
| sts2#790 ギャンブラーの醸造酒 | 2 | `cmd:CardCmd.DiscardAndDraw`, `history:CardDiscardedEntry` |
| sts2#792 未来視の雫 | 2 | `hook:Hook.AfterCardChangedPiles`, `cmd:CardPileCmd.Add` |
| sts2#996 ネオーの怒りの回収の選択 | 2 | `hook:Hook.AfterCardChangedPiles`, `cmd:CardPileCmd.Add` |
| sts2#783 彼方からの遠吠え（ExhaustOnNextPlay） | 1 | `hook:Hook.AfterAutoPostPlayPhaseEntered` |
| sts2#848 X コストの多段の回数 | 1 | `computed:Hook.ModifyXValue` |
| sts2#994 エンシェントカードの評価（追跡） | 1 | `manual:CardSelectCmd.skip_and_result` |

## phase 2 の形（提案）

public-missing を 1 本の MOD PR で出すなら、行ごとに Harmony の patch を足すのでなく、次の 3 つの共通の仕組みにまとめる。
上の表の提案キーはすべてこのどれかに入る。

1. **`battle.combat_log`（戦闘ごとの通し列）**。行は確定したら変えない（今の `card_resolution_events` と同じ「前の snapshot が後の
   snapshot の先頭になる」性質）。行の出どころは 4 つ:
   - `CombatHistory.Add`（private。全 *Entry がここを通る）の postfix 1 つで、履歴の全種（damage・block・attack・power・
     potion・monster_move・exhaust・discard・energy）を種別ごとの写像で出す
   - Hook の通知（表の hook:* の行）は、Hook の static メソッドの表（今の `ChoiceSourceHooks` と同じ形）に prefix/postfix を付けて
     `kind=hook` の括りを作り、各 listener の終わりは `AbstractModel.InvokeExecutionFinished`（Hook が listener ごとに呼ぶ）で拾う。
     上書きしていない listener は落とす（今の `HookOverrideDeclarer` を使う）。山札の札の listener は `hidden_reason: draw_pile`。
     `RelicModel.Flash` / `PowerModel.Flash` は直前の listener の行に `flashed` を付ける
   - 履歴に残らない命令（`CardPileCmd.Add` の pile_move・`Shuffle` / `ShuffleIfNecessary` の shuffle・`CardCmd.Transform*` の
     card_change・`PotionCmd.TryToProcure` / `Discard`・`CreatureCmd.Add` / `Kill` / `Escape` / HP 系・`PlayerCmd` のエナジー）は
     命令ごとの prefix/postfix で行を作る。`DrawInternal` の残り枚数は draw の行に足す
   - 手番の段階（phase:* の行）は `CombatManager` の private メソッドの prefix で `kind=turn_phase` の境界を入れる
2. **`decision_context`（全選択画面）**。今の `choice_source` を内包し、`CardSelectCmd` の全入口（戦闘外の `FromDeckFor*` と
   `RelicSelectCmd` を含む）に同じ patch を当てて、開いたもの・入口・段階・残り回数・親プレイを出す。画面が閉じたら
   `combat_log` に `kind=selection`（選んだ札・skip）を残す
3. **計算値の残り**: `player.potion_generation_pool`（`PotionFactory.GetPotionOptions`）と、撃つ前の X（`Hook.ModifyXValue`）。

見込みの大きさ（今の MOD の同種の実装から）: C# は新しいファイル 3〜4 本で 1,500 行前後（combat_log の本体と写像 600〜800、
patch の表と導入 300、decision_context 300、markdown の整形 150）、文書（`docs/raw-full.md`・`docs/fork-differences.md`）300 行前後、
契約の試験。schema は MINOR を 1 つ上げる。sts2#1115 の段階・残り枚数は 2 の decision_context と 1 の draw の行に入るので、
進行中のブランチと重ねるなら先に合流させる。

## exposed（MOD が出しているもの）

| キー | JSON キー | MOD の箇所 |
|---|---|---|
| `cmd:CardCmd.Afflict` | カードの項目（is_upgraded・enchantment・affliction・keywords） | McpMod.CardDetails.cs:45 |
| `cmd:CardCmd.ApplyKeyword` | カードの項目（is_upgraded・enchantment・affliction・keywords） | McpMod.StateBuilder.cs:1857 |
| `cmd:CardCmd.ApplySingleTurnRetain` | カードの項目（is_upgraded・enchantment・affliction・keywords） | McpMod.StateBuilder.cs:1857 |
| `cmd:CardCmd.AutoPlay` | card_resolution_events[kind=play].is_auto_play | McpMod.CombatHistory.cs:282 |
| `cmd:CardCmd.ClearAffliction` | カードの項目（is_upgraded・enchantment・affliction・keywords） | McpMod.CardDetails.cs:45 |
| `cmd:CardCmd.ClearEnchantment` | カードの項目（is_upgraded・enchantment・affliction・keywords） | McpMod.CardDetails.cs:63 |
| `cmd:CardCmd.Downgrade` | カードの項目（is_upgraded・enchantment・affliction・keywords） | McpMod.StateBuilder.cs:1853 |
| `cmd:CardCmd.Enchant` | カードの項目（is_upgraded・enchantment・affliction・keywords） | McpMod.CardDetails.cs:63 |
| `cmd:CardCmd.RemoveKeyword` | カードの項目（is_upgraded・enchantment・affliction・keywords） | McpMod.StateBuilder.cs:1857 |
| `cmd:CardCmd.Upgrade` | カードの項目（is_upgraded・enchantment・affliction・keywords） | McpMod.StateBuilder.cs:1853 |
| `cmd:CardPileCmd.AddCurseToDeck` | player.deck | McpMod.StateBuilder.cs:1825 |
| `cmd:CardPileCmd.AddCursesToDeck` | player.deck | McpMod.StateBuilder.cs:1825 |
| `cmd:CardPileCmd.AddGeneratedCardToCombat` | card_resolution_events[kind=generated] | McpMod.CombatHistory.cs:235 |
| `cmd:CardPileCmd.AddGeneratedCardsToCombat` | card_resolution_events[kind=generated] | McpMod.CombatHistory.cs:235 |
| `cmd:CardPileCmd.AddToCombatAndPreview` | card_resolution_events[kind=generated] | McpMod.CombatHistory.cs:235 |
| `cmd:CardPileCmd.AutoPlayFromDrawPile` | card_resolution_events[kind=auto_play_reservation] | McpMod.CombatHistory.cs:183 |
| `cmd:CardPileCmd.Draw` | card_resolution_events[kind=draw] | McpMod.CombatHistory.cs:228, McpMod.CombatHistory.cs:230 |
| `cmd:CardPileCmd.DrawWithoutBlockingOnOtherPlayers` | card_resolution_events[kind=draw] | McpMod.CombatHistory.cs:228 |
| `cmd:CardPileCmd.RemoveFromDeck` | player.deck | McpMod.StateBuilder.cs:1825 |
| `cmd:CreatureCmd.Stun` | enemies[].is_stun | McpMod.EnemyStunHandler.cs:61 |
| `cmd:MapCmd.SetBossEncounter` | map.boss | McpMod.StateBuilder.cs:2805 |
| `cmd:PlayerCmd.AddPet` | player.pets | McpMod.StateBuilder.cs:1724 |
| `cmd:PlayerCmd.GainGold` | player.gold | McpMod.StateBuilder.cs:1728 |
| `cmd:PlayerCmd.GainMaxPotionCount` | player.max_potion_slots | McpMod.StateBuilder.cs:1786 |
| `cmd:PlayerCmd.LoseGold` | player.gold | McpMod.StateBuilder.cs:1728 |
| `cmd:PlayerCmd.LoseMaxPotionCount` | player.max_potion_slots | McpMod.StateBuilder.cs:1786 |
| `cmd:PlayerCmd.MimicRestSiteHeal` | rest_site | McpMod.StateBuilder.cs:603 |
| `cmd:PlayerCmd.SetGold` | player.gold | McpMod.StateBuilder.cs:1728 |
| `cmd:RelicCmd.Melt` | player.relics[].melted | McpMod.StateBuilder.cs:1751 |
| `cmd:RelicCmd.Obtain` | player.relics | McpMod.StateBuilder.cs:1773 |
| `cmd:RelicCmd.Remove` | player.relics | McpMod.StateBuilder.cs:1773 |
| `cmd:RelicCmd.Replace` | player.relics | McpMod.StateBuilder.cs:1773 |
| `cmd:RewardsCmd.GenerateCustom` | rewards | McpMod.StateBuilder.cs:479 |
| `cmd:RewardsCmd.GenerateForRoomEnd` | rewards | McpMod.StateBuilder.cs:479 |
| `cmd:RewardsCmd.OfferCustom` | rewards | McpMod.StateBuilder.cs:479 |
| `cmd:RewardsCmd.OfferForRoomEnd` | rewards | McpMod.StateBuilder.cs:479 |
| `computed:CardFactory.CreateForMerchant` | shop | McpMod.StateBuilder.cs:590 |
| `computed:CardFactory.CreateForReward` | card_reward | McpMod.StateBuilder.cs:474 |
| `computed:CardFactory.CreateRandomCardForTransform` | player.transformation_options | McpMod.StateBuilder.cs:1686 |
| `computed:CardFactory.FilterForCombat` | player.generation_pool | McpMod.StateBuilder.cs:1634 |
| `computed:CardFactory.GetDefaultTransformationOptions` | player.transformation_options | McpMod.StateBuilder.cs:1686 |
| `computed:CardFactory.GetDistinctForCombat` | player.generation_pool | McpMod.StateBuilder.cs:1634 |
| `computed:CardFactory.GetForCombat` | player.generation_pool | McpMod.StateBuilder.cs:1634 |
| `computed:Hook.ModifyAttackHitCount` | hand[].preview.hits | McpMod.CardPreview.cs:20 |
| `computed:Hook.ModifyBlock` | hand[].preview.block | McpMod.CardPreview.cs:27 |
| `computed:Hook.ModifyCardBeingAddedToDeck` | player.deck | McpMod.StateBuilder.cs:1825 |
| `computed:Hook.ModifyCardPlayCount` | card_resolution_events[kind=play].play_count | McpMod.CombatHistory.cs:285 |
| `computed:Hook.ModifyCardRewardAlternatives` | card_reward | McpMod.StateBuilder.cs:474 |
| `computed:Hook.ModifyCardRewardCreationOptions` | card_reward | McpMod.StateBuilder.cs:474 |
| `computed:Hook.ModifyDamage` | hand[].vs_targets / enemies[].intents | McpMod.StateBuilder.cs:1915, McpMod.StateBuilder.cs:2210 |
| `computed:Hook.ModifyEnergyCostInCombat` | hand[].energy_cost | McpMod.StateBuilder.cs:1907 |
| `computed:Hook.ModifyExtraRestSiteHealText` | rest_site | McpMod.StateBuilder.cs:603 |
| `computed:Hook.ModifyExtraRestSiteMendText` | rest_site | McpMod.StateBuilder.cs:603 |
| `computed:Hook.ModifyGeneratedMap` | map | McpMod.StateBuilder.cs:2799 |
| `computed:Hook.ModifyGeneratedMapLate` | map | McpMod.StateBuilder.cs:2799 |
| `computed:Hook.ModifyGoldGained` | player.gold | McpMod.StateBuilder.cs:1728 |
| `computed:Hook.ModifyKeywordsInCombat` | hand[].keywords | McpMod.StateBuilder.cs:1857 |
| `computed:Hook.ModifyMaxEnergy` | player.max_energy | McpMod.StateBuilder.cs:1653 |
| `computed:Hook.ModifyMerchantCardCreationResults` | shop | McpMod.StateBuilder.cs:590 |
| `computed:Hook.ModifyMerchantPrice` | shop.*.price | McpMod.StateBuilder.cs:2483 |
| `computed:Hook.ModifyRestSiteHealAmount` | rest_site | McpMod.StateBuilder.cs:603 |
| `computed:Hook.ModifyRestSiteHealRewards` | rest_site | McpMod.StateBuilder.cs:603 |
| `computed:Hook.ModifyRestSiteOptions` | rest_site | McpMod.StateBuilder.cs:603 |
| `computed:Hook.ModifyRewards` | rewards | McpMod.StateBuilder.cs:479 |
| `computed:Hook.ModifyStarCost` | hand[].star_cost_state | McpMod.StateBuilder.cs:1908 |
| `computed:Hook.ShouldAddToDeck` | player.deck | McpMod.StateBuilder.cs:1825 |
| `computed:Hook.ShouldAllowFreeTravel` | map | McpMod.StateBuilder.cs:2799 |
| `computed:Hook.ShouldAllowHitting` | enemies[].hittable | McpMod.EnemyHittable.cs:18 |
| `computed:Hook.ShouldAllowMerchantCardRemoval` | shop | McpMod.StateBuilder.cs:590 |
| `computed:Hook.ShouldAllowSelectingMoreCardRewards` | rewards | McpMod.StateBuilder.cs:479 |
| `computed:Hook.ShouldAllowTargeting` | potions[].valid_target_entity_ids | McpMod.PotionUsabilityLive.cs:251 |
| `computed:Hook.ShouldDisableRemainingRestSiteOptions` | rest_site | McpMod.StateBuilder.cs:603 |
| `computed:Hook.ShouldForcePotionReward` | rewards | McpMod.StateBuilder.cs:479 |
| `computed:Hook.ShouldGenerateTreasure` | rewards | McpMod.StateBuilder.cs:479 |
| `computed:Hook.ShouldPlay` | hand[].can_play | McpMod.CardDetails.cs:22 |
| `computed:Hook.ShouldProceedToNextMapPoint` | map | McpMod.StateBuilder.cs:2799 |
| `computed:Hook.ShouldRefillMerchantEntry` | shop | McpMod.StateBuilder.cs:590 |
| `computed:Hook.TryModifyCardRewardOptions` | card_reward | McpMod.StateBuilder.cs:474 |
| `computed:Hook.TryReplaceDefaultRoomRewards` | rewards | McpMod.StateBuilder.cs:479 |
| `history:CardDrawnEntry` | card_resolution_events[kind=draw] | McpMod.CombatHistory.cs:228 |
| `history:CardGeneratedEntry` | card_resolution_events[kind=generated] | McpMod.CombatHistory.cs:235 |
| `history:CardPlayFinishedEntry` | card_resolution_events[kind=play,phase=finished] | McpMod.CombatHistory.cs:282 |
| `history:CardPlayStartedEntry` | card_resolution_events[kind=play,phase=started] | McpMod.CombatHistory.cs:282 |
| `hook:Hook.AfterActEntered` | map | McpMod.StateBuilder.cs:2799 |
| `hook:Hook.AfterGoldGained` | player.gold | McpMod.StateBuilder.cs:1728 |
| `hook:Hook.AfterItemPurchased` | shop | McpMod.StateBuilder.cs:590 |
| `hook:Hook.AfterMapGenerated` | map.nodes | McpMod.StateBuilder.cs:2799 |
| `hook:Hook.AfterModifyingCardRewardOptions` | card_reward | McpMod.StateBuilder.cs:474 |
| `hook:Hook.AfterModifyingGoldGained` | player.gold | McpMod.StateBuilder.cs:1728 |
| `hook:Hook.AfterModifyingRewards` | rewards | McpMod.StateBuilder.cs:479 |
| `hook:Hook.AfterRestSiteHeal` | rest_site | McpMod.StateBuilder.cs:603 |
| `hook:Hook.AfterRestSiteSmith` | rest_site / player.deck | McpMod.StateBuilder.cs:603 |
| `hook:Hook.AfterRewardTaken` | rewards | McpMod.StateBuilder.cs:479 |
| `hook:Hook.AfterRoomEntered` | player.relics / hp / gold | McpMod.StateBuilder.cs:1773 |
| `hook:Hook.BeforeCardRemoved` | player.deck | McpMod.StateBuilder.cs:1825 |
| `hook:Hook.BeforeCombatRewardOffered` | rewards | McpMod.StateBuilder.cs:479 |
| `hook:Hook.BeforeRoomEntered` | map / state_type | McpMod.StateBuilder.cs:2799 |
| `manual:CardModel.OnPlayWrapper` | card_resolution_events[kind=play] | McpMod.CombatHistory.cs:282 |
| `manual:CardReward` | card_reward | McpMod.StateBuilder.cs:474 |
| `manual:CombatState.IterateHookListeners` | <screen>.choice_source.hook.remaining_listeners | McpMod.ChoiceSource.cs:315 |
| `manual:EventModel.CurrentOptions` | event.options | McpMod.StateBuilder.cs:564 |
| `select:CardSelectCmd.FromChooseABundleScreen` | bundle_select | McpMod.StateBuilder.cs:454 |
| `select:CardSelectCmd.FromChooseACardScreen` | <screen>.choice_source（開いたモデル・hook・後続の listener・親プレイ） | McpMod.ChoiceSource.cs:32 |
| `select:CardSelectCmd.FromCombatPile` | <screen>.choice_source（開いたモデル・hook・後続の listener・親プレイ） | McpMod.ChoiceSource.cs:35 |
| `select:CardSelectCmd.FromHand` | <screen>.choice_source（開いたモデル・hook・後続の listener・親プレイ） | McpMod.ChoiceSource.cs:37 |
| `select:CardSelectCmd.FromHandForDiscard` | <screen>.choice_source（開いたモデル・hook・後続の listener・親プレイ） | McpMod.ChoiceSource.cs:38 |
| `select:CardSelectCmd.FromHandForUpgrade` | <screen>.choice_source（開いたモデル・hook・後続の listener・親プレイ） | McpMod.ChoiceSource.cs:39 |
| `select:CardSelectCmd.FromSimpleGrid` | <screen>.choice_source（開いたモデル・hook・後続の listener・親プレイ） | McpMod.ChoiceSource.cs:34 |
| `select:CardSelectCmd.FromSimpleGridForRewards` | <screen>.choice_source（開いたモデル・hook・後続の listener・親プレイ） | McpMod.ChoiceSource.cs:33 |
| `select:CardSelectorPrefs.Cancelable` | <screen>.can_cancel | McpMod.StateBuilder.cs:3289 |
| `select:CardSelectorPrefs.MaxSelect` | <screen>.max_select | McpMod.StateBuilder.cs:3253 |
| `select:CardSelectorPrefs.MinSelect` | <screen>.min_select | McpMod.StateBuilder.cs:3253 |
| `select:CardSelectorPrefs.Prompt` | <screen>.prompt | McpMod.StateBuilder.cs:3231 |
| `select:CardSelectorPrefs.RequireManualConfirmation` | <screen>.can_confirm | McpMod.StateBuilder.cs:3309 |
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
| `computed:Hook.ModifyCardPlayResultLocation` | public-missing | ModifyCardPlayResultLocation | combat |  | 3 | 1 | CardModel, CardPileCmd |
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
| `computed:Hook.ModifyHandDraw` | public-missing | ModifyHandDraw, ModifyHandDrawLate | combat |  | 13 | 7 | CombatManager |
| `computed:Hook.ModifyHpLost` | public-missing | ModifyHpLostBeforeOsty, ModifyHpLostBeforeOstyLate, ModifyHpLostAfterOsty, ModifyHpLostAfterOstyLate | run |  | 7 | 0 | CreatureCmd |
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
| `computed:Hook.ModifyPowerAmountGiven` | public-missing | ModifyPowerAmountGivenAdditive, ModifyPowerAmountGivenMultiplicative | combat |  | 1 | 1 | PowerCmd, PowerVar |
| `computed:Hook.ModifyPowerAmountReceived` | public-missing | TryModifyPowerAmountReceived | combat |  | 2 | 0 | PowerCmd |
| `computed:Hook.ModifyPreBlockDamageTarget` | public-missing | ModifyPreBlockDamageTarget |  |  | 0 | 1 | CreatureCmd |
| `computed:Hook.ModifyRestSiteHealAmount` | exposed | ModifyRestSiteHealAmount | run |  | 1 | 0 | HealRestSiteOption, MendRestSiteOption |
| `computed:Hook.ModifyRestSiteHealRewards` | exposed | TryModifyRestSiteHealRewards | run |  | 2 | 0 | HealRestSiteOption |
| `computed:Hook.ModifyRestSiteOptions` | exposed | TryModifyRestSiteOptions | run |  | 8 | 0 | RestSiteOption |
| `computed:Hook.ModifyRewards` | exposed | TryModifyRewards, TryModifyRewardsLate | run |  | 7 | 0 | RewardsSet |
| `computed:Hook.ModifyShuffleOrder` | hidden | ModifyShuffleOrder | combat |  | 1 | 0 | CardPile, CardPileCmd |
| `computed:Hook.ModifyStarCost` | exposed | TryModifyStarCost | combat |  | 1 | 1 | CardModel |
| `computed:Hook.ModifySummonAmount` | out_of_scope | ModifySummonAmount | combat |  | 0 | 0 | OstyCmd, SummonVar |
| `computed:Hook.ModifyUnblockedDamageTarget` | public-missing | ModifyUnblockedDamageTarget |  |  | 0 | 1 | CreatureCmd |
| `computed:Hook.ModifyUnknownMapPointRoomTypes` | hidden | ModifyUnknownMapPointRoomTypes | run |  | 4 | 0 | UnknownMapPointOdds |
| `computed:Hook.ModifyXValue` | public-missing | ModifyXValue | combat |  | 1 | 0 | CardModel |
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
| `hook:Hook.AfterAttack` | public-missing | AfterAttack | combat | ✓ | 6 | 3 | AttackCommand, AttackContext |
| `hook:Hook.AfterAutoPostPlayPhaseEntered` | public-missing | AfterAutoPostPlayPhaseEntered | combat | ✓ | 2 | 2 | CombatManager, PlayerTurnPhase |
| `hook:Hook.AfterAutoPrePlayPhaseEntered` | public-missing | AfterAutoPrePlayPhaseEnteredEarly, AfterAutoPrePlayPhaseEntered, AfterAutoPrePlayPhaseEnteredLate | combat | ✓ | 4 | 1 | CombatManager, PlayerCombatState, PlayerTurnPhase |
| `hook:Hook.AfterBlockBroken` | public-missing | AfterBlockBroken | combat_all | ✓ | 2 | 0 | CreatureCmd |
| `hook:Hook.AfterBlockCleared` | public-missing | AfterBlockCleared | combat |  | 6 | 0 | CombatManager |
| `hook:Hook.AfterBlockGained` | public-missing | AfterBlockGained | combat |  | 2 | 0 | CreatureCmd |
| `hook:Hook.AfterCardChangedPiles` | public-missing | AfterCardChangedPiles, AfterCardChangedPilesLate | run |  | 6 | 1 | CardCmd, CardPileCmd |
| `hook:Hook.AfterCardDiscarded` | out_of_scope | AfterCardDiscarded | combat | ✓ | 0 | 2 | CardCmd |
| `hook:Hook.AfterCardDrawn` | public-missing | AfterCardDrawnEarly, AfterCardDrawn | combat | ✓ | 6 | 7 | CardPileCmd |
| `hook:Hook.AfterCardEnteredCombat` | public-missing | AfterCardEnteredCombat | combat |  | 10 | 4 | CardCmd, CardPileCmd |
| `hook:Hook.AfterCardExhausted` | public-missing | AfterCardExhausted | combat | ✓ | 8 | 0 | CardCmd |
| `hook:Hook.AfterCardGeneratedForCombat` | public-missing | AfterCardGeneratedForCombat | combat |  | 1 | 9 | CardCmd, CardPileCmd |
| `hook:Hook.AfterCardPlayed` | public-missing | AfterCardPlayed, AfterCardPlayedLate | combat_all | ✓ | 45 | 31 | BranchingPlayerChoiceContext, CardModel |
| `hook:Hook.AfterCombatEnd` | public-missing | AfterCombatEnd | run |  | 39 | 6 | CombatManager |
| `hook:Hook.AfterCombatVictory` | public-missing | AfterCombatVictoryEarly, AfterCombatVictory | run |  | 6 | 0 | CombatManager |
| `hook:Hook.AfterCreatureAddedToCombat` | public-missing | AfterCreatureAddedToCombat | combat_all |  | 3 | 0 | CreatureCmd |
| `hook:Hook.AfterCurrentHpChanged` | public-missing | AfterCurrentHpChanged | run |  | 5 | 1 | CreatureCmd |
| `hook:Hook.AfterDamageGiven` | public-missing | AfterDamageGiven | combat_all | ✓ | 3 | 6 | CreatureCmd |
| `hook:Hook.AfterDamageReceived` | public-missing | AfterDamageReceived, AfterDamageReceivedLate | run | ✓ | 20 | 2 | CreatureCmd |
| `hook:Hook.AfterDeath` | public-missing | AfterDeath | run |  | 30 | 5 | CreatureCmd, Player |
| `hook:Hook.AfterDiedToDoom` | out_of_scope | AfterDiedToDoom | combat_all |  | 0 | 1 | DoomPower |
| `hook:Hook.AfterEnergyReset` | public-missing | AfterEnergyReset, AfterEnergyResetLate | combat |  | 5 | 5 | BoundPhylactery, CombatManager |
| `hook:Hook.AfterEnergySpent` | out_of_scope | AfterEnergySpent | combat |  | 0 | 1 | CardModel |
| `hook:Hook.AfterFlush` | out_of_scope | AfterFlush | combat | ✓ | 0 | 1 | CombatManager |
| `hook:Hook.AfterForge` | out_of_scope | AfterForge | combat |  | 0 | 1 | ForgeCmd |
| `hook:Hook.AfterGoldGained` | exposed | AfterGoldGained | run |  | 1 | 0 | PlayerCmd |
| `hook:Hook.AfterHandEmptied` | public-missing | AfterHandEmptied | combat | ✓ | 1 | 0 | CombatManager |
| `hook:Hook.AfterItemPurchased` | exposed | AfterItemPurchased | run |  | 1 | 0 | MerchantEntry |
| `hook:Hook.AfterMapGenerated` | exposed | AfterMapGenerated | run |  | 1 | 0 | RunManager |
| `hook:Hook.AfterModifyingBlockAmount` | public-missing | AfterModifyingBlockAmount | combat |  | 3 | 0 | CreatureCmd |
| `hook:Hook.AfterModifyingCardPlayCount` | public-missing | AfterModifyingCardPlayCount | combat |  | 4 | 4 | CardModel |
| `hook:Hook.AfterModifyingCardRewardOptions` | exposed | AfterModifyingCardRewardOptions | run |  | 2 | 0 | CardFactory, CardReward |
| `hook:Hook.AfterModifyingDamageAmount` | public-missing | AfterModifyingDamageAmount | run |  | 3 | 0 | CreatureCmd |
| `hook:Hook.AfterModifyingEnergyGain` | out_of_scope | AfterModifyingEnergyGain | combat |  | 0 | 1 | PlayerCmd |
| `hook:Hook.AfterModifyingGoldGained` | exposed | AfterModifyingGoldGained | run |  | 2 | 0 | PlayerCmd |
| `hook:Hook.AfterModifyingHandDraw` | public-missing | AfterModifyingHandDraw | combat |  | 4 | 0 | CombatManager |
| `hook:Hook.AfterModifyingHpLostAfterOsty` | public-missing | AfterModifyingHpLostAfterOsty | run |  | 5 | 0 | CreatureCmd |
| `hook:Hook.AfterModifyingHpLostBeforeOsty` | public-missing | AfterModifyingHpLostBeforeOsty | run |  | 1 | 0 | CreatureCmd |
| `hook:Hook.AfterModifyingOrbPassiveTriggerCount` | out_of_scope | AfterModifyingOrbPassiveTriggerCount | combat |  | 0 | 1 | OrbModel |
| `hook:Hook.AfterModifyingPowerAmountGiven` | out_of_scope | AfterModifyingPowerAmountGiven | combat |  | 0 | 1 | PowerCmd |
| `hook:Hook.AfterModifyingPowerAmountReceived` | public-missing | AfterModifyingPowerAmountReceived | combat |  | 2 | 0 | PowerCmd |
| `hook:Hook.AfterModifyingRewards` | exposed | AfterModifyingRewards | run |  | 2 | 0 | RewardsSet |
| `hook:Hook.AfterOrbChanneled` | out_of_scope | AfterOrbChanneled | combat | ✓ | 0 | 1 | OrbCmd |
| `hook:Hook.AfterOrbEvoked` | out_of_scope | AfterOrbEvoked | combat | ✓ | 0 | 1 | OrbCmd |
| `hook:Hook.AfterOstyRevived` | out_of_scope | AfterOstyRevived | combat |  | 1 | 0 | OstyCmd |
| `hook:Hook.AfterPlayerTurnStart` | public-missing | AfterPlayerTurnStartEarly, AfterPlayerTurnStart, AfterPlayerTurnStartLate | combat | ✓ | 18 | 9 | CombatManager |
| `hook:Hook.AfterPotionDiscarded` | public-missing | AfterPotionDiscarded | run |  | 1 | 0 | PotionCmd |
| `hook:Hook.AfterPotionProcured` | public-missing | AfterPotionProcured | run |  | 1 | 0 | PotionCmd |
| `hook:Hook.AfterPotionUsed` | public-missing | AfterPotionUsed | run |  | 2 | 0 | PotionModel |
| `hook:Hook.AfterPowerAmountChanged` | public-missing | AfterPowerAmountChanged | combat | ✓ | 7 | 5 | PowerCmd |
| `hook:Hook.AfterPreventingBlockClear` | public-missing | AfterPreventingBlockClear | combat |  | 2 | 0 | Creature |
| `hook:Hook.AfterPreventingDeath` | public-missing | AfterPreventingDeath | run |  | 2 | 0 | CreatureCmd |
| `hook:Hook.AfterPreventingDraw` | public-missing | AfterPreventingDraw | combat |  | 1 | 0 | CardPileCmd |
| `hook:Hook.AfterRestSiteHeal` | exposed | AfterRestSiteHeal | run |  | 2 | 0 | HealRestSiteOption, MendRestSiteOption |
| `hook:Hook.AfterRestSiteSmith` | exposed | AfterRestSiteSmith | run |  | 0 | 0 | SmithRestSiteOption |
| `hook:Hook.AfterRewardTaken` | exposed | AfterRewardTaken | run |  | 0 | 0 | Reward |
| `hook:Hook.AfterRoomEntered` | exposed | AfterRoomEntered | run |  | 33 | 4 | CombatRoom, EventRoom, MerchantRoom, RestSiteRoom …(+1) |
| `hook:Hook.AfterShuffle` | public-missing | AfterShuffle | combat | ✓ | 3 | 1 | CardPileCmd |
| `hook:Hook.AfterSideTurnEnd` | public-missing | AfterSideTurnEnd, AfterSideTurnEndLate | combat |  | 46 | 31 | CombatManager, PlayerTurnPhase |
| `hook:Hook.AfterSideTurnStart` | public-missing | AfterSideTurnStart, AfterSideTurnStartLate | combat |  | 32 | 22 | CombatManager, PlayerTurnPhase |
| `hook:Hook.AfterStarsGained` | out_of_scope | AfterStarsGained | combat |  | 0 | 1 | PlayerCmd |
| `hook:Hook.AfterStarsSpent` | out_of_scope | AfterStarsSpent | combat |  | 0 | 3 | CardModel |
| `hook:Hook.AfterSummon` | out_of_scope | AfterSummon | combat | ✓ | 0 | 0 | OstyCmd |
| `hook:Hook.AfterTakingExtraTurn` | public-missing | AfterTakingExtraTurn | combat |  | 2 | 0 | CombatManager |
| `hook:Hook.BeforeAttack` | public-missing | BeforeAttack | combat |  | 4 | 1 | AttackCommand, AttackContext |
| `hook:Hook.BeforeBlockGained` | public-missing | BeforeBlockGained | combat |  | 0 | 0 | CreatureCmd |
| `hook:Hook.BeforeCardAutoPlayed` | public-missing | BeforeCardAutoPlayed | combat |  | 0 | 0 | CardCmd |
| `hook:Hook.BeforeCardPlayed` | public-missing | BeforeCardPlayed | combat |  | 14 | 17 | CardModel |
| `hook:Hook.BeforeCardRemoved` | exposed | BeforeCardRemoved | run |  | 1 | 0 | CardPileCmd |
| `hook:Hook.BeforeCombatRewardOffered` | exposed | BeforeCombatRewardOffered | run |  | 1 | 0 | CombatRoom |
| `hook:Hook.BeforeCombatStart` | public-missing | BeforeCombatStart, BeforeCombatStartLate | run |  | 24 | 1 | CombatManager |
| `hook:Hook.BeforeDamageReceived` | public-missing | BeforeDamageReceived | run | ✓ | 1 | 0 | CreatureCmd |
| `hook:Hook.BeforeDeath` | public-missing | BeforeDeath | run |  | 4 | 0 | CreatureCmd |
| `hook:Hook.BeforeFlush` | public-missing | BeforeFlush, BeforeFlushLate | combat |  | 1 | 0 | CombatManager |
| `hook:Hook.BeforeHandDraw` | public-missing | BeforeHandDraw, BeforeHandDrawLate | combat | ✓ | 9 | 15 | CombatManager |
| `hook:Hook.BeforePotionUsed` | public-missing | BeforePotionUsed | run |  | 1 | 0 | PotionModel |
| `hook:Hook.BeforePowerAmountChanged` | public-missing | BeforePowerAmountChanged | combat |  | 1 | 1 | PowerCmd |
| `hook:Hook.BeforeRoomEntered` | exposed | BeforeRoomEntered | run |  | 1 | 0 | RunManager |
| `hook:Hook.BeforeSideTurnEnd` | public-missing | BeforeSideTurnEndVeryEarly, BeforeSideTurnEndEarly, BeforeSideTurnEnd | combat |  | 17 | 2 | CombatManager, PlayerTurnPhase |
| `hook:Hook.BeforeSideTurnStart` | public-missing | BeforeSideTurnStart | combat |  | 21 | 10 | BoundPhylactery, CombatManager |

## 付録: 手番の段階（CombatManager）の呼び出し順

| メソッド | 分類 | 呼び出し（出現順・重複除く） |
|---|---|---|
| `CombatManager.IsCombatEnding` (444) | internal | Hook.ShouldStopCombatFromEnding |
| `CombatManager.StartCombatInternal` (606) | public-missing | Hook.BeforeCombatStart |
| `CombatManager.StartTurn` (709) | public-missing | Hook.BeforeSideTurnStart, Hook.AfterBlockCleared, Hook.AfterSideTurnStart |
| `CombatManager.RunAutoPrePlayPhase` (884) | public-missing | Hook.AfterAutoPrePlayPhaseEntered |
| `CombatManager.SetupPlayerTurn` (902) | public-missing | Hook.ShouldPlayerResetEnergy, Hook.AfterEnergyReset, Hook.BeforeHandDraw, Hook.ModifyHandDraw, Hook.AfterModifyingHandDraw, CardPileCmd.Draw, Hook.AfterPlayerTurnStart |
| `CombatManager.CheckForEmptyHand` (1231) | internal | Hook.AfterHandEmptied |
| `CombatManager.HandlePlayerDeath` (1301) | out_of_scope | PlayerCmd.SetEnergy, PlayerCmd.SetStars |
| `CombatManager.RemoveDeadPlayerCardsFromCombat` (1325) | out_of_scope | CardPileCmd.RemoveFromCombat |
| `CombatManager.EndCombatInternal` (1380) | public-missing | Hook.AfterCombatEnd, Hook.AfterCombatVictory |
| `CombatManager.EndPlayerTurnPhaseOneInternal` (1614) | public-missing | Hook.AfterAutoPostPlayPhaseEntered, Hook.BeforeSideTurnEnd, Hook.BeforeFlush |
| `CombatManager.DoTurnEnd` (1698) | public-missing | Hook.ShouldEtherealTrigger, CardCmd.Exhaust |
| `CombatManager.AddTurnEndCardToPlayPileWithDelay` (1764) | cosmetic | CardPileCmd.Add |
| `CombatManager.ResolveTurnEndCardEffects` (1770) | public-missing | CardPileCmd.Add, CardCmd.Exhaust |
| `CombatManager.AwaitTurnEndCardTween` (1787) | cosmetic | CardPileCmd.GetTweenForCardsChangingPiles |
| `CombatManager.EndEnemyTurnInternal` (1806) | public-missing | Hook.BeforeSideTurnEnd, Hook.AfterSideTurnEnd |
| `CombatManager.EndPlayerTurnPhaseTwoInternal` (1856) | public-missing | Hook.AfterSideTurnEnd |
| `CombatManager.FlushPlayerHand` (1895) | public-missing | Hook.ShouldFlush, CardPileCmd.Add, Hook.AfterFlush |
| `CombatManager.SwitchFromPlayerToEnemySide` (1935) | public-missing | Hook.ShouldTakeExtraTurn, Hook.AfterTakingExtraTurn |
