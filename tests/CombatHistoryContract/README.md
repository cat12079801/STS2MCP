# 戦闘履歴 state の契約試験

本番の `McpMod.CombatHistory.cs` / `McpMod.CombatHistoryFormatting.cs` /
`McpMod.Warnings.cs` を直接リンクする。追加パッケージ・ゲーム・Godot は不要。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/libexec/dotnet run --project tests/CombatHistoryContract -c Release -p:BuildInParallel=false
```

開始／完了の区別、全 CardType と 0、リプレイの重複、所有者の絞り込み、
本体のターン判定への引数、このターンと前手番の uid の順序・重複・他 Player のプレイを検査する。
廃棄は entry の数、被ダメは正の UnblockedDamage の件数をターン／戦闘で区別する。
不動のブロックは CardPlay・プレイした Player・Move を検査し、Receiver で絞らず、
同じ CardPlay の複数件をそのまま数える。

履歴のリセット後の再読取と snapshot の独立、History／Entries／開始・完了 getter／
型 getter／ターン判定の失敗、combat state／PlayerCombatState の欠落での
null・理由・warnings と他項目の保持、復旧を確認する。
JSON は本番の `_jsonOptions` と同じ WhenWritingNull・SnakeCaseLower・Encoder 設定で検査し、
数値／文字列／null 型、辞書の null が残ることと `by_type` の Attack 等のキーが変わらないことを確認する。
Markdown の `this turn=[...]` / `last turn=[...]`、非空・空・失敗表示も検査する。
巨大な state builder はリンクせず、ソースの文字列検査で戦闘限定・共通の Player builder・
UID registry の更新・マルチ・Markdown の配線と schema 4 を確認する。

ターン判定と uid registry は境界ダブル。判定結果を設定し、本体メソッドへの委譲を確かめる。
本体のターン進行・履歴記録や実際の registry の実行を再現した証拠ではない。
現行 DLL へのビルドとは別に、実機確認は調整役が v0.113.0 の検証用ランで行う。

1. 開始直後の 0／空配列と、Attack・Skill・Power の開始／完了数を照合する。
   リプレイ・自動プレイも entry ごとに増えることを確認する。
2. 廃棄のない手番／廃棄後で忘れられし儀式・イービルアイの判定を照合する。
3. ブロックで全て防いだ攻撃、複数 hit、自傷などで UnblockedDamage > 0 の件数を
   悪意・八つ裂きと照合する。ターン限定値と戦闘全体値の境界も確認する。
4. ボーラ／共鳴の手斧の前手番の uid を各パイル・手札と照合する。
   手番中に手斧を撃って完了したあとの `card_plays_finished_this_turn_card_uids` と、
   次手番の開始時に同じ uid の札が回収されるかを照合する。
   追加ターンでは player の TurnNumber と battle.round を区別する。
5. 不動を持ち、カードでの獲得とカード外の獲得、同じ CardPlay の複数回獲得を照合する。
   `block_gains_this_turn` は次のプレイの null 文脈の件数なので、落ち着いた state を使う。
6. 非戦闘で省略、新しい戦闘でリセット、マルチのローカル Player のみの集計を確認する
   （このターン／前手番の uid 集合は他 Player の札も含む）。

ゲーム build・MOD commit・schema・前後の state を記録する。
