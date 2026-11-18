# 戦闘の強化結果（`upgrade_preview`）の契約試験

本番の `McpMod.CombatUpgradePreview.cs` と、それが使う `McpMod.CardDetails.cs` /
`McpMod.CardEnergyCost.cs` / `McpMod.CardStarCost.cs` / `McpMod.InternalState.cs` /
`McpMod.Warnings.cs`、表示の `McpMod.UpgradePreviewFormatting.cs` / `McpMod.PileFormatting.cs` /
`McpMod.CardDetailsFormatting.cs` / `McpMod.CardEnergyCostFormatting.cs`、選択候補の
`McpMod.SelectionCandidate.cs` / `McpMod.SelectionCandidateFormatting.cs` を直接リンクする（uid の registry は境界ダブル）。
追加パッケージ・ゲーム・Godot は不要。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec \
  /opt/homebrew/opt/dotnet@9/libexec/dotnet run \
  --project tests/CombatUpgradePreviewContract -c Release \
  -p:BuildInParallel=false -p:UseSharedCompilation=false
```

境界ダブル（`Stubs.cs`）は v0.113.1（`sts2-a671e9ef`）の複製と強化の仕組みを写したもの:
`AbstractModel.MutableClone`（MemberwiseClone → DeepCloneFields → AfterCloned）、
`CardModel.DeepCloneFields`（付与の貼り直しで `EnchantmentChanged` / `AfflictionChanged` を呼ぶ）/
`AfterCloned` / `UpgradeInternal` / `FinalizeUpgradeInternal` / `GetKeywordsWithSources` / `UpgradeStarCostBy`、
`CardEnergyCost.GetWithModifiers` / `UpgradeBy` / `Clone`、`DynamicVar.Clone` / `UpgradeValueBy`、
`EnchantmentModel.DeepCloneFields` / `ApplyInternal`、`AfflictionModel.AfterCloned` / `Amount`。
札ごとの `OnUpgrade` は、汚れた短剣・幻影・コストを下げる札・スターコストを下げる札を写したダブルで試す。
大域のフック（`HexPower` のキーワード、`FreeAttackPower` のコスト）は設定した分だけ動く。

検査すること:

- 5 パイルそれぞれの `ready`: 強化後の BaseValue（6 → 9）、段階・上限・可否、本文が親のパイルの種類で作られること、
  `with_all_modifiers` が null と `detached_preview_context` であること、親の行と現物が変わらないこと。
- `not_upgradable`（強化済み、上限 0）は複製を作らず、3 つのキーだけを持つ。
- 多段の札（汚れた短剣 7 → 11 → 16）は次の 1 段だけ。
- 隔離: 付与（エンチャント・災禍）の付いた現物に全 event の購読を付け、プレビューを作っても購読が呼ばれないこと。
  付与の持ち主・`RecalculateCardValues` の回数・パイルの中身と順序・現物の全値が変わらないこと。
  対照として、ふつうの `MutableClone` では購読が呼ばれること。同じ読取を繰り返しても同じ JSON になること。
- 呪術の Ethereal は実効の `keyword_ids` にだけ入り、`local_keyword_ids` には入らない。幻影の強化は複製の local だけを変える。
- コスト: 基礎の低下と Absolute の修正の切り詰めが複製だけに入る。大域のコストのフックは機械向けの値では複製に対して呼ばない
  （表示用の `cost` だけが、ゲームの強化プレビューと同じくパイルの無い複製で 1 回呼ぶ）。X コストの欠落は正常。
- スターコストの基礎の低下で、それを超える一時コストが複製から外れる。単ターン保留はキーワードと別に出る。
- 失敗の局所化: 本文・型・タグ・数値変数（強化そのもの）・エネルギーの基礎・event を隔離できない型（拒否は型ごとにキャッシュする）・上限の不整合・
  親の可否／段階の読取失敗は `unavailable`（部分値なし、パス付きの理由、warnings）。表示用の `cost` の失敗は `ready` のまま理由だけ残る。
  隣の札は `ready` のまま。
- 本番と同じ JSON 設定での形（明示の null・変数名の大小・小数）、Markdown（戦闘の行は手札・パイルとも失敗だけ、
  `not_upgradable` は無し、`status` の無い戦闘外の形は従来どおり）。
- `?perf=1` の計数（行・複製・状態・時間）と、既定では出さないこと。
- 大きさの見積もり: 旧 f35 r1 s00（26 枚・強化可能 18 枚）と f40 r4 s00（25 枚・23 枚）と同じ枚数の行で、
  追加の項目が増やすバイト数を出力する（実機の本文・キーワードは長いので、実測は調整役が実機で行う）。
- ソースの配線: 手札と 4 パイルの共通経路、条件付きの手札だけのプレビューが残っていないこと、
  `hand_select` と報酬は従来の契約、戦闘外のプレビューも隔離した複製を使うこと、プレビューのコードが
  UID・`CardCmd`・`CloneCard`・乱数・履歴を呼ばないこと、schema 8、`?perf=1` の配線。

札の効果の入力（`cat12079801/sts2#1019`、schema 7）:

- `ready` に `retain_this_turn` / `single_turn_retain` / `sly_this_turn` / `single_turn_sly` / `base_replay_count` /
  `extra_replay_this_turn` / `enchanted_replay_count` があり、単ターンのフラグ・リプレイ回数・Glam の Times が複製に残ること
  （Disabled の Glam は複製でも数えない）、付与の `dynamic_vars`、Mad Science の `tinker_time_rider`（複製でも同じ、強化の Innate は複製だけ）。
  読取で購読が呼ばれず、現物が変わらないこと。0 と false も `ready` の値。複製でこれらが読めなければ `unavailable` と項目のパス。
- 選択候補の `combat_instance`（`McpMod.SelectionCandidate.cs`）: 5 パイルの札は `source: "pile"`・`pile`・その行と同じ `uid`
  （`values` なし）。パイルに無い生成候補は `source: "detached"`・`pile: null`・uid を発行しない（uid の registry を呼ばない）。
  `values` に id・型・対象・段階・数値・付与・Mad Science の rider・単ターンの期限・リプレイ・両方のキーワード・
  `exhaust_on_next_play`・`energy_cost`（`with_all_modifiers` は null と `detached_preview_context`）・`star_cost_state` があり、
  `can_play` / `unplayable_reason` / `vs_targets` / `upgrade_preview` は無い。デッキ（戦闘のパイル以外）の札は detached で `pile: "Deck"`。
  2 つのパイルにある札は対応付けず null と `ambiguous` の理由・warning、所有者の読取失敗は null と理由、値の失敗は `values` の理由。
  読取でパイルと札を変えないこと、wire と Markdown の 1 行（戦闘外は出さない）。
- 配線（ソース文字列）: `hand_select` と戦闘中の `card_reward` は `GetLiveCombatStateForSelection` が非 null のときだけ
  registry を更新して `combat_instance` を付ける。`card_select` と choose は戦闘の分岐の中だけ。候補の読取が
  `CanPlay`・可否・対象のプレビュー・`CardCmd`・複製・乱数・`AddCard` を呼ばず、uid はパイルの札にだけ求めること。

実際の札の `OnUpgrade`・ローカライズの本文・購読の付いた UI の実機照合は、調整役の検証で行う。
