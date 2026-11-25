# patch の当て先の契約試験（ゲームの DLL で解決する）

ゲームがインストールされている環境だけで回る（`sts2.dll` / `0Harmony.dll` / `GodotSharp.dll` を参照する）。ゲームは起動しない。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/libexec/dotnet run --project tests/PatchTargetsContract -c Release -p:BuildInParallel=false -p:UseSharedCompilation=false
```

既定の `STS2GameDir` は macOS の Steam の場所。違うなら `-p:STS2GameDir=...` を渡す。

`Initialize` と同じ install のメソッドを同じ順（`TryApplyHarmonyPatches`・`InstallRewardTracking`・`InstallChoiceSourceTracking`・
`InstallAutoPlayReservationTracking`・`InstallCombatLogTracking`・`InstallDecisionContextTracking`、最後に戦闘の記録器の `RecorderPatches.Apply`〔`CombatRecorder.StartIfConfigured` が当てるもの。設定ファイルと記録の置き場に触らないよう直接呼ぶ〕）で本物の `sts2.dll` に対して呼び、次を確かめる。
外では `GD.Print*` が native の Godot に入って落ちるので、試験の中だけ Harmony で標準エラーに回す。

- すべての群が当たったこと（各 `*Error` が null、`CombatLogStore.InstallErrors` が空）
- Harmony の ID ごとの patch の数（combat_log 119、decision_context 9、choice_source 8）
- 履歴の行の出どころ: `CombatHistory` の公開の記録メソッド 18 件のそれぞれに combat_log の prefix・postfix が当たり、各メソッドが *Entry をちょうど 1 つ作り、
  *Entry の型がすべてどれかで作られること（private の `Add` に頼らない）
- インライン化の危険: patch したメソッドを、IL で呼出し先へ（patch していないゲームのメソッドを通って 3 段まで）たどり、そのメソッドの置き換えが
  作られた後の段階で patch した呼出し先が無いこと（たどるのは直接の呼出し〔call / callvirt / newobj〕だけで、デリゲートにするだけの参照〔ldftn〕は除く。ゲームは .NET 9.0.7 で、その JIT は例外処理〔try / catch / finally〕のあるメソッドをインライン化しないので、そのメソッドとその先はたどらない。virtual・abstract の呼出し先もたどらない。JIT がそれをインライン化するのは devirtualize できたときか、profile を要る guarded devirtualization のときだけで、Harmony の置き換え〔DynamicMethod〕では後者は起きない）。後から patch した小さなメソッドは、先に作られた置き換えにインライン化されていると patch を通らない
  （b692360 の実機確認で、`CardPlayStarted` → `Add` の `play` の started の行がこれで落ちた。旧コードではこの検査で落ちることを確かめた）
- 手番の段階は、`CombatTurnState` を取る private の多重定義だけに当てること（転送するだけの引数なしの版には当てない）

ゲームの版が上がって当て先の名前・引数・多重定義が変わると、ここで落ちる（文字列だけを見る `tools/event_inventory.py --check` では分からない）。

エントロピー醸造の契約も同じ DLL で検査する。本体の factory の候補・閾値、解放済みの空列と多重度、
所有者の切替と照合失敗、枠の条件付き可否と安定した対象 ID、4 通知の順と受け手を確かめる。
候補の繰り返し読み取りではベルト、正規モデルの動的変数、プレイヤーとランの RNG が変化しないことも照合する。
試験用の .NET プロセス内でモデルを組み立てるため、ネイティブの UI とローカライズは未初期化のまま使う。

三択ポーションの契約は、固定 DLL にある7段の override 宣言元の集合と、既存の介入源があるときの物理 slot 別の通知列を検査する。
同じ ID の瓶・別 owner・run/combat scope・pick/skip の欠測を照合する。二重読み取りと山札の並べ替えで、
通知列に山札の順が出ず、ゲームの RNG・履歴・ベルトが変化しないことも確かめる。

何でも屋+ の観測は、実手札・安定入力の条件と戦闘の試行識別子、両スコープの型・ID・件数を検査する。
重複、恒久デッキだけの札と付加、run に含まれる戦闘側のモデル、溶けたレリック、空瓶枠を照合する。
外部購読者と外部パワーの判定はそれぞれのコードを呼ぶ前に止まり、主敵不在では戦闘終了の hook を呼ばない。
片側だけの失敗は両列を未観測にし、既知の空列とは区別する。読み取りと山札の並べ替えを通じて、観測値とゲームの RNG・履歴・カウンタを照合する。

v0.114.0 のプレビューは実際の `DamageVar`・`CalculatedDamageVar` で、ブロック、HP 減少の上限と下限、超過ダメージの合計を照合する。
試算の前後で HP・ブロック・ログが変わらず、例外で試算が終わっても実際の HP 補正を記録できることを確かめる。
ワッピーの追加行は設定済みの HP・最大 HP を、強化プレビューは実際の複製と、強化時の例外が起きたときの候補行・wiki の応答を照合する。
戦闘個体の活動は本体の個体で本人・ワッピー・死亡処理を保留中の敵の参照と getter の実値を照合する。
