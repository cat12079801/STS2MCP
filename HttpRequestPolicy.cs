using System;
using System.Globalization;

namespace STS2_MCP;

/// <summary>Pure HTTP admission checks shared by all routes, without game dependencies.</summary>
internal static class HttpRequestPolicy
{
    internal static (int StatusCode, string Message)? Validate(
        string method, string? host, bool hasOrigin, string? contentType, int port)
    {
        // Presence matters: "null" (file://), empty, and same-origin values all fail closed.
        if (hasOrigin)
            return (403, "Requests with an Origin header are not allowed");

        // Compare the raw Host against the configured listener port, not request.Url:
        // parsing/normalizing a URL can hide an untrusted authority (DNS rebinding).
        string portText = port.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(host, "127.0.0.1:" + portText, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(host, "localhost:" + portText, StringComparison.OrdinalIgnoreCase))
            return (403, "Host must be 127.0.0.1 or localhost with the configured port");

        if (method == "OPTIONS")
            return (405, "Method not allowed");

        // Accept JSON parameters (e.g. charset=utf-8), but not lookalike media types.
        if (method == "POST"
            && !string.Equals(contentType?.Split(';', 2)[0].Trim(), "application/json",
                StringComparison.OrdinalIgnoreCase))
            return (415, "POST requests require Content-Type: application/json");

        return null;
    }
}
