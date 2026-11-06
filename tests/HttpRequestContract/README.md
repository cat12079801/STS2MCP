# HTTP 要求の契約検証

ゲーム・HTTP サーバーを起動せず、本番の純粋関数 `HttpRequestPolicy.Validate` をリンクする。
.NET 9 のみで実行でき、追加パッケージは不要。

```fish
dotnet run --project tests/HttpRequestContract -c Release
```

Origin の存在（空値・`null`・localhost も含む）、GET/POST/OPTIONS の拒否、
Host の許可範囲と設定ポート、JSON と charset パラメーターの許可、simple request の
Content-Type と類似した MIME 型の拒否を検査する。
実際の HttpListener のヘッダー処理・ルーティング・副作用の有無は実機で別途確認する。
