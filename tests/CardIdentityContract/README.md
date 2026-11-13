# 山札の表示順と戦闘内 uid の契約試験

本番の `McpMod.CardIdentity.cs` と `McpMod.Warnings.cs` を直接リンクする。追加パッケージ・ゲーム・Godot は不要。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec \
  /opt/homebrew/opt/dotnet@9/libexec/dotnet build \
  tests/CardIdentityContract -c Release \
  -p:TreatWarningsAsErrors=true -p:BuildInParallel=false -p:UseSharedCompilation=false -m:1
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec \
  /opt/homebrew/opt/dotnet@9/libexec/dotnet run --no-build \
  --project tests/CardIdentityContract -c Release
```

山札が Rarity → CardModel.CompareTo → uid（ordinal）の順になることを検査する。
CardModel の比較は境界ダブルの返却順位を使い、本体の比較を MOD 側で再実装しない。
同じ ID の強化・付与・コスト・BaseReplayCount・基底値で比較が異なる場合と、
比較が完全に同じ場合を分ける。2・10・16・24枚の同順位札を回転・反転しても
同じ表示順を保つこと、元の山札を変更しないこと、捨て札・廃棄札・Play の入力順を確認する。
本体比較が例外を出した場合は Rarity → Id.Entry → uid の順になること、
`draw_pile.order` が本番の warning collector を通じて state に付くことも確認する。

デッキ由来の札は全 ID を通した0始まりのデッキ位置を uid にする。
再ロードに相当する新しい combat state・Player・デッキ・戦闘カードを作り、
DeckVersion で対応する札の uid が同じになることを検査する。
初回観測の順の反転、同 ID の別個体、マルチの同じ位置と別 NetId も対象とする。

生成札の番号の供給だけ固定列に置き換えた registry で、初回観測順と番号の大小が独立であること、
衝突時の再採番、異なる ID と別 Player の同一 ID、直接手札に生成した札・複製札の
新規番号、パイルから消えた札の番号予約、同じ戦闘内の refresh・札の変更での固定、
次の戦闘での初期化と `#g<乱数>` / `#<デッキ位置>` の空間の分離を検査する。
本番の OS 乱数を使う registry も通し、形式・同順位の解消・生成札を足した後の固定・
JSON snapshot の独立性を確認する。

巨大な state builder とゲームの操作は実行せず、ソースの配線検査で全パイルの表示順適用、
手札より前の refresh、手札・パイル・grid・履歴・play_card の共通 uid を確認する。
履歴と本番 registry の実際の共有は `CombatHistoryContract` でも検査する。
ゲーム側のソート・デッキから戦闘用カードへの複製・シャッフルの出典は
`docs/raw-full.md` の「戦闘カードの uid とパイル順序」を参照。
