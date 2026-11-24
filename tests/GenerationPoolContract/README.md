# 戦闘生成 pool の契約試験

本番の `McpMod.GenerationPool.cs` / `McpMod.Warnings.cs` を直接リンクする。ゲーム DLL・Godot は不要。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/libexec/dotnet run --project tests/GenerationPoolContract -c Release -p:BuildInParallel=false -p:UseSharedCompilation=false
```

`GetUnlockedCards` / `CardModel.IsAllowedInRun` / `CardFactory.FilterForCombat` は呼び出しを記録する境界ダブル。
builder が本体の判定に委譲すること（全候補に owner の run で `IsAllowedInRun` を呼ぶ、`FilterForCombat` を通す、
owner の `UnlockState` と run の `CardMultiplayerConstraint` を渡す）、`filter_version: 2` と `filter_source` の完全一致、
Blitz（`IsAllowedInBlitz=false` の除外）・複数人（SingleplayerOnly の除外）、ID 昇順・重複なし、
酸液の Attack/Skill/Power の型 filter、何でも屋の自己除外と型で絞らないこと、
GetUnlockedCards の例外・遅延列挙の途中失敗での `known: false`・`cards: []`・`reason`・warning、無色の失敗がキャラ pool に波及しないこと、
run にいない player の拒否を検査する。スプラッシュの候補は、`UnlockState.CharacterCardPools` から 2 つ以上なら自キャラの pool を除くこと（1 つだけなら除かない、自キャラの pool の `GetUnlockedCards` を呼ばない）、Attack だけ・`IsAllowedInRun` → `FilterForCombat`・ID 昇順・重複なし、1 つの pool の失敗で `known: false`、filter_source の完全一致を検査する。state builder とはソースの文字列検査で、無色の候補が戦闘中だけ、酸液の pool は従来どおり、
Markdown の 1 行、schema 12 を確認する。境界ダブルの判定は本体の `IsAllowedInRun` を写したもので、本体の判定を動かした証拠ではない。
無色ポーションの所持時だけ専用欄を出す配線、何でも屋の解放有無と全型、別 owner、空集合・途中失敗も検査する。

実機（調整役が v0.113.1 の検証用ランで確認する）:

1. オロビックの酸液を持った戦闘で、`player.generation_pool` が `known: true`・`filter_version: 2`・
   `game_assembly_sha256` が `a671e9ef…`、Ironclad の解放済みの Attack/Skill/Power のうち Basic・Ancient・Event と
   `CanBeGeneratedInCombat=false` を除いた一覧であること（`OrobicAcid.OnUse`、`CardFactory.FilterForCombat`）。
   通常モードの単独ランでは版 1 と同じ ID 集合になる見込み（Ironclad の pool に `IsAllowedInBlitz=false` の札は無い。
   該当 4 クラスは `Debt` / `MyShinies` / `HandOfGreed` / `Royalties`）。
2. 酸液を使った直後の手札の 3 枚（各型 1 枚）の ID が、それぞれ使用前の一覧の同じ型に含まれること。
3. 戦闘中に `player.colorless_generation_pool` が出て、`unlocked_pools.colorless_cards` から `JACK_OF_ALL_TRADES` と
   `FilterForCombat` で落ちる札（`CanBeGeneratedInCombat=false` の `HAND_OF_GREED` / `ROYALTIES` など、Basic・Ancient・Event）を除いた一覧であること。
   解放済みなら `MY_SHINIES` を含む。戦闘外の state には出ないこと。
4. 何でも屋を撃った直後に生成された札（通常 1 枚、強化 2 枚）がこの一覧に含まれること（`JackOfAllTrades.OnPlay`）。
5. 戦闘中に `player.other_character_attack_generation_pool` が出て、スプラッシュの choose の 3 枚（強化なら強化済み）の ID が
   この一覧に含まれること、自キャラ（アイアンクラッド）の札を含まないこと（`Splash.OnPlay`）。
