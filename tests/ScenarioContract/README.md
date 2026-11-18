# 検証用の採取の契約（`cat12079801/sts2#1037`）

`dotnet run --project tests/ScenarioContract` で実行する。追加パッケージは不要。
本体の `McpMod.ScenarioGuard.cs`（ゲームの型を使わない判定）をリンクし、次を検証する。

- `scenario_capabilities`: `setup_guard: 1`・`selection_uid: 1`・`potion_availability: 0`（int。0 は未実装）
- 戦闘外の準備の判定（`scenario_setup_guard`）: 許すのは MapRoom の `map`・Neow の EventRoom の `event`・RestSiteRoom の `rest_site` だけ。
  ランなし・読取不能（null）・ラン終了・戦闘中・戦闘開始中・選択や報酬などの前面画面・戦闘に重なった map・別の部屋に重なった map・
  Neow 以外のイベント・部屋なし・解決中を、それぞれの `reason` で拒否する。キーの並びと、許可時の `reason: null` が JSON に残ること
- `dev_console` の `scenario_setup_guard` の受理（`{version: 1, expected_screen: map|event|rest_site}` だけ）と、未知の版・画面・キー・型の拒否。
  実行直前の判定が GET のときから動いた（別の画面・戦闘）場合の拒否
- `expected_uid` の読み取り（無ければ照合しない、空・非文字列は拒否）と照合
- `hand_select` の index が `NPlayerHand.ActiveHolders` の位置であること（札の無い holder は行を出さず、後ろの index を詰めない）。
  index と uid が同じ holder を指すこと、空の holder・範囲外・同名の別個体・並べ替え・uid が読めない場合の拒否。
  `NPlayerHand.OnHolderPressed` が押下を無視する条件（覗き見中・オーバーレイあり・戦闘外。読めない値も含む）は、expected_uid の有無によらず理由付きで拒否する

本体の state・action・Markdown・MCP サーバーへの配線（GET が passive のときだけ判定を足す、実行直前の判定が `ProcessCommand` より前、
`_selectedCards`・`_prefs`・覗き見・オーバーレイの数・戦闘の進行・表示中の確定ボタンの読取、行の `enabled` と action が同じ判定を使うこと、`combat_select_card` / `select_card` の照合が押下・覆いを外す操作より前、schema 10）は
ゲームの型が要るのでソース文字列で検査する。grid の expected_uid は `CardSelectContract`、`dev_console` の実行直前の判定と空入力は `DevConsoleContract` で動かして確かめる。

実機（ゲームの部屋・戦闘の実際の遷移、手札の holder の実際の並び、覗き見中の押下）は別途確認する。
