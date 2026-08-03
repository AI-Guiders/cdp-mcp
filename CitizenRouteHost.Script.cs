#nullable enable
using System.Text.Json;
using Cdp.Core;

namespace CdpMcp;

/// <summary>Citizen @intent script — sync ScriptScene.DispatchAsync; place script organ.</summary>
internal static partial class CitizenRouteHost
{
    /// <summary>Tests: inject fake script JSON; live uses ScriptScene.DispatchAsync.</summary>
    internal static Func<IReadOnlyDictionary<string, JsonElement>, string>? ScriptDispatchOverride { get; set; }

    /// <summary>Live: meta tool dispatch for script run → cdp_csx_run (Program.DispatchAsync).</summary>
    internal static Func<string, IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<string>>? MetaDispatchResolver { get; set; }

    static Applied RunScript(CitizenIntentRouter.Route route)
    {
        var op = string.IsNullOrWhiteSpace(route.Op) ? "scene" : route.Op!;
        var args = BuildScriptArgs(route.Raw, op);

        try
        {
            string json;
            if (ScriptDispatchOverride is { } ov)
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
                        Action: "script",
                        Go: "script",
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
                        Action: "script",
                        Go: "script",
                        Path: route.Path,
                        Reason: "no_session");
                }

                var byDomain = ByDomainResolver?.Invoke()
                    ?? new Dictionary<string, ICdpBackendModule>(StringComparer.Ordinal);
                var meta = MetaDispatchResolver
                    ?? ((_, _, _) => Task.FromResult("""{"ok":false,"error":"meta_dispatch_unbound"}"""));

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                json = ScriptScene.DispatchAsync(store, session, byDomain, args, meta, cts.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }

            var ok = TryReadLifecycleOk(json);
            var pulse = TryReadScriptPulse(json, op);
            string? full = null;
            string? docId = null;
            TryReadScriptPath(json, out full, out docId);
            var seat = IdeDeskSeats.PlaceOrgan("script");
            if (full is { Length: > 0 } && op is "put" or "open")
                PublishGlassLandOpen(full);
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: ok,
                Action: "script",
                Seat: seat,
                Go: "script",
                Path: full ?? route.Path,
                DocId: docId,
                Pulse: pulse,
                Reason: ok ? null : (TryReadLifecycleError(json) ?? pulse ?? "script_failed"));
        }
        catch (OperationCanceledException)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "script",
                Go: "script",
                Path: route.Path,
                Reason: "script_timeout");
        }
        catch (Exception ex)
        {
            return new Applied(
                route.Raw,
                route.Verb.ToString(),
                Ok: false,
                Action: "script",
                Go: "script",
                Path: route.Path,
                Reason: ex.GetType().Name + ": " + ex.Message);
        }
    }

    static Dictionary<string, JsonElement> BuildScriptArgs(string raw, string op)
    {
        var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["op"] = JsonSerializer.SerializeToElement(op)
        };

        // dry_run head → mode for ScriptScene.Run
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
        PutIfPresent(args, "refresh", CitizenIntentRouter.ExtractKeyedValue(raw, "refresh"));
        PutIfPresent(args, "symbol", CitizenIntentRouter.ExtractKeyedValue(raw, "symbol"));
        PutIfPresent(args, "topic", CitizenIntentRouter.ExtractKeyedValue(raw, "topic"));

        return args;
    }

    static void TryReadScriptPath(string json, out string? path, out string? docId)
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

    static string? TryReadScriptPulse(string json, string op)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("pulse", out var p) && p.ValueKind == JsonValueKind.String)
                return TruncPulse(p.GetString());

            var bits = new List<string> { "script", op };
            if (root.TryGetProperty("ok", out var okEl))
                bits.Add(okEl.ValueKind == JsonValueKind.True ? "ok" : "fail");
            if (root.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String
                && path.GetString() is { Length: > 0 } full)
                bits.Add(Path.GetFileName(full));
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String
                && err.GetString() is { Length: > 0 } e)
                bits.Add(e);
            if (root.TryGetProperty("mode", out var mode) && mode.ValueKind == JsonValueKind.String
                && mode.GetString() is { Length: > 0 } m)
                bits.Add(m);
            return TruncPulse(string.Join(' ', bits));
        }
        catch
        {
            return TruncPulse("script " + op);
        }
    }
}
