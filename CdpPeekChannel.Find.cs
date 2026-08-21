#nullable enable
using System.Text.Json;
using Cdp.Core;
using Cdp.ScriptableIde;

namespace CdpMcp;

internal static partial class CdpPeekChannel
{
    static object FindAndPeek(
        SessionContext session,
        LanguageRegistry langs,
        DocumentBufferStore? store,
        IReadOnlyDictionary<string, JsonElement> args,
        string query)
    {
        if (store is null)
        {
            return Fail("no_store", "Internal: document store not bound for find+peek.",
                "Call via MCP host; query= mode needs rg + session.");
        }

        var findArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var kv in args)
        {
            if (kv.Key is "query" or "pattern" or "q")
                continue;
            findArgs[kv.Key] = kv.Value;
        }

        findArgs["query"] = JsonSerializer.SerializeToElement(query);
        findArgs["peek"] = JsonSerializer.SerializeToElement(false);
        if (!findArgs.ContainsKey("scope"))
            findArgs["scope"] = JsonSerializer.SerializeToElement("project");

        var max = Math.Clamp(IntOr(args, "max") ?? IntOr(args, "head_limit") ?? 5, 1, 20);
        findArgs["max"] = JsonSerializer.SerializeToElement(max);

        var json = FindInFiles.Dispatch(store, session, findArgs, all: true);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var okEl) || !okEl.GetBoolean())
        {
            return new
            {
                schema = SchemaVersion,
                ok = false,
                tool = ToolName,
                mode = "find",
                find = JsonSerializer.Deserialize<object>(json),
                hint = "Fix query/scope or cdp_open first."
            };
        }

        var pad = Math.Clamp(IntOr(args, "pad") ?? 3, 0, 40);
        var windows = new List<object>();
        if (root.TryGetProperty("hits", out var hits) && hits.ValueKind == JsonValueKind.Array)
        {
            foreach (var hit in hits.EnumerateArray())
            {
                if (windows.Count >= max)
                    break;

                var abs = hit.TryGetProperty("path", out var pEl) ? pEl.GetString() : null;
                var line = hit.TryGetProperty("line", out var lEl) && lEl.TryGetInt32(out var ln) ? ln : 0;
                var anchor = hit.TryGetProperty("anchor", out var aEl) ? aEl.GetString() : null;
                if (abs is null || line <= 0)
                    continue;

                var peekArgs = new Dictionary<string, JsonElement>
                {
                    ["anchor"] = JsonSerializer.SerializeToElement(anchor ?? $"[F:{Rel(session.ProjectRoot, abs)};L:{line};]"),
                    ["pad"] = JsonSerializer.SerializeToElement(pad),
                    ["structured_only"] = JsonSerializer.SerializeToElement(true)
                };

                var land = PeekFile(session, abs, peekArgs, bindNote: null);
                windows.Add(new
                {
                    hit = new
                    {
                        anchor,
                        path = abs,
                        rel = Rel(session.ProjectRoot, abs),
                        line,
                        preview = hit.TryGetProperty("preview", out var prev) ? prev.GetString() : null
                    },
                    peek = land
                });
            }
        }

        return new
        {
            schema = SchemaVersion,
            ok = true,
            tool = ToolName,
            mode = "find",
            query,
            scope = root.TryGetProperty("scope", out var sc) ? sc.GetString() : null,
            count = windows.Count,
            windows,
            hint = "Pick anchor → cdp_edit_sniper or cdp_buffer op=edit. More hits: raise max= or cdp_search shape=list."
        };
    }
}
