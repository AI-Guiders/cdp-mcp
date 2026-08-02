#nullable enable
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace CdpMcp;

internal sealed partial class McpOutletHabitat
{
    private static async Task<IReadOnlyList<ToolCard>> ListToolCardsAsync(McpClient client, CancellationToken ct)
    {
        return (await client.ListToolsAsync((RequestOptions? )null, ct).ConfigureAwait(continueOnCapturedContext: false)).Select((McpClientTool t) => new ToolCard(t.Name, Truncate(t.Description, 240))).OrderBy<ToolCard, string>((ToolCard t) => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task SafeDisposeAsync(McpClient client)
    {
        try
        {
            await client.DisposeAsync().ConfigureAwait(continueOnCapturedContext: false);
        }
        catch
        {
        }
    }

    private static string Fail(string error, string? server, string? hint)
    {
        return JsonSerializer.Serialize(new { schema = "mcp_outlet/v1", ok = false, error = error, server = server, hint = hint }, Pretty);
    }

    private static string SanitizeId(string id)
    {
        string text = id.Trim();
        if (text.Length == 0)
        {
            return "mcp";
        }

        Span<char> span = stackalloc char[Math.Min(text.Length, 64)];
        int num = 0;
        string text2 = text;
        foreach (char c in text2)
        {
            if (num >= span.Length)
            {
                break;
            }

            int index = num++;
            bool flag = char.IsLetterOrDigit(c);
            if (!flag)
            {
                bool flag2 = ((c == '-' || c == '_') ? true : false);
                flag = flag2;
            }

            span[index] = (flag ? c : '_');
        }

        return new string (span.Slice(0, num));
    }

    private static string? Opt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static int? OptInt(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var value2))
        {
            return value2;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var result))
        {
            return result;
        }

        return null;
    }

    private static string[]? ReadStringArray(IReadOnlyDictionary<string, JsonElement> args, string key)
    {
        if (!args.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return (
            from e in value.EnumerateArray()
            where e.ValueKind == JsonValueKind.String
            select e.GetString()into s
                where s.Length > 0
                select s).ToArray();
    }

    private static bool IsReserved(string key)
    {
        switch (key)
        {
            case "id":
            case "op":
            case "filter":
            case "preset":
            case "server":
            case "tool":
            case "name":
            case "args":
            case "take":
            case "link":
            case "command":
            case "q":
            case "uri":
                return true;
            default:
                return false;
        }
    }

    private static object? JsonElementToObject(JsonElement el)
    {
        long value;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out value) ? ((double)value) : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => el.EnumerateArray().Select(JsonElementToObject).ToList(),
            JsonValueKind.Object => el.EnumerateObject().ToDictionary<JsonProperty, string, object>((JsonProperty p) => p.Name, (JsonProperty p) => JsonElementToObject(p.Value), StringComparer.Ordinal),
            _ => el.GetRawText(),
        };
    }

    private static string? Truncate(string? s, int max)
    {
        if (!string.IsNullOrEmpty(s))
        {
            if (s.Length > max)
            {
                return s.Substring(0, max) + "…";
            }

            return s;
        }

        return s;
    }
}