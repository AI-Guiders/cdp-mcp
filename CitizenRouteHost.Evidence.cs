#nullable enable
using System.Text.Json;

namespace CdpMcp;

/// <summary>Citizen @intent evidence — sync MetaDispatch cdp_evidence; place report organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake evidence JSON; live uses MetaDispatchResolver("cdp_evidence", …).</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? EvidenceDispatchOverride { get; set; }

    static Applied RunEvidence(CitizenIntentRouter.Route route)
    {
        var kind = string.IsNullOrWhiteSpace(route.Op) ? "auto" : route.Op!;
        var args = BuildEvidenceArgs(route, kind);

        try
        {
            string json;
            if (EvidenceDispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                json = meta("cdp_evidence", args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadEvidenceOk(json);
            var pulse = TryReadEvidencePulse(json, kind);
            var seat = IdeDeskSeats.PlaceOrgan("report");
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "evidence",
                Seat: seat,
                Go: "report",
                Path: route.Path,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "evidence_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "evidence",
                Go: "report",
                Path: route.Path,
                Reason: "evidence_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "evidence",
                Go: "report",
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildEvidenceArgs(
        CitizenIntentRouter.Route route, string kind)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["kind"] = JsonSerializer.SerializeToElement(kind)
        };

        PutIfPresent(args, "text", route.Tool
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "text")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "body")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "log"));
        PutIfPresent(args, "path", route.Path
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "path")
            ?? CitizenIntentRouter.ExtractKeyedValue(route.Raw, "file"));

        return args;
    }

    static bool TryReadEvidenceOk(string json)
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
            return root.TryGetProperty("schema", out _)
                || root.TryGetProperty("itemCount", out _)
                || root.TryGetProperty("items", out _);
        }
        catch
        {
            return false;
        }
    }

    static string? TryReadEvidencePulse(string json, string kind)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String
                && p.GetString() is { Length: > 0 } pulse)
                return TruncPulse("evidence " + kind + " " + pulse);

            var bits = new List<string> { "evidence", kind };
            if (root.TryGetProperty("ok", out var okEl))
                bits.Add(okEl.ValueKind == JsonValueKind.True ? "ok" : "fail");
            if (root.TryGetProperty("itemCount", out var count) && count.ValueKind == JsonValueKind.Number)
                bits.Add("n=" + count.GetInt32());
            else if (root.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.String
                && src.GetString() is { Length: > 0 } s)
                bits.Add(s);
            else if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                bits.Add(e);

            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse("evidence " + kind);
        }
    }
}
