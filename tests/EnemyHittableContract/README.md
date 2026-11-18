# 敵に当てられるか（`battle.enemies[].hittable`）の契約試験

本番の `McpMod.EnemyHittable.cs` / `McpMod.Warnings.cs` を直接リンクする。ゲーム DLL・Godot は不要。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/libexec/dotnet run --project tests/EnemyHittableContract -c Release -p:BuildInParallel=false -p:UseSharedCompilation=false
```

`Creature.IsHittable` は設定した答えを返す境界ダブル（本体の `IsDead` と `Hook.ShouldAllowHitting` は写していない）。
HP・alive と独立に設定して、builder が getter を 1 回読んだ値をそのまま出し、HP や alive から作らないこと
（生存で false、死亡で true の組み合わせも本体の答えのまま）、読取失敗の null・理由（既存の理由を保つ）・敵ごとの warning、
wire の true / false / null、Markdown の yes / no / null（理由）と旧 state で何も補わないことを検査する。
`BuildEnemyState` が生死の分岐の前に全行へ付けること、`BuildEnemyList` の全行とマルチが同じ builder を通ること、
Markdown の配線、schema 7 は本番ソースの文字列検査。

実機（調整役が v0.113.1 の検証用ランで確認する）:

1. 通常の敵・ミニオン（`MINION_POWER`）は生存中 `hittable: true`（`MinionPower` は `ShouldAllowHitting` を override しない）。
2. 万足ムカデ（`DECIMILLIPEDE_SEGMENT`）の体節を倒して `alive: false` で残る間は `hittable: false`（`IsDead`。`ReattachPower.AfterDeath` が `isReviving` を立てる）。
   再結合の後（`ReattachPower.DoReattach` が `isReviving` を下ろしてから `CreatureCmd.Heal`）は `true` に戻る。
3. パラフライト・歯の目（`IllusionPower`）、被検体（`AdaptablePower`）も、死亡から復活までは `false`、復活後は `true`
   （`IllusionPower.ReviveMove`・`AdaptablePower` も `isReviving` を下ろしてから回復する）。
   落ち着いた state では、生存中に復活中の印が立っている局面は無い見込みで、`hittable` は生存と一致するはず。食い違えばその局面を記録する。
4. ジャガーノートなどのランダムな対象の候補が、`hittable: true` の敵の集合と一致すること（`CombatState.HittableEnemies`）。
