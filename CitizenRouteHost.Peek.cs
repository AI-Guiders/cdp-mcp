#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent file_peek|eyes|cdp_peek — Meta cdp_peek; ship text into peer context.</summary>
internal static partial class CitizenRouteHost
{
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? FilePeekDispatchOverride { get; set; }

    static Applied RunFilePeek(CitizenIntentRouter.Route route)
    {
        var args = BuildFilePeekArgs(route);

        try
        {
            string json;
            if (FilePeekDispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                json = meta(CdpPeekChannel.ToolName, args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadPeekOk(json);
            var pulse = TryReadPeekPulse(json, route.Path);
            var ship = ok ? TryReadPeekShip(json) : null;
            if (ship is { Length: > 0 }
                && pulse is not null
                && pulse.IndexOf("ship=", StringComparison.Ordinal) < 0)
                pulse = TruncPulse(pulse + " ship=" + ship.Length);

            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "file_peek",
                Go: CdpPeekChannel.ToolName,
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "file_peek_failed"),
                Ship: ship);
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "file_peek",
                Go: CdpPeekChannel.ToolName,
                Path: route.Path,
                Reason: "file_peek_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "file_peek",
                Go: CdpPeekChannel.ToolName,
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildFilePeekArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var raw = route.Raw;

        PutIfPresent(args, "path", route.Path
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "path")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "file"));

        PutIfPresent(args, "anchor",
            route.Detail is { Length: > 0 } d && d.Contains('[', StringComparison.Ordinal)
                ? d
                : CitizenIntentRouter.ExtractKeyedValue(raw, "anchor")
                  ?? CitizenIntentRouter.ExtractKeyedValue(raw, "at"));

        if (route.Detail is { Length: > 0 } off && !off.Contains('[', StringComparison.Ordinal))
            PutIfPresent(args, "offset", off);
        else
            PutIfPresent(args, "offset",
                CitizenIntentRouter.ExtractKeyedValue(raw, "offset")
                ?? CitizenIntentRouter.ExtractKeyedValue(raw, "start_line"));

        PutIfPresent(args, "limit",
            route.NewString
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "limit")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "lines"));

        PutIfPresent(args, "query",
            route.Cmd
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "query")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "pattern")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "q"));

        PutIfPresent(args, "glob", CitizenIntentRouter.ExtractKeyedValue(raw, "glob"));
        PutIfPresent(args, "scope", CitizenIntentRouter.ExtractKeyedValue(raw, "scope"));
        PutIfPresent(args, "pad", CitizenIntentRouter.ExtractKeyedValue(raw, "pad"));
        return args;
    }

    static bool TryReadPeekOk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("ok", out var okEl)
                && okEl.ValueKind != JsonValueKind.False;
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadPeekPulse(string json, string? path)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("rel", out var rel) && rel.ValueKind == JsonValueKind.String)
                path = rel.GetString();
            if (root.TryGetProperty("mode", out var mode) && mode.GetString() is { } m
                && m is "image" or "find" or "batch")
            {
                return TruncPulse($"peek {m} ok");
            }

            if (root.TryGetProperty("returned", out var ret) && ret.TryGetInt32(out var n))
                return TruncPulse($"peek {path ?? "file"} lines={n}");
            return TruncPulse($"peek {path ?? "file"} ok");
        }
        catch
        {
            return TruncPulse("peek");
        }
    }

    internal const int FilePeekShipMaxChars = 48_000;

    static string? TryReadPeekShip(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
                && text.GetString() is { Length: > 0 } body)
                return TruncFilePeekShip(body);

            if (root.TryGetProperty("mode", out var mode) && mode.GetString() == "batch"
                && root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var f in files.EnumerateArray())
                {
                    if (f.TryGetProperty("text", out var ft) && ft.ValueKind == JsonValueKind.String
                        && ft.GetString() is { Length: > 0 } chunk)
                    {
                        if (f.TryGetProperty("rel", out var rel) && rel.GetString() is { Length: > 0 } r)
                            sb.Append("=== ").Append(r).Append(" ===\n");
                        sb.AppendLine(chunk);
                    }
                }

                var batch = sb.ToString().TrimEnd();
                return batch.Length > 0 ? TruncFilePeekShip(batch) : null;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    static string TruncFilePeekShip(string ship)
    {
        if (ship.Length <= FilePeekShipMaxChars)
            return ship;
        return ship[..FilePeekShipMaxChars] + "\n…[ship truncated chars=" + ship.Length + "]";
    }
}
