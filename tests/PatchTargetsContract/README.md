# patch の当て先の契約試験（ゲームの DLL で解決する）

ゲームがインストールされている環境だけで回る（`sts2.dll` / `0Harmony.dll` / `GodotSharp.dll` を参照する）。ゲームは起動しない。

```fish
env DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec /opt/homebrew/opt/dotnet@9/libexec/dotnet run --project tests/PatchTargetsContract -c Release -p:BuildInParallel=false -p:UseSharedCompilation=false
```

既定の `STS2GameDir` は macOS の Steam の場所。違うなら `-p:STS2GameDir=...` を渡す。

`Initialize` と同じ install のメソッド（`InstallChoiceSourceTracking`・`InstallAutoPlayReservationTracking`・
`InstallCombatLogTracking`・`InstallDecisionContextTracking`）を本物の `sts2.dll` に対して呼び、次を確かめる。

- すべての群が当たったこと（各 `*Error` が null、`CombatLogStore.InstallErrors` が空）
- Harmony の ID ごとの patch の数（combat_log 101、decision_context 9、choice_source 8）
- 手番の段階は、`CombatTurnState` を取る private の多重定義だけに当てること（転送するだけの引数なしの版には当てない）

ゲームの版が上がって当て先の名前・引数・多重定義が変わると、ここで落ちる（文字列だけを見る `tools/event_inventory.py --check` では分からない）。
