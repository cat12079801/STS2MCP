# 敵の攻撃の意図の契約試験

追加パッケージ・ゲームへの接続は不要。fish での実行例:

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/bin/dotnet run --project tests/EnemyAttackIntentContract -c Release -p:BuildInParallel=false -p:UseSharedCompilation=false
```

本番の `McpMod.EnemyAttackIntent.cs`（builder と Markdown）・内部状態の読取 helper・warnings をリンクする。
ゲームの型は `AttackIntent`（`DamageCalc`・`Repeats`・`GetSingleDamage`）・`SingleAttackIntent`・`MultiAttackIntent`・
`DeathBlowIntent`・`Creature.CombatState`・`LocalContext.GetMe` の公開メンバーの形だけを写す。`GetSingleDamage` は本体と同じ並び
（`DamageCalc` → `LocalContext.GetMe` → `Hook.ModifyDamage` → `Math.Max(0, (int)num)`）で、`Hook.ModifyDamage` だけを試験の補正関数に置き換える。

- 素 20・筋力 4 → 6 で、脆弱なら解決値 36 → 39、脱力なら 18 → 19 のあいだ `base_damage` は `"20"` のまま。素 21 の脱力でも 18 になる（表示から素値を戻せない）。
- decimal の素値を丸めずに invariant culture の文字列で出す（de-DE の文化でも `12.5`）。delegate・`_repeatCalc` は現在の値を読む。回数 0 も実値。
- DeathBlow にも付け、攻撃以外の意図には付けない（警告も出さない）。
- ローカルのプレイヤーが居ない（`Creature.CombatState` が null・`LocalContext.GetMe` が null・例外）ときは `resolved_damage` だけ null。補正前の値を解決値として出さない。
- `DamageCalc` が null・例外のときは `base_damage` と `resolved_damage` が null、`Repeats` が負・例外のときは `hits` が null。理由・個体と意図の位置を識別する warnings・Markdown の表示・wire の null。
- 読取で `DamageCalc` を差し替えないこと、前回の snapshot の独立。
- 意図ごとの配線（ラベルの try より前）・協力プレイの共通 builder・Markdown の配線・現行の schema 6 をソース文字列で確認。

根拠は v0.113.1（`sts2-a671e9ef`）の `AttackIntent.DamageCalc` / `Repeats` / `GetSingleDamage`、`SingleAttackIntent`・`MultiAttackIntent`・`DeathBlowIntent` の
コンストラクタと `Repeats`、`Hook.ModifyDamage` / `ModifyDamageInternal`、`StrengthPower.ModifyDamageAdditive`、
`VulnerablePower` / `WeakPower.ModifyDamageMultiplicative`、`LocalContext.GetMe(ICombatState?)`。実機での値の確認は取得担当へ引き渡す。
