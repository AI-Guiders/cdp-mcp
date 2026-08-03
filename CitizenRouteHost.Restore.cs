#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent restore|recent — sync MetaDispatch cdp_restore/cdp_recent; place restore organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake restore/recent JSON; live uses MetaDispatchResolver.</summary>
    internal static Func<string, IReadOnlyDictionary<string, JsonElement>, string>? RestoreDispatchOverride { get; set; }

    static Applied RunRestoreRecent(CitizenIntentRouter.Route route)
    {
        var family = string.IsNullOrWhiteSpace(route.Organ) ? "restore" : route.Organ!;
        var op = string.IsNullOrWhiteSpace(route.Op)
            ? (family == "recent" ? "list" : "restore")
            : route.Op!;
        var tool = family == "recent" ? "cdp_recent" : "cdp_restore";
        var args = BuildRestoreArgs(route, family, op);

        try
        {
            string json;
            if (RestoreDispatchOverride is { } ov)
            {
                json = ov(tool, args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                json = meta(tool, args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadLifecycleOk(json) || TryReadRestoreOk(json, family);
            var pulse = TryReadRestorePulse(json, family, op);
            var go = family == "recent" ? "recent" : "restore";
            var seat = IdeDeskSeats.PlaceOrgan("restore");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "restore",
                Seat: seat,
                Go: go,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "restore_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "restore",
                Go: family == "recent" ? "recent" : "restore",
                Reason: "restore_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "restore",
                Go: family == "recent" ? "recent" : "restore",
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildRestoreArgs(
        CitizenIntentRouter.Route route, string family, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (family == "restore")
            args["op"] = JsonSerializer.SerializeToElement(op);
        else
            PutIntIfPresent(args, "take", route.Detail
                ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "take"));
        return args;
    }

    static bool TryReadRestoreOk(string json, string family)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var okEl))
                return okEl.ValueKind != JsonValueKind.False;
            if (root.TryGetProperty("error", out var err)
                && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 })
                return false;
            if (family == "recent")
                return root.TryGetProperty("items", out _) || root.TryGetProperty("count", out _);
            return root.TryGetProperty("op", out _) || root.TryGetProperty("path", out _)
                || root.TryGetProperty("schema", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadRestorePulse(string json, string family, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse(pulse);

            var bits = new List<string> { family, op };
            if (root.TryGetProperty("ok", out var okEl))
                bits.Add(okEl.ValueKind == JsonValueKind.True ? "ok" : "fail");
            if (root.TryGetProperty("count", out var c)
                && c.ValueKind is JsonValueKind.Number)
                bits.Add("n=" + c.GetRawText());
            else if (root.TryGetProperty("buffer_count", out var bc)
                && bc.ValueKind is JsonValueKind.Number)
                bits.Add("buffers=" + bc.GetRawText());
            else if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                bits.Add(e);

            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse(family + " " + op);
        }
    }
}
