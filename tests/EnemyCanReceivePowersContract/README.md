# 敵にパワーを付けられるか（`battle.enemies[].can_receive_powers`）の契約試験

本番の `McpMod.EnemyCanReceivePowers.cs` / `McpMod.Warnings.cs` を直接リンクする。ゲーム DLL・Godot は不要。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/libexec/dotnet run --project tests/EnemyCanReceivePowersContract -c Release -p:BuildInParallel=false -p:UseSharedCompilation=false
```

`Creature.CanReceivePowers` と `Creature.IsHittable` は、設定した答えを返して読んだ回数を数える境界ダブル
（本体の `CombatState` の検査と `Hook.ShouldAllowHitting` は写していない）。HP・生死・`IsHittable` は独立に設定できる。

builder が getter を 1 回読んでその値をそのまま出すこと、`IsHittable` を読まないこと（死亡個体で hittable が false でも本体が true なら true、
生存で hittable と食い違っても本体の値）、生存中の拒否の false、読取失敗の null・理由（既存の理由を保ち、無ければ作る）・敵ごとの warning、
wire の true / false / null、Markdown の `| Receives powers: yes|no|null (理由)` と旧 state で何も補わないことを検査する。
`BuildEnemyState` が生死の分岐の前に、敵の種類・クイーンの有無の条件なしで全行へ付けること、`BuildEnemyList` の全行とマルチが同じ builder を通ること、
プレイヤー・味方の行に付けないこと、Markdown の配線、出来事の棚卸しの `computed:Hook.ShouldAllowHitting` が両方の出した先を引くこと、
状態の棚卸しの `Creature.CombatState` の備考、schema の履歴は本番ソース・分類表の文字列検査。

実機（調整役が v0.113.1 の検証用ランで確認する。手順は PR に書く）:

1. 通常の戦闘では、全ての敵の行で `can_receive_powers` が `hittable` と同じ値（生存中は true）。戦闘の進み方は変わらない。
2. クイーン（`QUEEN_BOSS`）の戦闘で、クイーン・トーチヘッド・アマルガムとも `can_receive_powers: true` の見込み（どちらも生存中で、
   `ShouldAllowHitting` を override するパワーを持たない）。
   「燃え上がれ」（`BURN_BRIGHT_FOR_ME_MOVE`）の後、アマルガムの筋力が付く（通常 +1・Dread +2）ことと突き合わせる。
3. 万足ムカデ（`DECIMILLIPEDE`）の体節を倒して復活を待つ間は `alive: false`・`hittable: false`・`can_receive_powers: false` の見込み
   （`ReattachPower.ShouldAllowHitting` は所有者が復活中〔`is_reviving`〕なら false）。食い違えばその局面を記録する。
