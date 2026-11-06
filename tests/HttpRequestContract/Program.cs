using System.Collections.Specialized;
using STS2_MCP;

int checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    checks++;
}
void Expect(int? status, string method = "GET", string? host = "127.0.0.1:15526",
    bool hasOrigin = false, string? contentType = null, int port = 15526,
    bool hasFetchMetadata = false, bool hasReferer = false)
{
    var error = HttpRequestPolicy.Validate(method, host, hasOrigin, hasFetchMetadata, hasReferer, contentType, port);
    Check(error?.StatusCode == status, $"{method}, {host}, origin={hasOrigin}, fetch={hasFetchMetadata}, referer={hasReferer}, {contentType}: expected {status}, got {error}");
    if (error is { } rejected) Check(!string.IsNullOrWhiteSpace(rejected.Message), "error message");
}

// NameValueCollection から存在フラグを作り、純粋関数の判定を検査する。
// HttpListener の実際のヘッダー処理や HandleRequest の配線は検査しない。
foreach (string origin in new[] { "https://example.invalid", "null", "", "http://localhost:15526" })
{
    var headers = new NameValueCollection { ["Origin"] = origin };
    foreach (string method in new[] { "GET", "POST", "OPTIONS", "HEAD" })
        Expect(403, method, hasOrigin: headers["Origin"] != null, contentType: "application/json");
}
// Origin なしでも各ヘッダー単独で拒否する。空値・空白・same-origin も例外にしない。
foreach (var (name, value) in new[] {
    ("Sec-Fetch-Site", "cross-site"), ("Sec-Fetch-Site", "same-origin"),
    ("Sec-Fetch-Mode", "no-cors"), ("Sec-Fetch-Dest", "image"),
    ("Referer", "http://localhost:15526/"),
    ("Sec-Fetch-Site", ""), ("Sec-Fetch-Mode", ""), ("Sec-Fetch-Dest", ""), ("Referer", ""),
    ("Sec-Fetch-Site", " "), ("Sec-Fetch-Mode", " "), ("Sec-Fetch-Dest", " "), ("Referer", " "),
    ("sec-fetch-site", "cross-site"), ("sec-fetch-mode", "no-cors"),
    ("sec-fetch-dest", "image"), ("referer", "http://localhost/") })
{
    var headers = new NameValueCollection { [name] = value };
    foreach (string method in new[] { "GET", "POST", "OPTIONS", "HEAD" })
        Expect(403, method, contentType: "application/json",
            hasFetchMetadata: headers["Sec-Fetch-Site"] != null
                || headers["Sec-Fetch-Mode"] != null || headers["Sec-Fetch-Dest"] != null,
            hasReferer: headers["Referer"] != null);
}
// 同時存在と他の違反との優先順位も純粋関数で確認する。
foreach (bool hasOrigin in new[] { false, true })
foreach (bool hasFetchMetadata in new[] { false, true })
foreach (bool hasReferer in new[] { false, true })
{
    var error = HttpRequestPolicy.Validate("POST", "example.invalid:15526", hasOrigin,
        hasFetchMetadata, hasReferer, "text/plain", 15526);
    string expected = hasOrigin ? "Requests with an Origin header are not allowed"
        : hasFetchMetadata || hasReferer ? "Requests with Fetch Metadata or Referer headers are not allowed"
        : "Host must be 127.0.0.1 or localhost with the configured port";
    Check(error?.StatusCode == 403 && error?.Message == expected, "拒否の優先順位");
    Expect(hasOrigin || hasFetchMetadata || hasReferer ? 403 : 405, "OPTIONS",
        hasOrigin: hasOrigin, hasFetchMetadata: hasFetchMetadata, hasReferer: hasReferer);
}
foreach (string host in new[] { "127.0.0.1:15526", "localhost:15526", "LOCALHOST:15526" })
{
    Expect(null, host: host);
    foreach (string type in new[] { "application/json", "application/json; charset=utf-8", "APPLICATION/JSON", "application/json ;charset=utf-8" })
        Expect(null, "POST", host, contentType: type);
    Expect(405, "OPTIONS", host, contentType: "application/json");
}
foreach (string? host in new string?[] { null, "", "localhost", "127.0.0.1", "localhost:80",
    "localhost:15527", "127.0.0.1:015526", "localhost.:15526", "127.1:15526", "2130706433:15526",
    "[::1]:15526", "example.invalid:15526", "localhost.example.invalid:15526",
    "localhost:15526@example.invalid", "localhost:15526,example.invalid:15526", " localhost:15526" })
{
    Expect(403, host: host);
    Expect(403, "POST", host, contentType: "application/json");
}
foreach (string? type in new string?[] { null, "", "text/plain", "application/x-www-form-urlencoded",
    "multipart/form-data; boundary=test", "application/jsonp", "application/json-patch+json",
    "application/json,text/plain", "text/plain; application/json" })
{
    Expect(415, "POST", contentType: type);
    Expect(null, contentType: type); // GET does not require Content-Type.
}
Expect(403, "POST", "example.invalid:15526", true, "text/plain");
Expect(405, "OPTIONS"); // No Origin and no Content-Type still cannot preflight.
Expect(null, host: "localhost:23456", port: 23456);
Expect(null, "POST", "127.0.0.1:23456", contentType: "application/json", port: 23456);
Expect(403, host: "localhost:15526", port: 23456);
Console.WriteLine($"HTTP request contract: {checks} checks passed");
