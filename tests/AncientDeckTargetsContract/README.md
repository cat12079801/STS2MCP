# エンシェント提示のデッキ対象契約

対象6種の読取処理をゲームの型のスタブで動かし、JSON のキーと値、Markdown の表示を検査する。
同名札の位置、強化済み・永劫・既存付加、読取失敗、空デッキ、連続取得をカバーする。
単独・マルチのイベントビルダーと表示中ボタンの絞り込みはソースで配線を検査する。

```fish
dotnet run --project tests/AncientDeckTargetsContract -c Release
```
