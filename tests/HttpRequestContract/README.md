# HTTP 要求の契約検証

ゲーム・HTTP サーバーを起動せず、本番の純粋関数 `HttpRequestPolicy.Validate` をリンクする。
.NET 9 のみで実行でき、追加パッケージは不要。

```fish
dotnet run --project tests/HttpRequestContract -c Release
```

Origin の存在（空値・`null`・localhost も含む）、Origin のない要求での
`Sec-Fetch-Site` / `Sec-Fetch-Mode` / `Sec-Fetch-Dest` / `Referer` 各ヘッダーの存在
（空値・空白も含む）、GET/POST/OPTIONS/HEAD の拒否と判定の優先順位、
Host の許可範囲と設定ポート、JSON と charset パラメーターの許可、simple request の
Content-Type と類似した MIME 型の拒否を検査する。
存在フラグはテスト内の NameValueCollection から作る。検査対象は純粋関数のみであり、
実際の HttpListener のヘッダー処理・HandleRequest の配線・CORS 応答ヘッダー・
ルーティング・副作用の有無は実機で別途確認する。
