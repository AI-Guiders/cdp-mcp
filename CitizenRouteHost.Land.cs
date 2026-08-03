#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent land — sync MetaDispatch cdp_land / NavigationLand; place land organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake land JSON; live uses MetaDispatchResolver("cdp_land", …).</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? LandDispatchOverride { get; set; }

    static Applied RunLand(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "restore" : route.Op!;
        var wire = route.Command;
        if (string.IsNullOrWhiteSpace(wire))
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "land",
                Go: "land",
                Path: route.Path,
                Reason: "land_anchor_required");
        }

        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["anchor"] = JsonSerializer.SerializeToElement(wire)
        };

        try
        {
            string json;
            if (LandDispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                json = meta("cdp_land", args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadLandOk(json);
            var pulse = TryReadLandPulse(json, op, route.Path);
            string? full = null;
            string? docId = null;
            TryReadLandPath(json, out full, out docId);
            var seat = IdeDeskSeats.PlaceOrgan("land");
            if (full is { Length: > 0 } && op is "open" or "goto")
                PublishGlassLandOpen(full);
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "land",
                Seat: seat,
                Go: "land",
                Path: full ?? route.Path,
                DocId: docId,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "land_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "land",
                Go: "land",
                Path: route.Path,
                Reason: "land_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "land",
                Go: "land",
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static bool TryReadLandOk(string json)
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
            return root.TryGetProperty("command", out _) || root.TryGetProperty("schema", out _);
        }
        catch
        {
            return false;
        }
    }

    static void TryReadLandPath(string json, out string? path, out string? docId)
    {
        path = null;
        docId = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
            {
                if (result.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String)
                    path = p.GetString();
                if (result.TryGetProperty("doc_id", out var d) && d.ValueKind == JsonValueKind.String)
                    docId = d.GetString();
            }

            if (path is null && root.TryGetProperty("path", out var rootPath) && rootPath.ValueKind == JsonValueKind.String)
                path = rootPath.GetString();
        }
        catch
        {
            /* best-effort */
        }
    }

    static string? TryReadLandPulse(string json, string op, string? path)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse(pulse);

            var bits = new List<string> { "land", op };
            if (root.TryGetProperty("ok", out var okEl))
                bits.Add(okEl.ValueKind == JsonValueKind.True ? "ok" : "fail");
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                bits.Add(e);
            else if (root.TryGetProperty("command", out var cmd) && cmd.ValueKind == JsonValueKind.String
                && cmd.GetString() is { Length: > 0 } c)
                bits.Add(c);

            if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("path", out var rp) && rp.ValueKind == JsonValueKind.String
                && rp.GetString() is { Length: > 0 } full)
                bits.Add(Path.GetFileName(full));
            else if (path is { Length: > 0 })
                bits.Add(Path.GetFileName(path));

            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse("land " + op);
        }
    }
}
