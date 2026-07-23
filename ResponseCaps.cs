using System.Text.Json;

namespace CdpMcp;

/// <summary>
/// Shared response budget for MCP tool payloads (avoid host thrash / context blowups).
/// Shell already CapTail's streams; this caps whole tool text + diagnostics item lists.
/// </summary>
internal static class ResponseCaps
{
    public const int MaxToolChars = 48_000;
    public const int MaxDiagItems = 40;

    public static string CapText(string s, int max = MaxToolChars)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        var omitted = s.Length - max;
        return s[..max] + $"\n…[truncated {omitted} chars; ResponseCaps.MaxToolChars={max}]";
    }

    public static object CapDiagnostics(JsonElement el, int maxItems = MaxDiagItems)
    {
        if (el.ValueKind == JsonValueKind.Array)
        {
            var list = el.EnumerateArray().Take(maxItems)
                .Select(e => JsonSerializer.Deserialize<JsonElement>(e.GetRawText())).ToArray();
            return new
            {
                count = el.GetArrayLength(),
                truncated = el.GetArrayLength() > maxItems,
                items = list
            };
        }

        if (el.ValueKind != JsonValueKind.Object)
            return el;

        // Roslyn MCP shape: { ok, kind, summary, data: { items: [...] } }
        if (el.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("items", out var dataItems)
            && dataItems.ValueKind == JsonValueKind.Array)
        {
            var list = dataItems.EnumerateArray().Take(maxItems)
                .Select(e => JsonSerializer.Deserialize<JsonElement>(e.GetRawText())).ToArray();
            var shown = data.TryGetProperty("shown", out var sh) && sh.TryGetInt32(out var sn)
                ? sn
                : dataItems.GetArrayLength();
            var errors = data.TryGetProperty("error_count", out var ec) && ec.TryGetInt32(out var en)
                ? en
                : (int?)null;
            return new
            {
                ok = !el.TryGetProperty("ok", out var okEl) || okEl.ValueKind != JsonValueKind.False,
                kind = el.TryGetProperty("kind", out var k) ? k.GetString() : null,
                summary = el.TryGetProperty("summary", out var s) ? s.GetString() : null,
                data = new
                {
                    shown,
                    error_count = errors,
                    truncated = dataItems.GetArrayLength() > maxItems,
                    items = list
                }
            };
        }

        if (el.TryGetProperty("diagnostics", out var diags) && diags.ValueKind == JsonValueKind.Array)
        {
            var list = diags.EnumerateArray().Take(maxItems)
                .Select(e => JsonSerializer.Deserialize<JsonElement>(e.GetRawText())).ToArray();
            return new
            {
                count = diags.GetArrayLength(),
                truncated = diags.GetArrayLength() > maxItems,
                items = list
            };
        }

        return el;
    }
}
