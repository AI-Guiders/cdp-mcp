#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent man — sync MetaDispatch cdp_man; place man organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake man text/JSON; live uses MetaDispatchResolver("cdp_man", …).</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? ManDispatchOverride { get; set; }

    static Applied RunMan(CitizenIntentRouter.Route route)
    {
        var args = BuildManArgs(route);

        try
        {
            string body;
            if (ManDispatchOverride is { } ov)
            {
                body = ov(args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                body = meta("cdp_man", args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadManOk(body);
            var pulse = TryReadManPulse(body, route.Tool);
            var seat = IdeDeskSeats.PlaceOrgan("man");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "man",
                Seat: seat,
                Go: "man",
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(body) ?? pulse ?? "man_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "man",
                Go: "man",
                Reason: "man_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "man",
                Go: "man",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildManArgs(CitizenIntentRouter.Route route)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        PutIfPresent(args, "tool", route.Tool
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "tool")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "name")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "page"));
        return args;
    }

    static bool TryReadManOk(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;

        var trimmed = body.TrimStart();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("ok", out var okEl))
                    return okEl.ValueKind != JsonValueKind.False;
                if (root.TryGetProperty("error", out var err)
                    && err.ValueKind == JsonValueKind.String
                    && err.GetString() is { Length: > 0 })
                    return false;
                return root.TryGetProperty("pulse", out _)
                    || root.TryGetProperty("text", out _)
                    || root.TryGetProperty("body", out _);
            }
            catch
            {
                return false;
            }
        }

        return trimmed.StartsWith("TOC:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("Manual:", StringComparison.OrdinalIgnoreCase)
            || trimmed.Length > 0;
    }

    static string? TryReadManPulse(string body, string? tool)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        var trimmed = body.TrimStart();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                    && p.GetString() is { Length: > 0 } pulse)
                    return TruncPulse(pulse);

                if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                    && err.GetString() is { Length: > 0 } e)
                    return TruncPulse("man · " + e);

                if (root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String
                    && t.GetString() is { Length: > 0 } text)
                    return TruncPulse(text);

                if (root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String
                    && b.GetString() is { Length: > 0 } bodyText)
                    return TruncPulse(bodyText);
            }
            catch
            {
                /* fall through to plain */
            }
        }

        var label = string.IsNullOrWhiteSpace(tool) ? "TOC" : tool.Trim();
        if (trimmed.StartsWith("TOC:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("Manual:", StringComparison.OrdinalIgnoreCase))
            return TruncPulse(trimmed);

        return TruncPulse("man · " + label + " · " + trimmed);
    }
}
