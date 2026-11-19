# 敵の HP の無限表示（`battle.enemies[].hp_display_is_infinite`）の契約試験

本番の `McpMod.EnemyHpDisplay.cs` / `McpMod.Warnings.cs` を直接リンクする。ゲーム DLL・Godot は不要。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/libexec/dotnet run --project tests/EnemyHpDisplayContract -c Release -p:BuildInParallel=false -p:UseSharedCompilation=false
```

`HpDisplay` は本体と同じ並びの列挙、`HpDisplayExtensions.IsInfinite` は呼び出しを数えて設定した答えを返す境界ダブル
（本体の分岐は写していない）。`Creature.HpDisplay` の getter は設定した値を返すか投げる。
builder が getter を 1 回読み、その値を本体の拡張に渡した答えをそのまま出すこと（拡張の答えが列挙と食い違っても従う）、
HP・最大 HP から作らないこと（999999999 でも `Normal` なら false）、死亡個体にも付けること、getter・拡張の失敗の null・理由
（既存の理由を保つ）・敵ごとの warning、wire の true / false / null、Markdown の ` ∞` / 何も付けない / ` ∞: null (理由)` と旧 state で何も補わないことを検査する。
`BuildEnemyState` が生死の分岐の前に全行へ付けること、`BuildEnemyList` の全行とマルチが同じ builder を通ること、
プレイヤー・味方の行に付けないこと、Markdown の HP の数字の直後の配線、棚卸しの分類表で `Creature.HpDisplay` が exposed で
備考に効果で読む箇所と見落としの理由があること、schema は本番ソースの文字列検査。

実機（調整役が v0.113.1 の検証用ランで確認する）:

1. 通常の敵は常に `hp_display_is_infinite: false`、Markdown の HP 行に ` ∞` が付かない。
2. 硬い殻（`HARDENED_SHELL_POWER`。蠢く群生体 `SKULKING_COLONY` が `AfterAddedToRoom` で自分自身に付ける）の敵に、その手番の被ダメージが上限に達するまで
   当てると `true`（`HardenedShellPower.AfterDamageReceived` が `InfiniteWithNumbers` にする）。HP の数字はそのまま出る。
   次の陣営の手番の開始（`BeforeSideTurnStart`）で `false` に戻る。上限に達しない手番は `false` のまま。
3. 滝の巨人（`WATERFALL_GIANT`）の HP を 0 にした後の死亡の継続（`SteamEruptionPower.AfterDeath` → `TriggerAboutToBlowState`。HP・最大 HP が 999999999、
   `combat_state.is_about_to_blow: true`）で `true`（`InfiniteWithoutNumbers`）。爆発（`ExplodeMove` の `CreatureCmd.Kill`）までそのまま。
4. ヘル・レイザー（`HELLRAISER_POWER`）を持ち、`hittable: true` の敵がすべて `hp_display_is_infinite: true` の手番にストライクを引き続けると、
   ヘル・レイザーの行の `infinite_auto_plays_this_turn` が 1 ずつ増え、9 に達した後に引いたストライクは自動プレイされない。
   どれか 1 体でも `false` の当てられる敵がいるドローでは 0 に戻る。
