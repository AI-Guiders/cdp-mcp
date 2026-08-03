#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent ps1 — sync Ps1Scene.DispatchAsync; place ps1_scene organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake ps1 JSON; live uses Ps1Scene.DispatchAsync.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? Ps1DispatchOverride { get; set; }

    static Applied RunPs1(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var args = BuildPs1Args(route.Raw, op);

        try
        {
            string json;
            if (Ps1DispatchOverride is { } ov)
            {
                json = ov(args);
            }
            else
            {
                var store = IdeLanguageTools.TryGetDocumentStore();
                if (store is null)
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "ps1",
                        Go: "ps1_scene",
                        Path: route.Path,
                        Reason: "doc_store_unbound");
                }

                var session = SessionResolver?.Invoke();
                if (session is null)
                {
                    return new Applied(
                        route.Raw,
                        route.Verb.ToString(),
                        Ok: false,
                        Action: "ps1",
                        Go: "ps1_scene",
                        Path: route.Path,
                        Reason: "no_session");
                }

                var byDomain = ByDomainResolver?.Invoke()
                    ?? new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal);

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                json = Ps1Scene.DispatchAsync(store, session, byDomain, args, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadLifecycleOk(json);
            var pulse = TryReadPs1Pulse(json, op);
            string? full = null;
            string? docId = null;
            TryReadPs1Path(json, out full, out docId);
            var seat = IdeDeskSeats.PlaceOrgan("ps1_scene");
            if (full is { Length: > 0 } && op is "put" or "open")
                PublishGlassLandOpen(full);
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "ps1",
                Seat: seat,
                Go: "ps1_scene",
                Path: full ?? route.Path,
                DocId: docId,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "ps1_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "ps1",
                Go: "ps1_scene",
                Path: route.Path,
                Reason: "ps1_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "ps1",
                Go: "ps1_scene",
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildPs1Args(string raw, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        if (raw.Contains("dry_run", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("dryrun", StringComparison.OrdinalIgnoreCase)
            || string.Equals(CitizenIntentRouter.ExtractKeyedValue(raw, "mode"), "dry_run", StringComparison.OrdinalIgnoreCase))
            args["mode"] = JsonSerializer.SerializeToElement("dry_run");

        PutIfPresent(args, "path",
            CitizenIntentRouter.ExtractKeyedValue(raw, "path"));
        PutIfPresent(args, "name",
            CitizenIntentRouter.ExtractKeyedValue(raw, "name")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "file"));
        PutIfPresent(args, "text",
            CitizenIntentRouter.ExtractKeyedValue(raw, "text")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "body")
            ?? CitizenIntentRouter.ExtractKeyedValue(raw, "code"));
        PutIfPresent(args, "mode", CitizenIntentRouter.ExtractKeyedValue(raw, "mode"));
        PutIfPresent(args, "overwrite", CitizenIntentRouter.ExtractKeyedValue(raw, "overwrite"));

        return args;
    }

    static void TryReadPs1Path(string json, out string? path, out string? docId)
    {
        path = null;
        docId = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String)
                path = p.GetString();
            if (root.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object
                && meta.TryGetProperty("doc_id", out var d) && d.ValueKind == JsonValueKind.String)
                docId = d.GetString();
            else if (root.TryGetProperty("doc_id", out var docEl) && docEl.ValueKind == JsonValueKind.String)
                docId = docEl.GetString();
        }
        catch
        {
            /* best-effort */
        }
    }

    static string? TryReadPs1Pulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String)
                return TruncPulse(p.GetString());

            var bits = new List<string> { "ps1", op };
            if (root.TryGetProperty("ok", out var okEl))
                bits.Add(okEl.ValueKind == JsonValueKind.True ? "ok" : "fail");
            if (root.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String
                && path.GetString() is { Length: > 0 } full)
                bits.Add(Path.GetFileName(full));
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                bits.Add(e);
            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse("ps1 " + op);
        }
    }
}
