#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Go-verb dispatch peel — soft-organ tool call + locus path inject.</summary>
internal static partial class IdeCockpit
{
    static async Task<object> DispatchGoAsync(
        string verb,
        IReadOnlyDictionary<string, JsonElement> cockpitArgs,
        BufferSnap buffer,
        string? focusId,
        Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>> dispatch,
        CancellationToken cancellationToken)
    {
        var detail = (OptString(cockpitArgs, "go_detail") ?? "pulse").Trim().ToLowerInvariant();
        if (detail is not ("pulse" or "full"))
            detail = "pulse";

        if (verb.Equals("cockpit", StringComparison.OrdinalIgnoreCase)
            || verb.Equals("cdp_cockpit", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                ok = false,
                go = verb,
                error = "refuse_self",
                hint = "go= routes to organs; use mfd=/locus= for cockpit itself."
            };
        }

        if (!GoMap.TryGetValue(verb, out var map))
        {
            return new
            {
                ok = false,
                go = verb,
                error = "unknown_go",
                hint = "Pick from go_verbs[] or next[].go / locus.go."
            };
        }

        var callArgs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (map.Defaults is not null)
        {
            foreach (var kv in map.Defaults)
                callArgs[kv.Key] = kv.Value;
        }

        if (cockpitArgs.TryGetValue("go_args", out var goArgs) && goArgs.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in goArgs.EnumerateObject())
                callArgs[p.Name] = p.Value.Clone();
        }

        InjectBufferPathFromLocus(verb, callArgs, buffer, focusId);

        try
        {
            var raw = await dispatch(map.Tool, callArgs, cancellationToken).ConfigureAwait(false);
            if (detail == "full")
            {
                var capped = CapGoResult(raw, GoResultCapChars);
                object? parsed = TryParseJson(capped.Text);
                return new
                {
                    ok = true,
                    go = verb,
                    tool = map.Tool,
                    detail = "full",
                    truncated = capped.Truncated,
                    result = parsed
                };
            }

            var pulse = PulseFromOrgan(raw);
            return new
            {
                ok = pulse.Ok,
                go = verb,
                tool = map.Tool,
                detail = "pulse",
                pulse = pulse.Line,
                schema = pulse.Schema,
                next = pulse.Next,
                hint = pulse.Hint ?? "go_detail=full for organ dump; or call organ tool directly."
            };
        }
        catch (Exception ex)
        {
            return new
            {
                ok = false,
                go = verb,
                tool = map.Tool,
                detail,
                error = ex.Message
            };
        }
    }

    /// <summary>
    /// Desk comfort: <c>locus=buffer:doc-N</c> + <c>go=reload|keep_disk|disk_peek</c>
    /// scopes to that file when <c>path=</c> / <c>go_args.path</c> omitted.
    /// </summary>
    static void InjectBufferPathFromLocus(
        string verb,
        Dictionary<string, JsonElement> callArgs,
        BufferSnap buffer,
        string? focusId)
    {
        if (verb is not ("reload" or "keep_disk" or "disk_peek"))
            return;
        if (callArgs.TryGetValue("path", out var pathEl)
            && pathEl.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(pathEl.GetString()))
            return;
        if (focusId is not { Length: > 0 }
            || !focusId.StartsWith("buffer:", StringComparison.OrdinalIgnoreCase)
            || focusId.Equals("buffer:none", StringComparison.OrdinalIgnoreCase))
            return;

        var docId = focusId["buffer:".Length..];
        var doc = buffer.Docs.FirstOrDefault(d =>
            string.Equals(d.DocId, docId, StringComparison.OrdinalIgnoreCase));
        if (doc is null || string.IsNullOrWhiteSpace(doc.Path) || doc.Path == "?")
            return;

        callArgs["path"] = JsonSerializer.SerializeToElement(doc.Path);
    }
}
