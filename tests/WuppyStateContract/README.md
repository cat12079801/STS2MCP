# ワッピーの公開契約

ゲームを起動せず、ペット builder と JSON のシリアライズを検査する。生存・HP 0・登録前・読取失敗、オスティとの同居、
協力プレイでの所有者の分離とレリック参照の照合を確認する。表示用の `status` に無い ID が `power_ids` に残ることと、
戦闘ログが同じ `Creature.CombatId` を読んでいることも検査する。ゲーム本体での召喚・被弾の実行は実機採取の対象。

```fish
dotnet run --project tests/WuppyStateContract -c Release
```
