#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Last/Clear/Refine helpers for IdeFindChannel.Shape (soft-warn peel).</summary>
internal static partial class IdeFindChannel
{
    static object LastCard()
    {
        var raw = IdeSettingsStore.GetOrNull(LastKey);
        if (raw is not { Length: > 0 })
        {
            return new
            {
                ok = true,
                schema = SchemaVersion,
                role = "find",
                go = "find_desk",
                op = "last",
                idle = true,
                pulse = "find · last · idle",
                hint = "No prior cdp_search yet."
            };
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<object>(raw);
            return new
            {
                ok = true,
                schema = SchemaVersion,
                role = "find",
                go = "find_desk",
                op = "last",
                idle = false,
                pulse = "find · last",
                last = parsed,
                hint = "op=refine to replay with exclude[]; op=run with same query."
            };
        }
        catch
        {
            return new
            {
                ok = false,
                schema = SchemaVersion,
                role = "find",
                go = "find_desk",
                op = "last",
                error = "last_corrupt",
                hint = "op=clear then run again."
            };
        }
    }

    static object ClearCard()
    {
        IdeSettingsStore.Unset(LastKey);
        CideFindDeskLatch.Publish(
            active: false,
            pulse: "find_desk · idle · cleared",
            op: "clear",
            where: null,
            query: null,
            hitCount: 0);
        return new
        {
            ok = true,
            schema = SchemaVersion,
            role = "find",
            go = "find_desk",
            op = "clear",
            pulse = "find · cleared",
            hint = "Last query dropped."
        };
    }

    static Dictionary<string, JsonElement> MergeRefine(IReadOnlyDictionary<string, JsonElement> args)
    {
        var merged = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var lastRaw = IdeSettingsStore.GetOrNull(LastKey);
        if (lastRaw is { Length: > 0 })
        {
            try
            {
                using var doc = JsonDocument.Parse(lastRaw);
                var root = doc.RootElement;
                foreach (var name in new[] { "what", "where", "shape", "query", "path", "glob", "regex", "ignore_case", "type" })
                {
                    if (root.TryGetProperty(name, out var el))
                        merged[name] = el.Clone();
                }

                if (root.TryGetProperty("roots", out var rootsEl))
                    merged["roots"] = rootsEl.Clone();
            }
            catch
            {
                // ignore corrupt last
            }
        }

        foreach (var kv in args)
        {
            if (kv.Key is "op") continue;
            merged[kv.Key] = kv.Value;
        }

        if (!merged.ContainsKey("op"))
            merged["op"] = JsonSerializer.SerializeToElement("run");

        return merged;
    }

    static void SaveLast(
        string what,
        string where,
        string shape,
        string query,
        IReadOnlyDictionary<string, JsonElement> findArgs,
        string? pathNote)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["what"] = what,
            ["where"] = where,
            ["shape"] = shape,
            ["query"] = query,
            ["path_note"] = pathNote,
            ["at_utc"] = DateTime.UtcNow.ToString("O")
        };

        if (findArgs.TryGetValue("path", out var pathEl) && pathEl.ValueKind == JsonValueKind.String)
            payload["path"] = pathEl.GetString();
        if (findArgs.TryGetValue("glob", out var globEl) && globEl.ValueKind == JsonValueKind.String)
            payload["glob"] = globEl.GetString();
        if (findArgs.TryGetValue("paths", out var pathsEl))
            payload["roots"] = JsonSerializer.Deserialize<object>(pathsEl.GetRawText());

        IdeSettingsStore.Set(LastKey, JsonSerializer.Serialize(payload, Compact));
    }

    static void CopyPassthrough(
        IReadOnlyDictionary<string, JsonElement> src,
        Dictionary<string, JsonElement> dst)
    {
        foreach (var key in new[] { "glob", "g", "type", "filetype", "regex", "ignore_case", "peek", "max", "path", "search_in", "root" })
        {
            if (src.TryGetValue(key, out var el))
                dst[key] = el;
        }
    }
}
