using System.Collections.Specialized;
using STS2_MCP;

int checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    checks++;
}
void Expect(int? status, string method = "GET", string? host = "127.0.0.1:15526",
    bool hasOrigin = false, string? contentType = null, int port = 15526)
{
    var error = HttpRequestPolicy.Validate(method, host, hasOrigin, contentType, port);
    Check(error?.StatusCode == status, $"{method}, {host}, origin={hasOrigin}, {contentType}: expected {status}, got {error}");
    if (error is { } rejected) Check(!string.IsNullOrWhiteSpace(rejected.Message), "error message");
}

// Header presence, including an empty value, must be preserved by the HTTP boundary.
foreach (string origin in new[] { "https://example.invalid", "null", "", "http://localhost:15526" })
{
    var headers = new NameValueCollection { ["Origin"] = origin };
    foreach (string method in new[] { "GET", "POST", "OPTIONS", "HEAD" })
        Expect(403, method, hasOrigin: headers["Origin"] != null, contentType: "application/json");
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
