# 敵の戦闘内部状態の契約試験

追加パッケージ・ゲームへの接続は不要。fish での実行例:

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/bin/dotnet run --project tests/EnemyCombatStateContract -c Release -p:BuildInParallel=false -p:UseSharedCompilation=false
```

本番の `McpMod.EnemyCombatState.cs`（builder と Markdown）・内部状態の読取 helper・warnings をリンクする。
ゲームの型は滝の巨人（`WaterfallGiant`）の private フィールド・getter の名前と型と、`RunManager.IsInProgress` だけを写し、
move の実行・`TriggerAboutToBlowState`・乱数・Ascension の判定は用意しない。値は試験がフィールドへ直接入れる。

- `is_about_to_blow` / `steam_eruption_damage` の false・0・true・正の値を実値として出す。理由・警告なし。
- 通常段階の 7 値（`current_pressure_gun_damage`・`steam_boost_amount`・`pressurize_amount`・`stomp_damage`・
  `ram_damage`・`pressure_up_damage`・`siphon_heal`）を、A7・A9＋Dread 相当・プレッシャーガン 2 回後・0 の値で、そのまま int で出す。
  getter は 6 個を 1 回ずつ読む（`current_pressure_gun_damage` はフィールド）。
- ラン外（`RunManager.IsInProgress=false`）は getter を呼ばず、getter の 6 項目を null と理由・warnings。フィールドの値は出す。
- getter の例外・負の値は、その項目だけ null と理由（0 に丸めない）。欠落・型違いの getter も null と理由。
- 読取がフィールドを変更しないこと、前回の snapshot の独立、本番の wire 設定で bool / int のまま送ること。
- Markdown の `**Combat state:**` 行（false=no、0 を表示）と、欠落・空の省略。
- Monster が無い敵・別の敵（同名フィールドを持つものを含む）には `combat_state` を付けず、警告も出さない。
- 読取失敗の形（null・`unavailable_reasons`・個体と項目を識別する warnings・Markdown の理由）は、
  共通の `ReadInternalStateValue` / `ReadDeclaredStateField` に欠落フィールド・型違いを与えて確認する
  （本番の `WaterfallGiant` は sealed で、同じ型に壊れたフィールドを持たせた二重は作れないため）。
- schema 6（`cat12079801/sts2#1018`）の行動の入力の敵 11 種の private フィールド（Axebot は getter）を値のまま
  （false・0・負も実値）、許可リストの項目だけ出す。ファブリケーターの `_lastSpawned` は、読めた null（まだ召喚なし、
  `last_spawned_readable=true`）・召喚したモデルの ID・ID が読めない失敗（`last_spawned_readable=false`・理由）を区別。
- `cat12079801/sts2#1065`（schema 1.1.0）の台詞の印 2 種、傭兵グレムリン（`GremlinMerc._hasSpoken` → `has_spoken`）と
  キンの司祭（`KinPriest._speechUsed` → `speech_used`）を、false・true とも bool の実値で出す（wire でも false を省略しない）。
  読取はフィールドだけで、値を変えず、`AssertMutable` の setter を持つ property（`HasSpoken` / `SpeechUsed`）を呼ばない。
  読取失敗（欠落フィールド・型違い）は同じ型・同じ項目名で null・理由・warnings・Markdown の `null (理由)` にし、false で補わない。
- `battle.enemy_move_context`（本番の `McpMod.EnemyMoveContext.cs`）: DLL hash・モード・Dread・`HasAscension` が true の段階（列挙順）・
  人数・遭遇の ID・`Slots` の順と `Enemies` 全体の占有個体。ラン外は Ascension・Dread・Slots を読まず理由付きの null、
  遭遇なしは `[]`、遭遇・Slots・占有個体の読取失敗は項目ごとの null と理由（途中までの列を返さない）。Markdown の 1 行。
- 敵の行の `slot_name`（実値・スロット無しの null・読取失敗）と Markdown の `| Slot:`。
- 全敵行への配線（生死で絞らない）・協力プレイの共通 builder・Markdown の配線・整数の schema_version 12 と semver の MAJOR 1 をソース文字列で確認（版の値そのものは `V0113CompatContract`）。

根拠は v0.113.1（`sts2-a671e9ef`）の `WaterfallGiant._isAboutToBlow` / `_steamEruptionDamage`、
`WaterfallGiant.TriggerAboutToBlowState` / `AboutToBlowMove` / `ExplodeMove`、`SteamEruptionPower.AfterDeath`、
`WaterfallGiant._currentPressureGunDamage` / `AfterAddedToRoom` / `PressureGunMove` / `SteamBoostAmount` / `PressurizeAmount` /
`StompDamage` / `RamDamage` / `PressureUpDamage` / `SiphonHeal`、`AscensionHelper.GetValueIfAscension` / `IsDread`、
`RunManager.HasAscension` / `IsDread` / `IsInProgress`、`GremlinMerc._hasSpoken` / `HasSpoken` / `GimmeMove`、
`KinPriest._speechUsed` / `SpeechUsed` / `RitualMove`。
実機での段階ごとの値の確認は取得担当へ引き渡す。
